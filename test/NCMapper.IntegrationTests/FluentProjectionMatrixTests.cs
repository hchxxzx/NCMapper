// 投影绑定明细消费集成——翻译矩阵端到端（EFCore SQLite）
//   ①纯可翻译 Fluent DTO（Map 异名）从 G1-① 整体降级分流为投影路径：AsDto 走 SQL 投影且注册表
//     异名明细消费（Title←RealName）——F5 防御主用例；
//   ②Compose 声明 Fluent DTO 强制降级不抛（HasComposeMappings 判定——FromSource 无运行时明细承载）；
//   ③[MapFrom] 通道投影回归由既有 IntegrationTests 全量覆盖（标准 4，不新增）。
// 本工程未引用 NCMapper.SourceGen（边界）——Fluent 注册为手工自足形态（与先例同范式）。
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.EFCore;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.IntegrationTests;

[Collection("PipelineGlobalState")]
public class FluentProjectionMatrixTests : IDisposable
{
    // ================================================================
    // 夹具
    // ================================================================

    public class FpmEntity
    {
        public int Id { get; set; }
        public string RealName { get; set; } = "";
    }

    // 命名避开 Dto 后缀（NCMAP019 命名启发式噪音规避先例同款）
    public class FpmView
    {
        public int Id { get; set; }
        public string AliasName { get; set; } = "";
    }

    public class CpmEntity
    {
        public int Id { get; set; }
        public string Ext { get; set; } = "";
    }

    public class CpmView
    {
        public int Id { get; set; }
        public string Ext { get; set; } = "";
    }

    private static void ClearRegistryState()
    {
        RuntimeSnapshotStore.Clear();
        EfCoreDegradationDetector.ClearCache();
        NCMapper.FreeSql.FreeSqlDegradationDetector.ClearCache();
    }

    public FluentProjectionMatrixTests()
    {
        var connStr = $"DataSource=file:fpmx{Guid.NewGuid():N}?mode=memory&cache=shared";
        _db = new FpmDbContext(connStr);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { ClearRegistryState(); } catch { }
    }

    private sealed class FpmDbContext : DbContext
    {
        private readonly string _connStr;
        public FpmDbContext(string connStr) : base() => _connStr = connStr;
        public DbSet<FpmEntity> FpmEntities => Set<FpmEntity>();
        public DbSet<CpmEntity> CpmEntities => Set<CpmEntity>();
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite(_connStr);
    }

    private readonly FpmDbContext _db;

    // ================================================================
    // 用例 1（标准 1 + F5 防御）：纯可翻译 Fluent DTO 走投影路径，注册表异名明细消费
    // ================================================================

    [Fact]
    [Trait("Capability", "A-01")] // Fluent 对级声明 ORM 投影消费锚
    [Trait("Capability", "A-07")] // 异名 Map 消费（A-07 显式属性映射 ORM 面）
    public void PureMapFluent_AsDto_ProjectionPath_ConsumesRegistryAlias()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(FpmEntity),
                TargetType = typeof(FpmView),
                SourceTypeFqn = typeof(FpmEntity).FullName!,
                TargetTypeFqn = typeof(FpmView).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[]
                {
                    new RuntimePropertyMapping
                    {
                        Target = new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" },
                        SourceName = "Id",
                    },
                    new RuntimePropertyMapping
                    {
                        Target = new RuntimeTargetPropertySlot { Name = "AliasName", TypeName = "String" },
                        SourceName = "RealName", // 异名声明（G1-② 前投影绑定读不到 → 静默丢）
                    },
                },
                AllTargetSlots = new[]
                {
                    new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" },
                    new RuntimeTargetPropertySlot { Name = "AliasName", TypeName = "String" },
                },
            });

            // 判定面：纯可翻译 → 不降级（投影路径实证）
            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(FpmEntity), typeof(FpmView), out _);
            Assert.False(needs, "纯 Map 异名 Fluent DTO 应走投影路径（G1-② 声明级分流）");

            _db.FpmEntities.Add(new FpmEntity { Id = 1, RealName = "张三" });
            _db.SaveChanges();

            var rows = _db.FpmEntities.OrderBy(e => e.Id).AsDto().ToList<FpmView>();

            Assert.Single(rows);
            Assert.Equal(1, rows[0].Id);
            Assert.Equal("张三", rows[0].AliasName); // 注册表异名明细投影消费——G1-① 形态下此值为空（静默丢）
        }
        finally
        {
            ClearRegistryState();
        }
    }

    // ================================================================
    // 用例 2（标准 2 邻接 + F5 防御）：Compose 声明 Fluent DTO 强制降级不抛
    // ================================================================

    [Fact]
    public void ComposeFluent_AsDto_ForcesDegrade_NoThrow()
    {
        try
        {
            ClearRegistryState();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(CpmEntity),
                TargetType = typeof(CpmView),
                SourceTypeFqn = typeof(CpmEntity).FullName!,
                TargetTypeFqn = typeof(CpmView).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                HasComposeMappings = true, // FromSource 声明无 pm 明细——运行时判不可翻译的唯一可见信号
                Properties = new[]
                {
                    new RuntimePropertyMapping
                    {
                        Target = new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" },
                        SourceName = "Id",
                    },
                },
            });

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(CpmEntity), typeof(CpmView), out var reason);
            Assert.True(needs, "Compose 声明 Fluent DTO 必须强制降级（多源 JOIN 无运行时承载）");
            Assert.Contains("Compose", reason);

            _db.CpmEntities.Add(new CpmEntity { Id = 7, Ext = "e" });
            _db.SaveChanges();

            var rows = _db.CpmEntities.OrderBy(e => e.Id).AsDto().ToList<CpmView>();
            Assert.Single(rows);
            Assert.Equal(7, rows[0].Id); // 降级内存映射产出（不抛 L1 / 不静默丢 L2）
        }
        finally
        {
            ClearRegistryState();
        }
    }
}
