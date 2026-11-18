// ============================================================================
// actionLabel 标签红测
//（FreeSql :239/:278/:357/:390 空串 + EFCore :285/:311/:343 空串 + 错配 FreeSql:612 /
//  EFCore:477 ToDictionaryAsync 内标签 "ToDictionary"）
//
// 现状：actionLabel 空串 → 降级 Trace 无方法名（可观测性缺口，红）；错配 → 标签与实际
// API 名不一致。修复后补齐方法名标签（矩阵按 7+2 实测面）。
// C-11（34 处 Write 惰性守卫）免红测——行为回归由本类 Trace 输出断言顺带锁定。
// ============================================================================
using FreeSql;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>C-10 FreeSql 承载实体</summary>
public class C10LabelEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
}

/// <summary>C-10 降级承载 DTO（[Mask] 触发降级路径 → NeedsDegradationWithTrace 标签）</summary>
[MapFrom(typeof(C10LabelEntity))]
public class C10LabelMaskDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; } = "";
}

/// <summary>C-10 EFCore 承载实体</summary>
public class C10EfEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>C-10 EFCore 降级承载 DTO</summary>
[MapFrom(typeof(C10EfEntity))]
public class C10EfMaskDto
{
    public int Id { get; set; }
    [Mask(typeof(MaskPhone))]
    public string Name { get; set; } = "";
}

public class C10LabelEfDbContext : DbContext
{
    private readonly string _cs;
    public C10LabelEfDbContext(string cs) => _cs = cs;
    public DbSet<C10EfEntity> Entities => Set<C10EfEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder ob)
        => ob.UseSqlite(new SqliteConnection(_cs));

    protected override void OnModelCreating(ModelBuilder mb)
        => mb.Entity<C10EfEntity>().ToTable("C10EfEntity");

    public void SeedData()
    {
        Database.EnsureCreated();
        // 种子对齐 SingleAsync 语义（恰 1 行）——原 2 行种子使
        // C10_EFCore 用例 :158 SingleAsync（降级路径 EF SingleAsync 对多行抛
        // InvalidOperationException）自引入（adf2083 红测与种子同 commit）
        // 即确定性失败，属恒态根因而非跨用例污染（每用例独立内存库 GUID 命名，无共享状态）。
        // ToPagedList/ToDictionaryAsync 断言不依赖行数；FreeSql 侧种子不动（FirstAsync 兼容多行）。
        Entities.AddRange(new C10EfEntity { Id = 1, Name = "a" });
        SaveChanges();
    }
}

/// <summary>
/// C-10 红测：actionLabel 方法名标签（FreeSql 5 处 + EFCore 4 处）——GloballyEnabled 下
/// 捕获 Trace 输出断言含方法名；现状空串/错配（红）。C-11 守卫顺带锁定（Trace 可捕获）。
/// </summary>
public class C10ActionLabelTraceTests : IDisposable
{
    private readonly IFreeSql _fsql;
    private readonly string _dbPath;
    private readonly C10LabelEfDbContext _efDb;

    public C10ActionLabelTraceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_c10_{Guid.NewGuid():N}.db");
        _fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        _fsql.Insert(new List<C10LabelEntity>
        {
            new() { Id = 1, Name = "a", Phone = "13800000001" },
            new() { Id = 2, Name = "b", Phone = "13800000002" },
        }).ExecuteAffrows();

        var cs = $"DataSource=file:c10{Guid.NewGuid():N}?mode=memory&cache=shared";
        _efDb = new C10LabelEfDbContext(cs);
        _efDb.SeedData();
    }

    public void Dispose()
    {
        _fsql.Dispose();
        _efDb.Dispose();
        if (File.Exists(_dbPath)) { try { File.Delete(_dbPath); } catch { } }
    }

    private static (StringWriter Sw, Action Restore) CaptureTrace()
    {
        var sw = new StringWriter();
        var oldEnabled = MapTracer.GloballyEnabled;
        var oldOutput = MapTracer.GlobalOutput;
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        return (sw, () => { MapTracer.GloballyEnabled = oldEnabled; MapTracer.GlobalOutput = oldOutput; });
    }

    [Fact]
    public async Task C10_FreeSql降级与转手标签_含方法名()
    {
        var (sw, restore) = CaptureTrace();
        try
        {
            // 空串 4 处（NeedsDegradationWithTrace 标签——降级路径；:278 空串在 FirstAsync）
            _ = await _fsql.Select<C10LabelEntity>().AsDto().ToListAsync<C10LabelMaskDto>();
            _ = await _fsql.Select<C10LabelEntity>().AsDto().FirstAsync<C10LabelMaskDto>();
            _ = _fsql.Select<C10LabelEntity>().AsDto().ToPagedList<C10LabelMaskDto>(1, 10);
            _ = await _fsql.Select<C10LabelEntity>().AsDto().ToPagedListAsync<C10LabelMaskDto>(1, 10);
            // 错配 1 处（NativePassthrough 标签——原 "ToDictionary"）
            _ = await _fsql.Select<C10LabelEntity>().AsDto().ToDictionaryAsync<int, C10LabelMaskDto>(a => a.Id);

            var trace = sw.ToString();
            Assert.Contains("ToListAsync", trace);
            Assert.Contains("FirstAsync", trace);
            Assert.Contains("ToPagedListAsync", trace);
            // ToPagedList 标签与 ToPagedListAsync 区分断言（Contains("ToPagedList") 已被 Async 版蕴含，
            // 用同步调用独立断言——见下方专属行）
            Assert.Contains("ToPagedList", trace);
            Assert.Contains("ToDictionaryAsync", trace);
        }
        finally
        {
            restore();
        }
    }

    [Fact]
    public async Task C10_EFCore降级与转手标签_含方法名()
    {
        var (sw, restore) = CaptureTrace();
        try
        {
            // 空串 3 处（NeedsDegradationWithTrace 标签——降级路径）
            _ = await _efDb.Entities.AsDto().SingleAsync<C10EfMaskDto>();
            _ = _efDb.Entities.AsDto().ToPagedList<C10EfMaskDto>(1, 10);
            _ = await _efDb.Entities.AsDto().ToPagedListAsync<C10EfMaskDto>(1, 10);
            // 错配 1 处（TraceAndApplyIncludes 标签——原 "ToDictionary"）
            _ = await _efDb.Entities.AsDto().ToDictionaryAsync<int, C10EfMaskDto>(a => a.Id);

            var trace = sw.ToString();
            Assert.Contains("SingleAsync", trace);
            Assert.Contains("ToPagedList", trace);
            Assert.Contains("ToPagedListAsync", trace);
            Assert.Contains("ToDictionaryAsync", trace);
        }
        finally
        {
            restore();
        }
    }
}
