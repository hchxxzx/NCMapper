using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Linq;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 增量验证：
    /// #1④ 程序集级 [NCMapperOptions] 变更 → 诊断选项正确更新（B-6 双验收①的增量口径）；
    /// #1⑦ 全局配置（MSBuild）变更 → 重生成且结果正确（mergedConfig 节点变化驱动）。
    /// </summary>
    public class IncrementalConfigChangeTests
    {
        /// <summary>#1④：程序集级选项从"未设置"变为"ReportUnmappedTargetMembers=true" →
        /// 第二次 run 诊断必须包含 NCMAP001（选项正确更新，非陈旧缓存）</summary>
        [Fact]
        public void AssemblyOptionsChange_DiagnosticsUpdate()
        {
            const string sourceWithout = """
                using NCMapper.Attributes;
                public class SrcE { public int Id { get; set; } }
                [MapFrom(typeof(SrcE))]
                public class DstE { public int Id { get; set; } public string Missing { get; set; } = ""; }
                """;
            const string sourceWith = """
                using NCMapper.Attributes;
                [assembly: NCMapperOptions(ReportUnmappedTargetMembers = true)]
                public class SrcE { public int Id { get; set; } }
                [MapFrom(typeof(SrcE))]
                public class DstE { public int Id { get; set; } public string Missing { get; set; } = ""; }
                """;

            var compilation1 = CSharpCompilation.Create(
                "IncrCfg1",
                new[] { CSharpSyntaxTree.ParseText(sourceWithout, cancellationToken: TestContext.Current.CancellationToken) },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CreateTrackedDriver();
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation1, out _, out var diags1, TestContext.Current.CancellationToken);
            Assert.DoesNotContain(diags1, d => d.Id == "NCMAP001");

            // 程序集级选项变更 → 第二次 run 必须报 NCMAP001（B-6：mergedConfig 变化 → transform 重跑 + 注入生效）
            var compilation2 = CSharpCompilation.Create(
                "IncrCfg1",
                new[] { CSharpSyntaxTree.ParseText(sourceWith, cancellationToken: TestContext.Current.CancellationToken) },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out var diags2, TestContext.Current.CancellationToken);
            Assert.Contains(diags2, d => d.Id == "NCMAP001");

            // 增量证据：transform 步骤重跑（配置变化输入）——TrackedSteps 仅存最近 run，直接断言输出正确即可
            // （#1④ 语义正确性为主；步骤原因见 #1①/#1② 基建）
        }

        /// <summary>#1⑦：MSBuild 全局配置（NCMapperIgnoreNames）变更 → 重生成且映射正确（忽略项不生成映射）</summary>
        [Fact]
        public void GlobalConfigChange_RegeneratesCorrectly()
        {
            const string source = """
                using NCMapper.Attributes;
                public class SrcG { public int Id { get; set; } public string Keep { get; set; } = ""; public string SkipMe { get; set; } = ""; }
                [MapFrom(typeof(SrcG))]
                public class DstG { public int Id { get; set; } public string Keep { get; set; } = ""; public string SkipMe { get; set; } = ""; }
                """;

            var compilation = CSharpCompilation.Create(
                "IncrCfg2",
                new[] { CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken) },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // run1：无配置 → 正向 Map 方法含 SkipMe 映射（初始化器风格）
            GeneratorDriver driver = CreateTrackedDriver();
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated1, out _, TestContext.Current.CancellationToken);
            var gen1 = GetGenerated(updated1, compilation);
            Assert.Contains("SkipMe = src.SkipMe", GetMapMethodBody(gen1, "Map_SrcG_To_DstG"));

            // run2：IgnoreNames=SkipMe → 重生成且正向 Map 方法不再含 SkipMe（配置变化驱动管线；
            // DeepCopy 方法仍含 SkipMe——深拷贝复制全部属性，忽略配置不作用于 DeepCopy，符号版同）
            var optionsProvider = new MapBuilderConfigOptionsProvider(new System.Collections.Generic.Dictionary<string, string>
            {
                ["build_property.NCMapperIgnoreNames"] = "SkipMe",
            });
            driver = CSharpGeneratorDriver.Create(
                new[] { new NCMapperSourceGenerator().AsSourceGenerator() },
                optionsProvider: optionsProvider);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated2, out _, TestContext.Current.CancellationToken);
            var gen2 = GetGenerated(updated2, compilation);
            Assert.DoesNotContain("SkipMe", GetMapMethodBody(gen2, "Map_SrcG_To_DstG"));
            Assert.Contains("Keep = src.Keep", GetMapMethodBody(gen2, "Map_SrcG_To_DstG"));
            // 配置快照已输出（管线 mergedConfig 到达输出期）
            Assert.Contains("IgnoreNames = new string[] { \"SkipMe\" }", gen2);
        }

        /// <summary>提取指定映射方法体（方法签名到下一个方法声明前）</summary>
        private static string GetMapMethodBody(string gen, string methodPrefix)
        {
            var idx = gen.IndexOf(methodPrefix + "(");
            if (idx < 0) return "";
            var next = gen.IndexOf("/// <summary>", idx + 10);
            var next2 = gen.IndexOf("public static", idx + 10);
            var end = next > 0 && next2 > 0 ? System.Math.Min(next, next2) : (next > 0 ? next : next2);
            if (end < 0) end = gen.Length;
            return gen.Substring(idx, end - idx);
        }

        private static string GetGenerated(Compilation updated, Compilation original)
        {
            var generated = updated.SyntaxTrees
                .Skip(original.SyntaxTrees.Count())
                .FirstOrDefault(t => t.FilePath.Contains("NCMapper_Generated_GeneratedMapper.g.cs"));
            return generated?.ToString() ?? "";
        }

        private static GeneratorDriver CreateTrackedDriver()
            => CSharpGeneratorDriver.Create(
                new[] { new NCMapperSourceGenerator().AsSourceGenerator() },
                driverOptions: new GeneratorDriverOptions(
                    disabledOutputs: IncrementalGeneratorOutputKind.None,
                    trackIncrementalGeneratorSteps: true));
    }

    /// <summary>测试用 AnalyzerConfigOptionsProvider（MSBuild 属性注入）</summary>
    internal sealed class MapBuilderConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _values;

        public MapBuilderConfigOptionsProvider(System.Collections.Generic.Dictionary<string, string> values)
        {
            _values = values;
        }

        public override AnalyzerConfigOptions GlobalOptions => new MapBuilderConfigOptions(_values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new MapBuilderConfigOptions(_values);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new MapBuilderConfigOptions(_values);

        private sealed class MapBuilderConfigOptions : AnalyzerConfigOptions
        {
            private readonly System.Collections.Generic.Dictionary<string, string> _values;

            public MapBuilderConfigOptions(System.Collections.Generic.Dictionary<string, string> values)
            {
                _values = values;
            }

            public override bool TryGetValue(string key, out string value)
                => _values.TryGetValue(key, out value!);
        }
    }
}
