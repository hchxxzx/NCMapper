using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace NCMapper.Internal;

/// <summary>
/// MapperBase 委托工厂实现 — MapperBase&lt;TSource,TTarget&gt; 经显式静态构造函数注册到
/// <see cref="MapperDelegateRegistry"/>（④ 策略化深化）。
/// <para>替代 ExpressionBuilder 3 处 typeof(MapperBase&lt;,&gt;).MakeGenericType(...)
/// 反射调用（TryBuildNewExpr:1656 record 构造器参数匹配 / GetClassExpression:1727 嵌套
/// fallback / GetListExpression:2005 集合递归），消除 P1-SHOULD-1 循环依赖。</para>
/// <para>泛型约束与 MapperBase 对齐：<c>where TTarget : class</c>（TSource 无约束、
/// 不加 new()——record 无无参构造路径正是 BuildConstructorArgs 服务的场景，
/// 加 new() 约束使该委托路径自设计起不可达）。</para>
/// </summary>
internal sealed class MapperBaseDelegateImpl<TSource, TTarget> : IMapperBaseDelegate
    where TTarget : class
{
    // 反射 MethodInfo 静态缓存（每次调用 GetMethod 反射查找不可接受）；
    // impl 泛型静态字段在 Register 触发 impl 类型初始化时计算一次，后续零开销。
    // GetMethod 用 (string, Type[]) 2 参重载（.NET 无 (string, BindingFlags, Type[])
    // 3 参重载；公共静态方法默认绑定即可，Map/MapList 重载按参数类型数组区分）
    //
    // nameof 引用带 Requires* 标注的方法组触发 IL2026（方法组含多个带标注重载时逐个报）；
    // 静态字段初始化器归属 .cctor 无法用 Requires* 标注（同 MapperBase.cs s_mapActionLazy
    // 先例）——#pragma 精确抑制，标注语义由本类三个接口实现方法承接
#pragma warning disable IL2026 // RequiresUnreferencedCode
    private static readonly MethodInfo s_mapMethod = typeof(MapperBase<TSource, TTarget>).GetMethod(
        nameof(MapperBase<TSource, TTarget>.Map),
        new[] { typeof(TSource), typeof(Action<TSource, TTarget>) })!;

    private static readonly MethodInfo s_mapListMethod = typeof(MapperBase<TSource, TTarget>).GetMethod(
        nameof(MapperBase<TSource, TTarget>.MapList),
        new[] { typeof(IEnumerable<TSource>), typeof(Action<TSource, TTarget>) })!;
#pragma warning restore IL2026 // RequiresUnreferencedCode

    /// <summary>
    /// 构建 record 构造器参数 — 直调 MapperBase.BuildConstructorArgs（签名一一对应）。
    /// <para>现状 private static 改 internal static 直调——私有成员访问（GetPropertiesDictIgnoreCase 等）
    /// 留在 MapperBase 方法体内，不外泄。</para>
    /// </summary>
    [RequiresDynamicCode("BuildConstructorArgs uses MakeGenericType and reflection. Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildConstructorArgs scans type members via reflection. Some members may be trimmed.")]
    public (ConstructorInfo Ctor, Expression[] Args) BuildConstructorArgs(Type targetType, Type sourceType, Expression sourceParam)
        => MapperBase<TSource, TTarget>.BuildConstructorArgs(targetType, sourceType, sourceParam);

    /// <summary>
    /// 嵌套对象映射 fallback — 仅构造 Map 调用表达式（null 守卫由 GetClassExpression 现状逻辑承担）。
    /// </summary>
    [RequiresDynamicCode("Map uses MakeGenericType and reflection. Not supported in AOT.")]
    [RequiresUnreferencedCode("Map scans type members via reflection. Some members may be trimmed.")]
    public Expression MapFallback(Expression sourceExpr)
        => Expression.Call(s_mapMethod, sourceExpr,
            Expression.Constant(null, typeof(Action<TSource, TTarget>)));

    /// <summary>
    /// 集合递归映射 — 构造 MapList 调用表达式。
    /// </summary>
    [RequiresDynamicCode("MapList uses MakeGenericType and reflection. Not supported in AOT.")]
    [RequiresUnreferencedCode("MapList scans type members via reflection. Some members may be trimmed.")]
    public Expression MapList(Expression sourceExpr)
        => Expression.Call(s_mapListMethod, sourceExpr,
            Expression.Constant(null, typeof(Action<TSource, TTarget>)));
}
