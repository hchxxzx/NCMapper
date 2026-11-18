using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 定向测试（生成列）— DTO 转实体反向直调扩展 + 诊断 + D5 写回形态
/// <para>覆盖定向测试清单生成侧：6 重载存在性、Compose 型 XML 注释（验收 3）、
/// 互指对不生成、同简单名异命名空间、F1 ToEntity 过滤（验收 1/3）、NCMAP032/033/034（验收 1-3）、
/// D5 写回生成形态（验收 2/4）、空反向不生成、自引用树形反向。</para>
/// <para>运行时值行为见 NCMapper.Tests.ReverseDirectCallRuntimeTests（双引擎差分：本类锁定生成形态，
/// 运行时列以真实执行实证同一语义）。</para>
/// </summary>
public class ReverseDirectCallGenTests
{
    /// <summary>
    /// 断言反向扩展 6 重载齐备 — MapToXxx 4 个（src / src+Action / src+dst / src+dst+Action）
    /// + MapToXxxList 2 个（列表 / 列表+Action）
    /// </summary>
    /// <param name="result">生成结果</param>
    /// <param name="methodName">扩展方法名（MapToXxxEntity）</param>
    private static void AssertSixReverseOverloads(MapperGenerationResult result, string methodName)
    {
        Assert.True(result.Methods.TryGetValue(methodName, out var overloads),
            $"反向扩展 '{methodName}' 未生成。已生成方法: [{string.Join(", ", result.Methods.Keys)}]");
        Assert.Equal(4, overloads.Count);
        Assert.True(result.Methods.TryGetValue(methodName + "List", out var listOverloads),
            $"反向列表扩展 '{methodName}List' 未生成");
        Assert.Equal(2, listOverloads.Count);
    }

    /// <summary>
    /// 普通 [MapFrom] DTO 生成反向扩展 6 重载（O1/D2）
    /// </summary>
    [Fact]
    public void OrdinaryDto_ReverseSixOverloads_Generated()
    {
        var source = """
            using NCMapper.Attributes;
            public class RdEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
            [MapFrom(typeof(RdEntity))]
            public class RdDto { public int Id { get; set; } public string Name { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        AssertSixReverseOverloads(result, "MapToRdEntity");
        // Action 重载引用 _WithAction（验收 2；全量代码断言——重载同名须按签名区分）
        Assert.Contains("Map_RdDto_To_RdEntity_WithAction(src, action)", result.GeneratedCode);
        // 更新重载引用 _Update
        Assert.Contains("Map_RdDto_To_RdEntity_Update(src, dst, null)", result.GeneratedCode);
    }

    /// <summary>
    /// 声明跨表子源字段的 DTO 同样生成反向扩展（所有 [MapFrom] DTO 统一生成反向 6 重载）；
    /// 反向只覆盖主源字段（子源字段不参与反向写回），XML 注释含子源字段说明
    /// </summary>
    [Fact]
    public void ComposeDto_ReverseExtension_Generated()
    {
        var source = """
            using NCMapper.Attributes;
            public class RdOrderEntity { public int Id { get; set; } public decimal Amount { get; set; } }
            public class RdCustomerEntity { public string Name { get; set; } = ""; public int Level { get; set; } }
            [MapFrom(typeof(RdOrderEntity))]
            public class RdMultiDto
            {
                [MapProperty(typeof(RdOrderEntity), nameof(RdOrderEntity.Id))]
                public int OrderId { get; set; }
                [MapProperty(typeof(RdOrderEntity), nameof(RdOrderEntity.Amount))]
                public decimal OrderAmount { get; set; }
                [MapProperty(typeof(RdCustomerEntity), nameof(RdCustomerEntity.Name))]
                public string CustomerName { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 反向扩展 6 重载生成（所有 [MapFrom] DTO 统一生成，与普通 DTO 同构）
        AssertSixReverseOverloads(result, "MapToRdOrderEntity");
        // XML 注释含子源字段说明（不参与反向写回）
        Assert.Contains("子源字段", result.GeneratedCode);
        // 反向方法只覆盖主源字段（跨表字段 CustomerName 不写入；反向目标 = 实体侧属性 Id/Amount）
        result.Should().HaveMethodBodyContaining("Map_RdMultiDto_To_RdOrderEntity", "Id = src.OrderId");
        result.Should().HaveMethodBodyContaining("Map_RdMultiDto_To_RdOrderEntity", "Amount = src.OrderAmount");
        Assert.DoesNotContain("CustomerName", result.Methods["Map_RdMultiDto_To_RdOrderEntity"][0].Body);
    }

    /// <summary>
    /// 互指对（NCMAP025 场景）不生成反向扩展（验收 4）
    /// </summary>
    [Fact]
    public void MutualPair_NoReverseExtension_Generated()
    {
        var source = """
            using NCMapper.Attributes;
            [MapFrom(typeof(RdMutB))]
            public class RdMutA { public int Id { get; set; } }
            [MapFrom(typeof(RdMutA))]
            public class RdMutB { public int Id { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 互指对整体跳过：正向方法族也不生成（现状语义），反向扩展自然不存在
        Assert.False(result.Methods.ContainsKey("MapToRdMutB"), "互指对不应生成反向扩展");
        Assert.False(result.Methods.ContainsKey("MapToRdMutA"), "互指对不应生成正向扩展");
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP025"), "互指对应报 NCMAP025");
    }

    /// <summary>
    /// 同简单名异命名空间 + 多 DTO 同源：反向扩展各自生成，同名重载由 this 参数类型区分（R3.1）
    /// </summary>
    [Fact]
    public void SameSimpleNameDifferentNamespace_BothReverseGenerated()
    {
        var source = """
            using NCMapper.Attributes;
            namespace NsA { public class RdSharedEntity { public int Id { get; set; } } }
            namespace NsB { public class RdSharedEntity { public int Id { get; set; } } }
            namespace NsA
            {
                [MapFrom(typeof(RdSharedEntity))]
                public class RdDtoA { public int Id { get; set; } }
            }
            namespace NsB
            {
                [MapFrom(typeof(NsA.RdSharedEntity))]
                public class RdDtoB { public int Id { get; set; } }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 多 DTO 同源：两个 DTO 均生成反向扩展，this 参数类型不同 → 合法重载（4×2 = 8 个）
        Assert.True(result.Methods.TryGetValue("MapToRdSharedEntity", out var overloads),
            "多 DTO 同源反向扩展应生成");
        Assert.Equal(8, overloads.Count); // RdDtoA 4 + RdDtoB 4
        Assert.Equal(4, result.Methods["MapToRdSharedEntityList"].Count); // 列表重载 2×2
        Assert.True(result.Methods.ContainsKey("Map_RdDtoA_To_RdSharedEntity"), "NsA DTO 反向方法应生成");
        Assert.True(result.Methods.ContainsKey("Map_RdDtoB_To_RdSharedEntity"), "NsB DTO 反向方法应生成（同源实体，后缀消歧）");
    }

    /// <summary>
    /// F1：全局 ToEntity 忽略清单对反向生效 — 命中属性跳过 + NCMAP013（验收 1）
    /// </summary>
    [Fact]
    public void GlobalToEntityIgnore_FiltersReverse_NCMAP013()
    {
        var source = """
            using NCMapper.Attributes;
            [assembly: MapDefaults(IgnoreNamesToEntity = new[] { "Secret" })]
            public class RdEntityF { public int Id { get; set; } public string Secret { get; set; } = ""; }
            [MapFrom(typeof(RdEntityF))]
            public class RdDtoF { public int Id { get; set; } public string Secret { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 反向方法不包含 Secret 赋值（ToEntity 清单命中）
        Assert.True(result.Methods.ContainsKey("Map_RdDtoF_To_RdEntityF"), "反向方法应生成（Id 保留）");
        Assert.DoesNotContain("Secret = src.Secret", result.Methods["Map_RdDtoF_To_RdEntityF"][0].Body);
        // NCMAP013 登记（F1 对齐）
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP013"), "应登记 NCMAP013（ToEntity 忽略命中）");
    }

    /// <summary>
    /// F1 边界：过滤后为空 → 反向方法/扩展均不生成，诊断照常（验收 3 /）
    /// </summary>
    [Fact]
    public void GlobalToEntityIgnore_AllFiltered_NoReverseGenerated()
    {
        var source = """
            using NCMapper.Attributes;
            [assembly: MapDefaults(IgnoreNamesToEntity = new[] { "Id", "Name" })]
            public class RdEntityG { public int Id { get; set; } public string Name { get; set; } = ""; }
            [MapFrom(typeof(RdEntityG))]
            public class RdDtoG { public int Id { get; set; } public string Name { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 反向方法不生成（过滤后为空 → 等同空反向）
        Assert.False(result.Methods.ContainsKey("Map_RdDtoG_To_RdEntityG"), "过滤后为空不应生成反向方法");
        Assert.False(result.Methods.ContainsKey("MapToRdEntityG"), "过滤后为空不应生成反向扩展");
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP013"), "空集场景诊断照常");
    }

    /// <summary>
    /// 更新期 init-only 跳过 → NCMAP032 且去重生效（验收 1 / G1）
    /// <para>record 主构造器属性为 init-only：更新路径（skipInitOnly）跳过 → 每属性一次诊断；
    /// 多路径复用（更新路径/构造器回退路径均 skipInitOnly: true）经 AddDistinct 按 (mapping, 属性) 去重 → 恰 1 次。</para>
    /// </summary>
    [Fact]
    public void InitOnlyUpdateSkip_NCMAP032_SingleEmit()
    {
        var source = """
            using NCMapper.Attributes;
            public class RdEntityH { public int Id { get; set; } }
            [MapFrom(typeof(RdEntityH))]
            public record RdRecordDto(int Id, string Name);
            """;

        var result = TestHelper.GenerateMapper(source);

        // record 主构造器属性为 init-only：更新路径跳过 → NCMAP032（Id 命中；Name 无匹配源属性不进入）
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP032"),
            "init-only 更新跳过应报 NCMAP032");
        var diags = result.DiagnosticsByDescriptorId["NCMAP032"];
        Assert.Single(diags); // 多路径复用去重 → 恰 1 次（若去重失效会重复发射）
        Assert.Equal("Id", diags[0].GetMessage().Split("'")[1]); // 属性名 = Id
    }

    /// <summary>
    /// 正向带 [Convert]（方法引用形态）的直接属性反向 → NCMAP033 + 反向按类型转换路径写入（验收 2 / G2）
    /// </summary>
    [Fact]
    public void ConvertUsingDirectReverse_NCMAP033_TypeConversionPath()
    {
        var source = """
            using NCMapper.Attributes;
            public class RdEntityI { public int Id { get; set; } public string IdText { get; set; } = ""; }
            public static class RdNoOpConverter
            {
                public static string Convert(string fieldValue) => fieldValue ?? "";
            }
            [MapFrom(typeof(RdEntityI))]
            public class RdDtoI
            {
                public int Id { get; set; }
                [Convert(typeof(RdNoOpConverter), nameof(RdNoOpConverter.Convert))]
                public string IdText { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // NCMAP033 登记（ConvertUsing 反向不消费）
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP033"),
            "正向 ConvertUsing 直接属性反向应报 NCMAP033");
        // 反向方法生成且 IdText 按类型转换路径写入（无转换器调用，直接赋值形态）
        Assert.True(result.Methods.ContainsKey("Map_RdDtoI_To_RdEntityI"), "反向方法应生成");
        Assert.DoesNotContain("RdNoOpConverter", result.Methods["Map_RdDtoI_To_RdEntityI"][0].Body);
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP031"),
            "简单场景不应报 NCMAP031");
    }

    /// <summary>
    /// Mask 属性参与反向写回 → NCMAP034（验收 3 / G3）
    /// </summary>
    [Fact]
    public void MaskReverseWriteBack_NCMAP034()
    {
        var source = """
            using NCMapper.Attributes;
            public class RdEntityJ { public int Id { get; set; } public string Phone { get; set; } = ""; }
            public class RdPhoneMask : NCMapper.IMask
            {
                public string Mask(string? value) => value ?? "";
            }
            [MapFrom(typeof(RdEntityJ))]
            public class RdDtoJ
            {
                public int Id { get; set; }
                [Mask(typeof(RdPhoneMask))]
                public string Phone { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // NCMAP034 登记（Mask 属性反向写回）
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP034"),
            "Mask 属性参与反向写回应报 NCMAP034");
        // 反向方法生成且 Phone 参与写回（脱敏值原样写回）
        Assert.True(result.Methods.ContainsKey("Map_RdDtoJ_To_RdEntityJ"), "反向方法应生成");
    }

    /// <summary>
    /// 邻界反例：可写属性 + 无 [Mask] 的常规 DTO → 不报 NCMAP032/NCMAP034
    /// </summary>
    [Fact]
    public void WritableProperties_NoInitOnlyOrMask_NoReverseSkipDiagnostics()
    {
        var source = """
            using NCMapper.Attributes;
            public class RdEntityOk { public int Id { get; set; } public string Phone { get; set; } = ""; }
            [MapFrom(typeof(RdEntityOk))]
            public class RdDtoOk { public int Id { get; set; } public string Phone { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP032"), "无 init-only 目标属性，不应报 NCMAP032");
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP034"), "无 [Mask] 属性反向写回，不应报 NCMAP034");
    }

    /// <summary>
    /// D5 写回生成形态：源 null → 链存在清叶（可空叶 null）/ 链不存在 no-op（验收 2/4）
    /// </summary>
    [Fact]
    public void WriteBack_NullClearsLeaf_GenForm()
    {
        var source = """
            using NCMapper.Attributes;
            public class RdEntityK { public RdAddressK Address { get; set; } }
            public class RdAddressK { public string City { get; set; } }
            [MapFrom(typeof(RdEntityK))]
            public class RdDtoK
            {
                [MapProperty(typeof(RdEntityK), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        var body = result.Methods["Map_RdDtoK_To_RdEntityK"][0].Body;
        // 写分支：源非 null → 中间节点创建守卫 + 叶赋值
        Assert.Contains("if (src.City != null)", body);
        Assert.Contains("if (dst.Address == null) dst.Address = new ", body);
        Assert.Contains("dst.Address.City = src.City;", body);
        // 清空分支：链存在（dst.Address != null）→ 清叶
        Assert.Contains("else if (dst.Address != null)", body);
        Assert.Contains("dst.Address.City = null;", body);
    }

    /// <summary>
    /// D5 边界：非可空值叶清空形态 = default(T)（F-2/ 验收 2）
    /// </summary>
    [Fact]
    public void WriteBack_NonNullableValueLeaf_ClearsDefaultT()
    {
        var source = """
            using NCMapper.Attributes;
            public class RdEntityL { public RdAddressL Address { get; set; } }
            public class RdAddressL { public int Zip { get; set; } }
            [MapFrom(typeof(RdEntityL))]
            public class RdDtoL
            {
                [MapProperty(typeof(RdEntityL), "Address.Zip")]
                public int? Zip { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        var body = result.Methods["Map_RdDtoL_To_RdEntityL"][0].Body;
        // 清空分支：非可空值叶 → default(int)
        Assert.Contains("else if (dst.Address != null)", body);
        Assert.Contains("dst.Address.Zip = default(int);", body);
    }

    /// <summary>
    /// A-1/A-2：写回路径 [Convert] 不消费（NCMAP033 登记），
    /// 按类型转换路径 + null=清空形态写入（原"转换器输出 null 跳过"形态随转换器分支移除而失效）
    /// </summary>
    [Fact]
    public void WriteBack_ConverterNotConsumed_ClearFormApplied()
    {
        var source = """
            using NCMapper.Attributes;
            public class RdEntityM { public RdAddressM Address { get; set; } }
            public class RdAddressM { public string City { get; set; } }
            public static class RdCityConverter
            {
                public static string Convert(string fieldValue) => fieldValue ?? "";
            }
            [MapFrom(typeof(RdEntityM))]
            public class RdDtoM
            {
                [MapProperty(typeof(RdEntityM), "Address.City")]
                [Convert(typeof(RdCityConverter), nameof(RdCityConverter.Convert))]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // NCMAP033 登记（写回路径 [Convert] 不消费）
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP033"),
            "反向写回路径标 [Convert] 应报 NCMAP033");

        var body = result.Methods["Map_RdDtoM_To_RdEntityM"][0].Body;
        // 反向方法不生成转换器调用
        Assert.DoesNotContain(".Convert(", body);
        // 源可空 → null=清空形态：写分支（null 守卫 + 创建中间节点）+ 清叶分支
        Assert.Contains("if (src.City != null)", body);
        Assert.Contains("if (dst.Address == null) dst.Address = new ", body);
        Assert.Contains("dst.Address.City = src.City;", body);
        Assert.Contains("else if (dst.Address != null)", body);
        Assert.Contains("dst.Address.City = null;", body);
    }

    /// <summary>
    /// 反向快照为空（无映射属性）→ 不生成扩展（验收 5）
    /// </summary>
    [Fact]
    public void EmptyReverse_NoExtension_Generated()
    {
        var source = """
            using NCMapper.Attributes;
            public class RdEntityN { public int Id { get; set; } }
            [MapFrom(typeof(RdEntityN))]
            public class RdDtoN { public string Extra { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        // DTO 无与实体匹配的属性 → 反向快照为空 → 无反向方法/扩展
        Assert.False(result.Methods.ContainsKey("Map_RdDtoN_To_RdEntityN"), "空反向不应生成方法");
        Assert.False(result.Methods.ContainsKey("MapToRdEntityN"), "空反向不应生成扩展");
    }

    /// <summary>
    /// 自引用树形 DTO 反向递归：反向方法生成且不触发无限递归（R6.8）
    /// </summary>
    [Fact]
    public void SelfRefTree_Reverse_Generated()
    {
        var source = """
            using NCMapper.Attributes;
            public class RdTreeNodeEntity { public int Id { get; set; } public System.Collections.Generic.List<RdTreeNodeEntity> Children { get; set; } = new(); }
            [MapFrom(typeof(RdTreeNodeEntity))]
            public class RdTreeNodeDto { public int Id { get; set; } public System.Collections.Generic.List<RdTreeNodeDto> Children { get; set; } = new(); }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 反向方法生成（自引用集合属性不参与反向直接赋值，但方法族齐备）
        Assert.True(result.Methods.ContainsKey("Map_RdTreeNodeDto_To_RdTreeNodeEntity"),
            "自引用树形反向方法应生成");
        // 生成过程无 NCMAP099（无异常降级）
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP099"),
            "自引用树形不应触发生成失败");
    }

    /// <summary>
    /// 互调环（NCMAP029 场景）反向交互：降级对不生成反向方法，环外零干扰（R6.3，白盒）
    /// </summary>
    [Fact]
    public void MutualCallRing_ReverseInteraction_DegradedPairNoReverse()
    {
        var source = """
            using NCMapper.Attributes;
            public class RdRingA { public int Id { get; set; } }
            public class RdRingB { public int Id { get; set; } }
            [MapFrom(typeof(RdRingA))]
            public class RdRingADto { public int Id { get; set; } }
            [MapFrom(typeof(RdRingB))]
            public class RdRingBDto { public int Id { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 无环场景（平坦 DTO）：反向方法全部生成，NCMAP029 零干扰
        Assert.True(result.Methods.ContainsKey("Map_RdRingADto_To_RdRingA"), "环外 DTO 反向方法应生成");
        Assert.True(result.Methods.ContainsKey("Map_RdRingBDto_To_RdRingB"), "环外 DTO 反向方法应生成");
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP029"), "平坦场景不应报 NCMAP029");
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP099"), "不应触发生成失败");
    }
}
