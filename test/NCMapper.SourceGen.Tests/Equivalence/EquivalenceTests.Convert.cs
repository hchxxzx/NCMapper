using System;
using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-04 Convert — Attribute no-annotation auto-match (DateTime→string via :1086 string passthrough)
/// vs Fluent .Map&lt;DateTime, string&gt;(d => d.Ts, s => s.Ts)
/// scheme row A-04 + construction note (selection locked: DateTime→string);
/// Q7: cross-type emission承接 path anchor — CodeGenerator/TypeConverter ConvertValue chain
/// </summary>
public class EquivalenceTestsConvert : EquivalenceFixtureBase
{
    // Selection locked: DateTime→string (scheme v1.2)
    // Attribute side: no annotation — auto-match DateTime→string via IsTypeCompatibleForDiagnostic :1086 string passthrough
    private const string AttrSource = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A04
        {
            public class A04Source { public DateTime Ts { get; set; } public string Name { get; set; } = ""; }

            [MapFrom(typeof(A04Source))]
            public class A04TargetDto
            {
                public string Ts { get; set; } = "";
                public string Name { get; set; } = "";
            }
        }
        """;

    // Fluent side: explicit .Member(d => d.Ts).From(s => s.Ts)
    private const string FluentSource = """
        using System;
        using NCMapper;
        namespace EqPairs.A04
        {
            public class A04Source { public DateTime Ts { get; set; } public string Name { get; set; } = ""; }
            public class A04TargetDto { public string Ts { get; set; } = ""; public string Name { get; set; } = ""; }

            [MapperConfig]
            public class A04Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A04Source, A04TargetDto>()
                       .Member(d => d.Ts).From(s => s.Ts);
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A04Source_To_A04TargetDto";

    [Fact]
    [EqPair("A-04")]
    [Trait("EqPair", "A-04")]
    public void A04_Convert_DateTimeToString_AttributeMatchesFluent()
    {
        AssertEquivalent(AttrSource, FluentSource, "A-04", MapMethodName);
    }

    [Fact]
    [EqPair("A-04")]
    [Trait("EqPair", "A-04")]
    public void A04_Convert_Mixed_With_AutoMatch()
    {
        // Name auto-matched, Ts cross-type converted — same as main source
        AssertEquivalent(AttrSource, FluentSource, "A-04-mixed", MapMethodName);
    }
}
