---
wiki-id: how-to/infrastructure/global-config
涉及诊断码: NCMAP001, NCMAP013, NCMAP020, NCMAP021, NCMAP022
库版本: v1.0.6
更新时间: 2026-09-30
---

# 全局映射配置（Global Map Config）

## 概述

`[assembly: MapDefaults]` 是程序集级全局配置：一次声明，**所有**映射自动生效。用于统一命名约定、批量忽略审计/版本字段、规范化前后缀，替代逐属性 `[MapProperty]` / `[MapIgnore]` 的重复劳动。

**方向语义**：忽略清单分"双向"与"ToEntity 方向额外"两组——ToEntity 方向忽略 ⟺（双向清单命中）∨（ToEntity 清单命中）；ToDto 方向仅双向清单生效（ToDto 无额外清单）。"ToDto"= 目标类型标注了 `[MapFrom]` 的方向（Entity→DTO）；"ToEntity"= 反向（DTO→Entity，目标无 `[MapFrom]`）。

```csharp
[assembly: MapDefaults(
    NamingStrategy = NamingStrategy.SnakeCase,     // 命名策略
    Prefixes       = new[] { "Tbl" },              // 源属性名前缀剥离
    Postfixes      = new[] { "Entity" },           // 源属性名后缀剥离
    IgnoreNames    = new[] { "RowVersion", "CreatedAt" }, // 双向忽略：精确名
    IgnoreTypes    = new[] { typeof(byte[]) },      // 双向忽略：类型全名
    IgnoreNamePrefixes = new[] { "Internal" },      // 双向忽略：名前缀
    IgnoreNameSuffixes = new[] { "Stamp" },         // 双向忽略：名后缀
    IgnoreNamesToEntity = new[] { "ComputedField" },// 仅 ToEntity 方向忽略
    IgnoreNamePrefixesToEntity = new[] { "Db" })    // 仅 ToEntity 方向：名前缀
```

各参数精确语义见「参数详解」；清单上限 **8 项**，超限该清单降级"未设置"并报 NCMAP020。

## 快速上手

SnakeCase 策略下，蛇形源字段自动匹配驼峰目标属性，无需逐个标注：

```csharp
public class UserEntity
{
    public string user_name { get; set; } = "";   // ← 自动匹配 UserName
}

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public string UserName { get; set; } = "";    // 零标注
}
```

前后缀剥离自动消解命名差异（仅剥离**源侧**）：

```csharp
[assembly: MapDefaults(
    Prefixes  = new[] { "Tbl" },
    Postfixes = new[] { "Entity" })]

public class TblUserEntity
{
    public int UserId { get; set; }               // 剥离 Tbl/Entity → 匹配 UserId
}

public class UserDto
{
    public int UserId { get; set; }               // 零标注
}
```

## 工作原理：匹配流水线

理解各参数之前，先看源属性 → 目标属性的匹配全过程（与源码 `NameCandidateResolver` 一一对应）：

```
① 源属性生成候选名集合
    原名 + 剥缀变体（Prefixes/Postfixes 命中则生成，仅源侧）
② 每个候选名按 NamingStrategy 归一化为比较键
    Default/CaseSensitive → 原名；Snake/UpperSnake → 去下划线 + 转大写
③ 比较键入"源字典"（撞键先到先得：声明序首个保留，后续同键丢弃）
④ 目标属性名归一化（同一策略）后在源字典中查表 → 命中即匹配
⑤ 忽略判定（fail-closed）：目标/源属性任一命中忽略清单 → 该映射对放弃
```

两个容易误判的细节：

- **SnakeCase 的匹配面比"蛇形↔驼峰"更宽**：归一化是"去下划线+转大写"，所以 `user_name`、`USER_NAME`、`UserName`、`username` 归一化后都是 `USERNAME`——策略下**任意大小写与下划线组合只要去掉下划线后相同即可匹配**（Default 只忽略大小写、不忽略下划线）。
- **扁平化拆解与段匹配不随策略**：`OrderItemName → Order.Item.Name` 的自动拆解在四种策略下均生效（段匹配固定 OrdinalIgnoreCase，四引擎一致：源生成器 / 运行时 Mapper.Map / ORM 投影 / Compose）；但**触发边界受策略同名命中影响**——SnakeCase 下源存在 `customer_name` 单段属性时，`CustomerName` 被单段命中，扁平化不触发；已匹配的扁平化映射**不参与策略重匹配**（源码 `ReplaceWithConfigAwareMatch` 对 `IsFlattened` 跳过）。
- **全局忽略清单 × 扁平化/点路径（叶节点判定）**：扁平化（`OrderItemName → Order.Item.Name`）与点路径（`[MapProperty(typeof(Entity), "Order.Item.Name")]`）命中项的忽略判定粒度为**叶节点**（最终选中路径末段）——叶节点命中忽略清单 → 该属性不映射（四引擎一致：源生成器值模型管线 / 运行时 Mapper.Map / Dapper / ORM 投影）；**中间导航节点命中不判定**（如 `AuthorName` 的中间节点 `Author` 命中清单不生效，仅叶节点 `Name` 命中才跳过，与源生成器值模型管线一致）。投影侧该判定**不触发降级**：投影构建期直接跳过该属性（与内存映射同形态，非数据丢失路径，详见 [ORM Projection 设计](../../explanation/orm-projection-design.md) §11.1）。
- **点路径显式支持**：`[MapProperty(typeof(Entity), "Order.Item.Name")]` 点路径四引擎对齐（源生成器 / 运行时 Mapper.Map / ORM 投影 / Compose(SourceGen)），与推导扁平化共享多段路径语义（SourcePath 规范形 + PathOrigin 区分来源）。失败为**显性可观测**：源生成器 NCMAP001/005 带失败段名与所在类型；运行时抛 MapConfigurationException（**显性失败**，不静默跳过）；投影跳过 + Trace。`[Mask]`/`[Convert]` × 点路径**已消费**（路径映射脱敏与转换生效；投影经降级链二步映射消费）。
- **`[MapProperty].FromField` 恒字面匹配**：显式标注的字段名不随命名策略变化（OrdinalIgnoreCase 字面比较），策略匹配不会改写它。

## 参数详解

### 1. `NamingStrategy` — 命名策略

| 项 | 值 |
|----|----|
| 类型 | `NamingStrategy` 枚举 |
| 默认值 | `Default` |
| 作用域 | 全局（所有映射） |

| 枚举值 | 匹配规则 | 归一化键示例 |
|--------|----------|--------------|
| `Default`（默认） | 忽略大小写同名匹配（现状行为，零变更） | `username` ↔ `UserName` |
| `CaseSensitive` | 大小写敏感精确匹配（关闭忽略大小写） | `UserName` ↔ `UserName`；`username` ✗ |
| `SnakeCase` | 去下划线 + 转大写后精确比较 | `user_name` ↔ `UserName` ↔ `USER_NAME` ↔ `username` |
| `UpperSnakeCase` | 同上（归一化算法与 SnakeCase 等价，语义上用于全大写蛇形场景） | `USER_NAME` ↔ `UserName` |

**边界**：

- 四种策略均保留自动扁平化；`CaseSensitive` 下必须逐字符一致才匹配。
- 策略只影响"无显式标注"的匹配；`[MapProperty].FromField` 恒字面匹配（见上）。
- 新增枚举值，switch 须带 default 分支。

### 2. `Prefixes` — 源属性名前缀剥离

| 项 | 值 |
|----|----|
| 类型 | `string[]`，可空（null = 未设置，继承下层） |
| 上限 | 8 项（超限 → NCMAP020，该项降级未设置） |
| 作用域 | 仅**源侧**（目标侧不剥缀） |

**匹配规则**：源属性名 `StartsWith(前缀, OrdinalIgnoreCase)` 且剥后名非空（`name.Length > prefix.Length`），生成"剥缀变体"参与匹配。剥离不区分大小写（`tblUser` 也命中 `Tbl`）。

**变体生成顺序**（`GetStripVariants`）：① 原名 → ② 剥每个命中前缀 → ③ 剥每个命中后缀 → ④ 先剥前缀再剥后缀（组合）。全部变体归一化后入源字典。

**示例**：

```csharp
[assembly: MapDefaults(Prefixes = new[] { "Tbl", "Get" })]

public class TblUserEntity
{
    public int TblUserId { get; set; }     // 剥 Tbl → UserId，匹配目标 UserId
    public string GetName { get; set; }    // 剥 Get → Name，匹配目标 Name
}

public class UserDto { public int UserId { get; set; } public string Name { get; set; } }
```

**边界（重要）**：

- 剥缀只影响"查表键"，不改动属性本身。
- **撞键先到先得**：源字典按属性声明序填充，先声明的属性（含其剥缀变体）抢占键，后声明属性同名键被丢弃。例：源同时有 `GetName`（声明在前）与 `Name`，且 `Prefixes=["Get"]` → `GetName` 的剥缀变体 `Name` 先入字典，`Name` 属性原名撞键被丢弃，目标 `Name` 绑定到 `GetName`。声明顺序影响绑定结果。
- 剥缀与命名策略叠加：剥缀变体仍按策略归一化后再比较（如 SnakeCase 下 `Tbl_user_name` 剥 `Tbl` → `user_name` → 归一化匹配 `UserName`）。

### 3. `Postfixes` — 源属性名后缀剥离

| 项 | 值 |
|----|----|
| 类型 | `string[]`，可空（null = 未设置） |
| 上限 | 8 项 |
| 作用域 | 仅**源侧** |

**匹配规则**：源属性名 `EndsWith(后缀, OrdinalIgnoreCase)` 且剥后名非空，生成"剥后缀变体"。与 `Prefixes` 的变体生成、撞键规则、策略叠加完全一致。

**示例**：

```csharp
[assembly: MapDefaults(Postfixes = new[] { "Entity" })]

public class UserEntity
{
    public int UserIdEntity { get; set; }  // 剥 Entity → UserId，匹配目标 UserId
}

public class UserDto { public int UserId { get; set; } }
```

### 4. `IgnoreNames` — 双向按属性名精确忽略

| 项 | 值 |
|----|----|
| 类型 | `string[]`，可空 |
| 上限 | 8 项 |
| 作用域 | **双向**（ToDto 与 ToEntity 均生效） |

**匹配规则**：属性名 `OrdinalIgnoreCase` 精确比较——**不区分大小写**（`rowversion`、`ROWVERSION` 均命中 `RowVersion`）。命中即忽略（fail-closed），目标属性留空、源属性不参与候选。

**示例**：

```csharp
[assembly: MapDefaults(IgnoreNames = new[] { "RowVersion", "CreatedAt" })]

public class UserEntity
{
    public string RowVersion { get; set; }  // 全局忽略 → DTO 无此字段
    public DateTime CreatedAt { get; set; } // 同上
}
```

### 5. `IgnoreTypes` — 双向按属性类型忽略

| 项 | 值 |
|----|----|
| 类型 | `Type[]`，可空 |
| 上限 | 8 项 |
| 作用域 | **双向** |
| 匹配键 | 属性类型的 `Type.FullName`（`Ordinal` 精确比较） |

**匹配规则**：属性类型全名精确命中即忽略。要点：

- **开放泛型不匹配**：清单写 `typeof(List<>)` 不会命中 `List<string>`；须写封闭类型 `typeof(List<string>)`。且 MVP 对含泛型 arity 反引号（`List`1`）的全名不承诺（MSBuild 通道直接降级 NCMAP020；特性通道写的开放泛型类型会生成带反引号全名，同样无法命中）。
- 数组类型写 `typeof(byte[])`，匹配键为 `System.Byte[]`（SourceGen 与运行时 `Type.FullName` 一致）。
- 嵌套类型全名为 `Outer+Inner`（`+` 分隔）。

**典型场景**：按类型忽略适合"整类系统字段不参与映射"——如 ORM 并发令牌列（SQL Server `rowversion`/`timestamp` 列映射为 `byte[]`）、平台基础设施类型。

**示例**：

```csharp
[assembly: MapDefaults(IgnoreTypes = new[] { typeof(byte[]) })]  // 所有 byte[] 属性全局忽略

public class UserEntity
{
    public byte[] RowVersion { get; set; }    // SQL Server rowversion/timestamp（ORM 并发令牌）→ 忽略
    public byte[] RowStamp { get; set; }      // 同上
}
```

**⚠️ 一刀切警告**：按类型忽略影响面最大——**所有**该类型的属性都不映射。若业务数据（图片、文件内容）也是 `byte[]`，会被一并忽略：改用 `IgnoreNames` 精确点名（只忽略 RowVersion/RowStamp），或给需要保留的目标属性加 `OverrideIgnore = true` 豁免。

### 6. `IgnoreNamePrefixes` — 双向按属性名前缀忽略

| 项 | 值 |
|----|----|
| 类型 | `string[]`，可空 |
| 上限 | 8 项 |
| 作用域 | **双向** |

**匹配规则**：属性名 `StartsWith(前缀, OrdinalIgnoreCase)` 命中即忽略（不区分大小写）。

**与 `Prefixes` 的本质区别**：`Prefixes` 是"剥掉前缀后**继续匹配**"，`IgnoreNamePrefixes` 是"命中前缀**直接放弃映射**"。

**示例**：

```csharp
[assembly: MapDefaults(IgnoreNamePrefixes = new[] { "Internal" })]

public class UserEntity
{
    public string InternalRemark { get; set; }  // Internal* → 忽略
    public string InternalState { get; set; }   // 同上
}
```

### 7. `IgnoreNameSuffixes` — 双向按属性名后缀忽略

| 项 | 值 |
|----|----|
| 类型 | `string[]`，可空 |
| 上限 | 8 项 |
| 作用域 | **双向** |

**匹配规则**：属性名 `EndsWith(后缀, OrdinalIgnoreCase)` 命中即忽略。与 `Postfixes` 的区别同第 6 条：一个是"剥掉继续匹配"，一个是"直接放弃"。

**示例**：

```csharp
[assembly: MapDefaults(IgnoreNameSuffixes = new[] { "Stamp" })]

public class UserEntity
{
    public string RowStamp { get; set; }     // *Stamp → 忽略
    public string TimeStamp { get; set; }    // 同上
}
```

### 8. `IgnoreNamesToEntity` — ToEntity 方向按名忽略

| 项 | 值 |
|----|----|
| 类型 | `string[]`，可空 |
| 上限 | 8 项 |
| 作用域 | **仅 ToEntity 方向**（DTO→Entity，目标无 `[MapFrom]`） |

**匹配规则**：与 `IgnoreNames` 相同（OrdinalIgnoreCase 精确），但仅 `isToDto=false` 时生效；与双向清单**取并集**。

**生效时机说明**：ToEntity 方向是反向映射（如 `Mapper.Map<UserDto, UserEntity>(dto)`，目标 `UserEntity` 无 `[MapFrom]`）。该清单对**源生成器反向路径**与**运行时路径**同时生效：

- **源生成器反向**（ F1 对齐）：反向快照构造时以 `isToDto=false` 应用全局忽略清单（三消费方——反向方法生成、模块初始化器注册、反向扩展生成——共用同一过滤后快照），命中属性跳过 + NCMAP013 登记；过滤后为空则反向方法/扩展不生成
- **运行时**：`PropertyIgnoreChecker` 依据"目标类型是否标注 `[MapFrom]`"推导方向，无 `[MapFrom]` 即 ToEntity，`*ToEntity` 清单生效

**示例**：

```csharp
[assembly: MapDefaults(IgnoreNamesToEntity = new[] { "ComputedField" })]

public class UserDto
{
    public string ComputedField { get; set; }   // 反向映射回实体时忽略
}
```

### 9. `IgnoreTypesToEntity` — ToEntity 方向按类型忽略

| 项 | 值 |
|----|----|
| 类型 | `Type[]`，可空 |
| 上限 | 8 项 |
| 作用域 | **仅 ToEntity 方向** |

**匹配规则**：与 `IgnoreTypes` 相同（`Type.FullName` Ordinal 精确），仅 `isToDto=false` 生效。典型场景：DTO 中的临时大对象字段（如 `byte[]` 附件）不写回实体。

### 10. `IgnoreNamePrefixesToEntity` — ToEntity 方向按名前缀忽略

| 项 | 值 |
|----|----|
| 类型 | `string[]`，可空 |
| 上限 | 8 项 |
| 作用域 | **仅 ToEntity 方向** |

**匹配规则**：与 `IgnoreNamePrefixes` 相同（OrdinalIgnoreCase 前缀命中），仅 `isToDto=false` 生效。

### 11. `IgnoreNameSuffixesToEntity` — ToEntity 方向按名后缀忽略

| 项 | 值 |
|----|----|
| 类型 | `string[]`，可空 |
| 上限 | 8 项 |
| 作用域 | **仅 ToEntity 方向** |

**匹配规则**：与 `IgnoreNameSuffixes` 相同（OrdinalIgnoreCase 后缀命中），仅 `isToDto=false` 生效。

> **ToEntity 组使用提醒**：SourceGen 编译期对 `[MapFrom]` 类型只产出 ToDto 方向映射（源码 `isToDto = true` 硬编码），因此 `*ToEntity` 清单在源生成路径不参与；它服务于**运行时反向映射**。若你的场景完全不走运行时反向映射，`*ToEntity` 清单不会产生任何效果。

## 豁免机制（OverrideIgnore）

全局忽略是 fail-closed（命中即忽略），但目标属性可通过显式特性声明**映射对级豁免**：

```csharp
[MapProperty(OverrideIgnore = true)]        // 四选一即可豁免整对
// 或 [Convert(OverrideIgnore = true)] / [Mask(OverrideIgnore = true)] / [ConvertBack(OverrideIgnore = true)]
public string RowVersion { get; set; }      // 命中全局忽略，但被显式救回
```

**规则**：

- 豁免覆盖五特性：`[MapProperty]` / `[Convert]` / `[Mask]` / `[ConvertBack]` / `[MapFormat]`，任一带 `OverrideIgnore=true` 即豁免**整对映射**（源、目标两侧的全局判定均跳过）。
- 豁免判定挂在**目标属性**上。
- **`[MapIgnore]` 不参与豁免**：属性级显式忽略恒生效。同一属性既标 `[MapIgnore]` 又标 `OverrideIgnore=true` 是矛盾配置，源生成器报 **NCMAP022**（Warning）。
- 显式标注（五特性）命中全局忽略但未声明 `OverrideIgnore` → 源生成器报 **NCMAP021**（Warning + 双向处置指引：要么删标注随全局忽略，要么加 `OverrideIgnore=true` 救回）。

**与三原则的边界（域外对象）**：全局忽略清单（`IgnoreNames`/`IgnoreTypes`/`IgnoreNamePrefixes`/`IgnoreNameSuffixes`）是**程序集级全局配置**，不属于任何类、也不是映射配置标注，因此不受"属性级标注跟随类锚"约束——无锚类同样会被全局忽略命中。同类域外对象还有三类：嵌套按名递归（按名匹配的基线行为）、类级 `[MapTree]`（树构建定义，`ToTreeList` 调用即激活）、`[MapSubclass]`（缺锚由专用诊断 `NCMAP_F49` 提示）。三原则的白话说明见 [映射配置所有权三原则](../../explanation/semantics/mapping-config-ownership.md)。

## 三层优先链

逐项非空覆盖，细粒度层优先：

```
属性级标注（[MapProperty] / [MapIgnore] / [Convert] / [Mask]）
      >  [assembly: MapDefaults]
      >  MSBuild 属性（<NCMapperNamingStrategy> 等，buildTransitive targets 通道）
      >  内建默认（Default 策略 + 空清单）
```

MSBuild 通道（仅对参与源生成的编译单元生效；清单分隔符为逗号 `,`，非分号——分号在 editorconfig 值语法中是行内注释符会截断；11 个属性全名单见下）：

```xml
<PropertyGroup>
  <NCMapperNamingStrategy>SnakeCase</NCMapperNamingStrategy>
  <NCMapperIgnoreNames>RowVersion,CreatedAt</NCMapperIgnoreNames>
  <NCMapperIgnoreNamesToEntity>DbState</NCMapperIgnoreNamesToEntity>
</PropertyGroup>
```

完整属性白名单（`src/NCMapper/NCMapper.targets` 的 `CompilerVisibleProperty`）：

```
NCMapperNamingStrategy
NCMapperPrefixes                NCMapperPostfixes
NCMapperIgnoreTypes             NCMapperIgnoreNames
NCMapperIgnoreNamePrefixes      NCMapperIgnoreNameSuffixes
NCMapperIgnoreTypesToEntity     NCMapperIgnoreNamesToEntity
NCMapperIgnoreNamePrefixesToEntity  NCMapperIgnoreNameSuffixesToEntity
```

> 无 `[MapFrom]` 的运行时回退路径不读 MSBuild 属性（仅源生成编译单元可见）。

## 冻结语义（两段式）

1. **注册**：源生成器编译期读取 `[assembly: MapDefaults]`，生成 `Mapper.RegisterGeneratedConfig` 快照注册代码
2. **冻结**：首次映射/`Validate()` 触发——冻结后配置不可修改（无修改入口）

多程序集各自声明不同配置时，首次冻结抛 `MapConfigurationException` 并列出冲突程序集名（fail-fast）；内容一致则幂等合并（清单顺序差异不误判冲突）。`Mapper.GetGlobalConfig()` 返回只读视图（不可变，调试/断言用）。

**回退通道**：无源生成代码的程序集（无 `[MapFrom]` 类型），`Mapper` 通过反射读取 `[assembly: MapDefaults]` 兜底。

## 读取与调试

[!code-csharp[读取全局配置只读视图](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal)]

适用于调试、日志、测试断言；清单底层数组不外泄。

## 诊断规则

| 诊断 | 严重度 | 触发条件 |
|------|--------|----------|
| NCMAP020 | Warning | 配置值非法（枚举越界、清单超 8 项、复杂类型全名）→ 该项降级"未设置"，不阻断编译 |
| NCMAP021 | Warning | 显式标注（`[MapProperty]`/`[Convert]`/`[ConvertBack]`/`[Mask]`/`[MapFormat]`）命中全局忽略且无 `OverrideIgnore` → 双向处置指引 |
| NCMAP022 | Warning | `[MapIgnore]` 与 `OverrideIgnore=true` 同标（矛盾配置） |

## 可运行演示（场景 606）

[!code-csharp[命名策略与忽略清单（SnakeCase 匹配 + 全局忽略）](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_StrategyAndIgnore)]

[!code-csharp[Fluent 豁免门与命名策略交互（overrideIgnore 救回全局忽略字段）](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_FluentInteract)]

## 迁移对照

- **迁移前**：逐属性 `[MapProperty(FromField="user_name")]` + 逐个 `[MapIgnore]`
- **迁移后**：`[assembly: MapDefaults(NamingStrategy = SnakeCase, IgnoreNames = [...])]`，DTO 零标注

可运行示例位于 `samples/NCMapper.Samples/`（场景 606，两路径映射结果一致性校验）。

核心示例源码（单点同源）：

[!code-csharp[全局配置生效（MapGlobal）](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal)]

[!code-csharp[全局配置生效（MapGlobal） · RunSummary](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal_RunSummary)]

[!code-csharp[全局配置生效（MapGlobal） · Run](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal_Run)]
