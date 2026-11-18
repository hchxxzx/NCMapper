using FreeSql.DataAnnotations;
using NCMapper.Attributes;

namespace NCMapper.Samples.Orm;

// ================================================================
// 场景 503 模型 — 目录/商品两表演示数据（多表 JOIN + 一对多导航）
// 来源任务：FreeSql 内存形态下验证
// 两表关联投影 + IncludeMany 导航集合投影 + 异步分批 + 分组。
// ================================================================

/// <summary>目录实体（一对多主表）。</summary>
public class CatalogEntity
{
    /// <summary>目录主键。</summary>
    public int Id { get; set; }

    /// <summary>目录名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>商品集合（IncludeMany 导航加载；Navigate 指向子表外键 CatalogId）。</summary>
    [Navigate(nameof(CatalogProductEntity.CatalogId))]
    public List<CatalogProductEntity> Products { get; set; } = new();
}

/// <summary>商品实体（多表子表）。</summary>
public class CatalogProductEntity
{
    /// <summary>商品主键。</summary>
    public int Id { get; set; }

    /// <summary>所属目录（JOIN 关联键）。</summary>
    public int CatalogId { get; set; }

    /// <summary>商品标题。</summary>
    public string Title { get; set; } = "";

    /// <summary>价格。</summary>
    public decimal Price { get; set; }
}

/// <summary>
/// 目录 DTO（特性通道）——含一对多导航集合，IncludeMany 后 Products 逐元素映射。
/// </summary>
[MapFrom(typeof(CatalogEntity))]
public class CatalogDto
{
    /// <summary>目录主键。</summary>
    public int Id { get; set; }

    /// <summary>目录名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>商品集合（元素按名映射 CatalogProductEntity → CatalogProductDto）。</summary>
    public List<CatalogProductDto> Products { get; set; } = new();
}

/// <summary>商品 DTO（特性通道）。</summary>
[MapFrom(typeof(CatalogProductEntity))]
public class CatalogProductDto
{
    /// <summary>商品主键。</summary>
    public int Id { get; set; }

    /// <summary>商品标题。</summary>
    public string Title { get; set; } = "";

    /// <summary>价格。</summary>
    public decimal Price { get; set; }
}

/// <summary>
/// 双表 JOIN 投影 DTO：商品字段 + 跨表目录名（MapProperty 指向第二源 CatalogEntity）。
/// </summary>
[MapFrom(typeof(CatalogProductEntity))]
public class ProductCatalogDto
{
    /// <summary>商品主键。</summary>
    public int Id { get; set; }

    /// <summary>商品标题。</summary>
    public string Title { get; set; } = "";

    /// <summary>价格。</summary>
    public decimal Price { get; set; }

    /// <summary>目录名称（跨表字段：取数来源为第二源 CatalogEntity）。</summary>
    [MapProperty(typeof(CatalogEntity), nameof(CatalogEntity.Name))]
    public string CatalogName { get; set; } = "";
}

/// <summary>目录 DTO（Fluent 通道形态——DTO 无标注，声明在 CatalogFluentMapper）。</summary>
public class CatalogFluentDto
{
    /// <summary>目录主键。</summary>
    public int Id { get; set; }

    /// <summary>目录名称。</summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// Fluent 配置类：CatalogEntity → CatalogFluentDto 同名自动匹配。
/// </summary>
[MapperConfig]
public class CatalogFluentMapper
{
    /// <summary>链式声明入口。</summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<CatalogEntity, CatalogFluentDto>();
}
