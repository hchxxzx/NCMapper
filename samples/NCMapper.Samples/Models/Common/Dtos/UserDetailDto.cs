using NCMapper.Attributes;
using NCMapper.Samples.Models.Converters;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Models.Dtos;

/// <summary>
/// 用户详情 DTO — 演示 [MapProperty] 异名字段、[MapIgnore] 方向控制、[Convert] 自定义转换、PascalCase 扁平化。
/// <para>映射自 <see cref="UserEntity"/>。</para>
/// </summary>
[MapFrom(typeof(UserEntity))]
public class UserDetailDto
{
    /// <summary>
    /// 用户 ID — 映射自 UserEntity.Id。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 用户显示名 — 映射自 UserEntity.Name（异名映射）。
    /// </summary>
    [MapProperty(typeof(UserEntity), nameof(UserEntity.Name))]
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// 用户年龄 — 映射自 UserEntity.Age。
    /// </summary>
    public int Age { get; set; }

    /// <summary>
    /// 用户邮箱 — 映射自 UserEntity.Email。
    /// </summary>
    public string Email { get; set; } = "";

    /// <summary>
    /// 状态文本 — 枚举→字符串转换（UserStatus → "Active"/"Inactive"/"Suspended"）。
    /// </summary>
    [MapProperty(typeof(UserEntity), nameof(UserEntity.Status))]
    public string StatusText { get; set; } = "";

    /// <summary>
    /// 注册日期 — 正向通过 DateTimeFormatter 将 DateTime 格式化为日期字符串；
    /// 反向通过 DateBackParser 将日期字符串解析回 DateTime（[ConvertBack]，与正向转换器同属性并列）。
    /// </summary>
    [MapProperty(typeof(UserEntity), nameof(UserEntity.CreatedAt))]
    [Convert(typeof(DateTimeFormatter), nameof(DateTimeFormatter.Convert))]
    [ConvertBack(typeof(DateBackParser), nameof(DateBackParser.Convert))]
    public string RegisterDate { get; set; } = "";

    /// <summary>
    /// 城市名 — PascalCase 扁平化：自动匹配 UserEntity.Address.City（源生成器编译时解析）。
    /// </summary>
    public string AddressCity { get; set; } = "";

    /// <summary>
    /// 街道 — PascalCase 扁平化：自动匹配 UserEntity.Address.Street（源生成器编译时解析）。
    /// </summary>
    public string AddressStreet { get; set; } = "";

    /// <summary>
    /// 全名 — [Convert] 多字段组合（Name + Email，T2 模板 M(TSource)）。
    /// <para>无对应源属性，转换方法直接以 source 参数读取源对象字段。</para>
    /// </summary>
    [Convert(typeof(FullNameConverter), nameof(FullNameConverter.Convert))]
    public string FullName { get; set; } = "";

    /// <summary>
    /// 内部备注 — [MapIgnore(Direction.Both)] 演示：DTO 独有字段，不参与双向映射。
    /// </summary>
    [MapIgnore(Direction.Both)]
    public string InternalNote { get; set; } = "";
}
