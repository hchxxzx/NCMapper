using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// （「豁免门/方向回归」「跨型来源」「反向派生」行）：
/// ①方向性忽略逐方向（ToDto = 正向跳过反向照常 / ToEntity = 反向跳过正向照常——src
/// FluentMappingConfig.MemberHead.Ignore(Direction) 语义注记）；②跨型来源（From 跨型内建转换
/// 可行零 NCMAP027 + FromSource 速记 object 占位跨型不受限）；③反向派生（变换反向不消费、
/// From 来源声明参与反向照常——反向派生口径）。
/// 断言口径 = 方法体级 + 产物形态双锚——正向/反向主方法为**对象初始化器形态**（`Name = src.Name`）、
/// _Update 方法为**赋值语句形态**（`dst.Name = src.Name;`）——探针实测（产物初始化器中 Secret 缺席
/// = ToDto 抑制实锚）；全文级会误命中另一方向段（先例 FluentChannelScenariosTests 口径）。
/// </summary>
public class FluentMemberDirectionalTests
{
    // ═══════════════════ ① 方向性忽略逐方向 ═══════════════════

    /// <summary>Ignore(ToDto)：正向初始化器无 Secret（跳过），反向初始化器含 Secret（照常）。</summary>
    [Fact]
    public void Directional_IgnoreToDto_ForwardSkipped()
    {
        var source = """
            using NCMapper;
            namespace Tc2Dir1
            {
                public class E { public string Name { get; set; } = ""; public string Secret { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public string Secret { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>()
                              .Member(d => d.Name).From(s => s.Name)
                              .Member(d => d.Secret).Ignore(NCMapper.Attributes.Direction.ToDto);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        var rev = Assert.Single(result.Methods["Map_D_To_E"]);
        Assert.Contains("Name = src.Name", fwd.Body);
        Assert.DoesNotContain("Secret = src.Secret", fwd.Body);
        Assert.Contains("Secret = src.Secret", rev.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>Ignore(ToEntity)：正向初始化器含 Secret（照常），反向初始化器无 Secret（跳过）。</summary>
    [Fact]
    public void Directional_IgnoreToEntity_ReverseSkipped()
    {
        var source = """
            using NCMapper;
            namespace Tc2Dir2
            {
                public class E { public string Name { get; set; } = ""; public string Secret { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public string Secret { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>()
                              .Member(d => d.Name).From(s => s.Name)
                              .Member(d => d.Secret).Ignore(NCMapper.Attributes.Direction.ToEntity);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        var rev = Assert.Single(result.Methods["Map_D_To_E"]);
        Assert.Contains("Secret = src.Secret", fwd.Body);
        Assert.Contains("Name = src.Name", rev.Body);
        Assert.DoesNotContain("Secret = src.Secret", rev.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    // ═══════════════════ ② 跨型来源 ═══════════════════

    /// <summary>From 跨型（DateTime→string，内建转换可行）→ 正常发射零 NCMAP027。</summary>
    [Fact]
    public void CrossType_From_BuiltinConversion_Emits()
    {
        var source = """
            using System;
            using NCMapper;
            namespace Tc2Dir3
            {
                public class E { public DateTime Created { get; set; } = new(2024, 6, 1); }
                public class D { public string Created { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Created).From(s => s.Created);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        // 跨型（无隐式转换可行对）经 TypeConvertHelper 回退通道承载（探针实测形态：dst.Created = TypeConvertHelper.ConvertValue<DateTime,string>(src.Created)!）
        Assert.Contains("src.Created", fwd.Body);
        Assert.Contains("TypeConvertHelper.ConvertValue", fwd.Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>FromSource 速记跨型字段（子源 int → 目标 string，object 占位跨型不受限）。</summary>
    [Fact]
    public void CrossType_FromSource_Shorthand_Emits()
    {
        var source = """
            using NCMapper;
            namespace Tc2Dir4
            {
                public class Sub { public int Code { get; set; } }
                public class E { public string Name { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public string Code { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>()
                              .Member(d => d.Name).From(s => s.Name)
                              .Member(d => d.Code).FromSource<Sub>(s => s.Code);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("Map_E_To_D(", result.GeneratedCode);
        // 跨源字段不入正向单源映射体（Compose 分流——方法体级锚）
        Assert.DoesNotContain("Code = ", Assert.Single(result.Methods["Map_E_To_D"]).Body);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id.StartsWith("NCMAP_F"));
    }

    // ═══════════════════ ③ 反向派生（新组合反向） ═══════════════════

    /// <summary>反向派生·变换不消费：Mask 字段正向脱敏（初始化器携带 __mask_ 管线）、反向照常同名赋值。</summary>
    [Fact]
    public void ReverseDerive_Mask_NotConsumedReverse()
    {
        var source = """
            using NCMapper;
            namespace Tc2Dir5
            {
                public class E { public string Phone { get; set; } = ""; }
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
        var rev = Assert.Single(result.Methods["Map_D_To_E"]);
        Assert.Contains(".Mask(", fwd.Body);
        Assert.Contains("Phone = src.Phone", rev.Body);
    }

    /// <summary>反向派生·来源照常：From 异名声明参与反向（初始化器对调 Mobile/Phone）。</summary>
    [Fact]
    public void ReverseDerive_From_ConsumedReverse()
    {
        var source = """
            using NCMapper;
            namespace Tc2Dir6
            {
                public class E { public string Mobile { get; set; } = ""; }
                public class D { public string Phone { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Phone).From(s => s.Mobile);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var fwd = Assert.Single(result.Methods["Map_E_To_D"]);
        var rev = Assert.Single(result.Methods["Map_D_To_E"]);
        Assert.Contains("Phone = src.Mobile", fwd.Body);
        // 反向：来源声明参与反向照常（异名对调）
        Assert.Contains("Mobile = src.Phone", rev.Body);
    }
}
