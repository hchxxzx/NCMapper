---
wiki-id: declarative/map-format
涉及诊断码: NCMAP019, NCMAP021, NCMAP022, NCMAP043, NCMAP044, NCMAP045, NCMAP046
库版本: v1.1.0+
更新时间: 2026-09-30
---

# 属性级格式化（MapFormat 通道）

## 概述

`[MapFormat("格式串")]` 让字段映射直接把源值按格式串输出为文本——日期、数字、枚举等常见展示场景不必再写 `[Convert]` 静态转换器（静态类 + 静态方法）。

**适用**：单字段 → 文本的格式转换（`yyyy-MM-dd`、`N2`、`D`）。
**不适用**：多字段组合、业务规则运算、需要固定文化输出——继续用 `[Convert]`。

**源类型白名单**：`DateTime`、`DateTimeOffset`、`TimeSpan`、`Guid`、全部数值类型、**枚举**——均可带 `Nullable<T>` 包装（枚举经 `IFormattable` 支持，net10 实测 `"D"`→`1`、`"G"`→`Monday`、`"X"`→`00000001`）。

> 本页是特性通道写法；Fluent 通道对等终端 `.Format(...)` 见 [Fluent 格式化](../fluent/format.md)。两通道同一快照承载、同一套守门，产物由等价测试锁定。可运行演示见 samples 场景 `202`。

## 基础用法

```csharp
[MapFrom(typeof(Order))]                      // ← 映射锚：无锚时属性级标注不消费（编译期报 NCMAP019）
public class OrderDto
{
    [MapFormat("yyyy-MM-dd")]                 // 日期 → 文本
    public string CreatedAtText { get; set; }

    [MapFormat("N2")]                         // 数值 → 千分位 + 两位小数
    public string AmountText { get; set; }

    [MapFormat("D")]                          // 枚举 → 整数形式（G=名称 / X=十六进制 / F=组合标志）
    public string StatusCode { get; set; }

    [MapFormat("hh\\:mm")]                    // TimeSpan（自定义格式串中冒号与句点须转义）
    public string DurationText { get; set; }

    [MapFormat("D")]                          // Guid（格式串仅认 N/D/B/P/X 五种）
    public string OrderIdText { get; set; }

    [MapFormat("yyyy-MM-dd")]
    public string? ShippedDate { get; set; }  // 源为 DateTime?；null 时目标为 null（不抛异常）
}
```

## 组合形态

### 跨字段绑定（与 [MapProperty] 组合）

```csharp
[MapProperty(typeof(Order), nameof(Order.CreatedAt))]   // 源字段与目标属性不同名时用 MapProperty 绑定
[MapFormat("yyyy-MM-dd")]
public string CreatedDate { get; set; }
```

### 先格式化再脱敏（与 [Mask] 组合）

```csharp
[MapFormat("N2")]                              // 执行顺序固定：先 ToString("N2")，再 Mask(...)
[Mask(typeof(MaskBankCard))]
public string CardNoMasked { get; set; }
```

### 正向格式化 + 反向反解析（与 [ConvertBack] 组合）

```csharp
[MapFormat("yyyy-MM-dd")]                      // 正向（Entity→DTO）：格式化输出
[ConvertBack(typeof(DateParser), nameof(DateParser.Parse))]   // 反向（DTO→Entity）：走 ConvertBack 反解析
public string BirthDate { get; set; }
```

> 两个特性方向正交、互不干扰。**只标 `[MapFormat]` 的属性反向映射时不赋值**（该字段保持实体侧原值，其余字段照常回写）——格式化输出有损且随文化，库不猜测反解析，仅给 NCMAP046 警告提示；该属性需要往返时再自行补 `[ConvertBack]`。

### 全局忽略豁免门

```csharp
[MapFormat("N2", OverrideIgnore = true)]       // 命中全局忽略清单时仍生效（唯一豁免门）
public string TotalText { get; set; }
```

## 守门与诊断码速查

| 写法 | 结果 |
|---|---|
| 目标属性非 `string` | **NCMAP043**（Error） |
| 源属性非 `IFormattable`（如 `string` 源、自定义 struct） | **NCMAP044**（Error） |
| 与 `[Convert]` 同标 | **NCMAP045**（Error，二选一） |
| 反向命中且未标 `[ConvertBack]` | **NCMAP046**（Warn——反向跳过该属性，不赋值） |
| 无锚类型上标注 | **NCMAP019**（既有码，家族口径） |
| 带 `OverrideIgnore=true` 且同标 `[MapIgnore]` | **NCMAP022**（既有码，矛盾诊断；忽略生效） |
| 裸 `[MapFormat]` + `[MapIgnore]` | 静默按忽略处理（Ignore 判定前置，家族口径） |
| 命中全局忽略清单且无豁免 | 按忽略跳过 + **NCMAP021** 指引（既有码） |
| 格式串为空/空白 | 特性构造器即抛 `ArgumentException` |

## 行为要点（上生产前需知）

| 要点 | 说明 |
|---|---|
| **文化** | 默认**当前文化**（`IFormattable.ToString(格式串, null)` 语义）。与现状"未标注属性恒 Invariant 输出"是**有意分叉**——同一字段加标注后输出会随机器文化变化（如 `1234,56`）；需要跨机器恒定的输出请用 `[Convert]` 自控文化 |
| **双引擎** | 源生成路径：格式串作为编译期常量内嵌（零反射、AOT 友好）；运行时回退路径：表达式树同语义。两路径产物一致 |
| **投影（EFCore/FreeSql AsDto）** | 自动降级为二步映射（先 SQL 查实体、再内存格式化）——与用 `[Convert]` 格式化同型；**降级按映射对整体生效**（DTO 上任一属性带标，整个 DTO 对的 AsDto 走二步） |
| **Dapper** | 零影响（Dapper 通道本就先查实体再内存映射） |
| **格式串拼错** | 编译期不校验（.NET 格式串语义是运行时的），运行时报 `FormatException`；`Guid`/`TimeSpan`/枚举的格式串域较窄，见上文注释 |
| **枚举** | 拿日期格式串套枚举会在运行时抛 `FormatException`——枚举格式串为 G/D/X/F 等枚举专用形式 |

## 写法对照（与 [Convert] 的选择）

```csharp
// 旧写法：为一句格式串要建静态转换器类
public static class DateFmt
{
    public static string ToShort(DateTime v) => v.ToString("yyyy-MM-dd");
}
public class OrderDtoOld
{
    [MapProperty(typeof(Order), nameof(Order.CreatedAt))]
    [Convert(typeof(DateFmt), nameof(DateFmt.ToShort))]
    public string CreatedDate { get; set; }
}

// 新写法：一行格式串
public class OrderDtoNew
{
    [MapProperty(typeof(Order), nameof(Order.CreatedAt))]
    [MapFormat("yyyy-MM-dd")]
    public string CreatedDate { get; set; }
}
```

**选择口径**：单字段转文本 → `[MapFormat]`；多字段/业务规则/固定文化 → `[Convert]`。

## 可运行演示（场景 202）

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

## 相关页

- [Fluent 格式化](../fluent/format.md) — `.Format` 终端（与特性通道等价）
- [数据脱敏](../infrastructure/masking.md) — `[Mask]` 与 FormatThenMask 组合的脱敏侧语义
- [诊断码索引](../../diagnostics/rules.md) — NCMAP043–046 全量条目
- [Fluent 入口](../fluent/basics.md) — 双通道声明规则（F11 仲裁）
