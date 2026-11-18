using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using NCMapper.Internal;
namespace NCMapper;
/// <summary>
/// 深拷贝策略抽象基类 — 用户可继承此类替换默认的深拷贝实现
/// <para>内置三个实现：</para>
/// <list type="bullet">
/// <item><see cref="FastDeepCopyProvider"/> — 快速深拷贝（默认，完整处理循环引用 / 自引用对象，~142ns）</item>
/// <item><see cref="ExpressionTreeDeepCopyProvider"/> — 表达式树深拷贝（含循环引用检测，~167ns，有循环引用时切换）</item>
/// <item><see cref="JsonDeepCopyProvider"/> — JSON 序列化中转深拷贝（兼容性兜底，不支持循环引用）</item>
/// </list>
/// <para>用法：继承此类并实现 <see cref="CreateInstance{T}"/> 和 <see cref="FillProperties{T}"/>，然后设置 <see cref="Current"/></para>
/// </summary>
public abstract class DeepCopyProviderBase
{
    // CacheRegistry 登记制自注册：仅登记清理委托，不触发缓存构建（防自触发）
    static DeepCopyProviderBase()
    {
        CacheRegistry.Register("deep-copy-provider", CacheLayer.Full, ClearCache);
    }

    /// <summary>
    /// 全局当前实例 — 默认为 <see cref="FastDeepCopyProvider"/>（完整处理循环引用 / 自引用对象，性能优先）
    /// <para>内置实现：</para>
    /// <list type="bullet">
    /// <item><see cref="FastDeepCopyProvider"/> — 默认，完整处理循环引用 / 自引用对象，~142ns</item>
    /// <item><see cref="ExpressionTreeDeepCopyProvider"/> — 含循环引用检测，~167ns，有循环引用时使用</item>
    /// <item><see cref="JsonDeepCopyProvider"/> — JSON 中转兜底</item>
    /// </list>
    /// </summary>
    public static DeepCopyProviderBase Current
    {
        get => s_current;
        set => s_current = value;
    }

    /// <summary>
    /// 当前深拷贝提供器实例（volatile 保证多线程可见性）
    /// </summary>
    private static volatile DeepCopyProviderBase s_current = new FastDeepCopyProvider();

    /// <summary>
    /// 类型是否有无参公共构造器的缓存 — 避免每次 DeepCopy 递归都反射 GetConstructor
    /// <para>缓存后从 ~50-100ns/次降为 ~2ns/次（字典查找）</para>
    /// </summary>
    private static readonly ConcurrentDictionary<Type, bool> s_hasParameterlessCtor = new();

    /// <summary>
    /// 手工委托递归中哨兵——Mapper 层1 对 Cyclic/Unknown
    /// 委托（手工 RegisterGeneratedDeepCopy 或未协作登记的生成代码）调用 genDeepCopy 前
    /// 写入 CycleCache；递归重入命中哨兵即创建占位实例登记返回（打破环），外层委托返回后
    /// 回拷占位并返回占位（"占位即最终对象"协议，B-02 同款）。仅 Mapper 层1 与本类
    /// CycleCache 命中点消费；FastDeepCopyProvider 走环形缓冲（不受哨兵影响，同源重入
    /// 必经 Mapper 层1 兜底）。
    /// </summary>
    internal static readonly object ManualCopyInFlight = new();

    /// <summary>
    /// 判断类型是否有无参公共构造器（带缓存）— internal 供 <see cref="FastDeepCopyProvider"/>
    /// 分发条件复用
    /// </summary>
    /// <param name="type">目标类型</param>
    /// <returns>有无参公共构造器返回 true</returns>
    internal static bool HasParameterlessConstructor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type)
    {
        // 缓存查询（TryAdd 幂等，并发重复计算无害；避免 GetOrAdd lambda 参数 DAM 触发 IL2111）
        if (!s_hasParameterlessCtor.TryGetValue(type, out var hasCtor))
        {
            hasCtor = type.GetConstructor(Type.EmptyTypes) != null;
            s_hasParameterlessCtor.TryAdd(type, hasCtor);
        }
        return hasCtor;
    }

    /// <summary>
    /// 占位回拷计划缓存 — key: 类型, value: (placeholder, result) => 回拷全部公开可读属性
    /// <para>每类型仅编译一次回拷决策（backing field / setter / 跳过），防热路径反射重复查找。</para>
    /// </summary>
    // 回拷计划编译含属性反射
    // 扫描（backing field / setter / 跳过判定），与委托缓存同模式 Lazy 包装统一并发口径。
    private static readonly ConcurrentDictionary<Type, Lazy<Action<object, object>?>> s_backfillPlanCache = new();

    /// <summary>
    /// 占位回拷 — 将完整实例（result）的公开可读属性值回拷到占位实例（placeholder）
    /// <para>回写优先级（自动属性 backing field
    /// （&lt;Name&gt;k__BackingField 反射回写，覆盖 getter-only/init-only 无 setter 场景）→
    /// 有 setter 属性（走 setter）→ 手写 getter-only 且无 backing field（已知限制，跳过）。</para>
    /// </summary>
    /// <param name="placeholder">占位实例（<see cref="System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject"/> 产物，缓存与返回指向它）</param>
    /// <param name="result">完整实例（构造器 + FillProperties 产物）</param>
    /// <param name="type">运行时类型</param>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("BackfillPlaceholder reflects over type members via GetProperties/GetField. Not supported in AOT. Use the source-generated deep copy path to make this AOT-safe.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("BackfillPlaceholder scans type members via reflection. Some members may be trimmed. Use the source-generated deep copy path to make this AOT-safe.")]
    internal static void BackfillPlaceholder(object placeholder, object result, Type type)
    {
        var plan = s_backfillPlanCache.GetOrAdd(type, static t => new Lazy<Action<object, object>?>(() => CompileBackfillPlan(t))).Value;
        plan?.Invoke(placeholder, result);
    }

    /// <summary>
    /// 编译 占位回拷计划 — 遍历公开实例可读非索引属性，按属性形态选择回写通道并闭包为单委托
    /// </summary>
    /// <param name="type">运行时类型</param>
    /// <returns>回拷委托；无可回写属性时返回 null</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("CompileBackfillPlan scans type members via reflection. Some members may be trimmed.")]
    private static Action<object, object>? CompileBackfillPlan(Type type)
    {
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        List<Action<object, object>>? steps = null;
        foreach (var prop in props)
        {
            if (prop.GetIndexParameters().Length != 0 || !prop.CanRead)
                continue;

            // 优先 backing field：自动属性（含 getter-only/init-only）编译器生成 <Name>k__BackingField，
            // 反射回写可绕过 setter 缺失限制
            var backing = type.GetField($"<{prop.Name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (backing != null)
            {
                steps ??= new List<Action<object, object>>(props.Length);
                steps.Add((ph, res) => backing.SetValue(ph, backing.GetValue(res)));
                continue;
            }

            // 手写属性无 backing field：有 setter 走 setter
            if (prop.CanWrite)
            {
                steps ??= new List<Action<object, object>>(props.Length);
                steps.Add((ph, res) => prop.SetValue(ph, prop.GetValue(res)));
            }

            // 手写 getter-only 且无 backing field：无法回写——已知限制
        }

        if (steps == null || steps.Count == 0)
            return null;
        var captured = steps.ToArray();
        return (ph, res) =>
        {
            foreach (var step in captured)
                step(ph, res);
        };
    }

    /// <summary>
    /// 深拷贝入口 — 处理 null 检查、循环引用检测、两阶段拷贝（创建实例→填充属性）
    /// <para>两阶段拷贝是循环引用检测的关键：</para>
    /// <para>1. 调用 <see cref="CreateInstance{T}"/> 创建空实例</para>
    /// <para>2. 将空实例加入循环引用缓存</para>
    /// <para>3. 调用 <see cref="FillProperties{T}"/> 填充属性（此阶段递归调用 DeepCopy 时能命中缓存）</para>
    /// <para>方向 I 优化：顶层创建 <see cref="DeepCopyContext"/> 并通过方法参数传递，
    /// 替代 [ThreadStatic] 字段，避免 net9/10 ThreadStatic ABI 变化导致的性能回退。</para>
    /// </summary>
    /// <typeparam name="T">对象类型（必须为引用类型）</typeparam>
    /// <param name="source">需要深拷贝的源对象</param>
    /// <returns>深拷贝后的新对象；source 为 null 时返回 null</returns>
    /// <exception cref="MapException">递归深度超过 <see cref="Mapper.MaxRecursionDepth"/>，可能存在循环引用</exception>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("DeepCopy uses RuntimeHelpers.GetUninitializedObject and Expression.Compile in the runtime fallback path. Not supported in AOT. Use the source-generated deep copy path to make this AOT-safe.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DeepCopy scans type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated deep copy path to make this AOT-safe.")]
    public virtual T? DeepCopy<T>(T source) where T : class
    {
        if (source == null) return null;
        return DeepCopy(source, new DeepCopyContext());
    }

    /// <summary>
    /// 带上下文的深拷贝入口 — 供递归调用复用同一 <see cref="DeepCopyContext"/>，保持循环引用检测有效
    /// <para>方向 I 优化：用 ctx 参数替代 [ThreadStatic] s_cycleCache/s_depth，
    /// 避免每次递归 6-8 次 ThreadStatic 访问（net9/10 ABI 从 SHARED 改为 DYNAMIC）。</para>
    /// <para>顶层调用时 ctx.Depth==0，懒初始化 ctx.CycleCache；递归调用复用同一 ctx。
    /// 本入口（Provider 路径）每顶层调用新建 ctx，退出后随 GC 回收；Mapper 顶层（Mapper.cs:822）
    /// 为 ThreadStatic ??= 复用 + 顶层 Clear 保桶。</para>
    /// </summary>
    /// <typeparam name="T">对象类型（必须为引用类型）</typeparam>
    /// <param name="source">需要深拷贝的源对象</param>
    /// <param name="ctx">深拷贝上下文（承载循环引用缓存和递归深度）</param>
    /// <returns>深拷贝后的新对象；source 为 null 时返回 null</returns>
    /// <exception cref="MapException">递归深度超过 <see cref="Mapper.MaxRecursionDepth"/>，可能存在循环引用</exception>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("DeepCopy uses RuntimeHelpers.GetUninitializedObject and Expression.Compile in the runtime fallback path. Not supported in AOT. Use the source-generated deep copy path to make this AOT-safe.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DeepCopy scans type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated deep copy path to make this AOT-safe.")]
    internal virtual T? DeepCopy<T>(T source, DeepCopyContext ctx) where T : class
    {
        if (source == null) return null;

        // 循环引用缓存懒初始化 — 无条件 ??=（幂等，递归重入命中既有实例）。
        // 不限 Depth==0：方案 F 分发器下，Acyclic 快路径递归中途
        // 可能转发基类（集合元素按运行时类型分发命中 Cyclic 派生类型，Depth>0），
        // 若此时 CycleCache 未初始化（公共入口新建 ctx 不经过层 1 ??=），
        // 下方 record 占位/POCO 入缓存写入将 NRE。基类路径自身保证前置条件。
        ctx.CycleCache ??= new Dictionary<object, object>(ReferenceEqualityComparer.Instance);

        try
        {
            ctx.Depth++;

            // 递归深度保护 — 超过 Mapper.MaxRecursionDepth 视为循环引用，
            // 抛出可 catch 的 MapException，防止深层嵌套导致不可 catch 的 StackOverflowException
            if (ctx.Depth > Mapper.MaxRecursionDepth)
                throw new MapException(
                    ErrorMessages.RecursionDepthExceeded(Mapper.MaxRecursionDepth, typeof(T).Name, typeof(T).Name),
                    typeof(T), typeof(T));

            // 循环引用检测 — 已拷贝过的对象直接返回缓存（TryGetValue 检测本体与 return 保留不变）
            // CycleCache 在顶层入口无条件 ??= 保证非空（不限 Depth==0，Acyclic 递归
            // 中途转发基类场景亦覆盖）；Fast Provider 自有路径不走本方法，不存在 null 态。
            // 哨兵透明 miss——命中 ManualCopyInFlight 时不作
            // 缓存命中处理，继续下方两阶段拷贝（本源真实注册后自然覆盖哨兵）。
            // 理由：同源再入本方法的主流形态是运行时回退再分发（生成委托内调
            // Mapper.DeepCopyRuntimeOnly → provider.DeepCopy(同源)——非环，须完整拷贝），
            // 哨兵若作命中处理会返回未初始化占位（RecordUnknown/C06 E2E 实测回归）；
            // 真实环回边的打破点在 Mapper 层1 哨兵分支（DeepCopyRuntimeOnly 不经层1）。
            if (ctx.CycleCache!.TryGetValue(source, out var existing)
                && !ReferenceEquals(existing, ManualCopyInFlight))
            {
                return (T)existing;
            }

            // 接口声明类型转发入口 —— 接口不可实例化，继续走下方
            // 占位分支将对接口 GetUninitializedObject 抛 MemberAccessException（"Cannot create an
            // instance of an interface"）。按 source 运行时具体类型分发（接口的运行时类型必为具体类，
            // 再入 DeepCopy<T> 时 typeof(T) 非接口，不满足转发条件，无递归—— 修复机制）；
            // 必须调带 ctx 的 DeepCopyObjectCore 重载复用同一 ctx，保持循环引用检测有效
            if (typeof(T).IsInterface)
                return (T?)DeepCopyObjectCore(source, ctx);

            // 对于无无参构造器的类型（record），先创建占位实例入缓存
            // 防止 CreateInstance 中 TryCompileRecordCreator 递归 DeepCopy 导致循环引用栈溢出
            // GetConstructor 结果缓存，避免每次递归反射 ~50-100ns
            // 占位即最终对象——构造完成后对 result 补填
            // 构参外可写属性，再将完整实例的公开可读属性回拷到占位（B-02，backing-field
            // 反射回写），缓存与返回始终指向占位，环回边引用一致（无 result/placeholder 分裂）
            if (!HasParameterlessConstructor(typeof(T)))
            {
                // record 类型：先创建占位实例入缓存
                var placeholder = (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));
                ctx.CycleCache![source] = placeholder;

                // CreateInstance 中递归 DeepCopy 能命中占位缓存，不会栈溢出
                var result = CreateInstance(source, ctx);

                // CreateInstance 失败（JSON 反序列化 null 等极端场景）：保持原语义返回 null
                if (result == null)
                {
                    // 清占位缓存——原样残留会使同 ctx 内
                    // 后续对同一 source 的引用命中未初始化空壳（GetUninitializedObject 产物，
                    // 属性全默认值）；失败语义单点收敛（重走占位路径仍失败仍返 null，不产空壳）
                    ctx.CycleCache!.Remove(source);
                    return null;
                }

                if (!ReferenceEquals(result, placeholder))
                {
                    // 构参外可写属性补填——构造器只设置构参匹配的属性，构参外带 setter
                    // 的属性（如 MutNode.Other）静默丢失；填充器对构参内属性同值重赋无副作用
                    FillProperties(source, result, ctx);

                    // 将完整实例的公开可读属性值回拷到占位实例——getter-only/init-only
                    // 属性（无 setter 可走）经 backing-field 反射回写；缓存保持 [source]=placeholder
                    BackfillPlaceholder(placeholder, result, typeof(T));
                }

                // 占位即最终对象：始终返回占位（缓存与返回一致，环回边指向同一完整实例）
                return placeholder;
            }

            // POCO：正常两阶段拷贝
            // 阶段1：创建空实例
            var result2 = CreateInstance(source, ctx);

            // 关键：在填充属性之前就加入缓存，防止循环引用导致无限递归
            if (result2 != null)
                ctx.CycleCache![source] = result2;

            // 阶段2：填充属性（此阶段递归调用 DeepCopy 时会命中缓存中的 result2）
            if (result2 != null)
                FillProperties(source, result2, ctx);

            return result2;
        }
        finally
        {
            ctx.Depth--;
            // 不需要清除 ctx.CycleCache — ctx 顶层退出后整体 GC，CycleCache 随之释放
        }
    }

    /// <summary>
    /// 阶段1：创建目标类型的空实例（new T()），不填充任何属性
    /// <para>子类实现此方法时只需创建实例，不需要拷贝属性数据</para>
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="source">源对象（保证非 null，可用于推断构造器参数）</param>
    /// <returns>新创建的空实例</returns>
    [RequiresDynamicCode("CreateInstance is implemented by concrete providers using MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("CreateInstance is implemented by concrete providers scanning type members and attributes via reflection. Some members may be trimmed.")]
    protected abstract T CreateInstance<T>(T source) where T : class;

    /// <summary>
    /// 阶段2：将源对象的属性深拷贝到目标对象
    /// <para>遇到引用类型属性时，应调用 <see cref="DeepCopy{T}(T)"/> 递归，以触发循环引用检测</para>
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="source">源对象（保证非 null）</param>
    /// <param name="target">目标对象（由 <see cref="CreateInstance{T}"/> 创建，保证非 null）</param>
    [RequiresDynamicCode("FillProperties is implemented by concrete providers using MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("FillProperties is implemented by concrete providers scanning type members and attributes via reflection. Some members may be trimmed.")]
    protected abstract void FillProperties<T>(T source, T target) where T : class;

    /// <summary>
    /// 带上下文的阶段1：创建目标类型的空实例 — 供方向 I 优化路径调用
    /// <para>默认转发到 <see cref="CreateInstance{T}(T)"/>（无 ctx 版本）。
    /// 内置子类（<see cref="FastDeepCopyProvider"/>/<see cref="ExpressionTreeDeepCopyProvider"/>）
    /// override 此方法以将 ctx 传递到 <see cref="DeepCopyCompiler"/>。</para>
    /// <para>外部继承类不 override 此方法时走默认转发，功能正常但无法享受方向 I 优化。</para>
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="source">源对象（保证非 null）</param>
    /// <param name="ctx">深拷贝上下文</param>
    /// <returns>新创建的空实例</returns>
    [RequiresDynamicCode("CreateInstance is implemented by concrete providers using MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("CreateInstance is implemented by concrete providers scanning type members and attributes via reflection. Some members may be trimmed.")]
    internal virtual T CreateInstance<T>(T source, DeepCopyContext ctx) where T : class
        => CreateInstance(source);

    /// <summary>
    /// 带上下文的阶段2：将源对象的属性深拷贝到目标对象 — 供方向 I 优化路径调用
    /// <para>默认转发到 <see cref="FillProperties{T}(T, T)"/>（无 ctx 版本）。
    /// 内置子类 override 此方法以将 ctx 传递到 <see cref="DeepCopyCompiler"/> 编译的委托，
    /// 使递归 DeepCopy 调用复用 ctx 保持循环引用检测有效。</para>
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="source">源对象（保证非 null）</param>
    /// <param name="target">目标对象（保证非 null）</param>
    /// <param name="ctx">深拷贝上下文</param>
    [RequiresDynamicCode("FillProperties is implemented by concrete providers using MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("FillProperties is implemented by concrete providers scanning type members and attributes via reflection. Some members may be trimmed.")]
    internal virtual void FillProperties<T>(T source, T target, DeepCopyContext ctx) where T : class
        => FillProperties(source, target);

    /// <summary>
    /// 强类型委托缓存 — key: 运行时类型, value: 编译后的 (provider, source, ctx) => provider.DeepCopy&lt;T&gt;((T)source, ctx)
    /// <para>用表达式树替代 MethodInfo.Invoke，消除反射调用开销 ~200ns → ~2ns（委托调用）</para>
    /// <para>方向 I 优化：委托签名加 DeepCopyContext 参数，调用 internal virtual DeepCopy&lt;T&gt;(source, ctx)
    /// 以复用 ctx 保持循环引用检测有效，替代 [ThreadStatic] 字段。</para>
    /// <para>委托签名：(DeepCopyProviderBase provider, object source, DeepCopyContext ctx) => object?</para>
    /// </summary>
    // Lazy(ExecutionAndPublication) 包装——GetOrAdd 并发首次
    // miss 时 CompileDeepCopyDelegate 会重复编译（DeepCopyObject/DeepCopyObjectCore 三消费点
    // 同缓存），统一委托缓存并发口径（对照 DeepCopyCompiler s_createCache 先例）。
    private static readonly ConcurrentDictionary<Type, Lazy<Func<DeepCopyProviderBase, object, DeepCopyContext, object?>>> s_delegateCache = new();

    /// <summary>
    /// 深拷贝委托编译计数
    /// CompileDeepCopyDelegate 方法体实际执行次数（Lazy 后并发首次 miss 也只执行 1 次；
    /// internal 测试可见，不计公共 API 面）。
    /// </summary>
    internal static long DelegateCompileCount;

    /// <summary>
    /// 方向 I 取样单点化：找 internal virtual
    /// DeepCopy&lt;T&gt;(T source, DeepCopyContext ctx) 重载（带 ctx 参数）。
    /// <para>ReflectionHelper.GetRequiredMethod 无法按参数类型查找 internal 方法
    /// （BindingFlags.NonPublic），故用 GetMethods + LINQ（原 DeepCopyCompiler.cs:215 与本文件
    /// CompileDeepCopyDelegate 三处逐字重复的取样注释收敛于此）。
    /// 计数判定 fail-fast：基类新增同形状重载时抛出并附候选签名清单，替代 First() 的
    /// 静默任取（编译结果不确定性）。</para>
    /// <para>文档块位置勘误（执行核对意见 001-P1-1）：本方法原插入
    /// CompileDeepCopyDelegate 的 XML 文档中间，致该成员 param 名错挂 / returns 割裂
    /// （CS1572 ×4）——已整体上移至调用方文档块之前，语义零变化。</para>
    /// </summary>
    /// <returns>未闭合泛型方法定义（调用方按目标类型 MakeGenericMethod 闭合）</returns>
    [RequiresUnreferencedCode("ResolveContextualDeepCopyMethod enumerates the type's methods via reflection to locate the internal DeepCopy<T>(T, DeepCopyContext) overload; the reflected members carry their own trimming contracts (RequiresUnreferencedCode / DynamicallyAccessedMembers).")]
    internal static MethodInfo ResolveContextualDeepCopyMethod()
    {
        var candidates = typeof(DeepCopyProviderBase)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.Name == nameof(DeepCopy)
                && m.IsGenericMethod
                && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType == typeof(DeepCopyContext))
            .ToList();
        if (candidates.Count != 1)
        {
            var signatures = string.Join("; ", candidates.Select(m =>
                $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})"));
            throw new InvalidOperationException(
                $"Expected exactly one DeepCopy<T>(T source, DeepCopyContext ctx) overload, found {candidates.Count}: [{signatures}]");
        }
        return candidates[0];
    }

    /// <summary>
    /// 编译强类型深拷贝委托 — 构建表达式树 (provider, source, ctx) => (object)provider.DeepCopy&lt;T&gt;((T)source, ctx)
    /// <para>方向 I 优化：调用 internal virtual DeepCopy&lt;T&gt;(T source, DeepCopyContext ctx) 重载，
    /// 非 public virtual DeepCopy&lt;T&gt;(T source)，以复用 ctx 保持循环引用检测有效。</para>
    /// <para>用 GetMethods + LINQ 查找带 ctx 参数的 internal virtual 重载 — ReflectionHelper.GetRequiredMethod
    /// 无法按参数类型查找 internal 方法（BindingFlags.NonPublic）。</para>
    /// </summary>
    /// <param name="runtimeType">运行时类型（已确保为引用类型）</param>
    /// <returns>编译后的委托</returns>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("DeepCopyObject uses MakeGenericMethod to call the generic DeepCopy method at runtime. This is not supported in AOT/Trimmed scenarios.")]
    [RequiresUnreferencedCode("CompileDeepCopyDelegate scans type members and attributes via reflection. Some members may be trimmed.")]
    private static Func<DeepCopyProviderBase, object, DeepCopyContext, object?> CompileDeepCopyDelegate(Type runtimeType)
    {
        Interlocked.Increment(ref DelegateCompileCount);
        var providerParam = System.Linq.Expressions.Expression.Parameter(typeof(DeepCopyProviderBase), "provider");
        var sourceParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "source");
        var ctxParam = System.Linq.Expressions.Expression.Parameter(typeof(DeepCopyContext), "ctx");
        var castSource = System.Linq.Expressions.Expression.Convert(sourceParam, runtimeType);
        var deepCopyMethod = ResolveContextualDeepCopyMethod().MakeGenericMethod(runtimeType);
        var call = System.Linq.Expressions.Expression.Call(providerParam, deepCopyMethod, castSource, ctxParam);
        var castResult = System.Linq.Expressions.Expression.Convert(call, typeof(object));
        return System.Linq.Expressions.Expression.Lambda<Func<DeepCopyProviderBase, object, DeepCopyContext, object?>>(
            castResult, providerParam, sourceParam, ctxParam).Compile();
    }

    /// <summary>
    /// 非泛型深拷贝入口 — 用于无法在编译时确定类型为引用类型的场景（如泛型集合的元素深拷贝）
    /// <para>内部通过表达式树委托调用 <see cref="DeepCopy{T}(T)"/>，使用运行时类型</para>
    /// <para>值类型（含装箱值类型）直接返回原对象 — 值语义无需深拷贝</para>
    /// <para><b>AOT/Trimming</b>：此方法使用 MakeGenericMethod 反射，AOT 下不可用。
    /// 有源生成器覆盖的 DeepCopy 路径不走此方法。</para>
    /// </summary>
    /// <param name="source">需要深拷贝的源对象</param>
    /// <returns>深拷贝后的新对象；source 为 null 时返回 null；值类型直接返回原对象</returns>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("DeepCopyObject uses MakeGenericMethod to call the generic DeepCopy method at runtime. This is not supported in AOT/Trimmed scenarios.")]
    [RequiresUnreferencedCode("DeepCopyObject scans type members and attributes via reflection. Some members may be trimmed.")]
    public object? DeepCopyObject(object source)
    {
        if (source == null) return null;
        // 值类型（含装箱的 int/double/struct 等）无需深拷贝，直接返回
        var runtimeType = source.GetType();
        if (runtimeType.IsValueType) return source;
        // 优先走表达式树委托缓存（~2ns 字典查找 + 委托调用），避免 MethodInfo.Invoke（~200ns）
        // 方向 I：顶层入口创建新 DeepCopyContext，委托内部调 internal virtual DeepCopy<T>(source, ctx)
        var func = s_delegateCache.GetOrAdd(runtimeType, static t => new Lazy<Func<DeepCopyProviderBase, object, DeepCopyContext, object?>>(() => CompileDeepCopyDelegate(t))).Value;
        return func(this, source, new DeepCopyContext());
    }

    /// <summary>
    /// 元素拷贝委托解析（循环外一次）
    /// <para>供 CollectionCopier 集合深拷贝在元素循环外解析一次委托，循环内直调
    /// （跳过 DeepCopyObjectCore 每元素 GetType+GetOrAdd+Lazy 字典分发链）。
    /// 委托与 DeepCopyObjectCore 内部缓存为同一实例（共享 s_delegateCache），
    /// 语义完全一致：(provider, src, ctx) => provider.DeepCopy&lt;T&gt;((T)src, ctx)。</para>
    /// </summary>
    /// <param name="elemType">元素类型（引用类型）</param>
    /// <returns>编译委托（与 DeepCopyObjectCore 同缓存同语义）</returns>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("GetOrAddElementCopier uses MakeGenericMethod to call the generic DeepCopy method at runtime. This is not supported in AOT/Trimmed scenarios.")]
    [RequiresUnreferencedCode("GetOrAddElementCopier scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Func<DeepCopyProviderBase, object, DeepCopyContext, object?> GetOrAddElementCopier(Type elemType)
        => s_delegateCache.GetOrAdd(elemType, static t => new Lazy<Func<DeepCopyProviderBase, object, DeepCopyContext, object?>>(
            () => CompileDeepCopyDelegate(t))).Value;
    // 值类型元素（含可变引用成员 struct）不经本内核
    // 泛型虚通道 DeepCopy<T> 受 class 约束（MakeGenericMethod 对值类型抛），且 Lazy 工厂
    // 嵌套 lambda 的注解覆盖断裂（IL2026/IL3050，b25v2 门禁实测）；调用方（DeepCopyList）
    // 对值元素改走 CopyElement → DeepCopyObjectCore 值类型分发 CopyStructBoxed（同语义）。
    /// <summary>
    /// 内部静态深拷贝入口 — 直接使用委托缓存，跳过实例方法分发
    /// <para>供 CollectionCopier 引用类型元素深拷贝复用 P1-6 委托缓存</para>
    /// <para>与 <see cref="DeepCopyObject"/> 共享同一委托缓存，避免重复编译</para>
    /// </summary>
    /// <param name="source">需要深拷贝的源对象（调用方已确保非 null 且为引用类型）</param>
    /// <returns>深拷贝后的新对象</returns>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("DeepCopyObjectCore uses MakeGenericMethod to call the generic DeepCopy method at runtime. This is not supported in AOT/Trimmed scenarios.")]
    [RequiresUnreferencedCode("DeepCopyObjectCore scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static object? DeepCopyObjectCore(object source)
    {
        var runtimeType = source.GetType();
        if (runtimeType.IsValueType)
        {
            // 装箱值类型——叶安全（枚举/基元/无可变引用
            // 实例字段）原样返回（值语义）；含可变引用成员的 struct 经 CopyStructBoxed
            // 逐成员深拷贝（对象通道统一口径）
            return Internal.LeafStructGuard.IsLeafSafe(runtimeType)
                ? source
                : DeepCopyCompiler.CopyStructBoxed(source, new DeepCopyContext());
        }
        var func = s_delegateCache.GetOrAdd(runtimeType, static t => new Lazy<Func<DeepCopyProviderBase, object, DeepCopyContext, object?>>(() => CompileDeepCopyDelegate(t))).Value;
        // fallback：无 ctx 调用方创建新 ctx（循环引用检测不完整，仅供无法传递 ctx 的场景）
        return func(s_current, source, new DeepCopyContext());
    }

    /// <summary>
    /// 带上下文的内部深拷贝入口 — 供递归调用（如 CollectionCopier 集合元素深拷贝）复用 ctx
    /// <para>方向 I 优化：复用顶层 ctx 保持循环引用检测有效，避免每个集合元素创建新 ctx
    /// 导致循环引用检测失效（A→List&lt;B&gt;→B.Parent→A 会无限递归栈溢出）。</para>
    /// <para>与 <see cref="DeepCopyObject"/> 共享同一委托缓存，避免重复编译</para>
    /// </summary>
    /// <param name="source">需要深拷贝的源对象（调用方已确保非 null 且为引用类型）</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx 保持循环引用检测有效）</param>
    /// <returns>深拷贝后的新对象</returns>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("DeepCopyObjectCore uses MakeGenericMethod to call the generic DeepCopy method at runtime. This is not supported in AOT/Trimmed scenarios.")]
    [RequiresUnreferencedCode("DeepCopyObjectCore scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static object? DeepCopyObjectCore(object source, DeepCopyContext ctx)
    {
        var runtimeType = source.GetType();
        if (runtimeType.IsValueType)
        {
            // 装箱值类型——叶安全原样返回（值语义）；
            // 含可变引用成员的 struct 经 CopyStructBoxed 逐成员深拷贝（对象通道统一口径，
            // 复用顶层 ctx 保持循环引用检测有效）
            return Internal.LeafStructGuard.IsLeafSafe(runtimeType)
                ? source
                : DeepCopyCompiler.CopyStructBoxed(source, ctx);
        }
        var func = s_delegateCache.GetOrAdd(runtimeType, static t => new Lazy<Func<DeepCopyProviderBase, object, DeepCopyContext, object?>>(() => CompileDeepCopyDelegate(t))).Value;
        return func(s_current, source, ctx);
    }

    /// <summary>
    /// 获取 JSON 深拷贝策略工厂实例 — 非 DI 场景下代替直接实例化 <c>JsonDeepCopyProvider</c>
    /// </summary>
    /// <remarks>
    /// 原 <c>JsonDeepCopyProvider</c> 类已降级为 internal，通过此属性保留公开接入点。
    /// </remarks>
    public static DeepCopyProviderBase JsonProvider => JsonDeepCopyProvider.Instance;

    /// <summary>
    /// 清除委托缓存和构造器检查缓存 — 供 Mapper.ClearCache() 调用
    /// </summary>
    internal static void ClearCache()
    {
        s_delegateCache.Clear();
        s_hasParameterlessCtor.Clear();
        s_backfillPlanCache.Clear(); // 占位回拷计划缓存
    }
}

/// <summary>
/// 基于 JSON 序列化中转的深拷贝实现 — 通过 <see cref="JsonSerializerBase"/> 实现完整深拷贝
/// <para>兼容性最好（能处理任意可序列化对象），但性能较差且不支持循环引用</para>
/// <para>作为 <see cref="ExpressionTreeDeepCopyProvider"/> 的回退方案</para>
/// <para>非 DI 场景可通过 <see cref="DeepCopyProviderBase.JsonProvider"/> 获取 JSON 深拷贝策略实例</para>
/// </summary>
/// <remarks>
/// 降级为 internal 而非直接删除——<see cref="DeepCopyStrategy.Json"/> 依赖内部实例化此类。公开入口统一推荐经过 <see cref="DeepCopyProviderBase.JsonProvider"/> 属性。
/// </remarks>
internal sealed class JsonDeepCopyProvider : DeepCopyProviderBase
{
    /// <summary>
    /// 单例实例 — 供需要 JSON 回退的场景使用
    /// </summary>
    internal static readonly JsonDeepCopyProvider Instance = new();

    /// <inheritdoc/>
    [RequiresDynamicCode("CreateInstance builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("CreateInstance scans type members and attributes via reflection. Some members may be trimmed.")]
    protected override T CreateInstance<T>(T source)
    {
        // JSON 方案无法分两阶段 — 直接在 CreateInstance 中完成完整深拷贝
        // FillProperties 为空操作
        // 注意：JSON 方案不支持循环引用（System.Text.Json 遇到循环引用会抛异常）
        if (typeof(T) == typeof(string))
            return source;

        try
        {
            var json = JsonSerializerBase.Current.Serialize(source);
            return (T?)JsonSerializerBase.Current.Deserialize(json, typeof(T))!;
        }
        catch (Exception ex)
        {
            // JSON 序列化/反序列化失败时抛出 MapException，而非返回原引用违反深拷贝契约
            throw new MapException(
                ErrorMessages.JsonDeepCopyFailed(typeof(T).Name, ex.Message),
                typeof(T), typeof(T), ex);
        }
    }

    /// <inheritdoc/>
    [RequiresDynamicCode("FillProperties is a no-op for JSON strategy; deep copy happens in CreateInstance via JsonSerializerBase. Not supported in AOT.")]
    [RequiresUnreferencedCode("FillProperties is a no-op for JSON strategy; deep copy happens in CreateInstance via JsonSerializerBase. Some members may be trimmed.")]
    protected override void FillProperties<T>(T source, T target)
    {
        // JSON 方案在 CreateInstance 中已完成所有工作，此处为空操作
    }
}
