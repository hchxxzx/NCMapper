// Gate tests for the runtime registry-mask leg (guard cases A/B/C/D).
// A/D are red-first obligations: they must fail before the registry leg exists
// (plain-text output = the registered mask detail is not consumed by the in-memory engine).
// B/C are regression guards: green before implementation and must stay green after
// (B: attribute-pair trace text/line count unchanged; C: plan-cache key count does not
// grow with registration version). Fixtures are plain POCOs without anchors, registered
// manually and cleared in try/finally (self-contained registration discipline).
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Runtime;
using NCMapper.Shared;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class RuntimeMaskRegistryLegTests
{
    public class LegMask : IMask
    {
        public string Mask(string? value) => (value ?? "") + "#";
    }

    private const string MaskTypeDisplayName = "NCMapper.Tests.Runtime.RuntimeMaskRegistryLegTests+LegMask";

    // ---- Gate A fixtures: sibling pairs share base-declared properties ----
    public class GateABaseEntity { public string Phone { get; set; } = ""; }
    public class GateABaseDto { public string Phone { get; set; } = ""; }
    public class GateAEntity1 : GateABaseEntity { }
    public class GateAEntity2 : GateABaseEntity { }
    public class GateADto1 : GateABaseDto { }
    public class GateADto2 : GateABaseDto { }

    // ---- Gate D fixtures: plain registered pair ----
    public class GateDEntity { public string Phone { get; set; } = ""; }
    public class GateDDto { public string Phone { get; set; } = ""; }

    // ---- Gate C fixture: plain pair for direct Plan calls ----
    public class GateCEntity { public string Phone { get; set; } = ""; }
    public class GateCDto { public string Phone { get; set; } = ""; }

    private static RuntimeTargetPropertySlot NewSlot(bool masked)
        => new RuntimeTargetPropertySlot
        {
            Name = "Phone",
            TypeName = "String",
            HasMask = masked,
            MaskTypeName = masked ? MaskTypeDisplayName : null,
        };

    private static RuntimeMapperMetadata Meta(Type s, Type d, bool masked)
    {
        var pm = new RuntimePropertyMapping
        {
            Target = NewSlot(masked),
            SourceName = "Phone",
            SourcePathNames = new[] { "Phone" },
            HasMask = masked,
            MaskTypeName = masked ? MaskTypeDisplayName : null,
        };
        return new RuntimeMapperMetadata
        {
            SourceType = s,
            TargetType = d,
            SourceTypeFqn = s.FullName!,
            TargetTypeFqn = d.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            Properties = new[] { pm },
            AllTargetSlots = new[] { NewSlot(masked) },
        };
    }

    // ---- Gate A: sibling pairs, only one registered ----
    [Fact]
    public void GateA_SiblingPairsSharedBaseProperty_NoCrossPairMasking()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta(typeof(GateAEntity1), typeof(GateADto1), masked: true));

            var d1 = Mapper.Map<GateAEntity1, GateADto1>(new GateAEntity1 { Phone = "13800000001" });
            var d2 = Mapper.Map<GateAEntity2, GateADto2>(new GateAEntity2 { Phone = "13800000002" });

            // registered pair: mask applies (red before the registry leg exists: plain text out)
            Assert.Equal("13800000001#", d1.Phone);
            // unregistered sibling pair (same base PropertyInfo instances): must stay plain
            Assert.Equal("13800000002", d2.Phone);
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    // ---- Gate D: the gap body ----
    [Fact]
    public void GateD_ManuallyRegisteredDetail_MaskedOnInMemoryMap()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta(typeof(GateDEntity), typeof(GateDDto), masked: true));

            var d = Mapper.Map<GateDEntity, GateDDto>(new GateDEntity { Phone = "13800000003" });

            // red before the registry leg exists: plain text out is exactly the gap
            Assert.Equal("13800000003#", d.Phone);
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    // ---- Gate B: attribute pair with co-existing registry detail, zero new trace ----
    // Uses the existing generated [MapFrom]+[Mask] fixture pair (zero emission increment):
    // its generated snapshot entry carries the mask detail, so attribute + detail co-exist
    // by construction. The trace text/line count must equal the recorded current state.
    [Fact]
    public void GateB_AttributePairCoExistingDetail_TraceUnchanged()
    {
        var prevEnabled = MapTracer.GloballyEnabled;
        var prevOutput = MapTracer.GlobalOutput;
        try
        {
            RuntimeSnapshotStore.Clear();
            var generated = RuntimeSnapshotPair<DegradationDetectorThreeStateSwitchTests.DdsMissEntity,
                                                DegradationDetectorThreeStateSwitchTests.DdsMissDto>.Metadata;
            if (generated != null) RuntimeSnapshotStore.Register(generated);

            // generated-delegate per-pair null override: [MapFrom] pairs default to the
            // generated channel where the runtime leg (MaskStrategy) never runs, which
            // would make the co-existence zero-trace assertion vacuous
            Mapper.RegisterGeneratedCore<DegradationDetectorThreeStateSwitchTests.DdsMissEntity,
                                         DegradationDetectorThreeStateSwitchTests.DdsMissDto>(null);
            Mapper.RegisterGenerated<DegradationDetectorThreeStateSwitchTests.DdsMissEntity,
                                     DegradationDetectorThreeStateSwitchTests.DdsMissDto>(null);

            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;

            var d = Mapper.Map<DegradationDetectorThreeStateSwitchTests.DdsMissEntity,
                               DegradationDetectorThreeStateSwitchTests.DdsMissDto>(
                new DegradationDetectorThreeStateSwitchTests.DdsMissEntity { Id = 1, Ext = "secret" });

            // attribute leg masks exactly as today
            Assert.Equal("secret*", d.Ext);
            // trace must equal the recorded current-state baseline (no new lines ever)
            // M-9 fix : normalize EOL on BOTH sides before comparing —
            // the report is composed in an internal StringWriter (NewLine = Environment.NewLine,
            // MapTracer.WriteTrace :152), so on Linux the actual trace ends with "\n" while this
            // baseline literal uses "\r\n". Repo precedent: ReplaceLineEndings() both sides
            // (MapperGenerationResultAssertions.cs:50/:69, GeneratedSnapshotBaselineTests.cs:134-135).
            var actual = sw.ToString();
            Assert.True(actual.ReplaceLineEndings() == GateBBaselineTrace.ReplaceLineEndings(),
                "trace mismatch; actual trace:\n" + actual);
        }
        finally
        {
            MapTracer.GloballyEnabled = prevEnabled;
            MapTracer.GlobalOutput = prevOutput;
            RuntimeSnapshotStore.Clear();
        }
    }

    // Recorded current-state trace for the attribute-pair scenario (pre-implementation run):
    // header line + one line per mapped property + trailing newline.
    private const string GateBBaselineTrace =
        "[NCMapper] DdsMissEntity \u2192 DdsMissDto\r\n" +
        "  Id: Id=... (direct)\r\n" +
        "  Ext: Ext=... (direct)\r\n";

    // ---- Gate C: plan-cache key count does not grow with registration version ----
    [Fact]
    public void GateC_PlanCacheKeyCount_DoesNotGrowWithRegistrationVersion()
    {
        var field = typeof(AssignmentPlanner).GetField("s_planCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var cache = (ICollection)field!.GetValue(null)!;

        var sp = typeof(GateCEntity).GetProperty("Phone")!;
        var tp = typeof(GateCDto).GetProperty("Phone")!;

        try
        {
            RuntimeSnapshotStore.Clear();
            var before = cache.Count;
            AssignmentPlanner.Plan(sp, tp, true, null, null);   // first build
            var afterFirst = cache.Count;
            Assert.Equal(before + 1, afterFirst);

            RuntimeSnapshotStore.Clear();   // registration version bump
            RuntimeSnapshotStore.Register(Meta(typeof(GateCEntity), typeof(GateCDto), masked: false));
            AssignmentPlanner.Plan(sp, tp, true, null, null);   // same key after the bump
            Assert.Equal(afterFirst, cache.Count);   // key count must not grow with version
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    // ---- resolver parse dimension ----
    [Fact]
    public void Resolve_FullName_InTargetAssembly_Resolves()
    {
        var t = MaskTypeNameResolver.Resolve(MaskTypeDisplayName, typeof(GateCEntity), typeof(GateCDto), typeof(GateCDto).GetProperty("Phone")!);
        Assert.Equal(typeof(LegMask), t);
    }

    [Fact]
    public void Resolve_AQN_Form_Resolves()
    {
        var aqn = typeof(LegMask).AssemblyQualifiedName!;
        var t = MaskTypeNameResolver.Resolve(aqn, null, typeof(GateCDto), typeof(GateCDto).GetProperty("Phone")!);
        Assert.Equal(typeof(LegMask), t);
    }

    [Fact]
    public void Resolve_AllSegmentsMiss_ThrowsEveryTime_NegativeCacheKeepsThrowing()
    {
        const string missing = "NoSuch.Mask.Type.Anywhere";
        var prop = typeof(GateCDto).GetProperty("Phone")!;
        Assert.Throws<MapException>(() => MaskTypeNameResolver.Resolve(missing, null, typeof(GateCDto), prop));
        // negative-cache hit must rethrow the same exception (saves re-parsing, not re-throwing)
        Assert.Throws<MapException>(() => MaskTypeNameResolver.Resolve(missing, null, typeof(GateCDto), prop));
    }

    [Fact]
    public void Resolve_TwoDomains_EachHits()
    {
        // same name, two target domains (test assembly vs core assembly): each domain resolves on its own
        var prop = typeof(GateCDto).GetProperty("Phone")!;
        var fromTestDomain = MaskTypeNameResolver.Resolve(MaskTypeDisplayName, null, typeof(GateCDto), prop);
        var fromCoreDomain = MaskTypeNameResolver.Resolve(MaskTypeDisplayName, null, typeof(Mapper), prop);
        Assert.Equal(typeof(LegMask), fromTestDomain);
        Assert.Equal(typeof(LegMask), fromCoreDomain);
    }

    // ==================== matrix close-out (plain POCO fixtures only) ====================
    // All cases below use plain POCOs (no [MapFrom] -> no generator emission -> the
    // generated-baseline byte gate stays untouched). Cases that need attribute anchors
    // ([Convert]/[MapFormat]) or flatten shapes live in IntegrationTests
    // RuntimeMaskDowngradeEndToEndTests (that project has no source generator, so
    // Mapper.Map naturally takes the runtime reflection path where the registry leg lives).
    // The Update overload (Mapper.Map(source, target)) goes straight into the Block engine
    // (BuildMapBody): the registry context resolves once per pair there, which makes the
    // build-time trace count assertions deterministic.

    private static RuntimePropertyMapping DetailSlot(string targetName, string? maskTypeName, bool hasMask = true)
        => new RuntimePropertyMapping
        {
            Target = new RuntimeTargetPropertySlot
            {
                Name = targetName,
                TypeName = "String",
                HasMask = hasMask,
                MaskTypeName = maskTypeName,
            },
            SourceName = targetName,
            SourcePathNames = new[] { targetName },
            HasMask = hasMask,
            MaskTypeName = maskTypeName,
        };

    private static RuntimeMapperMetadata PairMeta(Type s, Type d, params RuntimePropertyMapping[] props)
    {
        var slots = new RuntimeTargetPropertySlot[props.Length];
        for (var i = 0; i < props.Length; i++) slots[i] = props[i].Target;
        return new RuntimeMapperMetadata
        {
            SourceType = s,
            TargetType = d,
            SourceTypeFqn = s.FullName!,
            TargetTypeFqn = d.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            Properties = props,
            AllTargetSlots = slots,
        };
    }

    private static int CountOf(string text, string token)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += token.Length;
        }
        return count;
    }

    // ---- non-string target x registry mask: trace warning + plain mapping (guard extended leg) ----
    public class NsEntity { public int Qty { get; set; } }
    public class NsView { public int Qty { get; set; } }

    [Fact]
    public void RegistryMask_NonStringTarget_TraceAndPlainMap()
    {
        var prevEnabled = MapTracer.GloballyEnabled;
        var prevOutput = MapTracer.GlobalOutput;
        try
        {
            RuntimeSnapshotStore.Clear();
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            RuntimeSnapshotStore.Register(PairMeta(typeof(NsEntity), typeof(NsView), DetailSlot("Qty", MaskTypeDisplayName)));

            var target = new NsView();
            Mapper.Map(new NsEntity { Qty = 42 }, target);

            Assert.Equal(42, target.Qty);   // plain mapping, no masking
            var trace = sw.ToString();
            Assert.Contains("Mask 注册表明细不支持非 string 类型", trace);
            Assert.Contains("NsView.Qty", trace);
        }
        finally
        {
            MapTracer.GloballyEnabled = prevEnabled;
            MapTracer.GlobalOutput = prevOutput;
            RuntimeSnapshotStore.Clear();
        }
    }

    // ---- build-time traces: HasMask-without-type-name / orphan detail name (emitted at
    //      context build, once per pair; the Update overload resolves the context exactly once) ----
    public class TrEntity { public string A { get; set; } = ""; public string B { get; set; } = ""; }
    public class TrView { public string A { get; set; } = ""; public string B { get; set; } = ""; }

    [Fact]
    public void RegistryMask_BuildtimeTraces_NullNameAndOrphan_EmitOnceEach()
    {
        var prevEnabled = MapTracer.GloballyEnabled;
        var prevOutput = MapTracer.GlobalOutput;
        try
        {
            RuntimeSnapshotStore.Clear();
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            RuntimeSnapshotStore.Register(PairMeta(typeof(TrEntity), typeof(TrView),
                DetailSlot("A", null, hasMask: true),                 // HasMask but no type name
                DetailSlot("Ghost", MaskTypeDisplayName),             // no property with that name on target
                DetailSlot("B", MaskTypeDisplayName)));               // well-formed control

            var target = new TrView();
            Mapper.Map(new TrEntity { A = "a", B = "b" }, target);

            Assert.Equal("b#", target.B);   // well-formed declaration masks as usual
            Assert.Equal("a", target.A);    // no type name -> plain mapping
            var trace = sw.ToString();
            Assert.Contains("declares HasMask without MaskTypeName for 'A'", trace);
            Assert.Contains("declares mask for 'Ghost'", trace);
            Assert.Equal(1, CountOf(trace, "declares HasMask without MaskTypeName"));
            Assert.Equal(1, CountOf(trace, "declares mask for 'Ghost'"));
        }
        finally
        {
            MapTracer.GloballyEnabled = prevEnabled;
            MapTracer.GlobalOutput = prevOutput;
            RuntimeSnapshotStore.Clear();
        }
    }

    // ---- mask type does not implement IMask: MapException at build (existing message key) ----
    public class BadMaskEntity { public string Phone { get; set; } = ""; }
    public class BadMaskView { public string Phone { get; set; } = ""; }
    public sealed class NotAMask { }

    [Fact]
    public void RegistryMask_MaskTypeNotImplementIMask_BuildThrows()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(PairMeta(typeof(BadMaskEntity), typeof(BadMaskView),
                DetailSlot("Phone", "NCMapper.Tests.Runtime.RuntimeMaskRegistryLegTests+NotAMask")));

            Assert.Throws<MapException>(() =>
                Mapper.Map<BadMaskEntity, BadMaskView>(new BadMaskEntity { Phone = "13800000012" }));
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    // ---- registration order: map first, register later -> materialized delegate, mask not applied
    //      (current semantics carrier; masking assertions elsewhere all register BEFORE first map) ----
    public class OrderEntity { public string Phone { get; set; } = ""; }
    public class OrderView { public string Phone { get; set; } = ""; }

    [Fact]
    public void RegistryMask_MapBeforeRegister_MaterializedDelegateNotApplied()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            var source = new OrderEntity { Phone = "13800000005" };

            Assert.Equal("13800000005", Mapper.Map<OrderEntity, OrderView>(source)!.Phone);

            RuntimeSnapshotStore.Register(PairMeta(typeof(OrderEntity), typeof(OrderView),
                DetailSlot("Phone", MaskTypeDisplayName)));

            Assert.Equal("13800000005", Mapper.Map<OrderEntity, OrderView>(source)!.Phone);
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    // ---- plan cache across call sites: MemberInit attempt path and Block render path must
    //      both produce masking in either order (shared per-pair plan cache entry) ----
    public class D3aEntity { public string Phone { get; set; } = ""; }
    public class D3aView { public string Phone { get; set; } = ""; }
    public class D3bEntity { public string Phone { get; set; } = ""; }
    public class D3bView { public string Phone { get; set; } = ""; }

    [Fact]
    public void RegistryMask_PlanCacheCrossCallSite_BothOrdersMasked()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(PairMeta(typeof(D3aEntity), typeof(D3aView), DetailSlot("Phone", MaskTypeDisplayName)));
            RuntimeSnapshotStore.Register(PairMeta(typeof(D3bEntity), typeof(D3bView), DetailSlot("Phone", MaskTypeDisplayName)));

            // order 1: MemberInit attempt (new mapping) first, then Block render (in-place update)
            Assert.Equal("13800000006#", Mapper.Map<D3aEntity, D3aView>(new D3aEntity { Phone = "13800000006" })!.Phone);
            var t1 = new D3aView();
            Mapper.Map(new D3aEntity { Phone = "13800000007" }, t1);
            Assert.Equal("13800000007#", t1.Phone);

            // order 2: Block render first, then MemberInit attempt
            var t2 = new D3bView();
            Mapper.Map(new D3bEntity { Phone = "13800000008" }, t2);
            Assert.Equal("13800000008#", t2.Phone);
            Assert.Equal("13800000009#", Mapper.Map<D3bEntity, D3bView>(new D3bEntity { Phone = "13800000009" })!.Phone);
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    // ---- content-version mismatch recompute: same pair, detail content flips
    //      (no mask declaration -> mask declaration); the plan must recompute ----
    public class VerEntity { public string Phone { get; set; } = ""; }
    public class VerView { public string Phone { get; set; } = ""; }

    [Fact]
    public void RegistryMask_ContentVersionMismatch_PlanRecomputes()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(PairMeta(typeof(VerEntity), typeof(VerView),
                DetailSlot("Phone", null, hasMask: false)));

            // site 1: the in-place-update delegate materializes first -> the plan cache entry
            // holds the no-mask-declaration content at version v1
            var t1 = new VerView();
            Mapper.Map(new VerEntity { Phone = "13800000010" }, t1);
            Assert.Equal("13800000010", t1.Phone);

            RuntimeSnapshotStore.Clear();   // registry content version bump
            RuntimeSnapshotStore.Register(PairMeta(typeof(VerEntity), typeof(VerView),
                DetailSlot("Phone", MaskTypeDisplayName)));

            // site 2: a fresh delegate build (new mapping) after the bump must not keep hitting
            // the stale v1 cache entry -> version mismatch -> recompute -> masked
            Assert.Equal("13800000010#", Mapper.Map<VerEntity, VerView>(new VerEntity { Phone = "13800000010" })!.Phone);
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    // ---- control (anti-vacuous): HasMask=false detail -> plain text untouched (over-broad leg = red) ----
    public class CtlEntity { public string Phone { get; set; } = ""; }
    public class CtlView { public string Phone { get; set; } = ""; }

    [Fact]
    public void RegistryMask_HasMaskFalse_PlainTextUntouched()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(PairMeta(typeof(CtlEntity), typeof(CtlView),
                DetailSlot("Phone", null, hasMask: false)));

            Assert.Equal("13800000011", Mapper.Map<CtlEntity, CtlView>(new CtlEntity { Phone = "13800000011" })!.Phone);
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }
}
