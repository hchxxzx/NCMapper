using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-03 Mask — Attribute [Mask(typeof(MaskPhone))] vs Fluent .Member(t => t.Phone).Mask&lt;MaskPhone&gt;()
/// scheme row A-03; Q1 PathOrigin + Q2 L2 verification for Mask capability
/// </summary>
public class EquivalenceTestsMask : EquivalenceFixtureBase
{
    // Attribute channel source: [Mask(typeof(MaskPhone))] on Phone property
    private const string AttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A03
        {
            public class A03Source { public string Phone { get; set; } = ""; public string Name { get; set; } = ""; }

            [MapFrom(typeof(A03Source))]
            public class A03TargetDto
            {
                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; } = "";
                public string Name { get; set; } = "";
            }

            public class MaskPhone : IMask
            {
                public string Mask(string? value) => (value != null && value.Length > 3 ? value.Substring(0, 3) : value) + "***";
            }
        }
        """;

    // Fluent channel source: clean S/T + [MapperConfig] with .Member(t => t.Phone).Mask<MaskPhone>()
    private const string FluentSource = """
        using NCMapper;
        namespace EqPairs.A03
        {
            public class A03Source { public string Phone { get; set; } = ""; public string Name { get; set; } = ""; }
            public class A03TargetDto { public string Phone { get; set; } = ""; public string Name { get; set; } = ""; }

            [MapperConfig]
            public class A03Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A03Source, A03TargetDto>()
                       .Member(t => t.Phone).Mask<MaskPhone>();
                }
            }

            public class MaskPhone : IMask
            {
                public string Mask(string? value) => (value != null && value.Length > 3 ? value.Substring(0, 3) : value) + "***";
            }
        }
        """;

    private const string MapMethodName = "Map_A03Source_To_A03TargetDto";

    [Fact]
    [EqPair("A-03")]
    [Trait("EqPair", "A-03")]
    public void A03_Mask_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(AttrSource, FluentSource, "A-03", MapMethodName);
    }

    /// <summary>
    /// Mixed boundary scenario (scheme): Mask on Phone + auto-match on Name
    /// — verify decision-product overlay isomorphic
    /// </summary>
    [Fact]
    [EqPair("A-03")]
    [Trait("EqPair", "A-03")]
    public void A03_Mask_Mixed_With_AutoMatch()
    {
        // Same as main A-03 source — Phone has Mask, Name relies on auto-match
        AssertEquivalent(AttrSource, FluentSource, "A-03-mixed", MapMethodName);
    }
}
