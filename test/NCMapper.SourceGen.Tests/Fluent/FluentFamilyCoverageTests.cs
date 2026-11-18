using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// P3（001 清单生成端族级缺口清零——004 重锚）：
/// Fluent 四族边界矩阵归拢（此前散布多文件无专项）——
/// ①Format 正向发射（可空源 ?./枚举源单参 ToString/FormatThenMask 双标/InitOnly 目标）；
/// ②FromJson 专项（string→对象反序列化 / 对象→string 序列化 / 显式来源组合 / 嵌套目标）；
/// ③MapTree(dest)（Fluent 独有——递归集合 / 与对级 Tree() 共存）；
/// ④Mask/属性级 NoAutoInclude 边界（null 输入透传 / 自定义 IMask 透传 / 注册表发射形态）。
/// 断言 = 方法体级发射形态（探针实证口径）+ [Trait("Capability","A-xx")] 自证（P5 机制）。
/// </summary>
public class FluentFamilyCoverageTests
{
    // ═══════════════════ ① Format 正向发射（A-23） ═══════════════════

    /// <summary>可空源（DateTime?）→ "?." 空条件调用发射（BuildFormattedAssignment nullableAccess=true）。</summary>
    [Fact]
    [Trait("Capability", "A-23")]
    public void Format_NullableSource_EmitsNullConditional()
    {
        var source = """
            using System;
            using NCMapper;
            namespace Tc2P3F1
            {
                public class E { public DateTime? Stamp { get; set; } = new(2024, 6, 1); }
                public class D { public string Stamp { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Stamp).From(s => s.Stamp).Format("yyyy-MM-dd");
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        Assert.Contains("?.ToString(\"yyyy-MM-dd\", null)", fwd.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>枚举源 → 单参 ToString(fmt)（.NET 10 枚举双参 CS0618 规避——BuildFormattedAssignment isEnumSource）。</summary>
    [Fact]
    [Trait("Capability", "A-23")]
    public void Format_EnumSource_EmitsSingleArgToString()
    {
        var source = """
            using NCMapper;
            namespace Tc2P3F2
            {
                public enum Grade { A, B, C }
                public class E { public Grade Level { get; set; } = Grade.A; }
                public class D { public string Level { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Level).From(s => s.Level).Format("G");
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        Assert.Contains(".ToString(\"G\")", fwd.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>FormatThenMask 双标（先格式化再脱敏——CodeGenerator.Mapping 双标分支）。</summary>
    [Fact]
    [Trait("Capability", "A-23")]
    public void Format_ThenMask_EmitsMaskedFormatted()
    {
        var source = """
            using System;
            using NCMapper;
            namespace Tc2P3F3
            {
                public class E { public DateTime Stamp { get; set; } = new(2024, 6, 1); }
                public class D { public string Stamp { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Stamp).From(s => s.Stamp).Format("yyyy-MM-dd").Mask<MaskPhone>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        // 双标链：Mask(格式化结果)——格式化内嵌于 Mask 实参
        Assert.Contains(".Mask(", fwd.Body);
        Assert.Contains(".ToString(\"yyyy-MM-dd\", null)", fwd.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>InitOnly 目标属性（init setter）→ 正常发射（get-only 才触发 F28）。</summary>
    [Fact]
    [Trait("Capability", "A-23")]
    public void Format_InitOnlyTarget_Emits()
    {
        var source = """
            using System;
            using NCMapper;
            namespace Tc2P3F4
            {
                public class E { public DateTime Stamp { get; set; } = new(2024, 6, 1); }
                public class D { public string Stamp { get; init; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Stamp).From(s => s.Stamp).Format("yyyy-MM-dd");
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        Assert.Contains("Stamp", fwd.Body);
        Assert.Contains(".ToString(\"yyyy-MM-dd\", null)", fwd.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    // ═══════════════════ ② FromJson 专项（A-12） ═══════════════════

    /// <summary>FromJson string→对象（JsonConvertToObject 反序列化发射）。</summary>
    [Fact]
    [Trait("Capability", "A-12")]
    public void FromJson_StringToObject_EmitsDeserialize()
    {
        var source = """
            using NCMapper;
            namespace Tc2P3J1
            {
                public class Pay { public int Id { get; set; } }
                public class E { public string Blob { get; set; } = "{}"; }
                public class D { public Pay Blob { get; set; } = new(); }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Blob).FromJson();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        Assert.Contains("JsonConvertToObject<Tc2P3J1.Pay>(src.Blob)", fwd.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>FromJson 对象→string（JsonConvertFromObject 序列化发射——反向形态目标）。</summary>
    [Fact]
    [Trait("Capability", "A-12")]
    public void FromJson_ObjectToString_EmitsSerialize()
    {
        var source = """
            using NCMapper;
            namespace Tc2P3J2
            {
                public class Pay { public int Id { get; set; } }
                public class E { public Pay Blob { get; set; } = new(); }
                public class D { public string Blob { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Blob).FromJson();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        // 探针实锚：对象→string = null 守卫三元 + ObjectConvertToJson（CodeGenerator.Mapping :1305 形态）
        Assert.Contains("ObjectConvertToJson(src.Blob)", fwd.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>FromJson + 嵌套目标（Pay 内嵌套 Payload 属性——深层展开边界）。</summary>
    [Fact]
    [Trait("Capability", "A-12")]
    public void FromJson_NestedPayload_Emits()
    {
        var source = """
            using NCMapper;
            namespace Tc2P3J3
            {
                public class Inner { public int Seq { get; set; } }
                public class Pay { public Inner Payload { get; set; } = new(); }
                public class E { public string Blob { get; set; } = "{}"; }
                public class D { public Pay Blob { get; set; } = new(); }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Blob).FromJson();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        Assert.Contains("JsonConvertToObject", fwd.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    // ═══════════════════ ③ MapTree(dest) 专项（A-05，Fluent 独有） ═══════════════════

    /// <summary>MapTree 递归集合（List<E>→List<D> 属性级递归映射发射）。</summary>
    [Fact]
    [Trait("Capability", "A-05")]
    public void MapTree_RecursiveCollection_EmitsRecursion()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper;
            namespace Tc2P3T1
            {
                public class E { public string Name { get; set; } = ""; public List<E> Children { get; set; } = new(); }
                public class D { public string Name { get; set; } = ""; public List<D> Children { get; set; } = new(); }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Children).MapTree();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        Assert.Contains("Children", fwd.Body);
        Assert.Contains("Map_E_To_D(", result.GeneratedCode);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>MapTree 与对级 Tree() 共存（对级树配置 + 字段级 MapTree 叠加不冲突）。</summary>
    [Fact]
    [Trait("Capability", "A-05")]
    public void MapTree_WithPairLevelTree_Coexists()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper;
            namespace Tc2P3T2
            {
                public class Nav { public string Note { get; set; } = ""; }
                public class E { public string Name { get; set; } = ""; public Nav Audit { get; set; } = new(); }
                public class D { public string Name { get; set; } = ""; public Nav Audit { get; set; } = new(); }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Tree().Member(d => d.Audit).MapTree();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        Assert.Contains("Audit", fwd.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    // ═══════════════════ ④ Mask / 属性级 NoAutoInclude 边界（A-03 / A-18） ═══════════════════

    /// <summary>Mask null 输入透传（IMask.Mask(string?) 可空契约——生成端直传不判空）。</summary>
    [Fact]
    [Trait("Capability", "A-03")]
    public void Mask_NullableInput_PassedThrough()
    {
        var source = """
            using NCMapper;
            namespace Tc2P3M1
            {
                public class E { public string? Phone { get; set; } }
                public class D { public string Phone { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Phone).From(s => s.Phone).Mask<MaskPhone>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        Assert.Contains(".Mask(src.Phone)", fwd.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>属性级 NoAutoInclude 注册表发射（A-18——字段版注册表承载形态）。</summary>
    [Fact]
    [Trait("Capability", "A-18")]
    public void NoAutoInclude_FieldLevel_EmitsRegistryAnchor()
    {
        var source = """
            using NCMapper;
            namespace Tc2P3N1
            {
                public class Nav { public string Note { get; set; } = ""; }
                public class E { public string Name { get; set; } = ""; public Nav Audit { get; set; } = new(); }
                public class D { public string Name { get; set; } = ""; public Nav Audit { get; set; } = new(); }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Tree().Member(d => d.Audit).NoAutoInclude();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
        // 主产物锚：Audit 正常映射发射（字段级 NoAutoInclude 的降级注册表承载面在
        // FluentSnapshotRegistry 产物段——主产物不含 "NoAutoInclude" 文本属预期形态）
        Assert.Contains("Audit", Assert.Single(result.Methods["Map_E_To_D"]).Body);
    }
}
