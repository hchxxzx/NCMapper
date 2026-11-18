using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 象限语义守护生成侧测试（T5：C31-G / C32，D6/D8 裁决移交，2026-08-08 v1.1.5）
/// <para>C31-G：双 true 非互指 → 各声明覆盖有向对零重叠照常生成（方法存在性断言）。
/// C32：双 true 互指 → 生成侧该对不生成 + NCMAP025 Error 级诊断（目标语义，
/// 诊断由生成侧实现 → 当前 Skip 隔离）。两用例均用独立编译单元（验收式基座），
/// 规避互指样本对测试项目编译单元的污染（任务书 T5 否定条件 F1）。</para>
/// </summary>
public class QuadrantSemanticsGenerationTests
{
    /// <summary>
    /// C31-G：双 true 非互指有向对照常生成（方法存在性断言）
    /// <para>源码：X31GSrc 标 [MapFrom(typeof(Y31GTgt))]（有向对 X31GSrc→Y31GTgt）、
    /// Y31GTgt 标 [MapFrom(typeof(Z31GTgt))]（有向对 Y31GTgt→Z31GTgt，Z31GTgt≠X31GSrc）。
    /// 非互指 → 零重叠，X31GSrc 声明照常生成其覆盖的有向对。
    /// 断言：生成代码含 Map_X31GSrc_To_Y31GTgt 方法（C31 运行时契约见 DualEngineSemanticsConformanceTests）。</para>
    /// </summary>
    [Fact]
    public async Task C31G_DoubleTrue_NonMutual_DirectedPairGenerated()
    {
        var source = """
            using NCMapper.Attributes;

            namespace QuadrantGen;

            [MapFrom(typeof(Y31GTgt))]
            public class X31GSrc
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            [MapFrom(typeof(Z31GTgt))]
            public class Y31GTgt
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            public class Z31GTgt
            {
                public int Id { get; set; }
            }
            """;

        var (compilation, diagnostics) = await CompileAndGetDiagnostics(source);

        // 编译零 error（非互指无冲突）
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        // X31GSrc 声明照常生成有向对方法
        var generated = string.Join("\n", compilation.SyntaxTrees
            .Where(t => t.FilePath.Contains("Generated", System.StringComparison.OrdinalIgnoreCase))
            .Select(t => t.ToString()));
        Assert.Contains("Map_X31GSrc_To_Y31GTgt", generated);
        // Y31GTgt 声明独立生成（零重叠无歧义）
        Assert.Contains("Map_Y31GTgt_To_Z31GTgt", generated);
    }

    /// <summary>
    /// C32：双 true 互指 → 生成侧该对不生成 + NCMAP025 Error 级诊断（目标语义）
    /// <para>源码：X32GSrc 标 [MapFrom(typeof(Y32GTgt))]、Y32GTgt 标 [MapFrom(typeof(X32GSrc))] → 互指。
 /// 目标语义：生成侧该对不生成 + NCMAP025 Error 级诊断（构建失败，编号由 分配）。
 /// MutualReferenceRule（Models.cs:337）→ 本用例转绿，无 Skip。</para>
    /// </summary>
    [Fact]
    public async Task C32_DoubleTrue_Mutual_EmitsNCMAP025_AndNotGenerated()
    {
        var source = """
            using NCMapper.Attributes;

            namespace QuadrantGen;

            [MapFrom(typeof(Y32GTgt))]
            public class X32GSrc
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            [MapFrom(typeof(X32GSrc))]
            public class Y32GTgt
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }
            """;

        var (compilation, diagnostics) = await CompileAndGetDiagnostics(source);

 // NCMAP025 Error 级诊断（互指对两侧各报一次，数量 ≥1；编号/级别为契约）
        var mutualDiags = diagnostics.Where(d => d.Id == "NCMAP025").ToList();
        Assert.NotEmpty(mutualDiags);
        Assert.All(mutualDiags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Contains(mutualDiags, d => d.GetMessage().Contains("X32GSrc", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mutualDiags, d => d.GetMessage().Contains("Y32GTgt", StringComparison.OrdinalIgnoreCase));

        // 生成侧该对不生成
        var generated = string.Join("\n", compilation.SyntaxTrees
            .Where(t => t.FilePath.Contains("Generated", System.StringComparison.OrdinalIgnoreCase))
            .Select(t => t.ToString()));
        Assert.DoesNotContain("Map_X32GSrc_To_Y32GTgt", generated);
        Assert.DoesNotContain("Map_Y32GTgt_To_X32GSrc", generated);
    }

    /// <summary>
    /// 编译源码并返回更新后编译与全部诊断（验收式基座，独立编译单元）
    /// </summary>
    /// <param name="source">被测源码字符串</param>
    /// <returns>更新后编译与诊断数组</returns>
    private static async Task<(Compilation, ImmutableArray<Diagnostic>)> CompileAndGetDiagnostics(string source)
    {
        var ncmapperAssembly = typeof(NCMapper.Mapper).Assembly.Location;

        var refAssemblies = new[]
        {
            typeof(object).Assembly.Location,
            ncmapperAssembly,
            typeof(System.Runtime.CompilerServices.MethodImplAttribute).Assembly.Location,
        };

        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var coreAssemblies = new[]
        {
            System.IO.Path.Combine(runtimeDir, "System.Runtime.dll"),
            System.IO.Path.Combine(runtimeDir, "System.Collections.dll"),
            System.IO.Path.Combine(runtimeDir, "System.Linq.dll"),
            System.IO.Path.Combine(runtimeDir, "System.Linq.Expressions.dll"),
        };

        var allRefs = refAssemblies.Concat(coreAssemblies)
            .Where(System.IO.File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "QuadrantSemanticsTest",
            new[] { CSharpSyntaxTree.ParseText(source) },
            allRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new NCMapperSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

        return (updatedCompilation, diagnostics);
    }
}
