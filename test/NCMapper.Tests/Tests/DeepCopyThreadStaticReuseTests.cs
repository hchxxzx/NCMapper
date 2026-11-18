using Xunit;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 方向 I 补充优化测试 — ThreadStatic Dictionary + DeepCopyContext 复用
    /// <para>验证顶层 ThreadStatic 复用、重入安全、CycleCache 跨调用隔离、
    /// Generated 路径循环引用检测、RegisterDeepCopyTarget 行为。</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class DeepCopyThreadStaticReuseTests
    {
        // ================================================================
        // 测试模型
        // ================================================================

        /// <summary>
        /// 简单 POCO（无 [MapFrom]，走 Runtime Provider 路径）
        /// </summary>
        public class SimpleNode
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public SimpleNode? Next { get; set; }
        }

        /// <summary>
        /// 含复杂集合的模型（走 ExpressionTree Provider 路径）
        /// </summary>
        public class CollectionContainer
        {
            public int Id { get; set; }
            public List<SimpleNode>? Items { get; set; }
            public Dictionary<string, SimpleNode>? Map { get; set; }
        }

        // ================================================================
        // 1. DeepCopy 重入测试 — action 回调中再次调用 DeepCopy
        // ================================================================

        /// <summary>
        /// 重入安全 — 在 DeepCopy action 回调中调用另一个 DeepCopy，
        /// 验证内层调用不破坏外层的循环引用检测（isTopLevel 逻辑正确）
        /// </summary>
        [Fact]
        public void DeepCopy_Reentrant_InAction_BothSucceed()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();

                var outer = new SimpleNode { Id = 1, Name = "outer" };
                var inner = new SimpleNode { Id = 2, Name = "inner" };
                SimpleNode? innerCopy = null;

                // 外层 DeepCopy 的 action 中触发内层 DeepCopy（重入）
                var outerCopy = Mapper.DeepCopy(outer, (src, tgt) =>
                {
                    // 重入：在外层 DeepCopy 的 action 回调中调用另一个 DeepCopy
                    innerCopy = Mapper.DeepCopy(inner);
                })!;

                // 外层拷贝正确
                Assert.NotSame(outer, outerCopy);
                Assert.Equal(1, outerCopy.Id);
                Assert.Equal("outer", outerCopy.Name);

                // 内层拷贝正确（重入未破坏外层状态）
                Assert.NotNull(innerCopy);
                Assert.NotSame(inner, innerCopy);
                Assert.Equal(2, innerCopy!.Id);
                Assert.Equal("inner", innerCopy.Name);
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        /// <summary>
        /// 重入安全 — 循环引用对象 + action 中重入 DeepCopy，
        /// 验证重入不破坏外层 CycleCache（外层循环引用仍被正确检测）
        /// </summary>
        [Fact]
        public void DeepCopy_Reentrant_WithCircular_OuterCyclePreserved()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();

                // 外层：循环引用对象 A→B→A
                var nodeA = new SimpleNode { Id = 1, Name = "A" };
                var nodeB = new SimpleNode { Id = 2, Name = "B" };
                nodeA.Next = nodeB;
                nodeB.Next = nodeA; // 循环

                var unrelated = new SimpleNode { Id = 99, Name = "unrelated" };
                SimpleNode? unrelatedCopy = null;

                var copyA = Mapper.DeepCopy(nodeA, (src, tgt) =>
                {
                    // 重入：拷贝一个无关对象
                    unrelatedCopy = Mapper.DeepCopy(unrelated);
                })!;

                // 外层循环引用正确：copyA.Next.Next == copyA
                Assert.NotNull(copyA.Next);
                Assert.Same(copyA, copyA.Next!.Next);
                Assert.Equal("A", copyA.Name);
                Assert.Equal("B", copyA.Next.Name);

                // 内层拷贝正确
                Assert.NotNull(unrelatedCopy);
                Assert.Equal(99, unrelatedCopy!.Id);
                Assert.NotSame(unrelated, unrelatedCopy);
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        // ================================================================
        // 2. CycleCache 跨调用隔离 — ThreadStatic Clear 复用正确性
        // ================================================================

        /// <summary>
        /// 跨调用隔离 — 第一次 DeepCopy 有循环引用（CycleCache 被填充），
        /// 第二次 DeepCopy 不同对象时不命中残留缓存
        /// </summary>
        [Fact]
        public void DeepCopy_Sequential_CycleCache_Isolated()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();

                // 第一次：循环引用对象
                var circular = new SimpleNode { Id = 1, Name = "circular" };
                circular.Next = circular; // 自引用

                var copy1 = Mapper.DeepCopy(circular)!;
                Assert.Same(copy1, copy1.Next); // 循环引用正确

                // 第二次：不同对象（Id 相同但引用不同）
                var fresh = new SimpleNode { Id = 1, Name = "fresh" };
                var copy2 = Mapper.DeepCopy(fresh)!;

                // 关键断言：copy2 不应命中第一次的残留缓存
                Assert.NotSame(copy1, copy2);
                Assert.Equal("fresh", copy2.Name);
                Assert.Null(copy2.Next); // fresh.Next 是 null，不应被污染
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        /// <summary>
        /// 跨调用隔离 — 多次连续 DeepCopy 后 CycleCache 不累积
        /// <para>验证 ThreadStatic Dictionary 的 Clear() 在每次顶层退出时正确执行</para>
        /// </summary>
        [Fact]
        public void DeepCopy_ManySequential_NoCacheAccumulation()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();

                // 连续 50 次 DeepCopy 循环引用对象
                for (int i = 0; i < 50; i++)
                {
                    var node = new SimpleNode { Id = i, Name = $"node{i}" };
                    node.Next = node; // 自引用

                    var copy = Mapper.DeepCopy(node)!;
                    Assert.Same(copy, copy.Next); // 每次循环引用都正确
                    Assert.Equal(i, copy.Id);
                    Assert.NotSame(node, copy);
                }

                // 最后一次：无循环引用的对象，验证不受前面 50 次影响
                var plain = new SimpleNode { Id = 999, Name = "plain" };
                var plainCopy = Mapper.DeepCopy(plain)!;
                Assert.Null(plainCopy.Next);
                Assert.Equal(999, plainCopy.Id);
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        // ================================================================
        // 3. Generated 路径循环引用 — RegisterDeepCopyTarget + ctx.CycleCache
        //    手动注册 ctx-aware 委托模拟源生成器行为（测试项目未引用 SourceGen Analyzer）
        // ================================================================

        /// <summary>
        /// 注册模拟源生成器的 ctx-aware DeepCopy 委托 — 模拟 GenerateDeepCopyParameterlessPath 行为：
        /// new T() → RegisterDeepCopyTarget → 递归 DeepCopy(prop, ctx)
        /// </summary>
        private static void RegisterSimulatedGeneratedDeepCopy()
        {
            // 模拟源生成器为 GeneratedCircularNode 生成的代码：
            // var dst = new GeneratedCircularNode();
            // Mapper.RegisterDeepCopyTarget(src, dst, ctx);
            // dst.Id = src.Id;
            // dst.Name = src.Name;
            // dst.Self = Mapper.DeepCopy(src.Self!, ctx);
            Mapper.RegisterGeneratedDeepCopy<GeneratedCircularNode>((src, ctx) =>
            {
                var dst = new GeneratedCircularNode();
                Mapper.RegisterDeepCopyTarget(src, dst, ctx);
                dst.Id = src.Id;
                dst.Name = src.Name;
                dst.Self = Mapper.DeepCopy(src.Self!, ctx);
                return dst;
            });

            // 模拟源生成器为 GeneratedCycleA 生成的代码
            Mapper.RegisterGeneratedDeepCopy<GeneratedCycleA>((src, ctx) =>
            {
                var dst = new GeneratedCycleA();
                Mapper.RegisterDeepCopyTarget(src, dst, ctx);
                dst.Id = src.Id;
                dst.Name = src.Name;
                dst.B = Mapper.DeepCopy(src.B!, ctx);
                return dst;
            });

            // 模拟源生成器为 GeneratedCycleB 生成的代码
            Mapper.RegisterGeneratedDeepCopy<GeneratedCycleB>((src, ctx) =>
            {
                var dst = new GeneratedCycleB();
                Mapper.RegisterDeepCopyTarget(src, dst, ctx);
                dst.Id = src.Id;
                dst.Name = src.Name;
                dst.A = Mapper.DeepCopy(src.A!, ctx);
                return dst;
            });
        }

        /// <summary>
        /// Generated 路径循环引用 — 自引用对象通过 ctx-aware 委托不栈溢出
        /// <para>验证 RegisterDeepCopyTarget 在 new T() 后立即登记 src→dst，
        /// 递归 Mapper.DeepCopy(src.Self, ctx) 命中 CycleCache 打破循环。</para>
        /// </summary>
        [Fact]
        public void GeneratedDeepCopy_SelfReference_CycleBroken()
        {
            RegisterSimulatedGeneratedDeepCopy();
            try
            {
                Assert.True(Mapper.HasGeneratedDeepCopy<GeneratedCircularNode>());

                var node = new GeneratedCircularNode { Id = 42, Name = "self-ref" };
                node.Self = node; // 自引用

                var copy = Mapper.DeepCopy(node)!;

                Assert.NotSame(node, copy);
                Assert.Equal(42, copy.Id);
                Assert.Equal("self-ref", copy.Name);
                // 循环引用正确打破：copy.Self 指向 copy 自身
                Assert.Same(copy, copy.Self);
            }
            finally
            {
                Mapper.RegisterGeneratedDeepCopy<GeneratedCircularNode>((Func<GeneratedCircularNode, DeepCopyContext, GeneratedCircularNode>?)null);
            }
        }

        /// <summary>
        /// Generated 路径循环引用 — A↔B 双向引用通过 ctx-aware 委托不栈溢出
        /// </summary>
        [Fact]
        public void GeneratedDeepCopy_BidirectionalCycle_Broken()
        {
            RegisterSimulatedGeneratedDeepCopy();
            try
            {
                var a = new GeneratedCycleA { Id = 1, Name = "A" };
                var b = new GeneratedCycleB { Id = 2, Name = "B" };
                a.B = b;
                b.A = a; // A→B→A 循环

                var copyA = Mapper.DeepCopy(a)!;

                Assert.NotSame(a, copyA);
                Assert.Equal(1, copyA.Id);
                Assert.NotNull(copyA.B);
                Assert.NotSame(b, copyA.B);
                Assert.Equal(2, copyA.B!.Id);
                // 循环引用正确打破：copyA.B.A == copyA
                Assert.Same(copyA, copyA.B.A);
            }
            finally
            {
                Mapper.RegisterGeneratedDeepCopy<GeneratedCycleA>((Func<GeneratedCycleA, DeepCopyContext, GeneratedCycleA>?)null);
                Mapper.RegisterGeneratedDeepCopy<GeneratedCycleB>((Func<GeneratedCycleB, DeepCopyContext, GeneratedCycleB>?)null);
            }
        }

        /// <summary>
        /// Generated 路径 — 无循环引用时 CycleCache 不影响正常拷贝
        /// </summary>
        [Fact]
        public void GeneratedDeepCopy_NoCycle_NormalCopy()
        {
            RegisterSimulatedGeneratedDeepCopy();
            try
            {
                var node = new GeneratedCircularNode { Id = 7, Name = "no-cycle" };
                // Self 为 null，无循环引用

                var copy = Mapper.DeepCopy(node)!;

                Assert.NotSame(node, copy);
                Assert.Equal(7, copy.Id);
                Assert.Equal("no-cycle", copy.Name);
                Assert.Null(copy.Self);
            }
            finally
            {
                Mapper.RegisterGeneratedDeepCopy<GeneratedCircularNode>((Func<GeneratedCircularNode, DeepCopyContext, GeneratedCircularNode>?)null);
            }
        }

        // ================================================================
        // 4. RegisterDeepCopyTarget 行为验证
        // ================================================================

        /// <summary>
        /// RegisterDeepCopyTarget — 预注册后 DeepCopy 命中缓存返回已登记对象
        /// </summary>
        [Fact]
        public void RegisterDeepCopyTarget_PreRegistered_ReturnsCached()
        {
            var src = new SimpleNode { Id = 1, Name = "src" };
            var dst = new SimpleNode { Id = 1, Name = "dst" };
            var ctx = new DeepCopyContext
            {
                CycleCache = new Dictionary<object, object>(ReferenceEqualityComparer.Instance)
            };

            // 预注册 src → dst
            Mapper.RegisterDeepCopyTarget(src, dst, ctx);

            // 验证 CycleCache 包含映射
            Assert.True(ctx.CycleCache.ContainsKey(src));
            Assert.Same(dst, ctx.CycleCache[src]);
        }

        /// <summary>
        /// RegisterDeepCopyTarget — CycleCache 为 null 时自动懒初始化
        /// </summary>
        [Fact]
        public void RegisterDeepCopyTarget_NullCache_LazyInit()
        {
            var src = new SimpleNode { Id = 1, Name = "src" };
            var dst = new SimpleNode { Id = 1, Name = "dst" };
            var ctx = new DeepCopyContext { CycleCache = null };

            // CycleCache 为 null 时应自动初始化
            Mapper.RegisterDeepCopyTarget(src, dst, ctx);

            Assert.NotNull(ctx.CycleCache);
            Assert.Same(dst, ctx.CycleCache![src]);
        }

        /// <summary>
        /// RegisterDeepCopyTarget — 重复注册同一 src 覆盖旧映射
        /// </summary>
        [Fact]
        public void RegisterDeepCopyTarget_Duplicate_Overwrites()
        {
            var src = new SimpleNode { Id = 1, Name = "src" };
            var dst1 = new SimpleNode { Id = 1, Name = "dst1" };
            var dst2 = new SimpleNode { Id = 1, Name = "dst2" };
            var ctx = new DeepCopyContext
            {
                CycleCache = new Dictionary<object, object>(ReferenceEqualityComparer.Instance)
            };

            Mapper.RegisterDeepCopyTarget(src, dst1, ctx);
            Assert.Same(dst1, ctx.CycleCache![src]);

            // 重复注册覆盖
            Mapper.RegisterDeepCopyTarget(src, dst2, ctx);
            Assert.Same(dst2, ctx.CycleCache[src]);
        }

        // ================================================================
        // 5. Expression 路径复杂集合 DeepCopy
        // ================================================================

        /// <summary>
        /// Expression 路径 — List&lt;ComplexObject&gt; 深拷贝，元素独立
        /// </summary>
        [Fact]
        public void ExpressionDeepCopy_ListOfComplexObjects_Independent()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();

                var container = new CollectionContainer
                {
                    Id = 1,
                    Items = new List<SimpleNode>
                    {
                        new SimpleNode { Id = 10, Name = "item1" },
                        new SimpleNode { Id = 20, Name = "item2" },
                    }
                };

                var copy = Mapper.DeepCopy(container)!;

                Assert.NotSame(container, copy);
                Assert.Equal(1, copy.Id);
                Assert.NotNull(copy.Items);
                Assert.Equal(2, copy.Items!.Count);
                // 集合本身独立
                Assert.NotSame(container.Items, copy.Items);
                // 元素独立
                Assert.NotSame(container.Items[0], copy.Items[0]);
                Assert.NotSame(container.Items[1], copy.Items[1]);
                Assert.Equal(10, copy.Items[0].Id);
                Assert.Equal("item1", copy.Items[0].Name);
                Assert.Equal(20, copy.Items[1].Id);
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        /// <summary>
        /// Expression 路径 — Dictionary&lt;string, ComplexObject&gt; 深拷贝，值独立
        /// </summary>
        [Fact]
        public void ExpressionDeepCopy_DictionaryOfComplexObjects_Independent()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();

                var container = new CollectionContainer
                {
                    Id = 2,
                    Map = new Dictionary<string, SimpleNode>
                    {
                        ["key1"] = new SimpleNode { Id = 100, Name = "val1" },
                        ["key2"] = new SimpleNode { Id = 200, Name = "val2" },
                    }
                };

                var copy = Mapper.DeepCopy(container)!;

                Assert.NotSame(container, copy);
                Assert.NotNull(copy.Map);
                Assert.Equal(2, copy.Map!.Count);
                // Dictionary 本身独立
                Assert.NotSame(container.Map, copy.Map);
                // 值对象独立
                Assert.NotSame(container.Map["key1"], copy.Map["key1"]);
                Assert.Equal(100, copy.Map["key1"].Id);
                Assert.Equal("val1", copy.Map["key1"].Name);
                Assert.Equal(200, copy.Map["key2"].Id);
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        /// <summary>
        /// Expression 路径 — 集合元素含循环引用，DeepCopy 不栈溢出
        /// </summary>
        [Fact]
        public void ExpressionDeepCopy_CollectionWithCircularElements_NoStackOverflow()
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();

                var nodeA = new SimpleNode { Id = 1, Name = "A" };
                var nodeB = new SimpleNode { Id = 2, Name = "B" };
                nodeA.Next = nodeB;
                nodeB.Next = nodeA; // A↔B 循环

                var container = new CollectionContainer
                {
                    Id = 3,
                    Items = new List<SimpleNode> { nodeA, nodeB }
                };

                var copy = Mapper.DeepCopy(container)!;

                Assert.NotNull(copy.Items);
                Assert.Equal(2, copy.Items!.Count);
                // 元素独立
                Assert.NotSame(nodeA, copy.Items[0]);
                Assert.NotSame(nodeB, copy.Items[1]);
                // 循环引用在拷贝中保持：copy[0].Next.Next == copy[0]
                Assert.Same(copy.Items[0], copy.Items[0].Next!.Next);
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        // ================================================================
        // 6. A1-03Map/MapList 回调内 DeepCopy 上下文隔离
        //    缺陷机理：Map 回调期间 ctx.Depth ≥ 1，回调内顶层 DeepCopy 复用 s_cycleCache 且
        //    isTopLevel=false 不 Clear → 残留条目跨回调存活 → 同会话第二次同源 DeepCopy 静默返回旧副本。
        //    修复：顶层入口 Depth>0 时换入独立缓存（CycleCache + Fast 三字段），finally 恢复外层视图。
        // ================================================================

        /// <summary>
        /// 红测主用例（X5，Map 宿主）— Map 回调内两次 DeepCopy 同一自引用实例返回独立副本
        /// <para>缺陷态：第二次命中第一次的残留 CycleCache 条目 → 引用相等（测试红）；</para>
        /// <para>修复态：两次调用各自独立会话 → 引用不等 + 各自环自洽 + 互不影响。</para>
        /// </summary>
        [Fact]
        public void Map回调内DeepCopy_同一实例两次_返回独立副本()
        {
            var payload = new DeepIsoPayload { Value = "P" };
            payload.Next = payload; // 自引用 → Cyclic 分类 → CycleCache 完整检测路径

            var src = new DeepIsoSource { Id = 1, Name = "src" };
            var seen = new List<DeepIsoPayload>();

            var dto = Mapper.Map<DeepIsoSource, DeepIsoDto>(src, (s, d) =>
            {
                seen.Add(Mapper.DeepCopy(payload)!); // 第一次：登记独立缓存
                seen.Add(Mapper.DeepCopy(payload)!); // 第二次：缺陷态命中残留返回旧副本
            });

            Assert.NotNull(dto);
            Assert.Equal(2, seen.Count);
            // 关键断言：两份独立副本（缺陷态 Same → 红）
            Assert.NotSame(seen[0], seen[1]);
            // 各自环自洽（独立会话内循环引用正确）
            Assert.Same(seen[0], seen[0].Next);
            Assert.Same(seen[1], seen[1].Next);
            // 互不影响（副本间无共享状态）
            seen[0].Value = "changed";
            Assert.Equal("P", seen[1].Value);
        }

        /// <summary>
        /// 红测（X5，MapList 宿主）— MapList 单次 Depth++ 覆盖 N 项回调，各项回调内 DeepCopy 均隔离
        /// <para>缺陷态：首项回调内 DeepCopy 残留后续全部命中（最易复现宿主）。</para>
        /// </summary>
        [Fact]
        public void MapList回调内DeepCopy_各项回调均获独立副本()
        {
            var payload = new DeepIsoPayload { Value = "shared" };
            payload.Next = payload; // 自引用 → CycleCache 完整检测路径

            var sources = new List<DeepIsoSource>
            {
                new() { Id = 1, Name = "a" },
                new() { Id = 2, Name = "b" },
                new() { Id = 3, Name = "c" },
            };
            var seen = new List<DeepIsoPayload>();

            var dtos = Mapper.MapList<DeepIsoSource, DeepIsoDto>(sources, (s, d) =>
            {
                var c1 = Mapper.DeepCopy(payload)!;
                var c2 = Mapper.DeepCopy(payload)!;
                seen.Add(c1);
                seen.Add(c2);
                // 每项回调内两次 DeepCopy 必须独立（缺陷态：非首项必命中首项残留）
                Assert.NotSame(c1, c2);
            });

            Assert.Equal(3, dtos!.Count);
            Assert.Equal(6, seen.Count);
            // 6 个副本两两独立
            for (int i = 0; i < seen.Count; i++)
                for (int j = i + 1; j < seen.Count; j++)
                    Assert.NotSame(seen[i], seen[j]);
        }

        /// <summary>
        /// Provider 腿专用用例（A2）— 回调内 DeepCopy 的对象图引用外层会话已出现的实例
        /// <para>拦截 Fast 通道/独立缓存残留面：第二次 DeepCopy 的对象图含第一次已拷贝的
        /// cyclic 实例，独立会话下应产生全新副本而非复用第一次的副本或源引用。</para>
        /// </summary>
        [Fact]
        public void Map回调内DeepCopy_对象图引用已出现实例_返回独立副本()
        {
            var cyclic = new DeepIsoPayload { Value = "cyclic" };
            cyclic.Next = cyclic;
            var plain = new DeepIsoPayload { Value = "plain", Next = cyclic }; // 对象图引用已出现实例

            var src = new DeepIsoSource { Id = 1, Name = "src" };
            DeepIsoPayload? c1 = null;
            DeepIsoPayload? c2 = null;

            Mapper.Map<DeepIsoSource, DeepIsoDto>(src, (s, d) =>
            {
                c1 = Mapper.DeepCopy(cyclic)!;
                c2 = Mapper.DeepCopy(plain)!;
            });

            Assert.NotNull(c1);
            Assert.NotNull(c2);
            // c2 会话内对 cyclic 的拷贝 ≠ c1（独立会话，不共享副本）
            Assert.NotSame(c1, c2!.Next);
            Assert.NotSame(cyclic, c2.Next); // 也非源引用
            // c2 会话内环自洽：plain→cyclic→cyclic 在 c2 的独立缓存内成环
            Assert.Same(c2.Next, c2.Next!.Next);
            // c1 自身环不受影响
            Assert.Same(c1, c1!.Next);
        }

        /// <summary>
        /// Map 回调内 DeepCopy 无环类型（Provider Fast 快路径腿）— 独立副本
        /// </summary>
        [Fact]
        public void Map回调内DeepCopy_无环类型_独立副本()
        {
            var payload = new DeepIsoPlain { Value = "plain" }; // 无环 POCO → Fast 快路径

            var src = new DeepIsoSource { Id = 1, Name = "src" };
            var seen = new List<DeepIsoPlain>();

            Mapper.Map<DeepIsoSource, DeepIsoDto>(src, (s, d) =>
            {
                seen.Add(Mapper.DeepCopy(payload)!);
                seen.Add(Mapper.DeepCopy(payload)!);
            });

            Assert.Equal(2, seen.Count);
            Assert.NotSame(seen[0], seen[1]);
            Assert.Equal("plain", seen[0].Value);
            Assert.Equal("plain", seen[1].Value);
            seen[0].Value = "changed";
            Assert.Equal("plain", seen[1].Value); // 互不影响
        }

        /// <summary>
        /// 异常路径（A3①）— 回调内 DeepCopy 抛 MapException（深链超限）被捕获，外层 Map 会话后续正常
        /// </summary>
        [Fact]
        public void Map回调内DeepCopy抛异常_捕获后外层会话正常()
        {
            var deep = new DeepIsoPayload { Value = "L0" };
            var tail = deep;
            for (int i = 1; i < 100; i++) // 100 层深链 > MaxRecursionDepth(64)
            {
                tail.Next = new DeepIsoPayload { Value = $"L{i}" };
                tail = tail.Next!;
            }

            var src = new DeepIsoSource { Id = 1, Name = "src" };
            DeepIsoPayload? afterFailure = null;
            int callbackCount = 0;

            var dto = Mapper.Map<DeepIsoSource, DeepIsoDto>(src, (s, d) =>
            {
                callbackCount++;
                Assert.ThrowsAny<MapException>(() => Mapper.DeepCopy(deep)!); // 回调内捕获
                afterFailure = Mapper.DeepCopy(new DeepIsoPayload { Value = "after" })!; // 异常后继续正常
            });

            Assert.NotNull(dto);
            Assert.Equal(1, callbackCount);
            Assert.NotNull(afterFailure);
            Assert.Equal("after", afterFailure!.Value);
        }

        /// <summary>
        /// 异常路径（A3②）— 回调内 DeepCopy 异常传播出回调，Map 失败但 ctx 状态不残留（下一次会话正常）
        /// </summary>
        [Fact]
        public void Map回调内DeepCopy异常传播_状态不残留_后续会话正常()
        {
            var deep = new DeepIsoPayload { Value = "L0" };
            var tail = deep;
            for (int i = 1; i < 100; i++)
            {
                tail.Next = new DeepIsoPayload { Value = $"L{i}" };
                tail = tail.Next!;
            }

            var payload = new DeepIsoPayload { Value = "P" };
            payload.Next = payload;

            var src = new DeepIsoSource { Id = 1, Name = "src" };

            // 第一次：回调内 DeepCopy 抛异常（未捕获）→ Map 传播
            Assert.ThrowsAny<MapException>(() =>
                Mapper.Map<DeepIsoSource, DeepIsoDto>(src, (s, d) =>
                {
                    _ = Mapper.DeepCopy(deep); // 抛 MapException（深链超限）
                }));

            // 第二次：新 Map 会话 + 回调内 DeepCopy 完全正常（finally 恢复 + 残留清零验证）
            var seen = new List<DeepIsoPayload>();
            var dto2 = Mapper.Map<DeepIsoSource, DeepIsoDto>(src, (s, d) =>
            {
                var c1 = Mapper.DeepCopy(payload)!;
                var c2 = Mapper.DeepCopy(payload)!;
                seen.Add(c1);
                seen.Add(c2);
            });

            Assert.NotNull(dto2);
            Assert.Equal(2, seen.Count);
            Assert.NotSame(seen[0], seen[1]); // 隔离在异常后的新会话中继续有效
        }

        /// <summary>
        /// 外层视图保真（A4，Map 宿主）— 跨 Map 会话隔离：第一次会话的回调内 DeepCopy 副本不残留到第二次
        /// </summary>
        [Fact]
        public void 跨Map会话_回调内DeepCopy副本不残留()
        {
            var payload = new DeepIsoPayload { Value = "P" };
            payload.Next = payload;

            var src = new DeepIsoSource { Id = 1, Name = "src" };
            var all = new List<DeepIsoPayload>();

            for (int session = 0; session < 2; session++)
            {
                Mapper.Map<DeepIsoSource, DeepIsoDto>(src, (s, d) =>
                {
                    all.Add(Mapper.DeepCopy(payload)!);
                });
            }

            Assert.Equal(2, all.Count);
            Assert.NotSame(all[0], all[1]); // 第二次会话拿到全新副本（缺陷态：顶层会话虽 Clear，但回调内路径共享链无此保证）
            Assert.Same(all[0], all[0].Next); // 各自环自洽
            Assert.Same(all[1], all[1].Next);
        }

        /// <summary>
        /// 外层视图保真（A4，MapList 宿主）— 跨 MapList 会话隔离
        /// </summary>
        [Fact]
        public void 跨MapList会话_回调内DeepCopy副本不残留()
        {
            var payload = new DeepIsoPayload { Value = "P" };
            payload.Next = payload;

            var sources = new List<DeepIsoSource> { new() { Id = 1, Name = "a" } };
            var all = new List<DeepIsoPayload>();

            for (int session = 0; session < 2; session++)
            {
                Mapper.MapList<DeepIsoSource, DeepIsoDto>(sources, (s, d) =>
                {
                    all.Add(Mapper.DeepCopy(payload)!);
                });
            }

            Assert.Equal(2, all.Count);
            Assert.NotSame(all[0], all[1]);
        }

        /// <summary>
        /// 热路径保真（A2/N2）— 正常顶层（Depth==0）行为不变：同一自引用源连续两次顶层 DeepCopy 各自独立完整
        /// </summary>
        [Fact]
        public void 顶层连续DeepCopy_热路径行为不变()
        {
            var payload = new DeepIsoPayload { Value = "P" };
            payload.Next = payload;

            var c1 = Mapper.DeepCopy(payload)!;
            var c2 = Mapper.DeepCopy(payload)!;

            // 顶层会话每次退出 Clear → 两次各自独立完整拷贝（原语义保持）
            Assert.NotSame(payload, c1);
            Assert.NotSame(payload, c2);
            Assert.NotSame(c1, c2);
            Assert.Same(c1, c1.Next); // 各自环自洽
            Assert.Same(c2, c2.Next);
            Assert.Equal("P", c1.Value);
            Assert.Equal("P", c2.Value);
        }

        /// <summary>
        /// 嵌套顶层 DeepCopy 隔离 — 嵌套调用使用独立缓存，退出后外层视图恢复（外层环检测不受影响）
        /// </summary>
        [Fact]
        public void 嵌套顶层DeepCopy_隔离且外层视图恢复()
        {
            var outer = new DeepIsoPayload { Value = "outer" };
            outer.Next = outer; // 外层自引用环

            var inner = new DeepIsoPayload { Value = "inner" };
            inner.Next = inner;

            // DeepCopy(T, Action) 的 action 在外层拷贝完成后（Depth==0）执行——
            // 构造真实嵌套：Map 回调内先顶层 DeepCopy 外层环图，再嵌套第二层顶层调用（回调内连续两次即嵌套形态）
            DeepIsoPayload? outerCopy = null;
            DeepIsoPayload? innerCopy = null;

            var src = new DeepIsoSource { Id = 1, Name = "src" };
            Mapper.Map<DeepIsoSource, DeepIsoDto>(src, (s, d) =>
            {
                outerCopy = Mapper.DeepCopy(outer)!;   // 第一次回调内顶层调用（Depth>0 → 隔离会话）
                innerCopy = Mapper.DeepCopy(inner)!;   // 第二次（独立隔离会话，不命中 outer 残留）
            });

            // 两个独立会话各自环自洽
            Assert.NotNull(outerCopy);
            Assert.NotNull(innerCopy);
            Assert.Same(outerCopy, outerCopy!.Next);
            Assert.Same(innerCopy, innerCopy!.Next);
            Assert.NotSame(outerCopy, innerCopy);
            Assert.Equal("outer", outerCopy.Value);
            Assert.Equal("inner", innerCopy.Value);
        }
    }

    // ================================================================
    // Generated 路径循环引用测试模型（手动注册 ctx-aware 委托模拟源生成器行为）
    // ================================================================

    /// <summary>
    /// 自引用循环节点 — 通过手动注册 RegisterGeneratedDeepCopy 模拟源生成器路径
    /// <para>验证 RegisterDeepCopyTarget + ctx.CycleCache 打破自引用循环</para>
    /// </summary>
    public class GeneratedCircularNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public GeneratedCircularNode? Self { get; set; }
    }

    /// <summary>
    /// 双向循环 A — 通过手动注册 RegisterGeneratedDeepCopy 模拟源生成器路径
    /// </summary>
    public class GeneratedCycleA
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public GeneratedCycleB? B { get; set; }
    }

    /// <summary>
    /// 双向循环 B — 通过手动注册 RegisterGeneratedDeepCopy 模拟源生成器路径
    /// </summary>
    public class GeneratedCycleB
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public GeneratedCycleA? A { get; set; }
    }

    // ================================================================
    // A1-03隔离测试模型
    // ================================================================

    /// <summary>
    /// 自引用载荷 — 类型级自引用 → Cyclic 分类 → DeepCopy 走 CycleCache 完整检测路径
    /// </summary>
    public class DeepIsoPayload
    {
        public string? Value { get; set; }
        public DeepIsoPayload? Next { get; set; }
    }

    /// <summary>
    /// 无环载荷 POCO — Provider Fast 快路径（Acyclic 分类）
    /// </summary>
    public class DeepIsoPlain
    {
        public string? Value { get; set; }
    }

    /// <summary>Map 宿主源模型（纯 POCO，与载荷对象图无关——回调内 DeepCopy 用闭包捕获实例）</summary>
    public class DeepIsoSource
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>Map 宿主目标模型</summary>
    public class DeepIsoDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
