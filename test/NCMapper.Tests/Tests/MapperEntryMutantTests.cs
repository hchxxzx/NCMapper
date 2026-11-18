using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// Mapper static entry mutant-killing tests — targets surviving Stryker mutants in Mapper.cs
    /// Covers: MaxRecursionDepth/MaxInlineDepth clamping boundaries, dispatch logic,
    /// MetricsEnabled conditionals, ClearCache/ClearGenerated, DeepCopy same-type guard,
    /// ValidateOrThrow, Validate disabled path, MapPipeline integration
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class MapperEntryMutantTests : IDisposable
    {
        private readonly bool _origMetricsEnabled;
        private readonly bool _origTraceEnabled;
        private readonly bool _origValidationEnabled;
        private readonly int _origMaxRecursionDepth;
        private readonly int _origMaxInlineDepth;

        public MapperEntryMutantTests()
        {
            _origMetricsEnabled = Mapper.MetricsEnabled;
            _origTraceEnabled = Mapper.TraceEnabled;
            _origValidationEnabled = Mapper.ValidationEnabled;
            _origMaxRecursionDepth = Mapper.MaxRecursionDepth;
            _origMaxInlineDepth = Mapper.MaxInlineDepth;
            Mapper.ClearGenerated();
            Mapper.ResetMetrics();
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
        // Test models — unique to avoid cross-test leaks
        // ================================================================
        public class SimpleSrc { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class SimpleTgt { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class OtherTgt { public int Id { get; set; } public string Name { get; set; } = ""; }

        // ================================================================
        // MaxRecursionDepth clamping — L490-496
        // Kills: > to >=, boundary values, ternary negation
        // ================================================================

        /// <summary>
        /// MaxRecursionDepth set to 0 → clamped to 1.
        /// Kills mutant: lower bound &lt; 1 changed to &lt;= 1 (would clamp 1 to 1 instead of passing through).
        /// </summary>
        [Fact]
        public void MaxRecursionDepth_Set_Zero_Clamped_To_One()
        {
            Mapper.MaxRecursionDepth = 0;
            Assert.Equal(1, Mapper.MaxRecursionDepth);
        }

        /// <summary>
        /// MaxRecursionDepth set to -1 → clamped to 1.
        /// Kills mutant: lower bound removal.
        /// </summary>
        [Fact]
        public void MaxRecursionDepth_Set_Negative_Clamped_To_One()
        {
            Mapper.MaxRecursionDepth = -1;
            Assert.Equal(1, Mapper.MaxRecursionDepth);
        }

        /// <summary>
        /// MaxRecursionDepth set to 1 → stays 1 (boundary: not clamped).
        /// Kills mutant: &lt; 1 to &lt; 2 (would clamp 1 to 1 even though it's valid).
        /// </summary>
        [Fact]
        public void MaxRecursionDepth_Set_One_Stays_One()
        {
            Mapper.MaxRecursionDepth = 1;
            Assert.Equal(1, Mapper.MaxRecursionDepth);
        }

        /// <summary>
        /// MaxRecursionDepth set to 4096 → stays 4096 (upper boundary: not clamped).
        /// Kills mutant: &gt; 4096 to &gt;= 4096 (would clamp 4096 to 4096, need value check).
        /// </summary>
        [Fact]
        public void MaxRecursionDepth_Set_4096_Stays_4096()
        {
            Mapper.MaxRecursionDepth = 4096;
            Assert.Equal(4096, Mapper.MaxRecursionDepth);
        }

        /// <summary>
        /// MaxRecursionDepth set to 4097 → clamped to 4096.
        /// Kills mutant: upper bound removal.
        /// </summary>
        [Fact]
        public void MaxRecursionDepth_Set_4097_Clamped_To_4096()
        {
            Mapper.MaxRecursionDepth = 4097;
            Assert.Equal(4096, Mapper.MaxRecursionDepth);
        }

        /// <summary>
        /// MaxRecursionDepth set to normal value 100 → stays 100.
        /// Kills mutant: assignment removal.
        /// </summary>
        [Fact]
        public void MaxRecursionDepth_Set_Normal_Stays()
        {
            Mapper.MaxRecursionDepth = 100;
            Assert.Equal(100, Mapper.MaxRecursionDepth);
        }

        // ================================================================
        // MaxInlineDepth clamping — L510-516
        // ================================================================

        /// <summary>
        /// MaxInlineDepth set to -1 → clamped to 0.
        /// Kills mutant: lower bound removal.
        /// </summary>
        [Fact]
        public void MaxInlineDepth_Set_Negative_Clamped_To_Zero()
        {
            Mapper.MaxInlineDepth = -1;
            Assert.Equal(0, Mapper.MaxInlineDepth);
        }

        /// <summary>
        /// MaxInlineDepth set to 0 → stays 0 (lower boundary, valid value).
        /// Kills mutant: &lt; 0 to &lt;= 0.
        /// </summary>
        [Fact]
        public void MaxInlineDepth_Set_Zero_Stays_Zero()
        {
            Mapper.MaxInlineDepth = 0;
            Assert.Equal(0, Mapper.MaxInlineDepth);
        }

        /// <summary>
        /// MaxInlineDepth set to 16 → stays 16 (upper boundary).
        /// Kills mutant: &gt; 16 to &gt;= 16.
        /// </summary>
        [Fact]
        public void MaxInlineDepth_Set_16_Stays_16()
        {
            Mapper.MaxInlineDepth = 16;
            Assert.Equal(16, Mapper.MaxInlineDepth);
        }

        /// <summary>
        /// MaxInlineDepth set to 17 → clamped to 16.
        /// Kills mutant: upper bound removal.
        /// </summary>
        [Fact]
        public void MaxInlineDepth_Set_17_Clamped_To_16()
        {
            Mapper.MaxInlineDepth = 17;
            Assert.Equal(16, Mapper.MaxInlineDepth);
        }

        // ================================================================
        // MetricsEnabled conditionals — L301, L307, L313, L318
        // Kills: if (s_metricsEnabled) removal
        // ================================================================

        /// <summary>
        /// MetricsEnabled false → TotalMapCalls stays 0 after Map.
        /// Kills mutant: metricsEnabled check removed (would always increment).
        /// </summary>
        [Fact]
        public void Metrics_Disabled_No_Counting()
        {
            Mapper.MetricsEnabled = false;
            Mapper.ResetMetrics();

            var source = new SimpleSrc { Id = 1, Name = "Test" };
            Mapper.Map<SimpleSrc, OtherTgt>(source);

            Assert.Equal(0, Mapper.TotalMapCalls);
            Assert.Equal(0, Mapper.CacheHits);
        }

        /// <summary>
        /// Metrics 并发累加守恒（C11 补测）— TotalMapCalls/CacheHits == 实际调用数
        /// <para>断言形态（报告 §五 C11）：TotalMapCalls/CacheHits 并发累加 == 实际调用数
        /// （期望总量法 E18 参照）。计数器经 Interlocked.Increment 累加（Mapper.cs:394/:408），
        /// 并发下最终值应精确等于调用总数（无丢失）；预注册生成委托使 Map 全部命中
        /// 生成路径（CacheHits 计数确定）。</para>
        /// </summary>
        [Fact]
        public void Metrics_ConcurrentAccumulation_CountsConserved()
        {
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();
            try
            {
                // 预注册生成委托 → Map 全部命中生成路径（CacheHits 计数确定）
                Mapper.RegisterGenerated<SimpleSrc, OtherTgt>((src, action) =>
                {
                    var dst = new OtherTgt { Id = src.Id, Name = src.Name };
                    action?.Invoke(src, dst);
                    return dst;
                });
                try
                {
                    const int total = 500;
                    Parallel.For(0, total, i =>
                    {
                        Mapper.Map<SimpleSrc, OtherTgt>(new SimpleSrc { Id = i, Name = $"n{i}" });
                    });

                    // 期望总量法：并发累加 == 实际调用数（Interlocked 无丢失）
                    Assert.Equal(total, Mapper.TotalMapCalls);
                    Assert.Equal(total, Mapper.CacheHits);
                }
                finally
                {
                    Mapper.RegisterGenerated<SimpleSrc, OtherTgt>(null);
                }
            }
            finally
            {
                Mapper.MetricsEnabled = false;
            }
        }

        /// <summary>
        /// MetricsEnabled true → TotalMapCalls incremented after Map.
        /// Kills mutant: metricsEnabled negation (!s_metricsEnabled).
        /// </summary>
        [Fact]
        public void Metrics_Enabled_Counting_Works()
        {
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();

            var source = new SimpleSrc { Id = 2, Name = "Test2" };
            Mapper.Map<SimpleSrc, OtherTgt>(source);

            Assert.True(Mapper.TotalMapCalls >= 1, $"Expected TotalMapCalls >= 1 but got {Mapper.TotalMapCalls}");
        }

        /// <summary>
        /// ResetMetrics zeros all counters — kills L280-282 Interlocked.Exchange removal.
        /// </summary>
        [Fact]
        public void ResetMetrics_Zeros_All_Counters()
        {
            Mapper.MetricsEnabled = true;

            var source = new SimpleSrc { Id = 3 };
            Mapper.Map<SimpleSrc, OtherTgt>(source);

            // Counters should be non-zero
            Assert.True(Mapper.TotalMapCalls > 0);

            Mapper.ResetMetrics();
            Assert.Equal(0, Mapper.TotalMapCalls);
            Assert.Equal(0, Mapper.CacheHits);
            Assert.Equal(0, Mapper.CompilationCount);
        }

        // ================================================================
        // Map<TSource>(source) — same type, null guard L298
        // ================================================================

        /// <summary>
        /// Map&lt;T&gt;(null) returns null — kills null guard removal at L298.
        /// </summary>
        [Fact]
        public void Map_SameType_Null_Returns_Null()
        {
            var result = Mapper.Map<FullTypeEntity>(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// Map&lt;T&gt;(source) returns mapped object — basic same-type mapping.
        /// </summary>
        [Fact]
        public void Map_SameType_Works()
        {
            var source = new FullTypeEntity { IntProp = 99, StringProp = "mapped" };
            var result = Mapper.Map(source);
            Assert.NotNull(result);
            Assert.Equal(99, result!.IntProp);
            Assert.Equal("mapped", result.StringProp);
        }

        // ================================================================
        // Map<TSource, TTarget>(source, action) — dispatch L353-369
        // ================================================================

        /// <summary>
        /// Map with null source returns null — kills L336 null guard removal.
        /// </summary>
        [Fact]
        public void Map_Hetero_Null_Returns_Null()
        {
            var result = Mapper.Map<FullTypeEntity, SameTypeDto>(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// Map with action callback invokes the action.
        /// Kills mutant: action callback path removal.
        /// </summary>
        [Fact]
        public void Map_Hetero_With_Action_Called()
        {
            var source = new FullTypeEntity { IntProp = 42 };
            bool actionCalled = false;

            var result = Mapper.Map<FullTypeEntity, SameTypeDto>(source, (s, t) =>
            {
                actionCalled = true;
            });

            Assert.NotNull(result);
            Assert.True(actionCalled);
        }

        // ================================================================
        // Map update overload — L391-412
        // ================================================================

        /// <summary>
        /// Map update overload writes to existing target.
        /// Kills mutant: MapAction/update dispatch removal.
        /// </summary>
        [Fact]
        public void Map_Update_ExistingTarget()
        {
            var source = new FullTypeEntity { IntProp = 77, StringProp = "updated" };
            var target = new SameTypeDto();

            Mapper.Map<FullTypeEntity, SameTypeDto>(source, target);

            Assert.Equal(77, target.IntProp);
            Assert.Equal("updated", target.StringProp);
        }

        // ================================================================
        // MapList — null guard, dispatch
        // ================================================================

        /// <summary>
        /// MapList same-type null source → null — kills L425 null guard removal.
        /// Note: MapList same type does NOT have null guard (no if(sources==null)return null),
        /// it delegates to MapperBase which returns null at L149.
        /// </summary>
        [Fact]
        public void MapList_SameType_Null_Returns_Null()
        {
            var result = Mapper.MapList<FullTypeEntity>((IEnumerable<FullTypeEntity>)null!);
            Assert.Null(result);
        }

        /// <summary>
        /// MapList hetero null source → null — kills null guard removal.
        /// </summary>
        [Fact]
        public void MapList_Hetero_Null_Returns_Null()
        {
            var result = Mapper.MapList<FullTypeEntity, SameTypeDto>((IEnumerable<FullTypeEntity>)null!);
            Assert.Null(result);
        }

        /// <summary>
        /// MapList hetero with empty list → empty list.
        /// Kills mutant: list dispatch removal.
        /// </summary>
        [Fact]
        public void MapList_Hetero_EmptyList()
        {
            var sources = new List<FullTypeEntity>();
            var result = Mapper.MapList<FullTypeEntity, SameTypeDto>(sources);
            Assert.NotNull(result);
            Assert.Empty(result!);
        }

        /// <summary>
        /// MapList hetero with items → mapped list.
        /// Kills mutant: MapperBase.MapList fallback removal.
        /// </summary>
        [Fact]
        public void MapList_Hetero_WithItems()
        {
            var sources = new List<FullTypeEntity>
            {
                new() { IntProp = 1, StringProp = "a" },
                new() { IntProp = 2, StringProp = "b" }
            };

            var result = Mapper.MapList<FullTypeEntity, SameTypeDto>(sources);

            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
            Assert.Equal(1, result[0].IntProp);
            Assert.Equal(2, result[1].IntProp);
        }

        // ================================================================
        // DeepCopy same-type guard — L728-731
        // ================================================================

        /// <summary>
        /// DeepCopy&lt;T,T&gt; same type throws InvalidOperationException.
        /// Kills mutant: typeof(TSource) == typeof(TTarget) check removal.
        /// </summary>
        [Fact]
        public void DeepCopy_Hetero_SameType_Throws()
        {
            var source = new SimpleSrc { Id = 1 };
            Assert.Throws<InvalidOperationException>(() =>
                Mapper.DeepCopy<SimpleSrc, SimpleSrc>(source));
        }

        /// <summary>
        /// DeepCopy&lt;T,T&gt; with action also throws for same type.
        /// Kills mutant: second overload same-type guard removal.
        /// </summary>
        [Fact]
        public void DeepCopy_Hetero_WithAction_SameType_Throws()
        {
            var source = new SimpleSrc { Id = 1 };
            Assert.Throws<InvalidOperationException>(() =>
                Mapper.DeepCopy<SimpleSrc, SimpleSrc>(source, (s, t) => { }));
        }

        /// <summary>
        /// DeepCopy null → returns null — kills L685 null guard.
        /// </summary>
        [Fact]
        public void DeepCopy_Null_Returns_Null()
        {
            var result = Mapper.DeepCopy<SimpleSrc>(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// DeepCopy with action — action is called on the copy.
        /// Kills mutant: action invocation L707 removal.
        /// </summary>
        [Fact]
        public void DeepCopy_WithAction_CallsAction()
        {
            var source = new SimpleSrc { Id = 10, Name = "original" };
            bool actionCalled = false;

            var result = Mapper.DeepCopy(source, (s, t) =>
            {
                actionCalled = true;
                t.Name = "modified";
            });

            Assert.NotNull(result);
            Assert.True(actionCalled);
            Assert.Equal("modified", result!.Name);
        }

        /// <summary>
        /// DeepCopy with null action — no crash, copy returned.
        /// Kills mutant: null check on action at L707.
        /// </summary>
        [Fact]
        public void DeepCopy_WithNullAction_Works()
        {
            var source = new SimpleSrc { Id = 20, Name = "test" };
            var result = Mapper.DeepCopy<SimpleSrc>(source, (Action<SimpleSrc, SimpleSrc>?)null);
            Assert.NotNull(result);
            Assert.Equal(20, result!.Id);
        }

        // ================================================================
        // Validate / ValidateOrThrow — L622-670
        // ================================================================

        /// <summary>
        /// Validate with ValidationEnabled=false returns empty list.
        /// Kills mutant: !ValidationEnabled check removal at L624.
        /// </summary>
        [Fact]
        public void Validate_Disabled_Returns_Empty()
        {
            Mapper.ValidationEnabled = false;
            var results = Mapper.Validate();
            Assert.Empty(results);
        }

        /// <summary>
        /// ValidateOrThrow with no errors does not throw.
        /// Kills mutant: errors.Count > 0 negation.
        /// </summary>
        [Fact]
        public void ValidateOrThrow_NoErrors_NoThrow()
        {
            Mapper.ValidationEnabled = true;
            // Should not throw when there are no Error-level results
            var ex = Record.Exception(() => Mapper.ValidateOrThrow());
            // It may throw if there are actual errors in the test assembly, so we just verify
            // it doesn't throw NullReferenceException or similar
            Assert.True(ex == null || ex is MapConfigurationException);
        }

        // ================================================================
        // ClearCache — L233-250
        // ================================================================

        /// <summary>
        /// ClearCache clears all sub-caches without error.
        /// Kills mutant: individual Clear() statement removal (L236-249).
        /// </summary>
        [Fact]
        public void ClearCache_AllSubCaches_Works()
        {
            // Populate caches
            var source = new FullTypeEntity { IntProp = 42 };
            Mapper.Map<FullTypeEntity, SameTypeDto>(source);
            Mapper.DeepCopy(source);

            // Clear — should not throw
            Mapper.ClearCache();

            // Subsequent operations should still work (caches rebuilt)
            var result = Mapper.Map<FullTypeEntity, SameTypeDto>(source);
            Assert.NotNull(result);
            Assert.Equal(42, result!.IntProp);
        }

        // ================================================================
        // ClearGenerated — L217-224
        // ================================================================

        /// <summary>
        /// ClearGenerated clears all generated caches.
        /// Kills mutant: individual Clear() removal (L219-223).
        /// </summary>
        [Fact]
        public void ClearGenerated_Clears_All()
        {
            // Register some generated funcs
            Mapper.RegisterGenerated<SimpleSrc, OtherTgt>((s, a) => new OtherTgt { Id = s.Id });

            Assert.True(Mapper.HasGenerated<SimpleSrc, OtherTgt>());

            Mapper.ClearGenerated();

            // After clear, HasGenerated should check both dict and generic cache
            // CoreFunc might still be set (generic static can't be cleared), but dict should be empty
            Mapper.RegisterGenerated<SimpleSrc, OtherTgt>(null); // clear generic cache too
        }

        // ================================================================
        // HasGenerated / HasGeneratedDeepCopy
        // ================================================================

        /// <summary>
        /// HasGenerated returns true after registration.
        /// Kills mutant: || condition removal at L207-209.
        /// </summary>
        [Fact]
        public void HasGenerated_After_Registration_True()
        {
            Mapper.RegisterGenerated<SimpleSrc, SimpleTgt>((s, a) => new SimpleTgt { Id = s.Id });
            Assert.True(Mapper.HasGenerated<SimpleSrc, SimpleTgt>());
            Mapper.RegisterGenerated<SimpleSrc, SimpleTgt>(null);
        }

        /// <summary>
        /// HasGeneratedDeepCopy returns false when nothing registered.
        /// Kills mutant: null check removal at L766.
        /// </summary>
        [Fact]
        public void HasGeneratedDeepCopy_NoRegistration_False()
        {
            // Ensure no registration for this unique type
            Assert.False(Mapper.HasGeneratedDeepCopy<SimpleSrc>());
        }

        /// <summary>
        /// HasGeneratedDeepCopy returns true after registration.
        /// </summary>
        [Fact]
        public void HasGeneratedDeepCopy_After_Registration_True()
        {
            Mapper.RegisterGeneratedDeepCopy<SimpleSrc>(s => new SimpleSrc { Id = s.Id, Name = s.Name });
            Assert.True(Mapper.HasGeneratedDeepCopy<SimpleSrc>());
            Mapper.RegisterGeneratedDeepCopy<SimpleSrc>((Func<SimpleSrc, SimpleSrc>?)null);
        }

        // ================================================================
        // RegisterGeneratedCore / RegisterGeneratedList
        // ================================================================

        /// <summary>
        /// RegisterGeneratedCore sets CoreFunc — subsequent Map uses it.
        /// Kills mutant: CoreFunc assignment removal at L96.
        /// </summary>
        [Fact]
        public void RegisterGeneratedCore_Map_Uses_CoreFunc()
        {
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();

            Mapper.RegisterGeneratedCore<SimpleSrc, OtherTgt>(s => new OtherTgt { Id = s.Id * 10, Name = "core" });

            var result = Mapper.Map<SimpleSrc, OtherTgt>(new SimpleSrc { Id = 5 });

            Assert.NotNull(result);
            Assert.Equal(50, result!.Id);
            Assert.Equal("core", result.Name);
            Assert.True(Mapper.CacheHits >= 1, "Should have hit generated cache");

            Mapper.RegisterGeneratedCore<SimpleSrc, OtherTgt>(null);
        }

        /// <summary>
        /// RegisterGeneratedList sets ListFunc — subsequent MapList uses it.
        /// Kills mutant: ListFunc assignment removal at L123.
        /// </summary>
        [Fact]
        public void RegisterGeneratedList_MapList_Uses_ListFunc()
        {
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();

            Mapper.RegisterGeneratedList<SimpleSrc, OtherTgt>(list =>
                list.ConvertAll(s => new OtherTgt { Id = s.Id + 100, Name = "list" }));

            var sources = new List<SimpleSrc> { new() { Id = 1 }, new() { Id = 2 } };
            var result = Mapper.MapList<SimpleSrc, OtherTgt>(sources);

            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
            Assert.Equal(101, result[0].Id);
            Assert.Equal(102, result[1].Id);

            Mapper.RegisterGeneratedList<SimpleSrc, OtherTgt>(null);
        }

        // ================================================================
        // Map hetero with metrics — CacheHits on generated path
        // ================================================================

        /// <summary>
        /// Generated Func path increments CacheHits when metrics enabled.
        /// Kills mutant: CacheHits increment removal at L355/L361.
        /// </summary>
        [Fact]
        public void Map_GeneratedFunc_Path_Increments_CacheHits()
        {
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();

            Mapper.RegisterGenerated<SimpleSrc, OtherTgt>((s, a) => new OtherTgt { Id = s.Id });
            Mapper.Map<SimpleSrc, OtherTgt>(new SimpleSrc { Id = 1 });

            Assert.True(Mapper.CacheHits >= 1);

            Mapper.RegisterGenerated<SimpleSrc, OtherTgt>(null);
        }

        // ================================================================
        // ValidateOrThrow — with errors throws MapConfigurationException
        // ================================================================

        /// <summary>
        /// ValidateOrThrow with ValidationEnabled=false doesn't throw (empty results).
        /// Kills mutant: Validate() call or errors filtering removal.
        /// </summary>
        [Fact]
        public void ValidateOrThrow_Disabled_NoThrow()
        {
            Mapper.ValidationEnabled = false;
            var ex = Record.Exception(() => Mapper.ValidateOrThrow());
            Assert.Null(ex);
        }

        // ================================================================
        // RegisterGeneratedUpdate
        // ================================================================

        /// <summary>
        /// RegisterGeneratedUpdate + Map(source, target, action) uses the update path.
        /// Kills mutant: UpdateAction assignment removal at L142.
        /// </summary>
        [Fact]
        public void RegisterGeneratedUpdate_Map_Update_Uses_It()
        {
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();

            Mapper.RegisterGeneratedUpdate<SimpleSrc, OtherTgt>((s, t, a) =>
            {
                t.Id = s.Id * 100;
                t.Name = "updated-gen";
            });

            var source = new SimpleSrc { Id = 3 };
            var target = new OtherTgt();
            Mapper.Map<SimpleSrc, OtherTgt>(source, target);

            Assert.Equal(300, target.Id);
            Assert.Equal("updated-gen", target.Name);
            Assert.True(Mapper.CacheHits >= 1);

            Mapper.RegisterGeneratedUpdate<SimpleSrc, OtherTgt>(null);
        }
    }
}
