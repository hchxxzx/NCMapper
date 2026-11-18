using System.Globalization;
using NCMapper;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.MapFormat;

/// <summary>
/// [MapFormat] / .Member(dest).Format 场景集 —— 属性级格式化（特性通道 + Fluent 通道双形态）、
/// 可空源、FormatThenMask 组合、文化口径演示。
/// </summary>
public static class MapFormatScenarios
{
    /// <summary>
    /// 场景 1 — 特性通道：同名属性格式化（日期/数值/枚举/时长）+ 可空源 null 语义。
    /// </summary>
    /// <param name="src">源订单实体</param>
    /// <returns>特性通道映射 DTO</returns>
    public static MfOrderDto AttributeChannel(MfOrder src)
        => Mapper.Map<MfOrder, MfOrderDto>(src)!;

    /// <summary>
    /// 场景 2 — Fluent 通道：.Member(dest).Format 终端（与特性通道同承载同产物）。
    /// </summary>
    /// <param name="src">源订单实体</param>
    /// <returns>Fluent 通道映射 DTO</returns>
    public static MfFluentDto FluentChannel(MfOrder src)
        => Mapper.Map<MfOrder, MfFluentDto>(src)!;

    /// <summary>
    /// 场景调度 — 顺序执行全部场景并输出（断言失败即记录，禁"只 Dump 不验证"）。
    /// </summary>
    public static void Run()
    {
        // 1. 特性通道（en-US 固定文化——N2 千分位形态可断言）
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var src = new MfOrder
            {
                CreatedAt = new DateTime(2026, 9, 29, 18, 30, 0),
                Amount = 1234.5m,
                Status = DayOfWeek.Monday,
                Duration = new TimeSpan(8, 30, 0),
                ShippedDate = null,
                CardNo = 1234567890m,
            };
            var dto = AttributeChannel(src);
            OutputHelper.Dump("202-mapformat-attribute", src, dto);
            if (dto.CreatedAt != "2026-09-29" || dto.Amount != "1,234.50" || dto.Status != "Monday"
                || dto.Duration != "08:30" || dto.ShippedDate != null)
                OutputHelper.Error("202-mapformat-attribute: attribute-channel format output mismatch");
            if (!dto.CardNo.Contains('*'))
                OutputHelper.Error("202-mapformat-attribute: FormatThenMask combined output mismatch (should contain mask chars — format first, then mask)");

            // 2. Fluent 通道（同锚同产物）
            var fluentDto = FluentChannel(src);
            OutputHelper.Dump("202-mapformat-fluent", src, fluentDto);
            if (fluentDto.CreatedAt != "2026-09-29" || fluentDto.Amount != "1,234.50"
                || fluentDto.ShippedDate != null)
                OutputHelper.Error("202-mapformat-fluent: Fluent-channel format output mismatch");

            // 3. 文化分叉演示：de-DE 下 N2 输出形态随当前文化（有意设计——未标注属性恒 Invariant）
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var deDto = AttributeChannel(src);
            OutputHelper.Dump("202-mapformat-culture", src, deDto);
            if (deDto.Amount != "1.234,50")
                OutputHelper.Error("202-mapformat-culture: de-DE culture output mismatch (N2 should output 1.234,50)");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
