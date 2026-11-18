using FreeSql.DataAnnotations;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Orm;

// ===== 多表演示用实体类型 =====

/// <summary>
/// Wiki 订单实体 — 多表投影演示。
/// </summary>
[Table(Name = "WikiOrder")]
public class WikiOrder
{
    /// <summary>
    /// 主键。
    /// </summary>
    [Column(IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 订单号。
    /// </summary>
    public string OrderNo { get; set; } = "";

    /// <summary>
    /// 客户外键。
    /// </summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// 金额。
    /// </summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// Wiki 客户实体 — 多表投影演示。
/// </summary>
[Table(Name = "WikiCustomer")]
public class WikiCustomer
{
    /// <summary>
    /// 主键。
    /// </summary>
    [Column(IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 客户名。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 城市。
    /// </summary>
    public string City { get; set; } = "";
}

/// <summary>
/// Wiki 订单详情实体 — 三表投影演示。
/// </summary>
[Table(Name = "WikiOrderDetail")]
public class WikiOrderDetail
{
    /// <summary>
    /// 主键。
    /// </summary>
    [Column(IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 订单外键。
    /// </summary>
    public int OrderId { get; set; }

    /// <summary>
    /// 商品名。
    /// </summary>
    public string Product { get; set; } = "";

    /// <summary>
    /// 数量。
    /// </summary>
    public int Quantity { get; set; }
}

/// <summary>
/// 双表 Compose DTO — 映射自 WikiOrder + WikiCustomer。
/// </summary>
[MapFrom(typeof(WikiOrder))]
public class WikiOrderCustomerDto
{
    /// <summary>
    /// 订单 ID。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 订单号。
    /// </summary>
    public string OrderNo { get; set; } = "";

    /// <summary>
    /// 金额。
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 客户名称 — 跨表映射自 WikiCustomer.Name。
    /// </summary>
    [MapProperty(typeof(WikiCustomer), nameof(WikiCustomer.Name))]
    public string? CustomerName { get; set; }

    /// <summary>
    /// 客户城市 — 跨表映射自 WikiCustomer.City。
    /// </summary>
    [MapProperty(typeof(WikiCustomer), nameof(WikiCustomer.City))]
    public string? City { get; set; }
}

/// <summary>
/// 三表 Compose DTO — 映射自 WikiOrder + WikiCustomer + WikiOrderDetail。
/// </summary>
[MapFrom(typeof(WikiOrder))]
public class WikiOrderFullDto
{
    /// <summary>
    /// 订单 ID。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 订单号。
    /// </summary>
    public string OrderNo { get; set; } = "";

    /// <summary>
    /// 客户名称 — 映射自 WikiCustomer.Name。
    /// </summary>
    [MapProperty(typeof(WikiCustomer), nameof(WikiCustomer.Name))]
    public string? CustomerName { get; set; }

    /// <summary>
    /// 商品名 — 映射自 WikiOrderDetail.Product。
    /// </summary>
    [MapProperty(typeof(WikiOrderDetail), nameof(WikiOrderDetail.Product))]
    public string? Product { get; set; }

    /// <summary>
    /// 数量 — 映射自 WikiOrderDetail.Quantity。
    /// </summary>
    [MapProperty(typeof(WikiOrderDetail), nameof(WikiOrderDetail.Quantity))]
    public int Quantity { get; set; }
}

/// <summary>
/// Wiki 分类实体 — Include 导航演示。
/// </summary>
[Table(Name = "WikiCategory")]
public class WikiCategory
{
    /// <summary>
    /// 主键。
    /// </summary>
    [Column(IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 分类名。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 父分类外键。
    /// </summary>
    public int? ParentId { get; set; }

    /// <summary>
    /// 父分类导航。
    /// </summary>
    [Navigate(nameof(ParentId))]
    public WikiCategory? Parent { get; set; }
}

/// <summary>
/// 分类 DTO — 含导航路径 Parent.Name。
/// </summary>
[MapFrom(typeof(WikiCategory))]
public class WikiCategoryDto
{
    /// <summary>
    /// 分类 ID。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 分类名。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 父分类名 — PascalCase 展平：ParentName → Parent.Name。
    /// </summary>
    public string? ParentName { get; set; }
}

// ===== IncludeMany / ToTreeList / 运行时回退演示用实体 =====

/// <summary>
/// 自引用树形实体 — ToTreeList 演示。
/// </summary>
[Table(Name = "WikiTreeNode")]
public class WikiTreeNode
{
    /// <summary>
    /// 节点 ID。
    /// </summary>
    [Column(IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 父节点 ID。
    /// </summary>
    public int? ParentId { get; set; }

    /// <summary>
    /// 节点名。
    /// </summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// 树形 DTO — 含 Children 集合。
/// </summary>
[MapFrom(typeof(WikiTreeNode))]
public class WikiTreeNodeDto
{
    /// <summary>
    /// 节点 ID。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 父节点 ID。
    /// </summary>
    public int? ParentId { get; set; }

    /// <summary>
    /// 节点名。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 子节点集合 — [MapIgnore(Direction.Both)]，由 ToTreeList 自动填充，不参与映射。
    /// </summary>
    [MapIgnore(Direction.Both)]
    public List<WikiTreeNodeDto> Children { get; set; } = new();
}

/// <summary>
/// 分组实体 — IncludeMany 一对多演示。
/// </summary>
[Table(Name = "WikiGroup")]
public class WikiGroup
{
    /// <summary>
    /// 主键。
    /// </summary>
    [Column(IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 组名。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 成员列表（一对多导航）。
    /// </summary>
    [Navigate(nameof(WikiMember.GroupId))]
    public List<WikiMember> Members { get; set; } = new();
}

/// <summary>
/// 成员实体。
/// </summary>
[Table(Name = "WikiMember")]
public class WikiMember
{
    /// <summary>
    /// 主键。
    /// </summary>
    [Column(IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 组外键。
    /// </summary>
    public int GroupId { get; set; }

    /// <summary>
    /// 成员名。
    /// </summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// 成员 DTO。
/// </summary>
[MapFrom(typeof(WikiMember))]
public class WikiMemberDto
{
    /// <summary>
    /// 成员 ID。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 成员名。
    /// </summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// 运行时回退 DTO — 无 [MapFrom]，强制运行时表达式树路径。
/// </summary>
public class BenchRuntimeDto
{
    /// <summary>
    /// ID。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名称。
    /// </summary>
    public string Name { get; set; } = "";
}
