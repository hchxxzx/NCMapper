// 行 2——NCMAP025 四场景矩阵缺口补齐
//   001 探针（NCMAP025LocationProbeTests）已落三场景：特性互指 / For×For / 跨通道 + Fluent 定位断言；
//   本文件补第四场景：合成快照（MapSubclass 合成子类对）参与互指判定。
//   机理锚点：DualDriverArbiter.cs:196-208 合成对并入 mappings 全集 → CodeGenerator.DetectMutualPairs
// （232-269）扫全集 → 合成对与显式反方向对构成互指 → NCMAP025 Error（生成侧·Compose
//   合成快照行"合成快照继续进互指判定（测试锁定新增）"）。
using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using System.Threading.Tasks;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// NCMAP025 第四场景锁定：MapSubclass 合成对 × 显式反方向 [MapFrom] 对 → 互指 025 Error
    /// <para> 行 2（承接）。</para>
    /// </summary>
    public class NCMAP025SyntheticSnapshotTests
    {
        /// <summary>
        /// 合成对 (D1 → D1Dto)（经 [MapSubclass] 登记）与显式对 (D1Dto → D1)（[MapFrom]）
        /// 构成互指 → 025 Error 报出（合成快照参与互指判定的行为锁定）。
        /// 继承链：D1 : B1（基类源）、D1Dto : B1Dto（基类目标 = 承载 [MapFrom]+[MapSubclass] 的类）。
        /// </summary>
        [Fact]
        public async Task SyntheticPair_MutualWithExplicitMapFrom_Reports025()
        {
            var source = """
                using NCMapper.Attributes;

                public class B1 { public int Id { get; set; } }

                [MapFrom(typeof(B1))]
                [MapSubclass(typeof(D1), typeof(D1Dto))]
                public class B1Dto { public int Id { get; set; } }

                public class D1Dto : B1Dto { public string Tag { get; set; } = ""; }

                [MapFrom(typeof(D1Dto))]
                public class D1 : B1 { public string Tag { get; set; } = ""; }
                """;
            var diagnostics = await GetGeneratorDiagnostics(source);
            var d025s = diagnostics.Where(d => d.Id == "NCMAP025").ToList();
            Assert.NotEmpty(d025s);
            foreach (var d in d025s)
                Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            // 先验锚：合成对确实并入（无 F46 继承违约、无 F49 孤儿登记）
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP_F46");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP_F49");
        }

        /// <summary>
        /// 反向对照（先验锚 #67）：仅合成对、无显式反方向对 → 不报 025
        /// <para>排除"合成对自身误触发互指"的假绿形态——同语料删去显式 [MapFrom(typeof(D2))]
        /// 后 025 必须消失；同时 [MapSubclass] 锚定合法（承载类有 [MapFrom]）零 F49。</para>
        /// </summary>
        [Fact]
        public async Task SyntheticPairAlone_NoMutual_No025()
        {
            var source = """
                using NCMapper.Attributes;

                public class B2 { public int Id { get; set; } }

                [MapFrom(typeof(B2))]
                [MapSubclass(typeof(D2), typeof(D2Dto))]
                public class B2Dto { public int Id { get; set; } }

                public class D2Dto : B2Dto { public string Tag { get; set; } = ""; }

                public class D2 : B2 { public string Tag { get; set; } = ""; }
                """;
            var diagnostics = await GetGeneratorDiagnostics(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP025");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP_F49");
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
                "NCMAP025SyntheticSnapshot",
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
