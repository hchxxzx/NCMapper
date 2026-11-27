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
    /// 剥除投影 lambda 中的 IIF null 保护形态（**单参数**形态——单表 AsDto 通道）
    /// <para>无匹配节点时原样返回传入 lambda（引用等价，零开销）；有匹配时重建 lambda。</para>
    /// </summary>
    internal static Expression<Func<TSource, TTarget>> Strip<TSource, TTarget>(
        Expression<Func<TSource, TTarget>> lambda)
        where TSource : class
        where TTarget : class
    {
        var stripped = StripLambda(lambda);
        return ReferenceEquals(stripped, lambda)
            ? lambda
            : (Expression<Func<TSource, TTarget>>)stripped;
    }

    /// <summary>
    /// 剥除投影 lambda 中的 IIF null 保护形态（**双参数**形态——双表 Compose 通道，卡 C3）
    /// <para>判据与单参重载逐字同源（同一 <see cref="StripLambda"/> 单点）：元数与判据无关，
    /// 差异只在委托类型重建——Compose 产物是 <c>Func&lt;T1,T2,TTarget&gt;</c>，
    /// 单参强类型签名无法承载故另设重载。</para>
    /// </summary>
    internal static Expression<Func<T1, T2, TTarget>> Strip<T1, T2, TTarget>(
        Expression<Func<T1, T2, TTarget>> lambda)
        where T1 : class
        where T2 : class
        where TTarget : class
    {
        var stripped = StripLambda(lambda);
        return ReferenceEquals(stripped, lambda)
            ? lambda
            : (Expression<Func<T1, T2, TTarget>>)stripped;
    }

    /// <summary>
    /// 剥除投影 lambda 中的 IIF null 保护形态（**三参数**形态——三表 Compose 通道，卡 C3）
    /// <para>判据同双参重载；三表 Compose 产物为 <c>Func&lt;T1,T2,T3,TTarget&gt;</c>。</para>
    /// </summary>
    internal static Expression<Func<T1, T2, T3, TTarget>> Strip<T1, T2, T3, TTarget>(
        Expression<Func<T1, T2, T3, TTarget>> lambda)
        where T1 : class
        where T2 : class
        where T3 : class
        where TTarget : class
    {
        var stripped = StripLambda(lambda);
        return ReferenceEquals(stripped, lambda)
            ? lambda
            : (Expression<Func<T1, T2, T3, TTarget>>)stripped;
    }

    /// <summary>
    /// 元数无关剥除内核 —— 三个强类型重载的判据与遍历单点（消除按元数复制判据的漂移风险）。
    /// <para>只遍历 <see cref="LambdaExpression.Body"/>（参数表原样透传），故剥除判据与元数无关；
    /// 无匹配节点时**返回传入引用本身**（调用方据此零重建，与单参重载既有语义逐字一致）。</para>
    /// </summary>
    /// <param name="lambda">投影 lambda（任意元数）</param>
    /// <returns>剥除后的 lambda；无需剥除时为传入引用</returns>
    internal static LambdaExpression StripLambda(LambdaExpression lambda)
    {
        var visitor = new FreeSqlNullGuardStripVisitor();
        var newBody = visitor.Visit(lambda.Body);
        if (visitor.StrippedCount == 0)
            return lambda;
        return Expression.Lambda(lambda.Type, newBody!, lambda.Parameters);
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
