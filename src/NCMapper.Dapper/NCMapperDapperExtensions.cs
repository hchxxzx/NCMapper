// NCMapper Dapper 扩展包 — AsDto 统一入口
// 用法：connection.AsDto<User>().Query<UserDto>(sql, param)
// 依赖：NCMapper + Dapper

using System.Data;
using System.Diagnostics.CodeAnalysis;

namespace NCMapper.Dapper;

/// <summary>
/// NCMapper Dapper 扩展方法 — AsDto&lt;TSource&gt; 是唯一的公开入口
/// <para>用法：connection.AsDto&lt;User&gt;().Query&lt;UserDto&gt;(sql, param)</para>
/// <para>所有终端方法（Query/ToPagedList/ToDictionary/ToTreeList 等）均通过 DapperDtoSelector&lt;TSource&gt; 调用</para>
/// <para>Mask/Convert/Pipeline 在所有路径永远生效（运行时 + 源生成双路径）</para>
/// </summary>
public static class NCMapperDapperExtensions
{
    /// <summary>
    /// 返回 DTO 选择器 — AsDto 是终端操作的入口
    /// <para>用法：connection.AsDto&lt;User&gt;().Query&lt;UserDto&gt;(sql)</para>
    /// <para>方言自动检测：从 IDbConnection 类型名推断（SqlConnection→SqlServer 等），无法识别时回退 MySQL</para>
    /// </summary>
    /// <typeparam name="TSource">源实体类型</typeparam>
    /// <param name="connection">数据库连接</param>
    /// <param name="dialect">可选：显式指定 SQL 方言（覆盖自动检测）</param>
    /// <returns>DapperDtoSelector&lt;TSource&gt; 实例，用于后续调用查询/终端方法</returns>
    [RequiresDynamicCode("AsDto uses Expression.Compile() for the mapping delegate. Not supported in AOT. [MapFrom] makes mapping AOT-safe but delegate compilation still requires dynamic code.")]
    [RequiresUnreferencedCode("AsDto scans attributes via reflection.")]
    public static DapperDtoSelector<TSource> AsDto<TSource>(this IDbConnection connection, ISqlDialect? dialect = null)
        where TSource : class
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new DapperDtoSelector<TSource>(connection, dialect ?? SqlDialectResolver.Resolve(connection));
    }
}
