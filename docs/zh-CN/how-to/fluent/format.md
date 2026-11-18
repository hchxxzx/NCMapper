---
wiki-id: fluent/format
涉及诊断码: NCMAP_F64, NCMAP_F65, NCMAP_F66, NCMAP_F67
库版本: v1.1.0+
更新时间: 2026-10-23
---

# Fluent 格式化（.Format 终端）

## 概述

`.Format(format[, overrideIgnore])` 是格式化的 Fluent 通道终端（Member 三段式配置段）——语义镜像特性通道 `[MapFormat]`：源须实现 `IFormattable`（DateTime/DateTimeOffset/TimeSpan/Guid/数值/枚举，含 `Nullable<T>` 包装），目标须为 string。两通道同一快照承载、同一套守门与判定，产物由等价测试锁定。

终端签名（配置段，`MemberHead` / `MemberConfig` 两个位置均可直跟；`TProp` 由 `.Member` 锚定后从签名消失）：

```csharp
MemberConfig<TSource, TTarget, TProp> Format(
    string format,
    bool overrideIgnore = false);
```

## 基本用法

入口形态与既有 Fluent 声明一致（`[MapperConfig]` + 静态 `Configure(FluentMappingConfig)` 方法），锚定字段走 `.Member` 三段式：

```csharp
[MapperConfig]
public class OrderFluentConfig
{
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<Order, OrderDto>()
           .Member(d => d.CreatedAtText).Format("yyyy-MM-dd")               // 日期
           .Member(d => d.AmountText).Format("N2")                          // 数值
           .Member(d => d.StatusCode).Format("D")                           // 枚举
           .Member(d => d.DurationText).Format("hh\\:mm")                   // TimeSpan（冒号须转义）
           .Member(d => d.CardNoMasked).Format("N2", overrideIgnore: true); // 全局忽略豁免门（镜像特性侧）
    }
}
```

- `For<Order, OrderDto>()` 本身就是映射锚，无需 `[MapFrom]`；
- `.Member` 的目标属性路径为单段纯属性路径（多段/索引段形态不支持——F55/F06 同款拦截）；
- 源侧按目标字段名自动匹配同名属性（与 `.Mask` 同锚点语义）；跨字段绑定走 `.From` 显式来源组合（"自定义来源 + 格式化"叠用）或在特性通道用 `[MapProperty]`；
- 格式串须为编译期常量（非常量实参静默跳过声明——编译器已报类型错误的场景除外）。

## 守门诊断（F64–F67）

| 写法 | 结果 |
|---|---|
| `.Member` 锚定的目标属性非 `string` | **NCMAP_F64**（Error） |
| 源属性不可达/非 `IFormattable` | **NCMAP_F65**（Error） |
| 与 `.Convert` 同锚点 | **NCMAP_F66**（Error，二选一） |
| 反向派生命中且未标 `.ConvertBack` | **NCMAP_F67**（Warn——反向跳过该属性，不赋值） |

## 行为要点

- **文化口径**：恒传 null provider（= 当前文化）。与未标注属性恒 Invariant 输出为有意分叉；需要跨文化恒定输出请用 `.Convert` 自控。
- **三类窄格式域**：Guid 仅认 N/D/B/P/X；TimeSpan 自定义格式串冒号句点须转义（`hh\:mm`）；枚举格式串为 G/D/X/F 等枚举专用形式。
- **反向跳过**：`.Format` 是单向展示声明——裸声明完全合法；反向派生遇到时该属性不赋值（保持实体侧原值，其余字段照常），仅报 F67 警告。需要往返的属性用 `.Format` + `.ConvertBack` 同锚配对（组合时反向照常消费、无警告）。
- **投影**：与特性通道同口径——EFCore/FreeSql AsDto 自动降级二步映射。

## 可运行演示（场景 202）

[!code-csharp[Fluent 侧 .Format 终端与 .ConvertBack 往返配对（与特性通道同承载、同守门、同产物）](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_FluentChannel)]

[!code-csharp[Fluent 侧 .Format 终端与 .ConvertBack 往返配对（与特性通道同承载、同守门、同产物） · MfFluentDto_CreatedAt](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_FluentChannel_MfFluentDto_CreatedAt)]

[!code-csharp[Fluent 侧 .Format 终端与 .ConvertBack 往返配对（与特性通道同承载、同守门、同产物） · MfFluentDto_Amount](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_FluentChannel_MfFluentDto_Amount)]

[!code-csharp[Fluent 侧 .Format 终端与 .ConvertBack 往返配对（与特性通道同承载、同守门、同产物） · MfFluentDto_ShippedDate](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_FluentChannel_MfFluentDto_ShippedDate)]

[!code-csharp[Fluent 侧 .Format 终端与 .ConvertBack 往返配对（与特性通道同承载、同守门、同产物） · MfFluentConfig](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_FluentChannel_MfFluentConfig)]

[!code-csharp[Fluent 侧 .Format 终端与 .ConvertBack 往返配对（与特性通道同承载、同守门、同产物） · MfFluentConfig_Configure](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_FluentChannel_MfFluentConfig_Configure)]

## 相关页

- [MapFormat 通道](../declarative/map-format.md) — 特性通道写法（两通道等价）
- [Fluent 转换](convert.md) — `.Convert` 终端（多字段/固定文化场景）
- [Fluent 四终端](terminals.md) — 既有终端速查
- [诊断码索引](../../diagnostics/rules.md) — NCMAP_F64–F67 全量条目
