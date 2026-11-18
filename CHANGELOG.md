# Changelog

All notable changes to NCMapper will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### 架构

- **投影消费面快照化（架构演进，非行为变更）**：ORM 投影（`AsDto`）、降级检测与内存映射管道现统一消费同一份编译期发射的运行时快照——源生成器把 `[MapFrom]` / Fluent 声明发射为 `RuntimeSnapshotStore` 注册元数据（身份段 + 属性明细段 + `ComposeMappings` 跨表明细），投影主链与降级检测器经 `TryGetMappingDetail`（(源, 目标) 精确对）三态分流：①明细命中按快照构建（零反射、零特性读取）②off 态身份键维持 Fluent 通道强制降级（reason 文本逐字不变）③未注册对反射解析兜底（纯运行时模式行为不变）。投影表达式缓存按注册表 `ContentVersion` 版本化，声明变更加载后自动生效。架构口径："投影与映射消费同一快照"——见 [ORM Projection](docs/zh-CN/explanation/orm-projection-design.md#1101-快照消费架构快照优先反射兜底)

### 新增

- **Fluent 字段链式声明（新面）**：`.Member(d => …)` 三段式全量可用——来源段（`.From(…)`，可省略走同名默认）与变换段（`.Mask(…)` / `.Format(…)` / `.FromJson(…)` / `.MapTree(…)`）分槽叠加；同链多操作组合（如"自定义来源 + 脱敏 / 格式化"）编译期一体生成完整取值链，真冲突组合编译期报错（见下条变更）
- **对级新名 `NoAutoMatch()`**：对级"禁用自动匹配"写法，消除与字段级 `NoAutoInclude` 的同名异义；旧对级名 `NoAutoInclude()` 已随断代清扫退役（旧字段版 `.NoAutoInclude(dest)` 双签名族同批退役——字段级形态由 `.Member(d => d.X).NoAutoInclude()` 承载）
- **防呆诊断族 `NCMAP_F68`–`NCMAP_F71`**：裸 `Member` 提醒（F68）、省略来源且无同名可依（F69）、同维度重复声明（F70）、`MapWhen` 整源类型参不符（F71）
- **`[MapNoAutoMatch]` 类级特性（特性通道对级白名单开关）**：与 Fluent 侧 `cfg.For<S,T>().NoAutoMatch()` 语义逐项对等（双通道填同一承载位）。标注在 DTO 类上：该 (S,T) 对不再做同名自动匹配与自动扁平化，**只映射显式声明的属性**（特性侧 = 同身 `[MapProperty]`；Fluent 侧 = `.Member(d => d.X)` 点名），未声明的目标属性归 `default`；反向映射只翻转正向显式声明。沿继承链生效（派生 DTO 自动继承基类白名单）
- **公共 API：`RuntimeSnapshotStore.IsNoAutoMatch(Type, Type)`**：方向无关的白名单查询门面（先查注册表条目——含无序对两侧查找与按对校验，再查两侧类型的 `[MapNoAutoMatch]` 探针），供下游引擎与自定义映射路径消费
- **诊断 `NCMAP047`（Error）**：白名单模式下属性带映射变换特性（`[Convert]` / `[Mask]` / `[MapFormat]` / `[MapWhen]`）却未同身标注 `[MapProperty]`——白名单只映射显式声明的属性，该属性既不参与映射也不入候选。**两条出路**：补一条 `[MapProperty]` 显式声明，或移除该变换特性

### 变更

- **[行为变更] Compose 跨表明细掩码在内存映射腿生效（PII 泄露修复）**：Fluent `From<Sub>(…).Mask<TMask>()` 跨表声明（cm 级明细）此前在降级后二步映射的内存腿被静默忽略（注册表明细解析只扫属性明细，Compose-only 对整体返回 null → PII 明文，仅投影降级报告面可见）；现注册表明细解析同时覆盖 pm 级与 cm 级（仅 Mask 位——其余承载位属跨表取值语义维持现状），cm 级带 Mask 字段照常强制降级、降级后内存腿经同一注册表腿消费掩码（同目标属性 pm 优先 cm 补位，特性腿优先与"同一属性不双重施加"防线保持）；跨表明细取值以同名直配形态为限（非同名/多段路径取值属已登记限制）
- **[行为变更] 白名单门补齐运行时反射腿两通道**：`[MapNoAutoMatch]` 白名单对的"未声明同名不配对"此前只在 Block 路径属性赋值循环执行，运行时反射映射的 **MemberInit 快路径**与**构造器按名匹配腿**（record / 带参构造目标——执行序先于快路径/Block 分流）两条通道无门，白名单被静默绕过（`Mapper.Map<Ent, Dto>` 返回自动匹配值而非 `default`）；现两通道一体执行同一判定——快路径对白名单整对回退 Block 路径由既有门逐属性判定，构造器腿按名匹配命中后复用同一显式声明谓词（语义单源），未声明参数一律取 `default`。显式 `[MapProperty]` 声明（含跨表 `FromField` 与反向图镜像）在任一通道照常生效；生成通道行为零变化（快照显式映射天然不自动匹配）
- **[缺陷修复] ORM 投影嵌套⑤腿 `InvalidCastException`**：三层注册链中嵌套层 slots 含注册 DTO 属性但矩阵 miss 时，嵌套投影递归入口对成员表达式强转 `(ParameterExpression)` 必抛异常（投影构建整体失败）；现取消强转直传（两处内部签名第三参同步放宽为 `Expression`，`Expression.Property(Expression, PropertyInfo)` 对任意表达式合法——同文件 `:813`/`:856`/`:1194` 既有先例形态），嵌套 DTO 属性照常递归投影；全部调用点传参本为 `ParameterExpression`，源码兼容零适配
- **[测试修复] GateB 断言跨平台 EOL 归一**：`RuntimeMaskRegistryLegTests` GateB 基线常量硬编码 `\r\n`，而 trace 报告由内部 `StringWriter`（`NewLine = Environment.NewLine`）组装，Linux 上实际 trace 以 `\n` 结尾必红（推定 CI ubuntu test job 现红）；现比较前双侧 `ReplaceLineEndings()` 归一（仓内五处既有惯例同款），Windows/Linux 双侧皆绿，trace 语义零变化
- **[行为变更] 运行时内存映射补齐注册表 Mask 明细消费（缺口修复）**：经 Fluent `.Mask<TMask>()` 或手工注册写入注册表明细的脱敏声明，此前在运行时反射映射路径被静默忽略（明文输出，仅投影降级链的报告面可见）；现于内存映射全路径生效——单标 Mask（脱敏类型名按类型对两侧程序集解析，含嵌套形态与已加载程序集扫描，解析失败显式抛 `MapException` 不静默降级）、与 `[Convert]`/`[MapFormat]` 双标组合（先转换/格式化后脱敏）、点路径与命名扁平化叶节点、新建（MemberInit 尝试）与就地更新（Block 渲染）双渲染路径。分派边界：特性腿（`[Mask]`）优先、注册表腿补位、同一属性不双重施加；带声明但无类型名、目标面无同名属性、非 `string` 目标均输出 Trace 警告并照常/常规映射（与特性腿同口径）。已知限制不变：先映射后注册的类型对，已物化委托不因后续注册改变
- **[行为变更] Fluent Compose 多源合成解锁——恒降级内存映射改为明细可译性判定**：Fluent 通道的多源 `FromSource` 声明对（`cfg.For<S,T>()` 块内 `From<Sub>(…)` 合成）此前在 ORM 投影面恒判定为不可投影、强制走"查实体 → 内存映射"降级路径，且降级内存映射**不产出副源字段**（跨表字段静默缺失）；现按声明明细逐条判定可译性：**可译**（副源字段直取/点路径/索引段，无脱敏/转换/格式化/JSON/条件守卫承载，对级无钩子/多态分派）→ 走 SQL 侧投影（单表导航推断 + 双表 Compose 门面双路生效，跨表字段首次真正可映射——**查询形态变化：一条 SQL 直出含跨表字段的 DTO，性能特征正向**）；**不可译**（cm 级 Mask/Convert/Format/FromJson/When 任一，或对级 AfterMapping/BeforeMapping/SubclassDispatch）→ 维持强制降级（reason 细化为具体类别）；"声明有、产出无"形态保守维持降级。判定失败排查入口 = 降级 Trace 的 reason 类别；Mapper.Compose 内存多源合成渠道为特性对专属（Fluent 对不受本变更影响）。详见 [FreeSql 多表查询](docs/zh-CN/how-to/orm/freesql-multi-table.md) 的"Fluent Compose 解锁"节
- **`NCMAP_F59` 级别 Warning → Error，语义改写为"真冲突报错"**：同锚点跨种类声明覆盖（Ignore×映射操作、脱敏/格式化×树递归、脱敏×JSON、格式化×JSON、同维度重复）在编译期强制暴露；Error 级不可被 `#pragma` / NoWarn 抑制
- **`NCMAP_F29` 计数口径修正**：块内 `Format` / `MapTree` 为修饰型/递归型，不计入路径承载计数——块内"From + Format/MapTree"组合不再被误拒
- **Fluent 对不再消费属性级标注（行为语义变更）**：属性级配置标注（`[MapProperty]` / `[MapIgnore]` / `[Mask]` / `[Convert]` / `[ConvertBack]` / `[MapFormat]`）的运行时准入由"任一锚"收紧为**特性锚**——仅由 `cfg.For<S,T>()` 声明（无 `[MapFrom]`）的 Fluent 对，其 DTO 上残留的属性级标注在运行时回退路径、ORM 投影与降级判定中一律不再生效，配置以 Fluent 声明为准；`[MapFrom]` 特性对与无锚类型行为零变化，`[MapWhen]` 仍按既有口径触发投影降级。此前"生成端不读 / 运行时读"的双引擎分叉就此消除，详见[映射配置所有权三原则](docs/zh-CN/explanation/semantics/mapping-config-ownership.md)
- **`MapTracer` / `MapValidator` 报告口径同步**：Fluent 对上的属性级标注如实报为 `⚠️ not consumed (Fluent pair — attribute ignored; use Fluent declarations)`，与"无锚 = `⚠️ invalid config`"分列两态；`MapValidator` 统计面同步（该类属性进入未映射统计而非被当作已忽略）
- **Fluent 提取期缺陷修复**：① 无效成员头（如 `.Member(d => 5)`、`.Member(d => d.Items[0])`）不再导致生成器崩溃或声明静默写入——按段锚有效性统一拒绝，既有段开诊断（F05/F06/F07/F55）如约单报、不叠加 F69；② `Convert` / `ConvertBack` 的 `overrideIgnore` 实参不可静态求值时，由"声明静默丢失"改为 **`NCMAP_F58` 诊断 + 默认值生效 + 声明照常生成**
- **Fluent 索引段投影修复（EF Core `AsDto` 投影路径）**：注册表明细携带 `SourceIndexes`（索引段承载，生成器发射契约早已约定）时，投影工厂此前仅按段名拼点路径、索引信息被丢弃——`Items[0].Title` 类索引段声明绑定静默跳过（发射了没有消费方）；现按契约逐段重建段名索引字面量（`Items` + `0` → `Items[0]`）交既有点路径解析，索引段投影由"静默跳过"变"投影成功"；多段与单段带索引形态同口径修复
- **段名含 `[` 的手写索引字面量形态保持原行为**：段名已含索引字面量（如手写注册 `SourcePathNames = ["Items[0]"]`）时不再叠加重建（防 `Items[0][0]` 类双重索引）——防御性锁定既有合法形态
- **开启白名单后反向映射语义收窄（行为变更，需显著注意）**：特性通道对标注 `[MapNoAutoMatch]`（或 Fluent 侧 `.NoAutoMatch()`）后，**反向映射只覆盖正向显式声明的属性**；不开白名单时（默认态）反向仍覆盖同名自动匹配属性。同对的正反向因此不再对称——迁移既有"靠反向自动匹配回填"的写法时需一并复核
- **白名单对 ORM 投影强制降级**：`[MapNoAutoMatch]` 实现 `IProjectionPolicy`（`RequiresDowngrade = true`），EF Core / FreeSql 投影路径遇到白名单对一律降级为"先投影实体、再内存映射"，保证未声明列不被读出（正确性优先）。代价 = 该对的列裁剪优化失效；投影引擎消费白名单裁剪列为后续评估方向
- **白名单对不再产出 `NCMAP007`**：白名单模式下"源属性未被映射"是声明意图而非缺陷信号，`NCMAP007`（Info）对该对抑制——含类级 `[NCMapperOptions(ReportUnmappedSourceMembers = true)]` 显式开报告的形态；需要未映射源成员审计时请勿对该对开启白名单
- **`NCMAP019` 家族扩至十一项**：`[MapNoAutoMatch]` 纳入属性级配置家族——所在类无映射声明锚（`[MapFrom]` 或 `cfg.For<S,T>()`）时与其他十项同样报 `NCMAP019`（Error），配置不再静默失效
- **[用户可观测] EF Core `AsDto().Single<T>()` / `SingleAsync<T>()` 异常消息统一为本库资源文案**：零条或多条记录时抛出的消息不再来自 BCL 英文默认串，改由本库错误资源产出——**含目标 DTO 类型名**并按当前 UI 区域性在中英文之间切换，且与 FreeSql 包在对应场景下**消息文本逐字一致**；**异常类型不变**（仍为 `InvalidOperationException`），`First` / `FirstOrDefault` / `SingleOrDefault` 族口径未变

### 修复（注释与交付面清洁，零行为变更）

- **源码注释去过程信息**：XML doc（IntelliSense 与 docfx API 页的文本源）、实现层 `//` 注释、示例工程注释中残留的任务编号、需求 / 评审条目号、批次标记、内部文档章节引用与日期全部清除；语义主体逐条保留——线程语义、AOT 形态、容量与淘汰策略、异常行为、缓存清除口径等契约一字未改（公共 API 快照门与逐字节生成基线门双重证明零行为变更）。
- **生成码注释同步清洁**：`obj/Generated/**.g.cs` 的头部与成员注释不再含内部任务编号与内部文档引用；生成器的输出结构、发射条件与可执行内容零变化（逐字节基线在逐行归因后重建，差异全部落在注释文本）。
- **诊断文案仅剥括注**：`NCMAP033` / `NCMAP036` / `NCMAP_F15` / `NCMAP_F44` / `NCMAP_F45` / `NCMAP_F52` / `NCMAP_F54` 的 message / description 去掉内部编号注记（中英成对），`{0}` / `{1}` 占位、建议文案与诊断 ID、严重度全部不变。
- **贡献者规范与门禁**：`CONTRIBUTING.md` 新增 "Comment Content Contract" 节（注释三层归属 + 一句话判别法 + 禁写清单 + 四步写法）；新增 `scripts/check-comment-process-noise.ps1` 门禁，挂在 `scripts/local-ci-check.ps1 -Phase scan`，注释面出现过程信息即判红。

### 并存期已知行为差异（已全部解除——2026-10-10 断代清扫落位，旧名退役）

1. ~~对级开关位序限制~~ **已解除**：旧对级名退役后，`NoAutoMatch()` 在继承面任意链位可用
2. ~~同名双写怪相~~ **已解除**：`.Ignore().NoAutoInclude().NoAutoInclude()` 已被编译期拦截
3. ~~重复写 `NoAutoInclude` 暂不拦截~~ **已解除**（反例 N5 恢复拦截）：旧名删除后恢复编译期拦截

## [1.0.0-preview.1] - 2026-10-03

这是 NCMapper 的首个公开预览版本。作为初始版本，本版没有更早的版本可供对照，因此下面列出的是该版本提供的能力范围，而不是相对上一版的改动。

### 声明方式

- **双通道声明**：**Fluent 通道**（`[MapperConfig]` 配置类 + 链式声明）与 **Attribute 通道**（`[MapFrom]`、`[MapProperty]` 等特性族）能力对等、长期并存。同一类型对只能由其中一个通道声明一次（`NCMAP_F11` 互斥校验），不同类型对之间可自由混用
- **双引擎**：已声明的类型对走**源生成器**路径（编译期生成映射代码、零反射、AOT 友好）；未声明的类型对回退到**表达式树**路径；两条路径语义一致

### 映射能力

- **单对象映射**：`Mapper.Map<S,T>(src)` — 同名属性自动匹配（忽略大小写 + 扁平化），一次声明覆盖双向（Entity ↔ DTO）
- **就地更新**：`Mapper.Map(src, dst)` — 只覆盖参与映射的字段
- **批量映射**：`Mapper.MapList<S,T>(srcs)`；`MapListInto` 复用已分配的目标对象，零顶层分配
- **名称匹配与嵌套**：同名 / 忽略大小写 / 扁平化 / 前后缀剥离、点路径、任意深度嵌套对象
- **集合映射**：支持 List / 数组 / 字典 / 集合等常见集合类型
- **构造器与 record**：主构造器参数自动匹配、支持 `with` 形态；record 类型存在环引用或静态不可解析边时在源生成阶段自动检出并回退运行时（`NCMAP012`）
- **自定义转换**：`[Convert]` / `[ConvertBack]`（静态方法引用，4 种签名形态）↔ 链式 `.Convert` / `.ConvertBack`
- **条件守卫**：`[MapWhen]` / 链式 `.MapWhen`（具名方法 + 内联 lambda 两种形态）
- **多态子类型分派**：`[MapSubclass]` / 链式 `.MapSubclass<TSrcDerived, TTargetDerived>()` — 最派生优先 + 基类兜底
- **生命周期钩子**：类级 `[BeforeMapping]` / `[AfterMapping]` ↔ 链式 `.BeforeMapping` / `.AfterMapping`
- **构造器参数绑定**：链式 `.MapCtorParam`（Fluent 独有能力，无特性侧形态）
- **JSON 展开**：`[MapProperty(IsFromJson = true)]` / 链式 `.FromJson(dest)` — JSON 文本列 ↔ 强类型对象
- **属性级格式化**：`[MapFormat("格式串")]` / 链式 `.Format(dest, format)` — 源实现 `IFormattable` 的类型（DateTime / DateTimeOffset / TimeSpan / Guid / 全部数值类型 / 枚举，含 `Nullable<T>` 包装）格式化输出到目标 `string` 字段；格式串作为编译期常量内嵌生成代码，双引擎产物一致
- **数据脱敏**：`[Mask]` / 链式 `.Mask` — 9 种内置实现 + 自定义 `IMask`，可与值转换正交组合
- **多源合成**：`Compose()` 从多个源对象装配聚合 DTO；`ComposeList()` 批量合成
- **深拷贝**：三级策略 — 源生成器（约 181 ns，最快）→ `Fast`（默认，三元分类 + 条件分派）→ `ExpressionTree`（环引用检测 + DAG 共享引用保持）；覆盖 POCO / record / 16 种集合类型
- **全局映射配置**：`[assembly: MapDefaults]`（命名策略 / 前后缀剥离 / 忽略清单，双向 + ToEntity 两层），或经 MSBuild 属性（`NCMapperXxx`）配置
- **分页包装**：`PagedList<T>` + `ToPagedList` / `ToPagedListAsync`

### ORM 集成

三个相互独立的扩展包，`AsDto()` 终端操作直接产出 SQL 到 DTO；遇到无法翻译的声明时自动降级为两步映射：

- **NCMapper.FreeSql** — `AsDto<T>()` 投影、分页、树构建（`ToTreeList`）
- **NCMapper.EFCore** — `AsDto<T>()` 投影、降级检测
- **NCMapper.Dapper** — `AsDto` 列名映射、`ToPagedList`、`ToTreeList`

### 诊断与可观测性

- **编译期诊断**：`NCMAP0xx`（特性族）+ `NCMAP_Fxx`（Fluent 族），Error / Warn / Info 三级；完整诊断码索引见[诊断码规则](docs/zh-CN/diagnostics/rules.md)
- **映射验证**：`Validate()` 扫描程序集产出 OK / Warning / Error 三级报表；`ValidateOrThrow()` 快速失败
- **映射追踪**：开关控制、逐映射路径输出（全局 / 单次调用两种粒度）
- **映射管道**：`Mapper.Pipeline` 支持可插拔的 PostMap 回调；管道为空时零开销
- **性能指标**：`MetricsEnabled` 统计调用次数 / 缓存命中 / 编译次数

### 部署与集成

- **AOT / NativeAOT**：已声明的类型对走源生成路径，裁剪与 AOT 友好；运行时回退路径标注 `[RequiresDynamicCode]`
- **裁剪兼容声明**：`IsTrimmable` / `IsAotCompatible`
- **DI 集成**：`AddNCMapper()` 一行注册 `IMapper`，支持 Options 回调与 `appsettings.json` 配置绑定
- **可插拔策略**：JSON 序列化器（`JsonSerializerBase.Current`）与深拷贝提供程序（`DeepCopyProviderBase.Current`）均可替换

### 国际化

- 运行时错误消息双语（zh-CN / en-US，随 `CurrentUICulture` 切换）
- 编译期诊断消息双语（英文默认 + zh-CN）
- 双语 IntelliSense XML 文档：zh-Hans / en

### 包与目标框架

| 包 | 说明 |
| -- | ---- |
| `NCMapper` | 核心库（运行时引擎 + 表达式树 + 反射回退）；源生成器以 Analyzer 资产内嵌，引用核心包即自动生效 |
| `NCMapper.FreeSql` | FreeSql 集成 |
| `NCMapper.EFCore` | EF Core 集成 |
| `NCMapper.Dapper` | Dapper 集成 |

目标框架：**net8.0 / net9.0 / net10.0**。

### 已知事项

- 本版为**预览版本**，公开 API 在正式版发布前仍可能调整。后续行为变更会在本文件与文档站同步说明。
- 预览版本需显式指定才能安装：`dotnet add package NCMapper --prerelease`。
