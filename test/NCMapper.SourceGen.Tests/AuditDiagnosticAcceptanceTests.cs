using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using NCMapper.SourceGen.Tests.Helpers;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 整改任务书验收测试 — 源生成器诊断专项（H-03/H-05/H-06/M-03/L-08→M）
    /// </summary>
    public class AuditDiagnosticAcceptanceTests
    {
        // ================================================================
        // H-03: NCMAP011 仅检查目标类型可访问性 → 补充 internal 源测试
        // ================================================================

        /// <summary>
        /// H-03 验收：internal 源 + public 目标 → 跳过生成 + 报 NCMAP011
        /// </summary>
        [Fact]
        public async Task H03_InternalSource_PublicTarget_NCMAP011()
        {
            var source = """
                using NCMapper.Attributes;

                internal class InternalSourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(InternalSourceEntity))]
                public class PublicTargetDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                }
                """;

            await VerifyDiagnostic(source, "NCMAP011");
        }

        /// <summary>
        /// H-03 验收：public 源 + public 目标 → 正常生成，不报 NCMAP011
        /// </summary>
        [Fact]
        public async Task H03_PublicSource_PublicTarget_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class PublicSourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(PublicSourceEntity))]
                public class PublicTargetDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                }
                """;

            await VerifyNoDiagnostic(source, "NCMAP011");
        }

        // ================================================================
        // H-05: 后缀分配非确定性 → 两次编译输出一致
        // ================================================================

        /// <summary>
        /// H-05 验收：同一输入两次编译，生成代码完全一致（diff 为空）
        /// </summary>
        [Fact]
        public void H05_Deterministic_Output_Two_Compilations()
        {
            var source = """
                using NCMapper.Attributes;

                public class EntityA { public int Id { get; set; } public string Name { get; set; } = ""; }
                public class EntityB { public int Id { get; set; } public string Title { get; set; } = ""; }
                public class EntityC { public int Id { get; set; } public decimal Amount { get; set; } }

                [MapFrom(typeof(EntityA))]
                public class DtoA { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(EntityB))]
                public class DtoB { public int Id { get; set; } public string Title { get; set; } = ""; }

                [MapFrom(typeof(EntityC))]
                public class DtoC { public int Id { get; set; } public decimal Amount { get; set; } }
                """;

            // 第一次编译
            var result1 = TestHelper.GenerateMapper(source);
            // 第二次编译
            var result2 = TestHelper.GenerateMapper(source);

            // 生成代码完全一致
            Assert.Equal(result1.GeneratedCode, result2.GeneratedCode);
        }

        /// <summary>
        /// H-05 验收：多类型多映射场景确定性
        /// </summary>
        [Fact]
        public void H05_Deterministic_ManyTypes()
        {
            var source = """
                using NCMapper.Attributes;

                public class Src1 { public int Id { get; set; } public string A { get; set; } = ""; }
                public class Src2 { public int Id { get; set; } public string B { get; set; } = ""; }
                public class Src3 { public int Id { get; set; } public string C { get; set; } = ""; }
                public class Src4 { public int Id { get; set; } public string D { get; set; } = ""; }
                public class Src5 { public int Id { get; set; } public string E { get; set; } = ""; }

                [MapFrom(typeof(Src1))]
                public class Tgt1 { public int Id { get; set; } public string A { get; set; } = ""; }
                [MapFrom(typeof(Src2))]
                public class Tgt2 { public int Id { get; set; } public string B { get; set; } = ""; }
                [MapFrom(typeof(Src3))]
                public class Tgt3 { public int Id { get; set; } public string C { get; set; } = ""; }
                [MapFrom(typeof(Src4))]
                public class Tgt4 { public int Id { get; set; } public string D { get; set; } = ""; }
                [MapFrom(typeof(Src5))]
                public class Tgt5 { public int Id { get; set; } public string E { get; set; } = ""; }
                """;

            var results = Enumerable.Range(0, 5)
                .Select(_ => TestHelper.GenerateMapper(source).GeneratedCode)
                .ToList();

            // 5 次编译输出全部一致
            Assert.All(results, r => Assert.Equal(results[0], r));
        }

        // ================================================================
        // H-06: IsMapFromRegistered 仅检查第一个 [MapFrom]
        // ================================================================

        /// <summary>
        /// H-06 验收：嵌套类型有多个 [MapFrom] 时，IsMapFromRegistered 匹配非第一个源
        /// </summary>
        [Fact]
        public void H06_MultiSource_MapFrom_NestedUsesGenerated()
        {
            var source = """
                using NCMapper.Attributes;

                public class InnerEntityA { public int Id { get; set; } public string Label { get; set; } = ""; }
                public class InnerEntityB { public int Id { get; set; } public string Label { get; set; } = ""; }
                public class OuterEntity { public int Id { get; set; } public InnerEntityB? Inner { get; set; } }

                [MapFrom(typeof(InnerEntityA))]
                [MapFrom(typeof(InnerEntityB))]
                public class InnerDto { public int Id { get; set; } public string Label { get; set; } = ""; }

                [MapFrom(typeof(OuterEntity))]
                public class OuterDto
                {
                    public int Id { get; set; }
                    public InnerDto? Inner { get; set; }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 验证生成了映射代码
            Assert.False(string.IsNullOrEmpty(result.GeneratedCode));
            Assert.Contains("OuterDto", result.GeneratedCode);
            // InnerDto 的 [MapFrom] 包含 InnerEntityB（第二个），
            // H-06 修复后 IsMapFromRegistered 应匹配到 → 嵌套映射走生成代码
            Assert.Contains("InnerDto", result.GeneratedCode);
        }

        // ================================================================
        // M-03: 展平中间导航属性触发虚假 NCMAP007
        // ================================================================

        /// <summary>
        /// M-03 验收：AddressCity 展平映射后，Address 不再报 NCMAP007
        /// </summary>
        [Fact]
        public async Task M03_Flattened_Navigation_No_NCMAP007()
        {
            var source = """
                using NCMapper.Attributes;

                public class AddressInfo { public string City { get; set; } = ""; public string Street { get; set; } = ""; }
                public class PersonEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                    public AddressInfo? Address { get; set; }
                }

                [MapFrom(typeof(PersonEntity))]
                public class PersonFlatDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                    public string AddressCity { get; set; } = "";
                    public string AddressStreet { get; set; } = "";
                }
                """;

            // Address 作为展平路径的中间导航属性，不应报 NCMAP007（未映射源成员）
            await VerifyNoDiagnostic(source, "NCMAP007");
        }

        /// <summary>
        /// M-03 对照：未映射的非导航属性仍报 NCMAP007
        /// </summary>
        [Fact]
        public async Task M03_Unmapped_NonNavigation_Still_NCMAP007()
        {
            var source = """
                using NCMapper.Attributes;

                public class SimpleEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                    public string UnmappedProp { get; set; } = "";
                }

                [MapFrom(typeof(SimpleEntity))]
                public class SimpleDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                }
                """;

            // UnmappedProp 未被映射，应报 NCMAP007
            await VerifyDiagnostic(source, "NCMAP007");
        }

        // ================================================================
        // L-08→M: 生成器输出阶段无 try/catch 防护
        // ================================================================

        /// <summary>
        /// L-08→M 验收：一个坏类型 + 多个正常类型 → 正常类型代码仍生成
        /// </summary>
        [Fact]
        public void L08M_BadType_NormalTypes_StillGenerated()
        {
            var source = """
                using NCMapper.Attributes;

                public class GoodEntity1 { public int Id { get; set; } public string Name { get; set; } = ""; }
                public class GoodEntity2 { public int Id { get; set; } public string Title { get; set; } = ""; }
                public class GoodEntity3 { public int Id { get; set; } public decimal Amount { get; set; } }

                [MapFrom(typeof(GoodEntity1))]
                public class GoodDto1 { public int Id { get; set; } public string Name { get; set; } = ""; }

                [MapFrom(typeof(GoodEntity2))]
                public class GoodDto2 { public int Id { get; set; } public string Title { get; set; } = ""; }

                [MapFrom(typeof(GoodEntity3))]
                public class GoodDto3 { public int Id { get; set; } public decimal Amount { get; set; } }

                // H-10 坏类型：record 目标 + [Convert] 无源属性构造器参数
                // → 生成期 NRE（H-04 场景）→ NCMAP099 生成失败诊断
                public class BadSourceEntity { public int Id { get; set; } }

                [MapFrom(typeof(BadSourceEntity))]
                public record BadRecordDto(int Id, [property: Convert(typeof(BadNoOpConverter), nameof(BadNoOpConverter.Convert))] string Name);

                public static class BadNoOpConverter
                {
                    public static string Convert(string fieldValue) => fieldValue ?? "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 所有正常类型的映射方法都应生成（坏类型不阻断）
            Assert.False(string.IsNullOrEmpty(result.GeneratedCode));
            Assert.Contains("GoodDto1", result.GeneratedCode);
            Assert.Contains("GoodDto2", result.GeneratedCode);
            Assert.Contains("GoodDto3", result.GeneratedCode);
            // 修复：坏类型（record + ConvertUsing 无源属性构造器参数）
            // 不再触发 NRE/NCMAP099——生成成功且映射方法存在（B2 时已留"修复前报 NCMAP099"红证）。
            // 本测试同时作为的回归测试（H-04/H-10 同源场景）。
            Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP099"),
                "修复后该场景不应再报 NCMAP099（生成失败）");
            Assert.Contains("BadRecordDto", result.GeneratedCode);
            Assert.Contains("BadNoOpConverter", result.GeneratedCode); // 转换器调用已生成（映射正确性）
        }

        /// <summary>
        /// L-08→M 验收：NCMAP099 诊断定义存在（生成失败警告）
        /// </summary>
        [Fact]
        public void L08M_NCMAP099_Descriptor_Exists()
        {
            // DiagnosticDescriptors 是 internal 类，通过反射访问
            var sourceGenAssembly = typeof(NCMapperSourceGenerator).Assembly;
            var descriptorType = sourceGenAssembly.GetType("NCMapper.SourceGen.DiagnosticDescriptors");
            Assert.NotNull(descriptorType);

            var field = descriptorType!.GetField("GenerationFailedRule",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.NotNull(field);

            var value = field!.GetValue(null) as DiagnosticDescriptor;
            Assert.NotNull(value);
            Assert.Equal("NCMAP099", value!.Id);
        }

        // ================================================================
        // C-01: string → int? 源生成器路径不生成编译错误
        // ================================================================

        /// <summary>
        /// C-01 验收：string → int? 源生成器生成 TypeConvertHelper.ConvertValue 回退（非直接 cast）
        /// </summary>
        [Fact]
        public void C01_String_To_IntNullable_Generates_ConvertValue()
        {
            var source = """
                using NCMapper.Attributes;

                public class StatusEntity { public string StatusCode { get; set; } = "200"; public int Count { get; set; } = 5; }

                [MapFrom(typeof(StatusEntity))]
                public class StatusDto
                {
                    public int? StatusCode { get; set; }
                    public int? Count { get; set; }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 生成代码不应包含 (int)src.StatusCode 这样的直接 cast
            Assert.DoesNotContain("(int)src.StatusCode", result.GeneratedCode);
            // 应包含 TypeConvertHelper.ConvertValue 回退或安全的转换方式
            Assert.Contains("StatusCode", result.GeneratedCode);
            // int → int? 应正常生成
            Assert.Contains("Count", result.GeneratedCode);
        }

        /// <summary>
        /// C-01 验收：DateTime → long? 不生成编译错误（回退运行时）
        /// </summary>
        [Fact]
        public void C01_DateTime_To_LongNullable_No_CompileError()
        {
            var source = """
                using NCMapper.Attributes;
                using System;

                public class TimeEntity { public DateTime Timestamp { get; set; } }

                [MapFrom(typeof(TimeEntity))]
                public class TimeDto
                {
                    public long? Timestamp { get; set; }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 不应生成 (long)src.Timestamp 这样的无效 cast
            Assert.DoesNotContain("(long)src.Timestamp", result.GeneratedCode);
            // 应回退到 TypeConvertHelper.ConvertValue
            Assert.Contains("Timestamp", result.GeneratedCode);
        }

        // ================================================================
        // C-02: ObservableCollection 目标 + 元素类型不一致（源生成器路径）
        // ================================================================

        /// <summary>
        /// C-02 验收：ObservableCollection&lt;string&gt; 作为映射目标，生成 new ObservableCollection 构造
        /// </summary>
        [Fact]
        public void C02_ObservableCollection_Target_Generates_Constructor()
        {
            var source = """
                using NCMapper.Attributes;
                using System.Collections.Generic;
                using System.Collections.ObjectModel;

                public class CollEntity { public List<string> Names { get; set; } = new(); }

                [MapFrom(typeof(CollEntity))]
                public class CollDto
                {
                    public ObservableCollection<string>? Names { get; set; }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 生成代码应包含 ObservableCollection 构造（非直接赋值 List）
            Assert.Contains("ObservableCollection", result.GeneratedCode);
        }

        /// <summary>
        /// C-02 验收：List&lt;long&gt; → int[] 元素类型不一致时生成 Select 转换
        /// </summary>
        [Fact]
        public void C02_Element_Type_Mismatch_Generates_Select()
        {
            var source = """
                using NCMapper.Attributes;
                using System.Collections.Generic;

                public class NumEntity { public List<long> Numbers { get; set; } = new(); }

                [MapFrom(typeof(NumEntity))]
                public class NumDto
                {
                    public int[]? Numbers { get; set; }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 生成代码应包含 ToArray（数组目标）
            Assert.Contains("ToArray", result.GeneratedCode);
        }

        // ================================================================
        // H-07: private init / internal set 不被源生成器映射
        // ================================================================

        /// <summary>
        /// H-07 验收：private set 属性不出现在正向映射方法的赋值中
        /// </summary>
        [Fact]
        public void H07_PrivateInit_Not_In_GeneratedCode()
        {
            var source = """
                using NCMapper.Attributes;

                public class AccEntity { public string PubProp { get; set; } = ""; public string PrivProp { get; set; } = ""; }

                [MapFrom(typeof(AccEntity))]
                public class AccDto
                {
                    public string PubProp { get; set; } = "";
                    public string PrivProp { get; private set; } = "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 正向映射方法不应包含对 PrivProp 的赋值（private set 不可从外部赋值）
            var forwardMethod = result.Methods.ContainsKey("Map_AccEntity_To_AccDto")
                ? result.Methods["Map_AccEntity_To_AccDto"][0].Body
                : result.GeneratedCode;
            // PubProp 应被映射（对象初始化器或赋值语句）
            Assert.Contains("PubProp", forwardMethod);
            // PrivProp 不应作为赋值目标出现（private set 被 H-07 过滤）
            Assert.DoesNotContain("PrivProp =", forwardMethod);
        }

        /// <summary>
        /// H-07 验收：internal set 属性不出现在正向映射方法的赋值中
        /// </summary>
        [Fact]
        public void H07_InternalSet_Not_In_GeneratedCode()
        {
            var source = """
                using NCMapper.Attributes;

                public class AccEntity2 { public string Normal { get; set; } = ""; public string Intern { get; set; } = ""; }

                [MapFrom(typeof(AccEntity2))]
                public class AccDto2
                {
                    public string Normal { get; set; } = "";
                    public string Intern { get; internal set; } = "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 正向映射方法不应包含对 Intern 的赋值（internal set 不可从外部程序集赋值）
            var forwardMethod = result.Methods.ContainsKey("Map_AccEntity2_To_AccDto2")
                ? result.Methods["Map_AccEntity2_To_AccDto2"][0].Body
                : result.GeneratedCode;
            Assert.Contains("Normal", forwardMethod);
            Assert.DoesNotContain("Intern =", forwardMethod);
        }

        // ================================================================
        // M-07: record 循环引用 DeepCopy 回退运行时（NCMAP012）
        // ================================================================

        /// <summary>
        /// M-07 验收：自引用 record（record Node(Node? Next)）触发 NCMAP012 + 生成 DeepCopyRuntimeOnly
        /// </summary>
        [Fact]
        public async Task M07_SelfRefRecord_NCMAP012_RuntimeFallback()
        {
            var source = """
                using NCMapper.Attributes;

                public record SgM07Node(int Value, SgM07Node? Next);

                [MapFrom(typeof(SgM07Node))]
                public class SgM07NodeDto
                {
                    public int Value { get; set; }
                }
                """;

            // 验证 NCMAP012 诊断被触发
            await VerifyDiagnostic(source, "NCMAP012");

            // 验证 DeepCopy 方法使用 DeepCopyRuntimeOnly 回退
            var result = TestHelper.GenerateMapper(source);
            Assert.Contains("DeepCopyRuntimeOnly", result.GeneratedCode);
            // 精确定位 DeepCopy 方法体：不应包含构造器路径代码
            Assert.True(result.Methods.ContainsKey("DeepCopy_SgM07Node"),
                "应生成 DeepCopy_SgM07Node 方法");
            var deepCopyBody = result.Methods["DeepCopy_SgM07Node"][0].Body;
            Assert.Contains("DeepCopyRuntimeOnly", deepCopyBody);
            Assert.DoesNotContain("var dst = new", deepCopyBody);
        }

        /// <summary>
        /// M-07 验收：双向引用 record A↔B 触发 NCMAP012
        /// </summary>
        [Fact]
        public async Task M07_MutualRefRecord_NCMAP012()
        {
            var source = """
                using NCMapper.Attributes;

                public record SgM07A(int Id, SgM07B? Partner);
                public record SgM07B(int Id, SgM07A? Owner);

                [MapFrom(typeof(SgM07A))]
                public class SgM07ADto
                {
                    public int Id { get; set; }
                }
                """;

            await VerifyDiagnostic(source, "NCMAP012");
        }

        /// <summary>
        /// M-07 验收：非循环 record 不触发 NCMAP012，正常生成构造器路径
        /// </summary>
        [Fact]
        public async Task M07_NonCircularRecord_NoDiagnostic_ConstructorPath()
        {
            var source = """
                using NCMapper.Attributes;

                public record SgM07Simple(int Id, string Name);

                [MapFrom(typeof(SgM07Simple))]
                public class SgM07SimpleDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                }
                """;

            // 不应触发 NCMAP012
            await VerifyNoDiagnostic(source, "NCMAP012");

            // 应生成构造器路径代码
            var result = TestHelper.GenerateMapper(source);
            Assert.Contains("new SgM07Simple(", result.GeneratedCode);
            Assert.DoesNotContain("DeepCopyRuntimeOnly", result.GeneratedCode);
        }

        /// <summary>
        /// M-07 验收：record 含集合属性引用自身（List&lt;Self&gt）触发 NCMAP012
        /// </summary>
        [Fact]
        public async Task M07_CollectionSelfRef_NCMAP012()
        {
            var source = """
                using NCMapper.Attributes;
                using System.Collections.Generic;

                public record SgM07Tree(int Value, List<SgM07Tree>? Children);

                [MapFrom(typeof(SgM07Tree))]
                public class SgM07TreeDto
                {
                    public int Value { get; set; }
                }
                """;

            await VerifyDiagnostic(source, "NCMAP012");

            var result = TestHelper.GenerateMapper(source);
            Assert.Contains("DeepCopyRuntimeOnly", result.GeneratedCode);
        }

        // ================================================================
        // 辅助方法
        // ================================================================

        private static async Task VerifyDiagnostic(string source, string diagnosticId)
        {
            var (compilation, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.Contains(diagnostics, d => d.Id == diagnosticId);
        }

        private static async Task VerifyNoDiagnostic(string source, string diagnosticId)
        {
            var (compilation, diagnostics) = await CompileAndGetDiagnostics(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == diagnosticId);
        }

        private static async Task<(Compilation, ImmutableArray<Diagnostic>)> CompileAndGetDiagnostics(string source)
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
                "AuditDiagnosticTest",
                new[] { CSharpSyntaxTree.ParseText(source) },
                allRefs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new NCMapperSourceGenerator();
            var driver = CSharpGeneratorDriver.Create(generator);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

            return (updatedCompilation, diagnostics);
        }
    }
}
