using System;

namespace NCMapper.Internal
{
    /// <summary>
    /// 降级判定委托（各库检测器接入差异点）
    /// </summary>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标 DTO 类型</param>
    /// <param name="reason">降级原因（返回 true 时有效）</param>
    /// <returns>true 表示需降级</returns>
    internal delegate bool NeedsDegradationEvaluator(Type sourceType, Type targetType, out string reason);

    /// <summary>
    /// DTO 选择器终端方法骨架— 判定+Trace 收敛单点
    /// <para>收敛各终端方法内联重复的"降级判定 → 原因 Trace"骨架（EFCore 5 处 / FreeSql 6 处）。
    /// 方案调整登记：拟以 internal abstract 基类承载，但 public Selector（EFCoreDtoSelector 等）
    /// 继承 internal 基类触发 CS0060（可访问性不一致），且基类 public 化会破坏 PublicApi 零变化验收——
    /// 故以内部静态骨架辅助 + 检测器委托接入（<see cref="NeedsDegradationEvaluator"/>）实现等效收敛。
    /// 投影/降级分支仍由各终端方法表达（查询调用差异大，lambda 化无收益）。</para>
    /// <para>Trace 文案契约：与逐处手写版完全一致（"{tracePrefix} {actionLabel}降级: ..."），零行为变化。</para>
    /// </summary>
    internal static class DtoSelectorSkeleton
    {
        /// <summary>
        /// 降级判定 + 原因 Trace（骨架第 1-2 步）— Trace 文案与逐处手写保持一致
        /// <para>H-06 静态方法收敛至此单点</para>
        /// </summary>
        /// <param name="tracePrefix">Trace 前缀（如 "[EFCore AsDto]" / "[FreeSql AsDto]"）</param>
        /// <param name="sourceType">源类型</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <param name="actionLabel">动作标签（如 "ToChunkDto "；空串时输出 "{tracePrefix} 降级: ..."）</param>
        /// <param name="evaluator">降级检测器接入（差异点）</param>
        /// <returns>true 表示需降级（调用方走降级路径）</returns>
        public static bool NeedsDegradationWithTrace(
            string tracePrefix, Type sourceType, Type targetType, string actionLabel,
            NeedsDegradationEvaluator evaluator)
        {
            if (!evaluator(sourceType, targetType, out var reason))
                return false;
            MapTracer.Write($"{tracePrefix} {actionLabel}降级: {sourceType.Name} → {targetType.Name}, 原因: {reason}");
            return true;
        }
    }
}
