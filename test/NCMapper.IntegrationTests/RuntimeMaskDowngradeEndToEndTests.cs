// Runtime registry-mask leg: end-to-end closure + anchored combo matrix close-out.
// Placement rationale: this project does not reference the source generator, so [MapFrom]
// fixtures emit no generated delegates and Mapper.Map naturally takes the runtime reflection
// path (the only surface where the registry leg lives); the test-project generated-baseline
// byte gate stays untouched. Attribute-anchor pairs ([Convert]/[MapFormat]) therefore run here.
// Isolation discipline: PipelineGlobalState serialization + per-case Clear + try/finally.
// Sequence premise: every masking assertion registers BEFORE the pair's first Map (delegate
// materialization is an irreversible known limitation; the map-before-register case asserts
// that limitation itself).
using System;
using System.IO;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.IntegrationTests;

[Collection("PipelineGlobalState")]
public class RuntimeMaskDowngradeEndToEndTests
{
    // custom test-assembly mask type (built-in core-assembly masks alone would mask
    // resolution-domain blind spots and produce false greens)
    public sealed class SuffixMask : IMask
    {
        public string Mask(string? value) => (value ?? "") + "#";
    }

    private const string MaskName = "NCMapper.IntegrationTests.RuntimeMaskDowngradeEndToEndTests+SuffixMask";

    private static RuntimePropertyMapping DetailSlot(string targetName, string? maskTypeName, bool hasMask = true, string typeName = "String")
        => new RuntimePropertyMapping
        {
            Target = new RuntimeTargetPropertySlot
            {
                Name = targetName,
                TypeName = typeName,
                HasMask = hasMask,
                MaskTypeName = maskTypeName,
            },
            SourceName = targetName,
            SourcePathNames = new[] { targetName },
            HasMask = hasMask,
            MaskTypeName = maskTypeName,
        };

    // anchored pairs ([MapFrom] DTO) register via the MapFrom channel: same channel as the
    // generator emission form; a Fluent detail against a same-direction [MapFrom] pair would
    // hit the D1 dual-declaration gate
    private static RuntimeMapperMetadata AnchoredMeta(Type s, Type d, params RuntimePropertyMapping[] props)
    {
        var slots = new RuntimeTargetPropertySlot[props.Length];
        for (var i = 0; i < props.Length; i++) slots[i] = props[i].Target;
        return new RuntimeMapperMetadata
        {
            SourceType = s,
            TargetType = d,
            SourceTypeFqn = s.FullName!,
            TargetTypeFqn = d.FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            Properties = props,
            AllTargetSlots = slots,
        };
    }

    private static RuntimeMapperMetadata FluentMeta(Type s, Type d, params RuntimePropertyMapping[] props)
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

    private static void ClearAll()
    {
        RuntimeSnapshotStore.Clear();
        EfCoreDegradationDetector.ClearCache();
    }

    // ================================================================
    // end-to-end: detection face (ORM projection degrades) -> execution face (in-memory mask)
    // ================================================================

    public class E2eEntity { public int Id { get; set; } public string Phone { get; set; } = ""; }
    public class E2eView { public int Id { get; set; } public string Phone { get; set; } = ""; }

    [Fact]
    public void EndToEnd_RegistryMaskPair_DegradesThenMaskedOnInMemoryMap()
    {
        try
        {
            ClearAll();
            RuntimeSnapshotStore.Register(FluentMeta(typeof(E2eEntity), typeof(E2eView),
                DetailSlot("Id", null, hasMask: false, typeName: "Int32"),
                DetailSlot("Phone", MaskName)));

            // detection face: the Fluent-declared pair forces ORM projection degradation
            Assert.True(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(E2eView)));
            Assert.True(EfCoreDegradationDetector.NeedsDegradation(typeof(E2eEntity), typeof(E2eView), out _));

            // execution face: the degraded in-memory map applies the registry mask
            var v = Mapper.Map<E2eEntity, E2eView>(new E2eEntity { Id = 1, Phone = "13800000004" });
            Assert.Equal(1, v.Id);
            Assert.Equal("13800000004#", v.Phone);
        }
        finally { ClearAll(); }
    }

    // ================================================================
    // registry mask x attribute [Convert] -> convert first, mask second
    // ================================================================

    public class ConvEntity { public int Qty { get; set; } }
    private static class ConvFormatter
    {
        public static string Convert(int fieldValue) => "CV" + fieldValue;
    }

    [MapFrom(typeof(ConvEntity))]
    public class ConvView
    {
        [Convert(typeof(ConvFormatter), nameof(ConvFormatter.Convert))]
        public string Qty { get; set; } = "";
    }

    [Fact]
    public void RegistryMask_WithConvertAttribute_ConvertThenMask()
    {
        try
        {
            ClearAll();
            RuntimeSnapshotStore.Register(AnchoredMeta(typeof(ConvEntity), typeof(ConvView),
                DetailSlot("Qty", MaskName)));

            var v = Mapper.Map<ConvEntity, ConvView>(new ConvEntity { Qty = 21 });

            // mask input = converter output (masking a silently dropped plain conversion = red)
            Assert.Equal("CV21#", v.Qty);
        }
        finally { ClearAll(); }
    }

    // ================================================================
    // registry mask x attribute [MapFormat] -> format first, mask second (not plain Format)
    // ================================================================

    public class FmtEntity { public DateTime Stamp { get; set; } }

    [MapFrom(typeof(FmtEntity))]
    public class FmtView
    {
        [MapFormat("yyyy-MM-dd")]
        public string Stamp { get; set; } = "";
    }

    [Fact]
    public void RegistryMask_WithMapFormatAttribute_FormatThenMask()
    {
        try
        {
            ClearAll();
            RuntimeSnapshotStore.Register(AnchoredMeta(typeof(FmtEntity), typeof(FmtView),
                DetailSlot("Stamp", MaskName)));

            var v = Mapper.Map<FmtEntity, FmtView>(new FmtEntity { Stamp = new DateTime(2026, 1, 2, 3, 4, 5) });

            // mask input = formatted result; a masked-output assert proves the registry mask
            // was not silently dropped into a plain Format kind
            Assert.Equal("2026-01-02#", v.Stamp);
        }
        finally { ClearAll(); }
    }

    // ================================================================
    // derived flattening (naming-convention) leaf x registry mask
    // ================================================================

    public class FlatAddress { public string City { get; set; } = ""; }
    public class FlatEntity { public FlatAddress Address { get; set; } = new(); }

    [MapFrom(typeof(FlatEntity))]
    public class FlatView
    {
        public string AddressCity { get; set; } = "";
    }

    [Fact]
    public void RegistryMask_FlattenedLeaf_Applied()
    {
        try
        {
            ClearAll();
            RuntimeSnapshotStore.Register(AnchoredMeta(typeof(FlatEntity), typeof(FlatView),
                DetailSlot("AddressCity", MaskName)));

            var v = Mapper.Map<FlatEntity, FlatView>(new FlatEntity { Address = new FlatAddress { City = "sz" } });

            Assert.Equal("sz#", v.AddressCity);
        }
        finally { ClearAll(); }
    }

    // ================================================================
    // explicit dot-path ([MapProperty] with dotted FromField) leaf x registry mask
    // ================================================================

    [MapFrom(typeof(FlatEntity))]
    public class ExplicitView
    {
        [MapProperty(typeof(FlatEntity), "Address.City")]
        public string CityName { get; set; } = "";
    }

    [Fact]
    public void RegistryMask_ExplicitDotPathLeaf_Applied()
    {
        try
        {
            ClearAll();
            RuntimeSnapshotStore.Register(AnchoredMeta(typeof(FlatEntity), typeof(ExplicitView),
                DetailSlot("CityName", MaskName)));

            var v = Mapper.Map<FlatEntity, ExplicitView>(new FlatEntity { Address = new FlatAddress { City = "sz" } });

            Assert.Equal("sz#", v.CityName);
        }
        finally { ClearAll(); }
    }
}
