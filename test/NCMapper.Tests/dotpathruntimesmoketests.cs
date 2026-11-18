using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 显式点路径运行时冒烟测试（Batch-1）— A3-1~A3-5 + A5-1
/// <para>覆盖：同源点路径值正确（两段/三段）/ 缺失段与值类型中间节点抛 MapConfigurationException（且不进失败缓存）/
/// 缓存键 (Type, PropertyInfo) 同名字段互不污染 / 跨表字段点路径（含中间 null → default 不抛）/
/// IsFromJson 优先（C9）/ MapValidator 验证端同步拆段（H23）</para>
/// <para>路径说明：DTO 为 private 嵌套类，源生成器跳过（NCMAP011），Mapper.Map 走运行时 ExpressionBuilder 路径。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class DotPathRuntimeSmokeTests : IDisposable
{
    public DotPathRuntimeSmokeTests() => Mapper.ClearCache();

    public void Dispose() => Mapper.ClearCache();

    // ==================== 模型 ====================

    private class RpCity
    {
        public string? Name { get; set; }
    }

    private class RpAddress
    {
        public RpCity? City { get; set; }
        public int Zip { get; set; }
    }

    private class RpHome
    {
        public string? City { get; set; }
    }

    private class RpEntity
    {
        public RpAddress? Address { get; set; }
        public RpHome? Home { get; set; }
        public string? JsonData { get; set; }
    }

    [MapFrom(typeof(RpEntity))]
    private class RpDto
    {
        [MapProperty(typeof(RpEntity), "Address.City.Name")]
        public string? CityName { get; set; }
    }

    // 同名字段、不同显式路径（缓存键互不污染场景）
    [MapFrom(typeof(RpEntity))]
    private class RpDtoA
    {
        [MapProperty(typeof(RpEntity), "Address.City.Name")]
        public string? City { get; set; }
    }

    [MapFrom(typeof(RpEntity))]
    private class RpDtoB
    {
        [MapProperty(typeof(RpEntity), "Home.City")]
        public string? City { get; set; }
    }

    [MapFrom(typeof(RpEntity))]
    private class RpDtoMissing
    {
        [MapProperty(typeof(RpEntity), "Address.Missing")]
        public string? City { get; set; }
    }

    [MapFrom(typeof(RpEntity))]
    private class RpDtoValueTypeInterrupt
    {
        [MapProperty(typeof(RpEntity), "Address.Zip.X")]
        public int X { get; set; }
    }

    [MapFrom(typeof(RpEntity))]
    private class RpDtoIsFromJson
    {
        [MapProperty(typeof(RpEntity), "Address.City.Name", IsFromJson = true)]
        public string? CityName { get; set; }
    }

    // 跨表：FromSource != 主源，导航属性 Customer → Address.City
    private class RpCustomer
    {
        public RpAddress? Address { get; set; }
    }

    private class RpOrderEntity
    {
        public RpCustomer? Customer { get; set; }
    }

    [MapFrom(typeof(RpOrderEntity))]
    private class RpOrderDto
    {
        [MapProperty(typeof(RpCustomer), "Address.City.Name")]
        public string? CityName { get; set; }
    }

    // ==================== A3-1：同源点路径映射值正确 ====================

    [Fact]
    public void SameSourceDotPath_ThreeLevels_ValueCorrect()
    {
        var entity = new RpEntity
        {
            Address = new RpAddress { City = new RpCity { Name = "Shanghai" } }
        };

        var dto = Mapper.Map<RpEntity, RpDto>(entity);

        Assert.Equal("Shanghai", dto!.CityName);
    }

    [Fact]
    public void SameSourceDotPath_IntermediateNull_DefaultNoThrow()
    {
        var entity = new RpEntity { Address = null };

        var dto = Mapper.Map<RpEntity, RpDto>(entity);

        Assert.Null(dto!.CityName);
    }

    // ==================== A3-2：失败抛 MapConfigurationException（不进失败缓存） ====================

    [Fact]
    public void SameSourceDotPath_MissingSegment_Throws_AndRepeatedCallsStillThrow()
    {
        var entity = new RpEntity { Address = new RpAddress() };

        // 第一次调用抛（可观测，C6）
        var ex = Assert.Throws<MapConfigurationException>(() => Mapper.Map<RpEntity, RpDtoMissing>(entity));
        Assert.Contains("Missing", ex.Message);

        // 第二次调用仍抛——显式失败不进失败缓存（G4：可观测性不随缓存命中消失）
        Assert.Throws<MapConfigurationException>(() => Mapper.Map<RpEntity, RpDtoMissing>(entity));
    }

    [Fact]
    public void SameSourceDotPath_ValueTypeIntermediate_Throws()
    {
        var entity = new RpEntity { Address = new RpAddress { Zip = 200000 } };

        var ex = Assert.Throws<MapConfigurationException>(() => Mapper.Map<RpEntity, RpDtoValueTypeInterrupt>(entity));
        Assert.Contains("Zip", ex.Message);
    }

    // ==================== A3-3：缓存键 (Type, PropertyInfo) 同名字段互不污染 ====================

    [Fact]
    public void CacheKey_SameFieldName_DifferentDotPath_NoPollution()
    {
        var entity = new RpEntity
        {
            Address = new RpAddress { City = new RpCity { Name = "Shanghai" } },
            Home = new RpHome { City = "Beijing" }
        };

        var dtoA = Mapper.Map<RpEntity, RpDtoA>(entity);
        var dtoB = Mapper.Map<RpEntity, RpDtoB>(entity);

        Assert.Equal("Shanghai", dtoA!.City); // Address.City.Name
        Assert.Equal("Beijing", dtoB!.City);  // Home.City（不被 A 污染）
    }

    // ==================== A3-4：跨表字段点路径 ====================

    [Fact]
    public void CrossTableDotPath_ValueCorrect()
    {
        var order = new RpOrderEntity
        {
            Customer = new RpCustomer { Address = new RpAddress { City = new RpCity { Name = "Guangzhou" } } }
        };

        var dto = Mapper.Map<RpOrderEntity, RpOrderDto>(order);

        Assert.Equal("Guangzhou", dto!.CityName);
    }

    [Fact]
    public void CrossTableDotPath_IntermediateNull_DefaultNoThrow()
    {
        var order = new RpOrderEntity { Customer = new RpCustomer { Address = null } };

        var dto = Mapper.Map<RpOrderEntity, RpOrderDto>(order);

        Assert.Null(dto!.CityName);
    }

    // ==================== A3-5：IsFromJson 优先（C9） ====================

    [Fact]
    public void IsFromJson_PriorityOverDotPath_NoThrow_DefaultValue()
    {
        var entity = new RpEntity
        {
            Address = new RpAddress { City = new RpCity { Name = "Shanghai" } }
        };

        // IsFromJson=true 优先：点路径解析不生效（不抛异常），属性保持默认值 + Trace（可观测）
        var dto = Mapper.Map<RpEntity, RpDtoIsFromJson>(entity);

        Assert.Null(dto!.CityName);
    }

    // ==================== A5-1：MapValidator 验证端同步拆段（H23） ====================

    [Fact]
    public void ValidateMapping_DotPath_NoUnmapped()
    {
        var result = MapValidator.ValidateMapping(typeof(RpEntity), typeof(RpDto));

        Assert.DoesNotContain(result.UnmappedProperties, p => p.Contains("CityName"));
        Assert.Equal(MapValidationLevel.OK, result.Level);
    }

    [Fact]
    public void ValidateMapping_DotPath_FailedSegment_ReportsUnmapped_NoThrow()
    {
        var result = MapValidator.ValidateMapping(typeof(RpEntity), typeof(RpDtoMissing));

        // 拆段失败 → 验证端报未映射（Warn，不抛异常；与运行时抛异常互补，H23）
        Assert.Contains(result.UnmappedProperties, p => p.Contains("City"));
        Assert.Equal(MapValidationLevel.Warn, result.Level);
    }

    // ==================== B-4：跨表字段 [Convert] 运行时消费 ====================

    [MapFrom(typeof(RpOrderEntity))]
    private class RbOrderDto
    {
        [MapProperty(typeof(RpCustomer), "Address.City.Name")]
        [Convert(typeof(RbCityConverter), nameof(RbCityConverter.Convert))]
        public string? CityName { get; set; }
    }

    private static class RbCityConverter
    {
        public static int CallCount { get; set; }

        public static string Convert(string fieldValue)
        {
            CallCount++;
            return "CONV:" + fieldValue;
        }
    }

    [Fact]
    public void CrossTableConvertUsing_Runtime_ValueConverted()
    {
        RbCityConverter.CallCount = 0;
        var order = new RpOrderEntity
        {
            Customer = new RpCustomer { Address = new RpAddress { City = new RpCity { Name = "Shenzhen" } } }
        };

        var dto = Mapper.Map<RpOrderEntity, RbOrderDto>(order);

        // B-4：跨表消费转换器（静态方法形态 T1 单值模板，输入=末段访问 Address.City.Name）
        Assert.Equal("CONV:Shenzhen", dto!.CityName);
        Assert.Equal(1, RbCityConverter.CallCount);
    }

    [Fact]
    public void CrossTableConvertUsing_Runtime_NavigationNull_SkipNoCall()
    {
        RbCityConverter.CallCount = 0;
        var order = new RpOrderEntity { Customer = null };

        var dto = Mapper.Map<RpOrderEntity, RbOrderDto>(order);

        // 导航 null → null 安全链短路：不触发转换器调用、叶不写（导航 null 裁决，与生成端 ?. 链双端一致）
        Assert.Null(dto!.CityName);
        Assert.Equal(0, RbCityConverter.CallCount);
    }
}
