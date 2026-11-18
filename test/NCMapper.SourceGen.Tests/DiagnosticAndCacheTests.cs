using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Threading.Tasks;
using System.Collections.Immutable;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// T2-4: NCMAP013/017 诊断码触发测试
    /// T2-5: NCMapperSourceGenEnabled=false 抑制生成测试
    /// T2-6: 源生成器增量缓存确定性测试
    /// </summary>
    public class DiagnosticAndCacheTests
    {
        // ================================================================
        // T2-4: NCMAP013 — 反向映射属性跳过
        // ================================================================

        /// <summary>
        /// NCMAP013 触发：源实体含 get-only 属性，反向映射无法写入 → 跳过
        /// </summary>
        [Fact]
        public async Task NCMAP013_ReverseMapping_GetOnlyProperty_Skipped()
        {
            var source = """
                using NCMapper.Attributes;

                public class OrderEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                    public string Computed { get; } = "readonly";
                }

                [MapFrom(typeof(OrderEntity))]
                public class OrderDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                    public string Computed { get; set; } = "";
                }
                """;

            await VerifyDiagnostic(source, "NCMAP013");
        }

        /// <summary>
        /// NCMAP013 对照：简单双向映射无跳过 → 不触发
        /// </summary>
        [Fact]
        public async Task NCMAP013_SimpleBidirectional_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class SimpleEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(SimpleEntity))]
                public class SimpleDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP013");
        }

        // ================================================================
        // T2-4: NCMAP017 — [MapNoAutoInclude] 标注在引用属性上
        // ================================================================

        /// <summary>
        /// NCMAP017 触发：[MapNoAutoInclude] 标注在非集合引用属性上
        /// </summary>
        [Fact]
        public async Task NCMAP017_MapNoAutoInclude_OnReferenceProperty_Info()
        {
            var source = """
                using NCMapper.Attributes;

                public class BlogEntity { public int Id { get; set; } public string Title { get; set; } = ""; public AuthorEntity? Author { get; set; } }
                public class AuthorEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(BlogEntity))]
                public class BlogDto
                {
                    public int Id { get; set; }
                    public string Title { get; set; } = "";

                    [MapNoAutoInclude]
                    public AuthorDto? Author { get; set; }
                }

                [MapFrom(typeof(AuthorEntity))]
                public class AuthorDto { public int Id { get; set; } public string Name { get; set; } = ""; }
                """;

            await VerifyDiagnostic(source, "NCMAP017");
        }

        /// <summary>
        /// NCMAP017 对照：[MapNoAutoInclude] 标注在集合属性上 → 不触发
        /// </summary>
        [Fact]
        public async Task NCMAP017_MapNoAutoInclude_OnCollection_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;
                using System.Collections.Generic;

                public class BlogEntity2 { public int Id { get; set; } public string Title { get; set; } = ""; public List<CommentEntity> Comments { get; set; } = new(); }
                public class CommentEntity { public int Id { get; set; } public string Text { get; set; } = ""; }

                [MapFrom(typeof(BlogEntity2))]
                public class BlogDto2
                {
                    public int Id { get; set; }
                    public string Title { get; set; } = "";

                    [MapNoAutoInclude]
                    public List<CommentDto> Comments { get; set; } = new();
                }

                [MapFrom(typeof(CommentEntity))]
                public class CommentDto { public int Id { get; set; } public string Text { get; set; } = ""; }
                """;

            await VerifyNoDiagnostic(source, "NCMAP017");
        }

        // ================================================================
        // T2-5: NCMapperSourceGenEnabled=false 抑制生成
        // ================================================================

        /// <summary>
        /// Enabled=false 时生成方法数=0
        /// </summary>
        [Fact]
        public async Task SourceGenDisabled_NoCodeGenerated()
        {
            var source = """
                using NCMapper.Attributes;

                public class Entity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(Entity))]
                public class Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
                """;

            var (compilation, _) = await CompileAndGetDiagnostics(source, sourceGenEnabled: "false");

            // 检查生成的语法树数量 — 禁用后应无额外生成
            var generatedTrees = compilation.SyntaxTrees.Count() - 1; // 减去原始源码树
            Assert.Equal(0, generatedTrees);
        }

        /// <summary>
        /// Enabled=true（默认）时正常生成代码（对照）
        /// </summary>
        [Fact]
        public async Task SourceGenEnabled_CodeGenerated()
        {
            var source = """
                using NCMapper.Attributes;

                public class Entity2 { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(Entity2))]
                public class Dto2 { public int Id { get; set; } public string Name { get; set; } = ""; }
                """;

            var (compilation, _) = await CompileAndGetDiagnostics(source, sourceGenEnabled: "true");

            var generatedTrees = compilation.SyntaxTrees.Count() - 1;
            Assert.True(generatedTrees > 0, "启用源生成器后应有生成代码");
        }

        // ================================================================
        // T2-6: 增量缓存确定性
        // ================================================================

        /// <summary>
        /// 相同输入两次生成 → 字节一致
        /// </summary>
        [Fact]
        public async Task SameInput_Twice_IdenticalOutput()
        {
            var source = """
                using NCMapper.Attributes;

                public class CacheEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(CacheEntity))]
                public class CacheDto { public int Id { get; set; } public string Name { get; set; } = ""; }
                """;

            var output1 = await GetGeneratedCode(source);
            var output2 = await GetGeneratedCode(source);

            Assert.Equal(output1, output2);
        }

        /// <summary>
        /// 修改一个 DTO 后，另一个 DTO 的生成方法体不变（方法级隔离）
        /// 注：源生成器将所有映射输出到单一文件，因此验证粒度为方法级而非文件级
        /// </summary>
        [Fact]
        public async Task ModifyOneDto_OtherMethodUnchanged()
        {
            var source1 = """
                using NCMapper.Attributes;

                public class EntityA { public int Id { get; set; } public string Name { get; set; } = ""; }
                public class EntityB { public int Id { get; set; } public string Title { get; set; } = ""; }

                [MapFrom(typeof(EntityA))]
                public class DtoA { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(EntityB))]
                public class DtoB { public int Id { get; set; } public string Title { get; set; } = ""; }
                """;

            // 修改 DtoB 增加一个属性
            var source2 = """
                using NCMapper.Attributes;

                public class EntityA { public int Id { get; set; } public string Name { get; set; } = ""; }
                public class EntityB { public int Id { get; set; } public string Title { get; set; } = ""; public int Score { get; set; } }

                [MapFrom(typeof(EntityA))]
                public class DtoA { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(EntityB))]
                public class DtoB { public int Id { get; set; } public string Title { get; set; } = ""; public int Score { get; set; } }
                """;

            var code1 = await GetGeneratedCode(source1);
            var code2 = await GetGeneratedCode(source2);

            // 两次编译都应有生成代码
            Assert.False(string.IsNullOrWhiteSpace(code1), "第一次编译应有生成代码");
            Assert.False(string.IsNullOrWhiteSpace(code2), "第二次编译应有生成代码");

            // 整体输出应不同（DtoB 增加了 Score 属性）
            Assert.NotEqual(code1, code2);

            // 提取 EntityA→DtoA 的核心映射方法体，验证不变
            var methodA1 = ExtractMethod(code1, "Map_EntityA_To_DtoA");
            var methodA2 = ExtractMethod(code2, "Map_EntityA_To_DtoA");

            Assert.NotNull(methodA1);
            Assert.NotNull(methodA2);
            Assert.Equal(methodA1, methodA2);
        }

        // ================================================================
        // 辅助方法
        // ================================================================

        private static async Task VerifyDiagnostic(string source, string diagnosticId)
        {
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.Contains(diagnostics, d => d.Id == diagnosticId);
        }

        private static async Task VerifyNoDiagnostic(string source, string diagnosticId)
        {
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == diagnosticId);
        }

        private static async Task<string> GetGeneratedCode(string source)
        {
            var (compilation, _) = await CompileAndGetDiagnostics(source);
            var trees = compilation.SyntaxTrees.Skip(1).ToList();
            return string.Join("\n", trees.Select(t => t.ToString()));
        }

        /// <summary>
        /// 从生成代码中提取指定方法名的完整方法体（含签名到闭合大括号）
        /// </summary>
        private static string? ExtractMethod(string code, string methodName)
        {
            var lines = code.Split('\n');
            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(methodName) && lines[i].Contains("public static"))
                {
                    start = i;
                    break;
                }
            }
            if (start < 0) return null;

            int braceCount = 0;
            int end = start;
            for (int i = start; i < lines.Length; i++)
            {
                braceCount += lines[i].Count(c => c == '{');
                braceCount -= lines[i].Count(c => c == '}');
                end = i;
                if (braceCount == 0 && i > start) break;
            }
            return string.Join("\n", lines.Skip(start).Take(end - start + 1));
        }

        private static Task<(Compilation, ImmutableArray<Diagnostic>)> CompileAndGetDiagnostics(
            string source, string? sourceGenEnabled = null)
        {
            var ncmapperAssembly = typeof(NCMapper.Mapper).Assembly.Location;
            var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;

            var allRefs = new[]
            {
                typeof(object).Assembly.Location,
                ncmapperAssembly,
                typeof(System.Runtime.CompilerServices.MethodImplAttribute).Assembly.Location,
                System.IO.Path.Combine(runtimeDir, "System.Runtime.dll"),
                System.IO.Path.Combine(runtimeDir, "System.Collections.dll"),
                System.IO.Path.Combine(runtimeDir, "System.Linq.dll"),
                System.IO.Path.Combine(runtimeDir, "System.Linq.Expressions.dll"),
            }
            .Where(System.IO.File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

            var compilation = CSharpCompilation.Create(
                "DiagnosticTest",
                new[] { CSharpSyntaxTree.ParseText(source) },
                allRefs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new NCMapperSourceGenerator();

            GeneratorDriver driver;
            if (sourceGenEnabled != null)
            {
                var optionsProvider = new TestAnalyzerConfigOptionsProvider(sourceGenEnabled);
                driver = CSharpGeneratorDriver.Create(
                    new[] { generator.AsSourceGenerator() },
                    driverOptions: new GeneratorDriverOptions(
                        disabledOutputs: IncrementalGeneratorOutputKind.None,
                        trackIncrementalGeneratorSteps: false),
                    optionsProvider: optionsProvider);
            }
            else
            {
                driver = CSharpGeneratorDriver.Create(generator);
            }

            driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);
            return Task.FromResult((updatedCompilation, diagnostics));
        }

        /// <summary>
        /// 测试用 AnalyzerConfigOptionsProvider — 注入 NCMapperSourceGenEnabled 属性
        /// </summary>
        private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
        {
            private readonly string _enabled;
            public TestAnalyzerConfigOptionsProvider(string enabled) => _enabled = enabled;

            public override AnalyzerConfigOptions GlobalOptions => new TestGlobalOptions(_enabled);
            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => TestGlobalOptions.Empty;
            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => TestGlobalOptions.Empty;
        }

        private sealed class TestGlobalOptions : AnalyzerConfigOptions
        {
            public static readonly TestGlobalOptions Empty = new(null);
            private readonly string? _enabled;
            public TestGlobalOptions(string? enabled) => _enabled = enabled;

            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.NCMapperSourceGenEnabled" && _enabled != null)
                {
                    value = _enabled;
                    return true;
                }
                value = "";
                return false;
            }
        }

        // ================================================================
        // DiagnosticInfo 值相等
        // （Diagnostics 入 MappingInfo.Equals 的前置，C1 修订）
        // ================================================================

        /// <summary>
        /// 内容相同（Descriptor/Args/Location）的两个 DiagnosticInfo 实例 Equals 为 true、GetHashCode 相同
        /// </summary>
        [Fact]
        public void DiagnosticInfo_ValueEquality_SameContent_EqualAndSameHash()
        {
            var diag1 = new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptors.SourceFieldNotFoundRule,
                Args = new object[] { "UserName", "user_name", "UserDto" },
                Location = null,
            };
            var diag2 = new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptors.SourceFieldNotFoundRule,
                Args = new object[] { "UserName", "user_name", "UserDto" },
                Location = null,
            };

            Assert.True(diag1.Equals(diag2));
            Assert.Equal(diag1.GetHashCode(), diag2.GetHashCode());
        }

        /// <summary>
        /// 内容不同（Args 变化）的两个 DiagnosticInfo 实例 Equals 为 false
        /// </summary>
        [Fact]
        public void DiagnosticInfo_ValueEquality_DifferentArgs_NotEqual()
        {
            var diag1 = new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptors.SourceFieldNotFoundRule,
                Args = new object[] { "UserName", "user_name", "UserDto" },
            };
            var diag2 = new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptors.SourceFieldNotFoundRule,
                Args = new object[] { "OtherName", "user_name", "UserDto" },
            };

            Assert.False(diag1.Equals(diag2));
        }

        /// <summary>
        /// Descriptor 不同（不同诊断规则）的两个 DiagnosticInfo 实例 Equals 为 false
        /// </summary>
        [Fact]
        public void DiagnosticInfo_ValueEquality_DifferentDescriptor_NotEqual()
        {
            var diag1 = new DiagnosticInfo { Descriptor = DiagnosticDescriptors.SourceFieldNotFoundRule, Args = new object[] { "A", "B", "C" } };
            var diag2 = new DiagnosticInfo { Descriptor = DiagnosticDescriptors.TypeIncompatibleRule, Args = new object[] { "A", "B", "C" } };

            Assert.False(diag1.Equals(diag2));
        }

        // ================================================================
        // 缓存语义测试（GeneratorDriver 双跑）
        // 利用 Roslyn 节点级增量缓存：treeA 实例不变 → MappingInfo transform 缓存命中；
        // 无关语法树变化 → 输出阶段重跑。修复前（Equals 缺字段/输出期原地追加）
        // 此场景可观测累积与击穿；修复后行为正确。
        // ================================================================

        /// <summary>
        /// 无变更重编译诊断不累积：仅无关语法树变化触发输出重跑时，
        /// 缓存 MappingInfo 的诊断不得跨 run 累积（NCMAP027 数量恒为 1）
        /// </summary>
        [Fact]
        public async Task CacheSemantics_NoChangeRecompile_DiagnosticsNotAccumulated()
        {
            // treeA：触发 NCMAP027（DateTime→Guid 完全不相容跳过，TypeHelpers.HasMappingPath 步骤 10）
            var treeA = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcA { public System.DateTime Created { get; set; } }
                [MapFrom(typeof(SrcA))]
                public class DstA { public System.Guid Created { get; set; } }
                """, cancellationToken: TestContext.Current.CancellationToken);
            // treeB：无关文件（run2 替换内容触发输出阶段重跑；treeA 实例不变 → transform 缓存命中）
            var treeB = CSharpSyntaxTree.ParseText("public class UnrelatedB { public int X { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);

            var compilation1 = CSharpCompilation.Create(
                "CacheSemanticsT1",
                new[] { treeA, treeB },
                BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var driver = CSharpGeneratorDriver.Create(new NCMapperSourceGenerator().AsSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation1, out var updated1, out var diags1, TestContext.Current.CancellationToken);

            // 无关文件内容变化（treeB 新实例）→ 输出阶段重跑；treeA 实例不变 → MappingInfo 缓存命中
            var treeB2 = CSharpSyntaxTree.ParseText("public class UnrelatedB { public int X { get; set; } public int Y { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);
            var compilation2 = compilation1.ReplaceSyntaxTree(treeB, treeB2);
            driver.RunGeneratorsAndUpdateCompilation(compilation2, out var updated2, out var diags2, TestContext.Current.CancellationToken);

            // NCMAP027 数量恒定：同一映射在 Map/Update/MapList(含 Action 重载) 各生成路径
            // 各产生 1 条，MapListInto两分支（数组快速路径 + 通用索引循环）
            // 各 1 条（既有语义：诊断去重为非目标），单 run 共 8 条。
            // 修复前：缓存实例跨 run 原地追加 → run2 翻倍（16 条）；修复后：per-run collector → 恒 8 条
            Assert.Equal(8, diags1.Count(d => d.Id == "NCMAP027"));
            Assert.Equal(8, diags2.Count(d => d.Id == "NCMAP027"));
            Assert.Equal(diags1.Length, diags2.Length);
        }

        /// <summary>
        /// [NCMapperOptions] 开关变更触发重新生成：run1 无开关 NCMAP001 不报；
        /// run2 加类级 [NCMapperOptions(ReportUnmappedTargetMembers = true)] 后
        /// NCMAP001 恰好 1 条。
        /// 注（执行期验证结论，见差异登记）：跨编译树替换场景下源码符号
        /// SymbolEqualityComparer 恒不等，MappingInfo 必然重新生成——精确击穿复现需
        /// 同一编译内 Select（ApplyGlobalConfig）输出比较场景（driver 级配置变化），
        /// 测试模型不可构造；本测试以行为断言（开关生效 + 不累积）覆盖回归
        /// </summary>
        [Fact]
        public async Task CacheSemantics_NCMapperOptionsSwitchChange_Regenerates()
        {
            var treeA1 = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcB { public int Id { get; set; } }
                [MapFrom(typeof(SrcB))]
                public class DstB
                {
                    public int Id { get; set; }
                    public string Extra { get; set; } = "";
                }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var treeB = CSharpSyntaxTree.ParseText("public class UnrelatedC { public int X { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);

            var compilation1 = CSharpCompilation.Create(
                "CacheSemanticsT2",
                new[] { treeA1, treeB },
                BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var driver = CSharpGeneratorDriver.Create(new NCMapperSourceGenerator().AsSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation1, out var updated1, out var diags1, TestContext.Current.CancellationToken);

            // run1：默认 ReportUnmappedTargetMembers=false → 匹配失败属性 Extra 不报 NCMAP001
            Assert.DoesNotContain(diags1, d => d.Id == "NCMAP001");

            // run2：同一 DTO 加类级开关 → 应重新生成并报出 NCMAP001
            var treeA2 = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcB { public int Id { get; set; } }
                [NCMapperOptions(ReportUnmappedTargetMembers = true)]
                [MapFrom(typeof(SrcB))]
                public class DstB
                {
                    public int Id { get; set; }
                    public string Extra { get; set; } = "";
                }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var compilation2 = compilation1.ReplaceSyntaxTree(treeA1, treeA2);
            driver.RunGeneratorsAndUpdateCompilation(compilation2, out var updated2, out var diags2, TestContext.Current.CancellationToken);

            Assert.Equal(1, diags2.Count(d => d.Id == "NCMAP001"));
        }

        /// <summary>
        /// 新增匹配失败属性触发重新生成：run1 无 Extra（开关已开但无匹配失败属性 → 无 NCMAP001）；
        /// run2 DTO 新增匹配失败属性 Extra → AllTargetProperties/Diagnostics 变化 → 重新生成 →
        /// NCMAP001（Extra）出现且恰好 1 条。
        /// 注：同测试 2——跨编译场景击穿不可精确构造，本测试以行为断言覆盖回归
        /// </summary>
        [Fact]
        public async Task CacheSemantics_NewUnmatchedProperty_Regenerates()
        {
            var treeA1 = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcC { public int Id { get; set; } }
                [NCMapperOptions(ReportUnmappedTargetMembers = true)]
                [MapFrom(typeof(SrcC))]
                public class DstC
                {
                    public int Id { get; set; }
                }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var treeB = CSharpSyntaxTree.ParseText("public class UnrelatedD { public int X { get; set; } }", cancellationToken: TestContext.Current.CancellationToken);

            var compilation1 = CSharpCompilation.Create(
                "CacheSemanticsT3",
                new[] { treeA1, treeB },
                BuildMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var driver = CSharpGeneratorDriver.Create(new NCMapperSourceGenerator().AsSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation1, out var updated1, out var diags1, TestContext.Current.CancellationToken);
            Assert.DoesNotContain(diags1, d => d.Id == "NCMAP001");

            // run2：DTO 新增匹配失败属性 Extra（无 [MapProperty]）→ 应重新生成并报出 NCMAP001(Extra)
            var treeA2 = CSharpSyntaxTree.ParseText("""
                using NCMapper.Attributes;
                public class SrcC { public int Id { get; set; } }
                [NCMapperOptions(ReportUnmappedTargetMembers = true)]
                [MapFrom(typeof(SrcC))]
                public class DstC
                {
                    public int Id { get; set; }
                    public string Extra { get; set; } = "";
                }
                """, cancellationToken: TestContext.Current.CancellationToken);
            var compilation2 = compilation1.ReplaceSyntaxTree(treeA1, treeA2);
            driver.RunGeneratorsAndUpdateCompilation(compilation2, out var updated2, out var diags2, TestContext.Current.CancellationToken);

            Assert.Equal(1, diags2.Count(d => d.Id == "NCMAP001"));
            Assert.Contains(diags2, d => d.Id == "NCMAP001" && d.GetMessage().Contains("Extra"));
        }

        /// <summary>
        /// 构造测试编译引用集（与 CompileAndGetDiagnostics 同口径）
        /// </summary>
        internal static List<MetadataReference> BuildMetadataReferences()
        {
            var ncmapperAssembly = typeof(NCMapper.Mapper).Assembly.Location;
            var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;

            return new[]
            {
                typeof(object).Assembly.Location,
                ncmapperAssembly,
                typeof(System.Runtime.CompilerServices.MethodImplAttribute).Assembly.Location,
                System.IO.Path.Combine(runtimeDir, "System.Runtime.dll"),
                System.IO.Path.Combine(runtimeDir, "System.Collections.dll"),
                System.IO.Path.Combine(runtimeDir, "System.Linq.dll"),
                System.IO.Path.Combine(runtimeDir, "System.Linq.Expressions.dll"),
            }
            .Where(System.IO.File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        }
    }
}
