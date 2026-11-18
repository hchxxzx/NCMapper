using NCMapper.Attributes;
using NCMapper.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace NCMapper.Internal;

/// <summary>
/// 赋值策略契约 — 沿“赋值策略”切缝（④ 策略化深化）。
/// <para>每策略对应一个 <see cref="NCMapper.Shared.AssignmentKind"/>，
/// 由 <see cref="AssignmentStrategyResolver"/> 按 Kind 分发。</para>
/// <para>策略类单一职责：<c>BuildExpression</c> 渲染表达式（依赖 Expression，但可注入 mock
/// BuildSession 做表达式树等价断言单测）；决策（产出 AssignmentPlan）由 ②
/// AssignmentPlanner.Plan 单点承担，策略类不重复决策（② “三渲染器只渲染计划不决策”）。</para>
/// <para>设计理由：② 已建好 RenderPlan（=扩展现有 BuildAssignmentForProperty）按 Kind 分发，
/// 本期抽 IAssignmentStrategy 接口将 RenderPlan 的 switch 分支连同方法体拆为独立策略类
/// （方法体迁移，策略类承载实质渲染逻辑），
/// 使每策略可独立单测 + 为后续 MT-1 partial 拆分留接缝（策略边界即后续 partial 边界）。</para>
/// </summary>
internal interface IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别（与 <see cref="NCMapper.Shared.AssignmentKind"/> 一一对应）。
    /// </summary>
    AssignmentKind Kind { get; }

    /// <summary>
    /// 渲染赋值表达式（替代 RenderPlan switch 分支；方法体由 ExpressionBuilder 迁入，v1.1）。
    /// </summary>
    /// <param name="session">构建会话（不可变上下文，含源/目标类型、参数表达式与属性列表）</param>
    /// <param name="sourceItem">源属性（可为 null）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（CrossTable 等分支需要，现状 RenderPlan 消费；v1.1 新增）</param>
    /// <param name="plan">赋值计划（由 ② AssignmentPlanner.Plan 产出）</param>
    /// <returns>赋值表达式；null 表示跳过该属性（Ignore/Skip）</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan);
}
