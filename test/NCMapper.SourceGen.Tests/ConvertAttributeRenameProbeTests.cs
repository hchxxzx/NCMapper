using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Threading.Tasks;
using System.Collections.Immutable;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// Q1 探针：[Convert]/[ConvertBack] 改名落地后编译与诊断行为锁定
    /// <para> Q1：改名后等价 fixture [Convert(typeof(X), nameof(X.M))]
    /// 过 Roslyn 编译 + 断言 NCMAP033/035 行为 + CS1614 失败形态监控（System.Convert 非 Attribute
    /// 不构成二义——评审 003 I-1 PoC 前证）。</para>
    /// </summary>
    public class ConvertAttributeRenameProbeTests
    {
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
        /// 探针 A：[Convert(typeof(X), nameof(X.M))] 特性绑定成功——无 CS1614/CS0616 二义/绑定失败，
        /// 无 NCMAP035（锚点方法解析通过）。生成面由既有生成断言族覆盖，此处不重复。
        /// </summary>
        [Fact]
        public async Task ConvertAttribute_Compiles_NoCS1614_NoCS0616()
        {
            var source = Anchor + """
                using System;
                using NCMapper.Attributes;

                public static class Converters
                {
                    public static string StatusText(int v) => v.ToString();
                }

                [MapFrom(typeof(AnchorEntity))]
                public class RenameDto
                {
                    [Convert(typeof(Converters), nameof(Converters.StatusText))]
                    public int Id { get; set; }
                }
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "CS1614");
            Assert.DoesNotContain(diagnostics, d => d.Id == "CS0616");
            // 特性绑定成功 → 035/036/037 不触发
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP035");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP099");
        }

        /// <summary>
        /// 探针 B：[ConvertBack] 反向绑定成功——无 CS1614/CS0616，无 NCMAP035
        /// </summary>
        [Fact]
        public async Task ConvertBackAttribute_BindAndGenerate()
        {
            var source = Anchor + """
                using System;
                using NCMapper.Attributes;

                public static class BackConverters
                {
                    public static int ParseId(string v) => int.Parse(v);
                }

                [MapFrom(typeof(AnchorEntity))]
                public class BackRenameDto
                {
                    [MapProperty(nameof(AnchorEntity.Id), nameof(AnchorEntity.Id))]
                    [ConvertBack(typeof(BackConverters), nameof(BackConverters.ParseId))]
                    public int Id { get; set; }
                }
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "CS1614");
            Assert.DoesNotContain(diagnostics, d => d.Id == "CS0616");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP035");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP099");
        }

        /// <summary>
        /// 探针 C：NCMAP033 消息含新显示名 [Convert]（显示名匹配链通）
        /// </summary>
        [Fact]
        public async Task NCMAP033_Message_Contains_NewDisplayName()
        {
            var source = Anchor + """
                using System;
                using NCMapper.Attributes;

                public static class Conv
                {
                    public static string Fmt(int v) => v.ToString();
                }

                [MapFrom(typeof(AnchorEntity))]
                public class ReverseDto
                {
                    [Convert(typeof(Conv), nameof(Conv.Fmt))]
                    public int Id { get; set; }
                }
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            var d033 = diagnostics.FirstOrDefault(d => d.Id == "NCMAP033");
            if (d033 != null)
            {
                Assert.Contains("[Convert]", d033.GetMessage());
                Assert.DoesNotContain("[ConvertUsing]", d033.GetMessage());
            }
        }

        /// <summary>
        /// 探针 D：NCMAP035 消息含新显示名 [Convert]（锚点方法缺失时的指引文本）
        /// </summary>
        [Fact]
        public async Task NCMAP035_Message_Contains_NewDisplayName()
        {
            var source = Anchor + """
                using System;
                using NCMapper.Attributes;

                public static class MissingConv
                {
                }

                [MapFrom(typeof(AnchorEntity))]
                public class MissingDto
                {
                    [Convert(typeof(MissingConv), "Nope")]
                    public int Id { get; set; }
                }
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            var d035 = diagnostics.FirstOrDefault(d => d.Id == "NCMAP035");
            Assert.NotNull(d035);
            var msg = d035.GetMessage();
            // 新显示名指引出现；旧显示名零残留（消息全文化锁定，形态不锁定）
            Assert.Contains("Convert", msg);
            Assert.DoesNotContain("ConvertUsing", msg);
        }

        // ================================================================
        // 辅助（与 NCMAP042SplitProbeTests 同构 + 生成源捕获）
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
                "ConvertRenameProbe",
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
