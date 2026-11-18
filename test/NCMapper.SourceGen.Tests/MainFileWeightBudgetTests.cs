using System;
using System.IO;
using System.Linq;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// TG-S 减重校核（W3_1，附录 B v1.4）
/// <para>主文件 CodeGenerator.Mapping.cs 行数三档目标（v1.4 实测口径——原 v1.2/v1.3 承诺 ≤2300
/// 经实测 2451/净减 57 证伪（F-5），用户批准下修至实测口径）：v1.4 期承诺 ≤2460；
/// （v1.5，用户批准任务书 P9 前置上调）MapSubclass 四族分派发射
/// 预估 +110~170 → 承诺线上调 ≤2600（H1 收官实测 2415 + 预估上限 2585 + 余量 15）。</para>
/// <para> 冻结基线 2508 行（wc -l 口径 = File.ReadLines 总行数，含空行/注释）。
/// 承诺线断言兼作回归门：防止后续改动无意识回升越过承诺线。
/// 挑战线为记录性测试（不设断言——多态分派落地后挑战线宣称条件暂缓，后续渲染端注释瘦身再议）。</para>
/// </summary>
public class MainFileWeightBudgetTests
{
    /// <summary> 冻结基线行数（改造前主文件 CodeGenerator.Mapping.cs，wc -l 口径）</summary>
    private const int T001BaselineLines = 2508;

    /// <summary>承诺线（v1.5——MapSubclass 四族分派上调口径，任务书 P9 批准；断言目标）</summary>
    private const int CommitLine = 2600;

    /// <summary>挑战线（v1.4——原 v1.2/v1.3 承诺线 ≤2300 降级而来；记录目标，多态期暂缓）</summary>
    private const int ChallengeLine = 2300;

    /// <summary>远期线（v1.4 维持 ≤2000；移登记；本轮不断言）</summary>
    private const int RemoteLine = 2000;

    private readonly ITestOutputHelper _output;

    public MainFileWeightBudgetTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// 定位主文件（从测试输出目录向上找仓库根 NCMapper.slnx，再拼 src 相对路径——
    /// 与 GeneratedSnapshotBaselineTests.FindSourceBaselinePath 同模式）
    /// </summary>
    /// <returns>主文件绝对路径</returns>
    private static string FindMainFilePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            // 以 NCMapper.slnx 作为仓库根标记（不依赖 .git，纯拷贝代码树时同样可定位）
            if (File.Exists(Path.Combine(dir.FullName, "NCMapper.slnx")))
            {
                var path = Path.Combine(dir.FullName, "src", "NCMapper.SourceGen", "CodeGenerator.Mapping.cs");
                if (!File.Exists(path))
                    throw new InvalidOperationException($"主文件缺失: {path}");
                return path;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("无法定位仓库根（未找到 NCMapper.slnx）——TG-S 须在仓库内运行");
    }

    /// <summary>
    /// 统计文件总行数（wc -l 口径 = File.ReadLines 总行数，含空行/注释，与基线冻结口径一致）
    /// </summary>
    private static int CountLines(string path) => File.ReadLines(path).Count();

    /// <summary>
    /// TG-S 承诺线必达断言：主文件行数 ≤2600（v1.5——MapSubclass 四族分派上调口径）
    /// <para>S1：行数统计确定性（同文件同行数）；失败信息含基线/净减/承诺线，供回归定位。</para>
    /// </summary>
    [Fact]
    public void MainFileWeight_CommitLine_NotExceeded()
    {
        var lines = CountLines(FindMainFilePath());
        Assert.True(lines <= CommitLine,
            $"主文件 {lines} 行 > 承诺线 {CommitLine}（基线 {T001BaselineLines}，当前净减 {T001BaselineLines - lines}）。"
            + "W3_1 主文件减重承诺线回归（v1.5 = v1.4 口径 + MapSubclass 四族分派预估上调，任务书 P9 批准）。");
    }

    /// <summary>
    /// TG-S 挑战线达标判定记录：三档实测数据与缺口输出（记录性测试——多态分派期挑战线宣称暂缓，
    /// 后续渲染端注释瘦身后达标再转断言，v1.4 + 010 v1.5）
    /// </summary>
    [Fact]
    public void MainFileWeight_ThreeTier_Record()
    {
        var lines = CountLines(FindMainFilePath());
        var netReduction = T001BaselineLines - lines;

        _output.WriteLine($"TG-S 三档记录（v1.5 = v1.4 口径 + MapSubclass 上调，方案书附录 B +）：基线 {T001BaselineLines} → 实测 {lines}（净减 {netReduction}）");
        _output.WriteLine($"承诺线 {CommitLine}（MapSubclass 分派预估上调口径）：{(lines <= CommitLine ? "达成" : "未达")}——由 MainFileWeight_CommitLine_NotExceeded 断言");
        _output.WriteLine($"挑战线 {ChallengeLine}（多态期暂缓）：{(lines <= ChallengeLine ? "达成" : $"未达（缺口 {lines - ChallengeLine} 行——后续渲染端注释瘦身后宣称）")}");
        _output.WriteLine($"远期线 {RemoteLine}（净减 ≥508）：登记（移，本轮不断言）");

        // 记录性测试不设挑战/远期断言（宣称条件未满足）；仅校验读数有效（防御：定位/读取异常时非零）
        Assert.True(lines > 0, "主文件行数读数异常（0 行）——文件定位或读取失败");
    }
}
