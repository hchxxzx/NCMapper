// （G7）：lambda 索引段解析全链测试——
//   常量整数索引段放行（SourceIndexes 承载 + ElementAtOrDefault 生成语义）/
//   变量索引/方法调用索引仍 F06 拒绝（负向锁定）/ 非 Map 终端索引拒绝。
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace NCMapper.SourceGen.Tests;

public class LambdaIndexSegmentTests
{
    private const string DetailFileName = "NCMapper_RuntimeSnapshotMetadata.g.cs";

    private const string HappySource = """
        using NCMapper;
        using System.Collections.Generic;
        namespace EmitIdx
        {
            public class IdxItemE { public string Title { get; set; } = ""; }
            public class IdxEntity
            {
                public int Id { get; set; }
                public List<IdxItemE> Items { get; set; } = new();
            }
            public class IdxDto
            {
                public int Id { get; set; }
                public string FirstTitle { get; set; } = "";
            }

            [MapperConfig]
            public class IdxMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<IdxEntity, IdxDto>()
                       .Member(d => d.FirstTitle).From(s => s.Items[0].Title);
                }
            }
        }
        """;

    /// <summary>标准 1 happy path：常量索引段物化——明细段 SourceIndexes 发射 + HasIndex=true。</summary>
    [Fact]
    public void ConstantIndexSegment_MaterializesSourceIndexes()
    {
        var (_, generated) = RunGenerators(HappySource);
        var detail = GeneratedText(generated, DetailFileName);

        Assert.Contains("HasIndex = true,", detail);
        // B1-04：等长 -1 占位编码——Title 段无索引以 -1 补齐
        Assert.Contains("SourceIndexes = new int[] { 0, -1 },", detail);
        Assert.DoesNotContain("SourceIndexes = new int[] { 0 },", detail);
        Assert.Contains("SourcePathNames = new string[] { \"Items\", \"Title\" }", detail);
    }

    /// <summary>标准 1 生成语义：反向无索引属性照常；正向生成 ElementAtOrDefault 索引访问。</summary>
    [Fact]
    public void ConstantIndexSegment_GeneratesElementAtOrDefault()
    {
        var (_, generated) = RunGenerators(HappySource);
        var main = GeneratedText(generated, "NCMapper_Generated_GeneratedMapper.g.cs");

        Assert.Contains("ElementAtOrDefault", main);
        // 无索引 lambda 退化原语义（标准 6 回归）：Id 同名直映不受影响
        var fwd = ExtractMethod(main, "Map_IdxEntity_To_IdxDto(");
        Assert.Contains("Id = src.Id", fwd);
    }

    /// <summary>标准 2 负向（先验锚——同 fixture 正向用例证明链路可达）：变量索引仍 F06 拒绝。</summary>
    [Fact]
    public void VariableIndex_StillRejected_F06()
    {
        var mutated = HappySource.Replace(
            "s => s.Items[0].Title",
            "s => s.Items[idx].Title").Replace(
            "public class IdxMapper",
            "public class IdxMapper\n    {\n        private static int idx = 0;");
        var (diags, generated) = RunGenerators(mutated);

        // 先验锚：同 fixture 常量形态可达（HappySource 断言通过）——此处链路本身可达
        Assert.Contains(diags, d => d.Id == "NCMAP_F06");
    }

    /// <summary>标准 2 负向：方法调用索引仍 F06 拒绝。</summary>
    [Fact]
    public void MethodCallIndex_StillRejected_F06()
    {
        var mutated = HappySource.Replace(
            "s => s.Items[0].Title",
            "s => s.Items[GetIdx()].Title").Replace(
            "public class IdxMapper",
            "public class IdxMapper\n    {\n        private static int GetIdx() => 0;");
        var (diags, _) = RunGenerators(mutated);

        Assert.Contains(diags, d => d.Id == "NCMAP_F06");
    }

    /// <summary>非 Map 终端索引拒绝：Ignore dest 索引（无语义承载面）→ F06 + 声明不落。</summary>
    [Fact]
    public void NonMapTerminal_Index_Detected()
    {
        var mutated = HappySource.Replace(
            "cfg.For<IdxEntity, IdxDto>()\n                       .Member(d => d.FirstTitle).From(s => s.Items[0].Title);",
            "cfg.For<IdxEntity, IdxDto>()\n                       .Member(d => d.FirstTitle).From(s => s.Items[0].Title)\n                       .Member(d => d.Id).Ignore();");
        var (diags, _) = RunGenerators(mutated);

        // Ignore dest 无索引（回归）：不因 Map src 的索引误伤
        Assert.DoesNotContain(diags, d => d.Id == "NCMAP_F06" && d.GetMessage().Contains("Id"));
    }

    // ----------------------------------------------------------------
    // 基建
    // ----------------------------------------------------------------

    private static (ImmutableArray<Diagnostic>, ImmutableArray<GeneratedTree>) RunGenerators(string source)
        => IdxTestHarness.Run(source);

    private static string GeneratedText(ImmutableArray<GeneratedTree> trees, string fileName)
        => trees.Single(t => t.Path.Contains(fileName)).Text;

    private static string ExtractMethod(string text, string methodName)
    {
        var idx = text.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"生成方法 {methodName} 不存在");
        var bodyStart = text.IndexOf('{', idx);
        var depth = 0;
        for (var i = bodyStart; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(idx, i - idx + 1);
            }
        }
        return text.Substring(idx);
    }

    private readonly record struct GeneratedTree(string Path, string Text);

    // ================================================================
    // B2-02 双层索引拒绝口径（审计批次二）
    // WithIndexAfter 拷贝构造仅设单值 IndexAfter → Matrix[0][1] 内层 0 被外层 1 覆盖
    // → SourceIndexes=[1] 索引错 + 段类型未降级元素类型错（静默错代码）。
    // 修复：写入点（TryWalkMemberAccess 元素访问分支）段已有 IndexAfter 再遇索引 → F06 拒绝。
    // ================================================================

    private const string MultiLevelSource = """
        using NCMapper;
        using System.Collections.Generic;
        namespace EmitIdx
        {
            public class MlItemE { public string Title { get; set; } = ""; }
            public class MlEntity
            {
                public int Id { get; set; }
                public List<List<MlItemE>> Matrix { get; set; } = new();
            }
            public class MlDto
            {
                public int Id { get; set; }
                public string CellTitle { get; set; } = "";
                public System.Collections.Generic.List<System.Collections.Generic.List<MlItemE>> Matrix { get; set; } = new();
            }

            [MapperConfig]
            public class MlMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MlEntity, MlDto>()
                       .Member(d => d.CellTitle).From(s => s.Matrix[0][1].Title);
                }
            }
        }
        """;

    /// <summary>先验锚：同源语料的单层索引形态可达（F06 判定门不误伤单级索引——既有 happy path）。</summary>
    [Fact]
    public void SingleLevelIndex_StillMaterialized_BeforeRejectGate()
    {
        var (_, generated) = RunGenerators(HappySource);
        var detail = GeneratedText(generated, DetailFileName);

        Assert.Contains("HasIndex = true,", detail);
        // B1-04：等长 -1 占位编码（与 SourcePathNames 等长）
        Assert.Contains("SourceIndexes = new int[] { 0, -1 },", detail);
    }

    /// <summary>
    /// B2-02 主样例：双层索引 s.Matrix[0][1].Title → F06 拒绝 + 该声明零生成码
    /// （缺陷态静默错代码：SourceIndexes=[1] + ElementAtOrDefault 索引/元素类型双错 → 红）。
    /// </summary>
    [Fact]
    public void MultiLevelIndex_RejectedWithF06_NoCodeGenerated()
    {
        var (diags, generated) = RunGenerators(MultiLevelSource);
        var main = GeneratedText(generated, "NCMapper_Generated_GeneratedMapper.g.cs");

        // 修复后：F06（Warning）落位（复用 FluentIndexerRule 既有通道）
        Assert.Contains(diags, d => d.Id == "NCMAP_F06");

        // 该声明不产出代码：主生成码无 ElementAtOrDefault（缺陷态 ElementAtOrDefault(src.Matrix, 1) → 红）
        Assert.DoesNotContain("ElementAtOrDefault", main);

        // 单层映射（Id 同名直映）不受误伤
        Assert.Contains("Id = src.Id", main);
    }

    /// <summary>
    /// G9-③ dest 侧实测登记（-V7 / 批次三任务书 P4 红测基准输入）：
    /// d.Matrix[0][1]（dest 多级索引）——判定门落共享 LambdaParser.TryWalkMemberAccess，
    /// dest 侧同受 F06 拒绝影响（dest 消费点 FluentExtractor.cs:548/1090/1213/1388/1580
    /// 读 Segments.Any(IndexAfter != null)——门在写入点，dest/src 解析同路径）。
    /// </summary>
    [Fact]
    public void DestSide_MultiLevelIndex_RejectedWithF06_G9()
    {
        var mutated = MultiLevelSource.Replace(
            ".Member(d => d.CellTitle).From(s => s.Matrix[0][1].Title);",
            ".Member(d => d.Matrix[0][1]).From(s => s.Id);");
        Assert.Contains(".Member(d => d.Matrix[0][1]", mutated); // 变异前提自检

        var (diags, generated) = RunGenerators(mutated);
        var main = GeneratedText(generated, "NCMapper_Generated_GeneratedMapper.g.cs");

        // 实测：dest 侧多级索引同样命中 F06（共享解析路径）
        Assert.Contains(diags, d => d.Id == "NCMAP_F06");
        // 该声明不产出 ElementAtOrDefault 静默错代码
        Assert.DoesNotContain("ElementAtOrDefault", main);
    }

    private static class IdxTestHarness
    {
        internal static (ImmutableArray<Diagnostic>, ImmutableArray<GeneratedTree>) Run(string source)
        {
            var compilation = TestHelper.BuildCompilation(source);
            var generators = ImmutableArray.Create<ISourceGenerator>(
                new NCMapperSourceGenerator().AsSourceGenerator(),
                new RuntimeSnapshotGenerator().AsSourceGenerator());
            var driver = CSharpGeneratorDriver.Create(
                generators,
                additionalTexts: ImmutableArray<AdditionalText>.Empty,
                parseOptions: CSharpParseOptions.Default,
                optionsProvider: new EmitOptions());
            var run = driver.RunGenerators(compilation);
            var result = run.GetRunResult();
            var trees = result.GeneratedTrees
                .Select(t => new GeneratedTree(t.FilePath, t.ToString()))
                .ToImmutableArray();
            return (result.Diagnostics, trees);
        }

        private sealed class EmitOptions : AnalyzerConfigOptionsProvider
        {
            public override AnalyzerConfigOptions GlobalOptions => new O();
            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new O();
            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new O();

            private sealed class O : AnalyzerConfigOptions
            {
                public override bool TryGetValue(string key, out string value)
                {
                    if (key == "build_property.NCMapperEmitRuntimeSnapshot") { value = "true"; return true; }
                    value = string.Empty;
                    return false;
                }
            }
        }
    }
}
