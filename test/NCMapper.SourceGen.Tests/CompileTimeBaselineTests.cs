using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 配置变更编译时间基准冒烟（S6）
/// <para>测量"修改 NCMapperNamingStrategy 前后全量重生成"耗时并记录；
/// 硬阈值宽松（&lt; 10s）避免 CI 环境噪声误判（回归 &gt;10% 触发增量优化评估，
/// 属人工评估项，测试仅做冒烟与记录）。</para>
/// <para>本用例为方法学载体：正式基准在 BenchmarkDotNet（CandidateResolverBench），
/// 此处锁定"配置变化 → 增量管线重生成 → 结果更新"的端到端耗时上界。</para>
/// </summary>
public class CompileTimeBaselineTests
{
    /// <summary>
    /// 测试源码模板（含 [MapFrom] DTO）
    /// </summary>
    private const string SourceTemplate = """
        using NCMapper.Attributes;

        namespace Consumer;
        [MapFrom(typeof(UserEntity))]
        public class UserDto
        {
            public string UserName { get; set; } = "";
        }
        public class UserEntity
        {
            public string UserName { get; set; } = "";
        }
        """;

    /// <summary>
    /// 构建编译单元（引用 NCMapper）
    /// </summary>
    /// <returns>编译单元</returns>
    private static CSharpCompilation CreateCompilation()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(SourceTemplate);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();
        var ncmapper = typeof(NCMapper.Mapper).Assembly.Location;
        if (!references.Any(r => r.Display == ncmapper))
            references.Add(MetadataReference.CreateFromFile(ncmapper));
        return CSharpCompilation.Create("CompileTimeBaseline", new[] { syntaxTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// 运行生成器（指定 MSBuild 配置属性）
    /// </summary>
    /// <param name="properties">配置属性</param>
    /// <returns>生成代码文本</returns>
    private static string RunGenerator(Dictionary<string, string> properties)
    {
        var compilation = CreateCompilation();
        var generator = new NCMapperSourceGenerator();
        var optionsProvider = new GlobalConfigReaderTestsConfigOptionsProvider(properties);
        var driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() }, optionsProvider: optionsProvider);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return updated.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("NCMapper_Generated_GeneratedMapper.g.cs"))?
            .ToString() ?? "";
    }

    /// <summary>
    /// 验证：配置变更（Default → SnakeCase）触发重生成且耗时在宽松上界内
    /// </summary>
    [Fact]
    public void ConfigChange_Regeneration_WithinSmokeBound()
    {
        // 基准（无配置）
        var swBaseline = Stopwatch.StartNew();
        var baseline = RunGenerator(new Dictionary<string, string>());
        swBaseline.Stop();

        // 变更配置（SnakeCase）→ 增量管线应重生成（快照变化触发）
        var swChanged = Stopwatch.StartNew();
        var changed = RunGenerator(new Dictionary<string, string>
        {
            ["build_property.NCMapperNamingStrategy"] = "SnakeCase",
        });
        swChanged.Stop();

        // 行为断言：配置变更后生成代码含快照（RegisterGeneratedConfig）
        Assert.Contains("RegisterGeneratedConfig", changed);
        Assert.Contains("NamingStrategy = 2", changed); // SnakeCase = 2

        // 冒烟上界（宽松，避免 CI 噪声）：单次生成 ≤ 10s
        Assert.True(swChanged.ElapsedMilliseconds < 10_000,
            $"配置变更重生成耗时 {swChanged.ElapsedMilliseconds}ms 超冒烟上界 10s（基线 {swBaseline.ElapsedMilliseconds}ms）");
    }
}
