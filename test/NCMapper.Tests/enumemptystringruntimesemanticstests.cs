using NCMapper;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// string→enum 运行时 ConvertValue 对照矩阵（对齐准星）
/// <para>断言运行时 TypeConvertHelper.ConvertValue 的 6 输入 × 2 维度行为：
/// 维度1（非可空）：null → default(0)；名称/数值/大写 → 值；空串/非法串 → MapException
/// 维度2（可空）：null → null；名称/数值/大写 → 值；空串/非法串 → MapException</para>
/// <para>空串格口径：A1-08原"空串 → default/null"按显式抛口径
/// 订正为 MapException（Enum.Parse ArgumentException → 既有 catch 统一转换）。</para>
/// <para>生成路径侧断言见 NCMapper.SourceGen.Tests/EnumEmptyStringSemanticsTests.cs。</para>
/// </summary>
public class EnumEmptyStringRuntimeSemanticsTests
{
    public enum LogLevel
    {
        Debug = 10,
        Information = 20,
        Warning = 30,
        Error = 40,
        Fatal = 50
    }

    // ——维度1：非可空目标（维度1 矩阵）——

    [Theory]
    [InlineData(null, 0)]                 // null → default(0)（25 前置 null 语义保持）
    [InlineData("debug", 10)]             // 名称串（小写）
    [InlineData("10", 10)]                // 数值串
    [InlineData("DEBUG", 10)]             // 大写（ignoreCase:true）
    public void ConvertValue_NonNullable_MatchesSpecMatrix(string? input, int expected)
    {
        var result = TypeConvertHelper.ConvertValue<string, LogLevel>(input!);
        Assert.Equal((LogLevel)expected, result);
    }

    [Fact]
    public void ConvertValue_NonNullable_InvalidString_ThrowsMapException()
    {
        var ex = Assert.Throws<MapException>(() => TypeConvertHelper.ConvertValue<string, LogLevel>("xk3j9"));
        // 文化中立：断言类型名（中英文消息均含），不断言消息语言
        Assert.Contains("String", ex.Message);
        Assert.Contains("LogLevel", ex.Message);
    }

    /// <summary>
    /// 空串 → MapException（A1-08，原 :77 IsNullOrEmpty 特判
    /// 静默 default 按显式抛口径订正——矩阵空串格随契约反转，null 格保持）
    /// </summary>
    [Fact]
    public void ConvertValue_NonNullable_EmptyString_ThrowsMapException()
    {
        var ex = Assert.Throws<MapException>(() => TypeConvertHelper.ConvertValue<string, LogLevel>(""));
        Assert.Contains("LogLevel", ex.Message);
    }

    // ——维度2：可空目标（维度2 矩阵）——

    [Theory]
    [InlineData(null)]                    // null → null（default(LogLevel?)，:25 前置语义保持）
    public void ConvertValue_Nullable_Null_GivesNull(string? input)
    {
        var result = TypeConvertHelper.ConvertValue<string, LogLevel?>(input!);
        Assert.Null(result);
    }

    /// <summary>空串（可空目标）→ MapException（A1-08 矩阵格随契约反转）</summary>
    [Fact]
    public void ConvertValue_Nullable_EmptyString_ThrowsMapException()
    {
        var ex = Assert.Throws<MapException>(() => TypeConvertHelper.ConvertValue<string, LogLevel?>(""));
        Assert.Contains("Nullable`1", ex.Message);
    }

    [Theory]
    [InlineData("debug", 10)]
    [InlineData("10", 10)]
    [InlineData("DEBUG", 10)]
    public void ConvertValue_Nullable_ValidStrings_GiveValue(string input, int expected)
    {
        var result = TypeConvertHelper.ConvertValue<string, LogLevel?>(input);
        Assert.Equal((LogLevel)expected, result);
    }

    [Fact]
    public void ConvertValue_Nullable_InvalidString_ThrowsMapException()
    {
        var ex = Assert.Throws<MapException>(() => TypeConvertHelper.ConvertValue<string, LogLevel?>("xk3j9"));
        // 文化中立：断言类型名（中英文消息均含），不断言消息语言
        // 可空目标类型名 = Nullable`1（typeof(LogLevel?).Name）
        Assert.Contains("String", ex.Message);
        Assert.Contains("Nullable`1", ex.Message);
    }
}
