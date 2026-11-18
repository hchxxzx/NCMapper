using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// Compose 高级功能属性测试 — 回调、ComposeList 批量合成、三源合成
/// <para>覆盖路径：Compose(Action) 回调修改 DTO、ComposeList 单组/多组、三源全字段映射</para>
/// <para>每个 [Property] 默认运行 100 次随机输入，失败时自动 shrinking 到最小复现</para>
/// </summary>
public class ComposeAdvancedProperties
{
    // ================================================================
    // Compose 带回调 — Action<TTarget> 在合成完成后执行
    // ================================================================

    /// <summary>
    /// Compose 回调正确执行 — 合成完成后回调修改 DTO 字段
    /// </summary>
    [Property]
    public bool Compose_With_Action_Applies_Callback(int orderId, int amount)
    {
        var order = new FuzzOrderEntity { OrderId = orderId, Amount = amount };
        var dto = Mapper.Compose<FuzzComposeDto>(
            d => d.CustomerName = "INJECTED",
            order)!;
        return dto.OrderId == orderId
            && dto.Amount == amount
            && dto.CustomerName == "INJECTED";
    }

    /// <summary>
    /// Compose 回调在所有源写入后执行 — 回调可以访问已映射的字段值
    /// </summary>
    [Property]
    public bool Compose_With_Action_Can_Read_Mapped_Fields(
        int orderId, int amount, string customerName, int customerLevel)
    {
        var order = new FuzzOrderEntity { OrderId = orderId, Amount = amount };
        var customer = new FuzzCustomerEntity { CustomerName = customerName, CustomerLevel = customerLevel };

        string? capturedName = null;
        int capturedLevel = 0;
        var dto = Mapper.Compose<FuzzComposeDto>(
            d => { capturedName = d.CustomerName; capturedLevel = d.CustomerLevel; },
            order, customer)!;

        return capturedName == customerName
            && capturedLevel == customerLevel;
    }

    // ================================================================
    // ComposeList — 批量合成
    // ================================================================

    /// <summary>
    /// ComposeList 单组结果与 Compose 一致 — 同源对象批量合成和单次合成结果相同
    /// </summary>
    [Property]
    public bool ComposeList_Single_Group_Equals_Compose(
        int orderId, int amount, string customerName, int customerLevel)
    {
        var order = new FuzzOrderEntity { OrderId = orderId, Amount = amount };
        var customer = new FuzzCustomerEntity { CustomerName = customerName, CustomerLevel = customerLevel };

        var singleDto = Mapper.Compose<FuzzComposeDto>(order, customer)!;
        var list = Mapper.ComposeList<FuzzComposeDto>(new object[] { order, customer })!;

        return list.Count == 1
            && list[0].OrderId == singleDto.OrderId
            && list[0].Amount == singleDto.Amount
            && list[0].CustomerName == singleDto.CustomerName
            && list[0].CustomerLevel == singleDto.CustomerLevel;
    }

    /// <summary>
    /// ComposeList 多组全字段正确 — 两组源对象批量合成后各字段正确
    /// </summary>
    [Property]
    public bool ComposeList_Multiple_Groups_All_Mapped(
        int orderId1, int amount1, int orderId2, int amount2)
    {
        var group1 = new FuzzOrderEntity { OrderId = orderId1, Amount = amount1 };
        var group2 = new FuzzOrderEntity { OrderId = orderId2, Amount = amount2 };

        var list = Mapper.ComposeList<FuzzComposeDto>(
            new[] { group1 },
            new[] { group2 })!;

        return list.Count == 2
            && list[0].OrderId == orderId1 && list[0].Amount == amount1
            && list[1].OrderId == orderId2 && list[1].Amount == amount2;
    }

    // ================================================================
    // 三源合成 — OrderEntity + CustomerEntity + ProductEntity
    // ================================================================

    /// <summary>
    /// 三源合成全字段正确 — 三个源对象的字段均正确映射到 DTO
    /// </summary>
    [Property]
    public bool Compose_Three_Sources_All_Fields_Mapped(
        int orderId, int amount,
        string customerName, int customerLevel,
        string productName, int stock)
    {
        var order = new FuzzOrderEntity { OrderId = orderId, Amount = amount };
        var customer = new FuzzCustomerEntity { CustomerName = customerName, CustomerLevel = customerLevel };
        var product = new FuzzProductEntity { ProductName = productName, Stock = stock };

        var dto = Mapper.Compose<FuzzTripleComposeDto>(order, customer, product)!;

        return dto.OrderId == orderId
            && dto.Amount == amount
            && dto.CustomerName == customerName
            && dto.CustomerLevel == customerLevel
            && dto.ProductName == productName
            && dto.Stock == stock;
    }

    /// <summary>
    /// 三源合成 null 中间源跳过 — 传入 null 辅源时其他源字段仍正确映射
    /// </summary>
    [Property]
    public bool Compose_Three_Sources_Null_Middle_Skipped(
        int orderId, int amount,
        string productName, int stock)
    {
        var order = new FuzzOrderEntity { OrderId = orderId, Amount = amount };
        var product = new FuzzProductEntity { ProductName = productName, Stock = stock };

        // 中间传 null customer，前后源字段应正确映射
        var dto = Mapper.Compose<FuzzTripleComposeDto>(order, null!, product)!;

        return dto.OrderId == orderId
            && dto.Amount == amount
            && dto.CustomerName == ""
            && dto.CustomerLevel == 0
            && dto.ProductName == productName
            && dto.Stock == stock;
    }
}
