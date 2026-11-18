using System.Linq;
using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// c) 红测（修前证红件——① 空段索引崩溃）：
/// 空段/无效成员头形态（<c>.Member(d =&gt; 5)</c> 字面量族 / <c>d?.X</c> F05 / <c>d.Method()</c> F07）
/// × 主链（MapTree 等终端）/ 块内（FromSource 块）站点——提取器 <c>CheckMemberOmittedSource</c>
/// / <c>CheckBlockOmittedSource</c> 直索引 <c>Segments[0]</c> 抛 ArgumentOutOfRangeException，
/// driver 侧观测形态 = CS8785 生成器 transform 异常 + 该配置器整链零生成（探针实证见
/// 静态推导成立——原"AD0001"表述按实测订正为 CS8785，差异登记）。
/// 断言方向 = 修复后期望态（修前必红）：无 CS8785 + 段开既有诊断如约 + 无 F69 叠加 +
/// 同配置器其余合法声明照常生成。转绿点 =（G1 段锚有效性共享判定）；
/// 锁定族扩面（四变换 × 8 站点 + 写入侧）归 b 步。
/// </summary>
public class FluentEmptySegmentCrashRedTests
{
    /// <summary>族一·主链：空段字面量 + MapTree 终端——不崩溃、无 F69、同配置器合法声明照常生成。</summary>
    [Fact]
    public void EmptySegmentLiteral_MainChainMapTree_NoCrash_LegalSiblingGenerated()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<E, D>().Member(d => 5).MapTree();
                        cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
                    }
            """));
        AssertNoGeneratorCrash(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>族一·主链：空段二元式（<c>5 + 1</c>）——同族形态第二例，断言同上。</summary>
    [Fact]
    public void EmptySegmentBinary_MainChainMapTree_NoCrash_LegalSiblingGenerated()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<E, D>().Member(d => 5 + 1).MapTree();
                        cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
                    }
            """));
        AssertNoGeneratorCrash(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>族二·主链：空段 + 段开有诊断（F05 null 条件访问）——F05 如约单报、无 F69、不崩溃。</summary>
    [Fact]
    public void EmptySegmentNullConditional_MainChain_F05Emitted_NoCrash()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<E, D>().Member(d => d.Address?.City).MapTree();
                        cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
                    }
            """));
        AssertNoGeneratorCrash(result);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F05");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>族二·主链：空段 + 段开有诊断（F07 方法调用）——F07 如约单报、无 F69、不崩溃。</summary>
    [Fact]
    public void EmptySegmentMethodCall_MainChain_F07Emitted_NoCrash()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<E, D>().Member(d => d.GetName()).MapTree();
                        cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
                    }
            """));
        AssertNoGeneratorCrash(result);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F07");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>族一·块内：FromSource 块内空段 + MapTree——不崩溃、无 F69、块外合法声明照常。</summary>
    [Fact]
    public void EmptySegment_BlockMapTree_NoCrash_LegalSiblingGenerated()
    {
        var result = TestHelper.GenerateMapper(Source(
            """
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<E, D2>()
                            .Member(d => d.Name).From(s => s.Name)
                            .Member(d => d.Name).FromSource<Sub>(c => { c.Member(x => 5).MapTree(); });
                    }
            """));
        AssertNoGeneratorCrash(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    // ─────────────────────────── 承载 fixture 与观测辅助 ───────────────────────────

    /// <summary>CS8785 = driver 对生成器 transform 异常的归一诊断（探针实测形态）；修前必现、修后必零。</summary>
    private static void AssertNoGeneratorCrash(MapperGenerationResult result)
    {
        var crash = result.Diagnostics.Where(d => d.Id == "CS8785").ToList();
        Assert.Empty(crash);
        Assert.NotEmpty(result.GeneratedCode); // 整链零生成的反向锚
    }

    private static string Source(string configureBody) => $$"""
        using NCMapper;
        namespace T1Red1
        {
            public class E { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class Addr { public string City { get; set; } = ""; }
            public class D { public string Name { get; set; } = ""; public int Age { get; set; } public string GetName() => Name; public Addr? Address { get; set; } public string? City { get; set; } }
            public class D2 { public string Name { get; set; } = ""; }
            public class Sub { public string Name { get; set; } = ""; }

            [MapperConfig]
            public static class M
            {
        {{configureBody}}
            }
        }
        """;
}
