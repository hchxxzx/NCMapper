---
wiki-id: declarative/advanced
涉及诊断码: NCMAP020-022, NCMAP041, NCMAP_F11, NCMAP_F40-F45, NCMAP_F46-F50, NCMAP_F51-F54
库版本: Unreleased（随下个版本发布）
更新时间: 2026-09-30
---

# Attribute 高级形态

> 特性通道（第二体系）高级主题的**自足篇**：条件守卫、多态子类型、全局默认配置的完整语义在本页内可读完，不必跳转他页取语义。基础用法见 [basics](basics.md)；两通道选型对照见 [api-comparison](../../reference/api-comparison.md)；特性全表见 [attributes-index](../../reference/attributes-index.md)。Fluent 侧同名终端见 [fluent/terminals](../fluent/terminals.md)（对照与延伸用）。

## 1. \[MapWhen]：属性级条件守卫

`[MapWhen(nameof(谓词))]` 标注在目标属性上，谓词求值返回 `true` 才执行该属性的映射赋值（真才映射，极性入名）。声明可锚定两类映射载体：自动同名匹配的属性，以及显式 `[MapProperty]` 标注的属性。

> **类级锚前提**：本篇各节的属性级标注（`[MapWhen]`/`[MapSubclass]`/`[MapProperty]` 等）生效前提是**所在类有映射声明锚**——`[MapFrom]` 标注，或 Fluent 配置类里对本类参与类型对的 `For<S, T>()` 声明。无锚时属性级标注一律不认（编译期报 `NCMAP019`），见 [basics §2 类级锚前提](basics.md)。

### 谓词解析规则

- 解析位置：谓词方法在**被标注属性的声明类型**上按名解析（单类型自足），范围为 `DeclaredOnly`（含 partial 声明的全部部分）；`Inherited = true` 让声明随属性被子类 DTO 继承；
- 可见性门：方法须为 `public` 或 `internal static`（同程序集可见）；`private`/`protected` 跨类不可访问即解析失败；
- 签名门：单参 + 返回 `bool` + 无 `ref`/`out`/`in`；
- 入参形态判定（提取期自动判定，无需指定）：单参类型等于该 DTO 的 `[MapFrom]` 源实体类型时按 T2 整源（多字段判定），否则按 T1 字段值（参数类型须与源字段类型隐式兼容）。判定规则与 `[Convert]` 族一致。

### 跳过值规则

条件为假时跳过该属性的赋值，跳过后的取值按映射路径区分：

| 映射路径 | 条件假时的结果 |
| -------- | -------------- |
| 新建（`Mapper.Map<TSource, TTarget>(src)`） | 保持目标属性初始化器值；无初始化器则为 `default` |
| 更新既有实例（`Mapper.Map(src, dst)`） | 保留 dst 既有值 |
| 构造器映射目标 | 显式落 `default` |

### 组合与约束

- 链路次序为条件 → 转换 → 赋值：守卫看到的是**转换前**的源字段值；同锚点 `[MapProperty]` + `[MapWhen]` 组合合法；
- 单条件：`AllowMultiple = false` 与 `Inherited = true` 并存，语言层面同一属性只能标注一次，多判据请在单一谓词内组合；
- 负向过滤用谓词取反表达，无 `IgnoreWhen` 类入口；
- 反向映射不消费守卫声明（仅正向 Entity→DTO 生效）。

### 诊断

| 码 | 触发条件 | 处置 |
| --- | -------- | ---- |
| `NCMAP041` | 谓词解析失败：方法不存在、多重载歧义、非静态（含 lambda 与局部函数）、可见性不足、签名不符、入参类型既非源字段类型亦非源实体类型 | 将谓词声明在被标注属性所在类型，签名收敛到 T1/T2 二选一 |

### 投影行为

`[MapWhen]` 实现 `IProjectionPolicy` 且 `RequiresDowngrade = true`：守卫谓词为 CLR 委托路径，SQL 不可翻译，ORM 投影恒降级为内存映射（分类 `MapWhen`，与 `[Mask]`/`[Convert]` 同口径）。详见 [ORM 降级机制](../../explanation/orm-degradation.md)。

### 示例

谓词声明在 DTO 自身，`public static bool HasContent(string)` 命中 T1 字段值形态（`dotnet run --scenario 105`）：

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto)]

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫 · Id](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Id)]

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫 · Name](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Name)]

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫 · Score](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Score)]

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫 · HasContent](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_HasContent)]

## 2. \[MapSubclass]：多态子类型登记

`[MapSubclass(typeof(派生源), typeof(派生目标))]` 标注在承载 `[MapFrom]` 的基类 DTO 上，为基类映射对登记一对子类型映射。基类映射运行时按源实例的实际类型路由到子类专属映射体（编译期 isinst 分派链）。适用于基类实体到基类 DTO、派生实体到派生 DTO（含派生专有字段）的多态列表与多态字段场景。

### 声明形态

- 类级标注，`Inherited = false`（派生 DTO 不继承父类的子类登记）、`AllowMultiple = true`（每个实例登记一对，可多实例）；
- 标注类须同时标注 `[MapFrom(typeof(基类源))]` 锚定基类映射对；缺锚定即孤儿登记，报 `NCMAP_F49`。

### 分派语义

覆盖 `Map` / `_Update` / `MapList` / `MapListInto` 四族，规则一致：

- 可赋值匹配：`src is 派生源` 命中即路由子类体；未登记的更深派生类型命中最接近的已登记祖先层；
- 最派生优先：多对登记同时命中时，按（源侧继承距离，目标侧继承距离）字典序仲裁；部分重叠登记（同源异层、同源异目标）合法，不报错；
- 基类兜底：未命中任何登记子类（含未登记的第三方派生）走基类体，子类专有字段保持默认；
- 双实例匹配：`_Update` / `MapListInto` 要求 src 与 dst 同时命中才路由子类体；dst 运行时类型非登记子类目标时回落基类体，子类专有字段不写入；
- 反向映射：子类对经反向注册臂自动产出反向映射与反向直调重载（登记非单向）；反向基类对不消费分派（反向目标是调用方已存在的实体实例）。该不对称由 `NCMAP_F50`（Info）提供可见性。

### 继承约束与边界

- 两通道约束口径：Fluent 侧泛型约束（`where TSrcDerived : TSource`）在调用点由编译器拦截（CS0311/CS0314）；特性侧 `typeof` 实参无编译期约束，继承关系不满足在提取期报 `NCMAP_F46`；
- `TTargetDerived == 基类目标`（源侧多态、目标收敛）为合法用例；
- `TSrcDerived == 基类源`（登记基类自身，分派自引用死循环）与完全同对重复登记报 `NCMAP_F47`；
- 子类目标无公共可映射面或不满足构造约束报 `NCMAP_F48`。

### 诊断

| 码 | 通道 | 触发条件 | 处置 |
| --- | ---- | -------- | ---- |
| `NCMAP_F46` | 特性侧专码 | `typeof` 实参继承关系不满足（派生源须派生自基类对源，派生目标须派生自基类对目标） | 修正 `typeof` 实参；Fluent 侧同类错误由 CS0311/CS0314 在调用点拦截，无此码 |
| `NCMAP_F47` | 通用 | 完全相同子类型对重复登记（首条保留），或登记基类自身 | 移除重复登记或基类自身登记 |
| `NCMAP_F48` | 通用 | 子类目标无公共可映射面或不满足构造约束 | 为子类目标提供公共可映射属性与可构造形态 |
| `NCMAP_F49` | 特性侧专码 | 标注类无 `[MapFrom]` 锚定，基类映射对不存在（孤儿登记） | 补 `[MapFrom(typeof(基类源))]`，或改用 Fluent 通道 |
| `NCMAP_F50` | 通用（Info） | 登记存在静默面：反向不对称（反向基类对已生成但永不分派）、双实例匹配回落基类体 | 既定语义提示，无需处置；需子类字段写入时检查 dst 运行时类型 |

### 投影行为

`[MapSubclass]` 实现 `IProjectionPolicy` 且 `RequiresDowngrade = true`：isinst 分派链 SQL 不可翻译，ORM 投影恒降级（分类 `SubclassDispatch`，与 `[Mask]`/`[Convert]`/`[MapWhen]` 同口径）。

### 双侧性口径

两个口径分别表述，勿混：

- 功能双侧对称：`[MapSubclass]` 与 Fluent `.MapSubclass<TSrcDerived, TTargetDerived>()` 为等价对，分派语义同源；
- 诊断码通道不对称：`NCMAP_F46` 与 `NCMAP_F49` 为特性侧专码；Fluent 侧同类错误由泛型约束 CS0311/CS0314 在调用点被编译器拦截，生成器不可达，用户不应在 Fluent 侧寻找这两码。

### 示例

特性侧基类 DTO 多实例登记（`dotnet run --scenario 107`）：

[!code-csharp[Fluent 对级登记与特性侧多实例登记](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · ScAttrAnimalDto_Name](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Name)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · ScAttrAnimalDto_Weight](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Weight)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · ScAttrDogDto](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrDogDto)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · ScAttrDogDto_Breed](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrDogDto_Breed)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · ScAttrCatDto](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrCatDto)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · ScAttrCatDto_Indoor](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrCatDto_Indoor)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · SubclassFluentMapper](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_SubclassFluentMapper)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · SubclassFluentMapper_Configure](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_SubclassFluentMapper_Configure)]

## 3. \[assembly: MapDefaults]：全局默认配置

程序集级 `[assembly: MapDefaults(...)]` 一次声明，对该程序集内**所有**映射生效：统一命名约定、批量忽略审计与版本字段、规范化源属性前后缀，替代逐属性 `[MapProperty]` / `[MapIgnore]` 的重复标注。

### 声明形态

示例逐字同步自 samples（`NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs`，场景 606）：

```csharp
[assembly: MapDefaults(
    NamingStrategy = NamingStrategy.SnakeCase,
    IgnoreNames = new[] { "RowVersion" })]
```

适用位置为程序集，`AllowMultiple = false`（同一程序集只能声明一次）。

### 参数面（全 11 项）

| 参数 | 类型 | 作用域 | 语义 |
| ---- | ---- | ------ | ---- |
| `NamingStrategy` | `NamingStrategy` 枚举 | 全局 | 命名策略，默认 `Default` |
| `Prefixes` | `string[]` | 仅源侧 | 源属性名前缀剥离后继续匹配 |
| `Postfixes` | `string[]` | 仅源侧 | 源属性名后缀剥离后继续匹配 |
| `IgnoreNames` | `string[]` | 双向 | 属性名精确命中即忽略 |
| `IgnoreTypes` | `Type[]` | 双向 | 属性类型 `Type.FullName` 精确命中即忽略 |
| `IgnoreNamePrefixes` | `string[]` | 双向 | 属性名前缀命中即忽略 |
| `IgnoreNameSuffixes` | `string[]` | 双向 | 属性名后缀命中即忽略 |
| `IgnoreNamesToEntity` | `string[]` | 仅 ToEntity | 反向额外按属性名忽略 |
| `IgnoreTypesToEntity` | `Type[]` | 仅 ToEntity | 反向额外按属性类型忽略 |
| `IgnoreNamePrefixesToEntity` | `string[]` | 仅 ToEntity | 反向额外按属性名前缀忽略 |
| `IgnoreNameSuffixesToEntity` | `string[]` | 仅 ToEntity | 反向额外按属性名后缀忽略 |

清单上限 8 项，超限的清单降级为"未设置"并报 `NCMAP020`。各参数逐个定义与代码示例见 [global-config §参数详解](../infrastructure/global-config.md)（深挖页；本页已给出完整参数面与规则，链接为延伸而非语义外包）。

### 命名策略

| 枚举值 | 匹配规则 |
| ------ | -------- |
| `Default` | 忽略大小写同名匹配（零变更的默认行为） |
| `CaseSensitive` | 大小写敏感精确匹配 |
| `SnakeCase` | 去下划线 + 转大写的归一化比较 |
| `UpperSnakeCase` | 归一化算法与 `SnakeCase` 等价，语义上用于全大写蛇形场景 |

归一化口径：`SnakeCase` 与 `UpperSnakeCase` 为"去下划线 + 转大写"，因此 `user_name`、`USER_NAME`、`UserName`、`username` 归一化后同键；`Default` 只忽略大小写、不忽略下划线。策略只作用于无显式标注的匹配，`[MapProperty].FromField` 恒字面匹配（`OrdinalIgnoreCase`），不随策略变化。

### 匹配与叠加规则

- 方向语义：ToEntity 方向忽略 ⟺（双向清单命中）∨（ToEntity 清单命中）；ToDto 方向仅双向清单生效。"ToDto" 是目标类型标注 `[MapFrom]` 的方向，"ToEntity" 是反向；
- 剥缀变体仅在源侧生成，顺序为原名 → 剥每个命中前缀 → 剥每个命中后缀 → 先剥前缀再剥后缀；剥缀变体仍按命名策略归一化后比较；
- 撞键先到先得：源字典按属性声明序填充，先声明的属性（含其剥缀变体）抢占归一化键，后声明属性的同键被丢弃，声明顺序影响绑定结果。

### 豁免机制

全局忽略是 fail-closed（命中即忽略），目标属性可用 `OverrideIgnore = true` 做**映射对级**豁免，源、目标两侧的全局判定均跳过。

- 豁免覆盖**五特性**：`[MapProperty]` / `[Convert]` / `[Mask]` / `[ConvertBack]` / `[MapFormat]`，任一携带 `OverrideIgnore = true` 即豁免整对映射；判定挂在目标属性上；
- `[MapIgnore]` **不参与**豁免，属性级显式忽略恒生效；同一属性既标 `[MapIgnore]` 又标 `OverrideIgnore = true` 是矛盾配置，报 `NCMAP022`。

### 诊断

| 码 | 严重度 | 触发条件 | 处置 |
| --- | ------ | -------- | ---- |
| `NCMAP020` | Warning | 配置值非法（枚举越界、清单超 8 项、复杂类型全名） | 该项降级"未设置"，不阻断编译；按上限与合法取值修正 |
| `NCMAP021` | Warning | 显式标注（`[MapProperty]` / `[Convert]` / `[ConvertBack]` / `[Mask]` / `[MapFormat]`）命中全局忽略且无 `OverrideIgnore` | 删除标注随全局忽略，或加 `OverrideIgnore = true` 救回 |
| `NCMAP022` | Warning | `[MapIgnore]` 与 `OverrideIgnore = true` 同标（矛盾配置） | 移除其一 |

### 生效与迁移

三层优先链，逐项非空覆盖，细粒度层优先：

```text
属性级标注（[MapProperty] / [MapIgnore] / [Convert] / [Mask]）
      >  [assembly: MapDefaults]
      >  MSBuild 属性（<NCMapperNamingStrategy> 等，buildTransitive targets 通道）
      >  内建默认（Default 策略 + 空清单）
```

- 注册与冻结：源生成器在编译期读取 `[assembly: MapDefaults]` 并生成快照注册代码，首次映射或 `Validate()` 时冻结，冻结后无修改入口；多程序集声明内容冲突时首次冻结抛 `MapConfigurationException`（fail-fast），内容一致则幂等合并；
- 回退通道：无源生成代码的程序集（无 `[MapFrom]` 类型）由 `Mapper` 反射读取程序集特性兜底；该路径不读 MSBuild 属性；运行时可用 `Mapper.GetGlobalConfig()` 取只读视图（调试与断言用）；
- 迁移对照：把逐属性 `[MapProperty(FromField = "user_name")]` 加逐个 `[MapIgnore]` 收敛为一次 `[assembly: MapDefaults(NamingStrategy = SnakeCase, IgnoreNames = [...])]`，DTO 侧零标注。

MSBuild 通道、反射兜底与冻结细节见 [global-config §三层优先链](../infrastructure/global-config.md)。

### 示例

`dotnet run --scenario 606`，逐属性标注版与全局配置版的结果一致性校验：

[!code-csharp[全局配置生效（MapGlobal）](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal)]

[!code-csharp[全局配置生效（MapGlobal） · RunSummary](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal_RunSummary)]

[!code-csharp[全局配置生效（MapGlobal） · Run](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal_Run)]

## 4. MapCtorParam：无特性侧形态说明

构造器形参绑定**无特性侧形态**：公共 API 无 `MapCtorParamAttribute`。原因是声明式特性无法表达该终端的两点注入，它需要同时给出**目标构造器形参名**与**源侧取值表达式**，而特性实参只能是编译期常量。

需要构造器形参绑定时，走下列路径之一：

- 把该映射对改在 Fluent 通道声明（`.MapCtorParam`）；同一 (源, 目标) 对禁止双通道声明，双声明报 `NCMAP_F11`；
- 为目标类型设计同名属性，走自动同名匹配；record 的定位参数即同名 init 属性，既有 `.Map` 即可喂构造器。

Fluent 侧两形态与诊断（`NCMAP_F51`-`NCMAP_F54`）见 [fluent/terminals §MapCtorParam](../fluent/terminals.md)（含通道不对称条款）。

## 5. 诊断归属速记

本页主题在两条通道上的诊断码归属映射。全量诊断码清单由 [diagnostics/rules.md](../../diagnostics/rules.md) 唯一承载，本表不扩为全表。

| 主题 | 特性侧码 | Fluent 侧码 |
| ---- | -------- | ----------- |
| 条件守卫 | `NCMAP041` | `NCMAP_F40`-`NCMAP_F45` |
| 多态子类型 | `NCMAP_F46`、`NCMAP_F49`（特性侧专码） | 泛型约束 CS0311/CS0314（无 NCMAP 码） |
| 全局忽略与全局配置 | `NCMAP020`-`NCMAP022` | 同源（豁免门重载，无独立码） |
| 构造器形参绑定 | 无（见 §4） | `NCMAP_F51`-`NCMAP_F54` |

## 6. 小结：三列速查

| 能力 | API（Fluent 侧） | 特性（Attribute 侧） |
| ---- | --------------- | --------------------- |
| 属性级条件守卫 | `.Member(d => d.X).MapWhen(cond)` / `.MapWhen<TSource>(cond)`（命名 + 内联双形态） | `[MapWhen(nameof(谓词))]`（仅命名形态） |
| 多态子类型登记 | `.MapSubclass<TSrcDerived, TTargetDerived>()` | `[MapSubclass(typeof(派生源), typeof(派生目标))]` |
| 全局默认配置 | 无独立终端（同一程序集特性，运行时 `Mapper.GetGlobalConfig()` 读只读视图） | `[assembly: MapDefaults(...)]`（11 项参数） |
| 全局忽略豁免 | 同源（豁免门重载） | `OverrideIgnore = true`（五特性任一带） |
| 构造器形参绑定 | `.MapCtorParam(paramName, source)` | 无特性侧形态 |

## 进一步阅读

- 相关篇目：[basics](basics.md) · [attributes-index](../../reference/attributes-index.md) · [api-comparison](../../reference/api-comparison.md)
- 语义源页对照：[fluent/terminals](../fluent/terminals.md)（Fluent 侧口径） · [infrastructure/global-config](../infrastructure/global-config.md)（全局配置深挖）