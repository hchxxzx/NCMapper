> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: getting-started/overview
Diagnostic codes involved: NCMAP001, NCMAP002, NCMAP_F11  
Library version: v1.0.0+  
Last updated: 2026-09-30  
---

# What Is NCMapper?

> This is the first page of the documentation site, answering: *What is it? What problems does it solve? What capabilities does it offer? Which package should I install? Where should I go next?* For package installation and version selection, see [Installation & Package Selection](installation.md). For a 15-minute hands-on introduction, see [Quick Start](quickstart.md).

## One-Sentence Summary

NCMapper is a high-performance object-mapping library for .NET: describe the relationship between *source type* and *target type* using either Fluent configuration or attribute-based annotations; the mapping process is then fully automated by the library—replacing manual, field-by-field assignment code between entities and DTOs.

Understanding it requires only three key concepts:

| Keyword | Meaning |
|---------|---------|
| Dual Engines | Two execution mechanisms: source generators + expression trees. Types declared in mapping rules are compiled into native code at build time; undeclared types are handled automatically at runtime. |
| Dual Declaration Channels | Two equivalent declaration styles—Fluent chaining and attribute-based annotation—let teams choose based on preference. |
| Adaptive Behavior | You declare (or even skip declaring) mappings; the system automatically selects the appropriate engine and gracefully falls back when needed. |

**You decide *how* to write the rules; the library decides *how* to execute the mapping.**

## What Problems Does It Solve?

- **Boilerplate Code**: Repetitive manual property assignments between entities and DTOs across layered architectures (e.g., Controller → Service → Repository) — replaced with a single `[MapFrom]` attribute.
- **Silent Failures**: When manually assigning properties, renaming or changing types won’t trigger compile-time errors — values may be silently dropped at runtime. NCMapper parses mapping rules at compile time; mismatches in property names or types produce immediate compile-time diagnostics (`NCMAP`-prefixed diagnostic codes).
- **Configuration Duplication**: The same mapping logic is often reimplemented separately for in-memory mapping, ORM projections, and deep copying. With declarative rules, you write them once — and reuse everywhere.

## Two Declaration Channels — Pick One First

The same set of capabilities is accessible via two declaration channels. Choose whichever fits your team’s style — both are fully equivalent and coexist long-term: different type pairs may use different channels; however, **a given pair of types may be declared only once**, regardless of direction (`For<B, A>` vs `For<A, B>` or `For<S, T>()` vs `[MapFrom]` are considered the same pair). Declaring the same pair in both channels triggers `NCMAP_F11` (unordered mutual references are guarded by the sibling diagnostic `NCMAP025`). Remember to remove the declaration from the other channel during migration.

For a detailed comparison (30-second decision guide, per-dimension equivalence, and migration guidance), see [Dual API Comparison](../reference/api-comparison.md); for a complete list of attributes, see [Attribute Quick Reference](../reference/attributes-index.md).

| Dimension | Fluent Channel | Attribute Channel |
|----------|----------------|-------------------|
| Syntax | Chain calls on `FluentMappingConfig` inside a class marked with `[MapperConfig]` | Annotate DTO classes with attributes like `[MapFrom]`, `[MapProperty]`, etc. |
| Rule Location | Centralized configuration class (keeps DTOs clean) | Embedded directly in the DTO (self-descriptive) |
| Ordering Dependency | Supported (chain order is natural and explicit) | Not supported |
| Best For | Teams preferring centralized mapping intent without polluting DTOs | Teams favoring “what you see is what you get” (both channels perform equally well in AOT scenarios — see *Dual Engines* section below) |

## Dual Engines — How the Path Is Chosen

| Engine | When Active | Characteristics |
|--------|-------------|-----------------|
| Compile-Time Source Generator | Activated when a type pair has a mapping declaration — either via `For<S,T>()` in a Fluent config class **or** via `[MapFrom]` attribute (both channels trigger source generation). Roslyn incremental generators emit optimized mapping code at compile time. | Zero first-call latency; auto-registered via `[ModuleInitializer]` (no manual registration required); provides compile-time diagnostics. |
| Runtime Expression Trees | Fallback mechanism used when no source-generated mapping exists. | First invocation compiles an expression tree into a delegate and caches it; generic static caching avoids dictionary lookups. |

Internally, `Mapper.Map` prioritizes the source-generated delegate; if none is found, it falls back to expression trees. Performance between both paths is virtually identical. The source generator is built into the `NCMapper` package — no separate installation required.

## The Journey of a Single Mapping

Take `Mapper.Map<UserEntity, UserDto>(src)` as an example — here's the full path of one call:

1. **Declaration**: Declare `For<UserEntity, UserDto>()` in a Fluent config class (or annotate `UserDto` with `[MapFrom(typeof(UserEntity))]`; both channels are equivalent). For properties with mismatched names, supplement with Fluent `.Member(d => d.X).From(s => s.Y)` or `[MapProperty]`.
2. **Compile-Time Extraction**: The source generator reads declarations, emits optimized mapping code, validates rules, and reports issues immediately as `NCMAP` diagnostics.
3. **Registration**: Generated code registers the mapping delegate into `Mapper`’s internal cache via `[ModuleInitializer]`. No manual registration code is needed.
4. **First Call**: `Mapper.Map` hits the pre-registered source-generated delegate and executes it directly.
5. **Reuse**: Delegates are cached per type pair; generic static caching eliminates dictionary lookups for subsequent calls of the same type pair.
6. **Fallback**: If no `[MapFrom]` (or Fluent `For<...>`) declaration exists for the type pair, the cache miss triggers fallback to runtime expression trees — which compile and cache a delegate on first use.

This yields a practical insight: **Type pairs with explicit mapping declarations** (either via Fluent `For<S,T>()` or `[MapFrom]`) constitute the primary, high-performance, and AOT-safe path; undeclared type pairs still work but fall back to the runtime path — a detail requiring attention in trimming and AOT environments (see [AOT / NativeAOT Deployment](../how-to/aot/deployment.md)).

## How Diagnostic Codes Protect You

Mapping rules are parsed at compile time, so typos or misconfigurations surface as compile-time signals — not silent runtime failures. Diagnostics fall into two families: `NCMAP_Fxx` for Fluent-side issues, and `NCMAP0xx` for attribute-side issues. Severity levels are Error, Warning, and Info.

| What You’re Writing | Triggered Diagnostic | What You Get |
|---------------------|----------------------|--------------|
| `[MapProperty]` references a field that doesn’t exist in the source type | `NCMAP001` (Warning) | Compile-time notification that the field is missing |
| Mapping a source field to a target property with incompatible type | `NCMAP002` (Warning) | Compile-time notification of type incompatibility |
| Same type pair declared both via Fluent *and* `[MapFrom]` | `NCMAP_F11` (Error) | Compile-time error indicating dual-channel conflict |

Full diagnostic code list and severity levels are documented in [Diagnostic Rules](../diagnostics/rules.md).

## Capabilities Overview

| Capability | Engine | One-Sentence Description | Learn More |
|------------|--------|---------------------------|------------|
| Single-Object Mapping | Dual | `Mapper.Map<TSource, TTarget>(src)` — automatic case-insensitive name matching (with flattening support) | [declarative/basics](../how-to/declarative/basics.md) |
| List Mapping | Dual | `Mapper.MapList<TSource, TTarget>(srcs)` — batch map collections | [declarative/basics](../how-to/declarative/basics.md) |
| In-Place Batch Mapping | Dual | `Mapper.MapListInto(srcs, dtos)` — reuses pre-allocated destination arrays, ideal for high-frequency refreshes | [MapListInto](../how-to/mapping/map-list-into.md) |
| Update Existing Instance | Dual | `Mapper.Map(src, dst)` — overwrites only mapped fields | [declarative/basics](../how-to/declarative/basics.md) |
| Callbacks | Dual | `Mapper.Map(src, (s, t) => ...)`; class-level hooks `[BeforeMapping]` / `[AfterMapping]` | [fluent/hooks](../how-to/fluent/hooks.md) |
| Multi-Source Composition | Dual | `Mapper.Compose<T>(sources...)` — compose one DTO from multiple source objects (unlimited sources) | [compose](../how-to/compose/compose.md) |
| Deep Copy | Dual | `Mapper.DeepCopy<T>(src)` — three strategies: source generator, Fast, expression tree | [deep-copy](../how-to/deep-copy/deep-copy.md) |
| ORM Projection | Runtime | Three extension packages provide `AsDto()` — generate DTOs directly from SQL, with unsupported features automatically downgraded to two-step mapping | [FreeSql Single-Table](../how-to/orm/freesql-single-table.md) |
| Pagination & Tree Projection | Runtime | `ToPagedList` for pagination; `ToTreeList` + `[MapTree]` to assemble flat query results into hierarchical trees | [Dapper Query Series](../how-to/orm/dapper-query.md) |
| Post-Mapping Pipeline | Runtime | Register `PostMap` hooks via `Mapper.Pipeline`; zero overhead for empty pipelines | [pipeline](../how-to/infrastructure/pipeline.md) |
| Data Masking | Compile-Time | `[Mask]` — attribute-level masking with 9 built-in implementations (phone number, ID card, bank card, email, etc.) | [masking](../how-to/infrastructure/masking.md) |
| Custom Conversion | Dual | `[Convert]` — reference static conversion methods; reverse conversion requires explicit `[ConvertBack]` | [declarative/basics](../how-to/declarative/basics.md) |
| Property-Level Formatting | Dual | `[MapFormat("format-string")]` / `.Member(d => d.X).Format(format)` — format `IFormattable` sources (dates, numbers, enums) into strings; reverse mapping requires `[ConvertBack]` for round-trip support | [declarative/map-format](../how-to/declarative/map-format.md) |
| Conditional Guarding | Dual | `.MapWhen` / `[MapWhen]` — map a property only when its predicate evaluates to `true` | [declarative/advanced](../how-to/declarative/advanced.md) |
| Polymorphic Subtypes | Dual | `.MapSubclass` / `[MapSubclass]` — dispatch based on actual runtime type of source instance (`isinst`) | [declarative/advanced](../how-to/declarative/advanced.md) |
| Directional Ignoring | Dual | `.Member(d => d.X).Ignore(direction)` / `[MapIgnore(Direction)]` — control mapping direction per property | [Attribute Quick Reference](../reference/attributes-index.md) |
| JSON Column Mapping | Dual | `[MapProperty(IsFromJson = true)]` — bidirectional string ↔ object mapping; projection side requires downgrade | [declarative/basics](../how-to/declarative/basics.md) |
| Global Default Configuration | Compile-Time | `[assembly: MapDefaults]` — declare naming strategy and ignore lists once for the entire assembly | [global-config](../how-to/infrastructure/global-config.md) |
| Mapping Validation | Runtime | `Mapper.Validate()` — scans assemblies and reports OK / Warn / Error status | [validation](../how-to/infrastructure/validation.md) |
| Diagnostic Tracing | Runtime | `Mapper.TraceEnabled` and `MapTracer.Enable()` — visualize mapping execution flow | [validation](../how-to/infrastructure/validation.md) |
| Performance Metrics | Runtime | `Mapper.MetricsEnabled` — counts calls and cache hits; `Mapper.ResetMetrics()` resets counters | [performance](../how-to/infrastructure/performance.md) |
| DI Integration | Runtime | `AddNCMapper()` — registers `IMapper` and supports Options-pattern configuration | [di](../how-to/infrastructure/di.md) |
| AOT Compatibility | Dual | Source generator path is AOT-friendly; runtime path uses `[RequiresDynamicCode]` for trimming analysis | [AOT Deployment](../how-to/aot/deployment.md) |
| Compile-Time Diagnostics | Compile-Time | Two diagnostic families — `NCMAP_Fxx` (Fluent) and `NCMAP0xx` (attributes) — ensure correctness at build time | [diagnostics/rules](../diagnostics/rules.md) |

## Which Package Should I Install?

The core package has no ORM dependencies; ORM-specific capabilities are added via optional extension packages:

| Package | Purpose |
|---------|---------|
| `NCMapper` | Core library — **source generator included**, no separate installation needed |
| `NCMapper.FreeSql` | FreeSql projection extensions (`AsDto()` terminal operator + auto-downgrade) |
| `NCMapper.EFCore` | EF Core projection extensions |
| `NCMapper.Dapper` | Dapper integration (`AsDto` unified entry point, SQL-level pagination, dialect adaptation) |

```bash
dotnet add package NCMapper
```

For full package matrix and target framework details, see [Installation & Package Selection](installation.md).

## Project Structure & Tech Stack

The repository is organized into three layers: core + source generator, three ORM extension packages, and samples + tests.

| Project | Target Frameworks | Description |
|---------|-------------------|-------------|
| `NCMapper` | net8.0 / net9.0 / net10.0 | Core library — mapping logic depends only on BCL and `System.Linq.Expressions`; also references `Microsoft.Extensions.*` abstractions (DI and configuration) |
| `NCMapper.SourceGen` | netstandard2.0 | Roslyn incremental generator — distributed as an analyzer asset bundled with the core package |
| `NCMapper.FreeSql` | net8.0 / net9.0 / net10.0 | FreeSql projection extension |
| `NCMapper.EFCore` | net8.0 / net9.0 / net10.0 | EF Core projection extension |
| `NCMapper.Dapper` | net8.0 / net9.0 / net10.0 | Dapper integration and SQL dialect adaptation |
| `samples/NCMapper.Samples` | net10.0 | Interactive sample project covering 18 real-world scenarios |
| `samples/NCMapper.AotSample` | net10.0 | Minimal AOT deployment sample (`PublishAot`) |
| `test/` (six test projects) | — | Unit tests, source generator output validation, integration tests, fuzz testing, and AOT compilation compatibility verification |

## Minimal Working Example

A simple entity-to-DTO mapping demonstrating four entry points: single-object mapping, list mapping, in-place batch mapping, and callbacks:

[!code-csharp[Basic mapping: single object (case-insensitive name match)](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

This example covers four usage patterns:  
- `SimpleNameMatch`: simplest case — automatic matching of identically named properties  
- `ListMap`: batch mapping of collections  
- `ListMapInto`: in-place mapping reusing pre-allocated destination arrays  
- `MapWithAction`: callback executed immediately after mapping completes  

Each static method represents a distinct scenario and returns results for unified output by the orchestration layer.

The full runnable examples reside under `samples/NCMapper.Samples/`:

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 201
```

## Performance Orientation

The repository includes BenchmarkDotNet benchmarks (see `benchmarks/`). Measured average performance for flat mapping (9 identically named properties) on 2026-10-07 (Linux full batch run, Intel Xeon Silver 4314 / Ubuntu 24.04.5, net10.0):  
- Source-generated extension method: ~20.4 ns  
- `Mapper.Map` (source-generated path): ~23.9 ns  
- Hand-written assignment: ~20.0 ns  

Source-generated extension methods are JIT-inlinable and perform at the same level as hand-written code. Note that absolute performance numbers vary significantly across CPU/OS configurations — full data is available in [Performance Optimization Mechanisms §14.6](../explanation/performance.md).

For selection and tuning guidance (engine comparison, hot-path optimization, metric reading), see [Performance & Best Practices](../how-to/infrastructure/performance.md).

## Positioning and Trade-offs

| Dimension | NCMapper’s Approach | Rationale |
|-----------|---------------------|-----------|
| Dependencies | The core mapping engine depends only on BCL and `System.Linq.Expressions`; optional abstractions (e.g., DI and configuration) rely solely on `Microsoft.Extensions.*` packages | Minimizes version conflicts; ORM capabilities are provided as *optional* extension packages — the core library does *not* reference any ORM. |
| Execution Path | Compile-time generation is prioritized, with runtime fallback | Types annotated with `[MapFrom]` achieve zero first-call latency and compile-time diagnostics; unannotated types remain fully functional at runtime. |
| Error Exposure | Mapping rules are parsed at compile time, with diagnostics categorized by severity (Error / Warning / Info) | Mismatches in property names or types trigger explicit, actionable errors. |
| Declaration Location | Two declaration channels coexist, but are mutually exclusive for a given type pair | Enables teams to choose their preferred style while preventing contradictory mapping rules for the same type pair. |
| Scope Coverage | A single unified rule set serves memory mapping, ORM projections, deep cloning, and multi-source composition | Eliminates redundant, divergent configurations for the same mapping across different contexts. |

## Scenarios Not Supported

The following boundaries are detailed in [Limitations and Considerations](../explanation/limitations.md):

- **Runtime Path**: Does *not* support mapping `dynamic` types, `ref struct`, or `Span<T>`; cycle detection has a maximum depth of 64 levels.
- **Source Generator Path**: Processes only `class` types annotated with `[MapFrom]` (does *not* process `struct` or `record struct`); cross-assembly mapping requires the target assembly to also reference the source generator; cross-type field access requires all involved types to be visible within the same compilation unit.
- **ORM Projection**: Use of `[Convert]`, `[Mask]`, `IsFromJson`, or PostMap pipeline steps causes automatic fallback to two-step mapping (i.e., memory mapping); only `List<T>` and assignable interfaces project directly into collections — `T[]` and `HashSet<T>` trigger fallback; parameterized `record` nested types also fall back.

## Three Common Usage Paths

| Your Situation | Recommended Starting Point |
|----------------|----------------------------|
| First-time user aiming for a quick working demo | Start with [Quick Start](quickstart.md) for basic object mapping, then follow the [15-Minute Hands-On Tutorial](tutorial-15min.md) to walk through the full flow from definition to validation. |
| Already using another mapper and planning migration | First consult the [Dual API Surface Comparison](../reference/api-comparison.md) to align declaration patterns, then proceed step-by-step via [Migrating from Other Mappers](migrating-from-other-mappers.md). |
| Targeting AOT or trimmed deployments | Annotate *all* DTOs with `[MapFrom]` to leverage the source generator path; see [AOT / NativeAOT Deployment](../how-to/aot/deployment.md) for publishing guidance and boundary details. |

## Quick Reference Summary

| What You Want to Do | API or Feature | Where to Go |
|---------------------|----------------|-------------|
| Learn how to install and which package to use | `dotnet add package NCMapper` | [Installation and Package Selection](installation.md) |
| Run your first mapping | `[MapFrom]` + `Mapper.Map` | [Quick Start](quickstart.md) |
| Complete an end-to-end walkthrough | Full flow: from entity definition to validation | [15-Minute Hands-On Tutorial](tutorial-15min.md) |
| Choose between the two declaration channels | See detailed comparison table | [Dual API Surface Comparison](../reference/api-comparison.md) |
| Browse all features | Overview of 17 features | [Attribute Quick Reference](../reference/attributes-index.md) |
| Understand diagnostic codes | `NCMAP0xx` / `NCMAP_Fxx` | [Diagnostic Rules](../diagnostics/rules.md) |
| Explore runnable examples | 18 real-world scenarios | [Samples Mapping Table](../reference/samples-index.md) |

## Further Reading

- **Getting Started**: [Installation and Package Selection](installation.md) · [Quick Start](quickstart.md) · [15-Minute Hands-On Tutorial](tutorial-15min.md)  
- **Reference**: [Dual API Surface Comparison](../reference/api-comparison.md) · [Attribute Quick Reference](../reference/attributes-index.md) · [Core API](../reference/core-api.md)  
- **Conceptual Deep Dives**: [Dual-Engine Architecture](../explanation/dual-engine.md) · [Limitations and Considerations](../explanation/limitations.md)
