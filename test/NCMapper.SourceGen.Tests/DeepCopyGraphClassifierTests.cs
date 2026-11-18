using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 源生成侧深拷贝图三值分类测试
    /// <para>覆盖，三类分类（Acyclic/Cyclic/Unknown）的可观察输出 +
    /// NCMAP016 触发 + 三项盲区修复回归（字典 K-V、索引器、object 跳过假阴性）+
    /// F1 virtual 边判定（含 Roslyn 隐式接口实现 IsVirtual/IsSealed 行为守卫）。</para>
    /// <para>可观察口径（T5 落地后更新）：record Cyclic/Unknown → NCMAP012 + DeepCopyRuntimeOnly 回退；
    /// Unknown → NCMAP016（Info，逐不可解析边）；class Acyclic 变体省略 RegisterDeepCopyTarget 登记，
    /// class Cyclic/Unknown 保留登记；五类变体生成快照断言见 DeepCopyVariantGenerationTests（T5）。</para>
    /// </summary>
    public class DeepCopyGraphClassifierTests
    {
        // ================================================================
        // Unknown 分类 → NCMAP016
        // ================================================================

        /// <summary>
        /// object 属性 → 不可解析边 → NCMAP016（Info），消息含类型/属性/属性类型
        /// （修复原 UnwrapToNamedType 对 object 的"跳过"假阴性）
        /// </summary>
        [Fact]
        public void NCMAP016_ObjectProperty_InfoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class SgObjPayloadHolder { public int Id { get; set; } public object? Payload { get; set; } }

                [MapFrom(typeof(SgObjPayloadHolder))]
                public class SgObjPayloadDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            var diag = result.Diagnostics.SingleOrDefault(d => d.Id == "NCMAP016");
            Assert.NotNull(diag);
            Assert.Equal(DiagnosticSeverity.Info, diag!.Severity);
            var message = diag.GetMessage();
            Assert.Contains("SgObjPayloadHolder", message);
            Assert.Contains("Payload", message);
            // 无环语义不受影响：不应连带触发 NCMAP012（非 record 分支）
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP012");
        }

        /// <summary>
        /// 接口类型属性 → 不可解析边 → NCMAP016
        /// </summary>
        [Fact]
        public void NCMAP016_InterfaceProperty_InfoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public interface ISgHook { string Name { get; } }
                public class SgHookHolder { public int Id { get; set; } public ISgHook? Hook { get; set; } }

                [MapFrom(typeof(SgHookHolder))]
                public class SgHookDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            var diag = result.Diagnostics.SingleOrDefault(d => d.Id == "NCMAP016");
            Assert.NotNull(diag);
            Assert.Contains("Hook", diag!.GetMessage());
        }

        /// <summary>
        /// F1：可被重写的 virtual 引用属性（非 sealed 类）→ Unknown 边 → NCMAP016
        /// </summary>
        [Fact]
        public void NCMAP016_VirtualReferenceProperty_InfoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class SgVirtRefNode { public int Value { get; set; } }
                public class SgVirtRefHolder { public int Id { get; set; } public virtual SgVirtRefNode? Child { get; set; } }

                [MapFrom(typeof(SgVirtRefHolder))]
                public class SgVirtRefDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            var diag = result.Diagnostics.SingleOrDefault(d => d.Id == "NCMAP016");
            Assert.NotNull(diag);
            Assert.Contains("Child", diag!.GetMessage());
            // SgVirtRefNode（纯值属性）与 DTO 均为 Acyclic，不应有额外 NCMAP016
            Assert.Single(result.Diagnostics, d => d.Id == "NCMAP016");
        }

        /// <summary>
        /// 非泛型 IEnumerable 属性（无 IEnumerable&lt;T&gt）→ 元素类型不可解析 → NCMAP016
        /// </summary>
        [Fact]
        public void NCMAP016_NonGenericEnumerableProperty_InfoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;
                using System.Collections;

                public class SgLegacyBag : IEnumerable
                {
                    public IEnumerator GetEnumerator() => null!;
                }
                public class SgLegacyBagHolder { public int Id { get; set; } public SgLegacyBag? Items { get; set; } }

                [MapFrom(typeof(SgLegacyBagHolder))]
                public class SgLegacyBagDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            var diag = result.Diagnostics.SingleOrDefault(d => d.Id == "NCMAP016");
            Assert.NotNull(diag);
            Assert.Contains("Items", diag!.GetMessage());
        }

        // ================================================================
        // F1 例外：不误判场景（不触发 NCMAP016）
        // ================================================================

        /// <summary>
        /// F1 例外：sealed override（CLR virtual+final）不触发 Unknown —
        /// 派生类 sealed 类的 override 引用属性行为固定，按声明边跟随，整图 Acyclic
        /// </summary>
        [Fact]
        public void F1_SealedOverride_NotUnknown_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;

                public class SgSealedOvLeaf { public int Value { get; set; } }
                public class SgSealedOvBase { public virtual SgSealedOvLeaf? Data { get; set; } }
                public sealed class SgSealedOvHolder : SgSealedOvBase { public sealed override SgSealedOvLeaf? Data { get; set; } }

                [MapFrom(typeof(SgSealedOvHolder))]
                public class SgSealedOvDto { public SgSealedOvLeaf? Data { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP016");
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP012");
        }

        /// <summary>
        /// F1 守卫（要求）：隐式接口实现在 Roslyn 下为 virtual+final
        /// （IsSealed=true），不得误判为 Unknown — record 经接口实现成员成环
        /// 应判 Cyclic（NCMAP012 回退），而非 Unknown（无 NCMAP016）
        /// </summary>
        [Fact]
        public void F1_ImplicitInterfaceImpl_Ring_Cyclic_NotUnknown()
        {
            var source = """
                using NCMapper.Attributes;

                public interface ISgImplRingNext<T> where T : class { T? Next { get; set; } }
                public record SgImplRingRec(int Id) : ISgImplRingNext<SgImplRingRec>
                {
                    public SgImplRingRec? Next { get; set; }
                }

                [MapFrom(typeof(SgImplRingRec))]
                public class SgImplRingDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 环被检测 → record 回退（Cyclic 判定）
            Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP012");
            Assert.Contains("DeepCopyRuntimeOnly", result.GeneratedCode);
            // 隐式接口实现不误判为 Unknown
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP016");
        }

        /// <summary>
        /// Delegate 属性为不透明叶子，不产生边 → 无 NCMAP016
        /// </summary>
        [Fact]
        public void Leaf_Delegate_NoDiagnostic()
        {
            var source = """
                using NCMapper.Attributes;
                using System;

                public class SgDelegateHolder { public int Id { get; set; } public Action? Handler { get; set; } }

                [MapFrom(typeof(SgDelegateHolder))]
                public class SgDelegateDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP016");
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP012");
        }

        // ================================================================
        // 盲区修复回归
        // ================================================================

        /// <summary>
        /// 盲区①回归：record 字典值环（Dictionary&lt;string, Self&gt）—
        /// 旧实现 TryGetCollectionElementType 对字典直接 return false 漏检，
        /// 新实现按 K+V 建边检出环 → NCMAP012 回退运行时
        /// </summary>
        [Fact]
        public void BlindSpot1_DictionaryValueRing_Record_NCMAP012()
        {
            var source = """
                using NCMapper.Attributes;
                using System.Collections.Generic;

                public record SgDictRingRec(int Id, Dictionary<string, SgDictRingRec>? Map);

                [MapFrom(typeof(SgDictRingRec))]
                public class SgDictRingDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP012");
            Assert.True(result.Methods.ContainsKey("DeepCopy_SgDictRingRec"));
            Assert.Contains("DeepCopyRuntimeOnly", result.Methods["DeepCopy_SgDictRingRec"][0].Body);
            // 该图无不可解析边 → 不应报 NCMAP016（Cyclic 优先于 Unknown）
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP016");
        }

        /// <summary>
        /// 盲区②回归：record 仅经索引器自引用（this[int] => this）—
        /// 索引器不计边（Parameters.Length == 0 过滤），判 Acyclic 正常生成构造器路径。
        /// 旧实现将索引器计入边集会误判循环 → NCMAP012 误回退
        /// </summary>
        [Fact]
        public void BlindSpot2_IndexerOnlySelfRef_Acyclic_ConstructorPath()
        {
            var source = """
                using NCMapper.Attributes;

                public record SgIndexerRec(int Id)
                {
                    public SgIndexerRec this[int index] => this;
                }

                [MapFrom(typeof(SgIndexerRec))]
                public class SgIndexerDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP012");
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP016");
            // 正常生成构造器路径
            Assert.Contains("new SgIndexerRec(", result.GeneratedCode);
            Assert.DoesNotContain("DeepCopyRuntimeOnly", result.GeneratedCode);
        }

        // ================================================================
        // Acyclic / Cyclic 基线（T3 阶段生成行为不变）
        // ================================================================

        /// <summary>
        /// Acyclic 基线：扁平 + 嵌套链类型，无诊断。
        /// T5 落地后口径：Acyclic 无参变体省略
        /// RegisterDeepCopyTarget 登记（编译期已证明无环，层 1 IsAcyclic 分支跳过 CycleCache），
        /// 注册行携带 acyclic: true。Cyclic/Unknown 变体的登记保留断言见
        /// Cyclic_SelfRefClass_NoNcmap016_RegistrationRetained 与 DeepCopyVariantGenerationTests。
        /// </summary>
        [Fact]
        public void Acyclic_FlatAndChain_NoDiagnostics_RegistrationOmitted()
        {
            var source = """
                using NCMapper.Attributes;

                public class SgFlatChild { public string Name { get; set; } = ""; }
                public class SgFlatRoot { public int Id { get; set; } public string Name { get; set; } = ""; public SgFlatChild? Child { get; set; } }

                [MapFrom(typeof(SgFlatRoot))]
                public class SgFlatDto { public int Id { get; set; } public string Name { get; set; } = ""; }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP016");
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP012");
            // T5：Acyclic 变体省略 CycleCache 登记行
            Assert.DoesNotContain("RegisterDeepCopyTarget", result.GeneratedCode);
            Assert.DoesNotContain("DeepCopyRuntimeOnly", result.GeneratedCode);
            // T5：注册行携带 acyclic: true
            Assert.Contains("acyclic: true", result.GeneratedCode);
        }

        /// <summary>
        /// Cyclic 基线（class 分支）：自引用类 — 不报 NCMAP016（Cyclic ≠ Unknown），
        /// class 路径保留现状登记（NCMAP012 仅作用于 record 分支）
        /// </summary>
        [Fact]
        public void Cyclic_SelfRefClass_NoNcmap016_RegistrationRetained()
        {
            var source = """
                using NCMapper.Attributes;

                public class SgSelfRefCls { public int Id { get; set; } public SgSelfRefCls? Next { get; set; } }

                [MapFrom(typeof(SgSelfRefCls))]
                public class SgSelfRefClsDto { public int Id { get; set; } }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP016");
            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP012");
            Assert.Contains("RegisterDeepCopyTarget", result.GeneratedCode);
        }
    }
}
