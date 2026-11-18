using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// ConvertUsing 自定义转换器属性测试 — 验证静态转换方法（T2 模板 M(TSource)）多字段组合和计算
/// <para>覆盖路径：多字段组合（FullName=FirstName+LastName）、多字段计算（TotalPrice=Price*Quantity）</para>
/// <para>每个 [Property] 默认运行 100 次随机输入，失败时自动 shrinking 到最小复现</para>
/// </summary>
public class ConverterProperties
{
    // ================================================================
    // 多字段组合 — FullName = FirstName + " " + LastName
    // ================================================================

    /// <summary>
    /// ConvertUsing 多字段组合正确 — FullName 等于 FirstName + 空格 + LastName
    /// </summary>
    [Property]
    public bool Map_ConvertUsing_FullName_Combines_FirstName_And_LastName(string firstName, string lastName)
    {
        var entity = new FuzzConverterEntity { FirstName = firstName, LastName = lastName };
        var dto = Mapper.Map<FuzzConverterEntity, FuzzConverterDto>(entity)!;
        return dto.FullName == $"{firstName} {lastName}";
    }

    // ================================================================
    // 多字段计算 — TotalPrice = Price * Quantity（decimal 避免溢出）
    // ================================================================

    /// <summary>
    /// ConvertUsing 多字段计算正确 — TotalPrice 等于 Price * Quantity
    /// </summary>
    [Property]
    public bool Map_ConvertUsing_TotalPrice_Calculates_Price_Times_Quantity(int price, int quantity)
    {
        var entity = new FuzzConverterEntity { Price = price, Quantity = quantity };
        var dto = Mapper.Map<FuzzConverterEntity, FuzzConverterDto>(entity)!;
        return dto.TotalPrice == (decimal)price * quantity;
    }

    // ================================================================
    // 非 ConvertUsing 字段正常映射
    // ================================================================

    /// <summary>
    /// ConvertUsing 不影响普通字段映射 — Id 字段正常映射，不被转换器干扰
    /// </summary>
    [Property]
    public bool Map_ConvertUsing_Preserves_Normal_Fields(int id, string firstName, string lastName)
    {
        var entity = new FuzzConverterEntity
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName
        };
        var dto = Mapper.Map<FuzzConverterEntity, FuzzConverterDto>(entity)!;
        return dto.Id == id
            && dto.FullName == $"{firstName} {lastName}";
    }

    // ================================================================
    // 全字段组合 — 所有 ConvertUsing 字段同时映射
    // ================================================================

    /// <summary>
    /// 全字段 ConvertUsing 组合正确 — Id、FullName、TotalPrice 同时映射后值一致
    /// </summary>
    [Property]
    public bool Map_ConvertUsing_All_Fields_Correct(int id, string firstName, string lastName, int price, int quantity)
    {
        var entity = new FuzzConverterEntity
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            Price = price,
            Quantity = quantity
        };
        var dto = Mapper.Map<FuzzConverterEntity, FuzzConverterDto>(entity)!;
        return dto.Id == id
            && dto.FullName == $"{firstName} {lastName}"
            && dto.TotalPrice == (decimal)price * quantity;
    }
}
