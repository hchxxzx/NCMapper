using NCMapper.Attributes;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace NCMapper;

/// <summary>
/// 属性匹配逻辑 — 转发到 <see cref="PropertyMatcher"/>
/// </summary>
internal partial class MapperBase<TSource, TTarget> where TTarget : class
{
    /// <summary>
    /// 根据指定的源、目标对象，获取所有可用于处理的属性列表
    /// </summary>
    [RequiresDynamicCode("GetPropertyList builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("GetPropertyList scans type members and attributes via reflection. Some members may be trimmed.")]
    private static KeyValuePair<List<PropertyInfo>, List<PropertyInfo>> GetPropertyList(Type sourceType, Type targetType)
        => PropertyMatcher.GetPropertyList(sourceType, targetType);

    /// <summary>
    /// 获取指定类型的大小写不敏感属性字典（带缓存）
    /// </summary>
    /// <param name="that">要获取属性的类型</param>
    /// <returns>属性名到 PropertyInfo 的映射表；未缓存时返回 null</returns>
    [RequiresUnreferencedCode("GetPropertiesDictIgnoreCase scans type members and attributes via reflection. Some members may be trimmed.")]
    public static Dictionary<string, PropertyInfo>? GetPropertiesDictIgnoreCase(Type that)
        => PropertyMatcher.GetPropertiesDictIgnoreCase(that);

    /// <summary>
    /// 根据目标字段，获取与目标字段相对应的源字段对象
    /// </summary>
    /// <param name="sourceType">源对象类型</param>
    /// <param name="targetItem">目标字段</param>
    /// <param name="sourceTypes">源字段集合</param>
    /// <param name="sourceIsDefine">源对象是否有配置特性</param>
    /// <param name="targetIsDefine">目标对象是否有配置特性</param>
    /// <param name="attr">字段特性</param>
    /// <param name="split">锚门拆分结果（可选参数——循环调用方
    /// 上移复用；null = 现状自算。 ′ 封堵：包装器不透传则上移在该路径静默失效，复审 ′）</param>
    [RequiresUnreferencedCode("Non-default config path triggers global config freeze, which reflects over [assembly: MapDefaults] when no generated snapshot exists.")]
    private static PropertyInfo? GetSourceItemByTargetItem(Type sourceType, PropertyInfo targetItem,
        List<PropertyInfo> sourceTypes, bool sourceIsDefine, bool targetIsDefine,
        out MapPropertyAttribute? attr,
        (List<PropertyInfo> WithAttr, List<PropertyInfo> WithoutAttr)? split = null)
        => PropertyMatcher.GetSourceItemByTargetItem(sourceType, targetItem, sourceTypes, sourceIsDefine, targetIsDefine, out attr, split);

    /// <summary>
    /// 判断属性是否应被忽略（属性级 [MapIgnore] + 全局忽略清单；映射对级豁免由
    /// <paramref name="globalIgnoreExempt"/> 门控）
    /// </summary>
    /// <param name="prop">属性信息</param>
    /// <param name="isToDto">是否为 ToDto 方向</param>
    /// <param name="globalIgnoreExempt">映射对级豁免（目标属性带 OverrideIgnore=true 显式特性时 true）</param>
    [RequiresUnreferencedCode("Global ignore check triggers config freeze, which reflects over [assembly: MapDefaults] when no generated snapshot exists.")]
    private static bool ShouldIgnore(PropertyInfo prop, bool isToDto, bool globalIgnoreExempt = false)
        => PropertyMatcher.ShouldIgnore(prop, isToDto, globalIgnoreExempt);
}
