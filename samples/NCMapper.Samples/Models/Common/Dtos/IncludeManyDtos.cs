using NCMapper.Attributes;

namespace NCMapper.Samples.Models.Dtos;

/// <summary>
/// IncludeMany 降级演示 — 分类 DTO（含商品列表）
/// </summary>
[MapFrom(typeof(Entities.ImCategory))]
public class ImCategoryDto
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 分类名
    /// </summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// 商品列表（一对多集合导航，投影中触发降级）
    /// </summary>
    public List<ImItemDto> Items { get; set; } = new();
}

/// <summary>
/// IncludeMany 降级演示 — 商品 DTO
/// </summary>
[MapFrom(typeof(Entities.ImItem))]
public class ImItemDto
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 商品名
    /// </summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// 价格
    /// </summary>
    public decimal Price { get; set; }
}
