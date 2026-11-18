using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Samples.Fluent;

// ================================================================
// 01 Fluent Basics — 模型与 Mapper 配置类
// 链式声明写在 [MapperConfig] 类内（DTO 保持干净）；源生成器编译期
// 扫描 Configure 方法体生成与 [MapFrom] 通道相同的 GeneratedMapper。
// ================================================================

/// <summary>订单实体（基础链示例源）。</summary>
public class ChainOrderEntity
{
    /// <summary>订单主键（Map 单段异名源：Id → DTO OrderId）</summary>
    public int Id { get; set; }

    /// <summary>客户姓名（嵌套对象）</summary>
    public ChainCustomerInfo Customer { get; set; } = new();

    /// <summary>客户手机号（Mask 源侧同名属性）</summary>
    public string CustomerPhone { get; set; } = "";

    /// <summary>备注（演示 Ignore 终端）</summary>
    public string InternalNote { get; set; } = "internal";

    /// <summary>订单明细（演示 AfterMapping 汇总）</summary>
    public List<ChainOrderItemEntity> Items { get; set; } = new();
}

/// <summary>客户信息（嵌套路径源）。</summary>
public class ChainCustomerInfo
{
    /// <summary>姓名</summary>
    public string Name { get; set; } = "";
}

/// <summary>订单明细项。</summary>
public class ChainOrderItemEntity
{
    /// <summary>重量（AfterMapping 汇总源）</summary>
    public double Weight { get; set; }
}

/// <summary>订单 DTO（基础链示例目标）。</summary>
public class ChainOrderDto
{
    /// <summary>订单主键（Map 单段异名目标：← Id）</summary>
    public int OrderId { get; set; }

    /// <summary>客户姓名（Map 嵌套源路径：Customer.Name）</summary>
    public string CustomerName { get; set; } = "";

    /// <summary>客户手机号（Mask 脱敏：MaskPhone，源侧同名属性）</summary>
    public string CustomerPhone { get; set; } = "";

    /// <summary>备注（Ignore 忽略——目标保持默认值）</summary>
    public string InternalNote { get; set; } = "";

    /// <summary>总重量（AfterMapping 汇总写入）</summary>
    public double TotalWeight { get; set; }
}

/// <summary>
/// 基础链 Mapper 配置（对应 how-to/fluent/basics.md「概述」示例块）——
/// Map 单段异名 + 嵌套源路径 + Ignore + Mask + AfterMapping 连写；
/// 显式 Map 声明为符号级路径，不受全局 NamingStrategy 名称规范化影响。
/// </summary>
    #region FluentBasics_ChainOrderMapper
[MapperConfig]
public class ChainOrderMapper
{
    #endregion

    /// <summary>
    /// 链式声明入口（方法名任意，签名固定）。
    /// </summary>
    /// <param name="cfg">Fluent 配置根</param>
    #region FluentBasics_ChainOrderMapper_Configure
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<ChainOrderEntity, ChainOrderDto>()
           .Member(d => d.OrderId).From(s => s.Id)
           .Member(d => d.CustomerName).From(s => s.Customer.Name)
           .Member(d => d.InternalNote).Ignore()
           .Member(d => d.CustomerPhone).Mask<MaskPhone>()
           .AfterMapping(ApplyComputedFields);
    }
    #endregion


    /// <summary>
    /// AfterMapping 目标方法（须 public/internal static——private 报 NCMAP_F12）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="dst">目标 DTO</param>
    #region FluentBasics_ChainOrderMapper_ApplyComputedFields
    public static void ApplyComputedFields(ChainOrderEntity src, ChainOrderDto dst)
    {
        dst.TotalWeight = src.Items.Sum(i => i.Weight);
    }
}
    #endregion

/// <summary>分类节点实体（MapTree 递归映射源，自引用树）。</summary>
public class CategoryNodeEntity
{
    /// <summary>节点主键</summary>
    public int Id { get; set; }

    /// <summary>节点名称</summary>
    public string Name { get; set; } = "";

    /// <summary>子节点（MapTree 递归映射位）</summary>
    public List<CategoryNodeEntity> Children { get; set; } = new();
}

/// <summary>分类节点 DTO（MapTree 递归映射目标）。</summary>
public class CategoryNodeDto
{
    /// <summary>节点主键</summary>
    public int Id { get; set; }

    /// <summary>节点名称</summary>
    public string Name { get; set; } = "";

    /// <summary>子节点（递归映射产物）</summary>
    public List<CategoryNodeDto> Children { get; set; } = new();
}

/// <summary>
/// MapTree 递归映射配置——Children 声明为递归映射属性（源侧按同名解析，递归走相同配置）。
/// </summary>
[MapperConfig]
public class CategoryMapper
{
    /// <summary>
    /// 链式声明入口。
    /// </summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<CategoryNodeEntity, CategoryNodeDto>()
           .Member(d => d.Children).MapTree();
    }
}

/// <summary>审计实体（NoAutoInclude 对级示例源）。</summary>
public class AuditEntity
{
    /// <summary>主键（显式 Map 保留）</summary>
    public int Id { get; set; }

    /// <summary>名称（对级 NoAutoInclude 后不映射）</summary>
    public string Name { get; set; } = "";

    /// <summary>密钥（对级 NoAutoInclude 后不映射）</summary>
    public string Secret { get; set; } = "";
}

/// <summary>审计 DTO（仅显式 Map 声明的属性被填充）。</summary>
public class AuditDto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>名称（保持默认）</summary>
    public string Name { get; set; } = "";

    /// <summary>密钥（保持默认）</summary>
    public string Secret { get; set; } = "";
}

/// <summary>
/// NoAutoInclude 对级配置——该 (S,T) 对禁用自动匹配，仅映射显式 Map 声明。
/// </summary>
[MapperConfig]
public class AuditMapper
{
    /// <summary>
    /// 链式声明入口。
    /// </summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<AuditEntity, AuditDto>()
           .NoAutoMatch()
           .Member(d => d.Id).From(s => s.Id);
    }
}

/// <summary>报表实体（AfterMapping 独立场景源）。</summary>
public class ReportEntity
{
    /// <summary>报表名</summary>
    public string Title { get; set; } = "";
}

/// <summary>报表 DTO（AfterMapping 写入派生标签）。</summary>
public class ReportDto
{
    /// <summary>报表名</summary>
    public string Title { get; set; } = "";

    /// <summary>派生标签（AfterMapping 写入）</summary>
    public string ReportTag { get; set; } = "";
}

/// <summary>
/// AfterMapping 独立场景配置——映射完成后注入派生字段。
/// </summary>
[MapperConfig]
public class ReportMapper
{
    /// <summary>
    /// 链式声明入口。
    /// </summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<ReportEntity, ReportDto>()
           .AfterMapping(StampReport);
    }

    /// <summary>
    /// 后置回调：写入派生标签。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="dst">目标 DTO</param>
    public static void StampReport(ReportEntity src, ReportDto dst)
    {
        dst.ReportTag = $"[report:{src.Title}]";
    }
}

/// <summary>树节点实体（Tree 自定义三元组示例源，平铺存储形态）。</summary>
public class TreeNodeEntity
{
    /// <summary>主键（自定义三元组：Key）</summary>
    public int Key { get; set; }

    /// <summary>父键（自定义三元组：ParentKey）</summary>
    public int? ParentKey { get; set; }

    /// <summary>节点名</summary>
    public string Name { get; set; } = "";
}

/// <summary>树节点 DTO（自定义三元组挂载位：Nodes）。</summary>
public class TreeNodeDto
{
    /// <summary>主键</summary>
    public int Key { get; set; }

    /// <summary>父键</summary>
    public int? ParentKey { get; set; }

    /// <summary>节点名</summary>
    public string Name { get; set; } = "";

    /// <summary>子节点挂载位（自定义三元组：Nodes）</summary>
    public List<TreeNodeDto> Nodes { get; set; } = new();
}

/// <summary>
/// Tree 自定义三元组配置——
/// 声明树按 Key/ParentKey/Nodes 构建（opt-in：未调用 .Tree() 时注册表 TreeConfig=null，不启用树语义）；
/// ToTreeList/投影树挂载位判定经注册表消费。
/// </summary>
    #region FluentBasics_TreeNodeMapper
[MapperConfig]
public class TreeNodeMapper
{
    #endregion

    /// <summary>
    /// 链式声明入口。
    /// </summary>
    /// <param name="cfg">Fluent 配置根</param>
    #region FluentBasics_TreeNodeMapper_Configure
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<TreeNodeEntity, TreeNodeDto>()
           .Tree("Key", "ParentKey", "Nodes");
    }
}
    #endregion

/// <summary>文章实体（属性级 NoAutoInclude 示例源）。</summary>
public class ArticleEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>标题</summary>
    public string Title { get; set; } = "";

    /// <summary>审计日志集合（属性级 NoAutoInclude 锚点）</summary>
    public List<string> AuditLogs { get; set; } = new();
}

/// <summary>文章 DTO。</summary>
public class ArticleDto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>标题</summary>
    public string Title { get; set; } = "";

    /// <summary>审计日志集合</summary>
    public List<string> AuditLogs { get; set; } = new();
}

/// <summary>
/// 属性级 NoAutoInclude 配置——
/// 仅 AuditLogs 属性跳过 ORM 降级路径的导航树自动收集（其余属性照常自动匹配）；
/// 与对级 NoAutoInclude() 语义粒度不同（对级 = 整对禁用自动匹配）。
/// <para>属性级 NoAutoInclude 的主要使用场景即导航属性——dest 为实体/DTO 导航类型
///（如 AuthorDto ← AuthorEntity）同样受支持，且 dest 不参与可映射性校验
///；本演示采用集合属性锚点（与 006 测试发射路径同构）。</para>
/// </summary>
[MapperConfig]
public class ArticleMapper
{
    /// <summary>
    /// 链式声明入口。
    /// </summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<ArticleEntity, ArticleDto>()
           .Member(d => d.AuditLogs).NoAutoInclude();
    }
}

/// <summary>明细项实体（索引段 lambda 源）。</summary>
public class IdxItemEntity
{
    /// <summary>明细标题</summary>
    public string Title { get; set; } = "";
}

/// <summary>容器实体（索引段 lambda 源根）。</summary>
public class IdxEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>明细集合（索引段访问目标）</summary>
    public List<IdxItemEntity> Items { get; set; } = new();
}

/// <summary>容器 DTO（索引段 lambda 目标）。</summary>
public class IdxDto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>首条明细标题（Items[0].Title 索引段映射）</summary>
    public string FirstTitle { get; set; } = "";
}

/// <summary>
/// 索引段 lambda 配置——
/// Map 源路径支持常量整数索引段（ElementAtOrDefault 语义：越界/空集合取 null 不抛）；
/// 变量/方法调用索引仍被 F06 拒绝。
/// </summary>
[MapperConfig]
public class IndexMapper
{
    /// <summary>
    /// 链式声明入口。
    /// </summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<IdxEntity, IdxDto>()
           .Member(d => d.FirstTitle).From(s => s.Items[0].Title);
    }
}
