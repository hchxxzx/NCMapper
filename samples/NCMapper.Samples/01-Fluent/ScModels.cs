using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Samples.Fluent;

/// <summary>
/// 多态源实体（基类）——Name/Weight 为基类映射对字段。
/// </summary>
public class ScAnimalEntity
{
    /// <summary>名称（基类对映射）</summary>
    public string Name { get; set; } = "";

    /// <summary>体重（基类对映射）</summary>
    public double Weight { get; set; }
}

/// <summary>
/// 已登记子类源（犬）——Breed 为子类专有字段。
/// </summary>
public class ScDogEntity : ScAnimalEntity
{
    /// <summary>品种（子类专有）</summary>
    public string Breed { get; set; } = "";
}

/// <summary>
/// 已登记子类源（猫）——Indoor 为子类专有字段。
/// </summary>
public class ScCatEntity : ScAnimalEntity
{
    /// <summary>是否室内（子类专有）</summary>
    public bool Indoor { get; set; }
}

/// <summary>
/// 未登记子类源（鸟）——演示分派链未命中回落基类兜底体（子类专有字段无来源）。
/// </summary>
public class ScBirdEntity : ScAnimalEntity
{
    /// <summary>翼展（未登记，无对应映射）</summary>
    public double Wingspan { get; set; }
}

/// <summary>
/// Fluent 侧基类 DTO——无 [MapFrom]（Fluent 通道 cfg.For 即锚定基类映射对，两通道声明互斥 NCMAP_F11）。
/// </summary>
public class ScAnimalDto
{
    /// <summary>名称（基类体映射）</summary>
    public string Name { get; set; } = "";

    /// <summary>体重（基类对映射）</summary>
    public double Weight { get; set; }
}

/// <summary>
/// Fluent 侧子类 DTO（犬）——独立声明子类对（同 FQN 二元组被 MapSubclass 引用，不重发）。
/// </summary>
public class ScDogDto : ScAnimalDto
{
    /// <summary>品种（子类专有）</summary>
    public string Breed { get; set; } = "";
}

/// <summary>
/// Fluent 侧子类 DTO（猫）。
/// </summary>
public class ScCatDto : ScAnimalDto
{
    /// <summary>是否室内（子类专有）</summary>
    public bool Indoor { get; set; }
}

/// <summary>
/// 特性侧基类 DTO——[MapSubclass] type-level 多实例登记（Inherited=false，双 typeof 实参）；
/// 特性通道无泛型约束，须同时标注 [MapFrom] 锚定基类映射对（缺锚定报 NCMAP_F49）。
/// </summary>
    #region MapSubclass_ScAttrAnimalDto
[MapFrom(typeof(ScAnimalEntity))]
[MapSubclass(typeof(ScDogEntity), typeof(ScAttrDogDto))]
[MapSubclass(typeof(ScCatEntity), typeof(ScAttrCatDto))]
public class ScAttrAnimalDto
{
    #endregion

    /// <summary>名称（基类体映射）</summary>
    #region MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Name
    public string Name { get; set; } = "";
    #endregion


    /// <summary>体重（基类体映射）</summary>
    #region MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Weight
    public double Weight { get; set; }
}
    #endregion


/// <summary>
/// 特性侧子类 DTO（犬）。
/// </summary>
    #region MapSubclass_ScAttrAnimalDto_ScAttrDogDto
public class ScAttrDogDto : ScAttrAnimalDto
{
    #endregion

    /// <summary>品种（子类专有）</summary>
    #region MapSubclass_ScAttrAnimalDto_ScAttrDogDto_Breed
    public string Breed { get; set; } = "";
}
    #endregion


/// <summary>
/// 特性侧子类 DTO（猫）。
/// </summary>
    #region MapSubclass_ScAttrAnimalDto_ScAttrCatDto
public class ScAttrCatDto : ScAttrAnimalDto
{
    #endregion

    /// <summary>是否室内（子类专有）</summary>
    #region MapSubclass_ScAttrAnimalDto_ScAttrCatDto_Indoor
    public bool Indoor { get; set; }
}
    #endregion


/// <summary>
/// Fluent 侧多态 Mapper（对应 how-to/fluent/terminals.md「MapSubclass」节）——
/// 基类对级登记两对子类型，isinst 分派链覆盖 Map/_Update/MapList/MapListInto 四族。
/// </summary>
    #region MapSubclass_ScAttrAnimalDto_SubclassFluentMapper
[MapperConfig]
public class SubclassFluentMapper
{
    #endregion

    /// <summary>链式声明入口（方法名任意，签名固定）</summary>
    /// <param name="cfg">Fluent 配置根</param>
    #region MapSubclass_ScAttrAnimalDto_SubclassFluentMapper_Configure
    public static void Configure(FluentMappingConfig cfg)
    {
        // Pair-level terminal: register two subtype pairs (generic constraints CS0311/CS0314 intercepted at the call site -- a wrong inheritance relation fails to compile)
        cfg.For<ScAnimalEntity, ScAnimalDto>()
           .MapSubclass<ScDogEntity, ScDogDto>()
           .MapSubclass<ScCatEntity, ScCatDto>();
    }
}
    #endregion
