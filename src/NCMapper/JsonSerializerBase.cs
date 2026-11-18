using System.Text.Json;
namespace NCMapper;
/// <summary>
/// JSON 序列化抽象基类 — 用户可继承此类替换默认的 System.Text.Json 实现
/// <para>用法：继承此类并实现 Serialize/Deserialize，然后设置 <see cref="Current"/></para>
/// <code>
/// // 示例：替换为 Newtonsoft.Json
/// public class NewtonsoftJsonSerializer : JsonSerializerBase
/// {
///     public override string Serialize(object? obj) => Newtonsoft.Json.JsonConvert.SerializeObject(obj);
///     public override object? Deserialize(string json, Type targetType) => Newtonsoft.Json.JsonConvert.DeserializeObject(json, targetType);
/// }
/// JsonSerializerBase.Current = new NewtonsoftJsonSerializer();
/// </code>
/// </summary>
public abstract class JsonSerializerBase
{
    /// <summary>
    /// 全局当前实例 — 默认为 <see cref="SystemTextJsonSerializer"/>
    /// <para>替换后，所有 JSON 中转映射（[MapProperty(IsFromJson=true)]）和 JSON 深拷贝都将使用新实例</para>
    /// <para>赋值为 null 不受保护——后续 JSON 中转映射（[MapProperty(IsFromJson=true)]）、JSON 深拷贝（JSON 策略）或 JsonConverter 辅助路径将抛 NullReferenceException；如需恢复默认行为，请重新赋值为 SystemTextJsonSerializer 实例</para>
    /// <para>替换即时生效——所有 JSON 路径均在方法体每次执行时直读 Current，表达式树编译委托经 JsonConverter 方法间接消费，替换后下一次执行即用新实例，无追溯豁免</para>
    /// </summary>
    public static JsonSerializerBase Current
    {
        get => s_current;
        set => s_current = value;
    }

    /// <summary>
    /// 当前序列化器实例（volatile 保证多线程可见性）
    /// </summary>
    private static volatile JsonSerializerBase s_current = new SystemTextJsonSerializer();

    /// <summary>
    /// 将对象序列化为 JSON 字符串
    /// <para><b>AOT/Trimming</b>：具体实现的 Serialize 方法可能使用反射，AOT 下不可用</para>
    /// </summary>
    /// <param name="obj">需要序列化的对象（可为 null）</param>
    /// <returns>JSON 字符串；obj 为 null 时返回空字符串</returns>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("JSON serialization uses reflection. This is not supported in AOT/Trimmed scenarios. Consider using a source-generated JsonSerializerContext.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON serialization uses reflection. Some types may be trimmed in AOT/Trimmed scenarios.")]
    public abstract string Serialize(object? obj);

    /// <summary>
    /// 将 JSON 字符串反序列化为指定类型的对象
    /// <para><b>AOT/Trimming</b>：具体实现的 Deserialize 方法可能使用反射，AOT 下不可用</para>
    /// </summary>
    /// <param name="json">JSON 字符串</param>
    /// <param name="targetType">目标类型</param>
    /// <returns>反序列化后的对象；json 为空时返回 null</returns>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("JSON deserialization uses reflection. This is not supported in AOT/Trimmed scenarios. Consider using a source-generated JsonSerializerContext.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON deserialization uses reflection. Some types may be trimmed in AOT/Trimmed scenarios.")]
    public abstract object? Deserialize(string json, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] Type targetType);
}

/// <summary>
/// 基于 System.Text.Json 的默认 JSON 序列化实现
/// <para>支持通过构造函数注入自定义 <see cref="JsonSerializerOptions"/></para>
/// </summary>
internal sealed class SystemTextJsonSerializer : JsonSerializerBase
{
    /// <summary>
    /// 默认 JSON 序列化最大深度 — 与 System.Text.Json 默认值一致
    /// </summary>
    private const int DefaultJsonMaxDepth = 64;

    /// <summary>
    /// 序列化选项（大小写不敏感、MaxDepth 上限等）
    /// </summary>
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// 默认实例 — 使用内置默认选项（大小写不敏感、MaxDepth=64）
    /// <para>死字段
    /// （Current 初始化未引用、库内零消费）——本批 [Obsolete] 标记过渡，1.2.0 删除。
    /// 替代写法：<c>JsonSerializerBase.Current</c>（全局入口）或
    /// <c>new SystemTextJsonSerializer()</c>（需独立实例时）。</para>
    /// </summary>
    [Obsolete("Default 为死字段（Current 初始化未引用）。请使用 JsonSerializerBase.Current 或 new SystemTextJsonSerializer()。本字段将于 1.2.0 删除。详见 CHANGELOG。")]
    public static readonly SystemTextJsonSerializer Default = new();

    /// <summary>
    /// 使用默认选项创建实例
    /// </summary>
    public SystemTextJsonSerializer() : this(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = DefaultJsonMaxDepth
    })
    {
    }

    /// <summary>
    /// 使用自定义选项创建实例
    /// </summary>
    /// <param name="options">JSON 序列化选项</param>
    public SystemTextJsonSerializer(JsonSerializerOptions options)
    {
        _options = options;
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("JSON serialization uses reflection. This is not supported in AOT/Trimmed scenarios. Consider using a source-generated JsonSerializerContext.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON serialization uses reflection. Some types may be trimmed in AOT/Trimmed scenarios.")]
    public override string Serialize(object? obj)
    {
        if (obj == null) return "";
        return JsonSerializer.Serialize(obj, _options);
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("JSON deserialization uses reflection. This is not supported in AOT/Trimmed scenarios. Consider using a source-generated JsonSerializerContext.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON deserialization uses reflection. Some types may be trimmed in AOT/Trimmed scenarios.")]
    public override object? Deserialize(string json, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] Type targetType)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize(json, targetType, _options);
    }
}
