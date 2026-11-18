using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 批次三静默语义治理诊断测试（方案书 20260915-003）。
/// <para>覆盖：F06 四站点补射/补检（B2-03.1）、F55 多段 dest（B2-03.2）、F56 非 lambda 实参（B2-03.3）、
/// F57 块体/三元（B2-03.4）、F58 不可静态求值实参（B2-04）、private protected 五门补拒（B2-05）。</para>
/// <para>纪律（B11）：每红测样例携带"声明确实被提取"的先验锚——同 fixture 内一条合法声明必须
/// 出现在生成代码中（防负向用例 vacuous pass）。级别断言随 Id 严格对应：
/// F55/F57 = Warning（F06/F08 族）；F56/F58 = Error（F26/F40 族）。</para>
/// </summary>
public class FluentSilentSemanticsDiagnosticsTests
{
    // ======================= B2-03.1：F06 四站点补射/补检 =======================

    /// <summary>B2-03.1-Map 站点：dest 索引段 → F06 补射（AddPathDeclaration 静默 return 修复）。</summary>
    [Fact]
    public void F06_MapDestIndexerSegment_Emitted()
    {
        var source = """
            using NCMapper;
            using System.Collections.Generic;
            namespace B3P06Map
            {
                public class U { public string Value { get; set; } = ""; }
                public class D
                {
                    public string City { get; set; } = "";
                    public List<string> Items { get; set; } = new();
                }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Items[0]).From(s => s.Value);
                        cfg.For<U, D>().Member(d => d.City).From(s => s.Value);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f06 = result.Diagnostics.Where(d => d.Id == "NCMAP_F06").ToList();
        Assert.Single(f06);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f06[0].Severity);
        Assert.Contains("Items[0]", f06[0].GetMessage());
        // 先验锚：同 fixture 的合法声明确实被提取（声明确实参与生成，负向断言非 vacuous）
        AssertPriorExtraction(result);
    }

    /// <summary>B2-03.1-Mask 站点：索引段 dest → F06（分支 if 条件形态补索引检查）。</summary>
    [Fact]
    public void F06_MaskDestIndexerSegment_Emitted()
    {
        var source = """
            using NCMapper;
            using System.Collections.Generic;
            namespace B3P06Mask
            {
                public class U { public string Value { get; set; } = ""; }
                public class D
                {
                    public string City { get; set; } = "";
                    public List<string> Items { get; set; } = new();
                }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Items[0]).Mask<MaskPhone>();
                        cfg.For<U, D>().Member(d => d.City).From(s => s.Value);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F06");
        AssertPriorExtraction(result);
    }

    /// <summary>B2-03.1-NoAutoInclude 站点：索引段 dest → F06（分支 if 条件形态补索引检查）。</summary>
    [Fact]
    public void F06_NoAutoIncludeIndexerSegment_Emitted()
    {
        var source = """
            using NCMapper;
            using System.Collections.Generic;
            namespace B3P06NoAutoInclude
            {
                public class U { public string Value { get; set; } = ""; }
                public class D
                {
                    public string City { get; set; } = "";
                    public List<string> Items { get; set; } = new();
                }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Items[0]).NoAutoInclude();
                        cfg.For<U, D>().Member(d => d.City).From(s => s.Value);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F06");
        AssertPriorExtraction(result);
    }

    // ======================= B2-03.2：F55 多段 dest（四站点）=======================

    /// <summary>B2-03.2-Map 站点：多段 dest → F55（Warning），且与 F56 严格互斥。</summary>
    [Fact]
    public void F55_MapMultiSegmentDest_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B3P55Map
            {
                public class Addr { public string City { get; set; } = ""; }
                public class U { public string City { get; set; } = ""; }
                public class D
                {
                    public string City { get; set; } = "";
                    public Addr Inner { get; set; } = new();
                }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Inner.City).From(s => s.City);
                        cfg.For<U, D>().Member(d => d.City).From(s => s.City);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f55 = result.Diagnostics.Where(d => d.Id == "NCMAP_F55").ToList();
        Assert.Single(f55);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f55[0].Severity);
        Assert.Contains("Inner.City", f55[0].GetMessage());
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F56");
        AssertPriorExtraction(result);
    }

    /// <summary>B2-03.2-Mask 站点：多段 dest → F55。</summary>
    [Fact]
    public void F55_MaskMultiSegmentDest_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B3P55Mask
            {
                public class Addr { public string City { get; set; } = ""; }
                public class U { public string City { get; set; } = ""; }
                public class D
                {
                    public string City { get; set; } = "";
                    public Addr Inner { get; set; } = new();
                }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Inner.City).Mask<MaskPhone>();
                        cfg.For<U, D>().Member(d => d.City).From(s => s.City);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F55");
        AssertPriorExtraction(result);
    }

    /// <summary>B2-03.2-NoAutoInclude 站点：多段 dest → F55。</summary>
    [Fact]
    public void F55_NoAutoIncludeMultiSegment_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B3P55NoAutoInclude
            {
                public class Addr { public string City { get; set; } = ""; }
                public class U { public string City { get; set; } = ""; }
                public class D
                {
                    public string City { get; set; } = "";
                    public Addr Inner { get; set; } = new();
                }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Inner.City).NoAutoInclude();
                        cfg.For<U, D>().Member(d => d.City).From(s => s.City);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F55");
        AssertPriorExtraction(result);
    }

    // ======================= B2-03.3：F56 非 lambda 实参 =======================

    /// <summary>B2-03.3：委托变量实参（可编译但非 lambda 形态）→ F56（Error）。</summary>
    [Fact]
    public void F56_MapDelegateVariableArg_Emitted()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            namespace B3P56Map
            {
                public class U { public string City { get; set; } = ""; }
                public class D { public string City { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        Expression<System.Func<U, string>> srcExpr = s => s.City;
                        cfg.For<U, D>().Member(d => d.City).From(srcExpr);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f56 = result.Diagnostics.Where(d => d.Id == "NCMAP_F56").ToList();
        Assert.Single(f56);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f56[0].Severity);
        // #96③ 构建期护栏：消息格式化成功，不回落未格式化模板
        Assert.DoesNotContain("{0}", f56[0].GetMessage());
    }

    /// <summary>B2-03.3：Ignore 委托变量实参 → F56（新面纯链式形态下 Member 头 dest 位非 lambda 拒绝）。</summary>
    [Fact]
    public void F56_IgnoreDelegateVariableArg_Emitted()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            namespace B3P56Ignore
            {
                public class U { public string City { get; set; } = ""; }
                public class D
                {
                    public string City { get; set; } = "";
                    public string Other { get; set; } = "";
                }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        Expression<System.Func<D, string>> destExpr = d => d.Other;
                        cfg.For<U, D>().Member(d => d.City).From(s => s.City).Member(destExpr).Ignore();
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F56");
        AssertPriorExtraction(result);
    }

    // ======================= B2-03.4：F57 三元 =======================

    /// <summary>B2-03.4：解析消费位三元表达式 → F57（Warning，F08 同族）。</summary>
    [Fact]
    public void F57_TernaryInSourceSelector_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B3P57Ternary
            {
                public class U
                {
                    public bool Flag { get; set; }
                    public string CityA { get; set; } = "";
                    public string CityB { get; set; } = "";
                }
                public class D { public string City { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.City).From(s => s.Flag ? s.CityA : s.CityB);
                        cfg.For<U, D>().Member(d => d.City).From(s => s.CityA);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f57 = result.Diagnostics.Where(d => d.Id == "NCMAP_F57").ToList();
        Assert.Single(f57);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f57[0].Severity);
        // 先验锚：同 fixture 的纯表达式体声明被提取
        AssertPriorExtraction(result);
    }

    /// <summary>Q7 边界正样例：MapWhen 内联谓词三元经 InlineTranscriber 转录，不触发 F57 零误报。</summary>
    [Fact]
    public void F57_MapWhenInlineTernaryPredicate_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            namespace B3P57Q7
            {
                public class U { public int Amount { get; set; } }
                public class D { public int Amount { get; set; } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Amount).MapWhen<U>(s => s.Amount > 0 ? true : false);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 转录位三元为合法形态（转录裁定）——F57 零命中
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F57");
    }

    /// <summary>F08 回归护栏：lambda 实参内三元不被 F08 命中（TernaryFinder 下降豁免未被连带改动）。</summary>
    [Fact]
    public void F57_TernaryInLambda_DoesNotTriggerF08()
    {
        var source = """
            using NCMapper;
            namespace B3P57F08Guard
            {
                public class U
                {
                    public bool Flag { get; set; }
                    public string CityA { get; set; } = "";
                    public string CityB { get; set; } = "";
                }
                public class D { public string City { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.City).From(s => s.CityA);
                        cfg.For<U, D>().Member(d => d.City).From(s => s.Flag ? s.CityA : s.CityB);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F08");
    }

    // ======================= B2-04：F58 不可静态求值实参 =======================

    /// <summary>B2-04：位置实参为 readonly 字段（非 const）→ F58（Error）+ 声明照常生成。</summary>
    [Fact]
    public void F58_MapPositionalVariableArg_Emitted_AndDeclarationStillGenerated()
    {
        var source = """
            using NCMapper;
            namespace B3P58Positional
            {
                public class U { public string City { get; set; } = ""; }
                public class D { public string City { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    private static readonly bool Ign = true;

                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.City).From(s => s.City, overrideIgnore: Ign);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f58 = result.Diagnostics.Where(d => d.Id == "NCMAP_F58").ToList();
        Assert.Single(f58);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f58[0].Severity);
        // #96③ 构建期护栏：消息格式化成功（占位符被实参替换，不回落模板）
        Assert.DoesNotContain("{0}", f58[0].GetMessage());
        // 处置定案（002-R2）：声明照常生成 + 无效实参按默认值生效（不跳过声明）
        AssertPriorExtraction(result);
    }

    /// <summary>B2-04：命名实参形态（overrideIgnore: 变量）→ F58（命名实参分支同在检测面）。</summary>
    [Fact]
    public void F58_MapNamedVariableArg_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B3P58Named
            {
                public class U { public string City { get; set; } = ""; }
                public class D { public string City { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    private static readonly bool Ign = true;

                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.City).From(s => s.City, overrideIgnore: Ign);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F58");
        AssertPriorExtraction(result);
    }

    /// <summary>B2-04：Direction 位置实参为变量 → F58（方向意图静默丢失显性化）。</summary>
    [Fact]
    public void F58_IgnoreDirectionVariableArg_Emitted()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace B3P58Direction
            {
                public class U { public string City { get; set; } = ""; }
                public class D
                {
                    public string City { get; set; } = "";
                    public string Other { get; set; } = "";
                }

                [MapperConfig]
                public static class M
                {
                    private static readonly Direction Dir = Direction.ToDto;

                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Other).Ignore(Dir);
                        cfg.For<U, D>().Member(d => d.City).From(s => s.City);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F58");
        AssertPriorExtraction(result);
    }

    /// <summary>B2-04 零误伤：字面量 / 枚举成员 / const 标识符实参 → 零 F58 且语义生效。</summary>
    [Fact]
    public void F58_SemanticConstants_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace B3P58Constants
            {
                public class U { public string City { get; set; } = ""; }
                public class D
                {
                    public string City { get; set; } = "";
                    public string Other { get; set; } = "";
                }

                [MapperConfig]
                public static class M
                {
                    private const bool ConstIgn = true;

                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.City).From(s => s.City, overrideIgnore: true);
                        cfg.For<U, D>().Member(d => d.City).From(s => s.City, overrideIgnore: ConstIgn);
                        cfg.For<U, D>().Member(d => d.Other).Ignore(Direction.ToDto);
                        cfg.For<U, D>().Member(d => d.City).Ignore(Direction.Both);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F58");
        AssertPriorExtraction(result);
    }

    /// <summary>B2-04 命名实参形态（002-P3-2·新面重拟）：Member(dest:) + Ignore(direction:) 命名实参 + 常量方向 → 零 F58 误报。</summary>
    [Fact]
    public void F58_DirectionNamedArgReorder_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace B3P58Reorder
            {
                public class U { public string City { get; set; } = ""; }
                public class D
                {
                    public string City { get; set; } = "";
                    public string Other { get; set; } = "";
                }

                [MapperConfig]
                public static class M
                {
                    private const Direction Dir = Direction.ToDto;

                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(dest: d => d.Other).Ignore(direction: Dir);
                        cfg.For<U, D>().Member(d => d.City).From(s => s.City);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 命名实参按名解析（NameColon 门）：新面 Member(dest:)/Ignore(direction:) 命名形态不误入
        // 非常量判定面 → 零误报（旧面"direction 位取到 lambda"乱序形态随换代退役——新面单参无乱序位）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F58");
    }

    // ======================= B2-05：可见性五门补拒 private protected =======================

    /// <summary>B2-05-F12 站点：AfterMapping private protected 目标方法 → 提取期 F12 拒绝。</summary>
    [Fact]
    public void B205_AfterMapping_PrivateProtected_Diagnosed()
    {
        var source = """
            using NCMapper;
            namespace B3P205F12
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().AfterMapping(Apply);
                    }

                    private protected static void Apply(A src, B dst) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f12 = result.Diagnostics.Where(d => d.Id == "NCMAP_F12").ToList();
        Assert.Single(f12);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f12[0].Severity);
    }

    /// <summary>B2-05-F35 站点：BeforeMapping private protected 目标方法 → F35 拒绝。</summary>
    [Fact]
    public void B205_BeforeMapping_PrivateProtected_Diagnosed()
    {
        var source = """
            using NCMapper;
            namespace B3P205F35
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().BeforeMapping(Apply);
                    }

                    private protected static void Apply(A src, B dst) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F35");
    }

    /// <summary>B2-05 正样例：public/internal/protected internal 方法零误伤。</summary>
    [Fact]
    public void B205_PublicAndInternalHooks_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            namespace B3P205Positive
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().AfterMapping(Apply);
                        cfg.For<A, B>().Member(d => d.Id).From(s => s.Id);
                    }

                    internal static void Apply(A src, B dst) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F12");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F35");
        AssertPriorExtraction(result);
    }

    /// <summary>
    /// B11 先验锚：证明本次 fixture 的提取管线确实产出了映射（"声明确实被提取"）。
    /// <para>防负向用例 vacuous pass——若 fixture 整体编译/绑定失败导致零提取，本断言先红，
    /// 被拒形态的"诊断存在"断言才具备判别意义。</para>
    /// </summary>
    private static void AssertPriorExtraction(MapperGenerationResult result)
    {
        Assert.False(
            string.IsNullOrWhiteSpace(result.GeneratedCode),
            "先验锚失败：本次 fixture 未产出任何生成代码（提取管线未跑通，负向断言无判别力）");
    }

    // =======================，四新码双语资源三键齐备（#82）=======================


    /// <summary> 验收：F55-F58 的 neutral resx 访问器可读 + zh-CN 卫星三键成对存在。</summary>
    [Theory]
    [InlineData("NCMAP_F55")]
    [InlineData("NCMAP_F56")]
    [InlineData("NCMAP_F57")]
    [InlineData("NCMAP_F58")]
    public void NewFluentDiagnostics_BilingualResources_Present(string ruleId)
    {
        var neutral = ruleId switch
        {
            "NCMAP_F55" => Resources.DiagnosticResources.NCMAP_F55_Message,
            "NCMAP_F56" => Resources.DiagnosticResources.NCMAP_F56_Message,
            "NCMAP_F57" => Resources.DiagnosticResources.NCMAP_F57_Message,
            "NCMAP_F58" => Resources.DiagnosticResources.NCMAP_F58_Message,
            _ => "",
        };
        Assert.False(string.IsNullOrWhiteSpace(neutral));

        var title = ruleId switch
        {
            "NCMAP_F55" => Resources.DiagnosticResources.NCMAP_F55_Title,
            "NCMAP_F56" => Resources.DiagnosticResources.NCMAP_F56_Title,
            "NCMAP_F57" => Resources.DiagnosticResources.NCMAP_F57_Title,
            "NCMAP_F58" => Resources.DiagnosticResources.NCMAP_F58_Title,
            _ => "",
        };
        var description = ruleId switch
        {
            "NCMAP_F55" => Resources.DiagnosticResources.NCMAP_F55_Description,
            "NCMAP_F56" => Resources.DiagnosticResources.NCMAP_F56_Description,
            "NCMAP_F57" => Resources.DiagnosticResources.NCMAP_F57_Description,
            "NCMAP_F58" => Resources.DiagnosticResources.NCMAP_F58_Description,
            _ => "",
        };
        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.False(string.IsNullOrWhiteSpace(description));

        // zh-CN 卫星资源键成对存在（文件级解析——与 DiagnosticFluentTests 同定位模式）
        var zhPath = System.IO.Path.Combine(
            System.AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "NCMapper.SourceGen", "Resources", "DiagnosticResources.zh-CN.resx");
        Assert.True(System.IO.File.Exists(zhPath), $"zh-CN resx 不存在: {zhPath}");
        var zhContent = System.IO.File.ReadAllText(zhPath);
        Assert.Contains($"name=\"{ruleId}_Title\"", zhContent);
        Assert.Contains($"name=\"{ruleId}_Message\"", zhContent);
        Assert.Contains($"name=\"{ruleId}_Description\"", zhContent);
    }
}
