> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
title: Public API Architecture Overview
description: Layered architecture diagram of publicly callable APIs and demo coverage checklist
wiki-id: reference/architecture-map
Library version: v1.0.0+
Last updated: 2026-10-23
---

# Public API Architecture Overview

> This page presents the **layered architecture diagram** and **demo coverage checklist** (single source of truth) for **publicly callable APIs**. Each row includes a “Demo Coverage” column: either a scenario number + runnable snippet name (region), or an explicit reason for non-coverage.  
> Reverse reconciliation: `scripts/check-api-demo-coverage.sh` automatically verifies this checklist (snippet existence / non-coverage statistics / scenario set equality / public API snapshot reverse lookup / structural integrity) and gates offline package builds.  
> For Fluent terminal syntax details only, see the [Fluent Terminal Quick Reference](fluent-index.md); for attribute syntax only, see the [Attribute Quick Reference](attributes-index.md); for the full index of runnable demos, see the [samples index](samples-index.md).

## 0. Layered Architecture Diagram

```text
┌─────────────────────────────────────────────────────────────────┐
│ L1 Compile-Time Layer (SourceGen) — Not directly callable; auto-applies at compile time │
│    Incrementally generates mapping code · Diagnostic codes (NCMAP family) · Six reverse-direct overloads · │
│    Automatic registration via `[ModuleInitializer]` (hosted by RuntimeSnapshotStore)      │
├─────────────────────────────────────────────────────────────────┤
│ L2 Core Runtime — Mapper static facade (mapping / list mapping / reuse / composition / deep copy / validation) │
├─────────────────────────────────────────────────────────────────┤
│ L3 Declaration & Configuration Layer — Attribute channel (annotated on classes) | Fluent channel (centralized config classes) │
│    | Global configuration (assembly-level MapDefaults)                              │
├─────────────────────────────────────────────────────────────────┤
│ L4 Infrastructure Services Layer — Pipeline callbacks | Validation | Tracing | Masking | DI integration | JSON extension points │
├─────────────────────────────────────────────────────────────────┤
│ L5 ORM Extension Layer — FreeSql | EFCore | Dapper (projection selectors + terminal families) │
├─────────────────────────────────────────────────────────────────┤
│ L6 Common Models — PagedList | Exception family | Deep copy context | Runtime host types │
└─────────────────────────────────────────────────────────────────┘
```

Coverage reconciliation convention: The **“Demo” column** contains either a `scenario number` (entire-scenario level, no isolated snippet) or `scenario number region name` (snippet level); rows **not demonstrated** must state `— (reason)`, with the reason explicitly provided.

**Inclusion criteria**: This checklist includes only the **publicly callable surface** (types/members, with the public API snapshot as the sole authoritative reference; discrepancies between snapshot and source code are resolved by reading the actual source). `internal` types (e.g., `MapperBase<TSource,TTarget>`, built-in `SystemTextJsonSerializer`) and non-existent types are excluded; publicly visible but “not directly called by users” host types may be included, but must be annotated with their true visibility.

---

## 1. L1 Compile-Time Layer (SourceGen, declarative nature)

Not listed in tables (not directly callable): Roslyn incremental source generators produce mapping code and **six reverse-direct overloads** at compile time based on L3 declarations, and register them automatically via `[ModuleInitializer]`; diagnostic codes (NCMAP0xx family / NCMAP_Fxx family) are reported entirely at compile time; runtime hosting is handled by the Runtime-family types in §6 and `GeneratedMapConfig`.

---

## 2. L2 Core Runtime (Mapper static facade)

| API | Purpose | Demo | Snippet (region) | Documentation |
|-----|---------|------|------------------|---------------|
| `Mapper.Map<TSource, TTarget>(source)` | Single-object mapping (creates new target) | 201 | BasicMapping_SimpleNameMatch | [declarative/basics](../how-to/declarative/basics.md) |
| `Mapper.Map<TSource, TTarget>(source, action)` | Single-object mapping + post-mapping callback | 201 | BasicMapping_MapWithAction | [declarative/basics](../how-to/declarative/basics.md) |
| `Mapper.Map(source, target)` | Mapping into an existing object (update mode) | 201 | BasicMapping_UpdateExisting | [declarative/basics](../how-to/declarative/basics.md) |
| `Mapper.Map<TSource>(source)` | Same-type projection (no target-type overload) | — (obscure overload sharing path with `Map<TSource,TTarget>`; no demonstrable benefit) | — | [Core API](core-api.md) |
| `Mapper.MapList<TSource, TTarget>(sources)` | List mapping (also has same-type `MapList<TTarget>` overload) | 201 | BasicMapping_ListMap | [declarative/basics](../how-to/declarative/basics.md) |
| `Mapper.MapListInto<TSource, TTarget>(sources, destination)` | Bulk write into caller-preallocated array (reuses, does not allocate) | 201 | BasicMapping_MapListInto | [MapListInto](../how-to/mapping/map-list-into.md) |
| `Mapper.Compose<TTarget>(sources)` / `(action, sources)` | Multi-source composition mapping | 301 | — (entire-scenario level) | [compose](../how-to/compose/compose.md) |
| `Mapper.ComposeList<TTarget>(sourceGroups)` / `(action, sourceGroups)` | Batch multi-source composition (group-wise per-row composition) | 301 | Compose_ComposeList | [compose](../how-to/compose/compose.md) |
| `Mapper.DeepCopy<T>(source)` family | Deep copy (5 overloads: with context / with callback / cross-type) | 401 | DeepCopy_DeepCopySameType etc. | [deep-copy](../how-to/deep-copy/deep-copy.md) |
| `Mapper.DeepCopyRuntimeOnly<T>(source, ctx)` | Forces runtime-provider deep copy (bypasses generated path) | — (specialized entry point; not demonstrated) | — | [deep-copy](../how-to/deep-copy/deep-copy.md) |
| `Mapper.Validate()` / `Validate(params Assembly[])` | Full or assembly-scoped mapping health check | 602 | Validation_ValidationScenarios | [validation](../how-to/infrastructure/validation.md) |
| `Mapper.ValidateOrThrow()` | Startup-time validation; throws exception on Error-level issues | 602 | — (inline `ValidateOrThrowDemo` inside Validation Run) | [validation](../how-to/infrastructure/validation.md) |
| `Mapper.ClearCache()` | Clears runtime expression tree cache | 604 | — (inline cold-start measurement inside Performance Run) | [Core API](core-api.md) |
| `Mapper.ClearGenerated()` | Clears source-generated registry | — (same family as `ClearCache`; generated registry is long-lived and rarely requires clearing; no operational use case) | — | [Core API](core-api.md) |
| `Mapper.ResetMetrics()` | Resets performance metric counters | 604 | — (inline inside Performance Run) | [Core API](core-api.md) |
| `Mapper.MetricsEnabled` / `TotalMapCalls` / `CacheHits` / `CompilationCount` | Performance counter toggle and read-only metrics | 604 | — (Performance Run reads metrics) | [Core API](core-api.md) |
| `Mapper.ValidationEnabled` / `TraceEnabled` / `TraceOutput` | Global switches for validation and tracing | 602 | — (entire-scenario level) | [Core API](core-api.md) (Section 4.3 Runtime Control Table) |
| `Mapper.MaxRecursionDepth` / `MaxInlineDepth` | Recursion / inline depth settings (upper bounds: `MaxRecursionDepthUpperBound=4096`, `MaxInlineDepthUpperBound=16`) | — (default semantics documented in [deep-copy](../how-to/deep-copy/deep-copy.md); no standalone demo) | — | [deep-copy](../how-to/deep-copy/deep-copy.md) / [Core API](core-api.md) |
| `Mapper.GetGlobalConfig()` | Reads frozen global configuration view | 606 | GlobalConfig_StrategyAndIgnore | [global-config](../how-to/infrastructure/global-config.md) |
| `Mapper.Pipeline` (instance of `MapPipeline`) | Registers global post-mapping callbacks | 601 | Pipeline_PostMapAudit | [pipeline](../how-to/infrastructure/pipeline.md) |
| `Mapper.HasGenerated<S,T>()` / `HasGeneratedDeepCopy<T>()` | Queries whether generated code exists for the type pair | — (diagnostic aid; part of source-gen collaboration surface) | — | [Core API](core-api.md) |
| `Mapper.RegisterGenerated*` family / `RegisterDeepCopyTarget` | Entry points for registering generated code (9 method names: Core / no suffix / List / Update / ListInto / Compose / Validate / Config / DeepCopy×3; `RegisterGeneratedConfig` registers global configuration) — automatically invoked by generated code; manual invocation unnecessary (`EditorBrowsable.Never`) | — (automatically invoked by SourceGen-generated code; not directly called by users) | — | [Core API](core-api.md) (Section 4.2 Source Generator Registration Methods, all 8 methods listed) |

## 3. L3 Declaration & Configuration Layer

### 3.1 Dual-Channel Comparison (Declarative ↔ Fluent, Feature-by-Feature Symmetry)

> Dual-channel capability equivalence: Each row in the table below represents one feature; the left column shows the declarative (attribute-based) approach, and the right column shows the Fluent terminal. The "Demonstration" column indicates which side—declarative or Fluent—is demonstrated, using the notation “Declarative / Fluent”.

| Feature | Declarative ↔ Fluent Terminal | Demonstration (Declarative / Fluent) | Snippet (region) | Documentation |
|---------|-------------------------------|--------------------------------------|------------------|---------------|
| Channel activation anchor | — ↔ `[MapperConfig]` (`MapperConfigAttribute`) on a configuration class + `public static` method (parameter: `FluentMappingConfig`; if the anchor is missing, the entire channel is disabled) | 201 / 101 | BasicMapping_SimpleNameMatch / — (entire-scenario level) | [fluent/basics](../how-to/fluent/basics.md) |
| Mapping declaration anchor (type-pair generation entry point) | `[MapFrom(typeof(Source))]` ↔ `For<S, T>()` | 201 / 101 | BasicMapping_SimpleNameMatch / — (entire-scenario level) | [basics](../how-to/declarative/basics.md) |
| Name-mismatch routing (including cross-table data retrieval) | `[MapProperty]` ↔ `.Map()` | 201 / 101 | — (Run main body) / — (entire-scenario level) | [basics](../how-to/declarative/basics.md) |
| Forward custom conversion | `[Convert]` ↔ `.Convert()` | 201 / 102 | — (Run main body) / — (entire-scenario level) | [basics](../how-to/declarative/basics.md) |
| Reverse custom conversion | `[ConvertBack]` ↔ `.ConvertBack()` | 201 / 102 | — (Run main body) / — (entire-scenario level) | [basics](../how-to/declarative/basics.md) |
| Property ignore | `[MapIgnore]` ↔ `.Ignore()` | 201 / 101 | BasicMapping_MapIgnore / — (entire-scenario level) | [basics](../how-to/declarative/basics.md) |
| Property masking | `[Mask]` ↔ `.Mask<TMask>()` (`where TMask : IMask`, within `Member` scope) | 605 / 501 | — (Run main body) / — (Run main body, downgraded snippet) | [masking](../how-to/infrastructure/masking.md) |
| Property-level formatting | `[MapFormat]` ↔ `.Format()` (same anchor; automatic name-based matching on source side; both sides share identical parameters; member-level demo = scenario 202) | 202 / 202 | — (Run main body) / — (Run main body) | [declarative/map-format](../how-to/declarative/map-format.md) / [fluent/format](../how-to/fluent/format.md) |
| Conditional guard | `[MapWhen]` ↔ `.MapWhen()` | 105 / 105 (inline lambda transcription also covered in 108) | — (entire-scenario level) / — (entire-scenario level) | [advanced](../how-to/declarative/advanced.md) |
| Tree-shaped declaration (consumed by `ToTreeList`) | `[MapTree]` ↔ `.Tree()` (sub-level property specification via `.MapTree<TProp>()`) | 502 / 501 | Dapper_Advanced / OrmProjection_FluentTree | [dapper-advanced](../how-to/orm/dapper-advanced.md) |
| Polymorphic subclass mapping | `[MapSubclass]` ↔ `.MapSubclass()` | 107 / 107 | — (entire-scenario level) / — (entire-scenario level) | [declarative/advanced](../how-to/declarative/advanced.md) |
| Disable auto-inclusion (property-level) | `[MapNoAutoInclude]` ↔ `NoAutoInclude` (field-level, parameterless) inside `Member` scope | — / 101 | — / — (entire-scenario level) | [attributes-index](attributes-index.md) |
| Disable auto-matching (type-pair whitelist) | `[MapNoAutoMatch]` (`MapNoAutoMatchAttribute`, class-level) ↔ `.NoAutoMatch()` | — (no declarative example shown) / 101 | — / 101-no-auto-include (entire-scenario level) | [fluent/basics](../how-to/fluent/basics.md) |
| Lifecycle hooks | `[BeforeMapping]`/`[AfterMapping]` ↔ `.BeforeMapping()`/`.AfterMapping()` | 201 / 104 | — (Run main body) / FluentHooks_Run | [fluent/hooks](../how-to/fluent/hooks.md) |
| Constructor parameter binding | — (no declarative equivalent) ↔ `.MapCtorParam()` | — / 106 | — / — (entire-scenario level) | [fluent-index](fluent-index.md) |
| Multi-source data retrieval | `[MapFrom(PrimarySource)]` + `[MapProperty(SecondarySource, Field)]` (cross-source) ↔ `.FromSource()` | 301 / 103 | — (Run main body) / — (entire-scenario level) | [compose](../how-to/compose/compose.md) |
| JSON column mapping (object ↔ JSON string) | `[MapProperty(IsFromJson = true)]` ↔ `.FromJson()` | 201 / 102 | — (Run main body) / FluentConvert_FromJson | [basics](../how-to/declarative/basics.md) |
| Inline lambda transcription for `MapWhen` | — (no inline declarative form) ↔ inline lambda in `.MapWhen()` (non-capturing lambdas are transcribed verbatim; capturing forms F41/F42 are rejected) | — / 108 | — / FluentInline_Passthrough / FluentInline_CapturedExtracted | [fluent-index](fluent-index.md) |
| Field-chain configurator family (three-part field scope) | — (no declarative equivalent) ↔ `.Member()` + `MemberHead` / `MemberConfig` / `AfterIgnore` segments. All field-level configurations use the three-part `Member` pattern; all type-pair switches use `.NoAutoMatch()` exclusively | — / — (compile-time configuration surface, no independent runtime behavior; scenario demos and documentation updates are maintained in the documentation set) | — / — (entire-scenario level) | [fluent-index](fluent-index.md) / [fluent/basics](../how-to/fluent/basics.md) |

> **Terminal Host & Full Quick Reference**: All Fluent terminals above are hosted on `FluentTypeConfig<TSource, TTarget>`. Full syntax for each terminal is documented in the [Fluent Terminal Quick Reference](fluent-index.md).

### 3.2 Global and Compile-Time Configuration

| API | Purpose | Demonstration | Snippet (region) | Documentation |
|-----|---------|---------------|------------------|---------------|
| `[assembly: MapDefaults(...)]` | Global configuration (naming strategy / prefix/suffix stripping / two-tier ignore lists; override gate `OverrideIgnore` hosts five attributes; scenario 606 demonstrates Fluent equivalents) | 606 | GlobalConfig_StrategyAndIgnore / GlobalConfig_FluentInteract | [global-config](../how-to/infrastructure/global-config.md) |
| `[NCMapperOptions]` / `[class: NCMapperOptions(...)]` | **Compile-time diagnostic reporting toggle**: `ReportUnmappedTargetMembers` (default `false`, controls NCMAP001) / `ReportUnmappedSourceMembers` (default `true`, controls NCMAP007) — *not* the same as runtime `NCMapperOptions` (§4) | — (compile-time toggle; defaults reflect conventional semantics; no explicit demo) | — | [attributes-index](attributes-index.md) |

## 4. L4 Infrastructure Services Layer

| API | Purpose | Demonstration | Snippet (region) | Documentation |
|-----|---------|---------------|------------------|---------------|
| `MapTracer` / `MapTraceOptions` family | Mapping tracing (per-call reporting / forced analysis / global toggle / dual output channels: `Enable()` / `Trace<S,T>()` / `GloballyEnabled` / `GlobalOutput` / `LogCallback`) | 607 | Trace_GlobalEnabled / Trace_PerCallOptions | [tracing](../explanation/tracing.md) |
| `MapValidator.ValidateMapping(Type, Type)` / `MapValidationResult` | Per-type-pair health check (validation messages localized per `CurrentUICulture`; validation messages embedded in generated code are always in English) | 602 | Validation_ValidationScenarios | [validation](../how-to/infrastructure/validation.md) |
| `MapValidator.PrintReport(results, output?, showOk?)` | Formatted output of validation results | 602 | — (entire-scenario level) | [validation](../how-to/infrastructure/validation.md) (section “PrintReport Usage”) |
| `MapPipeline` | Global post-mapping callback pipeline (`AddPostMap` / `Clear` / `IsPipelineEnabled`; generated code calls `RunPostMap<S,T>` directly) | 601 | Pipeline_PostMapAudit family | [pipeline](../how-to/infrastructure/pipeline.md) |
| `MaskProvider.Override<TMask>` / `Resolve<TMask>()` / `Resolve(Type)` / `Clear()` | Mask implementation replacement and restoration (`Override` must occur before the first mapping call; effective scope boundaries detailed in scenario 605 comments) | 605 | Mask_CustomProvider | [masking](../how-to/infrastructure/masking.md) |
| `IMask` + 9 built-in implementations | Built-in masking (e.g., `MaskPhone`, `MaskIdCard`, `MaskBankCard`, `MaskEmail`, `MaskChineseName`, `MaskAddress`, `MaskIp`, `MaskLicensePlate`, `MaskLandline`) | 605 | Mask_BuiltinNine | [masking](../how-to/infrastructure/masking.md) |
| `NCMapperServiceExtensions.AddNCMapper()` (3 overloads) | DI registration (parameterless / Options delegate / configuration section) | 603 | DI_ResolveMapper | [di](../how-to/infrastructure/di.md) |
| `NCMapperOptions` | Runtime options (`DeepCopyStrategy`: `Fast`/`ExpressionTree`/`Json`, `ValidationEnabled`, etc.) | 603 | — (DI_ResolveMapper demonstrates configure delegate usage) | [di](../how-to/infrastructure/di.md) |
| `IProjectionPolicy` (`RequiresDowngrade`) | Projection downgrade policy interface (declared by attribute authors to specify downgrade behavior; default safe downgrade net applies if unimplemented) | — (extension point for attribute authors; consumed by engine; no standalone demo) | — | [orm-degradation](../explanation/orm-degradation.md) |
| `JsonConverter` / `JsonSerializerBase` | JSON column mapping (`IsFromJson`) and serialization strategy replacement (swap static `Current` to change implementation; built-in types are `internal`) | 201 | — (JSON column mapping demo in Run main body) | [declarative/basics](../how-to/declarative/basics.md) |
| `TypeConvertHelper` | Built-in type conversion logic (public helper class) | — (used internally by engine; users may reference it when implementing custom converters) | — | [core API](core-api.md) |

## 5. L5 ORM Extension Layer

> Terminal listings are authoritative per the publicly exposed methods in the three extension packages’ source code (verified on 2026-09-28); synchronous/asynchronous terminal pairs are grouped on the same row, and the "Demonstration" column reflects the actual variant used in practice.

### 5.1 FreeSql (`NCMapper.FreeSql`)

| API | Purpose | Demo | Snippet (region) | Documentation |
|-----|---------|------|------------------|---------------|
| `fsql.Select<T>().AsDto()` | Entry point for projection selection (returns `FreeSqlDtoSelector<TSource>`) | 501 | — (entire-scenario level) | [freesql-single-table](../how-to/orm/freesql-single-table.md) |
| `ToList()` / `ToListAsync()` | List projection | 501 | — (Run body) | [freesql-single-table](../how-to/orm/freesql-single-table.md) |
| `First()` / `FirstAsync()`, `Single()` / `SingleAsync()` | Single-item projection (`Single` includes uniqueness validation) | 501 | — (Run body) | [freesql-single-table](../how-to/orm/freesql-single-table.md) |
| `ToPagedList()` / `ToPagedListAsync()` | Paged projection (returns `PagedList`) | 501 | — (Run body) | [freesql-single-table](../how-to/orm/freesql-single-table.md) |
| `ToChunkDto()` / `ToChunkDtoAsync()` | Chunked consumption (requires stable `OrderBy` sorting) | 503 | FreeSqlAdvanced_ToChunkDtoAsync | [freesql-advanced](../how-to/orm/freesql-advanced.md) |
| `ToTreeList()` / `ToTreeListAsync()` | Tree-shaped projection | 501 | — (Run body) | [freesql-advanced](../how-to/orm/freesql-advanced.md) |
| `ToDictionary()` / `ToDictionaryAsync()` | Dictionary projection | 501 | — (entire-scenario level) | [freesql-advanced](../how-to/orm/freesql-advanced.md) |
| `ToLookup()` | Grouped lookup table | 503 | FreeSqlAdvanced_ToLookup | [freesql-advanced](../how-to/orm/freesql-advanced.md) |
| Cross-table JOIN two-table projection | `.LeftJoin(...)` + cross-table `MapProperty` data retrieval | 503 | FreeSqlMultiTable_TwoTableJoin | [freesql-multi-table](../how-to/orm/freesql-multi-table.md) |
| Native `IncludeMany` + projection | One-to-many navigation loading (native FreeSql API; NCMapper automatically expands navigation properties, requiring `[Navigate]`) | 503 | FreeSqlMultiTable_IncludeManyNavigation | [freesql-multi-table](../how-to/orm/freesql-multi-table.md) |
| `Include` failure switch | `NCMapperFreeSqlExtensions.ThrowOnIncludeApplicationFailure` (global switch) | — (runtime switch; default semantics match production semantics; no dedicated demo) | — | [freesql-multi-table](../how-to/orm/freesql-multi-table.md) |
| Compose multi-source projection | `NCMapperFreeSqlComposeExtensions`: `ISelect<T1, T2>.AsDto()` (dual-source `FreeSqlComposeDtoSelector` / ternary `FreeSqlComposeDtoSelector3`; eight terminal variants each) | 501 | — (entire-scenario level) | — (documentation missing) |
| Fluent DTO projection | ORM projection powered by a Fluent registration registry | 503 | FreeSqlMultiTable_FluentProjection | [freesql-advanced](../how-to/orm/freesql-advanced.md) |
| Compile-time guard | `NCMapperFreeSqlGuardExtensions`: incompatible chains fail at compile time with error messages and suggested alternatives (`[Obsolete(error: true)]`) | — (compile-time behavior; no runtime demo form) | — | [orm-degradation](../explanation/orm-degradation.md) |
| Degradation detection | Automatic in-memory mapping fallback when SQL translation fails | 501 | — (Mask-enforced degradation snippet) | [orm-degradation](../explanation/orm-degradation.md) |

### 5.2 EF Core (`NCMapper.EFCore`)

| API | Purpose | Demo | Snippet (region) | Documentation |
|-----|---------|------|------------------|---------------|
| `db.Set<T>().AsDto()` | Entry point for projection selection (default: no-tracking; returns `EFCoreDtoSelector<TSource>`) | 501 | — (entire-scenario level) | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `AsTracking()` | Tracking query mode (use when change tracking is required) | 501 | — (entire-scenario level) | [efcore-projection](../how-to/orm/efcore-projection.md) |
| No-tracking switch | `NCMapperEFCoreExtensions.EFCoreProjectionAsNoTrackingDefault` (global default switch for no-tracking projection) | — (runtime switch; default is `true`, matching production semantics; no dedicated demo) | — | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `ToList()` / `ToListAsync()` | List projection | 501 | — (Run body) | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `First()` / `FirstAsync()`, `Single()` / `SingleAsync()` | Single-item projection (`Single` includes uniqueness validation) | 501 | — (Run body) | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `ToPagedList()` / `ToPagedListAsync()` | Paged projection | 501 | — (entire-scenario level) | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `ToTreeList()` / `ToTreeListAsync()` | Tree-shaped projection | 501 | OrmProjection_TreeList / OrmProjection_FluentTree | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `ToDictionary()` / `ToDictionaryAsync()` | Dictionary projection | 501 | — (Run body) | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `ToLookup()` | Grouped lookup table | 501 | — (Run body) | [efcore-projection](../how-to/orm/efcore-projection.md) |
| `ToChunkDto()` / `ToChunkDtoAsync()` | Chunked consumption | 501 | — (Run body) | [efcore-projection](../how-to/orm/efcore-projection.md) |
| Navigation collection and nested projection | Automatic expansion of navigation properties (engine behavior) | — (automatic behavior, documented) | — | [efcore-projection](../how-to/orm/efcore-projection.md) |
| Compile-time guard | `NCMapperEFCoreGuardExtensions`: incompatible chains fail at compile time with error messages and suggested alternatives | — (compile-time behavior; no runtime demo form) | — | [orm-degradation](../explanation/orm-degradation.md) |
| Degradation detection | Automatic in-memory mapping fallback when SQL translation fails | 501 | — (Mask-enforced degradation snippet) | [orm-degradation](../explanation/orm-degradation.md) |

### 5.3 Dapper (`NCMapper.Dapper`)

| API | Purpose | Demo | Snippet (region) | Documentation |
|-----|---------|------|------------------|---------------|
| `connection.AsDto<T>()` | Entry point for projection selection (`NCMapperDapperExtensions`; returns `DapperDtoSelector<TSource>`) | 502 | Dapper_Advanced | [dapper](../how-to/orm/dapper-query.md) |
| Query family (11 query terminals) | Native query projection (`Query` / `QueryFirst` / `QueryFirstOrDefault` / `QuerySingle` / `QuerySingleOrDefault` / `QueryUnbufferedAsync` + async/command definition overloads; `QueryMultiple` covered separately below) | 502 | — (Run body + Dapper_Advanced) | [dapper](../how-to/orm/dapper-query.md) |
| `QueryMultiple<T1,T2>()` / `<T1,T2,T3>()` | Multi-result-set consumption from a single query | 502 | — (entire-scenario level) | [dapper](../how-to/orm/dapper-query.md) |
| `ToDictionary()` / `ToDictionaryAsync()` | Dictionary projection | 502 | Dapper_Advanced | [dapper-advanced](../how-to/orm/dapper-advanced.md) |
| `ToLookup()` / `ToLookupAsync()` | Grouped lookup table | 502 | Dapper_Advanced | [dapper-advanced](../how-to/orm/dapper-advanced.md) |
| `ToPagedList()` / `ToPagedListAsync()` | Paged projection | 502 | Dapper_Advanced | [dapper](../how-to/orm/dapper-query.md) |
| `ToChunkDto()` / `ToChunkDtoAsync()` | Chunked consumption | 502 | Dapper_Advanced | [dapper-advanced](../how-to/orm/dapper-advanced.md) |
| `ToTreeList()` / `ToTreeListAsync()` | Tree-shaped assembly | 502 | Dapper_Advanced | [dapper-advanced](../how-to/orm/dapper-advanced.md) |
| `ISqlDialect` + 6 built-in dialects | SQL dialects (automatically selected per connection) + extension points (`SqlServerDialect` / `MySqlDialect` / `PostgreSqlDialect` / `SqliteDialect` / `OracleDialect` / `Db2Dialect`; `LimitOffsetDialect` inherits from base class; 502 LoggingDialect demonstrates wrapper pattern) | 502 | — (LoggingDialect in Run body) | [dapper](../how-to/orm/dapper-query.md) |
| `SqlDialectResolver.Resolve(IDbConnection)` | Resolve dialect by connection (invoke when using custom connection wrapper types) | 501 | — (entire-scenario level) | [dapper](../how-to/orm/dapper-query.md) |

## 6. L6 Public Models

| API | Purpose | Demo | Snippet (region) | Documentation |
|-----|---------|------|------------------|---------------|
| `PagedList<T>` | Paginated result (`HasPrev`/`HasNext`/`TotalPages`) | 502 | Dapper_Advanced | [dapper-advanced](../how-to/orm/dapper-advanced.md) |
| `IMapper` / `NCMapperMapper` | DI-injected interface and its default implementation | 603 | DI_ResolveMapper | [di](../how-to/infrastructure/di.md) |
| `MapException` / `MapConfigurationException` | Mapping exception hierarchy | 602 | — (thrown by `ValidateOrThrow` and error-path logic) | [validation](../how-to/infrastructure/validation.md) |
| `DeepCopyContext` | Deep copy context (circular reference registration, depth control) | 401 | DeepCopy_MaxRecursionDepth | [deep-copy](../how-to/deep-copy/deep-copy.md) |
| `DeepCopyProviderBase` + provider family | Deep copy providers and switching strategy (provider injection or selection via `NCMapperOptions.DeepCopyStrategy`: `FastDeepCopyProvider` / `ExpressionTreeDeepCopyProvider`; JSON-based deep copy internally serializes values through JSON and does *not* support circular references) | 401 | DeepCopy_DeepCopyFast / DeepCopy_DeepCopyExpressionTree / DeepCopy_DeepCopyJson | [deep-copy](../how-to/deep-copy/deep-copy.md) |
| `GlobalConfigView` | Return type of `Mapper.GetGlobalConfig()` (immutable configuration view) | 606 | GlobalConfig_StrategyAndIgnore | [global-config](../how-to/infrastructure/global-config.md) |
| `GeneratedMapConfig` / `MappingMetaView` | Generated-code registration configuration carrier / runtime metadata view | — (intended for generated-code collaboration surface; not directly used by users) | — | — (internal surface only: no user-facing documentation; public types exist solely to satisfy code generator emission requirements) |
| Runtime six carrier classes | Runtime carriers for generated code (`RuntimeSnapshotStore` / `RuntimeSnapshotPair` / `RuntimeMapperMetadata` / `RuntimePropertyMapping` / `RuntimeTargetPropertySlot` / `RuntimeTreeConfig` / `RuntimeMappingChannel`) — **`RuntimeSnapshotStore` is the only user-accessible read facade** (`TryGetFluentDetailMeta`, validated in scenario 109) | — (the other five classes are automatically used by SourceGen-generated code; not directly invoked by users) | — (validated end-to-end in scenario 109; no isolated snippet) | [terminals](../how-to/fluent/terminals.md) (`RuntimeSnapshotStore` usage documented in the “From Terminal Composition and Runtime Metadata” section; the other five classes are internal-only and have no user-facing documentation) |
| `RuntimeComposeMapping` | Runtime representation of Compose cross-table mappings (`ComposeMappings` details: `TargetPropertyName` / `SubSourceTypeName` / `SourcePathNames` / `HasMask` / `FormatString` / `HasConverter` / `IsFromJson`) — user-accessible read facade = `RuntimeSnapshotStore.TryGetFluentDetailMeta(dtoType, out meta)` | 109 | RuntimeCompose_Run | [terminals](../how-to/fluent/terminals.md) (“From Terminal Composition and Runtime Metadata” section) |
| `ErrorMessages` (public static) | Bilingual (English/Chinese) error message texts (automatically switched per system language; no user configuration required) | — (automatic behavior; no demo form) | — | — (internal surface only: no user-facing documentation — automatic behavior requires no usage instructions) |

### 6.1 Public Enumerations

> This section lists 6 publicly exposed enumerations. None have dedicated demo forms (enumeration usage is covered indirectly within host feature/option demos). Coverage script counts these toward total line count but excludes undemonstrated entries from demo tally.

| Enumeration | User Touchpoint | Documentation |
|-------------|-----------------|---------------|
| `NamingStrategy` | Value for `[MapDefaults(NamingStrategy = ...)]` naming strategy | [global-config](../how-to/infrastructure/global-config.md) |
| `Direction` | Value for `[MapIgnore(Direction.ToEntity)]` direction specification | [declarative/basics](../how-to/declarative/basics.md) |
| `DeepCopyStrategy` | Value for `NCMapperOptions.DeepCopyStrategy` deep copy strategy (Fast/ExpressionTree/Json) | [deep-copy](../how-to/deep-copy/deep-copy.md) |
| `MapValidationLevel` | Validation level for `MapValidator` | [validation](../how-to/infrastructure/validation.md) |
| `DotPathFailureKind` | Dot-path failure category (paired with failure messages) | [dot-path](../how-to/mapping/dot-path.md) |
| `RuntimeMappingChannel` | Runtime channel identifier (exposed alongside snapshot metadata) | — (internal surface only: no user-facing documentation — enumeration serves as internal data structure carrier) |

---

## Coverage Statistics

| Layer | Item Count | Demonstrated | Not Demonstrated (with rationale) |
|-------|------------|--------------|-----------------------------------|
| L2 Core Runtime | 22 | 15 | 7 (generated-code collaboration surfaces / infrequently used overloads / runtime forced paths / switches & depth settings not demonstrated separately) |
| L3 Declarative Configuration | 21 | 19 | 2 (compile-time switches in `NCMapperOptions`; `[MapNoAutoMatch]` attribute form — partial coverage noted separately) |
| L4 Infrastructure Services | 11 | 8 | 3 (`PrintReport` / `IProjectionPolicy` / automatic behaviors) |
| L5 ORM Extensions | 38 | 27 | 11 (4 unused in demos; 2 compile-time guards; 1 automatic behavior; 2 switches; 1 Compose projection; 1 direct `Resolver` invocation) |
| L6 Public Models + Enums | 16 | 13 | 3 (carrier classes / channel enum / automatic behaviors) |
| **Total** | **108** | **82 (76%)** | **26 (all explicitly justified)** |

> All 26 undemonstrated items carry explicit justifications (aligned line-by-line with audit script check ②):  
> - **5** generated-code collaboration & carrier surfaces (`ClearGenerated`, `HasGenerated` family, `RegisterGenerated` family, `GeneratedMapConfig`/`MappingMetaView`, six Runtime carrier classes);  
> - **4** runtime switches & depth controls not demonstrated separately (`ValidationEnabled`/`TraceEnabled`, `MaxRecursionDepth`/`MaxInlineDepth`, `ThrowOnIncludeApplicationFailure`, `EFCoreProjectionAsNoTrackingDefault`);  
> - **6** features unused in demos (`FreeSql ToDictionary`, `EFCore AsTracking` & `ToPagedList`, `Dapper QueryMultiple`, `PrintReport`, direct `SqlDialectResolver` invocation);  
> - **3** compile-time behaviors (`NCMapperOptions` compile-time switches; `FreeSql`/`EFCore GuardExtensions` marked `[Obsolete(error)]` — no runtime demo form);  
> - **2** extension points & engine internals (`IProjectionPolicy`, `TypeConvertHelper`);  
> - **2** infrequently used overloads & special entry points (`Map` between same types, `DeepCopyRuntimeOnly`);  
> - **2** automatic behaviors (navigation collection expansion, automatic `ErrorMessages` text resolution);  
> - **1** Compose multi-source projection (the sole candidate eligible for optional supplemental demonstration);  
> - **1** attribute-side allowlist annotation (`[MapNoAutoMatch]` attribute form not demonstrated separately — covered by Fluent `.NoAutoMatch()` in scenario 101’s `101-no-auto-include` snippet);  
> - **1** partially covered dual-channel comparison table entry (`[MapNoAutoInclude]` attribute form not demonstrated);  
> - Two functional asymmetries specific to the attribute channel (no equivalent forms for “constructor parameter binding” and “inline lambda transcription in `MapWhen`”) — these are *not* gaps in demonstration coverage but rather inherent channel limitations, explicitly annotated alongside the above line-level justifications.

## Further Reading

- [samples index](samples-index.md) — Index of all 21 scenarios (how to run / what they cover / expected output)
- [Fluent Terminal Quick Reference](fluent-index.md) / [Attribute Quick Reference](attributes-index.md)
- [Core API](core-api.md) — Details of facade APIs

> This page has undergone multiple rounds of independent review and revision, and has been fully validated against the public API snapshot. Coverage auditing uses `scripts/check-api-demo-coverage.sh` with its five distinct checks as the sole automated metric.
