using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace NCMapper.Internal;

/// <summary>
/// MapperBase 委托工厂 — 解 P1-SHOULD-1 循环依赖（④ 策略化深化）。
/// <para>改造前 NCMapper.ExpressionBuilder 曾 3 处反射回指 TryBuildNewExpr / GetClassExpression / GetListExpression，
/// 并通过 typeof(MapperBase&lt;,&gt;).MakeGenericType(...) 反射调用 MapperBase 私有静态方法；本接口将反射调用改为委托注入，
/// 由 MapperBase&lt;TSource,TTarget&gt; 显式静态构造函数注册实现（现由 NCMapper.Internal.MapperDelegateRegistry 承接）。</para>
/// <para>设计为接口而非委托字段：3 处调用语义独立（record 构造器参数匹配 / 嵌套 fallback / 集合递归），
/// 接口方法签名可独立演化；委托字段需打包 3 个委托，签名耦合。</para>
/// <para>注册时序：MapperBase&lt;TSource,TTarget&gt; 新增显式
/// 静态构造函数，Register 前置于构造体首行（先于任何可能触发 ExpressionBuilder 调用的逻辑）；
/// Resolve 未命中时经 RuntimeHelpers.RunClassConstructor 触发目标类型对静态构造后重试一次
/// （覆盖编译期尚未触及的嵌套/集合元素类型对——现状 MapperBase 静态字段为 Lazy 懒加载，
/// 无显式 .cctor，嵌套类型对在编译期不会初始化，v1.0“MapperBase 静态构造先于
/// ExpressionBuilder 调用”论断失实已废弃）。</para>
/// <para>委托解析只在 3 处反射回指点按实际类型对独立查询（TryBuildNewExpr 按嵌套属性类型对 /
/// GetClassExpression Fallback 按嵌套类型对 / GetListExpression 按集合元素类型对），
/// 不进 BuildSession、不进 BuildMapBody/TryGetInlineMapBody 签名。</para>
/// </summary>
internal interface IMapperBaseDelegate
{
    /// <summary>
    /// 构建 record 构造器参数（替代现状 TryBuildNewExpr:1656 反射调用 BuildConstructorArgs，v1.1 签名修正）。
    /// </summary>
    /// <param name="targetType">目标类型（与 MapperBase.BuildConstructorArgs 一一对应）</param>
    /// <param name="sourceType">源类型</param>
    /// <param name="sourceParam">源参数表达式</param>
    /// <returns>构造器 + 参数表达式数组（调用方 New(ctor, args) 组装）</returns>
    [RequiresDynamicCode("BuildConstructorArgs uses MakeGenericType and reflection. Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildConstructorArgs scans type members via reflection. Some members may be trimmed.")]
    (ConstructorInfo Ctor, Expression[] Args) BuildConstructorArgs(Type targetType, Type sourceType, Expression sourceParam);

    /// <summary>
    /// 嵌套对象映射 fallback（替代现状 GetClassExpression:1727 反射调用 MapperBase.Map(src, null)）。
    /// </summary>
    /// <param name="sourceExpr">源对象表达式</param>
    /// <returns>映射调用表达式（null 守卫由 GetClassExpression 现状逻辑承担）</returns>
    [RequiresDynamicCode("Map uses MakeGenericType and reflection. Not supported in AOT.")]
    [RequiresUnreferencedCode("Map scans type members via reflection. Some members may be trimmed.")]
    Expression MapFallback(Expression sourceExpr);

    /// <summary>
    /// 集合递归映射（替代现状 GetListExpression:2005 反射调用 MapperBase.MapList）。
    /// </summary>
    /// <param name="sourceExpr">源集合表达式</param>
    /// <returns>映射结果列表表达式</returns>
    [RequiresDynamicCode("MapList uses MakeGenericType and reflection. Not supported in AOT.")]
    [RequiresUnreferencedCode("MapList scans type members via reflection. Some members may be trimmed.")]
    Expression MapList(Expression sourceExpr);
}

/// <summary>
/// 委托工厂注册中心 — 类型对→委托实现的查询（双层 CWT 弱键承载，先例 DegradationDetectorBase.Agnostic）。
/// <para><b>弱键口径</b>：原 ConcurrentDictionary&lt;(Type,Type),…&gt;
/// 强键元组会钉住 collectible ALC 动态类型（MapperBase&lt;S,T&gt; 闭合泛型的 Type 实例）阻止 ALC 回收；
/// 改为 ConditionalWeakTable 双层（外层 Source 弱键 / 内层 Target 弱键）——ALC 卸载后条目随 GC 失效。
/// 覆盖语义经 <see cref="ConditionalWeakTable{TKey, TValue}.AddOrUpdate"/> 保持（等价原索引器覆盖写）；
/// 清理改 <see cref="ConditionalWeakTable{TKey, TValue}.Clear"/>（net8+；字段保持 readonly，无实例换新）。</para>
/// <para>调试可观测性计数器（v1.3 增补）：注册数 / Resolve 命中数 / 兜底触发数——
/// Q5 时序 PoC 4 项测试以计数断言精确锁定时序。</para>
/// </summary>
internal static class MapperDelegateRegistry
{
    // 双层 CWT 弱键（外层 Source / 内层 Target）；readonly 保持，清理走 Clear()。
    private static readonly ConditionalWeakTable<Type, ConditionalWeakTable<Type, IMapperBaseDelegate>> s_registry = new();

    /// <summary>注册数计数（Interlocked 累加）</summary>
    private static long s_registerCount;
    /// <summary>Resolve 字典命中计数（含兜底后重试命中；Interlocked 累加）</summary>
    private static long s_resolveHitCount;
    /// <summary>RunClassConstructor 兜底触发计数（Interlocked 累加）</summary>
    private static long s_bootstrapCount;

    /// <summary>
    /// 注册数计数器 — 每次 Register 调用 +1（Q5 测试①断言依据；测试经 InternalsVisibleTo 读取）。
    /// </summary>
    internal static long RegisterCount => s_registerCount;

    /// <summary>
    /// Resolve 命中数计数器 — 字典命中路径 +1（含兜底后重试命中）。
    /// </summary>
    internal static long ResolveHitCount => s_resolveHitCount;

    /// <summary>
    /// 兜底触发数计数器 — RunClassConstructor 兜底路径执行 +1（Q5 测试②③断言依据）。
    /// </summary>
    internal static long BootstrapCount => s_bootstrapCount;

    /// <summary>
    /// 注册委托实现（由 MapperBase&lt;TSource,TTarget&gt; 显式静态构造函数首行调用，v1.1）。
    /// <para>双层 CWT 写入——外层 GetValue（不存在则建内层表）+
    /// 内层 <see cref="ConditionalWeakTable{TKey, TValue}.AddOrUpdate"/>（覆盖语义与原索引器一致）。</para>
    /// </summary>
    public static void Register<TSource, TTarget>(IMapperBaseDelegate impl)
    {
        Interlocked.Increment(ref s_registerCount);
        s_registry.GetValue(typeof(TSource), static _ => new())
            .AddOrUpdate(typeof(TTarget), impl);
    }

    /// <summary>
    /// 清空注册中心（测试支持，Q5 PoC 测试④“清空后兜底重注册”需要）。
    /// <para>CWT <see cref="ConditionalWeakTable{TKey, TValue}.Clear"/>（net8+；
    /// 字段保持 readonly，无实例换新。</para>
    /// <para>仅测试使用：.NET 类型初始化仅一次（.cctor 不重跑），清空后<b>已初始化</b>类型对的
    /// Resolve 将兜底未命中抛 <see cref="MapConfigurationException"/>——该边界行为正是
    /// 测试④幂等性验证对象；未初始化类型对仍可经兜底正常注册。</para>
    /// </summary>
    internal static void Clear() => s_registry.Clear();

    /// <summary>
    /// 查询委托实现（由 ExpressionBuilder 在 3 处反射回指点按实际类型对调用）。
    /// <para>v1.1 兜底时序：未命中时 RunClassConstructor 触发目标类型对
    /// 静态构造（Register 前置于其中）后重试一次；仍未命中抛 <see cref="MapConfigurationException"/>
    /// （该类型对无 MapperBase 泛型实例——理论上仅发生在 MapperBase 类型初始化异常场景，
    /// 由 F1 证伪条件覆盖）。v1.0“理论不可达，直接返回 null”设计已废弃——
    /// 嵌套/集合元素类型对在编译期未被触及，Resolve 必然未命中。</para>
    /// </summary>
    // Resolve 补双标注（MakeGenericType 是本期唯一新增反射点（3 处旧反射的集中
    // 替代落点），未标注则 trim 分析产 IL2055 新增警告，与 / “迁移后无新增警告”验收矛盾）
    [RequiresDynamicCode("Resolve uses MakeGenericType to construct MapperBase<TSource,TTarget> closed generic. Not supported in AOT.")]
    [RequiresUnreferencedCode("Resolve triggers MapperBase static constructor which scans type members via reflection. Some members may be trimmed.")]
    public static IMapperBaseDelegate Resolve(Type sourceType, Type targetType)
    {
        if (TryGet(sourceType, targetType, out var impl))
        {
            Interlocked.Increment(ref s_resolveHitCount);
            return impl;
        }
        // 兜底：触发 MapperBase<TSource,TTarget> 静态构造（Register 前置于其中），随后重试
        Interlocked.Increment(ref s_bootstrapCount);
        RuntimeHelpers.RunClassConstructor(typeof(MapperBase<,>).MakeGenericType(sourceType, targetType).TypeHandle);
        if (TryGet(sourceType, targetType, out impl))
        {
            Interlocked.Increment(ref s_resolveHitCount);
            return impl;
        }
        // v1.2：统一走 ErrorMessages 双语消息（/ - ——v1.1 骨架抛硬编码英文消息，
        // 与 / 的 MapperDelegateNotRegistered 双语机制口径矛盾）
        throw new MapConfigurationException(
            ErrorMessages.MapperDelegateNotRegistered(sourceType.Name, targetType.Name));
    }

    /// <summary>
    /// 双层 CWT 读路径：外层 TryGetValue（未命中不建内层表——无副作用）+
    /// 内层 TryGetValue；语义与原元组键 TryGetValue 等价。
    /// </summary>
    private static bool TryGet(Type sourceType, Type targetType, [NotNullWhen(true)] out IMapperBaseDelegate? impl)
    {
        impl = null;
        return s_registry.TryGetValue(sourceType, out var inner)
            && inner.TryGetValue(targetType, out impl);
    }
}
