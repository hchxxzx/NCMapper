using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using NCMapper.Shared;

using NCMapper.Internal;
namespace NCMapper;

/// <summary>
/// 深拷贝可达属性图的三值分类结果 — 方案 F 条件分发依据
/// <para>可靠性不变式（宁可多检测，不可漏检测）：<see cref="Acyclic"/> 类型的运行时对象图
/// 不可能出现深拷贝遍历路径上的环；分类器对边集取过近似，多算边只会把类型推向
/// <see cref="Cyclic"/>/<see cref="Unknown"/>（开启完整检测），永远不会漏检。</para>
/// </summary>
internal enum DeepCopyGraphShape : byte
{
    /// <summary>
    /// 无环 — 所有可达引用边均可解析（具体命名类型/数组/Nullable/集合元素/字典 K-V），且图上无环。
    /// 分发去向：零检测快路径（源生成层 1 跳过 CycleCache；Fast 路径保留环扫描兜底）。
    /// </summary>
    Acyclic,

    /// <summary>
    /// 有环 — 可达图中存在被证明的环。分发去向：完整检测（CycleCache + record 占位）。
    /// </summary>
    Cyclic,

    /// <summary>
    /// 未知 — 存在不可解析边：object/dynamic/接口类型/开放泛型/泛型参数/
    /// 可被重写的 virtual 引用属性。分发去向：完整检测（保守）。
    /// </summary>
    Unknown,
}

/// <summary>
/// 运行时深拷贝类型图三值分类器 — 反射 DFS，每类型仅分类一次（双缓存）
/// <para>热路径缓存：<see cref="Cache{T}.Value"/> 泛型静态字段，JIT 按泛型实例化，
/// 分发时单次字段读取（~1ns）；冷路径 <see cref="ConcurrentDictionary{TKey, TValue}"/>，
/// <c>Classify</c> 仅每类型执行一次。</para>
/// <para>边规则（与源生成侧 ClassifyDeepCopyGraph 共用口径）：
/// 遍历公开实例可读非索引器属性（过近似，不排除只读属性）；容器类型（数组/IEnumerable 实现）
/// 不按属性遍历，按元素建边；字典建 K+V 双边（盲区①修复）；索引器不计边（盲区②修复，
/// 对齐 DeepCopyCompiler.CompileFiller 的 <c>GetIndexParameters().Length == 0</c> 过滤）；
/// 可被重写的 virtual 引用属性归 <see cref="DeepCopyGraphShape.Unknown"/> 边（v1.1 F1，
/// 虚调度静态不可解析）。</para>
/// <para>容器边防递归：字典 K/V 与集合元素不在
/// <see cref="CollectEdgeTargets"/>/<see cref="CollectContainerEdges"/> 内联递归展开，
/// 经 <see cref="AddEdgeTarget"/> 守卫后直接入 targets 交 <see cref="ClassifyCore"/> 主栈
/// 三色标记兜底环检测——自引用容器类型（class B : List&lt;B&gt;）由主栈回边判
/// <see cref="DeepCopyGraphShape.Cyclic"/>，不再内联递归无底致 StackOverflow。</para>
/// <para>终止守卫：onPath/done 集合防检测自身无限递归；节点预算（默认 10,000 次节点访问/根类型，
/// 暂定值待 BCL 真实类型 PoC 校准）防深泛型/BCL 图拖垮首编译，超限归
/// <see cref="DeepCopyGraphShape.Unknown"/>。</para>
/// </summary>
internal static class DeepCopyTypeClassifier
{
    // CacheRegistry 登记制自注册：仅登记清理委托，不触发缓存构建（防自触发）
    static DeepCopyTypeClassifier()
    {
        CacheRegistry.Register("deep-copy-classifier", CacheLayer.Full, ClearCache);
    }

    /// <summary>
    /// 默认节点预算 — 每根类型 10,000 次节点访问上限（暂定， PoC 校准），超限归 Unknown
    /// </summary>
    internal const int DefaultNodeBudget = 10_000;

    /// <summary>
    /// 冷路径分类缓存 — 每类型仅分类一次，后续直接命中
    /// </summary>
    private static readonly ConcurrentDictionary<Type, DeepCopyGraphShape> s_cache = new();

    /// <summary>
    /// 热路径缓存 — JIT 按泛型实例化的静态字段，分发时单次字段读取
    /// </summary>
    /// <typeparam name="T">被分类的实体类型（引用类型）</typeparam>
    public static class Cache<T> where T : class
    {
        /// <summary>
        /// 类型 T 的深拷贝图分类结果（首次访问时触发一次 DFS 分类）
        /// </summary>
        // 静态字段初始化器归属 .cctor，无法用 Requires* 标注（CS0592/IL2116）；[UnconditionalSuppressMessage]
        // 仅作用于发布期 ILLink/ILC，对编译期分析器无效。此处初始化器调用已标注的
        // Classify(Type)，用 #pragma 精确抑制——Classify 本身已带 RequiresUnreferencedCode。
#pragma warning disable IL2026 // RequiresUnreferencedCode
        public static readonly DeepCopyGraphShape Value = Classify(typeof(T));
#pragma warning restore IL2026
    }

    /// <summary>
    /// 清空冷路径分类缓存（s_cache）— 接入 Mapper.ClearCache 链
    /// <para>注意：<see cref="Cache{T}.Value"/> 热路径泛型静态字段为 CLR 限制不可运行时清除，
    /// 已实例化的热路径值保留至进程结束（与 <see cref="Mapper.ClearCache"/> 对 GeneratedCache
    /// 的既有说明口径一致）。清空后首次 <see cref="Classify"/> 重新触发一次 DFS 分类。</para>
    /// </summary>
    internal static void ClearCache() => s_cache.Clear();

    /// <summary>
    /// 冷路径分类缓存条目数（internal 测试钩子 — s_cache 为 private，InternalsVisibleTo 仅放行
    /// internal 成员，验收测试经此钩子断言 ClearCache 后缓存被清空）
    /// </summary>
    internal static int CacheEntryCount => s_cache.Count;

    /// <summary>
    /// 分类指定类型的深拷贝可达属性图（结果缓存，每类型仅分类一次）
    /// </summary>
    /// <param name="type">待分类的类型</param>
    /// <returns>三值分类结果</returns>
    [RequiresUnreferencedCode("Classify walks the reachable property graph via reflection. Some members may be trimmed. Use the source-generated deep copy path to make this AOT-safe.")]
    public static DeepCopyGraphShape Classify(Type type)
        => s_cache.GetOrAdd(type, static t => ClassifyCore(t, DefaultNodeBudget));

    /// <summary>
    /// 不经过缓存的分类（测试钩子 — 允许收缩节点预算验证超限归 Unknown，不污染全局缓存）
    /// </summary>
    /// <param name="type">待分类的类型</param>
    /// <param name="nodeBudget">节点预算（次）</param>
    /// <returns>三值分类结果</returns>
    [RequiresUnreferencedCode("ClassifyUncached scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static DeepCopyGraphShape ClassifyUncached(Type type, int nodeBudget)
        => ClassifyCore(type, nodeBudget);

    /// <summary>
    /// DFS 分类核心 — 三色标记（onPath=灰/done=黑）迭代式遍历，回边命中灰色节点判 Cyclic
    /// </summary>
    /// <param name="root">根类型</param>
    /// <param name="nodeBudget">节点预算（次），超限归 Unknown</param>
    /// <returns>三值分类结果（Cyclic 优先于 Unknown）</returns>
    [RequiresUnreferencedCode("ClassifyCore scans type members and attributes via reflection. Some members may be trimmed.")]
    private static DeepCopyGraphShape ClassifyCore(Type root, int nodeBudget)
    {
        root = Nullable.GetUnderlyingType(root) ?? root;

        var onPath = new HashSet<Type>();
        var done = new HashSet<Type>();
        var stack = new Stack<(Type Type, bool Exit)>();
        var targets = new List<Type>(4);
        stack.Push((root, false));
        bool hasUnknown = false;
        int visits = 0;

        while (stack.Count > 0)
        {
            var (type, exit) = stack.Pop();
            if (exit)
            {
                onPath.Remove(type);
                done.Add(type);
                continue;
            }
            if (done.Contains(type)) continue;
            if (onPath.Contains(type)) return DeepCopyGraphShape.Cyclic;  // 回边 → 证明有环

            // 节点预算守卫 — 防深泛型/BCL 图拖垮首编译（暂定值， PoC 校准）
            if (++visits > nodeBudget) return DeepCopyGraphShape.Unknown;

            type = Nullable.GetUnderlyingType(type) ?? type;
            if (IsLeaf(type)) { done.Add(type); continue; }

            onPath.Add(type);
            stack.Push((type, true));

            if (IsCollectionLike(type))
            {
                // 容器类型不按属性遍历（填充器对集合返回 null 走集合拷贝路径，DeepCopyCompiler.cs:356-357），
                // 按元素/K-V 建边，镜像 CollectionCopier 的实际遍历
                targets.Clear();
                bool unresolvable = false;
                CollectContainerEdges(type, targets, ref unresolvable);
                if (unresolvable) hasUnknown = true;
                foreach (var t in targets) stack.Push((t, false));
                continue;
            }

            // 公开实例属性（非索引器，TypeHelper.GetPublicProperties 已过滤），过近似含只读属性
            foreach (var prop in TypeHelper.GetPublicProperties(type))
            {
                var getter = prop.GetMethod;
                if (getter == null) continue;

                targets.Clear();
                bool unresolvable = false;
                CollectEdgeTargets(prop.PropertyType, targets, ref unresolvable);
                if (unresolvable) { hasUnknown = true; continue; }
                if (targets.Count == 0) continue;  // 叶子属性（值类型/string/Delegate/不透明叶子），无边

                // F1（v1.1）：可被重写的引用属性 → Unknown 边，不跟随声明边。
                // 派生类 override 可返回编译期不可见的环引用（如 public override Base Parent => this;），
                // 虚调度静态不可解析。隐式接口实现为 CLR virtual+final（IsFinal=true），不误判；
                // sealed 类成员不可被重写，不误判。
                if (getter.IsVirtual && !getter.IsFinal && !(prop.DeclaringType?.IsSealed ?? false))
                {
                    hasUnknown = true;
                    continue;
                }

                foreach (var t in targets) stack.Push((t, false));
            }
        }

        return hasUnknown ? DeepCopyGraphShape.Unknown : DeepCopyGraphShape.Acyclic;
    }

    /// <summary>
    /// 解包属性类型，收集需要递归分类的引用类型边目标
    /// <para>解包顺序：Nullable&lt;T&gt; → T、数组 → 元素（递归有底：T[] 的元素恒 ≠ 数组自身）、
    /// 字典 → K+V 双边（盲区①修复，必须先于 IEnumerable 判定——Dictionary&lt;K,V&gt; 同时实现
    /// IEnumerable&lt;KeyValuePair&lt;K,V&gt;&gt;，而 KeyValuePair 为值类型会被误判为叶子）、
    /// IEnumerable&lt;T&gt; → T。</para>
    /// <para>容器边不内联展开：字典 K/V 与集合元素经<see cref="AddEdgeTarget"/>
    /// 守卫（叶子跳过/不可解析置 unresolvable）后直接入 targets，交 <see cref="ClassifyCore"/>
    /// 主栈三色标记兜底环检测——自引用容器（如 class B : List&lt;B&gt;）原在此内联递归无底
    /// 致 StackOverflow。</para>
    /// <para>AOT/Trimming：方法体经反射遍历类型成员（含字典接口与集合元素类型探测），
    /// trim 下部分成员可能被裁剪；调用方（ClassifyCore）已带 RequiresUnreferencedCode 闭包，
    /// 本方法标注后不向外部传播警告。</para>
    /// </summary>
    /// <param name="type">属性类型</param>
    /// <param name="targets">收集到的引用类型边目标</param>
    /// <param name="unresolvable">置 true 表示遇到不可解析边（object/接口/泛型参数/非泛型可枚举等）</param>
    [RequiresUnreferencedCode("CollectEdgeTargets walks the reachable type graph via reflection (GetElementType/GetInterfaces). Some members may be trimmed.")]
    private static void CollectEdgeTargets(Type type, List<Type> targets, ref bool unresolvable)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsArray)
        {
            CollectEdgeTargets(type.GetElementType()!, targets, ref unresolvable);
            return;
        }

        if (IsLeaf(type)) return;

        if (type == typeof(object)) { unresolvable = true; return; }

        // 字典 → K 和 V 双边（对齐 CollectionCopier 深拷贝键值的事实）；
        // K/V 不在本方法内递归展开，经 AddEdgeTarget 守卫后直接入 targets 交主栈三色标记（
        // class JsonNode : Dictionary<string, JsonNode> 的 V 边自引用原致内联递归无底）
        if (TryGetDictionaryKeyValueType(type, out var keyType, out var valueType))
        {
            AddEdgeTarget(keyType, targets, ref unresolvable);
            AddEdgeTarget(valueType, targets, ref unresolvable);
            return;
        }

        // 泛型集合 → 元素类型（复用 NavigationResolver 口径，排除字典/string/数组已先行处理）；
        // 元素不在本方法内递归展开，经 AddEdgeTarget 守卫后直接入 targets 交主栈三色标记（
        // class B : List<B> 的元素边自引用原致内联递归无底）
        var elementType = NavigationResolver.GetCollectionElementType(type);
        if (elementType != null)
        {
            AddEdgeTarget(elementType, targets, ref unresolvable);
            return;
        }

        // 非泛型 IEnumerable（无 IEnumerable<T>）— 元素类型静态不可解析
        if (typeof(IEnumerable).IsAssignableFrom(type)) { unresolvable = true; return; }

        // 接口类型 — 静态不可解析运行时实现
        if (type.IsInterface) { unresolvable = true; return; }

        // 泛型参数 / 指针 / ByRef — 无法解析为命名类型
        if (type.IsGenericParameter || type.IsPointer || type.IsByRef) { unresolvable = true; return; }

        // 其余具体命名类型 → 递归边
        targets.Add(type);
    }

    /// <summary>
    /// 容器边目标入栈前守卫— 字典 K/V 与集合元素不内联递归展开：
    /// 叶子跳过（无边）、不可解析形态（object/接口/泛型参数/指针/ByRef）置 unresolvable，
    /// 其余（数组/容器/具体命名类型）直接入 <paramref name="targets"/> 交
    /// <see cref="ClassifyCore"/> 主栈三色标记兜底环检测
    /// <para>语义对齐生成侧 TypeRefDeepCopyClassifier.GetContainerTargets 的"容器边收集
    /// targets 交主栈"模式（双端一致， D4）；嵌套容器（如 Dictionary&lt;string, List&lt;T&gt;&gt;
    /// 的 V 边）由主栈按 IsCollectionLike 逐层解包，自引用容器（V==自身或元素==自身）由
    /// 主栈 onPath 回边判 <see cref="DeepCopyGraphShape.Cyclic"/>。</para>
    /// <para>不可解析形态不得静默入 targets：主栈对 object/空接口按属性遍历得空边集会误判
    /// <see cref="DeepCopyGraphShape.Acyclic"/>，漏 <see cref="DeepCopyGraphShape.Unknown"/>
    /// 保守分发。</para>
    /// </summary>
    /// <param name="type">容器边解包出的 K/V/元素类型</param>
    /// <param name="targets">收集到的引用类型边目标</param>
    /// <param name="unresolvable">置 true 表示遇到不可解析边</param>
    [RequiresUnreferencedCode("AddEdgeTarget probes type leaf-ness and array/interface forms via reflection. Some members may be trimmed.")]
    private static void AddEdgeTarget(Type type, List<Type> targets, ref bool unresolvable)
    {
        if (IsLeaf(type)) return;
        if (type == typeof(object) || type.IsInterface || type.IsGenericParameter || type.IsPointer || type.IsByRef)
        {
            unresolvable = true;
            return;
        }

        targets.Add(type);
    }

    /// <summary>
    /// 容器类型建边 — 数组 → 元素（递归有底：T[] 的元素恒 ≠ 数组自身）、字典 → K+V、
    /// IEnumerable&lt;T&gt; → T、非泛型可枚举 → 不可解析
    /// <para>K/V/元素经 <see cref="AddEdgeTarget"/> 守卫后直接入 targets 交
    /// <see cref="ClassifyCore"/>主栈三色标记兜底环检测，不内联递归展开。</para>
    /// <para>AOT/Trimming：方法体经反射遍历类型成员，trim 下部分成员可能被裁剪；
    /// 调用方（ClassifyCore）已带 RequiresUnreferencedCode 闭包，本方法标注后不向外部传播警告。</para>
    /// </summary>
    /// <param name="type">容器类型（已保证非叶子的引用类型）</param>
    /// <param name="targets">收集到的引用类型边目标</param>
    /// <param name="unresolvable">置 true 表示元素类型不可解析</param>
    [RequiresUnreferencedCode("CollectContainerEdges walks the reachable type graph via reflection. Some members may be trimmed.")]
    private static void CollectContainerEdges(Type type, List<Type> targets, ref bool unresolvable)
    {
        if (type.IsArray)
        {
            CollectEdgeTargets(type.GetElementType()!, targets, ref unresolvable);
            return;
        }
        // 字典 K/V 不内联递归展开，经 AddEdgeTarget 守卫后交主栈三色标记
        if (TryGetDictionaryKeyValueType(type, out var keyType, out var valueType))
        {
            AddEdgeTarget(keyType, targets, ref unresolvable);
            AddEdgeTarget(valueType, targets, ref unresolvable);
            return;
        }
        // 集合元素不内联递归展开，经 AddEdgeTarget 守卫后交主栈三色标记
        var elementType = NavigationResolver.GetCollectionElementType(type);
        if (elementType != null)
        {
            AddEdgeTarget(elementType, targets, ref unresolvable);
            return;
        }
        unresolvable = true;
    }

    /// <summary>
    /// 判断类型是否为叶子（深拷贝遍历不产生递归边）
    /// <para>值类型（填充器直接赋值，DeepCopyCompiler.cs:392-394）、string、Delegate 派生
    /// （无任何可写公开属性，填充器不产生递归），以及 X4/Q2 不透明叶子白名单
    /// （Type/MemberInfo/Stream/DBNull — 均无公开可写引用属性，填充器不会递归进入；
    /// 可靠性依据由 DeepCopyTypeClassifierTests 的叶子白名单守卫测试断言）。</para>
    /// </summary>
    /// <param name="type">类型</param>
    /// <returns>叶子类型返回 true</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("IsLeaf consults LeafStructGuard which scans struct fields via reflection. Some members may be trimmed.")]
    private static bool IsLeaf(Type type)
    {
        bool isAnyLeafBaseType =
            type == typeof(Type)
            || typeof(System.Reflection.MemberInfo).IsAssignableFrom(type)
            || typeof(System.IO.Stream).IsAssignableFrom(type)
            || type == typeof(DBNull);

        var leaf = DeepCopyGraphRules.IsLeafType(
            isValueType: type.IsValueType,
            isString: type == typeof(string),
            isDelegate: typeof(Delegate).IsAssignableFrom(type),
            isAnyLeafBaseType: isAnyLeafBaseType);

        // 运行时判定收紧——含可变引用实例字段的
        // struct 非叶（原值类型一律叶 → 浅赋值共享可变引用成员）。共享规则
        // DeepCopyGraphRules 与 SourceGen 物化期不动（生成码零变化，G3 维持 IDENTICAL；
        // 生成期残留由 B5-4/收尾批承接——运行时/生成期分叉豁免登记见 LeafStructGuard）。
        // 口径：readonly 引用字段与 string/Delegate/叶白名单不触发（LeafStructGuard 单点）。
        return leaf && (!type.IsValueType || type.IsEnum
            || NCMapper.Internal.LeafStructGuard.IsLeafSafe(type));
    }

    /// <summary>
    /// 判断类型是否为容器（数组或 IEnumerable 实现）— 分类时按元素/K-V 建边而非属性遍历
    /// </summary>
    private static bool IsCollectionLike(Type type)
        => DeepCopyGraphRules.IsCollectionLike(
            isArray: type.IsArray,
            implementsIEnumerable: typeof(IEnumerable).IsAssignableFrom(type),
            isString: type == typeof(string));

    /// <summary>
    /// 提取字典类型的 K/V 泛型参数（IDictionary&lt;K,V&gt;/IReadOnlyDictionary&lt;K,V&gt;/Dictionary&lt;K,V&gt; 自身或其实现接口）
    /// </summary>
    /// <param name="type">字典类型</param>
    /// <param name="keyType">输出：Key 类型</param>
    /// <param name="valueType">输出：Value 类型</param>
    /// <returns>字典类型返回 true</returns>
    private static bool TryGetDictionaryKeyValueType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type,
        out Type keyType, out Type valueType)
    {
        keyType = null!;
        valueType = null!;

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (DeepCopyGraphRules.IsDictionaryDefinition(def.Namespace ?? "", def.Name))
            {
                var args = type.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
                return true;
            }
        }

        foreach (var iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;
            var def = iface.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                var args = iface.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
                return true;
            }
        }

        return false;
    }
}
