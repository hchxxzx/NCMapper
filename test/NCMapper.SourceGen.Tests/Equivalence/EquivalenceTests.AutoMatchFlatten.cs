using NCMapper;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-06 AutoMatch+Flatten — both channels default behavior (no annotation / no declaration)
/// scheme row A-06; assert: IsFlattened=true + PathOrigin=DerivedFlatten
/// (attribute :437 vs fluent :223 — both use same flatten path-origin logic)
/// </summary>
public class EquivalenceTestsAutoMatchFlatten : EquivalenceFixtureBase
{
    // Both channels: no property-level annotations, no .Member(dest).From(src) declarations
    // Auto-match + auto-flatten: Inner.City → InnerCity
    private const string AttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A06
        {
            public class A06InnerSource { public string City { get; set; } = ""; }
            public class A06Source { public string Street { get; set; } = ""; public A06InnerSource Inner { get; set; } = new(); }

            [MapFrom(typeof(A06Source))]
            public class A06TargetDto
            {
                public string Street { get; set; } = "";
                public string InnerCity { get; set; } = "";
            }
        }
        """;

    private const string FluentSource = """
        using NCMapper;
        namespace EqPairs.A06
        {
            public class A06InnerSource { public string City { get; set; } = ""; }
            public class A06Source { public string Street { get; set; } = ""; public A06InnerSource Inner { get; set; } = new(); }
            public class A06TargetDto { public string Street { get; set; } = ""; public string InnerCity { get; set; } = ""; }

            [MapperConfig]
            public class A06Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    // Street explicit (produces declaration); InnerCity auto-flattened (Inner.City → InnerCity)
                    cfg.For<A06Source, A06TargetDto>()
                       .Member(d => d.Street).From(s => s.Street);
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A06Source_To_A06TargetDto";

    [Fact]
    [EqPair("A-06")]
    [Trait("EqPair", "A-06")]
    public void A06_AutoMatchFlatten_AttributeMatchesFluent()
    {
        AssertEquivalent(AttrSource, FluentSource, "A-06", MapMethodName);
    }

    [Fact]
    [EqPair("A-06")]
    [Trait("EqPair", "A-06")]
    public void A06_AutoMatchFlatten_Mixed_Boundary()
    {
        // Same as main — Street auto-matched, InnerCity auto-flattened
        AssertEquivalent(AttrSource, FluentSource, "A-06-mixed", MapMethodName);
    }
}
