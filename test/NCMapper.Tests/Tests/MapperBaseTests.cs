using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using NCMapper;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// MapperBase core method tests — targets surviving Stryker mutants in MapperBase.cs
    /// <para>Covers: null guards, action callbacks, MapList code paths, recursion depth tracking,
    /// dual-counter (s_depthIn/s_depthOut) accumulation, and exception recovery.</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class MapperBaseTests : IDisposable
    {
        private readonly int _originalMaxRecursionDepth;

        public MapperBaseTests()
        {
            _originalMaxRecursionDepth = Mapper.MaxRecursionDepth;
            Mapper.ClearGenerated();
        }

        public void Dispose()
        {
            Mapper.MaxRecursionDepth = _originalMaxRecursionDepth;
            Mapper.ClearGenerated();
        }

        // ================================================================
        // Map(TSource, Action) overload — null source guard + action callback
        // ================================================================

        /// <summary>
        /// Map(null) returns default(TTarget) — null source guard at line 91.
        /// Kills mutant: null return removed.
        /// </summary>
        [Fact]
        public void Map_NullSource_Returns_Default()
        {
            var result = MapperBase<FullTypeEntity, SameTypeDto>.Map(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// Map(source, action) invokes action with correct source and target — line 106.
        /// Kills mutant: action?.Invoke removed.
        /// </summary>
        [Fact]
        public void Map_With_Action_Action_Is_Called()
        {
            var source = new FullTypeEntity { IntProp = 42, StringProp = "hello" };
            FullTypeEntity? capturedSource = null;
            SameTypeDto? capturedTarget = null;

            var result = MapperBase<FullTypeEntity, SameTypeDto>.Map(source, (s, t) =>
            {
                capturedSource = s;
                capturedTarget = t;
            });

            Assert.NotNull(result);
            Assert.Same(source, capturedSource);
            Assert.Same(result, capturedTarget);
            Assert.Equal(42, capturedTarget!.IntProp);
            Assert.Equal("hello", capturedTarget.StringProp);
        }

        /// <summary>
        /// Map(source) without action returns correctly mapped result — verifies s_depthOut++ at line 105.
        /// 70 sequential calls: if s_depthOut++ is removed, diff accumulates and call 65 throws.
        /// Kills mutants: s_depthOut++ removed (L105), s_depthOut-- instead of ++ (PostDecrement).
        /// </summary>
        [Fact]
        public void Map_Many_Sequential_Calls_Do_Not_Accumulate_Depth()
        {
            for (int i = 0; i < 70; i++)
            {
                var entity = new FullTypeEntity { IntProp = i };
                var dto = MapperBase<FullTypeEntity, SameTypeDto>.Map(entity);
                Assert.NotNull(dto);
                Assert.Equal(i, dto!.IntProp);
            }
        }

        // ================================================================
        // Map(TSource, TTarget, Action) overload — null guards + action + depth
        // ================================================================

        /// <summary>
        /// Map(null, null) does not throw — null guard at line 124.
        /// Kills mutant: null guard removed (statement removal).
        /// </summary>
        [Fact]
        public void Map_SourceNull_TargetNull_No_Exception()
        {
            var ex = Record.Exception(() =>
                MapperBase<FullTypeEntity, SameTypeDto>.Map(null!, null!));
            Assert.Null(ex);
        }

        /// <summary>
        /// Map(null, target) does not throw, target unchanged — line 124 || vs &amp;&amp;.
        /// With &amp;&amp; mutant: source==null is true but target!=null, so condition is false
        /// and code continues to s_depthIn++ — would not crash but corrupts depth.
        /// Kills mutant: || → &amp;&amp; (L124).
        /// </summary>
        [Fact]
        public void Map_SourceNull_TargetNonNull_No_Exception()
        {
            var target = new SameTypeDto { IntProp = 999 };

            var ex = Record.Exception(() =>
                MapperBase<FullTypeEntity, SameTypeDto>.Map(null!, target));

            Assert.Null(ex);
            Assert.Equal(999, target.IntProp); // target unchanged
        }

        /// <summary>
        /// Map(source, null) does not throw — line 124 || vs &amp;&amp;.
        /// With &amp;&amp; mutant: source!=null and target==null, condition false,
        /// code continues to MapAction(source, null) which crashes with NullReferenceException.
        /// Kills mutant: || → &amp;&amp; (L124), null guard removed (statement removal).
        /// </summary>
        [Fact]
        public void Map_SourceNonNull_TargetNull_No_Exception()
        {
            var source = new FullTypeEntity { IntProp = 42 };

            var ex = Record.Exception(() =>
                MapperBase<FullTypeEntity, SameTypeDto>.Map(source, null!));

            Assert.Null(ex);
        }

        /// <summary>
        /// Map(source, target, action) updates target and invokes action — lines 136-138.
        /// Kills mutants: MapAction removed (L136), action?.Invoke removed (L138),
        /// s_depthOut++ removed (L137).
        /// </summary>
        [Fact]
        public void Map_UpdateOverload_Action_Is_Called()
        {
            var source = new FullTypeEntity { IntProp = 55, StringProp = "updated" };
            var target = new SameTypeDto { IntProp = 0, StringProp = "original" };
            bool actionCalled = false;
            FullTypeEntity? capturedSrc = null;
            SameTypeDto? capturedTgt = null;

            MapperBase<FullTypeEntity, SameTypeDto>.Map(source, target, (s, t) =>
            {
                actionCalled = true;
                capturedSrc = s;
                capturedTgt = t;
            });

            Assert.True(actionCalled, "Action should have been invoked");
            Assert.Same(source, capturedSrc);
            Assert.Same(target, capturedTgt);
            Assert.Equal(55, target.IntProp);
            Assert.Equal("updated", target.StringProp);
        }

        /// <summary>
        /// Map(source, target) updates target correctly without action — verifies s_depthOut++ at L137.
        /// 70 sequential calls: if s_depthOut++ removed, diff accumulates and call 65 throws.
        /// Kills mutants: s_depthOut++ removed (L137), s_depthOut-- (PostDecrement).
        /// </summary>
        [Fact]
        public void Map_UpdateOverload_Many_Sequential_Calls_Do_Not_Accumulate()
        {
            for (int i = 0; i < 70; i++)
            {
                var source = new FullTypeEntity { IntProp = i, StringProp = $"S{i}" };
                var target = new SameTypeDto();
                MapperBase<FullTypeEntity, SameTypeDto>.Map(source, target);
                Assert.Equal(i, target.IntProp);
                Assert.Equal($"S{i}", target.StringProp);
            }
        }

        // ================================================================
        // MapList — null source, action, code paths
        // ================================================================

        /// <summary>
        /// MapList(null) returns null — null guard at line 149.
        /// Kills mutant: null return removed.
        /// </summary>
        [Fact]
        public void MapList_NullSource_Returns_Null()
        {
            var result = MapperBase<FullTypeEntity, SameTypeDto>.MapList(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// MapList with action invokes action for each element — lines 210, 245.
        /// Also verifies action != null bypasses fast paths (L163, L171 conditions are false).
        /// Kills mutants: action removed from MapListWithCapacity / MapListSlow.
        /// </summary>
        [Fact]
        public void MapList_With_Action_Each_Element()
        {
            var sources = new List<FullTypeEntity>
            {
                new FullTypeEntity { IntProp = 1, StringProp = "A" },
                new FullTypeEntity { IntProp = 2, StringProp = "B" },
                new FullTypeEntity { IntProp = 3, StringProp = "C" }
            };

            var actionCalls = new List<(FullTypeEntity src, SameTypeDto tgt)>();
            var result = MapperBase<FullTypeEntity, SameTypeDto>.MapList(sources, (s, t) =>
            {
                actionCalls.Add((s, t));
            });

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.Equal(3, actionCalls.Count);

            for (int i = 0; i < 3; i++)
            {
                Assert.Same(sources[i], actionCalls[i].src);
                Assert.Same(result[i], actionCalls[i].tgt);
                Assert.Equal(i + 1, actionCalls[i].tgt.IntProp);
            }
        }

        /// <summary>
        /// MapList with List source (ICollection) and no action — exercises fast path at L163
        /// (action==null + List + MapListFunc!=null). Verifies correct mapping.
        /// Kills mutant: MapListFunc == null → != null (L163) — if MapListFunc IS non-null
        /// and mutant changes condition to MapListFunc==null (false), code falls through.
        /// </summary>
        [Fact]
        public void MapList_ICollection_Path()
        {
            // Warm up to ensure MapListFunc is compiled
            _ = MapperBase<FullTypeEntity, SameTypeDto>.MapList(
                new List<FullTypeEntity> { new FullTypeEntity { IntProp = 0 } });

            var sources = new List<FullTypeEntity>
            {
                new FullTypeEntity { IntProp = 10, StringProp = "X" },
                new FullTypeEntity { IntProp = 20, StringProp = "Y" },
                new FullTypeEntity { IntProp = 30, StringProp = "Z" }
            };

            // No action + List → should take fast path (L163-168)
            var result = MapperBase<FullTypeEntity, SameTypeDto>.MapList(sources);

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.Equal(10, result[0].IntProp);
            Assert.Equal("X", result[0].StringProp);
            Assert.Equal(20, result[1].IntProp);
            Assert.Equal(30, result[2].IntProp);
        }

        /// <summary>
        /// MapList with IEnumerable (not ICollection, not List, not array) — exercises slow path at L187.
        /// Kills mutant: slow path statement removal.
        /// </summary>
        [Fact]
        public void MapList_NonICollection_Path()
        {
            IEnumerable<FullTypeEntity> YieldSources()
            {
                yield return new FullTypeEntity { IntProp = 100, StringProp = "first" };
                yield return new FullTypeEntity { IntProp = 200, StringProp = "second" };
            }

            var sources = YieldSources();

            var result = MapperBase<FullTypeEntity, SameTypeDto>.MapList(sources);

            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
            Assert.Equal(100, result[0].IntProp);
            Assert.Equal("first", result[0].StringProp);
            Assert.Equal(200, result[1].IntProp);
            Assert.Equal("second", result[1].StringProp);
        }

        /// <summary>
        /// MapList with array source and no action — exercises array fast path at L171-175.
        /// Verifies array → List via MapArrayFunc.
        /// Kills mutant: action == null → != null at L171 (array fast path bypassed).
        /// </summary>
        [Fact]
        public void MapList_Array_FastPath()
        {
            // Warm up to ensure MapArrayFunc is compiled
            _ = MapperBase<Post, PostDto>.MapList(new Post[] { new Post { Title = "warm", Views = 0 } });

            var sources = new[]
            {
                new Post { Title = "Alpha", Views = 10 },
                new Post { Title = "Beta", Views = 20 },
                new Post { Title = "Gamma", Views = 30 }
            };

            // No action + array → should take array fast path (L171-175)
            var result = MapperBase<Post, PostDto>.MapList(sources);

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.Equal("Alpha", result[0].Title);
            Assert.Equal(10, result[0].Views);
            Assert.Equal("Beta", result[1].Title);
            Assert.Equal(20, result[1].Views);
            Assert.Equal("Gamma", result[2].Title);
            Assert.Equal(30, result[2].Views);
        }

        /// <summary>
        /// MapList with null elements in list — null elements preserved as null.
        /// Verifies null handling in MapListWithCapacity (L204-207).
        /// </summary>
        [Fact]
        public void MapList_With_Null_Elements()
        {
            var sources = new List<FullTypeEntity>
            {
                new FullTypeEntity { IntProp = 1 },
                null!,
                new FullTypeEntity { IntProp = 3 }
            };

            var result = MapperBase<FullTypeEntity, SameTypeDto>.MapList(sources);

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.NotNull(result[0]);
            Assert.Equal(1, result[0].IntProp);
            Assert.Null(result[1]);
            Assert.NotNull(result[2]);
            Assert.Equal(3, result[2].IntProp);
        }

        /// <summary>
        /// MapList with null elements in array — null elements preserved as null.
        /// Verifies null handling in array fast path (BuildCollectionLoopBody storeNull).
        /// </summary>
        [Fact]
        public void MapList_Array_With_Null_Elements()
        {
            // Warm up
            _ = MapperBase<Post, PostDto>.MapList(new Post[] { new Post() });

            var sources = new Post?[]
            {
                new Post { Title = "First", Views = 1 },
                null,
                new Post { Title = "Third", Views = 3 }
            };

            var result = MapperBase<Post, PostDto>.MapList(sources!);

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.Equal("First", result[0].Title);
            Assert.Null(result[1]);
            Assert.Equal("Third", result[2].Title);
        }

        /// <summary>
        /// MapList sequential calls verify s_depthOut++ at L166 (fast path).
        /// If s_depthOut++ removed, diff accumulates across calls.
        /// 70 calls should succeed; with mutant, call 65 throws MapException.
        /// Kills mutants: s_depthOut++ removed (L166), s_depthOut-- (PostDecrement).
        /// </summary>
        [Fact]
        public void MapList_Many_Sequential_Calls_Do_Not_Accumulate()
        {
            // Warm up MapListFunc
            _ = MapperBase<FullTypeEntity, SameTypeDto>.MapList(
                new List<FullTypeEntity> { new FullTypeEntity() });

            for (int i = 0; i < 70; i++)
            {
                var sources = new List<FullTypeEntity>
                {
                    new FullTypeEntity { IntProp = i }
                };
                var result = MapperBase<FullTypeEntity, SameTypeDto>.MapList(sources);
                Assert.NotNull(result);
                Assert.Single(result!);
                Assert.Equal(i, result[0].IntProp);
            }
        }

        /// <summary>
        /// MapList array fast path sequential calls verify s_depthOut++ at L174.
        /// If s_depthOut++ removed, diff accumulates across calls.
        /// Kills mutants: s_depthOut++ removed (L174), s_depthOut-- (PostDecrement).
        /// </summary>
        [Fact]
        public void MapList_Array_Many_Sequential_Calls_Do_Not_Accumulate()
        {
            // Warm up MapArrayFunc
            _ = MapperBase<Post, PostDto>.MapList(new Post[] { new Post() });

            for (int i = 0; i < 70; i++)
            {
                var sources = new[] { new Post { Title = $"T{i}", Views = i } };
                var result = MapperBase<Post, PostDto>.MapList(sources);
                Assert.NotNull(result);
                Assert.Single(result!);
                Assert.Equal($"T{i}", result[0].Title);
                Assert.Equal(i, result[0].Views);
            }
        }

        // ================================================================
        // Recursion depth — throw, boundary, recovery
        // ================================================================

        /// <summary>
        /// Circular reference exceeds MaxRecursionDepth, throws MapException — lines 132-134.
        /// Kills mutants: throw statement removed (L132-134), s_depthIn++ removed (L127),
        /// s_depthIn-- (PostDecrement at L127).
        /// </summary>
        [Fact]
        public void RecursionDepth_Exceeded_Throws_MapException()
        {
            // Create a circular reference: node.Self = node
            var node = new SelfRefNode { Value = 1 };
            node.Self = node;

            var ex = Assert.Throws<MapException>(() =>
                MapperBase<SelfRefNode, SelfRefNodeDto>.Map(node));

            Assert.Contains("64", ex.Message);
            Assert.Equal(typeof(SelfRefNode), ex.SourceType);
            Assert.Equal(typeof(SelfRefNodeDto), ex.TargetType);
        }

        /// <summary>
        /// Circular reference via update overload throws MapException — lines 132-134.
        /// Targets the Map(source, target) overload specifically.
        /// Kills mutants: throw statement removed (L132-134).
        /// </summary>
        [Fact]
        public void RecursionDepth_UpdateOverload_Exceeded_Throws()
        {
            var node = new SelfRefNode { Value = 1 };
            node.Self = node; // circular
            var target = new SelfRefNodeDto();

            Assert.Throws<MapException>(() =>
                MapperBase<SelfRefNode, SelfRefNodeDto>.Map(node, target));
        }

        /// <summary>
        /// Boundary test for &gt; vs &gt;= mutant at line 128.
        /// MaxRecursionDepth=2, make 2 sequential MapList calls (diff reaches 2 but does not exceed).
        /// With &gt;=: diff=2 >= 2 → throws on 2nd call.
        /// With &gt;:   diff=2 &gt; 2 is false → 2nd call succeeds.
        /// Kills mutant: &gt; → &gt;= (L153).
        /// </summary>
        [Fact]
        public void RecursionDepth_MapList_Boundary_Exactly_At_Limit()
        {
            Mapper.MaxRecursionDepth = 2;

            // First call: s_depthIn=1, s_depthOut=0, diff=1. 1 > 2 = false → OK
            var sources1 = new List<FullTypeEntity> { new FullTypeEntity { IntProp = 1 } };
            var result1 = MapperBase<FullTypeEntity, SameTypeDto>.MapList(sources1);
            Assert.NotNull(result1);
            Assert.Equal(1, result1![0].IntProp);

            // Second call: s_depthIn=2, s_depthOut=0, diff=2. 2 > 2 = false → OK
            // With >= mutant: 2 >= 2 = true → THROWS
            var sources2 = new List<FullTypeEntity> { new FullTypeEntity { IntProp = 2 } };
            var result2 = MapperBase<FullTypeEntity, SameTypeDto>.MapList(sources2);
            Assert.NotNull(result2);
            Assert.Equal(2, result2![0].IntProp);
        }

        /// <summary>
        /// After recursion exception, counters reset to 0 and subsequent maps work —
        /// verifies s_depthIn=0; s_depthOut=0 at lines 130-131.
        /// Kills mutant: counter reset removed.
        /// </summary>
        [Fact]
        public void RecursionDepth_Reset_After_Exception()
        {
            // Trigger circular reference exception
            var node = new SelfRefNode { Value = 1 };
            node.Self = node;

            Assert.Throws<MapException>(() =>
                MapperBase<SelfRefNode, SelfRefNodeDto>.Map(node));

            // After exception, counters should be reset — subsequent maps must work
            var entity = new FullTypeEntity { IntProp = 777, StringProp = "AfterException" };
            var dto = MapperBase<FullTypeEntity, SameTypeDto>.Map(entity);
            Assert.NotNull(dto);
            Assert.Equal(777, dto!.IntProp);
            Assert.Equal("AfterException", dto.StringProp);

            // Update overload too
            var target = new SameTypeDto();
            MapperBase<FullTypeEntity, SameTypeDto>.Map(entity, target);
            Assert.Equal(777, target.IntProp);
        }

        /// <summary>
        /// MapList with circular-reference elements throws MapException — lines 157-159.
        /// Kills mutants: throw statement removed in MapList, s_depthIn++ removed (L152).
        /// </summary>
        [Fact]
        public void MapList_RecursionDepth_Exceeded_Throws()
        {
            var node = new SelfRefNode { Value = 1 };
            node.Self = node; // circular

            var sources = new List<SelfRefNode> { node };

            Assert.Throws<MapException>(() =>
                MapperBase<SelfRefNode, SelfRefNodeDto>.MapList(sources));
        }

        /// <summary>
        /// Recursion exception in MapList, then subsequent MapList calls work —
        /// verifies counter reset at lines 155-156.
        /// Kills mutant: counter reset removed in MapList.
        /// </summary>
        [Fact]
        public void MapList_RecursionDepth_Reset_After_Exception()
        {
            var node = new SelfRefNode { Value = 1 };
            node.Self = node;

            Assert.Throws<MapException>(() =>
                MapperBase<SelfRefNode, SelfRefNodeDto>.MapList(new List<SelfRefNode> { node }));

            // After exception, subsequent MapList calls must work
            var sources = new List<FullTypeEntity>
            {
                new FullTypeEntity { IntProp = 888 }
            };
            var result = MapperBase<FullTypeEntity, SameTypeDto>.MapList(sources);
            Assert.NotNull(result);
            Assert.Single(result!);
            Assert.Equal(888, result[0].IntProp);
        }

        /// <summary>
        /// Chain-style recursion (LinkNode) within depth limit maps correctly.
        /// Verifies correct depth tracking (s_depthIn++ and s_depthOut++) for nested calls.
        /// Kills mutants: s_depthIn++ removed (would allow deeper recursion than limit),
        /// s_depthIn-- (would prevent depth tracking entirely).
        /// </summary>
        [Fact]
        public void RecursionDepth_ChainWithinLimit_MapsCorrectly()
        {
            // Build a chain of 5 nodes: 5 → 4 → 3 → 2 → 1 → null
            var chain = new LinkNode
            {
                Value = 5,
                Next = new LinkNode
                {
                    Value = 4,
                    Next = new LinkNode
                    {
                        Value = 3,
                        Next = new LinkNode
                        {
                            Value = 2,
                            Next = new LinkNode { Value = 1, Next = null }
                        }
                    }
                }
            };

            var dto = MapperBase<LinkNode, LinkNodeDto>.Map(chain);

            Assert.NotNull(dto);
            Assert.Equal(5, dto!.Value);
            Assert.NotNull(dto.Next);
            Assert.Equal(4, dto.Next!.Value);
            Assert.Equal(3, dto.Next.Next!.Value);
            Assert.Equal(2, dto.Next.Next.Next!.Value);
            Assert.Equal(1, dto.Next.Next.Next.Next!.Value);
            Assert.Null(dto.Next.Next.Next.Next.Next);
        }

        /// <summary>
        /// Low MaxRecursionDepth with circular reference — verifies exception is thrown
        /// and the depth check condition (s_depthIn - s_depthOut > MaxRecursionDepth).
        /// With MaxRecursionDepth=1, a self-referencing node should eventually throw.
        /// Kills mutants: arithmetic s_depthIn + s_depthOut (diff formula broken for recursion),
        /// comparison operator changes.
        /// </summary>
        [Fact]
        public void RecursionDepth_LowLimit_CircularRef_Throws()
        {
            Mapper.MaxRecursionDepth = 1;

            var node = new SelfRefNode { Value = 42 };
            node.Self = node;

            Assert.Throws<MapException>(() =>
                MapperBase<SelfRefNode, SelfRefNodeDto>.Map(node));

            // After exception, verify recovery
            var entity = new FullTypeEntity { IntProp = 123 };
            var dto = MapperBase<FullTypeEntity, SameTypeDto>.Map(entity);
            Assert.NotNull(dto);
            Assert.Equal(123, dto!.IntProp);
        }

        /// <summary>
        /// Multiple sequential Map calls with update overload — verifies s_depthIn++/s_depthOut++
        /// correctly track depth for the update overload specifically (L127, L137).
        /// If s_depthIn++ changed to s_depthIn--, diff goes negative and never exceeds limit,
        /// even with circular references.
        /// Kills mutants: s_depthIn-- (PostDecrement at L127).
        /// </summary>
        [Fact]
        public void Map_UpdateOverload_CircularRef_Throws_Even_With_LowDepth()
        {
            Mapper.MaxRecursionDepth = 1;

            var node = new SelfRefNode { Value = 1 };
            node.Self = node;
            var target = new SelfRefNodeDto();

            Assert.Throws<MapException>(() =>
                MapperBase<SelfRefNode, SelfRefNodeDto>.Map(node, target));

            // Recovery after exception
            var src = new FullTypeEntity { IntProp = 456 };
            var tgt = new SameTypeDto();
            MapperBase<FullTypeEntity, SameTypeDto>.Map(src, tgt);
            Assert.Equal(456, tgt.IntProp);
        }

        /// <summary>
        /// MapList with non-ICollection IEnumerable + action — exercises slow path with action (L235-249).
        /// Verifies action is called for each element in the slow path.
        /// </summary>
        [Fact]
        public void MapList_NonICollection_WithAction_CallsAction()
        {
            IEnumerable<FullTypeEntity> YieldSources()
            {
                yield return new FullTypeEntity { IntProp = 10, StringProp = "slow1" };
                yield return new FullTypeEntity { IntProp = 20, StringProp = "slow2" };
            }

            var actionCalls = new List<int>();
            var result = MapperBase<FullTypeEntity, SameTypeDto>.MapList(YieldSources(), (s, t) =>
            {
                actionCalls.Add(s.IntProp);
            });

            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
            Assert.Equal(2, actionCalls.Count);
            Assert.Contains(10, actionCalls);
            Assert.Contains(20, actionCalls);
        }

        /// <summary>
        /// MapList with array + action — action is non-null, so fast path at L171 is skipped
        /// (action == null is false). Exercises ICollection path for array.
        /// Verifies action called for each element.
        /// Kills mutant: action == null → != null at L171 (would incorrectly enter array fast path).
        /// </summary>
        [Fact]
        public void MapList_Array_With_Action_Skips_FastPath()
        {
            var sources = new[]
            {
                new Post { Title = "A", Views = 1 },
                new Post { Title = "B", Views = 2 }
            };

            var actionCalls = new List<string>();
            var result = MapperBase<Post, PostDto>.MapList(sources, (s, t) =>
            {
                actionCalls.Add(s.Title);
            });

            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
            Assert.Equal("A", result[0].Title);
            Assert.Equal("B", result[1].Title);
            Assert.Equal(2, actionCalls.Count);
            Assert.Contains("A", actionCalls);
            Assert.Contains("B", actionCalls);
        }

        // ================================================================
        // Round 2 — Additional mutant-killing tests
        // ================================================================

        /// <summary>
        /// MapAction property returns same delegate on repeated access — verifies lazy init at L30.
        /// With mutant (s_mapAction = GetMapAction() without ??=), each access recompiles.
        /// Kills mutant: CoalesceAssignment removed (L30).
        /// </summary>
        [Fact]
        public void MapAction_LazyInit_Returns_Same_Delegate()
        {
            var action1 = MapperBase<FullTypeEntity, SameTypeDto>.MapAction;
            var action2 = MapperBase<FullTypeEntity, SameTypeDto>.MapAction;

            Assert.NotNull(action1);
            Assert.Same(action1, action2);
        }

        /// <summary>
        /// MapList with null elements in non-ICollection IEnumerable — exercises L230 slow path.
        /// Verifies null elements are preserved as null in the result.
        /// Kills mutant: item == null ? null! : MapFunc(item) → false ? null! : MapFunc(item) (L230).
        /// </summary>
        [Fact]
        public void MapList_NullElements_In_NonICollection_Enumerable()
        {
            IEnumerable<FullTypeEntity> YieldWithNulls()
            {
                yield return new FullTypeEntity { IntProp = 10 };
                yield return null!;
                yield return new FullTypeEntity { IntProp = 30 };
            }

            var result = MapperBase<FullTypeEntity, SameTypeDto>.MapList(YieldWithNulls());

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.NotNull(result[0]);
            Assert.Equal(10, result[0].IntProp);
            Assert.Null(result[1]);
            Assert.NotNull(result[2]);
            Assert.Equal(30, result[2].IntProp);
        }

        /// <summary>
        /// Simple same-type mapping uses MemberInit optimization path — verifies L282-286.
        /// FullTypeEntity → SameTypeDto has all same-type properties, should use MemberInit.
        /// Kills mutant: TryBuildMemberInit returns null (shouldFallback negated at L326).
        /// </summary>
        [Fact]
        public void MemberInit_SimpleMapping_Works()
        {
            var source = new FullTypeEntity
            {
                IntProp = 123,
                StringProp = "MemberInit",
                LongProp = 999L,
                FloatProp = 1.5f
            };

            var dto = MapperBase<FullTypeEntity, SameTypeDto>.MapFunc(source);

            Assert.NotNull(dto);
            Assert.Equal(123, dto.IntProp);
            Assert.Equal("MemberInit", dto.StringProp);
            Assert.Equal(999L, dto.LongProp);
            Assert.Equal(1.5f, dto.FloatProp);
        }

        /// <summary>
        /// Recursion depth EXACTLY at MaxRecursionDepth should NOT throw — boundary test for L153.
        /// Uses SelfRefNode chain of 3 with MaxRecursionDepth=3.
        /// Level 1: diff=1, Level 2: diff=2, Level 3: diff=3, 3 > 3 = false → all succeed.
        /// Kills mutant: > → >= (L153) — with >=, diff=3 >= 3 would throw incorrectly.
        /// </summary>
        [Fact]
        public void RecursionDepth_Exactly_At_Limit_Succeeds()
        {
            Mapper.MaxRecursionDepth = 3;

            // Build a chain of 3 SelfRefNodes: 3 levels of recursive Map calls
            var chain = new SelfRefNode
            {
                Value = 3,
                Self = new SelfRefNode
                {
                    Value = 2,
                    Self = new SelfRefNode { Value = 1, Self = null }
                }
            };

            var dto = MapperBase<SelfRefNode, SelfRefNodeDto>.Map(chain);

            Assert.NotNull(dto);
            Assert.Equal(3, dto!.Value);
            Assert.NotNull(dto.Self);
            Assert.Equal(2, dto.Self!.Value);
            Assert.Equal(1, dto.Self.Self!.Value);
            Assert.Null(dto.Self.Self.Self);
        }

        /// <summary>
        /// Recursion depth exceeding MaxRecursionDepth SHOULD throw — verifies L153 condition.
        /// Uses circular reference (SelfRefNode.Self = SelfRefNode) with MaxRecursionDepth=2.
        /// The circular reference causes infinite recursive Map calls, hitting the depth limit.
        /// Kills mutant: throw statement removed (L155-159), > → >= (L153).
        /// </summary>
        [Fact]
        public void RecursionDepth_One_Over_Limit_Throws()
        {
            Mapper.MaxRecursionDepth = 2;

            // Circular reference: node.Self = node causes infinite recursion
            var node = new SelfRefNode { Value = 1 };
            node.Self = node;

            // Should throw MapException when depth exceeds 2
            var ex = Assert.Throws<MapException>(() =>
                MapperBase<SelfRefNode, SelfRefNodeDto>.Map(node));

            // Verify the exception message contains the depth limit
            Assert.Contains("2", ex.Message);
        }

        /// <summary>
        /// MapFunc property returns same delegate on repeated access — verifies lazy init at L41.
        /// Kills mutant: CoalesceAssignment removed for s_mapFunc.
        /// </summary>
        [Fact]
        public void MapFunc_LazyInit_Returns_Same_Delegate()
        {
            var func1 = MapperBase<FullTypeEntity, SameTypeDto>.MapFunc;
            var func2 = MapperBase<FullTypeEntity, SameTypeDto>.MapFunc;

            Assert.NotNull(func1);
            Assert.Same(func1, func2);
        }

        /// <summary>
        /// MapList with ICollection path and null elements — verifies L204-207 null handling.
        /// This specifically tests the ICollection path (not List fast path) with null elements.
        /// </summary>
        [Fact]
        public void MapList_ICollection_NullElements_Preserved()
        {
            // Use a HashSet to force ICollection path (not List fast path)
            var sources = new HashSet<FullTypeEntity>
            {
                new FullTypeEntity { IntProp = 5 },
            };

            // Can't easily add null to HashSet, so use a List with action to bypass fast path
            var list = new List<FullTypeEntity> { new FullTypeEntity { IntProp = 10 }, null!, new FullTypeEntity { IntProp = 20 } };

            // Use action to force ICollection path (bypasses List fast path)
            var result = MapperBase<FullTypeEntity, SameTypeDto>.MapList(list, (s, t) => { });

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.Equal(10, result[0].IntProp);
            Assert.Null(result[1]);
            Assert.Equal(20, result[2].IntProp);
        }

        /// <summary>
        /// MapList sequential calls with non-ICollection IEnumerable — verifies s_depthOut++ at L188.
        /// If s_depthOut++ removed, diff accumulates and eventually throws.
        /// Kills mutant: s_depthOut++ removed in slow path (L188).
        /// </summary>
        [Fact]
        public void MapList_NonICollection_Many_Sequential_Calls_Do_Not_Accumulate()
        {
            IEnumerable<FullTypeEntity> YieldOne(int val)
            {
                yield return new FullTypeEntity { IntProp = val };
            }

            for (int i = 0; i < 70; i++)
            {
                var result = MapperBase<FullTypeEntity, SameTypeDto>.MapList(YieldOne(i));
                Assert.NotNull(result);
                Assert.Single(result!);
                Assert.Equal(i, result[0].IntProp);
            }
        }
    }
}
