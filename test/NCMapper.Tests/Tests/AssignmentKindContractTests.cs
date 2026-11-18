// AssignmentKind 双侧表征测试（W4，运行时侧；矩阵 18 用例）
// 断言基准 = 运行时 AssignmentPlanner.Plan(PropertyInfo) 现状实际行为（不预设对错，B5）。
// 分歧用例注释标注 D4-α/β/γ/δ/ε（评审意见_001 P1-1 防撞号编号），生成侧对应断言见
// NCMapper.SourceGen.Tests/AssignmentKindContractGenTests.cs（同矩阵同编号）；分歧清单为
// W5 仲裁输入（触发器 / 范围项）。
// 直调通道先例：PlanConsistencyExtendedTests（test/NCMapper.Tests/Tests/AssignmentPlanHubTestsExtended.cs，
// 含无源直调 Skip 断言，基准自洽）。
using System;
using System.Collections.Generic;
using System.Reflection;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Shared;
using Xunit;

namespace NCMapper.Tests.Tests;

public class AssignmentKindContractTests
{
    // ========== 测试模型（W4 专用，命名带 Contract 前缀防撞） ==========

    public class ContractCrossSource { }                                  // 跨表导航源类型（仅作 Type 标识）
    public class ContractNestedA { public int X { get; set; } }
    public class ContractNestedB { public int X { get; set; } }
    public static class ContractConverter
    {
        public static string Convert(int v) => v.ToString();
        // T8 补锚激活生成的反向签名（NCMAP037 校验：[ConvertBack] 需 int M(string) 形态）
        public static string ConvertBack(string v) => v;
    }

    public class ContractPlainDto { public string Value { get; set; } = ""; }                       // 用例 1
    [MapFrom(typeof(ContractIntSrc))] // T5 复核：Plan 直调输入的承载侧合法形态
    public class ContractConvUsingDto                                                                                 // 用例 2/13/14
    {
        [Convert(typeof(ContractConverter), nameof(ContractConverter.Convert))]
        public string Value { get; set; } = "";
    }
    [MapFrom(typeof(ContractStrSrc))] // T5 复核：Plan 直调输入的承载侧合法形态
    public class ContractMaskDto { [Mask(typeof(MaskPhone))] public string Value { get; set; } = ""; }   // 用例 3/9/10/12
    [MapFrom(typeof(ContractIntSrc))] // T5 复核：Plan 直调输入的承载侧合法形态
    public class ContractDualDto                                                                                          // 用例 11
    {
        [Convert(typeof(ContractConverter), nameof(ContractConverter.Convert))]
        [Mask(typeof(MaskPhone))]
        public string Value { get; set; } = "";
    }
    [MapFrom(typeof(ContractStrSrc))] // T5 复核：Plan 直调输入的承载侧合法形态
    public class ContractBackConvDto { [ConvertBack(typeof(ContractConverter), nameof(ContractConverter.ConvertBack))] public string Value { get; set; } = ""; }  // 用例 4/15

    public class ContractIntSrc { public int Value { get; set; } }
    public class ContractStrSrc { public string Value { get; set; } = ""; }
    public class ContractNestedASrc { public ContractNestedA Item { get; set; } = new(); }
    public class ContractNestedBTgt { public ContractNestedB Item { get; set; } = new(); }
    public class ContractListIntSrc { public List<int> Items { get; set; } = new(); }
    public class ContractListLongTgt { public List<long> Items { get; set; } = new(); }

    private static PropertyInfo Prop<T>(string name) => typeof(T).GetProperty(name)!;
    private static MapPropertyAttribute JsonAttr() => new(typeof(ContractStrSrc), "Value") { IsFromJson = true };

    // ========== 用例族① source 缺失（D4-α 及变体） ==========

    // 矩阵用例 1 ——分歧 D4-α：runtime=Skip ∧ gen=Ignore（分歧固化，不预设对错）
    [Fact]
    public void Case01_NoSource_NoAttr_Runtime_Skip()
    {
        var plan = AssignmentPlanner.Plan(null, Prop<ContractPlainDto>("Value"), isToDto: true, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Skip, plan.Kind);
    }

    // 矩阵用例 2 —— 分歧 D4-α 变体：无源 + [Convert] 正向，runtime=ConvertUsing ∧ gen=Ignore
    [Fact]
    public void Case02_NoSource_ConvertUsing_Runtime_ConvertUsing()
    {
        var plan = AssignmentPlanner.Plan(null, Prop<ContractConvUsingDto>("Value"), isToDto: true, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.ConvertUsing, plan.Kind);
    }

    // 矩阵用例 3 —— 分歧 D4-α 变体：无源 + [Mask]，runtime=Mask ∧ gen=Ignore
    [Fact]
    public void Case03_NoSource_Mask_Runtime_Mask()
    {
        var plan = AssignmentPlanner.Plan(null, Prop<ContractMaskDto>("Value"), isToDto: true, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Mask, plan.Kind);
    }

    // 矩阵用例 4 —— 分歧 D4-α/D4-ε 变体：无源 + [ConvertBack] 反向，runtime=Skip（99 不产 ConvertBackUsing）∧ gen=Ignore
    [Fact]
    public void Case04_NoSource_ConvertBackUsing_Runtime_Skip()
    {
        var plan = AssignmentPlanner.Plan(null, Prop<ContractBackConvDto>("Value"), isToDto: false, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Skip, plan.Kind);
    }

    // ========== 用例族② 判定序交叠（D4-β/γ 含 class 与集合两子形态 + 三因素叠加） ==========

    // 矩阵用例 5 —— 分歧 D4-β：crossTable≠null + 异型 class，runtime=Nested（PlanTypeShape Nested 先于 CrossTable）∧ gen=CrossTable
    [Fact]
    public void Case05_CrossTable_NestedClass_Runtime_Nested()
    {
        var plan = AssignmentPlanner.Plan(Prop<ContractNestedASrc>("Item"), Prop<ContractNestedBTgt>("Item"),
            isToDto: true, attr: null, crossTableFromSource: typeof(ContractCrossSource));
        Assert.Equal(AssignmentKind.Nested, plan.Kind);
    }

    // 矩阵用例 6 —— 一致：crossTable≠null + 等类型，双侧均 CrossTable
    [Fact]
    public void Case06_CrossTable_SameType_Both_CrossTable()
    {
        var plan = AssignmentPlanner.Plan(Prop<ContractStrSrc>("Value"), Prop<ContractStrSrc>("Value"),
            isToDto: true, attr: null, crossTableFromSource: typeof(ContractCrossSource));
        Assert.Equal(AssignmentKind.CrossTable, plan.Kind);
    }

    // 矩阵用例 7 —— 分歧 D4-γ：isFromJson + 异型 class（无 crossTable），runtime=Nested（Nested 先于 Json）∧ gen=Json
    [Fact]
    public void Case07_IsFromJson_NestedClass_Runtime_Nested()
    {
        var plan = AssignmentPlanner.Plan(Prop<ContractNestedASrc>("Item"), Prop<ContractNestedBTgt>("Item"),
            isToDto: true, attr: JsonAttr(), crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Nested, plan.Kind);
    }

    // 矩阵用例 8 —— 一致：isFromJson + 等类型，双侧均 Json
    [Fact]
    public void Case08_IsFromJson_SameType_Both_Json()
    {
        var plan = AssignmentPlanner.Plan(Prop<ContractStrSrc>("Value"), Prop<ContractStrSrc>("Value"),
            isToDto: true, attr: JsonAttr(), crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Json, plan.Kind);
    }

    // 矩阵用例 16 —— 分歧 D4-γ 内部次序（三因素叠加，评审意见_001 P2-2）：isFromJson + crossTable + 异型 class，
    // runtime=Nested（Nested 最优先）∧ gen=Json（生成侧 Json 先于 CrossTable）
    [Fact]
    public void Case16_IsFromJson_CrossTable_NestedClass_Runtime_Nested()
    {
        var plan = AssignmentPlanner.Plan(Prop<ContractNestedASrc>("Item"), Prop<ContractNestedBTgt>("Item"),
            isToDto: true, attr: JsonAttr(), crossTableFromSource: typeof(ContractCrossSource));
        Assert.Equal(AssignmentKind.Nested, plan.Kind);
    }

    // 矩阵用例 17 —— 分歧 D4-γ′（集合形态，评审意见_002 P2-2）：isFromJson + 异型集合，runtime=Collection ∧ gen=Json
    [Fact]
    public void Case17_IsFromJson_Collection_Runtime_Collection()
    {
        var plan = AssignmentPlanner.Plan(Prop<ContractListIntSrc>("Items"), Prop<ContractListLongTgt>("Items"),
            isToDto: true, attr: JsonAttr(), crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Collection, plan.Kind);
    }

    // 矩阵用例 18 —— 分歧 D4-β′（集合形态，评审意见_002 P2-2）：crossTable + 集合，runtime=Collection ∧ gen=CrossTable
    [Fact]
    public void Case18_CrossTable_Collection_Runtime_Collection()
    {
        var plan = AssignmentPlanner.Plan(Prop<ContractListIntSrc>("Items"), Prop<ContractListLongTgt>("Items"),
            isToDto: true, attr: null, crossTableFromSource: typeof(ContractCrossSource));
        Assert.Equal(AssignmentKind.Collection, plan.Kind);
    }

    // ========== 用例族③ 变体（D4-δ）==========

    // 矩阵用例 9 —— 分歧 D4-δ：源 int + 目标 string + 单标 [Mask]（isToDto），runtime=Convert（135 源非 string 落形态）∧ gen=Mask
    [Fact]
    public void Case09_NonStringSource_Mask_Runtime_Convert()
    {
        var plan = AssignmentPlanner.Plan(Prop<ContractIntSrc>("Value"), Prop<ContractMaskDto>("Value"),
            isToDto: true, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Convert, plan.Kind);
    }

    // 矩阵用例 10 —— 一致：源 string + 目标 string + [Mask]，双侧均 Mask
    [Fact]
    public void Case10_StringSource_Mask_Both_Mask()
    {
        var plan = AssignmentPlanner.Plan(Prop<ContractStrSrc>("Value"), Prop<ContractMaskDto>("Value"),
            isToDto: true, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Mask, plan.Kind);
    }

    // 矩阵用例 11 —— 一致：双标 [Convert]+[Mask] 正向，双侧均 ConvertUsingThenMask
    [Fact]
    public void Case11_ConvertUsingThenMask_Both()
    {
        var plan = AssignmentPlanner.Plan(Prop<ContractStrSrc>("Value"), Prop<ContractDualDto>("Value"),
            isToDto: true, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.ConvertUsingThenMask, plan.Kind);
    }

    // 矩阵用例 12 —— 分歧 D4-δ 变体：源 int + [Mask] + 反向（!isToDto），runtime=Convert（135 不命中源守卫 string 分支）∧ gen=Mask（无方向分支）
    [Fact]
    public void Case12_NonStringSource_Mask_Reverse_Runtime_Convert()
    {
        var plan = AssignmentPlanner.Plan(Prop<ContractIntSrc>("Value"), Prop<ContractMaskDto>("Value"),
            isToDto: false, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Convert, plan.Kind);
    }

    // ========== 用例族④ 方向矩阵 ==========

    // 矩阵用例 13 —— 一致：[Convert] 正向（isToDto），双侧均 ConvertUsing
    [Fact]
    public void Case13_ConvertUsing_Forward_Both_ConvertUsing()
    {
        var plan = AssignmentPlanner.Plan(Prop<ContractIntSrc>("Value"), Prop<ContractConvUsingDto>("Value"),
            isToDto: true, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.ConvertUsing, plan.Kind);
    }

    // 矩阵用例 14 —— 一致（标记两侧验证）：[Convert] 反向降级（异型 int→string），
    // 双侧均 Convert + IsConvertedFromConvertUsing=true（异型才携带标记；等型落 Direct 不带）
    [Fact]
    public void Case14_ConvertUsing_Reverse_Downgrade_Both_ConvertWithFlag()
    {
        // 改写：反向承载侧 = sourceItem（DTO 属性）——B12 单查承载侧
        // （恒 DTO 属性：正向 targetItem/反向 sourceItem）；旧直调 DTO 在 targetItem 位
        // 违反调用图约定，随收敛改写
        var plan = AssignmentPlanner.Plan(Prop<ContractConvUsingDto>("Value"), Prop<ContractIntSrc>("Value"),
            isToDto: false, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.Convert, plan.Kind);
        Assert.True(plan.IsConvertedFromConvertUsing);
    }

    // 矩阵用例 15 —— 一致：[ConvertBack] 反向（!isToDto，有源），双侧均 ConvertBackUsing
    [Fact]
    public void Case15_ConvertBackUsing_Reverse_Both()
    {
        // 改写：ConvertBackUsing 双查收敛单查承载侧（反向 sourceItem=DTO 属性）——
        // 旧 targetItem 兜底腿退役；本直调改传承载侧形态（真实调用图同构）
        var plan = AssignmentPlanner.Plan(Prop<ContractBackConvDto>("Value"), Prop<ContractStrSrc>("Value"),
            isToDto: false, attr: null, crossTableFromSource: null);
        Assert.Equal(AssignmentKind.ConvertBackUsing, plan.Kind);
    }
}
