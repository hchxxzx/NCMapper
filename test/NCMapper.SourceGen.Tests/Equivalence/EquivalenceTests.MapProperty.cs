using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-07 MapProperty dot-path — Attribute [MapProperty(typeof(Source), "Address.City")]
/// vs Fluent .Member(d => d.City).From(s => s.Address.City)
/// scheme row A-07; assert: SourcePath 2 segments + ExplicitDotPath (attr :486 vs fluent :144)
/// </summary>
public class EquivalenceTestsMapProperty : EquivalenceFixtureBase
{
    private const string AttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A07
        {
            public class A07AddressSource { public string City { get; set; } = ""; }
            public class A07Source { public A07AddressSource Address { get; set; } = new(); public string Name { get; set; } = ""; }

            [MapFrom(typeof(A07Source))]
            public class A07TargetDto
            {
                [MapProperty(typeof(A07Source), "Address.City")]
                public string City { get; set; } = "";
                public string Name { get; set; } = "";
            }
        }
        """;

    private const string FluentSource = """
        using NCMapper;
        namespace EqPairs.A07
        {
            public class A07AddressSource { public string City { get; set; } = ""; }
            public class A07Source { public A07AddressSource Address { get; set; } = new(); public string Name { get; set; } = ""; }
            public class A07TargetDto { public string City { get; set; } = ""; public string Name { get; set; } = ""; }

            [MapperConfig]
            public class A07Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A07Source, A07TargetDto>()
                       .Member(d => d.City).From(s => s.Address.City);
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A07Source_To_A07TargetDto";

    [Fact]
    [EqPair("A-07")]
    [Trait("EqPair", "A-07")]
    public void A07_MapProperty_DotPath_AttributeMatchesFluent()
    {
        AssertEquivalent(AttrSource, FluentSource, "A-07", MapMethodName);
    }

    [Fact]
    [EqPair("A-07")]
    [Trait("EqPair", "A-07")]
    public void A07_MapProperty_DotPath_Mixed_With_AutoMatch()
    {
        // City dot-path explicit, Name auto-matched — same as main source
        AssertEquivalent(AttrSource, FluentSource, "A-07-mixed", MapMethodName);
    }
}
