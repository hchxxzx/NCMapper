using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// DeepCopyProviderBase mutant-killing tests — targets surviving Stryker mutants in DeepCopyProviderBase.cs
    /// Covers: ClearCache, DeepCopyObject (non-generic path), depth boundary, s_depth tracking
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class DeepCopyProviderBaseTests : IDisposable
    {
        private readonly int _originalMaxRecursionDepth;

        public DeepCopyProviderBaseTests()
        {
            _originalMaxRecursionDepth = Mapper.MaxRecursionDepth;
            Mapper.ClearCache();
        }

        public void Dispose()
        {
            Mapper.MaxRecursionDepth = _originalMaxRecursionDepth;
            Mapper.ClearCache();
        }

        // ================================================================
        // ClearCache tests (L224-228)
        // ================================================================

        /// <summary>
        /// ClearCache clears delegate cache and constructor cache — subsequent copies still work.
        /// Verifies s_delegateCache.Clear() and s_hasParameterlessCtor.Clear() at L226-227.
        /// Kills mutant: ClearCache statement removal (L226, L227).
        /// </summary>
        [Fact]
        public void DeepCopyProviderBase_ClearCache_Works()
        {
            // First copy — populates cache
            var original = new Address { City = "Beijing", Street = "ChangAn", ZipCode = 100000 };
            var copy1 = Mapper.DeepCopy(original)!;
            Assert.Equal("Beijing", copy1.City);

            // Clear all caches (DeepCopyCompiler + DeepCopyProviderBase)
            Mapper.ClearCache();

            // Second copy — must recompile, should still work correctly
            var copy2 = Mapper.DeepCopy(original)!;
            Assert.NotSame(original, copy2);
            Assert.Equal("Beijing", copy2.City);
            Assert.Equal("ChangAn", copy2.Street);
            Assert.Equal(100000, copy2.ZipCode);
            Assert.NotSame(copy1, copy2);
        }

        /// <summary>
        /// ClearCache followed by deep copy of different type — verifies cache truly cleared.
        /// Kills mutant: ClearCache statement removal.
        /// </summary>
        [Fact]
        public void DeepCopyProviderBase_ClearCache_DifferentType_Works()
        {
            var addr = new Address { City = "Shanghai", Street = "Nanjing", ZipCode = 200000 };
            var copy1 = Mapper.DeepCopy(addr)!;
            Assert.Equal("Shanghai", copy1.City);

            Mapper.ClearCache();

            // Copy a different type after clear
            var post = new Post { Title = "Hello", Views = 42 };
            var copy2 = Mapper.DeepCopy(post)!;
            Assert.NotSame(post, copy2);
            Assert.Equal("Hello", copy2.Title);
            Assert.Equal(42, copy2.Views);
        }

        // ================================================================
        // DeepCopyObject non-generic path (L194-203)
        // ================================================================

        /// <summary>
        /// DeepCopyObject via non-generic entry — exercises L194-203.
        /// Passes object (not strongly typed), verifies correct deep copy.
        /// Kills mutant: DeepCopyObjectCore statement removal (L87-111).
        /// </summary>
        [Fact]
        public void DeepCopyObjectCore_Via_NonGeneric_Path()
        {
            object original = new Address { City = "Guangzhou", Street = "Tianhe", ZipCode = 510000 };

            var provider = DeepCopyProviderBase.Current;
            var copy = provider.DeepCopyObject(original);

            Assert.NotNull(copy);
            Assert.NotSame(original, copy);
            Assert.IsType<Address>(copy);
            var addrCopy = (Address)copy!;
            Assert.Equal("Guangzhou", addrCopy.City);
            Assert.Equal("Tianhe", addrCopy.Street);
            Assert.Equal(510000, addrCopy.ZipCode);
        }

        /// <summary>
        /// DeepCopyObject with null — returns null (L196).
        /// Kills mutant: null guard removed.
        /// </summary>
        [Fact]
        public void DeepCopyObject_Null_Returns_Null()
        {
            var provider = DeepCopyProviderBase.Current;
            var result = provider.DeepCopyObject(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// DeepCopyObject with value type — returns same value (L199).
        /// Verifies value types are not deep copied (value semantics).
        /// Kills mutant: value type check removed.
        /// </summary>
        [Fact]
        public void DeepCopyObject_ValueType_Returns_Same()
        {
            var provider = DeepCopyProviderBase.Current;
            object boxed = 42;
            var result = provider.DeepCopyObject(boxed);
            Assert.Equal(42, result);
        }

        // ================================================================
        // Depth boundary tests (L86, L135)
        // ================================================================

    /// <summary>
    /// 环引用触发深度保护抛 MapException（修复后恢复原断言）：
    /// Cyclic 分支深度保护补齐（Mapper.cs :927，与 Acyclic 分支同构）后，Depth 检查先于
    /// CycleCache 打破执行——9 节点环递归超过 MaxRecursionDepth=5 时抛 MapException，
    /// 生成器修复前（Fast 环形缓冲 8 槽）同样抛——原断言恢复有效。
    /// 杀变体：&gt;= → &gt;（抛太早/太晚）。
    /// </summary>
    [Fact]
    public void DeepCopy_CircularRef_Throws_At_DepthLimit()
    {
        Mapper.MaxRecursionDepth = 5;

        // 构造 9 节点环
        var nodes = new SelfRefNode[9];
        for (int i = 0; i < 9; i++) nodes[i] = new SelfRefNode { Value = i + 1 };
        for (int i = 0; i < 8; i++) nodes[i].Self = nodes[i + 1];
        nodes[8].Self = nodes[0]; // 形成环

        var ex = Assert.Throws<MapException>(() => Mapper.DeepCopy(nodes[0]));
        Assert.Contains("5", ex.Message);
    }

    /// <summary>
    /// 非环深链超过 MaxRecursionDepth 触发深度保护抛 MapException（补测：
    /// Cyclic 分支深度保护补齐后，深链经 CycleCache 不命中 → ctx.Depth 累计 → 超限抛；
    /// 杀变体：&gt;= → &gt）
    /// </summary>
    [Fact]
    public void DeepCopy_DeepChain_Throws_At_DepthLimit()
    {
        Mapper.MaxRecursionDepth = 5;

        // 构造 9 节点深链（非环）：超过 MaxRecursionDepth=5 → 深度保护抛
        var head = new SelfRefNode { Value = 1 };
        var cur = head;
        for (var i = 2; i <= 9; i++)
        {
            cur.Self = new SelfRefNode { Value = i };
            cur = cur.Self;
        }

        var ex = Assert.Throws<MapException>(() => Mapper.DeepCopy(head));
        Assert.Contains("5", ex.Message);
    }

        /// <summary>
        /// DeepCopy with nested objects within depth limit — succeeds.
        /// Verifies s_depth tracking (increment on entry, decrement on exit) at L82/L134.
        /// Kills mutant: s_depth-- removed (L135) — would cause depth to accumulate.
        /// </summary>
        [Fact]
        public void DeepCopy_NestedWithinLimit_Succeeds()
        {
            Mapper.MaxRecursionDepth = 10;

            // Build a chain of 5 nodes
            var chain = new SelfRefNode
            {
                Value = 5,
                Self = new SelfRefNode
                {
                    Value = 4,
                    Self = new SelfRefNode
                    {
                        Value = 3,
                        Self = new SelfRefNode
                        {
                            Value = 2,
                            Self = new SelfRefNode { Value = 1, Self = null }
                        }
                    }
                }
            };

            var copy = Mapper.DeepCopy(chain)!;

            Assert.NotSame(chain, copy);
            Assert.Equal(5, copy.Value);
            Assert.NotNull(copy.Self);
            Assert.Equal(4, copy.Self!.Value);
            Assert.Equal(3, copy.Self.Self!.Value);
            Assert.Equal(2, copy.Self.Self.Self!.Value);
            Assert.Equal(1, copy.Self.Self.Self.Self!.Value);
            Assert.Null(copy.Self.Self.Self.Self.Self);
        }

        /// <summary>
        /// Multiple sequential DeepCopy calls — verifies s_depth returns to 0 after each call.
        /// If s_depth-- is removed at L135, depth accumulates and eventually throws.
        /// Kills mutant: s_depth-- removed, s_depth != 0 check (L135).
        /// </summary>
        [Fact]
        public void DeepCopy_ManySequential_DontAccumulateDepth()
        {
            for (int i = 0; i < 70; i++)
            {
                var original = new Address { City = $"City{i}", Street = $"Street{i}", ZipCode = i };
                var copy = Mapper.DeepCopy(original)!;
                Assert.Equal($"City{i}", copy.City);
            }
        }

        /// <summary>
        /// DeepCopyObjectCore via static internal path — exercises L213-219.
        /// Uses Mapper.DeepCopy with entity containing nested reference types.
        /// Verifies the s_current static field is used correctly.
        /// </summary>
        [Fact]
        public void DeepCopyObjectCore_StaticPath_Works()
        {
            var original = new Blog
            {
                Title = "TestBlog",
                Posts = new List<Post>
                {
                    new Post { Title = "P1", Views = 10 },
                    new Post { Title = "P2", Views = 20 }
                }
            };

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal("TestBlog", copy.Title);
            Assert.NotNull(copy.Posts);
            Assert.NotSame(original.Posts, copy.Posts);
            Assert.Equal(2, copy.Posts!.Count);
            Assert.Equal("P1", copy.Posts[0].Title);
            Assert.NotSame(original.Posts[0], copy.Posts[0]);
        }

        /// <summary>
        /// DeepCopy with MaxRecursionDepth=1 and simple (non-recursive) object — should succeed.
        /// Verifies boundary: s_depth=1, MaxRecursionDepth=1, 1 > 1 is false → no throw.
        /// Kills mutant: > → >= at L86 (would throw at depth 1).
        /// </summary>
        [Fact]
        public void DeepCopy_DepthExactly_At_Limit_Succeeds()
        {
            Mapper.MaxRecursionDepth = 1;

            var original = new Address { City = "Shenzhen", Street = "Futian", ZipCode = 518000 };
            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal("Shenzhen", copy.City);
        }

        // ================================================================
        // A3-04：方向 I 取样单点化 + 唯一性守卫
        // ================================================================

        /// <summary>
        /// ResolveContextualDeepCopyMethod 白盒：返回方法与原三处内联过滤谓词一致
        /// （Name=DeepCopy / IsGenericMethod / 两参 / param[1]=DeepCopyContext）；
        /// 唯一命中前提实证——基类 DeepCopy 重载族恰 2 个（155 一参 public + :176 两参 internal），
        /// 谓词过滤后恰 1（Single 语义不误抛）。多命中敏感性由变异演练承担（临时加同形状重载 →
        /// 计数判定抛候选签名清单异常红 → 移除复绿，V2）。
        /// </summary>
        [Fact]
        public void ResolveContextualDeepCopyMethod_PredicateContract_UniqueHit()
        {
            var method = DeepCopyProviderBase.ResolveContextualDeepCopyMethod();

            // 谓词契约自校验（与原三处内联谓词逐条一致）
            Assert.Equal("DeepCopy", method.Name);
            Assert.True(method.IsGenericMethod);
            Assert.Equal(2, method.GetParameters().Length);
            Assert.Equal(typeof(DeepCopyContext), method.GetParameters()[1].ParameterType);

            // 唯一命中前提实证：重载族形态漂移须同步本断言（防先验锚退化）
            var overloadCount = typeof(DeepCopyProviderBase)
                .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .Count(m => m.Name == "DeepCopy");
            Assert.Equal(2, overloadCount);

            // 返回未闭合泛型方法定义（调用方 MakeGenericMethod 闭合——三消费方形态）
            Assert.True(method.ContainsGenericParameters);

            // 闭合可用性：MakeGenericMethod 后表达式树调用路径可用（CompileDeepCopyDelegate 同链）
            var closed = method.MakeGenericMethod(typeof(Address));
            Assert.Equal(typeof(Address), closed.GetGenericArguments()[0]);
        }

        /// <summary>取样收敛后深拷贝端到端回归：FastDeepCopyProvider（方向 I 消费方）对含嵌套
        /// 引用类型对象行为不变——守卫收敛不改变正常路径语义（A3-04 验收 #2 既有面）。</summary>
        [Fact]
        public void DeepCopy_AfterSamplingConsolidation_EndToEnd_Unchanged()
        {
            var original = new Blog
            {
                Title = "SamplingTest",
                Posts = new List<Post> { new Post { Title = "X1", Views = 1 } }
            };
            var copy = Mapper.DeepCopy(original)!;
            Assert.NotSame(original, copy);
            Assert.Equal("SamplingTest", copy.Title);
            Assert.Equal("X1", copy.Posts![0].Title);
        }
    }
}
