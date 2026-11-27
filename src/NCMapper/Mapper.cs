#pragma warning disable NCMapper001 // 内部使用 [Experimental] 的 Pipeline API
using NCMapper.Attributes;
using NCMapper.Internal;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
namespace NCMapper;
/// <summary>
/// 自动映射类 — 公共入口
/// 内部自动选择最优路径：有源生成器代码时走生成器（零首次延迟），否则走运行时表达式树编译
/// </summary>
public partial class Mapper
{
    /// <summary>
    /// 可观测性指标开关 — 默认 false（关闭），设为 true 后 Map/MapList 入口执行 Interlocked 计数
    /// <para>关闭时 TotalMapCalls/CacheHits/CompilationCount 不更新（省去 2 次 Interlocked.Increment 开销）</para>
    /// <para>CW使用 volatile 确保多线程下设置后立即对其他线程可见</para>
    /// </summary>
    public static bool MetricsEnabled
    {
        get => s_metricsEnabled;
        set => s_metricsEnabled = value;
    }

    /// <summary>
    /// 指标采集总开关（volatile 保证多线程可见性）
    /// </summary>
    private static volatile bool s_metricsEnabled = false;

    /// <summary>
    /// 泛型类型缓存 — 每个类型对有独立的静态字段，JIT 零开销查找（消除 Dictionary 查找）
    /// 源生成器注册时写入此泛型缓存
    /// </summary>
    private static class GeneratedCache<TSource, TTarget>
        where TSource : class
        where TTarget : class
    {
        /// <summary>
        /// 核心映射委托（无 Action，热路径，99% 场景走此路径）
        /// <para>CW使用 volatile 确保 ARM 弱内存序下多线程注册后可见</para>
        /// </summary>
        public static volatile Func<TSource, TTarget?>? CoreFunc;
        /// <summary>
        /// 带 Action 映射委托（仅 action 非 null 时走此路径，null 表示未注册）
        /// <para>CW使用 volatile 确保 ARM 弱内存序下多线程注册后可见</para>
        /// </summary>
        public static volatile Func<TSource, Action<TSource, TTarget>?, TTarget?>? Func;
        /// <summary>
        /// 更新已有目标对象的映射委托（接收 source、target、action 三个参数，void 返回）
        /// <para>由源生成器注册，命中后 Map(source, target, action) 重载走编译时代码路径</para>
        /// <para>CW使用 volatile 确保 ARM 弱内存序下多线程注册后可见</para>
        /// </summary>
        public static volatile Action<TSource, TTarget, Action<TSource, TTarget>?>? UpdateAction;
        /// <summary>
        /// 列表映射委托（null 表示未注册）
        /// <para>CW使用 volatile 确保 ARM 弱内存序下多线程注册后可见</para>
        /// </summary>
        public static volatile Func<List<TSource>, List<TTarget>>? ListFunc;
        /// <summary>
        /// 批量就地映射委托（接收 IList 源、目标数组、action，void 返回）
        /// <para>由源生成器注册，命中后 MapListInto 走编译时代码路径</para>
        /// <para>CW使用 volatile 确保 ARM 弱内存序下多线程注册后可见</para>
        /// </summary>
        public static volatile Action<IList<TSource>, TTarget[], Action<TSource, TTarget>?>? ListIntoAction;
    }

    /// <summary>
    /// 源生成器 Compose 委托缓存 — 按 DTO 类型查找，零反射编译时代码
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<object[], object?>> s_generatedComposeFuncs = new();

    /// <summary>
    /// 源生成器 Validate 委托缓存（多槽位）— 键为委托声明类型，每个程序集的生成代码
    /// 恰好产出一个 <c>GeneratedMapper</c> 类型（CodeGenerator.cs 每编译一份无条件输出注册行），
    /// 声明类型键与程序集键分槽粒度等价，多程序集注册互不覆盖，Validate() 聚合全部槽位结果
    /// <para>ConcurrentDictionary 自身保证多线程可见性与原子性，无需 volatile
    /// （由单槽位 volatile 字段改造）</para>
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<List<MapValidationResult>>> s_generatedValidateFuncs = new();

    /// <summary>
    /// 源生成器 DeepCopy 委托缓存 — 每个类型有独立的静态字段，JIT 零开销查找
    /// <para>方向 I 优化：委托签名加 DeepCopyContext 参数，生成代码递归调用复用 ctx
    /// 保持循环引用检测有效，替代 [ThreadStatic] s_deepCopyInProgress。</para>
    /// </summary>
    private static class GeneratedDeepCopyCache<T>
        where T : class
    {
        /// <summary>
        /// 深拷贝委托（null 表示未注册，走 DeepCopyProviderBase.Current）
        /// <para>CW使用 volatile 确保多线程下注册后立即对其他线程可见，
        /// 避免读到部分更新的委托引用</para>
        /// <para>方向 I 优化：委托签名 (T source, DeepCopyContext ctx) => T，
        /// 生成代码递归调用 Mapper.DeepCopy(src.Prop, ctx) 复用 ctx。</para>
        /// </summary>
        public static volatile Func<T, DeepCopyContext, T>? Func;

        /// <summary>
        /// 方案 F 三值分类标志 — 注册时由源生成代码声明该类型的深拷贝可达图无环（Acyclic）。
        /// <para>true 时层 1 跳过 CycleCache（编译期已证明无环），仅保留深度守卫；
        /// false（含旧版生成代码默认值）走现有 CycleCache 完整检测路径。</para>
        /// <para>与 <see cref="Func"/> 同受泛型静态字段运行时限制：<see cref="ClearGenerated"/>
        /// 无法统一清除，测试隔离通过三参 RegisterGeneratedDeepCopy&lt;T&gt;(null, false) 逐类型覆盖。</para>
        /// </summary>
        public static volatile bool IsAcyclic;
    }

    /// <summary>
    /// ThreadStatic 循环引用检测缓存 — 每个线程仅首次调用时分配，顶层 DeepCopy 退出时 Clear() 复用
    /// <para>方向 I 补充优化：消除每次顶层 DeepCopy 调用的 Dictionary 堆分配（~216B/次），
    /// 递归调用通过 ctx.CycleCache 参数传递（0 ThreadStatic 访问，不受 net9/10 ABI 变化影响）。</para>
    /// </summary>
    [ThreadStatic] private static Dictionary<object, object>? s_cycleCache;

    /// <summary>
    /// ThreadStatic DeepCopyContext 实例 — 每个线程仅首次调用时分配，复用避免每次顶层调用堆分配（~32B/次）
    /// <para>Depth 字段兼作重入检测：Depth > 0 时表示当前线程正在执行 DeepCopy（不清除缓存）。</para>
    /// </summary>
    [ThreadStatic] private static DeepCopyContext? s_deepCopyCtx;

    /// <summary>
    /// 获取当前线程复用的 DeepCopyContext
    /// <para>仅首次调用分配；CycleCache 生命周期由各顶层入口统一负责：
    /// DeepCopy 顶层（:822-835）与 MapperBase.Map/MapList 回退入口（ctx.Depth 归 0 时 Clear 保桶）。</para>
    /// </summary>
    /// <returns>当前线程复用的 DeepCopyContext</returns>
    internal static DeepCopyContext GetOrCreateDeepCopyCtx()
        => s_deepCopyCtx ??= new DeepCopyContext();

    /// <summary>
    /// 兼容旧 API：保留字典注册表（HasGenerated/ClearGenerated 用）
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, Type), Delegate> s_generatedFuncs = new();

    /// <summary>
    /// 生成器注册的 MapList 委托字典（HasGenerated/ClearGenerated 用）
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, Type), Delegate> s_generatedListFuncs = new();

    /// <summary>
    /// 注册源生成器生成的核心映射委托（无 Action 参数，热路径）
    /// 由生成器代码自动调用，用户不需要手动调用
    /// </summary>
    /// <param name="func">核心映射委托函数，可为 null 表示不注册</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterGeneratedCore<TSource, TTarget>(Func<TSource, TTarget?>? func)
        where TSource : class
        where TTarget : class
    {
        GeneratedCache<TSource, TTarget>.CoreFunc = func;
    }

    /// <summary>
    /// 注册源生成器生成的带 Action 映射委托
    /// 由生成器代码自动调用，用户不需要手动调用
    /// </summary>
    /// <param name="func">映射委托函数，可为 null 以注销注册</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterGenerated<TSource, TTarget>(Func<TSource, Action<TSource, TTarget>?, TTarget?>? func)
        where TSource : class
        where TTarget : class
    {
        GeneratedCache<TSource, TTarget>.Func = func;
        if (func != null)
            s_generatedFuncs[(typeof(TSource), typeof(TTarget))] = func;
        else
            s_generatedFuncs.TryRemove((typeof(TSource), typeof(TTarget)), out _);
    }

    /// <summary>
    /// 注册源生成器生成的列表映射委托（由生成器代码自动调用）
    /// </summary>
    /// <param name="func">列表映射委托函数，可为 null 以注销注册</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterGeneratedList<TSource, TTarget>(Func<List<TSource>, List<TTarget>>? func)
        where TSource : class
        where TTarget : class
    {
        GeneratedCache<TSource, TTarget>.ListFunc = func;
        if (func != null)
            s_generatedListFuncs[(typeof(TSource), typeof(TTarget))] = func;
        else
            s_generatedListFuncs.TryRemove((typeof(TSource), typeof(TTarget)), out _);
    }

    /// <summary>
    /// 注册源生成器生成的更新映射委托（由生成器代码自动调用）
    /// <para>注册后 Map(source, target, action) 更新重载走编译时代码路径，零首次编译延迟</para>
    /// </summary>
    /// <typeparam name="TSource">源对象类型</typeparam>
    /// <typeparam name="TTarget">目标对象类型</typeparam>
    /// <param name="action">更新委托：接收 source、target、action，将 source 属性赋值到已有 target</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterGeneratedUpdate<TSource, TTarget>(Action<TSource, TTarget, Action<TSource, TTarget>?>? action)
        where TSource : class
        where TTarget : class
    {
        GeneratedCache<TSource, TTarget>.UpdateAction = action;
    }

    /// <summary>
    /// 注册源生成器生成的批量就地映射委托（由生成器代码自动调用）
    /// <para>注册后 MapListInto(sources, destination, action) 走编译时代码路径，零首次编译延迟</para>
    /// <para>与 RegisterGeneratedUpdate 同构：仅写槽位，不维护辅助字典</para>
    /// </summary>
    /// <typeparam name="TSource">源对象类型</typeparam>
    /// <typeparam name="TTarget">目标对象类型</typeparam>
    /// <param name="action">批量就地映射委托：接收 IList 源、目标数组、action；可为 null 以注销注册</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterGeneratedListInto<TSource, TTarget>(Action<IList<TSource>, TTarget[], Action<TSource, TTarget>?>? action)
        where TSource : class
        where TTarget : class
    {
        GeneratedCache<TSource, TTarget>.ListIntoAction = action;
    }

    /// <summary>
    /// 注册源生成器生成的 Compose 合成委托（由生成器代码自动调用）
    /// <para>注册后 Compose&lt;TTarget&gt; 走编译时代码路径，零反射、AOT 友好</para>
    /// </summary>
    /// <typeparam name="TTarget">目标 DTO 类型</typeparam>
    /// <param name="func">Compose 合成委托，接收 object[] 源对象数组，返回合成后的 DTO</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterGeneratedCompose<TTarget>(Func<object[], TTarget?>? func) where TTarget : class
    {
        if (func == null)
        {
            s_generatedComposeFuncs.TryRemove(typeof(TTarget), out _);
        }
        else
        {
            s_generatedComposeFuncs[typeof(TTarget)] = (arr) => func(arr);
        }
    }

    /// <summary>
    /// 注册源生成器生成的 Validate 委托（由生成器代码自动调用）
    /// <para>注册后 Validate 走编译时代码路径，零反射、AOT 友好</para>
    /// <para>多槽位语义：键为委托声明类型，每个程序集一个槽位，
    /// 同声明类型重复注册幂等覆盖，多程序集注册互不覆盖；<c>Validate()</c> 聚合全部槽位结果。</para>
    /// <para>null 参数 = 清空全部已注册委托（保守保留旧单槽位 null 语义；兼作测试隔离复位入口）。</para>
    /// </summary>
    /// <param name="func">Validate 委托，返回编译时验证结果列表；null 表示清空全部注册</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterGeneratedValidate(Func<List<MapValidationResult>>? func)
    {
        if (func == null)
        {
            s_generatedValidateFuncs.Clear();
            return;
        }
        // 键 = 委托声明类型；动态方法 DeclaringType 为 null 时归一到 Mapper 槽位（幂等覆盖，防字典键 null 抛异常）
        s_generatedValidateFuncs[func.Method.DeclaringType ?? typeof(Mapper)] = func;
    }

    /// <summary>
    /// 注册源生成器输出的全局映射配置快照（生成代码专用，配置非默认时由源生成器输出）
    /// <para>两段式注册语义：未冻结 → 记录（含来源程序集名，供冲突消息产出程序集清单）；
    /// 已冻结且内容相同 → 幂等无操作；已冻结且内容不同 → 当场抛 <see cref="MapConfigurationException"/>。</para>
    /// <para><b>【仅供生成代码调用】</b>手工注册绕开源生成器违反"单一事实来源"原则，
    /// 多程序集配置不一致时首次冻结将抛 <see cref="MapConfigurationException"/>。</para>
    /// </summary>
    /// <param name="config">配置快照；null 表示不注册（等价于未配置，走内建默认）</param>
    /// <param name="assemblyName">来源程序集名（生成器内联常量，编译期确定，零反射）</param>
    /// <exception cref="MapConfigurationException">配置冻结后注册不同内容时抛出</exception>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterGeneratedConfig(GeneratedMapConfig? config, string assemblyName)
    {
        GlobalMapConfig.Register(config, assemblyName);
    }

    /// <summary>
    /// 获取当前冻结的全局映射配置只读视图 — 用于调试、测试断言、日志
    /// <para>未冻结时先触发冻结（幂等；多程序集配置冲突或 NamingStrategy 越界时抛
    /// <see cref="MapConfigurationException"/>），返回值为不可变记录：
    /// 清单为构造时的 defensive copy，底层数组不外泄。</para>
    /// </summary>
    /// <returns>冻结配置的只读视图（不可变）</returns>
    /// <exception cref="MapConfigurationException">冻结裁决失败（多程序集配置冲突 / NamingStrategy 越界）时抛出</exception>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("GetGlobalConfig triggers config freeze, which reflects over [assembly: MapDefaults] when no generated snapshot exists. Types annotated with [MapFrom] use the generated snapshot path with zero reflection.")]
    public static GlobalConfigView? GetGlobalConfig()
    {
        return GlobalMapConfig.GetView();
    }

    /// <summary>
    /// 检查是否有源生成器注册的映射委托
    /// <para>注意：此方法检查字典缓存，与 <see cref="ClearGenerated"/> 保持一致</para>
    /// <para>泛型静态字段（GeneratedCache&lt;T,S&gt;.CoreFunc/Func）无法在运行时统一清除，
    /// 测试隔离通过 <see cref="ClearGenerated"/> + 重新注册覆盖</para>
    /// </summary>
    /// <returns>true 表示有源生成器注册的映射委托；false 表示无注册</returns>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool HasGenerated<TSource, TTarget>()
        where TSource : class
        where TTarget : class
        => s_generatedFuncs.ContainsKey((typeof(TSource), typeof(TTarget)))
           || GeneratedCache<TSource, TTarget>.CoreFunc != null
           || GeneratedCache<TSource, TTarget>.Func != null;

    /// <summary>
    /// 静态构造 —— CacheRegistry 登记制自注册
    /// <para>仅登记清理委托，不触碰任何缓存字段（缓存惰性构建，防自触发）</para>
    /// </summary>
    static Mapper()
    {
        CacheRegistry.Register("generated-funcs", CacheLayer.Generated, () => s_generatedFuncs.Clear());
        CacheRegistry.Register("generated-list-funcs", CacheLayer.Generated, () => s_generatedListFuncs.Clear());
        CacheRegistry.Register("generated-compose-funcs", CacheLayer.Generated, () => s_generatedComposeFuncs.Clear());
        CacheRegistry.Register("generated-validate-funcs", CacheLayer.Generated, () => s_generatedValidateFuncs.Clear());
        CacheRegistry.Register("compose-map-delegate", CacheLayer.Full, () => s_composeMapDelegateCache.Clear());
        CacheRegistry.Register("compose-update-delegate", CacheLayer.Full, () => s_composeUpdateDelegateCache.Clear());
        CacheRegistry.Register("orm-extension-clears", CacheLayer.Full, () =>
        {
            foreach (var clear in s_projectionCacheClears.Values)
            {
                clear();
            }
        });
    }

    /// <summary>
    /// 清除已注册的源生成器委托（仅用于测试隔离，正常使用不需要调用）
    /// <para>登记表驱动：实现为 <see cref="CacheRegistry.ClearGenerated"/>，
    /// 清 Generated 层登记条目（generated-* 4 项）</para>
    /// <para>注意：泛型静态字段（GeneratedCache&lt;T,S&gt;.CoreFunc/Func）无法在运行时统一清除，
    /// 需通过 <see cref="RegisterGenerated{TSource, TTarget}"/> 传 null 逐个覆盖</para>
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void ClearGenerated()
    {
        CacheRegistry.ClearGenerated();
    }

    /// <summary>
    /// 清除所有运行时缓存（属性反射缓存、Compose 方法缓存、深拷贝方法缓存、集合构造器缓存、
    /// 方向歧义通知去重集）
    /// <para>适用于插件系统动态卸载程序集后释放缓存引用的场景</para>
    /// <para><b>级联范围（完备化）</b>：源生成器缓存
    /// （s_generatedFuncs/s_generatedListFuncs/s_generatedComposeFuncs/s_generatedValidateFuncs）、
    /// Compose 委托缓存（s_composeMapDelegateCache/s_composeUpdateDelegateCache）、运行时反射缓存
    /// （PropertyMatcher 三缓存 + PropertyIgnoreChecker、MapValidator、CollectionCopier、
    /// DeepCopyCompiler、DeepCopyProviderBase、DeepCopyTypeClassifier、ReflectionHelper、
    /// ExpressionBuilder 方向歧义去重集）、脱敏注册表（MaskProvider）、全局配置冻结状态（GlobalMapConfig）
    /// 与 ORM 投影缓存（EFCore/FreeSql 经委托反向注册）均被级联清除。</para>
    /// <para><b>容量策略（方案 b）</b>：PropertyMatcher 缓存对动态
    /// 生成类型（Reflection.Emit/可收集程序集）不缓存走直算——无界键空间（B5000 动态类型对 →
    /// 各缓存 12400 条目/52.94MB）由不缓存根治；静态类型键空间天然有界，行为零变化。动态类型
    /// 每次访问重新构建属性字典（直算），语义与缓存路径一致（映射结果守恒），无淘汰/重建保证问题。</para>
    /// <para>注意：泛型静态字段（GeneratedCache/GeneratedDeepCopyCache/MapperBase.MapFunc 等）
    /// 无法通过此方法清除，需通过重新注册或 AppDomain 卸载处理</para>
    /// <para><b>配置重冻结限制</b>：运行时路径已编译的映射委托
    /// （MapperBase&lt;TSource, TTarget&gt; 静态 Lazy 字段）一经物化不受 ClearCache 与配置重冻结
    /// 影响——重冻结仅对尚未编译的类型对生效；测试隔离同类型对须改用未预热场景或进程级隔离。</para>
    /// <para><b>登记表驱动</b>：现 17 项手写级联迁移为
    /// <see cref="CacheRegistry.ClearFull"/>登记表遍历，行为为超集（含 T-3 补漏
    /// assignment-planner/navigation-tree-collector 与 T-4 扩展库 free-sql-projection/free-sql-navigation）。</para>
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void ClearCache()
    {
        CacheRegistry.ClearFull();
    }

    /// <summary>
    /// 投影缓存清理委托（ORM 扩展注册用）— ClearCache 链接入。
    /// <para>核心库不能直接引用 EFCore/FreeSql 扩展（依赖方向），扩展在静态构造时注册
    /// 各自的 <c>ClearCache</c>；核心库 ClearCache 统一调用。</para>
    /// <para>多槽位语义：键为清理委托的声明类型，每个扩展一个槽位，
    /// 同声明类型重复注册幂等覆盖，EFCore/FreeSql 共存时各自槽位均被执行（修复单槽位互覆盖）。</para>
    /// <para>null 参数 = 清空全部已注册清理委托（对齐旧单槽位"置空即清注册"语义，修复）。</para>
    /// </summary>
    /// <param name="clear">投影缓存清理委托；null 表示清空全部注册</param>
    internal static void RegisterProjectionCacheClear(Action? clear)
    {
        if (clear == null)
        {
            s_projectionCacheClears.Clear();
            return;
        }
        // 键 = 委托声明类型；动态方法 DeclaringType 为 null 时归一到 Mapper 槽位（幂等覆盖，防字典键 null 抛异常）
        s_projectionCacheClears[clear.Method.DeclaringType ?? typeof(Mapper)] = clear;
    }

    /// <summary>
    /// 投影缓存清理委托字典（内部，多槽位）— EFCore/FreeSql 扩展静态构造注册，ClearCache 遍历执行
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Action> s_projectionCacheClears = new();

    /// <summary>
    /// 总映射调用次数 — 每次 Map/MapList 公共入口被调用时 +1（Interlocked 原子递增）
    /// <para>可观测性指标，用于监控映射流量和性能分析</para>
    /// <para>T9改为只读属性，防止外部代码篡改计数器。内部通过 Interlocked 原子操作更新后备字段。</para>
    /// </summary>
    public static long TotalMapCalls => Volatile.Read(ref s_totalMapCalls);
    private static long s_totalMapCalls;

    /// <summary>
    /// 缓存命中次数 — GeneratedCache 路径命中（CoreFunc/Func/ListFunc 任一）时 +1
    /// <para>可观测性指标，命中率 = CacheHits / TotalMapCalls 反映源生成器覆盖度</para>
    /// </summary>
    public static long CacheHits => Volatile.Read(ref s_cacheHits);
    private static long s_cacheHits;

    /// <summary>
    /// 运行时编译次数 — GeneratedCache 未命中回退到 MapperBase 路径时 +1
    /// <para>可观测性指标，此值高说明源生成器覆盖不足，可能存在首次映射延迟</para>
    /// <para>注意：此计数器统计 MapperBase 路径触发次数（每次 fallback +1），
    /// 实际表达式树编译次数小于等于此值（同一类型对首次触发才编译，后续复用缓存委托）</para>
    /// </summary>
    public static long CompilationCount => Volatile.Read(ref s_compilationCount);
    private static long s_compilationCount;
    /// <summary>
    /// 命中缓存计数（MetricsEnabled 时）— 热路径内联，禁用时零开销
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void RecordCacheHit()
    {
        if (s_metricsEnabled) Interlocked.Increment(ref s_cacheHits);
    }

    /// <summary>
    /// 回退编译计数（MetricsEnabled 时）— 热路径内联，禁用时零开销
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void RecordCompilation()
    {
        if (s_metricsEnabled) Interlocked.Increment(ref s_compilationCount);
    }

    /// <summary>
    /// 入口调用计数（MetricsEnabled 时）— 热路径内联，禁用时零开销
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void RecordMapCall()
    {
        if (s_metricsEnabled) Interlocked.Increment(ref s_totalMapCalls);
    }

    /// <summary>
    /// 重置所有可观测性指标计数器（TotalMapCalls / CacheHits / CompilationCount 归零；
    /// 扩展：级联重置缓存级命中/未命中计数 CacheMetrics）
    /// <para>登记制遥测级联：级联重置 CacheRegistry
    /// 注册/清理计数（ResetCounters，与 CacheMetrics.Reset 同层）</para>
    /// <para>用于测试隔离和性能基线测量</para>
    /// </summary>
    public static void ResetMetrics()
    {
        Interlocked.Exchange(ref s_totalMapCalls, 0);
        Interlocked.Exchange(ref s_cacheHits, 0);
        Interlocked.Exchange(ref s_compilationCount, 0);
        CacheMetrics.Reset();
        CacheRegistry.ResetCounters();
    }

    /// <summary>
    /// 映射分派核心— CoreFunc→Func→MapperBase 三分支 + 命中/回退指标收敛为单点；
    /// 入口只保留 null 守卫 / totalMapCalls 计数 / Pipeline / Trace 差异。
    /// 泛型静态字段 JIT 零开销查找不变；GeneratedCache 路径是编译时代码，无循环引用风险。
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    [RequiresDynamicCode("Map uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT.")]
    [RequiresUnreferencedCode("Map scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed.")]
    private static TTarget? MapCore<TSource, TTarget>(TSource source, Action<TSource, TTarget>? action)
        where TSource : class where TTarget : class
    {
        // 无 Action → 走 Core 委托（省 action 参数 + action?.Invoke）
        if (action == null && GeneratedCache<TSource, TTarget>.CoreFunc is { } coreGen)
        {
            RecordCacheHit();
            return coreGen(source);
        }
        // 有 Action → 走 WithAction 委托
        if (GeneratedCache<TSource, TTarget>.Func is { } gen)
        {
            RecordCacheHit();
            return gen(source, action);
        }
        // GeneratedCache 未命中 → 回退 MapperBase 运行时路径（内部已有递归深度保护）
        RecordCompilation();
        return MapperBase<TSource, TTarget>.Map(source, action);
    }

    /// <summary>
    /// 列表映射分派核心— Pipeline 逐元素 / ListFunc 批量 / 生成 Func 逐元素 / MapperBase 回退
    /// 四路径收敛为单点；同类与异类重载共用（自动补齐同类重载的生成 Func 逐元素分支——
    /// 仅补了异类重载，同类带 Action 场景此前直接回退 MapperBase）。
    /// </summary>
    [RequiresDynamicCode("MapList uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT.")]
    [RequiresUnreferencedCode("MapList scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed.")]
    private static List<TTarget>? MapListCore<TSource, TTarget>(IEnumerable<TSource> sources, Action<TSource, TTarget>? action)
        where TSource : class where TTarget : class
    {
        // Pipeline 启用时逐元素触发 PostMap，跳过 ListFunc 批量路径
        if (Mapper.Pipeline.IsEnabled)
        {
            // 容量预分配（照抄 gen3 分支口径，免逐元素扩容拷贝）
            var result = new List<TTarget>(sources is ICollection<TSource> coll ? coll.Count : 0);
            foreach (var src in sources)
            {
                if (src == null) { result.Add(null!); continue; }
                var mapped = MapCore<TSource, TTarget>(src, action);
                if (mapped != null) Pipeline.ExecutePostMap(src, mapped);
                result.Add(mapped!);
            }
            return result;
        }

        // 无 Action + List 源时优先走源生成器批量委托（泛型静态字段，JIT 零开销）
        if (action == null && sources is List<TSource> listSource
            && GeneratedCache<TSource, TTarget>.ListFunc is { } gen2)
        {
            RecordCacheHit();
            return gen2(listSource);
        }

        // 带 Action 或非 List 源 → 逐元素走生成 Func（AOT 安全；语义与运行时路径一致且性能更优）
        if (GeneratedCache<TSource, TTarget>.Func is { } gen3)
        {
            RecordCacheHit();
            var result = new List<TTarget>(sources is ICollection<TSource> coll ? coll.Count : 0);
            foreach (var src in sources)
            {
                if (src == null) { result.Add(null!); continue; }
                result.Add(gen3(src, action)!);
            }
            return result;
        }

        // GeneratedCache 未命中 → 回退 MapperBase 运行时路径（内部已有递归深度保护）
        RecordCompilation();
        return MapperBase<TSource, TTarget>.MapList(sources, action);
    }

    /// <summary>
    /// 批量就地映射分派核心— 四路径收敛为单点（与 MapListCore 对称）：
    /// ① Pipeline 启用 → 逐元素内联 UpdateAction 分发 + PostMap（与 Map(src,dst,action) 同构，
    /// 不走公共 Map 入口避免 RecordMapCall 重复计数，P0-1）→ ② ListIntoAction 批量委托（方向 A 主路径，
    /// 无 action==null 前置条件，P1-3）→ ③ UpdateAction 循环（方向 B 形态）→ ④ MapperBase.MapListInto
    /// 批量回退（一次 ctx 协议，R6）
    /// </summary>
    [RequiresDynamicCode("MapListInto uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT.")]
    [RequiresUnreferencedCode("MapListInto scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed.")]
    private static void MapListIntoCore<TSource, TTarget>(IList<TSource> sources, TTarget[] destination, Action<TSource, TTarget>? action)
        where TSource : class where TTarget : class
    {
        // 路径 1：Pipeline 启用时逐元素内联 UpdateAction 分发 + PostMap，跳过批量委托（对称 C9）
        if (Mapper.Pipeline.IsEnabled)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                var src = sources[i];
                if (src == null) continue;                        // 槽位保持原值
                var dst = destination[i];
                if (dst == null) continue;                        // no-op
                if (GeneratedCache<TSource, TTarget>.UpdateAction is { } updateInline)
                {
                    updateInline(src, dst, action);               // 生成就地委托（编译期产物，AOT 安全）
                }
                else
                {
                    RecordCompilation();
                    MapperBase<TSource, TTarget>.Map(src, dst, action);   // 运行时回退（MapperBase 就地重载）
                }
                Pipeline.ExecutePostMap(src, dst);
            }
            return;
        }

        // 路径 2：生成批量委托（方向 A 主路径，内联循环，零间接开销）
        // ListIntoAction 签名含 action 参数（与 ListFunc 的无 action 签名不同）——
        // 无需 action==null 前置条件，勿照抄 MapListCore ListFunc 分支
        if (GeneratedCache<TSource, TTarget>.ListIntoAction is { } listIntoGen)
        {
            RecordCacheHit();
            listIntoGen(sources, destination, action);
            return;
        }

        // 路径 3：回退 — 循环 UpdateAction（委托间接开销；destination[i]==null 由生成 Update 方法内部 null 防护 no-op）
        if (GeneratedCache<TSource, TTarget>.UpdateAction is { } updateGen)
        {
            RecordCacheHit();
            for (int i = 0; i < sources.Count; i++)
            {
                var src = sources[i];
                if (src == null) continue;                        // 槽位保持原值
                updateGen(src, destination[i], action);
            }
            return;
        }

        // 路径 4：GeneratedCache 未命中 → 回退 MapperBase 批量就地运行时路径（一次 ctx 协议 + MapAction 循环，R6）
        RecordCompilation();
        MapperBase<TSource, TTarget>.MapListInto(sources, destination, action);
    }

    /// <summary>
    /// (同类转换)将对象TSource复制为相同类型的对象
    /// 调用示例：Map(source);
    /// <para><b>拷贝行为说明</b>：此方法为混合模式——值类型/string 属性为值拷贝，
    /// 同类型引用类型属性为<b>浅拷贝</b>（共享引用，修改拷贝的嵌套对象会影响原对象），
    /// 不同类型引用类型属性为深拷贝（创建新对象递归映射）。
    /// 如需完全独立的深拷贝，请使用 <see cref="DeepCopy{T}(T)"/>。</para>
    /// </summary>
    /// <param name="source">源对象</param>
    /// <param name="action">方法，可在此处对对象进行后续操作，默认为空</param>
    /// <returns>转换后的目标对象；source 为 null 时返回 default</returns>
    [RequiresDynamicCode("Map uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Map scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public static TSource? Map<TSource>(TSource source, Action<TSource, TSource>? action = null) where TSource : class
    {
        if (source == null) return null;

        // 可观测性指标 — 仅 MetricsEnabled 时计数（省去 2 次 Interlocked.Increment 开销）
        RecordMapCall();

        // dispatch 收敛到 MapCore（三分支 + 命中/回退指标）；Pipeline/Trace 差异保留在入口
        var result = MapCore<TSource, TSource>(source, action);

        // Trace/PostMap 同构尾块提取共享 helper（原）
        ExecuteMapTail(source, result);

        return result;
    }

    /// <summary>
    /// (异类转换)将对象TSource转换为TTarget并返回
    /// 调用示例：Map&lt;Tin,Tout&gt;(source);
    /// <para><b>拷贝行为说明</b>：值类型/string 属性为值拷贝，
    /// 不同类型引用类型属性为<b>深拷贝</b>（创建新对象递归映射），
    /// 同类型引用类型属性为<b>浅拷贝</b>（共享引用）。
    /// 此行为与 AutoMapper/Mapperly/Mapster 等主流映射库一致。
    /// 如需完全独立的深拷贝，请使用 <see cref="DeepCopy{T}(T)"/>。</para>
    /// <para><b>导航属性契约</b>：内存映射<b>只做字段映射，不负责加载导航</b>——本方法不访问数据库、
    /// 也不触发延迟加载。若源实体的导航属性未被加载（值为 null，例如查询时未 Include），
    /// 目标 DTO 的对应嵌套对象即为 null。这是<b>预期行为</b>，不是映射缺陷：导航数据须由调用方
    /// 在查询层（Include / JOIN / 显式加载）取好再交给映射。</para>
    /// </summary>
    /// <param name="source">源对象</param>
    /// <param name="action">方法，可在此处对对象进行后续操作，默认为空</param>
    /// <returns>转换后的目标对象；source 为 null 时返回 default</returns>
    [RequiresDynamicCode("Map uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Map scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public static TTarget? Map<TSource, TTarget>(TSource source, Action<TSource, TTarget>? action = null) where TSource : class where TTarget : class
    {
        if (source == null) return null;

        // 可观测性指标 — 仅 MetricsEnabled 时计数（省去 2 次 Interlocked.Increment 开销）
        RecordMapCall();

        // dispatch 收敛到 MapCore；Pipeline/Trace 差异保留在入口
        var result = MapCore<TSource, TTarget>(source, action);

        // Trace/PostMap 同构尾块提取共享 helper（原）
        ExecuteMapTail(source, result);

        return result;
    }

    /// <summary>
    /// Map 入口 Trace/PostMap 同构尾块
    /// 原同类/异类两 Map 重载尾部约 14 行×2 逐字同构（仅泛型类型名差异）收敛于此单点
    /// （消费方：Map{TSource}(TSource) 与 Map{TSource,TTarget}(TSource) 两入口）。
    /// </summary>
    /// <param name="source">源对象（非 null，入口已判）</param>
    /// <param name="result">映射结果（可为 null，触发跳过 PostMap 的 Trace）</param>
    [RequiresUnreferencedCode("Trace tail calls MapTracer.WriteTrace which scans type members via reflection. Only called from the RequiresUnreferencedCode-attributed Map entry points.")]
    private static void ExecuteMapTail<TSource, TTarget>(TSource source, TTarget? result)
        where TSource : class
        where TTarget : class
    {
        // 同类重载补齐 WriteTrace（与异类重载一致性；Trace 默认关闭零开销）
        if (MapTracer.GloballyEnabled)
            MapTracer.WriteTrace(source, result, null);

        // T2 修复：Pipeline PostMap
        if (Mapper.Pipeline.IsEnabled && result != null)
        {
            Pipeline.ExecutePostMap(source, result);
        }
        else if (Mapper.Pipeline.IsEnabled)
        {
            // 前置守卫——Trace 关闭态免插值字符串分配
            if (MapTracer.GloballyEnabled)
                MapTracer.Write($"[NCMapper] Pipeline: 跳过 PostMap<{typeof(TSource).Name}→{typeof(TTarget).Name}>（原因：映射结果为 null，回调未执行）");
        }
    }

    /// <summary>
    /// 更新指定的目标对象
    /// <para><b>拷贝行为说明</b>：与 <see cref="Map{TSource, TTarget}(TSource, Action{TSource, TTarget}?)"/> 一致——
    /// 同类型引用类型属性为浅拷贝（共享引用），不同类型引用类型属性为深拷贝。</para>
    /// <para><b>null 行为</b>：source 或 target 为 null 时为 no-op（静默返回），不抛异常。</para>
    /// </summary>
    /// <param name="source">源对象</param>
    /// <param name="target">需要更新的目标对象；为 null 时 no-op 静默返回</param>
    /// <param name="action">方法，可在此处对对象进行后续操作，默认为空</param>
    [RequiresDynamicCode("Map uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Map scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public static void Map<TSource, TTarget>(TSource source, TTarget target, Action<TSource, TTarget>? action = null) where TSource : class where TTarget : class
    {
        // null 守卫 — source/target 为 null 时静默返回（no-op），避免 NullReferenceException
        if (source == null) return;
        if (target == null) return;

        // 可观测性指标 — 仅 MetricsEnabled 时计数（省去 2 次 Interlocked.Increment 开销）
        RecordMapCall();

        // T2 修复：Pipeline 覆盖 Update 重载

        // GeneratedCache 路径是编译时代码，不存在循环引用风险，无需 try-finally 递归保护
        if (GeneratedCache<TSource, TTarget>.UpdateAction is { } updateGen)
        {
            RecordCacheHit();
            updateGen(source, target, action);
        }
        else
        {
            // GeneratedCache 未命中 → 回退 MapperBase 运行时路径（MapperBase 内部已有递归深度保护）
            RecordCompilation();
            MapperBase<TSource, TTarget>.Map(source, target, action);
        }

        // Trace 开关开启时输出映射详情
        if (MapTracer.GloballyEnabled)
            MapTracer.WriteTrace(source, target, null);

        // T2 修复：Pipeline PostMap（Update 重载就地修改 target，可直接传给 PostMap）
        if (Mapper.Pipeline.IsEnabled)
        {
            Pipeline.ExecutePostMap(source, target);
        }
    }

    /// <summary>
    /// (同类转换)将指定列表对象转换为相同对象列表
    /// <para><b>拷贝行为说明</b>：列表本身为新建（独立集合），
    /// 但元素的同类型引用类型属性为<b>浅拷贝</b>（共享引用）。
    /// 如需元素也完全独立，请先对每个元素调用 <see cref="DeepCopy{T}(T)"/>。</para>
    /// </summary>
    /// <param name="sources">源列表对象</param>
    /// <param name="action">方法，可在此处对对象进行后续操作，默认为空</param>
    /// <returns>转换后的目标列表；sources 为 null 时返回 null</returns>
    [RequiresDynamicCode("MapList uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("MapList scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public static List<TTarget>? MapList<TTarget>(IEnumerable<TTarget> sources, Action<TTarget, TTarget>? action = null) where TTarget : class
    {
        if (sources == null) return null;

        // 可观测性指标 — 仅 MetricsEnabled 时计数
        RecordMapCall();

        // dispatch 收敛到 MapListCore（Pipeline 逐元素 / ListFunc 批量 / 生成 Func / 回退）；
        // 同类重载自动补齐生成 Func 逐元素分支（与异类重载一致性， 仅补了异类）
        return MapListCore<TTarget, TTarget>(sources, action);
    }

    /// <summary>
    /// (异类转换)将指定列表对象转换为其他列表对象
    /// <para><b>拷贝行为说明</b>：列表为新建（独立集合），元素为新建对象递归映射（深拷贝）。
    /// 同类型引用类型属性仍为浅拷贝（与单对象 Map 行为一致）。</para>
    /// </summary>
    /// <param name="sources">源列表对象</param>
    /// <param name="action">方法，可在此处对对象进行后续操作，默认为空</param>
    /// <returns>转换后的目标列表；sources 为 null 时返回 null</returns>
    [RequiresDynamicCode("MapList uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("MapList scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public static List<TTarget>? MapList<TSource, TTarget>(IEnumerable<TSource> sources, Action<TSource, TTarget>? action = null) where TSource : class where TTarget : class
    {
        if (sources == null) return null;

        // 可观测性指标 — 仅 MetricsEnabled 时计数
        RecordMapCall();

        // dispatch 收敛到 MapListCore（Pipeline 逐元素 / ListFunc 批量 / 生成 Func / 回退）
        return MapListCore<TSource, TTarget>(sources, action);
    }

    /// <summary>
    /// (异类转换)批量就地映射 — 将源集合逐元素填充进调用方预分配的目标数组，仅覆盖映射字段
    /// <para><b>复用契约</b>：调用方负责预分配 destination（长度 ≥ sources.Count）并初始化每个元素
    /// （destination[i] != null）；同一数组跨调用复用时，未映射字段保留上轮残留值（C6，调用方按需自行清理）。
    /// 复用场景下顶层对象零分配。</para>
    /// <para><b>null 处理（过程与意义）</b>：入口按 C1 → C8 → C2 顺序校验——
    /// ① destination 为 null 抛 <see cref="ArgumentNullException"/>（C1，优先级最高：预分配契约被违反必须显式暴露）；
    /// ② sources 为 null 直接返回，不做任何填充（C8，空源视为无操作，且不豁免 C1）；
    /// ③ destination.Length &lt; sources.Count 抛 <see cref="ArgumentException"/>（C2，防越界，显式失败）。
    /// 元素级逐槽位处理：④ 源元素 null → 该槽位保持原值、不覆盖（C4，"有数据才更新"——源无数据则不动目标，
    /// 避免破坏常驻目标数组中上轮已有的有效内容）；⑤ 目标槽位 null → 跳过 no-op（C5，更新对象须已存在，
    /// null 目标无可赋值对象，宽容跳过而非抛异常）；⑥ 仅填充前 sources.Count 个槽位，超量槽位保持原值。
    /// 组合效果：只对「源非 null 且 目标非 null」的槽位执行更新，其余槽位保持调用前状态——
    /// 目标数组在多次刷新复用中始终安全、稳定、可预测，不会写入 null 或清空已有数据。</para>
    /// <para><b>拷贝行为说明</b>：映射属性为就地赋值；嵌套引用类型属性每轮新建子对象
    /// （dst.Child = Map_...(src.Child)，非就地更新）；顶层零分配，全图非零分配。</para>
    /// </summary>
    /// <param name="sources">源集合；null 时直接返回，不做任何填充（C8，C1 不受其豁免）</param>
    /// <param name="destination">预分配目标数组；null 时抛 <see cref="ArgumentNullException"/>（C1，优先级最高）；
    /// 长度必须 ≥ sources.Count（C2，否则抛 <see cref="ArgumentException"/>）；仅填充前 sources.Count 个槽位，
    /// 超量槽位保持原值</param>
    /// <param name="action">方法，每个元素映射后调用 action(src, dst)，默认为空</param>
    /// <exception cref="ArgumentNullException">destination 为 null</exception>
    /// <exception cref="ArgumentException">destination.Length &lt; sources.Count</exception>
    /// <exception cref="MapException">映射递归深度超过上限时抛出（自引用类型深度保护 / MapperBase 回退路径）</exception>
    [RequiresDynamicCode("MapListInto uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("MapListInto scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public static void MapListInto<TSource, TTarget>(IList<TSource> sources, TTarget[] destination, Action<TSource, TTarget>? action = null) where TSource : class where TTarget : class
    {
        // （v1.2）：C1 优先 — destination==null 始终抛（调用方预分配契约被违反必须暴露），先于 sources==null 判断
        ArgumentNullException.ThrowIfNull(destination);

        // null 透传（destination 非 null）
        if (sources == null) return;

        // 长度校验（显式失败）
        if (destination.Length < sources.Count)
            throw new ArgumentException(
                $"destination.Length ({destination.Length}) must be >= sources.Count ({sources.Count})",
                nameof(destination));

        // 可观测性指标 — 仅 MetricsEnabled 时计数（与 Map 入口同口径）
        RecordMapCall();

        // dispatch 收敛到 MapListIntoCore（Pipeline 内联 / ListIntoAction 批量 / UpdateAction 循环 / 回退）
        MapListIntoCore(sources, destination, action);
    }

    /// <summary>
    /// 映射验证开关 — 默认 true（开启），设为 false 关闭验证功能
    /// <para>CW使用 volatile 确保多线程下设置后立即对其他线程可见</para>
    /// </summary>
    public static bool ValidationEnabled
    {
        get => s_validationEnabled;
        set => s_validationEnabled = value;
    }

    /// <summary>
    /// 验证总开关（volatile 保证多线程可见性）
    /// </summary>
    private static volatile bool s_validationEnabled = true;

    /// <summary>
    /// 最大映射递归深度 — 默认 64，超过此值视为循环引用抛异常
    /// <para>增大此值可支持更深的嵌套对象映射，但可能掩盖循环引用问题</para>
    /// <para>CW上限 4096，防止设置为极大值导致资源耗尽</para>
    /// <para>CW使用 volatile 确保多线程下设置后立即对其他线程可见</para>
    /// </summary>
    public const int MaxRecursionDepthUpperBound = 4096;

    /// <summary>
    /// 最大映射递归深度默认值（判据源单一化）
    /// 原三处 64 字面量（本字段 初始化 / NCMapperOptions.MaxRecursionDepth 默认值）收敛至此单点。
    /// <para>internal 而非原拟 public：G6 公开 API 快照零 diff 门禁优先（public const 必入
    /// approved.txt，见 MaxRecursionDepthUpperBound 先例），两消费方均同程序集，internal 达成目的。</para>
    /// </summary>
    internal const int DefaultMaxRecursionDepth = 64;

    /// <summary>
    /// 最大映射递归深度（默认 64，volatile 保证多线程可见性）
    /// </summary>
    private static volatile int s_maxRecursionDepth = DefaultMaxRecursionDepth;
    /// <summary>
    /// 最大映射递归深度 — 默认 64，超过此值视为循环引用抛异常
    /// <para>设置范围 [1, MaxRecursionDepthUpperBound]，超出自动钳制</para>
    /// </summary>
    public static int MaxRecursionDepth
    {
        get => s_maxRecursionDepth;
        set => s_maxRecursionDepth = value > MaxRecursionDepthUpperBound
            ? MaxRecursionDepthUpperBound
            : (value < 1 ? 1 : value);
    }

    /// <summary>
    /// 表达式树构建时的最大内联深度 — 默认 5，超过此层数的嵌套对象回退到委托调用
    /// <para>增大此值可内联更多层嵌套（减少委托调用开销），但会增加编译时间和表达式树复杂度</para>
    /// <para>CW上限 16，防止设置为极大值导致表达式树编译时栈溢出</para>
    /// <para>CW使用 volatile 确保多线程下设置后立即对其他线程可见</para>
    /// </summary>
    public const int MaxInlineDepthUpperBound = 16;

    /// <summary>
    /// 表达式树构建最大内联深度（默认 5，volatile 保证多线程可见性）
    /// </summary>
    private static volatile int s_maxInlineDepth = 5;
    /// <summary>
    /// 表达式树构建时的最大内联深度 — 默认 5，超过此层数的嵌套对象回退到委托调用
    /// <para>设置范围 [0, MaxInlineDepthUpperBound]，超出自动钳制</para>
    /// </summary>
    public static int MaxInlineDepth
    {
        get => s_maxInlineDepth;
        set => s_maxInlineDepth = value > MaxInlineDepthUpperBound
            ? MaxInlineDepthUpperBound
            : (value < 0 ? 0 : value);
    }

    /// <summary>
    /// 映射诊断 Trace 全局开关 — 默认 false（关闭），设为 true 后所有映射操作输出 Trace
    /// </summary>
    public static bool TraceEnabled
    {
        get => MapTracer.GloballyEnabled;
        set => MapTracer.GloballyEnabled = value;
    }

    /// <summary>
    /// 映射诊断 Trace 输出目标 — 默认 Console.Out
    /// </summary>
    public static System.IO.TextWriter TraceOutput
    {
        get => MapTracer.GlobalOutput;
        set => MapTracer.GlobalOutput = value;
    }

    /// <summary>
    /// 验证所有 [MapFrom] 标注的映射对，返回详细报告
    /// 需 Mapper.ValidationEnabled == true 才执行，否则返回空列表
    /// <para><b>路径选择</b>：有源生成器注册时返回编译时验证结果（零反射，AOT 友好）；
    /// 无注册时走运行时 AppDomain 扫描路径（需反射）。</para>
    /// </summary>
    /// <returns>映射验证结果列表</returns>
    [System.Diagnostics.CodeAnalysis.Experimental("NCMapper001")]
    public static List<MapValidationResult> Validate()
    {
        if (!ValidationEnabled) return new List<MapValidationResult>();

        // 层1：有源生成器注册 → 聚合全部槽位的编译时验证结果（零反射，AOT 友好）
        // 多槽位字典聚合，多程序集结果不再互覆盖；逐委托 ?? [] 防御
        // （public API 手工注册可能返回 null，聚合 SelectMany 遇 null 会抛 ArgumentNullException，
        // 防御后行为对齐旧单槽位"不因单个委托返回 null 而崩"）
        if (s_generatedValidateFuncs.Count > 0)
            return s_generatedValidateFuncs.Values
                .SelectMany(f => f() ?? new List<MapValidationResult>())
                .ToList();

        // 层2：无源生成器 → 走运行时反射路径
        // R2-MapValidator.Validate() 已带 Requires* 标注（反射扫描程序集）。此处为运行时回退
        // 路径——当且仅当无源生成器注册时执行，属用户显式接受的反射路径。Validate() 本身按 S5
        // 约束保持未标注（AOT 用户入口，源生成器注册时零反射），故在本调用点用 #pragma 精确抑制
        // （[UnconditionalSuppressMessage] 仅作用于发布期 ILLink/ILC，对编译期分析器无效， 已实证）。
#pragma warning disable IL2026 // RequiresUnreferencedCode
#pragma warning disable IL3050 // RequiresDynamicCode
        return MapValidator.Validate();
#pragma warning restore IL3050
#pragma warning restore IL2026
    }

    /// <summary>
    /// 验证指定程序集中所有 [MapFrom] 标注的映射对，返回详细报告
    /// 需 Mapper.ValidationEnabled == true 才执行，否则返回空列表
    /// <para><b>AOT/Trimming</b>：此重载使用反射扫描程序集和类型，AOT 下不可用。
    /// AOT 用户请使用无参 Validate() 方法。</para>
    /// </summary>
    /// <param name="assemblies">需要扫描的程序集；为空时扫描 AppDomain 全部程序集</param>
    /// <returns>映射验证结果列表</returns>
    [System.Diagnostics.CodeAnalysis.Experimental("NCMapper001")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Validate(params Assembly[]) uses reflection to scan assemblies and types. Use Validate() for AOT-compatible validation.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Validate(params Assembly[]) uses reflection to scan assemblies and types. Use Validate() for AOT-compatible validation.")]
    public static List<MapValidationResult> Validate(params System.Reflection.Assembly[] assemblies)
    {
        if (!ValidationEnabled) return new List<MapValidationResult>();
        return MapValidator.Validate(assemblies);
    }

    /// <summary>
    /// 验证所有映射配置，发现 Error 级别问题时抛出 <see cref="MapConfigurationException"/>
    /// <para>用法：在 Program.cs 启动时调用一行即可获得完整安全网</para>
    /// <para><code>Mapper.ValidateOrThrow(); // 有问题直接崩溃，不会带着 bug 上线</code></para>
    /// <para>验证通过时不抛异常；验证失败时抛 <see cref="MapConfigurationException"/>，包含所有 Error 级别问题摘要</para>
    /// </summary>
    /// <exception cref="MapConfigurationException">验证结果中存在 Error 级别问题时抛出</exception>
    [System.Diagnostics.CodeAnalysis.Experimental("NCMapper001")]
    public static void ValidateOrThrow()
    {
        var results = Validate();
        var errors = results.Where(r => r.Level == MapValidationLevel.Error).ToList();
        if (errors.Count > 0)
        {
            // Message 自带前导标点（zh 全角"，" / en ASCII ", "，供 MapValidator.ToString 报表行
            // "…属性已映射，N 个…"拼接）；此处类型对后已有 ": " 分隔，须去掉前导标点，
            // 否则输出悬空的 ": ，N 个类型不兼容" / ": , 1 incompatible type(s)"
            var summary = string.Join("; ", errors.Select(e =>
                $"{e.SourceType?.Name}→{e.TargetType?.Name}: {e.Message.TrimStart('，', ',', ' ')}"));
            throw new MapConfigurationException(
                ErrorMessages.MappingValidationFailed(errors.Count, summary));
        }
    }

    /// <summary>
    /// 深拷贝对象 — 转发到 <see cref="DeepCopyProviderBase.Current"/>
    /// <para>与 <see cref="Map{TSource}(TSource, Action{TSource,TSource}?)"/> 不同，此方法对所有引用类型属性递归创建独立副本，
    /// 拷贝后的对象与原对象完全独立（修改任何嵌套属性都不影响原对象）。</para>
    /// <para>默认使用表达式树编译深拷贝（接近手写性能），不支持的表达式树编译的类型（如 record、集合属性）自动回退到 JSON 深拷贝</para>
    /// <para>支持循环引用检测，用户可通过替换 <see cref="DeepCopyProviderBase.Current"/> 自定义深拷贝策略</para>
    /// <para><b>性能优化</b>：顶层入口复用 [ThreadStatic] Dictionary + DeepCopyContext（每线程仅首次分配），
    /// 递归调用通过 ctx 参数传递（0 ThreadStatic 访问、0 堆分配），顶层退出 Clear() 复用。</para>
    /// 调用示例：var copy = Mapper.DeepCopy(entity);
    /// </summary>
    /// <remarks>
    /// <b>路径与循环引用检测</b>：本方法按以下顺序选择执行路径：
    /// <list type="number">
    /// <item><b>层 1（源生成路径）</b>：类型已通过 <c>[MapFrom]</c> 标注并注册源生成 DeepCopy 委托时命中。
    /// 顶层入口从 [ThreadStatic] 获取复用的 Dictionary 并包装到 <see cref="DeepCopyContext"/>，
    /// 递归调用通过 ctx 参数传递（0 ThreadStatic 访问），循环引用图（如 A.Self = A 自引用、A↔B 双向引用）
    /// 通过 ctx.CycleCache 正确打破，不栈溢出。
    /// <b>仅对含无参构造器的类型生效</b>——源生成代码在 <c>var dst = new T()</c> 之后立即调用
    /// <c>Mapper.RegisterDeepCopyTarget(src, dst, ctx)</c> 登记，递归调用 <c>Mapper.DeepCopy(src.Prop, ctx)</c>
    /// 时能立即命中已登记的 dst。</item>
    /// <item><b>层 2/3（运行时 Provider 路径）</b>：源生成路径未命中时，转发到
    /// <see cref="DeepCopyProviderBase.Current"/>（默认 <c>FastDeepCopyProvider</c> 或
    /// <c>ExpressionTreeDeepCopyProvider</c>）。Provider 内部使用 ctx.CycleCache
    /// 循环检测，对所有类型（含 record、集合）均生效。</item>
    /// </list>
    /// <para><b>已知限制 — record 类型 + 循环引用图</b>：源生成路径的 record 分支
    /// （<c>GenerateDeepCopyRecordPath</c>，通过构造器参数匹配创建 dst）无法在构造器调用前登记
    /// src → dst 映射，因为 <c>dst</c> 在 <c>new T(arg1, arg2, ...)</c> 内部才创建，
    /// 而构造器参数本身可能就是 <c>Mapper.DeepCopy(src.Prop)</c>（若 <c>src.Prop</c> 循环回 src）。
    /// 此场景下源生成路径会栈溢出。<b>解决方法</b>：对存在循环引用的 record 类型，请使用
    /// <see cref="DeepCopyProviderBase.Current"/>.DeepCopy(source) 直接走运行时 Provider 路径，
    /// 或通过 <c>Mapper.RegisterGeneratedDeepCopy&lt;T&gt;(null)</c> 注销该类型的源生成委托使其回退。</para>
    /// </remarks>
    /// <typeparam name="T">对象类型（必须为引用类型）</typeparam>
    /// <param name="source">需要深拷贝的源对象</param>
    /// <returns>深拷贝后的新对象；source 为 null 时返回 default</returns>
    [RequiresDynamicCode("DeepCopy uses Expression.Compile and MakeGenericMethod in the runtime fallback path. Not supported in AOT. Use the source-generated deep copy registered by RegisterGeneratedDeepCopy to make this AOT-safe.")]
    [RequiresUnreferencedCode("DeepCopy scans type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated deep copy registered by RegisterGeneratedDeepCopy to make this AOT-safe.")]
    public static T? DeepCopy<T>(T source) where T : class
    {
        if (source == null) return null;

        var ctx = GetOrCreateDeepCopyCtx();
        bool isTopLevel = ctx.Depth == 0;

        // A1-03上下文隔离：外层会话（Map/MapList 回调、嵌套顶层调用）
        // 存续期间（Depth > 0）换入独立缓存，finally 恢复外层视图并丢弃独立缓存——
        // 回调内 DeepCopy 不再覆盖外层 CycleCache、条目不残留跨回调（原缺陷：同一 Map 会话内
        // 第二次回调内 DeepCopy 同一 source 实例命中残留条目，静默返回第一次旧副本）；
        // 嵌套顶层调用一并隔离（Depth>0 无法区分场景来源；嵌套共享为既有语义，行为变更见发版告知）。
        // Fast 三字段（FastSources/FastTargets/FastSlot）一并换入/恢复：Provider 路径祖先链环检测
        // 状态挂在共享 ctx 上，漏换则内层条目残留外层环（与 A1-03 同型的隔离不彻底残留面）。
        // 深度守卫保持共享 ctx（Depth 累计计数不变，跨回调累计守卫语义保留）。
        Dictionary<object, object> cache;
        Dictionary<object, object>? savedCycleCache = null;
        object?[]? savedFastSources = null;
        object?[]? savedFastTargets = null;
        int savedFastSlot = 0;
        if (isTopLevel)
        {
            // 方向 I 补充优化：ThreadStatic Dictionary + ctx 复用（0 堆分配/次，0 递归 ThreadStatic 访问）
            // 顶层：2 次 ThreadStatic 访问获取缓存实例（每线程仅首次分配）
            // 递归：通过 ctx 参数传递，0 次 ThreadStatic 访问（不受 net9/10 GETDYNAMIC ABI 影响）
            // 退出：Clear() Dictionary 复用（不释放，下次调用直接使用）
            cache = s_cycleCache ??= new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            ctx.CycleCache = cache;
        }
        else
        {
            // A1-03 隔离：换入独立 CycleCache（不读不写 s_cycleCache）+ 置空 Fast 三字段，
            // 外层视图在 finally 恢复
            cache = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            savedCycleCache = ctx.CycleCache;
            savedFastSources = ctx.FastSources;
            savedFastTargets = ctx.FastTargets;
            savedFastSlot = ctx.FastSlot;
            ctx.CycleCache = cache;
            ctx.FastSources = null;
            ctx.FastTargets = null;
            ctx.FastSlot = 0;
        }

        try
        {
            return DeepCopy(source, ctx);
        }
        finally
        {
            if (isTopLevel)
            {
                // 仅最外层调用清除缓存（重入时 Depth > 0，不清除，防止破坏外层循环引用检测）
                cache.Clear();
            }
            else
            {
                // 恢复外层视图（CycleCache 引用 + Fast 三字段），独立缓存随之 GC
                ctx.CycleCache = savedCycleCache;
                ctx.FastSources = savedFastSources;
                ctx.FastTargets = savedFastTargets;
                ctx.FastSlot = savedFastSlot;
            }
        }
    }

    /// <summary>
    /// 带上下文的深拷贝入口 — 供递归调用复用同一 <see cref="DeepCopyContext"/>
    /// <para>方向 I 优化：用 ctx 参数替代 [ThreadStatic] s_deepCopyInProgress/s_deepCopyDepth，
    /// 避免 net9/10 ThreadStatic ABI 从 SHARED 改为 DYNAMIC 导致的性能回退。</para>
    /// <para>Generated 路径用 ctx.CycleCache 替代 s_deepCopyInProgress；
    /// Provider 路径转发到 provider.DeepCopy(source, ctx) internal virtual。</para>
    /// <para>顶层调用创建新 ctx，递归调用复用同一 ctx，顶层退出后 ctx 被丢弃
    /// （CycleCache 随之 GC，无需显式 Clear）。</para>
    /// </summary>
    /// <typeparam name="T">对象类型（必须为引用类型）</typeparam>
    /// <param name="source">需要深拷贝的源对象</param>
    /// <param name="ctx">深拷贝上下文（承载循环引用缓存和递归深度）</param>
    /// <returns>深拷贝后的新对象；source 为 null 时返回 default</returns>
    /// <exception cref="MapException">深拷贝失败（递归深度超限或对象图无法深拷贝时抛出）</exception>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    [RequiresDynamicCode("DeepCopy uses Expression.Compile and MakeGenericMethod in the runtime fallback path. Not supported in AOT. Use the source-generated deep copy registered by RegisterGeneratedDeepCopy to make this AOT-safe.")]
    [RequiresUnreferencedCode("DeepCopy scans type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated deep copy registered by RegisterGeneratedDeepCopy to make this AOT-safe.")]
    public static T? DeepCopy<T>(T source, DeepCopyContext ctx) where T : class
    {
        if (source == null) return null;
        // 层1：源生成器有注册 → 直接调编译时生成的 DeepCopy（~100ns）
        if (GeneratedDeepCopyCache<T>.Func is { } genDeepCopy)
        {
            // 方案 F 层 1 条件化：无环类型编译期已证明无环 → 跳过 CycleCache，仅保留深度守卫。
            // 子图封闭性引理：根类型 Acyclic 时其可达类型必为 Acyclic，
            // 嵌套 Mapper.DeepCopy(src.Prop, ctx) 在本层按各自 IsAcyclic 标志同样免检测。
            if (GeneratedDeepCopyCache<T>.IsAcyclic)
            {
                ctx.Depth++;
                try
                {
                    // 深度守卫保留 — 无环图也可能很深，且是分类器缺陷的兜底
                    if (ctx.Depth > MaxRecursionDepth)
                        throw new MapException(
                            ErrorMessages.RecursionDepthExceeded(MaxRecursionDepth, typeof(T).Name, typeof(T).Name),
                            typeof(T), typeof(T));
                    return genDeepCopy(source, ctx);
                }
                finally
                {
                    ctx.Depth--;
                }
            }

            // T1源生成路径循环引用检测
            // 方向 I：用 ctx.CycleCache 替代 [ThreadStatic] s_deepCopyInProgress，
            // 递归调用 Mapper.DeepCopy(src.Prop, ctx) 复用同一 ctx
            // Cyclic/Unknown（含旧版生成代码，IsAcyclic 默认 false）→ CycleCache 完整检测路径
            ctx.CycleCache ??= new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            ctx.Depth++;
            try
            {
                // 范围扩大：Cyclic 分支深度保护补齐
                // 与 Acyclic 分支（:902）同构：深链（非环，CycleCache 不命中）超过 MaxRecursionDepth
                // 仍须抛 MapException 防栈溢出（生成器修复前 DeepCopy 走 FastDeepCopyProvider 有保护，
                // 修复后走生成委托 + CycleCache，Cyclic 分支缺 Depth 检查 → 深链不抛）。
                if (ctx.Depth > MaxRecursionDepth)
                    throw new MapException(
                        ErrorMessages.RecursionDepthExceeded(MaxRecursionDepth, typeof(T).Name, typeof(T).Name),
                        typeof(T), typeof(T));
                if (ctx.CycleCache.TryGetValue(source, out var existing))
                {
                    // A1-07运行时兜底：手工委托递归重入（委托未调
                    // RegisterDeepCopyTarget 协作登记）——哨兵命中即创建占位实例登记返回，打破环
                    //（provider「占位即最终对象」同款协议；外层委托返回后回拷占位并返回占位）
                    if (ReferenceEquals(existing, DeepCopyProviderBase.ManualCopyInFlight))
                    {
                        var manualPlaceholder = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(source.GetType());
                        ctx.CycleCache[source] = manualPlaceholder;
                        return (T)manualPlaceholder;
                    }
                    return (T)existing;
                }
                // 先登记哨兵——原登记在 genDeepCopy 返回后（:1111-1113），委托执行期内
                // 环回边不可见 → 同源重入无限自递归抛 RecursionDepthExceeded（手工委托/未协作
                // 登记的旧版生成代码；协作登记的生成代码首行 RegisterDeepCopyTarget 原位覆盖哨兵）
                ctx.CycleCache[source] = DeepCopyProviderBase.ManualCopyInFlight;
                try
                {
                    var result = genDeepCopy(source, ctx);
                    if (result != null)
                    {
                        if (ctx.CycleCache.TryGetValue(source, out var registered))
                        {
                            if (ReferenceEquals(registered, DeepCopyProviderBase.ManualCopyInFlight))
                                ctx.CycleCache[source] = result;   // 无环回——委托结果原位替换哨兵
                            else if (!ReferenceEquals(registered, result))
                            {
                                // 环回发生（内层已登记占位）——回拷占位并返回占位（占位即最终对象，
                                // 环回边与缓存一致；委托自建实例被占位替身，仅公开可读属性面经回拷保留）
                                DeepCopyProviderBase.BackfillPlaceholder(registered, result, source.GetType());
                                result = (T)registered;
                            }
                        }
                    }
                    else
                    {
                        ctx.CycleCache.Remove(source);   // 委托返 null：哨兵不残留
                    }
                    return result;
                }
                catch
                {
                    // 异常收敛：哨兵残留会使同 ctx 后续同源引用 (T)哨兵 抛 InvalidCastException
                    if (ctx.CycleCache.TryGetValue(source, out var registeredOnThrow)
                        && ReferenceEquals(registeredOnThrow, DeepCopyProviderBase.ManualCopyInFlight))
                        ctx.CycleCache.Remove(source);
                    throw;
                }
            }
            finally
            {
                ctx.Depth--;
                // 不需要 Clear — ctx 顶层退出后整体 GC，CycleCache 随之释放
            }
        }
        // 层2/3：运行时路径 → FastDeepCopyProvider（默认）或 ExpressionTreeDeepCopyProvider
        // 捕获 Current 到局部变量，防止 DeepCopy 执行过程中其他线程修改 Current 导致策略不一致
        var provider = DeepCopyProviderBase.Current;
        // 方案 F 转发前清环：无环生成链路的嵌套调用（Depth > 0）落到本转发分支时，
        // CycleCache 可能从未初始化（无环分支跳过初始化，基类仅在 Depth==0 时初始化，
        // DeepCopyProviderBase.cs:84-85），其 record 占位逻辑（:110/:129）的 CycleCache! 索引将 NPE。
        // 分配仅发生在"无生成委托"的低频回退路径，顶层 Mapper.DeepCopy 已预挂复用字典，通常零分配。
        ctx.CycleCache ??= new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        return provider.DeepCopy(source, ctx);
    }

    /// <summary>
    /// 深拷贝运行时回退入口 — 跳过源生成器委托，直接走 Provider 路径
    /// <para>供源生成器生成的代码调用：无公共构造器类型回退到运行时，
    /// 避免 <see cref="DeepCopy{T}(T, DeepCopyContext)"/> 重入 GeneratedDeepCopyCache 导致无限递归。</para>
    /// </summary>
    /// <param name="source">需要深拷贝的源对象</param>
    /// <param name="ctx">深拷贝上下文（承载循环引用缓存和递归深度）</param>
    /// <returns>深拷贝后的新对象；source 为 null 时返回 default</returns>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    [RequiresDynamicCode("DeepCopyRuntimeOnly always takes the runtime provider path (Expression.Compile / MakeGenericMethod), bypassing the source-generated delegate. Not supported in AOT. Generated code calls this only for members the generator cannot handle.")]
    [RequiresUnreferencedCode("DeepCopyRuntimeOnly scans type members via reflection. Some members may be trimmed. Generated code calls this only for members the generator cannot handle.")]
    public static T? DeepCopyRuntimeOnly<T>(T source, DeepCopyContext ctx) where T : class
    {
        if (source == null) return null;
        var provider = DeepCopyProviderBase.Current;
        return provider.DeepCopy(source, ctx);
    }

    /// <summary>
    /// 深拷贝对象（含回调） — 深拷贝后执行 action 对结果进行后续调整
    /// <para>调用示例：var copy = Mapper.DeepCopy(entity, (src, tgt) => { tgt.Name = "Modified"; });</para>
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ 使用注意</b>：action 同时接收 source 和 copy 两个参数。在 action 中将 source
    /// 的引用类型属性直接赋值给 copy（如 <c>tgt.Address = src.Address</c>）会破坏深拷贝语义，
    /// 导致 copy 与 source 共享同一引用对象，后续对一方的修改将影响另一方。</para>
    /// <para><b>正确做法</b>：action 中应仅修改 copy 的值类型字段或字符串；若需传递引用属性，
    /// 应对其再次调用 <see cref="DeepCopy{T}(T)"/> 创建独立副本，如
    /// <c>tgt.Address = Mapper.DeepCopy(src.Address!)</c>。</para>
    /// <para>启用 <see cref="MapTracer.GloballyEnabled"/> 后，action 执行前会输出警示信息。</para>
    /// </remarks>
    /// <typeparam name="T">对象类型（必须为引用类型）</typeparam>
    /// <param name="source">需要深拷贝的源对象</param>
    /// <param name="action">深拷贝后的回调，可在此修改结果对象</param>
    /// <returns>深拷贝后的新对象；source 为 null 时返回 default</returns>
    [RequiresDynamicCode("DeepCopy uses Expression.Compile and MakeGenericMethod in the runtime fallback path. Not supported in AOT. Use the source-generated deep copy registered by RegisterGeneratedDeepCopy to make this AOT-safe.")]
    [RequiresUnreferencedCode("DeepCopy scans type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated deep copy registered by RegisterGeneratedDeepCopy to make this AOT-safe.")]
    public static T? DeepCopy<T>(T source, Action<T, T>? action) where T : class
    {
        if (source == null) return null;
        var copy = DeepCopy(source);
        if (copy != null && action != null)
        {
            // 前置守卫——Trace 关闭态免插值字符串分配（DeepCopy 带 action 每次必经热点）
            if (MapTracer.GloballyEnabled)
                MapTracer.Write($"[NCMapper] DeepCopy<{typeof(T).Name}> action 即将执行 — 警告：在 action 中将 source 的引用属性直接赋值给 copy 会破坏深拷贝语义");
            action(source, copy);
        }
        return copy;
    }

    /// <summary>
    /// 异类深拷贝 — 将 TSource 转换为 TTarget 并确保不同类型引用属性递归创建独立副本
    /// <para>内部转发到 <see cref="Map{TSource, TTarget}(TSource, Action{TSource, TTarget}?)"/>，
    /// 因为异类映射对不同类型引用属性天然为深拷贝（创建新对象递归映射）。</para>
    /// <para><b>注意</b>：TSource 和 TTarget 不能为同一类型，同类深拷贝请使用 <see cref="DeepCopy{T}(T)"/>。</para>
    /// 调用示例：var entity = Mapper.DeepCopy&lt;UserDto, UserEntity&gt;(dto);
    /// </summary>
    /// <typeparam name="TSource">源对象类型</typeparam>
    /// <typeparam name="TTarget">目标对象类型</typeparam>
    /// <param name="source">源对象</param>
    /// <returns>深拷贝后的目标对象；source 为 null 时返回 default</returns>
    /// <exception cref="InvalidOperationException">TSource 与 TTarget 为同一类型时抛出</exception>
    [RequiresDynamicCode("Cross-type DeepCopy forwards to Map, which uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Cross-type DeepCopy forwards to Map, which scans type members and attributes via reflection. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public static TTarget? DeepCopy<TSource, TTarget>(TSource source)
        where TSource : class
        where TTarget : class
    {
        if (typeof(TSource) == typeof(TTarget))
            throw new InvalidOperationException(
                ErrorMessages.DeepCopySameTypeNotSupported(typeof(TSource).Name));
        return Map<TSource, TTarget>(source);
    }

    /// <summary>
    /// 异类深拷贝（含回调） — 转换并深拷贝后执行 action 对结果进行后续调整
    /// <para>调用示例：var entity = Mapper.DeepCopy&lt;UserDto, UserEntity&gt;(dto, (s, t) => { t.Name = "Modified"; });</para>
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ 使用注意</b>：action 同时接收 source 和 target 两个参数。在 action 中将 source
    /// 的引用类型属性直接赋值给 target（如 <c>t.Customer = src.Customer</c>）会破坏深拷贝语义，
    /// 导致 target 与 source 共享同一引用对象。</para>
    /// <para><b>正确做法</b>：action 中应仅修改 target 的值类型字段或基于 source 值计算的新值；
    /// 若需传递引用属性，应通过 <see cref="Map{TSource, TTarget}(TSource, Action{TSource, TTarget}?)"/>
    /// 或 <see cref="DeepCopy{T}(T)"/> 创建独立副本。</para>
    /// <para>启用 <see cref="MapTracer.GloballyEnabled"/> 后，action 执行前会输出警示信息。</para>
    /// </remarks>
    /// <typeparam name="TSource">源对象类型</typeparam>
    /// <typeparam name="TTarget">目标对象类型</typeparam>
    /// <param name="source">源对象</param>
    /// <param name="action">转换后的回调，可在此修改结果对象</param>
    /// <returns>深拷贝后的目标对象；source 为 null 时返回 default</returns>
    /// <exception cref="InvalidOperationException">TSource 与 TTarget 为同一类型时抛出</exception>
    [RequiresDynamicCode("Cross-type DeepCopy forwards to Map, which uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Cross-type DeepCopy forwards to Map, which scans type members and attributes via reflection. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public static TTarget? DeepCopy<TSource, TTarget>(TSource source, Action<TSource, TTarget>? action)
        where TSource : class
        where TTarget : class
    {
        if (typeof(TSource) == typeof(TTarget))
            throw new InvalidOperationException(
                ErrorMessages.DeepCopySameTypeNotSupported(typeof(TSource).Name));

        // trace 警示：仅在 MapTracer.GloballyEnabled 时输出，零默认开销
        // source 为 null 时 Map 直接返回 default，action 不会执行，无需输出 trace
        // 前置守卫——Trace 关闭态免插值字符串分配
        if (source != null && action != null && MapTracer.GloballyEnabled)
            MapTracer.Write($"[NCMapper] DeepCopy<{typeof(TSource).Name}→{typeof(TTarget).Name}> action 即将执行 — 警告：在 action 中将 source 的引用属性直接赋值给 target 会破坏深拷贝语义");

        return Map(source!, action);
    }

    /// <summary>
    /// 注册源生成器生成的 DeepCopy 委托（由生成器代码自动调用）
    /// <para>方向 I 优化：public 入口保持 <see cref="Func{T, T}"/> 签名以兼容外部调用方，
    /// 内部包装为 <c>(src, ctx) => func(src)</c> 忽略 ctx（此类委托无循环引用检测，
    /// 仅用于无循环引用图的简单场景）。源生成代码应优先调用
    /// <see cref="RegisterGeneratedDeepCopy{T}(Func{T, DeepCopyContext, T}?)"/> 重载
    /// 以传递 ctx-aware 委托获得循环引用检测能力。</para>
    /// </summary>
    /// <param name="func">深拷贝委托函数，可为 null 以注销注册</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterGeneratedDeepCopy<T>(Func<T, T>? func) where T : class
    {
        GeneratedDeepCopyCache<T>.Func = func == null ? null : (src, ctx) => func(src);
        // 方案 F：旧签名重载语义等价于 acyclic: false（保守），同步重置标志防止残留
        GeneratedDeepCopyCache<T>.IsAcyclic = false;
    }

    /// <summary>
    /// 注册源生成器生成的 ctx-aware DeepCopy 委托（由生成器代码自动调用）
    /// <para>方向 I 优化：接受 <see cref="Func{T, DeepCopyContext, T}"/> 委托，
    /// 源生成代码中递归 <c>Mapper.DeepCopy(src.Prop, ctx)</c> 调用复用同一 ctx，
    /// 通过 ctx.CycleCache 实现循环引用检测（打破自引用 / 双向引用图）。</para>
    /// <para>本方法为 public 是因为源生成器生成的代码在用户程序集里，需要可访问此重载
    /// 注册编译时生成的 ctx-aware 委托。普通用户不应直接调用，应通过
    /// <see cref="RegisterGeneratedDeepCopy{T}(Func{T, T}?)"/> 公开重载或让源生成器自动注册。</para>
    /// </summary>
    /// <param name="func">ctx-aware 深拷贝委托函数，可为 null 以注销注册</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterGeneratedDeepCopy<T>(Func<T, DeepCopyContext, T>? func) where T : class
    {
        GeneratedDeepCopyCache<T>.Func = func;
        // 方案 F：两参重载语义等价于 acyclic: false（保守），同步重置标志防止残留
        GeneratedDeepCopyCache<T>.IsAcyclic = false;
    }

    /// <summary>
    /// 注册源生成器生成的 ctx-aware DeepCopy 委托并声明三值分类结果（由生成器代码自动调用）
    /// <para>方案 F：源生成器在编译期对类型深拷贝可达图做三值分类，
    /// <paramref name="acyclic"/> 为 true（分类为 Acyclic）时层 1 跳过 CycleCache
    /// 实现零检测开销；为 false（Cyclic/Unknown）时走完整检测路径。</para>
    /// <para><b>测试隔离约束（v1.1 F2）</b>：<c>IsAcyclic</c> 与 <c>Func</c> 同受泛型静态字段
    /// 运行时限制，<see cref="ClearGenerated"/> 无法统一清除；逐类型传
    /// <c>RegisterGeneratedDeepCopy&lt;T&gt;(null, false)</c> 可同时重置两者。</para>
    /// </summary>
    /// <param name="func">ctx-aware 深拷贝委托函数，可为 null 以注销注册</param>
    /// <param name="acyclic">源生成器分类结果：true 表示该类型深拷贝可达图无环（Acyclic）</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterGeneratedDeepCopy<T>(Func<T, DeepCopyContext, T>? func, bool acyclic) where T : class
    {
        GeneratedDeepCopyCache<T>.Func = func;
        GeneratedDeepCopyCache<T>.IsAcyclic = func != null && acyclic;
    }

    /// <summary>
    /// 登记 source → dst 的映射到当前深拷贝上下文的循环引用检测表
    /// （由源生成代码在创建 dst 后立即调用）
    /// <para>T1 修复补丁：原版登记逻辑放在 Mapper.DeepCopy 入口的 genDeepCopy 返回后，
    /// 但循环引用图会在 genDeepCopy 执行过程中递归调用回 Mapper.DeepCopy(source)，
    /// 此时表里还没有 source 的登记，无法打破循环，导致栈溢出。</para>
    /// <para>补丁方案：源生成代码在 <c>var dst = new T();</c> 之后立即调用本方法登记，
    /// 这样递归 Mapper.DeepCopy(src.Prop, ctx) 调用（当 src.Prop 循环回 src 时）能立即
    /// TryGetValue 命中已登记的 dst，返回共享引用，打破循环。</para>
    /// <para>方向 I 优化：用 ctx.CycleCache 替代原 [ThreadStatic] s_deepCopyInProgress，
    /// 顶层调用创建新 ctx，递归调用复用同一 ctx，顶层退出后 ctx 整体 GC。</para>
    /// <para>仅在源生成路径（层 1）有效；Provider 路径（层 2/3）有自己的 ConditionalWeakTable 检测。</para>
    /// </summary>
    /// <param name="src">源对象（循环引用图中的节点）</param>
    /// <param name="dst">已创建但可能未填充完所有属性的拷贝对象</param>
    /// <param name="ctx">深拷贝上下文（承载 CycleCache）</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterDeepCopyTarget(object src, object dst, DeepCopyContext ctx)
    {
        ctx.CycleCache ??= new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        ctx.CycleCache[src] = dst;
    }

    /// <summary>
    /// 检查是否有源生成器注册的 DeepCopy 委托
    /// </summary>
    /// <returns>true 表示有源生成器注册的深拷贝委托；false 表示无注册</returns>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool HasGeneratedDeepCopy<T>() where T : class
        => GeneratedDeepCopyCache<T>.Func != null;

}
