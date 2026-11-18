using BenchmarkDotNet.Attributes;
using NCMapper.Shared;

namespace NCMapper.Benchmarks;

/// <summary>
/// 候选解析独立基准类 — 共享候选解析函数（NameCandidateResolver）
/// 全路径热点的性能基线。完整矩阵：4 种 NamingStrategy × 3 种清单规模 × 2 种属性数量 = 24 组合。
/// <para>每组合输出三项指标：归一化字典构建耗时（Mean）、单次查表耗时（Mean）、内存分配（Allocated）。</para>
/// <para>用途：后续策略扩展（kebab-case 等）与共享源文件跨项目一致性验证的性能基线。</para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class CandidateResolverBench
{
    /// <summary>
    /// 命名策略（0-3：Default/CaseSensitive/SnakeCase/UpperSnakeCase）
    /// </summary>
    [Params(0, 1, 2, 3)]
    public int Strategy;

    /// <summary>
    /// 清单规模（0/4/8 项：0 项 / 4 项 / 8 项上限）
    /// </summary>
    [Params(0, 1, 2)]
    public int ListSize;

    /// <summary>
    /// 源属性数量（10 / 100 属性类型）
    /// </summary>
    [Params(0, 1)]
    public int PropCount;

    private string[] _prefixes = [];
    private string[] _postfixes = [];
    private List<string> _sourceNames = [];
    private string _targetName = "";
    private Dictionary<string, int> _builtDict = [];

    /// <summary>
    /// 构建测试数据：源属性名（驼峰风格，10/100 个）、前缀清单（0/4/8 项）、目标名（查表用）
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var count = PropCount == 0 ? 10 : 100;
        _sourceNames = new List<string>(count);
        for (var i = 0; i < count; i++)
            _sourceNames.Add($"UserField{i}");

        var prefixCount = ListSize switch { 0 => 0, 1 => 4, _ => 8 };
        _prefixes = Enumerable.Range(0, prefixCount).Select(i => $"Pfx{i}").ToArray();
        _postfixes = Enumerable.Range(0, prefixCount).Select(i => $"Sfx{i}").ToArray();

        _targetName = "UserField0";
        _builtDict = BuildDict();
    }

    /// <summary>
    /// 构建归一化源字典（每迭代全新构建 — 测构建耗时）
    /// </summary>
    /// <returns>归一化键字典</returns>
    [Benchmark]
    public Dictionary<string, int> BuildNormalizedKeys() => BuildDict();

    /// <summary>
    /// 单次查表（复用已构建字典 — 测查表耗时）
    /// </summary>
    /// <returns>命中标志</returns>
    [Benchmark]
    public bool TryResolveCandidate()
        => NameCandidateResolver.TryResolveCandidate(_builtDict, _targetName, Strategy, out _);

    /// <summary>
    /// 构建字典辅助（GlobalSetup 与 Benchmark 共用）
    /// </summary>
    /// <returns>归一化键字典</returns>
    private Dictionary<string, int> BuildDict()
        => NameCandidateResolver.BuildNormalizedSourceKeys(
            _sourceNames,
            _prefixes,
            _postfixes,
            Strategy,
            Strategy == 0 ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
}
