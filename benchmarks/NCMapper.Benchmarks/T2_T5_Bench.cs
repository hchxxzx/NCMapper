using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Benchmarks;

// ================================================================
// ConvertUsing 性能验证 — 3 泛型参数双参数调用性能
// ================================================================

/// <summary>
/// ConvertUsing 改造后性能 — 验证双参数调用无明显回归
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class T2_ConvertUsing_Perf
{
    private BenchConverterSourceEntity _entity = null!;

    [GlobalSetup]
    public void Setup()
    {
        _entity = new BenchConverterSourceEntity
        {
            Id = 1,
            CreateTime = new DateTime(2025, 6, 27, 10, 30, 0),
            Code = 42
        };
    }

    [Benchmark]
    public BenchConverterDto? Map_WithConvertUsing() =>
        Mapper.Map<BenchConverterSourceEntity, BenchConverterDto>(_entity);
}

// T2 benchmark 模型
public class BenchConverterSourceEntity
{
    public int Id { get; set; } = 1;
    public DateTime CreateTime { get; set; } = new(2025, 6, 27);
    public int Code { get; set; } = 42;
}

public static class BenchDateTimeConverter
{
    public static string Convert(DateTime fieldValue)
        => fieldValue.ToString("yyyy-MM-dd");
}

[MapFrom(typeof(BenchConverterSourceEntity))]
public class BenchConverterDto
{
    public int Id { get; set; }

    [Convert(typeof(BenchDateTimeConverter), nameof(BenchDateTimeConverter.Convert))]
    public string CreateTime { get; set; } = "";

    public int Code { get; set; }
}

// ================================================================
// 管道为空时零开销验证
// ================================================================

/// <summary>
/// 管道为空时零开销 — 对比无管道和有管道的性能差异
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class T5_Pipeline_ZeroOverhead
{
    private BenchConverterSourceEntity _entity = null!;

    [GlobalSetup]
    public void Setup()
    {
        _entity = new BenchConverterSourceEntity { Id = 1, CreateTime = new(2025, 6, 27), Code = 42 };
        // 确保管道为空
        Mapper.Pipeline.Clear();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Mapper.Pipeline.Clear();
    }

    /// <summary>无管道映射 — volatile bool = false，零开销路径</summary>
    [Benchmark(Baseline = true)]
    public BenchConverterDto? Map_WithoutPipeline()
    {
        return Mapper.Map<BenchConverterSourceEntity, BenchConverterDto>(_entity);
    }

    /// <summary>有管道映射 — volatile bool = true，走管道路径</summary>
    [Benchmark]
    public BenchConverterDto? Map_WithPipeline()
    {
        // 注意：此方法在迭代中会被多次调用
        // 管道在 GlobalSetup 中已 Clear，这里临时注册一个空 PostMap
        Mapper.Pipeline.AddPostMap<BenchConverterSourceEntity, BenchConverterDto>((src, dto) => { });
        var result = Mapper.Map<BenchConverterSourceEntity, BenchConverterDto>(_entity);
        Mapper.Pipeline.Clear();
        return result;
    }
}
