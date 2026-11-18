using NCMapper;
using NCMapper.Attributes;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-01 Map — Attribute [MapProperty(typeof(Source), "Name")] vs Fluent .Member(d => d.Name).From(s => s.Name)
/// scheme row A-01; Q1 PathOrigin first verification + Q2 L2 text first verification
/// </summary>
public class EquivalenceTestsMap : EquivalenceFixtureBase
{
    // Attribute channel source: [MapFrom] DTO with [MapProperty] on Name
    private const string AttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A01
        {
            public class A01Source { public string Name { get; set; } = ""; public int Age { get; set; } }

            [MapFrom(typeof(A01Source))]
            public class A01TargetDto
            {
                [MapProperty(typeof(A01Source), "Name")]
                public string Name { get; set; } = "";
                public int Age { get; set; }
            }
        }
        """;

    // Fluent channel source: clean S/T classes + [MapperConfig] with .Member(dest).From(src)
    private const string FluentSource = """
        using NCMapper;
        namespace EqPairs.A01
        {
            public class A01Source { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class A01TargetDto { public string Name { get; set; } = ""; public int Age { get; set; } }

            [MapperConfig]
            public class A01Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A01Source, A01TargetDto>()
                       .Member(d => d.Name).From(s => s.Name);
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A01Source_To_A01TargetDto";

    [Fact]
    [EqPair("A-01")]
    [Trait("EqPair", "A-01")]
    public void A01_Map_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(AttrSource, FluentSource, "A-01", MapMethodName);
    }

    /// <summary>
    /// Mixed boundary scenario (scheme): [MapProperty] explicit + auto-match coexist
    /// — Name explicitly mapped, Age auto-matched; verify decision-product overlay isomorphic
    /// </summary>
    [Fact]
    [EqPair("A-01")]
    [Trait("EqPair", "A-01")]
    public void A01_Map_Mixed_Explicit_Plus_AutoMatch()
    {
        // Attribute: only Name has [MapProperty]; Age relies on auto-match
        // Fluent: only .Member(d => d.Name).From(s => s.Name); Age relies on auto-match
        // Same as main A-01 source — auto-match for Age is default behavior in both channels
        AssertEquivalent(AttrSource, FluentSource, "A-01-mixed", MapMethodName);
    }
}
