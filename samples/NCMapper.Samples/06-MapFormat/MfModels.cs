using System;
using System.Globalization;
using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Samples.MapFormat;


/// <summary>
/// [MapFormat] 演示模型 —— 源订单实体（日期/金额/枚举/时长/可空日期/卡号）。
/// </summary>
    #region MapFormat_AttrChannel
public class MfOrder
{
    #endregion

    #region MapFormat_AttrChannel_MfOrder_CreatedAt
    public DateTime CreatedAt { get; set; }
    #endregion

    #region MapFormat_AttrChannel_MfOrder_Amount
    public decimal Amount { get; set; }
    #endregion

    #region MapFormat_AttrChannel_MfOrder_Status
    public DayOfWeek Status { get; set; }
    #endregion

    #region MapFormat_AttrChannel_MfOrder_Duration
    public TimeSpan Duration { get; set; }
    #endregion

    #region MapFormat_AttrChannel_MfOrder_ShippedDate
    public DateTime? ShippedDate { get; set; }
    #endregion

    #region MapFormat_AttrChannel_MfOrder_CardNo
    public decimal CardNo { get; set; }
}
    #endregion


/// <summary>
/// [MapFormat] 演示 DTO —— 特性通道标注（格式化/可空/组合全形态）。
/// [ConvertBack] 配套：NCMAP046 反向拦截为编译期强制语义，反向演示路径挂解析器（方向正交合法组合）。
/// </summary>
    #region MapFormat_AttrChannel_MfOrderDto
[MapFrom(typeof(MfOrder))]
public class MfOrderDto
{
    #endregion

    #region MapFormat_AttrChannel_MfOrderDto_CreatedAt
    [MapFormat("yyyy-MM-dd")]
    [ConvertBack(typeof(MfParsers), nameof(MfParsers.ParseDateTime))]
    public string CreatedAt { get; set; } = "";
    #endregion

    #region MapFormat_AttrChannel_MfOrderDto_Amount

    [MapFormat("N2")]
    [ConvertBack(typeof(MfParsers), nameof(MfParsers.ParseDecimal))]
    public string Amount { get; set; } = "";
    #endregion

    #region MapFormat_AttrChannel_MfOrderDto_Status

    [MapFormat("G")]
    [ConvertBack(typeof(MfParsers), nameof(MfParsers.ParseDayOfWeek))]
    public string Status { get; set; } = "";
    #endregion

    #region MapFormat_AttrChannel_MfOrderDto_Duration

    [MapFormat("hh\\:mm")]
    [ConvertBack(typeof(MfParsers), nameof(MfParsers.ParseTimeSpan))]
    public string Duration { get; set; } = "";
    #endregion

    #region MapFormat_AttrChannel_MfOrderDto_ShippedDate

    [MapFormat("yyyy-MM-dd")]
    [ConvertBack(typeof(MfParsers), nameof(MfParsers.ParseNullableDateTime))]
    public string? ShippedDate { get; set; }
    #endregion

    #region MapFormat_AttrChannel_MfOrderDto_CardNo

    [MapFormat("N2")]
    [Mask(typeof(MfCardMask))]
    [ConvertBack(typeof(MfParsers), nameof(MfParsers.ParseDecimal))]
    public string CardNo { get; set; } = "";
}
    #endregion


/// <summary>反向解析器组（各类型 Parse 重载——参数同型异名防重载冲突）</summary>
public static class MfParsers
{
    public static DateTime ParseDateTime(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture);
    public static decimal ParseDecimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    public static TimeSpan ParseTimeSpan(string value) => TimeSpan.Parse(value, CultureInfo.InvariantCulture);
    public static DayOfWeek ParseDayOfWeek(string value) => (DayOfWeek)int.Parse(value, CultureInfo.InvariantCulture);
    public static DateTime? ParseNullableDateTime(string value)
        => value is { Length: > 0 } nonEmpty ? DateTime.Parse(nonEmpty, CultureInfo.InvariantCulture) : null;
}

/// <summary>
/// 卡号掩码实现（FormatThenMask 组合演示用——先格式化再脱敏）。
/// </summary>
public class MfCardMask : IMask
{
    public string Mask(string? value) => string.IsNullOrEmpty(value) ? "" : value.Length <= 4 ? value : value[..4] + "****" + value[^4..];
}


/// <summary>
/// Fluent 通道演示 DTO —— 干净模型（声明在 MfFluentConfig 集中配置）。
/// </summary>
    #region MapFormat_FluentChannel
public class MfFluentDto
{
    #endregion

    #region MapFormat_FluentChannel_MfFluentDto_CreatedAt
    public string CreatedAt { get; set; } = "";
    #endregion

    #region MapFormat_FluentChannel_MfFluentDto_Amount
    public string Amount { get; set; } = "";
    #endregion

    #region MapFormat_FluentChannel_MfFluentDto_ShippedDate
    public string? ShippedDate { get; set; }
}
    #endregion


/// <summary>
/// Fluent 通道声明 —— .Member(dest).Format 终端（与特性通道同承载、同守门、同产物）。
/// .Member(dest).ConvertBack 配套（F67 编译期反向拦截强制语义——方向正交合法组合）。
/// </summary>
    #region MapFormat_FluentChannel_MfFluentConfig
[MapperConfig]
public class MfFluentConfig
{
    #endregion

    #region MapFormat_FluentChannel_MfFluentConfig_Configure
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<MfOrder, MfFluentDto>()
           .Member(d => d.CreatedAt).Format("yyyy-MM-dd")
           .Member(d => d.Amount).Format("N2")
           .Member(d => d.ShippedDate).Format("yyyy-MM-dd")
           .Member(d => d.CreatedAt).ConvertBack<DateTime>(MfParsers.ParseDateTime)
           .Member(d => d.Amount).ConvertBack<decimal>(MfParsers.ParseDecimal)
           .Member(d => d.ShippedDate!).ConvertBack<DateTime?>(MfParsers.ParseNullableDateTime);
    }
}
    #endregion

