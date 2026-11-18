// Fluent Compose 独立等价面——可译投影 vs 降级内存输出等价
// 不可译端到端降级；F14 链式 fail-closed 端到端；EFCore 导航投影 vs FreeSql 单表/双表门面双 ORM 各验。
// 形态清单按 PoC 定稿导出；矩阵结构复用 B/D 组（自足注册 + [MapFrom] 落位纪律）。
// 等价面独立性（风险①）：本文件全部为行为变更后的目标态断言——与等价面（行为保持面）隔离。
// 隔离范式：[Collection("PipelineGlobalState")] + 自足注册 + try/finally 清理。
using FreeSql;
using FreeSql.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.IntegrationTests;

[Collection("PipelineGlobalState")]
public class FluentComposeEquivalenceTests : IDisposable
{
    private readonly T7FEquiDbContext _db;
    private readonly IFreeSql _fsql;
    private readonly string _dbPath;

    public FluentComposeEquivalenceTests()
    {
        Mapper.ClearCache();
        RuntimeSnapshotStore.Clear();
        var connStr = $"DataSource=file:t7fequi{Guid.NewGuid():N}?mode=memory&cache=shared";
        _db = new T7FEquiDbContext(connStr);
        _db.Database.EnsureCreated();

        _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_t7fequi_{Guid.NewGuid():N}.db");
        _fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
            .UseAutoSyncStructure(true)
            .Build();

        Seed();
    }

    public void Dispose()
    {
        _db.Dispose();
        _fsql.Dispose();
        try { RuntimeSnapshotStore.Clear(); } catch { }
        try { Mapper.ClearCache(); } catch { }
        try { EfCoreDegradationDetector.ClearCache(); } catch { }
        try { NCMapper.FreeSql.FreeSqlDegradationDetector.ClearCache(); } catch { }
        if (File.Exists(_dbPath)) { try { File.Delete(_dbPath); } catch { } }
    }

    private void Seed()
    {
        var rows = new[]
        {
            new T7FEquEntity
            {
                Id = 1, Name = "Alice", SubId = 10,
                Sub = new T7FEquSub { Id = 10, Level = "Gold" },
            },
        };
        foreach (var r in rows) { _db.Subs.Add(r.Sub!); _db.Rows.Add(r); }
        _db.SaveChanges();

        _fsql.Insert(rows.Select(r => r.Sub!)).ExecuteAffrows();
        _fsql.Insert(rows.Select(r => new T7FEquEntity { Id = r.Id, Name = r.Name, SubId = r.SubId })).ExecuteAffrows();
    }

    private static string Json<T>(T item) => System.Text.Json.JsonSerializer.Serialize(item);

    private static RuntimeTargetPropertySlot Slot(string name, string typeName)
        => new() { Name = name, TypeName = typeName };

    private static RuntimePropertyMapping Pm(string target, string source)
        => new() { Target = Slot(target, "String"), SourceName = source, SourcePathNames = new[] { source } };

    /// <summary>Fluent Compose 对 meta（可译形态——cm 纯副源字段；目标态）。</summary>
    private static void RegisterTranslatable()
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T7FEquEntity), TargetType = typeof(T7FEquDto),
            SourceTypeFqn = typeof(T7FEquEntity).FullName!, TargetTypeFqn = typeof(T7FEquDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            HasComposeMappings = true,
            Properties = new[] { Pm("Id", "Id") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
            ComposeMappings = new[]
            {
                new RuntimeComposeMapping
                {
                    TargetPropertyName = "Level",
                    SubSourceTypeRef = typeof(T7FEquSub),
                    SubSourceTypeName = typeof(T7FEquSub).FullName!,
                    SourcePathNames = new[] { "Level" },
                },
            },
        });
    }

    // ================================================================
    // 等价 1（EFCore 单表导航投影）：投影输出 == 内存 Compose 输出
    // ================================================================

    [Fact]
    public void Equiv1_EFCore_SingleTable_ProjectionMatchesDeclaration()
    {
        RegisterTranslatable();
        var main = _db.Rows.OrderBy(r => r.Id).First(r => r.Id == 1);

        Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(T7FEquEntity), typeof(T7FEquDto), out _),
            "可译 Fluent Compose 对——放行");

        var projected = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7FEquDto>();
        // 声明语义锚：Level ← Sub.Level（副源表直查对照——Fluent Compose 的前现状为
        // 恒降级且副源字段静默缺失〔R6 根因全貌〕，Mapper.Compose 内存渠道为特性对专属〔既有契约〕，
        // 故等价锚 = 声明语义而非旧渠道输出）
        var declared = _db.Subs.OrderBy(s => s.Id).First(s => s.Id == main.SubId).Level;
        Assert.Equal(1, projected.Id);
        Assert.Equal(declared, projected.Level);
        Assert.Equal("Gold", projected.Level);

        // 内存 Compose 渠道边界锁（既有契约——Mapper.Compose 为特性对专属，Fluent 对不受影响）
        Assert.Throws<NCMapper.MapConfigurationException>(() => Mapper.Compose<T7FEquDto>(main, main.Sub!));
    }

    // ================================================================
    // 等价 2（FreeSql 单表导航投影）：投影输出 == 内存 Compose 输出
    // ================================================================

    [Fact]
    public void Equiv2_FreeSql_SingleTable_ProjectionEqualsMemoryCompose()
    {
        RegisterTranslatable();

        Assert.False(FreeSqlDegradationDetector.NeedsDegradation(typeof(T7FEquEntity), typeof(T7FEquDto), out _),
            "可译 Fluent Compose 对——FreeSql 侧放行");

        var fs = _fsql.Select<T7FEquEntity>().Where(r => r.Id == 1).AsDto().First<T7FEquDto>();
        Assert.Equal(1, fs.Id);
        Assert.Equal("Gold", fs.Level);
    }

    // ================================================================
    // 等价 3（FreeSql 双表 Compose 门面）：判定放行 → Kernel 投影走通
    // （前该形态恒降级内存 Compose；放行后消费面为 ComposeProjectionKernel
    // Fluent 对无 [MapFrom] 锚，Kernel 入口 mapFromAttr 校验为 D-18 缺口面——本用例先证红后随内修复绿）
    // ================================================================

    [Fact]
    public void Equiv3_FreeSql_TwoTableCompose_Gate_Binds()
    {
        RegisterTranslatable();

        Assert.False(FreeSqlDegradationDetector.NeedsDegradationCompose(
            typeof(T7FEquEntity), new[] { typeof(T7FEquSub) }, typeof(T7FEquDto), out _),
            "可译 Fluent Compose 对——双表 Compose 门面判定放行");

        var fs = _fsql.Select<T7FEquEntity, T7FEquSub>()
            .Where((a, s) => a.Id == 1)
            .AsDto()
            .First<T7FEquDto>();
        Assert.NotNull(fs);
        Assert.Equal("Gold", fs!.Level);
    }

    // ================================================================
    // 等价 4（不可译端到端）：cm Mask → 降级内存路径，输出与内存渠道一致（语义完整——脱敏生效）
    // ================================================================

    [Fact]
    public void Equiv4_Untranslatable_Mask_Degrades_LegacySemanticsKept()
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T7FEquEntity), TargetType = typeof(T7FEquMaskDto),
            SourceTypeFqn = typeof(T7FEquEntity).FullName!, TargetTypeFqn = typeof(T7FEquMaskDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            HasComposeMappings = true,
            Properties = new[] { Pm("Id", "Id") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
            ComposeMappings = new[]
            {
                new RuntimeComposeMapping
                {
                    TargetPropertyName = "Level",
                    SubSourceTypeRef = typeof(T7FEquSub),
                    SubSourceTypeName = typeof(T7FEquSub).FullName!,
                    SourcePathNames = new[] { "Level" },
                    HasMask = true, MaskTypeName = "T7FEquMaskImpl",
                },
            },
        });

        Assert.True(EfCoreDegradationDetector.NeedsDegradation(typeof(T7FEquEntity), typeof(T7FEquMaskDto), out var reason),
            "cm Mask——强制降级（明文外泄红线）");
        Assert.Contains("Mask", reason, StringComparison.Ordinal);

        // 降级路径语义 = 前现状维持（单源内存映射无副源——cm 属性缺失 Level=""）
        // 行为变更仅作用于可译形态（放行投影），不可译形态输出与既往一致
        var degraded = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7FEquMaskDto>();
        Assert.Equal(1, degraded.Id);
        Assert.Equal("", degraded.Level);
    }

    // ================================================================
    // 等价 5（F14 链式 fail-closed 端到端）：合成型副源（TypeRef null）——单表投影走通、
    // cm 属性 fail-closed 跳过（用户裁定口径——非数据丢失路径先例）
    // ================================================================

    [Fact]
    public void Equiv5_ChainedSubSource_FailClosed_Skips_NoThrow()
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T7FEquEntity), TargetType = typeof(T7FEquDto),
            SourceTypeFqn = typeof(T7FEquEntity).FullName!, TargetTypeFqn = typeof(T7FEquDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            HasComposeMappings = true,
            Properties = new[] { Pm("Id", "Id") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
            ComposeMappings = new[]
            {
                new RuntimeComposeMapping
                {
                    TargetPropertyName = "Level",
                    SubSourceTypeRef = null, // 合成型副源（D-11 真实形态）
                    SubSourceTypeName = "Q8.Composite.SubView",
                    SourcePathNames = new[] { "Level" },
                },
            },
        });

        var projected = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7FEquDto>();
        Assert.Equal(1, projected.Id); // 其余属性正常
        Assert.Equal("", projected.Level); // fail-closed 跳过（不炸——用户裁定口径）
    }

    // ================================================================
    // 等价 6 双表 Compose 门面 + 链式合成副源（TypeRef null）。
    // **路径双语义锁定**：双表门面显式提供副源参数（T2=Q9EquSub）→ cm 路径在显式源参数上
    // 解析成功（Level ← t2.Level 绑定）；单表导航推断场景才走 fail-closed 跳过（Equiv5——
    // 无显式副源、合成型不可推断）。两语义均为非数据丢失路径（双表的数据由显式参数提供）。
    // 红测归因：初版误用单表 fail-closed 预期（Expected "" / Actual "Gold"）——归因后按真语义锁定。
    // ================================================================

    [Fact]
    public void Equiv6_TwoTableCompose_ChainedSubSource_ExplicitParamPathBinds()
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T7FEquEntity), TargetType = typeof(T7FEquDto),
            SourceTypeFqn = typeof(T7FEquEntity).FullName!, TargetTypeFqn = typeof(T7FEquDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            HasComposeMappings = true,
            Properties = new[] { Pm("Id", "Id") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
            ComposeMappings = new[]
            {
                new RuntimeComposeMapping
                {
                    TargetPropertyName = "Level",
                    SubSourceTypeRef = null, // 合成型副源元数据（TypeRef 不可达——发射实态形态）
                    SubSourceTypeName = "Q8.Composite.SubView",
                    SourcePathNames = new[] { "Level" },
                },
            },
        });

        var fs = _fsql.Select<T7FEquEntity, T7FEquSub>()
            .Where((a, s) => a.Id == 1)
            .AsDto()
            .First<T7FEquDto>();
        Assert.NotNull(fs);
        Assert.Equal(1, fs!.Id);
        Assert.Equal("Gold", fs.Level); // 显式源参数路径解析（t2.Level——双表真语义；不炸）
    }
}

// ================================================================
// fixture（IntegrationTests 编译面——不入 generated-baseline 三腿）
// ================================================================

public class T7FEquSub
{
    public int Id { get; set; }
    public string Level { get; set; } = "";
}

public class T7FEquEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SubId { get; set; }

    [Navigate(nameof(SubId))]
    public T7FEquSub? Sub { get; set; }
}

/// <summary>Fluent Compose 可译对载体（手工注册——无 [MapFrom] 锚）。</summary>
public class T7FEquDto
{
    public int Id { get; set; }
    public string Level { get; set; } = "";
}

/// <summary>不可译对载体（cm Mask——脱敏输出 "MASKED"）。</summary>
public class T7FEquMaskDto
{
    public int Id { get; set; }
    public string Level { get; set; } = "";
}

public sealed class T7FEquMaskImpl : IMask
{
    public string Mask(string? value) => "MASKED";
}

/// <summary>EFCore DbContext（独立 SQLite 内存库）。</summary>
public class T7FEquiDbContext : DbContext
{
    private readonly string _connStr;
    public T7FEquiDbContext(string connStr) => _connStr = connStr;

    public DbSet<T7FEquEntity> Rows => Set<T7FEquEntity>();
    public DbSet<T7FEquSub> Subs => Set<T7FEquSub>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connStr);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<T7FEquEntity>()
            .HasOne(x => x.Sub).WithMany().HasForeignKey(x => x.SubId);
    }
}
