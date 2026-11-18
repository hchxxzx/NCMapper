using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// 类型转换边界属性测试 — 验证 NCMapper 对隐式类型转换和枚举映射的处理
/// <para>覆盖转换路径：int→long（隐式扩展）、int→decimal（隐式转换）、enum→int（取底层值）</para>
/// <para>M-15: 每个 [Property] 运行 500 次随机输入（关键路径加强），失败时自动 shrinking 到最小复现</para>
/// </summary>
public class TypeConversionProperties
{
    // ================================================================
    // int → long — 隐式扩展转换（无精度损失）
    // ================================================================

    /// <summary>
    /// int→long 保留值 — 任意 int 值映射为 long 后数值一致
    /// </summary>
    [Property(MaxTest = 500)]
    public bool Map_Int_To_Long_Preserves_Value(int id)
    {
        var entity = new FuzzTypeEntity { Id = id };
        var dto = Mapper.Map<FuzzTypeEntity, FuzzTypeDto>(entity)!;
        return dto.Id == (long)id;
    }

    // ================================================================
    // int → decimal — 隐式转换（无精度损失）
    // ================================================================

    /// <summary>
    /// int→decimal 保留值 — 任意 int 值映射为 decimal 后数值一致
    /// </summary>
    [Property(MaxTest = 500)]
    public bool Map_Int_To_Decimal_Preserves_Value(int score)
    {
        var entity = new FuzzTypeEntity { Score = score };
        var dto = Mapper.Map<FuzzTypeEntity, FuzzTypeDto>(entity)!;
        return dto.Score == (decimal)score;
    }

    // ================================================================
    // enum → int — 取枚举底层值
    // ================================================================

    /// <summary>
    /// enum→int 保留值 — 任意 FuzzStatus 值映射为 int 后等于底层值
    /// </summary>
    [Property(MaxTest = 500)]
    public bool Map_Enum_To_Int_Preserves_Value(FuzzStatus status)
    {
        var entity = new FuzzTypeEntity { Status = status };
        var dto = Mapper.Map<FuzzTypeEntity, FuzzTypeDto>(entity)!;
        return dto.Status == (int)status;
    }

    // ================================================================
    // 组合转换 — 所有字段同时映射
    // ================================================================

    /// <summary>
    /// 组合转换保留所有字段 — int→long、int→decimal、enum→int 同时映射后值一致
    /// </summary>
    [Property(MaxTest = 500)]
    public bool Map_All_Type_Conversions_Preserves_Fields(int id, int score, FuzzStatus status)
    {
        var entity = new FuzzTypeEntity { Id = id, Score = score, Status = status };
        var dto = Mapper.Map<FuzzTypeEntity, FuzzTypeDto>(entity)!;
        return dto.Id == (long)id
            && dto.Score == (decimal)score
            && dto.Status == (int)status;
    }
}
