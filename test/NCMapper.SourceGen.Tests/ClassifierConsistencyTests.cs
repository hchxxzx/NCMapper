using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 两套深拷贝图分类器一致性语料
    /// <para>单一语料源（<see cref="CorpusSource"/>）同时喂给两套独立实现的分类器：</para>
    /// <para>- 源生成侧：TestHelper.GenerateMapper 跑真实生成器管道，从可观察输出提取三值分类
    /// （注册行 acyclic 字面量 + NCMAP016 报告集合）。NCMAP016 仅在最终分类为 Unknown 时报告
    /// （CodeGenerator.DeepCopy.cs 的 shape == Unknown 门），Cyclic 提前返回，提取无歧义。</para>
    /// <para>- 运行时侧：同一语料 Emit 为内存程序集加载，对每个类型调用
    /// <c>NCMapper.DeepCopyTypeClassifier.Classify(Type)</c>（IVT 可见）。</para>
    /// <para>语料覆盖（≥20 类型 × 两侧比对）：扁平/嵌套链/可空引用/数组/泛型集合/
    /// 字典 K-V 双侧（盲区①）/IEnumerable&lt;T&gt;/自引用环/双向互引用环/object/接口/
    /// virtual 非 sealed（F1）/非泛型可枚举/List&lt;object&gt;/sealed 隐式接口实现守卫/
    /// record 无环/record 引用边/record 环（回退）/record object（D4 回退）。</para>
    /// </summary>
    public class ClassifierConsistencyTests
    {
        /// <summary>
        /// 两分类器在共享语料上分类结果 100% 一致，且语料规模 ≥ 20 类型
        /// </summary>
        [Fact]
        public void BothClassifiers_Agree_OnSharedCorpus()
        {
            // 源生成侧：真实生成器管道 → 可观察输出提取三值分类
            var genResult = TestHelper.GenerateMapper(CorpusSource);
            var sgShapes = ExtractSourceGenShapes(genResult);

            Assert.True(sgShapes.Count >= 20,
                $"一致性语料类型数应 ≥ 20，实际 {sgShapes.Count}");

            // 运行时侧：同一语料 Emit 为内存程序集 → 逐类型 Classify 比对
            var compilation = TestHelper.BuildCompilation(CorpusSource);
            using var ms = new MemoryStream();
            // 显式传入测试取消令牌（xUnit1051：接受 CancellationToken 的方法应使用 TestContext.Current.CancellationToken）
            var emit = compilation.Emit(ms, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(emit.Success,
                "语料编译失败: " + string.Join("; ",
                    emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            var asm = Assembly.Load(ms.ToArray());

            foreach (var (typeName, sgShape) in sgShapes.OrderBy(kvp => kvp.Key))
            {
                var type = asm.GetType(typeName);
                Assert.True(type != null, $"语料类型 {typeName} 未在 Emit 程序集中找到");
                var rtShape = DeepCopyTypeClassifier.Classify(type!).ToString();
                Assert.True(rtShape == sgShape,
                    $"分类不一致 — {typeName}: 源生成={sgShape}, 运行时={rtShape}");
            }
        }

        /// <summary>
        /// 关键类别的期望分类锚定 — 防止"两侧一致但两侧同错"的漂移
        /// </summary>
        [Fact]
        public void CorpusCategories_HaveExpectedShapes()
        {
            var genResult = TestHelper.GenerateMapper(CorpusSource);
            var shapes = ExtractSourceGenShapes(genResult);

            // Acyclic：扁平/链/容器/字典/sealed 隐式实现/record 无环
            Assert.Equal("Acyclic", shapes["CcCorpus.CcFlat"]);
            Assert.Equal("Acyclic", shapes["CcCorpus.CcChain"]);
            Assert.Equal("Acyclic", shapes["CcCorpus.CcDict"]);
            Assert.Equal("Acyclic", shapes["CcCorpus.CcDictKey"]);
            Assert.Equal("Acyclic", shapes["CcCorpus.CcSealedImpl"]);
            Assert.Equal("Acyclic", shapes["CcCorpus.RcFlat"]);
            Assert.Equal("Acyclic", shapes["CcCorpus.RcRef"]);

            // Cyclic：自引用/双向互引用/record 环
            Assert.Equal("Cyclic", shapes["CcCorpus.CcSelfRef"]);
            Assert.Equal("Cyclic", shapes["CcCorpus.CcMutualA"]);
            Assert.Equal("Cyclic", shapes["CcCorpus.CcMutualB"]);
            Assert.Equal("Cyclic", shapes["CcCorpus.RcRingNode"]);

            // Unknown：object/接口/virtual 非 sealed/非泛型可枚举/集合 object 元素/record object
            Assert.Equal("Unknown", shapes["CcCorpus.CcObjectBox"]);
            Assert.Equal("Unknown", shapes["CcCorpus.CcIfaceHook"]);
            Assert.Equal("Unknown", shapes["CcCorpus.CcVirtualNode"]);
            Assert.Equal("Unknown", shapes["CcCorpus.CcRawEnumerable"]);
            Assert.Equal("Unknown", shapes["CcCorpus.CcListOfObject"]);
            Assert.Equal("Unknown", shapes["CcCorpus.RcBoxed"]);
        }

        /// <summary>
        /// 从生成器可观察输出提取每类型的三值分类
        /// <para>提取规则：注册行 acyclic: true → Acyclic；acyclic: false 且该类型有
        /// NCMAP016 报告 → Unknown（Info 逐不可解析边，仅最终 Unknown 时报告）；
        /// acyclic: false 且无 NCMAP016 → Cyclic。</para>
        /// </summary>
        private static Dictionary<string, string> ExtractSourceGenShapes(MapperGenerationResult genResult)
        {
            // NCMAP016 报告的类型集合。诊断消息为本地化文本（zh-CN 与 en 措辞不同），
            // OwnerType 需跨语言提取（见 ExtractOwnerType 双正则）；不依赖任何单一语言措辞。
            var unknownReported = new HashSet<string>(
                genResult.Diagnostics
                    .Where(d => d.Id == "NCMAP016")
                    .Select(d => ExtractOwnerType(d.GetMessage()))
                    .Where(v => v.Length > 0));

            var shapes = new Dictionary<string, string>();
            foreach (Match m in RegistrationRegex.Matches(genResult.GeneratedCode))
            {
                var typeName = m.Groups[1].Value;
                var acyclic = m.Groups[2].Value == "true";
                shapes[typeName] = acyclic
                    ? "Acyclic"
                    : unknownReported.Contains(typeName) ? "Unknown" : "Cyclic";
            }
            return shapes;
        }

        /// <summary>
        /// 注册行匹配 — RegisterGeneratedDeepCopy&lt;类型&gt;(GeneratedMapper.方法, acyclic: 字面量)
        /// </summary>
        private static readonly Regex RegistrationRegex = new(
            @"RegisterGeneratedDeepCopy<([^>]+)>\(GeneratedMapper\.\w+, acyclic: (true|false)\)",
            RegexOptions.Compiled);

        /// <summary>
        /// 从 NCMAP016 消息提取 OwnerType 全名 — 不依赖本地化：诊断消息为本地化资源文本，
        /// zh-CN 与 en（CI 的 en-US/invariant 环境）措辞不同，单一中文正则会在非中文环境失配，
        /// 导致 Unknown 类型被误判为 Cyclic（确定性 CI 失败）。故同时匹配两种已知 messageFormat：
        /// <para>zh-CN: 类型 '{0}' 的属性 '{1}'（类型 {2}）... → OwnerType 在「类型 'X'」</para>
        /// <para>en   : Property '{1}' (type {2}) of type '{0}' ... → OwnerType 在「of type 'X'」</para>
        /// <para>同一消息只会命中一种语言格式；若两正则均失配（未知新措辞）返回空串，由调用方过滤。</para>
        /// </summary>
        private static string ExtractOwnerType(string message)
        {
            var zh = UnknownOwnerZhRegex.Match(message);
            if (zh.Success) return zh.Groups[1].Value;
            var en = UnknownOwnerEnRegex.Match(message);
            return en.Success ? en.Groups[1].Value : "";
        }

        /// <summary>NCMAP016 消息 OwnerType 提取 — zh-CN 格式</summary>
        private static readonly Regex UnknownOwnerZhRegex = new(
            "类型 '([^']+)' 的属性",
            RegexOptions.Compiled);

        /// <summary>NCMAP016 消息 OwnerType 提取 — 默认（en）格式（参数 {0} 位于 "of type '...'"）</summary>
        private static readonly Regex UnknownOwnerEnRegex = new(
            @"\bof type '([^']+)'",
            RegexOptions.Compiled);

        /// <summary>
        /// 共享语料源 — 唯一事实来源，两套分类器均消费此文本
        /// <para>DTO 统一为扁平形态（仅 int Id），分类类别由实体承载；
        /// 每个实体/record 均需配对 [MapFrom] DTO 才会进入深拷贝注册。</para>
        /// </summary>
        private const string CorpusSource = """
            using NCMapper.Attributes;
            using System.Collections.Generic;

            namespace CcCorpus
            {
                // ---- Acyclic：扁平 / 链 / 可空 / 数组 / 集合 / 字典 / IEnumerable<T> ----

                public class CcFlat { public int Id { get; set; } public string Name { get; set; } = ""; }
                [MapFrom(typeof(CcFlat))]
                public class CcFlatDto { public int Id { get; set; } }

                public class CcChain { public int Id { get; set; } public CcFlat? Child { get; set; } }
                [MapFrom(typeof(CcChain))]
                public class CcChainDto { public int Id { get; set; } }

                public class CcNullableRefs { public int Id { get; set; } public CcFlat? Maybe { get; set; } public int? Count { get; set; } }
                [MapFrom(typeof(CcNullableRefs))]
                public class CcNullableRefsDto { public int Id { get; set; } }

                public class CcArrays { public int Id { get; set; } public int[] Nums { get; set; } = []; public string[] Tags { get; set; } = []; public CcFlat[] Items { get; set; } = []; }
                [MapFrom(typeof(CcArrays))]
                public class CcArraysDto { public int Id { get; set; } }

                public class CcList { public int Id { get; set; } public List<CcFlat> Items { get; set; } = new(); }
                [MapFrom(typeof(CcList))]
                public class CcListDto { public int Id { get; set; } }

                public class CcDict { public int Id { get; set; } public Dictionary<string, CcFlat> Map { get; set; } = new(); }
                [MapFrom(typeof(CcDict))]
                public class CcDictDto { public int Id { get; set; } }

                public class CcDictKey { public int Id { get; set; } public Dictionary<CcFlat, int> Map { get; set; } = new(); }
                [MapFrom(typeof(CcDictKey))]
                public class CcDictKeyDto { public int Id { get; set; } }

                public class CcEnumerable { public int Id { get; set; } public IEnumerable<CcFlat> Seq { get; set; } = new List<CcFlat>(); }
                [MapFrom(typeof(CcEnumerable))]
                public class CcEnumerableDto { public int Id { get; set; } }

                // ---- Cyclic：自引用 / 双向互引用 ----

                public class CcSelfRef { public int Id { get; set; } public CcSelfRef? Self { get; set; } }
                [MapFrom(typeof(CcSelfRef))]
                public class CcSelfRefDto { public int Id { get; set; } }

                public class CcMutualA { public int Id { get; set; } public CcMutualB? B { get; set; } }
                public class CcMutualB { public int Id { get; set; } public CcMutualA? A { get; set; } }
                [MapFrom(typeof(CcMutualA))]
                public class CcMutualADto { public int Id { get; set; } }
                [MapFrom(typeof(CcMutualB))]
                public class CcMutualBDto { public int Id { get; set; } }

                // ---- Unknown：object / 接口 / virtual 非 sealed / 非泛型可枚举 / List<object> ----

                public class CcObjectBox { public int Id { get; set; } public object? Payload { get; set; } }
                [MapFrom(typeof(CcObjectBox))]
                public class CcObjectBoxDto { public int Id { get; set; } }

                public interface ICcHook { string Name { get; } }
                public class CcIfaceHook { public int Id { get; set; } public ICcHook? Hook { get; set; } }
                [MapFrom(typeof(CcIfaceHook))]
                public class CcIfaceHookDto { public int Id { get; set; } }

                public class CcVirtualNode { public int Id { get; set; } public virtual CcFlat? Child { get; set; } }
                [MapFrom(typeof(CcVirtualNode))]
                public class CcVirtualNodeDto { public int Id { get; set; } }

                public class CcRawEnumerable { public int Id { get; set; } public System.Collections.ArrayList Items { get; set; } = new(); }
                [MapFrom(typeof(CcRawEnumerable))]
                public class CcRawEnumerableDto { public int Id { get; set; } }

                public class CcListOfObject { public int Id { get; set; } public List<object> Boxes { get; set; } = new(); }
                [MapFrom(typeof(CcListOfObject))]
                public class CcListOfObjectDto { public int Id { get; set; } }

                // ---- F1 守卫：sealed 类隐式接口实现（CLR virtual+final）不误判 Unknown ----

                public sealed class CcSealedImpl : ICcHook { public int Id { get; set; } public string Name { get; set; } = ""; }
                [MapFrom(typeof(CcSealedImpl))]
                public class CcSealedImplDto { public int Id { get; set; } }

                // ---- record：无环 / 引用边 / 环（回退）/ object（D4 回退） ----
                // NCMAP012 为预期回退诊断，语料层抑制；生成器级断言由 DeepCopyVariantGenerationTests 承担

                public record RcFlat(int Id, string Name);
                [MapFrom(typeof(RcFlat))]
                public record RcFlatDto(int Id, string Name);

                public record RcRef(int Id, CcFlat Child);
                [MapFrom(typeof(RcRef))]
                public class RcRefDto { public int Id { get; set; } }

                public record RcRingNode(int Id, RcRingNode? Next);
            #pragma warning disable NCMAP012
                [MapFrom(typeof(RcRingNode))]
                public record RcRingNodeDto(int Id, RcRingNodeDto? Next);
            #pragma warning restore NCMAP012

                public record RcBoxed(int Id, object? Payload);
            #pragma warning disable NCMAP012
                [MapFrom(typeof(RcBoxed))]
                public record RcBoxedDto(int Id, object? Payload);
            #pragma warning restore NCMAP012
            }
            """;
    }
}
