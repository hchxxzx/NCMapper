// NCMapper EF Core 扩展包 —— 降级检测器（薄壳子类）
// 继承共享内核 DegradationDetectorBase，注入 EFCore 特有检查
// 提取前行为由 T-A 双端一致性矩阵锁定。
using System.Diagnostics.CodeAnalysis;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Internal;
using NCMapper.Runtime;

namespace NCMapper.EFCore
{
    /// <summary>
    /// EF Core 降级检测器 —— 公共骨架（内核）+ HasNonProjectableCollection 特有检查
    /// </summary>
    internal sealed class EfCoreDegradationDetector : DegradationDetectorBase
    {
        // CacheRegistry 登记制自注册（对齐 FreeSql 三件套口径）
        // Instance 单例的判定缓存（基类 s_cache）原不经 Mapper.ClearCache 清理 → 残留泄漏；
        // 仅登记清理委托，不触发缓存构建（防自触发）
        static EfCoreDegradationDetector()
        {
            CacheRegistry.Register("ef-core-degradation-detector", CacheLayer.Full, ClearCache);
        }

        /// <summary>
        /// 嵌套投影深度上限（EF Core SQL 翻译能力强）
        /// </summary>
        protected override int MaxNestingDepth => 8;

        /// <summary>
        /// 单例门面（调用方使用静态 API）
        /// </summary>
        private static readonly EfCoreDegradationDetector Instance = new();

        /// <summary>
        /// 判定源/目标类型是否需要降级为内存映射（EFCore 门面，委托实例 Evaluate）
        /// </summary>
        /// <param name="sourceType">源实体类型</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <param name="reason">降级原因（返回 true 时有效）</param>
        /// <returns>需要降级时为 true</returns>
        [RequiresUnreferencedCode("NeedsDegradation scans type members and attributes via reflection. Some members may be trimmed.")]
        internal static bool NeedsDegradation(Type sourceType, Type targetType, out string reason)
            => Instance.Evaluate(sourceType, targetType, out reason);

        /// <summary>
        /// 清空降级判定缓存
        /// </summary>
        internal static void ClearCache() => Instance.ClearCaches();

        /// <summary>
        /// EFCore 额外检查：目标集合类型不可投影（仅 List/接口集合可翻译为 SQL）
        /// </summary>
        /// <param name="sourceType">源实体类型</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <param name="reason">降级原因（返回 true 时有效）</param>
        /// <returns>需要降级时为 true</returns>
        [RequiresUnreferencedCode("RunExtraChecks scans type members and attributes via reflection. Some members may be trimmed.")]
        protected override bool RunExtraChecks(Type sourceType, Type targetType, out string reason)
            => HasNonProjectableCollection(targetType, out reason);

        /// <summary>
        /// EFCore 支持集合投影：嵌套集合无额外检查
        /// </summary>
        /// <param name="nestedType">嵌套属性类型（元素或属性声明类型）</param>
        /// <param name="nestedSourceType">嵌套属性的源类型</param>
        /// <param name="isCollectionElement">当前嵌套属性是否为集合元素</param>
        /// <param name="reason">降级原因（返回 true 时有效）</param>
        /// <returns>恒 false（EFCore 不降级嵌套集合）</returns>
        [RequiresUnreferencedCode("RunNestedCollectionChecks scans type members and attributes via reflection. Some members may be trimmed.")]
        protected override bool RunNestedCollectionChecks(
            Type nestedType, Type nestedSourceType, bool isCollectionElement, out string reason)
        {
            reason = "";
            return false;
        }

        [RequiresUnreferencedCode("HasNonProjectableCollection scans type members and attributes via reflection. Some members may be trimmed.")]
        private static bool HasNonProjectableCollection(Type targetType, out string reason)
        {
            reason = "";
            var targetProps = TypeHelper.GetPublicProperties(targetType);
            foreach (var prop in targetProps)
            {
                var tgtElementType = NavigationResolver.GetCollectionElementType(prop.PropertyType);
                if (tgtElementType == null) continue;
                // 注册表优先 + 反射回退双读（消除 Fluent DTO 降级漏判）
                if (!RuntimeSnapshotStore.TryGetMappingMeta(tgtElementType, out _)) continue;

                var isList = prop.PropertyType.IsGenericType &&
                    prop.PropertyType.GetGenericTypeDefinition() == typeof(List<>);
                var isEnumerableInterface = prop.PropertyType.IsGenericType &&
                    (prop.PropertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                     prop.PropertyType.GetGenericTypeDefinition() == typeof(IList<>) ||
                     prop.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>) ||
                     prop.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ||
                     prop.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>));

                if (!isList && !isEnumerableInterface)
                {
                    reason = $"属性 {prop.Name} 的集合类型 {prop.PropertyType.Name} 不可投影（仅 List<T>/IEnumerable<T>/IList<T>/ICollection<T>/IReadOnlyList<T>/IReadOnlyCollection<T> 可翻译为 SQL），需降级到内存映射";
                    return true;
                }
            }
            return false;
        }
    }
}
