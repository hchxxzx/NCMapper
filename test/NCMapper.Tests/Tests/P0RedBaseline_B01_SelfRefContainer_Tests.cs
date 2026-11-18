// ============================================================================
// Red_B01 — 自引用容器深拷贝崩溃
//
// 缺陷（006 第 1 项）：DeepCopyTypeClassifier.CollectEdgeTargets/CollectContainerEdges
// （227-231/:235-239/:271-281）容器边内联递归无终止保护 —— 自引用容器类型深拷贝时
// StackOverflowException 进程崩溃（实测 0xC00000FD）。
//
// 契约特例（006 契约要点 2 / 评审 001_Qoder-S2）：为唯一子进程隔离例外 ——
// CLR 下 StackOverflowException 不可 catch，本用例经 RedTestRunner 以独立进程启动
// NCMapper.CrashProbe，断言「退出码 0 + stdout 协议双 Key」（classify=Cyclic / copyCheck=OK）。
//
// 修复后期望（断言口径，006 验收锚点 + 守卫）：
//   自引用列表/自引用字典容器：分类结果 == Cyclic（三色标记兜底环检测），深拷贝进程存活，
//   拷贝完整性 OK（元素数一致 + 环回边引用一致）。
//
// 当前基线（e59f1c9）落红形态（留档）：Classify 调用自身即栈溢出 —— 子进程以
// 0xC00000FD 崩溃（Windows 有符号 -1073741571），崩溃发生在协议输出之前，
// stdout 无任何 Key=Value 行 → 退出码断言与两个协议 Key 断言全部失败 = 预期落红。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class Red_B01_SelfRefContainer
{
    [Fact]
    public void 自引用列表容器_深拷贝_进程存活且分类Cyclic且拷贝完整()
    {
        // 修复后期望：exit 0 + classify=Cyclic + copyCheck=OK；
        // 当前形态（留档）：exit=0xC00000FD（栈溢出崩溃在协议输出前，无 stdout 协议）→ 落红
        var result = RedTestRunner.RunInSubProcess("selfref-list");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Cyclic", result.StdOut["classify"]);
        Assert.Equal("OK", result.StdOut["copyCheck"]);
    }

    [Fact]
    public void 自引用字典容器_深拷贝_进程存活且分类Cyclic且拷贝完整()
    {
        // 修复后期望：exit 0 + classify=Cyclic + copyCheck=OK；
        // 当前形态（留档）：exit=0xC00000FD（栈溢出崩溃在协议输出前，无 stdout 协议）→ 落红
        var result = RedTestRunner.RunInSubProcess("selfref-dict");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Cyclic", result.StdOut["classify"]);
        Assert.Equal("OK", result.StdOut["copyCheck"]);
    }
}
