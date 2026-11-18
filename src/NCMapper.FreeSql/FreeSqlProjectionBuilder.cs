// NCMapper FreeSql 扩展包 —— 投影表达式构建器（薄壳）
// 共享内核 ProjectionExpressionFactory + 策略声明
// ⑦: Compose 2/3 表构建逻辑收敛至 ComposeProjectionKernel 元数无关内核，
// 本文件保留强类型门面（缓存包装器 BuildCompose/BuildCompose3 + 薄壳 Core 转发内核）。
// 单表投影走共享内核；提取前行为由 T-A 双端一致性矩阵锁定。
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using NCMapper.Attributes;
using System.Diagnostics.CodeAnalysis;
using NCMapper.Internal;
using NCMapper.FreeSql.Internal;
using NCMapper.Runtime;

namespace NCMapper.FreeSql;

/// <summary>
/// FreeSql 投影表达式构建器（薄壳）—— 声明 FreeSql 投影策略并委托共享内核构建
/// <para>策略：深度 3 / 警告 2 / 集合投影关闭（FreeSql 翻译能力有限，保守策略）</para>
/// </summary>
internal static class FreeSqlProjectionBuilder
{
    // CacheRegistry 登记制自注册（补漏 3）：仅登记清理委托，不触发缓存构建（防自触发）
    static FreeSqlProjectionBuilder()
    {
        CacheRegistry.Register("free-sql-projection", CacheLayer.Full, ClearCache);
    }

    /// <summary>
    /// 投影策略（FreeSql 深度上限 3）
    /// </summary>
    private static readonly ProjectionStrategy Strategy = new(
        MaxNestingDepth: 3,
        WarningDepth: 2,
        EnableCollectionProjection: false,
        TracePrefix: "[NCMapper.FreeSql] Projection:");

    /// <summary>
    /// Compose 表达式缓存版本化条目（归档补丁—— ① 用户裁定"建议 1"处置：
    /// 与主链 ProjectionExpressionFactory.ProjectionCacheEntry 同构——命中条件扩为
    /// <c>Version == RuntimeSnapshotStore.ContentVersion</c>（注册表内容版本失配重算），写路径改
    /// 索引器（替换旧条目）——消除"运行时声明变更后同对双表/三表门面陈旧表达式续用"的一致性缺口）。
    /// </summary>
    private sealed class ComposeCacheEntry
    {
        public long Version;
        public required LambdaExpression Lambda;
    }

    /// <summary>
    /// 双表 Compose 表达式缓存（键 = (T1, T2, Target)；单表投影不缓存）
    /// </summary>
    private static readonly ConcurrentDictionary<(Type T1, Type T2, Type Target), ComposeCacheEntry> s_composeCache = new();

    /// <summary>
    /// 三表 Compose Projection 表达式缓存
    /// </summary>
    private static readonly ConcurrentDictionary<(Type T1, Type T2, Type T3, Type Target), ComposeCacheEntry> s_compose3Cache = new();

    /// <summary>
    /// 构建 Projection 表达式（单表）—— 委托共享内核
    /// </summary>
    [RequiresUnreferencedCode("Build scans type members and attributes via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("Build builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    internal static Expression<Func<TSource, TTarget>> Build<TSource, TTarget>()
        where TSource : class
        where TTarget : class
        => ProjectionExpressionFactory.Build<TSource, TTarget>(Strategy);

    /// <summary>
    /// 构建双表 Compose Projection 表达式（缓存包装器，薄壳保留）
    /// </summary>
    [RequiresUnreferencedCode("BuildCompose scans type members and attributes via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("BuildCompose builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    internal static Expression<Func<T1, T2, TTarget>> BuildCompose<T1, T2, TTarget>()
        where T1 : class
        where T2 : class
        where TTarget : class
    {
        var cacheKey = (typeof(T1), typeof(T2), typeof(TTarget));
        // 归档补丁：条目内版本校验（ContentVersion 失配重算）+ 索引器写路径（替换旧条目）
        // 与 ProjectionExpressionFactory.Build 同款时序（入口捕获版本，重算写回同版本）
        var contentVersion = RuntimeSnapshotStore.ContentVersion;
        if (s_composeCache.TryGetValue(cacheKey, out var cached) && cached.Version == contentVersion)
            return (Expression<Func<T1, T2, TTarget>>)cached.Lambda;

        var lambda = BuildComposeCore<T1, T2, TTarget>();
        s_composeCache[cacheKey] = new ComposeCacheEntry { Version = contentVersion, Lambda = lambda };
        return lambda;
    }

    /// <summary>
    /// 构建三表 Compose Projection 表达式（缓存包装器，薄壳保留）
    /// </summary>
    [RequiresUnreferencedCode("BuildCompose3 scans type members and attributes via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("BuildCompose3 builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    internal static Expression<Func<T1, T2, T3, TTarget>> BuildCompose3<T1, T2, T3, TTarget>()
        where T1 : class
        where T2 : class
        where T3 : class
        where TTarget : class
    {
        var cacheKey = (typeof(T1), typeof(T2), typeof(T3), typeof(TTarget));
        // 归档补丁：三表缓存同款版本门控（与双表/主链一致）
        var contentVersion = RuntimeSnapshotStore.ContentVersion;
        if (s_compose3Cache.TryGetValue(cacheKey, out var cached) && cached.Version == contentVersion)
            return (Expression<Func<T1, T2, T3, TTarget>>)cached.Lambda;

        var lambda = BuildCompose3Core<T1, T2, T3, TTarget>();
        s_compose3Cache[cacheKey] = new ComposeCacheEntry { Version = contentVersion, Lambda = lambda };
        return lambda;
    }

    /// <summary>
    /// 清除表达式缓存 —— 供测试隔离调用（Compose 缓存 + 共享内核缓存）
    /// </summary>
    internal static void ClearCache()
    {
        s_composeCache.Clear();
        s_compose3Cache.Clear();
        ProjectionExpressionFactory.ClearCache();
    }

    // ================================================================
    // Compose 构建（薄壳门面；构建逻辑委托 ComposeProjectionKernel 元数无关内核，W0-1 ⑦）
    // ================================================================

    [RequiresUnreferencedCode("BuildComposeCore forwards to ComposeProjectionKernel. Some members may be trimmed.")]
    [RequiresDynamicCode("BuildComposeCore forwards to ComposeProjectionKernel. Not supported in AOT.")]
    private static Expression<Func<T1, T2, TTarget>> BuildComposeCore<T1, T2, TTarget>()
        where T1 : class
        where T2 : class
        where TTarget : class
    {
        // 薄壳门面：类型安全转发内核（Lambda 由内核以 GetDelegateType 构造，强转恒成立）
        var lambda = ComposeProjectionKernel.BuildComposeCoreN(
            new[] { typeof(T1), typeof(T2) }, typeof(TTarget), Strategy);
        return (Expression<Func<T1, T2, TTarget>>)lambda;
    }

    [RequiresUnreferencedCode("BuildCompose3Core forwards to ComposeProjectionKernel. Some members may be trimmed.")]
    [RequiresDynamicCode("BuildCompose3Core forwards to ComposeProjectionKernel. Not supported in AOT.")]
    private static Expression<Func<T1, T2, T3, TTarget>> BuildCompose3Core<T1, T2, T3, TTarget>()
        where T1 : class
        where T2 : class
        where T3 : class
        where TTarget : class
    {
        // 薄壳门面：类型安全转发内核（Lambda 由内核以 GetDelegateType 构造，强转恒成立）
        var lambda = ComposeProjectionKernel.BuildComposeCoreN(
            new[] { typeof(T1), typeof(T2), typeof(T3) }, typeof(TTarget), Strategy);
        return (Expression<Func<T1, T2, T3, TTarget>>)lambda;
    }
}
