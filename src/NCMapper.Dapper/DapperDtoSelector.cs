using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Dapper;
using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Dapper;

/// <summary>
/// Dapper DTO 选择器 — AsDto&lt;TSource&gt; 返回的终端操作入口
/// <para>所有查询/终端方法统一走 NCMapper 映射管道：Mask/Convert/Pipeline 永远生效</para>
/// <para>泛型设计：TSource 由调用方通过 AsDto&lt;TSource&gt;() 显式指定，与 EFCore/FreeSql 对齐</para>
/// <para><b>导航属性契约</b>：本类只执行调用方给定的 SQL 并把结果集的内存映射为 DTO，
/// 即<b>只做字段映射，不负责加载导航</b>——不会为 DTO 的嵌套对象追加 JOIN 或二次查询。
/// SQL 未取出的列、未 JOIN 的关联表，其对应的嵌套字段即为 null，这是<b>预期行为</b>，
/// 不是映射缺陷；需要嵌套数据时请自行在 SQL 里 JOIN / 子查询取出。</para>
/// <para> 分家：partial 拆分 5 文件（门面/Paging/Tree/MultiResult/Execution），
/// 本文件为门面主文件（字符串/CommandDefinition 查询入口 + 基础设施 + 全部 static 字段留守）</para>
/// <para> 上移：BuildTree 已迁核心库 NCMapper.Internal.TreeBuilder
/// （树路径经 DapperDtoSelector.Tree.cs 改调，行为等价基线 = 现状自动查找语义）</para>
/// </summary>
/// <typeparam name="TSource">源实体类型</typeparam>
public sealed partial class DapperDtoSelector<TSource> where TSource : class
{
    // 分页参数名常量（-7：消除魔法字符串，同步/异步共用）
    /// <summary>
    /// 分页偏移参数名（消除魔法字符串）
    /// </summary>
    private const string OffsetParamName = "__nc_offset";

    /// <summary>
    /// 分页页大小参数名
    /// </summary>
    private const string PageSizeParamName = "__nc_pageSize";

    /// <summary>
    /// Dapper 数据库连接
    /// </summary>
    private readonly IDbConnection _connection;

    /// <summary>
    /// SQL 方言（负责分页/Count 改写）
    /// </summary>
    private readonly ISqlDialect _dialect;

    /// <summary>
    /// 初始化 DapperDtoSelector 实例（内部构造，由 AsDto 扩展方法创建）
    /// </summary>
    /// <param name="connection">Dapper 数据库连接</param>
    /// <param name="dialect">SQL 方言（负责分页/Count 改写）</param>
    internal DapperDtoSelector(IDbConnection connection, ISqlDialect dialect)
    {
        _connection = connection;
        _dialect = dialect;
    }

    // ===== 委托缓存=====

    // T-4 双轨归一（决策 A）：10 个查询委托 Lazy 已删除（基准证据：委托 vs 直调
    // 差异 -0.14%~-0.76%，噪声级，见 artifacts/dual_track/）。字符串版入口改为直调 Dapper 对应重载
    // （泛型类内 _connection.Query<TSource>(...) 编译期实例化，无需 MakeGenericMethod + Expression.Compile）。
    // 映射委托（s_mapListCache + CompileMapListDelegate）保留——属映射管道缓存，非查询执行路径。

    // 映射委托：同一 TSource 可按不同 TDto 映射，用 ConcurrentDictionary（键仅 dtoType，TSource 已固定）
    /// <summary>
    /// 映射委托缓存（键为 dtoType；TSource 已由泛型类固定）
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<IEnumerable<TSource>, IEnumerable<object>>> s_mapListCache = new();

    /// <summary>
    /// Trace 前缀 — 标识 Dapper AsDto 路径，与 EFCore/FreeSql 日志区分
    /// </summary>
    private const string TracePrefix = "[Dapper AsDto]";

    // ===== 查询类终端方法（对齐 Dapper 原生）=====

    /// <summary>
    /// 查询并映射为 DTO 列表 — Mask/Convert/Pipeline 永远生效
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="buffered">是否缓冲（默认 true）</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <returns>DTO 列表</returns>
    [RequiresDynamicCode("Query uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("Query scans attributes via reflection.")]
    public IEnumerable<TDto> Query<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null, bool buffered = true,
        int? commandTimeout = null, CommandType? commandType = null) where TDto : class
    {
        var entities = QuerySource(sql, param, transaction, buffered, commandTimeout, commandType);
        return MapListCached<TDto>(entities).Cast<TDto>();
    }

    /// <summary>
    /// 查询第一条并映射 — 走 Dapper 原生 QueryFirst 单行查询，无结果时抛 InvalidOperationException
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <returns>映射后的 DTO 对象</returns>
    [RequiresDynamicCode("QueryFirst uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryFirst scans attributes via reflection.")]
    public TDto QueryFirst<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null,
        int? commandTimeout = null, CommandType? commandType = null) where TDto : class
    {
        var entity = _connection.QueryFirst<TSource>(sql, param, transaction, commandTimeout, commandType);
        return Mapper.Map<TSource, TDto>(entity)!;
    }

    /// <summary>
    /// 查询第一条并映射 — 走 Dapper 原生 QueryFirstOrDefault，无结果时返回 null
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("QueryFirstOrDefault uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryFirstOrDefault scans attributes via reflection.")]
    public TDto? QueryFirstOrDefault<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null,
        int? commandTimeout = null, CommandType? commandType = null) where TDto : class
    {
        var entity = _connection.QueryFirstOrDefault<TSource>(sql, param, transaction, commandTimeout, commandType);
        return entity is null ? null : Mapper.Map<TSource, TDto>(entity);
    }

    /// <summary>
    /// 查询唯一一条并映射 — 走 Dapper 原生 QuerySingle，无结果或多条时抛异常
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <returns>映射后的 DTO 对象</returns>
    [RequiresDynamicCode("QuerySingle uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QuerySingle scans attributes via reflection.")]
    public TDto QuerySingle<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null,
        int? commandTimeout = null, CommandType? commandType = null) where TDto : class
    {
        var entity = _connection.QuerySingle<TSource>(sql, param, transaction, commandTimeout, commandType);
        return Mapper.Map<TSource, TDto>(entity)!;
    }

    /// <summary>
    /// 查询唯一一条并映射 — 走 Dapper 原生 QuerySingleOrDefault，无结果时返回 null，多条时抛异常
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <returns>映射后的 DTO 对象</returns>
    [RequiresDynamicCode("QuerySingleOrDefault uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QuerySingleOrDefault scans attributes via reflection.")]
    public TDto? QuerySingleOrDefault<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null,
        int? commandTimeout = null, CommandType? commandType = null) where TDto : class
    {
        var entity = _connection.QuerySingleOrDefault<TSource>(sql, param, transaction, commandTimeout, commandType);
        return entity is null ? null : Mapper.Map<TSource, TDto>(entity);
    }

    /// <summary>
    /// 异步查询并映射为 DTO 列表
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>DTO 列表</returns>
    [RequiresDynamicCode("QueryAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryAsync scans attributes via reflection.")]
    public async Task<IEnumerable<TDto>> QueryAsync<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null, CancellationToken cancellationToken = default) where TDto : class
    {
        var entities = await QuerySourceAsync(sql, param, transaction, commandTimeout, commandType, cancellationToken).ConfigureAwait(false);
        return MapListCached<TDto>(entities).Cast<TDto>();
    }

    /// <summary>
    /// 异步查询第一条 — 走 Dapper 原生 QueryFirstAsync，无结果时抛异常
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>映射后的 DTO 对象</returns>
    [RequiresDynamicCode("QueryFirstAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryFirstAsync scans attributes via reflection.")]
    public async Task<TDto> QueryFirstAsync<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null, CancellationToken cancellationToken = default) where TDto : class
    {
        var command = new CommandDefinition(sql, param, transaction, commandTimeout, commandType,
            CommandFlags.None, cancellationToken);
        var entity = await _connection.QueryFirstAsync<TSource>(command).ConfigureAwait(false);
        return Mapper.Map<TSource, TDto>(entity)!;
    }

    /// <summary>
    /// 异步查询第一条 — 走 Dapper 原生 QueryFirstOrDefaultAsync，无结果时返回 null
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("QueryFirstOrDefaultAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryFirstOrDefaultAsync scans attributes via reflection.")]
    public async Task<TDto?> QueryFirstOrDefaultAsync<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null, CancellationToken cancellationToken = default) where TDto : class
    {
        var command = new CommandDefinition(sql, param, transaction, commandTimeout, commandType,
            CommandFlags.None, cancellationToken);
        var entity = await _connection.QueryFirstOrDefaultAsync<TSource>(command).ConfigureAwait(false);
        return entity is null ? null : Mapper.Map<TSource, TDto>(entity);
    }

    /// <summary>
    /// 异步查询唯一一条 — 走 Dapper 原生 QuerySingleAsync，无结果或多条时抛异常
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>映射后的 DTO 对象</returns>
    [RequiresDynamicCode("QuerySingleAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QuerySingleAsync scans attributes via reflection.")]
    public async Task<TDto> QuerySingleAsync<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null, CancellationToken cancellationToken = default) where TDto : class
    {
        var command = new CommandDefinition(sql, param, transaction, commandTimeout, commandType,
            CommandFlags.None, cancellationToken);
        var entity = await _connection.QuerySingleAsync<TSource>(command).ConfigureAwait(false);
        return Mapper.Map<TSource, TDto>(entity)!;
    }

    /// <summary>
    /// 异步查询唯一一条 — 走 Dapper 原生 QuerySingleOrDefaultAsync，无结果时返回 null
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("QuerySingleOrDefaultAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QuerySingleOrDefaultAsync scans attributes via reflection.")]
    public async Task<TDto?> QuerySingleOrDefaultAsync<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null, CancellationToken cancellationToken = default) where TDto : class
    {
        var command = new CommandDefinition(sql, param, transaction, commandTimeout, commandType,
            CommandFlags.None, cancellationToken);
        var entity = await _connection.QuerySingleOrDefaultAsync<TSource>(command).ConfigureAwait(false);
        return entity is null ? null : Mapper.Map<TSource, TDto>(entity);
    }

    // ===== CommandDefinition 重载=====

    /// <summary>
    /// 通过 CommandDefinition 查询并映射为 DTO 列表 — 推荐使用 CommandDefinition 以减少参数传递
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="command">Dapper 命令定义（含 SQL、参数、事务、超时、命令类型、CT）</param>
    /// <returns>DTO 列表</returns>
    [RequiresDynamicCode("Query uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("Query scans attributes via reflection.")]
    public IEnumerable<TDto> Query<TDto>(CommandDefinition command) where TDto : class
    {
        var entities = QuerySource(command);
        return MapListCached<TDto>(entities).Cast<TDto>();
    }

    /// <summary>
    /// 通过 CommandDefinition 异步查询并映射为 DTO 列表
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="command">Dapper 命令定义</param>
    /// <returns>DTO 列表</returns>
    [RequiresDynamicCode("QueryAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryAsync scans attributes via reflection.")]
    public async Task<IEnumerable<TDto>> QueryAsync<TDto>(CommandDefinition command) where TDto : class
    {
        var entities = await _connection.QueryAsync<TSource>(command).ConfigureAwait(false);
        return MapListCached<TDto>(entities).Cast<TDto>();
    }

    /// <summary>
    /// 通过 CommandDefinition 查询第一条并映射
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="command">Dapper 命令定义</param>
    /// <returns>映射后的 DTO 对象，无结果时抛 InvalidOperationException</returns>
    [RequiresDynamicCode("QueryFirst uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryFirst scans attributes via reflection.")]
    public TDto QueryFirst<TDto>(CommandDefinition command) where TDto : class
    {
        var entity = _connection.QueryFirst<TSource>(command);
        return Mapper.Map<TSource, TDto>(entity)!;
    }

    /// <summary>
    /// 通过 CommandDefinition 异步查询第一条并映射
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="command">Dapper 命令定义</param>
    /// <returns>映射后的 DTO 对象</returns>
    [RequiresDynamicCode("QueryFirstAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryFirstAsync scans attributes via reflection.")]
    public async Task<TDto> QueryFirstAsync<TDto>(CommandDefinition command) where TDto : class
    {
        var entity = await _connection.QueryFirstAsync<TSource>(command).ConfigureAwait(false);
        return Mapper.Map<TSource, TDto>(entity)!;
    }

    /// <summary>
    /// 通过 CommandDefinition 查询第一条并映射 — 无结果时返回 null
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="command">Dapper 命令定义</param>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("QueryFirstOrDefault uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryFirstOrDefault scans attributes via reflection.")]
    public TDto? QueryFirstOrDefault<TDto>(CommandDefinition command) where TDto : class
    {
        var entity = _connection.QueryFirstOrDefault<TSource>(command);
        return entity is null ? null : Mapper.Map<TSource, TDto>(entity);
    }

    /// <summary>
    /// 通过 CommandDefinition 异步查询第一条并映射 — 无结果时返回 null
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="command">Dapper 命令定义</param>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("QueryFirstOrDefaultAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryFirstOrDefaultAsync scans attributes via reflection.")]
    public async Task<TDto?> QueryFirstOrDefaultAsync<TDto>(CommandDefinition command) where TDto : class
    {
        var entity = await _connection.QueryFirstOrDefaultAsync<TSource>(command).ConfigureAwait(false);
        return entity is null ? null : Mapper.Map<TSource, TDto>(entity);
    }

    /// <summary>
    /// 通过 CommandDefinition 查询唯一一条并映射
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="command">Dapper 命令定义</param>
    /// <returns>映射后的 DTO 对象，无结果或多条时抛异常</returns>
    [RequiresDynamicCode("QuerySingle uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QuerySingle scans attributes via reflection.")]
    public TDto QuerySingle<TDto>(CommandDefinition command) where TDto : class
    {
        var entity = _connection.QuerySingle<TSource>(command);
        return Mapper.Map<TSource, TDto>(entity)!;
    }

    /// <summary>
    /// 通过 CommandDefinition 异步查询唯一一条并映射
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="command">Dapper 命令定义</param>
    /// <returns>映射后的 DTO 对象</returns>
    [RequiresDynamicCode("QuerySingleAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QuerySingleAsync scans attributes via reflection.")]
    public async Task<TDto> QuerySingleAsync<TDto>(CommandDefinition command) where TDto : class
    {
        var entity = await _connection.QuerySingleAsync<TSource>(command).ConfigureAwait(false);
        return Mapper.Map<TSource, TDto>(entity)!;
    }

    /// <summary>
    /// 通过 CommandDefinition 查询唯一一条并映射 — 无结果时返回 null，多条时抛异常
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="command">Dapper 命令定义</param>
    /// <returns>映射后的 DTO 对象</returns>
    [RequiresDynamicCode("QuerySingleOrDefault uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QuerySingleOrDefault scans attributes via reflection.")]
    public TDto? QuerySingleOrDefault<TDto>(CommandDefinition command) where TDto : class
    {
        var entity = _connection.QuerySingleOrDefault<TSource>(command);
        return entity is null ? null : Mapper.Map<TSource, TDto>(entity);
    }

    /// <summary>
    /// 通过 CommandDefinition 异步查询唯一一条并映射 — 无结果时返回 null
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="command">Dapper 命令定义</param>
    /// <returns>映射后的 DTO 对象</returns>
    [RequiresDynamicCode("QuerySingleOrDefaultAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QuerySingleOrDefaultAsync scans attributes via reflection.")]
    public async Task<TDto?> QuerySingleOrDefaultAsync<TDto>(CommandDefinition command) where TDto : class
    {
        var entity = await _connection.QuerySingleOrDefaultAsync<TSource>(command).ConfigureAwait(false);
        return entity is null ? null : Mapper.Map<TSource, TDto>(entity);
    }

    // ===== 终端操作类方法（对齐 EFCore/FreeSql）=====
    // ToPagedList/ToPagedListAsync/ToChunkDto/ToChunkDtoAsync 迁入 DapperDtoSelector.Paging.cs；
    // ToTreeList/ToTreeListAsync 迁入 DapperDtoSelector.Tree.cs（分家）

    /// <summary>
    /// 查询并构建字典 — keySelector 作用于源实体 TSource，元素值由 NCMapper 映射为 TDto
    /// <para>与 EFCore/FreeSql 对齐：仅 TKey 和 TDto 两个泛型参数，TSource 由 Selector 泛型参数确定</para>
    /// </summary>
    /// <typeparam name="TKey">字典键类型</typeparam>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="keySelector">键选择器（作用于源实体 TSource）</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <returns>DTO 字典</returns>
    [RequiresDynamicCode("ToDictionary uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("ToDictionary scans attributes via reflection.")]
    public Dictionary<TKey, TDto> ToDictionary<TKey, TDto>(string sql, object? param,
        Func<TSource, TKey> keySelector, IDbTransaction? transaction = null,
        int? commandTimeout = null, CommandType? commandType = null)
        where TKey : notnull where TDto : class
    {
        var entityList = QuerySource(sql, param, transaction, true, commandTimeout, commandType).ToList(); // 单次枚举物化，映射与配对同源（防两次独立枚举错位）
        var dtos = MapListCached<TDto>(entityList).Cast<TDto>().ToList();
        var result = new Dictionary<TKey, TDto>();
        for (int i = 0; i < entityList.Count; i++)
        {
            var key = keySelector(entityList[i]);
            result.Add(key, dtos[i]); // 重复键抛 ArgumentException（与 EFCore/FreeSql 原生 ToDictionary 语义对齐）
        }
        return result;
    }

    /// <summary>
    /// 异步查询并构建字典 — keySelector 作用于源实体 TSource，元素值由 NCMapper 映射为 TDto
    /// </summary>
    /// <typeparam name="TKey">字典键类型</typeparam>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="keySelector">键选择器（作用于源实体 TSource）</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>DTO 字典</returns>
    [RequiresDynamicCode("ToDictionaryAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("ToDictionaryAsync scans attributes via reflection.")]
    public async Task<Dictionary<TKey, TDto>> ToDictionaryAsync<TKey, TDto>(string sql, object? param,
        Func<TSource, TKey> keySelector, IDbTransaction? transaction = null,
        int? commandTimeout = null, CommandType? commandType = null,
        CancellationToken cancellationToken = default)
        where TKey : notnull where TDto : class
    {
        var entityList = (await QuerySourceAsync(sql, param, transaction, commandTimeout, commandType, cancellationToken).ConfigureAwait(false)).ToList(); // C-15 单次枚举物化
        var dtos = MapListCached<TDto>(entityList).Cast<TDto>().ToList();
        var result = new Dictionary<TKey, TDto>();
        for (int i = 0; i < entityList.Count; i++)
        {
            var key = keySelector(entityList[i]);
            result.Add(key, dtos[i]); // 重复键抛 ArgumentException（与 EFCore/FreeSql 原生 ToDictionary 语义对齐）
        }
        return result;
    }

    /// <summary>
    /// 查询并构建 ILookup — keySelector 作用于源实体 TSource，元素值由 NCMapper 映射为 TDto
    /// </summary>
    /// <typeparam name="TKey">分组键类型</typeparam>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="keySelector">键选择器（作用于源实体 TSource）</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <returns>DTO 分组查找表</returns>
    [RequiresDynamicCode("ToLookup uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("ToLookup scans attributes via reflection.")]
    public ILookup<TKey, TDto> ToLookup<TKey, TDto>(string sql, object? param,
        Func<TSource, TKey> keySelector, IDbTransaction? transaction = null,
        int? commandTimeout = null, CommandType? commandType = null)
        where TKey : notnull where TDto : class
    {
        var entityList = QuerySource(sql, param, transaction, true, commandTimeout, commandType).ToList(); // C-15 单次枚举物化
        var dtos = MapListCached<TDto>(entityList).Cast<TDto>().ToList();
        var pairs = new List<(TKey Key, TDto Dto)>();
        for (int i = 0; i < entityList.Count; i++)
        {
            pairs.Add((keySelector(entityList[i]), dtos[i]));
        }
        return pairs.ToLookup(p => p.Key, p => p.Dto);
    }

    /// <summary>
    /// 异步查询并构建 ILookup — keySelector 作用于源实体 TSource，元素值由 NCMapper 映射为 TDto
    /// </summary>
    /// <typeparam name="TKey">分组键类型</typeparam>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="keySelector">键选择器（作用于源实体 TSource）</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>DTO 分组查找表</returns>
    [RequiresDynamicCode("ToLookupAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("ToLookupAsync scans attributes via reflection.")]
    public async Task<ILookup<TKey, TDto>> ToLookupAsync<TKey, TDto>(string sql, object? param,
        Func<TSource, TKey> keySelector, IDbTransaction? transaction = null,
        int? commandTimeout = null, CommandType? commandType = null,
        CancellationToken cancellationToken = default)
        where TKey : notnull where TDto : class
    {
        var entityList = (await QuerySourceAsync(sql, param, transaction, commandTimeout, commandType, cancellationToken).ConfigureAwait(false)).ToList(); // C-15 单次枚举物化
        var dtos = MapListCached<TDto>(entityList).Cast<TDto>().ToList();
        var pairs = new List<(TKey Key, TDto Dto)>();
        for (int i = 0; i < entityList.Count; i++)
        {
            pairs.Add((keySelector(entityList[i]), dtos[i]));
        }
        return pairs.ToLookup(p => p.Key, p => p.Dto);
    }

    /// <summary>
    /// 异步流式查询 — 逐元素读取并映射，内存占用 O(1)，适合超大量结果集
    /// <para>与 ToChunkDto 的区别：ToChunkDto 是分批回调（Action/Func），QueryUnbufferedAsync 是 IAsyncEnumerable 逐元素流式</para>
    /// <para>使用 Dapper ExecuteReaderAsync + GetRowParser 实现真正的流式读取，非全量缓存</para>
    /// <para>CT 取消后不再 yield 后续元素，已 yield 的元素不受影响</para>
    /// </summary>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <param name="sql">SQL 语句</param>
    /// <param name="param">参数</param>
    /// <param name="transaction">事务</param>
    /// <param name="commandTimeout">命令超时</param>
    /// <param name="commandType">命令类型</param>
    /// <param name="cancellationToken">取消令牌 — 传播到 ReadAsync，取消后终止枚举</param>
    /// <returns>DTO 异步枚举流</returns>
    [RequiresDynamicCode("QueryUnbufferedAsync uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("QueryUnbufferedAsync scans attributes via reflection.")]
    public async IAsyncEnumerable<TDto> QueryUnbufferedAsync<TDto>(string sql, object? param = null,
        IDbTransaction? transaction = null, int? commandTimeout = null,
        CommandType? commandType = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TDto : class
    {
        var command = new CommandDefinition(sql, param, transaction, commandTimeout, commandType,
            cancellationToken: cancellationToken);
        using var reader = await _connection.ExecuteReaderAsync(command).ConfigureAwait(false);
        var parser = SqlMapper.GetRowParser<TSource>(reader);
        var dbReader = reader as System.Data.Common.DbDataReader;

        while (dbReader != null
            ? await dbReader.ReadAsync(cancellationToken).ConfigureAwait(false)
            : reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entity = parser(reader);
            yield return Mapper.Map<TSource, TDto>(entity)!;
        }
    }

    // ===== QueryMultiple 多结果集=====
    // QueryMultiple×2 + ReadByType/MapListByType 迁入 DapperDtoSelector.MultiResult.cs（分家）

    // ===== 内部基础设施 =====

    /// <summary>
    /// 从 TDto 的 [MapFrom] 获取源实体类型 — 供 QueryMultiple 等场景推断次源类型
    /// <para>与泛型入口不同：泛型入口由调用方显式指定 TSource，此方法仅用于非泛型读取元数据场景</para>
    /// </summary>
    /// <param name="dtoType">DTO 类型</param>
    /// <returns>源实体类型</returns>
    /// <exception cref="MapConfigurationException">DTO 未标注 [MapFrom] 时抛出</exception>
    internal static Type ResolveSourceTypeFromMapFrom(Type dtoType)
    {
        var attr = dtoType.GetCustomAttribute<MapFromAttribute>();
        if (attr?.FromSource == null)
            throw new MapConfigurationException(
                ErrorMessages.DtoMissingMapFrom(dtoType.FullName!),
                sourceType: null, targetType: dtoType);
        return attr.FromSource;
    }

    /// <summary>
    /// 查询源实体（同步）— 直调 Dapper 字符串重载（T-4 决策 A：泛型类内编译期实例化，无委托编译）
    /// </summary>
    private IEnumerable<TSource> QuerySource(string sql, object? param,
        IDbTransaction? transaction, bool buffered, int? commandTimeout, CommandType? commandType)
        => _connection.Query<TSource>(sql, param, transaction, buffered, commandTimeout, commandType);

    /// <summary>
    /// 查询源实体（同步）— CommandDefinition 重载，直接调用 Dapper 原生方法
    /// </summary>
    private IEnumerable<TSource> QuerySource(CommandDefinition command)
        => _connection.Query<TSource>(command);

    /// <summary>
    /// 查询源实体（异步）— 通过 CommandDefinition 传递 CancellationToken
    /// </summary>
    /// <remarks>维持 Buffered（全量物化）——Dapper 异步管道的非缓冲
    /// 延迟枚举被同步 MoveNext 消费非安全形态（Sqlite 实测 "reader is closed"）；真流式通道由
    /// ToChunkDtoAsync 经同步非缓冲 Query(buffered:false) 承载（见 DapperDtoSelector.Paging.cs）。</remarks>
    private async Task<IEnumerable<TSource>> QuerySourceAsync(string sql, object? param,
        IDbTransaction? transaction, int? commandTimeout, CommandType? commandType,
        CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(sql, param, transaction, commandTimeout, commandType,
            CommandFlags.Buffered, cancellationToken);
        return await _connection.QueryAsync<TSource>(command).ConfigureAwait(false);
    }

    /// <summary>
    /// 映射列表（缓存委托）— 走 Mapper.MapList 完整管道（Mask/Convert/Pipeline）
    /// <para>委托调 Mapper.MapList，内部实时检查 MapPipeline.s_isEnabled，无需 Pipeline 版本号。</para>
    /// </summary>
    [RequiresDynamicCode("MapListCached builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("MapListCached scans type members and attributes via reflection. Some members may be trimmed.")]
    private static IEnumerable<object> MapListCached<TDto>(IEnumerable<TSource> entities) where TDto : class
    {
        if (MapTracer.GloballyEnabled) MapTracer.Write($"{TracePrefix} {typeof(TSource).Name} → {typeof(TDto).Name} (批量映射)");
        var mapFunc = GetMapListFunc<TDto>();
        return mapFunc(entities);
    }

    /// <summary>
    /// 获取或创建映射委托 — TSource 已固定，ConcurrentDictionary 键仅保留 dtoType
    /// </summary>
    [RequiresDynamicCode("GetMapListFunc builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("GetMapListFunc scans type members and attributes via reflection. Some members may be trimmed.")]
    private static Func<IEnumerable<TSource>, IEnumerable<object>> GetMapListFunc<TDto>() where TDto : class
        => s_mapListCache.GetOrAdd(typeof(TDto), _ => CompileMapListDelegate<TDto>());

    // ===== 委托编译基础设施（T-4 决策 A 后仅剩映射委托）=====

    // T-4 双轨归一（决策 A）：10 个 s_query*Method MethodInfo 已随查询委托删除——直调路径在泛型类内
    // 编译期实例化，不再需要 MakeGenericMethod 解析。仅保留映射管道所需反射设施（s_mapListMethod
    // 服务 CompileMapListDelegate；两个 ConcurrentDictionary 服务 QueryMultiple 反射缓存）。

    // AOT 分析说明：s_mapListMethod 初始化器经 GetMethods 扫描 Mapper 公共面（多名成员带 Requires* 标注），
    // 字段归属 .cctor 无法以 Requires* 标注，故以 pragma 精确抑制；消费者侧风险已在公开方法上如实标注。
#pragma warning disable IL2026 // RequiresUnreferencedCode

    /// <summary>
    /// Mapper.MapList 双泛型方法 MethodInfo（列表映射委托编译用）
    /// </summary>
    private static readonly MethodInfo s_mapListMethod =
        typeof(Mapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Mapper.MapList) && m.GetGenericArguments().Length == 2);

#pragma warning restore IL2026

    // QueryMultiple 次结果集反射缓存（ORM 发现 8）：每 (sourceType[, dtoType]) 一次解析，
    // 首次解析失败抛带版本信息的明确异常（替代 First 无匹配的隐式异常）
    /// <summary>
    /// QueryMultiple 次结果集反射缓存（键为 sourceType）
    /// </summary>
    private static readonly ConcurrentDictionary<Type, MethodInfo> s_readByTypeCache = new();

    /// <summary>
    /// MapList 按 (sourceType, dtoType) 的反射缓存
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, Type), MethodInfo> s_mapListByTypeCache = new();

}
