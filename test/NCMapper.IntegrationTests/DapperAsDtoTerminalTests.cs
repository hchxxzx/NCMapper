using Dapper;
using Microsoft.Data.Sqlite;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Dapper;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// Dapper AsDto 终端操作类方法集成测试 — ToPagedList/ToDictionary/ToLookup/ToChunkDto/ToTreeList + 方言
/// </summary>
[Collection("DapperGlobalState")]
public class DapperAsDtoTerminalTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DapperAsDtoTerminalTests()
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

        _connection.Execute("""
            CREATE TABLE TreeNode (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                ParentId INTEGER
            )
            """);
        _connection.Execute("""
            INSERT INTO TreeNode (Id, Name, ParentId) VALUES
            (1, 'Root',    NULL),
            (2, 'Child1',  1),
            (3, 'Child2',  1),
            (4, 'GrandChild', 2)
            """);
    }

    public void Dispose() => _connection.Dispose();

    // ===== ToPagedList =====

    [Fact]
    public void ToPagedList_FirstPage_CorrectItems()
    {
        var page = _connection.AsDto<TestUser>().ToPagedList<TestUserDto>(
            "SELECT * FROM TestUser ORDER BY Id", null, page: 1, pageSize: 2);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("Alice", page.Items[0].Name);
        Assert.Equal("Bob", page.Items[1].Name);
        Assert.Equal(1, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasNext);
        Assert.False(page.HasPrev);
    }

    [Fact]
    public void ToPagedList_LastPage_PartialItems()
    {
        var page = _connection.AsDto<TestUser>().ToPagedList<TestUserDto>(
            "SELECT * FROM TestUser ORDER BY Id", null, page: 3, pageSize: 2);

        Assert.Single(page.Items);
        Assert.Equal("Eve", page.Items[0].Name);
        Assert.False(page.HasNext);
        Assert.True(page.HasPrev);
    }

    [Fact]
    public void ToPagedList_WithParam_FiltersBeforePaging()
    {
        var page = _connection.AsDto<TestUser>().ToPagedList<TestUserDto>(
            "SELECT * FROM TestUser WHERE Age > 28 ORDER BY Id", null, page: 1, pageSize: 10);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public async Task ToPagedListAsync_Works()
    {
        var page = await _connection.AsDto<TestUser>().ToPagedListAsync<TestUserDto>(
            "SELECT * FROM TestUser ORDER BY Id", null, page: 2, pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("Charlie", page.Items[0].Name);
    }

    // ===== ToDictionary =====

    [Fact]
    public void ToDictionary_ByEmail_CorrectKeys()
    {
        var dict = _connection.AsDto<TestUser>().ToDictionary<string, TestUserDto>(
            "SELECT * FROM TestUser WHERE Age <= 30", null, u => u.Email);

        Assert.Equal(3, dict.Count);
        Assert.True(dict.ContainsKey("alice@test.com"));
        Assert.Equal("Alice", dict["alice@test.com"].Name);
    }

    [Fact]
    public async Task ToDictionaryAsync_Works()
    {
        var dict = await _connection.AsDto<TestUser>().ToDictionaryAsync<int, TestUserDto>(
            "SELECT * FROM TestUser", null, u => u.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, dict.Count);
        Assert.Equal("Bob", dict[2].Name);
    }

    // ===== ToLookup =====

    [Fact]
    public void ToLookup_ByAgeGroup_CorrectGroups()
    {
        var lookup = _connection.AsDto<TestUser>().ToLookup<int, TestUserDto>(
            "SELECT * FROM TestUser", null, u => u.Age / 10);

        Assert.Equal(2, lookup.Count); // 20s(25,28), 30s(30,35,32)
        Assert.Equal(2, lookup[2].Count()); // Alice(25), Diana(28)
        Assert.Equal(3, lookup[3].Count()); // Bob(30), Charlie(35), Eve(32)
    }

    [Fact]
    public async Task ToLookupAsync_Works()
    {
        var lookup = await _connection.AsDto<TestUser>().ToLookupAsync<int, TestUserDto>(
            "SELECT * FROM TestUser", null, u => u.Age / 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(lookup.Contains(2));
        Assert.True(lookup.Contains(3));
    }

    // ===== ToChunkDto =====

    [Fact]
    public void ToChunkDto_ChunksCorrectly()
    {
        var chunks = new List<List<TestUserDto>>();
        _connection.AsDto<TestUser>().ToChunkDto<TestUserDto>(
            "SELECT * FROM TestUser ORDER BY Id", null, size: 2, done: chunk => chunks.Add(chunk));

        Assert.Equal(3, chunks.Count); // 2+2+1
        Assert.Equal(2, chunks[0].Count);
        Assert.Equal(2, chunks[1].Count);
        Assert.Single(chunks[2]);
    }

    [Fact]
    public void ToChunkDto_EmptyResult_NoCallback()
    {
        var called = false;
        _connection.AsDto<TestUser>().ToChunkDto<TestUserDto>(
            "SELECT * FROM TestUser WHERE Age > 100", null, size: 2, done: _ => called = true);

        Assert.False(called);
    }

    [Fact]
    public async Task ToChunkDtoAsync_Works()
    {
        var total = 0;
        await _connection.AsDto<TestUser>().ToChunkDtoAsync<TestUserDto>(
            "SELECT * FROM TestUser ORDER BY Id", null, size: 3,
            done: chunk => { total += chunk.Count; return Task.CompletedTask; }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, total);
    }

    // ===== ToTreeList =====

    [Fact]
    public void ToTreeList_BuildsCorrectTree()
    {
        var roots = _connection.AsDto<TreeNode>().ToTreeList<TreeNodeDto>(
            "SELECT * FROM TreeNode ORDER BY Id");

        Assert.Single(roots);
        Assert.Equal("Root", roots[0].Name);
        Assert.Equal(2, roots[0].Children.Count);
        Assert.Equal("Child1", roots[0].Children[0].Name);
        Assert.Single(roots[0].Children[0].Children);
        Assert.Equal("GrandChild", roots[0].Children[0].Children[0].Name);
    }

    [Fact]
    public async Task ToTreeListAsync_Works()
    {
        var roots = await _connection.AsDto<TreeNode>().ToTreeListAsync<TreeNodeDto>(
            "SELECT * FROM TreeNode ORDER BY Id", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(roots);
        Assert.Equal(2, roots[0].Children.Count);
    }

    // ===== Trace 前缀统一（[Dapper AsDto] 前缀覆盖查询/建树/分页路径）=====

    [Fact]
    public void AsDto_Trace_PrefixCoversQueryTreePagingPaths()
    {
        var sw = new System.IO.StringWriter();
        var originalEnabled = MapTracer.GloballyEnabled;
        var originalOutput = MapTracer.GlobalOutput;
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            // 分页路径（内部含批量映射 Trace）+ 建树路径（内部含批量映射 Trace）
            _connection.AsDto<TestUser>().ToPagedList<TestUserDto>(
                "SELECT * FROM TestUser ORDER BY Id", null, page: 1, pageSize: 2);
            _connection.AsDto<TreeNode>().ToTreeList<TreeNodeDto>(
                "SELECT * FROM TreeNode ORDER BY Id");
        }
        finally
        {
            MapTracer.GloballyEnabled = originalEnabled;
            MapTracer.GlobalOutput = originalOutput;
        }

        var trace = sw.ToString();
        // 分页路径：前缀 + 分页明细
        Assert.Contains("[Dapper AsDto] TestUser → TestUserDto (分页 page=1, size=2, total=5)", trace);
        // 建树路径：前缀 + 平铺→根数量
        Assert.Contains("[Dapper AsDto] TreeNode → TreeNodeDto (树构建 平铺 4 项 → 根 1)", trace);
        // 查询路径（批量映射）：前缀 + 批量映射
        Assert.Contains("[Dapper AsDto] TestUser → TestUserDto (批量映射)", trace);
    }

    // ===== SQL 方言单元测试 =====

    [Fact]
    public void SqlServerDialect_CountSql_StripsOrderBy()
    {
        var dialect = new SqlServerDialect();
        var sql = "SELECT * FROM Users ORDER BY Name";
        var count = dialect.CountSql(sql);
        Assert.DoesNotContain("ORDER BY", count);
        Assert.Contains("COUNT(*)", count);
    }

    [Fact]
    public void SqlServerDialect_PageSql_InsertsOrderByWhenMissing()
    {
        var dialect = new SqlServerDialect();
        var sql = "SELECT * FROM Users";
        var page = dialect.PageSql(sql, "@offset", "@size");
        Assert.Contains("ORDER BY (SELECT NULL)", page);
        Assert.Contains("OFFSET @offset ROWS", page);
        Assert.Contains("FETCH NEXT @size ROWS ONLY", page);
    }

    [Fact]
    public void SqlServerDialect_PageSql_KeepsExistingOrderBy()
    {
        var dialect = new SqlServerDialect();
        var sql = "SELECT * FROM Users ORDER BY Name";
        var page = dialect.PageSql(sql, "@offset", "@size");
        Assert.DoesNotContain("SELECT NULL", page);
        Assert.Contains("ORDER BY Name", page);
    }

    [Fact]
    public void MySqlDialect_PageSql_LimitOffset()
    {
        var dialect = new MySqlDialect();
        var page = dialect.PageSql("SELECT * FROM Users", "@offset", "@size");
        Assert.Contains("LIMIT @size OFFSET @offset", page);
    }

    [Fact]
    public void PostgreSqlDialect_PageSql_LimitOffset()
    {
        var dialect = new PostgreSqlDialect();
        var page = dialect.PageSql("SELECT * FROM Users", "@offset", "@size");
        Assert.Contains("LIMIT @size OFFSET @offset", page);
    }

    [Fact]
    public void SqliteDialect_PageSql_LimitOffset()
    {
        var dialect = new SqliteDialect();
        var page = dialect.PageSql("SELECT * FROM Users", "@offset", "@size");
        Assert.Contains("LIMIT @size OFFSET @offset", page);
    }

    [Fact]
    public void OracleDialect_PageSql_OffsetFetch()
    {
        var dialect = new OracleDialect();
        var page = dialect.PageSql("SELECT * FROM Users", "@offset", "@size");
        Assert.Contains("OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY", page);
    }

    [Fact]
    public void Db2Dialect_PageSql_FetchFirst()
    {
        var dialect = new Db2Dialect();
        var sql = "SELECT * FROM Users";
        var page = dialect.PageSql(sql, "@offset", "@size");
        Assert.Contains("ORDER BY (SELECT NULL)", page);
        Assert.Contains("FETCH FIRST @size ROWS ONLY", page);
    }

    [Fact]
    public void Db2Dialect_CountSql_StripsOrderBy()
    {
        var dialect = new Db2Dialect();
        var count = dialect.CountSql("SELECT * FROM Users ORDER BY Id");
        Assert.DoesNotContain("ORDER BY", count);
    }

    [Fact]
    public void SqlServerDialect_CountSql_NestedOrderBy_NotStripped()
    {
        var dialect = new SqlServerDialect();
        var sql = "SELECT * FROM (SELECT * FROM Users ORDER BY Id) sub";
        var count = dialect.CountSql(sql);
        // 括号内的 ORDER BY 不应被剥离
        Assert.Contains("ORDER BY Id", count);
    }

    // ===== 补充边界用例 =====

    [Fact]
    public void ToPagedList_PageBeyondTotal_EmptyItems()
    {
        var page = _connection.AsDto<TestUser>().ToPagedList<TestUserDto>(
            "SELECT * FROM TestUser ORDER BY Id", null, page: 10, pageSize: 2);

        Assert.Equal(5, page.TotalCount);
        Assert.Empty(page.Items);
        Assert.False(page.HasNext);
    }

    [Fact]
    public void ToPagedList_PageSizeLargerThanTotal_AllItems()
    {
        var page = _connection.AsDto<TestUser>().ToPagedList<TestUserDto>(
            "SELECT * FROM TestUser ORDER BY Id", null, page: 1, pageSize: 100);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(1, page.TotalPages);
        Assert.False(page.HasNext);
        Assert.False(page.HasPrev);
    }

    [Fact]
    public void ToPagedList_EmptyTable_ZeroCount()
    {
        var page = _connection.AsDto<TestUser>().ToPagedList<TestUserDto>(
            "SELECT * FROM TestUser WHERE Age > 200 ORDER BY Id", null, page: 1, pageSize: 10);

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public void ToPagedList_MaskApplied()
    {
        var page = _connection.AsDto<TestUser>().ToPagedList<TestUserMaskDto>(
            "SELECT * FROM TestUser ORDER BY Id", null, page: 1, pageSize: 2);

        Assert.Equal("138****5678", page.Items[0].Phone);
    }

    [Fact]
    public void ToPagedList_Page_Zero_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _connection.AsDto<TestUser>().ToPagedList<TestUserDto>(
                "SELECT * FROM TestUser ORDER BY Id", null, page: 0, pageSize: 10));
        Assert.Equal("page", ex.ParamName);
    }

    [Fact]
    public void ToPagedList_PageSize_Zero_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _connection.AsDto<TestUser>().ToPagedList<TestUserDto>(
                "SELECT * FROM TestUser ORDER BY Id", null, page: 1, pageSize: 0));
        Assert.Equal("pageSize", ex.ParamName);
    }

    [Fact]
    public async Task ToPagedListAsync_Page_Zero_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _connection.AsDto<TestUser>().ToPagedListAsync<TestUserDto>(
                "SELECT * FROM TestUser ORDER BY Id", null, page: 0, pageSize: 10, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("page", ex.ParamName);
    }

    [Fact]
    public async Task ToPagedListAsync_PageSize_Zero_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _connection.AsDto<TestUser>().ToPagedListAsync<TestUserDto>(
                "SELECT * FROM TestUser ORDER BY Id", null, page: 1, pageSize: 0, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("pageSize", ex.ParamName);
    }

    [Fact]
    public void ToDictionary_EmptyResult_ReturnsEmpty()
    {
        var dict = _connection.AsDto<TestUser>().ToDictionary<int, TestUserDto>(
            "SELECT * FROM TestUser WHERE Age > 200", null, u => u.Id);

        Assert.Empty(dict);
    }

    [Fact]
    public void ToLookup_EmptyResult_NoGroups()
    {
        var lookup = _connection.AsDto<TestUser>().ToLookup<int, TestUserDto>(
            "SELECT * FROM TestUser WHERE Age > 200", null, u => u.Age / 10);

        Assert.Equal(0, lookup.Count);
    }

    [Fact]
    public void ToChunkDto_SizeLargerThanTotal_SingleChunk()
    {
        var chunks = new List<List<TestUserDto>>();
        _connection.AsDto<TestUser>().ToChunkDto<TestUserDto>(
            "SELECT * FROM TestUser ORDER BY Id", null, size: 100, done: chunk => chunks.Add(chunk));

        Assert.Single(chunks);
        Assert.Equal(5, chunks[0].Count);
    }

    [Fact]
    public async Task ToChunkDtoAsync_EmptyResult_NoCallback()
    {
        var called = false;
        await _connection.AsDto<TestUser>().ToChunkDtoAsync<TestUserDto>(
            "SELECT * FROM TestUser WHERE Age > 200", null, size: 2,
            done: _ => { called = true; return Task.CompletedTask; }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(called);
    }

    [Fact]
    public void ToTreeList_EmptyResult_ReturnsEmpty()
    {
        var roots = _connection.AsDto<TreeNode>().ToTreeList<TreeNodeDto>(
            "SELECT * FROM TreeNode WHERE Id > 100");

        Assert.Empty(roots);
    }

    [Fact]
    public void ToTreeList_AllRoots_NoChildren()
    {
        // 查询只有根节点（ParentId IS NULL）
        var roots = _connection.AsDto<TreeNode>().ToTreeList<TreeNodeDto>(
            "SELECT * FROM TreeNode WHERE ParentId IS NULL");

        Assert.Single(roots);
        Assert.Equal("Root", roots[0].Name);
        Assert.Empty(roots[0].Children); // 子节点不在结果集中
    }

    [Fact]
    public void SqlDialectResolver_SqliteConnection_ReturnsSqlite()
    {
        var dialect = SqlDialectResolver.Resolve(_connection);
        Assert.IsType<SqliteDialect>(dialect);
    }

    [Fact]
    public void OracleDialect_CountSql_WrapsSubquery()
    {
        var dialect = new OracleDialect();
        var count = dialect.CountSql("SELECT * FROM Users ORDER BY Name DESC");
        Assert.Contains("COUNT(*)", count);
        // Oracle 用子查询包装，ORDER BY 保留在括号内（不影响 COUNT 结果）
        Assert.Contains("FROM (", count);
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

    public class TreeNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
    }

    [MapFrom(typeof(TreeNode))]
    public class TreeNodeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public List<TreeNodeDto> Children { get; set; } = new();
    }
}
