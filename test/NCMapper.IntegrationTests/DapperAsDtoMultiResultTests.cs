using Dapper;
using Microsoft.Data.Sqlite;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Dapper;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// Dapper AsDto 多结果族集成测试 — QueryMultiple 两重载、ReadByType/MapListByType 反射读集腿、
/// ResolveSourceTypeFromMapFrom（含 [MapFrom] 缺失抛分支）与空结果集边界。
/// <para>断言落在映射结果值上（异名、类型转换、跨表源类型），不只断"不抛"。</para>
/// </summary>
[Collection("DapperGlobalState")]
public class DapperAsDtoMultiResultTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DapperAsDtoMultiResultTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _connection.Execute("""
            CREATE TABLE MrUser (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Age INTEGER NOT NULL
            )
            """);
        _connection.Execute("""
            INSERT INTO MrUser (Id, Name, Age) VALUES
            (1, 'Alice', 25),
            (2, 'Bob',   30)
            """);
        _connection.Execute("""
            CREATE TABLE MrOrder (
                Id INTEGER PRIMARY KEY,
                UserId INTEGER NOT NULL,
                OrderNo TEXT NOT NULL,
                Amount INTEGER NOT NULL
            )
            """);
        _connection.Execute("""
            INSERT INTO MrOrder (Id, UserId, OrderNo, Amount) VALUES
            (10, 1, 'ORD-10', 500),
            (11, 2, 'ORD-11', 700)
            """);
    }

    public void Dispose() => _connection.Dispose();

    // ===== QueryMultiple 两结果集 =====

    [Fact]
    public void QueryMultiple_TwoResultSets_MapsBothSets()
    {
        var (users, orders) = _connection.AsDto<MrUser>().QueryMultiple<MrUserDto, MrOrderDto>(
            "SELECT * FROM MrUser ORDER BY Id; SELECT * FROM MrOrder ORDER BY Id;");

        Assert.Equal(2, users.Count);
        Assert.Equal("Alice", users[0].Name);
        Assert.Equal(25, users[0].Age);
        Assert.Equal("Bob", users[1].Name);

        // 次结果集：源类型由 [MapFrom] 反查（MrOrder ≠ Selector 的 TSource）
        Assert.Equal(2, orders.Count);
        Assert.Equal("ORD-10", orders[0].OrderNo);
        Assert.Equal(1, orders[0].UserId);
        Assert.Equal(700, orders[1].Amount);
    }

    [Fact]
    public void QueryMultiple_ThreeResultSets_MapsAllThreeSets()
    {
        var (users, orders, lite) = _connection.AsDto<MrUser>().QueryMultiple<MrUserDto, MrOrderDto, MrOrderLiteDto>(
            "SELECT * FROM MrUser ORDER BY Id; SELECT * FROM MrOrder ORDER BY Id; SELECT Id, OrderNo FROM MrOrder ORDER BY Id;");

        Assert.Equal(2, users.Count);
        Assert.Equal(2, orders.Count);
        Assert.Equal(2, lite.Count);
        Assert.Equal("ORD-11", lite[1].OrderNo);
        Assert.Equal(0, lite[1].Amount); // 第三结果集未取 Amount 列 → 保持默认值
    }

    [Fact]
    public void QueryMultiple_SecondResultSet_AppliesMapPropertyRenamingAndConversion()
    {
        var (_, renamed) = _connection.AsDto<MrUser>().QueryMultiple<MrUserDto, MrOrderRenamedDto>(
            "SELECT * FROM MrUser ORDER BY Id; SELECT * FROM MrOrder ORDER BY Id;");

        Assert.Equal(2, renamed.Count);
        // 异名：OrderNo → Number（[MapProperty]）
        Assert.Equal("ORD-10", renamed[0].Number);
        // 类型转换：源 INTEGER(int) → 目标 decimal
        Assert.Equal(500m, renamed[0].Amount);
        Assert.Equal(700m, renamed[1].Amount);
    }

    [Fact]
    public void QueryMultiple_SecondResultSet_Empty_ReturnsEmptyList()
    {
        var (users, orders) = _connection.AsDto<MrUser>().QueryMultiple<MrUserDto, MrOrderDto>(
            "SELECT * FROM MrUser ORDER BY Id; SELECT * FROM MrOrder WHERE Id > 100;");

        Assert.Equal(2, users.Count);
        Assert.Empty(orders);
    }

    [Fact]
    public void QueryMultiple_FirstResultSet_Empty_ReturnsEmptyList()
    {
        var (users, orders) = _connection.AsDto<MrUser>().QueryMultiple<MrUserDto, MrOrderDto>(
            "SELECT * FROM MrUser WHERE Id > 100; SELECT * FROM MrOrder ORDER BY Id;");

        Assert.Empty(users);
        Assert.Equal(2, orders.Count);
    }

    // ===== ResolveSourceTypeFromMapFrom 抛分支 =====

    [Fact]
    public void QueryMultiple_SecondDtoWithoutMapFrom_ThrowsMapConfigurationException()
    {
        var ex = Assert.Throws<MapConfigurationException>(() =>
            _connection.AsDto<MrUser>().QueryMultiple<MrUserDto, MrNoMapFromDto>(
                "SELECT * FROM MrUser ORDER BY Id; SELECT * FROM MrOrder ORDER BY Id;"));

        Assert.Equal(ErrorMessages.DtoMissingMapFrom(typeof(MrNoMapFromDto).FullName!), ex.Message);
    }

    [Fact]
    public void QueryMultiple_ThreeOverload_ThirdDtoWithoutMapFrom_ThrowsMapConfigurationException()
    {
        var ex = Assert.Throws<MapConfigurationException>(() =>
            _connection.AsDto<MrUser>().QueryMultiple<MrUserDto, MrOrderDto, MrNoMapFromDto>(
                "SELECT * FROM MrUser ORDER BY Id; SELECT * FROM MrOrder ORDER BY Id; SELECT * FROM MrOrder ORDER BY Id;"));

        Assert.Equal(ErrorMessages.DtoMissingMapFrom(typeof(MrNoMapFromDto).FullName!), ex.Message);
    }

    // ===== 测试模型 =====

    public class MrUser
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    public class MrOrder
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string OrderNo { get; set; } = "";
        public int Amount { get; set; }
    }

    [MapFrom(typeof(MrUser))]
    public class MrUserDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    [MapFrom(typeof(MrOrder))]
    public class MrOrderDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string OrderNo { get; set; } = "";
        public int Amount { get; set; }
    }

    [MapFrom(typeof(MrOrder))]
    public class MrOrderLiteDto
    {
        public int Id { get; set; }
        public string OrderNo { get; set; } = "";
        public int Amount { get; set; }
    }

    [MapFrom(typeof(MrOrder))]
    public class MrOrderRenamedDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(MrOrder), nameof(MrOrder.OrderNo))]
        public string Number { get; set; } = "";

        public decimal Amount { get; set; }
    }

    /// <summary>无 [MapFrom] 锚 — 次结果集源类型不可反查，走抛分支。</summary>
    public class MrNoMapFromDto
    {
        public int Id { get; set; }
        public string OrderNo { get; set; } = "";
    }
}
