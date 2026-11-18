using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// 映射核心路径属性测试 — 用 FsCheck 自动生成随机输入，验证不变式恒成立
/// <para>覆盖路径：DeepCopy / Map / MapList / 双向映射 / null 安全</para>
/// <para>每个 [Property] 默认运行 100 次随机输入，失败时自动 shrinking 到最小复现</para>
/// </summary>
public class MappingProperties
{
    // ================================================================
    // DeepCopy — 深拷贝不变式
    // ================================================================

    /// <summary>
    /// DeepCopy 保留所有基本类型字段值 — 任意输入下深拷贝内容一致
    /// </summary>
    [Property]
    public bool DeepCopy_Preserves_Simple_Fields(int id, string name, int age, bool isActive, double score, DateTime createdAt)
    {
        var user = new FuzzUser
        {
            Id = id, Name = name, Age = age,
            IsActive = isActive, Score = score, CreatedAt = createdAt
        };
        var copy = Mapper.DeepCopy(user)!;
        return copy.Id == user.Id
            && copy.Name == user.Name
            && copy.Age == user.Age
            && copy.IsActive == user.IsActive
            && DoubleEqual(copy.Score, user.Score)
            && copy.CreatedAt == user.CreatedAt;
    }

    /// <summary>
    /// DeepCopy 创建独立副本 — 修改副本不影响原件
    /// </summary>
    [Property]
    public bool DeepCopy_Creates_Independent_Reference(int id, string name)
    {
        var user = new FuzzUser { Id = id, Name = name };
        var copy = Mapper.DeepCopy(user)!;
        copy.Id = -999;
        copy.Name = "MODIFIED";
        return user.Id == id && user.Name == name;
    }

    /// <summary>
    /// DeepCopy null 源返回 null — 顶层 null 守卫
    /// </summary>
    [Property]
    public bool DeepCopy_Null_Source_Returns_Null(FuzzUser? user)
    {
        // FsCheck 会生成 null 和非 null 两种情况
        var copy = Mapper.DeepCopy(user!);
        return user is null ? copy is null : copy is not null;
    }

    // ================================================================
    // Map — 映射正确性不变式
    // ================================================================

    /// <summary>
    /// Map 保留所有基本类型字段值 — 任意输入下映射后字段一致
    /// </summary>
    [Property]
    public bool Map_Preserves_Simple_Fields(int id, string name, int age, bool isActive, double score, DateTime createdAt)
    {
        var user = new FuzzUser
        {
            Id = id, Name = name, Age = age,
            IsActive = isActive, Score = score, CreatedAt = createdAt
        };
        var dto = Mapper.Map<FuzzUser, FuzzUserDto>(user)!;
        return dto.Id == user.Id
            && dto.Name == user.Name
            && dto.Age == user.Age
            && dto.IsActive == user.IsActive
            && DoubleEqual(dto.Score, user.Score)
            && dto.CreatedAt == user.CreatedAt;
    }

    /// <summary>
    /// Map null 源返回 null — null 守卫
    /// </summary>
    [Property]
    public bool Map_Null_Source_Returns_Null(FuzzUser? user)
    {
        var dto = Mapper.Map<FuzzUser, FuzzUserDto>(user!);
        return user is null ? dto is null : dto is not null;
    }

    // ================================================================
    // MapList — 列表映射一致性
    // ================================================================

    /// <summary>
    /// MapList 单元素结果与逐个 Map 一致
    /// </summary>
    [Property]
    public bool MapList_Single_Element_Equals_Map(int id, string name, int age)
    {
        var user = new FuzzUser { Id = id, Name = name, Age = age };
        var list = new List<FuzzUser> { user };
        var mappedList = Mapper.MapList<FuzzUser, FuzzUserDto>(list)!;
        var mappedSingle = Mapper.Map<FuzzUser, FuzzUserDto>(user)!;
        return mappedList.Count == 1
            && mappedList[0].Id == mappedSingle.Id
            && mappedList[0].Name == mappedSingle.Name
            && mappedList[0].Age == mappedSingle.Age;
    }

    // ================================================================
    // 双向映射 — 正反向一致性
    // ================================================================

    /// <summary>
    /// 双向映射往返一致 — ReverseMap(Map(x)) 字段 == 原始 x 字段
    /// </summary>
    [Property]
    public bool Map_Reverse_RoundTrip_Preserves_Fields(int id, string name, int age, bool isActive, double score)
    {
        var user = new FuzzUser
        {
            Id = id, Name = name, Age = age,
            IsActive = isActive, Score = score
        };
        var dto = Mapper.Map<FuzzUser, FuzzUserDto>(user)!;
        var roundTrip = Mapper.Map<FuzzUserDto, FuzzUser>(dto)!;
        return roundTrip.Id == user.Id
            && roundTrip.Name == user.Name
            && roundTrip.Age == user.Age
            && roundTrip.IsActive == user.IsActive
            && DoubleEqual(roundTrip.Score, user.Score);
    }

    // ================================================================
    // 辅助方法
    // ================================================================

    /// <summary>
    /// double 相等比较 — 处理 IEEE 754 NaN 语义
    /// <para>C# 中 NaN == NaN 返回 false，但映射/深拷贝后 NaN 被正确复制时应视为相等</para>
    /// <para>+0 == -0 保持 true，Infinity == Infinity 保持 true</para>
    /// </summary>
    /// <param name="a">第一个 double 值</param>
    /// <param name="b">第二个 double 值</param>
    /// <returns>两值语义相等返回 true，否则 false</returns>
    private static bool DoubleEqual(double a, double b)
    {
        if (double.IsNaN(a) && double.IsNaN(b)) return true;
        return a == b;
    }
}
