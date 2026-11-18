// 发射端独立身份登记 + 明细段新承载字段测试
//   - 身份段（NCMapper_FluentSnapshotRegistry.g.cs）：Fluent 对 on/off 双态恒发射（不受 EmitRuntimeSnapshot 控制）；
//     仅 Fluent 通道条目；身份级最小字段；排序确定性；总开关关闭同消（R-A）
//   - 明细段 additive 字段（SourceIgnoreDirection/IsPropertyNoAutoInclude/SourceIndexes）：条件发射——
//     默认值零痕迹（既有产物字节不变，F1 门）
//  - 双写合并：on 态身份段 + 明细段同 (S,T) 对双笔登记（RuntimeSnapshotStore.Register 幂等合并收敛——R-B 契约承接）
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 发射端测试——身份登记输出节点 + 明细段新承载字段条件发射
/// </summary>
public class RuntimeSnapshotIdentityEmitterTests
{
    private const string IdentityFileName = "NCMapper_FluentSnapshotRegistry.g.cs";
    private const string DetailFileName = "NCMapper_RuntimeSnapshotMetadata.g.cs";

    // Fluent 通道语料（两对——验证仅 Fluent 对入身份段 + 排序确定性）
    private const string FluentSource = """
        using NCMapper;
        namespace EmitIdent
        {
            public class AEntity { public int Id { get; set; } }
            public class ADto { public int Id { get; set; } }
            public class BEntity { public int Id { get; set; } }
            public class BDto { public int Id { get; set; } }

            [MapperConfig]
            public class ZMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<BEntity, BDto>();
                    cfg.For<AEntity, ADto>();
                }
            }
        }
        """;

    // MapFrom 通道语料（不入身份段——显式 off 态走反射回退）
    private const string MapFromSource = """
        using NCMapper.Attributes;
        namespace EmitIdentMf
        {
            public class MfEntity { public int Id { get; set; } }
            [MapFrom(typeof(MfEntity))]
            public class MfDto { public int Id { get; set; } }
        }
        """;

    // Fluent 方向性忽略（明细段发射对照锚——T 侧锚 .Ignore；源侧
    // SourceIgnoreDirection 发射段退役后本 fixture 保留为 A-11 承载侧对照）
    private const string FluentDirectionalIgnore = """
        using NCMapper;
        namespace EmitIdentIg
        {
            public class IgEntity { public int Id { get; set; } }
            public class IgDto { public int Id { get; set; } public string InternalNote { get; set; } = ""; }

            [MapperConfig]
            public class IgMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<IgEntity, IgDto>().Member(d => d.InternalNote).Ignore(NCMapper.Attributes.Direction.ToDto);
                }
            }
        }
        """;

    // [MapFrom] 通道 FromField 索引路径（明细段 SourceIndexes/HasIndex 条件发射锚）
    private const string MapFromIndexSource = """
        using System.Collections.Generic;
        using NCMapper.Attributes;
        namespace EmitIdentIdx
        {
            public class IdxItemE { public string Title { get; set; } = ""; }
            public class IdxEntity { public List<IdxItemE> Items { get; set; } = new(); }

            [MapFrom(typeof(IdxEntity))]
            public class IdxDto
            {
                [MapProperty(typeof(IdxEntity), "Items[0].Title")]
                public string FirstTitle { get; set; } = "";
            }
        }
        """;

    /// <summary>身份段：Fluent 对在 EmitRuntimeSnapshot on/off 双态恒发射（不受开关控制）+ 内容为身份级最小登记</summary>
    [Fact]
    public void FluentPair_IdentityEmitted_OnBothEmitSwitchStates()
    {
        foreach (var emitEnabled in new[] { true, false })
        {
            var driver = GenerateDriver(FluentSource, emitEnabled: emitEnabled, sourceGenEnabled: true);

            Assert.True(HasGeneratedTree(driver, IdentityFileName),
                $"EmitRuntimeSnapshot={emitEnabled} 时身份登记文件应发射（G1-① 显式 off 态兜底数据源）");

            var code = GetGeneratedCode(driver, IdentityFileName);
            Assert.Contains("RuntimeSnapshotStore.Register(new global::NCMapper.Runtime.RuntimeMapperMetadata", code);
            Assert.Contains("Channel = global::NCMapper.Runtime.RuntimeMappingChannel.Fluent,", code);
            Assert.Contains("IsFluentForward = true,", code);

            // 身份级最小登记：Properties/AllTargetSlots/明细字段零痕迹（明细由开关内明细段承载）
            Assert.DoesNotContain("Properties = new", code);
            Assert.DoesNotContain("HasMask", code);
        }
    }

    /// <summary>身份段：仅 Fluent 通道条目——[MapFrom] 对不在身份段发射（两对 Fluent 语料 → 恰两笔 Register）</summary>
    [Fact]
    public void IdentitySection_ContainsOnlyFluentChannelEntries()
    {
        var driver = GenerateDriver(FluentSource, emitEnabled: true, sourceGenEnabled: true);
        var code = GetGeneratedCode(driver, IdentityFileName);

        Assert.Equal(2, code.Split(new[] { "RuntimeSnapshotStore.Register(" }, StringSplitOptions.None).Length - 1);
    }

    /// <summary>身份段：[MapFrom] 通道语料 → 零身份产物（EmitRuntimeSnapshot off 态走反射回退，行为不变）</summary>
    [Fact]
    public void MapFromPair_NoIdentityFile()
    {
        var driver = GenerateDriver(MapFromSource, emitEnabled: true, sourceGenEnabled: true);

        Assert.False(HasGeneratedTree(driver, IdentityFileName),
            "[MapFrom] 通道对不应入身份段");
    }

    /// <summary>身份段排序确定性：声明序（B 先 A 后）≠ 产物序（按 (SourceFqn, TargetFqn) 序）且同输入两跑字节相同</summary>
    [Fact]
    public void IdentitySection_OrderingDeterministic()
    {
        var first = GetGeneratedCode(GenerateDriver(FluentSource, emitEnabled: true, sourceGenEnabled: true), IdentityFileName);
        var second = GetGeneratedCode(GenerateDriver(FluentSource, emitEnabled: true, sourceGenEnabled: true), IdentityFileName);

        Assert.Equal(first, second, ignoreCase: false, ignoreLineEndingDifferences: false);
        var aPos = first.IndexOf("pair 0: EmitIdent.AEntity", StringComparison.Ordinal);
        var bPos = first.IndexOf("pair 1: EmitIdent.BEntity", StringComparison.Ordinal);
        Assert.True(aPos >= 0 && bPos > aPos, "身份段须按 (SourceFqn, TargetFqn) 序发射（声明序 B→A 不影响产物序）");
    }

    /// <summary>总开关关闭（R-A）：身份文件与 GeneratedMapper 同消；既有明细段发射行为不变（X5 additive 纪律）</summary>
    [Fact]
    public void TotalSwitchOff_IdentitySuppressed_DetailStaysAsIs()
    {
        var driver = GenerateDriver(FluentSource, emitEnabled: true, sourceGenEnabled: false);

        Assert.False(HasGeneratedTree(driver, IdentityFileName), "总开关关闭时身份文件须消失");
        Assert.True(HasGeneratedTree(driver, DetailFileName), "既有明细段行为不变（X5——总开关关闭不影响明细段现状）");
    }

    /// <summary>明细段方向性忽略锚：Fluent .Member(dest).Ignore(Direction) 声明不阻断身份段发射；
    /// （方向物化进目标槽/反向派生语义属 A-11 等价域；实体侧 SourceIgnoreDirection 承载随物化路径落地——差异登记）</summary>
    [Fact]
    public void DetailSegment_FluentDirectionalIgnore_DoesNotBlockIdentity()
    {
        var driver = GenerateDriver(FluentDirectionalIgnore, emitEnabled: true, sourceGenEnabled: true);

        Assert.True(HasGeneratedTree(driver, IdentityFileName), "Fluent 忽略声明对照常入身份段");
        Assert.True(HasGeneratedTree(driver, DetailFileName), "明细段照常发射");
    }

    /// <summary>明细段双通道补发（决策点 5）：[MapFrom] 通道
    /// FromField 索引路径 → SourceIndexes 照常发射（pm 双门退役——特性对切换快照腿后消费端读快照，
    /// B2 变更日志登记：原「仅 Fluent 通道发射」断言随批准设计失效）；条件发射语义保持——
    /// 无声明字段（IsPropertyNoAutoInclude）仍零痕迹（F1 字节门精神收窄为「非默认值不落行」）</summary>
    [Fact]
    public void DetailSegment_MapFromIndexPath_NoNewFieldEmission()
    {
        var driver = GenerateDriver(MapFromIndexSource, emitEnabled: true, sourceGenEnabled: true);
        var code = GetGeneratedCode(driver, DetailFileName);

        // 既有承载保留（HasIndex 布尔在之前即发射）
        Assert.Contains("HasIndex = true,", code);
        // 双通道补发：MapFrom 通道索引路径对 SourceIndexes 照常发射（-1 占位契约）
        Assert.Contains("SourceIndexes = new int[]", code);
        // 条件发射语义保持：本语料无属性级 NoAutoInclude 声明 → 零痕迹
        Assert.DoesNotContain("IsPropertyNoAutoInclude", code);
    }

    /// <summary>明细段 additive 零痕迹：无新承载声明的 [MapFrom] 语料 → 三新字段零行（F1 字节门语义）</summary>
    [Fact]
    public void DetailSegment_DefaultDeclarations_ZeroTraceOfNewFields()
    {
        var driver = GenerateDriver(MapFromSource, emitEnabled: true, sourceGenEnabled: true);
        var code = GetGeneratedCode(driver, DetailFileName);

        Assert.DoesNotContain("SourceIgnoreDirection", code);
        Assert.DoesNotContain("IsPropertyNoAutoInclude", code);
        Assert.DoesNotContain("SourceIndexes", code);
    }

    /// <summary>双写合并前提：on 态同 (S,T) 对身份段 + 明细段两笔登记并存（RuntimeSnapshotStore.Register 幂等合并收敛——R-B）</summary>
    [Fact]
    public void EmitSwitchOn_BothSegmentsRegisterSamePair()
    {
        var driver = GenerateDriver(FluentSource, emitEnabled: true, sourceGenEnabled: true);
        var identity = GetGeneratedCode(driver, IdentityFileName);
        var detail = GetGeneratedCode(driver, DetailFileName);

        // 同一对 (AEntity→ADto) 在两个产物文件中各出现一笔 Register（运行时经 Register 幂等合并）
        Assert.Contains("pair 0: EmitIdent.AEntity -> EmitIdent.ADto", identity);
        Assert.Contains("pair 0: EmitIdent.AEntity -> EmitIdent.ADto", detail);
        // 身份段 Register 计数与明细段 Register 计数一致（同对双笔）
        Assert.Equal(
            identity.Split(new[] { "RuntimeSnapshotStore.Register(" }, StringSplitOptions.None).Length - 1,
            detail.Split(new[] { "RuntimeSnapshotStore.Register(" }, StringSplitOptions.None).Length - 1);
    }

    // ----------------------------------------------------------------
    // 测试基建（双开关语料——EmitRuntimeSnapshot + NCMapperSourceGenEnabled）
    // ----------------------------------------------------------------

    private static GeneratorDriver GenerateDriver(string source, bool emitEnabled, bool sourceGenEnabled)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var generators = ImmutableArray.Create<ISourceGenerator>(
            new NCMapperSourceGenerator().AsSourceGenerator(),
            new RuntimeSnapshotGenerator().AsSourceGenerator());
        var optionsProvider = new DualSwitchOptionsProvider(emitEnabled, sourceGenEnabled);
        var driver = CSharpGeneratorDriver.Create(
            generators,
            additionalTexts: ImmutableArray<AdditionalText>.Empty,
            parseOptions: CSharpParseOptions.Default,
            optionsProvider: optionsProvider);
        return driver.RunGenerators(compilation);
    }

    private static string GetGeneratedCode(GeneratorDriver driver, string fileName)
    {
        var runResult = driver.GetRunResult();
        var tree = runResult.GeneratedTrees.Single(t => t.FilePath.Contains(fileName));
        return tree.ToString();
    }

    private static bool HasGeneratedTree(GeneratorDriver driver, string fileName)
        => driver.GetRunResult().GeneratedTrees.Any(t => t.FilePath.Contains(fileName));

    private sealed class DualSwitchOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly bool _emitEnabled;
        private readonly bool _sourceGenEnabled;
        public DualSwitchOptionsProvider(bool emitEnabled, bool sourceGenEnabled)
        {
            _emitEnabled = emitEnabled;
            _sourceGenEnabled = sourceGenEnabled;
        }
        public override AnalyzerConfigOptions GlobalOptions => new Opts(_emitEnabled, _sourceGenEnabled);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Opts(_emitEnabled, _sourceGenEnabled);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Opts(_emitEnabled, _sourceGenEnabled);

        private sealed class Opts : AnalyzerConfigOptions
        {
            private readonly bool _emitEnabled;
            private readonly bool _sourceGenEnabled;
            public Opts(bool emitEnabled, bool sourceGenEnabled)
            {
                _emitEnabled = emitEnabled;
                _sourceGenEnabled = sourceGenEnabled;
            }
            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.NCMapperEmitRuntimeSnapshot")
                {
                    value = _emitEnabled ? "true" : "false";
                    return true;
                }
                if (key == "build_property.NCMapperSourceGenEnabled")
                {
                    value = _sourceGenEnabled ? "true" : "false";
                    return true;
                }
                value = string.Empty;
                return false;
            }
        }
    }
}
