using NCMapper.SourceGen.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 集合索引路径生成形态套件— 源生成引擎列 S11
/// <para>覆盖：S11a 多段索引（A.B[0].C）ElementAtOrDefault 形态 + null 守卫；S11b 单段索引（B[0]）；
/// S11c 中间索引（A[0].B.C）；S11d 值类型元素末段（A.B[0] → int）；S11e Compose 单段索引；
/// S11f 报告路径显示（Address.Posts[0].Title）；S11g 缺失段/非集合段索引诊断；
/// S11i 索引路径 × NCMAP009 判重键（不同索引不误报 / 相同索引真重复仍报）。</para>
/// <para>值语义（集合 null → default；空集合/越界 → default）由运行时列真实执行实证（NCMapper.Tests）。</para>
/// </summary>
public class CollectionIndexGenerationTests
{
    // ==================== S11a：多段索引 A.B[0].C ====================

    /// <summary>
    /// 场景 11a：Address.Posts[0].Title → 生成 ElementAtOrDefault 形态 + null 守卫
    /// <para>期望形态：dst.FirstPostTitle = (src.Address?.Posts != null ? System.Linq.Enumerable.ElementAtOrDefault(src.Address?.Posts, 0) : default)?.Title;</para>
    /// </summary>
    [Fact]
    public void S11a_MultiSegmentIndex_GeneratesElementAtOrDefault()
    {
        var source = """
            using NCMapper.Attributes;
            public class CiSgPost { public string Title { get; set; } }
            public class CiSgAddress { public System.Collections.Generic.List<CiSgPost> Posts { get; set; } }
            public class CiSgEntity { public CiSgAddress Address { get; set; } }
            [MapFrom(typeof(CiSgEntity))]
            public class CiSgDto
            {
                [MapProperty(typeof(CiSgEntity), "Address.Posts[0].Title")]
                public string FirstPostTitle { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_CiSgEntity_To_CiSgDto",
            "System.Linq.Enumerable.ElementAtOrDefault(src.Address?.Posts, 0)");
    }

    /// <summary>
    /// 场景 11a-2：多段索引生成 null 守卫（集合 null → default 短路）
    /// </summary>
    [Fact]
    public void S11a2_MultiSegmentIndex_GeneratesNullGuard()
    {
        var source = """
            using NCMapper.Attributes;
            public class CiSgPost2 { public string Title { get; set; } }
            public class CiSgAddress2 { public System.Collections.Generic.List<CiSgPost2> Posts { get; set; } }
            public class CiSgEntity2 { public CiSgAddress2 Address { get; set; } }
            [MapFrom(typeof(CiSgEntity2))]
            public class CiSgDto2
            {
                [MapProperty(typeof(CiSgEntity2), "Address.Posts[0].Title")]
                public string FirstPostTitle { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_CiSgEntity2_To_CiSgDto2",
            "src.Address?.Posts != null ? System.Linq.Enumerable.ElementAtOrDefault(src.Address?.Posts, 0) : default");
    }

    // ==================== S11b：单段索引 B[0]（元素 string） ====================

    /// <summary>
    /// 场景 11b：Tags[0]（List&lt;string&gt）→ 单段索引生成形态（Count==1 也走索引包装）
    /// </summary>
    [Fact]
    public void S11b_SingleSegmentIndex_GeneratesElementAtOrDefault()
    {
        var source = """
            using NCMapper.Attributes;
            public class CiSgEntityB { public System.Collections.Generic.List<string> Tags { get; set; } }
            [MapFrom(typeof(CiSgEntityB))]
            public class CiSgDtoB
            {
                [MapProperty(typeof(CiSgEntityB), "Tags[0]")]
                public string FirstTag { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_CiSgEntityB_To_CiSgDtoB",
            "System.Linq.Enumerable.ElementAtOrDefault(src.Tags, 0)");
    }

    // ==================== S11c：中间索引 A[0].B.C ====================

    /// <summary>
    /// 场景 11c：Orders[0].Customer.Name → 索引段后接导航链
    /// </summary>
    [Fact]
    public void S11c_MiddleIndex_GeneratesIndexThenNavChain()
    {
        var source = """
            using NCMapper.Attributes;
            public class CiSgCustC { public string Name { get; set; } }
            public class CiSgOrderC { public CiSgCustC Customer { get; set; } }
            public class CiSgEntityC { public System.Collections.Generic.List<CiSgOrderC> Orders { get; set; } }
            [MapFrom(typeof(CiSgEntityC))]
            public class CiSgDtoC
            {
                [MapProperty(typeof(CiSgEntityC), "Orders[0].Customer.Name")]
                public string FirstCustomerName { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 索引段后接导航链：ElementAtOrDefault 包装 + null 守卫 + ?. 链
        result.Should().HaveMethodBodyContaining("Map_CiSgEntityC_To_CiSgDtoC",
            "(src.Orders != null ? System.Linq.Enumerable.ElementAtOrDefault(src.Orders, 0) : default)?.Customer?.Name");
    }

    // ==================== S11d：值类型元素末段 A.B[0]（int） ====================

    /// <summary>
    /// 场景 11d：Scores[0]（List&lt;int&gt）→ 值类型元素直接赋值（无 ?? 提升问题）
    /// </summary>
    [Fact]
    public void S11d_ValueTypeElement_AssignsElementType()
    {
        var source = """
            using NCMapper.Attributes;
            public class CiSgEntityD { public System.Collections.Generic.List<int> Scores { get; set; } }
            [MapFrom(typeof(CiSgEntityD))]
            public class CiSgDtoD
            {
                [MapProperty(typeof(CiSgEntityD), "Scores[0]")]
                public int FirstScore { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_CiSgEntityD_To_CiSgDtoD",
            "System.Linq.Enumerable.ElementAtOrDefault(src.Scores, 0)");
    }

    // ==================== S11e：Compose 单段索引 ====================

    /// <summary>
    /// 场景 11e：Compose 跨表 [MapProperty(typeof(Sub), "Tags[0]")] 生成形态
    /// </summary>
    [Fact]
    public void S11e_ComposeSingleSegmentIndex_GeneratesElementAtOrDefault()
    {
        var source = """
            using NCMapper.Attributes;
            public class CiSgSubE { public System.Collections.Generic.List<string> Tags { get; set; } }
            [MapFrom(typeof(CiSgSubE))]
            public class CiSgDtoE
            {
                [MapProperty(typeof(CiSgSubE), "Tags[0]")]
                public string FirstTag { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_CiSgSubE_To_CiSgDtoE_Update",
            "System.Linq.Enumerable.ElementAtOrDefault(src.Tags, 0)");
    }

    // ==================== S11f：报告路径显示 ====================

    /// <summary>
    /// 场景 11f：报告源字段显示含索引（Address.Posts[0].Title）— 从生成树的报告文件断言
    /// </summary>
    [Fact]
    public void S11f_ReportShowsIndexedPath()
    {
        var source = """
            using NCMapper.Attributes;
            public class CiSgPostF { public string Title { get; set; } }
            public class CiSgAddressF { public System.Collections.Generic.List<CiSgPostF> Posts { get; set; } }
            public class CiSgEntityF { public CiSgAddressF Address { get; set; } }
            [MapFrom(typeof(CiSgEntityF))]
            public class CiSgDtoF
            {
                [MapProperty(typeof(CiSgEntityF), "Address.Posts[0].Title")]
                public string FirstPostTitle { get; set; }
            }
            """;

        var compilation = TestHelper.BuildCompilation(source);
        var generator = new NCMapperSourceGenerator();
        var optionsProvider = new ReportEnabledConfigOptionsProvider();
        var driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            optionsProvider: optionsProvider);
        var runResult = driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();

        var reportTree = runResult.GeneratedTrees
            .SingleOrDefault(t => t.FilePath.Contains("MappingReport"));
        Assert.NotNull(reportTree);
        Assert.Contains("Address.Posts[0].Title", reportTree!.ToString());
    }

    /// <summary>
    /// 测试用 AnalyzerConfigOptionsProvider — 注入 build_property.NCMapperGenerateReport=true（报告生成开关）
    /// </summary>
    private sealed class ReportEnabledConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions => new ReportEnabledGlobalOptions();
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new ReportEnabledGlobalOptions();
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new ReportEnabledGlobalOptions();

        private sealed class ReportEnabledGlobalOptions : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.NCMapperGenerateReport")
                {
                    value = "true";
                    return true;
                }
                value = "";
                return false;
            }
        }
    }

    // ==================== S11g：诊断（非集合段索引 / 语法错误） ====================

    /// <summary>
    /// 场景 11g-1：非集合属性带索引（Address[0] 中 Address 非集合）→ NCMAP001 失败诊断
    /// </summary>
    [Fact]
    public void S11g1_NonCollectionIndex_ReportsNCMAP001()
    {
        var source = """
            using NCMapper.Attributes;
            public class CiSgAddrG { public string City { get; set; } }
            public class CiSgEntityG { public CiSgAddrG Address { get; set; } }
            [MapFrom(typeof(CiSgEntityG))]
            public class CiSgDtoG
            {
                [MapProperty(typeof(CiSgEntityG), "Address[0].City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP001"), $"[{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
    }

    /// <summary>
    /// 场景 11g-2：非法索引语法（Scores[x]）→ NCMAP001 失败诊断
    /// </summary>
    [Fact]
    public void S11g2_InvalidIndexSyntax_ReportsNCMAP001()
    {
        var source = """
            using NCMapper.Attributes;
            public class CiSgEntityG2 { public System.Collections.Generic.List<int> Scores { get; set; } }
            [MapFrom(typeof(CiSgEntityG2))]
            public class CiSgDtoG2
            {
                [MapProperty(typeof(CiSgEntityG2), "Scores[x]")]
                public int FirstScore { get; set; }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP001"), $"[{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
    }

    // ==================== S11h：索引 × [Convert]（TField=元素类型） ====================

    /// <summary>
    /// 场景 11h：索引路径 × [Convert]（TField=元素类型）→ 转换器消费生成形态
    /// <para>TField 校验用元素类型（非集合属性类型）——转换器输入 = ElementAtOrDefault 结果；
    /// 起方法引用形态：T1 模板 M(TField)，TField=索引叶元素类型 CiSgPostH。</para>
    /// </summary>
    [Fact]
    public void S11h_IndexWithConvertUsing_GeneratesConverterCall()
    {
        var source = """
            using NCMapper.Attributes;
            using NCMapper;
            public class CiSgPostH { public string Title { get; set; } }
            public class CiSgEntityH { public System.Collections.Generic.List<CiSgPostH> Posts { get; set; } }
            public static class CiSgConverterH
            {
                public static string Convert(CiSgPostH? fieldValue) => fieldValue?.Title ?? "";
            }
            [MapFrom(typeof(CiSgEntityH))]
            public class CiSgDtoH
            {
                [MapProperty(typeof(CiSgEntityH), "Posts[0]")]
                [Convert(typeof(CiSgConverterH), nameof(CiSgConverterH.Convert))]
                public string FirstPostTitle { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 内联静态调用（输入 = 索引访问表达式，集合 null 守卫三元包裹）
        result.Should().HaveMethodBodyContaining("Map_CiSgEntityH_To_CiSgDtoH",
            "FirstPostTitle = global::CiSgConverterH.Convert((src.Posts != null ? System.Linq.Enumerable.ElementAtOrDefault(src.Posts, 0) : default))");
        // 无方法签名不匹配诊断（TField=元素类型校验通过；NCMAP008 为旧接口形态诊断，方法引用形态不产出）
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP037"),
            $"不应输出 NCMAP037（方法签名不匹配），实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
    }

    /// <summary>
    /// 场景 11h-2：索引路径 × [Convert]（字段参数=集合类型≠元素类型）→ 方法签名不匹配诊断（不消费，避免编译错误）
    /// <para> 起旧接口形态 NCMAP008（ConverterTypeMismatchRule）随接口退役；等价失配落点为
    /// 方法引用形态的 NCMAP037（TField 基准=索引叶元素类型，字段参数 List&lt;CiSgPostH2&gt; 失配）。</para>
    /// </summary>
    [Fact]
    public void S11h2_IndexWithMismatchedConvertUsing_ReportsMismatch()
    {
        var source = """
            using NCMapper.Attributes;
            using NCMapper;
            public class CiSgPostH2 { public string Title { get; set; } }
            public class CiSgEntityH2 { public System.Collections.Generic.List<CiSgPostH2> Posts { get; set; } }
            public static class CiSgConverterH2
            {
                public static string Convert(System.Collections.Generic.List<CiSgPostH2>? fieldValue) => "";
            }
            [MapFrom(typeof(CiSgEntityH2))]
            public class CiSgDtoH2
            {
                [MapProperty(typeof(CiSgEntityH2), "Posts[0]")]
                [Convert(typeof(CiSgConverterH2), nameof(CiSgConverterH2.Convert))]
                public string FirstPostTitle { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP037"),
            $"期望 NCMAP037（方法签名不匹配）诊断，实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
    }

    // ==================== S11i：索引路径 × NCMAP009 判重键（叶符号误报修复） ====================

    /// <summary>
    /// 场景 11i：Items[0].Name 与 Items[1].Name 叶符号相同但索引不同 → 不报 NCMAP009
    /// <para>判重键为完整源访问路径（属性名 + 索引序列）而非叶属性符号——仅按叶符号判重会把
    /// 不同集合元素误判为重复（samples/01_BasicMapping/IndexPathModels.cs 实证误报）。</para>
    /// </summary>
    [Fact]
    public void S11i_DistinctIndexes_NoNCMAP009()
    {
        var source = """
            using NCMapper.Attributes;
            public class CiSgItemI { public string Name { get; set; } }
            public class CiSgEntityI { public System.Collections.Generic.List<CiSgItemI> Items { get; set; } }
            [MapFrom(typeof(CiSgEntityI))]
            public class CiSgDtoI
            {
                [MapProperty(typeof(CiSgEntityI), "Items[0].Name")]
                public string FirstItemName { get; set; }
                [MapProperty(typeof(CiSgEntityI), "Items[1].Name")]
                public string SecondItemName { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 双向断言：无 NCMAP009（索引不同 = 不同数据源），但两条索引路径均已生成（排除"未生成导致无诊断"的假绿）
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP009"),
            $"索引不同（[0] vs [1]）不应报 NCMAP009，实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
        result.Should().HaveMethodBodyContaining("Map_CiSgEntityI_To_CiSgDtoI",
            "System.Linq.Enumerable.ElementAtOrDefault(src.Items, 0)");
        result.Should().HaveMethodBodyContaining("Map_CiSgEntityI_To_CiSgDtoI",
            "System.Linq.Enumerable.ElementAtOrDefault(src.Items, 1)");
    }

    /// <summary>
    /// 场景 11i-2：两条相同索引路径（均 Items[0].Name）→ 仍报 NCMAP009（真重复不漏报）
    /// <para>判重键升级不得放过路径完全一致的重复——索引段参与相等性判定。</para>
    /// </summary>
    [Fact]
    public void S11i2_SameIndex_StillReportsNCMAP009()
    {
        var source = """
            using NCMapper.Attributes;
            public class CiSgItemI2 { public string Name { get; set; } }
            public class CiSgEntityI2 { public System.Collections.Generic.List<CiSgItemI2> Items { get; set; } }
            [MapFrom(typeof(CiSgEntityI2))]
            public class CiSgDtoI2
            {
                [MapProperty(typeof(CiSgEntityI2), "Items[0].Name")]
                public string FirstItemName { get; set; }
                [MapProperty(typeof(CiSgEntityI2), "Items[0].Name")]
                public string AliasItemName { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP009"),
            $"两条相同索引路径应报 NCMAP009（真重复），实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
        // 诊断消息第 4 参数升级为完整路径（含索引段），可定位具体重复路径
        var message = result.DiagnosticsByDescriptorId["NCMAP009"][0].GetMessage();
        Assert.Contains("Items[0].Name", message);
    }
}
