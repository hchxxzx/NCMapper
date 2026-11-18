using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// spike：Fluent 通道 BeforeMapping 提取链一次性验证（验收 #1/#2——
/// 正式矩阵归，F37 构造器一体化形态归发射层判定，本文件不覆盖）。
/// <list type="bullet">
/// <item>验收 #1：.BeforeMapping(方法组) 链声明经 FluentExtractor 提取 → MappingSnapshotBuilder
/// 物化桥接 → MappingSnapshot.BeforeMappingMethodRef 值断言（提取链全通；评审 002 B-1 遗漏点 #1
/// 桥接的检出锚）</item>
/// <item>验收 #2：F35（private 可见性契约）/F36（lambda 方法组边界）/F38（FromSource 块内禁止）
/// 各触发 1 例——每个负向用例携带"声明确实被提取"先验锚（#67 反 vacuous pass）</item>
/// <item>验收 #3（否定）：无 BeforeMapping 声明的对 ref 恒 null（[MapFrom] 通道产物零输出 G3 前置）</item>
/// </list>
/// </summary>
public class FluentBeforeMappingSpikeTests
{
    private static FluentExtractionResult? ExtractResult(string source)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var typeDecl = compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("MapperConfig")))!;
        var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        return FluentExtractor.ExtractCore(compilation, typeSymbol!, typeDecl, CancellationToken.None);
    }

    /// <summary>
    /// 验收 #1：.BeforeMapping(方法组) 声明提取 → FluentSnapshot → 物化桥接 → MappingSnapshot
    /// （ref 值化形态 "{ContainingType.ToDisplayString()}.{Name}"）
    /// </summary>
    [Fact]
    public void BeforeMapping_MethodGroup_ParsedIntoMaterializedSnapshot()
    {
        var source = """
            using NCMapper;
            namespace FbmNs
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().BeforeMapping(ApplyGuard);
                    }

                    public static void ApplyGuard(A src, B dst) { }
                }
            }
            """;
        var result = ExtractResult(source)!;

        // 先验锚：链声明确实被提取（非 vacuous）
        var decl = Assert.Single(result.Declarations);
        // 物化快照承载（提取 → FluentSnapshot → MappingSnapshotBuilder 桥接全链）
        Assert.Equal("FbmNs.Mapper.ApplyGuard", decl.Snapshot.BeforeMappingMethodRef);
        // 通道无提取期诊断（方法组合法解析）
        Assert.DoesNotContain(result.Diagnostics, d => d.DescriptorId.StartsWith("NCMAP_F"));
    }

    /// <summary>
    /// 验收 #3（否定）：无 BeforeMapping 声明的对 ref 恒 null（G3 前置——无钩子声明零承载）
    /// </summary>
    [Fact]
    public void NoBeforeMapping_Declaration_RefStaysNull()
    {
        var source = """
            using NCMapper;
            namespace FbmPlainNs
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().Member(d => d.Id).From(s => s.Id);
                    }
                }
            }
            """;
        var result = ExtractResult(source)!;

        var decl = Assert.Single(result.Declarations);
        Assert.Null(decl.Snapshot.BeforeMappingMethodRef);
    }

    /// <summary>
    /// 验收 #2：F35——BeforeMapping 目标方法 private static → 编译期诊断（可见性契约 B-1b，镜像 F12）
    /// </summary>
    [Fact]
    public void F35_BeforeMapping_PrivateTarget_Diagnosed()
    {
        var source = """
            using NCMapper;
            namespace FbmF35Ns
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().BeforeMapping(PrivateGuard);
                    }

                    private static void PrivateGuard(A src, B dst) { }
                }
            }
            """;
        var result = ExtractResult(source)!;

        // 先验锚：链声明确实被提取（private 方法组可绑定、链未静默丢弃）
        Assert.Single(result.Declarations);
        var f35 = result.Diagnostics.Where(d => d.DescriptorId == "NCMAP_F35").ToList();
        Assert.Single(f35);
        Assert.Contains("PrivateGuard", Assert.Single(f35[0].Args));
        // 失败语义：ref 不承载（诊断已发即抑制声明）
        Assert.Null(result.Declarations[0].Snapshot.BeforeMappingMethodRef);
    }

    /// <summary>
    /// 验收 #2：F36——BeforeMapping 目标为 lambda → 编译期诊断（S-1b 方法组边界，镜像 F14）
    /// </summary>
    [Fact]
    public void F36_BeforeMapping_Lambda_Diagnosed()
    {
        var source = """
            using NCMapper;
            namespace FbmF36Ns
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().BeforeMapping((src, dst) => { dst.Id = src.Id; });
                    }
                }
            }
            """;
        var result = ExtractResult(source)!;

        // 先验锚：链声明确实被提取
        Assert.Single(result.Declarations);
        Assert.Single(result.Diagnostics, d => d.DescriptorId == "NCMAP_F36");
        Assert.Null(result.Declarations[0].Snapshot.BeforeMappingMethodRef);
    }

    /// <summary>
    /// 验收 #2：F38——FromSource 块内 .BeforeMapping → 编译期诊断（块内禁止，镜像 F32）
    /// </summary>
    [Fact]
    public void F38_BlockBeforeMapping_Diagnosed()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class FbmEntity { public string Tag { get; set; } = ""; }
            public class FbmSub { public string Tag { get; set; } = ""; }
            public class FbmDto { public string Tag { get; set; } = ""; }

            [MapperConfig]
            public class FbmMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FbmEntity, FbmDto>()
                       .FromSource<FbmSub>(c => { c.BeforeMapping((s, d) => { }); });
                }
            }
            """;
        var result = ExtractResult(source)!;

        // 先验锚：链声明确实被提取（FromSource 块被解析）
        Assert.Single(result.Declarations);
        Assert.Single(result.Diagnostics, d => d.DescriptorId == "NCMAP_F38");
    }

    /// <summary>
    /// F38 邻界反例：主链 .BeforeMapping(方法组)（非 FromSource 块内）→ 不报 NCMAP_F38
    /// </summary>
    [Fact]
    public void MainChain_BeforeMapping_NoF38()
    {
        var source = """
            using NCMapper;
            namespace FbmOkNs
            {
                public class A { public int Id { get; set; } }
                public class B { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().BeforeMapping(Guard);
                    }

                    public static void Guard(A src, B dst) { }
                }
            }
            """;
        var result = ExtractResult(source)!;

        Assert.Single(result.Declarations);
        Assert.DoesNotContain(result.Diagnostics, d => d.DescriptorId == "NCMAP_F38");
    }
}
