using System.IO;
using FreeSql;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.IntegrationTests.Models;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// 点路径 Golden Triple 正式套件 — 投影引擎列
/// <para>矩阵定位：7 场景 × 4 引擎（源生成 / 运行时 / 投影 / Compose-SourceGen）防漂移锚。
/// 本类 = 投影列（EFCore + FreeSql 双 provider，SQLite 真实 provider 执行）；
/// 源生成与 Compose-SourceGen 列见 NCMapper.SourceGen.Tests.DotPathGoldenTripleTests；
/// 运行时列见 NCMapper.Tests.DotPathGoldenTripleTests。</para>
/// <para>provider 约束（本任务书）：中间节点 null 断言必须走真实 provider
/// （SQLite/InMemory 集成），禁止对投影表达式直接 Compile 断言（编译执行中间 null 会 NRE）。</para>
/// <para>场景覆盖：S1 合法两段路径值相等；S2 缺失段投影跳过（属性 null + Trace）；S3 中间节点值类型中断跳过；
/// S4 叶节点命中全局忽略跳过；S5 中间节点 null → NULL 列不抛（EFCore 用可空 FK 专用模型——非可空 FK 的
/// BlogEntity 被 EF 翻译为 INNER JOIN，孤儿行被过滤，无法表达 Author=null；FreeSql 用孤儿行，LEFT JOIN 语义）；
/// 目标带 [Mask] 降级链（RequiresDowngrade → 二步映射 → 运行时消费脱敏生效）
/// 嵌套 DTO 内路径（嵌套内点路径 / 嵌套内推导扁平化 / 嵌套内叶节点忽略）。仅运行时。</para>
/// <para>隔离范式同 ProjectionIgnoreFlattenTests：[Collection("PipelineGlobalState")] + 构造器/Dispose 双 Mapper.ClearCache()
/// （含投影缓存清理链；进程级串行由 AssemblyInfo DisableTestParallelization 保证）。</para>
/// </summary>
[Collection("PipelineGlobalState")]
public class DotPathGoldenTripleTests : IDisposable
{
    // ==================== 投影列模型（Blog 类型对复用 Models.cs G7 fixture） ====================

    /// <summary>S1 合法两段路径：Author.Name</summary>
    [MapFrom(typeof(BlogEntity))]
    internal class GtBlogDotPathDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(BlogEntity), "Author.Name")]
        public string? AuthorName { get; set; }
    }

    /// <summary>S2 缺失段：Author.Missing → 投影跳过</summary>
    [MapFrom(typeof(BlogEntity))]
    internal class GtBlogDotPathMissingDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(BlogEntity), "Author.Missing")]
        public string? AuthorName { get; set; }
    }

    /// <summary>S3 值类型中断：AuthorId.Name（int 不可导航）→ 投影跳过</summary>
    [MapFrom(typeof(BlogEntity))]
    internal class GtBlogDotPathValueTypeDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(BlogEntity), "AuthorId.Name")]
        public string? AuthorName { get; set; }
    }

    /// <summary>S4 叶节点命中全局忽略：叶 Name 命中 IgnoreNames</summary>
    [MapFrom(typeof(BlogEntity))]
    internal class GtBlogDotPathIgnoreDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(BlogEntity), "Author.Name")]
        public string? AuthorName { get; set; }
    }

    /// <summary>S6 目标带 [Mask]：RequiresDowngrade → 二步映射 → 运行时消费（脱敏生效）</summary>
    [MapFrom(typeof(BlogEntity))]
    internal class GtBlogDotPathMaskDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(BlogEntity), "Author.Name")]
        [Mask(typeof(GtBlogMask))]
        public string? AuthorName { get; set; }
    }

    /// <summary>
    /// S6 专用 Mask — 若被消费则输出固定值 "MASKED"，未被消费则保持明文
    /// </summary>
    public sealed class GtBlogMask : IMask
    {
        public string Mask(string? value) => "MASKED";
    }

    // ==================== S5 专用模型（EFCore 可空 FK，Author 导航可为 null） ====================

    public class GtS5Author
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    public class GtS5Blog
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public int? AuthorId { get; set; }
        public GtS5Author? Author { get; set; }
    }

    /// <summary>独立 DbContext — 可空外键模型（避免侵入共享 TestDbContext）</summary>
    private sealed class GtS5DbContext : DbContext
    {
        public DbSet<GtS5Blog> Blogs { get; set; } = null!;
        public DbSet<GtS5Author> Authors { get; set; } = null!;

        private readonly string _connectionString;

        public GtS5DbContext(string connectionString) => _connectionString = connectionString;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite(_connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GtS5Author>().ToTable("S5Authors");
            modelBuilder.Entity<GtS5Blog>().ToTable("S5Blogs");
            modelBuilder.Entity<GtS5Blog>()
                .HasOne(b => b.Author)
                .WithMany()
                .HasForeignKey(b => b.AuthorId);
        }
    }

    [MapFrom(typeof(GtS5Blog))]
    internal class GtS5BlogDotPathDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(GtS5Blog), "Author.Name")]
        public string? AuthorName { get; set; }
    }

    // ==================== 生命周期 ====================

    private readonly TestDbContext _db;
    private readonly IFreeSql _fsql;
    private readonly string _dbPath;

    public DotPathGoldenTripleTests()
    {
        Mapper.ClearCache();
        GlobalMapConfig.ResetForTests(); // 迁移注册表隔离

        // EFCore：SQLite 内存库（memory+shared）
        var connStr = $"DataSource=file:gtproj{Guid.NewGuid():N}?mode=memory&cache=shared";
        _db = new TestDbContext(connStr);
        _db.SeedData();

        // FreeSql：SQLite 临时文件库（AutoSyncStructure 建表不建 FK 引用，孤儿行可直接插入）
        _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_gtproj_{Guid.NewGuid():N}.db");
        _fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        _fsql.Insert(new AuthorEntity { Id = 1, Name = "Alice Writer", Email = "alice@blog.com" }).ExecuteAffrows();
        _fsql.Insert(new BlogEntity { Id = 1, Title = "NCMapper 入门指南", AuthorId = 1 }).ExecuteAffrows();
        // S5 专用（FreeSql）：无匹配作者的孤儿行（LEFT JOIN 语义下 Author 导航 null）
        _fsql.Insert(new BlogEntity { Id = 99, Title = "Orphan Blog", AuthorId = 999 }).ExecuteAffrows();
    }

    public void Dispose()
    {
        Mapper.ClearCache();
        GlobalMapConfig.ResetForTests(); // 注册表隔离
        _db.Dispose();
        _fsql.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    // ==================== S1：合法两段路径值相等（真实 provider 执行） ====================

    /// <summary>
    /// 场景 1（EFCore）：Author.Name 投影 → "Alice Writer"（SQL 翻译 + LEFT JOIN 真实执行）
    /// </summary>
    [Fact]
    public void EFCore_S1_TwoSegmentPath_ValueCorrect()
    {
        var dto = _db.Blogs
            .Where(b => b.Id == 1)
            .Select(EfCoreProjectionBuilder.Build<BlogEntity, GtBlogDotPathDto>())
            .First();

        Assert.Equal("Alice Writer", dto.AuthorName);
        Assert.Equal(1, dto.Id);
    }

    /// <summary>
    /// 场景 1（FreeSql）：Author.Name 投影 → "Alice Writer"
    /// </summary>
    [Fact]
    public void FreeSql_S1_TwoSegmentPath_ValueCorrect()
    {
        var expr = FreeSqlProjectionBuilder.Build<BlogEntity, GtBlogDotPathDto>();
        var dto = _fsql.Select<BlogEntity>().Where(b => b.Id == 1).ToList(expr).First();

        Assert.Equal("Alice Writer", dto.AuthorName);
        Assert.Equal(1, dto.Id);
    }

    // ==================== S2：缺失段 — 投影跳过（属性留空）+ Trace 可观测 ====================

    /// <summary>
    /// 场景 2（EFCore）：Author.Missing → 构建期分段失败 Trace + 属性不映射（null），SQL 正常执行
    /// </summary>
    [Fact]
    public void EFCore_S2_MissingSegment_Skipped_WithTrace()
    {
        var sw = new StringWriter();
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            var dto = _db.Blogs
                .Where(b => b.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<BlogEntity, GtBlogDotPathMissingDto>())
                .First();

            Assert.Null(dto.AuthorName); // 缺失段 → 投影跳过（fail-closed 留空）
            Assert.Equal(1, dto.Id);
            Assert.Contains("点路径", sw.ToString());
            Assert.Contains("Missing", sw.ToString());
        }
        finally
        {
            MapTracer.GloballyEnabled = false;
            MapTracer.GlobalOutput = Console.Out;
        }
    }

    /// <summary>
    /// 场景 2（FreeSql）：Author.Missing → 跳过 + Trace
    /// </summary>
    [Fact]
    public void FreeSql_S2_MissingSegment_Skipped_WithTrace()
    {
        var sw = new StringWriter();
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            var expr = FreeSqlProjectionBuilder.Build<BlogEntity, GtBlogDotPathMissingDto>();
            var dto = _fsql.Select<BlogEntity>().Where(b => b.Id == 1).ToList(expr).First();

            Assert.Null(dto.AuthorName);
            Assert.Equal(1, dto.Id);
            Assert.Contains("点路径", sw.ToString());
            Assert.Contains("Missing", sw.ToString());
        }
        finally
        {
            MapTracer.GloballyEnabled = false;
            MapTracer.GlobalOutput = Console.Out;
        }
    }

    // ==================== S3：中间节点值类型中断 — 投影跳过 ====================

    /// <summary>
    /// 场景 3（EFCore）：AuthorId.Name（int 中断）→ 构建期中断 Trace + 属性不映射（null）
    /// </summary>
    [Fact]
    public void EFCore_S3_ValueTypeIntermediate_Skipped_WithTrace()
    {
        var sw = new StringWriter();
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            var dto = _db.Blogs
                .Where(b => b.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<BlogEntity, GtBlogDotPathValueTypeDto>())
                .First();

            Assert.Null(dto.AuthorName); // 值类型中间节点 → 投影跳过
            Assert.Equal(1, dto.Id);
            Assert.Contains("中断", sw.ToString());
            Assert.Contains("AuthorId", sw.ToString());
        }
        finally
        {
            MapTracer.GloballyEnabled = false;
            MapTracer.GlobalOutput = Console.Out;
        }
    }

    /// <summary>
    /// 场景 3（FreeSql）：AuthorId.Name（int 中断）→ 跳过 + Trace
    /// </summary>
    [Fact]
    public void FreeSql_S3_ValueTypeIntermediate_Skipped_WithTrace()
    {
        var sw = new StringWriter();
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            var expr = FreeSqlProjectionBuilder.Build<BlogEntity, GtBlogDotPathValueTypeDto>();
            var dto = _fsql.Select<BlogEntity>().Where(b => b.Id == 1).ToList(expr).First();

            Assert.Null(dto.AuthorName);
            Assert.Equal(1, dto.Id);
            Assert.Contains("中断", sw.ToString());
            Assert.Contains("AuthorId", sw.ToString());
        }
        finally
        {
            MapTracer.GloballyEnabled = false;
            MapTracer.GlobalOutput = Console.Out;
        }
    }

    // ==================== S4：叶节点命中全局忽略 — 投影跳过（不触发降级） ====================

    /// <summary>
    /// 场景 4（EFCore）：IgnoreNames=["Name"] → 叶节点 Name 命中 → 属性不映射（null）
    /// </summary>
    [Fact]
    public void EFCore_S4_LeafGlobalIgnore_Skipped()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["Name"] },
            "GtProjGoldenTriple");

        var dto = _db.Blogs
            .Where(b => b.Id == 1)
            .Select(EfCoreProjectionBuilder.Build<BlogEntity, GtBlogDotPathIgnoreDto>())
            .First();

        Assert.Null(dto.AuthorName); // 叶命中忽略清单 → 不映射（与源生成器/运行时一致）
        Assert.Equal(1, dto.Id);
    }

    /// <summary>
    /// 场景 4（FreeSql）：IgnoreNames=["Name"] → 叶节点命中 → 属性不映射（null）
    /// </summary>
    [Fact]
    public void FreeSql_S4_LeafGlobalIgnore_Skipped()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["Name"] },
            "GtProjGoldenTriple");

        var expr = FreeSqlProjectionBuilder.Build<BlogEntity, GtBlogDotPathIgnoreDto>();
        var dto = _fsql.Select<BlogEntity>().Where(b => b.Id == 1).ToList(expr).First();

        Assert.Null(dto.AuthorName);
        Assert.Equal(1, dto.Id);
    }

    // ==================== S5：中间节点 null → NULL 列不抛（真实 provider，禁止 Compile 断言） ====================

    /// <summary>
    /// 场景 5（EFCore）：可空 FK（AuthorId=null）→ Author 导航 null → SQL 翻译下中间 null → NULL 列，不抛
    /// <para>注：BlogEntity.AuthorId 为非可空 FK，EF 翻译为 INNER JOIN（孤儿行被过滤），故用可空 FK 专用模型表达。</para>
    /// </summary>
    [Fact]
    public void EFCore_S5_IntermediateNull_NullColumn_NoThrow()
    {
        var connStr = $"DataSource=file:gts5{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var db = new GtS5DbContext(connStr);
        db.Database.EnsureCreated();
        db.Authors.Add(new GtS5Author { Id = 1, Name = "Alice Writer", Email = "a@b.c" });
        db.Blogs.Add(new GtS5Blog { Id = 1, Title = "With Author", AuthorId = 1 });
        db.Blogs.Add(new GtS5Blog { Id = 2, Title = "No Author", AuthorId = null });
        db.SaveChanges();

        var dto = db.Blogs
            .Where(b => b.Id == 2)
            .Select(EfCoreProjectionBuilder.Build<GtS5Blog, GtS5BlogDotPathDto>())
            .First();

        Assert.Null(dto.AuthorName); // 中间节点 null → NULL 列（C7 投影措辞）
        Assert.Equal(2, dto.Id);
    }

    /// <summary>
    /// 场景 5（FreeSql）：孤儿行（AuthorId=999 无匹配 Author，LEFT JOIN 语义）→ NULL 列不抛
    /// </summary>
    [Fact]
    public void FreeSql_S5_IntermediateNull_NullColumn_NoThrow()
    {
        var expr = FreeSqlProjectionBuilder.Build<BlogEntity, GtBlogDotPathDto>();
        var dto = _fsql.Select<BlogEntity>().Where(b => b.Id == 99).ToList(expr).First();

        Assert.Null(dto.AuthorName);
        Assert.Equal(99, dto.Id);
    }

    // ==================== S6：目标带 [Mask] — 降级链（RequiresDowngrade → 二步映射 → 脱敏生效） ====================

    /// <summary>
    /// 场景 6（EFCore）：AsDto 检测 [Mask]（RequiresDowngrade）→ 降级二步映射 → 运行时消费脱敏 → "MASKED"
    /// <para>脱敏消费由运行时列 S6_Mask_Consumed_MaskedValue（NCMapper.Tests）稳定锁定；本列锁定降级路径 Trace + 脱敏值语义。</para>
    /// </summary>
    [Fact]
    public void EFCore_S6_Mask_Downgrade_MaskedValue()
    {
        var sw = new StringWriter();
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            var dtos = _db.Blogs
                .Where(b => b.Id == 1)
                .AsDto()
                .ToList<GtBlogDotPathMaskDto>();

            var dto = Assert.Single(dtos);
            Assert.Equal("MASKED", dto!.AuthorName); // 脱敏生效——点路径 × Mask 消费（H6）
            Assert.Contains("降级", sw.ToString()); // 降级链确实执行（RequiresDowngrade → 二步映射）
        }
        finally
        {
            MapTracer.GloballyEnabled = false;
            MapTracer.GlobalOutput = Console.Out;
        }
    }

    /// <summary>
    /// 场景 6（FreeSql）：AsDto 检测 [Mask] → 降级二步映射 → 运行时消费脱敏 → "MASKED"
    /// </summary>
    [Fact]
    public void FreeSql_S6_Mask_Downgrade_MaskedValue()
    {
        var sw = new StringWriter();
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            var dtos = _fsql.Select<BlogEntity>()
                .Where(b => b.Id == 1)
                .AsDto()
                .ToList<GtBlogDotPathMaskDto>();

            var dto = Assert.Single(dtos);
            Assert.Equal("MASKED", dto!.AuthorName); // 脱敏生效——点路径 × Mask 消费（H6）
            Assert.Contains("降级", sw.ToString()); // 降级链确实执行（RequiresDowngrade → 二步映射）
        }
        finally
        {
            MapTracer.GloballyEnabled = false;
            MapTracer.GlobalOutput = Console.Out;
        }
    }

    // ==================== 嵌套 DTO 内路径 ====================

    /// <summary>S8 专用模型：Blog → Author → Profile 两级导航（嵌套内路径用例）</summary>
    public class GtNProfile
    {
        public int Id { get; set; }
        public string City { get; set; } = "";
    }

    public class GtNAuthor
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int ProfileId { get; set; }
        public GtNProfile? Profile { get; set; }
    }

    public class GtNBlog
    {
        public int Id { get; set; }
        public int AuthorId { get; set; }
        public GtNAuthor? Author { get; set; }
    }

    /// <summary>S8 嵌套 DTO：嵌套内点路径（Profile.City）+ 嵌套内推导扁平化（ProfileCity → Profile.City）</summary>
    [MapFrom(typeof(GtNAuthor))]
    internal class GtNAuthorDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(GtNAuthor), "Profile.City")]
        public string? City { get; set; }

        public string? ProfileCity { get; set; }
    }

    /// <summary>S8 嵌套 DTO（叶忽略变体）：嵌套内点路径叶 City 命中忽略 → 跳过</summary>
    [MapFrom(typeof(GtNAuthor))]
    internal class GtNAuthorIgnoreDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(GtNAuthor), "Profile.City")]
        public string? City { get; set; }
    }

    [MapFrom(typeof(GtNBlog))]
    internal class GtNBlogDto
    {
        public int Id { get; set; }
        public GtNAuthorDto? Author { get; set; }
    }

    [MapFrom(typeof(GtNBlog))]
    internal class GtNBlogIgnoreDto
    {
        public int Id { get; set; }
        public GtNAuthorIgnoreDto? Author { get; set; }
    }

    /// <summary>S8 独立 DbContext — Blog → Author → Profile 两级导航</summary>
    private sealed class GtNDbContext : DbContext
    {
        public DbSet<GtNBlog> Blogs { get; set; } = null!;
        public DbSet<GtNAuthor> Authors { get; set; } = null!;
        public DbSet<GtNProfile> Profiles { get; set; } = null!;

        private readonly string _connectionString;

        public GtNDbContext(string connectionString) => _connectionString = connectionString;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite(_connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GtNProfile>().ToTable("NProfiles");
            modelBuilder.Entity<GtNAuthor>().ToTable("NAuthors");
            modelBuilder.Entity<GtNBlog>().ToTable("NBlogs");
            modelBuilder.Entity<GtNBlog>()
                .HasOne(b => b.Author)
                .WithMany()
                .HasForeignKey(b => b.AuthorId);
            modelBuilder.Entity<GtNAuthor>()
                .HasOne(a => a.Profile)
                .WithMany()
                .HasForeignKey(a => a.ProfileId);
        }
    }

    /// <summary>
    /// 场景 8（EFCore）：嵌套 DTO 内路径——嵌套内点路径 + 嵌套内推导扁平化值正确（A3-1/A3-2）
    /// </summary>
    [Fact]
    public void EFCore_S8_NestedDotPath_And_Flatten_ValueCorrect()
    {
        var connStr = $"DataSource=file:gts8{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var db = new GtNDbContext(connStr);
        db.Database.EnsureCreated();
        db.Profiles.Add(new GtNProfile { Id = 1, City = "Shanghai" });
        db.Authors.Add(new GtNAuthor { Id = 1, Name = "Alice Writer", ProfileId = 1 });
        db.Blogs.Add(new GtNBlog { Id = 1, AuthorId = 1 });
        db.SaveChanges();

        var dto = db.Blogs
            .Where(b => b.Id == 1)
            .Select(EfCoreProjectionBuilder.Build<GtNBlog, GtNBlogDto>())
            .First();

        Assert.Equal("Shanghai", dto.Author!.City); // A3-1 嵌套内点路径
        Assert.Equal("Shanghai", dto.Author.ProfileCity); // A3-2 嵌套内推导扁平化（与顶层同语义）
    }

    /// <summary>
    /// 场景 8（EFCore）：嵌套 DTO 内路径叶节点命中全局忽略 → 跳过（属性留空，A3-3）
    /// </summary>
    [Fact]
    public void EFCore_S8_NestedDotPath_LeafGlobalIgnore_Skipped()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["City"] },
            "GtNestedProj");

        var connStr = $"DataSource=file:gts8i{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var db = new GtNDbContext(connStr);
        db.Database.EnsureCreated();
        db.Profiles.Add(new GtNProfile { Id = 1, City = "Shanghai" });
        db.Authors.Add(new GtNAuthor { Id = 1, Name = "Alice Writer", ProfileId = 1 });
        db.Blogs.Add(new GtNBlog { Id = 1, AuthorId = 1 });
        db.SaveChanges();

        var dto = db.Blogs
            .Where(b => b.Id == 1)
            .Select(EfCoreProjectionBuilder.Build<GtNBlog, GtNBlogIgnoreDto>())
            .First();

        Assert.Null(dto.Author!.City); // 嵌套内点路径叶 City 命中忽略清单 → 跳过
    }

    /// <summary>
    /// 场景 8（FreeSql）：嵌套 DTO 内路径表达式构建正确——嵌套内点路径 + 嵌套内推导扁平化
    /// <para>执行范式同 FreeSqlProjectionContractTests 嵌套契约用例：FreeSql SQL 翻译器不支持嵌套 DTO
    /// MemberInit 投影（既有边界，非本任务引入），此处 Compile 内存执行验证表达式构建与值正确。</para>
    /// </summary>
    [Fact]
    public void FreeSql_S8_NestedDotPath_And_Flatten_ValueCorrect()
    {
        var entity = new GtNBlog
        {
            Id = 1,
            Author = new GtNAuthor { Id = 1, Name = "Alice Writer", Profile = new GtNProfile { Id = 1, City = "Shanghai" } }
        };

        var expr = FreeSqlProjectionBuilder.Build<GtNBlog, GtNBlogDto>();
        var dto = expr.Compile()(entity);

        Assert.Equal("Shanghai", dto.Author!.City); // A3-1 嵌套内点路径
        Assert.Equal("Shanghai", dto.Author.ProfileCity); // A3-2 嵌套内推导扁平化
    }

    /// <summary>
    /// 场景 8（FreeSql）：嵌套 DTO 内路径叶节点命中全局忽略 → 跳过（A3-3，Compile 内存执行）
    /// </summary>
    [Fact]
    public void FreeSql_S8_NestedDotPath_LeafGlobalIgnore_Skipped()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["City"] },
            "GtNestedProj");

        var entity = new GtNBlog
        {
            Id = 1,
            Author = new GtNAuthor { Id = 1, Name = "Alice Writer", Profile = new GtNProfile { Id = 1, City = "Shanghai" } }
        };

        var expr = FreeSqlProjectionBuilder.Build<GtNBlog, GtNBlogIgnoreDto>();
        var dto = expr.Compile()(entity);

        Assert.Null(dto.Author!.City); // 嵌套内点路径叶 City 命中忽略清单 → 跳过
    }
}
