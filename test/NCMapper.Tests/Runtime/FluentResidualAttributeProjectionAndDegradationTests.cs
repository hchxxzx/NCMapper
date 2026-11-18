// c：G5（投影链 [MapIgnore] 准入）/ G6（降级判定六族门）锚语义锁定族。
// 行"投影链/降级判定（G5/G6）"+ -M8/M9。断言方向：
//     · Fluent 对（无 [MapFrom]）+ 残属性级标注 → 投影不剔除（G5）/ 不触发降级（G6）；
//     · 特性对（[MapFrom]）对照 → 照旧剔除 / 照旧降级（零变化）；
//     · 无锚对照 → 不消费不变（T5 既有口径）；
//  · MapWhen 对照 → 起随"快照为权威"收口：Fluent 对残标不降级（与族内标注一致）；
//       特性对/纯运行时降级由明细承载与 miss 扫描腿承接（DegradationDetectorThreeStateSwitchTests）。
//   隔离范式（经验 #60/#65/#366）：[Collection("GlobalConfigState")] + 每用例自足注册 + try/finally 清理。
using System;
using System.Linq;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

/// <summary>
/// G5/G6 锚语义锁定族（c 步）——投影链与降级判定与 WP-A 同谓词同口径。
/// </summary>
[Collection("GlobalConfigState")]
public class FluentResidualAttributeProjectionAndDegradationTests
{
    // ══════════════════════════ fixture ══════════════════════════

    public class G56Entity
    {
        public int Id { get; set; }
        public string RealName { get; set; } = "";
        public string Phone { get; set; } = "";
    }

    /// <summary>Fluent 对（**无 [MapFrom]**）：明细仅可翻译声明（异名 Map），属性上残留六族标注。</summary>
    public class G56FluentDto
    {
        public int Id { get; set; }
        public string AliasName { get; set; } = "";
        [Mask(typeof(G56Mask))]
        [MapIgnore]
        [MapFormat("N")]
        [MapProperty(typeof(G56Entity), nameof(G56Entity.Phone))]
        public string Phone { get; set; } = "";
    }

    /// <summary>特性对对照（有 [MapFrom]）：同形态标注照旧参与投影/降级判定。</summary>
    [MapFrom(typeof(G56Entity))]
    public class G56AttrDto
    {
        public int Id { get; set; }
        public string AliasName { get; set; } = "";
        [Mask(typeof(G56Mask))]
        [MapIgnore]
        public string Phone { get; set; } = "";
    }

    /// <summary>MapWhen 对照：Fluent 对 + [MapWhen] → 必须降级口径保持（G6 族集不含 MapWhen）。</summary>
    public class G56WhenDto
    {
        public int Id { get; set; }
        public string AliasName { get; set; } = "";
        [MapWhen(nameof(Ok))]
        public string Phone { get; set; } = "";

        public static bool Ok(string value) => value.Length > 0;
    }

    /// <summary>嵌套对照：外层 Fluent 对，嵌套 DTO 带残标 → 嵌套扫描位（374）同门。</summary>
    public class G56OuterDto
    {
        public int Id { get; set; }
        public G56NestedDto? Nested { get; set; }
    }

    public class G56NestedDto
    {
        public int Id { get; set; }
        [Mask(typeof(G56Mask))]
        public string Phone { get; set; } = "";
    }

    public class G56NestedEntity { public int Id { get; set; } public string Phone { get; set; } = ""; }
    public class G56OuterEntity { public int Id { get; set; } public G56NestedEntity? Nested { get; set; } }

    public class G56Mask : IMask { public string Mask(string? value) => (value ?? "") + "*"; }

    // ══════════════════════════ 注册辅助 ══════════════════════════

    private static void ClearState()
    {
        RuntimeSnapshotStore.Clear();
        EfCoreDegradationDetector.ClearCache();
        FreeSqlDegradationDetector.ClearCache();
        NavigationTreeCollector.ClearCache();
    }

    /// <summary>可翻译明细（异名 Map）满载注册——使 Fluent 对落入"纯可翻译声明 → 不降级"分支，
    /// 从而让六族残标是否参与降级判定成为唯一判别位。</summary>
    private static void RegisterTranslatableFluentPair(Type src, Type tgt)
    {
        var detail = new[]
        {
            new RuntimePropertyMapping
            {
                Target = new RuntimeTargetPropertySlot { Name = nameof(G56FluentDto.AliasName), TypeName = "String" },
                SourceName = nameof(G56Entity.RealName),
                SourcePathNames = new[] { nameof(G56Entity.RealName) },
            },
            new RuntimePropertyMapping
            {
                Target = new RuntimeTargetPropertySlot { Name = nameof(G56FluentDto.Phone), TypeName = "String" },
                SourceName = nameof(G56Entity.Phone),
                SourcePathNames = new[] { nameof(G56Entity.Phone) },
            },
        };
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = src,
            TargetType = tgt,
            SourceTypeFqn = src.FullName!,
            TargetTypeFqn = tgt.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            Properties = detail,
            AllTargetSlots = detail.Select(p => p.Target).ToArray(),
        });
    }

    // ══════════════════════════ G5：投影链 [MapIgnore] 准入 ══════════════════════════

    /// <summary>G5·Fluent 对：残 [MapIgnore] 不参与投影剔除判定（投影按明细保留该列）。
    /// <para>须先注册 Fluent 对（双锚真 / 特性锚假态）——否则与无锚态不可区分，反向验证失效
    ///（变异实测：未注册时谓词变异不红，2026-10-10）。</para></summary>
    [Fact]
    public void G5_FluentPair_ResidualMapIgnore_DoesNotDropProjectionColumn()
    {
        try
        {
            ClearState();
            RegisterTranslatableFluentPair(typeof(G56Entity), typeof(G56FluentDto));
            Assert.True(RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(G56FluentDto)));

            var prop = typeof(G56FluentDto).GetProperty(nameof(G56FluentDto.Phone))!;
            Assert.False(ProjectionExpressionFactory.IsMapIgnoreDefined(prop));
        }
        finally { ClearState(); }
    }

    /// <summary>G5·特性对对照：[MapIgnore] 照旧参与投影剔除（零变化）。</summary>
    [Fact]
    public void G5_AttributeAnchoredPair_MapIgnore_StillDropsProjectionColumn()
    {
        var prop = typeof(G56AttrDto).GetProperty(nameof(G56AttrDto.Phone))!;
        Assert.True(ProjectionExpressionFactory.IsMapIgnoreDefined(prop));
    }

    /// <summary>G5·无锚对照：不消费不变（T5 既有口径）。</summary>
    [Fact]
    public void G5_Anchorless_MapIgnore_NotConsumed()
    {
        var prop = typeof(G56WhenDto).GetProperty(nameof(G56WhenDto.Phone))!;
        Assert.False(ProjectionExpressionFactory.IsMapIgnoreDefined(prop));
    }

    // ══════════════════════════ G6：降级判定六族门 ══════════════════════════

    /// <summary>G6·Fluent 对 + 六族残标（Mask/MapIgnore/MapFormat/MapProperty 同属性齐标）：
    /// **不触发降级**（可翻译明细在位，残标不参与判定）。</summary>
    [Fact]
    public void G6_FluentPair_ResidualAttributeFamily_DoesNotDegrade()
    {
        try
        {
            ClearState();
            RegisterTranslatableFluentPair(typeof(G56Entity), typeof(G56FluentDto));

            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(G56Entity), typeof(G56FluentDto), out var reason));
            Assert.DoesNotContain("MaskAttribute", reason);
            Assert.DoesNotContain("MapIgnoreAttribute", reason);
            Assert.False(FreeSqlDegradationDetector.NeedsDegradation(typeof(G56Entity), typeof(G56FluentDto), out _));
        }
        finally { ClearState(); }
    }

    /// <summary>G6·特性对对照（[MapFrom] + [Mask]）：照旧强制降级（零变化——反向验证锚）。</summary>
    [Fact]
    public void G6_AttributeAnchoredPair_Mask_StillDegrades()
    {
        try
        {
            ClearState();

            Assert.True(EfCoreDegradationDetector.NeedsDegradation(typeof(G56Entity), typeof(G56AttrDto), out var reason));
            Assert.Contains("MaskAttribute", reason);
        }
        finally { ClearState(); }
    }

    /// <summary>G6·MapWhen 对照（B2 修正——口径收口）：Fluent 对 + 残 [MapWhen] → **不降级**
    ///（快照为权威——「快照腿明细命中时扫描腿不跑」+ -D2「明细命中后属性级判定
    /// 只来自明细」；20261010-001 例外"族集不含 MapWhen"以扫描腿为存在前提，切换后失据——
    /// 与族内标注（Mask 等 :170 对照用例）口径一致化收口。特性对 [MapWhen] 明细承载降级
    ///（pm.ConditionMethodRef → Classify "MapWhen"）与纯运行时 miss 扫描腿降级由
    /// DegradationDetectorThreeStateSwitchTests 对齐表用例承接）。</summary>
    [Fact]
    public void G6_FluentPair_ResidualMapWhen_DoesNotDegrade_SnapshotAuthoritative()
    {
        try
        {
            ClearState();
            RegisterTranslatableFluentPair(typeof(G56Entity), typeof(G56WhenDto));

            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(G56Entity), typeof(G56WhenDto), out var reason),
                $"Fluent 纯可翻译明细命中后残标 [MapWhen] 不参与判定（快照为权威）——reason: {reason}");
            Assert.DoesNotContain("MapWhen", reason);
        }
        finally { ClearState(); }
    }

    /// <summary>G6·无锚对照（不注册 + [Mask]）：不消费不变（既有语义——不降级）。</summary>
    [Fact]
    public void G6_Anchorless_Mask_NotConsumed_NoDegrade()
    {
        try
        {
            ClearState();

            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(G56Entity), typeof(G56FluentDto), out _));
        }
        finally { ClearState(); }
    }

    /// <summary>G6·嵌套扫描位（374）：外层 Fluent 对 + 嵌套 DTO 残 [Mask] → 不因嵌套残标降级。</summary>
    [Fact]
    public void G6_NestedScan_ResidualMask_DoesNotDegrade()
    {
        try
        {
            ClearState();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(G56OuterEntity),
                TargetType = typeof(G56OuterDto),
                SourceTypeFqn = typeof(G56OuterEntity).FullName!,
                TargetTypeFqn = typeof(G56OuterDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[]
                {
                    new RuntimePropertyMapping
                    {
                        Target = new RuntimeTargetPropertySlot { Name = nameof(G56OuterDto.Nested), TypeName = "G56NestedDto" },
                        SourceName = nameof(G56OuterEntity.Nested),
                        SourcePathNames = new[] { nameof(G56OuterEntity.Nested) },
                    },
                },
                AllTargetSlots = new[]
                {
                    new RuntimeTargetPropertySlot { Name = nameof(G56OuterDto.Nested), TypeName = "G56NestedDto" },
                },
            });

            Assert.False(EfCoreDegradationDetector.NeedsDegradation(typeof(G56OuterEntity), typeof(G56OuterDto), out var reason));
            Assert.DoesNotContain("MaskAttribute", reason);
        }
        finally { ClearState(); }
    }
}
