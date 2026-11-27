---
wiki-id: explanation/semantics/mapping-rules
库版本: v1.0.0+
更新时间: 2026-11-27
---

# 语义共享层：映射规则

> 本篇是映射匹配规则的**单点同源**页（语义共享层契约：本页承载"匹配规则/嵌套/集合/类型转换"四类语义，两通道 how-to 页共同引用，不再各自复述）。API 语法细节与操作示例见 [declarative/basics](../../how-to/declarative/basics.md) 等操作页，本篇只讲引擎级语义。

## 1. 同名约定优于配置

引擎的默认匹配规则：**同名且类型相容的属性自动映射**，无须任何声明。显式声明仅在差异出现时需要：

| 差异类型 | Attribute 通道 | Fluent 通道 |
|---------|---------------|------------|
| 异名映射 | `[MapProperty("源", "目标")]` | `.Member(d => d.目标).From(s => s.源)` |
| 忽略 | `[MapIgnore]` | `.Member(d => d.目标).Ignore()` |
| 自定义转换 | `[Convert]` / `[ConvertBack]` | `.Convert` / `.ConvertBack`（各 4 形态，Member 段内） |
| 属性级格式化 | `[MapFormat("格式串")]` | `.Format(format)`（Member 段内；源 `IFormattable` → 目标 string，与 `[Convert]` 互斥 `NCMAP045`/`F66`） |

两通道对同一映射对声明等价语义（差异对照见 [api-comparison](../../reference/api-comparison.md)）；同一对同时双声明触发 `NCMAP_F11` 互斥错误。

> **Fluent 写法说明（Member 三段式）**：字段级声明统一写成 `.Member(dest)` 锚定 → `.From(src)` 定来源 → 终端叠加变换，字段名只写一次。上表 Fluent 列已含锚定段，实际写法如 `cfg.For<S,T>().Member(d => d.OrderId).From(s => s.Id)`。

**匹配口径细节**：public 可读写属性 + 大小写不敏感；类型不匹配的属性自动跳过（不抛异常）。

**异名映射的取数来源语义**：`[MapProperty(typeof(T), nameof(T.Field))]`（Fluent 等价 `.Member(d => d.X).From(s => s.Y)`）的第一个类型参数 `T` 是**字段实际取数来源的源类型**：等于 `[MapFrom]` 主源即同源映射（最常见）；等于另一源类型即跨表 / Compose 映射——从另一源对象取值，受支持且**无需**与主源一致。字段不存在时同源报 `NCMAP001`、跨表报 `NCMAP005`；目标属性类型与源字段类型无法赋值时报 `NCMAP002`（与"第一参数是否等于主源"无关）。

**映射规则优先级**（多种机制同时作用于同一属性时）：

| 优先级 | 机制 | 说明 |
|---|---|---|
| 1（最高） | 忽略（`[MapIgnore]` / `.Ignore`） | 标注的属性直接跳过，不参与映射 |
| 2 | 自定义转换（`[Convert]` / `.Convert`） | 转换器优先执行，覆盖其他映射逻辑 |
| 3 | 异名路由（`[MapProperty]` / `.Member(d => d.X).From(...)`） | 指定源属性 |
| 4（最低） | 同名匹配 | 默认行为，同名同类型自动匹配 |

**白名单模式（对级开关）**：上述默认匹配可按对关闭——特性通道在 DTO 类上标 `[MapNoAutoMatch]`，Fluent 通道用 `cfg.For<S,T>().NoAutoMatch()`，两通道语义逐条对等：

| 项 | 白名单开启后的行为 |
|---|---|
| 关闭的三样 | ①未声明同名自动配对 ②配置感知补匹配 ③自动扁平化（`AddressCity` 不再自动拼 `Address.City`） |
| 照常 | 映射本身（方法生成/注册/调用不变）、显式 `[MapProperty]` / `.Member(d => d.X)` 声明及其叠加变换、全局忽略 fail-closed |
| 显式声明边界 | 同属性标注 `[MapProperty]`（特性通道 ctor 为 `(Type fromSource, string fromField)`，FromField 须显式给出；Fluent 侧 `.Member(d => d.X)` 点名即声明、缺省同名默认）；带变换特性（`[Convert]`/`[Mask]`/`[MapFormat]`/`[MapWhen]`）但无 `[MapProperty]` → `NCMAP047`（Error，两条出路：补声明或移除变换特性） |
| 反向派生 | 仅翻转正向显式声明（自动匹配属性不再参与反向，含写回段扁平化不写回） |
| ORM 投影 | 白名单对强制降级为二步内存映射（`IProjectionPolicy` 协议位——未声明列不得被读出，列裁剪失效） |
| 运行时反射路径 | 门在反射映射全通道一体执行——属性赋值（Block 路径）、MemberInit 快路径、构造器按名匹配（record / 带参构造目标）三通道同一判定；"未声明同名不配对"在任一通道均成立，白名单对的 record 目标未声明参数一律取 `default` |

### 白名单的已知限制与可观测性

**① 未声明属性的诊断呈现——无专门"白名单跳过"分类（已知限制）**

白名单对下，未声明属性是**静默不映射**的：它既不属于"映射失败"，也不会被标记成"故意跳过"。诊断面现状（2026-10-23 实读 `src/NCMapper/` 源码）：

| 工具 | 对白名单的态度 | 实读依据 |
|---|---|---|
| `MapValidator` | **无白名单感知**——未声明属性照常计入 `unmapped` 统计，报告面无法区分"白名单跳过的未声明"与"真·漏映射" | `MapValidator.cs` 内 `NoAutoMatch` / 白名单关键词命中 **0 处** |
| `MapTracer` | **无白名单感知**——无"白名单跳过（未声明）"分类 | `MapTracer.cs` 内 `NoAutoMatch` / 白名单关键词命中 **0 处**；其跳过原因仅 `MapIgnore` / `GlobalIgnore` / 源类型不匹配三类 |
| `ExpressionBuilder`（反射映射路径） | **仅一个子集有 Trace**：目标属性带 `[Convert]` / `[Mask]` / `[MapFormat]` / `[MapWhen]` 但缺 `[MapProperty]` 时，在 `MapTracer.GloballyEnabled` 下输出"白名单模式下带映射变换特性但未标注 `[MapProperty]`"提示 | `ExpressionBuilder.cs:173`（判定族 `HasWhitelistTransformAttribute`，定义于 `:446`；提示文本在 `:175`） |

**因此**：白名单开得越多，`MapValidator` 的 `unmapped` 数越不能直接当作缺陷数读——因为其中有相当比例是"用户主观决定不映射"的属性，而库当前**没有**把它们与真漏映射分开呈现。这是**已知限制**，不是已实现能力。

**可观测替代**：编译期 `NCMAP047`（Error）已覆盖"带变换特性却缺 `[MapProperty]`"这一最易出错的子集——该形态是"以为在映射、实际被白名单跳过"的高发来源，因此有 Error 兜底；其余未声明属性仅静默跳过。

**② 白名单在继承形态下的覆盖状态**

白名单标注（`[MapNoAutoMatch]`，`AttributeUsage(..., Inherited = true)`）与 Fluent 对级 `.NoAutoMatch()` 在派生类型场景下分两种形态，状态不同：

| 形态 | 场景 | 状态（2026-10-23 实读） |
|---|---|---|
| 形态 A′ | 派生 DTO **自身声明** `[MapFrom]` 成对 + 基类带白名单标注 | **已收敛**：快照路径沿基类链读取，白名单生效 |
| 形态 B | 派生 DTO **零声明**（纯继承基类，自身无 `[MapFrom]`） | **投影降级路径已闭合**：`DegradationDetectorBase` 三处特性扫描均带 `inherit: true`（`:343` 类级 / `:366` 属性级 / `:496` 嵌套属性级），基类白名单对派生类型可见，投影降级随之触发 |

形态 B 的闭合有定桩用例守护：`NoAutoMatchRuntimeTests.Inherited_Leg4_ProjectionScan_DerivedDto_Q6_Measured` 已由"定桩缺口存在"翻转为**修复验证断言**（`Assert.True(needs)` + 断言降级 reason 含 `MapNoAutoMatchAttribute`）。

> **范围限定**：上表只陈述**投影降级路径**。纯反射路径的继承（`Inherited_Leg3_PureReflection_DerivedDtoInheritsWhitelist`，经 `IsDefined(inherit: true)`）与属性级/嵌套级扫描各有独立定桩，本页不展开；逐路径读数见 `NoAutoMatchRuntimeTests` 用例清单。

### 白名单相关的其他已知行为

- **子对不继承基类白名单**：经 `.MapSubclass<>()` / `[MapSubclass]` 注册的子类型对独立提取，不走主对的白名单读取点——子对仍会自动匹配。
- **两条路径"对侧标注"口径分界**：反射路径两侧对称探测（源侧标 `[MapNoAutoMatch]` 亦激活白名单）；生成侧只读目标类型继承链（实体侧标注不激活）。非缺陷，属两层语义各自正确。

## 2. 嵌套与扁平化

- 源侧嵌套对象属性可按**点路径**直达：`s => s.Customer.Name` 或特性通道的对应路径声明；生成器按路径展开取值。
- PascalCase 嵌套扁平化：目标 DTO 上的扁平属性（如 `CustomerName`）自动匹配源侧嵌套路径 `Customer.Name`（源生成器编译期解析）。
- 反向写回：扁平属性可写回实体嵌套路径，中间节点不可实例化时触发 `NCMAP031` 错误；null 源清空叶节点。
- 显式点路径的已知限制：映射值不消费 `[Mask]`/`[Convert]`（`NCMAP030` 可观测提示）。

**索引路径段**（`A.B[0].C`）：路径中途经过集合时用索引段 `[n]` 取第 n 个元素继续取属性，索引段可位于任意段位。语义为 `ElementAtOrDefault(n)`：集合 null、空集合、越界均返回 `default`，不抛异常。四引擎形态：运行时 `Mapper.Map` 与源生成器一致（`ElementAtOrDefault` + null 守卫短路）；EFCore 投影翻译为 `LIMIT 1 OFFSET n` 子查询（SQL 无 ORDER BY，语义是**无序取一**，空集合返回 NULL）；FreeSql 投影 SQL 侧不可翻译、自动降级为内存映射（值正确，失去 SQL 侧列裁剪）。连续索引 `A[0][1]` 不支持（全引擎登记限制）；反向方向集合索引路径不参与写回（跳过 + Trace）。段解析失败：源生成器报 `NCMAP001`（含失败段名）、运行时抛 `MapConfigurationException`。

**递归嵌套映射防护**（同类型对自引用结构，如树形 DTO）：编译期内联深度上限 `Mapper.MaxInlineDepth`（默认 5），超限停止内联回退 `MapperBase<T>.Map` 委托调用；运行期双计数器深度检查，超限重置归零并抛 `MapException`；递归深度上限 `Mapper.MaxRecursionDepth`（默认 64，上限 4096，配置方式见 [DI 集成](../../how-to/infrastructure/di.md)）。深层递归（超过上限）会抛 `MapException`，请按实际结构深度配置或调整映射结构。

### 嵌套导航由调用方负责（内存映射不加载导航）

内存映射指不经 SQL 投影、直接在内存里跑映射的两条入口：核心库的 `Mapper.Map` 与 Dapper 通道的 `IDbConnection.AsDto`。两者的契约是同一条：

> **只做字段映射，不负责加载导航。** 引擎不会为 DTO 的嵌套对象追加 JOIN、不会触发延迟加载、也不会发起二次查询；它只把「调用方已经拿到的那个对象」上的属性搬到目标 DTO 上。

因此在「源实体的导航属性没被加载（值为 null）」时，DTO 里对应的嵌套对象就是 **null**——这是**预期行为**，不是映射缺陷。取嵌套数据的责任在调用方，按通道分列：

| 通道 | 嵌套数据由谁取 | 未取到时 DTO 表现 |
|---|---|---|
| `Mapper.Map`（核心库） | 调用方在查询层先 `Include` / `JOIN` / 显式加载，再把实体交给映射 | 嵌套对象为 `null` |
| `IDbConnection.AsDto`（Dapper） | 调用方自己写 SQL：需要的关联表在 SQL 里 `JOIN` 或子查询取出 | SQL 未取出的嵌套字段为 `null` |
| `AsDto`（EFCore / FreeSql 投影通道） | 引擎把嵌套翻译成 SQL `JOIN` 自行取（属投影通道，与本契约不同） | — |
| `AsDto`（EFCore / FreeSql 降到内存映射时） | ORM 侧自动 `Include`（可观测：Trace 输出依赖清单）；仍取不到的导航字段为 `null` | 嵌套对象为 `null` |

**与 ORM 投影通道的分界**：投影通道（`IQueryable` / `ISelect` 上的 `AsDto`）会把嵌套翻译成 SQL，语义自然不同；本节的「不加载导航」只约束**内存映射**两条入口。两条通道的降级关系见 [ORM 投影与降级](../orm-degradation.md)。

**为什么不做成"自动帮我加载"**：内存映射拿不到查询上下文（连接、事务、跟踪状态），一旦在映射内部发起查询，N+1 与隐式网络往返都会不可控，也会让纯函数式的映射变得有副作用。所以库选择把这条边界显式写出来，而不是偷偷做。

## 3. 集合与字典

- 集合元素按元素类型递归套用同一匹配规则（`List<T>`/数组/`Dictionary<K,V>`/`HashSet<T>` 等集合形态）。
- 目标集合属性元素类型不可映射时报 `NCMAP007`（源属性未映射，Info）。
- 集合到字典、非泛型字典等特殊形态的转换矩阵见 samples 场景 201（Basic Mapping）与 401（Deep Copy）。

**支持的集合类型矩阵**（`CollectionTypeDetector` 自动识别源与目标集合类型；操作用法见 [declarative/basics](../../how-to/declarative/basics.md)，批量就地映射见 [MapListInto](../../how-to/mapping/map-list-into.md)）：

| 集合类型 | 目标类型 | 行为 |
|---|---|---|
| `List<T>` / `T[]` | `List<U>` / `U[]` | 创建新集合 |
| `IEnumerable<T>` / `ICollection<T>` / `IList<T>` / `IReadOnlyList<T>` | `List<U>` | 枚举后创建新 List |
| `HashSet<T>` / `ISet<T>` | `HashSet<U>` | 创建新 HashSet |
| `Dictionary<K,V>` / `IDictionary<K,V>` / `IReadOnlyDictionary<K,V>` | `Dictionary<K,V2>` | 创建新 Dictionary |
| `Queue<T>` / `Stack<T>` | `Queue<U>` / `Stack<U>` | 创建新集合，保持元素顺序 |
| `ObservableCollection<T>` | `ObservableCollection<U>` | 创建新实例（可绑定 UI） |

**语义要点**：

- 所有集合映射都创建新集合实例，不修改源集合；映射后修改源集合，目标不变
- 嵌套集合（`List<List<T>>`、`Dictionary<string, List<T>>`）逐层递归处理
- Dictionary 映射保留 Key（Key 类型须一致或可隐式转换，不参与映射变换），仅对 Value 走元素映射逻辑
- List→Dictionary 直接转换**不支持**（Key 来源不明确，属应用层业务逻辑）：先 `MapList` 为 List 再 `ToDictionary`

**内部机制**（`CollectionCopier` 分发器）：`CollectionTypeDetector` 识别源/目标集合类型 → 按类型组合选择复制策略 → 逐元素调用元素类型映射（源生成器或运行时表达式树，与单对象同路径）→ 创建目标集合并填充。类型识别在表达式构建期完成，运行时无反射开销；大集合内存占用 = 源集合 + 目标集合（非流式）。

## 4. 类型转换链

赋值前引擎按以下顺序解析转换：

1. **同名同型直赋**：类型隐式可转（`IsImplicitConvertible` 判定）即直赋。
2. **枚举与字符串**：string→enum 按成员名精确匹配（大小写敏感由 `NCMAP023` 提示退化）、数字串标签（别名枚举退化见 `NCMAP024`）；`null` 落 default，**空串抛 `MapException`**（不落 default）。双引擎（源生成器 / 运行时）同口径。
3. **自定义转换器**：`[Convert]`（或 `.Convert`）按四模板匹配静态方法签名（`NCMAP035/036/037` 校验引用合法性）。
4. **完全不相容**：跳过该成员并提示 `NCMAP027`（对齐 AutoMapper 语义）。

**内置类型转换表**（表达式构建期识别，无运行时反射开销；操作用法见 [declarative/basics](../../how-to/declarative/basics.md)）：

| 源类型 | 目标类型 | 示例 |
|---|---|---|
| 枚举 | `string` | `UserStatus.Active` → `"Active"` |
| `string` | 枚举 | `"Active"` → `UserStatus.Active` |
| 数值类型间 | — | `int` → `long`、`decimal` → `double` |
| 可空类型 | 对应非空类型 | `int?` → `int`（值存在时） |

`string` → `DateTime` 等需要解析的转换**刻意不支持**：日期字符串格式不确定、存在文化差异、解析失败的处理（抛异常/默认值/跳过）需要业务决策——这三点都应由业务以自定义转换方法显式表达。

**转换器方法签名模板**（`[Convert]` / `.Convert` / `[ConvertBack]` / `.ConvertBack` 共用）：

| 模板 | 签名 | 适用场景 |
|---|---|---|
| T0 | `M()` | 常量/固定值 |
| T1 | `M(TField)` | 单个源字段值（可跨实体复用） |
| T2 | `M(TSource)` | 从源对象组合多个字段 |
| T3 | `M(TSource, TField)` | 组合 + 字段值本身 |

方法须为 `static` 且声明于锚定类型自身（不含基类继承的静态方法）；可访问性白名单 `public`/`internal`（同程序集）/`protected internal`（同程序集），`private`/`protected` 拒绝（`NCMAP035`）；重载须唯一命中模板，多重载命中报 `NCMAP036`；无任何重载匹配报 `NCMAP037`。转换方法应为纯函数（无副作用），只负责数据变换、不负责映射路由。

**反向转换消费语义**（DTO → Entity）：`[Convert]` / `.Convert` 定义绑定**正向方向**，反向默认不消费（直接属性按类型转换路径写入，报 `NCMAP033` Info）；需要反向自定义转换时显式声明 `[ConvertBack]` / `.ConvertBack`（反向时模板的 `TSource` = DTO 类型）。`[ConvertBack]` 特性形态标注位置三形态：DTO 属性为规范位；实体属性标注按「无源（整个 DTO 入参）」「匹配对兜底（DTO 优先）」与「写回链叶属性兜底（DTO 优先）」消费；与 `[Convert]` 同属性并列合法。完整消费门与写回路径见 [反向映射语义 §3.2](../reverse-mapping-semantics.md)。

## 5. 反向映射契约

`[MapFrom]`（或 Fluent `For<S,T>()`）声明后正反向都可用，无需重复配置：源生成器为正向和反向各生成一套映射代码。反向（DTO → Entity）方向的语义契约：

| 契约项 | 语义 |
|---|---|
| 覆盖范围 | 反向只覆盖**主源字段**（`[MapFrom]` 主源类型的 PropertyMappings）；子源字段（跨表 ComposeMappings）**不参与反向写回** |
| 声明跨表子源字段的 DTO | 反向扩展同样生成（与普通 DTO 同构 6 重载），反向只覆盖主源字段 |
| 写回 null 语义 | 源值 null 且目标链存在 → **清空叶**（可空叶赋 null / 非可空值叶赋 `default(T)`）；目标链不存在 → no-op（不创建中间节点）；源值非 null → 中间节点 null 时创建链并写叶 |
| 转换器（[Convert]） | 正向带转换器的直接属性，反向**不消费**该转换器（转换方法绑定正向方向），按类型转换路径写入并报 NCMAP033（Info） |
| 反向自定义转换（[ConvertBack]） | DTO 属性标注 `[ConvertBack]` 后反向映射**显式消费**该静态转换方法，直接属性与写回路径双端（生成器/运行时）一致；与 `[Convert]` 同属性并列合法——正向只消费 `[Convert]`、反向只消费 `[ConvertBack]`，互不干扰 |
| 脱敏（[Mask]） | 带 [Mask] 的 DTO 属性参与反向写回时脱敏值原样写入实体（有损），报 NCMAP034（Info）警示，建议 `[MapIgnore(ToEntity)]` 阻断 |
| 格式化（[MapFormat]） | 带 [MapFormat] 的 DTO 属性反向**不赋值**（格式化输出有损且随文化，库不猜测反解析；实体侧保持原值，其余字段照常回写），报 NCMAP046（Warn）；需要往返时自行声明 `[ConvertBack]`（两特性方向正交、可同锚并存，详见 [反向映射语义 §5](../reverse-mapping-semantics.md)） |
| init-only 属性 | 更新路径（已有目标对象）无法对 init-only 属性赋值，跳过并报 NCMAP032（Info） |
| 全局忽略 | 全局 ToEntity 忽略清单（`IgnoreNamesToEntity` 等）对反向生效，命中属性跳过 + NCMAP013 |
| 扁平属性 | 含 PascalCase 展平属性的 DTO 不适合反向映射：展平字段（如 `AddressCity`）反向时找不到对应实体属性，静默跳过；需要反向时在 DTO 上显式声明对象属性 |

反向方向的判定细节与写回路径完整语义见 [反向映射语义](../reverse-mapping-semantics.md)。

---

## 6. 三原则（映射声明治理）

NCMapper 对映射声明的消费遵循三条置顶原则（P1/P2/P3）。所有编译期声明诊断都是三原则的检测器落点（检测器对照与全码条目见 [诊断规则](../../diagnostics/rules.md)）：

| 原则 | 一句话表述 | 检测器 |
| ---- | --------- | ------ |
| P1 通道互斥 | 同一类型对只能由一条通道声明 | `NCMAP_F11` · `NCMAP025` |
| P2 侧别互斥 | 映射对在任一侧至多登记一次 | CS0579 · `NCMAP025` |
| P3 配置跟随类锚 | 属性级配置标注无锚不消费 | `NCMAP019` · `NCMAP042` |

逐条展开：

- **P1 通道互斥**：同一类型对 (S, T) 只能由**特性通道或 Fluent 通道之一**声明——两通道在编译期汇入同一快照，双声明即所有权冲突，整对生成阻断；不同类型对可自由混用通道。检测器：`NCMAP_F11`（双声明互斥，Error）· `NCMAP025`（无序互指，Error）。
- **P2 侧别互斥**：映射对在任一侧至多登记一次——`[MapFrom]` 单类至多标注一次（重复标注编译期 CS0579）；`For<A, B>` 与 `For<B, A>` 不得同时声明。检测器：CS0579 · `NCMAP025`。
- **P3 配置跟随类锚**：属性级映射配置标注十一项（`[MapProperty]` / `[MapIgnore]` / `[Mask]` / `[Convert]` / `[ConvertBack]` / `[MapWhen]` / `[MapNoAutoInclude]` / `[BeforeMapping]` / `[AfterMapping]` / `[MapFormat]` / `[MapNoAutoMatch]`）只在所在类有映射声明锚（`[MapFrom]` 或 `cfg.For<S,T>()` 声明目标）时生效；无锚一律不认——不消费、不生效，映射退化为纯按名匹配。检测器：`NCMAP019`（Error，带配置标注但无锚）· `NCMAP042`（Info，Dto 命名启发式）。

**设计意图**：两通道编译期汇流同一快照，三原则保证每个映射对的声明所有权**唯一**——不存在需要运行时裁决"谁的声明生效"的歧义面。白话版、补锚路径（命中 NCMAP019/042 怎么办）与外部程序集/动态类型的边界见 [映射配置所有权三原则](mapping-config-ownership.md)。

## 7. 三原则边界（域外对象）

三原则（§6 P1 通道互斥 / P2 侧别互斥 / P3 配置跟随类锚）管制"通道声明 + 类级映射声明 + 属性级映射配置标注"。以下四类对象在管制域**之外**（不是规则的例外，是判定原语不适用）：

| 域外对象 | 说明 |
|---------|------|
| 全局忽略清单 | 程序集级 `[assembly: MapDefaults]` 的 `IgnoreNames` 等配置——全局配置、不属于任何类，无锚类同样被命中（详见 [全局映射配置](../../how-to/infrastructure/global-config.md)） |
| 嵌套按名递归 | 嵌套对象按名匹配的基线行为；P3 只管嵌套内的属性级标注 |
| 类级 `[MapTree]` | 树构建定义（`ToTreeList` 调用即激活路径），不是映射对配置 |
| `[MapSubclass]` | 缺锚由专用诊断 `NCMAP_F49` 提示（检测器分工） |

---

## 8. 相关诊断

本篇规则的违例诊断全集见 [diagnostics/rules.md](../../diagnostics/rules.md)；涉及码：`NCMAP001 / 002 / 006 / 007 / 027 / 030`（匹配）、`NCMAP023 / 024 / 033 / 035 / 036 / 037`（转换）、`NCMAP043 / 044 / 045 / 046`（MapFormat 族）、`NCMAP019 / 042`（P3 检测器）。

## 9. 深入阅读

- [declarative/basics](../../how-to/declarative/basics.md)（操作向：异名/转换/方向的用法与常见误用）
- [MapListInto 批量就地映射](../../how-to/mapping/map-list-into.md)（集合复用场景操作向）
- [反向映射语义](../reverse-mapping-semantics.md)（反向判定与写回路径）
- [api-comparison 对照页](../../reference/api-comparison.md)（选型）
