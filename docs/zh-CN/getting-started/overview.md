---
wiki-id: getting-started/overview
涉及诊断码: NCMAP001, NCMAP002, NCMAP_F11
库版本: v1.0.0+
更新时间: 2026-09-30
---

# NCMapper 是什么

> 本篇是文档站的第一站，回答"它是什么、解决什么问题、有哪些能力、该装哪个包、下一步去哪"。装包与版本选择见 [安装与包选型](installation.md)，15 分钟上手见 [快速开始](quickstart.md)。

## 一句话

NCMapper 是 .NET 的高性能对象映射库：用 Fluent 配置或特性标注描述"源类型到目标类型"的关系，映射过程交给库自动完成，替代手写的实体与 DTO 之间的逐字段赋值代码。

理解它只需三个关键词：

| 关键词 | 含义 |
| ------ | ---- |
| 双引擎 | 源生成器 + 表达式树两套执行装置：声明过的类型编译期直接出代码，没声明的运行时自动补上 |
| 双通道 | Fluent 链式、特性标注两种声明写法，能力等价，团队按喜好选 |
| 自适应 | 你只管声明（甚至不声明），走哪个引擎、要不要降级，系统自动判断、自动兜底 |

**规则怎么写你说了算，映射怎么走库说了算。**

## 它解决什么问题

- **样板代码**：分层架构里 Controller、Service、Repository 之间反复出现的实体与 DTO 手工赋值，一条 `[MapFrom]` 即可替代。
- **静默失败**：手工赋值时改了属性名或类型，编译器不会拦你，只会在运行时悄悄丢值。NCMapper 的映射规则在编译期解析，属性名/类型不匹配会直接得到编译期诊断（`NCMAP` 系列诊断码）。
- **配置重复**：同一组映射常常在内存映射、ORM 投影、深拷贝各写一份。声明式规则只写一次。

## 两条声明通道，先选一条

同一套能力有两条声明通道。按团队风格任选其一，两通道能力等价、长期并存：不同类型对可各自选通道混用；同一对类型（无论哪个方向）只能由一条通道声明一次。

| 维度 | Fluent 通道 | 特性通道 |
| ---- | ----------- | -------- |
| 写法 | 在 `[MapperConfig]` 配置类里用 `FluentMappingConfig` 链式调用 | 在 DTO 类上标注 `[MapFrom]` / `[MapProperty]` 等特性 |
| 规则落点 | 集中的配置类（DTO 保持干净） | DTO 自身（自描述） |
| 顺序依赖 | 支持（链式调用天然有序） | 不支持 |
| 适合 | 希望映射意图集中、不污染 DTO | 偏好"标在类上即所见即所得"（AOT 场景两通道同优，见下文双引擎节） |

两条通道对**同一个类型对**只能声明一次（无论哪个方向，`For<B, A>` 与 `For<A, B>`、`For<S, T>()` 与 `[MapFrom]` 均视为同一对），两侧同时声明会报 `NCMAP_F11`（无序互指由 `NCMAP025` 同族把守）。迁移时记得删掉另一侧声明。

选型细表（30 秒选型、逐维度等价关系、迁移指引）见 [双 API 面对照](../reference/api-comparison.md)；特性全表见 [Attribute 速查表](../reference/attributes-index.md)。

## 双引擎：路径怎么选

| 引擎 | 何时生效 | 特征 |
| ---- | -------- | ---- |
| 编译期源生成器 | 类型对声明了映射（Fluent 配置类 `For<S,T>()` 声明**或** `[MapFrom]` 标注，两通道同为编译期生成），Roslyn 增量生成器在编译期产出映射代码 | 零首次延迟；经 `[ModuleInitializer]` 自动注册，无需手动调用；同时给出编译期诊断 |
| 运行时表达式树 | 未命中源生成路径时兜底 | 首次调用编译为委托并缓存；泛型静态缓存避免字典查找 |

`Mapper.Map` 内部优先使用源生成委托，未命中才回退表达式树，两条路径的性能处于同一量级。源生成器已内置在 `NCMapper` 包中，无需额外安装。

## 一条映射的旅程

以 `Mapper.Map<UserEntity, UserDto>(src)` 为例，一次调用的完整路径：

1. **声明**：在 Fluent 配置类声明 `For<UserEntity, UserDto>()`（或在 `UserDto` 上标注 `[MapFrom(typeof(UserEntity))]`，两通道等价）；异名字段再用 Fluent `.Member(d => d.X).From(s => s.Y)` 或 `[MapProperty]` 补充。
2. **编译期提取**：源生成器读取声明并产出映射代码，校验规则，发现问题即报 `NCMAP` 诊断码。
3. **注册**：生成代码经 `[ModuleInitializer]` 把映射委托注册进 `Mapper` 的缓存，你不需要手写任何注册调用。
4. **首次调用**：`Mapper.Map` 命中已注册的源生成委托并直接执行。
5. **复用**：委托按类型对缓存；泛型静态缓存让后续同类调用不再走字典查找。
6. **兜底**：类型对没有 `[MapFrom]` 时缓存未命中，回退运行时表达式树，首次编译成委托后再缓存。

由此得到一条实践经验：**有映射声明的类型对**（Fluent `For<S,T>()` 声明或 `[MapFrom]` 标注，两通道同为编译期生成、运行时零差异）才是性能与 AOT 安全的主路径；未声明的类型对仍能工作，只是落在运行时路径上，这一点在裁剪与 AOT 环境下需要留意（见 [AOT / NativeAOT 部署](../how-to/aot/deployment.md)）。

## 诊断码怎么守护你

映射规则在编译期解析，手误会在编译期拿到信号，而不是运行时静默丢值。诊断码分两族：Fluent 侧 `NCMAP_Fxx`，特性侧 `NCMAP0xx`；严重度分 Error、Warn、Info 三级。

| 你在写什么 | 触发 | 你得到什么 |
| ---------- | ---- | ---------- |
| `[MapProperty]` 指定的字段在源类型里不存在 | `NCMAP001`（Warn） | 编译期指出字段不存在 |
| 把源字段映射到类型不兼容的目标属性 | `NCMAP002`（Warn） | 编译期指出类型不兼容 |
| 同一类型对既在 Fluent 侧声明又标 `[MapFrom]` | `NCMAP_F11`（Error） | 编译期报双通道声明冲突 |

全量码表与严重度见 [诊断规则](../diagnostics/rules.md)。

## 能力全景

| 能力 | 引擎 | 一句话说明 | 深入 |
| ---- | ---- | ---------- | ---- |
| 单对象映射 | 双引擎 | `Mapper.Map<TSource, TTarget>(src)`，自动同名匹配（忽略大小写并支持扁平化） | [declarative/basics](../how-to/declarative/basics.md) |
| 列表映射 | 双引擎 | `Mapper.MapList<TSource, TTarget>(srcs)` 批量映射 | [declarative/basics](../how-to/declarative/basics.md) |
| 就地批量映射 | 双引擎 | `Mapper.MapListInto(srcs, dtos)` 复用预分配目标数组，适合高频刷新 | [MapListInto](../how-to/mapping/map-list-into.md) |
| 更新既有实例 | 双引擎 | `Mapper.Map(src, dst)` 只覆盖被映射的字段 | [declarative/basics](../how-to/declarative/basics.md) |
| 回调 | 双引擎 | `Mapper.Map(src, (s, t) => ...)`；类级钩子 `[BeforeMapping]` / `[AfterMapping]` | [fluent/hooks](../how-to/fluent/hooks.md) |
| 多源合成 | 双引擎 | `Mapper.Compose<T>(sources...)` 从多个源对象合成一个 DTO，源数量不设上限 | [compose](../how-to/compose/compose.md) |
| 深拷贝 | 双引擎 | `Mapper.DeepCopy<T>(src)`，三层策略（源生成器、Fast、表达式树） | [deep-copy](../how-to/deep-copy/deep-copy.md) |
| ORM 投影 | 运行时 | 三个扩展包提供 `AsDto()`，一条 SQL 直接产出 DTO，不可翻译特性自动降级为二步映射 | [FreeSql 单表](../how-to/orm/freesql-single-table.md) |
| 分页与树形投影 | 运行时 | `ToPagedList` 分页；`ToTreeList` 配合 `[MapTree]` 把平铺结果组装成树 | [Dapper Query 系列](../how-to/orm/dapper-query.md) |
| 后置管道 | 运行时 | `Mapper.Pipeline` 注册 PostMap 钩子，空管道零开销 | [pipeline](../how-to/infrastructure/pipeline.md) |
| 脱敏 | 编译时 | `[Mask]` 属性级标注，内置 9 个实现（手机号、身份证、银行卡、邮箱等） | [masking](../how-to/infrastructure/masking.md) |
| 自定义转换 | 双引擎 | `[Convert]` 引用静态转换方法；反向转换需用 `[ConvertBack]` 显式开启 | [declarative/basics](../how-to/declarative/basics.md) |
| 属性级格式化 | 双引擎 | `[MapFormat("格式串")]` / `.Member(d => d.X).Format(format)` 把源 `IFormattable`（日期/数值/枚举等）按格式串输出为文本；反向不赋值，往返需补 `[ConvertBack]` | [declarative/map-format](../how-to/declarative/map-format.md) |
| 条件守卫 | 双引擎 | `.MapWhen` / `[MapWhen]`，谓词为真才执行该属性的映射 | [declarative/advanced](../how-to/declarative/advanced.md) |
| 多态子类型 | 双引擎 | `.MapSubclass` / `[MapSubclass]`，按源实例实际类型做 isinst 分派 | [declarative/advanced](../how-to/declarative/advanced.md) |
| 方向性忽略 | 双引擎 | `.Member(d => d.X).Ignore(direction)` / `[MapIgnore(Direction)]` 控制映射方向 | [Attribute 速查表](../reference/attributes-index.md) |
| JSON 列映射 | 双引擎 | `[MapProperty(IsFromJson = true)]` 实现字符串与对象双向映射，投影侧需降级 | [declarative/basics](../how-to/declarative/basics.md) |
| 全局默认配置 | 编译时 | `[assembly: MapDefaults]` 一次声明命名策略与忽略清单 | [global-config](../how-to/infrastructure/global-config.md) |
| 映射校验 | 运行时 | `Mapper.Validate()` 扫描程序集并给出 OK / Warn / Error 报告 | [validation](../how-to/infrastructure/validation.md) |
| 诊断追踪 | 运行时 | `Mapper.TraceEnabled` 与 `MapTracer.Enable()` 可视化映射过程 | [validation](../how-to/infrastructure/validation.md) |
| 性能指标 | 运行时 | `Mapper.MetricsEnabled` 计数调用与命中，`Mapper.ResetMetrics()` 归零 | [performance](../how-to/infrastructure/performance.md) |
| DI 集成 | 运行时 | `AddNCMapper()` 注入 `IMapper`，支持 Options 模式配置 | [di](../how-to/infrastructure/di.md) |
| AOT 兼容 | 双引擎 | 源生成器路径 AOT 友好；运行时路径标注 `[RequiresDynamicCode]` 供裁剪分析 | [AOT 部署](../how-to/aot/deployment.md) |
| 编译期诊断 | 编译时 | Fluent 侧 `NCMAP_Fxx` 与特性侧 `NCMAP0xx` 两族诊断码守护映射正确性 | [diagnostics/rules](../diagnostics/rules.md) |

## 装哪个包

核心包不引用任何 ORM，ORM 能力按需安装扩展包：

| 包 | 用途 |
| -- | ---- |
| `NCMapper` | 核心库，**源生成器已内置**，无需单独安装 |
| `NCMapper.FreeSql` | FreeSql 投影扩展（`AsDto()` 终端操作与自动降级） |
| `NCMapper.EFCore` | EF Core 投影扩展 |
| `NCMapper.Dapper` | Dapper 集成（`AsDto` 统一入口、SQL 级分页与方言适配） |

```bash
dotnet add package NCMapper
```

包矩阵与目标框架细节见 [安装与包选型](installation.md)。

## 工程与技术栈

仓库分三层：核心与源生成器、三个 ORM 扩展包、示例与测试。

| 项目 | 目标框架 | 说明 |
| ---- | -------- | ---- |
| `NCMapper` | net8.0 / net9.0 / net10.0 | 核心库，映射本体依赖 BCL 与 `System.Linq.Expressions`；另引 `Microsoft.Extensions.*` 抽象包（DI 与配置） |
| `NCMapper.SourceGen` | netstandard2.0 | Roslyn 增量生成器，随核心包以 analyzer 资产分发 |
| `NCMapper.FreeSql` | net8.0 / net9.0 / net10.0 | FreeSql 投影扩展 |
| `NCMapper.EFCore` | net8.0 / net9.0 / net10.0 | EF Core 投影扩展 |
| `NCMapper.Dapper` | net8.0 / net9.0 / net10.0 | Dapper 集成与 SQL 方言适配 |
| `samples/NCMapper.Samples` | net10.0 | 交互式示例工程，18 个场景 |
| `samples/NCMapper.AotSample` | net10.0 | 最小 AOT 部署示例（`PublishAot`） |
| `test/`（六个测试工程） | — | 单元、源生成器输出、集成、模糊与 AOT 编译兼容验证 |

## 最小可用示例

一个实体到 DTO 的映射，包含单对象映射、列表映射、就地批量映射与回调四个入口：

[!code-csharp[基础映射：单对象（同名自动匹配）](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

这段示例覆盖四个入口：`SimpleNameMatch` 是最基础的同名属性映射，`ListMap` 批量映射列表，`ListMapInto` 复用预分配数组做就地映射，`MapWithAction` 在映射完成后立即回调修改目标。每个静态方法对应一个子场景，返回结果供调度层统一输出。

完整可运行示例位于 `samples/NCMapper.Samples/`：

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 201
```

## 性能取向

仓库自带 BenchmarkDotNet 基准（见 `benchmarks/`），平铺映射（9 个同名属性）的实测均值（2026-10-07 Linux 全量跑批，Intel Xeon Silver 4314 / Ubuntu 24.04.5，net10.0）：源生成扩展方法约 20.4 ns，`Mapper.Map` 约 23.9 ns，手写赋值约 20.0 ns。源生成路径的扩展方法可被 JIT 内联，与手写赋值处于同一量级。注意性能数值随测试机 CPU / OS 差异较大，完整数据见[性能优化机制 §14.6](../explanation/performance.md)。

选型与调优建议（引擎对比、热路径优化、指标读取）见 [性能与最佳实践](../how-to/infrastructure/performance.md)。

## 定位与取舍

| 维度 | NCMapper 的做法 | 这样设计的原因 |
| ---- | --------------- | -------------- |
| 依赖面 | 映射本体只用 BCL 与 `System.Linq.Expressions`；依赖面收窄到 `Microsoft.Extensions.*` 抽象包（DI 与配置） | 减小版本冲突面，ORM 能力做成可选扩展包，核心不引用任何 ORM |
| 执行路径 | 编译期优先，运行时兜底 | 让标注了 `[MapFrom]` 的类型获得零首次延迟与编译期诊断，未标注类型仍可运行 |
| 错误暴露 | 映射规则在编译期解析，诊断码分级（Error / Warn / Info） | 属性名/类型不匹配显性报错 |
| 声明位置 | 两条通道并存，同一类型对互斥 | 允许团队按风格选择，同时避免同一对出现两份互相矛盾的规则 |
| 覆盖范围 | 内存映射、ORM 投影、深拷贝、多源合成共用同一套规则 | 避免同一映射在多处重复配置而彼此漂移 |

## 什么场景不适合

以下边界来自 [限制与注意事项](../explanation/limitations.md)：

- **运行时路径**：不支持 `dynamic` 类型映射，不支持 `ref struct` 与 `Span<T>`，循环引用检测上限 64 层。
- **源生成器路径**：只处理标注了 `[MapFrom]` 的 class（不处理 struct 与 record struct）；跨程序集映射要求目标程序集也引用源生成器；跨类型字段须在同一编译单元可见。
- **ORM 投影**：`[Convert]`、`[Mask]`、`IsFromJson`、管道 PostMap 命中即自动降级为二步映射（内存映射）；集合只有 `List<T>` 与可赋值接口可直接投影，`T[]`、`HashSet<T>` 会降级；带参数的 record 嵌套类型也会降级。

## 三条常见路径

| 你的处境 | 建议起点 |
| -------- | -------- |
| 第一次接触，想先跑通 | [快速开始](quickstart.md) 跑通单对象映射，再用 [15 分钟上手教程](tutorial-15min.md) 走完从定义到校验的全流程 |
| 已在用其他映射库，准备迁移 | 先看 [双 API 面对照](../reference/api-comparison.md) 确认声明方式，再按 [从其他映射库迁移](migrating-from-other-mappers.md) 逐项替换 |
| 要上 AOT 或裁剪部署 | 让全部 DTO 标注 `[MapFrom]` 走源生成路径，发布与边界见 [AOT / NativeAOT 部署](../how-to/aot/deployment.md) |

## 小结速查

| 你想做的事 | API 或特性 | 去哪里 |
| ---------- | ---------- | ------ |
| 了解怎么装、装哪个 | `dotnet add package NCMapper` | [安装与包选型](installation.md) |
| 跑通第一个映射 | `[MapFrom]` + `Mapper.Map` | [快速开始](quickstart.md) |
| 端到端演练一遍 | 从定义实体到校验的完整流程 | [15 分钟上手教程](tutorial-15min.md) |
| 在两条通道间做选择 | 见选型细表 | [双 API 面对照](../reference/api-comparison.md) |
| 查全部特性 | 17 个特性一览 | [Attribute 速查表](../reference/attributes-index.md) |
| 查诊断码含义 | `NCMAP0xx` / `NCMAP_Fxx` | [诊断规则](../diagnostics/rules.md) |
| 看可运行示例 | 18 个场景 | [samples 场景映射表](../reference/samples-index.md) |

## 进一步阅读

- 快速入门组：[安装与包选型](installation.md) · [快速开始](quickstart.md) · [15 分钟上手教程](tutorial-15min.md)
- 参考面：[双 API 面对照](../reference/api-comparison.md) · [Attribute 速查表](../reference/attributes-index.md) · [核心 API](../reference/core-api.md)
- 原理面：[双引擎架构](../explanation/dual-engine.md) · [限制与注意事项](../explanation/limitations.md)