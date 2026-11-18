// NCMapper FreeSql 扩展包 —— 导航路径收集器（薄壳）
// 共享内核 NavigationTreeCollector + 入口
// 清理：原文件头"扩展自有，不依赖核心 NavigationTree"注释失实（核心库本无此类型），已移除。
// 提取前行为由 T-A 双端一致性矩阵锁定。
#pragma warning disable NCMapper001 // 内部实现使用 [Experimental] 的 Pipeline API
using System.Diagnostics.CodeAnalysis;
using NCMapper.Internal;

namespace NCMapper.FreeSql
{
    /// <summary>
    /// FreeSql 导航路径收集器（薄壳）—— 降级路径自动 Include 的导航树入口
    /// <para>Include 应用外壳（ApplyFreeSqlIncludes）位于 NCMapperFreeSqlExtensions.cs</para>
    /// </summary>
    internal static class FreeSqlNavigationCollector
    {
        // CacheRegistry 登记制自注册（补漏 4）：仅登记清理委托，不触发缓存构建（防自触发）
        static FreeSqlNavigationCollector()
        {
            CacheRegistry.Register("free-sql-navigation", CacheLayer.Full, ClearCache);
        }

        // 原 AutoIncludeEnabled 伪开关（static readonly = true，
        // 恒真守卫）已删除——自动 Include 常开；属性级关闭走 [MapNoAutoInclude]。
        // 真开关属行为面，移批出口（X4 同款），不得顺带实现。

        /// <summary>
        /// 收集单源导航树（委托内核 NavigationTreeCollector）
        /// </summary>
        /// <param name="sourceType">源实体类型</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <returns>导航树</returns>
        [RequiresUnreferencedCode("Collect scans type members and attributes via reflection. Some members may be trimmed.")]
        internal static NavigationTree Collect(Type sourceType, Type targetType)
            => NavigationTreeCollector.Collect(sourceType, targetType);

        /// <summary>
        /// 收集多源导航树（Compose 2/3 表）
        /// </summary>
        /// <param name="sourceTypes">源实体类型数组</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <returns>导航树</returns>
        [RequiresUnreferencedCode("Collect scans type members and attributes via reflection. Some members may be trimmed.")]
        internal static NavigationTree Collect(Type[] sourceTypes, Type targetType)
            => NavigationTreeCollector.Collect(sourceTypes, targetType);

        /// <summary>
        /// 清空导航树缓存
        /// </summary>
        internal static void ClearCache() => NavigationTreeCollector.ClearCache();
    }
}
