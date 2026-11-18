using System.Diagnostics;
using NCMapper;
using NCMapper.Benchmarks;
using NCMapper.Benchmarks.Models;

namespace NCMapper.LoadBench;

/// <summary>
/// 持久运行压测场景：长时间混合映射负载下采样内存与 GC 状态，判定是否存在泄漏
/// </summary>
internal static class EnduranceScenario
{
    /// <summary>
    /// 混合负载单次动作：单对象映射 / 集合映射 / 深拷贝轮换
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
    /// 执行持久运行压测：定时采样托管堆/总分配/GC 次数/工作集，输出时间序列与泄漏判定
    /// </summary>
    /// <param name="durationSec">场景总时长（秒）</param>
    /// <param name="intervalSec">采样间隔（秒）</param>
    /// <returns>持久运行结果（含采样序列与判定）</returns>
    internal static EnduranceResult Run(int durationSec, int intervalSec)
    {
        Console.WriteLine($"=== 持久运行场景（{durationSec}s 混合负载，每 {intervalSec}s 采样） ===");

        var process = Process.GetCurrentProcess();
        var samples = new List<EnduranceSample>();
        var sw = Stopwatch.StartNew();
        double nextSampleAt = 0;

        // 预热缓存后清零分配基线
        for (int i = 0; i < 5000; i++)
        {
            RunMixed();
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();

        while (sw.Elapsed.TotalSeconds < durationSec)
        {
            if (sw.Elapsed.TotalSeconds >= nextSampleAt)
            {
                samples.Add(new EnduranceSample
                {
                    TimeSec = Math.Round(sw.Elapsed.TotalSeconds, 1),
                    ManagedHeapMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0,
                    TotalAllocMB = GC.GetTotalAllocatedBytes() / 1024.0 / 1024.0,
                    Gen0 = GC.CollectionCount(0),
                    Gen1 = GC.CollectionCount(1),
                    Gen2 = GC.CollectionCount(2),
                    WorkingSetMB = process.WorkingSet64 / 1024.0 / 1024.0,
                });
                Console.WriteLine($"  t={samples[^1].TimeSec,5:F1}s  Heap={samples[^1].ManagedHeapMB,8:F2}MB  Alloc={samples[^1].TotalAllocMB,8:F2}MB  Gen0={samples[^1].Gen0,5}  Gen2={samples[^1].Gen2,4}  WS={samples[^1].WorkingSetMB,8:F1}MB");
                nextSampleAt += intervalSec;
            }
            for (int i = 0; i < 2000; i++)
            {
                RunMixed();
            }
        }
        sw.Stop();

        // 综合判定（避免瞬时 heap 前后段均值受 GC 时机振荡误判）：
        //   1. 分配速率（TotalAlloc 斜率）——高分配是吞吐瓶颈的 GC 压力主因
        //   2. Gen2 累计次数增量——持续增长说明大对象/缓存压力
        //   3. 工作集增量——稳定说明无原生内存/缓存泄漏
        double ratio = 1.0;
        string verdict = "数据不足";
        if (samples.Count >= 4)
        {
            var first = samples[0];
            var last = samples[^1];
            var spanSec = Math.Max(last.TimeSec - first.TimeSec, 1.0);
            var allocRateMBs = (last.TotalAllocMB - first.TotalAllocMB) / spanSec;
            var gen2Delta = last.Gen2 - first.Gen2;
            var wsDeltaMB = last.WorkingSetMB - first.WorkingSetMB;

            // 托管堆峰值有界性：后 1/3 段峰值 / 前 1/3 段峰值
            var headMax = samples.Take(samples.Count / 3).Select(s => s.ManagedHeapMB).Max();
            var tailMax = samples.Skip(samples.Count * 2 / 3).Select(s => s.ManagedHeapMB).Max();
            ratio = headMax > 0 ? tailMax / headMax : 1.0;

            var notes = new List<string>();
            if (allocRateMBs > 100)
            {
                notes.Add($"高分配速率({allocRateMBs:F0}MB/s)");
            }
            if (gen2Delta > 30)
            {
                notes.Add($"Gen2 回收频繁(+{gen2Delta})");
            }
            if (wsDeltaMB > 50)
            {
                notes.Add($"工作集增长({wsDeltaMB:F0}MB)");
            }

            if (headMax < 50)
            {
                // 绝对量门槛：堆峰值 < 50MB 时 ratio 无意义（MB 级噪声，如 DATAS 形态 0.2-3MB），跳过
                verdict = notes.Count == 0
                    ? "收敛（无泄漏迹象；堆峰值<50MB，跳过 ratio 判定）"
                    : string.Join("；", notes) + "；堆峰值<50MB";
            }
            else
            {
                verdict = notes.Count == 0
                    ? "收敛（无泄漏迹象）"
                    : string.Join("；", notes) + (ratio > 1.5 ? "；托管堆峰值增长" : "；托管堆峰值有界");
            }
        }

        var allocRateDisplay = samples.Count >= 4
            ? $"{((samples[^1].TotalAllocMB - samples[0].TotalAllocMB) / Math.Max(samples[^1].TimeSec - samples[0].TimeSec, 1.0)):F0}MB/s"
            : "N/A";
        Console.WriteLine($"  分配速率={allocRateDisplay}  堆峰值趋势(尾/首)={ratio:F3} → {verdict}");
        Console.WriteLine();

        return new EnduranceResult
        {
            DurationSec = durationSec,
            SampleIntervalSec = intervalSec,
            Samples = samples,
            HeapTrendRatio = ratio,
            Verdict = verdict,
        };
    }
}
