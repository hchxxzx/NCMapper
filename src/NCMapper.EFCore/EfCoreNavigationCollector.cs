// NCMapper EF Core 扩展包 —— 导航收集器（薄壳）
// 共享内核 NavigationTreeCollector + Include 应用外壳
// 提取前行为由 T-A 双端一致性矩阵锁定。
#pragma warning disable NCMapper001 // 内部实现使用 [Experimental] 的 Pipeline API
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using NCMapper.Internal;

namespace NCMapper.EFCore
{
    /// <summary>
    /// EF Core 导航路径收集器（薄壳）—— Include 应用外壳 + 内核收集入口
    /// </summary>
    internal static class EfCoreNavigationCollector
    {
        // 原 AutoIncludeEnabled 伪开关（private static readonly = true，
        // 恒真守卫）已删除——自动 Include 常开；属性级关闭走 [MapNoAutoInclude]。
        // 真开关属行为面，移批出口（X4 同款），不得顺带实现。

        /// <summary>
        /// 降级路径自动 Include —— 分析 TTarget 跨表依赖，对查询应用 Include 链
        /// <para>失败回退裸查询（仅 Trace 警告，不阻断）</para>
        /// </summary>
        [RequiresUnreferencedCode("ApplyIncludes scans type members and attributes via reflection to collect navigation paths. Some members may be trimmed.")]
        internal static IQueryable<TSource> ApplyIncludes<TSource, TTarget>(IQueryable<TSource> query)
            where TSource : class
            where TTarget : class
        {
            // 自动 Include 常开（原恒真守卫删除）；属性级关闭走 [MapNoAutoInclude]
            try
            {
                var tree = Collect(typeof(TSource), typeof(TTarget));
                if (tree.IsEmpty) return query;
                foreach (var path in tree.FlattenPaths())
                    query = query.Include(path);
                // AsSplitQuery 防多集合导航行数相乘
                // 全 src 无 AsSplitQuery，多集合 Include 生成单条 JOIN（笛卡尔爆炸）；单集合/无集合
                // 导航场景 AsSplitQuery 语义等价（EF Core 文档：仅多集合 Include 时才拆分额外查询）
                query = query.AsSplitQuery();
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[EFCore AsDto] 自动 Include: {tree.Summary()}（AsSplitQuery）");
                return query;
            }
            catch (Exception ex)
            {
                // 告警升级——异常类型结构化留痕（回退裸查询契约不变）
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[EFCore AsDto] 自动 Include 失败，回退裸查询: {ex.GetType().Name}: {ex.Message}");
                return query;
            }
        }

        /// <summary>
        /// 收集导航树（委托内核 NavigationTreeCollector）
        /// </summary>
        /// <param name="sourceType">源实体类型</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <returns>导航树</returns>
        [RequiresUnreferencedCode("Collect scans type members and attributes via reflection. Some members may be trimmed.")]
        internal static NavigationTree Collect(Type sourceType, Type targetType)
            => NavigationTreeCollector.Collect(sourceType, targetType);

        /// <summary>
        /// 清空导航树缓存
        /// </summary>
        internal static void ClearCache() => NavigationTreeCollector.ClearCache();
    }
}
