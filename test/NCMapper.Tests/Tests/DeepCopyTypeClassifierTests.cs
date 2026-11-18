using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// T1 — 运行时深拷贝三值分类器 <see cref="DeepCopyTypeClassifier"/> 单元测试（测试矩阵）
    /// <para>分类器为纯静态只读缓存（无可变全局状态），无需 [Collection] 串行化。</para>
    /// </summary>
    public class DeepCopyTypeClassifierTests
    {
        // ==================== Acyclic 场景 ====================

        /// <summary>纯平铺 DTO — 无任何引用边</summary>
        public class FlatDto
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        /// <summary>无环引用链 A → B → FlatDto</summary>
        public class ChainA { public ChainB? B { get; set; } }
        public class ChainB { public FlatDto? Leaf { get; set; } }

        /// <summary>纯 DAG（含共享引用）— Left/Right 共享 DagChild 类型，无环</summary>
        public class DagParent { public DagChild? Left { get; set; } public DagChild? Right { get; set; } }
        public class DagChild { public string? Value { get; set; } }

        /// <summary>数组解包 — 元素为无环类型</summary>
        public class ArrayHolder { public FlatDto[]? Items { get; set; } }

        /// <summary>List&lt;T&gt; 解包 — 元素为无环类型</summary>
        public class ListHolder { public List<FlatDto>? Items { get; set; } }

        /// <summary>Nullable&lt;T&gt; — 值类型叶子</summary>
        public class NullableHolder { public int? Count { get; set; } }

        /// <summary>Delegate 属性 — 不透明叶子，不影响判定（边解析规则）</summary>
        public class DelegateHolder { public Action? Handler { get; set; } public string? Name { get; set; } }

        /// <summary>无环字典 — Dictionary&lt;string, FlatDto&gt;</summary>
        public class DictFlat { public Dictionary<string, FlatDto>? Values { get; set; } }

        /// <summary>预算测试链 C0 → C1 → C2 → C3 → C4（无环）</summary>
        public class BudgetC0 { public BudgetC1? Next { get; set; } }
        public class BudgetC1 { public BudgetC2? Next { get; set; } }
        public class BudgetC2 { public BudgetC3? Next { get; set; } }
        public class BudgetC3 { public BudgetC4? Next { get; set; } }
        public class BudgetC4 { public int Value { get; set; } }

        [Fact]
        public void FlatDto_IsAcyclic()
            => Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(FlatDto)));

        [Fact]
        public void ReferenceChain_IsAcyclic()
            => Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(ChainA)));

        [Fact]
        public void PureDag_WithSharedReference_IsAcyclic()
            => Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(DagParent)));

        [Fact]
        public void Array_And_List_Unwrap_ToAcyclicElement()
        {
            Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(ArrayHolder)));
            Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(ListHolder)));
            Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(NullableHolder)));
        }

        [Fact]
        public void Delegate_Property_IsLeaf_Acyclic()
            => Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(DelegateHolder)));

        [Fact]
        public void Acyclic_Dictionary_IsAcyclic()
            => Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(DictFlat)));

        // ==================== Cyclic 场景 ====================

        /// <summary>自引用环</summary>
        public class SelfRefNode { public SelfRefNode? Self { get; set; } }

        /// <summary>A↔B 双向环</summary>
        public class CycleA { public CycleB? B { get; set; } }
        public class CycleB { public CycleA? A { get; set; } }

        /// <summary>三节点环 T1 → T2 → T3 → T1</summary>
        public class ThreeRing1 { public ThreeRing2? Next { get; set; } }
        public class ThreeRing2 { public ThreeRing3? Next { get; set; } }
        public class ThreeRing3 { public ThreeRing1? Next { get; set; } }

        /// <summary>8 节点边界环（E 环形缓冲边界；分类层面任意环长均为 Cyclic）</summary>
        public class EightRing1 { public EightRing2? Next { get; set; } }
        public class EightRing2 { public EightRing3? Next { get; set; } }
        public class EightRing3 { public EightRing4? Next { get; set; } }
        public class EightRing4 { public EightRing5? Next { get; set; } }
        public class EightRing5 { public EightRing6? Next { get; set; } }
        public class EightRing6 { public EightRing7? Next { get; set; } }
        public class EightRing7 { public EightRing8? Next { get; set; } }
        public class EightRing8 { public EightRing1? Next { get; set; } }

        /// <summary>字典值环（盲区①回归：Dictionary&lt;string, T&gt; 的 V 边须建边）</summary>
        public class DictValueRing { public Dictionary<string, DictValueRing>? Values { get; set; } }

        /// <summary>IReadOnlyDictionary 值环（盲区①回归：只读字典同样建 K+V 边）</summary>
        public class ReadonlyDictRing { public IReadOnlyDictionary<string, ReadonlyDictRing>? Values { get; set; } }

        /// <summary>字典键环 — K 边回到持有类型</summary>
        public class DictKeyRing { public Dictionary<DictKeyNode, string>? Map { get; set; } }
        public class DictKeyNode { public DictKeyRing? Owner { get; set; } }

        [Fact]
        public void SelfReference_IsCyclic()
            => Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(SelfRefNode)));

        [Fact]
        public void Bidirectional_Cycle_IsCyclic()
        {
            Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(CycleA)));
            Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(CycleB)));
        }

        [Fact]
        public void ThreeNodeRing_IsCyclic()
            => Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(ThreeRing1)));

        [Fact]
        public void EightNodeRing_IsCyclic()
            => Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(EightRing1)));

        [Fact]
        public void Dictionary_ValueRing_IsCyclic_BlindSpot1_Fixed()
        {
            Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(DictValueRing)));
            Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(ReadonlyDictRing)));
        }

        [Fact]
        public void Dictionary_KeyRing_IsCyclic()
            => Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(DictKeyRing)));

        [Fact]
        public void ContainerRoot_ElementRing_IsCyclic()
        {
            // 容器自身作为分类根（DeepCopy<T[]> / DeepCopy<List<T>> 场景）— 按元素建边
            Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(SelfRefNode[])));
            Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(List<SelfRefNode>)));
        }

        // ==================== Unknown 场景 ====================

        /// <summary>object 属性 — 不可解析边</summary>
        public class ObjectHolder { public object? Payload { get; set; } }

        /// <summary>接口属性 — 静态不可解析运行时实现</summary>
        public class InterfaceHolder { public IDisposable? Resource { get; set; } }

        /// <summary>开放泛型属性载体（泛型参数不可解析）</summary>
        public class OpenGenericHolder<T> { public T? Item { get; set; } }

        /// <summary>可被重写的 virtual 自引用属性（F1 核心场景）— 归 Unknown 而非 Cyclic</summary>
        public class VirtSelfRef { public virtual VirtSelfRef? Next { get; set; } }

        /// <summary>virtual 引用属性（无环声明边）— F1 同样归 Unknown</summary>
        public class VirtFlatHolder { public virtual FlatDto? Leaf { get; set; } }

        [Fact]
        public void Object_Property_IsUnknown()
            => Assert.Equal(DeepCopyGraphShape.Unknown, DeepCopyTypeClassifier.Classify(typeof(ObjectHolder)));

        [Fact]
        public void Interface_Property_IsUnknown()
            => Assert.Equal(DeepCopyGraphShape.Unknown, DeepCopyTypeClassifier.Classify(typeof(InterfaceHolder)));

        [Fact]
        public void OpenGeneric_Property_IsUnknown_ClosedGeneric_IsAcyclic()
        {
            Assert.Equal(DeepCopyGraphShape.Unknown, DeepCopyTypeClassifier.Classify(typeof(OpenGenericHolder<>)));
            Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(OpenGenericHolder<FlatDto>)));
        }

        [Fact]
        public void Overridable_Virtual_ReferenceProperty_IsUnknown_F1()
        {
            Assert.Equal(DeepCopyGraphShape.Unknown, DeepCopyTypeClassifier.Classify(typeof(VirtSelfRef)));
            Assert.Equal(DeepCopyGraphShape.Unknown, DeepCopyTypeClassifier.Classify(typeof(VirtFlatHolder)));
        }

        // ==================== F1 例外：sealed / final 成员不误判 ====================
        // 注：sealed 类声明新 virtual 成员为 C# 语言非法（CS0549），
        // "!DeclaringType.IsSealed" 子句由 sealed override（CLR final）与隐式接口实现用例覆盖。

        /// <summary>sealed override — getter 为 CLR final，不触发 F1</summary>
        public class VirtOverrideBase { public virtual FlatDto? Leaf { get; set; } }
        public sealed class SealedOverrideHolder : VirtOverrideBase
        {
            public sealed override FlatDto? Leaf { get; set; }
        }

        /// <summary>隐式接口实现 — CLR 语义 virtual+final，不触发 F1</summary>
        public interface IRingHolder<T> where T : class { T? Next { get; set; } }
        public class ImplicitImplRing : IRingHolder<ImplicitImplRing>
        {
            public ImplicitImplRing? Next { get; set; }
        }

        /// <summary>索引器独占"环"— 索引器不计边（盲区②回归：对齐 DeepCopyCompiler.cs:372）</summary>
        public class IndexerSelfRef
        {
            public int Value { get; set; }
            public IndexerSelfRef this[int i] => this;
        }

        [Fact]
        public void SealedOverride_NotUnknown_Acyclic()
        {
            // 基类自身因 virtual 可重写归 Unknown；sealed override 派生类不触发 F1 → Acyclic
            Assert.Equal(DeepCopyGraphShape.Unknown, DeepCopyTypeClassifier.Classify(typeof(VirtOverrideBase)));
            Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(SealedOverrideHolder)));
        }

        [Fact]
        public void ImplicitInterfaceImpl_IsVirtualFinal_NotUnknown_Cyclic()
            => Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(ImplicitImplRing)));

        [Fact]
        public void Indexer_Only_Ring_IsAcyclic_BlindSpot2_Fixed()
            => Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(IndexerSelfRef)));

        // ==================== 节点预算 ====================

        [Fact]
        public void NodeBudget_Exceeded_IsUnknown_NoThrow()
        {
            // 预算 3 < 链长 5 → 超限归 Unknown（不抛异常、不拖死）
            Assert.Equal(DeepCopyGraphShape.Unknown,
                DeepCopyTypeClassifier.ClassifyUncached(typeof(BudgetC0), nodeBudget: 3));
            // 充足预算 → 正常 Acyclic（证明预算守卫本身不产生误判）
            Assert.Equal(DeepCopyGraphShape.Acyclic,
                DeepCopyTypeClassifier.ClassifyUncached(typeof(BudgetC0), DeepCopyTypeClassifier.DefaultNodeBudget));
        }

        // ==================== X4/Q2 不透明叶子白名单 ====================

        /// <summary>白名单属性持有类 — 叶子化后应为 Acyclic</summary>
        public class LeafWhitelistHolder
        {
            public Type? ClrType { get; set; }
            public MemberInfo? Meta { get; set; }
            public Stream? Data { get; set; }
            public DBNull? NoValue { get; set; }
        }

        [Fact]
        public void LeafWhitelist_Holder_IsAcyclic()
            => Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(LeafWhitelistHolder)));

            // ==================== 守卫：自引用容器（容器边交主栈三色标记） ====================
            // 006 第 1 项 / 守卫矩阵：修复前 CollectEdgeTargets/CollectContainerEdges
        // 字典 K/V 与集合元素边内联递归展开，绕过 ClassifyCore 主栈三色保护 ——
        // 自引用容器类型（元素/值 == 容器自身）无限递归致 StackOverflowException（0xC00000FD）。
        // 修复后容器边经 AddEdgeTarget 直接入 targets 交主栈，回边命中 onPath 判 Cyclic。
        // 注：经 ClassifyUncached 断言（不污染 s_cache 全局缓存）。

        /// <summary>自引用列表容器 — class B : List&lt;B&gt;（元素类型 == 容器自身）</summary>
        public class SelfRefListContainer : List<SelfRefListContainer>;

        /// <summary>自引用字典容器 — class JsonNode : Dictionary&lt;string, JsonNode&gt;（V 类型 == 容器自身，006 契约要点 2 形态）</summary>
        public class SelfRefDictContainer : Dictionary<string, SelfRefDictContainer>;

        /// <summary>双层自引用混合 — 字典 V 边与集合元素边分别经两层容器回到根类型</summary>
        public class MixedSelfRefHolder { public Dictionary<string, MixedSelfRefList>? Nodes { get; set; } }
        public class MixedSelfRefList : List<MixedSelfRefList>;

        [Fact]
        public void SelfRefListContainer_ElementSelfReference_IsCyclic_NoThrow()
            => Assert.Equal(DeepCopyGraphShape.Cyclic,
                DeepCopyTypeClassifier.ClassifyUncached(typeof(SelfRefListContainer), DeepCopyTypeClassifier.DefaultNodeBudget));

        [Fact]
        public void SelfRefDictContainer_ValueSelfReference_IsCyclic_NoThrow()
            => Assert.Equal(DeepCopyGraphShape.Cyclic,
                DeepCopyTypeClassifier.ClassifyUncached(typeof(SelfRefDictContainer), DeepCopyTypeClassifier.DefaultNodeBudget));

        [Fact]
        public void SelfRefContainer_MixedDoubleLayer_IsCyclic_NoThrow()
        {
            // 双层混合①：字典容器为根，V 边 → 列表容器 → 元素边 → 列表容器自身（回边）
            Assert.Equal(DeepCopyGraphShape.Cyclic,
                DeepCopyTypeClassifier.ClassifyUncached(typeof(Dictionary<string, MixedSelfRefList>), DeepCopyTypeClassifier.DefaultNodeBudget));
            // 双层混合②：普通持有类为根，字典 V 边 → 列表容器 → 元素边 → 列表容器自身（回边）
            Assert.Equal(DeepCopyGraphShape.Cyclic,
                DeepCopyTypeClassifier.ClassifyUncached(typeof(MixedSelfRefHolder), DeepCopyTypeClassifier.DefaultNodeBudget));
        }

        /// <summary>
        /// X4/Q2 可靠性守卫 — 白名单类型必须没有"公开可写非索引器引用属性"，
        /// 否则填充器（DeepCopyCompiler.CompileFiller 仅遍历 CanRead+CanWrite 属性）会递归进入，叶子化不再可靠。
        /// BCL 后续版本若新增此类属性，本测试失败提示重新评估白名单。
        /// </summary>
        [Theory]
        [InlineData(typeof(Type))]
        [InlineData(typeof(MemberInfo))]
        [InlineData(typeof(Stream))]
        [InlineData(typeof(DBNull))]
        public void LeafWhitelist_Types_HaveNoWritablePublicReferenceProperties(Type leafType)
        {
            var violating = leafType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0
                    && p.CanWrite
                    && !p.PropertyType.IsValueType
                    && p.PropertyType != typeof(string))
                .ToList();

            Assert.True(violating.Count == 0,
                $"{leafType.Name} 出现公开可写引用属性：{string.Join(", ", violating.ConvertAll(p => p.Name))}，叶子白名单可靠性依据失效");
        }
    }
}
