using NCMapper.Attributes;

namespace NCMapper.Samples.Orm;

// ================================================================
// 集合索引路径投影演示模型
// Blog 1→N Post 集合导航，演示 EFCore 投影对 Posts[0].Title 的翻译
// ================================================================

/// <summary>
/// 博客实体 — 含 Posts 集合导航，演示集合索引路径投影（Posts[0].Title）。
/// </summary>
public class Blog
{
    /// <summary>
    /// 博客标识。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 博客标题。
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// 文章集合 — 索引路径的集合段载体。
    /// </summary>
    public List<Post> Posts { get; set; } = new();
}

/// <summary>
/// 文章实体 — 集合元素，索引路径的叶段来源。
/// </summary>
public class Post
{
    /// <summary>
    /// 文章标识。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 所属博客外键。
    /// </summary>
    public int BlogId { get; set; }

    /// <summary>
    /// 文章标题。
    /// </summary>
    public string Title { get; set; } = "";
}

/// <summary>
/// 博客 DTO — 演示 [MapProperty(typeof(Blog), "Posts[0].Title")] EFCore 投影（LIMIT 1 OFFSET 0 子查询，无序取一）。
/// </summary>
[MapFrom(typeof(Blog))]
public class BlogDto
{
    /// <summary>
    /// 博客标识 — 同名自动匹配。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 博客标题 — 同名自动匹配。
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// 首篇文章标题 — 索引路径 Posts[0].Title（SQL 侧无序取一，空集合 → NULL）。
    /// </summary>
    [MapProperty(typeof(Blog), "Posts[0].Title")]
    public string? FirstPostTitle { get; set; }
}
