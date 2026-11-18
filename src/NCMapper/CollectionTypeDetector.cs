using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using NCMapper.Shared;

namespace NCMapper;

/// <summary>
/// 集合类型检测器 — 统一所有集合类型判断逻辑，消除分散的字符串匹配和类型名后缀判断
/// </summary>
internal static class CollectionTypeDetector
{
    /// <summary>
    /// 判断类型是否实现了 IDictionary&lt;K,V&gt; 泛型接口
    /// <para><see cref="Type.GetInterfaces"/> 不含类型自身
    /// 接口字典声明（如属性类型为 IDictionary&lt;K,V&gt; 自身）曾被误判非字典，补自身判定。</para>
    /// </summary>
    /// <param name="type">要检查的类型</param>
    /// <returns>是泛型字典返回 true，否则 false</returns>
    public static bool IsGenericDictionary(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        // GetInterfaces() 不含自身 —— 接口自身判定先行
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            return true;
        return type.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
    }

    /// <summary>
    /// 判断类型是否实现了非泛型 IDictionary 接口
    /// <para><see cref="Type.GetInterface(string)"/> 对接口自身
    /// 返回 null —— 非泛型 IDictionary 接口自身曾被误判非字典，补自身判定。</para>
    /// </summary>
    /// <param name="type">要检查的类型</param>
    /// <returns>实现 IDictionary 返回 true</returns>
    public static bool IsNonGenericDictionary(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        // GetInterface 对接口自身返回 null —— 自身判定先行
        if (type == typeof(IDictionary))
            return true;
        return type.GetInterface("System.Collections.IDictionary") != null;
    }

    /// <summary>
    /// 判断类型是否为字典类型（泛型或非泛型 IDictionary 系，含 IReadOnlyDictionary&lt;K,V&gt;）
    /// <para>口径收敛 —— 委托
    /// <see cref="TypeCompatAdapter.IsDictionaryShape"/> 权威口径（自身判定 + IReadOnlyDictionary&lt;,&gt;
    /// + 命名空间守卫），消除运行时 detector 与验证器（TypeCompatView）的双口径分叉。</para>
    /// </summary>
    /// <param name="type">要检查的类型</param>
    /// <returns>是字典类型返回 true，否则 false</returns>
    public static bool IsDictionary(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        return TypeCompatAdapter.IsDictionaryShape(type);
    }

    /// <summary>
    /// 判断类型是否实现了 IEnumerable（含数组）
    /// </summary>
    /// <param name="type">要检查的类型</param>
    /// <returns>实现 IEnumerable 返回 true，否则 false</returns>
    public static bool IsEnumerable(Type type)
    {
        return type.IsArray || typeof(IEnumerable).IsAssignableFrom(type);
    }
}
