using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// （「组合矩阵（生成端）」行）：
/// 多操作叠加的放开组合与互斥真冲突——
/// ①放开 5 类组合各发射用例（显式来源 + 脱敏/格式化/JSON 展开/树递归/条件守卫——新增
///   「显式来源 + 变换」组合渲染路径的生成端承载）；
/// ②互斥真冲突 F59（清单②③④：脱敏×树递归 / 脱敏×JSON / 格式化×JSON——清单①
///   Ignore×映射 与⑤同维度重复分别由 FluentSilentSiteDiagnosticsTests 与
///   FluentMemberFoolProofTests 承载，⑥既有门 F23/F17/F66 由既有测试面承载）。
/// 断言口径 = 生成产物片段 + 诊断码/级别（先验锚保真——真冲突拒绝后其余声明照常产出）。
/// </summary>
public class FluentMemberCombinationTests
{
    // ═══════════════════ ① 放开组合（显式来源 + 变换，各发射） ═══════════════════

    /// <summary>组合一：From + Mask（异名显式来源 → 脱敏管线消费显式路径）。</summary>
    [Fact]
    public void Combo_FromPlusMask_EmitsMaskOverExplicitSource()
    {
        var source = """
            using NCMapper;
            namespace Tc2Cb1
            {
                public class E { public string Mobile { get; set; } = ""; }
                public class D { public string Phone { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Phone).From(s => s.Mobile).Mask<MaskPhone>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        Assert.Contains("src.Mobile", fwd.Body);
        // 方法体级断言 = __mask_ 字段 .Mask(...) 管线调用（MaskProvider.Resolve 在字段初始化区，先例 MaskGenerationTests 口径）
        Assert.Contains(".Mask(", fwd.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>组合二：From + Convert（显式来源 → 转换器消费显式路径）。</summary>
    [Fact]
    public void Combo_FromPlusConvert_EmitsConverterOverExplicitSource()
    {
        var source = """
            using NCMapper;
            namespace Tc2Cb2
            {
                public class E { public int Count { get; set; } }
                public class D { public string Count { get; set; } = ""; }
                public static class C { public static string Text(int v) => "#" + v; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Count).From(s => s.Count).Convert<int>(C.Text);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        Assert.Contains("C.Text", fwd.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>组合三：From + MapWhen（显式来源 → 条件守卫挂显式取值链）。</summary>
    [Fact]
    public void Combo_FromPlusMapWhen_EmitsGuardOverExplicitSource()
    {
        var source = """
            using NCMapper;
            namespace Tc2Cb3
            {
                public class E { public string Title { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Name).From(s => s.Title).MapWhen(v => v.Length > 0);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("Map_When_", result.GeneratedCode);
        Assert.Contains("src.Title", Assert.Single(result.Methods["Map_E_To_D"]).Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>组合四：From + Format（显式来源 → 格式化消费显式路径）。</summary>
    [Fact]
    public void Combo_FromPlusFormat_EmitsToStringOverExplicitSource()
    {
        var source = """
            using System;
            using NCMapper;
            namespace Tc2Cb4
            {
                public class E { public DateTime Stamp { get; set; } = new(2024, 1, 1); }
                public class D { public string Stamp { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Stamp).From(s => s.Stamp).Format("yyyy-MM-dd");
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        Assert.Contains(".ToString(\"yyyy-MM-dd\", null)", fwd.Body);
        Assert.Contains("src.Stamp", fwd.Body);
    }

    /// <summary>组合五：From + FromJson（显式来源 → JSON 展开消费显式路径）。</summary>
    [Fact]
    public void Combo_FromPlusFromJson_EmitsJsonOverExplicitSource()
    {
        var source = """
            using NCMapper;
            namespace Tc2Cb5
            {
                public class Pay { public int Id { get; set; } }
                public class E { public string Blob { get; set; } = ""; }
                public class D { public Pay Blob { get; set; } = new(); }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Blob).From(s => s.Blob).FromJson();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        Assert.Contains("src.Blob", fwd.Body);
        Assert.Contains("Json", fwd.Body);
    }

    // ═══════════════════ ② 互斥真冲突 F59（清单②③④）═══════════════════

    /// <summary>真冲突②：脱敏 × 树递归（跨段同 dest）→ F59 Error。</summary>
    [Fact]
    public void F59_MaskVersusMapTree_TrueConflict_Emitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Cb6
            {
                public class E { public string Name { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>()
                              .Member(d => d.Name).From(s => s.Name).Mask<MaskPhone>()
                              .Member(d => d.Name).MapTree();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f59 = result.Diagnostics.Where(d => d.Id == "NCMAP_F59").ToList();
        Assert.Single(f59);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f59[0].Severity);
        // culture 无关锚（先例 IncompatibleMemberSkipTests：GetMessage(InvariantCulture) 取 EN 资源——
        // 本机 zh-CN 环境下 GetMessage() 返回本地化文案，EN 文案锚必失配）
        Assert.Contains("true conflict", f59[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>真冲突③：脱敏 × JSON 展开（跨段同 dest）→ F59 Error。</summary>
    [Fact]
    public void F59_MaskVersusFromJson_TrueConflict_Emitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Cb7
            {
                public class Pay { public int Id { get; set; } }
                public class E { public string Blob { get; set; } = ""; }
                public class D { public Pay Blob { get; set; } = new(); }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>()
                              .Member(d => d.Blob).From(s => s.Blob).Mask<MaskPhone>()
                              .Member(d => d.Blob).FromJson();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f59 = result.Diagnostics.Where(d => d.Id == "NCMAP_F59").ToList();
        Assert.Single(f59);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f59[0].Severity);
    }

    /// <summary>真冲突④：格式化 × JSON 展开（跨段同 dest）→ F59 Error。</summary>
    [Fact]
    public void F59_FormatVersusFromJson_TrueConflict_Emitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Cb8
            {
                public class Pay { public int Id { get; set; } }
                public class E { public string Blob { get; set; } = ""; }
                public class D { public Pay Blob { get; set; } = new(); }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>()
                              .Member(d => d.Blob).From(s => s.Blob).Format("N")
                              .Member(d => d.Blob).FromJson();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f59 = result.Diagnostics.Where(d => d.Id == "NCMAP_F59").ToList();
        Assert.Single(f59);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f59[0].Severity);
    }
}
