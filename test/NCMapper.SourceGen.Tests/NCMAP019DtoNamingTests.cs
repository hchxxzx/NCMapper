using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Threading.Tasks;
using System.Collections.Immutable;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// NCMAP019 诊断器扩展测试
    /// <para>新增触发场景：DTO 命名模式（类型名以 Dto/DTO 结尾）且未标 [MapFrom] → 也产生 NCMAP019
    /// （运行时回退反射路径 AOT 下不可用）；普通类名不受影响（防噪音）。</para>
    /// <para>口径订正（007 小账①证伪旧注释）：019 **不以"编译单元存在
    /// [MapFrom]（生成器 mappings 非空）"为输出前置**——零 [MapFrom] 编译单元下 019 Error 照报
    /// （回归锁 = <c>NCMAP019ZeroAnchorUnitRegressionTests</c>）。本文件保留锚点类型属语料结构与
    /// 对照需要（锚点用非 Dto 命名避免自触发），不是输出前提。</para>
    /// </summary>
    public class NCMAP019DtoNamingTests
    {
        // 锚点源码片段：语料结构与对照需要（非 019 输出前提——见类注释口径订正 + 零锚回归锁族）
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
        /// 场景①（既有，T1 拆码后升 Error）：有映射特性无 MapFrom → NCMAP019（Error）
        /// </summary>
        [Fact]
        public async Task MappingAttribute_Without_MapFrom_Triggers()
        {
            var source = Anchor + """
                public class OrderDto
                {
                    [MapProperty(typeof(AnchorEntity), nameof(AnchorEntity.Id))]
                    public int Id { get; set; }
                }
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            var d019 = Assert.Single(diagnostics, d => d.Id == "NCMAP019");
            Assert.Equal(DiagnosticSeverity.Error, d019.Severity);
        }

        /// <summary>
        /// 场景②（拆码改写锁定）：DTO 命名 + 完全未标任何特性 → NCMAP042（Info）
        /// <para>拆码后场景② 迁移至 NCMAP042（处理表），不再报 019。</para>
        /// </summary>
        [Fact]
        public async Task DtoNaming_Without_AnyAttribute_Triggers()
        {
            var source = Anchor + """
                public class UserDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                }
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.Contains(diagnostics, d => d.Id == "NCMAP042");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP019");
        }

        /// <summary>
        /// 场景②-2（新增，T1 拆码改写锁定）：DTO 大写命名 + 无特性 → NCMAP042
        /// </summary>
        [Fact]
        public async Task DtoNaming_Uppercase_Triggers()
        {
            var source = Anchor + """
                public class UserDTO
                {
                    public int Id { get; set; }
                }
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.Contains(diagnostics, d => d.Id == "NCMAP042");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP019");
        }

        /// <summary>
        /// 回归：DTO 命名 + [MapFrom] → 不触发（有源生成路径；019/042 均不报）
        /// </summary>
        [Fact]
        public async Task DtoNaming_With_MapFrom_DoesNotTrigger()
        {
            var source = Anchor + """
                [MapFrom(typeof(AnchorEntity))]
                public class UserDto
                {
                    public int Id { get; set; }
                }
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP019");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP042");
        }

        /// <summary>
        /// 防噪音：普通类名（非 DTO）无特性 → 不触发
        /// </summary>
        [Fact]
        public async Task PlainClassName_Without_MapFrom_DoesNotTrigger()
        {
            var source = Anchor + """
                public class UserEntity
                {
                    public int Id { get; set; }
                }
                """;
            await VerifyNoDiagnostic(source, "NCMAP019");
        }

        /// <summary>
        /// 防噪音：record DTO 命名 + 无特性 → 触发（record 也是 DTO 常见形态；T1 拆码改写锁定 NCMAP042）
        /// </summary>
        [Fact]
        public async Task DtoRecord_Without_MapFrom_Triggers()
        {
            var source = Anchor + """
                public record class ProductDto(int Id, string Name);
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.Contains(diagnostics, d => d.Id == "NCMAP042");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP019");
        }

        // ================================================================
        // 辅助方法（与 DiagnosticAndCacheTests 同构）
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
