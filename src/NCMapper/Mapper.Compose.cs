#pragma warning disable NCMapper001 // 内部使用 [Experimental] 的 Pipeline API
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
namespace NCMapper;

/// <summary>
/// Mapper 的 Compose 相关功能 — 多源合成映射
/// </summary>
public partial class Mapper
{
    /// <summary>
    /// 多源合成映射 — 从多个源对象合成一个 DTO
    /// <para>DTO 需标注 [MapFrom(typeof(主源类型))]，主源对象用于创建 DTO，其余源对象用于更新补充字段</para>
    /// <para>处理顺序规则：① 主源（[MapFrom] 声明的类型）恒第一个处理，创建 DTO；
    /// ② 其余源按传入数组顺序依次更新 DTO；③ 同名字段后来者覆盖先写入者。
    /// 用户可通过调整数组顺序控制重叠字段的最终取值。</para>
    /// 调用示例：var dto = Mapper.Compose&lt;OrderDetailDto&gt;(order, customer, product);
    /// </summary>
    /// <typeparam name="TTarget">目标 DTO 类型</typeparam>
    /// <param name="sources">源对象列表（类型需与 DTO 的 [MapProperty] 标注匹配）</param>
    /// <returns>合成后的 DTO 对象</returns>
    [System.Diagnostics.CodeAnalysis.Experimental("NCMapper001")]
    [RequiresDynamicCode("Compose uses MakeGenericType and MakeGenericMethod to build the compose delegate. Not supported in AOT. Register a source-generated compose delegate via RegisterGeneratedCompose to make this AOT-safe.")]
    [RequiresUnreferencedCode("Compose scans type members and attributes via reflection. Some members may be trimmed. Register a source-generated compose delegate via RegisterGeneratedCompose to make this AOT-safe.")]
    public static TTarget? Compose<TTarget>(params object[] sources) where TTarget : class
    {
        if (sources == null || sources.Length == 0) return null;

        TTarget? result;

        // 层1：有源生成器注册 → 走编译时代码（零反射，AOT 友好）
        if (s_generatedComposeFuncs.TryGetValue(typeof(TTarget), out var composeFunc))
            result = (TTarget?)composeFunc(sources);
        else
            // 层2：无源生成器 → 走运行时反射路径
            result = ComposeRuntime<TTarget>(sources);

        // Pipeline PostMap — 合成完成后对结果执行后处理
        if (Mapper.Pipeline.IsEnabled && result != null)
        {
            foreach (var source in sources)
            {
                if (source != null)
                    Pipeline.ExecutePostMapNonGeneric(source, result);
            }
        }

        return result;
    }

    /// <summary>
    /// 多源合成映射（带回调）— 合成完成后对最终 DTO 执行回调
    /// <para>action 在所有源对象写入 DTO 后执行，可在此计算字段、设置默认值等</para>
    /// <para>处理顺序规则同 <see cref="Compose{TTarget}(object[])"/>：主源恒首，其余按数组顺序，后来者覆盖</para>
    /// 调用示例：var dto = Mapper.Compose&lt;OrderDetailDto&gt;(d => d.DisplayName = "xxx", order, customer, product);
    /// </summary>
    /// <typeparam name="TTarget">目标 DTO 类型</typeparam>
    /// <param name="action">合成完成后的回调（可为 null）</param>
    /// <param name="sources">源对象列表</param>
    /// <returns>合成后的 DTO 对象</returns>
    [System.Diagnostics.CodeAnalysis.Experimental("NCMapper001")]
    [RequiresDynamicCode("Compose uses MakeGenericType and MakeGenericMethod to build the compose delegate. Not supported in AOT. Register a source-generated compose delegate via RegisterGeneratedCompose to make this AOT-safe.")]
    [RequiresUnreferencedCode("Compose scans type members and attributes via reflection. Some members may be trimmed. Register a source-generated compose delegate via RegisterGeneratedCompose to make this AOT-safe.")]
    public static TTarget? Compose<TTarget>(Action<TTarget>? action, params object[] sources) where TTarget : class
    {
        var result = Compose<TTarget>(sources);
        if (result != null) action?.Invoke(result);
        return result;
    }

    /// <summary>
    /// 多源合成列表映射 — 对一组源对象列表批量合成 DTO 列表
    /// 每组 sources 数组对应一个 DTO，action 对每个合成后的 DTO 执行回调
    /// 调用示例：var dtos = Mapper.ComposeList&lt;OrderDetailDto&gt;(d => d.DisplayName = "x", sourceGroups);
    /// </summary>
    /// <typeparam name="TTarget">目标 DTO 类型</typeparam>
    /// <param name="action">每个 DTO 合成完成后的回调（可为 null）</param>
    /// <param name="sourceGroups">源对象数组的列表，每组对应一个 DTO</param>
    /// <returns>合成后的 DTO 列表</returns>
    [System.Diagnostics.CodeAnalysis.Experimental("NCMapper001")]
    [RequiresDynamicCode("ComposeList uses MakeGenericType and MakeGenericMethod to build the compose delegate. Not supported in AOT. Register a source-generated compose delegate via RegisterGeneratedCompose to make this AOT-safe.")]
    [RequiresUnreferencedCode("ComposeList scans type members and attributes via reflection. Some members may be trimmed. Register a source-generated compose delegate via RegisterGeneratedCompose to make this AOT-safe.")]
    public static List<TTarget>? ComposeList<TTarget>(Action<TTarget>? action, params object[][] sourceGroups) where TTarget : class
    {
        if (sourceGroups == null || sourceGroups.Length == 0) return null;

        var list = new List<TTarget>(sourceGroups.Length);
        foreach (var sources in sourceGroups)
        {
            var dto = Compose<TTarget>(sources);
            if (dto != null)
            {
                action?.Invoke(dto);
                list.Add(dto);
            }
        }
        return list;
    }

    /// <summary>
    /// 多源合成列表映射（无回调）— 对一组源对象列表批量合成 DTO 列表
    /// </summary>
    /// <typeparam name="TTarget">目标 DTO 类型</typeparam>
    /// <param name="sourceGroups">源对象数组的列表，每组对应一个 DTO</param>
    /// <returns>合成后的 DTO 列表</returns>
    /// <exception cref="MapConfigurationException">主映射源类型未标注 [MapFrom] 或 Compose 主源类型缺失时抛出</exception>
    [System.Diagnostics.CodeAnalysis.Experimental("NCMapper001")]
    [RequiresDynamicCode("ComposeList uses MakeGenericType and MakeGenericMethod to build the compose delegate. Not supported in AOT. Register a source-generated compose delegate via RegisterGeneratedCompose to make this AOT-safe.")]
    [RequiresUnreferencedCode("ComposeList scans type members and attributes via reflection. Some members may be trimmed. Register a source-generated compose delegate via RegisterGeneratedCompose to make this AOT-safe.")]
    public static List<TTarget>? ComposeList<TTarget>(params object[][] sourceGroups) where TTarget : class
        => ComposeList<TTarget>(null, sourceGroups);

    /// <summary>
    /// 运行时 Compose 实现 — 通过表达式树委托调用映射方法
    /// <para>有源生成器注册时不会走此路径；此方法保留用于无源生成器场景的兼容</para>
    /// <para>用表达式树委托替代 MethodInfo.Invoke，消除反射调用开销</para>
    /// <para>执行 Pipeline PostMap：合成完成后若 Pipeline 启用，对合成结果执行 PostMap 回调</para>
    /// </summary>
    /// <typeparam name="TTarget">目标 DTO 类型</typeparam>
    /// <param name="sources">源对象列表</param>
    /// <returns>合成后的 DTO 对象</returns>
    [RequiresDynamicCode("ComposeRuntime builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("ComposeRuntime scans type members and attributes via reflection. Some members may be trimmed.")]
    private static TTarget? ComposeRuntime<TTarget>(object[] sources) where TTarget : class
    {
        /*
         * 顺序规则（规则2）：
         *   1. 主源（[MapFrom] 声明的类型）恒第一个处理，创建 DTO
         *   2. 其余源按传入数组顺序依次更新 DTO
         *   3. 同名字段后来者覆盖先写入者 — 用户可通过数组顺序控制字段优先级
         */

        // 获取 DTO 的 [MapFrom] 特性，确定主映射源类型
        var targetType = typeof(TTarget);
        var mapFromAttr = targetType.GetCustomAttribute<Attributes.MapFromAttribute>();
        if (mapFromAttr == null)
            throw new MapConfigurationException(
                ErrorMessages.MapFromNotApplied(targetType.Name, "Compose"),
                null, targetType);

        var primarySourceType = mapFromAttr.FromSource;

        // 第一趟：找到主源，创建 DTO
        TTarget? result = null;
        object? primarySource = null;

        foreach (var source in sources)
        {
            // 主源匹配由严格相等放宽为类型兼容
            // （本型或派生均可命中）——与生成引擎 `src is T primary` 语义对齐，消除双引擎分叉
            // （派生类主源曾被运行时引擎拒绝而生成引擎接受）。
            if (source != null && primarySourceType.IsInstanceOfType(source))
            {
                primarySource = source;
                // Lazy(ExecutionAndPublication) 包装
                // GetOrAdd 并发首次 miss 时 valueFactory 会重复执行（编译开销翻倍），
                // 统一委托缓存并发口径（对照 MapperBase.cs s_mapActionLazy 先例）。
                var mapFunc = s_composeMapDelegateCache.GetOrAdd(
                    (primarySourceType, targetType), key => new Lazy<Func<object, object?>>(() => CompileComposeMapDelegate(key))).Value;
                result = (TTarget?)mapFunc(source);
                break;
            }
        }

        // 主源对象未找到 → 抛出明确异常（非静默返回 null）
        if (result == null)
        {
            throw new MapConfigurationException(
                ErrorMessages.ComposeSourceNotFound(primarySourceType.Name),
                primarySourceType, targetType);
        }

        // 第二趟：按数组顺序更新其余源（跳过主源实例）
        foreach (var source in sources)
        {
            if (source == null || ReferenceEquals(source, primarySource)) continue;

            var sourceType = source.GetType();
            // 动态类型键直算不入缓存——更新委托缓存键含
            // source.GetType()（运行时型），动态派生类型（Reflection.Emit/DispatchProxy/
            // collectible ALC）下键空间无界，与 6 处「动态类型直算不入缓存」策略对齐
            //（判据 PropertyMatcher.IsDynamicType，ExpressionBuilder.Paths 双键成分检查同款）。
            if (PropertyMatcher.IsDynamicType(sourceType) || PropertyMatcher.IsDynamicType(targetType))
            {
                CompileComposeUpdateDelegate((sourceType, targetType))(source, result);
                continue;
            }
            // 同上 Lazy 包装（更新路径委托）。
            var updateFunc = s_composeUpdateDelegateCache.GetOrAdd(
                (sourceType, targetType), key => new Lazy<Action<object, object>>(() => CompileComposeUpdateDelegate(key))).Value;
            updateFunc(source, result);
        }

        // Pipeline 已在 Compose<TTarget> 入口统一执行（PostMap），此处不再重复

        return result;
    }

    /// <summary>
    /// Compose 非更新路径委托缓存 — key: (源类型, 目标类型), value: (source) => MapperBase&lt;TSource,TTarget&gt;.Map((TSource)source, null)
    /// <para>用表达式树委托替代 MethodInfo.Invoke</para>
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, Type), Lazy<Func<object, object?>>> s_composeMapDelegateCache = new();

    /// <summary>
    /// Compose 委托编译计数 —— 编译方法体实际执行次数
    ///（Lazy 包装后并发首次 miss 也只执行 1 次；internal 测试可见，不计公共 API 面）。
    /// </summary>
    internal static long ComposeCompileCount;

    /// <summary>
    /// Compose 更新路径委托缓存 — key: (源类型, 目标类型), value: (source, target) => MapperBase&lt;TSource,TTarget&gt;.Map((TSource)source, (TTarget)target, null)
    /// <para>用表达式树委托替代 MethodInfo.Invoke</para>
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, Type), Lazy<Action<object, object>>> s_composeUpdateDelegateCache = new();

    /// <summary>
    /// 编译 Compose 非更新路径委托 — 构建表达式树 (source) => MapperBase&lt;TSource,TTarget&gt;.Map((TSource)source, null)
    /// </summary>
    /// <param name="key">(源类型, 目标类型)</param>
    /// <returns>编译后的委托</returns>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("CompileComposeMapDelegate uses MakeGenericType and MakeGenericMethod. This is not supported in AOT/Trimmed scenarios.")]
    [RequiresUnreferencedCode("CompileComposeMapDelegate scans type members and attributes via reflection. Some members may be trimmed.")]
    private static Func<object, object?> CompileComposeMapDelegate((Type, Type) key)
    {
        Interlocked.Increment(ref ComposeCompileCount);
        var sourceType = key.Item1;
        var targetType = key.Item2;
        var mapperType = typeof(MapperBase<,>).MakeGenericType(sourceType, targetType);
        var actionType = typeof(Action<,>).MakeGenericType(sourceType, targetType);
        var mapMethod = ReflectionHelper.GetRequiredMethod(mapperType, "Map", new[] { sourceType, actionType });
        var srcParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "src");
        var castSrc = System.Linq.Expressions.Expression.Convert(srcParam, sourceType);
        var call = System.Linq.Expressions.Expression.Call(mapMethod, castSrc,
            System.Linq.Expressions.Expression.Constant(null, actionType));
        var castResult = System.Linq.Expressions.Expression.Convert(call, typeof(object));
        return System.Linq.Expressions.Expression.Lambda<Func<object, object?>>(castResult, srcParam).Compile();
    }

    /// <summary>
    /// 编译 Compose 更新路径委托 — 构建表达式树 (source, target) => MapperBase&lt;TSource,TTarget&gt;.Map((TSource)source, (TTarget)target, null)
    /// </summary>
    /// <param name="key">(源类型, 目标类型)</param>
    /// <returns>编译后的委托</returns>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("CompileComposeUpdateDelegate uses MakeGenericType and MakeGenericMethod. This is not supported in AOT/Trimmed scenarios.")]
    [RequiresUnreferencedCode("CompileComposeUpdateDelegate scans type members and attributes via reflection. Some members may be trimmed.")]
    private static Action<object, object> CompileComposeUpdateDelegate((Type, Type) key)
    {
        Interlocked.Increment(ref ComposeCompileCount);
        var sourceType = key.Item1;
        var targetType = key.Item2;
        var mapperType = typeof(MapperBase<,>).MakeGenericType(sourceType, targetType);
        var actionType = typeof(Action<,>).MakeGenericType(sourceType, targetType);
        var mapMethod = ReflectionHelper.GetRequiredMethod(mapperType, "Map", new[] { sourceType, targetType, actionType });
        var srcParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "src");
        var tgtParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "tgt");
        var castSrc = System.Linq.Expressions.Expression.Convert(srcParam, sourceType);
        var castTgt = System.Linq.Expressions.Expression.Convert(tgtParam, targetType);
        var call = System.Linq.Expressions.Expression.Call(mapMethod, castSrc, castTgt,
            System.Linq.Expressions.Expression.Constant(null, actionType));
        return System.Linq.Expressions.Expression.Lambda<Action<object, object>>(call, srcParam, tgtParam).Compile();
    }
}
