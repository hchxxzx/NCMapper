using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// Fluent 通道边界拒绝测试（spike 06/07 组移植 + 静默跳过深化补充）。
/// <para>覆盖：动态配置黑名单（难点⑥ F08 全 8 场景）/ lambda 边界（难点⑦ F05/F06/F07）/
/// D-2 静默跳过场景（链结构错误不产快照不产诊断——编译器已报）。</para>
/// </summary>
public class FluentBoundaryRejectionTests
{
    // ── 06 动态配置黑名单（F08）────────────────────────────────────────

    /// <summary>spike 06-1：if 控制流包裹 Fluent 调用 → F08 拒绝</summary>
    [Fact]
    public void IfStatement_WrappingFluentCall_Rejected()
    {
        var source = """
            using NCMapper;
            namespace Rj1
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        if (true) cfg.For<U, D>().Member(d => d.N).From(s => s.N);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F08");
        // 被拒语句的链不物化（不生成 U→D 方法）
        Assert.DoesNotContain("Map_U_To_D(", result.GeneratedCode);
    }

    /// <summary>spike 06-2：foreach 循环包裹 → F08</summary>
    [Fact]
    public void ForEachLoop_WrappingFluentCall_Rejected()
    {
        var source = """
            using NCMapper;
            namespace Rj2
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        foreach (var x in new[] { 1 }) cfg.For<U, D>().Member(d => d.N).From(s => s.N);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F08");
    }

    /// <summary>spike 06-3：while 循环包裹 → F08</summary>
    [Fact]
    public void WhileLoop_WrappingFluentCall_Rejected()
    {
        var source = """
            using NCMapper;
            namespace Rj3
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        while (false) cfg.For<U, D>().Member(d => d.N).From(s => s.N);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F08");
    }

    /// <summary>spike 06-4：switch 语句包裹 → F08</summary>
    [Fact]
    public void SwitchStatement_WrappingFluentCall_Rejected()
    {
        var source = """
            using NCMapper;
            namespace Rj4
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        switch (1)
                        {
                            case 1: cfg.For<U, D>().Member(d => d.N).From(s => s.N); break;
                        }
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F08");
    }

    /// <summary>spike 06-5：局部变量声明含三元 → F08（三元黑名单）</summary>
    [Fact]
    public void TernaryExpression_WrappingFluentCall_Rejected()
    {
        var source = """
            using NCMapper;
            namespace Rj5
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        var x = true ? cfg.For<U, D>().Member(d => d.N).From(s => s.N) : null;
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F08");
    }

    /// <summary>spike 06-6：局部变量声明（var c2 = 链）→ F08</summary>
    [Fact]
    public void LocalVariableDeclaration_WrappingFluentCall_Rejected()
    {
        var source = """
            using NCMapper;
            namespace Rj6
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        var c2 = cfg.For<U, D>().Member(d => d.N).From(s => s.N);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F08");
    }

    /// <summary>spike 06-7：变量重新赋值（c = 链）→ F08</summary>
    [Fact]
    public void AssignmentExpression_WrappingFluentCall_Rejected()
    {
        var source = """
            using NCMapper;
            namespace Rj7
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        FluentTypeConfig<U, D> c = null!;
                        c = cfg.For<U, D>().Member(d => d.N).From(s => s.N);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F08");
    }

    /// <summary>spike 06-8：for 循环包裹 → F08（spike 覆盖清单补全）</summary>
    [Fact]
    public void ForLoop_WrappingFluentCall_Rejected()
    {
        var source = """
            using NCMapper;
            namespace Rj8
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        for (int i = 0; i < 1; i++) cfg.For<U, D>().Member(d => d.N).From(s => s.N);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F08");
    }

    /// <summary>spike 06-9：白名单——纯链式静态调用通过（无 F08）</summary>
    [Fact]
    public void PureChainStaticCall_WhiteListed_Accepted()
    {
        var source = """
            using NCMapper;
            namespace Rj9
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(d => d.N).From(s => s.N).Member(d => d.NotExist).Ignore();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 白名单：无动态拒绝诊断（Ignore 的 NotExist 触发 F02——难点⑦，属预期）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F08");
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F02");
    }

    // ── 07 lambda 边界（难点⑦：编译器放行但 Fluent 输入域拒绝）────────

    /// <summary>spike 07-3：方法调用 src.Items.Count() → F07（分析器真实价值场景）</summary>
    [Fact]
    public void MethodInvocationInLambda_NCMAP_F07_Emitted()
    {
        var source = """
            using NCMapper;
            using System.Collections.Generic;
            using System.Linq;
            namespace Rj10
            {
                public class U { public List<int> Items { get; set; } = new(); }
                public class D { public int Count { get; set; } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(d => d.Count).From(s => s.Items.Count());
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F07");
        // 声明被丢弃：正向方法体不生成 Count 赋值（F07 丢声明后自动匹配/扁平化亦未覆盖——
        // 方法体级断言排除 DeepCopy 同名赋值干扰）
        var body = result.Methods["Map_U_To_D"].Single().Body;
        Assert.DoesNotContain("dst.Count", body);
    }

    /// <summary>spike 07-4：null 条件访问 src.Address?.City → F05</summary>
    [Fact]
    public void ConditionalAccessInLambda_NCMAP_F05_Emitted()
    {
        var source = """
            using NCMapper;
            namespace Rj11
            {
                public class Addr { public string City { get; set; } = ""; }
                public class U { public Addr? Address { get; set; } }
                public class D { public string? City { get; set; } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(d => d.City).From(s => s.Address?.City);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F05");
    }

    /// <summary>spike 07-5：索引器访问 → F06。
    /// <para>（G7）行为变更：**常量整数**索引段已放行
    ///（s.Items[0] → SourceIndexes 承载 + ElementAtOrDefault 语义，见 LambdaIndexSegmentTests）；
    /// F06 保留给变量/非常量索引（含先验锚：同形态常量版已可编译生成）。</para></summary>
    [Fact]
    public void IndexerAccessInLambda_NCMAP_F06_Emitted()
    {
        var source = """
            using NCMapper;
            using System.Collections.Generic;
            namespace Rj12
            {
                public class U { public List<int> Items { get; set; } = new(); }
                public class D { public int First { get; set; } }

                [MapperConfig]
                public static class M
                {
                    private static int _idx = 0;
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(d => d.First).From(s => s.Items[_idx]);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F06");
    }

    /// <summary>spike 07-7：lambda 根表达式非 lambda 参数（静态类引用）→ F04（声明丢弃）</summary>
    [Fact]
    public void LambdaRootNotParameter_NCMAP_F04_Emitted()
    {
        var source = """
            using NCMapper;
            namespace Rj18
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }
                public static class Holder { public static string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(d => d.N).From(s => Holder.N);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f04 = result.Diagnostics.Where(d => d.Id == "NCMAP_F04").ToList();
        Assert.Single(f04);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f04[0].Severity);
    }

    /// <summary>spike 07-8 邻界反例：lambda 根为 lambda 参数（合法形态）→ 不报 F04</summary>
    [Fact]
    public void LambdaRootIsParameter_NoF04()
    {
        var source = """
            using NCMapper;
            namespace Rj19
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(d => d.N).From(s => s.N);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F04");
    }

    /// <summary>spike 07-6：诊断含位置信息（external location 行号有效）</summary>
    [Fact]
    public void Diagnostic_LocationIncluded()
    {
        var source = """
            using NCMapper;
            namespace Rj13
            {
                public class Addr { public string City { get; set; } = ""; }
                public class U { public Addr? Address { get; set; } }
                public class D { public string? City { get; set; } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(d => d.City).From(s => s.Address?.City);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var diag = result.Diagnostics.First(d => d.Id == "NCMAP_F05");
        Assert.NotEqual(Microsoft.CodeAnalysis.Location.None, diag.Location);
        Assert.True(diag.Location.GetLineSpan().StartLinePosition.Line > 0);
    }

    // ── D-2 静默跳过深化（编译器已报的结构错误——源生成器不重复报）──

    /// <summary>链未以 For 起头 → 静默跳过（零快照零诊断——CS1061 已由编译器报）</summary>
    [Fact]
    public void ChainNotStartingWithFor_SilentlySkipped()
    {
        var source = """
            using NCMapper;
            namespace Rj14
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.Wrong<U, D>().Member(d => d.N).From(s => s.N); // CS1061：无 Wrong 方法
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 静默跳过：无 F-诊断、不产生 U→D 快照
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        Assert.DoesNotContain("Map_U_To_D(", result.GeneratedCode);
    }

    /// <summary>For 泛型参数个数错误 → 静默跳过（CS0305 已由编译器报）</summary>
    [Fact]
    public void ForTypeArgCountWrong_SilentlySkipped()
    {
        var source = """
            using NCMapper;
            namespace Rj15
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U>().Member(d => d.N).From(s => s.N); // CS0305：For 需 2 个类型参数
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        Assert.DoesNotContain("Map_U_To_D(", result.GeneratedCode);
    }

    /// <summary>未知链方法名 → F60 显式化 + 整链丢弃（契约转正：
    /// 原静默跳过 → B2-18 补诊断；丢弃语义不变）</summary>
    [Fact]
    public void UnknownChainMethod_F60Emitted_ChainDropped()
    {
        var source = """
            using NCMapper;
            namespace Rj16
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.N).From(s => s.N).Rename(d => d.N); // CS1061：无 Rename
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // B2-18：未知链终端（编译器已报 CS1061 场景同覆盖）→ F60（Warning，携带方法名）
        var f60 = result.Diagnostics.Where(d => d.Id == "NCMAP_F60").ToList();
        Assert.Single(f60);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f60[0].Severity);
        Assert.Contains("Rename", f60[0].GetMessage());
        // 丢弃语义保持：整链仍被丢弃（无映射方法）
        Assert.DoesNotContain("Map_U_To_D(", result.GeneratedCode);
    }

    /// <summary>Member 头声明后 From 实参不足（0 个而非 1 个 lambda）→ 静默跳过声明（编译器已报 CS7036）</summary>
    [Fact]
    public void MapInsufficientArgs_SilentlySkipped()
    {
        var source = """
            using NCMapper;
            namespace Rj17
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.N).From(); // CS7036：From 需 1 实参
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 静默跳过核心断言：无 F-诊断（编译器已报 CS7036，源生成器不重复报）；
        // 注：N 未显式声明时按"自动匹配默认开"（S-3a）同名映射属合法行为——不以其缺失/存在为判据
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }
}
