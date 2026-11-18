using Microsoft.EntityFrameworkCore;
using FreeSql;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests.Models;

// ================================================================
// 集合索引路径投影列（Golden Triple 投影列）
// EFCore：内联支持（PoC EF1b/EF2 实证可翻译为 LIMIT 1 子查询，真实 provider 执行）；
// FreeSql：登记边界 + 自动降级（FreeSqlDegradationDetector 检测 FromField 含 '[n]' → 二步映射运行时）。
// ================================================================

public class IdxBlog
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    [global::FreeSql.DataAnnotations.Navigate(nameof(IdxPost.BlogId))]
    public List<IdxPost> Posts { get; set; } = new();
}

public class IdxPost
{
    public int Id { get; set; }
    public int BlogId { get; set; }
    public string Title { get; set; } = "";
    public int? AuthorId { get; set; }

    [global::FreeSql.DataAnnotations.Navigate(nameof(AuthorId))]
    public IdxAuthor? Author { get; set; }
}

public class IdxAuthor
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>S11 DTO：集合索引路径（两段 + 三段）</summary>
[MapFrom(typeof(IdxBlog))]
public class IdxBlogDto
{
    public int Id { get; set; }

    [MapProperty(typeof(IdxBlog), "Posts[0].Title")]
    public string? FirstPostTitle { get; set; }

    [MapProperty(typeof(IdxBlog), "Posts[0].Author.Name")]
    public string? FirstAuthorName { get; set; }
}

public class IdxDbContext : DbContext
{
    public DbSet<IdxBlog> Blogs { get; set; } = null!;
    public DbSet<IdxPost> Posts { get; set; } = null!;
    public DbSet<IdxAuthor> Authors { get; set; } = null!;

    private readonly string _connectionString;

    public IdxDbContext(string connectionString) => _connectionString = connectionString;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlite(_connectionString);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdxBlog>().ToTable("IdxBlogs");
        modelBuilder.Entity<IdxPost>().ToTable("IdxPosts");
        modelBuilder.Entity<IdxAuthor>().ToTable("IdxAuthors");
        modelBuilder.Entity<IdxBlog>()
            .HasMany(b => b.Posts)
            .WithOne()
            .HasForeignKey(p => p.BlogId);
        modelBuilder.Entity<IdxPost>()
            .HasOne(p => p.Author)
            .WithMany()
            .HasForeignKey(p => p.AuthorId);
    }
}

public class CollectionIndexProjectionTests : IDisposable
{
    private readonly IdxDbContext _db;
    private readonly IFreeSql _fsql;
    private readonly string _dbPath;

    public CollectionIndexProjectionTests()
    {
        var connStr = $"DataSource=file:idxproj{Guid.NewGuid():N}?mode=memory&cache=shared";
        _db = new IdxDbContext(connStr);
        _db.Database.EnsureCreated();
        _db.Authors.Add(new IdxAuthor { Id = 1, Name = "Alice Writer" });
        _db.Blogs.Add(new IdxBlog
        {
            Id = 1,
            Title = "Blog One",
            Posts =
            {
                new IdxPost { Id = 11, BlogId = 1, Title = "Post A", AuthorId = 1 },
                new IdxPost { Id = 12, BlogId = 1, Title = "Post B", AuthorId = 1 },
            },
        });
        _db.Blogs.Add(new IdxBlog { Id = 2, Title = "Blog Empty" }); // 空集合
        _db.SaveChanges();

        _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_idxproj_{Guid.NewGuid():N}.db");
        _fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        _fsql.Insert(new IdxAuthor { Id = 1, Name = "Alice Writer" }).ExecuteAffrows();
        _fsql.Insert(new IdxBlog { Id = 1, Title = "Blog One" }).ExecuteAffrows();
        _fsql.Insert(new IdxBlog { Id = 2, Title = "Blog Empty" }).ExecuteAffrows();
        _fsql.Insert(new IdxPost { Id = 11, BlogId = 1, Title = "Post A", AuthorId = 1 }).ExecuteAffrows();
        _fsql.Insert(new IdxPost { Id = 12, BlogId = 1, Title = "Post B", AuthorId = 1 }).ExecuteAffrows();
    }

    public void Dispose()
    {
        _db.Dispose();
        _fsql.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    // ==================== EFCore：内联支持（SQL 翻译 + 真实执行） ====================

    /// <summary>S11-EF1：Nav[0].Prop 投影（LIMIT 1 子查询）→ 值正确</summary>
    [Fact]
    public void EFCore_CollectionIndex_TwoSegment_ValueCorrect()
    {
        var dto = _db.Blogs
            .Where(b => b.Id == 1)
            .Select(EfCoreProjectionBuilder.Build<IdxBlog, IdxBlogDto>())
            .First();

        Assert.Equal(1, dto.Id);
        Assert.NotNull(dto.FirstPostTitle); // SQL 侧无序取一（2 元素场景）
    }

    /// <summary>S11-EF2：Nav[0].Child.Prop 三段索引 → 值正确</summary>
    [Fact]
    public void EFCore_CollectionIndex_ThreeSegment_ValueCorrect()
    {
        var dto = _db.Blogs
            .Where(b => b.Id == 1)
            .Select(EfCoreProjectionBuilder.Build<IdxBlog, IdxBlogDto>())
            .First();

        Assert.Equal("Alice Writer", dto.FirstAuthorName);
    }

    /// <summary>S11-EF3：空集合 → NULL（SQL 侧 LIMIT 1 无匹配行）</summary>
    [Fact]
    public void EFCore_CollectionIndex_EmptyCollection_ReturnsNull()
    {
        var dto = _db.Blogs
            .Where(b => b.Id == 2)
            .Select(EfCoreProjectionBuilder.Build<IdxBlog, IdxBlogDto>())
            .First();

        Assert.Null(dto.FirstPostTitle);
        Assert.Null(dto.FirstAuthorName);
    }

    // ==================== FreeSql：登记边界 + 自动降级（二步映射运行时） ====================

    /// <summary>
    /// S11-FS1：集合索引路径 → 检测器识别 → 自动降级（值正确，不抛 SQL 翻译异常）
    /// <para>支持面：集合索引 + 元素直接属性（Posts[0].Title）；
    /// 元素内导航（Posts[0].Author.Name）登记限制：FreeSql 自动 Include 不支持集合内点分路径
    /// （IncludeByPropertyName NRE 实证 2026-08-16）→ FirstAuthorName 为 null；
    /// 用户可手动 IncludeMany(then.Include) + Mapper.Map 二步映射获得完整值。</para>
    /// </summary>
    [Fact]
    public void FreeSql_CollectionIndex_AutomaticallyDegrades()
    {
        var dtos = _fsql.Select<IdxBlog>()
            .Where(b => b.Id == 1)
            .AsDto()
            .ToList<IdxBlogDto>();

        var dto = Assert.Single(dtos);
        Assert.Equal(1, dto.Id);
        Assert.NotNull(dto.FirstPostTitle); // 降级走内存映射（运行时 ElementAtOrDefault）
        // 登记限制：元素内导航不自动加载（FreeSql IncludeByPropertyName 集合内路径 NRE）
        Assert.Null(dto.FirstAuthorName);
    }
}
