using NCMapper.Attributes;

namespace NCMapper.Samples.Declarative;

// ================================================================
// JSON 字段映射 — 内联演示模型
// ================================================================

/// <summary>
/// 模拟数据库实体 — 配置信息以 JSON 文本列存储。
/// </summary>
public class OrderConfigEntity
{
    /// <summary>
    /// 标识。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 订单号。
    /// </summary>
    public string OrderNo { get; set; } = "";

    /// <summary>
    /// JSON 文本列：收货地址。
    /// </summary>
    public string ShippingAddressJson { get; set; } = "{}";

    /// <summary>
    /// JSON 文本列：标签列表。
    /// </summary>
    public string TagsJson { get; set; } = "[]";
}

/// <summary>
/// DTO — 将 JSON 列反序列化为强类型。
/// </summary>
[MapFrom(typeof(OrderConfigEntity))]
public class OrderConfigDto
{
    /// <summary>
    /// 标识。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 订单号。
    /// </summary>
    public string OrderNo { get; set; } = "";

    /// <summary>
    /// 收货地址 — 从 ShippingAddressJson 反序列化（IsFromJson = true）。
    /// </summary>
    [MapProperty(typeof(OrderConfigEntity), nameof(OrderConfigEntity.ShippingAddressJson), IsFromJson = true)]
    public ShippingAddress? ShippingAddress { get; set; }

    /// <summary>
    /// 标签列表 — 从 TagsJson 反序列化（IsFromJson = true）。
    /// </summary>
    [MapProperty(typeof(OrderConfigEntity), nameof(OrderConfigEntity.TagsJson), IsFromJson = true)]
    public List<string>? Tags { get; set; }
}

/// <summary>
/// 嵌套对象 — 收货地址。
/// </summary>
public class ShippingAddress
{
    /// <summary>
    /// 城市。
    /// </summary>
    public string City { get; set; } = "";

    /// <summary>
    /// 街道。
    /// </summary>
    public string Street { get; set; } = "";

    /// <summary>
    /// 邮编。
    /// </summary>
    public string ZipCode { get; set; } = "";
}

/// <summary>
/// 值类型 JSON 实体 — 演示不支持的场景。
/// </summary>
public class ValueTypeJsonEntity
{
    /// <summary>
    /// 标识。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Score JSON。
    /// </summary>
    public string ScoreJson { get; set; } = "0";

    /// <summary>
    /// Date JSON。
    /// </summary>
    public string DateJson { get; set; } = "\"2000-01-01T00:00:00\"";
}

/// <summary>
/// 值类型目标 DTO — IsFromJson 标注在值类型属性上会被跳过。
/// </summary>
[MapFrom(typeof(ValueTypeJsonEntity))]
public class ValueTypeJsonDto
{
    /// <summary>
    /// 标识。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Score — 值类型，IsFromJson 不支持，映射被跳过。
    /// </summary>
    [MapProperty(typeof(ValueTypeJsonEntity), nameof(ValueTypeJsonEntity.ScoreJson), IsFromJson = true)]
    public int Score { get; set; }

    /// <summary>
    /// Date — 值类型，IsFromJson 不支持，映射被跳过。
    /// </summary>
    [MapProperty(typeof(ValueTypeJsonEntity), nameof(ValueTypeJsonEntity.DateJson), IsFromJson = true)]
    public DateTime Date { get; set; }
}

/// <summary>
/// 反向演示源 — 对象属性需要序列化为 JSON 字符串。
/// </summary>
public class OrderConfigSource
{
    /// <summary>
    /// 标识。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 订单号。
    /// </summary>
    public string OrderNo { get; set; } = "";

    /// <summary>
    /// 强类型对象属性。
    /// </summary>
    public ShippingAddress? ShippingAddress { get; set; }
}

/// <summary>
/// 反向演示 DTO — 将对象属性序列化为 JSON 文本列。
/// </summary>
[MapFrom(typeof(OrderConfigSource))]
public class OrderConfigReverseDto
{
    /// <summary>
    /// 标识。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 订单号。
    /// </summary>
    public string OrderNo { get; set; } = "";

    /// <summary>
    /// 收货地址 JSON — 从 ShippingAddress 序列化（IsFromJson = true）。
    /// </summary>
    [MapProperty(typeof(OrderConfigSource), nameof(OrderConfigSource.ShippingAddress), IsFromJson = true)]
    public string ShippingAddressJson { get; set; } = "";
}
