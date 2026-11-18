using NCMapper.Attributes;
using NCMapper.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace NCMapper.Internal;

/// <summary>
/// ConvertUsing 自定义转换器策略 — AssignmentKind.ConvertUsing 执行器（④ 策略化深化）。
/// <para>正向消费 [Convert]（原 RenderPlan 分支）：特性查询走
/// PropertyIgnoreChecker 缓存方法（承载侧单查 targetItem——删除段收口，
/// 对侧兜底支已删；PlanCore 已单查承载侧，本处 `?? 对侧` 为不变式保证不可达的死防御，
/// 保留即未来改动时绕过锚门消费源侧特性的违规入口）；原"对侧消费 targetItem 优先，
/// 原注释属 ADR-002 前口径，已废；
/// 转换器值构建消费 internal 化共享 helper ExpressionBuilder.BuildConverterValue。</para>
/// </summary>
internal sealed class ConvertUsingStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.ConvertUsing"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.ConvertUsing;

    /// <summary>
    /// 渲染转换器赋值表达式：<c>target.Prop = new Converter().Convert(source.Prop, source)</c>。
    /// </summary>
    /// <param name="session">构建会话（不可变上下文）</param>
    /// <param name="sourceItem">源属性（ConvertUsing 必非空——主循环 sourceItem 非空不变式）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（ConvertUsing 分支不消费）</param>
    /// <param name="plan">赋值计划</param>
    /// <returns>赋值表达式</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
    {
        // 两行前奏上提共享 helper（原 Property(SourceParameter/TargetParameter) 两行，语义逐字不变）
        var (sourceProperty, targetProperty) = AssignmentStrategyResolver.BuildPropertyPair(session, sourceItem, targetItem);
        // ConvertUsing 自定义转换器赋值（正向消费）
        // 渲染器按 Kind 反射查询特性实例（带缓存，与现状 一致）
        // sourceItem 入口保证非空（BuildMapBody 防御性断言）
        // 承载侧单查（PlanCore 仅在承载侧 GetConvertAttribute(targetItem) 命中且
        // isToDto 时产出本 Kind → 进入本策略时 targetItem 侧特性必非 null，对侧支不可达）
        var converterAttr = PropertyIgnoreChecker.GetConvertAttribute(targetItem);
        if (converterAttr == null)
            throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name, "（ConvertUsing Kind 但未查到特性——内部不变式违约）"));
        var convertValue = ExpressionBuilder.BuildConverterValue(sourceItem!, targetItem, converterAttr, sourceProperty, session.SourceParameter);
        return convertValue == null
            ? throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name))
            : Assign(targetProperty, convertValue);
    }
}
