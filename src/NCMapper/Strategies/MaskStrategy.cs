using NCMapper.Attributes;
using NCMapper.Shared;
using NCMapper.Runtime;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace NCMapper.Internal;

/// <summary>
/// Mask 脱敏策略 — AssignmentKind.Mask 执行器（④ 策略化深化， 试点）。
/// <para>消费 internal 化共享 helper（ExpressionBuilder.BuildMaskAssignment 四参形态 +
/// BuildMaskValue）——helper 留在 ExpressionBuilder 原处（被 BuildFlattenedAssignmentFromPath、
/// BuildReversePathWriteBack、BuildCrossTableAssignment 等保留路径交叉消费，不可迁入本类）。</para>
/// <para>特性查询走 PropertyIgnoreChecker.GetMaskAttribute 缓存方法（对齐原 RenderPlan，
/// 不裸 GetCustomAttribute）。</para>
/// </summary>
internal sealed class MaskStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.Mask"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.Mask;

    /// <summary>
    /// 渲染 Mask 脱敏赋值表达式：<c>target.Prop = MaskProvider.Resolve(maskType).Mask(source.Prop)</c>
    /// （string 类型，PlanCore 已守卫）。
    /// </summary>
    /// <param name="session">构建会话（不可变上下文）</param>
    /// <param name="sourceItem">源属性（Mask 必非空——主循环 sourceItem 非空不变式）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（Mask 分支不消费）</param>
    /// <param name="plan">赋值计划</param>
    /// <returns>赋值表达式</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
    {
        // 两行前奏上提共享 helper（原 Property(SourceParameter/TargetParameter) 两行，语义逐字不变）
        var (sourceProperty, targetProperty) = AssignmentStrategyResolver.BuildPropertyPair(session, sourceItem, targetItem);
        // 渲染分派：plan 携带注册表类型名 → 注册表腿（解析 + Type 承载重载）；
        // 否则特性腿现状（渲染器按 Kind 反射查询特性实例，带缓存，与现状一致）。
        // 决策腿已守卫特性腿先行——plan 携带注册表名时特性腿未命中，两种来源互斥到达
        if (plan.RegistryMaskTypeName is { Length: > 0 } registryMaskName)
        {
            var registryMaskType = MaskTypeNameResolver.Resolve(registryMaskName, session.SourceType, session.TargetType, targetItem);
            return ExpressionBuilder.BuildMaskAssignment(registryMaskType, sourceProperty, (MemberExpression)targetProperty, targetItem);
        }
        var maskAttr = PropertyIgnoreChecker.GetMaskAttribute(targetItem);
        if (maskAttr == null)
            throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name, "（Mask Kind 但未查到特性——内部不变式违约）"));
        // targetProperty 由 Property(targetParameter, targetItem) 产出，运行时为 MemberExpression
        // 四参调用（含 MemberExpression targetProperty）——对齐原 RenderPlan :457
        return ExpressionBuilder.BuildMaskAssignment(maskAttr, sourceProperty, (MemberExpression)targetProperty, targetItem);
    }
}
