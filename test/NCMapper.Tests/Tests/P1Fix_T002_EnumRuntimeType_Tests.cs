// ============================================================================
// P1Fix_T002 — 批次 3 段1：枚举运行时类型判定
// 验收用例。
//
// 缺陷（006 审计 20260827-005）：TypeConvertHelper.ConvertValue 以
// typeof(TSource)（编译期泛型声明）判定枚举/值类型形态——装箱场景（object 装枚举）
// 误判走兜底抛 ConversionFailedUnsupported。
//
// 修复：形态判定源改 source.GetType()（运行时类型）。
// ============================================================================

using Xunit;

namespace NCMapper.Tests.Tests;

public class P1Fix_T002_EnumRuntimeType
{
    private enum B3Enum { A = 1, B = 2 }

    /// <summary>
    /// A1：ConvertValue&lt;object,MyEnum&gt;(1)——object 装箱 int → 枚举，
    /// 运行时类型判定后成功（原 typeof(object) 误判抛 ConversionFailedUnsupported）。
    /// </summary>
    [Fact]
    public void S09_装箱数值转枚举_object声明_运行时类型判定成功()
    {
        object boxed = 1;
        var result = TypeConvertHelper.ConvertValue<object, B3Enum>(boxed);

        Assert.Equal(B3Enum.A, result);
    }

    /// <summary>
    /// 装箱枚举转数值（object 声明）——运行时类型走枚举→数字路径。
    /// </summary>
    [Fact]
    public void S09_装箱枚举转数值_object声明_运行时类型判定成功()
    {
        object boxed = B3Enum.B;
        var result = TypeConvertHelper.ConvertValue<object, int>(boxed);

        Assert.Equal(2, result);
    }

    /// <summary>
    /// 装箱字符串→枚举（object 声明）——运行时 string 走 Enum.Parse 路径。
    /// </summary>
    [Fact]
    public void S09_装箱字符串转枚举_object声明_成功()
    {
        object boxed = "B";
        var result = TypeConvertHelper.ConvertValue<object, B3Enum>(boxed);

        Assert.Equal(B3Enum.B, result);
    }

    /// <summary>
    /// 反向锚点：精确泛型声明（TSource=B3Enum）既有行为不变。
    /// </summary>
    [Fact]
    public void S09_精确泛型声明_既有行为不变()
    {
        Assert.Equal(2, TypeConvertHelper.ConvertValue<B3Enum, int>(B3Enum.B));
        Assert.Equal("B", TypeConvertHelper.ConvertValue<B3Enum, string>(B3Enum.B));
        Assert.Equal(B3Enum.A, TypeConvertHelper.ConvertValue<int, B3Enum>(1));
    }

    /// <summary>
    /// 消费方场景：非泛型字典跨类型（值装箱 object 中转）→ ConvertValue<object,T>
    /// ——CollectionCopier.ConvertDictEntry 键/值转换路径（ConvertDictionary 运行时场景）。
    /// </summary>
    [Fact]
    public void S09_字典跨类型装箱中转_枚举值转换成功()
    {
        object boxedKey = 2;
        var key = TypeConvertHelper.ConvertValue<object, B3Enum>(boxedKey);

        Assert.Equal(B3Enum.B, key);
    }
}