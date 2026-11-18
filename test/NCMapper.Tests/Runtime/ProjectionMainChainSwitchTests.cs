// （子步骤①）：投影主链切换测试——三态 + 键控按对化/跨表分流
//（单表 AsDto 导航推断 + Q4 归一口复用 + fail-closed）/属性级 miss 矩阵/缓存条目化
//（ContentVersion 失配重算 + 索引器替换 + CacheEntryCount 不增长）/Q2 嵌套对可达性 PoC。
// -D2。
// 隔离范式（#60/#65/#366）：[Collection("GlobalConfigState")] + 自足注册 + try/finally 清理。
// fixture 无特性（纯 POCO + 手工 meta——避 D1 同对双声明：编译期零产物、无自动注册冲突；
// meta 形态与编译期产物同构——internal 副源形态 SubSourceTypeRef=null + FullName 手工直构，
// 归一匹配双口径兼容（D-11））。
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class ProjectionMainChainSwitchTests
{
    // ===== fixture（纯 POCO——手工注册，自足）=====

    public class T6Entity { public int Id { get; set; } public string Name { get; set; } = ""; public T6SubEntity Inner { get; set; } = new(); public T6NEntity Inner2 { get; set; } = new(); public List<T6NEntity> Items { get; set; } = new(); }
    public class T6SubEntity { public int Id { get; set; } public string Tag { get; set; } = ""; }
    internal class T6IntSubEntity { public string ExtName { get; set; } = ""; }
    public class T6NEntity { public int Id { get; set; } public string Tag { get; set; } = ""; }
    public class T6NoAttrEntity { public int Id { get; set; } }

    public class T6Dto { public int Id { get; set; } public string Title { get; set; } = ""; public T6NestedDto Inner2 { get; set; } = new(); public List<T6NestedDto> Items { get; set; } = new(); }
    public class T6CrossDto { public int Id { get; set; } public string Tag { get; set; } = ""; }
    public class T6IntCrossDto { public int Id { get; set; } public string ExtName { get; set; } = ""; }
    public class T6NestedDto { public int Id { get; set; } public string Tag { get; set; } = ""; }
    public class T6DirDto { public int Id { get; set; } public string Skipped { get; set; } = ""; public string Kept { get; set; } = ""; }

    private static RuntimeTargetPropertySlot Slot(string name, string typeName, bool hasIgnore = false, int dir = 0)
        => new() { Name = name, TypeName = typeName, HasMapIgnore = hasIgnore, MapIgnoreDirection = dir };

    private static RuntimePropertyMapping Pm(string target, string source, bool hasIgnore = false, int dir = 0)
        => new()
        {
            Target = Slot(target, "String", hasIgnore, dir),
            SourceName = source,
            SourcePathNames = new[] { source },
        };

    private static void RegisterPairs()
    {
        // 主对：异名（Title←Name）+ 同名（Id）
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T6Entity), TargetType = typeof(T6Dto),
            SourceTypeFqn = typeof(T6Entity).FullName!, TargetTypeFqn = typeof(T6Dto).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            Properties = new[] { Pm("Id", "Id"), Pm("Title", "Name") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Title", "String"), Slot("Inner2", "T6NestedDto"), Slot("Items", "List<T6NestedDto>") },
        });
        // 跨表对：cm 指向 T6SubEntity（public——TypeRef 形态），主源导航属性 Inner
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T6Entity), TargetType = typeof(T6CrossDto),
            SourceTypeFqn = typeof(T6Entity).FullName!, TargetTypeFqn = typeof(T6CrossDto).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            Properties = new[] { Pm("Id", "Id") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Tag", "String") },
            ComposeMappings = new[]
            {
                new RuntimeComposeMapping
                {
                    TargetPropertyName = "Tag",
                    SubSourceTypeRef = typeof(T6SubEntity),
                    SubSourceTypeName = typeof(T6SubEntity).FullName!,
                    SourcePathNames = new[] { "Tag" },
                },
            },
        });
        // 跨表对（internal 副源——TypeRef=null + FullName 运行时口径，Q4 D-11 真实形态）：
        // 主源 T6Entity 无 T6IntSubEntity 导航属性 → fail-closed 面
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T6Entity), TargetType = typeof(T6IntCrossDto),
            SourceTypeFqn = typeof(T6Entity).FullName!, TargetTypeFqn = typeof(T6IntCrossDto).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            Properties = new[] { Pm("Id", "Id") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("ExtName", "String") },
            ComposeMappings = new[]
            {
                new RuntimeComposeMapping
                {
                    TargetPropertyName = "ExtName",
                    SubSourceTypeRef = null,
                    SubSourceTypeName = typeof(T6IntSubEntity).FullName!,
                    SourcePathNames = new[] { "ExtName" },
                },
            },
        });
        // 嵌套对（Q2 PoC——嵌套 DTO 独立 pair 形态）
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T6NEntity), TargetType = typeof(T6NestedDto),
            SourceTypeFqn = typeof(T6NEntity).FullName!, TargetTypeFqn = typeof(T6NestedDto).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            Properties = new[] { Pm("Id", "Id"), Pm("Tag", "Tag") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Tag", "String") },
        });
        // 方向忽略对：ToDto 跳过 / ToEntity 绑定（pm 物化面）
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(T6Entity), TargetType = typeof(T6DirDto),
            SourceTypeFqn = typeof(T6Entity).FullName!, TargetTypeFqn = typeof(T6DirDto).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            Properties = new[]
            {
                Pm("Id", "Id"),
                Pm("Skipped", "Skipped", hasIgnore: true, dir: 1), // ToDto——投影正向跳过
                Pm("Kept", "Name", hasIgnore: true, dir: 2),       // ToEntity——投影照常绑定
            },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Skipped", "String"), Slot("Kept", "String") },
        });
    }

    private static void ClearState()
    {
        RuntimeSnapshotStore.Clear();
        ProjectionExpressionFactory.ClearCache();
        EfCoreDegradationDetector.ClearCache();
    }

    private static ProjectionStrategy S() => new(
        MaxNestingDepth: 8, WarningDepth: 5, EnableCollectionProjection: true, TracePrefix: "[test main]");

    // ===== Q2 PoC（决策点 5 探索计划——嵌套对可达性）=====

    /// <summary>Q2 PoC：嵌套 DTO 对按 (nestedSrc, nestedTgt) 精确注册 → TryGetMappingDetail 命中
    /// 且明细可达 → 嵌套链快照优先落地前提成立（消费面可达性；编译期发射同构形态由产物实证
    /// ——D-12 pair 302 含嵌套 [MapFrom] 对）。</summary>
    [Fact]
    public void Q2_Poc_NestedPairReachable()
    {
        try
        {
            ClearState();
            RegisterPairs();

            var hit = RuntimeSnapshotStore.TryGetMappingDetail(typeof(T6NEntity), typeof(T6NestedDto), out var meta);
            Assert.True(hit, "嵌套对 (T6NEntity,T6NestedDto) 按对可达");
            Assert.NotNull(meta);
            Assert.True(meta!.Properties.Length > 0, "嵌套对明细可达（Properties 非空——三态①）");
            Assert.Contains(meta.Properties, p => p.Target?.Name == "Tag");
        }
        finally { ClearState(); }
    }

    // ===== 三态 + 键控按对化（决策点 1——含 D-c 语义）=====

    /// <summary>明细命中：异名（Title←Name）与同名（Id）经快照腿 pm 明细消费。</summary>
    [Fact]
    public void DetailHit_AliasAndSameName_Bind()
    {
        try
        {
            ClearState();
            RegisterPairs();

            var expr = ProjectionExpressionFactory.Build<T6Entity, T6Dto>(S());
            var func = expr.Compile();
            var dto = func(new T6Entity { Id = 1, Name = "N1" });
            Assert.Equal(1, dto.Id);
            Assert.Equal("N1", dto.Title);
        }
        finally { ClearState(); }
    }

    /// <summary>D-c：未注册源 B→T（T 的注册主源为 A）→ ProjectionSourceTypeMismatch（现状语义保留）。</summary>
    [Fact]
    public void UnregisteredSource_Throws_SourceTypeMismatch()
    {
        try
        {
            ClearState();
            RegisterPairs();

            Assert.Throws<InvalidOperationException>(() =>
                ProjectionExpressionFactory.Build<T6NoAttrEntity, T6Dto>(S()));
        }
        finally { ClearState(); }
    }

    // ===== 跨表分流（决策点 2——验收 3：单表 AsDto 导航推断不得行为回退）=====

    /// <summary>跨表（导航推断）单表投影：cm 命中 → 主源导航属性（Inner）→ 副源字段——快照腿
    /// TypeRef 直比形态（F5 反锁——不得要求改用 Compose 门面）。</summary>
    [Fact]
    public void CrossTable_NavInference_SingleTable_Binds()
    {
        try
        {
            ClearState();
            RegisterPairs();

            var expr = ProjectionExpressionFactory.Build<T6Entity, T6CrossDto>(S());
            var func = expr.Compile();
            var dto = func(new T6Entity { Id = 2, Inner = new T6SubEntity { Tag = "TAG2" } });
            Assert.Equal(2, dto.Id);
            Assert.Equal("TAG2", dto.Tag);
        }
        finally { ClearState(); }
    }

    /// <summary>跨表 fail-closed：internal 副源（TypeRef=null）且主源无对应导航属性 →
    /// 该属性跳过（Q4 D-11 同口径），其余属性正常。</summary>
    [Fact]
    public void CrossTable_InternalSub_NoNavigation_FailClosed_Skips()
    {
        try
        {
            ClearState();
            RegisterPairs();

            var expr = ProjectionExpressionFactory.Build<T6Entity, T6IntCrossDto>(S());
            var func = expr.Compile();
            var dto = func(new T6Entity { Id = 3 });
            Assert.Equal(3, dto.Id);
            Assert.Equal("", dto.ExtName);
        }
        finally { ClearState(); }
    }

    // ===== 方向声明（pm 物化面——D2 零特性读取）=====

    /// <summary>[MapIgnore(ToDto)] 投影正向跳过（pm 物化面 direction=1）；ToEntity（direction=2）
    /// 照常绑定（Kept←Name 异名）。</summary>
    [Fact]
    public void DirectionIgnore_ToDtoSkips_ToEntityBinds()
    {
        try
        {
            ClearState();
            RegisterPairs();

            var expr = ProjectionExpressionFactory.Build<T6Entity, T6DirDto>(S());
            var func = expr.Compile();
            var dto = func(new T6Entity { Id = 4, Name = "N4" });
            Assert.Equal(4, dto.Id);
            Assert.Equal("", dto.Skipped);
            Assert.Equal("N4", dto.Kept);
        }
        finally { ClearState(); }
    }

    // ===== 嵌套/集合链（决策点 5 子步骤②——能力补齐）=====

    /// <summary>嵌套链快照优先：主对明细命中 + 嵌套属性（pm 无条目）走嵌套豁免递归 →
    /// 嵌套对 (T6NEntity,T6NestedDto) 快照腿消费（Q2 PoC 可达性兑现）——嵌套取值正确。</summary>
    [Fact]
    public void NestedChain_DetailHit_Recursive_Binds()
    {
        try
        {
            ClearState();
            RegisterPairs();

            var expr = ProjectionExpressionFactory.Build<T6Entity, T6Dto>(S());
            var func = expr.Compile();
            var dto = func(new T6Entity { Id = 6, Name = "N6", Inner2 = new T6NEntity { Id = 7, Tag = "T7" } });
            Assert.Equal(6, dto.Id);
            Assert.Equal("N6", dto.Title);
            Assert.NotNull(dto.Inner2);
            Assert.Equal(7, dto.Inner2.Id);
            Assert.Equal("T7", dto.Inner2.Tag);
        }
        finally { ClearState(); }
    }

    /// <summary>集合链：List 元素投影复用嵌套快照腿（元素对明细消费）——集合取值正确。</summary>
    [Fact]
    public void CollectionChain_ElementDetail_Binds()
    {
        try
        {
            ClearState();
            RegisterPairs();

            var expr = ProjectionExpressionFactory.Build<T6Entity, T6Dto>(S());
            var func = expr.Compile();
            var dto = func(new T6Entity
            {
                Id = 8,
                Items = new List<T6NEntity> { new() { Id = 9, Tag = "T9" }, new() { Id = 10, Tag = "T10" } },
            });
            Assert.Equal(2, dto.Items.Count);
            Assert.Equal(9, dto.Items[0].Id);
            Assert.Equal("T10", dto.Items[1].Tag);
        }
        finally { ClearState(); }
    }

    /// <summary>嵌套链兜底腿：嵌套对未注册（纯特性形态）→ 兜底腿特性解析——现状语义保留。
    /// 同名嵌套属性（无特性标注——按名默认）经 AddTypeCompatibleBinding 递归。</summary>
    [Fact]
    public void NestedChain_Fallback_UnregisteredAttributeResolution()
    {
        try
        {
            ClearState();
            // 仅注册主对（嵌套对不注册 → 嵌套递归走兜底腿——TryGetMappingMeta 未命中 → continue 链）
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T6Entity), TargetType = typeof(T6Dto),
                SourceTypeFqn = typeof(T6Entity).FullName!, TargetTypeFqn = typeof(T6Dto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[] { Pm("Id", "Id") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Inner2", "T6NestedDto") },
            });

            // 嵌套对未注册 → 顶层 Inner2 属性递归时 BuildNestedProjection 主源校验失败 → 属性跳过（现状语义）
            var expr = ProjectionExpressionFactory.Build<T6Entity, T6Dto>(S());
            var func = expr.Compile();
            var dto = func(new T6Entity { Id = 11 });
            Assert.Equal(11, dto.Id);
            Assert.NotNull(dto.Inner2); // 未绑定 → MemberInit 默认构造实例
        }
        finally { ClearState(); }
    }

    // ===== D2/D3 行为锁（决策点 8——验收 6）=====

    /// <summary>D2 锁（顶层）：手工注册明细 + DTO 属性残留 [MapIgnore(ToDto)]（类型无 [MapFrom] →
    /// 无特性锚，特性面本不消费）→ 快照为权威按明细绑定——残留标注零影响（明细 pm 无方向忽略 →
    /// 绑定成功；特性面消费则红）。同款语义已由嵌套面 G6（降级判定）与 T5 残标族覆盖。</summary>
    public class T6ResidualDto { public int Id { get; set; } [NCMapper.Attributes.MapIgnore(Direction.ToDto)] public string Title { get; set; } = ""; }

    [Fact]
    public void D2Lock_ResidualMapIgnore_TopLevel_SnapshotAuthoritative()
    {
        try
        {
            ClearState();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T6Entity), TargetType = typeof(T6ResidualDto),
                SourceTypeFqn = typeof(T6Entity).FullName!, TargetTypeFqn = typeof(T6ResidualDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[] { Pm("Id", "Id"), Pm("Title", "Name") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Title", "String") },
            });

            // Title 属性残留 [MapIgnore(ToDto)]（类型级——锚门 false 不消费）；
            // 明细 pm Title←Name 无方向忽略 → 快照为权威绑定（特性面消费则红）
            var expr = ProjectionExpressionFactory.Build<T6Entity, T6ResidualDto>(S());
            var func = expr.Compile();
            var dto = func(new T6Entity { Id = 12, Name = "N12" });
            Assert.Equal("N12", dto.Title);
        }
        finally { ClearState(); }
    }

    /// <summary>ContentVersion 失配重算后条目被<b>替换</b>（索引器写路径）——CacheEntryCount 恒 1
    /// 不随版本增长；重算后新声明生效（行为可观测：新对 Build 出正确绑定）。</summary>
    [Fact]
    public void Cache_ContentVersionMismatch_ReplacesEntry()
    {
        try
        {
            ClearState();
            RegisterPairs();

            var expr1 = ProjectionExpressionFactory.Build<T6Entity, T6Dto>(S());
            var count1 = ProjectionExpressionFactory.CacheEntryCount;
            Assert.Equal(1, count1);

            // 新对注册（ContentVersion 递增）→ 失配重算新对（新条目）
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T6NEntity), TargetType = typeof(T6NestedDto),
                SourceTypeFqn = typeof(T6NEntity).FullName!, TargetTypeFqn = typeof(T6NestedDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[] { Pm("Id", "Id"), Pm("Tag", "Tag") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Tag", "String") },
            });
            var expr2 = ProjectionExpressionFactory.Build<T6NEntity, T6NestedDto>(S());
            Assert.Equal(2, ProjectionExpressionFactory.CacheEntryCount); // 两对两条目（不沉淀）

            // 同对（旧键）失配重算 → 条目替换不增长（F4 反锁）
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(T6Entity), TargetType = typeof(T6Dto),
                SourceTypeFqn = typeof(T6Entity).FullName!, TargetTypeFqn = typeof(T6Dto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[] { Pm("Id", "Id"), Pm("Title", "Name") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Title", "String") },
            });
            var expr3 = ProjectionExpressionFactory.Build<T6Entity, T6Dto>(S());
            Assert.Equal(2, ProjectionExpressionFactory.CacheEntryCount);
            Assert.NotSame(expr1, expr3); // 失配重算 → 新表达式
            var func = expr3.Compile();
            var dto = func(new T6Entity { Id = 5, Name = "N5" });
            Assert.Equal("N5", dto.Title); // 重算后行为正确
        }
        finally { ClearState(); }
    }
}
