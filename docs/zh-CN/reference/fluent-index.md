---
wiki-id: reference/fluent-index
库版本: Unreleased（随下个版本发布）
更新时间: 2026-10-23
---

# Fluent 终端速查表

> 全部 Fluent 链式终端一览（**18 个终端名 / 45 个重载**，以源码 `FluentMappingConfig` 公开方法集为准；不含入口 `For`——计入入口则为 19 / 46）。通道选型对照见 [api-comparison](api-comparison.md)；特性通道对等速查见 [Attribute 速查表](attributes-index.md)；逐终端完整语义与可运行示例见 [Fluent 基础（链式声明入口）](../how-to/fluent/basics.md)。

## 链式入口

| 终端 | 签名 | 用途 |
| ---- | ---- | ---- |
| `For<TSource, TTarget>()` | `FluentTypeConfig<TSource, TTarget>` | 声明映射对，链式起点（每条链必须以 For 起头；等价特性形态 `[MapFrom(typeof(源))]`） |

## 三段式分层

字段级声明采用 **Member 三段式**：`.Member(dest)` 锚定目标字段 → 头声明（`From` / `FromSource` / `Ignore`）指定来源或忽略 → 配置段（`Mask` / `Format` / `FromJson` / `MapTree` / `Convert` / `ConvertBack` / `MapWhen` / `NoAutoInclude`）叠加变换。**字段名只写一次**，同字段多终端连写不再重复 dest lambda。

| 段位 | 承载类型 | 该段终端 |
| ---- | ---- | ---- |
| 第一段（锚定） | `FluentTypeConfig` → `MemberHead` | `Member<TProp>(dest)` |
| 第二段（头声明） | `MemberHead` | `From` / `FromSource`（速记）/ `Ignore` |
| 第三段（配置段） | `MemberHead` 或 `MemberConfig` | `Mask` / `Format` / `FromJson` / `MapTree` / `Convert` / `ConvertBack` / `MapWhen` / `NoAutoInclude` |
| 回到外层（对级） | `MemberConfig` / `AfterIgnore` 继承 `FluentTypeConfig` | `NoAutoMatch` / `Tree` / `BeforeMapping` / `AfterMapping` / `MapCtorParam` / `MapSubclass` / `FromSource`（分组块） |

> **两种宿主**：`Mask` / `Format` / `FromJson` / `MapTree` / `Convert` / `ConvertBack` / `MapWhen` / `NoAutoInclude` 各有两个起始位置——一在 `MemberHead`（省略 `From` 时按目标字段名解析同名源），一在 `MemberConfig`（已在配置段内，可与 `From` 显式来源叠用）。因此下表重载数按**终端名跨宿主合计**统计。

## 属性映射与转换

| 终端 | 重载数 | 宿主 | 用途 | 等价特性形态 |
| ---- | ------ | ---- | ---- | ------------ |
| `Member<TProp>(dest)` | 1 | `FluentTypeConfig` | 三段式第一段：锚定目标字段（`TProp` 由 dest lambda 推断，后续签名不再重复 dest） | —（特性侧以属性标注本身充当锚） |
| `From<TSrcProp>(src, overrideIgnore = false)` | 1 | `MemberHead` | 显式来源声明（支持嵌套源路径与 `!` 抑制；跨类型由生成代码内建转换承接，无可行转换报 `NCMAP027`） | `[MapProperty]` |
| `Convert(converter, overrideIgnore = false)` | 8 | `MemberHead` + `MemberConfig` | 正向自定义转换（四形态模板 × 两宿主：T0 `Func<TProp>` / T1 `Convert<TSrcProp>(Func<TSrcProp, TProp>)` / T2 `Func<TSource, TProp>` / T3 `Convert<TSrcProp>(Func<TSource, TSrcProp, TProp>)`） | `[Convert]` |
| `ConvertBack<TEntityProp>(converter, overrideIgnore = false)` | 8 | `MemberHead` + `MemberConfig` | 反向自定义转换（DTO→Entity，四形态模板 × 两宿主：T0 `Func<TEntityProp>` / T1 `Func<TProp, TEntityProp>` / T2 `Func<TTarget, TEntityProp>` / T3 `Func<TTarget, TProp, TEntityProp>`） | `[ConvertBack]` |
| `Format(format, overrideIgnore = false)` | 2 | `MemberHead` + `MemberConfig` | 属性级格式化：源 `IFormattable` 按编译期常量格式串输出为文本（目标须 string；正向单向声明，反向不赋值 `NCMAP_F67`；与 `.Convert` 同锚点互斥 `NCMAP_F66`） | `[MapFormat("格式串")]` |
| `FromJson()` | 2 | `MemberHead` + `MemberConfig` | JSON 字符串展开（源侧 string → 目标对象；反向序列化写回） | `[MapProperty(IsFromJson = true)]` |
| `MapTree()` | 2 | `MemberHead` + `MemberConfig` | 属性级树形递归映射（该字段递归走相同 (S,T) 配置）；与对级 `Tree()` 名近义远，见下节 | （特性侧无属性级等价） |

## 忽略与守卫

| 终端 | 重载数 | 宿主 | 用途 | 等价特性形态 |
| ---- | ------ | ---- | ---- | ------------ |
| `Ignore()` | 2 | `MemberHead` | 忽略目标字段；第二重载携带 `Direction`（方向性忽略）。独唱终态：其后仅可链 `NoAutoInclude` 唯一例外 | `[MapIgnore]` / `[MapIgnore(Direction)]` |
| `MapWhen(predicate)` / `MapWhen<TInput>(condition)` | 4 | `MemberHead` + `MemberConfig` | 条件守卫（真才映射）：T1 非泛型 `Func<TProp, bool>` 字段值谓词 / T2 显式类型参 `TInput` 整源谓词；两形态 × 两宿主 | `[MapWhen(nameof(谓词))]` |
| `NoAutoInclude`（无参） | 3 | `MemberHead` + `MemberConfig` + `AfterIgnore` | **字段级**禁用自动包含（该字段跳过 ORM 降级路径导航收集）。`AfterIgnore` 上的重载是 `Ignore()` 之后唯一可跟的操作 | `[MapNoAutoInclude]`（属性级） |

## 脱敏与树形

| 终端 | 重载数 | 宿主 | 用途 | 等价特性形态 |
| ---- | ------ | ---- | ---- | ------------ |
| `Mask<TMask>(overrideIgnore = false)` | 2 | `MemberHead` + `MemberConfig` | 输出脱敏（目标须为 string；`where TMask : IMask`；与特性侧共享 MaskProvider） | `[Mask(typeof(实现))]` |
| `Tree()` / `Tree(idProperty, parentIdProperty, childrenProperty)` | 2 | `FluentTypeConfig` | **对级**树构建配置：声明树挂载位三元组（`[MapTree]` 的链式等价，供 `ToTreeList` 消费）；opt-in，未调用时树挂载位判定恒 false | `[MapTree]` |

## 跨源与构造器

| 终端 | 重载数 | 宿主 | 用途 | 等价特性形态 |
| ---- | ------ | ---- | ---- | ------------ |
| `FromSource<TSubSource>(src)` | 1 | `MemberHead` | 子源速记来源声明（一条声明绑定一个子源属性；`object` 占位，真实类型经符号解析恢复） | `[MapProperty(FromSource = typeof(子源))]` |
| `FromSource<TSubSource>(configure)` | 1 | `FluentTypeConfig` | **对级**分组块：同一子源多字段成组声明（块内 = `For` 同型链） | `[MapProperty(FromSource = typeof(子源))]` |
| `MapCtorParam<TSrcProp>(paramName, sourceProp)` / `MapCtorParam(paramName, sourcePropertyName)` | 2 | `FluentTypeConfig` | 显式构造器形参绑定（Fluent 独有；形态 1 源 lambda 为主形态，形态 2 源属性名字符串为辅助形态） | （无特性侧形态） |

## 生命周期与多态

| 终端 | 重载数 | 宿主 | 用途 | 等价特性形态 |
| ---- | ------ | ---- | ---- | ------------ |
| `BeforeMapping(handler)` | 1 | `FluentTypeConfig` | 生命周期前置钩子（`Action<TSource, TTarget>`；须可静态解析的方法组引用） | `[BeforeMapping]` |
| `AfterMapping(handler)` | 1 | `FluentTypeConfig` | 生命周期后置钩子（发射于共享核心方法末尾，6 正向形态继承；反向 4 形态不发射） | `[AfterMapping]` |
| `MapSubclass<TSrcDerived, TTargetDerived>()` | 1 | `FluentTypeConfig` | 多态子类型分派登记（isinst 链，对级；按源侧/目标侧继承距离「最派生优先」仲裁） | `[MapSubclass]` |

## 自动匹配开关（对级）

| 终端 | 重载数 | 宿主 | 用途 | 等价特性形态 |
| ---- | ------ | ---- | ---- | ------------ |
| `NoAutoMatch()` | 1 | `FluentTypeConfig` | **对级**白名单开关：该 (S,T) 对禁用自动匹配/自动包含（含自动扁平化），仅映射显式声明与同名默认 | `[MapNoAutoMatch]`（类级） |

> **辨析（同形不同义）**：`NoAutoMatch()` 是**对级**开关，`NoAutoInclude`（无参终端）是**字段级**操作。字段级写法嵌在 `Member` 段内——锚定字段之后再写无参 `NoAutoInclude`（如 `.Member(d => d.AuditLogs)` 之后接该终端），与旧对级名同形但语义不同，移植代码时勿把字段级写法误当对级开关改写。

## 豁免门（overrideIgnore）

`From` / `Mask` / `Format` / `Convert` / `ConvertBack` 五终端各带 `bool overrideIgnore = false` **可选参数**（不是独立重载）：全局忽略清单（`[assembly: MapDefaults]` 的 `IgnoreNames` 等）命中该字段时豁免，映射/脱敏/转换/格式化照常生成。语义镜像特性侧 `OverrideIgnore` 命名参数，正反向双链生效。全局忽略清单与豁免的完整规则见 [全局映射配置](../how-to/infrastructure/global-config.md)。

## 诊断

终端声明违反约束时的编译期诊断码（`NCMAP_F` 族）逐码条目分布见 [诊断规则](../diagnostics/rules.md)（该页为诊断码**主表 + 分页转指**）；三原则（通道互斥/侧别互斥/配置跟随类锚）与检测器对照同页。

## 深入阅读

- [Fluent 基础（链式声明入口）](../how-to/fluent/basics.md) — 链式声明、豁免门、树形三语义辨析与全族诊断索引
- [Fluent 四终端](../how-to/fluent/terminals.md) — MapWhen/MapCtorParam/MapSubclass 专题
- [Fluent 转换](../how-to/fluent/convert.md) — Convert/ConvertBack 专题
- [Fluent 格式化](../how-to/fluent/format.md) — Format 终端专题（守门诊断 F64–F67）
- [Fluent 跨源绑定](../how-to/fluent/from-source.md) — FromSource 专题
