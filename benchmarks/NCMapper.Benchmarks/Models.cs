using NCMapper.Attributes;
using MemoryPack;
using MessagePack;

namespace NCMapper.Benchmarks.Models;

// ================================================================
// 枚举定义
// ================================================================

public enum OrderStatus
{
    Pending = 0,
    Processing = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4
}

// ================================================================
// 1. 扁平对象（含枚举字段）
// ================================================================

public class FlatEntity
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "Benchmark Test";
    public int Age { get; set; } = 30;
    public decimal Salary { get; set; } = 50000.5m;
    public DateTime CreatedAt { get; set; } = new(2024, 1, 1);
    public bool IsActive { get; set; } = true;
    public double Score { get; set; } = 95.5;
    public long UserId { get; set; } = 1234567890L;
    public Guid Guid { get; set; } = Guid.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Shipped;
}

[MapFrom(typeof(FlatEntity))]
public class FlatDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public decimal Salary { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public double Score { get; set; }
    public long UserId { get; set; }
    public Guid Guid { get; set; }
    public OrderStatus Status { get; set; }
}

// ================================================================
// 2. 嵌套对象
// ================================================================

[MemoryPackable]
[MessagePackObject]
public partial class InnerEntity
{
    [Key(0)] public string City { get; set; } = "Shanghai";
    [Key(1)] public string Street { get; set; } = "Nanjing Road";
    [Key(2)] public int ZipCode { get; set; } = 200001;
}

public class NestedEntity
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "Parent";
    public InnerEntity? Child { get; set; }
}

[MapFrom(typeof(InnerEntity))]
public class InnerDto
{
    public string City { get; set; } = "";
    public string Street { get; set; } = "";
    public int ZipCode { get; set; }
}

[MapFrom(typeof(NestedEntity))]
public class NestedDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public InnerDto? Child { get; set; }
}

// ================================================================
// 3. 异名字段
// ================================================================

public class NameDiffEntity
{
    public int Id { get; set; } = 100;
    public string Name { get; set; } = "NameDiff";
    public int Age { get; set; } = 25;
    public DateTime Created { get; set; } = new(2024, 6, 1);
    public decimal Amount { get; set; } = 999.99m;
}

[MapFrom(typeof(NameDiffEntity))]
public class NameDiffDto
{
    public int Id { get; set; }

    [MapProperty(typeof(NameDiffEntity), nameof(NameDiffEntity.Name))]
    public string FullName { get; set; } = "";

    [MapProperty(typeof(NameDiffEntity), nameof(NameDiffEntity.Age))]
    public int UserAge { get; set; }

    [MapProperty(typeof(NameDiffEntity), nameof(NameDiffEntity.Id))]
    public long AccountId { get; set; }

    public DateTime Created { get; set; }

    [MapProperty(typeof(NameDiffEntity), nameof(NameDiffEntity.Amount))]
    public decimal TotalAmount { get; set; }
}

// 10 属性异名 — 验证方法体长度对 JIT 内联的影响
public class NameDiffBigEntity
{
    public int Id { get; set; } = 100;
    public string Name { get; set; } = "NameDiff";
    public int Age { get; set; } = 25;
    public DateTime Created { get; set; } = new(2024, 6, 1);
    public decimal Amount { get; set; } = 999.99m;
    public bool IsActive { get; set; } = true;
    public double Score { get; set; } = 95.5;
    public long UserId { get; set; } = 1234567890L;
    public Guid Guid { get; set; } = Guid.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Shipped;
}

[MapFrom(typeof(NameDiffBigEntity))]
public class NameDiffBigDto
{
    [MapProperty(typeof(NameDiffBigEntity), nameof(NameDiffBigEntity.Id))]
    public int Key { get; set; }

    [MapProperty(typeof(NameDiffBigEntity), nameof(NameDiffBigEntity.Name))]
    public string FullName { get; set; } = "";

    [MapProperty(typeof(NameDiffBigEntity), nameof(NameDiffBigEntity.Age))]
    public int UserAge { get; set; }

    [MapProperty(typeof(NameDiffBigEntity), nameof(NameDiffBigEntity.Created))]
    public DateTime CreatedAt { get; set; }

    [MapProperty(typeof(NameDiffBigEntity), nameof(NameDiffBigEntity.Amount))]
    public decimal TotalAmount { get; set; }

    [MapProperty(typeof(NameDiffBigEntity), nameof(NameDiffBigEntity.IsActive))]
    public bool Enabled { get; set; }

    [MapProperty(typeof(NameDiffBigEntity), nameof(NameDiffBigEntity.Score))]
    public double Grade { get; set; }

    [MapProperty(typeof(NameDiffBigEntity), nameof(NameDiffBigEntity.UserId))]
    public long AccountId { get; set; }

    [MapProperty(typeof(NameDiffBigEntity), nameof(NameDiffBigEntity.Guid))]
    public Guid Uid { get; set; }

    [MapProperty(typeof(NameDiffBigEntity), nameof(NameDiffBigEntity.Status))]
    public OrderStatus OrderState { get; set; }
}

// ================================================================
// 8. 枚举→字符串转换
// ================================================================

public class EnumEntity
{
    public int Id { get; set; } = 1;
    public OrderStatus Status { get; set; } = OrderStatus.Processing;
    public string Name { get; set; } = "Enum";
}

[MapFrom(typeof(EnumEntity))]
public class EnumToStringDto
{
    public int Id { get; set; }
    public string Status { get; set; } = "";  // enum → string
    public string Name { get; set; } = "";
}

// ================================================================
// 9. record 构造器映射
// ================================================================

public record RecordEntity(int Id, string Name, int Age, decimal Salary, DateTime CreatedAt);

[MapFrom(typeof(RecordEntity))]
public record RecordDto(int Id, string Name, int Age, decimal Salary, DateTime CreatedAt);

// 10 参数 record — 验证 JIT 内联策略对方法体长度的影响
public record RecordBigEntity(int Id, string Name, int Age, decimal Salary, DateTime CreatedAt,
    bool IsActive, double Score, long UserId, Guid Guid, OrderStatus Status);

[MapFrom(typeof(RecordBigEntity))]
public record RecordBigDto(int Id, string Name, int Age, decimal Salary, DateTime CreatedAt,
    bool IsActive, double Score, long UserId, Guid Guid, OrderStatus Status);

// ================================================================
// 10. 可空类型转换
// ================================================================

public class NullableEntity
{
    public int Id { get; set; } = 1;
    public int? Score { get; set; } = 85;
    public DateTime? UpdatedAt { get; set; } = new(2024, 6, 1);
    public string Name { get; set; } = "Nullable";
}

[MapFrom(typeof(NullableEntity))]
public class NullableDto
{
    public int Id { get; set; }
    public int Score { get; set; }          // int? → int
    public DateTime UpdatedAt { get; set; } // DateTime? → DateTime
    public string Name { get; set; } = "";
}

// ================================================================
// 11. 自动扁平化
// ================================================================

public class FlattenChildEntity
{
    public string Name { get; set; } = "ChildName";
    public int Level { get; set; } = 3;
    public string City { get; set; } = "Beijing";
    public string Street { get; set; } = "Rd";
    public int ZipCode { get; set; } = 100000;
    public bool IsActive { get; set; } = true;
    public double Score { get; set; } = 95.5;
    public long UserId { get; set; } = 1234567890L;
    public Guid Guid { get; set; } = Guid.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Shipped;
}

public class FlattenParentEntity
{
    public int Id { get; set; } = 1;
    public FlattenChildEntity? Child { get; set; }
}

[MapFrom(typeof(FlattenParentEntity))]
public class FlattenDto
{
    public int Id { get; set; }
    public string ChildName { get; set; } = "";
    public int ChildLevel { get; set; }
    public string ChildCity { get; set; } = "";
    public string ChildStreet { get; set; } = "";
    public int ChildZipCode { get; set; }
    public bool ChildIsActive { get; set; }
    public double ChildScore { get; set; }
    public long ChildUserId { get; set; }
    public Guid ChildGuid { get; set; }
    public OrderStatus ChildStatus { get; set; }
}

// ================================================================
// 12. DeepCopy 深拷贝测试模型
// ================================================================

/// <summary>无 [MapFrom] — 走 P0 FastDeepCopyProvider 路径</summary>
[MemoryPackable]
[MessagePackObject]
public partial class DeepCopyEntity
{
    [Key(0)] public int Id { get; set; } = 1;
    [Key(1)] public string Name { get; set; } = "DeepCopy";
    [Key(2)] public InnerEntity? Child { get; set; }
    [Key(3)] public List<int> Tags { get; set; } = new() { 1, 2, 3 };
}

/// <summary>有 [MapFrom] — 走 P1 源生成器 DeepCopy 路径</summary>
[MemoryPackable]
[MessagePackObject]
[MapFrom(typeof(DeepCopySourceEntity))]
public partial class DeepCopyMapFromDto
{
    [Key(0)] public int Id { get; set; } = 1;
    [Key(1)] public string Name { get; set; } = "DeepCopy";
    [Key(2)] public DeepCopyChildEntity? Child { get; set; }
    [Key(3)] public List<int> Tags { get; set; } = new() { 1, 2, 3 };
}

/// <summary>DeepCopyMapFromDto 的源实体（不标注 [MapFrom]）</summary>
[MemoryPackable]
[MessagePackObject]
public partial class DeepCopySourceEntity
{
    [Key(0)] public int Id { get; set; } = 1;
    [Key(1)] public string Name { get; set; } = "DeepCopy";
    [Key(2)] public DeepCopyChildEntity? Child { get; set; }
    [Key(3)] public List<int> Tags { get; set; } = new() { 1, 2, 3 };
}

/// <summary>DeepCopyMapFromDto 的子对象（无 [MapFrom]，Child 走 P0 递归）</summary>
[MemoryPackable]
[MessagePackObject]
public partial class DeepCopyChildEntity
{
    [Key(0)] public string City { get; set; } = "Beijing";
    [Key(1)] public string Street { get; set; } = "Chang'an Ave";
    [Key(2)] public int ZipCode { get; set; } = 100000;
}

// ================================================================
// 13. 无 [MapFrom] 对照组 — 走表达式树（MapperBase）路径
// 与上方有 [MapFrom] 的模型结构完全一致，仅去掉 [MapFrom] 特性
// ================================================================

/// <summary>Flat 对照 — 无 [MapFrom]，走表达式树</summary>
public class FlatEntityRuntime
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "Benchmark Test";
    public int Age { get; set; } = 30;
    public decimal Salary { get; set; } = 50000.5m;
    public DateTime CreatedAt { get; set; } = new(2024, 1, 1);
    public bool IsActive { get; set; } = true;
    public double Score { get; set; } = 95.5;
    public long UserId { get; set; } = 1234567890L;
    public Guid Guid { get; set; } = Guid.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Shipped;
}

/// <summary>Flat DTO 对照 — 无 [MapFrom]</summary>
public class FlatDtoRuntime
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public decimal Salary { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public double Score { get; set; }
    public long UserId { get; set; }
    public Guid Guid { get; set; }
    public OrderStatus Status { get; set; }
}

/// <summary>Nested 对照 — 无 [MapFrom]，走表达式树</summary>
public class NestedEntityRuntime
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "Parent";
    public InnerEntity? Child { get; set; }
}

public class NestedDtoRuntime
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public InnerDto? Child { get; set; }
}

/// <summary>Record 对照 — 无 [MapFrom]，走表达式树构造器匹配</summary>
public record RecordEntityRuntime(int Id, string Name, int Age, decimal Salary, DateTime CreatedAt);

public record RecordDtoRuntime(int Id, string Name, int Age, decimal Salary, DateTime CreatedAt);

/// <summary>Nullable 对照 — 无 [MapFrom]，走表达式树</summary>
public class NullableEntityRuntime
{
    public int Id { get; set; } = 1;
    public int? Score { get; set; } = 85;
    public DateTime? UpdatedAt { get; set; } = new(2024, 6, 1);
    public string Name { get; set; } = "Nullable";
}

public class NullableDtoRuntime
{
    public int Id { get; set; }
    public int Score { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>NameDiff 对照 — 无 [MapFrom]，走表达式树</summary>
public class NameDiffEntityRuntime
{
    public int Id { get; set; } = 100;
    public string Name { get; set; } = "NameDiff";
    public int Age { get; set; } = 25;
    public DateTime Created { get; set; } = new(2024, 6, 1);
    public decimal Amount { get; set; } = 999.99m;
}

public class NameDiffDtoRuntime
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public int UserAge { get; set; }
    public long AccountId { get; set; }
    public DateTime Created { get; set; }
    public decimal TotalAmount { get; set; }
}

/// <summary>Bidir 反向对照 — 无 [MapFrom]，走表达式树</summary>
public class FlatDtoRuntimeReverse
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "Test";
    public int Age { get; set; } = 30;
    public decimal Salary { get; set; } = 5000m;
    public DateTime CreatedAt { get; set; } = new(2024, 1, 1);
    public bool IsActive { get; set; } = true;
    public double Score { get; set; } = 99.5;
    public long UserId { get; set; } = 123456;
    public Guid Guid { get; set; } = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffffffffffff");
    public OrderStatus Status { get; set; } = OrderStatus.Shipped;
}

public class FlatEntityRuntimeReverse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public decimal Salary { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public double Score { get; set; }
    public long UserId { get; set; }
    public Guid Guid { get; set; }
    public OrderStatus Status { get; set; }
}

/// <summary>Action 对照 — 无 [MapFrom]，走表达式树</summary>
public class ActionEntityRuntime
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "Action";
    public int Age { get; set; } = 30;
    public decimal Salary { get; set; } = 5000m;
    public DateTime CreatedAt { get; set; } = new(2024, 1, 1);
    public bool IsActive { get; set; } = true;
    public double Score { get; set; } = 99.5;
    public long UserId { get; set; } = 123456;
    public Guid Guid { get; set; } = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffffffffffff");
    public OrderStatus Status { get; set; } = OrderStatus.Shipped;
}

public class ActionDtoRuntime
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public decimal Salary { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public double Score { get; set; }
    public long UserId { get; set; }
    public Guid Guid { get; set; }
    public OrderStatus Status { get; set; }
}

/// <summary>EnumString 对照 — 无 [MapFrom]，走表达式树</summary>
public class EnumEntityRuntime
{
    public int Id { get; set; } = 1;
    public OrderStatus Status { get; set; } = OrderStatus.Processing;
    public string Name { get; set; } = "Enum";
}

public class EnumToStringDtoRuntime
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>AutoFlatten 对照 — 无 [MapFrom]，走表达式树</summary>
public class FlattenParentEntityRuntime
{
    public int Id { get; set; } = 1;
    public FlattenChildEntity? Child { get; set; }
}

public class FlattenDtoRuntime
{
    public int Id { get; set; }
    public string ChildName { get; set; } = "";
    public int ChildLevel { get; set; }
    public string ChildCity { get; set; } = "";
    public string ChildStreet { get; set; } = "";
    public int ChildZipCode { get; set; }
    public bool ChildIsActive { get; set; }
    public double ChildScore { get; set; }
    public long ChildUserId { get; set; }
    public Guid ChildGuid { get; set; }
    public OrderStatus ChildStatus { get; set; }
}

// ================================================================
// 14. Compose 多源合成 — 3 源 DTO（订单 + 客户 + 产品）
// ================================================================

/// <summary>Compose 主源 — 订单实体</summary>
public class ComposeOrderSource
{
    public int OrderId { get; set; } = 1001;
    public decimal OrderAmount { get; set; } = 299.99m;
}

/// <summary>Compose 子源 — 客户实体</summary>
public class ComposeCustomerSource
{
    public string CustomerName { get; set; } = "Alice";
    public int CustomerLevel { get; set; } = 5;
}

/// <summary>Compose 子源 — 产品实体</summary>
public class ComposeProductSource
{
    public string ProductName { get; set; } = "Widget";
    public int Stock { get; set; } = 200;
}

/// <summary>3 源合成 DTO — 通过 [MapProperty] 从订单/客户/产品合成</summary>
[MapFrom(typeof(ComposeOrderSource))]
public class ComposeMultiSourceDto
{
    [MapProperty(typeof(ComposeOrderSource), nameof(ComposeOrderSource.OrderId))]
    public int OrderId { get; set; }

    [MapProperty(typeof(ComposeOrderSource), nameof(ComposeOrderSource.OrderAmount))]
    public decimal OrderAmount { get; set; }

    [MapProperty(typeof(ComposeCustomerSource), nameof(ComposeCustomerSource.CustomerName))]
    public string CustomerName { get; set; } = "";

    [MapProperty(typeof(ComposeCustomerSource), nameof(ComposeCustomerSource.CustomerLevel))]
    public int CustomerLevel { get; set; }

    [MapProperty(typeof(ComposeProductSource), nameof(ComposeProductSource.ProductName))]
    public string ProductName { get; set; } = "";

    [MapProperty(typeof(ComposeProductSource), nameof(ComposeProductSource.Stock))]
    public int Stock { get; set; }
}

