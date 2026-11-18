using NCMapper.Attributes;
using NCMapper.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace NCMapper.Internal;

/// <summary>
/// 忽略策略 — AssignmentKind.Ignore 防御壳（④ 策略化深化）。
/// <para>主循环对 plan.Kind==Ignore 提前 continue（含 Trace，:217-221），Ignore Kind 不进入
/// 策略分发——本类仅防御 Plan 与分发器不一致的内部不变式违约，返回 null；主循环 continue
/// 逻辑与 Trace 时序不迁移（保持现状触发时机）。</para>
/// <para>不含 Trace 调用（Trace 保留主循环原处——F3）。</para>
/// </summary>
internal sealed class IgnoreStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.Ignore"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.Ignore;

    /// <summary>
    /// 防御壳：恒返回 null（正常路径主循环提前 continue 不进入分发）。
    /// </summary>
    /// <param name="session">构建会话（不消费）</param>
    /// <param name="sourceItem">源属性（不消费）</param>
    /// <param name="targetItem">目标属性（不消费）</param>
    /// <param name="attr">[MapProperty] 特性（不消费）</param>
    /// <param name="plan">赋值计划（不消费）</param>
    /// <returns>null（忽略不构建赋值）</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
        => null;
}
