using System.Collections.Generic;
using System.Linq;
using NCMapper;
using NCMapper.EFCore;
using Xunit;

namespace NCMapper.Tests;

// =====================================================================
// Batch-2 批首探针（探索计划；D3/Q3 待验项实测载体）
// 实测三问：
//   P1 泛型约束——GetOrAddElementCopier 对值类型元素是否被拒（A3-08 注释声称
//      "MakeGenericMethod 对值类型抛"，本探针把声称变实测；失配即红 = 揭示注释失实）；
//   P2 分流形态编译+运行——拟落地的 CopyHashSet 直调形态原型（DeepCopyList :116-132
// A3-08 同款：循环外解析 elemCopier + 同型直调 + 派生回退）在 HashSet 通道
//      编译可行且与现状语义等价；
//   P3 现状语义锁定——叶 struct Comparer 透传短路 / 非叶 struct 对象通道（A3-08 在
//  HashSet 通道的现状）/ 同型引用独立副本，作为落码后的等价锚。
// 探针结论（结论落）：分流形态唯一——值元素按 raw IsValueType
// 分流（非叶 struct → CopyElement → CopyStructBoxed，泛型直调不触 struct），引用同型
// 元素直调 elemCopier，派生实例回退运行时分发。
// A1-05 断言动全局 Pipeline（Mapper.Pipeline 单例）——按 ReverseMapMultiTargetCacheTests
// 惯例入 PipelineGlobalState collection 防跨类并行污染。
[Collection("PipelineGlobalState")]
public class RuntimeHotPathProbeTests
{
    // ===== 探针模型 =====

    public class ProbeEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class ProbeDerived : ProbeEntity
    {
        public string Extra { get; set; } = "d";
    }

    public struct ProbeLeafStruct
    {
        public int X;
        public long Y;
    }

    /// <summary>非叶 struct：含可变引用实例字段（A3-08 改判对象通道的靶形态）。</summary>
    public struct ProbeNonLeafStruct
    {
        public int Id;
        public List<int> Items;
    }

    private sealed class Mod4Comparer : IEqualityComparer<short>
    {
        public bool Equals(short x, short y) => x == y;
        public int GetHashCode(short obj) => obj % 4;
    }

    // ===== P1：泛型约束实测（分流必要性证据） =====

    [Fact]
    public void Probe1_ValueTypeElementCopier_RejectedByGenericVirtualChannel()
    {
        // GetOrAddElementCopier 内部 Lazy 带 .Value 立即求值（459-460）——编译委托在
        // GetOrAdd 调用内即触发，异常抛在解析行而非委托调用行，两处一并纳入捕获
        var invokeEx = Record.Exception(() =>
        {
            var func = DeepCopyProviderBase.GetOrAddElementCopier(typeof(ProbeLeafStruct));
            _ = func!(DeepCopyProviderBase.Current, new ProbeLeafStruct { X = 1, Y = 2 }, new DeepCopyContext());
        });

        Assert.True(invokeEx is not null,
            "A3-08 注释声称泛型虚通道 DeepCopy<T>(class 约束) 对值类型抛（MakeGenericMethod）；" +
            $"实测未抛——注释与实现失配，分流前提须人工复核。实测行为：值类型委托解析+调用无异常。");
    }

    // ===== P2：拟落地形态原型（编译 + 运行 + 语义等价） =====

    /// <summary>
    /// 拟落地形态原型——CopyHashSet 引用分支直调形态（DeepCopyList :116-132 同款结构）。
    /// 方法头 IsValueType&lt;T&gt;() 短路属现状保留不进原型；回退分支以 CollectionCopier.DeepCopy
    /// （CopyElement 的内核调用，null 保护在探针内显式展开）承载。
    /// </summary>
    private static HashSet<T>? ProbeCopyHashSetDirectRefBranch<T>(HashSet<T>? sourceItems, DeepCopyContext ctx)
        where T : class
    {
        if (sourceItems == null) return null;
        var elemType = typeof(T);
        var elemCopier = DeepCopyProviderBase.GetOrAddElementCopier(elemType);
        var provider = DeepCopyProviderBase.Current;
        var result = new HashSet<T>(sourceItems.Count, sourceItems.Comparer);
        foreach (var item in sourceItems)
        {
            if (item == null) { result.Add(default!); continue; }
            result.Add(item.GetType() != elemType
                ? CollectionCopier.DeepCopy(item, ctx)!      // 派生/null 回退：运行时类型分发（=CopyElement 内核）
                : (T)elemCopier!(provider, item, ctx)!);     // 同型直调：跳过 DeepCopyObjectCore 分发链
        }
        return result;
    }

    [Fact]
    public void Probe2_DirectCallPrototype_SameTypeReference_DeepCopySemantics()
    {
        var src = new HashSet<ProbeEntity>
        {
            new() { Id = 1, Name = "a" },
            new() { Id = 2, Name = "b" },
        };
        var copy = ProbeCopyHashSetDirectRefBranch(src, new DeepCopyContext());

        Assert.NotNull(copy);
        Assert.Equal(2, copy!.Count);
        // 副本元素与源元素引用独立
        Assert.NotSame(src.First(x => x.Id == 2), copy.First(x => x.Id == 2));
        // 深度等价：改副本元素属性 → 源不变
        copy.First(x => x.Id == 1).Name = "mutated";
        Assert.Equal("a", src.First(x => x.Id == 1).Name);
        // Comparer 透传（拟落地形态保留现状 :435 行为）
        Assert.Same(src.Comparer, copy.Comparer);
    }

    [Fact]
    public void Probe2b_DirectCallPrototype_DerivedInstance_FallsBackToRuntimeDispatch()
    {
        var src = new HashSet<ProbeEntity> { new ProbeDerived { Id = 1, Extra = "e" } };
        var copy = ProbeCopyHashSetDirectRefBranch(src, new DeepCopyContext());

        var derived = Assert.IsType<ProbeDerived>(Assert.Single(copy!));
        Assert.Equal("e", derived.Extra);   // 派生字段随运行时类型分发保真（多态语义保持）
    }

    // ===== P3：现状语义锁定（落码后的等价锚）=====

    [Fact]
    public void Probe3_LeafStruct_ShortCircuit_PreservesComparer()
    {
        var src = new HashSet<short>(new Mod4Comparer()) { 1, 2, 3 };
        var copy = Mapper.DeepCopy(src)!;

        Assert.Same(src.Comparer, copy.Comparer);   // 透传（现状 CopyHashSet :434）
        Assert.Equal(src.OrderBy(x => x), copy.OrderBy(x => x));
    }

    [Fact]
    public void Probe3b_NonLeafStruct_ObjectChannel_MemberwiseDeepCopy()
    {
        var src = new HashSet<ProbeNonLeafStruct> { new() { Id = 1, Items = new List<int> { 7 } } };
        var copy = Mapper.DeepCopy(src)!;

        var item = Assert.Single(copy);
        item.Items.Add(99);                          // 改副本的引用字段内容
        Assert.DoesNotContain(99, Assert.Single(src).Items);   // 对象通道逐成员深拷贝（A3-08，HashSet 通道现状锁定）
    }

    [Fact]
    public void Probe3c_SameTypeReference_EndToEnd_IndependentCopies()
    {
        var src = new HashSet<ProbeEntity>
        {
            new() { Id = 1, Name = "keep" },
            new() { Id = 2, Name = "keep2" },
        };
        var copy = Mapper.DeepCopy(src)!;

        Assert.Equal(2, copy.Count);
        copy.First(x => x.Id == 2).Name = "mutated";
        Assert.Equal("keep2", src.First(x => x.Id == 2).Name);   // 现状逐元素 DeepCopy 独立副本
    }

    // ===== 验收④行为锁定断言（锁定现状绿，非红测先证——断言形态声明）=====

    /// <summary>
    /// A1-05 验收④：负缓存建立后 AddPostMap 同类型对 → 负缓存失效（X7）→ 回调命中执行
    /// （防 H-03 反向形态：注册后回调静默失效）。
    /// </summary>
    [Fact]
    public void T001_A1_05_NegativeCacheInvalidated_CallbackAfterMissFires()
    {
        try
        {
            // 1) 锚对点亮管道（不同于靶对）
            Mapper.Pipeline.AddPostMap<ProbeEntity, ProbeDerived>((s, d) => { });
            // 2) 靶对（无回调）跑两次 Map → 负缓存建立
            var src = new ProbeEntity { Id = 1, Name = "n" };
            _ = Mapper.Map<ProbeEntity, ProbeEntity>(src, (s, d) => { });
            _ = Mapper.Map<ProbeEntity, ProbeEntity>(src, (s, d) => { });
            // 3) 注册靶对回调（X7 失效点：AddPostMap 锁内 TryRemove 负缓存）
            var fired = false;
            Mapper.Pipeline.AddPostMap<ProbeEntity, ProbeEntity>((s, d) => fired = true);
            // 4) 再 Map → 回调必须执行（负缓存未失效即 H-03 反向形态回归）
            _ = Mapper.Map<ProbeEntity, ProbeEntity>(src, (s, d) => { });
            Assert.True(fired, "负缓存失效后回调未触发——X7 失效协议破坏（H-03 反向形态回归）");
        }
        finally
        {
            Mapper.Pipeline.Clear();   // 恢复全局管道态（测试隔离）
        }
    }

    /// <summary>A1-05 验收④补强：Clear 后同对再注册再触发（Clear 清负缓存路径）。</summary>
    [Fact]
    public void T001_A1_05_ClearInvalidatesNegativeCache_ReRegisteredCallbackFires()
    {
        try
        {
            Mapper.Pipeline.AddPostMap<ProbeEntity, ProbeDerived>((s, d) => { });
            var src = new ProbeEntity { Id = 2, Name = "m" };
            _ = Mapper.Map<ProbeEntity, ProbeEntity>(src, (s, d) => { });   // 靶对负缓存建立

            Mapper.Pipeline.Clear();                                    // X7：Clear 清负缓存
            Mapper.Pipeline.AddPostMap<ProbeEntity, ProbeEntity>((s, d) => d.Name = "fired");
            var dto = Mapper.Map<ProbeEntity, ProbeEntity>(src, (s, d) => { });
            Assert.Equal("fired", dto!.Name);                          // Clear 后注册必须生效
        }
        finally
        {
            Mapper.Pipeline.Clear();
        }
    }

    /// <summary>C-13 断言本地模型（仅 [MapTree]，无 [MapFrom]——生成基线零影响）。</summary>
    public sealed class ProbeTreeEntity
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Name { get; set; } = "";
        public List<ProbeTreeEntity> Children { get; set; } = new();
    }

    public sealed class ProbeTreeDto
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Name { get; set; } = "";
        public List<ProbeTreeDto> Children { get; set; } = new();
    }

    /// <summary>
    /// C-13 验收④：树清空后叶子节点 Children 保持空列表非 null（T-3 置空契约——
    /// ClearTreeChildrenSlot 仅清非空预填，空列表/null 保持原状；TreeBuilder 挂载后
    /// 叶子的挂载位仍为空列表而非 null）。
    /// </summary>
    [Fact]
    public void T001_C13_ClearSlot_KeepsLeafChildrenAsEmptyList()
    {
        // 通道武装（同 RuntimeHotPathBench C-13 夹具）：无 [MapFrom] 目标的 project 通道抛
        // "cannot use Projection"——EFCoreProjectionAsNoTrackingDefault=false + PostMap 强制
        // 降级（DegradationDetectorBase.Agnostic :98）。全局 static 测试窗口内 finally 恢复。
        NCMapperEFCoreExtensions.EFCoreProjectionAsNoTrackingDefault = false;
        Mapper.Pipeline.AddPostMap<ProbeTreeEntity, ProbeTreeDto>((s, t) => { });
        try
        {
            var rows = new List<ProbeTreeEntity>();
            for (var t = 0; t < 10; t++)
            {
                rows.Add(new ProbeTreeEntity { Id = t + 1, Name = $"p{t}" });
                rows.Add(new ProbeTreeEntity { Id = 100 + t, ParentId = t + 1, Name = $"c{t}" });
            }

            var tree = rows.AsQueryable().AsDto().ToTreeList<ProbeTreeDto>()!;
            Assert.Equal(10, tree.Count);
            foreach (var root in tree)
            {
                var leaf = Assert.Single(root.Children);      // 每根恰一子
                Assert.NotNull(leaf.Children);                // 叶子挂载位非 null（T-3：置 new List<T> 非 null）
                Assert.Empty(leaf.Children);                  // 且为空列表（无孙代）
            }
        }
        finally
        {
            NCMapperEFCoreExtensions.EFCoreProjectionAsNoTrackingDefault = true;
            Mapper.Pipeline.Clear();
        }
    }
}
