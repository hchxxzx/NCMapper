using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// Compose 多源合成属性测试 — 验证从多个源对象合成 DTO 的不变式
/// <para>覆盖路径：双源合成全字段映射、null 辅源跳过、仅主源创建 DTO、随机输入全字段一致</para>
/// <para>每个 [Property] 默认运行 100 次随机输入，失败时自动 shrinking 到最小复现</para>
/// </summary>
public class ComposeProperties
{
    // ================================================================
    // 双源合成 — 全字段映射正确性
    // ================================================================

    /// <summary>
    /// 双源合成保留所有字段 — 100 次随机输入下 OrderEntity 和 CustomerEntity 的字段均正确映射到 DTO
    /// </summary>
    [Property]
    public bool Compose_Two_Sources_All_Fields_Mapped(
        int orderId, int amount, string customerName, int customerLevel)
    {
        var order = new FuzzOrderEntity { OrderId = orderId, Amount = amount };
        var customer = new FuzzCustomerEntity { CustomerName = customerName, CustomerLevel = customerLevel };

        var dto = Mapper.Compose<FuzzComposeDto>(order, customer)!;

        return dto.OrderId == orderId
            && dto.Amount == amount
            && dto.CustomerName == customerName
            && dto.CustomerLevel == customerLevel;
    }

    // ================================================================
    // null 辅源跳过 — 不影响主源字段
    // ================================================================

    /// <summary>
    /// null 辅源被跳过 — 传入 null 辅源时主源字段仍正确映射，辅源字段保持默认
    /// </summary>
    [Property]
    public bool Compose_Null_Second_Source_Skipped(int orderId, int amount)
    {
        var order = new FuzzOrderEntity { OrderId = orderId, Amount = amount };

        var dto = Mapper.Compose<FuzzComposeDto>(order, null!)!;

        return dto.OrderId == orderId
            && dto.Amount == amount
            && dto.CustomerName == ""
            && dto.CustomerLevel == 0;
    }

    // ================================================================
    // 仅主源 — 也能创建 DTO
    // ================================================================

    /// <summary>
    /// 仅主源创建 DTO — 只传入主源对象时 DTO 正常创建，主源字段映射，辅源字段保持默认
    /// </summary>
    [Property]
    public bool Compose_Only_Primary_Source_Creates_Dto(int orderId, int amount)
    {
        var order = new FuzzOrderEntity { OrderId = orderId, Amount = amount };

        var dto = Mapper.Compose<FuzzComposeDto>(order)!;

        return dto is not null
            && dto.OrderId == orderId
            && dto.Amount == amount;
    }

    // ================================================================
    // 辅源顺序无关性 — 辅源在主源前或后，结果一致
    // ================================================================

    /// <summary>
    /// 辅源在主源前传入 — 主源创建 DTO 后补充辅源字段，结果与主源在前一致
    /// </summary>
    [Property]
    public bool Compose_Second_Source_Before_Primary_All_Fields_Mapped(
        int orderId, int amount, string customerName, int customerLevel)
    {
        var order = new FuzzOrderEntity { OrderId = orderId, Amount = amount };
        var customer = new FuzzCustomerEntity { CustomerName = customerName, CustomerLevel = customerLevel };

        // 辅源在前，主源在后 — ComposeRuntime 内部缓存 pendingSources
        var dto = Mapper.Compose<FuzzComposeDto>(customer, order)!;

        return dto.OrderId == orderId
            && dto.Amount == amount
            && dto.CustomerName == customerName
            && dto.CustomerLevel == customerLevel;
    }
}
