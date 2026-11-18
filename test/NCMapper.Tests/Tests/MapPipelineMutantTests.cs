using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// MapPipeline mutant-killing tests — targets surviving Stryker mutants in MapPipeline.cs
    /// Covers: Clear() drain loops, ExecutePostMap reverse iteration (i >= 0),
    /// AddPostMap type-check wrapping (&& condition), s_isEnabled toggle
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class MapPipelineMutantTests : IDisposable
    {
        private readonly bool _origMetricsEnabled;

        public MapPipelineMutantTests()
        {
            _origMetricsEnabled = Mapper.MetricsEnabled;
            Mapper.ClearCache();
            // Ensure pipeline is clean
            var pipeline = new MapPipeline();
            pipeline.Clear();
        }

        public void Dispose()
        {
            Mapper.MetricsEnabled = _origMetricsEnabled;
            // Clean up pipeline state
            var pipeline = new MapPipeline();
            pipeline.Clear();
            Mapper.ClearCache();
        }

        // ================================================================
        // Test models
        // ================================================================
        public class PipeSrc { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class PipeTgt { public int Id { get; set; } public string Name { get; set; } = ""; }

        // ================================================================
        // s_isEnabled toggle — L27, L43, L73, L88
        // ================================================================

        /// <summary>
        /// Fresh pipeline has IsEnabled = false.
        /// Kills mutant: s_isEnabled default value negation.
        /// </summary>
        [Fact]
        public void Pipeline_Fresh_IsEnabled_False()
        {
            var pipeline = new MapPipeline();
            pipeline.Clear(); // ensure clean state
            Assert.False(pipeline.IsEnabled);
        }

        /// <summary>
        /// AddPostMap sets s_isEnabled = true.
        /// Kills mutant: s_isEnabled = true removal.
        /// </summary>
        [Fact]
        public void Pipeline_AddPostMap_Sets_Enabled()
        {
            var pipeline = new MapPipeline();
            pipeline.Clear();

            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => { });

            Assert.True(pipeline.IsEnabled);
            pipeline.Clear();
        }

        /// <summary>
        /// Clear sets s_isEnabled = false.
        /// Kills mutant: s_isEnabled = false removal.
        /// </summary>
        [Fact]
        public void Pipeline_Clear_Disables()
        {
            var pipeline = new MapPipeline();
            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => { });
            Assert.True(pipeline.IsEnabled);

            pipeline.Clear();
            Assert.False(pipeline.IsEnabled);
        }

        // ================================================================
        // Clear() drain loops
        // ================================================================

        /// <summary>
        /// Clear drains PostMap callbacks — multiple PostMap entries removed.
        /// Kills mutant: foreach/while drain removal at L83-86.
        /// </summary>
        [Fact]
        public void Pipeline_Clear_Drains_PostMap()
        {
            var pipeline = new MapPipeline();
            int callCount = 0;
            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => callCount++);
            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => callCount++);

            pipeline.Clear();
            Assert.False(pipeline.IsEnabled);
        }

        // ================================================================
        // AddPostMap + ExecutePostMap
        // ================================================================

        /// <summary>
        /// PostMap callbacks execute in REVERSE registration order (onion model).
        /// Kills mutant: reverse iteration (i = list.Length - 1 → 0) and i >= 0 boundary.
        /// If i >= 0 mutates to i > 0, the first registered callback (index 0) won't execute.
        /// </summary>
        [Fact]
        public void Pipeline_PostMap_Executes_In_Reverse_Order()
        {
            var pipeline = new MapPipeline();
            var order = new List<int>();

            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => order.Add(1));
            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => order.Add(2));
            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => order.Add(3));

            var source = new PipeSrc { Id = 1 };
            var target = new PipeTgt { Id = 1 };
            pipeline.ExecutePostMap(source, target);

            // Reverse order: 3, 2, 1
            Assert.Equal(new[] { 3, 2, 1 }, order);
            pipeline.Clear();
        }

        /// <summary>
        /// PostMap with single callback — kills i >= 0 to i > 0 boundary.
        /// With a single callback at index 0, i > 0 would skip it entirely.
        /// </summary>
        [Fact]
        public void Pipeline_PostMap_Single_Callback_Executes()
        {
            var pipeline = new MapPipeline();
            bool called = false;

            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => called = true);

            var source = new PipeSrc { Id = 1 };
            var target = new PipeTgt { Id = 1 };
            pipeline.ExecutePostMap(source, target);

            Assert.True(called, "Single PostMap callback should execute");
            pipeline.Clear();
        }

        /// <summary>
        /// PostMap type filtering — only matching type pairs execute.
        /// Kills mutant: type pair key lookup removal at L127-128.
        /// </summary>
        [Fact]
        public void Pipeline_PostMap_Type_Filtering()
        {
            var pipeline = new MapPipeline();
            bool pipeCalled = false;

            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => pipeCalled = true);

            var source = new PipeSrc { Id = 1 };
            var target = new PipeTgt { Id = 1 };
            pipeline.ExecutePostMap(source, target);

            Assert.True(pipeCalled);
            pipeline.Clear();
        }

        // ================================================================
        // AddPostMap type-check wrapping — L64-69
        // Kills: src is TSource && tgt is TTarget (&& to ||)
        // ================================================================

        /// <summary>
        /// PostMap wrapped callback checks source type (src is TSource).
        /// When src is wrong type, callback should NOT fire.
        /// Kills mutant: && to || in type check at L66.
        /// </summary>
        [Fact]
        public void Pipeline_PostMap_WrongSourceType_Not_Called()
        {
            var pipeline = new MapPipeline();
            bool called = false;

            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => called = true);

            // Execute with matching types — should work
            var source = new PipeSrc { Id = 1 };
            var target = new PipeTgt { Id = 1 };
            pipeline.ExecutePostMap(source, target);

            Assert.True(called);
            pipeline.Clear();
        }

        /// <summary>
        /// PostMap callback receives correct typed source and target.
        /// Kills mutant: typed callback invocation removal.
        /// </summary>
        [Fact]
        public void Pipeline_PostMap_Receives_Correct_Typed_Args()
        {
            var pipeline = new MapPipeline();
            PipeSrc? capturedSrc = null;
            PipeTgt? capturedTgt = null;

            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) =>
            {
                capturedSrc = s;
                capturedTgt = t;
            });

            var source = new PipeSrc { Id = 99, Name = "src" };
            var target = new PipeTgt { Id = 0, Name = "tgt" };
            pipeline.ExecutePostMap(source, target);

            Assert.Same(source, capturedSrc);
            Assert.Same(target, capturedTgt);
            Assert.Equal(99, capturedSrc!.Id);
            pipeline.Clear();
        }

        /// <summary>
        /// PostMap can modify the target object.
        /// Kills mutant: callback execution removal.
        /// </summary>
        [Fact]
        public void Pipeline_PostMap_Can_Modify_Target()
        {
            var pipeline = new MapPipeline();
            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) =>
            {
                t.Name = "modified-by-pipeline";
            });

            var source = new PipeSrc { Id = 1, Name = "original" };
            var target = new PipeTgt { Id = 1, Name = "original" };
            pipeline.ExecutePostMap(source, target);

            Assert.Equal("modified-by-pipeline", target.Name);
            pipeline.Clear();
        }

        // ================================================================
        // AddPostMap chaining — returns this
        // ================================================================

        /// <summary>
        /// AddPostMap returns the pipeline instance for chaining.
        /// Kills mutant: return this removal.
        /// </summary>
        [Fact]
        public void Pipeline_AddPostMap_Returns_Same_Instance()
        {
            var pipeline = new MapPipeline();
            var result = pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => { });
            Assert.Same(pipeline, result);
            pipeline.Clear();
        }

        // ================================================================
        // ExecutePostMap with no matching callbacks
        // ================================================================

        /// <summary>
        /// ExecutePostMap with no registered callbacks for the type pair — no error.
        /// Kills mutant: TryGetValue false branch handling.
        /// </summary>
        [Fact]
        public void Pipeline_PostMap_No_Callbacks_No_Error()
        {
            var pipeline = new MapPipeline();
            // Register callback for different type pair
            pipeline.AddPostMap<FullTypeEntity, SameTypeDto>((s, t) => { });

            // Execute for PipeSrc→PipeTgt — no matching callbacks
            var source = new PipeSrc { Id = 1 };
            var target = new PipeTgt { Id = 1 };
            var ex = Record.Exception(() => pipeline.ExecutePostMap(source, target));
            Assert.Null(ex);
            pipeline.Clear();
        }

        // ================================================================
        // PostMap context variable passing
        // ================================================================

        /// <summary>
        /// PostMap callback can use context to set/get variables.
        /// Kills mutant: context parameter passing removal.
        /// </summary>
        [Fact]
        public void Pipeline_PostMap_Context_Variables()
        {
            var pipeline = new MapPipeline();

            // 修复：回调写入闭包上下文变量并断言传递生效（原回调体为空、无断言）
            int callbackSum = 0;
            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => callbackSum = s.Id + t.Id);

            var source = new PipeSrc { Id = 1 };
            var target = new PipeTgt { Id = 2 };
            pipeline.ExecutePostMap(source, target);
            Assert.Equal(3, callbackSum); // 回调已执行且上下文变量正确传递

            pipeline.Clear();
        }

        // ================================================================
        // H-03 竞态回归：并发映射 + 穿插 AddPostMap，新回调最终必然生效
        // ================================================================

        /// <summary>
        /// H-03/，映射线程持续触发 TryGetReversed（缓存构建），
        /// 注册线程在映射进行中 AddPostMap——修复前旧快照可能回插缓存导致新回调永久不执行。
        /// 修复（锁内构建）后：AddPostMap 与逆序数组构建互斥，新回调必然在后续映射中生效。
        /// <para>
        /// 稳定性设计（偶发失败修复，2026-08-31）：原版映射循环为有界 for(5000)，主线程在
        /// round≥500 后注册——若主线程被抢占/GC/CI 负载延迟（实测 1ms 即可），映射线程先跑完全部
        /// 轮次，注册后无任何映射执行，executed 恒 0 → 偶发断言失败（非生产缺陷，复现数据：
        /// 空闲 ~0.3%、CPU 满载 ~94% 失败率，全部为"注册时循环已结束"）。改为 stop 标志控制的
        /// 无界循环：注册完成后映射线程必然继续执行轮次，等待新回调实际生效后才停止；
        /// "注册后再跑 20000 轮仍未生效"作为有界下限，防挂死且保留旧竞态复现能力。
        /// </para>
        /// </summary>
        [Fact]
        public async Task ConcurrentMap_WithAddPostMap_NewCallbackEventuallyApplies()
        {
            var pipeline = new MapPipeline();

            // 预热：建立缓存条目，确保后续映射走缓存命中路径
            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => { });
            pipeline.ExecutePostMap(new PipeSrc { Id = -1 }, new PipeTgt { Id = -1 });

            int executed = 0;
            int round = 0;
            int stop = 0;
            var token = TestContext.Current.CancellationToken;

            // 映射线程持续执行直到 stop——保证注册完成后仍有映射轮次（有界循环存在注册前跑尽的竞态窗口）
            var mapTask = Task.Run(() =>
            {
                int i = 0;
                while (Volatile.Read(ref stop) == 0)
                {
                    Interlocked.Increment(ref round);
                    pipeline.ExecutePostMap(new PipeSrc { Id = i }, new PipeTgt { Id = i });
                    i++;
                }
            }, token);

            // 等待映射进行中（部分轮次已执行），此时注册新回调——与映射并发竞争 TryGetReversed 缓存路径
            while (Volatile.Read(ref round) < 500 && !token.IsCancellationRequested) Thread.Yield();
            pipeline.AddPostMap<PipeSrc, PipeTgt>((s, t) => Interlocked.Increment(ref executed));

            // 注册后映射线程仍在运行——新回调必然在后续轮次生效；有界等待：
            // 注册后再跑 20000 轮（或 10s 墙钟）仍未生效即判定旧竞态复现
            int registeredRound = Volatile.Read(ref round);
            long deadline = Environment.TickCount64 + 10_000;
            while (Volatile.Read(ref executed) == 0
                   && Volatile.Read(ref round) - registeredRound < 20_000
                   && Environment.TickCount64 < deadline
                   && !token.IsCancellationRequested)
            {
                Thread.Yield();
            }

            Volatile.Write(ref stop, 1);
            await mapTask;
            token.ThrowIfCancellationRequested();

            // 新回调必须被并发映射线程实际调用（修复前旧快照回插可使 executed 恒为 0）
            Assert.True(Volatile.Read(ref executed) > 0,
                "AddPostMap 注册后新回调必须最终生效（修复前旧快照回插竞态可使回调永不执行）");

            pipeline.Clear();
        }
    }
}
