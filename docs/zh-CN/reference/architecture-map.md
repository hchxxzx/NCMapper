---
title: 公开 API 架构总览
description: 用户公开可调用的 API 分层架构图与演示覆盖对账清单
wiki-id: reference/architecture-map
库版本: v1.0.0+
更新时间: 2026-10-23
---

# 公开 API 架构总览

> 本页是 **用户公开可调用的 API** 的分层架构图与演示覆盖对账清单（单一事实源）。每行带「演示覆盖」列：场景号 + 可运行片段名（region），或显式的未演示理由。
> 反向对账：`scripts/check-api-demo-coverage.sh` 按本页清单自动核对（片段实存 / 未演示统计 / 场景集合相等 / 公开 API 快照反查 / 结构完整性），挂接离线包构建门禁链。
> 只看 Fluent 终端语法细节请转 [Fluent 终端速查表](fluent-index.md)；只看特性语法请转 [Attribute 速查表](attributes-index.md)；可运行演示总索引见 [samples 索引](samples-index.md)。

## 0. 分层架构图

```text
┌─────────────────────────────────────────────────────────────────┐
│ L1 编译期层（SourceGen）—— 不可直接调用，编译期自动生效            │
│    增量生成映射代码 · 诊断码（NCMAP 族）· 反向直调六重载 ·          │
│    [ModuleInitializer] 自动注册（RuntimeSnapshotStore 承载）      │
├─────────────────────────────────────────────────────────────────┤
│ L2 核心运行时 —— Mapper 静态门面（映射/列表/复用/组合/深拷贝/验证） │
├─────────────────────────────────────────────────────────────────┤
│ L3 声明配置层 —— 特性通道（标注在类上）｜Fluent 通道（配置类集中） │
│    ｜全局配置（程序集级 MapDefaults）                              │
├─────────────────────────────────────────────────────────────────┤
│ L4 基建服务层 —— 管线回调｜验证｜追踪｜掩码｜DI 集成｜JSON 扩展点   │
├─────────────────────────────────────────────────────────────────┤
│ L5 ORM 扩展层 —— FreeSql｜EFCore｜Dapper（投影选择器 + 终端族）   │
├─────────────────────────────────────────────────────────────────┤
│ L6 公共模型 —— PagedList｜异常族｜深拷贝上下文｜运行时承载类        │
└─────────────────────────────────────────────────────────────────┘
```

对账口径约定：**「演示」列**写 `场景号`（整场景级，无独立片段）或 `场景号 region 名`（片段级）；**未演示**的行写 `—（理由）`，理由必须显式。

**收行标准**：本清单只收 **public 可调用面**（类型/成员以公开 API 快照为唯一基准，快照与源码不符时以源码实读裁定）；`internal` 类型（如 `MapperBase<TSource,TTarget>`、内置 `SystemTextJsonSerializer`）与不存在的类型不入清单；公开但"用户不直接调"的承载类可收录，但须按真实可见性标注。

---

## 1. L1 编译期层（SourceGen，标注性质）

不参与表格登记（不可直接调用）：Roslyn 增量源生成器按 L3 声明在编译期产出映射代码与**反向直调六重载**，经 `[ModuleInitializer]` 自动注册；诊断码（NCMAP0xx 族 / NCMAP_Fxx 族）全部在编译期报告；运行时承载由 §6 的 Runtime 族与 `GeneratedMapConfig` 承接。

---

## 2. L2 核心运行时（Mapper 静态门面）

| API | 用途 | 演示 | 片段（region） | 文档 |
|-----|------|------|----------------|------|
| `Mapper.Map<TSource, TTarget>(source)` | 单对象映射（新建目标） | 201 | BasicMapping_SimpleNameMatch | [declarative/basics](../how-to/declarative/basics.md) |
| `Mapper.Map<TSource, TTarget>(source, action)` | 单对象映射 + 事后回调 | 201 | BasicMapping_MapWithAction | [declarative/basics](../how-to/declarative/basics.md) |
| `Mapper.Map(source, target)` | 映射到已有对象（更新形态） | 201 | BasicMapping_UpdateExisting | [declarative/basics](../how-to/declarative/basics.md) |
| `Mapper.Map<TSource>(source)` | 同类型投影（无目标类型重载） | —（与 Map<TSource,TTarget> 同路径的冷门重载，演示无增益） | — | [核心 API](core-api.md) |
| `Mapper.MapList<TSource, TTarget>(sources)` | 列表映射（另有同类型 `MapList<TTarget>` 重载） | 201 | BasicMapping_ListMap | [declarative/basics](../how-to/declarative/basics.md) |
| `Mapper.MapListInto<TSource, TTarget>(sources, destination)` | 批量写入调用方预分配数组（复用不新建） | 201 | BasicMapping_MapListInto | [MapListInto](../how-to/mapping/map-list-into.md) |
| `Mapper.Compose<TTarget>(sources)` / `(action, sources)` | 多源组合映射 | 301 | —（整场景级） | [compose](../how-to/compose/compose.md) |
| `Mapper.ComposeList<TTarget>(sourceGroups)` / `(action, sourceGroups)` | 批量多源组合（分组源逐行组合） | 301 | Compose_ComposeList | [compose](../how-to/compose/compose.md) |
| `Mapper.DeepCopy<T>(source)` 族 | 深拷贝（5 重载：带上下文/带回调/跨类型） | 401 | DeepCopy_DeepCopySameType 等 | [deep-copy](../how-to/deep-copy/deep-copy.md) |
| `Mapper.DeepCopyRuntimeOnly<T>(source, ctx)` | 强制运行期提供者深拷贝（不走生成路径） | —（特殊场景入口，未演示） | — | [deep-copy](../how-to/deep-copy/deep-copy.md) |
| `Mapper.Validate()` / `Validate(params Assembly[])` | 全量/按程序集映射健康检查 | 602 | Validation_ValidationScenarios | [validation](../how-to/infrastructure/validation.md) |
| `Mapper.ValidateOrThrow()` | 启动时校验，Error 级抛异常 | 602 | —（Validation Run 内联 ValidateOrThrowDemo） | [validation](../how-to/infrastructure/validation.md) |
| `Mapper.ClearCache()` | 清运行时表达式树缓存 | 604 | —（Performance Run 内联冷启动测量） | [核心 API](core-api.md) |
| `Mapper.ClearGenerated()` | 清源生成注册表 | —（与 ClearCache 同族；生成注册常驻无需清理，日常运维无调用场景） | — | [核心 API](core-api.md) |
| `Mapper.ResetMetrics()` | 清性能指标计数 | 604 | —（Performance Run 内联） | [核心 API](core-api.md) |
| `Mapper.MetricsEnabled` / `TotalMapCalls` / `CacheHits` / `CompilationCount` | 性能计数开关与只读指标 | 604 | —（Performance Run 读取指标） | [核心 API](core-api.md) |
| `Mapper.ValidationEnabled` / `TraceEnabled` / `TraceOutput` | 校验/追踪全局开关 | 602 | —（整场景级） | [核心 API](core-api.md)（4.3 运行时控制表） |
| `Mapper.MaxRecursionDepth` / `MaxInlineDepth` | 递归/内联深度设置（上限常量见 `MaxRecursionDepthUpperBound=4096` / `MaxInlineDepthUpperBound=16`） | —（默认值语义见 [deep-copy](../how-to/deep-copy/deep-copy.md)，未单独演示） | — | [deep-copy](../how-to/deep-copy/deep-copy.md) / [核心 API](core-api.md) |
| `Mapper.GetGlobalConfig()` | 读冻结后的全局配置视图 | 606 | GlobalConfig_StrategyAndIgnore | [global-config](../how-to/infrastructure/global-config.md) |
| `Mapper.Pipeline`（MapPipeline 实例） | 全局后映射回调注册 | 601 | Pipeline_PostMapAudit | [pipeline](../how-to/infrastructure/pipeline.md) |
| `Mapper.HasGenerated<S,T>()` / `HasGeneratedDeepCopy<T>()` | 查询该类型对是否有生成代码 | —（诊断辅助，生成代码协作面） | — | [核心 API](core-api.md) |
| `Mapper.RegisterGenerated*` 族 / `RegisterDeepCopyTarget` | 生成代码注册入口（9 个方法名：Core/无后缀/List/Update/ListInto/Compose/Validate/Config/DeepCopy×3；`RegisterGeneratedConfig` 注册全局配置）——由生成器代码自动调用，用户无需手动调用（`EditorBrowsable.Never`） | —（SourceGen 生成代码自动调用，用户不直接调） | — | [核心 API](core-api.md)（4.2 源生成器注册方法，8 方法全列） |

## 3. L3 声明配置层

### 3.1 双通道对照（特性 ↔ Fluent，逐功能点对称）

> 双通道能力等价：下表每个功能点一行，左列特性通道、右列 Fluent 终端，演示列以「特性侧 / Fluent 侧」分别标注。

| 功能点 | 特性通道 ↔ Fluent 终端 | 演示（特性 / Fluent） | 片段（region） | 文档 |
|--------|------------------------|----------------------|----------------|------|
| 通道启用锚 | — ↔ `[MapperConfig]`（`MapperConfigAttribute`）配置类 + `public static` 方法（入参 `FluentMappingConfig`；缺锚整通道不生效） | 201 / 101 | BasicMapping_SimpleNameMatch / —（整场景级） | [fluent/basics](../how-to/fluent/basics.md) |
| 映射声明锚（类型对级生成入口） | `[MapFrom(typeof(源))]` ↔ `For<S, T>()` | 201 / 101 | BasicMapping_SimpleNameMatch / —（整场景级） | [basics](../how-to/declarative/basics.md) |
| 异名路由（含跨表取数） | `[MapProperty]` ↔ `.Map()` | 201 / 101 | —（Run 主体）/ —（整场景级） | [basics](../how-to/declarative/basics.md) |
| 正向自定义转换 | `[Convert]` ↔ `.Convert()` | 201 / 102 | —（Run 主体）/ —（整场景级） | [basics](../how-to/declarative/basics.md) |
| 反向自定义转换 | `[ConvertBack]` ↔ `.ConvertBack()` | 201 / 102 | —（Run 主体）/ —（整场景级） | [basics](../how-to/declarative/basics.md) |
| 属性忽略 | `[MapIgnore]` ↔ `.Ignore()` | 201 / 101 | BasicMapping_MapIgnore / —（整场景级） | [basics](../how-to/declarative/basics.md) |
| 属性脱敏 | `[Mask]` ↔ `.Mask<TMask>()`（`where TMask : IMask`，Member 段内） | 605 / 501 | —（Run 主体）/ —（Run 主体降级片段） | [masking](../how-to/infrastructure/masking.md) |
| 属性级格式化 | `[MapFormat]` ↔ `.Format()`（同锚、源侧按名自动匹配；豁免门两侧同参；成员级演示 = 202 场景） | 202 / 202 | —（Run 主体）/ —（Run 主体） | [declarative/map-format](../how-to/declarative/map-format.md) / [fluent/format](../how-to/fluent/format.md) |
| 条件守卫 | `[MapWhen]` ↔ `.MapWhen()` | 105 / 105（内联 lambda 转录另见 108） | —（整场景级）/ —（整场景级） | [advanced](../how-to/declarative/advanced.md) |
| 树形声明（ToTreeList 消费） | `[MapTree]` ↔ `.Tree()`（子级属性指定用 `.MapTree<TProp>()`） | 502 / 501 | Dapper_Advanced / OrmProjection_FluentTree | [dapper-advanced](../how-to/orm/dapper-advanced.md) |
| 多态子类映射 | `[MapSubclass]` ↔ `.MapSubclass()` | 107 / 107 | —（整场景级）/ —（整场景级） | [declarative/advanced](../how-to/declarative/advanced.md) |
| 禁用自动包含（属性级） | `[MapNoAutoInclude]` ↔ `Member` 段内 `NoAutoInclude`（字段级，无参） | — / 101 | — / —（整场景级） | [attributes-index](attributes-index.md) |
| 禁用自动匹配（对级白名单） | `[MapNoAutoMatch]`（`MapNoAutoMatchAttribute`，类级）↔ `.NoAutoMatch()` | —（特性形态未演示）/ 101 | — / 101-no-auto-include（整场景级） | [fluent/basics](../how-to/fluent/basics.md) |
| 生命周期钩子 | `[BeforeMapping]`/`[AfterMapping]` ↔ `.BeforeMapping()`/`.AfterMapping()` | 201 / 104 | —（Run 主体）/ FluentHooks_Run | [fluent/hooks](../how-to/fluent/hooks.md) |
| 构造器参数绑定 | —（特性通道无此形态）↔ `.MapCtorParam()` | — / 106 | — / —（整场景级） | [fluent-index](fluent-index.md) |
| 多源取数 | `[MapFrom(主源)]` + `[MapProperty(副源, 字段)]` 跨源 ↔ `.FromSource()` | 301 / 103 | —（Run 主体）/ —（整场景级） | [compose](../how-to/compose/compose.md) |
| JSON 列映射（对象 ↔ JSON 串） | `[MapProperty(IsFromJson = true)]` ↔ `.FromJson()` | 201 / 102 | —（Run 主体）/ FluentConvert_FromJson | [basics](../how-to/declarative/basics.md) |
| MapWhen 内联 lambda 转录 | —（无内联形态）↔ `.MapWhen()` 内联 lambda（无捕获放行转录；捕获形态 F41/F42 拒绝） | — / 108 | — / FluentInline_Passthrough / FluentInline_CapturedExtracted | [fluent-index](fluent-index.md) |
| 字段链式配置器族（字段段三段式） | —（特性通道无此形态）↔ `.Member()` + `MemberHead` / `MemberConfig` / `AfterIgnore` 分段承载。字段级一律 Member 三段式，对级开关一律 `.NoAutoMatch()` | — / —（编译期配置面，无独立运行时行为；场景演示与文档刷新归文档册） | — / —（整场景级） | [fluent-index](fluent-index.md) / [fluent/basics](../how-to/fluent/basics.md) |

> **终端宿主与全量速查**：以上 Fluent 终端全部宿主于 `FluentTypeConfig<TSource, TTarget>`，逐终端语法见 [Fluent 终端速查表](fluent-index.md)。

### 3.2 全局与编译期配置

| API | 用途 | 演示 | 片段（region） | 文档 |
|-----|------|------|----------------|------|
| `[assembly: MapDefaults(...)]` | 全局配置（命名策略/前后缀剥离/两级忽略清单；豁免门 `OverrideIgnore` 宿主五特性，606 演示 Fluent 形态） | 606 | GlobalConfig_StrategyAndIgnore / GlobalConfig_FluentInteract | [global-config](../how-to/infrastructure/global-config.md) |
| `[NCMapperOptions]` / `[class: NCMapperOptions(...)]` | **编译期诊断报告开关**：`ReportUnmappedTargetMembers`（默认 false，控 NCMAP001）/ `ReportUnmappedSourceMembers`（默认 true，控 NCMAP007）——与运行时 `NCMapperOptions`（§4）不是一回事 | —（编译期开关，默认值即常规语义，未演示） | — | [attributes-index](attributes-index.md) |

## 4. L4 基建服务层

| API | 用途 | 演示 | 片段（region） | 文档 |
|-----|------|------|----------------|------|
| `MapTracer` / `MapTraceOptions` 族 | 映射追踪（按调用报告/强制分析/全局开关/输出与回调双通道：`Enable()` / `Trace<S,T>()` / `GloballyEnabled` / `GlobalOutput` / `LogCallback`） | 607 | Trace_GlobalEnabled / Trace_PerCallOptions | [tracing](../explanation/tracing.md) |
| `MapValidator.ValidateMapping(Type, Type)` / `MapValidationResult` | 单类型对健康检查（验证消息随 `CurrentUICulture` 双语；生成器写进生成代码的验证消息固定英文） | 602 | Validation_ValidationScenarios | [validation](../how-to/infrastructure/validation.md) |
| `MapValidator.PrintReport(results, output?, showOk?)` | 验证结果格式化报告输出 | 602 | —（整场景级） | [validation](../how-to/infrastructure/validation.md)（PrintReport 用法节） |
| `MapPipeline` | 全局后映射回调管线（`AddPostMap` / `Clear` / `IsPipelineEnabled`；生成代码直调 `RunPostMap<S,T>`） | 601 | Pipeline_PostMapAudit 族 | [pipeline](../how-to/infrastructure/pipeline.md) |
| `MaskProvider.Override<TMask>` / `Resolve<TMask>()` / `Resolve(Type)` / `Clear()` | 掩码实现替换与恢复（Override 须在首次映射调用前，后效边界见 605 演示注释） | 605 | Mask_CustomProvider | [masking](../how-to/infrastructure/masking.md) |
| `IMask` + 9 内置实现 | 内置脱敏（MaskPhone / MaskIdCard / MaskBankCard / MaskEmail / MaskChineseName / MaskAddress / MaskIp / MaskLicensePlate / MaskLandline） | 605 | Mask_BuiltinNine | [masking](../how-to/infrastructure/masking.md) |
| `NCMapperServiceExtensions.AddNCMapper()`（3 重载） | DI 注册（无参/Options 委托/配置文件节） | 603 | DI_ResolveMapper | [di](../how-to/infrastructure/di.md) |
| `NCMapperOptions` | 运行时选项（`DeepCopyStrategy` 三值 Fast/ExpressionTree/Json、`ValidationEnabled` 等） | 603 | —（DI_ResolveMapper 演示 configure 委托） | [di](../how-to/infrastructure/di.md) |
| `IProjectionPolicy`（`RequiresDowngrade`） | 投影降级策略接口（特性作者声明降级行为；未实现则默认降级安全网） | —（特性作者扩展点，引擎消费，无独立演示） | — | [orm-degradation](../explanation/orm-degradation.md) |
| `JsonConverter` / `JsonSerializerBase` | JSON 列映射（IsFromJson）与序列化策略替换（替换静态 `Current` 即换实现；内置 internal） | 201 | —（Run 主体 JSON 列映射演示） | [declarative/basics](../how-to/declarative/basics.md) |
| `TypeConvertHelper` | 内置类型转换判定（公开帮助类） | —（引擎内部调用，用户直接使用场景为自定义转换器实现参考） | — | [核心 API](core-api.md) |

## 5. L5 ORM 扩展层

> 终端清单以三个扩展包源码公开方法面实读为准（2026-09-28 复核）；同步/异步成对终端归组一行，演示列按实际使用的变体标注。

### 5.1 FreeSql（`NCMapper.FreeSql`）

| API | 用途 | 演示 | 片段（region） | 文档 |
|-----|------|------|----------------|------|
| `fsql.Select<T>().AsDto()` | 投影选择器入口（返回 `FreeSqlDtoSelector<TSource>`） | 501 | —（整场景级） | [freesql-single-table](../how-to/orm/freesql-single-table.md) |
| `ToList()` / `ToListAsync()` | 列表投影 | 501 | —（Run 主体） | [freesql-single-table](../how-to/orm/freesql-single-table.md) |
| `First()` / `FirstAsync()`、`Single()` / `SingleAsync()` | 单条投影（Single 附唯一性校验） | 501 | —（Run 主体） | [freesql-single-table](../how-to/orm/freesql-single-table.md) |
| `ToPagedList()` / `ToPagedListAsync()` | 分页投影（返回 PagedList） | 501 | —（Run 主体） | [freesql-single-table](../how-to/orm/freesql-single-table.md) |
| `ToChunkDto()` / `ToChunkDtoAsync()` | 分批消费（OrderBy 稳定排序前提） | 503 | FreeSqlAdvanced_ToChunkDtoAsync | [freesql-advanced](../how-to/orm/freesql-advanced.md) |
| `ToTreeList()` / `ToTreeListAsync()` | 树形投影 | 501 | —（Run 主体） | [freesql-advanced](../how-to/orm/freesql-advanced.md) |
| `ToDictionary()` / `ToDictionaryAsync()` | 字典投影 | 501 | —（整场景级） | [freesql-advanced](../how-to/orm/freesql-advanced.md) |
| `ToLookup()` | 分组查找表 | 503 | FreeSqlAdvanced_ToLookup | [freesql-advanced](../how-to/orm/freesql-advanced.md) |
| 跨表 JOIN 双表投影 | `.LeftJoin(...)` + 跨表 MapProperty 取数 | 503 | FreeSqlMultiTable_TwoTableJoin | [freesql-multi-table](../how-to/orm/freesql-multi-table.md) |
| 原生 `IncludeMany` + 投影 | 一对多导航加载（FreeSql 原生 API，NCMapper 消费导航属性自动展开，需 `[Navigate]`） | 503 | FreeSqlMultiTable_IncludeManyNavigation | [freesql-multi-table](../how-to/orm/freesql-multi-table.md) |
| Include 失败开关 | `NCMapperFreeSqlExtensions`. `ThrowOnIncludeApplicationFailure`（全局开关） | —（运行时开关，默认语义即生产语义，未单独演示） | — | [freesql-multi-table](../how-to/orm/freesql-multi-table.md) |
| Compose 多源投影 | `NCMapperFreeSqlComposeExtensions`：`ISelect<T1, T2>.AsDto()`（双源 `FreeSqlComposeDtoSelector` / 三元 `FreeSqlComposeDtoSelector3`，终端族各 8 个） | 501 | —（整场景级） | —（文档缺） |
| Fluent DTO 投影 | Fluent 注册表承载的 ORM 投影 | 503 | FreeSqlMultiTable_FluentProjection | [freesql-advanced](../how-to/orm/freesql-advanced.md) |
| 编译期守护 | `NCMapperFreeSqlGuardExtensions`：不兼容链路编译即报错并给替代方案（`[Obsolete(error: true)]`） | —（编译期行为，无运行时演示形态） | — | [orm-degradation](../explanation/orm-degradation.md) |
| 降级检测 | SQL 不可翻译自动降级内存映射 | 501 | —（Mask 强制降级片段） | [orm-degradation](../explanation/orm-degradation.md) |

### 5.2 EF Core（`NCMapper.EFCore`）

| API | 用途 | 演示 | 片段（region） | 文档 |
|-----|------|------|----------------|------|
| `db.Set<T>().AsDto()` | 投影选择器入口（默认无跟踪；返回 `EFCoreDtoSelector<TSource>`） | 501 | —（整场景级） | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `AsTracking()` | 跟踪查询模式（需变更跟踪时用） | 501 | —（整场景级） | [efcore-projection](../how-to/orm/efcore-projection.md) |
| 无跟踪开关 | `NCMapperEFCoreExtensions`. `EFCoreProjectionAsNoTrackingDefault`（投影默认无跟踪全局开关） | —（运行时开关，默认 true 即生产语义，未单独演示） | — | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `ToList()` / `ToListAsync()` | 列表投影 | 501 | —（Run 主体） | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `First()` / `FirstAsync()`、`Single()` / `SingleAsync()` | 单条投影（Single 附唯一性校验） | 501 | —（Run 主体） | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `ToPagedList()` / `ToPagedListAsync()` | 分页投影 | 501 | —（整场景级） | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `ToTreeList()` / `ToTreeListAsync()` | 树形投影 | 501 | OrmProjection_TreeList / OrmProjection_FluentTree | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `ToDictionary()` / `ToDictionaryAsync()` | 字典投影 | 501 | —（Run 主体） | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `ToLookup()` | 分组查找表 | 501 | —（Run 主体） | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `ToChunkDto()` / `ToChunkDtoAsync()` | 分批消费 | 501 | —（Run 主体） | [efcore-projection](../how-to/orm/efcore-projection.md) |
| 导航收集与嵌套投影 | 导航属性自动展开（引擎行为） | —（自动行为，文档承载） | — | [efcore-projection](../how-to/orm/efcore-projection.md) |
| 编译期守护 | `NCMapperEFCoreGuardExtensions`：不兼容链路编译即报错并给替代方案 | —（编译期行为，无运行时演示形态） | — | [orm-degradation](../explanation/orm-degradation.md) |
| 降级检测 | SQL 不可翻译自动降级内存映射 | 501 | —（Mask 强制降级片段） | [orm-degradation](../explanation/orm-degradation.md) |

### 5.3 Dapper（`NCMapper.Dapper`）

| API | 用途 | 演示 | 片段（region） | 文档 |
|-----|------|------|----------------|------|
| `connection.AsDto<T>()` | 投影选择器入口（`NCMapperDapperExtensions`；返回 `DapperDtoSelector<TSource>`） | 502 | Dapper_Advanced | [dapper](../how-to/orm/dapper-query.md) |
| Query 族（11 查询终端） | 原生查询投影（`Query` / `QueryFirst` / `QueryFirstOrDefault` / `QuerySingle` / `QuerySingleOrDefault` / `QueryUnbufferedAsync` + ×Async / ×CommandDefinition 重载；`QueryMultiple` 见下行） | 502 | —（Run 主体 + Dapper_Advanced） | [dapper](../how-to/orm/dapper-query.md) |
| `QueryMultiple<T1,T2>()` / `<T1,T2,T3>()` | 一次查询多结果集消费 | 502 | —（整场景级） | [dapper](../how-to/orm/dapper-query.md) |
| `ToDictionary()` / `ToDictionaryAsync()` | 字典投影 | 502 | Dapper_Advanced | [dapper-advanced](../how-to/orm/dapper-advanced.md) |
| `ToLookup()` / `ToLookupAsync()` | 分组查找表 | 502 | Dapper_Advanced | [dapper-advanced](../how-to/orm/dapper-advanced.md) |
| `ToPagedList()` / `ToPagedListAsync()` | 分页投影 | 502 | Dapper_Advanced | [dapper](../how-to/orm/dapper-query.md) |
| `ToChunkDto()` / `ToChunkDtoAsync()` | 分批消费 | 502 | Dapper_Advanced | [dapper-advanced](../how-to/orm/dapper-advanced.md) |
| `ToTreeList()` / `ToTreeListAsync()` | 树形组装 | 502 | Dapper_Advanced | [dapper-advanced](../how-to/orm/dapper-advanced.md) |
| `ISqlDialect` + 6 内置方言 | SQL 方言（按连接自动选择）＋扩展点（`SqlServerDialect` / `MySqlDialect` / `PostgreSqlDialect` / `SqliteDialect` / `OracleDialect` / `Db2Dialect`；`LimitOffsetDialect` 可继承基类；502 LoggingDialect 演示包装形态） | 502 | —（LoggingDialect 在 Run 主体） | [dapper](../how-to/orm/dapper-query.md) |
| `SqlDialectResolver.Resolve(IDbConnection)` | 按连接解析方言（自定义连接包装类型时调用） | 501 | —（整场景级） | [dapper](../how-to/orm/dapper-query.md) |

## 6. L6 公共模型

| API | 用途 | 演示 | 片段（region） | 文档 |
|-----|------|------|----------------|------|
| `PagedList<T>` | 分页结果（`HasPrev`/`HasNext`/`TotalPages`） | 502 | Dapper_Advanced | [dapper-advanced](../how-to/orm/dapper-advanced.md) |
| `IMapper` / `NCMapperMapper` | DI 注入接口与默认实现 | 603 | DI_ResolveMapper | [di](../how-to/infrastructure/di.md) |
| `MapException` / `MapConfigurationException` | 映射异常族 | 602 | —（ValidateOrThrow 与错误路径抛出） | [validation](../how-to/infrastructure/validation.md) |
| `DeepCopyContext` | 深拷贝上下文（循环引用登记/深度控制） | 401 | DeepCopy_MaxRecursionDepth | [deep-copy](../how-to/deep-copy/deep-copy.md) |
| `DeepCopyProviderBase` + 提供者族 | 深拷贝提供者与切换（提供者注入或 `NCMapperOptions.DeepCopyStrategy` 选择：`FastDeepCopyProvider` / `ExpressionTreeDeepCopyProvider`；Json 值经内部 JSON 中转、不支持循环引用） | 401 | DeepCopy_DeepCopyFast / DeepCopy_DeepCopyExpressionTree / DeepCopy_DeepCopyJson | [deep-copy](../how-to/deep-copy/deep-copy.md) |
| `GlobalConfigView` | `Mapper.GetGlobalConfig()` 的返回类型（冻结配置视图） | 606 | GlobalConfig_StrategyAndIgnore | [global-config](../how-to/infrastructure/global-config.md) |
| `GeneratedMapConfig` / `MappingMetaView` | 生成代码注册配置载体 / 运行时元数据视图 | —（生成代码协作面，用户不直接调） | — | —（内部面，不提供用户文档：生成代码协作承载，公开类型仅为生成器发射需要） |
| Runtime 六承载类 | 生成代码运行时承载（`RuntimeSnapshotStore` / `RuntimeSnapshotPair` / `RuntimeMapperMetadata` / `RuntimePropertyMapping` / `RuntimeTargetPropertySlot` / `RuntimeTreeConfig` / `RuntimeMappingChannel`）——**其中 `RuntimeSnapshotStore` 为用户可达读取门面**（`TryGetFluentDetailMeta`，场景 109 实证） | —（其余五类 SourceGen 生成代码自动使用，用户不直接调） | —（109 整场景级实证，无独立片段） | [terminals](../how-to/fluent/terminals.md)（RuntimeSnapshotStore 用法见「From 终端族叠加与运行时元数据」节；其余五类为内部面，不提供用户文档） |
| `RuntimeComposeMapping` | Compose 跨表映射的运行时形态（`ComposeMappings` 明细：`TargetPropertyName` / `SubSourceTypeName` / `SourcePathNames` / `HasMask` / `FormatString` / `HasConverter` / `IsFromJson`）——用户可达读取门面 = `RuntimeSnapshotStore.TryGetFluentDetailMeta(dtoType, out meta)` | 109 | RuntimeCompose_Run | [terminals](../how-to/fluent/terminals.md)（「From 终端族叠加与运行时元数据」节） |
| `ErrorMessages`（public static） | 错误消息中英双语文案（按系统语言自动切换，用户不配置） | —（自动行为，无演示形态） | — | —（内部面，不提供用户文档：自动行为无使用文档可写） |

### 6.1 取值枚举（公开）

> 本节 6 行按公开取值面收录，无独立演示形态（枚举随宿主特性/选项的演示场景间接覆盖），对账脚本计入总行数、不计未演示。

| 枚举 | 用户接触点 | 文档 |
|------|-----------|------|
| `NamingStrategy` | `[MapDefaults(NamingStrategy = ...)]` 命名策略取值 | [global-config](../how-to/infrastructure/global-config.md) |
| `Direction` | `[MapIgnore(Direction.ToEntity)]` 忽略方向取值 | [declarative/basics](../how-to/declarative/basics.md) |
| `DeepCopyStrategy` | `NCMapperOptions.DeepCopyStrategy` 深拷贝策略取值（Fast/ExpressionTree/Json） | [deep-copy](../how-to/deep-copy/deep-copy.md) |
| `MapValidationLevel` | `MapValidator` 校验级别 | [validation](../how-to/infrastructure/validation.md) |
| `DotPathFailureKind` | 点路径失败种类（与失败消息配套） | [dot-path](../how-to/mapping/dot-path.md) |
| `RuntimeMappingChannel` | 运行时通道标识（随快照元数据暴露） | —（内部面，不提供用户文档：内部数据结构承载枚举） |

---

## 覆盖统计

| 分层 | 清单行数 | 已演示 | 未演示（含理由） |
|------|----------|--------|------------------|
| L2 核心运行时 | 22 | 15 | 7（生成协作面/冷门重载/运行期强制路径/开关与深度未单独演示） |
| L3 声明配置 | 21 | 19 | 2（NCMapperOptions 编译期开关；MapNoAutoMatch 特性形态；半覆盖另注） |
| L4 基建服务 | 11 | 8 | 3（PrintReport/IProjectionPolicy/自动行为） |
| L5 ORM 扩展 | 38 | 27 | 11（演示未用 4、编译期守护 2、自动行为 1、开关 2、Compose 投影 1、Resolver 直调 1） |
| L6 公共模型+枚举 | 16 | 13 | 3（承载类/通道枚举/自动行为） |
| **合计** | **108** | **82（76%）** | **26（全部显式理由）** |

> 未演示 26 行全部显式理由（与对账脚本查②逐行对齐）：生成代码协作与承载面 5（ClearGenerated、HasGenerated 族、RegisterGenerated 族、GeneratedMapConfig/MappingMetaView、Runtime 六承载类）、运行时开关与深度未单独演示 4（ValidationEnabled/TraceEnabled、MaxRecursionDepth/MaxInlineDepth、ThrowOnIncludeApplicationFailure、EFCoreProjectionAsNoTrackingDefault）、演示未用 6（FreeSql ToDictionary、EFCore AsTracking 与 ToPagedList、Dapper QueryMultiple、PrintReport、SqlDialectResolver 直接调用）、编译期行为 3（NCMapperOptions 编译期开关、FreeSql/EFCore GuardExtensions 为 `[Obsolete(error)]` 行为无运行时演示形态）、扩展点与引擎内部 2（IProjectionPolicy、TypeConvertHelper）、冷门重载与特殊入口 2（同类型 Map、DeepCopyRuntimeOnly）、自动行为 2（导航收集展开、ErrorMessages 自动文案）、Compose 多源投影 1（唯一可选补演示候选）、特性侧白名单标注 1（`[MapNoAutoMatch]` 特性形态未单独演示——Fluent 侧 `.NoAutoMatch()` 由 101 场景 `101-no-auto-include` 片段覆盖）。双通道对照表半覆盖 1 处（MapNoAutoInclude 特性形态未演示）；「构造器参数绑定」「MapWhen 内联 lambda 转录」两行为特性通道无此形态的功能不对称（非演示缺口），与上述行级理由并行显式标注。

## 深入阅读

- [samples 索引](samples-index.md) — 21 场景总索引（怎么跑/覆盖什么/预期输出）
- [Fluent 终端速查表](fluent-index.md) / [Attribute 速查表](attributes-index.md)
- [核心 API](core-api.md) — 门面 API 细节

> 本页经多轮独立评审与复审修订，并以公开 API 快照全量复核校订；对账以 `scripts/check-api-demo-coverage.sh` 五查为唯一自动化口径。
