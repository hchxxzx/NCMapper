using NCMapper.Attributes;

namespace NCMapper.Samples.Declarative;

// ================================================================
// 索引路径映射与反向写回路径演示模型
// F5：集合索引路径 A.B[0].C（运行时）
// F4：反向写回点路径 A.B.C（DTO 扁平属性 → 实体嵌套路径）
// ================================================================

/// <summary>
/// 索引订单实体 — 含 Items 集合导航，演示集合索引路径（Items[0].Name）。
/// </summary>
public class IndexOrderEntity
{
    /// <summary>
    /// 订单标识。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 明细集合 — 索引路径的集合段载体。
    /// </summary>
    public List<IndexOrderItem> Items { get; set; } = new();
}

/// <summary>
/// 索引订单明细 — 集合元素，索引路径的叶段来源。
/// </summary>
public class IndexOrderItem
{
    /// <summary>
    /// 明细名称。
    /// </summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// 索引订单 DTO — 演示 [MapProperty(typeof(T), "Items[0].Name")] 集合索引路径映射。
/// </summary>
[MapFrom(typeof(IndexOrderEntity))]
public class IndexOrderDto
{
    /// <summary>
    /// 订单标识 — 同名自动匹配。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 首项名称 — 索引路径 Items[0].Name（集合第 0 个元素）。
    /// </summary>
    [MapProperty(typeof(IndexOrderEntity), "Items[0].Name")]
    public string? FirstItemName { get; set; }

    /// <summary>
    /// 次项名称 — 索引路径 Items[1].Name（集合越界时返回 default）。
    /// </summary>
    [MapProperty(typeof(IndexOrderEntity), "Items[1].Name")]
    public string? SecondItemName { get; set; }
}

/// <summary>
/// 文章实体 — 含 Author 嵌套导航，演示反向写回点路径（Author.Name）。
/// </summary>
public class ArticleEntity
{
    /// <summary>
    /// 文章标识。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 文章标题。
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// 作者导航 — 反向写回的目标嵌套路径载体。
    /// </summary>
    public AuthorEntity? Author { get; set; }
}

/// <summary>
/// 作者实体 — 嵌套导航的叶对象。
/// <para>Name 声明为可空：反向写回清空叶语义（D5）要求源值 null 时赋 null。</para>
/// </summary>
public class AuthorEntity
{
    /// <summary>
    /// 作者姓名。
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>
/// 文章 DTO — 演示 [MapProperty(typeof(T), "Author.Name")] 反向写回点路径。
/// </summary>
[MapFrom(typeof(ArticleEntity))]
public class ArticleDto
{
    /// <summary>
    /// 文章标识 — 同名自动匹配。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 文章标题 — 同名自动匹配。
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// 作者姓名 — 扁平属性，反向写回实体嵌套路径 Author.Name。
    /// </summary>
    [MapProperty(typeof(ArticleEntity), "Author.Name")]
    public string? AuthorName { get; set; }
}
