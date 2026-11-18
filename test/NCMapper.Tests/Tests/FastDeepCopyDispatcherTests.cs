using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 方案 F T4：Fast Provider 条件分发器功能测试（矩阵，任务书验收 #5）
/// <para>覆盖：任意环长（20 节点）/ record+循环 / virtual 重写环（F1）/ 混合图 /
/// 跨调用清环（D2）/ 深度守卫 / Provider 切换。</para>
/// <para>修改 <see cref="Mapper.MaxRecursionDepth"/> 与 <see cref="DeepCopyProviderBase.Current"/>
/// 全局状态，归入 DeepCopyGlobalState 集合串行执行。</para>
/// </summary>
[Collection("DeepCopyGlobalState")]
public class FastDeepCopyDispatcherTests
{
    // ================================================================
    // 模型类型
    // ================================================================

    /// <summary>自引用类 — 分类 Cyclic，用于任意环长测试</summary>
    public class DispRingNode
    {
        public int Value { get; set; }
        public DispRingNode? Next { get; set; }
    }

    /// <summary>平坦无环类 — 分类 Acyclic，走快路径</summary>
    public class DispFlat
    {
        public int Value { get; set; }
        public string? Tag { get; set; }
    }

    /// <summary>自引用 record — 分类 Cyclic，无无参构造器（基类 record 占位路径）</summary>
    public record DispSelfRefRecord(int Value, DispSelfRefRecord? Next);

    /// <summary>互引用 record 对 — 分类 Cyclic</summary>
    public record DispRecA(int Id, DispRecB? Partner);

    /// <summary>互引用 record 对 — 分类 Cyclic</summary>
    public record DispRecB(int Id, DispRecA? Owner);

    /// <summary>virtual 引用属性基类 — F1 边规则判 Unknown（可被重写，静态不可解析）</summary>
    public class DispVirtualBase
    {
        public int Id { get; set; }
        public virtual DispVirtualBase? Link { get; set; }
    }

    /// <summary>sealed 派生类 override Link 返回 this 构成环 — 分类经基类 virtual 边归 Unknown</summary>
    public sealed class DispVirtualSelfLoop : DispVirtualBase
    {
        public override DispVirtualBase? Link
        {
            get => this;
            set { /* 构成自环，忽略写入 */ }
        }
    }

    /// <summary>无环分支叶子 — 混合图中的 Acyclic 子树</summary>
    public class DispBranchLeaf
    {
        public string Name { get; set; } = "";
        public int Number { get; set; }
    }

    /// <summary>Acyclic 元素基类 — 集合声明元素类型（分类 Acyclic）</summary>
    public class DispElementBase
    {
        public int Value { get; set; }
    }

    /// <summary>Cyclic 派生元素 — 自引用成环（运行时类型分发转发基类的触发器）</summary>
    public sealed class DispElementCyclic : DispElementBase
    {
        public DispElementCyclic? Self { get; set; }
    }

    /// <summary>Acyclic 容器根 — 元素声明类型 Acyclic，运行时可携带 Cyclic 派生实例</summary>
    public class DispPolyContainer
    {
        public int Id { get; set; }
        public List<DispElementBase>? Items { get; set; }
    }

    /// <summary>有环根 + 无环分支 — 整体分类 Cyclic（可达 Ring 环）</summary>
    public class DispMixedRoot
    {
        public int Id { get; set; }
        public DispRingNode? Ring { get; set; }
        public DispBranchLeaf? Leaf { get; set; }
    }

    // Acyclic 深链（深度守卫测试）— 每层独立类型避免成环，全部判 Acyclic 走快路径
    public class DispChainL0 { public int V { get; set; } public DispChainL1? Next { get; set; } }
    public class DispChainL1 { public int V { get; set; } public DispChainL2? Next { get; set; } }
    public class DispChainL2 { public int V { get; set; } public DispChainL3? Next { get; set; } }
    public class DispChainL3 { public int V { get; set; } public DispChainL4? Next { get; set; } }
    public class DispChainL4 { public int V { get; set; } public DispChainL5? Next { get; set; } }
    public class DispChainL5 { public int V { get; set; } public DispChainL6? Next { get; set; } }
    public class DispChainL6 { public int V { get; set; } public DispChainL7? Next { get; set; } }
    public class DispChainL7 { public int V { get; set; } }

    // ================================================================
    // 辅助方法
    // ================================================================

    /// <summary>构建 length 节点单环（Node[i].Next = Node[i+1]，尾节点指回头）</summary>
    private static DispRingNode BuildRing(int length)
    {
        var head = new DispRingNode { Value = 0 };
        var cur = head;
        for (int i = 1; i < length; i++)
        {
            cur.Next = new DispRingNode { Value = i };
            cur = cur.Next;
        }
        cur.Next = head;
        return head;
    }

    /// <summary>断言环结构保持：从头走 length 步回到头，且沿途值正确</summary>
    private static void AssertRingPreserved(DispRingNode? copyHead, int length)
    {
        Assert.NotNull(copyHead);
        var cur = copyHead!;
        for (int i = 0; i < length; i++)
        {
            Assert.NotNull(cur.Next);
            cur = cur.Next!;
            Assert.Equal((i + 1) % length, cur.Value);
        }
        Assert.Same(copyHead, cur);
    }

    // ================================================================
    // 分类前置断言（防止测试场景被误分发导致假通过）
    // ================================================================

    /// <summary>
    /// 测试模型的分类结果与预期一致 — 保证各用例确实走到目标分发分支
    /// </summary>
    [Fact]
    public void Classifier_Preconditions_TestModels_ClassifiedAsExpected()
    {
        Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(DispRingNode)));
        Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(DispFlat)));
        Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(DispSelfRefRecord)));
        Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(DispRecA)));
        Assert.Equal(DeepCopyGraphShape.Unknown, DeepCopyTypeClassifier.Classify(typeof(DispVirtualBase)));
        Assert.Equal(DeepCopyGraphShape.Unknown, DeepCopyTypeClassifier.Classify(typeof(DispVirtualSelfLoop)));
        Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(DispMixedRoot)));
        Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(DispChainL0)));
        Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(DispElementBase)));
        Assert.Equal(DeepCopyGraphShape.Cyclic, DeepCopyTypeClassifier.Classify(typeof(DispElementCyclic)));
        Assert.Equal(DeepCopyGraphShape.Acyclic, DeepCopyTypeClassifier.Classify(typeof(DispPolyContainer)));
    }

    // ================================================================
    // 任意环长（> 8，方案 E 会触发深度守卫，方案 F 翻正）
    // ================================================================

    /// <summary>
    /// 20 节点环在默认 Fast Provider 下返回有效拷贝，环结构保持
    /// （「任意环长」；E 的环形缓冲仅 8 槽，20 节点环会撞上深度守卫）
    /// </summary>
    [Fact]
    public void CyclicRing20_FastProvider_FullDetection_RingPreserved()
    {
        var head = BuildRing(20);

        var copy = Mapper.DeepCopy(head);

        Assert.NotNull(copy);
        Assert.NotSame(head, copy);
        AssertRingPreserved(copy, 20);
    }

    /// <summary>
    /// 12 节点环（恰好超过环形缓冲 8 槽）同样经完整检测返回有效拷贝
    /// </summary>
    [Fact]
    public void CyclicRing12_ExceedsFastRingBuffer_NoMapException()
    {
        var head = BuildRing(12);

        var copy = Mapper.DeepCopy(head);

        Assert.NotNull(copy);
        AssertRingPreserved(copy, 12);
    }

    // ================================================================
    // record + 循环（E 的 MapException 场景翻正）
    // ================================================================

    /// <summary>
    /// 自引用 record：E 的 Fast 路径在 CreateInstance 期间环插入尚未发生，递归撞深度守卫抛 MapException；
    /// F 分类 Cyclic 转发基类 record 占位路径，正常返回拷贝。
    /// <para>占位语义说明：构造器参数递归命中占位实例，copy.Next 指向占位实例而非最终 copy
    /// （基类 DeepCopyProviderBase.cs:106-121 的已知行为），断言以"无异常 + 独立拷贝 + 值正确"为正例口径。</para>
    /// </summary>
    [Fact]
    public void Record_SelfRef_CyclicDispatch_NoException_CopyIndependent()
    {
        var rec = new DispSelfRefRecord(42, null);
        rec = rec with { Next = rec };

        var copy = Mapper.DeepCopy(rec);

        Assert.NotNull(copy);
        Assert.NotSame(rec, copy);
        Assert.Equal(42, copy!.Value);
        Assert.NotNull(copy.Next);
    }

    /// <summary>
    /// record↔record 互引用：分类 Cyclic 转发基类，无异常，值正确
    /// </summary>
    [Fact]
    public void Record_MutualRef_CyclicDispatch_NoException_ValuesCorrect()
    {
        var a = new DispRecA(1, null);
        var b = new DispRecB(2, a);
        a = a with { Partner = b };

        var copyA = Mapper.DeepCopy(a);

        Assert.NotNull(copyA);
        Assert.NotSame(a, copyA);
        Assert.Equal(1, copyA!.Id);
        Assert.NotNull(copyA.Partner);
        Assert.Equal(2, copyA.Partner!.Id);
        Assert.NotNull(copyA.Partner.Owner);
    }

    // ================================================================
    // virtual 重写环（F1 验证，评审推演场景闭合）
    // ================================================================

    /// <summary>
    /// F1 场景：派生类 sealed override Link 返回 this 构成环；声明类型含 virtual 引用属性归 Unknown，
    /// Fast Provider 经完整检测返回有效拷贝（断言环打破指向拷贝自身，而非 MapException/栈溢出）
    /// </summary>
    [Fact]
    public void VirtualOverrideLoop_F1_UnknownDispatch_ValidCopy()
    {
        var source = new DispVirtualSelfLoop { Id = 7 };

        var copy = Mapper.DeepCopy(source);

        Assert.NotNull(copy);
        Assert.NotSame(source, copy);
        Assert.Equal(7, copy!.Id);
        // 环打破：copy.Link 指向拷贝自身（override getter 在拷贝实例上求值）
        Assert.Same(copy, copy.Link);
    }

    /// <summary>
    /// F1 场景基类型入口：以声明类型 DispVirtualBase 调用（运行时为成环派生类），同样经完整检测
    /// </summary>
    [Fact]
    public void VirtualOverrideLoop_BaseTypeEntry_ValidCopy()
    {
        DispVirtualBase source = new DispVirtualSelfLoop { Id = 9 };

        var copy = Mapper.DeepCopy(source);

        Assert.NotNull(copy);
        Assert.NotSame(source, copy);
        Assert.Equal(9, copy!.Id);
        Assert.Same(copy, copy.Link);
    }

    // ================================================================
    // 混合图：有环根 + 无环分支
    // ================================================================

    /// <summary>
    /// 有环根 + 无环分支子树：环打破且结构保持、分支值正确且独立、无异常
    /// </summary>
    [Fact]
    public void MixedGraph_CyclicRootWithAcyclicBranch_BothCorrect()
    {
        var root = new DispMixedRoot
        {
            Id = 7,
            Ring = BuildRing(12),
            Leaf = new DispBranchLeaf { Name = "leaf", Number = 3 },
        };

        var copy = Mapper.DeepCopy(root);

        Assert.NotNull(copy);
        Assert.NotSame(root, copy);
        Assert.Equal(7, copy!.Id);

        // 环分支：12 节点环结构保持
        AssertRingPreserved(copy.Ring, 12);
        Assert.NotSame(root.Ring, copy.Ring);

        // 无环分支：值正确且为独立实例
        Assert.NotSame(root.Leaf, copy.Leaf);
        Assert.Equal("leaf", copy.Leaf!.Name);
        Assert.Equal(3, copy.Leaf.Number);
    }

    /// <summary>
    /// T4 交互回归（Depth&gt;0 转发基类的 CycleCache 前置条件）：
    /// Acyclic 容器根的 List 元素在运行时为 Cyclic 派生类型（声明类型 Acyclic）。
    /// 集合元素经 CollectionCopier 按<b>运行时类型</b>分发（DeepCopyObjectCore），
    /// 在 Acyclic 快路径递归中途（Depth&gt;0）转发基类完整检测。
    /// <para>缺陷场景：经 Provider 公共入口调用（自建 ctx，不经过 Mapper 层 1 的 ??=）时，
    /// 基类若仅在 Depth==0 初始化 CycleCache，record 占位/POCO 入缓存写入将 NRE。
    /// 修复：DeepCopyProviderBase.DeepCopy 无条件 <c>CycleCache ??= new(...)</c>。</para>
    /// </summary>
    [Fact]
    public void MidRecursion_CyclicDispatch_ProviderPublicEntry_NoNre()
    {
        var provider = new FastDeepCopyProvider();
        var cyclic = new DispElementCyclic { Value = 3 };
        cyclic.Self = cyclic;
        var holder = new DispPolyContainer
        {
            Id = 1,
            Items = new List<DispElementBase>
            {
                new DispElementBase { Value = 1 },
                cyclic,
            },
        };

        // 公共入口：DeepCopy<T>(source) 内部新建 ctx，不经过层 1 ??=
        var copy = provider.DeepCopy(holder);

        Assert.NotNull(copy);
        Assert.NotSame(holder, copy);
        Assert.Equal(2, copy!.Items!.Count);
        Assert.Equal(1, copy.Items[0].Value);
        Assert.NotSame(holder.Items[1], copy.Items[1]);

        // Cyclic 派生元素经完整检测：环打破指向拷贝自身
        var copiedCyclic = Assert.IsType<DispElementCyclic>(copy.Items[1]);
        Assert.Equal(3, copiedCyclic.Value);
        Assert.Same(copiedCyclic, copiedCyclic.Self);
    }

    // ================================================================
    // 跨调用清环（D2 验证）
    // ================================================================

    /// <summary>
    /// D2 直接验证：构造残留脏环状态后再次顶层调用，分发前的 F1 重置保证不命中历史槽位
    /// <para>若 D2 缺失（重置不在分发之前或不存在），第二次调用环扫描命中 slot0 直接返回旧拷贝，
    /// NotSame 断言失败。</para>
    /// </summary>
    [Fact]
    public void D2_ResidualRingState_ResetAtTopLevel_NoStaleSlotHit()
    {
        var provider = new FastDeepCopyProvider();
        var ctx = new DeepCopyContext();
        var a = new DispFlat { Value = 1, Tag = "x" };

        var copy1 = provider.DeepCopy(a, ctx);
        Assert.NotNull(copy1);
        Assert.NotSame(a, copy1);

        // 构造残留脏环状态（模拟异常路径未完成 F2 回滚的跨调用泄漏）
        ctx.FastSources ??= new object[DeepCopyContext.FastRingSize];
        ctx.FastTargets ??= new object[DeepCopyContext.FastRingSize];
        ctx.FastSources[0] = a;
        ctx.FastTargets[0] = copy1;
        ctx.FastSlot = 1;
        Assert.Equal(0, ctx.Depth);  // 顶层退出后 Depth 已归零

        // 再次顶层调用：D2 重置清空槽位 → 必须生成新拷贝而非返回旧拷贝
        var copy2 = provider.DeepCopy(a, ctx);
        Assert.NotNull(copy2);
        Assert.NotSame(copy1, copy2);
        Assert.Equal(a.Value, copy2!.Value);
        Assert.Equal(a.Tag, copy2.Tag);
    }

    /// <summary>
    /// D2 行为级验证：Acyclic 顶层 → Cyclic 顶层 → Acyclic 顶层序列，产物互不污染
    /// （Mapper.DeepCopy 复用 [ThreadStatic] ctx）
    /// </summary>
    [Fact]
    public void D2_Sequence_Acyclic_Cyclic_Acyclic_NoCrossContamination()
    {
        var flat = new DispFlat { Value = 11, Tag = "seq" };

        var copy1 = Mapper.DeepCopy(flat);                    // Acyclic 顶层（快路径）
        var ringCopy = Mapper.DeepCopy(BuildRing(12));        // Cyclic 顶层（基类完整检测）
        var copy2 = Mapper.DeepCopy(flat);                    // Acyclic 顶层（快路径）

        Assert.NotNull(copy1);
        Assert.NotNull(copy2);
        Assert.NotSame(copy1, copy2);
        Assert.Equal(flat.Value, copy2!.Value);
        Assert.Equal(flat.Tag, copy2.Tag);
        AssertRingPreserved(ringCopy, 12);
    }

    // ================================================================
    // 深度守卫（Acyclic 快路径保留 E 行为）
    // ================================================================

    /// <summary>
    /// Acyclic 深链（8 层独立类型，合法无环图）超过 MaxRecursionDepth 时抛 MapException
    /// — 快路径深度守卫保留验证；try/finally 还原全局配置
    /// </summary>
    [Fact]
    public void DepthGuard_AcyclicDeepChain_ThrowsMapException()
    {
        var original = Mapper.MaxRecursionDepth;
        try
        {
            Mapper.MaxRecursionDepth = 4;
            var chain = new DispChainL0
            {
                V = 0,
                Next = new DispChainL1
                {
                    V = 1,
                    Next = new DispChainL2
                    {
                        V = 2,
                        Next = new DispChainL3
                        {
                            V = 3,
                            Next = new DispChainL4
                            {
                                V = 4,
                                Next = new DispChainL5
                                {
                                    V = 5,
                                    Next = new DispChainL6 { V = 6, Next = new DispChainL7 { V = 7 } },
                                },
                            },
                        },
                    },
                },
            };

            Assert.Throws<MapException>(() => Mapper.DeepCopy(chain));
        }
        finally
        {
            Mapper.MaxRecursionDepth = original;
        }
    }

    /// <summary>
    /// 同一深链在默认深度上限下正常拷贝（守卫不误伤合法图）
    /// </summary>
    [Fact]
    public void DepthGuard_AcyclicDeepChain_DefaultLimit_Succeeds()
    {
        var chain = new DispChainL0
        {
            V = 0,
            Next = new DispChainL1 { V = 1, Next = new DispChainL2 { V = 2 } },
        };

        var copy = Mapper.DeepCopy(chain);

        Assert.NotNull(copy);
        Assert.Equal(0, copy!.V);
        Assert.Equal(1, copy.Next!.V);
        Assert.Equal(2, copy.Next.Next!.V);
        Assert.NotSame(chain.Next, copy.Next);
    }

    // ================================================================
    // Provider 切换
    // ================================================================

    /// <summary>
    /// Fast → ExpressionTree → Fast 交叉调用无残留；finally 还原 Current
    /// </summary>
    [Fact]
    public void ProviderSwitch_Fast_ExpressionTree_Fast_NoResidue()
    {
        var original = DeepCopyProviderBase.Current;
        try
        {
            var flat = new DispFlat { Value = 5, Tag = "switch" };

            DeepCopyProviderBase.Current = new FastDeepCopyProvider();
            var ringCopy1 = Mapper.DeepCopy(BuildRing(12));

            DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
            var ringCopy2 = Mapper.DeepCopy(BuildRing(12));
            var flatCopyEt = Mapper.DeepCopy(flat);

            DeepCopyProviderBase.Current = new FastDeepCopyProvider();
            var flatCopy2 = Mapper.DeepCopy(flat);
            var ringCopy3 = Mapper.DeepCopy(BuildRing(20));

            AssertRingPreserved(ringCopy1, 12);
            AssertRingPreserved(ringCopy2, 12);
            AssertRingPreserved(ringCopy3, 20);
            Assert.Equal(5, flatCopyEt!.Value);
            Assert.Equal(5, flatCopy2!.Value);
            Assert.NotSame(flatCopyEt, flatCopy2);
        }
        finally
        {
            DeepCopyProviderBase.Current = original;
        }
    }

    // ================================================================
    // Acyclic 快路径基线健全性
    // ================================================================

    /// <summary>
    /// Acyclic 平坦类型仍走快路径且拷贝正确（分发器重构不改变无环行为）
    /// </summary>
    [Fact]
    public void Acyclic_FlatType_FastPath_CopyCorrect()
    {
        var source = new DispFlat { Value = 99, Tag = "fast" };

        var copy = Mapper.DeepCopy(source);

        Assert.NotNull(copy);
        Assert.NotSame(source, copy);
        Assert.Equal(99, copy!.Value);
        Assert.Equal("fast", copy.Tag);
    }
}
