using NCMapper.Attributes;
using NCMapper.Runtime;
using NCMapper.Internal;
using NCMapper.Shared;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;
namespace NCMapper;
/// <summary>
/// 自动映射核心类 — 类定义、静态字段、对外方法、委托编译
/// 注意：所有 static 字段声明集中在此文件，保证初始化顺序（partial class 跨文件不保证字段顺序）
/// <para><b>模式并存</b>：映射意图经 [MapFrom] 特性通道或 Fluent 入口通道
///（[MapperConfig] 类 + Configure 方法）声明，两通道产物统一注册到本运行时入口——
/// 对不同 (Source, Target) 类型对可混合使用；同一类型对双声明由编译期 NCMAP_F11 互斥禁止
///（详见 docs/zh-CN/how-to/23-Fluent-Mapping.md）。</para>
/// </summary>
/// <typeparam name="TSource">源对象</typeparam>
/// <typeparam name="TTarget">目标对象</typeparam>
internal partial class MapperBase<TSource, TTarget> where TTarget : class
{
    #region 定义 — 所有静态字段声明集中此处，保证初始化顺序

    // 特性类型 — 供 MapperBase 内部直接使用（PropertyMatcher 有独立副本）
    /// <summary>
    /// MapFromAttribute 类型引用
    /// </summary>
    private static readonly Type s_mapFromAttribute = typeof(MapFromAttribute);

    // 原 s_autoMapFieldAttribute 死字段删除
    // typeof(MapPropertyAttribute) 声明后零消费（全库 grep 证实），无消费方。

    /// <summary>
    /// 无返回值委托：更新委托（懒加载 — 避免类型初始化时触发 Expression.Compile() 影响 AOT）
    /// <para>使用 Lazy&lt;T&gt;(ExecutionAndPublication) 替代 ??=，消除多线程并发重复编译</para>
    /// <para>物化后不受 <see cref="Mapper.ClearCache"/> / 配置重冻结影响（限制详见 Mapper.ClearCache
    /// 文档）</para>
    /// </summary>
    // 静态字段初始化器归属 .cctor，无法用 Requires* 标注（CS0592/IL2116）；[UnconditionalSuppressMessage]
    // 仅作用于发布期 ILLink/ILC，对编译期分析器无效。此处 Lazy 初始化器调用已标注的
    // GetMapAction()（运行时回退路径），用 #pragma 精确抑制——MapAction 属性 getter 本身已带 Requires*。
#pragma warning disable IL2026 // RequiresUnreferencedCode
#pragma warning disable IL3050 // RequiresDynamicCode
    private static readonly Lazy<Action<TSource, TTarget>> s_mapActionLazy
        = new(() => GetMapAction(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
#pragma warning restore IL3050
#pragma warning restore IL2026
    /// <summary>
    /// 无返回值更新委托 — 将源对象的所有映射字段更新到目标对象（懒加载）
    /// </summary>
    public static Action<TSource, TTarget> MapAction
    {
        [RequiresDynamicCode("MapAction compiles a mapping delegate via Expression.Compile. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
        [RequiresUnreferencedCode("MapAction scans type members and attributes via reflection. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
        get => s_mapActionLazy.Value;
    }

    /// <summary>
    /// 返回值委托：编译 Lambda，内联创建对象 + MapAction（懒加载 — 避免类型初始化时触发 Expression.Compile() 影响 AOT）
    /// <para>B11支持无无参构造的类型（如 record），创建对象方式分流为 New(无参) / New(带参)</para>
    /// <para>使用 Lazy&lt;T&gt;(ExecutionAndPublication) 替代 ??=，消除多线程并发重复编译</para>
    /// </summary>
    // 同 s_mapActionLazy：.cctor 内调用已标注的 CreateMapFunc()，用 #pragma 精确抑制
#pragma warning disable IL2026 // RequiresUnreferencedCode
#pragma warning disable IL3050 // RequiresDynamicCode
    private static readonly Lazy<Func<TSource, TTarget>> s_mapFuncLazy
        = new(() => CreateMapFunc(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
#pragma warning restore IL3050
#pragma warning restore IL2026
    /// <summary>
    /// 返回值映射委托 — 构建新目标对象并从源复制所有映射字段，返回目标对象（懒加载）
    /// <para>支持无无参构造的类型（如 record），创建对象方式分流为 New(无参) / New(带参)</para>
    /// </summary>
    public static Func<TSource, TTarget> MapFunc
    {
        [RequiresDynamicCode("MapFunc compiles a mapping delegate via Expression.Compile. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
        [RequiresUnreferencedCode("MapFunc scans type members and attributes via reflection. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
        get => s_mapFuncLazy.Value;
    }

    /// <summary>
    /// 列表映射委托（懒加载 — 可能返回 null 表示类型不支持列表映射快速路径）
    /// <para>消除逐元素 MapFunc 委托调用层（100 元素省 100 次委托跳转）</para>
    /// </summary>
    // 同 s_mapActionLazy：.cctor 内调用已标注的 CreateMapListFunc()，用 #pragma 精确抑制
#pragma warning disable IL2026 // RequiresUnreferencedCode
#pragma warning disable IL3050 // RequiresDynamicCode
    private static readonly System.Lazy<Func<List<TSource>, List<TTarget>>?> s_mapListFuncLazy
        = new(() => CreateMapListFunc());
#pragma warning restore IL3050
#pragma warning restore IL2026
    private static Func<List<TSource>, List<TTarget>>? MapListFunc => s_mapListFuncLazy.Value;

    /// <summary>
    /// 数组映射委托（懒加载 — 可能返回 null 表示类型不支持数组映射快速路径）
    /// <para>消除逐元素 MapFunc 委托调用层，与 MapListFunc 对称</para>
    /// </summary>
    // 同 s_mapActionLazy：.cctor 内调用已标注的 CreateMapArrayFunc()，用 #pragma 精确抑制
#pragma warning disable IL2026 // RequiresUnreferencedCode
#pragma warning disable IL3050 // RequiresDynamicCode
    private static readonly System.Lazy<Func<TSource[], TTarget[]>?> s_mapArrayFuncLazy
        = new(() => CreateMapArrayFunc());
#pragma warning restore IL3050
#pragma warning restore IL2026
    private static Func<TSource[], TTarget[]>? MapArrayFunc => s_mapArrayFuncLazy.Value;

    /// <summary>
    /// 显式静态构造函数 — 委托工厂注册（④ 策略化深化）。
    /// <para>Register 前置于构造体首行——先于任何可能触发 ExpressionBuilder 调用的逻辑。
    /// 时序说明：字段初始化器（含 4 个 Lazy 字段）先于构造体体执行，但 Lazy&lt;T&gt;
    /// 初始化器仅构造 Lazy 对象、lambda 惰性求值不在 .cctor 期触发 ExpressionBuilder，
    /// 注册安全。</para>
    /// <para>ExpressionBuilder 3 处反射回指点按实际类型对经 MapperDelegateRegistry.Resolve
    /// 查询本注册；Resolve 未命中时 RunClassConstructor 触发本构造函数后重试一次——
    /// 覆盖编译期尚未触及的嵌套/集合元素类型对（现状无显式 .cctor、Lazy 懒加载，
    /// 嵌套类型对在编译期不会初始化，兜底为唯一注册触发路径）。</para>
    /// </summary>
    static MapperBase()
    {
        // v1.1：Register 前置——先于任何可能触发 ExpressionBuilder 调用的逻辑
        MapperDelegateRegistry.Register<TSource, TTarget>(new MapperBaseDelegateImpl<TSource, TTarget>());
    }

    /// <summary>
    /// 最大递归深度 — 超过此值视为循环引用，抛异常防止栈溢出
    /// <para>可通过 <see cref="Mapper.MaxRecursionDepth"/> 全局配置</para>
    /// </summary>
    private static int MaxRecursionDepth => Mapper.MaxRecursionDepth;
    /// <summary>
    /// 进入递归防护— 双计数器循环引用守卫；超限归零并抛 MapException（自清洁）。
    /// 泛型参数用于异常消息类型名；AggressiveInlining 保持热路径内联（禁用时零额外开销）。
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void EnterRecursion(Type sourceType, Type targetType)
    {
        s_depthIn++;
        if (s_depthIn - s_depthOut > MaxRecursionDepth)
        {
            s_depthIn = 0;
            s_depthOut = 0;
            throw new MapException(
                ErrorMessages.RecursionDepthExceeded(MaxRecursionDepth, sourceType.Name, targetType.Name),
                sourceType, targetType);
        }
    }

    /// <summary>
    /// 退出递归防护— 平衡 s_depthIn 计数
    /// <para>钳制：out 不得超过 in。深度超限时 EnterRecursion 归零双计数器并抛异常，
    /// 异常传播过程中间层（entered=true）的 finally 仍会调用本方法——若直接 ++，
    /// out 将越过归零后的 in 造成差值负数，深度检查（差值 &gt; MaxRecursionDepth）永久失效
    /// （回归， 修复）。钳制保证差值恒 ≥0，
    /// 正常路径 in/out 同步递增行为不变（分支几乎恒真，分支预测友好）。</para>
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void ExitRecursion()
    {
        // 正常路径 out < in 恒真（先 Enter 后 Exit）；异常归零后 out ≥ in 时不加，防止负差值
        if (s_depthOut < s_depthIn) s_depthOut++;
    }

    /// <summary>
    /// 进入计数器 — 每次 Map 调用 +1，无需 try-finally 保证回退
    /// </summary>
    [ThreadStatic]
    private static int s_depthIn;

    /// <summary>
    /// 退出计数器 — 每次 Map 调用退出时 +1（含异常路径：ExitRecursion 已移入 finally，B3 修复后异常也配对）
    /// <para>差值 s_depthIn - s_depthOut = 当前活跃递归深度</para>
    /// <para>in/out 同步配对，差值恒定，无异常残留（ExitRecursion 钳制见上）</para>
    /// <para>差值超过 MaxRecursionDepth 时重置归零并抛异常（自清洁）</para>
    /// </summary>
    [ThreadStatic]
    private static int s_depthOut;

    #endregion 定义

    #region 对外方法

    /// <summary>
    /// 将对象TSource转换为TTarget并返回
    /// </summary>
    /// <param name="source">源对象</param>
    /// <param name="action">方法，可在此处对对象进行后续操作，默认为空</param>
    /// <returns>转换后的目标对象；source 为 null 时返回 default</returns>
    /// <exception cref="MapException">映射递归深度超过上限时抛出</exception>
    [RequiresDynamicCode("Map uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Map scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public static TTarget? Map(TSource source, Action<TSource, TTarget>? action = null)
    {
        if (source == null) return default(TTarget);

        // 映射路径 ctx 生命周期协议（与 DeepCopy 顶层 对称）
        // Depth 共享计数：任何时刻仅最外层（Depth 归 0）执行 Clear；try-finally 保证异常安全
        var ctx = Mapper.GetOrCreateDeepCopyCtx();
        ctx.Depth++;
        // entered 守护——EnterRecursion 自身超限抛异常时
        // 双计数器已归零，finally 不得再 Exit（否则差值反向 -1）；仅正常返回后置 true
        bool entered = false;
        try
        {
            // 双计数器循环引用防护（H-02 收敛）
            EnterRecursion(typeof(TSource), typeof(TTarget));
            entered = true;
            var target = MapFunc(source);
            action?.Invoke(source, target);
            return target;
        }
        finally
        {
            // B3 修复：ExitRecursion 移入已有 finally——MapFunc/action 抛异常时差值不残留
            if (entered) ExitRecursion();
            ctx.Depth--;
            if (ctx.Depth == 0) ctx.CycleCache?.Clear();
        }
    }

    /// <summary>
    /// 更新指定的目标对象
    /// </summary>
    /// <param name="source">源对象</param>
    /// <param name="target">需要更新的目标对象</param>
    /// <param name="action">方法，可在此处对对象进行后续操作，默认为空</param>
    /// <exception cref="MapException">映射递归深度超过上限时抛出</exception>
    [RequiresDynamicCode("Map uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Map scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public static void Map(TSource source, TTarget target, Action<TSource, TTarget>? action = null)
    {
        if (source == null || target == null) return;

        // ctx 生命周期协议（同 Map 入口）
        var ctx = Mapper.GetOrCreateDeepCopyCtx();
        ctx.Depth++;
        // entered 守护（同 Map 入口，见上）
        bool entered = false;
        try
        {
            // 双计数器循环引用防护（H-02 收敛）
            EnterRecursion(typeof(TSource), typeof(TTarget));
            entered = true;
            MapAction(source, target);
            action?.Invoke(source, target);
        }
        finally
        {
            // B3 修复：ExitRecursion 移入已有 finally——MapAction/action 抛异常时差值不残留
            if (entered) ExitRecursion();
            ctx.Depth--;
            if (ctx.Depth == 0) ctx.CycleCache?.Clear();
        }
    }

    /// <summary>
    /// 列表映射 — 检测 ICollection 预分配容量并 foreach，消除 LINQ 迭代器链和动态扩容开销
    /// </summary>
    /// <param name="sources">源列表对象</param>
    /// <param name="action">方法，可在此处对对象进行后续操作，默认为空</param>
    /// <returns>转换后的目标列表；sources 为 null 时返回 null</returns>
    /// <exception cref="MapException">映射递归深度超过上限时抛出</exception>
    [RequiresDynamicCode("MapList uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("MapList scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public static List<TTarget>? MapList(IEnumerable<TSource> sources, Action<TSource, TTarget>? action = null)
    {
        if (sources == null) return null;

        // ctx 生命周期协议（同 Map 入口；内部元素经 MapFunc 直接映射不走 Map 入口，须在此统一）
        var ctx = Mapper.GetOrCreateDeepCopyCtx();
        ctx.Depth++;
        // entered 守护（同 Map 入口，见上）
        bool entered = false;
        try
        {
            // 双计数器循环引用防护（H-02 收敛）
            EnterRecursion(typeof(TSource), typeof(TTarget));
            entered = true;

            // 快速路径：List<TSource> + 无 action → 走编译的 MapListFunc（内联 for 循环）
            if (action == null && sources is List<TSource> listSource && MapListFunc != null)
            {
                var result = MapListFunc(listSource);
                return result;
            }

            // 快速路径：TSource[] + 无 action → 走编译的 MapArrayFunc（内联 for 循环），再转 List
            if (action == null && sources is TSource[] arraySource && MapArrayFunc != null)
            {
                var result = new List<TTarget>(MapArrayFunc(arraySource));
                return result;
            }

            // P1 优化：检测 ICollection 预分配容量，消除 LINQ 迭代器链和动态扩容开销
            //（原 MapListWithCapacity 删除，容量参数化并入 MapListSlow 唯一实现）
            if (sources is ICollection<TSource> collection)
            {
                var list = MapListSlow(collection, action, collection.Count);
                return list;
            }

            // 非 ICollection 路径：无法预知数量，走 foreach
            var slowList = MapListSlow(sources, action);
            return slowList;
        }
        finally
        {
            // B3四个分支内的 ExitRecursion 统一移入 finally，恰好执行一次（不得双重配对）
            if (entered) ExitRecursion();
            ctx.Depth--;
            if (ctx.Depth == 0) ctx.CycleCache?.Clear();
        }
    }

    /// <summary>
    /// 批量就地映射回退— 将 sources 逐元素填充进预分配 destination（仅覆盖映射字段）
    /// <para>ctx 生命周期协议与 MapList（:248-290）同构：ctx.Depth / EnterRecursion / ExitRecursion 各一次，
    /// 元素直接 MapAction 不走 Map 入口（逐元素调 MapperBase.Map 会增加每元素 ctx 协议固定开销，
    /// 未注册类型下 MapListInto 会慢于 MapList）。</para>
    /// <para>null 语义：src==null → 槽位保持原值；destination[i]==null → no-op 跳过。</para>
    /// </summary>
    /// <param name="sources">源集合；null 时直接返回（防御性——公共入口已校验）</param>
    /// <param name="destination">预分配目标数组；null 时直接返回（防御性——公共入口已校验）</param>
    /// <param name="action">方法，每个元素映射后调用 action(src, dst)，默认为空</param>
    /// <exception cref="MapException">映射递归深度超过上限时抛出</exception>
    [RequiresDynamicCode("MapListInto builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("MapListInto scans type members and attributes via reflection. Some members may be trimmed.")]
    public static void MapListInto(IList<TSource> sources, TTarget[] destination, Action<TSource, TTarget>? action = null)
    {
        // 公共入口已校验 destination.Length >= sources.Count（Mapper.MapListInto），此处防御性 return
        if (sources == null || destination == null) return;

        // ctx 生命周期协议（一次，与 MapList 同构）
        var ctx = Mapper.GetOrCreateDeepCopyCtx();
        ctx.Depth++;
        // entered 守护（同 Map 入口，见上）
        bool entered = false;
        try
        {
            // 双计数器循环引用防护（H-02 收敛）——批量就地同 MapList：防护一次，元素不重复进出
            EnterRecursion(typeof(TSource), typeof(TTarget));
            entered = true;

            for (int i = 0; i < sources.Count; i++)
            {
                var src = sources[i];
                if (src == null) continue;              // 槽位保持原值
                var dst = destination[i];
                if (dst == null) continue;              // no-op
                MapAction(src, dst);                    // 直接调编译委托（不走 Map 入口，避免每元素 ctx 协议开销）
                action?.Invoke(src, dst);
            }
        }
        finally
        {
            // B3 修复：ExitRecursion 移入 finally，恰好执行一次（不得双重配对）
            if (entered) ExitRecursion();
            ctx.Depth--;
            if (ctx.Depth == 0) ctx.CycleCache?.Clear();
        }
    }

    /// <summary>
    /// 非 ICollection 路径 — 无法预知数量，走 foreach
    /// <para>原 MapListWithCapacity/MapListSlow 孪生实现收敛
    /// 本方法容量参数化（capacity ≥ 0 时预分配，语义即原 WithCapacity 形态）成为唯一实现，
    /// 原 MapListWithCapacity 删除（循环体逐字相同，唯一差异 = List 预分配），
    /// ICollection 调用点改传 collection.Count。</para>
    /// </summary>
    /// <param name="sources">源可枚举对象</param>
    /// <param name="action">可选回调</param>
    /// <param name="capacity">已知容量（原 WithCapacity 路径传 collection.Count；未知传 -1 走默认增长）</param>
    /// <returns>目标列表</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    [RequiresDynamicCode("MapListSlow builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("MapListSlow scans type members and attributes via reflection. Some members may be trimmed.")]
    private static List<TTarget> MapListSlow(IEnumerable<TSource> sources, Action<TSource, TTarget>? action, int capacity = -1)
    {
        var list = capacity >= 0 ? new List<TTarget>(capacity) : new List<TTarget>();
        foreach (var item in sources)
        {
            if (item == null)
            {
                list.Add(null!);
                continue;
            }
            var info = MapFunc(item);
            action?.Invoke(item, info);
            list.Add(info);
        }
        return list;
    }
    #endregion 对外方法

    #region 委托编译

    /// <summary>
    /// 编译一个 Lambda：创建 TTarget → MapAction(src, tgt) → return tgt
    /// B11支持无无参构造的类型（如 record），通过构造器参数匹配创建实例
    /// 有无参构造 → New(Type) 现有路径；无则 → New(ConstructorInfo, args) 带参构造路径
    /// </summary>
    [RequiresDynamicCode("CreateMapFunc uses Expression.Compile() and reflection in the runtime fallback path. Not supported in AOT. Use the source generator to ensure AOT compatibility.")]
    [RequiresUnreferencedCode("CreateMapFunc scans type members and attributes via reflection. Some members may be trimmed. Use the source generator to ensure AOT compatibility.")]
    private static Func<TSource, TTarget> CreateMapFunc()
    {
        var srcParam = Parameter(typeof(TSource), "src");
        var tgtVar = Variable(typeof(TTarget), "tgt");

        // 检测目标类型有无无参公共构造函数
        var defaultCtor = typeof(TTarget).GetConstructor(Type.EmptyTypes);

        Expression newExpr;
        if (defaultCtor != null)
        {
            // 有无参构造 → 现有路径
            newExpr = New(defaultCtor);
        }
        else
        {
            // 无无参构造（record 主构造函数/带参构造）→ 匹配构造器参数
            var (ctor, args) = BuildConstructorArgs(typeof(TTarget), typeof(TSource), srcParam);
            newExpr = New(ctor, args);
        }

        // OPT: 尝试 MemberInit 优化 — 简单映射用 new T { Prop = src.Prop } 替代 var t = new T(); t.Prop = src.Prop;
        {
            var memberInitExpr = TryBuildMemberInit(srcParam, (NewExpression)newExpr);
            if (memberInitExpr != null)
                return Lambda<Func<TSource, TTarget>>(memberInitExpr, srcParam).Compile();
        }

        // Fallback：Block 表达式（复杂映射路径）
        var mapBodyExpressions = GetMapBody(srcParam, tgtVar);

        var body = Block(
            typeof(TTarget),
            new[] { tgtVar },
            Assign(tgtVar, newExpr),
            Block(mapBodyExpressions),
            tgtVar
        );
        return Lambda<Func<TSource, TTarget>>(body, srcParam).Compile();
    }

    /// <summary>
    /// 尝试构建 MemberInit 表达式 — 仅当所有属性映射都是简单赋值（同类型或隐式转换）时可用
    /// <para>生成: new T { Prop1 = src.Prop1, Prop2 = (T2)src.Prop2 }</para>
    /// <para>比 Block(var t = new T(); t.Prop = src.Prop; return t;) 更紧凑</para>
    /// <para>⚠️ 对齐检查点：ExpressionBuilder.BuildMapBody 新增特性（如 [Mask]、[Convert]）时，
    /// 必须在 TryBuildSingleBinding 中增加对应 shouldFallback 检测，否则本快捷路径将静默跳过该特性。</para>
    /// </summary>
    /// <returns>MemberInit 表达式；null 表示存在复杂映射，需回退 Block</returns>
    [RequiresDynamicCode("TryBuildMemberInit builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("TryBuildMemberInit scans type members and attributes via reflection. Some members may be trimmed.")]
    private static Expression? TryBuildMemberInit(ParameterExpression srcParam, NewExpression newExpr)
    {
        var sourceType = typeof(TSource);
        var targetType = typeof(TTarget);

        // H-1 白名单门（属性腿）——白名单对整对回退 Block 路径，由既有消费门
        //（ExpressionBuilder.cs:158-178）逐属性判定"未声明同名不配对"：语义与 Block 门
        // 绝对一致、零重复逻辑（MemberInit 快路径原无门，白名单可被绕过——腿①）。
        // 置于注册表腿上下文解析之前：白名单对免做一次 ctx 解析。
        if (RuntimeSnapshotStore.IsNoAutoMatch(sourceType, targetType)) return null;

        // 注册表腿上下文：每对解析一次（循环外——循环内 Resolve 会按属性重复发射构建期 Trace）
        var registryCtx = RegistryMaskContext.Resolve(sourceType, targetType);

        var key = GetPropertyList(sourceType, targetType);
        var sourceTypes = key.Key;
        var targetTypes = key.Value;

        var sourceIsDefine = sourceType.IsDefined(s_mapFromAttribute);
        var targetIsDefine = targetType.IsDefined(s_mapFromAttribute);

        var bindings = new List<MemberBinding>();

        // 循环不变量上移——sourceTypes 为循环外
        // 常量（GetPropertyList 缓存返回值， P6），按需循环外计算一次；无限定名调用经
        // MapperBase.Properties.cs:39-42 包装器透传（′）。
        (List<PropertyInfo> WithAttr, List<PropertyInfo> WithoutAttr)? split = (sourceIsDefine && !targetIsDefine) ? PropertyMatcher.SplitAttrsByAnchor(sourceTypes) : null;

        foreach (var targetItem in targetTypes)
        {
            var sourceItem = GetSourceItemByTargetItem(sourceType, targetItem, sourceTypes, sourceIsDefine, targetIsDefine, out MapPropertyAttribute? attr, split);

            // T6-象限④方向判定受限 Trace 引导（与 ExpressionBuilder.BuildMapBody
            // 共享入口）——简单属性路径走 MemberInit 快速路径不经过 ExpressionBuilder，此处补引导
            ExpressionBuilder.TryWriteDirectionAmbiguityTrace(sourceType, targetType, sourceItem, targetItem);

            // X3-A 方向判反 Trace：非规范位置（[MapFrom] 标源侧）方向判反
            // 可观测化——与复杂路径共享入口，简单路径不遗漏
            ExpressionBuilder.TryWriteDirectionReversalTrace(sourceType, targetType, sourceItem, targetItem);

            var binding = TryBuildSingleBinding(sourceItem, targetItem, srcParam, attr, targetIsDefine, out bool shouldFallback, registryCtx);
            if (shouldFallback) return null;
            if (binding != null) bindings.Add(binding);
        }

        if (bindings.Count == 0) return null;

        return MemberInit(newExpr, bindings);
    }

    /// <summary>
    /// 尝试为单个属性构建 MemberBinding — 仅处理简单赋值（同类型/隐式转换/可空转换/枚举）
    /// </summary>
    /// <param name="sourceItem">源属性</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="srcParam">源参数表达式</param>
    /// <param name="attr">[MapProperty] 特性（非 null 表示有显式标注）</param>
    /// <param name="targetIsDefine">目标类型是否标注 [MapFrom]</param>
    /// <param name="shouldFallback">true 表示遇到复杂映射需回退 Block</param>
    /// <param name="registryCtx">注册表腿上下文（TryBuildMemberInit 每对解析一次后传入；本方法只消费不解析）</param>
    /// <returns>MemberBinding；null 表示跳过该属性</returns>
    [RequiresUnreferencedCode("TryBuildSingleBinding scans type members and attributes via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("TryBuildSingleBinding builds enum conversion via MakeGenericMethod (TypeConvertHelper.ConvertValue). Not supported in AOT.")]
    private static MemberBinding? TryBuildSingleBinding(
        PropertyInfo? sourceItem, PropertyInfo targetItem,
        ParameterExpression srcParam, MapPropertyAttribute? attr,
        bool targetIsDefine, out bool shouldFallback,
        RegistryMaskContext? registryCtx = null)
    {
        // 原 164 行四类职责拆分——宿主保留守卫分发骨架，
        // ②守卫族（无源/不可读写/跨表内联）提取 TryBuildGuardedBinding、
        // ④简单转换子集提取 TryBuildSimpleConversionBinding、
        // ③可空条件转换经 A1-04 已收敛 ConvertStrategy.BuildNullableConditionalValue 单点。
        // 纯移动零逻辑改动：各分支与注释逐行迁移，出口语义一一对应。
        shouldFallback = false;
        var isToDto = targetIsDefine;

        // 职责①（宿主）： MapWhen 守卫检测（对齐检查点）
        // MemberInit 绑定列表无法表达条件跳过（新建保持构造器/初始化器值），
        // 回退 Block 路径（GetMapBody → BuildMapBody 守卫 IfThen 包裹——双路径一致，S8；
        // 生成端镜像 EmitGuardedSinglePropertyAssignment 赋值层 if 守卫）
        if (PropertyMatcher.TryGetWhenDeclaration(typeof(TSource), typeof(TTarget), targetItem) != null)
        {
            shouldFallback = true;
            return null;
        }

        // 职责②（提取）：前置守卫族——跨表内联 / 无源·不可读写 / attr 跨表
        var guarded = TryBuildGuardedBinding(sourceItem, targetItem, srcParam, attr, isToDto,
            out var guardHandled, out var guardFallback);
        if (guardHandled)
        {
            shouldFallback = guardFallback;
            return guarded;
        }

        // 职责①（宿主）：Plan 调用与按 Kind 分发骨架
        // sourceItem 非 null 由守卫族保证（无源场景 handled=true 已返回）——拆分后跨方法流分析断裂，
        // 经非空局部收敛断言（A1-15 门禁补遗，warnaserror 下 CS8604）
        var source = sourceItem!;
        var sourceProperty = Property(srcParam, source);

        // 赋值决策收进 Plan(PropertyInfo)（前置判定 + 类型形态决策单点化）
        var plan = AssignmentPlanner.Plan(source, targetItem, isToDto, attr, crossTableFromSource: null, registryCtx);

        // 按 Kind 分发渲染
        switch (plan.Kind)
        {
            case AssignmentKind.Ignore:
            case AssignmentKind.Skip:
                // 忽略/跳过 → 不构建绑定（Plan 已判定 ShouldIgnore）
                return null;

            case AssignmentKind.Direct:
                // 同类型直接绑定
                return Bind(targetItem, sourceProperty);

            case AssignmentKind.Convert:
                // 反向 ConvertUsing 降级 → shouldFallback（保持现状走 Block ConvertValue 路径）
                if (plan.IsConvertedFromConvertUsing)
                {
                    shouldFallback = true;
                    return null;
                }

                // 职责③：可空→非可空值类型 → Conditional（孪生点收敛： 预检 + 条件构建
                // 上提 ConvertStrategy.BuildNullableConditionalValue 共享单点，与 Block 路径
                // 双点位同源；形态改判以 ConvertStrategy 位为准——初判"本位为准"经 G2 快照
                // 测试证伪，本快路径树形态变化经全量测试等价验证，见单点注释）
                if (plan.IsNullableConditional)
                {
                    return Bind(targetItem,
                        ConvertStrategy.BuildNullableConditionalValue(source, targetItem, sourceProperty));
                }

                // 职责④（提取）：简单转换子集可留在 MemberInit（同类型/隐式转换/枚举/值类型→可空）
                var simple = TryBuildSimpleConversionBinding(source, targetItem, sourceProperty, out var simpleFallback);
                if (simpleFallback)
                {
                    shouldFallback = true;
                    return null;
                }
                return simple;

            default:
                // ConvertUsing/Mask/ConvertUsingThenMask/ConvertBackUsing/Json/CrossTable/Nested/Collection
                // → shouldFallback（回退 Block，由 RenderPlan 按 Kind 渲染）
                shouldFallback = true;
                return null;
        }
    }

    /// <summary>
    /// 前置守卫族（A1-15 职责②，原 TryBuildSingleBinding 逐行迁移）——
    /// 跨表内联（无源/有源两形态）与无源·不可读写跳过判定。
    /// </summary>
    /// <param name="sourceItem">源属性（null = 无源场景，由守卫族处置跨表内联或跳过）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="srcParam">源参数表达式</param>
    /// <param name="attr">[MapProperty] 特性（可为 null）</param>
    /// <param name="isToDto">是否 DTO 方向</param>
    /// <param name="handled">true 表示本属性已由守卫族处置（宿主直接采用返回值）；false 表示继续走 Plan 分发</param>
    /// <param name="fallback">true 表示守卫族判定需回退 Block 路径（宿主转写 shouldFallback）</param>
    /// <returns>已构建的 MemberBinding（仅跨表内联成功时非 null）；其余返回 null</returns>
    [RequiresUnreferencedCode("TryBuildGuardedBinding scans type members and attributes via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("TryBuildGuardedBinding builds enum conversion via MakeGenericMethod (TypeConvertHelper.ConvertValue). Not supported in AOT.")]
    private static MemberBinding? TryBuildGuardedBinding(
        PropertyInfo? sourceItem, PropertyInfo targetItem,
        ParameterExpression srcParam, MapPropertyAttribute? attr,
        bool isToDto, out bool handled, out bool fallback)
    {
        handled = true;
        fallback = false;

        // 保留：跨表内联（sourceItem==null + 跨表标注，:529-542）——不收进 Plan
        if (sourceItem == null && attr != null
            && attr.FromSource != typeof(TSource)
            && targetItem.IsDefined(typeof(MapPropertyAttribute))
            && targetItem.CanWrite)
        {
            var crossBinding = TryBuildCrossTableBinding(attr, targetItem, srcParam);
            if (crossBinding != null)
                return crossBinding;
            fallback = true;
            return null;
        }

        // 保留：sourceItem==null/不可读/不可写（含扁平化/反向写回 fallback，:544-563）——不收进 Plan
        if (sourceItem == null || !sourceItem.CanRead || sourceItem.PropertyType.IsNotPublic || !targetItem.CanWrite)
        {
            // 扁平化候选 → shouldFallback（:554-556）
            if (sourceItem == null && targetItem.CanWrite && isToDto
                && (attr == null || attr.FromSource == typeof(TSource)))
                fallback = true;
            // 反向写回 → shouldFallback（:560-562）
            if (sourceItem == null && targetItem.CanWrite && !isToDto
                && PropertyMatcher.GetMapFromAttribute(typeof(TSource))?.FromSource == typeof(TTarget))
                fallback = true;
            // 反向无源 [ConvertBack] → shouldFallback
            // MemberInit 快捷路径不经过 BuildMapBody 无源块——实体属性带 [ConvertBack] 且无 DTO
            // 源属性时必须回退 Block 路径（由无源块前置分支消费），否则简单/复杂路径分叉
            //（对齐检查点 :466-467；规范反向场景由上一守卫覆盖，本守卫补方向缺省场景）
            if (sourceItem == null && targetItem.CanWrite && !isToDto
                && targetItem.IsDefined(typeof(ConvertBackAttribute)))
                fallback = true;
            return null;
        }

        // 保留：attr!=null 跨表内联（sourceItem!=null 的情况，:576-587）
        if (attr != null)
        {
            if (attr.FromSource != typeof(TSource)
                && targetItem.IsDefined(typeof(MapPropertyAttribute)))
            {
                var crossBinding = TryBuildCrossTableBinding(attr, targetItem, srcParam);
                if (crossBinding != null)
                    return crossBinding;
                fallback = true;  // 跨表失败 → fallback
                return null;
            }
            // ② fallback 边界变更：同源标注属性（FromSource == TSource）
            // 从一律 shouldFallback 变为留 MemberInit（传入 Plan 判定，产 Direct/Convert Kind 时不 fallback）。
            // 现状 attr!=null 非跨表一律 shouldFallback，不抵达 Plan 调用。
        }

        handled = false;  // 未命中守卫 → 继续走 Plan 分发
        return null;
    }

    /// <summary>
    /// 简单转换子集绑定构建（A1-15 职责④，原 TryBuildSingleBinding Convert 分支 逐行迁移）——
    /// 同类型/隐式转换/值类型→可空/枚举对 ConvertValue 四形态留在 MemberInit，其余回退 Block。
    /// </summary>
    /// <param name="sourceItem">源属性</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="sourceProperty">源属性访问表达式</param>
    /// <param name="fallback">true 表示非隐式转换形态需回退 Block 路径</param>
    /// <returns>MemberBinding；fallback=true 时返回 null</returns>
    [RequiresUnreferencedCode("TryBuildSimpleConversionBinding scans type members and attributes via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("TryBuildSimpleConversionBinding builds enum conversion via MakeGenericMethod (TypeConvertHelper.ConvertValue). Not supported in AOT.")]
    private static MemberBinding? TryBuildSimpleConversionBinding(
        PropertyInfo sourceItem, PropertyInfo targetItem, Expression sourceProperty, out bool fallback)
    {
        fallback = false;

        Expression valueExpr;
        if (sourceItem.PropertyType == targetItem.PropertyType)
            valueExpr = sourceProperty;
        else if (TypeConverter.IsImplicitConvertible(sourceItem.PropertyType, targetItem.PropertyType))
            valueExpr = Convert(sourceProperty, targetItem.PropertyType);
        else if (sourceItem.PropertyType.IsValueType && !sourceItem.PropertyType.IsEnum
                 && TypeConverter.IsNullableType(targetItem.PropertyType))
            valueExpr = Convert(sourceProperty, targetItem.PropertyType);
        else if ((sourceItem.PropertyType.IsEnum || targetItem.PropertyType.IsEnum)
                 && sourceItem.PropertyType != typeof(string) && targetItem.PropertyType != typeof(string))
        {
            // 枚举对统一 ConvertValue
            // 语义（显式异常）——原 Expression.Convert unchecked 静默环绕（枚举底层值
            // 溢出目标域得环绕错值），与 Block 路径（ConvertValue 运行时转换）分叉；
            // 改调用 ConvertValue 对齐双引擎（MemberInit 侧 int?→long? 等隐式对仍走
            // 上方 Convert 快路径，A1 无 ConvertValue 节点不受影响）。
            var enumHelper = ReflectionHelper.GetRequiredMethod(typeof(TypeConvertHelper), nameof(TypeConvertHelper.ConvertValue))
                .MakeGenericMethod(sourceItem.PropertyType, targetItem.PropertyType);
            valueExpr = Call(null, enumHelper, sourceProperty);
        }
        else
        {
            fallback = true;  // 非隐式转换 → 回退 Block
            return null;
        }
        return Bind(targetItem, valueExpr);
    }

    /// <summary>
    /// 尝试构建跨表导航 MemberBinding — [MapProperty(typeof(X), "Field")] 且 X != TSource
    /// <para>在 TSource 中按类型匹配查找导航属性，再在导航类型中查找字段，生成 null 安全绑定</para>
    /// </summary>
    /// <param name="attr">[MapProperty] 特性</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="srcParam">源参数表达式</param>
    /// <returns>MemberBinding；导航/字段未找到或类型不兼容时返回 null</returns>
    [RequiresUnreferencedCode("TryBuildCrossTableBinding scans type members and attributes via reflection. Some members may be trimmed.")]
    private static MemberBinding? TryBuildCrossTableBinding(
        MapPropertyAttribute attr, PropertyInfo targetItem, ParameterExpression srcParam)
    {
        // AU值类型导航防护 — FromSource 为 struct 时无法构造 null 安全访问（Expression.Constant(null, struct) 会抛 ArgumentException）
        if (!attr.FromSource.IsClass) return null;

        // 跨表 MemberInit 快路径不消费 [Convert]/[ConvertBack]
        // 带转换器的组合静默直赋错值（编译期明确支持该组合：MappingExtractor.Properties.cs:151 /
        // CodeGenerator.Compose.cs:265 / Models.cs:1376）。入口守卫强制回退 Block 路径
        //（与 ConvertBackUsing 守卫同模式；转换器语义经 CrossTableStrategy 消费链恢复）。
        // 两处调用点（:552/:586）均已回设 shouldFallback = true，一行守卫达成完整闭环。
        if (targetItem.IsDefined(typeof(ConvertAttribute))
            || targetItem.IsDefined(typeof(ConvertBackAttribute)))
            return null;

        // 在源类型中查找类型为 attr.FromSource 的导航属性（按类型匹配，引用导航优先）
        var navProp = NavigationResolver.ResolveNavigationProperty(
            TypeHelper.GetPublicProperties(typeof(TSource)), attr.FromSource, typeof(TSource).Name);
        if (navProp == null) return null;

        // 在导航类型中查找 attr.FromField 字段
        var navField = TypeHelper.GetPublicProperties(attr.FromSource)
            .FirstOrDefault(p => string.Equals(p.Name, attr.FromField, StringComparison.OrdinalIgnoreCase));
        if (navField == null) return null;

        var navAccess = Property(srcParam, navProp);
        Expression fieldAccess = Property(navAccess, navField);
        var fieldType = navField.PropertyType;
        var targetType = targetItem.PropertyType;

        // 类型兼容性检查 + 转换
        if (fieldType != targetType)
        {
            if (TypeConverter.IsImplicitConvertible(fieldType, targetType))
                fieldAccess = Convert(fieldAccess, targetType);
            else if (TypeConverter.IsNullableType(targetType)
                     && Nullable.GetUnderlyingType(targetType) == fieldType)
                fieldAccess = Convert(fieldAccess, targetType); // T → T? 始终合法
            else
                return null;
        }

        // null 安全绑定：nav != null ? nav.Field : default(targetType)
        var nullCheck = NotEqual(navAccess, Constant(null, navProp.PropertyType));
        var nullSafe = Condition(nullCheck, fieldAccess, Default(targetType));
        return Bind(targetItem, nullSafe);
    }

    /// <summary>
    /// 为无无参构造的类型（如 record）匹配最佳构造器并生成参数表达式
    /// 按参数名（大小写不敏感）从源对象属性匹配，未匹配参数填 default
    /// 选择策略：全部匹配优先；否则选匹配数最多的构造器
    /// <para>private → internal——供 MapperBaseDelegateImpl
    /// 直接调用（消除 TryBuildNewExpr 反射回指）；私有成员访问留在本方法体内不外泄。</para>
    /// </summary>
    /// <param name="targetType">目标类型（如 record）</param>
    /// <param name="sourceType">源类型</param>
    /// <param name="sourceParam">源对象参数表达式</param>
    /// <returns>构造器信息 + 参数表达式数组</returns>
    [RequiresUnreferencedCode("BuildConstructorArgs scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static (ConstructorInfo Ctor, Expression[] Args) BuildConstructorArgs(
        Type targetType, Type sourceType, Expression sourceParam)
    {
        var sourceProps = GetPropertiesDictIgnoreCase(sourceType)
            ?? new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);

        // H-1 白名单门（构造器腿）——白名单对经构造器按名匹配通道同样必须遵守
        // "未声明同名不配对"（本腿执行序先于快路径/Block 分流，原无门——腿②）。
        // 门拦截的参数视同未匹配（留 Default），与 Block 门（ExpressionBuilder.cs:158-178）同形；
        // 显式声明判定复用 HasExplicitDeclarationForPair（internal 化，语义单源防两份实现分叉）。
        var noAutoMatch = RuntimeSnapshotStore.IsNoAutoMatch(sourceType, targetType);
        List<PropertyInfo>? gateSourceTypes = null;
        bool gateSourceIsDefine = false, gateTargetIsDefine = false;
        (List<PropertyInfo> WithAttr, List<PropertyInfo> WithoutAttr)? gateSplit = null;
        if (noAutoMatch)
        {
            gateSourceTypes = new List<PropertyInfo>(sourceProps.Values);
            gateSourceIsDefine = sourceType.IsDefined(s_mapFromAttribute);
            gateTargetIsDefine = targetType.IsDefined(s_mapFromAttribute);
            gateSplit = (gateSourceIsDefine && !gateTargetIsDefine) ? PropertyMatcher.SplitAttrsByAnchor(gateSourceTypes) : null;
        }

        // ctor 参数 ↔ 目标属性同名对应（record 形态一一对应）：显式声明挂在目标属性上
        //（同身 [MapProperty]，谓词分量①）或源侧 [MapProperty].FromField 反向镜像（分量②）；
        // 无同名目标属性 → 声明不可达 → 视同无显式声明（留默认）。
        bool CtorParamHasExplicitDeclaration(string paramName)
        {
            var targetProp = targetType.GetProperty(paramName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return targetProp != null && ExpressionBuilder.HasExplicitDeclarationForPair(
                targetProp, gateSourceTypes!, gateSourceIsDefine, gateTargetIsDefine, gateSplit);
        }

        var ctors = targetType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (ctors.Length == 0)
            throw new MapConfigurationException(
                ErrorMessages.NoPublicConstructor(targetType.Name),
                null, targetType);

        ConstructorInfo? bestCtor = null;
        Expression[]? bestArgs = null;
        int bestMatched = -1;
        // 记录首个"按名匹配但类型不兼容"的构造器详情（供全部不可用时抛异常）
        string? bestIncompatDetail = null;

        foreach (var ctor in ctors)
        {
            var parameters = ctor.GetParameters();
            var args = new Expression[parameters.Length];
            int matched = 0;
            bool incompatible = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramName = parameters[i].Name!;
                // ByRef/指针参数形态——按名匹配与未匹配路径均
                // 无法构建（未匹配 Default(ByRef) 裸抛 ArgumentException "Type must not be
                // ByRef"；匹配源属性亦无法为 New(ctor, args) 提供合法实参）——标记该构造器
                // 不可用（多构造器场景回退其他），全部不可用时由 B1 兜底抛
                // MapConfigurationException（含类型上下文，异常契约统一）。
                if (parameters[i].ParameterType.IsByRef || parameters[i].ParameterType.IsPointer)
                {
                    incompatible = true;
                    bestIncompatDetail ??=
                        $"{targetType.Name}..ctor({parameters[i].ParameterType.Name}) 含 ByRef/指针参数，无法映射构造";
                    break;
                }
                if (sourceProps.TryGetValue(paramName, out var srcProp) && srcProp.CanRead)
                {
                    // H-1 白名单门：白名单对且该参数无显式声明 → 跳过按名自动匹配、参数留默认
                    if (noAutoMatch && !CtorParamHasExplicitDeclaration(paramName))
                    {
                        args[i] = Default(parameters[i].ParameterType);
                        continue;
                    }
                    var srcExpr = Property(sourceParam, srcProp);
                    var srcType = srcProp.PropertyType;
                    var paramType = parameters[i].ParameterType;
                    if (srcType == paramType)
                    {
                        args[i] = srcExpr;
                    }
                    else if (IsCtorArgConvertible(srcType, paramType))
                    {
                        // 兼容转换：隐式拓宽/枚举/可空提升/显式数值/引用向上，Expression.Convert 均合法
                        args[i] = Convert(srcExpr, paramType);
                    }
                    else
                    {
                        // B1类型不兼容不再无条件 Convert（构建期崩 + Lazy 每次重编译），
                        // 记录详情并跳过该构造器，全部不可用时抛 MapConfigurationException
                        incompatible = true;
                        bestIncompatDetail ??=
                            $"{sourceType.Name}.{srcProp.Name}({srcType.Name}) → {targetType.Name}..ctor({paramType.Name})";
                        break;
                    }
                    matched++;
                }
                else
                {
                    // 未匹配的参数用默认值
                    args[i] = Default(parameters[i].ParameterType);
                }
            }

            if (incompatible)
                continue;

            // 全部匹配 → 直接选定
            if (matched == parameters.Length)
            {
                bestCtor = ctor;
                bestArgs = args;
                break;
            }

            // 否则记录匹配数最多的
            if (matched > bestMatched)
            {
                bestCtor = ctor;
                bestArgs = args;
                bestMatched = matched;
            }
        }

        // B1所有构造器均因类型不兼容不可用时，抛带类型上下文的配置异常（不静默填 default）
        if (bestCtor == null && bestIncompatDetail != null)
        {
            throw new MapConfigurationException(
                ErrorMessages.MapCtorIncompatible(bestIncompatDetail),
                sourceType, targetType);
        }

        return (bestCtor!, bestArgs!);
    }

    /// <summary>
    /// 构造器参数类型兼容性判定
    /// <para>判定 Expression.Convert 是否合法：类型相等直传；隐式拓宽（<see cref="TypeConverter.IsImplicitConvertible"/>）、
    /// 枚举↔数值/枚举互转、Nullable 提升（T↔T?、底层可转换）、显式数值互转、引用向上转换（派生→基类/接口）
    /// 均为合法 Convert；其余不兼容（如 string→int、基类→派生）返回 false，调用方抛 MapConfigurationException。</para>
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
    /// 数值家族判定— byte/sbyte/short/ushort/int/uint/long/ulong/float/double/decimal/char
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

    #endregion 委托编译

    #region 列表委托编译

    /// <summary>
    /// 编译列表映射 Lambda：new List(n) → for 循环 → new TTarget + MapAction + list.Add → return list
    /// 把 new TTarget 内联到循环中，消除 MapFunc 委托调用层（每元素省一次委托跳转）
    /// </summary>
    [RequiresDynamicCode("CreateMapListFunc uses Expression.Compile() and reflection in the runtime fallback path. Not supported in AOT. Use the source generator to ensure AOT compatibility.")]
    [RequiresUnreferencedCode("CreateMapListFunc scans type members and attributes via reflection. Some members may be trimmed. Use the source generator to ensure AOT compatibility.")]
    private static Func<List<TSource>, List<TTarget>>? CreateMapListFunc()
    {
        // TTarget 必须有公共构造函数（无参或带参均可）
        var defaultCtor = typeof(TTarget).GetConstructor(Type.EmptyTypes);
        if (defaultCtor == null)
        {
            var ctors = typeof(TTarget).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (ctors.Length == 0) return null;
        }

        var srcListParam = Parameter(typeof(List<TSource>), "srcList");
        var resultVar = Variable(typeof(List<TTarget>), "result");
        var iVar = Variable(typeof(int), "i");
        var countVar = Variable(typeof(int), "count");
        var itemVar = Variable(typeof(TSource), "item");
        var tgtVar = Variable(typeof(TTarget), "tgt");

        // 获取公共构建块
        var loopBody = BuildCollectionLoopBody(
            srcListParam, resultVar, iVar, countVar, itemVar, tgtVar,
            defaultCtor, isList: true);

        var listCtor = ReflectionHelper.GetRequiredConstructor(typeof(List<TTarget>), new[] { typeof(int) });
        var countProp = ReflectionHelper.GetRequiredProperty(typeof(List<TSource>), "Count");

        var body = Block(
            new[] { resultVar, iVar, countVar, itemVar, tgtVar },
            Assign(countVar, Property(srcListParam, countProp)),
            Assign(resultVar, New(listCtor, countVar)),
            loopBody,
            resultVar
        );

        return Lambda<Func<List<TSource>, List<TTarget>>>(body, srcListParam).Compile();
    }

    /// <summary>
    /// 编译数组映射 Lambda：new TTarget[n] → for 循环 → new TTarget + GetMapBody → return array
    /// 与 MapListFunc 对称，消除逐元素 MapFunc 委托调用层
    /// </summary>
    [RequiresDynamicCode("CreateMapArrayFunc uses Expression.Compile() and reflection in the runtime fallback path. Not supported in AOT. Use the source generator to ensure AOT compatibility.")]
    [RequiresUnreferencedCode("CreateMapArrayFunc scans type members and attributes via reflection. Some members may be trimmed. Use the source generator to ensure AOT compatibility.")]
    private static Func<TSource[], TTarget[]>? CreateMapArrayFunc()
    {
        var defaultCtor = typeof(TTarget).GetConstructor(Type.EmptyTypes);
        if (defaultCtor == null)
        {
            var ctors = typeof(TTarget).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (ctors.Length == 0) return null;
        }

        var srcArrayParam = Parameter(typeof(TSource[]), "srcArray");
        var resultVar = Variable(typeof(TTarget[]), "result");
        var iVar = Variable(typeof(int), "i");
        var lenVar = Variable(typeof(int), "len");
        var itemVar = Variable(typeof(TSource), "item");
        var tgtVar = Variable(typeof(TTarget), "tgt");

        // 获取公共构建块
        var loopBody = BuildCollectionLoopBody(
            srcArrayParam, resultVar, iVar, lenVar, itemVar, tgtVar,
            defaultCtor, isList: false);

        var body = Block(
            new[] { resultVar, iVar, lenVar, itemVar, tgtVar },
            Assign(lenVar, ArrayLength(srcArrayParam)),
            Assign(resultVar, NewArrayBounds(typeof(TTarget), lenVar)),
            loopBody,
            resultVar
        );

        return Lambda<Func<TSource[], TTarget[]>>(body, srcArrayParam).Compile();
    }

    /// <summary>
    /// 构建集合映射的循环体 — CreateMapListFunc 和 CreateMapArrayFunc 的共用逻辑
    /// </summary>
    /// <param name="srcParam">源集合参数表达式</param>
    /// <param name="resultVar">结果集合变量</param>
    /// <param name="iVar">循环索引变量</param>
    /// <param name="countVar">元素数量变量（List.Count 或 Array.Length）</param>
    /// <param name="itemVar">当前元素变量</param>
    /// <param name="tgtVar">当前目标变量</param>
    /// <param name="defaultCtor">无参构造器（null 表示 record 路径）</param>
    /// <param name="isList">true=List 路径（索引器+Add），false=Array 路径（数组访问+赋值）</param>
    /// <returns>循环体表达式</returns>
    [RequiresUnreferencedCode("BuildCollectionLoopBody scans type members and attributes via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("BuildCollectionLoopBody builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    private static Expression BuildCollectionLoopBody(
        ParameterExpression srcParam, ParameterExpression resultVar,
        ParameterExpression iVar, ParameterExpression countVar,
        ParameterExpression itemVar, ParameterExpression tgtVar,
        ConstructorInfo? defaultCtor, bool isList)
    {
        var breakLabel = Label("break");

        // new TTarget 表达式
        Expression newExpr;
        if (defaultCtor != null)
        {
            newExpr = New(defaultCtor);
        }
        else
        {
            var (ctor, args) = BuildConstructorArgs(typeof(TTarget), typeof(TSource), itemVar);
            newExpr = New(ctor, args);
        }

        // 内联赋值表达式列表
        var mapBodyExpressions = GetMapBody(itemVar, tgtVar);

        // 元素访问：List 用索引器（Item 属性），Array 用数组访问
        Expression itemAccess = isList
            ? Property(srcParam, ReflectionHelper.GetRequiredProperty(srcParam.Type, "Item"), iVar)
            : ArrayAccess(srcParam, iVar);

        // 结果写入：List 用 Add，Array 用索引赋值
        Expression storeResult = isList
            ? Call(resultVar, "Add", null, tgtVar)
            : Assign(ArrayAccess(resultVar, iVar), tgtVar);

        // null 元素写入：List 用 Add(null)，Array 用索引赋值 null
        Expression storeNull = isList
            ? Call(resultVar, "Add", null, Constant(null, typeof(TTarget)))
            : Assign(ArrayAccess(resultVar, iVar), Constant(null, typeof(TTarget)));

        return Loop(
            IfThenElse(
                LessThan(iVar, countVar),
                Block(
                    // item = src[i]
                    Assign(itemVar, itemAccess),
                    // if (item != null) { tgt = new TTarget(); 赋值; result.Add(tgt) / result[i] = tgt; }
                    // else { result.Add(null) / result[i] = null; }  — 保留 null 保持索引一致性
                    IfThenElse(
                        NotEqual(itemVar, Constant(null, typeof(TSource))),
                        Block(
                            Assign(tgtVar, newExpr),
                            Block(mapBodyExpressions),
                            storeResult
                        ),
                        storeNull
                    ),
                    // i++
                    PostIncrementAssign(iVar)
                ),
                Break(breakLabel)
            ),
            breakLabel
        );
    }

    #endregion 列表委托编译
}
