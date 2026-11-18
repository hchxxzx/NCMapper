using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace NCMapper.SourceGen;

/// <summary>
/// 反向映射规划器— C1 耦合点前移：
/// 原输出期经 CodeGenerator.BuildReverseSnapshotTable 回调 transform 期类 MappingExtractor
/// （H-1 唯一实证），现反向规划三方法收敛至本类，CodeGenerator 对 MappingExtractor 的依赖归零。
/// <para>三方法自原宿主逐行迁入（行为逐字节等价，仅宿主类引用变化）：</para>
/// <list type="bullet">
/// <item>BuildReverseSnapshotTable ← CodeGenerator.cs（三消费方唯一构造点）</item>
/// <item>CreateReverseMapping ← MappingExtractor.Materialize.cs（值版反向映射）</item>
/// <item>ApplyReverseGlobalIgnoreFilter ← MappingExtractor.SnapshotConfig.cs（反向全局忽略过滤）</item>
/// </list>
/// <para>正向路径广泛复用的辅助判定（ShouldIgnoreGlobalValue / HasExplicitAttributeValue /
/// GetIgnoreMatchDescriptionValue / ShouldIgnoreInDirectionValue）仍留 MappingExtractor，
/// 经 internal 可见性调用——迁移不产生两份实现（单一事实源）。</para>
/// </summary>
internal static class ReversePlanner
{
    /// <summary>
    /// 三消费方唯一构造点 — 输出期建 mapping → 过滤后反向快照 表。
    /// <para>构造链：CreateReverseMapping → ApplyReverseGlobalIgnoreFilter（isToDto=false 双侧过滤 + NCMAP013/021）；
    /// 反向诊断（NCMAP013/031/033/034 等）在此收敛发射一次。</para>
    /// <para>异常边界：per-mapping try/catch——异常时该 mapping 反向降级（表值 null：
    /// 不生成方法/不注册/不生成扩展）+ NCMAP099，不牵连其余 mapping（与主循环 L-08→M 等价边界）。</para>
    /// </summary>
    /// <param name="spc">源生产上下文（NCMAP099 报告）</param>
    /// <param name="mappings">映射快照集合</param>
    /// <param name="mutualPairs">互指有向对字典</param>
    /// <param name="globalConfig">全局映射配置快照（null/空 → 不过滤）</param>
    /// <param name="diagnostics">per-run 诊断收集器（反向诊断收敛发射）</param>
    /// <returns>mapping → 过滤后反向快照（null = 无反向：空反向/构造异常降级）</returns>
    internal static Dictionary<MappingSnapshot, MappingSnapshot?> BuildReverseSnapshotTable(
        SourceProductionContext spc,
        System.Collections.Immutable.ImmutableArray<MappingSnapshot?> mappings,
        Dictionary<TypeRef, TypeRef> mutualPairs,
        GlobalConfigSnapshot? globalConfig,
        DiagnosticCollector diagnostics)
    {
        var table = new Dictionary<MappingSnapshot, MappingSnapshot?>();
        foreach (var mapping in mappings)
        {
            spc.CancellationToken.ThrowIfCancellationRequested();
            if (mapping == null) continue;
            // NCMAP011 / 互指对：不生成代码的 mapping 无需反向快照（与主循环同过滤）
            if (mapping.SkipCodeGeneration) continue;
            if (CodeGenerator.IsMutualPair(mapping, mutualPairs)) continue;
            try
            {
                var reverse = CreateReverseMapping(mapping, out var reverseDiagnostics);
                // 反向诊断无论反向映射是否生成均发射（空反向不吞诊断， 语义保持）
                diagnostics.AddRange(mapping, reverseDiagnostics);
                if (reverse != null)
                {
                    reverse = ApplyReverseGlobalIgnoreFilter(reverse, mapping, globalConfig, out var filterDiagnostics);
                    diagnostics.AddRange(mapping, filterDiagnostics);
                }
                table[mapping] = reverse;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 反向降级——该 mapping 不生成方法/不注册/不生成扩展；正向不受影响
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.GenerationFailedRule,
                    CodeGenerator.RebuildTargetLocation(mapping) ?? Location.None,
                    mapping.SourceType.FullName, mapping.TargetType.FullName,
                    "反向快照唯一构造点失败（F1）: " + ex.Message));
                table[mapping] = null;
            }
        }
        return table;
    }

    /// <summary>
    /// 构建反向映射快照 — 将 Source→Target 的映射反转为 Target→Source（值版）
    /// <para>语义与符号版 CreateReverseMapping 逐分支同构：自映射跳过、集合索引路径跳过、
    /// ConvertUsing 无源跳过、setter 可写性检查、[MapIgnore] 方向检查；
    /// 点路径/扁平化属性改为反向写回（ReverseWritePath 目标链 + 中间段可写/可实例化校验）。</para>
    /// </summary>
    /// <param name="forward">正向映射快照</param>
    /// <param name="reverseDiagnostics">反向诊断收集（无论返回 null 与否均完整返回——
    /// 反向无可映射属性时 NCMAP013/NCMAP031 不丢失）</param>
    /// <returns>反转后的映射快照；如果无可映射属性则返回null</returns>
    public static MappingSnapshot? CreateReverseMapping(MappingSnapshot forward, out List<DiagnosticSnapshot> reverseDiagnostics)
    {
        // 自映射类型（Source == Target）的正向和反向方法签名相同，跳过反向避免重复生成
        if (TypeRefHelpers.IsSameType(forward.SourceType, forward.TargetType))
        {
            reverseDiagnostics = new List<DiagnosticSnapshot>();
            return null;
        }

        // : Compose 属性存储在 forward.ComposeMappings 而非 PropertyMappings 中，
        // 下方 foreach 只遍历 PropertyMappings，因此 Compose 属性天然不会被反转，无需额外跳过逻辑。

        var reverseMappings = new List<PropertyMappingSnapshot>();
        reverseDiagnostics = new List<DiagnosticSnapshot>();

        // 条件守卫反向剥离——反向派生 pm 一律不复制条件四字段
        //（ConditionMethodRef/Body/ParamName/Shape，下方全部 new PropertyMappingSnapshot 初始化器
        // 均不设置即默认 null/None）——[MapWhen]/.MapWhen 声明语义绑定正向方向（真才映射的
        // 过滤语义不随反向翻转），反向映射恒无条件赋值；反向写回 pm 同构（独立构造不携带）。
        foreach (var pm in forward.PropertyMappings)
        {
            // HasIndex 门提前——原门位于下方 SourcePath.Count > 1
            // 分支内，单段索引路径（Posts[0]）逃逸到普通反向分支 → 反向生成 entity.Posts = dto.X
            //（索引语义丢失），类型不匹配时生成不可编译代码且不发 NCMAP013。门提前后单段/多段
            // 索引路径统一登记跳过（NCMAP013，文案与多段场景统一——"嵌套路径"表述对单段失实）。
            if (pm.HasIndex)
            {
                reverseDiagnostics.Add(new DiagnosticSnapshot
                {
                    DescriptorId = DiagnosticDescriptors.ReverseMappingSkippedRule.Id,
                    Args = new[] { pm.Target.Name, forward.TargetType.FullName, forward.SourceType.FullName, "集合索引路径不适用反向映射（登记跳过）" },
                });
                continue;
            }

            // →执行后设计修订（用户拍板，附录 B）：反向跳过
            // Format 是单向展示声明（格式化输出有损且随文化，反解析不闭合，往返闭合率实证 8 项 3 断 1 损），
            // 裸声明合法、不强制配套。反向映射跳过该属性（不赋值，不编数据）并给警告级提示；
            // 需要往返的用户自行声明 [ConvertBack]/.ConvertBack（可选增强，组合时反向照常消费、无本警告）。
            // （F67 触发落点）：声明面来自 Fluent .Format（Target.HasFluentExplicit）
            // → 报 Fluent 侧 F67（与特性侧 046 同语义分通道——诊断码归属通道对齐；
            // 两支独立站点形态——发射站点静态扫描约定 *.XxxRule.Id 单形态）
            if (pm.FormatString != null && !pm.Target.HasConvertBackUsing)
            {
                if (pm.Target.HasFluentExplicit)
                {
                    reverseDiagnostics.Add(new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptorsFluent.FormatReverseNotConsumedRule.Id,
                        Args = new[] { pm.Target.Name },
                        Location = pm.Target.Location,
                    });
                }
                else
                {
                    reverseDiagnostics.Add(new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptors.MapFormatReverseNotConsumedRule.Id,
                        Args = new[] { pm.Target.Name },
                        Location = pm.Target.Location,
                    });
                }
                continue;
            }

            // 点路径/扁平化反向写回（SourcePath.Count > 1 → 目标链写回：
            // DTO 扁平属性 → 实体嵌套路径赋值，正向读链的镜像）。
            if (pm.SourcePath.Count > 1)
            {
                // 反向写回：目标链 = 正向 SourcePath（含叶）；中间段须 setter 可重复赋值 + 可实例化
                var writePath = pm.SourcePath;
                var leaf = writePath[writePath.Count - 1];
                var writePathOk = true;

                for (var i = 0; i < writePath.Count - 1; i++)
                {
                    var seg = writePath[i];

                    // 中间段 setter 可写性：需要可重复赋值的 public setter
                    // （init-only/private 在对象构造后无法再赋新实例，评估 改动点 2）
                    if (seg.SetMethodAccessibility != Accessibility.Public || seg.IsInitOnly)
                    {
                        var midSetterDesc = seg.SetMethodAccessibility == Accessibility.NotApplicable
                            ? "无 setter（get-only）"
                            : seg.SetMethodAccessibility.ToString().ToLowerInvariant();
                        reverseDiagnostics.Add(new DiagnosticSnapshot
                        {
                            DescriptorId = DiagnosticDescriptors.ReverseMappingSkippedRule.Id,
                            Args = new[] { pm.Target.Name, forward.TargetType.FullName, forward.SourceType.FullName, $"反向写回路径中间节点 {seg.Name} setter 为 {midSetterDesc}，无法赋值" },
                        });
                        writePathOk = false;
                        break;
                    }

                    // 中间节点类型可访问性（生成代码 new 表达式需 public 类型，NCMAP028 同哲学）
                    if (!TypeRefHelpers.IsAccessibleFromGeneratedCode(seg.Type.Accessibility))
                    {
                        reverseDiagnostics.Add(new DiagnosticSnapshot
                        {
                            DescriptorId = DiagnosticDescriptors.ReverseMappingSkippedRule.Id,
                            Args = new[] { pm.Target.Name, forward.TargetType.FullName, forward.SourceType.FullName, $"反向写回路径中间节点 {seg.Name} 类型 {seg.Type.SimpleName} 非 public，无法生成代码" },
                        });
                        writePathOk = false;
                        break;
                    }

                    // 中间节点可实例化：class 非 abstract + 公共无参构造器；
                    // 接口/抽象/无 ctor → NCMAP031 Error 编译阻断（不生成非法代码）
                    if (seg.Type.Kind != TypeKind.Class || seg.Type.IsAbstract || !seg.Type.HasPublicParameterlessCtor)
                    {
                        reverseDiagnostics.Add(new DiagnosticSnapshot
                        {
                            DescriptorId = DiagnosticDescriptors.ReverseMappingWriteBackNotInstantiableRule.Id,
                            Args = new[]
                            {
                                seg.Type.FullName,
                                seg.Name,
                                string.Join(".", writePath.Select(p => p.Name)),
                                forward.TargetType.FullName,
                                forward.SourceType.FullName,
                            },
                            Location = pm.Target.Location,
                        });
                        writePathOk = false;
                        break;
                    }
                }
                if (!writePathOk) continue;

                // 叶段 setter：写回是赋值语义，init-only 叶无法对已有实例赋值
                // （与普通属性反向的构造器路径不同——写回发生在 dst 实例构造/更新之后）
                if (leaf.SetMethodAccessibility != Accessibility.Public || leaf.IsInitOnly)
                {
                    var leafSetterDesc = leaf.SetMethodAccessibility == Accessibility.NotApplicable
                        ? "无 setter（get-only）"
                        : leaf.SetMethodAccessibility.ToString().ToLowerInvariant();
                    reverseDiagnostics.Add(new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptors.ReverseMappingSkippedRule.Id,
                        Args = new[] { pm.Target.Name, forward.TargetType.FullName, forward.SourceType.FullName, $"目标 setter 为 {leafSetterDesc}，无法赋值" },
                    });
                    continue;
                }

                var wbSource = pm.Target;
                var wbTarget = leaf;

                // 源侧 getter 检查（符号版：revSource.GetMethod == null continue）
                if (wbSource.GetMethodAccessibility == Accessibility.NotApplicable) continue;
                // [MapIgnore] 跳过 — 反向映射检查 DTO 属性（revSource）的 Direction，方向为 ToEntity
                if (MappingExtractor.ShouldIgnoreInDirectionValue(wbSource, toDto: false)) continue;

                // 写回属性快照：SourcePath 单段（读 DTO 扁平属性），ReverseWritePath = 目标链
                // 反向默认不消费 [Convert]（转换器泛型绑定正向方向）；
                // / DTO 属性标 [ConvertBack] → 写回快照携带反向转换器三字段
                // （正向三元组承载，发射端 BuildStaticConverterCall 消费），
                // 且与 NCMAP033 互斥（该属性反向消费的是 [ConvertBack]，不再是"降级不消费"场景）；
                // 未标 [ConvertBack] 时：原 DTO 属性（锚点）/实体叶属性对侧任一标 [Convert] → 登记 NCMAP033
                if (pm.Target.HasConvertBackUsing)
                {
                    // [ConvertBack] 反向写回：转换器 source 参数 = DTO 对象（wbSource 所在类型）
                    // B 形态方法体三字段随三元组复制（GenerateConverterMethods 发射消费）
                    reverseMappings.Add(new PropertyMappingSnapshot
                    {
                        Target = wbTarget,
                        Source = wbSource,
                        SourcePath = new[] { wbSource },
                        ReverseWritePath = writePath,
                        IsFlattened = false,
                        PathOrigin = PathOrigin.Direct,
                        ConverterDeclaringType = pm.Target.ConverterBackDeclaringType,
                        ConverterMethodName = pm.Target.ConverterBackMethodName,
                        ConverterShape = pm.Target.ConverterBackShape,
                        ConverterMethodBody = pm.Target.ConverterBackMethodBody,
                        ConverterMethodParamTypes = pm.Target.ConverterBackMethodParamTypes,
                        ConverterMethodParamNames = pm.Target.ConverterBackMethodParamNames,
                        IsFromJson = pm.IsFromJson,
                    });
                }
                // 「实体叶标注兜底（X2 补齐）」
                // 的 `else if (pm.Source?.HasConvertBackUsing)` 支按 删除段
                // （"[ConvertBack] 双查仲裁"）退役——写回链单查承载侧（wbSource = pm.Target），
                // 与直接分支（:348-367， 同批退役）及运行时
                // BuildReversePathWriteBackFromPath（ExpressionBuilder.Paths.cs:532-534，
                // T5 已删叶兜底）口径合一；叶标注形态落入下方 else →
                // 参与 NCMAP033"降级不消费"判定（不再作互斥豁免）。
                else
                {
                    // 正向转换器信号读 pm 级三字段（采集侧物化，覆盖目标侧/对侧标注两种场景）；
                    // 原 pm.Target/leaf（PropertyRef 级）正向三字段从不物化恒 null → NCMAP033 永不发射（测试守住）
                    // 原 `&& !(pm.Source?.HasConvertBackUsing == true)` 互斥子句随叶兜底退役删除
                    //（兜底退役后叶标注形态确为"降级不消费"，保留豁免会致静默不报，与"编译期给你看"相悖；
                    // 与直接分支同批处理）
                    var hasForwardConverter = pm.ConverterDeclaringType != null;
                    if (hasForwardConverter)
                    {
                        reverseDiagnostics.Add(new DiagnosticSnapshot
                        {
                            DescriptorId = DiagnosticDescriptors.ConvertUsingNotConsumedInReverseRule.Id,
                            Args = new[] { wbSource.Name, forward.TargetType.FullName, forward.SourceType.FullName },
                            Location = pm.Target.Location,
                        });
                    }
                    reverseMappings.Add(new PropertyMappingSnapshot
                    {
                        Target = wbTarget,
                        Source = wbSource,
                        SourcePath = new[] { wbSource },
                        ReverseWritePath = writePath,
                        IsFlattened = false,
                        PathOrigin = PathOrigin.Direct,
                        IsFromJson = pm.IsFromJson,
                    });
                }
                continue;
            }

            // [Convert] 无匹配源属性场景：pm.Source 为 null
            // 反向映射无法处理（无源属性可反向写入），跳过
            if (pm.Source == null)
            {
                reverseDiagnostics.Add(new DiagnosticSnapshot
                {
                    DescriptorId = DiagnosticDescriptors.ReverseMappingSkippedRule.Id,
                    Args = new[] { pm.Target.Name, forward.TargetType.FullName, forward.SourceType.FullName, "ConvertUsing 无对应源属性，无法反向写入" },
                });
                continue;
            }

            var revSource = pm.Target;
            var revTarget = pm.Source;

            // 源侧 getter 检查（符号版：revSource.GetMethod == null continue）
            if (revSource.GetMethodAccessibility == Accessibility.NotApplicable) continue;
            // 反向 setter 门收紧——与正向门同判（非 public
            // 拒绝，init-only 豁免取消——private init 入生成面即 CS0272；原 P1-SHOULD-8 口径废弃）
            if (revTarget.SetMethodAccessibility != Accessibility.Public)
            {
                var setterDesc = revTarget.SetMethodAccessibility == Accessibility.NotApplicable
                    ? "无 setter（get-only）"
                    : revTarget.SetMethodAccessibility.ToString().ToLowerInvariant();
                reverseDiagnostics.Add(new DiagnosticSnapshot
                {
                    DescriptorId = DiagnosticDescriptors.ReverseMappingSkippedRule.Id,
                    Args = new[] { revTarget.Name, forward.TargetType.FullName, forward.SourceType.FullName, $"目标 setter 为 {setterDesc}，无法赋值" },
                });
                continue;
            }
            // [MapIgnore] 跳过 — 反向映射检查 DTO 属性（revSource）的 Direction，方向为 ToEntity
            if (MappingExtractor.ShouldIgnoreInDirectionValue(revSource, toDto: false)) continue;

            // / DTO 属性标 [ConvertBack] → 反向 pm 携带反向转换器
            // 三字段（正向三元组承载，TSource = DTO = revSource 类型，发射端内联静态调用消费）
            // M-22（20260926-001 五轴审查）：承载侧单查——原"实体侧匹配对兜底"
            // 支按 删除段
            // （"[ConvertBack] 双查仲裁"删除）退役，与运行时 PlanCore 单查承载侧及 M-26 同口径；
            // 原 备案随之退役。
            reverseMappings.Add(new PropertyMappingSnapshot
            {
                Target = revTarget,
                Source = revSource,
                SourcePath = new[] { revSource },
                IsFlattened = false,
                PathOrigin = PathOrigin.Direct,
                IsFromJson = pm.IsFromJson,
                // 承载侧（反向 DTO = revSource）单查——原 revTarget 兜底支已删
                ConverterDeclaringType = revSource.ConverterBackDeclaringType,
                ConverterMethodName = revSource.ConverterBackMethodName,
                ConverterShape = revSource.ConverterBackShape,
                // B 形态方法体三字段随三元组同序复制（后同为 revSource 单取）
                ConverterMethodBody = revSource.ConverterBackMethodBody,
                ConverterMethodParamTypes = revSource.ConverterBackMethodParamTypes,
                ConverterMethodParamNames = revSource.ConverterBackMethodParamNames,
            });

            // G2（NCMAP033）：正向带 [Convert] 的直接属性在反向不消费转换器
            // （转换器泛型绑定正向方向），反向按类型转换路径写入；仅在属性成功进入反向快照时登记（避免跳过属性误报）
            // 互斥：DTO 属性标 [ConvertBack] 时反向消费的是反向转换器，非"降级不消费"场景 → 不登记
            // 实体侧 [ConvertBack] 兜底退役后该形态确为"降级不消费" → 不再作互斥豁免（原 扩展句废止）
            if (pm.ConverterDeclaringType != null && !revSource.HasConvertBackUsing)
            {
                reverseDiagnostics.Add(new DiagnosticSnapshot
                {
                    DescriptorId = DiagnosticDescriptors.ConvertUsingNotConsumedInReverseRule.Id,
                    Args = new[] { revSource.Name, forward.TargetType.FullName, forward.SourceType.FullName },
                    Location = pm.Target.Location,
                });
            }

            // G3（NCMAP034）：带 [Mask] 的 DTO 属性参与反向写回（脱敏值将原样写回）
            if (revSource.HasMask)
            {
                reverseDiagnostics.Add(new DiagnosticSnapshot
                {
                    DescriptorId = DiagnosticDescriptors.MaskReverseWriteBackRule.Id,
                    Args = new[] { revSource.Name, forward.TargetType.FullName, forward.SourceType.FullName },
                    Location = pm.Target.Location,
                });
            }
        }

        // 反向无源转换器映射追加 — 实体属性标 [ConvertBack] 且未被
        // 任何正向 pm 消费（覆盖排除集外）的条目逐条追加为反向 pm（Target=实体属性、
        // Source=null、SourcePath=空数组、三元组=反向转换器——TSource=DTO 对象整传、无源字段
        // 实参恒 default 字面量）。
        // 追加序：主 foreach 之后、计数门之前——「仅剩无源条目」最严场景（正向 pm 集合为空）
        // 反向快照仍非 null、反向方法照常生成（验收 #2）；与运行时镜像追加序对齐（暂挂登记 +
        // EmitPendingBackConverters 于写回链后统一发射，附录 D2「写回链后发射」基准）。
        // 发射端零改动复用：GenerateSinglePropertyAssignment 三字段分支（ConverterDeclaringType
        // != null 先于 Source 判空 → srcAccess=null → BuildStaticConverterCall default 字面量实参，
        // 正向无源同通路）；构造器参数匹配路径 无源守卫同消费；
        // 全局忽略过滤对本类条目 Target（实体属性）照常判定（Source 判空 已有）——全局忽略
        // 同样抑制无源反向转换；互指对沿用 BuildReverseSnapshotTable 既有跳过。
        // 运行时镜像：ExpressionBuilder.BuildBackConverterAssignmentForNullSource（无源块前置分支）。
        foreach (var ro in forward.ReverseOnlyConvertBacks)
        {
            // B 形态方法体三字段随三元组复制（ReverseOnly 条目
            // Fluent ConvertBack 反向无源场景的内联转录产物）
            reverseMappings.Add(new PropertyMappingSnapshot
            {
                Target = ro.Target,
                Source = null,
                SourcePath = Array.Empty<PropertyRef>(),
                IsFlattened = false,
                PathOrigin = PathOrigin.Direct,
                ConverterDeclaringType = ro.ConverterDeclaringType,
                ConverterMethodName = ro.ConverterMethodName,
                ConverterShape = ro.ConverterShape,
                ConverterMethodBody = ro.ConverterMethodBody,
                ConverterMethodParamTypes = ro.ConverterMethodParamTypes,
                ConverterMethodParamNames = ro.ConverterMethodParamNames,
            });
        }

        if (reverseMappings.Count == 0)
            return null;

        return new MappingSnapshot
        {
            SourceType = forward.TargetType,
            TargetType = forward.SourceType,
            SourceClassName = forward.TargetClassName,
            TargetClassName = forward.SourceClassName,
            SkipCodeGeneration = forward.SkipCodeGeneration,
            ReportUnmappedTargetMembers = forward.ReportUnmappedTargetMembers,
            ReportUnmappedSourceMembers = forward.ReportUnmappedSourceMembers,
            PropertyMappings = reverseMappings,
            SourceProperties = forward.AllTargetProperties,
            Diagnostics = reverseDiagnostics,
            // （反向快照透传）：对级白名单位随反向快照带位
            // 生成期语义正确性承载（反向快照相等性/哈希与后续消费面不再恒缺省）+ 守恒清单登记成本
            //（SnapshotConservationTests mayOmit 同批移除）。收益定性按开工探针结论（/：
            // 运行时注册表无 (Dto,Entity) 反向条目，透传非运行时反向腿必要条件）。
            NoAutoInclude = forward.NoAutoInclude,
        };
    }

    /// <summary>
    /// 全局忽略过滤（反向，值版）— 以 isToDto=false 对反向 PropertyMappings 双侧（源=DTO、目标=实体）
    /// 执行 ShouldIgnoreGlobalValue，过滤后为空 → 返回 null（等同 CreateReverseMapping 空反向）；诊断经 out 完整返回（不吞诊断）。
    /// </summary>
    /// <param name="reverse">反向映射快照</param>
    /// <param name="forward">正向映射快照（对照用）</param>
    /// <param name="config">全局配置快照（null/空 → 不过滤）</param>
    /// <param name="filterDiagnostics">过滤过程产生的诊断（out 返回）</param>
    /// <returns>过滤后的反向映射快照；过滤后为空时为 null</returns>
    internal static MappingSnapshot? ApplyReverseGlobalIgnoreFilter(
        MappingSnapshot reverse, MappingSnapshot forward, GlobalConfigSnapshot? config,
        out List<DiagnosticSnapshot> filterDiagnostics)
    {
        filterDiagnostics = new List<DiagnosticSnapshot>();
        if (config == null || config.IsEmpty) return reverse;

        var pms = reverse.PropertyMappings.ToList();
        for (var i = pms.Count - 1; i >= 0; i--)
        {
            var pm = pms[i];
            // 双侧判定（isToDto=false）：源 = DTO 属性（reverse 的 Source），目标 = 实体属性（reverse 的 Target）
            // OverrideIgnore 豁免与正向同构（HasOverrideIgnore → 不命中）
            var hitSource = pm.Source != null
                && !pm.Source.HasOverrideIgnore
                && MappingExtractor.ShouldIgnoreGlobalValue(pm.Source, config, isToDto: false);
            var hitTarget = !pm.Target.HasOverrideIgnore
                && MappingExtractor.ShouldIgnoreGlobalValue(pm.Target, config, isToDto: false);
            if (!hitSource && !hitTarget) continue;

            // 显式特性命中 → NCMAP021（处置指引，与正向语义一致）；其余场景 NCMAP013 登记跳过
            var hitProp = hitSource ? pm.Source! : pm.Target;
            if (MappingExtractor.HasExplicitAttributeValue(hitProp))
            {
                filterDiagnostics.Add(new DiagnosticSnapshot
                {
                    DescriptorId = DiagnosticDescriptors.ExplicitMappingHitGlobalIgnoreRule.Id,
                    Args = new[] { hitProp.Name, MappingExtractor.GetIgnoreMatchDescriptionValue(hitProp, config, isToDto: false) },
                });
            }
            filterDiagnostics.Add(new DiagnosticSnapshot
            {
                DescriptorId = DiagnosticDescriptors.ReverseMappingSkippedRule.Id,
                Args = new[]
                {
                    pm.Source?.Name ?? pm.Target.Name,
                    forward.TargetType.FullName,
                    forward.SourceType.FullName,
                    "命中全局 ToEntity 忽略清单（F1 对齐）",
                },
                Location = pm.Target.Location,
            });
            pms.RemoveAt(i);
        }

        if (pms.Count == 0) return null;

        // 不可变重建（仅替换 PropertyMappings；其余字段透传）——部分透传站点：14 项反向恒缺省
        // 登记于 SnapshotConservationTests mayOmit 白名单（B3-09 单点指引：新增字段须按白名单
        // 口径决策透传或登记缺省；：NoAutoInclude 由恒缺省转透传，同批移出清单）
        return new MappingSnapshot
        {
            SourceType = reverse.SourceType,
            TargetType = reverse.TargetType,
            SourceClassName = reverse.SourceClassName,
            TargetClassName = reverse.TargetClassName,
            SkipCodeGeneration = reverse.SkipCodeGeneration,
            ReportUnmappedTargetMembers = reverse.ReportUnmappedTargetMembers,
            ReportUnmappedSourceMembers = reverse.ReportUnmappedSourceMembers,
            PropertyMappings = pms,
            SourceProperties = reverse.SourceProperties,
            Diagnostics = reverse.Diagnostics,
            // （回验）：本处取 reverse.NoAutoInclude——其余字段自 reverse 透传，
            // CreateReverseMapping 构造补齐后两处值等价
            NoAutoInclude = reverse.NoAutoInclude,
        };
    }
}
