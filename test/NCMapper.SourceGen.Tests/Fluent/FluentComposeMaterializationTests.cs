using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 物化断言：双面向（PropertyMappings + ComposeMappings）/ 分流 /
/// SourcePath 归一 / SourceIndexes 恒空 / 块内 Convert 三字段。
/// 验证业务逻辑：①Compose 命中属性只入 ComposeMappings 不入 PropertyMappings（分流）；
/// ②主源自动匹配属性正常入 PropertyMappings（双面向并存）；
/// ③SourcePath 单段/多段归一；④SourceIndexes 恒空（B4 不支持索引器）；
/// ⑤块内 Convert 三字段（DeclaringType/MethodName/Shape）正确填充。
/// </summary>
public class FluentComposeMaterializationTests
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

    // ── 分流：Compose 命中属性只入 ComposeMappings ──────────────────────────────

    /// <summary>
    /// 分流核心：Compose 命中属性 Name 只入 ComposeMappings，不入 PropertyMappings；
    /// 主源未命中属性（无 Compose 声明）走自动匹配入 PropertyMappings（双面向并存）。
    /// 验证"显式跨源优先于按名匹配"——命中属性不参与主源自动匹配。
    /// </summary>
    [Fact]
    public void ComposeHit_AttributeOnlyInComposeMappings_MainSourceAutoMatchCoexists()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfmAEntity { public string Name { get; set; } = ""; public string Age { get; set; } = ""; }
            public class CfmASub { public string Name { get; set; } = ""; }
            public class CfmADto { public string Name { get; set; } = ""; public string Age { get; set; } = ""; }

            [MapperConfig]
            public class CfmAMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfmAEntity, CfmADto>()
                       .Member(d => d.Name).FromSource<CfmASub>(s => s.Name);
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        // Name 入 ComposeMappings
        Assert.Contains(snapshot!.ComposeMappings, c => c.Target.Name == "Name");
        // Name 不入 PropertyMappings（分流）
        Assert.DoesNotContain(snapshot.PropertyMappings, pm => pm.Target.Name == "Name");
        // Age 无 Compose 声明 → 主源自动匹配入 PropertyMappings
        Assert.Contains(snapshot.PropertyMappings, pm => pm.Target.Name == "Age");
    }

    // ── 双面向并存 ──────────────────────────────

    /// <summary>
    /// 双面向：同一 Fluent 声明下，主源属性（Age）入 PropertyMappings、
    /// 子源属性（Name）入 ComposeMappings，两者并存于同一 MappingSnapshot。
    /// </summary>
    [Fact]
    public void DualFacing_PropertyMappingsAndComposeMappingsCoexist()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfmBEntity { public string Age { get; set; } = ""; }
            public class CfmBSub { public string Name { get; set; } = ""; }
            public class CfmBDto { public string Age { get; set; } = ""; public string Name { get; set; } = ""; }

            [MapperConfig]
            public class CfmBMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfmBEntity, CfmBDto>()
                       .Member(d => d.Name).FromSource<CfmBSub>(s => s.Name);
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.PropertyMappings);
        Assert.Equal("Age", snapshot.PropertyMappings[0].Target.Name);
        Assert.Single(snapshot.ComposeMappings);
        Assert.Equal("Name", snapshot.ComposeMappings[0].Target.Name);
    }

    // ── D-1 消费：Block-Mask/FromJson 物化（评审 P1-3/P1-4）──────────

    /// <summary>
    /// 去降级后物化正向：块内 .Member(dest).Mask()/.FromJson() 与嵌套块内 .Member(dest).Mask() 声明进入物化分流，
    /// cm 承载 MaskType/IsFromJson（commit① 负向对照的翻转，G2 L1 快照级判据）。
    /// <para>同 dest（Phone）命中 Compose 分流不入 PropertyMappings（分流）；Mask 按名解析
    /// 子源同名属性（binding fallback 口径）。</para>
    /// </summary>
    [Fact]
    public void Consumed_BlockMaskFromJson_CmCarriesMaskTypeAndIsFromJson()
    {
        var source = """
            using NCMapper;
            using NCMapper.Masks;
            using System.Linq.Expressions;
            public class CfmMEntity { public string Name { get; set; } = ""; public string Phone { get; set; } = ""; }
            public class CfmMSub { public string Phone { get; set; } = ""; public string Config { get; set; } = ""; }
            public class CfmMNested { public string Email { get; set; } = ""; }
            public class CfmMConfig { public int Id { get; set; } public string Level { get; set; } = ""; }
            public class CfmMDto { public string Name { get; set; } = ""; public string Phone { get; set; } = ""; public CfmMConfig? Config { get; set; } public string Email { get; set; } = ""; }

            [MapperConfig]
            public class CfmMMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfmMEntity, CfmMDto>()
                       .FromSource<CfmMSub>(c =>
                       {
                           c.Member(d => d.Phone).Mask<MaskPhone>();
                           c.Member(d => d.Config).FromJson();
                           c.FromSource<CfmMNested>(x => { x.Member(d => d.Email).Mask<MaskEmail>(); });
                       });
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        // Phone：块内 Mask 声明产 cm，MaskType 承载（符号态），按名解析子源同名属性
        var phone = Assert.Single(snapshot!.ComposeMappings, c => c.Target.Name == "Phone");
        Assert.Equal("CfmMSub", phone.SubSource.SimpleName);
        Assert.NotNull(phone.MaskType);
        Assert.Equal("MaskPhone", phone.MaskType!.SimpleName);
        Assert.False(phone.IsFromJson);
        Assert.Single(phone.SourcePath);
        Assert.Equal("Phone", phone.SourcePath[0].Name);
        // 分流：Phone 命中 Compose 不入 PropertyMappings（主链自动匹配被跳过）
        Assert.DoesNotContain(snapshot.PropertyMappings, pm => pm.Target.Name == "Phone");
        // Config：块内 FromJson 声明产 cm，IsFromJson 承载
        var config = Assert.Single(snapshot.ComposeMappings, c => c.Target.Name == "Config");
        Assert.True(config.IsFromJson);
        Assert.Null(config.MaskType);
        // Email：嵌套块内 Mask 声明 flatten 后产 cm（块 switch 递归 :604 入口复用 :540）
        var email = Assert.Single(snapshot.ComposeMappings, c => c.Target.Name == "Email");
        Assert.Equal("CfmMNested", email.SubSource.SimpleName);
        Assert.NotNull(email.MaskType);
        Assert.Equal("MaskEmail", email.MaskType!.SimpleName);
        // 提取层零诊断误报
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.DescriptorId.StartsWith("NCMAP_F"));
    }

    /// <summary>
    /// 同 dest Map+Mask 合并语义（评审 P1-4）：binding 优先取带路径 Map 声明（显式点路径），
    /// Mask 修饰型全组遍历合并写 cm.MaskType——两声明**同时生效**，与声明顺序无关。
    /// 顺序反转（Mask 先入组）断言同一形态，钉死 FirstOrDefault 盲选缺口已消除。
    /// </summary>
    [Theory]
    [InlineData("mapFirst")]
    [InlineData("maskFirst")]
    public void Consumed_MapPlusMask_MergeOrderIndependent(string order)
    {
        var mapLine = "c.Member(d => d.Phone).From(s => s.Phone);";
        var maskLine = "c.Member(d => d.Phone).Mask<MaskPhone>();";
        var first = order == "mapFirst" ? mapLine : maskLine;
        var second = order == "mapFirst" ? maskLine : mapLine;
        var source = $$"""
            using NCMapper;
            using NCMapper.Masks;
            using System.Linq.Expressions;
            public class CfmNEntity { public string Name { get; set; } = ""; }
            public class CfmNSub { public string Phone { get; set; } = ""; }
            public class CfmNDto { public string Name { get; set; } = ""; public string Phone { get; set; } = ""; }

            [MapperConfig]
            public class CfmNMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfmNEntity, CfmNDto>()
                       .FromSource<CfmNSub>(c =>
                       {
                           {{first}}
                           {{second}}
                       });
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        // 合并形态：Map 显式路径 + Mask 脱敏并存于同一 cm（两断言同时成立 = 顺序无关）
        var phone = Assert.Single(snapshot!.ComposeMappings, c => c.Target.Name == "Phone");
        Assert.Single(phone.SourcePath);
        Assert.Equal("Phone", phone.SourcePath[0].Name);
        Assert.NotNull(phone.MaskType);
        Assert.Equal("MaskPhone", phone.MaskType!.SimpleName);
        // 单 cm（合并而非双 cm），零诊断
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.DescriptorId.StartsWith("NCMAP_F"));
    }

    // ── SourcePath 归一（续） ──────────────────────────────

    /// <summary>
    /// SourcePath 单段归一：速记单段 src（s.Name）→ SourcePath 单元素（Name）。
    /// </summary>
    [Fact]
    public void SourcePath_SingleSegment_SingleElement()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfmCEntity { public string Name { get; set; } = ""; }
            public class CfmCSub { public string Name { get; set; } = ""; }
            public class CfmCDto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class CfmCMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfmCEntity, CfmCDto>()
                       .Member(d => d.Name).FromSource<CfmCSub>(s => s.Name);
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        var cm = snapshot!.ComposeMappings[0];
        Assert.Single(cm.SourcePath);
        Assert.Equal("Name", cm.SourcePath[0].Name);
        Assert.Equal("Name", cm.SourcePath[0].Name);
    }

    /// <summary>
    /// SourcePath 多段归一：速记嵌套 src（s.Author.Name）→ SourcePath 两元素（Author, Name）。
    /// </summary>
    [Fact]
    public void SourcePath_MultiSegment_MultiElement()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfmDEntity { public string Title { get; set; } = ""; }
            public class CfmDAuthor { public string Name { get; set; } = ""; }
            public class CfmDSub { public CfmDAuthor Author { get; set; } = null!; }
            public class CfmDDto { public string Title { get; set; } = ""; }

            [MapperConfig]
            public class CfmDMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfmDEntity, CfmDDto>()
                       .Member(d => d.Title).FromSource<CfmDSub>(s => s.Author.Name);
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        var cm = snapshot!.ComposeMappings[0];
        Assert.Equal(2, cm.SourcePath.Count);
        Assert.Equal("Author", cm.SourcePath[0].Name);
        Assert.Equal("Name", cm.SourcePath[1].Name);
    }

    // ── SourceIndexes 恒空（B4）─────────────────────────────

    /// <summary>
    /// SourceIndexes 恒空：Fluent 通道 Compose 不支持索引器 → SourceIndexes.Count=0 + HasIndex=false。
    /// </summary>
    [Fact]
    public void SourceIndexes_AlwaysEmpty_B4()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfmEEntity { public string Name { get; set; } = ""; }
            public class CfmESub { public string Name { get; set; } = ""; }
            public class CfmEDto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class CfmEMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfmEEntity, CfmEDto>()
                       .Member(d => d.Name).FromSource<CfmESub>(s => s.Name);
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        var cm = snapshot!.ComposeMappings[0];
        Assert.Empty(cm.SourceIndexes);
        Assert.False(cm.HasIndex);
    }

    // ── 块内 Convert 三字段 ──────────────────────────────

    /// <summary>
    /// 块内 Convert 三字段：分组块 c.Convert(d.Tag, Conv.ToText) → ComposeMappings 携带
    /// ConverterDeclaringType（Conv FQN）/ ConverterMethodName（ToText）/ ConverterShape（T1）。
    /// 验证块内 Convert 经 ResolveConvertMethod 解析产物正确注入（对照 ProcessComposeMapping :326-331）。
    /// </summary>
    [Fact]
    public void BlockConvert_ThreeFields_Populated()
    {
        var source = """
            using NCMapper;
            using System.Linq.Expressions;
            public class CfmFEntity { public string Tag { get; set; } = ""; }
            public class CfmFSub { public string Tag { get; set; } = ""; }
            public class CfmFDto { public string Tag { get; set; } = ""; }
            public static class CfmFConv { public static string ToText(string v) => "v:" + v; }

            [MapperConfig]
            public class CfmFMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<CfmFEntity, CfmFDto>()
                       .FromSource<CfmFSub>(c => { c.Member(d => d.Tag).Convert<string>(CfmFConv.ToText); });
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.ComposeMappings);
        var cm = snapshot.ComposeMappings[0];
        // 三字段裸名口径（与特性侧 [Convert] 一致——MappingExtractor.cs:1304 存 ToDisplayString()，
        // global:: 由发射端 CodeGenerator.ConverterCall.cs:28 内联拼接，不存于快照）
        Assert.Equal("CfmFConv", cm.ConverterDeclaringType);
        Assert.Equal("ToText", cm.ConverterMethodName);
        Assert.Equal(ConverterShape.T1SingleValue, cm.ConverterShape);
        // 转换器源路径来自提取期解析产物
        Assert.Single(cm.SourcePath);
        Assert.Equal("Tag", cm.SourcePath[0].Name);
    }
}
