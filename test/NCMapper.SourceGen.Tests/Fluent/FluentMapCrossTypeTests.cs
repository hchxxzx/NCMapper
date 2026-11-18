using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 新增断言：路径式 Convert 并入 Map（双泛型）后的跨类型行为固化——
/// ① 跨型 Map 内建转换实测（DateTime→string 走 TypeConvertHelper.ConvertValue 发射链，
///    显式声明跳过 NCMAP027 自动匹配门——CodeGenerator.Mapping.cs IsExplicitlyMapped 分支）；
/// ② 跨型 Map 豁免门（overrideIgnore: true → Target.HasOverrideIgnore 置位；缺省对照组不置位）；
/// ③ 负向断言（F13 退役行为固化，评审 P1-2）：Map + 非纯路径表达式（二元表达式——
///    原 F13 唯一触发场景）→ 编译通过、零 NCMAP_F* 诊断、声明静默丢弃（该属性无赋值）且
///    同链其余声明不受影响（声明级丢弃 ≠ 整链丢弃）；
/// ④ 同型 Map 回归（T1==T2 直赋）。
/// </summary>
public class FluentMapCrossTypeTests
{
    /// <summary>直调 FluentExtractor.ExtractCore 取完整物化快照（等价机制同款入口）</summary>
    private static MappingSnapshot? ExtractSnapshot(string source)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var typeDecl = compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("MapperConfig")))!;
        var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        return FluentExtractor.ExtractCore(compilation, typeSymbol!, typeDecl, CancellationToken.None)
            ?.Declarations.FirstOrDefault()?.Snapshot;
    }

    /// <summary>定位快照中目标属性名对应的正向 pm</summary>
    private static PropertyMappingSnapshot PmOf(MappingSnapshot snapshot, string targetPropName)
        => snapshot.PropertyMappings.First(pm => pm.Target.Name == targetPropName);

    /// <summary>
    /// ① 跨型 Map 内建转换（DateTime→string）：显式声明跳过 NCMAP027 门，生成代码走
    /// TypeConvertHelper.ConvertValue 发射链（fallback 分支——与特性侧自动匹配同链路，A-04 等价侧证）；
    /// 同链同名属性自动匹配回归（Name = src.Name 直赋）。
    /// </summary>
    [Fact]
    public void CrossType_Map_DateTimeToString_BuiltInConversion()
    {
        var source = """
            using System;
            using NCMapper;
            public class FmcXEntity { public DateTime Ts { get; set; } public string Name { get; set; } = ""; }
            public class FmcXDto { public string Ts { get; set; } = ""; public string Name { get; set; } = ""; }

            [MapperConfig]
            public class FmcXMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FmcXEntity, FmcXDto>().Member(d => d.Ts).From(s => s.Ts);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.True(result.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error),
            "跨型 Map（DateTime→string）为合法声明，不应产生 Error 诊断");
        var fwdBody = result.Methods["Map_FmcXEntity_To_FmcXDto"][0].Body;
        Assert.Contains("TypeConvertHelper.ConvertValue", fwdBody);
        Assert.Contains("src.Ts", fwdBody);
        // 同名属性自动匹配回归（直赋）
        Assert.Contains("Name = src.Name", fwdBody);
    }

    /// <summary>
    /// ② 跨型 Map 豁免门（能力新增——原路径式 Convert 无豁免门，并入 Map 后免费获得）：
    /// overrideIgnore: true → Target.HasOverrideIgnore 置位；缺省（false）→ 不置位（对照组）。
    /// </summary>
    [Fact]
    public void CrossType_Map_OverrideIgnore_Flag()
    {
        var source = """
            using System;
            using NCMapper;
            public class FmcOEntity { public DateTime A { get; set; } public DateTime B { get; set; } }
            public class FmcODto { public string A { get; set; } = ""; public string B { get; set; } = ""; }

            [MapperConfig]
            public class FmcOMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FmcOEntity, FmcODto>()
                       .Member(d => d.A).From(s => s.A, overrideIgnore: true)
                       .Member(d => d.B).From(s => s.B);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pmA = PmOf(snapshot!, "A");
        Assert.True(pmA.Target.HasOverrideIgnore, "跨型 Map overrideIgnore: true → Target.HasOverrideIgnore 置位（豁免门能力新增）");
        var pmB = PmOf(snapshot!, "B");
        Assert.False(pmB.Target.HasOverrideIgnore, "跨型 Map 缺省 → 不置位（照常被全局忽略抑制——对照组）");
    }

    /// <summary>
    /// ③ 负向断言（F13 退役行为固化）：Map + 非纯路径表达式（二元表达式——原 NCMAP_F13 唯一触发场景，
    /// 用例形态继承自 DiagnosticFluentTests.F13_Convert_BinaryExpression_Diagnosed）→ 编译通过、
    /// 零 NCMAP_F* 诊断（F13 已退役，不新增等价防护——语义对齐同型 Map 静默丢弃）、
    /// 该属性无赋值（声明丢弃）且同链其余声明照常生成（声明级丢弃，非整链丢弃）。
    /// </summary>
    [Fact]
    public void Map_NonPurePath_SilentlyDropped_NoF13()
    {
        var source = """
            using System;
            using NCMapper;
            public class FmcNEntity { public string Name { get; set; } = ""; public string Sku { get; set; } = ""; public int Id { get; set; } }
            public class FmcNDto { public string Display { get; set; } = ""; public int Id { get; set; } }

            [MapperConfig]
            public class FmcNMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FmcNEntity, FmcNDto>()
                       .Member(d => d.Display).From(s => s.Name + " (" + s.Sku + ")")
                       .Member(d => d.Id).From(s => s.Id);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        // 编译通过 + 零 F 系诊断（静默丢弃——无 F13 等价防护）
        Assert.True(result.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error),
            "Map + 非纯路径表达式应编译通过（声明静默丢弃，不再阻断）");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F", StringComparison.Ordinal));
        // Display 声明静默丢弃——前向方法体无该属性赋值（生成代码全局含 DeepCopy_FmcNDto
        // 自拷贝等无关 Display 引用，断言须限定映射方法体范围）
        var fwdBody = result.Methods["Map_FmcNEntity_To_FmcNDto"][0].Body;
        Assert.DoesNotContain("Display", fwdBody);
        // 同链其余声明不受影响（Id 照常映射——声明级丢弃 ≠ 整链丢弃）
        Assert.Contains("Id = src.Id", fwdBody);
    }

    /// <summary>④ 同型 Map 回归（T1==T2 双泛型签名下直赋行为不变）。</summary>
    [Fact]
    public void SameType_Map_DirectAssign_Regression()
    {
        var source = """
            using System;
            using NCMapper;
            public class FmcSEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class FmcSDto { public int Id { get; set; } public string Name { get; set; } = ""; }

            [MapperConfig]
            public class FmcSMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FmcSEntity, FmcSDto>().Member(d => d.Id).From(s => s.Id);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        Assert.True(result.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error),
            "同型 Map 为既有合法声明，不应产生 Error 诊断");
        var fwdBody = result.Methods["Map_FmcSEntity_To_FmcSDto"][0].Body;
        Assert.Contains("Id = src.Id", fwdBody);
        // Name 自动匹配回归
        Assert.Contains("Name = src.Name", fwdBody);
    }
}
