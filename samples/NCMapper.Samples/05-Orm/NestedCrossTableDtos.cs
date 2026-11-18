using NCMapper.Attributes;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Models.Dtos;

// ================================================================
// 嵌套跨表投影演示 — DTO 模型
// <para>DemoOrderDto 嵌套 DemoOrderDetailDto，后者通过 [MapProperty] 映射
// DemoProduct 的跨表字段（ProductSku / ProductCategory / ProductPrice）</para>
// <para>ORM 投影路径自动翻译为多级 LEFT JOIN；MapList 路径通过导航属性内存解析</para>
// ================================================================

/// <summary>
/// 订单 DTO — 含嵌套明细 DTO。
/// </summary>
[MapFrom(typeof(DemoOrder))]
public class DemoOrderDto
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
    /// 嵌套明细 DTO
    /// </summary>
    public DemoOrderDetailDto? Detail { get; set; }
}

/// <summary>
/// 明细 DTO — 含跨表 [MapProperty] 从 DemoProduct 取字段。
/// <para>ProductName / ProductSku / ProductCategory / ProductPrice 均通过
/// Detail.Product 导航属性跨表取值，无需在 ORM 查询中手动 Join。</para>
/// </summary>
[MapFrom(typeof(DemoOrderDetail))]
public class DemoOrderDetailDto
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
    /// 产品名称 — MapProperty 跨表映射自 DemoProduct.Name（经 Detail.Product 导航）
    /// </summary>
    [MapProperty(typeof(DemoProduct), nameof(DemoProduct.Name))]
    public string? ProductName { get; set; }

    /// <summary>
    /// SKU 编码 — MapProperty 跨表映射自 DemoProduct.Sku
    /// </summary>
    [MapProperty(typeof(DemoProduct), nameof(DemoProduct.Sku))]
    public string? ProductSku { get; set; }

    /// <summary>
    /// 产品分类 — MapProperty 跨表映射自 DemoProduct.Category
    /// </summary>
    [MapProperty(typeof(DemoProduct), nameof(DemoProduct.Category))]
    public string? ProductCategory { get; set; }

    /// <summary>
    /// 售价（int 目标）— MapProperty 跨表映射自 DemoProduct.Price（null 导航 → 默认 0）
    /// </summary>
    [MapProperty(typeof(DemoProduct), nameof(DemoProduct.Price))]
    public int ProductPrice { get; set; }
}

/// <summary>
/// 明细 DTO（int? 变体）— ProductPrice 为可空值类型（null 导航 → null）
/// </summary>
[MapFrom(typeof(DemoOrderDetail))]
public class DemoOrderDetailNullableDto
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 售价（int? 目标）— null 导航时返回 null，非默认 0
    /// </summary>
    [MapProperty(typeof(DemoProduct), nameof(DemoProduct.Price))]
    public int? ProductPrice { get; set; }
}

/// <summary>
/// 订单 DTO（int? 变体）— 嵌套 DemoOrderDetailNullableDto
/// </summary>
[MapFrom(typeof(DemoOrder))]
public class DemoOrderNullableDto
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
    /// 嵌套明细 DTO（含 int? 跨表字段）
    /// </summary>
    public DemoOrderDetailNullableDto? Detail { get; set; }
}

// ================================================================
// AsDto 自动降级演示 — DTO 带 ConvertUsing 触发二步法
// <para>DemoOrderDetailMaskDto 在跨表字段 ProductSku 上标注 [Convert]，
// 导致嵌套 DTO 整体触发降级：AsDto 自动退化为"查实体 → 内存 MapList"</para>
// <para>对比：DemoOrderDto（无 Mask/ConvertUsing）→ AsDto 走 SQL 投影路径</para>
// ================================================================

/// <summary>
/// SKU 大写转换器 — 内存操作，ORM 无法翻译为 SQL，标注此转换器的属性会触发 AsDto 降级。
/// <para>T1 模板 M(TField)；TField = MapProperty 声明的源字段类型（DemoProduct.Sku，string?）。</para>
/// </summary>
public static class SkuUpperConverter
{
    /// <summary>
    /// 将 SKU 编码转为大写。
    /// </summary>
    /// <param name="fieldValue">源字段值（SKU 编码，可空）</param>
    /// <returns>大写后的 SKU 编码</returns>
    public static string Convert(string? fieldValue)
        => (fieldValue ?? "").ToUpperInvariant();
}

/// <summary>
/// 明细 DTO（带降级触发器）— ProductSku 标注 [Convert]，
/// ORM 无法将 C# 委托翻译为 SQL，AsDto 自动降级为二步法（查实体 → 内存映射）。
/// <para>其他跨表字段（ProductName/ProductCategory/ProductPrice）不受影响，仍在内存阶段正常映射。</para>
/// </summary>
[MapFrom(typeof(DemoOrderDetail))]
public class DemoOrderDetailMaskDto
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
    /// 产品名称 — 跨表映射
    /// </summary>
    [MapProperty(typeof(DemoProduct), nameof(DemoProduct.Name))]
    public string? ProductName { get; set; }

    /// <summary>
    /// SKU 编码 — 跨表映射 + [Convert] 大写转换。
    /// <para>⚠ [Convert] 触发 AsDto 整体降级为二步法。</para>
    /// </summary>
    [MapProperty(typeof(DemoProduct), nameof(DemoProduct.Sku))]
    [Convert(typeof(SkuUpperConverter), nameof(SkuUpperConverter.Convert))]
    public string? ProductSku { get; set; }

    /// <summary>
    /// 产品分类 — 跨表映射
    /// </summary>
    [MapProperty(typeof(DemoProduct), nameof(DemoProduct.Category))]
    public string? ProductCategory { get; set; }

    /// <summary>
    /// 售价 — 跨表映射
    /// </summary>
    [MapProperty(typeof(DemoProduct), nameof(DemoProduct.Price))]
    public int ProductPrice { get; set; }
}

/// <summary>
/// 订单 DTO（嵌套将触发降级的 DetailMaskDto）。
/// <para>与 DemoOrderDto 的唯一区别：Detail 类型为 DemoOrderDetailMaskDto（含 [Convert]）。</para>
/// </summary>
[MapFrom(typeof(DemoOrder))]
public class DemoOrderMaskDto
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
    /// 嵌套明细 DTO（含 [Convert] 触发器）
    /// </summary>
    public DemoOrderDetailMaskDto? Detail { get; set; }
}
