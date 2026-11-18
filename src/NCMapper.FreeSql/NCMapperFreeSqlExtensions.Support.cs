// NCMapper FreeSql 扩展包 — AsDto 终端操作 + 自动降级
// 用法：fsql.Select<OrderEntity>().Where(...).AsDto().ToList<OrderDto>()
// 依赖：NCMapper + FreeSql
#pragma warning disable NCMapper001 // Compose 为实验性 API，扩展包内部使用

using NCMapper.Runtime;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Internal;

namespace NCMapper.FreeSql;

// 自 NCMapperFreeSqlExtensions.cs 按关切拆分（纯移动，类型与成员逐字不动）。

/// <summary>
/// Compose 选择器共享执行器 — 2/3 表选择器的降级判定 / null 过滤 + 映射 / 自动 Include 收敛为单点。
/// <para>Trace 文案与逐处手写保持一致（前缀/动作参数化）；映射经 Mapper.Compose 单点。</para>
/// </summary>
internal static class FreeSqlComposeSupport
{
    /// <summary>
    /// 降级判定 + 原因 Trace（Trace 文案保持："[FreeSql Compose{prefix}] {actionSuffix}降级: ..."）
    /// </summary>
    /// <param name="primarySource">Compose 主源实体类型</param>
    /// <param name="additionalSources">Compose 附加源实体类型集合</param>
    /// <param name="targetType">目标 DTO 类型</param>
    /// <param name="prefix">Trace 前缀（如 "2"、"3"）</param>
    /// <param name="actionSuffix">动作后缀（如 "ToPagedList"）</param>
    /// <returns>需要降级时为 true（并已输出 Trace 原因）</returns>
    [RequiresUnreferencedCode("NeedsDegradationWithTrace scans type members and attributes via reflection in the degradation detector. Some members may be trimmed.")]
    internal static bool NeedsDegradationWithTrace(Type primarySource, Type[] additionalSources, Type targetType, string prefix, string actionSuffix)
    {
        if (!FreeSqlDegradationDetector.NeedsDegradationCompose(primarySource, additionalSources, targetType, out var reason))
            return false;
        var srcList = string.Join(", ", new[] { primarySource.Name }.Concat(additionalSources.Select(s => s.Name)));
        if (MapTracer.GloballyEnabled) MapTracer.Write($"[FreeSql Compose{prefix}] {actionSuffix}降级: ({srcList}) → {targetType.Name}, 原因: {reason}");
        return true;
    }

    /// <summary>
    /// null 行过滤 + Compose 映射（双表版）— null 过滤 Trace 文案由调用方提供模板（{0} 为过滤行数）
    /// </summary>
    [RequiresDynamicCode("Compose mapping uses MakeGenericType/MakeGenericMethod via Mapper.Compose. Not supported in AOT. Register a source-generated compose delegate to make this AOT-safe.")]
    [RequiresUnreferencedCode("Compose mapping scans type members and attributes via reflection in Mapper.Compose. Some members may be trimmed. Register a source-generated compose delegate to make this AOT-safe.")]
    internal static List<TTarget> FilterAndMap2<T1, T2, TTarget>(List<Tuple<T1, T2>> tuples, string nullFilterTraceFormat)
        where T1 : class where T2 : class where TTarget : class
    {
        var validTuples = tuples.Where(t => t.Item1 != null && t.Item2 != null).ToList();
        if (validTuples.Count < tuples.Count)
            if (MapTracer.GloballyEnabled) MapTracer.Write(string.Format(nullFilterTraceFormat, tuples.Count - validTuples.Count));
        return validTuples.Select(t => Mapper.Compose<TTarget>(t.Item1, t.Item2)!).ToList();
    }

    /// <summary>
    /// null 行过滤 + Compose 映射（三表版）— null 过滤 Trace 文案由调用方提供模板（{0} 为过滤行数）
    /// </summary>
    [RequiresDynamicCode("Compose mapping uses MakeGenericType/MakeGenericMethod via Mapper.Compose. Not supported in AOT. Register a source-generated compose delegate to make this AOT-safe.")]
    [RequiresUnreferencedCode("Compose mapping scans type members and attributes via reflection in Mapper.Compose. Some members may be trimmed. Register a source-generated compose delegate to make this AOT-safe.")]
    internal static List<TTarget> FilterAndMap3<T1, T2, T3, TTarget>(List<Tuple<T1, T2, T3>> tuples, string nullFilterTraceFormat)
        where T1 : class where T2 : class where T3 : class where TTarget : class
    {
        var validTuples = tuples.Where(t => t.Item1 != null && t.Item2 != null && t.Item3 != null).ToList();
        if (validTuples.Count < tuples.Count)
            if (MapTracer.GloballyEnabled) MapTracer.Write(string.Format(nullFilterTraceFormat, tuples.Count - validTuples.Count));
        return validTuples.Select(t => Mapper.Compose<TTarget>(t.Item1, t.Item2, t.Item3)!).ToList();
    }

    /// <summary>
    /// null 行过滤（仅过滤，不映射；分页/分批场景用）— 过滤 Trace 文案由调用方提供模板
    /// </summary>
    internal static List<Tuple<T1, T2>> FilterValid2<T1, T2>(List<Tuple<T1, T2>> tuples, string nullFilterTraceFormat)
        where T1 : class where T2 : class
    {
        var validTuples = tuples.Where(t => t.Item1 != null && t.Item2 != null).ToList();
        if (validTuples.Count < tuples.Count)
            if (MapTracer.GloballyEnabled) MapTracer.Write(string.Format(nullFilterTraceFormat, tuples.Count - validTuples.Count));
        return validTuples;
    }

    /// <summary>
    /// null 行过滤（三表版）— 过滤 Trace 文案由调用方提供模板
    /// </summary>
    internal static List<Tuple<T1, T2, T3>> FilterValid3<T1, T2, T3>(List<Tuple<T1, T2, T3>> tuples, string nullFilterTraceFormat)
        where T1 : class where T2 : class where T3 : class
    {
        var validTuples = tuples.Where(t => t.Item1 != null && t.Item2 != null && t.Item3 != null).ToList();
        if (validTuples.Count < tuples.Count)
            if (MapTracer.GloballyEnabled) MapTracer.Write(string.Format(nullFilterTraceFormat, tuples.Count - validTuples.Count));
        return validTuples;
    }

    // ================================================================
    // Compose 终端编排内核 —— 2/3 表选择器终端方法的
    // "降级判定 → 自动 Include → 路径分发"编排样板与分页循环收敛单点；tuple 获取/过滤/映射
    // 的 2 表与 3 表异型差异经委托参数化，各路径体内的逻辑逐字保留（等价性证据 = 既有
    // Compose 测试全绿 + 逐对"旧入口 = 新入口"同构核对）。
    // ================================================================

    /// <summary>
    /// 同步终端编排内核 — 降级判定（含 Trace）→ 命中则自动 Include 并走降级委托，否则走投影委托。
    /// </summary>
    /// <param name="primarySource">Compose 主源实体类型</param>
    /// <param name="additionalSources">Compose 附加源实体类型集合</param>
    /// <param name="targetType">目标 DTO 类型</param>
    /// <param name="prefix">Trace 前缀（双表 ""，三表 "3"）</param>
    /// <param name="actionSuffix">动作后缀（如 "ToChunkDto "）</param>
    /// <param name="arity">ISelect 泛型参数个数描述（如 "&lt;T1,T2&gt;"）</param>
    /// <param name="degrade">降级路径委托（体内含 tuple 获取 + 过滤映射）</param>
    /// <param name="project">投影路径委托（体内含 BuildCompose + 终端调用）</param>
    [RequiresUnreferencedCode("ComposeTerminal calls NeedsDegradationWithTrace which scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static TResult ComposeTerminal<TResult>(
        Type primarySource, Type[] additionalSources, Type targetType,
        string prefix, string actionSuffix, string arity,
        Func<TResult> degrade, Func<TResult> project)
    {
        if (NeedsDegradationWithTrace(primarySource, additionalSources, targetType, prefix, actionSuffix))
        {
            ApplyFreeSqlComposeIncludes(primarySource, additionalSources, targetType, prefix, arity);
            return degrade();
        }
        return project();
    }

    /// <summary>
    /// 异步终端编排内核 — 语义同 <see cref="ComposeTerminal{TResult}"/>；Task 透传不加额外状态机。
    /// </summary>
    [RequiresUnreferencedCode("ComposeTerminalAsync calls NeedsDegradationWithTrace which scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Task<TResult> ComposeTerminalAsync<TResult>(
        Type primarySource, Type[] additionalSources, Type targetType,
        string prefix, string actionSuffix, string arity,
        Func<Task<TResult>> degrade, Func<Task<TResult>> project)
    {
        if (NeedsDegradationWithTrace(primarySource, additionalSources, targetType, prefix, actionSuffix))
        {
            ApplyFreeSqlComposeIncludes(primarySource, additionalSources, targetType, prefix, arity);
            return degrade();
        }
        return project();
    }

    /// <summary>
    /// ToChunkDto 降级路径分页循环内核 — Page 取元组 → null 过滤（含页码 Trace）→ Compose 映射回调。
    /// <para>H-01 修复终止语义单点：终止条件用过滤前 tuples.Count
    /// Right/Full Join 含 null 实体行被过滤后 validTuples.Count 可能 &lt; size 但本页原始行数仍满页
    /// （后面还有数据），原实现提前 break 导致后续页静默丢失；== 0 提前 break 同源一并改用过滤前计数。</para>
    /// </summary>
    /// <param name="fetchPage">按页号取原始元组页（调用方闭包 _select 构造 Tuple）</param>
    /// <param name="filterValid">null 行过滤（对应 FilterValid2/FilterValid3，第二实参为 Trace 模板）</param>
    /// <param name="mapValid">过滤后元组批量 Compose 映射</param>
    /// <param name="chunkTracePrefix">页过滤 Trace 前缀（"[FreeSql Compose]" / "[FreeSql Compose3]"）</param>
    /// <param name="size">每批条数</param>
    /// <param name="done">分批回调</param>
    internal static void ComposeChunkDegradeCore<TTuple, TTarget>(
        Func<int, List<TTuple>> fetchPage,
        Func<List<TTuple>, string, List<TTuple>> filterValid,
        Func<List<TTuple>, List<TTarget>> mapValid,
        string chunkTracePrefix,
        int size, Action<List<TTarget>> done)
        where TTarget : class
    {
        var page = 1;
        while (true)
        {
            var tuples = fetchPage(page);
            var validTuples = filterValid(tuples, chunkTracePrefix + " ToChunkDto 第 " + page + " 页过滤了 {0} 条含 null 实体的行");

            // 终止条件改用过滤前 tuples.Count（见方法注释）
            if (tuples.Count == 0) break;

            if (validTuples.Count > 0)
                done(mapValid(validTuples));

            if (tuples.Count < size) break;
            page++;
        }
    }

    /// <summary>
    /// ToChunkDto 投影路径分页循环内核 — Page + ToList(expr) 逐页投影回调，空页即止、不满页即末页。
    /// </summary>
    /// <param name="fetchPage">按页号取投影 DTO 页（调用方闭包 _select 与编译表达式）</param>
    /// <param name="size">每批条数</param>
    /// <param name="done">分批回调</param>
    internal static void ComposeChunkProjectCore<TTarget>(
        Func<int, List<TTarget>> fetchPage, int size, Action<List<TTarget>> done)
        where TTarget : class
    {
        var pageIdx = 1;
        while (true)
        {
            var dtos = fetchPage(pageIdx);
            if (dtos.Count == 0) break;
            done(dtos);
            if (dtos.Count < size) break;
            pageIdx++;
        }
    }

    /// <summary>
    /// ToChunkDtoAsync 降级路径分页循环内核 — 异步镜像 <see cref="ComposeChunkDegradeCore"/>：
    /// 逐页真异步取元组 → null 过滤（含页码 Trace）→ Compose 映射回调。
    /// <para>H-01 修复终止语义随同步内核保持：终止条件用过滤前 tuples.Count。</para>
    /// <para>取消传播（X4 红线）：每轮取数前 <c>ThrowIfCancellationRequested</c>，
    /// 并将取消令牌透传到底层 <c>ToListAsync</c>（镜像 EFCore ToChunkDtoAsync 逐轮取消检查形态）。</para>
    /// </summary>
    /// <param name="fetchPage">按页号异步取原始元组页（调用方闭包 _select 构造 Tuple）</param>
    /// <param name="filterValid">null 行过滤（对应 FilterValid2/FilterValid3，第二实参为 Trace 模板）</param>
    /// <param name="mapValid">过滤后元组批量 Compose 映射</param>
    /// <param name="chunkTracePrefix">页过滤 Trace 前缀（"[FreeSql Compose]" / "[FreeSql Compose3]"）</param>
    /// <param name="size">每批条数</param>
    /// <param name="done">异步分批回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal static async Task ComposeChunkDegradeCoreAsync<TTuple, TTarget>(
        Func<int, Task<List<TTuple>>> fetchPage,
        Func<List<TTuple>, string, List<TTuple>> filterValid,
        Func<List<TTuple>, List<TTarget>> mapValid,
        string chunkTracePrefix,
        int size, Func<List<TTarget>, Task> done,
        CancellationToken cancellationToken)
        where TTarget : class
    {
        var page = 1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tuples = await fetchPage(page);
            var validTuples = filterValid(tuples, chunkTracePrefix + " ToChunkDtoAsync 第 " + page + " 页过滤了 {0} 条含 null 实体的行");

            // 终止条件改用过滤前 tuples.Count（与同步内核一致）
            if (tuples.Count == 0) break;

            if (validTuples.Count > 0)
                await done(mapValid(validTuples));

            if (tuples.Count < size) break;
            page++;
        }
    }

    /// <summary>
    /// ToChunkDtoAsync 投影路径分页循环内核 — 异步镜像 <see cref="ComposeChunkProjectCore"/>：
    /// 逐页真异步取投影 DTO 页，空页即止、不满页即末页。
    /// <para>取消传播（X4 红线）：每轮取数前 <c>ThrowIfCancellationRequested</c>，
    /// 并将取消令牌透传到底层 <c>ToListAsync</c>。</para>
    /// </summary>
    /// <param name="fetchPage">按页号异步取投影 DTO 页（调用方闭包 _select 与编译表达式）</param>
    /// <param name="size">每批条数</param>
    /// <param name="done">异步分批回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal static async Task ComposeChunkProjectCoreAsync<TTarget>(
        Func<int, Task<List<TTarget>>> fetchPage, int size, Func<List<TTarget>, Task> done,
        CancellationToken cancellationToken)
        where TTarget : class
    {
        var pageIdx = 1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dtos = await fetchPage(pageIdx);
            if (dtos.Count == 0) break;
            await done(dtos);
            if (dtos.Count < size) break;
            pageIdx++;
        }
    }

    /// <summary>
    /// 降级路径自动 Include（Compose 多源版，P-02）— FreeSql ISelect 多表不支持 IncludeByPropertyName，
    /// 仅输出 Trace 提示用户手动 Include（风险 R1 缓解）。Trace 文案保持逐类前缀与 ISelect 泛型描述。
    /// </summary>
    /// <param name="primarySource">Compose 主源实体类型</param>
    /// <param name="additionalSources">Compose 附加源实体类型集合</param>
    /// <param name="targetType">目标 DTO 类型</param>
    /// <param name="tracePrefix">Trace 前缀</param>
    /// <param name="selectArity">ISelect 泛型参数个数描述（如 "2"）</param>
    [RequiresUnreferencedCode("ApplyFreeSqlComposeIncludes scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static void ApplyFreeSqlComposeIncludes(Type primarySource, Type[] additionalSources, Type targetType, string tracePrefix, string selectArity)
    {
        // 自动 Include 常开（原 AutoIncludeEnabled 恒真守卫删除）；属性级关闭走 [MapNoAutoInclude]
        var allSources = new[] { primarySource }.Concat(additionalSources).ToArray();
        var tree = FreeSqlNavigationCollector.Collect(allSources, targetType);
        if (tree.IsEmpty) return;

        var navPaths = tree.FlattenPaths();
        var targetProps = TypeHelper.GetPublicProperties(targetType);
        var dtoNavDeps = new List<string>();

        foreach (var prop in targetProps)
        {
            // T5 锚门化：所在类型无锚时 [MapProperty] 不消费
            // 收紧：准入改**特性锚**——Fluent 对不因属性级 [MapProperty] 产导航依赖
            var mpAttr = RuntimeAnchorGate.HasAttributeAnchor(prop.DeclaringType!)
                ? prop.GetCustomAttribute<MapPropertyAttribute>()
                : null;
            if (mpAttr == null) continue;

            // 与 FreeSqlNavigationCollector.Collect 判定一致：fieldSourceType != 主源表示跨表字段，
            // 需经主源导航属性解析，降级路径无法自动 Include 该导航，对应 DTO 字段将为 null
            PropertyMatcher.TryGetMapPropertyInfo(prop, primarySource,
                out _, out Type fieldSourceType, out _);
            if (fieldSourceType == primarySource) continue;

            // 检查 FromSource 是否为 Compose 源类型之一（附加源）
            if (!allSources.Contains(mpAttr.FromSource)) continue;

            // 检查主源到该源类型的导航是否在收集到的树中（确保依赖真实存在，而非配置错误）
            var sourceProps = TypeHelper.GetPublicProperties(primarySource);
            var navProp = NavigationResolver.ResolveNavigationProperty(
                sourceProps, fieldSourceType, primarySource.Name);
            if (navProp == null) continue;
            if (navPaths.Contains(navProp.Name) || navPaths.Any(p => p.StartsWith(navProp.Name + ".")))
            {
                dtoNavDeps.Add(
                    $"{targetType.Name}.{prop.Name} 依赖 {mpAttr.FromSource.Name}.{mpAttr.FromField}");
            }
        }

        if (dtoNavDeps.Count > 0)
        {
            if (MapTracer.GloballyEnabled) MapTracer.Write(
                $"[FreeSql Compose{tracePrefix}] Compose 降级路径不会自动 Include 导航属性。以下 DTO 字段依赖源实体的导航属性，"
                + $"在 FreeSql ISelect{selectArity} 多表查询中这些导航数据将为 null。"
                + "请在使用 AsDto 前手动 .Include() 相关导航，或确保 DTO 字段从 Compose 的另一源类型直接取值：");
            foreach (var dep in dtoNavDeps)
                if (MapTracer.GloballyEnabled) MapTracer.Write($"  → {dep}");
        }
        else
        {
            if (MapTracer.GloballyEnabled) MapTracer.Write($"[FreeSql Compose{tracePrefix}] 自动 Include 不支持多表 ISelect{selectArity}，请手动 Include 导航属性: {tree.Summary()}");
        }
    }
}

// ==== C-07 ① 拆分：IncludeReflectionState 探测支撑归位本文件 ====

/// <summary>
/// NCMapper FreeSql 查询扩展（Support partial 段）——反射探测状态字段归位 Support 关切文件。
/// </summary>
public static partial class NCMapperFreeSqlExtensions
{
    /// <summary>
    /// 反射探测 FreeSql 内部 Include 字段可用性状态（实态订正，：
    /// 实际二态——0=未探测 / -1=不可用；原注释三态中的"1=可用"无读取点，写 1 的死赋值已删除，
    /// 状态从不置 1，可用性由探测缓存命中隐式表达）
    /// <para>volatile 保证多线程可见性（读侧快速路径裸读 IncludeReflectionState；
    /// -1 侧走 Interlocked.CompareExchange——CAS 保证仅首次探测写一次、避免重复警告），
    /// 同步策略分工：volatile 提供可见性，Interlocked 提供 CAS 原子性（与 Mapper.cs s_metricsEnabled 同模式）。</para>
    /// </summary>
    internal static volatile int IncludeReflectionState;

    /// <summary>
    /// -1 短路路径一次性会话级告警标志
    /// 原实现首警仅出现在探测失败瞬间，此后所有调用经快速路径完全静默，
    /// 调用方对"自动 Include 已持续退化"零感知；此标志保证短路路径进程内仅补告警一条（不随调用刷屏）。
    /// </summary>
    internal static int s_shortCircuitWarned;

    /// <summary>
    /// 自动 Include 部分失败时重抛异常的调用方开关。
    /// <para>默认 false 保持既有降级契约（部分失败路径保留 + Trace 留痕）；置 true 后
    /// ApplyFreeSqlIncludes 的 catch 不再吞异常，失败对调用方显式可见（调试/强一致场景）。</para>
    /// </summary>
    public static bool ThrowOnIncludeApplicationFailure;
}
