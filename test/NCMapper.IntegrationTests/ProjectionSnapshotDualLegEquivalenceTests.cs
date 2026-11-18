// 双腿等价矩阵（特性对）——手工 Register 快照腿 vs 反射腿（无注册态）
// 输出等价。维度：同名/异名 / 嵌套点路径 / 索引段 / Ignore 方向位 / 跨表（导航推断）/ 未声明同名属性（D-e 定性）。
// 双腿行 + 差异面定性；Fluent 侧双腿等价不可构造措辞限定特性对。
// 隔离范式（#60/#65/#366）：[Collection("PipelineGlobalState")] + 自足注册 + try/finally 清理；
// 构造器/Dispose 双 Mapper.ClearCache（黄金三角同款）。
// fixture 落位纪律（D-14 同源）：本文件 fixture 带 [MapFrom] 特性——属 IntegrationTests 编译面
//（不入 generated-baseline 三腿 samples/test/benchmarks——字节门零再生预期）；
// 快照腿 meta 形态与编译期产物同构（Channel=MapFrom + pm/ComposeMappings 手工直构）。
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
public class ProjectionSnapshotDualLegEquivalenceTests : IDisposable
{
    private readonly T7BLegDbContext _db;
    private readonly IFreeSql _fsql;
    private readonly string _dbPath;

    public ProjectionSnapshotDualLegEquivalenceTests()
    {
        Mapper.ClearCache(); // 类入口净缓存（黄金三角隔离范式同款——表达式/降级/探针缓存全链）
        var connStr = $"DataSource=file:t7bleg{Guid.NewGuid():N}?mode=memory&cache=shared";
        _db = new T7BLegDbContext(connStr);
        _db.Database.EnsureCreated();

        _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_t7bleg_{Guid.NewGuid():N}.db");
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
            new T7BLegEntity
            {
                Id = 1, Name = "Alice", Note = "n1", AddressId = 10, CustomerId = 100,
                Address = new T7BLegAddr { Id = 10, City = "Shanghai" },
                Customer = new T7BLegCustomer { Id = 100, Level = "Gold" },
                Items = new List<T7BLegItem> { new() { Id = 1, EntityId = 1, Title = "t0" }, new() { Id = 2, EntityId = 1, Title = "t1" } },
            },
        };
        foreach (var r in rows) { _db.Rows.Add(r); }
        _db.SaveChanges();

        _fsql.Insert(rows.Select(r => new T7BLegCustomer { Id = r.Customer!.Id, Level = r.Customer.Level })).ExecuteAffrows();
        _fsql.Insert(rows.Select(r => new T7BLegAddr { Id = r.Address!.Id, City = r.Address.City })).ExecuteAffrows();
        _fsql.Insert(rows.SelectMany(r => r.Items).ToList()).ExecuteAffrows();
        _fsql.Insert(rows.Select(r => new T7BLegEntity
        {
            Id = r.Id, Name = r.Name, Note = r.Note, AddressId = r.AddressId, CustomerId = r.CustomerId,
        })).ExecuteAffrows();
    }

    private static string Json<T>(T item) => System.Text.Json.JsonSerializer.Serialize(item);

    // ================================================================
    // 维度 1：同名（Id）+ 异名（Title←Name）——双腿等价（反射腿 == 快照腿 == 内存渠道）
    // ================================================================

    [Fact]
    public void SameNameAndAlias_Legs_Equivalent()
    {
        var entity = _db.Rows.OrderBy(r => r.Id).First(r => r.Id == 1);

        // 反射腿（无注册态）
        try { RuntimeSnapshotStore.Clear(); } catch { }
        var efLegReflection = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7BLegFlatDto>();
        var fsLegReflection = _fsql.Select<T7BLegEntity>().Where(r => r.Id == 1).AsDto().First<T7BLegFlatDto>();

        // 快照腿（手工 Register——发射形态同构 meta）
        RuntimeSnapshotStore.Register(T7BLegFlatMeta());
        var efLegSnapshot = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7BLegFlatDto>();
        var fsLegSnapshot = _fsql.Select<T7BLegEntity>().Where(r => r.Id == 1).AsDto().First<T7BLegFlatDto>();

        // 内存渠道（第三方对照）
        var mapped = Mapper.Map<T7BLegEntity, T7BLegFlatDto>(entity)!;

        Assert.Equal(1, efLegSnapshot.Id);
        Assert.Equal("Alice", efLegSnapshot.Title);
        Assert.Equal(Json(efLegReflection), Json(efLegSnapshot));
        Assert.Equal(Json(efLegSnapshot), Json(fsLegSnapshot));
        Assert.Equal(Json(fsLegReflection), Json(fsLegSnapshot));
        Assert.Equal(Json(mapped), Json(efLegSnapshot));
    }

    // ================================================================
    // 维度 2：嵌套点路径（Address.City 扁平化——IsFlattened + 多段 SourcePathNames）
    // 双腿等价（反射腿解析 FromField 点路径 / 快照腿 pm SourcePathNames 重建——同值）
    // ================================================================

    [Fact]
    public void NestedDotPath_Legs_Equivalent()
    {
        try { RuntimeSnapshotStore.Clear(); } catch { }
        var efLegReflection = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7BLegPathDto>();
        var fsLegReflection = _fsql.Select<T7BLegEntity>().Where(r => r.Id == 1).AsDto().First<T7BLegPathDto>();

        RuntimeSnapshotStore.Register(T7BLegPathMeta());
        var efLegSnapshot = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7BLegPathDto>();
        var fsLegSnapshot = _fsql.Select<T7BLegEntity>().Where(r => r.Id == 1).AsDto().First<T7BLegPathDto>();

        Assert.Equal("Shanghai", efLegSnapshot.City);
        Assert.Equal(Json(efLegReflection), Json(efLegSnapshot));
        Assert.Equal(Json(fsLegReflection), Json(fsLegSnapshot));
    }

    // ================================================================
    // 维度 3：索引段（Items[0].Title——HasIndex + SourceIndexes 等长 -1 占位）
    // 快照腿索引重建消费（发射契约形态——P2 A-16 inline 先例同构）；
    // 反射腿承载面属提取契约（FromField 内联索引以发射端为准）——本维度断言快照腿绑定。
    // FreeSql 集合投影走降级路径（策略差异——A 组场景 4 既有先例登记）——取 EFCore 腿。
    // ================================================================

    [Fact]
    public void IndexSegment_SnapshotLeg_Binds()
    {
        RuntimeSnapshotStore.Register(T7BLegIndexMeta());
        var efLegSnapshot = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7BLegIndexDto>();
        Assert.Equal("t0", efLegSnapshot.FirstTitle);
    }

    // ================================================================
    // 维度 4：Ignore 方向位（pm 物化面）——快照腿方向位消费 == 内存渠道方向感知契约
    //（D-a 收敛后语义：ToEntity 忽略正向照常绑定 / ToDto 忽略正向跳过）；
    // 反射腿对 ToEntity 方向盲跳过（既有渠道分歧——D-a 裁决收敛面，A 组场景 2 行为护栏已锁）。
    // ================================================================

    [Fact]
    public void DirectionIgnore_SnapshotLeg_MatchesMemoryChannel()
    {
        var entity = _db.Rows.OrderBy(r => r.Id).First(r => r.Id == 1);
        var mapped = Mapper.Map<T7BLegEntity, T7BLegDirDto>(entity)!;

        RuntimeSnapshotStore.Register(T7BLegDirMeta());
        var efLegSnapshot = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7BLegDirDto>();

        // 快照为权威：Name（ToEntity 忽略——pm 无方向位，正向绑定）== 内存渠道
        Assert.Equal("Alice", efLegSnapshot.Name);
        Assert.Equal(mapped.Name, efLegSnapshot.Name);
        // ToDto 忽略——pm 方向位承载，投影正向跳过 == 内存渠道
        Assert.Equal("", efLegSnapshot.Skipped);
        Assert.Equal(mapped.Skipped, efLegSnapshot.Skipped);
    }

    // ================================================================
    // 维度 5：跨表（导航推断）——Customer.Level 特性跨表，双腿等价
    //（反射腿 FromSource 解析导航推断 / 快照腿 ComposeMappings 承载——同值）
    // ================================================================

    [Fact]
    public void CrossTableNavInference_Legs_Equivalent()
    {
        try { RuntimeSnapshotStore.Clear(); } catch { }
        var efLegReflection = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7BLegCrossDto>();
        var fsLegReflection = _fsql.Select<T7BLegEntity>().Where(r => r.Id == 1).AsDto().First<T7BLegCrossDto>();

        RuntimeSnapshotStore.Register(T7BLegCrossMeta());
        var efLegSnapshot = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7BLegCrossDto>();
        var fsLegSnapshot = _fsql.Select<T7BLegEntity>().Where(r => r.Id == 1).AsDto().First<T7BLegCrossDto>();

        Assert.Equal("Gold", efLegSnapshot.Level);
        Assert.Equal(Json(efLegReflection), Json(efLegSnapshot));
        Assert.Equal(Json(fsLegReflection), Json(fsLegSnapshot));
    }

    // ================================================================
    // 维度 6（D-e 定性）：未声明同名属性——反射腿按名绑定 / 快照腿跳过（快照为权威）
    // 定性结论：该维度非双腿等价面——SourceGen 提取全量保证产物 pm = 可匹配全集
    //（快照完整性契约面），真实产物形态下两腿无分歧；手工注册形态按快照为权威定性。
    // ================================================================

    [Fact]
    public void UndeclaredSameName_DE_DivergenceCharacterized()
    {
        try { RuntimeSnapshotStore.Clear(); } catch { }
        var efLegReflection = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7BLegUndeclaredDto>();
        Assert.Equal("n1", efLegReflection.Note); // 反射腿：未声明同名属性按名绑定（现状语义）

        RuntimeSnapshotStore.Register(T7BLegUndeclaredMeta());
        var efLegSnapshot = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7BLegUndeclaredDto>();
        Assert.Equal("", efLegSnapshot.Note); // 快照为权威：pm 未声明 → 不按名重试（D-e 定性——不收敛）
        Assert.Equal("Alice", efLegSnapshot.Title); // 已声明异名照常绑定（局部性——不影响声明面）
        Assert.Equal(1, efLegSnapshot.Id); // 已声明同名照常绑定
    }

    // ================================================================
    // 快照腿 meta 直构（与发射产物同构——Channel=MapFrom + 手工 pm/ComposeMappings）
    // ================================================================

    private static RuntimeTargetPropertySlot Slot(string name, string typeName)
        => new() { Name = name, TypeName = typeName };

    private static RuntimePropertyMapping Pm(string target, string source)
        => new() { Target = Slot(target, "String"), SourceName = source, SourcePathNames = new[] { source } };

    private static RuntimeMapperMetadata BaseMeta(Type source, Type target,
        RuntimePropertyMapping[] props, string[] slotNames)
        => new()
        {
            SourceType = source,
            TargetType = target,
            SourceTypeFqn = source.FullName!,
            TargetTypeFqn = target.FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            Properties = props,
            AllTargetSlots = slotNames.Select(n => Slot(n, "String")).ToArray(),
        };

    private static RuntimeMapperMetadata T7BLegFlatMeta() => BaseMeta(
        typeof(T7BLegEntity), typeof(T7BLegFlatDto),
        new[] { Pm("Id", "Id"), Pm("Title", "Name") },
        new[] { "Id", "Title" });

    private static RuntimeMapperMetadata T7BLegPathMeta() => BaseMeta(
        typeof(T7BLegEntity), typeof(T7BLegPathDto),
        new[]
        {
            Pm("Id", "Id"),
            new RuntimePropertyMapping
            {
                Target = Slot("City", "String"),
                SourceName = "City",
                SourcePathNames = new[] { "Address", "City" },
                SourcePropertyTypeName = "String",
                IsFlattened = true,
            },
        },
        new[] { "Id", "City" });

    private static RuntimeMapperMetadata T7BLegIndexMeta() => BaseMeta(
        typeof(T7BLegEntity), typeof(T7BLegIndexDto),
        new[]
        {
            Pm("Id", "Id"),
            new RuntimePropertyMapping
            {
                Target = Slot("FirstTitle", "String"),
                SourceName = "Title",
                SourcePathNames = new[] { "Items", "Title" },
                SourceIndexes = new[] { 0, -1 }, // 对齐发射契约（等长 + -1 占位）
                SourcePropertyTypeName = "String",
                HasIndex = true,
            },
        },
        new[] { "Id", "FirstTitle" });

    private static RuntimeMapperMetadata T7BLegDirMeta() => new()
    {
        SourceType = typeof(T7BLegEntity),
        TargetType = typeof(T7BLegDirDto),
        SourceTypeFqn = typeof(T7BLegEntity).FullName!,
        TargetTypeFqn = typeof(T7BLegDirDto).FullName!,
        Channel = RuntimeMappingChannel.MapFrom,
        Properties = new[]
        {
            Pm("Id", "Id"),
            // D-a 收敛形态：ToEntity 忽略在快照 pm 无方向位（提取契约——正向绑定）
            Pm("Name", "Name"),
            new RuntimePropertyMapping
            {
                Target = new RuntimeTargetPropertySlot
                {
                    Name = "Skipped", TypeName = "String",
                    HasMapIgnore = true, MapIgnoreDirection = (int)Direction.ToDto,
                },
                SourceName = "Note",
                SourcePathNames = new[] { "Note" },
            },
        },
        AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String"), Slot("Skipped", "String") },
    };

    private static RuntimeMapperMetadata T7BLegCrossMeta() => new()
    {
        SourceType = typeof(T7BLegEntity),
        TargetType = typeof(T7BLegCrossDto),
        SourceTypeFqn = typeof(T7BLegEntity).FullName!,
        TargetTypeFqn = typeof(T7BLegCrossDto).FullName!,
        Channel = RuntimeMappingChannel.MapFrom,
        HasComposeMappings = false,
        Properties = new[] { Pm("Id", "Id") },
        AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
        ComposeMappings = new[]
        {
            new RuntimeComposeMapping
            {
                TargetPropertyName = "Level",
                SubSourceTypeRef = typeof(T7BLegCustomer),
                SubSourceTypeName = typeof(T7BLegCustomer).FullName!,
                SourcePathNames = new[] { "Level" },
            },
        },
    };

    private static RuntimeMapperMetadata T7BLegUndeclaredMeta() => BaseMeta(
        typeof(T7BLegEntity), typeof(T7BLegUndeclaredDto),
        new[] { Pm("Id", "Id"), Pm("Title", "Name") }, // Note 不在明细——D-e 定性输入
        new[] { "Id", "Title", "Note" });
}

// ================================================================
// fixture（IntegrationTests 编译面——不入 generated-baseline 三腿）
// ================================================================

public class T7BLegAddr
{
    public int Id { get; set; }
    public string City { get; set; } = "";
}

public class T7BLegCustomer
{
    public int Id { get; set; }
    public string Level { get; set; } = "";
}

public class T7BLegItem
{
    public int Id { get; set; }
    public int EntityId { get; set; }
    public string Title { get; set; } = "";
}

public class T7BLegEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Note { get; set; } = "";
    public int AddressId { get; set; }

    [Navigate(nameof(AddressId))]
    public T7BLegAddr? Address { get; set; }

    public int CustomerId { get; set; }

    [Navigate(nameof(CustomerId))]
    public T7BLegCustomer? Customer { get; set; }

    [Navigate(nameof(T7BLegItem.EntityId))]
    public List<T7BLegItem> Items { get; set; } = new();
}

/// <summary>同名 Id + 异名 Title←Name + 扩展转换（int→long——双腿等价基本面）</summary>
[MapFrom(typeof(T7BLegEntity))]
public class T7BLegFlatDto
{
    public int Id { get; set; }

    [MapProperty(typeof(T7BLegEntity), nameof(T7BLegEntity.Name))]
    public string Title { get; set; } = "";
}

/// <summary>嵌套点路径 City←Address.City（扁平化）</summary>
[MapFrom(typeof(T7BLegEntity))]
public class T7BLegPathDto
{
    public int Id { get; set; }

    [MapProperty(typeof(T7BLegEntity), "Address.City")]
    public string City { get; set; } = "";
}

/// <summary>索引段 FirstTitle←Items[0].Title</summary>
[MapFrom(typeof(T7BLegEntity))]
public class T7BLegIndexDto
{
    public int Id { get; set; }

    [MapProperty(typeof(T7BLegEntity), "Items[0].Title")]
    public string FirstTitle { get; set; } = "";
}

/// <summary>方向忽略对——Name×ToEntity（正向绑定面）/ Skipped×ToDto（正向跳过面）</summary>
[MapFrom(typeof(T7BLegEntity))]
public class T7BLegDirDto
{
    public int Id { get; set; }

    [MapIgnore(Direction.ToEntity)]
    public string Name { get; set; } = "";

    [MapIgnore(Direction.ToDto)]
    public string Skipped { get; set; } = "";
}

/// <summary>跨表导航推断——Level←Customer.Level（FromSource 形态）</summary>
[MapFrom(typeof(T7BLegEntity))]
public class T7BLegCrossDto
{
    public int Id { get; set; }

    [MapProperty(typeof(T7BLegCustomer), nameof(T7BLegCustomer.Level))]
    public string Level { get; set; } = "";
}

/// <summary>D-e 定性载体——Note 同名未声明（快照腿跳过面）+ Title 异名声明</summary>
[MapFrom(typeof(T7BLegEntity))]
public class T7BLegUndeclaredDto
{
    public int Id { get; set; }
    public string Note { get; set; } = "";

    [MapProperty(typeof(T7BLegEntity), nameof(T7BLegEntity.Name))]
    public string Title { get; set; } = "";
}

/// <summary>EFCore DbContext（独立 SQLite 内存库）</summary>
public class T7BLegDbContext : DbContext
{
    private readonly string _connStr;
    public T7BLegDbContext(string connStr) => _connStr = connStr;

    public DbSet<T7BLegEntity> Rows => Set<T7BLegEntity>();
    public DbSet<T7BLegAddr> Addresses => Set<T7BLegAddr>();
    public DbSet<T7BLegCustomer> Customers => Set<T7BLegCustomer>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connStr);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<T7BLegEntity>(e =>
        {
            e.HasOne(x => x.Address).WithMany().HasForeignKey(x => x.AddressId);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId);
            e.OwnsMany(x => x.Items, b =>
            {
                b.Property<int>("Id");
                b.HasKey("Id");
                b.WithOwner().HasForeignKey("EntityId");
            });
        });
    }
}
