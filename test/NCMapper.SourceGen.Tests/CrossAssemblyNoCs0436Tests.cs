using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 跨程序集 CS0436 集成测试 — 验证 internal 生成类不触发跨程序集同名类型冲突
    /// <para>（实测表）：LibA internal + LibB public 时
    /// LibB 编译 0 警告 0 错误；LibA public 时 LibB 触发 CS0436（对照组证明测试有效）。</para>
    /// </summary>
    public class CrossAssemblyNoCs0436Tests
    {
        /// <summary>
        /// LibB 引用点源码 — 同命名空间 public GeneratedMapper + Consumer 引用点
        /// <para>模拟 nopCommerce 插件场景：插件自身生成 public GeneratedMapper 且引用之。</para>
        /// </summary>
        private const string LibBSource = """
            namespace NCMapper.Generated
            {
                public static class GeneratedMapper { public static void Bar() { } }
                public class Consumer { public static void Test() => GeneratedMapper.Bar(); }
            }
            """;

        /// <summary>
        /// 构造系统程序集引用集
        /// </summary>
        /// <returns>系统程序集 MetadataReference 列表</returns>
        private static List<MetadataReference> SystemReferences()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
                .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
                .ToList();
        }

        /// <summary>
        /// 编译单文件程序集并返回其元数据引用
        /// </summary>
        /// <param name="source">C# 源码字符串</param>
        /// <param name="assemblyName">程序集名</param>
        /// <param name="references">引用集</param>
        /// <returns>编译产物的 MetadataReference</returns>
        private static MetadataReference CompileLib(string source, string assemblyName, IEnumerable<MetadataReference> references)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [tree],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = new MemoryStream();
            var emitResult = compilation.Emit(stream);
            Assert.True(emitResult.Success,
                "LibA 编译失败：" + string.Join("\n", emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            stream.Position = 0;
            return MetadataReference.CreateFromStream(stream);
        }

        /// <summary>
        /// 编译 LibB（引用 LibA）并返回全部诊断
        /// </summary>
        /// <param name="libASource">LibA 源码（GeneratedMapper 可见性可变）</param>
        /// <returns>LibB 编译诊断集合</returns>
        private static ImmutableArray<Diagnostic> CompileLibBWithLibA(string libASource)
        {
            var references = SystemReferences();
            references.Add(CompileLib(libASource, "LibA", references));

            var libBTree = CSharpSyntaxTree.ParseText(LibBSource);
            var libBCompilation = CSharpCompilation.Create(
                "LibB",
                [libBTree],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return libBCompilation.GetDiagnostics();
        }

        /// <summary>
        /// LibA 的 GeneratedMapper 为 internal 时，LibB 编译无 CS0436（核心断言）
        /// </summary>
        [Fact]
        public void Internal_GeneratedMapper_No_Cs0436_Cross_Assembly()
        {
            const string libASource = """
                namespace NCMapper.Generated
                {
                    internal static class GeneratedMapper { public static void Foo() { } }
                }
                """;

            var diagnostics = CompileLibBWithLibA(libASource);

            Assert.DoesNotContain(diagnostics, d => d.Id == "CS0436");
            Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        }

        /// <summary>
        /// 对照组：LibA 的 GeneratedMapper 为 public 时，LibB 编译触发 CS0436（证明测试环境有效）
        /// <para>反向验证：若此对照断言失败，说明测试环境无法复现 CS0436，测试无效。</para>
        /// </summary>
        [Fact]
        public void Public_GeneratedMapper_Cs0436_Cross_Assembly_Control()
        {
            const string libAPublicSource = """
                namespace NCMapper.Generated
                {
                    public static class GeneratedMapper { public static void Foo() { } }
                }
                """;

            var diagnostics = CompileLibBWithLibA(libAPublicSource);

            Assert.Contains(diagnostics, d => d.Id == "CS0436");
        }
    }
}
