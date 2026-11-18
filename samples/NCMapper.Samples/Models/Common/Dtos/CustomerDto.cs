using NCMapper.Attributes;
using NCMapper.Samples.Models.Converters;
using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Models.Dtos;

/// <summary>
/// 客户 DTO — 演示 [MapIgnore] 方向控制（ToEntity）和 PhoneMaskConverter 脱敏。
/// <para>映射自 <see cref="CustomerEntity"/>。</para>
/// </summary>
[MapFrom(typeof(CustomerEntity))]
public class CustomerDto
{
    /// <summary>
    /// 客户姓名 — 映射自 CustomerEntity.Name。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 客户等级文本 — 枚举→字符串转换。
    /// </summary>
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Level))]
    public string Level { get; set; } = "";

    /// <summary>
    /// 客户电话（脱敏）— 通过 PhoneMaskConverter 脱敏（T1 模板 M(TField)）。
    /// </summary>
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Phone))]
    [Convert(typeof(PhoneMaskConverter), nameof(PhoneMaskConverter.Convert))]
    public string Phone { get; set; } = "";

    /// <summary>
    /// 所属地区 — 映射自 CustomerEntity.Region。
    /// </summary>
    public string Region { get; set; } = "";

    /// <summary>
    /// 显示标签 — [MapIgnore(Direction.Both)]，DTO 独有计算字段，不参与双向映射。
    /// </summary>
    [MapIgnore(Direction.Both)]
    public string DisplayLabel { get; set; } = "";
}
