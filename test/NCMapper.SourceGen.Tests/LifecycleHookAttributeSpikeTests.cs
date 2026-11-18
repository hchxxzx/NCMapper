using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// spike：特性侧生命周期钩子（[BeforeMapping]/[AfterMapping]）
/// 提取/解析/承载/重建透传/跨对传播一次性验证（正式矩阵归，发射层归
/// 降级分类断言归联动）。
/// <list type="bullet">
/// <item>验收 #1：双 ctor 解析（单参=DTO 自身 / 双参=宿主类型）+ NCMAP038 不可解析含双参指引</item>
/// <item>验收 #2：[MapFrom]+[BeforeMapping]+[AfterMapping] 快照两字段填充</item>
/// <item>验收 #3：ApplyGlobalConfig 重建路径钩子字段不丢（透传清单）</item>
/// <item>验收 #4：跨对传播——签名适用对注入、不适用对不注入不发诊断（D-d）</item>
/// <item>验收 #5：Inherited=false 派生 DTO 不继承钩子（D-j）</item>
/// <item>验收 #8 冒烟：两特性 IProjectionPolicy.RequiresDowngrade=true（白名单关系——
/// 自声明降级路径，不入 s_projectionIrrelevantAttrs 豁免清单）</item>
/// </list>
/// </summary>
public class LifecycleHookAttributeSpikeTests
{
    /// <summary>
    /// 从源码构建编译并直调 ExtractMappingCore 提取 [MapFrom] 通道映射快照
    /// （首个 [MapFrom] 标注类型为 DTO；主源类型取其构造参数[0]符号）
    /// </summary>
    private static MappingSnapshot? ExtractSnapshot(string source)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var typeDecl = compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("MapFrom")))!;
        var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);
        var targetSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        var mapFromAttr = targetSymbol!.GetAttributes().First(a =>
            a.AttributeClass?.ToDisplayString() == "NCMapper.Attributes.MapFromAttribute");
        var sourceType = (INamedTypeSymbol)mapFromAttr.ConstructorArguments[0].Value!;
        return MappingExtractor.ExtractMappingCore(targetSymbol!, sourceType, typeDecl, CancellationToken.None);
    }

    /// <summary>
    /// Fluent 通道整链提取（FluentExtractor.ExtractCore → MappingSnapshotBuilder.Build 物化 +
    /// 跨对传播）——首个 [MapperConfig] 类为锚
    /// </summary>
    private static FluentExtractionResult? ExtractFluent(string source)
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

    // ================================================================
    // 验收 #1 + #2：happy path（单参/双参）+ 两字段填充
    // ================================================================

    /// <summary>
    /// 验收 #2：[MapFrom] + 单参双钩子（方法声明于 DTO 自身）→ 快照两字段填充
    /// </summary>
    [Fact]
    public void BothHooks_SingleCtorOnDto_FilledIntoSnapshot()
    {
        var source = """
            using NCMapper.Attributes;
            namespace LhsNs
            {
                public class Src { public int Id { get; set; } }

                [MapFrom(typeof(Src))]
                [BeforeMapping(nameof(Before))]
                [AfterMapping(nameof(After))]
                public class Dto
                {
                    public int Id { get; set; }
                    public static void Before(Src s, Dto d) { }
                    public static void After(Src s, Dto d) { }
                }
            }
            """;
        var snapshot = ExtractSnapshot(source)!;

        Assert.Equal("LhsNs.Dto.Before", snapshot.BeforeMappingMethodRef);
        Assert.Equal("LhsNs.Dto.After", snapshot.AfterMappingMethodRef);
        // 解析全成功：无钩子族诊断
        Assert.DoesNotContain(snapshot.Diagnostics, d =>
            d.DescriptorId is "NCMAP038" or "NCMAP039" or "NCMAP040");
    }

    /// <summary>
    /// 验收 #1：双参 ctor 指向宿主类型 → 解析到宿主方法（跨实体复用形态）
    /// </summary>
    [Fact]
    public void BothHooks_DualCtorHostType_Resolved()
    {
        var source = """
            using NCMapper.Attributes;
            namespace LhdNs
            {
                public class Src { public int Id { get; set; } }
                public class Other { public int Id { get; set; } }

                public static class HookHost
                {
                    public static void Normalize(Src s, Dto d) { }
                    public static void Finalize(Dto d, Other o) { }
                }

                [MapFrom(typeof(Src))]
                [BeforeMapping(typeof(HookHost), nameof(HookHost.Normalize))]
                [AfterMapping(typeof(HookHost), nameof(HookHost.Finalize))]
                public class Dto
                {
                    public int Id { get; set; }
                }
            }
            """;
        var snapshot = ExtractSnapshot(source)!;

        Assert.Equal("LhdNs.HookHost.Normalize", snapshot.BeforeMappingMethodRef);
        // After 宿主方法签名 void(Dto, Other)——第 2 参与映射对 (Src, Dto) 不兼容 → NCMAP039（签名不匹配）
        Assert.Null(snapshot.AfterMappingMethodRef);
        var ncmap039 = Assert.Single(snapshot.Diagnostics, d => d.DescriptorId == "NCMAP039");
        Assert.Equal("Finalize", ncmap039.Args[1]);
        Assert.Contains("expected void(LhdNs.Src, LhdNs.Dto)", ncmap039.Args[2]);
    }

    // ================================================================
    // 验收 #1 负向：NCMAP038/039/040
    // ================================================================

    /// <summary>
    /// NCMAP038：单参形态方法不在 DTO 自身 → 报错且消息含锚定类型信息（双参改写指引载体）
    /// </summary>
    [Fact]
    public void NCMAP038_MethodNotOnDto_Diagnosed()
    {
        var source = """
            using NCMapper.Attributes;
            namespace Lh38Ns
            {
                public class Src { public int Id { get; set; } }

                [MapFrom(typeof(Src))]
                [BeforeMapping(nameof(Ghost))]
                public class Dto
                {
                    public int Id { get; set; }
                }
            }
            """;
        var snapshot = ExtractSnapshot(source)!;

        Assert.Null(snapshot.BeforeMappingMethodRef);
        var ncmap038 = Assert.Single(snapshot.Diagnostics, d => d.DescriptorId == "NCMAP038");
        // Args[0]=attributeName / [1]=methodName / [2]=anchorName（消息含改写指引）
        Assert.Contains("BeforeMapping", ncmap038.Args[0]);
        Assert.Contains("Ghost", ncmap038.Args[1]);
        Assert.Contains("Lh38Ns.Dto", ncmap038.Args[2]);
    }

    /// <summary>
    /// NCMAP039：方法存在但签名不匹配（void 返回/2 参形态之一不符）→ 诊断含期望签名与候选明细
    /// </summary>
    [Fact]
    public void NCMAP039_SignatureMismatch_DiagnosedWithCandidates()
    {
        var source = """
            using NCMapper.Attributes;
            namespace Lh39Ns
            {
                public class Src { public int Id { get; set; } }

                [MapFrom(typeof(Src))]
                [AfterMapping(nameof(Bad))]
                public class Dto
                {
                    public int Id { get; set; }
                    public static int Bad(Src s, Dto d) => 0;
                }
            }
            """;
        var snapshot = ExtractSnapshot(source)!;

        Assert.Null(snapshot.AfterMappingMethodRef);
        var ncmap039 = Assert.Single(snapshot.Diagnostics, d => d.DescriptorId == "NCMAP039");
        // Args[2] = "expected void(Src, Dto); accessible candidates: ..."
        var detail = string.Join("|", ncmap039.Args);
        Assert.Contains("expected void(Lh39Ns.Src, Lh39Ns.Dto)", detail);
        Assert.Contains("candidates", detail);
    }

    /// <summary>
    /// NCMAP040：多重载签名兼容且无精确匹配（void(基类, Dto) 与 void(Src, 基类) 各自兼容非精确）
    /// </summary>
    [Fact]
    public void NCMAP040_AmbiguousCompatibleOverloads_Diagnosed()
    {
        var source = """
            using NCMapper.Attributes;
            namespace Lh40Ns
            {
                public class BaseSrc { public int Id { get; set; } }
                public class Src : BaseSrc { public int Extra { get; set; } }

                [MapFrom(typeof(Src))]
                [AfterMapping(nameof(Hook))]
                public class Dto
                {
                    public int Id { get; set; }
                    public static void Hook(BaseSrc s, Dto d) { }
                    public static void Hook(Src s, object d) { }
                }
            }
            """;
        var snapshot = ExtractSnapshot(source)!;

        Assert.Null(snapshot.AfterMappingMethodRef);
        var ncmap040 = Assert.Single(snapshot.Diagnostics, d => d.DescriptorId == "NCMAP040");
        Assert.Contains("Hook", ncmap040.Args[1]);
    }

    // ================================================================
    // 验收 #5：Inherited=false（D-j）
    // ================================================================

    /// <summary>
    /// 派生 DTO 不继承基类钩子（Inherited=false + GetAttributes 只见自身声明）：
    /// 基类 [BeforeMapping] 钩子方法签名 void(A, BaseB) 对派生对 (A, DerivedB) 签名兼容——
    /// 若实现遍历基类特性会误注入（本测试防的就是这个回归）；正确行为 = 不注入
    /// </summary>
    [Fact]
    public void DerivedDto_FluentPair_DoesNotInheritBaseHook()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace LhInhNs
            {
                public class A { public int Id { get; set; } }

                [BeforeMapping(nameof(Hook))]
                public class BaseB
                {
                    public int Id { get; set; }
                    public static void Hook(A src, BaseB dst) { }
                }

                public class DerivedB : BaseB
                {
                    public string Extra { get; set; } = "";
                }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, DerivedB>().Member(d => d.Id).From(s => s.Id);
                    }
                }
            }
            """;
        var result = ExtractFluent(source)!;

        var decl = Assert.Single(result.Declarations);
        // 派生对目标 DerivedB：基类钩子不随派生对生效（签名兼容也不注入——Inherited=false）
        Assert.Null(decl.Snapshot.BeforeMappingMethodRef);
        Assert.DoesNotContain(result.Diagnostics, d =>
            d.DescriptorId is "NCMAP038" or "NCMAP039" or "NCMAP040");
    }

    // ================================================================
    // 验收 #3：重建透传（ApplyGlobalConfig 快照重建字段不丢）
    // ================================================================

    /// <summary>
    /// 全局配置重建路径：钩子两字段经 SnapshotConfig 透传清单复制（007 缺陷②同型防线）
    /// </summary>
    [Fact]
    public void GlobalConfigRebuild_PreservesHookFields()
    {
        var source = """
            using NCMapper.Attributes;
            namespace LhRbNs
            {
                public class Src { public int Id { get; set; } }

                [MapFrom(typeof(Src))]
                [BeforeMapping(nameof(Before))]
                [AfterMapping(nameof(After))]
                public class Dto
                {
                    public int Id { get; set; }
                    public static void Before(Src s, Dto d) { }
                    public static void After(Src s, Dto d) { }
                }
            }
            """;
        var snapshot = ExtractSnapshot(source)!;

        // 非 null 配置触发重建（IgnoreNames 非空 = IsEmpty false）
        var config = new GlobalConfigSnapshot { IgnoreNames = ["Password"] };
        var rebuilt = MappingExtractor.ApplyGlobalConfig(
            System.Collections.Immutable.ImmutableArray.Create<MappingSnapshot?>(snapshot), config, CancellationToken.None);

        var rebuiltSnapshot = Assert.Single(rebuilt)!;
        Assert.Equal("LhRbNs.Dto.Before", rebuiltSnapshot.BeforeMappingMethodRef);
        Assert.Equal("LhRbNs.Dto.After", rebuiltSnapshot.AfterMappingMethodRef);
    }

    // ================================================================
    // 验收 #4：跨对传播（纯 Fluent 对注入——D-d 按签名适用逐对生效）
    // ================================================================

    /// <summary>
    /// 签名适用对注入：DTO（无 [MapFrom]）带单参 [BeforeMapping]，Fluent 声明对
    /// For&lt;A, B&gt;() 物化期回读注入
    /// </summary>
    [Fact]
    public void CrossPairPropagation_ApplicableSignature_InjectedIntoFluentPair()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace LhCpNs
            {
                public class A { public int Id { get; set; } }

                [BeforeMapping(nameof(Guard))]
                public class B
                {
                    public int Id { get; set; }
                    public static void Guard(A src, B dst) { }
                }

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
        var result = ExtractFluent(source)!;

        var decl = Assert.Single(result.Declarations);
        // 先验锚：声明物化成功（非 vacuous）
        Assert.False(decl.Snapshot.SkipCodeGeneration);
        // 跨对传播注入（Fluent 声明自身无 BeforeMapping——值来自 B 的类型级特性回读）
        Assert.Equal("LhCpNs.B.Guard", decl.Snapshot.BeforeMappingMethodRef);
        // 全程零诊断（声明所在对不在特性通道——D-d 静默路径）
        Assert.DoesNotContain(result.Diagnostics, d =>
            d.DescriptorId is "NCMAP038" or "NCMAP039" or "NCMAP040");
    }

    /// <summary>
    /// 签名不适用对不注入不发诊断（D-d 正常过滤路径）：钩子方法签名 void(C, B) 与
    /// 映射对 (A, B) 源侧不兼容 → 不注入、零诊断
    /// </summary>
    [Fact]
    public void CrossPairPropagation_InapplicableSignature_NotInjectedSilent()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace LhCp2Ns
            {
                public class A { public int Id { get; set; } }
                public class C { public int Id { get; set; } }

                [BeforeMapping(nameof(Guard))]
                public class B
                {
                    public int Id { get; set; }
                    public static void Guard(C src, B dst) { }
                }

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
        var result = ExtractFluent(source)!;

        var decl = Assert.Single(result.Declarations);
        // 签名不适用：不注入（ref 保持 null）
        Assert.Null(decl.Snapshot.BeforeMappingMethodRef);
        // 不发诊断（D-d：不适用是正常过滤，非错误）
        Assert.DoesNotContain(result.Diagnostics, d =>
            d.DescriptorId is "NCMAP038" or "NCMAP039" or "NCMAP040");
        Assert.DoesNotContain(decl.Snapshot.Diagnostics, d =>
            d.DescriptorId is "NCMAP038" or "NCMAP039" or "NCMAP040");
    }

    /// <summary>
    /// Fluent 显式声明值优先：Fluent 链 .BeforeMapping(...) 与 DTO 特性钩子并存时
    /// Fluent 值不被特性回读覆盖（值非空不覆盖；双通道重复声明仲裁归）
    /// </summary>
    [Fact]
    public void CrossPairPropagation_FluentExplicitDeclaration_TakesPrecedence()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace LhCp3Ns
            {
                public class A { public int Id { get; set; } }

                [BeforeMapping(nameof(AttrGuard))]
                public class B
                {
                    public int Id { get; set; }
                    public static void AttrGuard(A src, B dst) { }
                }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<A, B>().BeforeMapping(FluentGuard);
                    }

                    public static void FluentGuard(A src, B dst) { }
                }
            }
            """;
        var result = ExtractFluent(source)!;

        var decl = Assert.Single(result.Declarations);
        // Fluent 显式声明值保留（非空不覆盖——特性侧值不窜入）
        Assert.Equal("LhCp3Ns.Mapper.FluentGuard", decl.Snapshot.BeforeMappingMethodRef);
    }

    // ================================================================
    // 验收 #8 冒烟：IProjectionPolicy 自声明降级（不入白名单的关系锚）
    // ================================================================

    /// <summary>
    /// 两特性实现 IProjectionPolicy 且 RequiresDowngrade=true（投影相关——走自声明降级路径，
    /// 不入 s_projectionIrrelevantAttrs 豁免清单；完整降级断言归）
    /// </summary>
    [Fact]
    public void BothAttributes_DeclareRequiresDowngrade()
    {
        var before = new NCMapper.Attributes.BeforeMappingAttribute("M");
        var after = new NCMapper.Attributes.AfterMappingAttribute("M");
        Assert.True(((NCMapper.IProjectionPolicy)before).RequiresDowngrade);
        Assert.True(((NCMapper.IProjectionPolicy)after).RequiresDowngrade);
    }
}
