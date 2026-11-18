> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: how-to/orm/freesql-advanced
Diagnostic codes involved: (none)
Library version: v1.0.0+
Last updated: 2026-09-15
---

# FreeSql Projection (Part III): Advanced Terminal Methods

## Overview

This article covers advanced terminal methods integrated into FreeSql: `ToTreeList`, `ToDictionary`, `ToChunkDto`, `ToListAsync`, and their asynchronous variants. These methods are invoked in a fluent chain after `.AsDto()`, supporting tree-structured data, dictionary mapping, chunked reading, and asynchronous operations.

Core APIs:  
`.AsDto().ToTreeList<TDto>()` /  
`.AsDto().ToDictionary<TDto>(keySelector)` /  
`.AsDto().ToChunkDto<TDto>(size, action)`

---

## 1. ToTreeList: Tree-Structured Data Projection

### Usage

Maps flat hierarchical data (with a `ParentId` field) into a tree-shaped DTO list:

> ⚠️ **FreeSql `ToTreeList` does not read the `[MapTree]` attribute**. FreeSql uses its native tree-building capability—based on either the conventional `ParentId` field name or the `[Navigate(nameof(ParentId))]` attribute. The `[MapTree]` attribute is only consumed by Dapper’s `ToTreeList` (see [Dapper (Part II)](dapper-advanced.md)).

```csharp
using FreeSql.DataAnnotations;
using NCMapper.FreeSql;

public class CategoryEntity
{
    public int NodeId { get; set; }
    public int? ParentNodeId { get; set; }  // Parent node ID (FreeSql naming convention)
    public string Name { get; set; } = "";

    // Self-referencing navigation property — used by FreeSql's native tree building,
    // and serves as the source for DTO child collection mapping
    [Navigate(nameof(ParentNodeId))]
    public List<CategoryEntity> Children { get; set; } = new();
}

[MapFrom(typeof(CategoryEntity))]
public class CategoryDto
{
    public int NodeId { get; set; }
    public string Name { get; set; } = "";

    [MapProperty(typeof(CategoryEntity), nameof(CategoryEntity.ParentNodeId))]
    public int? ParentId { get; set; }

    // Must match the entity's navigation property name — `MapList` performs recursive name-based mapping.
    // Do NOT apply `[MapIgnore]`; otherwise, even matching names result in silent flattening (empty `Children`),
    // and the strict name-matching validation cannot intercept it.
    public List<CategoryDto> Children { get; set; } = new();
}
```

[!code-csharp[FreeSql ToTreeList Tree Projection](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_ToTreeList)]

**Key Points**:

- The entity must contain both a parent-key field and a self-referencing navigation property (`[Navigate(nameof(ParentKey))]` one-to-many collection), per FreeSql’s tree-query convention.
- **The DTO’s child-collection property participates in `MapList`’s name-based mapping**, and must share the **exact same name** as the entity’s self-referencing navigation property (its element type must be the DTO itself, enabling recursive mapping). Do **not** annotate it with `[MapIgnore]`—otherwise, even name matches will silently flatten the hierarchy (leaving `Children` perpetually empty), and the strict name-match validation cannot catch this.
- Returns `List<TDto>`, where root nodes (those with null parent key) appear at the top level, and `Children` properties are recursively populated.
- Supports arbitrarily deep nesting (bounded only by `MaxRecursionDepth`).
- Matching succeeds if *any* self-referencing navigation property name in the entity matches *any* self-referencing collection property name in the DTO (first match wins). If no names match, `MapConfigurationException` is thrown (see warning below).

> ⚠️ **Strict Name-Match Validation**: FreeSql’s `ToTreeList`/`ToTreeListAsync` validates that the DTO’s self-referencing collection property name matches the entity’s self-referencing navigation property name. **If no names match, `MapConfigurationException` is thrown** (previously, unmatched cases silently omitted subtrees and returned only roots with empty `Children`). The exception message lists all candidate property names from both sides and includes migration guidance: rename the DTO’s child-collection property to match the entity’s navigation property name (or vice versa). Note: FreeSql ignores `[MapTree]`; customizing the DTO’s child-collection property name has no effect here (Dapper’s tree contract is documented in [Dapper (Part II)](dapper-advanced.md)). If the entity lacks any self-referencing navigation property, validation is skipped (no impact on non-tree scenarios).

---

## 2. ToChunkDto: Chunked Reading

### Usage

Callback-based chunked reading for large datasets—each chunk contains `size` items—and uses client-side in-memory mapping (compiled projection delegates map items one-by-one in memory, *not* SQL-side projection, as explained below):

[!code-csharp[FreeSql Chunked Reading via ToChunkDto](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_ToChunkDto)]

**Key Points**:

- Callback-based chunked reading for large datasets, with configurable chunk size.
- Client-side in-memory mapping: `ToChunk` fetches entities in batches and maps them in memory (memory usage remains bounded per batch; no SQL-side projection).
- Both normal and fallback paths now automatically `Include` navigation properties (previously, the normal path did not `Include`, causing navigation properties to be silently `null`; this has been fixed).
- Asynchronous variant `ToChunkDtoAsync<TDto>(size, asyncAction, cancellationToken)` is available—a true async implementation: pages are fetched using `Skip/Take` + `ToListAsync(ct)`, and cancellation tokens are checked before each page fetch (callbacks won’t execute after cancellation).
- Memory semantics differ between sync and async versions: the synchronous version uses FreeSql’s native streaming `ToChunk` block processing, while the asynchronous version performs paginated queries (only `size` items are materialized per page; memory usage remains bounded per batch).
- `Skip/Take` pagination relies on stable sorting: always include an explicit `OrderBy` clause in your query; otherwise, large result sets may silently skip or duplicate rows across pages.

---

## 3. ToDictionary

### Usage

Maps query results to a dictionary, selecting keys from entity properties:

[!code-csharp[FreeSql ToDictionary Dictionary Projection](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_ToDictionary)]

**Key Points**:

- The key selector operates on `TSource` (the source entity), *not* `TDto` (signature: `ToDictionary<TKey, TTarget>(Func<TSource, TKey>)`).
- Returns `Dictionary<TKey, TDto>`.
- Duplicate keys overwrite earlier entries.

### ToDictionary in Multi-Table Queries

```csharp
var dict = fsql.Select<OrderEntity, CustomerEntity>()
    .LeftJoin((o, c) => o.UserId == c.Id)
    .AsDto()
    .ToDictionary<int, OrderDetailDto>(o => o.OrderId);  // keySelector operates on the primary table entity
```

### ToLookup

Maps query results to a grouped lookup table (`ILookup<TKey, TDto>`):

[!code-csharp[FreeSql Grouped Lookup Table via ToLookup (keySelector operates on source entity)](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlAdvanced_ToLookup)]

**Key Points**:

- The key selector operates on `TSource` (the source entity), *not* `TDto` (signature: `ToLookup<TKey, TTarget>(Func<TSource, TKey>)`).
- Returns `ILookup<TKey, TDto>`; the indexer returns an empty sequence (no exception) for missing keys.
- FreeSql 3.5.103 does not provide `ToLookup`/`ToLookupAsync`; implementation falls back to synchronous `ToList` to fetch entities, followed by in-memory grouping (each group is mapped in bulk via `MapList`).

---

## 4. Asynchronous Terminal Methods

| Method | Description | Return Type |
|---|---|---|
| `.ToListAsync<TDto>()` | Asynchronous list retrieval | `Task<List<TDto>>` |
| `.FirstAsync<TDto>()` | Asynchronous first item (returns `null` if none found) | `Task<TDto?>` |
| `.ToDictionaryAsync<TKey, TDto>()` | Asynchronous dictionary mapping | `Task<Dictionary<TKey, TDto>>` |
| `.ToChunkDtoAsync<TDto>(size, action)` | Asynchronous chunked reading | `Task` |

### Usage

```csharp
// Asynchronous list
var dtos = await fsql.Select<UserEntity>()
    .Where(u => u.Age > 20)
    .AsDto()
    .ToListAsync<UserDto>();

// Asynchronous first item
var first = await fsql.Select<UserEntity>()
    .Where(u => u.Id == 1)
    .AsDto()
    .FirstAsync<UserDto>();
```

---

## 5. IncludeMany: Child Collection Projection

When a DTO contains a child collection property, use `IncludeMany` to eagerly load child collection data:

```csharp
public class OrderEntity
{
    public int OrderId { get; set; }
    public List<OrderItemEntity> Items { get; set; } = new();
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public int OrderId { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}
```

[!code-csharp[IncludeMany Eager Loading of Child Collections Followed by AsDto Projection (Scenario 503)](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_IncludeManyNavigation)]

**Key Points**:

- `IncludeMany` eagerly loads child collections (one-to-many relationships).
- Field trimming for child collections: the DTO’s child collection property determines which columns `IncludeMany` selects.
- Column trimming is automatic under projection paths; manual handling is required in fallback paths.

---

## Common Misuses

### Misuse 1: GroupBy + AsDto

```csharp
// ❌ Misuse: GroupBy + AsDto is unsupported
// FreeSql’s GroupBy returns ISelectGrouping<TKey,TValue> (grouped aggregates), not entity queries
// → No entity structure exists for NCMapper to perform property mapping.
// NCMapper blocks this pattern at compile time using [Obsolete(error:true)]
var dtos = fsql.Select<UserEntity>()
    .GroupBy(u => u.Age)
    .AsDto()  // Compile-time error!
    .ToList<UserAgeGroupDto>();
```

> ℹ️ **Why blocked**: `GroupBy` returns `ISelectGrouping<TKey, TValue>`, whose elements are groups—not individual entities—so there is no entity structure for NCMapper to match properties against. NCMapper prevents this chain at compile time using `[Obsolete(error:true)]`, and the error message directly suggests the two alternative approaches below.

**Alternative Approaches (Two Paths)**:

```csharp
// Path A (Recommended): Use FreeSql’s native aggregation syntax to perform GROUP BY in SQL
// Aggregation results map directly to anonymous types/DTOs—no NCMapper projection needed
var stats = fsql.Select<UserEntity>()
    .GroupBy(u => u.Age)
    .ToList(g => new { Age = g.Key, Count = g.Count() });
// Can also map to DTO: .ToList<UserAgeGroupDto>(g => new UserAgeGroupDto { ... })

// Path B (Not recommended; provided only as an implementation option): Fetch into memory first, then LINQ GroupBy
// First call .AsDto().ToList() to materialize objects in memory, then use LINQ’s .GroupBy()
var rows = fsql.Select<UserEntity>()
    .AsDto()
    .ToList<UserDto>();                 // Full dataset loaded into memory
var grouped = rows
    .GroupBy(d => d.Age)                // Grouping performed in memory
    .Select(g => new { Age = g.Key, Count = g.Count() })
    .ToList();
```

> ⚠️ **Path B is not recommended**: It requires loading the full (or filtered) dataset into memory before performing client-side grouping—resulting in **significant performance overhead**, including unnecessary full-materialization cost and loss of SQL-side `GROUP BY` pushdown and index acceleration. If grouping/aggregation can be expressed in SQL (e.g., counting, summing, averaging), **always prefer Path A**. Path B should only be considered when SQL-based aggregation is truly impossible—choose it carefully based on real-world performance constraints.

### Misuse 2: Incorrect Understanding of `ToDictionary`’s `keySelector` Signature

The `keySelector` in `ToDictionary<TKey, TTarget>(Func<TSource, TKey> keySelector)` operates on the **source entity (`TSource`)** (see signature in `NCMapperFreeSqlExtensions.ToDictionary`), *not* the projected DTO. When only one generic parameter is supplied (i.e., `TTarget` is omitted), type inference fails and causes a compile-time error.

```csharp
// ✅ Correct: keySelector operates on source entity (TSource); Key = UserEntity.Id
var dict = fsql.Select<UserEntity>()
    .AsDto()
    .ToDictionary<int, UserDto>(u => u.Id);

// ❌ Misuse: Only one generic parameter supplied (TTarget cannot be inferred → CS0411 compile error)
var dict = fsql.Select<UserEntity>()
    .AsDto()
    .ToDictionary<UserDto>(u => u.Id);
```

### Misuse 3: Incorrectly Using `[MapTree]` in FreeSql Scenarios

```csharp
// ❌ Incorrect: FreeSql's ToTreeList does NOT read the `[MapTree]` attribute.
// `[MapTree]` is only read by Dapper's ToTreeList; FreeSql uses its native tree-building capability.
[MapFrom(typeof(CategoryEntity))]
[MapTree("Id", nameof(CategoryEntity.ParentNodeId))]  // Positional parameters only (constructor supports positional only; named parameters won't compile)
public class CategoryDto { ... }

// ✅ Correct: FreeSql's ToTreeList relies on naming conventions for parent-key fields or the `[Navigate]` attribute.
// DTOs do NOT require `[MapTree]`; child-collection properties must match the name of the self-referencing navigation property in the entity (participating in name-based mapping — do NOT apply `[MapIgnore]`).
[MapFrom(typeof(CategoryEntity))]
public class CategoryDto
{
    public int NodeId { get; set; }
    public List<CategoryDto> Children { get; set; } = new();  // Name matches the entity's navigation property; recursive name-based mapping applies.
}
```

> `[MapTree]` is a tree-mapping attribute dedicated to Dapper integration (supporting customizable `IdProperty`/`ParentIdProperty` field names). FreeSql’s `ToTreeList` leverages FreeSql’s native tree-building capability and does **not** read `[MapTree]`. See [Dapper (II): Advanced Methods](dapper-advanced.md) for details.

---

## Summary

| Capability | API | Notes |
|---|---|---|
| Tree Projection | `.AsDto().ToTreeList<TDto>()` | Uses FreeSql’s native tree building (ignores `[MapTree]`; enforces strict name matching) |
| Chunked Reading | `.AsDto().ToChunkDto<TDto>(size, action)` | Client-side memory mapping (controllable batch size); async version performs true per-page asynchronous execution |
| Grouped Lookup Table | `.AsDto().ToLookup<TKey, TDto>(keySelector)` | Key selector operates on source entity (`TSource`); synchronously fetches entities then groups them in-memory |
| Dictionary | `.AsDto().ToDictionary<TKey, TDto>(keySelector)` | Key selector operates on source entity (`TSource`) |
| Async List | `.AsDto().ToListAsync<TDto>()` | Asynchronous variant |
| Async Dictionary | `.AsDto().ToDictionaryAsync<TKey, TDto>()` | Asynchronous variant |
| Child Collections | `.IncludeMany(o => o.Items)` | One-to-many eager loading |
| `GroupBy` Interception | `[Obsolete(error:true)]` | Blocks usage at compile time |

---

## Compose Multi-Source Projection (Two-Table / Three-Table `AsDto`)

In FreeSql multi-table JOIN scenarios, NCMapper provides `NCMapperFreeSqlComposeExtensions.AsDto<T1, T2>()` and `AsDto<T1, T2, T3>()`, which map results from `ISelect<(T1, T2)>` or `ISelect<(T1, T2, T3)>` queries into merged DTOs. After calling `AsDto()`, you obtain a `FreeSqlComposeDtoSelector<T1, T2>` (or `FreeSqlComposeDtoSelector3<T1, T2, T3>` for three tables), offering the same family of terminal methods as single-table projection (8 variants total: `List`, `First`, etc.).

[!code-csharp[FreeSql Two-Table JOIN AsDto Compose](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_MultiTableTwoWay)]

On the DTO side, use `[MapProperty]` to declare which source each field originates from (same name-matching and aliasing rules as single-table projection). The example in this section demonstrates this pattern in a runnable form.

---

## Current Fluent DTO Support Status

- **`ToTreeList` Tree Building**: The Fluent pipeline’s `Tree()` / `Tree(id, parent, children)` declarations share the same registration store (`RuntimeSnapshotStore.GetTreeConfig`) with the `[MapTree]` attribute (opt-in semantics — tree mounting is disabled unless explicitly declared); custom triplet mounting behaves identically across both channels.
- **Per-Property `NoAutoInclude`**: Within a Fluent `Member(d => d.X)` block, the parameterless terminal `NoAutoInclude` declaration registers via `IsPropertyNoAutoInclude` and causes the navigation collection phase to skip Include for that property — semantically mirroring the per-property `[MapNoAutoInclude]` attribute.
- **Nested Fluent Sub-DTOs**: Projection translation checks propagate through nested layers (non-translatable declarations in sub-DTOs also trigger fallback — all nested declarations are uniformly consumed along the chain).

---

Runnable examples on this page (Scenario 503, FreeSql in-memory mode):

[!code-csharp[Async Chunked Reading: `ToChunkDtoAsync` (requires stable `OrderBy` sorting)](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlAdvanced_ToChunkDtoAsync)]

[!code-csharp[Grouped Lookup Table: `ToLookup`](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlAdvanced_ToLookup)]

---

## Companion Examples

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 503
```

Source code resides in `samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs`, in methods such as `ToChunkDtoAsyncDemo` and `ToLookupDemo`. Scenario 501 additionally includes synchronous chunked reading and navigation loading demos (`ToChunkDto` / `IncludeNavigation` in `05-Orm/FreeSqlScenarios.cs`).

---

## Further Reading

- [FreeSql (I): Single-Table](freesql-single-table.md) · [FreeSql (II): Multi-Table](freesql-multi-table.md)
- Technical Documentation: [NCMapper.FreeSql Extension Package](../../explanation/orm-freesql.md) §11.2
- Extension Source Code: `src/NCMapper.FreeSql/NCMapperFreeSqlExtensions.cs`
