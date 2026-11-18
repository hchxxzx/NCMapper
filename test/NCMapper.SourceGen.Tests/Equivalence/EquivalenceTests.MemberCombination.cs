using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-24 MemberCombination — 成员级组合叠加双通道等价（通道对照行，
/// 评审 002 P1-3① 硬性化；维度合并通道）：
/// 特性侧 = [MapProperty(源路径)] + 变换特性同属性叠加；Fluent 侧 = .Member(d).From(s).&lt;变换&gt;() 配置段叠加。
/// 五组合类各 ≥1（From+Mask / From+Convert / From+Format / From+FromJson / From+MapWhen）
/// + From 跨型变体（挂 A-01 能力行）+ 豁免门变体（[Mask(OverrideIgnore=true)] ↔ .Mask&lt;M&gt;(overrideIgnore: true)）。
/// 断言 = AssertEquivalent（L1 快照等价 + L2 方法体逐字等价——EquivalenceFixtureBase 既有口径）。
/// </summary>
public class EquivalenceTestsMemberCombination : EquivalenceFixtureBase
{
    // ── ① From + Mask ──────────────────────────────────────────────

    private const string MaskAttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A24
        {
            public class A24MaskSource { public string Mobile { get; set; } = ""; }
            [MapFrom(typeof(A24MaskSource))]
            public class A24MaskDto
            {
                [MapProperty(typeof(A24MaskSource), nameof(A24MaskSource.Mobile))]
                [Mask(typeof(A24MaskPhone))]
                public string Phone { get; set; } = "";
            }
            public class A24MaskPhone : IMask
            {
                public string Mask(string? value) => (value != null && value.Length > 3 ? value.Substring(0, 3) : value) + "***";
            }
        }
        """;

    private const string MaskFluentSource = """
        using NCMapper;
        namespace EqPairs.A24
        {
            public class A24MaskSource { public string Mobile { get; set; } = ""; }
            public class A24MaskDto { public string Phone { get; set; } = ""; }
            [MapperConfig]
            public class A24MaskMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<A24MaskSource, A24MaskDto>()
                          .Member(d => d.Phone).From(s => s.Mobile).Mask<A24MaskPhone>();
            }
            public class A24MaskPhone : IMask
            {
                public string Mask(string? value) => (value != null && value.Length > 3 ? value.Substring(0, 3) : value) + "***";
            }
        }
        """;

    [Fact]
    [EqPair("A-24")]
    [Trait("EqPair", "A-24")]
    public void A24_FromPlusMask_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(MaskAttrSource, MaskFluentSource, "A-24", "Map_A24MaskSource_To_A24MaskDto");
    }

    // ── ② From + Convert ───────────────────────────────────────────

    private const string ConvertAttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A24
        {
            public class A24ConvSource { public int Count { get; set; } }
            [MapFrom(typeof(A24ConvSource))]
            public class A24ConvDto
            {
                [MapProperty(typeof(A24ConvSource), nameof(A24ConvSource.Count))]
                [Convert(typeof(A24Conv), nameof(A24Conv.Text))]
                public string Count { get; set; } = "";
            }
            public static class A24Conv { public static string Text(int v) => "#" + v; }
        }
        """;

    private const string ConvertFluentSource = """
        using NCMapper;
        namespace EqPairs.A24
        {
            public class A24ConvSource { public int Count { get; set; } }
            public class A24ConvDto { public string Count { get; set; } = ""; }
            [MapperConfig]
            public class A24ConvMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<A24ConvSource, A24ConvDto>()
                          .Member(d => d.Count).From(s => s.Count).Convert<int>(A24Conv.Text);
            }
            public static class A24Conv { public static string Text(int v) => "#" + v; }
        }
        """;

    [Fact]
    [EqPair("A-24")]
    [Trait("EqPair", "A-24")]
    public void A24_FromPlusConvert_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(ConvertAttrSource, ConvertFluentSource, "A-24", "Map_A24ConvSource_To_A24ConvDto");
    }

    // ── ③ From + Format ────────────────────────────────────────────

    private const string FormatAttrSource = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A24
        {
            public class A24FmtSource { public DateTime Stamp { get; set; } = new(2024, 6, 1); }
            [MapFrom(typeof(A24FmtSource))]
            public class A24FmtDto
            {
                [MapProperty(typeof(A24FmtSource), nameof(A24FmtSource.Stamp))]
                [MapFormat("yyyy-MM-dd")]
                public string Stamp { get; set; } = "";
            }
        }
        """;

    private const string FormatFluentSource = """
        using System;
        using NCMapper;
        namespace EqPairs.A24
        {
            public class A24FmtSource { public DateTime Stamp { get; set; } = new(2024, 6, 1); }
            public class A24FmtDto { public string Stamp { get; set; } = ""; }
            [MapperConfig]
            public class A24FmtMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<A24FmtSource, A24FmtDto>()
                          .Member(d => d.Stamp).From(s => s.Stamp).Format("yyyy-MM-dd");
            }
        }
        """;

    [Fact]
    [EqPair("A-24")]
    [Trait("EqPair", "A-24")]
    public void A24_FromPlusFormat_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(FormatAttrSource, FormatFluentSource, "A-24", "Map_A24FmtSource_To_A24FmtDto");
    }

    // ── ④ From + FromJson ──────────────────────────────────────────

    private const string FromJsonAttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A24
        {
            public class A24Pay { public int Id { get; set; } }
            public class A24JsonSource { public string Blob { get; set; } = ""; }
            [MapFrom(typeof(A24JsonSource))]
            public class A24JsonDto
            {
                [MapProperty(typeof(A24JsonSource), nameof(A24JsonSource.Blob), IsFromJson = true)]
                public A24Pay Blob { get; set; } = new();
            }
        }
        """;

    private const string FromJsonFluentSource = """
        using NCMapper;
        namespace EqPairs.A24
        {
            public class A24Pay { public int Id { get; set; } }
            public class A24JsonSource { public string Blob { get; set; } = ""; }
            public class A24JsonDto { public A24Pay Blob { get; set; } = new(); }
            [MapperConfig]
            public class A24JsonMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<A24JsonSource, A24JsonDto>()
                          .Member(d => d.Blob).From(s => s.Blob).FromJson();
            }
        }
        """;

    [Fact]
    [EqPair("A-24")]
    [Trait("EqPair", "A-24")]
    public void A24_FromPlusFromJson_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(FromJsonAttrSource, FromJsonFluentSource, "A-24", "Map_A24JsonSource_To_A24JsonDto");
    }

    // ── ⑤ From + MapWhen ───────────────────────────────────────────

    private const string MapWhenAttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A24
        {
            public class A24MwSource { public string Title { get; set; } = ""; }
            [MapFrom(typeof(A24MwSource))]
            public class A24MwDto
            {
                [MapProperty(typeof(A24MwSource), nameof(A24MwSource.Title))]
                [MapWhen(nameof(HasContent))]
                public string Name { get; set; } = "";
                public static bool HasContent(string v) => v is { Length: > 0 };
            }
        }
        """;

    private const string MapWhenFluentSource = """
        using NCMapper;
        namespace EqPairs.A24
        {
            public class A24MwSource { public string Title { get; set; } = ""; }
            public class A24MwDto { public string Name { get; set; } = ""; public static bool HasContent(string v) => v is { Length: > 0 }; }
            [MapperConfig]
            public class A24MwMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<A24MwSource, A24MwDto>()
                          .Member(d => d.Name).From(s => s.Title).MapWhen(A24MwDto.HasContent);
            }
        }
        """;

    [Fact]
    [EqPair("A-24")]
    [Trait("EqPair", "A-24")]
    public void A24_FromPlusMapWhen_AttributeChannel_Matches_FluentChannel()
    {
        // 双通道均用命名方法承载条件守卫（特性 [MapWhen(nameof)] ↔ Fluent .MapWhen(方法组)）——
        // ConditionMethodRef/Body/ParamName 同构（内联 lambda 会合成匿名方法致 L1 三字段差异）
        AssertEquivalent(MapWhenAttrSource, MapWhenFluentSource, "A-24", "Map_A24MwSource_To_A24MwDto");
    }

    // ── ⑥ From 跨型变体（挂 A-01 Map 能力行——A-03_mixed 同款后缀形态） ──

    private const string CrossTypeAttrSource = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A24
        {
            public class A24CtSource { public DateTime Created { get; set; } = new(2024, 6, 1); }
            [MapFrom(typeof(A24CtSource))]
            public class A24CtDto
            {
                [MapProperty(typeof(A24CtSource), nameof(A24CtSource.Created))]
                public string Created { get; set; } = "";
            }
        }
        """;

    private const string CrossTypeFluentSource = """
        using System;
        using NCMapper;
        namespace EqPairs.A24
        {
            public class A24CtSource { public DateTime Created { get; set; } = new(2024, 6, 1); }
            public class A24CtDto { public string Created { get; set; } = ""; }
            [MapperConfig]
            public class A24CtMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<A24CtSource, A24CtDto>().Member(d => d.Created).From(s => s.Created);
            }
        }
        """;

    [Fact]
    [EqPair("A-01")]
    [Trait("EqPair", "A-01")]
    public void A01_From_CrossType_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(CrossTypeAttrSource, CrossTypeFluentSource, "A-01-crosstype", "Map_A24CtSource_To_A24CtDto");
    }

    // ── ⑦ 豁免门变体（[Mask(OverrideIgnore=true)] ↔ .Mask<M>(overrideIgnore: true)） ──

    private const string ExemptAttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A24
        {
            public class A24ExSource { public string Phone { get; set; } = ""; }
            [MapFrom(typeof(A24ExSource))]
            public class A24ExDto
            {
                [Mask(typeof(A24ExPhone), OverrideIgnore = true)]
                public string Phone { get; set; } = "";
            }
            public class A24ExPhone : IMask
            {
                public string Mask(string? value) => (value != null && value.Length > 3 ? value.Substring(0, 3) : value) + "***";
            }
        }
        """;

    private const string ExemptFluentSource = """
        using NCMapper;
        namespace EqPairs.A24
        {
            public class A24ExSource { public string Phone { get; set; } = ""; }
            public class A24ExDto { public string Phone { get; set; } = ""; }
            [MapperConfig]
            public class A24ExMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<A24ExSource, A24ExDto>()
                          .Member(d => d.Phone).Mask<A24ExPhone>(overrideIgnore: true);
            }
            public class A24ExPhone : IMask
            {
                public string Mask(string? value) => (value != null && value.Length > 3 ? value.Substring(0, 3) : value) + "***";
            }
        }
        """;

    [Fact]
    [EqPair("A-24")]
    [Trait("EqPair", "A-24")]
    public void A24_ExemptionGate_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(ExemptAttrSource, ExemptFluentSource, "A-24-exemptgate", "Map_A24ExSource_To_A24ExDto");
    }
}
