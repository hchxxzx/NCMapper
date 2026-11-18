using System.IO;
using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// Validator/Tracer 全局配置感知测试— 全局忽略不计未映射误报、Tracer GlobalIgnore 原因、
/// Validate 触发冻结（v1.2 G6）
/// </summary>
[Collection("GlobalConfigState")]
public class ValidatorTracerGlobalConfigTests : IDisposable
{
    /// <summary>
    /// 测试前重置全局配置冻结状态（测试隔离）
    /// </summary>
    public ValidatorTracerGlobalConfigTests()
    {
        Mapper.ClearCache();        // 迁移：Reset 不再清注册表，测试隔离需显式全清
        GlobalMapConfig.ResetForTests();
    }

    /// <summary>
    /// 类级清理 — 消除集合结束后冻结配置残留（进程级单例，
    /// 残留会污染并行/后续执行的未感知测试类，差异 #12 同类项）
    /// </summary>
    public void Dispose() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); } // 迁移

    /// <summary>
    /// 验证：配置全局忽略后 Validate() 不对被忽略属性报未映射
    /// </summary>
    [Fact]
    public void Validate_GlobalIgnoredProperty_NotReportedAsUnmapped()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["InternalCode"] },
            "ValidatorTest");

        var results = MapValidator.Validate(typeof(ValidatorTracerGlobalConfigTests).Assembly);

        var result = results.FirstOrDefault(r => r.TargetType == typeof(GlobalIgnoreDto));
        Assert.NotNull(result);
        Assert.DoesNotContain(result!.UnmappedProperties, p => p.Contains("InternalCode"));
    }

    /// <summary>
    /// 验证：无全局配置时被忽略属性（[MapIgnore]）同样不误报（现状行为保留）
    /// </summary>
    [Fact]
    public void Validate_MapIgnoreProperty_NotReportedAsUnmapped()
    {
        var results = MapValidator.Validate(typeof(ValidatorTracerGlobalConfigTests).Assembly);

        var result = results.FirstOrDefault(r => r.TargetType == typeof(AttributeIgnoreDto));
        Assert.NotNull(result);
        Assert.DoesNotContain(result!.UnmappedProperties, p => p.Contains("SkippedByAttribute"));
    }

    /// <summary>
    /// 验证：Tracer 输出 GlobalIgnore 跳过原因
    /// </summary>
    [Fact]
    public void Tracer_GlobalIgnoredProperty_ReportsGlobalIgnoreReason()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["InternalCode"] },
            "TracerTest");

        var report = MapTracer.Trace<TracerSourceEntity, GlobalIgnoreDto>(new TracerSourceEntity
        {
            Name = "n",
            InternalCode = "secret",
        });

        Assert.Contains("InternalCode: skipped (GlobalIgnore)", report);
        // 未忽略属性正常映射
        Assert.Contains("Name:", report);
        Assert.DoesNotContain("InternalCode: ⚠️", report);
    }

    /// <summary>
    /// 验证：Tracer 对 [MapIgnore] 属性输出 MapIgnore 原因（现状行为保留）
    /// </summary>
    [Fact]
    public void Tracer_MapIgnoreProperty_ReportsMapIgnoreReason()
    {
        var report = MapTracer.Trace<TracerSourceEntity, AttributeIgnoreDto>(new TracerSourceEntity
        {
            Name = "n",
        });

        Assert.Contains("SkippedByAttribute: skipped (MapIgnore)", report);
    }

    /// <summary>
    /// 验证：Validate() 触发全局配置冻结（G6）— 冻结后注册不同配置抛异常，重复 Validate 幂等
    /// </summary>
    [Fact]
    public void Validate_TriggersFreeze_AndRepeatedCallsAreIdempotent()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase },
            "FreezeAsm");

        // Validate 触发冻结
        _ = MapValidator.Validate(typeof(ValidatorTracerGlobalConfigTests).Assembly);

        // 重复 Validate 幂等（无副作用）
        _ = MapValidator.Validate(typeof(ValidatorTracerGlobalConfigTests).Assembly);
        _ = MapValidator.Validate();

        // 冻结后注册不同配置 → 当场抛（冲突检测）
        var ex = Assert.Throws<MapConfigurationException>(() =>
            Mapper.RegisterGeneratedConfig(
                new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.CaseSensitive },
                "FreezeAsm2"));
        Assert.Contains("FreezeAsm2", ex.Message);
    }

    /// <summary>
    /// 验证：Tracer 对豁免属性（OverrideIgnore=true）不误报 skipped (GlobalIgnore)
    /// <para>修复前未传豁免参数 → 豁免属性仍报 GlobalIgnore（AUD-7）</para>
    /// </summary>
    [Fact]
    public void Tracer_OverrideIgnoreProperty_NotReportedAsGlobalIgnore()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["InternalCode"] },
            "TracerExemptTest");

        var report = MapTracer.Trace<TracerSourceEntity, ExemptTraceDto>(new TracerSourceEntity
        {
            Name = "n",
            InternalCode = "secret",
        });

        Assert.DoesNotContain("InternalCode: skipped (GlobalIgnore)", report);
        Assert.Contains("InternalCode:", report);
        Assert.Contains("Name:", report);
    }

    /// <summary>
    /// 验证：Tracer 对源侧命中全局忽略（异名 FromField 源字段）报 GlobalIgnore（source side）
    /// <para>修复前 Tracer 仅目标侧判定 → 源侧命中漏报（AUD-7）</para>
    /// </summary>
    [Fact]
    public void Tracer_FromFieldSourceGlobalIgnore_ReportsSourceSide()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["InternalCode"] },
            "TracerSrcSideTest");

        var report = MapTracer.Trace<TracerSourceEntity, SourceSideTraceDto>(new TracerSourceEntity
        {
            Name = "n",
            InternalCode = "secret",
        });

        Assert.Contains("ExternalCode: skipped (GlobalIgnore, source side)", report);
        Assert.Contains("Name:", report);
    }
}

/// <summary>
/// 全局忽略验证 DTO — InternalCode 命中全局忽略清单
/// </summary>
[MapFrom(typeof(TracerSourceEntity))]
public class GlobalIgnoreDto
{
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
/// 属性级 [MapIgnore] 验证 DTO
/// </summary>
[MapFrom(typeof(TracerSourceEntity))]
public class AttributeIgnoreDto
{
    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 属性级忽略属性
    /// </summary>
    [MapIgnore]
    public string SkippedByAttribute { get; set; } = "";
}

/// <summary>
/// Tracer 验证源实体
/// </summary>
public class TracerSourceEntity
{
    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 内部编码
    /// </summary>
    public string InternalCode { get; set; } = "";

    /// <summary>
    /// 属性级忽略源属性
    /// </summary>
    public string SkippedByAttribute { get; set; } = "";
}

/// <summary>
/// Tracer 豁免验证 DTO — InternalCode 带 OverrideIgnore=true 豁免
/// </summary>
[MapFrom(typeof(TracerSourceEntity))]
public class ExemptTraceDto
{
    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 内部编码（OverrideIgnore=true 豁免全局忽略）
    /// </summary>
    [MapProperty(typeof(TracerSourceEntity), "InternalCode", OverrideIgnore = true)]
    public string InternalCode { get; set; } = "";
}

/// <summary>
/// Tracer 源侧判定验证 DTO — ExternalCode 异名指向源 InternalCode
/// </summary>
[MapFrom(typeof(TracerSourceEntity))]
public class SourceSideTraceDto
{
    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 外部编码（FromField 指向源 InternalCode，源侧命中全局忽略）
    /// </summary>
    [MapProperty(typeof(TracerSourceEntity), "InternalCode")]
    public string ExternalCode { get; set; } = "";
}
