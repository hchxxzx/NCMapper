using NCMapper.Attributes;
using NCMapper.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;
using static NCMapper.ExpressionBuilder;

namespace NCMapper.Internal;

/// <summary>
/// 嵌套类映射策略 — AssignmentKind.Nested 执行器（④ 策略化深化）。
/// <para>异类型 class 递归映射（原 RenderPlan 分支）：BuildNestedAssignment 内部走
/// GetClassExpression 内联优先 + fallback 链（内联深度超限/构建失败回退委托调用）。</para>
/// <para>方法体由 ExpressionBuilder 迁入（逐行不动——经 using static 引用留守的
/// GetClassExpression，迁移体零改动）。</para>
/// </summary>
internal sealed class NestedStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.Nested"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.Nested;

    /// <summary>
    /// 渲染嵌套映射赋值表达式（内联优先 + fallback 委托调用）。
    /// </summary>
    /// <param name="session">构建会话（不可变上下文）</param>
    /// <param name="sourceItem">源属性（Nested 必非空——主循环 sourceItem 非空不变式）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（Nested 分支不消费）</param>
    /// <param name="plan">赋值计划</param>
    /// <returns>赋值表达式</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
    {
        // 两行前奏上提共享 helper（原 Property(SourceParameter/TargetParameter) 两行，语义逐字不变）
        var (sourceProperty, targetProperty) = AssignmentStrategyResolver.BuildPropertyPair(session, sourceItem, targetItem);
        // 异类型 class → 嵌套映射
        return BuildNestedAssignment(sourceItem!, targetItem, sourceProperty, targetProperty);
    }

    /// <summary>
    /// 嵌套类映射赋值（计划器 Kind=Nested 的执行器）— 异类型 class 的递归映射（原 BuildCrossTypeAssignment class 分支）
    /// <para>由 ExpressionBuilder 迁入（方法体逐行不动）；
    /// internal static——过渡期 RenderPlan（BuildAssignmentForProperty）限定调用。</para>
    /// </summary>
    [RequiresDynamicCode("BuildNestedAssignment builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildNestedAssignment scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression? BuildNestedAssignment(PropertyInfo sourceItem, PropertyInfo targetItem,
        Expression sourceProperty, Expression targetProperty)
    {
        //数据类型不一致的：判断都是(非泛型、非数组)class
        var expression = GetClassExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
        return Assign(targetProperty, expression);
    }
}
