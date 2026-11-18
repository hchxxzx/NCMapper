using NCMapper.Attributes;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Models.Dtos;

/// <summary>
/// 用户 DTO — 演示基础同名属性映射。
/// <para>映射自 <see cref="UserEntity"/>，按名称约定自动匹配。</para>
/// </summary>
    #region Common_UserDto
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    #endregion

    /// <summary>
    /// 用户 ID — 映射自 UserEntity.Id。
    /// </summary>
    #region Common_UserDto_Id
    public int Id { get; set; }
    #endregion


    /// <summary>
    /// 用户姓名 — 映射自 UserEntity.Name。
    /// </summary>
    #region Common_UserDto_Name
    public string Name { get; set; } = "";
    #endregion


    /// <summary>
    /// 用户年龄 — 映射自 UserEntity.Age。
    /// </summary>
    #region Common_UserDto_Age
    public int Age { get; set; }
    #endregion


    /// <summary>
    /// 用户邮箱 — 映射自 UserEntity.Email。
    /// </summary>
    #region Common_UserDto_Email
    public string Email { get; set; } = "";
}
    #endregion
