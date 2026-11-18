// ============================================================================
// RuntimeSnapshotStore 消费读 vs 反射特性读 性能正式基准
//
// 探针量级（i7-12700K，Stopwatch 粗测）：
//   稳态（100 万次迭代 IsDefined+GetCustomAttribute.FromSource vs IsRegisteredDto+GetPrimarySource）≈ 55 倍快
//   冷启动（10 类型单轮）≈ 17 倍快
// 探针局限：非 BDN（无多轮统计/内存指标）。本文件用 BDN 正式基准确认量级方向（含 GC/分配维度）。
//
// 设计：量化消费端改造的核心操作——
//   on 热路径：RuntimeSnapshotStore.TryGetMappingMeta(type, out view)（注册表 O(1) 查询）
//   off 兜底：反射 IsDefined + GetCustomAttribute（改造前现状，反射读 [MapFrom]）
// 两路径独立 benchmark 类（BDN 跨进程隔离，避免全局 Register 状态串扰）。
// 验收：BDN 量级方向与探针 55/17 倍一致即视为达成（多轮取中位数）。
// ============================================================================
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using NCMapper.Attributes;
using NCMapper.Benchmarks.Models;
using NCMapper.Runtime;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NCMapper.Benchmarks;

/// <summary>消费端判定输入基准类型池（真实 [MapFrom] DTO——FlatDto 等经 ModuleInitializer 注册）</summary>
internal static class RsBenchTypes
{
    public static readonly Type[] Types =
    {
        typeof(FlatDto), typeof(InnerDto), typeof(NestedDto),
        typeof(NameDiffDto), typeof(EnumToStringDto), typeof(RecordBigDto),
    };
}

// ────────────────────────────────────────────────────────────────────────────
// on 热路径：注册表读（RuntimeSnapshotStore.TryGetMappingMeta）
// 前置：工程开关 on 构建 → FlatDto 等 [MapFrom] DTO 经 [ModuleInitializer] RuntimeSnapshot 注册
// ────────────────────────────────────────────────────────────────────────────
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class RuntimeSnapshot_RegistryRead
{
    private static readonly Type[] s_types = RsBenchTypes.Types;

    [Benchmark(Baseline = true)]
    [ArgumentsSource(nameof(TypeArgs))]
    public bool TryGetMappingMeta(Type type) => RuntimeSnapshotStore.TryGetMappingMeta(type, out _);
    public IEnumerable<Type> TypeArgs() => s_types;

    [Benchmark]
    [ArgumentsSource(nameof(TypeArgs))]
    public bool IsRegisteredDtoOnly(Type type) => RuntimeSnapshotStore.IsRegisteredDto(type);
}

// ────────────────────────────────────────────────────────────────────────────
// off 兜底：反射读 [MapFrom]（改造前现状——IsDefined + GetCustomAttribute 双查）
// ────────────────────────────────────────────────────────────────────────────
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class RuntimeSnapshot_ReflectionRead
{
    private static readonly Type[] s_types = RsBenchTypes.Types;

    [Benchmark]
    [ArgumentsSource(nameof(TypeArgs))]
    public bool IsDefinedOnly(Type type) => type.IsDefined(typeof(MapFromAttribute), inherit: false);

    [Benchmark]
    [ArgumentsSource(nameof(TypeArgs))]
    public Type? IsDefinedPlusGetCustomAttribute(Type type)
    {
        // 镜像改造前现状：投影内核先 IsDefined 判 DTO，命中再 GetCustomAttribute 读主源 FromSource
        if (!type.IsDefined(typeof(MapFromAttribute), inherit: false)) return null;
        return type.GetCustomAttribute<MapFromAttribute>()?.FromSource;
    }

    public IEnumerable<Type> TypeArgs() => s_types;
}
