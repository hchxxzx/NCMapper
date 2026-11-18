using System.IO;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// [ConvertBack] ORM 投影路径测试承诺锁定
/// <para>核心断言：DTO 仅标 [ConvertBack]（反向专用特性，正向投影不消费）时，
/// AsDto 不触发降级，走 SQL 投影路径；对照场景（同属性标 [Convert]）确认
/// 降级检测器仍正常触发，证明机制差异而非检测失效。</para>
/// </summary>
public class ConvertBackUsingProjectionTests : IDisposable
{
    private readonly ConvertBackUsingTestDbContext _db;

    public ConvertBackUsingProjectionTests()
    {
        var connStr = $"DataSource=file:cbutest{Guid.NewGuid():N}?mode=memory&cache=shared";
        _db = new ConvertBackUsingTestDbContext(connStr);
        _db.Database.EnsureCreated();
        SeedData();
    }

    public void Dispose() => _db.Dispose();

    private void SeedData()
    {
        _db.Departments.AddRange(
            new CbuDeptEntity { Id = 1, Name = "Engineering" },
            new CbuDeptEntity { Id = 2, Name = "Marketing" });
        _db.SaveChanges();
    }

    // ================================================================
    // 实体定义
    // ================================================================

    public class CbuDeptEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    // ================================================================
    // DTO 定义
    // ================================================================

    /// <summary>
    /// 仅标 [ConvertBack] 的 DTO — 反向专用特性，正向投影不消费 → 预期不降级
    /// </summary>
    [MapFrom(typeof(CbuDeptEntity))]
    public class DeptCbuOnlyDto
    {
        public int Id { get; set; }

        [ConvertBack(typeof(CbuNameBackConverter), nameof(CbuNameBackConverter.Convert))]
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 仅标 [Convert] 的 DTO（对照）— 正向投影消费转换器 → 预期触发降级
    /// </summary>
    [MapFrom(typeof(CbuDeptEntity))]
    public class DeptCuOnlyDto
    {
        public int Id { get; set; }

        [Convert(typeof(CuNameConverter), nameof(CuNameConverter.Convert))]
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 无转换器标注的 DTO（基线对照）— 预期走投影路径
    /// </summary>
    [MapFrom(typeof(CbuDeptEntity))]
    public class DeptPlainDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 反向转换器（静态方法引用形态，T1 模板 M(TField)，[ConvertBack] 用例）— 源为 DTO，正向投影不消费
    /// </summary>
    public static class CbuNameBackConverter
    {
        /// <summary>
        /// 源（DTO）→ 目标（实体）转换 — 反向专用，正向投影路径不调用
        /// </summary>
        public static string Convert(string fieldValue) => $"[{fieldValue}]";
    }

    /// <summary>
    /// 正向转换器（静态方法引用形态，T1 模板 M(TField)，[Convert] 对照用例）— 源为实体类型，降级后内存端消费
    /// </summary>
    public static class CuNameConverter
    {
        /// <summary>
        /// 源（实体）→ 目标（DTO）转换 — 大写化标记转换已执行
        /// </summary>
        public static string Convert(string fieldValue) => fieldValue.ToUpperInvariant();
    }

    // ================================================================
    // DbContext
    // ================================================================

    public class ConvertBackUsingTestDbContext : DbContext
    {
        public DbSet<CbuDeptEntity> Departments { get; set; } = null!;

        private readonly string _connectionString;

        public ConvertBackUsingTestDbContext(string connectionString) => _connectionString = connectionString;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite(_connectionString);
    }

    // ================================================================
    // 测试
    // ================================================================

    /// <summary>
    /// 仅标 [ConvertBack] 的 DTO 走投影路径（不降级）— MapTracer 断言无降级日志 + 数据正确
    /// <para>正向投影不消费反向转换器，Name 应为原值（非转换器输出）。</para>
    /// </summary>
    [Fact]
    public void ConvertBackUsing_Only_NoDegradation_ProjectionWorks()
    {
        var sw = new StringWriter();
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            var dtos = _db.Departments
                .AsDto()
                .ToList<DeptCbuOnlyDto>();

            Assert.Equal(2, dtos.Count);
            var eng = dtos.First(d => d.Id == 1);
            // 正向不消费 [ConvertBack] → 投影原值
            Assert.Equal("Engineering", eng.Name);

            // 关键断言：未触发降级（走 SQL 投影路径）
            Assert.DoesNotContain("降级", sw.ToString());
        }
        finally
        {
            MapTracer.GloballyEnabled = false;
            MapTracer.GlobalOutput = Console.Out;
        }
    }

    /// <summary>
    /// 对照：仅标 [Convert] 的 DTO 触发降级 — 降级链执行且转换器在内存端消费
    /// </summary>
    [Fact]
    public void ConvertUsing_Only_TriggersDegradation_ConverterApplied()
    {
        var sw = new StringWriter();
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            var dtos = _db.Departments
                .AsDto()
                .ToList<DeptCuOnlyDto>();

            Assert.Equal(2, dtos.Count);
            var eng = dtos.First(d => d.Id == 1);
            // 降级后内存端消费 [Convert] → 大写化
            Assert.Equal("ENGINEERING", eng.Name);

            // 关键对照断言：确认降级检测器仍正常触发
            Assert.Contains("降级", sw.ToString());
        }
        finally
        {
            MapTracer.GloballyEnabled = false;
            MapTracer.GlobalOutput = Console.Out;
        }
    }

    /// <summary>
    /// 基线对照：无转换器标注的 DTO 走投影路径（确认测试基建正常）
    /// </summary>
    [Fact]
    public void PlainDto_NoDegradation_ProjectionWorks()
    {
        var dtos = _db.Departments
            .AsDto()
            .ToList<DeptPlainDto>();

        Assert.Equal(2, dtos.Count);
        Assert.Contains(dtos, d => d.Name == "Engineering");
        Assert.Contains(dtos, d => d.Name == "Marketing");
    }
}
