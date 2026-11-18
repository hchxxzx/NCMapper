using NCMapper.Samples.Models.Enums;

namespace NCMapper.Samples.Models.Entities;

/// <summary>
/// 订单实体 — 演示枚举映射和 Compose 主源。
/// </summary>
public class OrderEntity
{
    /// <summary>
    /// 订单 ID。
    /// </summary>
    public int OrderId { get; set; }

    /// <summary>
    /// 订单编号。
    /// </summary>
    public string OrderNo { get; set; } = "";

    /// <summary>
    /// 订单金额。
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 关联用户 ID。
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// 订单状态。
    /// </summary>
    public OrderStatus Status { get; set; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
