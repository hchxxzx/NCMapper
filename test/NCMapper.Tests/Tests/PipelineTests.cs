using Xunit;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// T5: 映射管道测试 — PostMap 回调、类型过滤、执行顺序、线程安全
    /// </summary>
    public class PipelineTests : IDisposable
    {
        /// <summary>
        /// 每个测试后清除管道，避免影响其他测试
        /// </summary>
        public void Dispose()
        {
            Mapper.Pipeline.Clear();
        }

        /// <summary>
        /// PostMap 修改 DTO 属性
        /// </summary>
        [Fact]
        public void Pipeline_PostMap_Executed()
        {
            Mapper.Pipeline.AddPostMap<ConverterSourceEntity, ConverterDto>((src, dto) =>
            {
                dto.Id = 999;
            });

            var entity = new ConverterSourceEntity { Id = 1 };
            var dto = Mapper.Map<ConverterSourceEntity, ConverterDto>(entity)!;

            Assert.Equal(999, dto.Id);
        }

        /// <summary>
        /// 类型过滤 — PostMap 只对指定类型对生效
        /// </summary>
        [Fact]
        public void Pipeline_TypeFilter_OnlyMatchingTypes()
        {
            bool postMapCalled = false;

            Mapper.Pipeline.AddPostMap<ConverterSourceEntity, ConverterDto>((src, dto) =>
            {
                postMapCalled = true;
            });

            // 映射不匹配的类型对 — PostMap 不应触发
            var entity = new MultiFieldSourceEntity { Id = 1 };
            Mapper.Map<MultiFieldSourceEntity, MultiFieldDto>(entity);

            Assert.False(postMapCalled);
        }

        /// <summary>
        /// 多个 PostMap 按注册逆序执行
        /// </summary>
        [Fact]
        public void Pipeline_MultiplePostMap_ReverseOrder()
        {
            var order = new List<int>();

            Mapper.Pipeline
                .AddPostMap<ConverterSourceEntity, ConverterDto>((src, dto) => { order.Add(1); })
                .AddPostMap<ConverterSourceEntity, ConverterDto>((src, dto) => { order.Add(2); })
                .AddPostMap<ConverterSourceEntity, ConverterDto>((src, dto) => { order.Add(3); });

            var entity = new ConverterSourceEntity { Id = 1 };
            Mapper.Map<ConverterSourceEntity, ConverterDto>(entity);

            // 逆序执行：3, 2, 1
            Assert.Equal(new[] { 3, 2, 1 }, order);
        }

        /// <summary>
        /// 源生成器路径 + 管道正常触发
        /// </summary>
        [Fact]
        public void Pipeline_SourceGen_Compatible()
        {
            // ConverterDto 标注了 [MapFrom]，走源生成器路径（GeneratedCache）
            bool postMapCalled = false;

            Mapper.Pipeline.AddPostMap<ConverterSourceEntity, ConverterDto>((src, dto) =>
            {
                postMapCalled = true;
                Assert.Equal("2025-03-15", dto.CreateTime); // 源生成器映射结果正确
            });

            var entity = new ConverterSourceEntity { Id = 1, CreateTime = new(2025, 3, 15, 10, 30, 0) };
            var dto = Mapper.Map<ConverterSourceEntity, ConverterDto>(entity)!;

            Assert.True(postMapCalled);
            Assert.Equal("2025-03-15", dto.CreateTime);
        }

        /// <summary>
        /// PostMap 注册后降级路径 MapList 调用 PostMap
        /// </summary>
        [Fact]
        public void Pipeline_AsDto_AutoDowngrade_PostMap_Called()
        {
            bool postMapCalled = false;
            Mapper.Pipeline.AddPostMap<ConverterSourceEntity, ConverterDto>((src, dto) =>
            {
                postMapCalled = true;
            });

            // 降级路径（MapList）正确调用 PostMap
            var entities = new List<ConverterSourceEntity>
            {
                new() { Id = 42, CreateTime = new(2025, 1, 1) }
            };
            var dtos = Mapper.MapList<ConverterSourceEntity, ConverterDto>(entities);

            Assert.True(postMapCalled);
            Assert.NotNull(dtos);
            Assert.Equal(42, dtos![0].Id);
        }

        /// <summary>
        /// 多线程并发注册和映射 — 无异常，结果正确
        /// </summary>
        [Fact]
        public void Pipeline_ThreadSafe_ConcurrentRegister()
        {
            int counter = 0;

            Mapper.Pipeline.AddPostMap<ConverterSourceEntity, ConverterDto>((src, dto) =>
            {
                System.Threading.Interlocked.Increment(ref counter);
            });

            var exceptions = new List<Exception>();
            var entities = Enumerable.Range(0, 50)
                .Select(i => new ConverterSourceEntity { Id = i })
                .ToList();

            System.Threading.Tasks.Parallel.ForEach(entities, entity =>
            {
                try
                {
                    Mapper.Map<ConverterSourceEntity, ConverterDto>(entity);
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
            Assert.Equal(50, counter);
        }

        /// <summary>
        /// Execute 期间并发 Clear + 回插注册 — 回调计数守恒（C5 补测，期望总量法 E18 参照）
        /// <para>目标场景：MapPipeline Execute 中 Clear/T-1 回插竞态（126 现有测试仅覆盖
        /// 静态注册后并发 Map，未覆盖注册/清除与执行交错）。执行路径为快照语义
        /// （逆序数组快照后遍历，见 MapPipeline.ExecutePostMap），故每次 Map 至多触发
        /// 一次回调：断言 回调总执行次数 ≤ Map 次数（不重复），竞态零异常。</para>
        /// <para>恢复守恒：竞态结束后 Clear + 重新注册，单线程 Map 50 次，
        /// 断言回调增量恰为 50（回插后回调恢复，期望总量精确守恒）。</para>
        /// </summary>
        [Fact]
        public async Task Pipeline_Execute_ConcurrentClearAndReinsert_CountConserved()
        {
            int postMapCount = 0;
            int mapCount = 0;

            Mapper.Pipeline.AddPostMap<ConverterSourceEntity, ConverterDto>((src, dto) =>
            {
                System.Threading.Interlocked.Increment(ref postMapCount);
            });

            using var cts = new CancellationTokenSource();
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            // 线程1: 并发 Map（每次成功完成计数）
            // do-while 保证至少执行一次 Map——CI 低核高负载下线程可能在整个 20ms 时窗内
            // 未获调度，while 前置检查在 Cancel 后直接退出导致 mapCount==0 空转误报
            // （AuditAcceptanceTests C07 D6 修复同型）。
            var t1 = Task.Run(() =>
            {
                do
                {
                    try
                    {
                        Mapper.Map<ConverterSourceEntity, ConverterDto>(new ConverterSourceEntity { Id = 1 });
                        System.Threading.Interlocked.Increment(ref mapCount);
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                } while (!cts.Token.IsCancellationRequested);
            }, TestContext.Current.CancellationToken);

            // 线程2: 并发 Clear + 回插（重新注册同一回调）
            var t2 = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        Mapper.Pipeline.Clear();
                        Mapper.Pipeline.AddPostMap<ConverterSourceEntity, ConverterDto>((src, dto) =>
                        {
                            System.Threading.Interlocked.Increment(ref postMapCount);
                        });
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }
            }, TestContext.Current.CancellationToken);

            Thread.Sleep(20);
            cts.Cancel();
            await Task.WhenAll(t1, t2);

            // 竞态守恒：快照语义下每次 Map 至多触发一次回调（不重复），无异常
            Assert.Empty(exceptions);
            Assert.True(mapCount > 0, "并发 Map 空转（未执行任何映射）");
            Assert.True(postMapCount <= mapCount,
                $"回调计数越界: postMapCount={postMapCount} > mapCount={mapCount}（回调重复执行）");

            // 恢复守恒：清理后重新注册，单线程 Map 50 次 → 回调增量恰为 50
            Mapper.Pipeline.Clear();
            int restored = 0;
            Mapper.Pipeline.AddPostMap<ConverterSourceEntity, ConverterDto>((src, dto) =>
            {
                System.Threading.Interlocked.Increment(ref restored);
            });

            for (int i = 0; i < 50; i++)
            {
                Mapper.Map<ConverterSourceEntity, ConverterDto>(new ConverterSourceEntity { Id = i });
            }

            Assert.Equal(50, restored);
        }

        /// <summary>
        /// Clear 清除所有管道 — 后续映射不走管道
        /// </summary>
        [Fact]
        public void Pipeline_Clear_RemovesAll()
        {
            bool postMapCalled = false;

            Mapper.Pipeline.AddPostMap<ConverterSourceEntity, ConverterDto>((src, dto) =>
            {
                postMapCalled = true;
            });

            // Clear 后管道不应触发
            Mapper.Pipeline.Clear();

            var entity = new ConverterSourceEntity { Id = 1 };
            Mapper.Map<ConverterSourceEntity, ConverterDto>(entity);

            Assert.False(postMapCalled);
            Assert.False(Mapper.Pipeline.IsEnabled);
        }
    }
}
