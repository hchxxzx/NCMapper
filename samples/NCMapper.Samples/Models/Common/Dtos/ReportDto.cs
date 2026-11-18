using NCMapper.Attributes;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Models.Dtos;

/// <summary>
/// 报表 DTO — Compose 多源合成演示，主源 OrderEntity + 副源 CustomerEntity + 副源 ProductEntity。
/// </summary>
[MapFrom(typeof(OrderEntity))]
public class ReportDto
{
    /// <summary>
    /// 订单编号 — 映射自 OrderEntity.OrderNo（异名映射）。
    /// </summary>
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderNo))]
    public string OrderNumber { get; set; } = "";

    /// <summary>
    /// 订单金额 — 映射自 OrderEntity.Amount（异名映射）。
    /// </summary>
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.Amount))]
    public decimal OrderAmount { get; set; }

    /// <summary>
    /// 客户姓名 — 映射自 CustomerEntity.Name（Compose 副源）。
    /// </summary>
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
    public string CustomerName { get; set; } = "";

    /// <summary>
    /// 客户地区 — 映射自 CustomerEntity.Region（Compose 副源）。
    /// </summary>
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Region))]
    public string CustomerRegion { get; set; } = "";

    /// <summary>
    /// 产品名称 — 映射自 ProductEntity.Name（Compose 副源）。
    /// </summary>
    [MapProperty(typeof(ProductEntity), nameof(ProductEntity.Name))]
    public string ProductName { get; set; } = "";

    /// <summary>
    /// 产品类别 — 映射自 ProductEntity.Category（Compose 副源）。
    /// </summary>
    [MapProperty(typeof(ProductEntity), nameof(ProductEntity.Category))]
    public string ProductCategory { get; set; } = "";
}
