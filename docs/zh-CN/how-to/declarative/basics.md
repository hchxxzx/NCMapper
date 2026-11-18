---
wiki-id: declarative/basics
涉及诊断码: NCMAP001, NCMAP002, NCMAP005, NCMAP019
库版本: v1.0.0+
更新时间: 2026-09-29
---

# 基础映射（Attribute 通道）

## 概述

基础映射是 NCMapper 最核心的能力：把一个对象的属性值复制到另一个对象。NCMapper 提供双引擎自动选择最优路径：

- **源生成器引擎**：编译时为每个 `[MapFrom]` 类型对生成静态映射方法，零委托查表，JIT 可内联
- **运行时表达式树引擎**：未注册的类型对在首次调用时编译表达式树并缓存

两者结果一致，差异仅在调用路径。用户无需关心走哪条路径——`Mapper.Map<TSource, TTarget>(src)` 会自动选择。

> **声明式双通道**：「特性通道」= DTO 上标注 `[MapFrom]` 族特性；「Fluent 通道」= `[MapperConfig]` 配置类内用 `FluentMappingConfig` 链式声明。两通道等价并存：**不同类型对可自由混用**；**同一对类型（无论哪个方向）只能由一条通道声明一次**，双声明编译期报 `NCMAP_F11`（无序互指由 `NCMAP025` 同族把守）。本篇示例以特性通道为主，Fluent 等价写法与全部终端见 [Fluent 入口](../fluent/basics.md)。

---

## 1. 白名单匹配（[MapNoAutoMatch]）

默认同名自动匹配之外，特性通道提供对级白名单开关 `[MapNoAutoMatch]`：标注后仅显式声明的 `[MapProperty]` 参与映射，未列入白名单的同名属性一律不映射（与 Fluent 侧 `.NoAutoMatch()` 等价，后者由 101 场景 `101-no-auto-match` 片段演示）。适用于「只要显式声明的字段、防止新增同名属性被误映射」的收窄场景。

## 1. 单对象映射与列表

### 用法

定义了 `[MapFrom]` 的目标类型，调用 `Mapper.Map` 即可完成同名属性复制。

```csharp
public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}
```

单对象映射——

[!code-csharp[单对象映射](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

列表映射——

[!code-csharp[列表映射](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ListMap)]

带 Action 回调：映射后立即修改目标——

[!code-csharp[带 Action 回调的映射](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapWithAction)]

更新已有对象：不创建新实例——

[!code-csharp[更新已有对象](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_UpdateExisting)]

同一对映射声明在 Fluent 通道等价为（配置类内链式，无需改 DTO）：

```csharp
[MapperConfig]
public class UserMapperConfig
{
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<UserEntity, UserDto>();      // 与 [MapFrom(typeof(UserEntity))] 等价，二选一
}

// 调用方式不变：Mapper.Map<UserEntity, UserDto>(entity)
```

**要点**：

- `[MapFrom]` 标注在目标类型上，指明源类型
- 同名同类型属性自动映射，无需额外配置
- `MapList` 映射整个列表，返回新列表实例
- `Action` 回调在映射完成后立即执行，避免二次遍历
- 更新已有对象时，源属性覆写目标属性

### 1.1 集合属性自动映射

DTO 与 Entity 有同名集合属性时，元素映射自动发生：

```csharp
public class OrderEntity
{
    public int OrderId { get; set; }
    public List<OrderItemEntity> Items { get; set; } = new();
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public int OrderId { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}
// Items 元素自动从 OrderItemEntity → OrderItemDto
```

集合属性类型可以不同（如 `List<T>` → `IList<U>`），自动转换；Queue/Stack 映射保持元素顺序，`ObservableCollection<T>` 创建新实例可绑定 UI。

**常见误用**：

```csharp
// ❌ List→Dictionary 直接映射不支持（Key 来源不明确）
var dict = Mapper.MapList<UserEntity, UserDto>(entities);  // 返回 List<UserDto>

// ✅ 先 MapList 为 List，再手动 ToDictionary
var list = Mapper.MapList<UserEntity, UserDto>(entities);
var dict = list.ToDictionary(d => d.Id);

// ❌ 映射后修改源集合，期望目标同步变化
dtos = Mapper.MapList<UserEntity, UserDto>(entities);
entities[0].Name = "Modified";  // dtos[0].Name 不会变——映射创建了独立副本
```

完整的集合类型矩阵与引擎内部机制见 [映射规则（语义）集合与字典](../../explanation/semantics/mapping-rules.md)。

> ⚠️ 版本差异：编译期映射代码需要**映射声明锚**（`[MapFrom]` 标注，或 Fluent 配置类里的 `For<S,T>()` 声明——两者任一即告诉 NCMapper「这对类型存在映射声明」）。DTO 无锚时运行时表达式树引擎仍可工作，不会报错；但若类上还带了属性级映射配置标注（如 `[MapProperty]`/`[Convert]`），这些标注**一律不生效**（无锚不消费）且编译期报 **NCMAP019 Error**；纯未声明（无任何标注）仅提示 **NCMAP042 Info**（Dto 命名启发式）。

---

## 2. 异名字段与自定义转换

### 用法

当源和目标属性名不一致时，用 `[MapProperty]` 指明对应关系；需要多字段组合或格式化时，用 `[Convert]`。

> **类级锚前提（映射配置跟随类锚）**：属性级标注（`[MapProperty]`/`[Convert]`/`[MapIgnore]` 等）只在**所在类有映射声明锚**时生效——锚 = 本类上的 `[MapFrom]` 标注，或 Fluent 配置类里对本类参与类型对的 `For<S,T>()` 声明。无锚时属性级标注一律不认（编译期报 `NCMAP019`）。下例的 `UserDetailDto` 已标注 `[MapFrom]`，故其属性级标注生效。

```csharp
[MapFrom(typeof(UserEntity))]
public class UserDetailDto
{
    // 异名映射：源 Name → 目标 DisplayName
    [MapProperty(typeof(UserEntity), nameof(UserEntity.Name))]
    public string DisplayName { get; set; } = "";

    // 枚举 → 字符串转换
    [MapProperty(typeof(UserEntity), nameof(UserEntity.Status))]
    public string StatusText { get; set; } = "";

    // DateTime → 日期字符串（Convert + MapProperty 组合）
    [MapProperty(typeof(UserEntity), nameof(UserEntity.CreatedAt))]
    [Convert(typeof(DateTimeFormatter), nameof(DateTimeFormatter.Convert))]
    public string RegisterDate { get; set; } = "";

    // 独立 Convert：无对应源属性，从 source 参数组合
    [Convert(typeof(FullNameConverter), nameof(FullNameConverter.Convert))]
    public string FullName { get; set; } = "";
}
```

**要点**：

- `[MapProperty(typeof(T), nameof(T.Field))]` 的第一个类型参数 `T` 是**字段实际取数来源的源类型**：等于 `[MapFrom]` 主源即同源映射（最常见）；等于另一源类型即跨表/Compose 映射，**无需**与主源一致，也不会因此报错
- `[Convert]` 可独立使用（无对应源属性），也可与 `[MapProperty]` 组合
- 转换方法是静态方法（T0–T3 签名模板自动识别），声明于转换器类即可复用
- 枚举到字符串的转换是内置类型转换，无需额外 Convert

### 2.1 反向自定义转换（[ConvertBack]）

`[Convert]` 只服务正向（Entity→DTO）；反向（DTO→Entity）需要自定义转换时，在 DTO 属性上标注 `[ConvertBack(typeof(转换器类), nameof(转换器类.方法))]`。与 `[Convert]` 同属性并列合法（正向走 `[Convert]`、反向走 `[ConvertBack]`，互不干扰）：

```csharp
// 正向转换器：DateTime → "yyyy-MM-dd"（T1 模板）
public static class DateFormatter
{
    public static string Convert(DateTime fieldValue) => fieldValue.ToString("yyyy-MM-dd");
}

// 反向转换器：字符串 → DateTime 解析还原（T1 模板）
public static class DateParser
{
    public static DateTime Convert(string fieldValue)
        => DateTime.ParseExact(fieldValue, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    [MapProperty(typeof(UserEntity), nameof(UserEntity.CreatedAt))]
    [Convert(typeof(DateFormatter), nameof(DateFormatter.Convert))]      // 正向：格式化
    [ConvertBack(typeof(DateParser), nameof(DateParser.Convert))]        // 反向：解析还原
    public string CreatedDate { get; set; } = "";
}
```

要点：

- 方法签名模板（T0–T3）与访问性要求与 `[Convert]` 一致（见 [映射规则（语义）类型转换链](../../explanation/semantics/mapping-rules.md)）
- `string → DateTime` 等解析类转换**没有内置支持**（格式与文化差异需业务决策），一律走自定义转换方法
- 转换方法应为纯函数（无副作用），否则映射结果依赖调用顺序、难以测试

**常见误用**：

```csharp
// ❌ 方法为 private：NCMAP035 Error（锚定类型上不存在可访问的静态方法）
public class BadConverter
{
    private static string Convert(int value) => "";
}

// ❌ 单参形态 nameof(OtherType.M) 截断：nameof 编译产物仅为 "M"，OtherType 被静默丢弃；
//    方法位于其他类型时务必改用双参形态 [Convert(typeof(OtherType), nameof(OtherType.M))]
```

---

### 2.2 JSON 列映射（IsFromJson）

当数据库用 JSON 字符串列存储结构化对象时，用 `[MapProperty(IsFromJson = true)]` 实现 **对象 ↔ JSON 字符串** 双向转换，无需手动序列化/反序列化。

```csharp
public class ProductEntity
{
    public int Id { get; set; }
    // 数据库中的 JSON 字符串列
    public string? SpecJson { get; set; }
}

// string(源) → 对象(目标)：反序列化 JSON
[MapFrom(typeof(ProductEntity))]
public class ProductDto
{
    [MapProperty(typeof(ProductEntity), nameof(ProductEntity.SpecJson), IsFromJson = true)]
    public ProductSpec? Spec { get; set; }
}

// 对象(源) → string(目标)：序列化为 JSON
[MapFrom(typeof(ProductDto))]
public class ProductEntity2
{
    [MapProperty(typeof(ProductDto), nameof(ProductDto.Spec), IsFromJson = true)]
    public string? SpecJson { get; set; }
}
```

**行为规则**：

- 仅支持 **对象 ↔ string** 双向：对象侧必须是复杂引用类型（非 `string`、非值类型）
- `string → string`：`IsFromJson` 无实际效果，直接赋值
- 值类型参与的组合不支持：自动跳过该属性，并通过 `MapTracer` 输出跳过原因（开启 `GloballyEnabled` 时可见）
- JSON 为空或 `null`：目标返回 `default`（值类型返回 `default(T)`，引用类型返回 `null`）
- 畸形 JSON：反序列化失败抛 `MapException`（包装底层 `JsonException`）

**要点**：

- `IsFromJson` 会触发 `RequiresDowngrade = true`——ORM 投影（`AsDto`）遇到该特性会自动**降级到内存映射**，因为 SQL 无法表达 JSON 反序列化
- 序列化策略可替换：见 [§2.3 JSON 序列化策略可替换](#23-json-序列化策略可替换)

---

### 2.3 JSON 序列化策略可替换

NCMapper 的 JSON 中转（`IsFromJson` 映射 + JSON 深拷贝）默认使用 `System.Text.Json`。可通过 `JsonSerializerBase.Current` 替换为其他实现（如 Newtonsoft.Json）：

```csharp
public class NewtonsoftJsonSerializer : JsonSerializerBase
{
    public override string Serialize(object? obj) =>
        Newtonsoft.Json.JsonConvert.SerializeObject(obj);
    public override object? Deserialize(string json, Type targetType) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject(json, targetType);
}

// 全局替换
JsonSerializerBase.Current = new NewtonsoftJsonSerializer();
```

**要点**：

- `JsonSerializerBase.Current` 是 `static` 全局实例，默认 `SystemTextJsonSerializer`
- 替换后，所有 JSON 中转映射与 JSON 深拷贝都使用新实例
- 仅通过代码配置（非 `NCMapperOptions`）
- ⚠️ **AOT 注意**：`JsonSerializerBase` 的 `Serialize`/`Deserialize` 标注了 `[RequiresDynamicCode]` 与 `[RequiresUnreferencedCode]`——反射式 JSON 序列化在 NativeAOT 下不可用，需改用源生成的 `JsonSerializerContext`

> **相关 API**：`JsonConverter`——引擎侧 JSON 互转辅助类（表达式树 Lambda 与源生成代码经它完成对象 ↔ JSON 字符串转换，内部转发 `JsonSerializerBase.Current`；替换 Current 即替换其底层序列化实现）（源码锚：JsonConverter.cs:8）。

---

## 3. PascalCase 扁平化

### 用法

目标属性名按 PascalCase 拆分后，自动匹配源对象的嵌套路径。

```csharp
public class UserEntity
{
    public Address? Address { get; set; }
}
public class Address { public string City { get; set; } public string Street { get; set; } }

[MapFrom(typeof(UserEntity))]
public class UserDetailDto
{
    public string AddressCity { get; set; } = "";    // → src.Address?.City
    public string AddressStreet { get; set; } = "";  // → src.Address?.Street
}
```

**要点**：

- 拆分规则：`AddressCity` → `Address.City`
- 源生成器 / ORM 投影 / 运行时 `Mapper.Map` 均支持扁平化，也支持同名匹配
- 源生成器编译时生成 `src.Address?.City` 表达式，null 安全

> ⚠️ TFM 差异：PascalCase 扁平化在所有 TFM 下行为一致。源生成器编译时处理（生成 `src.Address?.City` null 安全链）；运行时 `Mapper.Map` 与 ORM 投影经共享 `BuildPathCandidates` 算法处理（段匹配 OrdinalIgnoreCase，仅正向）。

### 3.1 多级嵌套扁平化

PascalCase 扁平化支持 2-5 段路径，自动生成 null-conditional（`?.`）链式访问：

```csharp
public class OrderEntity
{
    public Customer? Customer { get; set; }
}
public class Customer
{
    public Address? Address { get; set; }
}
public class Address
{
    public string City { get; set; } = "";
}

[MapFrom(typeof(OrderEntity))]
public class OrderDetailDto
{
    // 2 段：src.Customer?.Address
    public string? CustomerAddress { get; set; }

    // 3 段：src.Customer?.Address?.City
    public string? CustomerAddressCity { get; set; }

    // 4 段：src.Customer?.Address?.City?.Length（值类型叶节点自动 ?? default）
    public int CustomerAddressCityLength { get; set; }
}
```

**拆分规则**：

- 位掩码枚举切分：属性名按大写字母边界拆分（如 `CustomerAddressCity` → `Customer` + `Address` + `City`）
- 支持路径段数 2~5 段，超出 5 段放弃拆分
- 连续大写字母超过 4 个（如 `HTTPResponse`）放弃拆分，避免误拆缩写词
- 值类型叶节点自动补充 `?? default(int)`，防止 null 引发异常
- **方向限制**：正向为读路径（嵌套路径 → 扁平属性）；反向（DTO→Entity）为**写回**（扁平属性 → 嵌套路径赋值）：中间节点 null 时 new 创建、源值 null 整体跳过、[Convert] 例外（输出 null 不写）；集合索引/接口中间节点跳过 + 诊断/Trace

> ⚠️ **与 Projection 的关系**：ORM 投影（AsDto）不支持 `?.` 表达式翻译为 SQL。投影路径下需用导航属性 + `[MapProperty]` 替代扁平化。

---

## 4. 双向映射与方向控制

### 用法

`[MapFrom]` 标注后正反向都可使用，无需重复配置。`[MapIgnore]` 控制特定属性只在指定方向参与映射。

```csharp
[MapFrom(typeof(CustomerEntity))]
public class CustomerDto
{
    public string Name { get; set; } = "";
    public string Level { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Region { get; set; } = "";

    // 反向映射（DTO → Entity）时忽略此属性
    [MapIgnore(Direction.ToEntity)]
    public string DisplayLabel { get; set; } = "";
}
```

正向映射（可运行版本，samples 场景 201 `ForwardMap`）——

[!code-csharp[正向映射：Entity → DTO](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ForwardMap)]

反向验证的可运行版本（samples 场景 201 `BackwardMap`，`dotnet run --scenario 201`；DisplayLabel 不会写入 Entity）：

[!code-csharp[反向映射验证：\[MapIgnore(Direction.ToEntity)\] 生效](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapIgnore)]

**要点**：

- `Direction` 枚举：`ToDto`（Entity→DTO 时忽略）、`ToEntity`（DTO→Entity 时忽略）、`Both`（双向忽略）（：ToTarget 为残留旧名，实现为 ToDto）
- 双向映射只需在一方标注 `[MapFrom]`
- **侧别互斥**：`[MapFrom]` 在一个类上至多标注一次（重复标注编译期 CS0579），同一对类型全局只有一个锚；Fluent 侧对应 `For<A, B>` 与 `For<B, A>` 不得同时声明（`NCMAP025` 检测）

---

## 5. 源生成器扩展方法

### 用法

源生成器为每个 `[MapFrom]` 类型对自动生成扩展方法（`NCMapper.Generated` 命名空间），零委托直调。

```csharp
using NCMapper.Generated;

var dto = entity.MapToUserDto();                          // 创建新对象
var dto = entity.MapToUserDto((src, tgt) => { ... });     // 带回调
entity.MapToUserDto(existingDto);                         // 更新已有对象（void）
```

**调用路径对比**：

| 调用方式 | 路径 | 性能 |
|---|---|---|
| `Mapper.Map<UserEntity, UserDto>(entity)` | 经委托注册表查找 → 调用生成代码 | ~17-29ns |
| `entity.MapToUserDto()` | 直接调用生成的静态方法，零委托 | ~13-27ns |

两者结果完全一致，差异仅在调用路径。扩展方法省去一次字典查找，适合热路径场景。

### 5.1 [ModuleInitializer] 预注册

源生成器生成一个 `GeneratedMapperInitializer` 类，使用 `[ModuleInitializer]` 特性在模块加载时自动注册所有映射委托到 `Mapper` 静态类：

```csharp
// 源生成器自动生成（用户无需编写；CodeGenerator.GenerateModuleInitializer）
[System.Runtime.CompilerServices.ModuleInitializer]
internal static void Initialize()
{
    NCMapper.Mapper.RegisterGeneratedCore<UserEntity, UserDto>(GeneratedMapper.Map_UserEntity_To_UserDto);
    NCMapper.Mapper.RegisterGenerated<UserEntity, UserDto>(GeneratedMapper.Map_UserEntity_To_UserDto_WithAction);
    // ... 所有 [MapFrom] 类型对（方法名模板：Map_{源}_To_{目标}）
}
```

**要点**：

- `[ModuleInitializer]` 在程序集加载时自动执行，无需在 `Program.cs` 中手动注册
- 这也是 `Mapper.Map<T,S>(src)` 能走源生成器路径的原因——预注册后委托查找表已有映射代码
- `Mapper.ClearCache()` 仅清除运行时缓存，不影响源生成器预注册的委托
- 扩展方法与 `Mapper.Map` 走同一份源生成器映射代码，性能基本等价

### 5.2 源生成器扩展方法全集

每个 `[MapFrom]` 类型对生成 6 个扩展方法：

| 方法 | 说明 | 返回类型 |
|---|---|---|
| `src.MapToXxxDto()` | 创建新对象 | `XxxDto` |
| `src.MapToXxxDto(Action<src,dto>)` | 带回调创建 | `XxxDto` |
| `src.MapToXxxDto(dto target)` | 更新已有对象 | `void` |
| `src.MapToXxxDto(dto target, Action<src,dto>)` | 带回调更新 | `void` |
| `srcs.MapToXxxDtoList()` | 列表映射 | `List<XxxDto>` |
| `srcs.MapToXxxDtoList(Action<src,dto>)` | 带回调列表映射 | `List<XxxDto>` |

> **反向直调（DTO → Entity）**：`[MapFrom]` DTO 同样生成反向 6 重载扩展
> （`dto.MapToXxxEntity()` / `MapToXxxEntity(Action)` / `MapToXxxEntity(entity)` 更新 / `MapToXxxEntity(entity, Action)` /
> `dtos.MapToXxxEntityList()` / `MapToXxxEntityList(Action)`），形态与上表对称，方法名 = `MapTo{正向源类型名}`。
> 声明跨表子源字段的 DTO 同样生成，反向只覆盖主源字段（[Compose §5.1](../compose/compose.md)）。

[!code-csharp[正反双向更新：零堆分配 + CRUD 写回](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#Declarative_ReverseMap_Pair)]

> 💡 **性能提示**：CRUD 场景推荐使用 `MapToXxxDto(target)` 更新已有对象，避免创建新对象的堆分配。源生成 `_Update` 方法直接调用生成的静态方法，零委托 + 零堆分配。

---

## 常见误用

### 误用 1：DTO 无映射声明（或带标注无锚）

```csharp
// ❌ 误用 A：DTO 未声明映射，源生成器不会为其生成映射代码
public class UserDto { ... }
var dto = Mapper.Map<UserEntity, UserDto>(entity);
// → NCMAP042 Info: UserDto 命名像 DTO 但未声明映射（Dto 命名启发式提示）

// ❌ 误用 B：类上带属性级配置标注却无映射声明锚
public class UserDto
{
    [MapProperty(typeof(UserEntity), nameof(UserEntity.Name))]
    public string DisplayName { get; set; } = "";
}
// → NCMAP019 Error: 映射配置标注所在类无映射声明锚（无锚不消费，标注不生效）

// ✅ 正确：标注 [MapFrom(typeof(源类型))]，或在 Fluent 配置类声明 For<S,T>()
[MapFrom(typeof(UserEntity))]
public class UserDto { ... }
```

### 误用 2：[MapProperty] 类型不兼容

```csharp
// ❌ 误用：目标属性类型与源字段类型不兼容
[MapFrom(typeof(UserEntity))]
public class UserDetailDto
{
    [MapProperty(typeof(UserEntity), nameof(UserEntity.Name))]
    public int DisplayName { get; set; }  // string → int 无法赋值
}
// → NCMAP002 Warning: 目标属性类型(int)与源字段类型(string)不兼容

// ✅ 正确：目标属性类型与源字段类型兼容
[MapProperty(typeof(UserEntity), nameof(UserEntity.Name))]
public string DisplayName { get; set; } = "";
```

> 💡 说明：`[MapProperty]` 第一个类型参数只需是**字段实际取数来源的源类型**——等于 `[MapFrom]` 主源即同源映射（最常见）；等于另一源类型即为**跨表 / Compose 映射**（例如从 Compose 的第二源取字段），这是完全受支持的用法，**不会**因"≠ `[MapFrom]` 源"而报错。NCMAP002 仅在目标属性类型与源字段类型无法赋值时触发，与"第一参数是否等于 `[MapFrom]` 源"无关；字段名在对应源类型中不存在时，同源报 NCMAP001、跨表报 NCMAP005。

> NCMAP001 检测 `[MapProperty]` 指定的源字段在源类型中不存在（编译期 Warning）；同源字段不存在报 NCMAP001、跨表字段不存在报 NCMAP005。NCMAP002 检测目标属性类型与 `[MapProperty]` 指定的源字段类型不兼容（无法赋值）。无映射声明锚的配置标注触发 NCMAP019（Error，无锚不消费）；纯未声明仅 NCMAP042（Info）。详见 [诊断码全索引](../../diagnostics/rules.md)。

### 误用 3：PascalCase 展平属性反向映射

```csharp
// ❌ 误用：含 PascalCase 展平属性的 DTO 反向映射
[MapFrom(typeof(UserEntity))]
public class UserDetailDto
{
    public string AddressCity { get; set; } = "";  // 展平属性
}

// 反向映射时，AddressCity 找不到 UserEntity 的对应属性，静默跳过
var entity = Mapper.Map<UserDetailDto, UserEntity>(dto);

// ✅ 正确：展平属性仅用于正向映射；需要反向时声明对象属性
[MapFrom(typeof(UserEntity))]
public class UserDetailDto
{
    public Address? Address { get; set; }  // 对象属性，可双向映射
}
```

### 误用 4：担心 [MapIgnore] 影响正向映射性能

`[MapIgnore]` 在映射表达式构建阶段就已确定，不产生运行时分支——加多少个都不影响运行时性能，它只有逻辑控制作用。

**多种机制同时作用于同一属性时的优先级**（忽略 > 自定义转换 > 异名路由 > 同名匹配）与反向映射完整契约见 [映射规则（语义）](../../explanation/semantics/mapping-rules.md)。

---

## 小结

| 能力 | API | 特性 |
|---|---|---|
| 单对象映射 | `Mapper.Map<TSource, TTarget>(src)` | `[MapFrom]` |
| 列表映射 | `Mapper.MapList<TSource, TTarget>(srcs)` | `[MapFrom]` |
| 回调 | `Mapper.Map(src, Action)` | — |
| 更新已有对象 | `Mapper.Map(src, target)` | — |
| 异名字段 | — | `[MapProperty]` |
| 自定义转换 | — | `[Convert]` 静态方法引用 |
| JSON 列映射 | `[MapProperty(IsFromJson = true)]` | 对象↔string 双向，触发投影降级 |
| JSON 序列化策略 | `JsonSerializerBase.Current` | 可替换为 Newtonsoft.Json |
| 扁平化 | — | PascalCase 命名约定（源生成器） |
| 双向映射 | 正反向 `Mapper.Map` | `[MapFrom]` |
| 方向控制 | — | `[MapIgnore(Direction)]` |
| 零委托直调 | `src.MapToXxxDto()` | 源生成器扩展方法 |

---

## 配套示例

本文示例代码位于 `samples/NCMapper.Samples/02-Declarative/`：

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 201
```

核心示例源码（单点同源，`dotnet run --scenario` 可运行验证）：

[!code-csharp[单对象映射](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

[!code-csharp[更新已有对象](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_UpdateExisting)]

[!code-csharp[反向直调六重载巡览](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ReverseExtensions)]

核心逻辑见 `BasicScenarios.cs`，每个静态方法对应一个子场景。

---

## 进一步阅读

- 相关篇目：[映射规则（语义）](../../explanation/semantics/mapping-rules.md) · [advanced](advanced.md)
- 技术文档深挖：[全集式技术文档](../../reference/features.md) 第 5 章
