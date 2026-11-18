using NCMapper.Attributes;
using NCMapper.Runtime;
using System.Collections.Concurrent;
using System.Reflection;

using NCMapper.Internal;
namespace NCMapper;

/// <summary>
/// 属性匹配器 — 从 MapperBase 泛型类提取的非泛型属性匹配逻辑
/// <para>消除 MapperBase&lt;A,B&gt; 和 MapperBase&lt;C,D&gt; 之间的反射调用，
/// 所有属性匹配逻辑通过此非泛型类共享</para>
/// <para>缓存从泛型类实例字段改为共享静态字段，提升缓存命中率</para>
/// </summary>
internal static class PropertyMatcher
{
    // CacheRegistry 登记制自注册：仅登记清理委托，不触发缓存构建（防自触发）
    static PropertyMatcher()
    {
        CacheRegistry.Register("property-matcher", CacheLayer.Full, ClearCache);
    }

    /// <summary>
    /// 属性字典缓存 — 共享缓存，所有类型对复用，提升命中率
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> s_propertiesCache = new();

    /// <summary>
    /// MapFromAttribute 缓存 — 避免每次属性匹配都反射调用 GetCustomAttribute
    /// </summary>
    private static readonly ConcurrentDictionary<Type, MapFromAttribute?> s_mapFromAttrCache = new();

    /// <summary>
    /// MapFromAttribute 类型缓存
    /// </summary>
    private static readonly Type s_mapFromAttribute = typeof(MapFromAttribute);

    // 原 s_autoMapFieldAttribute 死字段删除
    // typeof(MapPropertyAttribute) 声明后零消费（全库 grep 证实），无消费方。

    /// <summary>
    /// 根据指定的源、目标对象，获取所有可用于处理的属性列表
    /// </summary>
    /// <param name="sourceType">源对象类型</param>
    /// <param name="targetType">目标对象类型</param>
    /// <returns>Key=源属性列表，Value=目标属性列表</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("GetPropertyList scans type members and attributes via reflection. Some members may be trimmed. Use the source generator to ensure AOT compatibility.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("GetPropertyList uses reflection in the runtime fallback path. Not supported in AOT. Use the source generator to ensure AOT compatibility.")]
    public static KeyValuePair<List<PropertyInfo>, List<PropertyInfo>> GetPropertyList(Type sourceType, Type targetType)
    {
        // 注册表优先 + 反射回退双读（Fluent DTO 同命中，消除 isToDto 漏判）
        var isToDto = RuntimeSnapshotStore.TryGetMappingMeta(targetType, out _);

        List<PropertyInfo> targetTypes;
        List<PropertyInfo> sourceTypes;

        targetTypes = GetPropertiesDictIgnoreCase(targetType)?.Values?.Where(x => x.GetIndexParameters().Length == 0
                                                && (x.PropertyType.IsPublic || x.PropertyType.IsNestedPublic)
                                                && x.CanWrite
                                                && !ShouldIgnoreAttributeOnly(x, isToDto)).ToList() ?? new List<PropertyInfo>();
        sourceTypes = GetPropertiesDictIgnoreCase(sourceType)?.Values?.Where(x => x.GetIndexParameters().Length == 0
                                                && (x.PropertyType.IsPublic || x.PropertyType.IsNestedPublic)
                                                && x.CanRead
                                                && !ShouldIgnoreAttributeOnly(x, isToDto)).ToList() ?? new List<PropertyInfo>();

        // 注册表优先 + 反射回退双读（建议 2 反射缓存字段 MapFromAttribute?）
        // — view.MapFromAttribute：[MapFrom] 通道=发射物化实例；Fluent=null（与 GetMapFromAttribute 反射语义一致）
        RuntimeSnapshotStore.TryGetMappingMeta(sourceType, out var srcView);
        RuntimeSnapshotStore.TryGetMappingMeta(targetType, out var tgtView);
        var sourceAttr = srcView?.MapFromAttribute;
        var targetAttr = tgtView?.MapFromAttribute;
        // 反转空 if 分支——仅单边配置时才进入过滤逻辑（原空块掩盖分支结构真实意图）
        if ((sourceAttr == null) != (targetAttr == null))
        {
            var tempTypes = new List<PropertyInfo>();
            if (sourceAttr != null)
            {
                var fromSource = sourceAttr.FromSource;
                foreach (var item in sourceTypes)
                {
                    var propAttr = PropertyIgnoreChecker.GetMapPropertyAttribute(item); // 缓存族收敛（两处）
                    var propToSource = propAttr == null ? fromSource : propAttr.FromSource;
                    if (fromSource == targetType || propToSource == targetType)
                        tempTypes.Add(item);
                }
                sourceTypes = tempTypes;
            }
            else
            {
                var fromSource = targetAttr!.FromSource;
                foreach (var item in targetTypes)
                {
                    var propAttr = PropertyIgnoreChecker.GetMapPropertyAttribute(item); // 缓存族收敛（两处）
                    var propToSource = propAttr?.FromSource;
                    if (fromSource == sourceType)
                    {
                        // 跨表 [MapProperty]（FromSource != sourceType）保留在目标列表中，
                        // 由下游 BuildCrossTableAssignment / TryBuildCrossTableBinding 按类型匹配导航解析；
                        // 导航不存在时下游返回 null 优雅跳过
                        tempTypes.Add(item);
                    }
                    else
                    {
                        if (propToSource != null && propToSource == sourceType)
                            tempTypes.Add(item);
                    }
                }
                targetTypes = tempTypes;
            }
        }

        return new KeyValuePair<List<PropertyInfo>, List<PropertyInfo>>(sourceTypes, targetTypes);
    }

    /// <summary>
    /// 获取指定类型的大小写不敏感属性字典（带缓存）
    /// <para><b>AOT/Trimming</b>：此方法使用反射扫描属性，AOT 下可能遗漏被 trim 的属性</para>
    /// <para><b>容量策略（方案 b）</b>：动态生成类型（Reflection.Emit/可收集
    /// 程序集）不缓存走直算——消除无界键空间增长（B5000 动态类型对 → 本缓存 12400 条目）；
    /// 静态类型行为零变化（仅多一次 bool 检测）。</para>
    /// </summary>
    /// <param name="that">需要获取属性字典的类型</param>
    /// <returns>属性名→属性信息的字典（大小写不敏感）；that 为 null 时返回 null</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("GetPropertiesDictIgnoreCase uses reflection to scan type properties. Some properties may be trimmed in AOT/Trimmed scenarios.")]
    public static Dictionary<string, PropertyInfo>? GetPropertiesDictIgnoreCase([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] Type that)
    {
        if (that == null) return null;

        // 方案 b：动态类型直算不缓存（无界键空间治理，裁决）
        if (IsDynamicType(that)) return BuildPropertiesDict(that);

        // 观测扩展：命中/未命中计数（GetOrAddWithMetrics 收敛）；关闭时直走 GetOrAdd 热路径
        return GetOrAddWithMetrics(s_propertiesCache, that, BuildPropertiesDict, CacheMetrics.CacheKind.Properties);
    }

    /// <summary>
    /// 带指标观测的缓存读取— MetricsEnabled 时走 TryGetValue 判定 + 命中/未命中计数；
    /// 关闭时直走 GetOrAdd 热路径（零开销契约，W3E-B 基线对照验证）。
    /// </summary>
    private static TValue GetOrAddWithMetrics<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> cache, TKey key, Func<TKey, TValue> factory,
        CacheMetrics.CacheKind kind)
        where TKey : notnull
    {
        if (CacheMetrics.IsEnabled)
        {
            if (cache.TryGetValue(key, out var cachedHit))
            {
                CacheMetrics.RecordHit(kind);
                return cachedHit;
            }

            var built = factory(key);
            CacheMetrics.RecordMiss(kind);
            return cache.GetOrAdd(key, built);
        }

        return cache.GetOrAdd(key, factory);
    }

    /// <summary>
    /// 构建大小写不敏感属性字典（GetPropertiesDictIgnoreCase 的缓存工厂 / 动态类型直算共用）
    /// <para>接口类型：GetProperties() 不返回继承接口的属性，需合并接口属性；同名属性按
    /// 声明类型优先级保留（派生类声明优先）。</para>
    /// <para><b>Trimming 豁免说明</b>：本方法仅被已声明
    /// <see cref="System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"/> 的
    /// <see cref="GetPropertiesDictIgnoreCase"/> 调用（整条链路已声明"裁剪下不安全"）；且
    /// GetInterfaces() 返回元素的 DAM 注解（Interfaces）无法满足 lambda 内
    /// GetProperties() 的 PublicProperties 需求——DAM 注解不可声明于 lambda 参数，
    /// 故方法级豁免 IL2070 是唯一干净解法。</para>
    /// </summary>
    /// <param name="that">需要扫描属性的类型（非 null）</param>
    /// <returns>属性名→属性信息的字典（大小写不敏感）</returns>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Called only from RequiresUnreferencedCode-annotated GetPropertiesDictIgnoreCase; interface element DAM annotations cannot satisfy GetProperties() inside lambda, so method-level suppression is required.")]
    private static Dictionary<string, PropertyInfo> BuildPropertiesDict(Type that)
    {
        var allProps = that.IsInterface
            ? that.GetProperties().Concat(that.GetInterfaces().SelectMany(i => i.GetProperties()))
            : that.GetProperties();
        IEnumerable<PropertyInfo> enumerable = (from p in allProps
                                                group p by p.DeclaringType).Reverse().SelectMany((IGrouping<Type, PropertyInfo> p) => p);
        var list = enumerable.ToList();
        var dictionary = new Dictionary<string, PropertyInfo>(list.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (dictionary.TryGetValue(item.Name, out var value))
            {
                if (value.DeclaringType != item.DeclaringType
                    && item.DeclaringType != null && value.DeclaringType != null
                    && item.DeclaringType.IsSubclassOf(value.DeclaringType))
                {
                    dictionary[item.Name] = item;
                }
            }
            else if (item.GetGetMethod() != null)
            {
                dictionary.Add(item.Name, item);
            }
        }
        return dictionary;
    }

    /// <summary>
    /// 获取指定对象真实对应的映射特性（带缓存）
    /// <para><b>容量策略（方案 b）</b>：动态生成类型直算不缓存，
    /// 与 <see cref="GetPropertiesDictIgnoreCase"/> 同口径。</para>
    /// </summary>
    /// <param name="source">需要检查的类型</param>
    /// <returns>MapFromAttribute 特性；未标注时返回 null</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("GetMapFromAttribute reads custom attributes via reflection. Attributes may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static MapFromAttribute? GetMapFromAttribute(Type source)
    {
        // 方案 b：动态类型直算不缓存（无界键空间治理，裁决）
        if (IsDynamicType(source)) return source.GetCustomAttribute<MapFromAttribute>();

        // 观测扩展：命中/未命中计数（GetOrAddWithMetrics 收敛）；关闭时直走 GetOrAdd 热路径
        return GetOrAddWithMetrics(s_mapFromAttrCache, source, t => t.GetCustomAttribute<MapFromAttribute>(), CacheMetrics.CacheKind.MapFromAttr);
    }

    /// <summary>
    /// 动态类型检测（方案 b 容量策略）
    /// <para>判定标准：类型自身为 Reflection.Emit 动态程序集（<see cref="Assembly.IsDynamic"/>）
    /// 或可收集程序集（<c>Type.IsCollectible</c>），或其泛型实例化实参链中存在动态类型
    /// （覆盖泛型实例化爆炸场景——MakeGenericType 产物自身 IsDynamic=false，但实参可能为动态类型；
    /// 静态泛型如 List&lt;Customer&gt; 的实参非动态，不会误判）。</para>
    /// <para>检测成本：bool 属性读 + 泛型实参遍历（仅 IsGenericType 时），&lt;1ns 量级，
    /// 静态类型热路径仅增加一次属性读（W3E-B 基线对照验证，D7/D9 登记）。</para>
    /// </summary>
    /// <param name="type">待检测类型（非 null）</param>
    /// <returns>true 表示动态类型（含泛型实参链含动态类型）</returns>
    internal static bool IsDynamicType(Type type)
    {
        if (type.Assembly.IsDynamic || type.IsCollectible) return true;

        if (type.IsGenericType)
        {
            var args = type.GetGenericArguments();
            for (var i = 0; i < args.Length; i++)
            {
                if (IsDynamicType(args[i])) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 根据目标字段，获取与目标字段相对应的源字段对象
    /// <para>按名匹配经 <see cref="ResolveByName"/> 收拢（v1.1 E2）：默认配置路径与现状
    /// OrdinalIgnoreCase FirstOrDefault 逐字节一致；配置非默认（命名策略/前后缀剥离）时走
    /// 归一化字典查表（共享算法 NameCandidateResolver）。[MapProperty].FromField 比对恒字面匹配
    /// （v1.1 G3，不随命名策略）。</para>
    /// </summary>
    /// <param name="sourceType">源对象类型（归一化字典缓存键组成部分）</param>
    /// <param name="targetItem">目标字段</param>
    /// <param name="sourceTypes">源字段集合</param>
    /// <param name="sourceIsDefine">源对象是否有配置特性</param>
    /// <param name="targetIsDefine">目标对象是否有配置特性</param>
    /// <param name="attr">字段特性</param>
    /// <param name="split">锚门拆分结果（可选参数——循环调用方
    /// 上移复用；null = 现状行为分支内自算）</param>
    /// <returns>返回 PropertyInfo?。</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Non-default config path triggers global config freeze, which reflects over [assembly: MapDefaults] when no generated snapshot exists.")]
    public static PropertyInfo? GetSourceItemByTargetItem(Type sourceType, PropertyInfo targetItem,
        List<PropertyInfo> sourceTypes,
        bool sourceIsDefine,
        bool targetIsDefine,
        out MapPropertyAttribute? attr,
        (List<PropertyInfo> WithAttr, List<PropertyInfo> WithoutAttr)? split = null)
    {
        PropertyInfo? sourceItem = null;
        attr = null;

        if ((sourceIsDefine && targetIsDefine) || (!sourceIsDefine && !targetIsDefine))
        {
        //：属性级 [MapProperty] 标注消费
            // T5 锚门化：仅所在类型有锚（[MapFrom] ∨ 注册表命中）时消费；
            // 双 false（无锚）分支退化纯按名匹配（"无锚不消费"——置顶节三原则普适化）。
            // 仅当标注 FromSource 指向当前源类型且 FromField 匹配成功时消费并赋值 attr（避免
            // 跨表 FromSource ≠ sourceType 的标注污染下游 IsFromJson/跨表判断——CR-1）；
            // 否则回退按名匹配，行为与修改前一致。
            // 收紧：读特性准入改**特性锚**（Fluent 对不消费 [MapProperty]）
            var targetAttr = RuntimeAnchorGate.HasAttributeAnchor(targetItem.DeclaringType!)
                ? PropertyIgnoreChecker.GetMapPropertyAttribute(targetItem)
                : null;
            if (targetAttr != null && targetAttr.FromSource == sourceType)
            {
                var candidate = ResolveByName(sourceType, targetItem.DeclaringType!, sourceTypes, sourceIsDefine, targetIsDefine, targetAttr.FromField);
                if (candidate != null)
                {
                    attr = targetAttr;
                    return candidate;
                }
            }
            sourceItem = ResolveByName(sourceType, targetItem.DeclaringType!, sourceTypes, sourceIsDefine, targetIsDefine, targetItem.Name);
        }
        else
        {
            if (sourceIsDefine)
            {
            //：单边源分支同样消费目标属性上的 [MapProperty] 标注
                // （FromSource 指向当前源且 FromField 匹配成功时消费；跨表 FromSource ≠ sourceType 回退原有逻辑）。
                // T5 锚门化：所在类型无锚时不消费（同上）。
                // 收紧：读特性准入改**特性锚**（Fluent 对不消费单边腿 [MapProperty]）
                var srcTargetAttr = RuntimeAnchorGate.HasAttributeAnchor(targetItem.DeclaringType!)
                    ? PropertyIgnoreChecker.GetMapPropertyAttribute(targetItem)
                    : null;
                if (srcTargetAttr != null && srcTargetAttr.FromSource == sourceType)
                {
                    var srcCandidate = ResolveByName(sourceType, targetItem.DeclaringType!, sourceTypes, sourceIsDefine: true, targetIsDefine, srcTargetAttr.FromField);
                    if (srcCandidate != null)
                    {
                        attr = srcTargetAttr;
                        return srcCandidate;
                    }
                }
                // T5 两级查找恢复（S12i2 实证——反向调用图
                // sourceIsDefine 分支的 sourceTypes=DTO 属性 = 承载侧，非纯 S 侧形态）：
                // ① 无标注属性按名优先；② 失败后有标注属性按 FromField 匹配——逐属性锚门
                //（标注所在类无锚 → 视同无标注，S 侧退役在正向调用图自动实现）
                // split 可选参数——非 null 时直接复用调用方
                // 循环外上移结果（消除逐目标属性重算）；null = 现状行为（分支内自算）。
                var (withAttr, withoutAttr) = split ?? SplitAttrsByAnchor(sourceTypes);
                sourceItem = ResolveByName(sourceType, targetItem.DeclaringType!, withoutAttr, sourceIsDefine: true, targetIsDefine, targetItem.Name);
                if (sourceItem == null)
                {
                    foreach (var prop in withAttr)
                    {
                        var pa = PropertyIgnoreChecker.GetMapPropertyAttribute(prop);
                        if (pa != null && string.Equals(pa.FromField, targetItem.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            attr = pa;
                            sourceItem = prop;
                            break;
                        }
                    }
                }
            }
            else
            {
                // IsDefined+GetCustomAttribute 合并为一次缓存查询
                // （targetIsDefine = 目标有 [MapFrom] 锚——锚前提天然成立，T5 无锚不消费口径下仍合法）
                var targetName = targetItem.Name;
                attr = PropertyIgnoreChecker.GetMapPropertyAttribute(targetItem);
                if (attr != null)
                    targetName = attr.FromField ?? targetItem.Name;
                sourceItem = ResolveByName(sourceType, targetItem.DeclaringType!, sourceTypes, sourceIsDefine, targetIsDefine, targetName);

                // 反向匹配恢复（X2-B-1）：
                // 对侧（sourceTypes）属性带 [MapProperty(FromSource=目标类型)] 时消费——本块在
                // 反向调用图（DTO→Entity）中读取的是**承载侧（DTO 属性）**标注，
                // 非纯 S 侧形态；正向调用图（Entity→DTO）中 sourceTypes=Entity 属性 = S 侧，
                // 由逐属性锚门（标注所在类有锚才消费）实现退役——双向一体收口。
                // X2-B-3 互指守卫（IsMutualMapFromPair）不再恢复（025 生成侧已拦截，运行时不可达）。
                if (sourceItem == null)
                {
                    foreach (var prop in sourceTypes)
                    {
                        // 收紧：读特性准入改**特性锚**（Fluent 对不消费反向 [MapProperty]）
                        var reverseAttr = RuntimeAnchorGate.HasAttributeAnchor(prop.DeclaringType!)
                            ? PropertyIgnoreChecker.GetMapPropertyAttribute(prop)
                            : null;
                        if (reverseAttr != null
                            && reverseAttr.FromSource == targetItem.DeclaringType
                            && string.Equals(reverseAttr.FromField, targetItem.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            attr = reverseAttr;
                            sourceItem = prop;
                            break;
                        }
                    }
                }
            }
        }

        return sourceItem;
    }

    /// <summary>
    /// 归一化源字典缓存 — 键 = (源类型, 目标类型, 源是否 Define, 目标是否 Define)，值 = 归一化键 → 源属性序号。
    /// <para>四元组完全决定传入的 sourceTypes 子集：GetPropertyList 的属性级 [MapIgnore] 过滤由源类型与方向决定、
    /// withoutAttr 划分由源类型决定、反向分支（源带 [MapFrom]）的 tempTypes 过滤按具体 targetType 决定（:71-78），
    /// 故同键必然同子集，缓存正确（由三元组扩为四元组，修复 AUD-5 同键不同子集缺陷）；
    /// 配置冻结于首次使用之前，无需版本号（同理由）。</para>
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type SourceType, Type TargetType, bool SourceIsDefine, bool TargetIsDefine), Dictionary<string, int>> s_normalizedKeysCache = new();

    /// <summary>
    /// 收拢后的按名解析 — 三处 OrdinalIgnoreCase 按名匹配的唯一出口。
    /// <para>默认配置（空剥缀清单 ∧ NamingStrategy.Default）→ 现状 FirstOrDefault 逐字节一致（零变更承诺）；
    /// 配置非默认 → 构建/复用归一化源字典后一次查表（覆盖精确名/剥缀变体/策略归一化三键来源）。</para>
    /// </summary>
    /// <param name="sourceType">源对象类型</param>
    /// <param name="targetType">目标对象类型（归一化字典缓存键组成部分，四元组键修复同键不同子集）</param>
    /// <param name="sourceTypes">源字段集合（按名匹配候选子集）</param>
    /// <param name="sourceIsDefine">源对象是否有配置特性（缓存键组成部分）</param>
    /// <param name="targetIsDefine">目标对象是否有配置特性（缓存键组成部分）</param>
    /// <param name="targetName">目标属性名（或 [MapProperty].FromField 解析后的匹配名）</param>
    /// <returns>匹配的源属性；未命中返回 null</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Non-default config path triggers global config freeze, which reflects over [assembly: MapDefaults] when no generated snapshot exists.")]
    private static PropertyInfo? ResolveByName(Type sourceType, Type targetType, List<PropertyInfo> sourceTypes,
        bool sourceIsDefine, bool targetIsDefine, string targetName)
    {
        var config = GlobalMapConfig.GetEffective();
        if (config.Prefixes.Length == 0 && config.Postfixes.Length == 0
            && config.NamingStrategy == NamingStrategy.Default)
        {
            // 零变更默认路径：与现状逐字节一致
            return sourceTypes.FirstOrDefault(x => string.Equals(x.Name, targetName, StringComparison.OrdinalIgnoreCase));
        }

        // 方案 b：四元组键两端任一为动态类型 → 直算不缓存（无界键空间治理，裁决）
        Dictionary<string, int> dict;
        if (IsDynamicType(sourceType) || IsDynamicType(targetType))
        {
            dict = BuildNormalizedKeys(sourceType, targetType, sourceTypes, sourceIsDefine, targetIsDefine, config);
        }
        else
        {
            // 观测扩展：命中/未命中计数（GetOrAddWithMetrics 收敛）
            dict = GetOrAddWithMetrics(
                s_normalizedKeysCache,
                (sourceType, targetType, sourceIsDefine, targetIsDefine),
                _ => BuildNormalizedKeys(sourceType, targetType, sourceTypes, sourceIsDefine, targetIsDefine, config),
                CacheMetrics.CacheKind.NormalizedKeys);
        }

        if (NCMapper.Shared.NameCandidateResolver.TryResolveCandidate(dict, targetName, (int)config.NamingStrategy, out var index))
            return sourceTypes[index];
        return null;
    }

    /// <summary>
    /// 构建归一化源键字典（ResolveByName 的缓存工厂）
    /// </summary>
    /// <param name="sourceType">源对象类型</param>
    /// <param name="targetType">目标对象类型</param>
    /// <param name="sourceTypes">源属性列表</param>
    /// <param name="sourceIsDefine">源对象是否有配置特性</param>
    /// <param name="targetIsDefine">目标对象是否有配置特性</param>
    /// <param name="config">生效的全局映射配置</param>
    /// <returns>归一化键 → 源属性序号的字典</returns>
    private static Dictionary<string, int> BuildNormalizedKeys(Type sourceType, Type targetType, List<PropertyInfo> sourceTypes,
        bool sourceIsDefine, bool targetIsDefine, GlobalMapConfig config)
        => NCMapper.Shared.NameCandidateResolver.BuildNormalizedSourceKeys(
            sourceTypes.Select(p => p.Name).ToList(),
            config.Prefixes,
            config.Postfixes,
            (int)config.NamingStrategy,
            config.NamingStrategy == NamingStrategy.Default
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

    /// <summary>
    /// 判断属性是否应被忽略（属性级 [MapIgnore] + 全局忽略清单；映射对级豁免由
    /// <paramref name="globalIgnoreExempt"/> 门控）
    /// </summary>
    /// <param name="prop">属性信息</param>
    /// <param name="isToDto">是否为 ToDto 方向</param>
    /// <param name="globalIgnoreExempt">映射对级豁免（目标属性带 OverrideIgnore=true 显式特性时 true）</param>
    /// <returns>应忽略返回 true</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Global ignore check triggers config freeze, which reflects over [assembly: MapDefaults] when no generated snapshot exists.")]
    public static bool ShouldIgnore(PropertyInfo prop, bool isToDto, bool globalIgnoreExempt = false)
        => PropertyIgnoreChecker.ShouldIgnore(prop, isToDto, globalIgnoreExempt);

    /// <summary>
    /// 判断属性是否应被忽略（仅属性级 [MapIgnore]，属性枚举阶段过滤用）
    /// </summary>
    /// <param name="prop">属性信息</param>
    /// <param name="isToDto">是否为 ToDto 方向</param>
    /// <returns>应忽略返回 true</returns>
    public static bool ShouldIgnoreAttributeOnly(PropertyInfo prop, bool isToDto)
        => PropertyIgnoreChecker.ShouldIgnoreAttributeOnly(prop, isToDto);

    /// <summary>
    /// 尝试从目标属性解析 [MapProperty] 特性信息
    /// <para>统一 MapProjection / MapValidator / MapTracer 的 [MapProperty] 解析逻辑，
    /// 消除三处重复的 GetCustomAttribute + FromField/FromSource 读取代码</para>
    /// </summary>
    /// <param name="targetProp">目标属性</param>
    /// <param name="defaultSourceType">默认源类型（无 [MapProperty] 标注时回退使用）</param>
    /// <param name="sourceFieldName">解析出的源字段名（无标注时回退为属性名）</param>
    /// <param name="fieldSourceType">解析出的源类型（无标注时回退为默认源类型）</param>
    /// <param name="isFromJson">是否从 JSON 转换而来（无标注时为 false）</param>
    /// <returns>目标属性是否存在 [MapProperty] 标注</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("TryGetMapPropertyInfo reads custom attributes via reflection. Attributes may be trimmed. Use the source-generated path to make this AOT-safe.")]
    internal static bool TryGetMapPropertyInfo(PropertyInfo targetProp, Type defaultSourceType,
        out string sourceFieldName, out Type fieldSourceType, out bool isFromJson)
    {
        // T5 锚门化（单点门，覆盖全部调用方）：所在类型无锚时
        // [MapProperty] 一律不消费（"无锚不消费"——置顶节三原则普适化）
        // 收紧：读特性准入改**特性锚**（Fluent 对不消费 FromField 标注）
        var fieldAttr = RuntimeAnchorGate.HasAttributeAnchor(targetProp.DeclaringType!)
            ? PropertyIgnoreChecker.GetMapPropertyAttribute(targetProp)
            : null;
        if (fieldAttr != null)
        {
            sourceFieldName = fieldAttr.FromField;
            fieldSourceType = fieldAttr.FromSource;
            isFromJson = fieldAttr.IsFromJson;
            return true;
        }
        sourceFieldName = targetProp.Name;
        fieldSourceType = defaultSourceType;
        isFromJson = false;
        return false;
    }

    /// <summary>
    /// 按源属性列表拆分有/无 MapProperty 标注（T5 锚门口径）
    /// 所在类无锚的属性视同无标注（归入 withoutAttr），有锚类标注照常入 withAttr。
    /// 原 SplitSourceByAttr 的锚门化替身（X2-B-1/S12i2 复核连带恢复）。
    /// <para>可见性 private → internal（仅供库内循环调用方
    /// 上移复用，不进公共 API 面）；返回值语义与排序保持不变（with/without 各自按输入顺序追加）。</para>
    /// </summary>
    internal static (List<PropertyInfo> WithAttr, List<PropertyInfo> WithoutAttr) SplitAttrsByAnchor(
        List<PropertyInfo> sourceTypes)
    {
        var with = new List<PropertyInfo>();
        var without = new List<PropertyInfo>();
        foreach (var p in sourceTypes)
        {
            // + T5 锚门：无锚类不消费 [MapProperty]（"无锚不消费"口径）
            // 收紧：读特性准入改**特性锚**（Fluent 对不消费分区标注）
            var attr = RuntimeAnchorGate.HasAttributeAnchor(p.DeclaringType!)
                ? PropertyIgnoreChecker.GetMapPropertyAttribute(p)
                : null;
            if (attr != null) with.Add(p);
            else without.Add(p);
        }
        return (with, without);
    }

    /// <summary>
    /// 清除属性字典缓存 — 供 Mapper.ClearCache() 调用
    /// </summary>
    internal static void ClearCache()
    {
        s_propertiesCache.Clear();
        s_mapFromAttrCache.Clear();
        s_normalizedKeysCache.Clear();
        PropertyIgnoreChecker.ClearCache();
        s_whenPredicateCache.Clear();
    }

    /// <summary>MapWhen 谓词方法解析缓存— ref→MethodInfo；null=解析失败缓存</summary>
    private static readonly ConcurrentDictionary<string, System.Reflection.MethodInfo?> s_whenPredicateCache = new();

    /// <summary>
    /// MapWhen 守卫声明读取入口（B9， 义务②）
    /// 注册表优先 + 特性反射回退双读（:54/:72 GetPropertyList 同模式）：
    /// <para>①注册表（RuntimeSnapshotStore.FindByPair）：覆盖 Fluent A/B 双形态与特性侧（生成端
    /// 物化时双通道填充同一 ConditionMethodRef 字段，P-e）；ConditionShape 1=T1 字段值 / 2=T2 整源。</para>
    /// <para>②特性反射（注册表 miss 回退——有锚场景；无锚类 [MapWhen] 不消费，RuntimeAnchorGate 口径）：
    /// [MapWhen] 特性实参仅方法名——谓词方法在
    /// targetItem 声明类型上按名解析（编译期 NCMAP041 同契约），形态运行时判定
    /// （单参 == sourceType 判 T2 整源，否则 T1 字段值——提取期同规则）。</para>
    /// <para>运行时镜像锚点注释（#31.6）：生成端发射 = CodeGenerator.WhenGuard.cs
    /// EmitGuardedSinglePropertyAssignment（赋值层 if 守卫 + T1 局部变量承载）。</para>
    /// </summary>
    /// <param name="sourceType">源实体类型</param>
    /// <param name="targetType">目标 DTO 类型</param>
    /// <param name="targetItem">目标属性</param>
    /// <returns>守卫声明（MethodRef 优先，null 时按特性方法名解析）；null = 无守卫</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("TryGetWhenDeclaration reads the MapWhen attribute via reflection. Some members may be trimmed.")]
    internal static WhenDeclaration? TryGetWhenDeclaration(Type sourceType, Type targetType, PropertyInfo targetItem)
    {
        // ① 注册表优先：pm 级条件字段（Fluent A/B + 特性侧同承载）
        var rpm = RuntimeSnapshotStore.FindByPair(sourceType, targetType)?.Properties;
        if (rpm is { Length: > 0 })
        {
            foreach (var p in rpm)
            {
                if (p.ConditionMethodRef is { Length: > 0 } && string.Equals(p.Target.Name, targetItem.Name, System.StringComparison.Ordinal))
                    return new WhenDeclaration(p.ConditionMethodRef, p.ConditionShape == 2, null);
            }
        }

        // ② 特性反射回退（纯反射场景——Fluent 声明无凭据，仅特性侧）
        // T5 锚门化：所在类型无锚时 [MapWhen] 不消费（主链条件守卫失效 = 无守卫）
        // 收紧：②段准入改**特性锚**——Fluent 对不走 [MapWhen] 特性回退段
        //（①段明细优先面不变， MapWhen 明细扩展非本期）
        var attr = RuntimeAnchorGate.HasAttributeAnchor(targetItem.DeclaringType!)
            ? targetItem.GetCustomAttribute<Attributes.MapWhenAttribute>()
            : null;
        if (attr != null)
            return new WhenDeclaration(null, null, attr.MethodName);

        return null;
    }

    /// <summary>
    /// MapWhen 谓词方法运行时解析（A/B 双规则）— ref="全限定类型::方法名"：
    /// <para>A 形态（用户类型方法，public/internal static）：宿主 = ① targetItem 声明类型短路
    /// （特性侧契约——谓词声明于被标注属性所在类型）→ ② 同程序集 FQN 解析（含嵌套链逐段）。</para>
    /// <para>B 形态（NCMapper.Generated.GeneratedMapper::Map_When_*，private static）：② 命中后
    /// NonPublic 解析——B 形态方法被生成赋值代码直接引用不会被 trimming 裁剪，
    /// MethodInfo 调用经表达式树静态调用编译进映射委托，AOT 口径与既有注册表反射一致。</para>
    /// <para>解析失败抛 MapException（[Convert] 转换器解析失败同口径——显式失败优于静默丢守卫）。</para>
    /// </summary>
    /// <param name="contextType">解析上下文类型（targetItem 声明类型——同程序集解析起点）</param>
    /// <param name="methodRef">谓词方法引用（"FQN::方法名"；null = 特性路径，MethodName 直解析）</param>
    /// <param name="methodName">方法名（methodRef null 时使用——特性实参）</param>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("ResolveWhenPredicate resolves the predicate static method and nested host types via reflection. Some methods may be trimmed.")]
    internal static System.Reflection.MethodInfo ResolveWhenPredicate(Type contextType, string? methodRef, string? methodName)
    {
        // 键前缀追加程序集限定——解析域为 contextType 所在程序集
        //（ResolveWhenHost 短路/Assembly.GetType 均以 contextType 程序集为界），跨程序集同 FQN 类型
        //（collectible ALC / 多程序集同 FullName）+ 同方法名时，无程序集前缀的键会缓存串扰
        //（先解析方 MethodInfo 被后至方命中）。ClearCache 已含本缓存，键格式变更无迁移问题。
        var cacheKey = contextType.Assembly.FullName + "::" + (methodRef ?? contextType.FullName + "::" + methodName);
        if (s_whenPredicateCache.TryGetValue(cacheKey, out var cached))
            // 缓存命中 null = 解析失败标记——同键再调
            // 仍抛同款 MapException（原 `return cached!` 返 null，调用方 ExpressionBuilder
            // BuildWhenGuardCondition 直取 method.GetParameters() 即 NRE；失败语义单点收敛）
            return cached ?? ThrowWhenPredicateResolveFailed(cacheKey);

        System.Reflection.MethodInfo? resolved = null;
        if (methodRef is { Length: > 0 })
        {
            var sep = methodRef.LastIndexOf("::", System.StringComparison.Ordinal);
            var typeFqn = sep >= 0 ? methodRef.Substring(0, sep) : "";
            var name = sep >= 0 ? methodRef.Substring(sep + 2) : methodRef;
            var host = ResolveWhenHost(contextType, typeFqn);
            if (host != null)
                resolved = FindPredicateMethod(host, name);
        }
        else if (methodName is { Length: > 0 })
        {
            // 特性路径：谓词方法契约声明于 targetItem 声明类型（编译期 NCMAP041 同契约）
            resolved = FindPredicateMethod(contextType, methodName);
        }

        s_whenPredicateCache[cacheKey] = resolved;
        return resolved ?? ThrowWhenPredicateResolveFailed(cacheKey);
    }

    /// <summary>谓词解析失败统一抛点——缓存命中 null 与首调解析失败同款</summary>
    private static System.Reflection.MethodInfo ThrowWhenPredicateResolveFailed(string cacheKey) =>
        throw new MapException(
            $"MapWhen 谓词方法解析失败：'{cacheKey}'（宿主类型/方法不可达或签名不符——须为 static bool M(T) 单参形态）");

    /// <summary>谓词宿主类型解析（嵌套链 "." 形态逐段；泛型显示名不支持——Trace 级边界）</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "运行时回退路径 best-effort 解析（Assembly.GetType）；调用链 RequiresUnreferencedCode 已声明，类型被 trim 时降级 MapException（显式失败优于静默丢守卫）。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "GetNestedType 嵌套链解析仅服务 A 形态边界宿主（display-name 形态 best-effort）；B 形态 GeneratedMapper 非嵌套不受影响。")]
    private static Type? ResolveWhenHost(Type contextType, string typeFqn)
    {
        // 短路：声明类型即宿主（特性侧 + Fluent 声明于 DTO 的常见形态）
        if (MatchesDisplayName(contextType, typeFqn)) return contextType;

        var asm = contextType.Assembly;
        // 顶层类型直解析（B 形态 GeneratedMapper 非嵌套非泛型恒走此路径）
        var direct = asm.GetType(typeFqn, throwOnError: false);
        if (direct != null) return direct;

        // 嵌套链（ToDisplayString "." 形态）：最长可解析前缀 + 剩余段 GetNestedType
        var parts = typeFqn.Split('.');
        for (var i = parts.Length - 1; i >= 1; i--)
        {
            var outer = asm.GetType(string.Join(".", parts, 0, i), throwOnError: false);
            if (outer == null) continue;
            var cur = outer;
            for (var j = i; j < parts.Length; j++)
            {
                cur = cur.GetNestedType(parts[j]);
                if (cur == null) break;
            }
            if (cur != null) return cur;
        }
        return null;
    }

    /// <summary>类型显示名匹配（含嵌套链 "." 形态重建比对）</summary>
    private static bool MatchesDisplayName(Type type, string typeFqn)
    {
        if (type.FullName == typeFqn) return true;
        if (!type.IsNested) return false;
        var parts = new List<string>();
        var cur = type;
        while (cur.IsNested) { parts.Add(cur.Name); cur = cur.DeclaringType!; }
        parts.Add(cur.Name);
        parts.Reverse();
        var ns = cur.Namespace;
        var display = ns == null ? string.Join(".", parts) : ns + "." + string.Join(".", parts);
        return string.Equals(display, typeFqn, System.StringComparison.Ordinal);
    }

    /// <summary>谓词方法查找：static、单参、返回 bool（Public|NonPublic——B 形态 private 同解析）</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "GetMethods 反射解析谓词方法（运行时回退路径）；B 形态方法被生成赋值代码直接引用不会被 trim，调用链 RequiresUnreferencedCode 已声明。")]
    private static System.Reflection.MethodInfo? FindPredicateMethod(Type host, string methodName)
    {
        foreach (var m in host.GetMethods(System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
        {
            if (!string.Equals(m.Name, methodName, System.StringComparison.Ordinal)) continue;
            var ps = m.GetParameters();
            if (ps.Length == 1 && m.ReturnType == typeof(bool)) return m;
        }
        return null;
    }

    /// <summary>MapWhen 守卫声明（运行时读取形态）——MethodRef 非 null = 注册表/Fluent 路径（A/B 双形态）；
    /// null 时 MethodName 生效（特性路径——谓词方法在 targetItem 声明类型上解析）；
    /// IsWholeSource null = 特性路径形态运行时判定（单参 == sourceType 判 T2）</summary>
    internal sealed record WhenDeclaration(string? MethodRef, bool? IsWholeSource, string? MethodName);

    /// <summary>
    /// 缓存条目数观测（internal 测试钩子 — / 容量实证与
    /// 观测扩展基础；s_propertiesCache 等为 private 字段，InternalsVisibleTo 仅放行
    /// internal 成员，验收测试经此钩子断言动态类型不缓存后条目数不随动态类型数量增长）
    /// </summary>
    internal static (int Properties, int MapFromAttr, int NormalizedKeys) CacheEntryCount
        => (s_propertiesCache.Count, s_mapFromAttrCache.Count, s_normalizedKeysCache.Count);
}
