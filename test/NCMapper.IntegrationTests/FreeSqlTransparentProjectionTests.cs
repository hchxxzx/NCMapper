using FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests
{
    public class FreeSqlTransparentProjectionTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly string _dbPath;

        public FreeSqlTransparentProjectionTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_tp_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            _fsql.Insert(new List<TpOrder>
            {
                new() { Id = 1, OrderNo = "ORD-001", CustomerId = 10, ProductId = 100, Amount = 100.50m },
                new() { Id = 2, OrderNo = "ORD-002", CustomerId = 20, ProductId = 100, Amount = 200.00m }
            }).ExecuteAffrows();

            _fsql.Insert(new List<TpCustomer>
            {
                new() { Id = 10, Name = "Alice", Phone = "13800000001" },
                new() { Id = 20, Name = "Bob", Phone = "13800000002" }
            }).ExecuteAffrows();

            _fsql.Insert(new TpProduct { Id = 100, ProductName = "Widget", Price = 9.99m }).ExecuteAffrows();
        }

        public void Dispose()
        {
            _fsql.Dispose();
            if (File.Exists(_dbPath)) try { File.Delete(_dbPath); } catch { }
        }

        /// <summary>
        /// 测试 1：双表透传投影 — Select<T1, T2>().LeftJoin().ToList((a, b) => new { T1 = a, T2 = b })
        /// </summary>
        [Fact]
        public void DualTable_TransparentProjection_BothEntitiesFilled()
        {
            var result = _fsql.Select<TpOrder, TpCustomer>()
                .LeftJoin((a, b) => a.CustomerId == b.Id)
                .ToList((a, b) => new { Order = a, Customer = b })
                .OrderBy(x => x.Order.Id)
                .ToList();

            Assert.Equal(2, result.Count);

            // T1（Order）完整填充
            Assert.Equal(1, result[0].Order.Id);
            Assert.Equal("ORD-001", result[0].Order.OrderNo);
            Assert.Equal(100.50m, result[0].Order.Amount);

            // T2（Customer）完整填充
            Assert.Equal(10, result[0].Customer.Id);
            Assert.Equal("Alice", result[0].Customer.Name);
            Assert.Equal("13800000001", result[0].Customer.Phone);

            Assert.Equal("Bob", result[1].Customer.Name);
        }

        /// <summary>
        /// 测试 2：同名字段冲突 — 两表都有 Id，验证不混淆
        /// </summary>
        [Fact]
        public void DualTable_SameNameField_NoConflict()
        {
            var result = _fsql.Select<TpOrder, TpCustomer>()
                .LeftJoin((a, b) => a.CustomerId == b.Id)
                .ToList((a, b) => new { Order = a, Customer = b })
                .OrderBy(x => x.Order.Id)
                .ToList();

            Assert.Equal(1, result[0].Order.Id);    // Order.Id = 1
            Assert.Equal(10, result[0].Customer.Id); // Customer.Id = 10，不混淆
            Assert.Equal(2, result[1].Order.Id);
            Assert.Equal(20, result[1].Customer.Id);
        }

        /// <summary>
        /// 测试 3：三表透传投影
        /// </summary>
        [Fact]
        public void ThreeTable_TransparentProjection_AllFilled()
        {
            var result = _fsql.Select<TpOrder, TpCustomer, TpProduct>()
                .LeftJoin((a, b, c) => a.CustomerId == b.Id)
                .LeftJoin((a, b, c) => a.ProductId == c.Id)
                .ToList((a, b, c) => new { Order = a, Customer = b, Product = c })
                .OrderBy(x => x.Order.Id)
                .ToList();

            Assert.Equal(2, result.Count);
            Assert.Equal("ORD-001", result[0].Order.OrderNo);
            Assert.Equal("Alice", result[0].Customer.Name);
            Assert.Equal("Widget", result[0].Product.ProductName);
            Assert.Equal(9.99m, result[0].Product.Price);
        }

    }

    public class TpOrder
    {
        public int Id { get; set; }
        public string OrderNo { get; set; } = "";
        public int CustomerId { get; set; }
        public int ProductId { get; set; }
        public decimal Amount { get; set; }
    }

    public class TpCustomer
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Phone { get; set; } = "";
    }

    public class TpProduct
    {
        public int Id { get; set; }
        public string ProductName { get; set; } = "";
        public decimal Price { get; set; }
    }
}
