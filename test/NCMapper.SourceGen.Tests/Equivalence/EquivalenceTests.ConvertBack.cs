using System;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-09 ConvertBack — Attribute [ConvertBack] vs Fluent .Member(dest).ConvertBack（T1 单值 string→DateTime）
/// <para> Q7 C-01→A-09 迁移（等价机制首次能力转类实战）；
/// 003 迁移预案兑现。fixture 模型避开 get-only DTO 锚点（H-07 门方向 A 特有
/// 分叉——A-09 首发不覆盖该边界）；L2 断言反向方法体（A-08 反向语义对先例约定）。</para>
/// </summary>
public class EquivalenceTestsConvertBack : EquivalenceFixtureBase
{
    // Selection locked: T1 single-value string→DateTime（匹配对场景——正向自动匹配 + 反向自定义解析）
    // Attribute side: DTO 锚点标注 [ConvertBack]（规范位置——TField=DTO 属性类型、TSource=DTO 类型、
    // TTarget=实体同名属性类型）
    private const string AttrSource = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A09
        {
            public class A09Source { public DateTime Ts { get; set; } public string Name { get; set; } = ""; }

            [MapFrom(typeof(A09Source))]
            public class A09TargetDto
            {
                [ConvertBack(typeof(A09Conv), nameof(A09Conv.Parse))]
                public string Ts { get; set; } = "";
                public string Name { get; set; } = "";
            }

            public static class A09Conv
            {
                public static DateTime Parse(string fieldValue) => DateTime.Parse(fieldValue, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        """;

    // Fluent side: .Member(dest).ConvertBack 方法组声明（同 converter 方法、同 S/T 模型——双源约定）
    private const string FluentSource = """
        using System;
        using NCMapper;
        namespace EqPairs.A09
        {
            public class A09Source { public DateTime Ts { get; set; } public string Name { get; set; } = ""; }
            public class A09TargetDto { public string Ts { get; set; } = ""; public string Name { get; set; } = ""; }

            [MapperConfig]
            public class A09Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A09Source, A09TargetDto>()
                       .Member(d => d.Ts).ConvertBack(A09Conv.Parse);
                }
            }

            public static class A09Conv
            {
                public static DateTime Parse(string fieldValue) => DateTime.Parse(fieldValue, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        """;

    private const string ReverseMapMethodName = "Map_A09TargetDto_To_A09Source";

    [Fact]
    [EqPair("A-09")]
    [Trait("EqPair", "A-09")]
    public void A09_ConvertBack_T1SingleValue_AttributeMatchesFluent()
    {
        AssertEquivalent(AttrSource, FluentSource, "A-09", ReverseMapMethodName);
    }

    [Fact]
    [EqPair("A-09")]
    [Trait("EqPair", "A-09")]
    public void A09_ConvertBack_Mixed_With_AutoMatch()
    {
        // Name auto-matched, Ts reverse custom — same as main source
        AssertEquivalent(AttrSource, FluentSource, "A-09-mixed", ReverseMapMethodName);
    }

    // ── 写回目标三级解析与 lambda 形态等价（同名 → 自动扁平化叶 → lambda 体）────────────
    // 期望值来源：FluentMappingConfig ConvertBack 重载 XML 注释「写回目标解析顺序」语义＋
    // 生成链提取期解析序（同名命中优先；异名经 lambda 判定放行后，写回落点由正向来源字段承载）。
    // 本族钉「写回落点＋取参面」在双通道（特性方法组 ↔ Fluent lambda）下的语义等价。
    // 形态层文本不可全等（特性侧无 lambda 表达位、Fluent 侧异名无方法组出路——A 直调 vs B 转录
    // 分叉与「维持剥离」登记同因），故逐侧方法体钉断言＋L1 结构等价，不做 L2 全文对。

    /// <summary>
    /// T3 组合 lambda（异名 Format 链）↔ 特性侧 T3 方法组：两通道反向腿都写回实体 Amount、
    /// 都以 (src, src.AmountText) 取参——写回落点与取参面语义等价；无 F18/F67。
    /// </summary>
    [Fact]
    [EqPair("A-09")]
    [Trait("EqPair", "A-09")]
    public void A09_ConvertBack_LambdaT3Alias_EquivalentTo_AttrMethodGroupT3()
    {
        var attrSource = """
            using System;
            using NCMapper;
            using NCMapper.Attributes;
            namespace EqPairs.A09t
            {
                public class A09tSource { public decimal Amount { get; set; } }

                [MapFrom(typeof(A09tSource))]
                public class A09tTargetDto
                {
                    [MapProperty(typeof(A09tSource), "Amount")][MapFormat("N2")]
                    [ConvertBack(typeof(A09tConv), nameof(A09tConv.Parse))]
                    public string AmountText { get; set; } = "";
                }

                public static class A09tConv
                {
                    public static decimal Parse(A09tTargetDto dto, string amountText)
                        => decimal.Parse(amountText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            """;

        var fluentSource = """
            using System;
            using NCMapper;
            namespace EqPairs.A09t
            {
                public class A09tSource { public decimal Amount { get; set; } }
                public class A09tTargetDto { public string AmountText { get; set; } = ""; }

                public static class A09tConv
                {
                    public static decimal Parse(string amountText)
                        => decimal.Parse(amountText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture);
                }

                [MapperConfig]
                public class A09tMapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A09tSource, A09tTargetDto>()
                           .Member(d => d.AmountText).From(s => s.Amount).Format("N2")
                           .ConvertBack<decimal>((dto, text) => A09tConv.Parse(text));
                    }
                }
            }
            """;

        var attrGen = TestHelper.GenerateMapper(attrSource);
        Assert.DoesNotContain(attrGen.Diagnostics, d => d.Id == "NCMAP_F18" || d.Id == "NCMAP_F67" || d.Id == "NCMAP046");
        var attrRev = attrGen.Methods["Map_A09tTargetDto_To_A09tSource"][0].Body;
        Assert.Contains("Amount = global::EqPairs.A09t.A09tConv.Parse(src, src.AmountText)", attrRev);

        var fluentGen = TestHelper.GenerateMapper(fluentSource);
        Assert.DoesNotContain(fluentGen.Diagnostics, d => d.Id == "NCMAP_F18" || d.Id == "NCMAP_F67");
        var fluentRev = fluentGen.Methods["Map_A09tTargetDto_To_A09tSource"][0].Body;
        // B 形态转录接线：写回落点＝正向来源字段 Amount，取参面＝(src, src.AmountText)——与特性侧同语义
        Assert.Contains("Amount = global::NCMapper.Generated.GeneratedMapper.Map_CvBack_", fluentRev);
        Assert.Contains("(src, src.AmountText)", fluentRev);
        Assert.DoesNotContain("AmountText = ", fluentRev);
    }

    /// <summary>
    /// T2 整源 lambda（纯改名，裸成员访问形态）↔ 特性侧 T2 方法组：两通道反向腿都写回实体 Name、
    /// 都从 src.DisplayName 取值——「名字不同要显式写」的 T2 出口与特性侧显式来源语义等价。
    /// </summary>
    [Fact]
    [EqPair("A-09")]
    [Trait("EqPair", "A-09")]
    public void A09_ConvertBack_LambdaT2Rename_EquivalentTo_AttrMethodGroupT2()
    {
        var attrSource = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace EqPairs.A09r
            {
                public class A09rSource { public string Name { get; set; } = ""; }

                [MapFrom(typeof(A09rSource))]
                public class A09rTargetDto
                {
                    [MapProperty(typeof(A09rSource), "Name")]
                    [ConvertBack(typeof(A09rConv), nameof(A09rConv.Back))]
                    public string DisplayName { get; set; } = "";
                }

                public static class A09rConv
                {
                    public static string Back(A09rTargetDto dto) => dto.DisplayName;
                }
            }
            """;

        var fluentSource = """
            using NCMapper;
            namespace EqPairs.A09r
            {
                public class A09rSource { public string Name { get; set; } = ""; }
                public class A09rTargetDto { public string DisplayName { get; set; } = ""; }

                [MapperConfig]
                public class A09rMapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A09rSource, A09rTargetDto>()
                           .Member(d => d.DisplayName).From(s => s.Name)
                           .ConvertBack<string>((A09rTargetDto dto) => dto.DisplayName);
                    }
                }
            }
            """;

        var attrGen = TestHelper.GenerateMapper(attrSource);
        Assert.DoesNotContain(attrGen.Diagnostics, d => d.Id == "NCMAP_F18");
        var attrRev = attrGen.Methods["Map_A09rTargetDto_To_A09rSource"][0].Body;
        // 特性侧 T2 方法组＝整源入参（Back(src)）；写回落点 Name（正向来源字段）
        Assert.Contains("Name = global::EqPairs.A09r.A09rConv.Back(src)", attrRev);

        var fluentGen = TestHelper.GenerateMapper(fluentSource);
        Assert.DoesNotContain(fluentGen.Diagnostics, d => d.Id == "NCMAP_F18");
        var fluentRev = fluentGen.Methods["Map_A09rTargetDto_To_A09rSource"][0].Body;
        // 写回落点＝正向来源 Name（不是锚点名 DisplayName）；lambda 体成员访问（DisplayName）
        // 作为显式写回意图进入转录转换器命名——意图在、落点不错位
        Assert.Contains("Name = global::NCMapper.Generated.GeneratedMapper.Map_CvBack_A09rTargetDto_DisplayName_", fluentRev);
        Assert.DoesNotContain("DisplayName = ", fluentRev);
    }

    /// <summary>
    /// 同名优先（三级解析回归保护）：实体侧存在同名属性时解析止于同名步——lambda 体成员访问
    /// （指向 DTO 另一成员 When）不参与写回目标解析，写回点仍是同名 Ts；既有同名行为零变化。
    /// </summary>
    [Fact]
    [EqPair("A-09")]
    [Trait("EqPair", "A-09")]
    public void A09_ConvertBack_SameNamePriority_LambdaNotConsulted()
    {
        var attrSource = """
            using System;
            using NCMapper;
            using NCMapper.Attributes;
            namespace EqPairs.A09p
            {
                public class A09pSource { public DateTime Ts { get; set; } }

                [MapFrom(typeof(A09pSource))]
                public class A09pTargetDto
                {
                    [ConvertBack(typeof(A09pConv), nameof(A09pConv.Parse))]
                    public string Ts { get; set; } = "";
                    public DateTime When { get; set; }
                }

                public static class A09pConv
                {
                    public static DateTime Parse(string fieldValue)
                        => DateTime.Parse(fieldValue, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            """;

        var fluentSource = """
            using System;
            using NCMapper;
            namespace EqPairs.A09p
            {
                public class A09pSource { public DateTime Ts { get; set; } }
                public class A09pTargetDto { public string Ts { get; set; } = ""; public DateTime When { get; set; } }

                [MapperConfig]
                public class A09pMapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A09pSource, A09pTargetDto>()
                           .Member(d => d.Ts)
                           .ConvertBack<DateTime>((A09pTargetDto dto) => dto.When);
                    }
                }
            }
            """;

        var attrGen = TestHelper.GenerateMapper(attrSource);
        var attrRev = attrGen.Methods["Map_A09pTargetDto_To_A09pSource"][0].Body;
        Assert.Contains("Ts = global::EqPairs.A09p.A09pConv.Parse(src.Ts)", attrRev);

        var fluentGen = TestHelper.GenerateMapper(fluentSource);
        Assert.DoesNotContain(fluentGen.Diagnostics, d => d.Id == "NCMAP_F18");
        var fluentRev = fluentGen.Methods["Map_A09pTargetDto_To_A09pSource"][0].Body;
        // 同名 Ts 命中即止：写回点 Ts，lambda 体的 When 只进转换器体、不作写回落点
        Assert.Contains("Ts = global::NCMapper.Generated.GeneratedMapper.Map_CvBack_", fluentRev);
        Assert.DoesNotContain("When = ", fluentRev);
        Assert.Contains("dto.When", fluentGen.GeneratedCode);
    }
}
