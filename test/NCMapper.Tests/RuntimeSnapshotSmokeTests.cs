// 承载类型投影冒烟测试（Q6 风险前置——Q6 / #5）
// 验证目标：手工注册 Fluent DTO 元数据到 RuntimeSnapshotStore 后，
// ProjectionExpressionFactory.Build 不再抛 MapFromNotApplied 且投影表达式可编译执行。
// 负对照：未注册且无 [MapFrom] 特性时仍抛 MapFromNotApplied（防恒真）。
using NCMapper.Internal;
using NCMapper.Runtime;
using System;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 快照运行时元数据承载类型冒烟测试（T1 阶段——发射端 T3 未接入，元数据为手工构造）
/// </summary>
public class RuntimeSnapshotSmokeTests
{
    /// <summary>冒烟用投影策略（FreeSql 形态）</summary>
    private static readonly ProjectionStrategy s_strategy = new(
        MaxNestingDepth: 3, WarningDepth: 2, EnableCollectionProjection: false, TracePrefix: "FreeSql");

    // ---- 冒烟模型（Fluent DTO 形态——无 [MapFrom] 特性）----

    private sealed class SmokeEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
    }

    private sealed class SmokeFluentDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// 正向冒烟：注册 Fluent DTO 元数据后 Build 不抛 MapFromNotApplied，表达式编译执行值正确
    /// </summary>
    [Fact]
    public void FluentDto_AfterManualRegistration_BuildCoreDoesNotThrow()
    {
        RuntimeSnapshotStore.Clear();
        try
        {
            // 手工注册（模拟 T3 发射产物 ModuleInitializer 注册行）
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(SmokeEntity),
                TargetType = typeof(SmokeFluentDto),
                SourceTypeFqn = typeof(SmokeEntity).FullName!,
                TargetTypeFqn = typeof(SmokeFluentDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                NoAutoInclude = false,
                IsFluentForward = true,
                AfterMappingMethodRef = null,
                Properties = Array.Empty<RuntimePropertyMapping>(),
                AllTargetSlots = new[]
                {
                    new RuntimeTargetPropertySlot { Name = "Id", TypeName = "int" },
                    new RuntimeTargetPropertySlot { Name = "Name", TypeName = "string" },
                    new RuntimeTargetPropertySlot { Name = "Amount", TypeName = "decimal" },
                },
            });

            // 门面语义断言：注册表命中（IsDto=true、IsRegistered=true、Fluent 无特性实例）
            Assert.True(RuntimeSnapshotStore.TryGetMappingMeta(typeof(SmokeFluentDto), out var view));
            Assert.True(view.IsDto);
            Assert.True(view.IsRegistered);
            Assert.Equal(typeof(SmokeEntity), view.PrimarySource);
            Assert.Null(view.MapFromAttribute); // Fluent DTO 无 [MapFrom] 特性实例
            Assert.NotNull(view.Metadata);

            // 投影内核：不再抛 MapFromNotApplied（Q6 冒烟核心断言）
            var expr = ProjectionExpressionFactory.Build<SmokeEntity, SmokeFluentDto>(s_strategy);
            Assert.NotNull(expr);

            // 编译执行——内核端到端构建验证（绑定值正确性）
            var compiled = expr.Compile();
            var source = new SmokeEntity { Id = 42, Name = "smoke", Amount = 9.5m };
            var dto = compiled(source);
            Assert.NotNull(dto);
            Assert.Equal(42, dto.Id);
            Assert.Equal("smoke", dto.Name);
            Assert.Equal(9.5m, dto.Amount);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            ProjectionExpressionFactory.ClearCache();
        }
    }

    /// <summary>
    /// 负对照：未注册且无 [MapFrom] 特性时 Build 仍抛 MapFromNotApplied（防测试恒真）
    /// </summary>
    [Fact]
    public void UnregisteredFluentDto_WithoutRegistration_BuildCoreStillThrows()
    {
        RuntimeSnapshotStore.Clear();
        try
        {
            Assert.False(RuntimeSnapshotStore.TryGetMappingMeta(typeof(SmokeFluentDto), out _));

            var ex = Assert.Throws<InvalidOperationException>(
                () => ProjectionExpressionFactory.Build<SmokeEntity, SmokeFluentDto>(s_strategy));
            Assert.Contains(ErrorMessages.MapFromNotApplied(nameof(SmokeFluentDto), "Projection"), ex.Message);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            ProjectionExpressionFactory.ClearCache();
        }
    }

    /// <summary>
    /// 反射回退语义：未注册但标注 [MapFrom] 的 DTO 经门面反射回退命中（IsRegistered=false）
    /// </summary>
    [Fact]
    public void MapFromDto_NotRegistered_FacadeFallsBackToReflection()
    {
        RuntimeSnapshotStore.Clear();
        try
        {
            Assert.False(RuntimeSnapshotStore.IsRegisteredDto(typeof(SmokeMapFromDto)));
            Assert.True(RuntimeSnapshotStore.TryGetMappingMeta(typeof(SmokeMapFromDto), out var view));
            Assert.True(view.IsDto);
            Assert.False(view.IsRegistered); // 反射回退
            Assert.Equal(typeof(SmokeEntity), view.PrimarySource);
            Assert.NotNull(view.MapFromAttribute); // 反射实例
            Assert.Null(view.Metadata);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
        }
    }

    // ---- 反射回退用模型（标注 [MapFrom]）----

    [NCMapper.Attributes.MapFrom(typeof(SmokeEntity))]
    private sealed class SmokeMapFromDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
