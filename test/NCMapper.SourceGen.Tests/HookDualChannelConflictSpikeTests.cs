using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// spike：双通道钩子冲突 F39 + Suppress 字段同步一次性验证
/// （验收 #1/#2/#3；正式矩阵归）。
/// <list type="bullet">
/// <item>验收 #1：F39 双例（Before×Before / After×After）触发，交叉组合不报，
/// 跨对传播同 ref 注入不误报（值非空不覆盖——非冲突）</item>
/// <item>验收 #2：Suppress 重建后 BeforeMappingMethodRef 保留（直接调 MergeAndArbitrate 断言
/// 抑制快照字段——变异演练锚：临时删 DualDriverArbiter.Suppress 同步行应红）</item>
/// <item>验收 #3：F11/F16 既有路径零语义改动（本文件全部用例 F11 计数不变 + 既有仲裁测试全绿）</item>
/// </list>
/// </summary>
public class HookDualChannelConflictSpikeTests
{
    /// <summary>双声明基座：[MapFrom]+[BeforeMapping] DTO × Fluent 链（Before×Before 不同方法）</summary>
    private const string BeforeBeforeSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace HdcBbNs
        {
            public class Src { public int Id { get; set; } }

            [MapFrom(typeof(Src))]
            [BeforeMapping(nameof(AttrGuard))]
            public class Dto
            {
                public int Id { get; set; }
                public static void AttrGuard(Src s, Dto d) { }
            }

            [MapperConfig]
            public class Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<Src, Dto>().BeforeMapping(FluentGuard);
                }
                public static void FluentGuard(Src src, Dto dst) { }
            }
        }
        """;

    /// <summary>双声明基座：[MapFrom]+[AfterMapping] DTO × Fluent 链（After×After 不同方法）</summary>
    private const string AfterAfterSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace HdcAaNs
        {
            public class Src { public int Id { get; set; } }

            [MapFrom(typeof(Src))]
            [AfterMapping(nameof(AttrEnrich))]
            public class Dto
            {
                public int Id { get; set; }
                public static void AttrEnrich(Src s, Dto d) { }
            }

            [MapperConfig]
            public class Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<Src, Dto>().AfterMapping(FluentEnrich);
                }
                public static void FluentEnrich(Src src, Dto dst) { }
            }
        }
        """;

    // ================================================================
    // 验收 #1：F39 双例触发（双位置镜像 F11 口径）
    // ================================================================

    /// <summary>
    /// Before×Before：同对同类钩子并存且 ref 不等 → F39 双位置（Fluent 侧 + 特性侧）；
    /// F11 既有双位置计数不变（零语义改动锚）
    /// </summary>
    [Fact]
    public void F39_BeforeBefore_DualDeclaration_DiagnosedAtBothPositions()
    {
        var result = TestHelper.GenerateMapper(BeforeBeforeSource);

        // 先验锚：F11 双声明既有行为（双位置各一条——零语义改动）
        var f11 = result.Diagnostics.Where(d => d.Id == "NCMAP_F11").ToList();
        Assert.Equal(2, f11.Count);

        // F39 双位置：消息含双侧 ref（Args = [kind, src, tgt, attrRef, fluentRef]）
        var f39 = result.Diagnostics.Where(d => d.Id == "NCMAP_F39").ToList();
        Assert.Equal(2, f39.Count);
        Assert.All(f39, d => Assert.Equal("BeforeMapping", d.GetMessage().Contains("BeforeMapping") ? "BeforeMapping" : d.GetMessage()));
        Assert.Contains(f39, d => d.GetMessage().Contains("HdcBbNs.Dto.AttrGuard"));
        Assert.Contains(f39, d => d.GetMessage().Contains("HdcBbNs.Mapper.FluentGuard"));
    }

    /// <summary>
    /// After×After：同对同类钩子并存且 ref 不等 → F39（After 独立判定）
    /// </summary>
    [Fact]
    public void F39_AfterAfter_DualDeclaration_Diagnosed()
    {
        var result = TestHelper.GenerateMapper(AfterAfterSource);

        Assert.Equal(2, result.Diagnostics.Count(d => d.Id == "NCMAP_F11"));
        var f39 = result.Diagnostics.Where(d => d.Id == "NCMAP_F39").ToList();
        Assert.Equal(2, f39.Count);
        Assert.Contains(f39, d => d.GetMessage().Contains("HdcAaNs.Dto.AttrEnrich"));
        Assert.Contains(f39, d => d.GetMessage().Contains("HdcAaNs.Mapper.FluentEnrich"));
    }

    /// <summary>
    /// 验收 #1（交叉组合不报）：特性 Before + Fluent After → 无 F39（F11 照常）
    /// </summary>
    [Fact]
    public void F39_CrossCombo_AttrBeforeFluentAfter_NotDiagnosed()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace HdcCrossNs
            {
                public class Src { public int Id { get; set; } }

                [MapFrom(typeof(Src))]
                [BeforeMapping(nameof(AttrGuard))]
                public class Dto
                {
                    public int Id { get; set; }
                    public static void AttrGuard(Src s, Dto d) { }
                }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Src, Dto>().AfterMapping(FluentEnrich);
                    }
                    public static void FluentEnrich(Src src, Dto dst) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        Assert.Equal(2, result.Diagnostics.Count(d => d.Id == "NCMAP_F11"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F39");
    }

    /// <summary>
    /// 验收 #1（传播同 ref 不误报）：双声明对 + DTO [BeforeMapping] + Fluent 链未声明自身钩子——
    /// 跨对传播注入的同一 ref（值非空不覆盖）非冲突 → 无 F39
    /// </summary>
    [Fact]
    public void F39_PropagatedSameRef_DualDeclaration_NotDiagnosed()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace HdcPropNs
            {
                public class Src { public int Id { get; set; } }

                [MapFrom(typeof(Src))]
                [BeforeMapping(nameof(AttrGuard))]
                public class Dto
                {
                    public int Id { get; set; }
                    public static void AttrGuard(Src s, Dto d) { }
                }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Src, Dto>().Member(d => d.Id).From(s => s.Id);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        // F11 照常（双声明），F39 不报（同 ref = 传播注入的同一声明）
        Assert.Equal(2, result.Diagnostics.Count(d => d.Id == "NCMAP_F11"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F39");
    }

    // ================================================================
    // 验收 #2：Suppress 重建后 BeforeMappingMethodRef 保留（直接调仲裁器断言抑制快照）
    // ================================================================

    /// <summary>
    /// 抑制重建字段同步：F11 冲突抑制后特性侧快照 BeforeMappingMethodRef 保留（镜像 AfterMappingMethodRef）。
    /// 变异演练锚（#39）：临时删 DualDriverArbiter.Suppress 的 BeforeMappingMethodRef 同步行 → 本用例红 → 还原复绿。
    /// </summary>
    [Fact]
    public void Suppress_Rebuild_RetainsBeforeMappingMethodRef()
    {
        var compilation = TestHelper.BuildCompilation(BeforeBeforeSource);
        var root = compilation.SyntaxTrees.First().GetRoot();

        // 特性侧快照：[MapFrom] DTO 提取
        var dtoDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .First(t => t.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("MapFrom")));
        var dtoSymbol = (INamedTypeSymbol)compilation.GetSemanticModel(dtoDecl.SyntaxTree).GetDeclaredSymbol(dtoDecl)!;
        var mapFromAttr = dtoSymbol.GetAttributes().First(a => a.AttributeClass!.ToDisplayString() == "NCMapper.Attributes.MapFromAttribute");
        var sourceSymbol = (INamedTypeSymbol)mapFromAttr.ConstructorArguments[0].Value!;
        var attrSnapshot = MappingExtractor.ExtractMappingCore(dtoSymbol, sourceSymbol, dtoDecl, CancellationToken.None);
        Assert.Equal("HdcBbNs.Dto.AttrGuard", attrSnapshot!.BeforeMappingMethodRef); // 先验锚

        // Fluent 侧结果：[MapperConfig] 提取（含物化 + 跨对传播）
        var mapperDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .First(t => t.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("MapperConfig")));
        var mapperSymbol = (INamedTypeSymbol)compilation.GetSemanticModel(mapperDecl.SyntaxTree).GetDeclaredSymbol(mapperDecl)!;
        var fluentResult = FluentExtractor.ExtractCore(compilation, mapperSymbol, mapperDecl, CancellationToken.None);

        // 仲裁：同对双声明 → 两侧抑制（各自重建——字段同步双侧验证）
        var merge = DualDriverArbiter.MergeAndArbitrate(
            System.Collections.Immutable.ImmutableArray.Create<MappingSnapshot?>(attrSnapshot),
            System.Collections.Immutable.ImmutableArray.Create<FluentExtractionResult?>(fluentResult),
            CancellationToken.None);

        // 两侧抑制快照均保留各自 BeforeMappingMethodRef（同步行——变异锚：
        // 临时删 DualDriverArbiter.Suppress 的同步行 → 断言红）
        var suppressedAttr = merge.Mappings.Single(m => m != null && m.SkipCodeGeneration
            && m.BeforeMappingMethodRef == "HdcBbNs.Dto.AttrGuard")!;
        Assert.True(suppressedAttr.SkipCodeGeneration);
        Assert.Contains(suppressedAttr.Diagnostics, d => d.DescriptorId == "NCMAP_F11");
        Assert.Contains(suppressedAttr.Diagnostics, d => d.DescriptorId == "NCMAP_F39");

        var suppressedFluent = merge.Mappings.Single(m => m != null && m.SkipCodeGeneration
            && m.BeforeMappingMethodRef == "HdcBbNs.Mapper.FluentGuard")!;
        // Fluent 侧诊断走通道级流（既有 F11 口径）：F11 + F39（Before×Before）齐备
        Assert.Contains(merge.ExtraDiagnostics, d => d.DescriptorId == "NCMAP_F11");
        Assert.Contains(merge.ExtraDiagnostics, d => d.DescriptorId == "NCMAP_F39"
            && d.Args.Count == 5 && d.Args[3] == "HdcBbNs.Dto.AttrGuard" && d.Args[4] == "HdcBbNs.Mapper.FluentGuard");
    }

    // ================================================================
    // 双语资源冒烟（F39 resx 三件套）
    // ================================================================

    /// <summary>F39 neutral（EN）非空 + zh-CN resx 键成对存在</summary>
    [Fact]
    public void F39_BilingualResources_Present()
    {
        Assert.False(string.IsNullOrWhiteSpace(Resources.DiagnosticResources.NCMAP_F39_Message));
        Assert.False(string.IsNullOrWhiteSpace(Resources.DiagnosticResources.NCMAP_F39_Title));

        var zhPath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "NCMapper.SourceGen", "Resources", "DiagnosticResources.zh-CN.resx");
        Assert.True(System.IO.File.Exists(zhPath), $"zh-CN resx 不存在: {zhPath}");
        var zhContent = System.IO.File.ReadAllText(zhPath);
        Assert.Contains("name=\"NCMAP_F39_Message\"", zhContent);
        Assert.Contains("name=\"NCMAP_F39_Title\"", zhContent);
        Assert.Contains("name=\"NCMAP_F39_Description\"", zhContent);
    }
}
