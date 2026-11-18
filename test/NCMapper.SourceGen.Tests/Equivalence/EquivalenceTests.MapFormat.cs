using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-23 MapFormat — Attribute [MapFormat("fmt")] vs Fluent .Member(t => t.Prop).Format("fmt")
/// scheme row A-23;（方案书 20260929-003 v1.2 双引擎等价维度——
/// 两通道同承载 pm.FormatString，L1 快照字段级 + L2 指定方法体文本级（单方法，非全产物）等价）。
/// <para>锚点语义：与 .Member(dest).Mask() 同——单 dest lambda 声明、源侧同名自动匹配（跨名组合走特性通道
/// [MapProperty]+[MapFormat]，测试矩阵覆盖）；本 fixture 三形态同名锚点（DateTime/枚举/可空源），
/// 运行时反射路径独立断言在测试矩阵补齐。</para>
/// </summary>
public class EquivalenceTestsMapFormat : EquivalenceFixtureBase
{
    // Attribute channel source: [MapFormat("yyyy-MM-dd")] on CreatedAt（同名锚点：源 DateTime → 目标 string）
    private const string AttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        using System;
        namespace EqPairs.A23
        {
            public class A23Source { public DateTime CreatedAt { get; set; } public string Name { get; set; } = ""; }

            [MapFrom(typeof(A23Source))]
            public class A23TargetDto
            {
                [MapFormat("yyyy-MM-dd")]
                public string CreatedAt { get; set; } = "";
                public string Name { get; set; } = "";
            }
        }
        """;

    // Fluent channel source: clean S/T + [MapperConfig] with .Member(dest).Format(fmt)
    private const string FluentSource = """
        using NCMapper;
        using System;
        namespace EqPairs.A23
        {
            public class A23Source { public DateTime CreatedAt { get; set; } public string Name { get; set; } = ""; }
            public class A23TargetDto { public string CreatedAt { get; set; } = ""; public string Name { get; set; } = ""; }

            [MapperConfig]
            public class A23Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A23Source, A23TargetDto>()
                       .Member(t => t.CreatedAt).Format("yyyy-MM-dd");
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A23Source_To_A23TargetDto";

    [Fact]
    [EqPair("A-23")]
    [Trait("EqPair", "A-23")]
    public void A23_MapFormat_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(AttrSource, FluentSource, "A-23", MapMethodName);
    }

    // 枚举源形态（[MapFormat("D")] / .Member(dest).Format——Enum 实现 IFormattable，net8/9/10 探针实测一致）
    private const string AttrSourceEnum = """
        using NCMapper;
        using NCMapper.Attributes;
        using System;
        namespace EqPairs.A23Enum
        {
            public class A23EnumSource { public DayOfWeek Status { get; set; } }

            [MapFrom(typeof(A23EnumSource))]
            public class A23EnumTargetDto
            {
                [MapFormat("D")]
                public string Status { get; set; } = "";
            }
        }
        """;

    private const string FluentSourceEnum = """
        using NCMapper;
        using System;
        namespace EqPairs.A23Enum
        {
            public class A23EnumSource { public DayOfWeek Status { get; set; } }
            public class A23EnumTargetDto { public string Status { get; set; } = ""; }

            [MapperConfig]
            public class A23EnumMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A23EnumSource, A23EnumTargetDto>()
                       .Member(t => t.Status).Format("D");
                }
            }
        }
        """;

    private const string MapMethodNameEnum = "Map_A23EnumSource_To_A23EnumTargetDto";

    [Fact]
    [EqPair("A-23")]
    [Trait("EqPair", "A-23")]
    public void A23_MapFormat_EnumSource_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(AttrSourceEnum, FluentSourceEnum, "A-23-enum", MapMethodNameEnum);
    }

    // 可空源形态（DateTime? ——null 源 → 目标 null；?. 空条件调用生成形态）
    private const string AttrSourceNullable = """
        using NCMapper;
        using NCMapper.Attributes;
        using System;
        namespace EqPairs.A23Nullable
        {
            public class A23NullableSource { public DateTime? ShippedDate { get; set; } }

            [MapFrom(typeof(A23NullableSource))]
            public class A23NullableTargetDto
            {
                [MapFormat("yyyy-MM-dd")]
                public string? ShippedDate { get; set; }
            }
        }
        """;

    private const string FluentSourceNullable = """
        using NCMapper;
        using System;
        namespace EqPairs.A23Nullable
        {
            public class A23NullableSource { public DateTime? ShippedDate { get; set; } }
            public class A23NullableTargetDto { public string? ShippedDate { get; set; } }

            [MapperConfig]
            public class A23NullableMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A23NullableSource, A23NullableTargetDto>()
                       .Member(t => t.ShippedDate).Format("yyyy-MM-dd");
                }
            }
        }
        """;

    private const string MapMethodNameNullable = "Map_A23NullableSource_To_A23NullableTargetDto";

    [Fact]
    [EqPair("A-23")]
    [Trait("EqPair", "A-23")]
    public void A23_MapFormat_NullableSource_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(AttrSourceNullable, FluentSourceNullable, "A-23-nullable", MapMethodNameNullable);
    }

    // ================================================================
    // A-1 变异演练（A-23 行）——
    // attr 侧 [MapFormat("yyyy-MM-dd")] vs Fluent 侧 .Format("yyyy/MM/dd")：两侧格式串实际不等，
    // 走 AssertL1Difference → L1 必红且差异路径精确命中 PropertyMappings[0].FormatString。
    // 反向证明 L1 对格式串真实生效（B-3 首红的行为层第二重证据）：A-1 补齐前本例红在
    // "两通道快照未检出任何差异"（L1 负例失效 = 字段层等价缺证的假绿形态）；补齐后红在该字段路径。
    // 不挂 [EqPair]/[Trait]——本例为变异演练而非等价对（EquivalenceRegistry 登记面不变）。
    // ================================================================
    private const string AttrSourceFormatDrift = """
        using NCMapper;
        using NCMapper.Attributes;
        using System;
        namespace EqPairs.A23Drift
        {
            public class A23DriftSource { public DateTime CreatedAt { get; set; } }

            [MapFrom(typeof(A23DriftSource))]
            public class A23DriftTargetDto
            {
                [MapFormat("yyyy-MM-dd")]
                public string CreatedAt { get; set; } = "";
            }
        }
        """;

    private const string FluentSourceFormatDrift = """
        using NCMapper;
        using System;
        namespace EqPairs.A23Drift
        {
            public class A23DriftSource { public DateTime CreatedAt { get; set; } }
            public class A23DriftTargetDto { public string CreatedAt { get; set; } = ""; }

            [MapperConfig]
            public class A23DriftMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A23DriftSource, A23DriftTargetDto>()
                       .Member(t => t.CreatedAt).Format("yyyy/MM/dd");
                }
            }
        }
        """;

    [Fact]
    public void A23_Mutation_DifferentFormatString_L1DetectsFormatStringPath()
    {
        AssertL1Difference(AttrSourceFormatDrift, FluentSourceFormatDrift, "A-23-变异", "PropertyMappings[0].FormatString");
    }
}
