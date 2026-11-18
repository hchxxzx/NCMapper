using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Linq;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 度量基建— TrackIncrementalGeneratorSteps 增量步骤验收测试。
    /// <para>目标：以可度量断言替换 AuditDiagnosticAcceptanceTests 中脆弱的 Contains 断言；
    /// 测试 #1① 无关文件编辑命中缓存、#1② 语义相关编辑重跑且结果正确、#1③ 失效数量基线。</para>
    /// <para>步骤名实测（Roslyn 5.6.0，2026-08-13 开工 HEAD）：transform 步骤 =
    /// <c>result_ForAttributeWithMetadataName</c>（MappingExtractor.ExtractMappingInfo）；
    /// NCMAP019 步骤 = <c>result_ForAttributeInternal</c>。
    /// 注：GetRunResult().TrackedSteps 仅含最近一次 run 的步骤记录（2026-08-13 实测），
    /// 多 run 对比须每次 run 后即时取结果。</para>
    /// </summary>
    public class IncrementalStepsBaselineTests
    {
        /// <summary>transform 步骤名（ExtractMappingInfo，ForAttributeWithMetadataName 管道）</summary>
        private const string TransformStepName = "result_ForAttributeWithMetadataName";

        /// <summary>NCMAP019 缺失 MapFrom 检测步骤名</summary>
        private const string MissingMapFromStepName = "result_ForAttributeInternal";

        /// <summary>输出步骤名（RegisterSourceOutput 全量聚合）</summary>
        private const string SourceOutputStepName = "SourceOutput";

        // ================================================================
        // 测试 #1①：无关文件编辑 → transform 步骤缓存命中（Unchanged/Cached）
        // ================================================================

        /// <summary>
        /// treeA（映射源文件）实例不变 + treeB（无关文件）内容变化 → 第二次 run 时
        /// transform 步骤输出 Reason 为 Unchanged 或 Cached（命中缓存）。
        /// <para>口径：Roslyn 增量管线中 Cached = 输入未变直接复用；Unchanged = 输入变化但输出值相等。
        /// 本场景 treeA 语法节点引用不变 → 输入未变 → Cached。</para>
        /// </summary>
        [Fact]
        public void IncrementalSteps_UnrelatedEdit_TransformCached()
        {
            var treeA = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcA { public int Id { get; set; } public string Name { get; set; } = ""; }
                [MapFrom(typeof(SrcA))]
                public class DstA { public int Id { get; set; } public string Name { get; set; } = ""; }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var treeB = CSharpSyntaxTree.ParseText("public class UnrelatedB { public int X { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);

            var compilation1 = CSharpCompilation.Create(
                "IncrementalT1",
                new[] { treeA, treeB },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CreateTrackedDriver();
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation1, out _, out _, TestContext.Current.CancellationToken);
            var run0 = GetStep(driver, TransformStepName);
            Assert.NotNull(run0);
            Assert.Contains(run0!.Outputs.Select(o => o.Reason), r => r == IncrementalStepRunReason.New);

            // 无关文件内容变化（treeB 新实例）→ transform 输入不变 → 步骤缓存命中
            var treeB2 = CSharpSyntaxTree.ParseText("public class UnrelatedB { public int X { get; set; } public int Y { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);
            var compilation2 = compilation1.ReplaceSyntaxTree(treeB, treeB2);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _, TestContext.Current.CancellationToken);

            var run1 = GetStep(driver, TransformStepName);
            Assert.NotNull(run1);
            // 第二次 run 的 transform 输出应为 Unchanged 或 Cached（缓存命中）
            var run1Reasons = run1!.Outputs.Select(o => o.Reason).ToArray();
            Assert.Contains(run1Reasons, r => r is IncrementalStepRunReason.Unchanged or IncrementalStepRunReason.Cached);
            Assert.DoesNotContain(run1Reasons, r => r == IncrementalStepRunReason.New || r == IncrementalStepRunReason.Modified);
        }

        /// <summary>
        /// 对照：语义相关文件（映射源文件 treeA）内容变化 → transform 步骤必须重跑（Modified/New）
        /// </summary>
        [Fact]
        public void IncrementalSteps_SemanticEdit_TransformReruns()
        {
            var treeA1 = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcB { public int Id { get; set; } }
                [MapFrom(typeof(SrcB))]
                public class DstB { public int Id { get; set; } }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var treeB = CSharpSyntaxTree.ParseText("public class UnrelatedC { public int X { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);

            var compilation1 = CSharpCompilation.Create(
                "IncrementalT2",
                new[] { treeA1, treeB },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CreateTrackedDriver();
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation1, out _, out _, TestContext.Current.CancellationToken);

            // 语义相关编辑：DTO 新增属性 → transform 必须重跑（输入语法节点变化）
            var treeA2 = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcB { public int Id { get; set; } }
                [MapFrom(typeof(SrcB))]
                public class DstB { public int Id { get; set; } public string Extra { get; set; } = ""; }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var compilation2 = compilation1.ReplaceSyntaxTree(treeA1, treeA2);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _, TestContext.Current.CancellationToken);

            var run1 = GetStep(driver, TransformStepName);
            Assert.NotNull(run1);
            // 第二次 run 必须 Modified 或 New（语法输入变化，不可命中缓存）
            var run1Reasons = run1!.Outputs.Select(o => o.Reason).ToArray();
            Assert.Contains(run1Reasons, r => r is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified);
        }

        // ================================================================
        // 测试 #1②：语义相关编辑重跑且结果正确（防以命中掩盖陈旧）
        // ================================================================

        /// <summary>
        /// 语义相关编辑后：生成的代码包含新增属性映射（结果正确），且诊断正确更新
        /// </summary>
        [Fact]
        public void IncrementalSteps_SemanticEdit_ResultCorrect()
        {
            var treeA1 = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcC { public int Id { get; set; } public string Name { get; set; } = ""; }
                [MapFrom(typeof(SrcC))]
                public class DstC { public int Id { get; set; } }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var treeB = CSharpSyntaxTree.ParseText("public class UnrelatedD { public int X { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);

            var compilation1 = CSharpCompilation.Create(
                "IncrementalT3",
                new[] { treeA1, treeB },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CreateTrackedDriver();
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation1, out _, out _, TestContext.Current.CancellationToken);

            // DTO 新增 Name 属性（源中有同名）→ 重跑后生成代码应包含 Name 赋值
            var treeA2 = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcC { public int Id { get; set; } public string Name { get; set; } = ""; }
                [MapFrom(typeof(SrcC))]
                public class DstC { public int Id { get; set; } public string Name { get; set; } = ""; }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var compilation2 = compilation1.ReplaceSyntaxTree(treeA1, treeA2);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out var updated2, out _, TestContext.Current.CancellationToken);

            var gen2 = updated2.SyntaxTrees.Skip(compilation1.SyntaxTrees.Count()).FirstOrDefault()?.ToString() ?? "";
            Assert.Contains("dst.Name = src.Name", gen2);
        }

        // ================================================================
        // 测试 #1③：记录"单映射文件变更时输出节点失效数量"基线（A-3）
        // ================================================================

        /// <summary>
        /// 单映射文件变更 → SourceOutput 步骤输出 Modified（输出整体重算）。
        /// <para>失效面基线：suffix 字典现算不扩大失效面（A-3）——
        /// 断言输出节点失效数量 = 1（单 SourceOutput），作为改造后比对基线。</para>
        /// </summary>
        [Fact]
        public void IncrementalSteps_SingleMappingEdit_SourceOutputReruns()
        {
            var treeA1 = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcD { public int Id { get; set; } }
                [MapFrom(typeof(SrcD))]
                public class DstD { public int Id { get; set; } }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var treeB = CSharpSyntaxTree.ParseText("public class UnrelatedE { public int X { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);

            var compilation1 = CSharpCompilation.Create(
                "IncrementalT4",
                new[] { treeA1, treeB },
                DiagnosticAndCacheTests.BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CreateTrackedDriver();
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation1, out _, out _, TestContext.Current.CancellationToken);

            // 单映射文件变更（DTO 加属性）→ SourceOutput 必须重跑（输出整体重算）
            var treeA2 = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcD { public int Id { get; set; } }
                [MapFrom(typeof(SrcD))]
                public class DstD { public int Id { get; set; } public string Extra { get; set; } = ""; }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var compilation2 = compilation1.ReplaceSyntaxTree(treeA1, treeA2);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _, TestContext.Current.CancellationToken);

            var run1 = GetStep(driver, SourceOutputStepName);
            Assert.NotNull(run1);
            var run1Reasons = run1!.Outputs.Select(o => o.Reason).ToArray();
            Assert.Contains(run1Reasons, r => r == IncrementalStepRunReason.Modified);
            // 基线：输出节点失效数量 = 1（单 SourceOutput 重算）
            Assert.Single(run1.Outputs);
        }

        // ================================================================
        // 辅助
        // ================================================================

        /// <summary>
        /// 创建启用 TrackIncrementalGeneratorSteps 的 driver（注意：RunGeneratorsAndUpdateCompilation
        /// 返回新实例，必须接收返回值，否则 GetRunResult 无步骤数据——2026-08-13 实测）
        /// </summary>
        private static GeneratorDriver CreateTrackedDriver()
            => CSharpGeneratorDriver.Create(
                new[] { new NCMapperSourceGenerator().AsSourceGenerator() },
                driverOptions: new GeneratorDriverOptions(
                    disabledOutputs: IncrementalGeneratorOutputKind.None,
                    trackIncrementalGeneratorSteps: true));

        /// <summary>
        /// 从 driver 的 run result 提取指定步骤（最近一次 run）
        /// </summary>
        private static IncrementalGeneratorRunStep? GetStep(
            GeneratorDriver driver, string stepName)
        {
            var runResult = driver.GetRunResult();
            foreach (var result in runResult.Results)
            {
                if (result.TrackedSteps == null) continue;
                if (result.TrackedSteps.TryGetValue(stepName, out var steps) && steps.Length > 0)
                    return steps[0];
            }
            return null;
        }
    }
}
