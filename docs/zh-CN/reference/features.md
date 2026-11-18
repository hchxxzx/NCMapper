---
title: 特性系统
---

<!-- 来源：NCMapper 主技术文档 §5 特性系统（结构重组，2026-08-19 章级迁移，内容未重排） -->
## 5. 特性系统

> **声明式双通道**：本篇描述 **特性通道**（DTO 类上标注 `[MapFrom]`/`[MapProperty]`/`[Convert]` 等特性族）；NCMapper 另提供 **Fluent 通道**——`[MapperConfig]` 配置类内 `FluentMappingConfig` 链式声明（Member 三段式 + `Tree`/`NoAutoMatch` 等对级终端），两通道长期并存（同类型对互斥 NCMAP_F11），详见 [Fluent 入口](../how-to/fluent/basics.md)。

### 5.1 [MapFrom] — 类级标注

```csharp
[MapFrom(typeof(UserEntity))]
public class UserDto { ... }
```

- 标注在 DTO 类上，声明主映射源类型
- 每个 DTO 只能使用一次
- 仅对 class 有效
- 双向映射：NCMapper 基于属性名匹配，标注在一方即可双向映射。例如 UserDto 标注 `[MapFrom(typeof(User))]`，`Map<User, UserDto>` 和 `Map<UserDto, User>` 均可工作，无需在 User 上再标注

### 5.2 [MapProperty] — 属性级标注

```csharp
[MapProperty(typeof(OtherEntity), nameof(OtherEntity.OtherName))]
public string Name { get; set; }
```

| 参数           | 说明                             |
| ------------ | ------------------------------ |
| `fromSource` | 字段来源的类型（可与主源类型不同）              |
| `fromField`  | 源类型中的字段名称                      |
| `IsFromJson` | 是否通过 JSON 序列化/反序列化中转（默认 false） |

**IsFromJson 详解**

当数据库使用 JSON 文本列存储结构化数据时，`IsFromJson = true` 让映射引擎自动完成 string ↔ 对象的转换：

```csharp
// Entity — 数据库中 ShippingAddressJson 列存储 '{"City":"Shanghai","Street":"..."}'
public class OrderEntity
{
    public int Id { get; set; }
    public string ShippingAddressJson { get; set; }  // JSON 文本列
    public string TagsJson { get; set; }             // JSON 文本列
}

// DTO — 反序列化为强类型
[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public int Id { get; set; }

    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.ShippingAddressJson), IsFromJson = true)]
    public ShippingAddress? ShippingAddress { get; set; }  // string → 对象

    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.TagsJson), IsFromJson = true)]
    public List<string>? Tags { get; set; }                // string → 集合
}
```

行为规则：

- **正向**（string 源 → 引用类型目标）：调用 `JsonSerializer.Deserialize` 将 JSON 文本反序列化为目标对象。JSON 格式非法或反序列化过程中发生不可恢复错误时抛出 `JsonException`（数据损坏应被及时发现，不静默吞掉）；属性级别的多余/缺失由序列化器默认策略处理，映射层不做额外校验。
- **反向**（引用类型源 → string 目标）：调用 `JsonSerializer.Serialize` 将对象序列化为 JSON 字符串。源为 null 时目标得到空字符串。
- **string → string**：直接赋值，`IsFromJson` 不产生额外效果。
- **值类型不支持**：值类型（`int`、`DateTime`、`Guid` 等）出现在任一侧时，该属性被跳过（保持 default），并输出 Trace 诊断信息。值类型字段应使用同名映射或 `[MapProperty]` 异名映射，无需 JSON 中转。
- **null 守卫**：正向（string 源 → 引用类型目标）时，若源 JSON 文本为 `null` 或空白（`string.IsNullOrWhiteSpace`），目标直接赋 `null`，不进入反序列化流程（避免将空白误判为无效 JSON 而抛出 `JsonException`）；反向（引用类型源 → string 目标）时源为 `null` 得到空字符串（同前）。
- **ORM 投影降级**：`IsFromJson` 字段不可翻译为 SQL，EFCore/FreeSql 投影遇到时自动降级为内存映射（二步查询）。
- **序列化策略可替换**：默认使用 `System.Text.Json`，可通过替换 `JsonSerializerBase.Current` 切换为其他序列化方案。

**字符串路径支持与强类型写法：**

`[MapProperty]` 的 `fromField` 为字符串参数，**完整支持字符串路径**：简单属性名（`"DeptName"`）、嵌套点路径（`"Address.City"`）、"类型.属性"组合均可用，源生成器 / 运行时 / 投影 / Compose（SourceGen）四引擎已对齐。字符串路径提供动态灵活性；同时推荐 `typeof` + `nameof` 的强类型写法，两者在映射语义上等价：

- `typeof(OtherEntity)` 确保来源类型在编译时检查，类型重命名时自动跟随
- `nameof(OtherEntity.OtherName)` 确保属性名在编译时检查，属性重命名时自动跟随
- 字符串路径（如 `"OtherEntity.OtherName"`）无编译期属性检查，重构时需人工同步——因此作为**首选推荐**的是强类型写法，字符串路径适用于动态/简化场景

> ⚠️ **点路径行为边界**：`[MapProperty]` 的 `fromField` 支持**简单属性名**（如 `"DeptName"`）与 **`xx.xx` 点路径**（如 `"Address.City"`，四引擎对齐：源生成器 / 运行时 / 投影 / Compose(SourceGen)）。点路径失败为**显性可观测**：源生成器触发 **NCMAP001**（普通）/ **NCMAP005**（Compose 跨表）**Warning 且不生成该映射**，诊断带失败段名与所在类型；运行时抛 **MapConfigurationException**（显性失败）；投影侧跳过该属性并输出 Trace。跨表 / 嵌套导航仍可用 `[MapProperty(typeof(NavType), "Field")]` 显式指定导航实体类型。详见 [§18.3 Projection 限制](../explanation/limitations.md)。

类型 + 属性名的组合自然形成了"类型.属性"的映射语义，同时保留了强类型可靠性。

### 5.3 [MapIgnore] — 方向控制

```csharp
public enum Direction { Both, ToDto, ToEntity }

[MapIgnore]                           // 双向忽略
[MapIgnore(Direction.ToDto)]          // 仅 Entity→DTO 时忽略
[MapIgnore(Direction.ToEntity)]       // 仅 DTO→Entity 时忽略
public string InternalField { get; set; }
```

> **生成侧方向判定（双侧）**：源生成器对 `[MapIgnore]` 的方向判定同时检查**源侧与目标侧**——`Direction.ToEntity` 忽略"Entity→DTO"方向（DTO 属性不被 Entity 填充），`Direction.ToDto` 反之。双标组合（如 `[MapIgnore(Direction.ToDto)]` 叠加 `[MapProperty]` 反向声明）时，生成侧按两侧声明的方向分别裁剪，避免旧版"仅看单侧导致反向映射误忽略"的问题。

### 5.4 [Convert] — 自定义值转换（静态方法引用）

标注在属性上，指定映射时调用**静态转换方法**替换默认转换路径：

```csharp
// 单字段转换 — 模板 T1：M(TField)
public static class DateTimeToStringConverter
{
    public static string Convert(DateTime fieldValue) => fieldValue.ToString("yyyy-MM-dd");
}

// 多字段组合 — 模板 T2：M(TSource)
// FullName = FirstName + " " + LastName
public static class FullNameCalculator
{
    public static string Convert(UserEntity source)
        => $"{source.FirstName} {source.LastName}";
}

// 标注在属性上（双参形态，推荐）
[Convert(typeof(DateTimeToStringConverter), nameof(DateTimeToStringConverter.Convert))]
public string CreatedAt { get; set; }

// 单参形态：方法须声明于标注属性所属类型自身
[Convert(nameof(Convert))]
public string FullName { get; set; }
```

**签名模板**（自动识别）：

| 模板 | 签名 | 适用场景 |
|---|---|---|
| T0 | `M()` | 常量/固定值 |
| T1 | `M(TField)` | 单个源字段值（可跨实体复用） |
| T2 | `M(TSource)` | 从源对象组合多个字段 |
| T3 | `M(TSource, TField)` | 组合 + 字段值本身 |

| 概念                                          | 说明                                         |
| ------------------------------------------- | ------------------------------------------ |
| 锚定类型 | 声明静态转换方法的类型（双参形态指定）；单参形态 = 标注属性所属类型      |
| `[Convert]`                            | 属性级特性，指定转换器静态方法（typeof + nameof 双参 / nameof 单参） |
| `fieldValue`                                | 匹配到的源字段值（无匹配源属性时传 `default(TField)`）       |
| `source`                                    | 整个源实体对象（用于多字段组合、条件映射等场景）                   |
| 执行顺序                                        | 优先于类型兼容性检查、隐式转换、TypeConvertHelper fallback |

**方法解析规则** — 方法必须为 `static`、声明于锚定类型自身（不含基类继承的静态方法）、可访问（`public` / `internal` 同程序集 / `protected internal` 同程序集可见性）。重载须唯一命中模板：零命中 → **NCMAP037**；多重载命中 → **NCMAP036**；不存在或不可访问 → **NCMAP035**（Error，编译阻断）。不提供按签名选择重载的构造形态——请重命名或移除冲突重载。

> **nameof 截断警示（单参形态）**：`nameof(OtherType.M)` 编译产物仅为 `"M"`，OtherType 被静默丢弃——方法位于其他类型时务必改用双参形态 `[Convert(typeof(X), nameof(X.M))]`，否则 NCMAP035 报错并给出改写指引（不静默错配）。

**使用意义** — 默认转换路径只能处理同名同类型自动映射、隐式转换和 `TypeConvertHelper` 兜底；当目标属性需要**自定义逻辑**（格式化、跨字段组合、条件映射、单位换算等）时默认路径无法表达。`[Convert]` 提供声明式扩展点：转换逻辑封装为静态方法，编译期可见、可复用、类型安全，且被源生成器编译期生成直接调用代码。

**典型场景**：

| 场景     | 示例                                      |
| ------ | --------------------------------------- |
| 格式化    | `DateTime` → `"yyyy-MM-dd"` 字符串         |
| 多字段组合  | `FullName = FirstName + " " + LastName` |
| 枚举↔字符串 | 枚举带本地化描述                                |
| 加密/脱敏  | 密码哈希、手机号掩码                              |
| 单位换算   | 摄氏→华氏、分→元                               |
| 条件映射   | 根据 `source.Status` 决定目标值                |
| 跨字段计算  | `BMI = Weight / (Height/100)²`          |
| 纯计算属性  | 目标属性在源无对应字段，完全靠 `source` 计算             |

**无源属性场景** — 目标属性在源实体无匹配字段时，标注 `[Convert]` 的转换方法**仍会被调用**，`fieldValue` 传 `default(TField)`（T1/T3 模板），转换方法通过 `source` 参数访问实体字段实现纯计算。上例 `FullNameCalculator` 即属此场景（`FullName` 在源无对应字段）。

**与 Action 回调的取舍**：

| 维度    | `[Convert]` | Action 回调     |
| ----- | ---------------- | ------------- |
| 风格    | 声明式，封装为静态方法       | 命令式，内联 lambda |
| 复用    | 一处定义，多处标注        | 一次性，绑定单次调用    |
| 编译期检查 | 源生成器诊断方法存在性/可访问性/签名 | 无             |
| 性能    | 源生成器内联静态直调       | 运行时委托         |

> 可复用的通用转换逻辑（日期格式化、单位换算等）用 `[Convert]`；仅某次映射的特殊处理用 Action。

**性能与 AOT** — 源生成器路径为每个转换点生成**内联静态方法直调**（无单例字段），**零反射、零分配、AOT 友好**。**约束：转换方法应为无状态纯函数**（不得访问可变静态状态）；如需有状态逻辑，改用 Action 回调。

**反向方向（[ConvertBack]）** — `[Convert]` 绑定**正向**方向，反向（DTO → 实体）默认**不消费** `[Convert]`（编译期报 **NCMAP033** Info，含迁移指引），按类型转换路径写入。反向需要自定义转换时，使用对称特性 `[ConvertBack(typeof(X), nameof(X.M))]`：

```csharp
// 反向转换器：T2/T3 模板中 TSource = DTO 类型（反向映射的源对象）
public static class BackConverter
{
    public static decimal Convert(string fieldValue) => decimal.Parse(fieldValue);
}

[ConvertBack(typeof(BackConverter), nameof(BackConverter.Convert))]
public decimal Amount { get; set; }
```

| 标注组合 | 正向（实体 → DTO） | 反向（DTO → 实体） |
|---------|--------------------|--------------------|
| 仅 `[Convert]` | 消费 `[Convert]` | 不消费（NCMAP033），类型转换路径 |
| 仅 `[ConvertBack]` | 类型转换路径 | 消费 `[ConvertBack]` |
| 两者并列 | 消费 `[Convert]` | 消费 `[ConvertBack]` |

- 反向转换方法无法解析或签名不匹配时报 **NCMAP035/036/037**（Error，编译阻断）

### 5.5 [Mask] — 属性级脱敏

标注在 DTO 的 `string` 属性上，指定映射时自动执行脱敏处理。与 `[Convert]` 模式类似（声明式转换点 + 直调），但语义更明确——表达"此字段需要脱敏"而非"此字段需要类型转换"。

```csharp
// 接口定义
public interface IMask
{
    string Mask(string? value);
}

// 标注在属性上
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public string Name { get; set; }

    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; }       // 138****5678

    [Mask(typeof(MaskIdCard))]
    public string IdCard { get; set; }      // 1101**********1234

    [Mask(typeof(MaskEmail))]
    public string Email { get; set; }       // z***@example.com
}
```

#### 内置脱敏实现

| 类名                 | 脱敏规则                                  | 示例                   |
| ------------------ | ------------------------------------- | -------------------- |
| `MaskPhone`        | 保留前 3 后 4，中间固定 `****`                 | `138****5678`        |
| `MaskIdCard`       | 保留前 4 后 4，中间等量 `*`                    | `1101**********1234` |
| `MaskBankCard`     | 保留前 4 后 4，中间固定 `****`                 | `6222****1234`       |
| `MaskEmail`        | 首字符 + `***` + `@`及域名                  | `z***@example.com`   |
| `MaskChineseName`  | 1 字原样；2 字=首+`*`；≥3 字=首+等量`*`+末        | `张*丰`                |
| `MaskAddress`      | 保留前 6 字符 + `****`                     | `北京市朝阳区****`         |
| `MaskIp`           | 保留前 2 段，后 2 段替换为 `*`                  | `192.168.*.*`        |
| `MaskLicensePlate` | 保留前 2 后 2，中间等量 `*`                    | `京A***45`            |
| `MaskLandline`     | 有 `-` 保留区号+`****`+后 4；无 `-` 保留前 3 后 4 | `010-****5678`       |

所有内置类均为 `sealed`、无状态，满足单例零分配约束。

#### 与 [Convert] 正交组合

`[Mask]` 和 `[Convert]` 可标注在同一属性上，执行顺序为**先转换再脱敏**：

```csharp
[Convert(typeof(DateTimeToStringConverter), nameof(DateTimeToStringConverter.Convert))]
[Mask(typeof(MaskPhone))]
public string Phone { get; set; }
// 生成代码：dst.Phone = __mask_Phone.Mask(DateTimeToStringConverter.Convert(src.Phone));
```

> **双标组合语义**：`[Mask]` 与 `[Convert]` 同标时，双引擎（源生成器 / 运行时表达式树）行为一致——均执行"先 `[Convert]` 转换、再 `[Mask]` 脱敏"（同上方生成代码语义）。

#### 方向语义：仅正向参与

`[Mask]` 是**完全正向**的特性——只在实体 → DTO 方向执行脱敏（映射代码中调用 `Mask()`），反向（DTO → 实体）**不执行脱敏**（反向写回代码不含 `Mask()` 调用，与 `[Convert]` 反向不消费同族语义）。

但脱敏**有损且不可逆**：正向映射后 DTO 属性持有的是脱敏值。在"查实体 → 映射 DTO（脱敏）→ 编辑 → 反向写回实体"的典型 CRUD 流程中，反向写回会把 DTO 上的**脱敏值原样写回实体**，覆盖实体中的完整数据（round-trip 有损）——反向虽未执行脱敏，但正向脱敏的后果会经写回传导到源对象。

| 方向 | 行为 | 诊断 |
|------|------|------|
| 正向（实体 → DTO） | 执行脱敏（`Mask()` 调用） | — |
| 反向（DTO → 实体） | **不执行脱敏**（无 `Mask()` 调用）；DTO 当前值（含正向脱敏后的值）原样写回 | **NCMAP034**（Info）"脱敏值将原样写入目标实体，脱敏有损不可逆" |

需要阻断"脱敏值回写实体"时，对相应 DTO 属性标注 `[MapIgnore(Direction.ToEntity)]`（或全局 ToEntity 忽略清单）；也可在 DTO 上保留一个未脱敏字段（如 `[MapIgnore(Direction.ToDto)]`）供写回使用。详见 [反向映射语义 §4](../explanation/reverse-mapping-semantics.md) 与 [脱敏](../how-to/infrastructure/masking.md)。

#### 自定义脱敏

实现 `IMask` 接口即可：

```csharp
public sealed class MaskCustomRule : IMask
{
    // Mask 方法体——可运行版本见下方绑定引用
}

// 使用
[Mask(typeof(MaskCustomRule))]
public string SensitiveField { get; set; }
```

[!code-csharp[自定义 IMask 规则实现（首尾保留、中间打星）](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_MaskCustomRule_Mask)]

#### 动态替换内置实现

`MaskProvider.Override<TMask>()` 允许在应用启动时替换任意内置 `IMask` 实现，**无需修改 DTO 特性标注**。适用于国际化适配（如日本手机号格式 090-\*\*\*\*-1234）、合规策略调整等场景。

[!code-csharp[脱敏提供器启动时注册替换实现（Override）](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_OverrideMask)]

约束与注意事项：

| 约束   | 说明                                                                    |
| ---- | --------------------------------------------------------------------- |
| 调用时机 | 必须在首次映射调用**之前**执行（源生成器的 `static readonly` 字段初始化后不再重新解析）               |
| 替换类型 | 必须实现 `IMask` 接口（`Override<TMask>` 的泛型约束 `where TMask : IMask` 在编译期保证） |
| 线程安全 | `MaskProvider` 内部使用 `Dictionary<Type, IMask>`，`Override` 应在启动阶段单线程调用  |
| 清除 | `MaskProvider.Clear()` 显式清除所有替换注册（供测试用）。`Mapper.ClearCache()` 只清运行时实例缓存、不连带清空 Override 注册——Override 具独立生命周期，防止 PII 静默回退内置脱敏 |

> **内部机制**：源生成器调用 `MaskProvider.Resolve<TMask>()` 而非直接 `new TMask()`。`Resolve` 先查 `_overrides` 字典，命中则返回替换实现，未命中才创建默认实例。详见 [`MaskProvider.cs`](~/src/NCMapper/MaskProvider.cs)。

#### 生效范围

| 路径                      | 行为                                                                                         |
| ----------------------- | ------------------------------------------------------------------------------------------ |
| `Mapper.Map` / 源生成器扩展方法 | 脱敏生效                                                                                       |
| `Mapper.MapList`        | 逐元素脱敏                                                                                      |
| AsDto 投影（DTO 未定义脱敏）     | 走 SQL 投影路径（本路径不调用 CLR 委托，也不涉及脱敏，故无影响）                                                      |
| AsDto 投影（DTO 定义了脱敏）     | **自动降级为二步映射，脱敏全量生效** —— `[Mask]` 触发 `IProjectionPolicy.RequiresDowngrade`，框架强制走降级路径，脱敏一定生效 |
| 反向映射（Entity←DTO）        | 不脱敏                                                                                        |

> **关键结论**：`[Mask]` 实现 `IProjectionPolicy`（`RequiresDowngrade => true`）。**只要 DTO 上定义了脱敏，AsDto 终端方法必然自动切换降级路径执行脱敏，不存在"定义了脱敏、但正常路径下脱敏不生效"的情况。** SQL 投影路径仅在不含 `[Mask]` 等降级特性的 DTO 上采用；一旦定义脱敏，框架会强制降级，脱敏一定生效，无需用户干预。该行为由 FreeSql/EF Core 扩展包的 `*DegradationDetector` 在 `AsDto` 调用时统一检测（`EfCoreDegradationDetector` / `FreeSqlDegradationDetector` 均扫描 `IProjectionPolicy.RequiresDowngrade` 命中即整体降级；Dapper 包无降级检测器——始终走内存映射全管道，Mask 永远生效）。

#### 源生成器行为

源生成器为每个 `[Mask]` 属性生成 `private static readonly` 单例字段 + 直接 `Mask()` 调用，零反射、零额外分配：

```csharp
// 生成代码（简化）
private static readonly MaskPhone __mask_UserDto_Phone = new();

// 赋值处
dst.Phone = __mask_UserDto_Phone.Mask(src.Phone);
```

**编译期诊断**：`[Mask]` 指定的类型未实现 `IMask` 时 emit NCMAP014（Error）；目标属性非 `string` 类型时 emit NCMAP015（Error）。

### 5.6 [MapTree] — 树形字段名自定义

标注在 DTO 类上，指定树形结构对应的标识字段名和父标识字段名。默认值为 `"Id"` / `"ParentId"`：

```csharp
// 默认字段名：Id / ParentId
[MapFrom(typeof(CategoryEntity))]
public class CategoryDto
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public string Name { get; set; }
    public List<CategoryDto> Children { get; set; }
}

// 非默认字段名：通过 [MapTree] 自定义
[MapFrom(typeof(CategoryEntity))]
[MapTree("NodeId", "ParentNodeId")]
public class CategoryDto
{
    public int NodeId { get; set; }
    public int ParentNodeId { get; set; }
    public string Name { get; set; }
    public List<CategoryDto> Children { get; set; }
}
```

**使用场景**：当数据库表的标识列和父标识列不是 `Id` / `ParentId`（如遗留系统的 `CategoryId` / `ParentCategoryId`、多租户系统的 `NodeGuid` / `ParentGuid`）时，无需在 DTO 中强制改字段名，直接通过 `[MapTree]` 声明实际字段名即可。

**默认值行为**：未标注 `[MapTree]` 时，`ToTreeList` 默认查找 `Id` / `ParentId` 字段（区分大小写）。

> **适用边界**：[MapTree] 仅 Dapper `DapperDtoSelector.ToTreeList` 与 EF Core `EFCoreDtoSelector.ToTreeList` 读取。FreeSql `ToTreeList`（`FreeSqlDtoSelector.ToTreeList`）为 FreeSql 原生树构建能力包装，不读 [MapTree]。EF Core `ToTreeList`/`ToTreeListAsync`（`EFCoreDtoSelector`）采用"平铺查询 + 内存建树"，由核心库 `TreeBuilder` 按 Id/ParentId 组装，读 [MapTree]，语义与 Dapper 一致。详见 [§11.4 Dapper 扩展包](../explanation/orm-efcore-dapper.md) 及 [how-to §6 树形查询](../how-to/orm/efcore-projection.md)。

---

### 5.7 映射方向与标注位置规范

8.4 后映射引擎强化了**方向判定**与**标注位置**的语义约束，避免"双向映射时一侧声明影响另一侧"的隐性错误。

#### 方向象限

映射在两个方向独立判定：**ToDto**（源→目标，Entity→DTO）与 **ToEntity**（目标→源，DTO→Entity）。`[MapFrom]` 声明的是"谁是主源"，而方向由调用时的映射流向决定。

- `[MapFrom(typeof(B))]` 仅声明"本类型以 B 为主源"，不限制方向；B→A 与 A→B 两个方向各自独立生成。
- `[MapIgnore(Direction.ToEntity)]` 仅忽略 DTO→Entity 方向，Entity→DTO 方向仍映射该属性。

#### 互指对（NCMAP025，Error 级）

若 `A [MapFrom(B)]` 且 `B [MapFrom(A)]` 同时成立，构成**互指映射对**。映射双向而生成来源唯一，生成侧无法界定单一生成来源 → **该对不生成**并输出 `NCMAP025`（Error，构建失败）；运行时（源生成禁用时）按名匹配兜底。修复：删除一侧 `[MapFrom]` 声明即可恢复生成，或删除一侧改走 `Mapper.Map` 运行时映射。详见[诊断规则](../diagnostics/rules.md) §16。

#### 对侧属性反向匹配

当声明 `A.Prop1 → B.Prop2`（通过 `[MapProperty(typeof(B), "Prop2")]`），引擎在**对侧方向**自动尝试 `B.Prop2 → A.Prop1` 的反向匹配（同名/同类型优先），无需重复标注。若反向无对应属性或类型不兼容，则该方向静默跳过（不影响正向）。

#### 标注位置规范（NCMAP026，Warn 级）

- `[MapFrom]` 标注在**主源类**上（与生成代码归属同侧）。
- `[Mask]` 应标注在**目标侧**属性（与 `[MapFrom]` 同侧）。`[Mask]` 标注在与 `[MapFrom]` 不同侧（源侧）时**不生效**（Mask 语义单向：仅目标 string 脱敏），输出 `NCMAP026` 提示移至规范位置。
- `[MapIgnore]` / `[Convert]` / `[MapProperty]` 的方向判定见 §5.3 / §5.4 / §5.2。

#### IsFromJson Fallback

`[MapProperty(IsFromJson=true)]` 在 ORM 投影（AsDto）中不可翻译为 SQL 时自动降级为内存映射（二步查询），详见 §5.1 与 [ORM 投影设计与降级机制](../explanation/orm-projection-design.md)；若 JSON 文本为 null/空白，正向映射直接赋 null（见 §5.1 null 守卫），不进入反序列化。

---

### 5.8 [MapWhen] — 属性级条件守卫（等价 Fluent `.MapWhen`）

标注在属性上，为该属性声明**条件守卫**：仅当谓词返回 `true` 才执行该属性的映射赋值（真才映射，极性入名）。守卫可锚定自动匹配的同名源属性与显式 `[MapProperty]` 声明。Fluent 等价终端为 `.MapWhen`（双形态），本特性为**仅命名形态**（实参是编译期常量，内联转录形态 Fluent 独有，表达力不对称）。

可运行示例（`dotnet run --scenario 105` 实跑验证；`[MapWhen(nameof(HasContent))]` 属性级、谓词方法声明于 DTO 自身）：

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto)]

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫 · Id](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Id)]

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫 · Name](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Name)]

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫 · Score](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Score)]

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫 · HasContent](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_HasContent)]

行为要点：

- **属性级标注，`Inherited = true`**（随属性继承，与类级钩子特性 `Inherited = false` 不同）；单实例，同一属性不得重复标注
- **谓词解析域**：谓词方法须声明于**被标注属性的声明类型**（单类型自足，与 `[Convert(string)]` 单参形态同规则）
- **谓词签名**：`public/internal static bool M(T)`；单参 == 源实体类型判 T2 整源（谓词读整个源对象），否则 T1 字段值（谓词读源字段值）
- **链路次序**：条件 → 转换 → 赋值（条件看到转换前的源字段值）；条件假跳过赋值（新建路径保持初始化器值、`_Update` 路径保留既有值）
- **反向映射不消费**：守卫仅作用于正向（Entity→DTO）
- **投影行为**：守卫为 CLR 委托路径，SQL 不可翻译，投影恒降级（分类 `MapWhen`）

| 维度 | `[MapWhen]`（特性） | `.MapWhen`（Fluent） |
| --- | --- | --- |
| 标注位置 | 属性级 | 链式终端（锚定目标属性） |
| 谓词形态 | 仅命名方法（形态 A） | 命名方法组 + 无捕获内联 lambda（形态 A/B 双形态） |
| 解析域 | 被标注属性的声明类型 | 方法组可静态解析即合法（跨类宿主可复用） |
| 编译期诊断 | `NCMAP041`（谓词解析失败：方法不存在/多重载歧义/非静态/可见性不足/签名不符） | `NCMAP_F40`~`F45` |

> 完整语义（同锚点组合、单条件、PATCH 惯用法、FromSource 块内守卫）见 [Fluent 四终端 MapWhen 专节](../how-to/fluent/terminals.md)。

### 5.9 [BeforeMapping] / [AfterMapping] — 类级生命周期钩子（等价 Fluent `.BeforeMapping` / `.AfterMapping`）

类级标注的映射流水线钩子：`[BeforeMapping]` 在**目标实例构造后、首条属性赋值前**回调；`[AfterMapping]` 在**全部属性赋值完成后**回调。`Inherited = false`（派生 DTO 不继承钩子、需显式标注，与 `[MapTree]` 同口径）。Fluent 等价终端 `.BeforeMapping(handler)` / `.AfterMapping(handler)` 填充同一快照承载字段（双通道等价）。

可运行示例（`dotnet run --scenario 104` 实跑验证；双参宿主形态 + `[MapFrom]` 锚定）：

[!code-csharp[特性侧双钩子（双参宿主形态 + \[MapFrom\] 锚定）](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto)]

[!code-csharp[特性侧双钩子（双参宿主形态 + \[MapFrom\] 锚定） · Id](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Id)]

[!code-csharp[特性侧双钩子（双参宿主形态 + \[MapFrom\] 锚定） · Title](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Title)]

[!code-csharp[特性侧双钩子（双参宿主形态 + \[MapFrom\] 锚定） · Qty](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Qty)]

[!code-csharp[特性侧双钩子（双参宿主形态 + \[MapFrom\] 锚定） · NormalizedTitle](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_NormalizedTitle)]

[!code-csharp[特性侧双钩子（双参宿主形态 + \[MapFrom\] 锚定） · Total](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Total)]

行为要点：

- **双参构造（推荐）** `[BeforeMapping(typeof(HookHost), nameof(HookHost.AttrNormalize))]`：在指定类型上强定位静态回调方法，方法可集中宿主类跨实体复用（风格与 `[Convert(typeof(X), nameof(X.M))]` 统一）
- **单参构造** `[BeforeMapping(nameof(Normalize))]`：就近标注，解析域收紧为标注该特性的 DTO 类型自身；`nameof(OtherType.M)` 会被截断为方法名，方法声明于 DTO 之外时须用双参构造（否则 `NCMAP038`）
- **方法签名** `void(TSource, TTarget)`：第二参即该映射对的 DTO 类型（方法签名即适用范围）；签名匹配的对发射，签名不匹配的对不发射、也不发诊断
- **派生字段模式**：钩子写无源侧同名属性的派生字段，映射赋值只碰有源条目的属性，不覆盖钩子产物
- **PATCH 语义**：`_Update` 形态（复用既有目标对象）下 `BeforeMapping` 可见 dst 旧值
- **投影行为**：声明任一钩子的映射对在 ORM 投影下强制降级（嵌套投影不执行生成代码，钩子丢失即语义错误）

| 维度 | `[BeforeMapping]`/`[AfterMapping]`（特性） | `.BeforeMapping`/`.AfterMapping`（Fluent） |
| --- | --- | --- |
| 标注位置 | 类级（`Inherited = false`） | 链式终端（方法组） |
| 构造形态 | 双参宿主 / 单参就近 | 方法组直传 |
| 回调要求 | 可访问静态方法（双参强定位 / 单参就近解析） | 可静态解析的方法组（public / internal static） |
| 编译期诊断 | `NCMAP038`~`NCMAP040`（不可解析/签名不符/重载歧义）+ F35~F39 双侧码 | `NCMAP_F35`~`F39` |

> 完整语义（同类钩子双通道冲突 F39、构造器一体化目标无插入点 F37、FromSource 块内禁止声明 F38）见 [Fluent Hooks 生命周期钩子专节](../how-to/fluent/hooks.md)。

### 5.10 [MapSubclass] — 类级多态子类型映射（等价 Fluent `.MapSubclass<TSrcDerived, TTargetDerived>()`）

标注在基类 DTO 上，为基类映射对登记一对子类型映射：基类映射运行时按源实例**实际类型**路由到子类体（isinst 分派链）。type-level 多实例（`AllowMultiple = true`，每实例登记一对）；标注类须同时标注 `[MapFrom(typeof(基类源))]` 锚定基类映射对（缺锚定报 `NCMAP_F49`）。Fluent 等价终端为 `.MapSubclass<TSrcDerived, TTargetDerived>()`（对级登记，`cfg.For` 即锚定，双侧等价）。

可运行示例（`dotnet run --scenario 107` 实跑验证；`[MapFrom]` + 两实例 `[MapSubclass]` 登记与对应子类 DTO）：

[!code-csharp[特性侧 \[MapSubclass\] 多实例登记与子类 DTO](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto)]

[!code-csharp[特性侧 \[MapSubclass\] 多实例登记与子类 DTO · ScAttrAnimalDto_Name](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Name)]

[!code-csharp[特性侧 \[MapSubclass\] 多实例登记与子类 DTO · ScAttrAnimalDto_Weight](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Weight)]

[!code-csharp[特性侧 \[MapSubclass\] 多实例登记与子类 DTO · ScAttrDogDto](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrDogDto)]

[!code-csharp[特性侧 \[MapSubclass\] 多实例登记与子类 DTO · ScAttrDogDto_Breed](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrDogDto_Breed)]

[!code-csharp[特性侧 \[MapSubclass\] 多实例登记与子类 DTO · ScAttrCatDto](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrCatDto)]

[!code-csharp[特性侧 \[MapSubclass\] 多实例登记与子类 DTO · ScAttrCatDto_Indoor](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrCatDto_Indoor)]

[!code-csharp[特性侧 \[MapSubclass\] 多实例登记与子类 DTO · SubclassFluentMapper](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_SubclassFluentMapper)]

[!code-csharp[特性侧 \[MapSubclass\] 多实例登记与子类 DTO · SubclassFluentMapper_Configure](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_SubclassFluentMapper_Configure)]

行为要点：

- **`Inherited = false`**（派生 DTO 不继承父类的子类登记）、`AllowMultiple = true`（多实例多对登记）
- **无泛型约束**：继承关系（S 派生自基类源、T 派生自基类目标）不满足在提取期报 `NCMAP_F46`（Fluent 侧由泛型约束 CS0311/CS0314 在调用点拦截，用户不应在 Fluent 侧寻找该码）
- **分派语义**：可赋值匹配命中即路由、最派生优先仲裁、未命中回落基类体；`_Update`/`MapListInto` 双实例匹配（src 与 dst 同时命中才路由子类体）
- **反向映射**：子类对经反向注册臂自动产出反向映射与反向直调重载（登记非单向）；`NCMAP_F50`（Info）提示"reverse-asymmetry"等静默面
- **投影行为**：登记 MapSubclass 的基类映射对在 ORM 投影下恒降级（isinst 分派链 SQL 不可翻译，分类 SubclassDispatch）

| 维度 | `[MapSubclass]`（特性） | `.MapSubclass<TSrcDerived, TTargetDerived>()`（Fluent） |
| --- | --- | --- |
| 标注位置 | 类级多实例（type-level） | 链式终端（对级） |
| 锚定要求 | 须同标 `[MapFrom(typeof(基类源))]`（缺锚定 `NCMAP_F49`） | `cfg.For` 即锚定基类对 |
| 继承约束 | 无编译期约束，提取期校验（`NCMAP_F46`） | 泛型约束（CS0311/CS0314 调用点拦截） |
| 其余诊断 | `NCMAP_F47`/`F48`/`F50`（通用） | 同左 |

> 完整语义（最派生优先、基类兜底、双实例匹配、反向不对称）见 [Fluent 四终端 MapSubclass 专节](../how-to/fluent/terminals.md)。

### 5.11 [MapFormat] — 属性级格式化（等价 Fluent `.Format`）

源属性须实现 `IFormattable`（DateTime/DateTimeOffset/TimeSpan/Guid/数值/枚举，含 `Nullable<T>` 包装），目标须为 string。映射时按格式串输出文本：`IFormattable.ToString(format, null)`，恒传 null provider（= 当前文化语义；与未标注属性恒 InvariantCulture 的转换路径分叉为有意设计，需要跨文化一致输出请改用 `[Convert]` 自控文化）。

可运行示例（`dotnet run --scenario 202` 实跑验证）：

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态）](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrder_CreatedAt](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_CreatedAt)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrder_Amount](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_Amount)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrder_Status](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_Status)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrder_Duration](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_Duration)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrder_ShippedDate](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_ShippedDate)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrder_CardNo](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_CardNo)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrderDto](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrderDto_CreatedAt](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_CreatedAt)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrderDto_Amount](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_Amount)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrderDto_Status](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_Status)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrderDto_Duration](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_Duration)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrderDto_ShippedDate](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_ShippedDate)]

[!code-csharp[特性侧 \[MapFormat\] 标注与 \[ConvertBack\] 往返配对（日期/数值/枚举/时长/可空/FormatThenMask 全形态） · MfOrderDto_CardNo](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_CardNo)]

行为要点：

- **单向展示声明**：仅正向（Entity→DTO）消费；反向映射对该属性**不赋值**（实体侧保持原值，其余字段照常回写），裸声明合法，仅报 `NCMAP046`（Warn）提示；往返需同锚补 `[ConvertBack]`（两特性方向正交、可并存）
- **与 `[Convert]` 互斥**：同锚点并存报 `NCMAP045`（Error，二选一）
- **全局忽略豁免门**：`OverrideIgnore = true`（五特性豁免门之一，见 [全局映射配置](../how-to/infrastructure/global-config.md)）
- **投影行为**：实现 `IProjectionPolicy`（`RequiresDowngrade = true`），ORM AsDto 自动降级二步映射（格式化是 CLR 路径，SQL 不可翻译）
- **三类窄格式域**：Guid 仅认 N/D/B/P/X；TimeSpan 自定义格式串冒号句点须转义（`hh\:mm`）；枚举格式串为 G/D/X/F 等枚举专用形式（格式串语义为运行时，编译期只校验非空）
- **无锚不消费**：与 `[Mask]` 同口径——标注类无映射声明锚时不消费，`NCMAP019` 拦截

| 维度 | `[MapFormat]`（特性） | `.Format(format)`（Fluent） |
| --- | --- | --- |
| 标注位置 | 目标 string 属性（`Inherited = false`） | 链式终端（Member 段内，字段级） |
| 格式串要求 | 任意 string 实参（构造期校验非空，空/空白 `ArgumentException`） | 编译期常量（非常量静默跳过声明） |
| 源侧匹配 | 按名自动匹配（不支持跨字段） | 同左（跨字段用 `.Member(d => d.X).From(s => s.Y)` 组合） |
| 守门诊断 | `NCMAP043`~`NCMAP046` | `NCMAP_F64`~`F67`（同位码，双引擎同口径） |

> 完整语义（选型口径、反向不赋值、往返写法）见 [MapFormat 通道专节](../how-to/declarative/map-format.md) 与 [反向映射语义 §5](../explanation/reverse-mapping-semantics.md)。

