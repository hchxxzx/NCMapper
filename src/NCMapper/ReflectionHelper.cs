using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NCMapper.Internal;
namespace NCMapper;
/// <summary>
/// 反射辅助方法 — 提供 null 安全的成员查找，替代 ! 空断言
/// <para>内置 ConcurrentDictionary 缓存，避免表达式树构建期重复 GetMethod 反射查找
/// （ExpressionBuilder 通过反射回调 MapperBase&lt;,&gt; 的 Map/MapList/BuildConstructorArgs 等方法，
/// 同一类型对在表达式树构建过程中可能被多次查找）</para>
/// </summary>
internal static class ReflectionHelper
{
    // CacheRegistry 登记制自注册：仅登记清理委托，不触发缓存构建（防自触发）
    static ReflectionHelper()
    {
        CacheRegistry.Register("reflection-helper", CacheLayer.Full, ClearCache);
    }

    /// <summary>
    /// 无参签名方法缓存 — 键为 (类型, 方法名)
    /// </summary>
    private static readonly ConcurrentDictionary<(Type Type, string Name), MethodInfo> s_methodNoArgsCache = new();

    /// <summary>
    /// 带参数签名方法缓存 — 键为 (类型, 方法名, 参数类型签名)
    /// <para>参数类型签名用 FullName 拼接，避免 Type[] 数组哈希不稳定</para>
    /// </summary>
    private static readonly ConcurrentDictionary<(Type Type, string Name, string ParamKey), MethodInfo> s_methodWithArgsCache = new();

    /// <summary>
    /// 带绑定标志方法缓存 — 键为 (类型, 方法名, 绑定标志)
    /// </summary>
    private static readonly ConcurrentDictionary<(Type Type, string Name, BindingFlags Flags), MethodInfo> s_methodWithFlagsCache = new();

    /// <summary>
    /// 属性查找缓存 — 与 Method 缓存同口径
    /// </summary>
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo> s_propertyCache = new();

    /// <summary>
    /// 构造器查找缓存 — 与 Method 缓存同口径
    /// </summary>
    private static readonly ConcurrentDictionary<(Type Type, string ParamKey), ConstructorInfo> s_ctorCache = new();

    /// <summary>
    /// 将参数类型数组转换为稳定的字符串键 — 用 FullName 拼接，避免 Type[] 数组哈希不稳定
    /// <para>FullName=null 回退（动态类型/泛型参数）改用
    /// 模块限定名——原回退 Name 在同模块同名类型（嵌套 T 与另一 T）存在撞键风险。</para>
    /// </summary>
    /// <param name="types">参数类型数组</param>
    /// <returns>稳定的字符串键</returns>
    private static string BuildParamKey(Type[] types)
    {
        if (types == null || types.Length == 0) return "";
        return string.Join("|", types.Select(t => t.FullName ?? (t.Module.ToString() + "::" + t.Name)));
    }

    /// <summary>
    /// 获取公共方法 — 未找到时抛出含类型和方法名的 InvalidOperationException
    /// <para>通过 ConcurrentDictionary 缓存，同一 (类型,方法名) 仅反射查找一次</para>
    /// </summary>
    /// <param name="type">要查找的类型</param>
    /// <param name="name">方法名</param>
    /// <returns>找到的 MethodInfo</returns>
    /// <exception cref="InvalidOperationException">方法未找到</exception>
    [RequiresUnreferencedCode("GetRequiredMethod looks up members via reflection during runtime delegate construction. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static MethodInfo GetRequiredMethod(Type type, string name)
    {
        return s_methodNoArgsCache.GetOrAdd((type, name), k =>
            k.Type.GetMethod(k.Name)
                ?? throw new InvalidOperationException(
                    ErrorMessages.ReflectionMethodNotFound(k.Type.FullName!, k.Name)));
    }

    /// <summary>
    /// 获取带参数签名的公共方法 — 未找到时抛出含详细信息的 InvalidOperationException
    /// <para>通过 ConcurrentDictionary 缓存，同一 (类型,方法名,参数签名) 仅反射查找一次</para>
    /// </summary>
    /// <param name="type">要查找的类型</param>
    /// <param name="name">方法名</param>
    /// <param name="types">参数类型数组</param>
    /// <returns>找到的 MethodInfo</returns>
    /// <exception cref="InvalidOperationException">方法未找到</exception>
    [RequiresUnreferencedCode("GetRequiredMethod looks up members via reflection during runtime delegate construction. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static MethodInfo GetRequiredMethod(Type type, string name, Type[] types)
    {
        var paramKey = BuildParamKey(types);
        return s_methodWithArgsCache.GetOrAdd((type, name, paramKey), k =>
            k.Type.GetMethod(k.Name, types)
                ?? throw new InvalidOperationException(
                    ErrorMessages.ReflectionMethodNotFoundWithParams(k.Type.FullName!, k.Name, string.Join(", ", types.Select(t => t.Name)))));
    }

    /// <summary>
    /// 获取公共属性 — 未找到时抛出含类型和属性名的 InvalidOperationException
    /// <para>缓存补齐（与 Method 查找同口径，消除构建期重复 GetProperty 反射）</para>
    /// </summary>
    /// <param name="type">要查找的类型</param>
    /// <param name="name">属性名</param>
    /// <returns>找到的 PropertyInfo</returns>
    /// <exception cref="InvalidOperationException">属性未找到</exception>
    [RequiresUnreferencedCode("GetRequiredProperty looks up members via reflection during runtime delegate construction. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static PropertyInfo GetRequiredProperty(Type type, string name)
    {
        return s_propertyCache.GetOrAdd((type, name), k =>
            k.Type.GetProperty(k.Name)
                ?? throw new InvalidOperationException(
                    ErrorMessages.ReflectionPropertyNotFound(k.Type.FullName!, k.Name)));
    }

    /// <summary>
    /// 获取公共构造器 — 未找到时抛出含类型和参数签名的 InvalidOperationException
    /// <para>缓存补齐（与 Method 查找同口径）</para>
    /// </summary>
    /// <param name="type">要查找的类型</param>
    /// <param name="types">参数类型数组</param>
    /// <returns>找到的 ConstructorInfo</returns>
    /// <exception cref="InvalidOperationException">构造器未找到</exception>
    [RequiresUnreferencedCode("GetRequiredConstructor looks up constructors via reflection during runtime delegate construction. Some constructors may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static ConstructorInfo GetRequiredConstructor(Type type, Type[] types)
    {
        var paramKey = BuildParamKey(types);
        return s_ctorCache.GetOrAdd((type, paramKey), k =>
            k.Type.GetConstructor(types)
                ?? throw new InvalidOperationException(
                    ErrorMessages.ReflectionCtorNotFound(k.Type.FullName!, string.Join(", ", types.Select(t => t.Name)))));
    }

    /// <summary>
    /// 按绑定标志获取方法 — 用于查找 NonPublic/Static 方法
    /// <para>通过 ConcurrentDictionary 缓存，同一 (类型,方法名,绑定标志) 仅反射查找一次
    /// （ExpressionBuilder.TryBuildNewExpr 通过此重载查找 MapperBase&lt;,&gt;.BuildConstructorArgs 私有静态方法）</para>
    /// </summary>
    /// <param name="type">要查找的类型</param>
    /// <param name="name">方法名</param>
    /// <param name="bindingFlags">绑定标志</param>
    /// <returns>找到的 MethodInfo</returns>
    /// <exception cref="InvalidOperationException">方法未找到</exception>
    [RequiresUnreferencedCode("GetRequiredMethod looks up members via reflection during runtime delegate construction. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static MethodInfo GetRequiredMethod(Type type, string name, BindingFlags bindingFlags)
    {
        return s_methodWithFlagsCache.GetOrAdd((type, name, bindingFlags), k =>
            k.Type.GetMethod(k.Name, k.Flags)
                ?? throw new InvalidOperationException(
                    ErrorMessages.ReflectionMethodNotFoundWithFlags(k.Type.FullName!, k.Name, k.Flags.ToString())));
    }

    /// <summary>
    /// 清空所有反射缓存 — 由 Mapper.ClearCache() 调用，确保静态缓存不持有已卸载程序集的引用
    /// </summary>
    public static void ClearCache()
    {
        s_methodNoArgsCache.Clear();
        s_methodWithArgsCache.Clear();
        s_methodWithFlagsCache.Clear();
        s_propertyCache.Clear(); // N-10
        s_ctorCache.Clear(); // N-10
    }
}
