using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Threading.Tasks;
using System.Collections.Immutable;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// （任务书 20260901-001）解析器规则级单测 — MappingExtractor.ResolveConverterMethod
    /// <para>覆盖 R2-R7：锚定类型 / DeclaredOnly + 可访问性白名单 / 名称 Ordinal /
    /// 形态排除 / 四模板 + Q3 优先序 + 相等匹配（不做 assignable 放宽）/ Nullable 解包与提升 /
    /// R7 nameof 截断 / R6-b 无匹配源属性 / 反向 [ConvertBack] 基准。</para>
    /// <para>桥接期断言口径为诊断级（NCMAP035/036/037 触发与否）；Map() 行为级断言随
    /// 发射消费落地后并入集成矩阵（任务书 5.3 测试归属注）。</para>
    /// </summary>
    public class ConverterMethodResolutionTests
    {
        // ================================================================
        // 编译辅助（与 DiagnosticTests 同构：仅断言生成器诊断，不检用户/生成代码 CS 错误）
        // ================================================================

        private static async Task<ImmutableArray<Diagnostic>> CompileAndGetDiagnostics(string source)
        {
            var ncmapperAssembly = typeof(NCMapper.Mapper).Assembly.Location;

            var refAssemblies = new[]
            {
                typeof(object).Assembly.Location,
                ncmapperAssembly,
                typeof(System.Runtime.CompilerServices.MethodImplAttribute).Assembly.Location,
            };

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
                "ConverterMethodResolutionTest",
                new[] { CSharpSyntaxTree.ParseText(source) },
                allRefs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new NCMapperSourceGenerator();
            var driver = CSharpGeneratorDriver.Create(generator);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

            return diagnostics;
        }

        /// <summary>断言指定诊断被触发</summary>
        private static async Task AssertDiagnostic(string source, string diagnosticId)
        {
            var diagnostics = await CompileAndGetDiagnostics(source);
            Assert.Contains(diagnostics, d => d.Id == diagnosticId);
        }

        /// <summary>断言解析器三诊断（NCMAP035/036/037）均未触发（正例：解析成功）</summary>
        private static async Task AssertNoResolverDiagnostic(string source)
        {
            var diagnostics = await CompileAndGetDiagnostics(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP035");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP036");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP037");
        }

        // ================================================================
        // R2 锚定类型（双参=显式类型；单参=标注属性所属类型）
        // ================================================================

        /// <summary>R2/双参形态：锚定类型=显式传入的转换器类，T1 命中 → 无诊断</summary>
        [Fact]
        public async Task R2_DualForm_ExternalConverterClass_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class StringConv
                {
                    public static string Upper(string s) => s.ToUpperInvariant();
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(StringConv), nameof(StringConv.Upper))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>R2/单参形态：锚定类型=标注属性所属类型（DTO 自身声明的静态方法），T1 命中 → 无诊断</summary>
        [Fact]
        public async Task R2_SingleForm_MethodOnDtoContainingType_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(nameof(Echo))]
                    public string Name { get; set; } = "";

                    public static string Echo(string s) => s;
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>R2 细则 3：双参形态同样 DeclaredOnly——方法声明于基类时不参与解析 → NCMAP035</summary>
        [Fact]
        public async Task R2_DualForm_BaseTypeMethod_DeclaredOnly_NCMAP035()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public class ConvBase { public static string M(string s) => s; }
                public class ConvDerived : ConvBase { }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(ConvDerived), nameof(ConvBase.M))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP035");
        }

        // ================================================================
        // R3 可访问性白名单（6 种 C# 可访问性 × 判定，参数化）
        // ================================================================

        public static TheoryData<string, bool> AccessibilityCases => new()
        {
            // (可访问性, 是否期望 NCMAP035)：public / internal(同程序集) / protected internal(同程序集腿) 允许；
            // protected / private protected / private 拒绝（生成器产物与 DTO 同程序集、非嵌套、无派生关系）
            { "public", false },
            { "internal", false },
            { "protected internal", false },
            { "protected", true },
            { "private protected", true },
            { "private", true },
        };

        [Theory]
        [MemberData(nameof(AccessibilityCases))]
        public async Task R3_AccessibilityWhitelist_Parameterized(string accessibility, bool expectNcmap035)
        {
            var source = $$"""
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public class AccConv
                {
                    {{accessibility}} static string M(string s) => s;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(AccConv), nameof(AccConv.M))]
                    public string Name { get; set; } = "";
                }
                """;

            if (expectNcmap035)
                await AssertDiagnostic(source, "NCMAP035");
            else
                await AssertNoResolverDiagnostic(source);
        }

        // ================================================================
        // R4 名称匹配（Ordinal 大小写敏感精确匹配）
        // ================================================================

        /// <summary>R4：方法名大小写不一致（"upper" vs Upper）→ NCMAP035</summary>
        [Fact]
        public async Task R4_NameCaseMismatch_Ordinal_NCMAP035()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class StringConv
                {
                    public static string Upper(string s) => s.ToUpperInvariant();
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(StringConv), "upper")]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP035");
        }

        // ================================================================
        // R5 形态排除（同名静态可达方法被形态排除 → NCMAP037，细节逐一登记）
        // ================================================================

        /// <summary>R5：仅存在泛型方法 → 形态排除后无候选 → NCMAP037</summary>
        [Fact]
        public async Task R5_GenericMethodOnly_NCMAP037()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class GenConv
                {
                    public static string M<T>(string s) => s;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(GenConv), nameof(GenConv.M))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP037");
        }

        /// <summary>R5：仅存在可选参数方法 → NCMAP037</summary>
        [Fact]
        public async Task R5_OptionalParameterOnly_NCMAP037()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class OptConv
                {
                    public static string M(string s = "") => s;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(OptConv), nameof(OptConv.M))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP037");
        }

        /// <summary>R5：仅存在 params 数组方法 → NCMAP037</summary>
        [Fact]
        public async Task R5_ParamsArrayOnly_NCMAP037()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class ParamsConv
                {
                    public static string M(params string[] xs) => "";
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(ParamsConv), nameof(ParamsConv.M))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP037");
        }

        /// <summary>R5：仅存在 ref 参数方法 → NCMAP037</summary>
        [Fact]
        public async Task R5_RefParameterOnly_NCMAP037()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class RefConv
                {
                    public static string M(ref string s) => s;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(RefConv), nameof(RefConv.M))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP037");
        }

        /// <summary>R5/NCMAP035 判据①：仅存在同名实例方法（无静态方法）→ NCMAP035</summary>
        [Fact]
        public async Task R5_InstanceMethodOnly_NCMAP035()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public class InstConv
                {
                    public string M(string s) => s;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(InstConv), nameof(InstConv.M))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP035");
        }

        // ================================================================
        // R6 四模板（T0-T3）+ 相等匹配 + Q3 优先序 + Nullable 口径
        // ================================================================

        /// <summary>R6/T0：static TTarget M() 无参常量模板命中 → 无诊断</summary>
        [Fact]
        public async Task R6_T0_NoArgConstant_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class T0Conv
                {
                    public static string Stamp() => "x";
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(T0Conv), nameof(T0Conv.Stamp))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>R6/T1：static TTarget M(TField) 单值模板命中 → 无诊断</summary>
        [Fact]
        public async Task R6_T1_SingleValue_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class T1Conv
                {
                    public static string Upper(string s) => s.ToUpperInvariant();
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(T1Conv), nameof(T1Conv.Upper))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>R6/T2：static TTarget M(TSource) 多字段模板命中（参数=源实体类型）→ 无诊断</summary>
        [Fact]
        public async Task R6_T2_MultiField_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class T2Conv
                {
                    public static string Describe(Src s) => s.Name;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(T2Conv), nameof(T2Conv.Describe))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>R6/T3：static TTarget M(TSource, TField) 组合模板命中 → 无诊断</summary>
        [Fact]
        public async Task R6_T3_Combined_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class T3Conv
                {
                    public static string Describe(Src s, string name) => name + s.Name;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(T3Conv), nameof(T3Conv.Describe))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>R6 口径 1：相等匹配不做 assignable 放宽——M(object) 对 string 字段（object 可从 string 隐式转换但不相等）→ NCMAP037</summary>
        [Fact]
        public async Task R6_AssignableWidening_Rejected_NCMAP037()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class WideConv
                {
                    public static string M(object o) => o.ToString() ?? "";
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(WideConv), nameof(WideConv.M))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP037");
        }

        /// <summary>R6 口径 1/3：Nullable 解包——字段 int?、参数 int、返回 int?、目标 int? → T1 命中（null 守卫口径）→ 无诊断</summary>
        [Fact]
        public async Task R6_NullableUnwrap_ParamUnderlying_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public int? Count { get; set; } }
                public static class NulConv
                {
                    public static int? Inc(int v) => v + 1;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(NulConv), nameof(NulConv.Inc))]
                    public int? Count { get; set; }
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>R6 口径 2：提升只允许「非空→可空」——返回 int?、目标 int（禁止降级）→ NCMAP037</summary>
        [Fact]
        public async Task R6_NullableDowngrade_Rejected_NCMAP037()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public int Count { get; set; } }
                public static class DownConv
                {
                    public static int? Inc(int v) => v + 1;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(DownConv), nameof(DownConv.Inc))]
                    public int Count { get; set; }
                }
                """;

            await AssertDiagnostic(source, "NCMAP037");
        }

        /// <summary>R6/，多个重载同时命中模板（M(string) 走 T1 + M(Src) 走 T2）→ NCMAP036</summary>
        [Fact]
        public async Task R6_OverloadAmbiguity_NCMAP036()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class AmbConv
                {
                    public static string M(string s) => s;
                    public static string M(Src s) => s.Name;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(AmbConv), nameof(AmbConv.M))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP036");
        }

        /// <summary>R6：参数 ≥3 一律不匹配 → NCMAP037</summary>
        [Fact]
        public async Task R6_ThreeParams_NCMAP037()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class TriConv
                {
                    public static string M(Src s, string name, int i) => name;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(TriConv), nameof(TriConv.M))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP037");
        }

        /// <summary>R2 细则 4：双参形态显式 typeof(开放泛型) → NCMAP037（指引改用封闭泛型）</summary>
        [Fact]
        public async Task R6_OpenGenericAnchor_NCMAP037()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public class GenericConv<T>
                {
                    public static string M(T t) => t?.ToString() ?? "";
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(GenericConv<>), nameof(GenericConv<string>.M))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP037");
        }

        // ================================================================
        // R7 nameof 限定名截断处置（单参解析域收紧 → 显性报错而非静默错配）
        // ================================================================

        /// <summary>R7：单参形态 nameof(OtherConv.M)——限定名被编译器丢弃，锚定类型（DTO）上无 M → NCMAP035</summary>
        [Fact]
        public async Task R7_SingleFormNameOfOtherType_Truncated_NCMAP035()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class OtherConv
                {
                    public static string M(string s) => s;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(nameof(OtherConv.M))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP035");
        }

        // ================================================================
        // R6-b 无匹配源属性路径（fieldType=null：仅形态匹配，不做字段类型交叉校验）
        // ================================================================

        /// <summary>R6-b/T0：无匹配源属性 + M() → 形态匹配命中 → 无诊断</summary>
        [Fact]
        public async Task R6b_T0_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class NullSrcConv
                {
                    public static string M() => "x";
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    public string Name { get; set; } = "";

                    [Convert(typeof(NullSrcConv), nameof(NullSrcConv.M))]
                    public string Extra { get; set; } = "";
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>R6-b/T1：无匹配源属性 + 单参方法（参数≠源实体类型，任意类型）→ 形态匹配命中 → 无诊断</summary>
        [Fact]
        public async Task R6b_T1_AnySingleParam_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class NullSrcConv
                {
                    public static string M(int v) => "x" + v;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    public string Name { get; set; } = "";

                    [Convert(typeof(NullSrcConv), nameof(NullSrcConv.M))]
                    public string Extra { get; set; } = "";
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>R6-b/T3 反例：无匹配源属性 + 双参但参0≠源实体类型 → 形态匹配失败 → NCMAP037</summary>
        [Fact]
        public async Task R6b_TwoParamsNotEntity_NCMAP037()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Name { get; set; } = ""; }
                public static class NullSrcConv
                {
                    public static string M(string a, string b) => a + b;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    public string Name { get; set; } = "";

                    [Convert(typeof(NullSrcConv), nameof(NullSrcConv.M))]
                    public string Extra { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP037");
        }

        // ================================================================
        // 反向 [ConvertBack] 方法引用形态（基准 004 定稿：TSource=DTO、TField=DTO 属性、TTarget=实体属性）
        // ================================================================

        /// <summary>反向：[ConvertBack(typeof(X), nameof(X.M))] T1 命中（参数=DTO 属性类型，返回=实体属性类型）→ 无诊断</summary>
        [Fact]
        public async Task ConvertBackUsing_NewForm_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Ent { public string Name { get; set; } = ""; }
                public static class BackConv
                {
                    public static string Norm(string s) => s.Trim();
                }

                [MapFrom(typeof(Ent))]
                public class Dto
                {
                    [ConvertBack(typeof(BackConv), nameof(BackConv.Norm))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>反向：返回类型 ≠ 实体属性类型（int vs string）→ NCMAP037</summary>
        [Fact]
        public async Task ConvertBackUsing_NewForm_ReturnMismatch_NCMAP037()
        {
            var source = """
                using NCMapper.Attributes;

                public class Ent { public string Name { get; set; } = ""; }
                public static class BackConv
                {
                    public static int Len(string s) => s.Length;
                }

                [MapFrom(typeof(Ent))]
                public class Dto
                {
                    [ConvertBack(typeof(BackConv), nameof(BackConv.Len))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP037");
        }

        /// <summary>反向：单参形态锚定类型=DTO 自身（R2 同构）→ NCMAP035（DTO 上无同名静态方法）</summary>
        [Fact]
        public async Task ConvertBackUsing_SingleForm_MethodNotOnDto_NCMAP035()
        {
            var source = """
                using NCMapper.Attributes;

                public class Ent { public string Name { get; set; } = ""; }

                [MapFrom(typeof(Ent))]
                public class Dto
                {
                    [ConvertBack(nameof(NotOnDto))]
                    public string Name { get; set; } = "";
                }
                """;

            await AssertDiagnostic(source, "NCMAP035");
        }

        // ================================================================
        // 矩阵补齐（封闭泛型 / partial / Q3 单方法 / 反向 T0）
        // ================================================================

        /// <summary>矩阵/封闭泛型：双参形态 typeof(Conv&lt;int&gt;) + nameof(Conv&lt;int&gt;.M) 封闭泛型锚定 → T1 命中无诊断
        /// <para>（与 R6_OpenGenericAnchor_NCMAP037 对偶：开放泛型指引改用封闭泛型的正例）</para></summary>
        [Fact]
        public async Task R6_ClosedGenericAnchor_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public int Code { get; set; } }
                public static class Conv<T>
                {
                    public static string Convert(T fieldValue) => "GEN:" + fieldValue;
                }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    [Convert(typeof(Conv<int>), nameof(Conv<int>.Convert))]
                    public string Code { get; set; } = "";
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>矩阵/partial：方法与特性标注分属两个 partial 部分（单参形态）→ 合并符号可解析 → 无诊断</summary>
        [Fact]
        public async Task R2_PartialDeclaration_SingleForm_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src { public string Note { get; set; } = ""; }

                [MapFrom(typeof(Src))]
                public partial class Dto
                {
                    [Convert(nameof(Mark))]
                    public string Note { get; set; } = "";
                }

                public partial class Dto
                {
                    public static string Mark(string fieldValue) => "P:" + fieldValue;
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>矩阵/Q3 单方法边界：单参同时==源实体类型==字段类型 → 判 T2 多字段（Q3 优先序）→ 无诊断
        /// <para>（值级语义断言见 NCMapper.Tests 集成矩阵 Q3_SingleMethod_*：转换器收到源实体而非字段值）</para></summary>
        [Fact]
        public async Task R6_Q3_SingleMethod_ParamEqualsEntityAndFieldType_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Node
                {
                    public string Label { get; set; } = "";
                    public Node Child { get; set; } = null!;
                }
                public static class NodeConv
                {
                    public static string Convert(Node source) => "T2:" + source.Label;
                }

                [MapFrom(typeof(Node))]
                public class Dto
                {
                    [Convert(typeof(NodeConv), nameof(NodeConv.Convert))]
                    public string Child { get; set; } = "";
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }

        /// <summary>矩阵/反向 T0：[ConvertBack] 无参常量模板 M() 命中 → 无诊断</summary>
        [Fact]
        public async Task ConvertBackUsing_T0_NoArgConstant_Resolves_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class Ent { public string Tag { get; set; } = ""; }
                public static class BackConstConv
                {
                    public static string Convert() => "CONST";
                }

                [MapFrom(typeof(Ent))]
                public class Dto
                {
                    [ConvertBack(typeof(BackConstConv), nameof(BackConstConv.Convert))]
                    public string Tag { get; set; } = "";
                }
                """;

            await AssertNoResolverDiagnostic(source);
        }
    }
}
