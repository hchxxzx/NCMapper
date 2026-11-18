using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 块诊断断言：F29 三表（主链×Compose 同 dest）/ 四组合
///（同子源 F29 / 不同子源放行 / Map×Convert 互补）/ F30-F32（块内禁止方法）/ 块上下文误判回归。
/// 验证业务逻辑：①一个属性只能由一个源绑定供给——主链与 Compose 同 dest 矛盾 fail-fast；
/// ②同子源双绑定矛盾、不同子源顺序覆盖放行、Map×Convert 互补放行；
/// ③块内 ConvertBack/NoAutoInclude/AfterMapping 禁止（Error）；
/// ④块内合法方法（Map/Convert/Mask/Ignore/FromJson/MapTree）不误报。
/// </summary>
public class FluentComposeBlockDiagnosticsTests
{
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

    private static List<DiagnosticSnapshot> Diags(string source)
        => ExtractResult(source)?.Diagnostics.ToList() ?? new List<DiagnosticSnapshot>();

    /// <summary>
    /// 物化期诊断（F29 主链×Compose 同 dest 矛盾经 MappingSnapshot.Diagnostics 发射，
    /// 区别于提取期通道诊断 F30-F32——见 MappingSnapshotBuilder :112/:134/:147）。
    /// </summary>
    private static List<DiagnosticSnapshot> SnapshotDiags(string source)
        => ExtractResult(source)?.Declarations.FirstOrDefault()?.Snapshot.Diagnostics.ToList()
            ?? new List<DiagnosticSnapshot>();

    // ── F29 三表条件（_004）：主链三表 × Compose 同 dest ──────────────────────────────

    /// <summary>F29：主链 .Map × Compose 同 dest → Error（Declarations 表命中）。</summary>
    [Fact]
    public void F29_MainChainMap_X_Compose_SameDest_Error()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfdAEntity { public string Name { get; set; } = ""; }
            public class CfdASub { public string Name { get; set; } = ""; }
            public class CfdADto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class CfdAMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfdAEntity, CfdADto>()
                       .Member(d => d.Name).From(s => s.Name)
                       .Member(d => d.Name).FromSource<CfdASub>(s => s.Name);
                }
            }
            """;
        var diags = SnapshotDiags(source).Where(d => d.DescriptorId == "NCMAP_F29").ToList();
        Assert.Single(diags);
        Assert.Contains(diags[0].Args, a => a.Contains("Name"));
    }

    /// <summary>F29：主链 .Ignore × Compose 同 dest → Error（Declarations 表 Ignore 命中）。</summary>
    [Fact]
    public void F29_MainChainIgnore_X_Compose_SameDest_Error()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfdBEntity { public string Name { get; set; } = ""; }
            public class CfdBSub { public string Name { get; set; } = ""; }
            public class CfdBDto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class CfdBMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfdBEntity, CfdBDto>()
                       .Member(d => d.Name).Ignore()
                       .Member(d => d.Name).FromSource<CfdBSub>(s => s.Name);
                }
            }
            """;
        var diags = SnapshotDiags(source).Where(d => d.DescriptorId == "NCMAP_F29").ToList();
        Assert.Single(diags);
        Assert.Contains(diags[0].Args, a => a.Contains("Name"));
    }

    /// <summary>F29：主链 .Convert × Compose 同 dest → Error（ForwardDeclarations 表命中——三表之一）。</summary>
    [Fact]
    public void F29_MainChainConvert_X_Compose_SameDest_Error()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfdCEntity { public string Tag { get; set; } = ""; }
            public class CfdCSub { public string Tag { get; set; } = ""; }
            public class CfdCDto { public string Tag { get; set; } = ""; }
            public static class CfdCConv { public static string ToText(string v) => "v:" + v; }

            [MapperConfig]
            public class CfdCMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfdCEntity, CfdCDto>()
                       .Member(d => d.Tag).Convert<string>(CfdCConv.ToText)
                       .Member(d => d.Tag).FromSource<CfdCSub>(s => s.Tag);
                }
            }
            """;
        var diags = SnapshotDiags(source).Where(d => d.DescriptorId == "NCMAP_F29").ToList();
        Assert.Single(diags);
        Assert.Contains(diags[0].Args, a => a.Contains("Tag"));
    }

    /// <summary>F29：主链 .ConvertBack × Compose 同 dest → Error（BackDeclarations 表命中——三表之一）。</summary>
    [Fact]
    public void F29_MainChainConvertBack_X_Compose_SameDest_Error()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfdDEntity { public string Tag { get; set; } = ""; }
            public class CfdDSub { public string Tag { get; set; } = ""; }
            public class CfdDDto { public string Tag { get; set; } = ""; }
            public static class CfdDConv { public static string ToText(string v) => "b:" + v; }

            [MapperConfig]
            public class CfdDMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfdDEntity, CfdDDto>()
                       .Member(d => d.Tag).ConvertBack(CfdDConv.ToText)
                       .Member(d => d.Tag).FromSource<CfdDSub>(s => s.Tag);
                }
            }
            """;
        var diags = SnapshotDiags(source).Where(d => d.DescriptorId == "NCMAP_F29").ToList();
        Assert.Single(diags);
        Assert.Contains(diags[0].Args, a => a.Contains("Tag"));
    }

    // ── 四组合（_004）─────────────────────────────

    /// <summary>同子源双绑定：速记 × 块内 Map 同 dest → F29（同子源矛盾）。</summary>
    [Fact]
    public void SameSubSource_DoubleBinding_Error()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfdEEntity { public string Name { get; set; } = ""; }
            public class CfdESub { public string Name { get; set; } = ""; }
            public class CfdEDto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class CfdEMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfdEEntity, CfdEDto>()
                       .Member(d => d.Name).FromSource<CfdESub>(s => s.Name)
                       .FromSource<CfdESub>(c => { c.Member(d => d.Name).From(s => s.Name); });
                }
            }
            """;
        var diags = SnapshotDiags(source).Where(d => d.DescriptorId == "NCMAP_F29").ToList();
        Assert.Single(diags);
    }

    /// <summary>不同子源：Compose&lt;Sub1&gt; × Compose&lt;Sub2&gt; 同 dest → 放行（顺序覆盖，无 F29）。</summary>
    [Fact]
    public void DifferentSubSource_SameDest_Allowed()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfdFEntity { public string Name { get; set; } = ""; }
            public class CfdFSub1 { public string Name { get; set; } = ""; }
            public class CfdFSub2 { public string Name { get; set; } = ""; }
            public class CfdFDto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class CfdFMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfdFEntity, CfdFDto>()
                       .Member(d => d.Name).FromSource<CfdFSub1>(s => s.Name)
                       .Member(d => d.Name).FromSource<CfdFSub2>(s => s.Name);
                }
            }
            """;
        Assert.DoesNotContain(SnapshotDiags(source), d => d.DescriptorId == "NCMAP_F29");
        var snapshot = ExtractResult(source)!.Declarations[0].Snapshot;
        Assert.Equal(2, snapshot.ComposeMappings.Count);
    }

    /// <summary>块内 Map × Convert 互补：同子源 c.Map + c.Convert 同 dest → 放行（Q9 附着合并，无 F29）。</summary>
    [Fact]
    public void BlockMap_X_BlockConvert_SameDest_Allowed()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfdGEntity { public string Tag { get; set; } = ""; }
            public class CfdGSub { public string Tag { get; set; } = ""; }
            public class CfdGDto { public string Tag { get; set; } = ""; }
            public static class CfdGConv { public static string ToText(string v) => "v:" + v; }

            [MapperConfig]
            public class CfdGMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfdGEntity, CfdGDto>()
                       .FromSource<CfdGSub>(c =>
                       {
                           c.Member(d => d.Tag).From(s => s.Tag);
                           c.Member(d => d.Tag).Convert<string>(CfdGConv.ToText);
                       });
                }
            }
            """;
        Assert.DoesNotContain(SnapshotDiags(source), d => d.DescriptorId == "NCMAP_F29");
        var snapshot = ExtractResult(source)!.Declarations[0].Snapshot;
        Assert.Single(snapshot.ComposeMappings);
        // Map×Convert 互补：Converter 三字段注入（附着合并 Q9）
        Assert.Equal("ToText", snapshot.ComposeMappings[0].ConverterMethodName);
    }

    // ── F30-F32 块内禁止方法 ──────────────────────────────

    /// <summary>F30：块内 .ConvertBack → Error。</summary>
    [Fact]
    public void F30_BlockConvertBack_Error()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfdHEntity { public string Tag { get; set; } = ""; }
            public class CfdHSub { public string Tag { get; set; } = ""; }
            public class CfdHDto { public string Tag { get; set; } = ""; }
            public static class CfdHConv { public static string ToText(string v) => "b:" + v; }

            [MapperConfig]
            public class CfdHMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfdHEntity, CfdHDto>()
                       .FromSource<CfdHSub>(c => { c.Member(d => d.Tag).ConvertBack(CfdHConv.ToText); });
                }
            }
            """;
        var diags = Diags(source).Where(d => d.DescriptorId == "NCMAP_F30").ToList();
        Assert.Single(diags);
    }

    /// <summary>F31：块内 .NoAutoInclude → Error。</summary>
    [Fact]
    public void F31_BlockNoAutoInclude_Error()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfdIEntity { public string Tag { get; set; } = ""; }
            public class CfdISub { public string Tag { get; set; } = ""; }
            public class CfdIDto { public string Tag { get; set; } = ""; }

            [MapperConfig]
            public class CfdIMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfdIEntity, CfdIDto>()
                       .FromSource<CfdISub>(c => { c.NoAutoMatch(); });
                }
            }
            """;
        var diags = Diags(source).Where(d => d.DescriptorId == "NCMAP_F31").ToList();
        Assert.Single(diags);
    }

    /// <summary>F32：块内 .AfterMapping → Error。</summary>
    [Fact]
    public void F32_BlockAfterMapping_Error()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfdJEntity { public string Tag { get; set; } = ""; }
            public class CfdJSub { public string Tag { get; set; } = ""; }
            public class CfdJDto { public string Tag { get; set; } = ""; }

            [MapperConfig]
            public class CfdJMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfdJEntity, CfdJDto>()
                       .FromSource<CfdJSub>(c => { c.AfterMapping((s, d) => { }); });
                }
            }
            """;
        var diags = Diags(source).Where(d => d.DescriptorId == "NCMAP_F32").ToList();
        Assert.Single(diags);
    }

    // ── 块上下文误判回归（合法块内方法不误报）────────────────

    /// <summary>块内合法方法集（Map/Convert/Ignore/MapTree/FromJson/Mask）→ 无 F30-F32 误报。</summary>
    [Fact]
    public void Block_LegalMethods_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfdKEntity { public string Name { get; set; } = ""; public int Score { get; set; } }
            public class CfdKSub { public string Name { get; set; } = ""; public int Score { get; set; } }
            public class CfdKDto { public string Name { get; set; } = ""; public int Score { get; set; } }

            [MapperConfig]
            public class CfdKMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfdKEntity, CfdKDto>()
                       .FromSource<CfdKSub>(c =>
                       {
                           c.Member(d => d.Name).From(s => s.Name);
                           c.Member(d => d.Score).MapTree();
                           c.Member(d => d.Score).Ignore();
                       });
                }
            }
            """;
        Assert.DoesNotContain(Diags(source), d => d.DescriptorId == "NCMAP_F30" || d.DescriptorId == "NCMAP_F31" || d.DescriptorId == "NCMAP_F32");
    }

    /// <summary>块上下文误判回归：块内合法 Map + 块内合法 Ignore 不互相误报（F29 仅在物化期主链冲突时发）。</summary>
    [Fact]
    public void Block_MapAndIgnore_NoConflict()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfdLEntity { public string A { get; set; } = ""; public string B { get; set; } = ""; }
            public class CfdLSub { public string A { get; set; } = ""; public string B { get; set; } = ""; }
            public class CfdLDto { public string A { get; set; } = ""; public string B { get; set; } = ""; }

            [MapperConfig]
            public class CfdLMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfdLEntity, CfdLDto>()
                       .FromSource<CfdLSub>(c =>
                       {
                           c.Member(d => d.A).From(s => s.A);
                           c.Member(d => d.B).Ignore();
                       });
                }
            }
            """;
        var result = ExtractResult(source);
        var snapshot = result!.Declarations[0].Snapshot;
        // A 入 ComposeMappings（Map）；B 全 Ignore 组不产 cm
        Assert.Single(snapshot.ComposeMappings);
        Assert.Equal("A", snapshot.ComposeMappings[0].Target.Name);
        // 不误报 F30-F32（提取期通道诊断）
        Assert.DoesNotContain(result.Diagnostics, d =>
            d.DescriptorId == "NCMAP_F30"
            || d.DescriptorId == "NCMAP_F31" || d.DescriptorId == "NCMAP_F32");
        // 不误报 F29（物化期诊断）
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.DescriptorId == "NCMAP_F29");
    }
}
