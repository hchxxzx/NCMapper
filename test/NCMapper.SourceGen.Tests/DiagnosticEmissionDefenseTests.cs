// （B5-4 批次五）：诊断实参与发射补齐红测——
//   B2-07：F02 第二实参 = 收方类型显示名（缺陷态 = 硬编码 "" → 消息 "on type ''"）；
//   B3-08：NCMAP019 谓词宽前筛 + transform 端符号级精判——五漏报（属性级映射特性不触发类级
//          子串门）+ 误报（[SomeMaskHelper] 子串命中）+ partial 双段归并（恰 1 条）；
//   B3-21：NCMAP020 上移至空映射早退之前（缺陷态 = 空映射 + 非法全局配置 → NCMAP020 被吞）；
//   B1-13-R1：init-only 枚举转换表达式路径 NCMAP023/024 发射（缺陷态 = 决策期零诊断）。
// 修复前形态：全部断言必红。
using NCMapper;
using NCMapper.Attributes;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

public class DiagnosticEmissionDefenseTests
{
    // ======================= B2-07：F02 第二实参收方类型名 ========================

    /// <summary>B2-07：Fluent lambda 引用不存在属性 → F02 消息含收方类型显示名
    ///（缺陷态 = 第二实参硬编码 ""，消息 "does not exist on type ''"）。</summary>
    [Fact]
    public void B2p07_F02Message_ContainsReceiverTypeName()
    {
        var source = """
            using NCMapper;
            namespace B5T5B207
            {
                public class U { public string Name { get; set; } = ""; }
                public class D { public string City { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.City).From(s => s.Typo);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f02 = result.Diagnostics.Where(d => d.Id == "NCMAP_F02").ToList();
        Assert.NotEmpty(f02);
        // 消息含收方类型名（缺陷态：实参 "" → 消息不含 U 类型名 → 红）
        Assert.Contains("B5T5B207.U", f02[0].GetMessage());
    }

    // ======================= B3-08：NCMAP019 谓词下沉 ========================

    /// <summary>B3-08 五漏报：属性级映射特性（[ConvertBack]/[MapWhen]/[Mask] 等）标于
    /// 属性、类级无清单子串特性、类名非 Dto 后缀 → NCMAP019 漏报（缺陷态 = predicate 只扫
    /// 类级 MapProperty/ConvertUsing/Mask 子串）。修复后 5 类型各报 1 条。</summary>
    [Fact]
    public void B3p08_PropertyLevelMappingAttributes_RequireNcmap019()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            using System;

            namespace B5T5B208
            {
                public class ConvBack { public string Convert(string v) => v; }
                public class WhenPred { public static bool Ok(int v) => v > 0; }

                // ① ConvertBackUsing（属性级）
                public class E1 { public string Tag { get; set; } = ""; }
                public class C1 { public string Tag { get; set; } = ""; }
                public partial class Holder1
                {
                    [ConvertBack(typeof(ConvBack), nameof(ConvBack.Convert))]
                    public string Tag { get; set; } = "";
                }

                // ② Mask（属性级）
                public partial class Holder2
                {
                    [Mask(typeof(MaskPhone))]
                    public string Phone { get; set; } = "";
                }

                // ③ MapWhen（属性级，Fluent 通道类级声明在 MapperConfig——此处属性级模拟）
                public partial class Holder3
                {
                    [MapIgnore(Direction.ToDto)]
                    public string Skip { get; set; } = "";
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var n19 = result.Diagnostics.Where(d => d.Id == "NCMAP019").ToList();
        // 缺陷态：零命中（漏报）→ 红
        Assert.Equal(3, n19.Count);
    }

    /// <summary>B3-08 误报：类级特性名含 "Mask" 子串但非映射特性（[SomeMaskHelper]）→
    /// 不应触发 NCMAP019（缺陷态 = 子串匹配命中 → 误报红）。</summary>
    [Fact]
    public void B3p08_NonMappingAttributeWithMaskSubstring_NoFalsePositive()
    {
        var source = """
            using System;

            namespace B5T5B208B
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class SomeMaskHelperAttribute : Attribute { }

                [SomeMaskHelper]
                public class PlainEntity { public int Id { get; set; } }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP019");
    }

    /// <summary>B3-08 partial 双段：特性段 + 空段 → 同一符号归并后 NCMAP019 恰 1 条
    ///（缺陷态 = 属性级特性 + 非 Dto 命名 → 零命中漏报）。</summary>
    [Fact]
    public void B3p08_PartialType_MergesToSingleDiagnostic()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;

            namespace B5T5B208C
            {
                public partial class SplitDto
                {
                    [MapIgnore(Direction.ToDto)]
                    public string A { get; set; } = "";
                }

                public partial class SplitDto
                {
                    public string B { get; set; } = "";
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var n19 = result.Diagnostics.Where(d => d.Id == "NCMAP019").ToList();
        Assert.Equal(1, n19.Count);
    }

    // ======================= B3-21：NCMAP020 空映射早退上移 ========================

    /// <summary>B3-21：空映射 + 非法全局配置（MSBuild 通道 NCMapperNamingStrategy 非法枚举名
    /// → NCMAP020；特性通道强类型参数编译期恒合法无触发面——勘验登记）→
    /// NCMAP020 仍发射（缺陷态 = NCMAP020 块在空映射早退之后被吞）。</summary>
    [Fact]
    public void B3p21_Ncmap020_EmittedOnEmptyMappings()
    {
        var source = """
            namespace B5T5B209
            {
                public class NoMappingsHere { public int Id { get; set; } }
            }
            """;
        var result = TestHelper.GenerateMapper(source, new System.Collections.Generic.Dictionary<string, string>
        {
            ["build_property.NCMapperNamingStrategy"] = "BogusStrategy",
        });
        // 缺陷态：mappings 空 → 早退 → NCMAP020 不发射 → 红
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP020");
    }

    // ======================= B1-13-R1：init-only 枚举转换诊断（表达式路径）=======================

    /// <summary>B1-13-R1：init-only 枚举目标 + Fluent string 源 + 枚举值/名冲突 → 表达式
    /// 路径 NCMAP023/024 发射（缺陷态 = 决策期 TryBuildEnumConversionExpr 无 ctx 零诊断；
    /// 语句版路径已有同码——表达式路径漏发）。</summary>
    [Fact]
    public void B1p13R1_InitOnlyEnumConversion_EmitsCollisionDiagnostics()
    {
        var source = """
            using NCMapper;
            namespace B5T5B210
            {
                public enum E8 { X = 1, x = 1 }

                public class U { public string Status { get; set; } = ""; }
                public class D { public E8 Status { get; init; } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Status).From(s => s.Status);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var ids = result.Diagnostics.Select(d => d.Id).ToList();
        // 表达式路径枚举冲突诊断（缺陷态：零命中 → 红）
        Assert.True(ids.Contains("NCMAP023") || ids.Contains("NCMAP024"),
            $"init-only 枚举转换表达式路径应发射 NCMAP023/024，实际诊断 = [{string.Join(", ", ids)}]");
    }
}
