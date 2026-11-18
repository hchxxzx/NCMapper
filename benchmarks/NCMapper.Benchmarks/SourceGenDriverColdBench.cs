// ============================================================================
// SourceGen per-iteration 冷路径基准（平行夹具）
//
// 背景：CWT 缓存族（AttrSymbolCache /
// TypeRefFromSymbol / FindProperty）在 BDN 同 compilation 迭代协议下
// 跨迭代命中 = 热路径数据；冷路径（每次新 compilation，symbol 实例全新 → 符号键控
// CWT 全 miss）收益需 per-iteration 新 compilation 协议另行标定。
//
// 形态定稿（探索期实证）：
//   - 候选 (a) 同进程 [IterationSetup] 每迭代重建 compilation——对齐 :688 "单 run
//     内去重"目标口径（进程热 + 每 iteration 新 compilation）；job 口径对齐既有
//     基线（LaunchCount=1 / WarmupCount=3 / IterationCount=15）。
//   - 候选 (b) 纯进程隔离（LaunchCount=15/IterationCount=1/InvocationCount=1）已
//     证伪：JIT/进程冷启动混入测量（MapListShapeCold 两轮 Mean 7.546/8.010 ms、
//     StdDev 3.15/3.54 ms ≈ 42%，与"新 compilation 生成"口径不符）——退役登记。
//   - 关键坑（探索实证）：探针守门若与 workload 共用 compilation，探针阶段即把符号
//     键控 CWT 填热 → Benchmark 测得热路径。故探针用【独立 probe compilation】
//     （symbol 实例与 workload compilation 无交集，缓存不互通），每次 IterationSetup
//     建【全新 workload compilation】保证 [Benchmark] 首次生成 = 冷路径。
//
// 设计：
//   - 平行夹具类，零触既有热路径夹具与 MANIFEST 既有基线数字（既有基线复现载体不变）；
//   - 冷源文本经反射从既有热夹具收割（SourceText / ProbeMarkers / SourceTemplate），
//     零复制——热夹具文本演进时冷夹具自动跟随，防双源漂移；
//   - 探针守门：生成文本 > 0 + 内容锚全命中，防假基准。
//
// 运行入口（Program.cs 分发）：dotnet run ... -- sourcegen-cold [夹具名过滤]
// ============================================================================

using System.Reflection;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen;

namespace NCMapper.Benchmarks;

/// <summary>
/// 从既有热路径夹具收割冷夹具所需规格（源文本 + 探针锚）。
/// 反射访问 protected/私有成员——热夹具零改动，冷夹具零复制。
/// </summary>
internal static class ColdSpecHarvest
{
    public static (string SourceText, string[] ProbeMarkers, string Name) Harvest(Type hotType)
    {
        var inst = Activator.CreateInstance(hotType)
            ?? throw new InvalidOperationException($"ColdSpecHarvest: cannot instantiate {hotType.Name}.");

        // 优先属性（SourceGenFixtureBase 系：protected abstract → 具体类 override）
        var prop = hotType.GetProperty("SourceText", BindingFlags.NonPublic | BindingFlags.Instance);
        string source;
        if (prop != null)
        {
            source = (string)(prop.GetValue(inst) ?? throw new InvalidOperationException($"{hotType.Name}.SourceText is null."));
        }
        else
        {
            // 回退字段（SourceGenDriverBench 主夹具：private const/static SourceTemplate）
            var field = hotType.GetField("SourceTemplate", BindingFlags.NonPublic | BindingFlags.Static)
                ?? hotType.GetField("SourceTemplate", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"{hotType.Name}: neither SourceText property nor SourceTemplate field found.");
            source = (string)(field.GetValue(inst) ?? throw new InvalidOperationException($"{hotType.Name}.SourceTemplate is null."));
        }

        var markerProp = hotType.GetProperty("ProbeMarkers", BindingFlags.NonPublic | BindingFlags.Instance);
        string[] markers = markerProp != null
            ? (string[])(markerProp.GetValue(inst) ?? Array.Empty<string>())
            : ["class"]; // 主夹具无 ProbeMarkers——退化为"非空产物"守门（字节 > 0 检查兜底）

        return (source, markers, hotType.Name);
    }
}

/// <summary>
/// 冷路径夹具基类（候选 (a) 同进程 per-iteration 新 compilation 形态）。
/// <para>job 口径对齐既有基线（LaunchCount=1 / WarmupCount=3 / IterationCount=15）；
/// [IterationSetup] 每 iteration 重建 workload compilation（symbol
/// 全新 → 符号键控 CWT 全 miss = 冷路径）；探针守门用独立 probe compilation（进程级
/// 一次），与 workload compilation 缓存不互通（symbol 实例隔离），不热化 [Benchmark]。</para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(id: "Cold", launchCount: 1, warmupCount: 3, iterationCount: 15)]
public abstract class SourceGenColdBenchBase
{
    protected abstract Type HotFixtureType { get; }

    private MetadataReference[] _references = null!;
    private string _source = null!;
    private string[] _markers = null!;
    private CSharpCompilation _comp = null!;
    private ISourceGenerator _gen = null!;

    /// <summary>生成文本规模（UTF-8 字节数，GlobalSetup 探针结果）——冷口径文本规模锚（应与热口径一致）。</summary>
    public int GeneratedBytes { get; private set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var (source, markers, hotName) = ColdSpecHarvest.Harvest(HotFixtureType);
        _source = source;
        _markers = markers;

        _references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToArray();
        var ncmapper = typeof(NCMapper.Attributes.MapFromAttribute).Assembly.Location;
        if (!_references.Any(r => r.Display == ncmapper))
        {
            _references = _references.Append(MetadataReference.CreateFromFile(ncmapper)).ToArray();
        }

        // 探针守门（进程级一次）：用【独立 probe compilation】——其 symbol 实例与
        // IterationSetup 里的 workload compilation 无交集，符号键控 CWT 缓存不互通，
        // 因此探针跑生成不会热化 [Benchmark] 的冷路径。
        var probeComp = CreateCompilation(_source, $"SourceGenColdProbe_{hotName}");
        var probeGen = new NCMapperSourceGenerator().AsSourceGenerator();
        var probe = CSharpGeneratorDriver.Create(probeGen)
            .RunGenerators(probeComp)
            .GetRunResult();
        GeneratedBytes = probe.GeneratedTrees.Sum(t => System.Text.Encoding.UTF8.GetByteCount(t.ToString()));
        var allText = string.Join("\n", probe.GeneratedTrees.Select(t => t.ToString()));

        if (GeneratedBytes == 0)
            throw new InvalidOperationException(
                $"{GetType().Name}: the generator produced no output -- the benchmark would measure nothing.");
        foreach (var marker in _markers)
        {
            if (!allText.Contains(marker, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{GetType().Name}: probe marker '{marker}' missing from generated text -- target path not exercised.");
        }

        Console.WriteLine($"[{GetType().Name}] probe OK: {GeneratedBytes} bytes, markers [{string.Join(", ", _markers)}]");
    }

    /// <summary>
    /// 每 iteration 重建 workload compilation（symbol 实例全新 → CWT 冷）。
    /// IterationSetup 时间不计入 benchmark 值（BDN 语义）；不跑任何生成——冷路径由
    /// [Benchmark] 首次（唯一）RunGenerators 承载。
    /// </summary>
    [IterationSetup]
    public void IterationSetup()
    {
        _comp = CreateCompilation(_source, $"SourceGenColdWorkload_{HotFixtureType.Name}_{Guid.NewGuid():N}");
        _gen = new NCMapperSourceGenerator().AsSourceGenerator();
    }

    /// <summary>单次冷路径生成（新 compilation 首次 RunGenerators）——返回生成文本 UTF-8 字节数。</summary>
    [Benchmark]
    public int RunGeneratorDriverCold()
    {
        var runResult = CSharpGeneratorDriver.Create(_gen)
            .RunGenerators(_comp)
            .GetRunResult();

        var bytes = 0;
        foreach (var generatedTree in runResult.GeneratedTrees)
        {
            bytes += System.Text.Encoding.UTF8.GetByteCount(generatedTree.ToString());
        }
        return bytes;
    }

    private static CSharpCompilation CreateCompilation(string source, string assemblyName)
    {
        var tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default);
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
        return CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }
}

// ================================================================
// 冷夹具（平行于既有 9 夹具；HotFixtureType 指向既有热夹具）
// ================================================================

public class SourceGenDriverColdBench : SourceGenColdBenchBase
{
    protected override Type HotFixtureType => typeof(SourceGenDriverBench);
}

public class MapListShapeCold : SourceGenColdBenchBase
{
    protected override Type HotFixtureType => typeof(SourceGenDriverBench.MapListShape);
}

public class MutualPairs3Cold : SourceGenColdBenchBase
{
    protected override Type HotFixtureType => typeof(SourceGenDriverBench.MutualPairs3);
}

public class WidePropertiesCold : SourceGenColdBenchBase
{
    protected override Type HotFixtureType => typeof(SourceGenDriverBench.WideProperties);
}

public class DiagScaleCold : SourceGenColdBenchBase
{
    protected override Type HotFixtureType => typeof(SourceGenDriverBench.DiagScale);
}

public class RepeatedDeclarations3Cold : SourceGenColdBenchBase
{
    protected override Type HotFixtureType => typeof(SourceGenDriverBench.RepeatedDeclarations3);
}

public class SelfRecursiveInlineCold : SourceGenColdBenchBase
{
    protected override Type HotFixtureType => typeof(SourceGenDriverBench.SelfRecursiveInline);
}

public class DeepCopyGraphSharedCold : SourceGenColdBenchBase
{
    protected override Type HotFixtureType => typeof(SourceGenDriverBench.DeepCopyGraphShared);
}

public class FluentChainSubclassCold : SourceGenColdBenchBase
{
    protected override Type HotFixtureType => typeof(SourceGenDriverBench.FluentChainSubclass);
}
