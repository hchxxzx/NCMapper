using NCMapper;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Fluent;

/// <summary>
/// Fluent FromSource 场景集 — 跨源绑定速记/分组块、块内 Mask/FromJson 消费、
/// 顺序覆盖与主源面/合成面双面向演示。
/// </summary>
public static class FluentFromSourceScenarios
{
    /// <summary>
    /// 场景 1 — 分组块绑定 + 双面向对照：Mapper.Map 主源面（FromSource 字段保持默认）
    /// 与 Mapper.Compose 合成面（主链 + 子源一次合成）。
    /// </summary>
    /// <param name="order">主源订单</param>
    /// <param name="customer">子源客户</param>
    /// <returns>(主源面 DTO, 合成面 DTO)</returns>
    public static (FsOrderDto PrimaryFace, FsOrderDto ComposedFace) GroupedBlockDualFace(
        FsOrderEntity order, FsCustomerSub customer)
    {
        var primary = Mapper.Map<FsOrderEntity, FsOrderDto>(order)!;
        var composed = Mapper.Compose<FsOrderDto>(order, customer)!;
        return (primary, composed);
    }

    /// <summary>
    /// 场景 2 — 顺序覆盖：同一 dest（CustomerName）由两个子源分别绑定，
    /// Compose 按传入数组顺序处理后写覆盖先写。
    /// </summary>
    /// <param name="order">主源订单</param>
    /// <param name="customer">子源一（先写）</param>
    /// <param name="vendor">子源二（后写覆盖）</param>
    /// <returns>三源合成 DTO</returns>
    public static FsOrderDto OrderedOverride(FsOrderEntity order, FsCustomerSub customer, FsVendorSub vendor)
        => Mapper.Compose<FsOrderDto>(order, customer, vendor)!;

    /// <summary>
    /// 场景调度 — 顺序执行全部场景并输出。
    /// </summary>
    public static void Run()
    {
        var order = new FsOrderEntity
        {
            Id = 1001,
            OrderNo = "ORD-FS-001",
            Amount = 2999.00m,
        };
        var customer = new FsCustomerSub
        {
            Name = "WangWu",
            CustomerPhone = "13700001111",
            Payload = "{\"City\":\"Beijing\",\"Grade\":\"VIP\"}",
        };

        // 1. 分组块 + 双面向（主源面 FromSource 字段保持默认 / 合成面全量）
        var (primary, composed) = GroupedBlockDualFace(order, customer);
        OutputHelper.Dump("103-fromsource-dual-face", new { order, customer }, new { primary, composed });
        // 主源面：FromSource 命中字段（CustomerName/CustomerPhone/Payload）保持默认
        if (primary.CustomerName != "" || primary.CustomerPhone != "" || primary.Payload.City != "")
            OutputHelper.Error("103-fromsource-dual-face: primary face should keep FromSource fields at defaults (materialized split)");
        // 主链字段两面一致
        if (primary.OrderNo != composed.OrderNo || primary.Amount != composed.Amount)
            OutputHelper.Error("103-fromsource-dual-face: main-chain fields should match across both faces");
        // 合成面：块内 Map/Mask/FromJson 全量消费（安全断言：脱敏不落明文）
        if (composed.CustomerName != "WangWu")
            OutputHelper.Error($"103-fromsource-dual-face: composed-face CustomerName should be WangWu (actual: {composed.CustomerName})");
        if (composed.CustomerPhone.Contains("13700001111") || !composed.CustomerPhone.Contains("****"))
            OutputHelper.Error("103-fromsource-dual-face: in-block Mask did not mask (plaintext leak)");
        if (composed.Payload.City != "Beijing" || composed.Payload.Grade != "VIP")
            OutputHelper.Error("103-fromsource-dual-face: in-block FromJson expansion output mismatch");

        // 2. 顺序覆盖（同 dest 不同子源——后写覆盖先写）
        var vendor = new FsVendorSub { VendorName = "VENDOR-ZH" };
        var overridden = OrderedOverride(order, customer, vendor);
        OutputHelper.Dump("103-fromsource-ordered-override", new { customer.Name, vendor.VendorName }, overridden);
        if (overridden.CustomerName != "VENDOR-ZH")
            OutputHelper.Error($"103-fromsource-ordered-override: later write should override earlier write (actual: {overridden.CustomerName})");
        if (overridden.CustomerPhone.Contains("13700001111") || !overridden.CustomerPhone.Contains("****"))
            OutputHelper.Error("103-fromsource-ordered-override: Mask-no-plaintext assertion failed");
    }
}
