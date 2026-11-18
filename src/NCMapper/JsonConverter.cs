using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
namespace NCMapper;
/// <summary>
/// JSON 转换辅助类 — 提供对象与 JSON 字符串互转以及跨类型 JSON 中转映射，供表达式树 Lambda 和源生成代码调用
/// <para>内部转发到 <see cref="JsonSerializerBase.Current"/>，用户可通过替换 Current 实例来使用不同的 JSON 序列化方案</para>
/// </summary>
public static class JsonConverter
{
    /// <summary>
    /// 将JSON字符串转换为指定类型的对象
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="json">JSON字符串</param>
    /// <returns>反序列化后的对象；json 为空或空白时返回 default</returns>
    /// <exception cref="MapException">JSON 反序列化失败（包装：畸形 JSON/嵌套过深/STJ 不支持类型；InnerException 保留诊断信息）</exception>
    [RequiresDynamicCode("JSON deserialization uses reflection. Not supported in AOT. Provide a source-generated JsonSerializerContext via JsonSerializerBase.Current to make this AOT-safe.")]
    [RequiresUnreferencedCode("JSON deserialization uses reflection. Some members may be trimmed. Provide a source-generated JsonSerializerContext via JsonSerializerBase.Current to make this AOT-safe.")]
    public static T? JsonConvertToObject<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default(T);
        try
        {
            return (T?)JsonSerializerBase.Current.Deserialize(json, typeof(T));
        }
        catch (JsonException ex)
        {
            // IsFromJson 反序列化异常包装为 MapException（不新增异常类型，保留 InnerException）
            throw new MapException(ErrorMessages.JsonDeserializeFailed(typeof(T).Name), ex);
        }
        catch (NotSupportedException ex)
        {
            // STJ 对不支持类型抛 NotSupportedException，同样包装；不得吞未预期异常
            throw new MapException(ErrorMessages.JsonDeserializeFailed(typeof(T).Name), ex);
        }
    }

    /// <summary>
    /// 将指定对象转换为JSON文本
    /// </summary>
    /// <param name="info">需要序列化的源对象</param>
    /// <returns>JSON字符串；info 为 null 时返回空字符串</returns>
    /// <exception cref="MapException">JSON 序列化失败（A3-09，包装：STJ 不支持类型/畸形输出；InnerException 保留诊断信息）</exception>
    [RequiresDynamicCode("JSON serialization uses reflection. Not supported in AOT. Provide a source-generated JsonSerializerContext via JsonSerializerBase.Current to make this AOT-safe.")]
    [RequiresUnreferencedCode("JSON serialization uses reflection. Some members may be trimmed. Provide a source-generated JsonSerializerContext via JsonSerializerBase.Current to make this AOT-safe.")]
    public static string ObjectConvertToJson(object info)
    {
        if (info == null) return "";
        try
        {
            return JsonSerializerBase.Current.Serialize(info);
        }
        catch (JsonException ex)
        {
            // 序列化方向异常裸逃逸——与反序列化入口
            //（JsonConvertToObject）D7 包装契约对齐
            throw new MapException(ErrorMessages.JsonSerializeFailed(info.GetType().Name), ex);
        }
        catch (NotSupportedException ex)
        {
            throw new MapException(ErrorMessages.JsonSerializeFailed(info.GetType().Name), ex);
        }
    }

    /// <summary>
    /// 将指定对象通过JSON中转转换为其他类型对象
    /// </summary>
    /// <typeparam name="Tin">源类型</typeparam>
    /// <typeparam name="Tout">目标类型</typeparam>
    /// <param name="source">源对象</param>
    /// <returns>转换后的目标对象；source 为 null 时返回 default</returns>
    /// <exception cref="MapException">JSON 序列化或反序列化失败（包装，含 JsonConvertToTarget:50 同类泄漏点；InnerException 保留诊断信息）</exception>
    [RequiresDynamicCode("JSON round-trip conversion uses reflection. Not supported in AOT. Provide a source-generated JsonSerializerContext via JsonSerializerBase.Current to make this AOT-safe.")]
    [RequiresUnreferencedCode("JSON round-trip conversion uses reflection. Some members may be trimmed. Provide a source-generated JsonSerializerContext via JsonSerializerBase.Current to make this AOT-safe.")]
    public static Tout? JsonConvertToTarget<Tin, Tout>(Tin? source)
    {
        if (source == null) return default(Tout);
        string json;
        try
        {
            json = JsonSerializerBase.Current.Serialize(source);
        }
        catch (JsonException ex)
        {
            // 序列化方向原在 try 外裸逃逸——并入 D7 包装契约
            throw new MapException(ErrorMessages.JsonSerializeFailed(typeof(Tin).Name), ex);
        }
        catch (NotSupportedException ex)
        {
            throw new MapException(ErrorMessages.JsonSerializeFailed(typeof(Tin).Name), ex);
        }
        try
        {
            return (Tout?)JsonSerializerBase.Current.Deserialize(json, typeof(Tout));
        }
        catch (JsonException ex)
        {
            // 反序列化方向同类泄漏点一并包装
            throw new MapException(ErrorMessages.JsonDeserializeFailed(typeof(Tout).Name), ex);
        }
        catch (NotSupportedException ex)
        {
            // STJ 对不支持类型抛 NotSupportedException，同样包装；不得吞未预期异常
            throw new MapException(ErrorMessages.JsonDeserializeFailed(typeof(Tout).Name), ex);
        }
    }
}
