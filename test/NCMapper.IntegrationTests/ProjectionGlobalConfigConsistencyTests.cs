using FreeSql;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// 投影一致性测试全局忽略与命名策略下，EFCore/FreeSql 投影结果 == 内存 Mapper.Map 结果
/// <para>对应任务验收标准 1；门禁：投影结果与内存结果不一致即失败（F6）。</para>
/// </summary>
[Collection("PipelineGlobalState")]
public class ProjectionGlobalConfigConsistencyTests : IDisposable
{
    /// <summary>
    /// 测试前重置全局配置冻结状态（测试隔离）
    /// </summary>
    public ProjectionGlobalConfigConsistencyTests() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); } // 迁移

    /// <summary>
    /// 测试后重置冻结状态 — 防止配置冻结残留污染其他测试类
    /// （发现：缺 Dispose 清理时冻结配置跨测试类残留，后续注册不同内容触发冲突）
    /// </summary>
    public void Dispose()
    {
        Mapper.ClearCache();        GlobalMapConfig.ResetForTests(); // 迁移

    }

    /// <summary>
    /// 验证：全局忽略 InternalCode → FreeSql AsDto 投影结果与内存 Mapper.Map 一致（均不映射 InternalCode）
    /// </summary>
    [Fact]
    public void FreeSql_GlobalIgnore_ProjectionMatchesMemoryMap()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["InternalCode"] },
            "ProjConsistency");

        var dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_projcons_{Guid.NewGuid():N}.db");
        try
        {
            using var fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={dbPath}")
                .UseAutoSyncStructure(true)
                .Build();
            fsql.Insert(new GcUserEntity { Id = 1, Name = "Alice", InternalCode = "SECRET-1" }).ExecuteAffrows();

            // 投影路径（触发冻结；IgnoreNames 生效）
            var projected = fsql.Select<GcUserEntity>()
                .Where(x => x.Id == 1)
                .AsDto()
                .First<GcUserDto>();

            // 内存路径（同一冻结配置）
            var entity = fsql.Select<GcUserEntity>().Where(x => x.Id == 1).First();
            var mapped = Mapper.Map<GcUserEntity, GcUserDto>(entity);

            // 一致性：两条路径均不映射 InternalCode（fail-closed 留空）
            // 一致性：两条路径均不映射 InternalCode（fail-closed 留空）
            Assert.Equal(mapped!.InternalCode, projected!.InternalCode);
            Assert.Equal("", projected.InternalCode);
            // 未忽略属性正常映射
            Assert.Equal("Alice", projected.Name);
            Assert.Equal(mapped.Name, projected.Name);
        }
        finally
        {
            if (File.Exists(dbPath)) { try { File.Delete(dbPath); } catch { } }
        }
    }

    /// <summary>
    /// 验证：SnakeCase 策略 → FreeSql AsDto 投影与内存 Mapper.Map 均匹配 user_name ↔ UserName
    /// </summary>
    [Fact]
    public void FreeSql_SnakeCase_ProjectionMatchesMemoryMap()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase },
            "ProjConsistency");

        var dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_projcons_{Guid.NewGuid():N}.db");
        try
        {
            using var fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={dbPath}")
                .UseAutoSyncStructure(true)
                .Build();
            fsql.Insert(new GcSnakeEntity { Id = 1, user_name = "Bob" }).ExecuteAffrows();

            var projected = fsql.Select<GcSnakeEntity>()
                .Where(x => x.Id == 1)
                .AsDto()
                .First<GcSnakeDto>();

            var entity = fsql.Select<GcSnakeEntity>().Where(x => x.Id == 1).First();
            var mapped = Mapper.Map<GcSnakeEntity, GcSnakeDto>(entity);

            Assert.Equal("Bob", projected!.UserName);
            Assert.Equal(mapped!.UserName, projected.UserName);
        }
        finally
        {
            if (File.Exists(dbPath)) { try { File.Delete(dbPath); } catch { } }
        }
    }

    /// <summary>
    /// 验证：全局忽略 InternalCode → EFCore 投影结果与内存 Mapper.Map 一致
    /// </summary>
    [Fact]
    public void EFCore_GlobalIgnore_ProjectionMatchesMemoryMap()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["InternalCode"] },
            "ProjConsistency");

        var connStr = $"DataSource=file:projcons{Guid.NewGuid():N}?mode=memory&cache=shared";
        var options = new DbContextOptionsBuilder<GcTestDbContext>()
            .UseSqlite(connStr)
            .Options;
        using var db = new GcTestDbContext(options);
        db.Database.EnsureCreated();
        db.GcUsers.Add(new GcUserEntity { Id = 1, Name = "Carol", InternalCode = "SECRET-2" });
        db.SaveChanges();

        // 投影路径
        var projected = db.GcUsers
            .Select(EfCoreProjectionBuilder.Build<GcUserEntity, GcUserDto>())
            .First();

        // 内存路径
        var entity = db.GcUsers.First();
        var mapped = Mapper.Map<GcUserEntity, GcUserDto>(entity);

        Assert.Equal(mapped!.InternalCode, projected.InternalCode);
        Assert.Equal("", projected.InternalCode);
        Assert.Equal("Carol", projected.Name);
    }

    /// <summary>
    /// 验证：豁免（OverrideIgnore=true）命中全局忽略时，FreeSql 投影保留该属性 == 内存 Mapper.Map 保留
    /// <para>修复前投影端未传豁免参数 → 投影剔除豁免属性 → 与内存映射分歧</para>
    /// </summary>
    [Fact]
    public void FreeSql_OverrideIgnore_ProjectionKeepsProperty()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["InternalCode"] },
            "ProjConsistencyExempt");

        var dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_projcons_{Guid.NewGuid():N}.db");
        try
        {
            using var fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={dbPath}")
                .UseAutoSyncStructure(true)
                .Build();
            fsql.Insert(new GcExemptUserEntity { Id = 1, Name = "ExemptAlice", InternalCode = "SECRET-X" }).ExecuteAffrows();

            var projected = fsql.Select<GcExemptUserEntity>()
                .Where(x => x.Id == 1)
                .AsDto()
                .First<GcExemptUserDto>();

            var entity = fsql.Select<GcExemptUserEntity>().Where(x => x.Id == 1).First();
            var mapped = Mapper.Map<GcExemptUserEntity, GcExemptUserDto>(entity);

            // 豁免生效：两条路径均保留 InternalCode（与无豁免场景 fail-closed 相反）
            Assert.Equal(mapped!.InternalCode, projected!.InternalCode);
            Assert.Equal("SECRET-X", projected.InternalCode);
            Assert.Equal("ExemptAlice", projected.Name);
        }
        finally
        {
            if (File.Exists(dbPath)) { try { File.Delete(dbPath); } catch { } }
        }
    }

    /// <summary>
    /// 验证：豁免（OverrideIgnore=true）命中全局忽略时，EFCore 投影保留该属性 == 内存 Mapper.Map 保留
    /// <para>修复前 EFCore 投影剔除豁免属性，查询结果与 Mapper.Map 直接分歧（最严重语义分歧场景

    /// </summary>
    [Fact]
    public void EFCore_OverrideIgnore_ProjectionKeepsProperty()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["InternalCode"] },
            "ProjConsistencyExempt");

        var connStr = $"DataSource=file:projcons{Guid.NewGuid():N}?mode=memory&cache=shared";
        var options = new DbContextOptionsBuilder<GcTestDbContext>()
            .UseSqlite(connStr)
            .Options;
        using var db = new GcTestDbContext(options);
        db.Database.EnsureCreated();
        db.GcExemptUsers.Add(new GcExemptUserEntity { Id = 1, Name = "ExemptCarol", InternalCode = "SECRET-Y" });
        db.SaveChanges();

        var projected = db.GcExemptUsers
            .Select(EfCoreProjectionBuilder.Build<GcExemptUserEntity, GcExemptUserDto>())
            .First();

        var entity = db.GcExemptUsers.First();
        var mapped = Mapper.Map<GcExemptUserEntity, GcExemptUserDto>(entity);

        Assert.Equal(mapped!.InternalCode, projected.InternalCode);
        Assert.Equal("SECRET-Y", projected.InternalCode);
        Assert.Equal("ExemptCarol", projected.Name);
    }

    /// <summary>
    /// 验证：异名 FromField 源字段命中全局忽略 → EFCore 投影剔除该属性，与内存 Mapper.Map 一致
    /// <para>修复前投影仅目标侧判定（ExternalCode 不命中），源字段 InternalCode 命中但被投影保留

    /// </summary>
    [Fact]
    public void EFCore_FromFieldSourceGlobalIgnore_ProjectionMatchesMemoryMap()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["InternalCode"] },
            "ProjConsistencySrcSide");

        var connStr = $"DataSource=file:projcons{Guid.NewGuid():N}?mode=memory&cache=shared";
        var options = new DbContextOptionsBuilder<GcTestDbContext>()
            .UseSqlite(connStr)
            .Options;
        using var db = new GcTestDbContext(options);
        db.Database.EnsureCreated();
        db.FgOrders.Add(new FgOrderEntity { Id = 1, Name = "SrcSideCarol", InternalCode = "SECRET-Z" });
        db.SaveChanges();

        var projected = db.FgOrders
            .Select(EfCoreProjectionBuilder.Build<FgOrderEntity, FgOrderDto>())
            .First();

        var entity = db.FgOrders.First();
        var mapped = Mapper.Map<FgOrderEntity, FgOrderDto>(entity);

        // 源侧命中：两条路径均不映射 ExternalCode（fail-closed）
        Assert.Equal(mapped!.ExternalCode, projected!.ExternalCode);
        Assert.Equal("", projected.ExternalCode);
        Assert.Equal("SrcSideCarol", projected.Name);
    }

    /// <summary>
    /// 验证：异名 FromField 源字段命中全局忽略 → FreeSql 触发降级且剔除，与内存 Mapper.MapList 一致
    /// <para>修复前 FreeSql 降级检测器仅目标侧判定（CR-2/F1 登记缺口）→ 不降级且投影保留源忽略列

    /// </summary>
    [Fact]
    public void FreeSql_FromFieldSourceGlobalIgnore_DegradesAndMatchesMemoryMap()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["InternalCode"] },
            "ProjConsistencySrcSide");

        var dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_projcons_{Guid.NewGuid():N}.db");
        try
        {
            using var fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={dbPath}")
                .UseAutoSyncStructure(true)
                .Build();
            fsql.Insert(new FgOrderEntity { Id = 1, Name = "SrcSideAlice", InternalCode = "SECRET-W" }).ExecuteAffrows();

            var projected = fsql.Select<FgOrderEntity>()
                .Where(x => x.Id == 1)
                .AsDto()
                .First<FgOrderDto>();

            var entity = fsql.Select<FgOrderEntity>().Where(x => x.Id == 1).First();
            var mapped = Mapper.Map<FgOrderEntity, FgOrderDto>(entity);

            // 源侧命中：两条路径均不映射 ExternalCode（fail-closed）
            Assert.Equal(mapped!.ExternalCode, projected!.ExternalCode);
            Assert.Equal("", projected.ExternalCode);
            Assert.Equal("SrcSideAlice", projected.Name);
        }
        finally
        {
            if (File.Exists(dbPath)) { try { File.Delete(dbPath); } catch { } }
        }
    }
}

/// <summary>
/// 一致性测试实体 — 含全局忽略目标属性 InternalCode
/// </summary>
public class GcUserEntity
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 内部编码（全局忽略清单命中目标）
    /// </summary>
    public string InternalCode { get; set; } = "";
}

/// <summary>
/// 一致性测试 DTO
/// </summary>
[MapFrom(typeof(GcUserEntity))]
public class GcUserDto
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 内部编码
    /// </summary>
    public string InternalCode { get; set; } = "";
}

/// <summary>
/// SnakeCase 一致性测试实体 — snake_case 属性名
/// </summary>
public class GcSnakeEntity
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 用户名（snake_case）
    /// </summary>
    public string user_name { get; set; } = "";
}

/// <summary>
/// 豁免一致性测试实体 — InternalCode 命中全局忽略清单
/// </summary>
public class GcExemptUserEntity
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 内部编码（全局忽略清单命中目标）
    /// </summary>
    public string InternalCode { get; set; } = "";
}

/// <summary>
/// 豁免一致性测试 DTO — InternalCode 带 OverrideIgnore=true 豁免
/// </summary>
[MapFrom(typeof(GcExemptUserEntity))]
public class GcExemptUserDto
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 内部编码（OverrideIgnore=true 豁免全局忽略，映射对整体生效）
    /// </summary>
    [MapProperty(typeof(GcExemptUserEntity), "InternalCode", OverrideIgnore = true)]
    public string InternalCode { get; set; } = "";
}

/// <summary>
/// 源侧判定测试实体 — InternalCode 为源侧命中目标（异名 FromField 指向）
/// </summary>
public class FgOrderEntity
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 内部编码（全局忽略清单命中源字段）
    /// </summary>
    public string InternalCode { get; set; } = "";
}

/// <summary>
/// 源侧判定测试 DTO — ExternalCode 经 FromField 指向源 InternalCode
/// </summary>
[MapFrom(typeof(FgOrderEntity))]
public class FgOrderDto
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 外部编码（异名 FromField 指向源 InternalCode，源侧命中全局忽略 → 剔除）
    /// </summary>
    [MapProperty(typeof(FgOrderEntity), "InternalCode")]
    public string ExternalCode { get; set; } = "";
}

/// <summary>
/// SnakeCase 一致性测试 DTO — PascalCase 属性名
/// </summary>
[MapFrom(typeof(GcSnakeEntity))]
public class GcSnakeDto
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string UserName { get; set; } = "";
}

/// <summary>
/// 一致性测试 EFCore 上下文
/// </summary>
public class GcTestDbContext : DbContext
{
    /// <summary>
    /// 用户实体集
    /// </summary>
    public DbSet<GcUserEntity> GcUsers => Set<GcUserEntity>();

    /// <summary>
    /// 豁免测试用户实体集
    /// </summary>
    public DbSet<GcExemptUserEntity> GcExemptUsers => Set<GcExemptUserEntity>();

    /// <summary>
    /// 源侧判定测试订单实体集
    /// </summary>
    public DbSet<FgOrderEntity> FgOrders => Set<FgOrderEntity>();

    /// <summary>
    /// 创建上下文
    /// </summary>
    /// <param name="options">配置选项</param>
    public GcTestDbContext(DbContextOptions<GcTestDbContext> options) : base(options) { }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GcUserEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name);
            e.Property(x => x.InternalCode);
        });
        modelBuilder.Entity<GcExemptUserEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name);
            e.Property(x => x.InternalCode);
        });
        modelBuilder.Entity<FgOrderEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name);
            e.Property(x => x.InternalCode);
        });
    }
}
