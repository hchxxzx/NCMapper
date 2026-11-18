// DeepCopy GC Diagnostic Tool
// 用途: 定位 DeepCopy 各路径在不同 .NET 框架下的 GC 压力差异
// 调用: dotnet run -c Release -- gcdiag [iterations] [batchSize] [path]
//   path: all | runtime | generated | expression
//   默认: 1_000_000 次迭代, 每 100_000 次输出一次 GC 指标

using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.Versioning;
using NCMapper;

namespace NCMapper.Benchmarks;

/// <summary>
/// DeepCopy GC 诊断工具 — 仅面向 Windows 基准机运行（依赖 ProcessorAffinity 亲和性报告）。
/// </summary>
[SupportedOSPlatform("windows")]
public static class DeepCopyGcDiag
{
    public static void Run(string[] args)
    {
        long iterations = 1_000_000;
        int batchSize = 100_000;
        string path = "all";

        if (args.Length > 0 && long.TryParse(args[0], out var it))
            iterations = it;
        if (args.Length > 1 && int.TryParse(args[1], out var bs))
            batchSize = bs;
        if (args.Length > 2)
            path = args[2].ToLowerInvariant();

        Console.WriteLine("=== DeepCopy GC Diagnostic ===");
        Console.WriteLine($"  Framework:        {Environment.Version}");
        // W3D-7（F-8，2026-08-09）：GC 形态声明文字化——PERF-2 纪律固化（工具输出自带形态行，
        // 杜绝 ServerGC/Workstation 数字口径漂移；报告引用本行即可，无需二次推断）
        Console.WriteLine($"  GC Mode:          {(GCSettings.IsServerGC ? "ServerGC" : "Workstation")}");
        Console.WriteLine($"  ServerGC(bool):   {GCSettings.IsServerGC}");
        Console.WriteLine($"  Gen0/100K 口径:   Gen0 列（GC 次数/1000 次操作，见下表）");
        Console.WriteLine($"  LatencyMode:      {GCSettings.LatencyMode}");
        Console.WriteLine($"  ProcessorAffinity:{Process.GetCurrentProcess().ProcessorAffinity}");
        Console.WriteLine($"  Iterations:       {iterations:N0}");
        Console.WriteLine($"  BatchSize:        {batchSize:N0}");
        Console.WriteLine($"  Path:             {path}");
        Console.WriteLine();

        // 测试路径分发: 用 if-else 而非数组, 避免 lambda 在元组数组中类型推断失败 (CS0826)
        if (path == "all" || path == "runtime")
        {
            RunOne("Runtime (FastDeepCopyProvider)",
                () => DeepCopyProviderBase.Current = new FastDeepCopyProvider(),
                () => Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData),
                iterations, batchSize);
        }

        if (path == "all" || path == "generated")
        {
            RunOne("Generated (SourceGen)",
                () => DeepCopyProviderBase.Current = new FastDeepCopyProvider(),
                () => Mapper.DeepCopy(BenchmarkData.DeepCopyMapFromEntityData),
                iterations, batchSize);
        }

        if (path == "all" || path == "expression")
        {
            RunOne("Expression (ExpressionTree)",
                () => DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider(),
                () => Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData),
                iterations, batchSize);
        }

        if (path != "all" && path != "runtime" && path != "generated" && path != "expression")
        {
            Console.WriteLine($"ERROR: Unknown path '{path}'. Valid: all|runtime|generated|expression");
            return;
        }

        Console.WriteLine("=== Diagnostic complete ===");
    }

    private static void RunOne(string name, Action setup, Func<object?> action, long iterations, int batchSize)
    {
        Console.WriteLine($"--- {name} ---");

        // 切换到目标 Provider
        setup();

        // 局部预热: 稳定 JIT + 构建委托缓存
        for (int i = 0; i < 10_000; i++)
        {
            _ = action();
        }

        // 测量前做一次完整 GC, 让基线干净
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long bytesBefore = GC.GetTotalAllocatedBytes(precise: true);
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        int heap0Before = GC.CollectionCount(0); // placeholder for clarity

        // batch 边界
        long batchStartBytes = bytesBefore;
        int batchStartGen0 = gen0Before;
        int batchStartGen1 = gen1Before;
        int batchStartGen2 = gen2Before;
        long batchStartTicks = Stopwatch.GetTimestamp();

        // 表头
        Console.WriteLine($"  {"Iter",-14} {"ms",-10} {"ns/op",-12} {"Gen0",-8} {"Gen1",-8} {"Gen2",-8} {"KB",-12}");
        Console.WriteLine($"  {new string('-', 14)} {new string('-', 10)} {new string('-', 12)} {new string('-', 8)} {new string('-', 8)} {new string('-', 8)} {new string('-', 12)}");

        var totalSw = Stopwatch.StartNew();

        for (long i = 1; i <= iterations; i++)
        {
            _ = action();

            if (i % batchSize == 0)
            {
                long nowBytes = GC.GetTotalAllocatedBytes(precise: true);
                int nowGen0 = GC.CollectionCount(0);
                int nowGen1 = GC.CollectionCount(1);
                int nowGen2 = GC.CollectionCount(2);
                long nowTicks = Stopwatch.GetTimestamp();

                long batchBytes = nowBytes - batchStartBytes;
                int batchGen0 = nowGen0 - batchStartGen0;
                int batchGen1 = nowGen1 - batchStartGen1;
                int batchGen2 = nowGen2 - batchStartGen2;
                double batchMs = (nowTicks - batchStartTicks) * 1000.0 / Stopwatch.Frequency;
                double batchNsPerOp = batchMs * 1_000_000.0 / batchSize;

                Console.WriteLine($"  {i,14:N0} {batchMs,10:F2} {batchNsPerOp,12:F2} {batchGen0,8} {batchGen1,8} {batchGen2,8} {batchBytes / 1024.0,12:F2}");

                batchStartBytes = nowBytes;
                batchStartGen0 = nowGen0;
                batchStartGen1 = nowGen1;
                batchStartGen2 = nowGen2;
                batchStartTicks = nowTicks;
            }
        }

        totalSw.Stop();

        // 汇总
        long totalBytes = GC.GetTotalAllocatedBytes(precise: true) - bytesBefore;
        int totalGen0 = GC.CollectionCount(0) - gen0Before;
        int totalGen1 = GC.CollectionCount(1) - gen1Before;
        int totalGen2 = GC.CollectionCount(2) - gen2Before;
        double meanNs = totalSw.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;

        Console.WriteLine($"  {"TOTAL",-14} {totalSw.Elapsed.TotalMilliseconds,10:F2} {meanNs,12:F2} {totalGen0,8} {totalGen1,8} {totalGen2,8} {totalBytes / 1024.0,12:F2}");
        Console.WriteLine($"  Per-op alloc:  {totalBytes * 1.0 / iterations:F2} bytes/op");
        Console.WriteLine($"  Gen0/100K:     {totalGen0 * 100_000.0 / iterations:F2}");
        Console.WriteLine($"  Gen1/100K:     {totalGen1 * 100_000.0 / iterations:F2}");
        Console.WriteLine($"  Gen2/100K:     {totalGen2 * 100_000.0 / iterations:F2}");
        Console.WriteLine();
    }
}
