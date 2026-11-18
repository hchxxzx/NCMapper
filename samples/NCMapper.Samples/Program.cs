using System.Globalization;
using NCMapper.Samples;
using NCMapper.Samples.Helpers;

// ================================================================
// NCMapper Samples — Command-line driven demo
// 场景注册表驱动薄壳（域→场景二级结构）。
// 本文件只保留 CLI 解析 + 注册表查找 + 交互菜单；场景实现全部位于各域目录场景类。
// 场景号 = 三位数域编（101-606，见 ScenarioRegistry / samples README）；
// 退出码契约不变：0=成功、1=场景内验证失败（Error Summary）、2=非法场景号。
// ================================================================

// 展示语言固定英文：库的运行时错误消息按 CurrentUICulture 自动切换（zh=中文 / en=英文），
// demo 输出面向 wiki 双语引用，统一固定 en-US 使异常消息与英文输出文案一致。
CultureInfo.CurrentUICulture = new CultureInfo("en-US");

var scenarioArg = args.SkipWhile(a => a != "--scenario").Skip(1).FirstOrDefault();
var runAll = args.Contains("--all");

if (!string.IsNullOrEmpty(scenarioArg))
{
    if (!int.TryParse(scenarioArg, out var id))
    {
        OutputHelper.CliError(Console.Error, $"Invalid scenario: {scenarioArg}. Scenario ids are 3-digit domain codes (see README or interactive menu).");
        // 非法场景号 → 退出码 2（失败信号；供脚本/AI 判定，与 Error Summary 的 exit 1 区分）
        Environment.ExitCode = 2;
        return;
    }

    // 注册表查找未命中即非法（含区间内未注册号如 199/450），不做区间上下界比较
    var scenario = ScenarioRegistry.Find(id);
    if (scenario is null)
    {
        OutputHelper.CliError(Console.Error, $"Invalid scenario: {id}. Not a registered scenario id.");
        Environment.ExitCode = 2;
        return;
    }

    scenario.Run();
    OutputHelper.PrintErrorSummary();
    return;
}

if (runAll)
{
    RunAll();
    return;
}

// Interactive menu (no args)
OutputHelper.ClearErrors();

while (true)
{
    PrintMenu();
    Console.Write("\nEnter scenario id, 'all' to run everything (q to quit): ");
    var input = Console.ReadLine()?.Trim() ?? "";

    // EOF（管道/重定向输入耗尽时 ReadLine 返回 null → 空串）：视同退出，
    // 避免 continue 无限循环刷屏（此前管道输入会无限打印菜单直至磁盘写满）
    if (input.Length == 0 || input.Equals("q", StringComparison.OrdinalIgnoreCase))
        break;

    // all 命令：一次运行注册表内全部场景（与 --all 参数同一实现，逐场景错误隔离）
    if (input.Equals("all", StringComparison.OrdinalIgnoreCase))
    {
        RunAll();
        OutputHelper.ClearErrors();
        continue;
    }

    if (!int.TryParse(input, out var choice))
    {
        OutputHelper.CliError(Console.Out, "Invalid input. Please enter a registered scenario id (e.g. 101-107, 201, 301, 401, 501-502, 601-606).");
        continue;
    }

    var scenario = ScenarioRegistry.Find(choice);
    if (scenario is null)
    {
        // 注册表查找未命中即非法（含区间内未注册号），不做区间上下界比较
        OutputHelper.CliError(Console.Out, $"Invalid scenario id: {choice}. Not registered — see menu for valid ids.");
        continue;
    }

    scenario.Run();
    OutputHelper.PrintErrorSummary();
    OutputHelper.ClearErrors();
}

Console.WriteLine("Goodbye!");

// ================================================================
// Menu（域→场景二级结构，由注册表生成）
// ================================================================

static void PrintMenu()
{
    Console.WriteLine();
    Console.WriteLine("  NCMapper Samples — Demo Project v2.0");
    Console.WriteLine("  High-performance .NET object mapping library · Dual-engine (SourceGen + ExpressionTree)");
    Console.WriteLine();
    foreach (var domain in ScenarioRegistry.Domains)
    {
        Console.WriteLine($"  == {domain.Name} ==");
        foreach (var scenario in domain.Scenarios)
            Console.WriteLine($"  {scenario.Id}  {scenario.Name}");
        Console.WriteLine();
    }
}

// ================================================================
// Run all（注册表序：域→场景；逐场景错误隔离）
// ================================================================

static void RunAll()
{
    Console.WriteLine("\n  === Running all scenarios ===\n");

    var pass = 0;
    var fail = 0;
    var failed = new List<int>();

    foreach (var domain in ScenarioRegistry.Domains)
    {
        foreach (var scenario in domain.Scenarios)
        {
            Console.WriteLine($"\n  >>> [{domain.Name}] {scenario.Id} {scenario.Name} <<<");
            try
            {
                OutputHelper.ClearErrors();
                scenario.Run();
                if (OutputHelper.HasErrors)
                {
                    fail++;
                    failed.Add(scenario.Id);
                }
                else
                {
                    pass++;
                }
            }
            catch (Exception ex)
            {
                // 异常此前被静默吞掉，失败场景无任何可诊断输出；改为红色打印异常类型与消息
                OutputHelper.Error($"{scenario.Id} {scenario.Name}: scenario threw an exception ({ex.GetType().Name}: {ex.Message})");
                fail++;
                failed.Add(scenario.Id);
            }
        }
    }

    Console.WriteLine("\n  === All scenarios complete ===");
    Console.WriteLine();
    var prevColor = Console.ForegroundColor;
    Console.ForegroundColor = fail > 0 ? ConsoleColor.Red : ConsoleColor.Green;
    Console.WriteLine($"  PASS: {pass} / FAIL: {fail}" + (failed.Count > 0 ? $" (failed: {string.Join(", ", failed)})" : ""));
    Console.ForegroundColor = prevColor;

    // 全量运行任一场景失败 → 退出码 1（脚本/AI 以 $? 判定）
    if (fail > 0)
        Environment.ExitCode = 1;
}
