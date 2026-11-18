using NCMapper.Attributes;
using NCMapper.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace NCMapper.Internal;

/// <summary>
/// ConvertBackUsing 反向转换器策略 — AssignmentKind.ConvertBackUsing 执行器（④ 策略化深化）。
/// <para>仅反向（DTO→Entity）消费（原 RenderPlan 分支）：特性查询走
/// PropertyIgnoreChecker 缓存方法（承载侧单查 sourceItem（反向 DTO 侧）
/// 删除段收口，对侧兜底支已删；PlanCore 已保证 sourceItem 侧 [ConvertBack] 非 null，
/// 本处 `?? 对侧` 为不变式保证不可达的死防御；原"sourceItem 优先，对侧消费，
/// 原注释属 ADR-002 前口径，已废）；
/// 转换器值构建消费 internal 化共享 helper ExpressionBuilder.BuildBackConverterValue。</para>
/// </summary>
internal sealed class ConvertBackUsingStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.ConvertBackUsing"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.ConvertBackUsing;

    /// <summary>
    /// 渲染反向转换器赋值表达式：<c>target.Prop = new Converter().Convert(dto.Prop, dto)</c>。
    /// </summary>
    /// <param name="session">构建会话（不可变上下文）</param>
    /// <param name="sourceItem">源属性（反向映射中=DTO 属性；ConvertBackUsing 必非空——PlanCore 已判断）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（ConvertBackUsing 分支不消费）</param>
    /// <param name="plan">赋值计划</param>
    /// <returns>赋值表达式</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
    {
        // 两行前奏上提共享 helper（原 Property(SourceParameter/TargetParameter) 两行，语义逐字不变）
        var (sourceProperty, targetProperty) = AssignmentStrategyResolver.BuildPropertyPair(session, sourceItem, targetItem);
        // ConvertBackUsing 反向转换器赋值（仅反向消费，PlanCore 已判断 sourceItem != null）
        // 渲染器按 Kind 反射查询特性实例（带缓存，与现状 一致）
        // 承载侧单查（PlanCore 保证反向 sourceItem 侧 [ConvertBack] 非 null
        // → 进入本策略时对侧支不可达）
        var backAttr = PropertyIgnoreChecker.GetConvertBackAttribute(sourceItem!);
        if (backAttr == null)
            throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name, "（ConvertBackUsing Kind 但未查到特性——内部不变式违约）"));
        var convertValue = ExpressionBuilder.BuildBackConverterValue(sourceItem!, targetItem, backAttr, sourceProperty, session.SourceParameter);
        return convertValue == null
            ? throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name))
            : Assign(targetProperty, convertValue);
    }
}
