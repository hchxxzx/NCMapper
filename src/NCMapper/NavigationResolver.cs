using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace NCMapper;

/// <summary>
/// 导航属性解析器 — 在源类型中按类型匹配查找导航属性
/// <para>统一 MapProjection / ExpressionBuilder / MapperBase 中四处内联逻辑，消除漂移风险</para>
/// <para>匹配策略：按类型匹配（引用导航优先，集合导航兜底）；同类型多匹配时取第一个并输出 Trace 提示</para>
/// </summary>
internal static class NavigationResolver
{
    /// <summary>
    /// 在源类型属性列表中按类型匹配解析导航属性
    /// <para>匹配策略：按类型匹配（引用导航优先，集合导航兜底）；同类型多匹配时取第一个并输出 Trace 提示</para>
    /// </summary>
    /// <param name="sourceProperties">源类型的公共实例属性列表</param>
    /// <param name="navigationType">期望的导航属性类型（[MapProperty].FromSource）</param>
    /// <param name="sourceTypeName">源类型名称（仅用于 Trace 输出）</param>
    /// <returns>匹配的导航属性；未找到时返回 null</returns>
    [RequiresUnreferencedCode("ResolveNavigationProperty scans source type properties via reflection (including GetCollectionElementType interface probing). Some members may be trimmed.")]
    public static PropertyInfo? ResolveNavigationProperty(
        List<PropertyInfo> sourceProperties,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type navigationType,
        string sourceTypeName)
    {
        // 引用导航匹配（按类型精确匹配）
        var candidates = sourceProperties
            .Where(p => p.PropertyType == navigationType).ToList();

        // 集合导航匹配（List<X>/ICollection<X>/X[] 等，元素类型 == navigationType）
        if (candidates.Count == 0)
            candidates = sourceProperties
                .Where(p => GetCollectionElementType(p.PropertyType) == navigationType).ToList();

        if (candidates.Count == 0) return null;

        // 同类型多导航：取第一个 + Trace 提示（补偿移除显式导航消歧能力后的可诊断性）
        // 前置守卫——Trace 关闭态免插值白分配
        if (candidates.Count > 1 && MapTracer.GloballyEnabled)
            MapTracer.Write($"[NCMapper] {sourceTypeName} 存在 {candidates.Count} 个类型为 " +
                $"{navigationType.Name} 的导航属性，已选取第一个：{candidates[0].Name}");

        return candidates[0];
    }

    /// <summary>
    /// 提取集合类型的元素类型（非集合返回 null）
    /// <para>覆盖：数组、List&lt;T&gt;、ICollection&lt;T&gt;、IEnumerable&lt;T&gt;、HashSet&lt;T&gt;、IReadOnlyList&lt;T&gt;、
    /// 继承自泛型集合的非泛型类（class Orders : List&lt;Order&gt;）</para>
    /// <para>统一集合判定逻辑，供 NavigationPathCollector 复用（A2 审计修复）</para>
    /// </summary>
    /// <param name="type">指定类型。</param>
    /// <returns>返回类型对象（可能为 null）。</returns>
    internal static Type? GetCollectionElementType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        if (type == typeof(string)) return null;
        if (type.IsArray) return type.GetElementType();

        // 排除字典类型（与 SourceGen TypeHelpers.IsDictionaryType 对齐）
        if (IsDictionaryType(type)) return null;

        // 泛型集合（List<T>/ICollection<T>/IEnumerable<T>/HashSet<T> 等）
        if (type.IsGenericType)
        {
            var genArgs = type.GetGenericArguments();
            if (genArgs.Length == 1 && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
                return genArgs[0];
        }

        // 继承自泛型集合的非泛型类（class Orders : List<Order>）
        // 沿接口链查找 IEnumerable<T>
        // 循环不变条件（IEnumerable 可赋值性）提循环外
        if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            return null;
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType
                && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    /// 判断类型是否为字典（IDictionary&lt;K,V&gt; / IReadOnlyDictionary&lt;K,V&gt; 及其实现）
    /// </summary>
    private static bool IsDictionaryType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(System.Collections.Generic.IDictionary<,>)
                || def == typeof(System.Collections.Generic.IReadOnlyDictionary<,>)
                || def == typeof(System.Collections.Generic.Dictionary<,>))
                return true;
        }
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType)
            {
                var def = iface.GetGenericTypeDefinition();
                if (def == typeof(System.Collections.Generic.IDictionary<,>)
                    || def == typeof(System.Collections.Generic.IReadOnlyDictionary<,>))
                    return true;
            }
        }
        return false;
    }
}
