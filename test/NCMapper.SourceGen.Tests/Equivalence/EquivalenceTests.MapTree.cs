using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-05 nested type recursive mapping — both channels auto-match Child → Child and recurse
/// (scheme row A-05 adapted: [MapTree] attribute is tree-config not recursive-mapping;
///  per Q5 technical异议, A-05 verifies nested-type recursive mapping equivalence instead)
///  Q5: nested ContainingType chain TypeRef isomorphism (MapTree recursive model carries this)
/// </summary>
public class EquivalenceTestsMapTree : EquivalenceFixtureBase
{
    // Attribute channel: two [MapFrom] type pairs (parent + child) — auto-match Child + recurse
    private const string AttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A05
        {
            public class A05ChildSource { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class A05Source { public string Id { get; set; } = ""; public A05ChildSource Child { get; set; } = new(); }

            [MapFrom(typeof(A05Source))]
            public class A05TargetDto { public string Id { get; set; } = ""; public A05ChildTargetDto Child { get; set; } = new(); }

            [MapFrom(typeof(A05ChildSource))]
            public class A05ChildTargetDto { public string Name { get; set; } = ""; public int Age { get; set; } }
        }
        """;

    // Fluent channel: two .For<> pairs — auto-match Child + recurse
    private const string FluentSource = """
        using NCMapper;
        namespace EqPairs.A05
        {
            public class A05ChildSource { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class A05Source { public string Id { get; set; } = ""; public A05ChildSource Child { get; set; } = new(); }
            public class A05ChildTargetDto { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class A05TargetDto { public string Id { get; set; } = ""; public A05ChildTargetDto Child { get; set; } = new(); }

            [MapperConfig]
            public class A05Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A05Source, A05TargetDto>();
                    cfg.For<A05ChildSource, A05ChildTargetDto>();
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A05Source_To_A05TargetDto";

    [Fact]
    [EqPair("A-05")]
    [Trait("EqPair", "A-05")]
    public void A05_MapTree_NestedRecursive_AttributeMatchesFluent()
    {
        // L1-only: L2 text-level differs (attr=compile-time recursive call vs fluent=runtime fallback)
        // — registered as Q2 normalization rule in，L1 field-level equivalence is the core assertion
        AssertL1Only(AttrSource, FluentSource, "A-05");
    }

    [Fact]
    [EqPair("A-05")]
    [Trait("EqPair", "A-05")]
    public void A05_MapTree_Mixed_With_AutoMatch()
    {
        AssertL1Only(AttrSource, FluentSource, "A-05-mixed");
    }
}
