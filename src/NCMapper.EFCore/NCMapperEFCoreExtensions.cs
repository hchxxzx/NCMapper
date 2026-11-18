// NCMapper EF Core 扩展包 — AsDto 终端操作 + 自动降级
// 用法：context.Orders.Where(...).AsDto().ToList<OrderDto>()
// 依赖：NCMapper + Microsoft.EntityFrameworkCore
// 设计：不实现 ProjectTo（投影后组合无需求场景），AsDto 为唯一终端入口

using NCMapper.Runtime;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Internal;

namespace NCMapper.EFCore;

/// <summary>
/// NCMapper EF Core 查询扩展 — 将 IQueryable 结果映射为 DTO，自动检测是否需要降级
/// <para>正常路径：SQL 侧投影（EfCoreProjectionBuilder → query.Select(expr)）</para>
/// <para>降级路径：SQL 查全量实体（含自动 Include）→ 内存 MapList</para>
/// <para>多表场景：在 AsDto 之前使用 Include() 加载导航属性，降级路径自动处理嵌套映射</para>
/// </summary>
public static class NCMapperEFCoreExtensions
{
    /// <summary>C-13 树挂载位访问器缓存 — key: (TTarget, PropertyInfo)，value: 编译 getter/setter。</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Target, System.Reflection.PropertyInfo Prop), (Func<object, object?> Getter, Action<object> Setter)> s_treeSlotAccessors = new();

    static NCMapperEFCoreExtensions()
    {
        // C-17 登记制（对齐 CollectionCopier 先例）：ClearCache 链回收树挂载位委托缓存
        NCMapper.Internal.CacheRegistry.Register("efcore-tree-slot-accessors", NCMapper.Internal.CacheLayer.Full,
            static () =>
            {
                foreach (var kvp in s_treeSlotAccessors) s_treeSlotAccessors.TryRemove(kvp.Key, out _);
            });
    }

    /// <summary>
    /// EF Core AsDto 投影查询是否默认启用 AsNoTracking — 默认 true（自 NCMapperOptions 移入 EFCore 扩展）
    /// <para>启用后，EFCoreDtoSelector 的所有终端方法（ToList/First/ToPagedList）均追加 AsNoTracking()，
    /// 投影和降级路径均不产生变更跟踪实体。跟踪开关在 AsDto() 返回的 selector 上设置（query.AsDto().AsTracking()）</para>
    /// <para>设为 false 可全局关闭此行为（如某些场景需要变更跟踪）</para>
    /// </summary>
    public static bool EFCoreProjectionAsNoTrackingDefault { get; set; } = true;

    /// <summary>
    /// 返回 DTO 选择器 — AsDto 是终端操作的入口，所有 Where/OrderBy/Include/Skip/Take 应在 AsDto 之前完成
    /// <para>用法：context.Users.Where(u => u.Age > 18).AsDto().ToList&lt;UserDto&gt;()</para>
    /// </summary>
    /// <param name="query">EF Core 查询对象</param>
    /// <returns>DTO 选择器，用于后续调用 ToList/First/ToPagedList 等终端方法</returns>
    [RequiresDynamicCode("AsDto uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("AsDto scans attributes via reflection. Some types may be trimmed.")]
    public static EFCoreDtoSelector<TSource> AsDto<TSource>(this IQueryable<TSource> query)
        where TSource : class
        => new(query);
}

/// <summary>
/// EF Core DTO 选择器 — 终端方法内部自动判断投影/降级路径
/// <para>Single 族「恰一条」判定：<c>Take(2)</c> ＋ 本地 <see cref="AssertSingle{T}"/> 判定，
/// 消息单源 = <see cref="ErrorMessages.SequenceNoElements"/>／<see cref="ErrorMessages.SequenceMultipleElements"/>。
/// 判定为**双副本**承载——EFCore 与 FreeSql 各一份同构私有 helper（FreeSql 侧为
/// <c>NCMapperFreeSqlExtensions.cs</c> 内 <c>AssertSingle</c>），两侧须逐字同形；判据文本不得各自措辞。</para>
/// </summary>
/// <typeparam name="TSource">源实体类型（从 IQueryable 推断）</typeparam>
public sealed class EFCoreDtoSelector<TSource>
    where TSource : class
{
    /// <summary>
    /// EF Core 查询对象（终端方法内部使用）
    /// </summary>
    private readonly IQueryable<TSource> _query;

    // 用户显式请求变更跟踪的标志（AsTracking() 设置）——EF Core 表达式层无法区分
    // 用户显式 AsTracking() 与默认跟踪（两者 QueryTrackingBehavior 均为 TrackAll），
    // 故提供 selector 级显式声明，NoTrackQuery 据此跳过 AsNoTracking
    /// <summary>
    /// 显式变更跟踪标志（AsTracking() 设置）
    /// </summary>
    private bool _tracking;

    /// <summary>
    /// EFCoreDTO选择器。
    /// </summary>
    /// <param name="query">指定查询。</param>
    internal EFCoreDtoSelector(IQueryable<TSource> query) => _query = query;
    /// <summary>
    /// 降级判定 + 原因 Trace— 需降级时输出原因并返回 true；Trace 文案与逐处手写保持一致
    /// <para>骨架收敛至 DtoSelectorSkeleton 单点</para>
    /// </summary>
    /// <param name="sourceType">源实体类型</param>
    /// <param name="targetType">目标 DTO 类型</param>
    /// <param name="actionLabel">降级动作标签（如 "ToPagedList"）</param>
    /// <returns>需要降级时为 true（并已输出 Trace 原因）</returns>
    [RequiresUnreferencedCode("NeedsDegradationWithTrace scans type members and attributes via reflection in the degradation detector. Some members may be trimmed.")]
    internal static bool NeedsDegradationWithTrace(Type sourceType, Type targetType, string actionLabel)
        => DtoSelectorSkeleton.NeedsDegradationWithTrace(
            "[EFCore AsDto]", sourceType, targetType, actionLabel,
            EfCoreDegradationDetector.NeedsDegradation);

    /// <summary>
    /// 显式请求变更跟踪 — 覆盖默认 AsNoTracking（默认关闭跟踪）
    /// <para>用法：<c>query.AsDto().AsTracking().ToList&lt;TDto&gt;()</c>——投影与降级路径均保留变更跟踪。</para>
    /// <para>背景：EF Core 表达式层无法区分用户显式 <c>AsTracking()</c> 与默认跟踪（两者
    /// QueryTrackingBehavior 均为 TrackAll），旧文档承诺"AsDto 前显式调用 AsTracking() 覆盖"
    /// 实际失效（无条件 AsNoTracking 会覆盖）；本方法为可靠的显式声明入口。</para>
    /// </summary>
    /// <returns>支持变更跟踪的 DTO 选择器</returns>
    public EFCoreDtoSelector<TSource> AsTracking()
    {
        _tracking = true;
        return this;
    }

    /// <summary>
    /// 获取应用了 AsNoTracking 的查询 — 受 NCMapperEFCoreExtensions.EFCoreProjectionAsNoTrackingDefault 控制
    /// <para>默认开启（true），投影和降级路径均不产生变更跟踪实体</para>
    /// <para>如需变更跟踪：在 AsDto 后调用 <see cref="AsTracking"/> 显式声明。
    /// 旧文档"AsDto 前显式调用 AsTracking() 覆盖"因 EF Core 表达式层无法区分而失效，已修正）</para>
    /// </summary>
    private IQueryable<TSource> NoTrackQuery()
    {
        if (!NCMapperEFCoreExtensions.EFCoreProjectionAsNoTrackingDefault) return _query; // H-07⑧：属性已移入本类
        if (_tracking) return _query;
        // 默认路径：追加 AsNoTracking（用户显式 AsNoTracking 时幂等，EF Core 以后者为准）
        return _query.AsNoTracking();
    }

    /// <summary>
    /// 降级路径查询（树挂载位导航排除版，A4）—— 收集导航树后过滤树挂载位路径再 Include
    /// <para>树挂载位导航（[MapTree].ChildrenProperty 命中的同名实体导航）不 Include：树构建靠 Id/ParentId，
    /// Include 预填只会制造 TreeBuilder 二次挂载源 + 自引用笛卡尔展开；非树 DTO 走全量 ApplyIncludes 原逻辑</para>
    /// <para>实现形态：Collect + 过滤 FlattenPaths + 手动 Include，改动封闭于本文件</para>
    /// <para>失败回退自动 Include（仅 Trace 警告，不阻断）——与 ApplyIncludes 容错姿态对齐，预填由防御清空兜底</para>
    /// </summary>
    /// <typeparam name="TTarget">目标 DTO 类型</typeparam>
    /// <returns>排除树挂载位导航后的查询（非树 DTO 与 ApplyIncludes 行为一致）</returns>
    [RequiresUnreferencedCode("IncludeExcludingTreeSlot scans type members and attributes via reflection to collect navigation paths. Some members may be trimmed.")]
    private static IQueryable<TSource> IncludeExcludingTreeSlot<TTarget>(IQueryable<TSource> query)
        where TTarget : class
    {
        try
        {
            // 注册表优先（Fluent .Tree() 承载—— 消费点三）+ [MapTree] 反射回退
            var registryTree = NCMapper.Runtime.RuntimeSnapshotStore.GetTreeConfig(typeof(TTarget));
            // G6 T5 锚门回滚（[MapTree] 类级树配置——同 TreeBuilder 口径）
            var treeAttr = registryTree is null
                ? typeof(TTarget).GetCustomAttribute<MapTreeAttribute>()
                : null;
            if (registryTree is null && treeAttr is null)
                return EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(query);

            var tree = EfCoreNavigationCollector.Collect(typeof(TSource), typeof(TTarget));
            if (tree.IsEmpty) return query;

            var slotName = registryTree?.ChildrenProperty ?? treeAttr!.ChildrenProperty;
            // FlattenPaths 双算合并为一次（:139 foreach + Count）
            var allPaths = tree.FlattenPaths();
            var included = 0;
            foreach (var path in allPaths)
            {
                // 树挂载位路径排除：顶层段命中 ChildrenProperty（含子路径）——不 Include
                if (path == slotName || path.StartsWith(slotName + ".", StringComparison.Ordinal)) continue;
                query = query.Include(path);
                included++;
            }
            // 树挂载位分支为独立 Include 循环（不经过
            // ApplyIncludes 收口），与主收口同站对齐补 AsSplitQuery（自引用树路径已被排除，
            // 剩余多集合导航同样有相乘风险）
            if (included > 0) query = query.AsSplitQuery();
            if (MapTracer.GloballyEnabled) MapTracer.Write($"[EFCore AsDto] 树挂载位导航排除: {slotName}（全路径 {allPaths.Count} 条，实际 Include {included} 条）");
            return query;
        }
        catch (Exception ex)
        {
            // 与 ApplyIncludes 容错姿态对齐：失败回退自动 Include（完整导航），树挂载位预填由防御清空兜底
            // 告警升级——异常类型结构化留痕
            if (MapTracer.GloballyEnabled) MapTracer.Write($"[EFCore AsDto] 树挂载位导航排除失败，回退自动 Include: {ex.GetType().Name}: {ex.Message}");
            return EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(query);
        }
    }

    /// <summary>
    /// 防御清空树挂载位—— TreeBuilder.Build 前消解 MapList 残余预填
    /// <para>仅对非空集合置空列表（List&lt;TTarget&gt; 编译期类型安全）：叶子/空集合保持原状——
    /// 与 project 路径（初始化器 = new()）及 Dapper 树（叶子空列表）语义对齐（T-3 执行期修正：
    /// 原置 null 方案使叶子 Children=null，破坏三库一致性 + Assert.Empty 兼容）</para>
    /// <para>非树 DTO 零操作；挂载位不存在/不可写静默跳过（TreeBuilder 三判据兜底）</para>
    /// </summary>
    /// <typeparam name="TTarget">目标 DTO 类型</typeparam>
    [RequiresUnreferencedCode("ClearTreeChildrenSlot scans type members via reflection. Some members may be trimmed.")]
    private static void ClearTreeChildrenSlot<TTarget>(List<TTarget> flatList)
        where TTarget : class
    {
        if (flatList.Count == 0) return;
        // 注册表优先（Fluent .Tree() 承载—— 消费点三）+ [MapTree] 反射回退；
        // 双读均未命中（非树 DTO）零操作——opt-in 语义不取默认三元组
        var registryTree = NCMapper.Runtime.RuntimeSnapshotStore.GetTreeConfig(typeof(TTarget));
        // G6 T5 锚门回滚（[MapTree] 类级树配置，非映射对惰性治理域——同 TreeBuilder）
        var childrenName = registryTree?.ChildrenProperty
            ?? typeof(TTarget).GetCustomAttribute<MapTreeAttribute>()?.ChildrenProperty;
        if (childrenName is null) return;
        var prop = typeof(TTarget).GetProperty(childrenName,
            BindingFlags.Public | BindingFlags.Instance);
        if (prop is null || !prop.CanWrite) return;
        // getter/setter 编译委托按 (TTarget, prop) 缓存
        // 现状逐元素 GetValue/SetValue 反射装箱（每元素 2 次反射调用）改强类型委托直调。
        // X8 契约保持：仅清非空预填（getter is IList { Count: > 0 } 判定不变）、空列表/null
        // 保持原状、置 new List<TTarget>() 非 null（表达式 New(typeof(List<TTarget>)) 与原
        // 行为同型）、注册表优先+[MapTree] 回退双读顺序不变、CanWrite 守卫不变、委托缓存
        // 为本方法静态缓存（不跨 RuntimeSnapshotStore 快照缓存注册表读取）；CacheRegistry
        // 登记回收（C-17 登记制口径，对齐 CollectionCopier 先例）。
        var (getter, setter) = NCMapperEFCoreExtensions.s_treeSlotAccessors.GetOrAdd((typeof(TTarget), prop), static k =>
        {
            var target = System.Linq.Expressions.Expression.Parameter(typeof(object), "t");
            var typedTarget = System.Linq.Expressions.Expression.Convert(target, k.Target);
            var getterLambda = System.Linq.Expressions.Expression.Lambda<Func<object, object?>>(
                System.Linq.Expressions.Expression.Convert(
                    System.Linq.Expressions.Expression.Property(typedTarget, k.Prop), typeof(object)),
                target);
            var setterLambda = System.Linq.Expressions.Expression.Lambda<Action<object>>(
                System.Linq.Expressions.Expression.Assign(
                    System.Linq.Expressions.Expression.Property(typedTarget, k.Prop),
                    System.Linq.Expressions.Expression.New(typeof(List<TTarget>))),
                target);
            return (getterLambda.Compile(), setterLambda.Compile());
        });
        foreach (var dto in flatList)
        {
            // 仅清非空预填（MapList 嵌套映射产物）；空列表/null（无子节点）保持原语义
            if (getter(dto) is System.Collections.IList { Count: > 0 })
                setter(dto);
        }
    }

    /// <summary>
    /// 查询并映射为 DTO 列表
    /// <para>正常路径：SQL 侧投影（AsNoTracking）；降级路径：查实体（自动 Include + AsNoTracking）→ MapList</para>
    /// </summary>
    /// <returns>DTO 列表</returns>
    [RequiresDynamicCode("ToList uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToList scans attributes via reflection. Some types may be trimmed.")]
    public List<TTarget> ToList<TTarget>() where TTarget : class
        => AsDtoExecutor.Run<TSource, TTarget, List<TTarget>>(
            EfCoreDegradationDetector.NeedsDegradation(typeof(TSource), typeof(TTarget), out var reason),
            reason, "[EFCore AsDto] ToList",
            project: () => NoTrackQuery().Select(EfCoreProjectionBuilder.Build<TSource, TTarget>()).ToList(),
            degrade: () => Mapper.MapList<TSource, TTarget>(EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(NoTrackQuery()).ToList())!);

    /// <summary>
    /// 异步查询并映射为 DTO 列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>DTO 列表</returns>
    [RequiresDynamicCode("ToListAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToListAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<List<TTarget>> ToListAsync<TTarget>(CancellationToken cancellationToken = default)
        where TTarget : class
        => await AsDtoExecutor.Run<TSource, TTarget, Task<List<TTarget>>>(
            EfCoreDegradationDetector.NeedsDegradation(typeof(TSource), typeof(TTarget), out var reason),
            reason, "[EFCore AsDto] ToListAsync",
            project: () => NoTrackQuery().Select(EfCoreProjectionBuilder.Build<TSource, TTarget>()).ToListAsync(cancellationToken),
            degrade: async () =>
            {
                var entities = await EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(NoTrackQuery()).ToListAsync(cancellationToken);
                return Mapper.MapList<TSource, TTarget>(entities)!;
            });

    /// <summary>
    /// 查询第一条记录并映射为 DTO
    /// </summary>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("First uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("First scans attributes via reflection. Some types may be trimmed.")]
    public TTarget? First<TTarget>() where TTarget : class
        => AsDtoExecutor.Run<TSource, TTarget, TTarget?>(
            EfCoreDegradationDetector.NeedsDegradation(typeof(TSource), typeof(TTarget), out var reason),
            reason, "[EFCore AsDto] First",
            project: () => NoTrackQuery().Select(EfCoreProjectionBuilder.Build<TSource, TTarget>()).FirstOrDefault(),
            degrade: () =>
            {
                var entity = EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(NoTrackQuery()).FirstOrDefault();
                return entity is null ? null : Mapper.Map<TSource, TTarget>(entity);
            });

    /// <summary>
    /// 异步查询第一条记录并映射为 DTO
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("FirstAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("FirstAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<TTarget?> FirstAsync<TTarget>(CancellationToken cancellationToken = default)
        where TTarget : class
        => await AsDtoExecutor.Run<TSource, TTarget, Task<TTarget?>>(
            EfCoreDegradationDetector.NeedsDegradation(typeof(TSource), typeof(TTarget), out var reason),
            reason, "[EFCore AsDto] FirstAsync",
            project: () => NoTrackQuery().Select(EfCoreProjectionBuilder.Build<TSource, TTarget>()).FirstOrDefaultAsync(cancellationToken),
            degrade: async () =>
            {
                var entity = await EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(NoTrackQuery()).FirstOrDefaultAsync(cancellationToken);
                return entity is null ? null : Mapper.Map<TSource, TTarget>(entity);
            });

    /// <summary>
    /// 查询唯一记录并映射为 DTO
    /// <para>与 First 实现模板一致，仅把「取首条」换为「恰一条」判定：<c>Take(2)</c> ＋ 本地
    /// <see cref="AssertSingle{T}"/>（消息单源 <see cref="ErrorMessages.SequenceNoElements"/>／
    /// <see cref="ErrorMessages.SequenceMultipleElements"/>）</para>
    /// <para>Semantics: 0 条或 &gt;1 条时抛 <see cref="InvalidOperationException"/>，消息含目标类型名，
    /// 与 FreeSql 侧同口径（方法不返回 null）</para>
    /// </summary>
    /// <returns>映射后的 DTO 对象；0 条或 &gt;1 条时抛 <see cref="InvalidOperationException"/>（消息含目标类型名，与 FreeSql 侧同口径）</returns>
    [RequiresDynamicCode("Single uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("Single scans attributes via reflection. Some types may be trimmed.")]
    public TTarget? Single<TTarget>() where TTarget : class
        => AsDtoExecutor.Run<TSource, TTarget, TTarget?>(
            EfCoreDegradationDetector.NeedsDegradation(typeof(TSource), typeof(TTarget), out var reason),
            reason, "[EFCore AsDto] Single",
            project: () =>
            {
                var dtos = NoTrackQuery().Select(EfCoreProjectionBuilder.Build<TSource, TTarget>()).Take(2).ToList();
                return AssertSingle(dtos, typeof(TTarget));
            },
            degrade: () =>
            {
                var entities = EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(NoTrackQuery()).Take(2).ToList();
                var entity = AssertSingle(entities, typeof(TTarget));
                return entity is null ? null : Mapper.Map<TSource, TTarget>(entity);
            });

    /// <summary>
    /// 异步查询唯一记录并映射为 DTO
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>映射后的 DTO 对象；0 条或 &gt;1 条时抛 <see cref="InvalidOperationException"/>（消息含目标类型名，与 FreeSql 侧同口径）</returns>
    [RequiresDynamicCode("SingleAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("SingleAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<TTarget?> SingleAsync<TTarget>(CancellationToken cancellationToken = default)
        where TTarget : class
    {
        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "SingleAsync"))
        {
            var entities = await EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(NoTrackQuery()).Take(2).ToListAsync(cancellationToken);
            var entity = AssertSingle(entities, typeof(TTarget));
            return entity is null ? null : Mapper.Map<TSource, TTarget>(entity);
        }

        var expr = EfCoreProjectionBuilder.Build<TSource, TTarget>();
        var dtos = await NoTrackQuery().Select(expr).Take(2).ToListAsync(cancellationToken);
        return AssertSingle(dtos, typeof(TTarget));
    }

    /// <summary>
    /// 恰一条判定 — 与 FreeSql 侧 <c>NCMapperFreeSqlExtensions.cs</c> 内 <c>AssertSingle</c> 逐字同构
    /// <para>消息单源（<see cref="ErrorMessages.SequenceNoElements"/>／<see cref="ErrorMessages.SequenceMultipleElements"/>）、
    /// 判定双副本；调用方须先 <c>Take(2)</c> 限制行数</para>
    /// </summary>
    /// <typeparam name="T">元素类型</typeparam>
    /// <param name="items">已由调用方限制至多 2 条的列表</param>
    /// <param name="targetType">目标 DTO 类型（消息携带其 <c>Name</c>）</param>
    /// <returns>唯一元素；0 条或 &gt;1 条时抛 <see cref="InvalidOperationException"/></returns>
    private static T? AssertSingle<T>(List<T> items, Type targetType)
    {
        if (items.Count == 0)
            throw new InvalidOperationException(ErrorMessages.SequenceNoElements(targetType.Name));
        if (items.Count > 1)
            throw new InvalidOperationException(ErrorMessages.SequenceMultipleElements(targetType.Name));
        return items[0];
    }

    /// <summary>
    /// 分页查询并映射为 DTO 列表（Count + Skip/Take 均在 SQL 侧）
    /// </summary>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页条数</param>
    /// <returns>分页结果，包含当前页数据和分页信息</returns>
    [RequiresDynamicCode("ToPagedList uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToPagedList scans attributes via reflection. Some types may be trimmed.")]
    public PagedList<TTarget> ToPagedList<TTarget>(int page, int pageSize) where TTarget : class
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), page, ErrorMessages.PageNumberMustBePositive());
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, ErrorMessages.PageSizeMustBePositive());

        var totalCount = NoTrackQuery().Count();

        List<TTarget> items;
        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "ToPagedList"))
        {
            var entities = EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(NoTrackQuery()).Skip((page - 1) * pageSize).Take(pageSize).ToList();
            items = Mapper.MapList<TSource, TTarget>(entities)!;
        }
        else
        {
            var expr = EfCoreProjectionBuilder.Build<TSource, TTarget>();
            items = NoTrackQuery().Select(expr).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }

        return new PagedList<TTarget> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// 异步分页查询并映射为 DTO 列表
    /// </summary>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分页结果，包含当前页数据和分页信息</returns>
    [RequiresDynamicCode("ToPagedListAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToPagedListAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<PagedList<TTarget>> ToPagedListAsync<TTarget>(
        int page, int pageSize, CancellationToken cancellationToken = default) where TTarget : class
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), page, ErrorMessages.PageNumberMustBePositive());
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, ErrorMessages.PageSizeMustBePositive());

        var totalCount = await NoTrackQuery().CountAsync(cancellationToken);

        List<TTarget> items;
        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "ToPagedListAsync"))
        {
            var entities = await EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(NoTrackQuery()).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            items = Mapper.MapList<TSource, TTarget>(entities)!;
        }
        else
        {
            var expr = EfCoreProjectionBuilder.Build<TSource, TTarget>();
            items = await NoTrackQuery().Select(expr).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        }

        return new PagedList<TTarget> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// 查询平铺列表并构建嵌套树（EFCore 无原生树查询——平铺 + 核心库 TreeBuilder 内存建树，与 Dapper 同构）
    /// <para>DTO 契约：[MapFrom(typeof(TSource))]（投影路径必需）+ [MapTree]（可省略取默认 Id/ParentId/Children）
    /// + Children 自引用集合（树挂载位：投影侧跳过 + degrade 侧消解，由 TreeBuilder 填充）</para>
    /// <para>降级路径：实体平铺查询排除树挂载位导航（不 Include——树构建靠 Id/ParentId，规避笛卡尔展开）+
    /// MapList + 防御清空，双路径树结果一致</para>
    /// </summary>
    /// <typeparam name="TDto">树形 DTO 类型（含 Id/ParentId/Children 契约属性）</typeparam>
    /// <returns>根节点列表（递归嵌套；空结果返回空列表）</returns>
    [RequiresDynamicCode("ToTreeList uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToTreeList scans attributes via reflection. Some types may be trimmed.")]
    public List<TDto> ToTreeList<TDto>() where TDto : class
        => AsDtoExecutor.Run<TSource, TDto, List<TDto>>(
            EfCoreDegradationDetector.NeedsDegradation(typeof(TSource), typeof(TDto), out var reason),
            reason, "[EFCore AsDto] ToTreeList",
            project: () => BuildTreeFromFlat(
                NoTrackQuery().Select(EfCoreProjectionBuilder.Build<TSource, TDto>()).ToList()),
            degrade: () => BuildTreeFromFlat(
                Mapper.MapList<TSource, TDto>(IncludeExcludingTreeSlot<TDto>(NoTrackQuery()).ToList())!));

    /// <summary>
    /// 异步查询平铺列表并构建嵌套树（真异步，对齐 Dapper ToTreeListAsync； 定案）
    /// <para>双路径与 <see cref="ToTreeList{TDto}"/> 同构：project 投影平铺 / degrade 实体+MapList（挂载位导航排除）</para>
    /// </summary>
    /// <typeparam name="TDto">树形 DTO 类型（含 Id/ParentId/Children 契约属性）</typeparam>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>根节点列表（异步）</returns>
    [RequiresDynamicCode("ToTreeListAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToTreeListAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<List<TDto>> ToTreeListAsync<TDto>(CancellationToken cancellationToken = default)
        where TDto : class
        => await AsDtoExecutor.Run<TSource, TDto, Task<List<TDto>>>(
            EfCoreDegradationDetector.NeedsDegradation(typeof(TSource), typeof(TDto), out var reason),
            reason, "[EFCore AsDto] ToTreeListAsync",
            project: async () => BuildTreeFromFlat(
                await NoTrackQuery().Select(EfCoreProjectionBuilder.Build<TSource, TDto>()).ToListAsync(cancellationToken)),
            degrade: async () => BuildTreeFromFlat(
                Mapper.MapList<TSource, TDto>(
                    await IncludeExcludingTreeSlot<TDto>(NoTrackQuery()).ToListAsync(cancellationToken))!));

    /// <summary>
    /// 平铺 → 树（TreeBuilder 内核 + 挂载位防御清空，双路径统一收口）
    /// <para>防御清空在 TreeBuilder.Build 前执行（防御双保险：project 投影跳过 + degrade 导航排除之外的兜底），
    /// 仅清非空预填、置空列表（与 <see cref="ClearTreeChildrenSlot{TTarget}"/> 语义一致——叶子/空集合保持原状，
    /// 三库叶子语义对齐；详见 ClearTreeChildrenSlot 注释 T-3 修正记录）</para>
    /// </summary>
    /// <typeparam name="TDto">树形 DTO 类型</typeparam>
    /// <param name="flatList">平铺列表（父节点无须先于子节点）</param>
    /// <returns>根节点列表</returns>
    [RequiresDynamicCode("BuildTreeFromFlat uses TreeBuilder.Build with MakeGenericType. Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildTreeFromFlat scans type members via reflection. Some types may be trimmed.")]
    private static List<TDto> BuildTreeFromFlat<TDto>(List<TDto> flatList) where TDto : class
    {
        // 三判据守卫前置 —— ClearTreeChildrenSlot 的委托工厂
        // Expression.Assign 对类型不符 Children（如 List<string>）在表达式构造期即抛
        // ArgumentException，短路 TreeBuilder.Build 内的守卫；此处先跑三判据（复用
        // TreeBuilder.ResolveChildrenProp 内核，本任务起 internal），使 MapConfigurationException
        //（TreeChildrenPropertyInvalid 契约消息）先行抛出。对合法 DTO 幂等（Build 内二次
        // 校验 no-op）；未触共享表达式构造面，X1 范围零扩散。
        var treeConfig = TreeBuilder.ResolveConfig(typeof(TDto));
        _ = TreeBuilder.ResolveChildrenProp(typeof(TDto), treeConfig);
        ClearTreeChildrenSlot(flatList);
        var roots = TreeBuilder.Build(flatList, treeConfig);
        if (MapTracer.GloballyEnabled) MapTracer.Write($"[EFCore AsDto] 树构建 平铺 {flatList.Count} 项 → 根 {roots.Count}");
        return roots;
    }

    // ================================================================
    // 带映射转手终端方法（不走 NeedsDegradation/ProjectionBuilder）
    // ================================================================

    /// <summary>
    /// 带映射转手统一前缀 — Trace + ApplyIncludes 组合
    /// <para>统一 Trace 前缀为 [AsDto NativePassthrough]，便于日志筛选和与投影/降级路径区分</para>
    /// </summary>
    /// <typeparam name="TTarget">DTO 类型</typeparam>
    /// <param name="methodName">终端方法名（如 ToDictionary、ToLookup、Single）</param>
    /// <returns>应用了 Include 的 IQueryable</returns>
    [RequiresUnreferencedCode("TraceAndApplyIncludes scans type members and attributes via reflection. Some members may be trimmed.")]
    private IQueryable<TSource> TraceAndApplyIncludes<TTarget>(string methodName) where TTarget : class
    {
        if (MapTracer.GloballyEnabled) MapTracer.Write($"[AsDto NativePassthrough] EFCore {methodName} 走原生 + 内存映射路径（不投影）");
        return EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(NoTrackQuery());
    }

    /// <summary>
    /// 查询实体并通过 NCMapper 映射为 DTO 字典
    /// <para>实现模式：Trace + ApplyIncludes（按需补全跨表导航）
    /// → EF Core 原生 ToDictionary(keySelector) 取实体字典 → 逐个 Map value 映射为 DTO 字典</para>
    /// <para>keySelector 使用 Func&lt;TSource, TKey&gt;（非 Expression&lt;&gt;），与 EF Core 原生 API 一致</para>
    /// <para>不提供 elementSelector 重载：elementSelector 已决定 value 形状，NCMapper 无介入点</para>
    /// </summary>
    /// <typeparam name="TKey">字典键类型</typeparam>
    /// <typeparam name="TTarget">DTO 类型</typeparam>
    /// <param name="keySelector">键选择器</param>
    /// <returns>DTO 字典</returns>
    [RequiresDynamicCode("ToDictionary delegates to Mapper.Map which uses Expression.Compile in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("ToDictionary scans attributes via reflection. Some types may be trimmed.")]
    public Dictionary<TKey, TTarget> ToDictionary<TKey, TTarget>(Func<TSource, TKey> keySelector)
        where TTarget : class
        where TKey : notnull
    {
        var query = TraceAndApplyIncludes<TTarget>("ToDictionary");
        var entities = query.ToDictionary(keySelector);
        var result = new Dictionary<TKey, TTarget>();
        foreach (var kv in entities)
            result[kv.Key] = Mapper.Map<TSource, TTarget>(kv.Value)!;
        return result;
    }

    /// <summary>
    /// 异步查询实体并通过 NCMapper 映射为 DTO 字典
    /// <para>EF Core 原生提供 ToDictionaryAsync（接受 Func&lt;&gt;），直接调用原生异步方法</para>
    /// <para>实现模式：Trace + ApplyIncludes → NoTrackQuery().ToDictionaryAsync → 逐个 Map value</para>
    /// </summary>
    /// <typeparam name="TKey">字典键类型</typeparam>
    /// <typeparam name="TTarget">DTO 类型</typeparam>
    /// <param name="keySelector">键选择器</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>DTO 字典</returns>
    [RequiresDynamicCode("ToDictionaryAsync delegates to EF Core ToDictionaryAsync and then Mapper.Map which uses Expression.Compile in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("ToDictionaryAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<Dictionary<TKey, TTarget>> ToDictionaryAsync<TKey, TTarget>(
        Func<TSource, TKey> keySelector,
        CancellationToken cancellationToken = default)
        where TTarget : class
        where TKey : notnull
    {
        var query = TraceAndApplyIncludes<TTarget>("ToDictionaryAsync");
        var entities = await query.ToDictionaryAsync(keySelector, cancellationToken);
        var result = new Dictionary<TKey, TTarget>();
        foreach (var kv in entities)
            result[kv.Key] = Mapper.Map<TSource, TTarget>(kv.Value)!;
        return result;
    }

    /// <summary>
    /// 查询实体并通过 NCMapper 映射为 DTO 分组查找表
    /// <para>实现模式：Trace + ApplyIncludes → EF Core 原生 ToLookup(keySelector) 取实体分组
    /// → 每组 MapList 批量映射 → SelectMany 重建 ILookup</para>
    /// <para>EF Core 不提供 ToLookupAsync，仅同步版本</para>
    /// <para>keySelector 使用 Func&lt;TSource, TKey&gt;（非 Expression&lt;&gt;），与 EF Core 原生 API 一致</para>
    /// </summary>
    /// <typeparam name="TKey">分组键类型</typeparam>
    /// <typeparam name="TTarget">DTO 类型</typeparam>
    /// <param name="keySelector">键选择器</param>
    /// <returns>DTO 分组查找表</returns>
    [RequiresDynamicCode("ToLookup delegates to Mapper.MapList which uses Expression.Compile in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("ToLookup scans attributes via reflection. Some types may be trimmed.")]
    public ILookup<TKey, TTarget> ToLookup<TKey, TTarget>(Func<TSource, TKey> keySelector)
        where TTarget : class
        where TKey : notnull
    {
        var query = TraceAndApplyIncludes<TTarget>("ToLookup");
        var entityLookup = query.ToLookup(keySelector);
        // 每组批量 MapList，避免逐个 Map 的性能开销
        return entityLookup
            .SelectMany(g =>
            {
                var dtos = Mapper.MapList<TSource, TTarget>(g.ToList())!;
                return dtos.Select(d => (Key: g.Key, Dto: d));
            })
            .ToLookup(x => x.Key, x => x.Dto);
    }

    /// <summary>
    /// 分块查询并映射为 DTO — 单次查询流式读取，按 size 分批回调
    /// <para>投影路径：<c>NoTrackQuery().Select(expr)</c> → foreach 缓冲 → done 回调</para>
    /// <para>降级路径：查实体（自动 Include + AsNoTracking）→ 内存分块 → MapList → done 回调</para>
    /// <para>与 ToList 的区别：数据量大时 ToChunkDto 可分批处理、避免一次性加载全量数据到内存</para>
    /// </summary>
    /// <typeparam name="TTarget">DTO 类型</typeparam>
    /// <param name="size">每批条数</param>
    /// <param name="done">分批回调，参数为已映射的 DTO 列表</param>
    /// <remarks>
    /// <para>空结果集（0 条）时不会触发 done 回调。</para>
    /// <para>done 回调内禁止在同一 DbContext 上执行嵌套查询（会触发 DataReader 冲突异常）。</para>
    /// </remarks>
    [RequiresDynamicCode("ToChunkDto uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToChunkDto scans attributes via reflection. Some types may be trimmed.")]
    public void ToChunkDto<TTarget>(int size, Action<List<TTarget>> done) where TTarget : class
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), size, ErrorMessages.BatchSizeMustBePositive());

        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "ToChunkDto "))
        {
            // 降级路径改分页流式——逐页查询（Skip/Take）+ MapList + done，
            // 不再 .ToList() 全量物化（违背流式承诺且与 FreeSql 不对称）。
            // 终止条件用过滤前计数（吸收 教训：EFCore 降级无 null 行过滤，batch.Count 即过滤前计数）。
            // 注意：分页需稳定排序，用户应在查询上自备 OrderBy（与 FreeSql 降级 ToChunkDto 语义一致）。
            var query = EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(NoTrackQuery());
            var page = 0;
            while (true)
            {
                var batch = query.Skip(page * size).Take(size).ToList();
                if (batch.Count == 0) break;
                var dtos = Mapper.MapList<TSource, TTarget>(batch)!;
                done(dtos);
                if (batch.Count < size) break;
                page++;
            }
        }
        else
        {
            var expr = EfCoreProjectionBuilder.Build<TSource, TTarget>();
            var query = NoTrackQuery().Select(expr);
            var buffer = new List<TTarget>(size);
            foreach (var dto in query)
            {
                buffer.Add(dto);
                if (buffer.Count >= size)
                {
                    done(new List<TTarget>(buffer));
                    buffer.Clear();
                }
            }
            if (buffer.Count > 0)
                done(buffer);
        }
    }

    /// <summary>
    /// 异步分块查询并映射为 DTO — 单次查询流式异步读取，按 size 分批回调
    /// <para>投影路径：<c>NoTrackQuery().Select(expr).AsAsyncEnumerable()</c> → await foreach 缓冲 → done 回调</para>
    /// <para>降级路径：查实体（自动 Include + AsNoTracking）→ 内存分块 → MapList → done 回调</para>
    /// <para>使用 <c>await foreach</c> 流式读取，内存占用 O(size)，适合 ASP.NET Core 大结果集场景</para>
    /// </summary>
    /// <typeparam name="TTarget">DTO 类型</typeparam>
    /// <param name="size">每批条数</param>
    /// <param name="done">异步分批回调，参数为已映射的 DTO 列表</param>
    /// <param name="cancellationToken">取消令牌 — 传播到 <c>ReadAsync</c>，取消后不再触发回调</param>
    /// <returns>表示异步分块操作的任务</returns>
    /// <remarks>
    /// <para>空结果集（0 条）时不会触发 done 回调。</para>
    /// <para>done 回调内禁止在同一 DbContext 上执行嵌套查询（DataReader 嵌套冲突）。</para>
    /// </remarks>
    [RequiresDynamicCode("ToChunkDtoAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToChunkDtoAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task ToChunkDtoAsync<TTarget>(int size, Func<List<TTarget>, Task> done, CancellationToken cancellationToken = default)
        where TTarget : class
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), size, ErrorMessages.BatchSizeMustBePositive());

        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "ToChunkDtoAsync "))
        {
            // 同同步版——降级路径改分页流式（Skip/Take + MapList），
            // 不再 ToListAsync 全量物化；终止条件用过滤前计数。
            var query = EfCoreNavigationCollector.ApplyIncludes<TSource, TTarget>(NoTrackQuery());
            var page = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = await query.Skip(page * size).Take(size).ToListAsync(cancellationToken);
                if (batch.Count == 0) break;
                var dtos = Mapper.MapList<TSource, TTarget>(batch)!;
                await done(dtos);
                if (batch.Count < size) break;
                page++;
            }
        }
        else
        {
            var expr = EfCoreProjectionBuilder.Build<TSource, TTarget>();
            var query = NoTrackQuery().Select(expr);
            var buffer = new List<TTarget>(size);
            await foreach (var dto in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                buffer.Add(dto);
                if (buffer.Count >= size)
                {
                    await done(new List<TTarget>(buffer));
                    buffer.Clear();
                }
            }
            if (buffer.Count > 0)
                await done(buffer);
        }
    }
}
