// : ORM 导航收集内核统一 —— NavigationTreeCollector
// 从 EfCoreNavigationCollector / FreeSqlNavigationCollector 提取（CollectCore/TryRecurseNestedDto 逐行同构）。
// 单源与多源（Compose）缓存均入内核；两库薄壳仅保留 Include 应用外壳与入口。
#pragma warning disable NCMapper001 // 内部实现使用 [Experimental] 的 Pipeline API
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using NCMapper.Attributes;
using NCMapper.Runtime;

namespace NCMapper.Internal
{
    /// <summary>
    /// 导航树收集器 —— 分析 TTarget 跨表依赖，产出导航树供降级路径自动 Include 使用
    /// <para>核心逻辑：扫描 TTarget 属性的 [MapProperty(typeof(X), "Field")]，当 X != TSource 时
    /// 在 TSource 中解析导航属性，递归处理嵌套 DTO，产出 NavigationTree</para>
    /// </summary>
    internal static class NavigationTreeCollector
    {
        // CacheRegistry 登记制自注册（补漏）：仅登记清理委托，不触发缓存构建（防自触发）
        static NavigationTreeCollector()
        {
            CacheRegistry.Register("navigation-tree-collector", CacheLayer.Full, ClearCache);
        }

        /// <summary>
        /// 嵌套深度上限（防深 DTO 图栈溢出）
        /// </summary>
        private const int NestingDepthUpperBound = 32;

        /// <summary>
        /// 导航树缓存条目（版本由键成分改条目内字段——先例 Agnostic 通道条目结构）
        /// </summary>
        private sealed class NavigationTreeEntry
        {
            public int Version;
            public NavigationTree Tree = NavigationTree.Empty;
        }

        // 单源缓存改双层 CWT 弱键（外层 Source 弱键 / 内层 Target 弱键）
        // ——消除 ConcurrentDictionary 强键对 collectible ALC 动态类型的钉驻；版本失效改条目内校验。
        private static readonly ConditionalWeakTable<Type, ConditionalWeakTable<Type, NavigationTreeEntry>> s_cache = new();

        /// <summary>
        /// 多源缓存键改全量源类型序列化——原 (T1,T2,T3?,Target)
        /// 只承载前 3 源：Length&gt;3 键碰撞（(A,B,C,D)/(A,B,C,E) 同键，后者命中前者缓存返回错树）、
        /// Length&lt;2 越界。AQN（含程序集限定）序列化消除跨程序集同 FQN 碰撞（P2-4 同族口径）。
        /// <para>改外层 Target 弱键 + 内层保留 string 强键（ConcurrentDictionary）
        /// SourcesKey 字符串不持 Type 引用，唯一持 Type 的键成分（Target）由外层弱键承载；
        /// AQN 内层键语义逐字保留（A2-04 不退化）；版本失效改条目内校验。
        /// <b>值侧残留披露（CR #2）</b>：弱键化仅覆盖键侧
        /// 条目值（NavigationTree）持源侧 PropertyInfo/Type 强引用，静态 Target + collectible
        /// 动态源场景下源类型仍被钉驻至清理/版本变更（非本批回归，原 CD 值侧同形态；键侧 ALC
        /// 可回收性为本批目标面，值侧残留登记 后续批次）。</para>
        /// </summary>
        private static readonly ConditionalWeakTable<Type, ConcurrentDictionary<string, NavigationTreeEntry>> s_multiSourceCache = new();

        /// <summary>
        /// 收集单源导航树（CWT 弱键 + 版本条目内校验）
        /// </summary>
        /// <param name="sourceType">源实体类型</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <returns>导航树（无导航时为空树）</returns>
        [RequiresUnreferencedCode("Collect scans type members and attributes via reflection. Some members may be trimmed.")]
        internal static NavigationTree Collect(Type sourceType, Type targetType)
        {
            var version = Mapper.Pipeline.Version;
            var inner = s_cache.GetValue(sourceType, static _ => new());
            if (inner.TryGetValue(targetType, out var cached) && cached.Version == version)
                return cached.Tree;

            var visited = new HashSet<(Type Source, Type Target)>();
            var roots = CollectCore(sourceType, targetType, depth: 0, visited);
            var tree = roots.Count == 0 ? NavigationTree.Empty : new NavigationTree(roots);
            inner.AddOrUpdate(targetType, new NavigationTreeEntry { Version = version, Tree = tree });
            return tree;
        }

        /// <summary>
        /// 收集多源导航树（Compose 2/3 表；：外层 Target 弱键 + 内层 string 强键 + 版本条目内校验）
        /// </summary>
        /// <param name="sourceTypes">源实体类型数组（Compose 多源）</param>
        /// <param name="targetType">目标 DTO 类型</param>
        /// <returns>导航树（无导航时为空树）</returns>
        [RequiresUnreferencedCode("Collect scans type members and attributes via reflection. Some members may be trimmed.")]
        internal static NavigationTree Collect(Type[] sourceTypes, Type targetType)
        {
            // 防御——多源收集至少需要 2 个源类型
            //（原 sourceTypes[1] 对 0/1 长度数组抛 IndexOutOfRangeException，无参数上下文）
            if (sourceTypes == null) throw new ArgumentNullException(nameof(sourceTypes));
            if (sourceTypes.Length < 2)
                throw new ArgumentException(
                    $"Multi-source navigation collection requires at least 2 source types (got {sourceTypes.Length}).", nameof(sourceTypes));

            // 键按全量源类型 AQN 序列化（数组不可作字典键——引用相等）
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < sourceTypes.Length; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(sourceTypes[i].AssemblyQualifiedName);
            }
            var sourcesKey = sb.ToString();

            var version = Mapper.Pipeline.Version;
            var inner = s_multiSourceCache.GetValue(targetType, static _ => new());
            if (inner.TryGetValue(sourcesKey, out var cached) && cached.Version == version)
                return cached.Tree;

            var roots = new List<NavigationTreeNode>();
            foreach (var srcType in sourceTypes)
            {
                var tree = Collect(srcType, targetType);
                if (!tree.IsEmpty)
                    roots.AddRange(tree.Roots);
            }

            var result = roots.Count == 0 ? NavigationTree.Empty : new NavigationTree(roots);
            inner[sourcesKey] = new NavigationTreeEntry { Version = version, Tree = result }; // CD 索引器覆盖写（等价原 TryAdd 幂等终态）
            return result;
        }

        /// <summary>
        /// 清除缓存 —— 供测试隔离调用（两库薄壳 ClearCache 委托）。
        /// 两缓存各 CWT Clear()（net8+；字段 readonly 无实例换新；既有登记名与层不变）
        /// </summary>
        internal static void ClearCache()
        {
            s_cache.Clear();
            s_multiSourceCache.Clear();
        }

        /// <summary>
        /// 同源点路径/集合索引路径的首段导航收集
        /// 索引段 "Posts[0]" 按属性名 "Posts" 解析；索引路径的集合导航须收集
        /// （降级内存映射 ElementAtOrDefault 需要集合数据）。
        /// 边界：元素内导航子段不收集——FreeSql IncludeByPropertyName 对集合内
        /// 点分路径（"Posts.Author"）抛 NRE（实测），自动 Include 仅支持首段。
        /// </summary>
        [RequiresUnreferencedCode("CollectDotPathSegments scans type members and attributes via reflection. Some members may be trimmed.")]
        private static void CollectDotPathSegments(string sourceFieldName, List<PropertyInfo> sourceProps,
            Dictionary<string, (NavigationTreeNode Node, List<NavigationTreeNode> Children)> navChildren)
        {
            var firstSeg = sourceFieldName.Split('.')[0];
            var bracket = firstSeg.IndexOf('[');
            var segName = bracket >= 0 ? firstSeg.Substring(0, bracket) : firstSeg;

            var navProp = sourceProps.FirstOrDefault(p =>
                string.Equals(p.Name, segName, StringComparison.OrdinalIgnoreCase));
            if (navProp == null || !navProp.CanRead) return;

            var propType = navProp.PropertyType;
            if (propType.IsValueType || propType == typeof(string)) return;

            var elemType = NavigationResolver.GetCollectionElementType(propType);
            var isCollection = elemType != null;

            // 索引路径：集合导航须收集；非索引路径维持原边界
            // （引用导航收集；集合元素导航由嵌套 DTO 路径处理，此处不收集）
            if (bracket >= 0 || !isCollection)
            {
                var navKey = navProp.Name;
                if (!navChildren.TryGetValue(navKey, out _))
                {
                    // 原构造第三实参内嵌 List<NavigationTreeNode>()
                    // 为死实例——消费点 只读 entry.Children 与 entry.Node.Name/IsCollection，
                    // 节点内嵌 Children 从未被读取；改用构造缺省（空数组），零行为。
                    navChildren[navKey] = (new NavigationTreeNode(navKey, isCollection), new List<NavigationTreeNode>());
                }
            }
        }

        [RequiresUnreferencedCode("CollectCore scans type members and attributes via reflection. Some members may be trimmed.")]
        private static List<NavigationTreeNode> CollectCore(Type sourceType, Type targetType,
            int depth, HashSet<(Type Source, Type Target)> visited)
        {
            var nodes = new List<NavigationTreeNode>();

            if (depth >= NestingDepthUpperBound) return nodes;

            var visitKey = (sourceType, targetType);
            if (!visited.Add(visitKey)) return nodes;

            var sourceProps = TypeHelper.GetPublicProperties(sourceType);
            var targetProps = TypeHelper.GetPublicProperties(targetType);

            var navChildren = new Dictionary<string, (NavigationTreeNode Node, List<NavigationTreeNode> Children)>();

            foreach (var targetProp in targetProps)
            {
                if (!targetProp.CanWrite) continue;

                // 属性级 NoAutoInclude（消费点四/D1）——注册表优先
                //（Fluent .NoAutoInclude(dest) 对级数组承载 + [MapNoAutoInclude] pm 级发射承载，OR 语义）
                // + 反射回退（[MapNoAutoInclude] 特性既有语义零变化）
                if (RuntimeSnapshotStore.IsPropertyNoAutoInclude(targetType, targetProp.Name)
                    // T5 锚门化：所在类型无锚时 [MapNoAutoInclude] 反射腿不消费
                    //（注册表腿 = Fluent .NoAutoInclude 承载，不受门限）
                    // 收紧：反射腿准入改**特性锚**——Fluent 对不消费属性级
                    // [MapNoAutoInclude]（注册表腿口径不变， 边界细则）
                    || (RuntimeAnchorGate.HasAttributeAnchor(targetType)
                        && targetProp.GetCustomAttribute<MapNoAutoIncludeAttribute>() != null)) continue;

                PropertyMatcher.TryGetMapPropertyInfo(targetProp, sourceType,
                    out var sourceFieldName, out var fieldSourceType, out var isFromJson);

                if (isFromJson) continue;

                if (fieldSourceType != sourceType)
                {
                    var navProp = NavigationResolver.ResolveNavigationProperty(
                        sourceProps, fieldSourceType, sourceType.Name);
                    if (navProp == null) continue;

                    var isCollection = NavigationResolver.GetCollectionElementType(navProp.PropertyType) != null;
                    var navKey = navProp.Name;

                    if (!navChildren.TryGetValue(navKey, out _))
                    {
                        var children = new List<NavigationTreeNode>();
                        var node = new NavigationTreeNode(navKey, isCollection, children);
                        navChildren[navKey] = (node, children);
                    }
                }
                else
                {
                    // T1 同源显式点路径（FromField 含 '.'）→ 导航收集。
                    // 降级路径（[Mask]/[Convert] 触发二步映射）第一步实体查询须加载该导航，
                    // 否则内存映射时中间节点 null → 结果 null，与投影 SQL 翻译语义（LEFT JOIN）分叉。
                    // 集合索引路径（FromField 含 '['）同规则收集
                    // 索引段 "Posts[0]" 按属性名 "Posts" 解析，集合导航须收集（降级内存映射
                    // ElementAtOrDefault 需要集合数据）；元素内导航子段不收集（FreeSql
                    // IncludeByPropertyName 集合内点分路径 NRE 实证——见 CollectDotPathSegments 注释）。
                    if (sourceFieldName.Contains('.') || sourceFieldName.Contains('['))
                    {
                        CollectDotPathSegments(sourceFieldName, sourceProps, navChildren);
                    }

                    TryRecurseNestedDto(targetProp, sourceType, sourceProps, depth, visited, navChildren);
                }
            }

            foreach (var (_, entry) in navChildren)
            {
                var finalNode = new NavigationTreeNode(entry.Node.Name, entry.Node.IsCollection, entry.Children.ToArray());
                nodes.Add(finalNode);
            }

            visited.Remove(visitKey);
            return nodes;
        }

        [RequiresUnreferencedCode("TryRecurseNestedDto scans type members and attributes via reflection. Some members may be trimmed.")]
        private static void TryRecurseNestedDto(PropertyInfo targetProp, Type sourceType,
            List<PropertyInfo> sourceProps, int depth, HashSet<(Type Source, Type Target)> visited,
            Dictionary<string, (NavigationTreeNode Node, List<NavigationTreeNode> Children)> navChildren)
        {
            var propType = targetProp.PropertyType;
            if (propType.IsValueType || propType == typeof(string)) return;

            var elementType = NavigationResolver.GetCollectionElementType(propType);
            if (elementType != null && (elementType.IsValueType || elementType == typeof(string)))
                return; // 集合元素为值类型/string，非导航集合

            var nestedType = elementType ?? propType;
            if (nestedType.IsValueType || nestedType == typeof(string)) return;

            // 注册表优先 + 反射回退双读（主源查询）
            if (!RuntimeSnapshotStore.TryGetMappingMeta(nestedType, out var view216)) return;

            var nestedSourceType = view216.PrimarySource!;

            var navProp = NavigationResolver.ResolveNavigationProperty(
                sourceProps, nestedSourceType, sourceType.Name);
            if (navProp == null) return;

            var navKey = navProp.Name;
            var navIsCollection = NavigationResolver.GetCollectionElementType(navProp.PropertyType) != null;

            var nestedNodes = CollectCore(nestedSourceType, nestedType, depth + 1, visited);

            if (!navChildren.TryGetValue(navKey, out var entry))
            {
                var children = new List<NavigationTreeNode>(nestedNodes);
                var node = new NavigationTreeNode(navKey, navIsCollection, children);
                navChildren[navKey] = (node, children);
            }
            else
            {
                foreach (var nested in nestedNodes)
                {
                    if (!entry.Children.Any(c => c.Name == nested.Name))
                        entry.Children.Add(nested);
                }
            }
        }
    }
}
