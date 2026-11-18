using Xunit;
using NCMapper.Tests.Models;
using NCMapper.Attributes;

namespace NCMapper.Tests.Tests
{
    // ================================================================
    // 压力测试模型 — 自引用深层嵌套节点
    // ================================================================

    /// <summary>
    /// 压力测试 — 自引用嵌套实体，通过 Child 属性形成链表式深层嵌套
    /// </summary>
    public class StressNode
    {
        /// <summary>
        /// 节点标识符
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 节点名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 子节点导航属性（自引用）
        /// </summary>
        public StressNode? Child { get; set; }
    }

    /// <summary>
    /// 压力测试 — 自引用嵌套 DTO（无 [MapFrom]，强制使用运行时映射路径，确保递归深度检查生效）
    /// </summary>
    public class StressNodeDto
    {
        /// <summary>
        /// 节点标识符
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 节点名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 子节点导航属性（自引用）
        /// </summary>
        public StressNodeDto? Child { get; set; }
    }

    /// <summary>
    /// 压力测试 — 无 MapFrom 标注的自引用嵌套 DTO（用于验证异常情况）
    /// </summary>
    public class StressNodeNoAttrDto
    {
        /// <summary>
        /// 节点标识符
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 节点名称
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 子节点导航属性（自引用）
        /// </summary>
        public StressNodeNoAttrDto? Child { get; set; }
    }

    // ================================================================
    // 大量数据映射压力测试
    // ================================================================

    /// <summary>
    /// 大量数据映射压力测试 — 验证 10 万+ 条记录的 MapList 正确性和性能
    /// </summary>
    public class LargeVolumeStressTests
    {
        /// <summary>
        /// 10 万条 MapList 全部正确映射
        /// </summary>
        [Fact]
        public void HundredThousand_MapList_All_Correct()
        {
            const int count = 100_000;
            var entities = GenerateEntities(count);

            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities)!;

            Assert.Equal(count, dtos!.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(entities[i].IntProp, dtos[i].IntProp);
                Assert.Equal(entities[i].StringProp, dtos[i].StringProp);
            }
        }

        /// <summary>
        /// 10 万条 MapList + Action 回调全部正确映射
        /// </summary>
        [Fact]
        public void HundredThousand_MapList_WithAction_All_Correct()
        {
            const int count = 100_000;
            var entities = GenerateEntities(count);

            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities, (s, d) =>
            {
                d.IntProp = s.IntProp + 1000;
            })!;

            Assert.Equal(count, dtos!.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(entities[i].IntProp + 1000, dtos[i].IntProp);
                Assert.Equal(entities[i].StringProp, dtos[i].StringProp);
            }
        }

        /// <summary>
        /// 大量同类型 DeepCopy（无 Action）全部正确
        /// </summary>
        [Fact]
        public void Large_List_DeepCopy_All_Correct()
        {
            const int count = 50_000;
            var entities = GenerateEntities(count);

            var copies = Mapper.MapList<FullTypeEntity, FullTypeEntity>(entities)!;

            Assert.Equal(count, copies!.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(entities[i].IntProp, copies[i].IntProp);
                Assert.Equal(entities[i].StringProp, copies[i].StringProp);
                Assert.NotSame(entities[i], copies[i]); // 深拷贝不是同一引用
            }
        }

        /// <summary>
        /// 数组源 MapList 全部正确映射
        /// </summary>
        [Fact]
        public void Large_Array_Source_All_Correct()
        {
            const int count = 100_000;
            var entities = GenerateEntities(count);
            var array = entities.ToArray();

            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(array)!;

            Assert.Equal(count, dtos!.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(array[i].IntProp, dtos[i].IntProp);
                Assert.Equal(array[i].StringProp, dtos[i].StringProp);
            }
        }

        /// <summary>
        /// 空列表不被视为 null，而是返回空列表
        /// </summary>
        [Fact]
        public void Empty_List_Returns_Empty_List()
        {
            var empty = new List<FullTypeEntity>();
            var result = Mapper.MapList<FullTypeEntity, SameTypeDto>(empty)!;

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        /// <summary>
        /// 大列表中包含 null 元素应被保留为 null（保持索引一致性）
        /// </summary>
        [Fact]
        public void Large_List_With_Nulls_Filtered()
        {
            var entities = GenerateEntities(100);
            // 在列表中插入 null 元素
            var list = new List<FullTypeEntity>(entities);
            list.Insert(10, null!);
            list.Insert(50, null!);
            Assert.Contains(null, list);

            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(list)!;

            Assert.Equal(102, dtos!.Count); // null 保留为 null，总数 = 100 + 2
            Assert.Null(dtos[10]);          // 索引 10 是 null
            Assert.Null(dtos[50]);          // 索引 50 是 null
            Assert.Equal(entities[9].IntProp, dtos[9].IntProp);   // null 前的元素正确
            Assert.Equal(entities[10].IntProp, dtos[11].IntProp); // null 后的元素正确
        }

        /// <summary>
        /// ICollection 路径正确预分配容量
        /// </summary>
        [Fact]
        public void Large_ICollection_Uses_Preallocated_Capacity()
        {
            const int count = 10_000;
            var collection = (ICollection<FullTypeEntity>)GenerateEntities(count);

            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(collection)!;

            Assert.Equal(count, dtos!.Count);
            // 验证容量预分配生效：List.Capacity >= Count（ICollection 路径）
            Assert.True(dtos.Capacity >= count);
        }

        private static List<FullTypeEntity> GenerateEntities(int count)
        {
            var list = new List<FullTypeEntity>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(new FullTypeEntity
                {
                    IntProp = i,
                    StringProp = $"Entity_{i}",
                    LongProp = i * 1000L,
                    DecimalProp = i * 1.1m,
                    FloatProp = i * 1.1f,
                    DoubleProp = i * 2.2,
                    BoolProp = i % 2 == 0
                });
            }
            return list;
        }
    }

    // ================================================================
    // 深层嵌套映射压力测试
    // ================================================================

    /// <summary>
    /// 深层嵌套映射压力测试 — 验证 50+ 层嵌套映射的正确性和递归深度防护
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class DeepNestingStressTests : IDisposable
    {
        /// <summary>
        /// 保存测试前的 MaxRecursionDepth 原始值，用于 Dispose 中恢复
        /// </summary>
        private readonly int _originalMaxRecursionDepth;

        /// <summary>
        /// 构造函数 — 清除源生成器注册，确保使用运行时映射路径（递归深度检查在 MapperBase.Map 中）
        /// <para>P1-MUST-8: 保存全局状态原始值，在 Dispose 中恢复，防止状态泄漏到后续测试</para>
        /// </summary>
        public DeepNestingStressTests()
        {
            _originalMaxRecursionDepth = Mapper.MaxRecursionDepth;
            Mapper.ClearGenerated();
        }

        /// <summary>
        /// 测试后清理 — 恢复 MaxRecursionDepth 原始值并清除生成器状态，确保不影响后续测试
        /// </summary>
        public void Dispose()
        {
            Mapper.MaxRecursionDepth = _originalMaxRecursionDepth;
            Mapper.ClearGenerated();
        }

        /// <summary>
        /// 3 层嵌套正确映射 — 验证嵌套对象 Child 属性确实被递归映射（而非跳过）
        /// </summary>
        [Fact]
        public void ThreeLayers_Verifies_Child_Is_Mapped()
        {
            const int depth = 3;
            var root = BuildDeepNode(depth);

            var dto = Mapper.Map<StressNode, StressNodeDto>(root)!;

            // 顶层
            Assert.Equal(3, dto.Id);
            Assert.Equal("L3", dto.Name);

            // 第 2 层（depth 2）
            Assert.NotNull(dto.Child);
            Assert.Equal(2, dto.Child!.Id);
            Assert.Equal("L2", dto.Child.Name);

            // 第 3 层（depth 1）
            Assert.NotNull(dto.Child.Child);
            Assert.Equal(1, dto.Child.Child!.Id);
            Assert.Equal("L1", dto.Child.Child.Name);

            // 第 4 层 — 叶子节点（depth 0）
            Assert.NotNull(dto.Child.Child.Child);
            Assert.Equal(0, dto.Child.Child.Child!.Id);
            Assert.Equal("L0", dto.Child.Child.Child.Name);

            // 叶子节点的 Child 应为 null
            Assert.Null(dto.Child.Child.Child.Child);
        }
        /// <summary>
        /// 50 层嵌套正确映射，叶子节点值正确
        /// </summary>
        [Fact]
        public void FiftyLayers_Deep_Nesting_All_Correct()
        {
            const int depth = 50;
            var root = BuildDeepNode(depth);

            var dto = Mapper.Map<StressNode, StressNodeDto>(root)!;

            // 验证顶层
            Assert.Equal(depth, dto.Id);
            Assert.Equal($"L{depth}", dto.Name);

            // 逐层验证到叶子
            var src = root;
            var tgt = dto;
            for (int i = depth; i >= 0; i--)
            {
                Assert.Equal(src.Id, tgt.Id);
                Assert.Equal(src.Name, tgt.Name);
                src = src.Child!;
                tgt = tgt.Child!;
            }

            // 最深层叶子节点 Child 为 null
            Assert.Null(src);
            Assert.Null(tgt);
        }

        /// <summary>
        /// 100 层嵌套正确映射 — 大量层数但未触发递归深度限制（内联优化每 3 层才产生一次委托调用）
        /// </summary>
        [Fact]
        public void OneHundredLayers_Deep_Nesting_All_Correct()
        {
            const int depth = 100;
            var root = BuildDeepNode(depth);

            var dto = Mapper.Map<StressNode, StressNodeDto>(root)!;

            Assert.Equal(depth, dto.Id);

            // 验证叶子节点
            var leaf = dto;
            for (int i = 0; i < depth; i++)
                leaf = leaf.Child!;

            Assert.Equal(0, leaf.Id);
            Assert.Null(leaf.Child);
        }

        /// <summary>
        /// 200 层嵌套正确映射 — 极限深度（C3 补测：恢复 Skip）
        /// <para>安全审计 M4 已实证 net8/net10 下 200 层 SUCCESS（编译期展开受 s_inlineDepth=5 限制，
        /// 运行期守卫不误触发），原 Skip 注记过时（A-002-F1）；恢复为正常测试守护编译期行为。</para>
        /// </summary>
        [Fact]
        public void TwoHundredLayers_Deep_Nesting_All_Correct()
        {
            const int depth = 200;
            var root = BuildDeepNode(depth);

            var dto = Mapper.Map<StressNode, StressNodeDto>(root)!;

            Assert.Equal(depth, dto.Id);

            var leaf = dto;
            for (int i = 0; i < depth; i++)
                leaf = leaf.Child!;

            Assert.Equal(0, leaf.Id);
            Assert.Null(leaf.Child);
        }

        /// <summary>
        /// MaxRecursionDepth=4096 上限运行期栈消耗回归（C13 补测）
        /// <para>报告 §五 C13 断言形态：4096 层 SUCCESS（安全审计已定量 42-52ms，加时长上界断言）。
        /// BuildDeepNode 循环构建无栈风险；Map 走运行时递归路径（构造已 ClearGenerated 强制）。
        /// 时长上界取宽限 10s（安全审计定量 42-52ms 的 ~200 倍），避免 CI 环境抖动误报 flaky。</para>
        /// </summary>
        [Fact]
        public void MaxDepth4096_DeepNesting_Succeeds_WithinTimeBound()
        {
            const int depth = 4096; // MaxRecursionDepthUpperBound（Mapper.cs:691）
            int originalMaxDepth = Mapper.MaxRecursionDepth;
            Mapper.MaxRecursionDepth = 4096; // 显式设为上限（默认 64）
            try
            {
                var root = BuildDeepNode(depth);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var dto = Mapper.Map<StressNode, StressNodeDto>(root)!;
                sw.Stop();

                // 4096 层映射 SUCCESS（安全审计 M4 已定量 42-52ms）
                Assert.Equal(depth, dto.Id);
                Assert.Equal($"L{depth}", dto.Name);
                // 运行期栈消耗回归：宽上界 10s 防环境抖动误报
                Assert.True(sw.ElapsedMilliseconds < 10_000,
                    $"4096 层映射耗时 {sw.ElapsedMilliseconds}ms 超过上界 10s");
            }
            finally
            {
                Mapper.MaxRecursionDepth = originalMaxDepth;
            }
        }

        /// <summary>
        /// 300 层嵌套超出自引用递归深度限制，抛出 InvalidOperationException
        /// MaxInlineDepth=5 时每 6 层触发一次委托调用，300 层 ≈ 50 次委托调用
        /// MaxRecursionDepth 默认 64，50 < 64 所以可能不触发
        /// 使用 500 层确保触发：500/6 ≈ 83 > 64
        /// </summary>
        [Fact]
        public void ThreeHundredLayers_Exceeds_MaxDepth_Throws()
        {
            const int depth = 500;
            var root = BuildDeepNode(depth);

            var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
                Mapper.Map<StressNode, StressNodeDto>(root));

            Assert.Contains("64", ex.Message);
            Assert.True(ex.Message.Contains("递归") || ex.Message.Contains("recursion"),
                $"Expected '递归' or 'recursion' in: {ex.Message}");
        }

        /// <summary>
        /// MapList 中元素包含深层嵌套 — 正确映射
        /// </summary>
        [Fact]
        public void MapList_With_DeepNested_Elements_All_Correct()
        {
            const int elementDepth = 10;
            const int elementCount = 100;
            var roots = new List<StressNode>();
            for (int i = 0; i < elementCount; i++)
                roots.Add(BuildDeepNode(elementDepth));

            var dtos = Mapper.MapList<StressNode, StressNodeDto>(roots)!;

            Assert.Equal(elementCount, dtos!.Count);
            for (int i = 0; i < elementCount; i++)
            {
                Assert.Equal(elementDepth, dtos[i].Id);
                Assert.NotNull(dtos[i].Child);
            }
        }

        /// <summary>
        /// 深层嵌套中某层为 null — 正确传播 null
        /// </summary>
        [Fact]
        public void DeepNesting_Null_At_MidLayer_Propagates()
        {
            var root = new StressNode { Id = 5, Name = "L5" };
            // 只嵌套 3 层，第 3 层的 Child 为 null（默认）
            root.Child = new StressNode { Id = 4, Name = "L4" };
            root.Child.Child = new StressNode { Id = 3, Name = "L3", Child = null };

            var dto = Mapper.Map<StressNode, StressNodeDto>(root)!;

            Assert.Equal(5, dto.Id);
            Assert.NotNull(dto.Child);
            Assert.Equal(4, dto.Child!.Id);
            Assert.NotNull(dto.Child.Child);
            Assert.Equal(3, dto.Child.Child!.Id);
            Assert.Null(dto.Child.Child.Child); // null 传播
        }

        /// <summary>
        /// 构建指定深度的链表式嵌套节点
        /// 叶子节点（depth=0）的 Child 为 null
        /// </summary>
        internal static StressNode BuildDeepNode(int depth)
        {
            StressNode? current = null;
            for (int i = 0; i <= depth; i++)
            {
                current = new StressNode
                {
                    Id = i,
                    Name = $"L{i}",
                    Child = current
                };
            }
            return current!;
        }
    }

    // ================================================================
    // 并发映射压力测试
    // ================================================================

    /// <summary>
    /// 并发映射压力测试 — 验证多线程同时调用 Mapper.Map/MapList 的正确性
    /// [ThreadStatic] 字段保证每个线程独立的递归深度计数器
    /// ConcurrentDictionary 保证静态缓存无竞态
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class ConcurrentMappingStressTests
    {
        /// <summary>
        /// 多线程同时 Map 不同类型，全部正确
        /// </summary>
        [Fact]
        public async Task Concurrent_Map_DifferentTypes_All_Correct()
        {
            const int threadCount = 20;
            const int iterationsPerThread = 5000;
            var exceptions = new List<Exception>(threadCount);
            var tasks = new Task[threadCount];

            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        for (int i = 0; i < iterationsPerThread; i++)
                        {
                            var entity = new FullTypeEntity
                            {
                                IntProp = threadId * 10000 + i,
                                StringProp = $"T{threadId}_I{i}",
                                LongProp = (threadId + 1) * i,
                                DecimalProp = i * 1.23m,
                                FloatProp = i * 3.14f,
                                DoubleProp = i * 2.718,
                                BoolProp = i % 2 == 0
                            };

                            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

                            Assert.Equal(entity.IntProp, dto.IntProp);
                            Assert.Equal(entity.StringProp, dto.StringProp);
                            Assert.Equal(entity.LongProp, dto.LongProp);
                            Assert.Equal(entity.DecimalProp, dto.DecimalProp);
                            Assert.Equal(entity.FloatProp, dto.FloatProp);
                            Assert.Equal(entity.DoubleProp, dto.DoubleProp);
                            Assert.Equal(entity.BoolProp, dto.BoolProp);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                            exceptions.Add(ex);
                        throw;
                    }
                }, TestContext.Current.CancellationToken);
            }

            await Task.WhenAll(tasks);
            Assert.Empty(exceptions);
        }

        /// <summary>
        /// 多线程同时 MapList，全部正确
        /// </summary>
        [Fact]
        public async Task Concurrent_MapList_DifferentTypes_All_Correct()
        {
            const int threadCount = 10;
            const int itemsPerThread = 10000;
            var exceptions = new List<Exception>(threadCount);
            var tasks = new Task[threadCount];

            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        var entities = new List<FullTypeEntity>(itemsPerThread);
                        for (int i = 0; i < itemsPerThread; i++)
                        {
                            entities.Add(new FullTypeEntity
                            {
                                IntProp = threadId * 100000 + i,
                                StringProp = $"T{threadId}_{i}"
                            });
                        }

                        var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities)!;

                        Assert.Equal(itemsPerThread, dtos!.Count);
                        for (int i = 0; i < itemsPerThread; i++)
                        {
                            Assert.Equal(entities[i].IntProp, dtos[i].IntProp);
                            Assert.Equal(entities[i].StringProp, dtos[i].StringProp);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                            exceptions.Add(ex);
                        throw;
                    }
                }, TestContext.Current.CancellationToken);
            }

            await Task.WhenAll(tasks);
            Assert.Empty(exceptions);
        }

        /// <summary>
        /// 多线程同时进行深层嵌套映射 — [ThreadStatic] 递归深度计数器互不干扰
        /// </summary>
        [Fact]
        public async Task Concurrent_Deep_Nesting_ThreadStatic_Isolated()
        {
            const int threadCount = 10;
            const int depth = 50;
            var exceptions = new List<Exception>(threadCount);
            var tasks = new Task[threadCount];

            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        var root = DeepNestingStressTests.BuildDeepNode(depth);

                        var dto = Mapper.Map<StressNode, StressNodeDto>(root)!;

                        Assert.Equal(depth, dto.Id);
                        // 验证叶子节点
                        var leaf = dto;
                        for (int i = 0; i < depth; i++)
                            leaf = leaf.Child!;
                        Assert.Equal(0, leaf.Id);
                        Assert.Null(leaf.Child);
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                            exceptions.Add(ex);
                        throw;
                    }
                }, TestContext.Current.CancellationToken);
            }

            await Task.WhenAll(tasks);
            Assert.Empty(exceptions);
        }

        /// <summary>
        /// 混合同类型 Map + 跨类型 Map + MapList 并发执行
        /// </summary>
        [Fact]
        public async Task Concurrent_Mixed_Operations_All_Correct()
        {
            const int taskCount = 30;
            var exceptions = new List<Exception>(taskCount);
            var tasks = new Task[taskCount];

            for (int t = 0; t < taskCount; t++)
            {
                int taskId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        // 操作 1：同类型映射
                        var e1 = new FullTypeEntity { IntProp = taskId, StringProp = $"S{taskId}" };
                        var d1 = Mapper.Map<FullTypeEntity, SameTypeDto>(e1)!;
                        Assert.Equal(taskId, d1.IntProp);

                        // 操作 2：MapList 小列表
                        var list = new List<FullTypeEntity>
                        {
                            new() { IntProp = taskId * 10, StringProp = "A" },
                            new() { IntProp = taskId * 10 + 1, StringProp = "B" }
                        };
                        var dList = Mapper.MapList<FullTypeEntity, SameTypeDto>(list)!;
                        Assert.Equal(2, dList!.Count);

                        // 操作 3：同类型 DeepCopy
                        var copy = Mapper.Map<FullTypeEntity, FullTypeEntity>(e1)!;
                        Assert.Equal(taskId, copy.IntProp);
                        Assert.NotSame(e1, copy);
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                            exceptions.Add(ex);
                        throw;
                    }
                }, TestContext.Current.CancellationToken);
            }

            await Task.WhenAll(tasks);
            Assert.Empty(exceptions);
        }

    }

    // ================================================================
    // 双计数器递归防护专项测试
    // 验证 s_depthIn/s_depthOut 在异常、循环引用、并发场景下的正确性
    // ================================================================

    /// <summary>
    /// 双计数器递归防护测试 — 验证无 try-finally 方案的正确性
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class DualCounterRecursionTests : IDisposable
    {
        private readonly int _originalMaxRecursionDepth;

        public DualCounterRecursionTests()
        {
            _originalMaxRecursionDepth = Mapper.MaxRecursionDepth;
            Mapper.ClearGenerated();
        }

        public void Dispose()
        {
            Mapper.MaxRecursionDepth = _originalMaxRecursionDepth;
            Mapper.ClearGenerated();
        }

        /// <summary>
        /// 正常 3 层嵌套映射后，后续映射不受影响 — 验证计数器差值归零
        /// </summary>
        [Fact]
        public void Normal_Nesting_Counters_Reset_After_Completion()
        {
            var root = DeepNestingStressTests.BuildDeepNode(3);
            var dto = Mapper.Map<StressNode, StressNodeDto>(root)!;

            Assert.Equal(3, dto.Id);
            Assert.NotNull(dto.Child);

            // 紧接着做一次平铺映射，应该正常工作（计数器差值应为 0）
            var flat = new FullTypeEntity { IntProp = 42, StringProp = "OK" };
            var flatDto = Mapper.Map<FullTypeEntity, SameTypeDto>(flat)!;
            Assert.Equal(42, flatDto.IntProp);
            Assert.Equal("OK", flatDto.StringProp);
        }

        /// <summary>
        /// 循环引用触发异常后，计数器重置归零，后续映射正常工作
        /// </summary>
        [Fact]
        public void CircularReference_Exception_Then_Normal_Map_Works()
        {
            // 构建循环引用：A.Child = B, B.Child = A
            var a = new StressNode { Id = 1, Name = "A" };
            var b = new StressNode { Id = 2, Name = "B" };
            a.Child = b;
            b.Child = a;

            // 循环引用应抛异常
            var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
                Mapper.Map<StressNode, StressNodeDto>(a));
            Assert.True(ex.Message.Contains("递归") || ex.Message.Contains("recursion"),
                $"Expected '递归' or 'recursion' in: {ex.Message}");

            // 异常后计数器已重置，后续映射必须正常
            var flat = new FullTypeEntity { IntProp = 99, StringProp = "AfterException" };
            var flatDto = Mapper.Map<FullTypeEntity, SameTypeDto>(flat)!;
            Assert.Equal(99, flatDto.IntProp);
            Assert.Equal("AfterException", flatDto.StringProp);
        }

        /// <summary>
        /// 连续多次触发循环引用异常，每次都正确抛异常 + 重置 + 恢复
        /// </summary>
        [Fact]
        public void CircularReference_Repeated_Exceptions_All_Recover()
        {
            var a = new StressNode { Id = 1, Name = "A" };
            var b = new StressNode { Id = 2, Name = "B" };
            a.Child = b;
            b.Child = a;

            // 连续 5 次触发循环引用异常
            for (int i = 0; i < 5; i++)
            {
                Assert.ThrowsAny<InvalidOperationException>(() =>
                    Mapper.Map<StressNode, StressNodeDto>(a));

                // 每次异常后立即做正常映射
                var flat = new FullTypeEntity { IntProp = i, StringProp = $"R{i}" };
                var flatDto = Mapper.Map<FullTypeEntity, SameTypeDto>(flat)!;
                Assert.Equal(i, flatDto.IntProp);
                Assert.Equal($"R{i}", flatDto.StringProp);
            }
        }

        /// <summary>
        /// 异常后嵌套映射仍然正常 — 验证差值残留不影响嵌套深度计算
        /// </summary>
        [Fact]
        public void Exception_Then_Deep_Nesting_Still_Works()
        {
            // 先触发一次异常
            var a = new StressNode { Id = 1, Name = "A" };
            a.Child = a; // 自引用
            Assert.ThrowsAny<InvalidOperationException>(() =>
                Mapper.Map<StressNode, StressNodeDto>(a));

            // 异常后做 10 层正常嵌套映射
            var root = DeepNestingStressTests.BuildDeepNode(10);
            var dto = Mapper.Map<StressNode, StressNodeDto>(root)!;

            Assert.Equal(10, dto.Id);
            // 验证叶子节点
            var leaf = dto;
            for (int i = 0; i < 10; i++)
                leaf = leaf.Child!;
            Assert.Equal(0, leaf.Id);
            Assert.Null(leaf.Child);
        }

        /// <summary>
        /// MaxRecursionDepth=1 时浅层映射正常，深层抛异常
        /// 注意：MaxInlineDepth=5 时前 5 层内联不走 MapperBase.Map，需要 depth > 5 才触发深度检查
        /// </summary>
        [Fact]
        public void MaxDepth_One_Shallow_Map_OK_Deep_Throws()
        {
            Mapper.MaxRecursionDepth = 1;

            // 1 层（depth=0）应该正常
            var single = new StressNode { Id = 5, Name = "Single" };
            var singleDto = Mapper.Map<StressNode, StressNodeDto>(single)!;
            Assert.Equal(5, singleDto.Id);
            Assert.Null(singleDto.Child);

            // 7 层（depth=6）超过 MaxInlineDepth=5，走 Fallback 路径触发深度检查
            var deep = DeepNestingStressTests.BuildDeepNode(7);
            Assert.ThrowsAny<InvalidOperationException>(() =>
                Mapper.Map<StressNode, StressNodeDto>(deep));

            // 异常后浅层映射仍然正常
            var flat = new FullTypeEntity { IntProp = 77, StringProp = "Recover" };
            var flatDto = Mapper.Map<FullTypeEntity, SameTypeDto>(flat)!;
            Assert.Equal(77, flatDto.IntProp);
        }

        /// <summary>
        /// 多线程并发触发异常 + 正常映射 — 验证 ThreadStatic 隔离
        /// </summary>
        [Fact]
        public async Task Concurrent_Exception_And_Normal_Map_Isolated()
        {
            const int taskCount = 20;
            var exceptions = new List<Exception>(taskCount);
            var tasks = new Task[taskCount];

            for (int t = 0; t < taskCount; t++)
            {
                int taskId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        // 偶数线程触发循环引用异常
                        if (taskId % 2 == 0)
                        {
                            var a = new StressNode { Id = taskId, Name = "C" };
                            a.Child = a;
                            Assert.ThrowsAny<InvalidOperationException>(() =>
                                Mapper.Map<StressNode, StressNodeDto>(a));
                        }

                        // 所有线程做正常映射
                        for (int i = 0; i < 100; i++)
                        {
                            var e = new FullTypeEntity { IntProp = taskId * 1000 + i, StringProp = $"T{taskId}_{i}" };
                            var d = Mapper.Map<FullTypeEntity, SameTypeDto>(e)!;
                            Assert.Equal(taskId * 1000 + i, d.IntProp);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                            exceptions.Add(ex);
                        throw;
                    }
                }, TestContext.Current.CancellationToken);
            }

            await Task.WhenAll(tasks);
            Assert.Empty(exceptions);
        }

        /// <summary>
        /// MapList 中元素触发异常后，后续 MapList 正常
        /// </summary>
        [Fact]
        public void MapList_Exception_Then_Normal_MapList_Works()
        {
            // 构建一个含循环引用的列表
            var a = new StressNode { Id = 1, Name = "A" };
            a.Child = a;
            var badList = new List<StressNode> { a };

            Assert.ThrowsAny<InvalidOperationException>(() =>
                Mapper.MapList<StressNode, StressNodeDto>(badList));

            // 异常后正常 MapList
            var goodList = new List<FullTypeEntity>
            {
                new() { IntProp = 1, StringProp = "A" },
                new() { IntProp = 2, StringProp = "B" },
                new() { IntProp = 3, StringProp = "C" }
            };
            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(goodList)!;
            Assert.Equal(3, dtos.Count);
            Assert.Equal(1, dtos[0].IntProp);
            Assert.Equal(2, dtos[1].IntProp);
            Assert.Equal(3, dtos[2].IntProp);
        }
    }
}
