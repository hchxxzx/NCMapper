using System;
using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-10 Convert / A-11 DirectionalIgnore / A-12 IsFromJson —
///（registry C-02→A-10 / C-04→A-11 / C-05→A-12 迁移，等价机制第二批能力转类——004 A-09 先例；
/// Fluent 终端更名 ConvertUsing→Convert，fixture 随迁 ConvertStatic 防与 A-04 撞名）。
/// <para>A-10 四形态（T0 常量 / T1 字段值 / T2 整源 / T3 组合+字段值 + T1Guarded Nullable 守卫）：
/// 特性侧 [Convert] vs Fluent .Convert 方法组——双侧共用 ResolveConverterMethod 形态匹配
/// 与 BuildStaticConverterCall 发射端（零分叉预期）；L1 pm 级三元组直接可比（Table 2），
/// PropertyRef 级 HasConvertUsing/三元组为通道输入镜像（Table 4 stripped 组）。</para>
/// <para>A-11 方向性忽略：特性侧 [MapIgnore(Direction)] vs Fluent .Member(dest).Ignore(Direction)——
/// 方向语义经 Fluent 通道 IsFluentForward 标记补齐（执行期发现缺口）后正反向行为对齐。</para>
/// <para>A-12 JSON 展开：特性侧 [MapProperty(IsFromJson=true)] vs Fluent .Member(dest).FromJson()——
/// pm.IsFromJson 双侧置位（Table 2 直接比较）。</para>
/// </summary>
public class EquivalenceTestsConvertStatic : EquivalenceFixtureBase
{
    // ================================================================
    // A-10 Convert 四形态
    // ================================================================

    // T1 字段值（最常见）：int → string 跨类型
    private const string A10T1Attr = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A10
        {
            public class E10T1Source { public int Age { get; set; } public string Name { get; set; } = ""; }

            [MapFrom(typeof(E10T1Source))]
            public class E10T1TargetDto
            {
                [Convert(typeof(E10T1Conv), nameof(E10T1Conv.ToBand))]
                public string Age { get; set; } = "";
                public string Name { get; set; } = "";
            }

            public static class E10T1Conv
            {
                public static string ToBand(int fieldValue) => fieldValue < 18 ? "minor" : "adult";
            }
        }
        """;

    private const string A10T1Fluent = """
        using System;
        using NCMapper;
        namespace EqPairs.A10
        {
            public class E10T1Source { public int Age { get; set; } public string Name { get; set; } = ""; }
            public class E10T1TargetDto { public string Age { get; set; } = ""; public string Name { get; set; } = ""; }

            [MapperConfig]
            public class E10T1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<E10T1Source, E10T1TargetDto>().Member(d => d.Age).Convert<int>(E10T1Conv.ToBand);
                }
            }

            public static class E10T1Conv
            {
                public static string ToBand(int fieldValue) => fieldValue < 18 ? "minor" : "adult";
            }
        }
        """;

    // T0 无参常量
    private const string A10T0Attr = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A10
        {
            public class E10T0Source { public string Stamp { get; set; } = ""; }

            [MapFrom(typeof(E10T0Source))]
            public class E10T0TargetDto
            {
                [Convert(typeof(E10T0Conv), nameof(E10T0Conv.Stamp))]
                public string Stamp { get; set; } = "";
            }

            public static class E10T0Conv
            {
                public static string Stamp() => "fixed-value";
            }
        }
        """;

    private const string A10T0Fluent = """
        using System;
        using NCMapper;
        namespace EqPairs.A10
        {
            public class E10T0Source { public string Stamp { get; set; } = ""; }
            public class E10T0TargetDto { public string Stamp { get; set; } = ""; }

            [MapperConfig]
            public class E10T0Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<E10T0Source, E10T0TargetDto>().Member(d => d.Stamp).Convert(E10T0Conv.Stamp);
                }
            }

            public static class E10T0Conv
            {
                public static string Stamp() => "fixed-value";
            }
        }
        """;

    // T2 整源（单参 == 源实体类型——形态匹配判 T2，不断言重载选择）。
    // 模型全源成员消费（City/Street 入 DTO 自动匹配）——规避特性侧 NCMAP007 未映射源成员报告
    //（Fluent 通道无该报告，通道差异登记 D-8——等价对模型约定与其他 A 类 fixture 同口径）
    private const string A10T2Attr = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A10
        {
            public class E10T2Source { public string City { get; set; } = ""; public string Street { get; set; } = ""; public string FullAddress { get; set; } = ""; }

            [MapFrom(typeof(E10T2Source))]
            public class E10T2TargetDto
            {
                public string City { get; set; } = "";
                public string Street { get; set; } = "";
                [Convert(typeof(E10T2Conv), nameof(E10T2Conv.Build))]
                public string FullAddress { get; set; } = "";
            }

            public static class E10T2Conv
            {
                public static string Build(E10T2Source entity) => entity.City + ", " + entity.Street;
            }
        }
        """;

    private const string A10T2Fluent = """
        using System;
        using NCMapper;
        namespace EqPairs.A10
        {
            public class E10T2Source { public string City { get; set; } = ""; public string Street { get; set; } = ""; public string FullAddress { get; set; } = ""; }
            public class E10T2TargetDto { public string City { get; set; } = ""; public string Street { get; set; } = ""; public string FullAddress { get; set; } = ""; }

            [MapperConfig]
            public class E10T2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<E10T2Source, E10T2TargetDto>().Member(d => d.FullAddress).Convert(E10T2Conv.Build);
                }
            }

            public static class E10T2Conv
            {
                public static string Build(E10T2Source entity) => entity.City + ", " + entity.Street;
            }
        }
        """;

    // T3 组合+字段值（整源 + 字段值双入参）。模型全源成员消费（Locale 入 DTO——NCMAP007 口径同 T2）
    private const string A10T3Attr = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A10
        {
            public class E10T3Source { public string Locale { get; set; } = ""; public int Label { get; set; } }

            [MapFrom(typeof(E10T3Source))]
            public class E10T3TargetDto
            {
                public string Locale { get; set; } = "";
                [Convert(typeof(E10T3Conv), nameof(E10T3Conv.Build))]
                public string Label { get; set; } = "";
            }

            public static class E10T3Conv
            {
                public static string Build(E10T3Source entity, int fieldValue) => "v" + fieldValue + "@" + entity.Locale;
            }
        }
        """;

    private const string A10T3Fluent = """
        using System;
        using NCMapper;
        namespace EqPairs.A10
        {
            public class E10T3Source { public string Locale { get; set; } = ""; public int Label { get; set; } }
            public class E10T3TargetDto { public string Locale { get; set; } = ""; public string Label { get; set; } = ""; }

            [MapperConfig]
            public class E10T3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<E10T3Source, E10T3TargetDto>().Member(d => d.Label).Convert<int>(E10T3Conv.Build);
                }
            }

            public static class E10T3Conv
            {
                public static string Build(E10T3Source entity, int fieldValue) => "v" + fieldValue + "@" + entity.Locale;
            }
        }
        """;

    // T1Guarded Nullable 守卫（源 DateTime? + converter 入参 DateTime）
    private const string A10GAttr = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A10
        {
            public class E10GSource { public DateTime? CreatedAt { get; set; } }

            [MapFrom(typeof(E10GSource))]
            public class E10GTargetDto
            {
                [Convert(typeof(E10GConv), nameof(E10GConv.Format))]
                public string CreatedAt { get; set; } = "";
            }

            public static class E10GConv
            {
                public static string Format(DateTime fieldValue) => fieldValue.ToString("yyyy-MM-dd");
            }
        }
        """;

    private const string A10GFluent = """
        using System;
        using NCMapper;
        namespace EqPairs.A10
        {
            public class E10GSource { public DateTime? CreatedAt { get; set; } }
            public class E10GTargetDto { public string CreatedAt { get; set; } = ""; }

            [MapperConfig]
            public class E10GMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<E10GSource, E10GTargetDto>().Member(d => d.CreatedAt).Convert<DateTime>(E10GConv.Format);
                }
            }

            public static class E10GConv
            {
                public static string Format(DateTime fieldValue) => fieldValue.ToString("yyyy-MM-dd");
            }
        }
        """;

    [Fact]
    [EqPair("A-10")]
    [Trait("EqPair", "A-10")]
    public void A10_Convert_T1FieldValue_AttributeMatchesFluent()
        => AssertEquivalent(A10T1Attr, A10T1Fluent, "A-10-T1", "Map_E10T1Source_To_E10T1TargetDto");

    [Fact]
    [EqPair("A-10")]
    [Trait("EqPair", "A-10")]
    public void A10_Convert_T0Constant_AttributeMatchesFluent()
        => AssertEquivalent(A10T0Attr, A10T0Fluent, "A-10-T0", "Map_E10T0Source_To_E10T0TargetDto");

    [Fact]
    [EqPair("A-10")]
    [Trait("EqPair", "A-10")]
    public void A10_Convert_T2WholeSource_AttributeMatchesFluent()
        => AssertEquivalent(A10T2Attr, A10T2Fluent, "A-10-T2", "Map_E10T2Source_To_E10T2TargetDto");

    [Fact]
    [EqPair("A-10")]
    [Trait("EqPair", "A-10")]
    public void A10_Convert_T3Combined_AttributeMatchesFluent()
        => AssertEquivalent(A10T3Attr, A10T3Fluent, "A-10-T3", "Map_E10T3Source_To_E10T3TargetDto");

    [Fact]
    [EqPair("A-10")]
    [Trait("EqPair", "A-10")]
    public void A10_Convert_T1Guarded_AttributeMatchesFluent()
        => AssertEquivalent(A10GAttr, A10GFluent, "A-10-G", "Map_E10GSource_To_E10GTargetDto");

    [Fact]
    [EqPair("A-10")]
    [Trait("EqPair", "A-10")]
    public void A10_Convert_Mixed_With_AutoMatch()
        => AssertEquivalent(A10T1Attr, A10T1Fluent, "A-10-mixed", "Map_E10T1Source_To_E10T1TargetDto");

    // ================================================================
    // A-11 DirectionalIgnore（[MapIgnore(Direction)] vs .Member(dest).Ignore(Direction)）
    // ================================================================

    // ToDto：正向跳过 + 反向保留（双向行为一致性是本对核心——IsFluentForward 方向补齐的验收面）
    private const string A11Attr = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A11
        {
            public class E11Source { public int Id { get; set; } public string Secret { get; set; } = ""; }

            [MapFrom(typeof(E11Source))]
            public class E11TargetDto
            {
                public int Id { get; set; }
                [MapIgnore(Direction = Direction.ToDto)]
                public string Secret { get; set; } = "";
            }
        }
        """;

    private const string A11Fluent = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A11
        {
            public class E11Source { public int Id { get; set; } public string Secret { get; set; } = ""; }
            public class E11TargetDto { public int Id { get; set; } public string Secret { get; set; } = ""; }

            [MapperConfig]
            public class E11Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<E11Source, E11TargetDto>().Member(d => d.Secret).Ignore(Direction.ToDto);
                }
            }
        }
        """;

    [Fact]
    [EqPair("A-11")]
    [Trait("EqPair", "A-11")]
    public void A11_DirectionalIgnore_ToDto_AttributeMatchesFluent_Forward()
        => AssertEquivalent(A11Attr, A11Fluent, "A-11-fwd", "Map_E11Source_To_E11TargetDto");

    [Fact]
    [EqPair("A-11")]
    [Trait("EqPair", "A-11")]
    public void A11_DirectionalIgnore_ToDto_AttributeMatchesFluent_Reverse()
        => AssertEquivalent(A11Attr, A11Fluent, "A-11-rev", "Map_E11TargetDto_To_E11Source");

    // ================================================================
    // A-12 IsFromJson（[MapProperty(IsFromJson=true)] vs .Member(dest).FromJson()）
    // ================================================================

    private const string A12Attr = """
        using System;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A12
        {
            public class E12Source { public int Id { get; set; } public string Payload { get; set; } = ""; }

            [MapFrom(typeof(E12Source))]
            public class E12TargetDto
            {
                public int Id { get; set; }
                [MapProperty(typeof(E12Source), nameof(E12Source.Payload), IsFromJson = true)]
                public E12Data Payload { get; set; } = new();
            }

            public class E12Data { public string Name { get; set; } = ""; }
        }
        """;

    private const string A12Fluent = """
        using System;
        using NCMapper;
        namespace EqPairs.A12
        {
            public class E12Source { public int Id { get; set; } public string Payload { get; set; } = ""; }
            public class E12TargetDto { public int Id { get; set; } public E12Data Payload { get; set; } = new(); }

            [MapperConfig]
            public class E12Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<E12Source, E12TargetDto>().Member(d => d.Payload).FromJson();
                }
            }

            public class E12Data { public string Name { get; set; } = ""; }
        }
        """;

    [Fact]
    [EqPair("A-12")]
    [Trait("EqPair", "A-12")]
    public void A12_IsFromJson_AttributeMatchesFluent()
        => AssertEquivalent(A12Attr, A12Fluent, "A-12", "Map_E12Source_To_E12TargetDto");

    [Fact]
    [EqPair("A-12")]
    [Trait("EqPair", "A-12")]
    public void A12_IsFromJson_Mixed_With_AutoMatch()
        => AssertEquivalent(A12Attr, A12Fluent, "A-12-mixed", "Map_E12Source_To_E12TargetDto");
}
