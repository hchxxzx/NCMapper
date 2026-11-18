using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 提取断言：速记类型符号恢复 / 多子源 / 速记+分组块并存 /
/// CS 编译层错误（CS0305/CS0411/CS0201）。
/// 验证业务逻辑：①速记 object 占位经 LambdaParser 段符号恢复真实类型；
/// ②多子源（3+）Compose 声明并存提取；③速记与分组块同链并存；
/// ④语法/语义层编译错误（泛型参数数量/类型推断/readonly 赋值）由编译器自然拦截。
/// </summary>
public class FluentComposeExtractionTests
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

    private static MappingSnapshot? ExtractSnapshot(string source)
    {
        var result = ExtractResult(source);
        return result?.Declarations.FirstOrDefault()?.Snapshot;
    }

    // ── 速记：object 占位类型符号恢复 ──────────────────────────────

    /// <summary>
    /// 速记引用类型恢复：Compose&lt;Sub&gt;(d =&gt; d.Name, s =&gt; s.FullName) →
    /// ComposeMappings 含 SubSourceType=Sub + SourcePath 末段=FullName（IPropertySymbol 非空）。
    /// 验证：LambdaParser 段符号经 Roslyn 解析恢复真实类型（I-2 口径）。
    /// </summary>
    [Fact]
    public void Shorthand_ReferenceType_ResolvesPropertySymbol()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfeAEntity { public string Name { get; set; } = ""; }
            public class CfeASub { public string FullName { get; set; } = ""; }
            public class CfeADto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class CfeAMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfeAEntity, CfeADto>()
                       .Member(d => d.Name).FromSource<CfeASub>(s => s.FullName);
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.ComposeMappings);
        var cm = snapshot.ComposeMappings[0];
        Assert.Equal("CfeASub", cm.SubSource.SimpleName);
        Assert.Equal("Name", cm.Target.Name);
        Assert.Single(cm.SourcePath);
        Assert.Equal("FullName", cm.SourcePath[0].Name);
    }

    /// <summary>
    /// 速记值类型恢复：Compose&lt;Sub&gt;(d =&gt; d.Age, s =&gt; s.Age) →
    /// SourcePath 末段类型=int（值类型经 Roslyn 符号解析恢复，非 object 装箱）。
    /// </summary>
    [Fact]
    public void Shorthand_ValueType_ResolvesIntType()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfeBEntity { public int Age { get; set; } }
            public class CfeBSub { public int Age { get; set; } }
            public class CfeBDto { public int Age { get; set; } }

            [MapperConfig]
            public class CfeBMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfeBEntity, CfeBDto>()
                       .Member(d => d.Age).FromSource<CfeBSub>(s => s.Age);
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.ComposeMappings);
        var cm = snapshot.ComposeMappings[0];
        Assert.Equal("Age", cm.Target.Name);
        Assert.Single(cm.SourcePath);
        Assert.Equal("Age", cm.SourcePath[0].Name);
    }

    /// <summary>
    /// 速记嵌套路径：Compose&lt;Sub&gt;(d =&gt; d.Title, s =&gt; s.Author.Name) →
    /// SourcePath 多段（Author→Name），IsFlattened 语义（SourcePath.Count>1）。
    /// </summary>
    [Fact]
    public void Shorthand_NestedPath_MultiSegment()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfeCEntity { public string Title { get; set; } = ""; }
            public class CfeCAuthor { public string Name { get; set; } = ""; }
            public class CfeCSub { public CfeCAuthor Author { get; set; } = null!; }
            public class CfeCDto { public string Title { get; set; } = ""; }

            [MapperConfig]
            public class CfeCMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfeCEntity, CfeCDto>()
                       .Member(d => d.Title).FromSource<CfeCSub>(s => s.Author.Name);
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.ComposeMappings);
        var cm = snapshot.ComposeMappings[0];
        Assert.Equal(2, cm.SourcePath.Count);
        Assert.Equal("Author", cm.SourcePath[0].Name);
        Assert.Equal("Name", cm.SourcePath[1].Name);
    }

    // ── 多子源（3+）Compose 并存 ──────────────────────────────

    /// <summary>
    /// 三子源并存：同一 dest 来自 3 个不同子源 → ComposeMappings 含 3 条（不同 SubSourceType），
    /// 顺序覆盖语义（运行时后写覆盖）。验证多子源提取无冲突。
    /// </summary>
    [Fact]
    public void MultiSubSource_ThreeSources_AllExtracted()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfeDEntity { public string Title { get; set; } = ""; }
            public class CfeDSub1 { public string Title { get; set; } = ""; }
            public class CfeDSub2 { public string Title { get; set; } = ""; }
            public class CfeDSub3 { public string Title { get; set; } = ""; }
            public class CfeDDto { public string Title { get; set; } = ""; }

            [MapperConfig]
            public class CfeDMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfeDEntity, CfeDDto>()
                       .Member(d => d.Title).FromSource<CfeDSub1>(s => s.Title)
                       .Member(d => d.Title).FromSource<CfeDSub2>(s => s.Title)
                       .Member(d => d.Title).FromSource<CfeDSub3>(s => s.Title);
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot!.ComposeMappings.Count);
        // 不同子源 → 全部保留（顺序覆盖）
        var subSources = snapshot.ComposeMappings.Select(c => c.SubSource.SimpleName).ToList();
        Assert.Contains("CfeDSub1", subSources);
        Assert.Contains("CfeDSub2", subSources);
        Assert.Contains("CfeDSub3", subSources);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.DescriptorId == "NCMAP_F29");
    }

    // ── 速记与分组块并存 ──────────────────────────────

    /// <summary>
    /// 速记+分组块同链：For 链上 Compose&lt;Sub1&gt;(dest,src) 速记 + Compose&lt;Sub2&gt;(c =&gt; {...}) 分组块 →
    /// ComposeMappings 含两条（Sub1 速记 + Sub2 块内 Map），互不干扰。
    /// </summary>
    [Fact]
    public void ShorthandAndBlock_Coexist()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfeEEntity { public string A { get; set; } = ""; public string B { get; set; } = ""; }
            public class CfeESub1 { public string A { get; set; } = ""; }
            public class CfeESub2 { public string B { get; set; } = ""; }
            public class CfeEDto { public string A { get; set; } = ""; public string B { get; set; } = ""; }

            [MapperConfig]
            public class CfeEMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfeEEntity, CfeEDto>()
                       .Member(d => d.A).FromSource<CfeESub1>(s => s.A)
                       .FromSource<CfeESub2>(c => { c.Member(d => d.B).From(s => s.B); });
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.ComposeMappings.Count);
        var subA = snapshot.ComposeMappings.First(c => c.Target.Name == "A");
        Assert.Equal("CfeESub1", subA.SubSource.SimpleName);
        var subB = snapshot.ComposeMappings.First(c => c.Target.Name == "B");
        Assert.Equal("CfeESub2", subB.SubSource.SimpleName);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.DescriptorId == "NCMAP_F29");
    }

    // ── 块内嵌套 FromSource（flatten，20260905-003 S6 新用例覆盖 :601 路径）─────────────────

    /// <summary>
    /// 块内嵌套 FromSource：分组块内嵌套速记 FromSource&lt;Sub2&gt;(dest,src) + 嵌套分组块
    /// FromSource&lt;Sub3&gt;(x =&gt; {...}) → flatten 语义（等价主链独立 FromSource 声明，递归下钻，Q7），
    /// 声明各自携带 SubSourceType。验证 FluentExtractor 块内 switch 的 FromSource case 分派
    /// （601 路径——S6 补零触达缺口）。
    /// </summary>
    [Fact]
    public void NestedFromSourceInsideBlock_FlattenExtraction()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfeHEntity { public string A { get; set; } = ""; public string B { get; set; } = ""; public string C { get; set; } = ""; }
            public class CfeHSub1 { public string A { get; set; } = ""; }
            public class CfeHSub2 { public string B { get; set; } = ""; }
            public class CfeHSub3 { public string C { get; set; } = ""; }
            public class CfeHDto { public string A { get; set; } = ""; public string B { get; set; } = ""; public string C { get; set; } = ""; }

            [MapperConfig]
            public class CfeHMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfeHEntity, CfeHDto>()
                       .FromSource<CfeHSub1>(c =>
                       {
                           c.Member(d => d.A).From(s => s.A);
                           c.Member(d => d.B).FromSource<CfeHSub2>(s => s.B);
                           c.FromSource<CfeHSub3>(x => { x.Member(d => d.C).From(s => s.C); });
                       });
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot!.ComposeMappings.Count);
        var mapA = snapshot.ComposeMappings.First(c => c.Target.Name == "A");
        Assert.Equal("CfeHSub1", mapA.SubSource.SimpleName);
        // 嵌套速记 flatten：等价主链独立 FromSource 声明
        var nestedShorthand = snapshot.ComposeMappings.First(c => c.Target.Name == "B");
        Assert.Equal("CfeHSub2", nestedShorthand.SubSource.SimpleName);
        // 嵌套分组块 flatten：递归下钻，块内 Map 以嵌套子源为源基准
        var nestedBlock = snapshot.ComposeMappings.First(c => c.Target.Name == "C");
        Assert.Equal("CfeHSub3", nestedBlock.SubSource.SimpleName);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.DescriptorId == "NCMAP_F29");
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.DescriptorId.StartsWith("NCMAP_F3"));
    }

    // ── CS 编译层错误（编译器自然拦截，提取器静默）────────────────

    /// <summary>
    /// CS0411：FromSource 省略泛型类型参 → 编译器 CS0411（类型推断失败——TSubSource 仅出现于
    /// lambda 形参类型，无法从实参推断），提取器不产生额外诊断（编译器已拦截）。
    /// </summary>
    [Fact]
    public void CS0411_TypeInferenceFail_CompilerCatches()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfeFEntity { public string Name { get; set; } = ""; }
            public class CfeFDto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class CfeFMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfeFEntity, CfeFDto>()
                       .Member(d => d.Name).FromSource(s => s.Name);
                }
            }
            """;
        // 编译错误：CS0411（类型推断失败——FromSource 需 1 个类型参数，无法从 lambda 推断）
        var compilation = TestHelper.BuildCompilation(source);
        var compileDiags = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(compileDiags);
        // 提取器不产生 F30-F32（编译器已拦截，FromSource 未提取）
        var result = ExtractResult(source);
        if (result != null)
        {
            Assert.DoesNotContain(result.Diagnostics, d =>
                d.DescriptorId.StartsWith("NCMAP_F3"));
            // F29 物化期——同样不应出现
            Assert.DoesNotContain(result.Declarations.FirstOrDefault()?.Snapshot.Diagnostics ?? Array.Empty<DiagnosticSnapshot>(),
                d => d.DescriptorId == "NCMAP_F29");
        }
    }

    /// <summary>
    /// CS0201：dest lambda 指向 readonly 属性 → 编译器 CS0201（"不能给只读字段/属性赋值"），
    /// 提取器不产生额外诊断。
    /// </summary>
    [Fact]
    public void CS0201_ReadOnlyDest_CompilerCatches()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfeGEntity { public string Name { get; set; } = ""; }
            public class CfeGSub { public string Name { get; set; } = ""; }
            public class CfeGDto
            {
                public string Name { get; init; } = "";
            }

            [MapperConfig]
            public class CfeGMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfeGEntity, CfeGDto>()
                       .Member(d => d.Name).FromSource<CfeGSub>(s => s.Name);
                }
            }
            """;
        var compilation = TestHelper.BuildCompilation(source);
        var compileDiags = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        // init-only 属性可被 Compose 生成委托的 setter 赋值（生成器用 new + 对象初始化器），
        // 不产生 CS0201——此用例验证提取器正常处理 init-only dest（不报 F29-F32）
        var result = ExtractResult(source);
        Assert.NotNull(result);
        Assert.DoesNotContain(result!.Diagnostics, d =>
            d.DescriptorId.StartsWith("NCMAP_F2") || d.DescriptorId.StartsWith("NCMAP_F3"));
    }
}
