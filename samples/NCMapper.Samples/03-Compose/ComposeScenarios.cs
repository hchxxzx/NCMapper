using NCMapper.Generated;
using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;
using NCMapper.Samples.Helpers;
using NCMapper.Samples.Models.Enums;

namespace NCMapper.Samples.Compose;

/// <summary>
/// 多源合成场景集 — 从多个源对象合成一个 DTO。
/// </summary>
public static class ComposeScenarios
{
    /// <summary>
    /// 双源合成：OrderEntity + CustomerEntity → OrderDetailDto。
    /// </summary>
    /// <param name="order">主源（OrderEntity）</param>
    /// <param name="customer">副源（CustomerEntity）</param>
    /// <returns>合成后的 OrderDetailDto</returns>
    #region Compose_BasicCompose
    public static OrderDetailDto BasicCompose(OrderEntity order, CustomerEntity customer)
        => Mapper.Compose<OrderDetailDto>(order, customer)!;
    #endregion


    /// <summary>
    /// 三源合成：OrderEntity + CustomerEntity + ProductEntity → ReportDto。
    /// </summary>
    /// <param name="order">主源（OrderEntity）</param>
    /// <param name="customer">副源（CustomerEntity）</param>
    /// <param name="product">第三源（ProductEntity）</param>
    /// <returns>合成后的 ReportDto</returns>
    #region Compose_BasicCompose_MultiSourceCompose
    public static ReportDto MultiSourceCompose(OrderEntity order, CustomerEntity customer, ProductEntity product)
        => Mapper.Compose<ReportDto>(order, customer, product)!;
    #endregion


    /// <summary>
    /// Compose 合成后追加处理。
    /// </summary>
    /// <param name="order">主源</param>
    /// <param name="customer">副源</param>
    /// <returns>带自定义标签的合成 DTO</returns>
    #region Compose_BasicCompose_ComposeWithAction
    public static OrderDetailDto ComposeWithAction(OrderEntity order, CustomerEntity customer)
    {
        var dto = Mapper.Compose<OrderDetailDto>(order, customer)!;
        dto.CustomerName = $"[VIP] {dto.CustomerName}";
        return dto;
    }

    #endregion
    /// <summary>
    /// ComposeList：从多组源对象批量合成 DTO 列表。
    /// </summary>
    /// <param name="orders">订单实体数组</param>
    /// <param name="customers">客户实体数组（长度相同）</param>
    /// <returns>合成后的 OrderDetailDto 列表</returns>
    #region Compose_ComposeList
    public static List<OrderDetailDto> ComposeList(OrderEntity[] orders, CustomerEntity[] customers)
    {
        var sourceGroups = new object[orders.Length][];
        for (int i = 0; i < orders.Length; i++)
            sourceGroups[i] = new object[] { orders[i], customers[i] };
        return Mapper.ComposeList<OrderDetailDto>(sourceGroups)!;
    }
    #endregion

    /// <summary>
    /// Compose + 内联 Action 回调（Action 作为第一参数）。
    /// </summary>
    /// <param name="order">主源</param>
    /// <param name="customer">副源</param>
    /// <returns>经 Action 修改后的合成 DTO</returns>
    #region Compose_L60
    public static OrderDetailDto ComposeWithInlineAction(OrderEntity order, CustomerEntity customer)
        => Mapper.Compose<OrderDetailDto>(dto => dto.CustomerName = $"[Inline] {dto.CustomerName}", order, customer)!;
    #endregion

    /// <summary>
    /// ComposeList + 内联 Action 回调。
    /// </summary>
    /// <param name="orders">订单实体数组</param>
    /// <param name="customers">客户实体数组</param>
    /// <returns>合成后的 DTO 列表</returns>
    #region Compose_ComposeListWithAction
    public static List<OrderDetailDto> ComposeListWithAction(OrderEntity[] orders, CustomerEntity[] customers)
    {
        var sourceGroups = new object[orders.Length][];
        for (int i = 0; i < orders.Length; i++)
            sourceGroups[i] = new object[] { orders[i], customers[i] };
        return Mapper.ComposeList<OrderDetailDto>(dto => { }, sourceGroups)!;
    }
    #endregion

    /// <summary>
    /// 声明跨表子源字段的 DTO 的反向直调（DTO → Entity）：所有 [MapFrom] DTO（含此类）统一生成反向扩展。
    /// <para>语义边界：反向扩展指向 [MapFrom] 主源实体（MapToOrderEntity），副源字段
    /// （CustomerName / CustomerLevel / CustomerPhone）不参与反向写回——反向只覆盖主源字段。</para>
    /// </summary>
    /// <param name="dto">声明跨表字段的 DTO</param>
    /// <returns>反向映射的订单实体（主源字段已写回）</returns>
    #region Compose_L83
    public static OrderEntity ComposeReverseDirect(OrderDetailDto dto)
        => dto.MapToOrderEntity()!;
    #endregion

    // ===== F8 演示=====

    /// <summary>
    /// F8 — Compose 副源字段跨表 [Convert] 消费：副源 VendorCode 经转换器
    /// （int → "V-xxxx" 标签）在合成面正常执行。
    /// </summary>
    /// <param name="order">主源订单</param>
    /// <param name="vendor">副源供应商</param>
    /// <returns>副源字段已转换的合成 DTO</returns>
    public static F8ReportDto CrossTableConvertCompose(OrderEntity order, F8VendorEntity vendor)
        => Mapper.Compose<F8ReportDto>(order, vendor)!;

    /// <summary>Compose 末尾触发已注册 PostMap：合成完成后 OrderNumber 追加审计标签，片段收尾清理管道。</summary>
    #region Compose_ComposeWithPostMap
    public static OrderDetailDto ComposeWithPostMap(OrderEntity order, CustomerEntity customer)
    {
        // Register a PostMap scoped to the OrderEntity -> OrderDetailDto pair
        Mapper.Pipeline.AddPostMap<OrderEntity, OrderDetailDto>((src, tgt) =>
            tgt.OrderNumber = $"{tgt.OrderNumber} [verified]");
        // Compose fires the registered PostMap at the end of the composition
        var dto = Mapper.Compose<OrderDetailDto>(order, customer)!;
        Mapper.Pipeline.Clear();
        return dto;
    }
    #endregion

    // ===== 场景注册入口=====
    public static void Run()
    {
    var order = new OrderEntity
    {
        OrderId = 1001,
        OrderNo = "ORD-2026-001",
        Amount = 2999.00m,
        Status = OrderStatus.Shipped,
    };

    var customer = new CustomerEntity
    {
        Name = "ZhaoLiu",
        Level = CustomerLevel.Platinum,
        Phone = "13611112222",
        Region = "Shenzhen",
    };

    var composeResult = ComposeScenarios.BasicCompose(order, customer);
    OutputHelper.Dump("301-basic-compose", new { order.OrderNo, order.Amount, customer.Name }, composeResult);
    // 期望值：Compose<TTarget> 移交断言——字段来源跨对象聚合语义 + OrderDetailDto [MapProperty] 异名标注 OrderNo→OrderNumber
    if (composeResult.OrderId != order.OrderId || composeResult.OrderNumber != order.OrderNo)
        OutputHelper.Error($"301-basic-compose: Compose should aggregate order fields (actual: OrderId={composeResult.OrderId}, OrderNumber={composeResult.OrderNumber})");

    var composeWithAction = ComposeScenarios.ComposeWithAction(order, customer);
    OutputHelper.Dump("301-compose-with-action", new { order.OrderNo, customer.Name }, composeWithAction);

    var product = new ProductEntity
    {
        ProductId = 5001,
        Name = "High-Performance Server",
        Price = 15800.00m,
    };

    var reportResult = ComposeScenarios.MultiSourceCompose(order, customer, product);
    OutputHelper.Dump("301-multi-source-compose", new { orderNo = order.OrderNo, customerName = customer.Name, productName = product.Name }, reportResult);

    var orders = new[] { order, new OrderEntity { OrderId = 2, OrderNo = "ORD-002", Amount = 200m } };
    var customers = new[] { customer, new CustomerEntity { Name = "Guest", Level = CustomerLevel.Normal, Phone = "999" } };
    var composeList = ComposeScenarios.ComposeList(orders, customers);
    OutputHelper.Dump("301-compose-list", $"Count: {orders.Length}", composeList);
    // 期望值：ComposeList<TTarget> 移交断言——批量守恒 + OrderNumber 异名落位
    if (composeList.Count != orders.Length || composeList[0].OrderNumber == "")
        OutputHelper.Error($"301-compose-list: ComposeList should map {orders.Length} items with order fields (actual: {composeList.Count})");

    // 声明跨表字段的 DTO 的反向直调（所有 [MapFrom] DTO 统一生成反向扩展）：只写回主源字段
    var composeReverse = ComposeScenarios.ComposeReverseDirect(composeResult);
    OutputHelper.Dump("301-compose-reverse-direct", composeResult, composeReverse);

    // F8：Compose 副源字段跨表 [Convert] 消费
    var f8Vendor = new F8VendorEntity { VendorCode = 42 };
    var f8Report = ComposeScenarios.CrossTableConvertCompose(order, f8Vendor);
    OutputHelper.Dump("301-f8-cross-table-convert", new { order.OrderId, f8Vendor.VendorCode }, f8Report);
    if (f8Report.OrderId != order.OrderId || f8Report.VendorTag != "V-0042")
        OutputHelper.Error("301-f8-cross-table-convert: cross-table convert consumption of secondary-source field mismatch (VendorTag should be V-0042)");
    }

}
