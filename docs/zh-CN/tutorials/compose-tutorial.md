---
wiki-id: tutorials/compose-tutorial
涉及诊断码:
库版本: v1.0.0+
更新时间: 2026-08-21
---

# 多源合成（Compose）场景演练

## 概述

本教程解决一个具体业务问题：列表页要展示的数据分散在多个实体中，如何一次合成一个 DTO。你会在一个订单场景中完成双源合成、三源合成与批量合成，并验证结果。

示例来自 `samples/NCMapper.Samples` 的 02 场景，可运行验证。合成能力的 API 详解见 [Compose 多源合成](../how-to/compose/compose.md)，本教程侧重业务场景的完整决策过程。

---

## 1. 场景：订单列表页的数据从哪来

订单列表页需要同时展示三类信息：

| 数据 | 来源 | 说明 |
|------|------|------|
| 订单号、金额、状态 | `OrderEntity` | 主源，每行一条 |
| 客户姓名、等级、电话 | `CustomerEntity` | 副源，按客户关联 |
| 商品信息 | `ProductEntity` | 第三源，报表场景 |

两种做法：

- **先查订单，再逐条查客户**：N+1 次查询，列表页性能差
- **组装一个聚合 DTO**：一次查询合成，页面直接绑定

`Mapper.Compose` 是第二种做法的实现：从多个源对象合成一个目标 DTO，副源字段用 `[MapProperty]` 显式声明来源。

对比两种方案的取舍：

| 维度 | 逐条查询 + 内存拼接 | Compose 合成 |
|------|--------------------|-------------|
| 查询次数 | N+1（订单 1 次 + 每条订单 1 次客户） | 1 次（或按数据获取方式而定） |
| 映射配置 | 手工拼接代码，分散在各页面 | DTO 内声明，一处定义多处复用 |
| 可维护性 | 字段增删要改拼接代码 | 改 DTO 声明即可 |
| 适合场景 | 数据源无法一次性获取 | 数据已齐备，组装视图模型 |

Compose 适合"数据已就绪、需要组装展示模型"的场景；数据本身分布在不同存储（跨服务、跨库）时仍应分层获取后再合成。

---

## 2. 定义合成 DTO

定义一个 `OrderDetailDto`，标注 `[MapFrom(typeof(OrderEntity))]` 声明主源，副源字段（客户信息）用 `[MapProperty(typeof(CustomerEntity), ...)]` 声明来源：

[!code-csharp[OrderDetailDto 合成 DTO 定义](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto)]

[!code-csharp[OrderDetailDto 合成 DTO 定义 · OrderId](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_OrderId)]

[!code-csharp[OrderDetailDto 合成 DTO 定义 · OrderNumber](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_OrderNumber)]

[!code-csharp[OrderDetailDto 合成 DTO 定义 · TotalAmount](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_TotalAmount)]

[!code-csharp[OrderDetailDto 合成 DTO 定义 · StatusText](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_StatusText)]

[!code-csharp[OrderDetailDto 合成 DTO 定义 · CustomerName](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_CustomerName)]

[!code-csharp[OrderDetailDto 合成 DTO 定义 · CustomerLevel](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_CustomerLevel)]

[!code-csharp[OrderDetailDto 合成 DTO 定义 · CustomerPhone](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_CustomerPhone)]

要点：

- **主源**由 `[MapFrom]` 声明，主源属性按名称约定自动匹配；异名时用 `[MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderId))]` 声明（见 `OrderId`、`OrderNumber`）
- **副源**字段一律用 `[MapProperty(typeof(副源类型), nameof(副源属性))]` 声明，合成时按声明从对应源取值
- 枚举到文本的转换（`StatusText`、`CustomerLevel`）声明方式与普通属性一致，映射器按类型转换路径处理

`[MapProperty]` 的第一个泛型参数指明字段来源类型，这是合成 DTO 的核心契约：属性与数据源一一对应，编译期即确定。

本场景涉及的源实体字段：

| 源实体 | 字段 | DTO 目标属性 |
|--------|------|-------------|
| `OrderEntity`（主源） | `OrderId` / `OrderNo` / `Amount` / `Status` | `OrderId` / `OrderNumber` / `TotalAmount` / `StatusText` |
| `CustomerEntity`（副源） | `Name` / `Level` / `Phone` | `CustomerName` / `CustomerLevel` / `CustomerPhone` |

主源属性即使名称不同（`OrderId` → `OrderId`）也需显式声明；副源属性全部显式声明来源类型。

---

## 3. 双源合成：订单 + 客户

调用 `Mapper.Compose<TTarget>(source1, source2)` 合成：

[!code-csharp[双源合成](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose)]

[!code-csharp[双源合成 · MultiSourceCompose](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose_MultiSourceCompose)]

[!code-csharp[双源合成 · ComposeWithAction](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose_ComposeWithAction)]

合成结果：`OrderId` / `OrderNumber` / `TotalAmount` 来自 `order`，`CustomerName` / `CustomerLevel` / `CustomerPhone` 来自 `customer`。

与单源映射的区别：`Compose` 的参数数量对应 `[MapFrom]` + 副源声明，`[MapFrom]` 指定的类型是主源，后续参数按 `[MapProperty]` 声明的来源类型对号入座。

| 维度 | 单源映射（`Mapper.Map`） | 合成（`Mapper.Compose`） |
|------|------------------------|------------------------|
| 源数量 | 1 | 2-3 |
| 主源声明 | `[MapFrom]` | `[MapFrom]`（按类型定位，位置不限） |
| 副源字段 | 不适用 | 必须 `[MapProperty]` 显式声明 |
| 反向扩展 | 写回全部字段 | 只写回主源字段 |
| 典型场景 | 实体 ↔ DTO 单向/双向 | 多表聚合视图模型 |

---

## 4. 三源合成：订单 + 客户 + 商品

报表场景需要第三源，直接加参数：

[!code-csharp[三源合成](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose)]

[!code-csharp[三源合成 · MultiSourceCompose](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose_MultiSourceCompose)]

[!code-csharp[三源合成 · ComposeWithAction](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose_ComposeWithAction)]

`Compose<T>` 支持任意数量源（本文演示至三源）。主源按 `[MapFrom]` 声明的类型定位（**位置不限，不必在第一个参数**）；其余参数顺序与类型需与 DTO 中 `[MapProperty]` 声明的来源类型对应，运行时按类型对号入座。

---

## 5. 批量合成：订单列表

列表页不是单条数据，而是整页。用 `ComposeList` 批量合成：

[!code-csharp[批量合成列表](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_ComposeList)]

`ComposeList` 接收"源对象数组的数组"：外层数组的每个元素对应一个 DTO，内层数组是该 DTO 的全部源对象（主源 + 副源）。源列表长度必须一致，否则映射错位（见常见坑 3）。

批量合成适合两类输入：

- **已分组的数据**：订单与客户各查一批，按索引一一对应
- **查询结果直接配对**：主查询与关联查询按同一顺序返回

注意事项：

- 每组源对象的**顺序必须一致**：第一组是 (订单1, 客户1)，第二组是 (订单2, 客户2)，不能混排
- `ComposeList` 逐组调用同一份合成配置，与 `Compose` 单条结果一致
- 数据量大时分批合成（如按页），避免一次性构造超大数组

---

## 6. 合成后处理

合成结果需要统一加工（如加前缀、格式化），用内联 Action 回调：

[!code-csharp[合成后处理](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_L60)]

回调在合成完成后执行，参数为合成出的 DTO。适合页面展示层的前置加工，避免污染映射配置。

---

## 7. 反向直调（DTO → 主源实体）

声明跨表子源字段的 DTO 同样生成反向扩展，只写回主源字段：

[!code-csharp[Compose 反向直调](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_L83)]

反向扩展指向 `[MapFrom]` 主源实体（`MapToOrderEntity`），副源字段（客户信息）不参与写回——子源字段的数据源（子源对象）不在 DTO 上留存引用，反向只覆盖主源字段。需要保留副源数据时，在业务层单独更新对应实体。

---

## 组合用法：投影取主表 + Compose 组装

列表页数据量大时，可以组合两种能力：先用 ORM 投影（`AsDto`）在 SQL 侧取主表 DTO 列表，再分批 `Compose` 组装副源信息。主表字段走投影、副源字段走合成，各取所长（投影见 [ORM 投影场景演练](./orm-projection-tutorial.md)）。

组合要点：

- 主表 DTO 与 Compose DTO 可以是同一类型：投影负责主源字段，副源字段留待合成补充
- 副源按主表外键批量查询一次，避免逐条查询
- 分批处理时每组源对象保持顺序一致

## 运行验证

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 301
```

场景 301 输出双源、三源、批量、回调、反向各子场景的映射结果。运行前确认已安装 ORM 无关，Compose 属于核心库能力，不依赖任何扩展包。

## 验证合成配置

合成 DTO 同样参与映射验证。`Mapper.Validate()` 会扫描 `[MapFrom]` 类型对，包括 Compose 场景：

- `[MapProperty]` 声明的来源类型或字段名不存在时，编译期给出诊断，运行时 `Validate()` 也会报告
- 主源与副源类型写反（把副源类型填进 `[MapFrom]`）会在合成调用时暴露

建议在合成 DTO 定义完成后立即运行一次 `Validate()`，确认所有 `[MapProperty]` 来源合法（[验证与诊断](../how-to/infrastructure/validation.md)）。

## 常见坑

| 坑 | 现象 | 改法 |
|----|------|------|
| 副源字段漏标 `[MapProperty]` | 合成 DTO 中副源字段保持默认值，无提示 | 副源字段一律 `[MapProperty(typeof(副源类型), nameof(...))]` 声明 |
| 混淆主源与副源 | 副源字段被主源同名属性覆盖或漏映射 | 主源 = `[MapFrom]` 类型；其余全部显式声明来源 |
| `ComposeList` 源数组长度不一致 | 映射错位，出现错误数据 | 确保每组源对象数量与顺序一致 |
| 反向直调期望写回副源 | 副源字段在实体上无变化 | 副源写回需在业务层单独处理（见第 7 节） |

---

## 速查表

| 场景 | API | 说明 |
|------|-----|------|
| 声明合成 DTO | `[MapFrom(typeof(主源))]` + `[MapProperty(typeof(副源), ...)]` | 主源按名匹配，副源显式声明 |
| 双源合成 | `Mapper.Compose<T>(src1, src2)` | 主源 + 一个副源 |
| 多源合成 | `Mapper.Compose<T>(src1, src2, src3, ...)` | 任意数量源（演示至三源） |
| 批量合成 | `Mapper.ComposeList<T>(sourceGroups)` | 源对象数组的数组 |
| 合成后处理 | `Mapper.Compose<T>(dto => ..., sources)` | 内联 Action 回调 |
| 反向直调 | `dto.MapToOrderEntity()` | 只写回主源字段（子源字段不参与） |

---

## 相关文档

- [Compose 多源合成](../how-to/compose/compose.md)：API 详解与 AOT 行为
- [快速开始](../getting-started/tutorial-15min.md)：15 分钟基础映射演练
- [ORM 投影场景演练](./orm-projection-tutorial.md)：查询侧合成 DTO
