using System.Threading;

namespace NCMapper;

/// <summary>
/// 缓存级命中/未命中观测计数器
/// <para>两级分组：①聚合总数（TotalHits/TotalMisses）；②按缓存类分组
/// （<see cref="CacheKind"/> 三值，HitsByKind/MissesByKind 定长数组，无键空间增长）。
/// 驱动开关复用 <see cref="Mapper.MetricsEnabled"/>（volatile 读，与 Mapper 入口级 3 计数同源）；
/// MetricsEnabled 关闭时热路径仅多一次 volatile 读 + 分支（W3E-B 基线对照验证零退化）。</para>
/// <para>接入范围（本波次治理面）：PropertyMatcher 三缓存——s_propertiesCache（属性字典）、
/// s_mapFromAttrCache（[MapFrom] 特性）、s_normalizedKeysCache（归一化键，非默认配置路径）。
/// 计数语义弱一致（与 ClearCache 契约同族）：并发交错时计数允许偏差，观测用途可接受。</para>
/// </summary>
internal static class CacheMetrics
{
    /// <summary>
    /// 参与观测的缓存类（索引即数组下标，新增缓存类时同步扩展 Hit/Miss 定长数组）
    /// </summary>
    internal enum CacheKind
    {
        /// <summary>
        /// 属性字典缓存（s_propertiesCache）
        /// </summary>
        Properties = 0,

        /// <summary>
        /// [MapFrom] 特性缓存（s_mapFromAttrCache）
        /// </summary>
        MapFromAttr = 1,

        /// <summary>
        /// 归一化键缓存（s_normalizedKeysCache，非默认命名配置路径）
        /// </summary>
        NormalizedKeys = 2
    }

    /// <summary>
    /// 参与观测的缓存类总数（定长数组长度）
    /// </summary>
    private const int KindCount = 3;

    /// <summary>
    /// 聚合命中计数 — 全部缓存类命中累计（仅 MetricsEnabled 时 Interlocked 递增）
    /// </summary>
    private static long s_totalHits;

    /// <summary>
    /// 聚合未命中计数 — 全部缓存类未命中累计（仅 MetricsEnabled 时 Interlocked 递增）
    /// </summary>
    private static long s_totalMisses;

    /// <summary>
    /// 按缓存类分组命中计数（下标 = (int)CacheKind）
    /// </summary>
    private static readonly long[] s_hitsByKind = new long[KindCount];

    /// <summary>
    /// 按缓存类分组未命中计数（下标 = (int)CacheKind）
    /// </summary>
    private static readonly long[] s_missesByKind = new long[KindCount];

    /// <summary>
    /// 观测开关 — 直读 <see cref="Mapper.MetricsEnabled"/>（volatile），保证单一事实来源
    /// </summary>
    internal static bool IsEnabled => Mapper.MetricsEnabled;

    /// <summary>
    /// 记录一次缓存命中（聚合 + 分组两级递增）
    /// <para>调用方保证仅 IsEnabled 为 true 时调用（热路径零开销契约）。</para>
    /// </summary>
    /// <param name="kind">命中的缓存类</param>
    internal static void RecordHit(CacheKind kind)
    {
        Interlocked.Increment(ref s_totalHits);
        Interlocked.Increment(ref s_hitsByKind[(int)kind]);
    }

    /// <summary>
    /// 记录一次缓存未命中（聚合 + 分组两级递增）
    /// <para>调用方保证仅 IsEnabled 为 true 时调用（热路径零开销契约）。</para>
    /// </summary>
    /// <param name="kind">未命中的缓存类</param>
    internal static void RecordMiss(CacheKind kind)
    {
        Interlocked.Increment(ref s_totalMisses);
        Interlocked.Increment(ref s_missesByKind[(int)kind]);
    }

    /// <summary>
    /// 聚合命中计数（只读观测 — Volatile.Read 弱一致快照）
    /// </summary>
    internal static long TotalHits => Volatile.Read(ref s_totalHits);

    /// <summary>
    /// 聚合未命中计数（只读观测 — Volatile.Read 弱一致快照）
    /// </summary>
    internal static long TotalMisses => Volatile.Read(ref s_totalMisses);

    /// <summary>
    /// 按缓存类分组命中计数（只读视图，下标 = (int)CacheKind；不复制，观测弱一致）
    /// </summary>
    internal static IReadOnlyList<long> HitsByKind => s_hitsByKind;

    /// <summary>
    /// 按缓存类分组未命中计数（只读视图，下标 = (int)CacheKind；不复制，观测弱一致）
    /// </summary>
    internal static IReadOnlyList<long> MissesByKind => s_missesByKind;

    /// <summary>
    /// 重置全部计数（聚合 + 分组归零）— 由 <see cref="Mapper.ResetMetrics"/> 级联调用
    /// </summary>
    internal static void Reset()
    {
        Interlocked.Exchange(ref s_totalHits, 0);
        Interlocked.Exchange(ref s_totalMisses, 0);
        for (var i = 0; i < KindCount; i++)
        {
            Interlocked.Exchange(ref s_hitsByKind[i], 0);
            Interlocked.Exchange(ref s_missesByKind[i], 0);
        }
    }
}
