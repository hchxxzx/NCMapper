using FreeSql.DataAnnotations;

namespace NCMapper.Samples.Models.Entities;

/// <summary>
/// IncludeMany 降级演示 — 分类实体（一对多：Category → Items）
/// </summary>
public class ImCategory
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
    /// 商品集合（一对多）
    /// </summary>
    [Navigate(nameof(ImItem.CategoryId))]
    public List<ImItem> Items { get; set; } = new();
}

/// <summary>
/// IncludeMany 降级演示 — 商品实体
/// </summary>
public class ImItem
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
    /// <summary>
    /// 所属分类外键
    /// </summary>
    public int CategoryId { get; set; }
}
