<!-- 来源：NCMapper 主技术文档 §6.4-6.5 record 类型与枚举映射 -->
### 6.4 record 类型

NCMapper 完整支持 C# record 类型映射。record 通过主构造器定义属性，无无参构造器，NCMapper 自动匹配构造器参数名到源属性，生成 `new T(arg1, arg2, ...)` 形式的映射代码。

#### 构造器参数匹配规则

| 规则    | 说明                                                 |
| ----- | -------------------------------------------------- |
| 匹配方式  | 构造器参数名 ↔ 目标属性名，`OrdinalIgnoreCase` 大小写不敏感          |
| 构造器选择 | 参数最多的公共构造器（`OrderByDescending(Parameters.Length)`） |
| 未匹配参数 | 有显式默认值用默认值；引用类型用 `null`；值类型用 `default(T)`          |
| 类型转换  | 同类型直接赋值；隐式转换用 cast；其他走 `TypeConvertHelper`         |

```csharp
public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}

// record 主构造器参数名自动匹配源属性（大小写不敏感）
[MapFrom(typeof(UserEntity))]
public record UserDto(int Id, string Name, DateTime CreatedAt);

// 映射 — 源生成器生成: new UserDto(src.Id, src.Name, src.CreatedAt)
var dto = Mapper.Map<UserEntity, UserDto>(entity);
```

#### 额外属性（主构造器之外）

record 可以同时拥有主构造器参数和额外的可写属性。构造器参数通过 `new T(...)` 赋值，额外属性在构造后通过 `dst.Prop = value` 赋值：

```csharp
[MapFrom(typeof(UserEntity))]
public record UserDto(int Id, string Name)
{
    public string? Email { get; set; }       // 额外属性，构造后赋值
    public DateTime MappedAt { get; set; }   // 额外属性，构造后赋值
}

// 源生成器生成代码（伪代码）：
// var dst = new UserDto(src.Id, src.Name);
// dst.Email = src.Email;
// dst.MappedAt = src.MappedAt;
```

> **init-only 属性**（`{ get; init; }`）：源生成器在 object initializer 路径中支持（`new T { Prop = value }`）；构造器路径中 init-only 属性若非构造器参数则跳过（无法在构造后赋值）。

#### 继承

record 继承（`record B(int X) : A(Y)`）时，基类属性通过继承链正常匹配。`FindProperty` 沿 `BaseType` 向上搜索，构造器参数匹配覆盖继承属性：

```csharp
public record BaseEntity(int Id, DateTime CreatedAt);

[MapFrom(typeof(UserEntity))]
public record UserDto(int Id, DateTime CreatedAt, string Name) : BaseEntity(Id, CreatedAt);
// 基类参数 Id、CreatedAt 和自有参数 Name 均从源属性匹配
```

#### 源生成器 vs 运行时

| 维度    | 源生成器                          | 运行时引擎                                             |
| ----- | ----------------------------- | ------------------------------------------------- |
| 识别方式  | 无公共无参构造器 → 构造器路径              | `GetConstructor(Type.EmptyTypes) == null` → 构造器路径 |
| 构造器选择 | 参数最多的公共构造器                    | 全部匹配优先；否则选匹配数最多的                                  |
| 代码生成  | `new T(arg1, arg2, ...)` 字面代码 | `Expression.New(ctor, args[])` 表达式                |
| 额外属性  | 构造后逐属性赋值                      | 构造后 Block 赋值                                      |
| 首次延迟  | 零                             | 首次 Expression.Compile()                           |

#### DeepCopy 差异

record 的深拷贝通过构造器参数传递深拷贝后的值：

```csharp
// 源生成器生成（伪代码）：
// var dst = new UserDto(src.Id, Mapper.DeepCopy(src.Address), src.Name);
```

> ℹ️ **record 分类回退**：record 的 dst 在构造器内部创建，无法在构造前预登记到循环引用检测表，因此构造器路径先天不支持循环引用图。源生成器会在**编译期自动分类**属性图（三值分类 Acyclic/Cyclic/Unknown），一旦分类为 **Cyclic 或 Unknown**（含自引用 `A.Self = A`、双向引用 `A↔B`、或含 object/接口等静态不可解析边）即**自动生成** `Mapper.DeepCopyRuntimeOnly<T>` 回退代码走运行时路径（含完整检测），并发出 **NCMAP012** 警告。Acyclic record 仍走源生成器快路径。用户照常调用 `Mapper.DeepCopy(recordObj)` 即可得到正确结果，**无需手工干预**。详见[限制与注意事项 §18.6](../limitations.md)。

#### 完整示例

```csharp
// 源实体
public class CtOrderEntity
{
    public int Id { get; set; }
    public string OrderNo { get; set; }
    public decimal Amount { get; set; }
    public CtCustomerEntity? Customer { get; set; }
}

// record DTO — 主构造器 + 额外属性 + 嵌套 record
[MapFrom(typeof(CtOrderEntity))]
public record CtOrderDto(int Id, string OrderNo, decimal Amount)
{
    public CtCustomerDto? Customer { get; set; }   // 嵌套对象，构造后赋值
    public string? Remark { get; set; }          // 额外属性
}

[MapFrom(typeof(CtCustomerEntity))]
public record CtCustomerDto(string Name, string Email);  // 嵌套 record
```

映射三形态（正向 / 列表 / 反向 record→class）的形态展示——生成形态为
`var dst = new CtOrderDto(src.Id, src.OrderNo, src.Amount);` + 嵌套子映射 + 额外属性赋值：

[!code-csharp[record 构造器绑定巡览：Map / MapList / 反向 Map 三形态](~/samples/NCMapper.Samples/01-Fluent/CpModels.cs#CtorParam_RecordCtorTour)]

> **已知限制（故意反例）**：上区反向映射 `back = Mapper.Map<CtOrderDto, CtOrderEntity>` 是**零调用**的形态展示（巡览区不挂 `Run()`）——`CtCustomerDto` 未声明反向映射，真调用会抛 `MapConfigurationException`；校验报告已以 Error 级条目 `CtOrderEntity→CtOrderDto Customer` 呈现该注册场面缺口。口径警示：**不挂 `Run()` 只保证不被执行，不保证注册场面与打印计数零变化（声明式注册表是全局面）**——本例即「永不被执行因而永不被测」观察窗口的首个实质实例。

### 6.5 枚举映射

NCMapper 支持枚举类型与字符串、数值、其他枚举之间的自动转换，源生成器在编译期生成 switch 表达式（零反射），运行时引擎通过 `Expression.Switch` 或 `TypeConvertHelper` 处理。

#### 支持的转换方向

| 方向               | 源生成器实现                                    | 运行时实现                          |
| ---------------- | ----------------------------------------- | ------------------------------ |
| enum → string    | switch 表达式，每个成员 → 字符串字面量                  | `Expression.Switch` 整数跳转       |
| string → enum    | switch 表达式（大小写无关：`ToLowerInvariant` 后匹配），非法值抛 `MapException` | `Enum.Parse(ignoreCase: true)` |
| enum → enum（异类型） | 按值强转 `(TargetEnum)src`                    | `Expression.Convert`           |
| enum → 数值        | fallback `TypeConvertHelper.ConvertValue` | `Convert.ChangeType`           |
| 数值 → enum        | fallback `TypeConvertHelper.ConvertValue` | `Enum.ToObject`                |

#### 源生成器代码生成

```csharp
public enum OrderStatus { Pending, Processing, Shipped, Delivered }

public class OrderEntity
{
    public OrderStatus Status { get; set; }       // 枚举
    public OrderStatus? Priority { get; set; }    // 可空枚举
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public string Status { get; set; }            // enum → string
    public string? Priority { get; set; }         // enum? → string
}
```

源生成器为 `enum → string` 生成 switch 表达式：

```csharp
// 生成代码
dst.Status = src.Status switch
{
    OrderStatus.Pending => "Pending",
    OrderStatus.Processing => "Processing",
    OrderStatus.Shipped => "Shipped",
    OrderStatus.Delivered => "Delivered",
    _ => src.Status.ToString(),   // 兜底（[Flags] 组合值走此分支）
};

// 可空枚举 → string（null 安全）
dst.Priority = src.Priority switch
{
    OrderStatus.Pending => "Pending",
    OrderStatus.Processing => "Processing",
    OrderStatus.Shipped => "Shipped",
    OrderStatus.Delivered => "Delivered",
    _ => src.Priority?.ToString() ?? "",
};
```

`string → enum` 生成反向 switch：

```csharp
// 生成代码
dst.Status = src.Status switch
{
    "Pending" => OrderStatus.Pending,
    "Processing" => OrderStatus.Processing,
    "Shipped" => OrderStatus.Shipped,
    "Delivered" => OrderStatus.Delivered,
    _ => throw new NCMapper.MapException($"无法将 '{src.Status}' 映射为 {nameof(OrderStatus)}：无匹配枚举成员"),
};
```

`enum → enum`（不同枚举类型）按值强转：

```csharp
// 生成代码
dst.Status = (TargetEnum)src.Status;
```

#### NCMAP010 诊断 — 枚举成员无重叠

当源枚举和目标枚举的**成员名称无任何交集**时，源生成器输出 NCMAP010 警告：

```csharp
public enum Color { Red, Green, Blue }
public enum Size { Small, Medium, Large }   // 与 Color 无名称重叠

[MapFrom(typeof(Entity))]
public class Dto
{
    // ⚠️ NCMAP010: 源枚举 'Color' 和目标枚举 'Size' 的成员名称无任何重叠，
    //    按值强转可能导致非预期结果
    public Size Color { get; set; }
}
```

> 有名称重叠时不触发（如 `Color.Red` 和 `TargetColor.Red` 存在交集）。此诊断仅针对 enum→enum 按值强转场景，enum→string / string→enum 不触发。

#### [Flags] 枚举

[Flags] 枚举无特殊处理，与普通枚举使用相同的 switch 表达式。组合值（如 `Read | Write = 3`）不匹配任何单成员 case，落入默认分支：

| 路径               | 组合值处理                 | 结果                  |
| ---------------- | --------------------- | ------------------- |
| 源生成器 enum→string | 默认分支 `src.ToString()` | `"Read, Write"`（正确） |
| 运行时 enum→string  | `source.ToString()`   | `"Read, Write"`（正确） |
| 源生成器 string→enum | 无匹配 → **抛 `MapException`**       | 第一个成员（可能非预期）        |

> [Flags] 组合值的 string→enum 建议使用 Action 回调或 `[Convert]` 显式处理。

#### 可空枚举

`Enum?` 类型自动处理：源生成器使用 `GetUnderlyingType` 去除 `Nullable<T>` 包装后判断底层枚举，生成 null 安全的 switch 表达式（见上方 `Priority` 示例）。

可空→非可空走通用 Nullable 处理路径：`src.HasValue ? src.Value : default`。


#### 源生成器 vs 运行时关键差异

| 场景                   | 源生成器                                | 运行时                                        |
| -------------------- | ----------------------------------- | ------------------------------------------ |
| string → enum 大小写    | **大小写无关**（`ToLowerInvariant` 后 switch 精确匹配；非法值抛 `MapException`） | **不区分大小写**（`Enum.Parse(ignoreCase: true)`） |
| 无匹配 string → enum    | **抛 `MapException`**（不允许静默取默认值）        | 空字符串返回 `default`；非法字符串抛 `MapException`     |
| enum → 数值            | fallback `TypeConvertHelper`（运行时转换） | `Convert.ChangeType`（等效）                   |
| [Flags] 组合值 → string | 默认分支 `ToString()`（结果正确）             | `ToString()`（结果正确）                         |


#### 完整示例

```csharp
public enum OrderStatus { Pending, Processing, Shipped, Delivered }
public enum OrderStatusDto { Pending, Processing, Shipped, Delivered, Cancelled }

public class OrderEntity
{
    public OrderStatus Status { get; set; }
    public int StatusCode { get; set; }           // 数值
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public string Status { get; set; }            // enum → string（switch 表达式）
    public OrderStatusDto StatusCode { get; set; } // int → enum（TypeConvertHelper）
}

// 反向：string → enum
[MapFrom(typeof(OrderDto))]
public class OrderEntity2
{
    public OrderStatus Status { get; set; }       // string → enum（switch 表达式）
}
```

