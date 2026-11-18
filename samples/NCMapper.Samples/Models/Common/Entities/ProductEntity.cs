namespace NCMapper.Samples.Models.Entities;

/// <summary>
/// 产品实体 — 演示 DateTimeFormatter 和 Compose 第三源。
/// </summary>
public class ProductEntity
{
    /// <summary>
    /// 产品 ID。
    /// </summary>
    public int ProductId { get; set; }

    /// <summary>
    /// 产品名称。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 产品价格。
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 产品类别。
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
