using NCMapper.Tests.Models;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// [Convert] 自定义类型转换器测试 — 源生成器路径 + 运行时路径
/// </summary>
public class ConverterTests
{
    /// <summary>
    /// 源生成器路径：DateTime → string 转换器正确格式化
    /// </summary>
    [Fact]
    public void ConvertUsing_DateTime_To_String_SG()
    {
        var entity = new ConverterSourceEntity { CreateTime = new(2025, 3, 15, 10, 30, 0) };
        var dto = Mapper.Map<ConverterSourceEntity, ConverterDto>(entity!)!;

        Assert.Equal("2025-03-15", dto.CreateTime);
    }

    /// <summary>
    /// 源生成器路径：string → DateTime 转换器正确解析
    /// </summary>
    [Fact]
    public void ConvertUsing_String_To_DateTime_SG()
    {
        var entity = new ConverterSourceEntity { DateString = "2025-06-01" };
        var dto = Mapper.Map<ConverterSourceEntity, ConverterDto>(entity!)!;

        Assert.Equal(2025, dto.ParsedDate.Year);
        Assert.Equal(6, dto.ParsedDate.Month);
        Assert.Equal(1, dto.ParsedDate.Day);
    }

    /// <summary>
    /// 源生成器路径：int → string 转换器正确补零
    /// </summary>
    [Fact]
    public void ConvertUsing_Int_To_PaddedString_SG()
    {
        var entity = new ConverterSourceEntity { Code = 7 };
        var dto = Mapper.Map<ConverterSourceEntity, ConverterDto>(entity!)!;

        Assert.Equal("000007", dto.Code);
    }

    /// <summary>
    /// 源生成器路径：非 ConvertUsing 属性正常映射
    /// </summary>
    [Fact]
    public void ConvertUsing_NonConverter_Property_Normal()
    {
        var entity = new ConverterSourceEntity { Id = 99 };
        var dto = Mapper.Map<ConverterSourceEntity, ConverterDto>(entity!)!;

        Assert.Equal(99, dto.Id);
    }

    /// <summary>
    /// 源生成器路径：null 源对象返回 null
    /// </summary>
    [Fact]
    public void ConvertUsing_Null_Source_Returns_Null()
    {
        ConverterSourceEntity? entity = null;
        var dto = Mapper.Map<ConverterSourceEntity, ConverterDto>(entity!);

        Assert.Null(dto);
    }

    /// <summary>
    /// 全量映射：所有转换器属性 + 普通属性同时验证
    /// </summary>
    [Fact]
    public void ConvertUsing_All_Properties_Correct()
    {
        var entity = new ConverterSourceEntity
        {
            Id = 100,
            CreateTime = new(2025, 12, 31, 23, 59, 0),
            DateString = "2024-01-15",
            Code = 42
        };
        var dto = Mapper.Map<ConverterSourceEntity, ConverterDto>(entity!)!;

        Assert.Equal(100, dto.Id);
        Assert.Equal("2025-12-31", dto.CreateTime);
        Assert.Equal(2024, dto.ParsedDate.Year);
        Assert.Equal(1, dto.ParsedDate.Month);
        Assert.Equal(15, dto.ParsedDate.Day);
        Assert.Equal("000042", dto.Code);
    }

    // ================================================================
    // 运行时路径测试 — DTO 未标注 [MapFrom]，走 MapperBase 路径
    // ================================================================

    /// <summary>
    /// 运行时路径：反向（!isToDto）不消费 [Convert] — A-4
    /// <para>RuntimeConverterDto 未标注 [MapFrom]，isToDto=false（反向），[Convert] 降级类型转换路径
    /// （DateTime.ToString()），不再输出格式化值 "2025-07-04"</para>
    /// <para>迁移：DateTime→string 经 IConvertible 路径恒按
    /// InvariantCulture 输出——断言基准同步 Invariant 口径（业务语义不变：降级路径输出
    /// ToString 无格式化参数），实测全量唯一受影响用例（计数 1 ≤ 20，F5 未触发）</para>
    /// </summary>
    [Fact]
    public void ConvertUsing_Runtime_Reverse_NotConsumed()
    {
        var entity = new ConverterSourceEntity { CreateTime = new(2025, 7, 4, 8, 0, 0) };
        var dto = Mapper.Map<ConverterSourceEntity, RuntimeConverterDto>(entity!)!;

        Assert.Equal(entity.CreateTime.ToString(System.Globalization.CultureInfo.InvariantCulture), dto.CreateTime);
    }

    /// <summary>
    /// 运行时路径：反向（!isToDto）不消费 [Convert] — A-4
    /// <para>int → string 类型转换路径输出 "123"，不再补零 "000123"</para>
    /// </summary>
    [Fact]
    public void ConvertUsing_Runtime_Reverse_NotConsumed_Int()
    {
        var entity = new ConverterSourceEntity { Code = 123 };
        var dto = Mapper.Map<ConverterSourceEntity, RuntimeConverterDto>(entity!)!;

        Assert.Equal("123", dto.Code);
    }

    /// <summary>
    /// 运行时路径：非 ConvertUsing 属性正常映射（同类型直接赋值）
    /// </summary>
    [Fact]
    public void ConvertUsing_Runtime_NonConverter_Property_Normal()
    {
        var entity = new ConverterSourceEntity { Id = 555 };
        var dto = Mapper.Map<ConverterSourceEntity, RuntimeConverterDto>(entity!)!;

        Assert.Equal(555, dto.Id);
    }

    /// <summary>
    /// 运行时路径：null 源对象返回 null
    /// </summary>
    [Fact]
    public void ConvertUsing_Runtime_Null_Source_Returns_Null()
    {
        ConverterSourceEntity? entity = null;
        var dto = Mapper.Map<ConverterSourceEntity, RuntimeConverterDto>(entity!);

        Assert.Null(dto);
    }

    // ================================================================
    // T2: 多字段组合 + 条件映射测试
    // ================================================================

    /// <summary>
    /// 多字段组合 — FullName = FirstName + " " + LastName
    /// </summary>
    [Fact]
    public void ConvertUsing_MultiField_CombinesCorrectly()
    {
        var entity = new MultiFieldSourceEntity { FirstName = "张", LastName = "三" };
        var dto = Mapper.Map<MultiFieldSourceEntity, MultiFieldDto>(entity)!;

        Assert.Equal("张 三", dto.FullName);
    }

    /// <summary>
    /// 多字段数值计算 — TotalPrice = Price * Quantity
    /// </summary>
    [Fact]
    public void ConvertUsing_MultiField_NumericCalc()
    {
        var entity = new MultiFieldSourceEntity { Price = 29.99m, Quantity = 10 };
        var dto = Mapper.Map<MultiFieldSourceEntity, MultiFieldDto>(entity)!;

        Assert.Equal(299.9m, dto.TotalPrice);
    }

    /// <summary>
    /// 条件忽略 — 非管理员时 Salary 返回 default(0)
    /// </summary>
    [Fact]
    public void ConvertUsing_ConditionalIgnore_ReturnsDefault()
    {
        ConditionalSalaryConverter.IsAdmin = false;
        var entity = new MultiFieldSourceEntity { Salary = 8000m };
        var dto = Mapper.Map<MultiFieldSourceEntity, MultiFieldDto>(entity)!;

        Assert.Equal(0m, dto.Salary);
    }

    /// <summary>
    /// 条件脱敏 — 非管理员时 Phone 返回脱敏值
    /// </summary>
    [Fact]
    public void ConvertUsing_ConditionalMask_ReturnsMasked()
    {
        PhoneMaskConverter.IsAdmin = false;
        var entity = new MultiFieldSourceEntity { Phone = "13800000001" };
        var dto = Mapper.Map<MultiFieldSourceEntity, MultiFieldDto>(entity)!;

        Assert.Equal("138****0001", dto.Phone);
    }

    /// <summary>
    /// 无匹配源属性 — fieldValue 传 default，转换器不报错
    /// </summary>
    [Fact]
    public void ConvertUsing_NoSourceProperty_PassesDefault()
    {
        var entity = new MultiFieldSourceEntity { Id = 42 };
        var dto = Mapper.Map<MultiFieldSourceEntity, MultiFieldDto>(entity)!;

        Assert.Equal("Id=42", dto.ComputedField);
    }

    /// <summary>
    /// 嵌套对象访问 — 转换器通过 source.Info.Department 访问嵌套属性
    /// </summary>
    [Fact]
    public void ConvertUsing_NestedObject_AccessibleViaSource()
    {
        var entity = new MultiFieldSourceEntity { Info = new NestedInfo { Department = "Engineering" } };
        var dto = Mapper.Map<MultiFieldSourceEntity, MultiFieldDto>(entity)!;

        Assert.Equal("Engineering", dto.Department);
    }
}

/// <summary>
/// 运行时路径转换器测试 DTO — 未标注 [MapFrom]，强制走 MapperBase 路径
/// </summary>
public class RuntimeConverterDto
{
    public int Id { get; set; }

    /// <summary>
    /// DateTime → string（运行时表达式树路径）
    /// </summary>
    [Convert(typeof(DateTimeToStringConverter), nameof(DateTimeToStringConverter.Convert))]
    public string CreateTime { get; set; } = "";

    /// <summary>
    /// int → 补零字符串（运行时表达式树路径）
    /// </summary>
    [Convert(typeof(IntToPaddedStringConverter), nameof(IntToPaddedStringConverter.Convert))]
    public string Code { get; set; } = "";
}

#region P1-6: Converter 边界测试

/// <summary>
/// 返回 null 的转换器 — 用于测试 null 传播行为（T1 模板 M(TField)）
/// </summary>
public static class NullReturningConverter
{
    public static string? Convert(int fieldValue) => null;
}

[MapFrom(typeof(ConverterSourceEntity))]
public class NullReturnDto
{
    public int Id { get; set; }

    [Convert(typeof(NullReturningConverter), nameof(NullReturningConverter.Convert))]
    [MapProperty(typeof(ConverterSourceEntity), nameof(ConverterSourceEntity.Code))]
    public string? OptionalCode { get; set; }
}

/// <summary>
/// P1-6: Converter 边界测试 — null 传播、多转换器互不干扰
/// </summary>
public class ConverterBoundaryTests
{
    /// <summary>
    /// 转换器返回 null 时的 null 传播行为
    /// </summary>
    [Fact]
    public void ConvertUsing_ConverterReturnsNull_NullPropagated()
    {
        var entity = new ConverterSourceEntity { Id = 1, Code = 99 };
            var dto = NCMapper.Mapper.Map<ConverterSourceEntity, NullReturnDto>(entity)!;

            Assert.Null(dto.OptionalCode);
            Assert.Equal(1, dto.Id);
    }

    /// <summary>
    /// 同一 DTO 多个属性使用不同转换器 — 验证互不干扰
    /// </summary>
    [Fact]
    public void ConvertUsing_MultipleConverters_NoInterference()
    {
        var entity = new ConverterSourceEntity
        {
            Id = 7,
            DateString = "2025-01-15",
            Code = 42
        };

        var dto = NCMapper.Mapper.Map<ConverterSourceEntity, ConverterDto>(entity!)!;

            // 验证多个转换器各自正常工作
            Assert.Equal(7, dto.Id);
            Assert.Equal("2025-03-15", dto.CreateTime);  // DateTimeToString
            Assert.Equal(2025, dto.ParsedDate.Year);      // StringToDateTime
            Assert.Equal("000042", dto.Code);              // IntToPaddedString
    }
}

#endregion
