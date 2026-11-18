---
wiki-id: how-to/mapping/map-list-into
涉及诊断码: （无）
库版本: v1.0.8
更新时间: 2026-09-15
---

# MapListInto 批量就地映射（复用场景）

高频批量场景下，若不想每个元素都分配新的目标对象，用 `MapListInto` 把源集合逐元素填充进**调用方预分配的目标数组**，仅覆盖映射字段。同一数组可跨调用复用，复用场景下顶层对象图零分配。

对比价值：`MapList` 每次调用全量分配新 List + N 个 DTO；`MapListInto` 复用预分配目标数组，调用顶层零分配。适合 UI 列表高频刷新、批量同步到常驻缓冲区等场景，避免每轮全量分配带来的 GC 压力。

核心 API：`Mapper.MapListInto<TSource, TTarget>(sources, destination, action?)`

## 用法

一次性预分配数组并初始化每个元素，每次调用就地填充（类型与 samples 的 `UserEntity`/`UserDto` 同源）：

[!code-csharp[MapListInto 批量就地映射方法](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapListInto)]

## 复用循环（预分配 + 跨轮复用）

分页/流式场景复用同一数组，每轮仅覆盖映射字段：

[!code-csharp[MapListInto 复用循环](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapListIntoReuse)]

调用方预分配与多页逐轮填充的调度演示见上方 `BasicMapping_MapListIntoReuse` 片段（`dotnet run --scenario 201`）。

**要点**：

- 复用契约：调用方负责预分配 destination 并初始化每个元素（`destination[i] != null`）。null 槽位被**跳过且不抛异常**（C5）；未映射字段保留上轮残留值（C6），按需自行清理
- 长度契约：`destination.Length >= sources.Count`，仅填充前 sources.Count 个槽位，超量槽位保持原值（C3）；长度不足抛 `ArgumentException`；`destination == null` 抛 `ArgumentNullException`（最先检查，C1）
- `sources == null` 直接返回不填充（C8）；`action(src, dst)` 在每元素映射完成后调用（C10），语义与 `MapList` 一致
- 嵌套引用属性每轮新建子对象（C7）：`dst.Address = Map(src.Address)`，非就地更新。顶层零分配不等于全图零分配
- 已注册类型（[MapFrom]）走源生成器批量委托，未注册类型回退运行时表达式树，两条路径均满足复用契约
- 性能（20260820 负载实测，16 线程，3 轮中位数，DATAS 关闭）：缩放效率 0.66-0.71（TPS_16T/TPS_1T/16），1T 吞吐为同轮 MapList 的 5.2×-5.6×；详见 [性能](../infrastructure/performance.md) §3

## null 处理语义（过程与意义）

`MapListInto` 对 null 的处理分两层：入口校验与元素级逐槽位处理。语义目标是在"复用已有数组"的前提下，目标数组在任何输入下都不会被写坏（不会写入 null、不会清空已有数据）。

### 入口校验（按顺序执行）

| 顺序 | 条件 | 行为 | 编号 | 设计意义 |
|---|---|---|---|---|
| 1 | `destination == null` | 抛 `ArgumentNullException` | C1 | 优先级最高。预分配目标是调用契约的核心，契约被违反必须显式暴露，不能静默吞掉 |
| 2 | `sources == null` | 直接返回，不做任何填充 | C8 | 空源视为无操作。不豁免 C1（destination 为 null 仍抛） |
| 3 | `destination.Length < sources.Count` | 抛 `ArgumentException` | C2 | 防越界，显式失败优于静默写错 |

### 元素级逐槽位处理

| 条件 | 行为 | 编号 | 设计意义 |
|---|---|---|---|
| 源元素为 null | 该槽位**保持原值**，不覆盖 | C4 | "有数据才更新"：源无数据则不动目标，避免破坏常驻目标数组中上轮已有的有效内容 |
| 目标槽位为 null | **跳过**（no-op），不抛异常 | C5 | 更新对象须已存在；null 目标无可赋值对象，宽容跳过而非报错。入口不做逐元素预校验 |
| 槽位下标 ≥ sources.Count | 保持原值 | C3 | 仅填充前 sources.Count 个槽位，超量槽位不动 |

### 组合效果

只对「源非 null 且 目标非 null」的槽位执行更新，其余槽位保持调用前状态。因此目标数组在多次刷新复用中始终安全、稳定、可预测。

典型场景：UI 表格常驻 100 行数据，每 5 秒从服务端拉取新数据 `MapListInto` 刷新。某次查询某行无结果（源元素 null），该行保持上一轮数据显示，不闪空；目标数组某槽位尚未初始化（null），该槽位跳过，下次初始化后正常更新。

### 更新语义：有数据才更新（非 diff）

`MapListInto` 不做"值是否变化"的检测（非 diff 语义）。只要源元素非 null 且目标槽位非 null，就**无条件覆盖写入**，即使新旧值完全相同也会赋值。跳过只发生在两种情况下：

- 源元素为 null（无数据来源，C4 保持原值）
- 目标槽位为 null（无可更新对象，C5 no-op）

这是**数据驱动的跳过**，不是变化驱动的跳过：不比较新旧值，不提供"相同则不写"的优化。

### 多路径保障：四条执行路径 null 语义一致

`MapListInto` 根据类型注册状态与配置走不同执行路径，但 **C4/C5 语义在每条路径上完全一致**：

| 执行路径 | 触发条件 | 元素级 null 处理 |
|---|---|---|
| Pipeline 内联 | Pipeline 开启时 | src null 保持原值（C4）；dst null 跳过（C5） |
| 源生成器批量委托 | [MapFrom] 已注册类型 | 数组快速路径与通用索引循环均有双重 null 守卫（C4/C5） |
| UpdateAction 循环 | 生成 Update 方法可用 | src null 保持原值（C4）；dst null 由生成方法内部防护 |
| MapperBase 回退 | 未注册类型运行时路径 | 防御性 return + 元素级 C4/C5 |

无论走哪条路径，null 处理行为一致：已注册类型与未注册类型、Pipeline 开关与否，得到的语义完全相同。

## 语义风险

- **C5 未初始化 no-op**：新数组元素默认全 null，映射被静默跳过且不报错。预分配后务必先初始化每个元素
- **C6 残留**：数组复用时未被映射覆盖的属性保留上轮值，业务状态变化时必须自行清理
- **C7 嵌套新建**：嵌套引用每轮新建子对象，需要全图零分配时嵌套对象也要自行复用

## 与 MapList 的对比

| 维度 | `MapList` | `MapListInto` |
|---|---|---|
| 目标集合 | 每次新建 | 复用调用方预分配数组 |
| 元素对象 | 每元素新建实例 | 复用既有元素，仅覆盖映射字段 |
| 分配（复用场景） | 每元素 1 个新目标对象 | 顶层零分配（嵌套引用每轮新建，C7） |
| 未映射字段 | 默认值 | 保留上轮残留值（C6） |
| 源元素为 null 时 | 新列表对应位置为 null | 目标槽位保持原值（C4，不置 null） |
| 适用场景 | 一次性映射 | 高频批量：分页、流式、UI 绑定缓冲 |

## 配套示例

可运行演示位于 `samples/NCMapper.Samples/`：

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 201   # 基础映射：201-list-map-into / 201-list-map-into-reuse 子场景
dotnet run --scenario 604   # 性能对比：604-maplist-into（MapList vs MapListInto 吞吐）
```

核心示例源码（单点同源，`dotnet run --scenario` 可运行验证）：

[!code-csharp[MapListInto 批量就地映射](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapListInto)]

复用循环与超量槽位保持（C3）见场景 604 的性能演示（`06-Infrastructure/PerformanceScenarios.cs`，`dotnet run --scenario 604`）；吞吐对比见该文件 `MeasureMapListIntoPerformance` 方法。

## 相关文档

- [映射规则（语义）集合与字典](../../explanation/semantics/mapping-rules.md)：MapList 常规集合映射的引擎语义与集合类型矩阵
- [性能](../infrastructure/performance.md)：集合场景性能实测数据
