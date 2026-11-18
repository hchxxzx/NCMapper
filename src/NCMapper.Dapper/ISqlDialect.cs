namespace NCMapper.Dapper;

/// <summary>
/// SQL 方言适配接口 — 为 ToPagedList 提供 SQL 级分页能力
/// <para>不同数据库的分页语法差异由方言实现封装，DapperDtoSelector 通过方言检测或显式传入选择</para>
/// </summary>
public interface ISqlDialect
{
    /// <summary>
    /// 绑定变量前缀 — 分页占位符拼接用
    /// <para>默认 "@"；Oracle/达梦覆写为 ":"（ODP.NET/DM 绑定变量前缀）——调用方硬编码 "@"
    /// 会导致 Oracle/达梦分页 SQL 绑定失败（ORA-01036 类），占位符前缀必须由方言提供。</para>
    /// </summary>
    string ParameterPrefix => "@"; // 接口默认成员：默认 @；Oracle/达梦显式覆写为 :

    /// <summary>
    /// 生成 COUNT 查询 SQL — 包装原始 SQL 为子查询取 COUNT(*)
    /// <para>SQL Server/DB2 会剥离原始 SQL 末尾的 ORDER BY（子查询不允许）</para>
    /// </summary>
    /// <param name="originalSql">用户原始查询 SQL</param>
    /// <returns>COUNT 查询 SQL</returns>
    string CountSql(string originalSql);

    /// <summary>
    /// 生成分页查询 SQL — 在原始 SQL 后追加分页子句
    /// <para>SQL Server/DB2 无 ORDER BY 时自动插入 ORDER BY (SELECT NULL)</para>
    /// </summary>
    /// <param name="originalSql">用户原始查询 SQL</param>
    /// <param name="offsetParam">偏移量参数占位符名（如 "@__nc_offset"）</param>
    /// <param name="pageSizeParam">页大小参数占位符名（如 "@__nc_pageSize"）</param>
    /// <returns>分页查询 SQL</returns>
    string PageSql(string originalSql, string offsetParam, string pageSizeParam);
}
