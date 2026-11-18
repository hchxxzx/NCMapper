***

wiki-id: explanation/reverse-mapping-semantics
涉及诊断码: NCMAP032, NCMAP033, NCMAP034, NCMAP035, NCMAP036, NCMAP037, NCMAP046
库版本: v1.0.0+
更新时间: 2026-09-30
----------------

# 反向映射语义

## 概述

反向映射是 DTO → 实体的映射方向，与正向（实体 → DTO）语义并不对称。本篇文章解释反向映射的方向定义、自定义转换处理规则、脱敏写回风险、null 写回语义与忽略清单，以及相关诊断码。

反向直调的生成规则（6 重载扩展）随本篇聚焦语义本身：`[MapFrom]` DTO 统一生成，声明跨表子源字段的 DTO 同样生成（[Compose §5.1](../how-to/compose/compose.md)）。

***

## 1. 反向映射方向定义

### 1.1 方向判定

`[MapFrom(typeof(实体))]` 声明在 DTO 上，即"DTO 映射自实体"。由此定义方向：

| 方向           | 起点  | 终点  | 生成物                                                |
| ------------ | --- | --- | -------------------------------------------------- |
| 正向（ToDto）    | 实体  | DTO | `Map_实体_To_DTO`、`entity.MapToXxxDto()`             |
| 反向（ToEntity） | DTO | 实体  | `dto.MapToXxxEntity()`、`dtos.MapToXxxEntityList()` |

`[MapFrom]` DTO 统一生成反向 6 重载扩展：新建（`()`）、带回调（`(Action)`）、更新已有对象（`(entity)` / `(entity, Action)`）、列表（`List()` / `(Action)`），与正向对称。声明跨表子源字段的 DTO 同样生成，反向只覆盖主源字段（[Compose §5.1](../how-to/compose/compose.md)）。

### 1.2 反向的范围

反向映射只覆盖 DTO 中声明了来源的属性（主源字段）；声明跨表子源字段的 DTO，其子源字段不参与反向写回（[Compose §5.1](../how-to/compose/compose.md)）。

### 1.3 方向判定规则

方向由 `[MapFrom]` 标注位置判定：目标类型标注 `[MapFrom(源类型)]` 即"目标映射自源"。

| 标注位置                          | 正向                           | 反向                            |
| ----------------------------- | ---------------------------- | ----------------------------- |
| DTO 标 `[MapFrom(typeof(实体))]` | 实体 → DTO（生成 `MapToXxxDto`）   | DTO → 实体（生成 `MapToXxxEntity`） |
| 双侧未标 `[MapFrom]`              | 方向不可推断，`[MapIgnore]` 方向性判定受限 | 同左                            |

方向判定对 `[MapIgnore(Direction.ToEntity)]` 等方向性忽略规则生效，标注在规范位置（DTO 侧）时方向判定有据。非规范标注（`[MapFrom]` 标在实体侧）时方向判定与直觉相反，运行时输出一次性 Trace 提示（[映射规则（语义）](./semantics/mapping-rules.md)）。

***

## 2. \[Convert] 反向不消费（NCMAP033）

### 2.1 规则

`[Convert]` 引用的静态转换方法签名绑定正向方向（T1/T3 的 `TField` 为实体侧源字段类型，T2/T3 的 `TSource` 为实体类型），反向映射（DTO → 实体）**默认不消费**该转换方法：

- 直接属性与写回路径统一按类型转换路径写入

- 编译器报 **NCMAP033**（Info）：`[Convert]` 属性在反向映射中被跳过转换器

### 2.2 原因

转换方法签名绑定正向方向（`TSource` = 实体类型、`TField` = 实体侧字段类型），反向调用时输入类型（DTO 对象 / DTO 字段）与方法参数类型不匹配，消费会产生类型错乱。反向需要自定义转换时，使用 `[ConvertBack]`（见下节）。

***

## 3. \[ConvertBack] 反向自定义转换（NCMAP035/036/037）

### 3.1 规则

`[ConvertBack(typeof(Converter), nameof(Converter.Convert))]` 标注于 DTO 属性，反向映射显式消费该静态转换方法。方法签名模板（T0–T3，与 `[Convert]` 同构，但 `TSource` / `TField` 绑定**反向方向**）：

```csharp
public static TTarget Convert(DTO类型 source, TField fieldValue)  // T3 模板
```

与 `[Convert]` 并列标注合法：正向消费 `[Convert]`、反向消费 `[ConvertBack]`，互不干扰。

### 3.2 标注位置与两种消费形态

`[ConvertBack]` 的规范标注位置是 DTO 属性（§3.1）。实体侧（反向目标侧）标注同样合法，按以下两种形态消费——运行时与源生成器双引擎一致：

**形态①：实体侧标注 + DTO 无同名属性（无源反向消费）**

实体属性标注 `[ConvertBack]`，且 DTO 上不存在同名属性时，转换方法仍被调用——通过 `source` 参数访问**整个 DTO 对象**（`TSource` = DTO 类型），适配一对多拆分、无源计算属性等形态（如 DTO `FullName` 拆写实体 `FirstName`/`LastName`）。

消费门（与普通反向属性一致的门控，命中即不调用转换方法）：

- **可写性门**：实体属性不可写（get-only）时跳过（源生成器报 NCMAP013；运行时 Trace 跳过）；init-only 正常消费

- **全局忽略门**：全局 `Ignore*ToEntity` 清单命中该实体属性名时抑制（§7，NCMAP021 指引）；`[ConvertBack(..., OverrideIgnore = true)]` 显式豁免

- **写回覆盖门**：该实体属性已作为反向写回路径段（`[MapProperty]` 点路径折叠）被覆盖时静默排除——写回链产物优先，避免对同一属性双赋值

**形态②：匹配对实体侧标注（兜底位，DTO 优先）**

实体与 DTO 存在同名属性、且 `[ConvertBack]` 标注在实体侧时，作为**兜底位**消费：DTO 侧同名属性已标注时以 DTO 侧为准；DTO 侧无标注时消费实体侧标注。两处的模板绑定一致（`TSource` = DTO 类型、`TField` = DTO 侧字段类型、返回类型 = 实体属性类型）。

**形态③：写回链实体叶属性标注（非规范位置，DTO 优先 / 叶兜底）**

反向写回链（`[MapProperty(typeof(Entity), "Address.City")]` 扁平 DTO 属性 → 实体嵌套路径）场景下，`[ConvertBack]` 标注在**实体叶属性**（路径末段，如 `Address.City` 的 `City`）同样合法：DTO 扁平属性已标注时以 DTO 侧为准；DTO 侧无标注时消费叶侧标注（查询序与形态②一致）。模板绑定为 `TSource` = DTO 类型、`TField` = DTO 扁平属性类型、返回类型 = 实体叶属性类型（转换器 `source` 参数即 DTO 对象）；源 null 时清空叶、不调用转换方法（§6.2 语义不变）。运行时与源生成器双引擎一致。

不适用形态（标注静默不消费）：

- **中间段属性**：导航容器非值落点，静默排除（写回链优先，无双赋值）

- **集合索引路径**（`"Items[0].Name"`）：反向写回整体跳过（语义未定义），校验随之跳过

方法签名不匹配时同样报 NCMAP035/036/037（§3.3）；叶侧标注的诊断载体为叶属性。

完整示例见 [反向自定义转换](../how-to/declarative/basics.md)（操作向）与 [反向转换消费语义](./semantics/mapping-rules.md)（语义向）。

### 3.3 错配诊断

方法解析失败时报 **NCMAP035**（锚定类型上无可访问静态方法）/ **NCMAP036**（重载歧义）/ **NCMAP037**（无重载命中 T0–T3 模板）：编译阻断。这是编译期守护，比运行时才发现方法引用错误更早暴露问题。

***

## 4. \[Mask] 属性反向写回（NCMAP034）

### 4.1 规则：Mask 仅正向消费

`[Mask]` 是**仅正向消费**的特性：`IMask.Mask()` 只在正向映射（实体 → DTO）中被调用；反向映射（DTO → 实体）**不调用**脱敏器——反向写回的是 DTO 属性的当前值，与是否标注 `[Mask]` 无关。

### 4.2 风险

当 DTO 属性当前值是正向脱敏的结果时，反向写回即"脱敏值原样写回"：

```text
实体（完整值） → 正向脱敏 → DTO（脱敏值） → 反向写回 → 实体（脱敏值，原始数据被覆盖）
```

这个 round-trip 是有损的：实体中的完整数据被脱敏值覆盖。编译器报 **NCMAP034**（Info）：`[Mask]` 属性参与反向写回（脱敏值将原样写回）。NCMAP034 仅是**警示**（脱敏值写回有损），不是"反向会执行脱敏"——脱敏器在反向不会再次被调用。

### 4.3 处置建议

CRUD 更新场景下，脱敏 DTO 属性建议标注 `[MapIgnore(Direction.ToEntity)]` 阻断反向写回，避免实体完整数据被脱敏值覆盖。保留脱敏展示、阻断写回是常见组合。

***

## 5. [MapFormat] 反向不赋值（NCMAP046）

### 5.1 规则

`[MapFormat]`（Fluent 等价 `.Format`）是**仅正向**的声明：正向映射按格式串把源值格式化为文本写入目标 string 属性（`IFormattable.ToString(format, null)`，恒传 null provider = 当前文化）。反向映射（DTO → 实体）对带该标注的属性**不赋值**：

- 实体侧保持原值，其余字段照常回写；
- 编译期报 **NCMAP046**（Warn；Fluent 通道同位码 NCMAP_F67）；
- **裸声明完全合法**：只标 `[MapFormat]` 不补 `[ConvertBack]` 不构成错误——格式化是单向展示声明。

### 5.2 与 [Mask] 的同构性

两者同性质，都是**单向、有损的展示声明**：`[Mask]` 有损于截断、`[MapFormat]` 有损于格式化（且随文化），反向都无法无损反解析，库不猜测反解析。差异在反向处置与诊断级别：Mask 的值**原样写回**实体（round-trip 有损，NCMAP034 Info 警示写回行为）；Format 的值**根本不写回**（实体保持原值，NCMAP046 Warn 提示跳过行为）。

### 5.3 往返写法

格式化字段同时需要写回实体时，同锚点补 `[ConvertBack]` / `.ConvertBack`：两特性（终端）**方向正交**——正向消费 `[MapFormat]`（格式化输出）、反向消费 `[ConvertBack]`（显式反解析），同属性并列合法、互不干扰，反向命中时不再报 NCMAP046。不需要往返且想消除提示时，可改标 `[MapIgnore(Direction.ToEntity)]` 显式阻断。

***

## 6. null 写回与清空语义

### 6.1 规则

反向路径写回源值 null 时：

| 链状态  | 行为                                  |
| ---- | ----------------------------------- |
| 链存在  | 清空叶：可空叶赋 `null`、非可空值叶赋 `default(T)` |
| 链不存在 | no-op：不创建中间节点、不写叶                   |

清空路径逐段不创建中间节点。`[ConvertBack]` 分支在源 null 时不调用转换方法（叶清空语义不变，见 §6.2）。

### 6.2 转换分支的例外

`[ConvertBack]` 分支：源值（DTO 属性）null 时**不调用转换方法**，但叶清空语义与普通属性一致——链存在则清空叶（可空叶赋 `null`、非可空值叶赋 `default(T)`），链不存在则 no-op（不创建中间节点、不写叶）。跳过的是**转换调用**（转换方法输入 null 时输出不可预期，跳过更安全），而非跳过叶写入——源 null 时实体叶仍会被清空为 `null`。`[Convert]` 反向本就不消费（NCMAP033），与 null 无关。

***

## 7. ToEntity 忽略清单

### 7.1 规则

全局 `Ignore*ToEntity` 清单（`[MapIgnore(Direction.ToEntity)]` 及全局配置的 ToEntity 清单）对反向映射生效，且**双引擎一致**：

- 运行时反向路径消费清单

- 源生成器反向路径同样消费，命中属性跳过并报 **NCMAP013**

- 过滤后为空时，反向方法/扩展不生成

### 7.2 语义

忽略清单是反向映射的主要阻断手段：不需要写回实体的 DTO 属性（展示字段、派生字段）统一用 ToEntity 忽略，避免反向覆盖实体数据。

### 7.3 与 MapIgnore 方向的组合

方向性忽略有三种用法，组合决定属性的反向命运：

| 标注                                | 正向（ToDto） | 反向（ToEntity） |
| --------------------------------- | --------- | ------------ |
| 无标注                               | 映射        | 映射（受其他规则约束）  |
| `[MapIgnore(Direction.ToDto)]`    | 跳过        | 映射           |
| `[MapIgnore(Direction.ToEntity)]` | 映射        | 跳过（NCMAP013） |
| `[MapIgnore(Direction.Both)]`     | 跳过        | 跳过           |

展示字段（脱敏值、派生文本）用 ToEntity 忽略是 CRUD 场景的推荐姿势：页面展示需要、写回不需要。

***

## 8. 更新已有对象与 init-only

反向映射的"更新"重载（`dto.MapToXxxEntity(entity)`）对已有目标对象赋值。目标属性为 init-only 时无法再次赋值，属性跳过并报 **NCMAP032**（Info）：更新期 init-only 属性跳过（仅创建路径可写）。这是 C# `init` 访问器语义限制，不是配置错误；创建路径（`dto.MapToXxxEntity()`）不受影响。

***

## 9. 常见疑问

**为什么反向不直接复用正向的转换方法？**

转换方法签名绑定正向方向（`TSource` = 实体类型、`TField` = 实体侧字段类型），反向调用时输入是 DTO 对象 / DTO 字段，类型不匹配。复用会产生类型错乱，且转换通常不可逆（格式化、脱敏），自动反向推导会出错。需要反向转换时显式标 `[ConvertBack]`。

**脱敏 DTO 参与更新表单怎么办？**

脱敏值写回会覆盖实体完整数据（NCMAP034 警示）。建议：展示用脱敏 DTO，写回前对敏感属性标 `[MapIgnore(Direction.ToEntity)]` 阻断，或更新时显式排除脱敏字段。

**格式化的字段反向会写回吗？**

不会。`[MapFormat]` 是单向展示声明：反向映射跳过该属性（实体侧保持原值），编译期报 NCMAP046（Warn）提示。需要往返时同锚补 `[ConvertBack]` 显式反解析（正向格式化、反向转换，方向正交，§5.3）；不需要往返则无需处理。

**反向扩展方法没生成？**

检查 `[MapFrom]` 标注是否在 DTO 上；`Ignore*ToEntity` 清单过滤后为空时不生成反向方法（NCMAP013）。另外确认引用了生成的命名空间（`using NCMapper.Generated;`）。

**反向映射和 AutoMapper 的 ReverseMap 一样吗？**

方向一致（DTO → 实体），但 NCMapper 的语义更明确：反向不自动推导正向转换方法（NCMAP033）、脱敏写回有损（NCMAP034）、null 写回清空语义。迁移时按本指南逐条核对行为差异。

***

## 速查表

| 语义                   | 规则                                                                                                 | 诊断码                     |
| -------------------- | -------------------------------------------------------------------------------------------------- | ----------------------- |
| 方向定义                 | 正向 = 实体 → DTO（ToDto）；反向 = DTO → 实体（ToEntity）                                                       | —                       |
| `[Convert]` 反向  | 默认不消费，按类型转换路径写入                                                                                    | NCMAP033（Info）          |
| `[ConvertBack]` | 反向显式消费，静态方法签名绑定反向方向（TSource/TField = DTO 侧）；DTO 属性为规范位，实体侧标注按无源（整 DTO 入参）/匹配对兜底（DTO 优先）两形态消费（§3.2） | NCMAP035/036/037（Error） |
| `[Mask]` 反向写回        | 仅正向消费（反向不调用脱敏器）；脱敏值原样写回，round-trip 有损                                                              | NCMAP034（Info）          |
| `[MapFormat]` 反向       | 仅正向消费（格式化输出有损且随文化，不猜测反解析）；反向**不赋值**，实体侧保持原值；往返需同锚补 `[ConvertBack]`（§5）                | NCMAP046（Warn）          |
| null 写回              | 链存在清空叶、链不存在 no-op                                                                                  | —                       |
| ToEntity 忽略清单        | 双引擎一致，命中跳过                                                                                         | NCMAP013                |
| init-only 更新         | 更新路径跳过，创建路径可写                                                                                      | NCMAP032（Info）          |

***

## 相关文档

- [映射规则（语义）](./semantics/mapping-rules.md)：`[MapIgnore]` 与忽略清单、反向映射契约

- [属性级格式化](../how-to/declarative/map-format.md)：`[MapFormat]` 通道（§5 反向不赋值的操作向页面）

- [脱敏](../how-to/infrastructure/masking.md)：`[Mask]` 使用

- [映射配置所有权三原则](semantics/mapping-config-ownership.md)：无锚不消费、编译期诊断与承载侧登记面

- [15 分钟上手教程](../getting-started/tutorial-15min.md)：反向映射的首次使用（samples 01）

- [explanation 双引擎架构](./dual-engine.md)：反向语义的双引擎一致性背景

