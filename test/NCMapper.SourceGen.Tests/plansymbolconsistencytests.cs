using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.Shared;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 计划器一致性测试（选项甲 / Q1 / S4）：
/// ② 枢纽深化（T6）：TypeHelpers.PlanSymbol 已删除，生成侧验证待 T7 重写为 PlanSymbolAdapter 一致性测试。
/// 当前保留运行时 AssignmentPlanner.Plan（Type 版）决策矩阵验证。
/// </summary>
public class PlanSymbolConsistencyTests
{
    public class A { public int X { get; set; } }
    public class B { public int X { get; set; } }

    /// <summary>决策矩阵：Plan 必须给出预期 Kind（字符串承载，规避 internal 可见性）。</summary>
    public static TheoryData<Type, Type, string> Matrix => new()
    {
        // 等类型 → Direct（含同型可空）
        { typeof(int), typeof(int), "Direct" },
        { typeof(string), typeof(string), "Direct" },
        { typeof(int?), typeof(int?), "Direct" },
        { typeof(A), typeof(A), "Direct" },
        // 类型不同 → Convert（含无转换路径组合、可空→可空异型）
        { typeof(int), typeof(long), "Convert" },
        { typeof(string), typeof(int), "Convert" },
        { typeof(int), typeof(DateTime), "Convert" },
        { typeof(int?), typeof(long?), "Convert" },
        // 可空组合 → Convert
        { typeof(int?), typeof(int), "Convert" },
        { typeof(int), typeof(int?), "Convert" },
        // 嵌套 → Nested
        { typeof(A), typeof(B), "Nested" },
        // 集合 → Collection（同型/异型/数组/字典）
        { typeof(List<int>), typeof(List<int>), "Collection" },
        { typeof(List<int>), typeof(List<long>), "Collection" },
        { typeof(int[]), typeof(List<int>), "Collection" },
        { typeof(Dictionary<string, int>), typeof(Dictionary<string, long>), "Collection" },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void RuntimePlan_Consistent_With_ExpectedKind(Type source, Type target, string expectedKind)
    {
        // ② T6：生成侧 TypeHelpers.PlanSymbol 已删除，待 T7 重写为 PlanSymbolAdapter 一致性测试
        // 运行时入口（Type 版）
        var runtimePlan = AssignmentPlanner.Plan(
            source, target,
            hasConvertUsing: false, hasMask: false,
            isToDto: false, globallyIgnored: false,
            isFromJson: false, crossTableFromSource: null);
        Assert.Equal(expectedKind, runtimePlan.Kind.ToString());
    }
}
