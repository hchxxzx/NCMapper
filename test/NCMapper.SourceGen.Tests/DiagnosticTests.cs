using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Threading.Tasks;
using System.Collections.Immutable;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// NCMAP001-004 诊断描述符测试 — 验证源生成器编译时诊断正确触发
    /// </summary>
    public class DiagnosticTests
    {
        /// <summary>
        /// NCMAP001 — 源字段未找到时产生警告
        /// </summary>
        [Fact]
        public async Task NCMAP001_SourceFieldNotFound_Warning()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    [MapProperty(typeof(SourceEntity), "NonExistentField")]
                    public string SomeProp { get; set; } = "";
                }
                """;

            await VerifyDiagnostic(source, "NCMAP001");
        }

        /// <summary>
        /// NCMAP002 — 类型不兼容时产生警告
        /// </summary>
        [Fact]
        public async Task NCMAP002_TypeIncompatible_Warning()
        {
            var source = """
                using NCMapper.Attributes;
                using System;

                public class SourceEntity { public int Value { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    // int → DateTime 不兼容 → NCMAP002 类型不兼容
                    public DateTime Value { get; set; }
                    // string → string 兼容，不触发诊断
                    public string Name { get; set; } = "";
                }
                """;

            await VerifyDiagnostic(source, "NCMAP002");
        }

        /// <summary>
        /// NCMAP003 — 无可映射属性时产生警告
        /// </summary>
        [Fact]
        public async Task NCMAP003_NoPropertiesMapped_Warning()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntity { public int Id { get; set; } }

                [MapFrom(typeof(SourceEntity))]
                public class EmptyTargetDto
                {
                    [MapProperty(typeof(SourceEntity), "NonExistent")]
                    public string Prop1 { get; set; } = "";
                }
                """;

            await VerifyDiagnostic(source, "NCMAP003");
        }

        /// <summary>
        /// NCMAP002 — 类型不兼容时产生警告（源/目标属性同名但类型不兼容）
        /// M-05: 原 NCMAP004 已合并到 NCMAP002，不再重复触发
        /// </summary>
        [Fact]
        public async Task NCMAP004_ProjectionSkipped_Warning()
        {
            var source = """
                using NCMapper.Attributes;
                using System;

                public class SourceEntity { public DateTime CreatedAt { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    // DateTime → int 不兼容 → NCMAP002 类型不兼容
                    public int CreatedAt { get; set; }
                    // string → string 兼容，不触发诊断
                    public string Name { get; set; } = "";
                }
                """;

            await VerifyDiagnostic(source, "NCMAP002");
        }

        /// <summary>
        /// NCMAP002 — 类型兼容时不产生诊断（同类型属性正常映射）
        /// </summary>
        [Fact]
        public async Task NCMAP004_CompatibleType_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class CompatibleEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(CompatibleEntity))]
                public class CompatibleDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP002");
        }

        /// <summary>
        /// 正常映射不产生任何诊断
        /// </summary>
        [Fact]
        public async Task ValidMapping_NoDiagnostics()
        {
            var source = """
                using NCMapper.Attributes;

                public class GoodEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(GoodEntity))]
                public class GoodDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP001");
            await VerifyNoDiagnostic(source, "NCMAP002");
            await VerifyNoDiagnostic(source, "NCMAP003");
        }

        /// <summary>
        /// 验证指定诊断 ID 被触发
        /// </summary>
        private static async Task VerifyDiagnostic(string source, string diagnosticId)
        {
            var (compilation, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.Contains(diagnostics, d => d.Id == diagnosticId);
        }

        /// <summary>
        /// 验证指定诊断 ID 未被触发
        /// </summary>
        private static async Task VerifyNoDiagnostic(string source, string diagnosticId)
        {
            var (compilation, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == diagnosticId);
        }

        /// <summary>
        /// 编译源码并获取诊断
        /// </summary>
        private static async Task<(Compilation, ImmutableArray<Diagnostic>)> CompileAndGetDiagnostics(string source)
        {
            // 引用 NCMapper 程序集
            var ncmapperAssembly = typeof(NCMapper.Mapper).Assembly.Location;

            // 收集所有必要的引用
            var refAssemblies = new[]
            {
                typeof(object).Assembly.Location,
                ncmapperAssembly,
                typeof(System.Runtime.CompilerServices.MethodImplAttribute).Assembly.Location,
            };

            // 添加运行时目录中的核心程序集
            var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var coreAssemblies = new[]
            {
                System.IO.Path.Combine(runtimeDir, "System.Runtime.dll"),
                System.IO.Path.Combine(runtimeDir, "System.Collections.dll"),
                System.IO.Path.Combine(runtimeDir, "System.Linq.dll"),
                System.IO.Path.Combine(runtimeDir, "System.Linq.Expressions.dll"),
            };

            var allRefs = refAssemblies.Concat(coreAssemblies)
                .Where(System.IO.File.Exists)
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToList();

            var compilation = CSharpCompilation.Create(
                "DiagnosticTest",
                new[] { CSharpSyntaxTree.ParseText(source) },
                allRefs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new NCMapperSourceGenerator();
            var driver = CSharpGeneratorDriver.Create(generator);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

            return (updatedCompilation, diagnostics);
        }

        // ================================================================
        // NCMAP005 — Compose 跨表字段未找到
        // ================================================================

        /// <summary>
        /// NCMAP005 — Compose 跨表字段未找到时产生警告
        /// </summary>
        [Fact]
        public async Task NCMAP005_ComposeFieldNotFound_Warning()
        {
            var source = """
                using NCMapper.Attributes;

                public class SubSource { public int Id { get; set; } public string Name { get; set; } = ""; }
                public class MainSource { public int Id { get; set; } public SubSource? Sub { get; set; } }

                [MapFrom(typeof(MainSource))]
                public class ComposeDto
                {
                    public int Id { get; set; }
                    [MapProperty(typeof(SubSource), "NonExistentField")]
                    public string? SubName { get; set; }
                }
                """;

            await VerifyDiagnostic(source, "NCMAP005");
        }

        /// <summary>
        /// NCMAP005 邻界反例 — Compose 跨表字段存在（合法形态）时不报
        /// </summary>
        [Fact]
        public async Task NCMAP005_ComposeFieldFound_NoWarning()
        {
            var source = """
                using NCMapper.Attributes;

                public class SubSourceOk { public int Id { get; set; } public string Name { get; set; } = ""; }
                public class MainSourceOk { public int Id { get; set; } public SubSourceOk? Sub { get; set; } }

                [MapFrom(typeof(MainSourceOk))]
                public class ComposeOkDto
                {
                    public int Id { get; set; }
                    [MapProperty(typeof(SubSourceOk), "Name")]
                    public string? SubName { get; set; }
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP005");
        }

        // ================================================================
        // NCMAP006 — 目标属性未映射
        // ================================================================

        /// <summary>
        /// NCMAP001 — 目标属性找不到匹配源属性时产生警告
        /// M-05: 原 NCMAP006 已合并到 NCMAP001，不再重复触发
        /// 处置：默认不报（NCMAP001 开关默认 false），本用例显式标注 ReportUnmappedTargetMembers=true 保留原断言
        /// </summary>
        [Fact]
        public async Task NCMAP006_UnmappedTargetMember_Warning()
        {
            var source = """
                using NCMapper.Attributes;

                [assembly: NCMapperOptions(ReportUnmappedTargetMembers = true)]

                public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                    public string UnmappedProp { get; set; } = "";
                }
                """;

            await VerifyDiagnostic(source, "NCMAP001");
        }

        /// <summary>
        /// NCMAP001 — 标注了 [MapIgnore] 的属性不触发源字段未找到警告
        /// </summary>
        [Fact]
        public async Task NCMAP006_MapIgnore_NoWarning()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntity { public int Id { get; set; } }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public int Id { get; set; }
                    [MapIgnore]
                    public string? IgnoredProp { get; set; }
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP001");
        }

        /// <summary>
        /// NCMAP006 正例 — 目标属性 setter 非 public 且源存在同名属性 → 发出 Warning（方案C 发射路径）
        /// </summary>
        [Fact]
        public async Task NCMAP006_NonPublicSetter_SameNamedSource_Warning()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntityNw { public string Name { get; set; } = ""; }

                [MapFrom(typeof(SourceEntityNw))]
                public class TargetDtoNw
                {
                    public string Name { get; private set; } = "";
                }
                """;

            await VerifyDiagnostic(source, "NCMAP006");
        }

        /// <summary>
        /// NCMAP006 邻界反例 — 目标属性 setter 非 public 但源无同名属性 → 不报（不构成"未映射"）
        /// </summary>
        [Fact]
        public async Task NCMAP006_NonPublicSetterNoSameNamedSource_NoWarning()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntityNw2 { public int Id { get; set; } }

                [MapFrom(typeof(SourceEntityNw2))]
                public class TargetDtoNw2
                {
                    public int Id { get; set; }
                    public string Extra { get; private set; } = "";
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP006");
        }

        /// <summary>
        /// NCMAP001— 方向性 [MapIgnore(Direction.ToDto)] 的属性不触发源字段未找到警告。
        /// 修复前：方向性 [MapIgnore] 仅 L127 双向跳过，单方向忽略属性仍进源字段匹配 → 源无字段 → 误报 NCMAP001。
        /// </summary>
        [Fact]
        public async Task NCMAP001_MapIgnoreDirectionalToDto_NoWarning()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntity { public int Id { get; set; } }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public int Id { get; set; }
                    [MapIgnore(Direction.ToDto)]
                    public string? DtoOnlyProp { get; set; }
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP001");
        }

        /// <summary>
        /// NCMAP001— 方向性 [MapIgnore(Direction.ToEntity)] 的属性不触发源字段未找到警告。
        /// </summary>
        [Fact]
        public async Task NCMAP001_MapIgnoreDirectionalToEntity_NoWarning()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntity { public int Id { get; set; } }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public int Id { get; set; }
                    [MapIgnore(Direction.ToEntity)]
                    public string? EntityOnlyProp { get; set; }
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP001");
        }

        /// <summary>
        /// NCMAP001（保真）— 显式 [MapProperty] 的 FromField 在源中不存在，仍报 NCMAP001
        /// （真实错误保留，豁免仅覆盖含 [MapIgnore] 的属性）。
        /// </summary>
        [Fact]
        public async Task NCMAP001_ExplicitMapProperty_StillWarns()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntity { public int Id { get; set; } }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public int Id { get; set; }
                    [MapProperty(typeof(SourceEntity), "NonExistentField")]
                    public string? BadFieldProp { get; set; }
                }
                """;

            await VerifyDiagnostic(source, "NCMAP001");
        }

        // ================================================================
        // NCMAP007 — 源属性未映射
        // ================================================================

        /// <summary>
        /// NCMAP007 — 源属性没有对应目标属性时产生 Info
        /// </summary>
        [Fact]
        public async Task NCMAP007_UnmappedSourceMember_Info()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntity { public int Id { get; set; } public string ExtraProp { get; set; } = ""; }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public int Id { get; set; }
                }
                """;

            await VerifyDiagnostic(source, "NCMAP007");
        }

        // ================================================================
        // NCMAP009 — 重复映射源属性
        // ================================================================

        /// <summary>
        /// NCMAP009 — 多个目标属性指向同一源属性时产生警告
        /// </summary>
        [Fact]
        public async Task NCMAP009_DuplicateFieldMapping_Warning()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    [MapProperty(typeof(SourceEntity), "Name")]
                    public string Name1 { get; set; } = "";
                    [MapProperty(typeof(SourceEntity), "Name")]
                    public string Name2 { get; set; } = "";
                }
                """;

            await VerifyDiagnostic(source, "NCMAP009");
        }

        // ================================================================
        // NCMAP010 — 枚举映射成员无重叠
        // ================================================================

        /// <summary>
        /// NCMAP010 — 源枚举和目标枚举成员名称完全不同时产生警告
        /// </summary>
        [Fact]
        public async Task NCMAP010_EnumNoOverlap_Warning()
        {
            var source = """
                using NCMapper.Attributes;

                public enum SourceEnum { Alpha, Beta, Gamma }
                public enum TargetEnum { Red, Green, Blue }

                public class SourceEntity { public SourceEnum Color { get; set; } }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public TargetEnum Color { get; set; }
                }
                """;

            await VerifyDiagnostic(source, "NCMAP010");
        }

        /// <summary>
        /// NCMAP010 — 枚举成员有重叠时不触发警告
        /// </summary>
        [Fact]
        public async Task NCMAP010_EnumWithOverlap_NoWarning()
        {
            var source = """
                using NCMapper.Attributes;

                public enum SourceEnum { Low, Medium, High }
                public enum TargetEnum { Low, Medium, High, Critical }

                public class SourceEntity { public SourceEnum Priority { get; set; } }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public TargetEnum Priority { get; set; }
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP010");
        }

        // ================================================================
        // NCMAP008 已退役（接口形态 IValueConverter 泛型校验职责由 NCMAP035/036/037 取代，
        // 见 AnalyzerReleases.Unshipped.md）— 以下用例为原 008 三用例的静态方法形态语义迁移
        // ================================================================

        /// <summary>
        /// NCMAP035 — 锚定类型上不存在同名静态方法时产生 Error
        /// <para>（原 NCMAP008_ConverterNotImplementingInterface_Error 语义迁移：
        /// 旧形态"未实现接口"在新形态下即"锚定类型无可解析方法"）</para>
        /// </summary>
        [Fact]
        public async Task NCMAP035_ConverterMethodNotExists_Error()
        {
            var source = """
                using NCMapper.Attributes;
                using NCMapper;

                // 非转换器类型 — 无任何静态 Convert 方法
                public class NotAConverter { }

                public class SourceEntity { public int Value { get; set; } }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    [Convert(typeof(NotAConverter), "Convert")]
                    public string Value { get; set; } = "";
                }
                """;

            await VerifyDiagnostic(source, "NCMAP035");
        }

        /// <summary>
        /// NCMAP037 — 转换方法签名不匹配（四模板无命中）时产生 Error
        /// <para>（原 NCMAP008_ConverterGenericArgsMismatch_Error 语义迁移：
        /// 旧形态"泛型 TField 错配"在新形态下即"M(DateTime) 对 int 字段无模板命中"）</para>
        /// </summary>
        [Fact]
        public async Task NCMAP037_ConverterSignatureMismatch_Error()
        {
            var source = """
                using NCMapper.Attributes;
                using NCMapper;

                // T1 模板期望 M(int)（字段类型），实际 M(DateTime) → 四模板无命中
                public static class WrongConverter
                {
                    public static string Convert(DateTime fieldValue) => fieldValue.ToString("yyyy-MM-dd");
                }

                public class SourceEntity { public int Value { get; set; } }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    [Convert(typeof(WrongConverter), nameof(WrongConverter.Convert))]
                    public string Value { get; set; } = "";
                }
                """;

            await VerifyDiagnostic(source, "NCMAP037");
        }

        /// <summary>
        /// 静态方法签名匹配（T1 模板 M(TField)）时不产生诊断
        /// <para>（原 NCMAP008_ConverterGenericArgsMatch_NoDiagnostic 语义迁移）</para>
        /// </summary>
        [Fact]
        public async Task NCMAP037_ConverterSignatureMatch_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;
                using NCMapper;

                // T1 模板：M(int) 与字段类型匹配
                public static class IntToStringConverter
                {
                    public static string Convert(int fieldValue) => fieldValue.ToString();
                }

                public class SourceEntity { public int Value { get; set; } }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    [Convert(typeof(IntToStringConverter), nameof(IntToStringConverter.Convert))]
                    public string Value { get; set; } = "";
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP035");
            await VerifyNoDiagnostic(source, "NCMAP036");
            await VerifyNoDiagnostic(source, "NCMAP037");
        }

        // ================================================================
        // NCMAP011 — 非 public 类型跳过源生成器
        // ================================================================

        /// <summary>
        /// NCMAP011 — internal 类型标注 [MapFrom] 产生 Info 级诊断
        /// <para>源生成器生成的 GeneratedMapper 是 public static class，无法访问 internal 类型，
        /// 强行生成会导致 CS0122。internal 类型自动 fallback 到运行时引擎路径。</para>
        /// </summary>
        [Fact]
        public async Task NCMAP011_InternalType_Skipped()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(SourceEntity))]
                internal class InternalTargetDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                }
                """;

            await VerifyDiagnostic(source, "NCMAP011");
        }

        /// <summary>
        /// NCMAP011 — private 嵌套类型标注 [MapFrom] 产生 Info 级诊断
        /// <para>典型场景：测试项目的 private 嵌套 mutant 模型</para>
        /// </summary>
        [Fact]
        public async Task NCMAP011_PrivateNestedType_Skipped()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                public class OuterClass
                {
                    [MapFrom(typeof(SourceEntity))]
                    private class PrivateNestedDto
                    {
                        public int Id { get; set; }
                        public string Name { get; set; } = "";
                    }
                }
                """;

            await VerifyDiagnostic(source, "NCMAP011");
        }

        /// <summary>
        /// NCMAP011 — public 类型不产生诊断（负向验证）
        /// <para>public 类型是源生成器的正常处理对象，不应触发 NCMAP011</para>
        /// </summary>
        [Fact]
        public async Task NCMAP011_PublicType_NotSkipped()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(SourceEntity))]
                public class PublicTargetDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP011");
        }

        // ================================================================
        // NCMAP017 — [MapNoAutoInclude] 标注在引用属性上（Info）
        // ================================================================

        /// <summary>
        /// NCMAP017 — [MapNoAutoInclude] 标注在引用类型（非集合）属性上时产生 Info
        /// </summary>
        [Fact]
        public async Task NCMAP017_NoAutoIncludeOnReference_Info()
        {
            var source = """
                using NCMapper.Attributes;

                public class Address { public string City { get; set; } = ""; }
                public class SourceEntity { public int Id { get; set; } public Address? Address { get; set; } }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public int Id { get; set; }
                    [MapNoAutoInclude]
                    public Address? Address { get; set; }
                }
                """;

            await VerifyDiagnostic(source, "NCMAP017");
        }

        /// <summary>
        /// NCMAP017 — [MapNoAutoInclude] 标注在集合属性上不触发（集合是合理使用场景）
        /// </summary>
        [Fact]
        public async Task NCMAP017_NoAutoIncludeOnCollection_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;
                using System.Collections.Generic;

                public class OrderItem { public string Name { get; set; } = ""; }
                public class SourceEntity { public int Id { get; set; } public List<OrderItem> Items { get; set; } = new(); }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public int Id { get; set; }
                    [MapNoAutoInclude]
                    public List<OrderItem> Items { get; set; } = new();
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP017");
        }

        /// <summary>
        /// NCMAP017 — [MapNoAutoInclude] 标注在 string 属性上不触发（string 为特殊类型，非导航）
        /// </summary>
        [Fact]
        public async Task NCMAP017_NoAutoIncludeOnString_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public int Id { get; set; }
                    [MapNoAutoInclude]
                    public string Name { get; set; } = "";
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP017");
        }

        /// <summary>
        /// NCMAP017 — [MapNoAutoInclude] 标注在值类型（struct）属性上不触发（TypeKind != Class）
        /// </summary>
        [Fact]
        public async Task NCMAP017_NoAutoIncludeOnStruct_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;
                using System;

                public class SourceEntity { public int Id { get; set; } public DateTime CreatedAt { get; set; } }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public int Id { get; set; }
                    [MapNoAutoInclude]
                    public DateTime CreatedAt { get; set; }
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP017");
        }

        /// <summary>
        /// NCMAP017 — [MapNoAutoInclude] 标注在数组属性上不触发（数组实现 IEnumerable，属集合）
        /// </summary>
        [Fact]
        public async Task NCMAP017_NoAutoIncludeOnArray_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Tag { public string Label { get; set; } = ""; }
                public class SourceEntity { public int Id { get; set; } public Tag[] Tags { get; set; } = []; }

                [MapFrom(typeof(SourceEntity))]
                public class TargetDto
                {
                    public int Id { get; set; }
                    [MapNoAutoInclude]
                    public Tag[] Tags { get; set; } = [];
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP017");
        }
    }
}
