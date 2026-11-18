// 降级检测器切换测试——三态分流/对齐表落地（NoAutoMatch 分类 +
// ComposeMappings 五类扫描 + MapFrom 通道明细承载）/降级缓存双条件/miss 扫描腿兜底保留。
// （三态 + 逐特性对齐表 + 门禁提前）/（消费端语义表）/（缓存双条件）。
// 隔离范式（经验 #60/#65/#366）：[Collection("GlobalConfigState")] + 自足注册 + try/finally 清理。
using NCMapper;
using NCMapper.EFCore;
using NCMapper.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class DegradationDetectorThreeStateSwitchTests
{
    // ===== fixture（手工注册，自足）=====

    public class DdsEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class DdsDto { public int Id { get; set; } public string Alias { get; set; } = ""; }

    // off 态（身份键，Properties 空）
    public class DdsOffEntity { public int Id { get; set; } }
    public class DdsOffDto { public int Id { get; set; } }

    // 三态①纯可翻译明细 + 无参构造交互（record 主构造）
    public sealed record DdsRecordDto(int Id, string Alias);

    // MapFrom 通道明细承载（pm 级能力位——对齐表 #1/2/3/4/8）
    public class DdsAttrEntity { public int Id { get; set; } public string Ext { get; set; } = ""; }
    public class DdsAttrDto { public int Id { get; set; } public string Ext { get; set; } = ""; }

    // NoAutoMatch（对齐表 #9——双通道不对称）
    public class DdsNaEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class DdsNaDto { public int Id { get; set; } public string Alias { get; set; } = ""; }

    // miss 扫描腿（纯运行时——[MapFrom]+[Mask] 未注册形态走反射兜底）
    [NCMapper.Attributes.MapFrom(typeof(DdsMissEntity))]
    public class DdsMissDto
    {
        public int Id { get; set; }
        [NCMapper.Attributes.Mask(typeof(DdsMask))]
        public string Ext { get; set; } = "";
    }
    public class DdsMissEntity { public int Id { get; set; } public string Ext { get; set; } = ""; }
    public class DdsMask : IMask { public string Mask(string? value) => (value ?? "") + "*"; }

    private static RuntimePropertyMapping Pm(string target, string? source = null,
        bool hasMask = false, bool hasConverter = false, bool isFromJson = false,
        string? format = null, string? condition = null)
        => new()
        {
            Target = new RuntimeTargetPropertySlot { Name = target, TypeName = "String" },
            SourceName = source,
            HasMask = hasMask,
            HasConverter = hasConverter,
            IsFromJson = isFromJson,
            FormatString = format,
            ConditionMethodRef = condition,
        };

    private static RuntimeComposeMapping Cm(bool hasMask = false, bool hasConverter = false,
        bool isFromJson = false, string? format = null, string? condition = null, string[]? paths = null)
        => new()
        {
            TargetPropertyName = "Ext",
            SubSourceTypeName = "DdsOtherEntity",
            SourcePathNames = paths ?? new[] { "Raw" },
            HasMask = hasMask,
            HasConverter = hasConverter,
            IsFromJson = isFromJson,
            FormatString = format,
            ConditionMethodRef = condition,
        };

    private static RuntimeMapperMetadata Meta(Type src, Type tgt, RuntimeMappingChannel channel,
        RuntimePropertyMapping[]? props = null, bool noAutoInclude = false, bool hasCompose = false,
        RuntimeComposeMapping[]? compose = null)
        => new()
        {
            SourceType = src, TargetType = tgt,
            SourceTypeFqn = src.FullName!, TargetTypeFqn = tgt.FullName!,
            Channel = channel, IsFluentForward = channel == RuntimeMappingChannel.Fluent,
            NoAutoInclude = noAutoInclude,
            HasComposeMappings = hasCompose,
            ComposeMappings = compose ?? Array.Empty<RuntimeComposeMapping>(),
            Properties = props ?? Array.Empty<RuntimePropertyMapping>(),
        };

    // ===== 三态②：off 态 reason 文本逐字（F3 红线——B9）=====

    [Fact]
    public void OffState_ReasonText_Verbatim_AndForcesDegrade()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta(typeof(DdsOffEntity), typeof(DdsOffDto), RuntimeMappingChannel.Fluent));
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(DdsOffEntity), typeof(DdsOffDto), out var reason);
            Assert.True(needs, "off 态身份键明细不可达 → 维持强制降级（保守语义不放宽）");
            // 逐字断言（验收 1——F3：任何字符级差异即红）
            Assert.Equal($"Fluent 声明类型对（{nameof(DdsOffDto)}，off 态身份键/明细不可达），走降级兜底内存映射", reason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    // ===== 三态②：MapFrom 通道空明细 → 落既有判定序（等价现状——不误用 off 态文本）=====

    [Fact]
    public void EntryHitDetailUnreachable_MapFromChannel_FallsThrough()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta(typeof(DdsEntity), typeof(DdsDto), RuntimeMappingChannel.MapFrom));
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(DdsEntity), typeof(DdsDto), out var reason);
            Assert.False(needs, $"MapFrom 通道空明细落既有判定序（无特性不降级；不得误用 Fluent off 态文本）——reason: {reason}");
            Assert.DoesNotContain("off 态", reason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    // ===== 三态①：明细命中纯可翻译 → 无参构造检查仍生效 =====

    [Fact]
    public void DetailHitTranslatable_NoParamCtor_StillDegrades()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta(typeof(DdsEntity), typeof(DdsRecordDto), RuntimeMappingChannel.Fluent,
                props: new[] { Pm("Id", "Id"), Pm("Alias", "Name") }));
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(DdsEntity), typeof(DdsRecordDto), out var reason);
            Assert.True(needs, "明细命中纯可翻译 → 快照权威跳过扫描腿，但无参构造检查仍生效（record）");
            Assert.Contains("无无参构造器", reason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    // ===== 对齐表 #1/2/3/4/8：MapFrom 通道 pm 级五类（明细承载判定）=====

    [Theory]
    [InlineData("Mask")]
    [InlineData("Convert")]
    [InlineData("FromJson")]
    [InlineData("Format")]
    [InlineData("MapWhen")]
    public void DetailHit_MapFromChannel_PmCapability_Degrades(string category)
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta(typeof(DdsAttrEntity), typeof(DdsAttrDto), RuntimeMappingChannel.MapFrom,
                props: new[]
                {
                    Pm("Id", "Id"),
                    category switch
                    {
                        "Mask" => Pm("Ext", "Ext", hasMask: true),
                        "Convert" => Pm("Ext", "Ext", hasConverter: true),
                        "FromJson" => Pm("Ext", "Ext", isFromJson: true),
                        "Format" => Pm("Ext", "Ext", format: "N2"),
                        _ => Pm("Ext", "Ext", condition: "X.When"),
                    },
                }));
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(DdsAttrEntity), typeof(DdsAttrDto), out var reason);
            Assert.True(needs, $"{category}（pm 明细承载）必须降级（对齐表——特性对判定源切换）");
            Assert.Contains(category, reason);
            // MapFrom 通道 reason 不得误用 Fluent 文本（通道语义保真）
            Assert.DoesNotContain("Fluent", reason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    // ===== 对齐表扩展（G-5）：MapFrom 通道 ComposeMappings 五类扫描 =====

    [Theory]
    [InlineData("Mask")]
    [InlineData("Convert")]
    [InlineData("FromJson")]
    [InlineData("Format")]
    [InlineData("MapWhen")]
    public void DetailHit_MapFromChannel_ComposeMappings_FiveCategories_Degrades(string category)
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta(typeof(DdsAttrEntity), typeof(DdsAttrDto), RuntimeMappingChannel.MapFrom,
                props: new[] { Pm("Id", "Id") },
                compose: new[]
                {
                    category switch
                    {
                        "Mask" => Cm(hasMask: true),
                        "Convert" => Cm(hasConverter: true),
                        "FromJson" => Cm(isFromJson: true),
                        "Format" => Cm(format: "N2"),
                        _ => Cm(condition: "X.When"),
                    },
                }));
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(DdsAttrEntity), typeof(DdsAttrDto), out var reason);
            Assert.True(needs, $"跨表声明 {category} 能力位必须降级（G-5：ComposeMappings 明细扫描承接——否则 Mask 漏判 = 明文外泄级）");
            Assert.Contains(category, reason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    /// <summary>空组条目（SourcePathNames 空 = Block-Ignore 忽略信号）跳过能力位扫描。</summary>
    [Fact]
    public void DetailHit_ComposeMappings_EmptyGroup_Skipped()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta(typeof(DdsAttrEntity), typeof(DdsAttrDto), RuntimeMappingChannel.MapFrom,
                props: new[] { Pm("Id", "Id") },
                compose: new[] { Cm(hasMask: true, paths: Array.Empty<string>()) }));
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(DdsAttrEntity), typeof(DdsAttrDto), out var reason);
            Assert.False(needs, $"空组（忽略信号）跳过——不因组内残留能力位降级——reason: {reason}");
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    // ===== 对齐表 #9：NoAutoMatch 分类（通道限定——F4 防误伤）=====

    [Fact]
    public void NoAutoMatch_MapFromChannel_Degrades()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta(typeof(DdsNaEntity), typeof(DdsNaDto), RuntimeMappingChannel.MapFrom,
                props: new[] { Pm("Id", "Id"), Pm("Alias", "Name") }, noAutoInclude: true));
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(DdsNaEntity), typeof(DdsNaDto), out var reason);
            Assert.True(needs, "特性侧白名单协议位强制降级（对齐表 #9——20261010-005 D3）");
            Assert.Contains("NoAutoMatch", reason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    [Fact]
    public void NoAutoMatch_FluentChannel_NotDegrades()
    {
        // F4 防误伤：Fluent 侧白名单语义由提取期收窄承载（明细即白名单面），维持不降级
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta(typeof(DdsNaEntity), typeof(DdsNaDto), RuntimeMappingChannel.Fluent,
                props: new[] { Pm("Id", "Id"), Pm("Alias", "Name") }, noAutoInclude: true));
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(DdsNaEntity), typeof(DdsNaDto), out var reason);
            Assert.False(needs, $"Fluent 通道 NoAutoInclude 不因 NoAutoMatch 分类降级（通道限定——F4）——reason: {reason}");
            // 直调分类单点：通道限定判据
            Assert.Null(DegradationDetectorBase.ClassifyUntranslatableDeclaration(
                Meta(typeof(DdsNaEntity), typeof(DdsNaDto), RuntimeMappingChannel.Fluent, noAutoInclude: true)));
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    // ===== 三态③：miss → 扫描腿兜底（现状逻辑保留——reason 为扫描腿文本）=====

    [Fact]
    public void Miss_AttributeScanLeg_Mask_StillDegrades()
    {
        try
        {
            RuntimeSnapshotStore.Clear(); // 不注册 → miss
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(DdsMissEntity), typeof(DdsMissDto), out var reason);
            Assert.True(needs, "纯运行时 miss → 特性扫描腿兜底（IProjectionPolicy 判定保留）");
            Assert.Contains("MaskAttribute", reason); // 扫描腿文本（非 Classify 分类名）
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    // ===== 降级缓存双条件（ORM 通道与 Agnostic 通道同族）=====

    [Fact]
    public void Cache_ContentVersionMismatch_Recalculates()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();

            // 第一次判定（Pipeline.Version 恒定——无 PostMap 注册）：纯可翻译 → false，条目入缓存
            RuntimeSnapshotStore.Register(Meta(typeof(DdsEntity), typeof(DdsDto), RuntimeMappingChannel.Fluent,
                props: new[] { Pm("Id", "Id"), Pm("Alias", "Name") }));
            var first = EfCoreDegradationDetector.NeedsDegradation(typeof(DdsEntity), typeof(DdsDto), out _);
            Assert.False(first, "纯可翻译明细首次判定不降级");

            // 同对再注册（合并臂 OR 语义——HasMask=true）→ ContentVersion 递增
            RuntimeSnapshotStore.Register(Meta(typeof(DdsEntity), typeof(DdsDto), RuntimeMappingChannel.Fluent,
                props: new[] { Pm("Id", "Id"), Pm("Alias", "Name", hasMask: true) }));

            // ContentVersion 失配必须重算——旧缓存条目命中（false）则本断言红
            var second = EfCoreDegradationDetector.NeedsDegradation(typeof(DdsEntity), typeof(DdsDto), out var reason2);
            Assert.True(second, $"注册表内容版本失配必须重算（缓存双条件——注册后旧条目不得继续命中）——reason: {reason2}");
            Assert.Contains("Mask", reason2);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }
}
