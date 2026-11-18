using System.Globalization;
using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// （「防呆诊断」行 + 消息模板）：
/// 防呆四码仓内持久回归面——F68（裸 Member 提醒，Warning）/ F69（省略来源无同名，Error）/
/// F70（同维度跨段重复，Error）/ F71（MapWhen 类型参不符，Error）。
/// 主体代码册验收以外部探针（mnt/data/fl-impl-smoke）核对，仓内此前零承载
/// （test 全库 F68-F71 零命中实测）——本文件补齐；断言口径 = 触发码 + 严重级别 +
/// 消息实参 + 合法面负向锚 + 双语资源（ResourceManager Invariant/zh-CN，先例
/// MapSubclassDiagnosticsTests.ResxKeys_NeutralAndZhCN_BothPresent 口径）。
/// </summary>
public class FluentMemberFoolProofTests
{
    // ═══════════════════ F68 裸 Member 防呆（Warning） ═══════════════════

    /// <summary>裸 Member 单语句（段内无任何头声明/配置）→ F68 提醒级。</summary>
    [Fact]
    public void F68_BareMemberNoFollow_WarningEmitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Fp1
            {
                public class E { public string Name { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Name);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f68 = result.Diagnostics.Where(d => d.Id == "NCMAP_F68").ToList();
        Assert.Single(f68);
        Assert.Equal(DiagnosticSeverity.Warning, f68[0].Severity);
    }

    /// <summary>负向锚：Member 段有头声明跟随 → 零 F68（合法面不误伤）。</summary>
    [Fact]
    public void F68_MemberWithDeclarationFollow_NotEmitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Fp2
            {
                public class E { public string Name { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Name).From(s => s.Name);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F68");
    }

    /// <summary>负向锚：Member 段有配置调用跟随 → 零 F68。</summary>
    [Fact]
    public void F68_MemberWithConfigFollow_NotEmitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Fp3
            {
                public class E { public string Phone { get; set; } = ""; }
                public class D { public string Phone { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Phone).Mask<MaskPhone>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F68");
    }

    // ═══════════════════ F69 省略来源无同名（Error） ═══════════════════

    /// <summary>配置段省略来源 + 源类型无同名成员 → F69（fail-fast 防静默丢声明）。</summary>
    [Fact]
    public void F69_OmittedSourceNoSameNameMember_Emitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Fp4
            {
                public class E { public string Mobile { get; set; } = ""; }
                public class D { public string Phone { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Phone).Mask<MaskPhone>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f69 = result.Diagnostics.Where(d => d.Id == "NCMAP_F69").ToList();
        Assert.Single(f69);
        Assert.Equal(DiagnosticSeverity.Error, f69[0].Severity);
        // 消息实参：目标字段名 + 源类型名（zh/EN 资源 {0}/{1} 槽位）
        Assert.Contains("Phone", f69[0].GetMessage());
    }

    /// <summary>负向锚：源类型存在同名成员 → 来源可省合法面零 F69。</summary>
    [Fact]
    public void F69_OmittedSourceSameNameExists_NotEmitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Fp5
            {
                public class E { public string Phone { get; set; } = ""; }
                public class D { public string Phone { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>().Member(d => d.Phone).Mask<MaskPhone>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F69");
    }

    // ═══════════════════ F70 同维度跨段重复（Error） ═══════════════════

    /// <summary>跨段来源槽重复（From × From 同 dest）→ F70（Q2 跨段同槽预置判定必报错）。</summary>
    [Fact]
    public void F70_CrossSegmentSourceDuplicate_Emitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Fp6
            {
                public class E { public string Name { get; set; } = ""; public string Title { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>()
                              .Member(d => d.Name).From(s => s.Name)
                              .Member(d => d.Name).From(s => s.Title);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f70 = result.Diagnostics.Where(d => d.Id == "NCMAP_F70").ToList();
        Assert.Single(f70);
        Assert.Equal(DiagnosticSeverity.Error, f70[0].Severity);
        Assert.Contains("Name", f70[0].GetMessage());
    }

    /// <summary>跨段变换槽同 Kind 重复（Mask × Mask 同 dest）→ F70。</summary>
    [Fact]
    public void F70_CrossSegmentTransformDuplicate_Emitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Fp7
            {
                public class E { public string Phone { get; set; } = ""; }
                public class D { public string Phone { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>()
                              .Member(d => d.Phone).From(s => s.Phone).Mask<MaskPhone>()
                              .Member(d => d.Phone).Mask<MaskChineseName>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f70 = result.Diagnostics.Where(d => d.Id == "NCMAP_F70").ToList();
        Assert.Single(f70);
        Assert.Equal(DiagnosticSeverity.Error, f70[0].Severity);
    }

    /// <summary>负向锚：NoAutoInclude 清单重复幂等不报（资源描述明文「不在此列」）。</summary>
    [Fact]
    public void F70_NoAutoIncludeRepeat_IdempotentNotEmitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Fp8
            {
                public class E { public string Name { get; set; } = ""; public string Audit { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; public string Audit { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>()
                              .Member(d => d.Name).From(s => s.Name)
                              .Member(d => d.Audit).NoAutoInclude()
                              .Member(d => d.Audit).NoAutoInclude();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F70");
    }

    // ═══════════════════ F71 MapWhen 类型参不符（Error） ═══════════════════

    /// <summary>显式类型参 ∉ {TProp, TSource} → F71（防写错类型参被静默丢条件）。</summary>
    [Fact]
    public void F71_MapWhenTypeArgMismatch_Emitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Fp9
            {
                public class E { public string Name { get; set; } = ""; }
                public class D { public string Name { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>()
                              .Member(d => d.Name).From(s => s.Name).MapWhen<int>(v => v > 0);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f71 = result.Diagnostics.Where(d => d.Id == "NCMAP_F71").ToList();
        Assert.Single(f71);
        Assert.Equal(DiagnosticSeverity.Error, f71[0].Severity);
    }

    /// <summary>负向锚：字段值形态（非泛型）与整源形态（显式 TSource）合法 → 零 F71。</summary>
    [Fact]
    public void F71_MapWhenLegalForms_NotEmitted()
    {
        var source = """
            using NCMapper;
            namespace Tc2Fp10
            {
                public class E { public string Name { get; set; } = ""; public bool Flag { get; set; } }
                public class D { public string Name { get; set; } = ""; public bool Flag { get; set; } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                        => cfg.For<E, D>()
                              .Member(d => d.Name).From(s => s.Name).MapWhen(v => v.Length > 0)
                              .Member(d => d.Flag).MapWhen<E>(s => s.Flag);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F71");
    }

    // ═══════════════════ 双语资源（EN neutral + zh-CN 卫星） ═══════════════════

    /// <summary>防呆四码 Title/Message/Description 双语键齐备且非同文（先例 MapSubclassDiagnosticsTests 口径）。</summary>
    [Theory]
    [InlineData("NCMAP_F68")]
    [InlineData("NCMAP_F69")]
    [InlineData("NCMAP_F70")]
    [InlineData("NCMAP_F71")]
    public void ResxKeys_NeutralAndZhCN_BothPresent(string id)
    {
        var manager = NCMapper.SourceGen.Resources.DiagnosticResources.ResourceManager;
        foreach (var suffix in new[] { "Title", "Message", "Description" })
        {
            var key = $"{id}_{suffix}";
            var neutral = manager.GetString(key, CultureInfo.InvariantCulture);
            Assert.False(string.IsNullOrEmpty(neutral), $"neutral 缺键 {key}");
            var zh = manager.GetString(key, new CultureInfo("zh-CN"));
            Assert.False(string.IsNullOrEmpty(zh), $"zh-CN 缺键 {key}");
            Assert.NotEqual(neutral, zh);
        }
    }
}
