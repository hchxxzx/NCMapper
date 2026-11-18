using NCMapper;
using NCMapper.SourceGen.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// Fluent 物化期 MapWhen 兜底段锁定。
/// <para>覆盖 <c>MappingSnapshotBuilder.cs</c> 的 :689-698（FieldValue 补门）与 :711-720（块内锚空）；
/// :675-684 / :723-732 两条 F44 兜底的可达性以探针实测裁决（机制推演判 ExtractCore 直调链不可达，
/// 见执行期台账）。</para>
/// <para>断言口径：物化期诊断在 <c>Declarations[].Snapshot.Diagnostics</c>（DescriptorId 口径，
/// 沿用 FluentMapWhenExtractionTests 先例）；Args[0] 含锚点名。</para>
/// </summary>
public class FluentMapWhenMaterializeGuardTests
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
        return FluentExtractor.ExtractCore(compilation, typeSymbol!, typeDecl, System.Threading.CancellationToken.None);
    }

    // =================================================================
    // 例 2（动作 2）：FieldValue 补门（689-698）——锚点 pm 无源属性
    // =================================================================

    [Fact]
    public void F43_FieldValueGate_ConvertWholeSourceAnchor()
    {
        var source = """
            using NCMapper;
            public class Mfg2Entity
            {
                public string Raw { get; set; } = "";
                public static string Transform(Mfg2Entity e) => e.Raw.ToUpperInvariant();
            }
            public class Mfg2Dto
            {
                public string Name { get; set; } = "";
            }

            [MapperConfig]
            public class Mfg2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<Mfg2Entity, Mfg2Dto>()
                       // 内联整源 lambda Convert（T2）——建 ForwardOnly pm 且 SourceProperty=null
                       //（方法组 Convert 不建 forwardDecl——实测 PM=[] 走自动匹配 NCMAAP001）
                       .Member(d => d.Name).Convert(e => "fixed")
                       // 内联 FieldValue 形态（参数≠源类型 → Shape=FieldValue）
                       // → :689 FieldValue 补门命中（锚点 pm 存在＋SourceProperty=null）→ :691-697 F43
                       .Member(d => d.Name).MapWhen((string v) => v == "x");
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        // 整源内联 Convert（T2）→ ForwardOnly pm 且 SourceProperty == null；内联 MapWhen FieldValue
        // 守卫无从求值 → :691-697 F43 补门（方法组 Convert 不建 forwardDecl——PM=[] 走自动匹配，实测）
        var f43 = result!.Declarations.First().Snapshot.Diagnostics
            .Where(d => d.DescriptorId == "NCMAP_F43").ToList();
        Assert.True(f43.Count == 1, $"[例2] F43 应恰 1 条，实际 {f43.Count}；全部诊断=[{string.Join(";", result.Declarations.First().Snapshot.Diagnostics.Select(d => d.DescriptorId))}]");
        Assert.Contains("Name", string.Join(" ", f43[0].Args));
    }

    // =================================================================
    // 例 3（动作 3）：块内锚空（711-720）——块内 MapWhen 无 (子源,属性) 绑定
    // =================================================================

    [Fact]
    public void F43_BlockWhenAnchorMissing_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class Mfg3Entity { public int Id { get; set; } }
            public class Mfg3Sub { public string Nick { get; set; } = ""; public string Tag { get; set; } = ""; }
            public class Mfg3Dto
            {
                public int Id { get; set; }
                public string Nick { get; set; } = "";
                public string Tag { get; set; } = "";
                public static bool HasContent(string v) => v.Length > 0;
            }

            [MapperConfig]
            public class Mfg3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<Mfg3Entity, Mfg3Dto>()
                       .FromSource<Mfg3Sub>(block =>
                       {
                           // Nick 有块内绑定（cm 产生）；Tag 过提取期 T1 校验（子源实有字段）但块内无绑定
                           // → Build :707 查 (Sub,Tag) 无 cm → :711 cm==null → :714 块内锚空 F43
                           block.Member(d => d.Nick).From(s => s.Nick);
                           block.Member(d => d.Tag).MapWhen(Mfg3Dto.HasContent);
                       })
                       .Member(d => d.Id).Map(e => e.Id);
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        // Tag 过 T1 校验进 BlockWhenDeclarations，但块内无 (Sub,Tag) 绑定 → cm 不产 → :714 F43
        var f43 = result!.Declarations.First().Snapshot.Diagnostics
            .Where(d => d.DescriptorId == "NCMAP_F43").ToList();
        Assert.True(f43.Count == 1, $"[例3] F43 应恰 1 条，实际 {f43.Count}；全部诊断=[{string.Join(";", result.Declarations.First().Snapshot.Diagnostics.Select(d => d.DescriptorId))}]");
        Assert.Contains("Tag", string.Join(" ", f43[0].Args));
    }
}
