// AssignmentKind 双侧表征测试（W4，生成侧；矩阵 18 用例）
// 断言基准 = 生成侧 PlanSymbolAdapter.Plan 现状实际行为（不预设对错，B5）。
// 分歧用例注释标注 D4-α/β/γ/δ/ε（与运行时侧 NCMapper.Tests/AssignmentKindContractTests.cs 同矩阵同编号）；
// 分歧清单为 W5 仲裁输入（触发器 / 范围项）。
// TypeRef 构造复用 TypeToSymbol + MappingExtractor.TypeRefFromSymbol 通道（与 PlanForAssignmentMappingTests
// / TypeCompatViewConsistencyTests 同模式，勿新造轮子）。
// 断言形态注：Kind 以 ToString() 字符串比对——AssignmentPlan/AssignmentKind 经 SourceGen.csproj Link
// 同时编入 NCMapper 与 NCMapper.SourceGen 双程序集，Spell 名会触发 CS0433（PlanForAssignmentMappingTests 同规避）。
// Ignore 取舍注：生成侧 Ignore 由调用方（属性发射侧）承载，非 adapter 契约——
// 用例 1-4 的 gen=Ignore 仅锁定 source==null 前置分支现状（D4-α），不新增 Ignore 契约用例。
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

public class AssignmentKindContractGenTests
{
    // ========== 测试模型（与运行时侧同构） ==========

    public class ContractCrossSource { }
    public class ContractNestedA { public int X { get; set; } }
    public class ContractNestedB { public int X { get; set; } }

    private static readonly Lazy<CSharpCompilation> s_compilation = new(() =>
        (CSharpCompilation)TestHelper.BuildCompilation("class ContractGenMarker { }"));

    private static ITypeSymbol TypeToSymbol(Type type)
    {
        var compilation = s_compilation.Value;
        if (type.IsArray)
            return compilation.CreateArrayTypeSymbol(TypeToSymbol(type.GetElementType()!));

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var def = compilation.GetTypeByMetadataName(type.GetGenericTypeDefinition().FullName!);
            Assert.NotNull(def);
            return def!.Construct(type.GetGenericArguments().Select(TypeToSymbol).ToArray());
        }

        var symbol = compilation.GetTypeByMetadataName(type.FullName!);
        Assert.NotNull(symbol);
        return symbol!;
    }

    private static TypeRef TypeToRef(Type type) => MappingExtractor.TypeRefFromSymbol(TypeToSymbol(type));

    /// <summary>adapter 直调通道：返回 Kind 字符串（规避 CS0433，见文件头注）+ 标记位 out 承载。</summary>
    private static string PlanKind(Type? source, Type target,
        bool hasConvertUsing, bool hasMask, bool hasConvertBackUsing,
        bool isToDto, bool isFromJson, Type? crossTableFromSource,
        out bool isConvertedFromConvertUsing)
    {
        var plan = PlanSymbolAdapter.Plan(
            source is null ? null : TypeToRef(source), TypeToRef(target),
            hasConvertUsing, hasMask, hasConvertBackUsing,
            isToDto, isFromJson,
            crossTableFromSource is null ? null : TypeToRef(crossTableFromSource));
        isConvertedFromConvertUsing = plan.IsConvertedFromConvertUsing;
        return plan.Kind.ToString();
    }

    private static string PlanKind(Type? source, Type target,
        bool hasConvertUsing = false, bool hasMask = false, bool hasConvertBackUsing = false,
        bool isToDto = true, bool isFromJson = false, Type? crossTableFromSource = null)
        => PlanKind(source, target, hasConvertUsing, hasMask, hasConvertBackUsing,
            isToDto, isFromJson, crossTableFromSource, out _);

    // ========== 用例族① source 缺失（D4-α 及变体） ==========

    // 矩阵用例 1 ——分歧 D4-α：gen=Ignore ∧ runtime=Skip（分歧固化，不预设对错）
    [Fact]
    public void Case01_NullSource_Gen_Ignore()
    {
        Assert.Equal("Ignore", PlanKind(null, typeof(string)));
    }

    // 矩阵用例 2 —— 分歧 D4-α 变体：source null 前置早退（42-44）先于 ConvertUsing 判定 → gen=Ignore
    [Fact]
    public void Case02_NullSource_ConvertUsing_Gen_Ignore()
    {
        Assert.Equal("Ignore", PlanKind(null, typeof(string), hasConvertUsing: true));
    }

    // 矩阵用例 3 —— 分歧 D4-α 变体：source null 前置早退先于 Mask 判定 → gen=Ignore
    [Fact]
    public void Case03_NullSource_Mask_Gen_Ignore()
    {
        Assert.Equal("Ignore", PlanKind(null, typeof(string), hasMask: true));
    }

    // 矩阵用例 4 —— 分歧 D4-α/D4-ε 变体：source null 前置早退先于 ConvertBackUsing 判定（46-48 实际不可达） → gen=Ignore
    [Fact]
    public void Case04_NullSource_ConvertBackUsing_Gen_Ignore()
    {
        Assert.Equal("Ignore", PlanKind(null, typeof(string), hasConvertBackUsing: true, isToDto: false));
    }

    // ========== 用例族② 判定序交叠（D4-β/γ 含 class 与集合两子形态 + 三因素叠加） ==========

    // 矩阵用例 5 —— 分歧 D4-β：crossTable≠null + 异型 class，gen=CrossTable（75-77 先于类型形态）∧ runtime=Nested
    [Fact]
    public void Case05_CrossTable_NestedClass_Gen_CrossTable()
    {
        Assert.Equal("CrossTable", PlanKind(typeof(ContractNestedA), typeof(ContractNestedB),
            crossTableFromSource: typeof(ContractCrossSource)));
    }

    // 矩阵用例 6 —— 一致：crossTable≠null + 等类型，双侧均 CrossTable
    [Fact]
    public void Case06_CrossTable_SameType_Both_CrossTable()
    {
        Assert.Equal("CrossTable", PlanKind(typeof(string), typeof(string),
            crossTableFromSource: typeof(ContractCrossSource)));
    }

    // 矩阵用例 7 —— 分歧 D4-γ：isFromJson + 异型 class（无 crossTable），gen=Json（71-73 先于类型形态）∧ runtime=Nested
    [Fact]
    public void Case07_IsFromJson_NestedClass_Gen_Json()
    {
        Assert.Equal("Json", PlanKind(typeof(ContractNestedA), typeof(ContractNestedB), isFromJson: true));
    }

    // 矩阵用例 8 —— 一致：isFromJson + 等类型，双侧均 Json
    [Fact]
    public void Case08_IsFromJson_SameType_Both_Json()
    {
        Assert.Equal("Json", PlanKind(typeof(string), typeof(string), isFromJson: true));
    }

    // 矩阵用例 16 —— 分歧 D4-γ 内部次序（三因素叠加，评审意见_001 P2-2）：isFromJson + crossTable + 异型 class，
    // gen=Json（71-73 先于 :75-77 CrossTable）∧ runtime=Nested（Nested 最优先）
    [Fact]
    public void Case16_IsFromJson_CrossTable_NestedClass_Gen_Json()
    {
        Assert.Equal("Json", PlanKind(typeof(ContractNestedA), typeof(ContractNestedB),
            isFromJson: true, crossTableFromSource: typeof(ContractCrossSource)));
    }

    // 矩阵用例 17 —— 分歧 D4-γ′（集合形态，评审意见_002 P2-2）：isFromJson + 异型集合，gen=Json ∧ runtime=Collection
    [Fact]
    public void Case17_IsFromJson_Collection_Gen_Json()
    {
        Assert.Equal("Json", PlanKind(typeof(List<int>), typeof(List<long>), isFromJson: true));
    }

    // 矩阵用例 18 —— 分歧 D4-β′（集合形态，评审意见_002 P2-2）：crossTable + 集合，gen=CrossTable ∧ runtime=Collection
    [Fact]
    public void Case18_CrossTable_Collection_Gen_CrossTable()
    {
        Assert.Equal("CrossTable", PlanKind(typeof(List<int>), typeof(List<long>),
            crossTableFromSource: typeof(ContractCrossSource)));
    }

    // ========== 用例族③ 变体（D4-δ）==========

    // 矩阵用例 9 —— 分歧 D4-δ：源 int + 目标 string + 单标 [Mask]（isToDto），gen=Mask（59-65 无源守卫）∧ runtime=Convert
    [Fact]
    public void Case09_NonStringSource_Mask_Gen_Mask()
    {
        Assert.Equal("Mask", PlanKind(typeof(int), typeof(string), hasMask: true));
    }

    // 矩阵用例 10 —— 一致：源 string + 目标 string + [Mask]，双侧均 Mask
    [Fact]
    public void Case10_StringSource_Mask_Both_Mask()
    {
        Assert.Equal("Mask", PlanKind(typeof(string), typeof(string), hasMask: true));
    }

    // 矩阵用例 11 —— 一致：双标 [Convert]+[Mask] 正向，双侧均 ConvertUsingThenMask
    [Fact]
    public void Case11_ConvertUsingThenMask_Both()
    {
        Assert.Equal("ConvertUsingThenMask", PlanKind(typeof(string), typeof(string), hasConvertUsing: true, hasMask: true));
    }

    // 矩阵用例 12 —— 分歧 D4-δ 变体：源 int + [Mask] + 反向（!isToDto），gen=Mask（无方向分支）∧ runtime=Convert
    [Fact]
    public void Case12_NonStringSource_Mask_Reverse_Gen_Mask()
    {
        Assert.Equal("Mask", PlanKind(typeof(int), typeof(string), hasMask: true, isToDto: false));
    }

    // ========== 用例族④ 方向矩阵 ==========

    // 矩阵用例 13 —— 一致：[Convert] 正向（isToDto），双侧均 ConvertUsing
    [Fact]
    public void Case13_ConvertUsing_Forward_Both_ConvertUsing()
    {
        Assert.Equal("ConvertUsing", PlanKind(typeof(int), typeof(string), hasConvertUsing: true));
    }

    // 矩阵用例 14 —— 一致（标记两侧验证）：[Convert] 反向降级（异型 int→string），
    // 双侧均 Convert + IsConvertedFromConvertUsing=true（异型才携带标记；等型落 Direct 不带）
    [Fact]
    public void Case14_ConvertUsing_Reverse_Downgrade_Both_ConvertWithFlag()
    {
        var kind = PlanKind(typeof(int), typeof(string),
            hasConvertUsing: true, hasMask: false, hasConvertBackUsing: false,
            isToDto: false, isFromJson: false, crossTableFromSource: null,
            isConvertedFromConvertUsing: out var downgraded);
        Assert.Equal("Convert", kind);
        Assert.True(downgraded);
    }

    // 矩阵用例 15 —— 一致：[ConvertBack] 反向（!isToDto，有源），双侧均 ConvertBackUsing
    [Fact]
    public void Case15_ConvertBackUsing_Reverse_Both()
    {
        Assert.Equal("ConvertBackUsing", PlanKind(typeof(string), typeof(string), hasConvertBackUsing: true, isToDto: false));
    }
}
