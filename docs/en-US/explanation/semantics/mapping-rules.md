> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: explanation/semantics/mapping-rules
Library Version: v1.0.0+
Last Updated: 2026-11-27
---

# Semantic Sharing Layer: Mapping Rules

> This page is the **single-source-of-truth** for mapping matching rules (Semantic Sharing Layer Contract: this page defines the semantics of *matching rules*, *nesting*, *collections*, and *type conversion*; it is referenced by two how-to pages, eliminating redundant descriptions). API syntax details and usage examples are covered in operational guides such as [declarative/basics](../../how-to/declarative/basics.md); this page focuses exclusively on engine-level semantics.

## 1. Name Matching Convention Takes Precedence Over Configuration

The engine’s default matching rule is: **properties with identical names and compatible types are automatically mapped**, requiring no explicit declaration. Explicit declarations are needed only when mismatches occur:

| Mismatch Type | Attribute Channel | Fluent Channel |
|---------------|-------------------|----------------|
| Name Mismatch Mapping | `[MapProperty("Source", "Target")]` | `.Member(d => d.Target).From(s => s.Source)` |
| Ignore | `[MapIgnore]` | `.Member(d => d.Target).Ignore()` |
| Custom Conversion | `[Convert]` / `[ConvertBack]` | `.Convert` / `.ConvertBack` (four variants, within the `Member` clause) |
| Property-Level Formatting | `[MapFormat("FormatString")]` | `.Format(format)` (within the `Member` clause; source `IFormattable` → target `string`, mutually exclusive with `[Convert]`; error codes `NCMAP045`/`F66`) |

Both channels declare equivalent semantics for the same mapping pair (see [api-comparison](../../reference/api-comparison.md) for a detailed comparison); declaring the same mapping pair in both channels simultaneously triggers the mutual-exclusion error `NCMAP_F11`.

> **Fluent Syntax Note (Three-Part Member Syntax)**: Field-level declarations uniformly follow the pattern `.Member(dest)` to anchor the destination → `.From(src)` to specify the source → terminal transformations applied sequentially. The destination field name appears only once. The Fluent column in the table above includes the anchoring segment; the full usage is e.g., `cfg.For<S,T>().Member(d => d.OrderId).From(s => s.Id)`.

**Matching Scope Details**: Public, readable/writable properties only; case-insensitive name matching; properties with incompatible types are automatically skipped (no exception thrown).

**Data Source Semantics for Name-Mismatch Mapping**: In `[MapProperty(typeof(T), nameof(T.Field))]` (equivalent Fluent: `.Member(d => d.X).From(s => s.Y)`), the first type parameter `T` specifies the **actual source type from which the field value is read**: if `T` equals the primary source type specified by `[MapFrom]`, it's a same-source mapping (most common); if `T` equals another source type, it's a cross-table or Compose mapping—i.e., the value is drawn from another source object, which is supported and **does not require** consistency with the primary source. If the field does not exist: same-source mapping reports `NCMAP001`; cross-table mapping reports `NCMAP005`. If the target property type cannot be assigned from the source field type, `NCMAP002` is reported (independent of whether the first parameter matches the primary source).

**Mapping Rule Priority** (when multiple mechanisms apply to the same property):

| Priority | Mechanism | Description |
|----------|-----------|-------------|
| 1 (Highest) | Ignore (`[MapIgnore]` / `.Ignore()`) | Annotated properties are skipped entirely and excluded from mapping |
| 2 | Custom Conversion (`[Convert]` / `.Convert()`) | Converters execute first, overriding other mapping logic |
| 3 | Name-Mismatch Routing (`[MapProperty]` / `.Member(d => d.X).From(...)`) | Specifies the source property explicitly |
| 4 (Lowest) | Name Matching | Default behavior: automatic matching of identically named, type-compatible properties |

**Whitelist Mode (Pair-Level Switch)**: The above default matching can be disabled per mapping pair — via `[MapNoAutoMatch]` on the DTO class (Attribute channel), or `cfg.For<S,T>().NoAutoMatch()` (Fluent channel). Both channels provide semantically equivalent behavior:

| Item | Behavior When Whitelist Is Enabled |
|------|-----------------------------------|
| Three Disabled Behaviors | ① Automatic same-name pairing for undeclared properties<br>② Configuration-aware supplemental matching<br>③ Automatic flattening (e.g., `AddressCity` no longer auto-resolves to `Address.City`) |
| Unchanged | Core mapping mechanics (method generation, registration, invocation remain unaffected), explicit `[MapProperty]` / `.Member(d => d.X)` declarations and their chained transformations, global ignore remains fail-closed |
| Explicit Declaration Boundary | Same-property annotation `[MapProperty]` (Attribute channel constructor signature: `(Type fromSource, string fromField)`; `fromField` must be explicitly provided); Fluent side: `.Member(d => d.X)` constitutes an explicit declaration; omission defaults to same-name matching. Transformation attributes (`[Convert]`/`[Mask]`/`[MapFormat]`/`[MapWhen]`) without `[MapProperty]` trigger `NCMAP047` (Error; two resolutions: add `[MapProperty]` or remove the transformation attribute) |
| Reverse Derivation | Only reverses forward explicit declarations (automatically matched properties no longer participate in reverse mapping, including flattening write-backs) |
| ORM Projection | Whitelist forces downgrade to two-step in-memory mapping (`IProjectionPolicy` protocol bit — undeclared columns must not be projected; column pruning becomes ineffective) |
| Runtime Reflection Path | All reflection-based mapping paths execute under unified guard — property assignment (Block path), `MemberInit` fast path, and constructor-by-name matching (for `record` or parameterized constructors) share identical decision logic; “undeclared same-name properties are not paired” holds across all paths; for `record` targets under whitelist, undeclared constructor parameters always receive `default` |

### Known Limitations and Observability of Whitelist Mode

**① Diagnostics for Undeclared Properties — No Dedicated “Whitelist Skipped” Category (Known Limitation)**

Under whitelist mode, undeclared properties are **silently unmapped**: they belong neither to “mapping failure”, nor are they flagged as “intentionally skipped”. Current diagnostic landscape (as of 2026-10-23, verified against `src/NCMapper/` source code):

| Tool | Whitelist Awareness | Source Evidence |
|------|---------------------|-----------------|
| `MapValidator` | **No whitelist awareness** — undeclared properties are counted in `unmapped` statistics regardless; diagnostics cannot distinguish “whitelist-skipped undeclared” from “genuine missing mappings” | Zero occurrences of `NoAutoMatch` or “whitelist” keywords in `MapValidator.cs` |
| `MapTracer` | **No whitelist awareness** — no “whitelist skipped (undeclared)” category | Zero occurrences of `NoAutoMatch` or “whitelist” keywords in `MapTracer.cs`; skip reasons limited to `MapIgnore`, `GlobalIgnore`, and source-type mismatch only |
| `ExpressionBuilder` (reflection mapping path) | **Trace support for only one subset**: when a target property bears `[Convert]`/`[Mask]`/`[MapFormat]`/`[MapWhen]` but lacks `[MapProperty]`, a trace message “mapping transform attribute present without `[MapProperty]` in whitelist mode” is emitted *if* `MapTracer.GloballyEnabled` is true | `ExpressionBuilder.cs:173` (detection family `HasWhitelistTransformAttribute`, defined at `:446`; message text at `:175`) |

**Therefore**: The more aggressively whitelist mode is enabled, the less reliably `MapValidator`’s `unmapped` count reflects actual defects — because a significant portion consists of properties users *intentionally chose not to map*, yet the library currently **does not separate them** from genuine omissions. This is a **known limitation**, not an implemented capability.

**Observability Alternative**: Compile-time `NCMAP047` (Error) already covers the most error-prone subset — “transformation attribute present but `[MapProperty]` missing”. This pattern is the leading cause of “assumed mapping vs. actual whitelist skip”, hence the Error serves as a safety net; all other undeclared properties are silently skipped.

**② Whitelist Coverage State Under Inheritance**

Whitelist annotations (`[MapNoAutoMatch]`, declared with `AttributeUsage(..., Inherited = true)`) and Fluent pair-level `.NoAutoMatch()` behave differently under inheritance scenarios:

| Pattern | Scenario | Status (as of 2026-10-23) |
|---------|----------|----------------------------|
| Pattern A′ | Derived DTO **explicitly declares** `[MapFrom]` for pairing + base class carries whitelist annotation | **Converged**: Snapshot path reads along the base class chain; whitelist takes effect |
| Pattern B | Derived DTO has **zero declarations** (pure inheritance, no `[MapFrom]` on derived type itself) | **Projection downgrade path closed**: `DegradationDetectorBase` performs three attribute scans, all with `inherit: true` (`:343` class-level / `:366` property-level / `:496` nested property-level); base-class whitelist is visible to derived types, triggering projection downgrade |

Pattern B’s closure is guarded by a pinned test case: `NoAutoMatchRuntimeTests.Inherited_Leg4_ProjectionScan_DerivedDto_Q6_Measured` has transitioned from “gap exists” to **verified fix assertion** (`Assert.True(needs)` + assertion that downgrade reason contains `MapNoAutoMatchAttribute`).

> **Scope Limitation**: The table above addresses **only the projection downgrade path**. Pure reflection path inheritance (`Inherited_Leg3_PureReflection_DerivedDtoInheritsWhitelist`, using `IsDefined(inherit: true)`) and property/nested-property level scanning have independent pinned tests; this page does not elaborate further. Per-path verification details reside in the `NoAutoMatchRuntimeTests` test suite.

### Other Known Behaviors Related to Whitelist Mode

- **Sub-pairs Do Not Inherit Base-Class Whitelist**: Subtype pairs registered via `.MapSubclass<>()` / `[MapSubclass]` are extracted independently and do **not** inherit the parent pair’s whitelist evaluation point — sub-pairs still perform automatic matching.
- **“Opposite-Side Annotation” Scope Boundary Across Two Paths**: Reflection path detects symmetrically on both sides (annotating source type with `[MapNoAutoMatch]` also activates whitelist); code-generation path reads only the target type’s inheritance chain (annotation on entity side has no effect). This is **not a defect**, but reflects correct semantic separation across layers.

## 2. Nesting and Flattening

- Nested object properties on the source side can be accessed directly via **dot-path notation**, e.g., `s => s.Customer.Name` or its corresponding attribute declaration; the generator expands and evaluates the path.
- PascalCase Nesting Flattening: Flat properties on the target DTO (e.g., `CustomerName`) are automatically matched to nested source paths like `Customer.Name` (resolved at compile time by the source generator).
- Reverse Write-Back: Flat properties can be written back into nested entity paths; attempting to instantiate intermediate nodes triggers `NCMAP031`; null source clears leaf nodes.
- Known Limitation of Explicit Dot-Path: Mapping values do **not consume** `[Mask]`/`[Convert]` (observable hint `NCMAP030`).

**Index Path Segments** (`A.B[0].C`): When traversing collections mid-path, use index segments `[n]` to select the *n*-th element before continuing to access properties. Index segments may appear at any position. Semantics match `ElementAtOrDefault(n)`: null collection, empty collection, or out-of-bounds index all return `default`, with no exception thrown. Engine behavior across four contexts:
- Runtime `Mapper.Map` and source generator: consistent `ElementAtOrDefault` + null-guard short-circuiting.
- EFCore projection: translated to SQL `LIMIT 1 OFFSET n` subquery (SQL lacks `ORDER BY`, so semantics are **unordered selection of one row**; empty collection yields `NULL`).
- FreeSql projection: SQL-side translation unsupported → automatic downgrade to in-memory mapping (values correct, but loses SQL-side column pruning).
- Consecutive indices `A[0][1]` are unsupported (enforced restriction across all engines).
- Reverse-direction collection index paths are skipped during write-back (skipped + traced).
- Path segment parsing failure: source generator reports `NCMAP001` (including failed segment name); runtime throws `MapConfigurationException`.

**Recursive Nesting Mapping Protection** (self-referential structures of same type, e.g., tree-shaped DTOs): Compile-time inline depth limit `Mapper.MaxInlineDepth` (default 5); exceeding it halts inlining and falls back to delegate call `MapperBase<T>.Map`. Runtime employs dual counters for depth checking; exceeding resets counters and throws `MapException`. Recursive depth limit `Mapper.MaxRecursionDepth` (default 64, max 4096; configuration method documented in [DI Integration](../../how-to/infrastructure/di.md)). Deep recursion beyond limits throws `MapException`; configure based on actual structural depth or adjust mapping design.

### Nested navigation is the caller's responsibility (in-memory mapping does not load it)

"In-memory mapping" means the two entry points that map objects already materialized in memory, without any SQL projection: `Mapper.Map` in the core library and `IDbConnection.AsDto` in the Dapper channel. Both carry the same contract:

> **They only map fields — they do not load nested navigation.** The engine never appends a `JOIN` for a nested DTO property, never triggers lazy loading, and never issues a second query. It only copies the properties of *the object the caller already holds* onto the target DTO.

Consequently, when a source entity's navigation property is not loaded (its value is null), the corresponding nested object on the DTO is **null** — that is expected behavior, not a mapping defect. Fetching nested data is the caller's job, per channel:

| Channel | Who fetches the nested data | DTO shape when it was not fetched |
|---|---|---|
| `Mapper.Map` (core library) | The caller loads it at the query layer first (`Include` / `JOIN` / explicit load), then hands the entity to the mapper | Nested object is `null` |
| `IDbConnection.AsDto` (Dapper) | The caller writes the SQL: related tables must be `JOIN`ed or selected via subquery | Nested fields not selected by the SQL are `null` |
| `AsDto` (EFCore / FreeSql projection channel) | The engine translates nesting into SQL `JOIN` and fetches it itself (projection channel — a different contract) | — |
| `AsDto` (EFCore / FreeSql after downgrade to in-memory mapping) | The ORM auto-`Include`s what it can (observable: the dependency list is traced); navigation fields it still cannot reach are `null` | Nested object is `null` |

**Boundary against the ORM projection channel**: the projection channel (`AsDto` on `IQueryable` / `ISelect`) translates nesting into SQL, so its semantics necessarily differ; the "no navigation loading" rule here constrains only the two **in-memory** entry points. See [ORM Projection and Degradation](../orm-degradation.md) for how the two channels relate through degradation.

**Why not "load it automatically for me"**: in-memory mapping has no query context (connection, transaction, tracking state). Issuing a query from inside the mapper would make N+1 patterns and implicit network round-trips uncontrollable, and would turn a pure mapping function into one with side effects. The library therefore states this boundary explicitly instead of doing it silently.

## 3. Collections and Dictionaries

- Collection elements recursively apply the same matching rules (supported collection forms: `List<T>`, arrays, `Dictionary<K,V>`, `HashSet<T>`, etc.).
- When the target collection element type is unmappable, `NCMAP007` is reported (source property unmapped, Info level).
- Conversion matrices for special cases (e.g., collection-to-dictionary, non-generic dictionary) are demonstrated in samples scenarios 201 (Basic Mapping) and 401 (Deep Copy).

**Supported Collection Type Matrix** (`CollectionTypeDetector` auto-detects source and target collection types; usage instructions in [declarative/basics](../../how-to/declarative/basics.md); in-place batch mapping in [MapListInto](../../how-to/mapping/map-list-into.md)):

| Collection Type | Target Type | Behavior |
|-----------------|-------------|----------|
| `List<T>` / `T[]` | `List<U>` / `U[]` | Creates new collection |
| `IEnumerable<T>` / `ICollection<T>` / `IList<T>` / `IReadOnlyList<T>` | `List<U>` | Enumerates then creates new `List` |
| `HashSet<T>` / `ISet<T>` | `HashSet<U>` | Creates new `HashSet` |
| `Dictionary<K,V>` / `IDictionary<K,V>` / `IReadOnlyDictionary<K,V>` | `Dictionary<K,V2>` | Creates new `Dictionary` |
| `Queue<T>` / `Stack<T>` | `Queue<U>` / `Stack<U>` | Creates new collection, preserving element order |
| `ObservableCollection<T>` | `ObservableCollection<U>` | Creates new bindable instance |

**Semantic Highlights**:

- All collection mappings create new collection instances; source collections remain unmodified. Modifications to source collections after mapping do not affect targets.
- Nested collections (e.g., `List<List<T>>`, `Dictionary<string, List<T>>`) are processed recursively, layer by layer.
- Dictionary mapping preserves keys (key types must match or support implicit conversion; keys do not undergo mapping transformations); only values undergo element-level mapping logic.
- Direct `List`→`Dictionary` conversion is **not supported** (key source ambiguous, belongs to application-layer business logic): first `MapList` to `List`, then `ToDictionary`.

**Internal Mechanism** (`CollectionCopier` dispatcher): `CollectionTypeDetector` identifies source/target collection types → selects copy strategy based on type combination → invokes element-type mapping (via source generator or runtime expression tree, same path as single-object mapping) → creates and populates target collection. Type detection occurs during expression construction, imposing zero runtime reflection overhead; memory footprint for large collections = source collection + target collection (non-streaming).

## 4. Type Conversion Chain

Before assignment, the engine resolves conversions in the following order:

1. **Direct Assignment by Name and Type**: Direct assignment occurs if types are implicitly convertible (determined by `IsImplicitConvertible`).
2. **Enum ↔ String**:  
   - `string` → `enum`: Exact member-name matching (case-sensitive; case-insensitive fallback is indicated by diagnostic `NCMAP023`); numeric string literals (alias enum fallback is covered by `NCMAP024`).  
   - `null` maps to `default`; **empty string throws `MapException`** (does *not* fall back to `default`).  
   - Both source generators and runtime engines behave identically.
3. **Custom Converters**: `[Convert]` (or `.Convert`) matches static method signatures against four templates (`NCMAP035/036/037` validate reference legality).
4. **Completely Incompatible Types**: Skip the member and report `NCMAP027` (aligned with AutoMapper semantics).

**Built-in Type Conversion Table** (recognized at expression-building time—no runtime reflection overhead; usage examples in [declarative/basics](../../how-to/declarative/basics.md)):

| Source Type | Target Type | Example |
|---|---|---|
| Enum | `string` | `UserStatus.Active` → `"Active"` |
| `string` | Enum | `"Active"` → `UserStatus.Active` |
| Between numeric types | — | `int` → `long`, `decimal` → `double` |
| Nullable type | Corresponding non-nullable type | `int?` → `int` (when value is present) |

Conversions requiring parsing—e.g., `string` → `DateTime`—are **intentionally unsupported**: date string formats are ambiguous, culture differences exist, and failure-handling strategies (throw exception / use default / skip) require explicit business decisions—each of which must be expressed via a custom conversion method.

**Converter Method Signature Templates** (shared by `[Convert]` / `.Convert` / `[ConvertBack]` / `.ConvertBack`):

| Template | Signature | Use Case |
|---|---|---|
| T0 | `M()` | Constant or fixed value |
| T1 | `M(TField)` | Single source field value (reusable across entities) |
| T2 | `M(TSource)` | Compose multiple fields from the source object |
| T3 | `M(TSource, TField)` | Composition + the field value itself |

Methods must be `static` and declared directly on the anchored type (inherited static methods from base classes are *not* allowed). Accessibility is restricted to `public`, `internal` (same assembly), or `protected internal` (same assembly); `private`/`protected` methods are rejected (`NCMAP035`). Overloads must uniquely match exactly one template; multiple matches trigger `NCMAP036`; no match triggers `NCMAP037`. Converter methods must be pure functions (no side effects), responsible solely for data transformation—not mapping routing.

**Reverse Conversion Consumption Semantics** (DTO → Entity):  
`[Convert]` / `.Convert` defines binding for the **forward direction** only. By default, reverse direction does *not* consume this converter (direct properties are written using standard type-conversion paths, reporting `NCMAP033` Info). To enable custom reverse conversion, explicitly declare `[ConvertBack]` / `.ConvertBack`. During reverse mapping, `TSource` in the template refers to the DTO type. The `[ConvertBack]` attribute supports three declaration positions:  
- On the DTO property (canonical location);  
- On the entity property, consumed under three fallback rules: “no source” (entire DTO as input), “matching pair fallback” (DTO preferred), and “leaf-property write-back fallback” (DTO preferred);  
- It may coexist legally with `[Convert]` on the same property.  
Full consumption logic and write-back path details are defined in [Reverse Mapping Semantics §3.2](../reverse-mapping-semantics.md).

## 5. Reverse Mapping Contract

After declaring `[MapFrom]` (or Fluent `For<S,T>()`), both forward and reverse mappings become available without duplication: the source generator emits separate mapping code for each direction. Reverse mapping (DTO → Entity) follows these semantic contracts:

| Contract Item | Semantics |
|---|---|
| Scope of Coverage | Reverse mapping covers **only primary source fields** (i.e., `PropertyMappings` of the `[MapFrom]`-declared source type); sub-source fields (e.g., cross-table `ComposeMappings`) are **excluded from reverse write-back** |
| DTO Declaring Cross-Table Sub-Source Fields | Reverse extension generation proceeds identically (6 overloads, same structure as regular DTOs); however, reverse write-back still applies *only* to primary source fields |
| `null` Write-Back Semantics | If source value is `null` and target path exists → **clear the leaf node** (assign `null` to nullable leafs; assign `default(T)` to non-nullable leafs); if target path does *not* exist → no-op (intermediate nodes are *not* created); if source value is non-`null` → create missing intermediate nodes and write the leaf |
| Converter (`[Convert]`) | Forward converters attached to direct properties are **not consumed during reverse mapping** (converter binding is forward-only); values are written via standard type-conversion paths, reporting `NCMAP033` (Info) |
| Reverse Custom Converter (`[ConvertBack]`) | When a DTO property is annotated with `[ConvertBack]`, reverse mapping **explicitly consumes** that static conversion method—behavior is consistent across both source generator and runtime; `[ConvertBack]` may coexist legally with `[Convert]` on the same property: forward uses only `[Convert]`, reverse uses only `[ConvertBack]`, with no interference |
| Masking (`[Mask]`) | When a `[Mask]`-annotated DTO property participates in reverse write-back, the masked value is written *as-is* into the entity (lossy); `NCMAP034` (Info) warns about this; prefer `[MapIgnore(ToEntity)]` to block it |
| Formatting (`[MapFormat]`) | A `[MapFormat]`-annotated DTO property is **not assigned during reverse mapping**, because formatting output is lossy and culture-dependent—the library avoids guessing reverse-parsing logic; the entity retains its original value, while other fields are written normally, reporting `NCMAP046` (Warn); for round-trip support, declare `[ConvertBack]` explicitly (the two attributes are orthogonal and may coexist on the same anchor—see [Reverse Mapping Semantics §5](../reverse-mapping-semantics.md)) |
| `init`-Only Properties | During update (existing target object), `init`-only properties cannot be assigned; they are skipped and reported as `NCMAP032` (Info) |
| Global Ignored Names | Global `ToEntity` ignore lists (e.g., `IgnoreNamesToEntity`) apply to reverse mapping; matched properties are skipped + `NCMAP013` reported |
| Flattened Properties | DTOs containing PascalCase-flattened properties (e.g., `AddressCity`) are unsuitable for reverse mapping: flattened fields have no corresponding entity property and are silently skipped; to support reverse mapping, declare an explicit object-typed property on the DTO |

Complete reverse-direction decision logic and write-back path semantics are documented in [Reverse Mapping Semantics](../reverse-mapping-semantics.md).

---

## 6. Three Principles (Mapping Declaration Governance)

NCMapper enforces three foundational principles (P1/P2/P3) governing how mapping declarations are consumed. All compile-time diagnostics are concrete manifestations of these principles (detector mappings and full rule catalog in [Diagnostics Rules](../../diagnostics/rules.md)):

| Principle | One-Sentence Summary | Detectors |
|----|----------------------|-----------|
| P1 Channel Exclusivity | A given type pair `(S, T)` may be declared by *only one channel* | `NCMAP_F11` · `NCMAP025` |
| P2 Side Exclusivity | A mapping pair may be registered *at most once per side* | CS0579 · `NCMAP025` |
| P3 Configuration Anchoring | Property-level configuration annotations require a class-level mapping anchor to take effect | `NCMAP019` · `NCMAP042` |

Detailed breakdown:

- **P1 Channel Exclusivity**: A type pair `(S, T)` may be declared *either* via attribute channel *or* Fluent channel—but not both. Both channels converge into a single compile-time snapshot; dual declaration constitutes ownership conflict and blocks generation for the entire pair. Different type pairs may freely mix channels. Detectors: `NCMAP_F11` (dual-declaration conflict, Error) · `NCMAP025` (unordered mutual reference, Error).
- **P2 Side Exclusivity**: A mapping pair may be registered at most once on either side—e.g., a class may bear `[MapFrom]` at most once (duplicate annotation triggers compiler error CS0579); `For<A, B>` and `For<B, A>` must not coexist. Detectors: CS0579 · `NCMAP025`.
- **P3 Configuration Anchoring**: Eleven property-level mapping configurations—`[MapProperty]`, `[MapIgnore]`, `[Mask]`, `[Convert]`, `[ConvertBack]`, `[MapWhen]`, `[MapNoAutoInclude]`, `[BeforeMapping]`, `[AfterMapping]`, `[MapFormat]`, and `[MapNoAutoMatch]`—take effect *only* when their declaring class has a mapping anchor (`[MapFrom]` or `cfg.For<S,T>()` targeting it). Without such an anchor, all annotations are ignored—mapping falls back to pure name-based matching. Detectors: `NCMAP019` (Error: configuration present but no anchor) · `NCMAP042` (Info: DTO naming heuristic applied).

**Design Intent**: Both channels converge into a single compile-time snapshot; the three principles guarantee **uniqueness of declaration ownership** for every mapping pair—eliminating any ambiguity requiring runtime resolution of “which declaration wins.” Plain-language explanations, remediation paths for `NCMAP019`/`042`, and boundaries for external assemblies/dynamic types are covered in [Mapping Configuration Ownership Principles](mapping-config-ownership.md).

## 7. Boundaries of the Three Principles (External Objects)

The three principles (§6 P1 Channel Exclusivity / P2 Side Exclusivity / P3 Configuration Anchoring) govern only “channel declarations + class-level mapping declarations + property-level configuration annotations.” The following four categories lie **outside this governance domain** (they are *not* exceptions to the rules—they simply fall outside the scope of the core primitives used for detection):

| External Object | Description |
|-----------------|-------------|
| Global Ignore Lists | Assembly-level configurations like `[assembly: MapDefaults]` with `IgnoreNames`, etc.—these are global, class-agnostic, and apply regardless of anchoring (see [Global Mapping Configuration](../../how-to/infrastructure/global-config.md)) |
| Nested Name-Based Recursion | Baseline behavior for nested object matching by name; P3 applies *only* to property-level annotations *within* such nested structures |
| Class-Level `[MapTree]` | Tree construction definition (activated by `ToTreeList` call); not a mapping-pair configuration |
| `[MapSubclass]` | Lacks anchoring; detected by dedicated diagnostic `NCMAP_F49` (detector responsibility separation) |

---

## 8. Related Diagnostics

The complete set of diagnostics triggered by violations of the rules described here is listed in [diagnostics/rules.md](../../diagnostics/rules.md); relevant diagnostic codes include:  
`NCMAP001 / 002 / 006 / 007 / 027 / 030` (matching),  
`NCMAP023 / 024 / 033 / 035 / 036 / 037` (conversion),  
`NCMAP043 / 044 / 045 / 046` (the `[MapFormat]` family),  
`NCMAP019 / 042` (P3 detectors).

## 9. Further Reading

- [declarative/basics](../../how-to/declarative/basics.md) (Usage-oriented: handling name mismatches, conversions, directionality, and common pitfalls)  
- [MapListInto Bulk In-Place Mapping](../../how-to/mapping/map-list-into.md) (Usage-oriented: collection reuse scenarios)  
- [Reverse Mapping Semantics](../reverse-mapping-semantics.md) (Reverse-direction decision logic and write-back path)  
- [API Comparison Reference Page](../../reference/api-comparison.md) (Selection guidance)
