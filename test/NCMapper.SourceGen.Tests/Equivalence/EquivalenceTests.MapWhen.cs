using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-21 MapWhen — 条件守卫双通道等价对（落地，第六批能力转类前置登记）。
/// <para>特性 [MapWhen(nameof(HasContent))] ↔ Fluent .Member(d => d.Name).MapWhen(A21TargetDto.HasContent)
/// （双侧同承载 P-e：pm.ConditionMethodRef/ConditionShape 双通道同值参与 L1 比较——
/// SnapshotFieldRegistry Compared 登记；扩全矩阵（A/B 形态 × T1/T2））。</para>
/// <para>等价要点：L1 ConditionMethodRef/ConditionShape 双通道同值（"EqPairs.A21.A21TargetDto::HasContent" /
/// FieldValue）；L2 生成方法体含同一守卫包裹（发射后）。</para>
/// </summary>
public class EquivalenceTestsMapWhen : EquivalenceFixtureBase
{
    // Attribute channel source: [MapFrom] DTO + [MapWhen]（方法声明于 DTO 自身——单类型自足形态）
    private const string AttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A21
        {
            public class A21Source { public string Name { get; set; } = ""; public int Age { get; set; } }

            [MapFrom(typeof(A21Source))]
            public class A21TargetDto
            {
                [MapWhen(nameof(HasContent))]
                public string Name { get; set; } = "";
                public int Age { get; set; }

                public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
            }
        }
        """;

    // Fluent channel source: [MapperConfig] + .Member(dest).MapWhen(方法组)（谓词宿主与特性侧同为 DTO 类型——L1 ref 同值）
    private const string FluentSource = """
        using NCMapper;
        namespace EqPairs.A21
        {
            public class A21Source { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class A21TargetDto
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }

                public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
            }

            [MapperConfig]
            public class A21Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A21Source, A21TargetDto>()
                       .Member(d => d.Name).MapWhen(A21TargetDto.HasContent);
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A21Source_To_A21TargetDto";

    [Fact]
    [EqPair("A-21")]
    [Trait("EqPair", "A-21")]
    public void A21_MapWhen_AttributeChannel_Matches_FluentChannel()
    {
        // L1：ConditionMethodRef/ConditionShape 双通道同值（"EqPairs.A21.A21TargetDto::HasContent" / FieldValue——
        // P-e 双侧同承载，SnapshotFieldRegistry Compared）；L2：发射后含同一守卫包裹行
        AssertEquivalent(AttrSource, FluentSource, "A-21", MapMethodName);
    }

    // ==== 矩阵扩展（等价对行）：A-21 T2 整源形态——同 FixtureFile、
    // 同注册表条目（A-21 单条目多 fixture；#37 复刻型口径，#38 三核查自动联动）====

    // Attribute channel source: T2 整源谓词（入参 = 源实体类型）
    private const string AttrSourceT2 = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A21
        {
            public class A21T2Source { public int Id { get; set; } public int Score { get; set; } }

            [MapFrom(typeof(A21T2Source))]
            public class A21T2TargetDto
            {
                public int Id { get; set; }

                [MapWhen(nameof(IsPositive))]
                public int Score { get; set; }

                public static bool IsPositive(A21T2Source source) => source.Score > 0;
            }
        }
        """;

    // Fluent channel source: T2 整源谓词方法组（入参 = TSource 类级固定——方法组可直用重载）
    private const string FluentSourceT2 = """
        using NCMapper;
        namespace EqPairs.A21
        {
            public class A21T2Source { public int Id { get; set; } public int Score { get; set; } }
            public class A21T2TargetDto
            {
                public int Id { get; set; }
                public int Score { get; set; }

                public static bool IsPositive(A21T2Source source) => source.Score > 0;
            }

            [MapperConfig]
            public class A21T2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A21T2Source, A21T2TargetDto>()
                       .Member(d => d.Score).MapWhen<A21T2Source>(A21T2TargetDto.IsPositive);
                }
            }
        }
        """;

    private const string MapMethodNameT2 = "Map_A21T2Source_To_A21T2TargetDto";

    /// <summary>
    /// A-21 T2 整源形态（矩阵扩展）：L1 ConditionMethodRef/ConditionShape 双通道同值
    /// （"EqPairs.A21.A21T2TargetDto::IsPositive" / WholeSource——T2 整源实参恒 src，
    /// 提取层形态判定双通道同构）；L2 守卫包裹行（if (M(src)) 形态）。
    /// </summary>
    [Fact]
    [EqPair("A-21")]
    [Trait("EqPair", "A-21")]
    public void A21_T2_WholeSource_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(AttrSourceT2, FluentSourceT2, "A-21", MapMethodNameT2);
    }
}
