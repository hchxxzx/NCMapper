// （Q8 PoC 转正——G4 探索计划定稿 → 判定行为锁）：
// Fluent Compose 可译性判定边界——形态空间 14 形态定稿（用户 2026-10-12 确认：F14 链式
// 放行 + 消费端 fail-closed——与特性对同口径）。PoC 阶段实证现态恒降级（"Compose" 短路，
// commit `13853ad` 15 用例）；本文件随短路改造（H3 保守臂 + 通用判定序复用）转为
// **目标态行为锁**——可译形态 Assert.Null（放行）/ 不可译形态按类别断言（reason 细化）/ H3 维持。
// 消费面支撑（实读结论）：ComposeProjectionKernel 快照腿承载 ComposeMappings 消费；
// 索引段与 pm 级同一重建单点（BuildSegmentsWithIndexes）；链式（cm 副源为 Compose 合成型、
// 主源无导航）消费端按 D-11 归一口 fail-closed 逐属性跳过（非数据丢失路径先例口径）。
// 自足注册范式（D-14 同源）：fixture 纯 POCO——手工 meta（Channel=Fluent + HasComposeMappings），
// [Collection("GlobalConfigState")] + try/finally 清理；不触发射产物（Tests 编译面零 SourceGen）。
using NCMapper.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class Q8FluentComposeTranslatabilityPoCTests
{
    // ===== fixture（纯 POCO——手工注册，自足）=====

    public class Q8Entity { public int Id { get; set; } public string Name { get; set; } = ""; public Q8Sub? Sub { get; set; } }
    public class Q8Sub { public int Id { get; set; } public string Level { get; set; } = ""; }
    public class Q8Dto { public int Id { get; set; } public string Level { get; set; } = ""; }

    private static RuntimeTargetPropertySlot Slot(string name, string typeName)
        => new() { Name = name, TypeName = typeName };

    private static RuntimePropertyMapping Pm(string target, string source)
        => new() { Target = Slot(target, "String"), SourceName = source, SourcePathNames = new[] { source } };

    /// <summary>cm 基形（副源纯字段）。</summary>
    private static RuntimeComposeMapping Cm(string target, params string[] path) => new()
    {
        TargetPropertyName = target,
        SubSourceTypeRef = typeof(Q8Sub),
        SubSourceTypeName = typeof(Q8Sub).FullName!,
        SourcePathNames = path,
    };

    /// <summary>Fluent Compose 对 meta 基形（Channel=Fluent + HasComposeMappings——发射产物同构形态）。</summary>
    private static RuntimeMapperMetadata Meta(
        RuntimeComposeMapping[]? compose = null,
        RuntimePropertyMapping[]? props = null,
        string? afterRef = null,
        string? beforeRef = null,
        bool subclass = false,
        bool composeFlag = true)
        => new()
        {
            SourceType = typeof(Q8Entity),
            TargetType = typeof(Q8Dto),
            SourceTypeFqn = typeof(Q8Entity).FullName!,
            TargetTypeFqn = typeof(Q8Dto).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            HasComposeMappings = composeFlag,
            Properties = props ?? new[] { Pm("Id", "Id") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
            AfterMappingMethodRef = afterRef,
            BeforeMappingMethodRef = beforeRef,
            HasSubclassDispatch = subclass,
            ComposeMappings = compose ?? new[] { Cm("Level", "Level") },
        };

    // ===== 组 A：目标可译形态（纯字段/点路径/索引段/扩容形态）=====

    /// <summary>F1 纯副源字段（单段路径）——目标态：可译放行（行为锁）。</summary>
    [Fact]
    public void F1_PlainField_Translatable_Null()
    {
        var verdict = DegradationDetectorBase.ClassifyUntranslatableDeclaration(Meta());
        Assert.Null(verdict); // 后：可译放行（现态恒 "Compose"——PoC `13853ad` 实证）
    }

    /// <summary>F2 副源点路径（多段）——目标态：可译放行（行为锁）。</summary>
    [Fact]
    public void F2_DotPath_Translatable_Null()
    {
        var verdict = DegradationDetectorBase.ClassifyUntranslatableDeclaration(
            Meta(compose: new[] { Cm("Level", "Profile", "Level") }));
        Assert.Null(verdict); // 路径重建单点支撑
    }

    /// <summary>F3 副源索引段（SourceIndexes 承载）——目标态：可译放行（行为锁）。</summary>
    [Fact]
    public void F3_IndexSegment_Translatable_Null()
    {
        var cm = new RuntimeComposeMapping
        {
            TargetPropertyName = "Level",
            SubSourceTypeRef = typeof(Q8Sub),
            SubSourceTypeName = typeof(Q8Sub).FullName!,
            SourcePathNames = new[] { "Items", "Level" },
            SourceIndexes = new[] { 0, -1 },
            HasIndex = true,
        };
        var verdict = DegradationDetectorBase.ClassifyUntranslatableDeclaration(Meta(compose: new[] { cm }));
        Assert.Null(verdict); // BuildSegmentsWithIndexes 同一重建单点
    }

    /// <summary>F13 扩容形态（评审 001-P1-2）：Properties 空 + ComposeMappings 有效非空——
    /// 目标态：可译（明细可达判据三处同步扩容——检测器/主链/内核）。TryGetFluentDetailMeta 为
    /// 退役门面（src 零消费方）——其 Properties 判据维持现态不扩容（登记）。</summary>
    [Fact]
    public void F13_ComposeOnlyNoProperties_Translatable_Null()
    {
        var meta = Meta(compose: new[] { Cm("Level", "Level") }, props: Array.Empty<RuntimePropertyMapping>());
        var verdict = DegradationDetectorBase.ClassifyUntranslatableDeclaration(meta);
        Assert.Null(verdict); // 后：可译（判据扩容——本任务决策点 7 三处）
        Assert.False(RuntimeSnapshotStore.TryGetFluentDetailMeta(typeof(Q8Dto), out _),
            "退役门面 TryGetFluentDetailMeta 维持 Properties 判据现态（零消费方——不扩容，登记）");
    }

    // ===== 组 B：不可译类别集（cm 五类 + 对级三类——reason 细化目标）=====

    /// <summary>F4-F8 cm 能力位五类——按类别降级（reason 细化——行为锁）。</summary>
    [Theory]
    [InlineData("Mask", "Mask")]
    [InlineData("Convert", "Convert")]
    [InlineData("Format", "Format")]
    [InlineData("FromJson", "FromJson")]
    [InlineData("When", "MapWhen")]
    public void F4toF8_ComposeItemCapability_CategoryRefined(string kind, string expectedCategory)
    {
        // 能力位形态——完整初始化器（init-only 属性）
        var cm = kind switch
        {
            "Mask" => new RuntimeComposeMapping
            {
                TargetPropertyName = "Level", SubSourceTypeRef = typeof(Q8Sub), SubSourceTypeName = typeof(Q8Sub).FullName!,
                SourcePathNames = new[] { "Level" }, HasMask = true, MaskTypeName = "Q8MaskImpl",
            },
            "Convert" => new RuntimeComposeMapping
            {
                TargetPropertyName = "Level", SubSourceTypeRef = typeof(Q8Sub), SubSourceTypeName = typeof(Q8Sub).FullName!,
                SourcePathNames = new[] { "Level" }, HasConverter = true, ConverterDeclaringType = "Generated", ConverterMethodName = "Map_Cv_Q8",
            },
            "Format" => new RuntimeComposeMapping
            {
                TargetPropertyName = "Level", SubSourceTypeRef = typeof(Q8Sub), SubSourceTypeName = typeof(Q8Sub).FullName!,
                SourcePathNames = new[] { "Level" }, FormatString = "yyyy-MM-dd",
            },
            "FromJson" => new RuntimeComposeMapping
            {
                TargetPropertyName = "Level", SubSourceTypeRef = typeof(Q8Sub), SubSourceTypeName = typeof(Q8Sub).FullName!,
                SourcePathNames = new[] { "Level" }, IsFromJson = true,
            },
            _ => new RuntimeComposeMapping
            {
                TargetPropertyName = "Level", SubSourceTypeRef = typeof(Q8Sub), SubSourceTypeName = typeof(Q8Sub).FullName!,
                SourcePathNames = new[] { "Level" }, ConditionMethodRef = "Generated::Map_When_Q8",
            },
        };
        var verdict = DegradationDetectorBase.ClassifyUntranslatableDeclaration(Meta(compose: new[] { cm }));
        Assert.Equal(expectedCategory, verdict); // 后：类别细化（明文外泄红线面——Mask/Convert/Format/FromJson/MapWhen 全拦）
    }

    /// <summary>F9-F11 对级三类（After/Before/SubclassDispatch）+ Compose——按对级类别降级（行为锁）。</summary>
    [Theory]
    [InlineData("After", "AfterMapping")]
    [InlineData("Before", "BeforeMapping")]
    [InlineData("Subclass", "SubclassDispatch")]
    public void F9toF11_PairLevelCapability_CategoryRefined(string kind, string expectedCategory)
    {
        var meta = kind switch
        {
            "After" => Meta(afterRef: "NCMapper.Tests.Runtime.Q8Hooks::After"),
            "Before" => Meta(beforeRef: "NCMapper.Tests.Runtime.Q8Hooks::Before"),
            _ => Meta(subclass: true),
        };
        var verdict = DegradationDetectorBase.ClassifyUntranslatableDeclaration(meta);
        Assert.Equal(expectedCategory, verdict); // 对级检查先于 ComposeMappings 循环（判定序）
    }

    // ===== 组 C：边界形态 =====

    /// <summary>F12 H3 边界：HasComposeMappings=true 而 ComposeMappings 空（F29 声明有产出无族）——
    /// 保守维持降级"Compose"（无可译依据——明文；H3 保守臂行为锁）。</summary>
    [Fact]
    public void F12_H3_DeclaredButEmpty_KeepsCompose()
    {
        var verdict = DegradationDetectorBase.ClassifyUntranslatableDeclaration(
            Meta(compose: Array.Empty<RuntimeComposeMapping>()));
        Assert.Equal("Compose", verdict); // H3 保守臂——改造前后语义不变
    }

    /// <summary>F14 链式边界：cm 副源为 Compose 合成型（主源无对应导航）——**用户 2026-10-12 裁定：
    /// 判定放行 + 消费端 D-11 归一口 fail-closed 逐属性跳过**（与特性对同口径——非数据丢失路径）。
    /// 行为锁：判定面 Assert.Null（放行）；消费面 fail-closed 由既有语义承载（登记）。</summary>
    [Fact]
    public void F14_ChainedSubSource_Translatable_Null()
    {
        var cm = new RuntimeComposeMapping
        {
            TargetPropertyName = "Level",
            SubSourceTypeRef = null, // 合成型副源——TypeRef 不可达（D-11 真实形态）
            SubSourceTypeName = "Q8.Composite.SubView",
            SourcePathNames = new[] { "Level" },
        };
        var verdict = DegradationDetectorBase.ClassifyUntranslatableDeclaration(Meta(compose: new[] { cm }));
        Assert.Null(verdict); // 放行（消费端 fail-closed 跳过——用户裁定口径）
    }

    /// <summary>交叉锚：特性通道跨表（MapFrom 对 + ComposeMappings）现态已可译（定稿）——
    /// 改造不得回退（R8 防线锚；回归见 D 组/A 组跨表用例）。</summary>
    [Fact]
    public void R8_Anchor_AttributeChannel_CrossTable_AlreadyTranslatable()
    {
        var attrMeta = new RuntimeMapperMetadata
        {
            SourceType = typeof(Q8Entity),
            TargetType = typeof(Q8Dto),
            SourceTypeFqn = typeof(Q8Entity).FullName!,
            TargetTypeFqn = typeof(Q8Dto).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            HasComposeMappings = false, // 特性通道恒 false（RuntimeMapperMetadata :68 载荷语义——D-15⑥）
            Properties = new[] { Pm("Id", "Id") },
            AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Level", "String") },
            ComposeMappings = new[] { Cm("Level", "Level") },
        };
        var verdict = DegradationDetectorBase.ClassifyUntranslatableDeclaration(attrMeta);
        Assert.Null(verdict); // 特性通道跨表现态即可译——行为变更作用域 = Fluent 通道
    }

    // ===== 组 D：定向冒烟（决策点 6——投影路径走通 + 检测器全链）=====

    private static ProjectionStrategy S() => new(
        MaxNestingDepth: 8, WarningDepth: 5, EnableCollectionProjection: true, TracePrefix: "[q8 smoke]");

    /// <summary>冒烟①：可译形态全链——NeedsDegradation=false（三态①扩容分支 + Classify 放行）+
    /// Build 投影走通（cm 消费：主源 Sub 导航 → Sub.Level 绑定）。</summary>
    [Fact]
    public void Smoke1_TranslatableCompose_DetectorPasses_BuildBinds()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            ProjectionExpressionFactory.ClearCache();
            RuntimeSnapshotStore.Register(Meta());

            Assert.False(NCMapper.EFCore.EfCoreDegradationDetector.NeedsDegradation(typeof(Q8Entity), typeof(Q8Dto), out _),
                "可译 Fluent Compose 对——检测器放行（三态①扩容 + Classify null）");

            var dto = ProjectionExpressionFactory.Build<Q8Entity, Q8Dto>(S()).Compile()(
                new Q8Entity { Id = 1, Name = "N1", Sub = new Q8Sub { Id = 7, Level = "Gold" } });
            Assert.Equal(1, dto.Id);
            Assert.Equal("Gold", dto.Level); // cm 消费走通（主源导航 → 副源字段）
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            ProjectionExpressionFactory.ClearCache();
        }
    }

    /// <summary>冒烟②：扩容形态（Properties 空 + Compose 有效）投影走通——主链三态② 收窄后
    /// composeByTarget 承载全部绑定；F13 可译验收 8 落地面。</summary>
    [Fact]
    public void Smoke2_ComposeOnlyNoProperties_BuildBinds()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            ProjectionExpressionFactory.ClearCache();
            RuntimeSnapshotStore.Register(Meta(compose: new[] { Cm("Level", "Level") }, props: Array.Empty<RuntimePropertyMapping>()));

            var dto = ProjectionExpressionFactory.Build<Q8Entity, Q8Dto>(S()).Compile()(
                new Q8Entity { Id = 2, Sub = new Q8Sub { Id = 8, Level = "Silver" } });
            Assert.Equal(0, dto.Id); // 无 pm——主源字段不绑定（Compose-only 声明契约）
            Assert.Equal("Silver", dto.Level); // composeByTarget 承载绑定（扩容判据生效）
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            ProjectionExpressionFactory.ClearCache();
        }
    }

    /// <summary>冒烟③：不可译形态全链——NeedsDegradation=true + Fluent reason 类别细化
    ///（"Mask"——明文外泄红线面全链验证）。</summary>
    [Fact]
    public void Smoke3_MaskCapability_FullChainDegrades_CategoryReason()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            ProjectionExpressionFactory.ClearCache();
            RuntimeSnapshotStore.Register(Meta(compose: new[]
            {
                new RuntimeComposeMapping
                {
                    TargetPropertyName = "Level", SubSourceTypeRef = typeof(Q8Sub), SubSourceTypeName = typeof(Q8Sub).FullName!,
                    SourcePathNames = new[] { "Level" }, HasMask = true, MaskTypeName = "Q8MaskImpl",
                },
            }));

            var needs = NCMapper.EFCore.EfCoreDegradationDetector.NeedsDegradation(typeof(Q8Entity), typeof(Q8Dto), out var reason);
            Assert.True(needs, "cm Mask——强制降级（明文外泄红线）");
            Assert.Contains("Mask", reason, StringComparison.Ordinal);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            ProjectionExpressionFactory.ClearCache();
        }
    }
}
