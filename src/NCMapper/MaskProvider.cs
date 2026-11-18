using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using NCMapper.Internal;
namespace NCMapper;

/// <summary>
/// 脱敏实现注册表 — 允许在应用启动时替换内置 IMask 实现，无需修改 DTO 特性标注。
/// <para>用法：MaskProvider.Override&lt;MaskPhone&gt;(new JapanPhoneMask());</para>
/// <para>约束：Override 须在该 Mask 类型首次被映射调用之前执行（静态字段初始化后不再重新解析）</para>
/// </summary>
public static class MaskProvider
{
    // CacheRegistry 登记制自注册：仅登记清理委托，不触发缓存构建（防自触发）
    // 登记的清理委托只清运行时实例缓存——用户
    // Override 注册（编译期资产语义）移出 Full 清理层，Mapper.ClearCache 不再连带清空
    //（原形态 PII 静默回退内置脱敏）；显式 MaskProvider.Clear() 仍全清（独立生命周期出口）
    static MaskProvider()
    {
        CacheRegistry.Register("mask-provider", CacheLayer.Full, ClearInstanceCache);
    }

    /// <summary>
    /// 掩码类型覆盖注册表（Override 映射，按类型）
    /// </summary>
    private static readonly ConcurrentDictionary<Type, IMask> _overrides = new();

    /// <summary>
    /// Mask 实例缓存
    /// <para>Resolve(Type) 运行时路径按类型缓存实例，消除每次映射 Activator.CreateInstance
    /// 重建（连续两次 Map 仅一次实例化）。Override 优先（_overrides 先查），Override
    /// 变更时同步覆写本缓存（Override 后效语义保持）。线程安全：IMask 实现须无状态
    /// （内置 MaskPhone 等均为纯函数实现；有状态自定义 Mask 应保持 Override 单例语义不变）。</para>
    /// <para>CacheRegistry 登记制（mask-provider，Clear 覆盖）。</para>
    /// </summary>
    private static readonly ConcurrentDictionary<Type, IMask> _instanceCache = new();

    /// <summary>
    /// 替换指定 Mask 类型的实际实现。
    /// <para>例：MaskProvider.Override&lt;MaskPhone&gt;(new MyPhoneMask());</para>
    /// </summary>
    /// <typeparam name="TMask">特性中声明的 Mask 类型（作为 key）</typeparam>
    /// <param name="replacement">实际使用的脱敏实现</param>
    public static void Override<TMask>(IMask replacement) where TMask : IMask
    {
        ArgumentNullException.ThrowIfNull(replacement);
        _overrides[typeof(TMask)] = replacement;
        _instanceCache[typeof(TMask)] = replacement; // 实例缓存同步覆写（Override 后效保持）
    }

    /// <summary>
    /// 解析指定 Mask 类型的实际实现 — 有 Override 用 Override，否则 new TMask()。
    /// <para>由源生成器在静态字段初始化时调用，每次映射不再经过此方法。</para>
    /// </summary>
    /// <returns>实际使用的脱敏实现实例</returns>
    public static IMask Resolve<TMask>() where TMask : IMask, new()
        => _overrides.TryGetValue(typeof(TMask), out var mask) ? mask : new TMask();

    /// <summary>
    /// 非泛型解析 — 运行时表达式树路径使用。有 Override 用 Override，否则 Activator.CreateInstance。
    /// <para>行为与 <see cref="Resolve{TMask}"/> 一致，保证 Override 机制在运行时路径同样生效。</para>
    /// </summary>
    /// <param name="maskType">脱敏实现类型，必须实现 <see cref="IMask"/> 接口且有无参构造器</param>
    /// <returns>实际使用的脱敏实现实例</returns>
    /// <exception cref="ArgumentNullException"><paramref name="maskType"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException"><paramref name="maskType"/> 无参构造器创建实例失败（如无无参构造器）。</exception>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Resolve(Type) uses Activator.CreateInstance to instantiate the mask type at runtime. Not supported in AOT. Use the generic Resolve<TMask>() overload, which the source-generated path calls.")]
    [RequiresUnreferencedCode("Resolve scans type members and attributes via reflection. Some members may be trimmed.")]
    public static IMask Resolve(Type maskType)
    {
        ArgumentNullException.ThrowIfNull(maskType);
        if (_overrides.TryGetValue(maskType, out var mask))
            return mask;
        // 按类型缓存实例——连续两次 Map 仅一次 Activator 实例化
        return _instanceCache.GetOrAdd(maskType, static t => (IMask)(Activator.CreateInstance(t)
            ?? throw new InvalidOperationException(ErrorMessages.MaskCreateInstanceFailed(t.FullName ?? t.Name))));
    }

    /// <summary>
    /// 清除所有 Override 注册与运行时实例缓存（测试用/显式全清出口）
    /// <para>行为变更：<see cref="Mapper.ClearCache"/>
    /// <b>不再</b>连带清空 Override 注册（只清运行时实例缓存）——用户注册的 Override
    /// 掩码具有独立生命周期，须显式调用本方法清除。</para>
    /// <para>注意：已初始化的生成代码静态字段不会自动重置，需配合重新触发生成或重启测试进程。</para>
    /// </summary>
    public static void Clear()
    {
        _overrides.Clear();
        _instanceCache.Clear();
    }

    /// <summary>
    /// 仅清运行时实例缓存（CacheRegistry mask-provider 登记委托）——Override
    /// 注册不在清理面（Override 命中先于实例缓存，清理实例缓存不影响 Override 语义）
    /// </summary>
    private static void ClearInstanceCache() => _instanceCache.Clear();
}
