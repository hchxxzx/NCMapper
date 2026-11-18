using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// （「豁免门/方向回归」行——豁免门逐形态）：
/// 新面九终端豁免门（overrideIgnore 可选参数——双签名收拢为「单签名 + 可选参数」）
/// 逐形态承载：From / Mask / Format / Convert×4（T0/T1/T2/T3）/ ConvertBack×4（T0/T1/T2/T3）。
/// 每终端独立 (S,T) 对 + 独立编译片段（FluentExtractor.ExtractCore 为单 [MapperConfig] 类入口——
/// 多 Mapper 同源只提取首个，故逐对拆片编译；先例 FluentConvertBackTests 单 Mapper 形态）。
/// 断言口径 = 快照层 HasOverrideIgnore 置位（既有 Q2 先例 FluentConvertTests.Q2_* 口径）；
/// 缺省（false）不置位对照组锚定豁免门语义。方法组显式类型参按 D-3 固化规则
/// （Convert T1/T3 显式 / T0/T2 非泛型；ConvertBack T0 显式 TEntityProp、T1/T2/T3 返回位推断）。
/// </summary>
public class FluentMemberExemptionGateTests
{
    private const string Header = """
        using NCMapper;
        namespace Tc2Eg
        {
        """;

    private const string Footer = """
        }
        """;

    /// <summary>共享转换器组（各形态方法组——按对引用；ConvertBack 族与既有绿测同构：Entity/Dto 同型 string 锚）。</summary>
    private const string Conv = """
            public static class Conv
            {
                public static string T0() => "c0";
                public static string T1(string v) => "c1" + v;
                public static string T2(EgC2E s) => "c2";
                public static string T3(EgC3E s, string v) => "c3" + v;
                public static string B0() => "b0";
                public static string B1(string v) => "b1" + v;
                public static string B2(EgB2D s) => "b2";
                public static string B3(EgB3D s, string v) => "b3" + v;
            }
        """;

    /// <summary>逐终端片段（key = Dto 简单名；value = 该对类型 + MapperConfig 类体）。</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> Fragments = new()
    {
        ["EgFromD"] = """
            public class EgFromE { public string A { get; set; } = ""; }
            public class EgFromD { public string A { get; set; } = ""; }
            [MapperConfig]
            public static class EgFromM
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<EgFromE, EgFromD>().Member(d => d.A).From(s => s.A, overrideIgnore: true);
            }
        """,
        ["EgMaskD"] = """
            public class EgMaskE { public string Phone { get; set; } = ""; }
            public class EgMaskD { public string Phone { get; set; } = ""; }
            [MapperConfig]
            public static class EgMaskM
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<EgMaskE, EgMaskD>().Member(d => d.Phone).Mask<MaskPhone>(overrideIgnore: true);
            }
        """,
        ["EgFmtD"] = """
            public class EgFmtE { public System.DateTime Stamp { get; set; } = new(2024, 1, 1); }
            public class EgFmtD { public string Stamp { get; set; } = ""; }
            [MapperConfig]
            public static class EgFmtM
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<EgFmtE, EgFmtD>().Member(d => d.Stamp).From(s => s.Stamp).Format("yyyy-MM-dd", overrideIgnore: true);
            }
        """,
        ["EgC0D"] = """
            public class EgC0E { public string A { get; set; } = ""; }
            public class EgC0D { public string A { get; set; } = ""; }
            [MapperConfig]
            public static class EgC0M
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<EgC0E, EgC0D>().Member(d => d.A).Convert(Conv.T0, overrideIgnore: true);
            }
        """,
        ["EgC1D"] = """
            public class EgC1E { public string A { get; set; } = ""; }
            public class EgC1D { public string A { get; set; } = ""; }
            [MapperConfig]
            public static class EgC1M
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<EgC1E, EgC1D>().Member(d => d.A).Convert<string>(Conv.T1, overrideIgnore: true);
            }
        """,
        ["EgC2D"] = """
            public class EgC2E { public string A { get; set; } = ""; }
            public class EgC2D { public string A { get; set; } = ""; }
            [MapperConfig]
            public static class EgC2M
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<EgC2E, EgC2D>().Member(d => d.A).Convert(Conv.T2, overrideIgnore: true);
            }
        """,
        ["EgC3D"] = """
            public class EgC3E { public string A { get; set; } = ""; }
            public class EgC3D { public string A { get; set; } = ""; }
            [MapperConfig]
            public static class EgC3M
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<EgC3E, EgC3D>().Member(d => d.A).Convert<string>(Conv.T3, overrideIgnore: true);
            }
        """,
        ["EgB0D"] = """
            public class EgB0E { public string Tag { get; set; } = ""; }
            public class EgB0D { public string Tag { get; set; } = ""; }
            [MapperConfig]
            public static class EgB0M
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<EgB0E, EgB0D>().Member(d => d.Tag).ConvertBack<string>(Conv.B0, overrideIgnore: true);
            }
        """,
        ["EgB1D"] = """
            public class EgB1E { public string Tag { get; set; } = ""; }
            public class EgB1D { public string Tag { get; set; } = ""; }
            [MapperConfig]
            public static class EgB1M
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<EgB1E, EgB1D>().Member(d => d.Tag).ConvertBack(Conv.B1, overrideIgnore: true);
            }
        """,
        ["EgB2D"] = """
            public class EgB2E { public string Tag { get; set; } = ""; }
            public class EgB2D { public string Tag { get; set; } = ""; }
            [MapperConfig]
            public static class EgB2M
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<EgB2E, EgB2D>().Member(d => d.Tag).ConvertBack(Conv.B2, overrideIgnore: true);
            }
        """,
        ["EgB3D"] = """
            public class EgB3E { public string Tag { get; set; } = ""; }
            public class EgB3D { public string Tag { get; set; } = ""; }
            [MapperConfig]
            public static class EgB3M
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<EgB3E, EgB3D>().Member(d => d.Tag).ConvertBack(Conv.B3, overrideIgnore: true);
            }
        """,
        ["EgDefD"] = """
            public class EgDefE { public string B { get; set; } = ""; }
            public class EgDefD { public string B { get; set; } = ""; }
            [MapperConfig]
            public static class EgDefM
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<EgDefE, EgDefD>().Member(d => d.B).From(s => s.B);
            }
        """,
    };

    /// <summary>豁免门逐形态置位（九终端 overrideIgnore: true 全置位 + 缺省对照组不置位）。</summary>
    [Theory]
    [InlineData("EgFromD", "A", true)]
    [InlineData("EgMaskD", "Phone", true)]
    [InlineData("EgFmtD", "Stamp", true)]
    [InlineData("EgC0D", "A", true)]
    [InlineData("EgC1D", "A", true)]
    [InlineData("EgC2D", "A", true)]
    [InlineData("EgC3D", "A", true)]
    [InlineData("EgB0D", "Tag", true)]
    [InlineData("EgB1D", "Tag", true)]
    [InlineData("EgB2D", "Tag", true)]
    [InlineData("EgB3D", "Tag", true)]
    [InlineData("EgDefD", "B", false)]
    public void OverrideIgnore_PerTerminal_FlagMatrix(string dtoName, string prop, bool expected)
    {
        var body = Fragments[dtoName];
        // Convert 系片段需 Conv 组（同 namespace 共存——ExtractCore 单 Mapper 入口取该对）
        var needsConv = dtoName.StartsWith("EgC") || dtoName.StartsWith("EgB");
        var source = needsConv
            ? Header + "\n" + body + "\n" + Conv + "\n" + Footer
            : Header + "\n" + body + "\n" + Footer;

        var result = ExtractResult(source);
        Assert.NotNull(result);
        var decl = Assert.Single(result!.Declarations).Snapshot;
        var pm = PmOf(decl, prop);
        if (expected)
        {
            Assert.True(pm.Target.HasOverrideIgnore, $"{dtoName}.{prop}: overrideIgnore: true → HasOverrideIgnore 置位");
        }
        else
        {
            Assert.False(pm.Target.HasOverrideIgnore, $"{dtoName}.{prop}: 缺省 overrideIgnore 不置位（对照组）");
        }
    }

    /// <summary>直调 FluentExtractor.ExtractCore 取完整提取结果（先例 FluentConvertBackTests 形态）。</summary>
    private static FluentExtractionResult? ExtractResult(string source)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var typeDecl = compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("MapperConfig")))!;
        var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        return FluentExtractor.ExtractCore(compilation, typeSymbol!, typeDecl, CancellationToken.None);
    }

    private static PropertyMappingSnapshot PmOf(MappingSnapshot snapshot, string targetPropName)
        => snapshot.PropertyMappings.First(pm => pm.Target.Name == targetPropName);
}
