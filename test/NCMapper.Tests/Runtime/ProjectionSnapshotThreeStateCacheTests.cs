// （决策点 3）：三态/版本化缓存全量——
// ① 明细命中（快照为权威）/ ② 条目命中明细不可达（off 态身份键——reason 逐字 B9/F3）/ ③ miss（特性扫描腿兜底）；
// 身份/明细双笔登记合并（ComposeMappings 不丢）；Clear 边界（ContentVersion 递增）；
// 条目替换（失配重算收口回归）；P2ViewDir 场景（方向位翻转）。
// -3；先例 ProjectionMainChainSwitchTests（收口用例同族复验）。
// 隔离范式：[Collection("GlobalConfigState")] + 自足注册 + try/finally 清理（#60/#65/#366）。
// fixture 纯 POCO（手工 meta——避 D1 同对双声明；meta 与发射产物同构）。
using NCMapper;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class ProjectionSnapshotThreeStateCacheTests
{
    // ===== fixture（纯 POCO——手工注册，自足）=====

    public class T7CEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class T7CFluentDto { public int Id { get; set; } public string Title { get; set; } = ""; }
    public class T7CCrossDto { public int Id { get; set; } public string Level { get; set; } = ""; }
    public class T7CSubEntity { public int Id { get; set; } public string Level { get; set; } = ""; }
    public class T7CNoAttrEntity { public int Id { get; set; } }
    public class T7CViewDirDto { public int Id { get; set; } public string Kept { get; set; } = ""; }

    private static RuntimeTargetPropertySlot Slot(string name, string typeName, bool hasIgnore = false, int dir = 0)
        => new() { Name = name, TypeName = typeName, HasMapIgnore = hasIgnore, MapIgnoreDirection = dir };

    private static RuntimePropertyMapping Pm(string target, string source)
        => new() { Target = Slot(target, "String"), SourceName = source, SourcePathNames = new[] { source } };

    private static void ClearState()
    {
        RuntimeSnapshotStore.Clear();
        ProjectionExpressionFactory.ClearCache();
        EfCoreDegradationDetector.ClearCache();
        NCMapper.FreeSql.FreeSqlDegradationDetector.ClearCache();
    }

    private static ProjectionStrategy S() => new(
        MaxNestingDepth: 8, WarningDepth: 5, EnableCollectionProjection: true, TracePrefix: "[t7c three-state]");

    // ===== 三态①：明细命中（快照为权威）=====

    /// <summary>Fluent 明细命中：NeedsDegradation=false + 投影经 pm 消费（异名 Title←Name）——三态①全链。</summary>
    [Fact]
    public void State1_DetailHit_SnapshotAuthoritative()
    {
        try
        {
            ClearState();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T7CEntity), TargetType = typeof(T7CFluentDto),
                SourceTypeFqn = typeof(T7CEntity).FullName!, TargetTypeFqn = typeof(T7CFluentDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[] { Pm("Id", "Id"), Pm("Title", "Name") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Title", "String") },
            });

            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(T7CEntity), typeof(T7CFluentDto), out _),
                "三态①明细命中纯可翻译——不强制降级");
            Assert.True(RuntimeSnapshotStore.TryGetFluentDetailMeta(typeof(T7CFluentDto), out _),
                "on 态明细条目可达（TryGetFluentDetailMeta）");

            var expr = ProjectionExpressionFactory.Build<T7CEntity, T7CFluentDto>(S());
            var dto = expr.Compile()(new T7CEntity { Id = 1, Name = "N1" });
            Assert.Equal(1, dto.Id);
            Assert.Equal("N1", dto.Title);
        }
        finally { ClearState(); }
    }

    // ===== 三态②：off 态身份键（条目命中明细不可达——Fluent 通道强制降级）=====

    /// <summary>off 态身份键（Fluent、零明细）→ EFCore/FreeSql 双检测器强制降级；
    /// reason 逐字锁（B9/F3 红线——"off 态身份键/明细不可达" 分片段 Contains 承载）。</summary>
    [Fact]
    public void State2_OffStateIdentityKey_ForcesDegrade_ReasonVerbatim()
    {
        try
        {
            ClearState();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T7CEntity), TargetType = typeof(T7CFluentDto),
                SourceTypeFqn = typeof(T7CEntity).FullName!, TargetTypeFqn = typeof(T7CFluentDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent, // 身份键——零明细（发射端身份段无条件发射形态）
                Properties = Array.Empty<RuntimePropertyMapping>(),
                AllTargetSlots = Array.Empty<RuntimeTargetPropertySlot>(),
            });

            Assert.True(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(T7CFluentDto)),
                "off 态身份键命中 IsFluentRegisteredDto（Fluent 通道）");
            Assert.False(RuntimeSnapshotStore.TryGetFluentDetailMeta(typeof(T7CFluentDto), out _),
                "off 态明细不可达（TryGetFluentDetailMeta=false）");

            var needsEf = EfCoreDegradationDetector.NeedsDegradation(typeof(T7CEntity), typeof(T7CFluentDto), out var reasonEf);
            Assert.True(needsEf, "off 态 Fluent 对——EFCore 强制降级（B9 不放宽）");
            Assert.Contains("off 态身份键/明细不可达", reasonEf, StringComparison.Ordinal);

            var needsFs = FreeSqlDegradationDetector.NeedsDegradation(typeof(T7CEntity), typeof(T7CFluentDto), out var reasonFs);
            Assert.True(needsFs, "off 态 Fluent 对——FreeSql 强制降级（B9 不放宽）");
            Assert.Contains("off 态身份键/明细不可达", reasonFs, StringComparison.Ordinal);
        }
        finally { ClearState(); }
    }

    // ===== 三态③：miss（未注册对——特性扫描腿兜底，现状语义）=====

    /// <summary>未注册特性对：TryGetMappingDetail miss → NeedsDegradation=false（特性扫描腿兜底——现状判定序）。</summary>
    [Fact]
    public void State3_Miss_UnregisteredAttributePair_FallsToAttributeScan()
    {
        try
        {
            ClearState();
            Assert.False(RuntimeSnapshotStore.TryGetMappingDetail(typeof(T7CNoAttrEntity), typeof(T7CFluentDto), out _),
                "未注册对——注册表 miss（三态③）");
            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(T7CEntity), typeof(T7CFluentDto), out _),
                "miss 态纯 POCO 无声明——无强制降级（特性扫描腿零命中）");
        }
        finally { ClearState(); }
    }

    // ===== 身份/明细双笔登记合并（ComposeMappings 不丢——F5 同族防线）=====

    /// <summary>身份笔先登记 + 明细笔（含 ComposeMappings）后登记 → 合并条目 Properties/ComposeMappings 双全；
    /// 幂等合并契约（明细优先）——与 ModuleInitializer 跨文件执行顺序无关（先身份后明细 = 发射实态）。</summary>
    [Fact]
    public void DualEntryRegister_Merge_ComposeMappingsPreserved()
    {
        try
        {
            ClearState();

            // 身份笔（零明细）
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T7CEntity), TargetType = typeof(T7CCrossDto),
                SourceTypeFqn = typeof(T7CEntity).FullName!, TargetTypeFqn = typeof(T7CCrossDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                Properties = Array.Empty<RuntimePropertyMapping>(),
            });

            // 明细笔（pm + ComposeMappings）
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T7CEntity), TargetType = typeof(T7CCrossDto),
                SourceTypeFqn = typeof(T7CEntity).FullName!, TargetTypeFqn = typeof(T7CCrossDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                HasComposeMappings = true,
                Properties = new[] { Pm("Id", "Id") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
                ComposeMappings = new[]
                {
                    new RuntimeComposeMapping
                    {
                        TargetPropertyName = "Level",
                        SubSourceTypeRef = typeof(T7CSubEntity),
                        SubSourceTypeName = typeof(T7CSubEntity).FullName!,
                        SourcePathNames = new[] { "Level" },
                    },
                },
            });

            var merged = RuntimeSnapshotStore.FindByPair(typeof(T7CEntity), typeof(T7CCrossDto));
            Assert.NotNull(merged);
            Assert.True(merged!.Properties.Length > 0, "合并后明细不丢（身份笔不覆盖已登记明细）");
            Assert.Equal(1, merged.ComposeMappings.Length);
            Assert.Equal("Level", merged.ComposeMappings[0].TargetPropertyName);
            Assert.True(merged.HasComposeMappings, "HasComposeMappings 合并臂（OR 语义——A3-01/F5 模式）");
            Assert.True(RuntimeSnapshotStore.TryGetFluentDetailMeta(typeof(T7CCrossDto), out _),
                "合并终态 = on 态明细（TryGetFluentDetailMeta 可达）");
        }
        finally { ClearState(); }
    }

    // ===== Clear 边界（三容器全清 + ContentVersion 递增）=====

    /// <summary>Clear 后按对/按目标/Fluent 三查询全 miss + ContentVersion 递增（漏 Clear 臂 = 旧条目"失配"错判防线）。</summary>
    [Fact]
    public void ClearBoundary_RegistryEmptied_ContentVersionBumps()
    {
        try
        {
            ClearState();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T7CEntity), TargetType = typeof(T7CFluentDto),
                SourceTypeFqn = typeof(T7CEntity).FullName!, TargetTypeFqn = typeof(T7CFluentDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
                Properties = new[] { Pm("Id", "Id"), Pm("Title", "Name") },
            });
            var versionBefore = RuntimeSnapshotStore.ContentVersion;

            RuntimeSnapshotStore.Clear();

            Assert.Null(RuntimeSnapshotStore.FindByPair(typeof(T7CEntity), typeof(T7CFluentDto)));
            Assert.Empty(RuntimeSnapshotStore.FindByTarget(typeof(T7CFluentDto)));
            Assert.False(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(T7CFluentDto)));
            Assert.True(RuntimeSnapshotStore.ContentVersion > versionBefore,
                "Clear 臂 ContentVersion 递增（防陈旧缓存续命）");
        }
        finally { ClearState(); }
    }

    // ===== 条目替换（ContentVersion 失配重算——收口回归）=====

    /// <summary>失配重算：同对再注册（ContentVersion++）→ 旧条目被替换（NotSame）+ CacheEntryCount 不沉淀 + 新声明生效。</summary>
    [Fact]
    public void CacheEntryReplace_ContentVersionMismatch_Regression()
    {
        try
        {
            ClearState();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T7CEntity), TargetType = typeof(T7CFluentDto),
                SourceTypeFqn = typeof(T7CEntity).FullName!, TargetTypeFqn = typeof(T7CFluentDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
                Properties = new[] { Pm("Id", "Id"), Pm("Title", "Name") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Title", "String") },
            });
            var expr1 = ProjectionExpressionFactory.Build<T7CEntity, T7CFluentDto>(S());
            var count1 = ProjectionExpressionFactory.CacheEntryCount;

            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T7CEntity), TargetType = typeof(T7CFluentDto),
                SourceTypeFqn = typeof(T7CEntity).FullName!, TargetTypeFqn = typeof(T7CFluentDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
                Properties = new[] { Pm("Id", "Id"), Pm("Title", "Name") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Title", "String") },
            });
            var expr2 = ProjectionExpressionFactory.Build<T7CEntity, T7CFluentDto>(S());

            Assert.NotSame(expr1, expr2); // 失配重算 → 新表达式（条目替换）
            Assert.Equal(count1, ProjectionExpressionFactory.CacheEntryCount); // 同对——条目数不沉淀
            var dto = expr2.Compile()(new T7CEntity { Id = 2, Name = "N2" });
            Assert.Equal("N2", dto.Title); // 重算后行为正确
        }
        finally { ClearState(); }
    }

    // ===== P2ViewDir 场景（方向位翻转——独立对防 s_cache 串扰，版本化缓存行为可观测）=====

    /// <summary>P2ViewDir 场景：先注册 ToDto 忽略（投影跳过）→ Clear 后重注册无忽略（声明真实变更
    /// ——ContentVersion 递增）→ 失配重算 → 绑定翻转。独立类型对（对齐 P2 先例——s_cache 键
    /// (策略,S,T) 无注册态维度，跨用例串扰防线）。注意：同对合并语义（明细优先——HasMapIgnore OR
    /// 并集）永不丢声明，方向位翻转必须走 Clear+重注册形态（快照为权威的声明变更载体）。</summary>
    [Fact]
    public void P2ViewDir_Scenario_DirectionFlip()
    {
        try
        {
            ClearState();

            // 第一笔：Kept 带 ToDto 忽略 → 投影正向跳过
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T7CEntity), TargetType = typeof(T7CViewDirDto),
                SourceTypeFqn = typeof(T7CEntity).FullName!, TargetTypeFqn = typeof(T7CViewDirDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
                Properties = new[]
                {
                    Pm("Id", "Id"),
                    new RuntimePropertyMapping
                    {
                        Target = Slot("Kept", "String", hasIgnore: true, dir: 1), // ToDto——正向跳过
                        SourceName = "Name", SourcePathNames = new[] { "Name" },
                    },
                },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Kept", "String") },
            });
            var expr1 = ProjectionExpressionFactory.Build<T7CEntity, T7CViewDirDto>(S());
            var dto1 = expr1.Compile()(new T7CEntity { Id = 3, Name = "N3" });
            Assert.Equal("", dto1.Kept);
            Assert.Equal(1, ProjectionExpressionFactory.CacheEntryCount);

            // 第二笔（同对声明变更——Clear 后重注册，ContentVersion 已递增）：方向位摘除 → 失配重算 → 绑定翻转
            ClearState();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T7CEntity), TargetType = typeof(T7CViewDirDto),
                SourceTypeFqn = typeof(T7CEntity).FullName!, TargetTypeFqn = typeof(T7CViewDirDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
                Properties = new[] { Pm("Id", "Id"), Pm("Kept", "Name") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Kept", "String") },
            });
            var expr2 = ProjectionExpressionFactory.Build<T7CEntity, T7CViewDirDto>(S());
            Assert.NotSame(expr1, expr2);
            Assert.Equal(1, ProjectionExpressionFactory.CacheEntryCount); // 条目替换不沉淀
            var dto2 = expr2.Compile()(new T7CEntity { Id = 4, Name = "N4" });
            Assert.Equal("N4", dto2.Kept); // 翻转生效——版本化缓存行为可观测
        }
        finally { ClearState(); }
    }
}
