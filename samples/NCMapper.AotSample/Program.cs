using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.AotSample;

/// <summary>
/// NativeAOT 部署示例程序 — 演示标 [MapFrom] 的 AOT 安全用法。
/// <para>全部 DTO 标注 [MapFrom] 走源生成器路径（零反射），可完整 NativeAOT 发布。</para>
/// <para>发布命令：dotnet publish -c Release -r win-x64 --self-contained</para>
/// </summary>
    #region AotSample_EntryValidation
internal static class Program
{
    #endregion

    #region AotSample_EntryValidation_Program_Main
    static void Main()
    {
        Console.WriteLine("=== NCMapper AOT Sample ===");

        // 1. Map (source-generator path)
        var entity = new UserEntity { Id = 1, Name = "AOT User", Email = "user@example.com" };
        // DTO carries [MapFrom], so runtime takes the source-generated path (AOT-safe); the IL warning is only the conservative signal from Mapper.Map's public UC annotations, known safe
#pragma warning disable IL2026, IL3050
        var dto = Mapper.Map<UserEntity, UserDto>(entity)!;
#pragma warning restore IL2026, IL3050
        Assert(dto.Id == 1 && dto.Name == "AOT User" && dto.Email == "user@example.com", "Map");
        Console.WriteLine("[OK] Map");

        // 2. MapList (source-generator path)
        var list = new List<UserEntity>
        {
            new() { Id = 1, Name = "A", Email = "a@x.com" },
            new() { Id = 2, Name = "B", Email = "b@x.com" },
        };
        // DTO carries [MapFrom], so runtime takes the source-generated path (AOT-safe); the IL warning is only the conservative signal from Mapper.MapList's public UC annotations, known safe
#pragma warning disable IL2026, IL3050
        var dtos = Mapper.MapList<UserEntity, UserDto>(list)!;
#pragma warning restore IL2026, IL3050
        Assert(dtos.Count == 2 && dtos[1].Name == "B", "MapList");
        Console.WriteLine("[OK] MapList");

        // 3. DeepCopy (source-generator path)
        // DTO carries [MapFrom], so runtime takes the source-generated path (AOT-safe); the IL warning is only the conservative signal from Mapper.DeepCopy's public UC annotations, known safe
#pragma warning disable IL2026, IL3050
        var copy = Mapper.DeepCopy(entity)!;
#pragma warning restore IL2026, IL3050
        copy.Name = "Modified";
        Assert(entity.Name == "AOT User", "DeepCopy 隔离");
        Console.WriteLine("[OK] DeepCopy");

        // 4. Validate (source-generator compile-time validation results, zero reflection)
        var results = Mapper.Validate();
        Assert(results.Count > 0, "Validate 非空");
        Console.WriteLine($"[OK] Validate（{results.Count} 条，源生成路径）");

        // 5. Compose (source-generated path -- RegisterGeneratedCompose)
        var order = new OrderEntity { OrderId = 100, Amount = 9.9m };
        var customer = new CustomerEntity { Name = "ZhangSan" };
        // DTO carries [MapFrom], so runtime takes the source-generated path (AOT-safe); the IL warning is only the conservative signal from Mapper.Compose's public UC annotations, known safe
#pragma warning disable IL2026, IL3050
        var detail = Mapper.Compose<OrderDetailDto>(order, customer)!;
#pragma warning restore IL2026, IL3050
        Assert(detail.OrderId == 100 && detail.Amount == 9.9m && detail.CustomerName == "ZhangSan", "Compose");
        Console.WriteLine("[OK] Compose");

        Console.WriteLine("=== 全部 AOT 示例验证通过 ===");
    }
    #endregion


    /// <summary>
    /// 断言辅助 — 失败抛出异常使进程以非零码退出（NativeAOT 冒烟检测）
    /// </summary>
    #region AotSample_EntryValidation_Program_Assert
    static void Assert(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"验证失败: {name}");
    }
}
    #endregion


/// <summary>
/// 用户实体 — 源类型
/// </summary>
    #region AotSample_EntryValidation_UserEntity
public class UserEntity
{
    #endregion

    /// <summary>
    /// 主键
    /// </summary>
    #region AotSample_EntryValidation_UserEntity_Id
    public int Id { get; set; }
    #endregion

    /// <summary>
    /// 名称
    /// </summary>
    #region AotSample_EntryValidation_UserEntity_Name
    public string Name { get; set; } = "";
    #endregion

    /// <summary>
    /// 邮箱
    /// </summary>
    #region AotSample_EntryValidation_UserEntity_Email
    public string Email { get; set; } = "";
}
    #endregion


/// <summary>
/// 用户 DTO — 标 [MapFrom] 走源生成器路径（AOT 安全）
/// </summary>
    #region AotSample_EntryValidation_UserDto
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    #endregion

    /// <summary>
    /// 主键
    /// </summary>
    #region AotSample_EntryValidation_UserDto_Id
    public int Id { get; set; }
    #endregion

    /// <summary>
    /// 名称
    /// </summary>
    #region AotSample_EntryValidation_UserDto_Name
    public string Name { get; set; } = "";
    #endregion

    /// <summary>
    /// 邮箱
    /// </summary>
    #region AotSample_EntryValidation_UserDto_Email
    public string Email { get; set; } = "";
}
    #endregion


/// <summary>
/// 订单实体 — Compose 源
/// </summary>
    #region AotSample_EntryValidation_OrderEntity
public class OrderEntity
{
    #endregion

    /// <summary>
    /// 订单标识
    /// </summary>
    #region AotSample_EntryValidation_OrderEntity_OrderId
    public int OrderId { get; set; }
    #endregion

    /// <summary>
    /// 金额
    /// </summary>
    #region AotSample_EntryValidation_OrderEntity_Amount
    public decimal Amount { get; set; }
}
    #endregion


/// <summary>
/// 客户实体 — Compose 源
/// </summary>
    #region AotSample_EntryValidation_CustomerEntity
public class CustomerEntity
{
    #endregion

    /// <summary>
    /// 客户名称
    /// </summary>
    #region AotSample_EntryValidation_CustomerEntity_Name
    public string Name { get; set; } = "";
}
    #endregion


/// <summary>
/// 订单详情 DTO — Compose 目标（标 [MapFrom] + [MapProperty] 走源生成路径）
/// </summary>
    #region AotSample_EntryValidation_OrderDetailDto
[MapFrom(typeof(OrderEntity))]
public class OrderDetailDto
{
    #endregion

    /// <summary>
    /// 订单标识
    /// </summary>
    #region AotSample_EntryValidation_OrderDetailDto_OrderId
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderId))]
    public int OrderId { get; set; }
    #endregion

    /// <summary>
    /// 金额
    /// </summary>
    #region AotSample_EntryValidation_OrderDetailDto_Amount
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.Amount))]
    public decimal Amount { get; set; }
    #endregion

    /// <summary>
    /// 客户名称
    /// </summary>
    #region AotSample_EntryValidation_OrderDetailDto_CustomerName
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
    public string CustomerName { get; set; } = "";
    #endregion
}
