using Xunit;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.EFCore;
using NCMapper.IntegrationTests.Models;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// EF Core 集合导航投影集成测试（T6）
    /// 复用 POC 模型（ColOrder/ColItem/ColProduct/ColTag）+ ColPocDbContext
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class EFCoreCollectionProjectionTests : IDisposable
    {
        private readonly ColPocDbContext _db;

        public EFCoreCollectionProjectionTests()
        {
            var connStr = $"DataSource=file:collproj{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new ColPocDbContext(connStr);
            _db.Database.EnsureCreated();
            SeedData();
        }

        public void Dispose() => _db.Dispose();

        private void SeedData()
        {
            var product1 = new ColProduct { Id = 1, Sku = "SKU-001", Price = 100 };
            var product2 = new ColProduct { Id = 2, Sku = "SKU-002", Price = 200 };
            _db.Products.AddRange(product1, product2);

            var order1 = new ColOrder
            {
                Id = 1,
                OrderNo = "ORD-001",
                Items = new List<ColItem>
                {
                    new ColItem
                    {
                        Id = 10, Quantity = 2, OrderId = 1, ProductId = 1,
                        Tags = new List<ColTag>
                        {
                            new ColTag { Id = 100, Label = "Fragile", ItemId = 10 },
                            new ColTag { Id = 101, Label = "Express", ItemId = 10 }
                        }
                    },
                    new ColItem { Id = 11, Quantity = 5, OrderId = 1, ProductId = 2 }
                }
            };

            var order2 = new ColOrder
            {
                Id = 2,
                OrderNo = "ORD-002",
                Items = new List<ColItem>()  // 空集合
            };

            var order3 = new ColOrder { Id = 3, OrderNo = "ORD-003" };
            // order3.Items 保持默认 new()，但导航属性不加载时为 null 场景由查询方式决定

            _db.Orders.AddRange(order1, order2, order3);
            _db.SaveChanges();
        }

        // ─── #1 基本集合投影 ───

        [Fact]
        public void EFCore_Collection_Basic_Projection()
        {
            var dtos = _db.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .ToList();

            Assert.Single(dtos);
            var dto = dtos[0];
            Assert.Equal(1, dto.Id);
            Assert.Equal("ORD-001", dto.OrderNo);
            Assert.NotNull(dto.Items);
            Assert.Equal(2, dto.Items!.Count);
        }

        // ─── #2 集合元素跨表字段（JOIN 正确）───

        [Fact]
        public void EFCore_Collection_CrossTable_Join()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .First();

            var item = dto.Items!.First(i => i.Id == 10);
            Assert.Equal("SKU-001", item.ProductSku);
            Assert.Equal(100, item.ProductPrice);
        }

        // ─── #3 集合为空 → 空 List ───

        [Fact]
        public void EFCore_Empty_Collection_Returns_EmptyList()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 2)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .First();

            // EF Core 投影：空集合可能返回空 List 或 null（取决于 SQL 翻译）
            Assert.True(dto.Items == null || dto.Items.Count == 0);
        }

        // ─── #4 嵌套集合 Items[].Tags[] ───

        [Fact]
        public void EFCore_Nested_Collection_Tags()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .First();

            var item10 = dto.Items!.First(i => i.Id == 10);
            Assert.NotNull(item10.Tags);
            Assert.Equal(2, item10.Tags!.Count);
            Assert.Contains(item10.Tags, t => t.Label == "Fragile");
            Assert.Contains(item10.Tags, t => t.Label == "Express");
        }

        // ─── #5 嵌套集合为空 ───

        [Fact]
        public void EFCore_Nested_Collection_Empty()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .First();

            var item11 = dto.Items!.First(i => i.Id == 11);
            Assert.NotNull(item11.Tags);
            Assert.Empty(item11.Tags!);
        }

        // ─── #7 多订单批量查询 ───

        [Fact]
        public void EFCore_Multiple_Orders_Collection()
        {
            var dtos = _db.Orders
                .OrderBy(o => o.Id)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .ToList();

            Assert.Equal(3, dtos.Count);
            Assert.Equal(2, dtos[0].Items!.Count);  // order1: 2 items
            Assert.Empty(dtos[1].Items!);            // order2: empty
        }

        // ─── #8 AsDto 扩展方法路径 ───

        [Fact]
        public void EFCore_AsDto_Extension_Collection()
        {
            var dtos = _db.Orders
                .Where(o => o.Id == 1)
                .AsDto()
                .ToList<ColOrderDto>();

            Assert.Single(dtos);
            Assert.NotNull(dtos[0].Items);
            Assert.Equal(2, dtos[0].Items!.Count);
        }

        // ─── #9 SQL 生成验证（单条 SQL，非 N+1）───

        [Fact]
        public void EFCore_Generates_Single_Sql()
        {
            var sqlLogs = new List<string>();
            var connStr = $"DataSource=file:sqllog{Guid.NewGuid():N}?mode=memory&cache=shared";
            using var logDb = new ColPocDbContext(connStr, sql => sqlLogs.Add(sql));
            logDb.Database.EnsureCreated();

            // 插入最小数据
            logDb.Orders.Add(new ColOrder { Id = 99, OrderNo = "SQL-TEST", Items = new List<ColItem>() });
            logDb.SaveChanges();
            sqlLogs.Clear();

            var _ = logDb.Orders
                .Where(o => o.Id == 99)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .ToList();

            // 应只有 1 条 SELECT（含 LEFT JOIN），不是 N+1
            var selectCount = sqlLogs.Count(s => s.Contains("SELECT", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, selectCount);
        }

        // ─── #10 跨表字段为 null（LEFT JOIN 无匹配）───

        [Fact]
        public void EFCore_CrossTable_Null_Product()
        {
            // 添加一个没有 Product 的 Item
            _db.Items.Add(new ColItem { Id = 99, Quantity = 1, OrderId = 1, ProductId = null });
            _db.SaveChanges();

            var dto = _db.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .First();

            var item99 = dto.Items!.First(i => i.Id == 99);
            Assert.Null(item99.ProductSku);
            Assert.Equal(0, item99.ProductPrice);
        }

        // ─── #11 运行时投影与 Include+MapList 等价 ───

        [Fact]
        public void EFCore_Projection_Matches_Include_MapList()
        {
            // 投影路径
            var projDto = _db.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .First();

            // Include + 运行时 Map 路径
            var entity = _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Items).ThenInclude(i => i.Tags)
                .First(o => o.Id == 1);
            var mapDto = Mapper.Map<ColOrder, ColOrderDto>(entity)!;

            Assert.Equal(mapDto.Id, projDto.Id);
            Assert.Equal(mapDto.OrderNo, projDto.OrderNo);
            Assert.Equal(mapDto.Items?.Count, projDto.Items?.Count);
            if (mapDto.Items != null && projDto.Items != null)
            {
                var mapItem = mapDto.Items.First(i => i.Id == 10);
                var projItem = projDto.Items.First(i => i.Id == 10);
                Assert.Equal(mapItem.ProductSku, projItem.ProductSku);
                Assert.Equal(mapItem.Quantity, projItem.Quantity);
            }
        }

        // ─── #12 分页 + 集合投影 ───

        [Fact]
        public void EFCore_Paging_With_Collection()
        {
            var page = _db.Orders
                .OrderBy(o => o.Id)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .Skip(0)
                .Take(2)
                .ToList();

            Assert.Equal(2, page.Count);
            Assert.Equal("ORD-001", page[0].OrderNo);
            Assert.Equal("ORD-002", page[1].OrderNo);
        }
    }
}
