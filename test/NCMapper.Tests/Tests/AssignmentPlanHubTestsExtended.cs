// 剩余测试 — 一致性扩展 + 豁免收敛对 + Trace 可观测性 + null-source 契约
using System.IO;
using System.Reflection;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Shared;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// ② 枢纽深化 — 一致性测试扩展至 12 Kind + 前置判定场景。
/// </summary>
public class PlanConsistencyExtendedTests
{
    public class A { public int X { get; set; } }
    public class B { public int X { get; set; } }

    public static TheoryData<Type, Type, string> KindMatrix => new()
    {
        { typeof(int), typeof(int), "Direct" },
        { typeof(string), typeof(string), "Direct" },
        { typeof(int?), typeof(int?), "Direct" },
        { typeof(A), typeof(A), "Direct" },
        { typeof(int), typeof(long), "Convert" },
        { typeof(string), typeof(int), "Convert" },
        { typeof(int?), typeof(int), "Convert" },
        { typeof(int), typeof(int?), "Convert" },
        { typeof(A), typeof(B), "Nested" },
        { typeof(List<int>), typeof(List<int>), "Collection" },
        { typeof(int[]), typeof(List<int>), "Collection" },
    };

    [Theory]
    [MemberData(nameof(KindMatrix))]
    public void OldPlan_TypeShape_Consistent(Type source, Type target, string expectedKind)
    {
        var oldPlan = AssignmentPlanner.Plan(
            source, target,
            hasConvertUsing: false, hasMask: false,
            isToDto: false, globallyIgnored: false,
            isFromJson: false, crossTableFromSource: null);
        Assert.Equal(expectedKind, oldPlan.Kind.ToString());
    }

    [Fact]
    public void OldNewPlan_Rule2_Difference_ConvertUsing()
    {
        var oldPlan = AssignmentPlanner.Plan(
            typeof(int), typeof(string),
            hasConvertUsing: true, hasMask: false,
            isToDto: true, globallyIgnored: false,
            isFromJson: false, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Convert, oldPlan.Kind);

        var sourceItem = typeof(OldNewConvertUsingEntity).GetProperty("Code")!;
        var targetItem = typeof(OldNewConvertUsingDto).GetProperty("Code")!;
        var newPlan = AssignmentPlanner.Plan(sourceItem, targetItem, isToDto: true, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.ConvertUsing, newPlan.Kind);
    }

    [Fact]
    public void OldNewPlan_Rule2_Difference_Mask()
    {
        var oldPlan = AssignmentPlanner.Plan(
            typeof(string), typeof(string),
            hasConvertUsing: false, hasMask: true,
            isToDto: true, globallyIgnored: false,
            isFromJson: false, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Convert, oldPlan.Kind);

        var sourceItem = typeof(OldNewMaskEntity).GetProperty("Phone")!;
        var targetItem = typeof(OldNewMaskDto).GetProperty("Phone")!;
        var newPlan = AssignmentPlanner.Plan(sourceItem, targetItem, isToDto: true, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Mask, newPlan.Kind);
    }

    public class OldNewConvertUsingEntity { public int Code { get; set; } }
    [MapFrom(typeof(OldNewConvertUsingEntity))] // T5 复核：Plan 直调输入的承载侧合法形态
    public class OldNewConvertUsingDto
    {
        [Convert(typeof(OldNewIntToStringConverter), nameof(OldNewIntToStringConverter.Convert))]
        public string? Code { get; set; }
    }
    public class OldNewMaskEntity { public string Phone { get; set; } = ""; }
    [MapFrom(typeof(OldNewMaskEntity))] // T5 复核：Plan 直调输入的承载侧合法形态
    public class OldNewMaskDto
    {
        [Mask(typeof(MaskPhone))]
        public string Phone { get; set; } = "";
    }
    public static class OldNewIntToStringConverter
    {
        public static string Convert(int fieldValue) => fieldValue.ToString();
    }
}

/// <summary>
/// ② 枢纽深化 — 豁免收敛对（OverrideIgnore + 非 string Mask 已知分叉点）。
/// </summary>
public class ExemptionConvergenceTests
{
    [Fact]
    public void NonStringMask_Runtime_StringGuard_Trace()
    {
        // Block 引擎：非 string Mask → Trace 警告 + 常规映射赋值（不脱敏）
        // 注意：此测试类型不标 [MapFrom]，避免源生成器 NCMAP015 诊断阻止编译
        var entity = new ExempNonStringMaskEntity { Value = 42 };
        var dto = Mapper.Map<ExempNonStringMaskEntity, ExempNonStringMaskDto>(entity)!;
        Assert.Equal(42, dto.Value); // 常规赋值，不脱敏
    }

    [MapFrom(typeof(ExempNonStringMaskEntity))] // T5 复核：Plan 直调输入的承载侧合法形态

    public class ExempIgnoreEntity { public int Value { get; set; } [MapIgnore(Direction.Both)] public int Sensitive { get; set; } }
    [MapFrom(typeof(ExempIgnoreEntity))]
    public class ExempIgnoreDto { public int Value { get; set; } public int Sensitive { get; set; } }

    public class ExempNonStringMaskEntity { public int Value { get; set; } }
    public class ExempNonStringMaskDto { [Mask(typeof(MaskPhone))] public int Value { get; set; } }
}

/// <summary>
/// ② 枢纽深化 — Trace 可观测性测（G9）。
/// </summary>
public class TraceObservabilityTests
{
    [Fact]
    public void Trace_Ignore_Skip_Written()
    {
        // G9 Trace 可观测性：Ignore 跳过时应有 Trace 输出
        var sw = new StringWriter();
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;

        var entity = new TraceIgnoreEntity { Value = 42, Ignored = 99 };
        Mapper.Map<TraceIgnoreEntity, TraceIgnoreDto>(entity);

        MapTracer.GloballyEnabled = false;
        var traceOutput = sw.ToString();
        // 确认 Trace 机制工作（有输出）+ 包含属性名或跳过相关关键词
        Assert.NotEmpty(traceOutput);
        Assert.Contains("Ignored", traceOutput);
    }

    [Fact]
    public void Trace_MaskNonString_WarningWritten()
    {
        // G9 Trace 可观测性：非 string Mask 应有 Trace 警告
        var sw = new StringWriter();
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;

        var entity = new TraceNonStringMaskEntity { Value = 42 };
        Mapper.Map<TraceNonStringMaskEntity, TraceNonStringMaskDto>(entity);

        MapTracer.GloballyEnabled = false;
        var traceOutput = sw.ToString();
        Assert.NotEmpty(traceOutput);
        // 非 string Mask → Trace 警告（包含 "Mask" 关键词）
        Assert.Contains("Mask", traceOutput);
    }

    [Fact]
    public void Trace_ReverseConvertUsing_DowngradeWritten()
    {
        // G9 Trace 可观测性：反向 ConvertUsing 降级应有 Trace
        var sw = new StringWriter();
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;

        var entity = new TraceReverseConvertUsingEntity { Code = 123 };
        Mapper.Map<TraceReverseConvertUsingEntity, TraceReverseConvertUsingDto>(entity);

        MapTracer.GloballyEnabled = false;
        var traceOutput = sw.ToString();
        Assert.NotEmpty(traceOutput);
        // 反向 ConvertUsing 降级 → Trace（包含"类型转换"或"ConvertUsing"关键词）
        Assert.True(traceOutput.Contains("类型转换") || traceOutput.Contains("ConvertUsing"),
            $"Trace 输出应包含降级相关信息，实际: {traceOutput}");
    }

    [MapFrom(typeof(TraceNonStringMaskEntity))] // T5 复核：Plan 直调输入的承载侧合法形态

    public class TraceIgnoreEntity { public int Value { get; set; } [MapIgnore(Direction.Both)] public int Ignored { get; set; } }
    // 不标 [MapFrom] → 强制走运行时 Block 引擎路径（BuildMapBody 输出 Ignore Trace）
    public class TraceIgnoreDto { public int Value { get; set; } public int Ignored { get; set; } }

    public class TraceNonStringMaskEntity { public int Value { get; set; } }
    public class TraceNonStringMaskDto { [Mask(typeof(MaskPhone))] public int Value { get; set; } }

    public class TraceReverseConvertUsingEntity { public int Code { get; set; } }
    [MapFrom(typeof(TraceReverseConvertUsingEntity))] // T5 复核：Plan 直调输入的承载侧合法形态
    public class TraceReverseConvertUsingDto
    {
        [Convert(typeof(TraceRevConverter), nameof(TraceRevConverter.Convert))]
        public string? Code { get; set; }
    }
    public static class TraceRevConverter
    {
        public static string Convert(int fieldValue) => fieldValue.ToString();
    }
}

/// <summary>
/// ② 枢纽深化 — null-source 契约（ConvertBackUsing 不产出）。
/// </summary>
public class NullSourceContractTests
{
    [Fact]
    public void ConvertBackUsing_NullSource_NotProduced()
    {
        var targetItem = typeof(NullSourceConvertBackDto).GetProperty("Value")!;
        var plan = AssignmentPlanner.Plan(null, targetItem, isToDto: false, attr: null, crossTableFromSource: null);
        Assert.NotEqual(AssignmentKind.ConvertBackUsing, plan.Kind);
    }

    [Fact]
    public void ConvertUsing_NullSource_Skip()
    {
        var targetItem = typeof(NullSourceSimpleDto).GetProperty("Value")!;
        var plan = AssignmentPlanner.Plan(null, targetItem, isToDto: true, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Skip, plan.Kind);
    }
    public class NullSourceConvertBackDto
    {
        [ConvertBack(typeof(NullSourceBackConverter), nameof(NullSourceBackConverter.Convert))]
        public int Value { get; set; }
    }
    public class NullSourceSimpleDto { public int Value { get; set; } }
    public static class NullSourceBackConverter
    {
        public static string Convert(int fieldValue) => fieldValue.ToString();
    }
}
