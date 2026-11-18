using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen;

namespace NCMapper.Benchmarks;

/// <summary>
/// 编译期基准骨架 — 以 <see cref="CSharpGeneratorDriver"/> 进程内驱动
/// NCMapper 源生成器，度量"固定编译输入 → 生成器运行 → 产出生成文本"的编译期成本。
/// <para>用途：生成器期性能此前无任何既有度量手段（<c>benchmarks/</c> 内
/// <c>CSharpGeneratorDriver</c> 零命中；<c>NCMapper.SelfBench</c> 为 Stopwatch 手写微基准、
/// <c>NCMapper.LoadBench</c> 为负载压测），本类是其度量载体。骨架阶段只要求"跑通 + 产出指标形态 +
/// 噪声底离散度"。</para>
/// <para>指标：耗时（BDN 默认列）+ 生成文本规模（基准方法的返回值 = 生成树 UTF-8 字节数）。</para>
/// <para>入口：<c>dotnet run -c Release -f net10.0 --project benchmarks/NCMapper.Benchmarks -- sourcegen-driver</c>
/// （本工程 <c>Program.cs</c> 为自研 args 分发，不解析 BDN 的 <c>--filter</c>）。</para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 15)]
public partial class SourceGenDriverBench
{
    /// <summary>
    /// 固定编译输入 — 最小映射对（DTO + Entity，单属性 + 单值类型属性），
    /// 与 <c>NCMapper.SourceGen.Tests/Helpers/TestHelper</c> 及 <c>CompileTimeBaselineTests</c> 同族形态。
    /// </summary>
    private const string SourceTemplate = """
        using NCMapper.Attributes;

        namespace BenchInput;

        [MapFrom(typeof(UserEntity))]
        public class UserDto
        {
            public string UserName { get; set; } = "";
            public int Age { get; set; }
        }

        public class UserEntity
        {
            public string UserName { get; set; } = "";
            public int Age { get; set; }
        }
        """;

    private CSharpCompilation _compilation = null!;
    private ISourceGenerator _generator = null!;
    private int _generatedBytes;
    private int _generatedTrees;

    /// <summary>
    /// 生成文本规模（UTF-8 字节数）— GlobalSetup 一次性探针结果，同时作为"生成器真实接入"的守门值。
    /// </summary>
    public int GeneratedBytes => _generatedBytes;

    [GlobalSetup]
    public void Setup()
    {
        var tree = CSharpSyntaxTree.ParseText(SourceTemplate, CSharpParseOptions.Default);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var ncmapper = typeof(NCMapper.Attributes.MapFromAttribute).Assembly.Location;
        if (!references.Any(r => r.Display == ncmapper))
        {
            references.Add(MetadataReference.CreateFromFile(ncmapper));
        }

        _compilation = CSharpCompilation.Create(
            "SourceGenDriverBenchInput",
            new[] { tree },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        _generator = new NCMapperSourceGenerator().AsSourceGenerator();

        // 骨架守门：生成器必须真实接入。生成文本规模恒为 0 说明 driver 空转，基准无意义 —
        // 显式抛错而不是静默产出"零成本"结果（防假基准）。
        var probe = CSharpGeneratorDriver.Create(_generator)
            .RunGenerators(_compilation)
            .GetRunResult();
        _generatedTrees = probe.GeneratedTrees.Length;
        _generatedBytes = probe.GeneratedTrees.Sum(t => Encoding.UTF8.GetByteCount(t.ToString()));

        if (_generatedBytes == 0)
        {
            throw new InvalidOperationException(
                "SourceGenDriverBench: the generator produced no output for the fixed input -- " +
                "the benchmark would measure nothing (driver not wired).");
        }

        Console.WriteLine(
            $"[SourceGenDriverBench] fixed input -> {_generatedTrees} generated tree(s), {_generatedBytes} bytes");
    }

    /// <summary>
    /// 单次生成器运行 — 每次迭代新建 driver（driver 自身状态不跨迭代复用，度量含构造成本）。
    /// </summary>
    /// <returns>生成文本总字节数（UTF-8）</returns>
    [Benchmark]
    public int RunGeneratorDriver()
    {
        var runResult = CSharpGeneratorDriver.Create(_generator)
            .RunGenerators(_compilation)
            .GetRunResult();

        var bytes = 0;
        foreach (var generatedTree in runResult.GeneratedTrees)
        {
            bytes += Encoding.UTF8.GetByteCount(generatedTree.ToString());
        }
        return bytes;
    }
}
