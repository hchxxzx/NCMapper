using System;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// T2 — 层 1 IsAcyclic 条件分发 + 三参注册重载测试（-2）
    /// <para>修改泛型静态注册状态与 Mapper.MaxRecursionDepth，须串行隔离。</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class GeneratedDeepCopyLayer1Tests
    {
        // ==================== 测试模型与手写委托 ====================

        /// <summary>无环根（子图封闭性：Child 同为 Acyclic）</summary>
        public class L1AcyclicRoot
        {
            public string? Name { get; set; }
            public L1AcyclicChild? Child { get; set; }
        }

        public class L1AcyclicChild { public int Value { get; set; } }

        /// <summary>有环节点（Cyclic 路径：RegisterDeepCopyTarget 预登记）</summary>
        public class L1CyclicNode
        {
            public int Id { get; set; }
            public L1CyclicNode? Self { get; set; }
        }

        /// <summary>record 叶子（无无参构造器 → 基类占位路径 DeepCopyProviderBase.cs:106-121）</summary>
        public record L1RecordLeaf(string Name);

        /// <summary>无环根 + record 属性（NPE 回归：嵌套落 Provider 转发时 Depth>0）</summary>
        public class L1AcyclicWithRecord
        {
            public L1RecordLeaf? Rec { get; set; }
        }

        /// <summary>深度守卫测试链</summary>
        public class L1Chain { public L1Chain? Next { get; set; } }

        private static L1AcyclicRoot AcyclicRootDelegate(L1AcyclicRoot src, DeepCopyContext ctx)
        {
            var dst = new L1AcyclicRoot { Name = src.Name };
            dst.Child = src.Child != null ? Mapper.DeepCopy(src.Child, ctx) : null;
            return dst;
        }

        private static L1AcyclicChild AcyclicChildDelegate(L1AcyclicChild src, DeepCopyContext ctx)
            => new L1AcyclicChild { Value = src.Value };

        private static L1CyclicNode CyclicNodeDelegate(L1CyclicNode src, DeepCopyContext ctx)
        {
            var dst = new L1CyclicNode { Id = src.Id };
            Mapper.RegisterDeepCopyTarget(src, dst, ctx);
            dst.Self = src.Self != null ? Mapper.DeepCopy(src.Self, ctx) : null;
            return dst;
        }

        private static L1AcyclicWithRecord AcyclicWithRecordDelegate(L1AcyclicWithRecord src, DeepCopyContext ctx)
        {
            var dst = new L1AcyclicWithRecord();
            dst.Rec = src.Rec != null ? Mapper.DeepCopy(src.Rec, ctx) : null;
            return dst;
        }

        private static L1Chain ChainDelegate(L1Chain src, DeepCopyContext ctx)
        {
            var dst = new L1Chain();
            dst.Next = src.Next != null ? Mapper.DeepCopy(src.Next, ctx) : null;
            return dst;
        }

        // ==================== 层 1 Acyclic 分支 ====================

        [Fact]
        public void Layer1_Acyclic_SkipsCycleCache_CopyCorrect()
        {
            Mapper.RegisterGeneratedDeepCopy<L1AcyclicRoot>(AcyclicRootDelegate, acyclic: true);
            Mapper.RegisterGeneratedDeepCopy<L1AcyclicChild>(AcyclicChildDelegate, acyclic: true);
            try
            {
                var src = new L1AcyclicRoot { Name = "root", Child = new L1AcyclicChild { Value = 42 } };

                // 直接走带 ctx 入口，观察 CycleCache 状态（Acyclic 分支不初始化 → 保持 null）
                var ctx = new DeepCopyContext();
                var dst = Mapper.DeepCopy(src, ctx)!;

                Assert.Equal("root", dst.Name);
                Assert.NotNull(dst.Child);
                Assert.Equal(42, dst.Child!.Value);
                Assert.NotSame(src.Child, dst.Child);
                Assert.Null(ctx.CycleCache);   // 零检测：跳过 CycleCache
                Assert.Equal(0, ctx.Depth);     // Depth 正确还原
            }
            finally
            {
                Mapper.RegisterGeneratedDeepCopy<L1AcyclicRoot>(null, acyclic: false);
                Mapper.RegisterGeneratedDeepCopy<L1AcyclicChild>(null, acyclic: false);
            }
        }

        // ==================== 层 1 Cyclic 路径（现状保持） ====================

        [Fact]
        public void Layer1_Cyclic_UsesCycleCache_BreaksSelfReference()
        {
            Mapper.RegisterGeneratedDeepCopy<L1CyclicNode>(CyclicNodeDelegate, acyclic: false);
            try
            {
                var src = new L1CyclicNode { Id = 1 };
                src.Self = src;   // 自引用

                var ctx = new DeepCopyContext();
                var dst = Mapper.DeepCopy(src, ctx)!;

                Assert.Equal(1, dst.Id);
                Assert.NotNull(ctx.CycleCache);   // Cyclic 路径初始化并使用 CycleCache
                Assert.Same(dst, dst.Self);       // 环被打破指向拷贝自身
                Assert.NotSame(src, dst.Self);
                Assert.Equal(0, ctx.Depth);
            }
            finally
            {
                Mapper.RegisterGeneratedDeepCopy<L1CyclicNode>(null, acyclic: false);
            }
        }

        // ==================== Provider 转发前 CycleCache 懒初始化（NPE 修复）====================

        [Fact]
        public void Layer1_Acyclic_NestedRecord_ProviderForward_NoNPE()
        {
            // 无环分支不初始化 CycleCache，嵌套 record 无生成委托 → 落 Provider 转发（Depth > 0）。
            // 修复前：基类仅 Depth==0 时初始化（DeepCopyProviderBase.cs:84-85），
            // record 占位逻辑（110）ctx.CycleCache! 索引抛 NullReferenceException。
            Mapper.RegisterGeneratedDeepCopy<L1AcyclicWithRecord>(AcyclicWithRecordDelegate, acyclic: true);
            try
            {
                var src = new L1AcyclicWithRecord { Rec = new L1RecordLeaf("leaf") };

                var ctx = new DeepCopyContext();
                var dst = Mapper.DeepCopy(src, ctx)!;

                Assert.NotNull(dst.Rec);
                Assert.Equal("leaf", dst.Rec!.Name);
                Assert.Equal(0, ctx.Depth);
            }
            finally
            {
                Mapper.RegisterGeneratedDeepCopy<L1AcyclicWithRecord>(null, acyclic: false);
            }
        }

        // ==================== Acyclic 分支深度守卫 ====================

        [Fact]
        public void Layer1_Acyclic_DepthGuard_ThrowsMapException()
        {
            var original = Mapper.MaxRecursionDepth;
            Mapper.RegisterGeneratedDeepCopy<L1Chain>(ChainDelegate, acyclic: true);
            try
            {
                Mapper.MaxRecursionDepth = 5;

                // 10 层链 > 深度上限 5 → 守卫抛可 catch 的 MapException
                var head = new L1Chain();
                var cur = head;
                for (int i = 0; i < 10; i++)
                {
                    cur.Next = new L1Chain();
                    cur = cur.Next;
                }

                Assert.Throws<MapException>(() => Mapper.DeepCopy(head, new DeepCopyContext()));
            }
            finally
            {
                Mapper.MaxRecursionDepth = original;
                Mapper.RegisterGeneratedDeepCopy<L1Chain>(null, acyclic: false);
            }
        }

        // ==================== 测试隔离：ClearGenerated + 逐类型覆盖（v1.1 F2） ====================

        [Fact]
        public void ClearGenerated_DoesNotResetGenericStatics_PerTypeOverride_Works()
        {
            Mapper.RegisterGeneratedDeepCopy<L1AcyclicChild>(AcyclicChildDelegate, acyclic: true);

            Mapper.ClearGenerated();

            // 泛型静态字段限制（Mapper.cs ClearGenerated 注释）：ClearGenerated 无法清除 IsAcyclic/Func，
            // 委托仍可用（既有行为，Func 字段同模式）
            Assert.True(Mapper.HasGeneratedDeepCopy<L1AcyclicChild>());

            // 逐类型传 null 覆盖：同时重置 Func 与 IsAcyclic
            Mapper.RegisterGeneratedDeepCopy<L1AcyclicChild>(null, acyclic: false);
            Assert.False(Mapper.HasGeneratedDeepCopy<L1AcyclicChild>());

            // 覆盖后重注册生效，行为与覆盖前一致
            Mapper.RegisterGeneratedDeepCopy<L1AcyclicChild>(AcyclicChildDelegate, acyclic: true);
            try
            {
                var dst = Mapper.DeepCopy(new L1AcyclicChild { Value = 7 }, new DeepCopyContext())!;
                Assert.Equal(7, dst.Value);
            }
            finally
            {
                Mapper.RegisterGeneratedDeepCopy<L1AcyclicChild>(null, acyclic: false);
            }
        }

        // ==================== 旧重载语义等价 acyclic: false ====================

        [Fact]
        public void TwoArgOverload_ResetsIsAcyclic_Conservative()
        {
            Mapper.RegisterGeneratedDeepCopy<L1CyclicNode>(CyclicNodeDelegate, acyclic: true);
            try
            {
                // 两参重载重新注册 → 等价 acyclic: false，不残留 true 标志
                Mapper.RegisterGeneratedDeepCopy<L1CyclicNode>((Func<L1CyclicNode, DeepCopyContext, L1CyclicNode>)CyclicNodeDelegate);

                var src = new L1CyclicNode { Id = 2 };
                src.Self = src;
                var ctx = new DeepCopyContext();
                var dst = Mapper.DeepCopy(src, ctx)!;

                // IsAcyclic 若残留 true 将跳过 CycleCache → 自引用死递归；走 CycleCache 才能打破环
                Assert.NotNull(ctx.CycleCache);
                Assert.Same(dst, dst.Self);
            }
            finally
            {
                Mapper.RegisterGeneratedDeepCopy<L1CyclicNode>(null, acyclic: false);
            }
        }
    }
}
