---
wiki-id: fluent/terminals
涉及诊断码: NCMAP_F40-F45, F46-F54, NCMAP041
库版本: Unreleased（随下个版本发布）
更新时间: 2026-10-23
---

# Fluent 四终端：MapWhen / MapCtorParam / MapSubclass

> Fluent 线高级终端专题（条件守卫、构造器形参绑定、多态子类型分派）。四终端的通道不对称权威口径与特性侧等价形态见 [api-comparison](../../reference/api-comparison.md)；`MapCtorParam` 为 Fluent 独有（无特性侧形态）。

## MapWhen：条件守卫

`MapWhen` 是 Member 三段式配置段终端：为 `.Member` 锚定的目标字段声明**条件守卫**——仅当谓词求值返回 `true` 时执行该字段的映射赋值（真才映射，极性入名）。守卫可锚定两类映射载体：自动匹配的同名源属性与显式 `From` 声明；同字段段的 `Convert`/`Mask` 伴随终端同构（组合合法）。

**两个重载即两种谓词入参形态**（挂在 `MemberHead`/`MemberConfig` 字段段上）：

| 重载 | 谓词入参 | 形态 |
| --- | --- | --- |
| `MapWhen(Func<TProp, bool>)` | 源字段值（锚点字段映射的源值；TProp 已由 Member 锚定） | T1 字段值（最常见；**非泛型**——内联 lambda 直接可用） |
| `MapWhen<TInput>(Func<TInput, bool>)` | 整个源对象（`TInput` = TSource） | T2 整源（多字段判定；**须显式类型参**——内联 lambda 无推断位，写 `.MapWhen<源类型>(...)`；类型参既非 TProp 也非 TSource 报 `NCMAP_F71`） |

- **形态 A（命名静态方法组）**：`public/internal static bool M(T)`（与生成代码同程序集可见）——T1 位直给 `MapWhen(M)`（入参 == 源字段类型），T2 位 `MapWhen<TSource>(M)`（入参 == 源实体类型）；
- **形态 B（无捕获内联 lambda）**：表达式体 lambda，转录引擎按符号级限定名重写后转录进生成代码（无运行时委托，AOT 友好；T1 位可直写，T2 位须显式类型参）；
- **特性侧** `[MapWhen(nameof(HasContent))]`（属性级，`Inherited = true`，单实例）：谓词方法在**被标注属性的声明类型**上按名解析（单类型自足，与 `[Convert(string)]` 单参形态同规则）；表达力不对称：特性实参是编译期常量，仅命中形态 A，内联转录（形态 B）为 Fluent 独有。

**要点**：

- **链路次序 = 条件 → 转换 → 赋值**：条件看到转换前的源字段值；同字段段 `From` + `MapWhen` 组合合法，谓词读的是源字段值而非转换结果；
- **条件假跳过赋值**：新建路径保持初始化器值（无初始化器则 default）；`_Update` 路径保留 dst 既有值；构造器映射目标显式落 `default`；
- **单条件**：同一属性不得声明两次 MapWhen（F44）；负向过滤用谓词取反表达，多判据在单一谓词内组合。**IgnoreWhen 类负向入口永不立项**：取反谓词语义等价，API 不提供第二个入口，请在谓词内写取反；
- **反向映射不消费本声明**：守卫仅作用于正向（Entity→DTO）；
- **PATCH 惯用法**：`.Member(d => d.Name).MapWhen(DtoRules.NotNull)` 表达 null = 未提供 → 跳过赋值（守卫仅跳过映射，不区分"未提供"与"显式清空"，精确语义需调用方自行前置判断）；
- **FromSource 块**：块内 Member 链的 MapWhen 有真实发射落点（块内子映射真实赋值）；主链锚定"仅由跨源覆盖"的属性报 `NCMAP_F45`。

**诊断**（触发条件与处置）：

| 码 | 触发条件 | 处置 |
| --- | --- | --- |
| `NCMAP_F40` | 形态 A 方法组契约失败（不可解析/非静态/可见性不足/签名不符/缺隐式转换） | 谓词收敛为 `public/internal static bool M(T)`，T 为源字段类型或源实体类型 |
| `NCMAP_F41` | 形态 B 内联 lambda 捕获变量或访问私有成员（转录禁令） | 提取为命名静态方法，改形态 A |
| `NCMAP_F42` | 形态 B 内联 lambda 含语句体或白名单外节点（dynamic/unsafe/await/查询语法等；白名单含成员链/算术比较/条件访问/静态与实例调用/nameliteral） | 同上 |
| `NCMAP_F43` | 锚点字段无可守卫的映射（无自动匹配源且无显式 `From`，或被 `Ignore` 排除；守卫永不生效） | 修正锚点字段（多为拼写错误）或先声明映射再挂守卫 |
| `NCMAP_F44` | 同一属性重复声明 MapWhen（单条件） | 判据合并进单一谓词 |
| `NCMAP_F45` | 主链 MapWhen 锚点属性仅由 FromSource 跨源覆盖（主链 Compose 不展开逐属性赋值链，守卫无发射落点） | 将守卫声明在 FromSource 块内 |
| `NCMAP041` | 特性侧 `[MapWhen]` 谓词解析失败（方法不存在/多重载歧义/非静态/可见性不足/签名不符/入参类型既不等于源字段类型也不等于源实体类型） | 谓词声明于被标注属性所在类型，签名收敛到 T1/T2 二选一 |

**投影行为**：投影路径恒降级（守卫谓词为 CLR 委托路径，SQL 不可翻译；分类 `MapWhen`，与 `[Mask]`/`[Convert]` 同口径）。详见 [ORM 降级机制 §2.3](../../explanation/orm-degradation.md)。

**可运行示例**（`dotnet run --scenario 105` 实跑验证；命名方法组 / 内联转录 / 整源 / 同锚点组合四形态同链）：

[!code-csharp[MapWhen 四形态同链（Fluent）](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MapWhenFluentMapper)]

[!code-csharp[MapWhen 四形态同链（Fluent） · Configure](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MapWhenFluentMapper_Configure)]

特性侧属性级守卫（谓词方法声明于 DTO 自身，单类型自足）：

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto)]

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫 · Id](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Id)]

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫 · Name](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Name)]

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫 · Score](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Score)]

[!code-csharp[特性侧 \[MapWhen\] 属性级守卫 · HasContent](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_HasContent)]

场景输出校验跳过值语义：全真全映射、新建条件假保持默认、`_Update` 条件假保留既有值而条件真照常更新、特性侧同构：

[!code-csharp[MapWhen 跳过值断言（场景 105 四个子场景）](~/samples/NCMapper.Samples/01-Fluent/MapWhenScenarios.cs#MapWhen_Run)]

MapWhen 内联 lambda 转录的可运行演示（场景 108）：

[!code-csharp[无捕获内联 lambda 放行（真/假路径对照）](~/samples/NCMapper.Samples/01-Fluent/InlineLambdaScenarios.cs#FluentInline_Passthrough)]

[!code-csharp[捕获场景提取静态方法组后生效](~/samples/NCMapper.Samples/01-Fluent/InlineLambdaScenarios.cs#FluentInline_CapturedExtracted)]

## MapCtorParam：显式构造器形参绑定

`.MapCtorParam` 把源属性值显式绑定到目标类型公共构造器的指定**形参**。触发场景为**无公共无参构造器**的目标类型：纯构造器形参不对应任何目标属性（不可变带参构造类：无目标属性可锚定、`Member` 没有可写的 dest，自动同名也绑不上），绑定缺口经两点注入承接。

**两个形态**：

| 形态 | 签名 | 源锚定 |
| --- | --- | --- |
| 形态 1（主，推荐） | `MapCtorParam<TSrcProp>(string paramName, Expression<Func<TSource, TSrcProp>> sourceProp)` | 单段纯属性路径 lambda；`TSrcProp` 类型参数由 lambda 返回类型推断；多段链/方法调用等非纯路径沿用既有 lambda 诊断机制拒绝 |
| 形态 2（辅） | `MapCtorParam(string paramName, string sourcePropertyName)` | 源属性名字符串（适用于无类型上下文/动态场景）；名字拼错或无公共 getter 时生成期报 `NCMAP_F51`（fail-fast，不静默绑空） |

**要点（两点注入）**：

- **选器加权**：绑定形参名命中的候选公共构造器升权优先；多命中时维持"参数最多优先"的机械裁决；
- **参数匹配优先**：构造目标时，构造器实参先查显式绑定表，再走同名匹配；
- 形参名与目标参数集**忽略大小写**匹配；ByRef/指针形参不可绑定；
- **record 场景边界**：record 位置参数即同名 init 属性，既有 `.Member(d => d.X).From(...)` 声明即可喂构造器（匹配键 = 目标属性名）；MapCtorParam 是"免目标属性锚定"的便利性补充，不是 record 的必需项；
- **通道不对称（Fluent 独有）**：本终端无特性侧形态（声明式特性无法表达构造器形参的两点注入，PublicApi 无 `MapCtorParamAttribute`）。`[MapFrom]` 通道声明的 DTO 不能声明本终端；确需绑定时，将该对改在 Fluent 通道声明（同对双通道声明互斥，`NCMAP_F11`），或为目标设计同名属性走自动匹配。

**诊断**（触发条件与处置）：

| 码 | 触发条件 | 处置 |
| --- | --- | --- |
| `NCMAP_F51` | 绑定源属性不存在或无公共可读 getter（形态 2 字符串解析失败；形态 1 非纯路径同口径） | 传入单段 lambda（形态 1）或有效的公共属性名（形态 2） |
| `NCMAP_F52` | 目标类型没有任何公共构造器声明该名的可绑定形参（消息列出可用参数名） | 修正形参名（忽略大小写）；ByRef/指针形参不可绑定 |
| `NCMAP_F53` | 同一构造器形参重复绑定，或绑定与显式 `From` 条目指向同一形参（矛盾声明，fail-fast 不静默覆盖） | 移除其一；record 名不一致对的绑定用既有 `.Member(...).From(...)` 即可解决 |
| `NCMAP_F54` | 多个公共构造器参数数量并列第一（Info 提示：生成选器静默按参数数量裁决） | 声明 `.MapCtorParam` 绑定显式消解构造器选择 |

**投影行为**：本终端声明不进入不可翻译分类集（`ClassifyUntranslatableDeclaration` 无 MapCtorParam 类别），不触发投影降级：仅含纯绑定声明（其余为纯 `From` 来源声明 / `Ignore`）的对仍走 SQL 侧投影；同对含 `Mask`/`Convert`/`FromJson`/钩子/`MapWhen`/`FromSource` 声明时，由它们决定降级。

**可运行示例**（`dotnet run --scenario 106` 实跑验证；两形态 + record 经既有 `.Map` + `MapList` 元素级）：

[!code-csharp[MapCtorParam 双形态与 record 边界澄清](~/samples/NCMapper.Samples/01-Fluent/CpModels.cs#CtorParam_CtorParamFluentMapper)]

[!code-csharp[MapCtorParam 双形态与 record 边界澄清 · Configure](~/samples/NCMapper.Samples/01-Fluent/CpModels.cs#CtorParam_CtorParamFluentMapper_Configure)]

## MapSubclass：多态子类型映射

`.MapSubclass<TSrcDerived, TTargetDerived>()` 为当前 (TSource, TTarget) **基类映射对**登记一对子类型映射：基类映射运行时按源实例的**实际类型**路由到子类专属映射体（isinst 分派链）。适用于"基类实体到基类 DTO、派生实体到派生 DTO（含派生专有字段）"的多态列表与多态字段场景。

**语法**（对级终端，作用于整对而非属性）：

| 声明侧 | 形态 | 锚定要求 |
| --- | --- | --- |
| Fluent | `cfg.For<TEntity, TBaseDto>().MapSubclass<TDerived, TDerivedDto>()`（可链式登记多对） | `cfg.For` 即锚定基类对；DTO 侧不得再标 `[MapFrom]`（同对双通道声明互斥 `NCMAP_F11`） |
| 特性 | `[MapFrom(typeof(TEntity))]` + `[MapSubclass(typeof(TDerived), typeof(TDerivedDto))]` 标注于基类 DTO（type-level 多实例，每实例登记一对） | 标注类须同时标注 `[MapFrom(typeof(基类源))]`，缺锚定报 `NCMAP_F49` |

**分派语义**（Map/_Update/MapList/MapListInto 四族一致）：

- **可赋值匹配**：`src is TSrcDerived` 命中即路由子类体；未登记的更深派生类型命中**最近登记祖先层**；
- **最派生优先**：多对登记同时命中时按（源侧继承距离，目标侧继承距离）字典序仲裁；部分重叠登记（同源异层/同源异目标）合法不报；
- **基类兜底**：未命中任何登记子类（含未登记的第三方派生）走基类体，子类专有字段保持默认；
- **双实例匹配**（`_Update`/`MapListInto`）：src 与 dst 同时 isinst 命中才路由子类体；dst 运行时类型非登记子类目标时回落基类体（子类专有字段不写入）；
- **反向映射**：子类对经反向注册臂自动产出反向映射与反向直调重载（登记非单向）；反向基类对不消费分派（反向目标是调用方已存在的实体实例），不对称由 `NCMAP_F50`（Info）提供可见性。

**继承约束与边界**：

- **两通道约束口径**：Fluent 侧泛型约束（`where TSrcDerived : TSource`）在调用点被编译器拦截（CS0311/CS0314）；特性侧 typeof 实参无编译期约束，继承关系不满足在提取期报 `NCMAP_F46`；
- `TTargetDerived == TTarget`（源侧多态、目标收敛）为合法用例；`TSrcDerived == TSource`（登记基类自身，分派自引用死循环）与完全同对重复登记报 `NCMAP_F47`；
- 子类目标无公共可映射面或不满足构造约束报 `NCMAP_F48`；
- `[MapSubclass]` 为 `Inherited = false`（派生 DTO 不继承父类的子类登记）、`AllowMultiple = true`（多实例多对登记）。

**诊断**（触发条件与处置）：

| 码 | 通道 | 触发条件 | 处置 |
| --- | --- | --- | --- |
| `NCMAP_F46` | 特性侧专码 | `[MapSubclass(typeof(S), typeof(T))]` 实参继承关系不满足（S 须派生自基类对源、T 须派生自基类对目标；特性形态无泛型约束，本校验在提取期执行） | 修正 typeof 实参；Fluent 侧同类错误由 CS0311/CS0314 在调用点拦截，不产生本码 |
| `NCMAP_F47` | 通用 | 完全相同 (TSrcDerived, TTargetDerived) 对重复登记（首条保留），或 `TSrcDerived == TSource` 登记基类自身（基类对分派到自身产生无限循环，已拒绝）；部分重叠登记合法，按最派生优先仲裁不报 | 移除重复登记或基类自身登记 |
| `NCMAP_F48` | 通用 | 登记的子类对无可映射产物：子类目标类型无公共可映射面或不满足构造约束（合成快照被跳过或零属性映射） | 为子类目标提供公共可映射属性与可构造形态 |
| `NCMAP_F49` | 特性侧专码 | `[MapSubclass]` 标注类无 `[MapFrom]` 锚定，基类映射对不存在，该登记未被任何分派面引用（孤儿登记） | 补 `[MapFrom(typeof(基类源))]`，或改用 Fluent 通道（`cfg.For` 即锚定） |
| `NCMAP_F50` | 通用（Info） | 登记 MapSubclass 的对存在静默面："reverse-asymmetry"（反向基类对已生成但永不分派，子类专有字段不填充）/"dual-instance-fallback"（双实例匹配回落基类体） | 既定语义提示，无需处置；需子类字段写入时检查 dst 运行时类型 |

**投影行为**：登记 MapSubclass 的基类映射对在 ORM 投影下**恒降级**（isinst 分派链 SQL 不可翻译，分类 SubclassDispatch；`[MapSubclass]` 实现 `IProjectionPolicy`，与 `[Mask]`/`[Convert]`/`[MapWhen]` 同口径）。

**双侧性口径**（两口径分别表述，勿混）：**功能双侧对称**，Fluent `.MapSubclass` 与特性 `[MapSubclass]` 为等价对，分派语义同源；**诊断码通道不对称**，`F46`/`F49` 为特性侧专码，Fluent 侧同类错误由泛型约束 CS0311/CS0314 在调用点被编译器拦截（生成器不可达），用户不应在 Fluent 侧寻找这两码。

**可运行示例**（`dotnet run --scenario 107` 实跑验证；四族分派 + 兜底 + 反向 + 特性侧）：

[!code-csharp[Fluent 对级登记与特性侧多实例登记](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · ScAttrAnimalDto_Name](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Name)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · ScAttrAnimalDto_Weight](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Weight)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · ScAttrDogDto](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrDogDto)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · ScAttrDogDto_Breed](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrDogDto_Breed)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · ScAttrCatDto](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrCatDto)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · ScAttrCatDto_Indoor](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrCatDto_Indoor)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · SubclassFluentMapper](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_SubclassFluentMapper)]

[!code-csharp[Fluent 对级登记与特性侧多实例登记 · SubclassFluentMapper_Configure](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_SubclassFluentMapper_Configure)]

[!code-csharp[四族分派、基类兜底与 _Update 双实例匹配](~/samples/NCMapper.Samples/01-Fluent/SubclassScenarios.cs#MapSubclass_Dispatch)]

## From 终端族叠加与运行时元数据

`.Member(dest).From(...)` 终端族支持多段叠加与跨型绑定（跨型形态由生成代码内建转换承接）；Compose 跨源声明（速记 + 分组块）的明细在运行时可经 `RuntimeSnapshotStore.TryGetFluentDetailMeta` 读取（对应 `reference/architecture-map.md` §6 `RuntimeComposeMapping` 行）。三类叠加形态同链演示与 Compose 明细逐项断言见场景 109（`dotnet run --scenario 109`）：

[!code-csharp[From 终端族三形态叠加与 Compose 跨源声明（Mapper 配置）](~/samples/NCMapper.Samples/01-Fluent/RuntimeComposeScenarios.cs#RuntimeCompose_Declarations)]

[!code-csharp[From 终端族三形态叠加与 Compose 跨源声明（Mapper 配置） · Configure](~/samples/NCMapper.Samples/01-Fluent/RuntimeComposeScenarios.cs#RuntimeCompose_Declarations_Configure)]

[!code-csharp[场景 109 全子场景断言与运行时元数据读取](~/samples/NCMapper.Samples/01-Fluent/RuntimeComposeScenarios.cs#RuntimeCompose_Run)]
