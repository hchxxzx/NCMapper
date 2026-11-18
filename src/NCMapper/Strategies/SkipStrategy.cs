using NCMapper.Attributes;
using NCMapper.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace NCMapper.Internal;

/// <summary>
/// 跳过策略 — AssignmentKind.Skip 防御壳（④ 策略化深化）。
/// <para>主循环对不可达属性提前 continue，Skip Kind 不进入策略分发——本类仅防御 Plan 与
/// 分发器不一致的内部不变式违约，返回 null（同 IgnoreStrategy 定位）。</para>
/// <para>不含 Trace 调用（Trace 保留主循环原处）。</para>
/// </summary>
internal sealed class SkipStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.Skip"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.Skip;

    /// <summary>
    /// 防御壳：恒返回 null（正常路径不可达属性不进入分发）。
    /// </summary>
    /// <param name="session">构建会话（不消费）</param>
    /// <param name="sourceItem">源属性（不消费）</param>
    /// <param name="targetItem">目标属性（不消费）</param>
    /// <param name="attr">[MapProperty] 特性（不消费）</param>
    /// <param name="plan">赋值计划（不消费）</param>
    /// <returns>null（跳过不构建赋值）</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
        => null;
}
