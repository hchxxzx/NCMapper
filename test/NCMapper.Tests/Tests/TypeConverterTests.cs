using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// TypeConverter 隐式转换测试 — 参数化版本（041 重构）
/// 将 29 个 [Fact] 合并为 [Theory]+[InlineData]，覆盖所有隐式转换场景
/// </summary>
public class TypeConverterTests
{
    // ================================================================
    // IsImplicitConvertible — 可转换 pair（true）
    // ================================================================

    [Theory]
    [InlineData(typeof(byte), typeof(short))]
    [InlineData(typeof(byte), typeof(ushort))]
    [InlineData(typeof(byte), typeof(int))]
    [InlineData(typeof(byte), typeof(uint))]
    [InlineData(typeof(byte), typeof(long))]
    [InlineData(typeof(byte), typeof(ulong))]
    [InlineData(typeof(byte), typeof(float))]
    [InlineData(typeof(byte), typeof(double))]
    [InlineData(typeof(byte), typeof(decimal))]
    [InlineData(typeof(sbyte), typeof(short))]
    [InlineData(typeof(sbyte), typeof(int))]
    [InlineData(typeof(sbyte), typeof(long))]
    [InlineData(typeof(sbyte), typeof(float))]
    [InlineData(typeof(sbyte), typeof(double))]
    [InlineData(typeof(sbyte), typeof(decimal))]
    [InlineData(typeof(short), typeof(int))]
    [InlineData(typeof(short), typeof(long))]
    [InlineData(typeof(short), typeof(float))]
    [InlineData(typeof(short), typeof(double))]
    [InlineData(typeof(short), typeof(decimal))]
    [InlineData(typeof(ushort), typeof(int))]
    [InlineData(typeof(ushort), typeof(uint))]
    [InlineData(typeof(ushort), typeof(long))]
    [InlineData(typeof(ushort), typeof(ulong))]
    [InlineData(typeof(ushort), typeof(float))]
    [InlineData(typeof(ushort), typeof(double))]
    [InlineData(typeof(ushort), typeof(decimal))]
    [InlineData(typeof(int), typeof(long))]
    [InlineData(typeof(int), typeof(float))]
    [InlineData(typeof(int), typeof(double))]
    [InlineData(typeof(int), typeof(decimal))]
    [InlineData(typeof(uint), typeof(long))]
    [InlineData(typeof(uint), typeof(ulong))]
    [InlineData(typeof(uint), typeof(float))]
    [InlineData(typeof(uint), typeof(double))]
    [InlineData(typeof(uint), typeof(decimal))]
    [InlineData(typeof(long), typeof(float))]
    [InlineData(typeof(long), typeof(double))]
    [InlineData(typeof(long), typeof(decimal))]
    [InlineData(typeof(ulong), typeof(float))]
    [InlineData(typeof(ulong), typeof(double))]
    [InlineData(typeof(ulong), typeof(decimal))]
    [InlineData(typeof(char), typeof(ushort))]
    [InlineData(typeof(char), typeof(int))]
    [InlineData(typeof(char), typeof(uint))]
    [InlineData(typeof(char), typeof(long))]
    [InlineData(typeof(char), typeof(ulong))]
    [InlineData(typeof(char), typeof(float))]
    [InlineData(typeof(char), typeof(double))]
    [InlineData(typeof(char), typeof(decimal))]
    [InlineData(typeof(float), typeof(double))]
    [InlineData(typeof(int), typeof(int))]       // 同类型
    /// <summary>
    /// 验证存在隐式数值转换的类型对返回 true
    /// </summary>
    /// <param name="src">源类型</param>
    /// <param name="tgt">目标类型</param>
    public void IsImplicitConvertible_True(Type src, Type tgt)
        => Assert.True(TypeConverter.IsImplicitConvertible(src, tgt));

    // ================================================================
    // IsImplicitConvertible — 不可转换 pair（false）
    // ================================================================

    [Theory]
    [InlineData(typeof(long), typeof(int))]
    [InlineData(typeof(double), typeof(float))]
    [InlineData(typeof(int), typeof(byte))]
    [InlineData(typeof(float), typeof(int))]
    [InlineData(typeof(decimal), typeof(long))]
    [InlineData(typeof(decimal), typeof(double))]
    [InlineData(typeof(double), typeof(decimal))]
    [InlineData(typeof(float), typeof(decimal))]
    [InlineData(typeof(int), typeof(uint))]
    [InlineData(typeof(uint), typeof(int))]
    [InlineData(typeof(int), typeof(ulong))]
    [InlineData(typeof(string), typeof(string))]   // 引用类型
    /// <summary>
    /// 验证不存在隐式数值转换的类型对返回 false
    /// </summary>
    /// <param name="src">源类型</param>
    /// <param name="tgt">目标类型</param>
    public void IsImplicitConvertible_False(Type src, Type tgt)
        => Assert.False(TypeConverter.IsImplicitConvertible(src, tgt));

    /// <summary>
    /// 验证枚举到 int 不属于隐式转换
    /// </summary>
    [Fact]
    public void IsImplicitConvertible_Enum_To_Int_False()
        => Assert.False(TypeConverter.IsImplicitConvertible(typeof(Models.OrderStatus), typeof(int)));

    // ================================================================
    // IsNullableType
    // ================================================================

    [Theory]
    [InlineData(typeof(int?), true)]
    [InlineData(typeof(DateTime?), true)]
    [InlineData(typeof(Guid?), true)]
    [InlineData(typeof(int), false)]
    [InlineData(typeof(string), false)]
    [InlineData(typeof(DateTime), false)]
    /// <summary>
    /// 验证可空类型判断
    /// </summary>
    /// <param name="type">待检查的类型</param>
    /// <param name="expected">期望是否为可空类型</param>
    public void IsNullableType(Type type, bool expected)
        => Assert.Equal(expected, TypeConverter.IsNullableType(type));

    // ================================================================
    // GetUnderlyingType
    // ================================================================

    [Theory]
    [InlineData(typeof(int?), typeof(int))]
    [InlineData(typeof(DateTime?), typeof(DateTime))]
    [InlineData(typeof(Guid?), typeof(Guid))]
    [InlineData(typeof(int), typeof(int))]
    [InlineData(typeof(string), typeof(string))]
    /// <summary>
    /// 验证获取可空类型的底层类型
    /// </summary>
    /// <param name="type">待检查的类型</param>
    /// <param name="expected">期望的底层类型</param>
    public void GetUnderlyingType(Type type, Type expected)
        => Assert.Equal(expected, Nullable.GetUnderlyingType(type) ?? type); // 遗留修复：TypeConverter.GetUnderlyingType 已删（零调用清理），语义等价

    // ================================================================
    // TypeConvertHelper.ConvertValue tests — targeting L24, L58-62, L76, L80-84
    // ================================================================

    /// <summary>
    /// Convert float to int — exercises IConvertible path at L30-34.
    /// Verifies float 3.7f converts to int via ChangeType (rounds to 4).
    /// Kills mutant: sourceType != typeof(float) → == at L80.
    /// </summary>
    [Fact]
    public void Convert_Float_To_Int()
    {
        var result = TypeConvertHelper.ConvertValue<float, int>(3.7f);
        Assert.Equal(4, result); // Convert.ChangeType rounds, not truncates
    }

    /// <summary>
    /// Convert float to double — exercises implicit conversion via IConvertible.
    /// Verifies float 2.5f converts to double 2.5.
    /// Kills mutant: sourceType.IsValueType || sourceType != typeof(float) → && at L80.
    /// </summary>
    [Fact]
    public void Convert_Float_To_Double()
    {
        var result = TypeConvertHelper.ConvertValue<float, double>(2.5f);
        Assert.Equal(2.5, result);
    }

    /// <summary>
    /// Convert nullable int (with value) to int — exercises nullable unwrapping at L24.
    /// Verifies int? with value 42 converts to int 42.
    /// Kills mutant: Nullable.GetUnderlyingType(targetType) ?? targetType (L24).
    /// </summary>
    [Fact]
    public void Convert_NullableInt_To_Int()
    {
        int? source = 42;
        var result = TypeConvertHelper.ConvertValue<int?, int>(source);
        Assert.Equal(42, result);
    }

    /// <summary>
    /// Convert string to string — exercises direct type match at L27.
    /// Verifies string passes through unchanged.
    /// Kills mutant: targetType != typeof(string) at L58.
    /// </summary>
    [Fact]
    public void Convert_String_To_String_Direct()
    {
        var result = TypeConvertHelper.ConvertValue<string, string>("hello");
        Assert.Equal("hello", result);
    }

    /// <summary>
    /// Convert int to float — exercises IConvertible path.
    /// Verifies int 10 converts to float 10.0f.
    /// Additional coverage for value type conversions.
    /// </summary>
    [Fact]
    public void Convert_Int_To_Float()
    {
        var result = TypeConvertHelper.ConvertValue<int, float>(10);
        Assert.Equal(10.0f, result);
    }

    /// <summary>
    /// Convert enum to string — exercises L58-59 enum→string path.
    /// Verifies OrderStatus.Shipped converts to "Shipped".
    /// Kills mutant: targetType == typeof(string) condition at L58.
    /// </summary>
    [Fact]
    public void Convert_Enum_To_String()
    {
        var result = TypeConvertHelper.ConvertValue<Models.OrderStatus, string>(Models.OrderStatus.Shipped);
        Assert.Equal("Shipped", result);
    }

    /// <summary>
    /// Convert string to enum — exercises L73-76 string→enum path.
    /// Verifies "High" converts to Priority.High.
    /// Kills mutant: false → true at L76 (empty string check).
    /// </summary>
    [Fact]
    public void Convert_String_To_Enum()
    {
        var result = TypeConvertHelper.ConvertValue<string, Models.Priority>("High");
        Assert.Equal(Models.Priority.High, result);
    }

    /// <summary>
    /// Convert null source returns default — exercises L20 null guard.
    /// Verifies null int? returns 0 (default for int).
    /// Kills mutant: null guard removed.
    /// </summary>
    [Fact]
    public void Convert_Null_Source_Returns_Default()
    {
        int? source = null;
        var result = TypeConvertHelper.ConvertValue<int?, int>(source);
        Assert.Equal(0, result);
    }
}
