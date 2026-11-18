using NCMapper.Attributes;

namespace NCMapper.Tests.Models;

// ================================================================
// 转换器测试模型（静态方法引用形态，任务书 20260901-001）
// ================================================================

/// <summary>
/// DateTime → string 转换器 — 格式化为 yyyy-MM-dd（T1 模板 M(TField)）
/// </summary>
public static class DateTimeToStringConverter
{
    public static string Convert(DateTime fieldValue) => fieldValue.ToString("yyyy-MM-dd");
}

/// <summary>
/// string → DateTime 转换器 — 解析 ISO 日期（T1 模板 M(TField)）
/// </summary>
public static class StringToDateTimeConverter
{
    public static DateTime Convert(string fieldValue) => DateTime.Parse(fieldValue);
}

/// <summary>
/// int → string 转换器 — 数字前补零到 6 位（T1 模板 M(TField)）
/// </summary>
public static class IntToPaddedStringConverter
{
    public static string Convert(int fieldValue) => fieldValue.ToString("D6");
}

/// <summary>
/// 转换器测试源实体
/// </summary>
public class ConverterSourceEntity
{
    public int Id { get; set; } = 42;
    public DateTime CreateTime { get; set; } = new(2025, 3, 15, 10, 30, 0);
    public string DateString { get; set; } = "2025-06-01";
    public int Code { get; set; } = 7;
}

/// <summary>
/// 转换器测试 DTO — 使用 [Convert] 静态方法引用标注
/// </summary>
[MapFrom(typeof(ConverterSourceEntity))]
public class ConverterDto
{
    public int Id { get; set; }

    [Convert(typeof(DateTimeToStringConverter), nameof(DateTimeToStringConverter.Convert))]
    public string CreateTime { get; set; } = "";

    [Convert(typeof(StringToDateTimeConverter), nameof(StringToDateTimeConverter.Convert))]
    [MapProperty(typeof(ConverterSourceEntity), "DateString")]
    public DateTime ParsedDate { get; set; }

    [Convert(typeof(IntToPaddedStringConverter), nameof(IntToPaddedStringConverter.Convert))]
    public string Code { get; set; } = "";
}

// ================================================================
// T2: 多字段组合 + 条件映射测试模型
// ================================================================

/// <summary>
/// 多字段组合测试源实体
/// </summary>
public class MultiFieldSourceEntity
{
    public int Id { get; set; } = 1;
    public string FirstName { get; set; } = "张";
    public string LastName { get; set; } = "三";
    public decimal Price { get; set; } = 29.99m;
    public int Quantity { get; set; } = 10;
    public string Phone { get; set; } = "13800000001";
    public decimal Salary { get; set; } = 8000m;
    public NestedInfo? Info { get; set; } = new();
}

/// <summary>
/// 嵌套信息对象 — 用于嵌套对象访问测试
/// </summary>
public class NestedInfo
{
    public string Department { get; set; } = "Engineering";
}

/// <summary>
/// 多字段组合转换器 — FullName = FirstName + " " + LastName（T2 模板 M(TSource)）
/// </summary>
public static class FullNameCalculator
{
    public static string Convert(MultiFieldSourceEntity source)
        => $"{source.FirstName} {source.LastName}";
}

/// <summary>
/// 多字段数值计算转换器 — TotalPrice = Price * Quantity（T2 模板 M(TSource)）
/// </summary>
public static class TotalPriceCalculator
{
    public static decimal Convert(MultiFieldSourceEntity source)
        => source.Price * source.Quantity;
}

/// <summary>
/// 条件忽略转换器 — 非管理员时返回 default（模拟条件映射，T1 模板 M(TField)）
/// </summary>
public static class ConditionalSalaryConverter
{
    public static bool IsAdmin { get; set; } = false;

    public static decimal Convert(decimal fieldValue)
        => IsAdmin ? fieldValue : default;
}

/// <summary>
/// 条件脱敏转换器 — 非管理员时返回脱敏手机号（T1 模板 M(TField)）
/// </summary>
public static class PhoneMaskConverter
{
    public static bool IsAdmin { get; set; } = false;

    public static string Convert(string fieldValue)
        => IsAdmin ? fieldValue : MaskPhone(fieldValue);

    private static string MaskPhone(string phone)
        => phone.Length >= 7 ? $"{phone[..3]}****{phone[^4..]}" : phone;
}

/// <summary>
/// 无匹配源属性转换器 — 无源模板直接读 source（T2 模板 M(TSource)）
/// </summary>
public static class NoSourceFieldConverter
{
    public static string Convert(MultiFieldSourceEntity source)
        => $"Id={source.Id}";
}

/// <summary>
/// 嵌套对象访问转换器 — 通过 source.Info.Department 访问嵌套属性（T2 模板 M(TSource)）
/// </summary>
public static class NestedDepartmentConverter
{
    public static string Convert(MultiFieldSourceEntity source)
        => source.Info?.Department ?? "";
}

/// <summary>
/// 多字段组合测试 DTO
/// </summary>
[MapFrom(typeof(MultiFieldSourceEntity))]
public class MultiFieldDto
{
    public int Id { get; set; }

    [Convert(typeof(FullNameCalculator), nameof(FullNameCalculator.Convert))]
    [MapProperty(typeof(MultiFieldSourceEntity), nameof(MultiFieldSourceEntity.FirstName))]
    public string FullName { get; set; } = "";

    [Convert(typeof(TotalPriceCalculator), nameof(TotalPriceCalculator.Convert))]
    [MapProperty(typeof(MultiFieldSourceEntity), nameof(MultiFieldSourceEntity.Price))]
    public decimal TotalPrice { get; set; }

    [Convert(typeof(ConditionalSalaryConverter), nameof(ConditionalSalaryConverter.Convert))]
    [MapProperty(typeof(MultiFieldSourceEntity), nameof(MultiFieldSourceEntity.Salary))]
    public decimal Salary { get; set; }

    [Convert(typeof(PhoneMaskConverter), nameof(PhoneMaskConverter.Convert))]
    [MapProperty(typeof(MultiFieldSourceEntity), nameof(MultiFieldSourceEntity.Phone))]
    public string Phone { get; set; } = "";

    [Convert(typeof(NoSourceFieldConverter), nameof(NoSourceFieldConverter.Convert))]
    public string ComputedField { get; set; } = "";

    [Convert(typeof(NestedDepartmentConverter), nameof(NestedDepartmentConverter.Convert))]
    public string Department { get; set; } = "";
}
