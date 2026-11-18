// FreeSql 通道 SQL 投影分支兜底 guard
// 设计：兜底不上移共享内核（AsDtoExecutor 纯骨架零改动，C1）、
// 不进 Build 本体（chunk 内存路径共用），收拢在终端方法 SQL 投影分支：
//   guard.Run(label, sqlProject: Build → StripVisitor → SQL 执行, degrade: 自动 Include + 查实体 + MapList)
// catch 范围 = 整个 sqlProject（覆盖最初上报的痛点「翻译期炸」——null.Id 在 FreeSql 翻译时抛，非构建时）；
// catch 行为 = Trace 完整原始异常（类型 + 消息，诊断不丢失，C4）→ 调 degrade；
// degrade 再炸则异常链自然上抛（不静默吞真故障：连接/数据错误在降级路径同样失败且带上下文）。
// 接入面 = 终端方法 SQL 分支约 8 处；chunk 两处与 Compose 通道零改动。
using System;
using NCMapper;

namespace NCMapper.FreeSql.Internal;

/// <summary>
/// FreeSql 通道 SQL 投影分支兜底执行器
/// <para>SQL 投影（含嵌套投影改写器）失败时降级内存映射并输出含原始异常的 Trace，
/// 防用户裸炸（最初上报的痛点：FreeSql 内部异常「null.Id cannot be null」不可感知根因）。</para>
/// </summary>
internal static class FreeSqlSqlProjectionGuard
{
    /// <summary>
    /// 执行 SQL 投影分支；异常时输出降级 Trace（含完整原始异常）并转降级路径
    /// </summary>
    /// <param name="label">Trace 标签（含终端方法名，如 "[FreeSql AsDto] ToList"）</param>
    /// <param name="sqlProject">SQL 投影委托（Build → 剥保护 → SQL 翻译执行）</param>
    /// <param name="degrade">降级委托（自动 Include + 查实体 + 内存映射，复用各终端方法既有降级逻辑）</param>
    /// <returns>SQL 投影或降级路径的结果</returns>
    internal static TResult Run<TResult>(string label, Func<TResult> sqlProject, Func<TResult> degrade)
    {
        try
        {
            return sqlProject();
        }
        catch (Exception ex)
        {
            MapTracer.Write(
                $"[FreeSql AsDto] {label} SQL 投影失败（含嵌套投影 IIF 剥除改写），降级内存映射。" +
                $"原始异常: {ex.GetType().FullName}: {ex.Message}");
            return degrade();
        }
    }
}
