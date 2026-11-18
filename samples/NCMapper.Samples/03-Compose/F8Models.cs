using NCMapper.Attributes;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Compose;

// ================================================================
// F8 演示模型
// F8：Compose 副源字段跨表 [Convert] 消费——副源字段的转换器在合成面正常执行
// ================================================================

/// <summary>F8 供应商副源实体（跨表字段源）。</summary>
public class F8VendorEntity
{
    /// <summary>供应商编码（int——转换器入参）</summary>
    public int VendorCode { get; set; }
}

/// <summary>
/// F8 演示 DTO —— 主源 OrderEntity + 副源 F8VendorEntity 合成；
/// 副源字段 VendorTag 标注 [Convert]（跨表转换消费演示）。
/// </summary>
[MapFrom(typeof(OrderEntity))]
public class F8ReportDto
{
    /// <summary>订单主键（主源字段）</summary>
    public int OrderId { get; set; }

    /// <summary>供应商标签（副源字段——跨表 [Convert]：int → 带装饰文本）</summary>
    [MapProperty(typeof(F8VendorEntity), nameof(F8VendorEntity.VendorCode))]
    [Convert(typeof(F8Conv), nameof(F8Conv.ToTag))]
    public string VendorTag { get; set; } = "";
}

/// <summary>F8 跨表转换器（副源字段消费面）。</summary>
public static class F8Conv
{
    /// <summary>副源字段转换：供应商编码 → 标签文本。</summary>
    public static string ToTag(int code) => $"V-{code:D4}";
}
