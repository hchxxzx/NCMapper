using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// Compose 副源字段 [Mask]/[IsFromJson] 数据承载测试。
/// <para>
/// 覆盖：①ComposeMappingSnapshot 新字段（MaskType/IsFromJson）参与相等性/哈希（Q7 敏感性，
/// 验收 #2）；②提取→物化端到端——已校验镜像通道落地（验收 #4）；③校验行为面——无效副源
/// [Mask] 报既有 NCMAP014/015（作用面扩展，不新增规则）；④IsFromJson 值类型守卫镜像主源
/// （源/目标含非 string 值类型 → 整条丢弃）。
/// </para>
/// <para>入口：MappingExtractor.ExtractMappingCore 直调（K2 拆分既有直调入口）。</para>
/// </summary>
public class ComposeSubSourceSnapshotTests
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

    // ================================================================
    // ① Q7 敏感性：ComposeMappingSnapshot 新字段参与相等性/哈希
    // ================================================================

    /// <summary>
    /// 仅 MaskType 不同的两个 ComposeMappingSnapshot 判不等（增量缓存正确性——
    /// 「改副源 [Mask] 脱敏类型」的增量失效不漏判）
    /// </summary>
    [Fact]
    public void ComposeSnapshot_MaskType_ParticipatesInEquality()
    {
        var maskType = new TypeRef { FullName = "M", EqualityKey = "M", MinimallyQualifiedName = "M" };
        var type = new TypeRef { FullName = "T", EqualityKey = "T", MinimallyQualifiedName = "T" };
        var prop = new PropertyRef { Name = "X", Type = type };

        var withMask = new ComposeMappingSnapshot
        {
            SubSource = type,
            Source = prop,
            Target = prop,
            SourcePath = new[] { prop },
            MaskType = maskType
        };
        var withoutMask = new ComposeMappingSnapshot
        {
            SubSource = type,
            Source = prop,
            Target = prop,
            SourcePath = new[] { prop },
            MaskType = null
        };
        var withMask2 = new ComposeMappingSnapshot
        {
            SubSource = type,
            Source = prop,
            Target = prop,
            SourcePath = new[] { prop },
            MaskType = maskType
        };

        Assert.NotEqual(withMask, withoutMask);
        Assert.NotEqual(withMask.GetHashCode(), withoutMask.GetHashCode());
        Assert.Equal(withMask, withMask2);
    }

    /// <summary>
    /// 仅 IsFromJson 不同的两个 ComposeMappingSnapshot 判不等
    /// </summary>
    [Fact]
    public void ComposeSnapshot_IsFromJson_ParticipatesInEquality()
    {
        var type = new TypeRef { FullName = "T", EqualityKey = "T", MinimallyQualifiedName = "T" };
        var prop = new PropertyRef { Name = "X", Type = type };

        var withJson = new ComposeMappingSnapshot
        {
            SubSource = type,
            Source = prop,
            Target = prop,
            SourcePath = new[] { prop },
            IsFromJson = true
        };
        var withoutJson = new ComposeMappingSnapshot
        {
            SubSource = type,
            Source = prop,
            Target = prop,
            SourcePath = new[] { prop },
            IsFromJson = false
        };

        Assert.NotEqual(withJson, withoutJson);
        Assert.NotEqual(withJson.GetHashCode(), withoutJson.GetHashCode());
    }

    // ================================================================
    // ② 物化端到端：提取读注 + 已校验镜像通道落地（验收 #4）
    // ================================================================

    /// <summary>
    /// 副源字段 [Mask]（有效：string 目标 + IMask 实现）→ cm.MaskType 物化非空
    /// </summary>
    [Fact]
    public void ComposeSubSourceMask_MaterializesValidatedMaskType()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class CsmPrimary { public int Id { get; set; } }
            public class CsmSub { public string Phone { get; set; } = ""; }
            [MapFrom(typeof(CsmPrimary))]
            public class CsmDto
            {
                public int Id { get; set; }
                [MapProperty(typeof(CsmSub), nameof(CsmSub.Phone))]
                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; } = "";
            }
            """;

        var snapshot = ExtractSnapshot(source);

        Assert.NotNull(snapshot);
        var cm = Assert.Single(snapshot!.ComposeMappings);
        Assert.Equal("Phone", cm.Target.Name);
        // 已校验镜像通道落地：MaskType 物化非空（TypeRef 全名 = MaskPhone）
        Assert.NotNull(cm.MaskType);
        Assert.Equal("NCMapper.MaskPhone", cm.MaskType!.FullName);
        // 主源字段不受影响
        Assert.Contains(snapshot.PropertyMappings, pm => pm.Target.Name == "Id");
    }

    /// <summary>
    /// 副源字段 [MapProperty(IsFromJson=true)]（string→对象形态，守卫通过）→ cm.IsFromJson 物化 true
    /// </summary>
    [Fact]
    public void ComposeSubSourceIsFromJson_MaterializesFlag()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class CsmJPrimary { public int Id { get; set; } }
            public class CsmJSub { public string Payload { get; set; } = ""; }
            public class CsmJDetail { public string Value { get; set; } = ""; }
            [MapFrom(typeof(CsmJPrimary))]
            public class CsmJDto
            {
                public int Id { get; set; }
                [MapProperty(typeof(CsmJSub), nameof(CsmJSub.Payload), IsFromJson = true)]
                public CsmJDetail Payload { get; set; } = new();
            }
            """;

        var snapshot = ExtractSnapshot(source);

        Assert.NotNull(snapshot);
        var cm = Assert.Single(snapshot!.ComposeMappings);
        Assert.Equal("Payload", cm.Target.Name);
        Assert.True(cm.IsFromJson);
    }

    // ================================================================
    // ③ 校验行为面：无效副源 [Mask] 报既有 NCMAP014/015（作用面扩展）
    // ================================================================

    /// <summary>
    /// 副源字段 [Mask] 目标非 string → NCMAP015（MaskTargetNotString）+ MaskType 不置位
    /// </summary>
    [Fact]
    public void ComposeSubSourceMask_NonStringTarget_ReportsNCMAP015()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class CsmNPrimary { public int Id { get; set; } }
            public class CsmNSub { public int Code { get; set; } }
            [MapFrom(typeof(CsmNPrimary))]
            public class CsmNDto
            {
                public int Id { get; set; }
                [MapProperty(typeof(CsmNSub), nameof(CsmNSub.Code))]
                [Mask(typeof(MaskPhone))]
                public int Code { get; set; }
            }
            """;

        var snapshot = ExtractSnapshot(source);

        Assert.NotNull(snapshot);
        var cm = Assert.Single(snapshot!.ComposeMappings);
        // 校验失败不置位（已校验镜像通道——仅校验通过才写入）
        Assert.Null(cm.MaskType);
        // 快照诊断经 GeneratorDriver 输出——此处断言物化后诊断条目（DescriptorId）
        Assert.Contains(snapshot.Diagnostics, d => d.DescriptorId == "NCMAP015");
    }

    /// <summary>
    /// 副源字段 [Mask] 类型非 IMask → NCMAP014（MaskTypeMismatch）+ MaskType 不置位
    /// </summary>
    [Fact]
    public void ComposeSubSourceMask_NonIMaskType_ReportsNCMAP014()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class CsmMPrimary { public int Id { get; set; } }
            public class CsmMSub { public string Phone { get; set; } = ""; }
            public class NotAMask { }
            [MapFrom(typeof(CsmMPrimary))]
            public class CsmMDto
            {
                public int Id { get; set; }
                [MapProperty(typeof(CsmMSub), nameof(CsmMSub.Phone))]
                [Mask(typeof(NotAMask))]
                public string Phone { get; set; } = "";
            }
            """;

        var snapshot = ExtractSnapshot(source);

        Assert.NotNull(snapshot);
        var cm = Assert.Single(snapshot!.ComposeMappings);
        Assert.Null(cm.MaskType);
        Assert.Contains(snapshot.Diagnostics, d => d.DescriptorId == "NCMAP014");
    }

    // ================================================================
    // ④ IsFromJson 值类型守卫镜像主源（整条丢弃）
    // ================================================================

    /// <summary>
    /// 副源字段 [MapProperty(IsFromJson=true)] 源为非 string 值类型 → 整条丢弃
    /// （镜像主源 MappingExtractor.cs:546-560 行为：不入 ComposeMappings）
    /// </summary>
    [Fact]
    public void ComposeSubSourceIsFromJson_ValueTypeSource_DropsMapping()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class CsmVPrimary { public int Id { get; set; } }
            public class CsmVSub { public int Count { get; set; } }
            [MapFrom(typeof(CsmVPrimary))]
            public class CsmVDto
            {
                public int Id { get; set; }
                [MapProperty(typeof(CsmVSub), nameof(CsmVSub.Count), IsFromJson = true)]
                public string Count { get; set; } = "";
            }
            """;

        var snapshot = ExtractSnapshot(source);

        Assert.NotNull(snapshot);
        // 值类型守卫：源 int 非 string → 整条丢弃（ComposeMappings 空）
        Assert.Empty(snapshot!.ComposeMappings);
    }
}
