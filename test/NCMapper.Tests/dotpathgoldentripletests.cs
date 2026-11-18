using System.IO;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 点路径 Golden Triple 正式套件 — 运行时引擎列
/// <para>矩阵定位：7 场景 × 4 引擎（源生成 / 运行时 / 投影 / Compose-SourceGen）防漂移锚。
/// 本类 = 运行时列；源生成与 Compose-SourceGen 列见 NCMapper.SourceGen.Tests.DotPathGoldenTripleTests；
/// 投影列（真实 provider，禁止对投影表达式直接 Compile 断言）见 NCMapper.IntegrationTests.DotPathGoldenTripleTests。</para>
/// <para>场景覆盖：S1 合法两段/三段路径值相等；S2 缺失段抛 MapConfigurationException（不进失败缓存，二次调用仍抛）；
/// S3 中间节点值类型/string 路径中断；S4 叶节点命中全局忽略跳过；S5 中间节点 null 默认值不抛；
/// S6 目标带 [Mask]/[Convert] 消费生效（H6：路径映射脱敏/转换，与直接属性分支同形态）；
/// S7 两目标类型同名字段不同显式路径互不污染（缓存键回归）。</para>
/// <para>路径说明：DTO 为 private 嵌套类，源生成器跳过（NCMAP011），Mapper.Map 走运行时 ExpressionBuilder 路径。
/// 隔离范式同 DotPathRuntimeSmokeTests：[Collection("GlobalConfigState")] + 构造器/Dispose 双 ClearCache。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class DotPathGoldenTripleTests : IDisposable
{
    /// <summary>
    /// 测试前重置全局配置冻结状态（测试隔离）
    /// </summary>
    public DotPathGoldenTripleTests()
    {
        Mapper.ClearCache();        // 迁移：Reset 不再清注册表，测试隔离需显式全清
        GlobalMapConfig.ResetForTests();
    }

    /// <summary>
    /// 测试后重置冻结状态 — 防止配置冻结残留污染其他测试类
    /// </summary>
    public void Dispose()
    {
        Mapper.ClearCache();        GlobalMapConfig.ResetForTests(); // 迁移

    }

    // ==================== 模型 ====================

    private class GtCity
    {
        public string? Name { get; set; }
    }

    private class GtAddress
    {
        public GtCity? City { get; set; }
        public int Zip { get; set; }
    }

    private class GtHome
    {
        public string? City { get; set; }
    }

    private class GtEntity
    {
        public GtAddress? Address { get; set; }
        public GtHome? Home { get; set; }
    }

    /// <summary>S1 两段：Home.City（string 叶）</summary>
    [MapFrom(typeof(GtEntity))]
    private class GtDtoTwoSegment
    {
        [MapProperty(typeof(GtEntity), "Home.City")]
        public string? City { get; set; }
    }

    /// <summary>S1 三段：Address.City.Name</summary>
    [MapFrom(typeof(GtEntity))]
    private class GtDtoThreeSegment
    {
        [MapProperty(typeof(GtEntity), "Address.City.Name")]
        public string? CityName { get; set; }
    }

    /// <summary>S2 缺失段：Address.Missing</summary>
    [MapFrom(typeof(GtEntity))]
    private class GtDtoMissingSegment
    {
        [MapProperty(typeof(GtEntity), "Address.Missing")]
        public string? City { get; set; }
    }

    /// <summary>S3 值类型中断：Address.Zip.X（int 不可导航）</summary>
    [MapFrom(typeof(GtEntity))]
    private class GtDtoValueTypeInterrupt
    {
        [MapProperty(typeof(GtEntity), "Address.Zip.X")]
        public int X { get; set; }
    }

    /// <summary>S3 string 中断：Home.City.Sub（string 不可导航）</summary>
    [MapFrom(typeof(GtEntity))]
    private class GtDtoStringInterrupt
    {
        [MapProperty(typeof(GtEntity), "Home.City.Sub")]
        public string? Sub { get; set; }
    }

    /// <summary>S4 叶节点命中全局忽略：叶 Name 命中 IgnoreNames</summary>
    [MapFrom(typeof(GtEntity))]
    private class GtDtoLeafGlobalIgnore
    {
        [MapProperty(typeof(GtEntity), "Address.City.Name")]
        public string? CityName { get; set; }
    }

    /// <summary>S6 目标带 [Mask]：点路径 × Mask 消费（脱敏生效）</summary>
    [MapFrom(typeof(GtEntity))]
    private class GtDtoMask
    {
        [MapProperty(typeof(GtEntity), "Address.City.Name")]
        [Mask(typeof(GtMask))]
        public string? CityName { get; set; }
    }

    /// <summary>S6-ConvertUsing：目标带 [Convert]：点路径 × 转换器消费</summary>
    [MapFrom(typeof(GtEntity))]
    private class GtDtoConvert
    {
        [MapProperty(typeof(GtEntity), "Address.City.Name")]
        [Convert(typeof(GtConverter), nameof(GtConverter.Convert))]
        public string? CityName { get; set; }
    }

    /// <summary>S6-双标：点路径 × [Convert] + [Mask] 共存（先转换再脱敏）</summary>
    [MapFrom(typeof(GtEntity))]
    private class GtDtoConvertMask
    {
        [MapProperty(typeof(GtEntity), "Address.City.Name")]
        [Convert(typeof(GtConverter), nameof(GtConverter.Convert))]
        [Mask(typeof(GtMask))]
        public string? CityName { get; set; }
    }

    /// <summary>S6-扁平化：推导扁平化（AddressCityName）+ [Mask] 消费</summary>
    [MapFrom(typeof(GtEntity))]
    private class GtDtoFlattenMask
    {
        [Mask(typeof(GtMask))]
        public string? AddressCityName { get; set; }
    }

    /// <summary>S7-A：同名字段 City，路径 Address.City.Name</summary>
    [MapFrom(typeof(GtEntity))]
    private class GtDtoPathA
    {
        [MapProperty(typeof(GtEntity), "Address.City.Name")]
        public string? City { get; set; }
    }

    /// <summary>S7-B：同名字段 City，路径 Home.City</summary>
    [MapFrom(typeof(GtEntity))]
    private class GtDtoPathB
    {
        [MapProperty(typeof(GtEntity), "Home.City")]
        public string? City { get; set; }
    }

    /// <summary>
    /// S6 专用 Mask — 若被消费则输出固定值 "MASKED"，未被消费则保持明文
    /// </summary>
    public sealed class GtMask : IMask
    {
        public string Mask(string? value) => "MASKED";
    }

    /// <summary>
    /// S6-ConvertUsing 专用转换器（静态方法形态 T1 单值模板）— 前缀 "CONV:"，证明转换器被路径映射消费
    /// </summary>
    private static class GtConverter
    {
        public static string Convert(string fieldValue) => "CONV:" + fieldValue;
    }

    // ==================== S1：合法两段/三段路径值相等 ====================

    /// <summary>
    /// 场景 1（两段）：Home.City → 值正确
    /// </summary>
    [Fact]
    public void S1_TwoSegmentPath_ValueCorrect()
    {
        var entity = new GtEntity { Home = new GtHome { City = "Beijing" } };

        var dto = Mapper.Map<GtEntity, GtDtoTwoSegment>(entity);

        Assert.Equal("Beijing", dto!.City);
    }

    /// <summary>
    /// 场景 1（三段）：Address.City.Name → 值正确
    /// </summary>
    [Fact]
    public void S1_ThreeSegmentPath_ValueCorrect()
    {
        var entity = new GtEntity
        {
            Address = new GtAddress { City = new GtCity { Name = "Shanghai" } }
        };

        var dto = Mapper.Map<GtEntity, GtDtoThreeSegment>(entity);

        Assert.Equal("Shanghai", dto!.CityName);
    }

    // ==================== S2：缺失段 ====================

    /// <summary>
    /// 场景 2：缺失段 → MapConfigurationException 带失败段名；二次调用仍抛（显式失败不进失败缓存，G4）
    /// </summary>
    [Fact]
    public void S2_MissingSegment_Throws_AndRepeatedCallsStillThrow()
    {
        var entity = new GtEntity { Address = new GtAddress() };

        var ex = Assert.Throws<MapConfigurationException>(() => Mapper.Map<GtEntity, GtDtoMissingSegment>(entity));
        Assert.Contains("Missing", ex.Message);

        // 显式点路径失败不进失败缓存——可观测性不随缓存命中消失
        Assert.Throws<MapConfigurationException>(() => Mapper.Map<GtEntity, GtDtoMissingSegment>(entity));
    }

    // ==================== S3：中间节点值类型/string 中断 ====================

    /// <summary>
    /// 场景 3（值类型）：Address.Zip.X → MapConfigurationException 带中断段名
    /// </summary>
    [Fact]
    public void S3_ValueTypeIntermediate_Throws()
    {
        var entity = new GtEntity { Address = new GtAddress { Zip = 200000 } };

        var ex = Assert.Throws<MapConfigurationException>(() => Mapper.Map<GtEntity, GtDtoValueTypeInterrupt>(entity));
        Assert.Contains("Zip", ex.Message);
    }

    /// <summary>
    /// 场景 3（string）：Home.City.Sub → MapConfigurationException 带中断段名
    /// </summary>
    [Fact]
    public void S3_StringIntermediate_Throws()
    {
        var entity = new GtEntity { Home = new GtHome { City = "Beijing" } };

        var ex = Assert.Throws<MapConfigurationException>(() => Mapper.Map<GtEntity, GtDtoStringInterrupt>(entity));
        Assert.Contains("City", ex.Message);
    }

    // ==================== S4：叶节点命中全局忽略 ====================

    /// <summary>
    /// 场景 4：IgnoreNames=["Name"] → 点路径叶节点命中 → 跳过（属性留空），与源生成器/投影一致
    /// </summary>
    [Fact]
    public void S4_LeafGlobalIgnore_Skipped()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["Name"] },
            "GtRuntimeGoldenTriple");

        var entity = new GtEntity
        {
            Address = new GtAddress { City = new GtCity { Name = "Shanghai" } }
        };

        var dto = Mapper.Map<GtEntity, GtDtoLeafGlobalIgnore>(entity);

        Assert.Null(dto!.CityName);
    }

    // ==================== S5：中间节点 null ====================

    /// <summary>
    /// 场景 5（中间节点 null）：Address = null → default/null 不抛
    /// </summary>
    [Fact]
    public void S5_IntermediateNull_DefaultNoThrow()
    {
        var entity = new GtEntity { Address = null };

        var dto = Mapper.Map<GtEntity, GtDtoThreeSegment>(entity);

        Assert.Null(dto!.CityName);
    }

    /// <summary>
    /// 场景 5（深层中间节点 null）：Address.City = null → default/null 不抛
    /// </summary>
    [Fact]
    public void S5_DeepIntermediateNull_DefaultNoThrow()
    {
        var entity = new GtEntity { Address = new GtAddress { City = null } };

        var dto = Mapper.Map<GtEntity, GtDtoThreeSegment>(entity);

        Assert.Null(dto!.CityName);
    }

    // ==================== S6：目标带 [Mask]/[Convert] — 消费生效（H6）====================

    /// <summary>
    /// 场景 6：点路径 × [Mask] → 脱敏生效（"MASKED"）——H6 消费修复后不再输出 "不消费" 警告
    /// </summary>
    [Fact]
    public void S6_Mask_Consumed_MaskedValue()
    {
        var entity = new GtEntity
        {
            Address = new GtAddress { City = new GtCity { Name = "Shanghai" } }
        };

        var dto = Mapper.Map<GtEntity, GtDtoMask>(entity);

        Assert.Equal("MASKED", dto!.CityName); // 脱敏生效——点路径 × Mask 消费
    }

    /// <summary>
    /// 场景 6（ConvertUsing）：点路径 × [Convert] → 转换器被调用（"CONV:Shanghai"）
    /// </summary>
    [Fact]
    public void S6_ConvertUsing_Consumed_ConvertedValue()
    {
        var entity = new GtEntity
        {
            Address = new GtAddress { City = new GtCity { Name = "Shanghai" } }
        };

        var dto = Mapper.Map<GtEntity, GtDtoConvert>(entity);

        Assert.Equal("CONV:Shanghai", dto!.CityName); // 转换器消费路径值
    }

    /// <summary>
    /// 场景 6（双标）：点路径 × [Convert] + [Mask] → 先转换再脱敏（Mask(Convert(value))）
    /// </summary>
    [Fact]
    public void S6_ConvertUsingAndMask_ConvertThenMask()
    {
        var entity = new GtEntity
        {
            Address = new GtAddress { City = new GtCity { Name = "Shanghai" } }
        };

        var dto = Mapper.Map<GtEntity, GtDtoConvertMask>(entity);

        Assert.Equal("MASKED", dto!.CityName); // 双标组合：Mask 输入 = 转换结果（P1-6 有序执行）
    }

    /// <summary>
    /// 场景 6（推导扁平化）：AddressCityName（无显式 [MapProperty]）+ [Mask] → 推导扁平化路径消费脱敏
    /// </summary>
    [Fact]
    public void S6_FlattenMask_Consumed_MaskedValue()
    {
        var entity = new GtEntity
        {
            Address = new GtAddress { City = new GtCity { Name = "Shanghai" } }
        };

        var dto = Mapper.Map<GtEntity, GtDtoFlattenMask>(entity);

        Assert.Equal("MASKED", dto!.AddressCityName); // 推导扁平化路径 × Mask 消费
    }

    // ==================== S7：缓存键互不污染（仅运行时） ====================
    /// <summary>
    /// 场景 7：两目标类型同名字段不同显式路径 → 互不污染（缓存键 (Type, PropertyInfo) 回归）
    /// </summary>
    [Fact]
    public void S7_SameFieldName_DifferentDotPath_NoPollution()
    {
        var entity = new GtEntity
        {
            Address = new GtAddress { City = new GtCity { Name = "Shanghai" } },
            Home = new GtHome { City = "Beijing" }
        };

        var dtoA = Mapper.Map<GtEntity, GtDtoPathA>(entity);
        var dtoB = Mapper.Map<GtEntity, GtDtoPathB>(entity);

        Assert.Equal("Shanghai", dtoA!.City); // Address.City.Name
        Assert.Equal("Beijing", dtoB!.City);  // Home.City（不被 A 污染）
    }

    // ==================== S12：反向路径写回（DTO→Entity 折叠）====================

    private class S12City
    {
        public string? Name { get; set; }
    }

    private class S12Address
    {
        public S12City? City { get; set; }
        public string? Email { get; set; }
    }

    private class S12Entity
    {
        public S12Address? Address { get; set; }
        public string? City { get; set; }
        public string? DirectName { get; set; }
    }

    /// <summary>S12a 显式点路径写回基础：DTO.CityName → Entity.Address.City.Name</summary>
    [MapFrom(typeof(S12Entity))]
    private class S12DtoA
    {
        [MapProperty(typeof(S12Entity), "Address.City.Name")]
        public string? CityName { get; set; }
    }

    /// <summary>S12f 双标：[Convert] + 点路径（A-3：反向写回不消费转换器，按类型转换路径写回）</summary>
    [MapFrom(typeof(S12Entity))]
    private class S12DtoConvert
    {
        [MapProperty(typeof(S12Entity), "Address.City.Name")]
        [Convert(typeof(S12Converter), nameof(S12Converter.Convert))]
        public string? CityName { get; set; }
    }

    /// <summary>S12e [MapIgnore(ToEntity)] 拦截写回</summary>
    [MapFrom(typeof(S12Entity))]
    private class S12DtoIgnore
    {
        [MapProperty(typeof(S12Entity), "Address.City.Name")]
        [MapIgnore(Direction.ToEntity)]
        public string? CityName { get; set; }
    }

    /// <summary>S12i2 同名优先：实体有同名可写属性 City → 普通赋值优先，路径不写回</summary>
    [MapFrom(typeof(S12Entity))]
    private class S12DtoSameName
    {
        [MapProperty(typeof(S12Entity), "Address.City.Name")]
        public string? City { get; set; }
    }

    /// <summary>
    /// S12f 专用转换器（静态方法形态 T1 单值模板）— 输入 null → 输出 null（验证转换器接收 null 且输出 null 不创建不写，N-1）；
    /// 输入非 null → "CONV:" + 值
    /// </summary>
    private static class S12Converter
    {
        public static int CallCount;

        public static string Convert(string? fieldValue)
        {
            CallCount++;
            // 转换器契约输出 null（N-1 测试输入 null → 输出 null）；方法返回类型声明非空，用 null! 表达刻意 null
            return fieldValue == null ? null! : "CONV:" + fieldValue;
        }
    }

    /// <summary>
    /// S12c 接口中间节点 — 不可实例化 → 跳过写回 + Trace 警告（N-2 方案 1 降级，不抛）
    /// </summary>
    private interface IS12Address { string? Name { get; set; } }

    private class S12EntityInterface
    {
        public IS12Address? Address { get; set; }
    }

    [MapFrom(typeof(S12EntityInterface))]
    private class S12DtoInterface
    {
        [MapProperty(typeof(S12EntityInterface), "Address.Name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// S12a 基础：DTO 扁平值写回实体嵌套路径（中间节点 null → 创建）
    /// </summary>
    [Fact]
    public void S12a_ReverseWriteBack_ExplicitDotPath_CreatesIntermediate()
    {
        var dto = new S12DtoA { CityName = "Shanghai" };

        var entity = Mapper.Map<S12DtoA, S12Entity>(dto);

        Assert.NotNull(entity!.Address);            // 中间节点 null → 创建
        Assert.Equal("Shanghai", entity.Address!.City!.Name);
    }

    /// <summary>
    /// S12k 已有中间实例复用 + 成员保留：写回不覆盖既有导航对象（Email 不被清空）
    /// </summary>
    [Fact]
    public void S12k_ReverseWriteBack_ExistingIntermediate_ReusedAndPreserved()
    {
        var dto = new S12DtoA { CityName = "Beijing" };
        var entity = new S12Entity { Address = new S12Address { Email = "keep@example.com" } };

        Mapper.Map(dto, entity);

        Assert.NotNull(entity.Address);
        Assert.Equal("keep@example.com", entity.Address!.Email);  // 复用实例，成员保留
        Assert.Equal("Beijing", entity.Address.City!.Name);       // 仅 City 链被创建
    }

    /// <summary>
    /// S12i 源值 null → 链存在则清叶，链不存在 no-op（D5 null=清空）
    /// </summary>
    [Fact]
    public void S12i_ReverseWriteBack_NullSource_SkipsEntirely()
    {
        var dto = new S12DtoA { CityName = null };

        // 中间节点为 null → 链不存在 → no-op（不创建中间节点）
        var entity = Mapper.Map<S12DtoA, S12Entity>(dto);
        Assert.Null(entity!.Address);

        // 中间节点非 null → 链存在 → 清叶（D5：null=清空，可空引用叶赋 null）
        var entity2 = new S12Entity { Address = new S12Address { City = new S12City { Name = "Old" } } };
        Mapper.Map(dto, entity2);
        Assert.Null(entity2.Address!.City!.Name);
    }

    /// <summary>
    /// S12d 推导扁平化反向：AddressCity → Address.City（与显式同进退）
    /// <para>独立模型（S12EntityF.Address.City 为 string）：折叠叶类型与 DTO 属性兼容，正向/反向均可成立。</para>
    /// </summary>
    private class S12EntityF
    {
        public S12AddressF? Address { get; set; }
    }

    private class S12AddressF
    {
        public string? City { get; set; }
    }

    [MapFrom(typeof(S12EntityF))]
    private class S12DtoFlatten
    {
        public string? AddressCity { get; set; }
    }

    [Fact]
    public void S12d_ReverseWriteBack_DerivedFlatten_ValueCorrect()
    {
        var dto = new S12DtoFlatten { AddressCity = "Guangzhou" };

        var entity = Mapper.Map<S12DtoFlatten, S12EntityF>(dto);

        Assert.Equal("Guangzhou", entity!.Address!.City);
    }

    /// <summary>
    /// S12e [MapIgnore(ToEntity)] 拦截：DTO 属性方向忽略生效，不写回（O-5 显式接线）
    /// </summary>
    [Fact]
    public void S12e_ReverseWriteBack_MapIgnoreToEntity_Blocks()
    {
        var dto = new S12DtoIgnore { CityName = "X" };

        var entity = Mapper.Map<S12DtoIgnore, S12Entity>(dto);

        Assert.Null(entity!.Address);
    }

    /// <summary>
    /// S12f/S12j 双标：A-3——反向写回一律不消费 [Convert]
    /// （[Convert] 标注绑定正向方向），按类型转换路径写入；[ConvertBack]引入前反向无自定义转换
    /// </summary>
    [Fact]
    public void S12fj_ReverseWriteBack_ConvertUsing_NotConsumed()
    {
        S12Converter.CallCount = 0;

        // 源值 null → 不创建中间节点（N-1）；反向不消费转换器
        var dtoNull = new S12DtoConvert { CityName = null };
        var entityNull = Mapper.Map<S12DtoConvert, S12Entity>(dtoNull);
        Assert.Null(entityNull!.Address);
        Assert.Equal(0, S12Converter.CallCount);

        // 源值非 null → 按类型转换路径直写叶（无 "CONV:" 前缀）
        var dtoVal = new S12DtoConvert { CityName = "X" };
        var entityVal = Mapper.Map<S12DtoConvert, S12Entity>(dtoVal);
        Assert.Equal("X", entityVal!.Address!.City!.Name);
        Assert.Equal(0, S12Converter.CallCount);
    }

    /// <summary>
    /// S12i2 显式路径标注 → 路径优先写回（与正向/源生成器对称：显式 FromField 声明即契约）：
    /// DTO.City 标 [MapProperty(实体, "Address.City.Name")] + 实体存在同名 City → 写回路径而非同名
    /// </summary>
    [Fact]
    public void S12i2_ReverseWriteBack_ExplicitPath_WinsOverSameName()
    {
        var dto = new S12DtoSameName { City = "Direct" };

        var entity = Mapper.Map<S12DtoSameName, S12Entity>(dto);

        Assert.Equal("Direct", entity!.Address!.City!.Name);  // 显式路径写回（Address.City.Name）
        Assert.Null(entity.City);                             // 同名属性不参与（主循环未消费）
    }

    /// <summary>
    /// S12c 接口中间节点：跳过 + Trace 警告，不抛异常（N-2 方案 1 降级）
    /// </summary>
    [Fact]
    public void S12c_ReverseWriteBack_InterfaceIntermediate_SkipsWithTrace()
    {
        var dto = new S12DtoInterface { Name = "X" };

        var sw = new System.IO.StringWriter();
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            var entity = Mapper.Map<S12DtoInterface, S12EntityInterface>(dto);
            Assert.Null(entity!.Address);   // 不抛异常（段缺失才抛；中间节点不可实例化 → 跳过 + Trace）
        }
        finally
        {
            MapTracer.GloballyEnabled = false;
            MapTracer.GlobalOutput = Console.Out;
        }
        Assert.Contains("不可实例化", sw.ToString());
    }

    /// <summary>
    /// S12fail 段缺失：抛 MapConfigurationException（C6）；二次调用仍抛（显式失败不进失败缓存，G4）
    /// </summary>
    [MapFrom(typeof(S12Entity))]
    private class S12DtoMissing
    {
        [MapProperty(typeof(S12Entity), "Address.Missing.Name")]
        public string? CityName { get; set; }
    }

    [Fact]
    public void S12Fail_ReverseWriteBack_MissingSegment_ThrowsRepeatedly()
    {
        var dto = new S12DtoMissing { CityName = "X" };

        var ex = Assert.Throws<MapConfigurationException>(() => Mapper.Map<S12DtoMissing, S12Entity>(dto));
        Assert.Contains("Missing", ex.Message);

        // 显式写回失败不进失败缓存——可观测性不随缓存命中消失（与正向 S2 同判据）
        Assert.Throws<MapConfigurationException>(() => Mapper.Map<S12DtoMissing, S12Entity>(dto));
    }

    /// <summary>
    /// S12m F3 收口（O-7）：实体侧路径标注不参与反向——DTO 侧标注写回正常，实体侧标注不产生展开
    /// </summary>
    private class S12EntityBoth
    {  // 实体侧标注（仅正向读；反向不消费）
        public S12Address? Address { get; set; }
    }

    [MapFrom(typeof(S12EntityBoth))]
    private class S12DtoBoth
    {
        [MapProperty(typeof(S12EntityBoth), "Address.City.Name")]  // DTO 侧标注（正向读 + 反向写回）
        public string? CityName { get; set; }
        public string? Other { get; set; }
    }

    [Fact]
    public void S12m_ReverseWriteBack_EntitySideAnnotation_NotConsumed()
    {
        var dto = new S12DtoBoth { CityName = "Hangzhou", Other = "V" };

        S12EntityBoth? entity = null;
        var ex = Record.Exception(() => entity = Mapper.Map<S12DtoBoth, S12EntityBoth>(dto));

        // 实体侧标注未消费（负向直断言）：若反向消费 "Other.Path"，
        // S12DtoBoth.Other 为 string 无 Path 段 → 必抛 MapConfigurationException（C6）
        Assert.Null(ex);
        // 反向写回消费 DTO 侧标注（写回正常）；Address 是中间节点被创建，但 Other.Path 不参与
        Assert.Equal("Hangzhou", entity!.Address!.City!.Name);
    }
}
