using System;
using System.Linq;
using NCMapper.Generated;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// Fluent 通道 PoC 冒烟测试（B-4/D-1 方案 A——验收 #1/#2/#7 PoC 级抽样验证；
/// spike 35 测试全量移植归 Batch-3）。
/// <para>两层验证：</para>
/// <list type="bullet">
/// <item>文本层（TestHelper.GenerateMapper）：派生方法清单（6 正向 + 4 反向）、注册行、
/// AfterMapping 调用发射行、双通道并存无方法名冲突（P3/P4）</item>
/// <item>运行时层（本测试程序集自生成代码直调）：Map 触发 AfterMapping 副作用、MapList 逐项
/// 调用、NoAutoInclude 语义、Mask/Convert 行为、反向派生（P5）</item>
/// </list>
/// </summary>
public class FluentSmokePoCTests
{
    /// <summary>Fluent PoC 源码（文本层生成验证用——与运行时模型同构）</summary>
    private const string FluentSource = """
        using NCMapper;
        namespace FluentPoC
        {
            public class Order { public int OrderId { get; set; } public string Note { get; set; } = ""; }
            public class OrderDto { public int OrderId { get; set; } public string Note { get; set; } = ""; public string? Tag { get; set; } }

            [MapperConfig]
            public class OrderMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<Order, OrderDto>()
                       .Member(d => d.OrderId).From(s => s.OrderId)
                       .Member(d => d.Note).Ignore()
                       .NoAutoMatch()
                       .AfterMapping(ApplyTag);
                }
                public static void ApplyTag(Order src, OrderDto dst) { dst.Tag = "after:" + src.OrderId; }
            }
        }
        """;

    /// <summary>
    /// 验收 #1/#2：完整物化产物喂现有派生内核——6 正向 + 4 反向方法清单 + 注册行 + AfterMapping 发射
    /// </summary>
    [Fact]
    public void FluentPoC_Generates_SixForward_And_FourReverse_Methods()
    {
        var result = TestHelper.GenerateMapper(FluentSource);
        var code = result.GeneratedCode;

        // 6 正向形态（Core/WithAction/Update/List/ListInto；Compose 需跨表字段——PoC 无）
        Assert.Contains("Map_Order_To_OrderDto(FluentPoC.Order src)", code);
        Assert.Contains("Map_Order_To_OrderDto_WithAction(FluentPoC.Order src", code);
        Assert.Contains("Map_Order_To_OrderDto_Update(", code);
        Assert.Contains("MapList_Order_To_OrderDto(", code);
        Assert.Contains("MapListInto_Order_To_OrderDto(", code);

        // 4 反向形态（Core/WithAction/Update/List——无 ListInto/Compose，设计决策）
        Assert.Contains("Map_OrderDto_To_Order(FluentPoC.OrderDto src)", code);
        Assert.Contains("Map_OrderDto_To_Order_WithAction(FluentPoC.OrderDto src", code);
        Assert.Contains("Map_OrderDto_To_Order_Update(", code);
        Assert.Contains("MapList_OrderDto_To_Order(", code);

        // P5：模块初始化器注册（正向 5 委托 + 反向 4 委托）
        Assert.Contains("RegisterGeneratedCore<FluentPoC.Order, FluentPoC.OrderDto>(GeneratedMapper.Map_Order_To_OrderDto)", code);
        Assert.Contains("RegisterGeneratedListInto<FluentPoC.Order, FluentPoC.OrderDto>", code);
        Assert.Contains("RegisterGeneratedCore<FluentPoC.OrderDto, FluentPoC.Order>(GeneratedMapper.Map_OrderDto_To_Order)", code);

        // 验收 #7①：AfterMapping 调用发射行（核心方法末尾）
        Assert.Contains("FluentPoC.OrderMapper.ApplyTag(src, dst);", code);
    }

    /// <summary>
    /// 验收 #7③（否定）+ F4 字节不变：[MapFrom] 通道产物（无 Fluent 声明）不含 AfterMapping 调用发射
    /// </summary>
    [Fact]
    public void FluentPoC_MapFromOnly_GeneratedCode_HasNo_AfterMapping_Emission()
    {
        var source = """
            using NCMapper.Attributes;
            namespace MapFromOnly
            {
                public class Src { public int Id { get; set; } }
                [MapFrom(typeof(Src))]
                public class Dst { public int Id { get; set; } }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("Map_Src_To_Dst(MapFromOnly.Src src)", result.GeneratedCode);
        // AfterMappingMethodRef 恒 null → 无任何 AfterMapping 发射（IsNullOrEmpty 短路）
        Assert.DoesNotContain("ApplyTag(src, dst)", result.GeneratedCode);
        Assert.DoesNotContain("ApplyCustom", result.GeneratedCode);
    }

    /// <summary>
    /// P4/Q3：双通道并存（不同 (S,T) 对）无方法名冲突——[MapFrom] A→ADto + Fluent B→BDto 同文件
    /// </summary>
    [Fact]
    public void FluentPoC_DualChannel_Coexistence_NoMethodNameConflict()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace DualPoC
            {
                public class A { public int Id { get; set; } }
                [MapFrom(typeof(A))]
                public class ADto { public int Id { get; set; } }

                public class B { public int Id { get; set; } }
                public class BDto { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<B, BDto>().Member(d => d.Id).From(s => s.Id);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var code = result.GeneratedCode;

        // 两通道各自派生方法并存且互不冲突（P4）
        Assert.Contains("Map_A_To_ADto(", code);
        Assert.Contains("Map_B_To_BDto(", code);
        Assert.Contains("RegisterGeneratedCore<DualPoC.A, DualPoC.ADto>", code);
        Assert.Contains("RegisterGeneratedCore<DualPoC.B, DualPoC.BDto>", code);
    }

    /// <summary>
    /// 验收 #7①：运行时 Mapper.Map 命中注册委托并触发 AfterMapping（副作用断言——P5 全链路）
    /// </summary>
    [Fact]
    public void FluentPoC_Runtime_Map_Triggers_AfterMapping()
    {
        var src = new FluentOrder { OrderId = 42, CustomerName = "Alice", InternalNote = "secret", Total = 99m };
        var dst = GeneratedMapper.Map_FluentOrder_To_FluentOrderDto(src)!;

        // 显式 Map 声明生效
        Assert.Equal(42, dst.OrderId);
        // NoAutoInclude 对级语义：未显式声明的属性不自动匹配
        Assert.Equal("", dst.CustomerName);
        Assert.Equal(0m, dst.Total);
        // Ignore 语义：不映射
        Assert.Equal("", dst.InternalNote);
        // AfterMapping 副作用（验收 #7①）
        Assert.Equal("after:42", dst.ComputedTag);
    }

    /// <summary>
    /// 验收 #7②：List 形态逐项调用核心方法 → AfterMapping 逐项触发
    /// </summary>
    [Fact]
    public void FluentPoC_Runtime_MapList_InvokesAfterMapping_PerItem()
    {
        var sources = new System.Collections.Generic.List<FluentOrder>
        {
            new() { OrderId = 1 },
            new() { OrderId = 2 },
            new() { OrderId = 3 },
        };
        var dtos = GeneratedMapper.MapList_FluentOrder_To_FluentOrderDto(sources)!;

        Assert.Equal(3, dtos.Count);
        Assert.All(dtos, d => Assert.NotNull(d));
        Assert.Equal("after:1", dtos[0].ComputedTag);
        Assert.Equal("after:2", dtos[1].ComputedTag);
        Assert.Equal("after:3", dtos[2].ComputedTag);
    }

    /// <summary>
    /// 验收 #7④（否定）：反向 4 形态不发射 AfterMapping（FluentOrderDto → FluentOrder 无副作用载体语义）
    /// ——反向方法正常派生且映射正确（反向仅覆盖显式声明属性——S-3a 语义差异告知）
    /// </summary>
    [Fact]
    public void FluentPoC_Runtime_Reverse_Works_Without_AfterMapping()
    {
        var dto = new FluentOrderDto { OrderId = 7, CustomerName = "Bob", InternalNote = "note" };
        var entity = GeneratedMapper.Map_FluentOrderDto_To_FluentOrder(dto)!;

        // 反向基于正向显式声明逐条翻转：OrderId 映射；未显式声明/Ignore 属性保持默认
        Assert.Equal(7, entity.OrderId);
        Assert.Equal("", entity.CustomerName);
    }

    /// <summary>运行时：自动匹配默认开（S-3a）——Pair B 同名属性全量映射 + 反向派生可用</summary>
    [Fact]
    public void FluentPoC_Runtime_AutoMatch_Default_On()
    {
        var src = new FluentCategory { Id = 9, Name = "books" };
        var dto = GeneratedMapper.Map_FluentCategory_To_FluentCategoryDto(src)!;
        Assert.Equal(9, dto.Id);
        Assert.Equal("books", dto.Name);

        // 反向派生（4 形态）：Dto → Entity
        var back = GeneratedMapper.Map_FluentCategoryDto_To_FluentCategory(dto)!;
        Assert.Equal(9, back.Id);
        Assert.Equal("books", back.Name);
    }

    /// <summary>运行时：Convert 双泛型跨类型（DateTime→string 经 ConvertValue 内建转换，S-1a）</summary>
    [Fact]
    public void FluentPoC_Runtime_Convert_CrossType()
    {
        var stamp = new DateTime(2026, 9, 2, 12, 30, 0);
        var dto = GeneratedMapper.Map_FluentEvent_To_FluentEventDto(new FluentEvent { Timestamp = stamp })!;
        Assert.Equal(System.Convert.ToString(stamp, System.Globalization.CultureInfo.InvariantCulture), dto.DisplayTime);
    }

    /// <summary>运行时：Mask 脱敏（S-2a——下游 MaskProvider 共享，脱敏输出含 *）</summary>
    [Fact]
    public void FluentPoC_Runtime_Mask()
    {
        var src = new FluentUser { Name = "Alice", Phone = "13800138000" };
        var dto = GeneratedMapper.Map_FluentUser_To_FluentUserDto(src)!;
        Assert.Equal("Alice", dto.Name); // 未显式声明 → 自动匹配
        Assert.NotEqual("13800138000", dto.Phone);
        Assert.Contains("*", dto.Phone);
    }
}
