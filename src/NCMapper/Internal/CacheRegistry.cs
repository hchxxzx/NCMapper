using System.Collections.Concurrent;

namespace NCMapper.Internal;

/// <summary>
/// 缓存生命周期注册中心 —— 统一登记各缓存模块的清理委托，ClearGenerated/ClearCache 由登记表驱动。
/// 仿 MapperDelegateRegistry（W2-1 ④）结构：ConcurrentDictionary + 遥测计数器。
/// </summary>
internal static class CacheRegistry
{
    /// <summary>
    /// 登记表 —— 注册名 → (层级标签, 清理委托)。ConcurrentDictionary 保证原子写；遍历时 ToArray 显式快照。
    /// </summary>
    private static readonly ConcurrentDictionary<string, (CacheLayer Layer, Action Clear)> s_registry = new();

    /// <summary>
    /// 注册次数计数（Interlocked 累加；ResetCounters 归零）。
    /// </summary>
    private static long s_registerCount;

    /// <summary>
    /// 清理执行次数计数（Interlocked 累加；每次 Clear* 入口 +1，非每条目；ResetCounters 归零）。
    /// </summary>
    private static long s_clearCount;

    /// <summary>
    /// 清理委托异常计数（Interlocked 累加；ResetCounters 归零）
    /// N-05 try 隔离下被吞的委托异常的观测面（此前仅 System.Diagnostics.Trace 无计数）。
    /// </summary>
    private static long s_clearFailureCount;

    /// <summary>
    /// 注册清理委托（幂等覆盖：同名重复注册覆盖，无累加）。
    /// 由各缓存模块静态构造自注册；静态构造仅注册，不触发自身缓存构建。
    /// </summary>
    /// <param name="name">注册名（调试/遥测/测试断言用，kebab-case）</param>
    /// <param name="layer">层级标签：Generated（ClearGenerated 清）/ Full（ClearCache 清）</param>
    /// <param name="clear">清理委托（幂等——重复执行不得抛异常）</param>
    internal static void Register(string name, CacheLayer layer, Action clear)
    {
        s_registry[name] = (layer, clear);
        Interlocked.Increment(ref s_registerCount);
    }

    /// <summary>
    /// 注销清理委托（测试隔离用——按名移除登记条目；不存在时 no-op）。
    /// <para>B2 门禁复验：红测以独有名注册的临时条目须
    /// finally 注销——原无移除出口时条目残留进程全程，污染 CacheRegistryTests.Snapshot
    /// 全等守卫（实测 23→25 必红，G2 新增失败）。</para>
    /// </summary>
    /// <param name="name">注册名</param>
    internal static void Unregister(string name) => s_registry.TryRemove(name, out _);

    /// <summary>
    /// 清空 Generated 层（源生成器委托字典，现 ClearGenerated 语义；只遍历 Layer == Generated 的条目）。
    /// </summary>
    internal static void ClearGenerated() => ClearCore(CacheLayer.Generated);

    /// <summary>
    /// 清空全部层（现 ClearCache 语义；Generated ⊆ Full 子集关系由登记表直接表达， 定案）。
    /// </summary>
    internal static void ClearFull() => ClearCore(null);

    /// <summary>
    /// 按层过滤遍历执行清理委托（显式快照：遍历期间并发 Register 安全）。
    /// <para>单条清理委托 try 隔离——一条违约
    /// 不再阻断其余条目清理、计数不丢失（当前委托均不抛，防御性口径）。</para>
    /// </summary>
    /// <param name="layer">层过滤条件；null 表示全量遍历（ClearFull 语义）</param>
    private static void ClearCore(CacheLayer? layer)
    {
        var items = s_registry.ToArray();
        foreach (var (name, (entryLayer, clear)) in items)
        {
            if (layer is null || entryLayer == layer)
            {
                try { clear(); } // 单条隔离
                catch (Exception ex)
                {
                    // 异常计数 + MapTracer 结构化留痕
                    //（原仅 System.Diagnostics.Trace——GloballyEnabled 通道零观测面）
                    Interlocked.Increment(ref s_clearFailureCount);
                    MapTracer.Write($"[NCMapper] CacheRegistry: 清理委托 '{name}'（{entryLayer} 层）异常，已隔离继续其余条目: {ex.GetType().Name}: {ex.Message}");
                    System.Diagnostics.Trace.WriteLine($"[NCMapper] CacheRegistry: 清理委托异常（已隔离，继续其余条目）: {ex.Message}");
                }
            }
        }
        Interlocked.Increment(ref s_clearCount);
    }

    /// <summary>
    /// 登记表快照（名称 + Layer 列表）——测试断言"无漏登记"（新增缓存未登记 = 快照集合与基准不符 = 必红）。
    /// </summary>
    /// <returns>登记条目的名称与层级标签列表（顺序不保证，测试断言按集合比较）</returns>
    internal static IReadOnlyList<(string Name, CacheLayer Layer)> Snapshot()
        => s_registry.Select(kv => (kv.Key, kv.Value.Layer)).ToList();

    /// <summary>
    /// 注册次数计数（Interlocked 累加；测试经 InternalsVisibleTo 读取断言）。
    /// </summary>
    internal static long RegisterCount => s_registerCount;

    /// <summary>
    /// 清理执行次数计数（Interlocked 累加；每次 Clear* 入口 +1，非每条目）。
    /// </summary>
    internal static long ClearCount => s_clearCount;

    /// <summary>
    /// 清理委托异常计数——N-05 隔离吞掉的异常次数
    ///（测试经 InternalsVisibleTo 读取断言）。
    /// </summary>
    internal static long ClearFailureCount => s_clearFailureCount;

    /// <summary>
    /// 重置遥测计数器（供 Mapper.ResetMetrics 级联调用；Interlocked 原子归零）。
    /// </summary>
    internal static void ResetCounters()
    {
        Interlocked.Exchange(ref s_registerCount, 0);
        Interlocked.Exchange(ref s_clearCount, 0);
        Interlocked.Exchange(ref s_clearFailureCount, 0);
    }
}

/// <summary>
/// 缓存层级标签 —— ClearGenerated ⊆ ClearCache 子集关系由 Layer 表达。
/// </summary>
internal enum CacheLayer
{
    /// <summary>
    /// 源生成器委托字典（ClearGenerated 清理）
    /// </summary>
    Generated,

    /// <summary>
    /// 全部缓存（ClearCache 清理；含 Generated 层）
    /// </summary>
    Full,
}
