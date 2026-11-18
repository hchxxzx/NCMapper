---
wiki-id: reference/samples-index
库版本: v1.0.0+
更新时间: 2026-10-23
---

# samples 场景映射表（docs ↔ samples 唯一映射源）

> 本表为文档引用 samples 的**唯一权威映射**：how-to 页禁止直接引用 samples 行号区间与目录路径，一律经本表条目（region 直名引用）。场景或文件变更时仅更新本表。场景总数 = 注册表实场景数 **23**（07-Aot 为域占位非场景，不计）。

| 文档域/篇 | 场景号 | 场景名 | 源文件 | 关键 region |
|-----------|--------|--------|--------|-------------|
| how-to/fluent/basics.md；tutorials/fluent-modeling-tutorial.md | 101 | Fluent 基础链 | `~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs` | FluentBasics_ChainOrderMapper、FluentBasics_TreeNodeMapper |
| how-to/fluent/basics.md；how-to/fluent/convert.md；tutorials/fluent-modeling-tutorial.md | 102 | Fluent Convert | `~/samples/NCMapper.Samples/01-Fluent/CvModels.cs` | FluentConvert_Convert、FluentConvert_ConvertBack、FluentConvert_FromJson、FluentConvert_Ignore |
| how-to/fluent/from-source.md | 103 | Fluent FromSource | `~/samples/NCMapper.Samples/01-Fluent/FsModels.cs` | FluentFromSource_Bindings |
| how-to/fluent/hooks.md；reference/features.md | 104 | Fluent Hooks | `~/samples/NCMapper.Samples/01-Fluent/FluentHooksScenarios.cs`、`~/samples/NCMapper.Samples/01-Fluent/HookModels.cs` | FluentHooks_AttrHookDto、FluentHooks_HookFluentMapper、FluentHooks_HookHostAttrMethods、FluentHooks_Run |
| how-to/fluent/terminals.md；reference/features.md；tutorials/fluent-modeling-tutorial.md | 105 | MapWhen | `~/samples/NCMapper.Samples/01-Fluent/MapWhenScenarios.cs`、`~/samples/NCMapper.Samples/01-Fluent/MwModels.cs` | MapWhen_MapWhenFluentMapper、MapWhen_MwAttrItemDto、MapWhen_Run |
| how-to/fluent/terminals.md；tutorials/fluent-modeling-tutorial.md；explanation/mechanics/constructors-and-types.md | 106 | MapCtorParam | `~/samples/NCMapper.Samples/01-Fluent/CpModels.cs` | CtorParam_CtorParamFluentMapper、CtorParam_RecordCtorTour |
| how-to/fluent/terminals.md；reference/features.md | 107 | MapSubclass | `~/samples/NCMapper.Samples/01-Fluent/ScModels.cs`、`~/samples/NCMapper.Samples/01-Fluent/SubclassScenarios.cs` | MapSubclass_Dispatch、MapSubclass_ScAttrAnimalDto |
| how-to/fluent/terminals.md | 108 | 内联 lambda 转录运行对比 | `~/samples/NCMapper.Samples/01-Fluent/InlineLambdaScenarios.cs` | FluentInline_Passthrough、FluentInline_CapturedExtracted |
| reference/architecture-map.md；how-to/fluent/from-source.md；how-to/fluent/terminals.md；how-to/declarative/map-format.md | 109 | Compose 运行时元数据（RuntimeComposeMapping）+ 新语法叠加 | `~/samples/NCMapper.Samples/01-Fluent/RuntimeComposeScenarios.cs` | RuntimeCompose_Declarations、RuntimeCompose_Run |
| getting-started/overview.md；getting-started/tutorial-15min.md；getting-started/quickstart.md；how-to/declarative/basics.md；how-to/mapping/map-list-into.md；tutorials/mapping-tutorial.md；explanation/mechanics/callbacks-collections.md；explanation/source-generator.md | 201 | 基础映射（Attribute） | `~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs` | BasicMapping_SimpleNameMatch、BasicMapping_ListMap、BasicMapping_MapListInto、BasicMapping_MapWithAction、BasicMapping_UpdateExisting、BasicMapping_MapIgnore、BasicMapping_ForwardMap、BasicMapping_SourceGenExtension、BasicMapping_MapListIntoReuse、BasicMapping_DotPath、BasicMapping_ReverseExtensions、Core_Generated_EntryExtensions、Declarative_ReverseMap_Pair、Declarative_ReverseMap_New、BasicMapping_ReverseDirectExtension、BasicMapping_ReverseDirectUpdate |
| how-to/declarative/map-format.md；how-to/fluent/format.md；reference/features.md | 202 | MapFormat 属性级格式化 | `~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs` | MapFormat_AttrChannel、MapFormat_FluentChannel |
| explanation/mechanics/nested-mapping.md | — | 嵌套映射控制场景集（文档演示专用，非注册场景） | `~/samples/NCMapper.Samples/02-Declarative/NestedScenarios.cs` | Core_Nested_InlineDepthTune、Core_Nested_InvoiceFlatTour |
| how-to/compose/compose.md；tutorials/compose-tutorial.md；explanation/compose.md；how-to/infrastructure/pipeline.md | 301 | 多源合成 | `~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs` | Compose_BasicCompose、Compose_ComposeList、Compose_L60、Compose_L83、Compose_ComposeWithPostMap、Compose_ComposeListWithAction、Compose_BasicCompose_MultiSourceCompose、Compose_BasicCompose_ComposeWithAction |
| how-to/deep-copy/deep-copy.md；tutorials/deepcopy-tutorial.md；explanation/mechanics/callbacks-collections.md；how-to/infrastructure/performance.md | 401 | 深拷贝 | `~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs` | DeepCopy_DeepCopySameType、DeepCopy_DeepCopyFast、DeepCopy_DeepCopyExpressionTree、DeepCopy_DeepCopyJson、DeepCopy_MaxRecursionDepth |
| how-to/orm/efcore-projection.md；how-to/orm/freesql-single-table.md；how-to/orm/freesql-advanced.md；tutorials/orm-projection-tutorial.md；explanation/orm-efcore-dapper.md；explanation/orm-freesql.md；explanation/orm-projection-design.md；how-to/orm/dapper-query.md | 501 | ORM 投影（FreeSql/EFCore） | `~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs`、`~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs`、`~/samples/NCMapper.Samples/05-Orm/FxScenarios.cs` | OrmProjection_SingleTableAsDto、OrmProjection_SingleTablePaged、OrmProjection_TreeList、OrmProjection_FluentDualMode、OrmProjection_FluentTree、OrmProjection_NestedCrossTable、OrmProjection_ToLookup、OrmProjection_ToDictionary、OrmProjection_NoAutoIncludeManual、OrmProjection_GroupByNativeAggregate、OrmProjection_GroupByInMemoryLinq、OrmProjection_ToChunkDto、OrmProjection_MultiTableTwoWay、OrmProjection_MultiTableThreeWay、OrmProjection_IncludeNavigation、OrmProjection_WithSqlAsDto、OrmProjection_TwoTableWithSql、OrmProjection_WhereIfFilter、OrmProjection_AnySubquery、OrmProjection_NoAutoIncludeGroup、OrmProjection_ToTreeList、OrmProjection_ToDictionaryAsync、OrmProjection_EFAsyncTerminals、OrmProjection_AsTrackingExplicit、OrmProjection_FreeSqlAsyncTerminals、OrmProjection_FreeSqlPagedAsync |
| how-to/orm/dapper-query.md；how-to/orm/dapper-advanced.md；explanation/orm-efcore-dapper.md | 502 | Dapper 集成 | `~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs` | Dapper_Query、Dapper_Advanced、Dapper_QueryFirst、Dapper_QueryFirstOrDefault、Dapper_QuerySingle、Dapper_QuerySingleOrDefault、Dapper_QueryAsync、Dapper_QueryFirstOrDefaultAsync、Dapper_Advanced_QueryMultiple、Dapper_Advanced_QueryMultiple3、Dapper_Advanced_QueryUnbufferedAsync、Dapper_Advanced_ToChunkDtoAsync、Dapper_Advanced_PagedWithDialect |
| how-to/orm/freesql-multi-table.md；how-to/orm/freesql-advanced.md；explanation/orm-projection-design.md；explanation/orm-freesql.md | 503 | FreeSql 多表与高级终端 | `~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs`、`~/samples/NCMapper.Samples/05-Orm/MultiTableModels.cs` | FreeSqlMultiTable_TwoTableJoin、FreeSqlMultiTable_IncludeManyNavigation、FreeSqlAdvanced_ToChunkDtoAsync、FreeSqlAdvanced_ToLookup、FreeSqlMultiTable_FluentProjection、FreeSqlMultiTable_TwoTableFirst |
| how-to/infrastructure/pipeline.md | 601 | Pipeline | `~/samples/NCMapper.Samples/06-Infrastructure/PipelineScenarios.cs` | Pipeline_PostMapAudit、Infra_Pipeline_PostMapOrder、Infra_Pipeline_ClearAll、Infra_Pipeline_CrossCutOnce |
| how-to/infrastructure/validation.md；explanation/validation.md；reference/localization.md | 602 | Validation 与诊断 | `~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs` | Validation_ValidationScenarios、Infra_Validation_ExceptionFields、Validation_CultureBilingualTour |
| how-to/infrastructure/di.md；tutorials/web-integration-tutorial.md；reference/configuration.md | 603 | DI 集成 | `~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs` | DI_ResolveMapper、DI_ResolveMapperWithOptions、DI_ConfigSectionBind |
| how-to/infrastructure/performance.md；reference/core-api.md | 604 | 性能 | `~/samples/NCMapper.Samples/06-Infrastructure/PerformanceScenarios.cs` | Performance_MeasureMapPerformance、Core_Perf_GeneratedHotPath、Infra_Perf_MetricsObserve、Core_Perf_ColdStartAfterClear、Core_Api_GeneratedProbe |
| how-to/infrastructure/masking.md；tutorials/masking-tutorial.md；reference/core-api.md；reference/features.md | 605 | 脱敏 | `~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs` | Mask_ConvertUsing、Mask_BuiltinNine、Mask_CustomProvider、Mask_MultiLocaleSwap、Infra_Api_AuditMaskCombo、Mask_JapanPhoneMask_Mask、Mask_MaskedUserMap、Mask_MaskCustomRule_Mask、Mask_MaskingCustomerGet、Mask_MaskingSmokeTour |
| how-to/fluent/basics.md | — | Fluent 三段式链配置载体（文档演示专用，非注册场景） | `~/samples/NCMapper.Samples/01-Fluent/FluentBasicsScenarios.cs` | FluentBasics_OrderChain_Configure |
| how-to/infrastructure/global-config.md；reference/configuration.md | 606 | 全局配置 | `~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs` | GlobalConfig_MapGlobal、GlobalConfig_StrategyAndIgnore、GlobalConfig_FluentInteract |
| getting-started/tutorial-15min.md、tutorials/compose-tutorial.md（共享模型定义引用） | — | 共享模型 | `~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs`、`~/samples/NCMapper.Samples/Models/Common/Dtos/UserDto.cs`、`~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs` | Common_UserEntity、Common_UserDto、Common_OrderDetailDto |
| explanation/tracing.md；how-to/infrastructure/validation.md | 607 | 映射追踪 | `~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs` | Trace_GlobalEnabled、Trace_PerCallOptions、Trace_OutputToFile、Infra_Tracer_DeepCopyProbe |
| reference/security.md | — | 安全阈值调整（文档演示专用，非注册场景） | `~/samples/NCMapper.Samples/06-Infrastructure/SecurityScenarios.cs` | Core_Security_RecursionDepth |

## 占位域

- **07-Aot**：域占位（README 承载），不入本表计数；AOT 运行演示见 `samples/NCMapper.AotSample/`。

## 引用契约

1. 文档内 DFM 引用统一形态：`[!code-csharp] 标记 + ]( 内以 ~/samples/NCMapper.Samples/<域目录>/<文件>.cs 为目标、片段为 #<RegionName> 直名（title 必填）`（region 直名，title 必填）。
2. 行号区间（`#L`）为已归零的过渡形态，本表不再登记。
3. 场景号新编为三位数域编（域号×100+序号）；非法场景号 = 注册表未命中（退出码 2）。
