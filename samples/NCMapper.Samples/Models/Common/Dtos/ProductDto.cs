using NCMapper.Attributes;
using NCMapper.Samples.Models.Converters;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Models.Dtos;

/// <summary>
/// 产品 DTO — 演示 [Convert] DateTimeFormatter 和隐式数值转换。
/// <para>映射自 <see cref="ProductEntity"/>。</para>
/// </summary>
[MapFrom(typeof(ProductEntity))]
public class ProductDto
{
    /// <summary>
    /// 产品 ID — 映射自 ProductEntity.ProductId。
    /// </summary>
    public int ProductId { get; set; }

    /// <summary>
    /// 产品名称 — 映射自 ProductEntity.Name。
    /// </summary>
    [MapProperty(typeof(ProductEntity), nameof(ProductEntity.Name))]
    public string ProductName { get; set; } = "";

    /// <summary>
    /// 产品价格 — 映射自 ProductEntity.Price（decimal → double 隐式数值转换）。
    /// </summary>
    public double Price { get; set; }

    /// <summary>
    /// 产品类别 — 映射自 ProductEntity.Category。
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// 创建日期 — 通过 ProductDateTimeFormatter 将 DateTime 格式化为日期字符串（T1 模板 M(TField)）。
    /// <para>注意：T1 模板仅依赖字段类型（DateTime → string），跨实体可复用同一转换器类；
    /// 静态方法形态不再受 TSource 泛型绑定约束（接口形态的 TSource 必须与 [MapFrom] 源类型一致）。</para>
    /// </summary>
    [MapProperty(typeof(ProductEntity), nameof(ProductEntity.CreatedAt))]
    [Convert(typeof(ProductDateTimeFormatter), nameof(ProductDateTimeFormatter.Convert))]
    public string CreatedDate { get; set; } = "";
}
