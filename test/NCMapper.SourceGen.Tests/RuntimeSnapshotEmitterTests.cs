// 发射端测试（通道×字段×边界）
// 用 GeneratorDriver 驱动双生成器（NCMapperSourceGenerator + RuntimeSnapshotGenerator），
// 断言 RuntimeSnapshot 产物（NCMapper_RuntimeSnapshotMetadata.g.cs）对快照字段的正确转译：
//   - MapFrom 通道：RuntimeMapperMetadata 全字段（SourceType/TargetType/Channel=MapFrom/Properties/AllTargetSlots）
//   - Fluent 通道：Channel=Fluent + Fluent 专属（IsFluentForward=true）
//   - 字段级：HasMapIgnore/MapIgnoreDirection/HasMask/MaskTypeName/HasConverter/ConverterMethodName/IsFromJson/建议2 MapFromAttribute 缓存
//   - 边界：无映射类 → 零产物（不 AddSource）
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 发射端测试——RuntimeSnapshotGenerator + SnapshotMetadataEmitter 对快照字段的正确转译
/// </summary>
public class RuntimeSnapshotEmitterTests
{
    private const string RuntimeSnapshotFileName = "NCMapper_RuntimeSnapshotMetadata.g.cs";

    // MapFrom 通道富字段语料：嵌套 + IsFromJson + ConvertUsing + Mask + 多字段
    private const string MapFromRichSource = """
        using System;
        using System.Collections.Generic;
        using NCMapper.Attributes;

        namespace EmitRich
        {
            public class InnerE { public int X { get; set; } }
            public class InnerD { public int X { get; set; } }
            public class JsonPayload { public string K { get; set; } = ""; }

            public class RichEntity
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public InnerE Inner { get; set; } = new();
                public string PayloadJson { get; set; } = "{}";
            }

            [MapFrom(typeof(RichEntity))]
            public class RichDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public InnerD Inner { get; set; } = new();
                [MapProperty(typeof(RichEntity), nameof(RichEntity.PayloadJson), IsFromJson = true)]
                public JsonPayload? Payload { get; set; }
                [Convert(typeof(EmitConvert), nameof(EmitConvert.Convert))]
                public string IdText { get; set; } = "";
                [Mask(typeof(EmitMask))]
                public string Phone { get; set; } = "";
                [MapIgnore(NCMapper.Direction.ToDto)]
                public string InternalNote { get; set; } = "";
            }

            [MapFrom(typeof(RichEntity))]
            public class InnerDtoRef
            {
                public int Id { get; set; }
            }

            public static class EmitConvert
            {
                public static string Convert(int v) => v.ToString();
            }
            public class EmitMask : NCMapper.IMask
            {
                public string Mask(string? v) => v ?? "";
            }
        }
        """;

    // Fluent 通道语料
    private const string FluentSource = """
        using NCMapper;
        namespace EmitFluent
        {
            public class FEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class FDto { public int Id { get; set; } public string Name { get; set; } = ""; }

            [MapperConfig]
            public class FMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FEntity, FDto>()
                       .Member(d => d.Id).From(s => s.Id)
                       .Member(d => d.Name).From(s => s.Name);
                }
            }
        }
        """;

    // 无映射类语料（边界——零产物）
    private const string NoMappingSource = """
        namespace EmitNone
        {
            public class PlainA { public int Id { get; set; } }
            public class PlainB { public int Id { get; set; } }
        }
        """;

    /// <summary>
    /// MapFrom 通道富字段：发射产物含 RuntimeMapperMetadata 全字段 + 字段级转译（MapFrom 通道）
    /// </summary>
    [Fact]
    public void MapFromChannel_RichFields_EmitCorrectly()
    {
        var driver = GenerateDriverWithSwitch(MapFromRichSource, emitEnabled: true);
        Assert.True(HasGeneratedTree(driver, RuntimeSnapshotFileName), "on 状态应发射 RuntimeSnapshot 产物");
        var code = GetGeneratedCode(driver, RuntimeSnapshotFileName);

        // 对级字段（MapFrom 通道 Channel=MapFrom，不含 IsFluentForward=true 对）
        Assert.Contains("Channel = global::NCMapper.Runtime.RuntimeMappingChannel.MapFrom", code);
        // SourceType/TargetType typeof（用户类型无 global:: 前缀）
        Assert.Contains("typeof(EmitRich.RichEntity)", code);
        Assert.Contains("typeof(EmitRich.RichDto)", code);
        // 建议 2：MapFrom 通道共享 MapFromAttribute 实例（承载类型含 global）
        Assert.Contains("global::NCMapper.Attributes.MapFromAttribute(typeof(EmitRich.RichEntity))", code);
        // 字段级：IsFromJson + ConvertUsing + Mask
        Assert.Contains("IsFromJson = true", code);
        Assert.Contains("HasConverter = true", code);
        Assert.Contains("EmitConvert", code);       // ConverterMethodName 锚定类型
        Assert.Contains("HasMask = true", code);
        Assert.Contains("EmitMask", code);
        // 注：[MapIgnore] 属性（含 InternalNote ToDto）不进 AllTargetSlots（Both 忽略差集重建、
        // 单向忽略物化到 PropertyMapping 排除）——MapIgnore 方向由消费端运行时差集重建，非发射端字段，
        // 故此处不强断言 MapIgnoreDirection（快照层已有方向物化测试覆盖）。
        // 嵌套 DTO 独立 (S,T) 对（InnerEntity→InnerDto 未标 MapFrom 则走嵌套；此处 InnerDtoRef 标了）
        Assert.Contains("typeof(EmitRich.InnerDtoRef)", code);
        // 注册双写形态
        Assert.Contains("RuntimeSnapshotPair<", code);
        Assert.Contains("RuntimeSnapshotStore.Register(", code);
        // ModuleInitializer 形态
        Assert.Contains("[System.Runtime.CompilerServices.ModuleInitializer]", code);
    }

    /// <summary>
    /// Fluent 通道：发射产物 Channel=Fluent + IsFluentForward=true（区分 MapFrom 通道）
    /// </summary>
    [Fact]
    public void FluentChannel_EmitWithFluentChannelMarker()
    {
        var driver = GenerateDriverWithSwitch(FluentSource, emitEnabled: true);
        Assert.True(HasGeneratedTree(driver, RuntimeSnapshotFileName), "on 状态应发射 RuntimeSnapshot 产物");
        var code = GetGeneratedCode(driver, RuntimeSnapshotFileName);

        Assert.Contains("Channel = global::NCMapper.Runtime.RuntimeMappingChannel.Fluent", code);
        Assert.Contains("IsFluentForward = true", code);
        Assert.Contains("typeof(EmitFluent.FEntity)", code);
        Assert.Contains("typeof(EmitFluent.FDto)", code);
        // Fluent DTO 无 [MapFrom] 特性实例（建议 2：HasMapFromAttribute=false）
        Assert.DoesNotContain("HasMapFromAttribute = true", code);
    }

    /// <summary>
    /// 边界：无映射类 → 发射器零产物（不 AddSource RuntimeSnapshot 文件）
    /// </summary>
    [Fact]
    public void NoMapping_ZeroEmission()
    {
        var driver = GenerateDriverWithSwitch(NoMappingSource, emitEnabled: true);
        // 无 [MapFrom]/[MapperConfig] → 无有效 MappingSnapshot → Emit 返回 null → 不 AddSource
        Assert.False(HasGeneratedTree(driver, RuntimeSnapshotFileName),
            "无映射类时不应发射 RuntimeSnapshot 产物（零产物）");
    }

    /// <summary>
    /// spike：前置/后置钩子元数据发射（验收 #2——钩子声明对经
    /// 生成产物可查询到 ref；特性侧 [MapFrom]+[BeforeMapping]+[AfterMapping] 单参形态）
    /// </summary>
    [Fact]
    public void LifecycleHooks_EmitMetadataRefs()
    {
        var source = """
            using NCMapper.Attributes;
            namespace EmitHook
            {
                public class HEntity { public int Id { get; set; } }

                [MapFrom(typeof(HEntity))]
                [BeforeMapping(nameof(Before))]
                [AfterMapping(nameof(After))]
                public class HDto
                {
                    public int Id { get; set; }
                    public static void Before(HEntity s, HDto d) { }
                    public static void After(HEntity s, HDto d) { }
                }
            }
            """;
        var driver = GenerateDriverWithSwitch(source, emitEnabled: true);
        Assert.True(HasGeneratedTree(driver, RuntimeSnapshotFileName));
        var code = GetGeneratedCode(driver, RuntimeSnapshotFileName);

        // 前置钩子 ref 落元数据产物（条件行——非 null 才发射）
        Assert.Contains("BeforeMappingMethodRef = \"EmitHook.HDto.Before\"", code);
        // 后置钩子既有无条件行同样携带（特性侧填充同一快照字段）
        Assert.Contains("AfterMappingMethodRef = \"EmitHook.HDto.After\"", code);
    }

    /// <summary>
    /// （B9 字节门）：无钩子声明映射对产物不含 BeforeMappingMethodRef
    /// 行（条件行零输出——无声明对产物逐字节不变）
    /// </summary>
    [Fact]
    public void NoHookDeclaration_ZeroBeforeLineInProduct()
    {
        var driver = GenerateDriverWithSwitch(MapFromRichSource, emitEnabled: true);
        var code = GetGeneratedCode(driver, RuntimeSnapshotFileName);
        // 富字段语料无任何钩子声明——产物不含前置钩子行
        Assert.DoesNotContain("BeforeMappingMethodRef", code);
    }

    /// <summary>
    /// 边界：开关 off → 零发射（与 GeneratedSnapshotBaselineTests F1 断言一致，此处双通道独立复核）
    /// </summary>
    [Fact]
    public void SwitchOff_ZeroEmission()
    {
        var driver = GenerateDriverWithSwitch(MapFromRichSource, emitEnabled: false);
        Assert.False(HasGeneratedTree(driver, RuntimeSnapshotFileName),
            "开关 off 状态零发射（D5 门）");
    }

    /// <summary>
    /// 运行 NCMapperSourceGenerator + RuntimeSnapshotGenerator 双生成器，注入发射开关
    /// </summary>
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
