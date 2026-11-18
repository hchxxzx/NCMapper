// NCMapper FreeSql 扩展包 — 编译期守护（Guard）
// 为不兼容的 AsDto 用法提供 [Obsolete(error:true)] 编译期错误引导
// 覆盖：GroupBy 返回的 ISelectGrouping<TKey, TValue> 类型
// 注意：ISelectGrouping 仅 2 类型参数，单表/多表 GroupBy 均返回此类型
#pragma warning disable CS1591 // 缺少 XML 注释（Guard 方法错误消息即文档）

using FreeSql;

namespace NCMapper.FreeSql;

/// <summary>
/// NCMapper FreeSql 编译期守护 — 对不兼容 AsDto 的 FreeSql 查询链提供编译期错误提示
/// <para>所有方法均标记 [Obsolete(error: true)]，编译即报错，附带替代方案说明</para>
/// </summary>
public static class NCMapperFreeSqlGuardExtensions
{
    /// <summary>
    /// [编译错误] AsDto 链路不支持 GroupBy
    /// <para>GroupBy 返回 ISelectGrouping&lt;TKey, TValue&gt;（分组聚合），不再是实体查询 → 无法做实体→DTO 属性映射</para>
    /// <para>替代方案：先 .AsDto().ToList() 将数据取到内存，再使用 LINQ 的 .GroupBy() 进行分组；或使用 FreeSql 原生聚合语法</para>
    /// </summary>
    [Obsolete(
        "AsDto 不支持链式 GroupBy。GroupBy 返回分组聚合结果（ISelectGrouping），无法做实体→DTO 映射。" +
        "替代写法：先 .AsDto().ToList() 将数据取到内存，再使用 LINQ 的 .GroupBy() 进行分组；或使用 FreeSql 原生 Select 聚合语法。" +
        "参见 NCMapper 技术文档 §11.2 了解 ORM 边界。",
        error: true)]
    public static void AsDto<TKey, TValue>(this ISelectGrouping<TKey, TValue> select)
        => throw new NotSupportedException(ErrorMessages.ChainedGroupByNotSupported("AsDto"));
}
