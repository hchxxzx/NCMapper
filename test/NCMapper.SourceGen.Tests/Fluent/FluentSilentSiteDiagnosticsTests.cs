// （B5-4 批次二）：静默站点补诊断红测——
//   B2-06（F59）：同锚点跨种类声明覆盖（AddPathDeclaration / ProcessChainMask 两写入位）；
//   B2-18（F60）：未知链终端方法整链丢弃（主链 default + 块内 default）；
//  B3-23（F61）已随终端退役（ConvertBackSource 退役，F61 descriptor 删除）；
//   站点清单：MapCtorParam / FromSource（速记+块内）/ MapWhen 的多段 F55、索引 F06 补发射。
// 纪律（B11 同款）：每红测携带"声明确实被提取"先验锚；级别断言随 Id 严格对应
//（F59 = Error（真冲突，改写）/ F60 = Warning；F55/F06 = Warning；F61 已退役）。
// 修复前形态：本文件全部站点断言必红（零诊断/零资源键）；修复 commit 后转绿。
using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

public class FluentSilentSiteDiagnosticsTests
{
    // ======================= B2-06：F59 同锚点跨种类覆盖（两写入位）=======================

    /// <summary>B2-06-AddPathDeclaration 位：同链 From → Ignore 同 dest 真冲突（清单①）→ F59（Error）。</summary>
    [Fact]
    public void F59_CrossKindOverride_AddPathDeclaration_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B5T3F59Map
            {
                public class U { public string Name { get; set; } = ""; public string City { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public string City { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.City).From(s => s.City).Member(d => d.City).Ignore();
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f59 = result.Diagnostics.Where(d => d.Id == "NCMAP_F59").ToList();
        Assert.Single(f59);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f59[0].Severity);
        Assert.Contains("City", f59[0].GetMessage());
        Assert.Contains("Map", f59[0].GetMessage());
        Assert.Contains("Ignore", f59[0].GetMessage());
        // W3：消息文本对齐既定资源文案（NCMAP_F59_Message EN——真冲突语义，非旧"跨种类覆盖"口径）；
        // culture 无关化（⑱ 修复——GetMessage 钉 InvariantCulture，先例 FluentMemberCombinationTests:179）
        Assert.Contains("true conflict", f59[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        // 行为不变锚：真冲突拒绝语义下其余声明照常产出（先验锚保持）
        AssertPriorExtraction(result);
    }

    /// <summary>B2-06-ProcessChainMask 写入位：同链 Ignore → Mask 同 dest 真冲突（清单①）→ F59（Error）。</summary>
    [Fact]
    public void F59_CrossKindOverride_MaskWriteSite_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B5T3F59Mask
            {
                public class U { public string Name { get; set; } = ""; public string City { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public string City { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.City).Ignore().Member(d => d.City).Mask<MaskPhone>();
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f59 = result.Diagnostics.Where(d => d.Id == "NCMAP_F59").ToList();
        Assert.Single(f59);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f59[0].Severity);
        Assert.Contains("City", f59[0].GetMessage());
        // W3：消息文本对齐既定资源文案（NCMAP_F59_Message EN——真冲突语义）；
        // culture 无关化（⑱ 修复——GetMessage 钉 InvariantCulture，先例 FluentMemberCombinationTests:179）
        Assert.Contains("true conflict", f59[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        AssertPriorExtraction(result);
    }

    /// <summary>B2-06 边界守护：同 Kind 重声明（Map → Map）不报 F59（既有 last-wins 语义）。</summary>
    [Fact]
    public void F59_SameKindRedeclare_NoDiagnostic()
    {
        var source = """
            using NCMapper;
            namespace B5T3F59Same
            {
                public class U { public string Name { get; set; } = ""; public string City { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public string City { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.City).From(s => s.Name).Member(d => d.City).From(s => s.City);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F59");
        AssertPriorExtraction(result);
    }

    // ======================= B2-18：F60 未知链终端（主链 + 块内）=======================

    /// <summary>B2-18-主链：链尾 object 继承方法（ToString——可编译未知终端）→ F60（Warning），整链丢弃语义保持。</summary>
    [Fact]
    public void F60_UnknownMainChainTerminal_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B5T3F60Main
            {
                public class U1 { public string City { get; set; } = ""; }
                public class D1 { public string City { get; set; } = ""; }
                public class U2 { public string Name { get; set; } = ""; }
                public class D2 { public string Name { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U1, D1>().Member(d => d.City).Ignore().ToString();
                        cfg.For<U2, D2>().Member(d => d.Name).From(s => s.Name);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f60 = result.Diagnostics.Where(d => d.Id == "NCMAP_F60").ToList();
        Assert.Single(f60);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f60[0].Severity);
        Assert.Contains("ToString", f60[0].GetMessage());
        // 丢弃语义保持锚：整链被丢弃——U1/D1 对完全未注册（无映射方法），仅第二对存活
        Assert.False(result.Methods.ContainsKey("Map_U1_To_D1"), "整链丢弃语义破坏：U1→D1 不应生成映射方法");
        Assert.True(result.Methods.ContainsKey("Map_U2_To_D2"), "先验锚失败：第二对合法链未生成");
        AssertPriorExtraction(result);
    }

    /// <summary>B2-18-块内：块语句 object 继承方法（c.ToString()）→ F60（仅跳过该语句节点）。</summary>
    [Fact]
    public void F60_UnknownBlockTerminal_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B5T3F60Block
            {
                public class Sub { public string Tag { get; set; } = ""; }
                public class U { public string Name { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public string Tag { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().FromSource<Sub>(c => { c.Member(d => d.Tag).From(s => s.Tag); c.ToString(); });
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f60 = result.Diagnostics.Where(d => d.Id == "NCMAP_F60").ToList();
        Assert.Single(f60);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f60[0].Severity);
        Assert.Contains("ToString", f60[0].GetMessage());
        AssertPriorExtraction(result);
    }

    /// <summary>B2-18 邻界反例：已知链终端（From/Ignore 等白名单方法）→ 不报 F60。</summary>
    [Fact]
    public void KnownChainTerminal_NoF60()
    {
        var source = """
            using NCMapper;
            namespace B5T3F60Ok
            {
                public class U { public string City { get; set; } = ""; public string Name { get; set; } = ""; }
                public class D { public string City { get; set; } = ""; public string Name { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.City).From(s => s.City).Member(d => d.Name).Ignore();
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F60");
        AssertPriorExtraction(result);
    }

    // ======================= 站点清单：FromSource 速记（多段 F55 / 索引 F06）=======================

    /// <summary>站点②-速记：FromSource dest 多段 → F55（原静默丢弃）。</summary>
    [Fact]
    public void F55_FromSourceShorthand_MultiSegmentDest_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B5T3F55Sh
            {
                public class Addr { public string City { get; set; } = ""; }
                public class Sub { public string X { get; set; } = ""; }
                public class U { public string Name { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public Addr Inner { get; set; } = new(); }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Name).From(s => s.Name).Member(d => d.Inner.City).FromSource<Sub>(s => s.X);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f55 = result.Diagnostics.Where(d => d.Id == "NCMAP_F55").ToList();
        Assert.Single(f55);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f55[0].Severity);
        Assert.Contains("Inner.City", f55[0].GetMessage());
        AssertPriorExtraction(result);
    }

    /// <summary>站点②-速记：FromSource dest 单段带索引 → F06（原静默收窄）。</summary>
    [Fact]
    public void F06_FromSourceShorthand_IndexDest_Emitted()
    {
        var source = """
            using NCMapper;
            using System.Collections.Generic;
            namespace B5T3F06Sh
            {
                public class Sub { public string X { get; set; } = ""; }
                public class U { public string Name { get; set; } = ""; }
                public class D
                {
                    public string Name { get; set; } = "";
                    public List<string> Items { get; set; } = new();
                }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Name).From(s => s.Name).Member(d => d.Items[0]).FromSource<Sub>(s => s.X);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f06 = result.Diagnostics.Where(d => d.Id == "NCMAP_F06").ToList();
        Assert.Single(f06);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f06[0].Severity);
        Assert.Contains("Items[0]", f06[0].GetMessage());
        AssertPriorExtraction(result);
    }

    // ======================= 站点清单：FromSource 块内锚点（多段 F55 / 索引 F06）=======================

    /// <summary>站点②-块内：块内 Ignore 多段 dest → F55（原静默）。</summary>
    [Fact]
    public void F55_FromSourceBlock_AnchorMultiSegment_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B5T3F55Blk
            {
                public class Addr { public string City { get; set; } = ""; }
                public class Sub { public string Tag { get; set; } = ""; }
                public class U { public string Name { get; set; } = ""; }
                public class D
                {
                    public string Name { get; set; } = "";
                    public string Tag { get; set; } = "";
                    public Addr Inner { get; set; } = new();
                }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().FromSource<Sub>(c => { c.Member(d => d.Inner.City).Ignore(); c.Member(d => d.Tag).From(s => s.Tag); });
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f55 = result.Diagnostics.Where(d => d.Id == "NCMAP_F55").ToList();
        Assert.Single(f55);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f55[0].Severity);
        Assert.Contains("Inner.City", f55[0].GetMessage());
        AssertPriorExtraction(result);
    }

    /// <summary>站点②-块内：块内 Ignore 单段带索引 dest → F06（原静默）。</summary>
    [Fact]
    public void F06_FromSourceBlock_AnchorIndex_Emitted()
    {
        var source = """
            using NCMapper;
            using System.Collections.Generic;
            namespace B5T3F06Blk
            {
                public class Sub { public string Tag { get; set; } = ""; }
                public class U { public string Name { get; set; } = ""; }
                public class D
                {
                    public string Name { get; set; } = "";
                    public string Tag { get; set; } = "";
                    public List<string> Items { get; set; } = new();
                }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().FromSource<Sub>(c => { c.Member(d => d.Items[0]).Ignore(); c.Member(d => d.Tag).From(s => s.Tag); });
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f06 = result.Diagnostics.Where(d => d.Id == "NCMAP_F06").ToList();
        Assert.Single(f06);
        Assert.Contains("Items[0]", f06[0].GetMessage());
        AssertPriorExtraction(result);
    }

    // ======================= 站点清单：MapCtorParam（多段/字段段 F55 / 索引 F06）=======================

    /// <summary>站点①：MapCtorParam 源 lambda 多段 → F55（原静默丢弃——FluentCtorParamTests Row3b 契约随本批转正）。</summary>
    [Fact]
    public void F55_MapCtorParam_MultiSegmentSource_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B5T3F55Ctor
            {
                public class Inner { public string Value { get; set; } = ""; }
                public class U { public Inner Nested { get; set; } = new(); public string Name { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public D(string nick) { Nick = nick; } public string Nick { get; } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Name).From(s => s.Name).MapCtorParam("nick", s => s.Nested.Value);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f55 = result.Diagnostics.Where(d => d.Id == "NCMAP_F55").ToList();
        Assert.Single(f55);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f55[0].Severity);
        Assert.Contains("Nested.Value", f55[0].GetMessage());
        AssertPriorExtraction(result);
    }

    /// <summary>站点①：MapCtorParam 源 lambda 单段带索引 → F06（原静默收窄）。</summary>
    [Fact]
    public void F06_MapCtorParam_IndexSource_Emitted()
    {
        var source = """
            using NCMapper;
            using System.Collections.Generic;
            namespace B5T3F06Ctor
            {
                public class U { public List<string> Items { get; set; } = new(); public string Name { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public D(string nick) { Nick = nick; } public string Nick { get; } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Name).From(s => s.Name).MapCtorParam("nick", s => s.Items[0]);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f06 = result.Diagnostics.Where(d => d.Id == "NCMAP_F06").ToList();
        Assert.Single(f06);
        Assert.Contains("Items[0]", f06[0].GetMessage());
        AssertPriorExtraction(result);
    }

    /// <summary>站点①：MapCtorParam 源 lambda 字段段 → F55（1197 门——原静默丢弃；F55 复用为"锚点声明被跳过"口径）。</summary>
    [Fact]
    public void F55_MapCtorParam_FieldSegmentSource_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B5T3F55Fld
            {
                public class U { public string CodeField; public string Name { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public D(string nick) { Nick = nick; } public string Nick { get; } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Name).From(s => s.Name).MapCtorParam("nick", s => s.CodeField);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f55 = result.Diagnostics.Where(d => d.Id == "NCMAP_F55").ToList();
        Assert.Single(f55);
        Assert.Contains("CodeField", f55[0].GetMessage());
        AssertPriorExtraction(result);
    }

    // ======================= 站点清单：MapWhen 多段锚（F55）=======================

    /// <summary>站点③：MapWhen dest 多段锚 → F55（原静默；索引 F06 与重复 F44 已在位不动）。</summary>
    [Fact]
    public void F55_MapWhen_MultiSegmentAnchor_Emitted()
    {
        var source = """
            using NCMapper;
            namespace B5T3F55When
            {
                public class Addr { public string City { get; set; } = ""; }
                public class U { public string Name { get; set; } = ""; public static bool Guard(U u) => true; }
                public class D { public string Name { get; set; } = ""; public Addr Inner { get; set; } = new(); }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Name).From(s => s.Name).Member(d => d.Inner.City).MapWhen<U>(U.Guard);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f55 = result.Diagnostics.Where(d => d.Id == "NCMAP_F55").ToList();
        Assert.Single(f55);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f55[0].Severity);
        Assert.Contains("Inner.City", f55[0].GetMessage());
        AssertPriorExtraction(result);
    }

    // ======================= X5：F59/F60 双语资源键齐备（#82 前置拦截形态；
    // F61 退役，InlineData 移除）=======================

    /// <summary> 验收②：F59/F60 的 neutral 资源键可读 + zh-CN 卫星键成对存在
    ///（string 键形态——修复前资源缺失即红，且不依赖 Designer 访问器编译）。</summary>
    [Theory]
    [InlineData("NCMAP_F59")]
    [InlineData("NCMAP_F60")]
    public void NewSilentSiteDiagnostics_BilingualResources_Present(string ruleId)
    {
        // neutral：ResourceManager 按键直读（修复前键不存在 → null → 红）
        var neutral = Resources.DiagnosticResources.ResourceManager.GetString($"{ruleId}_Title");
        Assert.False(string.IsNullOrWhiteSpace(neutral));
        Assert.False(string.IsNullOrWhiteSpace(
            Resources.DiagnosticResources.ResourceManager.GetString($"{ruleId}_Message")));
        Assert.False(string.IsNullOrWhiteSpace(
            Resources.DiagnosticResources.ResourceManager.GetString($"{ruleId}_Description")));

        // zh-CN 卫星资源键成对存在（文件级解析——与 FluentSilentSemanticsDiagnosticsTests 同定位模式）
        var zhPath = System.IO.Path.Combine(
            System.AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "NCMapper.SourceGen", "Resources", "DiagnosticResources.zh-CN.resx");
        Assert.True(System.IO.File.Exists(zhPath), $"zh-CN resx 不存在: {zhPath}");
        var zhContent = System.IO.File.ReadAllText(zhPath);
        Assert.Contains($"name=\"{ruleId}_Title\"", zhContent);
        Assert.Contains($"name=\"{ruleId}_Message\"", zhContent);
        Assert.Contains($"name=\"{ruleId}_Description\"", zhContent);
    }

    // ======================= 基建 =======================

    private static void AssertPriorExtraction(MapperGenerationResult result)
    {
        Assert.False(
            string.IsNullOrWhiteSpace(result.GeneratedCode),
            "先验锚失败：本次 fixture 未产出任何生成代码（提取管线未跑通，负向断言无判别力）");
    }
}
