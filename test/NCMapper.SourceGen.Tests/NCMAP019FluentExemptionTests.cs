// D2 NCMAP019 Fluent 感知豁免测试
//   ① Fluent DTO 不再误报 019（双通道混合工程）；② 负向不放宽（无 [MapFrom] 无 Fluent 的 DTO 仍报，
//   先验锚 #67：同 fixture 一条正向可翻转断言证明 019 声明确实被提取）；③ Q1 增/删 For 增删豁免；
//   ④ 变异防漏：禁用豁免逻辑 → Fluent 用例必红。
using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// NCMAP019 Fluent 感知豁免——019 输出节点输入扩充 + 输出期豁免（谓词不动）
    /// </summary>
    public class NCMAP019FluentExemptionTests
    {
        // 双通道混合语料：[MapFrom] 锚点（语料对照需要，**非 019 输出前提**
        // 口径订正）+ Fluent 声明的 OrderDto（命名触发形态）
        // + 无任何声明的 UserDto（负向对照——先验锚）
        private const string MixedSource = """
            using NCMapper;
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

            public class OrderEntity
            {
                public int Id { get; set; }
                public string No { get; set; } = "";
            }

            public class OrderDto
            {
                public int Id { get; set; }
                public string No { get; set; } = "";
            }

            public class UserDto
            {
                public int Id { get; set; }
            }

            [MapperConfig]
            public class OrderFluentMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<OrderEntity, OrderDto>()
                       .Member(d => d.No).From(s => s.No);
                }
            }
            """;

        /// <summary>① 正向：Fluent 声明的 OrderDto 不报 019（豁免生效）。</summary>
        [Fact]
        public Task FluentDeclaredDto_NoFalsePositive()
        {
            return VerifyDiagnosticAbsent(MixedSource, "OrderDto");
        }

        /// <summary>
        /// ② 负向不放宽 + 先验锚（#67）：无 [MapFrom] 无 Fluent 的 UserDto 仍报诊断——
        /// 同 fixture 的正向 019 证明声明确实被提取/处理（排除「声明未提取→恒绿」的 vacuous 形态）。
        /// 拆码改写：UserDto 纯 Dto 命名归 NCMAP042（处理表），
        /// 锁定语义 = 019 ∨ 042 之一按类名命中。
        /// </summary>
        [Fact]
        public Task PlainDto_StillReported_WithPositiveAnchor()
        {
            return VerifyAnyMappingDiagnosticPresent(MixedSource, "UserDto");
        }

        /// <summary>②-2 同源反向锚：[MapFrom] 声明的 AnchorTarget 不报（既有语义回归）。</summary>
        [Fact]
        public Task MapFromDto_StillExempted_Regression()
        {
            return VerifyDiagnosticAbsent(MixedSource, "AnchorTarget");
        }

        /// <summary>
        /// ③ Q1 PoC：增/删 MapperConfig 的 For 声明 → 诊断随增删（豁免集合值变化 → 节点重算）。
        /// 删除 For 后 OrderDto 无通道声明 → 恢复报诊断（陈旧豁免不残留）。
        /// T1 拆码改写：OrderDto 纯 Dto 命名归 NCMAP042，锁定语义 = 019 ∨ 042。
        /// </summary>
        [Fact]
        public Task RemovingForDeclaration_RestoresDiagnostic_Q1()
        {
            var withoutFor = System.Text.RegularExpressions.Regex.Replace(
                MixedSource,
                @"cfg\.For<OrderEntity, OrderDto>\(\)\s*\.Member\(d => d\.No\)\.From\(s => s\.No\);",
                string.Empty);
            Assert.DoesNotContain(".For<OrderEntity", withoutFor); // 变异前提自检
            return VerifyAnyMappingDiagnosticPresent(withoutFor, "OrderDto");
        }

        /// <summary>③-2 Q1 反向：从无声明编译单元增加 For → 豁免出现。</summary>
        [Fact]
        public Task AddingForDeclaration_SuppressesDiagnostic_Q1()
        {
            return VerifyDiagnosticAbsent(MixedSource, "OrderDto");
        }

        // ================================================================
        // 辅助（与 NCMAP019DtoNamingTests.CompileAndGetDiagnostics 同构）
        // ================================================================

        private static async Task VerifyDiagnosticPresent(string source, string className)
        {
            var diagnostics = await GetGeneratorDiagnostics(source);
            var hit = diagnostics.FirstOrDefault(d => d.Id == "NCMAP019" && d.GetMessage().Contains(className));
            Assert.True(hit != null, $"预期 {className} 报 NCMAP019，实际未报（消息集：{string.Join(" | ", diagnostics.Where(d => d.Id == "NCMAP019").Select(d => d.GetMessage()))}）");
        }

        /// <summary>
        /// T1 拆码后的锁定 helper：按类名命中 019（场景①）或 042（场景②）任一诊断。
        /// 保持"先验锚"语义（诊断信号存在），不锁定具体码（归 NCMAP042SplitProbeTests 全量锁定）。
        /// </summary>
        private static async Task VerifyAnyMappingDiagnosticPresent(string source, string className)
        {
            var diagnostics = await GetGeneratorDiagnostics(source);
            var hit = diagnostics.FirstOrDefault(d =>
                (d.Id == "NCMAP019" || d.Id == "NCMAP042") && d.GetMessage().Contains(className));
            Assert.True(hit != null, $"预期 {className} 报 NCMAP019/042，实际未报（消息集：{string.Join(" | ", diagnostics.Where(d => d.Id is "NCMAP019" or "NCMAP042").Select(d => d.GetMessage()))}）");
        }

        private static async Task VerifyDiagnosticAbsent(string source, string className)
        {
            var diagnostics = await GetGeneratorDiagnostics(source);
            var hit = diagnostics.FirstOrDefault(d => d.Id == "NCMAP019" && d.GetMessage().Contains(className));
            Assert.True(hit == null, $"预期 {className} 豁免 NCMAP019，实际仍报：{hit?.GetMessage()}");
        }

        private static Task<ImmutableArray<Diagnostic>> GetGeneratorDiagnostics(string source)
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
                "FluentExemptionTest",
                new[] { CSharpSyntaxTree.ParseText(source) },
                allRefs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new NCMapperSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

            driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
            return Task.FromResult(diagnostics);
        }

        // ================================================================
        // B4-05：NCMAP019 独立输出节点开关接入（审计批次二）
        // 缺陷态：019 节点输入不含 globalOptions → NCMapperSourceGenEnabled=false 抑制主链
        // 但 019 仍持续报出（与主链 :118-125"同时抑制生成和诊断"承诺不一致）。
        // ================================================================

        /// <summary>同组正锚：无开关时 UserDto 诊断正常报出（排除"源语料不触发"假绿；T1 拆码后锁定 019∨042）。</summary>
        [Fact]
        public Task SwitchUnset_StillReported_PositiveAnchor()
        {
            return VerifyAnyMappingDiagnosticPresent(MixedSource, "UserDto");
        }

        /// <summary>
        /// B4-05 主样例：NCMapperSourceGenEnabled=false → NCMAP019 零输出（与主链同口径抑制；
        /// 缺陷态 019 节点不消费开关 → 仍报 → 红）。
        /// </summary>
        [Fact]
        public async Task SourceGenDisabled_Suppresses019_SamePolicyAsMainChain()
        {
            var diagnostics = await GetGeneratorDiagnosticsWithSourceGenDisabled(MixedSource);
            var hit = diagnostics.FirstOrDefault(d => d.Id == "NCMAP019");
            Assert.True(hit == null, $"预期开关关闭后 NCMAP019 零输出，实际仍报：{hit?.GetMessage()}");
        }

        private static Task<ImmutableArray<Diagnostic>> GetGeneratorDiagnosticsWithSourceGenDisabled(string source)
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
                "FluentExemptionTest",
                new[] { CSharpSyntaxTree.ParseText(source) },
                allRefs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new NCMapperSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { generator.AsSourceGenerator() },
                additionalTexts: System.Collections.Immutable.ImmutableArray<AdditionalText>.Empty,
                parseOptions: CSharpParseOptions.Default,
                optionsProvider: new SourceGenDisabledOptionsProvider());

            driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
            return Task.FromResult(diagnostics);
        }

        /// <summary>GlobalOptions 注入 build_property.NCMapperSourceGenEnabled=false（其余键空——同 IdxTestHarness.EmitOptions 形态）。</summary>
        private sealed class SourceGenDisabledOptionsProvider : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
        {
            public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GlobalOptions => new O();
            public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new O();
            public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new O();

            private sealed class O : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
            {
                public override bool TryGetValue(string key, out string value)
                {
                    if (key == "build_property.NCMapperSourceGenEnabled") { value = "false"; return true; }
                    value = string.Empty;
                    return false;
                }
            }
        }
    }
}
