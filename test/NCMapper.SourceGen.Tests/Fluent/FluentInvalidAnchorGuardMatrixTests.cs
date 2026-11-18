using System.Linq;
using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// b) driver 诊断族——**无效段锚 × 变换终端 × 主链/块内站点**锁定矩阵
/// （消费实证形态与 G1/G2 修复面；新增测试矩阵 driver 段）。
/// <para>三族断言方向：</para>
/// <list type="number">
///   <item><description><b>空段两族</b>（字面量/二元式 = 段开无诊断；F05 null 条件访问 / F07 方法调用 =
///   段开有诊断）× 四变换（Mask/Format/FromJson/MapTree）× 主链/块内 = 8+4 站点：
///   无 CS8785（生成器不崩）+ 段开既有诊断如约 + **无 F69 叠加**（G1 段锚有效性单点判定）+
///   同配置器其余合法声明照常生成。</description></item>
///   <item><description><b>无效锚写入侧</b>（单段常量索引 <c>d.Items[0]</c> × 源同名/源无同名 ×
///   主链/块内）：F06 单报 + 无 F69 + <b>产物无该映射声明</b>（<c>__mask_</c> 字段零发射——G1 ②/③
///   写入门收口；缺陷态 = 声明照写 → <c>__mask_</c> 出现 → 红）。</description></item>
///   <item><description><b>无 F69 双报</b>（多段 dest / 索引段 / 空段 + 变换段）：段开诊断单报、
///   F69 零命中（防"跳过声明"与"缺源"两诊断叠加成双报）。</description></item>
/// </list>
/// <para>反向验证（NF1 防假绿）：正对照用例 <see cref="ValidAnchor_Mask_MainChain_GeneratesMaskField"/>
/// 锁定 <c>__mask_</c> 在位——若写入门被整体禁用，负向用例集体 vacuous pass，本对照先红。</para>
/// </summary>
public class FluentInvalidAnchorGuardMatrixTests
{
    // ═══════════════ 族一：空段（段开无诊断）× 四变换 × 主链 ═══════════════

    /// <summary>1/8 主链·空段 + Mask：不崩、无 F69、合法兄弟声明照常。</summary>
    [Fact]
    public void EmptySegment_MainChain_Mask_NoCrash_NoF69()
        => AssertEmptySegmentMainChain(".Member(d => 5).Mask<MaskPhone>();");

    /// <summary>2/8 主链·空段 + Format。</summary>
    [Fact]
    public void EmptySegment_MainChain_Format_NoCrash_NoF69()
        => AssertEmptySegmentMainChain(".Member(d => 5).Format(\"yyyy-MM-dd\");");

    /// <summary>3/8 主链·空段 + FromJson。</summary>
    [Fact]
    public void EmptySegment_MainChain_FromJson_NoCrash_NoF69()
        => AssertEmptySegmentMainChain(".Member(d => 5).FromJson();");

    /// <summary>4/8 主链·空段 + MapTree。</summary>
    [Fact]
    public void EmptySegment_MainChain_MapTree_NoCrash_NoF69()
        => AssertEmptySegmentMainChain(".Member(d => 5).MapTree();");

    // ═══════════════ 族一：空段 × 四变换 × 块内（FromSource 块）═══════════════

    /// <summary>5/8 块内·空段 + Mask。</summary>
    [Fact]
    public void EmptySegment_Block_Mask_NoCrash_NoF69()
        => AssertEmptySegmentBlock("c.Member(x => 5).Mask<MaskPhone>();");

    /// <summary>6/8 块内·空段 + Format。</summary>
    [Fact]
    public void EmptySegment_Block_Format_NoCrash_NoF69()
        => AssertEmptySegmentBlock("c.Member(x => 5).Format(\"N\");");

    /// <summary>7/8 块内·空段 + FromJson。</summary>
    [Fact]
    public void EmptySegment_Block_FromJson_NoCrash_NoF69()
        => AssertEmptySegmentBlock("c.Member(x => 5).FromJson();");

    /// <summary>8/8 块内·空段 + MapTree。</summary>
    [Fact]
    public void EmptySegment_Block_MapTree_NoCrash_NoF69()
        => AssertEmptySegmentBlock("c.Member(x => 5).MapTree();");

    // ═══════════════ 族二：空段 + 段开既有诊断（F05/F07）═══════════════

    /// <summary>族二·主链 F05（null 条件访问）+ Mask：F05 如约、无 F69、不崩。</summary>
    [Fact]
    public void NullConditional_MainChain_Mask_F05Single_NoF69()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    cfg.For<E, D>().Member(d => d.Address?.City).Mask<MaskPhone>();
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
            """));
        AssertNoGeneratorCrash(result);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F05");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>族二·主链 F07（方法调用）+ Format：F07 如约、无 F69、不崩。</summary>
    [Fact]
    public void MethodCallHead_MainChain_Format_F07Single_NoF69()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    cfg.For<E, D>().Member(d => d.GetName()).Format("N");
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
            """));
        AssertNoGeneratorCrash(result);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F07");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>族二·块内 F05 + MapTree：F05 如约、无 F69、块外合法声明照常。</summary>
    [Fact]
    public void NullConditional_Block_MapTree_F05Single_NoF69()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
                    cfg.For<E, D3>().FromSource<Sub>(c => { c.Member(x => x.Addr?.City).MapTree(); });
            """));
        AssertNoGeneratorCrash(result);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F05");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>族二·块内 F07 + Mask：F07 如约、无 F69、<c>__mask_</c> 零发射（声明被段开诊断拒收）。</summary>
    [Fact]
    public void MethodCallHead_Block_Mask_F07Single_NoF69_NoMaskField()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
                    cfg.For<E, D3>().FromSource<Sub>(c => { c.Member(x => x.GetName()).Mask<MaskPhone>(); });
            """));
        AssertNoGeneratorCrash(result);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F07");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.DoesNotContain("__mask_", result.GeneratedCode);
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    // ═══════════════ 无效锚写入侧：单段常量索引 × 源同名/无同名 × 主链/块内 ═══════════════

    /// <summary>写入侧·主链·索引段 + Mask（**源同名存在**）：F06 单报 + 无 F69 + 产物无 <c>__mask_</c>。</summary>
    [Fact]
    public void IndexerDest_MainChain_SameNameSource_Mask_F06Single_NoF69_NoMaskField()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    cfg.For<E, D>().Member(d => d.Items[0]).Mask<MaskPhone>();
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
            """));
        AssertNoGeneratorCrash(result);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F06");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.DoesNotContain("__mask_", result.GeneratedCode);
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>写入侧·主链·索引段 + Mask（**源无同名**）：F06 单报 + 无 F69（无效锚不叠加缺源诊断）。</summary>
    [Fact]
    public void IndexerDest_MainChain_NoSameNameSource_Mask_F06Single_NoF69_NoMaskField()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    cfg.For<E, D>().Member(d => d.Widgets[0]).Mask<MaskPhone>();
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
            """));
        AssertNoGeneratorCrash(result);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F06");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.DoesNotContain("__mask_", result.GeneratedCode);
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>写入侧·块内·索引段 + Mask：F06 单报 + 无 F69 + 产物无 <c>__mask_</c>。</summary>
    [Fact]
    public void IndexerDest_Block_Mask_F06Single_NoF69_NoMaskField()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
                    cfg.For<E, D3>().FromSource<Sub>(c => { c.Member(d => d.Items[0]).Mask<MaskPhone>(); });
            """));
        AssertNoGeneratorCrash(result);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F06");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.DoesNotContain("__mask_", result.GeneratedCode);
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>写入侧·块内·索引段 + MapTree：F06 单报 + 无 F69（块内写入门与主链同口径）。</summary>
    [Fact]
    public void IndexerDest_Block_MapTree_F06Single_NoF69()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
                    cfg.For<E, D3>().FromSource<Sub>(c => { c.Member(d => d.Items[0]).MapTree(); });
            """));
        AssertNoGeneratorCrash(result);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F06");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>
    /// 正对照（反向验证锚）：合法单段锚 + Mask → 无 F06/F69 且 <c>__mask_</c> 字段在位。
    /// <para>若写入门被整体禁用（错误实现），上面四条写入侧用例集体 vacuous pass——本用例先红。</para>
    /// </summary>
    [Fact]
    public void ValidAnchor_Mask_MainChain_GeneratesMaskField()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    cfg.For<E, D>().Member(d => d.Tag).Mask<MaskPhone>();
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
            """));
        AssertNoGeneratorCrash(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F06");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("__mask_", result.GeneratedCode);
        Assert.Contains("MaskProvider.Resolve", result.GeneratedCode);
    }

    // ═══════════════ ③ 无 F69 双报（多段 / 索引段 / 空段 + 变换段）═══════════════

    /// <summary>③·多段 dest + Mask：F55 单报（Warning，声明跳过）、F69 零命中。</summary>
    [Fact]
    public void MultiSegmentDest_WithMask_F55Single_NoF69()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    cfg.For<E, D>().Member(d => d.Inner.City).Mask<MaskPhone>();
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
            """));
        AssertNoGeneratorCrash(result);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F55");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.DoesNotContain("__mask_", result.GeneratedCode);
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>③·多段 dest + FromJson：F55 单报、F69 零命中（变换终端无关性）。</summary>
    [Fact]
    public void MultiSegmentDest_WithFromJson_F55Single_NoF69()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    cfg.For<E, D>().Member(d => d.Inner.City).FromJson();
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
            """));
        AssertNoGeneratorCrash(result);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F55");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>③·空段 + Convert（变换段带实参）：不崩、F69 零命中、合法兄弟照常。</summary>
    [Fact]
    public void EmptySegment_WithConvert_NoCrash_NoF69()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    cfg.For<E, D>().Member(d => 5).Convert<string>(GcConv.ToText);
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
            """));
        AssertNoGeneratorCrash(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    // ─────────────────────────── 承载 fixture 与观测辅助 ───────────────────────────

    private static void AssertEmptySegmentMainChain(string badDeclaration)
    {
        var result = TestHelper.GenerateMapper(Source(
            $$"""
                    cfg.For<E, D>{{badDeclaration}}
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
            """));
        AssertNoGeneratorCrash(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    private static void AssertEmptySegmentBlock(string badBlockDeclaration)
    {
        var result = TestHelper.GenerateMapper(Source(
            $$"""
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
                    cfg.For<E, D3>().FromSource<Sub>(c => { {{badBlockDeclaration}} });
            """));
        AssertNoGeneratorCrash(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>CS8785 = driver 对生成器 transform 异常的归一诊断（探针实测形态）。</summary>
    private static void AssertNoGeneratorCrash(MapperGenerationResult result)
    {
        Assert.Empty(result.Diagnostics.Where(d => d.Id == "CS8785").ToList());
        Assert.False(string.IsNullOrWhiteSpace(result.GeneratedCode),
            "先验锚失败：本次 fixture 未产出任何生成代码（提取管线未跑通，负向断言无判别力）");
    }

    private static string Source(string configureBody) => $$"""
        using NCMapper;
        using NCMapper.Attributes;
        using System.Collections.Generic;
        namespace T6Guard
        {
            public class E
            {
                public string Name { get; set; } = "";
                public string Tag { get; set; } = "";
                public List<string> Items { get; set; } = new();
                public Addr? Address { get; set; }
                public Inner? Sub { get; set; }
                public string GetName() => Name;
            }
            public class Addr { public string City { get; set; } = ""; }
            public class Inner { public string City { get; set; } = ""; }
            public class D
            {
                public string Name { get; set; } = "";
                public string Tag { get; set; } = "";
                public List<string> Items { get; set; } = new();
                public List<string> Widgets { get; set; } = new();
                public Addr? Address { get; set; }
                public Inner? Inner { get; set; }
                public string? City { get; set; }
            }
            public class D2 { public string Name { get; set; } = ""; }
            public class D3 { public string Name { get; set; } = ""; public List<string> Items { get; set; } = new(); public Addr? Addr { get; set; } public string GetName() => Name; }
            public class Sub
            {
                public string Name { get; set; } = "";
                public string Tag { get; set; } = "";
                public List<string> Items { get; set; } = new();
                public Addr? Addr { get; set; }
                public string GetName() => Name;
            }
            public class MaskPhone : IMask { public string Mask(string? value) => value + "***"; }
            public static class GcConv { public static string ToText(string v) => "t:" + v; }

            [MapperConfig]
            public static class M
            {
                public static void Configure(FluentMappingConfig cfg)
                {
        {{configureBody}}
                }
            }
        }
        """;
}
