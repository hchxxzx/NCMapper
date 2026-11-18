using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// [Mask] 脱敏源生成器集成测试 — 验证端到端生成代码的正确性
/// <para>覆盖场景：基本脱敏、无脱敏、ConvertUsing+Mask 共存、反向映射无脱敏、多属性多脱敏器、自定义 IMask</para>
/// </summary>
public class MaskGenerationTests
{
    /// <summary>
    /// 场景1：基本 [Mask] — 生成 static readonly 单例字段 + Mask() 包装赋值
    /// </summary>
    [Fact]
    public void BasicMask_GeneratesFieldAndWrappedAssignment()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MaskEntity { public int Id { get; set; } public string Phone { get; set; } }
            [MapFrom(typeof(MaskEntity))]
            public class MaskDto
            {
                public int Id { get; set; }
                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 验证静态单例字段生成（通过 MaskProvider 解析）
        Assert.Contains("private static readonly global::NCMapper.IMask __mask_MaskDto_Phone = global::NCMapper.MaskProvider.Resolve<NCMapper.MaskPhone>();", result.GeneratedCode);

        // 验证赋值包装
        result.Should().HaveMethodBodyContaining("Map_MaskEntity_To_MaskDto",
            "Phone = __mask_MaskDto_Phone.Mask(src.Phone)");
    }

    /// <summary>
    /// 场景2：无 [Mask] — 不生成任何 __mask_ 字段
    /// </summary>
    [Fact]
    public void NoMask_DoesNotGenerateMaskFields()
    {
        var source = """
            using NCMapper.Attributes;
            public class NoMaskEntity { public int Id { get; set; } public string Name { get; set; } }
            [MapFrom(typeof(NoMaskEntity))]
            public class NoMaskDto { public int Id { get; set; } public string Name { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.DoesNotContain("__mask_", result.GeneratedCode);
    }

    /// <summary>
    /// 场景3：[Convert] + [Mask] 共存 — 先转换再脱敏
    /// </summary>
    [Fact]
    public void ConvertUsingPlusMask_WrapsConverterResultWithMask()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class CmEntity { public int Id { get; set; } public int Phone { get; set; } }
            public static class PhoneConverter
            {
                public static string Convert(int fieldValue) => fieldValue.ToString();
            }
            [MapFrom(typeof(CmEntity))]
            public class CmDto
            {
                public int Id { get; set; }
                [Convert(typeof(PhoneConverter), nameof(PhoneConverter.Convert))]
                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 验证静态转换调用与 mask 字段都存在（转换器内联静态调用，无 __conv 字段）
        Assert.Contains("global::PhoneConverter.Convert(src.Phone)", result.GeneratedCode);
        Assert.Contains("__mask_CmDto_Phone", result.GeneratedCode);

        // 验证赋值：先 Convert 再 Mask
        result.Should().HaveMethodBodyContaining("Map_CmEntity_To_CmDto",
            "Phone = __mask_CmDto_Phone.Mask(global::PhoneConverter.Convert(src.Phone))");
    }

    /// <summary>
    /// 场景4：反向映射无脱敏 — 反向方法体不包含 .Mask( 调用
    /// </summary>
    [Fact]
    public void ReverseMapping_DoesNotApplyMask()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class RevEntity { public int Id { get; set; } public string Phone { get; set; } }
            [MapFrom(typeof(RevEntity))]
            public class RevDto
            {
                public int Id { get; set; }
                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 正向有 Mask
        result.Should().HaveMethodBodyContaining("Map_RevEntity_To_RevDto",
            "__mask_RevDto_Phone.Mask(src.Phone)");

        // 反向无 Mask — 直接赋值
        result.Should().HaveMethodBodyContaining("Map_RevDto_To_RevEntity",
            "Phone = src.Phone");

        // 反向方法体不包含 .Mask(
        var reverseMethod = result.GetMethod("Map_RevDto_To_RevEntity");
        Assert.NotNull(reverseMethod);
        Assert.DoesNotContain(".Mask(", reverseMethod!.Body);
    }

    /// <summary>
    /// 场景5：多个属性各自不同 Mask — 每个属性独立字段
    /// </summary>
    [Fact]
    public void MultipleProperties_EachGetsOwnMaskField()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MultiEntity { public string Phone { get; set; } public string Email { get; set; } public string Name { get; set; } }
            [MapFrom(typeof(MultiEntity))]
            public class MultiDto
            {
                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; }
                [Mask(typeof(MaskEmail))]
                public string Email { get; set; }
                [Mask(typeof(MaskChineseName))]
                public string Name { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 三个独立字段（通过 MaskProvider 解析）
        Assert.Contains("private static readonly global::NCMapper.IMask __mask_MultiDto_Phone = global::NCMapper.MaskProvider.Resolve<NCMapper.MaskPhone>();", result.GeneratedCode);
        Assert.Contains("private static readonly global::NCMapper.IMask __mask_MultiDto_Email = global::NCMapper.MaskProvider.Resolve<NCMapper.MaskEmail>();", result.GeneratedCode);
        Assert.Contains("private static readonly global::NCMapper.IMask __mask_MultiDto_Name = global::NCMapper.MaskProvider.Resolve<NCMapper.MaskChineseName>();", result.GeneratedCode);

        // 各自包装
        result.Should().HaveMethodBodyContaining("Map_MultiEntity_To_MultiDto",
            "Phone = __mask_MultiDto_Phone.Mask(src.Phone)");
        result.Should().HaveMethodBodyContaining("Map_MultiEntity_To_MultiDto",
            "Email = __mask_MultiDto_Email.Mask(src.Email)");
        result.Should().HaveMethodBodyContaining("Map_MultiEntity_To_MultiDto",
            "Name = __mask_MultiDto_Name.Mask(src.Name)");
    }

    /// <summary>
    /// 场景6：自定义 IMask 实现（非内置）— 同样生成字段和包装
    /// </summary>
    [Fact]
    public void CustomMask_GeneratesFieldAndWrappedAssignment()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MyCustomMask : IMask
            {
                public string Mask(string? value) => value == null ? "" : "***";
            }
            public class CustomEntity { public string Secret { get; set; } }
            [MapFrom(typeof(CustomEntity))]
            public class CustomDto
            {
                [Mask(typeof(MyCustomMask))]
                public string Secret { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 自定义 Mask 类型字段（通过 MaskProvider 解析）
        Assert.Contains("private static readonly global::NCMapper.IMask __mask_CustomDto_Secret = global::NCMapper.MaskProvider.Resolve<MyCustomMask>();", result.GeneratedCode);

        // 赋值包装
        result.Should().HaveMethodBodyContaining("Map_CustomEntity_To_CustomDto",
            "Secret = __mask_CustomDto_Secret.Mask(src.Secret)");
    }

    /// <summary>
    /// 场景7：NCMAP014 诊断 — [Mask] 指定类型未实现 IMask 接口时报错
    /// </summary>
    [Fact]
    public void InvalidMaskType_EmitsNCMAP014Diagnostic()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class NotAMask { public string Mask(string? v) => v ?? ""; }
            public class DiagEntity { public string Phone { get; set; } }
            [MapFrom(typeof(DiagEntity))]
            public class DiagDto
            {
                [Mask(typeof(NotAMask))]
                public string Phone { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 应产生 NCMAP014 诊断
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP014"),
            $"期望 NCMAP014 诊断，实际诊断: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");

        // 不应生成 __mask_ 字段（验证失败时不设置 MaskType）
        Assert.DoesNotContain("__mask_", result.GeneratedCode);
    }

    /// <summary>
    /// 场景8：ConvertUsing 无源属性 + [Mask] 共存 — Mask 不被静默丢弃（#1 修复验证）
    /// </summary>
    [Fact]
    public void ConvertUsingNoSource_MaskNotSilentlyDropped()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class NsEntity { public int Id { get; set; } }
            public static class FullNameConverter
            {
                public static string Convert(int fieldValue) => "User_" + fieldValue;
            }
            [MapFrom(typeof(NsEntity))]
            public class NsDto
            {
                public int Id { get; set; }
                [Convert(typeof(FullNameConverter), nameof(FullNameConverter.Convert))]
                [Mask(typeof(MaskChineseName))]
                public string FullName { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // Mask 字段应生成（不被静默丢弃）
        Assert.Contains("__mask_NsDto_FullName", result.GeneratedCode);

        // 赋值应为 Mask(Convert(...)) 包装（R6-b 无匹配源属性 → 字段实参 default 字面量）
        result.Should().HaveMethodBodyContaining("Map_NsEntity_To_NsDto",
            "FullName = __mask_NsDto_FullName.Mask(global::FullNameConverter.Convert(default))");
    }

    /// <summary>
    /// 场景9：NCMAP015 诊断 — [Mask] 标注非 string 属性时报错（#2 修复验证）
    /// </summary>
    [Fact]
    public void MaskOnNonStringProperty_EmitsNCMAP015Diagnostic()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class NsEntity2 { public int Age { get; set; } }
            [MapFrom(typeof(NsEntity2))]
            public class NsDto2
            {
                [Mask(typeof(MaskPhone))]
                public int Age { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 应产生 NCMAP015 诊断
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP015"),
            $"期望 NCMAP015 诊断，实际诊断: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");

        // 不应生成 __mask_ 字段
        Assert.DoesNotContain("__mask_", result.GeneratedCode);
    }

    /// <summary>
    /// 邻界反例：合法 [Mask] 形态（类型实现 IMask 且目标为 string）→ 不报 NCMAP014/NCMAP015
    /// </summary>
    [Fact]
    public void ValidMaskTypeAndStringTarget_NoMaskDiagnostics()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MaskOkEntity { public string Phone { get; set; } = ""; }
            [MapFrom(typeof(MaskOkEntity))]
            public class MaskOkDto
            {
                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP014"), "Mask 类型实现 IMask，不应报 NCMAP014");
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP015"), "Mask 目标为 string，不应报 NCMAP015");
    }

    /// <summary>
    /// 场景10：同 DTO 两个属性用同一种 Converter — 各自独立消费（#4 附带修复验证）
    /// <para> 起不再生成 __conv 单例字段，"独立消费"语义由每个属性各自的
    /// 内联静态调用（global::FQN.M(src.X)）承载。</para>
    /// </summary>
    [Fact]
    public void SameConverterType_TwoProperties_BothFieldsGenerated()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class DupEntity { public int Phone { get; set; } public int Fax { get; set; } }
            public static class IntToStringConverter
            {
                public static string Convert(int fieldValue) => fieldValue.ToString();
            }
            [MapFrom(typeof(DupEntity))]
            public class DupDto
            {
                [Convert(typeof(IntToStringConverter), nameof(IntToStringConverter.Convert))]
                public string Phone { get; set; }
                [Convert(typeof(IntToStringConverter), nameof(IntToStringConverter.Convert))]
                public string Fax { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 不再生成 __conv 单例字段；同类型转换器两属性各自内联静态调用互不干扰
        Assert.DoesNotContain("__conv_", result.GeneratedCode);

        // 各自引用正确（每个属性的静态调用绑定各自的源字段访问）
        result.Should().HaveMethodBodyContaining("Map_DupEntity_To_DupDto",
            "Phone = global::IntToStringConverter.Convert(src.Phone)");
        result.Should().HaveMethodBodyContaining("Map_DupEntity_To_DupDto",
            "Fax = global::IntToStringConverter.Convert(src.Fax)");
    }

    // ================================================================
    // Compose 副源字段 [Mask]/[IsFromJson] 发射形态
    // ================================================================

    /// <summary>
    /// 场景11：Compose 副源字段 [Mask] — 生成静态单例字段 + Compose 部分更新方法 mask 包装
    /// </summary>
    [Fact]
    public void ComposeSubSourceMask_GeneratesFieldAndWrappedAssignment()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class CsmGenPrimary { public int Id { get; set; } }
            public class CsmGenSub { public string Phone { get; set; } = ""; }
            [MapFrom(typeof(CsmGenPrimary))]
            public class CsmGenDto
            {
                public int Id { get; set; }
                [MapProperty(typeof(CsmGenSub), nameof(CsmGenSub.Phone))]
                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 静态单例字段生成（GenerateMaskFields 扩展遍历 ComposeMappings）
        Assert.Contains("private static readonly global::NCMapper.IMask __mask_CsmGenDto_Phone = global::NCMapper.MaskProvider.Resolve<NCMapper.MaskPhone>();", result.GeneratedCode);

        // Compose 部分更新方法 mask 包装（四段序分支②：直包，与主源/运行时一致）
        result.Should().HaveMethodBodyContaining("Update_CsmGenSub_To_CsmGenDto",
            "Phone = __mask_CsmGenDto_Phone.Mask(src.Phone)");
    }

    /// <summary>
    /// 场景12：Compose 副源字段 [Convert]+[Mask] 共存 — 先转换后脱敏（四段序分支①）
    /// </summary>
    [Fact]
    public void ComposeSubSourceConvertPlusMask_WrapsConverterResultWithMask()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class CsmCmPrimary { public int Id { get; set; } }
            public class CsmCmSub { public string Phone { get; set; } = ""; }
            public static class CsmCmConverter
            {
                public static string Extract(string fieldValue) => fieldValue.Trim();
            }
            [MapFrom(typeof(CsmCmPrimary))]
            public class CsmCmDto
            {
                public int Id { get; set; }
                [MapProperty(typeof(CsmCmSub), nameof(CsmCmSub.Phone))]
                [Convert(typeof(CsmCmConverter), nameof(CsmCmConverter.Extract))]
                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // mask 字段生成
        Assert.Contains("__mask_CsmCmDto_Phone", result.GeneratedCode);

        // 先转换再脱敏（converter+mask 形：直包无 N-1 守卫——D2 裁定，镜像主源/运行时）
        result.Should().HaveMethodBodyContaining("Update_CsmCmSub_To_CsmCmDto",
            "Phone = __mask_CsmCmDto_Phone.Mask(global::CsmCmConverter.Extract(src.Phone))");
    }

    /// <summary>
    /// 场景13：Compose 副源字段 [MapProperty(IsFromJson=true)] — JSON 反序列化发射（四段序分支③）
    /// </summary>
    [Fact]
    public void ComposeSubSourceIsFromJson_GeneratesJsonConversion()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class CsmJsonPrimary { public int Id { get; set; } }
            public class CsmJsonSub { public string Payload { get; set; } = ""; }
            public class CsmJsonDetail { public string Value { get; set; } = ""; }
            [MapFrom(typeof(CsmJsonPrimary))]
            public class CsmJsonDto
            {
                public int Id { get; set; }
                [MapProperty(typeof(CsmJsonSub), nameof(CsmJsonSub.Payload), IsFromJson = true)]
                public CsmJsonDetail Payload { get; set; } = new();
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // string → 对象：反序列化（镜像主源 :1120）
        result.Should().HaveMethodBodyContaining("Update_CsmJsonSub_To_CsmJsonDto",
            "Payload = global::NCMapper.JsonConverter.JsonConvertToObject<CsmJsonDetail>(src.Payload)");
    }

    // ================================================================
    // B1-01 掩码字段引用 methodSuffix 断链（审计批次二）
    // 声明侧 GenerateMaskFields 用真后缀（GetSuffix），引用侧三站点未传 methodSuffix
    // → 同名类冲突（_2）场景引用 __mask_X_Prop vs 声明 __mask_X_2_Prop = CS0103。
    // 站点①② = TryBuildSimpleAssignments 调用点（简单赋值路径）；
    // 站点③ = GeneratePropertyAssignments 调用点（ConvertUsing 致简单路径早退后的标准路径）。
    // ================================================================

    /// <summary>
    /// B1-01 A 组：简单赋值路径 + 同名类后缀冲突 + [Mask] — 引用/声明名一致（站点①②）。
    /// <para>NsA/NsB 同名对按 FullName 字典序：NsA 无后缀、NsB 拿 "_2"（方法名
    /// Map_MsfEntity_2_To_MsfDto_2，后缀同时加在源/目标类名——TG-P-2 先例）。缺陷态引用侧
    /// methodSuffix 断链拼 "" → 冲突对引用 __mask_MsfDto_Phone vs 声明 __mask_MsfDto_2_Phone = CS0103。</para>
    /// </summary>
    [Fact]
    public void MaskFieldConflict_SimpleAssignPath_SuffixConsistent()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace NsA
            {
                public class MsfEntity { public string Phone { get; set; } = ""; }
                [MapFrom(typeof(MsfEntity))]
                public class MsfDto
                {
                    [Mask(typeof(MaskPhone))]
                    public string Phone { get; set; } = "";
                }
            }
            namespace NsB
            {
                public class MsfEntity { public string Phone { get; set; } = ""; }
                [MapFrom(typeof(MsfEntity))]
                public class MsfDto
                {
                    [Mask(typeof(MaskPhone))]
                    public string Phone { get; set; } = "";
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 声明侧：两对各自后缀的掩码字段（NsB 冲突对带 _2——GenerateMaskFields 真后缀，缺陷无关面）
        Assert.Contains("private static readonly global::NCMapper.IMask __mask_MsfDto_Phone = global::NCMapper.MaskProvider.Resolve<NCMapper.MaskPhone>();", result.GeneratedCode);
        Assert.Contains("private static readonly global::NCMapper.IMask __mask_MsfDto_2_Phone = global::NCMapper.MaskProvider.Resolve<NCMapper.MaskPhone>();", result.GeneratedCode);

        // 引用侧：冲突对方法体引用带后缀字段名（缺陷态拼 __mask_MsfDto_Phone → 红）
        result.Should().HaveMethodBodyContaining("Map_MsfEntity_2_To_MsfDto_2",
            "__mask_MsfDto_2_Phone.Mask(src.Phone)");

        // 生成码编译零错误（缺陷态 CS0103）
        Assert.DoesNotContain("CS0103", EmitErrorIds(source));
    }

    /// <summary>
    /// B1-01 B 组：非简单赋值路径（[Convert] 致简单路径 :182 早退）+ 后缀冲突 + [Mask] —
    /// 站点③（GeneratePropertyAssignments 调用点）后缀一致。
    /// </summary>
    [Fact]
    public void MaskFieldConflict_NonSimpleAssignPath_SuffixConsistent()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public static class MsfConv
            {
                public static string Conv(string fieldValue) => fieldValue;
            }
            namespace NsA
            {
                public class MsfEntity { public string Phone { get; set; } = ""; }
                [MapFrom(typeof(MsfEntity))]
                public class MsfDto { public string Phone { get; set; } = ""; }
            }
            namespace NsB
            {
                public class MsfEntity { public string Phone { get; set; } = ""; }
                [MapFrom(typeof(MsfEntity))]
                public class MsfDto
                {
                    [Convert(typeof(MsfConv), "Conv")]
                    [Mask(typeof(MaskPhone))]
                    public string Phone { get; set; } = "";
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 声明侧冲突对字段带 _2
        Assert.Contains("private static readonly global::NCMapper.IMask __mask_MsfDto_2_Phone", result.GeneratedCode);

        // 引用侧：ConvertUsing+Mask 包装（标准路径 :1193-1197）引用带后缀字段名（缺陷态红）
        result.Should().HaveMethodBodyContaining("Map_MsfEntity_2_To_MsfDto_2",
            "__mask_MsfDto_2_Phone.Mask(global::MsfConv.Conv(src.Phone))");

        // 生成码编译零错误（缺陷态 CS0103）
        Assert.DoesNotContain("CS0103", EmitErrorIds(source));
    }

    /// <summary>Emit 编译错误 ID 提取（RunGeneratorsAndUpdateCompilation 含生成码的 output 诊断——红绿对照 CS0103 用）。</summary>
    private static List<string> EmitErrorIds(string source)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(new NCMapperSourceGenerator().AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return output.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.Id)
            .ToList();
    }
}
