using System.Diagnostics;
using NCMapper;
using NCMapper.Benchmarks;
using NCMapper.Benchmarks.Models;

namespace NCMapper.LoadBench;

/// <summary>
/// 冷启动压测场景：量化"高峰突增时新类型首次出现"的编译/缓存构建开销
/// </summary>
internal static class ColdStartScenario
{
    /// <summary>
    /// 单条冷启动用例定义：类型对、首次调用动作与源生成器判定
    /// </summary>
    internal sealed class ColdStartCase
    {
        /// <summary>
        /// 场景名称（与 SelfBench 微基准同名，便于对照）
        /// </summary>
        internal required string Scenario { get; init; }

        /// <summary>
        /// 源类型
        /// </summary>
        internal required Type Source { get; init; }

        /// <summary>
        /// 目标类型
        /// </summary>
        internal required Type Target { get; init; }

        /// <summary>
        /// 映射调用动作（首次调用即执行此闭包）
        /// </summary>
        internal required Action Work { get; init; }

        /// <summary>
        /// 源生成器（Ext）形态判定委托
        /// </summary>
        internal required Func<bool> IsGenerated { get; init; }
    }

    /// <summary>
    /// 构建标准用例集：与 SelfBench 微基准使用相同类型对与测试数据，保证结果可对照
    /// </summary>
    /// <returns>冷启动用例列表</returns>
    internal static List<ColdStartCase> BuildCases()
    {
        return
        [
            new ColdStartCase
            {
                Scenario = "Flat_Map",
                Source = typeof(FlatEntity),
                Target = typeof(FlatDto),
                Work = () => Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity),
                IsGenerated = () => Mapper.HasGenerated<FlatEntity, FlatDto>(),
            },
            new ColdStartCase
            {
                Scenario = "Nested_Map",
                Source = typeof(NestedEntity),
                Target = typeof(NestedDto),
                Work = () => Mapper.Map<NestedEntity, NestedDto>(BenchmarkData.NestedEntity),
                IsGenerated = () => Mapper.HasGenerated<NestedEntity, NestedDto>(),
            },
            new ColdStartCase
            {
                Scenario = "Record_Map",
                Source = typeof(RecordEntity),
                Target = typeof(RecordDto),
                Work = () => Mapper.Map<RecordEntity, RecordDto>(BenchmarkData.RecordEntityData),
                IsGenerated = () => Mapper.HasGenerated<RecordEntity, RecordDto>(),
            },
            new ColdStartCase
            {
                Scenario = "RecordBig_Map",
                Source = typeof(RecordBigEntity),
                Target = typeof(RecordBigDto),
                Work = () => Mapper.Map<RecordBigEntity, RecordBigDto>(BenchmarkData.RecordBigEntityData),
                IsGenerated = () => Mapper.HasGenerated<RecordBigEntity, RecordBigDto>(),
            },
            new ColdStartCase
            {
                Scenario = "NameDiffBig_Map",
                Source = typeof(NameDiffBigEntity),
                Target = typeof(NameDiffBigDto),
                Work = () => Mapper.Map<NameDiffBigEntity, NameDiffBigDto>(BenchmarkData.NameDiffBigEntityData),
                IsGenerated = () => Mapper.HasGenerated<NameDiffBigEntity, NameDiffBigDto>(),
            },
            new ColdStartCase
            {
                Scenario = "AutoFlatten_Map",
                Source = typeof(FlattenParentEntity),
                Target = typeof(FlattenDto),
                Work = () => Mapper.Map<FlattenParentEntity, FlattenDto>(BenchmarkData.FlattenEntityData),
                IsGenerated = () => Mapper.HasGenerated<FlattenParentEntity, FlattenDto>(),
            },
            new ColdStartCase
            {
                Scenario = "Collection_MapList",
                Source = typeof(FlatEntity),
                Target = typeof(FlatDto),
                Work = () => Mapper.MapList<FlatEntity, FlatDto>(BenchmarkData.FlatEntities100),
                IsGenerated = () => Mapper.HasGenerated<FlatEntity, FlatDto>(),
            },
            new ColdStartCase
            {
                Scenario = "DeepCopy_Fast",
                Source = typeof(DeepCopyEntity),
                Target = typeof(DeepCopyEntity),
                Work = () => Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData),
                IsGenerated = () => Mapper.HasGeneratedDeepCopy<DeepCopyEntity>(),
            },
        ];
    }

    /// <summary>
    /// 测量并触发进程级一次性初始化：访问 BenchmarkData 触发 ModuleInitializer 委托注册
    /// 与静态构造，再用用例集外的类型对（DapperUserEntity→DapperUserDto）做 dummy 映射，
    /// 使后续各用例的首次调用只承担自身的首次 JIT（分离"进程冷启动"与"类型对冷启动"）。
    /// 注：进程初始化（ModuleInitializer）进程内仅发生一次，故取首次测量值；
    /// 测量稳定性问题由各用例自身的首次 JIT 多次测量缓解。
    /// </summary>
    /// <returns>进程级初始化耗时（微秒）</returns>
    internal static double MeasureProcessInit()
    {
        var sw = Stopwatch.StartNew();
        _ = BenchmarkData.FlatEntity;
        Mapper.Map<DapperUserEntity, DapperUserDto>(new DapperUserEntity());
        sw.Stop();
        return sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency;
    }

    /// <summary>
    /// 执行冷启动压测：每个用例测首次调用耗时与稳态均值，计算冷启动开销占比
    /// </summary>
    /// <param name="cases">冷启动用例列表</param>
    /// <returns>冷启动结果列表</returns>
    internal static List<ColdStartResult> Run(List<ColdStartCase> cases)
    {
        // 前置触发进程级初始化，确保各用例首次调用仅含自身 JIT（进程冷启动单独作为首行指标）
        var processInitUs = MeasureProcessInit();
        Console.WriteLine($"  [进程级初始化] ModuleInitializer+BenchmarkData 静态构造+dummy 映射 = {processInitUs:F1}µs（独立于各用例）");

        Console.WriteLine("=== 冷启动场景（首次调用 vs 稳态） ===");
        Console.WriteLine($"{"Scenario",-22} {"Ext",-5} {"First(us)",-12} {"Steady(us)",-12} {"Ratio",-8}");
        Console.WriteLine(new string('-', 64));

        var results = new List<ColdStartResult>();
        foreach (var c in cases)
        {
            // 首次调用：该类型对在进程内从未被映射过，耗时为编译+缓存构建+首次执行
            var sw = Stopwatch.StartNew();
            c.Work();
            sw.Stop();
            var firstUs = sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency;

            // 稳态：紧接着连续 99 次，取平均
            long totalTicks = 0;
            for (int i = 0; i < 99; i++)
            {
                var sw2 = Stopwatch.StartNew();
                c.Work();
                sw2.Stop();
                totalTicks += sw2.ElapsedTicks;
            }
            var steadyUs = totalTicks * 1_000_000.0 / 99.0 / Stopwatch.Frequency;

            var isGen = c.IsGenerated();
            var ratio = steadyUs > 0 ? firstUs / steadyUs : 0;
            results.Add(new ColdStartResult
            {
                Scenario = c.Scenario,
                Source = c.Source.Name,
                Target = c.Target.Name,
                IsGenerated = isGen,
                FirstCallUs = firstUs,
                SteadyUs = steadyUs,
                Ratio = ratio,
            });

            Console.WriteLine($"{c.Scenario,-22} {(isGen ? "yes" : "no"),-5} {firstUs,-12:F1} {steadyUs,-12:F3} {ratio,-8:F1}");
        }

        // 进程级初始化作为独立指标行（首行），与各类型对冷启动分离呈现
        results.Insert(0, new ColdStartResult
        {
            Scenario = "ProcessInit_ModuleInitializer",
            Source = "<进程>",
            Target = "<ModuleInitializer+静态构造>",
            IsGenerated = false,
            FirstCallUs = processInitUs,
            SteadyUs = 0,
            Ratio = 0,
        });

        Console.WriteLine();
        return results;
    }
}
