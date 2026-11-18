using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// NCMAP018 分析器检测测试 — 验证表达式树 lambda 中数组 Contains 绑定 MemoryExtensions.Contains 的检测精度与误报控制
/// <para>正样本（int[].Contains in Expression）仅在 net10.0 下触发：C# 14 重载决议 + net10 BCL 才能使 T[].Contains 绑定 MemoryExtensions.Contains。
/// net8/net9（C# 13）下绑定 Enumerable.Contains，分析器自动不报——通过方法符号判定实现 TFM 自适应。</para>
/// </summary>
public class ArrayContainsInExpressionAnalyzerTests
{
    /// <summary>
    /// 正样本：net10 下 Expression&lt;Func&lt;T,bool&gt;&gt; lambda 内 int[].Contains 报 NCMAP018
    /// <para>本测试项目已局部 override Roslyn 4.14.0（VersionOverride，全局 4.8.0 不动），
    /// 本测试自 2026-08-13 起启用（修复前分析器死代码——先留红证，修复后转绿）。</para>
    /// </summary>
    [Fact]
    public async Task NCMAP018_ExpressionTree_ArrayContains_Reported_OnNet10()
    {
        var source = """
            using System;
            using System.Linq;
            using System.Linq.Expressions;

            public class WstUser { public int Id { get; set; } }

            class Test {
                void M(int[] idArray)
                {
                    Expression<Func<WstUser, bool>> e = a => idArray.Contains(a.Id);
                }
            }
            """;
        var diagnostics = await CompileAndGetAnalyzerDiagnostics(source);
        Assert.Contains(diagnostics, d => d.Id == "NCMAP018");
    }

    /// <summary>
    /// 负样本：命令式 if (arr.Contains(x)) 不报（MemoryExtensions.Contains 在此是更优实现，不应拦截）
    /// </summary>
    [Fact]
    public async Task NCMAP018_Imperative_ArrayContains_NotReported()
    {
        var source = """
            using System;
            using System.Linq;

            class Test {
                bool M(int[] arr, int x)
                {
                    return arr.Contains(x);
                }
            }
            """;
        var diagnostics = await CompileAndGetAnalyzerDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP018");
    }

    /// <summary>
    /// 负样本：Expression lambda 内 List&lt;int&gt;.Contains 不报（List 实例方法不进 span 扩展方法重载竞争）
    /// </summary>
    [Fact]
    public async Task NCMAP018_ExpressionTree_ListContains_NotReported()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Linq.Expressions;

            public class WstUser { public int Id { get; set; } }

            class Test {
                void M(List<int> idList)
                {
                    Expression<Func<WstUser, bool>> e = a => idList.Contains(a.Id);
                }
            }
            """;
        var diagnostics = await CompileAndGetAnalyzerDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP018");
    }

    /// <summary>
    /// 负样本：表达式树内 string.Contains 不报（string.Contains 翻译 LIKE，且非数组 span 重载）
    /// </summary>
    [Fact]
    public async Task NCMAP018_ExpressionTree_StringContains_NotReported()
    {
        var source = """
            using System;
            using System.Linq;
            using System.Linq.Expressions;

            public class WstUser { public string Name { get; set; } = ""; }

            class Test {
                void M(string keyword)
                {
                    Expression<Func<WstUser, bool>> e = a => a.Name.Contains(keyword);
                }
            }
            """;
        var diagnostics = await CompileAndGetAnalyzerDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP018");
    }

    /// <summary>
    /// 编译源码并运行 ArrayContainsInExpressionAnalyzer 获取诊断
    /// </summary>
    /// <param name="source">待分析的 C# 源码</param>
    /// <returns>分析器产生的诊断集合</returns>
    private static async Task<ImmutableArray<Diagnostic>> CompileAndGetAnalyzerDiagnostics(string source)
    {
        // 收集 BCL 引用（与 DiagnosticTests.CompileAndGetDiagnostics 一致）
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var refPaths = new[]
        {
            typeof(object).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            typeof(System.Linq.Expressions.Expression).Assembly.Location,
            Path.Combine(runtimeDir, "System.Runtime.dll"),
            Path.Combine(runtimeDir, "System.Collections.dll"),
            Path.Combine(runtimeDir, "System.Linq.dll"),
            Path.Combine(runtimeDir, "System.Linq.Expressions.dll"),
            // 补 System.Memory 引用——C# 14 下 T[].Contains 需绑定 MemoryExtensions.Contains（位于 System.Memory）
            Path.Combine(runtimeDir, "System.Memory.dll"),
        };

        var allRefs = refPaths
            .Where(File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        // C# 14（Latest）触发 T[]→ReadOnlySpan<T> 语言级转换，使 T[].Contains 绑定 MemoryExtensions.Contains
        // net8/net9 下 Latest 为 C# 12/13，不触发重载变更，分析器自动不报
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "ArrayContainsAnalyzerTest",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            allRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new ArrayContainsInExpressionAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
