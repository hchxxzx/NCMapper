using Microsoft.EntityFrameworkCore;
using FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests.Models;

// ================================================================
// PoC：集合索引路径 A.B[0].C 投影 SQL 翻译实证
// 目的：验证 EFCore/FreeSql 对集合导航索引访问的 LINQ→SQL 翻译能力，
//       决定 T4 走向（内联/降级/登记边界）。本 PoC 直接构造 LINQ（不经
//       NCMapper 投影构建器，构建器尚未支持索引段），实证底层 provider 翻译边界。
//
// PoC 结论（2026-08-16）：
//   EFCore：Nav[0].Prop / Nav[0].Child.Prop / ElementAtOrDefault 形态均可翻译为
//           SQL 并真实执行（SQLite）→ T4 内联支持具备实证基础；
//   FreeSql：集合导航索引访问不可翻译（Column not found）→ FreeSql 侧登记边界。
// ================================================================

public class CiPocBlog
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    public List<CiPocPost> Posts { get; set; } = new();
}

public class CiPocPost
{
    public int Id { get; set; }
    public int BlogId { get; set; }
    public string Title { get; set; } = "";
    public int? AuthorId { get; set; }
    public CiPocAuthor? Author { get; set; }
}

public class CiPocAuthor
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>EFCore PoC DbContext（SQLite 内存库）</summary>
public class CiPocDbContext : DbContext
{
    public DbSet<CiPocBlog> Blogs { get; set; } = null!;
    public DbSet<CiPocPost> Posts { get; set; } = null!;
    public DbSet<CiPocAuthor> Authors { get; set; } = null!;

    private readonly string _connectionString;

    public CiPocDbContext(string connectionString) => _connectionString = connectionString;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlite(_connectionString);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CiPocBlog>().ToTable("CiPocBlogs");
        modelBuilder.Entity<CiPocPost>().ToTable("CiPocPosts");
        modelBuilder.Entity<CiPocAuthor>().ToTable("CiPocAuthors");
        modelBuilder.Entity<CiPocBlog>()
            .HasMany(b => b.Posts)
            .WithOne()
            .HasForeignKey(p => p.BlogId);
        modelBuilder.Entity<CiPocPost>()
            .HasOne(p => p.Author)
            .WithMany()
            .HasForeignKey(p => p.AuthorId);
    }
}

public class CollectionIndexProjectionPocTests : IDisposable
{
    /// <summary>PoC SQL 实证落盘（临时目录，便于人工查看翻译产物）</summary>
    private static readonly string SqlLogPath =
        Path.Combine(Path.GetTempPath(), "ncmapper_cipoc_sql.txt");

    private readonly CiPocDbContext _db;
    private readonly IFreeSql _fsql;
    private readonly string _dbPath;

    public CollectionIndexProjectionPocTests()
    {
        var connStr = $"DataSource=file:cipoc{Guid.NewGuid():N}?mode=memory&cache=shared";
        _db = new CiPocDbContext(connStr);
        _db.Database.EnsureCreated();
        _db.Authors.Add(new CiPocAuthor { Id = 1, Name = "Alice Writer" });
        _db.Blogs.Add(new CiPocBlog
        {
            Id = 1,
            Title = "Blog One",
            Posts =
            {
                new CiPocPost { Id = 11, BlogId = 1, Title = "Post A", AuthorId = 1 },
                new CiPocPost { Id = 12, BlogId = 1, Title = "Post B", AuthorId = 1 },
            },
        });
        _db.Blogs.Add(new CiPocBlog { Id = 2, Title = "Blog Empty" }); // 空集合
        _db.SaveChanges();

        _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_cipoc_{Guid.NewGuid():N}.db");
        _fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        _fsql.Insert(new CiPocAuthor { Id = 1, Name = "Alice Writer" }).ExecuteAffrows();
        _fsql.Insert(new CiPocBlog { Id = 1, Title = "Blog One" }).ExecuteAffrows();
        _fsql.Insert(new CiPocBlog { Id = 2, Title = "Blog Empty" }).ExecuteAffrows();
        _fsql.Insert(new CiPocPost { Id = 11, BlogId = 1, Title = "Post A", AuthorId = 1 }).ExecuteAffrows();
        _fsql.Insert(new CiPocPost { Id = 12, BlogId = 1, Title = "Post B", AuthorId = 1 }).ExecuteAffrows();
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

    private static void Log(string text)
    {
        try { File.AppendAllText(SqlLogPath, text + Environment.NewLine); } catch { }
    }

    /// <summary>
    /// PoC-EF1：EFCore Nav[0].Prop（索引器形态）— SQL 生成 + 真实执行 + 值语义
    /// </summary>
    [Fact]
    public void EFCore_NavIndex_Property_Translates()
    {
        IQueryable<string?> q = _db.Blogs.Where(b => b.Id == 1).Select(b => b.Posts[0].Title);
        var sql = q.ToQueryString();
        Log($"[EF1] Nav[0].Prop SQL: {sql}");

        var value = q.First();
        Assert.NotNull(value); // SQL 侧无序，2 元素场景取到其一即可证明可执行
    }

    /// <summary>
    /// PoC-EF1b：EFCore ElementAtOrDefault 形态（NCMapper 生成端候选）— 翻译实证
    /// </summary>
    [Fact]
    public void EFCore_NavIndex_ElementAtOrDefault_TranslationProbe()
    {
        IQueryable<string?> q = _db.Blogs.Where(b => b.Id == 1)
            .Select(b => b.Posts.ElementAtOrDefault(0)!.Title);

        string? error = null;
        try
        {
            _ = q.ToQueryString();
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
        }

        Log(error ?? "[EF1b] ElementAtOrDefault 可翻译");
        Assert.True(error == null, $"EFCore ElementAtOrDefault 翻译失败 —— {error}");
    }

    /// <summary>
    /// PoC-EF2：EFCore Nav[0].Child.Prop（索引 + 导航 + 属性）— SQL 生成 + 真实执行
    /// </summary>
    [Fact]
    public void EFCore_NavIndex_NestedNav_Translates()
    {
        IQueryable<string?> q = _db.Blogs.Where(b => b.Id == 1).Select(b => b.Posts[0].Author!.Name);
        var sql = q.ToQueryString();
        Log($"[EF2] Nav[0].Child.Prop SQL: {sql}");

        var value = q.First();
        Assert.NotNull(value);
    }

    /// <summary>
    /// PoC-EF3（对照）：EFCore 集合元素投影（Select 子查询）翻译基线 —— B2 既有能力
    /// </summary>
    [Fact]
    public void EFCore_NavElementProjection_Translates()
    {
        IQueryable<string?> q = _db.Blogs.Where(b => b.Id == 1)
            .Select(b => b.Posts.Select(p => p.Title).FirstOrDefault());
        var sql = q.ToQueryString();
        Log($"[EF3] 对照 Select+FirstOrDefault SQL: {sql}");
        Assert.NotNull(sql);
    }

    /// <summary>
    /// PoC-FS1：FreeSql 集合导航索引访问 — 预期不可翻译（Column not found），实证登记边界依据
    /// </summary>
    [Fact]
    public void FreeSql_NavIndex_NotTranslatable()
    {
        string? error = null;
        try
        {
            _ = _fsql.Select<CiPocBlog>()
                .Where(b => b.Id == 1)
                .ToOne(x => x.Posts[0].Title);
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
        }

        Log(error ?? "[FS1] FreeSql 可翻译（意外）");
        // 实证结论：FreeSql 集合导航索引不可翻译（Posts 被当列名解析）→ T4 登记边界
        Assert.True(error != null, "FreeSql 集合导航索引可翻译（与预期不符，需重新评估 T4）");
    }
}
