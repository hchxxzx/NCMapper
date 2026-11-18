// 集成测试：EFCoreDtoSelector.ToDictionary/Async 带映射转手字典映射
// 覆盖维度：基础字典 / 跨表 / 异步 / 空结果
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.IntegrationTests.Models;
using Xunit;

namespace NCMapper.IntegrationTests
{
    // ================================================================
    // EFCore 字典测试 DTO
    // ================================================================

    /// <summary>
    /// 订单扁平 DTO — 含跨表 CustomerName，用于 EFCore 字典映射测试
    /// </summary>
    [MapFrom(typeof(OrderEntity))]
    public class OrderFlatDto
    {
        /// <summary>订单 ID</summary>
        public int Id { get; set; }

        /// <summary>订单编号</summary>
        public string OrderNo { get; set; } = "";

        /// <summary>订单金额</summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// 客户名称 — 跨表映射自 CustomerEntity.Name
        /// </summary>
        [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
        public string? CustomerName { get; set; }
    }

    /// <summary>
    /// EF Core ToDictionary 带映射转手集成测试
    /// <para>复用 TestDbContext（SQLite 内存数据库）+ OrderEntity/CustomerEntity</para>
    /// </summary>
    public class EFCoreDictionaryProjectionTests : IDisposable
    {
        private readonly TestDbContext _db;

        /// <summary>
        /// 初始化 SQLite 内存数据库并植入测试数据
        /// </summary>
        public EFCoreDictionaryProjectionTests()
        {
            var connStr = $"DataSource=file:test{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new TestDbContext(connStr);
            _db.SeedData();
        }

        /// <summary>
        /// 释放数据库上下文
        /// </summary>
        public void Dispose() => _db.Dispose();

        /// <summary>
        /// E-D01：AsDto().ToDictionary 基础字典映射
        /// </summary>
        [Fact]
        public void ToDictionary_基础字典映射正确()
        {
            var dict = _db.Orders.AsDto().ToDictionary<int, OrderFlatDto>(o => o.Id);

            Assert.Equal(3, dict.Count);
            Assert.Equal("ORD-001", dict[1].OrderNo);
            Assert.Equal(299.99m, dict[1].Amount);
            Assert.Equal("ORD-002", dict[2].OrderNo);
            Assert.Equal(1500m, dict[2].Amount);
            Assert.Equal("ORD-003", dict[3].OrderNo);
            Assert.Equal(50.5m, dict[3].Amount);
        }

        /// <summary>
        /// E-D02：AsDto().ToDictionary 跨表 CustomerName 自动 Include + 映射
        /// </summary>
        [Fact]
        public void ToDictionary_跨表CustomerName填充()
        {
            var dict = _db.Orders.AsDto().ToDictionary<int, OrderFlatDto>(o => o.Id);

            Assert.Equal(3, dict.Count);
            Assert.Equal("ZhangSan", dict[1].CustomerName);
            Assert.Equal("LiSi", dict[2].CustomerName);
            Assert.Equal("ZhangSan", dict[3].CustomerName);
        }

        /// <summary>
        /// E-D03：AsDto().ToDictionary 字符串键类型
        /// </summary>
        [Fact]
        public void ToDictionary_字符串键类型()
        {
            var dict = _db.Orders.AsDto().ToDictionary<string, OrderFlatDto>(o => o.OrderNo);

            Assert.Equal(3, dict.Count);
            Assert.Equal(1, dict["ORD-001"].Id);
            Assert.Equal(299.99m, dict["ORD-001"].Amount);
        }

        /// <summary>
        /// E-D04：AsDto().ToDictionaryAsync 异步版本行为一致
        /// </summary>
        [Fact]
        public async Task ToDictionaryAsync_异步字典映射正确()
        {
            var dict = await _db.Orders.AsDto()
                .ToDictionaryAsync<int, OrderFlatDto>(o => o.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, dict.Count);
            Assert.Equal("ORD-001", dict[1].OrderNo);
            Assert.Equal("ZhangSan", dict[1].CustomerName);
        }

        /// <summary>
        /// E-D05：AsDto().ToDictionary 空结果集返回空字典
        /// </summary>
        [Fact]
        public void ToDictionary_空结果集_返回空字典()
        {
            var dict = _db.Orders
                .Where(o => o.Id < 0)
                .AsDto()
                .ToDictionary<int, OrderFlatDto>(o => o.Id);

            Assert.NotNull(dict);
            Assert.Empty(dict);
        }

        /// <summary>
        /// E-D06：AsDto().ToDictionaryAsync 异步空结果
        /// </summary>
        [Fact]
        public async Task ToDictionaryAsync_空结果集_返回空字典()
        {
            var dict = await _db.Orders
                .Where(o => o.Amount > 10000)
                .AsDto()
                .ToDictionaryAsync<int, OrderFlatDto>(o => o.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(dict);
            Assert.Empty(dict);
        }
    }
}
