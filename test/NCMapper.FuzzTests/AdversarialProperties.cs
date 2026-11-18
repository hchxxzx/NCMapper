using FsCheck;
using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// T3-5: 对抗性属性测试 — 异常路径不抛未处理异常
/// <para>验证 NCMapper 在恶意/畸形输入下的防御性行为</para>
/// </summary>
public class AdversarialProperties
{
    // ================================================================
    // 随机字符串→数值转换：不抛未处理异常
    // ================================================================

    /// <summary>
    /// 任意字符串赋值到 string 属性后映射不崩溃
    /// </summary>
    [Property(MaxTest = 200)]
    public bool Map_ArbitraryString_NeverCrashes(string name)
    {
        var entity = new FuzzUser { Id = 1, Name = name ?? "" };
        var dto = Mapper.Map<FuzzUser, FuzzUserDto>(entity);
        return dto != null && dto.Name == (name ?? "");
    }

    /// <summary>
    /// null 源对象→Map 返回 null（不抛 NRE）
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Map_NullSource_ReturnsNull(bool dummy)
    {
        var result = Mapper.Map<FuzzUser, FuzzUserDto>(null!);
        return result == null;
    }

    // ================================================================
    // null 集合元素→MapList 不崩溃
    // ================================================================

    /// <summary>
    /// 含 null 元素的集合→MapList 跳过 null 或映射为 null，不抛未处理异常
    /// </summary>
    [Property(MaxTest = 200)]
    public bool MapList_WithNullElements_NoUnhandledException(int count)
    {
        var n = Math.Abs(count % 20); // 限制集合大小
        var list = new List<FuzzUser?>();
        for (int i = 0; i < n; i++)
            list.Add(i % 3 == 0 ? null : new FuzzUser { Id = i, Name = $"u{i}" });

        // 不应抛未处理异常
        try
        {
            var result = Mapper.MapList<FuzzUser, FuzzUserDto>(list!);
            return result != null;
        }
        catch (MapException)
        {
            return true; // MapException 是预期的受控异常
        }
    }

    /// <summary>
    /// 空集合→MapList 返回空列表
    /// </summary>
    [Property(MaxTest = 50)]
    public bool MapList_EmptySource_ReturnsEmpty(bool dummy)
    {
        var result = Mapper.MapList<FuzzUser, FuzzUserDto>(new List<FuzzUser>());
        return result != null && result.Count == 0;
    }

    // ================================================================
    // 极端数值边界
    // ================================================================

    /// <summary>
    /// 极端 int 值（MinValue/MaxValue/0）映射不崩溃
    /// </summary>
    [Property(MaxTest = 200)]
    public bool Map_ExtremeIntValues_Preserved(int id)
    {
        var entity = new FuzzUser { Id = id, Name = "x" };
        var dto = Mapper.Map<FuzzUser, FuzzUserDto>(entity);
        return dto != null && dto.Id == id;
    }

    /// <summary>
    /// 极端 double 值（NaN/Infinity）映射不崩溃
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Map_SpecialDoubles_NoCrash(double score)
    {
        var entity = new FuzzUser { Id = 1, Name = "x", Score = score };
        try
        {
            var dto = Mapper.Map<FuzzUser, FuzzUserDto>(entity);
            return dto != null;
        }
        catch (MapException)
        {
            return true; // 受控异常可接受
        }
    }
}
