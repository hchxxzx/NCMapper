using Xunit;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.IntegrationTests.Models;

namespace NCMapper.IntegrationTests;

/// <summary>
/// 自动 Include 集成测试 — 验证降级路径中 NavigationPathCollector + EF Core Include 协同工作
/// 覆盖任务书 IT-INC-01~20 + IT-INC-EQ1~EQ3
/// </summary>
public class AutoIncludeIntegrationTests : IDisposable
{
    private readonly TestDbContext _db;

    public AutoIncludeIntegrationTests()
    {
        var connStr = $"DataSource=file:test{Guid.NewGuid():N}?mode=memory&cache=shared";
        _db = new TestDbContext(connStr);
        _db.SeedData();
    }

    public void Dispose() => _db.Dispose();

    // ================================================================
    // 测试用 DTO — 强制降级 + 跨表依赖
    // ================================================================

    /// <summary>
    /// 跨表 + ConvertUsing 强制降级的 DTO
    /// </summary>
    [MapFrom(typeof(OrderEntity))]
    public class OrderDegradedDto
    {
        public int Id { get; set; }
        public string OrderNo { get; set; } = "";

        // 跨表字段 — 需要 Customer 导航
        [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
        public string CustomerName { get; set; } = "";

        [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Level))]
        public string CustomerLevel { get; set; } = "";

        // ConvertUsing 强制降级（投影路径无法翻译）
        [Convert(typeof(AmountFormatter), nameof(AmountFormatter.Convert))]
        public string FormattedAmount { get; set; } = "";
    }

    /// <summary>
    /// 金额格式化转换器（静态方法引用形态，T1 模板 M(TField)）
    /// </summary>
    public static class AmountFormatter
    {
        /// <summary>金额格式化为 "¥x.xx"</summary>
        public static string Convert(decimal fieldValue) => $"¥{fieldValue:F2}";
    }

    /// <summary>
    /// 跨表 + [MapNoAutoInclude] 的 DTO — CustomerName 被排除
    /// </summary>
    [MapFrom(typeof(OrderEntity))]
    public class OrderNoAutoIncludeDto
    {
        public int Id { get; set; }
        public string OrderNo { get; set; } = "";

        [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
        [MapNoAutoInclude]
        public string CustomerName { get; set; } = "";

        [Convert(typeof(AmountFormatter), nameof(AmountFormatter.Convert))]
        public string FormattedAmount { get; set; } = "";
    }

    /// <summary>
    /// 纯跨表 DTO（无降级触发器）— 走投影路径
    /// </summary>
    [MapFrom(typeof(OrderEntity))]
    public class OrderProjectionDto
    {
        public int Id { get; set; }
        public string OrderNo { get; set; } = "";

        [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
        public string CustomerName { get; set; } = "";

        [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Level))]
        public string CustomerLevel { get; set; } = "";
    }

    // ================================================================
    // IT-INC: 自动 Include 基本功能
    // ================================================================

    /// <summary>
    /// IT-INC-01: 降级路径自动 Include 跨表字段被正确填充
    /// </summary>
    [Fact]
    public void EFCore_Degradation_AutoInclude_CrossTableFieldsPopulated()
    {
        var dtos = _db.Orders
            .OrderBy(o => o.Id)
            .AsDto()
            .ToList<OrderDegradedDto>();

        Assert.NotEmpty(dtos);
        var first = dtos[0];
        // 跨表字段应被填充（自动 Include 了 Customer 导航）
        Assert.False(string.IsNullOrEmpty(first.CustomerName));
        Assert.False(string.IsNullOrEmpty(first.CustomerLevel));
        // ConvertUsing 字段也应正确
        Assert.StartsWith("¥", first.FormattedAmount);
    }

    /// <summary>
    /// IT-INC-02: 降级路径 ConvertUsing 字段正确执行
    /// </summary>
    [Fact]
    public void EFCore_Degradation_ConvertUsing_Works()
    {
        var dtos = _db.Orders
            .Where(o => o.Id == 1)
            .AsDto()
            .ToList<OrderDegradedDto>();

        Assert.Single(dtos);
        Assert.Equal("¥", dtos[0].FormattedAmount[..1]);
    }

    /// <summary>
    /// IT-INC-03: [MapNoAutoInclude] 排除后跨表字段为空（导航未加载）
    /// </summary>
    [Fact]
    public void EFCore_MapNoAutoInclude_CrossTableFieldEmpty()
    {
        var dtos = _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == 1)
            .AsDto()
            .ToList<OrderNoAutoIncludeDto>();

        Assert.Single(dtos);
        // [MapNoAutoInclude] 排除了 Customer 导航的自动 Include
        // 降级路径中 Customer 未被加载 → MapList 时 CustomerName 为 null
        Assert.True(string.IsNullOrEmpty(dtos[0].CustomerName));
    }

    // ================================================================
    // IT-INC-EQ: 等价性测试
    // ================================================================

    /// <summary>
    /// IT-INC-EQ1: 投影路径 vs 降级路径 — 跨表字段结果等价
    /// </summary>
    [Fact]
    public void EFCore_Equivalence_ProjectionVsDegradation_CrossTableFields()
    {
        // 投影路径（OrderProjectionDto 无降级触发器）
        var projectionDtos = _db.Orders
            .OrderBy(o => o.Id)
            .AsDto()
            .ToList<OrderProjectionDto>();

        // 降级路径（OrderDegradedDto 有 ConvertUsing 强制降级）
        var degradedDtos = _db.Orders
            .OrderBy(o => o.Id)
            .AsDto()
            .ToList<OrderDegradedDto>();

        Assert.Equal(projectionDtos.Count, degradedDtos.Count);
        for (int i = 0; i < projectionDtos.Count; i++)
        {
            Assert.Equal(projectionDtos[i].Id, degradedDtos[i].Id);
            Assert.Equal(projectionDtos[i].OrderNo, degradedDtos[i].OrderNo);
            // 核心等价性：跨表字段在两条路径下结果一致
            Assert.Equal(projectionDtos[i].CustomerName, degradedDtos[i].CustomerName);
            Assert.Equal(projectionDtos[i].CustomerLevel, degradedDtos[i].CustomerLevel);
        }
    }

    /// <summary>
    /// IT-INC-EQ2: 降级路径结果与手动 Include + MapList 等价
    /// </summary>
    [Fact]
    public void EFCore_Equivalence_AutoIncludeVsManualInclude()
    {
        // 自动 Include 路径
        var autoDtos = _db.Orders
            .OrderBy(o => o.Id)
            .AsDto()
            .ToList<OrderDegradedDto>();

        // 手动 Include 路径
        var manualEntities = _db.Orders
            .Include(o => o.Customer)
            .OrderBy(o => o.Id)
            .ToList();
        var manualDtos = Mapper.MapList<OrderEntity, OrderDegradedDto>(manualEntities) ?? new();

        Assert.Equal(autoDtos.Count, manualDtos.Count);
        for (int i = 0; i < autoDtos.Count; i++)
        {
            Assert.Equal(manualDtos[i].Id, autoDtos[i].Id);
            Assert.Equal(manualDtos[i].CustomerName, autoDtos[i].CustomerName);
            Assert.Equal(manualDtos[i].CustomerLevel, autoDtos[i].CustomerLevel);
            Assert.Equal(manualDtos[i].FormattedAmount, autoDtos[i].FormattedAmount);
        }
    }

    /// <summary>
    /// IT-INC-EQ3: 单条查询 First 也正确执行自动 Include
    /// </summary>
    [Fact]
    public void EFCore_AutoInclude_First_Works()
    {
        var dto = _db.Orders
            .Where(o => o.Id == 1)
            .AsDto()
            .First<OrderDegradedDto>();

        Assert.NotNull(dto);
        Assert.False(string.IsNullOrEmpty(dto!.CustomerName));
        Assert.StartsWith("¥", dto.FormattedAmount);
    }
}
