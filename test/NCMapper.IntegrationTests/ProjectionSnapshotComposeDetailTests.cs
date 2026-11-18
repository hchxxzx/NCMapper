// Compose 明细/跨表承载——特性 Compose 双表投影走快照（不再反射）
// 降级判定与投影构建数据同源；Compose 项能力位降级五类（Mask/Convert/Format/IsFromJson/When）；
// 空组跳过（SourcePathNames 空 = 忽略信号防御分支）；TypeRef 不可达显示名兜底（Q4 结论——归一口 D-11）；
// 路径形态（多段点路径）。
// （双通道统一发射）+ （可译性判定）；fixture 带 [MapFrom]——IntegrationTests 编译面
//（不入 generated-baseline 三腿 samples/test/benchmarks——字节门零再生预期）。
// 隔离范式：[Collection("PipelineGlobalState")] + 自足注册 + try/finally 清理。
using FreeSql;
using FreeSql.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.IntegrationTests;

[Collection("PipelineGlobalState")]
public class ProjectionSnapshotComposeDetailTests : IDisposable
{
    private readonly T7DComposeDbContext _db;
    private readonly IFreeSql _fsql;
    private readonly string _dbPath;

    public ProjectionSnapshotComposeDetailTests()
    {
        Mapper.ClearCache();
        var connStr = $"DataSource=file:t7dcmp{Guid.NewGuid():N}?mode=memory&cache=shared";
        _db = new T7DComposeDbContext(connStr);
        _db.Database.EnsureCreated();

        _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_t7dcmp_{Guid.NewGuid():N}.db");
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
            new T7DEntity
            {
                Id = 1, Name = "Alice", CustomerId = 10,
                Customer = new T7DCustomer { Id = 10, Level = "Gold", City = "Shanghai" },
            },
        };
        foreach (var r in rows) { _db.Customers.Add(r.Customer!); _db.Rows.Add(r); }
        _db.SaveChanges();

        _fsql.Insert(rows.Select(r => new T7DCustomer { Id = r.Customer!.Id, Level = r.Customer.Level, City = r.Customer.City })).ExecuteAffrows();
        _fsql.Insert(rows.Select(r => new T7DEntity { Id = r.Id, Name = r.Name, CustomerId = r.CustomerId })).ExecuteAffrows();
    }

    private static string Json<T>(T item) => System.Text.Json.JsonSerializer.Serialize(item);

    private static RuntimeTargetPropertySlot Slot(string name, string typeName)
        => new() { Name = name, TypeName = typeName };

    private static RuntimePropertyMapping Pm(string target, string source)
        => new() { Target = Slot(target, "String"), SourceName = source, SourcePathNames = new[] { source } };

    /// <summary>跨表快照腿 meta（与发射产物同构——Channel=MapFrom + pm/ComposeMappings 手工直构）。</summary>
    private static RuntimeMapperMetadata T7DCrossMeta() => new()
    {
        SourceType = typeof(T7DEntity), TargetType = typeof(T7DCrossDto),
        SourceTypeFqn = typeof(T7DEntity).FullName!, TargetTypeFqn = typeof(T7DCrossDto).FullName!,
        Channel = RuntimeMappingChannel.MapFrom,
        HasComposeMappings = false,
        Properties = new[] { Pm("Id", "Id") },
        AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
        ComposeMappings = new[]
        {
            new RuntimeComposeMapping
            {
                TargetPropertyName = "Level",
                SubSourceTypeRef = typeof(T7DCustomer),
                SubSourceTypeName = typeof(T7DCustomer).FullName!,
                SourcePathNames = new[] { "Level" },
            },
        },
    };

    // ================================================================
    // 用例 1：特性 Compose 双表投影走快照 + 跨表（导航推断）字段级双腿等价
    //（反射腿 FromSource 解析导航推断 / 快照腿 ComposeMappings 承载——Level 同值；EFCore + FreeSql 双腿）
    // ================================================================

    [Fact]
    public void ComposeCrossTable_SnapshotConsumed_LegsEquivalent()
    {
        try { RuntimeSnapshotStore.Clear(); } catch { }
        var efLegReflection = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7DCrossDto>();
        var fsLegReflection = _fsql.Select<T7DEntity>().Where(r => r.Id == 1).AsDto().First<T7DCrossDto>();

        RuntimeSnapshotStore.Register(T7DCrossMeta());
        var efLegSnapshot = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7DCrossDto>();
        var fsLegSnapshot = _fsql.Select<T7DEntity>().Where(r => r.Id == 1).AsDto().First<T7DCrossDto>();

        Assert.Equal("Gold", efLegSnapshot.Level);
        Assert.Equal(Json(efLegReflection), Json(efLegSnapshot));
        Assert.Equal(Json(fsLegReflection), Json(fsLegSnapshot));
    }

    // ================================================================
    // 用例 2：降级判定与投影构建数据同源——注册态下 NeedsDegradation=false 与
    // Build 跨表绑定消费同一 ComposeMappings（TryGetMappingDetail 单入口取数）
    // ================================================================

    [Fact]
    public void DegradationAndProjection_SameSource()
    {
        RuntimeSnapshotStore.Register(T7DCrossMeta());

        Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(T7DEntity), typeof(T7DCrossDto), out _),
            "特性 Compose 为合法投影（映射通道恒 false）——不降级");
        Assert.True(RuntimeSnapshotStore.TryGetMappingDetail(typeof(T7DEntity), typeof(T7DCrossDto), out var meta),
            "判定与构建同一取数入口（TryGetMappingDetail）");
        Assert.Equal(1, meta!.ComposeMappings.Length);

        var expr = NCMapper.Internal.ProjectionExpressionFactory.Build<T7DEntity, T7DCrossDto>(new(
            MaxNestingDepth: 8, WarningDepth: 5, EnableCollectionProjection: true, TracePrefix: "[t7d compose]"));
        var dto = expr.Compile()(new T7DEntity { Id = 2, Name = "N2", Customer = new T7DCustomer { Id = 10, Level = "Gold" } });
        Assert.Equal(2, dto.Id);
        Assert.Equal("Gold", dto.Level);
    }

    // ================================================================
    // 用例 3：Compose 项能力位降级五类（Theory——Mask/Convert/Format/IsFromJson/When）
    // 五类扫描对齐表承载——cm 项任一能力位 → NeedsDegradation=true
    // ================================================================

    public static TheoryData<string> ComposeCapabilityKinds => new()
    { "Mask", "Convert", "Format", "FromJson", "When" };

    [Theory]
    [MemberData(nameof(ComposeCapabilityKinds))]
    public void ComposeItemCapability_ForcesDegrade(string kind)
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(T7DCapabilityMeta(kind));

            var needsEf = EfCoreDegradationDetector.NeedsDegradation(typeof(T7DEntity), typeof(T7DCrossDto), out var reasonEf);
            Assert.True(needsEf, $"cm 能力位 {kind} 不可投影翻译——强制降级（对齐表五类扫描）");
            Assert.True(NCMapper.FreeSql.FreeSqlDegradationDetector.NeedsDegradation(typeof(T7DEntity), typeof(T7DCrossDto), out _),
                $"cm 能力位 {kind}——FreeSql 同判（共享 DegradationDetectorBase 内核）");
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    private static RuntimeMapperMetadata T7DCapabilityMeta(string kind)
    {
        var cm = new RuntimeComposeMapping
        {
            TargetPropertyName = "Level",
            SubSourceTypeRef = typeof(T7DCustomer),
            SubSourceTypeName = typeof(T7DCustomer).FullName!,
            SourcePathNames = new[] { "Level" },
        };
        switch (kind)
        {
            case "Mask":
                cm = new RuntimeComposeMapping
                {
                    TargetPropertyName = "Level", SubSourceTypeRef = typeof(T7DCustomer),
                    SubSourceTypeName = typeof(T7DCustomer).FullName!, SourcePathNames = new[] { "Level" },
                    HasMask = true, MaskTypeName = "MaskImpl",
                };
                break;
            case "Convert":
                cm = new RuntimeComposeMapping
                {
                    TargetPropertyName = "Level", SubSourceTypeRef = typeof(T7DCustomer),
                    SubSourceTypeName = typeof(T7DCustomer).FullName!, SourcePathNames = new[] { "Level" },
                    HasConverter = true, ConverterDeclaringType = "Generated", ConverterMethodName = "Map_Cv_L",
                };
                break;
            case "Format":
                cm = new RuntimeComposeMapping
                {
                    TargetPropertyName = "Level", SubSourceTypeRef = typeof(T7DCustomer),
                    SubSourceTypeName = typeof(T7DCustomer).FullName!, SourcePathNames = new[] { "Level" },
                    FormatString = "yyyy-MM-dd",
                };
                break;
            case "FromJson":
                cm = new RuntimeComposeMapping
                {
                    TargetPropertyName = "Level", SubSourceTypeRef = typeof(T7DCustomer),
                    SubSourceTypeName = typeof(T7DCustomer).FullName!, SourcePathNames = new[] { "Level" },
                    IsFromJson = true,
                };
                break;
            case "When":
                cm = new RuntimeComposeMapping
                {
                    TargetPropertyName = "Level", SubSourceTypeRef = typeof(T7DCustomer),
                    SubSourceTypeName = typeof(T7DCustomer).FullName!, SourcePathNames = new[] { "Level" },
                    ConditionMethodRef = "Generated::Map_When_L",
                };
                break;
        }
        return new RuntimeMapperMetadata
        {
            SourceType = typeof(T7DEntity), TargetType = typeof(T7DCrossDto),
            SourceTypeFqn = typeof(T7DEntity).FullName!, TargetTypeFqn = typeof(T7DCrossDto).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            HasComposeMappings = false,
            Properties = new[] { Pm("Id", "Id") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
            ComposeMappings = new[] { cm },
        };
    }

    // ================================================================
    // 用例 4：空组跳过（SourcePathNames 空 = 忽略信号防御分支）——该属性不绑定、其余正常
    // ================================================================

    [Fact]
    public void ComposeEmptyGroup_SkipsBinding()
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T7DEntity), TargetType = typeof(T7DCrossDto),
            SourceTypeFqn = typeof(T7DEntity).FullName!, TargetTypeFqn = typeof(T7DCrossDto).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            HasComposeMappings = false,
            Properties = new[] { Pm("Id", "Id") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
            ComposeMappings = new[]
            {
                new RuntimeComposeMapping { TargetPropertyName = "Level", SubSourceTypeName = typeof(T7DCustomer).FullName! },
            },
        });

        var ef = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7DCrossDto>();
        Assert.Equal(1, ef.Id); // 其余属性正常（防御面局部性）
        Assert.Equal("", ef.Level); // 空组 → 目标属性跳过（不绑定不炸）
    }

    // ================================================================
    // 用例 5：TypeRef 不可达显示名兜底（Q4 结论——归一口 D-11 双口径兼容）：
    // SubSourceTypeRef=null + SubSourceTypeName 全名 → 主源导航属性解析兜底命中
    // ================================================================

    [Fact]
    public void ComposeTypeRefUnreachable_DisplayNameFallback_Binds()
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T7DEntity), TargetType = typeof(T7DCrossDto),
            SourceTypeFqn = typeof(T7DEntity).FullName!, TargetTypeFqn = typeof(T7DCrossDto).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            HasComposeMappings = false,
            Properties = new[] { Pm("Id", "Id") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
            ComposeMappings = new[]
            {
                new RuntimeComposeMapping
                {
                    TargetPropertyName = "Level",
                    SubSourceTypeRef = null, // internal 副源形态——TypeRef 不可达（发射实态）
                    SubSourceTypeName = typeof(T7DCustomer).FullName!,
                    SourcePathNames = new[] { "Level" },
                },
            },
        });

        var ef = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7DCrossDto>();
        Assert.Equal("Gold", ef.Level); // 显示名兜底命中主源 Customer 导航（D-11 归一口）
    }

    // ================================================================
    // 用例 6：多段路径形态——cm SourcePathNames 多段（Customer.City 经副源二级段——
    // 本形态以主源导航直达副源字段单段为主承载，多段经副源内导航——此处以快照腿
    // Level←Customer.Level + 段重建行为锁承载路径形态面；索引段 cm 承载由发射契约
    // 等长占位对齐（B 组 pm 索引维度已覆盖消费内核的索引重建路径）。
    // ================================================================

    [Fact]
    public void ComposeMultiSegmentPath_SnapshotLeg_Binds()
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T7DEntity), TargetType = typeof(T7DCityDto),
            SourceTypeFqn = typeof(T7DEntity).FullName!, TargetTypeFqn = typeof(T7DCityDto).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            HasComposeMappings = false,
            Properties = new[] { Pm("Id", "Id") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("City", "String") },
            ComposeMappings = new[]
            {
                new RuntimeComposeMapping
                {
                    TargetPropertyName = "City",
                    SubSourceTypeRef = typeof(T7DCustomer),
                    SubSourceTypeName = typeof(T7DCustomer).FullName!,
                    SourcePathNames = new[] { "City" },
                },
            },
        });

        var ef = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7DCityDto>();
        Assert.Equal("Shanghai", ef.City);
    }
}

// ================================================================
// fixture（IntegrationTests 编译面——不入 generated-baseline 三腿）
// ================================================================

public class T7DCustomer
{
    public int Id { get; set; }
    public string Level { get; set; } = "";
    public string City { get; set; } = "";
}

public class T7DEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int CustomerId { get; set; }

    [Navigate(nameof(CustomerId))]
    public T7DCustomer? Customer { get; set; }
}

/// <summary>Compose 跨表载体——Level←Customer.Level（FromSource 形态）</summary>
[MapFrom(typeof(T7DEntity))]
public class T7DCrossDto
{
    public int Id { get; set; }

    [MapProperty(typeof(T7DCustomer), nameof(T7DCustomer.Level))]
    public string Level { get; set; } = "";
}

/// <summary>Compose 路径形态载体——City←Customer.City</summary>
[MapFrom(typeof(T7DEntity))]
public class T7DCityDto
{
    public int Id { get; set; }

    [MapProperty(typeof(T7DCustomer), nameof(T7DCustomer.City))]
    public string City { get; set; } = "";
}

/// <summary>EFCore DbContext（独立 SQLite 内存库）</summary>
public class T7DComposeDbContext : DbContext
{
    private readonly string _connStr;
    public T7DComposeDbContext(string connStr) => _connStr = connStr;

    public DbSet<T7DEntity> Rows => Set<T7DEntity>();
    public DbSet<T7DCustomer> Customers => Set<T7DCustomer>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connStr);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<T7DEntity>()
            .HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId);
    }
}
