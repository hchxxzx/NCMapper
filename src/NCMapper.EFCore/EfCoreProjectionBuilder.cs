// NCMapper EF Core 扩展包 —— 投影表达式构建器（薄壳）
// 共享内核 ProjectionExpressionFactory + 策略声明
// 提取前行为由 T-A 双端一致性矩阵锁定；本文件仅保留策略常量 + ORM 入口。
using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;
using NCMapper.Internal;

namespace NCMapper.EFCore;

/// <summary>
/// EF Core 投影表达式构建器（薄壳）—— 声明 EF Core 投影策略并委托共享内核构建
/// <para>策略：深度 8 / 警告 5 / 集合投影开启（EF Core SQL 翻译能力强）</para>
/// </summary>
internal static class EfCoreProjectionBuilder
{
    /// <summary>
    /// 投影策略（EFCore 深度上限 8）
    /// </summary>
    private static readonly ProjectionStrategy Strategy = new(
        MaxNestingDepth: 8,
        WarningDepth: 5,
        EnableCollectionProjection: true,
        TracePrefix: "[NCMapper.EFCore] Projection:");

    /// <summary>
    /// 静态构造 —— 向 Mapper 注册投影缓存清理委托（ClearCache 链接口；
    /// 核心库不引用扩展，经委托反向注册）
    /// </summary>
    static EfCoreProjectionBuilder()
    {
        Mapper.RegisterProjectionCacheClear(ClearCache);
    }

    /// <summary>
    /// 构建 Projection 表达式 —— 用于 EF Core 查询时直接映射为 DTO，一条 SQL 完成查询
    /// <para><b>Null 传播</b>：导航属性访问自动生成 null 安全表达式，ORM 翻译为 LEFT JOIN + COALESCE</para>
    /// </summary>
    [RequiresUnreferencedCode("Build scans type members and attributes via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("Build builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    internal static Expression<Func<TSource, TTarget>> Build<TSource, TTarget>()
        where TSource : class
        where TTarget : class
        => ProjectionExpressionFactory.Build<TSource, TTarget>(Strategy);

    /// <summary>
    /// 清除表达式缓存 —— 供测试隔离调用（委托共享内核）
    /// </summary>
    internal static void ClearCache()
        => ProjectionExpressionFactory.ClearCache();
}
