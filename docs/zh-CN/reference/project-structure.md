---
title: 项目结构
---

<!-- 来源：NCMapper 主技术文档 §3 项目结构（结构重组，2026-08-19 章级迁移，内容未重排） -->
## 3. 项目结构

> 以仓库实际文件清单为准核对（2026-09-30）。partial class 主文件与其子文件以缩进归组。

```
src/
├── NCMapper/                              — 核心库（net8.0;net9.0;net10.0，113 个 .cs：顶层 64 + Internal 11 + Runtime 9 + Shared 10 + Strategies 19）
│   ├── Mapper.cs                           — 公共入口 + JIT 泛型缓存 + 策略分发
│   ├── IMapper.cs                          — DI 接口
│   ├── NCMapperMapper.cs                   — IMapper 默认实现（Singleton）
│   ├── NCMapperServiceExtensions.cs        — AddNCMapper() DI 扩展
│   ├── MapperBase.cs                       — 核心泛型类 + 委托编译 + 静态字段
│   ├── MapperBase.ExpressionBuilder.cs     — GetMapBody + 内联表达式构建
│   ├── MapperBase.Properties.cs            — 属性匹配（大小写不敏感 + 扁平化）
│   ├── ExpressionBuilder.cs                — 表达式树构建辅助（partial 主文件）
│   │   ├── ExpressionBuilder.Converters.cs — 嵌套/JSON/转换表达式子路径
│   │   ├── ExpressionBuilder.Collections.cs — 集合/字典表达式子路径
│   │   └── ExpressionBuilder.Paths.cs      — 路径（dot-path/扁平化）表达式子路径
│   │
│   ├── MapFromAttribute.cs                 — [MapFrom] 类级映射声明锚
│   ├── MapperConfigAttribute.cs            — [MapperConfig] Fluent 通道配置类标记
│   ├── NCMapperOptionsAttribute.cs         — [assembly: NCMapperOptions] 编译期诊断开关
│   ├── MapPropertyAttribute.cs             — [MapProperty] 属性级特性（异名/JSON/FromSource/豁免门）
│   ├── MapIgnoreAttribute.cs               — [MapIgnore] 方向控制忽略
│   ├── ConvertAttribute.cs                 — [Convert] 自定义值转换特性（静态方法引用）
│   ├── ConvertBackAttribute.cs             — [ConvertBack] 反向值转换特性（静态方法引用）
│   ├── MapFormatAttribute.cs               — [MapFormat] 属性级格式化（IFormattable→string，IProjectionPolicy）
│   ├── MaskAttribute.cs                    — [Mask] 属性级脱敏特性（实现 IProjectionPolicy）
│   ├── MapWhenAttribute.cs                 — [MapWhen] 属性级条件守卫
│   ├── MapNoAutoIncludeAttribute.cs        — [MapNoAutoInclude] 关闭导航自动 Include（NCMAP017 Info）
│   ├── MapTreeAttribute.cs                 — [MapTree] 树形字段名自定义特性
│   ├── MapSubclassAttribute.cs             — [MapSubclass] 类级多态子类型映射登记
│   ├── BeforeMappingAttribute.cs           — [BeforeMapping] 类级生命周期前置钩子
│   ├── AfterMappingAttribute.cs            — [AfterMapping] 类级生命周期后置钩子
│   ├── MapDefaultsAttribute.cs             — [assembly: MapDefaults] 全局映射配置特性（命名策略/忽略清单）
│   ├── NamingStrategy.cs                   — 命名策略枚举与转换（PascalCase/SnakeCase 等）
│   ├── IMask.cs                            — 脱敏接口（string Mask(string? value)）
│   ├── MaskProvider.cs                     — 脱敏实现注册表（启动时 Override 替换内置实现）
│   ├── Masks.cs                            — 9 个内置脱敏实现（MaskPhone/MaskIdCard/...）
│   ├── IProjectionPolicy.cs                — 投影降级策略接口（RequiresDowngrade）
│   ├── GlobalIgnoreChecker.cs              — 全局忽略清单检查器（fail-closed，OverrideIgnore 可豁免）
│   ├── PropertyIgnoreChecker.cs            — [MapIgnore] 检查逻辑提取
│   │
│   ├── FluentMappingConfig.cs              — Fluent 通道：FluentMappingConfig/FluentTypeConfig 链式终端全集
│   ├── GeneratedMapConfig.cs               — 源生成器生成的配置注册入口
│   ├── NCMapperOptions.cs                  — Options 模式配置类
│   ├── GlobalMapConfig.cs                  — 全局映射配置模型（三段优先链聚合）
│   ├── GlobalConfigView.cs                 — 全局配置只读视图（冻结后不可变）
│   │
│   ├── NavigationResolver.cs               — 投影导航属性解析器（Include 路径解析）
│   ├── MapValidator.cs                     — 映射验证器 + 结果模型
│   ├── MapTracer.cs                        — 映射诊断 Trace
│   ├── MapException.cs                     — 映射异常类 + MapConfigurationException（含源/目标类型构造器）
│   │
│   ├── DeepCopyProviderBase.cs             — 深拷贝策略抽象基类（两阶段+循环检测）
│   ├── DeepCopyTypeClassifier.cs           — 深拷贝三值分类器（Acyclic/Cyclic/Unknown 条件分发）
│   ├── DeepCopyContext.cs                  — 深拷贝共享上下文（缓存/循环检测状态）
│   ├── FastDeepCopyProvider.cs             — 快速深拷贝（默认策略，三值分类条件分发：Acyclic/Cyclic/Unknown）
│   ├── ExpressionTreeDeepCopyProvider.cs   — 表达式树深拷贝（含循环引用检测）
│   ├── DeepCopyCompiler.cs                 — 深拷贝表达式树编译器
│   ├── CollectionCopier.cs                 — 集合拷贝（16 种类型全覆盖）
│   ├── CollectionTypeDetector.cs           — 集合类型检测器
│   │
│   ├── JsonSerializerBase.cs               — JSON 序列化策略基类（可替换）
│   ├── JsonConverter.cs                    — JSON 转换辅助（转发到策略）
│   ├── TypeConvertHelper.cs                — 运行时类型转换（public，源生成器引用）
│   ├── TypeConverter.cs                    — 隐式数值转换查找表
│   ├── TypeHelper.cs                       — 属性提取共享工具
│   ├── ReflectionHelper.cs                 — 反射辅助方法
│   ├── CacheMetrics.cs                     — 缓存指标内部观测（internal static，供性能诊断）
│   ├── ProjectionNameResolver.cs           — 投影字段名解析器（扁平化/树形命名约定）
│   ├── PropertyMatcher.cs                  — 属性匹配器
│   ├── MapPipeline.cs                      — 映射管道（PostMap 调度）
│   ├── Mapper.Pipeline.cs                  — 映射管道实现（洋葱模型）
│   ├── Mapper.Compose.cs                   — Compose 多源合成实现
│   ├── ErrorMessages.cs                    — 中英文错误消息资源
│   ├── PagedList.cs                        — 分页查询结果类型（FreeSql/EFCore/Dapper 扩展共用）
│   │
│   ├── Shared/                             — 双引擎共享层（源生成器与运行时同口径消费）
│   │   ├── assignmentplan.cs               —   赋值计划模型（AssignmentKind 全集：Direct/Convert/Json/Mask/Format/FormatThenMask/...）
│   │   ├── assignmentplanner.cs            —   赋值计划编排
│   │   ├── NameCandidateResolver.cs        —   名称候选解析（大小写不敏感 + 扁平化）
│   │   ├── PathResolver.cs（+.Type.cs）    —   点路径解析（符号面/类型面）
│   │   ├── TypeCompatAdapter.cs（+typecompatview.cs） — 类型兼容适配
│   │   ├── TypeCompatibilityCore.cs        —   类型兼容核心判定
│   │   ├── DeepCopyGraphRules.cs           —   深拷贝图规则
│   │   └── MutualMapPairRules.cs           —   互指对规则（NCMAP025 判定）
│   ├── Runtime/                            — 运行时引擎承载面（快照/通道/锚点门）
│   │   ├── RuntimeSnapshotStore.cs         —   运行时快照仓库
│   │   ├── RuntimeSnapshotPair.cs          —   方向对快照
│   │   ├── RuntimeMappingChannel.cs        —   映射通道
│   │   ├── RuntimePropertyMapping.cs       —   属性映射槽（含 FormatString/MaskType）
│   │   ├── RuntimeTargetPropertySlot.cs    —   目标属性槽（含豁免门状态）
│   │   ├── RuntimeAnchorGate.cs            —   锚点门（无锚不消费）
│   │   ├── RuntimeMapperMetadata.cs        —   映射元数据
│   │   ├── RuntimeTreeConfig.cs            —   树形配置
│   │   └── MappingMetaView.cs              —   映射元数据视图
│   ├── Strategies/                         — 运行时赋值策略集（19 个策略/解析器，含 FormatStrategy/FormatThenMaskStrategy）
│   │   └── IAssignmentStrategy.cs 等        —   Direct/Nested/Collection/Json/Mask/Format/Convert*/CrossTable/Skip/Ignore...
│   ├── Internal/                           — ORM 投影基础设施（扩展包共用）
│   │   ├── AsDtoExecutor.cs                —   AsDto 执行骨架
│   │   ├── DtoSelectorSkeleton.cs          —   终端方法选择器骨架
│   │   ├── DegradationDetectorBase.cs（+.Agnostic.cs） — 降级检测基类（IProjectionPolicy 驱动）
│   │   ├── NavigationTree.cs（+NavigationTreeCollector.cs） — 导航树与收集
│   │   ├── ProjectionExpressionFactory.cs  —   投影表达式工厂
│   │   ├── ProjectionStrategy.cs           —   投影策略
│   │   ├── TreeBuilder.cs                  —   树形组装
│   │   ├── LeafStructGuard.cs              —   叶结构守护
│   │   └── CacheRegistry.cs                —   注册缓存
│
├── NCMapper.SourceGen/                     — 源生成器（netstandard2.0，顶层 46 个 .cs + Resources）
│   ├── NCMapperSourceGenerator.cs          — IIncrementalGenerator 入口
│   ├── SourceGenPipeline.cs                — 生成管线编排（准备 → 提取 → 仲裁 → 物化 → 生成）
│   ├── GenerationPreparation.cs            — 生成准备阶段
│   ├── GenerationContext.cs                — 单次生成共享上下文
│   ├── GlobalConfigReader.cs               — [assembly: MapDefaults] 全局配置读取
│   │
│   ├── MappingExtractor.cs                 — 特性通道：[MapFrom] 语义信息提取 + 诊断生成（partial 主文件）
│   │   ├── MappingExtractor.Properties.cs  —   属性级匹配/标注提取子路径
│   │   ├── MappingExtractor.Materialize.cs —   物化阶段（声明定稿/诊断发射）
│   │   ├── MappingExtractor.Lifecycle.cs   —   生命周期钩子提取
│   │   ├── MappingExtractor.SnapshotConfig.cs — 快照配置
│   │   ├── MappingExtractor.Traversal.cs   —   类型遍历
│   │   └── MappingExtractor.GlobalConfig.cs — 全局配置应用
│   ├── FluentExtractor.cs                  — Fluent 通道：[MapperConfig] 声明提取（partial 主文件）
│   │   ├── FluentExtractor.Parsing.cs      —   链式调用解析
│   │   ├── FluentExtractor.Declarations.cs —  声明物化
│   │   └── FluentExtractor.Equality.cs     —   等价/冲突判定
│   ├── DualDriverArbiter.cs                — 双通道快照合并仲裁（通道/侧别互斥原则落点）
│   ├── MappingSnapshotBuilder.cs           — 映射快照构建（partial 主文件）
│   │   └── MappingSnapshotBuilder.When.cs  —   MapWhen 守卫快照
│   ├── Models.Snapshots.cs                 — 快照模型
│   ├── SnapshotMetadataEmitter.cs          — 快照元数据发射
│   ├── RuntimeSnapshotGenerator.cs         — 运行时引擎快照（双引擎同口径承载）
│   ├── ReversePlanner.cs                   — 反向映射规划
│   │
│   ├── CodeGenerator.cs                    — 映射代码生成入口编排（partial 主文件）
│   │   ├── CodeGenerator.Mapping.cs        —   Core/Action/MapList + 属性赋值
│   │   ├── CodeGenerator.Collections.cs    —   集合/字典赋值得物化
│   │   ├── CodeGenerator.Conversion.cs     —   内建转换表达式
│   │   ├── CodeGenerator.ConverterCall.cs  —   转换器调用渲染
│   │   ├── CodeGenerator.ConverterMethod.cs —  转换器方法物化
│   │   ├── CodeGenerator.WhenGuard.cs      —   MapWhen 守卫代码
│   │   ├── CodeGenerator.DeepCopy.cs       —   DeepCopy record/集合/无参构造路径
│   │   ├── CodeGenerator.Compose.cs        —   Compose 合成映射
│   │   ├── CodeGenerator.Validate.cs       —   编译时验证结果
│   │   └── CodeGenerator.Report.cs         —   映射关系可视化报告生成（NCMapperGenerateReport）
│   │
│   ├── DiagnosticCollector.cs              — 诊断收集
│   ├── DiagnosticDescriptors.Fluent.cs     — NCMAP_Fxx 族诊断描述符
│   ├── DiagnosticOptionsResolver.cs        — 诊断选项解析
│   ├── ArrayContainsInExpressionAnalyzer.cs — NCMAP018 诊断分析器：检测表达式树 lambda 中 T[].Contains 绑定 MemoryExtensions.Contains（net10/C#14）
│   ├── ChainCallParser.cs                  — 链式调用解析
│   ├── LambdaParser.cs                     — lambda 解析
│   ├── TernaryFinder.cs                    — 三元表达式查找
│   ├── InlineTranscriber.cs                — 无捕获 lambda 内联转录
│   ├── ConfigureMethodLocator.cs           — Configure 方法定位
│   ├── SubclassSyntheticPipeline.cs        — MapSubclass 合成管线
│   ├── PathResolver.Symbol.cs              — 点路径解析（符号面）
│   ├── PlanSymbolAdapter.cs                — 计划/符号适配
│   ├── TypeCompatAdapter.Symbol.cs         — 类型兼容适配（符号面）
│   ├── TypeCompatAdapter.TypeRef.cs        — 类型兼容适配（TypeRef 面）
│   ├── TypeRefHelpers.cs                   — TypeRef 工具
│   ├── TypeRefDeepCopyClassifier.cs        — 深拷贝三值分类（TypeRef 面）
│   ├── TypeHelpers.cs                      — Roslyn 类型判断辅助
│   ├── attributenames.cs                   — 特性全名常量
│   ├── SourceGenEscape.cs                  — 生成代码转义
│   ├── Models.cs                           — 数据模型（MappingInfo/PropertyMapping/DiagnosticInfo）
│   └── Resources/                          — 诊断消息资源（DiagnosticResources.resx + zh-CN）
│
├── NCMapper.FreeSql/                       — FreeSql 投影扩展包
│   ├── NCMapperFreeSqlExtensions.cs          — 查询扩展：AsDto 终端操作 + 自动降级 + 多表 Compose + 分页 + 树形/字典投影 + 带映射转手（partial 主文件）
│   │   ├── NCMapperFreeSqlExtensions.Compose.cs — Compose 多表合成子路径
│   │   └── NCMapperFreeSqlExtensions.Support.cs — 分页/树形/字典支持子路径
│   ├── NCMapperFreeSqlGuardExtensions.cs     — 编译期守护：对不兼容查询链（如 GroupBy）提供编译期错误提示
│   ├── FreeSqlProjectionBuilder.cs           — 投影表达式构建器：生成 ORM 兼容 Projection 表达式（单/双/三表 Compose）
│   ├── FreeSqlDegradationDetector.cs         — 降级检测：判断是否需降级为二步映射（FreeSql 嵌套投影上限 3 层）
│   ├── FreeSqlNavigationCollector.cs         — 导航路径收集器：分析 DTO 跨表依赖，产出导航树供降级路径 IncludeByPropertyName
│   └── Internal/ComposeProjectionKernel.cs   — Compose 投影内核（多表 Join 合成）
│
├── NCMapper.EFCore/                        — EF Core 投影扩展包
│   ├── NCMapperEFCoreExtensions.cs           — 查询扩展：AsDto 终端操作 + 自动降级 + 分页 + 字典/查找 + 带映射转手
│   ├── NCMapperEFCoreGuardExtensions.cs      — 编译期守护：GroupBy 后 / 已脱离 IQueryable 后不可用 AsDto 的编译期错误提示
│   ├── EfCoreProjectionBuilder.cs            — 投影表达式构建器：生成 `Expression<Func<TSource,TTarget>>`（含命名约定扁平化、集合导航投影）
│   ├── EfCoreDegradationDetector.cs          — 降级检测：IProjectionPolicy 驱动判断是否需降级为二步映射
│   └── EfCoreNavigationCollector.cs          — 导航路径收集器：分析 DTO 跨表依赖，产出导航树供降级路径自动 Include
│
├── NCMapper.Dapper/                        — Dapper 适配扩展包
│   ├── NCMapperDapperExtensions.cs           — AsDto 统一入口扩展
│   ├── DapperDtoSelector.cs                 — 终端方法选择器（Query/ToPagedList/ToTreeList/QueryMultiple 等，partial 主文件）
│   │   ├── DapperDtoSelector.Execution.cs    —   执行子路径
│   │   ├── DapperDtoSelector.MultiResult.cs  —   多结果集子路径
│   │   ├── DapperDtoSelector.Paging.cs       —   分页子路径
│   │   └── DapperDtoSelector.Tree.cs         —   树形子路径
│   ├── ISqlDialect.cs                       — SQL 方言接口（CountSql / PageSql）
│   └── SqlDialects.cs                       — 6 内置方言 + SqlDialectResolver 自动检测
```

---

