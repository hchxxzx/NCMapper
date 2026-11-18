// ============================================================================
// EFCore 行为红测
//（C-06 全 src 无 AsSplitQuery → 两集合导航行数相乘 / C-09 EFCore 检测器缓存不经
//  CacheRegistry 登记 → Mapper.ClearCache 后残留泄漏 / C-08② 告警升级无可构造
//  触发形态免红测（随修复落结构化文案）/ C-23 三处文案订正免红测随修复+同刷）
//
// 修复前形态：标注【现状红】的用例必红；修复 commit 后全部转绿；回退任一字态 → 复红。
// ============================================================================

using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>C-06 承载实体：两集合导航父表</summary>
public class T64B8Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<T64B8Order> Orders { get; set; } = new();
    public List<T64B8Invoice> Invoices { get; set; } = new();
}

/// <summary>C-06 承载实体：订单子表</summary>
public class T64B8Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string No { get; set; } = "";
}

/// <summary>C-06 承载实体：发票子表</summary>
public class T64B8Invoice
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string No { get; set; } = "";
}

/// <summary>C-06 降级承载 DTO（[Mask] 触发降级 → 降级路径 ApplyIncludes 含两集合 Include）</summary>
[MapFrom(typeof(T64B8Customer))]
public class T64B8CustomerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    [Mask(typeof(MaskPhone))]
    public string MaskHolder { get; set; } = "";

    public List<T64B8OrderDto> Orders { get; set; } = new();
    public List<T64B8InvoiceDto> Invoices { get; set; } = new();
}

/// <summary>C-06 订单 DTO</summary>
[MapFrom(typeof(T64B8Order))]
public class T64B8OrderDto
{
    public int Id { get; set; }
    public string No { get; set; } = "";
}

/// <summary>C-06 发票 DTO</summary>
[MapFrom(typeof(T64B8Invoice))]
public class T64B8InvoiceDto
{
    public int Id { get; set; }
    public string No { get; set; } = "";
}

public class T64B8Db : DbContext
{
    public DbSet<T64B8Customer> Customers => Set<T64B8Customer>();
    public DbSet<T64B8Order> Orders => Set<T64B8Order>();
    public DbSet<T64B8Invoice> Invoices => Set<T64B8Invoice>();

    public T64B8Db(DbContextOptions<T64B8Db> options) : base(options) { }
}

[Collection("DapperGlobalState")]
public class T64Batch8RedTests
{
    private static (T64B8Db Db, SqliteConnection Conn) CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<T64B8Db>()
            .UseSqlite(conn)
            .Options;
        var db = new T64B8Db(options);
        db.Database.EnsureCreated();
        db.Customers.Add(new T64B8Customer
        {
            Id = 1, Name = "c1",
            Orders = { new() { Id = 1, CustomerId = 1, No = "o1" }, new() { Id = 2, CustomerId = 1, No = "o2" } },
            Invoices = { new() { Id = 1, CustomerId = 1, No = "i1" }, new() { Id = 2, CustomerId = 1, No = "i2" } },
        });
        db.SaveChanges();
        return (db, conn);
    }

    private static (System.IO.StringWriter Sw, Action Restore) CaptureTrace()
    {
        var sw = new System.IO.StringWriter();
        var oldEnabled = MapTracer.GloballyEnabled;
        var oldOutput = MapTracer.GlobalOutput;
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        return (sw, () => { MapTracer.GloballyEnabled = oldEnabled; MapTracer.GlobalOutput = oldOutput; });
    }

    // ==================================================================
    // C-06：降级路径两集合导航 Include → 全 src 无 AsSplitQuery，行数相乘
    // ==================================================================

    /// <summary>【现状红】降级路径（ApplyIncludes）两集合导航 Include 走单条 JOIN SQL（Executed DbCommand == 1）；
    /// 修复后 AsSplitQuery 应拆分为主查 + 每集合一条（≥3 条）。</summary>
    [Fact]
    public async Task C06_降级路径两集合导航_拆分为split查询()
    {
        var (db, conn) = CreateDb();
        try
        {
            var sqlCommandCount = 0;
            var options = new DbContextOptionsBuilder<T64B8Db>()
                .UseSqlite(conn)
                .LogTo(s => { if (s.Contains("Executed DbCommand")) sqlCommandCount++; },
                    LogLevel.Information)
                .Options;
            using var loggedDb = new T64B8Db(options);

            List<T64B8CustomerDto>? result;
            var (sw, restore) = CaptureTrace();
            try
            {
                result = await loggedDb.Customers.AsDto().ToListAsync<T64B8CustomerDto>();
            }
            finally { restore(); }

            Assert.NotNull(result);
            var dto = Assert.Single(result);
            Assert.Equal(2, dto.Orders.Count);   // 映射正确性锚（数据无丢失）
            Assert.Equal(2, dto.Invoices.Count);

            // 修复后：split 查询 = 主查 1 条 + 两集合各 1 条 ≥ 3；现状单 JOIN = 1 条 → 红
            Assert.True(sqlCommandCount >= 3,
                $"两集合导航应拆分为 split 查询（≥3 条 SQL），现状 {sqlCommandCount} 条（单 JOIN 相乘形态）");
        }
        finally { db.Dispose(); conn.Dispose(); }
    }

    // ==================================================================
    // C-09：EFCore 降级检测器缓存经 CacheRegistry 登记 → ClearCache 全链覆盖
    // （适配：将 s_cache 改实例级 CWT 弱键——CWT 无
    //  Count/枚举面〔评审 003 N-2 口径〕，原"反射 as IDictionary 计数"载体随之退役；
    //  清理实效改由登记存在性（ClearCore 遍历 Full 条目必调用其清理委托 = Instance.ClearCaches()
    //  = s_cache.Clear() 的结构等价条件）+ 清后重算等价两通道承载）
    // <para><b>检测力弱化口径（CR #4）</b>：本形态对"清理委托为 no-op/清错对象"不敏感
    //（重算等价恒真）——实例 CWT 清理实效的直接观测由 NCMapper.Tests.AnchorGateCacheTests
    // 的 ProbeDetector 探针族承载（弱键回收/版本重算计数），本测试保留登记存在性防回归面。</para>
    // ==================================================================

    /// <summary>C-09：NeedsDegradation 填充缓存 → Mapper.ClearCache() → 登记链覆盖清理。
    /// 原【现状红】使命（证明 EFCore 检测器漏登记）已由 C-09 修复批闭环，现存形态为防回归：
    /// 防"登记被删"（登记缺席 = 清理链不覆盖 = 残留泄漏回归）。</summary>
    [Fact]
    public void C09_ClearCache后检测器缓存残留()
    {
        // 1) 触发 EFCore 检测器类型初始化并填充判定缓存（[Mask] DTO 确定走降级）
        Assert.True(EfCoreDegradationDetector.NeedsDegradation(
            typeof(T64B8Customer), typeof(T64B8CustomerDto), out var reasonBefore));

        // 2) 登记存在性（反射——IntegrationTests 无 internal 访问权）：清理链覆盖的可判定条件
        //（Snapshot 元组第二元素为 internal enum CacheLayer，编译期不可见 → 纯反射投影，Full=1）
        var snapshotObj = typeof(NCMapper.Internal.CacheRegistry)
            .GetMethod("Snapshot", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;
        var entries = ((System.Collections.IEnumerable)snapshotObj).Cast<object>()
            .Select(o =>
            {
                var t = o.GetType();
                return (Name: (string)t.GetField("Item1")!.GetValue(o)!,
                        Layer: (int)t.GetField("Item2")!.GetValue(o)!);
            })
            .ToList();
        Assert.Contains(entries, e => e.Name == "ef-core-degradation-detector" && e.Layer == 1); // 1 = CacheLayer.Full

        // 3) Mapper.ClearCache → 全局清理链（含 ef-core-degradation-detector 委托执行）
        Mapper.ClearCache();

        // 4) 清后重算等价：判定结论确定（清理只失效缓存、不改变语义——降级结论复现）
        Assert.True(EfCoreDegradationDetector.NeedsDegradation(
            typeof(T64B8Customer), typeof(T64B8CustomerDto), out var reasonAfter));
        Assert.Equal(reasonBefore, reasonAfter);
    }
}
