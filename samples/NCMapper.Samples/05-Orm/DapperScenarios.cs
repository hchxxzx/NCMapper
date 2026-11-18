using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using NCMapper.Attributes;
using NCMapper.Dapper;
using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Orm;

/// <summary>
/// Dapper 集成场景集 — Query 系列、ToPagedList、ToDictionary、ToChunkDto、ToTreeList、QueryMultiple 等。
/// </summary>
public static class DapperScenarios
{
    /// <summary>
    /// Query + AsDto：SQL 结果映射为 DTO 列表。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>映射后的 DTO 列表</returns>
    #region Dapper_Query
    public static List<UserDto> Query(SqliteConnection connection)
        => connection.AsDto<UserEntity>()
            .Query<UserDto>("SELECT * FROM Users WHERE Age > @Age", new { Age = 20 })
    #endregion
            .ToList();

    /// <summary>
    /// QueryFirst：返回第一条映射后的 DTO。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>第一条匹配的 DTO，或 null</returns>
    #region Dapper_QueryFirst
    public static UserDto? QueryFirst(SqliteConnection connection)
        => connection.AsDto<UserEntity>()
            .QueryFirst<UserDto>("SELECT * FROM Users WHERE Id = @Id", new { Id = 1 });
    #endregion

    /// <summary>
    /// QueryFirstOrDefault：返回第一条或 null。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>第一条匹配的 DTO，或 null</returns>
    #region Dapper_QueryFirstOrDefault
    public static UserDto? QueryFirstOrDefault(SqliteConnection connection)
        => connection.AsDto<UserEntity>()
            .QueryFirstOrDefault<UserDto>("SELECT * FROM Users WHERE Id = 999", null);
    #endregion

    /// <summary>
    /// QuerySingle：恰好一条记录。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>唯一匹配的 DTO</returns>
    #region Dapper_QuerySingle
    public static UserDto? QuerySingle(SqliteConnection connection)
        => connection.AsDto<UserEntity>()
            .QuerySingle<UserDto>("SELECT * FROM Users WHERE Id = 1", null);
    #endregion

    /// <summary>
    /// QuerySingleOrDefault：恰好一条或 null。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>唯一匹配的 DTO，或 null</returns>
    #region Dapper_QuerySingleOrDefault
    public static UserDto? QuerySingleOrDefault(SqliteConnection connection)
        => connection.AsDto<UserEntity>()
            .QuerySingleOrDefault<UserDto>("SELECT * FROM Users WHERE Id = 999", null);
    #endregion

    /// <summary>
    /// ToPagedList：SQL 级分页，自动检测方言。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>分页结果</returns>
    #region Dapper_Advanced
    // Advanced terminal group: paging / dictionary / lookup / chunked read / tree query
    public static PagedList<UserDto> ToPagedList(SqliteConnection connection)
        => connection.AsDto<UserEntity>()
            .ToPagedList<UserDto>("SELECT * FROM Users", null, 1, 2);
    #endregion


    /// <summary>
    /// ToDictionary：映射为字典。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>以实体 Id 为 Key 的字典</returns>
    #region Dapper_Advanced_ToDictionary
    public static Dictionary<int, UserDto> ToDictionary(SqliteConnection connection)
        => connection.AsDto<UserEntity>()
            .ToDictionary<int, UserDto>("SELECT * FROM Users", null, e => e.Id);
    #endregion


    /// <summary>
    /// ToLookup：按键分组。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>按 Age/10 分组的 Lookup</returns>
    #region Dapper_Advanced_ToLookup
    public static ILookup<int, UserDto> ToLookup(SqliteConnection connection)
        => connection.AsDto<UserEntity>()
            .ToLookup<int, UserDto>("SELECT * FROM Users", null, e => e.Age / 10);
    #endregion


    /// <summary>
    /// ToChunkDto：分批回调读取。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>全部批次合并</returns>
    #region Dapper_Advanced_ToChunkDto
    public static List<UserDto> ToChunkDto(SqliteConnection connection)
    {
        var all = new List<UserDto>();
        connection.AsDto<UserEntity>()
            .ToChunkDto<UserDto>("SELECT * FROM Users", null, 2, chunk => all.AddRange(chunk));
        return all;
    }
    #endregion


    /// <summary>
    /// ToTreeList：树形结构投影。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接（需含 Categories 表）</param>
    /// <returns>树形结构 DTO</returns>
    #region Dapper_Advanced_ToTreeList
    public static List<CategoryDto> ToTreeList(SqliteConnection connection)
        => connection.AsDto<CategoryEntity>()
            .ToTreeList<CategoryDto>("SELECT * FROM Categories", null);

    #endregion

    /// <summary>
    /// QueryUnbufferedAsync：异步流式枚举。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>异步流式返回的 DTO 列表</returns>
    #region Dapper_Advanced_QueryUnbufferedAsync
    public static async Task<List<UserDto>> QueryUnbufferedAsync(SqliteConnection connection)
    {
        var results = new List<UserDto>();
        await foreach (var dto in connection.AsDto<UserEntity>()
            .QueryUnbufferedAsync<UserDto>("SELECT * FROM Users"))
        {
            results.Add(dto);
        }
        return results;
    }
    #endregion

    /// <summary>
    /// QueryMultiple：多结果集查询。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>两个结果集的 DTO 列表</returns>
    #region Dapper_Advanced_QueryMultiple
    public static (List<UserDto> First, List<UserDto> Second) QueryMultiple(SqliteConnection connection)
    {
        var result = connection.AsDto<UserEntity>()
            .QueryMultiple<UserDto, UserDto>("SELECT * FROM Users WHERE Age > 20; SELECT * FROM Users WHERE Age <= 25");
        return (result.Items1, result.Items2);
    }
    #endregion

    /// <summary>
    /// QueryMultiple 三结果集：一次 SQL 消费三个结果集（三泛型重载形态）。
    /// <para>主结果集用 Selector 的 TSource（UserEntity）；次结果集按 TDto 的 [MapFrom] 推断源类型</para>
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接（需含 Users 与 Categories 表）</param>
    /// <returns>三个结果集的 DTO 列表（Users 两态 + Categories 平铺）</returns>
    #region Dapper_Advanced_QueryMultiple3
    public static (List<UserDto> Set1, List<UserDto> Set2, List<CategoryDto> Set3) QueryMultiple3(SqliteConnection connection)
        => connection.AsDto<UserEntity>()
            .QueryMultiple<UserDto, UserDto, CategoryDto>(
                "SELECT * FROM Users WHERE Age > 20; SELECT * FROM Users WHERE Age <= 25; SELECT * FROM Categories");
    #endregion

    /// <summary>
    /// QueryAsync：异步查询。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>映射后的 DTO 列表</returns>
    #region Dapper_QueryAsync
    public static async Task<IEnumerable<UserDto>> QueryAsync(SqliteConnection connection)
        => await connection.AsDto<UserEntity>()
            .QueryAsync<UserDto>("SELECT * FROM Users WHERE Age > @Age", new { Age = 20 });
    #endregion

    /// <summary>
    /// ToPagedListAsync：异步分页。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>异步分页结果</returns>
    public static async Task<PagedList<UserDto>> ToPagedListAsync(SqliteConnection connection)
        => await connection.AsDto<UserEntity>()
            .ToPagedListAsync<UserDto>("SELECT * FROM Users", null, 1, 2);

    /// <summary>
    /// QueryFirstAsync：异步查询首条。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>第一条匹配的 DTO</returns>
    #region Dapper_QueryFirstAsync
    public static async Task<UserDto?> QueryFirstAsync(SqliteConnection connection)
        => await connection.AsDto<UserEntity>()
            .QueryFirstAsync<UserDto>("SELECT * FROM Users WHERE Id = @Id", new { Id = 1 });
    #endregion

    /// <summary>
    /// QueryFirstOrDefaultAsync：异步查询首条或 null。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>第一条匹配的 DTO，无匹配时为 null</returns>
    #region Dapper_QueryFirstOrDefaultAsync
    public static async Task<UserDto?> QueryFirstOrDefaultAsync(SqliteConnection connection)
        => await connection.AsDto<UserEntity>()
            .QueryFirstOrDefaultAsync<UserDto>("SELECT * FROM Users WHERE Id = @Id", new { Id = 999 });
    #endregion

    /// <summary>
    /// ToChunkDtoAsync：异步分批回调读取（逐页取数，每页 size 条）。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>全部批次合并</returns>
    #region Dapper_Advanced_ToChunkDtoAsync
    public static async Task<List<UserDto>> ToChunkDtoAsync(SqliteConnection connection)
    {
        var all = new List<UserDto>();
        await connection.AsDto<UserEntity>()
            .ToChunkDtoAsync<UserDto>("SELECT * FROM Users", null, 2, async chunk =>
            {
                all.AddRange(chunk);   // batch accumulate, size rows per batch
                await Task.CompletedTask;
            });
        return all;
    }
    #endregion

    /// <summary>
    /// ToPagedListWithDialect：显式注入 ISqlDialect 的 SQL 级分页（方言经 AsDto 入口注入，跳过自动检测）。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>分页结果</returns>
    #region Dapper_Advanced_PagedWithDialect
    public static PagedList<UserDto> ToPagedListWithDialect(SqliteConnection connection)
        => connection.AsDto<UserEntity>(new MySqlDialect())
            .ToPagedList<UserDto>("SELECT * FROM Users", null, 1, 2);
    #endregion

    // ===== 场景注册入口=====
    public static void Run()
    {
    using var connection = DbSeedData.CreateInMemoryConnection();
    connection.Execute(@"
        CREATE TABLE Users (
            Id INTEGER PRIMARY KEY,
            Name TEXT,
            Age INTEGER,
            Email TEXT,
            Status INTEGER,
            CreatedAt TEXT
        )");
    connection.Execute("INSERT INTO Users VALUES (1,'Alice',25,'alice@example.com',0,'2025-01-15')");
    connection.Execute("INSERT INTO Users VALUES (2,'Bob',30,'bob@example.com',1,'2025-03-20')");
    connection.Execute("INSERT INTO Users VALUES (3,'Charlie',35,'charlie@example.com',0,'2025-06-01')");

    // Categories for ToTreeList
    connection.Execute("CREATE TABLE Categories (NodeId INTEGER PRIMARY KEY, ParentNodeId INTEGER, Name TEXT)");
    connection.Execute("INSERT INTO Categories VALUES (1,NULL,'Root'),(2,1,'Child1'),(3,1,'Child2'),(4,2,'Grandchild')");

    var dapperDtos = DapperScenarios.Query(connection);
    OutputHelper.Dump("502-dapper-query", "WHERE Age > 20", dapperDtos);
    // 期望值：种子 SQL 契约 WHERE Age > 20 → 3 行，首行 Alice/25（列→属性映射语义）
    if (dapperDtos.Count != 3 || dapperDtos[0].Name != "Alice" || dapperDtos[0].Age != 25)
        OutputHelper.Error($"502-dapper-query: WHERE Age > 20 should map 3 rows with first row Alice/25 (actual: {dapperDtos.Count} rows, first={dapperDtos.FirstOrDefault()?.Name}/{dapperDtos.FirstOrDefault()?.Age})");

    var dapperFirst = DapperScenarios.QueryFirst(connection);
    OutputHelper.Dump("502-dapper-first", "Id=1", dapperFirst!);
    // 期望值：种子契约 Id=1 行映射为 Alice
    if (dapperFirst == null || dapperFirst.Name != "Alice")
        OutputHelper.Error($"502-dapper-first: Id=1 should map to Alice (actual: {dapperFirst?.Name ?? "null"})");

    var dapperFirstOrDef = DapperScenarios.QueryFirstOrDefault(connection);
    OutputHelper.Dump("502-dapper-firstordefault", "Id=999 → null", dapperFirstOrDef == null ? "null ✓" : "unexpected");
    // 期望值：QueryFirstOrDefault 契约——无匹配返回 null
    if (dapperFirstOrDef != null)
        OutputHelper.Error("502-dapper-firstordefault: Id=999 should return null (no matching row)");

    var dapperSingle = DapperScenarios.QuerySingle(connection);
    OutputHelper.Dump("502-dapper-single", "Id=1", dapperSingle!);
    // 期望值：种子契约 Id=1 唯一行 → Alice，Email 字段照常映射
    if (dapperSingle == null || dapperSingle.Email != "alice@example.com")
        OutputHelper.Error($"502-dapper-single: Id=1 should map Email (actual: {dapperSingle?.Email ?? "null"})");

    var dapperSingleOrDef = DapperScenarios.QuerySingleOrDefault(connection);
    OutputHelper.Dump("502-dapper-singleordefault", "Id=999 → null", dapperSingleOrDef == null ? "null ✓" : "unexpected");
    // 期望值：QuerySingleOrDefault 契约——无匹配返回 null
    if (dapperSingleOrDef != null)
        OutputHelper.Error("502-dapper-singleordefault: Id=999 should return null (no matching row)");

    var dapperPaged = DapperScenarios.ToPagedList(connection);
    OutputHelper.Dump("502-dapper-paged", "Page 1, Size 2", dapperPaged);
    // 期望值：SQL 级分页返回 PagedList<TDto>——种子 3 行按页大小 2 切分（Page=1 两项且有下一页）
    if (dapperPaged.Page != 1 || dapperPaged.PageSize != 2 || dapperPaged.TotalCount != 3
        || dapperPaged.Items.Count != 2 || !dapperPaged.HasNext)
        OutputHelper.Error($"502-dapper-paged: page=1 size=2 over 3 rows should yield Page=1/PageSize=2/TotalCount=3/Items=2/HasNext (actual: Page={dapperPaged.Page}, Size={dapperPaged.PageSize}, Total={dapperPaged.TotalCount}, Items={dapperPaged.Items.Count}, HasNext={dapperPaged.HasNext})");

    var dapperDict = DapperScenarios.ToDictionary(connection);
    OutputHelper.Dump("502-dapper-dict", $"Keys: {string.Join(",", dapperDict.Keys)}", dapperDict);
    // 期望值：ToDictionary(keySelector) 语义——种子键 1/2/3，dict[2] 为 Bob
    if (dapperDict.Count != 3 || !dapperDict.ContainsKey(2) || dapperDict[2].Name != "Bob")
        OutputHelper.Error($"502-dapper-dict: keys should be {{1,2,3}} with dict[2].Name=Bob (actual: {dapperDict.Count} entries, dict[2]={dapperDict.GetValueOrDefault(2)?.Name ?? "missing"})");

    var dapperLookup = DapperScenarios.ToLookup(connection);
    OutputHelper.Dump("502-dapper-lookup", $"Groups: {dapperLookup.Count}", "");
    // 期望值：ILookup 分组语义——按种子 Age/10 分为组 2（1 行）与组 3（2 行）
    if (dapperLookup.Count != 2 || dapperLookup[2].Count() != 1 || dapperLookup[3].Count() != 2)
        OutputHelper.Error($"502-dapper-lookup: Age/10 should yield 2 groups (group 2: 1 row, group 3: 2 rows) (actual: {dapperLookup.Count} groups)");

    var dapperChunk = DapperScenarios.ToChunkDto(connection);
    OutputHelper.Dump("502-dapper-chunk", $"Total: {dapperChunk.Count}", dapperChunk);
    // 期望值：分批回调语义——读取全程行数守恒（种子 3 行）
    if (dapperChunk.Count != 3)
        OutputHelper.Error($"502-dapper-chunk: batched reads should conserve all 3 rows (actual: {dapperChunk.Count})");

    var dapperTree = DapperScenarios.ToTreeList(connection);
    OutputHelper.Dump("502-dapper-tree", $"Root children: {dapperTree[0].Children.Count}", dapperTree);
    // 期望值：树形投影语义——种子 Categories 树 Root→Child1/Child2→Grandchild 层级保持
    if (dapperTree.Count != 1 || dapperTree[0].Children.Count != 2
        || dapperTree[0].Children[0].Children.Count != 1)
        OutputHelper.Error($"502-dapper-tree: tree should be Root→2 children with Child1→1 grandchild (actual: roots={dapperTree.Count}, root children={dapperTree[0].Children.Count})");

    var multiResult = DapperScenarios.QueryMultiple(connection);
    OutputHelper.Dump("502-dapper-multi", $"Set1={multiResult.First.Count}, Set2={multiResult.Second.Count}", "");
    // 期望值：种子契约 Age>20 → 3 行 / Age<=25 → 1 行（双结果集各自条数）
    if (multiResult.First.Count != 3 || multiResult.Second.Count != 1)
        OutputHelper.Error($"502-dapper-multi: two result sets should be 3 rows (Age>20) and 1 row (Age<=25) (actual: {multiResult.First.Count}/{multiResult.Second.Count})");

    // 补演示：QueryMultiple 三泛型重载——期望值来源：多结果集选择器契约（主结果集用
    // TSource=UserEntity、次结果集按 TDto [MapFrom] 推断（UserDto→UserEntity、CategoryDto→CategoryEntity）+
    // 种子契约 Age>20=3 行、Age<=25 仅 Alice=25、Categories 4 行平铺（第三结果集无树组装，Children 空））
    var multi3 = DapperScenarios.QueryMultiple3(connection);
    OutputHelper.Dump("502-dapper-multi3", $"Set1={multi3.Set1.Count}, Set2={multi3.Set2.Count}, Set3={multi3.Set3.Count}", "");
    if (multi3.Set1.Count != 3 || multi3.Set2.Count != 1 || multi3.Set2[0].Name != "Alice"
        || multi3.Set3.Count != 4 || !multi3.Set3.Any(c => c.NodeId == 4 && c.Name == "Grandchild"))
        OutputHelper.Error($"502-dapper-multi3: three result sets should be 3 / 1(Alice) / 4(flat with Grandchild) (actual: {multi3.Set1.Count}/{multi3.Set2.Count}/{multi3.Set3.Count})");

    // 期望值：ISqlDialect 契约——SQLite 为 LIMIT/OFFSET 方言，CountSql 包裹子查询 COUNT
    // （ParameterPrefix/PageSql 形态按方言接口约定）
    var sqliteDialect = new SqliteDialect();
    var dialectCount = sqliteDialect.CountSql("SELECT * FROM Users");
    var dialectPage = sqliteDialect.PageSql("SELECT * FROM Users", "@offset", "@pageSize");
    if (!dialectCount.Contains("COUNT") || !dialectPage.Contains("LIMIT") || !dialectPage.Contains("OFFSET"))
        OutputHelper.Error($"502-dapper-dialect: SqliteDialect should wrap COUNT and append LIMIT/OFFSET (count={dialectCount}, page={dialectPage})");
    }
}

/// <summary>
/// 树形结构实体 — Dapper ToTreeList 演示。
/// </summary>
public class CategoryEntity
{
    /// <summary>
    /// 节点 ID。
    /// </summary>
    [System.ComponentModel.DataAnnotations.Key]  // EFCore DbSet 挂载需要主键（NodeId 不满足命名约定）；Dapper 路径无感
    public int NodeId { get; set; }

    /// <summary>
    /// 父节点 ID。
    /// </summary>
    public int? ParentNodeId { get; set; }

    /// <summary>
    /// 分类名。
    /// </summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// 树形结构 DTO。
/// </summary>
[MapFrom(typeof(CategoryEntity))] // EFCore ToTreeList 投影路径要求映射锚；Dapper SQL 原生路径无感
[MapTree("NodeId", "ParentNodeId")] // Dapper ToTreeList 运行时消费载体（.globalconfig 兜底）
public class CategoryDto
{
    /// <summary>
    /// 节点 ID。
    /// </summary>
    public int NodeId { get; set; }

    /// <summary>
    /// 父节点 ID。
    /// </summary>
    public int? ParentNodeId { get; set; }

    /// <summary>
    /// 分类名。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 子节点集合。
    /// </summary>
    public List<CategoryDto> Children { get; set; } = new();
}

/// <summary>
/// 自定义 SQLite 方言 — 记录分页 SQL 日志。
/// </summary>
public class LoggingDialect : ISqlDialect
{
    /// <summary>
    /// 内部 SQLite 方言实现（委托调用）
    /// </summary>
    private readonly SqliteDialect _inner = new();

    /// <inheritdoc />
    public string CountSql(string originalSql) => _inner.CountSql(originalSql);

    /// <inheritdoc />
    public string PageSql(string originalSql, string offsetParam, string pageSizeParam)
        => _inner.PageSql(originalSql, offsetParam, pageSizeParam);

}
