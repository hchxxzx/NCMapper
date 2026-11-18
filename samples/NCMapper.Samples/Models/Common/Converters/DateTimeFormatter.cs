namespace NCMapper.Samples.Models.Converters;

/// <summary>
/// 日期时间格式化转换器 — 将 DateTime 转换为 "yyyy-MM-dd" 格式字符串（T1 模板 M(TField)）。
/// <para>用法：[Convert(typeof(DateTimeFormatter), nameof(DateTimeFormatter.Convert))]</para>
/// </summary>
public static class DateTimeFormatter
{
    /// <summary>
    /// 将 DateTime 格式化为日期字符串。
    /// </summary>
    /// <param name="fieldValue">源字段值（DateTime）</param>
    /// <returns>格式化后的日期字符串</returns>
    public static string Convert(DateTime fieldValue)
    {
        return fieldValue.ToString("yyyy-MM-dd");
    }
}

/// <summary>
/// 产品日期格式化转换器 — 将 DateTime 转换为 "yyyy-MM-dd" 格式字符串（T1 模板 M(TField)）。
/// <para>用法：[Convert(typeof(ProductDateTimeFormatter), nameof(ProductDateTimeFormatter.Convert))]</para>
/// </summary>
public static class ProductDateTimeFormatter
{
    /// <summary>
    /// 将 DateTime 格式化为日期字符串。
    /// </summary>
    /// <param name="fieldValue">源字段值（DateTime）</param>
    /// <returns>格式化后的日期字符串</returns>
    public static string Convert(DateTime fieldValue)
    {
        return fieldValue.ToString("yyyy-MM-dd");
    }
}

/// <summary>
/// 反向日期解析转换器 — 将 "yyyy-MM-dd" 字符串解析回 DateTime（DTO → Entity 反向路径，T1 模板）。
/// <para>用法：[ConvertBack(typeof(DateBackParser), nameof(DateBackParser.Convert))] — 与正向
/// [Convert(typeof(DateTimeFormatter), nameof(DateTimeFormatter.Convert))] 同属性并列。</para>
/// <para>方向语义：正向 [Convert] 反向默认不消费（NCMAP033）；反向需显式 [ConvertBack]。</para>
/// </summary>
public static class DateBackParser
{
    /// <summary>
    /// 将日期字符串解析为 DateTime。
    /// </summary>
    /// <param name="fieldValue">源字段值（日期字符串）</param>
    /// <returns>解析后的 DateTime</returns>
    public static DateTime Convert(string fieldValue)
    {
        return DateTime.ParseExact(fieldValue, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }
}
