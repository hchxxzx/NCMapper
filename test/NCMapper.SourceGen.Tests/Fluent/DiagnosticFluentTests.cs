using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// Fluent 通道编译期诊断触发测试（验收 #3——B-4/D-1 拆分：
/// F14/F15 触发验证由移交至此收编；F11 见 DualDriverArbiterTests；
/// F02/F04~F08 触发链路经 EmitFluentDiagnostics——本文件覆盖 F08/F02 抽样 + F12/F14/F15 全量；
/// F13 已退役（路径式 Convert 并入 Map，非法表达式与同型 Map 一致静默丢弃——
/// 负向断言见 FluentMapCrossTypeTests））
/// </summary>
public class DiagnosticFluentTests
{
    /// <summary>F12：AfterMapping 目标方法为 private static → 编译期诊断（可见性契约 B-1b）</summary>
    [Fact]
    public void F12_AfterMapping_PrivateTarget_Diagnosed()
    {
        var source = """
            using NCMapper;
            namespace F12Ns
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().AfterMapping(PrivateApply);
                    }

                    private static void PrivateApply(A src, B dst) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f12 = result.Diagnostics.Where(d => d.Id == "NCMAP_F12").ToList();
        Assert.Single(f12);
        Assert.Contains("PrivateApply", f12[0].GetMessage());
        // Error 级（生成代码 CS0122 阻断）
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f12[0].Severity);
    }

    /// <summary>F14：AfterMapping 目标为 lambda → 编译期诊断（S-1b 方法组边界）</summary>
    [Fact]
    public void F14_AfterMapping_Lambda_Diagnosed()
    {
        var source = """
            using NCMapper;
            namespace F14Ns
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().AfterMapping((src, dst) => { dst.Id = src.Id + 1; });
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F14");
    }

    /// <summary>F15：[MapperConfig] 类内无匹配签名方法 → 编译期诊断（A1.5 配套启发式）</summary>
    [Fact]
    public void F15_MapperConfig_NoMatchingMethod_Diagnosed()
    {
        var source = """
            using NCMapper;
            namespace F15Ns
            {
                [MapperConfig]
                public class Mapper
                {
                    // 签名不匹配：非 static / 参数类型不符——不构成 Fluent 入口
                    public void ConfigureWrong(object cfg) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f15 = result.Diagnostics.Where(d => d.Id == "NCMAP_F15").ToList();
        Assert.Single(f15);
        Assert.Contains("Mapper", f15[0].GetMessage());
    }

    /// <summary>F08 抽样：Configure 方法体 if 控制流 → 编译期诊断（难点⑥黑名单）</summary>
    [Fact]
    public void F08_DynamicConfig_IfStatement_Diagnosed()
    {
        var source = """
            using NCMapper;
            namespace F08Ns
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        if (System.DateTime.Now.Year > 2000)
                        {
                            cfg.For<A, B>().Member(d => d.Id).From(s => s.Id);
                        }
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f08 = result.Diagnostics.Where(d => d.Id == "NCMAP_F08").ToList();
        Assert.Single(f08);
        Assert.Contains("if", f08[0].GetMessage());
    }

    /// <summary>F02 抽样：dest lambda 引用不存在属性 → 编译期诊断（属性不存在）</summary>
    [Fact]
    public void F02_DestPropertyNotExist_Diagnosed()
    {
        var source = """
            using NCMapper;
            namespace F02Ns
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().Member(d => d.NotExist).From(s => s.Id);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f02 = result.Diagnostics.Where(d => d.Id == "NCMAP_F02").ToList();
        Assert.Single(f02);
        Assert.Contains("NotExist", f02[0].GetMessage());
        // 声明被丢弃：无该属性映射，但链上其余声明不受影响（B 无其他声明——生成代码无 Id 赋值来自显式声明）
        Assert.DoesNotContain("dst.NotExist", result.GeneratedCode);
    }

    /// <summary>
    /// 邻界反例：合法 Fluent 配置（dest 属性存在 + public static 方法组 AfterMapping + Configure 签名匹配）
    /// → 不报 NCMAP_F02/F14/F15
    /// </summary>
    [Fact]
    public void FluentConfig_ValidForms_NoDiagnostics()
    {
        var source = """
            using NCMapper;
            namespace FOksNs
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().AfterMapping(Apply).Member(d => d.Id).From(s => s.Id);
                    }

                    public static void Apply(A src, B dst) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var codes = result.Diagnostics.Select(d => d.Id).ToList();

        Assert.DoesNotContain("NCMAP_F02", codes);
        Assert.DoesNotContain("NCMAP_F14", codes);
        Assert.DoesNotContain("NCMAP_F15", codes);
    }

    /// <summary> 验收 #2：双语齐备——neutral（EN）+ zh-CN 资源键成对存在</summary>
    [Theory]
    [InlineData("NCMAP_F02")]
    [InlineData("NCMAP_F04")]
    [InlineData("NCMAP_F05")]
    [InlineData("NCMAP_F06")]
    [InlineData("NCMAP_F07")]
    [InlineData("NCMAP_F08")]
    [InlineData("NCMAP_F11")]
    [InlineData("NCMAP_F12")]
    [InlineData("NCMAP_F14")]
    [InlineData("NCMAP_F15")]
    [InlineData("NCMAP_F16")]
    public void FluentDiagnostics_BilingualResources_Present(string ruleId)
    {
        // neutral（EN 默认 fallback）经强类型访问器可读且非空
        var neutral = ruleId switch
        {
            "NCMAP_F02" => Resources.DiagnosticResources.NCMAP_F02_Message,
            "NCMAP_F04" => Resources.DiagnosticResources.NCMAP_F04_Message,
            "NCMAP_F05" => Resources.DiagnosticResources.NCMAP_F05_Message,
            "NCMAP_F06" => Resources.DiagnosticResources.NCMAP_F06_Message,
            "NCMAP_F07" => Resources.DiagnosticResources.NCMAP_F07_Message,
            "NCMAP_F08" => Resources.DiagnosticResources.NCMAP_F08_Message,
            "NCMAP_F11" => Resources.DiagnosticResources.NCMAP_F11_Message,
            "NCMAP_F12" => Resources.DiagnosticResources.NCMAP_F12_Message,
            "NCMAP_F14" => Resources.DiagnosticResources.NCMAP_F14_Message,
            "NCMAP_F15" => Resources.DiagnosticResources.NCMAP_F15_Message,
            "NCMAP_F16" => Resources.DiagnosticResources.NCMAP_F16_Message,
            _ => "",
        };
        Assert.False(string.IsNullOrWhiteSpace(neutral));

        // zh-CN 卫星资源键成对存在（文件级解析——避免依赖测试进程 CurrentUICulture；
        // bin/Debug/net10.0 起 5 层上溯 = 仓库根，与 MainFileWeightBudgetTests 同定位模式）
        var zhPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "NCMapper.SourceGen", "Resources", "DiagnosticResources.zh-CN.resx");
        Assert.True(File.Exists(zhPath), $"zh-CN resx 不存在: {zhPath}");
        var zhContent = File.ReadAllText(zhPath);
        Assert.Contains($"name=\"{ruleId}_Message\"", zhContent);
        Assert.Contains($"name=\"{ruleId}_Title\"", zhContent);
        Assert.Contains($"name=\"{ruleId}_Description\"", zhContent);
    }

    /// <summary> 验收 #4：保留位 F03/F09/F10 不误实现——FromId 反查不含保留位</summary>
    [Fact]
    public void ReservedIds_F03_F09_F10_NotImplemented()
    {
        Assert.Null(NCMapper.SourceGen.DiagnosticDescriptors.FromId("NCMAP_F03"));
        Assert.Null(NCMapper.SourceGen.DiagnosticDescriptors.FromId("NCMAP_F09"));
        Assert.Null(NCMapper.SourceGen.DiagnosticDescriptors.FromId("NCMAP_F10"));
    }
}
