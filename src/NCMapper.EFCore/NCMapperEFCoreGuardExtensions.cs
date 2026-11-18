// NCMapper EF Core 扩展包 — 编译期守护（Guard）
// 为不兼容的 AsDto 用法提供 [Obsolete(error:true)] 编译期错误引导
// 覆盖：IQueryable<IGrouping<>> + IEnumerable<T>
#pragma warning disable CS1591 // 缺少 XML 注释（Guard 方法错误消息即文档）

using Microsoft.EntityFrameworkCore;

namespace NCMapper.EFCore;

/// <summary>
/// NCMapper EF Core 编译期守护 — 对不兼容 AsDto 的查询链提供编译期错误提示
/// <para>所有方法均标记 [Obsolete(error: true)]，编译即报错，附带替代方案说明</para>
/// </summary>
public static class NCMapperEFCoreGuardExtensions
{
    /// <summary>
    /// [编译错误] GroupBy 后不可使用 AsDto
    /// <para>GroupBy 返回 IQueryable&lt;IGrouping&lt;TKey, T&gt;&gt;，不是 IQueryable&lt;T&gt;</para>
    /// <para>分组聚合结果非单条实体 → 无法做实体→DTO 属性映射</para>
    /// </summary>
    [Obsolete(
        "EF Core GroupBy 后不可使用 AsDto。GroupBy 返回分组聚合结果（IGrouping），不是单条实体，" +
        "无法做实体→DTO 属性映射。" +
        "替代写法：1) 先 .AsDto().ToList() 再在内存中 GroupBy；2) 使用 EF Core 原生 Select 聚合语法。" +
        "参见：NCMapper 技术文档 §11.3",
        error: true)]
    public static void AsDto<TKey, TSource>(this IQueryable<IGrouping<TKey, TSource>> query)
        where TSource : class
        => throw new NotSupportedException(ErrorMessages.GroupByNotSupported("AsDto"));

    /// <summary>
    /// [编译错误] 已脱离 IQueryable 进入 IEnumerable 域后不可使用 AsDto
    /// <para>AsEnumerable/AsAsyncEnumerable 后数据已拉入内存，应用 Mapper.MapList 替代</para>
    /// </summary>
    [Obsolete(
        "已脱离 IQueryable 域（如 .AsEnumerable() / .ToList() 后），不可再使用 AsDto。" +
        "替代写法：直接使用 Mapper.MapList<TSource, TTarget>(items) 进行内存映射。" +
        "参见：NCMapper 技术文档 §11.3",
        error: true)]
    public static void AsDto<TSource>(this IEnumerable<TSource> source)
        where TSource : class
        => throw new NotSupportedException(ErrorMessages.IEnumerableDomainReached("Mapper.MapList"));
}
