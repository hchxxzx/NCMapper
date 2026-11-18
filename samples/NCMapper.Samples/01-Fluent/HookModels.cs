using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Samples.Fluent;

/// <summary>
/// 钩子源实体（订单）——Id/Title/Qty/UnitPrice 参与自动同名映射。
/// </summary>
public class HookOrderEntity
{
    /// <summary>订单主键</summary>
    public int Id { get; set; }

    /// <summary>订单标题（含首尾空白——Before 预处理 Trim+大写）</summary>
    public string Title { get; set; } = "";

    /// <summary>数量</summary>
    public int Qty { get; set; }

    /// <summary>单价</summary>
    public double UnitPrice { get; set; }
}

/// <summary>
/// Fluent 侧钩子 DTO——NormalizedTitle/Total 为派生字段（无源侧同名属性，钩子写入不被映射覆盖）；
/// 可变属性（set 访问器）——_Update 形态生成的前提。
/// </summary>
public class HookOrderDto
{
    /// <summary>订单主键（自动同名映射）</summary>
    public int Id { get; set; }

    /// <summary>标题（自动同名映射，原样复制——Before 预处理只写派生字段，不碰映射将写的属性）</summary>
    public string Title { get; set; } = "";

    /// <summary>数量（自动同名映射）</summary>
    public int Qty { get; set; }

    /// <summary>派生字段（无源侧同名属性）：BeforeMapping 在赋值前写入，映射不覆盖</summary>
    public string NormalizedTitle { get; set; } = "";

    /// <summary>派生字段（无源侧同名属性）：AfterMapping 在映射完成后计算写入</summary>
    public string Total { get; set; } = "";
}

/// <summary>
/// 钩子宿主类——Before/After 回调方法组（须 public/internal static + 与生成代码同程序集可见）。
/// 每映射对一组方法：签名第二参为该对的 DTO 类型（方法签名即适用范围声明，D-d）。
/// </summary>
public static class HookHost
{
    /// <summary>
    /// Fluent 对前置钩子——dst 已构造、属性尚未填充：写派生字段（源驱动预处理）；
    /// 顺带记录 Before 时刻 dst.Title 的可见值（新建 = 初始化器默认，_Update = dst 旧值）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="dst">目标 DTO（构造后未填充）</param>
    public static void Normalize(HookOrderEntity src, HookOrderDto dst)
    {
        HooksLog.Steps.Add("Before");
        HooksLog.BeforeSeenTitle = dst.Title;
        dst.NormalizedTitle = src.Title.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Fluent 对后置钩子——映射完成：计算派生字段 Total（数量×单价）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="dst">目标 DTO（已映射）</param>
    public static void ComputeTotal(HookOrderEntity src, HookOrderDto dst)
    {
        HooksLog.Steps.Add("After");
        dst.Total = (src.Qty * src.UnitPrice).ToString("0.00");
    }

    /// <summary>特性对前置钩子——签名 (HookOrderEntity, AttrHookDto)，与 Fluent 对方法区分。</summary>
    /// <param name="src">源实体</param>
    /// <param name="dst">目标 DTO（构造后未填充）</param>
    #region FluentHooks_HookHostAttrMethods
    public static void AttrNormalize(HookOrderEntity src, AttrHookDto dst)
    {
        HooksLog.Steps.Add("Before");
        HooksLog.BeforeSeenTitle = dst.Title;
        dst.NormalizedTitle = src.Title.Trim().ToUpperInvariant();
    }
    #endregion


    /// <summary>特性对后置钩子——签名 (HookOrderEntity, AttrHookDto)。</summary>
    /// <param name="src">源实体</param>
    /// <param name="dst">目标 DTO（已映射）</param>
    #region FluentHooks_HookHostAttrMethods_AttrComputeTotal
    public static void AttrComputeTotal(HookOrderEntity src, AttrHookDto dst)
    {
        HooksLog.Steps.Add("After");
        dst.Total = (src.Qty * src.UnitPrice).ToString("0.00");
    }
    #endregion
}

/// <summary>
/// 钩子执行相位日志（场景断言 Before→赋值→After 次序与可见值）。
/// </summary>
public static class HooksLog
{
    /// <summary>相位序列（"Before"/"After" 按执行序追加）</summary>
    public static readonly System.Collections.Generic.List<string> Steps = new();

    /// <summary>Before 时刻 dst.Title 的可见值</summary>
    public static string? BeforeSeenTitle;

    /// <summary>清空（每子场景调用）</summary>
    public static void Reset()
    {
        Steps.Clear();
        BeforeSeenTitle = null;
    }
}

/// <summary>
/// Fluent 侧钩子 Mapper（对应 how-to/fluent/hooks.md「BeforeMapping/AfterMapping」节）——
/// Before/After 双钩子连写（方法组编译期绑定）。
/// </summary>
    #region FluentHooks_HookFluentMapper
[MapperConfig]
public class HookFluentMapper
{
    #endregion

    /// <summary>链式声明入口（方法名任意，签名固定）</summary>
    /// <param name="cfg">Fluent 配置根</param>
    #region FluentHooks_HookFluentMapper_Configure
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<HookOrderEntity, HookOrderDto>()
           .BeforeMapping(HookHost.Normalize)
           .AfterMapping(HookHost.ComputeTotal);
    }
}
    #endregion

/// <summary>
/// 跨源过滤演示源（D-d：钩子随 DTO、按方法签名适用逐对生效——
/// 本源与 AttrHookDto 的 Fluent 对签名不匹配，特性侧钩子经跨对传播注入后被过滤，不发射、不发诊断）。
/// </summary>
public class HookNoteSource
{
    /// <summary>备注（跨对绑定 Title）</summary>
    public string Note { get; set; } = "";
}

/// <summary>
/// 特性侧钩子 Mapper（对应 how-to/fluent/hooks.md「BeforeMapping/AfterMapping」节的特性通道）——
/// [BeforeMapping]/[AfterMapping] 类级特性（双参宿主形态）+ [MapFrom] 锚定；Inherited=false 不随继承传播。
/// </summary>
[MapperConfig]
public class HookCrossMapper
{
    /// <summary>链式声明入口</summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<HookNoteSource, AttrHookDto>()
           .Member(d => d.Title).From(s => s.Note);
    }
}

/// <summary>
/// 特性侧钩子 DTO——与 Fluent 侧填充同构的派生字段（双通道等价）；
/// 钩子随 DTO：HookOrderEntity→AttrHookDto 对签名匹配发射，HookNoteSource→AttrHookDto 对签名不匹配不发射。
/// </summary>
    #region FluentHooks_AttrHookDto
[MapFrom(typeof(HookOrderEntity))]
[BeforeMapping(typeof(HookHost), nameof(HookHost.AttrNormalize))]
[AfterMapping(typeof(HookHost), nameof(HookHost.AttrComputeTotal))]
public class AttrHookDto
{
    #endregion

    /// <summary>订单主键（自动同名映射）</summary>
    #region FluentHooks_AttrHookDto_Id
    public int Id { get; set; }
    #endregion


    /// <summary>标题（自动同名映射）</summary>
    #region FluentHooks_AttrHookDto_Title
    public string Title { get; set; } = "";
    #endregion


    /// <summary>数量（自动同名映射）</summary>
    #region FluentHooks_AttrHookDto_Qty
    public int Qty { get; set; }
    #endregion


    /// <summary>派生字段：BeforeMapping 写入</summary>
    #region FluentHooks_AttrHookDto_NormalizedTitle
    public string NormalizedTitle { get; set; } = "";
    #endregion


    /// <summary>派生字段：AfterMapping 写入</summary>
    #region FluentHooks_AttrHookDto_Total
    public string Total { get; set; } = "";
}
    #endregion
