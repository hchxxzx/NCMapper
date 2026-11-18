using NCMapper.SourceGen.Tests.Models;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// [Convert] 无匹配源属性 + 扁平化共存场景的 NRE 回归测试
/// <para>2026-07-18 修复源生成器 4 处 NullReferenceException，本测试固化回归防线：</para>
/// <list type="number">
/// <item>GenerateSinglePropertyAssignment — 正向 Map 路径</item>
/// <item>TryBuildSimpleAssignments — MapList 内联元素路径</item>
/// <item>CollectProjectionBindings — BuildProjection 路径</item>
/// <item>CreateReverseMapping — 反向映射路径</item>
/// </list>
/// <para>触发条件：DTO 同时含 [Convert] 无源属性 + 扁平化属性时，
/// SourceProperty=null! 下游访问 .Type/.SetMethod 抛 NRE，整个源生成器崩溃（CS8785）</para>
/// <para>本测试直接调用 GeneratedMapper 静态方法 — 若源生成器崩溃则方法不存在，编译失败</para>
/// </summary>
public class ConvertUsingNoSourceNreRegressionTests
{
    // ================================================================
    // NRE 1: GenerateSinglePropertyAssignment — 正向 Map 路径
    // ================================================================

    /// <summary>
    /// 正向 Map — 扁平化属性 + ConvertUsing 无源属性 共存于同一 DTO
    /// <para>复现条件：DTO 含 AddressCity（扁平化）+ ComputedField（无源属性 ConvertUsing）</para>
    /// <para>修复前：GenerateSinglePropertyAssignment 访问 pm.SourceProperty.Type 抛 NRE，
    /// 整个源生成器崩溃，GeneratedMapper 类不会被生成，本测试编译失败</para>
    /// <para>修复后：ConvertUsing 分支前置，扁平化属性正确生成 src.Address?.City</para>
    /// </summary>
    [Fact]
    public void Flatten_And_ConvertUsing_NoSource_Coexist_Map()
    {
        var entity = new SgNreUserEntity
        {
            Id = 100,
            Name = "LiSi",
            Address = new SgNreAddress { City = "Beijing", Street = "Wangfu Street" }
        };

        // 直接调用源生成器生成的方法 — 若 NRE 崩溃则此方法不存在
        var dto = GeneratedMapper.Map_SgNreUserEntity_To_SgNreFlattenAndConverterDto(entity)!;

        Assert.Equal(100, dto.Id);
        Assert.Equal("LiSi", dto.Name);
        // 扁平化属性 — 源生成器生成 src.Address?.City
        Assert.Equal("Beijing", dto.AddressCity);
        Assert.Equal("Wangfu Street", dto.AddressStreet);
        // ConvertUsing 无源属性 — 源生成器生成 __conv_ComputedField.Convert(default(string), src)
        Assert.Equal("User-LiSi", dto.ComputedField);
    }

    /// <summary>
    /// 嵌套对象为 null 时扁平化字段保持默认值（null 安全验证）
    /// <para>源生成器生成 src.Address?.City，Address=null 时返回 null</para>
    /// <para>注意：源生成器直接赋值 dst.AddressCity = src.Address?.City，
    /// 会覆盖 DTO 初始化器的 "" 默认值。这是预期行为 — null 合并需调用方显式处理</para>
    /// </summary>
    [Fact]
    public void Flatten_And_ConvertUsing_NoSource_Address_Null()
    {
        var entity = new SgNreUserEntity
        {
            Id = 200,
            Name = "NullAddr",
            Address = null
        };

        var dto = GeneratedMapper.Map_SgNreUserEntity_To_SgNreFlattenAndConverterDto(entity)!;

        Assert.Equal(200, dto.Id);
        Assert.Equal("NullAddr", dto.Name);
        // Address=null 时 src.Address?.City 返回 null，覆盖 DTO 初始化器的 ""
        Assert.Null(dto.AddressCity);
        Assert.Null(dto.AddressStreet);
        // ConvertUsing 无源属性路径不依赖 Address，仍正常工作
        Assert.Equal("User-NullAddr", dto.ComputedField);
    }

    // ================================================================
    // NRE 2: TryBuildSimpleAssignments — MapList 内联元素路径
    // ================================================================

    /// <summary>
    /// MapList — 列表映射路径不崩溃
    /// <para>复现条件：MapList 内联元素路径调用 TryBuildSimpleAssignments + GenerateInlineElement</para>
    /// <para>修复前：TryBuildSimpleAssignments 遍历到无源属性 PM 时访问 .Type 抛 NRE</para>
    /// <para>修复后：foreach 开头 if (pm.ConverterDeclaringType != null) return false; 提前退出简单赋值路径（静态方法形态三字段，历史 NRE 修复时为 ConverterType）</para>
    /// </summary>
    [Fact]
    public void Flatten_And_ConvertUsing_NoSource_Coexist_MapList()
    {
        var entities = new[]
        {
            new SgNreUserEntity
            {
                Id = 1,
                Name = "Alice",
                Address = new SgNreAddress { City = "CityA", Street = "StreetA" }
            },
            new SgNreUserEntity
            {
                Id = 2,
                Name = "Bob",
                Address = new SgNreAddress { City = "CityB", Street = "StreetB" }
            }
        };

        var dtos = GeneratedMapper.MapList_SgNreUserEntity_To_SgNreFlattenAndConverterDto(entities)!;

        Assert.Equal(2, dtos.Count);
        Assert.Equal("Alice", dtos[0].Name);
        Assert.Equal("CityA", dtos[0].AddressCity);
        Assert.Equal("StreetA", dtos[0].AddressStreet);
        Assert.Equal("User-Alice", dtos[0].ComputedField);
        Assert.Equal("Bob", dtos[1].Name);
        Assert.Equal("CityB", dtos[1].AddressCity);
        Assert.Equal("StreetB", dtos[1].AddressStreet);
        Assert.Equal("User-Bob", dtos[1].ComputedField);
    }

    // ================================================================
    // NRE 3: CollectProjectionBindings — BuildProjection 路径
    // 修正注：BuildProjection 路径需 EFCore/FreeSql 扩展
    // 组件，本项目不引用，由 NCMapper.IntegrationTests.FreeSqlProjectionContractTests
    // Nre3_ProjectionBuilder_ConvertUsingNoSource_DoesNotThrowNre 承接（不抛 NRE 防回归）。


    // ================================================================
    // NRE 4: CreateReverseMapping — 反向映射路径
    // ================================================================

    /// <summary>
    /// 反向映射 — DTO → Entity 不崩溃
    /// <para>复现条件：CreateReverseMapping 遍历 forward.PropertyMappings，
    /// pm.SourceProperty=null! 时访问 revTarget.SetMethod 抛 NRE</para>
    /// <para>修复后：foreach 开头 if (pm.SourceProperty == null) continue; 跳过无源属性 PM</para>
    /// <para>反向映射语义：ConvertUsing 无源属性场景无源属性可反向写入，跳过合理</para>
    /// </summary>
    [Fact]
    public void ConvertUsing_NoSource_ReverseMap_DoesNotCrash()
    {
        var dto = new SgNreFlattenAndConverterDto
        {
            Id = 400,
            Name = "Reverse",
            // 反向映射时这些字段无对应源属性可写入 Entity，跳过
            AddressCity = "Ignored",
            AddressStreet = "Ignored",
            ComputedField = "Ignored"
        };

        // 反向映射不应抛 NRE — 源生成器生成 Map_SgNreFlattenAndConverterDto_To_SgNreUserEntity
        var entity = GeneratedMapper.Map_SgNreFlattenAndConverterDto_To_SgNreUserEntity(dto)!;

        // 同名属性正常反向映射
        Assert.Equal(400, entity.Id);
        Assert.Equal("Reverse", entity.Name);
        // 扁平化属性在反向映射中被跳过（MappingExtractor.CreateReverseMapping line 332）
        // ConvertUsing 无源属性在反向映射中被跳过（line 333 新增 null 检查）
        // Address 保持默认值（new SgNreUserEntity 的初始化器 new()）
        Assert.NotNull(entity.Address);
    }

    // ================================================================
    // 综合：正向 + 列表 + 反向 + Projection + Action 一致性
    // ================================================================

    /// <summary>
    /// 综合一致性 — 正向 Map + MapList + 反向 Map + Projection + Action 全部不崩溃且结果正确
    /// <para>这是 2026-07-18 修复的端到端验证：源生成器不再因 NRE 崩溃，
    /// 所有路径（Core/Action/Update/MapList/Projection/Reverse）都能正常生成代码</para>
    /// </summary>
    [Fact]
    public void Flatten_And_ConvertUsing_NoSource_All_Paths_Consistent()
    {
        var entity = new SgNreUserEntity
        {
            Id = 999,
            Name = "AllPaths",
            Address = new SgNreAddress { City = "FinalCity", Street = "FinalStreet" }
        };

        // 1. 正向 Map（Core 路径）
        var dto1 = GeneratedMapper.Map_SgNreUserEntity_To_SgNreFlattenAndConverterDto(entity)!;
        Assert.Equal("FinalCity", dto1.AddressCity);
        Assert.Equal("User-AllPaths", dto1.ComputedField);

        // 2. MapList
        var dto2 = GeneratedMapper.MapList_SgNreUserEntity_To_SgNreFlattenAndConverterDto(new[] { entity })![0];
        Assert.Equal("FinalCity", dto2.AddressCity);
        Assert.Equal("User-AllPaths", dto2.ComputedField);

        // 3. 反向 Map（不崩溃即可）
        var roundTrip = GeneratedMapper.Map_SgNreFlattenAndConverterDto_To_SgNreUserEntity(dto1)!;
        Assert.Equal(999, roundTrip.Id);
        Assert.Equal("AllPaths", roundTrip.Name);

        // 5. 带 Action 回调的 Map
        var dto5 = GeneratedMapper.Map_SgNreUserEntity_To_SgNreFlattenAndConverterDto_WithAction(entity, (src, dst) =>
        {
            dst.Name = $"{src.Name}-Modified";
        })!;
        Assert.Equal("AllPaths-Modified", dto5.Name);
        // Action 修改不影响其他字段
        Assert.Equal("FinalCity", dto5.AddressCity);
        Assert.Equal("User-AllPaths", dto5.ComputedField);
    }
}
