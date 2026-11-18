> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §6.2–6.3 Nested Objects and Automatic Flattening -->

### 6.2 Nested Objects

When both the source and target properties are of `class` type but differ in concrete type (e.g., `CustomerEntity → CustomerDto`), NCMapper automatically performs recursive mapping of nested objects—no manual call to `Mapper.Map` is required.

#### Trigger Conditions

Nested object mapping is triggered **only when all** of the following conditions are satisfied:

| Condition         | Description                                    |
| ----------------- | ---------------------------------------------- |
| Both sides are `class` | Does not trigger for `struct` / `record struct` |
| Not `string`      | `string` follows the type-conversion path        |
| Not a generic type | `List<T>`, `Dictionary<K,V>`, etc. follow collection/dictionary paths |
| Not an array        | `T[]` follows the collection path                |
| Source type ≠ Target type | Identical types use direct assignment (shallow copy) |

```csharp
public class OrderEntity
{
    public int Id { get; set; }
    public CustomerEntity Customer { get; set; }      // Nested object → recursively mapped
    public AddressEntity ShippingAddress { get; set; } // Nested object → recursively mapped
    public List<OrderItemEntity> Items { get; set; }   // Collection → collection mapping path
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public int Id { get; set; }
    public CustomerDto Customer { get; set; }          // CustomerEntity → CustomerDto
    public AddressDto ShippingAddress { get; set; }    // AddressEntity → AddressDto
    public List<OrderItemDto> Items { get; set; }      // Collection elements recursively mapped
}
```

#### Inline Depth Control (`MaxInlineDepth`)

The runtime engine *inlines* nested object mappings into the outer expression tree to avoid delegate invocation overhead. Inline depth is controlled by `Mapper.MaxInlineDepth` (default: `5`):

```
OrderEntity → OrderDto
  └── Customer (depth 1) → inlined
        └── Address (depth 2) → inlined
              └── City (depth 3) → inlined
                    └── ... (depth 4, 5) → inlined
                          └── depth 6 → exceeds MaxInlineDepth → falls back to MapperBase<S,T>.Map() delegate
```

| Depth             | Behavior                                      | Performance     |
| :----------------: | --------------------------------------------- | ---------------- |
| ≤ `MaxInlineDepth` (default 5) | Inlined into outer expression tree; zero delegate calls | Optimal          |
| > `MaxInlineDepth` | Falls back to `MapperBase<S,T>.Map()` delegate call | One extra delegate indirection |

[!code-csharp[Tuning inline depth (range 0–16)](~/samples/NCMapper.Samples/02-Declarative/NestedScenarios.cs#Core_Nested_InlineDepthTune)]

> **Design intent**: Prevent infinite expansion of expression trees caused by cyclic reference types (e.g., `A.Child → B.Parent → A`). After exceeding inline depth, the delegate path employs its own `MaxRecursionDepth` (default `64`) cycle-detection safeguard.

#### Null Handling

When a source nested object is `null`, the corresponding target property is assigned `null` (no empty object is created):

```csharp
var order = new OrderEntity { Id = 1, Customer = null };
var dto = Mapper.Map<OrderEntity, OrderDto>(order);
// dto.Customer == null (does NOT instantiate new CustomerDto())
```

| Engine       | Implementation                                                             |
| ------------ | -------------------------------------------------------------------------- |
| Source Generator | `if (src.Customer != null) dst.Customer = ...;` (conditional assignment; skips if null) |
| Runtime      | `Condition(src.Customer != null, mapExpression, null)` (ternary expression) |

#### Impact of `[MapFrom]` on Nested Mapping

The nested target type **does not require** `[MapFrom]`, but presence or absence affects the source generator’s code generation path:

| Nested Target Type                             | Source Generator Code Generated                                           | Performance     |
| ---------------------------------------------- | -------------------------------------------------------------------------- | ---------------- |
| Has `[MapFrom(typeof(ChildEntity))]`          | `GeneratedMapper.Map_ChildEntity_To_ChildDto(src.Child)` — compile-time direct method call | Zero first-use latency |
| No `[MapFrom]`                                 | `NCMapper.Mapper.Map<ChildEntity, ChildDto>(src.Child)` — falls back to runtime | First use triggers expression compilation |

```csharp
// Recommended: Annotate nested types with [MapFrom] to enable full source-generator pipeline
[MapFrom(typeof(CustomerEntity))]
public class CustomerDto
{
    public string Name { get; set; }
    public string Email { get; set; }
}
```

> The runtime engine ignores `[MapFrom]`—any two distinct non-generic `class` types are automatically recursively mapped (via inlined `BuildMapBody` or `MapperBase<S,T>.Map`).

#### Nested Objects Inside Collections

Collection properties such as `List<ChildEntity> → List<ChildDto>` recursively map each element per the nested object rules:

```csharp
public class OrderEntity
{
    public List<OrderItemEntity> Items { get; set; }
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public List<OrderItemDto> Items { get; set; }  // Element-wise recursive mapping
}
```

| Engine       | Implementation                                                                |
| ------------ | ----------------------------------------------------------------------------- |
| Source Generator | Calls `GeneratedMapper.MapList_X_To_Y(src.Items)` (inlined `for` loop + pre-allocated capacity) |
| Runtime      | Calls `MapperBase<X,Y>.MapList(src.Items, null)` (compiled list delegate)            |

Null elements inside collections are preserved as `null` (index consistency is maintained).

#### Source Generator vs Runtime Comparison

| Dimension            | Source Generator                                    | Runtime Engine                                   |
| --------------------- | --------------------------------------------------- | ----------------------------------------------- |
| Nested with `[MapFrom]` | Calls `GeneratedMapper.Map_X_To_Y()` — compile-time method | Inlines `BuildMapBody` or invokes `MapperBase.Map` |
| Nested without `[MapFrom]` | Falls back to `Mapper.Map<S,T>()` → runtime           | Inlines `BuildMapBody` or invokes `MapperBase.Map` |
| Inline Optimization     | None (always method call)                              | Inlines first `MaxInlineDepth` levels (zero delegate) |
| Cyclic Reference Protection | None (no risk at compile time)                         | Protected by `MaxRecursionDepth` (default `64`)     |
| First-Use Latency       | Zero (generated at compile time)                      | First use triggers `Expression.Compile()`         |
| AOT Compatibility       | Fully compatible                                     | Requires `DynamicCode`                          |
| Null Handling           | Conditional assignment: `if (src != null)`            | Expression-based: `Condition(src != null, map, null)` |

### 6.3 Automatic Flattening

In layered architectures, entities often organize related data via navigation properties (e.g., `Order.Customer.Name`), while DTOs prefer flat structures (e.g., `CustomerName`) to reduce frontend nesting and parsing complexity. Automatic flattening enables the source generator to *statically decompose* PascalCase DTO property names into nested navigation paths matching the source entity’s property chain—eliminating the need for manual `[MapProperty]` attributes or imperative assignment logic.

> **Engine Support**: Automatic flattening is implemented in both the **source generator path** (splitting target property names at PascalCase boundaries, e.g., `AddressCity → Address.City`) and the **ORM projection path**. At runtime, `MapProjection.TryBuildFlattenedExpression` splits candidate names on uppercase letters and builds an `Expression.Property` chain (`AddressCity → src.Address.City`) suitable for SQL translation. The runtime `Mapper.Map` expression-tree engine (`ExpressionBuilder`) supports flattening matching (case-insensitive ordinal segment matching, same rules as source generator/projection, forward-only). Case-insensitive (`OrdinalIgnoreCase`) name matching and explicit `[MapProperty]` binding are also supported.

#### Matching Algorithm

For each DTO property that is **not annotated with `[MapProperty]`** and fails exact-name matching, the source generator executes the following steps:

```
DTO property name "CustomerName"
  │
  ├── 1. PascalCase Splitting — Attempt split at every uppercase letter position
  │     Uppercase positions: [C]ustomer[N]ame → split candidates at index 8
  │     Enumerate all combinations via bitmask → candidates: ["Customer", "Name"]
  │
  ├── 2. Path Validation — Resolve each segment stepwise in the source type (case-insensitive)
  │     "Customer" → OrderEntity.Customer ✓ (type: CustomerEntity, class)
  │     "Name"     → CustomerEntity.Name ✓ (leaf node)
  │
  └── 3. Match Success → Generate assignment: src.Customer?.Name
```

**Splitting Rule Details**:

| Rule               | Description                           |
| ------------------ | ------------------------------------- |
| Split Points       | Every uppercase letter position (excluding first character) |
| Combination Enumeration | Bitmask enumerates all 2^n possible splits |
| Uppercase Limit    | Abandons splitting if >4 uppercase letters (avoids combinatorial explosion) |
| Segment Count      | Only retains candidates with 2–5 segments (1-segment matches are handled earlier) |
| Candidate Ordering | Prefer shorter segment counts first (greedy strategy favors shallower nesting) |
| First Match Wins   | First candidate fully passing path validation is adopted; no backtracking |

**Path Validation Constraints**:

| Constraint             | Description                                  |
| ---------------------- | -------------------------------------------- |
| Intermediate Nodes Must Be `class` | Value types (`int`, `DateTime`, `struct`) and interfaces cannot serve as intermediate nodes |
| Intermediate Nodes Must Have a Getter | Write-only properties (no `get`) cannot appear in path intermediates |
| Leaf Node Type Unrestricted | Final property may be any type (`string`, `int`, `DateTime`, etc.) |
| Case-Insensitive Matching | Each level uses `OrdinalIgnoreCase` |

#### Basic Example

```csharp
// Source Entity — Navigation Properties
public class NmInvoiceEntity
{
    public int Id { get; set; }
    public NmCustomerEntity Customer { get; set; }   // Navigation property
    public NmShipperEntity Shipper { get; set; }     // Navigation property
}

public class NmCustomerEntity
{
    public string Name { get; set; }
    public string Email { get; set; }
}

public class NmShipperEntity
{
    public string Name { get; set; }
    public string Phone { get; set; }
}

// DTO — Flat property names, auto-matched to nested paths
[MapFrom(typeof(NmInvoiceEntity))]
public class NmInvoiceDto
{
    public int Id { get; set; }                    // Exact match
    public string CustomerName { get; set; }       // → Customer.Name
    public string CustomerEmail { get; set; }      // → Customer.Email
    public string ShipperName { get; set; }        // → Shipper.Name
    public string ShipperPhone { get; set; }       // → Shipper.Phone
}
```

A runnable version of nested mapping—the source generator automatically emits assignments like `src.Customer?.Name` (e.g., `dto.CustomerName = invoice.Customer.Name`, `dto.ShipperPhone = invoice.Shipper.Phone`):

[!code-csharp[Flat navigation property walkthrough (direct source-generator invocation)](~/samples/NCMapper.Samples/02-Declarative/NestedScenarios.cs#Core_Nested_InvoiceFlatTour)]

#### Multi-level Nested Flattening

Supports up to **5 segments** of path depth:

```csharp
// Source entity — 4-level nesting
public class OrderEntity
{
    public int Id { get; set; }
    public CustomerEntity Customer { get; set; }
}
public class CustomerEntity
{
    public AddressEntity Address { get; set; }
}
public class AddressEntity
{
    public CityEntity City { get; set; }
}
public class CityEntity
{
    public string Name { get; set; }
    public string Province { get; set; }
}

// DTO — multi-level flattening
[MapFrom(typeof(OrderEntity))]
public class OrderFlatDto
{
    public int Id { get; set; }
    public string CustomerAddressCityName { get; set; }      // → Customer.Address.City.Name (4 segments)
    public string CustomerAddressCityProvince { get; set; }  // → Customer.Address.City.Province (4 segments)
}

// Generated code: dst.CustomerAddressCityName = src.Customer?.Address?.City?.Name;
```

| Path Segments | Example Property Name              | Matched Path                              | Result |
| :--: | ------------------------------- | ----------------------------------- | :---: |
|   2  | `CustomerName`                  | `Customer.Name`                     |   ✅   |
|   3  | `CustomerAddressCity`           | `Customer.Address.City`             |   ✅   |
|   4  | `CustomerAddressCityName`       | `Customer.Address.City.Name`        |   ✅   |
|   5  | `Level2Level3Level4Level5Value` | `Level2.Level3.Level4.Level5.Value` |   ✅   |
|  6+  | —                               | Exceeds limit                         | ❌ Not mapped |

#### Priority Rules

Automatic flattening resides at the **lowest priority** in the matching priority chain:

```
1. [MapProperty] explicit annotation → Highest priority; uses the explicitly specified source member directly.
2. Case-insensitive name match (OrdinalIgnoreCase) → Direct match if source type has a property with identical name.
3. [Convert] with no source property → Converter is still invoked (fieldValue = default).
4. Automatic flattening match → Triggered only when all above rules fail.
```

**Ambiguity Handling**: If the source type contains both a direct property `CustomerName` and a nested path `Customer.Name`, the case-insensitive name match (priority #2) takes precedence and matches the direct property — flattening is **not triggered**.

```csharp
public class OrderEntity
{
    public string CustomerName { get; set; }       // Direct property
    public CustomerEntity Customer { get; set; }   // Navigation property (Customer.Name also exists)
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    // Matches OrderEntity.CustomerName (direct name match), not Customer.Name
    public string CustomerName { get; set; }
}
```

To force usage of the nested path instead of the direct same-named property, use `[MapProperty]` to specify it explicitly:

```csharp
[MapProperty(typeof(OrderEntity), nameof(OrderEntity.Customer))]  // Explicitly route through nesting
public string CustomerName { get; set; }
```

#### Null Safety

The source generator emits **null-conditional (`?.`) chained access** for flattened paths, preventing `NullReferenceException` when intermediate navigation properties are null:

```csharp
// Generated code (output by source generator)
dst.CustomerName = src.Customer?.Name;
dst.CityName = src.Customer?.Address?.City?.Name;

// Value-type leaf nodes — ?. chain yields T?, automatically supplemented with ?? default
dst.CustomerAge = src.Customer?.Age ?? default(int);
```

| Leaf Node Type         | Generated Code                                | Result When Intermediate Is Null |
| ------------------ | ------------------------------------------- | ---------------------------- |
| Reference types (e.g., `string`) | `src.Customer?.Name`                | `null`                       |
| Value types (e.g., `int`)        | `src.Customer?.Age ?? default(int)` | `default(T)`                 |
| Nullable value types (e.g., `int?`) | `src.Customer?.Score`               | `null`                       |

#### Directional Support

Automatic flattening supports both **Entity→DTO direction** (forward mapping annotated with `[MapFrom]`) and **DTO→Entity reverse write-back**:

```csharp
[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public string CustomerName { get; set; }  // Entity→DTO: automatic flattening ✓
}

// Forward: OrderEntity → OrderDto, CustomerName auto-resolved from Customer.Name ✓
var dto = Mapper.Map<OrderEntity, OrderDto>(entity);

// Reverse: OrderDto → OrderEntity, CustomerName written back to Customer.Name ✓
// (Intermediate Customer is auto-created if null; reused if already present, then Name assigned)
var entity = Mapper.Map<OrderDto, OrderEntity>(dto);
```

**Reverse Write-back Semantics (consistent across source generator + runtime engines)**:

| Scenario | Behavior |
|----------|----------|
| Source value (DTO property) is non-null, but intermediate node is null | Creates intermediate instance(s) and writes leaf value (only null segments are created; existing instances are reused). |
| Source value is non-null, intermediate node already exists | Reuses existing instance and writes leaf value (does not overwrite other members of the navigation object). |
| Source value is null | Skips entirely (no intermediate node creation, no leaf assignment; original entity value preserved). |
| Source value is null + `[Convert]` applied | Converter is invoked (with null input); if converter output is non-null → intermediate(s) created (if needed) and leaf written; if output is null → skipped entirely. |
| Intermediate node is an interface / abstract class / lacks public parameterless constructor | Source generator reports NCMAP031 (Error — compilation blocked); runtime skips + logs MapTracer warning. |
| Intermediate segment has `init-only` or private setter | Skipped + NCMAP013 diagnostic (source generator) / Trace (runtime). |
| Path resolution fails (missing segment / value type or `string` breaks path) | Runtime throws `MapConfigurationException` (not cached in failure cache); source generator reports NCMAP001. |
| Collection index path in reverse direction | Not supported: skipped + NCMAP013 (source generator) / Trace (runtime). |

**Priority Rules**: Flattened folding inference respects “same-name direct property first” constraint (if entity has a writable same-name property → ordinary assignment takes precedence, symmetric with forward mapping); explicit `[MapProperty]` path annotation = contract (path takes precedence, consistent with forward mapping). **Path annotations on entity side do NOT participate in reverse mapping**: An attribute like `[MapProperty(typeof(Dto), "A.B")]` on an entity property applies *only* to forward read paths; reverse write-back consumes *only* DTO-side annotations.

> **Interaction of Reverse Write-back with EF Change Tracking / Lazy-Loading Proxies (O-2)**: During reverse write-back, when an intermediate node is null, `new T()` is executed to instantiate it. EF Core change tracking will recognize the new instance as **Added**, scheduling an INSERT of a new row (or modifying foreign key ownership) — this poses a data correctness risk, not merely a construction side effect. Before submitting an edit form (`Map(dto, entity)` + `SaveChanges`), verify: ① Whether “creating associated rows on missing navigation objects” aligns with business expectations; ② With lazy-loading proxies, proxy types usually have public parameterless constructors (so instantiation succeeds), but their navigation property initialization behavior differs from plain entities. To enforce conservative semantics (“update only existing chains, never create new nodes”), use `[MapIgnore(Direction.ToEntity)]` to suppress the corresponding DTO property, or switch to explicit Action callbacks.

#### Relationship with Projection

ORM Projection is implemented independently per ORM extension package (`FreeSqlProjectionBuilder` / `EfCoreProjectionBuilder`) and exposed via the `AsDto()` terminal operator. The source generator no longer emits public Projection extension methods. Projection currently does **not support null-conditional expressions** (e.g., `src.Customer?.Name`). Nested-object flattening in projections is achieved via navigation properties + `[MapProperty]` or naming conventions. See [ORM Projection Design](../orm-projection-design.md).

> Projection expression building and downgrade detection are internal implementations within each extension package; users consume them exclusively via the `AsDto` terminal operator.

**Global Ignore List × Flattening / Dot Paths**: In projection paths, the global ignore list performs source-side ignore evaluation on **leaf nodes only** (the final segment of the resolved path): if the leaf node matches the global ignore list → that property is unmapped (consistent with source generator / runtime in-memory mapping — convergence across all four engines); **intermediate navigation nodes matching the list are ignored for evaluation** (e.g., if `AuthorName`’s intermediate node `Author` matches the list, it has no effect — only a match on the leaf `Name` triggers skipping). Dot-path annotations (`[MapProperty(typeof(Entity), "Author.Name")]`) follow the same rule. This evaluation does **not trigger downgrade**: the projection builder simply skips the property during expression construction (see [ORM Downgrade Mechanism §2](../orm-degradation.md) for downgrade trigger conditions).

#### Boundaries and Limitations Summary

| Condition                          | Behavior                             |
| -------------------------------- | ------------------------------------ |
| Property annotated with `[MapProperty]` | Skips flattening; uses explicit annotation. |
| Source type has same-name property          | Name match takes priority; flattening not triggered. |
| More than 4 consecutive uppercase letters     | Abandons splitting; property unmapped. |
| Path exceeds 5 segments                   | No candidate generated; property unmapped. |
| Intermediate node is value type / interface / `struct` | Path validation fails; tries next candidate. |
| Intermediate node lacks getter            | Path validation fails. |
| Reverse mapping (DTO→Entity)            | Supports folded write-back: creates intermediates if null; skips if source value is null; `[Convert]` exceptions apply; collection indices / interfaces skipped. |
| ORM Projection                        | Supports naming-convention flattening (e.g., `AddressCity` → `src.Address.City`). |
| Runtime engine (no source generator)       | Supports flattening (segment matching uses OrdinalIgnoreCase; forward-only). |
| Inside collection elements                | Flattening applies only to top-level properties; does not recurse into collection elements. |

#### Trade-offs Between Automatic Flattening and `[MapProperty]`

| Dimension         | Automatic Flattening           | `[MapProperty]` Explicit Annotation |
| ----------------- | ------------------------------ | ----------------------------------- |
| Declaration Cost  | Zero (property name *is* the rule) | One attribute line per property     |
| Compile-time Validation | Source generator validates path existence | Strong-typed `typeof` + `nameof` checks |
| Readability       | Path implied by name (requires knowledge of convention) | Mapping relationship immediately obvious |
| Reverse Mapping   | Supported (folded write-back, see above) | Supported                           |
| Projection        | Supported (naming-convention flattening) | Supported                           |
| Typical Use Cases | Rapid prototyping, simple 2–3 level nesting | Production code, explicit contracts required |

> **Recommendation**: For simple scenarios (2–3 level nesting), automatic flattening reduces boilerplate; when explicit contracts are needed (to eliminate path ambiguity or lock down field origin), prefer `[MapProperty]` for clarity and maintainability.
