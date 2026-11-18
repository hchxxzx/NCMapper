# NCMapper Samples — Demonstration Projects

A comprehensive demonstration project for NCMapper, organized by feature domains (6 scenario domains + 1 AOT placeholder domain / total of 22 scenarios). Scenario IDs are three-digit numbers formed as `DomainID × 100 + SequenceNumber`. Each scenario is an independent runnable unit, executable either via an interactive menu (two-level hierarchy: domain → scenario; input `all` to run all scenarios at once) or via the `--scenario <id>` command-line argument.

## Execution Methods

```bash
cd samples/NCMapper.Samples
dotnet run                    # Interactive menu (domain → scenario two-level hierarchy; input 'all' to run all scenarios)
dotnet run -- --scenario 101  # Run specified scenario
dotnet run -- --all           # Run all scenarios
```

- **Exit Code Contract**:  
  `0` = Success,  
  `1` = In-scenario validation failure (Error Summary),  
  `2` = Invalid scenario ID (registry miss — including unregistered IDs within valid numeric range).

- **Expected Failure Demos**: Some scenarios intentionally trigger exceptions to demonstrate the library’s error reporting capability. Such outputs include a note line: `note : Expected demo — the exception below is triggered on purpose, not a program error` (via `OutputHelper.DumpExpectedFailure`) — this does *not* indicate a program error.

- **Localization Behavior**:  
  Demo output messages, runtime exception messages from the library, and validator messages (from `MapValidator` / source generator) adapt to the entry-point UI culture (`CurrentUICulture=en-US` → English, `zh-CN` → Chinese).  
  Validation messages embedded into generated code by the source generator are *always in English*, fixed at compile time and unaffected by runtime culture.  
  Chinese demo data (e.g., names, addresses, license plates) used in masking-related scenarios are retained intentionally for functional demonstration purposes.

- **Scenario Registry**: `ScenarioRegistry.cs` (static registration, compile-time fixed; add one line per new scenario in its corresponding domain section).

- **Bulk Execution Script**: `scripts/run-demo-all.sh` (run from repository root).

## Scenario Domain Directory

| Domain | Scenario IDs | Scenario | Description |
|--------|--------------|----------|-------------|
| [01-Fluent](./01-Fluent/) | 101–108 | Fluent Full Suite | Basic chains / conversions / cross-source mapping / hooks / guards / constructors / polymorphism / inline transcription |
| [02-Declarative](./02-Declarative/) | 201 | Basic Mapping (Attribute) | `Map` / `MapList` / `Action` / `Property` / `Convert` / Source Generator / JSON field mapping / index-path mapping |
| [03-Compose](./03-Compose/) | 301 | Multi-Source Compose | `Compose` / `MultiSourceCompose` / `ComposeList` / direct reverse invocation / cross-table transformation |
| [04-DeepCopy](./04-DeepCopy/) | 401 | Deep Copy | Three strategies (Fast / ExpressionTree / Json) / deep collection copy / recursion depth limit |
| [05-Orm](./05-Orm/) | 501–503 | ORM Projection | Dual ORM support (FreeSql + EFCore), Dapper, multi-table & advanced terminals |
| [06-Infrastructure](./06-Infrastructure/) | 601–607 | Infrastructure | Pipeline / validation / DI / performance / data masking / global configuration / tracing |
| [07-Aot](./07-Aot/) | — | Placeholder Domain | AOT execution demo located at `samples/NCMapper.AotSample/` |

## Per-Scenario Descriptions

### 01-Fluent Domain

**101 Fluent Basics** — `dotnet run -- --scenario 101`  
Covers: basic fluent chaining (renamed `Map`, nested paths, `Ignore`, `Mask` for data masking, `AfterMapping` aggregation), `MapTree` recursion, `NoAutoInclude`, custom ternary `Tree()` usage, indexed path segments.  
Expected output: JSON results for each sub-scenario, with masked fields rendered as `****` (no plaintext exposure).

**102 Fluent Convert** — `dotnet run -- --scenario 102`  
Covers: four forms of `Convert`/`ConvertBack` templates, paired declarations, `overrideIgnore` exemption gate, `FromJson`, entity-side anchor families.  
Expected output: before/after conversion comparison and exemption-gate recovery demonstration.

**103 Fluent FromSource** — `dotnet run -- --scenario 103`  
Covers: cross-source binding shortcuts and grouped blocks, `Mask`/`FromJson` inside blocks, order-based override, dual-direction constraints.  
Expected output: cross-source field value extraction results.

**104 Fluent Hooks** — `dotnet run -- --scenario 104`  
Covers: `BeforeMapping`/`AfterMapping` on both fluent and class-level attribute sides, execution phase ordering, cross-pair signature filtering.  
Expected output: hook execution trace and phase sequence.

**105 MapWhen** — `dotnet run -- --scenario 105`  
Covers: conditional guard patterns (A-T1/B-T1/T2/S-6) within same chain, attribute-side `[MapWhen]`.  
Expected output: mapping differences between true/false guard paths.

**106 MapCtorParam** — `dotnet run -- --scenario 106`  
Covers: explicit constructor parameter binding (two forms), record boundary clarification, element-level `MapList`.  
Expected output: binding result for target instances constructed with parameters.

**107 MapSubclass** — `dotnet run -- --scenario 107`  
Covers: polymorphic subtype dispatch (four families), base-class fallback, reverse subclass pairing, attribute-side `[MapSubclass]`.  
Expected output: mapping results dispatched according to the actual runtime type of the source instance.

**108 Inline Lambda Transcription Runtime Comparison** — `dotnet run -- --scenario 108`  
Covers: non-capturing inline lambdas allowed (transcribed into generated code — true/false path comparison); capturing scenarios require extraction into static method groups to become effective (F41/F42 compile-time rejection forms documented in comments and reference docs).  
Expected output: runtime comparison between inline predicate and method-group predicate outcomes.

### 02-Declarative Domain

**201 Basic Mapping (Attribute)** — `dotnet run -- --scenario 201`  
Covers: name-matching, `MapList`, `MapListInto` reuse, callbacks, updating existing objects, `[MapProperty]` renaming, `[Convert]`/`[ConvertBack]` round-trip, six overloads of direct reverse invocation, dot-path flattening, bidirectional JSON field mapping, enum semantics, index-path mapping.  
Expected output: JSON results for each sub-scenario (including `ListMapInto` reuse loop and out-of-bounds slot preservation demonstrations).

### 03-Compose Domain

**301 Multi-Source Compose** — `dotnet run -- --scenario 301`  
Covers: `Mapper.Compose` for dual/triple/batch sources and callbacks, direct reverse invocation, cross-table transformation.  
Expected output: multi-source composed DTO result (field values aggregated across objects).

### 04-DeepCopy Domain

**401 Deep Copy** — `dotnet run -- --scenario 401`  
Covers: three strategies (Fast / ExpressionTree / Json) for same-type copying, callbacks, deep collection copy, `MaxRecursionDepth` recursion limit.  
Expected output: copy results per strategy and depth-protection behavior.

### 05-Orm Domain

**501 ORM Projection (FreeSql/EFCore)** — `dotnet run -- --scenario 501`  
Covers: FreeSql — single table / pagination / batch / multi-table `Compose` / `Include` navigation / `WithSql` / `GroupBy` guard / tree projection; EFCore — single table / batch / grouping / nested cross-table / index-path projection; Fluent DTO dual mode (pure translatable projection + `Mask`-enforced downgrade); `ToTreeList` tree projection (attribute channel + Fluent `Tree()`).  
Expected output: projection JSON results and downgrade traces; tree fragments showing Root → children → grandchildren nesting.

**502 Dapper Integration** — `dotnet run -- --scenario 502`  
Covers: `Query` family, `ToPagedList`, `ToDictionary`, `ToLookup`, `ToChunkDto`, `ToTreeList`, streaming enumeration, `QueryMultiple`, async variants.  
Expected output: terminal outputs and pagination metadata.

**503 FreeSql Multi-Table & Advanced Terminals** — `dotnet run -- --scenario 503`  
Covers: dual-table JOIN projection (cross-table field extraction), `IncludeMany` one-to-many navigation collection projection, `ToChunkDtoAsync` async batching (requires stable `OrderBy`), `ToLookup` grouping, Fluent DTO projection — all using in-memory SQLite.  
Expected output: JOIN result with 4 rows, non-empty nested collections, 2 batches totaling 4 items, 2 groups.

### 06-Infrastructure Domain

**601 Pipeline** — `dotnet run -- --scenario 601`  
Covers: `PostMap` hooks, audit pipeline, onion model.  
Expected output: pipeline execution trace.

**602 Validation & Diagnostics** — `dotnet run -- --scenario 602`  
Covers: `Mapper.Validate()` assembly scanning, `ValidateOrThrow` startup-time validation, exception diagnostics.  
Expected output: OK/Warn/Error tiered report.

**603 DI Integration** — `dotnet run -- --scenario 603`  
Covers: `AddNCMapper` service registration and `IMapper` injection.  
Expected output: container-resolved artifacts.

**604 Performance** — `dotnet run -- --scenario 604`  
Covers: Metrics counters, throughput comparison, dual-engine comparison, cold start, `MapListInto` throughput.  
Expected output: performance counters and timing data.

**605 Data Masking** — `dotnet run -- --scenario 605`  
Covers: `[Mask]` attribute masking, 9 built-in implementations (original → masked value comparison), fake-masking assertion (output must change and contain mask characters), custom `IMask` registration & restoration (Provider layer), `OverrideMask` post-effect boundary verification.  
Expected output: 9-line masking comparison (all synthetic data) and registration/restoration trace.

**606 Global Config** — `dotnet run -- --scenario 606`  
Covers: `[assembly: MapDefaults]` migration comparison (per-property attributes vs. global config), SnakeCase naming strategy matching, `IgnoreNames` global ignore, Fluent exemption gate (`overrideIgnore` recovers ignored fields), F7 directional ignore.  
Expected output: consistency verification across both approaches and frozen config view.

**607 Mapping Tracing** — `dotnet run -- --scenario 607`  
Covers: global `TraceEnabled` + `LogCallback` full lifecycle (enable → capture → assert → restore); forced analysis report via `MapTracer.Trace` (covering all four mapping modes: `direct`/`nested`/`convert`/`skipped`).  
Expected output: captured trace lines and analysis report with all four mode tags present.

## Model Definitions

Cross-domain shared models reside under [Models/Common/](./Models/Common/) (namespace remains `NCMapper.Samples.Models.*`):

| Directory | File | Description |
|-----------|------|-------------|
| [Entities/](./Models/Common/Entities/) | `UserEntity`, `OrderEntity`, `CustomerEntity`, `ProductEntity`, `IncludeManyEntities` | Entity classes (including nested `Address`, enum fields) |
| [Dtos/](./Models/Common/Dtos/) | `UserDto`, `UserDetailDto`, `OrderDetailDto`, `CustomerDto`, `ProductDto`, `ReportDto`, `IncludeManyDtos` | DTO classes (annotated with `[MapFrom]`/`[MapProperty]`/`[MapIgnore]`/`[Convert]`) |
| [Converters/](./Models/Common/Converters/) | `DateTimeFormatter`, `PhoneMaskConverter`, `FullNameConverter` | Static converter method classes (referenced by `[Convert]`) |
| [Enums/](./Models/Common/Enums/) | `UserStatus`, `OrderStatus`, `CustomerLevel` | Enum definitions |

Domain-private models reside within respective domain directories (e.g., `01-Fluent/FluentModels.cs`, `05-Orm/MultiTableModels.cs`, `02-Declarative/JsonFieldModels.cs`, etc.), with namespace `NCMapper.Samples.<Domain>`.

## Feature Coverage Overview

> Per-API coverage mapping is detailed in the [Public API Architecture Overview](../../docs/en-US/reference/architecture-map.html) (layered architecture diagram + demo coverage reconciliation checklist; automatically verified by script `check-api-demo-coverage.sh`).

| Feature | Scenario IDs |
|---------|--------------|
| Fluent Chaining Full Suite | 101–108 |
| Attribute-Based Basic & Advanced Mapping | 201 |
| Multi-Source Composition | 301 |
| Deep Copy (Three Strategies) | 401 |
| ORM Projection (FreeSql / EFCore / Dapper) & Downgrade | 501–503 |
| Post-Mapping Pipeline | 601 |
| Mapping Validation & Diagnostics | 602 |
| DI Integration | 603 |
| Performance Metrics | 604 |
| Data Masking (Attribute + Provider) | 605 |
| Global Configuration & Exemption Gates | 606 |
| Mapping Tracing | 607 |

## Technical Documentation Mapping

The mapping between scenarios and documentation is defined in the technical documentation site’s [samples-index](../../docs/en-US/reference/samples-index.html) (the single authoritative docs↔samples mapping table). Technical documentation homepage: `docs/en-US/index.html`.
