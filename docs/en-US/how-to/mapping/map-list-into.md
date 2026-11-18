> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: how-to/mapping/map-list-into
Diagnostic codes involved: (none)  
Library version: v1.0.8  
Last updated: 2026-09-15  
---

# `MapListInto`: Batch In-Place Mapping (Reuse Scenarios)

In high-frequency batch scenarios where you want to avoid allocating a new target object for each source element, use `MapListInto` to populate a **caller-preallocated destination array** element-by-element, overwriting only the mapped fields. The same array can be reused across multiple invocations; in reuse scenarios, the top-level object graph incurs zero allocations.

**Comparative value**: `MapList` allocates a brand-new `List<T>` and `N` DTO instances on every call; `MapListInto` reuses a preallocated destination array, resulting in zero top-level allocations per invocation. This makes it ideal for high-frequency UI list refreshes, bulk synchronization into long-lived buffers, and similar scenarios—avoiding GC pressure caused by full allocation on every iteration.

Core API: `Mapper.MapListInto<TSource, TTarget>(sources, destination, action?)`

## Usage

Preallocate the array once and initialize each element. On each call, perform in-place population (types share the same origin as `UserEntity`/`UserDto` in the samples):

[!code-csharp[MapListInto batch in-place mapping method](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapListInto)]

## Reuse Loop (Preallocation + Cross-Iteration Reuse)

Reuse the same array across pages or streaming batches; only mapped fields are overwritten per iteration:

[!code-csharp[MapListInto reuse loop](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapListIntoReuse)]

The caller’s preallocation strategy and per-page population scheduling is demonstrated in the `BasicMapping_MapListIntoReuse` snippet above (`dotnet run --scenario 201`).

**Key points**:

- **Reuse contract**: The caller must preallocate `destination` and initialize each element (`destination[i] != null`). Null slots are **skipped silently (no exception)** (C5); unmapped fields retain residual values from the previous iteration (C6), and must be cleaned up manually if needed.
- **Length contract**: `destination.Length >= sources.Count`. Only the first `sources.Count` slots are populated; excess slots retain their original values (C3); insufficient length throws `ArgumentException`; `destination == null` throws `ArgumentNullException` (checked first, C1).
- `sources == null` returns immediately without populating (C8); `action(src, dst)` is invoked after mapping each element (C10), with semantics identical to `MapList`.
- Nested reference properties are newly instantiated per iteration (C7): `dst.Address = Map(src.Address)`, not updated in-place. Zero top-level allocation ≠ zero allocation across the entire object graph.
- Registered types (annotated with `[MapFrom]`) use source-generated batch delegates; unregistered types fall back to runtime expression trees. Both paths fully honor the reuse contract.
- Performance (measured on 2026-08-20 under load: 16 threads, median of 3 runs, DATAS disabled): Scaling efficiency 0.66–0.71 (TPS_16T / TPS_1T / 16); 1-thread throughput is 5.2×–5.6× that of `MapList` in the same run. See [Performance](../infrastructure/performance.md) §3 for details.

## Null Handling Semantics (Process and Rationale)

`MapListInto` handles `null` at two levels: entry-point validation and per-slot element-level processing. The semantic goal is to guarantee that — *under the “reuse existing array” premise* — the destination array remains safe under any input: no `null` writes, no unintended clearing of existing data.

### Entry-Point Validation (Executed in Order)

| Order | Condition | Behavior | ID | Design Rationale |
|---|---|---|---|---|
| 1 | `destination == null` | Throws `ArgumentNullException` | C1 | Highest priority. Preallocating the destination is core to the contract; violation must be explicitly surfaced—not silently swallowed. |
| 2 | `sources == null` | Returns immediately, performs no population | C8 | Empty source is treated as a no-op. Does *not* exempt C1 (if `destination` is `null`, still throws). |
| 3 | `destination.Length < sources.Count` | Throws `ArgumentException` | C2 | Prevents buffer overrun. Explicit failure is safer than silent corruption. |

### Per-Slot Element-Level Processing

| Condition | Behavior | ID | Design Rationale |
|---|---|---|---|
| Source element is `null` | Slot **retains its original value**, no overwrite | C4 | “Update only when data is present”: absence of source data leaves the target unchanged, preserving valid prior content in long-lived destination arrays. |
| Destination slot is `null` | **Skipped** (no-op), no exception thrown | C5 | Target object must already exist to be updated; a `null` destination has no object to assign to. Per-element pre-validation is *not* performed at the entry point. |
| Slot index ≥ `sources.Count` | Retains original value | C3 | Only the first `sources.Count` slots are populated; excess slots remain untouched. |

### Combined Effect

Updates occur *only* for slots where both the source element *and* the destination slot are non-`null`. All other slots retain their pre-call state. Thus, the destination array remains safe, stable, and predictable across repeated reuse and refresh cycles.

**Typical scenario**: A UI table holds 100 persistent rows. Every 5 seconds, fresh data is pulled from the server and applied via `MapListInto`. If one row is missing in a given fetch (source element is `null`), that row retains its prior display—no flash-to-blank. If a destination slot hasn’t yet been initialized (`null`), that slot is skipped and will update normally once initialized in a future iteration.

### Update Semantics: “Update Only When Data Is Present” (Non-Diff)

`MapListInto` does *not* detect whether values have changed (i.e., it is **not diff-based**). As long as the source element is non-`null` *and* the destination slot is non-`null`, it **unconditionally overwrites**, even if old and new values are identical. Skipping occurs *only* in two cases:

- Source element is `null` (no data to apply → retain original value, C4)
- Destination slot is `null` (no object to update → no-op, C5)

This is **data-driven skipping**, not change-driven skipping: no old-vs-new comparison is performed; no “skip if unchanged” optimization is provided.

### Multi-Path Guarantees: Consistent Null Semantics Across All Four Execution Paths

`MapListInto` routes execution based on type registration status and configuration—but **C4/C5 semantics are strictly identical across all paths**:

| Execution Path | Trigger Condition | Per-Element Null Handling |
|---|---|---|
| Pipeline inline | Pipeline enabled | `src == null` → retain original (C4); `dst == null` → skip (C5) |
| Source-generator batch delegate | Type registered with `[MapFrom]` | Fast array path and generic indexed loop both enforce dual null guards (C4/C5) |
| `UpdateAction` loop | Generated `Update` method available | `src == null` → retain original (C4); `dst == null` → guarded internally by generated method |
| `MapperBase` fallback | Unregistered type, runtime path | Defensive `return` + per-element C4/C5 enforcement |

Regardless of execution path—whether registered/unregistered, Pipeline on/off—the null-handling behavior is identical.

## Semantic Risks

- **C5: Silent no-op on uninitialized slots**: Newly allocated array elements default to `null`; mapping is silently skipped with no error. Always initialize each element after preallocation.
- **C6: Residual values**: When reusing arrays, unmapped fields retain values from prior iterations. Business state changes require manual cleanup.
- **C7: Nested object recreation**: Nested references are newly instantiated each iteration. For true zero-allocation across the full object graph, nested objects must also be reused manually.

## Comparison with `MapList`

| Dimension | `MapList` | `MapListInto` |
|---|---|---|
| Target collection | New `List<T>` created on every call | Reuses caller-preallocated array |
| Element objects | New instance created per element | Reuses existing elements; only mapped fields overwritten |
| Allocations (in reuse scenarios) | One new target object per element | Zero top-level allocations (nested references recreated per iteration, C7) |
| Unmapped fields | Default-initialized | Retain residual values from prior iteration (C6) |
| When source element is `null` | Corresponding position in new list is `null` | Destination slot retains original value (C4, never set to `null`) |
| Typical use cases | One-off mapping | High-frequency batch: pagination, streaming, UI binding buffers |

## Companion Examples

Runnable demos reside in `samples/NCMapper.Samples/`:

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 201   # Basic mapping: sub-scenarios 201-list-map-into / 201-list-map-into-reuse
dotnet run --scenario 604   # Performance comparison: 604-maplist-into (MapList vs MapListInto throughput)
```

Core example source code (single-source-of-truth, runnable via `dotnet run --scenario`):

[!code-csharp[MapListInto batch in-place mapping](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapListInto)]

Reuse loop and preservation of excess slots (C3) appear in the performance demo for scenario 604 (`06-Infrastructure/PerformanceScenarios.cs`, `dotnet run --scenario 604`); throughput comparison is implemented in the `MeasureMapListIntoPerformance` method in that file.

## Related Documentation

- [Mapping Rules (Semantics) for Collections and Dictionaries](../../explanation/semantics/mapping-rules.md): Engine semantics and collection-type matrix for conventional `MapList` collection mapping.
- [Performance](../infrastructure/performance.md): Empirical performance measurements for collection scenarios.
