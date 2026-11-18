// 源生成器发射形态断言测试（②）
// 用 GeneratorDriver 驱动 NCMapperSourceGenerator，对生成代码字符串做形态断言：
//  T3 V3：集合四分支生成代码含 null 条件构造（替代 diff 抽查，P2 缓解升级）
//  T2 V3：冲突枚举退化诊断（NCMAP023 大小写冲突 / NCMAP024 数值重复）
//  T2 否定条件（B10）：string→enum 生成代码全限定 + 无短名引用
//  T4：未注册元素对内联（消除 MapList_Int32_To_Int64 引用）
//  T5：List→Dict 发射 NotSupportedException
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 源生成器发射形态断言测试 — 编译期驱动生成器，断言生成代码形态（不依赖运行时行为）
    /// </summary>
    public class SourceGenEmissionShapeTests
    {
        /// <summary>
        /// 运行源生成器并返回生成代码文本与诊断
        /// <para>编译引用采用 AppDomain 全量程序集（与 NCMapper.SourceGen.Tests 一致）——
        /// 只拼系统程序集子集会导致生成器 transform 类型解析差异（T4 内联/T5 NotSupported
        /// 分支在子集引用下不触发，实测定位：List/Dictionary 相关分发走 fallback）</para>
        /// </summary>
        /// <param name="source">被测源代码（含 [MapFrom] 标注）</param>
        /// <param name="generatedCode">输出：拼接后的生成代码</param>
        /// <param name="diagnostics">输出：生成器报告的诊断</param>
        private static void RunGenerator(string source, out string generatedCode, out ImmutableArray<Diagnostic> diagnostics)
        {
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
                .ToList();
            var ncmapper = typeof(NCMapper.Mapper).Assembly.Location;
            if (!references.Any(r => r.Display == ncmapper))
                references.Add(MetadataReference.CreateFromFile(ncmapper));

            var compilation = CSharpCompilation.Create(
                "EmissionShapeTest",
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

            var driver = CSharpGeneratorDriver.Create(new NCMapperSourceGenerator());
            driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
                compilation, out var outputCompilation, out var diags);

            generatedCode = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.GetText().ToString()));
            diagnostics = diags;

            // 调试辅助（验证期）：生成代码落盘供断言失败时人工比对，正常后保留（无害）
            try
            {
                var debugDir = @"D:\OpenCode\NCMapper\.workbuddy\audit-tmp";
                System.IO.Directory.CreateDirectory(debugDir);
                var dictType = outputCompilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2");
                var listType = outputCompilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(debugDir, "ncmapper_emission_debug.cs"),
                    $"GEN_DLL={typeof(NCMapperSourceGenerator).Assembly.Location}|{System.IO.File.GetLastWriteTime(typeof(NCMapperSourceGenerator).Assembly.Location):HH:mm:ss}\n" +
                    $"DictionarySym={dictType?.ToDisplayString() ?? "NULL"}|Kind={dictType?.TypeKind}\n" +
                    $"ListSym={listType?.ToDisplayString() ?? "NULL"}|Kind={listType?.TypeKind}\n" +
                    $"RefCount={references.Count}\n{generatedCode}");
            }
            catch
            {
                // 调试输出失败不影响测试
            }
        }

        /// <summary>
        /// 加载测试项目真实构建产物（obj/Generated）的生成代码
        /// <para> 验证期修正：T3/T4/T5 形态断言改读真实构建产物——
        /// GeneratorDriver 独立编译环境与测试项目构建存在引用/上下文差异（T4 内联/T5 NotSupported
        /// 分支在 Driver 环境不触发，实测定位），真实构建产物（EmitCompilerGeneratedFiles=true
        /// 输出 obj/Generated）为生成器在真实编译下的权威输出，断言更可靠。</para>
        /// </summary>
        /// <returns>生成代码全文</returns>
        private static string LoadGeneratedCode()
        {
            var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../.."));
            var genFile = System.IO.Path.Combine(
                projectRoot,
                "obj/Generated/NCMapper.SourceGen/NCMapper.SourceGen.NCMapperSourceGenerator/NCMapper_Generated_GeneratedMapper.g.cs");
            return System.IO.File.ReadAllText(genFile);
        }

        /// <summary>
        /// T3 V3：集合四分支（List/接口/数组/ISet/具体集合）生成代码均含 null 条件构造
        /// <para>List/接口直接赋值（null 即 null）；数组/ISet/具体集合为 <c>== null ? null : &lt;构造&gt;</c>。
        /// 载体：测试项目 ContainerNullSource/ContainerNullGenDto（真实构建产物）</para>
        /// </summary>
        [Fact]
        public void T3_CollectionMapping_Emits_NullConditionalConstruction()
        {
            var code = LoadGeneratedCode();

            // 断言真实构建产物（ContainerNullGenDto 六容器属性名：ListProp/EnumerableProp/ArrayProp/SetProp/QueueProp/DictProp）
            // 数组分支：null 条件构造 + ToArray
            Assert.Contains("dst.ArrayProp = _list_ArrayProp == null ? null : _list_ArrayProp.ToArray();", code);
            // ISet 接口分支：null 条件构造 + HashSet 构造器
            Assert.Contains("dst.SetProp = _list_SetProp == null ? null : new System.Collections.Generic.HashSet<NCMapper.Tests.Models.ElemB>(_list_SetProp);", code);
            // 具体集合分支（HashSet 具体类型）：null 条件构造 + 构造器
            Assert.Contains("new System.Collections.Generic.HashSet<NCMapper.Tests.Models.ElemB>(_list_SetProp)", code);
            // 具体集合分支（Queue）：null 条件构造 + 构造器
            Assert.Contains("dst.QueueProp = _list_QueueProp == null ? null : new System.Collections.Generic.Queue<NCMapper.Tests.Models.ElemB>(_list_QueueProp);", code);
            // List 分支：直接赋值（null 即赋 null）
            Assert.Contains("dst.ListProp = _list_ListProp;", code);
            // 否定条件：不得出现旧守卫形态（对 null tmpVar 解引用的无条件构造）
            Assert.DoesNotContain("if (_list_ArrayProp != null) dst.ArrayProp = _list_ArrayProp.ToArray();", code);
            Assert.DoesNotContain("if (_list_QueueProp != null) dst.QueueProp = new", code);
        }

        /// <summary>
        /// T4：未注册元素对（int→long）内联转换 — 不发射不存在的 MapList 委托（CS0117 消除）
        /// <para>载体：测试项目 F04Source/F04Dto（真实构建产物），生成代码含全限定
        /// System.Linq.Enumerable.Select + 隐式强转 (long)_e（路径①）</para>
        /// </summary>
        [Fact]
        public void T4_UnregisteredElementPair_Emits_InlineSelect()
        {
            var code = LoadGeneratedCode();

            Assert.Contains("System.Linq.Enumerable.Select(src.Nums, _e => (long)_e)", code);
            // 否定条件：不得引用不存在的委托
            Assert.DoesNotContain("MapList_Int32_To_Int64", code);
            // 否定条件（B10）：Select 必须全限定（不得出现裸短名调用形态 " = Select(" 或 "= Select("）
            Assert.DoesNotContain("= Select(src.Nums", code);
        }

        /// <summary>
        /// T2：string→enum 正常枚举发射形态 — 全小写标签 + 数字串标签 + null 分支 + 抛 MapException（全限定）
        /// </summary>
        [Fact]
        public void T2_StringToEnum_Emits_LowercaseAndNumericLabels()
        {
            const string source = """
                using NCMapper.Attributes;
                namespace T2N;
                public enum Color2 { Red = 1, Blue = 2 }
                public class T2S { public string? C { get; set; } }
                [MapFrom(typeof(T2S))]
                public class T2D { public Color2 C { get; set; } }
                """;

            RunGenerator(source, out var code, out _);

            // 全小写标签 + ToLowerInvariant
            Assert.Contains("src.C?.ToLowerInvariant() switch", code);
            Assert.Contains("\"red\" => T2N.Color2.Red,", code);
            // 数字串标签
            Assert.Contains("\"1\" => T2N.Color2.Red,", code);
            // null 分支
            Assert.Contains("null => default(T2N.Color2),", code);
            // 非法值抛 MapException（全限定；消息改调 ErrorMessages，抛出时随 Culture 切换）
            Assert.Contains("throw new NCMapper.MapException", code);
            Assert.Contains("NCMapper.ErrorMessages.EnumConversionFailedNoMatch", code);
            // 否定条件（B10）：不发射静默 default 分支
            Assert.DoesNotContain("_ => default(T2N.Color2),", code);
            // 否定条件：全限定 typeof
            Assert.Contains("typeof(T2N.Color2)", code);
        }

        /// <summary>
        /// T2 V3：大小写冲突枚举（Red/RED，违反 CLS）→ 退化为精确匹配 + NCMAP023 信息级诊断
        /// </summary>
        [Fact]
        public void T2_CaseCollisionEnum_Emits_DiagnosticNCMAP023()
        {
            const string source = """
                using NCMapper.Attributes;
                namespace T2N;
                public enum CaseConflict { Red = 1, RED = 2 }
                public class T2S { public string? C { get; set; } }
                [MapFrom(typeof(T2S))]
                public class T2D { public CaseConflict C { get; set; } }
                """;

            RunGenerator(source, out var code, out var diagnostics);

            Assert.Contains(diagnostics, d => d.Id == "NCMAP023");
            // 退化：精确名称匹配（不发射 ToLowerInvariant）
            Assert.DoesNotContain("ToLowerInvariant()", code);
            // 非法值维度仍收敛：default 抛 MapException（含 null 分支）
            Assert.Contains("throw new NCMapper.MapException", code);
        }

        /// <summary>
        /// T2 V3：数值重复枚举（别名枚举 A=1/B=1）→ 数字串标签退化 + NCMAP024 信息级诊断
        /// </summary>
        [Fact]
        public void T2_ValueCollisionEnum_Emits_DiagnosticNCMAP024()
        {
            const string source = """
                using NCMapper.Attributes;
                namespace T2N;
                public enum AliasVal { A = 1, B = 1, C = 2 }
                public class T2S { public string? C { get; set; } }
                [MapFrom(typeof(T2S))]
                public class T2D { public AliasVal C { get; set; } }
                """;

            RunGenerator(source, out var code, out var diagnostics);

            Assert.Contains(diagnostics, d => d.Id == "NCMAP024");
            // 数值重复 → 不发射数字串标签（避免重复 switch 标签编译错误）
            Assert.DoesNotContain("\"1\" => T2N.AliasVal.", code);
            // 名称标签仍按全小写发射
            Assert.Contains("\"a\" => T2N.AliasVal.A,", code);
        }

        /// <summary>
        /// T2 邻界反例：无冲突枚举（名称唯一大小写无撞 + 数值唯一）→ 不报 NCMAP023/NCMAP024
        /// </summary>
        [Fact]
        public void T2_CleanEnum_NoCollisionDiagnostics()
        {
            const string source = """
                using NCMapper.Attributes;
                namespace T2N;
                public enum CleanEnum { Red = 1, Blue = 2, Green = 3 }
                public class T2S { public string? C { get; set; } }
                [MapFrom(typeof(T2S))]
                public class T2D { public CleanEnum C { get; set; } }
                """;

            RunGenerator(source, out _, out var diagnostics);

            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP023");
            Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP024");
        }

        /// <summary>
        /// T5：List→Dict 生成代码发射 NotSupportedException（异常类型与运行时统一）
        /// <para>载体：测试项目 ListToDictSource/ListToDictDto（真实构建产物，List&lt;string&gt;→Dictionary&lt;string,string&gt）</para>
        /// </summary>
        [Fact]
        public void T5_ListToDict_Emits_NotSupportedException()
        {
            var code = LoadGeneratedCode();

            Assert.Contains("throw new System.NotSupportedException", code);
            Assert.Contains("NCMapper.ErrorMessages.UnsupportedListToDictionaryConversion", code);
            Assert.Contains("== null ? null : throw", code);
            // 否定条件：不得落入 ConvertValue fallback 抛 MapException
            Assert.DoesNotContain("TypeConvertHelper.ConvertValue<System.Collections.Generic.List", code);
        }
    }
}
