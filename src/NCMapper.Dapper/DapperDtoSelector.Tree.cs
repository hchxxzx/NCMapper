using System.Data;
using System.Diagnostics.CodeAnalysis;
using NCMapper;
using NCMapper.Internal;

namespace NCMapper.Dapper;

// DapperDtoSelector.Tree.cs — 树构建关切（分家，partial 拆分）
// 内容：ToTreeList/ToTreeListAsync（平铺查询 → TreeBuilder 内存建树）
// T-2 上移：建树算法改调核心库 NCMapper.Internal.TreeBuilder（行为等价基线 = 现状自动查找语义；
// [MapTree] 契约解析归核心库 TreeBuilder.ResolveConfig —— 职责边界）
// static 字段与 pragma 块留守门面主文件 DapperDtoSelector.cs（B-1 字段留守定案）

public sealed partial class DapperDtoSelector<TSource> where TSource : class
{
    /// <summary>
    /// 查询扁平列表并构建为树形结构 — DTO 须有自引用集合属性（如 List&lt;TDto&gt; Children）
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="buffered">是否缓冲（默认 true）</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <returns>树形结构的 DTO 列表（仅根节点）</returns>
    [RequiresDynamicCode("ToTreeList uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("ToTreeList scans attributes via reflection.")]
    public List<TDto> ToTreeList<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null, bool buffered = true,
        int? commandTimeout = null, CommandType? commandType = null) where TDto : class
    {
        var entities = QuerySource(sql, param, transaction, buffered, commandTimeout, commandType);
        var flatList = MapListCached<TDto>(entities).Cast<TDto>().ToList();
        var roots = TreeBuilder.Build(flatList, TreeBuilder.ResolveConfig(typeof(TDto)));
        if (MapTracer.GloballyEnabled) MapTracer.Write($"{TracePrefix} {typeof(TSource).Name} → {typeof(TDto).Name} (树构建 平铺 {flatList.Count} 项 → 根 {roots.Count})");
        return roots;
    }

    /// <summary>
    /// 异步查询并构建树形结构
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>树形结构的 DTO 列表（仅根节点）</returns>
    [RequiresDynamicCode("ToTreeListAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("ToTreeListAsync scans attributes via reflection.")]
    public async Task<List<TDto>> ToTreeListAsync<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null, CancellationToken cancellationToken = default) where TDto : class
    {
        var entities = await QuerySourceAsync(sql, param, transaction, commandTimeout, commandType, cancellationToken).ConfigureAwait(false);
        var flatList = MapListCached<TDto>(entities).Cast<TDto>().ToList();
        var roots = TreeBuilder.Build(flatList, TreeBuilder.ResolveConfig(typeof(TDto)));
        if (MapTracer.GloballyEnabled) MapTracer.Write($"{TracePrefix} {typeof(TSource).Name} → {typeof(TDto).Name} (树构建 平铺 {flatList.Count} 项 → 根 {roots.Count})");
        return roots;
    }
}
