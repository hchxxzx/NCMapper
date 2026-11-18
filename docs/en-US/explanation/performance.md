> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §14 Performance Optimization Mechanisms -->

## 14. Performance Optimization Mechanisms

NCMapper’s mapping performance approaches hand-written code—source-generated direct calls take approximately 7–33 ns (depending on scenario complexity, measured on .NET 10.0 Linux), and source-generated deep copy takes ~179 ns. This is achieved through a multi-layered optimization strategy spanning compile time to runtime: source generators emit zero-delegate code at compile time; JIT generic static caching isolates implementations per type combination for zero-overhead access; expression tree compilation caching avoids redundant compilation; `MemberInit`-simplified expressions facilitate JIT inlining; and inline depth control balances expansion depth against compilation time.

### 14.1 JIT Generic Static Caching

```csharp
// A separate static field is generated for each type combination (zero-overhead JIT access)
private static class GeneratedCache<TSource, TTarget> {
    public static Func<TSource, TTarget?>? CoreFunc;            // Hot path without Action
    public static Func<TSource, Action<TSource,TTarget>?, TTarget?>? Func;  // With Action
    public static Func<List<TSource>, List<TTarget>>? ListFunc; // List mapping
}
```

### 14.2 Expression Tree Compilation Caching

| Delegate                            | Purpose                              |
| ----------------------------------- | ------------------------------------ |
| `CoreFunc` / `Func`                 | New-object mapping (hot path without Action / with Action) |
| `UpdateAction`                      | Updating existing objects             |
| `ListFunc`                          | List mapping (with inlined `for` loop) |

### 14.3 Projection Expression Caching

Projection builders from ORM extension packages (`EfCoreProjectionBuilder` / `FreeSqlProjectionBuilder`) internally cache constructed Lambda expressions via `ProjectionStrategy`. Subsequent invocations incur zero reflection overhead after the first build.

Multi-table expression composition caching works similarly (`FreeSqlProjectionBuilder.s_composeCache` / `s_compose3Cache`). Repeated calls with identical type combinations return cached instances directly.

### 14.4 `MemberInit` Optimization

For simple mappings (no nesting, no conversions), `new T { Prop = src.Prop }` is generated using only `MemberInit` expressions—avoiding `Block`-based variable assignment overhead and enabling easier JIT inlining.

### 14.5 Inline Depth Control

Nested objects are inlined within expression trees up to a configurable depth (`MaxInlineDepth = 5`). Beyond this limit, fallback occurs to delegate-based `Mapper.Map<>` invocation, preventing infinite expression tree expansion and potential compilation timeouts.

### 14.6 Performance Benchmark Data

> **Test Environment**: Intel Xeon Silver 4314 @ 2.40GHz (2 sockets / 40 logical cores, baseline locked to cores 0–19), 62 GB RAM, Ubuntu 24.04.5 LTS, .NET 8.0/9.0/10.0, BenchmarkDotNet 0.14.0 (5 warmups × 15 iterations, 3 additional warmup runs)  
> **Test Date**: 2026-10-07 07:49 – 15:16 UTC (full-component, cross-framework, all-scenario batch run; total duration: 7 hours 27 minutes) | Full dataset in *NCMapper Full-Component Cross-Framework All-Scenario Performance Test Report_20261007_Linux*  
> **Measurement Notes**: Performance values vary significantly across CPU/OS/.NET versions (real-world cross-machine measurements may differ by 1–2×). Values in this section reflect relative performance levels and orders of magnitude *only* for this specific Linux environment—not absolute cross-environment performance guarantees.

#### Mapping Performance (.NET 10.0, full-scenario batch)

| Scenario | Source-Gen Direct Call | API Call | Runtime Expression Tree | Hand-Written | Notes |
| ------------------------- | -------: | -------: | -------: | -------: | ------------------------- |
| Flat (9 flat fields) | 20.36 ns | 23.94 ns | 34.76 ns | 19.97 ns | Most basic property-to-property mapping by name |
| NameDiff (name mismatch) | 16.40 ns | 19.67 ns | 28.38 ns | 16.28 ns | All properties require `[MapProperty]` redirection |
| Nested (nested objects) | 26.69 ns | 29.16 ns | 38.35 ns | 26.16 ns | Recursive mapping including inner objects |
| Record (immutable records) | 16.35 ns | 19.75 ns | 32.94 ns | 15.75 ns | Immutable types mapped via constructor parameter matching |
| Nullable (nullable types) | 13.30 ns | 16.94 ns | 27.35 ns | 14.25 ns | Mixed nullable/non-nullable mapping |
| AutoFlatten (flattening) | 23.23 ns | 26.70 ns | 30.85 ns | 23.37 ns | Naming-convention flattening (e.g., `AddressCity`) |
| EnumString (enum ↔ string) | 15.44 ns | 18.42 ns | 29.92 ns | 26.70 ns | Compile-time generated `switch` expression |
| Action (with callback) | 32.41 ns | 37.41 ns | 46.22 ns | 34.45 ns | Full mapping with user-provided callback |
| Update (update existing object) | 7.06 ns | 10.36 ns | 18.56 ns | 8.22 ns | No new object allocation; direct property assignment |
| BidirDE (bidirectional Entity→DTO) | 24.62 ns | 26.80 ns | 34.51 ns | 22.94 ns | Bidirectional mapping (Entity→DTO direction) |
| BidirED (bidirectional DTO→Entity) | 20.72 ns | 23.73 ns | 34.94 ns | 19.58 ns | Bidirectional mapping (DTO→Entity direction) |
| Collection (collection, 100 items) | 2.491 μs | 2.461 μs | 2.131 μs | 2.142 μs | `MapList` / source-gen list extensions |
| CollectionInto (into pre-allocated collection, 100 items) | — | 476.1 ns | 762.8 ns | — | Reuses pre-allocated target; zero allocations at top level |

> **“Hand-Written” column notes**: Baseline `ManualMapper` with explicit field-by-field assignments (`benchmarks/NCMapper.Benchmarks/Program.cs`, theoretical optimal reference line); data taken from same 2026-10-07 full batch (same machine, .NET 10.0) for rival frameworks; vs Manual % = (NCMapper − Hand-Written) / Hand-Written (negative = faster than hand-written). `CollectionInto` has no hand-written counterpart (no common convention for reusing pre-allocated collections). Full 36 comparison sets and percentile data appear in the aforementioned comprehensive report.

**Meaning of the three paths**:

- **Source-Gen Direct Call**: Source generator emits direct-call methods like `src.MapToXxxDto()` (as extension methods); JIT inlining eliminates delegate overhead—the highest-performance path.
- **API Call**: Invoked via generic `Mapper.Map<S,T>(src)` API; delegates are resolved via static cache lookup, adding one layer of indirection.
- **Runtime Expression Tree**: Used automatically when no `[MapFrom]` attribute is present; incurs compilation latency on first use, then uses cached compiled delegates.

#### Deep Copy Performance (Three Frameworks Compared)

| Path | .NET 8.0 | .NET 9.0 | .NET 10.0 | Notes |
| ---- | -------: | -------: | ------: | --------------------------- |
| Source Generator | 233.0 ns | 203.1 ns | 179.2 ns | Field-by-field copy code generated at compile time—fastest |
| Runtime | 530.5 ns | 456.1 ns | 400.9 ns | `FastDeepCopyProvider`, default strategy |
| Expression Tree | 555.0 ns | 474.6 ns | 409.2 ns | Includes cycle-detection; suitable for complex object graphs |

(Hand-written baseline: 87.0 / 84.4 / 70.6 ns; deep-copy competitors MemoryPack / MessagePack / JSON results in the above full report.)

#### Cross-Framework Trends

All paths improve with newer .NET versions. For example, deep copy via expression tree improves from 555.0 ns (.NET 8.0) → 474.6 ns (.NET 9.0) → 409.2 ns (.NET 10.0), a ~26% gain from .NET 8.0 to .NET 10.0—primarily due to improved JIT inlining strategies and devirtualization. Mapping paths (source-gen direct call / API call / runtime expression tree) also consistently improve from .NET 8.0 to .NET 10.0 (typically 3–73%, most >10%), with the largest gains seen in the runtime expression tree path (e.g., `Update`: 69.85 ns → 18.56 ns, −73%; `Action`: 96.34 ns → 46.22 ns, −52%).

#### Competitor Comparison (12 Comparable Scenarios × 3 Frameworks)

- NCMapper’s fastest path outperforms the fastest competitor in **15 out of 36 scenarios**, stays within ±15% of the competitor’s best in **21 scenarios**, and falls outside ±15% in **0 scenarios**;
- **34 out of 36 scenarios** achieve ≤1.10× the hand-written baseline;
- `EnumString` conversion is ~42% faster than hand-written; `Update` is 2–3× faster than Mapster and nearly 9× faster than AutoMapper (`Update` is unimplemented in Mapperly);
- The only relatively weaker scenario is `Collection`: the direct-call path is ~16% slower than hand-written, but the fastest path overall (runtime expression tree) differs from hand-written, Mapster, and Mapperly by <5%.

**Per-scenario comparison (.NET 10.0, mean per operation, 2026-10-07 batch)**:

| Scenario | NCMapper Direct Call | Mapperly | Mapster | AutoMapper | Hand-Written |
| ---- | -------: | -------: | -------: | -------: | -------: |
| Update (update existing object) | 7.06 ns | — | 17.43 ns | 62.38 ns | 8.22 ns |
| Nullable (nullable types) | 13.30 ns | 14.00 ns | 25.71 ns | 72.36 ns | 14.25 ns |
| Record (immutable records) | 16.35 ns | 16.65 ns | 38.19 ns | 76.13 ns | 15.75 ns |
| NameDiff (name mismatch) | 16.40 ns | 15.83 ns | 25.44 ns | 76.40 ns | 16.28 ns |
| BidirED (DTO→Entity) | 20.72 ns | 19.94 ns | 30.38 ns | 78.97 ns | 19.58 ns |
| Flat (9 flat fields) | 20.36 ns | 19.78 ns | 30.28 ns | 79.30 ns | 19.97 ns |
| BidirDE (Entity→DTO) | 24.62 ns | 23.86 ns | 33.78 ns | 82.27 ns | 22.94 ns |
| Nested (nested objects) | 26.69 ns | 25.60 ns | 37.81 ns | 82.78 ns | 26.16 ns |
| EnumString (enum ↔ string) | 15.44 ns | 16.60 ns | 30.62 ns | 83.17 ns | 26.70 ns |
| Action (with callback) | 32.41 ns | 33.24 ns | 42.19 ns | 92.04 ns | 34.45 ns |
| Collection (100 items) | 2.491 μs | 2.197 μs | 2.067 μs | 2.734 μs | 2.142 μs |
| AutoFlatten (flattening) | 23.23 ns | 22.89 ns | — | — | 23.37 ns |

> `—` = Not implemented by that library. NCMapper column shows source-generator direct-call path (fastest path); `Mapper.Map` API call path appears in the earlier table. All values come from the same benchmark machine and batch run (2026-10-07).

### 14.7 Cache Governance

`ClearCache()` provides a unified entry point for cache cleanup, cascading through multiple layers of static caches:

| Cached Object                                       | Description                                       |
| -------------------------------------------------- | ------------------------------------------------- |
| Mapping delegate caches (`GeneratedCache` / `MapperBase` static `Lazy<T>` fields) | Source-generated delegates + compiled expression-tree delegates |
| `PropertyMatcher` / `ReflectionHelper`             | Property dictionaries and reflection metadata caches |
| `MapValidator` / `CollectionCopier` / `DeepCopy*` | Validation results, collection copiers, deep-copy classifiers |
| Projection cache cleanup delegates (multi-slot `ConcurrentDictionary`) | EF Core / FreeSql extension-registered `ProjectionBuilder` caches; `ClearCache()` iterates and invokes each |

**Key Constraints**:

- **Dynamic Types Are Not Cached**: Static `Lazy<T>` fields in `MapperBase<TSource,TTarget>` use closed-type combinations as keys; mappings involving `dynamic`, open generics, or anonymous types fall back to runtime expression trees and are *not* statically cached—slightly lower performance but guaranteed correctness.
- **Configuration Re-Freezing Restriction**: Global configuration via `[assembly: MapDefaults]` becomes immutable (`GlobalConfigView`) after first consumption; to modify configuration, call `ClearGenerated()` first to unfreeze, then re-register—otherwise newly registered configurations will *not* apply to **already-compiled** type pairs (`MapperBase` static `Lazy<T>` instances, once materialized, remain unaffected by `ClearCache()` or config re-freezing).
- **`CacheMetrics` Internal Observability**: `CacheMetrics` (internal static) tracks cache hit/miss counts; both `ClearCache()` and `ResetMetrics()` cascade `CacheMetrics.Reset()`, supporting performance diagnostics—but `CacheMetrics` is *not* part of the public API.

---
