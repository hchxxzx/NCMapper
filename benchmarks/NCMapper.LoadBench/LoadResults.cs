using System.Text.Json;
using System.Text.Json.Serialization;

namespace NCMapper.LoadBench;

/// <summary>
/// 冷启动场景单条结果：某类型对首次调用耗时与稳态均值的对比
/// </summary>
internal sealed class ColdStartResult
{
    /// <summary>
    /// 场景名称（如 Flat_Map）
    /// </summary>
    [JsonPropertyName("scenario")]
    public required string Scenario { get; init; }

    /// <summary>
    /// 源类型名称
    /// </summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>
    /// 目标类型名称
    /// </summary>
    [JsonPropertyName("target")]
    public required string Target { get; init; }

    /// <summary>
    /// 是否为源生成器（Ext）形态
    /// </summary>
    [JsonPropertyName("isGenerated")]
    public required bool IsGenerated { get; init; }

    /// <summary>
    /// 首次调用耗时（微秒，含表达式编译/缓存构建开销）
    /// </summary>
    [JsonPropertyName("firstCallUs")]
    public required double FirstCallUs { get; init; }

    /// <summary>
    /// 第 2~100 次调用均值（微秒，稳态）
    /// </summary>
    [JsonPropertyName("steadyUs")]
    public required double SteadyUs { get; init; }

    /// <summary>
    /// 首次与稳态的比值（越大说明编译/冷启动开销占比越高）
    /// </summary>
    [JsonPropertyName("ratio")]
    public required double Ratio { get; init; }
}

/// <summary>
/// 高并发场景单档位结果：固定线程数下的吞吐与延迟
/// </summary>
internal sealed class ConcurrencyPoint
{
    /// <summary>
    /// 工作负载名称（如 Flat_Map / Mixed）
    /// </summary>
    [JsonPropertyName("workload")]
    public required string Workload { get; init; }

    /// <summary>
    /// 并发线程数
    /// </summary>
    [JsonPropertyName("threads")]
    public required int Threads { get; init; }

    /// <summary>
    /// 测量窗口内总操作数
    /// </summary>
    [JsonPropertyName("totalOps")]
    public required long TotalOps { get; init; }

    /// <summary>
    /// 吞吐量（ops/s）
    /// </summary>
    [JsonPropertyName("tps")]
    public required double Tps { get; init; }

    /// <summary>
    /// 平均单次调用延迟（微秒）
    /// </summary>
    [JsonPropertyName("avgLatencyUs")]
    public required double AvgLatencyUs { get; init; }

    /// <summary>
    /// 相对单线程的扩展效率（2 线程理论 2.0，1.0 为完全无扩展）
    /// </summary>
    [JsonPropertyName("scaleEfficiency")]
    public required double ScaleEfficiency { get; init; }
}

/// <summary>
/// 持久运行场景单次采样：某一时刻的内存与 GC 状态
/// </summary>
internal sealed class EnduranceSample
{
    /// <summary>
    /// 距场景开始的秒数
    /// </summary>
    [JsonPropertyName("timeSec")]
    public required double TimeSec { get; init; }

    /// <summary>
    /// 当前托管堆大小（MB）
    /// </summary>
    [JsonPropertyName("managedHeapMB")]
    public required double ManagedHeapMB { get; init; }

    /// <summary>
    /// 进程累计分配的托管字节（MB）
    /// </summary>
    [JsonPropertyName("totalAllocMB")]
    public required double TotalAllocMB { get; init; }

    /// <summary>
    /// Gen0 累计回收次数
    /// </summary>
    [JsonPropertyName("gen0")]
    public required long Gen0 { get; init; }

    /// <summary>
    /// Gen1 累计回收次数
    /// </summary>
    [JsonPropertyName("gen1")]
    public required long Gen1 { get; init; }

    /// <summary>
    /// Gen2 累计回收次数
    /// </summary>
    [JsonPropertyName("gen2")]
    public required long Gen2 { get; init; }

    /// <summary>
    /// 进程工作集（MB，含原生内存）
    /// </summary>
    [JsonPropertyName("workingSetMB")]
    public required double WorkingSetMB { get; init; }
}

/// <summary>
/// 持久运行场景汇总：含采样序列与内存收敛判定
/// </summary>
internal sealed class EnduranceResult
{
    /// <summary>
    /// 场景总时长（秒）
    /// </summary>
    [JsonPropertyName("durationSec")]
    public required double DurationSec { get; init; }

    /// <summary>
    /// 采样间隔（秒）
    /// </summary>
    [JsonPropertyName("sampleIntervalSec")]
    public required double SampleIntervalSec { get; init; }

    /// <summary>
    /// 采样序列
    /// </summary>
    [JsonPropertyName("samples")]
    public required List<EnduranceSample> Samples { get; init; }

    /// <summary>
    /// 内存收敛判定：末段托管堆均值 / 首段托管堆均值，接近 1 说明收敛（无泄漏）
    /// </summary>
    [JsonPropertyName("heapTrendRatio")]
    public required double HeapTrendRatio { get; init; }

    /// <summary>
    /// 结论文本（收敛 / 疑似泄漏）
    /// </summary>
    [JsonPropertyName("verdict")]
    public required string Verdict { get; init; }
}

/// <summary>
/// MapListInto 复用场景单档结果（口径同 ConcurrencyPoint）
/// </summary>
internal sealed class ReusePoint
{
    /// <summary>
    /// 工作负载名（ListInto_Reuse / ListInto_Unreg_Reuse / MapList_Unreg / Collection_MapList）
    /// </summary>
    [JsonPropertyName("workload")]
    public required string Workload { get; init; }

    /// <summary>
    /// 线程档位
    /// </summary>
    [JsonPropertyName("threads")]
    public required int Threads { get; init; }

    /// <summary>
    /// 窗口内总操作数（中位数轮）
    /// </summary>
    [JsonPropertyName("totalOps")]
    public required long TotalOps { get; init; }

    /// <summary>
    /// 吞吐（各轮 TPS 中位数）
    /// </summary>
    [JsonPropertyName("tps")]
    public required double Tps { get; init; }

    /// <summary>
    /// 平均延迟（微秒）：threads × 窗口耗时 / 总操作数
    /// </summary>
    [JsonPropertyName("avgLatencyUs")]
    public required double AvgLatencyUs { get; init; }

    /// <summary>
    /// 扩展效率：tps / (1T tps × threads)
    /// </summary>
    [JsonPropertyName("scaleEfficiency")]
    public required double ScaleEfficiency { get; init; }
}

/// <summary>
/// MapListInto 复用场景汇总：零分配断言 + 分档吞吐
/// </summary>
internal sealed class ReuseResult
{
    /// <summary>
    /// 每 op 元素数
    /// </summary>
    [JsonPropertyName("elements")]
    public required int Elements { get; init; }

    /// <summary>
    /// 零分配断言复用调用次数（单线程）
    /// </summary>
    [JsonPropertyName("reuseIters")]
    public required int ReuseIters { get; init; }

    /// <summary>
    /// 注册对复用调用分配增量（字节）；0 = 零分配断言通过
    /// </summary>
    [JsonPropertyName("regReuseAllocBytes")]
    public required long RegReuseAllocBytes { get; init; }

    /// <summary>
    /// 注册对复用调用 Gen0 回收次数增量；0 = 零分配断言通过
    /// </summary>
    [JsonPropertyName("regReuseGen0")]
    public required int RegReuseGen0 { get; init; }

    /// <summary>
    /// 未注册对复用调用分配增量（字节，参考值：MapperBase ctx 协议）
    /// </summary>
    [JsonPropertyName("unregReuseAllocBytes")]
    public required long UnregReuseAllocBytes { get; init; }

    /// <summary>
    /// 未注册对复用调用 Gen0 回收次数增量（参考值）
    /// </summary>
    [JsonPropertyName("unregReuseGen0")]
    public required int UnregReuseGen0 { get; init; }

    /// <summary>
    /// 零分配断言通过（注册复用 Δalloc==0 且 Δgen0==0）
    /// </summary>
    [JsonPropertyName("zeroAllocPass")]
    public required bool ZeroAllocPass { get; init; }

    /// <summary>
    /// R6 对比通过（各档位 ListInto_Unreg_Reuse TPS ≥ MapList_Unreg TPS）
    /// </summary>
    [JsonPropertyName("r6Pass")]
    public required bool R6Pass { get; init; }

    /// <summary>
    /// F2 扩展判据通过（ListInto_Reuse 16T scaleEfficiency ≥ 0.30）
    /// </summary>
    [JsonPropertyName("f2ScalePass")]
    public required bool F2ScalePass { get; init; }

    /// <summary>
    /// ListInto_Reuse 16T scaleEfficiency（F2 证伪点：&lt;0.30 为 F2）
    /// </summary>
    [JsonPropertyName("listIntoReuseScaleEff16T")]
    public required double ListIntoReuseScaleEff16T { get; init; }

    /// <summary>
    /// 结论文本（PASS / 各判据 FAIL / 未运行）
    /// </summary>
    [JsonPropertyName("verdict")]
    public required string Verdict { get; init; }

    /// <summary>
    /// 分档结果（4 工作负载 × 各线程档位）
    /// </summary>
    [JsonPropertyName("points")]
    public required List<ReusePoint> Points { get; init; }
}

/// <summary>
/// 全部场景汇总结果（JSON 落盘对象）
/// </summary>
internal sealed class LoadSummary
{
    /// <summary>
    /// 运行环境信息（TFM / 机器 / 时间）
    /// </summary>
    [JsonPropertyName("environment")]
    public required string Environment { get; init; }

    /// <summary>
    /// GC 模式：DOTNET_GCDynamicAdaptationMode 环境变量值（"0"=关 DATAS，"1"=开，null=运行时默认）
    /// </summary>
    [JsonPropertyName("gcMode")]
    public required string GcMode { get; init; }

    /// <summary>
    /// 并发场景每档位测量轮数（取中位数）
    /// </summary>
    [JsonPropertyName("concurrencyRounds")]
    public required int ConcurrencyRounds { get; init; }

    /// <summary>
    /// 冷启动场景结果
    /// </summary>
    [JsonPropertyName("coldStart")]
    public required List<ColdStartResult> ColdStart { get; init; }

    /// <summary>
    /// 高并发场景结果（按工作负载分组）
    /// </summary>
    [JsonPropertyName("concurrency")]
    public required Dictionary<string, List<ConcurrencyPoint>> Concurrency { get; init; }

    /// <summary>
    /// 持久运行场景结果
    /// </summary>
    [JsonPropertyName("endurance")]
    public required EnduranceResult Endurance { get; init; }

    /// <summary>
    /// MapListInto 复用场景结果（未运行时为 "未运行"）
    /// </summary>
    [JsonPropertyName("reuse")]
    public required ReuseResult Reuse { get; init; }
}

/// <summary>
/// 负载压测结果序列化辅助：将结果对象写入指定目录
/// </summary>
internal static class LoadResultWriter
{
    /// <summary>
    /// 将汇总结果以 JSON 写入指定目录
    /// </summary>
    /// <param name="directory">输出目录（不存在则创建）</param>
    /// <param name="summary">汇总结果对象</param>
    public static void WriteSummary(string directory, LoadSummary summary)
    {
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(directory, "load_summary.json"), json);
    }

    /// <summary>
    /// 将持久运行采样序列以 CSV 写入指定目录（供图表/回归比对）
    /// </summary>
    /// <param name="directory">输出目录</param>
    /// <param name="samples">采样序列</param>
    public static void WriteEnduranceCsv(string directory, List<EnduranceSample> samples)
    {
        Directory.CreateDirectory(directory);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("timeSec,managedHeapMB,totalAllocMB,gen0,gen1,gen2,workingSetMB");
        foreach (var s in samples)
        {
            sb.Append($"{s.TimeSec:F1},{s.ManagedHeapMB:F2},{s.TotalAllocMB:F2},")
              .Append($"{s.Gen0},{s.Gen1},{s.Gen2},{s.WorkingSetMB:F2}")
              .AppendLine();
        }
        File.WriteAllText(Path.Combine(directory, "endurance.csv"), sb.ToString());
    }

    /// <summary>
    /// 将高并发场景结果以 CSV 写入指定文件
    /// </summary>
    /// <param name="directory">输出目录</param>
    /// <param name="points">各档位结果</param>
    /// <param name="fileName">输出文件名（如 concurrency_Flat_Map.csv）</param>
    public static void WriteConcurrencyCsv(string directory, IReadOnlyList<ConcurrencyPoint> points, string fileName)
    {
        Directory.CreateDirectory(directory);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("workload,threads,totalOps,tps,avgLatencyUs,scaleEfficiency");
        foreach (var p in points)
        {
            sb.Append($"{p.Workload},{p.Threads},{p.TotalOps},{p.Tps:F1},{p.AvgLatencyUs:F2},{p.ScaleEfficiency:F3}")
              .AppendLine();
        }
        File.WriteAllText(Path.Combine(directory, fileName), sb.ToString());
    }

    /// <summary>
    /// 将 MapListInto 复用场景结果以 CSV 写入指定文件
    /// </summary>
    /// <param name="directory">输出目录</param>
    /// <param name="points">分档结果</param>
    /// <param name="fileName">输出文件名（reuse.csv）</param>
    public static void WriteReuseCsv(string directory, IReadOnlyList<ReusePoint> points, string fileName)
    {
        Directory.CreateDirectory(directory);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("workload,threads,totalOps,tps,avgLatencyUs,scaleEfficiency");
        foreach (var p in points)
        {
            sb.Append($"{p.Workload},{p.Threads},{p.TotalOps},{p.Tps:F1},{p.AvgLatencyUs:F2},{p.ScaleEfficiency:F3}")
              .AppendLine();
        }
        File.WriteAllText(Path.Combine(directory, fileName), sb.ToString());
    }
}
