using NCMapper.Attributes;
using NCMapper.Generated;

namespace NCMapper.Samples.Declarative;

// ================================================================
// 嵌套映射控制场景集（02-Declarative 域）— 直转文档绑定源（B1 第一波）。
// 文档演示专用：不注册 ScenarioRegistry 执行路径，仅编译与 DFM 引用。
// ================================================================

/// <summary>
/// 嵌套内联深度控制场景集（直转文档 Core_Nested_InlineDepthTune 的绑定源）。
/// </summary>
public static class NestedScenarios
{
    /// <summary>调整内联深度（范围 [0, 16]）：深内联换运行时性能、置 0 全走委托换更小表达式树，用后恢复。</summary>
    #region Core_Nested_InlineDepthTune
    public static void InlineDepthTune()
    {
        int saved = Mapper.MaxInlineDepth;
        try
        {
            // Deeper inlining (range [0, 16]): favors deep object graphs, costs compile time
            Mapper.MaxInlineDepth = 8;
            // All calls go through delegates: smallest expression-tree footprint
            Mapper.MaxInlineDepth = 0;
        }
        finally
        {
            // Restore default: global knob, must not leak across scenarios
            Mapper.MaxInlineDepth = saved;
        }
    }
    #endregion

    /// <summary>
    /// 嵌套导航扁平化巡览 — DTO 扁平名自动匹配 Customer.Name / Shipper.Phone 路径（源生成扩展直调）。
    /// </summary>
    /// <param name="src">含 Customer/Shipper 导航的发票实体</param>
    /// <returns>扁平化发票 DTO</returns>
    #region Core_Nested_InvoiceFlatTour
    public static NmInvoiceDto InvoiceFlat(NmInvoiceEntity src)
        => src.MapToNmInvoiceDto()!;
    #endregion
}

/// <summary>
/// 发票源实体 — Customer/Shipper 为嵌套导航属性（扁平化演示源）。
/// </summary>
public class NmInvoiceEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>客户导航属性</summary>
    public NmCustomerEntity Customer { get; set; } = new();

    /// <summary>承运人导航属性</summary>
    public NmShipperEntity Shipper { get; set; } = new();
}

/// <summary>
/// 发票客户实体（嵌套导航目标）。
/// </summary>
public class NmCustomerEntity
{
    /// <summary>姓名</summary>
    public string Name { get; set; } = "";

    /// <summary>邮箱</summary>
    public string Email { get; set; } = "";
}

/// <summary>
/// 发票承运人实体（嵌套导航目标）。
/// </summary>
public class NmShipperEntity
{
    /// <summary>姓名</summary>
    public string Name { get; set; } = "";

    /// <summary>电话</summary>
    public string Phone { get; set; } = "";
}

/// <summary>
/// 扁平发票 DTO — 扁平属性名自动匹配嵌套路径（CustomerName → Customer.Name 等）。
/// </summary>
[MapFrom(typeof(NmInvoiceEntity))]
public class NmInvoiceDto
{
    /// <summary>主键（同名直接匹配）</summary>
    public int Id { get; set; }

    /// <summary>→ Customer.Name</summary>
    public string CustomerName { get; set; } = "";

    /// <summary>→ Customer.Email</summary>
    public string CustomerEmail { get; set; } = "";

    /// <summary>→ Shipper.Name</summary>
    public string ShipperName { get; set; } = "";

    /// <summary>→ Shipper.Phone</summary>
    public string ShipperPhone { get; set; } = "";
}
