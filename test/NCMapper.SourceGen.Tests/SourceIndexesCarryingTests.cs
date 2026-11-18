// （B5-4）Commit A 红测——SourceIndexes 双承载面（快照重建 / 元数据发射）：
//   B3-15：ReplaceWithConfigAwareMatchValue 重建初始化器（MappingExtractor.SnapshotConfig.cs:296-321）
//          补 SourceIndexes = pm.SourceIndexes——单段带索引 + 配置感知重匹配换源重建时索引
//          静默丢失（四道前置挡板 :249/:260/:261/:262 通过形态）；
//   B1-04：SnapshotMetadataEmitter.cs:290 发射改等长 -1 占位编码（X1：与 SourcePath 等长、
//          无索引段 -1、维持 :288 if (pm.HasIndex) 门、禁改 Runtime 类型）——过滤式发射
//          （Where(i => i.HasValue)）丢段位置对齐：[null, 1, null] 发成 [1]。
// 修复前形态：断言用例必红；修复 commit 后转绿；回退修复行 → 复红（验收① 机械锚）。
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

public class SourceIndexesCarryingTests
{
    // ================================================================
    // B3-15：单段带索引穿透重建（值域 ApplyGlobalConfig 公开入口——
    // LifecycleHookAttributeSpikeTests 验收 #3 同驱动形态）
    // ================================================================

    /// <summary>值域手构 TypeRef（FullName=EqualityKey 同键）。</summary>
    private static TypeRef MakeType(string fullName) => new()
    {
        FullName = fullName,
        EqualityKey = fullName,
        NonNullableFullName = fullName,
        SimpleName = fullName.Substring(fullName.LastIndexOf('.') + 1),
        Namespace = fullName.Substring(0, fullName.LastIndexOf('.')),
    };

    private static readonly TypeRef StringType = MakeType("System.String");

    /// <summary>值域手构 PropertyRef（仅 Name/Type/ContainingType 参与重建路径）。</summary>
    private static PropertyRef MakeProp(string name, TypeRef containingType) => new()
    {
        Name = name,
        Type = StringType,
        ContainingType = containingType,
    };

    /// <summary>
    /// B3-15 主样例：单段带索引 pm 经配置感知重匹配换源重建——SourceIndexes 必须透传。
    /// <para>场景：源类型含两个仅大小写不同的属性（先声明 tags、后声明 Tags），目标属性 Tags；
    /// 原始 pm 绑定 tags（默认 IgnoreCase 提取形态）且携带单段索引 [2]；
    /// 配置 NamingStrategy=1（CaseSensitive）→ FindPropertyValue 按序数精确命中 Tags →
    /// SameProperty(tags, Tags) 判否 → 重建分支触发（四道挡板全过：无 MapProperty/
    /// 无 FluentExplicit/单段/有源）。</para>
    /// </summary>
    [Fact]
    public void ConfigAwareRebuild_SingleSegmentIndexed_PreservesSourceIndexes()
    {
        var sourceType = MakeType("B315Ns.Src");
        var targetType = MakeType("B315Ns.Dto");
        var tagsRef = MakeProp("tags", sourceType);
        var tagsUpperRef = MakeProp("Tags", sourceType);
        var targetRef = MakeProp("Tags", targetType);

        var pm = new PropertyMappingSnapshot
        {
            Target = targetRef,
            Source = tagsRef,
            SourcePath = new[] { tagsRef },    // 单段（挡板③ SourcePath.Count == 1）
            SourceIndexes = new int?[] { 2 },  // B3-15：单段带索引（HasIndex = true）
            IsFlattened = false,
            PathOrigin = PathOrigin.Direct,
        };
        var snapshot = new MappingSnapshot
        {
            SourceType = sourceType,
            TargetType = targetType,
            SourceClassName = "Src",
            TargetClassName = "Dto",
            PropertyMappings = new[] { pm },
            AllTargetProperties = new[] { targetRef },
            SourceProperties = new[] { tagsRef, tagsUpperRef }, // 声明序：tags 在前、Tags 在后
        };

        // NamingStrategy=1（CaseSensitive）：IsEmpty=false 触发重建；FindPropertyValue 按序数
        // 精确查 "Tags" → 命中后声明的 Tags（Ordinal 字典下与 tags 不同键）
        var config = new GlobalConfigSnapshot { NamingStrategy = 1 };
        var rebuilt = MappingExtractor.ApplyGlobalConfig(
            ImmutableArray.Create<MappingSnapshot?>(snapshot), config, CancellationToken.None);

        var rebuiltPm = Assert.Single(Assert.Single(rebuilt)!.PropertyMappings);
        // 先验锚：重建分支确实触发（源由 tags 换绑 Tags——非 SameProperty 换源重建；
        // 若未触发则 Source.Name 仍为 tags，锚先行失败提示场景形态漂移）
        Assert.Equal("Tags", rebuiltPm.Source!.Name);
        Assert.Equal("Tags", rebuiltPm.SourcePath[0].Name);
        // 主断言（B3-15）：SourceIndexes 随重建透传——修复前重建初始化器不赋值 →
        // 默认空列表 → 红
        Assert.Equal(new int?[] { 2 }, rebuiltPm.SourceIndexes);
    }

    /// <summary>
    /// B3-15 同属性对照（现状不变式锚）：重匹配命中同属性（SameProperty）→ 保留原 pm 不重建
    /// → 索引天然保留。修复只触碰重建分支，本路径行为不变（修复前后均绿）。
    /// </summary>
    [Fact]
    public void ConfigAwareRebuild_SameSourceProperty_KeepsOriginalPmWithIndexes()
    {
        var sourceType = MakeType("B315Ns2.Src");
        var targetType = MakeType("B315Ns2.Dto");
        var tagsRef = MakeProp("Tags", sourceType);
        var targetRef = MakeProp("Tags", targetType);

        var pm = new PropertyMappingSnapshot
        {
            Target = targetRef,
            Source = tagsRef,
            SourcePath = new[] { tagsRef },
            SourceIndexes = new int?[] { 3 },
            IsFlattened = false,
            PathOrigin = PathOrigin.Direct,
        };
        var snapshot = new MappingSnapshot
        {
            SourceType = sourceType,
            TargetType = targetType,
            SourceClassName = "Src",
            TargetClassName = "Dto",
            PropertyMappings = new[] { pm },
            AllTargetProperties = new[] { targetRef },
            SourceProperties = new[] { tagsRef },
        };

        var config = new GlobalConfigSnapshot { NamingStrategy = 1 };
        var rebuilt = MappingExtractor.ApplyGlobalConfig(
            ImmutableArray.Create<MappingSnapshot?>(snapshot), config, CancellationToken.None);

        var rebuiltPm = Assert.Single(Assert.Single(rebuilt)!.PropertyMappings);
        // 同属性（Name + ContainingType 同键）→ SameProperty 保留原 pm
        Assert.Equal("Tags", rebuiltPm.Source!.Name);
        Assert.Equal(new int?[] { 3 }, rebuiltPm.SourceIndexes);
    }

    // ================================================================
    // B1-04：等长 -1 占位发射（X1 契约——维持 :288 if (pm.HasIndex) 门）
    // ================================================================

    private const string RuntimeSnapshotFileName = "NCMapper_RuntimeSnapshotMetadata.g.cs";

    /// <summary>
    /// B1-04 主样例：段首索引 + 尾段纯属性（s.Items[0].Sku → SourceIndexes = [0, null]）。
    /// 修复前过滤式发射 new int[] { 0 }（[null] 丢弃）；修复后等长占位 new int[] { 0, -1 }。
    /// </summary>
    private const string LeadingIndexSource = """
        using NCMapper;
        using System.Collections.Generic;
        namespace B104Lead
        {
            public class B1ItemE { public string Sku { get; set; } = ""; }
            public class B1Entity
            {
                public int Id { get; set; }
                public List<B1ItemE> Items { get; set; } = new();
            }
            public class B1Dto
            {
                public int Id { get; set; }
                public string FirstSku { get; set; } = "";
            }

            [MapperConfig]
            public class B1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<B1Entity, B1Dto>()
                       .Member(d => d.FirstSku).From(s => s.Items[0].Sku)
                       .Member(d => d.Id).From(s => s.Id);
                }
            }
        }
        """;

    /// <summary>
    /// B1-04 位置样例：中段索引（s.Holder.Mids[1].Tag → SourceIndexes = [null, 1, null]）。
    /// 修复前过滤式发射 new int[] { 1 }（首/尾 null 丢弃——段位置对齐丢失坐实面）；
    /// 修复后等长占位 new int[] { -1, 1, -1 }。
    /// </summary>
    private const string MidIndexSource = """
        using NCMapper;
        using System.Collections.Generic;
        namespace B104Mid
        {
            public class B1MidE { public string Tag { get; set; } = ""; }
            public class B1Holder
            {
                public System.Collections.Generic.List<B1MidE> Mids { get; set; } = new();
            }
            public class B1MEntity
            {
                public int Id { get; set; }
                public B1Holder Holder { get; set; } = new();
            }
            public class B1MDto
            {
                public int Id { get; set; }
                public string DeepTag { get; set; } = "";
            }

            [MapperConfig]
            public class B1MMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<B1MEntity, B1MDto>()
                       .Member(d => d.DeepTag).From(s => s.Holder.Mids[1].Tag);
                }
            }
        }
        """;

    /// <summary>段首索引样例：等长 -1 占位发射（B1-04 主断言——修复前过滤式 → 红）。</summary>
    [Fact]
    public void FluentLeadingIndexSegment_EmitsEqualLengthMinusOneEncoding()
    {
        var driver = GenerateDriverWithSwitch(LeadingIndexSource, emitEnabled: true);
        Assert.True(HasGeneratedTree(driver, RuntimeSnapshotFileName), "Fluent 索引段样例应发射快照元数据");
        var code = GetGeneratedCode(driver, RuntimeSnapshotFileName);

        // 先验锚：链路可达（HasIndex 置位 + 双段路径名）
        Assert.Contains("HasIndex = true,", code);
        Assert.Contains("SourcePathNames = new string[] { \"Items\", \"Sku\" }", code);
        // 主断言（B1-04 / X1 契约）：SourceIndexes 与 SourcePath 等长——无索引段以 -1 占位
        // （[0, null] → [0, -1]）；修复前过滤式 new int[] { 0 } → 红
        Assert.Contains("SourceIndexes = new int[] { 0, -1 },", code);
        // 过滤式形态不得残存（修复后完备锚——同文本下 Contains/DoesNotContain 双向锁形）
        Assert.DoesNotContain("SourceIndexes = new int[] { 0 },", code);
    }

    /// <summary>段首索引样例：发射门维持（X1——纯无索引 pair 不新增 SourceIndexes 行）。</summary>
    [Fact]
    public void FluentLeadingIndexSegment_EmissionGate_UnindexedPairStaysSilent()
    {
        var driver = GenerateDriverWithSwitch(LeadingIndexSource, emitEnabled: true);
        var code = GetGeneratedCode(driver, RuntimeSnapshotFileName);

        // LeadingIndexSource 含两个 Map：FirstSku（带索引）+ Id（同名直映无索引）——
        // 维持 :288 if (pm.HasIndex) 门：仅 HasIndex pair 落 SourceIndexes 行（Id pair 不落）
        var emissionCount = code.Split(new[] { "SourceIndexes = " }, StringSplitOptions.None).Length - 1;
        Assert.Equal(1, emissionCount);
    }

    /// <summary>中段索引样例：等长 -1 占位保段位置（[null, 1, null] → [-1, 1, -1]）。</summary>
    [Fact]
    public void FluentMidIndexSegment_EmitsEqualLengthMinusOneEncoding()
    {
        var driver = GenerateDriverWithSwitch(MidIndexSource, emitEnabled: true);
        Assert.True(HasGeneratedTree(driver, RuntimeSnapshotFileName), "Fluent 中段索引样例应发射快照元数据");
        var code = GetGeneratedCode(driver, RuntimeSnapshotFileName);

        // 先验锚：链路可达（HasIndex 置位 + 三段路径名）
        Assert.Contains("HasIndex = true,", code);
        Assert.Contains("SourcePathNames = new string[] { \"Holder\", \"Mids\", \"Tag\" }", code);
        // 主断言（B1-04）：中段索引 [null, 1, null] → 等长占位 [-1, 1, -1]——
        // 修复前过滤式 new int[] { 1 }（首尾 null 丢弃）→ 红
        Assert.Contains("SourceIndexes = new int[] { -1, 1, -1 },", code);
        Assert.DoesNotContain("SourceIndexes = new int[] { 1 },", code);
    }

    // ---- 驱动辅助（RuntimeSnapshotEmitterTests 同款形态）----

    /// <summary>运行 NCMapperSourceGenerator + RuntimeSnapshotGenerator 双生成器，注入发射开关。</summary>
    private static GeneratorDriver GenerateDriverWithSwitch(string source, bool emitEnabled)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var generators = ImmutableArray.Create<ISourceGenerator>(
            new NCMapperSourceGenerator().AsSourceGenerator(),
            new RuntimeSnapshotGenerator().AsSourceGenerator());
        var optionsProvider = new EmitSwitchOptionsProvider(emitEnabled);
        var driver = CSharpGeneratorDriver.Create(
            generators,
            additionalTexts: ImmutableArray<AdditionalText>.Empty,
            parseOptions: CSharpParseOptions.Default,
            optionsProvider: optionsProvider);
        return driver.RunGenerators(compilation);
    }

    private static string GetGeneratedCode(GeneratorDriver driver, string fileName)
        => driver.GetRunResult().GeneratedTrees
            .SingleOrDefault(t => Path.GetFileName(t.FilePath) == fileName)?.ToString() ?? string.Empty;

    private static bool HasGeneratedTree(GeneratorDriver driver, string fileName)
        => driver.GetRunResult().GeneratedTrees
            .Any(t => Path.GetFileName(t.FilePath) == fileName);

    private sealed class EmitSwitchOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly bool _emitEnabled;
        public EmitSwitchOptionsProvider(bool emitEnabled) => _emitEnabled = emitEnabled;
        public override AnalyzerConfigOptions GlobalOptions => new Opts(_emitEnabled);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Opts(_emitEnabled);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Opts(_emitEnabled);
        private sealed class Opts : AnalyzerConfigOptions
        {
            private readonly bool _emitEnabled;
            public Opts(bool emitEnabled) => _emitEnabled = emitEnabled;
            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.NCMapperEmitRuntimeSnapshot")
                {
                    value = _emitEnabled ? "true" : "false";
                    return true;
                }
                value = string.Empty;
                return false;
            }
        }
    }
}
