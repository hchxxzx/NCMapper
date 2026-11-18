using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Threading.Tasks;
using System.Collections.Immutable;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 拆码探针：NCMAP019（场景①，Error）与 NCMAP042（场景②，Info）分流锁定
    /// <para>探针最小集（全量矩阵归）：
    /// ① 场景②（Dto 命名无锚）→ NCMAP042 Info、不报 019；
    /// ② 场景①（[Mask] 无锚）→ NCMAP019 Error、不报 042；
    /// ③ 边界：[Mask] + Dto 命名 → 全归 019（含映射配置标注者不归 042）；
    /// ④ 负例：[MapSubclass] 无锚 → 报 F49 不报 019（防双报）。</para>
    /// </summary>
    public class NCMAP042SplitProbeTests
    {
        // 锚点源码片段：语料结构需要（**非 019/042 输出前提**——口径订正；
        // 零 [MapFrom] 编译单元照报的回归锁见 NCMAP019ZeroAnchorUnitRegressionTests）
        private const string Anchor = """
            using NCMapper.Attributes;

            public class AnchorEntity
            {
                public int Id { get; set; }
            }

            [MapFrom(typeof(AnchorEntity))]
            public class AnchorTarget
            {
                public int Id { get; set; }
            }

            """;

        /// <summary>
        /// 探针①：场景②——CvDto 命名类无锚 → NCMAP042（Info），不报 NCMAP019
        /// </summary>
        [Fact]
        public async Task DtoNaming_NoAnchor_Reports042_Not019()
        {
            var source = Anchor + """
                public class CvDto
                {
                    public int Id { get; set; }
                }
                """;
            var (compilation, diagnostics) = await CompileAndGetDiagnostics(source);
            var d042 = Assert.Single(diagnostics, d => d.Id == "NCMAP042");
            Assert.Equal(DiagnosticSeverity.Info, d042.Severity);
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP019");
        }

        /// <summary>
        /// 探针②：场景①——同型类带 [Mask] 无锚 → NCMAP019（Error），不报 NCMAP042
        /// </summary>
        [Fact]
        public async Task MaskAttribute_NoAnchor_Reports019_Error_Not042()
        {
            var source = Anchor + """
                public class OrderView
                {
                    [Mask(3, 4)]
                    public string Phone { get; set; } = "";
                }
                """;
            var (compilation, diagnostics) = await CompileAndGetDiagnostics(source);
            var d019 = Assert.Single(diagnostics, d => d.Id == "NCMAP019");
            Assert.Equal(DiagnosticSeverity.Error, d019.Severity);
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP042");
        }

        /// <summary>
        /// 探针③：边界——[Mask] + Dto 命名 → 全归 NCMAP019（含映射配置标注者不归 042）
        /// </summary>
        [Fact]
        public async Task MaskAttribute_WithDtoNaming_Reports019_Only()
        {
            var source = Anchor + """
                public class ProfileDto
                {
                    [Mask(2, 6)]
                    public string Email { get; set; } = "";
                }
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.Contains(diagnostics, d => d.Id == "NCMAP019");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP042");
        }

        /// <summary>
        /// 探针④（唯一负例）：[MapSubclass] 无锚 → 报 F49 不报 019（防双报）
        /// </summary>
        [Fact]
        public async Task MapSubclass_NoAnchor_ReportsF49_Not019()
        {
            var source = Anchor + """
                [MapSubclass(typeof(AnchorEntity), typeof(AnchorTarget))]
                public class SubHolderDto
                {
                    public int Id { get; set; }
                }
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.Contains(diagnostics, d => d.Id == "NCMAP_F49");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP019");
        }

        // 探针⑤退役（D10 用户裁决 2026-09-25）：[MapTree] 是类级树构建
        // 定义（定义，不是映射对），移出 NCMAP019 清单——"无锚报 019"行为随裁决移除；
        // 新增反向探针：无锚 [MapTree] 不再报 019（定义类标注合法存在）

        /// <summary>
        /// 探针⑤改判：[MapTree]（类级）无锚 → 不报 NCMAP019（D10——定义类标注合法）
        /// </summary>
        [Fact]
        public async Task MapTree_NoAnchor_No019_AfterD10()
        {
            var source = Anchor + """
                [MapTree("Id", "ParentId", "Children")]
                public class TreeNodeView
                {
                    public int Id { get; set; }
                    public int ParentId { get; set; }
                }
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP019");
        }

        // ================================================================
        // 辅助方法（与 NCMAP019DtoNamingTests 同构）
        // ================================================================

        private static async Task<(Compilation, ImmutableArray<Diagnostic>)> CompileAndGetDiagnostics(string source)
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
                "NCMAP042SplitProbe",
                new[] { CSharpSyntaxTree.ParseText(source) },
                allRefs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new NCMapperSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

            driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);
            return await Task.FromResult((updatedCompilation, diagnostics));
        }
    }
}
