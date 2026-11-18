> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §18 Limitations and Considerations -->

## 18. Limitations and Considerations

### 18.1 Expression Tree Limitations

- `dynamic` type mapping is not supported.
- `ref struct` / `Span<T>` is not supported.
- Maximum cycle reference depth is limited to 64 levels.

### 18.2 Source Generator Limitations

- Only classes annotated with `[MapFrom]` are processed (structs and record structs are excluded).
- Cross-assembly mapping requires the target assembly to also reference `NCMapper.SourceGen`.
- Cross-type fields referenced via `[MapProperty].FromSource` must be visible within the same compilation unit.

### 18.3 Projection Limitations

`AsDto` projection is implemented independently by each ORM extension package; limitations vary depending on the ORM’s translation capabilities. The following sections categorize limitations by ORM.

#### Common Limitations (Shared Across All Three ORMs)

| Limitation | Behavior | Description |
|------------|----------|-------------|
| `[Convert]` / `[Mask]` / Pipeline PostMap / `IsFromJson` | Automatic degradation | `DegradationDetector` triggers two-step mapping upon detection. |
| Collection types: `T[]` / `HashSet<T>` | Automatic degradation | Only `List<T>` and assignable interfaces (`IEnumerable<T>` / `IList<T>` / `ICollection<T>` / `IReadOnlyList<T>` / `IReadOnlyCollection<T>`) are projectable. |
| Record-nested DTOs (lacking parameterless constructor) | Automatic degradation | Record types lack a parameterless constructor → `ProjectionBuilder` cannot generate `MemberInit`. |
| Naming-convention flattening | Supported | `AddressCity → src.Address.City`: automatically splits on uppercase boundaries in the **target property name**. |
| Dot-path notation `[MapProperty(typeof(T), "A.B")]` | **Supported uniformly** across all four engines: source generator / runtime / projection / Compose(SourceGen) | All four engines parse valid paths segment-by-segment (segment matching uses `OrdinalIgnoreCase`; intermediate nodes must be reference types — i.e., classes, interfaces, or `string`, not value types). If an intermediate node is `null`, no exception is thrown: at runtime/generation, result defaults to `default`/`null`; in SQL projection, corresponding columns yield `NULL`. Leaf-node matches against global ignore lists are skipped (granularity = leaf node). **All failures are explicitly observable**: <br>• Source generator emits warning `NCMAP001` (ordinary) or `NCMAP005` (Compose cross-table), including failing segment name and declaring type.<br>• Runtime throws `MapConfigurationException` (explicit failure).<br>• Projection builder skips the property during construction and logs a trace (with segmented failure/interruption wording).<br><br>`[Mask]` / `[Convert]` × dot-path: ignored (but observable) at generation/runtime (`NCMAP030`/warning); projection degrades to two-step mapping producing *plaintext* output (**plaintext refers only to the `[Mask]` / `[Convert]` attribute form**; Fluent-channel obfuscation declared via registered mappings remains effective in degraded memory mapping — registered mappings override both dot-path and flattened leaf nodes). Note: naming-convention flattening (`AddressCity → src.Address.City`) automatically splits the **target property name**, independent of whether users manually write dot-path syntax. |

#### FreeSql-Specific Limitations

| Limitation | Behavior |
|------------|----------|
| Collection navigation projection | Not supported in SQL translation; always triggers degradation. |
| Reference navigation nested projection | Supported in SQL projection (rewritten using `IIF` for null-safety); **null navigation yields an empty-shell DTO**, differing from EF Core’s null-propagation semantics — see detailed note below. |
| Nested projection depth | Maximum 3 levels (exceeding this triggers degradation). |
| `AsDto` after `GroupBy` | **Compilation error** (`[Obsolete(error:true)]`). |
| `ToChunk` | Use `ToChunkDto` instead (returns pre-mapped DTO chunks without degradation). |
| `ToTreeList` | Directly supports `ToTreeList<TTarget>()` for hierarchical DTO projection (no degradation required). |

**Null-shell semantics for reference navigation nested projection (FreeSql channel)**: Shared-core nested projection expresses null propagation as `IIF(navigation == null, null, new NestedDTO())`. EF Core’s translator supports this pattern; FreeSql’s does not. To preserve SQL projection capability, NCMapper strips the `IIF` guard *before* SQL translation in the FreeSql channel (`IIF` → direct `new NestedDTO()`). Behavioral differences:

- **Non-null navigation**: Identical behavior on both sides — no difference.
- **Null navigation**: EF Core returns `null` (exact null propagation); FreeSql returns an **empty-shell DTO** (nested object is non-`null`, `int` fields default to `0`, reference fields default to `null`) — code relying on `dto.Dept != null` to detect association existence must adapt under FreeSql (e.g., check `dto.Dept.Id != 0`).
- **Chunk-memory path** (`ToChunkDto` / `ToChunkDtoAsync`): Does *not* strip the `IIF` guard, preserving exact `null` semantics (identical to EF Core).
- **SQL projection fallback**: If translation still fails after stripping the guard, automatic degradation to memory mapping occurs, accompanied by a trace containing the original exception (no unhandled crash; diagnostics remain visible).
- **Collection navigation nesting**: Still always degrades (conservative policy unchanged).

#### EF Core-Specific Limitations

| Limitation | Behavior |
|------------|----------|
| Collection navigation projection | Supported (`Select+ToList` subquery), but target type must be `List<T>` or an assignable interface. |
| Nested projection depth | Maximum 8 levels. |
| `AsDto` after `GroupBy` | **Compilation error** (`[Obsolete(error:true)]`). |
| `AsEnumerable().AsDto()` | **Compilation error** (`[Obsolete(error:true)]`). |
| `AsNoTracking` | Enabled by default; can be disabled via `NCMapperOptions.EFCoreProjectionAsNoTrackingDefault`. |

#### Dapper-Specific Limitations

| Limitation | Behavior |
|------------|----------|
| Multiple result sets | Not supported; use `QueryMultiple` + `Mapper.MapList` composition instead. |
| Projection expressions | Not applicable (Dapper relies on column-name-based `TypeMap`, not expression trees). |

> **Projection implementation is internal API**: Each extension package’s `ProjectionBuilder` and `DegradationDetector` are `internal`. Users interact indirectly via terminal operations like `AsDto()`. The source generator no longer emits public projection extension methods.

#### Fluent Channel Boundaries

| Boundary | Description |
|----------|-------------|
| `Configure` method body supports **pure chained static calls only** | Dynamic constructs (e.g., `if`/`for`/assignments/local variables) trigger `NCMAP_F08`; lambdas support property paths only (`?.`/method calls trigger `NCMAP_F05`/`F07`; constant integer index segments in `Map` source paths are permitted; variable/method-call indices are rejected). |
| `FromSource` block restricted zone | `ConvertBack` (F30), `NoAutoInclude` (F31), and `AfterMapping` (F32) are prohibited inside `FromSource` blocks — composite surfaces lack reverse-writeback, auto-inclusion, or hook landing points. |
| `NCMAP019` exemption | Once a Fluent type is registered by the source generator, DTO naming heuristic hints are **automatically exempted** for Fluent DTOs. Removing the declaration restores hints; additions/removals support incremental validation. |
| ORM projection/degradation boundary | Per-declaration routing: purely translatable declarations proceed to projection; declarations involving `Mask`/`Convert`/`FromJson`/`AfterMapping`/`FromSource` force degradation — see [ORM Degradation Mechanism Explanation](./orm-degradation.md) §2.3 and each ORM’s “Fluent DTO Support Status” section in their respective how-to guides. |

### 18.4 AOT / NativeAOT Limitations

- **Source generator path**: AOT-friendly — all code generated at compile time.
- **Compose**: Public APIs annotated with `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]` (AOT restriction annotations). AOT-compatible when `RegisterGeneratedCompose` delegate is registered by the source generator; falls back to runtime `MakeGenericType` path — which is *not* AOT-compatible.
- **Validate**: Parameterless `Validate()` is AOT-compatible (uses compile-time validation results from source generator; annotated `[Experimental("NCMapper001")]` indicates API stability warning, *not* AOT restriction). `Validate(params Assembly[])` is annotated `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`, thus *not* AOT-compatible (relies on reflection-based assembly scanning).
- **DeepCopy**: Source generator path is AOT-friendly; runtime expression-tree path is annotated `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`.
- **ORM Extensions (FreeSql / EF Core)**: Projection path (`AsDto` → `ToList`/`ToChunkDto`, etc.) is **unconditionally unavailable under AOT** — `BuildProjection` uses `MakeGenericMethod` to construct SQL projection expression trees, with *no* source-generator alternative (`RegisterGenerated*` registrations cover seven scenarios, none involving Projection). Degraded path (in-memory `MapList`/`Compose`) is conditionally available *if* annotated with `[MapFrom]`, and further depends on the ORM’s own AOT compatibility at runtime.
- **ORM Extension (Dapper)**: Mapping portion annotated with `[MapFrom]` uses source generator (AOT-safe); `Query` delegate compilation uses `Expression.Compile()`, which is *not* AOT-compatible.
- **General degradation path**: When `NeedsDegradation=true`, `Mapper.MapList`/`Compose` — if annotated with `[MapFrom]`, delegates to source-generated code (AOT-safe); otherwise falls back to runtime compilation (AOT-incompatible).
- Production environments are recommended to annotate *all* mappings with `[MapFrom]` and rely exclusively on the source generator path.

**Package-level trim compatibility declaration**: The project files for `NCMapper`, `EFCore`, `FreeSql`, and `Dapper` libraries all declare both `<IsAotCompatible>true</IsAotCompatible>` and `<IsTrimmable>true</IsTrimmable>`, providing complete, unambiguous package-level trim compatibility declarations — enabling trim analysis without false positives due to missing annotations.

**AOT Reflection Guard**: `tools/AotReflectionGuard` scans the public method bodies of all three ORM extension packages during CI builds, using dual channels (`MetadataLoadContext` + `PEReader`) to inspect IL. Any detected reflection call *without* corresponding `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]` annotations causes CI to fail — ensuring AOT reflection constraints (24 items) never break silently and remain continuously monitored.

### 18.5 Performance Recommendations

- Use the source generator path in production (zero first-use latency, ~13–27 ns on .NET 10.0 Linux).
- Extension methods `MapToXxx()` offer the fastest path (zero delegate overhead, JIT-inlinable, ~13–27 ns).
- Avoid `Compose` on hot paths (reflection overhead).
- For bulk mapping, prefer `MapList` (inlined `for` loop, no per-element delegate).
- Prefer the source generator path for deep copying (~179 ns on .NET 10.0 Linux), auto-generated via `[MapFrom]`.
- `MetricsEnabled` is disabled by default to avoid `Interlocked` overhead.
- .NET 10 PGO provides free performance gains for runtime paths (−6% to −47%), though `DeepCopy` exhibits regression risk on .NET 10.

### 18.6 Record Types + Cyclic Reference Graphs: Source-Generated DeepCopy Automatically Falls Back to Runtime Path

- **Conclusion (No Manual Intervention Required)**: The source generator **automatically performs a three-valued classification (Acyclic/Cyclic/Unknown) on the property graph of `record` types (or any type lacking a parameterless constructor) at compile time**. If the classification yields **Cyclic or Unknown** (e.g., self-reference `A.Self = A`, bidirectional reference `A↔B`, tree structures with parent pointers, or properties typed as `object`, interfaces, open generics, or `virtual` members — i.e., edges that cannot be statically resolved), the generated `DeepCopy` method body is **automatically rewritten** to `return Mapper.DeepCopyRuntimeOnly<T>(src, ctx);`, thereby delegating directly to the runtime path (which uses `ConditionalWeakTable`-based cyclic reference detection and full validation). Simultaneously, warning **NCMAP012** is emitted. Acyclic `record` types still use the source-generator fast path (skipping `RegisterDeepCopyTarget` registration). Thus, users can simply call `Mapper.DeepCopy(recordObj)` and obtain **correct results** (no stack overflow), requiring no additional action — only Cyclic/Unknown types forfeit the performance benefits of source generation and fall back to the runtime path overhead.

- **Root Cause**: The `record` path constructs the destination object via constructor invocation (`new T(arg1, arg2, ...)`), where the `dst` object is instantiated *inside* the constructor — **making it impossible to pre-register `dst` into the cyclic reference detection table before construction**. In contrast, the parameterless-constructor path executes `var dst = new T()` first, then immediately calls `Mapper.RegisterDeepCopyTarget(src, dst)` to pre-register `dst`, enabling recursive calls like `Mapper.DeepCopy(src.Prop)` to detect and break cycles upon hitting an already-registered `dst`. Because pre-registration is fundamentally impossible for constructor-based paths, the source generator opts for **compile-time detection + full fallback**, rather than generating code that would infinitely recurse.

- **Detection Strategy**: `HasCircularReference` performs a DFS traversal over the public instance property graph of the type (automatically stripping `Nullable<T>` annotations and extracting element types from collections, e.g., `List<Node> → Node`) to determine whether a path exists from the type back to itself.

- **Scope of Impact**: Affects only the source-generated path (Layer 1, `GeneratedDeepCopyCache<T>.Func`). The runtime provider path (Layers 2/3, based on `ConditionalWeakTable`) handles cyclic reference graphs in `record` types correctly; fallback redirects execution to this path.

- **Manual Intervention (Edge Cases Only)**: Typically unnecessary. Explicit action may be considered only in the following scenarios:
  - Cyclic references are invisible at compile time (e.g., object graphs assembled dynamically at runtime): call `DeepCopyProviderBase.Current.DeepCopy(source)` directly to force the runtime path.
  - To route *all* `DeepCopy` operations for a given type exclusively through the runtime provider: unregister the source-generated delegate via `Mapper.RegisterGeneratedDeepCopy<T>(null)`.

- **Typical Scenarios**: Self-referencing `record`s, `record` bidirectional navigation properties, and `record` tree structures with parent pointers — all are automatically detected at compile time and gracefully fall back; users remain unaware.
