// 行 1——NCMAP019 治理特性正测试全量矩阵（D10 后口径）
//   判定清单 = MappingExtractor.IsMappingAttributeClass 改定后 10 项（含 MapFrom 早退技术项；
//   治理正测试面 = 其余 9 项：MapProperty/MapIgnore/Mask/Convert/ConvertBack/MapNoAutoInclude/
//   BeforeMapping/AfterMapping/MapWhen——[MapTree] 已按 D10 用户裁决移出（001 提交 8692150））。
//  隐含前提显式化（行 1 评审 002 I-3）：正测试类型须未标 [MapFrom] 且命名避开
//   Dto 后缀（防 NCMAP042 自触发干扰断言——#67 先验锚同源纪律）。
//  探针吸收定案（登记）：探针三件独立保留（NCMAP042SplitProbeTests 已覆盖场景①/②分流、
//   [Mask]+Dto 边界、[MapSubclass] 负例 F49、[MapTree] D10 反向锁定；NCMAP025LocationProbeTests
//   覆盖 025 三场景；ConvertAttributeRenameProbeTests 覆盖改名行）——本文件只补矩阵缺口，不重复断言。
using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Threading.Tasks;
using System.Collections.Immutable;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// NCMAP019 治理特性正测试矩阵——九项逐项锁定"映射配置标注无锚 → 019 Error"目标态
    /// <para> 行 1（承接）；D9/D10 裁决后口径。</para>
    /// </summary>
    public class NCMAP019GovernanceMatrixTests
    {
        // 锚点源码片段：语料结构与对照需要——**非 019/042 输出前提**（口径订正
        // 零 [MapFrom] 编译单元下 019 Error 与 042 Info 照报，回归锁见
        // NCMAP019ZeroAnchorUnitRegressionTests）
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

        // ── 九项正测试（每项：类型未标 [MapFrom] + 非 Dto 命名 + 标注对应治理特性 → 019 Error）──

        /// <summary>1/9 MapProperty（属性级）：无锚标注 → 019 Error、不报 042。</summary>
        [Fact]
        public async Task MapProperty_NoAnchor_Reports019_Error()
        {
            var source = Anchor + """
                public class OrderView
                {
                    [MapProperty(typeof(AnchorEntity), nameof(AnchorEntity.Id))]
                    public int Id { get; set; }
                }
                """;
            await Assert019ErrorOnly(source);
        }

        /// <summary>2/9 MapIgnore（属性级）：无锚标注 → 019 Error、不报 042。</summary>
        [Fact]
        public async Task MapIgnore_NoAnchor_Reports019_Error()
        {
            var source = Anchor + """
                public class AuditView
                {
                    [MapIgnore(NCMapper.Attributes.Direction.ToDto)]
                    public string AuditStamp { get; set; } = "";
                }
                """;
            await Assert019ErrorOnly(source);
        }

        /// <summary>3/9 Mask（属性级）：无锚标注 → 019 Error、不报 042（探针②同族矩阵侧复锁定；合法 Type 构造形态）。</summary>
        [Fact]
        public async Task Mask_NoAnchor_Reports019_Error()
        {
            var source = Anchor + """
                public class ContactView
                {
                    [Mask(NCMapper.MaskPhone)]
                    public string Phone { get; set; } = "";
                }
                """;
            await Assert019ErrorOnly(source);
        }

        /// <summary>4/9 Convert（属性级，改名后新名）：无锚标注 → 019 Error、不报 042。</summary>
        [Fact]
        public async Task Convert_NoAnchor_Reports019_Error()
        {
            var source = Anchor + """
                using System;

                public static class GmConverters
                {
                    public static string StatusText(int v) => v.ToString();
                }

                public class StatusView
                {
                    [Convert(typeof(GmConverters), nameof(GmConverters.StatusText))]
                    public int Id { get; set; }
                }
                """;
            await Assert019ErrorOnly(source);
        }

        /// <summary>5/9 ConvertBack（属性级，改名后新名）：无锚标注 → 019 Error、不报 042。</summary>
        [Fact]
        public async Task ConvertBack_NoAnchor_Reports019_Error()
        {
            var source = Anchor + """
                using System;

                public static class GmBackConverters
                {
                    public static int ParseId(string v) => int.Parse(v);
                }

                public class ParseView
                {
                    [ConvertBack(typeof(GmBackConverters), nameof(GmBackConverters.ParseId))]
                    public int Id { get; set; }
                }
                """;
            await Assert019ErrorOnly(source);
        }

        /// <summary>6/9 MapNoAutoInclude（属性级）：无锚标注 → 019 Error、不报 042。</summary>
        [Fact]
        public async Task MapNoAutoInclude_NoAnchor_Reports019_Error()
        {
            var source = Anchor + """
                public class CatalogView
                {
                    public int Id { get; set; }

                    [MapNoAutoInclude]
                    public System.Collections.Generic.List<string>? Tags { get; set; }
                }
                """;
            await Assert019ErrorOnly(source);
        }

        /// <summary>7/9 BeforeMapping（类级——AttributeTargets.Class）：无锚标注 → 019 Error、不报 042。</summary>
        [Fact]
        public async Task BeforeMapping_NoAnchor_Reports019_Error()
        {
            var source = Anchor + """
                [BeforeMapping(nameof(Normalize))]
                public class HookView
                {
                    internal static void Normalize(AnchorEntity source, HookView target) { }

                    public int Id { get; set; }
                }
                """;
            await Assert019ErrorOnly(source);
        }

        /// <summary>8/9 AfterMapping（类级——AttributeTargets.Class）：无锚标注 → 019 Error、不报 042。</summary>
        [Fact]
        public async Task AfterMapping_NoAnchor_Reports019_Error()
        {
            var source = Anchor + """
                [AfterMapping(nameof(Enrich))]
                public class EnrichView
                {
                    internal static void Enrich(AnchorEntity source, EnrichView target) { }

                    public int Id { get; set; }
                }
                """;
            await Assert019ErrorOnly(source);
        }

        /// <summary>9/9 MapWhen（属性级）：无锚标注 → 019 Error、不报 042。</summary>
        [Fact]
        public async Task MapWhen_NoAnchor_Reports019_Error()
        {
            var source = Anchor + """
                public class GuardedView
                {
                    public int Id { get; set; }

                    [MapWhen(nameof(HasContent))]
                    public string Note { get; set; } = "";

                    internal static bool HasContent(string v) => !string.IsNullOrEmpty(v);
                }
                """;
            await Assert019ErrorOnly(source);
        }

        // ── 042 Info 三断言中的矩阵缺口补齐：Fluent 目标豁免锁定断言 ──
        //（探针①已锁定"命名命中报 042"；探针③已锁定"含映射配置标注时报 019 不报 042"；
        //  Fluent 豁免面 = NCSGen :136 豁免守卫对 019/042 两码同用（显式决策）——
        //  既有 NCMAP019FluentExemptionTests 仅断言 019 豁免，042 豁免显式断言为本矩阵缺口补齐）

        /// <summary>
        /// 042 Fluent 目标豁免锁定：Dto 命名类经 cfg.For&lt;S,T&gt;() 声明后 019/042 双豁免
        /// <para>先验锚：同语料无 For 声明时 042 必报（探针① DtoNaming_NoAnchor_Reports042_Not019）。</para>
        /// </summary>
        [Fact]
        public async Task FluentDeclaredDtoNaming_Exempt_042_Not019()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;

                public class FmEntity
                {
                    public int Id { get; set; }
                }

                public class FmDto
                {
                    public int Id { get; set; }
                }

                [MapperConfig]
                public class FmConfig
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FmEntity, FmDto>().Member(d => d.Id).From(s => s.Id);
                    }
                }
                """;
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP019");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP042");
        }

        // ================================================================
        // 辅助：019 Error 单一命中断言（Anchor 为语料先验锚；mappings 非空**不是** 019 输出前提——
        // 口径订正）
        // ================================================================

        private static async Task Assert019ErrorOnly(string source)
        {
            var (_, diagnostics) = await CompileAndGetDiagnostics(source);
            var d019 = Assert.Single(diagnostics, d => d.Id == "NCMAP019");
            Assert.Equal(DiagnosticSeverity.Error, d019.Severity);
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP042");
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
                "NCMAP019GovernanceMatrix",
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
