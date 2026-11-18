using NCMapper.Samples.Models.Enums;

namespace NCMapper.Samples.Models.Entities;

/// <summary>
/// 客户实体 — 演示自定义转换器（电话脱敏）和 Compose 副源。
/// </summary>
public class CustomerEntity
{
    /// <summary>
    /// 客户姓名。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 客户等级。
    /// </summary>
    public CustomerLevel Level { get; set; }

    /// <summary>
    /// 客户电话 — 配合 PhoneMaskConverter 实现脱敏。
    /// </summary>
    public string Phone { get; set; } = "";

    /// <summary>
    /// 所属地区。
    /// </summary>
    public string Region { get; set; } = "";
}
