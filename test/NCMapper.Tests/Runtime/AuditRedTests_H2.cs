using System;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

/// <summary>
/// 审计缺陷 H-2 红测（探针转正——来源＝20261013-001 审计报告附录 B）。
/// <para>缺陷形态：三层注册链 Top→Mid→Leaf；Mid 层 slots 含 Leaf 但 Properties/ComposeMappings
/// 均无（矩阵 miss）→ 嵌套腿⑤递归入口（IsRegisteredDto(LeafDto) 命中）→
/// BuildProjectionValueExpression(tgtProp, srcType, (ParameterExpression)srcExpr, ...) 对成员表达式
/// 强转必抛 InvalidCastException（ProjectionExpressionFactory.cs:1204；
/// `94be4ad` 嵌套链快照优先复制顶层构建逻辑时引入）。</para>
/// <para>修复＝甲案（评审 001/002/003 P1 同抓、用户 2026-10-13 追认）：:529/:617 第三参放宽为
/// Expression（813/:856 同文件先例；:1194 主证）＋ :1204 取消强转直传——调用点全集
///（340/:555/:572/:365/:384/:1204）传参零适配。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class AuditRedTests_H2 : IDisposable
{
    public AuditRedTests_H2() { RuntimeSnapshotStore.Clear(); }
    public void Dispose() { RuntimeSnapshotStore.Clear(); }

    private class TopSrc { public int Id { get; set; } public MidSrc Mid { get; set; } = new(); }
    private class MidSrc { public string Other { get; set; } = ""; }
    private class LeafSrc { public string X { get; set; } = ""; }
    private class TopDto { public int Id { get; set; } public MidDto Mid { get; set; } = new(); }
    private class MidDto { public string Other { get; set; } = ""; public LeafDto Leaf { get; set; } = new(); }
    private class LeafDto { public string X { get; set; } = ""; }

    [Fact]
    public void H2_NestedMatrixMiss_RecursiveLeg_NoInvalidCast()
    {
        // 顶层（TopSrc,TopDto）：Properties 声明 Id/Mid；Mid 层：（MidSrc,MidDto）slots 含 Other+Leaf、
        // Properties 仅 Other；Leaf：（LeafSrc,LeafDto）注册（IsRegisteredDto(LeafDto) 命中 → ⑤腿递归入口）
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(TopSrc), TargetType = typeof(TopDto),
            SourceTypeFqn = typeof(TopSrc).FullName!, TargetTypeFqn = typeof(TopDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            Properties = new[]
            {
                new RuntimePropertyMapping { Target = new RuntimeTargetPropertySlot { Name = "Id", TypeName = "int" }, SourceName = "Id" },
                new RuntimePropertyMapping { Target = new RuntimeTargetPropertySlot { Name = "Mid", TypeName = typeof(MidDto).FullName }, SourceName = "Mid" },
            },
            AllTargetSlots = new[]
            {
                new RuntimeTargetPropertySlot { Name = "Id", TypeName = "int" },
                new RuntimeTargetPropertySlot { Name = "Mid", TypeName = typeof(MidDto).FullName },
            },
        });
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(MidSrc), TargetType = typeof(MidDto),
            SourceTypeFqn = typeof(MidSrc).FullName!, TargetTypeFqn = typeof(MidDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            Properties = new[] { new RuntimePropertyMapping { Target = new RuntimeTargetPropertySlot { Name = "Other", TypeName = "string" }, SourceName = "Other" } },
            AllTargetSlots = new[] { new RuntimeTargetPropertySlot { Name = "Other", TypeName = "string" },
                                     new RuntimeTargetPropertySlot { Name = "Leaf", TypeName = typeof(LeafDto).FullName } },
        });
        RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
        {
            SourceType = typeof(LeafSrc), TargetType = typeof(LeafDto),
            SourceTypeFqn = typeof(LeafSrc).FullName!, TargetTypeFqn = typeof(LeafDto).FullName!,
            Channel = RuntimeMappingChannel.Fluent, IsFluentForward = true,
            Properties = Array.Empty<RuntimePropertyMapping>(),
            AllTargetSlots = new[] { new RuntimeTargetPropertySlot { Name = "X", TypeName = "string" } },
        });

        var ex = Record.Exception(() =>
            NCMapper.Internal.ProjectionExpressionFactory.Build<TopSrc, TopDto>(new(
                MaxNestingDepth: 8, WarningDepth: 5, EnableCollectionProjection: true, TracePrefix: "[audit-probe]")));
        // 修复前实红：InvalidCastException（1204 对成员表达式强转）；修复后：构建正常返回
        Assert.True(ex == null, $"投影构建抛异常：{ex?.GetType().Name}: {ex?.Message}");
    }
}
