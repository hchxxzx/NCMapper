using System.Diagnostics.CodeAnalysis;

namespace NCMapper.Dapper;

/// <summary>
/// LIMIT/OFFSET 分页方言基类（H-05 收敛）— MySQL 系 / PostgreSQL 系 / SQLite 共用模板。
/// <para>语义：CountSql 直接包裹子查询（LIMIT 方言无 ORDER BY 剥离需求），PageSql 追加 LIMIT/OFFSET。</para>
/// </summary>
public abstract class LimitOffsetDialect : ISqlDialect
{
    /// <inheritdoc />
    public string CountSql(string originalSql)
        => $"SELECT COUNT(*) FROM ({SqlServerDialect.TrimStatementTail(originalSql)}) AS _count_tbl";

    /// <inheritdoc />
    public string PageSql(string originalSql, string offsetParam, string pageSizeParam)
        => $"{SqlServerDialect.TrimStatementTail(originalSql)} LIMIT {pageSizeParam} OFFSET {offsetParam}";
}

/// <summary>
/// SQL Server 方言 — OFFSET/FETCH 分页，CountSql 剥离 ORDER BY
/// <para>兼容：达梦 DM（Oracle 语法更近，但 OFFSET/FETCH 12c+ 通用）</para>
/// </summary>
public sealed class SqlServerDialect : ISqlDialect
{
    /// <inheritdoc />
    public string CountSql(string originalSql)
        => $"SELECT COUNT(*) FROM ({StripOrderBy(TrimStatementTail(originalSql))}) AS _count_tbl";

    /// <inheritdoc />
    public string PageSql(string originalSql, string offsetParam, string pageSizeParam)
    {
        // 先消毒再判 ORDER BY 再拼接
        // 若先补 "ORDER BY (SELECT NULL)" 再消毒，尾分号会被夹在语句中间剥不到
        var trimmed = TrimStatementTail(originalSql);
        var sql = HasOrderBy(trimmed) ? trimmed : $"{trimmed} ORDER BY (SELECT NULL)";
        return $"{sql} OFFSET {offsetParam} ROWS FETCH NEXT {pageSizeParam} ROWS ONLY";
    }

    /// <summary>
    /// 剥离 SQL 尾部的 ORDER BY 子句，供 CountSql 包裹为子查询
    /// </summary>
    /// <param name="sql">原始 SQL 语句</param>
    /// <returns>去除 ORDER BY 后的 SQL；无 ORDER BY 时原样返回</returns>
    internal static string StripOrderBy(string sql)
    {
        var idx = FindLastOrderBy(sql);
        return idx >= 0 ? sql[..idx].TrimEnd() : sql;
    }

    /// <summary>
    /// 判断 SQL 是否包含 ORDER BY 子句
    /// </summary>
    /// <param name="sql">原始 SQL 语句</param>
    /// <returns>包含 ORDER BY 时返回 true，否则 false</returns>
    internal static bool HasOrderBy(string sql) => FindLastOrderBy(sql) >= 0;

    /// <summary>
    /// 从后向前查找最后一个不在括号/字面量/注释内的 ORDER BY 起始位置
    /// </summary>
    /// <param name="sql">原始 SQL 语句</param>
    /// <returns>ORDER BY 起始索引；未找到返回 -1</returns>
    internal static int FindLastOrderBy(string sql)
    {
        // 正向一遍扫描，取 depth==0 文本中最后一个 ORDER BY 的起始位置。
        // 单引号字面量状态机（含 SQL 转义 ''）
        // 字面量内的括号与 "ORDER BY" 文本不参与匹配（原实现下 WHERE Name='(ORDER BY)' 会被误判）。
        // 行注释（--）、块注释、双引号标识符、
        // 中括号标识符（[...]）、backtick 标识符内的括号与 "ORDER BY" 文本同规则跳过。
        // 注：反向扫描存在结构性缺陷——注释/标识符"内容"在遇到边界标记之前已被当作正常文本扫过，
        // （如 "... ORDER BY Id -- ORDER BY fake" 中注释内的 ORDER BY 会先被命中），故改正向一遍。
        var upper = sql.ToUpperInvariant();
        var depth = 0;
        var last = -1;
        var i = 0;
        while (i < sql.Length)
        {
            var c = upper[i];

            // 行注释：-- 到行尾
            if (c == '-' && i + 1 < sql.Length && upper[i + 1] == '-')
            { i += 2; while (i < sql.Length && upper[i] != '\n') i++; continue; }

            // 块注释：/* 到 */（未闭合则吞到语句尾）
            if (c == '/' && i + 1 < sql.Length && upper[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(upper[i] == '*' && upper[i + 1] == '/')) i++;
                i = Math.Min(i + 2, sql.Length);
                continue;
            }

            // 单引号字面量（含 '' 转义对）
            if (c == '\'')
            {
                i++;
                while (i < sql.Length)
                {
                    if (upper[i] == '\'')
                    {
                        if (i + 1 < sql.Length && upper[i + 1] == '\'') { i += 2; continue; }
                        i++; break;
                    }
                    i++;
                }
                continue;
            }

            // 双引号标识符 / backtick 标识符 / 中括号标识符（[...]）
            if (c == '"') { i++; while (i < sql.Length && upper[i] != '"') i++; i++; continue; }
            if (c == '`') { i++; while (i < sql.Length && upper[i] != '`') i++; i++; continue; }
            if (c == '[') { i++; while (i < sql.Length && upper[i] != ']') i++; i++; continue; }

            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && c == 'O' && i + 8 <= sql.Length
                     && upper.Substring(i, 8) == "ORDER BY"
                     && (i + 8 == sql.Length || !char.IsLetterOrDigit(upper[i + 8])))
            { last = i; i += 8; continue; }
            i++;
        }
        return last;
    }

    /// <summary>
    /// 语句尾消毒：剥离尾部的空白、分号、尾行注释与完整块注释序列；
    /// M-01 补尾行注释分支）
    /// </summary>
    /// <remarks>
    /// 场景：用户 SQL 常以 ";" 收尾，CountSql 包裹成子查询 / PageSql 直拼 OFFSET/LIMIT 时
    /// 残留分号产生非法 SQL；尾行注释（"-- ..." 到语句尾）同理——`SELECT ... -- x` 收尾时
    /// CountSql 产出 `... (-- x) AS _count_tbl` 括号被注释吞掉必语法错，SqlServerDialect.PageSql
    /// 的 OFFSET 强制 ORDER BY 同被吞。仅处理语句尾部（空白、分号、尾行注释、完整闭合的块注释），
    /// 不改动语句主体。
    /// </remarks>
    internal static string TrimStatementTail(string sql)
    {
        var s = sql;
        while (true)
        {
            s = s.TrimEnd();
            // 尾行注释剥离：置于分号判定之前（注释内的 ";" 属注释内容，非语句终止符）
            var tailComment = FindTailLineCommentStart(s);
            if (tailComment >= 0) { s = s[..tailComment]; continue; }
            if (s.EndsWith(";")) { s = s[..^1]; continue; }
            if (s.EndsWith("*/"))
            {
                var open = s.LastIndexOf("/*", StringComparison.Ordinal);
                if (open >= 0 && s.IndexOf("*/", open + 2, StringComparison.Ordinal) == s.Length - 2)
                { s = s[..open]; continue; } // 尾部为完整闭合块注释才剥离（未闭合不动，防误切）
            }
            return s;
        }
    }

    /// <summary>
    /// 查找延伸至语句尾的行注释（"--"）起始位置 — 与 <see cref="FindLastOrderBy"/> 行注释
    /// 扫描同规则：单引号字面量（含 '' 转义）、块注释、双引号 / backtick / 中括号标识符内的 "--"
    /// 属内容不参与匹配。
    /// </summary>
    /// <param name="sql">语句（调用方已 TrimEnd）</param>
    /// <returns>尾行注释起始索引；无则 -1</returns>
    private static int FindTailLineCommentStart(string sql)
    {
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];

            // 行注释：-- 到行尾；其后无换行 → 注释延伸至语句尾
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var nl = sql.IndexOf('\n', i);
                if (nl < 0) return i;
                i = nl + 1;
                continue;
            }

            // 块注释：/* 到 */（未闭合则吞到语句尾，与 FindLastOrderBy 同处理）
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                i = Math.Min(i + 2, sql.Length);
                continue;
            }

            // 单引号字面量（含 '' 转义对）
            if (c == '\'')
            {
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'') { i += 2; continue; }
                        i++; break;
                    }
                    i++;
                }
                continue;
            }

            // 双引号标识符 / backtick 标识符 / 中括号标识符
            if (c == '"') { i++; while (i < sql.Length && sql[i] != '"') i++; i++; continue; }
            if (c == '`') { i++; while (i < sql.Length && sql[i] != '`') i++; i++; continue; }
            if (c == '[') { i++; while (i < sql.Length && sql[i] != ']') i++; i++; continue; }

            i++;
        }
        return -1;
    }
}

/// <summary>
/// MySQL 方言 — LIMIT/OFFSET 分页
/// <para>兼容：MariaDB、OceanBase、TiDB、PolarDB(MySQL)</para>
/// </summary>
public sealed class MySqlDialect : LimitOffsetDialect
{
    // LIMIT/OFFSET 模板继承自 LimitOffsetDialect（H-05 收敛）
}

/// <summary>
/// PostgreSQL 方言 — LIMIT/OFFSET 分页
/// <para>兼容：人大金仓 KingbaseES、openGauss、GaussDB、PolarDB(PG)</para>
/// </summary>
public sealed class PostgreSqlDialect : LimitOffsetDialect
{
    // LIMIT/OFFSET 模板继承自 LimitOffsetDialect（H-05 收敛）
}

/// <summary>
/// SQLite 方言 — LIMIT/OFFSET 分页
/// </summary>
public sealed class SqliteDialect : LimitOffsetDialect
{
    // LIMIT/OFFSET 模板继承自 LimitOffsetDialect（H-05 收敛）
}

/// <summary>
/// Oracle 方言 — OFFSET/FETCH 分页（12c+），CountSql 允许子查询含 ORDER BY
/// <para>兼容：达梦 DM</para>
/// </summary>
public sealed class OracleDialect : ISqlDialect
{
    /// <summary>
    /// ODP.NET/达梦绑定变量前缀为冒号
    /// </summary>
    public string ParameterPrefix => ":";

    /// <inheritdoc />
    public string CountSql(string originalSql)
        => $"SELECT COUNT(*) FROM ({SqlServerDialect.TrimStatementTail(originalSql)}) cnt";

    /// <inheritdoc />
    public string PageSql(string originalSql, string offsetParam, string pageSizeParam)
        => $"{SqlServerDialect.TrimStatementTail(originalSql)} OFFSET {offsetParam} ROWS FETCH NEXT {pageSizeParam} ROWS ONLY";
}

/// <summary>
/// DB2 方言 — OFFSET/FETCH FIRST 分页（10.5+），CountSql 剥离 ORDER BY
/// </summary>
public sealed class Db2Dialect : ISqlDialect
{
    /// <inheritdoc />
    public string CountSql(string originalSql)
        => $"SELECT COUNT(*) FROM ({SqlServerDialect.StripOrderBy(SqlServerDialect.TrimStatementTail(originalSql))}) AS _count_tbl";

    /// <inheritdoc />
    public string PageSql(string originalSql, string offsetParam, string pageSizeParam)
    {
        // 与 SqlServerDialect.PageSql 同次序——先消毒再判 ORDER BY 再拼接。
        // 原实现先补 "ORDER BY (SELECT NULL)" 后消毒，尾分号被夹在语句中段（ORDER BY 之前）剥不到。
        var trimmed = SqlServerDialect.TrimStatementTail(originalSql);
        var sql = SqlServerDialect.HasOrderBy(trimmed) ? trimmed : $"{trimmed} ORDER BY (SELECT NULL)";
        return $"{sql} OFFSET {offsetParam} ROWS FETCH FIRST {pageSizeParam} ROWS ONLY";
    }
}

/// <summary>
/// SQL 方言工厂 — 从 IDbConnection 类型名推断默认方言
/// </summary>
public static class SqlDialectResolver
{
    /// <summary>
    /// 根据连接类型名推断方言，无法识别时回退 MySQL（LIMIT/OFFSET 最通用）
    /// <para>设计说明（备案保留）：类型名字符串 switch 为<b>有意设计</b>
    /// 避免对 ADO provider 程序集（SqlClient/Npgsql/MySqlConnector 等）的编译期硬依赖；
    /// 新增方言仅需扩展 switch 分支，不引入包引用。常量化为纯修饰性改动，收益低，故保留。</para>
    /// <para>入口增加解包探测——生产环境连接常被包装
    /// （MiniProfiler ProfiledDbConnection 等暴露 InnerConnection、Polly/代理包装），类型名 switch 对
    /// 包装连接必然失配并静默回退 MySQL；解包探测与类型名 switch 备案兼容（switch 本身不动）。</para>
    /// </summary>
    /// <param name="connection">Dapper 数据库连接实例</param>
    /// <returns>对应的 SQL 方言实现（无法识别时回退 MySQL）</returns>
    [RequiresUnreferencedCode("SqlDialectResolver.Resolve unwraps wrapper connections via reflection (GetProperty/GetValue). Wrapped connection types may be trimmed.")]
    public static ISqlDialect Resolve(System.Data.IDbConnection connection)
    {
        var current = connection;
        var unwrapped = TryUnwrap(connection);
        if (unwrapped != null) current = unwrapped;

        var typeName = current.GetType().Name;
        ISqlDialect dialect = typeName switch
        {
            "SqlConnection" => new SqlServerDialect(),
            "MySqlConnection" => new MySqlDialect(),
            "NpgsqlConnection" => new PostgreSqlDialect(),
            "SqliteConnection" or "SQLiteConnection" => new SqliteDialect(),
            "OracleConnection" => new OracleDialect(),
            "DB2Connection" => new Db2Dialect(),
            _ => new MySqlDialect() // 回退：LIMIT/OFFSET 语法最通用
        };

        // /：默认回退路径（非 MySQL 系）输出一次性警告——包装连接失配是生产环境常见根因，
        // 静默回退对 SQL Server/Oracle/DB2 会发出 LIMIT/OFFSET 非法 SQL（运行时报错）。
        // 两分支并存——未包装未知连接（ReferenceEquals 恒等分支，
        // 原实现恒不告警）与包装后仍未知连接（原已覆盖面）均告警；保留一次性告警防刷屏语义。
        // 防刷屏去重键取外层连接类型名（用户传入对象）：若按解包后内层名去重，包装连接会与
        // "直接传入该内层类型"的其他调用方共用去重额度，造成该告警被他人吞掉。
        var outerTypeName = connection.GetType().Name;
        if (dialect is MySqlDialect
            && !s_warnedTypeNames.ContainsKey(outerTypeName)
            && !IsMySqlTypeName(typeName))
        {
            s_warnedTypeNames.TryAdd(outerTypeName, 0);
            var wrapped = !ReferenceEquals(current, connection);
            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper.Dapper] SqlDialectResolver: 连接类型 {connection.GetType().Name} 未识别"
                + (wrapped ? "，解包后内层类型仍未识别" : "（非包装或无法解包）")
                + $"，默认回退 MySqlDialect（LIMIT/OFFSET）。若目标库为 SQL Server/Oracle/DB2 请显式指定方言："
                + $"AsDto(connection, new XxxDialect())。内层连接类型: {current.GetType().Name}");
        }

        return dialect;
    }

    /// <summary>
    /// 解包探测：按常见包装连接属性名（InnerConnection/WrappedConnection）反射获取内层连接，
    /// 递归最多 3 层；属性查找结果按类型缓存避免重复反射。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo?> s_unwrapPropertyCache = new();

    /// <summary>
    /// 已告警的连接类型集合（避免重复 Trace）
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> s_warnedTypeNames = new();

    /// <summary>
    /// 常见包装连接属性名清单
    /// </summary>
    private static readonly string[] s_unwrapPropertyNames = { "InnerConnection", "WrappedConnection" };

    [RequiresUnreferencedCode("TryUnwrap reflects over connection types (GetProperty). Wrapped connection types may be trimmed.")]
    private static System.Data.IDbConnection? TryUnwrap(System.Data.IDbConnection connection, int depth = 0)
    {
        if (depth >= 3) return null;
        var prop = s_unwrapPropertyCache.GetOrAdd(connection.GetType(), t =>
        {
            foreach (var name in s_unwrapPropertyNames)
            {
                var p = t.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (p != null && typeof(System.Data.IDbConnection).IsAssignableFrom(p.PropertyType))
                    return p;
            }
            return null;
        });
        if (prop == null) return null;
        if (prop.GetValue(connection) is not System.Data.IDbConnection inner) return null;
        if (inner.GetType() == connection.GetType()) return null; // 自引用防护
        var deeper = TryUnwrap(inner, depth + 1);
        return deeper ?? inner;
    }

    private static bool IsMySqlTypeName(string typeName)
        => typeName == "MySqlConnection" || typeName.Contains("MySql");
}
