// D1 运行时同对双声明门（两臂合拢 + 零误伤边界）
// + ContentVersion 递增面/读侧契约（Register 新条目/合并替换/Clear 三入口，幂等不递增）。
// 隔离范式（经验库 #60/#65，同 RuntimeSnapshotStoreRegisterMergeTests）：[Collection("GlobalConfigState")]
// 防跨集合并行 + 每场景独立嵌套类型对（唯一 (Source,Target) 键）——不调用 Clear()，零污染其他测试。
using NCMapper.Attributes;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class RuntimeSnapshotStoreD1GateAndVersionTests
{
    // ================================================================
    // 场景类型对（每场景唯一键）
    // ================================================================

    // 臂一场景：目标类型具 [MapFrom(S)] 锚（同向对）——Fluent 明细注册 → 抛
    private sealed class D1AnchorEntity { public int Id { get; set; } }

    [MapFrom(typeof(D1AnchorEntity))]
    private sealed class D1AnchorDto { public int Id { get; set; } }

    // 臂一负向场景（跨对并存）：目标类型具 [MapFrom(A)] 锚，Fluent 注册 (B, Dto)——不同源 → 放行
    private sealed class D1CrossEntityA { public int Id { get; set; } }

    [MapFrom(typeof(D1CrossEntityA))]
    private sealed class D1CrossDto { public int Id { get; set; } }

    private sealed class D1CrossEntityB { public int Id { get; set; } }

    // 臂一负向场景（同对非明细）：同对 + 锚 + 空明细（MixedForm 形态——分叉面只在明细形态）→ 放行
    private sealed class D1MixedEntity { public int Id { get; set; } }

    [MapFrom(typeof(D1MixedEntity))]
    private sealed class D1MixedDto { public int Id { get; set; } }

    // 臂二场景：异通道条目先在表（双向）→ 抛
    private sealed class D1Arm2Entity { public int Id { get; set; } }
    private sealed class D1Arm2Dto { public int Id { get; set; } }

    // 版本化场景类型对
    private sealed class VerEntity { public int Id { get; set; } }
    private sealed class VerDto { public int Id { get; set; } }
    private sealed class VerEntityMerge { public int Id { get; set; } }
    private sealed class VerDtoMerge { public int Id { get; set; } }
    private sealed class VerEntityManual { public int Id { get; set; } }
    private sealed class VerDtoManual { public int Id { get; set; } }

    private static RuntimeMapperMetadata FluentDetail(Type source, Type target)
        => new()
        {
            SourceType = source,
            TargetType = target,
            SourceTypeFqn = source.FullName!,
            TargetTypeFqn = target.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            // 明细形态（#151 纪律：Properties + AllTargetSlots 成对承载）
            Properties = new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" },
                    SourceName = "Id",
                },
            },
            AllTargetSlots = new[] { new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" } },
        };

    private static RuntimeMapperMetadata FluentIdentity(Type source, Type target)
        => new()
        {
            SourceType = source,
            TargetType = target,
            SourceTypeFqn = source.FullName!,
            TargetTypeFqn = target.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            // 身份形态（零明细——off 态身份键）
        };

    private static RuntimeMapperMetadata MapFromEntry(Type source, Type target)
        => new()
        {
            SourceType = source,
            TargetType = target,
            SourceTypeFqn = source.FullName!,
            TargetTypeFqn = target.FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            // 手工模拟特性通道（发射端 [MapFrom] 通道形态——无 IsFluentForward）
        };

    // ================================================================
    // D1 臂一（锚探测——同向对 + 明细形态）
    // ================================================================

    [Fact]
    public void Register_FluentDetail_WithSamePairAnchor_Throws()
    {
        // 同向对明细形态 → 抛 MapConfigurationException（消息含"同对"关键字 + NCMAP_D1 检索标识）
        var ex = Assert.Throws<MapConfigurationException>(
            () => RuntimeSnapshotStore.Register(FluentDetail(typeof(D1AnchorEntity), typeof(D1AnchorDto))));

        Assert.Contains("同对", ex.Message);
        Assert.Contains("NCMAP_D1", ex.Message);
    }

    [Fact]
    public void Register_CrossPair_NoThrow()
    {
        // 跨对并存（[MapFrom(A)] Dto + Fluent 注册 (B, Dto)——F11 明确不触发的合法形态）：放行
        //（收窄判据——臂一要求锚对 (S,T) 与注册对同向；此处锚对 (A,Dto) ≠ 注册对 (B,Dto)）
        var meta = FluentDetail(typeof(D1CrossEntityB), typeof(D1CrossDto));
        RuntimeSnapshotStore.Register(meta);

        var merged = RuntimeSnapshotStore.FindByPair(typeof(D1CrossEntityB), typeof(D1CrossDto));
        Assert.NotNull(merged);
        Assert.True(merged!.Properties.Length > 0);
    }

    [Fact]
    public void Register_SamePair_EmptyDetail_NoThrow()
    {
        // 同对 + 锚 + 空明细（MixedForm 形态——非明细形态放行，投影走反射兜底腿=按特性锚读，零分叉）
        RuntimeSnapshotStore.Register(FluentIdentity(typeof(D1MixedEntity), typeof(D1MixedDto)));

        var merged = RuntimeSnapshotStore.FindByPair(typeof(D1MixedEntity), typeof(D1MixedDto));
        Assert.NotNull(merged);
        Assert.Equal(0, merged!.Properties.Length);
    }

    // ================================================================
    // D1 臂二（异通道条目先在表——双向）
    // ================================================================

    [Fact]
    public void Register_CrossChannel_MapFromFirst_Throws()
    {
        RuntimeSnapshotStore.Register(MapFromEntry(typeof(D1Arm2Entity), typeof(D1Arm2Dto)));

        var ex = Assert.Throws<MapConfigurationException>(
            () => RuntimeSnapshotStore.Register(FluentIdentity(typeof(D1Arm2Entity), typeof(D1Arm2Dto))));

        Assert.Contains("同对", ex.Message);
        Assert.Contains("NCMAP_D1", ex.Message);
    }

    [Fact]
    public void Register_CrossChannel_FluentFirst_Throws()
    {
        // 反向登记顺序（Fluent 先在、MapFrom 后注册）——臂二双向检测
        RuntimeSnapshotStore.Register(FluentIdentity(typeof(D1CrossEntityA), typeof(D1Arm2Dto)));

        var ex = Assert.Throws<MapConfigurationException>(
            () => RuntimeSnapshotStore.Register(MapFromEntry(typeof(D1CrossEntityA), typeof(D1Arm2Dto))));

        Assert.Contains("同对", ex.Message);
    }

    [Fact]
    public void Register_ManualMapFromOnly_NoThrow()
    {
        // 单通道放行（手工模拟特性通道——臂一要求 Channel==Fluent 不触发；无既有异通道条目臂二不触发）
        RuntimeSnapshotStore.Register(MapFromEntry(typeof(VerEntityManual), typeof(VerDtoManual)));

        var merged = RuntimeSnapshotStore.FindByPair(typeof(VerEntityManual), typeof(VerDtoManual));
        Assert.NotNull(merged);
        Assert.Equal(RuntimeMappingChannel.MapFrom, merged!.Channel);
    }

    // ================================================================
    // ContentVersion（三入口递增 + 读侧契约）
    // 注：MergeMetadata 恒构造新实例（无 ReferenceEquals 短路生效形态）——同对重复注册
    // 走合并替换路径每次递增，为设计内递增面（方案书递增面清单 = Register 新条目 + 合并路径 + Clear）
    // ================================================================

    [Fact]
    public void ContentVersion_Increments_OnNewEntry()
    {
        var before = RuntimeSnapshotStore.ContentVersion;
        RuntimeSnapshotStore.Register(FluentIdentity(typeof(VerEntity), typeof(VerDto)));
        var after = RuntimeSnapshotStore.ContentVersion;

        Assert.True(after > before, $"ContentVersion 应递增：before={before}, after={after}");
    }

    [Fact]
    public void ContentVersion_Increments_OnMergeReplace()
    {
        // 合并替换路径（身份 + 明细双笔登记 → ReplaceEntry）亦递增
        RuntimeSnapshotStore.Register(FluentIdentity(typeof(VerEntityMerge), typeof(VerDtoMerge)));
        var before = RuntimeSnapshotStore.ContentVersion;
        RuntimeSnapshotStore.Register(FluentDetail(typeof(VerEntityMerge), typeof(VerDtoMerge)));
        var after = RuntimeSnapshotStore.ContentVersion;

        Assert.True(after > before, $"合并替换路径应递增：before={before}, after={after}");
    }

    [Fact]
    public void ContentVersion_Increments_OnClear()
    {
        // Clear 入口递增（SnapshotScope 既有 Clear 范式——套件 Clear-tolerant，每用例自足注册）
        var before = RuntimeSnapshotStore.ContentVersion;
        RuntimeSnapshotStore.Clear();
        var after = RuntimeSnapshotStore.ContentVersion;

        Assert.True(after > before, $"Clear 应递增：before={before}, after={after}");
        Assert.Null(RuntimeSnapshotStore.FindByPair(typeof(VerEntity), typeof(VerDto)));
    }
}
