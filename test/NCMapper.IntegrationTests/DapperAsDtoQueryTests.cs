using Dapper;
using Microsoft.Data.Sqlite;
using NCMapper.Attributes;
using NCMapper.Dapper;
using System.Reflection;
using Xunit;
using System.Diagnostics.CodeAnalysis;

namespace NCMapper.IntegrationTests;

/// <summary>
/// Dapper AsDto 查询类方法集成测试 — SQLite 内存数据库
/// </summary>
[Collection("DapperGlobalState")]
public class DapperAsDtoQueryTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DapperAsDtoQueryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _connection.Execute("""
            CREATE TABLE TestUser (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Age INTEGER NOT NULL,
                Email TEXT NOT NULL,
                Phone TEXT NOT NULL
            )
            """);
        _connection.Execute("""
            INSERT INTO TestUser (Id, Name, Age, Email, Phone) VALUES
            (1, 'Alice',   25, 'alice@test.com',   '13812345678'),
            (2, 'Bob',     30, 'bob@test.com',     '13987654321'),
            (3, 'Charlie', 35, 'charlie@test.com', '15011112222'),
            (4, 'Diana',   28, 'diana@test.com',   '18633334444'),
            (5, 'Eve',     32, 'eve@test.com',     '17755556666')
            """);
    }

    public void Dispose() => _connection.Dispose();

    // ===== Query =====

    [Fact]
    public void Query_ReturnsAllMapped()
    {
        var result = _connection.AsDto<TestUser>().Query<TestUserDto>("SELECT * FROM TestUser ORDER BY Id").ToList();
        Assert.Equal(5, result.Count);
        Assert.Equal("Alice", result[0].Name);
        Assert.Equal(25, result[0].Age);
    }

    [Fact]
    public void Query_WithParam_FiltersCorrectly()
    {
        var result = _connection.AsDto<TestUser>().Query<TestUserDto>(
            "SELECT * FROM TestUser WHERE Age > @age", new { age = 30 }).ToList();
        Assert.Equal(2, result.Count);
        Assert.All(result, u => Assert.True(u.Age > 30));
    }

    [Fact]
    public void Query_EmptyResult_ReturnsEmpty()
    {
        var result = _connection.AsDto<TestUser>().Query<TestUserDto>(
            "SELECT * FROM TestUser WHERE Age > 100").ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void Query_MaskApplied_PhoneMasked()
    {
        var result = _connection.AsDto<TestUser>().Query<TestUserMaskDto>(
            "SELECT * FROM TestUser WHERE Id = 1").First();
        Assert.Equal("138****5678", result.Phone);
    }

    // ===== QueryFirst =====

    [Fact]
    public void QueryFirst_ReturnsSingle()
    {
        var result = _connection.AsDto<TestUser>().QueryFirst<TestUserDto>(
            "SELECT * FROM TestUser WHERE Id = @id", new { id = 2 });
        Assert.Equal("Bob", result.Name);
    }

    [Fact]
    public void QueryFirst_NoResult_Throws()
    {
        Assert.ThrowsAny<Exception>(() =>
            _connection.AsDto<TestUser>().QueryFirst<TestUserDto>(
                "SELECT * FROM TestUser WHERE Id = 999"));
    }

    // ===== QueryFirstOrDefault =====

    [Fact]
    public void QueryFirstOrDefault_Found_Returns()
    {
        var result = _connection.AsDto<TestUser>().QueryFirstOrDefault<TestUserDto>(
            "SELECT * FROM TestUser WHERE Id = 3");
        Assert.NotNull(result);
        Assert.Equal("Charlie", result!.Name);
    }

    [Fact]
    public void QueryFirstOrDefault_NotFound_ReturnsNull()
    {
        var result = _connection.AsDto<TestUser>().QueryFirstOrDefault<TestUserDto>(
            "SELECT * FROM TestUser WHERE Id = 999");
        Assert.Null(result);
    }

    // ===== QuerySingle =====

    [Fact]
    public void QuerySingle_ExactlyOne_Returns()
    {
        var result = _connection.AsDto<TestUser>().QuerySingle<TestUserDto>(
            "SELECT * FROM TestUser WHERE Id = 4");
        Assert.Equal("Diana", result.Name);
    }

    [Fact]
    public void QuerySingle_Multiple_Throws()
    {
        Assert.ThrowsAny<Exception>(() =>
            _connection.AsDto<TestUser>().QuerySingle<TestUserDto>(
                "SELECT * FROM TestUser WHERE Age > 20"));
    }

    // ===== QuerySingleOrDefault =====

    [Fact]
    public void QuerySingleOrDefault_Found_Returns()
    {
        var result = _connection.AsDto<TestUser>().QuerySingleOrDefault<TestUserDto>(
            "SELECT * FROM TestUser WHERE Id = 5");
        Assert.NotNull(result);
        Assert.Equal("Eve", result!.Name);
    }

    [Fact]
    public void QuerySingleOrDefault_NotFound_ReturnsNull()
    {
        var result = _connection.AsDto<TestUser>().QuerySingleOrDefault<TestUserDto>(
            "SELECT * FROM TestUser WHERE Id = 999");
        Assert.Null(result);
    }

    // ===== Async variants =====

    [Fact]
    public async Task QueryAsync_ReturnsMapped()
    {
        var result = (await _connection.AsDto<TestUser>().QueryAsync<TestUserDto>(
            "SELECT * FROM TestUser ORDER BY Id", cancellationToken: TestContext.Current.CancellationToken)).ToList();
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task QueryFirstAsync_Returns()
    {
        var result = await _connection.AsDto<TestUser>().QueryFirstAsync<TestUserDto>(
            "SELECT * FROM TestUser WHERE Id = 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Alice", result.Name);
    }

    [Fact]
    public async Task QueryFirstOrDefaultAsync_NotFound_Null()
    {
        var result = await _connection.AsDto<TestUser>().QueryFirstOrDefaultAsync<TestUserDto>(
            "SELECT * FROM TestUser WHERE Id = 999", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task QuerySingleAsync_Returns()
    {
        var result = await _connection.AsDto<TestUser>().QuerySingleAsync<TestUserDto>(
            "SELECT * FROM TestUser WHERE Id = 2", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Bob", result.Name);
    }

    [Fact]
    public async Task QuerySingleOrDefaultAsync_NotFound_Null()
    {
        var result = await _connection.AsDto<TestUser>().QuerySingleOrDefaultAsync<TestUserDto>(
            "SELECT * FROM TestUser WHERE Id = 999", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    // ===== [MapFrom] 缺失 =====

    [Fact]
    public void Query_NoMapFrom_MapsSameType()
    {
        // 泛型模型下 AsDto<TSource> 显式指定源类型，不需要 [MapFrom] 推断 TSource
        // NoMapFromDto 作为 TSource 可直接映射为自身
        var result = _connection.AsDto<NoMapFromDto>().Query<NoMapFromDto>("SELECT Id, Name FROM TestUser ORDER BY Id").ToList();
        Assert.Equal(5, result.Count);
        Assert.Equal("Alice", result[0].Name);
    }

    // ===== 方言检测 =====

    [Fact]
    public void AsDto_SqliteConnection_UsesSqliteDialect()
    {
        var selector = _connection.AsDto<TestUser>();
        // 修复原仅 Assert.NotNull——方言实际类型从未被验证
        var dialect = (NCMapper.Dapper.ISqlDialect?)typeof(DapperDtoSelector<TestUser>)
            .GetField("_dialect", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(selector);
        Assert.IsType<SqliteDialect>(dialect);
    }

    [Fact]
    public void AsDto_ExplicitDialect_Overrides()
    {
        var selector = _connection.AsDto<TestUser>(new SqlServerDialect());
        // 修复显式方言应覆盖自动探测
        var dialect = (NCMapper.Dapper.ISqlDialect?)typeof(DapperDtoSelector<TestUser>)
            .GetField("_dialect", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(selector);
        Assert.IsType<SqlServerDialect>(dialect);
    }

    // ===== 解包探测 =====

    /// <summary>
    /// 包装连接（暴露 InnerConnection）应解包探测内层类型名
    /// 内层为真实 Microsoft.Data.Sqlite.SqliteConnection → 解析为 SqliteDialect。
    /// 修复前：包装连接类型名（FakeWrappedConnection）失配 → 静默回退 MySqlDialect。
    /// </summary>
    [Fact]
    public void Resolve_WrappedConnection_UnwrapsInnerTypeName()
    {
        var wrapper = new FakeWrappedConnection(_connection);
        var dialect = SqlDialectResolver.Resolve(wrapper);
        Assert.IsType<SqliteDialect>(dialect);
    }

    /// <summary>
    /// 未包装连接行为不变（直接按类型名解析）
    /// </summary>
    [Fact]
    public void Resolve_UnwrappedConnection_BehavesAsBefore()
    {
        var dialect = SqlDialectResolver.Resolve(_connection);
        Assert.IsType<SqliteDialect>(dialect);
    }

    // ===== 参数前缀 =====

    /// <summary>
    /// Oracle/达梦方言绑定变量前缀为 ":"——分页占位符拼接后 SQL 含冒号前缀
    /// （修复前：DapperDtoSelector 硬编码 "@" 前缀 → Oracle/达梦分页 SQL 绑定失败 ORA-01036 类）
    /// </summary>
    [Fact]
    public void OracleDialect_ParameterPrefix_IsColon()
    {
        var dialect = new OracleDialect();
        Assert.Equal(":", dialect.ParameterPrefix);
        var pageSql = dialect.PageSql("SELECT * FROM T", ":__nc_offset", ":__nc_pageSize");
        Assert.Contains("OFFSET :__nc_offset ROWS FETCH NEXT :__nc_pageSize ROWS ONLY", pageSql);
    }

    /// <summary>
    /// 其余方言保持 "@" 前缀（行为零变化）
    /// </summary>
    [Fact]
    public void OtherDialects_ParameterPrefix_IsAt()
    {
        // 接口默认成员需经接口引用访问（C# 默认接口成员不暴露给实现类实例）
        Assert.Equal("@", ((ISqlDialect)new SqlServerDialect()).ParameterPrefix);
        Assert.Equal("@", ((ISqlDialect)new MySqlDialect()).ParameterPrefix);
        Assert.Equal("@", ((ISqlDialect)new PostgreSqlDialect()).ParameterPrefix);
        Assert.Equal("@", ((ISqlDialect)new SqliteDialect()).ParameterPrefix);
        Assert.Equal("@", ((ISqlDialect)new Db2Dialect()).ParameterPrefix);
    }

    // ===== FindLastOrderBy/StripOrderBy =====
    // 说明：FindLastOrderBy/StripOrderBy 为 internal（Dapper 项目无 InternalsVisibleTo），
    // 经 public CountSql 间接断言（SQL Server CountSql 总走 StripOrderBy 路径）。

    /// <summary>
    /// 单引号字面量内 "(ORDER BY)" 不参与匹配——真实 ORDER BY 仍被剥离
    /// （修复前：字面量内括号污染深度计数、字面量内 ORDER BY 被误判为剥离点）
    /// </summary>
    [Fact]
    public void CountSql_StringLiteralContainingOrderBy_NotMisparsed()
    {
        var d = new SqlServerDialect();
        var countSql = d.CountSql("SELECT * FROM T WHERE Name='(ORDER BY)' ORDER BY Id");
        Assert.Contains("'(ORDER BY)'", countSql);       // 字面量保留
        Assert.DoesNotContain("ORDER BY Id", countSql);  // 真实 ORDER BY 剥离
    }

    /// <summary>
    /// SQL 转义引号 '' 内的文本不被误判为字符串边界
    /// </summary>
    [Fact]
    public void CountSql_StringLiteralEscapedQuote_Handled()
    {
        var d = new SqlServerDialect();
        var countSql = d.CountSql("SELECT * FROM T WHERE Name='it''s (ORDER BY)' ORDER BY Id");
        Assert.Contains("(ORDER BY)", countSql);
        Assert.DoesNotContain("ORDER BY Id", countSql);
    }

    /// <summary>
    /// 结尾分号被剥离——CountSql 包裹后无非法尾分号
    /// （修复前：尾分号残留 → SELECT COUNT(*) FROM (...;) 非法 SQL）
    /// </summary>
    [Fact]
    public void CountSql_TrailingSemicolon_Stripped()
    {
        var d = new SqlServerDialect();
        var countSql = d.CountSql("SELECT * FROM T ORDER BY Id;");
        Assert.DoesNotContain(";", countSql);
        Assert.DoesNotContain("ORDER BY", countSql);
    }

    // ===== ToDictionary 重复键 =====

    /// <summary>
    /// 重复键抛 ArgumentException（与 EFCore/FreeSql 原生 ToDictionary 语义对齐）
    /// 修复前 `result[key] = dtos[i]` 静默覆盖，掩盖数据质量问题。
    /// 数据：TestUser 5 行，keySelector = Id % 3 → 键 1/2/0/1/2 必然重复。
    /// </summary>
    [Fact]
    public void ToDictionary_DuplicateKey_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            _connection.AsDto<TestUser>().ToDictionary<long, TestUserDto>(
                "SELECT Id, Name FROM TestUser ORDER BY Id", null, u => u.Id % 3));

        // .NET 原生消息含冲突键描述（与 EFCore/FreeSql 走同一 Dictionary.Add 路径）
        Assert.Contains("key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 无重复键场景行为不变（正常返回字典）
    /// </summary>
    [Fact]
    public void ToDictionary_UniqueKey_Works()
    {
        var dict = _connection.AsDto<TestUser>().ToDictionary<long, TestUserDto>(
            "SELECT Id, Name FROM TestUser ORDER BY Id", null, u => u.Id);

        Assert.Equal(5, dict.Count);
        Assert.Equal("Alice", dict[1].Name);
    }

    /// <summary>
    /// 包装连接 Fake——暴露 InnerConnection 属性（MiniProfiler ProfiledDbConnection 模式）
    /// </summary>
    private sealed class FakeWrappedConnection : System.Data.IDbConnection
    {
        public FakeWrappedConnection(System.Data.IDbConnection inner) => InnerConnection = inner;

        public System.Data.IDbConnection InnerConnection { get; }

        [AllowNull]
        public string ConnectionString { get => InnerConnection.ConnectionString; set => InnerConnection.ConnectionString = value; }
        public int ConnectionTimeout => InnerConnection.ConnectionTimeout;
        public string Database => InnerConnection.Database;
        public System.Data.ConnectionState State => InnerConnection.State;
        public System.Data.IDbTransaction BeginTransaction() => InnerConnection.BeginTransaction();
        public System.Data.IDbTransaction BeginTransaction(System.Data.IsolationLevel il) => InnerConnection.BeginTransaction(il);
        public void ChangeDatabase(string databaseName) => InnerConnection.ChangeDatabase(databaseName);
        public void Close() => InnerConnection.Close();
        public System.Data.IDbCommand CreateCommand() => InnerConnection.CreateCommand();
        public void Open() => InnerConnection.Open();
        public void Dispose() => InnerConnection.Dispose();
    }

    // ===== 补充边界用例 =====

    [Fact]
    public async Task QueryAsync_WithParam_Filters()
    {
        var result = (await _connection.AsDto<TestUser>().QueryAsync<TestUserDto>(
            "SELECT * FROM TestUser WHERE Age >= @age", new { age = 32 }, cancellationToken: TestContext.Current.CancellationToken)).ToList();
        Assert.Equal(2, result.Count);
        Assert.All(result, u => Assert.True(u.Age >= 32));
    }

    [Fact]
    public async Task QueryAsync_EmptyResult_ReturnsEmpty()
    {
        var result = (await _connection.AsDto<TestUser>().QueryAsync<TestUserDto>(
            "SELECT * FROM TestUser WHERE Age > 200", cancellationToken: TestContext.Current.CancellationToken)).ToList();
        Assert.Empty(result);
    }

    [Fact]
    public async Task QueryFirstAsync_NoResult_Throws()
    {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _connection.AsDto<TestUser>().QueryFirstAsync<TestUserDto>(
                "SELECT * FROM TestUser WHERE Id = 999", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QuerySingleAsync_Multiple_Throws()
    {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _connection.AsDto<TestUser>().QuerySingleAsync<TestUserDto>(
                "SELECT * FROM TestUser WHERE Age > 20", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Query_MaskEmail_Applied()
    {
        var result = _connection.AsDto<TestUser>().Query<TestUserEmailMaskDto>(
            "SELECT * FROM TestUser WHERE Id = 1").First();
        Assert.DoesNotContain("alice@test.com", result.Email);
        Assert.Contains("***", result.Email);
    }

    [Fact]
    public void Query_ConvertUsing_Applied()
    {
        var result = _connection.AsDto<TestUser>().Query<TestUserConvertDto>(
            "SELECT * FROM TestUser WHERE Id = 1").First();
        Assert.Equal("Alice(25)", result.Summary);
    }

    [Fact]
    public void AsDto_SelectorReusable_MultipleQueries()
    {
        var selector = _connection.AsDto<TestUser>();
        var r1 = selector.Query<TestUserDto>("SELECT * FROM TestUser WHERE Id = 1").First();
        var r2 = selector.Query<TestUserDto>("SELECT * FROM TestUser WHERE Id = 2").First();
        Assert.Equal("Alice", r1.Name);
        Assert.Equal("Bob", r2.Name);
    }

    [Fact]
    public void Query_WithParam_ObjectSyntax_Works()
    {
        var result = _connection.AsDto<TestUser>().Query<TestUserDto>(
            "SELECT * FROM TestUser WHERE Name = @name", new { name = "Eve" }).ToList();
        Assert.Single(result);
        Assert.Equal(32, result[0].Age);
    }

    [Fact]
    public async Task QuerySingleOrDefaultAsync_Found_Returns()
    {
        var result = await _connection.AsDto<TestUser>().QuerySingleOrDefaultAsync<TestUserDto>(
            "SELECT * FROM TestUser WHERE Id = 3", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("Charlie", result!.Name);
    }

    [Fact]
    public async Task QueryFirstOrDefaultAsync_Found_Returns()
    {
        var result = await _connection.AsDto<TestUser>().QueryFirstOrDefaultAsync<TestUserDto>(
            "SELECT * FROM TestUser WHERE Id = 4", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("Diana", result!.Name);
    }

    // ===== Mask+ConvertUsing 组合回归 =====

    [Fact]
    public void Query_MaskAndConvertUsing_SameProperty_ConvertUsingWins()
    {
        // [Mask] 和 [Convert] 同时标注同一属性时，ConvertUsing 优先生效
        var result = _connection.AsDto<TestUser>().Query<TestUserMaskConvertDto>(
            "SELECT * FROM TestUser WHERE Id = 1").First();
        // ConvertUsing 拼接 "Name(Age)"，不为脱敏格式
        Assert.Equal("Alice(25)", result.Summary);
    }

    // ===== ToChunkDto 异常传播 =====

    [Fact]
    public void ToChunkDto_DoneThrowsException_Propagates()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _connection.AsDto<TestUser>().ToChunkDto<TestUserDto>(
                "SELECT * FROM TestUser ORDER BY Id", null, size: 2,
                done: _ => throw new InvalidOperationException("done 回调异常")));
        Assert.Equal("done 回调异常", ex.Message);
    }

    [Fact]
    public async Task ToChunkDtoAsync_DoneThrowsException_Propagates()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _connection.AsDto<TestUser>().ToChunkDtoAsync<TestUserDto>(
                "SELECT * FROM TestUser ORDER BY Id", null, size: 2,
                done: _ => throw new InvalidOperationException("done 异步回调异常"), cancellationToken: TestContext.Current.CancellationToken));
        // 实际抛 AggregateException→内部异常被 Assert.ThrowsAsync 解包
    }

    // ===== 测试模型 =====

    public class TestUser
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
    }

    [MapFrom(typeof(TestUser))]
    public class TestUserDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public string Email { get; set; } = "";
    }

    [MapFrom(typeof(TestUser))]
    public class TestUserMaskDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        [Mask(typeof(MaskPhone))]
        public string Phone { get; set; } = "";
    }

    public class NoMapFromDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    [MapFrom(typeof(TestUser))]
    public class TestUserEmailMaskDto
    {
        public int Id { get; set; }
        [Mask(typeof(MaskEmail))]
        public string Email { get; set; } = "";
    }

    [MapFrom(typeof(TestUser))]
    public class TestUserConvertDto
    {
        public int Id { get; set; }
        [Convert(typeof(SummaryConverter), nameof(SummaryConverter.Convert))]
        public string Summary { get; set; } = "";
    }

    [MapFrom(typeof(TestUser))]
    public class TestUserMaskConvertDto
    {
        public int Id { get; set; }
        [Mask(typeof(MaskPhone))]
        [Convert(typeof(SummaryConverter), nameof(SummaryConverter.Convert))]
        public string Summary { get; set; } = "";
    }

    /// <summary>测试用转换器（静态方法引用形态，T2 模板 M(TSource)）— 拼接 Name(Age)</summary>
    public static class SummaryConverter
    {
        /// <summary>从源实体取 Name 与 Age 拼接</summary>
        public static string Convert(TestUser source) => $"{source.Name}({source.Age})";
    }
}
