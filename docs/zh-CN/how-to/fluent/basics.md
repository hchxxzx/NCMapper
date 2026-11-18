---
wiki-id: fluent/basics
涉及诊断码: NCMAP_F02, F04-F08, F11, F15, F16, F17, F23, F59, F60, F64-F67, F68-F71, NCMAP019
库版本: Unreleased（随下个版本发布）
更新时间: 2026-10-23
---

# Fluent 基础（链式声明入口）

> Fluent 线总入口（第一 API 面）。本篇承载链式基础（Member 三段式）、忽略族、Tree 三语义、自动匹配与白名单双模式与全族诊断索引；终端专题见 [terminals](terminals.md)、[convert](convert.md)、[from-source](from-source.md)、[hooks](hooks.md)；跨面对照见 [api-comparison](../../reference/api-comparison.md)。

## 概述

NCMapper 推荐以 **Fluent 链式入口**声明映射：在独立的 **Mapper 配置类**上标注 `[MapperConfig]`，并在类内声明 `static void M(FluentMappingConfig cfg)` 方法（**方法名任意**），以链式调用声明映射。偏好"标注直接贴在 DTO 类上"的，可用等价的特性通道——在 DTO 上标注 `[MapFrom]` 声明映射意图（见 [基础映射](../declarative/basics.md)）。

> **锚本体**：`[MapperConfig]` 对应的特性类型为 `MapperConfigAttribute`（只能标注在类上，每个类至多一个）（源码锚：MapperConfigAttribute.cs:17）。

两条通道**长期并存（模式并存）**，你可以对不同 `(Source, Target)` 类型对自由混合使用；仅当**同一类型对被两条通道同时声明**时，编译期报 `NCMAP_F11` 互斥禁止（双位置各报一条，消息互指）。

这里"同一类型对"按**无序类型对**判定：`{S, T}` 与 `{T, S}` 是同一对——Fluent 侧声明 `For<A, B>()` 之后，再声明 `For<B, A>()` 或在 A/B 任一类上标注 `[MapFrom]` 都会触发互斥（无序互指由 `NCMAP025` 检测）。不同类型对之间互不影响，可自由混用两条通道。

可运行示例（`dotnet run --scenario 101` 实跑验证）：

[!code-csharp[Fluent 基础链：Member 三段式 From/Ignore/Mask + AfterMapping 连写](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper)]

[!code-csharp[Fluent 基础链：Member 三段式 From/Ignore/Mask + AfterMapping 连写 · Configure](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_Configure)]

[!code-csharp[Fluent 基础链：Member 三段式 From/Ignore/Mask + AfterMapping 连写 · ApplyComputedFields](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_ApplyComputedFields)]

源生成器在编译期扫描 Configure 方法体并生成与 `[MapFrom]` 通道**完全相同**的 `GeneratedMapper` 映射方法（6 正向形态 + 4 反向形态派生、深拷贝、注册），运行时零差异。

## 链式三段式（Member 语法）

字段级声明采用 **Member 三段式**：`.Member(dest)` 锚定目标字段（第一段）→ 头声明来源/忽略（第二段，可选）→ 配置段叠加变换（第三段，可选）。**字段名只写一次**，同字段多终端连写不再重复 dest lambda：

可运行版本（`Order`/`OrderDto` 类型对，`[MapperConfig]` 集中声明，巡览不挂场景菜单）：

[!code-csharp[链式三段式：锚定→显式来源→变换 / 同名省略 From / Mask / Ignore](~/samples/NCMapper.Samples/01-Fluent/FluentBasicsScenarios.cs#FluentBasics_OrderChain_Configure)]

- **第一段（锚定）**：`Member<TProp>(dest)` 锚定目标属性（单段纯属性路径），TProp 由 lambda 推断锚定——后续字段段方法签名里不再出现该类型参数；返回字段"头"配置器 `MemberHead`。
- **第二段（头声明）**：`From` / `FromSource`（速记）/ `Ignore` 三组，**只能在头位置写一次**；`From`/`FromSource` 返回字段"配置"对象 `MemberConfig`，`Ignore` 返回"忽略之后"对象 `AfterIgnore`（终态，见忽略族）。
- **第三段（配置段）**：`Mask` / `Format` / `FromJson` / `MapTree` / `Convert` / `ConvertBack` / `MapWhen` / 字段级 `NoAutoInclude`——`MemberHead` 与 `MemberConfig` 两个位置都可用（头段直跟即"省略来源"形态，来源按目标字段名解析同名源属性）。
- **回到外层**：`MemberConfig` 继承对级配置器——字段段配完后可直接续下一个 `.Member(...)` 或对级终端（`NoAutoMatch` / `Tree` / 钩子 / `FromSource` 分组块等）；`AfterIgnore` 亦继承对级。
- **编译期分层约束**：`MemberHead` 不继承对级——裸 `.Member(d => d.X)` 无任何跟随声明时不可续链（提取期按 `NCMAP_F68` Warn 提醒补来源/忽略/变换）；`MemberConfig` 段内不可再补 `From`/`Ignore` 头声明（结构强制，同字段来源唯一性由头段保证）。

## 终端方法参考

链式入口 `cfg.For<TSource, TTarget>()` 返回对级配置器 `FluentTypeConfig<TSource, TTarget>`；下表按三段式分层列出全部终端（字段段终端配完回到外层可续链；对级终端返回自身可链式）：

| 段位 | 终端签名 | 关键实参/泛型 | 语义 |
| --- | --- | --- | --- |
| 入口 | `For<TSource, TTarget>()` | — | 声明类型对，链式起点（每链必须以 For 起头） |
| 锚定 | `Member<TProp>(dest)` | 目标属性 lambda（单段纯属性路径） | 三段式第一段：锚定目标字段，TProp 由推断锚定、后续签名消失 |
| 头声明 | `From<TSrcProp>(src, overrideIgnore = false)` | 源属性 lambda（支持嵌套路径与 null 抑制 `!`）+ 可选豁免门 | 显式来源（同型/跨型均支持，跨类型如 `DateTime→string` 由生成代码内建转换承接）；取代旧双实参来源声明 |
| 头声明 | `FromSource<TSubSource>(src)` | 子源属性 lambda（object 占位，支持嵌套路径） | 跨源绑定速记（见 [from-source](from-source.md) 专节） |
| 头声明 | `Ignore()` | 无参 | 忽略目标字段（等同 `[MapIgnore]`；独唱终态，见下文） |
| 头声明 | `Ignore(direction)` | `Direction` 参数（ToDto/ToEntity/Both） | 方向性忽略（等同 `[MapIgnore(Direction)]`，见下文专节） |
| 配置段 | `Mask<TMask>(overrideIgnore = false)` | `where TMask : IMask` + 可选豁免门 | 脱敏（下游与 `[Mask]` 共享 MaskProvider；目标须为 string；可与 From 显式来源叠用） |
| 配置段 | `Format(format, overrideIgnore = false)` | 编译期常量格式串 + 可选豁免门 | 属性级格式化（源 `IFormattable` → 目标 string，语义镜像 `[MapFormat]`，见 [format](format.md) 专节） |
| 配置段 | `FromJson()` | 无参 | JSON 字符串展开（源侧 string 经 JsonConverter 反序列化，等同 `[MapProperty(IsFromJson = true)]`） |
| 配置段 | `MapTree()` | 无参 | 树形映射（该字段递归走相同 (S,T) 配置，源侧省略 From 时按同名属性解析；与对级 `Tree()` 辨析见下文三语义） |
| 配置段 | `Convert(converter, overrideIgnore = false)` 等 4 重载 | T0 `Func<TProp>` / T1 `Convert<TSrcProp>(Func<TSrcProp, TProp>)` / T2 `Func<TSource, TProp>` / T3 `Convert<TSrcProp>(Func<TSource, TSrcProp, TProp>)` | 正向自定义转换（Entity→DTO，语义镜像 `[Convert]`，四形态见 [convert](convert.md) 专节） |
| 配置段 | `ConvertBack<TEntityProp>(converter, overrideIgnore = false)` 等 4 重载 | T0 `Func<TEntityProp>` / T1 `Func<TProp, TEntityProp>` / T2 `Func<TTarget, TEntityProp>` / T3 `Func<TTarget, TProp, TEntityProp>` | 反向自定义转换（DTO→Entity，语义镜像 `[ConvertBack]`，见 [convert](convert.md) 专节）。写回目标解析顺序：同名 → 自动扁平化叶 → lambda 体；两侧属性名不同（前两步落空）时用 lambda 形态自行指明——T2 整源形态 `(dto) => dto.XxxYyy`（形参上的直接成员访问即写回目标）、T3 组合形态 `(dto, 字段值) => M(字段值)`（写回目标取正向来源字段）；解析规则刻意收窄，复合形态不猜测（仍报 `NCMAP_F18`） |
| 配置段 | `MapWhen(predicate)`（T1 字段值） | `Func<TProp, bool>`：命名静态方法组或无捕获内联 lambda | 条件守卫（真才映射；T1 非泛型——TProp 已锚定，内联 lambda 直接可用；见 [terminals](terminals.md) 专节） |
| 配置段 | `MapWhen<TInput>(condition)`（T2 整源） | 显式类型参 `TInput` ∈ {TProp, TSource}；整源形态写 `<TSource>` | 条件守卫（多字段判定；须显式类型参——内联 lambda 无推断位；见 [terminals](terminals.md) 专节） |
| 配置段 | `NoAutoInclude`（Member 段内，无参） | 无参 | **字段级**禁用自动包含（该字段跳过 ORM 降级路径导航收集，等同 `[MapNoAutoInclude]`；与对级 NoAutoMatch 辨析见下文双模式节） |
| 忽略终态 | `AfterIgnore` 上唯一例外：`NoAutoInclude`（无参） | 无参 | `Ignore()` 之后唯一可跟的操作（"忽略 + 查询不加载"组合）；写完即回对级，不可再写第二个 |
| 对级 | `NoAutoMatch()` | 无参 | **对级**白名单开关：该 (S,T) 对禁用自动匹配/自动包含（见下文双模式节；旧对级名已改此名） |
| 对级 | `Tree()` / `Tree(id, parent, children)` | 无参，或 id/parent/children 属性名三元组 | 对级构建配置终端（非字段映射终端）：`[MapTree]` 链式等价，注册表承载（opt-in），未调用时树挂载位判定恒 false（见下文三语义辨析） |
| 对级 | `FromSource<TSubSource>(configure)` | 分组块 `Action<FluentTypeConfig<TSubSource, TTarget>>` | 同一子源多字段分组声明（块内 = For 同型链，见 [from-source](from-source.md) 专节） |
| 对级 | `BeforeMapping(handler)` / `AfterMapping(handler)` | `Action<TSource, TTarget>` 静态方法组 | 生命周期前置/后置钩子（见 [hooks](hooks.md) 专节） |
| 对级 | `MapCtorParam<TSrcProp>(paramName, sourceProp)` / `MapCtorParam(paramName, sourcePropertyName)` | 形参名 + 单段源 lambda / 源属性名字符串 | 显式构造器形参绑定（Fluent 独有，见 [terminals](terminals.md) 专节） |
| 对级 | `MapSubclass<TSrcDerived, TTargetDerived>()` | 双泛型实参（子类源/子类目标） | 多态子类型对登记（对级，见 [terminals](terminals.md) 专节） |

> `From`/`Mask`/`Format`/`Convert`/`ConvertBack` 五终端的豁免门为**单签名内置可选参数** `bool overrideIgnore = false`（见下文专节），不再是独立重载。

## OverrideIgnore：全局忽略豁免门

豁免能力落在五个来源/变换终端（`From`/`Mask`/`Format`/`Convert`/`ConvertBack`）的**单签名可选参数** `bool overrideIgnore = false` 上，语义镜像特性侧 `OverrideIgnore` 命名参数：**全局忽略清单**（MapOptions 全局配置）命中该字段时豁免，映射/脱敏/转换/格式化照常生成不被抑制，正反向双链生效。

可运行示例（`dotnet run --scenario 102` 实跑验证；豁免门可选参形态）：

[!code-csharp[OverrideIgnore 豁免门链式声明](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_Ignore)]

与同字段段 `Ignore()` 同标仍构成矛盾（声明面内无歧义，fail-fast 报 `NCMAP_F23`/`NCMAP_F17`）。

## Ignore 方向重载：方向性忽略

`Ignore(direction)` 语义等同 `[MapIgnore(Direction)]`：仅在指定方向忽略（另一方向照常映射）。

可运行示例（`dotnet run --scenario 102` 实跑验证）：

[!code-csharp[Ignore 方向重载：ToDto/ToEntity 方向性忽略](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_Ignore)]

省略方向（裸 `Ignore()`）等同双向忽略（`Both` 语义）。

**忽略独唱终态**：`Ignore()` / `Ignore(direction)` 是该字段段的结束声明——其后不得跟任何映射/变换操作（编译期强制），唯一例外是"忽略 + 查询不加载"组合的字段级 `NoAutoInclude`（写完即回对级、不可再写第二个）。

## 自动匹配默认行为

与 `[MapFrom]` 通道一致：**未显式声明的同名属性仍会被自动匹配 + 自动扁平化**。只写点名过的字段段声明时，其余同名属性照常映射；需要"仅映射显式声明"的白名单模式时使用对级 `NoAutoMatch()`（见下文双模式节）。

## 自动匹配与白名单双模式（NoAutoMatch 与字段级 NoAutoInclude 辨析）

> 术语注解：「**对**」= 一对类型（把源类型 S 映射到目标类型 T 构成一个映射对 `(S, T)`，行业惯用语非自造）。「**对级**」= 配置作用范围是整对之间的映射关系（挂在 `For<S, T>()` 返回的对级配置器上）；「**字段级**」= 只作用于单个属性（`.Member(d => d.X)` 段内）。

**口诀：配不配看 Match（对级），带不带看 Include（字段级）。**

两个名字同族的开关**身份不同**：

| | `NoAutoMatch()`（对级） | `NoAutoInclude`（字段级，Member 段内） |
| --- | --- | --- |
| 管什么 | **映射不自动配对**——该 (S,T) 对禁用自动匹配范围，仅映射显式声明 | **查询不加载**——该字段不参与导航自动收集（ORM 降级路径的导航 Include 跳过，`NavigationTreeCollector` 判定面） |
| 粒度 | 对级（全对**所有字段**的匹配策略） | 字段级（只有那**一个字段**的行为） |
| 进入方式 | `For<S,T>()` 后直写；`From(...)` 之后经继承面任意位置可达 | `.Member(d => d.X)` 段内直跟（或 From 声明后叠加） |
| 特性双通道 | **无**（Fluent 独有）；类级特性等价物 `[MapNoAutoMatch]` 见 [dual-channel](../../explanation/dual-channel.md) 与诊断 `NCMAP047` | **有**：`[MapNoAutoInclude]`（属性级，双通道等价） |

两者是**双承载并行**关系（互不迁移、可各自使用）；两开关叠加时物化取并集。

### NoAutoMatch 关闭了什么（三被关自动清单）

默认自动匹配（未声明也自动做）的完整清单是**三样**，`NoAutoMatch()` 全部关闭：

| # | 被关的「自动」 | 说明 |
|---|---|---|
| ① | 未声明的**同名自动配对**（忽略大小写） | 没点名的属性，同名也不接 |
| ② | **配置感知补匹配** | 声明部分字段后，引擎默认把剩下的字段自动补上；开启后不再补 |
| ③ | **自动扁平化** | 嵌套路径自动拼接（如 `Address.City` → `AddressCity`）不再做 |

### 白名单模式不禁止映射本身

`NoAutoMatch` 禁的是「**自动**」两个字，不是「映射」：映射方法照常生成（6 正向 + 4 反向）、注册照常、`Map(...)` 调用照常成功。行为对照（运行时实测）：

| 状态 | 映射执行 | 显式声明的属性 | 未声明但同名的属性 |
|---|---|---|---|
| **默认态** | ✅ 正常 | ✅ 映射 | ✅ **自动接上** |
| **NoAutoMatch 态** | ✅ **照样正常** | ✅ 映射 | ❌ **不接**（同名也不接） |
| （误解）"禁止映射" | ❌ 报错/不可用 | —— | —— |

**一句话（修正版）**：

> **可以映射，但切到白名单模式——只有 Fluent 里点名过（`.Member` 声明过）的属性才映射；没点名的，哪怕同名、哪怕能自动拼路径，一律不接。**

两个注脚：

- **「点名」有两种写法都算**：`.Member(d => d.X).From(s => s.Y)`（显式写明来源）与 `.Member(d => d.X)`（省略 From → 默认从源同名属性取）。关键是**写了 `.Member(...)` 这个动作本身**——`NoAutoMatch` 注释里「仅映射显式声明/同名默认的属性」中的「同名默认」**不是**"同名属性默认会映射"，而是字段链声明内的语法糖：先声明，省略 From 时按同名取；没声明连门都没有。
- **反向同规则**：反向映射逐条翻转正向声明——白名单模式下反向也只覆盖点名过的属性。

**用途定位（防意外开关）**：默认自动挡更省事；只有对安全敏感的对才切手动挡——典型场景是源类型将来新增属性（如 `Password`），默认态下下次编译就静默流进 DTO；`NoAutoMatch()` 一开，新人进门先报名字才放行。

**两条易混淆源**：

1. **名字同族**：都叫 NoAuto*——"Match = 配不配（映射配对）/ Include = 带不带（查询加载）"是两个领域概念；
2. **静默跨作用域**：`.Member(...)` 之后经继承面调 `NoAutoMatch()` 合法但作用的是**整对**——调用点看着像字段操作、实际动的是对级开关。误写路径区分：裸对级位写 `NoAutoMatch` 之外的错误对级名编译期全拦（CS1061）；**互换类（两开关互相写错）→ 静默换语义、零诊断**，靠本节口诀自查。

两开关的可运行演示均在场景 101（`dotnet run --scenario 101`，实码见 `samples/NCMapper.Samples/01-Fluent/FluentModels.cs`：对级白名单对与字段级导航不收集对各一）。

## \[MapTree] / MapTree（Member 段内）/ Tree() 三语义辨析

三个名字相近的树相关声明**语义不同**（名近义远，互补使用，递归映射属性通常即 Children 挂载位）：

可运行示例（`dotnet run --scenario 101` 实跑验证；Tree 自定义三元组对级构建配置）：

[!code-csharp[Tree 终端：自定义三元组（Key/ParentKey/Nodes）对级构建配置](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_TreeNodeMapper)]

[!code-csharp[Tree 终端：自定义三元组（Key/ParentKey/Nodes）对级构建配置 · Configure](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_TreeNodeMapper_Configure)]

| 声明 | 粒度 | 语义 |
|------|------|------|
| `[MapTree]`（特性，类级标注） | 对级构建配置 | 声明树挂载位三元组（默认 `Id`/`ParentId`/`Children`），供 `ToTreeList` 树构建与 ORM 投影树挂载位判定（`IsTreeChildrenSlot`）消费，**运行时 TreeBuilder 反射消费**，Fluent DTO 同样可受益于其回退判定 |
| `MapTree()`（Fluent 配置段终端） | 字段级映射声明 | 声明"该字段**递归映射**"（源侧省略 From 时按同名属性解析，递归走相同配置），物化层映射声明 |
| `Tree()` / `Tree(id, parent, children)` | 对级构建配置 | `[MapTree]` 的**链式等价**：注册表承载（`GetTreeConfig` 优先，反射回退），opt-in，未调用时注册表 `TreeConfig=null`，树挂载位判定恒 false（防"恰有同名 Children 自引用集合属性"的双挂载缺口） |

同理，属性级 `[MapNoAutoInclude]`（该属性跳过 ORM 降级路径导航收集）的链式等价为 Member 段内直跟无参 `NoAutoInclude`（如锚定 `.Member(d => d.AuditLogs)` 后段内续写——字段级唯一写法；对级开关是 `NoAutoMatch()`，勿混），粒度与对级白名单开关不同，见上节双模式辨析。

## 限制与边界（当前版本）

- Configure 方法体仅支持**纯链式静态调用**（动态构造报 `NCMAP_F08`）

- 属性级特性（`[MapProperty]`/`[MapIgnore]`/`[Mask]`/`[Convert]`）在 Fluent 通道**不生效**，用链式方法表达等价语义；树配置与字段级禁用自动包含亦有链式等价（`Tree()` ↔ `[MapTree]`、Member 段内 `NoAutoInclude` ↔ `[MapNoAutoInclude]`，见上方三语义辨析与双模式节）。Fluent 侧天然满足"属性级配置跟随类锚"约束（映射配置跟随类锚）：终端声明全部写在 `For<S, T>()` 链上，不存在游离于映射声明之外的配置，无需额外判定。注意区分生效面：不生效的是**属性级特性标注**；经链式声明（如 `.Member(d => d.X).Mask<TMask>()`）写入注册表明细的语义在运行时内存映射**照常生效**——注册表明细直接消费声明本身，不受特性锚门约束

- `Member` 的目标属性路径须为**单段**（`From` 源侧支持嵌套多段）

- lambda 仅支持**属性**路径（字段引用的声明被丢弃）

- 结构性错误（链未以 `For` 起头/泛型参数错误/未知方法名等）由 C# 编译器报告，源生成器不重复报

- 同一 (S,T) 对在 Fluent 内重复声明将触发 `NCMAP_F16`（Error）：每处声明位置各报一条（消息列出全部冲突方 Mapper 类名），全部冲突声明均不生成映射方法（消费端 CS0111 随之消失）；请合并为单一声明或删除重复项

- 同一锚点属性的真冲突（同槽位重复声明，或声明与 `Ignore` 并存）报 `NCMAP_F59`（Error）；来源槽/变换槽各只允许一次的字段段内重复报 `NCMAP_F70`（Error）——合法组合清单（如 `From` + `Mask` 叠用）见上文三段式与双模式节

- Fluent 类型对 **享受 ORM 投影加速**：`AsDto` 经运行时注册表双读做**逐声明翻译判定**，纯可翻译声明（来源声明/Ignore 等价）走 SQL 侧投影；`Mask`/`Convert`/`FromJson`/`AfterMapping`/`BeforeMapping`/`MapWhen`/`FromSource`/`MapSubclass` 一律强制降级（内存映射全语义；MapSubclass 为 isinst 分派链，分类 SubclassDispatch）；`MapCtorParam` 不触发降级（不进入不可翻译分类集）。详见 [ORM 降级机制 §2.3](../../explanation/orm-degradation.md) 与各 ORM how-to 的「Fluent DTO 支持现状」节

## 编译期诊断（NCMAP\_F 系列）全族索引

| 错误码         | 场景                                                                                                                    |
| ----------- | --------------------------------------------------------------------------------------------------------------------- |
| `NCMAP_F02` | lambda 引用的属性在类型上不存在                                                                                                   |
| `NCMAP_F04` | lambda 根不是参数（局部变量/静态类引用）                                                                                              |
| `NCMAP_F05` | lambda 含 null 条件访问 `?.`                                                                                               |
| `NCMAP_F06` | lambda 含索引器访问 `[]`                                                                                                    |
| `NCMAP_F07` | lambda 含方法调用 `()`                                                                                                     |
| `NCMAP_F08` | Configure 方法体含动态配置语句（if/for/foreach/while/switch/try/三元/赋值/局部变量，仅允许纯链式静态调用）                                          |
| `NCMAP_F11` | 同一 (S,T) 对被 `[MapFrom]` 与 Fluent 双声明（Error，双位置互指，两侧均不生成）                                                              |
| `NCMAP_F12` | AfterMapping 目标方法为 private（Error）                                                                                     |
| `NCMAP_F14` | AfterMapping 目标不是可静态解析的方法组（Error）                                                                                     |
| `NCMAP_F15` | `[MapperConfig]` 类内没有匹配签名的方法（提示入口约定）                                                                                  |
| `NCMAP_F16` | 同一 (S,T) 对在 Fluent 通道内重复声明（Error，每处声明各报一条且消息列出全部冲突方 Mapper 类名；全部冲突声明均不生成映射方法）                                         |
| `NCMAP_F17` | 同字段段 `Ignore` + `ConvertBack` 矛盾（Error，Ignore 使属性不入候选，反向写永远不会执行）                                                      |
| `NCMAP_F18` | `ConvertBack` 实体侧反向写回点不存在（同名与扁平化解析均未命中，Error）                                                                         |
| `NCMAP_F19` | `ConvertBack` 转换器为 private/protected（Error，生成代码同程序集不可见）                                                              |
| `NCMAP_F20` | `ConvertBack` 转换器为闭包（捕获外层状态）/局部函数/实例绑定（Error；无捕获内联 lambda 已放行，转录进生成代码）                                                                 |
| `NCMAP_F21` | `ConvertBack` 转换器签名不匹配四形态模板（T0/T1/T2/T3；泛型方法/可选参/params/ref 排除，Error）                                                 |
| `NCMAP_F22` | `ConvertBack` 锚点属性无 public setter 或 init 访问器（Warning，声明将被物化阶段忽略；改 public/init setter 或用特性通道实体侧 `[ConvertBack]`） |
| `NCMAP_F23` | 同字段段 `Ignore` + `Convert` 矛盾（Error，Ignore 使属性不入候选，正向写永远不会执行）                                                      |
| `NCMAP_F24` | `Convert` 源侧不可达（同名/扁平化/`[MapProperty]` 反向查找均未命中，Error，fail-fast；特性侧无源走 default 调用为通道边界差异）            |
| `NCMAP_F25` | `Convert` 转换器为 private/protected（Error，生成代码同程序集不可见）                                                              |
| `NCMAP_F26` | `Convert` 转换器为闭包（捕获外层状态）/局部函数/实例绑定（Error；无捕获内联 lambda 已放行，转录进生成代码）                                                                 |
| `NCMAP_F27` | `Convert` 转换器签名不匹配四形态模板（T0/T1/T2/T3；泛型方法/可选参/params/ref 排除，Error）                                                 |
| `NCMAP_F28` | `Convert` 锚点属性无 public setter 或 init 访问器（Warning，声明将被物化阶段忽略）                                                    |
| `NCMAP_F29` | 主链声明×FromSource 绑定同 dest 冲突 / 同子源多绑定冲突（Error，一个属性只能由一个源供给；同子源组内仅路径承载型来源声明（`From`/`FromSource` 速记）与 `MapTree`、`Convert` 计数，`Mask`/`FromJson` 修饰型附着不入计数）                                  |
| `NCMAP_F30` | FromSource 块内禁止 `ConvertBack`（Error，FromSource 绑定仅正向映射，无反向写回落点）                                                          |
| `NCMAP_F31` | FromSource 块内禁止对级禁用自动匹配开关（Error，子源字段全显式声明，块上下文无自动包含可禁）                                                        |
| `NCMAP_F32` | FromSource 块内禁止 `AfterMapping`（Error，生成的 Compose 管线无钩子注入口，声明永不执行）                                                      |
| `NCMAP_F35` | `BeforeMapping` 目标方法为 `private`（Error，跨类不可访问）                                                                                   |
| `NCMAP_F36` | `BeforeMapping` 目标不是可静态解析的方法组引用（lambda/闭包/捕获变量，Error）                                                                         |
| `NCMAP_F37` | 目标类型经带参/init-only 构造器构造（构造与赋值不可分，BeforeMapping 无插入点，Error；双侧同报）                                                               |
| `NCMAP_F38` | FromSource 块内禁止 `BeforeMapping`（Error，Compose 管线无钩子注入口，声明永不执行）                                                                       |
| `NCMAP_F39` | 同一映射对双通道声明同类生命周期钩子（Before×Before / After×After，Error；交叉组合不冲突）                                                                   |
| `NCMAP_F40` | `MapWhen` 形态 A 谓词方法组契约失败（不可解析/非静态/可见性不足/签名不符/缺隐式转换，Error）                                                                     |
| `NCMAP_F41` | `MapWhen` 形态 B 内联 lambda 捕获变量或访问私有成员（转录禁令，Error）                                                                                  |
| `NCMAP_F42` | `MapWhen` 形态 B 内联 lambda 含语句体或白名单外节点（转录禁令，Error）                                                                                   |
| `NCMAP_F43` | `MapWhen` 锚点字段无可守卫的映射（无同名源且无显式 `From`，或被静态 `Ignore` 排除，Error，守卫永不生效）                                                                 |
| `NCMAP_F44` | 同一属性重复声明 `MapWhen`（单条件，Error；负向过滤用谓词取反）                                                                                             |
| `NCMAP_F45` | 主链 `MapWhen` 锚点属性仅由 FromSource 跨源覆盖（Compose 无发射落点，Error；块内声明合法）                                                                       |
| `NCMAP_F51` | `MapCtorParam` 绑定源属性不存在或无公共可读 getter（Error，fail-fast）                                                                                 |
| `NCMAP_F52` | `MapCtorParam` 目标公共构造器无该形参（消息列出可用参数名，Error；ByRef/指针不可绑定）                                                                         |
| `NCMAP_F53` | `MapCtorParam` 重复绑定或与显式 `From` 条目同形参矛盾（Error，fail-fast）                                                                                   |
| `NCMAP_F54` | 多等权构造器并列（Info 提示：选器静默按参数数量裁决，声明绑定可显式消解）                                                                                     |
| `NCMAP_F59` | 同一锚点属性的真冲突（Error，2026-10-09 语义改写：原"后写覆盖先写（警告）"收窄为"仅真冲突报错"——同槽位重复声明或映射声明与 Ignore 并存直接拒绝；保留其一或按上文合法组合清单叠用） |
| `NCMAP_F60` | Fluent 链上出现未识别的终端方法名（可编译但非终端，如三段式消费适配落地前的未知调用，Warning，该节点声明被跳过；主链场景整条链丢弃） |
| `NCMAP_F64` | `Format` 目标属性非 `string`（Error，格式化输出须为文本属性）                                                                                          |
| `NCMAP_F65` | `Format` 源属性不可达/非 `IFormattable`（Error，支持 DateTime/DateTimeOffset/TimeSpan/Guid/数值/枚举含 Nullable 包装）                                       |
| `NCMAP_F66` | `Format` 与 `Convert` 同锚点（Error，二选一）                                                                                                         |
| `NCMAP_F67` | `Format` 反向派生命中且未标 `ConvertBack`（Warn——反向跳过该属性不赋值；往返需补 `ConvertBack`，详见 [format](format.md)）                                        |
| `NCMAP_F68` | Member 字段段无任何跟随声明（裸 `.Member(...)`，Warning——补来源（`From`/`FromSource`）、忽略（`Ignore`）或变换（`Mask`/`Format` 等）） |
| `NCMAP_F69` | 字段段省略来源且源类型（或 FromSource 子源）无同名成员（Error——旧字段级写法该声明静默消失，Member 三段式显式报错；补 `From` 指定来源） |
| `NCMAP_F70` | 同一属性重复声明（同槽位重复——来源槽/变换槽各只允许一次，Error，报告首个与重复的声明种类；保留其一） |
| `NCMAP_F71` | `MapWhen` 整源形态类型参数既不匹配目标属性类型也不匹配源类型（Error——整源形态写 `MapWhen<源类型>(...)`，字段值形态省略类型参数） |

> 注 1：`NCMAP019`（Dto 命名启发式提示"未标注 \[MapFrom]"）对 **Fluent DTO 自动豁免**：Fluent 类型对被源生成器登记后不再触发该提示（双通道混合工程同样免报，未声明的普通 DTO 负向不放宽）；删除对应 Fluent 声明即恢复提示（增减可增量验证）。
> 注 2：`NCMAP_F46`–`NCMAP_F50`（MapSubclass 族）逐码见 [terminals](terminals.md) 的 MapSubclass 专节诊断表；`NCMAP_F55`–`NCMAP_F58`/`NCMAP_F62`/`NCMAP_F63`（lambda 形态与链解析治理族）逐码见 [诊断规则](../../diagnostics/rules.md) 主表。
> 注 3：特性侧双通道解析族 `NCMAP038/039/040`（`[BeforeMapping]`/`[AfterMapping]` 钩子方法不可解析/签名不符/重载歧义，镜像 NCMAP035/036/037 的 Convert 族）与 `NCMAP041`（`[MapWhen]` 谓词解析失败）触发条件见 [terminals](terminals.md)/[hooks](hooks.md) 各专节诊断表；完整条目见 [诊断规则](../../diagnostics/rules.md)。

## 配套示例

可运行演示位于 `samples/NCMapper.Samples/`（场景 101-107、109，`dotnet run --scenario N`）：

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 101   # Fluent 基础链：Member.From/Ignore/Mask/AfterMapping、MapTree、对级 NoAutoMatch、字段级 NoAutoInclude、Tree、索引段
dotnet run --scenario 102   # Fluent 转换：Convert/ConvertBack 四形态、配对、豁免门可选参、FromJson、实体侧锚点族
dotnet run --scenario 103   # Fluent FromSource：速记/分组块、块内 Mask/FromJson、顺序覆盖、双面向
dotnet run --scenario 104   # Fluent 生命周期钩子：BeforeMapping/AfterMapping（Fluent + 类级特性双侧）、相位序、跨对签名过滤
dotnet run --scenario 105   # Fluent 条件守卫：MapWhen 四形态同链、[MapWhen] 特性侧
dotnet run --scenario 106   # Fluent 构造器形参绑定：MapCtorParam 双形态、record 边界澄清、MapList 元素级
dotnet run --scenario 107   # Fluent 多态子类型映射：MapSubclass 四族分派、基类兜底、反向子类对、[MapSubclass] 特性侧
dotnet run --scenario 109   # Compose 运行时元数据（RuntimeComposeMapping）+ 新语法叠加：跨型 From、From+Format+Mask（FormatThenMask）、From+Mask+MapWhen
```

本文示例代码与 samples 单点同源（`[!code-csharp]` 引用），改动以 samples 为准。

## 下一步

- 基础特性映射：[declarative/basics](../declarative/basics.md)
- 点路径与嵌套：[mapping/dot-path](../mapping/dot-path.md)
- 脱敏：[infrastructure/masking](../infrastructure/masking.md)
- 诊断规则：[diagnostics/rules](../../diagnostics/rules.md)（数字族全表 + F 族分页转指）
- 跨面对照：[api-comparison](../../reference/api-comparison.md)
