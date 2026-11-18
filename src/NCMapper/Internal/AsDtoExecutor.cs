// ⑥: 终端方法骨架收敛器（AsDtoExecutor， 选项甲）
// 三库 Selector 终端方法的"降级判定 → Trace → 双路径"骨架收敛为单点；
// Include 应用与查询执行仍由调用方（各库外壳）提供。
using System;

namespace NCMapper.Internal
{
    /// <summary>
    /// AsDto 终端方法骨架执行器 —— 收敛降级判定 + Trace + 投影/降级双路径
    /// <para>低风险、不改公共签名；各库终端方法委托本执行器，骨架不再内联重复</para>
    /// </summary>
    internal static class AsDtoExecutor
    {
        /// <summary>
        /// 执行终端方法骨架：降级走 degrade 路径（含 Trace），否则走 project 路径
        /// </summary>
        /// <param name="needsDegrade">降级判定结果</param>
        /// <param name="reason">降级原因（needsDegrade=true 时有效）</param>
        /// <param name="tracePrefix">Trace 前缀（含库名与终端方法名，如 "[EFCore AsDto] ToList"）</param>
        /// <param name="project">投影路径委托（SQL 侧投影）</param>
        /// <param name="degrade">降级路径委托（查实体 + 内存映射）</param>
        public static TResult Run<TSource, TTarget, TResult>(
            bool needsDegrade, string reason, string tracePrefix,
            Func<TResult> project, Func<TResult> degrade)
            where TSource : class
            where TTarget : class
        {
            if (needsDegrade)
            {
                MapTracer.Write($"{tracePrefix} 降级: {typeof(TSource).Name} → {typeof(TTarget).Name}, 原因: {reason}");
                return degrade();
            }
            return project();
        }
    }
}
