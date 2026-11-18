using NCMapper.Attributes;
using NCMapper.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace NCMapper.Internal;

/// <summary>
/// 直接赋值策略 — AssignmentKind.Direct 执行器（④ 策略化深化， 试点）。
/// <para>等类型属性直接赋值（原 RenderPlan Direct 分支）：
/// <c>target.Prop = source.Prop</c>。无迁移方法（三种实现形态之一——无迁移）。</para>
/// </summary>
internal sealed class DirectStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.Direct"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.Direct;

    /// <summary>
    /// 渲染直接赋值表达式：<c>Assign(targetProperty, sourceProperty)</c>。
    /// </summary>
    /// <param name="session">构建会话（不可变上下文）</param>
    /// <param name="sourceItem">源属性（Direct 必非空——主循环 sourceItem 非空不变式）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（Direct 分支不消费）</param>
    /// <param name="plan">赋值计划</param>
    /// <returns>赋值表达式</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
    {
        // 常规字段（类型相同的），直接赋值（对齐原 RenderPlan :395-397：调用方构造的 sourceProperty/targetProperty）
        // 两行前奏上提共享 helper（原 Property(SourceParameter/TargetParameter) 两行，语义逐字不变）
        var (sourceProperty, targetProperty) = AssignmentStrategyResolver.BuildPropertyPair(session, sourceItem, targetItem);
        return Assign(targetProperty, sourceProperty);
    }
}
