using System.Data;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using NCMapper;

namespace NCMapper.Dapper;

// DapperDtoSelector.Paging.cs — 分页/分块关切（分家，partial 拆分）
// 内容：ToPagedList/ToPagedListAsync（方言分页）/ ToChunkDto/ToChunkDtoAsync（流式分块）
// static 字段与 pragma 块留守门面主文件 DapperDtoSelector.cs（B-1 字段留守定案）

public sealed partial class DapperDtoSelector<TSource> where TSource : class
{
    /// <summary>
    /// SQL 级分页查询 — 走 ISqlDialect 方言生成 COUNT + 分页 SQL，仅传当页数据
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <returns>分页结果，包含当前页数据和分页信息</returns>
    [RequiresDynamicCode("ToPagedList uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("ToPagedList scans attributes via reflection.")]
    public PagedList<TDto> ToPagedList<TDto>(string sql, object? param, int page, int pageSize,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null) where TDto : class
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), page, ErrorMessages.PageNumberMustBePositive());
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, ErrorMessages.PageSizeMustBePositive());

        var countSql = _dialect.CountSql(sql);
        var totalCount = _connection.ExecuteScalar<long>(countSql, param, transaction, commandTimeout, commandType);

        var mergedParam = new DynamicParameters(param);
        mergedParam.Add(OffsetParamName, (page - 1) * pageSize);
        mergedParam.Add(PageSizeParamName, pageSize);

        var pageSql = _dialect.PageSql(sql, _dialect.ParameterPrefix + OffsetParamName, _dialect.ParameterPrefix + PageSizeParamName); // 占位符前缀由方言提供（Oracle/达梦为 :）
        var entities = QuerySource(pageSql, mergedParam, transaction, true, commandTimeout, commandType);
        var items = MapListCached<TDto>(entities).Cast<TDto>().ToList();

        if (MapTracer.GloballyEnabled) MapTracer.Write($"{TracePrefix} {typeof(TSource).Name} → {typeof(TDto).Name} (分页 page={page}, size={pageSize}, total={totalCount})");
        return new PagedList<TDto> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// 异步 SQL 级分页查询
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分页结果，包含当前页数据和分页信息</returns>
    [RequiresDynamicCode("ToPagedListAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("ToPagedListAsync scans attributes via reflection.")]
    public async Task<PagedList<TDto>> ToPagedListAsync<TDto>(string sql, object? param, int page, int pageSize,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null, CancellationToken cancellationToken = default) where TDto : class
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), page, ErrorMessages.PageNumberMustBePositive());
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, ErrorMessages.PageSizeMustBePositive());

        var countSql = _dialect.CountSql(sql);
        var totalCount = await _connection.ExecuteScalarAsync<long>(
            new CommandDefinition(countSql, param, transaction, commandTimeout, commandType, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var mergedParam = new DynamicParameters(param);
        mergedParam.Add(OffsetParamName, (page - 1) * pageSize);
        mergedParam.Add(PageSizeParamName, pageSize);

        var pageSql = _dialect.PageSql(sql, _dialect.ParameterPrefix + OffsetParamName, _dialect.ParameterPrefix + PageSizeParamName); // 占位符前缀由方言提供（Oracle/达梦为 :）
        var entities = await QuerySourceAsync(pageSql, mergedParam, transaction, commandTimeout, commandType, cancellationToken).ConfigureAwait(false);
        var items = MapListCached<TDto>(entities).Cast<TDto>().ToList();

        if (MapTracer.GloballyEnabled) MapTracer.Write($"{TracePrefix} {typeof(TSource).Name} → {typeof(TDto).Name} (分页 page={page}, size={pageSize}, total={totalCount})");
        return new PagedList<TDto> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// 流式分块查询 — buffered:false 逐批读取，每批映射后回调
    /// <para>空结果不触发回调</para>
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="size">每批条数</param>
    /// <param name="done">分批回调，参数为已映射的 DTO 列表</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <remarks>
    /// <para>空结果集（0 条）时不会触发 done 回调。</para>
    /// <para>reader 以 buffered:false 打开并在 done 回调期间保持打开——禁止在回调外继续消费该 reader，回调内亦禁止在同一连接上执行嵌套查询（DataReader 冲突）。</para>
    /// </remarks>
    [RequiresDynamicCode("ToChunkDto uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("ToChunkDto scans attributes via reflection.")]
    public void ToChunkDto<TDto>(string sql, object? param, int size, Action<List<TDto>> done,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null) where TDto : class
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), size, ErrorMessages.BatchSizeMustBePositive()); // T-F ② 契约统一（与 EFCore 对齐）

        var entities = _connection.Query<TSource>(sql, param, transaction, false, commandTimeout, commandType);

        var mapFunc = GetMapListFunc<TDto>();
        var chunk = new List<TSource>(size);
        foreach (var entity in entities)
        {
            chunk.Add(entity);
            if (chunk.Count >= size)
            {
                var mapped = mapFunc(chunk).Cast<TDto>().ToList();
                done(mapped);
                chunk = new List<TSource>(size);
            }
        }
        if (chunk.Count > 0)
        {
            var mapped = mapFunc(chunk).Cast<TDto>().ToList();
            done(mapped);
        }
    }

    /// <summary>
    /// 异步流式分块查询
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="size">每批条数</param>
    /// <param name="done">异步分批回调，参数为已映射的 DTO 列表</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步分块操作的任务</returns>
    /// <remarks>
    /// <para>真流式——经同步非缓冲通道打开（与同步 ToChunkDto 对齐），
    /// reader 逐行读取、每批映射后回调，回调期间 reader 保持打开——回调内禁止在同一连接上执行嵌套查询（DataReader 冲突）。</para>
    /// <para>不经 Dapper QueryAsync(CommandFlags.None)：异步管道返回的延迟枚举被同步 MoveNext 消费时，
    /// 在部分 provider（Microsoft.Data.Sqlite 实测）报 "reader is closed"（异步打开的 reader 非同步枚举安全形态）。</para>
    /// </remarks>
    [RequiresDynamicCode("ToChunkDtoAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("ToChunkDtoAsync scans attributes via reflection.")]
    public async Task ToChunkDtoAsync<TDto>(string sql, object? param, int size, Func<List<TDto>, Task> done,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null, CancellationToken cancellationToken = default) where TDto : class
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), size, ErrorMessages.BatchSizeMustBePositive()); // T-F ② 契约统一（与 EFCore 对齐）

        // 修复：buffered:false 同步非缓冲打开，真流式（原默认 buffered 全量物化后 foreach，伪流式）
        var entities = _connection.Query<TSource>(sql, param, transaction, false, commandTimeout, commandType);

        var mapFunc = GetMapListFunc<TDto>();
        var chunk = new List<TSource>(size);
        foreach (var entity in entities)
        {
            chunk.Add(entity);
            if (chunk.Count >= size)
            {
                var mapped = mapFunc(chunk).Cast<TDto>().ToList();
                await done(mapped).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                chunk = new List<TSource>(size);
            }
        }
        if (chunk.Count > 0)
        {
            var mapped = mapFunc(chunk).Cast<TDto>().ToList();
            await done(mapped).ConfigureAwait(false);
        }
    }
}
