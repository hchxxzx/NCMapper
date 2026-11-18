---
wiki-id: reference/attributes-index
库版本: v1.0.0+
更新时间: 2026-10-23
---

# Attribute 速查表

> 全部特性一览（17 个）。特性通道体系定位与 Fluent 对照见 [api-comparison](api-comparison.md)；引擎级语义见语义共享层（[映射规则](../explanation/semantics/mapping-rules.md)、[方向语义](../explanation/semantics/direction-semantics.md)）。

## 对级声明

| 特性 | 适用位置 | 用途 | Fluent 等价终端 |
| ---- | -------- | ---- | --------------- |
| `[MapFrom(typeof(源))]` | DTO 类 | 声明映射对（双向语义，一次声明承载正反两向） | `cfg.For<源, 目标>()` |
| `[MapperConfig]` | 配置类 | 标记 Fluent 配置入口类（Configure 方法宿主） | （Fluent 通道入口本身） |
| `[MapDefaults]` | 程序集 | 全局默认映射配置（命名策略/忽略清单） | 无独立 Fluent 终端（程序集级声明；运行时经 `Mapper.GetGlobalConfig()` 读只读视图） |

## 属性级声明

| 特性 | 适用位置 | 用途 | Fluent 等价终端 |
| ---- | -------- | ---- | --------------- |
| `[MapProperty("源", "目标")]` | 目标属性 | 异名映射；跨表字段用 `typeof(SubSource)` 双参形态 | `.Member(d => d.目标).From(s => s.源)` |
| `[MapProperty(IsFromJson = true)]` | 目标属性 | JSON 字段展开（string ↔ 对象双向） | `.Member(d => d.X).FromJson()` |
| `[MapProperty(FromSource = typeof(子源))]` | 目标属性 | Compose 跨源绑定声明 | `.Member(d => d.X).FromSource<子源>(s => s.Y)`（速记）/ `.FromSource<子源>(cfg => …)`（对级分组块） |
| `[MapIgnore]` / `[MapIgnore(Direction)]` | 属性 | 方向性忽略（双向/ToDto/ToEntity） | `.Member(d => d.X).Ignore()` / `.Ignore(direction)` |
| `[Convert]` | 目标属性 | 正向自定义转换（静态方法引用 4 形态） | `.Convert`（4 形态模板，Member 段内） |
| `[ConvertBack]` | 目标属性 | 反向自定义转换（4 形态） | `.ConvertBack`（4 形态模板，Member 段内） |
| `[MapFormat("格式串")]` | 目标 string 属性 | 属性级格式化：源 `IFormattable` 按格式串输出为文本（日期/数值/枚举等，正向单向声明；反向不赋值 `NCMAP046`，往返需补 `[ConvertBack]`；与 `[Convert]` 互斥 `NCMAP045`） | `.Format(format)`（Member 段内，含 `overrideIgnore` 豁免门） |
| `[Mask(typeof(掩码实现))]` | 目标 string 属性 | 输出脱敏（目标侧标注；源侧不生效报 `NCMAP026`） | `.Member(d => d.X).Mask<TMask>()` |
| `[MapWhen(nameof(谓词))]` | 目标属性 | 条件守卫（命名方法形态） | `.MapWhen`（命名 + 内联双形态，Member 段内） |
| `[MapNoAutoInclude]` | 属性 | 该属性跳过 ORM 投影导航收集 | `Member` 段内 `NoAutoInclude`（字段级，无参） |
| `[MapTree]` | 类 | 树挂载位三元组声明（默认 Id/ParentId/Children） | `Tree(id, parent, children)`（对级） |

> **对级白名单**：`[MapNoAutoMatch]`（类级）对应 Fluent 侧 `.NoAutoMatch()`（对级），与上表属性级 `[MapNoAutoInclude]` 粒度不同、双承载并行而非等价——前者关整对的自动匹配，后者只让单个属性跳过导航收集。二者语义辨析见 [Fluent 基础：自动匹配与白名单双模式](../how-to/fluent/basics.md)。

## 类级声明（钩子与多态）

| 特性 | 适用位置 | 用途 | Fluent 等价终端 |
| ---- | -------- | ---- | --------------- |
| `[BeforeMapping]` / `[AfterMapping]` | DTO 类 | 生命周期钩子（`void(TSource, TTarget)` 契约，`NCMAP038/039/040` 校验） | `.BeforeMapping` / `.AfterMapping` |
| `[MapSubclass(typeof(派生源), typeof(派生目标))]` | DTO 类（多实例） | 多态子类型分派登记（isinst 链） | `.MapSubclass<TSrcDerived, TTargetDerived>()`（对级） |

## 诊断与运行时

| 特性 | 用途 |
| ---- | ---- |
| `[NCMapperOptions]` | 程序集/类级映射诊断配置（`ReportUnmappedTargetMembers`/`ReportUnmappedSourceMembers` 开关，读取优先级：类级 > 程序集级 > 默认） |

`[NCMapperOptions]` 控制编译期诊断的严格程度：`ReportUnmappedTargetMembers`（目标未映射成员是否上报，默认 false）与 `ReportUnmappedSourceMembers`（源未消费成员是否上报，默认 true）。标注在程序集（`[assembly: NCMapperOptions(...)]`）或单个 DTO 类上，类级优先于程序集级，未标注走默认值。与运行时开关 `Mapper.ValidationEnabled`（DI `NCMapperOptions` 类）无关——本特性只影响源生成器的诊断产出。


引用合法性校验诊断（方法引用形态四模板、钩子契约、谓词签名）全集见 [diagnostics/rules.md](../diagnostics/rules.md)。
