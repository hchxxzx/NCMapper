// ============================================================================
// RedTestRunner — 崩溃类红测的子进程启动/协议解析辅助
//
// 职责：
//   1. 以独立进程启动 NCMapper.CrashProbe（Exe 输出，复用 FuzzTests 的 Exe 形态）；
//   2. 解析 CrashProbe 协议输出文件中的 Key=Value 行（classify / copyCheck）；
//   3. 返回退出码 + 协议字典；崩溃码经 NormalizedExitCode（unchecked((uint)) 归一）供诊断。
//
// 关键约束与实测经验（执行期探明，经验沉淀候选）：
//   ① 子进程崩溃（StackOverflowException → 0xC00000FD）不影响父进程 —— 父进程照常拿到
//     ExitCode（Windows 有符号形态 -1073741571），红测断言层自然落红。
//   ② 「Process 管道重定向（RedirectStandardOutput/Error）+ 栈溢出崩溃」组合会使
//     崩溃子进程挂起不退出（CLR 崩溃转储写管道死锁，实测 3 分钟以上不退出）——
//     故本 runner 不做管道重定向：CreateNoWindow=true 让子进程获得隐藏控制台
//     （崩溃转储写隐藏控制台被丢弃，零污染），协议经输出文件传递（CrashProbe 侧
//     AutoFlush 逐行落盘）。
//   ③ MTP 测试宿主可能注入 DOTNET_DiagnosticPorts 等诊断环境变量，子进程继承后
//     启动期挂起等待诊断 IPC —— 显式剥离。
// 本辅助类不改动 NCMapper.Tests.csproj（CrashProbe 经输出目录探测定位，而非
// ProjectReference，「Tests csproj 可不改」分支）。
// ============================================================================
using System.Diagnostics;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 红测子进程结果：退出码（原样 int）+ 协议字典（Key=Value）+ 原始协议文本。
/// </summary>
internal sealed record RedSubProcessResult(int ExitCode, Dictionary<string, string> StdOut, string RawStdOut)
{
    /// <summary>
    /// 归一退出码（uint 视图）—— 0xC00000FD（StackOverflowException 崩溃）在 Windows 下
    /// Process.ExitCode 为有符号 -1073741571，归一后可与 0xC00000FDu 直接比较（任务书 D4）。
    /// </summary>
    public uint NormalizedExitCode => unchecked((uint)ExitCode);
}

/// <summary>
/// 红测子进程 runner：启动 NCMapper.CrashProbe 并解析 stdout 协议。
/// </summary>
internal static class RedTestRunner
{
    /// <summary>
    /// 以独立进程运行指定 CrashProbe 用例。
    /// </summary>
    /// <param name="caseName">CrashProbe 用例名（selfref-list / selfref-dict）。</param>
    /// <returns>退出码 + 协议字典 + 原始协议文本。</returns>
    /// <exception cref="InvalidOperationException">CrashProbe 未构建（找不到可执行输出）时抛出。</exception>
    public static RedSubProcessResult RunInSubProcess(string caseName)
    {
        var exePath = LocateCrashProbeExecutable()
            ?? throw new InvalidOperationException(
                "未找到 NCMapper.CrashProbe 的构建输出。请先执行：dotnet build test/NCMapper.CrashProbe");

        // 协议输出文件（CrashProbe 侧 AutoFlush 逐行落盘；崩溃前已写行保留）
        var protocolFile = Path.Combine(Path.GetTempPath(), $"ncmapper-crashprobe-{Guid.NewGuid():N}.protocol");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"{caseName} \"{protocolFile}\"",
                // 不做管道重定向（见类头注释 ②）：CreateNoWindow 隐藏控制台承接崩溃转储
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // 剥离 MTP 宿主注入的诊断环境变量（见类头注释 ③）
            psi.Environment.Remove("DOTNET_DiagnosticPorts");
            psi.Environment.Remove("DOTNET_DefaultDiagnosticPortSuspend");
            psi.Environment.Remove("DOTNET_EnableCrashReport");

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start 返回 null：" + exePath);
            process.WaitForExit();

            var rawProtocol = File.Exists(protocolFile) ? File.ReadAllText(protocolFile) : string.Empty;
            return new RedSubProcessResult(process.ExitCode, ParseProtocol(rawProtocol), rawProtocol);
        }
        finally
        {
            try { if (File.Exists(protocolFile)) File.Delete(protocolFile); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 定位 CrashProbe 构建输出（apphost exe 优先，回退 dll —— 调用方以 dotnet exec 启动）。
    /// 探测路径：从测试输出目录向上找到 test/ 目录，再进入 NCMapper.CrashProbe/bin/**/ 下
    /// 递归查找（覆盖 Debug/Release 与多 TFM；CrashProbe 为 net8.0 单 TFM）。
    /// </summary>
    private static string? LocateCrashProbeExecutable()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.Name.Equals("test", StringComparison.OrdinalIgnoreCase))
            dir = dir.Parent;
        if (dir is null) return null;

        var binRoot = Path.Combine(dir.FullName, "NCMapper.CrashProbe", "bin");
        if (!Directory.Exists(binRoot)) return null;

        // 优先 apphost 可执行体（可直接 Process.Start）：
        // Windows 下为 NCMapper.CrashProbe.exe；Linux/macOS 下 SDK 生成的 apphost 为无扩展名
        // 的 NCMapper.CrashProbe（ELF/Mach-O 可执行，dotnet build OutputType=Exe 默认 UseAppHost=true）。
        // 修复前仅匹配 .exe，非 Windows 会回退到 dll，而 Process.Start 直接启动 dll 会抛
        // Win32Exception(Exec format error)——CI(ubuntu) 上红测即失败。
        if (OperatingSystem.IsWindows())
        {
            var exe = Directory.EnumerateFiles(binRoot, "NCMapper.CrashProbe.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (exe is not null) return exe;
        }
        else
        {
            var apphost = Directory.EnumerateFiles(binRoot, "NCMapper.CrashProbe", SearchOption.AllDirectories)
                .FirstOrDefault(f => !f.EndsWith(".dll", StringComparison.Ordinal)
                                  && !f.EndsWith(".pdb", StringComparison.Ordinal)
                                  && !f.EndsWith(".deps.json", StringComparison.Ordinal)
                                  && !f.EndsWith(".runtimeconfig.json", StringComparison.Ordinal));
            if (apphost is not null) return apphost;
        }

        // 兜底：dll（调用方需以 `dotnet exec` 启动；runtimeconfig 位于同一输出目录）
        return Directory.EnumerateFiles(binRoot, "NCMapper.CrashProbe.dll", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    /// <summary>
    /// 解析协议文本：每行 Key=Value，# 开头行与空行忽略（容错诊断输出）。
    /// </summary>
    private static Dictionary<string, string> ParseProtocol(string protocolText)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in protocolText.Split('\n'))
        {
            var line = rawLine.Trim('\r', '\n', ' ');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            dict[line[..eq]] = line[(eq + 1)..];
        }
        return dict;
    }
}
