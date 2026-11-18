using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// X-1/X-2 分叉收敛与诊断基线测试（W2-3 ③，TG-C/TG-D）。
/// <para>X-1/X-2 收敛 = 预期行为变更（任务书 D1.3 清单）：TypeRef 版判据链向符号版权威收敛后，
/// float/double→enum、string→非白名单值类型的生成侧判定由 true 变 false——新增跳过 + NCMAP027。</para>
/// <para>TG-C 诊断基线：诊断链差异分支（任一侧 enum/string/Guid 等宽分支）保持私有——
/// NCMAP002 在收紧敏感场景不触发（D1.4 基线快照），生成侧跳过经 NCMAP027 报告。</para>
/// </summary>
public class TypeCompatConvergenceTests
{
    private static bool HasDiagnostic(MapperGenerationResult result, string id)
        => result.Diagnostics.Any(d => d.Id == id);

    // ============ X-1 收敛（D1.3 #1）：float/double→enum 新增跳过 + NCMAP027 ============

    [Theory]
    [InlineData("float")]
    [InlineData("double")]
    public void X1_FloatDoubleToEnum_ConvergedToSkip(string numericType)
    {
        var source = $$"""
            using NCMapper.Attributes;
            namespace Consumer;
            public enum Color { Red, Green }
            public class Entity { public {{numericType}} Status { get; set; } }
            [MapFrom(typeof(Entity))]
            public class Dto { public Color Status { get; set; } }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.True(HasDiagnostic(result, "NCMAP027"),
            $"{numericType}→enum 收敛后应新增跳过 + NCMAP027（符号版权威：float/double 排除）");
        // TG-C：诊断链任一侧 enum 宽分支保持私有——NCMAP002 不触发（D1.4 基线）
        Assert.False(HasDiagnostic(result, "NCMAP002"));
    }

    // ============ X-2 收敛（D1.3 #2-4）：string→非白名单值类型新增跳过 + NCMAP027 ============

    [Theory]
    [InlineData("System.TimeSpan")]
    [InlineData("System.DateTimeOffset")]
    public void X2_StringToNonWhitelistValueType_ConvergedToSkip(string targetType)
    {
        var source = $$"""
            using System;
            using NCMapper.Attributes;
            namespace Consumer;
            public class Entity { public string Value { get; set; } = ""; }
            [MapFrom(typeof(Entity))]
            public class Dto { public {{targetType}} Value { get; set; } }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.True(HasDiagnostic(result, "NCMAP027"),
            $"string→{targetType} 收敛后应新增跳过 + NCMAP027（符号版权威：白名单外值类型）");
        Assert.False(HasDiagnostic(result, "NCMAP002"));
    }

    [Fact]
    public void X2_StringToCustomStruct_ConvergedToSkip()
    {
        var source = """
            using NCMapper.Attributes;
            namespace Consumer;
            public struct CustomStruct { public int X; }
            public class Entity { public string Value { get; set; } = ""; }
            [MapFrom(typeof(Entity))]
            public class Dto { public CustomStruct Value { get; set; } }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.True(HasDiagnostic(result, "NCMAP027"),
            "string→自定义 struct 收敛后应新增跳过 + NCMAP027（符号版权威：白名单外值类型）");
        Assert.False(HasDiagnostic(result, "NCMAP002"));
    }

    // ============ TG-C 诊断基线：收紧敏感场景 NCMAP002 不触发 + 生成侧 NCMAP027 ============

    /// <summary>Guid↔非 string（D1.4 基线：诊断链 :1134 Guid 任一侧宽分支 → NCMAP002 静默；主链跳过 + NCMAP027）</summary>
    [Fact]
    public void TGC_GuidToNonString_NoNCMAP002_SkippedWithNCMAP027()
    {
        var source = """
            using System;
            using NCMapper.Attributes;
            namespace Consumer;
            public class Entity { public Guid Value { get; set; } }
            [MapFrom(typeof(Entity))]
            public class Dto { public int Value { get; set; } }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.False(HasDiagnostic(result, "NCMAP002"), "诊断链 Guid 任一侧宽分支保持私有——NCMAP002 不触发（D1.4 基线）");
        Assert.True(HasDiagnostic(result, "NCMAP027"), "主链 Guid→int 完全不相容 → 跳过 + NCMAP027");
    }

    /// <summary>enum↔非数值非 string（D1.4 基线：诊断链 :1131 enum 任一侧宽分支 → NCMAP002 静默；主链跳过 + NCMAP027）</summary>
    [Fact]
    public void TGC_EnumToNonNumeric_NoNCMAP002_SkippedWithNCMAP027()
    {
        var source = """
            using System.IO;
            using NCMapper.Attributes;
            namespace Consumer;
            public enum Color { Red, Green }
            public class Entity { public Color Status { get; set; } }
            [MapFrom(typeof(Entity))]
            public class Dto { public Stream? Status { get; set; } }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.False(HasDiagnostic(result, "NCMAP002"), "诊断链 enum 任一侧宽分支保持私有——NCMAP002 不触发（D1.4 基线）");
        Assert.True(HasDiagnostic(result, "NCMAP027"), "主链 enum→Stream 完全不相容 → 跳过 + NCMAP027");
    }

    /// <summary>string↔非 IConvertible（D1.4 基线：诊断链 :1133 string 任一侧宽分支 → NCMAP002 静默；主链跳过 + NCMAP027）</summary>
    [Fact]
    public void TGC_StringToNonConvertible_NoNCMAP002_SkippedWithNCMAP027()
    {
        var source = """
            using System.IO;
            using NCMapper.Attributes;
            namespace Consumer;
            public class Entity { public string Value { get; set; } = ""; }
            [MapFrom(typeof(Entity))]
            public class Dto { public Stream? Value { get; set; } }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.False(HasDiagnostic(result, "NCMAP002"), "诊断链 string 任一侧宽分支保持私有——NCMAP002 不触发（D1.4 基线）");
        Assert.True(HasDiagnostic(result, "NCMAP027"), "主链 string→Stream 完全不相容 → 跳过 + NCMAP027");
    }

    // ============ 步骤 0 兜底（TG-D Error 态，C02 回归根因——Error/null 不参与跳过） ============

    /// <summary>error 类型符号 → 步骤 0 兜底 true（C02 回归根因：保持 fallback 行为）</summary>
    [Fact]
    public void ErrorTypeSymbol_Step0_Fallback_True()
    {
        var compilation = TestHelper.BuildCompilation("class Marker { }");
        var error = compilation.CreateErrorTypeSymbol(null, "MissingType", 0);
        var int32 = compilation.GetTypeByMetadataName("System.Int32");
        Assert.NotNull(int32);

        Assert.True(TypeHelpers.HasMappingPath(error, int32!));
        Assert.True(TypeHelpers.HasMappingPath(int32!, error));
    }

    /// <summary>TypeRef null 输入 → 步骤 0 兜底 true（镜像原 TypeRefHelpers.HasMappingPath :245 语义）</summary>
    [Fact]
    public void NullTypeRef_Step0_Fallback_True()
    {
        var compilation = TestHelper.BuildCompilation("class Marker { }");
        var int32 = compilation.GetTypeByMetadataName("System.Int32");
        Assert.NotNull(int32);
        var intRef = MappingExtractor.TypeRefFromSymbol(int32!);

        Assert.True(TypeRefHelpers.HasMappingPath(null, intRef));
        Assert.True(TypeRefHelpers.HasMappingPath(intRef, null));
    }
}
