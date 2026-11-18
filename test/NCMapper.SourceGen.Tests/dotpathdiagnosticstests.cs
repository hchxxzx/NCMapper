using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 点路径诊断用例正式化（A1 复盘承接）
/// <para>覆盖：NCMAP007 中间节点标记（多段路径中间导航属性不触发虚假"源未映射"）/
/// NCMAP009 显式重复双向断言（显式×显式报 / 显式×推导豁免，补冒烟缺口）/
/// NCMAP013 反向诊断措辞按 PathOrigin（显式点路径 vs 推导扁平化措辞区分）。</para>
/// <para>已有冒烟（DotPathSourceGenSmokeTests，A1 Batch-1）：NCMAP001 失败段名×2（缺失段/值类型）、
/// NCMAP009 显式×显式报 + 推导×推导豁免、NCMAP030 Mask/ConvertUsing 可观测×2 —— 本类不重复，
/// 仅补缺口并做"双向断言"完整化。</para>
/// </summary>
public class DotPathDiagnosticsTests
{
    // ==================== NCMAP007：中间节点标记 ====================

    /// <summary>
    /// 点路径 Address.City 的中间节点 Address 与叶 City 均标记为已映射 → 无虚假 NCMAP007
    /// <para>改判前（IsFlattened 语义缺失）：仅 SourceProperty（叶）标记，中间导航节点 Address 报"源未映射"。</para>
    /// </summary>
    [Fact]
    public void NCMAP007_IntermediateNode_MarkedAsMapped_NoFalsePositive()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtDiagEntity { public GtDiagAddress Address { get; set; } }
            public class GtDiagAddress { public string City { get; set; } }
            [MapFrom(typeof(GtDiagEntity))]
            public class GtDiagDto
            {
                [MapProperty(typeof(GtDiagEntity), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 中间节点 Address + 叶 City 均已映射 → 无 NCMAP007
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP007"),
            "点路径中间节点应标记为已映射，不应输出 NCMAP007（虚假源未映射）");
    }

    /// <summary>
    /// 反向保障：真正未映射的源属性仍报 NCMAP007（点路径改判不破坏检测）
    /// </summary>
    [Fact]
    public void NCMAP007_TrulyUnmapped_StillReported()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtDiagEntity2 { public GtDiagAddress2 Address { get; set; } public string Extra { get; set; } }
            public class GtDiagAddress2 { public string City { get; set; } }
            [MapFrom(typeof(GtDiagEntity2))]
            public class GtDiagDto2
            {
                [MapProperty(typeof(GtDiagEntity2), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP007"), "未映射源属性 Extra 应报 NCMAP007");
        var message = result.DiagnosticsByDescriptorId["NCMAP007"][0].GetMessage();
        Assert.Contains("Extra", message);
    }

    // ==================== NCMAP009：显式重复双向断言 ====================

    /// <summary>
    /// 方向一（显式×显式）：两个显式点路径指向同一叶 → NCMAP009
    /// <para>冒烟 DotPath_ExplicitDuplicate_EmitsNCMAP009 已覆盖，此处保持矩阵完整性（正式化引用）。</para>
    /// </summary>
    [Fact]
    public void NCMAP009_ExplicitDuplicate_Reported()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtDiagEntity5 { public GtDiagAddress5 Address { get; set; } }
            public class GtDiagAddress5 { public string City { get; set; } }
            [MapFrom(typeof(GtDiagEntity5))]
            public class GtDiagDto5
            {
                [MapProperty(typeof(GtDiagEntity5), "Address.City")]
                public string City { get; set; }
                [MapProperty(typeof(GtDiagEntity5), "Address.City")]
                public string City2 { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP009"),
            "两个显式点路径指向同一叶应报 NCMAP009（PathOrigin=ExplicitDotPath 参与判据）");
    }

    /// <summary>
    /// 方向二（显式×推导混合）：显式点路径 Address.City + 推导扁平化 AddressCity → 推导豁免，不报 NCMAP009
    /// <para>补冒烟缺口：过渡期显式点路径 IsFlattened=true，仅靠 IsFlattened 无法区分推导/显式——混合场景锁定 PathOrigin 判据。</para>
    /// </summary>
    [Fact]
    public void NCMAP009_ExplicitPlusDerivedFlatten_Exempt()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtDiagEntity6 { public GtDiagAddress6 Address { get; set; } }
            public class GtDiagAddress6 { public string City { get; set; } }
            [MapFrom(typeof(GtDiagEntity6))]
            public class GtDiagDto6
            {
                [MapProperty(typeof(GtDiagEntity6), "Address.City")]
                public string City { get; set; }
                public string AddressCity { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP009"),
            "显式×推导混合指向同一叶应豁免（推导扁平化 PathOrigin=DerivedFlatten 不参与重复检测）");
    }

    // ==================== NCMAP013：反向写回后语义收窄 ====================

    /// <summary>
    /// 反向写回（显式点路径）：不再输出 NCMAP013，反向映射生成路径写回代码
    /// <para>DTO 含 Id（Direct 可反向）保证反向映射生成；点路径属性（Address.City）在反向中
    /// 写回实体嵌套路径（中间节点 null 创建），而非跳过。</para>
    /// </summary>
    [Fact]
    public void NCMAP013_ReverseWriteBack_ExplicitDotPath_NoDiagnostic()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtDiagEntity7 { public int Id { get; set; } public GtDiagAddress7 Address { get; set; } }
            public class GtDiagAddress7 { public string City { get; set; } }
            [MapFrom(typeof(GtDiagEntity7))]
            public class GtDiagDto7
            {
                public int Id { get; set; }
                [MapProperty(typeof(GtDiagEntity7), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP013"), "点路径属性反向不再跳过（已实现反向写回）");
        // 反向映射 Map_GtDiagDto7_To_GtDiagEntity7 生成写回：中间节点 null 创建 + 叶赋值
        result.Should().HaveMethodBodyContaining("Map_GtDiagDto7_To_GtDiagEntity7", "if (dst.Address == null) dst.Address = new ");
        result.Should().HaveMethodBodyContaining("Map_GtDiagDto7_To_GtDiagEntity7", "dst.Address.City = src.City");
    }

    /// <summary>
    /// 反向写回（推导扁平化）：不再输出 NCMAP013，与显式点路径同进退（P1 同契约）
    /// </summary>
    [Fact]
    public void NCMAP013_ReverseWriteBack_DerivedFlatten_NoDiagnostic()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtDiagEntity8 { public int Id { get; set; } public GtDiagAddress8 Address { get; set; } }
            public class GtDiagAddress8 { public string City { get; set; } }
            [MapFrom(typeof(GtDiagEntity8))]
            public class GtDiagDto8
            {
                public int Id { get; set; }
                public string AddressCity { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP013"), "推导扁平化属性反向不再跳过（已实现反向写回）");
        result.Should().HaveMethodBodyContaining("Map_GtDiagDto8_To_GtDiagEntity8", "if (dst.Address == null) dst.Address = new ");
        result.Should().HaveMethodBodyContaining("Map_GtDiagDto8_To_GtDiagEntity8", "dst.Address.City = src.AddressCity");
    }

    /// <summary>
    /// 反向写回中间节点不可实例化（接口）：NCMAP031 Error 编译阻断（B10，不生成非法代码）
    /// <para>接口段正向读路径合法（null 安全链），反向需 new 实例 → 不可实例化 → Error 诊断。</para>
    /// </summary>
    [Fact]
    public void NCMAP031_ReverseWriteBack_InterfaceIntermediate_ErrorDiagnostic()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtDiagEntity9 { public int Id { get; set; } public IGtDiagAddr9 Address { get; set; } }
            public interface IGtDiagAddr9 { string City { get; set; } }
            [MapFrom(typeof(GtDiagEntity9))]
            public class GtDiagDto9
            {
                public int Id { get; set; }
                [MapProperty(typeof(GtDiagEntity9), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP031"), "应输出 NCMAP031（中间节点接口不可实例化）");
        var message = result.DiagnosticsByDescriptorId["NCMAP031"][0].GetMessage();
        Assert.Contains("IGtDiagAddr9", message);
        // Error 级：不生成非法代码（反向方法中不含 Address.City 写回）
        var body = result.GetMethod("Map_GtDiagDto9_To_GtDiagEntity9")?.Body ?? "";
        Assert.DoesNotContain("new IGtDiagAddr9", body);
    }
}
