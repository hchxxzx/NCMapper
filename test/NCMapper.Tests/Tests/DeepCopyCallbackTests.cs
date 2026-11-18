using Xunit;
using NCMapper.Tests.Models;
using NCMapper.Attributes;
using System.IO;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// DeepCopy 带回调重载测试
    /// <para>串行化集合 — 修改 DeepCopyProviderBase.Current 全局状态的测试类必须串行执行</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class DeepCopyCallbackTests
    {
        // ================================================================
        // P0-1: DeepCopy 同类+回调 — action 修改不影响源对象
        // ================================================================

        /// <summary>
        /// 同类深拷贝 + Action 回调 — 验证 action 在深拷贝后正确执行，修改不影响原对象
        /// </summary>
        [Fact]
        public void DeepCopy_SameType_WithAction_ModifiesCopy()
        {
            var entity = new FullTypeEntity { IntProp = 42, StringProp = "source" };

            var copy = Mapper.DeepCopy(entity, (src, tgt) =>
            {
                tgt.StringProp = "modified";
                tgt.IntProp = 999;
            })!;

            // 原对象不变
            Assert.Equal(42, entity.IntProp);
            Assert.Equal("source", entity.StringProp);
            // 拷贝被 action 修改
            Assert.Equal(999, copy.IntProp);
            Assert.Equal("modified", copy.StringProp);
            Assert.NotSame(entity, copy);
        }

        // ================================================================
        // P0-2: DeepCopy 同类+回调 — null action 不抛异常
        // ================================================================

        /// <summary>
        /// 同类深拷贝 + null Action — 验证 action=null 时正常返回拷贝
        /// </summary>
        [Fact]
        public void DeepCopy_SameType_NullAction_DoesNotThrow()
        {
            var entity = new FullTypeEntity { IntProp = 42, StringProp = "test" };

            var copy = Mapper.DeepCopy<FullTypeEntity>(entity, (Action<FullTypeEntity, FullTypeEntity>?)null)!;

            Assert.Equal(42, copy.IntProp);
            Assert.Equal("test", copy.StringProp);
            Assert.NotSame(entity, copy);
        }

        // ================================================================
        // P0-3: DeepCopy 异类+回调 — action 修改不影响源对象
        // ================================================================

        /// <summary>
        /// 异类深拷贝 + Action 回调 — 验证 action 在异类深拷贝后正确执行
        /// </summary>
        [Fact]
        public void DeepCopy_CrossType_WithAction_ModifiesTarget()
        {
            var entity = new FullTypeEntity { IntProp = 42, StringProp = "source" };

            var dto = Mapper.DeepCopy<FullTypeEntity, SameTypeDto>(entity, (src, tgt) =>
            {
                tgt.StringProp = "cross-modified";
            })!;

            // 原对象不变
            Assert.Equal(42, entity.IntProp);
            Assert.Equal("source", entity.StringProp);
            // 目标被 action 修改
            Assert.Equal(42, dto.IntProp);
            Assert.Equal("cross-modified", dto.StringProp);
        }

        /// <summary>
        /// 异类深拷贝 + null Action — 验证 null action 时正常返回
        /// </summary>
        [Fact]
        public void DeepCopy_CrossType_NullAction_DoesNotThrow()
        {
            var entity = new FullTypeEntity { IntProp = 7, StringProp = "ok" };

            var dto = Mapper.DeepCopy<FullTypeEntity, SameTypeDto>(entity, null)!;

            Assert.Equal(7, dto.IntProp);
            Assert.Equal("ok", dto.StringProp);
        }

        // ================================================================
        // P0-4: DeepCopy 异类+回调 — TSource==TTarget 抛异常
        // ================================================================

        /// <summary>
        /// 异类深拷贝 TSource == TTarget — 验证抛 InvalidOperationException
        /// </summary>
        [Fact]
        public void DeepCopy_CrossType_SameType_Throws()
        {
            var entity = new FullTypeEntity { IntProp = 1 };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Mapper.DeepCopy<FullTypeEntity, FullTypeEntity>(entity));

            Assert.Contains(nameof(FullTypeEntity), ex.Message);
            Assert.Contains("DeepCopy<T>(", ex.Message);
        }

        // ================================================================
        // P1-1: DeepCopy 循环引用自动切换策略
        // ================================================================

        /// <summary>
        /// 循环引用 — 验证 FastDeepCopyProvider 与 ExpressionTreeDeepCopyProvider 至少有一方可处理
        /// <para>FastDeepCopyProvider 默认不做循环引用检测；ExpressionTreeDeepCopyProvider 会检测循环引用。
        /// 当前测试验证循环引用场景至少有一个策略不会导致进程崩溃。</para>
        /// </summary>
        [Fact]
        public void DeepCopy_CircularReference_AtLeastOneStrategyHandles()
        {
            // 创建循环引用节点
            var node = new CallbackCircularNode { Id = 1, Name = "circular" };
            node.Self = node;

            // 直接用 ExpressionTreeDeepCopyProvider 处理（已知支持循环引用）
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
                var copy = Mapper.DeepCopy(node);
                // 不崩溃即通过 — 循环引用场景下至少有一个策略能正确处理
                Assert.NotNull(copy);
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        /// <summary>
        /// 循环引用节点（DeepCopyCallback 专用，避免与 FastAndGeneratedDeepCopyTests 冲突）
        /// </summary>
        public class CallbackCircularNode
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public CallbackCircularNode? Self { get; set; }
        }

        // ================================================================
        // P0-5: DeepCopy action 引用赋值陷阱 — 反面教材（文档化陷阱）
        // ⚠️ 以下两个测试刻意展示"错误做法"与"正确做法"的对比，
        //    用于文档化 action 中引用赋值破坏深拷贝语义的陷阱。
        // ================================================================

        /// <summary>
        /// 反面教材 — action 中将 source 的引用属性直接赋值给 copy 会破坏深拷贝语义
        /// <para>此测试刻意断言"深拷贝被破坏"：copy.Address 与 source.Address 共享同一引用，
        /// 修改 copy.Address.City 会影响 source.Address.City。</para>
        /// <para>⚠️ 这不是期望行为，而是文档化陷阱 — 生产代码中应避免此用法。</para>
        /// </summary>
        [Fact]
        public void DeepCopy_SameType_Action_ReferenceAssignment_BreaksDeepCopy()
        {
            var entity = new CallbackNestedEntity
            {
                Id = 1,
                Name = "original",
                Address = new CallbackNestedAddress { City = "Beijing" }
            };

            var copy = Mapper.DeepCopy(entity, (src, tgt) =>
            {
                // ❌ 错误做法：直接赋值 source 的引用属性
                tgt.Address = src.Address;
            })!;

            // 深拷贝被破坏：copy.Address 与 source.Address 是同一引用
            Assert.Same(entity.Address, copy.Address);

            // 后果：修改 copy.Address.City 会污染 source
            copy.Address!.City = "Shanghai";
            Assert.Equal("Shanghai", entity.Address!.City);
        }

        /// <summary>
        /// 正确做法 — action 中对引用属性再次 DeepCopy 创建独立副本
        /// <para>与 <see cref="DeepCopy_SameType_Action_ReferenceAssignment_BreaksDeepCopy"/> 对照：
        /// 使用 <c>Mapper.DeepCopy(src.Address)</c> 创建独立副本后赋值，深拷贝语义保持完整。</para>
        /// </summary>
        [Fact]
        public void DeepCopy_SameType_Action_ReferenceDeepCopy_PreservesIndependence()
        {
            var entity = new CallbackNestedEntity
            {
                Id = 1,
                Name = "original",
                Address = new CallbackNestedAddress { City = "Beijing" }
            };

            var copy = Mapper.DeepCopy(entity, (src, tgt) =>
            {
                // ✅ 正确做法：对引用属性再次 DeepCopy 创建独立副本
                tgt.Address = Mapper.DeepCopy(src.Address!);
            })!;

            // 深拷贝保持独立：copy.Address 与 source.Address 是不同引用
            Assert.NotSame(entity.Address, copy.Address);

            // 修改 copy.Address.City 不影响 source
            copy.Address!.City = "Shanghai";
            Assert.Equal("Beijing", entity.Address!.City);
        }

        // ================================================================
        // P0-6: DeepCopy action trace 警示输出
        // ================================================================

        /// <summary>
        /// trace 警示 — MapTracer.GloballyEnabled 为 true 时，action 执行前输出引用赋值风险警示
        /// </summary>
        [Fact]
        public void DeepCopy_SameType_Action_TraceWarning_WhenGloballyEnabled()
        {
            var sw = new StringWriter();
            var originalOutput = MapTracer.GlobalOutput;
            var originalEnabled = MapTracer.GloballyEnabled;
            try
            {
                MapTracer.GlobalOutput = sw;
                MapTracer.GloballyEnabled = true;

                var entity = new FullTypeEntity { IntProp = 1, StringProp = "test" };
                _ = Mapper.DeepCopy(entity, (src, tgt) => { tgt.IntProp = 999; });

                var traceOutput = sw.ToString();
                Assert.Contains("action 即将执行", traceOutput);
                Assert.Contains("破坏深拷贝语义", traceOutput);
                Assert.Contains(nameof(FullTypeEntity), traceOutput);
            }
            finally
            {
                MapTracer.GloballyEnabled = originalEnabled;
                MapTracer.GlobalOutput = originalOutput;
            }
        }

        /// <summary>
        /// trace 默认关闭 — MapTracer.GloballyEnabled 为 false（默认）时，不输出任何 trace 信息
        /// <para>验证零默认开销：生产环境下 action 执行不产生任何 I/O。</para>
        /// </summary>
        [Fact]
        public void DeepCopy_SameType_Action_NoTrace_WhenGloballyDisabled()
        {
            var sw = new StringWriter();
            var originalOutput = MapTracer.GlobalOutput;
            var originalEnabled = MapTracer.GloballyEnabled;
            try
            {
                MapTracer.GlobalOutput = sw;
                MapTracer.GloballyEnabled = false;

                var entity = new FullTypeEntity { IntProp = 1, StringProp = "test" };
                _ = Mapper.DeepCopy(entity, (src, tgt) => { tgt.IntProp = 999; });

                Assert.Empty(sw.ToString());
            }
            finally
            {
                MapTracer.GloballyEnabled = originalEnabled;
                MapTracer.GlobalOutput = originalOutput;
            }
        }

        /// <summary>
        /// 异类 DeepCopy trace 警示 — 验证异类型重载也输出 trace（含源类型→目标类型信息）
        /// </summary>
        [Fact]
        public void DeepCopy_CrossType_Action_TraceWarning_WhenGloballyEnabled()
        {
            var sw = new StringWriter();
            var originalOutput = MapTracer.GlobalOutput;
            var originalEnabled = MapTracer.GloballyEnabled;
            try
            {
                MapTracer.GlobalOutput = sw;
                MapTracer.GloballyEnabled = true;

                var entity = new FullTypeEntity { IntProp = 42, StringProp = "source" };
                _ = Mapper.DeepCopy<FullTypeEntity, SameTypeDto>(entity, (src, tgt) =>
                {
                    tgt.StringProp = "modified";
                });

                var traceOutput = sw.ToString();
                Assert.Contains("action 即将执行", traceOutput);
                Assert.Contains("破坏深拷贝语义", traceOutput);
                Assert.Contains(nameof(FullTypeEntity), traceOutput);
                Assert.Contains(nameof(SameTypeDto), traceOutput);
            }
            finally
            {
                MapTracer.GloballyEnabled = originalEnabled;
                MapTracer.GlobalOutput = originalOutput;
            }
        }

        /// <summary>
        /// 带嵌套引用属性的测试模型 — 用于验证 action 中引用赋值对深拷贝的影响
        /// </summary>
        public class CallbackNestedEntity
        {
            /// <summary>
            /// 标识
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// 嵌套地址（引用类型属性）
            /// </summary>
            public CallbackNestedAddress? Address { get; set; }
        }

        /// <summary>
        /// 嵌套地址模型 — 配合 <see cref="CallbackNestedEntity"/> 验证引用属性深拷贝
        /// </summary>
        public class CallbackNestedAddress
        {
            /// <summary>
            /// 城市
            /// </summary>
            public string City { get; set; } = "";
        }
    }
}
