> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §11.0–11.1 Design Principles and Automatic Degradation Mechanism -->

## 11. ORM Projection

### 11.0 Design Principles

**`AsDto` solves the "Entity → DTO property mapping" problem**: property matching, type conversion, nested navigation, flattening, data masking, and custom converters. It transforms `[MapFrom]` / `[MapProperty]` declarations into ORM-executable `Expression<Func<TSource, TTarget>>` projection expressions.

**`AsDto` does *not* solve the "aggregation/expression projection" problem**: composite queries such as `GroupBy` + `Sum`/`Count`, computed columns, or conditional expressions are the native responsibility of the ORM. NCMapper provides no automation here; users must compose such projections manually using their ORM’s native `Select` syntax.

**The responsibilities of these two capabilities are distinct and non-interchangeable.**

---

NCMapper delivers ORM projection capability via three independent extension packages. A single SQL query performs both data retrieval and DTO mapping—eliminating the need to first fetch entities and then map them in-memory. Each package exposes `AsDto()` as its sole terminal operation entry point. Internally, each package uses its own `ProjectionBuilder` to construct the projection expression, while the `DegradationDetector` automatically evaluates degradation conditions: when the DTO employs features that cannot be translated to SQL (e.g., `[Convert]`, `[Mask]`, Pipeline PostMap, collection types like `T[]`/`HashSet<T>`, or records lacking parameterless constructors), NCMapper seamlessly falls back to a two-step process — “SQL query for entities → in-memory `MapList`” — ensuring *all* mapping features remain fully functional without requiring manual intervention from the user.

```xml
<PackageReference Include="NCMapper.FreeSql" />
<PackageReference Include="NCMapper.EFCore" />
<PackageReference Include="NCMapper.Dapper" />
```

Three runnable examples (demo conditions configured per sample dataset):

[!code-csharp[FreeSql: One-liner query + mapping](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto)]
[!code-csharp[EF Core: Equally concise](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_SingleTableAsDto)]
[!code-csharp[Multi-table projection + First (FreeSql two-table Compose chain)](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_TwoTableFirst)]

**Projection capabilities**: direct assignment for same-type properties, implicit C# numeric conversions, automatic inference of navigation properties (via naming convention flattening, e.g., `AddressCity → src.Address.City`, or dot-path notation `"Address.City" → src.Address.City`), and recursive nested object mapping (when child types are annotated with `[MapFrom]` and source types match).

**`AsDto` is a terminal operation** — filtering (`Where`) and sorting (`OrderBy`, `Skip`, `Take`) must be applied on the entity-level `IQueryable` / `ISelect` *before* calling `AsDto()`. `AsDto()` returns an intermediate `DtoSelector` object; the actual query executes only upon invoking terminal methods such as `ToList<TTarget>()` or `First<TTarget>()`.

### 11.0.1 Snapshot-Based Consumption Architecture (Snapshot-First, Reflection Fallback)

Both projection and in-memory mapping consume **the same snapshot**: at compile time, the source generator emits `[MapFrom]` / Fluent declarations as runtime metadata (comprising identity segments, property-detail segments, and cross-table `ComposeMappings` details), which are registered into the `RuntimeSnapshotStore` (a type-keyed registry) by module initializers. Both the main projection pipeline and the degradation detector uniformly retrieve mapping details via `RuntimeSnapshotStore.TryGetMappingDetail` (exact `(source, target)` key match), enabling three-state dispatch:

| State | Condition | Behavior |
| --- | --- | --- |
| ① Detail Hit | Entry exists and detail is non-empty | Snapshot is authoritative — projection built strictly from detail; zero reflection, zero attribute reading |
| ② Detail Unreachable | Identity key is `off` (detail is empty) | Fluent channel enforces forced degradation; `[MapFrom]` channel follows existing evaluation order |
| ③ Miss | Unregistered pair | Reflection fallback — attribute parsing (pure runtime mode) |

Projection expression caching is versioned by the registry’s `ContentVersion`: any content change (registration, merge, cleanup) increments the version, causing cache mismatches and automatic recomputation — no manual cache invalidation is required after declaration changes (e.g., assembly reloads or test registrations).

> Consumption-side architectural principle: **"Projection and mapping consume the same snapshot"** — ORM projection (`AsDto`), degradation detection, and in-memory mapping pipelines all read from the *same* set of compile-time-emitted declaration details, eliminating “dual-read divergence” between generation-time and runtime.

### 11.1 Automatic Degradation Mechanism

Each extension package implements its own degradation detector (`FreeSqlDegradationDetector` / `EfCoreDegradationDetector`) inside `AsDto`’s terminal methods. The Dapper package has *no* degradation detector — it always uses the full in-memory mapping pipeline:

| Path | Condition | Behavior |
| ---- | --------- | -------- |
| Normal Path | DTO contains no untranslatable features | `ProjectionBuilder.Build` constructs expression → ORM-side projection (single SQL yields DTO directly) |
| Degraded Path | Degradation condition detected | SQL retrieves entities → `Mapper.MapList` performs in-memory mapping (all features fully active) |

**Conditions triggering degradation** (evaluated by each ORM extension package in priority order):

| Check | Description |
| ------- | ----------- |
| Class-/property-level attributes implementing `IProjectionPolicy.RequiresDowngrade == true` | `[Convert]`, `[Mask]`, `[MapProperty(IsFromJson = true)]` all return `true` |
| NCMapper-namespace attributes not implementing `IProjectionPolicy` | Safety fallback: undeclared attributes default to degradation |
| Non-projectable collection types (C-1) | Degradation occurs when DTO property is of an untranslatable type such as `T[]` or `HashSet<T>` |
| Nested DTO lacks parameterless constructor (C-3) | Degradation occurs for record types or others lacking a default constructor |
| Global ignore list hit (target-side or source-side) | Degradation occurs if DTO property matches the ignore list, or if an aliased `[MapProperty].FromField` source field matches the list (mapping-pair level exemption `OverrideIgnore = true` prevents degradation) |
| Pipeline PostMap registered | Degradation triggered when `HasPostMapFor(sourceType, targetType)` returns `true`, switching to in-memory mapping path |

> **Flattened/dot-path leaf-node hits against the ignore list do *not* trigger degradation**: the degradation detector’s static “source-side hit” check applies *only* to pre-checkable aliased `FromField` source fields. For naming-convention flattening (`AuthorName → Author.Name`) and dot-path expressions (`Author.Name`), leaf-node ignore-list matching occurs during projection expression construction (`TryBuildFlattenedExpression` / `TryBuildDotPathExpression`), and results in *direct omission* of that property from the projection — matching in-memory mapping behavior (property remains unset), **not data loss**, and thus requires no degradation. Granularity is leaf-node only; intermediate navigation node hits are *not* evaluated (consistent with the source generator’s value-model pipeline).

All extension-package degradation detectors include caching (`ConcurrentDictionary`, with keys including Pipeline version numbers); registering new Pipeline callbacks automatically invalidates old cache entries. During degradation, diagnostics are emitted via `MapTracer.Write` (requires `MapTracer.GloballyEnabled = true`).

> External attributes (e.g., DataAnnotations `[Required]`) *do not* trigger degradation — the safety net applies exclusively to attributes in the `NCMapper` namespace.

#### Automatic `Include` Mechanism

In the degraded path (SQL entity query → `Mapper.MapList` in-memory mapping), FreeSql and EF Core automatically analyze cross-table field dependencies declared in the DTO and append corresponding navigation property `Include()` calls — preventing N+1 queries.

**Trigger condition:** When a DTO’s `[MapProperty]` references a field from a source type *other than* `TSource` (e.g., `[MapProperty(typeof(CustomerEntity), "CustomerName")]`, while the DTO is annotated with `[MapFrom(typeof(OrderEntity))]`), the framework analyzes which navigation properties of `OrderEntity` point to `CustomerEntity`, and appends the appropriate `Include()` calls during entity retrieval.

**Behavior:**
- FreeSql: Uses `FreeSqlNavigationCollector` to analyze cross-table fields → builds navigation tree → invokes `IncludeByPropertyName` layer-by-layer  
- EF Core: Uses `EfCoreNavigationCollector` to analyze cross-table fields → builds navigation tree → invokes `Include` / `ThenInclude` layer-by-layer  
- Dapper: No such mechanism (Dapper has no ORM-level navigation concept; `SELECT` statements are fully user-controlled)  
- Projection path (SQL-side expression tree): Does *not* trigger automatic `Include` (field mapping is performed entirely within SQL; navigation properties are not loaded)

**Built-in toggle (internal configuration, not exposed as user API):**  
- FreeSql: `FreeSqlNavigationCollector.AutoIncludeEnabled` = `true` (internal constant)  
- EF Core: `EfCoreNavigationCollector.AutoIncludeEnabled` = `true` (private constant)  
- Hardcoded to `true` in current version; no user-configurable global switch is provided. Planned for future iteration as a configurable option under `NCMapperOptions`.

#### `[MapNoAutoInclude]` — Opt-Out of Automatic `Include`

Applied to a DTO property to exclude it from automatic `Include` analysis. Applicable for:

- **Large collection navigation properties**, e.g., `List<OrderDetailEntity>`, where automatic loading is undesirable  
- **Deferred loading scenarios**, where users intend to manually `Include` on-demand  
- **Circular references**, e.g., navigation chains like A→B→A, which could cause infinite recursive `Include`

**Not applicable for:**  
- Reference navigation properties (e.g., `OrderEntity.Customer`) — even when `Included`, they add negligible overhead (single row)  
- Properties *without* cross-table dependencies — triggers diagnostic `NCMAP017` (Info level)

```csharp
[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public int Id { get; set; }

    // Standard cross-table field — Customer navigation property auto-included
    [MapProperty(typeof(CustomerEntity), "CustomerName")]
    public string CustomerName { get; set; }

    // Marked to skip auto-include — OrderDetail navigation will *not* be appended, even with cross-table dependency
    [MapNoAutoInclude]
    [MapProperty(typeof(OrderDetailEntity), "ProductName")]
    public string ProductName { get; set; }
}
```

> **Scope of applicability**: `[MapNoAutoInclude]` takes effect *only* in the FreeSql / EF Core degraded paths; it has no effect in Dapper (SQL is user-controlled); and it is irrelevant in the projection path (automatic `Include` is never triggered there).
