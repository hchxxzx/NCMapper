using NCMapper.Attributes;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Models.Dtos;

/// <summary>
/// 订单详情 DTO — Compose 目标，主源 OrderEntity + 副源 CustomerEntity。
/// <para>映射自 <see cref="OrderEntity"/>（主源）和 <see cref="CustomerEntity"/>（副源）。</para>
/// </summary>
    #region Common_OrderDetailDto
[MapFrom(typeof(OrderEntity))]
public class OrderDetailDto
{
    #endregion

    /// <summary>
    /// 订单 ID — 映射自 OrderEntity.OrderId（异名映射）。
    /// </summary>
    #region Common_OrderDetailDto_OrderId
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderId))]
    public int OrderId { get; set; }
    #endregion


    /// <summary>
    /// 订单编号 — 映射自 OrderEntity.OrderNo（异名映射）。
    /// </summary>
    #region Common_OrderDetailDto_OrderNumber
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderNo))]
    public string OrderNumber { get; set; } = "";
    #endregion


    /// <summary>
    /// 订单总金额 — 映射自 OrderEntity.Amount（异名映射）。
    /// </summary>
    #region Common_OrderDetailDto_TotalAmount
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.Amount))]
    public decimal TotalAmount { get; set; }
    #endregion


    /// <summary>
    /// 订单状态文本 — 枚举→字符串转换（OrderStatus → 文本）。
    /// </summary>
    #region Common_OrderDetailDto_StatusText
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.Status))]
    public string StatusText { get; set; } = "";
    #endregion


    /// <summary>
    /// 客户姓名 — 映射自 CustomerEntity.Name（Compose 副源）。
    /// </summary>
    #region Common_OrderDetailDto_CustomerName
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
    public string CustomerName { get; set; } = "";
    #endregion


    /// <summary>
    /// 客户等级文本 — 映射自 CustomerEntity.Level（枚举→字符串，Compose 副源）。
    /// </summary>
    #region Common_OrderDetailDto_CustomerLevel
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Level))]
    public string CustomerLevel { get; set; } = "";
    #endregion


    /// <summary>
    /// 客户电话 — 映射自 CustomerEntity.Phone（Compose 副源）。
    /// </summary>
    #region Common_OrderDetailDto_CustomerPhone
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Phone))]
    public string CustomerPhone { get; set; } = "";
}
    #endregion
