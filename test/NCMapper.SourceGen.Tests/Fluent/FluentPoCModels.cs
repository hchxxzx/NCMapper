using NCMapper;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// Fluent 通道 PoC 运行时模型（验收 #1/#2/#7）——
/// 本文件随测试工程编译即被源生成器处理（测试工程引用 SourceGen 为 Analyzer），
/// 生成的 GeneratedMapper 方法编译进测试程序集供运行时断言（P5 注册链路验证）。
/// </summary>

// ── Pair A：显式声明 + Ignore + NoAutoInclude + AfterMapping（验收 #7 全要素）──
/// <summary>PoC 源实体（订单）</summary>
public class FluentOrder
{
    /// <summary>订单号</summary>
    public int OrderId { get; set; }

    /// <summary>客户名（NoAutoInclude 场景下未显式声明 → 不映射）</summary>
    public string CustomerName { get; set; } = "";

    /// <summary>内部备注（Ignore → 不映射）</summary>
    public string InternalNote { get; set; } = "";

    /// <summary>总额（NoAutoInclude 场景下未显式声明 → 不映射）</summary>
    public decimal Total { get; set; }
}

/// <summary>PoC 目标 DTO（订单）</summary>
public class FluentOrderDto
{
    /// <summary>订单号</summary>
    public int OrderId { get; set; }

    /// <summary>客户名</summary>
    public string CustomerName { get; set; } = "";

    /// <summary>内部备注</summary>
    public string InternalNote { get; set; } = "";

    /// <summary>总额</summary>
    public decimal Total { get; set; }

    /// <summary>AfterMapping 副作用载体（运行时断言）</summary>
    public string? ComputedTag { get; set; }
}

/// <summary>
/// Fluent 通道入口（PoC）：显式 Map + Ignore + NoAutoInclude + AfterMapping
/// </summary>
[MapperConfig]
public class FluentOrderMapper
{
    /// <summary>Fluent 链式声明（方法名任意——A1.5）</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FluentOrder, FluentOrderDto>()
           .Member(d => d.OrderId).From(s => s.OrderId)
           .Member(d => d.InternalNote).Ignore()
           .NoAutoMatch()
           .AfterMapping(ApplyComputedTag);
    }

    /// <summary>AfterMapping 目标（public static——可见性契约 B-1b）</summary>
    public static void ApplyComputedTag(FluentOrder src, FluentOrderDto dst)
    {
        dst.ComputedTag = "after:" + src.OrderId;
    }
}

// ── Pair B：纯自动匹配（默认开——S-3a）+ 反向派生验证 ──
/// <summary>PoC 源实体（类目）</summary>
public class FluentCategory
{
    /// <summary>Id</summary>
    public int Id { get; set; }

    /// <summary>名称</summary>
    public string Name { get; set; } = "";
}

/// <summary>PoC 目标 DTO（类目）</summary>
public class FluentCategoryDto
{
    /// <summary>Id</summary>
    public int Id { get; set; }

    /// <summary>名称</summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// Fluent 通道入口（PoC）：空链体——仅声明类型对，全部属性自动匹配
/// </summary>
[MapperConfig]
public class FluentCategoryMapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FluentCategory, FluentCategoryDto>()
           .Member(d => d.Id).From(s => s.Id);
    }
}

// ── Pair C：Convert 跨类型（DateTime→string，S-1a 双泛型）──
/// <summary>PoC 源实体（事件）</summary>
public class FluentEvent
{
    /// <summary>时间戳</summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>PoC 目标 DTO（事件）</summary>
public class FluentEventDto
{
    /// <summary>显示时间（string ← DateTime 跨类型转换）</summary>
    public string DisplayTime { get; set; } = "";
}

/// <summary>
/// Fluent 通道入口（PoC）：Map 双泛型跨类型转换（路径式 Convert 并入 Map）
/// </summary>
[MapperConfig]
public class FluentEventMapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FluentEvent, FluentEventDto>()
           .Member(d => d.DisplayTime).From(s => s.Timestamp);
    }
}

// ── Pair D：Mask 脱敏（S-2a 必带 mask type）──
/// <summary>PoC 源实体（用户）</summary>
public class FluentUser
{
    /// <summary>姓名</summary>
    public string Name { get; set; } = "";

    /// <summary>手机号（脱敏目标）</summary>
    public string Phone { get; set; } = "";
}

/// <summary>PoC 目标 DTO（用户）</summary>
public class FluentUserDto
{
    /// <summary>姓名</summary>
    public string Name { get; set; } = "";

    /// <summary>手机号（脱敏后）</summary>
    public string Phone { get; set; } = "";
}

/// <summary>
/// Fluent 通道入口（PoC）：Mask&lt;MaskPhone, string&gt; 脱敏（下游 MaskProvider 共享）
/// </summary>
[MapperConfig]
public class FluentUserMapper
{
    /// <summary>Fluent 链式声明</summary>
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<FluentUser, FluentUserDto>()
           .Member(d => d.Phone).Mask<MaskPhone>();
    }
}
