using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace NCMapper.SourceGen;

/// <summary>
/// 发射前准备产物— 输出回调内一次构造，
/// 发射期（GenerateMapperCode）零决策、零回调 transform 期类。
/// <para>承载 9 产物：上提三方法产物（MethodSuffixMap / TypeNameMap / MutualPairs）+
/// C1 反向表（ReverseSnapshotTable， ReversePlanner 产物）+ C3 环检测就绪标记
/// （CycleDetectionPrepared，_degradedPairs 静态缓存已写、IsDegraded 查询点不变）+
/// C4 深拷贝图分类（DeepCopyGraphs，键 TypeRef、值含 TypeName——GenerateDeepCopyForType
/// 第三参依赖， '）+ C2a 表达式化计划两表（InitOnlyExprPlans / ElementExprPlans，
/// ——BuildInitOnlyAssignmentExpr / GenerateElementMappingExpression 判定链前移产物，
/// 发射端按 Kind 纯渲染）+ 简单赋值预扫描表（SimpleAssignments，
/// MapList 四路径发射期查表）。</para>
/// <para>编排入口 <see cref="Prepare"/> 为输出回调内的确定性函数（输入值模型 + 输出产物）；
/// 进程级静态缓存仅经 NestedCycleDetector 与 TryMarkDegraded 两处状态机访问（非纯函数边界如实标注），不进增量管线 step。
/// 注：契约的 required 修饰在 netstandard2.0 无 RequiredMemberAttribute 支持（实现级适配），
/// 以唯一构造点（Prepare 内对象初始化器全字段赋值）保证非空。</para>
/// </summary>
internal sealed class GenerationPreparation
{
    /// <summary>
    /// 反向快照表（C1 前移产物， ReversePlanner.BuildReverseSnapshotTable）— mapping → 过滤后反向快照
    /// </summary>
    public Dictionary<MappingSnapshot, MappingSnapshot?> ReverseSnapshotTable { get; init; } = null!;

    /// <summary>
    /// 深拷贝图分类产物（C4 前移；键保持 TypeRef；值含 TypeName——GenerateDeepCopyForType 第三参依赖， '；
    /// Node=null 为防御条目——节点缺失时发射端跳过方法生成，注册行仍输出 acyclic:false）
    /// </summary>
    public Dictionary<TypeRef, (string TypeName, DeepCopyNodeShape Node, DeepCopyGraphShape Shape, IReadOnlyList<UnknownEdgeSnapshot> UnknownEdges)> DeepCopyGraphs { get; init; } = null!;

    /// <summary>
    /// 环检测就绪标记（C3 前移；_degradedPairs 静态缓存已写，IsDegraded 查询点 :718/:1518 不变）
    /// </summary>
    public bool CycleDetectionPrepared { get; init; }

    /// <summary>
    /// 方法名后缀映射（原 AssignMethodSuffixes 产物，上提后供 GenerateMapperCode 复用）
    /// </summary>
    public Dictionary<string, string> MethodSuffixMap { get; init; } = null!;

    /// <summary>
    /// 类型名映射（原 BuildTypeNameMap 产物，发射期 12+ 处消费， ' 补）
    /// </summary>
    public Dictionary<TypeRef, string> TypeNameMap { get; init; } = null!;

    /// <summary>
    /// 互指对表（原 DetectMutualPairs 产物，发射期 6 处消费， ' 补）
    /// </summary>
    public Dictionary<TypeRef, TypeRef> MutualPairs { get; init; } = null!;

    /// <summary>
    /// init-only 表达式化计划表（C2a 前移产物）— pm → 表达式化路径计划。
    /// <para>覆盖面 = 正向 + 反向 mapping 全部 init-only 目标属性（发射端 BuildInitOnlyInitializerMembers
    /// 查询集的超集）；发射端经 ctx.Preparation 通道查表纯渲染，miss 时兜底现场决策（防御不变量）。</para>
    /// </summary>
    public Dictionary<PropertyMappingSnapshot, InitOnlyExprPlan> InitOnlyExprPlans { get; init; } = null!;

    /// <summary>
    /// 元素映射表达式计划表（C2a 前移产物）— (源元素类型, 目标元素类型) → 路径计划。
    /// <para>覆盖面 = 正向 + 反向 mapping 全部属性无条件提取的集合元素对 + 字典 K/V 对
    /// （发射端分发链 ⑬⑭ 查询集的超集；元素形态判定整体前移见）；
    /// 发射端 GenerateCollectionMapping / GenerateDictionaryMapping 查表纯渲染。</para>
    /// </summary>
    public Dictionary<(TypeRef Source, TypeRef Target), ElementExprPlan> ElementExprPlans { get; init; } = null!;

    /// <summary>
    /// 简单赋值预扫描表（前移产物）— mapping → 简单赋值列表。
    /// <para>覆盖面 = 全部非 null 正向 mapping（发射端查询集 = 主循环正向 MapList 四路径的
    /// 超集——GenerateInlineElement 全部 4 调用点位于正向 MapList 发射族，Batch-3 重定位实证；
    /// SkipCodeGeneration/互指对多算无害，不查询不命中）；值 null = 非简单赋值判定
    /// （TryBuildSimpleAssignments=false 同样入表，发射端免重算）；后缀与发射端同源
    /// （GetSuffix(suffixMap, mapping)——Mask 字段名消费后缀）。发射端查表 miss 兜底现场计算
    /// （防御不变量，同 C2a 两表口径）。</para>
    /// </summary>
    public Dictionary<MappingSnapshot, List<string>?> SimpleAssignments { get; init; } = null!;

    /// <summary>
    /// 发射前准备段编排入口（RegisterSourceOutput 回调内、GenerateMapperCode 调用之前）—
    /// 内部序按现状时序（'：环检测先于反向表）：
    /// AssignMethodSuffixes → BuildTypeNameMap → DetectMutualPairs（上提三方法）→
    /// NestedCycleDetector.Prepare→ ReversePlanner.BuildReverseSnapshotTable→
    /// TypeRefDeepCopyClassifier.Classify（C4，产物入 DeepCopyGraphs）。
    /// </summary>
    /// <param name="spc">源生产上下文（C4 防御分支 NCMAP099 直报 + C1 异常 NCMAP099 + 取消令牌）</param>
    /// <param name="mappings">映射快照集合（纯值，缓存零符号）</param>
    /// <param name="globalConfig">全局映射配置快照（合并后；可为 null）</param>
    /// <param name="generationContext">生成期上下文（C3 NCMAP029 诊断收集）</param>
    /// <param name="diagnostics">per-run 诊断收集器（C1 反向诊断收集）</param>
    /// <returns>准备产物（6 字段）</returns>
    internal static GenerationPreparation Prepare(
        SourceProductionContext spc,
        System.Collections.Immutable.ImmutableArray<MappingSnapshot?> mappings,
        GlobalConfigSnapshot? globalConfig,
        GenerationContext? generationContext,
        DiagnosticCollector diagnostics)
    {
        // 上提三方法（原 GenerateMapperCode :43/:44/:48——纯函数，逻辑逐行不动）
        var suffixMap = CodeGenerator.AssignMethodSuffixes(mappings);
        var typeNameMap = CodeGenerator.BuildTypeNameMap(mappings, suffixMap);
        var mutualPairs = CodeGenerator.DetectMutualPairs(mappings);

        // C3 前移（原）：嵌套互调环检测（NCMAP029）——决策逻辑 补迁至本类 PlanCycleDetection
        // （原 NestedCycleDetector.Prepare 本体 + DFS 辅助逐行迁移；降级对写入 NestedCycleDetector 缓存）
        PlanCycleDetection(mappings, generationContext);

        // C1 前移（原）：反向快照唯一构造点（ReversePlanner 迁入；
        // mutualPairs 经产物携带——DetectMutualPairs 上提内部计算， '）
        var reverseSnapshotTable = ReversePlanner.BuildReverseSnapshotTable(
            spc, mappings, mutualPairs, globalConfig, diagnostics);

        // C4 前移（原 键收集 + nodeMap 合并 + Classify 循环三段整体前移）
        var deepCopyGraphs = BuildDeepCopyGraphs(spc, mappings, mutualPairs, typeNameMap);

        // C2a 前移：BuildInitOnlyAssignmentExpr / GenerateElementMappingExpression
        // 判定链前移——依赖 typeNameMap（已建）+ _degradedPairs（C3 已建）+ 反向表（C1 已建），
        // 时序与现状发射期查询一致
        var initOnlyPlans = new Dictionary<PropertyMappingSnapshot, InitOnlyExprPlan>();
        var elementPlans = new Dictionary<(TypeRef Source, TypeRef Target), ElementExprPlan>();
        BuildExprPlans(mappings, reverseSnapshotTable, typeNameMap, suffixMap, generationContext, initOnlyPlans, elementPlans);

        // B1-10 前移：MapList 四路径共享的简单赋值预扫描表
        //（C2a 前移形态——prep 段一次计算、发射端查表纯消费；依赖 suffixMap（:92 已建））
        var simpleAssignments = BuildSimpleAssignmentsTable(mappings, suffixMap);

        var preparation = new GenerationPreparation
        {
            MethodSuffixMap = suffixMap,
            TypeNameMap = typeNameMap,
            MutualPairs = mutualPairs,
            CycleDetectionPrepared = true,
            ReverseSnapshotTable = reverseSnapshotTable,
            DeepCopyGraphs = deepCopyGraphs,
            InitOnlyExprPlans = initOnlyPlans,
            ElementExprPlans = elementPlans,
            SimpleAssignments = simpleAssignments,
        };

        // ctx 通道挂载：发射方法族经 ctx.Preparation 查表（GenerationContext 收纳先例，
        // 避免方法族 typeNameMap 签名全量传染）
        if (generationContext != null)
            generationContext.Preparation = preparation;
        return preparation;
    }

    /// <summary>
    /// 嵌套互调环检测决策（W3_1， 补迁 / C3 完整面）
    /// 原 <c>NestedCycleDetector.Prepare</c> 方法本体逐行前移：
    /// 构建嵌套属性委托依赖图（边=嵌套注册对，IsMapFromRegistered 口径与生成分发链 1:1——S9）+
    /// DFS 检测长度 ≥2 互调环（A→B 与 B→A 经嵌套属性委托互调——生成代码互调无深度计数，
    /// 成环数据 StackOverflow；Q7 同类）+ 环上全部注册对降级（NCMAP029 Info 诊断）。
    /// <para>降级对写入 <see cref="NestedCycleDetector"/> 缓存（Reset + TryMarkDegraded——
    /// 原 _degradedPairs ContainsKey+写入合并为 TryAdd 语义，幂等不变）；发射期 IsDegraded 查询点零改动。
    /// 直接自引用（长度 1）由 IsSelfRecursiveMapping 深度保护维持（P0，不降级——B1）；
    /// 集合元素边不纳入图（与 Q7 既有降级不叠加）。原签名带 typeNameMap（口径核验预留，
    /// 方法体未引用），迁移时省略——行为等价。</para>
    /// </summary>
    /// <param name="mappings">全量映射快照</param>
    /// <param name="ctx">生成上下文（NCMAP029 诊断输出）</param>
    internal static void PlanCycleDetection(
        System.Collections.Immutable.ImmutableArray<MappingSnapshot?> mappings,
        GenerationContext? ctx)
    {
        NestedCycleDetector.Reset();

        // 1. 收集注册对（跳过 SkipCodeGeneration）
        var registered = new List<(TypeRef Src, TypeRef Tgt, MappingSnapshot Mapping)>();
        foreach (var m in mappings)
        {
            if (m == null || m.SkipCodeGeneration) continue;
            registered.Add((m.SourceType, m.TargetType, m));
        }
        if (registered.Count == 0) return;

        // 2. 嵌套边邻接表：registered[i] → 其嵌套注册对（走委托的）下标集合
        var adj = new List<List<int>>(registered.Count);
        for (int i = 0; i < registered.Count; i++) adj.Add(new List<int>());

        for (int i = 0; i < registered.Count; i++)
        {
            var mapping = registered[i].Mapping;
            foreach (var pm in mapping.PropertyMappings)
            {
                var propSrc = pm.Source?.Type;
                if (propSrc == null) continue;
                var propTgt = pm.Target.Type;
                // 仅嵌套对象属性边（集合元素边不纳入——S6）；注册对走委托（与 CodeGenerator.IsMapFromRegistered 同口径——S9）
                if (!TypeRefHelpers.IsNestedClassProperty(propSrc, propTgt)) continue;
                if (!IsRegisteredPair(propSrc, propTgt)) continue;
                for (int j = 0; j < registered.Count; j++)
                {
                    if (TypeRefHelpers.IsSameType(registered[j].Src, propSrc)
                        && TypeRefHelpers.IsSameType(registered[j].Tgt, propTgt))
                    {
                        adj[i].Add(j);
                        break;
                    }
                }
            }
        }

        // 3. DFS 检测互调环（长度 ≥2；长度 1 自引用跳过——深度保护维持 B1）
        // 迭代 DFS + 跨起点 visited 记忆化 + 每起点预算
        //（缺陷态：递归无记忆化无预算——密集图重复探索指数爆炸 + 10_001 深链递归直接
        // StackOverflow 进程崩溃 0xC0000005 实测；visited 跨起点传递）
        var path = new List<int>();
        var inPath = new HashSet<int>();
        var visited = new HashSet<int>();
        for (int start = 0; start < registered.Count; start++)
        {
            DfsDetect(start, registered, adj, path, inPath, visited, ctx);
        }
    }

    /// <summary>
    /// 判定嵌套属性对是否为注册对（走 GeneratedMapper 委托）——与 CodeGenerator.IsMapFromRegistered
    /// 同口径：目标类型标注了 [MapFrom(typeof(源类型))]（支持多源 DTO，任一匹配即可）
    /// </summary>
    /// <param name="sourceType">源属性类型</param>
    /// <param name="nestedTgtType">目标属性类型</param>
    /// <returns>注册对返回 true</returns>
    private static bool IsRegisteredPair(TypeRef sourceType, TypeRef nestedTgtType)
        => nestedTgtType.MapFromSources.Any(s => TypeRefHelpers.IsSameType(s, sourceType));

    /// <summary>
    /// DFS 检测环：路径栈命中 inPath 节点即发现环，长度 ≥2 时降级环上全部边。
    /// <para>B4-09迭代化：显式栈 (node, exit) 替代递归
    ///（10_001 深链形态递归版 StackOverflow 进程崩溃 0xC0000005——实测，比审计登记的
    /// 指数爆炸更烈）；跨起点 visited 记忆化（完全探索且无环的子图免重复进入——密集图
    /// 重复探索爆炸根源消除）；每起点 NodeBudget 访问预算（先例 =
    /// TypeRefDeepCopyClassifier.NodeBudget 同款纪律），超限该起点"未决"中止
    ///（清 path/inPath/栈——栈中未回溯节点不落 visited；F63，不发射 NCMAP029、不标记降级）。
    /// 遍历序保原递归形（子节点逆序 push → 正序 pop——环发现序与 NCMAP029 诊断序不变）。</para>
    /// </summary>
    /// <param name="start">起点节点下标</param>
    /// <param name="registered">注册对列表</param>
    /// <param name="adj">嵌套边邻接表</param>
    /// <param name="path">当前路径栈（节点下标；每起点方法内重建）</param>
    /// <param name="inPath">路径栈成员集合（每起点方法内重建）</param>
    /// <param name="visited">跨起点完全探索集合（exit 弹出时落档；预算中止的未完全探索节点不落）</param>
    /// <param name="ctx">生成上下文（诊断输出）</param>
    private static void DfsDetect(int start, List<(TypeRef Src, TypeRef Tgt, MappingSnapshot Mapping)> registered,
        List<List<int>> adj, List<int> path, HashSet<int> inPath, HashSet<int> visited, GenerationContext? ctx)
    {
        // 每起点访问预算：与 TypeRefDeepCopyClassifier.NodeBudget 同款防御纪律
        const int NodeBudget = 10_000;
        path.Clear();
        inPath.Clear();
        var stack = new Stack<(int Node, bool Exit)>();
        stack.Push((start, false));
        int visits = 0;
        while (stack.Count > 0)
        {
            var (node, exit) = stack.Pop();
            if (exit)
            {
                // 回溯：节点所有子树完全探索且无环 → 跨起点记忆化落档
                path.RemoveAt(path.Count - 1);
                inPath.Remove(node);
                visited.Add(node);
                continue;
            }

            if (inPath.Contains(node))
            {
                // 发现环：path 中 node 首次出现位置到末尾 + 回到 node（同原递归版语义）
                int idx = path.IndexOf(node);
                var cycle = path.Skip(idx).Append(node).ToList();
                // 长度 ≥2 才降级（长度 1 = 直接自引用，深度保护维持）
                if (cycle.Count - 1 >= 2)
                {
                    DegradeCycle(cycle, registered, ctx);
                }
                continue;
            }

            // 剪枝（免预算）：他起点已完全探索且无环的子图不再进入
            if (visited.Contains(node))
                continue;

            if (++visits > NodeBudget)
            {
                // 预算耗尽：该起点环检测未决——探索状态全清（栈中未回溯的 exit 标记随之
                // 丢弃，未完全探索节点不落 visited），F63 advisory
                stack.Clear();
                path.Clear();
                inPath.Clear();
                if (ctx != null)
                {
                    ctx.Diagnostics.Add(registered[start].Mapping, new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptorsFluent.CycleDetectionBudgetExceededRule.Id,
                        Args = new[]
                        {
                            $"{registered[start].Src.FullName} → {registered[start].Tgt.FullName}",
                            NodeBudget.ToString(),
                        },
                    });
                }
                return;
            }

            inPath.Add(node);
            path.Add(node);
            stack.Push((node, true)); // 回溯标记（弹出时 visited 落档）
            for (int k = adj[node].Count - 1; k >= 0; k--)
                stack.Push((adj[node][k], false)); // 逆序 push → 正序 pop（保原递归遍历序）
        }
    }

    /// <summary>
    /// 降级环上全部边（注册对）并输出 NCMAP029 诊断（幂等：已降级对不重复输出——
    /// TryMarkDegraded 返回 false 即已降级，跳过诊断）
    /// </summary>
    /// <param name="cycle">环路径（v0..vn,v0）</param>
    /// <param name="registered">注册对列表</param>
    /// <param name="ctx">生成上下文（诊断输出）</param>
    private static void DegradeCycle(List<int> cycle, List<(TypeRef Src, TypeRef Tgt, MappingSnapshot Mapping)> registered,
        GenerationContext? ctx)
    {
        for (int k = 0; k < cycle.Count - 1; k++)
        {
            var from = registered[cycle[k]];
            var to = registered[cycle[k + 1]];
            // 原 _degradedPairs!.ContainsKey + 写入 → TryMarkDegraded（TryAdd 语义，行为等价）
            if (!NestedCycleDetector.TryMarkDegraded(from.Src, from.Tgt))
            {
                continue;
            }

            if (ctx != null)
            {
                ctx.Diagnostics.Add(from.Mapping, new DiagnosticSnapshot
                {
                    DescriptorId = DiagnosticDescriptors.NestedCycleDegradedRule.Id,
                    Args = new[]
                    {
                        $"{from.Src.FullName} → {from.Tgt.FullName}",
                        $"{to.Src.FullName} → {to.Tgt.FullName}",
                    },
                });
            }
        }
    }

    /// <summary>
    /// C4 深拷贝图分类 — 三段原样前移（键收集 + nodeMap 合并 + Classify 循环含防御分支），
    /// 遍历序与原 GenerateMapperCode 一致（Dictionary 插入序——发射序逐字节等价的前提）
    /// </summary>
    /// <param name="spc">源生产上下文（防御分支 NCMAP099 直报 + 取消令牌）</param>
    /// <param name="mappings">映射快照集合</param>
    /// <param name="mutualPairs">互指有向对字典</param>
    /// <param name="typeNameMap">类型 → "类名+后缀" 映射表（统一分配的名称）</param>
    /// <returns>TypeRef → (TypeName, Node, Shape, UnknownEdges)</returns>
    private static Dictionary<TypeRef, (string TypeName, DeepCopyNodeShape Node, DeepCopyGraphShape Shape, IReadOnlyList<UnknownEdgeSnapshot> UnknownEdges)> BuildDeepCopyGraphs(
        SourceProductionContext spc,
        System.Collections.Immutable.ImmutableArray<MappingSnapshot?> mappings,
        Dictionary<TypeRef, TypeRef> mutualPairs,
        Dictionary<TypeRef, string> typeNameMap)
    {
        // 段1 键收集（原主循环 :123-127——过滤条件逐行同构：null / SkipCodeGeneration / 互指对跳过）
        var deepCopyTypes = new Dictionary<TypeRef, string>();
        foreach (var mapping in mappings)
        {
            if (mapping == null) continue;
            if (mapping.SkipCodeGeneration) continue;
            if (CodeGenerator.IsMutualPair(mapping, mutualPairs)) continue;

            // 使用 typeNameMap 中已统一分配的名称，避免同一类型在不同 mapping 中后缀不同
            if (!deepCopyTypes.ContainsKey(mapping.SourceType))
                deepCopyTypes[mapping.SourceType] = CodeGenerator.GetTypeNameWithSuffix(mapping.SourceType, typeNameMap);
            if (!deepCopyTypes.ContainsKey(mapping.TargetType))
                deepCopyTypes[mapping.TargetType] = CodeGenerator.GetTypeNameWithSuffix(mapping.TargetType, typeNameMap);
        }

        // 段2 nodeMap 合并（原 :149-160——各 mapping 物化的 DeepCopy 节点图合并为全量节点表，EqualityKey 键控）
        var deepCopyNodeMap = new Dictionary<string, DeepCopyNodeShape>(StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            if (mapping == null) continue;
            foreach (var node in mapping.DeepCopyNodes)
            {
                var nk = node.Type.EqualityKey;
                if (!deepCopyNodeMap.ContainsKey(nk))
                    deepCopyNodeMap[nk] = node;
            }
        }

        // 段3 Classify 循环（原 :161-179；防御分支 随前移迁入——spc 可用，机制等价）
        // 跨根共享 done 集——仅 Acyclic 结局根并入（Classify
        // 尾部合并），后根对共享子树跳过探索（无 Unknown 边、无环——零行为差）；onPath 保持
        // per-call（路径语义不可共享， 明示）
        var sharedDone = new HashSet<string>(StringComparer.Ordinal);
        var deepCopyGraphs = new Dictionary<TypeRef, (string TypeName, DeepCopyNodeShape Node, DeepCopyGraphShape Shape, IReadOnlyList<UnknownEdgeSnapshot> UnknownEdges)>();
        foreach (var kvp in deepCopyTypes)
        {
            spc.CancellationToken.ThrowIfCancellationRequested();
            if (!deepCopyNodeMap.TryGetValue(kvp.Key.EqualityKey, out var node))
            {
                // 防御：节点缺失（不应发生——物化闭包已覆盖 deepCopyTypes 收集集）
                // Node=null 标记防御条目：发射端跳过方法生成（原 continue 等价），注册行仍输出（acyclic:false）
                deepCopyGraphs[kvp.Key] = (kvp.Value, null!, DeepCopyGraphShape.Unknown, Array.Empty<UnknownEdgeSnapshot>());
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.GenerationFailedRule,
                    Location.None,
                    kvp.Key.FullName, kvp.Key.FullName, "DeepCopy 节点物化缺失（S3 闭包断言失败）"));
                continue;
            }
            var (shape, unknownEdges) = TypeRefDeepCopyClassifier.Classify(kvp.Key, deepCopyNodeMap, sharedDone);
            deepCopyGraphs[kvp.Key] = (kvp.Value, node, shape, unknownEdges);
        }
        return deepCopyGraphs;
    }

    /// <summary>
    /// C2a 表达式化计划编排— 遍历正向 + 反向 mapping 全部属性映射，
    /// 填充 init-only 计划表（pm 键）与元素映射计划表（类型对键）。
    /// <para>覆盖面 = 发射期查询集的超集（发射方法族消费的 mapping 集 = 主循环正向 mapping +
    /// 其 reverseSnapshotTable 反向值，本编排按非 null 全量枚举——被 SkipCodeGeneration/
    /// IsMutualPair 过滤的 mapping 多算无害，不查询不命中）；枚举遗漏由发射端 miss 兜底防御。</para>
    /// </summary>
    /// <param name="mappings">映射快照集合（正向）</param>
    /// <param name="reverseSnapshotTable">反向快照表（C1 产物）</param>
    /// <param name="typeNameMap">类型 → "类名+后缀" 映射表（决策期方法名派生依赖）</param>
    /// <param name="suffixMap">对级后缀表（B1-05——注册对方法名派生改对级键）</param>
    /// <param name="initOnlyPlans">init-only 表达式化计划表（输出，调用方先建空表）</param>
    /// <param name="elementPlans">元素映射表达式计划表（输出，调用方先建空表）</param>
    private static void BuildExprPlans(
        System.Collections.Immutable.ImmutableArray<MappingSnapshot?> mappings,
        Dictionary<MappingSnapshot, MappingSnapshot?> reverseSnapshotTable,
        Dictionary<TypeRef, string> typeNameMap,
        Dictionary<string, string> suffixMap,
        GenerationContext? generationContext,
        Dictionary<PropertyMappingSnapshot, InitOnlyExprPlan> initOnlyPlans,
        Dictionary<(TypeRef Source, TypeRef Target), ElementExprPlan> elementPlans)
    {
        foreach (var mapping in mappings)
        {
            if (mapping == null) continue;
            CollectExprPlans(mapping, typeNameMap, suffixMap, generationContext, initOnlyPlans, elementPlans);
            if (reverseSnapshotTable.TryGetValue(mapping, out var reverse) && reverse != null)
                CollectExprPlans(reverse, typeNameMap, suffixMap, generationContext, initOnlyPlans, elementPlans);
        }
    }

    /// <summary>
    /// B1-10 简单赋值预扫描表构建— 对全部非 null 正向 mapping
    /// 各执行一次 TryBuildSimpleAssignments（与发射端同参：mapping 实例 + GetSuffix 后缀——
    /// Mask 字段名消费后缀，同源保证文本等价），结果连同"非简单赋值"判定一并入表；
    /// 键 = MappingSnapshot（值相等，同 ReverseSnapshotTable 键语义——同内容实例判定恒同，
    /// 无后缀错配面）。
    /// </summary>
    private static Dictionary<MappingSnapshot, List<string>?> BuildSimpleAssignmentsTable(
        System.Collections.Immutable.ImmutableArray<MappingSnapshot?> mappings,
        Dictionary<string, string> suffixMap)
    {
        var table = new Dictionary<MappingSnapshot, List<string>?>();
        foreach (var mapping in mappings)
        {
            if (mapping == null || table.ContainsKey(mapping)) continue;
            table[mapping] = CodeGenerator.TryBuildSimpleAssignments(
                mapping, out var assignments, CodeGenerator.GetSuffix(suffixMap, mapping))
                ? assignments
                : null;
        }
        return table;
    }

    /// <summary>
    /// 单 mapping 的表达式化计划收集 — init-only 目标属性逐 pm 计划；
    /// 集合元素对 / 字典 K/V 对无条件提取（形态判定在发射端分发链序位命中才消费，超集无害）。
    /// </summary>
    private static void CollectExprPlans(
        MappingSnapshot mapping,
        Dictionary<TypeRef, string> typeNameMap,
        Dictionary<string, string> suffixMap,
        GenerationContext? generationContext,
        Dictionary<PropertyMappingSnapshot, InitOnlyExprPlan> initOnlyPlans,
        Dictionary<(TypeRef Source, TypeRef Target), ElementExprPlan> elementPlans)
    {
        foreach (var pm in mapping.PropertyMappings)
        {
            // init-only 表达式化计划（发射端查询集 = IsInitOnly=true 且通过调用方前置过滤的 pm）
            if (pm.Target.IsInitOnly == true && !initOnlyPlans.ContainsKey(pm))
                initOnlyPlans[pm] = PlanInitOnlyExpr(pm, typeNameMap, suffixMap, generationContext, mapping);

            // 元素映射计划：仅对有源的 pm 提取（Source==null 的 pm 走 Converter/Mask 分支，不查询元素表）
            if (pm.Source == null) continue;
            var effSrcType = CodeGenerator.GetEffectiveSourceType(pm);
            if (effSrcType == null) continue;

            // 集合元素对（分发链 ⑬ 的提取序——TryGetCollectionElementTypes 判定同现状发射端）
            if (TypeRefHelpers.TryGetCollectionElementTypes(effSrcType, pm.Target.Type, out var elemSrc, out var elemTgt)
                && elemSrc != null && elemTgt != null)
            {
                var elemKey = (elemSrc, elemTgt);
                if (!elementPlans.ContainsKey(elemKey))
                    elementPlans[elemKey] = PlanElementExpr(elemSrc, elemTgt, typeNameMap, suffixMap);
            }

            // 字典 K/V 对（分发链 ⑭ 的提取序——双方字典形态判定同现状发射端）
            if (TypeRefHelpers.TryGetDictionaryKeyValueTypes(effSrcType, out var srcKey, out var srcVal)
                && TypeRefHelpers.TryGetDictionaryKeyValueTypes(pm.Target.Type, out var tgtKey, out var tgtVal))
            {
                if (srcKey != null && tgtKey != null)
                {
                    var keyPair = (srcKey, tgtKey);
                    if (!elementPlans.ContainsKey(keyPair))
                        elementPlans[keyPair] = PlanElementExpr(srcKey, tgtKey, typeNameMap, suffixMap);
                }
                if (srcVal != null && tgtVal != null)
                {
                    var valPair = (srcVal, tgtVal);
                    if (!elementPlans.ContainsKey(valPair))
                        elementPlans[valPair] = PlanElementExpr(srcVal, tgtVal, typeNameMap, suffixMap);
                }
            }
        }
    }

    /// <summary>
    /// init-only 表达式化决策（C2a 前移）— 原 BuildInitOnlyAssignmentExpr 判定链逐行迁移
    /// （CodeGenerator.Mapping.cs:658-736），输出路径计划；发射端按 Kind 纯模板渲染。
    /// <para>枚举分支预计算完整表达式（TryBuildEnumConversionExpr 与 srcAccess 强耦合的
    /// 5 子分支判定与表达式发射整体保留决策期，避免双调用与判定复制分叉）。</para>
    /// </summary>
    /// <param name="pm">属性映射信息</param>
    /// <param name="typeNameMap">类型 → "类名+后缀" 映射表</param>
    /// <param name="suffixMap">对级后缀表（B1-05——嵌套/集合注册对方法名派生改对级键）</param>
    /// <returns>表达式化路径计划（NotExpressible = 无法单表达式化，调用方降级诊断）</returns>
    internal static InitOnlyExprPlan PlanInitOnlyExpr(PropertyMappingSnapshot pm, Dictionary<TypeRef, string> typeNameMap, Dictionary<string, string>? suffixMap = null, GenerationContext? ctx = null, MappingSnapshot? diagMapping = null)
    {
        if (pm.Source == null)
            return new InitOnlyExprPlan { Kind = InitOnlyExprKind.NotExpressible };

        // Format 判定（直接表达式化，
        // 探针 1/2 实证 Condition(HasValue, …)/装箱接口调用形态可构建可编译）——
        // 显式声明短路（镜像运行时 PlanCore 3.5 步：Format 判定在类型形态决策之前）；
        // FormatThenMask（+Mask 双标）沿用 Mask/Convert 表达式版 NotExpressible 降级语句版口径；
        // 无效源（编译期 043/044 已拦的理论路径）→ fall through 落常规转换链（生成侧守卫口径）
        if (pm.FormatString != null)
        {
            if (pm.MaskType != null)
                return new InitOnlyExprPlan { Kind = InitOnlyExprKind.NotExpressible };
            if (pm.Target.Type.SpecialType == SpecialType.System_String
                && IsFormattableTypeRef(CodeGenerator.GetEffectiveSourceType(pm)!))
            {
                return new InitOnlyExprPlan { Kind = InitOnlyExprKind.Format };
            }
        }

        // 类型判据统一用有效源类型（末段索引=元素类型，与 srcAccess 表达式类型一致）
        var effSrcType = CodeGenerator.GetEffectiveSourceType(pm)!;
        var srcPropType = effSrcType.FullName;
        var tgtPropType = pm.Target.Type.FullName;

        // 同类型直接赋值（标量——排除集合/字典/string 外 IEnumerable）
        if (TypeRefHelpers.IsSameType(effSrcType, pm.Target.Type)
            && !TypeRefHelpers.IsDictionaryType(effSrcType)
            && (effSrcType.SpecialType == SpecialType.System_String
                || !TypeRefHelpers.ImplementsIEnumerable(effSrcType)))
        {
            return new InitOnlyExprPlan { Kind = InitOnlyExprKind.SameTypeDirect };
        }

        // 隐式转换（int → long 等）
        if (TypeRefHelpers.IsImplicitConvertible(effSrcType, pm.Target.Type))
        {
            return new InitOnlyExprPlan { Kind = InitOnlyExprKind.ImplicitCast };
        }

        // Nullable→Nullable 同底层直接赋值
        if (TypeRefHelpers.IsNullableToNullable(effSrcType, pm.Target.Type))
        {
            var underlyingSrc = TypeRefHelpers.GetUnderlyingType(effSrcType);
            var underlyingTgt = TypeRefHelpers.GetUnderlyingType(pm.Target.Type);
            if (TypeRefHelpers.IsSameType(underlyingSrc, underlyingTgt))
            {
                return new InitOnlyExprPlan { Kind = InitOnlyExprKind.NullableSameUnderlying };
            }
        }

        // 嵌套对象（注册对 → GeneratedMapper 委托；未注册 → 运行时回退 + null 守卫三元）
        if (TypeRefHelpers.IsNestedClassProperty(effSrcType, pm.Target.Type))
        {
            // 互调环降级——环上注册对返回 null（调用方降级诊断 NCMAP027
            // 叠加输出；NCMAP029 已在 Prepare 阶段输出——语义以 NCMAP029 为准）
            if (NestedCycleDetector.IsDegraded(effSrcType, pm.Target.Type))
            {
                return new InitOnlyExprPlan { Kind = InitOnlyExprKind.NotExpressible };
            }

            // 嵌套调用名改对级后缀查表（同 Collections 嵌套站点口径；
            // miss/注册性判定维持 IsMapFromRegistered，未注册走 RuntimeFallback 不消费方法名）
            var pairSuffix = CodeGenerator.GetPairSuffix(effSrcType!, pm.Target.Type!, suffixMap);
            var nestedSrcName = effSrcType!.SimpleName + pairSuffix;
            var nestedTgtName = pm.Target.Type!.SimpleName + pairSuffix;
            var nestedMethodName = $"Map_{nestedSrcName}_To_{nestedTgtName}";
            if (CodeGenerator.IsMapFromRegistered(effSrcType, pm.Target.Type))
            {
                return new InitOnlyExprPlan { Kind = InitOnlyExprKind.NestedRegistered, RegisteredMethodName = nestedMethodName };
            }
            return new InitOnlyExprPlan
            {
                Kind = InitOnlyExprKind.NestedRuntimeFallback,
                RuntimeMapTypeParams = $"{srcPropType}, {tgtPropType}",
            };
        }

        // 集合（注册对 → MapList 委托）
        if (TypeRefHelpers.TryGetCollectionElementTypes(effSrcType, pm.Target.Type, out var elemSrc, out var elemTgt))
        {
            var isRegisteredPair = elemSrc is TypeRef elemSrcNamed && elemTgt is TypeRef elemTgtNamed
                && typeNameMap.ContainsKey(elemSrcNamed) && typeNameMap.ContainsKey(elemTgtNamed);
            if (isRegisteredPair)
            {
                // 元素对方法名改对级后缀查表（注册性判定维持 typeNameMap 存在性语义）
                var elemPairSuffix = CodeGenerator.GetPairSuffix(elemSrc!, elemTgt!, suffixMap);
                var elemSrcName = elemSrc!.SimpleName + elemPairSuffix;
                var elemTgtName = elemTgt!.SimpleName + elemPairSuffix;
                return new InitOnlyExprPlan
                {
                    Kind = InitOnlyExprKind.CollectionRegistered,
                    RegisteredMethodName = $"MapList_{elemSrcName}_To_{elemTgtName}",
                };
            }
        }

        // 枚举转换（init-only 表达式化）
        // 与 TryGenerateEnumConversion 语句版同判定同语义（5 分支：switch/退避 ToString/精确匹配/throw/强转）；
        // srcAccess 决策期可算（BuildSourceAccess 为 pm 纯函数），表达式整体预计算入 Plan
        // B1-13-ctx/mapping 透传（表达式路径枚举冲突退化诊断 NCMAP023/024 发射单点）
        var enumExpr = CodeGenerator.TryBuildEnumConversionExpr(pm, CodeGenerator.BuildSourceAccess(pm), ctx, diagMapping);
        if (enumExpr != null)
        {
            return new InitOnlyExprPlan { Kind = InitOnlyExprKind.Enum, EnumExpr = enumExpr };
        }

        // 其他分支（字典/Json/Mask/ConvertUsing/未注册集合内联）——降级
        return new InitOnlyExprPlan { Kind = InitOnlyExprKind.NotExpressible };
    }

    /// <summary>
    /// TypeRef 是否满足 IFormattable 约束
    /// DateTime/DateTimeOffset/TimeSpan/Guid/数值/枚举白名单 + Nullable&lt;T&gt; 解包 +
    /// IFormattable 接口声明源。语句版守卫与 init-only 表达式版判定共用（两渲染点同一口径）。
    /// <para>枚举经 TypeKind.Enum 判定（Enum 实现 IFormattable，net8/9/10 探针实测一致）。</para>
    /// </summary>
    internal static bool IsFormattableTypeRef(TypeRef t)
    {
        if (t == null) return false;
        if (t.IsNullableValueType)
            return t.UnderlyingType != null && IsFormattableTypeRef(t.UnderlyingType);
        switch (t.SpecialType)
        {
            case SpecialType.System_DateTime:
            case SpecialType.System_Decimal:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
                return true;
        }
        var fqn = t.FullName;
        if (fqn == "System.DateTimeOffset" || fqn == "System.TimeSpan" || fqn == "System.Guid")
            return true;
        // net7+ 数值类型（Half/Int128/UInt128）——netstandard2.0 引用的 Roslyn SpecialType 无对应成员，
        // 以全名判定兜底（半角浮点与 128 位整数均实现 IFormattable）
        if (fqn == "System.Half" || fqn == "System.Int128" || fqn == "System.UInt128")
            return true;
        if (fqn == "System.IFormattable")
            return true;
        if (t.Kind == TypeKind.Enum)
            return true;
        return false;
    }

    /// <summary>
    /// 元素映射表达式决策（C2a 前移）— 原 GenerateElementMappingExpression 判定链逐行迁移
    /// （原方法在 CodeGenerator.Mapping，已由本方法承接），输出路径计划；发射端
    /// CodeGenerator.Collections.RenderElementMappingExpression 按 Kind 纯模板渲染（类型串渲染期从元素类型读取，非决策）。
    /// </summary>
    /// <param name="srcElemType">源元素类型</param>
    /// <param name="tgtElemType">目标元素类型</param>
    /// <param name="typeNameMap">类型 → "类名+后缀" 映射表（注册对方法名派生依赖）</param>
    /// <param name="suffixMap">对级后缀表（B1-05——嵌套注册对方法名派生改对级键）</param>
    /// <returns>元素映射路径计划</returns>
    internal static ElementExprPlan PlanElementExpr(TypeRef srcElemType, TypeRef tgtElemType, Dictionary<TypeRef, string> typeNameMap, Dictionary<string, string>? suffixMap = null)
    {
        // 同类型直接赋值
        if (TypeRefHelpers.IsSameType(srcElemType, tgtElemType))
            return new ElementExprPlan { Kind = ElementExprKind.SameTypeDirect };

        // 隐式数值转换（int→long 等）
        if (TypeRefHelpers.IsImplicitConvertible(srcElemType, tgtElemType))
            return new ElementExprPlan { Kind = ElementExprKind.ImplicitCast };

        // 可空→非可空值类型
        if (TypeRefHelpers.IsNullableToNonNullable(srcElemType, tgtElemType))
            return new ElementExprPlan { Kind = ElementExprKind.NullableToNonNullable };

        // 非可空→可空值类型
        if (TypeRefHelpers.IsNonNullableToNullable(srcElemType, tgtElemType))
        {
            // 检查底层类型兼容性
            var underlyingTgt = TypeRefHelpers.GetUnderlyingType(tgtElemType)!;
            if (TypeRefHelpers.IsSameType(srcElemType, underlyingTgt)
                || TypeRefHelpers.IsImplicitConvertible(srcElemType, underlyingTgt))
                return new ElementExprPlan { Kind = ElementExprKind.NonNullableToNullableCompatible };
            return new ElementExprPlan { Kind = ElementExprKind.RuntimeConvert };
        }

        // 嵌套 class 映射 — 优先调用源生成器方法，回退到运行时 Mapper.Map
        if (TypeRefHelpers.IsNestedClassProperty(srcElemType, tgtElemType))
        {
            // 元素嵌套调用名改对级后缀查表（同嵌套站点口径）
            var pairSuffix = CodeGenerator.GetPairSuffix(srcElemType, tgtElemType, suffixMap);
            var srcName = srcElemType.SimpleName + pairSuffix;
            var tgtName = tgtElemType.SimpleName + pairSuffix;
            var methodName = $"Map_{srcName}_To_{tgtName}";
            if (CodeGenerator.IsMapFromRegistered(srcElemType, tgtElemType))
                return new ElementExprPlan { Kind = ElementExprKind.NestedRegistered, RegisteredMethodName = methodName };
            // Mapper.Map 的 TSource/TTarget 带 class 约束，元素类型可空注解（如 ItemDto?）违反约束（CS8634）
            return new ElementExprPlan { Kind = ElementExprKind.NestedRuntimeFallback };
        }

        // 其他类型回退到 TypeConvertHelper
        return new ElementExprPlan { Kind = ElementExprKind.RuntimeConvert };
    }
}

/// <summary>
/// init-only 表达式化路径判定（C2a 决策前移）— 原 BuildInitOnlyAssignmentExpr
/// 发射期判定链的决策输出；发射端按 Kind 分发纯模板渲染，不再判定。
/// </summary>
internal enum InitOnlyExprKind
{
    /// <summary>
    /// 无法单表达式化（Source 为 null / 互调环降级 / 复杂多语句分支：字典/Json/Mask/ConvertUsing/
    /// FormatThenMask 双标/未注册集合内联）——调用方降级保持默认值 + NCMAP028 Info 诊断
    /// </summary>
    NotExpressible,

    /// <summary>
    /// 格式化赋值（[MapFormat] / Fluent .Format 单标
    /// src.Prop?.ToString("fmt", null) 纯单表达式，直接表达式化；可空源 ?. 空条件调用）
    /// </summary>
    Format,

    /// <summary>
    /// 同类型标量直接赋值（排除集合/字典/string 外 IEnumerable）
    /// </summary>
    SameTypeDirect,

    /// <summary>
    /// 隐式转换强转（int → long 等）
    /// </summary>
    ImplicitCast,

    /// <summary>
    /// Nullable→Nullable 同底层类型直接赋值（int? → int?）
    /// </summary>
    NullableSameUnderlying,

    /// <summary>
    /// 嵌套注册对 → GeneratedMapper 委托（[MapFrom] 注册）
    /// </summary>
    NestedRegistered,

    /// <summary>
    /// 嵌套未注册 → 运行时回退 + null 守卫三元（NCMapper.Mapper.Map）
    /// </summary>
    NestedRuntimeFallback,

    /// <summary>
    /// 集合注册对 → MapList 委托
    /// </summary>
    CollectionRegistered,

    /// <summary>
    /// 枚举转换（5 子分支：switch/退避 ToString/精确匹配/throw/强转）——
    /// 表达式决策期经 TryBuildEnumConversionExpr 整体预计算入 <see cref="InitOnlyExprPlan.EnumExpr"/>
    /// </summary>
    Enum,
}

/// <summary>
/// init-only 表达式化计划（C2a 决策前移）— 承载决策结果（Kind + 决策期派生参数），
/// 发射端按 Kind 纯模板渲染（srcAccess 渲染期经 BuildSourceAccess 构造）。
/// </summary>
internal sealed class InitOnlyExprPlan
{
    /// <summary>
    /// 表达式化路径判定
    /// </summary>
    public InitOnlyExprKind Kind { get; init; }

    /// <summary>
    /// 注册委托方法名（含 Map_ / MapList_ 前缀；NestedRegistered / CollectionRegistered 分支用）
    /// </summary>
    public string? RegisteredMethodName { get; init; }

    /// <summary>
    /// NestedRuntimeFallback 的 Mapper.Map 泛型类型参数串（"源类型全名, 目标类型全名"）
    /// </summary>
    public string? RuntimeMapTypeParams { get; init; }

    /// <summary>
    /// 枚举分支预计算表达式（决策期经 TryBuildEnumConversionExpr 构造，含 srcAccess 代入）
    /// </summary>
    public string? EnumExpr { get; init; }
}

/// <summary>
/// 元素映射表达式路径判定（C2a 决策前移）— 原 GenerateElementMappingExpression
/// 发射期判定链的决策输出；发射端按 Kind 分发纯模板渲染（类型串从元素类型读取）。
/// </summary>
internal enum ElementExprKind
{
    /// <summary>
    /// 同类型直接赋值
    /// </summary>
    SameTypeDirect,

    /// <summary>
    /// 隐式数值转换强转（int→long 等）
    /// </summary>
    ImplicitCast,

    /// <summary>
    /// 可空→非可空值类型（HasValue 三元 + default 目标）
    /// </summary>
    NullableToNonNullable,

    /// <summary>
    /// 非可空→可空值类型且底层兼容（new Nullable 构造）
    /// </summary>
    NonNullableToNullableCompatible,

    /// <summary>
    /// 嵌套注册对 → GeneratedMapper 委托（[MapFrom] 注册）
    /// </summary>
    NestedRegistered,

    /// <summary>
    /// 嵌套未注册 → 运行时回退（NCMapper.Mapper.Map，AOT 回退点）
    /// </summary>
    NestedRuntimeFallback,

    /// <summary>
    /// 其他类型回退 TypeConvertHelper.ConvertValue（AOT 回退点）
    /// </summary>
    RuntimeConvert,
}

/// <summary>
/// 元素映射表达式计划（C2a 决策前移）— 承载决策结果（Kind + 决策期派生参数），
/// 发射端按 Kind 纯模板渲染（FullName / NonNullableFullName 等类型串渲染期从元素类型读取）。
/// </summary>
internal sealed class ElementExprPlan
{
    /// <summary>
    /// 元素映射路径判定
    /// </summary>
    public ElementExprKind Kind { get; init; }

    /// <summary>
    /// 注册委托方法名（含 Map_ 前缀；NestedRegistered 分支用）
    /// </summary>
    public string? RegisteredMethodName { get; init; }
}
