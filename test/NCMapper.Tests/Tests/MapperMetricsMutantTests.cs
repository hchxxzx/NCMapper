using Xunit;
using NCMapper;

namespace NCMapper.Tests.Tests;

// Inline test models
public class SimpleSrc { public int Id { get; set; } public string Name { get; set; } = ""; }
public class SimpleTgt { public int Id { get; set; } public string Name { get; set; } = ""; }
public class OtherTgt { public int Id { get; set; } public string Name { get; set; } = ""; }

/// <summary>
/// Tests targeting surviving mutants in Mapper.cs — metrics counters, ClearGenerated,
/// ClearCache, MapTracer, boundary clamping, and pipeline condition.
/// </summary>
[Collection("DeepCopyGlobalState")]
public class MapperMetricsMutantTests : IDisposable
{
    // ── saved original state ──────────────────────────────────────────
    private readonly bool _origMetricsEnabled;
    private readonly bool _origGloballyEnabled;
    private readonly Action<string>? _origLogCallback;
    private readonly System.IO.TextWriter? _origGlobalOutput;
    private readonly int _origMaxRecursionDepth;
    private readonly int _origMaxInlineDepth;
    private readonly bool _origValidationEnabled;

    public MapperMetricsMutantTests()
    {
        _origMetricsEnabled = Mapper.MetricsEnabled;
        _origGloballyEnabled = MapTracer.GloballyEnabled;
        _origLogCallback = MapTracer.LogCallback;
        _origGlobalOutput = MapTracer.GlobalOutput;
        _origMaxRecursionDepth = Mapper.MaxRecursionDepth;
        _origMaxInlineDepth = Mapper.MaxInlineDepth;
        _origValidationEnabled = Mapper.ValidationEnabled;

        // Start each test from a clean baseline
        Mapper.MetricsEnabled = false;
        MapTracer.GloballyEnabled = false;
        MapTracer.LogCallback = null;
        Mapper.ResetMetrics();
    }

    public void Dispose()
    {
        // Restore global state
        Mapper.MetricsEnabled = _origMetricsEnabled;
        MapTracer.GloballyEnabled = _origGloballyEnabled;
        MapTracer.LogCallback = _origLogCallback;
        MapTracer.GlobalOutput = _origGlobalOutput!;
        Mapper.MaxRecursionDepth = _origMaxRecursionDepth;
        Mapper.MaxInlineDepth = _origMaxInlineDepth;
        Mapper.ValidationEnabled = _origValidationEnabled;

        // Clear all generated registrations used by tests
        Mapper.ClearGenerated();
        Mapper.RegisterGeneratedCore<SimpleSrc, SimpleTgt>(null);
        Mapper.RegisterGeneratedCore<SimpleSrc, SimpleSrc>(null);
        Mapper.RegisterGenerated<SimpleSrc, SimpleTgt>(null);
        Mapper.RegisterGenerated<SimpleSrc, SimpleSrc>(null);
        Mapper.RegisterGeneratedList<SimpleSrc, SimpleTgt>(null);
        Mapper.RegisterGeneratedList<SimpleSrc, SimpleSrc>(null);
        Mapper.RegisterGeneratedUpdate<SimpleSrc, SimpleTgt>(null);
        Mapper.RegisterGeneratedCompose<SimpleTgt>(null);
        Mapper.RegisterGeneratedValidate(null);

        // Clear pipeline and caches
        Mapper.Pipeline.Clear();
        Mapper.ResetMetrics();
    }

    // ================================================================
    // 1. ClearGenerated — statement mutations (L219-222)
    //    Each .Clear() is mutated to `;`. These tests verify that the
    //    corresponding dictionary is actually emptied.
    // ================================================================

    [Fact]
    public void ClearGenerated_CoreFunc_Cleared()
    {
        // RegisterGeneratedCore sets the generic static CoreFunc.
        // ClearGenerated cannot clear generic statics, so we null it afterwards.
        Mapper.RegisterGeneratedCore<SimpleSrc, SimpleTgt>(
            s => new SimpleTgt { Id = s.Id, Name = s.Name });
        Assert.True(Mapper.HasGenerated<SimpleSrc, SimpleTgt>());

        Mapper.ClearGenerated();
        Mapper.RegisterGeneratedCore<SimpleSrc, SimpleTgt>(null);

        Assert.False(Mapper.HasGenerated<SimpleSrc, SimpleTgt>());
    }

    [Fact]
    public void ClearGenerated_Func_Cleared()
    {
        // RegisterGenerated sets both GeneratedCache.Func AND s_generatedFuncs.
        // After ClearGenerated + null re-registration, HasGenerated must be false.
        Mapper.RegisterGenerated<SimpleSrc, SimpleTgt>(
            (s, a) => new SimpleTgt { Id = s.Id, Name = s.Name });
        Assert.True(Mapper.HasGenerated<SimpleSrc, SimpleTgt>());

        Mapper.ClearGenerated();
        Mapper.RegisterGenerated<SimpleSrc, SimpleTgt>(null);

        Assert.False(Mapper.HasGenerated<SimpleSrc, SimpleTgt>());
    }

    [Fact]
    public void ClearGenerated_ListFunc_Cleared()
    {
        Mapper.RegisterGeneratedList<SimpleSrc, SimpleTgt>(
            list => list.Select(s => new SimpleTgt { Id = s.Id, Name = s.Name }).ToList());

        Mapper.ClearGenerated();
        Mapper.RegisterGeneratedList<SimpleSrc, SimpleTgt>(null);

        // After clearing, MapList must fall through to runtime compilation path
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();
        var sources = new List<SimpleSrc> { new() { Id = 1, Name = "a" } };
        var result = Mapper.MapList<SimpleSrc, SimpleTgt>(sources);
        Assert.NotNull(result);
        Assert.Equal(1, Mapper.CompilationCount);
        Assert.Equal(0, Mapper.CacheHits);
    }

    [Fact]
    public void ClearGenerated_ComposeFunc_Cleared()
    {
        Mapper.RegisterGeneratedCompose<SimpleTgt>(
            sources => new SimpleTgt { Id = 99, Name = "composed" });

        // Before clear: Compose uses the generated path
        var before = Mapper.Compose<SimpleTgt>(new SimpleSrc { Id = 1, Name = "x" });
        Assert.Equal(99, before?.Id);

        Mapper.ClearGenerated();

        // After clear: no generated func, no [MapFrom] on SimpleTgt → throws
        Assert.Throws<MapConfigurationException>(
            () => Mapper.Compose<SimpleTgt>(new SimpleSrc { Id = 1, Name = "x" }));
    }

    // ================================================================
    // 2. ClearCache — statement mutations (L236-249)
    //    Each .Clear() / .ClearCache() is mutated to `;`.
    // ================================================================

    [Fact]
    public void ClearCache_Map_Still_Works_After_Clear()
    {
        var source = new SimpleSrc { Id = 42, Name = "cache-test" };

        // Populate runtime reflection caches
        var r1 = Mapper.Map<SimpleSrc, SimpleTgt>(source);
        Assert.NotNull(r1);
        Assert.Equal(42, r1!.Id);

        Mapper.ClearCache();

        // After clearing, Map must rebuild caches and still produce correct results
        var r2 = Mapper.Map<SimpleSrc, SimpleTgt>(source);
        Assert.NotNull(r2);
        Assert.Equal(42, r2!.Id);
        Assert.Equal("cache-test", r2.Name);
    }

    [Fact]
    public void ClearCache_PropertyMatcher_Rebuilt()
    {
        var source = new SimpleSrc { Id = 7, Name = "rebuilt" };
        var first = Mapper.Map<SimpleSrc, SimpleTgt>(source);
        Assert.NotNull(first);

        Mapper.ClearCache();

        // Second call after clear — cache rebuilt, result identical
        var second = Mapper.Map<SimpleSrc, SimpleTgt>(source);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(first.Name, second.Name);
    }

    // ================================================================
    // 3a. Metrics — Map<TSource>(source) same-type overload
    // ================================================================

    [Fact]
    public void Metrics_SameType_TotalMapCalls_Incremented()
    {
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.Map(new SimpleSrc { Id = 1, Name = "t" });

        Assert.Equal(1, Mapper.TotalMapCalls);
    }

    [Fact]
    public void Metrics_SameType_CacheHits_With_Generated()
    {
        Mapper.RegisterGeneratedCore<SimpleSrc, SimpleSrc>(
            s => new SimpleSrc { Id = s.Id, Name = s.Name });
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.Map(new SimpleSrc { Id = 1, Name = "t" });

        Assert.Equal(1, Mapper.CacheHits);
        Assert.Equal(0, Mapper.CompilationCount);
    }

    [Fact]
    public void Metrics_SameType_CompilationCount_Without_Generated()
    {
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.Map(new SimpleSrc { Id = 1, Name = "t" });

        Assert.Equal(1, Mapper.CompilationCount);
        Assert.Equal(0, Mapper.CacheHits);
    }

    [Fact]
    public void Metrics_SameType_Disabled_No_Counters_Change()
    {
        Mapper.MetricsEnabled = false;
        Mapper.ResetMetrics();

        Mapper.Map(new SimpleSrc { Id = 1, Name = "t" });

        Assert.Equal(0, Mapper.TotalMapCalls);
        Assert.Equal(0, Mapper.CacheHits);
        Assert.Equal(0, Mapper.CompilationCount);
    }

    // ================================================================
    // 3b. Metrics — Map<TSource, TTarget>(source) hetero overload
    // ================================================================

    [Fact]
    public void Metrics_Hetero_TotalMapCalls_Incremented()
    {
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.Map<SimpleSrc, SimpleTgt>(new SimpleSrc { Id = 1, Name = "t" });

        Assert.Equal(1, Mapper.TotalMapCalls);
    }

    [Fact]
    public void Metrics_Hetero_CacheHits_With_Generated()
    {
        Mapper.RegisterGeneratedCore<SimpleSrc, SimpleTgt>(
            s => new SimpleTgt { Id = s.Id, Name = s.Name });
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.Map<SimpleSrc, SimpleTgt>(new SimpleSrc { Id = 1, Name = "t" });

        Assert.Equal(1, Mapper.CacheHits);
        Assert.Equal(0, Mapper.CompilationCount);
    }

    [Fact]
    public void Metrics_Hetero_CompilationCount_Without_Generated()
    {
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.Map<SimpleSrc, SimpleTgt>(new SimpleSrc { Id = 1, Name = "t" });

        Assert.Equal(1, Mapper.CompilationCount);
        Assert.Equal(0, Mapper.CacheHits);
    }

    [Fact]
    public void Metrics_Hetero_Disabled_No_Counters_Change()
    {
        Mapper.MetricsEnabled = false;
        Mapper.ResetMetrics();

        Mapper.Map<SimpleSrc, SimpleTgt>(new SimpleSrc { Id = 1, Name = "t" });

        Assert.Equal(0, Mapper.TotalMapCalls);
        Assert.Equal(0, Mapper.CacheHits);
        Assert.Equal(0, Mapper.CompilationCount);
    }

    // ================================================================
    // 3c. Metrics — Map<TSource, TTarget>(source, target) update overload
    // ================================================================

    [Fact]
    public void Metrics_Update_TotalMapCalls_Incremented()
    {
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.Map(new SimpleSrc { Id = 5, Name = "upd" }, new SimpleTgt());

        Assert.Equal(1, Mapper.TotalMapCalls);
    }

    [Fact]
    public void Metrics_Update_CacheHits_With_Generated()
    {
        Mapper.RegisterGeneratedUpdate<SimpleSrc, SimpleTgt>(
            (s, t, a) => { t.Id = s.Id; t.Name = s.Name; });
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        var target = new SimpleTgt();
        Mapper.Map(new SimpleSrc { Id = 5, Name = "upd" }, target);

        Assert.Equal(1, Mapper.CacheHits);
        Assert.Equal(0, Mapper.CompilationCount);
        Assert.Equal(5, target.Id);
    }

    [Fact]
    public void Metrics_Update_CompilationCount_Without_Generated()
    {
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.Map(new SimpleSrc { Id = 5, Name = "upd" }, new SimpleTgt());

        Assert.Equal(1, Mapper.CompilationCount);
        Assert.Equal(0, Mapper.CacheHits);
    }

    [Fact]
    public void Metrics_Update_Disabled_No_Counters_Change()
    {
        Mapper.MetricsEnabled = false;
        Mapper.ResetMetrics();

        Mapper.Map(new SimpleSrc { Id = 5, Name = "upd" }, new SimpleTgt());

        Assert.Equal(0, Mapper.TotalMapCalls);
        Assert.Equal(0, Mapper.CacheHits);
        Assert.Equal(0, Mapper.CompilationCount);
    }

    // ================================================================
    // 3d. Metrics — MapList<TTarget>(sources) same-type list overload
    // ================================================================

    [Fact]
    public void Metrics_SameTypeList_TotalMapCalls_Incremented()
    {
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.MapList(new List<SimpleSrc> { new() { Id = 1, Name = "a" } });

        Assert.Equal(1, Mapper.TotalMapCalls);
    }

    [Fact]
    public void Metrics_SameTypeList_CacheHits_With_Generated()
    {
        Mapper.RegisterGeneratedList<SimpleSrc, SimpleSrc>(
            list => list.Select(s => new SimpleSrc { Id = s.Id, Name = s.Name }).ToList());
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.MapList(new List<SimpleSrc> { new() { Id = 1, Name = "a" } });

        Assert.Equal(1, Mapper.CacheHits);
        Assert.Equal(0, Mapper.CompilationCount);
    }

    [Fact]
    public void Metrics_SameTypeList_CompilationCount_Without_Generated()
    {
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.MapList(new List<SimpleSrc> { new() { Id = 1, Name = "a" } });

        Assert.Equal(1, Mapper.CompilationCount);
        Assert.Equal(0, Mapper.CacheHits);
    }

    [Fact]
    public void Metrics_SameTypeList_Disabled_No_Counters_Change()
    {
        Mapper.MetricsEnabled = false;
        Mapper.ResetMetrics();

        Mapper.MapList(new List<SimpleSrc> { new() { Id = 1, Name = "a" } });

        Assert.Equal(0, Mapper.TotalMapCalls);
        Assert.Equal(0, Mapper.CacheHits);
        Assert.Equal(0, Mapper.CompilationCount);
    }

    // ================================================================
    // 3e. Metrics — MapList<TSource, TTarget>(sources) hetero list overload
    // ================================================================

    [Fact]
    public void Metrics_HeteroList_TotalMapCalls_Incremented()
    {
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.MapList<SimpleSrc, SimpleTgt>(
            new List<SimpleSrc> { new() { Id = 1, Name = "a" } });

        Assert.Equal(1, Mapper.TotalMapCalls);
    }

    [Fact]
    public void Metrics_HeteroList_CacheHits_With_Generated()
    {
        Mapper.RegisterGeneratedList<SimpleSrc, SimpleTgt>(
            list => list.Select(s => new SimpleTgt { Id = s.Id, Name = s.Name }).ToList());
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.MapList<SimpleSrc, SimpleTgt>(
            new List<SimpleSrc> { new() { Id = 1, Name = "a" } });

        Assert.Equal(1, Mapper.CacheHits);
        Assert.Equal(0, Mapper.CompilationCount);
    }

    [Fact]
    public void Metrics_HeteroList_CompilationCount_Without_Generated()
    {
        Mapper.MetricsEnabled = true;
        Mapper.ResetMetrics();

        Mapper.MapList<SimpleSrc, SimpleTgt>(
            new List<SimpleSrc> { new() { Id = 1, Name = "a" } });

        Assert.Equal(1, Mapper.CompilationCount);
        Assert.Equal(0, Mapper.CacheHits);
    }

    [Fact]
    public void Metrics_HeteroList_Disabled_No_Counters_Change()
    {
        Mapper.MetricsEnabled = false;
        Mapper.ResetMetrics();

        Mapper.MapList<SimpleSrc, SimpleTgt>(
            new List<SimpleSrc> { new() { Id = 1, Name = "a" } });

        Assert.Equal(0, Mapper.TotalMapCalls);
        Assert.Equal(0, Mapper.CacheHits);
        Assert.Equal(0, Mapper.CompilationCount);
    }

    // ================================================================
    // 4. MapTracer.GloballyEnabled negation mutations (L371, L410)
    // ================================================================

    [Fact]
    public void MapTracer_Enabled_LogCallback_Invoked()
    {
        string? captured = null;
        MapTracer.GloballyEnabled = true;
        MapTracer.LogCallback = msg => captured = msg;

        Mapper.Map<SimpleSrc, SimpleTgt>(new SimpleSrc { Id = 1, Name = "trace" });

        Assert.NotNull(captured);
        Assert.NotEmpty(captured!);
    }

    [Fact]
    public void MapTracer_Disabled_LogCallback_Not_Invoked()
    {
        string? captured = null;
        MapTracer.GloballyEnabled = false;
        MapTracer.LogCallback = msg => captured = msg;

        Mapper.Map<SimpleSrc, SimpleTgt>(new SimpleSrc { Id = 1, Name = "quiet" });

        Assert.Null(captured);
    }

    [Fact]
    public void MapTracer_Enabled_Update_Overload_LogCallback_Invoked()
    {
        string? captured = null;
        MapTracer.GloballyEnabled = true;
        MapTracer.LogCallback = msg => captured = msg;

        Mapper.Map(new SimpleSrc { Id = 2, Name = "upd" }, new SimpleTgt());

        Assert.NotNull(captured);
        Assert.NotEmpty(captured!);
    }

    [Fact]
    public void MapTracer_Disabled_Update_Overload_LogCallback_Not_Invoked()
    {
        string? captured = null;
        MapTracer.GloballyEnabled = false;
        MapTracer.LogCallback = msg => captured = msg;

        Mapper.Map(new SimpleSrc { Id = 2, Name = "upd" }, new SimpleTgt());

        Assert.Null(captured);
    }

    // ================================================================
    // 5. MaxRecursionDepth boundary mutations (L493, L495)
    // ================================================================

    [Fact]
    public void MaxRecursionDepth_At_UpperBound_Returns_4096()
    {
        Mapper.MaxRecursionDepth = 4096;
        Assert.Equal(4096, Mapper.MaxRecursionDepth);
    }

    [Fact]
    public void MaxRecursionDepth_Above_UpperBound_Clamped_To_4096()
    {
        Mapper.MaxRecursionDepth = 4097;
        Assert.Equal(4096, Mapper.MaxRecursionDepth);
    }

    [Fact]
    public void MaxRecursionDepth_At_LowerBound_Returns_1()
    {
        Mapper.MaxRecursionDepth = 1;
        Assert.Equal(1, Mapper.MaxRecursionDepth);
    }

    [Fact]
    public void MaxRecursionDepth_Below_LowerBound_Clamped_To_1()
    {
        Mapper.MaxRecursionDepth = 0;
        Assert.Equal(1, Mapper.MaxRecursionDepth);
    }

    [Fact]
    public void MaxRecursionDepth_Normal_Value_Passes_Through()
    {
        Mapper.MaxRecursionDepth = 2;
        Assert.Equal(2, Mapper.MaxRecursionDepth);
    }

    // ================================================================
    // 6. MaxInlineDepth boundary mutations (L513, L515)
    // ================================================================

    [Fact]
    public void MaxInlineDepth_At_UpperBound_Returns_16()
    {
        Mapper.MaxInlineDepth = 16;
        Assert.Equal(16, Mapper.MaxInlineDepth);
    }

    [Fact]
    public void MaxInlineDepth_Above_UpperBound_Clamped_To_16()
    {
        Mapper.MaxInlineDepth = 17;
        Assert.Equal(16, Mapper.MaxInlineDepth);
    }

    [Fact]
    public void MaxInlineDepth_Zero_Passes_Through()
    {
        Mapper.MaxInlineDepth = 0;
        Assert.Equal(0, Mapper.MaxInlineDepth);
    }

    [Fact]
    public void MaxInlineDepth_Negative_Clamped_To_0()
    {
        Mapper.MaxInlineDepth = -1;
        Assert.Equal(0, Mapper.MaxInlineDepth);
    }

    [Fact]
    public void MaxInlineDepth_Normal_Value_Passes_Through()
    {
        Mapper.MaxInlineDepth = 8;
        Assert.Equal(8, Mapper.MaxInlineDepth);
    }

    // ================================================================
    // 7. Pipeline PostMap — MapPipeline.s_isEnabled && result != null
    // ================================================================

    [Fact]
    public void Pipeline_PostMap_Called_When_Both_NonNull()
    {
        bool postMapCalled = false;
        Mapper.Pipeline.AddPostMap<SimpleSrc, SimpleTgt>((src, tgt) =>
        {
            postMapCalled = true;
        });

        var source = new SimpleSrc { Id = 10, Name = "pipeline" };
        var result = Mapper.Map<SimpleSrc, SimpleTgt>(source);

        Assert.NotNull(result);
        Assert.True(postMapCalled,
            "PostMap callback must be invoked when pipeline is enabled and result is non-null");
    }

    [Fact]
    public void Pipeline_PostMap_Receives_Correct_Target()
    {
        SimpleTgt? capturedTarget = null;
        Mapper.Pipeline.AddPostMap<SimpleSrc, SimpleTgt>((src, tgt) =>
        {
            capturedTarget = tgt;
        });

        var source = new SimpleSrc { Id = 20, Name = "verify" };
        var result = Mapper.Map<SimpleSrc, SimpleTgt>(source);

        Assert.NotNull(capturedTarget);
        Assert.Same(result, capturedTarget);
        Assert.Equal(20, capturedTarget!.Id);
    }
}
