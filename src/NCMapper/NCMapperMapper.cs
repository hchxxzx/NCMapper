using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
namespace NCMapper;
/// <summary>
/// IMapper 的默认实现 — 纯适配器（H-07⑥）：直接转发到静态 Mapper，零额外开销；
/// 保留接口价值（DI 注册 / 测试替身 / 实例语义），不承载任何映射逻辑。
/// </summary>
public sealed class NCMapperMapper : IMapper
{
    /// <summary>
    /// 同类型对象拷贝
    /// </summary>
    /// <param name="source">源对象</param>
    /// <param name="action">映射后回调操作，可选</param>
    /// <returns>转换后的目标对象；source 为 null 时返回 default</returns>
    [RequiresDynamicCode("Map uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Map scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public TSource? Map<TSource>(TSource source, Action<TSource, TSource>? action = null) where TSource : class
        => Mapper.Map<TSource>(source, action);

    /// <summary>
    /// 跨类型对象映射
    /// </summary>
    /// <param name="source">源对象</param>
    /// <param name="action">映射后回调操作，可选</param>
    /// <returns>转换后的目标对象；source 为 null 时返回 default</returns>
    [RequiresDynamicCode("Map uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Map scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public TTarget? Map<TSource, TTarget>(TSource source, Action<TSource, TTarget>? action = null) where TSource : class where TTarget : class
        => Mapper.Map<TSource, TTarget>(source, action);

    /// <summary>
    /// 更新已有目标对象
    /// </summary>
    /// <param name="source">源对象</param>
    /// <param name="target">需要更新的目标对象</param>
    /// <param name="action">映射后回调操作，可选</param>
    [RequiresDynamicCode("Map uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Map scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public void Map<TSource, TTarget>(TSource source, TTarget target, Action<TSource, TTarget>? action = null) where TSource : class where TTarget : class
        => Mapper.Map<TSource, TTarget>(source, target, action);

    /// <summary>
    /// 同类型列表拷贝
    /// </summary>
    /// <param name="sources">源列表</param>
    /// <param name="action">映射后回调操作，可选</param>
    /// <returns>转换后的目标列表；sources 为 null 时返回 null</returns>
    [RequiresDynamicCode("MapList uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("MapList scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public List<TTarget>? MapList<TTarget>(IEnumerable<TTarget> sources, Action<TTarget, TTarget>? action = null) where TTarget : class
        => Mapper.MapList<TTarget>(sources, action);

    /// <summary>
    /// 跨类型列表映射
    /// </summary>
    /// <param name="sources">源列表</param>
    /// <param name="action">映射后回调操作，可选</param>
    /// <returns>转换后的目标列表；sources 为 null 时返回 null</returns>
    [RequiresDynamicCode("MapList uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("MapList scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    public List<TTarget>? MapList<TSource, TTarget>(IEnumerable<TSource> sources, Action<TSource, TTarget>? action = null) where TSource : class where TTarget : class
        => Mapper.MapList<TSource, TTarget>(sources, action);

    /// <summary>
    /// 多源合成 — 从多个源对象合成一个 DTO
    /// </summary>
    /// <param name="sources">源对象数组</param>
    /// <returns>合成后的目标对象；无匹配时返回 default</returns>
    // L-11: NCMapper001 抑制理由 — 接口转发方法，Compose 由运行时处理，无需源生成器实现
#pragma warning disable NCMapper001
    [RequiresDynamicCode("Compose uses MakeGenericType and MakeGenericMethod to build the compose delegate. Not supported in AOT. Register a source-generated compose delegate via RegisterGeneratedCompose to make this AOT-safe.")]
    [RequiresUnreferencedCode("Compose scans type members and attributes via reflection. Some members may be trimmed. Register a source-generated compose delegate via RegisterGeneratedCompose to make this AOT-safe.")]
    public TTarget? Compose<TTarget>(params object[] sources) where TTarget : class
        => Mapper.Compose<TTarget>(sources);
#pragma warning restore NCMapper001

    /// <summary>
    /// 深拷贝 — 递归创建完全独立的副本
    /// </summary>
    /// <param name="source">需要深拷贝的源对象</param>
    /// <returns>深拷贝后的新对象；source 为 null 时返回 default</returns>
    [RequiresDynamicCode("DeepCopy uses Expression.Compile and MakeGenericMethod in the runtime fallback path. Not supported in AOT. Use the source-generated deep copy path to make this AOT-safe.")]
    [RequiresUnreferencedCode("DeepCopy scans type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated deep copy path to make this AOT-safe.")]
    public T? DeepCopy<T>(T source) where T : class
        => Mapper.DeepCopy<T>(source);
}
