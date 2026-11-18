// b（007 小账②）：NCMAP019/042 "零 [MapFrom] 编译单元"回归锁定。
//   被证伪的旧口径（六+一文件注释原文）："019 仅在编译单元存在 [MapFrom] 类型时输出
//   （生成器 mappings 非空前置）"——007 探针四组实证：编译单元内**完全没有** [MapFrom] 时，
//   019（Error，属性级标注无锚）与 042（Info，Dto 命名缺锚）照报，与 mappings 是否为空无关。
//  本文件把该实态固化为回归锁（防后续改动引入"mappings 非空前置"门），并作为 a 步
//   注释修订的实态依据（注释按本文件断言的行为改写）。
using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 零 [MapFrom] 编译单元下 019/042 照报的回归锁定族
    /// <para>形态轴：① 属性级标注无锚 + Dto 命名同单元（019 与 042 分流并存）；② 仅 Dto 命名无标注（042 单报）；
    /// ③ 仅属性级标注非 Dto 命名（019 单报）。三形态均在**编译单元零 [MapFrom]**前提下断言诊断码与级别
    /// （NF2：不得弱断言）。</para>
    /// </summary>
    public class NCMAP019ZeroAnchorUnitRegressionTests
    {
        /// <summary>① 零 [MapFrom] 单元：属性级标注无锚（019 Error）与 Dto 命名缺锚（042 Info）同单元分流照报。</summary>
        [Fact]
        public void ZeroMapFromUnit_Reports019Error_And042Info()
        {
            var source = """
                using NCMapper.Attributes;

                public class OrderDto
                {
                    [MapIgnore(NCMapper.Attributes.Direction.ToDto)]
                    public string Note { get; set; } = "";
                }

                public class CustomerDto
                {
                    public int Id { get; set; }
                }
                """;
            var diagnostics = Compile(source);

            var d019 = Assert.Single(diagnostics, d => d.Id == "NCMAP019");
            Assert.Equal(DiagnosticSeverity.Error, d019.Severity);
            var d042 = Assert.Single(diagnostics, d => d.Id == "NCMAP042");
            Assert.Equal(DiagnosticSeverity.Info, d042.Severity);
        }

        /// <summary>② 零 [MapFrom] 单元、仅 Dto 命名（无属性级标注）：042 Info 照报、零 019。</summary>
        [Fact]
        public void ZeroMapFromUnit_DtoNamingOnly_Reports042Info()
        {
            var source = """
                public class PlainDto
                {
                    public int Id { get; set; }
                }
                """;
            var diagnostics = Compile(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP019");
            var d042 = Assert.Single(diagnostics, d => d.Id == "NCMAP042");
            Assert.Equal(DiagnosticSeverity.Info, d042.Severity);
        }

        /// <summary>③ 零 [MapFrom] 单元、仅属性级标注（非 Dto 命名）：019 Error 照报、零 042。</summary>
        [Fact]
        public void ZeroMapFromUnit_AttributeOnlyNonDto_Reports019Error()
        {
            var source = """
                using NCMapper.Attributes;

                public class AuditView
                {
                    [MapIgnore(NCMapper.Attributes.Direction.ToDto)]
                    public string Note { get; set; } = "";
                }
                """;
            var diagnostics = Compile(source);

            var d019 = Assert.Single(diagnostics, d => d.Id == "NCMAP019");
            Assert.Equal(DiagnosticSeverity.Error, d019.Severity);
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP042");
        }

        // ================================================================
        // 辅助：零 [MapFrom] 语料的 driver 诊断收集（NCMAP019GovernanceMatrixTests 同构口径）
        // ================================================================

        private static ImmutableArray<Diagnostic> Compile(string source)
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
                "NCMAP019ZeroAnchorUnit",
                new[] { CSharpSyntaxTree.ParseText(source) },
                allRefs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new NCMapperSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

            driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);
            return diagnostics;
        }
    }
}
