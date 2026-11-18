using System.Reflection;
using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// A1 测试——init-only 枚举属性表达式化
/// <para>验收 1（评审 B3/S8）：双形态断言（switch 形态 + enum→enum 强转形态）+ 用例矩阵
/// （enum→enum / enum→string 含可空源 / string→enum 含 throw 分支 / 冲突退避），
/// 退避分支与语句版逐字一致（R5 口径：生成文本断言）。</para>
/// </summary>
public class InitOnlyEnumExpressionTests
{
    /// <summary>
    /// enum→enum（不同枚举类型）init-only 目标：强转形态 (TgtEnum)src.Prop
    /// <para>修复前：枚举不在表达式构建器中 → 静默保持默认值（丢字段）；修复后：强转表达式。</para>
    /// </summary>
    [Fact]
    public void EnumToEnum_InitOnly_GeneratesCastExpression()
    {
        var source = """
            using NCMapper.Attributes;
            public enum SourceStatus { Zero = 0, One = 1 }
            public enum TargetStatus { Zero = 0, One = 1 }
            public class SourceUser
            {
                public SourceStatus Status { get; set; }
            }
            [MapFrom(typeof(SourceUser))]
            public class TargetUserDto
            {
                public TargetStatus Status { get; init; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("Status = (TargetStatus)src.Status", result.GeneratedCode);
    }

    /// <summary>
    /// enum→string init-only 目标：switch 表达式形态（逐成员 arm + _ 兜底）
    /// </summary>
    [Fact]
    public void EnumToString_InitOnly_GeneratesSwitchExpression()
    {
        var source = """
            using NCMapper.Attributes;
            public enum Color { White = 0, Red = 1 }
            public class SourceColorHolder
            {
                public Color ColorName { get; set; }
            }
            [MapFrom(typeof(SourceColorHolder))]
            public class TargetColorDto
            {
                public string ColorName { get; init; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("ColorName = src.ColorName switch", result.GeneratedCode);
        Assert.Contains("Color.Red => \"Red\"", result.GeneratedCode);
    }

    /// <summary>
    /// Nullable&lt;Enum&gt;→string init-only 目标：_ 兜底 arm 用可空安全表达式（与语句版一致）
    /// </summary>
    [Fact]
    public void NullableEnumToString_InitOnly_GeneratesNullableFallback()
    {
        var source = """
            using NCMapper.Attributes;
            public enum Color { White = 0, Red = 1 }
            public class SourceNullableColorHolder
            {
                public Color? ColorName { get; set; }
            }
            [MapFrom(typeof(SourceNullableColorHolder))]
            public class TargetNullableColorDto
            {
                public string ColorName { get; init; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("_ => src.ColorName?.ToString() ?? \"\"", result.GeneratedCode);
    }

    /// <summary>
    /// string→enum init-only 目标：ToLowerInvariant switch + null 分支 + 非法值 throw MapException
    /// </summary>
    [Fact]
    public void StringToEnum_InitOnly_GeneratesSwitchWithThrow()
    {
        var source = """
            using NCMapper.Attributes;
            public enum Color { White = 0, Red = 1 }
            public class SourceStringColorHolder
            {
                public string ColorName { get; set; }
            }
            [MapFrom(typeof(SourceStringColorHolder))]
            public class TargetStringToEnumDto
            {
                public Color ColorName { get; init; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("ColorName = src.ColorName?.ToLowerInvariant() switch", result.GeneratedCode);
        Assert.Contains("null => default(Color)", result.GeneratedCode);
        Assert.Contains("_ => throw new NCMapper.MapException", result.GeneratedCode);
    }

    /// <summary>
    /// 别名冲突退避（enum→string）：生成 ToString() 单表达式（非 switch，与语句版逐字一致）
    /// </summary>
    [Fact]
    public void EnumToString_AliasCollision_GeneratesToStringFallback()
    {
        var source = """
            using NCMapper.Attributes;
            public enum AliasColor { A = 0, B = 0, C = 1 }
            public class SourceAliasColorHolder
            {
                public AliasColor ColorName { get; set; }
            }
            [MapFrom(typeof(SourceAliasColorHolder))]
            public class TargetAliasColorDto
            {
                public string ColorName { get; init; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("ColorName = src.ColorName.ToString()", result.GeneratedCode);
        Assert.False(result.GeneratedCode.Contains("ColorName = src.ColorName switch"),
            "别名冲突退避不应生成 switch 形态（与语句版一致走 ToString）");
    }

    /// <summary>
    /// 大小写冲突退避（string→enum，Red 与 RED）：精确匹配 switch（大小写敏感，无 ToLowerInvariant）
    /// </summary>
    [Fact]
    public void StringToEnum_NameCollision_GeneratesExactMatchSwitch()
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
        Assert.Contains("\"Red\" => CaseColor.Red", result.GeneratedCode);
        Assert.False(result.GeneratedCode.Contains("ToLowerInvariant()"),
            "大小写冲突退避不应走 ToLowerInvariant 路径（精确匹配保持大小写敏感）");
    }

    // ================================================================
    // B1-02 构造器枚举默认值渲染（审计批次二）
    // FormatExplicitDefault 兜底 Convert.ToString 对枚举输出底层整数
    // → new Dto(src.Id, 1) 中 int 不可隐式转枚举 = CS1503。修复：IsEnum 分支输出 (T)value。
    // ================================================================

    /// <summary>
    /// B1-02 主样例：record 目标 + 枚举构造器默认值（显式固定底层值 Active=1）→
    /// 生成 (EdfStatus)1 显式转换形态，生成码编译零错误（缺陷态裸 1 → CS1503）。
    /// </summary>
    [Fact]
    public void EnumCtorDefault_RendersExplicitCast_CompilesClean()
    {
        var source = """
            using NCMapper.Attributes;
            public enum EdfStatus { Active = 1, Inactive = 2 }
            public class EdfSrc { public int Id { get; set; } }
            [MapFrom(typeof(EdfSrc))]
            public record EdfDto(int Id, EdfStatus S = EdfStatus.Active);
            """;

        var result = TestHelper.GenerateMapper(source);

        // 修复形态：底层整数渲染 = (EdfStatus)1 显式转换（缺陷态裸 1 → 红）
        Assert.Contains("(EdfStatus)1", result.GeneratedCode);

        // 生成码编译零错误（缺陷态 CS1503）
        Assert.DoesNotContain("CS1503", EmitErrorIds(source));
    }

    /// <summary>
    /// B1-02 行为断言：Emit 加载后反射调用 Map 方法，S == EdfStatus.Active（默认值语义保持）。
    /// </summary>
    [Fact]
    public void EnumCtorDefault_MappedInstance_KeepsDefaultValue()
    {
        var source = """
            using NCMapper.Attributes;
            public enum EdfStatus { Active = 1, Inactive = 2 }
            public class EdfSrc { public int Id { get; set; } }
            [MapFrom(typeof(EdfSrc))]
            public record EdfDto(int Id, EdfStatus S = EdfStatus.Active);
            """;

        var asm = TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
        var mapperType = asm.GetTypes().Single(t => t.Name == "GeneratedMapper");
        var mapMethod = mapperType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "Map_EdfSrc_To_EdfDto");
        var srcType = asm.GetType("EdfSrc", throwOnError: true)!;
        var src = Activator.CreateInstance(srcType)!;
        srcType.GetProperty("Id")!.SetValue(src, 7);

        var dto = mapMethod.Invoke(null, new[] { src })!;
        Assert.Equal(7, dto.GetType().GetProperty("Id")!.GetValue(dto));
        // EdfStatus.Active 的底层值（fixture 内枚举，测试程序集不可见——显式固定底层值 1，任务书 A4）
        Assert.Equal(1, (int)dto.GetType().GetProperty("S")!.GetValue(dto)!);
    }

    /// <summary>
    /// B1-02 回归：非枚举默认值（string/int）渲染形态不变（CWE-94 转义与 InvariantCulture 既有语义）。
    /// </summary>
    [Fact]
    public void NonEnumCtorDefaults_UnchangedRendering()
    {
        var source = """
            using NCMapper.Attributes;
            public class EdfRegSrc { public int Id { get; set; } }
            [MapFrom(typeof(EdfRegSrc))]
            public record EdfRegDto(int Id, string Name = "n", int Level = 3);
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.Contains("\"n\"", result.GeneratedCode);
        Assert.Contains(", 3)", result.GeneratedCode);
        Assert.DoesNotContain("CS1503", EmitErrorIds(source));
    }

    /// <summary>Emit 编译错误 ID 提取（含生成码的 output 诊断——红绿对照 CS1503 用）。</summary>
    private static List<string> EmitErrorIds(string source)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(new NCMapperSourceGenerator().AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return output.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.Id)
            .ToList();
    }
}
