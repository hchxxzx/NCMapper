// A-25 对级白名单等价对（FluentOnly → A-25 Both 转类闭环）。
// 等价口径 = L1 快照字段级（NoAutoInclude 承载字段自本批起参与比较——SnapshotFieldRegistry 顶层行
//   Stripped→Compared 翻转，迁出条件"Fluent 落地对级 NoAutoMatch 改名等价物"由 [MapNoAutoMatch] 兑现）
//   + L2 指定方法体文本级（单方法，非全产物）+ 运行时行为面断言（生成产物层四象限 / 反向仅翻转显式 / E5 写回段负例 /
//   NCMAP007 双通道同零）。
// 断言强度基准 = FluentSmokePoCTests Pair A（CustomerName 同名未声明 → "" / OrderId 显式 ✓ /
//   反向 entity.CustomerName == ""）+ A-18 模板（EquivalenceTests.PropertyNoAutoInclude.cs）。
// 假绿防线（方案书证伪 F5 / 变异清单第 ③ 条）：A25_FieldCarry_ParticipatesInL1Comparison
//   直证该字段不再被剥离且差异可被比较器检出——回退 SnapshotFieldRegistry 翻转即红。
// 开工探针结论（④）：生成期不发射 (Dto,Entity) 反向注册条目，反向语义经
//   ReversePlanner 生成期透传 + 本 fixture 反向方法体断言承载（不期望注册表反向条目）。
using System;
using System.Linq;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-25 NoAutoMatch — Attribute <c>[MapNoAutoMatch]</c> vs Fluent <c>cfg.For&lt;S,T&gt;().NoAutoMatch()</c>
/// （双通道对级白名单等价，第十一批能力转类）。
/// </summary>
public class EquivalenceTestsNoAutoMatch : EquivalenceFixtureBase
{
    // ================================================================
    // 夹具源码（双通道同形状：显式声明 Declared/Alias(←Renamed)，未声明 Id/CustomerName/AddressCity）
    // ================================================================

    /// <summary>特性通道：[MapFrom] + [MapNoAutoMatch] + 两条 [MapProperty] 显式声明。</summary>
    private const string AttrSourceMain = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A25
        {
            public class A25Address { public string City { get; set; } = ""; }
            public class A25Entity
            {
                public int Id { get; set; }
                public string Declared { get; set; } = "";
                public string Renamed { get; set; } = "";
                public string CustomerName { get; set; } = "";
                public A25Address Address { get; set; } = new();
            }

            [MapFrom(typeof(A25Entity))]
            [MapNoAutoMatch]
            public class A25Dto
            {
                public int Id { get; set; }
                [MapProperty(typeof(A25Entity), nameof(A25Entity.Declared))]
                public string Declared { get; set; } = "";
                [MapProperty(typeof(A25Entity), nameof(A25Entity.Renamed))]
                public string Alias { get; set; } = "";
                public string CustomerName { get; set; } = "";
                public string AddressCity { get; set; } = "";
            }
        }
        """;

    /// <summary>Fluent 通道：.NoAutoMatch() + 两条 .Member().From() 显式声明（无 [MapFrom]）。</summary>
    private const string FluentSource = """
        using NCMapper;
        namespace EqPairs.A25
        {
            public class A25Address { public string City { get; set; } = ""; }
            public class A25Entity
            {
                public int Id { get; set; }
                public string Declared { get; set; } = "";
                public string Renamed { get; set; } = "";
                public string CustomerName { get; set; } = "";
                public A25Address Address { get; set; } = new();
            }
            public class A25Dto
            {
                public int Id { get; set; }
                public string Declared { get; set; } = "";
                public string Alias { get; set; } = "";
                public string CustomerName { get; set; } = "";
                public string AddressCity { get; set; } = "";
            }

            [MapperConfig]
            public class A25Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A25Entity, A25Dto>()
                       .Member(d => d.Declared).From(s => s.Declared)
                       .Member(d => d.Alias).From(s => s.Renamed)
                       .NoAutoMatch();
                }
            }
        }
        """;

    /// <summary>负例夹具：Fluent 侧未开白名单（其余同 FluentSource）——L1 须检出 NoAutoInclude 字段级差异。</summary>
    private const string FluentSourceNoWhitelist = """
        using NCMapper;
        namespace EqPairs.A25
        {
            public class A25Address { public string City { get; set; } = ""; }
            public class A25Entity
            {
                public int Id { get; set; }
                public string Declared { get; set; } = "";
                public string Renamed { get; set; } = "";
                public string CustomerName { get; set; } = "";
                public A25Address Address { get; set; } = new();
            }
            public class A25Dto
            {
                public int Id { get; set; }
                public string Declared { get; set; } = "";
                public string Alias { get; set; } = "";
                public string CustomerName { get; set; } = "";
                public string AddressCity { get; set; } = "";
            }

            [MapperConfig]
            public class A25Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A25Entity, A25Dto>()
                       .Member(d => d.Declared).From(s => s.Declared)
                       .Member(d => d.Alias).From(s => s.Renamed);
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A25Entity_To_A25Dto";
    private const string ReverseMapMethodName = "Map_A25Dto_To_A25Entity";

    // ================================================================
    // ① 等价主断言：L1 快照字段级 + L2 指定方法体文本级（单方法，非全产物）
    // ================================================================

    [Fact]
    [EqPair("A-25")]
    [Trait("EqPair", "A-25")]
    public void A25_NoAutoMatch_AttributeChannel_Matches_FluentChannel()
    {
        AssertEquivalent(AttrSourceMain, FluentSource, "A-25", MapMethodName);
    }

    // ================================================================
    // ② 语义四象限（生成产物层，Pair A 断言模板）
    // ================================================================

    /// <summary>
    /// 正向四象限：显式同名（Declared）/ 显式异名（Alias←Renamed）映射 ✓；同名未声明（CustomerName）
    /// 不接；扁平化候选未声明（AddressCity）不拼；未声明（Id）归 default。
    /// </summary>
    [Fact]
    [Trait("EqPair", "A-25")]
    public void A25_Forward_GeneratedBody_QuadrantSemantics()
    {
        var body = TestHelper.GenerateMapper(AttrSourceMain).Methods[MapMethodName][0].Body;

        Assert.Contains("Declared = src.Declared", body, StringComparison.Ordinal);   // 显式同名 ✓
        Assert.Contains("Alias = src.Renamed", body, StringComparison.Ordinal);       // 显式异名 ✓
        Assert.DoesNotContain("CustomerName", body);                                  // 同名未声明 → 不接
        Assert.DoesNotContain("AddressCity", body);                                   // 扁平化不拼
        Assert.DoesNotContain("Id = src.Id", body);                                   // 未声明 → 归 default
    }

    /// <summary>
    /// 反向仅翻转正向显式声明（D4/S-3a）：Declared/Renamed 写回 ✓；CustomerName/Id 不写回；
    /// E5 写回段负例——未声明扁平化（AddressCity → Address.City）不折叠写回。
    /// </summary>
    [Fact]
    [Trait("EqPair", "A-25")]
    public void A25_Reverse_GeneratedBody_OnlyExplicitFlipped_AndNoDerivedFlattenWriteBack()
    {
        var reverse = TestHelper.GenerateMapper(AttrSourceMain).Methods[ReverseMapMethodName][0].Body;

        Assert.Contains("Declared = src.Declared", reverse, StringComparison.Ordinal);
        Assert.Contains("Renamed = src.Alias", reverse, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerName", reverse);   // 自动匹配不参与反向
        Assert.DoesNotContain("Id = src.Id", reverse);
        // E5 负例：未声明扁平化不写回（生成端反向写回段与运行时 ExpressionBuilder.Paths 同契约）
        Assert.DoesNotContain("AddressCity", reverse);
        Assert.DoesNotContain("Address.City", reverse);
    }

    // ================================================================
    // ③ 候选面：未声明属性不入候选（门位在 AllTargetProperties.Add 之前）
    // ================================================================

    /// <summary>
    /// 候选面集合相等（评审 002-P2-7 断言具体化）：特性侧与 Fluent 侧 <c>AllTargetProperties</c>
    /// 集合相等且仅含两条显式声明目标属性——未声明（Id/CustomerName/AddressCity）不入候选，
    /// 故 AllTargetSlots 发射面 / 反向候选 SourceProperties / 快照相等性三面同源一致。
    /// </summary>
    [Fact]
    [Trait("EqPair", "A-25")]
    public void A25_CandidateFace_AttrEqualsFluent_ExcludesUndeclared()
    {
        var attrNames = AllTargetPropertyNames(AttrSourceMain, fluentChannel: false);
        var fluentNames = AllTargetPropertyNames(FluentSource, fluentChannel: true);

        Assert.Equal(attrNames, fluentNames);
        Assert.Equal(new[] { "Alias", "Declared" }, attrNames);
    }

    // ================================================================
    // ④ 字段层等价直证（假绿防线 F5 / 变异清单 ③ 敏感点）
    // ================================================================

    /// <summary>
    /// NoAutoInclude 承载字段参与 L1 比较（SnapshotFieldRegistry 顶层行 Stripped→Compared 翻转直证）：
    /// ①注册表角色 = Compared；②比较器剥离集不含该字段；③比较器对"特性侧开 × Fluent 侧未开"负例
    /// 检出该字段级差异。回退翻转 → 本用例三点全红（字段层等价缺证 = F5 假绿形态）。
    /// </summary>
    [Fact]
    [Trait("EqPair", "A-25")]
    public void A25_FieldCarry_ParticipatesInL1Comparison()
    {
        var row = Assert.Single(SnapshotFieldRegistry.MappingSnapshot, r => r.Field == "NoAutoInclude");
        Assert.Equal(FieldRole.Compared, row.Role);

        Assert.DoesNotContain("NoAutoInclude", SnapshotEquivalenceComparer.TopLevelDenyList);

        // 负例：特性侧开白名单 × Fluent 侧未开 → L1 须检出 NoAutoInclude 字段级差异
        //（该字段若为 Stripped，两侧差异均不可见 → 本断言红 = F5 假绿防线）
        AssertL1Difference(AttrSourceMain, FluentSourceNoWhitelist, "A-25", "NoAutoInclude");
    }

    // ================================================================
    // ⑤ NCMAP007 双通道同零（D6 语义级抑制）
    // ================================================================

    /// <summary>
    /// 白名单对显式开 ReportUnmappedSourceMembers 时 NCMAP007 零产出（双通道同零——语义级抑制：
    /// 白名单下"未映射源成员"是声明意图而非缺陷信号）；对照组（白名单关）照常产出，排除恒绿。
    /// </summary>
    [Fact]
    [Trait("EqPair", "A-25")]
    public void A25_NCMAP007_SuppressedOnBothChannels_WithControl()
    {
        const string attrOn = """
            using NCMapper;
            using NCMapper.Attributes;
            [assembly: NCMapperOptions(ReportUnmappedSourceMembers = true)]
            namespace EqPairs.A25Opt
            {
                public class A25oEntity { public int Id { get; set; } public string Extra { get; set; } = ""; }
                [MapFrom(typeof(A25oEntity))]
                [MapNoAutoMatch]
                public class A25oDto
                {
                    [MapProperty(typeof(A25oEntity), nameof(A25oEntity.Id))]
                    public int Id { get; set; }
                }
            }
            """;
        const string attrOff = """
            using NCMapper;
            using NCMapper.Attributes;
            [assembly: NCMapperOptions(ReportUnmappedSourceMembers = true)]
            namespace EqPairs.A25Opt
            {
                public class A25oEntity { public int Id { get; set; } public string Extra { get; set; } = ""; }
                [MapFrom(typeof(A25oEntity))]
                public class A25oDto
                {
                    [MapProperty(typeof(A25oEntity), nameof(A25oEntity.Id))]
                    public int Id { get; set; }
                }
            }
            """;
        const string fluentOn = """
            using NCMapper;
            [assembly: NCMapperOptions(ReportUnmappedSourceMembers = true)]
            namespace EqPairs.A25Opt
            {
                public class A25oEntity { public int Id { get; set; } public string Extra { get; set; } = ""; }
                public class A25oDto { public int Id { get; set; } }

                [MapperConfig]
                public class A25oMapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A25oEntity, A25oDto>()
                           .Member(d => d.Id).From(s => s.Id)
                           .NoAutoMatch();
                    }
                }
            }
            """;

        var aOn = TestHelper.GenerateMapper(attrOn);
        var fOn = TestHelper.GenerateMapper(fluentOn);
        var aOff = TestHelper.GenerateMapper(attrOff);

        Assert.DoesNotContain(aOn.Diagnostics, d => d.Id == "NCMAP007");
        Assert.DoesNotContain(fOn.Diagnostics, d => d.Id == "NCMAP007");
        // 对照组：白名单关 + 开报告 → NCMAP007 必产出（证明上述零产出由白名单抑制臂所致，非报告门未开）
        Assert.Contains(aOff.Diagnostics, d => d.Id == "NCMAP007");
    }
}
