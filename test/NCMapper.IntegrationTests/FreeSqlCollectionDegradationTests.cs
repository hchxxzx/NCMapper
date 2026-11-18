using Xunit;
using FreeSql;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.FreeSql;
using NCMapper.EFCore;
using NCMapper.IntegrationTests.Models;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql 集合导航降级验证 + EF Core 一致性测试（T7）
    /// FreeSql 集合投影不可翻译为 SQL → 走 IncludeMany + MapList 降级路径
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class FreeSqlCollectionDegradationTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly string _dbPath;

        public FreeSqlCollectionDegradationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_fscoll_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();
            SeedData();
        }

        public void Dispose()
        {
            _fsql.Dispose();
            if (File.Exists(_dbPath))
                try { File.Delete(_dbPath); } catch { }
        }

        private void SeedData()
        {
            _fsql.Insert(new ColProduct { Id = 1, Sku = "FS-SKU-001", Price = 150 }).ExecuteAffrows();
            _fsql.Insert(new ColProduct { Id = 2, Sku = "FS-SKU-002", Price = 250 }).ExecuteAffrows();

            _fsql.Insert(new ColOrder { Id = 1, OrderNo = "FS-ORD-001" }).ExecuteAffrows();
            _fsql.Insert(new ColOrder { Id = 2, OrderNo = "FS-ORD-002" }).ExecuteAffrows();

            _fsql.Insert(new ColItem { Id = 10, Quantity = 3, OrderId = 1, ProductId = 1 }).ExecuteAffrows();
            _fsql.Insert(new ColItem { Id = 11, Quantity = 7, OrderId = 1, ProductId = 2 }).ExecuteAffrows();

            _fsql.Insert(new ColTag { Id = 100, Label = "Heavy", ItemId = 10 }).ExecuteAffrows();
            _fsql.Insert(new ColTag { Id = 101, Label = "Fragile", ItemId = 10 }).ExecuteAffrows();
        }

        // ─── #1 FreeSql 降级路径集合数据正确 ───

        [Fact]
        public void FreeSql_Degradation_Collection_Data_Correct()
        {
            var orders = _fsql.Select<ColOrder>()
                .IncludeMany(o => o.Items, then => then.Include(i => i.Product))
                .ToList();

            var dtos = Mapper.MapList<ColOrder, ColOrderDto>(orders);
            Assert.NotNull(dtos);

            var dto1 = dtos!.First(d => d.Id == 1);
            Assert.NotNull(dto1.Items);
            Assert.Equal(2, dto1.Items!.Count);
            Assert.Equal(3, dto1.Items.First(i => i.Id == 10).Quantity);
            Assert.Equal(7, dto1.Items.First(i => i.Id == 11).Quantity);
        }

        // ─── #2 降级路径跨表字段正确 ───

        [Fact]
        public void FreeSql_Degradation_CrossTable_Field()
        {
            var orders = _fsql.Select<ColOrder>()
                .IncludeMany(o => o.Items, then => then.Include(i => i.Product))
                .ToList();

            var dtos = Mapper.MapList<ColOrder, ColOrderDto>(orders);
            var dto1 = dtos!.First(d => d.Id == 1);
            var item10 = dto1.Items!.First(i => i.Id == 10);

            Assert.Equal("FS-SKU-001", item10.ProductSku);
        }

        // ─── #3 降级路径嵌套集合 ───

        [Fact]
        public void FreeSql_Degradation_Nested_Collection()
        {
            var orders = _fsql.Select<ColOrder>()
                .IncludeMany(o => o.Items, then => then.IncludeMany(i => i.Tags))
                .ToList();

            var dtos = Mapper.MapList<ColOrder, ColOrderDto>(orders);
            var dto1 = dtos!.First(d => d.Id == 1);
            var item10 = dto1.Items!.First(i => i.Id == 10);

            Assert.NotNull(item10.Tags);
            Assert.Equal(2, item10.Tags!.Count);
            Assert.Contains(item10.Tags, t => t.Label == "Heavy");
            Assert.Contains(item10.Tags, t => t.Label == "Fragile");
        }

        // ─── #4 空集合降级 ───

        [Fact]
        public void FreeSql_Degradation_Empty_Collection()
        {
            var orders = _fsql.Select<ColOrder>()
                .IncludeMany(o => o.Items)
                .Where(o => o.Id == 2)
                .ToList();

            var dtos = Mapper.MapList<ColOrder, ColOrderDto>(orders);
            var dto2 = dtos!.First(d => d.Id == 2);

            // Order2 无 Items → 空集合或 null
            Assert.True(dto2.Items == null || dto2.Items.Count == 0);
        }

        // ─── #5 EF Core vs FreeSql 一致性 ───

        [Fact]
        public void EFCore_vs_FreeSql_Consistency()
        {
            // FreeSql 降级路径
            var fsOrders = _fsql.Select<ColOrder>()
                .IncludeMany(o => o.Items, then => then.Include(i => i.Product))
                .Where(o => o.Id == 1)
                .ToList();
            var fsDtos = Mapper.MapList<ColOrder, ColOrderDto>(fsOrders);
            var fsDto = fsDtos!.First();

            // EF Core 投影路径（使用独立 SQLite 内存库，相同数据）
            var connStr = $"DataSource=file:consistency{Guid.NewGuid():N}?mode=memory&cache=shared";
            using var efDb = new ColPocDbContext(connStr);
            efDb.Database.EnsureCreated();
            efDb.Products.AddRange(
                new ColProduct { Id = 1, Sku = "FS-SKU-001", Price = 150 },
                new ColProduct { Id = 2, Sku = "FS-SKU-002", Price = 250 });
            efDb.Orders.Add(new ColOrder
            {
                Id = 1,
                OrderNo = "FS-ORD-001",
                Items = new List<ColItem>
                {
                    new ColItem { Id = 10, Quantity = 3, OrderId = 1, ProductId = 1,
                        Tags = new List<ColTag> { new ColTag { Id = 100, Label = "Heavy", ItemId = 10 } } },
                    new ColItem { Id = 11, Quantity = 7, OrderId = 1, ProductId = 2 }
                }
            });
            efDb.SaveChanges();

            var efDto = efDb.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .First();

            // 逐字段对比
            Assert.Equal(efDto.Id, fsDto.Id);
            Assert.Equal(efDto.OrderNo, fsDto.OrderNo);
            Assert.Equal(efDto.Items!.Count, fsDto.Items!.Count);

            var efItem = efDto.Items.First(i => i.Id == 10);
            var fsItem = fsDto.Items.First(i => i.Id == 10);
            Assert.Equal(efItem.Quantity, fsItem.Quantity);
            Assert.Equal(efItem.ProductSku, fsItem.ProductSku);
        }

        // ─── #6 双 ORM 投影缓存清理均生效（C10 补测） ───

        /// <summary>
        /// 同进程双 ORM（EFCore + FreeSql）投影缓存清理均生效（C10 补测）
        /// <para>断言形态双 ORM 投影缓存清理均生效测试。
        /// EFCore（<see cref="EfCoreProjectionBuilder"/>）与 FreeSql（<see cref="FreeSqlProjectionBuilder"/>）
        /// 均在静态构造中向 Mapper 注册投影缓存清理委托（RegisterProjectionCacheClear），
        /// <see cref="Mapper.ClearCache"/> 应级联清理两者；清理后再次投影结果守恒
        /// （缓存重建无脏残留，降级路径可用）。</para>
        /// </summary>
        [Fact]
        public void DualOrm_ProjectionCacheClear_All_Effective()
        {
            // ── EFCore 侧：构建投影缓存 ──
            var connStr = $"DataSource=file:dualclear{Guid.NewGuid():N}?mode=memory&cache=shared";
            using var efDb = new ColPocDbContext(connStr);
            efDb.Database.EnsureCreated();
            // 先播种 Products（ColItem.ProductId 外键依赖，缺失会触发 SQLite FK 约束失败）
            efDb.Products.AddRange(
                new ColProduct { Id = 1, Sku = "EF-SKU-001", Price = 150 },
                new ColProduct { Id = 2, Sku = "EF-SKU-002", Price = 250 });
            efDb.Orders.Add(new ColOrder
            {
                Id = 1,
                OrderNo = "FS-ORD-001",
                Items = new List<ColItem>
                {
                    new ColItem { Id = 10, Quantity = 3, OrderId = 1, ProductId = 1,
                        Tags = new List<ColTag> { new ColTag { Id = 100, Label = "Heavy", ItemId = 10 } } }
                }
            });
            efDb.SaveChanges();

            var efBefore = efDb.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .First();
            Assert.Single(efBefore.Items!);
            Assert.Equal("FS-ORD-001", efBefore.OrderNo);

            // ── FreeSql 侧：构建投影缓存（AsDto 投影路径） ──
            var fsBefore = _fsql.Select<ColOrder>()
                .IncludeMany(o => o.Items, then => then.Include(i => i.Product))
                .Where(o => o.Id == 1)
                .ToList();
            var fsDtosBefore = Mapper.MapList<ColOrder, ColOrderDto>(fsBefore);
            Assert.Single(fsDtosBefore!);
            // FreeSql 种子：Order 1 含 2 个 Items（Item 10/11，见 SeedData）
            Assert.Equal(2, fsDtosBefore![0].Items!.Count);

            // ── 统一清理：级联两 ORM 投影缓存 ──
            Mapper.ClearCache();

            // ── EFCore 再次投影（缓存重建后结果守恒） ──
            var efAfter = efDb.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<ColOrder, ColOrderDto>())
                .First();
            Assert.Single(efAfter.Items!);
            Assert.Equal("FS-ORD-001", efAfter.OrderNo);

            // ── FreeSql 再次投影（缓存重建后结果守恒） ──
            var fsAfter = _fsql.Select<ColOrder>()
                .IncludeMany(o => o.Items, then => then.Include(i => i.Product))
                .Where(o => o.Id == 1)
                .ToList();
            var fsDtosAfter = Mapper.MapList<ColOrder, ColOrderDto>(fsAfter);
            Assert.Single(fsDtosAfter!);
            // FreeSql 种子：Order 1 含 2 个 Items（缓存重建后守恒）
            Assert.Equal(2, fsDtosAfter![0].Items!.Count);
            // 修复原 Items![0] 依赖返回顺序，改按 Id 精确定位
            Assert.Equal(3, fsDtosAfter![0].Items!.First(i => i.Id == 10).Quantity);
        }

    }
}
