// 集成测试：EFCoreDtoSelector.ToLookup 带映射转手分组映射
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.IntegrationTests.Models;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// 订单分组 DTO — 含跨表 CustomerName
    /// </summary>
    [MapFrom(typeof(OrderEntity))]
    public class OrderGroupDto
    {
        /// <summary>订单 ID</summary>
        public int Id { get; set; }

        /// <summary>订单编号</summary>
        public string OrderNo { get; set; } = "";

        /// <summary>客户 ID（分组键用）</summary>
        public int CustomerId { get; set; }

        /// <summary>
        /// 客户名称 — 跨表映射自 CustomerEntity.Name
        /// </summary>
        [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
        public string? CustomerName { get; set; }
    }

    /// <summary>
    /// EF Core ToLookup 带映射转手集成测试
    /// </summary>
    public class EFCoreLookupProjectionTests : IDisposable
    {
        private readonly TestDbContext _db;

        public EFCoreLookupProjectionTests()
        {
            var connStr = $"DataSource=file:test{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new TestDbContext(connStr);
            _db.SeedData();
        }

        public void Dispose() => _db.Dispose();

        /// <summary>
        /// L-01：AsDto().ToLookup 基础分组映射
        /// </summary>
        [Fact]
        public void ToLookup_基础分组映射正确()
        {
            var lookup = _db.Orders.AsDto().ToLookup<int, OrderGroupDto>(o => o.CustomerId);

            // CustomerId=1: ORD-001 + ORD-003
            Assert.Equal(2, lookup[1].Count());
            Assert.Contains(lookup[1], d => d.OrderNo == "ORD-001");
            Assert.Contains(lookup[1], d => d.OrderNo == "ORD-003");

            // CustomerId=2: ORD-002
            Assert.Single(lookup[2]);
            Assert.Equal("ORD-002", lookup[2].First().OrderNo);
        }

        /// <summary>
        /// L-02：AsDto().ToLookup 跨表 CustomerName 填充
        /// </summary>
        [Fact]
        public void ToLookup_跨表CustomerName填充()
        {
            var lookup = _db.Orders.AsDto().ToLookup<int, OrderGroupDto>(o => o.CustomerId);

            // ZhangSan 的订单
            Assert.All(lookup[1], d => Assert.Equal("ZhangSan", d.CustomerName));

            // LiSi 的订单
            Assert.All(lookup[2], d => Assert.Equal("LiSi", d.CustomerName));
        }

        /// <summary>
        /// L-03：AsDto().ToLookup 空结果返回空 Lookup
        /// </summary>
        [Fact]
        public void ToLookup_空结果集_返回空Lookup()
        {
            var lookup = _db.Orders
                .Where(o => o.Id < 0)
                .AsDto()
                .ToLookup<int, OrderGroupDto>(o => o.CustomerId);

            Assert.Empty(lookup);
        }

        /// <summary>
        /// L-04：AsDto().ToLookup 字符串键类型
        /// </summary>
        [Fact]
        public void ToLookup_字符串键类型()
        {
            var lookup = _db.Orders.AsDto().ToLookup<string, OrderGroupDto>(o => o.CustomerId.ToString());

            Assert.Equal(2, lookup.Count);
            Assert.Equal(2, lookup["1"].Count());
            Assert.Single(lookup["2"]);
        }
    }
}
