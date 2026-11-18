using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// B3 递归计数器异常安全测试
/// <para>覆盖报告-20260818-010 B3 验收点：①连续 64 次异常后正常映射不再误抛
/// （累积性故障消除）；②异常与正常交替 1000 次深度检测仍准确（差值不漂移）；
/// ③真实循环引用（A→B→A）仍抛 MapException（保护未被削弱）。</para>
/// <para>修复机制：<see cref="MapperBase{TSource,TTarget}"/> 三入口将 ExitRecursion 移入
/// 已有 finally，并用 bool entered 守护 EnterRecursion 自身超限抛异常的场景；
/// 异常时差值不再永久 +1（MapperBase.cs:173-175/205-207 修复）。</para>
/// </summary>
public class RecursionGuardExceptionSafetyTests
{
    /// <summary>
    /// 异常制造源类型 — Bad 为 string（与目标 int 类型不兼容，映射时抛转换异常）
    /// </summary>
    private class RgBadSrc
    {
        /// <summary>
        /// 类型不匹配字段（与目标 int 不兼容）
        /// </summary>
        public string Bad { get; set; } = "";
    }

    /// <summary>
    /// 异常制造目标类型 — Bad 为 int（与源 string 类型不兼容）
    /// </summary>
    private class RgBadTgt
    {
        /// <summary>
        /// 目标 int 字段
        /// </summary>
        public int Bad { get; set; }
    }

    /// <summary>
    /// 正常映射源类型 — 浅映射，单次调用真实深度为 1
    /// </summary>
    private class RgNormalSrc
    {
        /// <summary>
        /// 标识字段
        /// </summary>
        public int Id { get; set; }
    }

    /// <summary>
    /// 正常映射目标类型
    /// </summary>
    private class RgNormalTgt
    {
        /// <summary>
        /// 标识字段
        /// </summary>
        public int Id { get; set; }
    }

    /// <summary>
    /// 循环引用源类型 A — Self 自引用（同类型对循环，走 Map 入口递归触发深度守卫）
    /// <para>验收点③采用同类型自引用（与既有 KnownIssueTests.CircularReference 契约一致）：
    /// 跨类型对双向循环（A→B→A）在运行时被 CycleCache 优雅打破不抛异常（实测），
    /// 自引用形态嵌套映射经 Mapper.Map 入口递归、EnterRecursion 差值累积触发守卫抛 MapException。
    /// <b>public 可见性必须与既有 SelfRefNode 模型一致</b>（private 嵌套类型实测不触发守卫）。</para>
    /// </summary>
    public class RgCycleSrcA
    {
        /// <summary>
        /// 自引用（成环）
        /// </summary>
        public RgCycleSrcA? Self { get; set; }
    }

    /// <summary>
    /// 循环引用目标类型 A
    /// </summary>
    public class RgCycleTgtA
    {
        /// <summary>
        /// 自引用（成环）
        /// </summary>
        public RgCycleTgtA? Self { get; set; }
    }

    /// <summary>
    /// 验收点①：连续 64 次异常后，正常映射不再误抛"递归深度超限"
    /// <para>报告 B3 复现：异常调用 64 次（= MaxRecursionDepth 默认值）后，
    /// 第 65 次完全正常的映射也应成功。修复前差值累积导致误抛 MapException。</para>
    /// </summary>
    [Fact]
    public void RecursionGuard_After64Exceptions_NormalMapSucceeds()
    {
        // 连续 64 次异常调用（MaxRecursionDepth 默认 64）
        for (int i = 0; i < 64; i++)
        {
            var ex = Record.Exception(() =>
                Mapper.Map<RgBadSrc, RgBadTgt>(new RgBadSrc { Bad = "not-an-int" }));
            Assert.NotNull(ex);
        }

        // 第 65 次：正常映射必须成功（修复前误抛 MapException）
        var result = Mapper.Map<RgNormalSrc, RgNormalTgt>(new RgNormalSrc { Id = 1 });
        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }

    /// <summary>
    /// 验收点②：异常与正常交替调用 1000 次，深度检测仍准确（差值不漂移）
    /// <para>每次交替中正常映射都必须成功；若差值漂移，1000 次内必然触发守卫误抛。</para>
    /// </summary>
    [Fact]
    public void RecursionGuard_AlternateExceptionAndNormal_1000Times_Accurate()
    {
        for (int i = 0; i < 1000; i++)
        {
            // 异常调用（类型不兼容 → 抛转换异常）
            var ex = Record.Exception(() =>
                Mapper.Map<RgBadSrc, RgBadTgt>(new RgBadSrc { Bad = "x" }));
            Assert.NotNull(ex);

            // 正常调用（同一类型对独立计数；必须成功）
            var result = Mapper.Map<RgNormalSrc, RgNormalTgt>(new RgNormalSrc { Id = i });
            Assert.NotNull(result);
            Assert.Equal(i, result!.Id);
        }
    }

    /// <summary>
    /// 验收点③：真实循环引用（同类型自引用）仍抛 MapException（保护未被削弱）
    /// <para>修复只消除"异常残留"，不得削弱真实递归超限检测。采用同类型自引用形态
    /// （跨类型对 A→B→A 在运行时被 CycleCache 优雅打破不抛——实测行为，报告验收点③
    /// 的"A→B→A"表述与实际机制有出入，记差异）。</para>
    /// </summary>
    [Fact]
    public void RecursionGuard_RealCycle_StillThrows()
    {
        var a = new RgCycleSrcA();
        a.Self = a;

        // 自引用无限递归，超过 MaxRecursionDepth 应抛 MapException（而非 StackOverflowException）
        var ex = Assert.Throws<MapException>(() => Mapper.Map<RgCycleSrcA, RgCycleTgtA>(a));
        Assert.Contains(nameof(RgCycleSrcA), ex.Message);
    }
}
