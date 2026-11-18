// RuntimeSnapshotStore 承载扩展测试
// 幂等合并契约（R-B 三场景）+ 新承载字段绑定/默认值 + GetTreeConfig/IsFluentRegisteredDto 门面语义。
// 隔离范式（经验库 #60/#65）：[Collection("GlobalConfigState")] 防跨集合并行 + 每场景独立嵌套类型对
//（唯一 (Source,Target) 键）——不调用 Clear()，零污染其他测试的全局注册状态。
using NCMapper.Attributes;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class RuntimeSnapshotStoreRegisterMergeTests
{
    // ================================================================
    // 场景类型对（每场景唯一键——避免测试间注册状态串扰）
    // ================================================================

    private sealed class MergeEntityA { public int Id { get; set; } }
    private sealed class MergeTargetA { public int Id { get; set; } }
    private sealed class MergeEntityB { public int Id { get; set; } }
    private sealed class MergeTargetB { public int Id { get; set; } }
    private sealed class MergeEntityC { public int Id { get; set; } }
    private sealed class MergeTargetC { public int Id { get; set; } }
    private sealed class MergeEntityD { public int Id { get; set; } }
    private sealed class MergeTargetD { public int Id { get; set; } }
    private sealed class TreeEntity { public int Id { get; set; } }
    private sealed class TreeTarget { public int Id { get; set; } }
    private sealed class FluentOnlyEntity { public int Id { get; set; } }
    private sealed class FluentOnlyTarget { public int Id { get; set; } }
    private sealed class MapFromOnlyEntity { public int Id { get; set; } }
    private sealed class MapFromOnlyTarget { public int Id { get; set; } }
    // （M2-9 检出器）：条件守卫合并臂场景类型对
    private sealed class MergeEntityE { public int Id { get; set; } }
    private sealed class MergeTargetE { public int Id { get; set; } }

    private static RuntimeMapperMetadata IdentityEntry(Type source, Type target)
        => new()
        {
            SourceType = source,
            TargetType = target,
            SourceTypeFqn = source.FullName!,
            TargetTypeFqn = target.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            // 身份级最小登记：Properties 空 + 明细字段默认（发射端身份段口径）
        };

    private static RuntimeMapperMetadata DetailEntry(Type source, Type target)
        => new()
        {
            SourceType = source,
            TargetType = target,
            SourceTypeFqn = source.FullName!,
            TargetTypeFqn = target.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            NoAutoInclude = true,
            TreeConfig = new RuntimeTreeConfig { IdProperty = "KeyId", ParentIdProperty = "KeyParentId", ChildrenProperty = "Nodes" },
            Properties = new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "MaskedName", TypeName = "String" },
                    SourceName = "Name",
                    HasMask = true,
                    MaskTypeName = "NCMapper.MaskPhone",
                    IsPropertyNoAutoInclude = true,
                    SourceIndexes = new[] { 0, 2 },
                    HasIndex = true,
                },
            },
            AllTargetSlots = new[] { new RuntimeTargetPropertySlot { Name = "MaskedName", TypeName = "String" } },
        };

    // ================================================================
    // R-B 三场景：同 (S,T) 键重复登记终态一致（明细优先）
    // ================================================================

    [Fact]
    public void Register_SamePair_IdentityThenDetail_MergesToDetail()
    {
        RuntimeSnapshotStore.Register(IdentityEntry(typeof(MergeEntityA), typeof(MergeTargetA)));
        RuntimeSnapshotStore.Register(DetailEntry(typeof(MergeEntityA), typeof(MergeTargetA)));

        var merged = RuntimeSnapshotStore.FindByPair(typeof(MergeEntityA), typeof(MergeTargetA));

        AssertDetailWins(merged);
    }

    [Fact]
    public void Register_SamePair_DetailThenIdentity_MergesToDetail()
    {
        // R-B 顺序无关：先明细后身份——身份条目不得覆盖已登记明细
        RuntimeSnapshotStore.Register(DetailEntry(typeof(MergeEntityB), typeof(MergeTargetB)));
        RuntimeSnapshotStore.Register(IdentityEntry(typeof(MergeEntityB), typeof(MergeTargetB)));

        var merged = RuntimeSnapshotStore.FindByPair(typeof(MergeEntityB), typeof(MergeTargetB));

        AssertDetailWins(merged);
    }

    [Fact]
    public void Register_SamePair_IdentityAfterDetail_SingleEntryInAllContainers()
    {
        // 终态单条目：s_byPair/s_byTarget/s_all 三容器均不残留身份条目副本
        RuntimeSnapshotStore.Register(DetailEntry(typeof(MergeEntityC), typeof(MergeTargetC)));
        RuntimeSnapshotStore.Register(IdentityEntry(typeof(MergeEntityC), typeof(MergeTargetC)));

        var byTarget = RuntimeSnapshotStore.FindByTarget(typeof(MergeTargetC));

        Assert.Single(byTarget);
        Assert.NotNull(RuntimeSnapshotStore.FindByPair(typeof(MergeEntityC), typeof(MergeTargetC)));
    }

    [Fact]
    public void Register_SamePair_TwiceDetailed_FieldUnionMerge()
    {
        // 退化形态（发射端 F16 保证不出现；契约仍须确定性）：两侧均明细 → 字段并集
        var first = DetailEntry(typeof(MergeEntityD), typeof(MergeTargetD));
        var second = DetailEntry(typeof(MergeEntityD), typeof(MergeTargetD));
        RuntimeSnapshotStore.Register(first);
        RuntimeSnapshotStore.Register(second);

        var merged = RuntimeSnapshotStore.FindByPair(typeof(MergeEntityD), typeof(MergeTargetD));

        Assert.NotNull(merged);
        Assert.Equal(first.Properties.Length, merged!.Properties.Length);
        Assert.True(merged.Properties[0].HasMask);
        Assert.Equal("NCMapper.MaskPhone", merged.Properties[0].MaskTypeName);
    }

    /// <summary>
    /// （M2-9 检出器）：ConditionMethodRef/ConditionShape 合并臂字段并集——
    /// 首条明细携带条件守卫、次条不携带 → 合并结果保留（RuntimeSnapshotStore.cs:399-400
    /// a ?? b / 非零取值——与 Mask/Converter 并集臂同构；B 形态运行时回退经注册表读消费）。
    /// </summary>
    [Fact]
    public void Register_TwiceDetailed_ConditionMethodRef_UnionMerge()
    {
        RuntimeMapperMetadata WhenDetailEntry(bool withWhen) => new()
        {
            SourceType = typeof(MergeEntityE),
            TargetType = typeof(MergeTargetE),
            SourceTypeFqn = typeof(MergeEntityE).FullName!,
            TargetTypeFqn = typeof(MergeTargetE).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            Properties = new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" },
                    SourceName = "Id",
                    ConditionMethodRef = withWhen ? "NCMapper.Generated.GeneratedMapper::Map_When_MergeTargetE_Id_abcd1234" : null,
                    ConditionShape = withWhen ? 1 : 0,
                },
            },
        };

        RuntimeSnapshotStore.Register(WhenDetailEntry(withWhen: true));
        RuntimeSnapshotStore.Register(WhenDetailEntry(withWhen: false));

        var merged = RuntimeSnapshotStore.FindByPair(typeof(MergeEntityE), typeof(MergeTargetE));

        // 合并臂保留条件守卫承载（a ?? b 字段并集——丢失即运行时回退守卫静默失效）
        Assert.NotNull(merged);
        var prop = Assert.Single(merged!.Properties);
        Assert.Equal("NCMapper.Generated.GeneratedMapper::Map_When_MergeTargetE_Id_abcd1234", prop.ConditionMethodRef);
        Assert.Equal(1, prop.ConditionShape);
    }

    private static void AssertDetailWins(RuntimeMapperMetadata? merged)
    {
        Assert.NotNull(merged);
        Assert.True(merged!.NoAutoInclude);
        Assert.NotNull(merged.TreeConfig);
        Assert.Equal("Nodes", merged.TreeConfig!.ChildrenProperty);
        var prop = Assert.Single(merged.Properties);
        Assert.True(prop.HasMask);
        Assert.Equal("NCMapper.MaskPhone", prop.MaskTypeName);
        Assert.True(prop.IsPropertyNoAutoInclude);
        Assert.Equal(new[] { 0, 2 }, prop.SourceIndexes);
        Assert.True(prop.HasIndex);
        // SourceIgnoreDirection 承载退役（源侧腿退役），断言随之移除
    }

    // ================================================================
    // 新承载字段默认值（additive：[MapFrom] 通道既有发射语义零变化）
    // ================================================================

    [Fact]
    public void NewCarryingFields_Defaults_MatchAdditiveContract()
    {
        var prop = new RuntimePropertyMapping();

        Assert.False(prop.IsPropertyNoAutoInclude);
        Assert.Null(prop.SourceIndexes);

        var meta = new RuntimeMapperMetadata();
        Assert.Null(meta.TreeConfig);

        var tree = new RuntimeTreeConfig();
        Assert.Equal("Id", tree.IdProperty);
        Assert.Equal("ParentId", tree.ParentIdProperty);
        Assert.Equal("Children", tree.ChildrenProperty);
    }

    // ================================================================
    // 门面语义：GetTreeConfig（opt-in null）
    // ================================================================

    [Fact]
    public void GetTreeConfig_Unregistered_ReturnsNull()
    {
        Assert.Null(RuntimeSnapshotStore.GetTreeConfig(typeof(FluentOnlyEntity)));
    }

    [Fact]
    public void GetTreeConfig_RegisteredWithoutTree_ReturnsNull_OptIn()
    {
        // 未调用 .Tree() → TreeConfig=null → 消费端不得取默认三元组
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(FluentOnlyEntity),
            TargetType = typeof(FluentOnlyTarget),
            SourceTypeFqn = typeof(FluentOnlyEntity).FullName!,
            TargetTypeFqn = typeof(FluentOnlyTarget).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
        });

        var config = RuntimeSnapshotStore.GetTreeConfig(typeof(FluentOnlyTarget));

        Assert.Null(config);
    }

    [Fact]
    public void GetTreeConfig_RegisteredWithTree_ReturnsConfig()
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(TreeEntity),
            TargetType = typeof(TreeTarget),
            SourceTypeFqn = typeof(TreeEntity).FullName!,
            TargetTypeFqn = typeof(TreeTarget).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            TreeConfig = new RuntimeTreeConfig { IdProperty = "KeyId", ParentIdProperty = "KeyParentId", ChildrenProperty = "Nodes" },
        });

        var config = RuntimeSnapshotStore.GetTreeConfig(typeof(TreeTarget));

        Assert.NotNull(config);
        Assert.Equal("KeyId", config!.IdProperty);
        Assert.Equal("KeyParentId", config.ParentIdProperty);
        Assert.Equal("Nodes", config.ChildrenProperty);
    }

    // ================================================================
    // 门面语义：IsFluentRegisteredDto（通道判定）
    // ================================================================

    [Fact]
    public void IsFluentRegisteredDto_FluentChannelEntry_ReturnsTrue()
    {
        Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(FluentOnlyEntity),
            TargetType = typeof(FluentOnlyTarget),
            SourceTypeFqn = typeof(FluentOnlyEntity).FullName!,
            TargetTypeFqn = typeof(FluentOnlyTarget).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
        });

        Assert.True(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(FluentOnlyTarget)));
    }

    [Fact]
    public void IsFluentRegisteredDto_MapFromChannelEntry_ReturnsFalse()
    {
        Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(MapFromOnlyEntity),
            TargetType = typeof(MapFromOnlyTarget),
            SourceTypeFqn = typeof(MapFromOnlyEntity).FullName!,
            TargetTypeFqn = typeof(MapFromOnlyTarget).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
        });

        Assert.False(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(MapFromOnlyTarget)));
        Assert.True(RuntimeSnapshotStore.IsRegisteredDto(typeof(MapFromOnlyTarget)));
    }

    [Fact]
    public void IsFluentRegisteredDto_Unregistered_ReturnsFalse()
    {
        Assert.False(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(MergeEntityA)));
    }

    // ================================================================
    // 执行期缺陷修复回归：双写合并须保留 PropertyNoAutoIncludeTargets
    // （对级数组承载为 metadata 级字段——MergeMetadata 合并清单此前漏项，samples
    // 双 ModuleInitializer（身份段+明细段）场景下合并终态 targets 恒空，门面/收集器全链失效）
    // ================================================================

    private sealed class MergeEntityT { public int Id { get; set; } }
    private sealed class MergeTargetT { public int Id { get; set; } }

    private static RuntimeMapperMetadata DetailEntryWithTargets(Type source, Type target)
        => new()
        {
            SourceType = source,
            TargetType = target,
            SourceTypeFqn = source.FullName!,
            TargetTypeFqn = target.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            PropertyNoAutoIncludeTargets = new[] { "AuditLogs" },
            Properties = new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "AuditLogs", TypeName = "List`1" },
                },
            },
        };

    [Fact]
    public void Register_IdentityThenDetail_PreservesPropertyNoAutoIncludeTargets()
    {
        RuntimeSnapshotStore.Register(IdentityEntry(typeof(MergeEntityT), typeof(MergeTargetT)));
        RuntimeSnapshotStore.Register(DetailEntryWithTargets(typeof(MergeEntityT), typeof(MergeTargetT)));

        var merged = Assert.Single(RuntimeSnapshotStore.FindByTarget(typeof(MergeTargetT)));
        Assert.Equal(new[] { "AuditLogs" }, merged.PropertyNoAutoIncludeTargets);
    }

    [Fact]
    public void Register_DetailThenIdentity_PreservesPropertyNoAutoIncludeTargets()
    {
        RuntimeSnapshotStore.Register(DetailEntryWithTargets(typeof(MergeEntityT), typeof(MergeTargetT)));
        RuntimeSnapshotStore.Register(IdentityEntry(typeof(MergeEntityT), typeof(MergeTargetT)));

        var merged = Assert.Single(RuntimeSnapshotStore.FindByTarget(typeof(MergeTargetT)));
        Assert.Equal(new[] { "AuditLogs" }, merged.PropertyNoAutoIncludeTargets);
    }

    // ================================================================
    // （A3-01 批次一）：MergeMetadata 漏 HasComposeMappings 合并臂回归——
    // 重复键合并（Register 必经，merged 恒新实例）该字段两登记方向均丢，Fluent Compose 多源 DTO
    // 投影降级分类（DegradationDetectorBase.ClassifyUntranslatableDeclaration "Compose" 分支）
    // 恒失效。同族 HasSubclassDispatch 合并臂先例（F5 模式）。
    // ================================================================

    private sealed class MergeEntityF { public int Id { get; set; } }
    private sealed class MergeTargetF { public int Id { get; set; } }
    private sealed class MergeEntityG { public int Id { get; set; } }
    private sealed class MergeTargetG { public int Id { get; set; } }
    private sealed class MergeEntityH { public int Id { get; set; } }
    private sealed class MergeTargetH { public int Id { get; set; } }
    private sealed class MergeEntityI { public int Id { get; set; } }
    private sealed class MergeTargetI { public int Id { get; set; } }

    private static RuntimeMapperMetadata ComposeFlagEntry(Type source, Type target, bool hasComposeMappings)
        => new()
        {
            SourceType = source,
            TargetType = target,
            SourceTypeFqn = source.FullName!,
            TargetTypeFqn = target.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            HasComposeMappings = hasComposeMappings,
        };

    [Fact]
    public void Register_IdentityThenDetail_PreservesHasComposeMappings_TrueLeg()
    {
        // 缺陷主场景（Fluent Compose 双笔登记形态）：身份段默认 false、明细段 true——
        // 合并臂缺失时 new 对象取默认 false → HasComposeMappings 丢（首评 #88①"有值被清空"盲区）
        RuntimeSnapshotStore.Register(ComposeFlagEntry(typeof(MergeEntityF), typeof(MergeTargetF), hasComposeMappings: false));
        RuntimeSnapshotStore.Register(ComposeFlagEntry(typeof(MergeEntityF), typeof(MergeTargetF), hasComposeMappings: true));

        var merged = Assert.Single(RuntimeSnapshotStore.FindByTarget(typeof(MergeTargetF)));
        Assert.True(merged.HasComposeMappings);
        // 消费点（P0 用户可见面在分类结果，字段保真是中间态）：降级分类恢复 "Compose"
        Assert.Equal("Compose", NCMapper.Internal.DegradationDetectorBase.ClassifyUntranslatableDeclaration(merged));
    }

    [Fact]
    public void Register_DetailThenIdentity_PreservesHasComposeMappings_TrueLeg()
    {
        // 反向登记（a=true/b=false）：明细先登记、身份后登记——身份条目不得覆盖已登记明细
        RuntimeSnapshotStore.Register(ComposeFlagEntry(typeof(MergeEntityG), typeof(MergeTargetG), hasComposeMappings: true));
        RuntimeSnapshotStore.Register(ComposeFlagEntry(typeof(MergeEntityG), typeof(MergeTargetG), hasComposeMappings: false));

        var merged = Assert.Single(RuntimeSnapshotStore.FindByTarget(typeof(MergeTargetG)));
        Assert.True(merged.HasComposeMappings);
    }

    [Fact]
    public void Register_BothComposeFlagged_MergesTrue()
    {
        // 退化形态（a=true/b=true）：两侧均明细时字段并集确定性合并
        RuntimeSnapshotStore.Register(ComposeFlagEntry(typeof(MergeEntityH), typeof(MergeTargetH), hasComposeMappings: true));
        RuntimeSnapshotStore.Register(ComposeFlagEntry(typeof(MergeEntityH), typeof(MergeTargetH), hasComposeMappings: true));

        var merged = Assert.Single(RuntimeSnapshotStore.FindByTarget(typeof(MergeTargetH)));
        Assert.True(merged.HasComposeMappings);
    }

    [Fact]
    public void Register_NeitherComposeFlagged_MergesFalse_NegativeLeg()
    {
        // 负向腿（a=false/b=false）：无 Compose 声明合并后不得放大为 true
        //（防"误写常量 true / 无条件放大"类错误实现——首评 P2-3）
        RuntimeSnapshotStore.Register(ComposeFlagEntry(typeof(MergeEntityI), typeof(MergeTargetI), hasComposeMappings: false));
        RuntimeSnapshotStore.Register(ComposeFlagEntry(typeof(MergeEntityI), typeof(MergeTargetI), hasComposeMappings: false));

        var merged = Assert.Single(RuntimeSnapshotStore.FindByTarget(typeof(MergeTargetI)));
        Assert.False(merged.HasComposeMappings);
    }

    private static void Register(RuntimeMapperMetadata metadata)
        => RuntimeSnapshotStore.Register(metadata);

    // ================================================================
    // （b）：MergeMetadata ComposeMappings 合并臂——
    // 漏加 = 双笔登记（身份条目 + 明细条目）合并时 Compose 明细丢失（A3-01/F5 模式同族）。
    // ================================================================

    private sealed class MergeEntityJ { public int Id { get; set; } }
    private sealed class MergeTargetJ { public int Id { get; set; } }
    private sealed class SubSourceJ1 { public int Id { get; set; } }
    private sealed class SubSourceJ2 { public int Id { get; set; } }
    private sealed class MergeEntityK { public int Id { get; set; } }
    private sealed class MergeTargetK { public int Id { get; set; } }

    private static RuntimeComposeMapping ComposeEntry(string targetName, string subSourceTypeName, string sourcePath)
        => new()
        {
            TargetPropertyName = targetName,
            SubSourceTypeName = subSourceTypeName,
            SourcePathNames = new[] { sourcePath },
        };

    [Fact]
    public void Register_IdentityThenDetail_ComposeMappings_UnionKept()
    {
        // 主场景（Fluent Compose 双笔登记形态）：身份段 ComposeMappings 空、明细段携带 N 条——
        // 合并臂缺失时 MergeComposeMappings(空, b) 虽按"空集取另一侧"保真，防御性断言
        // 明细不丢 + HasComposeMappings 布尔与数组同向（消费端的承载前提）
        var detail = new RuntimeMapperMetadata
        {
            SourceType = typeof(MergeEntityJ),
            TargetType = typeof(MergeTargetJ),
            SourceTypeFqn = typeof(MergeEntityJ).FullName!,
            TargetTypeFqn = typeof(MergeTargetJ).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            HasComposeMappings = true,
            ComposeMappings = new[]
            {
                ComposeEntry("Score", "SubSourceJ1", "IntProp"),
                ComposeEntry("StatusText", "SubSourceJ1", "StatusProp"),
                ComposeEntry("Title", "SubSourceJ2", "TitleText"),
            },
        };
        RuntimeSnapshotStore.Register(IdentityEntry(typeof(MergeEntityJ), typeof(MergeTargetJ)));
        RuntimeSnapshotStore.Register(detail);

        var merged = RuntimeSnapshotStore.FindByPair(typeof(MergeEntityJ), typeof(MergeTargetJ));

        Assert.NotNull(merged);
        Assert.True(merged!.HasComposeMappings);
        Assert.Equal(3, merged.ComposeMappings.Length);
        // 按 TargetPropertyName 可索引消费（消费端契约前提）
        Assert.Contains(merged.ComposeMappings, cm => cm.TargetPropertyName == "Title" && cm.SubSourceTypeName == "SubSourceJ2");
    }

    [Fact]
    public void Register_TwiceCompose_UnionDedup_ByTargetAndSubSource()
    {
        // 退化形态（两侧均明细）：重叠条目按 (TargetPropertyName, SubSourceTypeName) 去重、
        // 同键取 a（a 侧路径胜），非重叠并集保留——顺序确定（a 在前、b 新增追加）
        var first = new RuntimeMapperMetadata
        {
            SourceType = typeof(MergeEntityK),
            TargetType = typeof(MergeTargetK),
            SourceTypeFqn = typeof(MergeEntityK).FullName!,
            TargetTypeFqn = typeof(MergeTargetK).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            HasComposeMappings = true,
            ComposeMappings = new[]
            {
                ComposeEntry("Score", "SubSourceJ1", "IntPropFromA"),
                ComposeEntry("Title", "SubSourceJ2", "TitleText"),
            },
        };
        var second = new RuntimeMapperMetadata
        {
            SourceType = typeof(MergeEntityK),
            TargetType = typeof(MergeTargetK),
            SourceTypeFqn = typeof(MergeEntityK).FullName!,
            TargetTypeFqn = typeof(MergeTargetK).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            HasComposeMappings = true,
            ComposeMappings = new[]
            {
                ComposeEntry("Score", "SubSourceJ1", "IntPropFromB"),
                ComposeEntry("StatusText", "SubSourceJ2", "StatusProp"),
            },
        };
        RuntimeSnapshotStore.Register(first);
        RuntimeSnapshotStore.Register(second);

        var merged = RuntimeSnapshotStore.FindByPair(typeof(MergeEntityK), typeof(MergeTargetK));

        Assert.NotNull(merged);
        // 并集去重：重叠键 (Score, SubSourceJ1) 保留 1 条取 a 路径；非重叠 2 条追加 → 3 条
        Assert.Equal(3, merged!.ComposeMappings.Length);
        Assert.Equal("IntPropFromA", merged.ComposeMappings[0].SourcePathNames[0]);
        Assert.Equal("TitleText", merged.ComposeMappings[1].SourcePathNames[0]);
        Assert.Equal("StatusProp", merged.ComposeMappings[2].SourcePathNames[0]);
    }
}
