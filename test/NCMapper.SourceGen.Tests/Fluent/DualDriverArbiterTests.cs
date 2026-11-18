using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 双驱动仲裁测试（用例判定表 5 条 + F16 用例组）——
/// 同 (S,T) 对双声明 F11 触发（双位置 + 消息互指 + 两侧不派生）+ 模式并存边界（v1.6 红线）+ 单通道回归
/// + Fluent 通道自身同对重复 F16（Q6 拍板"报错"候选：每处各报一条 + 全抑制派生）+ F11×F16 叠加。
/// </summary>
public class DualDriverArbiterTests
{
    /// <summary>用例 1：[MapFrom] A→ADto + Fluent For&lt;A,ADto&gt; 同对 → F11 ×2 + 两侧快照均不派生</summary>
    [Fact]
    public void SamePair_DualDeclaration_F11_BothSides_Suppressed()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Arb1
            {
                public class A { public int Id { get; set; } }
                public class ADto { public int Id { get; set; } }

                [MapFrom(typeof(A))]
                public class ADtoMarked { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, ADto>().Member(d => d.Id).From(s => s.Id);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        // F11 触发（无同对冲突——ADtoMarked 是不同对）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F11");

        // —— 上例为对照（不同对并存合法）；下面构造真同对冲突 ——
        var conflictSource = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Arb2
            {
                public class A { public int Id { get; set; } }
                [MapFrom(typeof(A))]
                public class ADto { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, ADto>().Member(d => d.Id).From(s => s.Id);
                    }
                }
            }
            """;
        var conflict = TestHelper.GenerateMapper(conflictSource);

        // 用例 1 判定：F11 ×2（MapFrom 位置 + Fluent 位置，消息互指）
        var f11 = conflict.Diagnostics.Where(d => d.Id == "NCMAP_F11").ToList();
        Assert.Equal(2, f11.Count);
        // 消息互指：含 DTO 类名与 Mapper 类名 + (S,T) 对全名
        Assert.All(f11, d =>
        {
            var msg = d.GetMessage();
            Assert.Contains("ADto", msg);
            Assert.Contains("Mapper", msg);
            Assert.Contains("Arb2.A", msg);
            Assert.Contains("Arb2.ADto", msg);
        });
        // 双位置：一条定位在 DTO 类声明（[MapFrom] 侧——TargetIdentifierLocation 重建 external location），
        // 一条定位在 For invocation（Fluent 侧）；external location 无 SourceTree（S3）——以行号区分双侧
        var lines = f11.Select(d => d.Location.GetLineSpan().StartLinePosition.Line).Distinct().ToList();
        Assert.Equal(2, lines.Count);

        // 两侧快照均不派生：生成代码不含任何 A→ADto 方法/注册
        Assert.DoesNotContain("Map_A_To_ADto", conflict.GeneratedCode);
        Assert.DoesNotContain("RegisterGeneratedCore<Arb2.A, Arb2.ADto>", conflict.GeneratedCode);
        // 诊断仍发射（Error 级）
        Assert.All(f11, d => Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, d.Severity));
    }

    /// <summary>用例 2：模式并存（v1.6 边界红线）——两通道各自映射不同 (S,T) 对不触发 F11</summary>
    [Fact]
    public void Coexistence_DifferentPairs_NoF11_BothGenerated()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Arb3
            {
                public class A { public int Id { get; set; } }
                [MapFrom(typeof(A))]
                public class ADto { public int Id { get; set; } }

                public class B { public int Id { get; set; } }
                public class BDto { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<B, BDto>().Member(d => d.Id).From(s => s.Id);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F11");
        // 两通道产物各自派生（P4/Q3 无方法名冲突）
        Assert.Contains("Map_A_To_ADto(", result.GeneratedCode);
        Assert.Contains("Map_B_To_BDto(", result.GeneratedCode);
    }

    /// <summary>用例 3：仅 [MapFrom] 若干对 → 不触发（回归）</summary>
    [Fact]
    public void MapFromOnly_NoF11()
    {
        var source = """
            using NCMapper.Attributes;
            namespace Arb4
            {
                public class A { public int Id { get; set; } }
                [MapFrom(typeof(A))]
                public class ADto { public int Id { get; set; } }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F11");
        Assert.Contains("Map_A_To_ADto(", result.GeneratedCode);
    }

    /// <summary>用例 4：仅 Fluent 若干对 → 不触发（回归）</summary>
    [Fact]
    public void FluentOnly_NoF11()
    {
        var source = """
            using NCMapper;
            namespace Arb5
            {
                public class B { public int Id { get; set; } }
                public class BDto { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<B, BDto>().Member(d => d.Id).From(s => s.Id);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F11");
        // 否定用例⑤：单声明不触发 F16
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F16");
        Assert.Contains("Map_B_To_BDto(", result.GeneratedCode);
    }

    /// <summary>
    /// 用例 5：Fluent 自身同对重复（同 Configure 两链 For&lt;A,B&gt）→ 不触发 NCMAP_F11，
    /// 触发 2×NCMAP_F16（每处声明各一条，Error 级）+ 双快照全抑制派生。
    /// D9 规格变更（Q6 拍板"报错"候选，Q6/D15 已登记）——非 B2 违规。
    /// </summary>
    [Fact]
    public void FluentSelfRepeat_SamePair_NoF11_TwoF16_Suppressed()
    {
        var source = """
            using NCMapper;
            namespace Arb6
            {
                public class A { public int Id { get; set; } public string Name { get; set; } = ""; }
                public class B { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().Member(d => d.Id).From(s => s.Id);
                        cfg.For<A, B>().Member(d => d.Name).Ignore();
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 同侧重复不触发跨通道互斥（F11 只管 MapFrom+Fluent 双声明）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F11");
        // Q6 拍板：同对重复触发 F16 ×2（每处声明各一条）
        var f16 = result.Diagnostics.Where(d => d.Id == "NCMAP_F16").ToList();
        Assert.Equal(2, f16.Count);
        Assert.All(f16, d =>
        {
            Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, d.Severity);
            var msg = d.GetMessage();
            Assert.Contains("Arb6.A", msg);
            Assert.Contains("Arb6.B", msg);
            Assert.Contains("Mapper", msg);
        });
        // 双位置：两处 For invocation 各一条（行号互异——互指达成）
        var lines = f16.Select(d => d.Location.GetLineSpan().StartLinePosition.Line).Distinct().ToList();
        Assert.Equal(2, lines.Count);
        // 全抑制（D6）：两条冲突声明均不派生——无同名派生方法/无注册（CS0111 消失的生成器侧等价断言）
        Assert.DoesNotContain("Map_A_To_B(", result.GeneratedCode);
        Assert.DoesNotContain("RegisterGeneratedCore<Arb6.A, Arb6.B>", result.GeneratedCode);
    }

    /// <summary>
    /// 用例 6：跨 [MapperConfig] 类同对重复 → 2×F16 + 每条消息列出全部冲突方 Mapper 类名（互指）+ 双位置 + 全抑制
    /// </summary>
    [Fact]
    public void FluentCrossClassRepeat_SamePair_TwoF16_AllSuppressed()
    {
        var source = """
            using NCMapper;
            namespace Arb7
            {
                public class A { public int Id { get; set; } public string Name { get; set; } = ""; }
                public class B { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapperConfig]
                public class MapperOne
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().Member(d => d.Id).From(s => s.Id);
                    }
                }

                [MapperConfig]
                public class MapperTwo
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().Member(d => d.Name).Ignore();
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F11");
        var f16 = result.Diagnostics.Where(d => d.Id == "NCMAP_F16").ToList();
        Assert.Equal(2, f16.Count);
        // 消息互指：每条均列出全部冲突方 Mapper 类名（D5 全抑制互指语义）
        Assert.All(f16, d =>
        {
            var msg = d.GetMessage();
            Assert.Contains("MapperOne", msg);
            Assert.Contains("MapperTwo", msg);
            Assert.Contains("Arb7.A", msg);
            Assert.Contains("Arb7.B", msg);
            Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, d.Severity);
        });
        // 双位置：两处 For invocation 各一条
        var lines = f16.Select(d => d.Location.GetLineSpan().StartLinePosition.Line).Distinct().ToList();
        Assert.Equal(2, lines.Count);
        // 全抑制（D6）：跨类冲突的两条声明均不派生
        Assert.DoesNotContain("Map_A_To_B(", result.GeneratedCode);
        Assert.DoesNotContain("RegisterGeneratedCore<Arb7.A, Arb7.B>", result.GeneratedCode);
    }

    /// <summary>用例 7：3 处同对重复（3 个 [MapperConfig] 类）→ 3×F16，每条消息含全部 3 个类名 + 三位置互指</summary>
    [Fact]
    public void FluentTripleRepeat_SamePair_ThreeF16_MessageListsAll()
    {
        var source = """
            using NCMapper;
            namespace Arb8
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class M1
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().Member(d => d.Id).From(s => s.Id);
                    }
                }

                [MapperConfig]
                public class M2
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().Member(d => d.Id).Ignore();
                    }
                }

                [MapperConfig]
                public class M3
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().Member(d => d.Id).From(s => s.Id);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f16 = result.Diagnostics.Where(d => d.Id == "NCMAP_F16").ToList();
        Assert.Equal(3, f16.Count);
        Assert.All(f16, d =>
        {
            var msg = d.GetMessage();
            Assert.Contains("M1", msg);
            Assert.Contains("M2", msg);
            Assert.Contains("M3", msg);
            Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, d.Severity);
        });
        // 三位置：三处 For invocation 各一条
        var lines = f16.Select(d => d.Location.GetLineSpan().StartLinePosition.Line).Distinct().ToList();
        Assert.Equal(3, lines.Count);
        Assert.DoesNotContain("Map_A_To_B(", result.GeneratedCode);
    }

    /// <summary>用例 8（否定）：Fluent 多对不同 (S,T)（模式并存扩展边界）→ 不触发 F16/F11，产物正常派生</summary>
    [Fact]
    public void FluentDifferentPairs_NoF16()
    {
        var source = """
            using NCMapper;
            namespace Arb9
            {
                public class A { public int Id { get; set; } }
                public class ADto { public int Id { get; set; } }
                public class B { public int Id { get; set; } }
                public class BDto { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, ADto>().Member(d => d.Id).From(s => s.Id);
                        cfg.For<B, BDto>().Member(d => d.Id).From(s => s.Id);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F16");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F11");
        Assert.Contains("Map_A_To_ADto(", result.GeneratedCode);
        Assert.Contains("Map_B_To_BDto(", result.GeneratedCode);
    }

    /// <summary>
    /// 用例 9：F11×F16 叠加（1 MapFrom + 2 Fluent 同 key）→ F11 系照发（3 条：2 条 Fluent 侧 +
    /// 1 条 MapFrom 侧——次条因已抑制跳过）+ 2×F16 独立叠加 + 全部声明抑制（D7 幂等口径）
    /// </summary>
    [Fact]
    public void Stacking_MapFromPlusTwoFluent_F11AndF16_BothSuppressed()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Arb10
            {
                public class A { public int Id { get; set; } public string Name { get; set; } = ""; }
                [MapFrom(typeof(A))]
                public class B { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().Member(d => d.Id).From(s => s.Id);
                        cfg.For<A, B>().Member(d => d.Name).Ignore();
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // F11 系照发（逐行保留的既有分支）：2 条 Fluent 侧 + 1 条 MapFrom 侧
        var f11 = result.Diagnostics.Where(d => d.Id == "NCMAP_F11").ToList();
        Assert.Equal(3, f11.Count);
        // F16 独立叠加：2 条（每处 Fluent 声明 1 条）
        var f16 = result.Diagnostics.Where(d => d.Id == "NCMAP_F16").ToList();
        Assert.Equal(2, f16.Count);
        // 全抑制：MapFrom + 两条 Fluent 声明均不派生（CS0111 消失）
        Assert.DoesNotContain("Map_A_To_B(", result.GeneratedCode);
        Assert.DoesNotContain("RegisterGeneratedCore<Arb10.A, Arb10.B>", result.GeneratedCode);
    }

    /// <summary>
    /// 用例 10：抑制精确计数（D4-⑦）——冲突场景生成代码中 "Map_A_To_B" 子串出现次数精确为 0
    ///（覆盖方法定义与一切引用点；对照正向：无冲突 Fluent 单声明生成 ≥1 次——用例 4 固化）
    /// </summary>
    [Fact]
    public void SuppressedPair_DerivedMethodOccurrenceCount_IsExactlyZero()
    {
        var source = """
            using NCMapper;
            namespace Arb11
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class MapperOne
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().Member(d => d.Id).From(s => s.Id);
                    }
                }

                [MapperConfig]
                public class MapperTwo
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().Member(d => d.Id).Ignore();
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var occurrences = result.GeneratedCode.Split("Map_A_To_B").Length - 1;
        Assert.Equal(0, occurrences);
        Assert.DoesNotContain("RegisterGeneratedCore<Arb11.A, Arb11.B>", result.GeneratedCode);
        // 对照：F16 恰 2 条（确认进入的是抑制路径而非提取失败静默）
        Assert.Equal(2, result.Diagnostics.Count(d => d.Id == "NCMAP_F16"));
    }

    // ================================================================
    // B3-16：mapFromIndex 索引双口径回归——
    // value 必须存压缩列表下标（null 跳过后的 mappings 列；消费点以 mappings[idx] 索引）；
    // 原实现存原数组下标，null 混入时错位（F11 派发到错误快照）/越界两个失败模式。
    // ================================================================

    /// <summary>B3-16 回归基座：两 (S,T) 对 [MapFrom] 特性快照 + Fluent 对指定对的显式声明</summary>
    private static (MappingSnapshot attrA, MappingSnapshot attrB, FluentExtractionResult fluent) ExtractB316Fixtures(string forDeclaration)
    {
        var source = $$"""
            using NCMapper;
            using NCMapper.Attributes;
            namespace B316Ns
            {
                public class SrcA { public int Id { get; set; } }
                public class SrcB { public int Id { get; set; } }

                [MapFrom(typeof(SrcA))]
                public class DtoA { public int Id { get; set; } }

                [MapFrom(typeof(SrcB))]
                public class DtoB { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        {{forDeclaration}}
                    }
                }
            }
            """;
        var compilation = TestHelper.BuildCompilation(source);
        var root = compilation.SyntaxTrees.First().GetRoot();

        MappingSnapshot ExtractAttr(string dtoName)
        {
            var dtoDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>().First(t => t.Identifier.Text == dtoName);
            var dtoSymbol = (INamedTypeSymbol)compilation.GetSemanticModel(dtoDecl.SyntaxTree).GetDeclaredSymbol(dtoDecl)!;
            var mapFromAttr = dtoSymbol.GetAttributes().First(a => a.AttributeClass!.ToDisplayString() == "NCMapper.Attributes.MapFromAttribute");
            var sourceSymbol = (INamedTypeSymbol)mapFromAttr.ConstructorArguments[0].Value!;
            return MappingExtractor.ExtractMappingCore(dtoSymbol, sourceSymbol, dtoDecl, CancellationToken.None)!;
        }

        var attrA = ExtractAttr("DtoA");
        var attrB = ExtractAttr("DtoB");

        var mapperDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>().First(t => t.Identifier.Text == "Mapper");
        var mapperSymbol = (INamedTypeSymbol)compilation.GetSemanticModel(mapperDecl.SyntaxTree).GetDeclaredSymbol(mapperDecl)!;
        var fluent = FluentExtractor.ExtractCore(compilation, mapperSymbol, mapperDecl, CancellationToken.None);

        return (attrA, attrB, fluent);
    }

    /// <summary>
    /// B3-16（错位方向）：null 混入（[null, A, null, B]）+ Fluent 与先位 A 冲突——
    /// F11 必须派发到 A（压缩列位 0），B 不受牵连。
    /// 修复前（存原数组下标 1）：mappings[1] = B → B 被错杀、A 漏抑制（静默错位）→ 本用例红。
    /// </summary>
    [Fact]
    public void B316_NullMixedIn_F11_DispatchedToCorrectSnapshot_NotMisaligned()
    {
        var (attrA, attrB, fluent) = ExtractB316Fixtures("cfg.For<SrcA, DtoA>().Member(d => d.Id).From(s => s.Id);");

        var merge = DualDriverArbiter.MergeAndArbitrate(
            System.Collections.Immutable.ImmutableArray.Create<MappingSnapshot?>(null, attrA, null, attrB),
            System.Collections.Immutable.ImmutableArray.Create<FluentExtractionResult?>(fluent),
            CancellationToken.None);

        // 特性侧 A（IsFluentForward=false 区分 Fluent 侧同 ClassName 快照）：抑制 + 快照诊断流 F11
        var dtoA = merge.Mappings.Single(m => m != null && m.TargetClassName == "DtoA" && !m.IsFluentForward)!;
        Assert.True(dtoA.SkipCodeGeneration);
        Assert.Contains(dtoA.Diagnostics, d => d.DescriptorId == "NCMAP_F11");
        // 特性侧 B：不受牵连（无同对冲突——修复前被错位抑制）
        var dtoB = merge.Mappings.Single(m => m != null && m.TargetClassName == "DtoB" && !m.IsFluentForward)!;
        Assert.False(dtoB.SkipCodeGeneration);
        Assert.DoesNotContain(dtoB.Diagnostics, d => d.DescriptorId == "NCMAP_F11");
        // Fluent 侧（通道级流）F11 照发（双位置口径不变）
        Assert.Contains(merge.ExtraDiagnostics, d => d.DescriptorId == "NCMAP_F11");
    }

    /// <summary>
    /// B3-16（越界方向）：null 混入（[null, A, null, B]）+ Fluent 与靠后 B 冲突——
    /// 修复前（存原数组下标 3）：mappings[3] 越出压缩列表长度 2 → ArgumentOutOfRangeException；
    /// 修复后 B（压缩列位 1）正确抑制、A 不动。
    /// </summary>
    [Fact]
    public void B316_NullMixedIn_F11_LaterSnapshot_NoOutOfBounds()
    {
        var (attrA, attrB, fluent) = ExtractB316Fixtures("cfg.For<SrcB, DtoB>().Member(d => d.Id).From(s => s.Id);");

        var merge = DualDriverArbiter.MergeAndArbitrate(
            System.Collections.Immutable.ImmutableArray.Create<MappingSnapshot?>(null, attrA, null, attrB),
            System.Collections.Immutable.ImmutableArray.Create<FluentExtractionResult?>(fluent),
            CancellationToken.None);

        // 特性侧 B：抑制 + 快照诊断流 F11（修复前此处先越界抛异常）
        var dtoB = merge.Mappings.Single(m => m != null && m.TargetClassName == "DtoB" && !m.IsFluentForward)!;
        Assert.True(dtoB.SkipCodeGeneration);
        Assert.Contains(dtoB.Diagnostics, d => d.DescriptorId == "NCMAP_F11");
        // 特性侧 A：不受牵连
        var dtoA = merge.Mappings.Single(m => m != null && m.TargetClassName == "DtoA" && !m.IsFluentForward)!;
        Assert.False(dtoA.SkipCodeGeneration);
        Assert.DoesNotContain(dtoA.Diagnostics, d => d.DescriptorId == "NCMAP_F11");
        // Fluent 侧（通道级流）F11 照发
        Assert.Contains(merge.ExtraDiagnostics, d => d.DescriptorId == "NCMAP_F11");
    }
}
