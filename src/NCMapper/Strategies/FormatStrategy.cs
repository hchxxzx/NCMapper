using NCMapper.Attributes;
using NCMapper.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace NCMapper.Internal;

/// <summary>
/// 格式化策略 — AssignmentKind.Format 执行器。
/// <para>消费 internal 共享 helper（ExpressionBuilder.BuildFormatValue）——helper 留 ExpressionBuilder
/// 原地（MaskStrategy.cs:12-14 注释口径）；特性查询走 PropertyIgnoreChecker.GetMapFormatAttribute
/// 缓存方法（家族惯例，对齐 MaskStrategy，不裸 GetCustomAttribute）。</para>
/// <para>渲染形态：<c>target.Prop = ((IFormattable)src.Prop).ToString(format, null)</c>——
/// 统一绑定接口双参重载（防接口声明源无单参重载编译不过）；可空源由 helper
/// 以 Condition(HasValue, …, null) 形态展开。</para>
/// </summary>
internal sealed class FormatStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.Format"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.Format;

    /// <summary>
    /// 渲染格式化赋值表达式：<c>target.Prop = src.Prop.ToString(format, null)</c>
    /// （目标 string、源 IFormattable——PlanCore 已守卫）。
    /// </summary>
    /// <param name="session">构建会话（不可变上下文）</param>
    /// <param name="sourceItem">源属性（Format 必非空——主循环 sourceItem 非空不变式）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（Format 分支不消费）</param>
    /// <param name="plan">赋值计划</param>
    /// <returns>赋值表达式</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
    {
        // 两行前奏上提共享 helper（原 Property(SourceParameter/TargetParameter) 两行，语义逐字不变）
        var (sourceProperty, targetProperty) = AssignmentStrategyResolver.BuildPropertyPair(session, sourceItem, targetItem);
        // 格式化赋值（string 类型，PlanCore 已守卫）
        // 渲染器按 Kind 反射查询特性实例（带缓存，与 MaskStrategy 一致）
        // sourceItem 入口保证非空（BuildMapBody 防御性断言）
        var formatAttr = PropertyIgnoreChecker.GetMapFormatAttribute(targetItem);
        if (formatAttr == null)
            throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name, "（Format Kind 但未查到特性——内部不变式违约）"));
        var formatValue = ExpressionBuilder.BuildFormatValue(sourceItem!, targetItem, formatAttr, sourceProperty);
        return formatValue == null
            ? throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name))
            : Assign(targetProperty, formatValue);
    }
}
