using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// W4S-5: NCMAP026 诊断测试 — [Mask] 标注位置非规范（源侧）
    /// <para>触发条件：[Mask] 标注所在类 ≠ [MapFrom] 标注类（Mask 标在源侧 = 非规范位置）。
    /// Mask 语义单向（目标 string 脱敏），源侧标注不生效——仅诊断不消费（方案语义点②a）。</para>
    /// <para>P2-7：互指对（双侧均有 [MapFrom] 且互指）时由 NCMAP025（Error）优先，NCMAP026 不叠加。</para>
    /// <para>口径订正（007 小账①证伪旧注释）：026 **不以"编译单元存在
    /// [MapFrom]（mappings 非空）"为输出前置**（同 019 族实态；零锚回归锁 =
    /// <c>NCMAP019ZeroAnchorUnitRegressionTests</c>）。本文件保留锚点类型属语料结构需要。</para>
    /// </summary>
    public class NCMAP026Tests
    {
        // 锚点源码片段：语料结构需要（非 026 输出前提——见类注释口径订正）
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
        /// 触发：源类型属性标 [Mask]（源侧）→ NCMAP026
        /// <para>[Mask] 标在 Entity（源侧），[MapFrom] 标在 DTO（目标侧）——标注位置错位。</para>
        /// </summary>
        [Fact]
        public async Task MaskOnSourceSide_Triggers()
        {
            var source = Anchor + """
                public class OrderEntity
                {
                    [Mask(typeof(NCMapper.MaskPhone))]
                    public string Phone { get; set; } = "";
                }

                [MapFrom(typeof(OrderEntity))]
                public class OrderDto
                {
                    public string Phone { get; set; } = "";
                }
                """;
            await VerifyDiagnostic(source, "NCMAP026");
        }

        /// <summary>
        /// 规范：Mask 标在目标侧（与 [MapFrom] 同侧）→ 不触发
        /// </summary>
        [Fact]
        public async Task MaskOnTargetSide_DoesNotTrigger()
        {
            var source = Anchor + """
                public class OrderEntity2
                {
                    public string Phone { get; set; } = "";
                }

                [MapFrom(typeof(OrderEntity2))]
                public class OrderDto2
                {
                    [Mask(typeof(NCMapper.MaskPhone))]
                    public string Phone { get; set; } = "";
                }
                """;
            await VerifyNoDiagnostic(source, "NCMAP026");
        }

        /// <summary>
        /// 规范：源类型无 [Mask] 标注 → 不触发（防误报）
        /// </summary>
        [Fact]
        public async Task NoMaskOnSource_DoesNotTrigger()
        {
            var source = Anchor + """
                public class OrderEntity3
                {
                    public string Phone { get; set; } = "";
                }

                [MapFrom(typeof(OrderEntity3))]
                public class OrderDto3
                {
                    public string Phone { get; set; } = "";
                }
                """;
            await VerifyNoDiagnostic(source, "NCMAP026");
        }

        /// <summary>
        /// P2-7：互指对（双侧均有 [MapFrom] 且互指）→ NCMAP026 不叠加（NCMAP025 Error 优先）
        /// <para>MutualEntity [MapFrom(MutualDto)] 且 MutualDto [MapFrom(MutualEntity)] 构成真互指，
        /// 处理 MutualDto 映射时 sourceType=MutualEntity 标了 [MapFrom(MutualDto)] → 互指 → NCMAP026 跳过。</para>
        /// </summary>
        [Fact]
        public async Task MutualPair_DoesNotTriggerNCMAP026()
        {
            var source = Anchor + """
                [MapFrom(typeof(MutualDto))]
                public class MutualEntity
                {
                    [Mask(typeof(NCMapper.MaskPhone))]
                    public string Phone { get; set; } = "";
                }

                [MapFrom(typeof(MutualEntity))]
                public class MutualDto
                {
                    public string Phone { get; set; } = "";
                }
                """;
            await VerifyNoDiagnostic(source, "NCMAP026");
        }

        // ================================================================
        // 辅助方法（与 NCMAP019DtoNamingTests 同构）
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

        private static Task<(Compilation, ImmutableArray<Diagnostic>)> CompileAndGetDiagnostics(string source)
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
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

            driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);
            return Task.FromResult((updatedCompilation, diagnostics));
        }
    }
}
