// （G1-②）：声明级降级分流 + 投影绑定明细消费测试——
//   判定面：纯可翻译（Map 异名/嵌套）不降级；不可翻译（Mask/Convert/FromJson/AfterMapping/Compose）强制降级；
//   off 态身份键维持强制降级（Q6 同族保守语义）；特性通道 IProjectionPolicy 腿回归。
//   投影面：注册表明细消费（异名 SourceName/嵌套 SourcePathNames 点路径/Ignore 差集重建与方向声明）。
//   隔离：GlobalConfigState 集合 + 自足注册（RuntimeSnapshotStore.Register）+ Clear + 检测器/收集器 ClearCache。
using System.Linq.Expressions;
using NCMapper.EFCore;
using NCMapper.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class FluentProjectionSplitTests
{
    // ===== fixture（手工注册，自足）=====

    public class FpEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class FpDto { public int Id { get; set; } public string Title { get; set; } = ""; }

    public class FmEntity { public int Id { get; set; } public string Phone { get; set; } = ""; }
    public class FmDto { public int Id { get; set; } public string Phone { get; set; } = ""; }

    public class FcEntity { public int Id { get; set; } public string Raw { get; set; } = ""; }
    public class FcDto { public int Id { get; set; } public string Raw { get; set; } = ""; }

    public class FjEntity { public int Id { get; set; } public string Payload { get; set; } = ""; }
    public class FjDto { public int Id { get; set; } public string Payload { get; set; } = ""; }

    public class FaEntity { public int Id { get; set; } }
    public class FaDto { public int Id { get; set; } }

    public class CmEntity { public int Id { get; set; } public string Ext { get; set; } = ""; }
    public class CmDto { public int Id { get; set; } public string Ext { get; set; } = ""; }

    public class OffEntity { public int Id { get; set; } }
    public class OffDto { public int Id { get; set; } }

    // 特性通道回归（IProjectionPolicy 腿；B2 修正：原 AttrMask fixture 带 pm.HasMask
    // 且无特性标注——切换后明细判定降级与"通道隔离"本义冲突，改为无能力位形态承载通道隔离语义，
    // pm.HasMask 明细承载判定转由 AttributeChannel_DetailCarry_Mask_Degrades 正向验证）
    public class AttrPlainEntity { public int Id { get; set; } public string Phone { get; set; } = ""; }
    public class AttrPlainDto { public int Id { get; set; } public string Phone { get; set; } = ""; }

    private static RuntimePropertyMapping Pm(string target, string? source = null,
        bool hasMask = false, bool hasConverter = false, bool isFromJson = false,
        bool hasMapIgnore = false, int ignoreDirection = 0, string? typeName = null)
        => new()
        {
            Target = new RuntimeTargetPropertySlot
            {
                Name = target,
                TypeName = typeName ?? "String",
                HasMapIgnore = hasMapIgnore,
                MapIgnoreDirection = ignoreDirection,
            },
            SourceName = source,
            HasMask = hasMask,
            HasConverter = hasConverter,
            IsFromJson = isFromJson,
        };

    private static void RegisterPairs()
    {
        // 纯可翻译（Map 异名：Title←Name）
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(FpEntity), TargetType = typeof(FpDto),
            SourceTypeFqn = typeof(FpEntity).FullName!, TargetTypeFqn = typeof(FpDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            Properties = new[] { Pm("Id", "Id"), Pm("Title", "Name") },
            AllTargetSlots = new[]
            {
                new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" },
                new RuntimeTargetPropertySlot { Name = "Title", TypeName = "String" },
            },
        });
        // 不可翻译五类
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(FmEntity), TargetType = typeof(FmDto),
            SourceTypeFqn = typeof(FmEntity).FullName!, TargetTypeFqn = typeof(FmDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            Properties = new[] { Pm("Id", "Id"), Pm("Phone", "Phone", hasMask: true) },
        });
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(FcEntity), TargetType = typeof(FcDto),
            SourceTypeFqn = typeof(FcEntity).FullName!, TargetTypeFqn = typeof(FcDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            Properties = new[] { Pm("Id", "Id"), Pm("Raw", "Raw", hasConverter: true) },
        });
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(FjEntity), TargetType = typeof(FjDto),
            SourceTypeFqn = typeof(FjEntity).FullName!, TargetTypeFqn = typeof(FjDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            Properties = new[] { Pm("Id", "Id"), Pm("Payload", "Payload", isFromJson: true) },
        });
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(FaEntity), TargetType = typeof(FaDto),
            SourceTypeFqn = typeof(FaEntity).FullName!, TargetTypeFqn = typeof(FaDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            AfterMappingMethodRef = "Some.Mapper.After",
            Properties = new[] { Pm("Id", "Id") },
        });
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(CmEntity), TargetType = typeof(CmDto),
            SourceTypeFqn = typeof(CmEntity).FullName!, TargetTypeFqn = typeof(CmDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            HasComposeMappings = true,
            Properties = new[] { Pm("Id", "Id") },
        });
        // off 态身份键（Properties 空）
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(OffEntity), TargetType = typeof(OffDto),
            SourceTypeFqn = typeof(OffEntity).FullName!, TargetTypeFqn = typeof(OffDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            Properties = Array.Empty<RuntimePropertyMapping>(),
        });
        // 特性通道回归腿（B2 修正：无能力位——通道隔离语义）
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(AttrPlainEntity), TargetType = typeof(AttrPlainDto),
            SourceTypeFqn = typeof(AttrPlainEntity).FullName!, TargetTypeFqn = typeof(AttrPlainDto).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            Properties = new[] { Pm("Id", "Id"), Pm("Phone", "Phone") },
        });
    }

    // ===== 判定面：声明级分流 =====

    [Fact]
    public void PureTranslatable_NotDegraded()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RegisterPairs();
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(FpEntity), typeof(FpDto), out var reason);
            Assert.False(needs, $"纯可翻译 Fluent DTO 不应降级（G1-② 声明级分流）——reason: {reason}");
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    [Theory]
    [InlineData(typeof(FmEntity), typeof(FmDto), "Mask")]
    [InlineData(typeof(FcEntity), typeof(FcDto), "Convert")]
    [InlineData(typeof(FjEntity), typeof(FjDto), "FromJson")]
    [InlineData(typeof(FaEntity), typeof(FaDto), "AfterMapping")]
    [InlineData(typeof(CmEntity), typeof(CmDto), "Compose")]
    public void UntranslatableDeclaration_ForcesDegrade(Type src, Type tgt, string expectedCategory)
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RegisterPairs();
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(src, tgt, out var reason);
            Assert.True(needs, $"{expectedCategory} 声明必须强制降级（F5 红线：投影无法表达）");
            Assert.Contains(expectedCategory, reason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    [Fact]
    public void OffState_IdentityOnly_ForcesDegrade()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RegisterPairs();
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(OffEntity), typeof(OffDto), out var reason);
            Assert.True(needs, "off 态身份键明细不可达 → 维持强制降级（保守语义不放宽）");
            Assert.Contains("off 态", reason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    [Fact]
    public void AttributeChannel_IProjectionPolicy_Leg_Unchanged()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RegisterPairs();
            EfCoreDegradationDetector.ClearCache();

            // 特性通道（MapFrom）条目明细命中（纯可翻译，无能力位）→ 不降级（B2 修正：
            // 原形态"MapFrom 不进 Fluent 分流"随统一门面三态化退役——现语义 = 明细命中按
            // Classify 判定，纯可翻译不降级；通道隔离 = MapFrom 不误用 off 态/Fluent 文本，
            // 由 OffState_IdentityOnly_ForcesDegrade（Fluent 通道）与本用例对照锁边）
            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(AttrPlainEntity), typeof(AttrPlainDto), out var reason);
            Assert.False(needs, $"MapFrom 通道纯可翻译明细不降级（通道隔离）——reason: {reason}");
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    [Fact]
    public void AttributeChannel_DetailCarry_Mask_Degrades()
    {
        // 对齐表 #1（MapFrom 通道明细承载）：pm.HasMask 明细命中 → Classify "Mask" 降级
        //（切换后特性对判定源 = 明细；fixture 形态对齐编译期产物——特性 [Mask] 提取进 pm.HasMask）
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(AttrPlainEntity), TargetType = typeof(AttrPlainDto),
                SourceTypeFqn = typeof(AttrPlainEntity).FullName!, TargetTypeFqn = typeof(AttrPlainDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[] { Pm("Id", "Id"), Pm("Phone", "Phone", hasMask: true) },
            });
            EfCoreDegradationDetector.ClearCache();

            var needs = EfCoreDegradationDetector.NeedsDegradation(typeof(AttrPlainEntity), typeof(AttrPlainDto), out var reason);
            Assert.True(needs, "MapFrom 通道明细 pm.HasMask 必须降级（对齐表 #1——明细承载判定）");
            Assert.Contains("Mask", reason);
            Assert.DoesNotContain("Fluent", reason);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
        }
    }

    // ===== 投影面：注册表明细消费 =====

    /// <summary>
    /// 构建投影表达式并提取 MemberInit 绑定（成员名 → 源属性名）字典
    /// </summary>
    private static Dictionary<string, string> BoundSourceMap<TSource, TTarget>()
        where TSource : class
        where TTarget : class
    {
        var strategy = new ProjectionStrategy(
            MaxNestingDepth: 8, WarningDepth: 5, EnableCollectionProjection: true, TracePrefix: "[test split]");
        var expr = ProjectionExpressionFactory.Build<TSource, TTarget>(strategy);
        var init = (MemberInitExpression)expr.Body;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var b in init.Bindings)
        {
            if (b is MemberAssignment ma && ma.Expression is MemberExpression me)
                map[b.Member.Name] = me.Member.Name;
        }
        return map;
    }

    [Fact]
    public void RegistryDetail_AliasName_ConsumedInProjection()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RegisterPairs();
            EfCoreDegradationDetector.ClearCache();
            ProjectionExpressionFactory.ClearCache(); // 表达式缓存键不含注册表状态——测试隔离须显式清

            var map = BoundSourceMap<FpEntity, FpDto>();
            Assert.True(map.ContainsKey("Title"), "异名声明（Title←Name）应进投影绑定");
            Assert.Equal("Name", map["Title"]);
            Assert.Equal("Id", map["Id"]);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
            ProjectionExpressionFactory.ClearCache();
        }
    }

    [Fact]
    public void FluentIgnoreBoth_SkippedInProjection()
    {
        // Ignore(Both)：属性不在 AllTargetSlots（零痕迹）→ slot 差集重建跳过
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(FpEntity), TargetType = typeof(FpDto),
                SourceTypeFqn = typeof(FpEntity).FullName!, TargetTypeFqn = typeof(FpDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
                Properties = new[] { Pm("Id", "Id") },
                AllTargetSlots = new[]
                {
                    new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" },
                    // Title 缺席 = Ignore(Both) 零痕迹
                },
            });
            EfCoreDegradationDetector.ClearCache();
            ProjectionExpressionFactory.ClearCache();

            var map = BoundSourceMap<FpEntity, FpDto>();
            Assert.False(map.ContainsKey("Title"), "Ignore(Both) 属性（slot 零痕迹）应跳过投影绑定");
            Assert.Equal("Id", map["Id"]);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
            ProjectionExpressionFactory.ClearCache();
        }
    }

    [Fact]
    public void FluentIgnoreToEntity_BoundInProjection()
    {
        // Ignore(ToEntity)：pm 级方向声明 Direction=2 → 投影（正向）照常绑定
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(FpEntity), TargetType = typeof(FpDto),
                SourceTypeFqn = typeof(FpEntity).FullName!, TargetTypeFqn = typeof(FpDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
                Properties = new[]
                {
                    Pm("Id", "Id"),
                    Pm("Title", "Name", hasMapIgnore: true, ignoreDirection: 2),
                },
                AllTargetSlots = new[]
                {
                    new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" },
                    new RuntimeTargetPropertySlot { Name = "Title", TypeName = "String" },
                },
            });
            EfCoreDegradationDetector.ClearCache();
            ProjectionExpressionFactory.ClearCache();

            var map = BoundSourceMap<FpEntity, FpDto>();
            Assert.Equal("Name", map["Title"]);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
            ProjectionExpressionFactory.ClearCache();
        }
    }

    [Fact]
    public void FluentIgnoreToDto_SkippedInProjection()
    {
        // Ignore(ToDto)：pm 级方向声明 Direction=1 → 投影（正向）跳过
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(FpEntity), TargetType = typeof(FpDto),
                SourceTypeFqn = typeof(FpEntity).FullName!, TargetTypeFqn = typeof(FpDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
                Properties = new[]
                {
                    Pm("Id", "Id"),
                    Pm("Title", "Name", hasMapIgnore: true, ignoreDirection: 1),
                },
                AllTargetSlots = new[]
                {
                    new RuntimeTargetPropertySlot { Name = "Id", TypeName = "Int32" },
                    new RuntimeTargetPropertySlot { Name = "Title", TypeName = "String" },
                },
            });
            EfCoreDegradationDetector.ClearCache();
            ProjectionExpressionFactory.ClearCache();

            var map = BoundSourceMap<FpEntity, FpDto>();
            Assert.False(map.ContainsKey("Title"));
            Assert.Equal("Id", map["Id"]);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            EfCoreDegradationDetector.ClearCache();
            ProjectionExpressionFactory.ClearCache();
        }
    }
}
