---
wiki-id: reference/samples-exemptions
库版本: v1.0.0+
更新时间: 2026-11-02
---

# 按原文呈现的示例代码块

本文档中的 C# 示例块有两种呈现方式：**绑定块**——文档构建时从本仓库 `samples/` 目录下的真实可编译示例中按区域提取（源码以 `#region` 标记），内容随示例代码自动保持同步；**按原文呈现块**——直接按文档中所写展示。

按原文呈现的块通常是：含省略号占位的示意片段、自标伪代码或源生成器输出形态的展示、纯注释的概念辨析——它们不是可独立编译执行的单元，不适用 `samples/` 区域绑定。本页逐条列出中文文档中全部此类块、其原因，以及内容与实现一致的核对说明（锚点为源码文件与行段）。

## 按原文呈现的块清单

| # | 所在页:行 | 块首行（内容锚） | 为何按原文呈现 | 与实现的核对说明 |
|---|---|---|---|---|
| 1 | explanation/mechanics/callbacks-collections.md:135 | // 生成代码（简化） | 演示源生成器为 MapList 三层快速路径发射的代码形态，属生成侧输出而非用户调用代码，真实示例无法承载 | 运行时三分支结构与 `src/NCMapper/Mapper.cs:512-530`（List 快路径预分配、null 元素保留跳过的语义）及生成器 `CodeGenerator.Collections.cs:590` 集合计划分派吻合；预分配容量=源长度、数组路径 for+边界可消除形态吻合 |
| 2 | explanation/mechanics/constructors-and-types.md:77 | // 源生成器生成（伪代码） | record DeepCopy 构造器传参的伪代码（块自标「伪代码」），单行注释形态，非可独立编译单元 | 构造器参数递归传参形态与 `src/NCMapper.SourceGen/CodeGenerator.DeepCopy.cs:132-140`（回退 `NCMapper.Mapper.DeepCopyRuntimeOnly` 发射）吻合；Cyclic/Unknown 分类回退+NCMAP012 警告与 `Models.cs:285`（NCMAP012 定义）及 `Mapper.cs:1231`（DeepCopyRuntimeOnly 公开面）吻合 |
| 3 | explanation/mechanics/constructors-and-types.md:155 | // 生成代码 | enum→string 生成代码形态展示（块自标「生成代码」），生成侧形态不在示例演示面 | switch 表达式每成员→字符串字面量+兜底 `ToString()` 形态与 `CodeGenerator.Conversion.cs:95`（switch 发射）、`:86-90`（同值别名冲突退化为 ToString）、可空输入 `null 输入命中 _ 兜底 → ToString()`（`:90`）逐语义吻合 |
| 4 | explanation/mechanics/constructors-and-types.md:179 | // 生成代码 | string→enum 反向 switch 生成形态展示（块自标「生成代码」） | `"字面量" => 成员` 反向 switch+非法值 throw MapException 与 `CodeGenerator.Conversion.cs:158/:948`（`?.ToLowerInvariant() switch` 大小写无关匹配）吻合；异常消息「无匹配枚举成员」双语形态有值断言用例在案（`test/NCMapper.Tests/Tests/EdgeCaseTests.cs:1162`） |
| 5 | explanation/mechanics/constructors-and-types.md:193 | // 生成代码 | enum→enum 按值强转生成形态展示（块自标「生成代码」，单行） | `(TargetEnum)src` 按值强转与 `GenerationPreparation.cs:792`（「enum→enum 强转」5 子分支之一）及 `CodeGenerator.Mapping.cs:851`（分支注释）吻合 |
| 6 | explanation/mechanics/name-matching.md:148 | // 同类型 → 直接赋值 | 类型兼容分发五分支的生成代码族展示，非可执行单元 | 同类型直赋/隐式 cast/Nullable↔NonNullable/`NCMapper.TypeConvertHelper.ConvertValue` 回退+「fallback to runtime」注记，与 `GenerationPreparation.cs:635`（「5 分支：switch/退避 ToString/精确匹配/throw/强转」同判定）及 `TypeCompatAdapter.Symbol.cs:139` SpecialType 分派吻合 |
| 7 | explanation/mechanics/nested-mapping.md:299 | // 生成代码（源生成器输出） | 嵌套扁平 `?.` 链+值类型叶 `?? default` 的生成输出形态展示（块自标「生成代码」） | `src.Customer?.Name` 访问表达式为生成器标准产出——`CodeGenerator.Conversion.cs:37`（返回值注释明例「如 src.Name 或 src.Child?.Name」）；值类型叶 `?. … ?? default(int)` 与可空兜底语义同 `Conversion.cs:90` 处理族 |
| 8 | tutorials/deepcopy-tutorial.md:22 | var original = Mapper.DeepCopy(entity); | 以叙事为主的业务决策块（编辑前快照/取消还原场景注释主导），API 调用形态已由本教程其余演示与 `04-DeepCopy` 场景承载，按原文呈现可保留叙事 | `Mapper.DeepCopy<T>` 公开面 `src/NCMapper/Mapper.cs:1027`（独立副本语义=DeepCopyProviderBase.Current 策略路径，嵌套对象深拷贝与块内叙事吻合）；对应场景演示见 `04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyFast` 等 |
| 9 | tutorials/fluent-modeling-tutorial.md:110 | // 同名锚点：DTO 属性名与实体属性名相同 | `.Format` 用法片段，注释自标「生成代码里是编译期常量」（伪代码形态） | 格式串编译期常量写入与 `FluentExtractor.cs:1086`（「新形态变换槽格式化写入——.Format(format[, oi])（格式串前移 args[0]）」常量固化发射）吻合；同名锚点/文化无关注记与 `how-to/fluent/format.md` 的用法说明一致 |
| 10 | explanation/deep-copy.md:105 | // 防御性拷贝 — 方法内部修改不影响调用方的原始对象 | 方法体含 `/* ... */` 省略占位，叙事场景块非独立演示 | `Mapper.DeepCopy<T>` 公开面 `Mapper.cs:1027` 返回独立对象图（DeepCopyProviderBase 递归复制嵌套引用），方法内改副本不影响入参语义吻合 |
| 11 | explanation/orm-freesql.md:44 | // 双表 — DTO 通过 [MapProperty(typeof(CustomerEntity), ...)] 声明跨表字段 | 两段重复声明 `var dtos` 的不可编译组合，注释含省略号 | `fsql.Select<A,B>().AsDto().ToList<Dto>()` 形态与 samples `FreeSqlScenarios.cs#OrmProjection_MultiTableTwoWay`/`#OrmProjection_MultiTableThreeWay` 逐段 1:1，投影消费 `ComposeProjectionKernel.cs` |
| 12 | explanation/reverse-mapping-semantics.md:71 | public static TTarget Convert(DTO类型 source, TField fieldValue) | 含中文占位符「DTO类型」的伪签名，不可编译 | T3 组合形态=整源+字段值双入参，与 samples `CvModels.cs#FluentConvert_Convert` 的 `.Convert<DateTime>(CvConverters.FormatModified)`（T3 注释在区）同模板 |
| 13 | getting-started/quickstart.md:86 | // 1. 带 Action 回调：映射完成后额外处理 | 三形态连展示，含 `{ ... }` 省略占位 | 三形态均有演示区对应（`BasicScenarios.cs#BasicMapping_MapWithAction`/`#BasicMapping_ListMap`/`#BasicMapping_UpdateExisting`）；`Mapper.Map<S,T>(src,action)` 重载公开面 `Mapper.cs:1257` |
| 14 | how-to/compose/compose.md:234 | // ❌ 误用：副源字段如果没有 [MapProperty]，不会从副源匹配 | 误用反例块含 `...` 省略，反例语义不宜挂进正例演示区 | 副源字段须显式 `[MapProperty(副源类型,…)]` 才参与解析——同页 95-114 行正例已绑定 `Compose_BasicCompose_MultiSourceCompose`（四字段全带 MapProperty），快照元数据按声明发射 `SnapshotMetadataEmitter.cs:266`（`pm.Source?.Name` 仅已声明者入表） |
| 15 | how-to/declarative/basics.md:408 | using NCMapper.Generated; | 三调用连展示，含 `(src, tgt) => { ... }` 省略 | 生成入口扩展方法（创建/回调/更新 void）三形态在 samples `BasicScenarios.cs#Core_Generated_EntryExtensions` 同型演示（该演示区同样被 installation.md 引用） |
| 16 | how-to/declarative/basics.md:429 | // 源生成器自动生成（用户无需编写；CodeGenerator.GenerateModuleInitializer) | 块自标「源生成器自动生成（用户无需编写）」，展示的是生成器自动产出的注册形态 | `CodeGenerator.cs:573` GenerateModuleInitializer + `:613/:639` `NCMapper.Mapper.RegisterGeneratedCore<S,T>(GeneratedMapper.Map_源_To_目标)` 注册行发射逐字同型（含反向注册行），方法名模板注释吻合 |
| 17 | how-to/declarative/map-format.md:59 | [MapFormat("N2")]（先 ToString 再 Mask 顺序注记） | 属性标注连用片段（两属性行无宿主类），不可编译 | 「先格式后脱敏」包装方向与 `CodeGenerator.Collections.cs:247/:263`（赋值包装为 `__mask_X.Mask(srcAccess/(type)srcAccess)`——Mask 包在外层、格式化/转换在inner）吻合 |
| 18 | how-to/deep-copy/deep-copy.md:127 | // ❌ 误解：DeepCopy 不是浅拷贝 | 纯注释的概念辨析块（零代码行） | DeepCopy=递归复制全部嵌套引用创建独立对象图（`DeepCopyProviderBase` 三策略均递归）、`MemberwiseClone` 浅拷贝边界为 .NET 语义；辨析方向与 `Mapper.cs:1027` 注释族一致 |
| 19 | how-to/infrastructure/pipeline.md:110 | // 只对 UserEntity → UserDto 生效 | `AddPostMap` 双声明示例含 `{ ... }` 省略占位 | 按类型对粒度独立注册——`MapPipeline.cs:108` `AddPostMap<TSource,TTarget>` 按对维护回调数组；`:22-:60` 缓存失效按对清除（映射另一对不触发本 PostMap） |
| 20 | how-to/mapping/dot-path.md:109 | // 正向：Entity.Address.City → DTO.City（读路径） | 两行纯注释块（零代码行） | 反向写回 null 时自动创建中间节点与 `ReversePlanner.cs:489`（源侧点路径写回链）吻合；正向读路径演示 `BasicScenarios.cs#BasicMapping_DotPath`（该演示区同样被 installation.md 引用） |
| 21 | reference/core-api.md:68 | Mapper.MetricsEnabled = true; | 块含「执行映射操作...」省略占位 | `Mapper.cs:21` MetricsEnabled（:18 注释 Interlocked 开销说明）/`:397` TotalMapCalls/`:402` CacheHits+CompilationCount/`:443` ResetMetrics 五成员全实存 |
| 22 | reference/features.md:245 | [Convert(typeof(DateTimeToStringConverter)…)] | 双标注片段+「生成代码：」自标注释行 | Convert 先执行、`__mask_Phone.Mask(Convert(...))` 外层包 Mask——`CodeGenerator.cs:350`（`__mask_` 字段命名）+`Collections.cs:247` 包装方向吻合 |
| 23 | reference/features.md:326 | // 生成代码（简化） | 块自标生成代码 | `private static readonly MaskPhone __mask_UserDto_Phone` 静态字段+赋值处包装与 `CodeGenerator.cs:350` MaskFieldName 形态逐字同型（IMask.cs:6 注释亦同款说明） |
| 24 | reference/security.md:14 | // 循环引用：A.Child → B.Parent → A.Child → ... | 注释含省略号，叙事性 try/catch 块非独立演示 | `Mapper.cs:855` `DefaultMaxRecursionDepth = 64`（NCMapperOptions.cs:57 默认引用之）——「第 64 层抛 MapException（可 catch）不 StackOverflow」与默认值及守卫语义吻合 |
| 25 | how-to/orm/dapper-advanced.md:221 | // ❌ 误用：实现无 GridReader 形式（无 multi.Read<T>() / using 释放语义） | 误用反例块含省略，反例语义不宜挂进正例演示区 | 正例 GridReader 消费形态绑定于同页 82-92 行引用的 `DapperScenarios.cs#Dapper_Advanced_QueryMultiple`（QueryMultiple 返回 reader 的 using 释放语义在区体）；反例辨析方向吻合 |
| 27 | how-to/orm/freesql-advanced.md:203 | // 路径 A（推荐）：FreeSql 原生聚合语法，在 SQL 侧完成 GROUP BY | 聚合伪码含 `...` 省略占位 | 块主体为 FreeSql 第三方库面（Select…GroupBy 原生聚合），NCMapper 不参与该路径；NCMapper 侧 GROUP BY 对应演示绑定于 samples `#OrmProjection_GroupByNativeAggregate`/`#OrmProjection_GroupByInMemoryLinq`（EF Core 面） |
| 28 | how-to/orm/freesql-multi-table.md:157 | fsql.GlobalFilter.Apply<ISoftDelete>("SoftDelete", e => e.IsDele… | FreeSql 第三方库 API 示例块，含省略与多段拼接 | GlobalFilter.Apply 为 FreeSql 官方库面；NCMapper `AsDto()` 投影运行在过滤后的 FreeSql 查询之上（ComposeProjectionKernel 消费源查询，过滤上游生效）。samples 目前无 GlobalFilter 演示 |
| 29 | how-to/orm/freesql-single-table.md:214 | // 注册全局过滤器 | 同 28（FreeSql 库面叙事块，含省略） | 机制同 28——过滤在 FreeSql 查询侧生效，NCMapper 投影兼容消费；samples 目前无对应演示 |

## 这份清单的作用

绑定块的内容正确性由构建期提取与自动化对账保证；按原文呈现的块不经该提取，其内容一致性以上表的逐条核对说明为准，清单随文档修订同步维护。若发现清单中某块与当前实现或上下文不符，欢迎通过 issue 反馈。
