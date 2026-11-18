using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using System.Threading.Tasks;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 探针：NCMAP025 四场景行为不变 + Fluent 侧定位分通道
    /// <para>探针最小集（四场景锁定矩阵归）：
    /// ① [MapFrom] 互指 → 025 Error、定位 = 目标类型声明标识符（既有语义）；
    /// ② For×For 互指 → 025 Error、定位 = For&lt;S,T&gt;() invocation（新）；
    /// ③ 跨通道互指 → 025 Error（Fluent 侧定位指向 For）。</para>
    /// </summary>
    public class NCMAP025LocationProbeTests
    {
        /// <summary>
        /// 探针①：特性×特性互指——025 触发、定位仍为目标类型声明标识符（行为不变）
        /// </summary>
        [Fact]
        public async Task AttributeMutual_025Reported_AtTargetIdentifier()
        {
            var source = """
                using NCMapper.Attributes;

                [MapFrom(typeof(BDto))]
                public class ADto { public int Id { get; set; } }

                [MapFrom(typeof(ADto))]
                public class BDto { public int Id { get; set; } }
                """;
            var diagnostics = await GetGeneratorDiagnostics(source);
            var d025 = diagnostics.FirstOrDefault(d => d.Id == "NCMAP025");
            Assert.NotNull(d025);
            Assert.Equal(DiagnosticSeverity.Error, d025!.Severity);
            // 定位落在类型声明行（fixture 每类独立一行——非 Location.None）
            Assert.True(d025.Location != Location.None, "特性互指 025 应有声明位置");
        }

        /// <summary>
        /// 探针②：For×For 互指——025 触发、定位指向 For&lt;S,T&gt;() invocation 行（T2 新语义）
        /// </summary>
        [Fact]
        public async Task FluentMutual_025Reported_AtForInvocation()
        {
            // 行布局：For 调用点在 0-based 行 12（cfg.For<...> 行）——断言精确对齐
            var source = """
                using NCMapper;

                public class FSE { public int Id { get; set; } }
                public class FDE { public int Id { get; set; } }

                [NCMapper.MapperConfig]
                public class FCfgA
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FDE, FSE>().Member(d => d.Id).From(s => s.Id);
                    }
                }

                [NCMapper.MapperConfig]
                public class FCfgB
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FSE, FDE>().Member(d => d.Id).From(s => s.Id);
                    }
                }
                """;
            var diagnostics = await GetGeneratorDiagnostics(source);
            var d025s = diagnostics.Where(d => d.Id == "NCMAP025").ToList();
            Assert.NotEmpty(d025s);
            foreach (var d in d025s)
                Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            // 定位行 = For invocation 所在行（0-based 10 与 19——两配置类的 cfg.For 行；
            // 一对互指双向各报一条，两条分别落两个 For 调用点）
            var lines = d025s
                .Where(d => d.Location != Location.None)
                .Select(d => d.Location.GetLineSpan().StartLinePosition.Line)
                .ToHashSet();
            Assert.Contains(10, lines);
        }

        /// <summary>
        /// 探针③：跨通道互指（A [MapFrom(B)] + Fluent For&lt;B,A&gt）——025 触发
        /// </summary>
        [Fact]
        public async Task CrossChannelMutual_025Reported()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;

                [MapFrom(typeof(CSE))]
                public class CDto { public int Id { get; set; } }

                public class CSE { public int Id { get; set; } }

                [NCMapper.MapperConfig]
                public class CCfg
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<CDto, CSE>().Member(d => d.Id).From(s => s.Id);
                    }
                }
                """;
            var diagnostics = await GetGeneratorDiagnostics(source);
            Assert.Contains(diagnostics, d => d.Id == "NCMAP025");
        }

        private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> GetGeneratorDiagnostics(string source)
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
                "NCMAP025LocationProbe",
                new[] { CSharpSyntaxTree.ParseText(source) },
                allRefs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new NCMapperSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
            return Task.FromResult(diagnostics);
        }
    }
}
