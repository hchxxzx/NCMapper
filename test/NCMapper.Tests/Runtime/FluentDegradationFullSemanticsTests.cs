// G1-① 降级兜底——内存映射全语义断言（「语义红利」）
//   本工程引用 NCMapper.SourceGen（用户工程标准形态）：Fluent 对生成映射器经 ModuleInitializer 注册
//   GeneratedCache，降级路径（AsDto → Mapper.Map）调用生成映射器——异名 Map/Mask/FromJson 全语义生效。
//   断言：① 降级判定 on/off 双注册表状态均命中（双 ORM 检测器）；② 生成映射器输出 =
//   异名不丢 + 「脱敏不落明文」安全断言 + FromJson 反序列化；③ [MapFrom] 通道回归零变化。
// 隔离范式（经验库 #60/#65/#366）：[Collection("GlobalConfigState")]（DisableParallelization）+
//   每用例自足注册（Clear + 幂等 Register + 检测器 ClearCache）+ try/finally 清理。
using System.Linq;
using NCMapper;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class FluentDegradationFullSemanticsTests
{
    // ================================================================
    // 夹具：异名 Map + Mask + FromJson 三类声明的 Fluent 对（生成器全链）
    // ================================================================

    public class FsEntity
    {
        public int Id { get; set; }
        public string RealName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Info { get; set; } = "{}";
    }

    public class FsPayload
    {
        public string K { get; set; } = "";
    }

    public class FsView
    {
        public int Id { get; set; }
        public string AliasName { get; set; } = "";
        public string Phone { get; set; } = "";
        public FsPayload Info { get; set; } = new();
    }

    [MapperConfig]
    internal class FsFluentMapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<FsEntity, FsView>()
               .Member(d => d.AliasName).From(s => s.RealName)   // 异名 From
               .Member(d => d.Phone).Mask<MaskPhone>()     // 脱敏
               .Member(d => d.Info).FromJson();                    // JSON 反序列化
        }
    }

    private static FsEntity NewEntity() => new()
    {
        Id = 1,
        RealName = "张三",
        Phone = "13812345678",
        Info = """{"K":"payload-v1"}""",
    };

    private static void ClearRegistryState()
    {
        RuntimeSnapshotStore.Clear();
        EfCoreDegradationDetector.ClearCache();
        FreeSqlDegradationDetector.ClearCache();
    }

    private static void RegisterIdentityOnly()
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(FsEntity),
            TargetType = typeof(FsView),
            SourceTypeFqn = typeof(FsEntity).FullName!,
            TargetTypeFqn = typeof(FsView).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
        });
    }

    // ================================================================
    // 断言内核：降级路径输出 = 全语义 + 脱敏不落明文（安全断言）
    // ================================================================

    private static void AssertFullSemantics(FsView dto)
    {
        Assert.Equal(1, dto.Id);
        // 异名 Map 不丢（反射引擎不消费 Fluent 异名声明——生成映射器才具备，证明确实走了生成路径）
        Assert.Equal("张三", dto.AliasName);
        // 脱敏不落明文（安全断言）：输出 ≠ 原值且已脱敏形态
        Assert.NotEqual("13812345678", dto.Phone);
        Assert.Contains("*", dto.Phone);
        Assert.DoesNotContain("1234", dto.Phone);
        // FromJson 不丢：JSON 反序列化生效（非 string 直拷/类型转换）
        Assert.NotNull(dto.Info);
        Assert.Equal("payload-v1", dto.Info!.K);
    }

    // ================================================================
    // ① 降级判定：on 态（身份+明细）与 off 态模拟（仅身份）均命中双 ORM 检测器
    // ================================================================

    [Fact]
    public void NeedsDegradation_FluentDto_FiresInBothRegistryStates()
    {
        try
        {
            // on 态形态：身份 + 明细（Register 幂等合并）
            ClearRegistryState();
            RegisterIdentityOnly();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(FsEntity),
                TargetType = typeof(FsView),
                SourceTypeFqn = typeof(FsEntity).FullName!,
                TargetTypeFqn = typeof(FsView).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[]
                {
                    new RuntimePropertyMapping
                    {
                        Target = new RuntimeTargetPropertySlot { Name = "Phone", TypeName = "String" },
                        SourceName = "Phone",
                        HasMask = true,
                        MaskTypeName = "NCMapper.MaskPhone",
                    },
                },
            });

            Assert.True(EfCoreDegradationDetector.NeedsDegradation(typeof(FsEntity), typeof(FsView), out var reasonOn));
            Assert.Contains("Fluent", reasonOn);
            Assert.True(FreeSqlDegradationDetector.NeedsDegradation(typeof(FsEntity), typeof(FsView), out _));

            // off 态形态：仅身份键（明细段零产物——独立输出节点形态）
            ClearRegistryState();
            RegisterIdentityOnly();
            EfCoreDegradationDetector.ClearCache();
            FreeSqlDegradationDetector.ClearCache();

            Assert.True(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(FsView)));
            Assert.True(EfCoreDegradationDetector.NeedsDegradation(typeof(FsEntity), typeof(FsView), out var reasonOff));
            Assert.Contains("Fluent", reasonOff);
        }
        finally
        {
            ClearRegistryState();
        }
    }

    // ================================================================
    // ② 降级路径输出全语义：Mapper.Map（AsDto 降级路径的落点）走生成映射器
    //    —— 异名不丢 + 脱敏不落明文（安全断言）+ FromJson 生效
    // ================================================================

    [Fact]
    public void DegradedInMemoryMapping_FullSemantics_SecurityMaskNotPlaintext()
    {
        try
        {
            // 注册表清理（对 Mapper.Map 无影响——GeneratedCache 由 ModuleInitializer 注册，独立容器）
            ClearRegistryState();

            var dto = Mapper.Map<FsEntity, FsView>(NewEntity());

            Assert.NotNull(dto);
            AssertFullSemantics(dto);
        }
        finally
        {
            ClearRegistryState();
        }
    }

    // ================================================================
    // ③ [MapFrom] 回归：Fluent 顶层判定对特性通道零误伤（投影路径行为不变）
    // ================================================================

    [Fact]
    public void MapFromChannel_NotAffectedByFluentTopLevelCheck()
    {
        try
        {
            ClearRegistryState();

            // 空注册表下 [MapFrom] DTO 经反射回退命中 → 非 Fluent 判定 → 不强制降级
            Assert.False(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(FsView)));
            Assert.True(RuntimeSnapshotStore.TryGetMappingMeta(typeof(ExistingAttrDto), out var view));
            Assert.True(view.IsDto);
            Assert.False(view.IsRegistered); // 反射回退命中（注册表空）——通道判定不依赖注册表
            Assert.Equal(typeof(ExistingAttrEntity), view.PrimarySource);
        }
        finally
        {
            ClearRegistryState();
        }
    }

    public class ExistingAttrEntity
    {
        public int Id { get; set; }
    }

    [NCMapper.Attributes.MapFrom(typeof(ExistingAttrEntity))]
    public class ExistingAttrDto
    {
        public int Id { get; set; }
    }
}
