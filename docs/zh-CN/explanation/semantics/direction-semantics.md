---
wiki-id: explanation/semantics/direction-semantics
涉及诊断码: NCMAP013, NCMAP025, NCMAP031, NCMAP032, NCMAP033, NCMAP034, NCMAP046, NCMAP_F67
库版本: v1.0.0+
更新时间: 2026-09-30
---

# 语义共享层：双向方向语义

> 本篇是方向语义的**单点同源**页：两 API 面的 how-to 页共同引用本篇。反向映射的完整展开（含生成物 6 重载、迁移指引）见 [反向映射语义](../reverse-mapping-semantics.md)。

## 1. 方向由声明位置判定

`[MapFrom(typeof(源))]` 标注在**目标类型（DTO）**上，语义为"目标映射自源"。由此定义双向：

| 方向 | 起点 | 终点 | 生成物 |
| ---- | ---- | ---- | ---- |
| 正向（ToDto） | 实体 | DTO | `entity.MapToXxxDto()` 等 |
| 反向（ToEntity） | DTO | 实体 | `dto.MapToXxxEntity()` 等（反向 6 重载统一生成） |

Fluent 通道以 `cfg.For<源, 目标>()` 声明对，方向语义与特性通道一致。

## 2. 双向语义终审原则（D8）

`[MapFrom]` 为**双向语义**：一次声明同时承载正反两向的映射关系，不需要（也不支持）按方向拆分声明。配套裁决：

- `[Map]` 本身双向且 position 不敏感（Entity/DTO 互换不影响结果），互指场景除外。
- **双 true 互指**（A 声明 `[MapFrom(B)]` 且 B 声明 `[MapFrom(A)]`）为矛盾声明：`NCMAP025` 错误级阻断该对生成，删除一侧可恢复。
- 同一对被特性与 Fluent 双声明同理互斥（`NCMAP_F11`）。

## 3. 反向的不对称点

反向不是正向的镜像，以下行为刻意不对称：

| 主题 | 正向 | 反向 |
| ---- | ---- | ---- |
| 自定义转换 | `[Convert]` 生效 | 默认**不消费**（`NCMAP033` Info），需反向转换请标注 `[ConvertBack]` |
| 脱敏 | `[Mask]` 目标 string 脱敏 | 脱敏值将**原样写回**实体（`NCMAP034` Info，有损不可逆；建议 `[MapIgnore(ToEntity)]`） |
| 格式化 | `[MapFormat]` / `.Format` 源 `IFormattable` → 目标 string | **不赋值**（`NCMAP046`/`F67` Warn，实体侧保持原值，其余字段照常回写）——与脱敏同性质：单向、有损的展示声明，库不猜测反解析；需要往返时同锚补 `[ConvertBack]` / `.ConvertBack`（组合时反向照常消费、无警告） |
| init-only 属性 | 创建路径可写 | 更新路径跳过（`NCMAP032` Info） |
| 子源字段 | 参与合成 | 不参与写回（仅主源字段） |

## 4. 忽略的方向性

`[MapIgnore]` 支持方向参数（`ToDto`/`ToEntity`/双向默认）；Fluent 侧对应 `.Member(d => d.X).Ignore(direction)`（Member 段内，方向参可选）。忽略与转换器声明冲突（同属性 Ignore + Convert/ConvertBack）触发 `NCMAP_F17`/`F23`。

## 5. 深入阅读

- [反向映射语义](../reverse-mapping-semantics.md)（完整反向语义展开）
- [diagnostics/rules.md](../../diagnostics/rules.md)（诊断码**主表**；本篇涉及的 `NCMAP013/025/031/032/033/034/046` 与 `NCMAP_F67` 可在该页或其所转指的分页页查到）
