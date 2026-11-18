using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-08 reverse derivation — dot-path triggers ReverseWritePath;
/// L1 verifies forward snapshot equivalence (includes ReverseWritePath segment isomorphism);
/// L2 verifies REVERSE method body (Map_Dto_To_Entity) equivalence — Q3 ReverseWritePath isomorphism
/// scheme row A-08
/// </summary>
public class EquivalenceTestsReverseDerive : EquivalenceFixtureBase
{
    // Dot-path: DTO flat City ← Entity nested Address.City → triggers ReverseWritePath on reverse derivation
    private const string AttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A08
        {
            public class A08AddressEntity { public string City { get; set; } = ""; }
            public class A08Entity { public A08AddressEntity Address { get; set; } = new(); public string Name { get; set; } = ""; }

            [MapFrom(typeof(A08Entity))]
            public class A08Dto
            {
                [MapProperty(typeof(A08Entity), "Address.City")]
                public string City { get; set; } = "";
                public string Name { get; set; } = "";
            }
        }
        """;

    private const string FluentSource = """
        using NCMapper;
        namespace EqPairs.A08
        {
            public class A08AddressEntity { public string City { get; set; } = ""; }
            public class A08Entity { public A08AddressEntity Address { get; set; } = new(); public string Name { get; set; } = ""; }
            public class A08Dto { public string City { get; set; } = ""; public string Name { get; set; } = ""; }

            [MapperConfig]
            public class A08Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A08Entity, A08Dto>()
                       .Member(d => d.City).From(s => s.Address.City);
                }
            }
        }
        """;

    // L2 compares REVERSE method body (Q3: ReverseWritePath isomorphism)
    private const string ReverseMapMethodName = "Map_A08Dto_To_A08Entity";

    [Fact]
    [EqPair("A-08")]
    [Trait("EqPair", "A-08")]
    public void A08_ReverseDerive_AttributeMatchesFluent()
    {
        AssertEquivalent(AttrSource, FluentSource, "A-08", ReverseMapMethodName);
    }

    [Fact]
    [EqPair("A-08")]
    [Trait("EqPair", "A-08")]
    public void A08_ReverseDerive_Mixed_With_AutoMatch()
    {
        // City dot-path (reverse write), Name auto-matched — same as main source
        AssertEquivalent(AttrSource, FluentSource, "A-08-mixed", ReverseMapMethodName);
    }
}
