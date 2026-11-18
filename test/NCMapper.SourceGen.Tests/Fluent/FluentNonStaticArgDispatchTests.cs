using System.Linq;
using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// b) ② 族——**非恒定 <c>overrideIgnore</c> 实参 × 分派残留清扫后单分支**锁定
/// （消费 G2 修复面：旧 <c>IsBoolConstantArg</c> 布尔常量探测 + 旧形态分支已退役，
/// 1/2 参调用恒走新形态分派；G2 + driver 段）。
/// <para>缺陷态（修复前）：第二实参非布尔常量 → 旧分支接管 → **声明静默丢失**（无诊断、无产物）。
/// 修复后期望态 = F58 单报（实参不可静态求值）+ 默认值生效 + **声明照常生成**（产物含转换器接线）。</para>
/// <para>形态轴：局部变量 / 取反表达式 / 命名实参 / lambda 转换器 / 可解析纯路径 lambda 亚型 /
/// 块内站点 / ConvertBack 站点；恒定实参（true/false）作零 F58 对照。</para>
/// </summary>
public class FluentNonStaticArgDispatchTests
{
    // ═══════════════ 非恒定实参四形态（方法组转换器）═══════════════

    /// <summary>①局部变量实参：F58 单报 + 声明照常（转换器接线在位）。</summary>
    [Fact]
    public void Convert_LocalVarArg_F58Single_DeclarationStillEmitted()
    {
        var result = Generate(
            """
                    var flag = NsFlag.Get();
                    cfg.For<E, D>().Member(d => d.Tag).Convert<string>(NsConv.ToText, flag);
            """);
        AssertF58Single(result);
        AssertContainsConverterWiring(result);
    }

    /// <summary>②取反表达式实参：F58 单报 + 声明照常。</summary>
    [Fact]
    public void Convert_InvertedExpressionArg_F58Single_DeclarationStillEmitted()
    {
        var result = Generate(
            """
                    var flag = NsFlag.Get();
                    cfg.For<E, D>().Member(d => d.Tag).Convert<string>(NsConv.ToText, !flag);
            """);
        AssertF58Single(result);
        AssertContainsConverterWiring(result);
    }

    /// <summary>③命名实参形态（<c>overrideIgnore:</c>）同在检测面：F58 单报 + 声明照常。</summary>
    [Fact]
    public void Convert_NamedArg_F58Single_DeclarationStillEmitted()
    {
        var result = Generate(
            """
                    var flag = NsFlag.Get();
                    cfg.For<E, D>().Member(d => d.Tag).Convert<string>(NsConv.ToText, overrideIgnore: flag);
            """);
        AssertF58Single(result);
        AssertContainsConverterWiring(result);
    }

    /// <summary>④lambda 转换器 + 非恒定实参：F58 单报 + 声明照常（转录体 <c>Map_Cv_</c> 在位）。</summary>
    [Fact]
    public void Convert_LambdaConverterWithLocalArg_F58Single_TranscriptionEmitted()
    {
        var result = Generate(
            """
                    var flag = NsFlag.Get();
                    cfg.For<E, D>().Member(d => d.Tag).Convert((string v) => "x:" + v, flag);
            """);
        AssertF58Single(result);
        Assert.Contains("Map_Cv_", result.GeneratedCode);
    }

    /// <summary>⑤可解析纯路径 lambda 亚型（lambda 形似源路径，仍属 converter 实参位）：
    /// F58 单报 + 声明照常 + 不误判为 From 声明。</summary>
    [Fact]
    public void Convert_PurePathLikeLambdaArg_F58Single_NotTreatedAsFrom()
    {
        var result = Generate(
            """
                    var flag = NsFlag.Get();
                    cfg.For<E, D>().Member(d => d.Tag).Convert((E s) => s.Name, flag);
            """);
        AssertF58Single(result);
        // lambda 转换器 = 转录体 Map_Cv_ 接线（非方法组 NsConv.ToText）
        Assert.Contains("Map_Cv_", result.GeneratedCode);
        // 不误判为 From：源路径应为成员头同名（Tag），非 lambda 路径（Name）
        Assert.DoesNotContain("Tag = src.Name", result.GeneratedCode);
    }

    // ═══════════════ 恒定实参对照（零 F58——检测面不过度触发）═══════════════

    /// <summary>对照⑥恒定 true：零 F58 + 声明照常。</summary>
    [Fact]
    public void Convert_ConstantTrueArg_NoF58_DeclarationEmitted()
    {
        var result = Generate(
            """
                    cfg.For<E, D>().Member(d => d.Tag).Convert<string>(NsConv.ToText, true);
            """);
        AssertNoF58(result);
        AssertContainsConverterWiring(result);
    }

    /// <summary>对照⑦恒定 false（默认值等价）：零 F58 + 声明照常。</summary>
    [Fact]
    public void Convert_ConstantFalseArg_NoF58_DeclarationEmitted()
    {
        var result = Generate(
            """
                    cfg.For<E, D>().Member(d => d.Tag).Convert<string>(NsConv.ToText, false);
            """);
        AssertNoF58(result);
        AssertContainsConverterWiring(result);
    }

    /// <summary>对照⑧const 标识符（语义常量）：零 F58（<c>GetConstantValue</c> 有值放行口径）。</summary>
    [Fact]
    public void Convert_ConstIdentifierArg_NoF58()
    {
        var result = Generate(
            """
                    cfg.For<E, D>().Member(d => d.Tag).Convert<string>(NsConv.ToText, NsFlag.Always);
            """);
        AssertNoF58(result);
        AssertContainsConverterWiring(result);
    }

    // ═══════════════ 站点覆盖：块内 Convert / 主链 ConvertBack ═══════════════

    /// <summary>⑨块内站点（FromSource 块内 Convert）+ 非恒定实参：F58 单报 + 块外合法声明照常。</summary>
    [Fact]
    public void BlockConvert_LocalVarArg_F58Single_SiblingGenerated()
    {
        var result = Generate(
            """
                    var flag = NsFlag.Get();
                    cfg.For<E, D2>().Member(d => d.Name).From(s => s.Name);
                    cfg.For<E, D>().FromSource<Sub>(c => { c.Member(d => d.Tag).Convert<string>(NsConv.ToText, flag); });
            """);
        AssertF58Single(result);
        Assert.Contains("Map_E_To_D2", result.GeneratedCode);
    }

    /// <summary>⑩ConvertBack 站点 + 非恒定实参：F58 单报 + 反向声明照常（反向体含转换器接线）。</summary>
    [Fact]
    public void ConvertBack_LocalVarArg_F58Single_ReverseDeclarationEmitted()
    {
        var result = Generate(
            """
                    var flag = NsFlag.Get();
                    cfg.For<E, D>().Member(d => d.Tag).ConvertBack<string>(NsConv.ToText, flag);
            """);
        AssertF58Single(result);
        AssertContainsConverterWiring(result);
    }

    /// <summary>⑪ConvertBack 恒定对照：零 F58 + 反向声明照常。</summary>
    [Fact]
    public void ConvertBack_ConstantArg_NoF58()
    {
        var result = Generate(
            """
                    cfg.For<E, D>().Member(d => d.Tag).ConvertBack<string>(NsConv.ToText, true);
            """);
        AssertNoF58(result);
        AssertContainsConverterWiring(result);
    }

    // ─────────────────────────── 承载 fixture 与观测辅助 ───────────────────────────

    private static MapperGenerationResult Generate(string configureBody)
        => TestHelper.GenerateMapper($$"""
            using NCMapper;
            using NCMapper.Attributes;
            namespace T6Arg
            {
                public class E { public string Name { get; set; } = ""; public string Tag { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public string Tag { get; set; } = ""; }
                public class D2 { public string Name { get; set; } = ""; }
                public class Sub { public string Name { get; set; } = ""; public string Tag { get; set; } = ""; }
                public static class NsConv { public static string ToText(string v) => "t:" + v; }
                public static class NsFlag
                {
                    public const bool Always = true;
                    public static bool Get() => true;
                }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
            {{configureBody}}
                    }
                }
            }
            """);

    private static void AssertF58Single(MapperGenerationResult result)
    {
        Assert.Empty(result.Diagnostics.Where(d => d.Id == "CS8785").ToList());
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F58");
    }

    private static void AssertNoF58(MapperGenerationResult result)
    {
        Assert.Empty(result.Diagnostics.Where(d => d.Id == "CS8785").ToList());
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F58");
    }

    /// <summary>声明照常生成的边界敏感产物 = 转换器调用接线（缺陷态 = 声明丢失 → 零接线）。</summary>
    private static void AssertContainsConverterWiring(MapperGenerationResult result)
    {
        Assert.False(string.IsNullOrWhiteSpace(result.GeneratedCode), "先验锚失败：零生成代码");
        Assert.Contains("NsConv.ToText", result.GeneratedCode);
    }
}
