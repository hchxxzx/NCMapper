using NCMapper.Samples.Models.Enums;

namespace NCMapper.Samples.Models.Entities;

/// <summary>
/// 用户实体 — 演示嵌套对象映射和 PascalCase 扁平化。
/// </summary>
    #region Common_UserEntity
public class UserEntity
{
    #endregion

    /// <summary>
    /// 用户 ID。
    /// </summary>
    #region Common_UserEntity_Id
    public int Id { get; set; }
    #endregion


    /// <summary>
    /// 用户姓名。
    /// </summary>
    #region Common_UserEntity_Name
    public string Name { get; set; } = "";
    #endregion


    /// <summary>
    /// 用户年龄。
    /// </summary>
    #region Common_UserEntity_Age
    public int Age { get; set; }
    #endregion


    /// <summary>
    /// 用户邮箱。
    /// </summary>
    #region Common_UserEntity_Email
    public string Email { get; set; } = "";
    #endregion


    /// <summary>
    /// 用户状态。
    /// </summary>
    #region Common_UserEntity_Status
    public UserStatus Status { get; set; }
    #endregion


    /// <summary>
    /// 注册时间。
    /// </summary>
    #region Common_UserEntity_CreatedAt
    public DateTime CreatedAt { get; set; }
    #endregion


    /// <summary>
    /// 用户地址（嵌套对象 — 演示嵌套映射和 PascalCase 扁平化）。
    /// <para>PascalCase 扁平化：Address.City 自动匹配 CustomerCity 属性（AddressCity 亦可）。</para>
    /// </summary>
    #region Common_UserEntity_Address
    public Address? Address { get; set; }
}
    #endregion

/// <summary>
/// 地址对象 — 作为嵌套对象演示嵌套映射。
/// </summary>
public class Address
{
    /// <summary>
    /// 城市。
    /// </summary>
    public string City { get; set; } = "";

    /// <summary>
    /// 街道。
    /// </summary>
    public string Street { get; set; } = "";
}
