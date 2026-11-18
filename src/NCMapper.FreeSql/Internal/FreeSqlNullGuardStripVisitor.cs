// FreeSql 通道嵌套投影 IIF null 保护剥除器
// 背景：共享内核嵌套投影对 MemberInit 无条件注入 Expression.Condition 空传播保护
//（src/NCMapper/Internal/ProjectionExpressionFactory.cs 的 nullCheck ? null : memberInit 注入点，
// 无 provider 差异开关）。EF Core 翻译器支持该 IIF 形态；FreeSql 翻译器不支持 IIF 内嵌 MemberInit，
// 运行时抛内部异常「null.Id cannot be null」（用户不可感知根因，已实测实证）。
// 本剥除器在 FreeSql 通道终端方法 SQL 投影分支（且仅该分支，C3）将产物中的该形态剥除为纯 MemberInit，
// 恢复 FreeSql SQL 投影能力。语义变化：null 导航 → 空壳 DTO（Dept!=null、
// int 字段 0、引用字段 null），与 IIF 形态的「Dept=null」不等价——LEFT JOIN 无匹配时字段取默认值。
// 形态判据（全部满足才剥）：①节点为 ConditionalExpression；
// ②IfTrue 为 ConstantExpression 且值为 null 且类型与节点类型一致（防御性对称：IfFalse 同构）；
// ③IfFalse 为 MemberInitExpression（内核产物恒为纯 MemberInit，无 Convert 包裹）。
// 内核全部 5 处 Condition 注入点中仅 nullCheck 注入点与判据匹配，其余 4 处在 FreeSql 通道各有前置拦截
//——剥除器对 EF/Dapper 通道零影响（本类型不在其依赖图内）。
// 处理顺序：自底向上（先 Visit 子节点、后判当前节点），剥除计数每层独立累加（C5，供单测断言）。
using System.Linq.Expressions;

namespace NCMapper.FreeSql.Internal;

/// <summary>
/// FreeSql 通道嵌套投影 IIF null 保护剥除器
/// <para>仅服务 FreeSql 通道 SQL 投影分支；<c>FreeSqlProjectionBuilder.Build</c> 本体与
/// chunk 内存编译路径不经过本类型（chunk 内存求值保留原版精确 null 语义）。</para>
/// </summary>
internal sealed class FreeSqlNullGuardStripVisitor : ExpressionVisitor
{
    /// <summary>本次遍历剥除的 Condition 节点数（每层独立累加，C5 单测断言面）</summary>
    internal int StrippedCount { get; private set; }

    private FreeSqlNullGuardStripVisitor()
    {
    }

    /// <summary>
    /// 剥除投影 lambda 中的 IIF null 保护形态
    /// <para>无匹配节点时原样返回传入 lambda（引用等价，零开销）；有匹配时重建 lambda。</para>
    /// </summary>
    internal static Expression<Func<TSource, TTarget>> Strip<TSource, TTarget>(
        Expression<Func<TSource, TTarget>> lambda)
        where TSource : class
        where TTarget : class
    {
        var visitor = new FreeSqlNullGuardStripVisitor();
        var newBody = visitor.Visit(lambda.Body);
        if (visitor.StrippedCount == 0)
            return lambda;
        return Expression.Lambda<Func<TSource, TTarget>>(newBody!, lambda.Parameters);
    }

    /// <inheritdoc />
    protected override Expression VisitConditional(ConditionalExpression node)
    {
        // 自底向上：先处理子节点（嵌套多层时内层先剥），再判当前节点
        var visited = (ConditionalExpression)base.VisitConditional(node);

        // 正向形态（内核产物 :1031）：IIF(test, null, memberInit)
        if (visited.IfTrue is ConstantExpression ifTrue
            && ifTrue.Value is null
            && ifTrue.Type == visited.Type
            && visited.IfFalse is MemberInitExpression)
        {
            StrippedCount++;
            return visited.IfFalse;
        }

        // 防御性对称形态（产物中不出现，评估 B1 反转形态实证另有炸点，仅防御兼容）
        if (visited.IfFalse is ConstantExpression ifFalse
            && ifFalse.Value is null
            && ifFalse.Type == visited.Type
            && visited.IfTrue is MemberInitExpression)
        {
            StrippedCount++;
            return visited.IfTrue;
        }

        return visited;
    }
}
