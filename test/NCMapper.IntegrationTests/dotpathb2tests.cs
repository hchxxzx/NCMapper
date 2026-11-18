using System;
using System.IO;
using System.Linq;
using FreeSql;
using FreeSql.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// 点路径冒烟套件 — 集合元素内路径 + FreeSql Compose 点路径
/// <para>H15 集合元素内路径：元素 DTO 内点路径/推导扁平化/叶节点忽略（EFCore 真实 SQL 子查询 + FreeSql 降级链终端）；
/// H25 Compose 点路径：FreeSql 双表/三表点路径 + 扁平化 + 叶节点忽略 + 失败静默跳过（与单表顶层同语义）。</para>
/// <para>隔离范式同 DotPathGoldenTripleTests：[Collection("PipelineGlobalState")] + 构造器/Dispose 双 ClearCache。</para>
/// </summary>
[Collection("PipelineGlobalState")]
public class DotPathB2Tests : IDisposable
{
    public DotPathB2Tests() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); } // 迁移

    public void Dispose() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); } // 迁移

    // ==================== H15 模型（Blog → Posts → Category 两级导航） ====================

    public class B2Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class B2Post
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public int BlogId { get; set; }
        public int CategoryId { get; set; }
        public B2Category? Category { get; set; }
    }

    public class B2Blog
    {
        public int Id { get; set; }

        [Navigate(nameof(B2Post.BlogId))]
        public List<B2Post> Posts { get; set; } = new();
    }

    /// <summary>集合元素 DTO：元素内点路径（Category.Name）+ 元素内推导扁平化（CategoryName → Category.Name）</summary>
    [MapFrom(typeof(B2Post))]
    internal class B2PostDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(B2Post), "Category.Name")]
        public string? City { get; set; }

        public string? CategoryName { get; set; }
    }

    /// <summary>集合元素 DTO（叶忽略变体）</summary>
    [MapFrom(typeof(B2Post))]
    internal class B2PostIgnoreDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(B2Post), "Category.Name")]
        public string? City { get; set; }
    }

    /// <summary>集合元素 DTO（Mask 变体，降级链终端消费验证）</summary>
    [MapFrom(typeof(B2Post))]
    internal class B2PostMaskDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(B2Post), "Category.Name")]
        [Mask(typeof(B2Mask))]
        public string? City { get; set; }
    }

    public sealed class B2Mask : IMask
    {
        public string Mask(string? value) => "MASKED";
    }

    [MapFrom(typeof(B2Blog))]
    internal class B2BlogDto
    {
        public int Id { get; set; }
        public List<B2PostDto> Posts { get; set; } = new();
    }

    [MapFrom(typeof(B2Blog))]
    internal class B2BlogIgnoreDto
    {
        public int Id { get; set; }
        public List<B2PostIgnoreDto> Posts { get; set; } = new();
    }

    [MapFrom(typeof(B2Blog))]
    internal class B2BlogMaskDto
    {
        public int Id { get; set; }
        public List<B2PostMaskDto> Posts { get; set; } = new();
    }

    /// <summary>H15 独立 DbContext（Blog → Posts 集合 + Post → Category 导航）</summary>
    private sealed class B2DbContext : DbContext
    {
        public DbSet<B2Blog> Blogs { get; set; } = null!;
        public DbSet<B2Post> Posts { get; set; } = null!;
        public DbSet<B2Category> Categories { get; set; } = null!;

        private readonly string _connectionString;

        public B2DbContext(string connectionString) => _connectionString = connectionString;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite(_connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<B2Category>().ToTable("B2Categories");
            modelBuilder.Entity<B2Post>().ToTable("B2Posts");
            modelBuilder.Entity<B2Blog>().ToTable("B2Blogs");
            modelBuilder.Entity<B2Post>()
                .HasOne(p => p.Category)
                .WithMany()
                .HasForeignKey(p => p.CategoryId);
            modelBuilder.Entity<B2Blog>()
                .HasMany(b => b.Posts)
                .WithOne()
                .HasForeignKey(p => p.BlogId);
        }
    }

    // ==================== H25 模型（Order → Address 导航 + Customer 跨表） ====================

    public class B2ComposeAddress
    {
        public int Id { get; set; }
        public string City { get; set; } = "";
    }

    public class B2ComposeCustomer
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class B2ComposeOrder
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public int AddressId { get; set; }
        public B2ComposeAddress? Address { get; set; }
        public int CustomerId { get; set; }
        public B2ComposeCustomer? Customer { get; set; }
    }

    /// <summary>Compose 双表 DTO：点路径（Address.City）+ 推导扁平化（AddressCity）+ 跨表字段（现有能力对照）</summary>
    [MapFrom(typeof(B2ComposeOrder))]
    internal class B2ComposeDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(B2ComposeOrder), "Address.City")]
        public string? OrderCity { get; set; }

        public string? AddressCity { get; set; }

        [MapProperty(typeof(B2ComposeCustomer), "Name")]
        public string? CustomerName { get; set; }
    }

    /// <summary>Compose 三表 DTO：t1 内点路径 + t3 直接字段 + 扁平化</summary>
    [MapFrom(typeof(B2ComposeOrder))]
    internal class B2Compose3Dto
    {
        public int Id { get; set; }

        [MapProperty(typeof(B2ComposeOrder), "Address.City")]
        public string? OrderCity { get; set; }

        public string? AddressCity { get; set; }

        [MapProperty(typeof(B2ComposeAddress), "City")]
        public string? AddressCityDirect { get; set; }
    }

    /// <summary>Compose 叶忽略变体</summary>
    [MapFrom(typeof(B2ComposeOrder))]
    internal class B2ComposeIgnoreDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(B2ComposeOrder), "Address.City")]
        public string? OrderCity { get; set; }

        [MapProperty(typeof(B2ComposeCustomer), "Name")]
        public string? CustomerName { get; set; }
    }

    /// <summary>Compose 失败场景变体：路径段缺失 → 静默跳过 + Trace</summary>
    [MapFrom(typeof(B2ComposeOrder))]
    internal class B2ComposeMissingDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(B2ComposeOrder), "Address.Missing")]
        public string? OrderCity { get; set; }
    }

    // ==================== H15：集合元素内路径（EFCore 真实 SQL 子查询） ====================

    /// <summary>
    /// H15（EFCore）：集合元素内点路径 + 元素内推导扁平化值正确（B1-1/B1-2，真实 SQL 子查询）
    /// </summary>
    [Fact]
    public void EFCore_CollectionElement_DotPath_And_Flatten_ValueCorrect()
    {
        var connStr = $"DataSource=file:b2h15{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var db = new B2DbContext(connStr);
        db.Database.EnsureCreated();
        db.Categories.Add(new B2Category { Id = 1, Name = "Tech" });
        db.Categories.Add(new B2Category { Id = 2, Name = "Travel" });
        db.Blogs.Add(new B2Blog { Id = 1 });
        db.Posts.Add(new B2Post { Id = 1, Title = "P1", BlogId = 1, CategoryId = 1 });
        db.Posts.Add(new B2Post { Id = 2, Title = "P2", BlogId = 1, CategoryId = 2 });
        db.SaveChanges();

        var dto = db.Blogs
            .Where(b => b.Id == 1)
            .Select(EfCoreProjectionBuilder.Build<B2Blog, B2BlogDto>())
            .First();

        Assert.Equal(2, dto.Posts.Count);
        Assert.Equal("Tech", dto.Posts[0].City);          // B1-1 集合元素内点路径
        Assert.Equal("Tech", dto.Posts[0].CategoryName);  // B1-2 集合元素内推导扁平化（与顶层同语义）
        Assert.Equal("Travel", dto.Posts[1].City);
        Assert.Equal("Travel", dto.Posts[1].CategoryName);
    }

    /// <summary>
    /// H15（EFCore）：集合元素内路径叶节点命中全局忽略 → 跳过（B1-3）
    /// </summary>
    [Fact]
    public void EFCore_CollectionElement_LeafGlobalIgnore_Skipped()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["Name"] },
            "GtB2H15");

        var connStr = $"DataSource=file:b2h15i{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var db = new B2DbContext(connStr);
        db.Database.EnsureCreated();
        db.Categories.Add(new B2Category { Id = 1, Name = "Tech" });
        db.Blogs.Add(new B2Blog { Id = 1 });
        db.Posts.Add(new B2Post { Id = 1, Title = "P1", BlogId = 1, CategoryId = 1 });
        db.SaveChanges();

        var dto = db.Blogs
            .Where(b => b.Id == 1)
            .Select(EfCoreProjectionBuilder.Build<B2Blog, B2BlogIgnoreDto>())
            .First();

        Assert.Null(dto.Posts[0].City); // 元素内点路径叶 Name 命中忽略清单 → 跳过
    }

    /// <summary>
    /// H15（FreeSql）：集合导航触发降级链 → 终端运行时映射 → 元素内路径值 + [Mask] 消费（B1-4）
    /// </summary>
    [Fact]
    public void FreeSql_Collection_DegradationChain_PathAndMask_Consumed()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_b2h15_{Guid.NewGuid():N}.db");
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"DataSource={dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        try
        {
            fsql.Insert(new B2Category { Id = 1, Name = "Tech" }).ExecuteAffrows();
            fsql.Insert(new B2Blog { Id = 1 }).ExecuteAffrows();
            fsql.Insert(new B2Post { Id = 1, Title = "P1", BlogId = 1, CategoryId = 1 }).ExecuteAffrows();

            // 集合导航 → 降级（FreeSql 不支持集合子查询，既有边界）→ 内存映射
            // 注：自动 Include 对集合元素内导航（Posts.Category）为既有机制边界（FreeSql
            // IncludeByPropertyName 深层集合路径 NRE，阶段 A 机制限制，已登记）——
            // 数据加载由用户显式 IncludeMany（真实使用范式，跳过规则兼容）
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var dtos = fsql.Select<B2Blog>()
                    .IncludeMany(b => b.Posts, then => then.Include(p => p.Category!))
                    .Where(b => b.Id == 1)
                    .AsDto()
                    .ToList<B2BlogDto>();

                var dto = Assert.Single(dtos);
                Assert.Equal("Tech", dto!.Posts[0].City); // 降级链终端：元素内点路径经运行时消费
                Assert.Contains("降级", sw.ToString());     // 降级链确实执行

                // Mask 变体：降级链终端运行时消费（B1 已实现路径 × Mask）
                var dtosMask = fsql.Select<B2Blog>()
                    .IncludeMany(b => b.Posts, then => then.Include(p => p.Category!))
                    .Where(b => b.Id == 1)
                    .AsDto()
                    .ToList<B2BlogMaskDto>();
                var dtoMask = Assert.Single(dtosMask);
                Assert.Equal("MASKED", dtoMask!.Posts[0].City); // 元素内路径 × [Mask] 消费
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }
        finally
        {
            fsql.Dispose();
        }
    }

    // ==================== H25：FreeSql Compose 点路径 ====================

    /// <summary>
    /// H25（FreeSql 双表）：Compose 点路径 + 推导扁平化值正确（B2-1/B2-3；导航自动 JOIN 实证）
    /// </summary>
    [Fact]
    public void FreeSql_Compose2_DotPath_And_Flatten_ValueCorrect()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_b2h25_{Guid.NewGuid():N}.db");
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"DataSource={dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        try
        {
            fsql.Insert(new B2ComposeAddress { Id = 1, City = "Shanghai" }).ExecuteAffrows();
            fsql.Insert(new B2ComposeCustomer { Id = 1, Name = "Alice" }).ExecuteAffrows();
            fsql.Insert(new B2ComposeOrder { Id = 1, Title = "T1", AddressId = 1, CustomerId = 1 }).ExecuteAffrows();

            var query = fsql.Select<B2ComposeOrder, B2ComposeCustomer>()
                .LeftJoin((a, b) => a.CustomerId == b.Id)
                .Where((a, b) => a.Id == 1);

            var dtos = query.AsDto().ToList<B2ComposeDto>();

            var dto = Assert.Single(dtos);
            Assert.Equal("Shanghai", dto!.OrderCity);    // B2-1 Compose 点路径（t1 内导航，FreeSql 自动 JOIN）
            Assert.Equal("Shanghai", dto.AddressCity);   // B2-3 Compose 推导扁平化（AddressCity → Address.City）
            Assert.Equal("Alice", dto.CustomerName);     // 跨表直接字段（现有能力对照）
        }
        finally
        {
            fsql.Dispose();
        }
    }

    /// <summary>
    /// H25（FreeSql 三表）：Compose 三表点路径 + t3 直接字段 + 扁平化（B2-2）
    /// </summary>
    [Fact]
    public void FreeSql_Compose3_DotPath_ValueCorrect()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_b2h25_3_{Guid.NewGuid():N}.db");
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"DataSource={dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        try
        {
            fsql.Insert(new B2ComposeAddress { Id = 1, City = "Shanghai" }).ExecuteAffrows();
            fsql.Insert(new B2ComposeCustomer { Id = 1, Name = "Alice" }).ExecuteAffrows();
            fsql.Insert(new B2ComposeOrder { Id = 1, Title = "T1", AddressId = 1, CustomerId = 1 }).ExecuteAffrows();

            var query = fsql.Select<B2ComposeOrder, B2ComposeCustomer, B2ComposeAddress>()
                .LeftJoin((a, b, c) => a.CustomerId == b.Id)
                .LeftJoin((a, b, c) => a.AddressId == c.Id)
                .Where((a, b, c) => a.Id == 1);

            var dtos = query.AsDto().ToList<B2Compose3Dto>();

            var dto = Assert.Single(dtos);
            Assert.Equal("Shanghai", dto!.OrderCity);       // B2-2 三表点路径（t1 内导航）
            Assert.Equal("Shanghai", dto.AddressCity);      // 三表扁平化
            Assert.Equal("Shanghai", dto.AddressCityDirect); // t3 直接字段（现有能力对照）
        }
        finally
        {
            fsql.Dispose();
        }
    }

    /// <summary>
    /// H25（FreeSql 双表）：Compose 点路径叶节点命中全局忽略 → 跳过（B2-4）
    /// </summary>
    [Fact]
    public void FreeSql_Compose2_LeafGlobalIgnore_Skipped()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["City"] },
            "GtB2H25");

        var dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_b2h25i_{Guid.NewGuid():N}.db");
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"DataSource={dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        try
        {
            fsql.Insert(new B2ComposeAddress { Id = 1, City = "Shanghai" }).ExecuteAffrows();
            fsql.Insert(new B2ComposeCustomer { Id = 1, Name = "Alice" }).ExecuteAffrows();
            fsql.Insert(new B2ComposeOrder { Id = 1, Title = "T1", AddressId = 1, CustomerId = 1 }).ExecuteAffrows();

            var query = fsql.Select<B2ComposeOrder, B2ComposeCustomer>()
                .LeftJoin((a, b) => a.CustomerId == b.Id)
                .Where((a, b) => a.Id == 1);

            var dtos = query.AsDto().ToList<B2ComposeIgnoreDto>();

            var dto = Assert.Single(dtos);
            Assert.Null(dto!.OrderCity);      // Compose 点路径叶 City 命中忽略清单 → 跳过
            Assert.Equal("Alice", dto.CustomerName); // 非忽略字段保留
        }
        finally
        {
            fsql.Dispose();
        }
    }

    /// <summary>
    /// H25（FreeSql 双表）：Compose 点路径段缺失 → 静默跳过 + Trace（B2-5，与单表顶层一致）
    /// </summary>
    [Fact]
    public void FreeSql_Compose2_MissingSegment_Skipped_WithTrace()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_b2h25m_{Guid.NewGuid():N}.db");
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"DataSource={dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        try
        {
            fsql.Insert(new B2ComposeAddress { Id = 1, City = "Shanghai" }).ExecuteAffrows();
            fsql.Insert(new B2ComposeCustomer { Id = 1, Name = "Alice" }).ExecuteAffrows();
            fsql.Insert(new B2ComposeOrder { Id = 1, Title = "T1", AddressId = 1, CustomerId = 1 }).ExecuteAffrows();

            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var query = fsql.Select<B2ComposeOrder, B2ComposeCustomer>()
                    .LeftJoin((a, b) => a.CustomerId == b.Id)
                    .Where((a, b) => a.Id == 1);

                var dtos = query.AsDto().ToList<B2ComposeMissingDto>();

                var dto = Assert.Single(dtos);
                Assert.Null(dto!.OrderCity); // 缺失段 → 静默跳过（fail-closed 留空）
                Assert.Contains("点路径", sw.ToString());
                Assert.Contains("Missing", sw.ToString());
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }
        finally
        {
            fsql.Dispose();
        }
    }
}
