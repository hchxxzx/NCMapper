using Xunit;
using NCMapper.SourceGen.Tests.Helpers;
using NCMapper.SourceGen;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// A8 零差异自动化守卫 · 方案 A（ANALYSIS-20260814-001 / AUDIT-001 建议落地）：
    /// 代表性场景小型基线快照 — 内嵌源码经生成器输出必须与固化快照逐字节一致。
    /// <para>覆盖值类型化关键路径：基本标量、可空、集合、枚举、IsFromJson、record 构造器、
    /// Compose 跨表、ConvertUsing/Mask、字典、嵌套类、全局命名空间、反向映射。</para>
    /// <para>更新基线：设置环境变量 <c>NCMAPPER_UPDATE_BASELINE=1</c> 运行本测试，
    /// 生成代码写入 <c>Baselines/RepresentativeSnapshot.g.cs</c>（有意变更生成格式时使用，
    /// 更新后必须人工审查 diff 并提交）。</para>
    /// </summary>
    public class GeneratedSnapshotBaselineTests
    {
        private const string BaselineFileName = "RepresentativeSnapshot.g.cs";

        /// <summary>代表性场景源码（覆盖值模型关键路径；修改须同步评估基线）</summary>
        private const string RepresentativeSource = """
            using System;
            using System.Collections.Generic;
            using NCMapper.Attributes;

            // 全局命名空间类型（命名空间覆盖）
            public class GlobalEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
            [MapFrom(typeof(GlobalEntity))]
            public class GlobalDto { public int Id { get; set; } public string Name { get; set; } = ""; }

            namespace Snap
            {
                public enum Status { None = 0, Active = 1, Done = 2 }
                public enum StatusText { None = 0, Active = 1, Done = 2 }

                // 基本标量 + 可空 + 集合 + 枚举 + 字典 + 嵌套
                public class InnerEntity { public int X { get; set; } }
                public class InnerDto { public int X { get; set; } }

                public class BasicEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                    public string? Note { get; set; }
                    public int? Score { get; set; }
                    public List<string> Tags { get; set; } = new();
                    public Dictionary<string, int> Counts { get; set; } = new();
                    public Status State { get; set; }
                    public InnerEntity Inner { get; set; } = new();
                }

                [MapFrom(typeof(BasicEntity))]
                public class BasicDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                    public string? Note { get; set; }
                    public int? Score { get; set; }
                    public List<string> Tags { get; set; } = new();
                    public Dictionary<string, int> Counts { get; set; } = new();
                    public StatusText State { get; set; }
                    public InnerDto Inner { get; set; } = new();
                }

                // record 构造器路径
                public record RecordEntity(int A, string B, DateTime C);
                [MapFrom(typeof(RecordEntity))]
                public record RecordDto(int A, string B, DateTime C);

                // IsFromJson
                public class JsonEntity { public int Id { get; set; } public string PayloadJson { get; set; } = "{}"; }
                public class Payload { public string K { get; set; } = ""; }
                [MapFrom(typeof(JsonEntity))]
                public class JsonDto
                {
                    public int Id { get; set; }
                    [MapProperty(typeof(JsonEntity), nameof(JsonEntity.PayloadJson), IsFromJson = true)]
                    public Payload? Payload { get; set; }
                }

                // Compose 跨表（真 Compose 型：跨表字段 SubS 来自子源 SubEntity.S，ComposeMappings 非空；
                // 反向扩展不生成——Compose 仅单向，DTO→主源走 Mapper.Map 门面）
                public class SubEntity { public int S { get; set; } }
                public class ComposeEntity { public int Id { get; set; } public SubEntity Sub { get; set; } = new(); }
                public class SubDto { public int S { get; set; } }
                [MapFrom(typeof(ComposeEntity))]
                public class ComposeDto
                {
                    public int Id { get; set; }
                    [MapProperty(typeof(SubEntity), nameof(SubEntity.S))]
                    public int SubS { get; set; }
                }

                // ConvertUsing + Mask
                public class ConvEntity { public int Id { get; set; } public string Phone { get; set; } = ""; }
                public class ConvDto
                {
                    public int Id { get; set; }
                    [Convert(typeof(IntConverter), nameof(IntConverter.Convert))]
                    public string IdText { get; set; } = "";
                    [Mask(typeof(PhoneMask))]
                    public string Phone { get; set; } = "";
                }
                public static class IntConverter
                {
                    public static string Convert(int fieldValue) => fieldValue.ToString();
                }
                public class PhoneMask : NCMapper.IMask
                {
                    public string Mask(string? value) => value ?? "";
                }
            }
            """;

        [Fact]
        public void RepresentativeSnapshot_ByteIdentical()
        {
            var updateMode = Environment.GetEnvironmentVariable("NCMAPPER_UPDATE_BASELINE") == "1";
            var baselinePath = Path.Combine(AppContext.BaseDirectory, "Baselines", BaselineFileName);
            // 更新模式跳过存在性检查（基线就是要生成的）
            if (!updateMode)
                Assert.True(File.Exists(baselinePath), $"基线文件缺失: {baselinePath}（首次运行请设 NCMAPPER_UPDATE_BASELINE=1 生成）");

            var result = TestHelper.GenerateMapper(RepresentativeSource);
            var actual = result.GeneratedCode.Replace("\r\n", "\n");
            var baseline = updateMode ? "" : File.ReadAllText(baselinePath).Replace("\r\n", "\n");

            if (updateMode)
            {
                // 更新模式：写入源码 Baselines 目录（非 bin 输出目录）——向上定位仓库根（含 NCMapper.slnx）
                var sourceBaseline = FindSourceBaselinePath();
                Directory.CreateDirectory(Path.GetDirectoryName(sourceBaseline)!);
                File.WriteAllText(sourceBaseline, actual);
                return; // 写入基线后通过（人工审查 diff）
            }

            Assert.True(string.Equals(actual, baseline, StringComparison.Ordinal),
                "生成代码与固化基线不一致——生成格式被意外改动。有意变更请设 NCMAPPER_UPDATE_BASELINE=1 更新基线并人工审查 diff。");
        }

        /// <summary>定位源码 Baselines 目录（从输出目录向上找仓库根 NCMapper.slnx，再拼测试项目路径）</summary>
        private static string FindSourceBaselinePath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                // 以 NCMapper.slnx 作为仓库根标记（不依赖 .git，纯拷贝代码树时同样可定位）
                if (File.Exists(Path.Combine(dir.FullName, "NCMapper.slnx")))
                {
                    return Path.Combine(dir.FullName, "test", "NCMapper.SourceGen.Tests", "Baselines", BaselineFileName);
                }
                dir = dir.Parent;
            }
            throw new InvalidOperationException("无法定位仓库根（未找到 NCMapper.slnx）——更新模式须在仓库内运行");
        }

        // ───，F1 证伪 + on/off 产物存在断言（Q1）─────────
        // F1 证伪：开关 on/off 状态主生成器产物（NCMapper_Generated_GeneratedMapper.g.cs）逐字节不变
        //       （独立 AddSource 不触碰主生成器输出文本——spike D5 实证；本测试 L-1 主线并入后复跑）
        // on 产物存在：开关 on 状态发射 NCMapper_RuntimeSnapshotMetadata.g.cs（含 [ModuleInitializer] 注册）
        // off 产物为零：开关 off 状态零发射（基线产物字节不变 D5 门）

        private const string RuntimeSnapshotFileName = "NCMapper_RuntimeSnapshotMetadata.g.cs";

        /// <summary>
        /// F1 证伪 + on/off 产物存在：开关 on/off 主生成器产物逐字节不变；
        /// on 状态发射 RuntimeSnapshot 产物含 ModuleInitializer；off 状态零发射
        /// </summary>
        [Fact]
        public void RuntimeSnapshotSwitch_OnOff_MainGeneratorByteIdentical()
        {
            var offDriver = GenerateDriverWithRuntimeSnapshotSwitch(RepresentativeSource, emitEnabled: false);
            var onDriver = GenerateDriverWithRuntimeSnapshotSwitch(RepresentativeSource, emitEnabled: true);

            // F1 证伪：主生成器产物 on/off 逐字节不变（独立 AddSource 不影响主生成器输出文本）
            var mainOff = GetGeneratedCode(offDriver, TestHelper.GeneratedFileName);
            var mainOn = GetGeneratedCode(onDriver, TestHelper.GeneratedFileName);
            Assert.True(string.Equals(mainOff, mainOn, StringComparison.Ordinal),
                $"F1 证伪失败：开关 on/off 主生成器产物 diff≠0（on 长度={mainOn.Length}, off 长度={mainOff.Length}）");

            // off 产物为零：开关 off 状态零 RuntimeSnapshot 发射（D5 门）
            Assert.False(HasGeneratedTree(offDriver, RuntimeSnapshotFileName),
                "开关 off 状态不应发射 RuntimeSnapshot 产物（D5 门：零产物）");

            // on 产物存在 + ModuleInitializer 注册形态
            Assert.True(HasGeneratedTree(onDriver, RuntimeSnapshotFileName),
                "开关 on 状态应发射 NCMapper_RuntimeSnapshotMetadata.g.cs");
            var runtimeSnapshotCode = GetGeneratedCode(onDriver, RuntimeSnapshotFileName);
            Assert.Contains("[System.Runtime.CompilerServices.ModuleInitializer]", runtimeSnapshotCode);
            Assert.Contains("RuntimeSnapshotMetadataInitializer", runtimeSnapshotCode);
            Assert.Contains("global::NCMapper.Runtime.RuntimeSnapshotStore.Register(", runtimeSnapshotCode);
            Assert.Contains("global::NCMapper.Runtime.RuntimeSnapshotPair<", runtimeSnapshotCode);
        }

        /// <summary>
        /// 运行 NCMapperSourceGenerator + RuntimeSnapshotGenerator 双生成器，注入发射开关 GlobalOptions
        /// </summary>
        private static GeneratorDriver GenerateDriverWithRuntimeSnapshotSwitch(string source, bool emitEnabled)
        {
            var compilation = TestHelper.BuildCompilation(source);
            var generators = ImmutableArray.Create<ISourceGenerator>(
                new NCMapperSourceGenerator().AsSourceGenerator(),
                new RuntimeSnapshotGenerator().AsSourceGenerator());
            var optionsProvider = new SwitchAnalyzerConfigOptionsProvider(emitEnabled);
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

        private sealed class SwitchAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
        {
            private readonly bool _emitEnabled;
            public SwitchAnalyzerConfigOptionsProvider(bool emitEnabled) => _emitEnabled = emitEnabled;
            public override AnalyzerConfigOptions GlobalOptions => new SwitchAnalyzerConfigOptions(_emitEnabled);
            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new SwitchAnalyzerConfigOptions(_emitEnabled);
            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new SwitchAnalyzerConfigOptions(_emitEnabled);
        }

        private sealed class SwitchAnalyzerConfigOptions : AnalyzerConfigOptions
        {
            private const string EmitSwitchKey = "build_property.NCMapperEmitRuntimeSnapshot";
            private readonly bool _emitEnabled;
            public SwitchAnalyzerConfigOptions(bool emitEnabled) => _emitEnabled = emitEnabled;
            public override bool TryGetValue(string key, out string value)
            {
                if (string.Equals(key, EmitSwitchKey, StringComparison.Ordinal))
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
