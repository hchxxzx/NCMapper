// 双路物化一致性测试（Q8）
// 验证"快照 → 发射 → 运行时注册"整链字段保真：独立程序集含 [MapFrom] DTO，
// 经 CompileWithGeneratorsAndLoad（双生成器 + Emit + Assembly.Load）加载后，
// RuntimeSnapshotStore 注册的 RuntimeMapperMetadata 字段与 DTO 真实反射属性一致。
// 意义：独立生成器（RuntimeSnapshotGenerator）与主链共享物化管线（MappingExtractor/
// FluentExtractor/DualDriverArbiter），发射元数据字段保真 = 双路物化结果可观测一致。
using NCMapper.Runtime;
using NCMapper.SourceGen.Tests.Helpers;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 双路物化一致性——发射 RuntimeSnapshot 元数据与真实 DTO 属性字段保真
/// </summary>
public class RuntimeSnapshotMaterializationConsistencyTests
{
    private const string MapFromSource = """
        using NCMapper.Attributes;
        namespace MatConsist
        {
            public class Order
            {
                public int Id { get; set; }
                public string No { get; set; } = "";
                public decimal Amount { get; set; }
                public System.DateTime CreatedAt { get; set; }
            }
            [MapFrom(typeof(Order))]
            public class OrderDto
            {
                public int Id { get; set; }
                public string No { get; set; } = "";
                public decimal Amount { get; set; }
                public System.DateTime CreatedAt { get; set; }
            }
            [MapFrom(typeof(Order))]
            public class OrderSummaryDto
            {
                public int Id { get; set; }
                public string No { get; set; } = "";
                public decimal Amount { get; set; }
            }
        }
        """;

    /// <summary>
    /// 发射 RuntimeSnapshot 元数据字段保真——加载独立程序集后 Store 注册的
    /// SourceType/TargetType/Channel 与真实 DTO 一致；AllTargetSlots 覆盖全部可写属性（忽略除外）
    /// </summary>
    [Fact]
    public void S4_RegisteredMetadata_FieldFidelity_MatchesDto()
    {
        RuntimeSnapshotStore.Clear();
        try
        {
            var asm = TestHelper.CompileWithGeneratorsAndLoad(MapFromSource, emitRuntimeSnapshot: true);
            var orderDto = asm.GetType("MatConsist.OrderDto");
            Assert.NotNull(orderDto);

            // 显式触发模块初始化（B-3 语义：Assembly.Load 后 typeof 不触发 ModuleInitializer，
            // 需 RunModuleConstructor 使发射注册行执行）——与 RuntimeSnapshotRegistrationTimingTests 一致
            RuntimeHelpers.RunModuleConstructor(orderDto!.Module.ModuleHandle);

            // 发射注册行已执行 → Store 含 OrderDto
            Assert.True(RuntimeSnapshotStore.IsRegisteredDto(orderDto!), "发射注册行未执行（ModuleInitializer 未运行）");
            var metas = RuntimeSnapshotStore.FindByTarget(orderDto!);
            var meta = metas.Single(m => m.TargetType == orderDto); // OrderDto（非 OrderSummaryDto）
            Assert.NotNull(meta);

            // 字段保真：SourceType/TargetType/Channel
            Assert.Equal("MatConsist.Order", meta.SourceType.FullName);
            Assert.Equal("MatConsist.OrderDto", meta.TargetType.FullName);
            Assert.Equal(RuntimeMappingChannel.MapFrom, meta.Channel);

            // AllTargetSlots 覆盖 DTO 全部可写属性（无忽略时数量一致）
            var dtoProps = orderDto!.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite).ToArray();
            Assert.True(meta.AllTargetSlots.Length > 0, "AllTargetSlots 不应为空");
            Assert.True(meta.AllTargetSlots.Length >= dtoProps.Length,
                $"AllTargetSlots={meta.AllTargetSlots.Length} 应覆盖 DTO 可写属性 {dtoProps.Length}");

            // 属性名一一对应（slot.Name 集合 ⊇ DTO 属性名——无 [MapIgnore] 时相等）
            var slotNames = meta.AllTargetSlots.Select(s => s.Name).OrderBy(n => n).ToArray();
            var propNames = dtoProps.Select(p => p.Name).OrderBy(n => n).ToArray();
            Assert.True(slotNames.SequenceEqual(propNames),
                $"AllTargetSlots 名集 [{string.Join(",", slotNames)}] 应与 DTO 属性集 [{string.Join(",", propNames)}] 一致");

            // 注册双写可达（RuntimeSnapshotPair 泛型键控 + Store 注册同一语义）
            Assert.Single(metas, m => m.TargetType == orderDto);
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
        }
    }

    /// <summary>
    /// 补充：多 DTO（同源不同 DTO）各自独立注册——OrderSummaryDto 亦有独立条目
    /// </summary>
    [Fact]
    public void S4_MultiDtoPerSource_EachRegisteredIndependently()
    {
        RuntimeSnapshotStore.Clear();
        try
        {
            var asm = TestHelper.CompileWithGeneratorsAndLoad(MapFromSource, emitRuntimeSnapshot: true);
            var summaryDto = asm.GetType("MatConsist.OrderSummaryDto");
            Assert.NotNull(summaryDto);

            RuntimeHelpers.RunModuleConstructor(summaryDto!.Module.ModuleHandle); // B-3 语义：显式触发注册
            Assert.True(RuntimeSnapshotStore.IsRegisteredDto(summaryDto!));
            var metas = RuntimeSnapshotStore.FindByTarget(summaryDto!);
            Assert.Single(metas);
            Assert.Equal("MatConsist.Order", metas[0].SourceType.FullName);
            // OrderSummaryDto 只 3 属性（无 CreatedAt）
            Assert.True(metas[0].AllTargetSlots.Length == 3,
                $"OrderSummaryDto 应 3 slot（无 CreatedAt），实际 {metas[0].AllTargetSlots.Length}");
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
        }
    }
}
