using NCMapper.Attributes;
using NCMapper.Shared;
using NCMapper.Runtime;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace NCMapper.Internal;

/// <summary>
/// ConvertUsing + Mask 组合策略 — AssignmentKind.ConvertUsingThenMask 执行器（④ 策略化深化）。
/// <para>双标有序执行——先转换再脱敏（原 RenderPlan 分支）：转换值经
/// BuildConverterValue 产出后作为 BuildMaskValue 的输入；特性查询走
/// PropertyIgnoreChecker 缓存方法（双标两查询）。</para>
/// </summary>
internal sealed class ConvertUsingThenMaskStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.ConvertUsingThenMask"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.ConvertUsingThenMask;

    /// <summary>
    /// 渲染组合赋值表达式：<c>target.Prop = MaskProvider.Resolve(maskType).Mask(new Converter().Convert(...))</c>。
    /// </summary>
    /// <param name="session">构建会话（不可变上下文）</param>
    /// <param name="sourceItem">源属性（组合必非空——主循环 sourceItem 非空不变式）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（组合分支不消费）</param>
    /// <param name="plan">赋值计划</param>
    /// <returns>赋值表达式</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
    {
        // 两行前奏上提共享 helper（原 Property(SourceParameter/TargetParameter) 两行，语义逐字不变）
        var (sourceProperty, targetProperty) = AssignmentStrategyResolver.BuildPropertyPair(session, sourceItem, targetItem);
        // ConvertUsing + Mask 组合赋值（双标有序执行——先转换再脱敏）
        // 渲染器反射查询两个特性实例（带缓存，与现状 一致）
        // sourceItem 入口保证非空（BuildMapBody 防御性断言）
        var converterAttr = PropertyIgnoreChecker.GetConvertAttribute(targetItem)
            ?? PropertyIgnoreChecker.GetConvertAttribute(sourceItem!);
        if (converterAttr == null)
            throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name, "（ConvertUsingThenMask Kind 但未查到 ConvertUsing 特性——内部不变式违约）"));
        var convertValue = ExpressionBuilder.BuildConverterValue(sourceItem!, targetItem, converterAttr, sourceProperty, session.SourceParameter);
        if (convertValue == null)
            throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name));
        // Mask 腿分派：plan 携带注册表类型名 → 注册表腿（Mask 输入 = 转换结果，先转换再脱敏）；
        // 否则特性腿现状（miss 即抛）
        var registryMaskName = plan.RegistryMaskTypeName;
        var maskCall = registryMaskName is { Length: > 0 }
            ? ExpressionBuilder.BuildMaskValue(
                MaskTypeNameResolver.Resolve(registryMaskName, session.SourceType, session.TargetType, targetItem),
                convertValue, targetItem)
            : ExpressionBuilder.BuildMaskValue(
                PropertyIgnoreChecker.GetMaskAttribute(targetItem)
                ?? throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name, "（ConvertUsingThenMask Kind 但未查到 Mask 特性——内部不变式违约）")),
                convertValue, targetItem);
        return Assign(targetProperty, maskCall);
    }
}
