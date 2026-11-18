---
wiki-id: fluent/from-source
涉及诊断码: NCMAP_F29-F32, F38, F45
库版本: Unreleased（随下个版本发布）
更新时间: 2026-10-23
---

# Fluent FromSource：跨源绑定

> Fluent 线跨源绑定专题。多源合成的运行时机制（Compose 合成面）见 [explanation/compose](../../explanation/compose.md)；方向语义见 [direction-semantics](../../explanation/semantics/direction-semantics.md)。

`FromSource<TSubSource>` 将**子源**类型字段绑定到目标 DTO 属性，主链 `For<TSource, TTarget>` 承载主源映射，子源字段经 FromSource 声明跨源供给（特性通道对应形态：类上 `[MapFrom(typeof(主源))]` + 属性上 `[MapProperty(FromSource = typeof(子源), ...)]`；终端名与特性侧 `FromSource` 属性一字对齐）。

## 两种声明形态

速记（单字段绑定，Member 头声明位——先 `.Member` 锚定目标字段，再 `FromSource<子源>(子源路径)` 绑定来源）：

可运行示例（`dotnet run --scenario 103` 实跑验证）：

[!code-csharp[FromSource 速记：单字段跨源绑定](~/samples/NCMapper.Samples/01-Fluent/FsModels.cs#FluentFromSource_Bindings)]

分组块（同一子源多字段；块接收者类型为 `FluentTypeConfig<TSubSource, TTarget>`——块内 = 对 (子源, 目标) 的 For 同型链，字段声明同样走 Member 三段式：`From`/`Mask`/`FromJson`/`Convert` 等，源锚点为子源）：

可运行示例（`dotnet run --scenario 103` 实跑验证）：

[!code-csharp[FromSource 分组块：同子源多字段（From + Mask + FromJson）](~/samples/NCMapper.Samples/01-Fluent/FsModels.cs#FluentFromSource_Bindings)]

## 双面向

同一 Fluent 声明下两个运行时面同时可用：

- **主源面** `Mapper.Map<Order, OrderDto>(order)`：仅填充主链字段，FromSource 命中字段保持默认（物化分流，FromSource 命中属性只入 `ComposeMappings` 不入 `PropertyMappings`）；
- **合成面** `Mapper.Compose<OrderDto>(order, customer, ...)`：主链字段 + 全部子源字段一次合成。

## 运行时形态与 AOT

- 生成器对 FromSource 目标发射专用合成委托并自动注册，`Mapper.Compose` 优先走该零反射生成路径（AOT 友好）；
- **AOT 豁免说明**：Fluent FromSource 声明复用特性通道同一 Compose 发射器，零新增发射形态，既有特性通道 Compose 的 AOT 覆盖直接承继；`Mapper.Compose` 上的 `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]` 仅约束反射兜底路径（`ComposeRuntime`），生成委托路径不受影响；
- 未注册生成委托时（无生成器或生成器关闭）回退 `ComposeRuntime` 反射路径，该路径要求目标标注 `[MapFrom]`，Fluent DTO 无此特性将抛 `MapConfigurationException`。

## 顺序覆盖

同一目标属性可由不同子源分别绑定（Fluent 独有能力，特性通道单字段单子源粒度不可表达）：`Mapper.Compose` 按传入数组顺序处理子源，**后写覆盖先写**。

## 声明冲突与块内禁区（NCMAP_F29~F32）

- `NCMAP_F29`（Error）：同 dest 声明冲突，双触发面，①主链任一字段段声明（`From` 来源 / `Ignore` / `Mask` / `MapTree` / `FromJson` / `Convert` / `ConvertBack`）与 FromSource 子源绑定指向同一属性（一个属性只能由一个源供给）；②同一子源上多个路径承载型绑定（`From` 速记 / 块内 `From` / `MapTree`）或多个 `Convert`（块内 `Mask`/`FromJson` 为修饰型附着不入计数，单个 `Convert` 与 `From` 组合按附着合并放行）；
- FromSource 绑定仅正向映射且无钩子注入口，块内以下终端报 Error：`ConvertBack`（F30，无反向写回落点）、对级禁用自动匹配开关 `NoAutoMatch`（F31——原对级旧名 `NoAutoInclude` 已改此名，本禁区随正名生效；子源字段全显式声明，块上下文无自动包含可禁）、`AfterMapping`（F32，生成管线无钩子注入口，声明永不执行）。

## Mask（块内已启用）

块内声明同样走 Member 三段式——`.Member(d => d.X).Mask<TMask>()` 已被物化消费：运行时合成值按声明脱敏，与主链 Mask 语义一致。块内同一字段叠加 `Format` 与 `Mask`（FormatThenMask 双标）同样支持——先格式化再脱敏，与主链 `From + Format + Mask` 形态等价（可运行演示见场景 109 的 `JoinedAt`）。

## 与特性通道对照

| Fluent 通道 | 特性通道 |
| ----------- | -------- |
| `.Member(d => d.X).FromSource<Sub>(s => s.Y)`（Member 头声明位速记，一条声明绑定一个子源字段） | `[MapProperty(FromSource = typeof(Sub), nameof(Sub.Y))]`（类上另有 `[MapFrom(typeof(主源))]`） |
| 块内 `c.Member(d => d.X).Convert<T1>(M)`（T1 = 源字段类型；目标类型随 Member 锚定） | `[MapProperty(FromSource = typeof(Sub), ...)]` + `[Convert(typeof(M类), nameof(M类.M))]` |
| 同一目标字段不同子源顺序覆盖 | 不可表达（单字段单子源粒度） |
