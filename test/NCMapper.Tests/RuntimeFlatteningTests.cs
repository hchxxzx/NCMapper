using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 运行时 Mapper.Map 命名约定扁平化测试
/// <para>覆盖:基础扁平 / 多段嵌套 / 值类型叶节点 / 同名优先 / 最短路径优先 / 方向守卫 /
/// [MapProperty]/[Convert] 优先级 / 配置×扁平化交叉(命名策略/前后缀/忽略清单/Reset) /
/// 缓存一致性 / 端到端 / [Mask] 不消费(O-1 方案 2)。</para>
/// <para>路径说明:DTO 标 [MapFrom] 使 targetIsDefine=true(方向守卫通过),但 DTO 为 private 嵌套类,
/// 源生成器跳过(private → NCMAP011 SkipCodeGeneration),Mapper.Map 走运行时 ExpressionBuilder 回退路径。</para>
/// <para>测试隔离:复用 GlobalConfigState 集合(进程级冻结状态,禁并行);每测试前 ClearCache。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class RuntimeFlatteningTests : IDisposable
{
    public RuntimeFlatteningTests() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); } // 迁移

    public void Dispose() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); } // 迁移

    // ============================================================
    // 模型
    // ============================================================

    private class RfCustomer
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public int Age { get; set; }
        public RfAddress? Address { get; set; }
    }

    private class RfAddress
    {
        public RfCity? City { get; set; }
    }

    private class RfCity
    {
        public string? Name { get; set; }
    }

    private class RfOrderEntity
    {
        public int Id { get; set; }
        public RfCustomer? Customer { get; set; }
    }

    [MapFrom(typeof(RfOrderEntity))]
    private class RfOrderDto
    {
        public int Id { get; set; }
        public string? CustomerName { get; set; }               // → Customer.Name
        public string? CustomerEmail { get; set; }              // → Customer.Email
        public string? CustomerAddressCityName { get; set; }    // → Customer.Address.City.Name (4 段)
        public int CustomerAge { get; set; }                    // 值类型叶 → Customer.Age
    }

    private class RfAmbiguousSource
    {
        public string? CustomerName { get; set; }               // 直接属性(同名优先)
        public RfCustomer? Customer { get; set; }
    }

    [MapFrom(typeof(RfAmbiguousSource))]
    private class RfAmbiguousDto
    {
        public string? CustomerName { get; set; }
    }

    private class RfA
    {
        public RfB? B { get; set; }
    }

    private class RfB
    {
        public string? Name { get; set; }
    }

    private class RfAB
    {
        public string? Name { get; set; }
    }

    private class RfShortestSource
    {
        public RfA? A { get; set; }
        public RfAB? AB { get; set; }
    }

    [MapFrom(typeof(RfShortestSource))]
    private class RfShortestDto
    {
        public string? ABName { get; set; }
    }

    // 方向守卫:源 DTO 与目标实体均无 [MapFrom] → isToDto=false → 不触发扁平化
    private class RfReverseSourceDto
    {
        public RfReverseCustomer? Customer { get; set; }
    }

    private class RfReverseCustomer
    {
        public string? Name { get; set; }
    }

    private class RfReverseTargetEntity
    {
        public string? CustomerName { get; set; }
    }

    private class RfPriSource
    {
        public RfPriCustomer? Customer { get; set; }
        public string? DirectName { get; set; }
    }

    private class RfPriCustomer
    {
        public string? Name { get; set; }
    }

    [MapFrom(typeof(RfPriSource))]
    private class RfPriDto
    {
        [MapProperty(typeof(RfPriSource), "DirectName")]
        public string? CustomerName { get; set; }
    }

    private class RfConvSource
    {
        public RfConvCustomer? Customer { get; set; }
    }

    private class RfConvCustomer
    {
        public string? Name { get; set; }
    }

    private static class RfNameConverter
    {
        public static string Convert(string fieldValue) => "converted";
    }

    [MapFrom(typeof(RfConvSource))]
    private class RfConvDto
    {
        [Convert(typeof(RfNameConverter), nameof(RfNameConverter.Convert))]
        public string? CustomerName { get; set; }
    }

    private class RfMaskSource
    {
        public RfMaskCustomer? Customer { get; set; }
    }

    private class RfMaskCustomer
    {
        public string? Phone { get; set; }
    }

    private class RfMaskImpl : IMask
    {
        public string Mask(string? value) => "MASKED";
    }

    [MapFrom(typeof(RfMaskSource))]
    private class RfMaskDto
    {
        [Mask(typeof(RfMaskImpl))]
        public string? CustomerPhone { get; set; }
    }

    // SnakeCase 单段命中:源有 customer_name 直接属性 + Customer.Name 路径
    private class RfSnakeSource
    {
        public string? customer_name { get; set; }
        public RfSnakeCustomer? Customer { get; set; }
    }

    private class RfSnakeCustomer
    {
        public string? Name { get; set; }
    }

    [MapFrom(typeof(RfSnakeSource))]
    private class RfSnakeDto
    {
        public string? CustomerName { get; set; }
    }

    // 配置下扁平化仍工作:仅 Customer.Name 路径(无单段)
    private class RfSnakeOnlyNestedSource
    {
        public RfSnakeCustomer? Customer { get; set; }
    }

    [MapFrom(typeof(RfSnakeOnlyNestedSource))]
    private class RfSnakeOnlyNestedDto
    {
        public string? CustomerName { get; set; }
    }

    // 前后缀段不剥缀:源导航属性带前缀 EntityCustomer,无 Customer 直接属性
    private class RfPrefixedSource
    {
        public RfPrefixedCustomer? EntityCustomer { get; set; }
    }

    private class RfPrefixedCustomer
    {
        public string? Name { get; set; }
    }

    [MapFrom(typeof(RfPrefixedSource))]
    private class RfPrefixedDto
    {
        public string? CustomerName { get; set; }
    }

    // 忽略清单叶节点:IgnoreNames=["Name"] → 叶 Name 命中忽略
    private class RfIgnoreSource
    {
        public RfIgnoreCustomer? Customer { get; set; }
    }

    private class RfIgnoreCustomer
    {
        public string? Name { get; set; }
    }

    [MapFrom(typeof(RfIgnoreSource))]
    private class RfIgnoreDto
    {
        public string? CustomerName { get; set; }
    }

    // 缓存专用类型对(U9 独占,避免被其他测试预热后 MapperBase 委托复用导致缓存不重建)
    private class RfCacheEntity
    {
        public RfCacheCustomer? Customer { get; set; }
    }

    private class RfCacheCustomer
    {
        public string? Name { get; set; }
    }

    [MapFrom(typeof(RfCacheEntity))]
    private class RfCacheDto
    {
        public string? CustomerName { get; set; }
    }

    // ============================================================
    // 用例
    // ============================================================


    /// <summary>U1 基础扁平 + null 导航:AuthorName → Author.Name;中间导航 null 时目标为 null 不抛 NRE</summary>
    [Fact]
    public void Basic_Flattening_And_NullNavigation_Works()
    {
        var entity = new RfOrderEntity { Id = 1, Customer = new RfCustomer { Name = "Alice", Email = "a@b.c", Age = 30 } };
        var dto = Mapper.Map<RfOrderEntity, RfOrderDto>(entity)!;
        Assert.Equal(1, dto.Id);
        Assert.Equal("Alice", dto.CustomerName);
        Assert.Equal("a@b.c", dto.CustomerEmail);
        Assert.Equal(30, dto.CustomerAge);

        // null 导航:Customer = null → CustomerName 为 null,不抛 NRE
        var empty = Mapper.Map<RfOrderEntity, RfOrderDto>(new RfOrderEntity { Id = 2 })!;
        Assert.Equal(2, empty.Id);
        Assert.Null(empty.CustomerName);
        Assert.Null(empty.CustomerEmail);
    }

    /// <summary>U2 多段嵌套:CustomerAddressCityName → Customer.Address.City.Name(4 段)</summary>
    [Fact]
    public void MultiLevel_Flattening_Works()
    {
        var entity = new RfOrderEntity
        {
            Customer = new RfCustomer { Name = "Bob", Address = new RfAddress { City = new RfCity { Name = "Shanghai" } } }
        };
        var dto = Mapper.Map<RfOrderEntity, RfOrderDto>(entity)!;
        Assert.Equal("Shanghai", dto.CustomerAddressCityName);

        // 中间段 null:Address = null → 目标为 null,不 NRE
        var empty = Mapper.Map<RfOrderEntity, RfOrderDto>(new RfOrderEntity { Customer = new RfCustomer() })!;
        Assert.Null(empty.CustomerAddressCityName);
    }

    /// <summary>U3 值类型叶节点:CustomerAge(int),Customer = null → default(int)=0,不 NRE</summary>
    [Fact]
    public void ValueTypeLeaf_Defaults_WhenNavigationNull()
    {
        var dto = Mapper.Map<RfOrderEntity, RfOrderDto>(new RfOrderEntity { Id = 9 })!;
        Assert.Equal(0, dto.CustomerAge);
    }

    /// <summary>U4 同名优先:源同时有 CustomerName 直接属性与 Customer.Name → 直接属性命中,不扁平化</summary>
    [Fact]
    public void SameName_Wins_Over_Flattening()
    {
        var source = new RfAmbiguousSource { CustomerName = "direct", Customer = new RfCustomer { Name = "nested" } };
        var dto = Mapper.Map<RfAmbiguousSource, RfAmbiguousDto>(source)!;
        Assert.Equal("direct", dto.CustomerName);
    }

    /// <summary>U5 最短路径优先:ABName 候选 [AB,Name](2 段)优先于 [A,B,Name](3 段)</summary>
    [Fact]
    public void ShortestPathCandidate_Wins()
    {
        var source = new RfShortestSource
        {
            A = new RfA { B = new RfB { Name = "deep" } },
            AB = new RfAB { Name = "shallow" }
        };
        var dto = Mapper.Map<RfShortestSource, RfShortestDto>(source)!;
        Assert.Equal("shallow", dto.ABName);
    }

    /// <summary>U6 方向守卫:双向均无 [MapFrom](isToDto=false)不触发扁平化,目标属性保持默认</summary>
    [Fact]
    public void ReverseDirection_DoesNot_Flatten()
    {
        var source = new RfReverseSourceDto { Customer = new RfReverseCustomer { Name = "x" } };
        var entity = Mapper.Map<RfReverseSourceDto, RfReverseTargetEntity>(source)!;
        Assert.Null(entity.CustomerName);
    }

    /// <summary>U7 [MapProperty] 显式标注优先于扁平化</summary>
    [Fact]
    public void ExplicitMapProperty_Wins_Over_Flattening()
    {
        var source = new RfPriSource { DirectName = "explicit", Customer = new RfPriCustomer { Name = "nested" } };
        var dto = Mapper.Map<RfPriSource, RfPriDto>(source)!;
        Assert.Equal("explicit", dto.CustomerName);
    }

    /// <summary>U7b [Convert] 无源属性优先于扁平化:转换器被调用</summary>
    [Fact]
    public void ConvertUsing_Wins_Over_Flattening()
    {
        var source = new RfConvSource { Customer = new RfConvCustomer { Name = "nested" } };
        var dto = Mapper.Map<RfConvSource, RfConvDto>(source)!;
        Assert.Equal("converted", dto.CustomerName);
    }

    /// <summary>U8 命名策略配置下扁平化仍生效(段匹配 OrdinalIgnoreCase,不随策略)</summary>
    [Fact]
    public void Flattening_Unaffected_By_NamingStrategy()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase }, "RfFlattenSnake");
        var source = new RfSnakeOnlyNestedSource { Customer = new RfSnakeCustomer { Name = "nested" } };
        var dto = Mapper.Map<RfSnakeOnlyNestedSource, RfSnakeOnlyNestedDto>(source)!;
        Assert.Equal("nested", dto.CustomerName);
    }

    /// <summary>U9 缓存一致性:同类型对二次 Map 结果一致;扁平化路径缓存有条目</summary>
    [Fact]
    public void FlattenPathCache_Consistent_Across_Maps()
    {
        var source1 = new RfCacheEntity { Customer = new RfCacheCustomer { Name = "c1" } };
        var source2 = new RfCacheEntity { Customer = new RfCacheCustomer { Name = "c2" } };
        var dto1 = Mapper.Map<RfCacheEntity, RfCacheDto>(source1)!;
        var dto2 = Mapper.Map<RfCacheEntity, RfCacheDto>(source2)!;
        Assert.Equal("c1", dto1.CustomerName);
        Assert.Equal("c2", dto2.CustomerName);
        Assert.True(ExpressionBuilder.FlattenPathCacheEntryCount > 0, "扁平化路径缓存应已有条目,实际:" + ExpressionBuilder.FlattenPathCacheEntryCount);

        // ClearCache 后缓存清空(清理链)
        Mapper.ClearCache();
        Assert.Equal(0, ExpressionBuilder.FlattenPathCacheEntryCount);
    }

    /// <summary>U10 端到端:与投影 G7 同构的内存版(全字段断言,含 4 段)</summary>
    [Fact]
    public void EndToEnd_Flattening_AllFields()
    {
        var entity = new RfOrderEntity
        {
            Id = 7,
            Customer = new RfCustomer { Name = "Alice Writer", Email = "alice@blog.com", Age = 42, Address = new RfAddress { City = new RfCity { Name = "Suzhou" } } }
        };
        var dto = Mapper.Map<RfOrderEntity, RfOrderDto>(entity)!;
        Assert.Equal(7, dto.Id);
        Assert.Equal("Alice Writer", dto.CustomerName);
        Assert.Equal("alice@blog.com", dto.CustomerEmail);
        Assert.Equal("Suzhou", dto.CustomerAddressCityName);
        Assert.Equal(42, dto.CustomerAge);
    }

    /// <summary>U11 [Mask] + 扁平化:消费脱敏生效（H6，替代旧 O-1 方案 2 不消费行为）</summary>
    [Fact]
    public void Mask_Consumed_By_Flattening()
    {
        var source = new RfMaskSource { Customer = new RfMaskCustomer { Phone = "13800000000" } };
        var dto = Mapper.Map<RfMaskSource, RfMaskDto>(source)!;
        Assert.Equal("MASKED", dto.CustomerPhone); // 脱敏生效(消费)
    }

    /// <summary>U12 SnakeCase 下源有 customer_name 单段属性时,单段命中优先,不触发扁平化</summary>
    [Fact]
    public void ConfigAwareSingleSegment_Wins_Over_Flattening()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase }, "RfFlattenSnake2");
        var source = new RfSnakeSource { customer_name = "single", Customer = new RfSnakeCustomer { Name = "nested" } };
        var dto = Mapper.Map<RfSnakeSource, RfSnakeDto>(source)!;
        Assert.Equal("single", dto.CustomerName);
    }

    /// <summary>U13 前后缀配置下扁平化段不剥缀:EntityCustomer 不匹配段 Customer</summary>
    [Fact]
    public void Prefixes_DoNot_Apply_To_FlattenSegments()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { Prefixes = ["Entity"] }, "RfFlattenPrefix");
        var source = new RfPrefixedSource { EntityCustomer = new RfPrefixedCustomer { Name = "prefixed" } };
        var dto = Mapper.Map<RfPrefixedSource, RfPrefixedDto>(source)!;
        Assert.Null(dto.CustomerName); // 段 Customer 不剥缀,找不到 → 不映射
    }

    /// <summary>U14 忽略清单命中扁平化叶节点时跳过(对齐源生成器值模型管线,评审核对 V-13)</summary>
    [Fact]
    public void GlobalIgnore_On_Leaf_Skips_Flattening()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = ["Name"] }, "RfFlattenIgnore");
        var source = new RfIgnoreSource { Customer = new RfIgnoreCustomer { Name = "x" } };
        var dto = Mapper.Map<RfIgnoreSource, RfIgnoreDto>(source)!;
        Assert.Null(dto.CustomerName);
    }

    /// <summary>U15 Reset 配置后,未预热类型对按新配置重新判定触发边界</summary>
    [Fact]
    public void ConfigReset_Redetermines_TriggerBoundary()
    {
        // 第一阶段:SnakeCase → 单段命中(类型对 A)
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase }, "RfFlattenReset1");
        var a = Mapper.Map<RfSnakeSource, RfSnakeDto>(new RfSnakeSource { customer_name = "single" })!;
        Assert.Equal("single", a.CustomerName);

        // 第二阶段:Reset + Default → 扁平化生效(类型对 B,未预热)
        // 迁移：ClearCache 不再清注册表，配置重置需显式全清
        Mapper.ClearCache();
        GlobalMapConfig.ResetForTests();
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { }, "RfFlattenReset2");
        var b = Mapper.Map<RfSnakeOnlyNestedSource, RfSnakeOnlyNestedDto>(
            new RfSnakeOnlyNestedSource { Customer = new RfSnakeCustomer { Name = "nested" } })!;
        Assert.Equal("nested", b.CustomerName);
    }
}
