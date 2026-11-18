using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// Mutation verification — three mutation types proving fixture + comparer are not silently broken.
/// scheme mutation verification (v1.2 finalized) + K5.
/// Three-state registration: predicted-fail / actual-fail / difference-attribution .
/// </summary>
public class EquivalenceMutationTests : EquivalenceFixtureBase
{
    // ----------------------------------------------------------------
    // Mutation 1: flip comparison field (MapProperty source property) => L1 MUST be RED
    // ----------------------------------------------------------------

    [Fact]
    [Trait("Mutation", "1-FlipComparisonField")]
    public void Mutation1_FlipMapPropertySource_L1MustBeRed()
    {
        // Attribute source A: [MapProperty(typeof(Source), "Name")] on Name (correct)
        const string attrSourceCorrect = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace EqPairs.Mut1
            {
                public class Source { public string Name { get; set; } = ""; public string Name2 { get; set; } = ""; }
                [MapFrom(typeof(Source))]
                public class TargetDto
                {
                    [MapProperty(typeof(Source), "Name")]
                    public string Name { get; set; } = "";
                    public string Name2 { get; set; } = "";
                }
            }
            """;

        // Attribute source B: [MapProperty(typeof(Source), "Name2")] on Name (FLIPPED — wrong source)
        const string attrSourceFlipped = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace EqPairs.Mut1
            {
                public class Source { public string Name { get; set; } = ""; public string Name2 { get; set; } = ""; }
                [MapFrom(typeof(Source))]
                public class TargetDto
                {
                    [MapProperty(typeof(Source), "Name2")]
                    public string Name { get; set; } = "";
                    public string Name2 { get; set; } = "";
                }
            }
            """;

        var snapshotCorrect = ExtractAttrSnapshotForMutation(attrSourceCorrect);
        var snapshotFlipped = ExtractAttrSnapshotForMutation(attrSourceFlipped);

        Assert.NotNull(snapshotCorrect);
        Assert.NotNull(snapshotFlipped);

        var result = SnapshotEquivalenceComparer.Compare(snapshotCorrect, snapshotFlipped);

        // Mutation 1: L1 MUST be RED (differences non-empty, containing Source path entry)
        Assert.False(result.AreEquivalent,
            "Mutation 1 FAILED: flipping MapProperty source should make L1 RED, but it passed (GREEN) — fixture/comparer may be silently broken");
        Assert.NotEmpty(result.Differences);
        // Differences should contain Source-related entries (the flipped property's Source path differs)
        // string list → EquivalenceDifference; legacy d.Contains matched the
        // field-path text embedded in the message — now matches FieldPath directly (same target text,
        // assert semantics unchanged — not a loosening).
        Assert.Contains(result.Differences, d => d.FieldPath.Contains("Source") || d.FieldPath.Contains("SourcePath") || d.FieldPath.Contains("PropertyMappings"));
    }

    // ----------------------------------------------------------------
    // Mutation 2: Fluent single-side .AfterMapping()
    // => L1 RED + L2 RED——AfterMappingMethodRef 迁出 denylist 参与比较，单侧声明在 L1 即可检出
    //（旧形态"L1 GREEN 豁免验证"随 迁移退役——迁移本身就是本变异检出能力的证明）
    // ----------------------------------------------------------------

    [Fact]
    [Trait("Mutation", "2-AfterMappingDetection")]
    public void Mutation2_AfterMapping_L1Red_L2Red_AfterB01Migration()
    {
        // Base A-01 sources (no AfterMapping)
        const string attrSource = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace EqPairs.Mut2
            {
                public class Source { public string Name { get; set; } = ""; public int Age { get; set; } }
                [MapFrom(typeof(Source))]
                public class TargetDto
                {
                    [MapProperty(typeof(Source), "Name")]
                    public string Name { get; set; } = "";
                    public int Age { get; set; }
                }
            }
            """;

        // Fluent source WITH .AfterMapping() (single-side — attr channel has no AfterMapping)
        const string fluentSourceWithAfterMapping = """
            using NCMapper;
            namespace EqPairs.Mut2
            {
                public class Source { public string Name { get; set; } = ""; public int Age { get; set; } }
                public class TargetDto { public string Name { get; set; } = ""; public int Age { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Source, TargetDto>()
                           .Member(d => d.Name).From(s => s.Name)
                           .AfterMapping(ApplyTag);
                    }
                    public static void ApplyTag(Source src, TargetDto dst) { dst.Name = "after:" + src.Name; }
                }
            }
            """;

            // L1: MUST be RED（迁移后 AfterMappingMethodRef 参与比较——单侧声明 L1 检出）
        var attrSnapshot = ExtractAttrSnapshotForMutation(attrSource);
        var fluentSnapshot = ExtractFluentSnapshotForMutation(fluentSourceWithAfterMapping);

        Assert.NotNull(attrSnapshot);
        Assert.NotNull(fluentSnapshot);

        var l1Result = SnapshotEquivalenceComparer.Compare(attrSnapshot, fluentSnapshot);
        Assert.False(l1Result.AreEquivalent,
            "Mutation 2 L1 FAILED: 迁移后单侧 AfterMapping 应在 L1 检出（AfterMappingMethodRef 参与比较），但 L1 为 GREEN:\n" +
            string.Join("\n", l1Result.Differences));
        Assert.Contains(l1Result.Differences, d => d.FieldPath == "AfterMappingMethodRef");

        // L2: MUST be RED (text-level sensitive — Fluent has AfterMapping call, attr doesn't)
        var attrGen = NCMapper.SourceGen.Tests.Helpers.TestHelper.GenerateMapper(attrSource);
        var fluentGen = NCMapper.SourceGen.Tests.Helpers.TestHelper.GenerateMapper(fluentSourceWithAfterMapping);

        var attrBody = GetMethodBodyForMutation(attrGen, "Map_Source_To_TargetDto");
        var fluentBody = GetMethodBodyForMutation(fluentGen, "Map_Source_To_TargetDto");

        Assert.True(attrBody != null, "Mutation 2: attr method not found");
        Assert.True(fluentBody != null, "Mutation 2: fluent method not found");
        Assert.NotEqual(attrBody, fluentBody);
    }

    // ----------------------------------------------------------------
    // Mutation 3: normalization projection (Location/FilePath differ but stripped => judge equivalent)
    // ----------------------------------------------------------------

    [Fact]
    [Trait("Mutation", "3-NormalizationProjection")]
    public void Mutation3_LocationStripped_JudgeEquivalent()
    {
        // Two identical attribute sources (same semantics) — built via separate BuildCompilation calls
        // their snapshots have different FilePath (two separate syntax trees) but Location is stripped
        const string attrSourceA = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace EqPairs.Mut3
            {
                public class Source { public string Name { get; set; } = ""; }
                [MapFrom(typeof(Source))]
                public class TargetDto { [MapProperty(typeof(Source), "Name")] public string Name { get; set; } = ""; }
            }
            """;

        const string attrSourceB = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace EqPairs.Mut3
            {
                public class Source { public string Name { get; set; } = ""; }
                [MapFrom(typeof(Source))]
                public class TargetDto { [MapProperty(typeof(Source), "Name")] public string Name { get; set; } = ""; }
            }
            """;

        var snapshotA = ExtractAttrSnapshotForMutation(attrSourceA);
        var snapshotB = ExtractAttrSnapshotForMutation(attrSourceB);

        Assert.NotNull(snapshotA);
        Assert.NotNull(snapshotB);

        var result = SnapshotEquivalenceComparer.Compare(snapshotA, snapshotB);

        // Mutation 3: MUST judge equivalent (Location stripped — FilePath/Span/LineSpan all stripped)
        Assert.True(result.AreEquivalent,
            "Mutation 3 FAILED: identical sources with different FilePath should be equivalent (Location stripped), but got differences:\n" +
            string.Join("\n", result.Differences));
    }

    // ----------------------------------------------------------------
    // Helpers (duplicated from FixtureBase for mutation-specific extraction)
    // ----------------------------------------------------------------

    internal static MappingSnapshot? ExtractAttrSnapshotForMutation(string source)
        => ExtractAttrChannelSnapshotInternal(source);

    internal static MappingSnapshot? ExtractFluentSnapshotForMutation(string source)
        => ExtractFluentChannelSnapshotInternal(source);

    internal static string? GetMethodBodyForMutation(NCMapper.SourceGen.Tests.Helpers.MapperGenerationResult result, string methodName)
    {
        if (result.Methods.TryGetValue(methodName, out var methods) && methods.Count > 0)
            return methods[0].Body;
        return null;
    }

    // These call the private FixtureBase methods via reflection-free internal wrappers
    // (FixtureBase methods are protected static; mutation tests inherit FixtureBase)
    private static MappingSnapshot? ExtractAttrChannelSnapshotInternal(string source)
    {
        var compilation = NCMapper.SourceGen.Tests.Helpers.TestHelper.BuildCompilation(source);
        var semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.First());

        var typeDecl = compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("MapFrom")));

        if (typeDecl == null) return null;
        var targetSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as Microsoft.CodeAnalysis.INamedTypeSymbol;
        if (targetSymbol == null) return null;

        var mapFromAttr = targetSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "NCMapper.Attributes.MapFromAttribute");
        if (mapFromAttr == null || mapFromAttr.ConstructorArguments.Length == 0) return null;

        var sourceType = mapFromAttr.ConstructorArguments[0].Value as Microsoft.CodeAnalysis.INamedTypeSymbol;
        if (sourceType == null) return null;

        return NCMapper.SourceGen.MappingExtractor.ExtractMappingCore(
            targetSymbol, sourceType, typeDecl, System.Threading.CancellationToken.None);
    }

    private static MappingSnapshot? ExtractFluentChannelSnapshotInternal(string source)
    {
        var compilation = NCMapper.SourceGen.Tests.Helpers.TestHelper.BuildCompilation(source);
        var typeDecl = compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("MapperConfig")));

        if (typeDecl == null) return null;
        var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as Microsoft.CodeAnalysis.INamedTypeSymbol;
        if (typeSymbol == null) return null;

        var result = NCMapper.SourceGen.FluentExtractor.ExtractCore(
            compilation, typeSymbol, typeDecl, System.Threading.CancellationToken.None);
        if (result == null || result.Declarations.Count == 0) return null;
        return result.Declarations[0].Snapshot;
    }
}
