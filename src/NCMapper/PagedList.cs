namespace NCMapper;

/// <summary>
/// 分页查询结果 — 三包（EFCore/FreeSql/Dapper）共用的返回类型
/// </summary>
/// <typeparam name="T">列表元素类型</typeparam>
public sealed class PagedList<T>
{
    /// <summary>
    /// 当前页数据列表
    /// </summary>
    public List<T> Items { get; init; } = new();

    /// <summary>
    /// 总记录数
    /// </summary>
    public long TotalCount { get; init; }

    /// <summary>
    /// 当前页码（从 1 开始）
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// 每页条数
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// 总页数
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    /// <summary>
    /// 是否有下一页
    /// </summary>
    public bool HasNext => Page < TotalPages;

    /// <summary>
    /// 是否有上一页
    /// </summary>
    public bool HasPrev => Page > 1;
}
