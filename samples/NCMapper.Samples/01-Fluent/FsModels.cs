using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Samples.Fluent;

// ================================================================
// 03 Fluent FromSource — 模型与 Mapper 配置类
// FromSource 跨源绑定：速记（单字段）/ 分组块（同子源多字段）/ 顺序覆盖（同 dest 不同子源）/
// 块内 Mask/FromJson 修饰型消费。运行时双面向：Mapper.Map 主源面（FromSource 字段保持默认）
// + Mapper.Compose 合成面（主链 + 全部子源一次合成）。
// ================================================================

/// <summary>订单实体（FromSource 主源）。</summary>
public class FsOrderEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>订单号</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>金额</summary>
    public decimal Amount { get; set; }
}

/// <summary>客户子源（分组块演示源）。</summary>
public class FsCustomerSub
{
    /// <summary>客户名（分组块 Map 绑定）</summary>
    public string Name { get; set; } = "";

    /// <summary>客户手机号（块内 Mask 同名锚点）</summary>
    public string CustomerPhone { get; set; } = "";

    /// <summary>JSON 载荷（块内 FromJson 同名锚点）</summary>
    public string Payload { get; set; } = "";
}

/// <summary>供应商子源（顺序覆盖演示源）。</summary>
public class FsVendorSub
{
    /// <summary>供应商名（与 CustomerSub.Name 同 dest 绑定——后写覆盖先写）</summary>
    public string VendorName { get; set; } = "";
}

/// <summary>订单 DTO（FromSource 合成目标）。</summary>
public class FsOrderDto
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>订单号</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>金额</summary>
    public decimal Amount { get; set; }

    /// <summary>客户名（CustomerSub.Name 绑定 + VendorSub.VendorName 顺序覆盖）</summary>
    public string CustomerName { get; set; } = "";

    /// <summary>客户手机号（块内 Mask 脱敏产物）</summary>
    public string CustomerPhone { get; set; } = "";

    /// <summary>载荷对象（块内 FromJson 反序列化产物）</summary>
    public FsPayload Payload { get; set; } = new();
}

/// <summary>载荷对象（FromJson 反序列化目标）。</summary>
public class FsPayload
{
    /// <summary>城市</summary>
    public string City { get; set; } = "";

    /// <summary>等级</summary>
    public string Grade { get; set; } = "";
}

/// <summary>
/// FromSource 演示 Mapper 配置 —— 速记/分组块/顺序覆盖/块内修饰型消费。
/// <para><b>声明冲突与块内禁区（NCMAP_F29~F32）触发样例</b>（Error 级——真实声明会阻断编译，
/// 以注释形态呈现，对齐 how-to/fluent/from-source.md「声明冲突与块内禁区」节）：</para>
/// <code>
/// // NCMAP_F29（同 dest 冲突）：主链 Map 与 FromSource 绑定指向同一属性
/// cfg.For&lt;FsOrderEntity, FsOrderDto&gt;()
///    .Member(d => d.OrderNo).From(s => s.OrderNo)          // 主链声明
///    .Member(d =&gt; d.OrderNo).FromSource&lt;FsCustomerSub&gt;(s =&gt; s.Name);  // Error：一个属性只能由一个源供给
///
/// // NCMAP_F30（块内禁止 ConvertBack）：FromSource 绑定仅正向映射，无反向写回落点
/// cfg.For&lt;FsOrderEntity, FsOrderDto&gt;()
///    .FromSource&lt;FsCustomerSub&gt;(c =>
///    {
///        c.Member(d => d.CustomerName).ConvertBack(Conv);  // Error：块内禁止
///    });
///
/// // NCMAP_F31（块内禁止 NoAutoInclude）：子源字段全显式声明，无自动包含可禁
/// // NCMAP_F32（块内禁止 AfterMapping）：生成的 Compose 管线无钩子注入口，声明永不执行
/// </code>
/// </summary>
[MapperConfig]
public class FsMapper
{
    /// <summary>
    /// 链式声明入口。
    /// </summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
    {
        #region FluentFromSource_Bindings
        cfg.For<FsOrderEntity, FsOrderDto>()
           // Group block: multiple fields on the same sub-source (Map + Mask + FromJson modifiers attached)
           .FromSource<FsCustomerSub>(c =>
           {
               c.Member(d => d.CustomerName).From(s => s.Name);
               c.Member(d => d.CustomerPhone).Mask<MaskPhone>();
               c.Member(d => d.Payload).FromJson();
           })
           // Shorthand: bind a single field to another sub-source (same dest as the group block -- sequential override demo)
           .Member(d => d.CustomerName).FromSource<FsVendorSub>(s => s.VendorName);
        #endregion
    }
}
