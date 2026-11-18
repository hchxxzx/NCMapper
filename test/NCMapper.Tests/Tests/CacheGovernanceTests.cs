using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 缓存治理测试 — ClearCache 级联完备性
    /// <para>覆盖 ExpressionBuilder.s_directionAmbiguousNotified 接入 Mapper.ClearCache 链后的
    /// 正向断言（清空前 &gt;0 → ClearCache 后 =0 → 新类型对重建）、去重语义与并发弱一致契约。
    /// 因测试触发全局 ClearCache，加入 GlobalConfigState 串行集合（DisableParallelization）。</para>
    /// <para><b>设计约束（保留项语义）</b>MapperBase&lt;TSource,TTarget&gt;.MapFunc 为
    /// 静态 Lazy（一经物化不被 ClearCache 清除），同类型对首次映射触发方向歧义 Trace 后，
    /// 后续映射走已物化委托不再重新编译——故"重建"断言须用<b>新类型对</b>（触发重新编译），
    /// 并用独立类型对隔离不同测试间的物化顺序影响。</para>
    /// </summary>
    [Collection("GlobalConfigState")]
    public class CacheGovernanceTests
    {
        /// <summary>
        /// 方向歧义 Trace 触发载体组 A — 源类型（双侧均无 [MapFrom]，含方向性 [MapIgnore] 属性）
        /// <para>标注 Direction.ToDto：双 false 场景 isToDto=false 时不忽略，属性保留在源列表
        /// （sourceItem 非 null 且 HasDirectionalMapIgnore=true）→ 触发方向歧义 Trace。
        /// 若标注 ToEntity 会在 isToDto=false 方向被 GetPropertyList 属性枚举剔除（sourceItem=null）不触发。</para>
        /// </summary>
        private class AmbiguitySource
        {
            /// <summary>
            /// 主键
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 方向性忽略属性（Direction.ToDto）— 满足方向歧义判定条件（HasDirectionalMapIgnore）
            /// </summary>
#pragma warning disable NCMAP019
            [MapIgnore(Direction.ToDto)]
#pragma warning restore NCMAP019
            public string? Secret { get; set; }
        }

        /// <summary>
        /// 方向歧义 Trace 触发载体组 A — 目标类型（双侧均无 [MapFrom]）
        /// </summary>
        private class AmbiguityTarget
        {
            /// <summary>
            /// 主键
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 与源 Secret 对应（无 [MapIgnore] 标注）
            /// </summary>
            public string? Secret { get; set; }
        }

        /// <summary>
        /// 方向歧义 Trace 触发载体组 B — 源类型（重建断言用，独立类型对隔离物化顺序）
        /// </summary>
        private class AmbiguitySourceB
        {
            /// <summary>
            /// 主键
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 方向性忽略属性（Direction.ToDto）
            /// </summary>
            [MapIgnore(Direction.ToDto)]
            public string? Secret { get; set; }
        }

        /// <summary>
        /// 方向歧义 Trace 触发载体组 B — 目标类型
        /// </summary>
        private class AmbiguityTargetB
        {
            /// <summary>
            /// 主键
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 与源 Secret 对应
            /// </summary>
            public string? Secret { get; set; }
        }

        /// <summary>
        /// 并发弱一致契约载体 — 源类型（独立类型对，避免并发测试物化顺序影响正向测试的 Count 断言）
        /// </summary>
        private class AmbiguitySourceC
        {
            /// <summary>
            /// 主键
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 方向性忽略属性（Direction.ToDto）
            /// </summary>
            [MapIgnore(Direction.ToDto)]
            public string? Secret { get; set; }
        }

        /// <summary>
        /// 并发弱一致契约载体 — 目标类型
        /// </summary>
        private class AmbiguityTargetC
        {
            /// <summary>
            /// 主键
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 与源 Secret 对应
            /// </summary>
            public string? Secret { get; set; }
        }

        /// <summary>
        /// 每个测试用例执行前清空全局缓存，避免跨用例缓存状态泄漏（既有 GlobalConfigState 集合惯例）
        /// </summary>
        public CacheGovernanceTests()
        {
            Mapper.ClearCache();
        }

        /// <summary>
        /// F-1 正向断言：首次映射触发方向歧义去重 → ClearCache 级联归零 → 新类型对重建
        /// <para>断言载体遵循规则 3：清空后条目归零 + 重建结果守恒（防恒真断言）。
        /// 同类型对再次映射不重复触发（MapFunc 已物化，去重语义保持）。</para>
        /// </summary>
        [Fact]
        public void ClearCache_Resets_DirectionAmbiguityNotified()
        {
            // 组 A 首次映射：物化 MapFunc 时触发方向歧义 Trace → 去重集恰好 1 条
            var first = Mapper.Map<AmbiguitySource, AmbiguityTarget>(new AmbiguitySource { Id = 1, Secret = "s" });
            Assert.Equal(1, ExpressionBuilder.CacheEntryCount);

            // ClearCache 级联 → 归零
            Mapper.ClearCache();
            Assert.Equal(0, ExpressionBuilder.CacheEntryCount);

            // 组 B 新类型对首次映射：重新编译触发 → 重建（去重集恢复，功能守恒）
            var second = Mapper.Map<AmbiguitySourceB, AmbiguityTargetB>(new AmbiguitySourceB { Id = 2, Secret = "t" });
            Assert.Equal(1, ExpressionBuilder.CacheEntryCount);

            // 组 A 同类型对再次映射：MapFunc 已物化不重新编译 → 不重复触发（去重语义），Count 仍为组 B 的 1 条
            Mapper.Map<AmbiguitySource, AmbiguityTarget>(new AmbiguitySource { Id = 3, Secret = "u" });
            Assert.Equal(1, ExpressionBuilder.CacheEntryCount);

            // 映射结果守恒：Id 正常映射、Secret 在双 false 方向（isToDto=false）ToDto 标注不忽略 → 正常映射
            Assert.Equal(1, first!.Id);
            Assert.Equal("s", first.Secret);
            Assert.Equal(2, second!.Id);
            Assert.Equal("t", second.Secret);
        }

        /// <summary>
        /// 并发弱一致契约（规则 3 +）：清空与并发构建交错不抛异常
        /// <para>弱一致语义：清空即失效、在途构建结果可丢弃、下次访问重建（已验证范式）；
        /// ConcurrentDictionary 并发安全，Clear 后任一线程下次访问触发重建，结果不受影响。</para>
        /// </summary>
        [Fact]
        public async Task ClearCache_Concurrent_WeakConsistency_NoThrow()
        {
            var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            var tasks = new List<Task>();

            for (int t = 0; t < 4; t++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < 200; i++)
                    {
                        try
                        {
                            var dto = Mapper.Map<AmbiguitySourceC, AmbiguityTargetC>(
                                new AmbiguitySourceC { Id = i, Secret = "s" });
                            if (dto!.Id != i)
                            {
                                errors.Enqueue(new InvalidOperationException($"映射结果不一致: 期望 {i} 实际 {dto.Id}"));
                            }

                            // 每 10 次迭代触发一次全局清空，制造清理与并发构建交错
                            if (i % 10 == 0)
                            {
                                Mapper.ClearCache();
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Enqueue(ex);
                        }
                    }
                }, TestContext.Current.CancellationToken));
            }

            await Task.WhenAll(tasks.ToArray());
            Assert.Empty(errors);
        }
    }
}
