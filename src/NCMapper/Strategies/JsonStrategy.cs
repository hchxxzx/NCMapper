using NCMapper.Attributes;
using NCMapper.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;
using static NCMapper.ExpressionBuilder;

namespace NCMapper.Internal;

/// <summary>
/// JSON 转换策略 — AssignmentKind.Json 执行器（④ 策略化深化）。
/// <para>[MapProperty(IsFromJson=true)] 对象 ↔ JSON string 双向转换（原 RenderPlan 分支）。</para>
/// <para>方法体由 ExpressionBuilder 迁入（逐行不动——经 using static 引用留守的
/// GetObjectByJsonExpression/GetJsonByObjectExpression，迁移体零改动）。</para>
/// </summary>
internal sealed class JsonStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.Json"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.Json;

    /// <summary>
    /// 渲染 JSON 转换赋值表达式（string→对象反序列化 / 对象→string 序列化 / string→string 直赋）。
    /// </summary>
    /// <param name="session">构建会话（不可变上下文）</param>
    /// <param name="sourceItem">源属性（Json 必非空——主循环 sourceItem 非空不变式）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（Json 分支不消费——IsFromJson 已编码进 Kind）</param>
    /// <param name="plan">赋值计划</param>
    /// <returns>赋值表达式；值类型组合不支持时返回 null（附 Trace）</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
    {
        // 两行前奏上提共享 helper（原 Property(SourceParameter/TargetParameter) 两行，语义逐字不变）
        var (sourceProperty, targetProperty) = AssignmentStrategyResolver.BuildPropertyPair(session, sourceItem, targetItem);
        // 从 json 转换为对象，调用静态方法
        return BuildJsonAssignment(sourceItem!, targetItem, sourceProperty, targetProperty);
    }

    /// <summary>
    /// 构建 JSON 转换赋值表达式 — 从 BuildMapBody 拆分
    /// <para>由 ExpressionBuilder 迁入（方法体逐行不动）；
    /// internal static——过渡期 RenderPlan（BuildAssignmentForProperty）限定调用。</para>
    /// </summary>
    [RequiresDynamicCode("BuildJsonAssignment builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildJsonAssignment scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression? BuildJsonAssignment(PropertyInfo sourceItem, PropertyInfo targetItem,
        Expression sourceProperty, Expression targetProperty)
    {
        // IsFromJson 服务于"对象 ↔ JSON string"双向转换
        // 对象侧必须是复杂引用类型（非 string、非值类型）

        var srcIsString = sourceItem.PropertyType == typeof(string);
        var tgtIsString = targetItem.PropertyType == typeof(string);
        var srcIsObject = !sourceItem.PropertyType.IsValueType && !srcIsString;
        var tgtIsObject = !targetItem.PropertyType.IsValueType && !tgtIsString;

        if (srcIsString && tgtIsObject)
        {
            // string → 对象：JSON 反序列化
            var expression = GetObjectByJsonExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
            return Assign(targetProperty, expression);
        }

        if (srcIsObject && tgtIsString)
        {
            // 对象 → string：JSON 序列化
            var expression = GetJsonByObjectExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
            return Assign(targetProperty, expression);
        }

        if (srcIsString && tgtIsString)
        {
            // string → string：IsFromJson 无实际效果，直接赋值
            return Assign(targetProperty, sourceProperty);
        }

        // 值类型参与的组合不支持
        MapTracer.Write(
            $"[NCMapper] ExpressionBuilder: 跳过属性 {targetItem.Name}（原因：IsFromJson 仅支持 对象↔string 转换，当前为 {sourceItem.PropertyType.Name}→{targetItem.PropertyType.Name}）");
        return null;
    }
}
