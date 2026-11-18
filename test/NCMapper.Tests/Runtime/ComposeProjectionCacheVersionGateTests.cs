// 归档补丁（Q3① 用户裁定"建议 1"处置验证）：
// s_composeCache/s_compose3Cache 版本门控行为锁——ContentVersion 失配重算 + 索引器替换
//（与主链 ProjectionExpressionFactory.Cache_ContentVersionMismatch_ReplacesEntry 同构）。
// 场景：运行时声明变更（同对 Fluent Compose 对，cm 绑定源变更）后，双表门面重算新绑定
//（无门控时 TryAdd 恒命中旧条目 → Level 仍为旧值——红测对照语义在注释中登记）。
// 自足注册范式（D-14）：fixture 纯 POCO + try/finally 清理。
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using NCMapper.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class ComposeProjectionCacheVersionGateTests
{
    public class Q9Entity { public int Id { get; set; } public Q9Sub? Sub { get; set; } }
    public class Q9Sub { public int Id { get; set; } public string Level { get; set; } = ""; public string Rank { get; set; } = ""; }
    public class Q9Dto { public int Id { get; set; } public string Level { get; set; } = ""; }

    private static RuntimeTargetPropertySlot Slot(string name, string typeName)
        => new() { Name = name, TypeName = typeName };

    private static RuntimePropertyMapping Pm(string target, string source)
        => new() { Target = Slot(target, "String"), SourceName = source, SourcePathNames = new[] { source } };

    private static RuntimeComposeMapping Cm(string path) => new()
    {
        TargetPropertyName = "Level",
        SubSourceTypeRef = typeof(Q9Sub),
        SubSourceTypeName = typeof(Q9Sub).FullName!,
        SourcePathNames = new[] { path },
    };

    private static RuntimeMapperMetadata Meta(string cmPath) => new()
    {
        SourceType = typeof(Q9Entity), TargetType = typeof(Q9Dto),
        SourceTypeFqn = typeof(Q9Entity).FullName!, TargetTypeFqn = typeof(Q9Dto).FullName!,
        Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
        HasComposeMappings = true,
        Properties = new[] { Pm("Id", "Id") },
        AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
        ComposeMappings = new[] { Cm(cmPath) },
    };

    [Fact]
    public void ComposeCache_ContentVersionMismatch_ReplacesEntry()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            ProjectionExpressionFactory.ClearCache();
            FreeSqlProjectionBuilder.ClearCache();

            // 第一版声明：Level ← Sub.Level
            RuntimeSnapshotStore.Register(Meta("Level"));
            var main = new Q9Entity { Id = 1, Sub = new Q9Sub { Id = 7, Level = "Gold", Rank = "Platinum" } };

            var expr1 = FreeSqlProjectionBuilder.BuildCompose<Q9Entity, Q9Sub, Q9Dto>();
            var dto1 = expr1.Compile()(main, main.Sub!);
            Assert.Equal("Gold", dto1.Level);

            // 声明变更（同对重注册——ContentVersion 递增）：Level ← Sub.Rank
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta("Rank"));

            var expr2 = FreeSqlProjectionBuilder.BuildCompose<Q9Entity, Q9Sub, Q9Dto>();
            Assert.NotSame(expr1, expr2); // 版本失配 → 重算新表达式（条目替换）

            var dto2 = expr2.Compile()(main, main.Sub!);
            Assert.Equal("Platinum", dto2.Level); // 新声明生效（无门控时 expr2==expr1 → 仍 "Gold"）
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            ProjectionExpressionFactory.ClearCache();
            FreeSqlProjectionBuilder.ClearCache();
        }
    }

    // ===== 归档复查补丁行为锁（P2-2 D1 扩容 / P2-3 取首统一）=====

    /// <summary>P2-2：D1 臂一判据随扩容同步——Properties 空 + ComposeMappings 非空的
    /// Fluent 同向对（目标具 [MapFrom(S)] 锚）Register 须抛 D1（原判据仅看 Properties → 绕门）。
    /// 锚对复用 DdsMiss（DegradationDetectorThreeStateSwitchTests 既有 [MapFrom] fixture——零发射增量，D-14 纪律：
    /// 初版自建 Q9CAnchorDto [MapFrom] 进入 test 编译面 → 字节门真 DIFF 证红 → 改复用既有锚对回基线）。</summary>
    [Fact]
    public void P2_2_D1Gate_ComposeOnlySameDirection_Throws()
    {
        try
        {
            RuntimeSnapshotStore.Clear();

            // 锚对：DdsMissDto 标 [MapFrom(DdsMissEntity)]（同向）——Fluent Compose-only 注册须被 D1 拦截
            Assert.Throws<MapConfigurationException>(() =>
                RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
                {
                    SourceType = typeof(DegradationDetectorThreeStateSwitchTests.DdsMissEntity),
                    TargetType = typeof(DegradationDetectorThreeStateSwitchTests.DdsMissDto),
                    SourceTypeFqn = typeof(DegradationDetectorThreeStateSwitchTests.DdsMissEntity).FullName!,
                    TargetTypeFqn = typeof(DegradationDetectorThreeStateSwitchTests.DdsMissDto).FullName!,
                    Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
                    HasComposeMappings = true,
                    Properties = Array.Empty<RuntimePropertyMapping>(), // Compose-only——原判据绕门形态
                    ComposeMappings = new[] { new RuntimeComposeMapping
                    {
                        TargetPropertyName = "Level", SubSourceTypeRef = typeof(Q9CSub),
                        SubSourceTypeName = typeof(Q9CSub).FullName!, SourcePathNames = new[] { "Level" },
                    } },
                }));
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    /// <summary>P2-3：Compose 同目标多副源——主链与内核胜出规则统一为"取首"
    ///（双 cm 同 TargetPropertyName，首条 Level←Level 与次条 Level←Rank——主链 Build 绑首条语义）。</summary>
    [Fact]
    public void P2_3_MultiSubSourceSameTarget_TakesFirst_Unified()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            ProjectionExpressionFactory.ClearCache();

            var meta = new RuntimeMapperMetadata
            {
                SourceType = typeof(Q9CEntity), TargetType = typeof(Q9CDto),
                SourceTypeFqn = typeof(Q9CEntity).FullName!, TargetTypeFqn = typeof(Q9CDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
                HasComposeMappings = true,
                Properties = new[] { Pm("Id", "Id") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
                ComposeMappings = new[]
                {
                    new RuntimeComposeMapping { TargetPropertyName = "Level", SubSourceTypeRef = typeof(Q9CSub), SubSourceTypeName = typeof(Q9CSub).FullName!, SourcePathNames = new[] { "Level" } },
                    new RuntimeComposeMapping { TargetPropertyName = "Level", SubSourceTypeRef = typeof(Q9CSub), SubSourceTypeName = typeof(Q9CSub).FullName!, SourcePathNames = new[] { "Rank" } },
                },
            };
            RuntimeSnapshotStore.Register(meta);

            var main = new Q9CEntity { Id = 1, Sub = new Q9CSub { Id = 7, Level = "Gold", Rank = "Platinum" } };
            // 主链（ProjectionExpressionFactory）取首 → "Gold"
            var dto = ProjectionExpressionFactory.Build<Q9CEntity, Q9CDto>(new(
                MaxNestingDepth: 8, WarningDepth: 5, EnableCollectionProjection: true, TracePrefix: "[q9 p23]")).Compile()(main);
            Assert.Equal("Gold", dto.Level);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            ProjectionExpressionFactory.ClearCache();
        }
    }
}

public class Q9CEntity { public int Id { get; set; } public Q9CSub? Sub { get; set; } }
public class Q9CSub { public int Id { get; set; } public string Level { get; set; } = ""; public string Rank { get; set; } = ""; }

/// <summary>P2-3 无锚载体（多副源同目标——取首统一语义）。</summary>
public class Q9CDto
{
    public int Id { get; set; }
    public string Level { get; set; } = "";
}
