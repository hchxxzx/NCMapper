# NCMapper Samples — 演示项目

NCMapper 的完整演示项目，按特性域组织（6 个场景域 + 1 个 AOT 占位域 / 23 个场景），场景号为三位数域编（域号×100+序号）。每个场景为独立可运行单元，可通过交互式菜单（域→场景二级结构，支持输入 `all` 一次运行全部场景）或 `--scenario <id>` 运行。

## 运行方式

```bash
cd samples/NCMapper.Samples
dotnet run                    # 交互式菜单（域→场景二级结构；输入 all 一次运行全部场景）
dotnet run -- --scenario 101  # 运行指定场景
dotnet run -- --all           # 运行全部场景
```

- 退出码契约：0=成功、1=场景内验证失败（Error Summary）、2=非法场景号（注册表未命中，含区间内未注册号）。
- 预期失败演示：部分场景故意触发异常以展示库的错误报告能力，此类输出带 `note : Expected demo — the exception below is triggered on purpose, not a program error` 标注行（`OutputHelper.DumpExpectedFailure`），不代表程序出错。
- 展示语言：demo 输出文案、库运行时异常消息与验证器消息（MapValidator/源生成器）均随入口文化（`CurrentUICulture=en-US` 为英文、zh-CN 为中文）；源生成器写进生成代码的验证消息固定英文（编译期固化，不随运行时文化）。脱敏等场景的中文演示数据（姓名/地址/车牌）为功能演示对象，有意保留。
- 场景注册表：`ScenarioRegistry.cs`（静态注册，编译期固定；新增场景在对应域登记一行即可）。
- 批量驱动脚本：`scripts/run-demo-all.sh`（仓库根执行）。

## 场景域目录

| 域 | 场景号 | 场景 | 说明 |
|----|--------|------|------|
| [01-Fluent](./01-Fluent/) | 101-109 | Fluent 全家桶 | 基础链/转换/跨源/钩子/守卫/构造器/多态/内联转录/运行时元数据合成 |
| [02-Declarative](./02-Declarative/) | 201-202 | Basic Mapping (Attribute) | Map/MapList/Action/Property/Convert/SourceGen/JSON 字段/索引路径/MapFormat 属性级格式化 |
| [03-Compose](./03-Compose/) | 301 | Multi-Source Compose | Compose/MultiSourceCompose/ComposeList/反向直调/跨表转换 |
| [04-DeepCopy](./04-DeepCopy/) | 401 | Deep Copy | 三策略（Fast/ExpressionTree/Json）/集合深拷贝/深度限制 |
| [05-Orm](./05-Orm/) | 501-503 | ORM 投影 | FreeSql+EFCore 双 ORM、Dapper、多表与高级终端 |
| [06-Infrastructure](./06-Infrastructure/) | 601-607 | 基建 | Pipeline/校验/DI/性能/脱敏/全局配置/追踪 |
| [06-MapFormat](./06-MapFormat/) | — | MapFormat 源码目录 | 场景 202 注册于 02-Declarative 段（2xx 号段），源码位于本目录 |
| [07-Aot](./07-Aot/) | — | 占位域 | AOT 运行演示见 `samples/NCMapper.AotSample/` |

## 逐场景说明

### 01-Fluent 域

**101 Fluent Basics** — `dotnet run -- --scenario 101`
覆盖：链式基础（异名 `Map`/嵌套路径/`Ignore`/`Mask` 脱敏/`AfterMapping` 汇总）、`MapTree` 递归、`NoAutoInclude`、`Tree()` 自定义三元组、索引段路径。
预期输出：各子场景 JSON 结果，脱敏字段以 `****` 形态出现（不落明文）。

**102 Fluent Convert** — `dotnet run -- --scenario 102`
覆盖：`Convert`/`ConvertBack` 四形态模板、配对声明、`overrideIgnore` 豁免门、`FromJson`、实体侧锚点族。
预期输出：转换前后对照与豁免门救回演示。

**103 Fluent FromSource** — `dotnet run -- --scenario 103`
覆盖：跨源绑定速记与分组块、块内 `Mask`/`FromJson`、顺序覆盖、双面向限制。
预期输出：跨源字段取值结果。

**104 Fluent Hooks** — `dotnet run -- --scenario 104`
覆盖：`BeforeMapping`/`AfterMapping` 双侧（Fluent + 类级特性）、执行相位序、跨对签名过滤。
预期输出：钩子执行轨迹与相位顺序。

**105 MapWhen** — `dotnet run -- --scenario 105`
覆盖：条件守卫 A-T1/B-T1/T2/S-6 四形态同链、特性侧 `[MapWhen]`。
预期输出：守卫真/假路径的映射差异。

**106 MapCtorParam** — `dotnet run -- --scenario 106`
覆盖：显式构造器形参绑定双形态、record 边界澄清、`MapList` 元素级。
预期输出：带参构造目标实例的绑定结果。

**107 MapSubclass** — `dotnet run -- --scenario 107`
覆盖：多态子类型分派四族、基类兜底、反向子类对、特性侧 `[MapSubclass]`。
预期输出：按源实例实际类型分派的映射结果。

**108 内联 lambda 转录运行对比** — `dotnet run -- --scenario 108`
覆盖：无捕获内联 lambda 放行（转录进生成代码，真/假路径对照）；捕获场景提取静态方法组后生效（F41/F42 编译期拒绝形态由文档与注释承载）。
预期输出：内联谓词与方法组谓词的运行产物对照。

**109 Compose 运行时元数据（RuntimeComposeMapping）** — `dotnet run -- --scenario 109`
覆盖：新语法叠加组合三形态同链（跨型 `From`：int→string；`From+Format+Mask`：FormatThenMask 先格式化再脱敏；`From+Mask+MapWhen`：条件→脱敏→赋值三重叠加）；Compose 跨源声明（速记 + 分组块）在运行时经 `RuntimeSnapshotStore.TryGetFluentDetailMeta` 可读（`ComposeMappings` 明细逐项断言：目标属性/子源类型/源路径/Format/Mask 槽位）；负向探针（未声明的目标属性应查不到，防恒真断言）。
预期输出：主源面、MapWhen 假守卫跳过赋值、Compose 合成面三段 JSON，以及运行时元数据明细（3 行 ComposeMappings 关键字段对照）。

### 02-Declarative 域

**201 Basic Mapping（Attribute）** — `dotnet run -- --scenario 201`
覆盖：同名匹配、`MapList`、`MapListInto` 复用、回调、更新已有对象、`[MapProperty]` 异名、`[Convert]`/`[ConvertBack]` 往返、反向直调六重载、点路径扁平化、JSON 字段双向、枚举语义、索引路径。
预期输出：各子场景 JSON 结果（含 ListMapInto 复用循环与越界槽位保持演示）。

**202 MapFormat（Attribute & Fluent）** — `dotnet run -- --scenario 202`（源码位于 `06-MapFormat/` 目录）
覆盖：`[MapFormat]` 特性通道与 `.Member(dest).Format` Fluent 通道双形态属性级格式化（日期/数值/枚举/时长）、可空源 null 语义（null 原样透传）、`FormatThenMask` 组合（先格式化再脱敏）、文化口径（en-US 固定文化断言 + de-DE 文化分叉演示——未标注属性恒 Invariant）。
预期输出：特性/Fluent 两通道格式化 JSON 对照与 de-DE 文化分叉输出（同值异形，如 N2 千分位 `1,234.50` vs `1.234,50`）。

### 03-Compose 域

**301 Multi-Source Compose** — `dotnet run -- --scenario 301`
覆盖：`Mapper.Compose` 双源/三源/批量/回调、反向直调、跨表转换。
预期输出：多源合成 DTO 结果（字段来源跨对象聚合）。

### 04-DeepCopy 域

**401 Deep Copy** — `dotnet run -- --scenario 401`
覆盖：三策略（Fast/ExpressionTree/Json）同类型复制、回调、集合深拷贝、`MaxRecursionDepth` 深度限制。
预期输出：各策略复制结果与深度保护行为。

### 05-Orm 域

**501 ORM 投影（FreeSql/EFCore）** — `dotnet run -- --scenario 501`
覆盖：FreeSql 单表/分页/分批/多表 Compose/Include 导航/WithSql/GroupBy 守护/树形；EFCore 单表/分批/分组/嵌套跨表/索引路径投影；Fluent DTO 双态（纯可翻译投影 + Mask 强制降级）；`ToTreeList` 树形投影（特性通道 + Fluent `Tree()`）。
预期输出：投影 JSON 结果与降级 Trace；树形片段输出 Root→子→孙嵌套结构。

**502 Dapper 集成** — `dotnet run -- --scenario 502`
覆盖：`Query` 系列、`ToPagedList`、`ToDictionary`、`ToLookup`、`ToChunkDto`、`ToTreeList`、流式枚举、`QueryMultiple`、异步族。
预期输出：各终端产物与分页元数据。

**503 FreeSql 多表与高级终端** — `dotnet run -- --scenario 503`
覆盖：双表 JOIN 投影（跨表字段取数）、`IncludeMany` 一对多导航集合投影、`ToChunkDtoAsync` 异步分批（OrderBy 稳定排序前提）、`ToLookup` 分组、Fluent DTO 投影。全部内存 SQLite 形态。
预期输出：JOIN 结果 4 行、嵌套集合非空、分批 2 批共 4 条、分组 2 组。

### 06-Infrastructure 域

**601 Pipeline** — `dotnet run -- --scenario 601`
覆盖：PostMap 钩子、审计管道、洋葱模型。
预期输出：管道执行轨迹。

**602 Validation & Diagnostics** — `dotnet run -- --scenario 602`
覆盖：`Mapper.Validate()` 程序集扫描、`ValidateOrThrow` 启动期校验、异常诊断。
预期输出：OK/Warn/Error 分级报告。

**603 DI 集成** — `dotnet run -- --scenario 603`
覆盖：`AddNCMapper` 服务注册与 `IMapper` 注入。
预期输出：容器解析产物。

**604 Performance** — `dotnet run -- --scenario 604`
覆盖：Metrics 计数、吞吐对比、双引擎对比、冷启动、`MapListInto` 吞吐。
预期输出：性能计数与耗时数据。

**605 Data Masking** — `dotnet run -- --scenario 605`
覆盖：`[Mask]` 特性脱敏、9 个内置实现原值→脱敏值对照（假脱敏断言：输出必须变化且含掩码符）、自定义 `IMask` 注册与恢复（Provider 层）、`OverrideMask` 后效边界实证。
预期输出：9 行脱敏对照（全部假数据）与注册/恢复轨迹。

**606 Global Config** — `dotnet run -- --scenario 606`
覆盖：`[assembly: MapDefaults]` 迁移对照（逐属性标注 vs 全局配置）、SnakeCase 命名策略匹配、`IgnoreNames` 全局忽略、Fluent 豁免门（`overrideIgnore` 救回被忽略字段）、F7 方向性忽略。
预期输出：两路径一致性校验与冻结配置视图。

**607 映射追踪** — `dotnet run -- --scenario 607`
覆盖：全局 `TraceEnabled` + `LogCallback` 全流程（开启→捕获→断言→恢复）；按调用 `MapTracer.Trace` 强制分析报告（direct/nested/convert/skipped 四类映射方式标记全覆盖断言）。
预期输出：捕获到的 Trace 行与四类标记齐备的分析报告。

## 模型定义

跨域共享模型在 [Models/Common/](./Models/Common/)（namespace 保持 `NCMapper.Samples.Models.*`）：

| 目录 | 文件 | 说明 |
|------|------|------|
| [Entities/](./Models/Common/Entities/) | UserEntity, OrderEntity, CustomerEntity, ProductEntity, IncludeManyEntities | 实体类（含嵌套 Address、枚举字段） |
| [Dtos/](./Models/Common/Dtos/) | UserDto, UserDetailDto, OrderDetailDto, CustomerDto, ProductDto, ReportDto, IncludeManyDtos | DTO 类（标注 [MapFrom]/[MapProperty]/[MapIgnore]/[Convert]） |
| [Converters/](./Models/Common/Converters/) | DateTimeFormatter, PhoneMaskConverter, FullNameConverter | 静态转换方法类（[Convert] 引用） |
| [Enums/](./Models/Common/Enums/) | UserStatus, OrderStatus, CustomerLevel | 枚举定义 |

域私有模型位于各域目录内（如 01-Fluent/FluentModels.cs、05-Orm/MultiTableModels.cs、02-Declarative/JsonFieldModels.cs 等），namespace 为 `NCMapper.Samples.<域>`。

## 功能覆盖总览

> 逐 API 的覆盖对照见 [公开 API 架构总览](../../docs/zh-CN/reference/architecture-map.md)（分层架构图 + 演示覆盖对账清单，脚本 `check-api-demo-coverage.sh` 自动核对）。

| 功能点 | 场景号 |
|--------|--------|
| Fluent 链式声明全家桶 | 101-109 |
| 特性通道基础与进阶 | 201-202 |
| 多源合成 | 301 |
| 深拷贝三策略 | 401 |
| ORM 投影（FreeSql/EFCore/Dapper）与降级 | 501-503 |
| 后置管道 | 601 |
| 映射校验与诊断 | 602 |
| DI 集成 | 603 |
| 性能指标 | 604 |
| 数据脱敏（特性 + Provider） | 605 |
| 全局配置与豁免门 | 606 |
| 映射追踪 | 607 |

## 技术文档对照

场景与文档的映射关系见技术文档站 `reference/samples-index.md`（docs↔samples 唯一映射表）。技术文档入口：`docs/zh-CN/index.md`。
