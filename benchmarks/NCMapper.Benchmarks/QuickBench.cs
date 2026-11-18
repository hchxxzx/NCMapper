using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using NCMapper.Benchmarks.Models;
using NCMapper.Generated;

namespace NCMapper.Benchmarks;

/// <summary>
/// 快速验证 — 最小 benchmark 集，1 launch × 3 warmup × 5 iteration
/// </summary>
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class Quick_NCMapper_Flat
{
    [Benchmark]
    public FlatDto Run() => NCMapper.Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity)!;
}

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class Quick_NCMapper_Nullable
{
    [Benchmark]
    public NullableDto Run() => NCMapper.Mapper.Map<NullableEntity, NullableDto>(BenchmarkData.NullableEntityData)!;
}

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class Quick_NCMapper_BidirED
{
    [Benchmark]
    public FlatDto Run() => NCMapper.Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity)!;
}

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class Quick_NCMapper_Record
{
    [Benchmark]
    public RecordDto Run() => NCMapper.Mapper.Map<RecordEntity, RecordDto>(BenchmarkData.RecordEntityData)!;
}

// 对照组
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class Quick_Mapperly_Flat
{
    [Benchmark]
    public FlatDto Run() => BenchmarkData.Mapperly.FlatEntityToDto(BenchmarkData.FlatEntity);
}

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class Quick_Mapperly_Nullable
{
    [Benchmark]
    public NullableDto Run() => BenchmarkData.Mapperly.NullableEntityToDto(BenchmarkData.NullableEntityData);
}

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class Quick_Mapperly_Record
{
    [Benchmark]
    public RecordDto Run() => BenchmarkData.Mapperly.RecordEntityToDto(BenchmarkData.RecordEntityData);
}

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class Quick_Ext_Flat
{
    [Benchmark]
    public FlatDto Run() => BenchmarkData.FlatEntity.MapToFlatDto()!;
}

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class Quick_Ext_Nullable
{
    [Benchmark]
    public NullableDto Run() => BenchmarkData.NullableEntityData.MapToNullableDto()!;
}

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class Quick_Ext_Record
{
    [Benchmark]
    public RecordDto Run() => BenchmarkData.RecordEntityData.MapToRecordDto()!;
}
