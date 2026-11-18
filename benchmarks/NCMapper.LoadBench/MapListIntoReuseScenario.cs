using System.Diagnostics;
using NCMapper;
using NCMapper.Benchmarks;
using NCMapper.Benchmarks.Models;

namespace NCMapper.LoadBench;

/// <summary>
/// MapListInto 复用场景：
/// ① 复用零分配断言：destination 预分配（计入首调）+ 循环复用调用，测量复用调用分配增量 / Gen0 次数增量
/// ② 16T 扩展：每线程预分配独立 (sources, destination) 对（无共享可写状态），线程档位 1/2/4/8/16 测吞吐（每档 3 轮取中位数）
/// ③ R6 未注册类型对比：FlatEntityRuntime/FlatDtoRuntime（无 [MapFrom]，两 API 均走 MapperBase 运行时回退），MapList vs MapListInto 同源对比
/// ④ 同跑基线：Collection_MapList（注册生成路径、每调用分配，与 151232 复测基线同口径）
/// </summary>
internal static class MapListIntoReuseScenario
{
    /// <summary>
    /// 每 op 元素数（与 Collection_MapList 同口径）
    /// </summary>
    private const int Elements = 100;

    /// <summary>
    /// 零分配断言预热轮数（排除首调编译 / JIT 分级，不计入复用测量）
    /// </summary>
    private const int WarmupIters = 2_000;

    /// <summary>
    /// 零分配断言复用调用次数（单线程）
    /// </summary>
    private const int ReuseIters = 10_000;

    /// <summary>
    /// 线程档位上限（每线程预分配独立 destination 池）
    /// </summary>
    private const int MaxThreads = 16;

    /// <summary>
    /// 执行复用场景：零分配断言（单线程）→ 分档吞吐（多线程）。
    /// 协议与 ConcurrencyScenario 一致：每档预热 5000 次 + GC.Collect，每档 rounds 轮取 TPS 中位数。
    /// </summary>
    /// <param name="threadLevels">线程档位</param>
    /// <param name="windowMs">测量窗口时长（毫秒）</param>
    /// <param name="rounds">每档轮数（取中位数）</param>
    /// <returns>复用场景汇总结果</returns>
    internal static ReuseResult Run(IReadOnlyList<int> threadLevels, int windowMs, int rounds = 3)
    {
        Console.WriteLine($"=== MapListInto 复用场景（零分配断言 + 扩展 + 未注册对比，每档 {rounds} 轮取中位数） ===");

        var regSources = BenchmarkData.FlatEntities100;
        var unregSources = BenchmarkData.FlatEntities100Runtime;

        // ---- ① 零分配断言（单线程、复用调用；首调编译 / JIT 分级已排除） ----
        var regDst = PreallocFlatDtos();
        for (int i = 0; i < WarmupIters; i++)
        {
            Mapper.MapListInto<FlatEntity, FlatDto>(regSources, regDst);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long regAllocBefore = GC.GetTotalAllocatedBytes(true);
        int regGen0Before = GC.CollectionCount(0);
        for (int i = 0; i < ReuseIters; i++)
        {
            Mapper.MapListInto<FlatEntity, FlatDto>(regSources, regDst);
        }
        long regAllocDelta = GC.GetTotalAllocatedBytes(true) - regAllocBefore;
        int regGen0Delta = GC.CollectionCount(0) - regGen0Before;

        var unregDst = PreallocRuntimeDtos();
        for (int i = 0; i < WarmupIters; i++)
        {
            Mapper.MapListInto<FlatEntityRuntime, FlatDtoRuntime>(unregSources, unregDst);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long unregAllocBefore = GC.GetTotalAllocatedBytes(true);
        int unregGen0Before = GC.CollectionCount(0);
        for (int i = 0; i < ReuseIters; i++)
        {
            Mapper.MapListInto<FlatEntityRuntime, FlatDtoRuntime>(unregSources, unregDst);
        }
        long unregAllocDelta = GC.GetTotalAllocatedBytes(true) - unregAllocBefore;
        int unregGen0Delta = GC.CollectionCount(0) - unregGen0Before;

        var zeroAllocPass = regAllocDelta == 0 && regGen0Delta == 0;
        Console.WriteLine($"零分配断言（注册复用 ×{ReuseIters}）：Δalloc={regAllocDelta} B，Δgen0={regGen0Delta} → {(zeroAllocPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"零分配参考（未注册复用 ×{ReuseIters}，MapperBase ctx 协议）：Δalloc={unregAllocDelta} B，Δgen0={unregGen0Delta}");

        // ---- ②③④ 分档吞吐（每线程独立预分配 destination，无共享可写状态） ----
        var regPairs = new FlatDto[MaxThreads][];
        for (int t = 0; t < MaxThreads; t++)
        {
            regPairs[t] = PreallocFlatDtos();
        }
        var unregPairs = new FlatDtoRuntime[MaxThreads][];
        for (int t = 0; t < MaxThreads; t++)
        {
            unregPairs[t] = PreallocRuntimeDtos();
        }

        var points = new List<ReusePoint>();
        var tpsByWorkload = new Dictionary<string, Dictionary<int, double>>();

        var workloadDefs = new (string Name, Func<int, Action> MakeWork)[]
        {
            ("ListInto_Reuse", t => (Action)(() => Mapper.MapListInto<FlatEntity, FlatDto>(regSources, regPairs[t]))),
            ("ListInto_Unreg_Reuse", t => (Action)(() => Mapper.MapListInto<FlatEntityRuntime, FlatDtoRuntime>(unregSources, unregPairs[t]))),
            ("MapList_Unreg", _ => (Action)(() => Mapper.MapList<FlatEntityRuntime, FlatDtoRuntime>(unregSources))),
            ("Collection_MapList", _ => (Action)(() => Mapper.MapList<FlatEntity, FlatDto>(regSources))),
        };

        foreach (var (name, makeWork) in workloadDefs)
        {
            Console.WriteLine($"\n[workload: {name}]");
            Console.WriteLine($"{"Threads",-9} {"TotalOps",-12} {"TPS",-12} {"AvgLat(us)",-12} {"ScaleEff",-8}");
            Console.WriteLine(new string('-', 56));

            var works = new Action[MaxThreads];
            for (int t = 0; t < MaxThreads; t++)
            {
                works[t] = makeWork(t);
            }

            var byThreads = new Dictionary<int, double>();
            double singleTps = 0;

            foreach (var threads in threadLevels)
            {
                // 预热：单线程跑 5000 次，确保编译缓存/表达式已命中，测的是稳态并发（协议同 ConcurrencyScenario）
                for (int i = 0; i < 5000; i++)
                {
                    works[0]();
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // 多轮测量取 TPS 中位数（按 TPS 排序取中间轮）
                var roundResults = new List<(double tps, long totalOps, double elapsedMs)>();
                for (int r = 0; r < rounds; r++)
                {
                    var (roundOps, roundMs) = RunWindow(works, threads, windowMs);
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
                byThreads[threads] = tps;
                points.Add(new ReusePoint
                {
                    Workload = name,
                    Threads = threads,
                    TotalOps = totalOps,
                    Tps = tps,
                    AvgLatencyUs = avgLatencyUs,
                    ScaleEfficiency = scaleEff,
                });
                Console.WriteLine($"{threads,-9} {totalOps,-12} {tps,-12:F0} {avgLatencyUs,-12:F2} {scaleEff,-8:F3}  (rounds={string.Join("/", roundResults.Select(x => $"{x.tps:F0}"))})");
            }

            tpsByWorkload[name] = byThreads;
        }

        Console.WriteLine();

        // ---- ③ R6 判定：各档位 ListInto_Unreg_Reuse TPS ≥ MapList_Unreg TPS ----
        var r6Pass = true;
        foreach (var threads in threadLevels)
        {
            var into = tpsByWorkload["ListInto_Unreg_Reuse"][threads];
            var list = tpsByWorkload["MapList_Unreg"][threads];
            var ok = into >= list;
            r6Pass &= ok;
            Console.WriteLine($"R6 对比 @T{threads}：ListInto_Unreg_Reuse {into:F0} TPS vs MapList_Unreg {list:F0} TPS → {(ok ? "PASS" : "FAIL")}");
        }
        Console.WriteLine($"R6 判定（未注册 MapListInto 不劣于 MapList）：{(r6Pass ? "PASS" : "FAIL")}");

        // ---- ② F2 判定：ListInto_Reuse 16T scaleEfficiency ≥ 0.30 ----
        var scaleEff16 = points.FirstOrDefault(p => p.Workload == "ListInto_Reuse" && p.Threads == 16)?.ScaleEfficiency ?? 0;
        var f2Pass = scaleEff16 >= 0.30;
        Console.WriteLine($"F2 扩展判据（ListInto_Reuse 16T scaleEff={scaleEff16:F3}，目标 ≥0.30）：{(f2Pass ? "PASS" : "FAIL → F2 回流")}");

        Console.WriteLine();

        var verdict = !zeroAllocPass && !r6Pass
            ? "零分配断言 FAIL + R6 FAIL"
            : !zeroAllocPass
                ? "零分配断言 FAIL"
                : !r6Pass
                    ? "R6 FAIL"
                    : f2Pass
                        ? "PASS"
                        : "F2 扩展判据 FAIL";

        return new ReuseResult
        {
            Elements = Elements,
            ReuseIters = ReuseIters,
            RegReuseAllocBytes = regAllocDelta,
            RegReuseGen0 = regGen0Delta,
            UnregReuseAllocBytes = unregAllocDelta,
            UnregReuseGen0 = unregGen0Delta,
            ZeroAllocPass = zeroAllocPass,
            R6Pass = r6Pass,
            F2ScalePass = f2Pass,
            ListIntoReuseScaleEff16T = scaleEff16,
            Verdict = verdict,
            Points = points,
        };
    }

    /// <summary>
    /// 预分配填满的 destination 数组（每槽非 null，复用就地更新）
    /// </summary>
    private static FlatDto[] PreallocFlatDtos()
    {
        var dst = new FlatDto[Elements];
        for (int i = 0; i < Elements; i++)
        {
            dst[i] = new FlatDto();
        }
        return dst;
    }

    /// <summary>
    /// 预分配填满的未注册类型 destination 数组
    /// </summary>
    private static FlatDtoRuntime[] PreallocRuntimeDtos()
    {
        var dst = new FlatDtoRuntime[Elements];
        for (int i = 0; i < Elements; i++)
        {
            dst[i] = new FlatDtoRuntime();
        }
        return dst;
    }

    /// <summary>
    /// 并行执行一个测量窗口：Barrier 同步起跑，各线程独立计数至窗口结束（每线程使用专属预分配 destination）
    /// </summary>
    /// <param name="works">每线程动作（索引 = 线程序号）</param>
    /// <param name="threads">并发线程数</param>
    /// <param name="windowMs">测量窗口时长（毫秒）</param>
    /// <returns>元组：总操作数、实际耗时（毫秒）</returns>
    private static (long totalOps, double elapsedMs) RunWindow(Action[] works, int threads, int windowMs)
    {
        var ready = new Barrier(threads + 1);
        var counters = new long[threads];
        var threadsRef = new Thread[threads];

        for (int t = 0; t < threads; t++)
        {
            var index = t;
            var work = works[t];
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
