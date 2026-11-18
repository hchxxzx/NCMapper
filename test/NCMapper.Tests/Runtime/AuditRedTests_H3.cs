using System;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

/// <summary>
/// 审计缺陷 H-3 红测（探针转正——来源＝20261013-001 审计报告附录 B.2）。
/// <para>缺陷形态：Compose-only 对（Properties 空 + ComposeMappings 带 HasMask）的 cm 级脱敏
/// 声明在内存映射腿静默失效——RegistryMaskContext.Resolve 对 Properties 空的明细直接返回 null
///（ComposeMappings 零扫描），决策链（AssignmentPlanner）与渲染链（点路径/扁平化叶节点）的
/// registryCtx 全为 null → PII 明文输出（对照 GateD 同形态但掩码在 pm 级 → 生效）。</para>
/// <para>修复方向二选一（探索计划 PoC 后定案）：方案 A = Resolve 扩 ComposeMappings 扫描
///（单点，下游按目标属性名索引自然消费）；方案 B = 渲染/决策各站点按 cm 明细补位。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class AuditRedTests_H3 : IDisposable
{
    public AuditRedTests_H3() { RuntimeSnapshotStore.Clear(); Mapper.ClearCache(); }
    public void Dispose() { RuntimeSnapshotStore.Clear(); Mapper.ClearCache(); }

    public class CmMask : IMask { public string Mask(string? value) => (value ?? "") + "#"; }
    public class CmMaskB : IMask { public string Mask(string? value) => (value ?? "") + "@"; }

    private class CmEnt { public string Phone { get; set; } = ""; }
    private class CmDto { public string Phone { get; set; } = ""; }

    // pm/cm 并存防线用例独立类型对（同类型对委托缓存跨用例复用会架空判据——隔离发现）
    private class CmEnt2 { public string Phone { get; set; } = ""; }
    private class CmDto2 { public string Phone { get; set; } = ""; }

    // 脱敏类型显示名全名形态（MaskTypeNameResolver 按名解析；占位已按实际测试类全名替换）
    private const string MaskName = "NCMapper.Tests.Runtime.AuditRedTests_H3+CmMask";

    [Fact]
    public void H3_ComposeMask_InMemoryLeg_Consumed()
    {
        // cm 级掩码明细（Properties 空 + ComposeMappings 带 HasMask）——Compose-only 对形态
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(CmEnt), TargetType = typeof(CmDto),
            SourceTypeFqn = typeof(CmEnt).FullName!, TargetTypeFqn = typeof(CmDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            Properties = Array.Empty<RuntimePropertyMapping>(),
            ComposeMappings = new[]
            {
                new RuntimeComposeMapping
                {
                    TargetPropertyName = "Phone",
                    SubSourceTypeName = typeof(CmEnt).FullName,
                    SourcePathNames = new[] { "Phone" },
                    HasMask = true,
                    MaskTypeName = MaskName,
                },
            },
            AllTargetSlots = new[] { new RuntimeTargetPropertySlot { Name = "Phone", TypeName = "string" } },
        });

        var d = Mapper.Map<CmEnt, CmDto>(new CmEnt { Phone = "13800000000" });
        // 脱敏语义：值+"#"（修复前实红：Phone='13800000000'——PII 明文）
        Assert.True(d.Phone == "13800000000#", $"Phone='{d.Phone}'（期望 '13800000000#'）");
    }

    [Fact]
    public void H3_PmCmCoExisting_SingleMaskApplied()
    {
        // 防线用例（回归锁定，非红测）：同目标属性 pm/cm 并存时 pm 优先、cm 补位不覆盖——
        // 同一属性只施加一次掩码（特性腿优先 > pm > cm 的分层在 ctx 合并处保持）。
        // pm 行 → CmMask（+"#"）；cm 行 → CmMaskB（+"@"）：期望只出 pm 形态
        //（若 cm 覆盖 pm → "...@"；若双重施加 → "...#@"——两态均判红）。
        // 注意：使用独立类型对（CmEnt2/CmDto2）——同类型对的映射委托跨用例缓存复用会
        // 架空两用例判据（先跑者的委托被后跑者复用，隔离发现见任务书差异登记）。
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(CmEnt2), TargetType = typeof(CmDto2),
            SourceTypeFqn = typeof(CmEnt2).FullName!, TargetTypeFqn = typeof(CmDto2).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            Properties = new[]
            {
                new RuntimePropertyMapping
                {
                    Target = new RuntimeTargetPropertySlot { Name = "Phone", TypeName = "string" },
                    SourceName = "Phone", HasMask = true, MaskTypeName = MaskName,
                },
            },
            ComposeMappings = new[]
            {
                new RuntimeComposeMapping
                {
                    TargetPropertyName = "Phone",
                    SubSourceTypeName = typeof(CmEnt2).FullName,
                    SourcePathNames = new[] { "Phone" },
                    HasMask = true,
                    MaskTypeName = "NCMapper.Tests.Runtime.AuditRedTests_H3+CmMaskB",
                },
            },
            AllTargetSlots = new[] { new RuntimeTargetPropertySlot { Name = "Phone", TypeName = "string" } },
        });

        var d = Mapper.Map<CmEnt2, CmDto2>(new CmEnt2 { Phone = "13800000000" });
        Assert.True(d.Phone == "13800000000#",
            $"pm/cm 并存防线失守：Phone='{d.Phone}'（期望 pm 优先且单次施加 '13800000000#'）");
    }
}
