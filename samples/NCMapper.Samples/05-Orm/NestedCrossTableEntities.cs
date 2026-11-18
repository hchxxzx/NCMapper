namespace NCMapper.Samples.Models.Entities;

// ================================================================
// 嵌套跨表投影演示 — 实体模型
// <para>链路：DemoOrder → DemoOrderDetail → DemoProduct（两级导航）</para>
// <para>用于演示嵌套 DTO 上 [MapProperty(typeof(DemoProduct), ...)] 跨表字段映射</para>
// ================================================================

/// <summary>
/// 订单实体 — 含 Detail 一对一导航属性。
/// </summary>
public class DemoOrder
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 订单编号
    /// </summary>
    public string OrderNo { get; set; } = "";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 明细外键（可空 — 用于演示一级 null 场景）
    /// </summary>
    public int? DetailId { get; set; }

    /// <summary>
    /// 明细导航属性
    /// </summary>
    public DemoOrderDetail? Detail { get; set; }
}

/// <summary>
/// 订单明细实体 — 含 Product 导航属性。
/// </summary>
public class DemoOrderDetail
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 数量
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// 单价
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 产品外键（可空 — 用于演示末级 null 场景）
    /// </summary>
    public int? ProductId { get; set; }

    /// <summary>
    /// 产品导航属性
    /// </summary>
    public DemoProduct? Product { get; set; }
}

/// <summary>
/// 产品实体 — 跨表映射的目标源类型。
/// </summary>
public class DemoProduct
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 产品名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// SKU 编码
    /// </summary>
    public string Sku { get; set; } = "";

    /// <summary>
    /// 产品分类
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// 售价（值类型 — 用于演示 null 导航时 default 行为）
    /// </summary>
    public int Price { get; set; }
}
