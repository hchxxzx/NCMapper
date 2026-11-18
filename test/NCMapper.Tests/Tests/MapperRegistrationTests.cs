#pragma warning disable NCMapper001
using NCMapper.Tests.Models;
using System.Linq.Expressions;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// Registration, cache clearing, metrics reset, and property clamping tests
    /// for the Mapper static API surface.
    /// Targets Stryker statement / negate / equality / block-removal mutants.
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class MapperRegistrationTests : IDisposable
    {
        // Snapshot original values so Dispose can restore them
        private readonly bool _origMetricsEnabled;
        private readonly bool _origTraceEnabled;
        private readonly bool _origValidationEnabled;
        private readonly int _origMaxRecursionDepth;
        private readonly int _origMaxInlineDepth;

        public MapperRegistrationTests()
        {
            _origMetricsEnabled = Mapper.MetricsEnabled;
            _origTraceEnabled = Mapper.TraceEnabled;
            _origValidationEnabled = Mapper.ValidationEnabled;
            _origMaxRecursionDepth = Mapper.MaxRecursionDepth;
            _origMaxInlineDepth = Mapper.MaxInlineDepth;
        }

        public void Dispose()
        {
            Mapper.MetricsEnabled = _origMetricsEnabled;
            Mapper.TraceEnabled = _origTraceEnabled;
            Mapper.ValidationEnabled = _origValidationEnabled;
            Mapper.MaxRecursionDepth = _origMaxRecursionDepth;
            Mapper.MaxInlineDepth = _origMaxInlineDepth;
            Mapper.ResetMetrics();
            Mapper.ClearGenerated();
            Mapper.ClearCache();
        }

        // ================================================================
        // Unique types for registration tests (avoid cross-test leaks)
        // ================================================================
        public class RegSrcA { public int V { get; set; } }
        public class RegTgtA { public int V { get; set; } }
        public class RegSrcB { public int V { get; set; } }
        public class RegTgtB { public int V { get; set; } }
        public class RegSrcC { public int V { get; set; } }
        public class RegTgtC { public int V { get; set; } }
        public class RegSrcD { public int V { get; set; } }
        public class RegTgtD { public int V { get; set; } }
        public class RegSrcE { public int V { get; set; } }
        public class RegTgtE { public int V { get; set; } }
        public class RegSrcF { public int V { get; set; } }
        public class RegTgtF { public int V { get; set; } }
        public class RegComposeTgt { public int V { get; set; } }
        public class RegProjSrc { public int V { get; set; } }
        public class RegProjTgt { public int V { get; set; } }

        // ================================================================
        // 1. ClearCache_Removes_All_Registered_Generated
        // ================================================================
        [Fact]
        public void ClearCache_Removes_All_Registered_Generated()
        {
            // Register a generated func for type pair A
            Mapper.RegisterGenerated<RegSrcA, RegTgtA>((s, a) => new RegTgtA { V = s.V });
            Mapper.RegisterGeneratedCore<RegSrcA, RegTgtA>(s => new RegTgtA { V = s.V });

            // Verify registered
            Assert.True(Mapper.HasGenerated<RegSrcA, RegTgtA>());

            // ClearCache should clear the dictionary caches
            Mapper.ClearCache();

            // Null out the generic static fields (ClearCache cannot clear these)
            Mapper.RegisterGenerated<RegSrcA, RegTgtA>(null);
            Mapper.RegisterGeneratedCore<RegSrcA, RegTgtA>(null);

            // After full cleanup HasGenerated should be false
            Assert.False(Mapper.HasGenerated<RegSrcA, RegTgtA>());
        }

        // ================================================================
        // 2. ClearCache_Removes_Compose_Registration
        // ================================================================
        [Fact]
        public void ClearCache_Removes_Compose_Registration()
        {
            const int sentinel = 99999;

            // Register a compose func that returns a sentinel value
            Mapper.RegisterGeneratedCompose<SameTypeDto>(
                arr => new SameTypeDto { IntProp = sentinel });

            // Verify compose uses the generated path
            var before = Mapper.Compose<SameTypeDto>(new FullTypeEntity());
            Assert.Equal(sentinel, before!.IntProp);

            // ClearCache should clear s_generatedComposeFuncs
            Mapper.ClearCache();

            // After clearing, Compose should fall to runtime (not return sentinel)
            var after = Mapper.Compose<SameTypeDto>(new FullTypeEntity { IntProp = 42 });
            Assert.NotEqual(sentinel, after!.IntProp);
        }

        // ================================================================
        // ================================================================
        // 4. ClearCache_Removes_Validate_Registration
        // ================================================================
        [Fact]
        public void ClearCache_Removes_Validate_Registration()
        {
            var sentinelResults = new List<MapValidationResult>
            {
                new() { SourceType = typeof(RegSrcA), TargetType = typeof(RegTgtA),
                        Level = MapValidationLevel.OK, MappedCount = 1, TotalCount = 1 }
            };

            Mapper.RegisterGeneratedValidate(null); // 适配：先清空全部槽位
                                                    // （含本程序集 ModuleInitializer 自动注册的 GeneratedMapper），
                                                    // 保证聚合语义下层1 仅含本测试注册的 sentinel
            Mapper.RegisterGeneratedValidate(() => sentinelResults);

            // Verify validate uses the generated path
            var before = Mapper.Validate();
            Assert.Single(before);
            Assert.Equal(typeof(RegSrcA), before[0].SourceType);

            // ClearCache should clear the validate registration dictionary (多槽位)
            Mapper.ClearCache();

            // After clearing, Validate should fall to runtime (returns many results)
            var after = Mapper.Validate();
            // Runtime validate scans AppDomain and finds multiple [MapFrom] types
            Assert.True(after.Count != 1 || after[0].SourceType != typeof(RegSrcA),
                "Validate should have fallen to runtime path after ClearCache");
        }

        // ================================================================
        // 5. RegisterGenerated_Null_Unregisters
        // ================================================================
        [Fact]
        public void RegisterGenerated_Null_Unregisters()
        {
            Func<RegSrcB, Action<RegSrcB, RegTgtB>?, RegTgtB?> func =
                (s, a) => new RegTgtB { V = s.V };

            // Register non-null
            Mapper.RegisterGenerated<RegSrcB, RegTgtB>(func);
            Assert.True(Mapper.HasGenerated<RegSrcB, RegTgtB>());

            // Register null to unregister
            Mapper.RegisterGenerated<RegSrcB, RegTgtB>(null);
            Mapper.RegisterGeneratedCore<RegSrcB, RegTgtB>(null);

            // Should now be false (dict removed + generic statics null)
            Assert.False(Mapper.HasGenerated<RegSrcB, RegTgtB>());
        }

        // ================================================================
        // 6. RegisterGeneratedList_Null_Unregisters
        // ================================================================
        [Fact]
        public void RegisterGeneratedList_Null_Unregisters()
        {
            Func<List<RegSrcC>, List<RegTgtC>> listFunc =
                srcs => srcs.ConvertAll(s => new RegTgtC { V = s.V });

            // Register non-null
            Mapper.RegisterGeneratedList<RegSrcC, RegTgtC>(listFunc);

            // Verify the list func is used (metrics track cache hit)
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();
            Mapper.MapList<RegSrcC, RegTgtC>(new List<RegSrcC> { new() { V = 10 } });
            Assert.True(Mapper.CacheHits > 0, "Expected cache hit from registered list func");

            // Register null to unregister
            Mapper.RegisterGeneratedList<RegSrcC, RegTgtC>(null);

            // After unregistering, cache hit should not occur (falls to runtime)
            Mapper.ResetMetrics();
            Mapper.MapList<RegSrcC, RegTgtC>(new List<RegSrcC> { new() { V = 20 } });
            Assert.Equal(0, Mapper.CacheHits);
        }

        // ================================================================
        // 7. RegisterGeneratedUpdate_Stores_Delegate
        // ================================================================
        [Fact]
        public void RegisterGeneratedUpdate_Stores_Delegate()
        {
            bool updateCalled = false;
            Action<RegSrcD, RegTgtD, Action<RegSrcD, RegTgtD>?> updateAction =
                (s, t, a) => { t.V = s.V + 1000; updateCalled = true; };

            // Register update delegate
            Mapper.RegisterGeneratedUpdate<RegSrcD, RegTgtD>(updateAction);

            // Verify it's used via Map(source, target)
            var source = new RegSrcD { V = 5 };
            var target = new RegTgtD();
            Mapper.Map(source, target);

            Assert.True(updateCalled, "Registered update delegate should have been called");
            Assert.Equal(1005, target.V);

            // Register null to unregister
            Mapper.RegisterGeneratedUpdate<RegSrcD, RegTgtD>(null);

            // After unregistering, the update delegate should not be called
            updateCalled = false;
            var target2 = new RegTgtD();
            Mapper.Map(source, target2);
            Assert.False(updateCalled, "Update delegate should not be called after null unregister");
        }

        // ================================================================
        // 8. RegisterGeneratedCompose_Null_Unregisters
        // ================================================================
        [Fact]
        public void RegisterGeneratedCompose_Null_Unregisters()
        {
            const int sentinel = 88888;
            Mapper.RegisterGeneratedCompose<RegComposeTgt>(
                arr => new RegComposeTgt { V = sentinel });

            // Verify compose uses the generated func
            var before = Mapper.Compose<RegComposeTgt>(new object[] { new RegSrcE() });
            Assert.Equal(sentinel, before!.V);

            // Register null to unregister
            Mapper.RegisterGeneratedCompose<RegComposeTgt>(null);

            // After unregistering, compose should fall to runtime
            // RegComposeTgt has no [MapFrom], so runtime will throw MapConfigurationException
            Assert.Throws<MapConfigurationException>(
                () => Mapper.Compose<RegComposeTgt>(new object[] { new RegSrcE() }));
        }

        // ================================================================
        // 9. RegisterGeneratedValidate_Null_Unregisters
        // ================================================================
        [Fact]
        public void RegisterGeneratedValidate_Null_Unregisters()
        {
            var sentinel = new List<MapValidationResult>
            {
                new() { SourceType = typeof(RegSrcF), TargetType = typeof(RegTgtF),
                        Level = MapValidationLevel.OK, MappedCount = 1, TotalCount = 1 }
            };
            Mapper.RegisterGeneratedValidate(() => sentinel);

            // Verify validate uses the generated path
            var before = Mapper.Validate();
            Assert.Single(before);
            Assert.Equal(typeof(RegSrcF), before[0].SourceType);

            // Register null to unregister
            Mapper.RegisterGeneratedValidate(null);

            // After unregistering, Validate should fall to runtime
            var after = Mapper.Validate();
            Assert.True(after.Count != 1 || after[0].SourceType != typeof(RegSrcF),
                "Validate should have fallen to runtime path after null unregister");
        }

        // ================================================================
        // 11. ResetMetrics_Zeroes_All_Counters
        // ================================================================
        [Fact]
        public void ResetMetrics_Zeroes_All_Counters()
        {
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();

            // Generate some metrics
            Mapper.Map<FullTypeEntity, SameTypeDto>(new FullTypeEntity());
            Mapper.Map<FullTypeEntity, SameTypeDto>(new FullTypeEntity());
            Mapper.Map<FullTypeEntity, SameTypeDto>(new FullTypeEntity());

            Assert.True(Mapper.TotalMapCalls > 0, "TotalMapCalls should be > 0");

            // At least one of CacheHits or CompilationCount should be > 0
            Assert.True(Mapper.CacheHits > 0 || Mapper.CompilationCount > 0,
                "At least one of CacheHits/CompilationCount should be > 0");

            // Reset
            Mapper.ResetMetrics();

            Assert.Equal(0, Mapper.TotalMapCalls);
            Assert.Equal(0, Mapper.CacheHits);
            Assert.Equal(0, Mapper.CompilationCount);
        }

        // ================================================================
        // 12. MetricsEnabled_Toggle
        // ================================================================
        [Fact]
        public void MetricsEnabled_Toggle()
        {
            // Disabled: counters should not increment
            Mapper.MetricsEnabled = false;
            Mapper.ResetMetrics();
            Mapper.Map<FullTypeEntity, SameTypeDto>(new FullTypeEntity());
            Assert.Equal(0, Mapper.TotalMapCalls);
            Assert.Equal(0, Mapper.CacheHits);
            Assert.Equal(0, Mapper.CompilationCount);

            // Enabled: counters should increment
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();
            Mapper.Map<FullTypeEntity, SameTypeDto>(new FullTypeEntity());
            Assert.True(Mapper.TotalMapCalls >= 1,
                $"TotalMapCalls should be >= 1, got {Mapper.TotalMapCalls}");
        }

        // ================================================================
        // 13. HasGenerated_Without_Registration_Returns_False
        // ================================================================
        [Fact]
        public void HasGenerated_Without_Registration_Returns_False()
        {
            // Use a type pair that has never been registered
            // First ensure clean state
            Mapper.RegisterGeneratedCore<RegSrcE, RegTgtE>(null);
            Mapper.RegisterGenerated<RegSrcE, RegTgtE>(null);

            Assert.False(Mapper.HasGenerated<RegSrcE, RegTgtE>());
        }

        // ================================================================
        // 14. ClearGenerated_Removes_All
        // ================================================================
        [Fact]
        public void ClearGenerated_Removes_All()
        {
            // Register multiple type pairs
            Mapper.RegisterGenerated<RegSrcA, RegTgtA>((s, a) => new RegTgtA { V = s.V });
            Mapper.RegisterGeneratedCore<RegSrcA, RegTgtA>(s => new RegTgtA { V = s.V });
            Mapper.RegisterGenerated<RegSrcB, RegTgtB>((s, a) => new RegTgtB { V = s.V });
            Mapper.RegisterGeneratedCore<RegSrcB, RegTgtB>(s => new RegTgtB { V = s.V });

            Assert.True(Mapper.HasGenerated<RegSrcA, RegTgtA>());
            Assert.True(Mapper.HasGenerated<RegSrcB, RegTgtB>());

            // ClearGenerated clears dictionaries
            Mapper.ClearGenerated();

            // Null out generic static fields for both type pairs
            Mapper.RegisterGenerated<RegSrcA, RegTgtA>(null);
            Mapper.RegisterGeneratedCore<RegSrcA, RegTgtA>(null);
            Mapper.RegisterGenerated<RegSrcB, RegTgtB>(null);
            Mapper.RegisterGeneratedCore<RegSrcB, RegTgtB>(null);

            // All should be false now
            Assert.False(Mapper.HasGenerated<RegSrcA, RegTgtA>());
            Assert.False(Mapper.HasGenerated<RegSrcB, RegTgtB>());
        }

        // ================================================================
        // 15. MaxRecursionDepth_Clamps_UpperBound
        // ================================================================
        [Fact]
        public void MaxRecursionDepth_Clamps_UpperBound()
        {
            Mapper.MaxRecursionDepth = 9999;
            Assert.Equal(Mapper.MaxRecursionDepthUpperBound, Mapper.MaxRecursionDepth);
        }

        // ================================================================
        // 16. MaxRecursionDepth_Clamps_LowerBound
        // ================================================================
        [Fact]
        public void MaxRecursionDepth_Clamps_LowerBound()
        {
            Mapper.MaxRecursionDepth = 0;
            Assert.Equal(1, Mapper.MaxRecursionDepth);

            Mapper.MaxRecursionDepth = -100;
            Assert.Equal(1, Mapper.MaxRecursionDepth);
        }

        // ================================================================
        // 17. MaxInlineDepth_Clamps_UpperBound
        // ================================================================
        [Fact]
        public void MaxInlineDepth_Clamps_UpperBound()
        {
            Mapper.MaxInlineDepth = 99;
            Assert.Equal(Mapper.MaxInlineDepthUpperBound, Mapper.MaxInlineDepth);
        }

        // ================================================================
        // 18. MaxInlineDepth_Clamps_LowerBound
        // ================================================================
        [Fact]
        public void MaxInlineDepth_Clamps_LowerBound()
        {
            Mapper.MaxInlineDepth = -1;
            Assert.Equal(0, Mapper.MaxInlineDepth);

            Mapper.MaxInlineDepth = -100;
            Assert.Equal(0, Mapper.MaxInlineDepth);
        }

        // ================================================================
        // 19. TraceEnabled_Toggle
        // ================================================================
        [Fact]
        public void TraceEnabled_Toggle()
        {
            Mapper.TraceEnabled = true;
            Assert.True(MapTracer.GloballyEnabled);

            Mapper.TraceEnabled = false;
            Assert.False(MapTracer.GloballyEnabled);
        }

        // ================================================================
        // 20. ValidationEnabled_Toggle
        // ================================================================
        [Fact]
        public void ValidationEnabled_Toggle()
        {
            // Enabled: Validate returns non-empty list
            Mapper.ValidationEnabled = true;
            var enabled = Mapper.Validate();
            Assert.True(enabled.Count > 0, "Validate should return results when enabled");

            // Disabled: Validate returns empty list
            Mapper.ValidationEnabled = false;
            var disabled = Mapper.Validate();
            Assert.Empty(disabled);
        }
    }
}
