using NCMapper.Shared;
using System.Linq.Expressions;
using System.Reflection;

namespace NCMapper.Internal;

/// <summary>
/// 策略分发器 — 按 AssignmentPlan.Kind 查询 IAssignmentStrategy（④ 策略化深化）。
/// <para>替代 RenderPlan（=BuildAssignmentForProperty）的 switch 分支，
/// 改为按 Kind 查表分发；策略类实例无状态，可静态单例。</para>
/// </summary>
internal static class AssignmentStrategyResolver
{
    /// <summary>
    /// 源/目标属性访问表达式对构建
    /// 原 9 处策略 BuildExpression 两行前奏（Property(session.SourceParameter, sourceItem!) +
    /// Property(session.TargetParameter, targetItem)）上提为共享单点，九策略改调用。
    /// </summary>
    /// <param name="session">构建会话</param>
    /// <param name="sourceItem">源属性（可为 null，调用方保证已判空）</param>
    /// <param name="targetItem">目标属性</param>
    /// <returns>(源属性访问表达式, 目标属性访问表达式)</returns>
    internal static (Expression SourceProperty, Expression TargetProperty) BuildPropertyPair(
        in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem)
        => (Expression.Property(session.SourceParameter, sourceItem!),
            Expression.Property(session.TargetParameter, targetItem));

    // 策略数组 — 按 Kind 枚举值索引（12 槽位与 AssignmentKind 一一对应， 全量就位）。
    // 主循环接入在，此前策略产出仅由等价测消费。
    private static readonly IAssignmentStrategy?[] s_strategies = BuildStrategies();

    private static IAssignmentStrategy?[] BuildStrategies()
    {
        // 槽位按枚举定义数定长（原硬编码 12
        // 枚举扩值即静态构造越界 → TypeInitializationException 永久缓存）
        var arr = new IAssignmentStrategy?[Enum.GetValues<AssignmentKind>().Length];
        arr[(int)AssignmentKind.Direct] = new DirectStrategy();
        arr[(int)AssignmentKind.Convert] = new ConvertStrategy();
        arr[(int)AssignmentKind.Collection] = new CollectionStrategy();
        arr[(int)AssignmentKind.Nested] = new NestedStrategy();
        arr[(int)AssignmentKind.Ignore] = new IgnoreStrategy();
        arr[(int)AssignmentKind.Skip] = new SkipStrategy();
        arr[(int)AssignmentKind.Json] = new JsonStrategy();
        arr[(int)AssignmentKind.CrossTable] = new CrossTableStrategy();
        arr[(int)AssignmentKind.ConvertUsing] = new ConvertUsingStrategy();
        arr[(int)AssignmentKind.Mask] = new MaskStrategy();
        arr[(int)AssignmentKind.ConvertUsingThenMask] = new ConvertUsingThenMaskStrategy();
        arr[(int)AssignmentKind.ConvertBackUsing] = new ConvertBackUsingStrategy();
        // （硬义务）：Format 族两槽位注册
        // 漏注册 → Resolve 返回 null → ExpressionBuilder strategy?.BuildExpression 产 null → 属性静默跳过赋值
        arr[(int)AssignmentKind.Format] = new FormatStrategy();
        arr[(int)AssignmentKind.FormatThenMask] = new FormatThenMaskStrategy();
        return arr;
    }

    /// <summary>
    /// 按赋值计划查询策略。
    /// </summary>
    /// <param name="plan">赋值计划（由 ② AssignmentPlanner.Plan 产出）</param>
    /// <returns>策略实例；null 表示 Kind 越界（理论不可达）</returns>
    public static IAssignmentStrategy? Resolve(AssignmentPlan plan)
        => (int)plan.Kind < s_strategies.Length ? s_strategies[(int)plan.Kind] : null;
}
