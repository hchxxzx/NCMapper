using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Samples.Orm;

// ================================================================
// Runtime×ORM 演示模型
// Fluent DTO + ORM AsDto 双态：
//   ① 纯可翻译声明（同名 Map/自动匹配）→ 投影路径可用（SQL 侧列裁剪）
//   ② 含 Mask 声明 → 一律强制降级（内存映射承载全语义，脱敏不落明文）
// 注意：本程序集存在 [assembly: MapDefaults(SnakeCase)] 全局配置——显式 Map 声明为符号级
// 路径不受名称策略影响；本演示采用同名形态（投影列裁剪场景）。
// ================================================================

/// <summary>百科条目实体（纯可翻译 Fluent DTO 演示源）。</summary>
public class FxWikiEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>标题（投影列裁剪演示）</summary>
    public string Title { get; set; } = "";

    /// <summary>正文（投影列裁剪演示）</summary>
    public string Body { get; set; } = "";
}

/// <summary>百科条目 DTO（同名自动匹配——纯可翻译声明，投影路径可用）。</summary>
public class FxWikiDto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>标题</summary>
    public string Title { get; set; } = "";
}

/// <summary>脱敏实体（Mask 强制降级演示源）。</summary>
public class FxMaskEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>手机号（Mask 声明——ORM 侧一律强制降级）</summary>
    public string Phone { get; set; } = "";

    /// <summary>标题</summary>
    public string Title { get; set; } = "";
}

/// <summary>脱敏 DTO（降级路径内存映射 + 脱敏产物）。</summary>
public class FxMaskDto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>手机号（脱敏后文本）</summary>
    public string Phone { get; set; } = "";

    /// <summary>标题</summary>
    public string Title { get; set; } = "";
}

/// <summary>
/// Runtime×ORM Fluent Mapper 配置 —— 两对演示类型对（纯可翻译 / 含 Mask 强制降级）。
/// </summary>
[MapperConfig]
public class FxMapper
{
    /// <summary>
    /// 链式声明入口。
    /// </summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
    {
        // 纯可翻译声明（同名自动匹配）——ORM 投影路径可用
        cfg.For<FxWikiEntity, FxWikiDto>();

        // 含 Mask 声明——ORM 侧一律强制降级（内存映射承载全语义）
        cfg.For<FxMaskEntity, FxMaskDto>()
           .Member(d => d.Phone).Mask<MaskPhone>();

           // 树形对级构建配置：Tree() 链式等价 [MapTree]，
        // 注册表承载（GetTreeConfig 消费），ToTreeList 树挂载位判定取此声明
        cfg.For<FxTreeEntity, FxTreeDto>()
           .Member(d => d.Children).Ignore()   // Children 为树挂载位，由 ToTreeList 填充，不参与映射
           .Tree();                   // 默认三元组 Id/ParentId/Children
    }
}

/// <summary>树形条目实体（Fluent Tree 演示源——平铺自引用结构）。</summary>
public class FxTreeEntity
{
    /// <summary>节点主键。</summary>
    public int Id { get; set; }

    /// <summary>节点名。</summary>
    public string Name { get; set; } = "";

    /// <summary>父节点（根节点为 null）。</summary>
    public int? ParentId { get; set; }
}

/// <summary>树形条目 DTO（Children 为树挂载位，ToTreeList 组装）。</summary>
public class FxTreeDto
{
    /// <summary>节点主键。</summary>
    public int Id { get; set; }

    /// <summary>节点名。</summary>
    public string Name { get; set; } = "";

    /// <summary>父节点。</summary>
    public int? ParentId { get; set; }

    /// <summary>子节点集合（树挂载位）。</summary>
    public List<FxTreeDto> Children { get; set; } = new();
}
