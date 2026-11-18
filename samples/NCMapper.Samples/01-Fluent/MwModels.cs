using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Samples.Fluent;

/// <summary>
/// 守卫源实体（条目）——Name/Score/Kind 为谓词输入。
/// </summary>
public class MwItemEntity
{
    /// <summary>条目主键</summary>
    public int Id { get; set; }

    /// <summary>名称（命名方法组谓词入参）</summary>
    public string Name { get; set; } = "";

    /// <summary>分值（内联谓词入参）</summary>
    public int Score { get; set; }

    /// <summary>分类（T2 整源谓词判定输入）</summary>
    public string Kind { get; set; } = "";

    /// <summary>有界源值（组合专用——独立于自动同名属性 Score，显式 .Map 与其同锚点 MapWhen 共享）</summary>
    public int BoundedSource { get; set; }
}

/// <summary>
/// Fluent 侧守卫 DTO——四个属性各锚一种谓词形态（命名方法组 / 内联转录 / 整源 / 同锚点组合）。
/// </summary>
public class MwItemDto
{
    /// <summary>条目主键（自动同名，无守卫）</summary>
    public int Id { get; set; }

    /// <summary>名称（命名静态方法组谓词 HasContent）</summary>
    public string Name { get; set; } = "";

    /// <summary>分值（无捕获内联 lambda 谓词，转录进生成代码）</summary>
    public int Score { get; set; }

    /// <summary>分类标签（T2 整源谓词 + 显式 Map——条件→转换→赋值）</summary>
    public string KindTag { get; set; } = "";

        /// <summary>有界分值（同锚点：.Member(d =&gt; d.BoundedScore).From(s =&gt; s.BoundedSource) + .Member(d =&gt; d.BoundedScore).MapWhen()——条件假则跳过赋值）</summary>
    public int BoundedScore { get; set; }
}

/// <summary>
/// 谓词宿主类——A 形态命名静态方法组（须 public/internal static + 同程序集可见）。
/// </summary>
public static class MwRules
{
    /// <summary>T1 字段值谓词——非空非空白</summary>
    /// <param name="value">源字段值</param>
    /// <returns>true 才映射</returns>
    public static bool HasContent(string value) => !string.IsNullOrWhiteSpace(value);

    /// <summary>T2 整源谓词——单参 == 源实体类型判 T2（与 Convert 族同规则）</summary>
    /// <param name="src">源实体</param>
    /// <returns>Kind=="A" 才映射</returns>
    public static bool IsKindA(MwItemEntity src) => src.Kind == "A";
}

/// <summary>
/// Fluent 侧守卫 Mapper（对应 how-to/fluent/terminals.md「MapWhen」节）——
/// 命名方法组 + 内联转录 + 整源 + 同锚点（Map+MapWhen）四形态同链。
/// </summary>
    #region MapWhen_MapWhenFluentMapper
[MapperConfig]
public class MapWhenFluentMapper
{
    #endregion

    /// <summary>链式声明入口（方法名任意，签名固定）</summary>
    /// <param name="cfg">Fluent 配置根</param>
    #region MapWhen_MapWhenFluentMapper_Configure
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<MwItemEntity, MwItemDto>()
           .Member(d => d.Name).MapWhen(MwRules.HasContent)
           .Member(d => d.Score).MapWhen<MwItemEntity>(s => s.Score >= 90)
           .Member(d => d.KindTag).From(s => s.Kind)
           .Member(d => d.KindTag).MapWhen<MwItemEntity>(MwRules.IsKindA)
           .Member(d => d.BoundedScore).From(s => s.BoundedSource)
           .Member(d => d.BoundedScore).MapWhen<MwItemEntity>(s => s.BoundedSource > 0);
    }
}
    #endregion

/// <summary>
/// 特性侧守卫 DTO（对应 how-to/fluent/terminals.md「MapWhen」节的特性通道）——
/// [MapWhen(nameof(HasContent))] 属性级：谓词方法在被标注属性的声明类型上按名解析（单类型自足）；
/// Inherited=true 随属性继承（与钩子特性 Inherited=false 不同）。
/// </summary>
    #region MapWhen_MwAttrItemDto
[MapFrom(typeof(MwItemEntity))]
public class MwAttrItemDto
{
    #endregion

    /// <summary>条目主键（自动同名）</summary>
    #region MapWhen_MwAttrItemDto_Id
    public int Id { get; set; }
    #endregion


    /// <summary>名称（[MapWhen] 属性级守卫——谓词 HasContent 声明于本类型）</summary>
    #region MapWhen_MwAttrItemDto_Name
    [MapWhen(nameof(HasContent))]
    public string Name { get; set; } = "";
    #endregion


    /// <summary>分值（自动同名，无守卫）</summary>
    #region MapWhen_MwAttrItemDto_Score
    public int Score { get; set; }
    #endregion


    /// <summary>T1 字段值谓词（public static，签名 bool(string)——与 [Convert(string)] 单参形态同规则）</summary>
    /// <param name="value">源字段值</param>
    /// <returns>true 才映射</returns>
    #region MapWhen_MwAttrItemDto_HasContent
    public static bool HasContent(string value) => !string.IsNullOrWhiteSpace(value);
}
    #endregion
