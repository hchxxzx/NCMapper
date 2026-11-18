using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

using NCMapper.Internal;
namespace NCMapper;

/// <summary>
/// 深拷贝表达式树编译器 — 共享工具类，供 <see cref="ExpressionTreeDeepCopyProvider"/> 和 <see cref="FastDeepCopyProvider"/> 共用
/// <para>提取此类以消除 FastDeepCopyProvider 对兄弟具体类 ExpressionTreeDeepCopyProvider 的直接依赖（DIP 修复）</para>
/// <para>为每种类型编译两个委托：</para>
/// <para>1. <c>CreateInstance</c> — new T() 创建空实例（阶段1）</para>
/// <para>2. <c>FillProperties</c> — 逐属性深拷贝（阶段2，引用类型属性递归调用 DeepCopy）</para>
/// </summary>
internal static class DeepCopyCompiler
{
    // CacheRegistry 登记制自注册：仅登记清理委托，不触发缓存构建（防自触发）
    static DeepCopyCompiler()
    {
        CacheRegistry.Register("deep-copy-compiler", CacheLayer.Full, ClearCache);
    }

    /// <summary>
    /// 创建实例委托缓存 — key: 类型, value: Lazy&lt;Delegate?&gt;（阶段1）
    /// <para>使用 Lazy 防止并发 GetOrAdd 时 valueFactory 重复执行编译</para>
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Lazy<Delegate?>> s_createCache = new();

    /// <summary>
    /// 填充属性委托缓存 — key: 类型, value: Lazy&lt;Action&lt;object, object, DeepCopyContext&gt;?&gt;（阶段2）
    /// <para>使用 Lazy 防止并发 GetOrAdd 时 valueFactory 重复执行编译</para>
    /// <para>方向 I 优化：委托签名加 DeepCopyContext 参数，内部递归 DeepCopy 调用复用 ctx
    /// 保持循环引用检测有效，替代 [ThreadStatic] 字段。</para>
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Lazy<Action<object, object, DeepCopyContext>?>> s_fillCache = new();

    /// <summary>
    /// 集合深拷贝委托缓存 — key: 类型, value: Lazy&lt;Func&lt;object, DeepCopyContext, object?&gt;?&gt;（编译后的 CollectionCopier 调用）
    /// <para>使用 Lazy 防止并发 GetOrAdd 时 valueFactory 重复执行编译</para>
    /// <para>方向 I 优化：委托签名加 DeepCopyContext 参数，集合元素深拷贝复用 ctx
    /// 保持循环引用检测有效（A→List&lt;B&gt;→B.Parent→A 不栈溢出）。</para>
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Lazy<Func<object, DeepCopyContext, object?>?>> s_collectionCopyCache = new();

    /// <summary>
    /// 创建实例的简化入口，自动创建深拷贝上下文（循环引用检测在递归调用中通过上下文生效）。
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="source">源对象</param>
    /// <returns>创建的新实例（浅拷贝阶段）</returns>
    [RequiresDynamicCode("DeepCopy uses Expression.Compile() and reflection in the runtime fallback path. Not supported in AOT. Use the source generator to ensure AOT compatibility.")]
    [RequiresUnreferencedCode("DeepCopy scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Use the source generator to ensure AOT compatibility.")]
    internal static T CreateInstanceCore<T>(T source) where T : class
        => CreateInstanceCore<T>(source, new DeepCopyContext());

    /// <summary>
    /// 内部入口 — 创建实例（循环引用检测通过传入的 DeepCopyContext 在递归中生效）
    /// <para>回退顺序：编译委托 → 集合拷贝委托 → FormatterServices（无构造器）→ JSON（最终兜底）</para>
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="source">源对象</param>
    /// <param name="ctx">深拷贝上下文</param>
    /// <returns>创建的新实例（浅拷贝阶段）</returns>
    [RequiresDynamicCode("DeepCopy uses Expression.Compile() and reflection in the runtime fallback path. Not supported in AOT. Use the source generator to ensure AOT compatibility.")]
    [RequiresUnreferencedCode("DeepCopy scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Use the source generator to ensure AOT compatibility.")]
    internal static T CreateInstanceCore<T>(T source, DeepCopyContext ctx) where T : class
    {
        if (typeof(T) == typeof(string))
            return source;

        var creator = s_createCache.GetOrAdd(typeof(T), t => new Lazy<Delegate?>(() => CompileCreator<T>(t))).Value;
        if (creator != null)
            return ((Func<T, DeepCopyContext, T>)creator)(source, ctx);

        var collectionCopier = s_collectionCopyCache.GetOrAdd(typeof(T), t => new Lazy<Func<object, DeepCopyContext, object?>?>(() => CompileCollectionCopy(t))).Value;
        if (collectionCopier != null)
            return (T)collectionCopier(source!, ctx)!;

        // 具体集合类型兜底守卫 —— 宁可 MapException 不静默。
        // 走到此处（无编译委托且无集合拷贝委托）的具体集合类型（如非泛型字典派生类 DictionaryBase，
        // GetCollectionElementType 字典排除后返回 null）原经 GetUninitializedObject 兜底只能产出
        // 内部状态未初始化的集合空壳（数据静默丢失、后续 Add 抛 NRE 远离拷贝现场），改抛显式异常。
        // 接口/抽象类型不拦（执行期补充，差异登记 D1）：GetUninitializedObject 对其抛
        // MemberAccessException → JSON 回退可行（保持既有行为；Fast 分发层已将接口
        // 统一转发 base 接口转发入口，接口正常不达此处——本条件为纵深防御）
        if (IsCollectionType(typeof(T)) && !typeof(T).IsInterface && !typeof(T).IsAbstract)
            throw new MapException(
                ErrorMessages.UnknownCollectionType(typeof(T).FullName!, "<deep copy>"),
                typeof(T), typeof(T));

        // 无编译委托且非集合 → 用 FormatterServices 创建未初始化实例（不调用构造器）
        try
        {
            return (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));
        }
        catch (Exception ex)
        {
            MapTracer.Write($"[NCMapper] FormatterServices.GetUninitializedObject 失败: {typeof(T).Name}: {ex.Message}");
            try
            {
                var json = JsonSerializerBase.Current.Serialize(source);
                return (T?)JsonSerializerBase.Current.Deserialize(json, typeof(T))!;
            }
            catch (Exception jsonEx)
            {
                // 所有深拷贝路径均失败，抛出异常而非返回原引用，避免违反深拷贝契约
                throw new MapException(
                    ErrorMessages.DeepCopyAllFallbacksFailed(typeof(T).Name, ex.Message, jsonEx.Message),
                    typeof(T), typeof(T), jsonEx);
            }
        }
    }

    /// <summary>
    /// 填充属性的简化入口，自动创建深拷贝上下文（循环引用检测在递归调用中通过上下文生效）。
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="source">源对象</param>
    /// <param name="target">目标对象</param>
    [RequiresDynamicCode("DeepCopy uses Expression.Compile() and reflection in the runtime fallback path. Not supported in AOT. Use the source generator to ensure AOT compatibility.")]
    [RequiresUnreferencedCode("DeepCopy scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Use the source generator to ensure AOT compatibility.")]
    internal static void FillPropertiesCore<T>(T source, T target) where T : class
        => FillPropertiesCore<T>(source, target, new DeepCopyContext());

    /// <summary>
    /// 内部入口 — 填充属性（循环引用检测通过传入的 DeepCopyContext 在递归中生效）
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="source">源对象</param>
    /// <param name="target">目标对象</param>
    /// <param name="ctx">深拷贝上下文</param>
    [RequiresDynamicCode("DeepCopy uses Expression.Compile() and reflection in the runtime fallback path. Not supported in AOT. Use the source generator to ensure AOT compatibility.")]
    [RequiresUnreferencedCode("DeepCopy scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Use the source generator to ensure AOT compatibility.")]
    internal static void FillPropertiesCore<T>(T source, T target, DeepCopyContext ctx) where T : class
    {
        if (typeof(T) == typeof(string))
            return;

        var filler = s_fillCache.GetOrAdd(typeof(T), t => new Lazy<Action<object, object, DeepCopyContext>?>(() => CompileFiller<T>(t))).Value;
        if (filler == null)
            return;

        filler(source, target, ctx);
    }

    /// <summary>
    /// 编译阶段1委托：创建实例
    /// <para>对于有无参构造器的 POCO → new T()</para>
    /// <para>对于集合类型 → 返回 null，由 CompileCollectionCopy 处理</para>
    /// <para>对于无无参构造器但有带参构造器的类型（record 等）→ 通过构造器参数匹配 + DeepCopy 创建完整实例</para>
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="type">运行时类型</param>
    /// <returns>创建实例的委托；null 表示需回退</returns>
    [RequiresDynamicCode("CompileCreator uses Expression.Compile() and reflection to build delegates. Not supported in AOT.")]
    [RequiresUnreferencedCode("CompileCreator scans type members and attributes via reflection. Some members may be trimmed.")]
    private static Delegate? CompileCreator<T>(Type type) where T : class
    {
        if (IsCollectionType(type))
        {
            // 非泛型集合派生类（class OrderList : List<string>、
            // 自引用容器 class SelfRefList : List<SelfRefList>）补两阶段路径 —— 有无参构造且
            // 元素类型（IEnumerable<T> 接口链）或 K/V（IDictionary<K,V> 接口链）可提取时返回
            // new T() 空容器委托，元素填充交 CompileFiller；基类在 CreateInstance 返回后写入
            // CycleCache，随后填充阶段的元素递归命中缓存 → 自引用环被打破，拷贝完整且环回边
            // 引用一致（T1 Red_B01 copyCheck 转绿节点）。泛型集合维持
            // CompileCollectionCopy 单阶段（既有行为不变）；提取不到元素/K-V 的形态（DictionaryBase
            // 等）返回 null 落 CreateInstanceCore 兜底守卫
            if (type.IsGenericType || type.GetConstructor(Type.EmptyTypes) == null
                || (NavigationResolver.GetCollectionElementType(type) == null && GetDictionaryKeyValueInterface(type) == null))
                return null;
            var srcParam = Parameter(type, "src");
            var ctxParam = Parameter(typeof(DeepCopyContext), "ctx");
            var body = New(type.GetConstructor(Type.EmptyTypes)!);
            return Lambda<Func<T, DeepCopyContext, T>>(body, srcParam, ctxParam).Compile();
        }

        var defaultCtor = type.GetConstructor(Type.EmptyTypes);
        if (defaultCtor != null)
        {
            var srcParam = Parameter(type, "src");
            var ctxParam = Parameter(typeof(DeepCopyContext), "ctx");
            var body = New(defaultCtor);
            // 方向 I：委托签名加 ctx 占位参数（POCO new T() 不使用 ctx，但统一签名以便 s_createCache 存储）
            return Lambda<Func<T, DeepCopyContext, T>>(body, srcParam, ctxParam).Compile();
        }

        var ctorWithArgs = TryCompileRecordCreator<T>(type);
        return ctorWithArgs;
    }

    /// <summary>
    /// 为无无参构造器的类型（record 等）编译构造器参数匹配委托
    /// <para>按参数名（大小写不敏感）从源对象属性匹配，引用类型参数调 DeepCopy</para>
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="type">运行时类型</param>
    /// <returns>创建实例的委托；null 表示无可用构造器</returns>
    [RequiresDynamicCode("TryCompileRecordCreator uses Expression.Compile() and reflection to build delegates. Not supported in AOT.")]
    [RequiresUnreferencedCode("TryCompileRecordCreator scans type members and attributes via reflection. Some members may be trimmed.")]
    private static Delegate? TryCompileRecordCreator<T>(Type type) where T : class
    {
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (ctors.Length == 0)
            return null;

        var sourceProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        // 方向 I：找 internal virtual DeepCopy<T>(T source, DeepCopyContext ctx) 重载（带 ctx 参数）
        // ReflectionHelper.GetRequiredMethod 无法按参数类型查找 internal 方法，用 GetMethods + LINQ
        var deepCopyMethod = typeof(DeepCopyProviderBase)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .First(m => m.Name == nameof(DeepCopyProviderBase.DeepCopy)
                && m.IsGenericMethod
                && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType == typeof(DeepCopyContext));
        var currentAccess = Property(null, ReflectionHelper.GetRequiredProperty(typeof(DeepCopyProviderBase), nameof(DeepCopyProviderBase.Current)));

        ConstructorInfo? bestCtor = null;
        Expression[]? bestArgs = null;
        int bestMatched = -1;
        // 记录首个"按名匹配但类型不兼容"的构造器详情（供全部不可用时抛异常）
        string? bestIncompatDetail = null;

        var srcParam = Parameter(type, "src");
        var ctxParam = Parameter(typeof(DeepCopyContext), "ctx");

        foreach (var ctor in ctors)
        {
            var parameters = ctor.GetParameters();
            var args = new Expression[parameters.Length];
            int matched = 0;
            bool incompatible = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramName = parameters[i].Name!;
                // ByRef/指针参数形态——未匹配时
                // Default(ByRef) 构建期裸抛 ArgumentException（与映射侧 MapperBase
                // BuildConstructorArgs 同形态），标记该构造器不可用，全部不可用时由
                // B2 兜底抛 MapConfigurationException（异常契约统一；范围守卫：深拷贝侧
                // 同类缺陷顺带修复，登记）。
                if (parameters[i].ParameterType.IsByRef || parameters[i].ParameterType.IsPointer)
                {
                    incompatible = true;
                    bestIncompatDetail ??=
                        $"{type.Name}..ctor({parameters[i].ParameterType.Name}) 含 ByRef/指针参数，无法深拷贝构造";
                    break;
                }
                if (sourceProps.TryGetValue(paramName, out var srcProp))
                {
                    var srcAccess = Property(srcParam, srcProp);
                    var paramType = parameters[i].ParameterType;

                    if (IsSimpleType(paramType))
                    {
                        var srcType = srcProp.PropertyType;
                        if (srcType == paramType)
                        {
                            args[i] = srcAccess;
                        }
                        else if (IsCtorArgConvertible(srcType, paramType))
                        {
                            // 兼容转换：隐式拓宽/枚举/可空提升/显式数值/引用向上，Expression.Convert 均合法
                            args[i] = Convert(srcAccess, paramType);
                        }
                        else
                        {
                            // B2类型不兼容不再无条件 Convert（构建期崩 + Lazy 不缓存异常每次重编译），
                            // 记录详情并跳过该构造器，全部不可用时抛 MapConfigurationException
                            incompatible = true;
                            bestIncompatDetail ??=
                                $"{type.Name}.{srcProp.Name}({srcType.Name}) → {type.Name}..ctor({paramType.Name})";
                            break;
                        }
                    }
                    else if (paramType.IsValueType)
                    {
                        // struct 构参——同 FillPropertiesCore
                        // 值类型分支口径（装箱走对象通道 + 拆箱，免 null 条件表达式）
                        var boxedCtorArg = Convert(srcAccess, typeof(object));
                        args[i] = Unbox(Call(GetDeepCopyObjectCoreWithContext(), boxedCtorArg, ctxParam), paramType);
                    }
                    else
                    {
                        var genericMethod = deepCopyMethod.MakeGenericMethod(paramType);
                        // 方向 I：递归 DeepCopy 调用带 ctx 参数，复用顶层 ctx 保持循环引用检测有效
                        var deepCopyCall = Call(currentAccess, genericMethod, srcAccess, ctxParam);
                        args[i] = Condition(
                            NotEqual(srcAccess, Constant(null, paramType)),
                            deepCopyCall,
                            Constant(null, paramType));
                    }
                    matched++;
                }
                else
                {
                    // 未按名匹配的参数——HasDefaultValue 时用
                    // 构造器声明默认值（原静默 Default(null/0) 与默认值语义可能不一致：如
                    // ctor(int id = -1) 被填 0）；无默认值时保持 Default 填充 + Trace 可观测
                    //（"无匹配"为可恢复形态，B2 已对"按名匹配但类型不兼容"抛 MapConfigurationException）。
                    if (parameters[i].HasDefaultValue)
                        args[i] = Constant(parameters[i].DefaultValue, parameters[i].ParameterType);
                    else
                    {
                        MapTracer.Write($"[NCMapper] DeepCopyCompiler: 构造器参数 {type.Name}.{parameters[i].Name}({parameters[i].ParameterType.Name}) 无匹配源属性且无默认值，深拷贝将填充类型默认值");
                        args[i] = Default(parameters[i].ParameterType);
                    }
                }
            }

            if (incompatible)
                continue;

            if (matched == parameters.Length)
            {
                bestCtor = ctor;
                bestArgs = args;
                break;
            }

            if (matched > bestMatched)
            {
                bestCtor = ctor;
                bestArgs = args;
                bestMatched = matched;
            }
        }

        // B2所有构造器均因类型不兼容不可用时，抛带类型上下文的配置异常（不静默降级）
        if (bestCtor == null && bestIncompatDetail != null)
        {
            throw new MapConfigurationException(
                ErrorMessages.DeepCopyCtorIncompatible(bestIncompatDetail),
                type, type);
        }

        if (bestCtor == null || bestArgs == null)
            return null;

        var body = New(bestCtor, bestArgs);
        // 方向 I：委托签名加 ctx 参数，递归 DeepCopy 调用复用 ctx
        return Lambda<Func<T, DeepCopyContext, T>>(body, srcParam, ctxParam).Compile();
    }

    /// <summary>
    /// 编译集合深拷贝委托 — 根据集合类型选择 CollectionCopier 的对应方法
    /// </summary>
    /// <param name="type">集合类型</param>
    /// <returns>集合深拷贝委托；null 表示不支持的集合类型</returns>
    [RequiresDynamicCode("CompileCollectionCopy uses Expression.Compile() and reflection to build delegates. Not supported in AOT.")]
    [RequiresUnreferencedCode("CompileCollectionCopy scans type members and attributes via reflection. Some members may be trimmed.")]
    private static Func<object, DeepCopyContext, object?>? CompileCollectionCopy(Type type)
    {
        if (!TryGetCollectionCopyMethod(type, out var method, out var genericArgs))
            return null;

        var genericMethod = method.MakeGenericMethod(genericArgs);

        var srcParam = Parameter(typeof(object), "src");
        var ctxParam = Parameter(typeof(DeepCopyContext), "ctx");
        var castParam = Convert(srcParam, type);
        // 方向 I：传递 ctx 给 CollectionCopier 带 ctx 方法，集合元素深拷贝复用 ctx
        var call = Call(genericMethod, castParam, ctxParam);
        var castResult = Convert(call, typeof(object));
        return Lambda<Func<object, DeepCopyContext, object?>>(castResult, srcParam, ctxParam).Compile();
    }

    /// <summary>
    /// 根据集合类型查找对应的 CollectionCopier 方法及泛型参数
    /// </summary>
    /// <param name="type">集合类型</param>
    /// <param name="method">匹配到的 MethodInfo</param>
    /// <param name="genericArgs">泛型参数数组</param>
    /// <returns>true 表示找到匹配方法；false 表示不支持的集合类型</returns>
    [RequiresUnreferencedCode("TryGetCollectionCopyMethod scans type members and attributes via reflection. Some members may be trimmed.")]
    private static bool TryGetCollectionCopyMethod(Type type, out MethodInfo method, out Type[] genericArgs)
    {
        method = null!;
        genericArgs = null!;
        var ccType = typeof(CollectionCopier);

        if (type.IsArray)
        {
            method = ReflectionHelper.GetRequiredMethod(ccType, nameof(CollectionCopier.DeepCopyArray));
            genericArgs = new[] { type };
            return true;
        }

        if (!type.IsGenericType)
        {
            // 非泛型集合派生类（class OrderList : List<string>）
            // 不走本方法的单阶段集合拷贝委托——单阶段委托（CopyEnumerable 等）的元素递归发生在
            // 基类 CycleCache 写入之前，自引用容器（class B : List<B>，T1 的 copyCheck 转绿节点）
            // 将无限递归/JSON 回退遇环即炸；改由 CompileCreator/CompileFiller 两阶段路径承接
            //（空容器 → 基类入缓存 → 逐元素填充，元素递归时命中缓存打破环）。
            // 元素类型/K-V 均不可提取的形态（DictionaryBase 等非泛型字典派生）→ return false
            // 落 CreateInstanceCore 兜底守卫（宁可 MapException 不静默）
            return false;
        }
        var def = type.GetGenericTypeDefinition();

        // 双值集合（Dictionary 系列）
        if (def == typeof(Dictionary<,>))
            method = ReflectionHelper.GetRequiredMethod(ccType, nameof(CollectionCopier.CopyDictionary));
        else if (def == typeof(ConcurrentDictionary<,>))
            method = ReflectionHelper.GetRequiredMethod(ccType, nameof(CollectionCopier.CopyConcurrentDictionary));
        // 单值集合（List/HashSet/Queue/Stack 等）
        else if (def == typeof(List<>))
            method = ReflectionHelper.GetRequiredMethod(ccType, nameof(CollectionCopier.DeepCopyList));
        else if (def == typeof(HashSet<>))
            method = ReflectionHelper.GetRequiredMethod(ccType, nameof(CollectionCopier.CopyHashSet));
        else if (def == typeof(Queue<>))
            method = ReflectionHelper.GetRequiredMethod(ccType, nameof(CollectionCopier.CopyQueue));
        else if (def == typeof(Stack<>))
            method = ReflectionHelper.GetRequiredMethod(ccType, nameof(CollectionCopier.CopyStack));
        else if (def == typeof(ConcurrentQueue<>))
            method = ReflectionHelper.GetRequiredMethod(ccType, nameof(CollectionCopier.CopyConcurrentQueue));
        else if (def == typeof(ConcurrentStack<>))
            method = ReflectionHelper.GetRequiredMethod(ccType, nameof(CollectionCopier.CopyConcurrentStack));
        else if (def == typeof(ReadOnlyCollection<>))
            method = ReflectionHelper.GetRequiredMethod(ccType, nameof(CollectionCopier.CopyReadOnlyCollection));
        // 通用回退：单值泛型 + IEnumerable
        else if (type.GetGenericArguments().Length == 1 && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        {
            method = ReflectionHelper.GetRequiredMethod(ccType, nameof(CollectionCopier.CopyEnumerable));
            genericArgs = new[] { type };
            return true;
        }
        // 通用回退：双值泛型 + IDictionary
        else if (type.GetGenericArguments().Length == 2 && typeof(System.Collections.IDictionary).IsAssignableFrom(type))
        {
            method = ReflectionHelper.GetRequiredMethod(ccType, nameof(CollectionCopier.CopyDictionaryByKeyValue));
            genericArgs = new[] { type };
            return true;
        }
        else
            return false;

        genericArgs = type.GetGenericArguments();
        return true;
    }

    /// <summary>
    /// 编译阶段2委托：逐属性深拷贝
    /// <para>值类型/string → 直接赋值；引用类型 → 递归调用 DeepCopy</para>
    /// <para>无无参构造器的类型（record/带参构造类）不再
    /// 短路返回 null——构参外可写属性（CanWrite，含普通 set 与 init-only）经本填充器补填；
    /// getter-only 构参属性天然被 CanWrite 过滤跳过（其值由构造器设置、经 占位回拷承接）。</para>
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="type">运行时类型</param>
    /// <returns>填充属性委托；null 表示无需填充（集合路径）</returns>
    [RequiresDynamicCode("CompileFiller uses Expression.Compile() and reflection to build delegates. Not supported in AOT.")]
    [RequiresUnreferencedCode("CompileFiller scans type members and attributes via reflection. Some members may be trimmed.")]
    private static Action<object, object, DeepCopyContext>? CompileFiller<T>(Type type) where T : class
    {
        if (IsCollectionType(type))
        {
            // 非泛型集合派生类的阶段2填充委托（与 CompileCreator
            // 的 分支配对）—— 逐元素/逐 K-V 深拷贝填充到阶段1创建的空容器；元素递归发生在
            // 基类 CycleCache 写入之后，自引用容器经缓存命中打破环。泛型集合维持 null（走
            // CompileCollectionCopy 单阶段，无阶段2）
            // 数组排除（回归修复，实测回归：FastDeepCopy_Collection_Independent 落红——
            // int[] 等数组同为非泛型集合且 GetCollectionElementType 可提取元素，但数组固定
            // 大小（SZArrayHelper.Add 抛 NotSupportedException）——数组完整拷贝由
            // TryGetCollectionCopyMethod 数组分支（DeepCopyArray 单阶段）承接，无阶段2填充)
            if (!type.IsGenericType && !type.IsArray)
            {
                var elemType = NavigationResolver.GetCollectionElementType(type);
                if (elemType != null)
                    return CompileNonGenericCollectionFiller<T>(type, elemType);
                var dictInterface = GetDictionaryKeyValueInterface(type);
                if (dictInterface != null)
                    return CompileNonGenericDictionaryFiller<T>(type, dictInterface);
            }
            return null;
        }

        var srcParam = Parameter(typeof(object), "src");
        var tgtParam = Parameter(typeof(object), "tgt");
        var ctxParam = Parameter(typeof(DeepCopyContext), "ctx");
        var srcCast = Convert(srcParam, type);
        var tgtCast = Convert(tgtParam, type);

        var expressions = new List<Expression>();

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead && p.CanWrite);

        // 方向 I：internal virtual DeepCopy<T>(T source, DeepCopyContext ctx) 重载（带 ctx 参数）——
        // 取样单点化于 DeepCopyProviderBase.ResolveContextualDeepCopyMethod
        var deepCopyMethod = DeepCopyProviderBase.ResolveContextualDeepCopyMethod();

        foreach (var prop in props)
        {
            if (prop.IsDefined(typeof(Attributes.MapIgnoreAttribute)))
                continue;

            var propType = prop.PropertyType;
            var srcAccess = Property(srcCast, prop);
            var tgtAccess = Property(tgtCast, prop);

            if (IsSimpleType(propType))
            {
                expressions.Add(Assign(tgtAccess, srcAccess));
            }
            else if (propType.IsValueType)
            {
                // 含可变引用成员的 struct 属性——装箱走
                // DeepCopyObjectCore（对象通道对非叶 struct 分发 CopyStructBoxed 逐成员深拷贝，
                // 引用成员复用编译委托缓存）+ 拆箱回写（值语义落位）；值类型无 null 态，
                // 免引用分支的 null 条件表达式（Constant(null, 值类型) 构建期抛 ArgumentException）
                var boxed = Convert(srcAccess, typeof(object));
                var coreCall = Call(GetDeepCopyObjectCoreWithContext(), boxed, ctxParam);
                expressions.Add(Assign(tgtAccess, Unbox(coreCall, propType)));
            }
            else
            {
                var genericMethod = deepCopyMethod.MakeGenericMethod(propType);
                var currentAccess = Property(null, typeof(DeepCopyProviderBase)
                    .GetProperty(nameof(DeepCopyProviderBase.Current))!);
                // 方向 I：递归 DeepCopy 调用带 ctx 参数，复用顶层 ctx 保持循环引用检测有效
                var deepCopyCall = Call(currentAccess, genericMethod, srcAccess, ctxParam);

                var condition = Condition(
                    NotEqual(srcAccess, Constant(null, propType)),
                    deepCopyCall,
                    Constant(null, propType));
                expressions.Add(Assign(tgtAccess, condition));
            }
        }

        var body = Block(expressions);
        // 方向 I：委托签名加 ctx 参数，递归 DeepCopy 调用复用 ctx
        return Lambda<Action<object, object, DeepCopyContext>>(body, srcParam, tgtParam, ctxParam).Compile();
    }

    /// <summary>
    /// 提取非泛型字典派生类的 IDictionary&lt;K,V&gt; 接口
    /// <para>自引用字典容器（class SelfRefDict : Dictionary&lt;string, SelfRefDict&gt;）等"继承泛型
    /// 字典的非泛型类"沿接口链提取 K/V 泛型参数；仅实现非泛型 IDictionary 的形态（DictionaryBase
    /// 派生等，K/V 无法静态提取）返回 null → 落 CreateInstanceCore 兜底守卫。</para>
    /// </summary>
    /// <param name="type">非泛型字典派生类型</param>
    /// <returns>IDictionary&lt;K,V&gt; 接口类型；不可提取时返回 null</returns>
    private static Type? GetDictionaryKeyValueInterface(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                return iface;
        }
        return null;
    }

    /// <summary>
    /// 编译非泛型集合派生类的元素填充委托（阶段2，与 CompileCreator 的 分支配对）
    /// </summary>
    /// <typeparam name="T">集合派生类型（编译期签名统一用）</typeparam>
    /// <param name="type">运行时集合派生类型</param>
    /// <param name="elemType">经接口链提取的元素类型</param>
    /// <returns>逐元素深拷贝填充委托</returns>
    [RequiresDynamicCode("CompileNonGenericCollectionFiller uses MakeGenericMethod. Not supported in AOT.")]
    [RequiresUnreferencedCode("CompileNonGenericCollectionFiller scans type members via reflection. Some members may be trimmed.")]
    private static Action<object, object, DeepCopyContext> CompileNonGenericCollectionFiller<T>(Type type, Type elemType) where T : class
    {
        var method = typeof(DeepCopyCompiler)
            .GetMethod(nameof(FillNonGenericCollectionElements), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type, elemType);
        var srcParam = Parameter(typeof(object), "src");
        var tgtParam = Parameter(typeof(object), "tgt");
        var ctxParam = Parameter(typeof(DeepCopyContext), "ctx");
        var call = Call(method, Convert(srcParam, type), Convert(tgtParam, type), ctxParam);
        return Lambda<Action<object, object, DeepCopyContext>>(call, srcParam, tgtParam, ctxParam).Compile();
    }

    /// <summary>
    /// 编译非泛型字典派生类的 K/V 填充委托（阶段2，与 CompileCreator 的 分支配对）
    /// </summary>
    /// <typeparam name="T">字典派生类型（编译期签名统一用）</typeparam>
    /// <param name="type">运行时字典派生类型</param>
    /// <param name="dictInterface">经接口链提取的 IDictionary&lt;K,V&gt; 接口</param>
    /// <returns>逐 K/V 深拷贝填充委托</returns>
    [RequiresDynamicCode("CompileNonGenericDictionaryFiller uses MakeGenericMethod. Not supported in AOT.")]
    [RequiresUnreferencedCode("CompileNonGenericDictionaryFiller scans type members via reflection. Some members may be trimmed.")]
    private static Action<object, object, DeepCopyContext> CompileNonGenericDictionaryFiller<T>(Type type, Type dictInterface) where T : class
    {
        var genArgs = dictInterface.GetGenericArguments();
        var method = typeof(DeepCopyCompiler)
            .GetMethod(nameof(FillNonGenericDictionaryEntries), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type, genArgs[0], genArgs[1]);
        var srcParam = Parameter(typeof(object), "src");
        var tgtParam = Parameter(typeof(object), "tgt");
        var ctxParam = Parameter(typeof(DeepCopyContext), "ctx");
        var call = Call(method, Convert(srcParam, type), Convert(tgtParam, type), ctxParam);
        return Lambda<Action<object, object, DeepCopyContext>>(call, srcParam, tgtParam, ctxParam).Compile();
    }

    /// <summary>
    /// 非泛型集合派生类的逐元素填充（阶段2实现）—— 值类型/string 元素直接加（值语义/不可变，
    /// 等价深拷贝），引用元素经 <see cref="DeepCopyProviderBase.DeepCopyObjectCore"/> 复用 ctx 深拷贝
    /// <para>调用时序保证：基类已将空容器写入 CycleCache → 自引用容器（class B : List&lt;B&gt;）的
    /// 元素递归命中缓存返回容器自身副本，环回边引用一致（T1 的 copyCheck 语义）。</para>
    /// </summary>
    /// <typeparam name="TColl">集合派生类型（实现 IEnumerable&lt;TElem&gt; 与 ICollection&lt;TElem&gt;）</typeparam>
    /// <typeparam name="TElem">经接口链提取的元素类型</typeparam>
    /// <param name="source">源集合</param>
    /// <param name="target">阶段1创建的空容器</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    [RequiresDynamicCode("FillNonGenericCollectionElements falls back to the runtime deep copy path. Not supported in AOT.")]
    [RequiresUnreferencedCode("FillNonGenericCollectionElements scans element type members via reflection in the runtime fallback path. Some members may be trimmed.")]
    private static void FillNonGenericCollectionElements<TColl, TElem>(TColl source, TColl target, DeepCopyContext ctx)
        where TColl : class, IEnumerable<TElem>
    {
        var targetCol = (ICollection<TElem>)target;
        // 值类型（含枚举/Nullable）/string：值语义或不可变，直接加即等价深拷贝
        if (typeof(TElem).IsValueType || typeof(TElem) == typeof(string))
        {
            foreach (var item in source)
                targetCol.Add(item);
            return;
        }
        foreach (var item in source)
            targetCol.Add(item == null ? default! : (TElem)DeepCopyProviderBase.DeepCopyObjectCore(item, ctx)!);
    }

    /// <summary>
    /// 非泛型字典派生类的逐 K/V 填充（阶段2实现）—— 简单 K/V 直接加，复杂 K/V 经
    /// <see cref="DeepCopyProviderBase.DeepCopyObjectCore"/> 复用 ctx 深拷贝（环语义同
    /// <see cref="FillNonGenericCollectionElements"/>：自引用字典容器 copy["self"]==copy）
    /// </summary>
    /// <typeparam name="TColl">字典派生类型（实现 IDictionary&lt;TKey,TValue&gt;）</typeparam>
    /// <typeparam name="TKey">经接口链提取的键类型</typeparam>
    /// <typeparam name="TValue">经接口链提取的值类型</typeparam>
    /// <param name="source">源字典</param>
    /// <param name="target">阶段1创建的空容器</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    [RequiresDynamicCode("FillNonGenericDictionaryEntries falls back to the runtime deep copy path. Not supported in AOT.")]
    [RequiresUnreferencedCode("FillNonGenericDictionaryEntries scans key/value type members via reflection in the runtime fallback path. Some members may be trimmed.")]
    private static void FillNonGenericDictionaryEntries<TColl, TKey, TValue>(TColl source, TColl target, DeepCopyContext ctx)
        where TColl : class, IDictionary<TKey, TValue>
        where TKey : notnull
    {
        var keySimple = typeof(TKey).IsValueType || typeof(TKey) == typeof(string);
        var valSimple = typeof(TValue).IsValueType || typeof(TValue) == typeof(string);
        foreach (var kv in source)
        {
            var key = keySimple || kv.Key == null
                ? kv.Key! // TKey:notnull——病态空键落 target.Add 抛 ArgumentNullException（宁可失败不静默）
                : (TKey)DeepCopyProviderBase.DeepCopyObjectCore(kv.Key, ctx)!;
            var value = valSimple || kv.Value == null
                ? kv.Value
                : (TValue)DeepCopyProviderBase.DeepCopyObjectCore(kv.Value, ctx)!;
            target.Add(key, value);
        }
    }

    /// <summary>
    /// 判断类型是否为"简单类型"（值类型/枚举/string/Nullable&lt;T&gt;）
    /// <para>含可变引用实例字段的 struct 不再作
    /// 叶子浅赋值（共享可变引用成员）——改判非简单，走 <see cref="CopyStructBoxed"/> 逐成员
    /// 深拷贝；仅运行时侧消费点改判，SourceGen 物化期不动（生成码零变化，G3 维持
    /// IDENTICAL）。</para>
    /// </summary>
    /// <param name="type">类型</param>
    /// <returns>简单类型返回 true</returns>
    [RequiresUnreferencedCode("IsSimpleType consults LeafStructGuard which scans struct fields via reflection. Some members may be trimmed.")]
    private static bool IsSimpleType(Type type)
    {
        if (!type.IsValueType) return type == typeof(string);
        return type.IsEnum || LeafStructGuard.IsLeafSafe(type);
    }

    /// <summary>
    /// 构造器参数类型兼容性判定
    /// <para>判定 Expression.Convert 是否合法：类型相等直传；隐式拓宽（<see cref="TypeConverter.IsImplicitConvertible"/>）、
    /// 枚举↔数值/枚举互转、Nullable 提升（T↔T?、底层可转换）、显式数值互转、引用向上转换（派生→基类/接口）
    /// 均为合法 Convert；其余不兼容返回 false，调用方抛 MapConfigurationException。</para>
    /// </summary>
    /// <param name="sourceType">源属性类型</param>
    /// <param name="targetType">构造器参数类型</param>
    /// <returns>可安全 Convert 返回 true，否则返回 false</returns>
    private static bool IsCtorArgConvertible(Type sourceType, Type targetType)
    {
        if (sourceType == targetType) return true;
        if (TypeConverter.IsImplicitConvertible(sourceType, targetType)) return true;
        // 枚举↔底层数值/枚举互转（显式转换，Expression.Convert 合法；IsImplicitConvertible 对枚举返回 false）。
        // 精确化：string↔enum 无转换操作符，必须判不兼容（抛 MapConfigurationException）
        if (sourceType.IsEnum)
            return targetType.IsEnum || IsNumericFamily(targetType);
        if (targetType.IsEnum)
            return IsNumericFamily(sourceType);
        // Nullable 提升：T↔T?、T?↔T?（underlying 间递归判定，含枚举/数值场景）
        var srcUnder = Nullable.GetUnderlyingType(sourceType);
        var tgtUnder = Nullable.GetUnderlyingType(targetType);
        if (srcUnder != null || tgtUnder != null)
            return IsCtorArgConvertible(srcUnder ?? sourceType, tgtUnder ?? targetType);
        // 显式数值转换（数值家族任意互转，如 long→int、double→int）
        if (IsNumericFamily(sourceType) && IsNumericFamily(targetType)) return true;
        // 引用类型向上转换（派生→基类/接口）；基类→派生无转换操作符，判不兼容
        if (!sourceType.IsValueType && !targetType.IsValueType && targetType.IsAssignableFrom(sourceType)) return true;
        return false;
    }

    /// <summary>
    /// 数值家族判定 — byte/sbyte/short/ushort/int/uint/long/ulong/float/double/decimal/char
    /// <para>数值家族任意互转在 Expression.Convert 中均有显式转换操作符；bool 与枚举不属于数值家族。</para>
    /// </summary>
    /// <param name="type">要判定的类型</param>
    /// <returns>数值类型返回 true，否则返回 false</returns>
    private static bool IsNumericFamily(Type type)
    {
        if (type.IsEnum) return false;
        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
            case TypeCode.Char:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 判断类型是否为集合类型（实现 IEnumerable 且非 string）— internal 供
    /// <see cref="FastDeepCopyProvider"/> 分发条件复用（与 CreateInstanceCore
    /// 的集合拷贝回退路径同口径：集合类型不走 TryCompileRecordCreator 构参路径）
    /// </summary>
    /// <param name="type">类型</param>
    /// <returns>集合类型返回 true</returns>
    internal static bool IsCollectionType(Type type)
    {
        return typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
            && type != typeof(string);
    }

    /// <summary>
    /// 清除所有深拷贝委托缓存 — 供 Mapper.ClearCache() 调用
    /// </summary>
    internal static void ClearCache()
    {
        s_createCache.Clear();
        s_fillCache.Clear();
        s_collectionCopyCache.Clear();
        s_structPlanCache.Clear();
    }

    // ================================================================
    // 含可变引用成员 struct 的运行时深拷贝通道
    // ================================================================

    /// <summary>
    /// DeepCopyObjectCore(object, DeepCopyContext) 解析 — A3-08 值类型分支发射用
    /// <para>方法组解析在带注解方法内惰性执行（静态字段初始化器无注解上下文，
    /// GetMethod 解析 RequiresUnreferencedCode 成员会报 IL2026——b24 门禁实测）；
    /// 幂等竞态无害（同值重复赋值）。</para>
    /// </summary>
    private static MethodInfo? s_deepCopyObjectCoreWithContextMi;

    [RequiresDynamicCode("Resolves and emits calls to DeepCopyObjectCore which builds compiled delegates. Not supported in AOT.")]
    [RequiresUnreferencedCode("Resolves DeepCopyObjectCore via method group; it scans type members via reflection. Some members may be trimmed.")]
    private static MethodInfo GetDeepCopyObjectCoreWithContext()
        => s_deepCopyObjectCoreWithContextMi ??= ((Func<object, DeepCopyContext, object?>)DeepCopyProviderBase.DeepCopyObjectCore).Method;

    /// <summary>struct 逐成员深拷贝计划缓存 — (字段, 是否深通道)</summary>
    private static readonly ConcurrentDictionary<Type, (FieldInfo Field, bool Deep)[]> s_structPlanCache = new();

    /// <summary>
    /// 装箱 struct 逐成员深拷贝——非叶 struct 的运行时拷贝通道
    /// <para>两阶段：GetUninitializedObject 占位 → 逐字段回填。深通道成员（可变引用/
    /// 嵌套非叶 struct）经 DeepCopyObjectCore 对象通道深拷贝（复用编译委托缓存与
    /// CycleCache 环检测）；值/叶引用（string/Delegate/叶白名单/叶安全 struct）直传
    /// ——与引用路径成员语义一致。readonly 字段经 FieldInfo 回写（backing-field
    /// 反射回写同通道，覆盖 getter-only 属性形态）。</para>
    /// <para>入口仅经 DeepCopyProviderBase.DeepCopyObjectCore 值类型分发抵达（装箱态），
    /// 拆箱回写由调用方发射（Unbox）完成。</para>
    /// </summary>
    /// <param name="boxedSource">装箱的 struct 源（非 null、运行时类型为 struct）</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx 保持循环引用检测有效）</param>
    /// <returns>装箱的深拷贝结果</returns>
    [RequiresDynamicCode("CopyStructBoxed recurses into DeepCopyObjectCore which builds compiled delegates. Not supported in AOT.")]
    [RequiresUnreferencedCode("CopyStructBoxed scans struct fields via reflection. Some members may be trimmed.")]
    internal static object? CopyStructBoxed(object boxedSource, DeepCopyContext ctx)
    {
        var type = boxedSource.GetType();
        var plan = s_structPlanCache.GetOrAdd(type, static t =>
        {
            var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var arr = new (FieldInfo Field, bool Deep)[fields.Length];
            for (var i = 0; i < fields.Length; i++)
            {
                var ft = fields[i].FieldType;
                bool deep = ft.IsValueType ? !IsSimpleType(ft) : !LeafStructGuard.IsLeafReference(ft);
                arr[i] = (fields[i], deep);
            }
            return arr;
        });
        var result = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        foreach (var (field, deep) in plan)
        {
            var v = field.GetValue(boxedSource);
            field.SetValue(result, deep && v != null ? DeepCopyProviderBase.DeepCopyObjectCore(v, ctx) : v);
        }
        return result;
    }
}
