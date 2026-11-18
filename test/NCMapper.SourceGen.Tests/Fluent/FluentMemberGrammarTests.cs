using System.Linq;
using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// （「语法合法矩阵」行）：
/// Member 三段式**合法**语法形态的生成端承载——
/// ①头声明 3 种（From 显式来源 / FromSource 速记 / Ignore 忽略出口）；
/// ②配置段组合（Mask / Format / FromJson / MapTree / Convert / ConvertBack / MapWhen / NoAutoInclude）；
/// ③来源可省（省略头声明时按目标字段名解析同名源属性）；
/// ④Ignore 独唱终态 + 唯一例外（AfterIgnore.NoAutoInclude 可链）。
/// 每形态 ≥1 happy path。纪律：断言绑定生成产物 + 零诊断面（不依赖提取器内部结构），
/// 与既有 Fluent 族测试同口径（TestHelper.GenerateMapper）。
/// </summary>
public class FluentMemberGrammarTests
{
    // ═══════════════════ ① 头声明三形态 ═══════════════════

    /// <summary>头声明形态一：From 显式来源（异名）→ 正向赋值取显式源路径。</summary>
    [Fact]
    public void HeadFrom_ExplicitSource_EmitsAssignment()
    {
        var source = """
            using NCMapper;
            namespace Tc2Gm1
            {
                public class G1Entity { public string Name { get; set; } = ""; }
                public class G1Dto { public string FullName { get; set; } = ""; }

                [MapperConfig]
                public static class G1Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G1Entity, G1Dto>().Member(d => d.FullName).From(s => s.Name);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("Map_G1Entity_To_G1Dto(", result.GeneratedCode);
        Assert.Contains("dst.FullName = src.Name;", result.GeneratedCode);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>头声明形态二：FromSource 速记（跨型子源）→ Compose 声明承载（多源合成路径）。</summary>
    [Fact]
    public void HeadFromSourceShorthand_EmitsComposeRegistration()
    {
        var source = """
            using NCMapper;
            namespace Tc2Gm2
            {
                public class G2Entity { public string Name { get; set; } = ""; }
                public class G2Sub { public string FullName { get; set; } = ""; }
                public class G2Dto { public string Name { get; set; } = ""; public string FullName { get; set; } = ""; }

                [MapperConfig]
                public static class G2Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G2Entity, G2Dto>().Member(d => d.FullName).FromSource<G2Sub>(s => s.FullName);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 跨源字段不入正向单源映射体（Compose 分流）；产物为双向（正向+反向段），断言锚定正向方法体
        // （先例 FluentChannelScenariosTests 两对链口径——全文级会误命中反向/Compose 段）
        Assert.DoesNotContain("dst.FullName =", Assert.Single(result.Methods["Map_G2Entity_To_G2Dto"]).Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        Assert.Contains("Map_G2Entity_To_G2Dto(", result.GeneratedCode);
    }

    /// <summary>头声明形态三：Ignore 独唱终态 → 该字段不赋值，链上其余声明不受影响。</summary>
    [Fact]
    public void HeadIgnore_SoloTerminal_SkipsAssignment()
    {
        var source = """
            using NCMapper;
            namespace Tc2Gm3
            {
                public class G3Entity { public string Name { get; set; } = ""; public string Secret { get; set; } = ""; }
                public class G3Dto { public string Name { get; set; } = ""; public string Secret { get; set; } = ""; }

                [MapperConfig]
                public static class G3Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G3Entity, G3Dto>()
                             .Member(d => d.Name).From(s => s.Name)
                             .Member(d => d.Secret).Ignore();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("dst.Name = src.Name;", result.GeneratedCode);
        // 方法体级断言：产物为双向（正向+反向段），Fluent Ignore 方向语义只抑制正向——
        // 全文级 DoesNotContain("Secret =") 会误命中反向段（先例 FluentChannelScenariosTests :420-421 口径）
        Assert.DoesNotContain("dst.Secret =", Assert.Single(result.Methods["Map_G3Entity_To_G3Dto"]).Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    // ═══════════════════ ② 配置段组合（变换槽） ═══════════════════

    /// <summary>配置段 Mask（来源可省——同名解析）：脱敏管线发射。</summary>
    [Fact]
    public void ConfigMask_OmittedSource_ResolvesSameName()
    {
        var source = """
            using NCMapper;
            namespace Tc2Gm4
            {
                public class G4Entity { public string Phone { get; set; } = ""; }
                public class G4Dto { public string Phone { get; set; } = ""; }

                [MapperConfig]
                public static class G4Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G4Entity, G4Dto>().Member(d => d.Phone).Mask<MaskPhone>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("MaskProvider.Resolve", result.GeneratedCode);
        Assert.Contains("__mask_", result.GeneratedCode);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>配置段 Format（来源可省）：IFormattable.ToString(format, null) 发射。</summary>
    [Fact]
    public void ConfigFormat_OmittedSource_EmitsToStringFormat()
    {
        var source = """
            using System;
            using NCMapper;
            namespace Tc2Gm5
            {
                public class G5Entity { public DateTime CreatedAt { get; set; } }
                public class G5Dto { public string CreatedAt { get; set; } = ""; }

                [MapperConfig]
                public static class G5Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G5Entity, G5Dto>().Member(d => d.CreatedAt).Format("yyyy-MM-dd");
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(".ToString(\"yyyy-MM-dd\", null)", result.GeneratedCode);
        // F67（Format 不可反解→反向跳过提示，warning 级）为该形态既有预期诊断面
        // （先例 MapFormatGenerationTests.FluentFormat_ReverseDerivation_EmitsF67）——合法性断言只锁 error 级 F 族
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>配置段 FromJson（来源可省）：JSON 反序列化发射。</summary>
    [Fact]
    public void ConfigFromJson_OmittedSource_EmitsJsonDeserialize()
    {
        var source = """
            using NCMapper;
            namespace Tc2Gm6
            {
                public class G6Payload { public int Id { get; set; } }
                public class G6Entity { public string Payload { get; set; } = ""; }
                public class G6Dto { public G6Payload Payload { get; set; } = new(); }

                [MapperConfig]
                public static class G6Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G6Entity, G6Dto>().Member(d => d.Payload).FromJson();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("Payload", result.GeneratedCode);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>配置段 MapTree（来源可省）：递归映射发射（属性级递归，与对级 Tree() 互补）。</summary>
    [Fact]
    public void ConfigMapTree_OmittedSource_EmitsRecursion()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper;
            namespace Tc2Gm7
            {
                public class G7Entity { public string Name { get; set; } = ""; public List<G7Entity> Children { get; set; } = new(); }
                public class G7Dto { public string Name { get; set; } = ""; public List<G7Dto> Children { get; set; } = new(); }

                [MapperConfig]
                public static class G7Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G7Entity, G7Dto>().Member(d => d.Children).MapTree();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("Map_G7Entity_To_G7Dto(", result.GeneratedCode);
        Assert.Contains("Children", result.GeneratedCode);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>配置段 Convert（T1 字段值方法组，显式类型参）：转换器静态调用发射。</summary>
    [Fact]
    public void ConfigConvertT1_MethodGroup_EmitsConverterCall()
    {
        var source = """
            using NCMapper;
            namespace Tc2Gm8
            {
                public class G8Entity { public int Age { get; set; } }
                public class G8Dto { public string AgeBand { get; set; } = ""; }
                public static class G8Conv { public static string ToBand(int v) => v < 18 ? "minor" : "adult"; }

                [MapperConfig]
                public static class G8Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G8Entity, G8Dto>().Member(d => d.AgeBand).From(s => s.Age).Member(d => d.AgeBand).Convert<int>(G8Conv.ToBand);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("G8Conv.ToBand", result.GeneratedCode);
    }

    /// <summary>配置段 MapWhen（T1 字段值内联谓词）：条件守卫发射。</summary>
    [Fact]
    public void ConfigMapWhen_T1Inline_EmitsGuard()
    {
        var source = """
            using NCMapper;
            namespace Tc2Gm9
            {
                public class G9Entity { public string Name { get; set; } = ""; }
                public class G9Dto { public string Name { get; set; } = ""; }

                [MapperConfig]
                public static class G9Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G9Entity, G9Dto>().Member(d => d.Name).From(s => s.Name).Member(d => d.Name).MapWhen(v => v.Length > 0);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("Map_When_", result.GeneratedCode);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>配置段 NoAutoInclude（字段版，来源可省）：属性级注册表承载发射。</summary>
    [Fact]
    public void ConfigNoAutoInclude_FieldLevel_EmitsRegistry()
    {
        var source = """
            using NCMapper;
            namespace Tc2Gm10
            {
                public class G10Entity { public string Name { get; set; } = ""; public G10Nav Audit { get; set; } = new(); }
                public class G10Nav { public string Note { get; set; } = ""; }
                public class G10Dto { public string Name { get; set; } = ""; public G10Nav Audit { get; set; } = new(); }

                [MapperConfig]
                public static class G10Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G10Entity, G10Dto>().Tree().Member(d => d.Audit).NoAutoInclude();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    // ═══════════════════ ③ 段间回外层（链式续写） ═══════════════════

    /// <summary>MemberConfig 继承对级：配置段后可直接续下一个 Member 段（链式续写合法）。</summary>
    [Fact]
    public void ConfigSegment_ReturnsToPairLevel_NextMemberChainLegal()
    {
        var source = """
            using NCMapper;
            namespace Tc2Gm11
            {
                public class G11Entity { public string Mobile { get; set; } = ""; public string Note { get; set; } = ""; }
                public class G11Dto { public string Phone { get; set; } = ""; public string Note { get; set; } = ""; }

                [MapperConfig]
                public static class G11Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G11Entity, G11Dto>()
                             .Member(d => d.Phone).From(s => s.Mobile).Member(d => d.Phone).Mask<MaskPhone>()
                             .Member(d => d.Note).From(s => s.Note);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("dst.Note = src.Note;", result.GeneratedCode);
        Assert.Contains("MaskProvider.Resolve", result.GeneratedCode);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    // ═══════════════════ ④ Ignore 唯一例外（AfterIgnore 挂点） ═══════════════════

    /// <summary>Ignore 独唱终态唯一例外：`.Member(dest).Ignore().NoAutoInclude()` 合法且承载属性级注册表。</summary>
    [Fact]
    public void IgnoreThenNoAutoInclude_UniqueException_ChainLegal()
    {
        var source = """
            using NCMapper;
            namespace Tc2Gm12
            {
                public class G12Entity { public string Name { get; set; } = ""; public G12Nav Audit { get; set; } = new(); }
                public class G12Nav { public string Note { get; set; } = ""; }
                public class G12Dto { public string Name { get; set; } = ""; public G12Nav Audit { get; set; } = new(); }

                [MapperConfig]
                public static class G12Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G12Entity, G12Dto>().Tree().Member(d => d.Audit).Ignore().NoAutoInclude();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 方法体级断言：Tree 导航段/反向段的 Audit 行为合法承载，Ignore 抑制只锚定正向方法体
        Assert.DoesNotContain("dst.Audit =", Assert.Single(result.Methods["Map_G12Entity_To_G12Dto"]).Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>对级新名 NoAutoMatch 经字段段继承面任意位置可调（MemberConfig : FluentTypeConfig）。</summary>
    [Fact]
    public void PairLevelNoAutoMatch_ReachableFromMemberSegment()
    {
        var source = """
            using NCMapper;
            namespace Tc2Gm13
            {
                public class G13Entity { public string Name { get; set; } = ""; }
                public class G13Dto { public string Name { get; set; } = ""; }

                [MapperConfig]
                public static class G13Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<G13Entity, G13Dto>().Member(d => d.Name).From(s => s.Name).NoAutoMatch();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("dst.Name = src.Name;", result.GeneratedCode);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }
}
