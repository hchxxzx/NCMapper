// NCMapper FreeSql 扩展包 — AsDto 终端操作 + 自动降级
// 用法：fsql.Select<OrderEntity>().Where(...).AsDto().ToList<OrderDto>()
// 依赖：NCMapper + FreeSql
#pragma warning disable NCMapper001 // Compose 为实验性 API，扩展包内部使用

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
/// NCMapper FreeSql 多表查询扩展 — 双表/三表 JOIN 后映射为 DTO
/// </summary>
public static class NCMapperFreeSqlComposeExtensions
{
    /// <summary>
    /// 双表 AsDto 入口
    /// </summary>
    /// <param name="select">FreeSql 双表查询对象</param>
    /// <returns>双表 DTO 选择器</returns>
    [RequiresDynamicCode("AsDto uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("AsDto scans attributes via reflection. Some types may be trimmed.")]
    public static FreeSqlComposeDtoSelector<T1, T2> AsDto<T1, T2>(this ISelect<T1, T2> select)
        where T1 : class
        where T2 : class
        => new(select);

    /// <summary>
    /// 三表 AsDto 入口
    /// </summary>
    /// <param name="select">FreeSql 三表查询对象</param>
    /// <returns>三表 DTO 选择器</returns>
    [RequiresDynamicCode("AsDto uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("AsDto scans attributes via reflection. Some types may be trimmed.")]
    public static FreeSqlComposeDtoSelector3<T1, T2, T3> AsDto<T1, T2, T3>(this ISelect<T1, T2, T3> select)
        where T1 : class
        where T2 : class
        where T3 : class
        => new(select);
}

/// <summary>
/// FreeSql 双表 DTO 选择器 — 支持正常投影和 Compose 降级
/// </summary>
/// <typeparam name="T1">Compose 映射的第一张源表实体类型</typeparam>
/// <typeparam name="T2">Compose 映射的第二张源表实体类型</typeparam>
public sealed class FreeSqlComposeDtoSelector<T1, T2>
    where T1 : class
    where T2 : class
{
    /// <summary>
    /// FreeSql 双表查询对象
    /// </summary>
    private readonly ISelect<T1, T2> _select;

    /// <summary>
    /// FreeSQL组合DTO选择器。
    /// </summary>
    /// <param name="select">指定选择。</param>
    internal FreeSqlComposeDtoSelector(ISelect<T1, T2> select) => _select = select;


    /// <summary>
    /// 查询并映射为 DTO 列表
    /// <para>正常路径：多参数表达式投影；降级路径：查实体对 → Compose</para>
    /// </summary>
    /// <returns>DTO 列表</returns>
    [RequiresDynamicCode("ToList uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToList scans attributes via reflection. Some types may be trimmed.")]
    public List<TTarget> ToList<TTarget>() where TTarget : class
        // 降级判定 + 自动 Include + 路径分发编排收敛 ComposeTerminal 单点（体内逻辑逐字保留）
        => FreeSqlComposeSupport.ComposeTerminal<List<TTarget>>(
            typeof(T1), new[] { typeof(T2) }, typeof(TTarget), "", "", "<T1,T2>",
            degrade: () =>
            {
                var tuples = _select.ToList((a, b) => new Tuple<T1, T2>(a, b));
                return FreeSqlComposeSupport.FilterAndMap2<T1, T2, TTarget>(tuples, "[FreeSql Compose] 降级路径过滤了 {0} 条含 null 实体的行（如 Right/Full Join 产生），被过滤的行将不会出现在结果中");
            },
            project: () =>
            {
                var expr = FreeSqlProjectionBuilder.BuildCompose<T1, T2, TTarget>();
                return _select.ToList(expr);
            });

    /// <summary>
    /// 异步查询并映射为 DTO 列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>DTO 列表</returns>
    [RequiresDynamicCode("ToListAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToListAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<List<TTarget>> ToListAsync<TTarget>(CancellationToken cancellationToken = default)
        where TTarget : class
        // 编排收敛 ComposeTerminalAsync 单点（体内逻辑逐字保留）
        => await FreeSqlComposeSupport.ComposeTerminalAsync<List<TTarget>>(
            typeof(T1), new[] { typeof(T2) }, typeof(TTarget), "", "", "<T1,T2>",
            degrade: async () =>
            {
                var tuples = await _select.ToListAsync((a, b) => new Tuple<T1, T2>(a, b), cancellationToken);
                return FreeSqlComposeSupport.FilterAndMap2<T1, T2, TTarget>(tuples, "[FreeSql Compose] 降级路径过滤了 {0} 条含 null 实体的行（如 Right/Full Join 产生），被过滤的行将不会出现在结果中");
            },
            project: async () =>
            {
                var expr = FreeSqlProjectionBuilder.BuildCompose<T1, T2, TTarget>();
                return await _select.ToListAsync(expr, cancellationToken);
            });

    /// <summary>
    /// 查询第一条记录并映射为 DTO
    /// </summary>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("First uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("First scans attributes via reflection. Some types may be trimmed.")]
    public TTarget? First<TTarget>() where TTarget : class
        // 编排收敛 ComposeTerminal 单点（体内逻辑逐字保留）
        => FreeSqlComposeSupport.ComposeTerminal<TTarget?>(
            typeof(T1), new[] { typeof(T2) }, typeof(TTarget), "", "", "<T1,T2>",
            degrade: () =>
            {
                var tuple = _select.First((a, b) => new Tuple<T1, T2>(a, b));
                if (tuple is null) return null;
                if (tuple.Item1 is null || tuple.Item2 is null)
                {
                    if (MapTracer.GloballyEnabled) MapTracer.Write(
                        $"[FreeSql Compose] 降级路径 First 遇到含 null 实体的行（如 Right/Full Join 产生），返回 null");
                    return null;
                }
                return Mapper.Compose<TTarget>(tuple.Item1, tuple.Item2);
            },
            project: () =>
            {
                var expr = FreeSqlProjectionBuilder.BuildCompose<T1, T2, TTarget>();
                return _select.First(expr);
            });

    /// <summary>
    /// 异步查询第一条记录并映射为 DTO
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("FirstAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("FirstAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<TTarget?> FirstAsync<TTarget>(CancellationToken cancellationToken = default)
        where TTarget : class
        // 编排收敛 ComposeTerminalAsync 单点（体内逻辑逐字保留）
        => await FreeSqlComposeSupport.ComposeTerminalAsync<TTarget?>(
            typeof(T1), new[] { typeof(T2) }, typeof(TTarget), "", "", "<T1,T2>",
            degrade: async () =>
            {
                var tuple = await _select.FirstAsync((a, b) => new Tuple<T1, T2>(a, b), cancellationToken);
                if (tuple is null) return null;
                if (tuple.Item1 is null || tuple.Item2 is null)
                {
                    if (MapTracer.GloballyEnabled) MapTracer.Write(
                        $"[FreeSql Compose] 降级路径 First 遇到含 null 实体的行（如 Right/Full Join 产生），返回 null");
                    return null;
                }
                return Mapper.Compose<TTarget>(tuple.Item1, tuple.Item2);
            },
            project: async () =>
            {
                var expr = FreeSqlProjectionBuilder.BuildCompose<T1, T2, TTarget>();
                return await _select.FirstAsync(expr, cancellationToken);
            });

    /// <summary>
    /// 分页查询并映射为 DTO 列表（Count + 分页均在 SQL 侧）
    /// </summary>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页条数</param>
    /// <returns>分页结果，包含当前页数据和分页信息</returns>
    /// <remarks>
    /// TotalCount 语义：降级路径下 <c>totalCount = _select.Count()</c> 在 null 行过滤之前计算，
    /// 统计的是 JOIN 总行数；当 Right/Full Join 产生含 null 实体的行被过滤后，
    /// TotalCount 可能大于各页实际返回的 DTO 行数之和。投影路径不做 null 过滤，TotalCount 与返回行数一致。
    /// </remarks>
    [RequiresDynamicCode("ToPagedList uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToPagedList scans attributes via reflection. Some types may be trimmed.")]
    public PagedList<TTarget> ToPagedList<TTarget>(int page, int pageSize) where TTarget : class
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), page, ErrorMessages.PageNumberMustBePositive());
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, ErrorMessages.PageSizeMustBePositive());

        var totalCount = _select.Count();

        // 编排收敛 ComposeTerminal 单点（体内逻辑逐字保留）
        var items = FreeSqlComposeSupport.ComposeTerminal<List<TTarget>>(
            typeof(T1), new[] { typeof(T2) }, typeof(TTarget), "", "", "<T1,T2>",
            degrade: () =>
            {
                var tuples = _select.Page(page, pageSize).ToList((a, b) => new Tuple<T1, T2>(a, b));
                return FreeSqlComposeSupport.FilterAndMap2<T1, T2, TTarget>(tuples, "[FreeSql Compose] 降级路径过滤了 {0} 条含 null 实体的行（如 Right/Full Join 产生）");
            },
            project: () =>
            {
                var expr = FreeSqlProjectionBuilder.BuildCompose<T1, T2, TTarget>();
                return _select.Page(page, pageSize).ToList(expr);
            });

        return new PagedList<TTarget> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// 异步分页查询并映射为 DTO 列表
    /// </summary>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分页结果，包含当前页数据和分页信息</returns>
    /// <remarks>
    /// TotalCount 语义：降级路径下 <c>totalCount = await _select.CountAsync()</c> 在 null 行过滤之前计算，
    /// 统计的是 JOIN 总行数；当 Right/Full Join 产生含 null 实体的行被过滤后，
    /// TotalCount 可能大于各页实际返回的 DTO 行数之和。投影路径不做 null 过滤，TotalCount 与返回行数一致。
    /// </remarks>
    [RequiresDynamicCode("ToPagedListAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToPagedListAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<PagedList<TTarget>> ToPagedListAsync<TTarget>(
        int page, int pageSize, CancellationToken cancellationToken = default) where TTarget : class
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), page, ErrorMessages.PageNumberMustBePositive());
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, ErrorMessages.PageSizeMustBePositive());

        var totalCount = await _select.CountAsync(cancellationToken);

        // 编排收敛 ComposeTerminalAsync 单点（体内逻辑逐字保留）
        var items = await FreeSqlComposeSupport.ComposeTerminalAsync<List<TTarget>>(
            typeof(T1), new[] { typeof(T2) }, typeof(TTarget), "", "", "<T1,T2>",
            degrade: async () =>
            {
                var tuples = await _select.Page(page, pageSize).ToListAsync((a, b) => new Tuple<T1, T2>(a, b), cancellationToken);
                return FreeSqlComposeSupport.FilterAndMap2<T1, T2, TTarget>(tuples, "[FreeSql Compose] 降级路径过滤了 {0} 条含 null 实体的行（如 Right/Full Join 产生）");
            },
            project: async () =>
            {
                var expr = FreeSqlProjectionBuilder.BuildCompose<T1, T2, TTarget>();
                return await _select.Page(page, pageSize).ToListAsync(expr, cancellationToken);
            });

        return new PagedList<TTarget> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// 分块查询并映射为 DTO — 使用 Page+ToList 分轮方式实现完整 Compose 映射
    /// <para>将原始 <c>_select.ToChunk</c>（chunk.Object 仅为 List&lt;T1&gt;，无法获取 T2/T3）替换为
    /// <c>while(true) { Page(page,size).ToList((a,b)=>Tuple) → null 过滤 → Compose }</c> 循环，
    /// 确保 T1、T2 字段均正确填充</para>
    /// </summary>
    /// <param name="size">每批条数</param>
    /// <param name="done">分批回调，参数为已 Compose 映射的 DTO 列表</param>
    /// <remarks>
    /// 每批执行一次 SQL（Page + ToList），等价于循环调用 ToPagedList 的 items 部分。
    /// <para>空结果集（0 条）时不会触发 done 回调（与单表 1 次回调不同）。</para>
    /// </remarks>
    [RequiresDynamicCode("ToChunkDto uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToChunkDto scans attributes via reflection. Some types may be trimmed.")]
    public void ToChunkDto<TTarget>(int size, Action<List<TTarget>> done) where TTarget : class
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), size, ErrorMessages.BatchSizeMustBePositive()); // T-F ② 契约统一

        // T-F ④ 降级判定（S1 设计缺口）：可投影走 SQL 分页；降级走现状内存分批合并路径
        // 分页循环收敛 ComposeChunkDegradeCore/ComposeChunkProjectCore 单点（H-01 终止语义随内核单点）
        if (FreeSqlComposeSupport.NeedsDegradationWithTrace(typeof(T1), new[] { typeof(T2) }, typeof(TTarget), "", "ToChunkDto "))
        {
            FreeSqlComposeSupport.ApplyFreeSqlComposeIncludes(typeof(T1), new[] { typeof(T2) }, typeof(TTarget), "", "<T1,T2>");

            FreeSqlComposeSupport.ComposeChunkDegradeCore<Tuple<T1, T2>, TTarget>(
                p => _select.Page(p, size).ToList((a, b) => new Tuple<T1, T2>(a, b)),
                (tuples, fmt) => FreeSqlComposeSupport.FilterValid2(tuples, fmt),
                validTuples => validTuples.Select(t => Mapper.Compose<TTarget>(t.Item1, t.Item2)!).ToList(),
                "[FreeSql Compose]", size, done);
            return;
        }

        var expr = FreeSqlProjectionBuilder.BuildCompose<T1, T2, TTarget>();
        FreeSqlComposeSupport.ComposeChunkProjectCore<TTarget>(
            p => _select.Page(p, size).ToList(expr), size, done);
    }

    /// <summary>
    /// 异步分块查询并映射为 DTO — 逐页真异步取数（Page + ToListAsync），按 size 分批回调
    /// <para>语义同同步版 <see cref="ToChunkDto"/>：可投影走 SQL 分页 + 编译表达式逐页投影；
    /// 降级走逐页取元组 → null 过滤 → Compose 内存映射（H-01 终止语义随异步内核单点保持）。</para>
    /// <para>分页依赖稳定排序：请在查询上自备 OrderBy，否则大结果集跨页取数可能静默漏行/重行
    /// （与 EFCore 版/单表异步版口径一致）。</para>
    /// </summary>
    /// <param name="size">每批条数</param>
    /// <param name="done">异步分批回调，参数为已 Compose 映射的 DTO 列表</param>
    /// <param name="cancellationToken">取消令牌 — 每轮取数前检查并透传到底层 <c>ToListAsync</c></param>
    /// <returns>表示异步分块操作的任务</returns>
    /// <remarks>
    /// <para>空结果集（0 条）时不会触发 done 回调（与单表 1 次回调不同）。</para>
    /// <para>每批执行一次 SQL（Page + ToListAsync），逐页物化、每页仅 size 条。</para>
    /// </remarks>
    [RequiresDynamicCode("ToChunkDtoAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToChunkDtoAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task ToChunkDtoAsync<TTarget>(int size, Func<List<TTarget>, Task> done, CancellationToken cancellationToken = default)
        where TTarget : class
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), size, ErrorMessages.BatchSizeMustBePositive()); // T-F ② 契约统一

        // T-F ④ 降级判定（S1 设计缺口）+ 异步镜像同步版分流，分页循环收敛
        // ComposeChunkDegradeCoreAsync/ComposeChunkProjectCoreAsync 单点（H-01 终止语义随内核单点）
        if (FreeSqlComposeSupport.NeedsDegradationWithTrace(typeof(T1), new[] { typeof(T2) }, typeof(TTarget), "", "ToChunkDtoAsync "))
        {
            FreeSqlComposeSupport.ApplyFreeSqlComposeIncludes(typeof(T1), new[] { typeof(T2) }, typeof(TTarget), "", "<T1,T2>");

            await FreeSqlComposeSupport.ComposeChunkDegradeCoreAsync<Tuple<T1, T2>, TTarget>(
                p => _select.Page(p, size).ToListAsync((a, b) => new Tuple<T1, T2>(a, b), cancellationToken),
                (tuples, fmt) => FreeSqlComposeSupport.FilterValid2(tuples, fmt),
                validTuples => validTuples.Select(t => Mapper.Compose<TTarget>(t.Item1, t.Item2)!).ToList(),
                "[FreeSql Compose]", size, done, cancellationToken);
            return;
        }

        var expr = FreeSqlProjectionBuilder.BuildCompose<T1, T2, TTarget>();
        await FreeSqlComposeSupport.ComposeChunkProjectCoreAsync<TTarget>(
            p => _select.Page(p, size).ToListAsync(expr, cancellationToken), size, done, cancellationToken);
    }
}

/// <summary>
/// FreeSql 三表 DTO 选择器 — 支持正常投影和 Compose 降级
/// </summary>
/// <typeparam name="T1">Compose 映射的第一张源表实体类型</typeparam>
/// <typeparam name="T2">Compose 映射的第二张源表实体类型</typeparam>
/// <typeparam name="T3">Compose 映射的第三张源表实体类型</typeparam>
public sealed class FreeSqlComposeDtoSelector3<T1, T2, T3>
    where T1 : class
    where T2 : class
    where T3 : class
{
    /// <summary>
    /// FreeSql 三表查询对象
    /// </summary>
    private readonly ISelect<T1, T2, T3> _select;

    /// <summary>
    /// FreeSQL组合DTO选择器3。
    /// </summary>
    /// <param name="select">指定选择。</param>
    internal FreeSqlComposeDtoSelector3(ISelect<T1, T2, T3> select) => _select = select;


    /// <summary>
    /// 查询并映射为 DTO 列表
    /// </summary>
    /// <returns>DTO 列表</returns>
    [RequiresDynamicCode("ToList uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToList scans attributes via reflection. Some types may be trimmed.")]
    public List<TTarget> ToList<TTarget>() where TTarget : class
        // 降级判定 + 自动 Include + 路径分发编排收敛 ComposeTerminal 单点（体内逻辑逐字保留）
        => FreeSqlComposeSupport.ComposeTerminal<List<TTarget>>(
            typeof(T1), new[] { typeof(T2), typeof(T3) }, typeof(TTarget), "3", "", "<T1,T2,T3>",
            degrade: () =>
            {
                var tuples = _select.ToList((a, b, c) => new Tuple<T1, T2, T3>(a, b, c));
                return FreeSqlComposeSupport.FilterAndMap3<T1, T2, T3, TTarget>(tuples, "[FreeSql Compose3] 降级路径过滤了 {0} 条含 null 实体的行（如 Right/Full Join 产生），被过滤的行将不会出现在结果中");
            },
            project: () =>
            {
                var expr = FreeSqlProjectionBuilder.BuildCompose3<T1, T2, T3, TTarget>();
                return _select.ToList(expr);
            });

    /// <summary>
    /// 异步查询并映射为 DTO 列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>DTO 列表</returns>
    [RequiresDynamicCode("ToListAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToListAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<List<TTarget>> ToListAsync<TTarget>(CancellationToken cancellationToken = default)
        where TTarget : class
        // 编排收敛 ComposeTerminalAsync 单点（体内逻辑逐字保留）
        => await FreeSqlComposeSupport.ComposeTerminalAsync<List<TTarget>>(
            typeof(T1), new[] { typeof(T2), typeof(T3) }, typeof(TTarget), "3", "", "<T1,T2,T3>",
            degrade: async () =>
            {
                var tuples = await _select.ToListAsync((a, b, c) => new Tuple<T1, T2, T3>(a, b, c), cancellationToken);
                return FreeSqlComposeSupport.FilterAndMap3<T1, T2, T3, TTarget>(tuples, "[FreeSql Compose3] 降级路径过滤了 {0} 条含 null 实体的行（如 Right/Full Join 产生），被过滤的行将不会出现在结果中");
            },
            project: async () =>
            {
                var expr = FreeSqlProjectionBuilder.BuildCompose3<T1, T2, T3, TTarget>();
                return await _select.ToListAsync(expr, cancellationToken);
            });

    /// <summary>
    /// 查询第一条记录并映射为 DTO
    /// </summary>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("First uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("First scans attributes via reflection. Some types may be trimmed.")]
    public TTarget? First<TTarget>() where TTarget : class
        // 编排收敛 ComposeTerminal 单点（体内逻辑逐字保留）
        => FreeSqlComposeSupport.ComposeTerminal<TTarget?>(
            typeof(T1), new[] { typeof(T2), typeof(T3) }, typeof(TTarget), "3", "", "<T1,T2,T3>",
            degrade: () =>
            {
                var tuple = _select.First((a, b, c) => new Tuple<T1, T2, T3>(a, b, c));
                if (tuple is null) return null;
                if (tuple.Item1 is null || tuple.Item2 is null || tuple.Item3 is null)
                {
                    if (MapTracer.GloballyEnabled) MapTracer.Write(
                        $"[FreeSql Compose3] 降级路径 First 遇到含 null 实体的行（如 Right/Full Join 产生），返回 null");
                    return null;
                }
                return Mapper.Compose<TTarget>(tuple.Item1, tuple.Item2, tuple.Item3);
            },
            project: () =>
            {
                var expr = FreeSqlProjectionBuilder.BuildCompose3<T1, T2, T3, TTarget>();
                return _select.First(expr);
            });

    /// <summary>
    /// 异步查询第一条记录并映射为 DTO
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("FirstAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("FirstAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<TTarget?> FirstAsync<TTarget>(CancellationToken cancellationToken = default)
        where TTarget : class
        // 编排收敛 ComposeTerminalAsync 单点（体内逻辑逐字保留）
        => await FreeSqlComposeSupport.ComposeTerminalAsync<TTarget?>(
            typeof(T1), new[] { typeof(T2), typeof(T3) }, typeof(TTarget), "3", "", "<T1,T2,T3>",
            degrade: async () =>
            {
                var tuple = await _select.FirstAsync((a, b, c) => new Tuple<T1, T2, T3>(a, b, c), cancellationToken);
                if (tuple is null) return null;
                if (tuple.Item1 is null || tuple.Item2 is null || tuple.Item3 is null)
                {
                    if (MapTracer.GloballyEnabled) MapTracer.Write(
                        $"[FreeSql Compose3] 降级路径 First 遇到含 null 实体的行（如 Right/Full Join 产生），返回 null");
                    return null;
                }
                return Mapper.Compose<TTarget>(tuple.Item1, tuple.Item2, tuple.Item3);
            },
            project: async () =>
            {
                var expr = FreeSqlProjectionBuilder.BuildCompose3<T1, T2, T3, TTarget>();
                return await _select.FirstAsync(expr, cancellationToken);
            });

    /// <summary>
    /// 分页查询并映射为 DTO 列表（Count + 分页均在 SQL 侧）
    /// </summary>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页条数</param>
    /// <returns>分页结果，包含当前页数据和分页信息</returns>
    /// <remarks>
    /// TotalCount 语义：降级路径下 <c>totalCount = _select.Count()</c> 在 null 行过滤之前计算，
    /// 统计的是 JOIN 总行数；当 Right/Full Join 产生含 null 实体的行被过滤后，
    /// TotalCount 可能大于各页实际返回的 DTO 行数之和。投影路径不做 null 过滤，TotalCount 与返回行数一致。
    /// </remarks>
    [RequiresDynamicCode("ToPagedList uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToPagedList scans attributes via reflection. Some types may be trimmed.")]
    public PagedList<TTarget> ToPagedList<TTarget>(int page, int pageSize) where TTarget : class
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), page, ErrorMessages.PageNumberMustBePositive());
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, ErrorMessages.PageSizeMustBePositive());

        var totalCount = _select.Count();

        // 编排收敛 ComposeTerminal 单点（体内逻辑逐字保留）
        var items = FreeSqlComposeSupport.ComposeTerminal<List<TTarget>>(
            typeof(T1), new[] { typeof(T2), typeof(T3) }, typeof(TTarget), "3", "", "<T1,T2,T3>",
            degrade: () =>
            {
                var tuples = _select.Page(page, pageSize).ToList((a, b, c) => new Tuple<T1, T2, T3>(a, b, c));
                return FreeSqlComposeSupport.FilterAndMap3<T1, T2, T3, TTarget>(tuples, "[FreeSql Compose3] 降级路径过滤了 {0} 条含 null 实体的行（如 Right/Full Join 产生）");
            },
            project: () =>
            {
                var expr = FreeSqlProjectionBuilder.BuildCompose3<T1, T2, T3, TTarget>();
                return _select.Page(page, pageSize).ToList(expr);
            });

        return new PagedList<TTarget> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// 异步分页查询并映射为 DTO 列表
    /// </summary>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分页结果，包含当前页数据和分页信息</returns>
    /// <remarks>
    /// TotalCount 语义：降级路径下 <c>totalCount = await _select.CountAsync()</c> 在 null 行过滤之前计算，
    /// 统计的是 JOIN 总行数；当 Right/Full Join 产生含 null 实体的行被过滤后，
    /// TotalCount 可能大于各页实际返回的 DTO 行数之和。投影路径不做 null 过滤，TotalCount 与返回行数一致。
    /// </remarks>
    [RequiresDynamicCode("ToPagedListAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToPagedListAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<PagedList<TTarget>> ToPagedListAsync<TTarget>(
        int page, int pageSize, CancellationToken cancellationToken = default) where TTarget : class
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), page, ErrorMessages.PageNumberMustBePositive());
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, ErrorMessages.PageSizeMustBePositive());

        var totalCount = await _select.CountAsync(cancellationToken);

        // 编排收敛 ComposeTerminalAsync 单点（体内逻辑逐字保留）
        var items = await FreeSqlComposeSupport.ComposeTerminalAsync<List<TTarget>>(
            typeof(T1), new[] { typeof(T2), typeof(T3) }, typeof(TTarget), "3", "", "<T1,T2,T3>",
            degrade: async () =>
            {
                var tuples = await _select.Page(page, pageSize).ToListAsync((a, b, c) => new Tuple<T1, T2, T3>(a, b, c), cancellationToken);
                return FreeSqlComposeSupport.FilterAndMap3<T1, T2, T3, TTarget>(tuples, "[FreeSql Compose3] 降级路径过滤了 {0} 条含 null 实体的行（如 Right/Full Join 产生）");
            },
            project: async () =>
            {
                var expr = FreeSqlProjectionBuilder.BuildCompose3<T1, T2, T3, TTarget>();
                return await _select.Page(page, pageSize).ToListAsync(expr, cancellationToken);
            });

        return new PagedList<TTarget> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// 分块查询并映射为 DTO — 使用 Page+ToList 分轮方式实现完整 Compose 映射
    /// <para>将原始 <c>_select.ToChunk</c>（chunk.Object 仅为 List&lt;T1&gt;，无法获取 T2/T3）替换为
    /// <c>while(true) { Page(page,size).ToList((a,b,c)=>Tuple) → null 过滤 → Compose }</c> 循环，
    /// 确保 T1、T2、T3 字段均正确填充</para>
    /// </summary>
    /// <param name="size">每批条数</param>
    /// <param name="done">分批回调，参数为已 Compose 映射的 DTO 列表</param>
    /// <remarks>
    /// 每批执行一次 SQL（Page + ToList），等价于循环调用 ToPagedList 的 items 部分。
    /// <para>空结果集（0 条）时不会触发 done 回调（与单表 1 次回调不同）。</para>
    /// </remarks>
    [RequiresDynamicCode("ToChunkDto uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToChunkDto scans attributes via reflection. Some types may be trimmed.")]
    public void ToChunkDto<TTarget>(int size, Action<List<TTarget>> done) where TTarget : class
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), size, ErrorMessages.BatchSizeMustBePositive()); // T-F ② 契约统一

        // T-F ④ 降级判定（S1 设计缺口）：可投影走 SQL 分页；降级走现状内存分批合并路径
        // 分页循环收敛 ComposeChunkDegradeCore/ComposeChunkProjectCore 单点（H-01 终止语义随内核单点）
        if (FreeSqlComposeSupport.NeedsDegradationWithTrace(typeof(T1), new[] { typeof(T2), typeof(T3) }, typeof(TTarget), "3", "ToChunkDto "))
        {
            FreeSqlComposeSupport.ApplyFreeSqlComposeIncludes(typeof(T1), new[] { typeof(T2), typeof(T3) }, typeof(TTarget), "3", "<T1,T2,T3>");

            FreeSqlComposeSupport.ComposeChunkDegradeCore<Tuple<T1, T2, T3>, TTarget>(
                p => _select.Page(p, size).ToList((a, b, c) => new Tuple<T1, T2, T3>(a, b, c)),
                (tuples, fmt) => FreeSqlComposeSupport.FilterValid3(tuples, fmt),
                validTuples => validTuples.Select(t => Mapper.Compose<TTarget>(t.Item1, t.Item2, t.Item3)!).ToList(),
                "[FreeSql Compose3]", size, done);
            return;
        }

        var expr = FreeSqlProjectionBuilder.BuildCompose3<T1, T2, T3, TTarget>();
        FreeSqlComposeSupport.ComposeChunkProjectCore<TTarget>(
            p => _select.Page(p, size).ToList(expr), size, done);
    }

    /// <summary>
    /// 异步分块查询并映射为 DTO — 逐页真异步取数（Page + ToListAsync），按 size 分批回调
    /// <para>语义同同步版 <see cref="ToChunkDto"/>：可投影走 SQL 分页 + 编译表达式逐页投影；
    /// 降级走逐页取元组 → null 过滤 → Compose 内存映射（H-01 终止语义随异步内核单点保持）。</para>
    /// <para>分页依赖稳定排序：请在查询上自备 OrderBy，否则大结果集跨页取数可能静默漏行/重行
    /// （与 EFCore 版/单表异步版口径一致）。</para>
    /// </summary>
    /// <param name="size">每批条数</param>
    /// <param name="done">异步分批回调，参数为已 Compose 映射的 DTO 列表</param>
    /// <param name="cancellationToken">取消令牌 — 每轮取数前检查并透传到底层 <c>ToListAsync</c></param>
    /// <returns>表示异步分块操作的任务</returns>
    /// <remarks>
    /// <para>空结果集（0 条）时不会触发 done 回调（与单表 1 次回调不同）。</para>
    /// <para>每批执行一次 SQL（Page + ToListAsync），逐页物化、每页仅 size 条。</para>
    /// </remarks>
    [RequiresDynamicCode("ToChunkDtoAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToChunkDtoAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task ToChunkDtoAsync<TTarget>(int size, Func<List<TTarget>, Task> done, CancellationToken cancellationToken = default)
        where TTarget : class
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), size, ErrorMessages.BatchSizeMustBePositive()); // T-F ② 契约统一

        // T-F ④ 降级判定（S1 设计缺口）+ 异步镜像同步版分流，分页循环收敛
        // ComposeChunkDegradeCoreAsync/ComposeChunkProjectCoreAsync 单点（H-01 终止语义随内核单点）
        if (FreeSqlComposeSupport.NeedsDegradationWithTrace(typeof(T1), new[] { typeof(T2), typeof(T3) }, typeof(TTarget), "3", "ToChunkDtoAsync "))
        {
            FreeSqlComposeSupport.ApplyFreeSqlComposeIncludes(typeof(T1), new[] { typeof(T2), typeof(T3) }, typeof(TTarget), "3", "<T1,T2,T3>");

            await FreeSqlComposeSupport.ComposeChunkDegradeCoreAsync<Tuple<T1, T2, T3>, TTarget>(
                p => _select.Page(p, size).ToListAsync((a, b, c) => new Tuple<T1, T2, T3>(a, b, c), cancellationToken),
                (tuples, fmt) => FreeSqlComposeSupport.FilterValid3(tuples, fmt),
                validTuples => validTuples.Select(t => Mapper.Compose<TTarget>(t.Item1, t.Item2, t.Item3)!).ToList(),
                "[FreeSql Compose3]", size, done, cancellationToken);
            return;
        }

        var expr = FreeSqlProjectionBuilder.BuildCompose3<T1, T2, T3, TTarget>();
        await FreeSqlComposeSupport.ComposeChunkProjectCoreAsync<TTarget>(
            p => _select.Page(p, size).ToListAsync(expr, cancellationToken), size, done, cancellationToken);
    }
}
