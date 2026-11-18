using System;
using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-13 Compose —（registry C-03 → A-13 迁移，等价机制第三批能力转类——
/// 004 A-09 / 006 A-10~A-12 先例；Fluent 通道 Compose 多源组合（F-4/C-03）落地）。
/// <para>A-13 基础等价对：特性侧 [MapFrom]+[MapProperty(FromSource=子源)]（无索引、无转换器）↔
/// Fluent .Compose&lt;TSub&gt;(dest, source) 速记 / 块内 c.Map——跨源字段双侧均物化为 ComposeMappings
///（特性侧 MappingExtractor.ProcessComposeMapping:245；Fluent 侧 MappingSnapshotBuilder:173），
/// L1 cm 级七字段（SubSource/Source/SourcePath/SourceIndexes/Target/Converter 三元组）直接可比
///（SnapshotEquivalenceComparer）；SourceIndexes 两侧恒空（特性侧 :314 无索引路径空列表 / Fluent 侧 B4 恒空）。
/// L2 主链 Map 方法体：跨源字段双侧均不进入主链生成体（物化分流——Compose 命中只入 ComposeMappings
/// 不入 PropertyMappings），主链赋值文本等价。</para>
/// <para>A-13 转换器等价对：块内 .Member(dest).Convert<TSrcProp>（T1 字段值形态）↔ 特性 [Convert]——
/// 特性侧跨源字段原生消费 [Convert]（MappingExtractor.cs:320-332，ResolveConverterMethod
/// 与 Fluent 块内同一解析器），Converter 三字段（DeclaringType/MethodName/Shape）同源可比
///（A-10 T1 先例口径）。</para>
/// <para>A-13 Mask 组合等价对（激活，002 条件条目解除）：
/// 特性 [MapProperty(FromSource)]+[Mask] 同名 ↔ Fluent 块内单 c.Member(dest).Mask()——两侧块内 Mask 均物化
/// 入 ComposeMappings（D-1 降级已解除），L1 cm 级 MaskType 直接可比（comparer :242-248，
/// 001 已登记 Compared）。</para>
/// <para> 否定（F9）：Compose×Compose 同 dest 不同子源为 Fluent 独有能力（特性侧
/// [MapProperty] 单字段单子源粒度无法表达同 dest 双子源顺序覆盖），不入等价对。</para>
/// </summary>
public class EquivalenceTestsCompose : EquivalenceFixtureBase
{
    // ================================================================
    // 基础等价对：跨源字段（无索引、无转换器）——速记 + 块内 Map 混合
    // ================================================================

    private const string A13BasicAttr = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A13
        {
            public class E13Source { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class E13Sub { public string Title { get; set; } = ""; public string Phone { get; set; } = ""; }

            [MapFrom(typeof(E13Source))]
            public class E13TargetDto
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
                [MapProperty(typeof(E13Sub), nameof(E13Sub.Title))]
                public string Title { get; set; } = "";
                [MapProperty(typeof(E13Sub), nameof(E13Sub.Phone))]
                public string Phone { get; set; } = "";
            }
        }
        """;

    private const string A13BasicFluent = """
        using System;
        using NCMapper;
        namespace EqPairs.A13
        {
            public class E13Source { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class E13Sub { public string Title { get; set; } = ""; public string Phone { get; set; } = ""; }
            public class E13TargetDto { public string Name { get; set; } = ""; public int Age { get; set; }
                public string Title { get; set; } = ""; public string Phone { get; set; } = ""; }

            [MapperConfig]
            public class E13Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<E13Source, E13TargetDto>()
                       .Member(d => d.Title).FromSource<E13Sub>(s => s.Title)
                       .FromSource<E13Sub>(c => { c.Member(d => d.Phone).From(s => s.Phone); });
                }
            }
        }
        """;

    // ================================================================
    // 转换器等价对：块内 Member(dest).Convert T1（字段值）↔ [Convert]
    // ================================================================

    private const string A13ConvAttr = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A13
        {
            public class E13CSource { public string Name { get; set; } = ""; }
            public class E13CSub { public string Code { get; set; } = ""; }

            [MapFrom(typeof(E13CSource))]
            public class E13CTargetDto
            {
                public string Name { get; set; } = "";
                [MapProperty(typeof(E13CSub), nameof(E13CSub.Code))]
                [Convert(typeof(E13Conv), nameof(E13Conv.Upper))]
                public string Code { get; set; } = "";
            }

            public static class E13Conv
            {
                public static string Upper(string fieldValue) => fieldValue.ToUpperInvariant();
            }
        }
        """;

    private const string A13ConvFluent = """
        using System;
        using NCMapper;
        namespace EqPairs.A13
        {
            public class E13CSource { public string Name { get; set; } = ""; }
            public class E13CSub { public string Code { get; set; } = ""; }
            public class E13CTargetDto { public string Name { get; set; } = ""; public string Code { get; set; } = ""; }

            [MapperConfig]
            public class E13CMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<E13CSource, E13CTargetDto>()
                       .FromSource<E13CSub>(c => { c.Member(d => d.Code).Convert<string>(E13Conv.Upper); });
                }
            }

            public static class E13Conv
            {
                public static string Upper(string fieldValue) => fieldValue.ToUpperInvariant();
            }
        }
        """;

    // ================================================================
    // Mask 组合等价对：[MapProperty]+[Mask] ↔ 块内 c.Member(dest).Mask()（激活）
    // ================================================================

    private const string A13MaskAttr = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A13
        {
            public class E13MSource { public string Name { get; set; } = ""; }
            public class E13MSub { public string Phone { get; set; } = ""; }

            [MapFrom(typeof(E13MSource))]
            public class E13MTargetDto
            {
                public string Name { get; set; } = "";
                [MapProperty(typeof(E13MSub), nameof(E13MSub.Phone))]
                [Mask(typeof(NCMapper.MaskPhone))]
                public string Phone { get; set; } = "";
            }
        }
        """;

    private const string A13MaskFluent = """
        using System;
        using NCMapper;
        namespace EqPairs.A13
        {
            public class E13MSource { public string Name { get; set; } = ""; }
            public class E13MSub { public string Phone { get; set; } = ""; }
            public class E13MTargetDto { public string Name { get; set; } = ""; public string Phone { get; set; } = ""; }

            [MapperConfig]
            public class E13MMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<E13MSource, E13MTargetDto>()
                       .FromSource<E13MSub>(c => { c.Member(d => d.Phone).Mask<NCMapper.MaskPhone>(); });
                }
            }
        }
        """;

    [Fact]
    [EqPair("A-13")]
    [Trait("EqPair", "A-13")]
    public void A13_Compose_Basic_AttributeMatchesFluent()
        => AssertEquivalent(A13BasicAttr, A13BasicFluent, "A-13-basic", "Map_E13Source_To_E13TargetDto");

    [Fact]
    [EqPair("A-13")]
    [Trait("EqPair", "A-13")]
    public void A13_Compose_Converter_T1FieldValue_AttributeMatchesFluent()
        => AssertEquivalent(A13ConvAttr, A13ConvFluent, "A-13-conv", "Map_E13CSource_To_E13CTargetDto");

    [Fact]
    [EqPair("A-13")]
    [Trait("EqPair", "A-13")]
    public void A13_Compose_Mask_AttributeMatchesFluent()
        => AssertEquivalent(A13MaskAttr, A13MaskFluent, "A-13-mask", "Map_E13MSource_To_E13MTargetDto");
}
