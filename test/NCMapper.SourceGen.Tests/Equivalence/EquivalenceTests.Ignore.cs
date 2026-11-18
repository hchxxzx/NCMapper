using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-02 Ignore — Attribute [MapIgnore] (no param = Both) vs Fluent .Member(d => d.X).Ignore()
/// scheme row A-02; assert: property excluded from PropertyMappings/AllTargetProperties on both sides
/// </summary>
public class EquivalenceTestsIgnore : EquivalenceFixtureBase
{
    private const string AttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A02
        {
            // Source-side [MapIgnore] suppresses NCMAP007 (unmapped source member) for Ignored
            public class A02Source { public string Name { get; set; } = ""; [MapIgnore] public string Ignored { get; set; } = ""; }

            [MapFrom(typeof(A02Source))]
            public class A02TargetDto
            {
                public string Name { get; set; } = "";
                [MapIgnore]
                public string Ignored { get; set; } = "";
            }
        }
        """;

    private const string FluentSource = """
        using NCMapper;
        namespace EqPairs.A02
        {
            public class A02Source { public string Name { get; set; } = ""; public string Ignored { get; set; } = ""; }
            public class A02TargetDto { public string Name { get; set; } = ""; public string Ignored { get; set; } = ""; }

            [MapperConfig]
            public class A02Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A02Source, A02TargetDto>()
                       .Member(d => d.Ignored).Ignore();
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A02Source_To_A02TargetDto";

    [Fact]
    [EqPair("A-02")]
    [Trait("EqPair", "A-02")]
    public void A02_Ignore_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(AttrSource, FluentSource, "A-02", MapMethodName);
    }

    [Fact]
    [EqPair("A-02")]
    [Trait("EqPair", "A-02")]
    public void A02_Ignore_Mixed_With_AutoMatch()
    {
        // Name auto-matched, Ignored excluded — same as main source
        AssertEquivalent(AttrSource, FluentSource, "A-02-mixed", MapMethodName);
    }
}
