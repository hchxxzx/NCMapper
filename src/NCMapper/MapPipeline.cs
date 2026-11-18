#pragma warning disable NCMapper001 // 内部使用 [Experimental] 的 Pipeline API
using System.Collections.Concurrent;

namespace NCMapper;

/// <summary>
/// 映射管道 — 可插拔的后处理（PostMap）回调
/// <para>管道为空时零开销（volatile bool 检查）</para>
/// <para>执行顺序：映射核心 → Action 回调 → PostMap（按注册逆序）</para>
/// <para>线程安全：管道注册使用 ConcurrentDictionary，多线程下安全</para>
/// </summary>
[System.Diagnostics.CodeAnalysis.Experimental("NCMapper001")]
public sealed class MapPipeline
{
    /// <summary>
    /// PostMap 回调字典 — 按 (SourceType, TargetType) 分组，每组按注册逆序执行
    /// </summary>
    private readonly ConcurrentDictionary<(Type Source, Type Target), ConcurrentQueue<Action<object, object>>> _postMapCallbacks = new();

    /// <summary>
    /// PostMap 逆序缓存 — 首次执行时构建逆序数组并缓存，消除每次 ToArray() 分配
    /// <para>缓存一致性由 _sync 保证：AddPostMap/Clear 在锁内移除/清空缓存，执行路径按需重建</para>
    /// </summary>
    private readonly ConcurrentDictionary<(Type Source, Type Target), Action<object, object>[]> _postMapReversedCache = new();

    /// <summary>
    /// PostMap 负查找缓存 — 记录"确定无回调"的类型对
    /// <para>现状：管道启用后，每次 Map 无回调类型对都要走 TryGetReversed 的 lock + 双字典
    /// 负查找（负结果不缓存，~150ns/次）。负缓存命中即免锁免查直接返回。</para>
    /// <para>X7 失效协议：负缓存失效随 AddPostMap/Clear 的 _sync 锁内清除点覆盖（与
    /// _postMapReversedCache 同点），Version 递增（AddPostMap/Clear 各自锁内）同步覆盖——
    /// ProjectionStrategy 降级缓存键语义不变。</para>
    /// </summary>
    private readonly ConcurrentDictionary<(Type Source, Type Target), byte> _postMapNegativeCache = new();

    /// <summary>
    /// 配置操作同步锁 — 序列化 AddPostMap/Clear 的写序列，消除注册与清理交错竞态
    /// <para>AddPostMap/Clear 均为低频配置路径，锁开销可忽略；ExecutePostMap 执行路径不加锁</para>
    /// </summary>
    private readonly object _sync = new();
    /// <summary>
    /// 获取 PostMap 逆序缓存— 缓存未命中时构建逆序数组并缓存；无回调队列返回 null。
    /// 缓存一致性由 _sync 保证：AddPostMap/Clear 在锁内移除/清空缓存；逆序数组构建同样在 _sync 内完成——
    /// 与写路径互斥，杜绝旧快照回插竞态（原实现无锁构建 + TryAdd，AddPostMap 后旧快照可能永久回插，
    /// 新回调永不生效）。热路径（缓存命中）无锁，性能不受影响。
    /// </summary>
    private Action<object, object>[]? TryGetReversed((Type Source, Type Target) key)
    {
        // 负缓存命中 = 该类型对确定无回调，免锁免双字典查找直接返回
        if (_postMapNegativeCache.ContainsKey(key)) return null;
        if (_postMapReversedCache.TryGetValue(key, out var reversed)) return reversed;
        lock (_sync)
        {
            // 双检锁：等待锁期间 AddPostMap/Clear 可能已重建或移除缓存
            if (_postMapNegativeCache.ContainsKey(key)) return null;
            if (_postMapReversedCache.TryGetValue(key, out reversed)) return reversed;
            if (!_postMapCallbacks.TryGetValue(key, out var queue))
            {
                // 负结果入缓存（锁内写，与 AddPostMap/Clear 的清除互斥——无旧负缓存回插竞态：
                // 若负缓存写入先于 AddPostMap 的锁内 TryRemove → 被清除；后于 → 此处 callbacks 必命中不会写入）
                _postMapNegativeCache.TryAdd(key, 0);
                return null;
            }
            var arr = queue.ToArray();
            Array.Reverse(arr);
            _postMapReversedCache.TryAdd(key, arr);
            return arr;
        }
    }

    /// <summary>
    /// 管道是否启用（实例状态） — 本实例有任何注册回调时为 true，为空时为 false
    /// <para>volatile 保证多线程可见性，管道为空时 Mapper.Map 跳过管道检查（零开销）</para>
    /// <para>
    /// 原 static 标志与实例回调字典混杂——任意实例 Clear() 会拉低全局标志致单例回调静默失效、
    /// 任意实例注册/清空互增全局版本号致降级缓存反复失效；标志实例化后各实例
    /// 状态隔离，公共入口经 <see cref="Mapper.Pipeline"/>（全局单例）。</para>
    /// </summary>
    internal volatile bool _isEnabled = false;

    /// <summary>
    /// 管道版本号（实例状态） — 本实例每次 AddPostMap/Clear 时递增
    /// <para>volatile：写侧 Interlocked 为全栅栏，但读侧（降级缓存键）在 ARM 弱内存序下普通读可能读到旧值，volatile 保证读侧可见性</para>
    /// <para>用于 ProjectionStrategy 降级缓存的 key，确保 Pipeline 注册后旧缓存失效</para>
    /// </summary>
    internal volatile int _version = 0;


    /// <summary>
    /// 获取管道是否启用（本实例）
    /// </summary>
    public bool IsEnabled => _isEnabled;

    /// <summary>
    /// 获取管道版本号（本实例）
    /// </summary>
    internal int Version => _version;

    /// <summary>
    /// 注册 PostMap 回调 — 映射后执行，可修改目标对象
    /// <para>按注册逆序执行（洋葱模型）</para>
    /// <para>类型过滤：只对指定的 TSource→TTarget 类型对生效</para>
    /// </summary>
    /// <typeparam name="TSource">源实体类型</typeparam>
    /// <typeparam name="TTarget">目标 DTO 类型</typeparam>
    /// <param name="callback">回调函数，接收源对象和目标对象</param>
    /// <returns>this（支持链式注册）</returns>
    public MapPipeline AddPostMap<TSource, TTarget>(Action<TSource, TTarget> callback)
        where TSource : class
        where TTarget : class
    {
        // /：队列写入、标志置位、版本递增、缓存清除五动作整体入锁，
        // 与 Clear 互斥，消除"Enqueue 后标志被清覆盖 / 队列被 Clear 吞掉 / 旧快照回插"三类过渡态
        lock (_sync)
        {
            var key = (typeof(TSource), typeof(TTarget));
            var queue = _postMapCallbacks.GetOrAdd(key, _ => new ConcurrentQueue<Action<object, object>>());

            // 包装回调，将 object 转换为强类型
            Action<object, object> wrapped = (src, tgt) =>
            {
                if (src is TSource typedSrc && tgt is TTarget typedTgt)
                {
                    callback(typedSrc, typedTgt);
                }
                else
                {
                    MapTracer.Write($"[NCMapper] Pipeline: PostMap<{typeof(TSource).Name}→{typeof(TTarget).Name}> 回调未执行（原因：运行时类型不匹配，实际 src={src?.GetType().Name ?? "null"}, tgt={tgt?.GetType().Name ?? "null"}）");
                }
            };

            queue.Enqueue(wrapped);
            _isEnabled = true;
            System.Threading.Interlocked.Increment(ref _version);
            // 清除反转缓存，确保新添加的 PostMap 回调生效
            _postMapReversedCache.TryRemove(key, out _);
            // 同步失效负缓存——注册后同类型对下次 Map 必须重新走注册判定
            _postMapNegativeCache.TryRemove(key, out _);
            return this;
        }
    }

    /// <summary>
    /// 清除所有管道回调 — 后续映射不走管道
    /// </summary>
    public void Clear()
    {
        // /：与 AddPostMap 同锁互斥，确保清空序列与注册序列原子交错，无中间过渡态可见
        lock (_sync)
        {
            foreach (var kvp in _postMapCallbacks)
            {
                while (kvp.Value.TryDequeue(out _)) { }
            }
            _postMapCallbacks.Clear();
            _postMapReversedCache.Clear(); // P1-PostMap：同步清除逆序缓存
            _postMapNegativeCache.Clear(); // X7（A1-05）：同步清除负查找缓存
            _isEnabled = false;
            // AU递增版本号，使依赖 version 的 ProjectionStrategy 降级缓存失效
            System.Threading.Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// 执行 PostMap 回调 — 非泛型版本，供 ComposeRuntime 等无法在编译时确定类型对的场景使用
    /// <para>遍历所有匹配 (source.GetType(), target.GetType()) 的已注册 PostMap 回调</para>
    /// </summary>
    /// <param name="source">源对象</param>
    /// <param name="target">目标对象</param>
    internal void ExecutePostMapNonGeneric(object source, object target)
    {
        if (!_isEnabled) return;
        if (source == null || target == null) return;

        // 沿源类型基类链回退查找——原仅运行时精确类型
        // 匹配，派生源（DerivedSource）注册在基类（BaseSource）的回调静默不触发，与
        // Generic 入口（typeof(TSource) 编译期声明）口径分叉；基类链回退后
        // Compose（NonGeneric 入口）+ 派生源类型回调可触发。命中最派生注册即执行（对齐
        // Generic 单层语义，不叠加基类回调）。
        for (var sourceType = source.GetType(); sourceType != null; sourceType = sourceType.BaseType)
        {
            var key = (sourceType, target.GetType());
            if (!_postMapCallbacks.TryGetValue(key, out _))
                continue;

            // 使用缓存的逆序数组（TryGetReversed 收敛）
            var reversed = TryGetReversed(key);
            if (reversed == null) continue;

            foreach (var callback in reversed)
            {
                callback(source, target);
            }
            return;
        }
    }

    /// <summary>
    /// 执行 PostMap 回调 — 按注册逆序执行（洋葱模型）
    /// <para>只执行匹配 TSource→TTarget 类型对的回调</para>
    /// </summary>
    /// <typeparam name="TSource">源实体类型</typeparam>
    /// <typeparam name="TTarget">目标 DTO 类型</typeparam>
    /// <param name="source">源对象</param>
    /// <param name="target">目标对象</param>
    internal void ExecutePostMap<TSource, TTarget>(TSource source, TTarget target)
        where TSource : class
        where TTarget : class
    {
        if (!_isEnabled) return;

        // 精确匹配的类型对
        var key = (typeof(TSource), typeof(TTarget));

        // P1-PostMap 优化：首次调用时构建逆序数组并缓存（TryGetReversed 收敛），后续直接遍历
        var reversed = TryGetReversed(key);
        if (reversed == null) return;

        foreach (var callback in reversed)
        {
            callback(source, target);
        }
    }

    /// <summary>
    /// 查询指定类型对是否注册了 PostMap 回调 — 供 ProjectionStrategy 降级检测使用
    /// </summary>
    /// <param name="sourceType">指定源类型。</param>
    /// <param name="targetType">指定目标类型。</param>
    /// <returns>返回判断结果（true 表示是，false 表示否）。</returns>
    internal bool HasPostMapFor(Type sourceType, Type targetType)
        => _postMapCallbacks.ContainsKey((sourceType, targetType));

    #region 源生成器扩展方法公共门面

    /// <summary>
    /// 管道是否启用 — 供源生成器扩展方法守卫使用（volatile 读取，管道为空时 false）
    /// <para>转发全局单例（<see cref="Mapper.Pipeline"/>）实例状态——生成代码语义
    /// 固定为全局管线（与 Mapper.Map 入口一致），签名保持静态（生成侧零改动）。</para>
    /// </summary>
    public static bool IsPipelineEnabled => Mapper.Pipeline.IsEnabled;

    /// <summary>
    /// 执行 PostMap 回调 — 供源生成器扩展方法使用（委托到全局 Pipeline 实例）
    /// </summary>
    public static void RunPostMap<TSource, TTarget>(TSource source, TTarget target)
        where TSource : class
        where TTarget : class
        => Mapper.Pipeline.ExecutePostMap(source, target);

    #endregion
}
