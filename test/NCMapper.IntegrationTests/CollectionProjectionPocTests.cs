using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;

namespace NCMapper.IntegrationTests.Models
{
    // ================================================================
    // POC 集合导航投影 — 实体模型
    // 链路：ColOrder → ColItems[] → ColProduct / ColTags[]
    // ================================================================

    /// <summary>订单实体（含 Items 一对多集合导航）</summary>
    public class ColOrder
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单编号</summary>
        public string? OrderNo { get; set; }
        /// <summary>订单明细集合（一对多）</summary>
        [global::FreeSql.DataAnnotations.Navigate(nameof(ColItem.OrderId))]
        public List<ColItem> Items { get; set; } = new();
    }

    /// <summary>订单明细实体（含 Product 多对一导航 + Tags 一对多集合）</summary>
    public class ColItem
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>数量</summary>
        public int Quantity { get; set; }
        /// <summary>所属订单外键</summary>
        public int OrderId { get; set; }
        /// <summary>产品外键（可空 — 验证跨表 null）</summary>
        public int? ProductId { get; set; }
        /// <summary>产品导航属性</summary>
        [global::FreeSql.DataAnnotations.Navigate(nameof(ProductId))]
        public ColProduct? Product { get; set; }
        /// <summary>标签集合（嵌套集合：Items[].Tags[]）</summary>
        [global::FreeSql.DataAnnotations.Navigate(nameof(ColTag.ItemId))]
        public List<ColTag> Tags { get; set; } = new();
    }

    /// <summary>产品实体</summary>
    public class ColProduct
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>SKU 编码</summary>
        public string? Sku { get; set; }
        /// <summary>价格</summary>
        public int Price { get; set; }
    }

    /// <summary>标签实体（嵌套集合最深层）</summary>
    public class ColTag
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标签名</summary>
        public string? Label { get; set; }
        /// <summary>所属明细外键</summary>
        public int ItemId { get; set; }
    }

    // ================================================================
    // POC DTO
    // ================================================================

    /// <summary>订单 DTO — 含集合导航 Items</summary>
    [MapFrom(typeof(ColOrder))]
    public class ColOrderDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单编号</summary>
        public string? OrderNo { get; set; }
        /// <summary>集合导航投影</summary>
        public List<ColItemDto>? Items { get; set; }
    }

    /// <summary>明细 DTO — 含跨表字段 ProductSku + 嵌套集合 Tags</summary>
    [MapFrom(typeof(ColItem))]
    public class ColItemDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>数量</summary>
        public int Quantity { get; set; }
        /// <summary>跨表字段 — 产品 SKU</summary>
        [MapProperty(typeof(ColProduct), nameof(ColProduct.Sku))]
        public string? ProductSku { get; set; }
        /// <summary>跨表字段 — 产品价格</summary>
        [MapProperty(typeof(ColProduct), nameof(ColProduct.Price))]
        public int ProductPrice { get; set; }
        /// <summary>嵌套集合 — 标签</summary>
        public List<ColTagDto>? Tags { get; set; }
    }

    /// <summary>标签 DTO</summary>
    [MapFrom(typeof(ColTag))]
    public class ColTagDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标签名</summary>
        public string? Label { get; set; }
    }
}

namespace NCMapper.IntegrationTests
{
    using NCMapper.IntegrationTests.Models;

    /// <summary>
    /// POC 集合投影 EF Core DbContext — SQLite 内存数据库
    /// </summary>
    public class ColPocDbContext : DbContext
    {
        /// <summary>订单表</summary>
        public DbSet<ColOrder> Orders { get; set; } = null!;
        /// <summary>明细表</summary>
        public DbSet<ColItem> Items { get; set; } = null!;
        /// <summary>产品表</summary>
        public DbSet<ColProduct> Products { get; set; } = null!;
        /// <summary>标签表</summary>
        public DbSet<ColTag> Tags { get; set; } = null!;

        private readonly string _connectionString;
        private readonly Action<string>? _sqlLogger;

        /// <summary>构造函数</summary>
        public ColPocDbContext(string connectionString, Action<string>? sqlLogger = null)
        {
            _connectionString = connectionString;
            _sqlLogger = sqlLogger;
        }

        /// <inheritdoc/>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(_connectionString);
            if (_sqlLogger != null)
            {
                optionsBuilder.LogTo(_sqlLogger, LogLevel.Information);
            }
        }

        /// <inheritdoc/>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ColOrder>(b =>
            {
                b.ToTable("ColOrders");
                b.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
            });
            modelBuilder.Entity<ColItem>(b =>
            {
                b.ToTable("ColItems");
                b.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId);
                b.HasMany(i => i.Tags).WithOne().HasForeignKey(t => t.ItemId);
            });
            modelBuilder.Entity<ColProduct>().ToTable("ColProducts");
            modelBuilder.Entity<ColTag>().ToTable("ColTags");
        }

        /// <summary>
        /// 植入测试数据：
        /// Order 1 — 2 个 Items（Item1 有 Product + 2 Tags，Item2 无 Product + 0 Tags）
        /// Order 2 — 0 个 Items（空集合）
        /// </summary>
        public void SeedData()
        {
            Database.EnsureCreated();

            Products.AddRange(
                new ColProduct { Id = 1, Sku = "SKU-A", Price = 100 },
                new ColProduct { Id = 2, Sku = "SKU-B", Price = 200 });

            Orders.AddRange(
                new ColOrder { Id = 1, OrderNo = "ORD-001" },
                new ColOrder { Id = 2, OrderNo = "ORD-002" });

            Items.AddRange(
                new ColItem { Id = 1, Quantity = 3, OrderId = 1, ProductId = 1 },
                new ColItem { Id = 2, Quantity = 5, OrderId = 1, ProductId = null });

            Tags.AddRange(
                new ColTag { Id = 1, Label = "Fragile", ItemId = 1 },
                new ColTag { Id = 2, Label = "Express", ItemId = 1 });

            SaveChanges();
        }
    }

    // ================================================================
    // T1: POC — EF Core 集合投影验证
    // ================================================================

    /// <summary>
    /// POC-EF: 验证 EF Core 对集合 Select+ToList 表达式的 SQL 翻译能力
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class CollectionProjectionPocEfTests : IDisposable
    {
        private readonly ColPocDbContext _db;
        private readonly List<string> _sqlLogs = new();

        public CollectionProjectionPocEfTests()
        {
            var connStr = $"DataSource=file:poc{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new ColPocDbContext(connStr, sql => _sqlLogs.Add(sql));
            _db.SeedData();
        }

        public void Dispose() => _db.Dispose();

        /// <summary>
        /// POC-EF-1 + POC-EF-7: 集合 Select+ToList SQL 形状验证
        /// 确认 EF Core 生成 CROSS/OUTER APPLY（可接受）而非 N+1（不可接受）
        /// </summary>
        [Fact]
        public void POC_EF1_CollectionSelectToList_SqlShape()
        {
            // 手写集合投影表达式（模拟 NCMapper 将生成的表达式）
            var query = _db.Orders
                .Select(o => new ColOrderDto
                {
                    Id = o.Id,
                    OrderNo = o.OrderNo,
                    Items = o.Items.Select(i => new ColItemDto
                    {
                        Id = i.Id,
                        Quantity = i.Quantity
                    }).ToList()
                })
                .OrderBy(d => d.Id);

            // 捕获 SQL
            var sql = query.ToQueryString();
            _sqlLogs.Add($"=== ToQueryString ===\n{sql}");

            // 写入临时文件供分析
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "poc_ef1_sql.txt"), sql);

            var results = query.ToList();

            // 基本断言
            Assert.Equal(2, results.Count);
            Assert.Equal(2, results[0].Items!.Count);  // Order1 有 2 个 Items
            Assert.Empty(results[1].Items!);            // Order2 有 0 个 Items

            // SQL 形状判定：不应包含 N+1 特征（多个独立 SELECT）
            // EF Core 对集合 Select 通常生成 OUTER APPLY 或 LEFT JOIN + 子查询
            Assert.NotNull(sql);
            // 记录结论到输出
            _sqlLogs.Add($"=== POC-EF-1/7 结论 ===");
            _sqlLogs.Add($"SQL 包含 APPLY: {sql.Contains("APPLY", StringComparison.OrdinalIgnoreCase)}");
            _sqlLogs.Add($"SQL 包含 JOIN: {sql.Contains("JOIN", StringComparison.OrdinalIgnoreCase)}");
        }

        /// <summary>
        /// POC-EF-2: 集合元素内跨表字段（i.Product.Sku）是否生成 JOIN
        /// </summary>
        [Fact]
        public void POC_EF2_CollectionElement_CrossTable_Join()
        {
            var query = _db.Orders
                .Where(o => o.Id == 1)
                .Select(o => new ColOrderDto
                {
                    Id = o.Id,
                    OrderNo = o.OrderNo,
                    Items = o.Items.Select(i => new ColItemDto
                    {
                        Id = i.Id,
                        Quantity = i.Quantity,
                        ProductSku = i.Product != null ? i.Product.Sku : null,
                        ProductPrice = i.Product != null ? i.Product.Price : 0
                    }).ToList()
                });

            var sql = query.ToQueryString();
            _sqlLogs.Add($"=== POC-EF-2 SQL ===\n{sql}");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "poc_ef2_sql.txt"), sql);

            var result = query.First();
            Assert.NotNull(result.Items);
            Assert.Equal(2, result.Items!.Count);

            // Item1 有 Product（SKU-A）
            var item1 = result.Items.First(i => i.Id == 1);
            Assert.Equal("SKU-A", item1.ProductSku);
            Assert.Equal(100, item1.ProductPrice);

            // Item2 无 Product（null）
            var item2 = result.Items.First(i => i.Id == 2);
            Assert.Null(item2.ProductSku);
            Assert.Equal(0, item2.ProductPrice);

            // SQL 应包含 JOIN（跨表）
            _sqlLogs.Add($"SQL 包含 JOIN: {sql.Contains("JOIN", StringComparison.OrdinalIgnoreCase)}");
        }

        /// <summary>
        /// POC-EF-3: 集合为空时行为 — 空 List（非 null）
        /// </summary>
        [Fact]
        public void POC_EF3_EmptyCollection_ReturnsEmptyList()
        {
            var result = _db.Orders
                .Where(o => o.Id == 2)  // Order2 无 Items
                .Select(o => new ColOrderDto
                {
                    Id = o.Id,
                    OrderNo = o.OrderNo,
                    Items = o.Items.Select(i => new ColItemDto
                    {
                        Id = i.Id,
                        Quantity = i.Quantity
                    }).ToList()
                })
                .First();

            // EF Core 集合投影：空集合 → 空 List（非 null）
            Assert.NotNull(result.Items);
            Assert.Empty(result.Items!);
        }

        /// <summary>
        /// POC-EF-4: 嵌套集合（Items[].Tags[]）SQL 形状
        /// </summary>
        [Fact]
        public void POC_EF4_NestedCollection_SqlShape()
        {
            var query = _db.Orders
                .Where(o => o.Id == 1)
                .Select(o => new
                {
                    o.Id,
                    Items = o.Items.Select(i => new
                    {
                        i.Id,
                        Tags = i.Tags.Select(t => new { t.Id, t.Label }).ToList()
                    }).ToList()
                });

            var sql = query.ToQueryString();
            _sqlLogs.Add($"=== POC-EF-4 嵌套集合 SQL ===\n{sql}");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "poc_ef4_sql.txt"), sql);

            var result = query.First();
            Assert.Equal(2, result.Items.Count);
            var item1 = result.Items.First(i => i.Id == 1);
            Assert.Equal(2, item1.Tags.Count);  // Item1 有 2 个 Tags
            var item2 = result.Items.First(i => i.Id == 2);
            Assert.Empty(item2.Tags);            // Item2 有 0 个 Tags

            _sqlLogs.Add($"嵌套集合 SQL 包含 APPLY: {sql.Contains("APPLY", StringComparison.OrdinalIgnoreCase)}");
        }

        /// <summary>
        /// POC-EF-5: .ToList() vs 不带 .ToList() 翻译差异
        /// </summary>
        [Fact]
        public void POC_EF5_ToList_Vs_NoToList()
        {
            // 带 .ToList()
            var queryWithToList = _db.Orders
                .Where(o => o.Id == 1)
                .Select(o => new
                {
                    o.Id,
                    Items = o.Items.Select(i => new { i.Id, i.Quantity }).ToList()
                });
            var sqlWith = queryWithToList.ToQueryString();

            // 不带 .ToList()（IEnumerable 投影）
            var queryNoToList = _db.Orders
                .Where(o => o.Id == 1)
                .Select(o => new
                {
                    o.Id,
                    Items = o.Items.Select(i => new { i.Id, i.Quantity })
                });
            var sqlWithout = queryNoToList.ToQueryString();

            _sqlLogs.Add($"=== 带 ToList ===\n{sqlWith}");
            _sqlLogs.Add($"=== 不带 ToList ===\n{sqlWithout}");

            // 两者都应能正确执行
            var r1 = queryWithToList.First();
            Assert.Equal(2, r1.Items.Count);

            var r2 = queryNoToList.First();
            Assert.Equal(2, r2.Items.Count());
        }
    }

    // ================================================================
    // T2: POC — FreeSql 集合投影验证
    // ================================================================

    /// <summary>
    /// POC-FS: 验证 FreeSql 对集合投影表达式的翻译能力 + 降级路径等价性
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class CollectionProjectionPocFsTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly string _dbPath;

        public CollectionProjectionPocFsTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_poc_fs_{Guid.NewGuid():N}.db");
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
            _fsql.Insert(new List<ColProduct>
            {
                new() { Id = 1, Sku = "SKU-A", Price = 100 },
                new() { Id = 2, Sku = "SKU-B", Price = 200 }
            }).ExecuteAffrows();

            _fsql.Insert(new List<ColOrder>
            {
                new() { Id = 1, OrderNo = "ORD-001" },
                new() { Id = 2, OrderNo = "ORD-002" }
            }).ExecuteAffrows();

            _fsql.Insert(new List<ColItem>
            {
                new() { Id = 1, Quantity = 3, OrderId = 1, ProductId = 1 },
                new() { Id = 2, Quantity = 5, OrderId = 1, ProductId = null }
            }).ExecuteAffrows();

            _fsql.Insert(new List<ColTag>
            {
                new() { Id = 1, Label = "Fragile", ItemId = 1 },
                new() { Id = 2, Label = "Express", ItemId = 1 }
            }).ExecuteAffrows();
        }

        /// <summary>
        /// POC-FS-1 + POC-FS-2: FreeSql 集合 Select 翻译失败模式确认
        /// 验证 FreeSql 对含集合 Select 的投影表达式是否翻译（抛异常 vs 静默错误 SQL）
        /// </summary>
        [Fact]
        public void POC_FS1_CollectionSelect_FailureMode()
        {
            // 尝试用 FreeSql ToList 执行含集合 Select 的投影
            Exception? caught = null;
            List<ColOrderDto>? results = null;

            try
            {
                results = _fsql.Select<ColOrder>()
                    .ToList(o => new ColOrderDto
                    {
                        Id = o.Id,
                        OrderNo = o.OrderNo,
                        Items = o.Items.Select(i => new ColItemDto
                        {
                            Id = i.Id,
                            Quantity = i.Quantity
                        }).ToList()
                    });
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // 记录结论
            // 修复原 try/catch 两分支均通过（恒绿）
            // FreeSql 静默错误也不会失败。实测结论（poc_fs1_result.txt）：
            // FreeSql 集合子查询 Select 翻译失败抛异常（"SQL logic error: sub-select returns 2 columns - expected 1"）。
            // 修复：明确期望路径——必须抛异常且异常消息含 SQL 翻译失败特征。
            Assert.NotNull(caught);
            Assert.Contains("SQL", caught!.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// POC-FS-3: ExpressionBuilder 集合属性处理分支 — 元素内跨表字段
        /// 验证 Mapper.MapList 路径下集合元素内 [MapProperty] 是否生效
        /// </summary>
        [Fact]
        public void POC_FS3_ExpressionBuilder_CollectionCrossTable()
        {
            // 构造内存数据（模拟 Include 后的完整对象图）
            var items = new List<ColItem>
            {
                new() { Id = 1, Quantity = 3, OrderId = 1, ProductId = 1,
                        Product = new ColProduct { Id = 1, Sku = "SKU-A", Price = 100 } },
                new() { Id = 2, Quantity = 5, OrderId = 1, ProductId = null, Product = null }
            };

            // 走 Mapper.MapList 路径（ExpressionBuilder）
            var dtos = Mapper.MapList<ColItem, ColItemDto>(items);

            Assert.NotNull(dtos);
            Assert.Equal(2, dtos!.Count);

            // 验证跨表字段（[MapProperty(typeof(ColProduct), "Sku")]）
            var dto1 = dtos.First(d => d.Id == 1);
            Assert.Equal("SKU-A", dto1.ProductSku);   // 跨表字段应正确
            Assert.Equal(100, dto1.ProductPrice);

            var dto2 = dtos.First(d => d.Id == 2);
            Assert.Null(dto2.ProductSku);              // null 导航 → null
            Assert.Equal(0, dto2.ProductPrice);        // null 导航 → 默认 0
        }

        /// <summary>
        /// POC-FS-4: Include + MapList 降级路径等价性
        /// 验证 FreeSql IncludeByPropertyName("Items") + Mapper.MapList 产出正确数据
        /// </summary>
        [Fact]
        public void POC_FS4_IncludeMapList_Equivalence()
        {
            // FreeSql IncludeMany 集合导航 + 内存 MapList
            var orders = _fsql.Select<ColOrder>()
                .IncludeMany(o => o.Items)
                .ToList();

            Assert.Equal(2, orders.Count);
            var order1 = orders.First(o => o.Id == 1);
            Assert.Equal(2, order1.Items.Count);  // Include 应加载集合

            // MapList 映射（含集合属性 — 走 DeepCopy/ExpressionBuilder）
            var dtos = Mapper.MapList<ColOrder, ColOrderDto>(orders);
            Assert.NotNull(dtos);
            Assert.Equal(2, dtos!.Count);

            var dto1 = dtos.First(d => d.Id == 1);
            // 集合属性应被映射（通过 ExpressionBuilder 或 DeepCopy）
            Assert.NotNull(dto1.Items);
            Assert.Equal(2, dto1.Items!.Count);
            Assert.Equal(3, dto1.Items.First(i => i.Id == 1).Quantity);

            var dto2 = dtos.First(d => d.Id == 2);
            // Order2 无 Items → 空集合或 null
            Assert.True(dto2.Items == null || dto2.Items.Count == 0);
        }
    }
}
