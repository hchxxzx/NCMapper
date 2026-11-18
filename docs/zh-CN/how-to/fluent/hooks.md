---
wiki-id: fluent/hooks
涉及诊断码: NCMAP_F12, F14, F35-F39, NCMAP038-040
库版本: Unreleased（随下个版本发布）
更新时间: 2026-09-30
---

# Fluent Hooks：生命周期钩子

> Fluent 线生命周期钩子专题（BeforeMapping/AfterMapping，含特性侧双通道等价）。特性速查见 [attributes-index](../../reference/attributes-index.md)；投影降级机制见 [ORM 降级机制](../../explanation/orm-degradation.md)。

映射流水线的两个生命周期钩子：`BeforeMapping` 在**目标实例构造后、首条属性赋值前**调用（属性初始化器值可见，可被回调预处理）；`AfterMapping` 在**全部属性赋值完成后**调用。Fluent 终端与特性侧 `[BeforeMapping]`/`[AfterMapping]` 填充同一快照承载字段（双通道等价）。

**Fluent 侧语法**：

| 终端                    | 签名                            | 执行时点                                                        |
| --------------------- | ----------------------------- | ----------------------------------------------------------- |
| `BeforeMapping(handler)` | `Action<TSource, TTarget>` 方法组 | 首条属性赋值前（仅正向映射发射；反向 4 形态不发射）                                        |
| `AfterMapping(handler)`  | `Action<TSource, TTarget>` 方法组 | 共享核心方法末尾（6 正向形态继承；反向 4 形态不发射）                                      |

`handler` 必须是**可静态解析的方法组**：目标方法须为 `public` 或 `internal static`，且与生成代码（同程序集）可见；lambda/闭包/捕获变量没有可静态寻址的入口点。

**特性侧语法**（类级标注，`Inherited = false`，派生 DTO 不继承钩子、需显式标注，与 `[MapTree]` 同口径）：

[!code-csharp[特性侧双钩子（双参宿主形态 + \[MapFrom\] 锚定）](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto)]

[!code-csharp[特性侧双钩子（双参宿主形态 + \[MapFrom\] 锚定） · Id](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Id)]

[!code-csharp[特性侧双钩子（双参宿主形态 + \[MapFrom\] 锚定） · Title](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Title)]

[!code-csharp[特性侧双钩子（双参宿主形态 + \[MapFrom\] 锚定） · Qty](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Qty)]

[!code-csharp[特性侧双钩子（双参宿主形态 + \[MapFrom\] 锚定） · NormalizedTitle](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_NormalizedTitle)]

[!code-csharp[特性侧双钩子（双参宿主形态 + \[MapFrom\] 锚定） · Total](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Total)]

- **双参构造** `[BeforeMapping(typeof(HookHost), nameof(HookHost.AttrNormalize))]`：在指定类型上强定位静态回调方法（推荐形态；方法可集中在公共宿主类，跨实体复用，风格与 `[Convert(typeof(X), nameof(X.M))]` 统一）；
- **单参构造** `[BeforeMapping(nameof(Normalize))]`：就近标注，解析域收紧为标注该特性的 DTO 类型自身（含 partial 各部分，不含基类继承与外层嵌套类型）；`nameof(OtherType.M)` 会被截断为方法名，方法声明在 DTO 之外时必须用双参构造，否则报 `NCMAP038`；
- **钩子随 DTO、按签名逐对生效**：方法签名第二参即该对的 DTO 类型（方法签名即适用范围声明）。签名与映射对 `(TSource, TTarget)` 匹配的对发射（含纯 Fluent 声明对，经跨对传播注入）；签名不匹配的对不发射、也不发诊断。

双参构造引用的宿主方法实码（`HookHost` 特性对双钩子，`dotnet run --scenario 104` 实跑验证；钩子写的是 DTO 无源侧同名属性的**派生字段**，映射赋值只碰有源属性，不覆盖钩子产物）：

[!code-csharp[HookHost 特性对双钩子方法（AttrNormalize/AttrComputeTotal）](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_HookHostAttrMethods)]

[!code-csharp[HookHost 特性对双钩子方法（AttrNormalize/AttrComputeTotal） · AttrComputeTotal](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_HookHostAttrMethods_AttrComputeTotal)]

- **`AttrNormalize`（前置钩子）**：目标构造后、首条属性赋值前执行——对 `src.Title` 做 Trim + 大写预处理，写入派生字段 `dst.NormalizedTitle`；
- **`AttrComputeTotal`（后置钩子）**：全部属性赋值完成后执行——按 `src.Qty * src.UnitPrice` 计算派生字段 `dst.Total`。

**要点**：

- **派生字段模式**：钩子写无源侧同名属性的派生字段（如 NormalizedTitle/Total），映射赋值只碰有源条目的属性，不覆盖钩子产物（可运行示例即此模式）；
- **PATCH 语义**：`_Update` 形态（复用既有目标对象）下，`BeforeMapping` 可见 dst 旧值；
- **构造器一体化目标**：经带参/init-only 构造器构造的目标（构造与赋值不可分）无 BeforeMapping 插入点，报 `NCMAP_F37`（双侧同报，见下表）；
- **同类钩子冲突**：同一映射对双通道声明同类钩子（Before×Before / After×After）报 `NCMAP_F39`；交叉组合（特性 After + Fluent Before）不构成冲突。

**诊断**（触发条件与处置）：

| 码 | 触发条件 | 处置 |
| --- | --- | --- |
| `NCMAP_F12` | AfterMapping 目标方法为 `private` | 提升为 `public`/`internal static` |
| `NCMAP_F14` | AfterMapping 目标不是可静态解析的方法组（lambda/闭包/捕获变量） | 提取命名静态方法，改方法组引用 |
| `NCMAP_F35` | BeforeMapping 目标方法为 `private`（F12 的 Before 族同位码） | 同上 |
| `NCMAP_F36` | BeforeMapping 目标不是可静态解析的方法组引用（lambda/闭包/捕获变量） | 同上 |
| `NCMAP_F37` | 目标类型经带参/init-only 构造器构造（构造与赋值不可分，无插入点） | 改用可写属性目标，或移除该钩子 |
| `NCMAP_F38` | FromSource 块内声明 BeforeMapping（生成的 Compose 管线无钩子注入口，声明永不执行） | 在主链声明钩子 |
| `NCMAP_F39` | 同一映射对双通道声明同类钩子（Before×Before / After×After；交叉组合不冲突） | 只在单一通道声明该钩子 |
| `NCMAP038` | 特性侧钩子方法不可解析（锚定类型上不存在可访问静态方法；方法声明在 DTO 之外而未用双参构造） | 改用双参构造形态显式指定宿主类型 |
| `NCMAP039` | 特性侧钩子签名不满足 `void(TSource, TTarget)` 契约（含开放泛型锚定不支持） | 收敛签名；开放泛型宿主改用双参构造传入封闭类型 |
| `NCMAP040` | 特性侧钩子方法存在多个与映射对签名兼容且无精确匹配的可访问重载（歧义） | 收敛为单一方法 |

**投影行为**：声明任一钩子的映射对在 ORM 投影下强制降级（嵌套投影不执行生成代码，钩子丢失即语义错误；分类 `AfterMapping`/`BeforeMapping`，与 `[Mask]`/`[Convert]` 同口径）。详见 [ORM 降级机制 §2.3](../../explanation/orm-degradation.md)。

**可运行示例**（`dotnet run --scenario 104` 实跑验证；Fluent 双钩子链 + 特性侧双钩子 + 跨对签名过滤）：

[!code-csharp[Fluent 双钩子链（Before + After 方法组连写）](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_HookFluentMapper)]

[!code-csharp[Fluent 双钩子链（Before + After 方法组连写） · Configure](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_HookFluentMapper_Configure)]

场景输出校验执行相位 Before→赋值→After、派生字段产物、映射属性原样，以及 `_Update` 形态 Before 见 dst 旧值：

[!code-csharp[钩子相位断言（场景 104 四个子场景）](~/samples/NCMapper.Samples/01-Fluent/FluentHooksScenarios.cs#FluentHooks_Run)]
