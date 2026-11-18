// MapTracer 锚门补全定向测试（488 诊断面分层条款）
//  用例矩阵：①③⑥证红（B11——T1 实施前于开工基线实跑留痕）→ T1 实施后复绿；
//             ②④⑤行为锁定防回退（有锚语义恒绿）。
//   隔离：[Collection("GlobalConfigState")]（DisableParallelization，与既有冻结/注册表消费者串行）；
//         用例⑤注册表腿自足注册 + try/finally Clear（先例：Runtime/FluentProjectionSplitTests.cs 隔离三件套）。
//   fixture 承载形态说明：用例①③⑥为"无锚类带属性级配置标注"、用例⑤为"注册表锚（运行时概念）类带
//         属性级配置标注"——均命中生成端 NCMAP019（Error，符号级十项清单）的触发面。019 已由仓库根
//  .globalconfig 全局豁免（severity=none），本文件 `#pragma warning
//         disable NCMAP019` 为防御性冗余（防豁免面调整后本文件爆 019 的自保形态）。两类 fixture 均为
//         本测试的预期语义场景（"配置无激活路径"的报告形态 + 生成端静态判定盲区），编译零错误，
//         不属经验库 #117"容忍编译错"形态；抑制范围仅限本文件。
#pragma warning disable NCMAP019
using NCMapper.Attributes;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Tests;

[Collection("GlobalConfigState")]
public class MapTracerAnchorGateReportTests
{
    // ===== fixture（嵌套自足，不与他测共享——防注册表副作用外溢）=====

    /// 用例①③⑥共用源类型（有同名属性 Name；无 Title）
    public class GateEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    /// 用例③④⑥的异名源类型（≠ TSource）
    public class GateOtherEntity
    {
        public int Id { get; set; }
        public string F { get; set; } = "";
    }

    /// ① 无锚（无 [MapFrom]、无注册）+ [MapIgnore]
    public class NoAnchorIgnoreDto
    {
        public int Id { get; set; }
        [MapIgnore]
        public string Name { get; set; } = "";
    }

    /// ② [MapFrom] 锚 + [MapIgnore]（恒绿）
    [MapFrom(typeof(GateEntity))]
    public class AnchoredIgnoreDto
    {
        public int Id { get; set; }
        [MapIgnore]
        public string Name { get; set; } = "";
    }

    /// ③ 无锚 + [MapProperty] 异名，目标属性名在源侧无同名（基线 = not found 确定形态——J1 pin）
    public class NoAnchorMapPropertyNoSourceDto
    {
        public int Id { get; set; }
        [MapProperty(typeof(GateOtherEntity), "F")]
        public string Title { get; set; } = "";
    }

    /// ④ [MapFrom] 锚 + [MapProperty] FromSource ≠ TSource（恒绿：skipped (source type——J2 pin 无对冲）
    [MapFrom(typeof(GateEntity))]
    public class AnchoredMapPropertyDto
    {
        public int Id { get; set; }
        [MapProperty(typeof(GateOtherEntity), "F")]
        public string Title { get; set; } = "";
    }

    /// ⑤ Fluent 注册表锚（运行时手工注册，无 [MapFrom]）+ [MapIgnore]（恒绿：双锚谓词注册表腿）
    public class RegistryAnchorIgnoreDto
    {
        public int Id { get; set; }
        [MapIgnore]
        public string Name { get; set; } = "";
    }

    /// ⑥ 无锚 + [MapProperty] FromField 与目标属性同名、源侧存在同名属性（基线 = 按名映射行——J3 信息面收窄锁定）
    public class NoAnchorMapPropertySameNameDto
    {
        public int Id { get; set; }
        [MapProperty(typeof(GateOtherEntity), "Name")]
        public string Name { get; set; } = "";
    }

    /// ⑦ Fluent 注册表锚（无 [MapFrom]）+ [MapProperty]——三态第二族
    public class RegistryAnchorMapPropertyDto
    {
        public int Id { get; set; }
        [MapProperty(typeof(GateOtherEntity), "F")]
        public string Title { get; set; } = "";
    }

    /// ⑧ Fluent 注册表锚 + [Mask]（Q10 加门位）：脱敏判定不再以残标为据
    public class RegistryAnchorMaskDto
    {
        public int Id { get; set; }
        [Mask(typeof(TracerGateMask))]
        public string Name { get; set; } = "";
    }

    /// ⑧ 对照：特性锚 + [Mask]（照旧脱敏——零变化）
    [MapFrom(typeof(GateEntity))]
    public class AnchoredMaskDto
    {
        public int Id { get; set; }
        [Mask(typeof(TracerGateMask))]
        public string Name { get; set; } = "";
    }

    public class TracerGateMask : NCMapper.IMask
    {
        public string Mask(string? value) => (value ?? "") + "#masked";
    }

    /// ⑨ MapValidator 统计通道三态：[MapIgnore] 且源侧无同名（特性对 = 剔除出统计 / Fluent 对与无锚 = 入统计）
    [MapFrom(typeof(GateEntity))]
    public class AnchoredIgnoreNoSourceDto
    {
        public int Id { get; set; }
        [MapIgnore]
        public string Title { get; set; } = "";
    }

    /// ⑨ Fluent 注册表锚形态（同 fixture 形状，运行时注册）
    public class RegistryAnchorIgnoreNoSourceDto
    {
        public int Id { get; set; }
        [MapIgnore]
        public string Title { get; set; } = "";
    }

    /// ⑨ 无锚形态（不注册）
    public class NoAnchorIgnoreNoSourceDto
    {
        public int Id { get; set; }
        [MapIgnore]
        public string Title { get; set; } = "";
    }

    // ===== 用例 =====

    /// ① 无锚 [MapIgnore] → ⚠️ invalid config（B11 证红：基线输出 skipped (MapIgnore)）
    [Fact]
    public void Anchorless_MapIgnore_ReportsInvalidConfig()
    {
        var report = MapTracer.Trace<GateEntity, NoAnchorIgnoreDto>(new GateEntity { Id = 1, Name = "n" });

        Assert.Contains("Name: ⚠️ invalid config ([MapIgnore] inert without mapping anchor)", report);
    }

    /// ② 有锚 [MapIgnore] → 照常 skipped（行为锁定防回退）
    [Fact]
    public void Anchored_MapIgnore_KeepsSkippedLine()
    {
        var report = MapTracer.Trace<GateEntity, AnchoredIgnoreDto>(new GateEntity { Id = 1, Name = "n" });

        Assert.Contains("Name: skipped (MapIgnore)", report);
    }

    /// ③ 无锚 [MapProperty] 异名（目标属性名源侧无同名）→ invalid config（B11 证红：基线 not found）
    [Fact]
    public void Anchorless_MapProperty_ReportsInvalidConfig()
    {
        var report = MapTracer.Trace<GateEntity, NoAnchorMapPropertyNoSourceDto>(new GateEntity { Id = 1, Name = "n" });

        Assert.Contains("Title: ⚠️ invalid config ([MapProperty] inert without mapping anchor)", report);
    }

    /// ④ 有锚 [MapProperty] FromSource ≠ TSource → skipped (source type（J2 pin）
    [Fact]
    public void Anchored_MapProperty_SourceTypeMismatch_KeepsSkippedLine()
    {
        var report = MapTracer.Trace<GateEntity, AnchoredMapPropertyDto>(new GateEntity { Id = 1, Name = "n" });

        Assert.Contains("Title: skipped (source type", report);
    }

    /// ⑤ Fluent 注册表锚（无 [MapFrom]）→ **not consumed** 三态新文案。
    /// <para> 筛查翻正（登记；断言翻正 + 用例意图保留——经验 #96）：
    /// 原断言锁定"双锚（注册表腿）→ skipped (MapIgnore)"，即 007 实证的"Fluent 对消费属性级标注"
    /// 缺陷行为；新口径三态 = 特性锚 → skipped / Fluent 对 → not consumed / 无锚 → invalid config
    ///（Q5 终稿，用户 2026-10-10 裁定）。用例意图不变：注册表锚形态的报告分支仍被锁定。</para>
    [Fact]
    public void RegistryAnchored_MapIgnore_KeepsNotConsumedLine()
    {
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(GateEntity),
            TargetType = typeof(RegistryAnchorIgnoreDto),
            SourceTypeFqn = typeof(GateEntity).FullName!,
            TargetTypeFqn = typeof(RegistryAnchorIgnoreDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            Properties = Array.Empty<RuntimePropertyMapping>(),
        });
        try
        {
            var report = MapTracer.Trace<GateEntity, RegistryAnchorIgnoreDto>(new GateEntity { Id = 1, Name = "n" });

            Assert.Contains("Name: ⚠️ not consumed (Fluent pair — attribute ignored; use Fluent declarations)", report);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
        }
    }

    /// ⑥ 无锚 [MapProperty] 同名（源侧存在同名属性）→ invalid config（B11 证红：基线按名映射行——信息面收窄锁定）
    [Fact]
    public void Anchorless_MapProperty_SameName_ReportsInvalidConfig()
    {
        var report = MapTracer.Trace<GateEntity, NoAnchorMapPropertySameNameDto>(new GateEntity { Id = 1, Name = "n" });

        Assert.Contains("Name: ⚠️ invalid config ([MapProperty] inert without mapping anchor)", report);
    }

    // ===== c：三态矩阵补全（Fluent 对 × MapProperty / Mask + MapValidator 统计面）=====

    /// <summary>⑦ Fluent 对 + [MapProperty] → not consumed 三态文案（与 MapIgnore 族同口径）。</summary>
    [Fact]
    public void RegistryAnchored_MapProperty_ReportsNotConsumedLine()
    {
        RegisterFluentPair(typeof(GateEntity), typeof(RegistryAnchorMapPropertyDto));
        try
        {
            var report = MapTracer.Trace<GateEntity, RegistryAnchorMapPropertyDto>(new GateEntity { Id = 1, Name = "n" });

            Assert.Contains("Title: ⚠️ not consumed (Fluent pair — attribute ignored; use Fluent declarations)", report);
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    /// <summary>⑧ Fluent 对 + [Mask]（Q10 加门位）：ShowValues 下值**不脱敏**（残标不构成脱敏依据）。</summary>
    [Fact]
    public void RegistryAnchored_Mask_Q10Gate_ValueNotMasked()
    {
        RegisterFluentPair(typeof(GateEntity), typeof(RegistryAnchorMaskDto));
        try
        {
            var report = MapTracer.Trace<GateEntity, RegistryAnchorMaskDto>(
                new GateEntity { Id = 1, Name = "plain" },
                new MapTraceOptions { Enabled = true, ShowValues = true });

            Assert.Contains("Name=\"plain\"", report);
            Assert.DoesNotContain("[MASKED]", report);
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    /// <summary>⑧ 对照：特性锚 + [Mask] → 照旧 [MASKED]（零变化）。</summary>
    [Fact]
    public void AttributeAnchored_Mask_StillMasked()
    {
        var report = MapTracer.Trace<GateEntity, AnchoredMaskDto>(
            new GateEntity { Id = 1, Name = "plain" },
            new MapTraceOptions { Enabled = true, ShowValues = true });

        Assert.Contains("[MASKED]", report);
    }

    /// <summary>⑨ MapValidator 统计通道三态·特性对：[MapIgnore] 属性剔除出统计（既有语义零变化）。</summary>
    [Fact]
    public void Validator_AttributeAnchored_MapIgnore_ExcludedFromStatistics()
    {
        var result = MapValidator.ValidateMapping(typeof(GateEntity), typeof(AnchoredIgnoreNoSourceDto));

        Assert.Equal(1, result.TotalCount);
        Assert.DoesNotContain("Title", result.UnmappedProperties);
    }

    /// <summary>⑨ MapValidator 统计通道三态·Fluent 对：[MapIgnore] 不消费 → 属性入统计通道（计入未映射）。</summary>
    [Fact]
    public void Validator_FluentPair_MapIgnore_EntersStatisticsChannel()
    {
        RegisterFluentPair(typeof(GateEntity), typeof(RegistryAnchorIgnoreNoSourceDto));
        try
        {
            var result = MapValidator.ValidateMapping(typeof(GateEntity), typeof(RegistryAnchorIgnoreNoSourceDto));

            Assert.Equal(2, result.TotalCount);
            Assert.Contains(result.UnmappedProperties, p => p.StartsWith("Title", StringComparison.Ordinal));
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    /// <summary>⑨ MapValidator 统计通道三态·无锚：与 Fluent 对同落统计通道（T5 既有口径零变化）。</summary>
    [Fact]
    public void Validator_Anchorless_MapIgnore_EntersStatisticsChannel()
    {
        var result = MapValidator.ValidateMapping(typeof(GateEntity), typeof(NoAnchorIgnoreNoSourceDto));

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.UnmappedProperties, p => p.StartsWith("Title", StringComparison.Ordinal));
    }

    private static void RegisterFluentPair(Type sourceType, Type targetType)
        => RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = sourceType,
            TargetType = targetType,
            SourceTypeFqn = sourceType.FullName!,
            TargetTypeFqn = targetType.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            Properties = Array.Empty<RuntimePropertyMapping>(),
        });
}
