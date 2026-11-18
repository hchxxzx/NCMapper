using NCMapper.Runtime;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NCMapper.Attributes;
namespace NCMapper;
/// <summary>
/// 属性忽略判断 — 从 MapperBase.Properties 提取，减少泛型类耦合
/// <para>两层判定：属性级 [MapIgnore]（显式忽略恒生效，不参与豁免）+
/// 全局忽略清单（fail-closed；映射对级豁免由 <see cref="HasOverrideIgnore"/> 门控）。</para>
/// <para>② 枢纽深化：新增 Convert/Mask/ConvertBack 特性查询缓存，
/// 供 AssignmentPlanner.Plan(PropertyInfo) 前置判定 + 三渲染器按 Kind 查特性时复用，
/// 缓解特性查询次数增量（现状约 2-3 次/属性 → 改造后约 6-7 次，缓存后反射调用次数等价）。</para>
/// </summary>
internal static class PropertyIgnoreChecker
{
    /// <summary>
    /// MapIgnoreAttribute 缓存 — 避免每次属性匹配都反射调用 GetCustomAttribute
    /// <para>缓存 PropertyInfo → MapIgnoreAttribute?，方向判断从缓存特性计算，无反射</para>
    /// </summary>
    private static readonly ConcurrentDictionary<PropertyInfo, MapIgnoreAttribute?> s_ignoreAttrCache = new();

    /// <summary>
    /// ConvertAttribute 缓存 — ② 新增：Plan(PropertyInfo) 前置判定 + RenderPlan 按 Kind 查特性时复用
    /// </summary>
    private static readonly ConcurrentDictionary<PropertyInfo, ConvertAttribute?> s_convertUsingAttrCache = new();

    /// <summary>
    /// MaskAttribute 缓存 — ② 新增：Plan(PropertyInfo) 前置判定 + RenderPlan 按 Kind 查特性时复用
    /// </summary>
    private static readonly ConcurrentDictionary<PropertyInfo, MaskAttribute?> s_maskAttrCache = new();

    /// <summary>
    /// ConvertBackAttribute 缓存 — ② 新增：Plan(PropertyInfo) 前置判定 + RenderPlan 按 Kind=ConvertBackUsing 查特性时复用
    /// </summary>
    private static readonly ConcurrentDictionary<PropertyInfo, ConvertBackAttribute?> s_convertBackUsingAttrCache = new();

    /// <summary>
    /// MapFormatAttribute 缓存：Plan(PropertyInfo) 前置判定（3.5 步）
    /// + Format 族策略按 Kind 查特性时复用（家族惯例同 Mask）。
    /// </summary>
    private static readonly ConcurrentDictionary<PropertyInfo, MapFormatAttribute?> s_mapFormatAttrCache = new();

    /// <summary>
    /// MapPropertyAttribute 缓存
    /// <para>构建期属性匹配热路径（GetPropertyList/GetSourceItemByTargetItem/TryGetMapPropertyInfo）
    /// 逐属性反射查询统一收敛到本缓存族——消除 O(n×m) 匹配循环内重复 GetCustomAttribute 反射。
    /// 同口径：动态类型直算不入缓存（ALC 卸载守卫）。</para>
    /// </summary>
    private static readonly ConcurrentDictionary<PropertyInfo, MapPropertyAttribute?> s_mapPropertyAttrCache = new();

    /// <summary>
    /// 查询属性的 [MapProperty] 特性（带缓存）
    /// <para>构建期属性匹配热路径统一入口（含 IsDefined+GetCustomAttribute 合并场景——
    /// 一次缓存查询替代 IsDefined 探测 + GetCustomAttribute 读取两次反射）。
    /// 首次查询走反射，后续从缓存取；动态类型直算不入缓存（守卫同口径）。</para>
    /// </summary>
    /// <param name="prop">属性信息</param>
    /// <returns>特性实例；未标注返回 null</returns>
    public static MapPropertyAttribute? GetMapPropertyAttribute(PropertyInfo prop)
        => IsDynamicProperty(prop)
            ? prop.GetCustomAttribute<MapPropertyAttribute>()
            : s_mapPropertyAttrCache.GetOrAdd(prop, p => p.GetCustomAttribute<MapPropertyAttribute>());

    /// <summary>
    /// 判断属性是否应被忽略 — 属性级 [MapIgnore] + 全局忽略清单（fail-closed）
    /// <para>判定序：① 属性级 [MapIgnore] 命中 → 忽略（显式忽略恒生效，不参与 OverrideIgnore 豁免，
    /// 与 OverrideIgnore=true 同标即矛盾，源生成器报 NCMAP022）；
    /// ② globalIgnoreExempt=true（目标属性带 OverrideIgnore=true 的显式特性，映射对级豁免）→ 跳过全局判定；
    /// ③ 全局忽略清单命中 → 忽略。</para>
    /// <para>方向判断：如果目标类型标注了 [MapFrom]，说明目标是 DTO，方向是 ToDto；否则是 ToEntity</para>
    /// </summary>
    /// <param name="prop">属性信息</param>
    /// <param name="isToDto">是否为 Entity→DTO 方向（true=ToDto，false=ToEntity）</param>
    /// <param name="globalIgnoreExempt">映射对级豁免（目标属性带 OverrideIgnore=true 显式特性时 true）</param>
    /// <returns>true 表示该属性在当前方向应被忽略</returns>
    [RequiresUnreferencedCode("Global ignore check triggers config freeze, which reflects over [assembly: MapDefaults] when no generated snapshot exists.")]
    public static bool ShouldIgnore(PropertyInfo prop, bool isToDto, bool globalIgnoreExempt = false)
    {
        // 动态类型直算不入缓存（ALC 卸载守卫，与四查询方法同口径）
        // T5 锚门化：所在类型无锚时 [MapIgnore] 不消费（"无锚不消费"
        // A-11 承载侧（DTO 属性标 [MapIgnore]）为唯一合法形态，实体侧形态随 A-14 退役）
        // 收紧：准入由双锚（HasAnchor）改**特性锚**（HasAttributeAnchor）
        // Fluent 对（仅注册表命中）不再消费属性级 [MapIgnore]，按快照明细执行（007 双引擎分叉收口）
        var ignoreAttr = RuntimeAnchorGate.HasAttributeAnchor(prop.DeclaringType!)
            ? (IsDynamicProperty(prop)
                ? prop.GetCustomAttribute<MapIgnoreAttribute>()
                : s_ignoreAttrCache.GetOrAdd(prop, p => p.GetCustomAttribute<MapIgnoreAttribute>()))
            : null;
        if (ignoreAttr != null)
        {
            return ignoreAttr.Direction switch
            {
                Direction.Both => true,
                Direction.ToDto => isToDto,
                Direction.ToEntity => !isToDto,
                _ => true
            };
        }

        if (globalIgnoreExempt)
            return false;

        return GlobalIgnoreChecker.ShouldIgnore(prop, isToDto);
    }

    /// <summary>
    /// 动态类型守卫
    /// <para>四缓存键为 PropertyInfo，其 DeclaringType 强引用链会阻止可收集程序集（ALC）卸载
    /// （方案 b 同口径）。动态类型的属性直算不入缓存——消除强引用滞留，静态类型零变化。</para>
    /// </summary>
    private static bool IsDynamicProperty(PropertyInfo prop)
        => PropertyMatcher.IsDynamicType(prop.DeclaringType!);

    /// <summary>
    /// 判断属性是否应被忽略（仅属性级 [MapIgnore]）— 供属性枚举阶段（PropertyMatcher.GetPropertyList）过滤使用。
    /// <para>属性枚举阶段不得应用全局忽略判定：源属性命中全局忽略时若在此被剔除，
    /// 映射对级豁免（目标属性 OverrideIgnore=true）将无法救回该映射对；
    /// 全局忽略统一在双侧判定处（ShouldIgnore(source) || ShouldIgnore(target)）裁决。</para>
    /// </summary>
    /// <param name="prop">属性信息</param>
    /// <param name="isToDto">是否为 Entity→DTO 方向（true=ToDto，false=ToEntity）</param>
    /// <returns>true 表示该属性在当前方向应被忽略</returns>
    public static bool ShouldIgnoreAttributeOnly(PropertyInfo prop, bool isToDto)
    {
        // 动态类型直算不入缓存（ALC 卸载守卫，口径同 GetPropertiesDictIgnoreCase）
        // T5 锚门化：所在类型无锚时 [MapIgnore] 不消费（同 ShouldIgnore 门口径）
        // 收紧：准入改**特性锚**——Fluent 对不消费属性级 [MapIgnore]
        var ignoreAttr = RuntimeAnchorGate.HasAttributeAnchor(prop.DeclaringType!)
            ? (IsDynamicProperty(prop)
                ? prop.GetCustomAttribute<MapIgnoreAttribute>()
                : s_ignoreAttrCache.GetOrAdd(prop, p => p.GetCustomAttribute<MapIgnoreAttribute>()))
            : null;
        if (ignoreAttr == null) return false;

        return ignoreAttr.Direction switch
        {
            Direction.Both => true,
            Direction.ToDto => isToDto,
            Direction.ToEntity => !isToDto,
            _ => true
        };
    }

    /// <summary>
    /// 查询属性的 [Convert] 特性（带缓存）
    /// <para>供 AssignmentPlanner.Plan(PropertyInfo) 前置判定 + RenderPlan 按 Kind=ConvertUsing 查特性时复用。
    /// 首次查询走反射 GetCustomAttribute，后续从缓存取（ConcurrentDictionary 线程安全）。</para>
    /// <para>动态类型直算不入缓存（ALC 卸载守卫）。</para>
    /// </summary>
    /// <param name="prop">属性信息</param>
    /// <returns>特性实例；未标注返回 null</returns>
    public static ConvertAttribute? GetConvertAttribute(PropertyInfo prop)
        => IsDynamicProperty(prop)
            ? prop.GetCustomAttribute<ConvertAttribute>()
            : s_convertUsingAttrCache.GetOrAdd(prop, p => p.GetCustomAttribute<ConvertAttribute>());

    /// <summary>
    /// 查询属性的 [Mask] 特性（带缓存）
    /// <para>供 AssignmentPlanner.Plan(PropertyInfo) 前置判定 + RenderPlan 按 Kind=Mask 查特性时复用。</para>
    /// <para>动态类型直算不入缓存（ALC 卸载守卫）。</para>
    /// </summary>
    /// <param name="prop">属性信息</param>
    /// <returns>特性实例；未标注返回 null</returns>
    public static MaskAttribute? GetMaskAttribute(PropertyInfo prop)
        => IsDynamicProperty(prop)
            ? prop.GetCustomAttribute<MaskAttribute>()
            : s_maskAttrCache.GetOrAdd(prop, p => p.GetCustomAttribute<MaskAttribute>());

    /// <summary>
    /// 查询属性的 [ConvertBack] 特性（带缓存）
    /// <para>供 AssignmentPlanner.Plan(PropertyInfo) 前置判定 + RenderPlan 按 Kind=ConvertBackUsing 查特性时复用。</para>
    /// <para>动态类型直算不入缓存（ALC 卸载守卫）。</para>
    /// </summary>
    /// <param name="prop">属性信息</param>
    /// <returns>特性实例；未标注返回 null</returns>
    public static ConvertBackAttribute? GetConvertBackAttribute(PropertyInfo prop)
        => IsDynamicProperty(prop)
            ? prop.GetCustomAttribute<ConvertBackAttribute>()
            : s_convertBackUsingAttrCache.GetOrAdd(prop, p => p.GetCustomAttribute<ConvertBackAttribute>());

    /// <summary>
    /// 查询属性的 [MapFormat] 特性（带缓存）
    /// <para>供 AssignmentPlanner.Plan(PropertyInfo) 3.5 步判定 + Format 族策略按 Kind 查特性时复用。</para>
    /// <para>动态类型直算不入缓存（ALC 卸载守卫）。</para>
    /// </summary>
    /// <param name="prop">属性信息</param>
    /// <returns>特性实例；未标注返回 null</returns>
    public static MapFormatAttribute? GetMapFormatAttribute(PropertyInfo prop)
        => IsDynamicProperty(prop)
            ? prop.GetCustomAttribute<MapFormatAttribute>()
            : s_mapFormatAttrCache.GetOrAdd(prop, p => p.GetCustomAttribute<MapFormatAttribute>());

    /// <summary>
    /// 检测目标属性是否声明了映射对级豁免（OverrideIgnore=true 显式特性）
    /// <para>覆盖 [MapProperty]/[Convert]/[Mask]/[ConvertBack] 四特性（覆盖范围
    /// + 补齐反向豁免门）；
    /// 任一特性带 OverrideIgnore=true 即豁免。豁免作用于映射对整体，
    /// 双侧判定处（ShouldIgnore(source)||ShouldIgnore(target)）任一命中即整对忽略，豁免时两侧全局判定均跳过。</para>
    /// <para>② 枢纽深化：[Convert]/[Mask]/[ConvertBack] 查询改用缓存方法（与 Plan 共享缓存）。</para>
    /// </summary>
    /// <param name="prop">目标属性信息</param>
    /// <returns>声明了豁免返回 true</returns>
    public static bool HasOverrideIgnore(PropertyInfo prop)
    {
        // MapProperty 查询收敛缓存族
        // （原每属性 GetCustomAttribute 裸反射，与三特性查询缓存分叉）
        // T5 锚门化：任一特性命中且所在类无锚 → 不豁免
        //（豁免语义仅对有锚映射对有意义；无锚类上的标注一律不消费）
        // 收紧：豁免门准入改**特性锚**——Fluent 对不因属性级标注产豁免语义
        if (!RuntimeAnchorGate.HasAttributeAnchor(prop.DeclaringType!))
            return false;
        return (GetMapPropertyAttribute(prop)?.OverrideIgnore ?? false)
            || (GetConvertAttribute(prop)?.OverrideIgnore ?? false)
            || (GetMaskAttribute(prop)?.OverrideIgnore ?? false)
            || (GetConvertBackAttribute(prop)?.OverrideIgnore ?? false)
            || (GetMapFormatAttribute(prop)?.OverrideIgnore ?? false); // 豁免门聚合点含 Format 族
    }

    /// <summary>
    /// 清除所有特性查询缓存 — 供 PropertyMatcher.ClearCache() 调用
    /// <para>② 枢纽深化：新增 Convert/Mask/ConvertBack 三缓存清除。</para>
    /// <para>新增 MapProperty 缓存清除（缓存族统一收敛配套）。</para>
    /// </summary>
    internal static void ClearCache()
    {
        s_ignoreAttrCache.Clear();
        s_convertUsingAttrCache.Clear();
        s_maskAttrCache.Clear();
        s_convertBackUsingAttrCache.Clear();
        s_mapPropertyAttrCache.Clear();
        s_mapFormatAttrCache.Clear(); // MapFormat 缓存清除（缓存族统一收敛配套）
    }
}
