namespace NCMapper.Samples.Models.Enums;

/// <summary>
/// 用户状态枚举。
/// </summary>
public enum UserStatus
{
    /// <summary>
    /// 活跃。
    /// </summary>
    Active = 0,

    /// <summary>
    /// 非活跃。
    /// </summary>
    Inactive = 1,

    /// <summary>
    /// 已停用。
    /// </summary>
    Suspended = 2,
}

/// <summary>
/// 订单状态枚举。
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// 待处理。
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 处理中。
    /// </summary>
    Processing = 1,

    /// <summary>
    /// 已发货。
    /// </summary>
    Shipped = 2,

    /// <summary>
    /// 已送达。
    /// </summary>
    Delivered = 3,

    /// <summary>
    /// 已取消。
    /// </summary>
    Cancelled = 4,
}

/// <summary>
/// 客户等级枚举。
/// </summary>
public enum CustomerLevel
{
    /// <summary>
    /// 普通客户。
    /// </summary>
    Normal = 0,

    /// <summary>
    /// 银卡客户。
    /// </summary>
    Silver = 1,

    /// <summary>
    /// 金卡客户。
    /// </summary>
    Gold = 2,

    /// <summary>
    /// 白金客户。
    /// </summary>
    Platinum = 3,
}
