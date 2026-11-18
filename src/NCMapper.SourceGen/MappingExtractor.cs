using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.Shared;
namespace NCMapper.SourceGen;
    /// <summary>
    /// 映射信息提取 — 从 [MapFrom] 特性上下文提取映射关系 + 自动扁平化匹配
    /// <para>partial：全局配置感知修正（值版）在 MappingExtractor.SnapshotConfig.cs</para>
    /// </summary>
    internal static partial class MappingExtractor
    {
        /// <summary>
        /// 路径语义归一解析器— ⑥⑦ 段循环收敛为 SymbolPathResolver
        /// （符号版 adapter，FindProperty 配置感知 + B-2 统一判据；无状态安全）
        /// </summary>
        private static readonly SymbolPathResolver s_symbolResolver = new();

        /// <summary>
        /// 从 [MapFrom] 特性上下文提取映射信息 + 诊断信息
        /// <para><b>匹配规则需与 MapperBase.Properties.GetSourceItemByTargetItem 保持一致</b>：</para>
        /// <para>1. [MapProperty] 的 FromSource 必须与 [MapFrom] 的源类型一致才参与映射</para>
        /// <para>2. 跨表字段（FromSource ≠ 主源类型）收集到 ComposeMappings，由源生成器生成 Compose 方法</para>
        /// <para>3. MapIgnore 属性跳过</para>
        /// <para>4. 属性名匹配使用 OrdinalIgnoreCase</para>
        /// <para>5. 反向映射（CreateReverseMapping）基于正向结果翻转，不重新匹配</para>
        /// <para>差异：源生成器额外支持自动扁平化，运行时不支持</para>
        /// <para>：内部仍以符号模型解析，出口 Materialize 为纯值 MappingSnapshot
        /// （缓存值零符号持有）</para>
        /// </summary>
        /// <param name="ctx">特性语法上下文，包含目标类型和MapFrom特性信息</param>
        /// <param name="ct">取消令牌，用于在长时间编译时响应取消请求</param>
        /// <returns>解析出的映射快照；如果无法提取则返回null</returns>
        public static MappingSnapshot? ExtractMappingInfo(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
        {
            var targetSymbol = ctx.TargetSymbol as INamedTypeSymbol;
            if (targetSymbol == null) return null;

            var mapFromAttr = ctx.Attributes.FirstOrDefault();
            if (mapFromAttr == null || mapFromAttr.ConstructorArguments.Length == 0)
                return null;

            var sourceType = mapFromAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
            if (sourceType == null) return null;

            return ExtractMappingCore(targetSymbol, sourceType, (TypeDeclarationSyntax)ctx.TargetNode, ct);
        }

        /// <summary>
        /// [K2 拆分 ] ExtractMappingInfo 符号级内核——承接 ctx 解包后的完整物化逻辑
        /// <para>薄壳保留 ctx 依赖校验，内核入参不重复校验</para>
        /// <para>纯移动纪律：语句序列、局部变量名、诊断构造原样搬移自原 ExtractMappingInfo</para>
        /// <para>直调入口——测试侧经 BuildCompilation + 符号定位直调，绕过 ForAttributeWithMetadataName 管线</para>
        /// </summary>
        /// <param name="targetSymbol">目标类型符号（薄壳已非空校验）</param>
        /// <param name="sourceType">源类型符号（薄壳已非空校验）</param>
        /// <param name="targetSyntax">目标类型声明语法（ctx.TargetNode 转型）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>解析出的映射快照；物化后返回</returns>
        public static MappingSnapshot? ExtractMappingCore(INamedTypeSymbol targetSymbol, INamedTypeSymbol sourceType, TypeDeclarationSyntax targetSyntax, CancellationToken ct)
        {
            var info = new MappingInfo
            {
                TargetType = targetSymbol,
                SourceType = sourceType,
                SourceClassName = sourceType.Name,
                TargetClassName = targetSymbol.Name,
                TargetSyntax = targetSyntax
            };

            // 解析未映射成员诊断开关（类级 > 程序集级 > 默认），
            // 供 NCMAP001/NCMAP007 触发点按策略抑制
            info.DiagnosticOptions = DiagnosticOptionsResolver.Resolve(targetSymbol);

            // 特性侧生命周期钩子提取（[BeforeMapping]/[AfterMapping]
            // 类型级特性——双 ctor 解析 + 签名适配本对 (sourceType, targetSymbol)；失败发
            // NCMAP038/039/040）。位于 NCMAP011 短路检查之前——non-public 跳过生成的对
            // 仍填充钩子承载（诊断 fail-fast 与运行时元数据可见性）
            ExtractLifecycleHooks(info, sourceType, targetSymbol);

            // 特性侧多态子类型对登记提取（[MapSubclass]—— 方案 1：
            // 标注在承载 [MapFrom] 的基类 DTO 上；Inherited=false 零传播）。F46/F47 校验单点发射
            //（合成管道静默复用同解析）；最派生优先排序与 Fluent 侧同口径。无声明恒空清单
            //（发射端零分派产物，G3 字节门）。位于 NCMAP011 短路之前——non-public 对仍填充
            //（与钩子承载同可见性语义）。
            var subclassPairs = ResolveSubclassRegistrations(targetSymbol, sourceType, info.Diagnostics);
            if (subclassPairs.Count > 0)
                info.SubclassPairs = subclassPairs;

            // 第四个类级特性读取点
            // [MapNoAutoMatch] 对级白名单开关 → 复用既有对级位 info.NoAutoInclude（零新增字段，
            // 下游补匹配门 MappingExtractor.SnapshotConfig.cs:62 / 反向派生 ReversePlanner / 运行时元数据
            // 发射 SnapshotMetadataEmitter:188 三消费点零改动自然生效）。
            // ⚠️ 读法约束：Roslyn INamedTypeSymbol.GetAttributes() **不返回继承特性**，而本特性
            //    Inherited=true——必须沿基类型链遍历（GetTypeAndBaseTypes 先例，:318）；否则派生 DTO
            //    收不到基类白名单 → 特性腿静默失效，且与反射腿 Type.IsDefined(inherit:true) 分叉。
            //    （AttributeUsage(Class) 使接口腿天然无标注，GetTypeAndBaseTypes 的接口枚举段不构成过匹配。）
            if (HasMapNoAutoMatchInHierarchy(targetSymbol))
                info.NoAutoInclude = true;

            // NCMAP011：非 public 类型跳过源生成器代码生成（判定内核单点——B3-10 组1；
            // 薄壳差异仅 Location 口径：特性侧恒 DTO 标识位置，Fluent 侧各类型自身位置）
            // 生成的映射器类是 internal，当前版本对 non-public 类型不生成编译时代码，
            // 自动 fallback 到运行时引擎路径（功能不受影响）。
            // 典型场景：测试项目的 private 嵌套 mutant 模型、internal 内部 DTO
            // 同时检查源类型可访问性（跨程序集 internal 源同样导致 CS0122）
            var nonPublicSkip = TryBuildNonPublicSkipDiagnostic(sourceType, targetSymbol.Locations.FirstOrDefault())
                ?? TryBuildNonPublicSkipDiagnostic(targetSymbol, targetSymbol.Locations.FirstOrDefault());
            if (nonPublicSkip != null)
            {
                info.SkipCodeGeneration = true;
                info.Diagnostics.Add(nonPublicSkip);
                return Materialize(info);
            }

            // 属性处理逻辑拆分为 ProcessTargetProperty
            // 遍历门收敛至 EnumerateWritableTargetProperties（共享门，见 MappingExtractor.Traversal.cs），
            // NCMAP006 发射移出为本地函数 EmitNonWritableDiagnostic（非 static，捕获 info/sourceType——否则触发 CS8421）
            // NCMAP006 方案C：setter 非 public 且源存在同名属性时发出 Warning
            void EmitNonWritableDiagnostic(IPropertySymbol prop)
            {
                // 排除编译器隐式声明的属性（如 record 的 EqualityContract）和已标注 [MapIgnore] 的属性
                var hasMapIgnore = prop.IsImplicitlyDeclared || prop.GetAttributes().Any(a =>
                    AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapIgnore);
                if (!hasMapIgnore)
                {
                    var matchingSource = FindProperty(sourceType, prop.Name);
                    if (matchingSource != null)
                    {
                        var setterDesc = prop.SetMethod == null
                            ? "无 setter（get-only）"
                            : prop.SetMethod.DeclaredAccessibility.ToString().ToLowerInvariant();
                        info.Diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptors.UnmappedTargetMemberRule,
                            Args = new object[] { prop.Name, prop.Type.ToDisplayString(), sourceType.Name, setterDesc },
                            Location = prop.Locations.FirstOrDefault()
                        });
                    }
                }
            }

            foreach (var prop in EnumerateWritableTargetProperties(targetSymbol, ct, EmitNonWritableDiagnostic))
            {

                // [MapIgnore(Direction.Both)] 跳过；Direction.ToDto/ToEntity 保留，在代码生成时按方向跳过
                // NCMAP022：[MapIgnore] 与 OverrideIgnore=true 同标 → 矛盾诊断（忽略生效）
                // 必须在双向 continue 之前判定（该属性不会进入 AllTargetProperties，Applier 侧不可见）
                // [ConvertBack] 纳入 OverrideIgnore 矛盾面（与 [Convert] 对称）
                // （接入面 c）：[MapFormat] 纳入矛盾面——漏改 = 矛盾配置静默漏报
                if (prop.GetAttributes().Any(a => AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapIgnore)
                    && prop.GetAttributes().Any(a =>
                        (AttrSymbolCache.DisplayName(a.AttributeClass) is AttributeNames.MapProperty
                            or AttributeNames.Convert
                            or AttributeNames.Mask
                            or AttributeNames.ConvertBack
                            or AttributeNames.MapFormat)
                        && a.NamedArguments.Any(kv => kv.Key == "OverrideIgnore" && kv.Value.Value is true)))
                {
                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptors.MapIgnoreWithOverrideRule,
                        Args = new object[] { prop.Name },
                        Location = prop.Locations.FirstOrDefault(),
                    });
                }

                if (ShouldIgnoreInDirection(prop, toDto: true) && ShouldIgnoreInDirection(prop, toDto: false)) continue;

                // 特性通道对级白名单门——门位置于
                // AllTargetProperties.Add **之前**（未声明不入候选：该列直进 AllTargetSlots 发射面
                //（SnapshotMetadataEmitter:227-233）、反向候选 SourceProperties（ReversePlanner:435）与
                // 快照相等性三面同源）；Fluent 侧同构锚 = MappingSnapshotBuilder.cs:608/:611/:632。
                // 既有治理照常先行（[MapIgnore] 双向 continue / NCMAP022 / NCMAP006）；显式 [MapProperty]
                //（含跨表 FromSource≠S）照常；实体侧 [ConvertBack] ReverseOnly 注入与白名单无关（D1 边界）。
                if (info.NoAutoInclude && !HasExplicitMapPropertyDeclaration(prop))
                {
                    // 白名单开 && 无 [MapProperty] && 带变换特性
                    // → NCMAP047（Error）+ 跳过（不映射、不入候选）
                    if (HasWhitelistTransformAttribute(prop))
                    {
                        info.Diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptors.TransformWithoutMapPropertyUnderNoAutoMatchRule,
                            Args = new object[] { prop.Name },
                            Location = prop.Locations.FirstOrDefault(),
                        });
                    }
                    continue;
                }

                // 保留全部可匹配目标属性候选（含匹配失败者）供管线 Select 阶段配置感知补匹配
                info.AllTargetProperties.Add(prop);

                ProcessTargetProperty(info, prop, sourceType);
            }

            if (info.PropertyMappings.Count == 0 && info.ComposeMappings.Count == 0)
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.NoPropertiesMappedRule,
                    Args = new object[] { targetSymbol.Name, sourceType.Name },
                    Location = targetSymbol.Locations.FirstOrDefault()
                });
            }

            // NCMAP007：检测源属性未被映射（Info 级）
            DetectUnmappedSourceMembers(info, sourceType);

            // NCMAP009：检测重复 [MapProperty] 指向同一源属性
            DetectDuplicateFieldMappings(info, sourceType);

            // NCMAP010：检测枚举映射成员无重叠
            DetectEnumNoOverlap(info);

            // NCMAP026：[Mask] 标注位置非规范（源侧）诊断
            // 触发条件：[Mask] 标注所在类 ≠ [MapFrom] 标注类（Mask 标在源侧 = 非规范位置）
            // Mask 语义单向（目标 string 脱敏），源侧标注不生效——仅诊断不消费（方案语义点②a）。
            // 互指对（双侧均有 [MapFrom] 且互指）时由 NCMAP025（Error）优先，本诊断不叠加。
            DetectMaskPositionMismatch(info, sourceType, targetSymbol);

            // 实体侧 [ConvertBack] 反向无源扫描（扫描物化）
            // 实体独有属性对反向管线可见化的唯一入口（运行时镜像：ExpressionBuilder.BuildMapBody
            // 无源块前置分支；消费端：ReversePlanner.CreateReverseMapping 追加）
            DetectReverseOnlyConvertBackMappings(info, sourceType, targetSymbol);

            return Materialize(info);
        }

        /// <summary>
        /// 反向匹配 — 锚点属性按名匹配失败时，从源类型属性上的
        /// [MapProperty(FromSource=标注类, FromField=锚点属性名)] 反向查找对应源属性
        /// <para>B1（背书）：属性级标注无论类级 [MapFrom]
        /// 标注位置均应消费。规范场景不进入此路径；仅 sourceProp 按名匹配失败时触发。</para>
        /// </summary>
        /// <param name="sourceType">源类型（构造参数[0]，对侧类型）</param>
        /// <param name="targetProp">锚点属性（标注类属性，当前 prop）</param>
        /// <param name="reverseAttr">输出：匹配到的对侧 [MapProperty] 或 [Convert] 标注（未匹配为 null）</param>
        /// <returns>匹配到的源属性；未匹配返回 null</returns>
        /// <para>：internal 化供 Fluent 通道 Convert 源侧解析复用
        ///（ResolveConvertMethod 步骤 4——与特性侧 [Convert] 消费端解析序一致；
        /// 逻辑零变化，同程序集可见性放宽，ApplyConverterMethodReference 先例）。</para>
        internal static IPropertySymbol? TryFindSourceByReverseAttribute(
            INamedTypeSymbol sourceType, IPropertySymbol targetProp, out AttributeData? reverseAttr)
        {
            reverseAttr = null;
            // 遍历继承层次（与库内 GetTypeAndBaseTypes 惯例一致，L76/649/905）
            foreach (var type in GetTypeAndBaseTypes(sourceType))
            {
                foreach (var member in type.GetMembers())
                {
                    if (member is not IPropertySymbol sp || sp.GetMethod == null)
                        continue;

                    // [MapIgnore(Direction=Both)] 跳过（与正向 L127 双向检查一致）；
                    // 方向性 ToDto/ToEntity 过滤在生成阶段由 IsToDtoMapping 按目标 [MapFrom] 推导（见）
                    if (ShouldIgnoreInDirection(sp, toDto: true) && ShouldIgnoreInDirection(sp, toDto: false))
                        continue;

                    // X1-1b（W4S-2，语义点①a）：对侧 [Convert] 消费
                    // 锚点属性无标注时，源侧同名属性带 [Convert] 按名匹配命中（Convert 无 FromField，
                    // 匹配语义 = 属性名 OrdinalIgnoreCase），reverseAttr 返回 Convert 标注供转换器消费。
                    var convAttr = sp.GetAttributes().FirstOrDefault(a =>
                        AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Convert);
                    if (convAttr != null
                        && string.Equals(sp.Name, targetProp.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        reverseAttr = convAttr;
                        return sp;
                    }

                    var attr = sp.GetAttributes().FirstOrDefault(a =>
                        AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapProperty);
                    if (attr == null || attr.ConstructorArguments.Length < 2)
                        continue;

                    var attrFromSource = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
                    var attrFromField = attr.ConstructorArguments[1].Value as string;

                    // OrdinalIgnoreCase（与运行时 PropertyMatcher L301 / FindProperty L653 一致）
                    if (SymbolEqualityComparer.Default.Equals(attrFromSource, targetProp.ContainingType)
                        && string.Equals(attrFromField, targetProp.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        reverseAttr = attr;
                        return sp;
                    }
                }
            }
            return null;
        }

        // 原「构建反向映射信息」无宿主文档块删除（T6.1）——当前载体 NCMapper.SourceGen.ReversePlanner.CreateReverseMapping

        /// <summary>
        /// 判断符号是否标注了指定名称的特性
        /// </summary>
        /// <param name="symbol">要检查的符号</param>
        /// <param name="attrFullName">特性的完全限定名</param>
        /// <returns>如果符号标注了该特性则返回true</returns>
        private static bool HasAttribute(ISymbol symbol, string attrFullName)
        {
            return symbol.GetAttributes().Any(a => AttrSymbolCache.DisplayName(a.AttributeClass) == attrFullName);
        }

        /// <summary>
        /// 检查属性是否在指定映射方向应被忽略 — 读取 [MapIgnore] 的 Direction 值
        /// <para>Direction.Both → 始终跳过；Direction.ToDto → toDto=true 时跳过；Direction.ToEntity → toDto=false 时跳过</para>
        /// <para>支持构造函数参数 [MapIgnore(Direction.ToDto)] 和命名属性 [MapIgnore(Direction = Direction.ToDto)] 两种方式</para>
        /// </summary>
        /// <param name="prop">属性符号</param>
        /// <param name="toDto">是否为 ToDto 方向（true=Entity→DTO，false=DTO→Entity）</param>
        /// <returns>true 表示该属性在当前方向应被忽略</returns>
        private static bool ShouldIgnoreInDirection(IPropertySymbol prop, bool toDto)
        {
            var attr = prop.GetAttributes().FirstOrDefault(
                a => AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapIgnore);
            if (attr == null) return false;

            // 默认 Direction = Both (0)
            int directionValue = 0;

            // 构造函数参数方式：[MapIgnore(Direction.ToDto)]
            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int ctorVal)
                directionValue = ctorVal;

            // 命名属性方式：[MapIgnore(Direction = Direction.ToDto)]
            if (attr.NamedArguments != null)
            {
                foreach (var arg in attr.NamedArguments)
                {
                    if (arg.Key == "Direction" && arg.Value.Value is int namedVal)
                        directionValue = namedVal;
                }
            }

            // Direction 枚举值：0=Both, 1=ToDto, 2=ToEntity
            return directionValue switch
            {
                0 => true,          // Both
                1 => toDto,         // ToDto
                2 => !toDto,        // ToEntity
                _ => true           // 未知值默认跳过
            };
        }

        /// <summary>
        /// [MapNoAutoMatch] 类级白名单开关判定（第四个类级特性读取点内核）
        /// 沿基类型链遍历（<see cref="GetTypeAndBaseTypes"/> 先例；Roslyn GetAttributes() 不含继承特性，
        /// 而本特性 Inherited=true——D5① 逐腿口径表符号提取腿）。
        /// </summary>
        /// <param name="targetSymbol">目标类型符号（[MapFrom] 承载侧 / DTO）</param>
        /// <returns>类型链上任一类型标注 [MapNoAutoMatch] 时为 true</returns>
        private static bool HasMapNoAutoMatchInHierarchy(INamedTypeSymbol targetSymbol)
        {
            foreach (var type in GetTypeAndBaseTypes(targetSymbol))
            {
                if (type.GetAttributes().Any(a => AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapNoAutoMatch))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 白名单"显式声明"判定（显式声明边界行）
        /// 目标属性同身标注 [MapProperty]（含单参缺省 FromField 同名默认与跨表 FromSource≠S 形态，
        /// 两者均为"点名"动作本身）。对侧（源侧）标注不参与（对侧标注行——门后不可达）。
        /// </summary>
        private static bool HasExplicitMapPropertyDeclaration(IPropertySymbol prop)
            => prop.GetAttributes().Any(a => AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapProperty);

        /// <summary>
        /// D1 变换特性族判定（NCMAP047 触发面）——目标侧属性带
        /// [Convert]/[Mask]/[MapFormat]/[MapWhen] 之一即视为"声明了映射变换但缺 [MapProperty] 头声明"。
        /// <para>边界：IsFromJson 为 [MapProperty] 具名参数（必随同身 [MapProperty]，已被显式声明分支
        /// 覆盖，不入本判定）；[ConvertBack] 属反向声明（ReverseOnly 行——D1 不管辖）；
        /// [MapTree] 为类级定义（AttributeUsage 不含 Property，天然不入属性级判定）。</para>
        /// </summary>
        private static bool HasWhitelistTransformAttribute(IPropertySymbol prop)
            => prop.GetAttributes().Any(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) is AttributeNames.Convert
                    or AttributeNames.Mask
                    or AttributeNames.MapFormat
                    or AttributeNames.MapWhen);

        /// <summary>
        /// 获取类型及其完整继承层次（类走 BaseType 链，接口额外遍历 AllInterfaces）
        /// <para>：internal 化供 Fluent 通道（MappingSnapshotBuilder）复用（逻辑零变化）</para>
        /// </summary>
        internal static IEnumerable<INamedTypeSymbol> GetTypeAndBaseTypes(INamedTypeSymbol type)
        {
            for (var t = type; t != null; t = t.BaseType)
                yield return t;

            // 统一遍历 AllInterfaces（类实现的接口 + 接口继承的接口）
            foreach (var iface in type.AllInterfaces)
                yield return iface;
        }

        /// <summary>
        /// 在类型及其基类/继承接口中按名称（大小写不敏感）查找属性
        /// <para>：internal 化供 Fluent 通道（MappingSnapshotBuilder）复用（逻辑零变化）</para>
        /// <para>(类型,名)→属性 记忆化——全族站点（Fluent 通道
        /// 直调/SnapshotBuilder/config 感知版/扁平化段）共用本单点缓存。形态：CWT&lt;INamedTypeSymbol,
        /// ConcurrentDictionary&gt; 符号键控（X10 同款：条目随符号 GC 回收不跨 compilation
        /// "禁跨 GeneratorRun"实质合规：FindProperty 为纯函数，同 (type,name) 恒同结果——基类链/
        /// 成员/B3-14 过滤判定均为符号静态形状，缓存值恒新鲜）；命中语义"最派生优先"由
        /// FindPropertyCore 遍历序承载（逐字保留），缓存仅免重扫；未命中同样入表（null 值）。
        /// ConcurrentDictionary 承载 transform 期可能的并行回调（GetOrAdd 工厂幂等）；
        /// 键 OrdinalIgnoreCase 与判定口径同源（同键共享同结果条目）。</para>
        /// </summary>
        /// <param name="type">要搜索的类型</param>
        /// <param name="name">属性名称</param>
        /// <returns>找到的属性符号；未找到则返回null</returns>
        internal static IPropertySymbol? FindProperty(INamedTypeSymbol type, string name)
        {
            var byName = _findPropertyCache.GetValue(type,
                static _ => new System.Collections.Concurrent.ConcurrentDictionary<string, IPropertySymbol?>(System.StringComparer.OrdinalIgnoreCase));
            // netstandard2.0 无 GetOrAdd(key, factory, arg) 三参版 → 闭包版（GetOrAdd 工厂幂等，
            // 并发重复计算无害）；FindPropertyCore 纯函数保证重复执行结果恒同
            return byName.GetOrAdd(name, n => FindPropertyCore(type, n));
        }

        /// <summary>
        /// FindProperty 遍历核（B2-14 抽出）——原实现逐字保留（B3-14 过滤在位），
        /// 类型链 × 成员全扫的记忆化对象
        /// </summary>
        private static IPropertySymbol? FindPropertyCore(INamedTypeSymbol type, string name)
        {
            foreach (var t in GetTypeAndBaseTypes(type))
            {
                foreach (var prop in t.GetMembers().OfType<IPropertySymbol>())
                {
                    // 补 IsStatic/IsIndexer 过滤
                    // 缺陷态：静态属性（含派生 new static 遮蔽）被命中 → 发射实例访问静态
                    //（CS0176 面）；索引器同理错位。可读性过滤经 t65b6 回归裁定回退
                    //（ReverseOnly 写回目标 = write-only 实体属性为合法消费面，GetPublic 过滤误伤）
                    if (prop.IsStatic || prop.IsIndexer) continue;
                    if (System.StringComparer.OrdinalIgnoreCase.Equals(prop.Name, name))
                        return prop;
                }
            }
            return null;
        }

        /// <summary>
        /// FindProperty 记忆化缓存— 键 = 目标类型符号（CWT 引用键控，生命周期随符号）
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<INamedTypeSymbol, System.Collections.Concurrent.ConcurrentDictionary<string, IPropertySymbol?>> _findPropertyCache
            = new System.Runtime.CompilerServices.ConditionalWeakTable<INamedTypeSymbol, System.Collections.Concurrent.ConcurrentDictionary<string, IPropertySymbol?>>();

        /// <summary>
        /// 显式点路径拆段解析— FromField 含 '.' 时按段逐级查找
        /// <para>段匹配 OrdinalIgnoreCase；中间节点须 class/interface（非值类型/string，C3）；
        /// 任一段缺失或不可导航返回 null，并输出失败段名 + 失败段所在类型（供 NCMAP001/NCMAP005 带失败段名诊断）。</para>
        /// <para>：支持集合索引段 "B[0]"（属性名 + 非负整数索引；
        /// 索引段元素类型须可导航（class/interface 非 string）才能后接属性段；非集合类型段带索引 → 失败）。</para>
        /// </summary>
        /// <param name="rootType">根类型（主源类型或 Compose 子源类型）</param>
        /// <param name="dotPath">点路径（如 "A.B.C"、"A.B[0].C"）</param>
        /// <param name="failedSegment">失败段名（成功时为 null）</param>
        /// <param name="failedType">失败段所在类型（成功时为 null）</param>
        /// <param name="sourceIndexes">段索引元数据（与路径等长；null=纯属性段，非 null=集合索引；失败时为 null）</param>
        /// <returns>路径属性列表（末段 = 叶节点）；失败返回 null</returns>
        private static List<IPropertySymbol>? TryResolveDotPath(INamedTypeSymbol rootType, string dotPath,
            out string? failedSegment, out INamedTypeSymbol? failedType, out List<int?>? sourceIndexes)
        {
            failedSegment = null;
            failedType = null;
            sourceIndexes = null;

            // 段循环收敛为 SymbolPathResolver（正向尽力式）
            // FailedSegment/FailedSegmentIndex 回传诊断；失败类型名由调用方按 FailedSegmentIndex 取
            var result = s_symbolResolver.Resolve(rootType, dotPath, PathResolutionOptions.ForwardEager, context: null);
            if (!result.Success)
            {
                failedSegment = result.FailedSegment ?? dotPath.Split('.')[result.FailedSegmentIndex];
                failedType = GetFailedType(rootType, result);
                return null;
            }

            sourceIndexes = result.SourceIndexes;
            return result.Path;
        }

        /// <summary>
        /// 按失败段下标取失败相关类型（诊断用类型名）— FailedSegmentIndex==0 时为根类型，
        /// 否则为前缀路径末段属性类型（查找所在类型语义，与原 failedType=currentType 一致）。
        /// </summary>
        /// <param name="rootType">根类型</param>
        /// <param name="result">解析失败结果</param>
        /// <returns>失败相关类型</returns>
        private static INamedTypeSymbol? GetFailedType(INamedTypeSymbol rootType, PathResolutionResult<IPropertySymbol> result)
        {
            if (result.FailedSegmentIndex == 0 || result.Path.Count == 0)
            {
                return rootType;
            }

            return result.Path[result.FailedSegmentIndex - 1].Type as INamedTypeSymbol ?? rootType;
        }

        /// <summary>
        /// 提取集合元素类型— 数组 / 泛型 IEnumerable&lt;T&gt;；
        /// 非集合（含 string，实现 IEnumerable&lt;char&gt; 但不可索引导航）返回 null
        /// </summary>
        private static ITypeSymbol? TryGetCollectionElementType(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol arrayType)
                return arrayType.ElementType;
            if (type.SpecialType == SpecialType.System_String)
                return null;
            foreach (var iface in type.AllInterfaces)
            {
                if (iface is INamedTypeSymbol nt && nt.IsGenericType
                    && nt.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                {
                    return nt.TypeArguments[0];
                }
            }
            return null;
        }

        /// <summary>
        /// 从 [MapProperty] 特性数据提取 FromField— 构造位置参数第 2 个（索引 1）
        /// </summary>
        private static bool TryGetMapPropertyFromField(AttributeData attr, out string fromField)
        {
            if (attr.ConstructorArguments.Length >= 2 && attr.ConstructorArguments[1].Value is string s)
            {
                fromField = s;
                return true;
            }
            fromField = "";
            return false;
        }

        /// <summary>
        /// 自动扁平化匹配 — 将目标属性名拆解为候选路径，在源类型的嵌套属性中查找匹配
        /// <para>：internal 化供 Fluent 通道（MappingSnapshotBuilder）复用（逻辑零变化）</para>
        /// </summary>
        internal static List<IPropertySymbol>? TryFindFlattenedPath(INamedTypeSymbol sourceType, string targetPropName)
        {
            // 候选生成收敛为 PathCandidateGenerator.Build（镜像删除），
            // 段循环收敛为 SymbolPathResolver（正向尽力式，context=null 现状 OrdinalIgnoreCase 匹配）
            var candidates = PathCandidateGenerator.Build(targetPropName);

            foreach (var candidate in candidates)
            {
                if (candidate.Count <= 1) continue;

                // B-2 修复落点：中间节点判据随核心统一（排除值类型 + string，向 ① 行为收敛——
                // string 中间节点由"后续段失败"提前为 NotNavigable，最终映射结果不变，失败时机提前）
                var result = s_symbolResolver.Resolve(sourceType, candidate, PathResolutionOptions.ForwardEager, context: null);
                if (result.Success && result.Path.Count > 1)
                {
                    return result.Path;
                }
            }

            return null;
        }

        /// <summary>
        /// 诊断用途的类型兼容性检查
        /// <para>：internal 化供 Fluent 通道（MappingSnapshotBuilder）复用（逻辑零变化）</para>
        /// </summary>
        internal static bool IsTypeCompatibleForDiagnostic(ITypeSymbol sourceType, ITypeSymbol targetType)
        {

            // ③：本诊断判据链的全部差异分支（基类链/接口链/
            // 泛型同定义/Nullable 解包等类型/任一侧 enum·string·Guid）经 TG-C 基线比对后一律保持
            // 诊断链私有——并入共享核心将放宽主链判定（裁决原则：比对失败保持私有，
            // v1.2 B-4 选项甲）；共享部分（等类型/隐式转换/集合字典元素级）经 TypeHelpers 转发至
            // TypeCompatibilityCore 单点（隐式转换随 经 TypeHelpers.IsImplicitConvertible 达核心表）。
            if (sourceType.Equals(targetType, SymbolEqualityComparer.Default)) return true;

            var srcUnderlying = (sourceType as INamedTypeSymbol)?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? ((INamedTypeSymbol)sourceType).TypeArguments[0] : sourceType;
            var tgtUnderlying = (targetType as INamedTypeSymbol)?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? ((INamedTypeSymbol)targetType).TypeArguments[0] : targetType;

            if (srcUnderlying.Equals(tgtUnderlying, SymbolEqualityComparer.Default)) return true;

            for (var t = srcUnderlying; t != null; t = t.BaseType)
            {
                if (t.Equals(tgtUnderlying, SymbolEqualityComparer.Default)) return true;
            }

            // 接口继承链：检查源接口是否继承自目标类型
            if (srcUnderlying is INamedTypeSymbol srcNamed && srcNamed.TypeKind == TypeKind.Interface)
            {
                foreach (var iface in srcNamed.AllInterfaces)
                {
                    if (iface.Equals(tgtUnderlying, SymbolEqualityComparer.Default)) return true;
                }
            }

            if (IsImplicitConvertible(srcUnderlying, tgtUnderlying)) return true;

            if (srcUnderlying.TypeKind == TypeKind.Enum || tgtUnderlying.TypeKind == TypeKind.Enum) return true;

            if (srcUnderlying.SpecialType == SpecialType.System_String || tgtUnderlying.SpecialType == SpecialType.System_String) return true;
            if (srcUnderlying.ToDisplayString() == "System.Guid" || tgtUnderlying.ToDisplayString() == "System.Guid") return true;

            if (srcUnderlying.TypeKind == TypeKind.Class && tgtUnderlying.TypeKind == TypeKind.Class
                && srcUnderlying is INamedTypeSymbol srcN && tgtUnderlying is INamedTypeSymbol tgtN
                && srcN.IsGenericType && tgtN.IsGenericType)
            {
                var srcDef = srcN.OriginalDefinition;
                var tgtDef = tgtN.OriginalDefinition;
                if (srcDef.Equals(tgtDef, SymbolEqualityComparer.Default)) return true;
            }

            // （真实项目暴露，与生成分发链一致性修复）：集合↔集合 / 字典↔字典
            // 存在元素级转换路径（TryGetCollectionElementTypes / TryGetDictionaryKeyValueTypes）即视为兼容——
            // CleanArchitecture TodoListDto.Items：IList<TodoItem> → IReadOnlyCollection<TodoItemDto>
            // 可元素级映射（TodoItem→TodoItemDto），原判定误报 NCMAP002（Warning）→ TreatWarningsAsErrors
            // 项目构建失败。与 GenerateSinglePropertyAssignment 分发链的集合/字典分支对齐。
            if (TypeHelpers.TryGetCollectionElementTypes(sourceType, targetType, out _, out _))
                return true;
            if (TypeHelpers.TryGetDictionaryKeyValueTypes(sourceType, targetType, out _, out _, out _, out _))
                return true;

            return false;
        }

        /// <summary>
        /// 判断源类型到目标类型是否可隐式转换 — 转发到TypeHelpers
        /// </summary>
        /// <param name="sourceType">源类型</param>
        /// <param name="targetType">目标类型</param>
        /// <returns>如果可隐式转换则返回true</returns>
        // IsImplicitConvertible 已提取到 TypeHelpers，此处直接转发
        private static bool IsImplicitConvertible(ITypeSymbol sourceType, ITypeSymbol targetType)
            => TypeHelpers.IsImplicitConvertible(sourceType, targetType);

        /// <summary>
        /// 路径映射（推导扁平化/显式点路径）的 [Mask]/[Convert] 提取与验证
        /// <para>与直接属性分支同构：Mask 验证目标 string + 实现 IMask；Convert 经生成端解析器
        /// 解析方法引用（TField 取路径叶属性类型）。验证失败输出对应诊断且不填充。
        /// Convert 对侧消费：锚点无 [Convert] 时检查叶属性标注（与直接分支 X1-1b 同构）。</para>
        /// </summary>
        /// <param name="info">映射信息（诊断收集）</param>
        /// <param name="prop">目标属性</param>
        /// <param name="leafProp">路径叶属性（源侧，对侧 [Convert] 消费用）</param>
        /// <param name="sourceEntityType">源实体类型</param>
        /// <param name="pathHasIndex">路径是否含集合索引段— 索引路径时
        /// TField 校验用集合元素类型（转换器输入 = ElementAtOrDefault 结果 = 元素类型）</param>
        /// <param name="maskType">提取成功的 [Mask] 类型；null=无标注或验证失败</param>
        /// <param name="converterMethod">方法引用形态解析命中的方法符号；null=无标注/解析失败</param>
        /// <param name="converterShape">方法引用形态命中的签名模板；None=未命中</param>
        private static void ExtractMaskAndConverterForPath(
            MappingInfo info, IPropertySymbol prop, IPropertySymbol leafProp, INamedTypeSymbol sourceEntityType,
            bool pathHasIndex,
            out INamedTypeSymbol? maskType,
            out IMethodSymbol? converterMethod, out ConverterShape converterShape)
        {
            maskType = null;
            converterMethod = null;
            converterShape = ConverterShape.None;

            // [Mask] 脱敏（与直接属性分支同构：目标 string + IMask 验证）
            var maskAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Mask);
            if (maskAttr != null && maskAttr.ConstructorArguments.Length >= 1)
            {
                var maskTypeRaw = maskAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
                if (maskTypeRaw != null)
                {
                    if (prop.Type.SpecialType != SpecialType.System_String)
                    {
                        info.Diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptors.MaskTargetNotStringRule,
                            Args = new object[] { prop.Name, prop.Type.ToDisplayString() },
                            Location = prop.Locations.FirstOrDefault()
                        });
                    }
                    else
                    {
                        var implementsIMask = maskTypeRaw.AllInterfaces.Any(i =>
                            i.ToDisplayString() == "NCMapper.IMask");
                        if (!implementsIMask)
                        {
                            info.Diagnostics.Add(new DiagnosticInfo
                            {
                                Descriptor = DiagnosticDescriptors.MaskTypeMismatchRule,
                                Args = new object[] { prop.Name, maskTypeRaw.Name },
                                Location = prop.Locations.FirstOrDefault()
                            });
                        }
                        else
                        {
                            maskType = maskTypeRaw;
                        }
                    }
                }
            }

            // [Convert] 自定义转换器 — 锚点优先，对侧（叶属性）兜底（原则 1：标注位置不影响结果）
            var converterAttr = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Convert);
            var converterAttrProp = prop;
            if (converterAttr == null)
            {
                converterAttr = leafProp.GetAttributes().FirstOrDefault(a =>
                    AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Convert);
                converterAttrProp = leafProp;
            }
            if (converterAttr != null && converterAttr.ConstructorArguments.Length >= 1)
            {
                // 索引路径时有效源类型 = 集合元素类型
                // （转换器输入 = ElementAtOrDefault 结果；集合属性类型会误判 TField）
                var effectiveLeafType = pathHasIndex
                    ? (TryGetCollectionElementType(leafProp.Type) ?? leafProp.Type)
                    : leafProp.Type;

                // 方法引用形态：单参锚定类型 = 携带特性的属性所属类型；索引路径 TField 基准 = 元素类型；
                // 采集侧：解析命中经 out 传出（调用方物化 pm 三字段）
                if (TryGetMethodReference(converterAttr, converterAttrProp, out var mrAnchorType, out var mrMethodName))
                {
                    ResolveConverterMethod(mrAnchorType, mrMethodName, effectiveLeafType, sourceEntityType, prop.Type,
                        prop.Name, prop.Locations.FirstOrDefault(), info.Diagnostics, out converterMethod, out converterShape);
                }
            }

            // [ConvertBack] 校验 — 仅承载侧（DTO 属性 prop）
            // 原"叶属性兜底"支随 实体叶兜底
            // 消费能力退役（删除段）删除——叶侧标注现 100% 不可消费，
            // 校验不可消费的标注属噪音（静默口径与同任务 D3 索引路径先例同族）。
            // 原注释所称"镜像运行时 BuildReversePathWriteBackFromPath 的 sourceProp ?? leaf 查询序"
            // 已失实：该运行时叶兜底已由 T5 退役（ExpressionBuilder.Paths.cs
            // 单查 sourceProp）。
            // 基准三元：TField=prop.Type（DTO 属性类型）、TSource=info.TargetType（DTO 类型）、
            // TTarget=leafProp.Type（实体叶属性类型）——反向转换器两实参恒来自 DTO 侧；
            // 仅产诊断（值版物化经 PropertyRefFromSymbol 独立采集、物化期诊断丢弃——单点产出防重复发射）。
            // 索引路径（pathHasIndex）跳过校验——反向写回对索引路径整体跳过同上。
            if (!pathHasIndex)
            {
                var backConvAttr = prop.GetAttributes().FirstOrDefault(a =>
                    AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.ConvertBack);
                if (backConvAttr != null && backConvAttr.ConstructorArguments.Length >= 1
                    && TryGetMethodReference(backConvAttr, prop, out var bcAnchorType, out var bcMethodName))
                {
                    ResolveConverterMethod(bcAnchorType, bcMethodName, prop.Type, info.TargetType, leafProp.Type,
                        prop.Name, prop.Locations.FirstOrDefault(), info.Diagnostics, out _, out _);
                }
            }
        }

        // =====：特性侧生命周期钩子提取（[BeforeMapping]/[AfterMapping]）=====
        // 镜像 Convert 双 ctor 解析族（R2-R4 同序）；签名适配 D-d——方法签名即适用范围声明。

        /// <summary>
        /// 解析基类 DTO 类型级全部 [MapSubclass] 登记（特性侧—— 方案 1）。
        /// <para>双通道共享：特性通道 ExtractMappingCore（主通道 SubclassPairs 注入 + F46/F47 诊断）
        /// 与 Fluent 式合成管道 ExtractSubclassSynthetics（合成对构造，诊断静默——有锚定类必进
        /// 主通道，F46/F47 由主通道单点发射防双报）。</para>
        /// <para>校验链（与 Fluent 侧 Build 物化同构）：F46 继承（typeof 实参无泛型约束编译期拦截，
        /// 提取期校验—— 定案）→ F47 base-self / duplicate-pair → FQN 去重 → 最派生优先排序
        ///（InheritanceDistance 双轴字典序降序，与 MappingSnapshotBuilder 同口径）。</para>
        /// </summary>
        /// <param name="targetSymbol">基类 DTO 类型符号（[MapSubclass] 锚定宿主，Inherited=false 天然零传播）</param>
        /// <param name="sourceType">基类映射对源类型符号（[MapFrom] 实参——继承校验基准）</param>
        /// <param name="diagnostics">诊断追加目标；null = 静默（合成管道——主通道单点报）</param>
        /// <returns>去重排序后的子类对清单（空 = 无有效登记）</returns>
        internal static List<SubclassPairMapping> ResolveSubclassRegistrations(
            INamedTypeSymbol targetSymbol, INamedTypeSymbol sourceType, List<DiagnosticInfo>? diagnostics)
        {
            var pairs = new List<SubclassPairMapping>();
            var seen = new HashSet<(string, string)>();
            foreach (var attr in targetSymbol.GetAttributes())
            {
                if (AttrSymbolCache.DisplayName(attr.AttributeClass) != AttributeNames.MapSubclass) continue;
                if (attr.ConstructorArguments.Length != 2) continue; // 构造签名不符——编译器已报
                var subSrc = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
                var subTgt = attr.ConstructorArguments[1].Value as INamedTypeSymbol;
                if (subSrc == null || subTgt == null) continue;
                // 特性自身语法位置（ApplicationSyntaxReference——合成场景 null，回退类型首位置）
                var attrLocation = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? targetSymbol.Locations.FirstOrDefault();

                // 继承关系校验（特性形态无泛型约束：F46 专挂特性侧）
                if (!IsDerivedFrom(subSrc, sourceType) || !IsDerivedFrom(subTgt, targetSymbol))
                {
                    diagnostics?.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.MapSubclassAttributeInheritanceRule,
                        Args = new object[]
                        {
                            subSrc.ToDisplayString(), subTgt.ToDisplayString(),
                            targetSymbol.ToDisplayString(), sourceType.ToDisplayString(), targetSymbol.ToDisplayString(),
                        },
                        Location = attrLocation,
                    });
                    continue;
                }
                // F47-② base-self（与 Fluent 侧同条件）
                if (SymbolEqualityComparer.Default.Equals(subSrc, sourceType))
                {
                    diagnostics?.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.SubclassDuplicateOrSelfRule,
                        Args = new object[] { sourceType.ToDisplayString(), targetSymbol.ToDisplayString(), "base-self" },
                        Location = attrLocation,
                    });
                    continue;
                }
                // F47-① duplicate-pair（FQN 去重，首见保留）
                var key = (subSrc.ToDisplayString(), subTgt.ToDisplayString());
                if (!seen.Add(key))
                {
                    diagnostics?.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.SubclassDuplicateOrSelfRule,
                        Args = new object[] { subSrc.ToDisplayString(), subTgt.ToDisplayString(), "duplicate-pair" },
                        Location = attrLocation,
                    });
                    continue;
                }
                pairs.Add(new SubclassPairMapping
                {
                    SourceType = subSrc,
                    TargetType = subTgt,
                    Location = attrLocation,
                });
            }
            // 最派生优先排序（与 MappingSnapshotBuilder.InheritanceDistance 同口径）
            if (pairs.Count > 1)
            {
                pairs.Sort((a, b) =>
                {
                    var sd = MappingSnapshotBuilder.InheritanceDistance(a.SourceType, sourceType);
                    var bd = MappingSnapshotBuilder.InheritanceDistance(b.SourceType, sourceType);
                    if (sd != bd) return bd.CompareTo(sd);
                    var ta = MappingSnapshotBuilder.InheritanceDistance(a.TargetType, targetSymbol);
                    var tb = MappingSnapshotBuilder.InheritanceDistance(b.TargetType, targetSymbol);
                    return tb.CompareTo(ta);
                });
            }
            return pairs;
        }

        /// <summary>：派生判定（BaseType 链可达——F46 继承校验输入；
        /// InheritanceDistance 返回 0 当 derived == base，非 MaxValue 即派生可达）</summary>
        private static bool IsDerivedFrom(INamedTypeSymbol derived, INamedTypeSymbol baseType)
            => MappingSnapshotBuilder.InheritanceDistance(derived, baseType) != int.MaxValue;

        // ===== =====
        // 运行时镜像：ExpressionBuilder.ResolveStaticMethod（落地，逐条同规则零分叉）。

        /// <summary>
        /// 特性形态判别 — 方法引用形态提取锚定类型与方法名
        /// </summary>
        /// <param name="attr">[Convert]/[ConvertBack] 特性数据</param>
        /// <param name="annotatedProp">承载该特性的属性符号（单参形态锚定类型 = 其所属类型，R2）</param>
        /// <param name="anchorType">锚定类型：双参=显式传入；单参=标注属性所属类型</param>
        /// <param name="methodName">方法名（R4 Ordinal 精确匹配）</param>
        /// <returns>true=方法引用形态（双参/单参）；false=异常形态（非双参 Type+string / 单参 string 构造，理论上不可达）</returns>
        private static bool TryGetMethodReference(AttributeData attr, IPropertySymbol annotatedProp,
            out INamedTypeSymbol anchorType, out string methodName)
        {
            anchorType = null!;
            methodName = string.Empty;

            // 双参形态 (Type declaringType, string methodName)：类型显式传递，R7 nameof 限定名截断天然免疫
            if (attr.ConstructorArguments.Length == 2
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol declaringType
                && attr.ConstructorArguments[1].Value is string dualMethodName)
            {
                anchorType = declaringType;
                methodName = dualMethodName;
                return true;
            }

            // 单参形态 (string methodName)：解析域收紧为标注属性所属类型自身（R2/R3；R7 截断在此显性报错）
            if (attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is string singleMethodName
                && annotatedProp.ContainingType != null)
            {
                anchorType = annotatedProp.ContainingType;
                methodName = singleMethodName;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 转换方法引用三字段物化— 解析命中的方法符号 + 形态写入模型三字段
        /// <para>FQN 统一取 <c>ContainingType.ToDisplayString()</c>（含命名空间/嵌套/封闭泛型实参，
        /// 发射端拼 <c>global::FQN.Method(args)</c>）；method=null（解析失败，诊断已产出）时不动模型。</para>
        /// <para>：internal 化供 Fluent 通道 ReverseOnly 条目构建复用
        ///（MappingSnapshotBuilder.BuildReverseOnlyEntry——mutate 签名零变化，同程序集可见性放宽）。</para>
        /// </summary>
        internal static void ApplyConverterMethodReference(PropertyMapping pm, IMethodSymbol? method, ConverterShape shape)
        {
            if (method == null) return;
            pm.ConverterDeclaringType = method.ContainingType.ToDisplayString();
            pm.ConverterMethodName = method.Name;
            pm.ConverterShape = shape;
        }

        /// <summary>
        /// 转换方法引用三字段物化— Compose 跨表映射重载（与<see cref="ApplyConverterMethodReference(PropertyMapping, IMethodSymbol?, ConverterShape)"/>同口径）
        /// </summary>
        private static void ApplyConverterMethodReference(ComposePropertyMapping cm, IMethodSymbol? method, ConverterShape shape)
        {
            if (method == null) return;
            cm.ConverterDeclaringType = method.ContainingType.ToDisplayString();
            cm.ConverterMethodName = method.Name;
            cm.ConverterShape = shape;
        }

        /// <summary>
        /// [Convert] 无匹配源属性 + [Mask] 共存检测（提取共享）— 方法引用形态
        /// pmForNull 对称复用：目标非 string 报 NCMAP017、掩码类未实现 IMask 报 NCMAP016，否则填充 MaskType
        /// </summary>
        private static void ApplyMaskForNullSource(MappingInfo info, IPropertySymbol prop, PropertyMapping pmForNull)
        {
            var maskAttrForNull = prop.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Mask);
            if (maskAttrForNull == null || maskAttrForNull.ConstructorArguments.Length < 1) return;
            var maskTypeForNull = maskAttrForNull.ConstructorArguments[0].Value as INamedTypeSymbol;
            if (maskTypeForNull == null) return;

            if (prop.Type.SpecialType != SpecialType.System_String)
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MaskTargetNotStringRule,
                    Args = new object[] { prop.Name, prop.Type.ToDisplayString() },
                    Location = prop.Locations.FirstOrDefault()
                });
                return;
            }

            var implementsIMask = maskTypeForNull.AllInterfaces.Any(i => i.ToDisplayString() == "NCMapper.IMask");
            if (!implementsIMask)
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MaskTypeMismatchRule,
                    Args = new object[] { prop.Name, maskTypeForNull.Name },
                    Location = prop.Locations.FirstOrDefault()
                });
                return;
            }

            pmForNull.MaskType = maskTypeForNull;
        }

        /// <summary>
        /// 生成端转换方法解析器（Roslyn 侧实现）
        /// <para>R2 锚定类型 → R3 DeclaredOnly+可访问性白名单 → R4 名称 Ordinal → R5 形态排除 →
        /// R6 四模板+Q3 优先序+相等匹配 → R7 截断处置（Q5 降级：仅 NCMAP035 指引文案，不做语法回看）。</para>
        /// <para>失败时向 diagnostics 追加 NCMAP035（不可达）/NCMAP036（重载歧义）/NCMAP037（签名不匹配）。</para>
        /// </summary>
        /// <param name="sourceFieldType">源字段类型基准（相等匹配）；null = 无匹配源属性路径（R6-b：仅形态匹配，不做字段类型交叉校验）</param>
        /// <param name="sourceEntityType">源实体类型基准（反向 [ConvertBack] 时 = DTO 类型，004 定稿）</param>
        /// <param name="targetType">目标属性类型基准（反向时 = 实体属性类型）</param>
        /// <returns>true=恰一重载命中；false=已追加诊断</returns>
        private static bool ResolveConverterMethod(
            INamedTypeSymbol anchorType,
            string methodName,
            ITypeSymbol? sourceFieldType,
            INamedTypeSymbol sourceEntityType,
            ITypeSymbol targetType,
            string propertyName,
            Location? location,
            List<DiagnosticInfo> diagnostics,
            out IMethodSymbol? method,
            out ConverterShape shape)
        {
            method = null;
            shape = ConverterShape.None;
            var anchorName = anchorType.Name;

            // R2 细则 4：开放泛型锚定类型——方法参数含类型参数必然失配（单参形态落在泛型 DTO 定义上、双参 typeof(X<>)）
            // typeof(X<>) 经 AttributeData 传入的是未绑定泛型符号：IsUnboundGenericType=true、IsDefinition=false、
            // TypeArguments 为 Error 类型且 GetMembers() 为空——必须显式判 IsUnboundGenericType，否则误报 NCMAP035
            if (anchorType.IsGenericType
                && (anchorType.IsUnboundGenericType
                    || anchorType.IsDefinition
                    || anchorType.TypeArguments.Any(t => t.TypeKind == TypeKind.TypeParameter)))
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.ConverterMethodSignatureMismatchRule,
                    Args = new object[] { propertyName, methodName, anchorName, targetType.ToDisplayString(),
                        "anchor type is an open generic type; use the two-argument form with a closed generic type (e.g. typeof(X<int>))" },
                    Location = location
                });
                return false;
            }

            // R3/锚定类型自身声明的静态方法（GetMembers 不含基类继承 = DeclaredOnly；partial 天然合并），名称 Ordinal 精确匹配
            var sameName = new List<IMethodSymbol>();
            foreach (var member in anchorType.GetMembers())
            {
                if (member is IMethodSymbol m && m.IsStatic
                    && string.Equals(m.Name, methodName, StringComparison.Ordinal))
                {
                    sameName.Add(m);
                }
            }

            // NCMAP035 判据①：锚定类型上无该名称静态方法（名称不存在 / 仅有实例方法）
            if (sameName.Count == 0)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.ConverterMethodInaccessibleRule,
                    Args = new object[] { propertyName, methodName, anchorName },
                    Location = location
                });
                return false;
            }

            // R3 可访问性白名单：public / internal（同程序集）/ protected internal（同程序集腿）；
            // protected / private protected / private 拒绝。显式判定（不用 IsAccessibleWithin——尊重 IVT 会与反射口径分歧）
            var accessible = new List<IMethodSymbol>();
            foreach (var m in sameName)
            {
                if (m.DeclaredAccessibility == Accessibility.Public
                    || ((m.DeclaredAccessibility == Accessibility.Internal || m.DeclaredAccessibility == Accessibility.ProtectedOrInternal)
                        && SymbolEqualityComparer.Default.Equals(m.ContainingAssembly, anchorType.ContainingAssembly)))
                {
                    accessible.Add(m);
                }
            }

            // NCMAP035 判据②：存在同名静态方法但可见性不足（与名称不存在同码，消息覆盖两种理由）
            if (accessible.Count == 0)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.ConverterMethodInaccessibleRule,
                    Args = new object[] { propertyName, methodName, anchorName },
                    Location = location
                });
                return false;
            }

            // R5 形态排除（静默；NCMAP037 细节逐一登记）+ R6 模板匹配 + 守卫变体编码
            //（per-candidate 循环体与守卫后置块抽取为 method-symbol 直入重载，
            // 壳内转调——语句序列原样搬移，行为零变化）
            var matches = new List<IMethodSymbol>();
            var matchedShapes = new List<ConverterShape>();
            var exclusionNotes = new List<string>();
            foreach (var candidate in accessible)
            {
                if (ResolveConverterMethod(candidate, sourceFieldType, sourceEntityType, targetType,
                        out var candidateShape, out var note))
                {
                    matches.Add(candidate);
                    matchedShapes.Add(candidateShape);
                }
                else
                {
                    exclusionNotes.Add(note!);
                }
            }

            if (matches.Count == 0)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.ConverterMethodSignatureMismatchRule,
                    Args = new object[] { propertyName, methodName, anchorName, targetType.ToDisplayString(),
                        string.Join("; ", exclusionNotes) },
                    Location = location
                });
                return false;
            }

            if (matches.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.ConverterMethodAmbiguousRule,
                    Args = new object[] { propertyName, methodName, anchorName },
                    Location = location
                });
                return false;
            }

            method = matches[0];
            shape = matchedShapes[0];
            return true;
        }

        /// <summary>
        /// 转换方法解析器 method-symbol 直入重载（抽取——纯移动）
        /// 单方法 R5 形态排除 + R6 模板匹配 + Nullable 守卫变体编码。
        /// <para>语句序列原样搬移自 (anchorType, methodName) 壳的 per-candidate 循环体与守卫后置块
        ///（守卫编码为 (method, sourceFieldType, shape) 纯函数——per-candidate 调用下对 matches[0]
        /// 的编码结果与壳内后置编码逐值等价，多候选 NCMAP036 路径无副作用差异）；壳内循环改为转调本重载。</para>
        /// <para>消费方：特性壳（既有全部 [Convert]/[ConvertBack] 解析路径）+
        /// Fluent ConvertBack（FluentExtractor.ResolveConvertBackMethod——方法组绑定产物单方法直入，
        /// 歧义在编译期由 CS0121 承担，无多候选枚举面）。</para>
        /// </summary>
        /// <param name="method">已解析的静态方法符号（方法组绑定/锚定类型候选）</param>
        /// <param name="sourceFieldType">源字段类型基准（相等匹配）；null = 无字段类型交叉校验（R6-b）</param>
        /// <param name="sourceEntityType">源实体类型基准</param>
        /// <param name="targetType">目标属性类型基准</param>
        /// <param name="shape">命中形态（含守卫变体编码产物）</param>
        /// <param name="exclusionNote">未命中原因（R5 排除/R6 失配——NCMAP037 消息素材）；命中时为 null</param>
        /// <returns>true=命中；false=未命中（exclusionNote 非空）</returns>
        internal static bool ResolveConverterMethod(
            IMethodSymbol method,
            ITypeSymbol? sourceFieldType,
            INamedTypeSymbol sourceEntityType,
            ITypeSymbol targetType,
            out ConverterShape shape,
            out string? exclusionNote)
        {
            shape = ConverterShape.None;
            exclusionNote = null;
            var signature = $"{method.Name}({string.Join(", ", method.Parameters.Select(p => p.Type.ToDisplayString()))})";

            if (method.TypeParameters.Length > 0)
            {
                exclusionNote = $"{signature}: generic method";
                return false;
            }
            if (method.Parameters.Any(p => p.HasExplicitDefaultValue))
            {
                exclusionNote = $"{signature}: optional parameter";
                return false;
            }
            if (method.Parameters.Length > 0 && method.Parameters[method.Parameters.Length - 1].IsParams)
            {
                exclusionNote = $"{signature}: params array";
                return false;
            }
            if (method.Parameters.Any(p => p.RefKind != RefKind.None))
            {
                exclusionNote = $"{signature}: ref/out/in parameter";
                return false;
            }

            if (!TryMatchConverterTemplate(method, sourceFieldType, sourceEntityType, targetType, out shape))
            {
                exclusionNote = $"{signature}: signature mismatch (return '{method.ReturnType.ToDisplayString()}')";
                return false;
            }

            // 发射端：Nullable 守卫形态编码（R6 口径 3 守卫行）——字段基准 Nullable<T>且
            // 字段参数为非可空值类型 T → 守卫变体（发射端生成「字段.HasValue ? M(字段.Value) : default」
            // 三元：null 不调用、目标保持默认， null 语义表；参数同为 Nullable<T> 时直传）
            if (sourceFieldType != null
                && (shape == ConverterShape.T1SingleValue || shape == ConverterShape.T3Combined)
                && TryUnwrapNullable(sourceFieldType, out _))
            {
                var fieldParam = shape == ConverterShape.T1SingleValue ? method.Parameters[0] : method.Parameters[1];
                if (fieldParam.Type.IsValueType && !TryUnwrapNullable(fieldParam.Type, out _))
                    shape = shape == ConverterShape.T1SingleValue ? ConverterShape.T1Guarded : ConverterShape.T3Guarded;
            }
            return true;
        }

        /// <summary>
        /// R6 四模板匹配 + Q3 优先序 + 相等匹配（Nullable 解包后完全相等，不做 assignable 放宽）
        /// </summary>
        private static bool TryMatchConverterTemplate(
            IMethodSymbol candidate,
            ITypeSymbol? sourceFieldType,
            INamedTypeSymbol sourceEntityType,
            ITypeSymbol targetType,
            out ConverterShape shape)
        {
            shape = ConverterShape.None;

            // 返回类型口径：解包后 == 目标属性类型；void 拒绝；提升只允许「非空→可空」
            if (!ConverterReturnTypeMatches(candidate.ReturnType, targetType))
                return false;

            var ps = candidate.Parameters;

            // static TTarget M(TSource) —— Q3 优先序：单参同时==源实体与源字段类型时判多字段
            if (ps.Length == 1 && TypeEqualsUnwrapped(ps[0].Type, sourceEntityType))
            {
                shape = ConverterShape.T2MultiField;
                return true;
            }

            // static TTarget M(TSource, TField)；R6-b（fieldType=null）不做字段类型交叉校验
            if (ps.Length == 2
                && TypeEqualsUnwrapped(ps[0].Type, sourceEntityType)
                && (sourceFieldType == null || TypeEqualsUnwrapped(ps[1].Type, sourceFieldType)))
            {
                shape = ConverterShape.T3Combined;
                return true;
            }

            // static TTarget M(TField)；R6-b（fieldType=null）任意单参形态命中（字段值实参一律 default）
            if (ps.Length == 1 && (sourceFieldType == null || TypeEqualsUnwrapped(ps[0].Type, sourceFieldType)))
            {
                shape = ConverterShape.T1SingleValue;
                return true;
            }

            // static TTarget M()
            if (ps.Length == 0)
            {
                shape = ConverterShape.T0Constant;
                return true;
            }

            return false; // 参数 ≥3 一律不匹配（NCMAP037）
        }

        /// <summary>
        /// 返回类型匹配（R6 口径 2）：解包后相等；void 拒绝；提升只允许「非空→可空」
        /// <para>（M 返回 DateTime、目标 DateTime? 允许；返回 DateTime?、目标 DateTime → 不匹配）</para>
        /// </summary>
        private static bool ConverterReturnTypeMatches(ITypeSymbol returnType, ITypeSymbol targetType)
        {
            if (returnType.SpecialType == SpecialType.System_Void)
                return false;

            var retIsNullable = TryUnwrapNullable(returnType, out var retUnderlying);
            var tgtIsNullable = TryUnwrapNullable(targetType, out var tgtUnderlying);

            if (retIsNullable && !tgtIsNullable)
                return false;

            return SymbolEqualityComparer.Default.Equals(retUnderlying, tgtUnderlying);
        }

        /// <summary>
        /// 相等匹配（R6 口径 1）：两侧 Nullable&lt;T&gt; 解包后完全相等；引用类型可空标注不参与（仅解值类型 Nullable）
        /// </summary>
        private static bool TypeEqualsUnwrapped(ITypeSymbol a, ITypeSymbol b)
            => SymbolEqualityComparer.Default.Equals(UnwrapNullableType(a), UnwrapNullableType(b));

        private static ITypeSymbol UnwrapNullableType(ITypeSymbol type)
            => TryUnwrapNullable(type, out var underlying) ? underlying : type;

        private static bool TryUnwrapNullable(ITypeSymbol type, out ITypeSymbol underlying)
        {
            if (type is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                underlying = named.TypeArguments[0];
                return true;
            }
            underlying = type;
            return false;
        }

        /// <summary>
        /// 检测源属性未被任何目标属性映射 — NCMAP007（Info 级，对标 RMG020）
        /// </summary>
        /// <param name="info">映射信息</param>
        /// <param name="sourceType">主源类型</param>
        private static void DetectUnmappedSourceMembers(MappingInfo info, INamedTypeSymbol sourceType)
        {
            // （语义级抑制）：白名单对不产出 NCMAP007
            // 白名单意图 = "未声明的源属性是有意为之"，与 Fluent 通道零产出对齐（A-25 等价 fixture
            // 实测揭示：抑制仅置于 GlobalMapConfig 后处理管线时，无类级/程序集级 [NCMapperOptions]
            // 的默认形态漏抑制——B-6 下 007 无条件检测、输出期按选项过滤，故抑制点须在检测入口）。
            // SnapshotConfig 管线内的同名单臂保留（覆盖后处理重建快照的形态）。
            if (info.NoAutoInclude) return;

                        // 无条件检测 NCMAP007——报告与否由输出期按注入后选项过滤

            // 收集所有已映射的源属性
            var mappedSourceProps = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var pm in info.PropertyMappings)
            {
                if (pm.SourceProperty != null)
                    mappedSourceProps.Add(pm.SourceProperty);
                // 多段路径上的中间导航属性也标记为已映射，避免虚假 NCMAP007（改键）
                if (pm.SourcePath is { Count: > 1 })
                    foreach (var pathProp in pm.SourcePath)
                        mappedSourceProps.Add(pathProp);
            }

            // 遍历源类型属性（含继承接口），检查是否有未被映射的
            foreach (var t in GetTypeAndBaseTypes(sourceType))
            {
                foreach (var prop in t.GetMembers().OfType<IPropertySymbol>())
                {
                    if (prop.IsStatic || prop.IsIndexer) continue;
                    if (prop.GetMethod == null || prop.GetMethod.DeclaredAccessibility != Accessibility.Public) continue;

                    // 跳过已映射的
                    if (mappedSourceProps.Contains(prop)) continue;

                    // 跳过标注了 [MapIgnore] 的源属性
                    if (HasAttribute(prop, AttributeNames.MapIgnore)) continue;

                    // 跳过基类的基础属性（object 类型成员）
                    if (prop.ContainingType?.SpecialType == SpecialType.System_Object) continue;

                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptors.UnmappedSourceMemberRule,
                        Args = new object[] { sourceType.Name, prop.Name, prop.Type.Name },
                        Location = prop.Locations.FirstOrDefault()
                    });
                }
            }
        }

        /// <summary>
        /// 实体侧 [ConvertBack] 反向无源扫描— 遍历实体
        /// （sourceType）public 实例属性，未被任何正向 pm 消费且带 [ConvertBack] 标注者
        /// 物化为 ReverseOnlyConvertBacks 条目（TargetProperty=实体属性、SourceProperty=null!、
        /// 三元组=反向转换器方法引用）。
        /// <para>运行时镜像：ExpressionBuilder.BuildMapBody 无源块前置分支（sourceItem == null
        /// 且实体属性标 [ConvertBack] 时暂挂 + 写回链后发射，T2/T3 模板 TSource 实参=整个 DTO 对象）；
        /// 双引擎门互补镜像：本扫描的排除集对应运行时 coveredSegments（写回链覆盖排除）。</para>
        /// <para>消费端：ReversePlanner.CreateReverseMapping（追加于反向主 foreach 之后、
        /// 计数门之前——追加序与运行时 EmitPendingBackConverters 于写回链后发射对齐）。</para>
        /// </summary>
        /// <param name="info">映射信息（结果追加到此对象）</param>
        /// <param name="sourceType">主源类型（实体——[MapFrom].FromSource）</param>
        /// <param name="targetSymbol">目标类型（DTO——[MapFrom] 标注类）</param>
        private static void DetectReverseOnlyConvertBackMappings(MappingInfo info, INamedTypeSymbol sourceType, INamedTypeSymbol targetSymbol)
        {
            // 预筛（性能）：实体属性存在 [ConvertBack] 标注才继续——无标注类型对零成本
            //（仅特性查询，不建覆盖集、不解析；与 Detect* 族同量级）
            List<IPropertySymbol>? candidates = null;
            foreach (var prop in GetTypeAndBaseTypes(sourceType)
                .SelectMany(t => t.GetMembers().OfType<IPropertySymbol>())
                .GroupBy(p => p.Name).Select(g => g.First()))
            {
                if (prop.IsStatic || prop.IsIndexer) continue;
                var backAttr = prop.GetAttributes().FirstOrDefault(a =>
                    AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.ConvertBack);
                if (backAttr == null || backAttr.ConstructorArguments.Length < 1) continue;
                (candidates ??= new List<IPropertySymbol>()).Add(prop);
            }
            if (candidates == null) return;

            // 覆盖排除集（v1.1/）：各正向 pm 的 SourceProperty ∪ SourcePath 全段（含中间段）。
            // 匹配对属性零重复入集（匹配对实体侧标注由 反向 pm 三元组兜底路径消费，与本集合互斥）；
            // 路径段（中间段/叶段）静默排除、写回链优先——与正向「路径场景不进无源分支」先例同形
            //（ProcessSameSourceMapping :371-383「本分支仅处理真·无源属性」）；不新增诊断。
            // 运行时镜像：BuildReversePathWriteBacks coveredSegments 段集。
            var covered = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var pm in info.PropertyMappings)
            {
                if (pm.SourceProperty != null) covered.Add(pm.SourceProperty);
                if (pm.SourcePath is { Count: > 1 })
                    foreach (var seg in pm.SourcePath)
                        covered.Add(seg);
            }

            foreach (var prop in candidates)
            {
                // 静默排除：已被正向 pm 消费（含写回路径段）——写回链优先，无双赋值
                if (covered.Contains(prop)) continue;

                // [MapIgnore] 方向门（运行时 GetPropertyList 目标属性枚举过滤镜像：
                // ShouldIgnoreAttributeOnly(x, isToDto: false)——Both/ToEntity 即排除，
                // 双引擎枚举口径一致；静默跳过）
                if (ShouldIgnoreInDirection(prop, toDto: false)) continue;

                // setter 门 + 解析委托 + 组装收敛至 TryAppendReverseOnlyEntry（
                // 门序 = setter 门 → 解析委托 → 组装——/， B9 禁止倒置）
                // 三元组解析封装为委托（setter 门通过后才调用——门先于解析，"setter 不可写 + 引用无效"场景诊断不漂移）：
                //   TryGetMethodReference 失败静默返回 null；ResolveConverterMethod 失败自发 NCMAP035/036/037 后返回 null
                // 三元组解析（与正向 pmForNull 同构）：TField=null（无源——R6-b 仅形态匹配，
                // 不做字段类型交叉校验）、TSource=DTO 类型（targetSymbol）、TTarget=实体属性类型（prop.Type）；
                // 解析失败诊断由 ResolveConverterMethod 既有 NCMAP035/036/037 承担（与 DTO 侧同源文案）
                //（委托元组 Method 位可空化—— B 形态占位兼容；特性侧恒非 null）
                Func<(IMethodSymbol? Method, ConverterShape Shape)?> resolveConverter = () =>
                {
                    var backConvAttr = prop.GetAttributes().First(a =>
                        AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.ConvertBack);
                    if (!TryGetMethodReference(backConvAttr, prop, out var anchorType, out var methodName)) return null;
                    if (!ResolveConverterMethod(anchorType, methodName, null, targetSymbol, prop.Type,
                            prop.Name, prop.Locations.FirstOrDefault(), info.Diagnostics, out var method, out var shape))
                        return null;
                    return (method!, shape);
                };
                TryAppendReverseOnlyEntry(info, prop, sourceType, targetSymbol, resolveConverter);
            }
        }

        /// <summary>
        /// 检测多个目标属性的 [MapProperty] 指向同一源访问路径 — NCMAP009（Warning 级，对标 RMG074）
        /// <para>判重键为完整源访问路径（属性名序列 + 索引序列，如 "Items[0].Name"）而非仅叶属性符号：
        /// 索引路径下 Items[0].Name 与 Items[1].Name 叶符号相同但访问不同集合元素，
        /// 仅按叶符号判重会误报；路径逐段相等（含索引值）方构成重复。</para>
        /// </summary>
        /// <param name="info">映射信息</param>
        /// <param name="sourceType">主源类型</param>
        private static void DetectDuplicateFieldMappings(MappingInfo info, INamedTypeSymbol sourceType)
        {
            // 按完整源访问路径分组，检测重复（值含叶符号供诊断 Location 定位）
            var sourcePathGroups = new Dictionary<string, (IPropertySymbol Leaf, List<string> Targets)>();

            foreach (var pm in info.PropertyMappings)
            {
                // 推导扁平化豁免（自动行为重复无意义）；显式（Direct/ExplicitDotPath）参与重复检测
                // 过渡期显式点路径 IsFlattened=true，仅靠 IsFlattened 无法区分推导/显式，须 PathOrigin 显式参与判据
                if (pm.SourceProperty == null || pm.PathOrigin == PathOrigin.DerivedFlatten) continue;

                var pathKey = GetDuplicateDetectionPathKey(pm);
                if (!sourcePathGroups.TryGetValue(pathKey, out var entry))
                {
                    entry = (pm.SourceProperty, new List<string>());
                    sourcePathGroups[pathKey] = entry;
                }

                entry.Targets.Add(pm.TargetProperty.Name);
            }

            foreach (var kvp in sourcePathGroups)
            {
                if (kvp.Value.Targets.Count > 1)
                {
                    for (int i = 1; i < kvp.Value.Targets.Count; i++)
                    {
                        info.Diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptors.DuplicateFieldMappingRule,
                            Args = new object[] { kvp.Value.Targets[0], kvp.Value.Targets[i], sourceType.Name, kvp.Key },
                            Location = kvp.Value.Leaf.Locations.FirstOrDefault()
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 获取 NCMAP009 判重用的完整源访问路径键
        /// <para>格式与报告端路径显示一致："Items[0].Name"（属性段以 '.' 连接，索引段带 [n] 后缀）；
        /// 单段无索引仅返回属性名（与既有消息措辞兼容）。</para>
        /// <para>同一源类型下属性名序列唯一确定符号序列（类型内属性名唯一），字符串键与符号序列判重等价。</para>
        /// </summary>
        /// <param name="pm">属性映射</param>
        /// <returns>完整源访问路径键</returns>
        private static string GetDuplicateDetectionPathKey(PropertyMapping pm)
        {
            // SourcePath 写入端归一恒非空（Direct 单段 / 显式点路径多段）；SourceIndexes 与之等长（仅显式路径写入）
            if (pm.SourcePath.Count == 1 && !pm.HasIndex)
                return pm.SourcePath[0].Name;

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < pm.SourcePath.Count; i++)
            {
                if (i > 0) sb.Append('.');
                sb.Append(pm.SourcePath[i].Name);
                if (pm.SourceIndexes.Count > i && pm.SourceIndexes[i] is int n)
                    sb.Append($"[{n}]");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 检测枚举↔枚举映射时成员名称无重叠 — NCMAP010（Warning 级，对标 RMG003/037）
        /// </summary>
        /// <param name="info">映射信息</param>
        private static void DetectEnumNoOverlap(MappingInfo info)
        {
            foreach (var pm in info.PropertyMappings)
            {
                // 跳过纯转换器属性（无匹配源属性），SourceProperty 为 null
                if (pm.SourceProperty == null) continue;

                var srcUnderlying = (pm.SourceProperty.Type as INamedTypeSymbol)?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                    ? ((INamedTypeSymbol)pm.SourceProperty.Type).TypeArguments[0] : pm.SourceProperty.Type;
                var tgtUnderlying = (pm.TargetProperty.Type as INamedTypeSymbol)?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                    ? ((INamedTypeSymbol)pm.TargetProperty.Type).TypeArguments[0] : pm.TargetProperty.Type;

                // 只检测枚举↔枚举
                if (srcUnderlying.TypeKind != TypeKind.Enum || tgtUnderlying.TypeKind != TypeKind.Enum) continue;
                if (SymbolEqualityComparer.Default.Equals(srcUnderlying, tgtUnderlying)) continue;

                // 收集成员名称
                // 过滤编译器合成的 value__ 字段（ConstantValue == null），否则 hasOverlap 恒 true
                var srcNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (srcUnderlying is INamedTypeSymbol srcEnum)
                    foreach (var member in srcEnum.GetMembers().OfType<IFieldSymbol>().Where(f => f.ConstantValue != null))
                        srcNames.Add(member.Name);

                var tgtNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (tgtUnderlying is INamedTypeSymbol tgtEnum)
                    foreach (var member in tgtEnum.GetMembers().OfType<IFieldSymbol>().Where(f => f.ConstantValue != null))
                        tgtNames.Add(member.Name);

                // 检查是否有重叠
                bool hasOverlap = false;
                foreach (var name in srcNames)
                {
                    if (tgtNames.Contains(name))
                    {
                        hasOverlap = true;
                        break;
                    }
                }

                if (!hasOverlap)
                {
                    info.Diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptors.EnumNoOverlapRule,
                        Args = new object[] { srcUnderlying.Name, tgtUnderlying.Name },
                        Location = pm.TargetProperty.Locations.FirstOrDefault()
                    });
                }
            }
        }

        /// <summary>
        /// NCMAP026 检测：[Mask] 标注位置非规范（源侧）— 仅诊断不消费
        /// <para>触发条件：[Mask] 标注所在类 ≠ [MapFrom] 标注类（Mask 标在源侧 = 非规范位置）。
        /// Mask 语义单向（目标 string 脱敏，MaskProvider），源侧标注不生效（方案语义点②a），
        /// 输出 Warning 提示改规范位置（移至目标侧，与 [MapFrom] 同侧）。
        /// 互指对（双侧均有 [MapFrom] 且互指）时由 NCMAP025（Error）优先，本诊断不叠加。</para>
        /// </summary>
        /// <param name="info">映射信息（诊断结果追加到此对象）</param>
        /// <param name="sourceType">源类型（[MapFrom] 构造参数，可能带源侧 [Mask] 标注）</param>
        /// <param name="targetSymbol">目标类型（[MapFrom] 标注类）</param>
        private static void DetectMaskPositionMismatch(MappingInfo info, INamedTypeSymbol sourceType, INamedTypeSymbol targetSymbol)
        {
            // 互指对检测——源类型也标了 [MapFrom(targetSymbol)] 即构成互指，由 NCMAP025 处理，本诊断不叠加
            // 单向指向判定收敛至共享规则（-1，NCMapper.Shared.MutualMapPairRules）
            var sourceMapFrom = sourceType.GetAttributes().FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapFrom);
            if (Shared.MutualMapPairRules.PointsToTarget(
                    sourceMapFrom?.ConstructorArguments.Length >= 1,
                    sourceMapFrom?.ConstructorArguments.Length >= 1
                        && sourceMapFrom.ConstructorArguments[0].Value is INamedTypeSymbol sourceMapFromTarget
                        && SymbolEqualityComparer.Default.Equals(sourceMapFromTarget, targetSymbol)))
            {
                return;
            }

            // 遍历源类型属性，检测带 [Mask] 的源侧标注
            foreach (var sp in GetTypeAndBaseTypes(sourceType)
                .SelectMany(t => t.GetMembers().OfType<IPropertySymbol>()))
            {
                if (sp.IsStatic || sp.IsIndexer) continue;

                var maskAttr = sp.GetAttributes().FirstOrDefault(a =>
                    AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Mask);
                if (maskAttr == null) continue;

                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MaskPositionRule,
                    Args = new object[] { sp.Name },
                    Location = sp.Locations.FirstOrDefault()
                });
            }
        }

        /// <summary>
        /// NCMAP019 谓词：检测类型是否缺少 [MapFrom]
        /// <para>两种触发场景：①标注了映射特性（[MapProperty]/[Convert]/[Mask]）但未标 [MapFrom]；
        /// ②DTO 命名模式（类型名以 Dto/DTO 结尾）且未标 [MapFrom]——即使完全未标任何特性，
        /// 运行时映射将走反射回退路径（AOT 下不可用），同样应提示。</para>
        /// </summary>
        /// <param name="node">语法节点</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>true 表示可能触发 NCMAP019</returns>
        public static bool HasMappingAttributesWithoutMapFrom(SyntaxNode node, CancellationToken ct)
        {
            if (node is not TypeDeclarationSyntax typeDecl)
                return false;

            // MapFrom 快速否定保留（语法级子串——多数类型带 [MapFrom]，提前短路省 transform；
            // 映射特性判定精度已全部下沉 transform 端符号级精判）
            foreach (var attrList in typeDecl.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    if (attr.Name.ToString().Contains("MapFrom"))
                        return false;
                }
            }

            // 宽前筛 = 类级含任意特性 ∨ 任一成员含特性 ∨
            // DTO 命名——缺陷态：MapProperty/ConvertUsing/Mask 子串只扫类级（属性级映射特性
            // [ConvertBack]/[Mask]/[MapIgnore]/[MapWhen] 等全部漏报）；MapProperty/ConvertUsing/
            // Mask 三子串匹配取消（SomeMaskHelper 等同名前缀非映射特性误报源），精度下沉 transform
            // 端符号级精确对照（SymbolEqualityComparer 语义）
            if (typeDecl.AttributeLists.Count > 0)
                return true;
            foreach (var member in typeDecl.Members)
            {
                if (member is MemberDeclarationSyntax m && m.AttributeLists.Count > 0)
                    return true;
            }

            // DTO 命名模式启发式（后缀 Dto/DTO）——有限范围，避免对普通实体类产生噪音
            var typeName = typeDecl.Identifier.Text;
            var isDtoByNaming = typeName.EndsWith("Dto", StringComparison.Ordinal)
                                || typeName.EndsWith("DTO", StringComparison.Ordinal);

            return isDtoByNaming;
        }

        /// <summary>
        /// 映射特性精确判定名单（全名形态，对齐 AttributeNames 常量单一源）——
        /// 含类级与属性级两用面（MapFrom/MapDefaults 类级/程序集级，其余属性级可用）。
        /// T1 清单改定：剔除 MapSubclass（缺锚归 F49 专属，防双报）；
        /// 剔除 MapTree——[MapTree] 是类级树
        /// 构建定义（定义，不是映射对），ToTreeList 调用即激活路径，不属"映射对配置惰性"
        /// 治理域（同 D9 类级配置口径）；MapFrom 保留于清单（hasMapFrom 早退在前）。改定后 11 项
        ///（代码注释口径 = 清单实项含 [MapFrom]；登记文本口径 = 属性级不含 [MapFrom]——两套分列，
        /// 双口径纪律）。
        /// 纳入 MapNoAutoMatch（11→12 项）——孤儿
        /// [MapNoAutoMatch]（类无 [MapFrom]）经 NCMAP019 独立输出节点承接 Error；判据同族（"映射配置
        /// 特性无锚"），消费点仅 HasMappingAttrSymbol 两处（:1619/:1624）零副作用。
        /// </summary>
        private static bool IsMappingAttributeClass(INamedTypeSymbol? attrClass)
        {
            if (attrClass == null) return false;
            var fqn = attrClass.ToDisplayString();
            // （接入面 b）：MapFormat 纳入家族清单；
            // （D2 承接）：MapNoAutoMatch 纳入（现 12 项，含 MapFrom）
            // 漏改 = 无锚 [MapNoAutoMatch] 不被编译期拦截、标注静默无效
            return fqn == AttributeNames.MapFrom
                || fqn == AttributeNames.MapProperty
                || fqn == AttributeNames.MapIgnore
                || fqn == AttributeNames.Mask
                || fqn == AttributeNames.Convert
                || fqn == AttributeNames.ConvertBack
                || fqn == AttributeNames.MapNoAutoInclude
                || fqn == AttributeNames.BeforeMapping
                || fqn == AttributeNames.AfterMapping
                || fqn == AttributeNames.MapWhen
                || fqn == AttributeNames.MapFormat
                || fqn == AttributeNames.MapNoAutoMatch;
        }

        /// <summary>类型（含其属性成员）是否标注映射特性——类级特性 + 属性成员特性
        /// 双扫（符号级精确对照，[SomeMaskHelper] 等同名前缀非映射特性不命中）。</summary>
        private static bool HasMappingAttrSymbol(INamedTypeSymbol typeSymbol)
        {
            if (typeSymbol.GetAttributes().Any(a => IsMappingAttributeClass(a.AttributeClass)))
                return true;
            foreach (var member in typeSymbol.GetMembers())
            {
                if (member is IPropertySymbol prop
                    && prop.GetAttributes().Any(a => IsMappingAttributeClass(a.AttributeClass)))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// NCMAP019 转换：提取缺少 [MapFrom] 的类型信息用于诊断报告
        /// </summary>
        /// <param name="ctx">语法上下文</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>诊断位置、类名与拆码判别位；null 表示无需报告。
        /// IsDtoNamingOnly = true 表示场景②（纯 Dto 命名 → NCMAP042 Info）；
        /// false 表示场景①（映射配置特性无锚 → NCMAP019 Error）。
        /// 判别位透传（分流值在 :1654/:1657 已计算，随返回元组一并透传，
        /// 不拆两个 CreateSyntaxProvider 节点——单节点防增量缓存面翻倍）。</returns>
        public static (LocationSnapshot? Location, string ClassName, bool IsDtoNamingOnly)? ExtractMissingMapFromDiagnostic(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            if (ctx.Node is not TypeDeclarationSyntax typeDecl)
                return null;

            // 符号级精判（宽前筛谓词放行后由此把关）——GetDeclaredSymbol 对 partial
            // 各段返回同一 INamedTypeSymbol（特性并集天然全量=类型级判定），仅主声明段产出
            // 诊断（恰 1 条，Location = 主声明标识符）
            var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;
            if (typeSymbol == null)
                return null;
            if (typeSymbol.DeclaringSyntaxReferences.Length > 0
                && typeSymbol.DeclaringSyntaxReferences[0].GetSyntax(ct) != typeDecl)
                return null;

            var className = typeSymbol.Name;
            var hasMapFrom = typeSymbol.GetAttributes()
                .Any(a => a.AttributeClass != null && AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapFrom);
            if (hasMapFrom)
                return null;

            var hasMappingAttr = HasMappingAttrSymbol(typeSymbol);

            // DTO 命名模式启发式（后缀 Dto/DTO）——有限范围，避免对普通实体类产生噪音
            var isDtoByNaming = className.EndsWith("Dto", StringComparison.Ordinal)
                                || className.EndsWith("DTO", StringComparison.Ordinal);

            if (!hasMappingAttr && !isDtoByNaming)
                return null;

            // T1 拆码分流：命中映射配置家族（十项）→ 场景① 归 NCMAP019（Error）；
            // 未命中家族且 Dto 命名 → 场景② 归 NCMAP042（Info）。hasMappingAttr 为 true 时
            // IsDtoNamingOnly 恒 false——"含映射配置标注者全归 019，不再归 042"。
            var isDtoNamingOnly = !hasMappingAttr;

            // 输出值化——Location 为符号引用会破坏输出级增量缓存
            //（无关编辑时 missingDiags 值不等 → SourceOutput Modified）；改 LocationSnapshot 纯值
            var location = MappingExtractor.SnapshotLocationInternal(typeDecl.Identifier.GetLocation());
            return (location, className, isDtoNamingOnly);
        }
    }
