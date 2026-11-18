namespace NCMapper.Samples.Models.Converters;

/// <summary>
/// 手机号脱敏转换器 — 将手机号中间 4 位替换为 ****（T1 模板 M(TField)）。
/// <para>用法：[Convert(typeof(PhoneMaskConverter), nameof(PhoneMaskConverter.Convert))]</para>
/// <para>示例：13800138000 → 138****8000</para>
/// </summary>
public static class PhoneMaskConverter
{
    /// <summary>
    /// 对手机号进行脱敏处理。
    /// </summary>
    /// <param name="fieldValue">源字段值（手机号字符串）</param>
    /// <returns>脱敏后的手机号</returns>
    public static string Convert(string fieldValue)
    {
        if (string.IsNullOrEmpty(fieldValue) || fieldValue.Length < 7)
            return fieldValue;

        return fieldValue[..3] + "****" + fieldValue[^4..];
    }
}
