using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NCMapper.Internal;
namespace NCMapper;
/// <summary>
/// 集合拷贝辅助类 — 提供 List/Dictionary/Queue/Stack/HashSet/Concurrent 等集合的浅拷贝与深拷贝方法，供表达式树 Lambda 调用
/// </summary>
internal class CollectionCopier
{
    // CacheRegistry 登记制自注册：仅登记清理委托，不触发缓存构建（防自触发）
    static CollectionCopier()
    {
        CacheRegistry.Register("collection-copier", CacheLayer.Full, ClearCache);
    }

    /// <summary>
    /// 判断泛型类型是否为值类型、枚举或字符串（即"简单类型"，可浅拷贝）
    /// <para>含可变引用实例字段的 struct 改判
    /// 非简单——元素经 <see cref="CopyElement{T}"/> → 对象通道（CopyStructBoxed）逐成员
    /// 深拷贝（本判定统一作用于全部集合元素/键值门：List/Array/Queue/Stack/HashSet/
    /// Concurrent 族/字典 K-V）；仅运行时侧改判，SourceGen 物化期不动（生成码零变化，
    /// G3 维持 IDENTICAL）。</para>
    /// </summary>
    [RequiresUnreferencedCode("IsValueType consults LeafStructGuard which scans struct fields via reflection. Some members may be trimmed.")]
    private static bool IsValueType<T>()
    {
        var type = typeof(T);
        if (!type.IsValueType) return type == typeof(string);
        return type.IsEnum || Internal.LeafStructGuard.IsLeafSafe(type);
    }
    /// <summary>
    /// 元素深拷贝 — null 保序 + 引用类型 DeepCopy；值类型直传。热路径内联。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Collection element copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Collection element copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static T CopyElement<T>(T item, DeepCopyContext ctx) => item == null ? default! : DeepCopy(item, ctx)!;

    /// <summary>
    /// IEnumerable&lt;T&gt; 构造器缓存解析 — 值/引用类型两分支共用一次缓存查询。
    /// DAM 标注：GetConstructor 需要 PublicConstructors；MakeGenericType 需动态代码（调用方已声明 Requires*）。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Generic collection construction uses MakeGenericType. Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Collection construction looks up constructors via reflection. Some types may be trimmed. Use the source-generated path to make this AOT-safe.")]
    private static ConstructorInfo? GetEnumerableCtor(
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] Type type,
        Type elemType)
        => s_enumerableCtorCache.GetOrAdd(type, t =>
        {
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(elemType);
            return t.GetConstructor(new[] { enumerableType });
        });

    /// <summary>
    /// 深拷贝对象 — 转发到 <see cref="DeepCopyProviderBase.Current"/>
    /// <para>默认使用 <see cref="ExpressionTreeDeepCopyProvider"/>（表达式树编译，高性能）</para>
    /// <para>用户可通过 <c>DeepCopyProviderBase.Current = DeepCopyProviderBase.JsonProvider</c> 切换回 JSON 方案</para>
    /// <para>值类型/枚举/string 直接返回原引用（值语义，无需深拷贝）</para>
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效，避免每个集合元素创建新 ctx
    /// 导致循环引用检测失效（A→List&lt;B&gt;→B.Parent→A 无限递归栈溢出）。</para>
    /// </summary>
    /// <param name="source">需要深拷贝的源对象</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx 保持循环引用检测有效）</param>
    /// <returns>深拷贝后的新对象；source 为 null 时返回 default</returns>
    [RequiresDynamicCode("DeepCopy builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("DeepCopy scans type members and attributes via reflection. Some members may be trimmed.")]
    public static T? DeepCopy<T>(T source, DeepCopyContext ctx)
    {
        if (source == null) return default;
        var type = typeof(T);
        // 值类型/枚举/string → 直接返回（值语义复制，无需深拷贝）
        // 含可变引用成员的 struct 经 IsValueType<T>改判非简单——装箱态落
        // DeepCopyObjectCore 对象通道（其值类型分支分发 CopyStructBoxed 逐成员深拷贝）
        if (IsValueType<T>())
            return source;
        // 引用类型 → 通过非泛型入口调用 DeepCopyProviderBase（内部用表达式树委托缓存分发到泛型方法）
        // 复用 P1-6 的委托缓存，跳过实例方法分发
        // 方向 I：复用 ctx 保持循环引用检测有效
        return (T?)DeepCopyProviderBase.DeepCopyObjectCore(source, ctx);
    }

    /// <summary>
    /// List对象浅复制
    /// </summary>
    /// <param name="sourceItems">源列表</param>
    /// <returns>浅拷贝后的新列表；sourceItems 为 null 时返回 null</returns>
    public static List<T>? CopyList<T>(List<T> sourceItems)
    {
        if (sourceItems == null) return null;
        var result = sourceItems.ToList();
        return result;
    }

    /// <summary>
    /// List对象深拷贝 — 值类型/string 元素直接拷贝，引用类型元素逐个 DeepCopy
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效</para>
    /// <para>元素拷贝委托循环外解析一次 + provider 循环外快照
    /// 同型元素（List&lt;T&gt; 非协变，元素运行时类型为 T 或其派生，常态同型）直调编译委托，
    /// 跳过 DeepCopyObjectCore 每元素 GetType+GetOrAdd+Lazy 字典分发链。语义等价：委托本体与
    /// DeepCopyObjectCore 内部缓存委托为同一表达式（provider.DeepCopy&lt;T&gt;((T)src, ctx)）；
    /// 派生实例回退 CopyElement 按运行时类型分发，多态语义保持。</para>
    /// </summary>
    /// <param name="sourceItems">源列表</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    /// <returns>深拷贝后的新列表；sourceItems 为 null 时返回 null</returns>
    [RequiresDynamicCode("DeepCopyList builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("DeepCopyList scans type members and attributes via reflection. Some members may be trimmed.")]
    public static List<T>? DeepCopyList<T>(List<T> sourceItems, DeepCopyContext ctx)
    {
        if (sourceItems == null) return null;
        if (IsValueType<T>())
            return sourceItems.ToList();
        var result = new List<T>(sourceItems.Count);
        // 循环外解析（元素拷贝委托 + provider 快照，单次调用策略一致）
        var elemType = typeof(T);
        // 值类型元素（含可变引用成员 struct，IsValueType<T>
        // 已分流至此）不经 elemCopier 编译内核（DeepCopy<T> class 约束）——走 CopyElement →
        // DeepCopyObjectCore 值类型分发 CopyStructBoxed（同语义对象通道）
        bool elemIsValueType = elemType.IsValueType;
        var elemCopier = elemIsValueType ? null : DeepCopyProviderBase.GetOrAddElementCopier(elemType);
        var provider = DeepCopyProviderBase.Current;
        foreach (var item in sourceItems)
        {
            if (item == null) { result.Add(default!); continue; }
            result.Add(elemIsValueType || item.GetType() != elemType
                ? CopyElement(item, ctx)
                : (T)elemCopier!(provider, item, ctx)!); // 派生实例：按运行时类型分发（多态语义保持）
        }
        return result;
    }

    /// <summary>
    /// Array对象浅复制（值类型/string元素无需深拷贝）
    /// </summary>
    /// <param name="sourceItems">源数组</param>
    /// <returns>浅拷贝后的新数组；sourceItems 为 null 时返回 default</returns>
    public static T? CopyArray<T>(T sourceItems)
    {
        if (sourceItems == null) return default(T);
        if (sourceItems is Array arr)
            return (T)(object)arr.Clone();
        return sourceItems;
    }

    /// <summary>
    /// Array对象深拷贝 — 值类型/string 元素直接拷贝，引用类型元素逐个 DeepCopy
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效</para>
    /// <para>一维 SZ 数组（运行时类型与元素类型精确匹配，
    /// 非协变/非二维/非非零基）走强类型内核——元素下标直访（免 GetValue/SetValue 反射）+
    /// 同型元素直调 provider 虚方法（跳过 DeepCopyObjectCore 字典分发链）；
    /// 协变数组（如 object[] 装派生元素）回退原反射路径保运行时类型分发语义。</para>
    /// </summary>
    /// <param name="sourceItems">源数组</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    /// <returns>深拷贝后的新数组；sourceItems 为 null 时返回 default</returns>
    [RequiresDynamicCode("DeepCopyArray builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("DeepCopyArray scans type members and attributes via reflection. Some members may be trimmed.")]
    public static T? DeepCopyArray<T>(T sourceItems, DeepCopyContext ctx)
    {
        if (sourceItems == null) return default;
        if (sourceItems is not Array arr)
            return sourceItems;

        var runtimeType = sourceItems.GetType();
        var elemType = runtimeType.GetElementType()!;
        // 叶安全值类型/string 方 Clone（值语义直拷）；
        // 含可变引用成员的 struct 改判走深路径（元素经对象通道逐成员深拷贝）
        if (elemType == typeof(string) ||
            (elemType.IsValueType && (elemType.IsEnum || Internal.LeafStructGuard.IsLeafSafe(elemType))))
            return (T)(object)arr.Clone();

        // 非协变一维 SZ 数组 → 强类型内核（元素下标直访 + 同型直调）
        // 内核 CopyArrayCore<TElem>受 class 约束——值类型元素（非叶 struct）走
        // 下方反射路径（GetValue/SetValue + DeepCopyObjectCore 值类型分发，语义一致）
        if (runtimeType == elemType.MakeArrayType() && !elemType.IsValueType)
        {
            var core = s_arrayCoreCache.GetOrAdd(elemType, static t =>
            {
                var method = typeof(CollectionCopier).GetMethod(nameof(CopyArrayCore),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .MakeGenericMethod(t);
                var arrParam = System.Linq.Expressions.Expression.Parameter(typeof(Array), "arr");
                var ctxParam = System.Linq.Expressions.Expression.Parameter(typeof(DeepCopyContext), "ctx");
                var call = System.Linq.Expressions.Expression.Call(method,
                    System.Linq.Expressions.Expression.Convert(arrParam, t.MakeArrayType()), ctxParam);
                return System.Linq.Expressions.Expression.Lambda<Func<Array, DeepCopyContext, Array>>(
                    System.Linq.Expressions.Expression.Convert(call, typeof(Array)), arrParam, ctxParam).Compile();
            });
            return (T)(object)core(arr, ctx);
        }

        // 协变/多维/非零基数组 → 反射路径（运行时元素类型分发，原语义）
        // 原实现 `Array.CreateInstance(elemType, arr.Length)` + 一维下标
        // GetValue(i)/SetValue(...,i) 对多维数组必然失败（ArgumentException），对非零基一维数组
        // 亦因下界不匹配越界——注释声称"反射路径"但实现一维化，深拷贝契约失效。按 GetArrayRank /
        // GetLowerBound 重建（BCL 唯一正道）；一维零基为该统一路径的自然特例，语义不变。
        var rank = runtimeType.GetArrayRank();
        var lengths = new int[rank];
        var lowerBounds = new int[rank];
        for (var d = 0; d < rank; d++)
        {
            lengths[d] = arr.GetLength(d);
            lowerBounds[d] = arr.GetLowerBound(d);
        }

        var result = Array.CreateInstance(elemType, lengths, lowerBounds);
        var indices = new int[rank];
        for (int i = 0; i < arr.Length; i++)
        {
            // 扁平位置 → 各维下标（按各维长度与下界换算）
            var remainder = i;
            for (var d = rank - 1; d >= 0; d--)
            {
                indices[d] = lowerBounds[d] + remainder % lengths[d];
                remainder /= lengths[d];
            }
            var item = arr.GetValue(indices);
            // 方向 I：复用 ctx 保持循环引用检测有效
            result.SetValue(item == null ? null : DeepCopyProviderBase.DeepCopyObjectCore(item, ctx), indices);
        }
        return (T)(object)result;
    }

    /// <summary>
    /// 数组强类型深拷贝内核
    /// <para>元素直接下标访问（免 GetValue/SetValue 反射）；同型元素直调 provider 虚方法
    /// （语义与 DeepCopyObjectCore 同型路径等价）；协变数组元素（运行时类型为 TElem 派生类）
    /// 回退 DeepCopyObjectCore 按运行时类型分发，多态语义保持。复用 ctx（循环引用检测有效）。</para>
    /// </summary>
    [RequiresDynamicCode("CopyArrayCore builds expressions via MakeGenericMethod. Not supported in AOT.")]
    [RequiresUnreferencedCode("CopyArrayCore scans type members via reflection. Some members may be trimmed.")]
    private static TElem[] CopyArrayCore<TElem>(TElem[] arr, DeepCopyContext ctx) where TElem : class
    {
        var provider = DeepCopyProviderBase.Current; // 单次数组拷贝策略一致
        var elemType = typeof(TElem);
        var result = new TElem[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            var item = arr[i];
            if (item == null) continue;
            result[i] = item.GetType() == elemType
                ? provider.DeepCopy(item, ctx)!
                : (TElem)DeepCopyProviderBase.DeepCopyObjectCore(item, ctx)!; // 派生元素：运行时类型分发
        }
        return result;
    }

    /// <summary>
    /// Dictionary对象复制 — 同类型字典的深拷贝
    /// 值类型元素直接复制，复杂类型元素逐元素 DeepCopy
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效</para>
    /// </summary>
    /// <param name="sourceItems">源字典</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    /// <returns>深拷贝后的新字典；sourceItems 为 null 时返回 null</returns>
    [RequiresDynamicCode("Collection copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("Collection copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static Dictionary<Tin, Tout>? CopyDictionary<Tin, Tout>(Dictionary<Tin, Tout> sourceItems, DeepCopyContext ctx) where Tin : notnull
    {
        if (sourceItems == null) return null;

        // 所有元素都是值类型或string → 直接复制
        if (IsValueType<Tin>() && IsValueType<Tout>())
            // 透传源 Comparer——原 ToDictionary 无参版
            // 丢 OrdinalIgnoreCase 等查语义（深拷贝后 copy["abc"] miss，变两键）
            return sourceItems.ToDictionary(x => x.Key, x => x.Value, sourceItems.Comparer);

        // 有复杂类型元素 → 逐元素深拷贝（K/V 两路各自
        // 循环外解析直调委托（DeepCopyList 同款），保留 IsValueType 门与 Comparer
        // 透传；非叶 struct 键/值（raw IsValueType=true）经 copier=null 走 DeepCopy →
        // CopyStructBoxed 对象通道——泛型虚通道 class 约束对值类型即抛（探针 P1 实测 b41a39b））
        var keyType = typeof(Tin);
        var valueType = typeof(Tout);
        bool keyIsRawValueType = keyType.IsValueType;
        bool valueIsRawValueType = valueType.IsValueType;
        var keyCopier = keyIsRawValueType ? null : DeepCopyProviderBase.GetOrAddElementCopier(keyType);
        var valueCopier = valueIsRawValueType ? null : DeepCopyProviderBase.GetOrAddElementCopier(valueType);
        var provider = DeepCopyProviderBase.Current;
        var result = new Dictionary<Tin, Tout>(sourceItems.Count, sourceItems.Comparer);
        foreach (var kv in sourceItems)
        {
            var key = IsValueType<Tin>() ? kv.Key
                : kv.Key == null ? default!                         // null 键：与 DeepCopy(null)→default 语义等价
                : keyIsRawValueType || kv.Key.GetType() != keyType ? CopyElement(kv.Key, ctx)
                : (Tin)keyCopier!(provider, kv.Key, ctx)!;
            var value = IsValueType<Tout>() ? kv.Value
                : valueIsRawValueType || kv.Value == null || kv.Value.GetType() != valueType ? CopyElement(kv.Value, ctx)
                : (Tout)valueCopier!(provider, kv.Value, ctx)!;
            result[key!] = value;
        }
        return result;
    }

    /// <summary>
    /// ConcurrentDictionary对象复制 — 同类型字典的深拷贝
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效</para>
    /// </summary>
    /// <param name="sourceItems">源并发字典</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    /// <returns>深拷贝后的新并发字典；sourceItems 为 null 时返回 null</returns>
    [RequiresDynamicCode("Collection copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("Collection copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static ConcurrentDictionary<Tin, Tout>? CopyConcurrentDictionary<Tin, Tout>(ConcurrentDictionary<Tin, Tout> sourceItems, DeepCopyContext ctx) where Tin : notnull
    {
        if (sourceItems == null) return null;
        // 透传源 Comparer——原 new() 默认比较器丢键查语义
        //（OrdinalIgnoreCase 源 copy["ABC"] 落空）；与 CopyDictionary 同款口径。
        // Comparer 为 .NET 6+ 公共属性（net8/9/10 覆盖， v1.2 核验）。
        var result = new ConcurrentDictionary<Tin, Tout>(
            Environment.ProcessorCount, sourceItems.Count, sourceItems.Comparer);

        var keyComplex = !IsValueType<Tin>();
        var valComplex = !IsValueType<Tout>();

        foreach (var item in sourceItems)
        {
            var key = keyComplex ? DeepCopy(item.Key, ctx)! : item.Key;
            var value = (valComplex && item.Value != null) ? DeepCopy(item.Value, ctx)! : item.Value;
            // TryAdd 碰撞显式策略——首值保留 + MapTracer 结构化留痕（原静默丢键零观测面，
            // A2-08 同款口径）。透传后正常语义键不可能碰撞（源字典已按同比较器去重），本分支
            // 仅深拷贝键等价类塌缩等病态场景可达（构造分析见 探索①）。
            if (!result.TryAdd(key, value))
                MapTracer.Write($"[NCMapper] CollectionCopier: ConcurrentDictionary 深拷贝键碰撞（保留首值，丢弃后续等价键）: {key}");
        }
        return result;
    }

    /// <summary>
    /// 将IDictionary转换为Dictionary — 跨类型字典映射
    /// 支持元素类型不同时的自动映射（通过 MapperBase.Map 递归）
    /// B4 修复：valueMapper 改为按运行时类型独立缓存，支持混合类型字典
    /// </summary>
    /// <param name="sourceItem">源字典（IDictionary 接口）</param>
    /// <returns>转换后的新字典</returns>
    [RequiresDynamicCode("Collection copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("Collection copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static Dictionary<Tkey, Tvalue> ConvertDictionary<Tkey, Tvalue>(IDictionary sourceItem) where Tkey : notnull
    {
        var result = new Dictionary<Tkey, Tvalue>(sourceItem?.Count ?? 0);
        if (sourceItem == null) return result;

        foreach (DictionaryEntry entry in sourceItem)
        {
            var (key, value) = ConvertDictEntry<Tkey, Tvalue>(entry);
            result[key] = value;
        }
        return result;
    }

    /// <summary>
    /// 将IDictionary转换为ConcurrentDictionary — 跨类型字典映射
    /// B4 修复：valueMapper 改为按运行时类型独立缓存，支持混合类型字典
    /// </summary>
    /// <param name="sourceItem">源字典（IDictionary 接口）</param>
    /// <returns>转换后的新并发字典</returns>
    [RequiresDynamicCode("Collection copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("Collection copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static ConcurrentDictionary<Tkey, Tvalue> ConvertConcurrentDictionaryFrom<Tkey, Tvalue>(IDictionary sourceItem) where Tkey : notnull
    {
        var result = new ConcurrentDictionary<Tkey, Tvalue>();
        if (sourceItem == null) return result;

        foreach (DictionaryEntry entry in sourceItem)
        {
            var (key, value) = ConvertDictEntry<Tkey, Tvalue>(entry);
            result.TryAdd(key, value);
        }
        return result;
    }

    /// <summary>
    /// 字典条目转换 — ConvertDictionary 和 ConvertConcurrentDictionaryFrom 的共用逻辑
    /// </summary>
    /// <typeparam name="Tkey">目标键类型</typeparam>
    /// <typeparam name="Tvalue">目标值类型</typeparam>
    /// <param name="entry">源字典条目</param>
    /// <returns>转换后的键值对</returns>
    [RequiresDynamicCode("ConvertDictEntry builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("ConvertDictEntry scans type members and attributes via reflection. Some members may be trimmed.")]
    private static (Tkey key, Tvalue value) ConvertDictEntry<Tkey, Tvalue>(DictionaryEntry entry) where Tkey : notnull
    {
        var targetValType = typeof(Tvalue);
        var needValueMapping = !targetValType.IsValueType && !targetValType.IsEnum && targetValType != typeof(string);

        var key = TypeConvertHelper.ConvertValue<object, Tkey>(entry.Key)!;

        Tvalue value;
        if (entry.Value is Tvalue tv)
        {
            value = tv;
        }
        else if (needValueMapping && entry.Value != null)
        {
            var srcValType = entry.Value.GetType();
            var valueMapper = GetDictValueMapper(srcValType, targetValType);
            value = (Tvalue)valueMapper(entry.Value)!;
        }
        else
        {
            value = TypeConvertHelper.ConvertValue<object, Tvalue>(entry.Value ?? default!)!;
        }

        return (key, value);
    }

    /// <summary>
    /// ConcurrentQueue对象深拷贝 — 值类型元素直接拷贝，引用类型元素逐个 DeepCopy
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效</para>
    /// </summary>
    /// <param name="sourceItems">源并发队列</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    /// <returns>深拷贝后的新并发队列；sourceItems 为 null 时返回 null</returns>
    [RequiresDynamicCode("Collection copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("Collection copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static ConcurrentQueue<Tin>? CopyConcurrentQueue<Tin>(ConcurrentQueue<Tin> sourceItems, DeepCopyContext ctx)
    {
        if (sourceItems == null) return null;
        var result = new ConcurrentQueue<Tin>();
        if (IsValueType<Tin>())
        {
            foreach (var item in sourceItems)
                result.Enqueue(item);
        }
        else
        {
            // 同型直调 + 派生回退（注释见 CopyHashSet）
            var elemType = typeof(Tin);
            bool elemIsRawValueType = elemType.IsValueType;
            var elemCopier = elemIsRawValueType ? null : DeepCopyProviderBase.GetOrAddElementCopier(elemType);
            var provider = DeepCopyProviderBase.Current;
            foreach (var item in sourceItems)
            {
                if (item == null) { result.Enqueue(default!); continue; }
                result.Enqueue(elemIsRawValueType || item.GetType() != elemType
                    ? CopyElement(item, ctx)
                    : (Tin)elemCopier!(provider, item, ctx)!);
            }
        }
        return result;
    }

    /// <summary>
    /// HashSet对象深拷贝 — 值类型元素直接拷贝，引用类型元素逐个 DeepCopy
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效</para>
    /// </summary>
    /// <param name="sourceItems">源集合</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    /// <returns>深拷贝后的新集合；sourceItems 为 null 时返回 null</returns>
    [RequiresDynamicCode("Collection copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("Collection copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static HashSet<T>? CopyHashSet<T>(HashSet<T> sourceItems, DeepCopyContext ctx)
    {
        if (sourceItems == null) return null;
        if (IsValueType<T>())
            // 透传源 Comparer（保查语义）
            return new HashSet<T>(sourceItems, sourceItems.Comparer);
        // 引用分支直调内核（DeepCopyList /
        // 同款）——同型引用元素直调编译委托（跳过 DeepCopyObjectCore 每元素 GetType+GetOrAdd+
        // Lazy 分发链），派生实例回退 CopyElement 运行时分发（多态语义保持）；值元素半边：
        // 叶安全 struct 已由 IsValueType<T>() 短路，非叶 struct（raw IsValueType=true）经
        // elemCopier=null 全走 CopyElement → CopyStructBoxed（泛型虚通道 class 约束对值类型
        // 即抛——探针 P1 实测 b41a39b，分流必要性成立）。
        var elemType = typeof(T);
        bool elemIsRawValueType = elemType.IsValueType;
        var elemCopier = elemIsRawValueType ? null : DeepCopyProviderBase.GetOrAddElementCopier(elemType);
        var provider = DeepCopyProviderBase.Current;
        var result = new HashSet<T>(sourceItems.Count, sourceItems.Comparer);
        foreach (var item in sourceItems)
        {
            if (item == null) { result.Add(default!); continue; }
            result.Add(elemIsRawValueType || item.GetType() != elemType
                ? CopyElement(item, ctx)
                : (T)elemCopier!(provider, item, ctx)!);
        }
        return result;
    }

    /// <summary>
    /// Queue对象深拷贝 — 值类型元素直接拷贝，引用类型元素逐个 DeepCopy，保持 FIFO 顺序
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效</para>
    /// </summary>
    /// <param name="sourceItems">源队列</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    /// <returns>深拷贝后的新队列；sourceItems 为 null 时返回 null</returns>
    [RequiresDynamicCode("Collection copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("Collection copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static Queue<T>? CopyQueue<T>(Queue<T> sourceItems, DeepCopyContext ctx)
    {
        if (sourceItems == null) return null;
        if (IsValueType<T>())
            return new Queue<T>(sourceItems);
        // 引用分支直调内核（DeepCopyList 同款，注释见 CopyHashSet）
        var elemType = typeof(T);
        bool elemIsRawValueType = elemType.IsValueType;
        var elemCopier = elemIsRawValueType ? null : DeepCopyProviderBase.GetOrAddElementCopier(elemType);
        var provider = DeepCopyProviderBase.Current;
        var result = new Queue<T>(sourceItems.Count);
        foreach (var item in sourceItems)
        {
            if (item == null) { result.Enqueue(default!); continue; }
            result.Enqueue(elemIsRawValueType || item.GetType() != elemType
                ? CopyElement(item, ctx)
                : (T)elemCopier!(provider, item, ctx)!);
        }
        return result;
    }

    /// <summary>
    /// Stack对象深拷贝 — 值类型元素直接拷贝，引用类型元素逐个 DeepCopy，保持 LIFO 顺序一致
    /// 关键：Stack 枚举顺序是栈顶到栈底，直接 new Stack(source) 会反转顺序，
    /// 需先 CopyTo（栈顶到栈底）再 Reverse（变为栈底到栈顶）后构造，使 Pop 顺序与原栈一致。
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效</para>
    /// </summary>
    /// <param name="sourceItems">源栈</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    /// <returns>深拷贝后的新栈；sourceItems 为 null 时返回 null</returns>
    [RequiresDynamicCode("Collection copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("Collection copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static Stack<T>? CopyStack<T>(Stack<T> sourceItems, DeepCopyContext ctx)
    {
        if (sourceItems == null) return null;
        var arr = new T[sourceItems.Count];
        sourceItems.CopyTo(arr, 0);   // 栈顶到栈底
        Array.Reverse(arr);            // 反转为栈底到栈顶
        if (IsValueType<T>())
            return new Stack<T>(arr);  // 值类型直接构造
        // 引用类型逐个 DeepCopy 后再 Push（同型直调 + 派生回退，注释见 CopyHashSet）
        var elemType = typeof(T);
        bool elemIsRawValueType = elemType.IsValueType;
        var elemCopier = elemIsRawValueType ? null : DeepCopyProviderBase.GetOrAddElementCopier(elemType);
        var provider = DeepCopyProviderBase.Current;
        var result = new Stack<T>(arr.Length);
        foreach (var item in arr)
        {
            if (item == null) { result.Push(default!); continue; }
            result.Push(elemIsRawValueType || item.GetType() != elemType
                ? CopyElement(item, ctx)
                : (T)elemCopier!(provider, item, ctx)!);
        }
        return result;
    }

    /// <summary>
    /// ConcurrentStack对象深拷贝 — 值类型元素直接拷贝，引用类型元素逐个 DeepCopy，顺序特殊处理
    /// 与 Stack 同理：枚举顺序是栈顶到栈底，直接构造会反转，
    /// 需 CopyTo（栈顶到栈底）→ Reverse（栈底到栈顶）→ 逐个 Push，使 Pop 顺序与原栈一致
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效</para>
    /// </summary>
    /// <param name="sourceItems">源并发栈</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    /// <returns>深拷贝后的新并发栈；sourceItems 为 null 时返回 null</returns>
    [RequiresDynamicCode("Collection copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("Collection copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static ConcurrentStack<T>? CopyConcurrentStack<T>(ConcurrentStack<T> sourceItems, DeepCopyContext ctx)
    {
        if (sourceItems == null) return null;
        var arr = new T[sourceItems.Count];
        sourceItems.CopyTo(arr, 0);   // 栈顶到栈底
        Array.Reverse(arr);            // 反转为栈底到栈顶
        var result = new ConcurrentStack<T>();
        if (IsValueType<T>())
        {
            foreach (var item in arr)       // 从栈底到栈顶逐个 Push → 栈顶恢复原序
                result.Push(item);
        }
        else
        {
            // 同型直调 + 派生回退（注释见 CopyHashSet）
            var elemType = typeof(T);
            bool elemIsRawValueType = elemType.IsValueType;
            var elemCopier = elemIsRawValueType ? null : DeepCopyProviderBase.GetOrAddElementCopier(elemType);
            var provider = DeepCopyProviderBase.Current;
            foreach (var item in arr)
            {
                if (item == null) { result.Push(default!); continue; }
                result.Push(elemIsRawValueType || item.GetType() != elemType
                    ? CopyElement(item, ctx)
                    : (T)elemCopier!(provider, item, ctx)!);
            }
        }
        return result;
    }

    /// <summary>
    /// ReadOnlyCollection对象深拷贝 — 值类型元素直接拷贝，引用类型元素逐个 DeepCopy
    /// ReadOnlyCollection 只有 IList&lt;T&gt; 构造器，无 IEnumerable&lt;T&gt; 构造器
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效</para>
    /// </summary>
    /// <param name="sourceItems">源只读集合</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    /// <returns>深拷贝后的新只读集合；sourceItems 为 null 时返回 null</returns>
    [RequiresDynamicCode("Collection copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("Collection copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static ReadOnlyCollection<T>? CopyReadOnlyCollection<T>(ReadOnlyCollection<T> sourceItems, DeepCopyContext ctx)
    {
        if (sourceItems == null) return null;
        if (IsValueType<T>())
            return new ReadOnlyCollection<T>(new List<T>(sourceItems));
        // 同型直调 + 派生回退（注释见 CopyHashSet）
        var elemType = typeof(T);
        bool elemIsRawValueType = elemType.IsValueType;
        var elemCopier = elemIsRawValueType ? null : DeepCopyProviderBase.GetOrAddElementCopier(elemType);
        var provider = DeepCopyProviderBase.Current;
        var list = new List<T>(sourceItems.Count);
        foreach (var item in sourceItems)
        {
            if (item == null) { list.Add(default!); continue; }
            list.Add(elemIsRawValueType || item.GetType() != elemType
                ? CopyElement(item, ctx)
                : (T)elemCopier!(provider, item, ctx)!);
        }
        return new ReadOnlyCollection<T>(list);
    }

    // ================================================================
    // 通用构造器拷贝 — 归纳 C# 已知集合类型，消除 JSON 深拷贝兜底
    // ================================================================

    /// <summary>
    /// IEnumerable&lt;T&gt; 构造器缓存 — 避免每次反射查找
    /// </summary>
    private static readonly ConcurrentDictionary<Type, ConstructorInfo?> s_enumerableCtorCache = new();
    /// <summary>
    /// IDictionary&lt;K,V&gt; 构造器缓存 — 避免每次反射查找
    /// </summary>
    private static readonly ConcurrentDictionary<Type, ConstructorInfo?> s_dictCtorCache = new();
    /// <summary>
    /// 字典 value 映射委托缓存 — key: (源值类型, 目标值类型), value: 编译后的 Func&lt;object, object?&gt;
    /// <para>ConvertDictionary/ConvertConcurrentDictionaryFrom 共用，避免每次调用重新编译 Lambda</para>
    /// <para>键 = 运行时值类型对
    /// （B4 混合类型契约不变，X5 不改声明类型键），动态类型生成场景下键随运行时类型无限
    /// 增长——达 <see cref="DictValueMapperCacheCapacity"/> 软上限后新键不再入缓存、当场
    /// 编译即时使用（仅首次多付编译成本，既有条目稳态命中零变化；并发下 Count 检查与
    /// GetOrAdd 非原子可略超上限，属防御性软界非精确契约）。唯一回收点 ClearCache 不变。</para>
    /// </summary>
    private static readonly ConcurrentDictionary<(Type srcValType, Type tgtValType), Func<object, object?>> s_dictValueMapperCache = new();

    /// <summary>A3-18 缓存软上限（Q-3 取值：混合键业务常态远低于此；512 × 编译委托 ≈ 数十 KB 内存上界）。</summary>
    private const int DictValueMapperCacheCapacity = 512;

    /// <summary>
    /// 数组强类型深拷贝内核缓存
    /// <para>key: 元素类型, value: (arr, ctx) => 强类型拷贝后的新数组（编译委托，消除逐元素 GetValue/SetValue 反射）</para>
    /// <para>CacheRegistry 登记制（"collection-copier"，ClearCache 覆盖）</para>
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<Array, DeepCopyContext, Array>> s_arrayCoreCache = new();

    /// <summary>
    /// IEnumerable&lt;T&gt; 构造器编译委托缓存
    /// <para>key: 集合类型, value: src => new 集合((IEnumerable&lt;元素&gt;)src)——替代 ctor.Invoke 反射（~150ns → ~5ns）</para>
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<object, object>?> s_enumerableCtorDelegateCache = new();

    /// <summary>
    /// IDictionary&lt;K,V&gt; 构造器编译委托缓存
    /// <para>key: 字典类型, value: src => new 字典((IDictionary&lt;K,V&gt;)src)——替代 ctor.Invoke 反射</para>
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<object, object>?> s_dictCtorDelegateCache = new();

    /// <summary>
    /// 无参构造器编译工厂缓存
    /// <para>key: 类型, value: () => new 类型()——替代 Activator.CreateInstance（List 容器/临时字典等）</para>
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<object>> s_objectFactoryCache = new();

    /// <summary>
    /// 获取 IEnumerable&lt;T&gt; 构造器编译委托：GetOrAdd 缓存 + Expression.New 编译（对照 GetDictValueMapper 先例）
    /// <para>无构造器类型缓存 null（负缓存缺失，与 s_enumerableCtorCache 既有模式一致）</para>
    /// </summary>
    [RequiresDynamicCode("GetEnumerableCtorDelegate uses MakeGenericType and Expression.Compile. Not supported in AOT.")]
    [RequiresUnreferencedCode("GetEnumerableCtorDelegate looks up constructors via reflection. Some members may be trimmed.")]
    private static Func<object, object>? GetEnumerableCtorDelegate(Type type, Type elemType)
        => s_enumerableCtorDelegateCache.GetOrAdd(type, t =>
        {
            var ctor = GetEnumerableCtor(t, elemType);
            if (ctor == null) return null;
            var srcParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "src");
            var castSrc = System.Linq.Expressions.Expression.Convert(srcParam,
                typeof(IEnumerable<>).MakeGenericType(elemType));
            var body = System.Linq.Expressions.Expression.New(ctor, castSrc);
            return System.Linq.Expressions.Expression.Lambda<Func<object, object>>(body, srcParam).Compile();
        });

    /// <summary>
    /// 获取 IDictionary&lt;K,V&gt; 构造器编译委托：GetOrAdd 缓存 + Expression.New 编译
    /// </summary>
    [RequiresDynamicCode("GetDictCtorDelegate uses MakeGenericType and Expression.Compile. Not supported in AOT.")]
    [RequiresUnreferencedCode("GetDictCtorDelegate looks up constructors via reflection. Some members may be trimmed.")]
    private static Func<object, object>? GetDictCtorDelegate(Type type, Type keyType, Type valType)
        => s_dictCtorDelegateCache.GetOrAdd(type, t =>
        {
            var dictIfaceType = typeof(IDictionary<,>).MakeGenericType(keyType, valType);
            var ctor = s_dictCtorCache.GetOrAdd(t, tt => tt.GetConstructor(new[] { dictIfaceType }));
            if (ctor == null) return null;
            var srcParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "src");
            var castSrc = System.Linq.Expressions.Expression.Convert(srcParam, dictIfaceType);
            var body = System.Linq.Expressions.Expression.New(ctor, castSrc);
            return System.Linq.Expressions.Expression.Lambda<Func<object, object>>(body, srcParam).Compile();
        });

    /// <summary>
    /// 获取无参构造器编译工厂：替代 Activator.CreateInstance（集合容器构建）
    /// </summary>
    [RequiresDynamicCode("GetParameterlessFactory uses Expression.Compile. Not supported in AOT.")]
    [RequiresUnreferencedCode("GetParameterlessFactory looks up constructors via reflection. Some members may be trimmed.")]
    private static Func<object> GetParameterlessFactory(Type type)
        => s_objectFactoryCache.GetOrAdd(type, t =>
            System.Linq.Expressions.Expression.Lambda<Func<object>>(
                System.Linq.Expressions.Expression.New(t)).Compile());

    /// <summary>
    /// 获取或创建字典 value 映射委托 — 编译 Func&lt;object, object?&gt; 替代 MethodInfo.Invoke 反射调用
    /// </summary>
    /// <param name="srcValType">源值运行时类型</param>
    /// <param name="tgtValType">目标值类型</param>
    /// <returns>编译后的映射委托</returns>
    [RequiresDynamicCode("GetDictValueMapper uses MakeGenericType, MakeGenericMethod and Expression.Compile to build the value mapper. Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("GetDictValueMapper resolves members via reflection. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    private static Func<object, object?> GetDictValueMapper(Type srcValType, Type tgtValType)
    {
        // A3-18 软上限门——快路径 = TryGetValue 单次无锁查找（与现状 GetOrAdd 同成本；
        // 首轮实测把 Count 门放快路径使 ConcurrentDictionary.Count 的锁数组遍历每元素
        // 执行一次，DictValueMapperMixed 56→229µs 恶化 4 倍——Count 仅允许出现在 miss 低频路径）
        var key = (srcValType, tgtValType);
        if (s_dictValueMapperCache.TryGetValue(key, out var cached)) return cached;
        // miss（新键，低频）→ 软上限判定：超限当场编译不缓存；未超限入缓存
        if (s_dictValueMapperCache.Count >= DictValueMapperCacheCapacity)
        {
            return CompileDictValueMapper(srcValType, tgtValType);
        }
        return s_dictValueMapperCache.GetOrAdd(key, k => CompileDictValueMapper(k.srcValType, k.tgtValType));
    }

    /// <summary>
    /// 编译字典 value 映射委托（A3-18 从 GetOrAdd lambda 提取：上限外路径与缓存路径共用）
    /// </summary>
    [RequiresDynamicCode("CompileDictValueMapper uses MakeGenericType, MakeGenericMethod and Expression.Compile to build the value mapper. Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("CompileDictValueMapper resolves members via reflection. Some members may be trimmed.")]
    private static Func<object, object?> CompileDictValueMapper(Type srcValType, Type tgtValType)
    {
        var mapperType = typeof(MapperBase<,>).MakeGenericType(srcValType, tgtValType);
        var mapMethod = ReflectionHelper.GetRequiredMethod(mapperType, "Map",
            new[] { srcValType, typeof(Action<,>).MakeGenericType(srcValType, tgtValType) });
        // 构造 Lambda: (object src) => MapperBase<srcType, targetValType>.Map((srcType)src, null)
        var param = System.Linq.Expressions.Expression.Parameter(typeof(object), "src");
        var castParam = System.Linq.Expressions.Expression.Convert(param, srcValType);
        var call = System.Linq.Expressions.Expression.Call(mapMethod,
            castParam,
            System.Linq.Expressions.Expression.Constant(null, typeof(Action<,>).MakeGenericType(srcValType, tgtValType)));
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<object, object?>>(call, param);
        return lambda.Compile();
    }

    /// <summary>
    /// 通用单值集合深拷贝 — 值类型元素通过构造器拷贝（等价深拷贝），引用类型元素逐个 DeepCopy 后构造
    /// 覆盖：SortedSet/LinkedList/ObservableCollection/ConcurrentBag 等有 IEnumerable&lt;T&gt; 构造器的集合
    /// 无合适构造器时回退 JSON
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效</para>
    /// </summary>
    /// <param name="source">源集合对象</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    /// <returns>深拷贝后的新集合；source 为 null 时返回 null</returns>
    /// <exception cref="MapException">集合元素深拷贝失败或递归深度超限时抛出</exception>
    [RequiresDynamicCode("Collection copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("Collection copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static T? CopyEnumerable<T>(T source, DeepCopyContext ctx) where T : class
    {
        if (source == null) return null;
        var type = typeof(T);
        var args = type.GetGenericArguments();

        if (args.Length != 1)
        {
            // 非单值泛型 → 无法通过构造器创建同类型实例，回退 JSON 深拷贝
            var jsonResult = JsonSerializerBase.Current.Serialize(source);
            return (T?)JsonSerializerBase.Current.Deserialize(jsonResult, type);
        }

        var elemType2 = args[0];
        // 构造器编译委托（缓存），替代 ctor.Invoke 反射
        // 值类型/string/枚举 → 构造器拷贝等价深拷贝
        var ctorDelegate = GetEnumerableCtorDelegate(type, elemType2);
        if (elemType2.IsValueType || elemType2.IsEnum || elemType2 == typeof(string))
        {
            if (ctorDelegate != null)
                return (T)ctorDelegate(source);
            //（IReadOnlyList<int> 等）本可经 List 收窄兜底返回；抛出先于收窄时
            // 该形态不可达。双条件与 CopyDictionary 同款口径：
            // IsInterface 判定 + IsAssignableFrom 腿（防 List<elemType2> 未实现 T 的形态被误收窄
            // 而引入 InvalidCastException）。元素为值类型/枚举/string，逐个 Add 即值复制，
            // 与构造器拷贝等价（深拷贝语义保持）；不可收窄时仍按下行抛出（JSON 回退语义不变）。
            if (typeof(T).IsInterface)
            {
                var narrowList = (System.Collections.IList)GetParameterlessFactory(
                    typeof(List<>).MakeGenericType(elemType2))();
                foreach (var item in (System.Collections.IEnumerable)source)
                    narrowList.Add(item);
                if (typeof(T).IsAssignableFrom(narrowList.GetType()))
                    return (T)narrowList;
            }
            // 无法创建同类型独立副本，抛异常而非返回浅拷贝
            throw new MapException(
                ErrorMessages.CopyEnumerableNoIEnumerableCtor(type.FullName!),
                typeof(T), typeof(T));
        }

        // 引用类型元素 → 先逐个 DeepCopy 到 List，再用 List 构造目标集合
        // （IEnumerable<T> 协变：元素可为声明元素类型的派生实例，逐元素 DeepCopyObjectCore
        //  按运行时类型分发的语义必须保持，不做同型直调优化）
        var listType = typeof(List<>).MakeGenericType(elemType2);
        var list2 = (System.Collections.IList)GetParameterlessFactory(listType)(); // 替代 Activator
        foreach (var item in (System.Collections.IEnumerable)source)
        {
            // 方向 I：复用 ctx 保持循环引用检测有效
            list2.Add(item == null ? null : DeepCopyProviderBase.DeepCopyObjectCore(item, ctx));
        }

        if (ctorDelegate != null)
            return (T)ctorDelegate(list2);

        // 接口集合（IList<T>/IEnumerable<T>/ICollection<T> 等
        // 无构造器形态）直接构造 List<T> 返回——元素已在上方逐个 DeepCopy 并接 ctx（DAG 引用
        // 一致性保持：同一元素实例多引用得到同一拷贝实例）；原 JSON 回退三重退化：
        // ① STJ 仅绑定公共 setter，internal setter 属性丢失；② 序列化不接 CycleCache（DAG 拆散）；
        // ③ 实测 6.1× 性能退化。List<T> 构造为深拷贝通行语义（结果类型收窄）。
        if (typeof(T).IsInterface && typeof(T).IsAssignableFrom(list2.GetType()))
            return (T)list2;

        // 非接口且无构造器 → 回退 JSON 深拷贝（保证类型正确，避免 InvalidCastException）
        var json = JsonSerializerBase.Current.Serialize(source);
        return (T?)JsonSerializerBase.Current.Deserialize(json, type);
    }

    /// <summary>
    /// 通用双值集合深拷贝 — 值类型元素通过构造器拷贝（等价深拷贝），引用类型元素逐个 DeepCopy 后构造
    /// 覆盖：SortedDictionary/SortedList/ReadOnlyDictionary 等有 IDictionary&lt;K,V&gt; 构造器的字典
    /// 无合适构造器时回退浅拷贝
    /// <para>方向 I 优化：复用 ctx 保持循环引用检测有效</para>
    /// </summary>
    /// <param name="source">源字典对象</param>
    /// <param name="ctx">深拷贝上下文（复用顶层 ctx）</param>
    /// <returns>深拷贝后的新字典；source 为 null 时返回 null</returns>
    /// <exception cref="MapException">非双泛型参数字典无法拷贝时抛出</exception>
    [RequiresDynamicCode("Collection copy falls back to the runtime deep copy path (Expression.Compile / MakeGenericMethod). Not supported in AOT. Use the source-generated path to make this AOT-safe.")]
    [RequiresUnreferencedCode("Collection copy scans element type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    public static T? CopyDictionaryByKeyValue<T>(T source, DeepCopyContext ctx) where T : class
    {
        if (source == null) return null;
        var type = typeof(T);
        var args = type.GetGenericArguments();

        // 非双泛型参数字典无法拷贝，抛异常而非返回浅拷贝
        if (args.Length != 2)
            throw new MapException(
                ErrorMessages.CopyDictionaryNotGeneric(type.FullName!),
                typeof(T), typeof(T));

        var keyType = args[0];
        var valType = args[1];
        var keySimple = keyType.IsValueType || keyType.IsEnum || keyType == typeof(string);
        var valSimple = valType.IsValueType || valType.IsEnum || valType == typeof(string);

        // 构造器编译委托（缓存），替代 ctor.Invoke 反射
        // Key 和 Value 都是简单类型 → 构造器拷贝等价深拷贝
        if (keySimple && valSimple)
        {
            var ctorDelegate = GetDictCtorDelegate(type, keyType, valType);
            if (ctorDelegate != null)
                return (T)ctorDelegate(source);
            // 无构造器 → 回退 JSON 深拷贝（保证类型正确，避免 InvalidCastException）
            var jsonFallback = JsonSerializerBase.Current.Serialize(source);
            return (T?)JsonSerializerBase.Current.Deserialize(jsonFallback, type);
        }

        // 有复杂类型 → 逐个 DeepCopy key/value 后构造
        var tempDictType = typeof(Dictionary<,>).MakeGenericType(keyType, valType);
        var tempDict = (System.Collections.IDictionary)GetParameterlessFactory(tempDictType)(); // 替代 Activator
        foreach (System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)source)
        {
            // 方向 I：复用 ctx 保持循环引用检测有效
            var key = keySimple ? entry.Key : (entry.Key == null ? null : DeepCopyProviderBase.DeepCopyObjectCore(entry.Key, ctx));
            var val = valSimple ? entry.Value : (entry.Value == null ? null : DeepCopyProviderBase.DeepCopyObjectCore(entry.Value, ctx));
            tempDict[key!] = val;
        }

        var ctorDelegateRef = GetDictCtorDelegate(type, keyType, valType);
        if (ctorDelegateRef != null)
            return (T)ctorDelegateRef(tempDict);

        // 无构造器 → 回退 JSON 深拷贝（保证类型正确，避免 InvalidCastException）
        var jsonResult = JsonSerializerBase.Current.Serialize(source);
        return (T?)JsonSerializerBase.Current.Deserialize(jsonResult, type);
    }

    /// <summary>
    /// 清除所有构造器和映射委托缓存 — 供 Mapper.ClearCache() 调用
    /// <para>s_arrayCoreCache/s_enumerableCtorDelegateCache/
    /// s_dictCtorDelegateCache/s_objectFactoryCache 同步清除（CacheRegistry "collection-copier" 登记语义覆盖）</para>
    /// </summary>
    internal static void ClearCache()
    {
        s_enumerableCtorCache.Clear();
        s_dictCtorCache.Clear();
        s_dictValueMapperCache.Clear();
        s_arrayCoreCache.Clear();
        s_enumerableCtorDelegateCache.Clear();
        s_dictCtorDelegateCache.Clear();
        s_objectFactoryCache.Clear();
    }
}
