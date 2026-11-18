// 投影契约测试 — 验证 EFCore 和 FreeSql 扩展对相同输入产生等价 DTO 输出
// 六组契约用例：平坦映射 / null 导航 / 空集合 / 深层嵌套 / 降级 / Compose
// 不共享实现，共享验证标准

using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests;

#region 契约测试用实体/DTO 定义

/// <summary>契约测试源实体</summary>
public class ContractEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public ContractChild? Child { get; set; }
    public List<ContractItem> Items { get; set; } = new();
}

/// <summary>契约测试子实体</summary>
public class ContractChild
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public ContractGrandChild? GrandChild { get; set; }
}

/// <summary>契约测试孙实体（深层嵌套）</summary>
public class ContractGrandChild
{
    public int Id { get; set; }
    public string DeepValue { get; set; } = "";
}

/// <summary>契约测试集合元素实体</summary>
public class ContractItem
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
}

/// <summary>契约测试平坦 DTO</summary>
[MapFrom(typeof(ContractEntity))]
public class ContractFlatDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

/// <summary>契约测试嵌套 DTO</summary>
[MapFrom(typeof(ContractEntity))]
public class ContractNestedDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ContractChildDto? Child { get; set; }
}

/// <summary>契约测试子 DTO</summary>
[MapFrom(typeof(ContractChild))]
public class ContractChildDto
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public ContractGrandChildDto? GrandChild { get; set; }
}

/// <summary>契约测试孙 DTO</summary>
[MapFrom(typeof(ContractGrandChild))]
public class ContractGrandChildDto
{
    public int Id { get; set; }
    public string DeepValue { get; set; } = "";
}

/// <summary>契约测试集合 DTO（EFCore 投影 / FreeSql 降级）</summary>
[MapFrom(typeof(ContractEntity))]
public class ContractCollectionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<ContractItemDto> Items { get; set; } = new();
}

/// <summary>契约测试集合元素 DTO</summary>
[MapFrom(typeof(ContractItem))]
public class ContractItemDto
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
}

/// <summary>契约测试降级 DTO（ConvertUsing 触发降级）</summary>
[MapFrom(typeof(ContractEntity))]
public class ContractDegradedDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    [MapProperty(typeof(ContractEntity), "Price")]
    [Convert(typeof(ContractPriceConverter), nameof(ContractPriceConverter.Convert))]
    public string PriceText { get; set; } = "";
}

/// <summary>契约测试用转换器（静态方法引用形态，T1 模板 M(TField)）</summary>
public static class ContractPriceConverter
{
    /// <summary>金额格式化为 "$x.xx"</summary>
    public static string Convert(decimal fieldValue) => $"${fieldValue:F2}";
}

/// <summary>Compose 源实体 A</summary>
public class ContractComposeA
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

/// <summary>Compose 源实体 B</summary>
public class ContractComposeB
{
    public int Id { get; set; }
    public string Category { get; set; } = "";
}

/// <summary>Compose 目标 DTO</summary>
[MapFrom(typeof(ContractComposeA))]
public class ContractComposeDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    [MapProperty(typeof(ContractComposeB), nameof(ContractComposeB.Category))]
    public string CategoryName { get; set; } = "";
}

/// <summary>契约测试 Mask 降级 DTO（[Mask] 触发降级）</summary>
[MapFrom(typeof(ContractEntity))]
public class ContractMaskDto
{
    public int Id { get; set; }

    [Mask(typeof(MaskPhone))]
    public string Name { get; set; } = "";
}

#endregion

/// <summary>
/// EFCore 投影契约测试 — 验证 EfCoreProjectionBuilder 输出符合契约标准
/// </summary>
public class EfCoreProjectionContractTests
{
    private static ContractEntity CreateTestEntity() => new()
    {
        Id = 1,
        Name = "Test",
        Price = 99.95m,
        Child = new ContractChild
        {
            Id = 10,
            Label = "ChildLabel",
            GrandChild = new ContractGrandChild { Id = 100, DeepValue = "Deep" }
        },
        Items = new List<ContractItem>
        {
            new() { Id = 1000, Sku = "SKU-A" },
            new() { Id = 1001, Sku = "SKU-B" }
        }
    };

    /// <summary>契约1: 平坦映射</summary>
    [Fact]
    public void Contract_FlatMapping()
    {
        var expr = EfCoreProjectionBuilder.Build<ContractEntity, ContractFlatDto>();
        var dto = expr.Compile()(CreateTestEntity());

        Assert.Equal(1, dto.Id);
        Assert.Equal("Test", dto.Name);
        Assert.Equal(99.95m, dto.Price);
    }

    /// <summary>契约2: null 导航属性不抛异常</summary>
    [Fact]
    public void Contract_NullNavigation()
    {
        var entity = CreateTestEntity();
        entity.Child = null;

        var expr = EfCoreProjectionBuilder.Build<ContractEntity, ContractNestedDto>();
        var dto = expr.Compile()(entity);

        Assert.Equal(1, dto.Id);
        Assert.Null(dto.Child);
    }

    /// <summary>契约3: 空集合返回空列表</summary>
    [Fact]
    public void Contract_EmptyCollection()
    {
        var entity = CreateTestEntity();
        entity.Items = new List<ContractItem>();

        var expr = EfCoreProjectionBuilder.Build<ContractEntity, ContractCollectionDto>();
        var dto = expr.Compile()(entity);

        Assert.NotNull(dto.Items);
        Assert.Empty(dto.Items);
    }

    /// <summary>契约4: 深层嵌套（3层）</summary>
    [Fact]
    public void Contract_DeepNesting()
    {
        var expr = EfCoreProjectionBuilder.Build<ContractEntity, ContractNestedDto>();
        var dto = expr.Compile()(CreateTestEntity());

        Assert.NotNull(dto.Child);
        Assert.Equal("ChildLabel", dto.Child!.Label);
        Assert.NotNull(dto.Child.GrandChild);
        Assert.Equal("Deep", dto.Child.GrandChild!.DeepValue);
    }

    /// <summary>契约5: 降级场景（ConvertUsing）— 走 MapList 路径结果正确</summary>
    [Fact]
    public void Contract_Degradation()
    {
        // ConvertUsing 触发降级 → 验证降级检测 + MapList 结果
        Assert.True(EfCoreDegradationDetector.NeedsDegradation(
            typeof(ContractEntity), typeof(ContractDegradedDto), out _));

        var dto = Mapper.Map<ContractEntity, ContractDegradedDto>(CreateTestEntity());
        Assert.NotNull(dto);
        Assert.Equal(1, dto!.Id);
        Assert.Equal("Test", dto.Name);
        Assert.Equal("$99.95", dto.PriceText);
    }

    /// <summary>契约6: 集合投影元素正确</summary>
    [Fact]
    public void Contract_CollectionElements()
    {
        var expr = EfCoreProjectionBuilder.Build<ContractEntity, ContractCollectionDto>();
        var dto = expr.Compile()(CreateTestEntity());

        Assert.Equal(2, dto.Items.Count);
        Assert.Equal("SKU-A", dto.Items[0].Sku);
        Assert.Equal("SKU-B", dto.Items[1].Sku);
    }

    /// <summary>契约7: Mask 触发降级 — 降级检测正确 + MaskProvider 脱敏输出正确</summary>
    [Fact]
    public void Contract_MaskDegradation()
    {
        Assert.True(EfCoreDegradationDetector.NeedsDegradation(
            typeof(ContractEntity), typeof(ContractMaskDto), out var reason));
        Assert.Contains("Mask", reason);

        // Mask 是源生成器专属功能，运行时路径不支持 → 验证 MaskProvider 直接输出
        var mask = new MaskPhone();
        Assert.Equal("138****5678", mask.Mask("13812345678"));
    }
}

/// <summary>
/// FreeSql 投影契约测试 — 验证 FreeSqlProjectionBuilder 输出符合契约标准
/// </summary>
public class FreeSqlProjectionContractTests
{
    private static ContractEntity CreateTestEntity() => new()
    {
        Id = 1,
        Name = "Test",
        Price = 99.95m,
        Child = new ContractChild
        {
            Id = 10,
            Label = "ChildLabel",
            GrandChild = new ContractGrandChild { Id = 100, DeepValue = "Deep" }
        },
        Items = new List<ContractItem>
        {
            new() { Id = 1000, Sku = "SKU-A" },
            new() { Id = 1001, Sku = "SKU-B" }
        }
    };

    /// <summary>契约1: 平坦映射</summary>
    [Fact]
    public void Contract_FlatMapping()
    {
        var expr = FreeSqlProjectionBuilder.Build<ContractEntity, ContractFlatDto>();
        var dto = expr.Compile()(CreateTestEntity());

        Assert.Equal(1, dto.Id);
        Assert.Equal("Test", dto.Name);
        Assert.Equal(99.95m, dto.Price);
    }

    /// <summary>契约2: null 导航属性不抛异常</summary>
    [Fact]
    public void Contract_NullNavigation()
    {
        var entity = CreateTestEntity();
        entity.Child = null;

        var expr = FreeSqlProjectionBuilder.Build<ContractEntity, ContractNestedDto>();
        var dto = expr.Compile()(entity);

        Assert.Equal(1, dto.Id);
        Assert.Null(dto.Child);
    }

    /// <summary>契约3: 集合导航触发降级（FreeSql 不支持集合子查询）</summary>
    [Fact]
    public void Contract_CollectionTriggersDegradation()
    {
        // FreeSql 策略：含集合导航 → 降级
        Assert.True(FreeSqlDegradationDetector.NeedsDegradation(
            typeof(ContractEntity), typeof(ContractCollectionDto), out var reason));
        Assert.Contains("集合导航", reason);

        // 降级路径 → MapList 结果正确
        var dto = Mapper.Map<ContractEntity, ContractCollectionDto>(CreateTestEntity());
        Assert.NotNull(dto);
        Assert.Equal(2, dto!.Items.Count);
        Assert.Equal("SKU-A", dto.Items[0].Sku);
    }

    /// <summary>契约4: 深层嵌套（3层，FreeSql 上限内）</summary>
    [Fact]
    public void Contract_DeepNesting()
    {
        var expr = FreeSqlProjectionBuilder.Build<ContractEntity, ContractNestedDto>();
        var dto = expr.Compile()(CreateTestEntity());

        Assert.NotNull(dto.Child);
        Assert.Equal("ChildLabel", dto.Child!.Label);
        Assert.NotNull(dto.Child.GrandChild);
        Assert.Equal("Deep", dto.Child.GrandChild!.DeepValue);
    }

    /// <summary>契约5: 降级场景（ConvertUsing）— 走 MapList 路径结果正确</summary>
    [Fact]
    public void Contract_Degradation()
    {
        Assert.True(FreeSqlDegradationDetector.NeedsDegradation(
            typeof(ContractEntity), typeof(ContractDegradedDto), out _));

        var dto = Mapper.Map<ContractEntity, ContractDegradedDto>(CreateTestEntity());
        Assert.NotNull(dto);
        Assert.Equal(1, dto!.Id);
        Assert.Equal("Test", dto.Name);
        Assert.Equal("$99.95", dto.PriceText);
    }

    /// <summary>契约6: Compose 多源字段正确分配</summary>
    [Fact]
    public void Contract_Compose()
    {
        var a = new ContractComposeA { Id = 1, Title = "Hello" };
        var b = new ContractComposeB { Id = 2, Category = "Tech" };

        var expr = FreeSqlProjectionBuilder.BuildCompose<ContractComposeA, ContractComposeB, ContractComposeDto>();
        var dto = expr.Compile()(a, b);

        Assert.Equal(1, dto.Id);
        Assert.Equal("Hello", dto.Title);
        Assert.Equal("Tech", dto.CategoryName);
    }

    /// <summary>契约7: Mask 触发降级 — 降级检测正确 + MaskProvider 脱敏输出正确</summary>
    [Fact]
    public void Contract_MaskDegradation()
    {
        Assert.True(FreeSqlDegradationDetector.NeedsDegradation(
            typeof(ContractEntity), typeof(ContractMaskDto), out var reason));
        Assert.Contains("Mask", reason);

        var mask = new MaskPhone();
        Assert.Equal("138****5678", mask.Mask("13812345678"));
    }
}

/// <summary>
/// 跨 ORM 等价性契约测试 — 验证 EFCore 和 FreeSql 对相同输入产生相同 DTO
/// </summary>
public class CrossOrmEquivalenceContractTests
{
    private static ContractEntity CreateTestEntity() => new()
    {
        Id = 42,
        Name = "Equivalence",
        Price = 123.45m,
        Child = new ContractChild
        {
            Id = 7,
            Label = "Sub",
            GrandChild = new ContractGrandChild { Id = 77, DeepValue = "Bottom" }
        },
        Items = new List<ContractItem> { new() { Id = 1, Sku = "X" } }
    };

    /// <summary>平坦映射：EFCore 与 FreeSql 输出逐属性等价</summary>
    [Fact]
    public void Equivalence_FlatMapping()
    {
        var entity = CreateTestEntity();

        var efDto = EfCoreProjectionBuilder.Build<ContractEntity, ContractFlatDto>().Compile()(entity);
        var fsDto = FreeSqlProjectionBuilder.Build<ContractEntity, ContractFlatDto>().Compile()(entity);

        Assert.Equal(efDto.Id, fsDto.Id);
        Assert.Equal(efDto.Name, fsDto.Name);
        Assert.Equal(efDto.Price, fsDto.Price);
    }

    /// <summary>嵌套映射：EFCore 与 FreeSql 输出逐属性等价</summary>
    [Fact]
    public void Equivalence_NestedMapping()
    {
        var entity = CreateTestEntity();

        var efDto = EfCoreProjectionBuilder.Build<ContractEntity, ContractNestedDto>().Compile()(entity);
        var fsDto = FreeSqlProjectionBuilder.Build<ContractEntity, ContractNestedDto>().Compile()(entity);

        Assert.Equal(efDto.Id, fsDto.Id);
        Assert.Equal(efDto.Name, fsDto.Name);
        Assert.NotNull(efDto.Child);
        Assert.NotNull(fsDto.Child);
        Assert.Equal(efDto.Child!.Id, fsDto.Child!.Id);
        Assert.Equal(efDto.Child.Label, fsDto.Child.Label);
        Assert.Equal(efDto.Child.GrandChild!.DeepValue, fsDto.Child.GrandChild!.DeepValue);
    }

    /// <summary>null 导航：EFCore 与 FreeSql 均返回 null 子对象</summary>
    [Fact]
    public void Equivalence_NullNavigation()
    {
        var entity = CreateTestEntity();
        entity.Child = null;

        var efDto = EfCoreProjectionBuilder.Build<ContractEntity, ContractNestedDto>().Compile()(entity);
        var fsDto = FreeSqlProjectionBuilder.Build<ContractEntity, ContractNestedDto>().Compile()(entity);

        Assert.Null(efDto.Child);
        Assert.Null(fsDto.Child);
    }

    /// <summary>降级场景：两者均降级到 MapList，结果等价</summary>
    [Fact]
    public void Equivalence_Degradation()
    {
        var entity = CreateTestEntity();

        // 两者都应检测到降级
        Assert.True(EfCoreDegradationDetector.NeedsDegradation(
            typeof(ContractEntity), typeof(ContractDegradedDto), out _));
        Assert.True(FreeSqlDegradationDetector.NeedsDegradation(
            typeof(ContractEntity), typeof(ContractDegradedDto), out _));

        // 降级路径统一走 Mapper.Map → 结果自然等价
        // 降级后统一走 Mapper.Map——同一输入两次调用输出一致（确定性；修复）
        var dto1 = Mapper.Map<ContractEntity, ContractDegradedDto>(entity);
        var dto2 = Mapper.Map<ContractEntity, ContractDegradedDto>(entity);

        Assert.Equal(dto1!.Id, dto2!.Id);
        Assert.Equal(dto1.Name, dto2.Name);
        Assert.Equal(dto1.PriceText, dto2.PriceText);
        // 映射正确性（非自比较）：与源实体一致
        Assert.Equal(entity.Id, dto1.Id);
        Assert.Equal(entity.Name, dto1.Name);
        // 注：EFCore 与 FreeSql 投影路径（非降级）的输出等价性由本文件 Equivalence_* 投影场景测试承接
    }

    /// <summary>Mask 降级：两者均检测到降级，MaskProvider 输出一致</summary>
    [Fact]
    public void Equivalence_MaskDegradation()
    {
        Assert.True(EfCoreDegradationDetector.NeedsDegradation(
            typeof(ContractEntity), typeof(ContractMaskDto), out _));
        Assert.True(FreeSqlDegradationDetector.NeedsDegradation(
            typeof(ContractEntity), typeof(ContractMaskDto), out _));

        // Mask 是源生成器专属，运行时不支持 Mapper.Map → 验证 MaskProvider 输出
        var mask = new MaskPhone();
        var result = mask.Mask("13812345678");
        Assert.Equal("138****5678", result);
    }

    // ================================================================
    // 补位 BuildProjection 路径 NRE 回归
    // 2026-07-18 修复点（CollectProjectionBindings/BuildProjection）：DTO 同时含
    // [Convert] 无源属性 + 扁平化属性时，投影构建不得因 SourceProperty=null 抛 NRE。
    // 断言"不抛 NullReferenceException"；其他契约异常（如"需降级"的 InvalidOperationException）
    // 属投影构建器的设计行为，允许（降级路径由各自 ORM 测试承接）。
    // ================================================================

    /// <summary>NRE3 防回归实体</summary>
    public class Nre3UserEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public Nre3Address? Address { get; set; }
    }

    /// <summary>NRE3 防回归嵌套地址</summary>
    public class Nre3Address
    {
        public string City { get; set; } = "";
    }

    /// <summary>无操作转换器（静态方法引用形态，T1 模板 M(TField)）— null → 空串</summary>
    public static class Nre3NoOpConverter
    {
        /// <summary>原值返回（null → 空串）</summary>
        public static string Convert(string fieldValue) => fieldValue ?? "";
    }

    /// <summary>DTO 同时含 [Convert] 无源属性 + 扁平化属性（2026-07-18 NRE3 复现条件）</summary>
    [MapFrom(typeof(Nre3UserEntity))]
    public class Nre3FlattenAndConverterDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        [Convert(typeof(Nre3NoOpConverter), nameof(Nre3NoOpConverter.Convert))]
        public string? ComputedField { get; set; }

        public string? AddressCity { get; set; } // 扁平化：Address.City
    }

    [Fact]
    public void Nre3_ProjectionBuilder_ConvertUsingNoSource_DoesNotThrowNre()
    {
        Exception? thrown = null;
        try
        {
            var efExpr = EfCoreProjectionBuilder.Build<Nre3UserEntity, Nre3FlattenAndConverterDto>();
            Assert.NotNull(efExpr);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }
        Assert.False(thrown is NullReferenceException,
            $"EFCore BuildProjection 不应抛 NRE（2026-07-18 修复点）: {thrown}");

        thrown = null;
        try
        {
            var fsExpr = FreeSqlProjectionBuilder.Build<Nre3UserEntity, Nre3FlattenAndConverterDto>();
            Assert.NotNull(fsExpr);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }
        Assert.False(thrown is NullReferenceException,
            $"FreeSql BuildProjection 不应抛 NRE（2026-07-18 修复点）: {thrown}");
    }
}
