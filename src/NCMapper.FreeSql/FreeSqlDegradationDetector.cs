// NCMapper FreeSql 扩展包 —— 降级检测器（薄壳子类）
// 继承共享内核 DegradationDetectorBase，，注入 FreeSql 4 项特有检查
// + 嵌套集合降级；NeedsDegradationCompose 为 FreeSql 特有入口，保留本文件。
// 提取前行为由 T-A 双端一致性矩阵锁定。
#pragma warning disable NCMapper001 // 内部实现使用 [Experimental] 的 Pipeline API
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Internal;
using NCMapper.Runtime;

namespace NCMapper.FreeSql
{
    /// <summary>
    /// FreeSql 降级检测器 —— 公共骨架（内核）+ 4 项特有检查
    /// <para>特有检查：全局忽略属性 / 集合导航 / 嵌套深度（MaxNestingDepth=3）/ 嵌套跨表 [MapProperty]</para>
    /// </summary>
    internal sealed class FreeSqlDegradationDetector : DegradationDetectorBase
    {
        // CacheRegistry 登记制自注册（纳入 #22）：仅登记清理委托，防扩展库漏登记
        static FreeSqlDegradationDetector()
        {
            NCMapper.Internal.CacheRegistry.Register("free-sql-degradation-detector", NCMapper.Internal.CacheLayer.Full, ClearCache);
        }

        /// <summary>
        /// 嵌套投影深度上限（FreeSql 翻译能力有限，保守策略）
        /// </summary>
        protected override int MaxNestingDepth => 3;

        /// <summary>
        /// 单例门面（调用方使用静态 API）
        /// </summary>
        private static readonly FreeSqlDegradationDetector Instance = new();

        /// <summary>
        /// 判定源/目标类型是否需要降级为内存映射（FreeSql 门面，委托实例 Evaluate）
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
        /// Compose 降级判定（FreeSql 特有）—— 主源走公共判定 + 附加源 PostMap 检测
        /// </summary>
        /// <param name="primarySource">Compose 主源实体类型</param>
        /// <param name="additionalSources">Compose 附加源实体类型集合</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <param name="reason">降级原因（返回 true 时有效）</param>
        /// <returns>需要降级时为 true</returns>
        [RequiresUnreferencedCode("NeedsDegradationCompose scans type members and attributes via reflection. Some members may be trimmed.")]
        internal static bool NeedsDegradationCompose(Type primarySource, Type[] additionalSources, Type targetType, out string reason)
        {
            if (NeedsDegradation(primarySource, targetType, out reason))
                return true;

            if (Mapper.Pipeline.IsEnabled)
            {
                foreach (var src in additionalSources)
                {
                    if (Mapper.Pipeline.HasPostMapFor(src, targetType))
                    {
                        reason = $"已为 ({src.Name} → {targetType.Name}) 注册了 Pipeline.PostMap";
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// FreeSql 额外检查：全局忽略 / 集合导航 / 嵌套深度 / 嵌套跨表
        /// </summary>
        /// <param name="sourceType">源实体类型</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <param name="reason">降级原因（返回 true 时有效）</param>
        /// <returns>命中任一 FreeSql 降级条件时为 true</returns>
        [RequiresUnreferencedCode("RunExtraChecks scans type members and attributes via reflection. Some members may be trimmed.")]
        protected override bool RunExtraChecks(Type sourceType, Type targetType, out string reason)
        {
            if (HasGlobalIgnoredProperty(targetType, sourceType))
            {
                reason = $"目标类型 {targetType.Name} 存在命中全局忽略清单的属性（目标侧或源侧），投影无法排除忽略列，需降级为内存映射";
                return true;
            }

            if (HasCollectionNavigation(targetType, sourceType))
            {
                reason = $"目标类型 {targetType.Name} 含集合导航属性（FreeSql 不支持集合子查询投影，需降级为内存映射）";
                return true;
            }

            var maxDepth = GetMaxNestingDepth(targetType, sourceType);
            if (maxDepth > MaxNestingDepth)
            {
                reason = $"目标类型 {targetType.Name} 嵌套投影深度 {maxDepth} 超过 FreeSql 上限 {MaxNestingDepth}（需降级为内存映射）";
                return true;
            }

            if (HasNestedCrossTableNavigation(targetType, sourceType, visited: null))
            {
                reason = $"目标类型 {targetType.Name} 的嵌套 DTO 含跨表 [MapProperty]（FreeSql 无法翻译内层 null 守卫表达式，需降级为内存映射）";
                return true;
            }

            // 集合索引路径（A.B[0].C）— FreeSql 无法翻译集合导航索引访问
            // （Column not found）→ 降级为内存映射（运行时 ElementAtOrDefault 支持索引）
            if (HasCollectionIndexPath(sourceType, targetType, visited: null))
            {
                reason = $"目标类型 {targetType.Name} 含集合索引路径（特性通道 FromField 含 '[n]' 或 Fluent 索引段 lambda，FreeSql 无法翻译集合导航索引访问，需降级为内存映射）";
                return true;
            }

            reason = "";
            return false;
        }

        /// <summary>
        /// 检测目标 DTO（含嵌套）是否存在集合索引路径
        /// 顶层与嵌套统一判定；跨表 [MapProperty(FromSource=其他类型)] 由 HasNestedCrossTableNavigation 已有分支覆盖。
        /// <para>注册表优先：仅经 TryGetMapPropertyInfo 读目标属性 [MapProperty] 特性——
        /// Fluent 属性无特性 → 永不命中。改读「注册表优先」：FindByPair 命中时按属性明细
        /// <see cref="RuntimePropertyMapping.HasIndex"/> 判定（双通道承载， 明细段/ Fluent 索引段）；
        /// 注册表未命中（显式 off 态）→ 反射回退 TryGetMapPropertyInfo（特性通道现状口径）。</para>
        /// </summary>
        [RequiresUnreferencedCode("HasCollectionIndexPath scans type members and attributes via reflection. Some members may be trimmed.")]
        private static bool HasCollectionIndexPath(Type sourceType, Type targetType, HashSet<Type>? visited)
        {
            visited ??= new HashSet<Type>();
            if (!visited.Add(targetType)) return false;

            // 注册表优先：属性明细 HasIndex 覆盖双通道索引段承载
            var meta = RuntimeSnapshotStore.FindByPair(sourceType, targetType);
            if (meta != null)
            {
                foreach (var rpm in meta.Properties)
                {
                    if (rpm.HasIndex) return true;
                }
            }

            var targetProps = TypeHelper.GetPublicProperties(targetType);
            foreach (var prop in targetProps)
            {
                // 反射回退（注册表未命中——显式 off 态）：[MapProperty] FromField 含 '['
                if (meta == null)
                {
                    PropertyMatcher.TryGetMapPropertyInfo(prop, targetType,
                        out var srcFieldName, out _, out _);
                    if (srcFieldName.Contains('[')) return true;
                }

                var propType = prop.PropertyType;
                if (propType.IsValueType || propType == typeof(string)) continue;

                // 嵌套 DTO（[MapFrom] 标注或 Fluent 注册）递归——注册表命中时以 PrimarySource 为嵌套源
                if (RuntimeSnapshotStore.TryGetMappingMeta(propType, out var nestedView))
                {
                    if (HasCollectionIndexPath(nestedView.PrimarySource!, propType, visited)) return true;
                }

                // 集合元素为嵌套 DTO（FreeSql 集合导航已整体降级，此处防御性覆盖）
                Type? elementType = null;
                if (propType.IsGenericType)
                {
                    var genArgs = propType.GetGenericArguments();
                    if (genArgs.Length == 1 && !genArgs[0].IsValueType && genArgs[0] != typeof(string))
                        elementType = genArgs[0];
                }
                else if (propType.IsArray)
                {
                    var et = propType.GetElementType();
                    if (et != null && !et.IsValueType && et != typeof(string))
                        elementType = et;
                }
                if (elementType != null && RuntimeSnapshotStore.TryGetMappingMeta(elementType, out var elementView))
                {
                    if (HasCollectionIndexPath(elementView.PrimarySource!, elementType, visited)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// FreeSql 嵌套集合检查：集合元素即降级；嵌套 DTO 含集合导航亦降级
        /// </summary>
        /// <param name="nestedType">嵌套属性类型（元素或属性声明类型）</param>
        /// <param name="nestedSourceType">嵌套属性的源类型</param>
        /// <param name="isCollectionElement">当前嵌套属性是否为集合元素</param>
        /// <param name="reason">降级原因（返回 true 时有效）</param>
        /// <returns>需要降级时为 true</returns>
        [RequiresUnreferencedCode("RunNestedCollectionChecks scans type members and attributes via reflection. Some members may be trimmed.")]
        protected override bool RunNestedCollectionChecks(
            Type nestedType, Type nestedSourceType, bool isCollectionElement, out string reason)
        {
            if (isCollectionElement)
            {
                reason = $"嵌套 DTO {nestedType.Name} 含集合导航属性（FreeSql 不支持集合子查询投影，需降级为内存映射）";
                return true;
            }

            if (HasCollectionNavigation(nestedType, nestedSourceType))
            {
                reason = $"嵌套 DTO {nestedType.Name} 含集合导航属性（FreeSql 不支持集合子查询投影，需降级为内存映射）";
                return true;
            }

            reason = "";
            return false;
        }

        /// <summary>
        /// 检测目标 DTO 是否存在命中全局忽略清单的属性（触发冻结；映射对级豁免不降级）。
        /// <para>判定面双侧（AUD-6 归并 CR-2/F1）：目标侧（属性名/类型命中）与源侧
        /// （异名 FromField 源字段命中，按源类型解析后判定）任一命中即降级；
        /// 豁免（目标属性 OverrideIgnore=true）时两侧全局判定均跳过。</para>
        /// </summary>
        [RequiresUnreferencedCode("HasGlobalIgnoredProperty triggers global config freeze via reflection. Some members may be trimmed.")]
        private static bool HasGlobalIgnoredProperty(Type targetType, Type sourceType)
        {
            var targetProps = TypeHelper.GetPublicProperties(targetType);
            var sourceProps = TypeHelper.GetPublicProperties(sourceType);
            foreach (var prop in targetProps)
            {
                var globalIgnoreExempt = PropertyIgnoreChecker.HasOverrideIgnore(prop);
                // 目标侧判定（原逻辑）
                if (PropertyMatcher.ShouldIgnore(prop, isToDto: true, globalIgnoreExempt))
                    return true;
                // 源侧判定：异名 FromField 源字段命中忽略 → 降级
                PropertyMatcher.TryGetMapPropertyInfo(prop, sourceType,
                    out var srcFieldName, out var fieldSourceType, out _);
                if (fieldSourceType == sourceType)
                {
                    var srcProp = ProjectionNameResolver.Resolve(sourceProps, srcFieldName);
                    if (srcProp != null && PropertyMatcher.ShouldIgnore(srcProp, isToDto: true, globalIgnoreExempt))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检测目标 DTO 是否含集合导航属性（元素类型标注 [MapFrom]）
        /// </summary>
        [RequiresUnreferencedCode("HasCollectionNavigation scans type members and attributes via reflection. Some members may be trimmed.")]
        private static bool HasCollectionNavigation(Type targetType, Type sourceType)
        {
            var targetProps = TypeHelper.GetPublicProperties(targetType);
            foreach (var prop in targetProps)
            {
                var propType = prop.PropertyType;
                if (propType.IsValueType || propType == typeof(string)) continue;

                Type? elementType = null;
                if (propType.IsGenericType)
                {
                    var genArgs = propType.GetGenericArguments();
                    if (genArgs.Length == 1 && !genArgs[0].IsValueType && genArgs[0] != typeof(string))
                        elementType = genArgs[0];
                }
                else if (propType.IsArray)
                {
                    var et = propType.GetElementType();
                    if (et != null && !et.IsValueType && et != typeof(string))
                        elementType = et;
                }

                // 注册表优先 + 反射回退双读
                if (elementType != null && RuntimeSnapshotStore.TryGetMappingMeta(elementType, out _))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 计算目标 DTO 的最大嵌套投影深度（递归 [MapFrom] 标注的嵌套属性）
        /// </summary>
        [RequiresUnreferencedCode("GetMaxNestingDepth scans type members and attributes via reflection. Some members may be trimmed.")]
        private static int GetMaxNestingDepth(Type targetType, Type sourceType, int currentDepth = 0, HashSet<Type>? visited = null)
        {
            visited ??= new HashSet<Type>();

            if (currentDepth > 8) return currentDepth;

            if (!visited.Add(targetType)) return currentDepth;

            int max = currentDepth;
            var targetProps = TypeHelper.GetPublicProperties(targetType);
            foreach (var prop in targetProps)
            {
                var propType = prop.PropertyType;
                if (propType.IsValueType || propType == typeof(string)) continue;
                if (propType.IsGenericType || propType.IsArray) continue; // 集合已在 HasCollectionNavigation 处理

                // 注册表优先 + 反射回退双读（主源查询）
                if (!RuntimeSnapshotStore.TryGetMappingMeta(propType, out var view286)) continue;

                var nestedDepth = GetMaxNestingDepth(propType, view286.PrimarySource!, currentDepth + 1, visited);
                if (nestedDepth > max) max = nestedDepth;
            }

            visited.Remove(targetType);
            return max;
        }

        /// <summary>
        /// 检测嵌套 DTO 是否含跨表 [MapProperty]（指向非自身 [MapFrom] 源类型的字段）。
        /// 此模式会在 BuildNestedProjectionBindings 中生成内层 Conditional null 守卫，
        /// FreeSql 翻译器无法处理（"null.X cannot be null"），须降级为内存映射。
        /// </summary>
        [RequiresUnreferencedCode("HasNestedCrossTableNavigation scans type members and attributes via reflection. Some members may be trimmed.")]
        private static bool HasNestedCrossTableNavigation(Type targetType, Type sourceType, HashSet<Type>? visited)
        {
            visited ??= new HashSet<Type>();
            if (!visited.Add(targetType)) return false;

            var targetProps = TypeHelper.GetPublicProperties(targetType);
            foreach (var prop in targetProps)
            {
                var propType = prop.PropertyType;
                if (propType.IsValueType || propType == typeof(string)) continue;
                if (propType.IsGenericType || propType.IsArray) continue;

                // 注册表优先 + 反射回退双读（主源查询）
                if (!RuntimeSnapshotStore.TryGetMappingMeta(propType, out var view315)) continue;

                var nestedSource = view315.PrimarySource!;

                // 检查嵌套 DTO 的属性是否有 [MapProperty(typeof(X), ...)] 且 X != nestedSource
                // 决策点 4：明细优先——嵌套 DTO 已注册时
                // 先读 ComposeMappings 承载（SubSourceTypeRef / SubSourceTypeName 与主源比较判跨表；
                // 空组条目跳过），命中即判定（零特性读取）；明细无信号 → 反射兜底腿（下方特性扫描，
                // 锚门语义维持——特性对嵌套 DTO 的 [MapProperty] 读取保留）
                if (view315.IsRegistered && view315.Metadata != null)
                {
                    foreach (var cm in view315.Metadata.ComposeMappings)
                    {
                        if (cm.SourcePathNames.Length == 0) continue; // 空组=忽略信号
                        var subType = cm.SubSourceTypeRef;
                        if (subType != null)
                        {
                            if (subType != nestedSource) return true;
                        }
                        else
                        {
                            // SubSourceTypeRef 缺失（编译期非 public）→ 按 SubSourceTypeName 与主源全名
                            // 比较——不一致即跨表。口径：运行时归一比较（定稿——嵌套类型编译期
                            // '.' 分隔 vs 运行时 '+'，两侧归一后比较；此处只判"≠主源"无歧义面）
                            var nestedFullName = nestedSource.FullName;
                            var match = string.Equals(cm.SubSourceTypeName, nestedFullName, StringComparison.Ordinal)
                                || (!string.IsNullOrEmpty(nestedFullName)
                                    && string.Equals(cm.SubSourceTypeName?.Replace('+', '.'), nestedFullName.Replace('+', '.'), StringComparison.Ordinal));
                            if (!match)
                                return true;
                        }
                    }
                }

                var nestedProps = TypeHelper.GetPublicProperties(propType);
                foreach (var nestedProp in nestedProps)
                {
                    // T5 锚门化：嵌套类型无锚时 [MapProperty] 不消费
                    // 收紧：准入改**特性锚**——Fluent 对不因嵌套 [MapProperty] 走本判定
                    var mpAttr = RuntimeAnchorGate.HasAttributeAnchor(nestedProp.DeclaringType!)
                        ? nestedProp.GetCustomAttribute<MapPropertyAttribute>()
                        : null;
                    if (mpAttr == null) continue;

                    // MapProperty 的 FromSource 指向跨表类型 → 需要导航 → 生成 Conditional
                    var mpSourceType = mpAttr.FromSource;
                    if (mpSourceType != null && mpSourceType != nestedSource)
                        return true;
                }

                // 递归：嵌套 DTO 自身还有嵌套 DTO
                if (HasNestedCrossTableNavigation(propType, nestedSource, visited))
                    return true;
            }

            visited.Remove(targetType);
            return false;
        }
    }
}
