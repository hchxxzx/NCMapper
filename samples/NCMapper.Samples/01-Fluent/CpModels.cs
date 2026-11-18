using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Samples.Fluent;

/// <summary>
/// 构造参数绑定源实体（人员）——FullName/Age 为绑定源。
/// </summary>
public class CpPersonEntity
{
    /// <summary>全名（绑定形参 name）</summary>
    public string FullName { get; set; } = "";

    /// <summary>年龄（绑定形参 age）</summary>
    public int Age { get; set; }
}

/// <summary>
/// 不可变目标（真实缺口场景）——无公共无参构造 + 全 get-only 属性：
/// 形参不对应任何可写属性，自动同名匹配不产生 PropertyMapping，须 .MapCtorParam 直喂形参。
/// </summary>
public sealed class CpPersonView
{
    /// <summary>构造——形参 name/age（与属性 Name/Age 异名+get-only，无自动匹配锚点）</summary>
    /// <param name="name">名称</param>
    /// <param name="age">年龄</param>
    public CpPersonView(string name, int age)
    {
        Name = name;
        Age = age;
    }

    /// <summary>名称（get-only）</summary>
    public string Name { get; }

    /// <summary>年龄（get-only）</summary>
    public int Age { get; }
}

/// <summary>
/// 构造参数绑定源实体（订单，形态 2 字符串辅形态场景）。
/// </summary>
public class CpOrderEntity
{
    /// <summary>订单主键（绑定形参 id）</summary>
    public int Id { get; set; }

    /// <summary>订单号（绑定形参 orderNo）</summary>
    public string OrderNo { get; set; } = "";
}

/// <summary>
/// 不可变目标（形态 2：源属性名字符串绑定）。
/// </summary>
public sealed class CpOrderView
{
    /// <summary>构造——形参 orderNo/id</summary>
    /// <param name="orderNo">订单号</param>
    /// <param name="id">订单主键</param>
    public CpOrderView(string orderNo, int id)
    {
        OrderNo = orderNo;
        Id = id;
    }

    /// <summary>订单号（get-only）</summary>
    public string OrderNo { get; }

    /// <summary>订单主键（get-only）</summary>
    public int Id { get; }
}

/// <summary>
/// record 目标（边界澄清场景）——位置参数 = 同名 init 属性：
/// 既有 .Map 即可喂构造器（匹配键 = 目标属性名），MapCtorParam 仅为免 dest 锚的便利性补充。
/// </summary>
/// <param name="Name">名称（init 属性）</param>
/// <param name="Age">年龄（init 属性）</param>
public record CpRecordDto(string Name, int Age);

/// <summary>
/// Fluent 侧构造参数绑定 Mapper（对应 how-to/fluent/terminals.md「MapCtorParam」节）——
/// 形态 1（源 lambda）/ 形态 2（源属性名字符串）+ record 经既有 .Map。
/// </summary>
    #region CtorParam_CtorParamFluentMapper
[MapperConfig]
public class CtorParamFluentMapper
{
    #endregion

    /// <summary>链式声明入口（方法名任意，签名固定）</summary>
    /// <param name="cfg">Fluent 配置根</param>
    #region CtorParam_CtorParamFluentMapper_Configure
    public static void Configure(FluentMappingConfig cfg)
    {
        // Form 1 (primary, recommended): source-lambda symbol anchor (single-segment pure property path)
        cfg.For<CpPersonEntity, CpPersonView>()
           .MapCtorParam("name", p => p.FullName)
           .MapCtorParam("age", p => p.Age);

        // Form 2 (secondary): source property-name string (generation-time symbol resolution + typo diagnostic F51)
        cfg.For<CpOrderEntity, CpOrderView>()
           .MapCtorParam("orderNo", "OrderNo")
           .MapCtorParam("id", "Id");

        // record scenario: the existing .Map suffices (match key = target property name, ctor params receive it automatically)
        cfg.For<CpPersonEntity, CpRecordDto>()
           .Member(d => d.Name).From(s => s.FullName)
           .Member(d => d.Age).From(s => s.Age);
    }
}
    #endregion

/// <summary>
/// 嵌套构造订单实体（class，Customer 为嵌套导航对象）— record DTO 构造器绑定演示源。
/// </summary>
public class CtOrderEntity
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>订单号</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>金额</summary>
    public decimal Amount { get; set; }

    /// <summary>嵌套客户对象（record 构造后赋值）</summary>
    public CtCustomerEntity? Customer { get; set; }
}

/// <summary>
/// 嵌套客户实体（class）— 嵌套 record DTO 的映射源。
/// </summary>
public class CtCustomerEntity
{
    /// <summary>姓名</summary>
    public string Name { get; set; } = "";

    /// <summary>邮箱</summary>
    public string Email { get; set; } = "";
}

/// <summary>
/// record DTO — 主构造器 + 额外属性 + 嵌套 record（构造器传参与属性赋值混合形态）。
/// </summary>
/// <param name="Id">主键（构造器参数）</param>
/// <param name="OrderNo">订单号（构造器参数）</param>
/// <param name="Amount">金额（构造器参数）</param>
[MapFrom(typeof(CtOrderEntity))]
public record CtOrderDto(int Id, string OrderNo, decimal Amount)
{
    /// <summary>嵌套客户（record DTO，构造后赋值）</summary>
    public CtCustomerDto? Customer { get; set; }

    /// <summary>额外属性（构造后赋值）</summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 嵌套 record DTO — 客户信息（主构造器直绑）。
/// </summary>
/// <param name="Name">姓名（构造器参数）</param>
/// <param name="Email">邮箱（构造器参数）</param>
[MapFrom(typeof(CtCustomerEntity))]
public record CtCustomerDto(string Name, string Email);

/// <summary>
/// record 构造器绑定巡览 — 单对象 / 列表 / 反向三腿（how-to constructors-and-types.md 映射腿绑定源，巡览不挂 Run）。
/// </summary>
/// <param name="entity">源订单实体</param>
/// <param name="entities">源订单列表</param>
/// <returns>正向 DTO、列表 DTO、反向实体三结果</returns>
public static class CtorParamRecordTour
{
    /// <summary>Map / MapList / 反向 Map 三腿一体巡览。</summary>
    #region CtorParam_RecordCtorTour
    public static (CtOrderDto Dto, List<CtOrderDto> Dtos, CtOrderEntity Back) RecordCtorTour(CtOrderEntity entity, List<CtOrderEntity> entities)
    {
        var dto = Mapper.Map<CtOrderEntity, CtOrderDto>(entity)!;
        var dtos = Mapper.MapList<CtOrderEntity, CtOrderDto>(entities)!;
        var back = Mapper.Map<CtOrderDto, CtOrderEntity>(dto)!;
        return (dto, dtos, back);
    }
    #endregion
}
