// ① 降级兜底——双 ORM AsDto 机制断言
//   on/off 双注册表状态 × EFCore/FreeSql：Fluent 声明 DTO 强制降级（不抛 MapFromNotApplied L1）
//   + 降级原因可判读 + [MapFrom] 回归抽查（Fluent 顶层判定不误伤特性通道）。
//   注：本工程未引用 NCMapper.SourceGen（等价「总开关关闭」环境——R-A 承诺域边界），
//   内存映射的「全语义不丢」断言由 NCMapper.Tests/FluentDegradationFullSemanticsTests 承载
//   （该工程含生成器，降级路径调用的生成映射器具备异名/Mask/FromJson 全语义；等价门 A-01/A-03/A-12 双通道锁定）。
// 隔离范式（经验库 #60/#65/#366）：程序集级 DisableTestParallelization（AssemblyInfo.cs）+
//   每用例自足注册（Clear + 幂等 Register + 检测器 ClearCache）+ try/finally 清理——用例与执行顺序解耦。
using FreeSql;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.IntegrationTests;

[Collection("PipelineGlobalState")]
public class FluentDegradationFallbackTests : IDisposable
{
    // ================================================================
    // 夹具：Fluent 声明 DTO（含异名/Mask/FromJson 声明——本工程断言降级机制，语义断言另址）
    // ================================================================

    public class FbEntity
    {
        public int Id { get; set; }
        public string RealName { get; set; } = "";
        public string Phone { get; set; } = "";
    }

    public class FbPayload
    {
        public string K { get; set; } = "";
    }

    // 命名避开 Dto 后缀（豁免落地前的 NCMAP019 命名启发式噪音）
    public class FbView
    {
        public int Id { get; set; }
        public string AliasName { get; set; } = "";
        public string Phone { get; set; } = "";
    }

    [MapperConfig]
    internal class FbFluentMapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<FbEntity, FbView>()
               .Member(d => d.AliasName).From(s => s.RealName)   // 异名 From
               .Member(d => d.Phone).Mask<MaskPhone>();    // 脱敏（FromJson 声明移除——见下注）
            // 注：不含 Member().FromJson()——本工程未引用 SourceGen（无生成映射器），降级后的反射引擎
            // 对 String→复杂类型 直接抛转换异常（总开关关闭边界行为）；FromJson 全语义断言在
            // NCMapper.Tests/FluentDegradationFullSemanticsTests（生成器环境）承载。
        }
    }

    private static FbEntity NewEntity() => new()
    {
        Id = 1,
        RealName = "张三",
        Phone = "13812345678",
    };

    public FluentDegradationFallbackTests()
    {
        var connStr = $"DataSource=file:fbdg{Guid.NewGuid():N}?mode=memory&cache=shared";
        _db = new FbDbContext(connStr);
        _db.Database.EnsureCreated();

        _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_fbdg_{Guid.NewGuid():N}.db");
        _fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
    }

    public void Dispose()
    {
        _db.Dispose();
        _fsql.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private sealed class FbDbContext : DbContext
    {
        private readonly string _connStr;
        public FbDbContext(string connStr) : base() => _connStr = connStr;
        public DbSet<FbEntity> FbEntities => Set<FbEntity>();
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite(_connStr);
    }

    private readonly FbDbContext _db;
    private readonly IFreeSql _fsql;
    private readonly string _dbPath;

    // ================================================================
    // 注册状态工具（on 态 = 身份+明细双笔；off 态 = 仅身份——与发射产物形态一致）
    // ================================================================

    private static void ClearRegistryState()
    {
        RuntimeSnapshotStore.Clear();
        EfCoreDegradationDetector.ClearCache();
        FreeSqlDegradationDetector.ClearCache();
    }

    private static void RegisterIdentity()
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(FbEntity),
            TargetType = typeof(FbView),
            SourceTypeFqn = typeof(FbEntity).FullName!,
            TargetTypeFqn = typeof(FbView).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
        });
    }

    private static void RegisterDetail()
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(FbEntity),
            TargetType = typeof(FbView),
            SourceTypeFqn = typeof(FbEntity).FullName!,
            TargetTypeFqn = typeof(FbView).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            Properties = new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "AliasName", TypeName = "String" },
                    SourceName = "RealName",
                },
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "Phone", TypeName = "String" },
                    SourceName = "Phone",
                    HasMask = true,
                    MaskTypeName = "NCMapper.MaskPhone",
                },
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "Info", TypeName = "FbPayload" },
                    SourceName = "Info",
                    IsFromJson = true,
                },
            },
        });
    }

    // ================================================================
    // 用例 1：默认 on 态（注册表 = 身份+明细合并）——双 ORM AsDto 降级不抛（L1 消除）
    // ================================================================

    [Fact]
    public void OnState_FluentDto_AsDto_DegradesWithoutThrow_BothOrms()
    {
        try
        {
            ClearRegistryState();
            RegisterIdentity();
            RegisterDetail(); // 与 on 态双笔登记同形态（Register 幂等合并收敛）

            Assert.True(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(FbView)));

            var source = NewEntity();
            _db.FbEntities.Add(source);
            _db.SaveChanges();
            _fsql.Insert(source).ExecuteInserted().ToList();

            // 显式 off 态的 L1 异常（MapFromNotApplied）在 on/off 双态均不得出现——降级兜底生效
            var ef = _db.FbEntities.OrderBy(e => e.Id).AsDto().ToList<FbView>();
            var fs = _fsql.Select<FbEntity>().OrderBy(e => e.Id).AsDto().ToList<FbView>();

            Assert.Single(ef);
            Assert.Single(fs);
            Assert.Equal(1, ef[0].Id);
            Assert.Equal(1, fs[0].Id);
        }
        finally
        {
            ClearRegistryState();
        }
    }

    // ================================================================
    // 用例 2：显式 off 态模拟（注册表 = 仅身份键，无明细）——强制降级与开关解耦
    // ================================================================

    [Fact]
    public void OffStateSimulated_IdentityOnlyRegistry_DegradesWithoutThrow()
    {
        try
        {
            ClearRegistryState();
            RegisterIdentity(); // 仅身份（off 态构建下明细段零产物独立输出节点形态）

            Assert.True(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(FbView)));
            Assert.Null(RuntimeSnapshotStore.GetTreeConfig(typeof(FbView)));

            var source = NewEntity();
            _db.FbEntities.Add(source);
            _db.SaveChanges();
            _fsql.Insert(source).ExecuteInserted().ToList();

            var ef = _db.FbEntities.OrderBy(e => e.Id).AsDto().ToList<FbView>();
            var fs = _fsql.Select<FbEntity>().OrderBy(e => e.Id).AsDto().ToList<FbView>();

            Assert.Single(ef);
            Assert.Single(fs);
        }
        finally
        {
            ClearRegistryState();
        }
    }

    // ================================================================
    // 用例 3：降级原因可判读（reason 含 Fluent 语义标识——双 ORM 检测器）
    // ================================================================

    [Fact]
    public void NeedsDegradation_ReasonReadable_FluentMarker()
    {
        try
        {
            ClearRegistryState();
            RegisterIdentity();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(FbEntity), typeof(FbView), out var reasonEf);
            Assert.True(needs);
            Assert.Contains("Fluent", reasonEf);

            var needsFs = FreeSqlDegradationDetector.NeedsDegradation(typeof(FbEntity), typeof(FbView), out var reasonFs);
            Assert.True(needsFs);
            Assert.Contains("Fluent", reasonFs);
        }
        finally
        {
            ClearRegistryState();
        }
    }

    // ================================================================
    // 用例 4：[MapFrom] 回归抽查——Fluent 顶层判定不误伤特性通道
    //（[MapFrom] DTO 无其它降级触发因子时 NeedsDegradation 仍 false，投影路径零变化）
    // ================================================================

    [Fact]
    public void MapFromDto_NotForcedByFluentCheck()
    {
        try
        {
            ClearRegistryState(); // 空注册表：IsFluentRegisteredDto 对任意类型均 false

            Assert.False(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(PlainAttrDto)));

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(PlainAttrEntity), typeof(PlainAttrDto), out _);
            Assert.False(needs);

            var needsFs = FreeSqlDegradationDetector.NeedsDegradation(typeof(PlainAttrEntity), typeof(PlainAttrDto), out _);
            Assert.False(needsFs);
        }
        finally
        {
            ClearRegistryState();
        }
    }

    public class PlainAttrEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    [NCMapper.Attributes.MapFrom(typeof(PlainAttrEntity))]
    public class PlainAttrDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
