using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
namespace NCMapper;
/// <summary>
/// 映射器接口 — 依赖注入用，包装静态 Mapper 为实例方法
/// </summary>
public interface IMapper
{
    /// <summary>
    /// 同类型对象拷贝
    /// </summary>
    /// <typeparam name="TSource">源/目标类型</typeparam>
    /// <param name="source">源对象</param>
    /// <param name="action">回调节点，可在此处对对象进行后续操作，可选</param>
    /// <returns>转换后的目标对象；source 为 null 时返回 default</returns>
    [RequiresDynamicCode("Map uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Map scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    TSource? Map<TSource>(TSource source, Action<TSource, TSource>? action = null) where TSource : class;

    /// <summary>
    /// 跨类型对象映射
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <param name="source">源对象</param>
    /// <param name="action">回调节点，可在此处对对象进行后续操作，可选</param>
    /// <returns>转换后的目标对象；source 为 null 时返回 default</returns>
    [RequiresDynamicCode("Map uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Map scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    TTarget? Map<TSource, TTarget>(TSource source, Action<TSource, TTarget>? action = null) where TSource : class where TTarget : class;

    /// <summary>
    /// 更新已有目标对象
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <param name="source">源对象</param>
    /// <param name="target">需要更新的目标对象</param>
    /// <param name="action">回调节点，可在此处对对象进行后续操作，可选</param>
    [RequiresDynamicCode("Map uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("Map scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    void Map<TSource, TTarget>(TSource source, TTarget target, Action<TSource, TTarget>? action = null) where TSource : class where TTarget : class;

    /// <summary>
    /// 同类型列表拷贝
    /// </summary>
    /// <typeparam name="TTarget">列表元素类型</typeparam>
    /// <param name="sources">源列表对象</param>
    /// <param name="action">回调节点，可在此处对对象进行后续操作，可选</param>
    /// <returns>转换后的目标列表；sources 为 null 时返回 null</returns>
    [RequiresDynamicCode("MapList uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("MapList scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    List<TTarget>? MapList<TTarget>(IEnumerable<TTarget> sources, Action<TTarget, TTarget>? action = null) where TTarget : class;

    /// <summary>
    /// 跨类型列表映射
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <param name="sources">源列表对象</param>
    /// <param name="action">回调节点，可在此处对对象进行后续操作，可选</param>
    /// <returns>转换后的目标列表；sources 为 null 时返回 null</returns>
    [RequiresDynamicCode("MapList uses Expression.Compile and reflection in the runtime fallback path. Not supported in AOT. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    [RequiresUnreferencedCode("MapList scans type members and attributes via reflection in the runtime fallback path. Some members may be trimmed. Mark the DTO with [MapFrom] to use the AOT-safe source-generated path.")]
    List<TTarget>? MapList<TSource, TTarget>(IEnumerable<TSource> sources, Action<TSource, TTarget>? action = null) where TSource : class where TTarget : class;

    /// <summary>
    /// 多源合成 — 从多个源对象合成一个 DTO
    /// </summary>
    /// <typeparam name="TTarget">目标 DTO 类型</typeparam>
    /// <param name="sources">源对象列表（类型需与 DTO 的 [MapProperty] 标注匹配）</param>
    /// <returns>合成后的 DTO 对象</returns>
    [RequiresDynamicCode("Compose uses MakeGenericType and MakeGenericMethod to build the compose delegate. Not supported in AOT. Register a source-generated compose delegate via RegisterGeneratedCompose to make this AOT-safe.")]
    [RequiresUnreferencedCode("Compose scans type members and attributes via reflection. Some members may be trimmed. Register a source-generated compose delegate via RegisterGeneratedCompose to make this AOT-safe.")]
    TTarget? Compose<TTarget>(params object[] sources) where TTarget : class;

    /// <summary>
    /// 深拷贝 — 递归创建完全独立的副本
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="source">需要深拷贝的源对象</param>
    /// <returns>深拷贝后的新对象；source 为 null 时返回 default</returns>
    [RequiresDynamicCode("DeepCopy uses Expression.Compile and MakeGenericMethod in the runtime fallback path. Not supported in AOT. Use the source-generated deep copy path to make this AOT-safe.")]
    [RequiresUnreferencedCode("DeepCopy scans type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated deep copy path to make this AOT-safe.")]
    T? DeepCopy<T>(T source) where T : class;

}
