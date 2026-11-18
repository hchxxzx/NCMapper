using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 测试 #6c（S3）：输出级增量命中——解除 CompilationProvider Combine 后，
    /// 无关文件编辑时 RegisterSourceOutput 输入全部 Unchanged/Cached → 输出节点 Reason=Cached/Unchanged
    /// （回调不执行、源文本复用——输出级缓存恢复，ANALYSIS-001 优化目标）。
    /// </summary>
    public class OutputLevelIncrementalTests
    {
        private const string SourceOutputStepName = "SourceOutput";

        [Fact]
        public void UnrelatedEdit_SourceOutputCached()
        {
            var treeA = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcO { public int Id { get; set; } public string Name { get; set; } = ""; }
                [MapFrom(typeof(SrcO))]
                public class DstO { public int Id { get; set; } public string Name { get; set; } = ""; }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var treeB = CSharpSyntaxTree.ParseText("public class UnrelatedO { public int X { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);

            var compilation1 = CSharpCompilation.Create(
                "OutLevel1",
                new[] { treeA, treeB },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CreateTrackedDriver();
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation1, out _, out _, TestContext.Current.CancellationToken);

            // 无关文件编辑（treeB 新实例）——transform 命中 + 输出输入纯值未变 → SourceOutput 缓存命中
            var treeB2 = CSharpSyntaxTree.ParseText("public class UnrelatedO { public int X { get; set; } public int Y { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);
            var compilation2 = compilation1.ReplaceSyntaxTree(treeB, treeB2);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _, TestContext.Current.CancellationToken);

            var step = GetSourceOutputStep(driver);
            var keys = new System.Collections.Generic.List<string>();
            foreach (var result in driver.GetRunResult().Results)
                if (result.TrackedSteps != null)
                    foreach (var k in result.TrackedSteps.Keys)
                        keys.Add(k);
            var reasons = step == null ? new[] { IncrementalStepRunReason.Modified } : step.Outputs.Select(o => o.Reason).ToArray();
            Assert.True(step != null
                    && reasons.All(r => r is IncrementalStepRunReason.Unchanged or IncrementalStepRunReason.Cached),
                $"#6c 失败: step={(step == null ? "null" : "ok")} keys=[{string.Join("|", keys)}] reasons=[{string.Join(",", reasons)}]");
        }

        [Fact]
        public void SemanticEdit_SourceOutputModified()
        {
            var treeA1 = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcP { public int Id { get; set; } }
                [MapFrom(typeof(SrcP))]
                public class DstP { public int Id { get; set; } }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var treeB = CSharpSyntaxTree.ParseText("public class UnrelatedQ { public int X { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);

            var compilation1 = CSharpCompilation.Create(
                "OutLevel2",
                new[] { treeA1, treeB },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CreateTrackedDriver();
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation1, out _, out _, TestContext.Current.CancellationToken);

            // 语义编辑（DTO 加属性）→ 输出必须重算（Modified/New）
            var treeA2 = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcP { public int Id { get; set; } }
                [MapFrom(typeof(SrcP))]
                public class DstP { public int Id { get; set; } public string Extra { get; set; } = ""; }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var compilation2 = compilation1.ReplaceSyntaxTree(treeA1, treeA2);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _, TestContext.Current.CancellationToken);

            var step = GetSourceOutputStep(driver);
            Assert.NotNull(step);
            var reasons = step!.Outputs.Select(o => o.Reason).ToArray();
            Assert.Contains(reasons, r => r is IncrementalStepRunReason.Modified or IncrementalStepRunReason.New);
        }

        /// <summary>
        /// F4 探针：Fluent For 工程无关编辑 → SourceOutput 缓存命中
        /// <para>ForInvocationLocation 物化进 MappingSnapshot（LocationSnapshot 纯值，S3 同款）后，
        /// Fluent 通道输出输入在无关编辑时仍两轮恒等 → 输出级缓存不被破坏（F4 回流预案不触发）。</para>
        /// </summary>
        [Fact]
        public void FluentFor_UnrelatedEdit_SourceOutputCached()
        {
            var treeA = CSharpSyntaxTree.ParseText("""
                using NCMapper;
                public class FSrc { public int Id { get; set; } }
                public class FDst { public int Id { get; set; } }
                [NCMapper.MapperConfig]
                public class FCfg
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FSrc, FDst>().Member(d => d.Id).From(s => s.Id);
                    }
                }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var treeB = CSharpSyntaxTree.ParseText("public class UnrelatedF { public int X { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);

            var compilation1 = CSharpCompilation.Create(
                "OutLevelF4",
                new[] { treeA, treeB },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CreateTrackedDriver();
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation1, out _, out _, TestContext.Current.CancellationToken);

            // 无关文件编辑 → 含 ForInvocationLocation 的快照值恒等 → SourceOutput 缓存命中
            var treeB2 = CSharpSyntaxTree.ParseText("public class UnrelatedF { public int X { get; set; } public int Y { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);
            var compilation2 = compilation1.ReplaceSyntaxTree(treeB, treeB2);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _, TestContext.Current.CancellationToken);

            var step = GetSourceOutputStep(driver);
            var reasons = step == null ? new[] { IncrementalStepRunReason.Modified } : step.Outputs.Select(o => o.Reason).ToArray();
            Assert.True(step != null
                    && reasons.All(r => r is IncrementalStepRunReason.Unchanged or IncrementalStepRunReason.Cached),
                $"T2 F4 失败: step={(step == null ? "null" : "ok")} reasons=[{string.Join(",", reasons)}]");
        }

        private static GeneratorDriver CreateTrackedDriver()
            => CSharpGeneratorDriver.Create(
                new[] { new NCMapperSourceGenerator().AsSourceGenerator() },
                driverOptions: new GeneratorDriverOptions(
                    disabledOutputs: IncrementalGeneratorOutputKind.None,
                    trackIncrementalGeneratorSteps: true));

        private static IncrementalGeneratorRunStep? GetSourceOutputStep(GeneratorDriver driver)
        {
            var runResult = driver.GetRunResult();
            foreach (var result in runResult.Results)
            {
                if (result.TrackedSteps == null) continue;
                if (result.TrackedSteps.TryGetValue(SourceOutputStepName, out var steps) && steps.Length > 0)
                    return steps[0];
            }
            return null;
        }
    }
}
