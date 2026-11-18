using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace NCMapper.SourceGen;

/// <summary>
/// 双驱动仲裁器
/// [MapFrom] 特性通道与 Fluent 通道产物的合并点 + 同对双声明仲裁（NCMAP_F11，候选 A 从严互斥）。
/// <para>仲裁规则（判定算法）：key = (SourceType.EqualityKey, TargetType.EqualityKey)
/// 与 CodeGenerator 注册键语义一致（同一 GeneratedCache&lt;TSource,TTarget&gt; 静态单槽位冲突点）；
/// 同 key 双侧（MapFrom + Fluent）→ 双位置各发一条 F11（消息互指）+ 两侧快照均不派生
///（SkipCodeGeneration=true 重建——不生成方法/不注册/不生成扩展，诊断仍发射）。</para>
/// <para>互斥边界（v1.6 红线；F16 增补于）：F11 仅同一 (S,T) 对被两通道双声明触发；
/// 不同 (S,T) 对各走各通道（模式并存）不触发。Fluent 通道自身同对重复由 NCMAP_F16 独立诊断
///（"报错"候选）：每处声明各发一条 F16（消息列出全部冲突方 Mapper 类名）+ 全部冲突快照
/// 抑制派生；[MapFrom] 仲裁表仍仅以 MapFrom 侧为基准，F11 分支零改动（可叠加）。</para>
/// </summary>
internal static class DualDriverArbiter
{
    /// <summary>
    /// 合并 + 仲裁两通道产物（管线 Select 阶段——纯函数，输入输出均纯值）
    /// </summary>
    /// <param name="mapFromMappings">[MapFrom] 特性通道产物（调用方可能传入含 null 的数组，本方法逐项跳过）</param>
    /// <param name="fluentResults">Fluent 通道提取结果（每 [MapperConfig] 类一项）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>合并产物（统一 mappings + 通道级独立诊断流）</returns>
    public static ChannelMergeResult MergeAndArbitrate(
        ImmutableArray<MappingSnapshot?> mapFromMappings,
        ImmutableArray<FluentExtractionResult?> fluentResults,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var mappings = new List<MappingSnapshot?>(mapFromMappings.Length);
        // [MapFrom] 侧索引：key → 产物下标（仅 MapFrom 侧——Fluent 自身重复走 fluentIndex/F16 路径，不进本表）。
        // value 存压缩列表下标（mappings 为 null 跳过后的压缩列，
        // 消费点以 mappings[idx] 索引——原实现存原数组下标 i，null 混入时越界/静默错位）
        var mapFromIndex = new Dictionary<string, List<int>>();

        foreach (var m in mapFromMappings)
        {
            if (m == null) continue;
            mappings.Add(m);
            var key = ConflictKey(m);
            if (!mapFromIndex.TryGetValue(key, out var list))
            {
                list = new List<int>();
                mapFromIndex[key] = list;
            }
            list.Add(mappings.Count - 1);
        }

        // Fluent 侧预扫描索引——同 (S,T) 对在 Fluent 通道内的重复声明定位
        //（key → (resultIdx, declIdx) 列表；与 mapFromIndex 相互独立，F11 跨通道仲裁流程不受影响。
        //   两遍扫描纯函数，管线 Select 步骤划分不变——Q6 失效粒度结论：两候选增量代价相同）
        var fluentIndex = new Dictionary<string, List<(int resultIdx, int declIdx)>>();
        for (var ri = 0; ri < fluentResults.Length; ri++)
        {
            var result = fluentResults[ri];
            if (result == null) continue;
            for (var di = 0; di < result.Declarations.Count; di++)
            {
                var key = ConflictKey(result.Declarations[di].Snapshot);
                if (!fluentIndex.TryGetValue(key, out var slots))
                {
                    slots = new List<(int, int)>();
                    fluentIndex[key] = slots;
                }
                slots.Add((ri, di));
            }
        }

        // 通道级诊断流（Fluent 通道诊断 + F11 仲裁产物）
        var diagnostics = new List<DiagnosticSnapshot>();

        foreach (var result in fluentResults)
        {
            if (result == null) continue;
            ct.ThrowIfCancellationRequested();
            diagnostics.AddRange(result.Diagnostics);

            foreach (var declaration in result.Declarations)
            {
                var key = ConflictKey(declaration.Snapshot);
                MappingSnapshot snapshot = declaration.Snapshot;

                if (mapFromIndex.TryGetValue(key, out var conflictIndices))
                {
                    // 判定算法步骤 5：同 (S,T) 对双声明 → 双位置各发一条 F11（消息互指）
                    // + 两侧快照均不派生（冲突产物不参与后续派生注册——避免 GeneratedMapper 重复方法）
                    var dtoClassName = declaration.Snapshot.TargetClassName;
                    var sourceFullName = declaration.Snapshot.SourceType.FullName;
                    var targetFullName = declaration.Snapshot.TargetType.FullName;

                    // F39 双通道钩子重复声明（D-h）——同对 Before×Before /
                    // After×After 同类并存且 ref 不等各发一条（Before/After 独立判定；交叉组合不报；
                    // D-d 签名适用过滤前置 = ref 非空即适用已过；**ref 相等 = 跨对传播注入的同一声明
                    //（值非空不覆盖）非冲突**）。双位置（Fluent 侧 For 位置 + 特性侧 DTO 标识位置）
                    // 镜像 F11 口径，消息含双侧 ref 便于任一侧定位修复。
                    var fluentSideHookConflicts = new List<DiagnosticSnapshot>();

                    // Fluent 侧（For invocation 位置）——进通道级诊断流
                    diagnostics.Add(new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptorsFluent.DualDriverConflictRule.Id,
                        Args = new[] { dtoClassName, result.MapperClassName, sourceFullName, targetFullName },
                        Location = declaration.ForLocation,
                    });
                    // Fluent 侧快照抑制派生（重建）
                    snapshot = Suppress(declaration.Snapshot);

                    // [MapFrom] 侧（DTO 类标识符位置——TargetIdentifierLocation，与 NCMAP025 同口径）
                    // 诊断经快照 Diagnostics 流（EmitDiagnostics 消费）；快照抑制派生（重建）
                    foreach (var idx in conflictIndices)
                    {
                        var mapFromSnapshot = mappings[idx];
                        if (mapFromSnapshot == null || mapFromSnapshot.SkipCodeGeneration) continue;

                        // F11 + F39 同批追加进特性侧快照诊断流（F39 per 同类钩子一条，
                        // 位置 = DTO 标识位置）；Fluent 侧 F39（位置 = For invocation）同步累积
                        var attrSideDiagnostics = new List<DiagnosticSnapshot>
                        {
                            new()
                            {
                                DescriptorId = DiagnosticDescriptorsFluent.DualDriverConflictRule.Id,
                                Args = new[] { dtoClassName, result.MapperClassName, sourceFullName, targetFullName },
                                Location = mapFromSnapshot.TargetIdentifierLocation,
                            },
                        };
                        foreach (var hookConflict in EnumerateHookConflicts(mapFromSnapshot, declaration.Snapshot, sourceFullName, targetFullName))
                        {
                            attrSideDiagnostics.Add(new DiagnosticSnapshot
                            {
                                DescriptorId = hookConflict.DescriptorId,
                                Args = hookConflict.Args,
                                Location = mapFromSnapshot.TargetIdentifierLocation,
                            });
                            fluentSideHookConflicts.Add(new DiagnosticSnapshot
                            {
                                DescriptorId = hookConflict.DescriptorId,
                                Args = hookConflict.Args,
                                Location = declaration.ForLocation,
                            });
                        }

                        mappings[idx] = Suppress(mapFromSnapshot, attrSideDiagnostics);
                    }

                    diagnostics.AddRange(fluentSideHookConflicts);
                }

                // Fluent 通道内同 (S,T) 对重复声明（拍板"报错"候选）
                // 每处声明各发一条 F16（位置=For invocation，消息列出全部冲突方 Mapper 类名互指）；
                // 全部冲突快照抑制派生（SkipCodeGeneration 重建——不生成派生方法/不注册/不生成反向扩展，
                // 消费端 CS0111 消失）。不依赖 Collect 顺序（放弃"保留先位"策略的跨语法树顺序风险）。
                // 与 F11 分支独立叠加：双重命中时 Suppress 幂等（两次 SkipCodeGeneration=true 重建等价），
                // F11 系诊断照发、F16 系独立追加，两类诊断分属独立流不去重。
                if (fluentIndex.TryGetValue(key, out var fluentConflicts) && fluentConflicts.Count > 1)
                {
                    var sourceFullName = declaration.Snapshot.SourceType.FullName;
                    var targetFullName = declaration.Snapshot.TargetType.FullName;
                    var mapperClassNames = string.Join(", ", fluentConflicts
                        .Select(c => fluentResults[c.resultIdx]!.MapperClassName)
                        .Distinct());
                    diagnostics.Add(new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptorsFluent.FluentDuplicateTypePairRule.Id,
                        Args = new[]
                        {
                            sourceFullName,
                            targetFullName,
                            fluentConflicts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            mapperClassNames,
                        },
                        Location = declaration.ForLocation,
                    });
                    snapshot = Suppress(snapshot);
                }

                mappings.Add(snapshot);
            }
        }

        // MapSubclass 合成子类对并入（前置 1 / 去重查表）
        // 平行流三查去重后入 mappings（值化层 FQN 二元组查表 = "已在 mappings → 引用不重发；
        // 不在 → 生成"语义）：
        // ① [MapFrom] 显式（mapFromIndex 命中）→ 弃合成（显式声明对生成，基类分派引用其方法）；
        // ② Fluent 显式（fluentIndex 命中——只含显式 Declarations，合成对不在索引面）→ 弃合成；
        // ③ 已并入合成集（跨基类对共享同一子类对，如 (Animal,AnimalDto) 与 (Creature,CreatureDto)
        //    都登记 (Dog,DogDto)）→ 保留首个（同源同目标映射实现唯一，非冲突不报）。
        // 不触发 F11/F16（合成流不经 Declarations 冲突仲裁面）；显式对若被 Suppress（冲突态）
        // 仍视为"已声明"——合成不顶替（冲突场景零生成，合成顶替会复活冲突产物）。
        var syntheticSeen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var result in fluentResults)
        {
            if (result == null) continue;
            ct.ThrowIfCancellationRequested();
            foreach (var synthetic in result.SyntheticDeclarations)
            {
                var key = ConflictKey(synthetic.Snapshot);
                if (mapFromIndex.ContainsKey(key)) continue; // ① 特性显式
                if (fluentIndex.ContainsKey(key)) continue; // ② Fluent 显式
                if (!syntheticSeen.Add(key)) continue; // ③ 合成集去重
                mappings.Add(synthetic.Snapshot);
            }
        }

        return new ChannelMergeResult(mappings.ToImmutableArray(), diagnostics.ToImmutableArray());
    }

    /// <summary>仲裁键：(SourceType.EqualityKey, TargetType.EqualityKey)——与注册键/缓存槽位语义一致</summary>
    private static string ConflictKey(MappingSnapshot m)
        => m.SourceType.EqualityKey + "\u0001" + m.TargetType.EqualityKey;

    /// <summary>
    /// 枚举双通道同类钩子冲突（F39，D-h）
    /// 同对 Before×Before / After×After 同类并存且 ref 不等（Before/After 独立判定；交叉不报；
    /// ref 相等 = 跨对传播注入的同一声明（值非空不覆盖）非冲突）。D-d 签名适用过滤前置：
    /// ref 非空即签名适用已过，不适用 ref 不进冲突判定。
    /// </summary>
    /// <param name="attrSnapshot">[MapFrom] 特性侧快照（钩子 ref 来自 DTO 类型级特性提取）</param>
    /// <param name="fluentSnapshot">Fluent 侧快照（钩子 ref 来自链声明或跨对传播注入）</param>
    /// <param name="sourceFullName">源类型全名（消息参数）</param>
    /// <param name="targetFullName">目标类型全名（消息参数）</param>
    /// <returns>同类冲突诊断序列（无冲突为空；Before/After 各至多一条）</returns>
    private static IEnumerable<DiagnosticSnapshot> EnumerateHookConflicts(
        MappingSnapshot attrSnapshot, MappingSnapshot fluentSnapshot, string sourceFullName, string targetFullName)
    {
        if (!string.IsNullOrEmpty(attrSnapshot.BeforeMappingMethodRef)
            && !string.IsNullOrEmpty(fluentSnapshot.BeforeMappingMethodRef)
            && !string.Equals(attrSnapshot.BeforeMappingMethodRef, fluentSnapshot.BeforeMappingMethodRef, System.StringComparison.Ordinal))
        {
            yield return new DiagnosticSnapshot
            {
                DescriptorId = DiagnosticDescriptorsFluent.HookDualChannelConflictRule.Id,
                Args = new[] { "BeforeMapping", sourceFullName, targetFullName, attrSnapshot.BeforeMappingMethodRef!, fluentSnapshot.BeforeMappingMethodRef! },
                Location = attrSnapshot.TargetIdentifierLocation,
            };
        }
        if (!string.IsNullOrEmpty(attrSnapshot.AfterMappingMethodRef)
            && !string.IsNullOrEmpty(fluentSnapshot.AfterMappingMethodRef)
            && !string.Equals(attrSnapshot.AfterMappingMethodRef, fluentSnapshot.AfterMappingMethodRef, System.StringComparison.Ordinal))
        {
            yield return new DiagnosticSnapshot
            {
                DescriptorId = DiagnosticDescriptorsFluent.HookDualChannelConflictRule.Id,
                Args = new[] { "AfterMapping", sourceFullName, targetFullName, attrSnapshot.AfterMappingMethodRef!, fluentSnapshot.AfterMappingMethodRef! },
                Location = attrSnapshot.TargetIdentifierLocation,
            };
        }
    }

    /// <summary>
    /// 冲突产物抑制派生（不可变重建：SkipCodeGeneration=true——不生成方法/不注册/不生成反向/诊断仍发射；
    /// 可批量追加 F11/F39 诊断进快照诊断流）
    /// </summary>
    private static MappingSnapshot Suppress(MappingSnapshot m, IReadOnlyList<DiagnosticSnapshot>? withDiagnostics = null)
    {
        IReadOnlyList<DiagnosticSnapshot> diags = m.Diagnostics;
        if (withDiagnostics != null && withDiagnostics.Count > 0)
        {
            var list = new List<DiagnosticSnapshot>(m.Diagnostics);
            list.AddRange(withDiagnostics);
            diags = list;
        }
        return new MappingSnapshot
        {
            SourceType = m.SourceType,
            TargetType = m.TargetType,
            SourceClassName = m.SourceClassName,
            TargetClassName = m.TargetClassName,
            SkipCodeGeneration = true,
            ReportUnmappedTargetMembers = m.ReportUnmappedTargetMembers,
            ReportUnmappedSourceMembers = m.ReportUnmappedSourceMembers,
            HasClassLevelDiagnosticOptions = m.HasClassLevelDiagnosticOptions,
            PropertyMappings = m.PropertyMappings,
            AllTargetProperties = m.AllTargetProperties,
            SourceProperties = m.SourceProperties,
            DeepCopyNodes = m.DeepCopyNodes,
            ComposeMappings = m.ComposeMappings,
            ReverseOnlyConvertBacks = m.ReverseOnlyConvertBacks,
            Diagnostics = diags,
            TargetIdentifierLocation = m.TargetIdentifierLocation,
            // ForInvocationLocation 全量透传（Suppress 臂字段守恒
            // 互指对经 F11/SkipCodeGeneration 臂重建后 025 Fluent 侧定位不丢）
            ForInvocationLocation = m.ForInvocationLocation,
            NoAutoInclude = m.NoAutoInclude,
            AfterMappingMethodRef = m.AfterMappingMethodRef,
            // 字段守恒（B3-09 单点指引）：全量透传站点——新增 MappingSnapshot 字段须补入本
            // 初始化器（SnapshotConservationTests 反射集 vs 赋值集机械防线）
            BeforeMappingMethodRef = m.BeforeMappingMethodRef,
            IsFluentForward = m.IsFluentForward,
            TreeConfig = m.TreeConfig,
            PropertyNoAutoIncludeTargets = m.PropertyNoAutoIncludeTargets,
            HasComposeMappings = m.HasComposeMappings,
            SubclassPairs = m.SubclassPairs,
        };
    }
}

/// <summary>
/// 双通道合并产物（管线缓存值——纯值语义）
/// </summary>
internal sealed class ChannelMergeResult : IEquatable<ChannelMergeResult>
{
    /// <summary>统一映射快照集合（[MapFrom] + Fluent 合并后）</summary>
    public ImmutableArray<MappingSnapshot?> Mappings { get; }

    /// <summary>通道级独立诊断流（Fluent 通道 F02/F04~F08/F12~F15 + F11/F16 仲裁产物；
    /// 独立于快照内 Diagnostics——不随 IsDefaultOrEmpty 早退）</summary>
    public ImmutableArray<DiagnosticSnapshot> ExtraDiagnostics { get; }

    public ChannelMergeResult(ImmutableArray<MappingSnapshot?> mappings, ImmutableArray<DiagnosticSnapshot> extraDiagnostics)
    {
        Mappings = mappings;
        ExtraDiagnostics = extraDiagnostics;
    }

    /// <inheritdoc />
    public bool Equals(ChannelMergeResult? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Mappings.SequenceEqual(other.Mappings)
               && ExtraDiagnostics.SequenceEqual(other.ExtraDiagnostics);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ChannelMergeResult);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var m in Mappings) hash = hash * 31 + (m?.GetHashCode() ?? 0);
            foreach (var d in ExtraDiagnostics) hash = hash * 31 + d.GetHashCode();
            return hash;
        }
    }
}
