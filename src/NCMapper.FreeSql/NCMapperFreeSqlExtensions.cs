// NCMapper FreeSql 扩展包 — AsDto 终端操作 + 自动降级
// 用法：fsql.Select<OrderEntity>().Where(...).AsDto().ToList<OrderDto>()
// 依赖：NCMapper + FreeSql
#pragma warning disable NCMapper001 // Compose 为实验性 API，扩展包内部使用

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql.Internal;
using NCMapper.Internal;

namespace NCMapper.FreeSql;

/// <summary>
/// NCMapper FreeSql 查询扩展 — 将 ISelect 结果映射为 DTO，自动检测是否需要降级
/// <para>正常路径：SQL 侧投影（FreeSqlProjectionBuilder → select.ToList(expr)）</para>
/// <para>降级路径：SQL 查全量实体 → 内存 MapList（Convert/PostMap/集合导航等场景）</para>
/// </summary>
public static partial class NCMapperFreeSqlExtensions
{
    /// <summary>
    /// 反射探测字段名常量（消除魔法字符串；internal 供 FreeSqlDtoSelector 探测点使用）
    /// </summary>
    internal const string IncludeInfoFieldName = "_includeInfo";

    /// <summary>
    /// 反射探测 Include 字段名常量
    /// </summary>
    internal const string IncludeFieldName = "_include";

    /// <summary>
    /// 返回 DTO 选择器 — AsDto 是终端操作的入口，所有 Where/OrderBy/Page 应在 AsDto 之前完成
    /// <para>用法：fsql.Select&lt;UserEntity&gt;().Where(...).AsDto().ToList&lt;UserDto&gt;()</para>
    /// <para>支持终端方法：ToList/Async、First/Async、ToPagedList/Async（投影或降级）；
    /// ToTreeList/Async（树形实体映射）；ToDictionary/Async（字典映射）</para>
    /// <para>不兼容方法：GroupBy（编译错误 CS...）、ToChunk（void 返回值不可链式，请用 ToChunkDto）、
    /// ToSql/ToDataTable（非实体查询）</para>
    /// <para>如有分组需求：先 .AsDto().ToList() 再在内存中 GroupBy</para>
    /// <para>如有分块需求：使用 ToChunkDto 终端方法进行分批映射</para>
    /// </summary>
    /// <param name="select">FreeSql 查询对象</param>
    /// <returns>DTO 选择器，用于后续调用 ToList/First/ToPagedList/ToTreeList/ToDictionary 等终端方法</returns>
    [RequiresDynamicCode("AsDto uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("AsDto scans attributes via reflection. Some types may be trimmed.")]
    public static FreeSqlDtoSelector<TSource> AsDto<TSource>(this ISelect<TSource> select)
        where TSource : class
        => new(select);
}

/// <summary>
/// FreeSql DTO 选择器 — 终端方法内部自动判断投影/降级路径
/// </summary>
/// <typeparam name="TSource">源实体类型（从 ISelect 推断）</typeparam>
public sealed class FreeSqlDtoSelector<TSource>
    where TSource : class
{
    /// <summary>
    /// FreeSql 查询对象（终端方法内部使用）
    /// </summary>
    private readonly ISelect<TSource> _select;

    /// <summary>
    /// 首次终端调用（自动 Include 写入之前）拍下的用户声明 Include 根集基线。
    /// <para>null = 未拍快照；非 null 后 GetUserDeclaredIncludeRoots 一律返回快照——
    /// 自动 Include 对 _includeInfo 的原地写入不再污染"用户已声明"判定。</para>
    /// </summary>
    private HashSet<string>? _includeRootsBaseline;

    /// <summary>
    /// ToChunkDto 投影编译委托缓存（per-TSource 泛型类实例一份；与投影表达式静态缓存同级生命周期）
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Delegate> s_toChunkProjectionCache = new();

    /// <summary>
    /// Include 内部字段探测缓存——按 ISelect 运行时类型键控，
    /// 值为沿基类链探测到的 FieldInfo（null = 该类型链上未找到）。探测一次，后续命中跳过
    /// GetField 反射查找；按 Type 键控而非静态单值：不同 ISelect 实现类型的 FieldInfo 不同，
    /// 单值缓存下异型 _select 的 field.GetValue(_select) 会抛 ArgumentException。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, FieldInfo?> s_includeFieldCache = new();

    /// <summary>
    /// FreeSQLDTO选择器。
    /// </summary>
    /// <param name="select">指定选择。</param>
    internal FreeSqlDtoSelector(ISelect<TSource> select) => _select = select;
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
            "[FreeSql AsDto]", sourceType, targetType, actionLabel,
            FreeSqlDegradationDetector.NeedsDegradation);

    /// <summary>
    /// 降级路径自动 Include — 分析 TTarget 跨表依赖，追加 FreeSql IncludeByPropertyName
    /// <para>兜底策略：IncludeByPropertyName 为原地修改，部分失败时已应用的路径保留</para>
    /// <para>跳过规则：若用户已对某导航属性显式声明 Include/IncludeMany（含 then/Take/Select 修饰），
    /// 则不再追加同路径的 IncludeByPropertyName，避免覆盖用户修饰符（FreeSql 对同路径 Include 是覆盖语义）</para>
    /// </summary>
    [RequiresUnreferencedCode("ApplyFreeSqlIncludes scans type members and attributes via reflection. Some members may be trimmed.")]
    private void ApplyFreeSqlIncludes<TTarget>() where TTarget : class
    {
        // 自动 Include 常开（原 AutoIncludeEnabled 恒真守卫删除）；属性级关闭走 [MapNoAutoInclude]
        var applied = new List<string>(); // C-08①：声明提到 try 外——catch 的结构化留痕需引用已应用清单
        try
        {
            var tree = FreeSqlNavigationCollector.Collect(typeof(TSource), typeof(TTarget));
            if (tree.IsEmpty) return;

            var existingRoots = GetUserDeclaredIncludeRoots();
            foreach (var path in tree.FlattenPaths())
            {
                var root = path.IndexOf('.') is var idx and > 0 ? path[..idx] : path;
                if (existingRoots.Contains(root))
                {
                    if (MapTracer.GloballyEnabled) MapTracer.Write($"[FreeSql AsDto] 跳过自动 Include（用户已声明）: {path}");
                    continue;
                }
                _select.IncludeByPropertyName(path);
                applied.Add(path);
            }

            if (applied.Count > 0)
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[FreeSql AsDto] 自动 Include: {string.Join(", ", applied)}");
        }
        catch (Exception ex) when (!NCMapperFreeSqlExtensions.ThrowOnIncludeApplicationFailure)
        {
            // 降级显式告警升级——异常类型 + 已应用路径清单结构化留痕；
            // ThrowOnIncludeApplicationFailure 开关（默认 false 保持降级契约）置 true 时异常上抛不吞
            if (MapTracer.GloballyEnabled) MapTracer.Write(
                $"[FreeSql AsDto] 自动 Include 部分失败（已应用的路径保留）: {ex.GetType().Name}: {ex.Message}"
                + (applied.Count > 0 ? $"；已应用: {string.Join(", ", applied)}" : "；无路径被应用"));
        }
    }

    /// <summary>
    /// 反射读取 FreeSql Select 内部已注册的 Include 路径首段（属性名集合）
    /// <para>FreeSql Select0Provider 内部字段 _includeInfo: Dictionary&lt;string, IncludeInfo&gt;，
    /// 键为点分导航路径（如 "Items"、"Category.Parent"）</para>
    /// <para>BindingFlags 须含 Public|NonPublic|Instance|DeclaredOnly：
    /// internal 字段跨程序集反射需 Public 标志；DeclaredOnly 配合手动遍历基类</para>
    /// <para>反射失败时返回空集合并输出一次性警告 — 退化为全量追加，保持向后兼容</para>
    /// </summary>
    [RequiresUnreferencedCode("GetUserDeclaredIncludeRoots reads FreeSql internal fields via reflection. Some members may be trimmed.")]
    private HashSet<string> GetUserDeclaredIncludeRoots()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // -1 短路一次性会话级告警——必须置于快照检查之前，
        // 否则 C-04 基线快照命中会绕过短路路径，调用方对"自动 Include 已持续退化"仍零感知；
        // 进程内仅补一条（不随调用次数刷屏；首警已在探测失败瞬间输出）
        if (NCMapperFreeSqlExtensions.IncludeReflectionState == -1
            && Interlocked.CompareExchange(ref NCMapperFreeSqlExtensions.s_shortCircuitWarned, 1, 0) == 0)
        {
            if (MapTracer.GloballyEnabled) MapTracer.Write(
                "[FreeSql AsDto] 警告: Include 反射探测已标记不可用（会话内已报告），" +
                "自动 Include 持续退化为全量追加（可能覆盖用户已声明的 IncludeMany 修饰）。");
        }

        // 基线快照优先——首次调用（自动 Include 写入之前）拍下的用户声明根集
        // 在同一 selector 的后续调用中原样返回，自动 Include 对 _includeInfo 的写入不再污染"用户已声明"判定
        // （原实现每次终端调用都读当前字典，首次自动 Include 后同一 selector 第二次调用会误判跳过追加）
        if (_includeRootsBaseline != null)
            return _includeRootsBaseline;

        // 快速路径：已探测过且不可用 → 直接返回空（避免每次反射开销）
        if (NCMapperFreeSqlExtensions.IncludeReflectionState == -1)
            return _includeRootsBaseline ??= result;
        try
        {
            // FieldInfo 按运行时类型缓存——探测循环沿基类链
            // 逐型 GetField，首次探测写入 s_includeFieldCache，后续命中跳过反射查找（GetValue
            // 行为不变，additive 零行为）；"未找到"的 CAS 置 -1 与一次性警告副作用保留在调用侧
            var field = s_includeFieldCache.GetOrAdd(_select.GetType(), static probeType =>
            {
                var type = probeType;
                while (type != null)
                {
                    // FreeSql 3.5.x: Select0Provider._includeInfo (Dictionary<string, ...>)
                    // DeclaredOnly 必需：非公有成员 GetField 不带 DeclaredOnly 时不搜索基类
                    // Public 必需：internal 字段跨程序集反射需同时指定 Public|NonPublic
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                    var found = type.GetField(NCMapperFreeSqlExtensions.IncludeInfoFieldName, flags)
                             ?? type.GetField(NCMapperFreeSqlExtensions.IncludeFieldName, flags);
                    if (found != null) return found;
                    type = type.BaseType;
                }
                return null;
            });

            if (field == null)
            {
                // 字段不存在 — FreeSql 版本内部结构已变化
                if (Interlocked.CompareExchange(ref NCMapperFreeSqlExtensions.IncludeReflectionState, -1, 0) == 0)
                    if (MapTracer.GloballyEnabled) MapTracer.Write(
                        "[FreeSql AsDto] 警告: 未找到 FreeSql 内部字段 _includeInfo/_include，" +
                        "自动 Include 退化为全量追加（可能覆盖用户已声明的 IncludeMany 修饰）。" +
                        "请确认 FreeSql 版本在支持范围 [3.5.103, 4.0.0) 内。");
                return _includeRootsBaseline ??= result; // 退化形态同样入快照（后续调用零反射开销）
            }

            if (field.GetValue(_select) is System.Collections.IDictionary dict)
            {
                foreach (var key in dict.Keys)
                {
                    if (key is string path)
                    {
                        var root = path.IndexOf('.') is var idx and > 0 ? path[..idx] : path;
                        result.Add(root);
                    }
                }
            }
        }
        catch
        {
            // 反射异常（安全限制/类型变更等）→ 标记不可用 + 一次性警告
            if (Interlocked.CompareExchange(ref NCMapperFreeSqlExtensions.IncludeReflectionState, -1, 0) == 0)
                if (MapTracer.GloballyEnabled) MapTracer.Write(
                    "[FreeSql AsDto] 警告: 反射读取 FreeSql 内部 Include 状态失败，" +
                    "自动 Include 退化为全量追加（可能覆盖用户已声明的 IncludeMany 修饰）。" +
                    "请确认 FreeSql 版本在支持范围 [3.5.103, 4.0.0) 内。");
        }
        return _includeRootsBaseline ??= result; // 首拍基线入快照
    }

    /// <summary>
    /// 查询并映射为 DTO 列表
    /// <para>正常路径：SQL 侧投影；降级路径：查实体 → MapList</para>
    /// </summary>
    /// <returns>DTO 列表</returns>
    [RequiresDynamicCode("ToList uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToList scans attributes via reflection. Some types may be trimmed.")]
    public List<TTarget> ToList<TTarget>() where TTarget : class
    {
        List<TTarget> Degrade()
        {
            ApplyFreeSqlIncludes<TTarget>();
            var entities = _select.ToList();
            return Mapper.MapList<TSource, TTarget>(entities)!;
        }

        return AsDtoExecutor.Run<TSource, TTarget, List<TTarget>>(
            FreeSqlDegradationDetector.NeedsDegradation(typeof(TSource), typeof(TTarget), out var reason),
            reason, "[FreeSql AsDto] ToList",
            project: () => FreeSqlSqlProjectionGuard.Run(
                "[FreeSql AsDto] ToList",
                () => _select.ToList(FreeSqlNullGuardStripVisitor.Strip(FreeSqlProjectionBuilder.Build<TSource, TTarget>())),
                Degrade),
            degrade: Degrade);
    }

    /// <summary>
    /// 异步查询并映射为 DTO 列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>DTO 列表</returns>
    [RequiresDynamicCode("ToListAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToListAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<List<TTarget>> ToListAsync<TTarget>(CancellationToken cancellationToken = default)
        where TTarget : class
    {
        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "ToListAsync"))
        {
            ApplyFreeSqlIncludes<TTarget>();
            var entities = await _select.ToListAsync(cancellationToken);
            return Mapper.MapList<TSource, TTarget>(entities)!;
        }

        return await FreeSqlSqlProjectionGuard.Run(
            "[FreeSql AsDto] ToListAsync",
            () => _select.ToListAsync(FreeSqlNullGuardStripVisitor.Strip(FreeSqlProjectionBuilder.Build<TSource, TTarget>()), cancellationToken),
            async () =>
            {
                ApplyFreeSqlIncludes<TTarget>();
                var entities = await _select.ToListAsync(cancellationToken);
                return Mapper.MapList<TSource, TTarget>(entities)!;
            });
    }

    /// <summary>
    /// 查询第一条记录并映射为 DTO
    /// </summary>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("First uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("First scans attributes via reflection. Some types may be trimmed.")]
    public TTarget? First<TTarget>() where TTarget : class
    {
        TTarget? Degrade()
        {
            ApplyFreeSqlIncludes<TTarget>();
            var entity = _select.First();
            return entity is null ? null : Mapper.Map<TSource, TTarget>(entity);
        }

        return AsDtoExecutor.Run<TSource, TTarget, TTarget?>(
            FreeSqlDegradationDetector.NeedsDegradation(typeof(TSource), typeof(TTarget), out var reason),
            reason, "[FreeSql AsDto] First",
            project: () => FreeSqlSqlProjectionGuard.Run(
                "[FreeSql AsDto] First",
                () => _select.First(FreeSqlNullGuardStripVisitor.Strip(FreeSqlProjectionBuilder.Build<TSource, TTarget>())),
                Degrade),
            degrade: Degrade);
    }

    /// <summary>
    /// 异步查询第一条记录并映射为 DTO
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>映射后的 DTO 对象，无结果时返回 null</returns>
    [RequiresDynamicCode("FirstAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("FirstAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<TTarget?> FirstAsync<TTarget>(CancellationToken cancellationToken = default)
        where TTarget : class
    {
        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "FirstAsync"))
        {
            ApplyFreeSqlIncludes<TTarget>();
            var entity = await _select.FirstAsync(cancellationToken);
            return entity is null ? null : Mapper.Map<TSource, TTarget>(entity);
        }

        return await FreeSqlSqlProjectionGuard.Run<Task<TTarget?>>(
            "[FreeSql AsDto] FirstAsync",
            () => _select.FirstAsync(FreeSqlNullGuardStripVisitor.Strip(FreeSqlProjectionBuilder.Build<TSource, TTarget>()), cancellationToken)!,
            async () =>
            {
                ApplyFreeSqlIncludes<TTarget>();
                var entity = await _select.FirstAsync(cancellationToken);
                return entity is null ? null : Mapper.Map<TSource, TTarget>(entity);
            });
    }

    /// <summary>
    /// 查询唯一记录并映射为 DTO（T-F ① 契约统一新增 —— 与 EFCore Single 语义一致；
    /// 空结果与多于一条均抛 InvalidOperationException，方法不返回 null）
    /// </summary>
    /// <returns>映射后的 DTO 对象</returns>
    [RequiresDynamicCode("Single uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("Single scans attributes via reflection. Some types may be trimmed.")]
    public TTarget? Single<TTarget>() where TTarget : class
    {
        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "Single "))
        {
            ApplyFreeSqlIncludes<TTarget>();
            var entities = _select.Take(2).ToList();
            var single = AssertSingle(entities, typeof(TTarget));
            return single is null ? null : Mapper.Map<TSource, TTarget>(single);
        }

        // AssertSingle（空/多条契约异常）在 guard 外：非投影失败，不触发降级重查；SQL 路径产物已是 DTO，直接契约断言
        var singleDtos = FreeSqlSqlProjectionGuard.Run(
            "[FreeSql AsDto] Single",
            () => _select.Take(2).ToList(FreeSqlNullGuardStripVisitor.Strip(FreeSqlProjectionBuilder.Build<TSource, TTarget>())),
            () =>
            {
                ApplyFreeSqlIncludes<TTarget>();
                var rawEntities = _select.Take(2).ToList();
                return Mapper.MapList<TSource, TTarget>(rawEntities)!;
            });
        return AssertSingle(singleDtos, typeof(TTarget));
    }

    /// <summary>
    /// 异步查询唯一记录并映射为 DTO（T-F ① 契约统一新增 —— 与 EFCore SingleAsync 语义一致）
    /// </summary>
    [RequiresDynamicCode("SingleAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("SingleAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<TTarget?> SingleAsync<TTarget>(CancellationToken cancellationToken = default)
        where TTarget : class
    {
        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "SingleAsync "))
        {
            ApplyFreeSqlIncludes<TTarget>();
            var entities = await _select.Take(2).ToListAsync(cancellationToken);
            var single = AssertSingle(entities, typeof(TTarget));
            return single is null ? null : Mapper.Map<TSource, TTarget>(single);
        }

        // AssertSingle（空/多条契约异常）在 guard 外：非投影失败，不触发降级重查；SQL 路径产物已是 DTO，直接契约断言
        var singleDtos = await FreeSqlSqlProjectionGuard.Run(
            "[FreeSql AsDto] SingleAsync",
            () => _select.Take(2).ToListAsync(FreeSqlNullGuardStripVisitor.Strip(FreeSqlProjectionBuilder.Build<TSource, TTarget>()), cancellationToken),
            async () =>
            {
                ApplyFreeSqlIncludes<TTarget>();
                var rawEntities = await _select.Take(2).ToListAsync(cancellationToken);
                return Mapper.MapList<TSource, TTarget>(rawEntities)!;
            });
        return AssertSingle(singleDtos, typeof(TTarget));
    }

    private static T? AssertSingle<T>(List<T> items, Type targetType)
    {
        if (items.Count == 0)
            throw new InvalidOperationException(ErrorMessages.SequenceNoElements(targetType.Name));
        if (items.Count > 1)
            throw new InvalidOperationException(ErrorMessages.SequenceMultipleElements(targetType.Name));
        return items[0];
    }

    /// <summary>
    /// 分页查询并映射为 DTO 列表（Count + 分页均在 SQL 侧）
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

        var totalCount = _select.Count();

        List<TTarget> items;
        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "ToPagedList"))
        {
            ApplyFreeSqlIncludes<TTarget>();
            var entities = _select.Page(page, pageSize).ToList();
            items = Mapper.MapList<TSource, TTarget>(entities)!;
        }
        else
        {
            items = FreeSqlSqlProjectionGuard.Run(
                "[FreeSql AsDto] ToPagedList",
                () => _select.Page(page, pageSize).ToList(FreeSqlNullGuardStripVisitor.Strip(FreeSqlProjectionBuilder.Build<TSource, TTarget>())),
                () =>
                {
                    ApplyFreeSqlIncludes<TTarget>();
                    var guardEntities = _select.Page(page, pageSize).ToList();
                    return Mapper.MapList<TSource, TTarget>(guardEntities)!;
                });
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

        var totalCount = await _select.CountAsync(cancellationToken);

        List<TTarget> items;
        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "ToPagedListAsync"))
        {
            ApplyFreeSqlIncludes<TTarget>();
            var entities = await _select.Page(page, pageSize).ToListAsync(cancellationToken);
            items = Mapper.MapList<TSource, TTarget>(entities)!;
        }
        else
        {
            items = await FreeSqlSqlProjectionGuard.Run(
                "[FreeSql AsDto] ToPagedListAsync",
                () => _select.Page(page, pageSize).ToListAsync(FreeSqlNullGuardStripVisitor.Strip(FreeSqlProjectionBuilder.Build<TSource, TTarget>()), cancellationToken),
                async () =>
                {
                    ApplyFreeSqlIncludes<TTarget>();
                    var guardEntities = await _select.Page(page, pageSize).ToListAsync(cancellationToken);
                    return Mapper.MapList<TSource, TTarget>(guardEntities)!;
                });
        }

        return new PagedList<TTarget> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// 分块查询并映射为 DTO — 利用 FreeSql ToChunk 分批回调，每批自动映射
    /// <para>与 ToList 的区别：数据量大时 ToChunkDto 可分批处理、避免一次性加载全量数据到内存</para>
    /// <para>两条路径均为客户端内存映射：正常路径 = 编译投影委托在内存中对实体逐条映射；
    /// 降级路径 = 查实体 → MapList。两者均先经 <c>ApplyFreeSqlIncludes</c> 自动 Include 导航属性
    ///（正常路径未走 Include 会致导航静默 null）。</para>
    /// </summary>
    /// <param name="size">每批条数</param>
    /// <param name="done">分批回调，参数为已映射的 DTO 列表</param>
    [RequiresDynamicCode("ToChunkDto uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToChunkDto scans attributes via reflection. Some types may be trimmed.")]
    public void ToChunkDto<TTarget>(int size, Action<List<TTarget>> done) where TTarget : class
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), size, ErrorMessages.BatchSizeMustBePositive()); // T-F ② 契约统一（与 EFCore 对齐）

        // ApplyFreeSqlIncludes 提升至两条路径公共前置
        // 原实现仅在降级分支调用，正常路径（客户端映射）不 Include，致导航属性静默 null
        //（降级路径反而更正确）。两条路径的导航结果由此一致。
        ApplyFreeSqlIncludes<TTarget>();

        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "ToChunkDto "))
        {
            _select.ToChunk(size, chunk =>
            {
                if (chunk.Object.Count == 0) return; // T-F ③ 空结果不回调（契约统一）
                var dtos = Mapper.MapList<TSource, TTarget>(chunk.Object)!;
                done(dtos);
            });
        }
        else
        {
            // 编译委托缓存（ORM 发现 11，消除每次调用 Build+Compile 的 ~ms 级固定成本）
            var compiledExpr = (Func<TSource, TTarget>)s_toChunkProjectionCache.GetOrAdd(
                typeof(TTarget),
                _ => FreeSqlProjectionBuilder.Build<TSource, TTarget>().Compile());
            _select.ToChunk(size, chunk =>
            {
                if (chunk.Object.Count == 0) return; // T-F ③ 空结果不回调（契约统一）
                var dtos = chunk.Object.Select(compiledExpr).ToList();
                done(dtos);
            });
        }
    }

    /// <summary>
    /// 异步分块查询并映射为 DTO — 逐页真异步取数（Skip/Take + ToListAsync），按 size 分批回调
    /// <para>与 ToList 的区别：数据量大时 ToChunkDtoAsync 可分批处理、避免一次性加载全量数据到内存</para>
    /// <para>两条路径均为客户端内存映射：投影路径 = 逐页取实体后套编译投影委托在内存中逐条映射；
    /// 降级路径 = 逐页查实体 → MapList。两者均先经 <c>ApplyFreeSqlIncludes</c> 自动 Include 导航属性
    ///（与同步版一致）。</para>
    /// <para>内存语义与同步版 <see cref="ToChunkDto"/> 的差异：同步版走 FreeSql 原生 <c>ToChunk</c> 流式分块；
    /// 本方法为逐页查询（每页仅物化 size 条到内存），内存占用仍按批可控——FreeSql 3.5.103 无流式异步枚举 API。</para>
    /// <para>分页依赖稳定排序：请在查询上自备 OrderBy，否则大结果集跨页取数可能静默漏行/重行
    /// （Skip/Take 分页在无稳定排序时页边界不确定，与 EFCore 版口径一致）。</para>
    /// </summary>
    /// <param name="size">每批条数</param>
    /// <param name="done">异步分批回调，参数为已映射的 DTO 列表</param>
    /// <param name="cancellationToken">取消令牌 — 每轮取数前检查并透传到底层 <c>ToListAsync</c>，取消后不再触发回调</param>
    /// <returns>表示异步分块操作的任务</returns>
    /// <remarks>
    /// <para>空结果集（0 条）时不会触发 done 回调。</para>
    /// <para>降级路径不使用 <c>_select.ToChunk</c>（同步回调 API，无法取消传播）。</para>
    /// </remarks>
    [RequiresDynamicCode("ToChunkDtoAsync uses BuildProjection with MakeGenericMethod. Not supported in AOT. Projection source generator not yet available.")]
    [RequiresUnreferencedCode("ToChunkDtoAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task ToChunkDtoAsync<TTarget>(int size, Func<List<TTarget>, Task> done, CancellationToken cancellationToken = default)
        where TTarget : class
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), size, ErrorMessages.BatchSizeMustBePositive()); // T-F ② 契约统一（与同步版/EFCore 对齐）

        // ApplyFreeSqlIncludes 提升至两条路径公共前置（与同步版一致）
        ApplyFreeSqlIncludes<TTarget>();

        if (NeedsDegradationWithTrace(typeof(TSource), typeof(TTarget), "ToChunkDtoAsync "))
        {
            // 降级路径：逐页查实体（Skip/Take + ToListAsync(ct) 真异步，T6.7 探针实证形态）→ MapList → done。
            // 禁全量物化：逐页 Skip/Take，每页仅物化 size 条（纪律，镜像 EFCore ToChunkDtoAsync）。
            // 终止条件用过滤前计数（H-01 语义——本路径无 null 行过滤，batch.Count 即过滤前计数）。
            var page = 1;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = await _select.Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
                if (batch.Count == 0) break;
                var dtos = Mapper.MapList<TSource, TTarget>(batch)!;
                await done(dtos);
                if (batch.Count < size) break;
                page++;
            }
        }
        else
        {
            // 投影路径：逐页取实体（Skip/Take 真异步）→ 编译委托内存映射 → done。
            // 禁全量物化（纪律）：全量 ToListAsync 后内存切块同时违背同步版 XML
            // "避免一次性加载全量数据到内存"承诺；委托为 Func<TSource,TTarget> 非 Expression
            // 不能进 SQL 翻译，故逐页取实体 + 内存映射为唯一形态。编译委托缓存与同步版共用。
            var compiledExpr = (Func<TSource, TTarget>)s_toChunkProjectionCache.GetOrAdd(
                typeof(TTarget),
                _ => FreeSqlProjectionBuilder.Build<TSource, TTarget>().Compile());
            var page = 1;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entities = await _select.Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
                if (entities.Count == 0) break;
                var dtos = entities.Select(compiledExpr).ToList();
                await done(dtos);
                if (entities.Count < size) break;
                page++;
            }
        }
    }

    // ================================================================
    // 带映射转手终端方法（不走 NeedsDegradation/ProjectionBuilder）
    // ================================================================

    /// <summary>
    /// 带映射转手统一前缀 — Trace + ApplyFreeSqlIncludes 组合
    /// <para>统一 Trace 前缀为 [AsDto NativePassthrough]，便于日志筛选和与投影/降级路径区分</para>
    /// </summary>
    /// <typeparam name="TTarget">DTO 类型</typeparam>
    /// <param name="methodName">终端方法名（如 ToTreeList、ToDictionary）</param>
    [RequiresUnreferencedCode("TraceNativePassthrough scans type members and attributes via reflection. Some members may be trimmed.")]
    private void TraceNativePassthrough<TTarget>(string methodName) where TTarget : class
    {
        if (MapTracer.GloballyEnabled) MapTracer.Write($"[AsDto NativePassthrough] FreeSql {methodName} 走原生 + 内存映射路径（不投影）");
        ApplyFreeSqlIncludes<TTarget>();
    }

    /// <summary>
    /// 查询实体树并通过 NCMapper 映射为 DTO 树
    /// <para>实现模式：Trace + ApplyFreeSqlIncludes（按需补全跨表导航）→ FreeSql 原生 ToTreeList() 取实体树
    /// → NCMapper.MapList 递归映射为 DTO 树</para>
    /// <para>DTO 约束：须标注 [MapFrom(typeof(TSource))]，并具有与实体树导航属性同名的自引用集合属性（元素类型为 TTarget 自身）</para>
    /// <para>同名校验：DTO 自引用集合属性名须与实体自引用导航属性名一致（任一命中即通过），
    /// 全不匹配抛异常——防异名场景子树静默丢失（仅返回根节点）</para>
    /// <para>关键约束：不走 NeedsDegradation 判断、不走 ProjectionBuilder——永远走"原生 + 内存映射"路径</para>
    /// </summary>
    /// <typeparam name="TTarget">DTO 类型（须标注 [MapFrom]，且具有自引用集合属性）</typeparam>
    /// <returns>DTO 树列表（仅包含根节点）</returns>
    /// <exception cref="MapConfigurationException">TTarget 缺少自引用集合属性（元素类型为 TTarget 自身）时抛出；
    /// 或 TTarget 自引用集合属性名与 TSource 自引用导航属性名全不匹配时抛出（Q8 同名校验）</exception>
    [RequiresDynamicCode("ToTreeList delegates to Mapper.MapList which uses Expression.Compile in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("ToTreeList scans attributes via reflection. Some types may be trimmed.")]
    public List<TTarget> ToTreeList<TTarget>() where TTarget : class
    {
        ValidateTreeDtoTarget<TTarget>();
        ValidateTreeChildrenNameMatch<TSource, TTarget>();
        TraceNativePassthrough<TTarget>("ToTreeList");
        var entityTree = _select.ToTreeList();
        return Mapper.MapList<TSource, TTarget>(entityTree)!;
    }

    /// <summary>
    /// 反射校验 DTO 的自引用集合属性 — 用于 ToTreeList 入口校验
    /// <para>规则：TTarget 必须包含至少一个集合属性，其元素类型为 TTarget 自身</para>
    /// <para>校验失败抛 MapConfigurationException（与 ErrorMessages 体系一致）</para>
    /// </summary>
    /// <typeparam name="TTarget">DTO 类型</typeparam>
    /// <exception cref="MapConfigurationException">TTarget 缺少自引用集合属性时抛出</exception>
    [RequiresUnreferencedCode("ValidateTreeDtoTarget scans type members and attributes via reflection. Some members may be trimmed.")]
    private static void ValidateTreeDtoTarget<TTarget>() where TTarget : class
    {
        var targetType = typeof(TTarget);
        var props = TypeHelper.GetPublicProperties(targetType);
        foreach (var prop in props)
        {
            var elementType = NavigationResolver.GetCollectionElementType(prop.PropertyType);
            if (elementType == targetType)
                return; // 找到自引用集合属性，校验通过
        }
        throw new MapConfigurationException(ErrorMessages.TreeDtoMissingChildProperty(targetType.Name));
    }

    /// <summary>
    /// 反射校验 DTO 子集合属性名与实体自引用导航名一致 — 用于 ToTreeList 入口同名校验
    /// <para>规则：TTarget 任一自引用集合属性名与 TSource 某自引用导航属性名同名即通过（任一命中判定，
    /// 与 MapList 按名映射实际行为对齐）；TSource 无自引用导航属性时跳过校验（普通非树场景零影响）；
    /// 全不匹配抛 MapConfigurationException——防异名场景子树静默丢失（仅返回根节点）</para>
    /// <para>FreeSql 不读取 [MapTree]，校验以反射实际属性名为准（不读 [MapTree].ChildrenProperty）</para>
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <exception cref="MapConfigurationException">DTO 自引用集合名与实体自引用导航名全不匹配时抛出</exception>
    [RequiresUnreferencedCode("ValidateTreeChildrenNameMatch scans type members and attributes via reflection. Some members may be trimmed.")]
    private static void ValidateTreeChildrenNameMatch<TEntity, TDto>()
        where TEntity : class
        where TDto : class
    {
        // TDto 侧：全部自引用集合属性名（元素类型 == TDto 自身，与 ValidateTreeDtoTarget 同构判定）
        var dtoChildrenNames = TypeHelper.GetPublicProperties(typeof(TDto))
            .Where(p => NavigationResolver.GetCollectionElementType(p.PropertyType) == typeof(TDto))
            .Select(p => p.Name)
            .ToHashSet();

        // TEntity 侧：全部自引用导航属性名（元素类型 == TEntity 自身）
        var sourceNavNames = TypeHelper.GetPublicProperties(typeof(TEntity))
            .Where(p => NavigationResolver.GetCollectionElementType(p.PropertyType) == typeof(TEntity))
            .Select(p => p.Name)
            .ToHashSet();

        // 实体无自引用导航 → 跳过（F1 兜底：普通非树场景零影响）
        if (sourceNavNames.Count == 0)
            return;

        // 任一命中 → 通过；全不匹配 → 抛（消息列全部候选名便于迁移）
        if (!dtoChildrenNames.Overlaps(sourceNavNames))
            throw new MapConfigurationException(ErrorMessages.TreeDtoChildrenNameMismatch(
                typeof(TEntity).Name, typeof(TDto).Name,
                string.Join(", ", sourceNavNames), string.Join(", ", dtoChildrenNames)));
    }

    /// <summary>
    /// 异步查询实体树并通过 NCMapper 映射为 DTO 树
    /// <para>FreeSql 3.5.103 不提供 ToTreeListAsync，本方法通过 Task.FromResult 同步包装 ToTreeList 实现异步语义</para>
    /// <para>实际执行为同步：Trace → ApplyFreeSqlIncludes → FreeSql 原生 ToTreeList() → NCMapper.MapList 递归映射。
    /// C-14 让步方案明示：同步执行、取消令牌仅在入口检查一次（ThrowIfCancellationRequested），
    /// 执行期间不可取消；调用方如需细粒度取消，请改用同步 ToTreeList 并在批间自主控制取消粒度</para>
    /// <para>同名校验随同步入口继承：DTO 自引用集合属性名与实体自引用导航名全不匹配时同样抛
    /// MapConfigurationException</para>
    /// </summary>
    /// <typeparam name="TTarget">DTO 类型</typeparam>
    /// <param name="cancellationToken">取消令牌（仅入口生效——同步实现，执行期间不可取消）</param>
    /// <returns>DTO 树列表（仅包含根节点）</returns>
    /// <exception cref="MapConfigurationException">TTarget 缺少自引用集合属性，或其名与 TSource 自引用导航名全不匹配时抛出（Q8 同名校验）</exception>
    [RequiresDynamicCode("ToTreeListAsync delegates to ToTreeList and then Mapper.MapList which uses Expression.Compile in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("ToTreeListAsync scans attributes via reflection. Some types may be trimmed.")]
    public Task<List<TTarget>> ToTreeListAsync<TTarget>(CancellationToken cancellationToken = default)
        where TTarget : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ToTreeList<TTarget>());
    }

    /// <summary>
    /// 查询实体并通过 NCMapper 映射为 DTO 字典
    /// <para>实现模式：Trace → ApplyFreeSqlIncludes → FreeSql 原生 ToDictionary(keySelector) 取实体字典
    /// → 逐个 Map value 映射为 DTO 字典</para>
    /// <para>keySelector 使用 Func&lt;TSource, TKey&gt;（非 Expression&lt;&gt;），在内存执行无需 SQL 翻译能力</para>
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
        TraceNativePassthrough<TTarget>("ToDictionary");
        var entities = _select.ToDictionary(keySelector);
        var result = new Dictionary<TKey, TTarget>();
        foreach (var kv in entities)
            result[kv.Key] = Mapper.Map<TSource, TTarget>(kv.Value)!;
        return result;
    }

    /// <summary>
    /// 异步查询实体并通过 NCMapper 映射为 DTO 字典
    /// <para>FreeSql 3.5.103 提供 ToDictionaryAsync，本方法直接调用原生异步方法</para>
    /// <para>实现模式：Trace + ApplyFreeSqlIncludes → FreeSql 原生 ToDictionaryAsync → 逐个 Map value</para>
    /// </summary>
    /// <typeparam name="TKey">字典键类型</typeparam>
    /// <typeparam name="TTarget">DTO 类型</typeparam>
    /// <param name="keySelector">键选择器</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>DTO 字典</returns>
    [RequiresDynamicCode("ToDictionaryAsync delegates to FreeSql ToDictionaryAsync and then Mapper.Map which uses Expression.Compile in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("ToDictionaryAsync scans attributes via reflection. Some types may be trimmed.")]
    public async Task<Dictionary<TKey, TTarget>> ToDictionaryAsync<TKey, TTarget>(
        Func<TSource, TKey> keySelector,
        CancellationToken cancellationToken = default)
        where TTarget : class
        where TKey : notnull
    {
        TraceNativePassthrough<TTarget>("ToDictionaryAsync");
        var entities = await _select.ToDictionaryAsync(keySelector, cancellationToken);
        var result = new Dictionary<TKey, TTarget>();
        foreach (var kv in entities)
            result[kv.Key] = Mapper.Map<TSource, TTarget>(kv.Value)!;
        return result;
    }

    /// <summary>
    /// 查询实体并通过 NCMapper 映射为 DTO 分组查找表
    /// <para>实现模式：Trace + ApplyFreeSqlIncludes → FreeSql 原生 ToList 取实体 → LINQ ToLookup 分组
    /// → 每组 MapList 批量映射 → SelectMany 重建 ILookup</para>
    /// <para>FreeSql 3.5.103 不提供 ToLookup/ToLookupAsync 任一（FreeSql.xml 探针实证零条目），
    /// 以同步 ToList 取实体 + 内存分组实现（与 EFCore 版组装形态同构）</para>
    /// <para>keySelector 使用 Func&lt;TSource, TKey&gt;（非 Expression&lt;&gt;），在内存执行无需 SQL 翻译能力</para>
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
        TraceNativePassthrough<TTarget>("ToLookup");
        var entityLookup = _select.ToList().ToLookup(keySelector);
        // 每组批量 MapList，避免逐个 Map 的性能开销（组装形态与 EFCore ToLookup 一致）
        return entityLookup
            .SelectMany(g =>
            {
                var dtos = Mapper.MapList<TSource, TTarget>(g.ToList())!;
                return dtos.Select(d => (Key: g.Key, Dto: d));
            })
            .ToLookup(x => x.Key, x => x.Dto);
    }
}

// ================================================================
// 多表 Compose 投影扩展（ISelect<T1, T2> / ISelect<T1, T2, T3>）
// ================================================================
