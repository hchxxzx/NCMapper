using System.Runtime.InteropServices;
using NCMapper.LoadBench;

// ================================================================
// NCMapper.LoadBench — 高峰负载压测 harness
// 场景：
//   1. coldstart   冷启动（首次调用 vs 稳态，量化编译开销）
//   2. concurrency 高并发（多线程吞吐扩展性与锁竞争）
//   3. endurance   持久运行（长时混合负载内存/GC 采样，泄漏判定）
//   4. reuse       MapListInto 复用（零分配断言 + 16T 扩展 + 未注册对比）
// 用法：
//   --scenario all|coldstart|concurrency|endurance|reuse  默认 all
//   --threads "1,2,4,8,16"                          并发档位
//   --window 3000                                    并发单轮窗口(ms)
//   --rounds 3                                       并发每档轮数（取中位数）
//   --duration 60 --interval 5                       持久时长/采样间隔(s)
//   --out <dir>                                      结果输出目录
//   GC 模式由环境变量 DOTNET_GCDynamicAdaptationMode 决定（0=关 DATAS/1=开）
// ================================================================

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

// 参数解析辅助
static string GetArg(string[] argv, string name, string defaultValue)
{
    for (int i = 0; i < argv.Length - 1; i++)
    {
        if (argv[i] == name)
        {
            return argv[i + 1];
        }
    }
    foreach (var a in argv)
    {
        if (a.StartsWith(name + "=", StringComparison.Ordinal))
        {
            return a[(name.Length + 1)..];
        }
    }
    return defaultValue;
}

var scenario = GetArg(cliArgs, "--scenario", "all");
var outDir = GetArg(cliArgs, "--out", Path.Combine(Directory.GetCurrentDirectory(), "load-results"));
var threadCsv = GetArg(cliArgs, "--threads", "1,2,4,8,16");
var windowMs = int.Parse(GetArg(cliArgs, "--window", "3000"));
var rounds = int.Parse(GetArg(cliArgs, "--rounds", "3"));
var durationSec = int.Parse(GetArg(cliArgs, "--duration", "60"));
var intervalSec = int.Parse(GetArg(cliArgs, "--interval", "5"));

var threadLevels = threadCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(int.Parse)
    .ToArray();

// GC 模式记录（跨框架对比须可知 GC 策略）
var gcMode = Environment.GetEnvironmentVariable("DOTNET_GCDynamicAdaptationMode");
var gcModeDisplay = gcMode switch
{
    "0" => "DATAS 关闭（DOTNET_GCDynamicAdaptationMode=0）",
    "1" => "DATAS 开启（DOTNET_GCDynamicAdaptationMode=1）",
    _ => "运行时默认（net8 无 DATAS / net9+ 开 DATAS）",
};

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"NCMapper.LoadBench | TFM: {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"Scenario={scenario} Threads=[{string.Join(",", threadLevels)}] Window={windowMs}ms Rounds={rounds} Duration={durationSec}s Out={outDir}");
Console.WriteLine($"GC: {gcModeDisplay}");
Console.WriteLine();

var runCold = scenario is "all" or "coldstart";
var runConc = scenario is "all" or "concurrency";
var runEndu = scenario is "all" or "endurance";
var runReuse = scenario is "all" or "reuse";

// 冷启动必须在最前：其"首次调用"语义要求该类型对在进程内从未被映射过
List<ColdStartResult>? coldStart = null;
if (runCold)
{
    coldStart = ColdStartScenario.Run(ColdStartScenario.BuildCases());
}

Dictionary<string, List<ConcurrencyPoint>>? concurrency = null;
if (runConc)
{
    concurrency = ConcurrencyScenario.Run(ConcurrencyScenario.BuildWorkloads(), threadLevels, windowMs, rounds);
}

EnduranceResult? endurance = null;
if (runEndu)
{
    endurance = EnduranceScenario.Run(durationSec, intervalSec);
}

ReuseResult? reuse = null;
if (runReuse)
{
    reuse = MapListIntoReuseScenario.Run(threadLevels, windowMs, rounds);
}

var summary = new LoadSummary
{
    Environment = RuntimeInformation.FrameworkDescription + " | " + Environment.MachineName + " | " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    GcMode = gcMode ?? "default",
    ConcurrencyRounds = rounds,
    ColdStart = coldStart ?? [],
    Concurrency = concurrency ?? [],
    Endurance = endurance ?? new EnduranceResult
    {
        DurationSec = 0,
        SampleIntervalSec = 0,
        Samples = [],
        HeapTrendRatio = 0,
        Verdict = "未运行",
    },
    Reuse = reuse ?? new ReuseResult
    {
        Elements = 0,
        ReuseIters = 0,
        RegReuseAllocBytes = 0,
        RegReuseGen0 = 0,
        UnregReuseAllocBytes = 0,
        UnregReuseGen0 = 0,
        ZeroAllocPass = false,
        R6Pass = false,
        F2ScalePass = false,
        ListIntoReuseScaleEff16T = 0,
        Verdict = "未运行",
        Points = [],
    },
};

LoadResultWriter.WriteSummary(outDir, summary);
if (endurance is not null)
{
    LoadResultWriter.WriteEnduranceCsv(outDir, endurance.Samples);
}
if (concurrency is not null)
{
    foreach (var kv in concurrency)
    {
        LoadResultWriter.WriteConcurrencyCsv(outDir, kv.Value, $"concurrency_{kv.Key}.csv");
    }
}
if (reuse is not null)
{
    LoadResultWriter.WriteReuseCsv(outDir, reuse.Points, "reuse.csv");
}

Console.WriteLine($"\n结果已写入: {Path.GetFullPath(outDir)}");
