using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 建（原口径：空串 → 独立 "" arm 落 default）；A1-08
/// 运行时改抛后本套随契约反转——生成侧撤 "" arm，空串落 _ 分支抛。
/// <para>覆盖 4 象限：2 入口（语句版普通属性 / 表达式版 init-only 目标）× 2 路径（正常 / 名称冲突退化）× 2 维度（非可空 / 可空目标）。
/// 现行口径：null → default(枚举) / default(枚举?)；空串 → MapException（走 _ throw 分支，不再有独立 arm）；
/// 可空目标 null 分支发射类型化 default(枚举?)（评审 B-1 / B8）。</para>
/// <para>运行时对照见 NCMapper.Tests/EnumEmptyStringRuntimeSemanticsTests.cs（矩阵）。</para>
/// </summary>
public class EnumEmptyStringSemanticsTests
{
    // —— 语句版 · 正常路径（普通属性，TryGenerateEnumConversion）——

    [Fact]
    public void Statement_Normal_NonNullable_EmptyStringFallsToThrow()
    {
        var source = """
            using NCMapper.Attributes;
            public enum Color { White = 0, Red = 1, Blue = 2 }
            public class SourceColorHolder
            {
                public string ColorName { get; set; }
            }
            [MapFrom(typeof(SourceColorHolder))]
            public class TargetColorDto
            {
                public Color ColorName { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("ColorName = src.ColorName?.ToLowerInvariant() switch", result.GeneratedCode);
        Assert.Contains("    null => default(Color),", result.GeneratedCode);
        // 6 输入矩阵的 arm 落位：null → null arm；"" → 撤独立 arm 落 _ throw；"red"/"RED" → 小写标签；"1" → 数字串标签
        Assert.DoesNotContain("\"\" =>", result.GeneratedCode);
        Assert.Contains("    \"red\" => Color.Red,", result.GeneratedCode);
        Assert.Contains("    \"1\" => Color.Red,", result.GeneratedCode);
        Assert.Contains("_ => throw new NCMapper.MapException", result.GeneratedCode);
    }

    [Fact]
    public void Statement_Normal_NullableTarget_EmitsTypedNullableDefault()
    {
        var source = """
            using NCMapper.Attributes;
            public enum Color { White = 0, Red = 1, Blue = 2 }
            public class SourceColorHolder
            {
                public string ColorName { get; set; }
            }
            [MapFrom(typeof(SourceColorHolder))]
            public class TargetColorDto
            {
                public Color? ColorName { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        // 可空目标：null → default(Color?) = null（评审，类型化字面量，禁裸 null）；空串 → 抛（同 A1-08 矩阵）
        Assert.Contains("    null => default(Color?),", result.GeneratedCode);
        Assert.DoesNotContain("\"\" =>", result.GeneratedCode);
        Assert.DoesNotContain("null => null", result.GeneratedCode);
    }

    // —— 语句版 · 退化路径（成员名大小写冲突 Red/RED → 精确匹配 switch）——

    [Fact]
    public void Statement_Degraded_NonNullable_EmptyStringFallsToThrow()
    {
        var source = """
            using NCMapper.Attributes;
            public enum CaseColor { Red = 0, RED = 1 }
            public class SourceCaseColorHolder
            {
                public string ColorName { get; set; }
            }
            [MapFrom(typeof(SourceCaseColorHolder))]
            public class TargetCaseColorDto
            {
                public CaseColor ColorName { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        // 退化路径：精确匹配 switch（无 ToLowerInvariant），不补 "" arm——落 _ throw
        Assert.Contains("ColorName = src.ColorName switch", result.GeneratedCode);
        Assert.Contains("    null => default(CaseColor),", result.GeneratedCode);
        Assert.DoesNotContain("\"\" =>", result.GeneratedCode);
        Assert.Contains("    \"Red\" => CaseColor.Red,", result.GeneratedCode);
        Assert.Contains("_ => throw new NCMapper.MapException", result.GeneratedCode);
    }

    [Fact]
    public void Statement_Degraded_NullableTarget_EmitsTypedNullableDefault()
    {
        var source = """
            using NCMapper.Attributes;
            public enum CaseColor { Red = 0, RED = 1 }
            public class SourceCaseColorHolder
            {
                public string ColorName { get; set; }
            }
            [MapFrom(typeof(SourceCaseColorHolder))]
            public class TargetCaseColorDto
            {
                public CaseColor? ColorName { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("    null => default(CaseColor?),", result.GeneratedCode);
        Assert.DoesNotContain("\"\" =>", result.GeneratedCode);
        Assert.DoesNotContain("null => null", result.GeneratedCode);
    }

    // —— 表达式版 · 正常路径（init-only 目标，TryBuildEnumConversionExpr）——

    [Fact]
    public void Expression_InitOnly_Normal_NonNullable_EmptyStringFallsToThrow()
    {
        var source = """
            using NCMapper.Attributes;
            public enum Color { White = 0, Red = 1, Blue = 2 }
            public class SourceColorHolder
            {
                public string ColorName { get; set; }
            }
            [MapFrom(typeof(SourceColorHolder))]
            public class TargetColorDto
            {
                public Color ColorName { get; init; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("ColorName = src.ColorName?.ToLowerInvariant() switch", result.GeneratedCode);
        // 表达式版 arm 为 join(", ") 内联形态：仅 null arm + 标签 arm + throw
        Assert.Contains("null => default(Color)", result.GeneratedCode);
        Assert.DoesNotContain("\"\" =>", result.GeneratedCode);
        Assert.Contains("\"red\" => Color.Red", result.GeneratedCode);
        Assert.Contains("_ => throw new NCMapper.MapException", result.GeneratedCode);
    }

    [Fact]
    public void Expression_InitOnly_Normal_NullableTarget_EmitsTypedNullableDefault()
    {
        var source = """
            using NCMapper.Attributes;
            public enum Color { White = 0, Red = 1, Blue = 2 }
            public class SourceColorHolder
            {
                public string ColorName { get; set; }
            }
            [MapFrom(typeof(SourceColorHolder))]
            public class TargetColorDto
            {
                public Color? ColorName { get; init; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("null => default(Color?)", result.GeneratedCode);
        Assert.DoesNotContain("\"\" =>", result.GeneratedCode);
        Assert.DoesNotContain("null => null", result.GeneratedCode);
    }

    // —— 表达式版 · 退化路径（init-only 目标 + 名称冲突枚举）——

    [Fact]
    public void Expression_InitOnly_Degraded_NonNullable_EmptyStringFallsToThrow()
    {
        var source = """
            using NCMapper.Attributes;
            public enum CaseColor { Red = 0, RED = 1 }
            public class SourceCaseColorHolder
            {
                public string ColorName { get; set; }
            }
            [MapFrom(typeof(SourceCaseColorHolder))]
            public class TargetCaseColorDto
            {
                public CaseColor ColorName { get; init; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("ColorName = src.ColorName switch", result.GeneratedCode);
        Assert.Contains("null => default(CaseColor)", result.GeneratedCode);
        Assert.DoesNotContain("\"\" =>", result.GeneratedCode);
        Assert.Contains("\"Red\" => CaseColor.Red", result.GeneratedCode);
        Assert.DoesNotContain("ToLowerInvariant()", result.GeneratedCode);
    }

    [Fact]
    public void Expression_InitOnly_Degraded_NullableTarget_EmitsTypedNullableDefault()
    {
        var source = """
            using NCMapper.Attributes;
            public enum CaseColor { Red = 0, RED = 1 }
            public class SourceCaseColorHolder
            {
                public string ColorName { get; set; }
            }
            [MapFrom(typeof(SourceCaseColorHolder))]
            public class TargetCaseColorDto
            {
                public CaseColor? ColorName { get; init; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("null => default(CaseColor?)", result.GeneratedCode);
        Assert.DoesNotContain("\"\" =>", result.GeneratedCode);
        Assert.DoesNotContain("null => null", result.GeneratedCode);
    }
}