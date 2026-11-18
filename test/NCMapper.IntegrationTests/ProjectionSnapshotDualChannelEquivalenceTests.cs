// 双渠道等价矩阵——同一 fixture 投影渠道 vs 降级内存渠道输出等价。
// （等价机制先例 ProjectionGlobalConfigConsistencyTests 扩展）+ 差异面 D-a/D-d 预登记裁决落地。
// 固定 fixture 含 D7 族（[MapIgnore(ToEntity)] × DTO 侧锚——差异面 D-a：现状投影方向盲跳过、内存渠道方向感知照常
// 映射——切换后快照为权威收敛到内存渠道契约）与 WMI2 族（点路径 × 任意方向 [MapIgnore]——差异面 D-d：提取期静默
// 豁免无 pm——切换后投影不再按名重试，跳过收敛）——防"矩阵未覆盖即静默通过"（评审 003-P2-1 落实）。
// fixture 落位纪律（D-14 同源）：本文件 fixture 带 [MapFrom] 特性——属 IntegrationTests 编译面（不入 generated-baseline
// 三腿 samples/test/benchmarks——字节门零再生预期）；Fluent 侧双腿等价不可构造（P2-2 订正）——靠本双渠道等价覆盖。
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
public class ProjectionSnapshotDualChannelEquivalenceTests : IDisposable
{
    private readonly T7DualDbContext _db;
    private readonly IFreeSql _fsql;
    private readonly string _dbPath;

    public ProjectionSnapshotDualChannelEquivalenceTests()
    {
        // 隔离范式（黄金三角同款）：类入口清缓存 + 快照注册表——防跨类/跨用例串扰
        Mapper.ClearCache();
        RuntimeSnapshotStore.Clear();
        var connStr = $"DataSource=file:t7dual{Guid.NewGuid():N}?mode=memory&cache=shared";
        _db = new T7DualDbContext(connStr);
        _db.Database.EnsureCreated();

        _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_t7dual_{Guid.NewGuid():N}.db");
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
        // Customer 实例跨行共享（EFCore 同键双实例追踪冲突防线）+ FreeSql 副源表独立插入
        //（导航对象不随主实体级联——副源表数据缺失会导致 FreeSql 导航 JOIN 恒空）
        var custA = new T7DualCustomer { Id = 1, Name = "CustA", Level = "Gold" };
        var custB = new T7DualCustomer { Id = 2, Name = "CustB", Level = "Silver" };
        var rows = new[]
        {
            new T7DualEntity { Id = 1, Name = "Alice", Age = 30, Note = "n1", CustomerId = 1, AddressId = 10,
                Address = new T7DualAddress { Id = 10, City = "Shanghai", Street = "Rd-1" }, Customer = custA },
            new T7DualEntity { Id = 2, Name = "Bob", Age = 40, Note = "n2", CustomerId = 1, AddressId = 20,
                Address = new T7DualAddress { Id = 20, City = "Beijing", Street = "Rd-2" }, Customer = custA },
            new T7DualEntity { Id = 3, Name = "Carol", Age = 50, Note = "n3", CustomerId = 2, AddressId = 30,
                Address = new T7DualAddress { Id = 30, City = "Shenzhen", Street = "Rd-3" }, Customer = custB },
        };
        foreach (var r in rows) { _db.Rows.Add(r); }
        _db.SaveChanges();

        _fsql.Insert(rows.Select(r => r.Address!)).ExecuteAffrows();
        _fsql.Insert(new[] { custA, custB }).ExecuteAffrows();
        _fsql.Insert(rows.Select(r => new T7DualEntity
        {
            Id = r.Id, Name = r.Name, Age = r.Age, Note = r.Note, CustomerId = r.CustomerId, AddressId = r.AddressId,
        })).ExecuteAffrows();
    }

    private static string Json<T>(T item) => System.Text.Json.JsonSerializer.Serialize(item);

    private static RuntimeTargetPropertySlot Slot(string name, string typeName)
        => new() { Name = name, TypeName = typeName };

    private static RuntimePropertyMapping Pm(string target, string source)
        => new() { Target = Slot(target, "String"), SourceName = source, SourcePathNames = new[] { source } };

    /// <summary>D7 对快照腿 meta（D-a 收敛载体——提取契约：ToEntity 方向忽略不进 pm 方向位，正向绑定）。</summary>
    private static RuntimeMapperMetadata DirIgnoreMeta() => new()
    {
        SourceType = typeof(T7DualEntity), TargetType = typeof(T7DirIgnoreDto),
        SourceTypeFqn = typeof(T7DualEntity).FullName!, TargetTypeFqn = typeof(T7DirIgnoreDto).FullName!,
        Channel = RuntimeMappingChannel.MapFrom,
        Properties = new[] { Pm("Id", "Id"), Pm("Name", "Name") },
        AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String") },
    };

    /// <summary>WMI2 对快照腿 meta（D-d 载体——点路径×方向忽略提取期豁免：零 pm 零 slot）。</summary>
    private static RuntimeMapperMetadata DotPathIgnoreMeta(Type target) => new()
    {
        SourceType = typeof(T7DualEntity), TargetType = target,
        SourceTypeFqn = typeof(T7DualEntity).FullName!, TargetTypeFqn = target.FullName!,
        Channel = RuntimeMappingChannel.MapFrom,
        Properties = new[] { Pm("Id", "Id") },
        AllTargetSlots = new[] { Slot("Id", "Int32") },
    };

    // ================================================================
    // 场景 1：基元直映 + 异名（[MapProperty]）+ 类型扩展转换（int→long）
    // 双渠道等价：EFCore 投影 / FreeSql 投影（各自路径）== 内存 Mapper.Map
    // ================================================================

    [Fact]
    public void PrimitiveAliasConvert_DualChannel_Equivalent()
    {
        var mapped = Mapper.Map<T7DualEntity, T7FlatDto>(_db.Rows.OrderBy(r => r.Id).First(r => r.Id == 1))!;

        var ef = _db.Rows.OrderBy(r => r.Id).AsDto().ToList<T7FlatDto>();
        var fs = _fsql.Select<T7DualEntity>().OrderBy(r => r.Id).AsDto().ToList<T7FlatDto>();

        Assert.Equal(3, ef.Count);
        Assert.Equal(Json(mapped), Json(ef[0]));
        Assert.Equal(Json(ef), Json(fs));
        Assert.Equal("Alice", ef[0].Title);   // 异名 Title←Name（快照 pm 明细消费）
        Assert.Equal(30L, ef[0].AgeAsLong);   // int→long 扩展转换双渠道语义一致
    }

    // ================================================================
    // 场景 2（D7 族固定 fixture——差异面 D-a）：[MapIgnore(ToEntity)] × DTO 侧锚
    // 现状反射腿方向盲跳过 / 内存渠道方向感知照常映射——既有渠道分歧；
    // 快照为权威（D-a 编制侧建议口径，用户批准裁决）→ 投影正向绑定收敛内存渠道契约。
    // 双渠道等价断言即该裁决的行为护栏（投影输出与内存一致 = 分歧消除）。
    // ================================================================

    [Fact]
    public void D7_DirectionIgnoreToEntity_DualChannel_Equivalent()
    {
        RuntimeSnapshotStore.Register(DirIgnoreMeta()); // 快照腿（D-a 收敛载体——发射形态模拟）
        var entity = _db.Rows.OrderBy(r => r.Id).First(r => r.Id == 2);
        var mapped = Mapper.Map<T7DualEntity, T7DirIgnoreDto>(entity)!;

        var ef = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 2).AsDto().Single<T7DirIgnoreDto>();
        var fs = _fsql.Select<T7DualEntity>().Where(r => r.Id == 2).AsDto().First<T7DirIgnoreDto>();

        // 快照为权威：投影正向绑定 Name（D-a 收敛）——与内存渠道一致
        Assert.Equal("Bob", mapped.Name);
        Assert.Equal(Json(mapped), Json(ef));
        Assert.Equal(Json(ef), Json(fs));
    }

    // ================================================================
    // 场景 3（WMI2 族固定 fixture——差异面 D-d）：点路径 × 任意方向 [MapIgnore]
    // ================================================================
    // 场景 3（WMI2 族固定 fixture——差异面 D-d）：点路径 × 任意方向 [MapIgnore]
    // 提取期静默豁免（零 pm 零 slot）——现状反射腿按名重试绑定成功；切换后投影不再按名重试，跳过。
    // D-d 双形态如实锁定（IntegrationTests 无 SourceGen——内存渠道为运行时反射形态）：
    // ① 运行时内存：反射 Mapper 直读特性，方向感知——ToEntity 忽略不拦正向 → 绑定 CityName；
    //    生成代码形态的豁免语义由提取契约收敛（快照腿 pm 模拟发射实态）。
    // ② 快照为权威（矩阵 D-d 口径）→ 投影跳过（不按名重试）——切换后形态。
    // ToEntity 变体（点路径 Address.City + ToEntity 忽略实证形态）
    // 与 ToDto 变体（单段点路径 Name + ToDto 忽略——"任意方向"覆盖）。
    // ================================================================

    [Fact]
    public void WMI2_DotPathDirectionIgnore_DualChannel_Equivalent()
    {
        // 快照腿注册（D-d 收敛载体——发射形态模拟：点路径×方向忽略提取期豁免，零 pm 零 slot）
        RuntimeSnapshotStore.Register(DotPathIgnoreMeta(typeof(T7DotPathIgnoreToEntityDto)));
        RuntimeSnapshotStore.Register(DotPathIgnoreMeta(typeof(T7DotPathIgnoreToDtoDto)));
        var entity = _db.Rows.OrderBy(r => r.Id).First(r => r.Id == 3);
        var mappedToEntity = Mapper.Map<T7DualEntity, T7DotPathIgnoreToEntityDto>(entity)!;
        var mappedToDto = Mapper.Map<T7DualEntity, T7DotPathIgnoreToDtoDto>(entity)!;

        var efCross = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 3).AsDto().Single<T7DotPathIgnoreToEntityDto>();
        var fsCross = _fsql.Select<T7DualEntity>().Where(r => r.Id == 3).AsDto().First<T7DotPathIgnoreToEntityDto>();
        var efSame = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 3).AsDto().Single<T7DotPathIgnoreToDtoDto>();
        var fsSame = _fsql.Select<T7DualEntity>().Where(r => r.Id == 3).AsDto().First<T7DotPathIgnoreToDtoDto>();

        // D-d 双形态如实锁定：① 运行时内存方向感知（ToEntity 不拦正向 → 绑定；ToDto 正向拦截 → 跳过）
        Assert.Equal("Shenzhen", mappedToEntity.CityName);
        Assert.Equal("", mappedToDto.Name);
        // ② 快照为权威：提取期豁免（零 pm 零 slot）→ 投影跳过（不按名重试）——切换后形态
        Assert.Equal("", efCross.CityName);
        Assert.Equal("", efSame.Name);
        // 双投影渠道等价（快照腿——提取豁免在两 ORM 投影侧同形态）
        Assert.Equal(Json(efCross), Json(fsCross));
        Assert.Equal(Json(efSame), Json(fsSame));
        // 其余属性正常（豁免面局部性——Id 照常）
        Assert.Equal(3, efCross.Id);
        Assert.Equal(3, efSame.Id);
    }

    // ================================================================
    // 场景 4：嵌套导航 + 集合投影 + 深导航——双渠道等价
    // EFCore 投影 SQL 直译嵌套 MemberInit；FreeSql SQL 翻译器不支持嵌套 DTO MemberInit
    //（既有边界——DualEngine 场景 5a 同源登记，非本任务引入）：等价面按先例范式走
    // Compile 内存执行（FreeSqlProjectionContractTests 嵌套契约同款）。
    // null 导航行不参与跨渠道断言（投影 null 安全 vs 内存嵌套构造为既有策略差异——先例登记）。
    // ================================================================

    [Fact]
    public void NestedCollectionDeepNav_DualChannel_Equivalent()
    {
        var entity = _db.Rows.OrderBy(r => r.Id).First(r => r.Id == 1);
        var mapped = Mapper.Map<T7DualEntity, T7NestedDto>(entity)!;

        var ef = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7NestedDto>();
        var fsExpr = NCMapper.FreeSql.FreeSqlProjectionBuilder.Build<T7DualEntity, T7NestedDto>().Compile();
        var fs = fsExpr(entity);

        Assert.Equal("Shanghai", mapped.Address!.City);
        Assert.Equal("Gold", mapped.Customer!.Level);
        Assert.Equal(Json(mapped.Address), Json(ef.Address));
        Assert.Equal(Json(mapped.Customer), Json(ef.Customer));
        Assert.Equal(Json(ef.Address), Json(fs.Address));
        Assert.Equal(Json(ef.Customer), Json(fs.Customer));
    }

    // ================================================================
    // 场景 5：降级形态（record 主构造）——双 ORM 均降级内存路径 → 输出等价
    // （off/明细不可达语义之下，特性对 record 降级判定维持——矩阵收口复验）
    // ================================================================

    [Fact]
    public void DegradedRecordForm_DualChannel_Equivalent()
    {
        var entity = _db.Rows.OrderBy(r => r.Id).First(r => r.Id == 1);
        var mapped = Mapper.Map<T7DualEntity, T7RecordDto>(entity)!;

        var ef = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7RecordDto>();
        var fs = _fsql.Select<T7DualEntity>().Where(r => r.Id == 1).AsDto().First<T7RecordDto>();

        Assert.Equal(1, mapped.Id);
        Assert.Equal("Alice", mapped.Name);
        Assert.Equal(Json(ef), Json(fs));
        Assert.Equal(mapped.Id, ef.Id);
        Assert.Equal(mapped.Name, ef.Name);
    }

    // ================================================================
    // 场景 6（混合形态——[MapFrom(A)]Dto + For<B,Dto> 并存多源并存行）
    // 标准模式模块加载成功（编译期零诊断 + 运行时双通道注册并存）——两对各自可投影。
    // D1 生成路径零命中反向验收（B2 复现用例——同对双声明门不误伤跨对并存形态）。
    // ================================================================

    [Fact]
    public void MixedForm_Loaded_BothPairsProjectable()
    {
        // Fluent 对注册（[MapperConfig] 需 SourceGen 挂载——IntegrationTests 未挂 SourceGen〔M-13 先验锚〕，
        // 以运行时手工注册模拟标准模式 Fluent 注册形态——FluentProjectionMatrixP2Tests 同款隔离范式）
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T7DualAddress), TargetType = typeof(T7AddrFluentDto),
            SourceTypeFqn = typeof(T7DualAddress).FullName!, TargetTypeFqn = typeof(T7AddrFluentDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            Properties = new[] { Pm("Id", "Id"), Pm("City", "City") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("City", "String") },
        });

        // 特性对（[MapFrom]）投影可译路径
        var efAttr = _db.Rows.OrderBy(r => r.Id).Where(r => r.Id == 1).AsDto().Single<T7FlatDto>();
        Assert.Equal("Alice", efAttr.Title);

        // Fluent 对（For<T7DualAddress, T7AddrFluentDto>）——经 FluentProjectionBuilder 投影路径
        var efAddr = _db.Set<T7DualAddress>().Where(a => a.Id == 10).AsDto().Single<T7AddrFluentDto>();
        var fsAddr = _fsql.Select<T7DualAddress>().Where(a => a.Id == 10).AsDto().First<T7AddrFluentDto>();
        Assert.Equal("Shanghai", efAddr.City);
        Assert.Equal(Json(efAddr), Json(fsAddr));

        // 跨对并存：两对各自独立注册（D1 同对双声明门零命中——并存合法形态放行）
        var addrMetaHit = NCMapper.Runtime.RuntimeSnapshotStore.TryGetMappingDetail(
            typeof(T7DualAddress), typeof(T7AddrFluentDto), out _);
        Assert.True(addrMetaHit, "Fluent 对 (T7DualAddress,T7AddrFluentDto) 注册可达（混合形态并存放行——D1 门零误伤）");
    }
}

// ================================================================
// fixture（IntegrationTests 编译面——不入 generated-baseline 三腿）
// ================================================================

public class T7DualAddress
{
    public int Id { get; set; }
    public string City { get; set; } = "";
    public string Street { get; set; } = "";
}

public class T7DualCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Level { get; set; } = "";
}

public class T7DualEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Note { get; set; } = "";
    public int CustomerId { get; set; }
    public int AddressId { get; set; }

    [Navigate(nameof(AddressId))]
    public T7DualAddress? Address { get; set; }

    [Navigate(nameof(CustomerId))]
    public T7DualCustomer? Customer { get; set; }
}

/// <summary>基元 + 异名 + 类型扩展转换（int→long）</summary>
[MapFrom(typeof(T7DualEntity))]
public class T7FlatDto
{
    public int Id { get; set; }

    [MapProperty(typeof(T7DualEntity), nameof(T7DualEntity.Name))]
    public string Title { get; set; } = "";

    [MapProperty(typeof(T7DualEntity), nameof(T7DualEntity.Age))]
    public long AgeAsLong { get; set; }
}

/// <summary>D7 族（差异面 D-a）——[MapIgnore(ToEntity)] × DTO 侧锚（无点路径）</summary>
[MapFrom(typeof(T7DualEntity))]
public class T7DirIgnoreDto
{
    public int Id { get; set; }

    [MapIgnore(Direction.ToEntity)]
    public string Name { get; set; } = "";
}

/// <summary>WMI2 族 ToEntity 变体（差异面 D-d 实证形态）——点路径 Address.City + ToEntity 方向忽略</summary>
[MapFrom(typeof(T7DualEntity))]
public class T7DotPathIgnoreToEntityDto
{
    public int Id { get; set; }

    [MapProperty(typeof(T7DualEntity), "Address.City")]
    [MapIgnore(Direction.ToEntity)]
    public string CityName { get; set; } = "";
}

/// <summary>WMI2 族 ToDto 变体——单段点路径（主源属性名）+ ToDto 方向忽略（"任意方向"覆盖）</summary>
[MapFrom(typeof(T7DualEntity))]
public class T7DotPathIgnoreToDtoDto
{
    public int Id { get; set; }

    [MapProperty(typeof(T7DualEntity), nameof(T7DualEntity.Name))]
    [MapIgnore(Direction.ToDto)]
    public string Name { get; set; } = "";
}

/// <summary>嵌套导航 DTO（Address/Customer 子对）</summary>
[MapFrom(typeof(T7DualEntity))]
public class T7NestedDto
{
    public int Id { get; set; }

    [MapFrom(typeof(T7DualAddress))]
    public class AddressDto
    {
        public int Id { get; set; }
        public string City { get; set; } = "";
    }

    [MapFrom(typeof(T7DualCustomer))]
    public class CustomerDto
    {
        public int Id { get; set; }
        public string Level { get; set; } = "";
    }

    public AddressDto? Address { get; set; }
    public CustomerDto? Customer { get; set; }
}

/// <summary>record 降级形态（主构造——无参构造缺失 → 降级内存映射）</summary>
[MapFrom(typeof(T7DualEntity))]
public record T7RecordDto(int Id, string Name);

/// <summary>Fluent 对目标（混合形态 For&lt;T7DualAddress, T7AddrFluentDto&gt;）</summary>
public class T7AddrFluentDto
{
    public int Id { get; set; }
    public string City { get; set; } = "";
}

/// <summary>EFCore DbContext（独立 SQLite 内存库）</summary>
public class T7DualDbContext : DbContext
{
    private readonly string _connStr;
    public T7DualDbContext(string connStr) => _connStr = connStr;

    public DbSet<T7DualEntity> Rows => Set<T7DualEntity>();
    public DbSet<T7DualAddress> Addresses => Set<T7DualAddress>();
    public DbSet<T7DualCustomer> Customers => Set<T7DualCustomer>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connStr);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<T7DualEntity>(e =>
        {
            e.HasOne(x => x.Address).WithMany().HasForeignKey(x => x.AddressId);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId);
        });
    }
}
