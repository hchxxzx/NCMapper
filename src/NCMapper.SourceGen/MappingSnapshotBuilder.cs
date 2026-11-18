using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

/// <summary>
/// Fluent 通道完整物化器（step 3 / S-4a）
/// 把解析期 <see cref="FluentSnapshot"/> 物化为完整 <see cref="MappingSnapshot"/>，
/// 复用 <see cref="MappingExtractor.Materialize"/> 既有符号遍历
///（AllTargetProperties/SourceProperties/DeepCopyNodes/ComposeMappings/Diagnostics 全字段）。
/// <para>属性遍历语义（与 [MapFrom] 通道对齐处与分叉处）：</para>
/// <list type="bullet">
/// <item>对齐：继承层次遍历 + GroupBy 最派生优先；public setter 门；按名自动匹配（OrdinalIgnoreCase）
/// + 自动扁平化兜底；NCMAP001/NCMAP002 自动路径诊断（受诊断选项过滤，与 [MapFrom] 同口径）；
/// NCMAP011 non-public 跳过生成。</list>
/// <item>分叉（Fluent 特有， 特性在 Fluent 通道不生效）：显式声明优先于按名匹配
/// 声明的属性不做自动匹配（含转换失败场景，防静默覆盖显式意图）；Ignore 跳过属性
///（[MapIgnore(Both)] 同位）；NoAutoInclude 时未声明属性不匹配不入候选；
/// 显式 Map/Convert 声明不发 NCMAP002（声明式跨类型意图，由内建 ConvertValue 转换承接）；
/// 不读取 [MapProperty]/[MapIgnore]/[Mask]/[Convert] 等属性级特性。</list>
/// </list>
/// </summary>
internal static partial class MappingSnapshotBuilder
{
    /// <summary>
    /// 完整物化 FluentSnapshot → MappingSnapshot（S-4a：物化复用 MappingExtractor.Materialize）
    /// </summary>
    /// <param name="fs">解析期单链快照（符号态）</param>
    /// <param name="classSyntax">[MapperConfig] 类声明语法（TargetSyntax/NCMAP099 定位）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>完整物化快照；符号不可用时为 null</returns>
    public static MappingSnapshot? Build(FluentSnapshot fs, TypeDeclarationSyntax? classSyntax, CancellationToken ct)
    {
        var info = ExtractInfo(fs, classSyntax, ct);

        // NCMAP011 镜像：non-public 类型跳过源生成（判定内核单点——B3-10 组1；与 [MapFrom] 通道同口径——
        // 薄壳差异仅 Location 口径：Fluent 侧各类型自身位置，特性侧恒 DTO 标识位置）
        var nonPublicSkip = MappingExtractor.TryBuildNonPublicSkipDiagnostic(fs.SourceType, fs.SourceType.Locations.FirstOrDefault())
            ?? MappingExtractor.TryBuildNonPublicSkipDiagnostic(fs.TargetType, fs.TargetType.Locations.FirstOrDefault());
        if (nonPublicSkip != null)
        {
            info.SkipCodeGeneration = true;
            info.Diagnostics.Add(nonPublicSkip);
            return MappingExtractor.Materialize(info);
        }

        // 诊断开关解析（[NCMapperOptions] 类级 > 程序集级 > 默认——报告配置非映射意图，两通道共用）
        info.DiagnosticOptions = DiagnosticOptionsResolver.Resolve(fs.TargetType);


        BuildPropertyMappings(fs, info, ct);

        return FinalizeSnapshot(fs, info);

        // 提取/综合/合成三段（分段设计；零语义重组：局部函数捕获原作用域，
        //    语句顺序/条件/表达式零改动—— S3）
        static MappingInfo ExtractInfo(FluentSnapshot fs, TypeDeclarationSyntax? classSyntax, CancellationToken ct)
        {
            var info = new MappingInfo
            {
                SourceType = fs.SourceType,
                TargetType = fs.TargetType,
                SourceClassName = fs.SourceType.Name,
                TargetClassName = fs.TargetType.Name,
                TargetSyntax = classSyntax,
                // Fluent additive 两字段（[MapFrom] 通道取默认值——其 NoAutoInclude 自
                // 起由 [MapNoAutoMatch] 提取链填充，非本桥接路径；AfterMappingMethodRef 双侧共用）
                NoAutoInclude = fs.NoAutoInclude,
                AfterMappingMethodRef = fs.AfterMappingMethodRef,
                // 前置生命周期钩子桥接透传（镜像 AfterMappingMethodRef
                // 遗漏点 #1 同型防线；漏加即 Fluent 通道静默丢字段）
                BeforeMappingMethodRef = fs.BeforeMappingMethodRef,
                // Fluent 树配置/属性级 NoAutoInclude（[MapFrom] 通道恒默认值）
                TreeConfig = fs.TreeConfig,
                PropertyNoAutoIncludeTargets = fs.NoAutoIncludeTargets.ToList(),
                // Compose 声明面标记（降级判定输入——FromSource 属性无 pm 明细，
                // 运行时判不可翻译的唯一可见信号；[MapFrom] 通道恒默认 false）
                HasComposeMappings = fs.ComposeDeclarations.Count > 0,
                // （消费补齐）：Fluent 正向方向标记——For<S,T> 声明
                // S=Entity、T=DTO（dest 恒指 TTarget 契约），正向映射写入 DTO 侧。特性通道方向推导
                // 依赖目标 [MapFrom]（IsToDtoMapping），Fluent 声明无该特性——物化恒补 true 供
                // 生成端方向过滤（ShouldSkipForDirectionPair）正确判定；反向快照（ReversePlanner
                // 全新构造）不受影响恒 false
                IsFluentForward = true,
                // For 调用点位置桥接（NCMAP025 Fluent 侧定位
                // 互指诊断指向 For invocation；物化期转纯值 LocationSnapshot）
                ForLocation = fs.ForLocation,
            };

            // 多态子类型对物化透传
            // 边界拦截 + FQN 二元组去重查表：
            // ① TSrcDerived == TSource（登记基类自身， 边界 → 诊断）必须拦截
            //    否则发射分派链 `if (src is TSource) return Map_TSource_T(...)` 自引用死循环
            //    （正确性防护先行；诊断已在本拦截点接线——见下 DiagnosticDescriptorsFluent.SubclassDuplicateOrSelfRule 发射）；
            // ② 完全同对 (TSrcDerived, TTargetDerived) 重复登记 → 保留首条（首见即登记语义；
            //    F47 重复登记诊断已接线——见下 SubclassDuplicateOrSelfRule 发射）；
            // ③ 部分重叠（同源异层/同源异目标）合法保留（发射端按最派生优先仲裁，不报）；
            //    TTargetDerived == TTarget（目标收敛）为合法用例不拦截。
            // [MapFrom] 通道恒空（特性侧 [MapSubclass] 由 特性扫描链注入）——快照默认空集合，
            // 发射端零分派产物（G3 字节门前提）。
            if (fs.SubclassDeclarations.Count > 0)
            {
                var seen = new HashSet<(string, string)>(); // 元组默认比较器 = 逐元素序数（string 默认序数比较，等价 Ordinal）
                foreach (var sd in fs.SubclassDeclarations)
                {
                    // ① 基类自身登记拦截（F47-②， 接线——泛型约束允许
                    //    TSrcDerived == TSource（自身满足 where），分派自引用死循环必须拦截）
                    if (SymbolEqualityComparer.Default.Equals(sd.SourceType, fs.SourceType))
                    {
                        info.Diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.SubclassDuplicateOrSelfRule,
                            Args = new object[]
                            {
                                fs.SourceType.ToDisplayString(), fs.TargetType.ToDisplayString(), "base-self",
                            },
                            Location = sd.Location,
                        });
                        continue;
                    }
                    var key = (sd.SourceType.ToDisplayString(), sd.TargetType.ToDisplayString());
                    if (seen.Add(key)) // ② 完全同对去重
                    {
                        info.SubclassPairs.Add(new SubclassPairMapping
                        {
                            SourceType = sd.SourceType,
                            TargetType = sd.TargetType,
                            Location = sd.Location,
                        });
                    }
                    else
                    {
                        // ② 完全同对重复登记（F47-①， 接线——部分重叠合法不报）
                        info.Diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.SubclassDuplicateOrSelfRule,
                            Args = new object[]
                            {
                                sd.SourceType.ToDisplayString(), sd.TargetType.ToDisplayString(), "duplicate-pair",
                            },
                            Location = sd.Location,
                        });
                    }
                }
                // ④ 最派生优先排序（仲裁——发射端按此序生成 isinst 顺序链）：
                // （源侧继承距离，目标侧继承距离）字典序降序——距离大 = 更派生 = 先匹配
                //（登记 Dog 与 Puppy : Dog 同登 → Puppy 先；继承图编译期静态可判定无环）。
                // 符号态排序（BaseType 链步数），物化后快照 SubclassPairs 顺序即分派链顺序。
                info.SubclassPairs.Sort((a, b) =>
                {
                    var sd = InheritanceDistance(a.SourceType, fs.SourceType);
                    var bd = InheritanceDistance(b.SourceType, fs.SourceType);
                    if (sd != bd) return bd.CompareTo(sd);
                    var ta = InheritanceDistance(a.TargetType, fs.TargetType);
                    var tb = InheritanceDistance(b.TargetType, fs.TargetType);
                    return tb.CompareTo(ta);
                });
            }

            // MapCtorParam 绑定校验与建表
            // ① 子规则：.Map 显式条目（fs.Declarations Kind==Map）与 MapCtorParam 指向同一 ctor 形参
            //    （record 同形参场景）→ F53 fail-fast（对齐 F39 双声明/NCMAP_F23 同锚矛盾纪律，报错条目不建表）；
            // ② 同一形参重复绑定 → F53（首条生效 + error 可见，F29 报错条目跳过同款口径）；
            // ③：绑定形参名不存在于任何公共构造器参数集 → F52（提示可用参数集；ByRef/指针形参
            //    跳过比对；OrdinalIgnoreCase 与 BuildConstructorArgs 同口径）；
            // 合法条目建 (形参名→源属性名) OrdinalIgnoreCase 表传 Materialize（ConstructorParameterRef.
            // SourcePropertyName 承载——目标类型顶层物化 + DeepCopy 目标节点双点填充）。
            // [MapFrom] 通道 CtorParamBindings 恒 null（fs.CtorParamBindings 空）——B9 字节门前提。
            if (fs.CtorParamBindings.Count > 0)
            {
                var bindingTable = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                var seenParams = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                // .Map 显式条目名集（OrdinalIgnoreCase——构造器形参匹配全局口径；fs.Declarations 键为
                // Ordinal 声明名，形参名大小写分叉场景（.Map(d=>d.Nick) + .MapCtorParam("nick")）须同判）
                var mapDeclNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var kv in fs.Declarations)
                {
                    if (kv.Value.Kind == FluentDeclarationKind.Map) mapDeclNames.Add(kv.Key);
                }
                // 可用参数集（ByRef/指针形参不可绑定，跳过比对与提示）
                var availableNames = new List<string>();
                foreach (var c in fs.TargetType.InstanceConstructors)
                {
                    if (c.DeclaredAccessibility != Accessibility.Public) continue;
                    foreach (var p in c.Parameters)
                    {
                        if (p.RefKind != RefKind.None
                            || p.Type.TypeKind == TypeKind.Pointer
                            || p.Type.TypeKind == TypeKind.FunctionPointer) continue;
                        if (!availableNames.Contains(p.Name, System.StringComparer.OrdinalIgnoreCase))
                            availableNames.Add(p.Name);
                    }
                }
                foreach (var binding in fs.CtorParamBindings)
                {
                    // 子规则：.Map 显式条目同形参 → F53（map-conflict）——fail-fast 非静默覆盖
                    if (mapDeclNames.Contains(binding.ParamName))
                    {
                        info.Diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.CtorParamDuplicateOrConflictRule,
                            Args = new object[] { binding.ParamName, fs.TargetType.ToDisplayString(), "map-conflict" },
                            Location = binding.Location,
                        });
                        continue;
                    }
                    // 同一形参重复绑定 → F53（duplicate）
                    if (!seenParams.Add(binding.ParamName))
                    {
                        info.Diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.CtorParamDuplicateOrConflictRule,
                            Args = new object[] { binding.ParamName, fs.TargetType.ToDisplayString(), "duplicate" },
                            Location = binding.Location,
                        });
                        continue;
                    }
                    // 形参名不在任何公共构造器参数集 → F52（提示可用参数集）
                    if (!availableNames.Contains(binding.ParamName, System.StringComparer.OrdinalIgnoreCase))
                    {
                        info.Diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.CtorParamParamNotFoundRule,
                            Args = new object[] { binding.ParamName, fs.TargetType.ToDisplayString(),
                                string.Join(", ", availableNames) },
                            Location = binding.Location,
                        });
                        continue;
                    }
                    bindingTable[binding.ParamName] = binding.SourceProperty.Name;
                }
                if (bindingTable.Count > 0) info.CtorParamBindings = bindingTable;
            }

            // 跨对传播——目标 DTO 类型级钩子特性回读注入（注入点 定案：
            // 候选② MappingSnapshotBuilder 物化期，fs.TargetType 符号在手直读——钩子随 DTO
            // target-level、按签名适用逐对生效）。[MapFrom] 对由特性通道发现管线 ExtractLifecycleHooks
            // 直读填充（不经本方法）；纯 Fluent 对在此注入。签名不适用不注入、全程零诊断
            // （声明所在对 NCMAP038~040 已由特性通道产出——[MapFrom] 共存 DTO；纯 Fluent 场景为
            // D-d 静默路径）；Fluent 显式声明值优先（非空不覆盖，双通道重复声明由 仲裁抑制）。
            PropagateTargetLevelHooks(fs, info);

            return info;
        }

        static void BuildPropertyMappings(FluentSnapshot fs, MappingInfo info, CancellationToken ct)
        {
            // 属性遍历（收敛至 EnumerateWritableTargetProperties 共享门，
            // onNonWritable=null 静默跳过——Fluent 通道不读特性级 [MapIgnore]，NCMAP006 分叉保留参数化）
            foreach (var prop in MappingExtractor.EnumerateWritableTargetProperties(fs.TargetType, ct))
            {

                // ConvertBack 反向声明消费（物化三分支）
                // 物化期零重复解析（B-1 提取期单点：实体侧 FindProperty 与 get-only 门都在
                // FluentExtractor.ResolveConvertBackMethod；此处只读 backDecl.EntityProperty）
                var backDecl = fs.BackDeclarations.TryGetValue(prop.Name, out var bd) ? bd : null;

                // Convert 正向声明消费（附着模式——提取期 F24 保证源可达；
                // 随终端更名 ConvertUsing→Convert）
                var forwardDecl = fs.ForwardDeclarations.TryGetValue(prop.Name, out var fd) ? fd : null;

                // Compose 多源组合命中判定（显式跨源优先
                // 与特性侧"FromSource ≠ 主源 → ProcessComposeMapping"同位）。
                // 命中属性不参与主源自动匹配、不进 Declarations 分派（显式声明优先于按名匹配）。
                var composeDecls = fs.ComposeDeclarations.Count > 0
                    ? fs.ComposeDeclarations
                        .Where(cd => cd.TargetPropertyName == prop.Name && IsEffectiveComposeDeclaration(cd))
                        .ToList()
                    : null;
                if (composeDecls != null && composeDecls.Count > 0)
                {
                    // F29 三表条件（_004）：Compose 命中 && 主链三表任一含该 dest → Error。
                    // 附着面无 pm 锚点将静默丢，须 fail-fast。
                    if (fs.Declarations.ContainsKey(prop.Name)
                        || fs.ForwardDeclarations.ContainsKey(prop.Name)
                        || fs.BackDeclarations.ContainsKey(prop.Name))
                    {
                        info.Diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.ComposeConflictRule,
                            Args = new object[] { prop.Name, fs.TargetType.ToDisplayString() },
                            Location = composeDecls[0].Location,
                        });
                        continue; // fail-fast：跳过此属性
                    }

                    // 同 dest 四组合（_004）：按子源分组判定
                    var f29Emitted = false;
                    var subSourceGroups = composeDecls
                        .GroupBy(cd => cd.SubSourceType, (IEqualityComparer<INamedTypeSymbol>)SymbolEqualityComparer.Default)
                        .ToList();
                    foreach (var group in subSourceGroups)
                    {
                        // F29 独占计数口径：仅"路径承载型"声明（速记/块内 Map
                        // 定义源路径从哪来）计入多绑定矛盾；Mask/FromJson 为修饰型（附着语义，P1-4 合并对象），
                        // 与 Convert 同构不入计数（同组 Map×Convert Q9 附着合并先例）。
                        // /D4 连带（用户拍板本册连带修）：Format/MapTree 亦为
                        // 修饰型/递归型（新面块内 From+Format/MapTree 组合此前被误计双绑定 → F29 误拒；
                        // 块内 MapTree SourcePath 恒 null 按名解析——消费流 binding 选择与
                        // 全 Ignore 判定 独立于本计数，不受影响）
                        var bindings = group.Where(cd => cd.Converter == null
                            && cd.BlockKind != FluentDeclarationKind.Ignore
                            && cd.BlockKind != FluentDeclarationKind.Mask
                            && cd.BlockKind != FluentDeclarationKind.FromJson
                            && cd.BlockKind != FluentDeclarationKind.Format
                            && cd.BlockKind != FluentDeclarationKind.MapTree).ToList();
                        var converts = group.Where(cd => cd.Converter != null).ToList();
                        // 同子源多绑定（速记×速记/速记×块/块×块双绑定）→ F29
                        if (bindings.Count > 1)
                        {
                            info.Diagnostics.Add(new DiagnosticInfo
                            {
                                Descriptor = DiagnosticDescriptorsFluent.ComposeConflictRule,
                                Args = new object[] { prop.Name, fs.TargetType.ToDisplayString() },
                                Location = bindings[1].Location,
                            });
                            f29Emitted = true;
                        }
                        // 同子源多 Convert → F29
                        if (converts.Count > 1)
                        {
                            info.Diagnostics.Add(new DiagnosticInfo
                            {
                                Descriptor = DiagnosticDescriptorsFluent.ComposeConflictRule,
                                Args = new object[] { prop.Name, fs.TargetType.ToDisplayString() },
                                Location = converts[1].Location,
                            });
                            f29Emitted = true;
                        }
                        // bindings==1 && converts==1 → 放行（块内 Map×Convert 互补，Q9 附着合并）
                        // bindings==1/converts==0 → 放行；bindings==0/converts==1 → 放行（ForwardOnly 等价）
                        // bindings==0/converts==0 → 全 Ignore（属性在 Compose 上下文中忽略）
                    }
                    if (f29Emitted) continue;

                    // ComposeMappings 填充（逐子源组）——先收集全部候选，再按"是否实际产 cm"
                    // 决定是否入 AllTargetProperties（全 Ignore 组不产 cm → 不入候选集，
                    // 与主链 Ignore continue 不入候选集语义一致，防补匹配误从主源映射）
                    var composedMappings = new List<ComposePropertyMapping>();
                    foreach (var group in subSourceGroups)
                    {
                        var groupDecls = group.ToList();
                        // binding 只供 SourcePath：
                        // 优先取带路径声明（速记/块内 Map——显式点路径），盲选 FirstOrDefault 会让
                        // 同组 Map+Mask 双声明行为依赖声明顺序（Map 先入 → MaskType 丢失；Mask 先入 → 路径丢失）。
                        // Mask/FromJson/MapTree 无路径 → fallback 按名解析。
                        var binding = groupDecls.FirstOrDefault(cd => cd.Converter == null
                            && cd.BlockKind != FluentDeclarationKind.Ignore
                            && cd.SourcePath is { Count: > 0 })
                            ?? groupDecls.FirstOrDefault(cd => cd.Converter == null
                            && cd.BlockKind != FluentDeclarationKind.Ignore);
                        var convert = groupDecls.FirstOrDefault(cd => cd.Converter != null);

                        // 全 Ignore 组：该子源不产生 ComposeMapping（属性在 Compose 上下文中被忽略）
                        if (binding == null && convert == null) continue;

                        var cm = new ComposePropertyMapping
                        {
                            SubSourceType = group.Key,
                            TargetProperty = prop,
                            SourceIndexes = new List<int?>(), // Fluent 通道恒空（不支持索引器）
                        };

                        // SourcePath 归一（对照 MappingExtractor.ProcessComposeMapping）
                        if (binding != null && binding.SourcePath != null && binding.SourcePath.Count > 0)
                        {
                            // 显式路径（速记 src / 块内 Map 携带路径）
                            cm.SourcePath = binding.SourcePath;
                            cm.SourceProperty = binding.SourcePath[binding.SourcePath.Count - 1];
                        }
                        else if (binding != null && binding.SourcePath == null)
                        {
                            // 块内 MapTree/Mask/FromJson/Ignore：按名解析子源侧
                            var subSourceProp = MappingExtractor.FindProperty(group.Key, prop.Name);
                            if (subSourceProp == null || subSourceProp.GetMethod == null)
                            {
                                // NCMAP005：跨源字段未找到（两通道共用 ComposeFieldNotFoundRule）
                                info.Diagnostics.Add(new DiagnosticInfo
                                {
                                    Descriptor = DiagnosticDescriptors.ComposeFieldNotFoundRule,
                                    Args = new object[] { prop.Name, group.Key.Name, prop.Name },
                                    Location = prop.Locations.FirstOrDefault(),
                                });
                                continue;
                            }
                            cm.SourcePath = new List<IPropertySymbol> { subSourceProp };
                            cm.SourceProperty = subSourceProp;
                        }
                        else // binding == null && convert != null
                        {
                            // Convert-only（ForwardOnly 等价）：源路径来自提取期 ResolveConvertMethod 解析产物
                            var convDecl = convert!.Converter!.Decl;
                            cm.SourcePath = convDecl.SourcePath;
                            cm.SourceProperty = convDecl.SourceProperty;
                        }

                        // Converter 三字段（块内 Convert 解析产物——对照 ProcessComposeMapping）。
                        // B 形态（内联转录）分路——生成类型限定名 + 生成方法名
                        //（FNV 含转录体）+ 方法体三字段（发射端 GenerateConverterMethods 消费）
                        if (convert != null)
                        {
                            var convDecl = convert.Converter!.Decl;
                            if (convDecl.Method != null)
                            {
                                cm.ConverterDeclaringType = convDecl.Method.ContainingType.ToDisplayString();
                                cm.ConverterMethodName = convDecl.Method.Name;
                            }
                            else
                            {
                                cm.ConverterDeclaringType = GeneratedMapperTypeName;
                                cm.ConverterMethodName = BuildGeneratedConverterMethodName(
                                    group.Key, fs.TargetType, prop.Name, convDecl.TranscribedBody!, isBack: false);
                                cm.ConverterMethodBody = convDecl.TranscribedBody;
                                cm.ConverterMethodParamTypes = convDecl.TranscribedParamTypes;
                                cm.ConverterMethodParamNames = convDecl.TranscribedParamNames;
                            }
                            cm.ConverterShape = convDecl.Shape;
                        }

                        // D-1 消费：全组遍历写
                        // Mask/FromJson 语义字段——修饰型声明与 binding 选择解耦，同 dest Map+Mask
                        // 双声明不再依赖顺序（Map 显式路径 + Mask 脱敏同时生效，特性侧 [Mask] 独立
                        // 读取合并到同一 cm 的镜像口径）。同组多 Mask/多 FromJson 超出范围，
                        // 后写者胜（与生成端单值字段消费一致），执行记录 登记。
                        foreach (var decl in groupDecls)
                        {
                            if (decl.BlockKind == FluentDeclarationKind.Mask && decl.MaskType != null)
                                cm.MaskType = decl.MaskType;
                            else if (decl.BlockKind == FluentDeclarationKind.FromJson)
                                cm.IsFromJson = true;
                            // 块内 Format 装配（F64/F65 校验后置位
                            // 镜像主链 BuildFormatMapping 判据；cm 源=块内子源属性，F65 判定基准）
                            else if (decl.BlockKind == FluentDeclarationKind.Format && decl.FormatString != null)
                            {
                                var fmtTargetOk = cm.TargetProperty.Type.SpecialType == SpecialType.System_String;
                                var fmtSourceOk = MappingExtractor.IsFormattableSourceSymbol(cm.SourceProperty.Type);
                                if (fmtTargetOk && fmtSourceOk)
                                    cm.FormatString = decl.FormatString;
                                else if (!fmtTargetOk)
                                    info.Diagnostics.Add(new DiagnosticInfo
                                    {
                                        Descriptor = DiagnosticDescriptorsFluent.FormatTargetNotStringRule,
                                        Args = new object[] { cm.TargetProperty.Name, cm.TargetProperty.Type.ToDisplayString() },
                                        Location = decl.Location,
                                    });
                                else
                                    info.Diagnostics.Add(new DiagnosticInfo
                                    {
                                        Descriptor = DiagnosticDescriptorsFluent.FormatSourceNotFormattableRule,
                                        Args = new object[] { cm.TargetProperty.Name, cm.SourceProperty.Type.ToDisplayString() },
                                        Location = decl.Location,
                                    });
                            }
                        }

                        composedMappings.Add(cm);
                    }

                    // 至少一组实际产 ComposeMapping 才入候选集（防补匹配误从主源映射被忽略属性）
                    if (composedMappings.Count > 0)
                    {
                        info.AllTargetProperties.Add(prop);
                        info.ComposeMappings.AddRange(composedMappings);
                    }

                    continue; // 跳过主链分派（显式跨源优先）
                }

                // F23 矛盾门（物化期——两表均 Builder 可达，Kind 判定与遍历顺序无关；镜像 F17）：
                // Ignore 使属性不入候选（无 pm 即无转换器附着），Convert 正向写永远不会执行——
                // fail-fast（Error 级：Fluent 同声明面内无歧义矛盾）；丢弃 forwardDecl，Ignore 照常
                if (forwardDecl != null
                    && fs.Declarations.TryGetValue(prop.Name, out var conflictFwdDecl)
                    && conflictFwdDecl.Kind == FluentDeclarationKind.Ignore)
                {
                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.FluentIgnoreConvertConflictRule,
                        Args = new object[] { prop.Name, fs.TargetType.ToDisplayString() },
                        Location = forwardDecl.Location,
                    });
                    forwardDecl = null; // 丢弃 forwardDecl，Ignore 照常
                }

                // F17 矛盾门（物化期—— 既有；两表均 Builder 可达）：
                // Ignore 使属性不入候选（无 pm 即无反向派生），ConvertBack 反向写永远不会执行——
                // fail-fast（Error 级：非 NCMAP022 镜像——Fluent 同声明面内无歧义矛盾）
                if (backDecl != null
                    && fs.Declarations.TryGetValue(prop.Name, out var conflictDecl)
                    && conflictDecl.Kind == FluentDeclarationKind.Ignore)
                {
                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.FluentIgnoreConvertBackConflictRule,
                        Args = new object[] { prop.Name, fs.TargetType.ToDisplayString() },
                        Location = backDecl.Location,
                    });
                    backDecl = null; // 丢弃 backDecl，Ignore 照常
                }

                if (fs.Declarations.TryGetValue(prop.Name, out var declared))
                {
                    // 显式声明分派（Fluent 通道特性不生效——声明即唯一意图来源）
                    // 维度合并：多槽声明（新面合法组合——来源槽+Mask/Format、
                    // 来源槽+FromJson/MapTree、Mask+Format、FromJson+MapTree）走组合构建；单占声明走
                    // 既有单分派（产物一致纪律——单占路径零行为变化，字节门 8 部分零 DIFF）
                    if (IsMultiSlotDeclaration(declared))
                    {
                        // F66 矛盾门（Format × Convert 同锚——镜像 case Format 既有门；清单⑥保持）
                        if (declared.FormatString != null && forwardDecl != null)
                        {
                            info.Diagnostics.Add(new DiagnosticInfo
                            {
                                Descriptor = DiagnosticDescriptorsFluent.FormatConvertConflictRule,
                                Args = new object[] { prop.Name },
                                Location = declared.Location,
                            });
                            forwardDecl = null;
                        }
                        var comboPm = BuildComposedFluentMapping(info, prop, declared, fs.SourceType);
                        if (declared.OverrideIgnore && comboPm != null) comboPm.FluentOverrideIgnore = true;
                        AttachForwardDeclaration(comboPm, forwardDecl, fs);
                        AttachBackDeclaration(comboPm, backDecl, fs);
                    }
                    else
                    switch (declared.Kind)
                    {
                        case FluentDeclarationKind.Ignore:
                            // 方向性忽略（搭车）：null 或 Both(0) = 完全跳过
                            //（[MapIgnore(Both)] 同位——既有语义零变化）；ToDto(1)/ToEntity(2) = 方向感知——
                            // 建 pm + FluentIgnoreDirection 值化（镜像 [MapIgnore(Direction)] 采集面，
                            // 正向 ShouldSkipForDirection / 反向 ShouldIgnoreInDirectionValue 自动消费）
                            if (declared.Direction is null or 0)
                            {
                                continue;
                            }
                            info.AllTargetProperties.Add(prop);
                            var dirPm = BuildAutoMatch(info, prop, fs.SourceType);
                            if (dirPm != null)
                            {
                                dirPm.FluentIgnoreDirection = declared.Direction;
                                AttachBackDeclaration(dirPm, backDecl, fs);
                            }
                            break;
                        case FluentDeclarationKind.Map:
                            // 匹配对注入①：显式声明产 pm 后附着（Map 与 ConvertBack/Convert
                            // 同名锚点配对——双向显式；Mask/MapTree 同附着）
                            var explicitPm = BuildExplicitPathMapping(info, prop, declared);
                            // Map 声明 overrideIgnore 豁免门（四终端之一）
                            if (declared.OverrideIgnore && explicitPm != null) explicitPm.FluentOverrideIgnore = true;
                            AttachForwardDeclaration(explicitPm, forwardDecl, fs);
                            AttachBackDeclaration(explicitPm, backDecl, fs);
                            break;
                        case FluentDeclarationKind.Mask:
                            var maskPm = BuildMaskMapping(info, prop, declared, fs.SourceType);
                            // Mask 声明 overrideIgnore 豁免门（四终端之一）
                            if (declared.OverrideIgnore && maskPm != null) maskPm.FluentOverrideIgnore = true;
                            AttachForwardDeclaration(maskPm, forwardDecl, fs);
                            AttachBackDeclaration(maskPm, backDecl, fs);
                            break;
                        case FluentDeclarationKind.Format:
                            // Format 装配（镜像 Mask case——F66 矛盾门 +
                            // overrideIgnore 豁免门 + 双附着）
                            // F66 矛盾门（镜像 F23 FluentIgnoreConvertConflict——Error 级 fail-fast；
                            // 丢弃 forwardDecl、Format 照常生成——语义重叠二选一，Format 为声明主面）
                            if (forwardDecl != null)
                            {
                                info.Diagnostics.Add(new DiagnosticInfo
                                {
                                    Descriptor = DiagnosticDescriptorsFluent.FormatConvertConflictRule,
                                    Args = new object[] { prop.Name },
                                    Location = declared.Location,
                                });
                                forwardDecl = null;
                            }
                            var formatPm = BuildFormatMapping(info, prop, declared, fs.SourceType);
                            if (declared.OverrideIgnore && formatPm != null) formatPm.FluentOverrideIgnore = true;
                            AttachForwardDeclaration(formatPm, forwardDecl, fs);
                            AttachBackDeclaration(formatPm, backDecl, fs);
                            break;
                        case FluentDeclarationKind.MapTree:
                            var treePm = BuildMapTreeMapping(info, prop, fs.SourceType);
                            AttachForwardDeclaration(treePm, forwardDecl, fs);
                            AttachBackDeclaration(treePm, backDecl, fs);
                            break;
                        case FluentDeclarationKind.FromJson:
                            // （搭车）：JSON 展开——BuildFromJsonMapping 源侧按名
                            // 匹配 + 类型门（镜像特性侧 [MapProperty(IsFromJson=true)] 采集），pm.IsFromJson 置位
                            var jsonPm = BuildFromJsonMapping(info, prop, fs.SourceType);
                            AttachForwardDeclaration(jsonPm, forwardDecl, fs);
                            AttachBackDeclaration(jsonPm, backDecl, fs);
                            break;
                    }
                }
                else if (forwardDecl != null)
                {
                    // Convert 显式声明（无 Map/Mask/MapTree/FromJson
                    // 正向声明时）——ForwardOnly 建 pm：源用提取期解析产物（同名/扁平化/反向查找统一，
                    // 不走 BuildAutoMatch——NCMAP001/NCMAP002 对显式转换声明不适用，特性侧 [Convert]
                    // 同口径）；NoAutoInclude 不抑制（显式声明优先——004 ReverseOnly 同构语义）
                    info.AllTargetProperties.Add(prop);
                    var fwdPm = BuildForwardDeclarationMapping(info, prop, forwardDecl, fs);
                    AttachBackDeclaration(fwdPm, backDecl, fs);
                }
                else if (!fs.NoAutoInclude)
                {
                    // 自动匹配（默认开——S-3a：与 [MapFrom] 通道一致，含自动扁平化）
                    info.AllTargetProperties.Add(prop);
                    var autoPm = BuildAutoMatch(info, prop, fs.SourceType);
                    if (autoPm != null)
                    {
                        // 匹配对注入②：自动匹配命中（直接/扁平化写回链）附着
                        AttachBackDeclaration(autoPm, backDecl, fs);
                    }
                    else if (backDecl != null)
                    {
                        // 匹配对注入③：自动匹配未命中 + Back（场景 4——仅当实体同名属性 write-only 可达：
                        // 同名可读属性必命中建 pm）→ ReverseOnly 条目（NCMAP001 已由 BuildAutoMatch 发出，
                        // 与反向条目正交不冗余）
                        BuildReverseOnlyEntry(info, backDecl, fs);
                    }
                }
                else if (backDecl != null)
                {
                    // 匹配对注入④：NoAutoInclude 已声明 && 无显式正向声明 → ReverseOnly 条目
                    //（正向不消费本声明——对级语义；反向仍生成转换器写入）
                    BuildReverseOnlyEntry(info, backDecl, fs);
                }
                // NoAutoInclude && 未显式声明 && 无 Back/Forward → 不匹配不入候选（对级语义）
            }

        }

        static MappingSnapshot FinalizeSnapshot(FluentSnapshot fs, MappingInfo info)
        {
            // MapWhen 主链条件守卫声明消费——锚点解析（F43/F44/F45）
            // + pm 条件四字段注入（单条件；双侧同承载 P-e——与特性侧 [MapWhen] 填同一字段）。
            foreach (var kvp in fs.WhenDeclarations)
            {
                var anchorName = kvp.Key;
                var whenDecl = kvp.Value;
                var targetPm = info.PropertyMappings.FirstOrDefault(pm => pm.TargetProperty?.Name == anchorName);

                if (targetPm == null)
                {
                    // 被静态 Ignore 排除（Ignore 属性不入候选——无 pm）与无映射统一 F43；
                    // 主链 Compose × MapWhen：锚定属性仅由 FromSource 跨源映射覆盖
                    // 主链生成器按设计不展开属性赋值链，守卫语义无处安放（静默丢失过滤
                    // 语义属正确性危害；块内声明除外—— 有真实发射落点）
                    if (info.ComposeMappings.Any(cm => cm.TargetProperty.Name == anchorName))
                    {
                        info.Diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.MapWhenComposeNoEmissionSiteRule,
                            Args = new object[] { anchorName, fs.TargetType.ToDisplayString() },
                            Location = whenDecl.Location,
                        });
                    }
                    else
                    {
                        info.Diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.MapWhenAnchorNotFoundRule,
                            Args = new object[] { anchorName, fs.TargetType.ToDisplayString() },
                            Location = whenDecl.Location,
                        });
                    }
                    continue;
                }

                // F44 物化期兜底（提取期已拒同锚点双声明；跨面注入幂等防御）
                if (targetPm.ConditionMethodRef != null)
                {
                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.MapWhenDuplicateRule,
                        Args = new object[] { anchorName, fs.TargetType.ToDisplayString() },
                        Location = whenDecl.Location,
                    });
                    continue;
                }

                // 补门：T1（字段值形态）锚定 [Convert] 无源属性（SourceProperty == null!，
                // 物化 pm 存在但无源值可判）——守卫无从求值，按锚点落空 F43 拒绝（防发射期
                // BuildSourceAccess NRE / 谓词实参缺失；T2 整源不受影响——实参恒 src）
                if (whenDecl.Shape == ConditionShape.FieldValue && targetPm.SourceProperty is null)
                {
                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.MapWhenAnchorNotFoundRule,
                        Args = new object[] { anchorName, fs.TargetType.ToDisplayString() },
                        Location = whenDecl.Location,
                    });
                    continue;
                }

                ApplyWhenDeclaration(targetPm, whenDecl, fs.SourceType, fs.TargetType);
            }

            // FromSource 块内 MapWhen 声明消费
            // ComposePropertyMapping 条件四字段注入（发射落点 = Update_{Sub}_To_{Target} 赋值层）
            foreach (var blockWhen in fs.BlockWhenDeclarations)
            {
                var cm = info.ComposeMappings.FirstOrDefault(c =>
                    SymbolEqualityComparer.Default.Equals(c.SubSourceType, blockWhen.SubSourceType)
                    && c.TargetProperty.Name == blockWhen.TargetPropertyName);

                if (cm == null)
                {
                    // 块内锚点落空——(子源, 属性) 无 Compose 映射（块内 Map/速记未声明该属性）
                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.MapWhenAnchorNotFoundRule,
                        Args = new object[] { blockWhen.TargetPropertyName, blockWhen.SubSourceType.ToDisplayString() },
                        Location = blockWhen.When.Location,
                    });
                    continue;
                }

                if (cm.ConditionMethodRef != null)
                {
                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.MapWhenDuplicateRule,
                        Args = new object[] { blockWhen.TargetPropertyName, blockWhen.SubSourceType.ToDisplayString() },
                        Location = blockWhen.When.Location,
                    });
                    continue;
                }

                ApplyComposeWhenDeclaration(cm, blockWhen.When, blockWhen.SubSourceType, fs.TargetType);
            }

            return MappingExtractor.Materialize(info);
        }
    }

    /// <summary>
    /// B 形态转换器承载的生成类型限定名（ConverterDeclaringType 注入值
    /// BuildStaticConverterCall 渲染 global::{本值}.{方法名}，生成代码同类直呼）。
    /// </summary>
    private const string GeneratedMapperTypeName = "NCMapper.Generated.GeneratedMapper";

    /// <summary>
    /// B 形态转换器生成方法名（F26 收窄——Convert/ConvertBack 内联 lambda
    /// 转录产物物化为 GeneratedMapper 私有静态方法的名称）——
    /// "Map_Cv[_Back]_{目标类名}_{属性名}_{稳定哈希8}"（仅方法名段——与 When 的全限定 :: 形态不同：
    /// ConverterDeclaringType 承载类型，方法名单独承载，BuildStaticConverterCall 拼接消费）。
    /// 哈希 = FNV-1a(sourceFqn|targetFqn|propName|body) 低 32 位十六进制——
    /// 含转录体：同对同属性多通道（pm 主链/cm 块内）异体声明天然异名；同体同名同签名（形参类型组
    /// 同源同判）dedup 安全；与 When 名（Map_When_ 前缀）零冲突。
    /// </summary>
    private static string BuildGeneratedConverterMethodName(
        INamedTypeSymbol sourceType, INamedTypeSymbol targetType, string anchorName, string body, bool isBack)
    {
        var hashInput = sourceType.ToDisplayString() + "|" + targetType.ToDisplayString()
                        + "|" + anchorName + "|" + body;
        var hash = 2166136261u;
        foreach (var ch in hashInput)
        {
            hash ^= ch;
            hash *= 16777619u;
        }
        return (isBack ? "Map_CvBack_" : "Map_Cv_")
               + targetType.Name + "_" + SanitizeWhenIdentifier(anchorName) + "_" + hash.ToString("x8");
    }

    /// <summary>
    /// 继承距离计算（derived 沿 BaseType 链到 base 的步数）
    /// 最派生优先排序输入。泛型约束保证源/目标侧可达；
    /// 防御：不可达（跨层级异常）返回 int.MaxValue 排最后。
    /// internal 化：特性侧 ResolveSubclassRegistrations 复用（双通道同排序口径）。
    /// </summary>
    internal static int InheritanceDistance(Microsoft.CodeAnalysis.INamedTypeSymbol derived, Microsoft.CodeAnalysis.INamedTypeSymbol baseType)
    {
        var distance = 0;
        var current = (Microsoft.CodeAnalysis.ITypeSymbol?)derived;
        while (current != null && distance <= 64)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType)) return distance;
            current = current.BaseType;
            distance++;
        }
        return int.MaxValue;
    }

    /// <summary>
    /// 正向声明附着（匹配对路径）——pm 正向三元组填充 + 豁免门置位
    ///（符号态 PropertyMapping 正向 Converter* 三字段既有消费面零改动——只注入、不新消费）。
    /// <para>：B 形态（内联转录）分路——ConverterDeclaringType=生成类型限定名 +
    /// ConverterMethodName=生成方法名 + ConverterMethod* 方法体三字段（发射端 GenerateConverterMethods
    /// 物化为 GeneratedMapper 私有静态方法，调用点 BuildStaticConverterCall 零分叉复用）。</para>
    /// </summary>
    /// <param name="pm">正向 pm（Build* 产物；null = 声明自身失败未建 pm——附着无锚点，静默跳过）</param>
    /// <param name="forwardDecl">正向声明（null = 该属性无 Convert 声明）</param>
    /// <param name="fs">解析期单链快照（B 形态生成方法名 (S,T) 哈希源）</param>
    private static void AttachForwardDeclaration(PropertyMapping? pm, FluentForwardDeclaration? forwardDecl, FluentSnapshot fs)
    {
        if (pm == null || forwardDecl == null) return;
        if (forwardDecl.Method != null)
        {
            pm.ConverterDeclaringType = forwardDecl.Method.ContainingType.ToDisplayString();
            pm.ConverterMethodName = forwardDecl.Method.Name;
        }
        else
        {
            // B 形态：生成类型限定名 + 生成方法名（FNV 含转录体——同对同属性多通道同体天然同名
            // 同签名，dedup 安全；异体异名）+ 方法体三字段
            pm.ConverterDeclaringType = GeneratedMapperTypeName;
            pm.ConverterMethodName = BuildGeneratedConverterMethodName(
                fs.SourceType, fs.TargetType, pm.TargetProperty.Name, forwardDecl.TranscribedBody!, isBack: false);
            pm.ConverterMethodBody = forwardDecl.TranscribedBody;
            pm.ConverterMethodParamTypes = forwardDecl.TranscribedParamTypes;
            pm.ConverterMethodParamNames = forwardDecl.TranscribedParamNames;
        }
        pm.ConverterShape = forwardDecl.Shape;
        // Q2 豁免门（四终端之一）：Convert overrideIgnore → Target.HasOverrideIgnore 物化叠加
        if (forwardDecl.OverrideIgnore) pm.FluentOverrideIgnore = true;
    }

    /// <summary>
    /// Convert ForwardOnly 建 pm（无显式正向声明场景）
    /// 源侧用提取期解析产物（同名/扁平化/反向查找统一，F24 门保证可达；零重复解析）；
    /// 路径语义与 BuildAutoMatch 同名/扁平化产物一致（Direct/DerivedFlatten）；
    /// 正向三元组注入（pm.Converter*——既有消费面零改动）。
    /// <para>：B 形态（内联转录）分路——AttachForwardDeclaration 同款注入
    /// （生成三元组 + 方法体三字段）。</para>
    /// </summary>
    private static PropertyMapping BuildForwardDeclarationMapping(MappingInfo info, IPropertySymbol prop, FluentForwardDeclaration forwardDecl, FluentSnapshot fs)
    {
        var pm = new PropertyMapping
        {
            TargetProperty = prop,
            SourceProperty = forwardDecl.SourceProperty,
            SourcePath = forwardDecl.SourcePath,
            IsFlattened = forwardDecl.SourcePath.Count > 1,
            PathOrigin = forwardDecl.SourcePath.Count > 1 ? PathOrigin.DerivedFlatten : PathOrigin.Direct,
            // 正向三元组（附着模式同款注入——ForwardOnly 场景 pm 即附着产物）
            ConverterDeclaringType = forwardDecl.Method != null
                ? forwardDecl.Method.ContainingType.ToDisplayString()
                : GeneratedMapperTypeName,
            ConverterMethodName = forwardDecl.Method != null
                ? forwardDecl.Method.Name
                : BuildGeneratedConverterMethodName(fs.SourceType, fs.TargetType, prop.Name, forwardDecl.TranscribedBody!, isBack: false),
            ConverterShape = forwardDecl.Shape,
            // B 形态方法体三字段
            ConverterMethodBody = forwardDecl.TranscribedBody,
            ConverterMethodParamTypes = forwardDecl.TranscribedParamTypes,
            ConverterMethodParamNames = forwardDecl.TranscribedParamNames,
            // 显式声明标记（全局配置重匹配豁免）
            HasFluentExplicit = true,
        };
        // Q2 豁免门（四终端之一）
        if (forwardDecl.OverrideIgnore) pm.FluentOverrideIgnore = true;
        info.PropertyMappings.Add(pm);
        return pm;
    }

    /// <summary>
    /// JSON 展开映射——源侧按名自动匹配 + 类型门
    ///（镜像特性侧 [MapProperty(IsFromJson=true)] 采集 源/目标均须 string 或引用类型，
    /// 值类型参与时声明丢弃静默）；pm.IsFromJson 置位（既有物化/消费面零改动）。
    /// </summary>
    private static PropertyMapping? BuildFromJsonMapping(MappingInfo info, IPropertySymbol prop, INamedTypeSymbol sourceType)
    {
        var sourceProp = MappingExtractor.FindProperty(sourceType, prop.Name);
        if (sourceProp == null || sourceProp.GetMethod == null) return null; // 无同名源——声明丢弃（静默，NCMAP001 由自动匹配口径承载）

        // 类型门（与运行时 BuildJsonAssignment 行为一致——值类型参与时跳过）
        var srcIsString = sourceProp.Type.SpecialType == SpecialType.System_String;
        var tgtIsString = prop.Type.SpecialType == SpecialType.System_String;
        var srcIsValueType = sourceProp.Type.IsValueType;
        var tgtIsValueType = prop.Type.IsValueType;
        if ((srcIsValueType && !srcIsString) || (tgtIsValueType && !tgtIsString)) return null;

        info.AllTargetProperties.Add(prop);
        var pm = new PropertyMapping
        {
            TargetProperty = prop,
            SourceProperty = sourceProp,
            SourcePath = new List<IPropertySymbol> { sourceProp },
            IsFlattened = false,
            PathOrigin = PathOrigin.Direct,
            IsFromJson = true,
            // 显式声明标记（全局配置重匹配豁免）
            HasFluentExplicit = true,
        };
        info.PropertyMappings.Add(pm);
        return pm;
    }

    /// <summary>
    /// 反向声明附着（匹配对路径）——pm.BackConverter* 三字段填充
    ///（符号态 PropertyMapping additive 字段，Materialize 传参物化进 PropertyRef 反向三元组）。
    /// <para>：ConvertBack overrideIgnore 豁免门（四终端之一）
    /// 置位 pm.FluentOverrideIgnore（物化叠加进 Target.HasOverrideIgnore，正反向双链豁免）。</para>
    /// <para>：B 形态（内联转录）分路——BackConverterDeclaringType=生成类型
    /// 限定名 + BackConverterMethodName=生成方法名 + BackConverterMethod* 方法体三字段
    ///（物化传参进 PropertyRef.ConverterBackMethod*，ReversePlanner 复制到反向 pm 消费）。</para>
    /// </summary>
    /// <param name="pm">正向 pm（Build* 产物；null = 声明自身失败未建 pm——附着无锚点，静默跳过）</param>
    /// <param name="backDecl">反向声明（null = 该属性无 ConvertBack 声明）</param>
    /// <param name="fs">解析期单链快照（B 形态生成方法名 (S,T) 哈希源）</param>
    private static void AttachBackDeclaration(PropertyMapping? pm, FluentBackDeclaration? backDecl, FluentSnapshot fs)
    {
        if (pm == null || backDecl == null) return;
        // 异名 lambda 写回（T3 形态）：提取期实体写回目标不可知（entityProp 为 null），
        // 此处以正向 pm 的来源字段作为写回落点——ReversePlanner 的写回快照 Target 取的就是
        // 正向 pm.SourceProperty（实体属性），故 B 形态生成方法名同源取值。
        var backTargetName = backDecl.EntityProperty?.Name ?? pm.SourceProperty?.Name;
        if (backTargetName == null) return;
        if (backDecl.Method != null)
        {
            pm.BackConverterDeclaringType = backDecl.Method.ContainingType.ToDisplayString();
            pm.BackConverterMethodName = backDecl.Method.Name;
        }
        else
        {
            // B 形态：生成类型限定名 + 生成方法名（锚点=实体侧写回点，与声明一一对应）+
            // 方法体三字段
            pm.BackConverterDeclaringType = GeneratedMapperTypeName;
            pm.BackConverterMethodName = BuildGeneratedConverterMethodName(
                fs.SourceType, fs.TargetType, backTargetName, backDecl.TranscribedBody!, isBack: true);
            pm.BackConverterMethodBody = backDecl.TranscribedBody;
            pm.BackConverterMethodParamTypes = backDecl.TranscribedParamTypes;
            pm.BackConverterMethodParamNames = backDecl.TranscribedParamNames;
        }
        pm.BackConverterShape = backDecl.Shape;
        // Q2 豁免门（四终端之一）：ConvertBack overrideIgnore
        if (backDecl.OverrideIgnore) pm.FluentOverrideIgnore = true;
    }

    /// <summary>
    /// ReverseOnly 条目构建（反向无源——NoAutoInclude / 自动匹配未命中场景）。
    /// 两步式（v1.3：ApplyConverterMethodReference 为 mutate 签名）：new PropertyMapping →
    /// ApplyConverterMethodReference 填 pm 级三元组；实体属性 setter 门镜像特性侧
    /// DetectReverseOnlyConvertBackMappings 门序（public setter 或 init-only 接受，不可写 → 登记
    /// ReverseMappingSkippedRule 跳过）。
    /// </summary>
    /// <param name="info">映射信息（ReverseOnlyConvertBacks 追加目标）</param>
    /// <param name="backDecl">反向声明（EntityProperty 提取期已解析）</param>
    /// <param name="fs">解析期单链快照（诊断消息类型名）</param>
    private static void BuildReverseOnlyEntry(MappingInfo info, FluentBackDeclaration backDecl, FluentSnapshot fs)
    {
        var entityProp = backDecl.EntityProperty;
        if (entityProp == null) return; // 防御（提取期不变量：进入物化的 backDecl 恒有 EntityProperty）

        // ReverseOnly 适配门（差异登记 D-2 配套）：仅直接成员（ContainingType == SourceType）——
        // 扁平化叶（嵌套属性）无正向 pm 时反向不可表达（写回链需正向多段 pm 承载，ReverseOnly
        // 直赋不适用嵌套目标）→ 静默跳过
        if (!SymbolEqualityComparer.Default.Equals(entityProp.ContainingType, fs.SourceType)) return;

        // setter 门 + 两步式组装收敛至 TryAppendReverseOnlyEntry（
        // 纯移动——backDecl 提取期已解析、委托恒非空；overrideIgnore 豁免经 fluentOverrideIgnore 参数
        // 进入共享方法——原 逻辑保留于共享方法内，F4 否定条件防遗漏）
        // B 形态分路——委托携带 (null 方法, shape)，生成三元组 + 方法体
        // 三字段经 configureBShape 注入（A 形态路径零变化）
        MappingExtractor.TryAppendReverseOnlyEntry(
            info, entityProp, fs.SourceType, fs.TargetType,
            () => (backDecl.Method, backDecl.Shape),
            backDecl.OverrideIgnore,
            backDecl.Method == null
                ? pmBack =>
                {
                    pmBack.ConverterDeclaringType = GeneratedMapperTypeName;
                    pmBack.ConverterMethodName = BuildGeneratedConverterMethodName(
                        fs.SourceType, fs.TargetType, entityProp.Name, backDecl.TranscribedBody!, isBack: true);
                    pmBack.ConverterMethodBody = backDecl.TranscribedBody;
                    pmBack.ConverterMethodParamTypes = backDecl.TranscribedParamTypes;
                    pmBack.ConverterMethodParamNames = backDecl.TranscribedParamNames;
                }
                : null);
    }

    /// <summary>
    /// Compose 声明有效性判定（物化分流入口过滤）。
    /// <para>有效 = 主形态（P6/F7 不受阻）：Shorthand（速记）/ Block-Map / Block-MapTree /
    /// Block-Ignore（Q6 倾向 b——提升 DTO 级忽略，物化期产 ComposeMappings 空组即忽略语义）/
    /// Convert（块内转换器，Converter 非空）/ Block-Mask / Block-FromJson。</para>
    /// <para>： 交接消费——去除 Block-Mask / Block-FromJson 降级排除
    /// （002 交接注释使命完成），物化期全组遍历写 cm.MaskType / cm.IsFromJson（合并语义见物化段）。</para>
    /// </summary>
    private static bool IsEffectiveComposeDeclaration(ComposeFluentDeclaration cd)
    {
        if (cd.Converter != null) return true; // Convert（块内转换器）
        if (cd.Form == ComposeDeclarationForm.Shorthand) return true; // 速记
        if (cd.Form == ComposeDeclarationForm.Block) return true; // Block 全形态（D-1 就绪：Mask/FromJson 消费上线）
        return false;
    }

    /// <summary>
    /// 显式路径映射（Map/Convert 共用）——src 路径来自 lambda 解析段；
    /// 段须全为属性（字段段声明丢弃——映射模型仅承载 IPropertySymbol，与 [MapFrom] FindProperty 同边界）。
    /// 显式声明不发 NCMAP002（声明式跨类型意图，内建 ConvertValue 转换承接——差异登记 D2）。
    /// <para>：返回产物 pm（声明失败未建 pm 为 null——供 ConvertBack 附着）。</para>
    /// </summary>
    private static PropertyMapping? BuildExplicitPathMapping(MappingInfo info, IPropertySymbol prop, FluentPropertyDeclaration declared)
    {
        if (declared.SrcSegments == null || declared.SrcSegments.Count == 0) return null;

        var srcPath = new List<IPropertySymbol>();
        // 索引段收集——与特性通道 FromField "Items[0].Title"
        // 同承载（SourceIndexes 与 SourcePath 等长平行列表，null=该段无索引访问）
        var srcIndexes = new List<int?>();
        foreach (var segment in declared.SrcSegments)
        {
            if (segment.Property == null) return null; // 字段段/空段——声明丢弃（静默）
            srcPath.Add(segment.Property);
            srcIndexes.Add(segment.IndexAfter);
        }

        info.AllTargetProperties.Add(prop);
        var pm = new PropertyMapping
        {
            TargetProperty = prop,
            SourceProperty = srcPath[srcPath.Count - 1],
            SourcePath = srcPath,
            SourceIndexes = srcIndexes,
            IsFlattened = srcPath.Count > 1,
            // 过渡期统一多段语义（与 [MapFrom] 显式点路径同口径）：多段 IsFlattened=true + ExplicitDotPath
            PathOrigin = srcPath.Count > 1 ? PathOrigin.ExplicitDotPath : PathOrigin.Direct,
            // 显式声明标记（全局配置重匹配豁免）
            HasFluentExplicit = true,
        };
        info.PropertyMappings.Add(pm);
        return pm;
    }

    /// <summary>
    /// Mask 映射——源侧按名自动匹配（S-2a 必带 mask type，泛型约束已保证 TMask 实现 IMask）；
    /// 目标须为 string（镜像 [Mask] 通道 MaskTargetNotStringRule——IMask.Mask(string?) → string）。
    /// </summary>
    private static PropertyMapping? BuildMaskMapping(MappingInfo info, IPropertySymbol prop, FluentPropertyDeclaration declared, INamedTypeSymbol sourceType)
    {
        if (declared.MaskType == null) return null;
        var sourceProp = MappingExtractor.FindProperty(sourceType, prop.Name);
        if (sourceProp == null || sourceProp.GetMethod == null) return null; // 无同名源——声明丢弃（静默）

        info.AllTargetProperties.Add(prop);
        // 判定内核单点（B3-10 组2）：目标 string 判定 + IMask 实现判定——Fluent TMask 泛型约束
        //（where TMask : IMask）编译期保证第二判定恒过（保留作声明面放宽时的自动防线）
        if (!MappingExtractor.ValidateMaskApplication(info, prop, declared.MaskType))
        {
            return null;
        }

        var pm = new PropertyMapping
        {
            TargetProperty = prop,
            SourceProperty = sourceProp,
            SourcePath = new List<IPropertySymbol> { sourceProp },
            IsFlattened = false,
            PathOrigin = PathOrigin.Direct,
            MaskType = declared.MaskType,
            // 显式声明标记（全局配置重匹配豁免）
            HasFluentExplicit = true,
        };
        info.PropertyMappings.Add(pm);
        return pm;
    }

    /// <summary>
    /// Format 映射——源侧按名自动匹配（路径 7；镜像 <see cref="BuildMaskMapping"/> 形态）。
    /// <para>校验两段：F64 目标须 string → F65 源不可达/非 IFormattable（Nullable&lt;T&gt; 解包判定；
    /// 枚举经接口判定自然通过）。F66（Format × Convert 同锚点矛盾）由调用方 case 分派处理。
    /// 校验通过置位 pm.FormatString（与特性 [MapFormat] 双通道同承载）。</para>
    /// </summary>
    private static PropertyMapping? BuildFormatMapping(MappingInfo info, IPropertySymbol prop,
        FluentPropertyDeclaration declared, INamedTypeSymbol sourceType)
    {
        if (declared.FormatString == null) return null;

        // 目标属性须为 string（镜像特性侧 NCMAP043 判据）
        if (prop.Type.SpecialType != SpecialType.System_String)
        {
            info.Diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.FormatTargetNotStringRule,
                Args = new object[] { prop.Name, prop.Type.ToDisplayString() },
                Location = declared.Location,
            });
            return null;
        }

        // 源侧按名自动匹配（镜像 Mask "物化时按名自动匹配"——无同名源即"源属性不可达" → F65）
        var sourceProp = MappingExtractor.FindProperty(sourceType, prop.Name);
        if (sourceProp == null || sourceProp.GetMethod == null)
        {
            info.Diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.FormatSourceNotFormattableRule,
                Args = new object[] { prop.Name, "（无同名源属性——不可达）" },
                Location = declared.Location,
            });
            return null;
        }

        // 源类型未实现 IFormattable（Nullable<T>解包判定——与特性侧 ValidateFormatApplication
        // 同口径；枚举经接口判定自然通过）
        var underlyingRaw = sourceProp.Type as INamedTypeSymbol;
        var underlyingType = underlyingRaw != null
            && underlyingRaw.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? (INamedTypeSymbol?)underlyingRaw.TypeArguments[0]
                : underlyingRaw;
        if (underlyingType == null
            || !underlyingType.AllInterfaces.Any(i => i.ToDisplayString() == "System.IFormattable"))
        {
            info.Diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.FormatSourceNotFormattableRule,
                Args = new object[] { prop.Name, sourceProp.Type.ToDisplayString() },
                Location = declared.Location,
            });
            return null;
        }

        info.AllTargetProperties.Add(prop);
        var pm = new PropertyMapping
        {
            TargetProperty = prop,
            SourceProperty = sourceProp,
            SourcePath = new List<IPropertySymbol> { sourceProp },
            IsFlattened = false,
            PathOrigin = PathOrigin.Direct,
            FormatString = declared.FormatString,
            HasFluentExplicit = true,
        };
        info.PropertyMappings.Add(pm);
        return pm;
    }

    /// <summary>
    /// 维度合并：多槽判定——声明占用 ≥2 个槽位（来源槽 SrcSegments +
    /// 变换槽 MaskType/FormatString/FromJsonDeclared/MapTreeDeclared）即为新面组合形态，走组合构建；
    /// 单占声明（旧面全部形态 + 新面单操作声明）走既有单分派（产物逐字节一致）。
    /// </summary>
    private static bool IsMultiSlotDeclaration(FluentPropertyDeclaration declared)
        => (declared.SrcSegments != null ? 1 : 0)
         + (declared.MaskType != null ? 1 : 0)
         + (declared.FormatString != null ? 1 : 0)
         + (declared.FromJsonDeclared ? 1 : 0)
         + (declared.MapTreeDeclared ? 1 : 0) > 1;

    /// <summary>
    /// 组合构建（"来源 + 变换"合成一条完整取值链）
    /// 仅多槽声明可达（<see cref="IsMultiSlotDeclaration"/>；真冲突组合已被提取期 F59②③④拦截，
    /// 可达面 = 来源槽 × {Mask, Format}（FormatThenMask 既有双标链先例）+ 来源槽/同名 ×
    /// {FromJson, MapTree}（FromJson 承载赋值、MapTree 递归经共享内核））。
    /// <para>来源槽：SrcSegments 非空 → 显式路径（<see cref="BuildExplicitPathMapping"/> 产物——
    /// 已登记 AllTargetProperties/PropertyMappings）；缺省 → 同名解析（变换省略来源口径）。
    /// 变换槽依 Mask → Format → FromJson/MapTree 序叠用，各槽校验与单占 Builder 同口径：
    /// Mask = ValidateMaskApplication（失败弃 Mask 槽）；Format = F64 目标 string + F65 源
    /// IFormattable（源 = 来源槽解析产物——块内合并 同基准；失败弃 Format 槽并留诊断）；
    /// FromJson = 值类型参与类型门（BuildFromJsonMapping 同口径，失败弃 JSON 槽）；MapTree = 无
    /// 附加产物字段（Direct 同名 pm 即承载）。无同名源且无显式路径 → Format 保留"源属性不可达"
    /// 显式诊断（BuildFormatMapping 同口径），其余静默丢弃（各单占 Builder 同口径）。</para>
    /// </summary>
    private static PropertyMapping? BuildComposedFluentMapping(MappingInfo info, IPropertySymbol prop,
        FluentPropertyDeclaration declared, INamedTypeSymbol sourceType)
    {
        PropertyMapping? pm;
        if (declared.SrcSegments != null)
        {
            // 显式来源槽（BuildExplicitPathMapping 内部已登记 AllTargetProperties + PropertyMappings）
            pm = BuildExplicitPathMapping(info, prop, declared);
            if (pm == null) return null;
        }
        else
        {
            var sourceProp = MappingExtractor.FindProperty(sourceType, prop.Name);
            if (sourceProp == null || sourceProp.GetMethod == null)
            {
                // 无同名源：Format 显式诊断（BuildFormatMapping 同口径）；Mask/MapTree/FromJson 静默
                if (declared.FormatString != null)
                {
                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.FormatSourceNotFormattableRule,
                        Args = new object[] { prop.Name, "（无同名源属性——不可达）" },
                        Location = declared.Location,
                    });
                }
                return null;
            }

            pm = new PropertyMapping
            {
                TargetProperty = prop,
                SourceProperty = sourceProp,
                SourcePath = new List<IPropertySymbol> { sourceProp },
                IsFlattened = false,
                PathOrigin = PathOrigin.Direct,
                HasFluentExplicit = true,
            };
            info.AllTargetProperties.Add(prop);
            info.PropertyMappings.Add(pm);
        }

        // 变换槽叠用（可达组合面见方法注释；判定序 Mask → Format → FromJson/MapTree）
        if (declared.MaskType != null
            && MappingExtractor.ValidateMaskApplication(info, prop, declared.MaskType))
        {
            pm.MaskType = declared.MaskType;
        }

        if (declared.FormatString != null)
        {
            if (prop.Type.SpecialType != SpecialType.System_String)
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.FormatTargetNotStringRule,
                    Args = new object[] { prop.Name, prop.Type.ToDisplayString() },
                    Location = declared.Location,
                });
            }
            else
            {
                // 源（=来源槽解析产物）IFormattable 判定——G4①换用
                // 既有单点 MappingExtractor.IsFormattableSourceSymbol（MappingExtractor.Properties.cs:658；块内合并
                // :438 先例同款）——原内联实现（Nullable<T> 解包 + AllInterfaces 接口判定）与单点逐字
                // 等价 ⇒ 行为零变化（回归全绿 + 字节门零 DIFF 为门， ①）
                if (!MappingExtractor.IsFormattableSourceSymbol(pm.SourceProperty.Type))
                {
                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.FormatSourceNotFormattableRule,
                        Args = new object[] { prop.Name, pm.SourceProperty.Type.ToDisplayString() },
                        Location = declared.Location,
                    });
                }
                else
                {
                    pm.FormatString = declared.FormatString;
                }
            }
        }

        if (declared.FromJsonDeclared)
        {
            // JSON 展开类型门（BuildFromJsonMapping 同口径——值类型参与时弃 JSON 槽）
            // G4②：F64/FromJson 维持**在册独立防御**设计——三站点内联同口径
            // 属既有登记（引用对象 = MappingExtractor.Properties.cs:651-656 注释"三站点独立防御……登记
            // 观察"，只读核对不改其注释）；本方案继承该设计不推翻，此处补 composed 内联消费点说明，
            // 代码行为零改动（②）
            var srcIsString = pm.SourceProperty.Type.SpecialType == SpecialType.System_String;
            var tgtIsString = prop.Type.SpecialType == SpecialType.System_String;
            if (!((pm.SourceProperty.Type.IsValueType && !srcIsString) || (prop.Type.IsValueType && !tgtIsString)))
            {
                pm.IsFromJson = true;
            }
        }
        // MapTree：无附加产物字段（Direct pm 即承载——递归经共享派生内核，BuildMapTreeMapping 同构）

        return pm;
    }

    /// <summary>
    /// MapTree 映射——源侧按名解析（"递归走相同 (S,T) 配置"：复杂属性递归/深拷贝由共享派生内核
    /// 既有行为承接，物化层等价 Direct 同名映射）。
    /// </summary>
    private static PropertyMapping? BuildMapTreeMapping(MappingInfo info, IPropertySymbol prop, INamedTypeSymbol sourceType)
    {
        var sourceProp = MappingExtractor.FindProperty(sourceType, prop.Name);
        if (sourceProp == null || sourceProp.GetMethod == null) return null; // 无同名源——声明丢弃（静默）

        info.AllTargetProperties.Add(prop);
        var pm = new PropertyMapping
        {
            TargetProperty = prop,
            SourceProperty = sourceProp,
            SourcePath = new List<IPropertySymbol> { sourceProp },
            IsFlattened = false,
            PathOrigin = PathOrigin.Direct,
            // 显式声明标记（全局配置重匹配豁免）
            HasFluentExplicit = true,
        };
        info.PropertyMappings.Add(pm);
        return pm;
    }

    /// <summary>
    /// 自动匹配（未显式声明 && !NoAutoInclude）——按名匹配（OrdinalIgnoreCase）→ 扁平化兜底 →
    /// NCMAP001（受 ReportUnmappedTargetMembers 选项过滤，与 [MapFrom] 同口径）；
    /// 命中后类型不兼容发 NCMAP002（自动路径与 [MapFrom] 逐字节同口径）。
    /// <para>不读取源侧 [MapProperty]/[Convert] 反向标注（特性在 Fluent 通道不生效）。</para>
    /// <para>：返回产物 pm（命中为 pm——含扁平化写回链；未命中为 null
    /// ConvertBack 场景 4 判据：仅当实体同名属性 write-only 时未命中可达）。</para>
    /// </summary>
    private static PropertyMapping? BuildAutoMatch(MappingInfo info, IPropertySymbol prop, INamedTypeSymbol sourceType)
    {
        // 判定内核单点（B3-10 组3）：三态判定（同名命中 / 扁平化兜底 / 未命中 + NCMAP001 构造）
        //——FindProperty 前置留薄壳（Fluent 侧无反向匹配/无 config，恒按名直查）
        var judgment = MappingExtractor.JudgeAutoMatch(prop, sourceType, prop.Name,
            MappingExtractor.FindProperty(sourceType, prop.Name), isExplicitMiss: false);

        if (judgment.SourceProp == null)
        {
            // 自动扁平化兜底
            if (judgment.FlatPath != null)
            {
                var flatPm = MappingExtractor.BuildFlattenMapping(prop, judgment.FlatPath);
                info.PropertyMappings.Add(flatPm);
                return flatPm;
            }

            // NCMAP001（无条件生成——报告与否由输出期按注入后选项过滤）
            info.Diagnostics.Add(judgment.MissDiagnostic!);
            return null;
        }

        var pm = MappingExtractor.BuildDirectMapping(prop, judgment.SourceProp);
        info.PropertyMappings.Add(pm);

        // NCMAP002（自动路径类型不兼容——与 [MapFrom] ProcessSameSourceMapping 同口径）
        // ：属性级 NoAutoInclude(dest) 命中
        // 属性豁免——dest 不参与可映射性校验（导航属性为主要使用场景，承载为导航收集跳过标记
        // 而非映射面），类型不兼容噪声诊断不再发射（List<string> 形态本就无 pm 无诊断，等价对不受影响）
        if (!info.PropertyNoAutoIncludeTargets.Contains(prop.Name))
        {
            MappingExtractor.EmitTypeIncompatibleIfNotCompatible(info, prop, judgment.SourceProp);
        }
        return pm;
    }
}
