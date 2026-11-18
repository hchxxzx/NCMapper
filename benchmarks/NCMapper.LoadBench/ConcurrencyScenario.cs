using System.Diagnostics;
using NCMapper;
using NCMapper.Benchmarks;
using NCMapper.Benchmarks.Models;

namespace NCMapper.LoadBench;

/// <summary>
/// 高并发压测场景：多线程并发调用映射，量化吞吐扩展性与锁竞争影响
/// </summary>
internal static class ConcurrencyScenario
{
    /// <summary>
    /// 单个工作负载定义：名称与并发调用动作
    /// </summary>
    internal sealed class Workload
    {
        /// <summary>
        /// 工作负载名称
        /// </summary>
        internal required string Name { get; init; }

        /// <summary>
        /// 单次调用动作（每线程循环执行）
        /// </summary>
        internal required Action Work { get; init; }
    }

    /// <summary>
    /// 构建标准工作负载集：单对象映射 / 集合映射 / 深拷贝 / 混合轮换
    /// </summary>
    /// <returns>工作负载列表</returns>
    internal static List<Workload> BuildWorkloads()
    {
        return
        [
            new Workload { Name = "Flat_Map", Work = () => Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity) },
            new Workload { Name = "Collection_MapList", Work = () => Mapper.MapList<FlatEntity, FlatDto>(BenchmarkData.FlatEntities100) },
            new Workload { Name = "DeepCopy_Fast", Work = () => Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData) },
            new Workload { Name = "Mixed", Work = RunMixed },
        ];
    }

    /// <summary>
    /// 混合负载单次动作：三种操作轮换执行
    /// </summary>
    private static int _mixIndex;

    /// <summary>
    /// 混合负载轮换动作体
    /// </summary>
    private static void RunMixed()
    {
        var i = Interlocked.Increment(ref _mixIndex) % 3;
        switch (i)
        {
            case 0:
                Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity);
                break;
            case 1:
                Mapper.MapList<FlatEntity, FlatDto>(BenchmarkData.FlatEntities100);
                break;
            default:
                Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData);
                break;
        }
    }

    /// <summary>
    /// 执行高并发压测：对每个工作负载依次测量各线程数档位的吞吐与扩展效率。
    /// 每档位测量 rounds 轮取 TPS 中位数（单轮 1T 基准存在离群污染风险）
    /// </summary>
    /// <param name="workloads">工作负载列表</param>
    /// <param name="threadLevels">并发档位（线程数）列表</param>
    /// <param name="windowMs">每轮测量窗口时长（毫秒）</param>
    /// <param name="rounds">每档位轮数（取中位数，默认 3）</param>
    /// <returns>按工作负载分组的并发结果字典</returns>
    internal static Dictionary<string, List<ConcurrencyPoint>> Run(
        List<Workload> workloads,
        IReadOnlyList<int> threadLevels,
        int windowMs,
        int rounds = 3)
    {
        Console.WriteLine($"=== 高并发场景（并发映射吞吐与扩展性，每档 {rounds} 轮取中位数） ===");
        var result = new Dictionary<string, List<ConcurrencyPoint>>();

        foreach (var wl in workloads)
        {
            Console.WriteLine($"\n[workload: {wl.Name}]");
            Console.WriteLine($"{"Threads",-9} {"TotalOps",-12} {"TPS",-12} {"AvgLat(us)",-12} {"ScaleEff",-8}");
            Console.WriteLine(new string('-', 56));

            var points = new List<ConcurrencyPoint>();
            double singleTps = 0;

            foreach (var threads in threadLevels)
            {
                // 预热：单线程跑 5000 次，确保编译缓存/表达式已命中，测的是稳态并发
                for (int i = 0; i < 5000; i++)
                {
                    wl.Work();
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // 多轮测量取 TPS 中位数（按 TPS 排序取中间轮）
                var roundResults = new List<(double tps, long totalOps, double elapsedMs)>();
                for (int r = 0; r < rounds; r++)
                {
                    var (roundOps, roundMs) = RunWindow(wl.Work, threads, windowMs);
                    roundResults.Add((roundOps * 1000.0 / roundMs, roundOps, roundMs));
                }
                var median = roundResults.OrderBy(x => x.tps).ElementAt(roundResults.Count / 2);
                var tps = median.tps;
                var totalOps = median.totalOps;
                var avgLatencyUs = threads * median.elapsedMs * 1000.0 / totalOps;
                if (threads == 1)
                {
                    singleTps = tps;
                }

                var scaleEff = singleTps > 0 && threads > 1 ? tps / (singleTps * threads) : 1.0;
                points.Add(new ConcurrencyPoint
                {
                    Workload = wl.Name,
                    Threads = threads,
                    TotalOps = totalOps,
                    Tps = tps,
                    AvgLatencyUs = avgLatencyUs,
                    ScaleEfficiency = scaleEff,
                });

                Console.WriteLine($"{threads,-9} {totalOps,-12} {tps,-12:F0} {avgLatencyUs,-12:F2} {scaleEff,-8:F3}  (rounds={string.Join("/", roundResults.Select(x => $"{x.tps:F0}"))})");
            }

            result[wl.Name] = points;
        }

        Console.WriteLine();
        return result;
    }

    /// <summary>
    /// 并行执行一个测量窗口：Barrier 同步起跑，各线程独立计数至时间窗结束
    /// </summary>
    /// <param name="work">单次调用动作</param>
    /// <param name="threads">并发线程数</param>
    /// <param name="windowMs">测量窗口时长（毫秒）</param>
    /// <returns>元组：总操作数、实际耗时（毫秒）</returns>
    private static (long totalOps, double elapsedMs) RunWindow(Action work, int threads, int windowMs)
    {
        var ready = new Barrier(threads + 1);
        var counters = new long[threads];
        var threadsRef = new Thread[threads];

        for (int t = 0; t < threads; t++)
        {
            var index = t;
            threadsRef[t] = new Thread(() =>
            {
                ready.SignalAndWait();
                var sw = Stopwatch.StartNew();
                long local = 0;
                while (sw.ElapsedMilliseconds < windowMs)
                {
                    work();
                    local++;
                }
                sw.Stop();
                counters[index] = local;
            });
            threadsRef[t].IsBackground = true;
            threadsRef[t].Start();
        }

        var masterSw = Stopwatch.StartNew();
        ready.SignalAndWait();
        foreach (var th in threadsRef)
        {
            th.Join();
        }
        masterSw.Stop();

        return (counters.Sum(), masterSw.ElapsedMilliseconds);
    }
}
