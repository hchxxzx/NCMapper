---
wiki-id: how-to/mapping/dot-path
涉及诊断码: NCMAP001, NCMAP005, NCMAP033, NCMAP034
库版本: v1.0.0
更新时间: 2026-09-15
---

# 点路径与嵌套映射

## 1. 点路径是什么

`[MapProperty]` 的 `fromField` 支持 `xx.xx` 点路径，用于声明**嵌套属性**的映射来源：

```csharp
// 点路径：源实体 Address 导航属性的 City 字段 → 目标 CustomerCity 属性
[MapProperty(typeof(CustomerEntity), "Address.City")]
public string CustomerCity { get; set; } = "";
```

点路径按段解析（段匹配 `OrdinalIgnoreCase`），可写任意深度（`"Order.Customer.Address.City"`）；中间节点必须是 `class`/`interface`（值类型与 `string` 不能作为中间段）。

### 1.1 根类型锚定与路径翻译

`typeof(CustomerEntity)` 锚定路径解析的**根类型**：`"Address.City"` 翻译为 `CustomerEntity.Address.City`（即 `source.Address.City`，`source` 为 `CustomerEntity` 实例）。注意根类型段**不写在 `fromField` 中**，由 `typeof` 参数隐式提供：

```csharp
[MapProperty(typeof(CustomerEntity), "Address.City")] // 实际解析：CustomerEntity.Address.City
public string CustomerCity { get; set; } = "";
```

- **首段必须是根类型的直接属性**：`Address` 必须直接存在于 `CustomerEntity` 上
- 若目标是孙属性，须写全完整路径：`"CustomerInfo.Address.City"`（首段仍从根类型找起）
- 段匹配不区分大小写：`"address.city"` 同样命中

## 2. 点路径 vs 命名约定扁平化

| 方式 | 写法 | 触发 |
|------|------|------|
| **点路径** | `[MapProperty(typeof(T), "Address.City")]` | 显式声明，映射来源 = 嵌套导航的深层字段 |
| **推导扁平化** | 目标属性名 `AddressCity` | 自动拆解属性名大写边界 → `Address.City`（无需标注） |

两者相互独立：推导扁平化是命名约定自动触发；点路径是显式声明，用于目标属性名无法通过命名约定表达的场景（如 `CustomerCity` 需要明确取 `Address.City` 而非 `Customer.City`）。

## 3. 声明形式

`[MapProperty]` 的 `fromField` 为字符串参数，支持三种形式：

- **简单属性名**：`"DeptName"`（同源同类型最常用）
- **点路径**：`"Address.City"`（嵌套导航）
- **"类型.属性"组合**：与 `typeof` + `nameof` 等价

推荐 `typeof` + `nameof` 强类型写法以获得编译期检查（属性重命名自动跟随）；字符串形式适用于动态/简化场景：

```csharp
[MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Address))]
public string City { get; set; } = "";
```

## 4. 四引擎行为

点路径在源生成器 / 运行时 / ORM 投影 / Compose（SourceGen）四引擎一致支持：

| 引擎 | 行为 |
|------|------|
| 源生成器 | 按段解析生成取值链；跨表（Compose）同样支持 |
| 运行时 | 表达式树逐段访问 |
| ORM 投影 | JOIN 或降级路径生效（见 [FreeSql 多表](../orm/freesql-multi-table.md)） |
| Compose | 多表合成时按声明段链取值 |

## 5. 失败可观测

点路径失败为**显性可观测**（不静默跳过）：

- **源生成器**：段解析失败触发 **NCMAP001**（普通映射）/ **NCMAP005**（Compose 跨表）Warning，且**不生成该映射**，诊断带失败段名与所在类型
- **运行时**：抛 `MapConfigurationException`
- **投影侧**：跳过该属性并输出 Trace

**首段缺失（根类型无此属性）在首段即失败**，不会进入后续段解析。例如 `CustomerEntity` 上不存在 `Address` 属性时：

- **运行时**消息（含失败段名 + 所在类型）：

  ```
  [NCMapper] 显式点路径 'Address.City' 在段 'Address' 处失败（类型 CustomerEntity 中未找到属性），目标属性 CustomerCity 无法映射
  ```

- **编译期**：NCMAP001 诊断携带失败段 `Address` 与所在类型 `CustomerEntity`，`CustomerCity` 不生成映射代码

> **相关 API**：`DotPathFailureKind`——点路径失败原因分类枚举（正向 5 种：索引语法非法 / 索引须非负整数 / 属性未找到 / 值型不可导航 / 非集合不可索引；反向写回 2 种：属性未找到 / 值型不可导航），运行时失败消息由 `ErrorMessages` 按此分类生成（源码锚：ErrorMessages.cs:816）。

## 6. 与 [Mask] / [Convert] 组合

`[Mask]` / `[Convert]` 与点路径组合**已消费**，点路径属性上的脱敏/自定义转换与直接属性同形态生效：

- **正向（源 → 目标）**：点路径叶值经 null 安全链后输入转换器/脱敏器；`[Convert]` 输出可再叠加 `[Mask]`（`Mask(Convert(...))` 形态）
- **反向（DTO → Entity 写回）**：`[ConvertBack]` 已消费；无 `[ConvertBack]` 时**不消费** `[Convert]`（转换器泛型绑定正向方向，NCMAP033 提示）——需反向自定义转换时标注 `[ConvertBack]`
- `[Mask]` 反向写回为脱敏值原样写回（round-trip lossy，NCMAP034 提示）

## 7. 反向写回（DTO → Entity）

反向方向（DTO → Entity）的嵌套路径为**写回**（扁平属性 → 嵌套路径赋值）：

- 中间节点 null 时 `new` 创建
- 源值 null 时整体跳过
- `[ConvertBack]` 例外（反向自定义转换；无标注时 `[Convert]` 不消费，NCMAP033）
- 集合索引 / 接口中间节点跳过 + 诊断/Trace

```csharp
// 正向：Entity.Address.City → DTO.City（读路径）
// 反向：DTO.City → Entity.Address.City（写回，Address 为 null 时创建）
```

`null` 写回语义：源值 null 且目标链存在 → 清空叶（可空叶赋 null / 非可空值叶赋 `default(T)`）；目标链不存在 → no-op；源值非 null → 中间节点 null 时创建链并写叶。

## 8. 相关

- [映射规则（语义）](../../explanation/semantics/mapping-rules.md)：`[MapProperty]` 异名映射与忽略清单

本页可运行演示（场景 201，点路径扁平化）：

[!code-csharp[点路径与扁平化：AddressCity ← Address.City](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_DotPath)]
- [FreeSql 多表](../orm/freesql-multi-table.md)：Compose 投影点路径
- [嵌套对象场景详解](../../explanation/mechanics/nested-mapping.md)：扁平化与反向写回完整场景
