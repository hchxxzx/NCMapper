> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: how-to/orm/dapper-advanced
Diagnostic codes involved: (none)
Library version: v1.0.0+
Last updated: 2026-09-15
---

# Dapper Integration (Part II): Advanced Terminal Methods

## Overview

This article covers advanced terminal methods for Dapper integration: `ToDictionary`, `ToChunkDto`, `ToTreeList`, and `QueryMultiple`. These methods are invoked in a fluent chain after `AsDto<TEntity>()`, supporting dictionary mapping, chunked reading, hierarchical (tree) data, and multi-result-set scenarios.

Core APIs:  
`.AsDto<T>().ToDictionary<TKey,TDto>(sql, param, keySelector)` /  
`.ToChunkDto<TDto>(sql, param, size, action)` /  
`.QueryMultiple(sql)`

> The terminal family also includes `ToLookup()` / `ToLookupAsync()` (grouping lookup table, returning `ILookup<TKey, TDto>`) and `ToPagedList()` / `ToPagedListAsync()` (SQL-level pagination, returning `PagedList<TDto>`). These share the same selector as the above terminals; usage details and pagination dialects are covered in [Dapper (Part I): Query Series](dapper-query.md) §3 (source anchor: `DapperDtoSelector.cs:513/:543`; `DapperDtoSelector.Paging.cs:28/:65`).

---

## 1. `ToDictionary`: Map to Dictionary

### Usage

Maps query results to a dictionary, where the key is selected from an entity property:

[!code-csharp[Dapper Integration: using NCMapper.Dapper and query patterns](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Query)]

**Key points**:

- The key selector operates on `TEntity` (the source entity), *not* `TDto`.
- Returns `Dictionary<TKey, TDto>`.
- If duplicate keys exist, later entries overwrite earlier ones.
- Parameters: `(sql, param, keySelector)` — `keySelector` is of type `Func<TEntity, TKey>`.

### Why does `keySelector` operate on `Entity` instead of `DTO`?

Because Dapper first maps the result set to the entity (via `TypeMap`), then maps from the entity to the DTO. Key selection occurs at the entity stage to avoid cases where the key property is missing or inaccessible in the DTO.

---

## 2. `ToChunkDto`: Chunked Reading

### Usage

Callback-based batch processing for large datasets, with each batch containing `size` items:

[!code-csharp[Dapper Chunked Reading: `ToChunkDto` (callback per batch of `size` items)](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToChunkDto)]

**Key points**:

- Controls memory footprint in large-data scenarios.
- Internally implemented via SQL pagination + sequential batch reads.
- An asynchronous counterpart `ToChunkDtoAsync` is available.

### Asynchronous Chunked Reading

[!code-csharp[Dapper Asynchronous Chunked Reading: `ToChunkDtoAsync`](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToChunkDtoAsync)]

---

## 3. `QueryMultiple`: Multi-Result Sets

### Usage

Executes multiple SQL statements in one round-trip and returns multiple result sets (implemented as generic `ValueTuple` overloads — *no* `GridReader`-style API):

[!code-csharp[Dapper `QueryMultiple`: Deconstructing Two Result Sets](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_QueryMultiple)]

**Key points**:

- `QueryMultiple<TDto1, TDto2>(sql, param)` returns `(List<TDto1>, List<TDto2>)`; three-result-set overloads use three generic type arguments.
- Result sets map to DTO types in declaration order.
- Ideal for fetching multiple related datasets in a single database connection (reducing round trips).

### Example: Multi-Result Set Scenario

[!code-csharp[Dapper `QueryMultiple`: Three-Result-Set Overload with Three Generic Types](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_QueryMultiple3)]

> Note: Each result set is mapped directly into its corresponding DTO list (*not* row-by-row via `GridReader`). For scalar aggregate results (e.g., counts), use separate dedicated queries.

---

### 3.1 Streaming Query (`QueryUnbufferedAsync`)

For result sets in the tens of millions, use `QueryUnbufferedAsync` to enumerate elements lazily as `IAsyncEnumerable<TDto>`, avoiding full in-memory materialization:

[!code-csharp[Dapper `QueryUnbufferedAsync`: Streaming Enumeration](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_QueryUnbufferedAsync)]

**Implementation details**:

- Internally uses Dapper’s `ExecuteReaderAsync` + `GetRowParser` to achieve true streaming — *not* buffered caching.
- When `CancellationToken` is canceled, no further elements are yielded; already-yielded elements remain unaffected.

**Difference vs. `ToChunkDto`**:

| Method | Pattern | Typical Use Case |
|---|---|---|
| `QueryUnbufferedAsync<TDto>` | `IAsyncEnumerable<TDto>` — element-by-element streaming | Extremely large datasets, streaming pipelines |
| `ToChunkDto<TDto>(size, action)` | Batch callback (`Action` / `Func`) | Bulk persistence, batched message sending |

**Key points**:

- The returned `IAsyncEnumerable<TDto>` must be consumed via `await foreach`; calling `ToListAsync()` defeats streaming by forcing full materialization.
- The database connection remains open during streaming — ensure timely disposal after processing completes.

---

## 4. `ToTreeList`: Hierarchical Projection

### Usage

Maps flat parent-child relational data into a hierarchical DTO list:

```csharp
public class CategoryEntity
{
    public int NodeId { get; set; }
    public int? ParentNodeId { get; set; }
    public string Name { get; set; } = "";
}

[MapFrom(typeof(CategoryEntity))]
[MapTree("NodeId", nameof(CategoryEntity.ParentNodeId))]  // Positional form (; key name must match entity primary key `NodeId`)
public class CategoryDto
{
    public int NodeId { get; set; }
    public string Name { get; set; } = "";

    [MapProperty(typeof(CategoryEntity), nameof(CategoryEntity.ParentNodeId))]
    public int? ParentId { get; set; }

    public List<CategoryDto> Children { get; set; } = new();
}
```

[!code-csharp[Dapper Hierarchical Projection: `ToTreeList` (build tree from flat query)](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToTreeList)]

**Key points**:

- The source entity must expose a `ParentId`-like field (e.g., `ParentNodeId`).
- `[MapTree]` annotates the DTO to declare the key, parent-key, and child-collection property names used for tree construction.
- **Strict Tree Contract**: The DTO *must* declare a writable self-referencing collection property (default name: `Children`; customizable via third parameter of `[MapTree]`). Missing or mis-typed properties throw `MapConfigurationException` (see “Tree Contract” subsection below).
- Returns `List<TDto>` with root nodes at top level and `Children` recursively populated.
- Parameters: `(sql, param)`.
- Custom field names: By default, `[MapTree]` assumes `Id` / `ParentId` / `Children` as key / parent-key / child-collection property names (`MapTreeAttribute` constructor defaults). This differs from the example where the entity primary key is `NodeId`. **If the entity key is not named `Id` or `ParentId`, explicit specification is mandatory**, otherwise tree building fails silently due to missing field resolution. Since these properties are read-only, use positional parameters or named constructor arguments `idProperty:` / `parentIdProperty:` / `childrenProperty:` to specify non-default names:

```csharp
// When entity key is `NodeId` and parent key is `ParentNodeId`
[MapTree("NodeId", "ParentNodeId")]  // Positional form
public class CategoryDto { ... }

// When child-collection property is named `SubCategories` (not default `Children`)
[MapTree("NodeId", "ParentNodeId", "SubCategories")]  // Positional form
public class CategoryDto
{
    public int NodeId { get; set; }
    public int? ParentNodeId { get; set; }
    public List<CategoryDto> SubCategories { get; set; } = new();
}
```

> ⚠️ `[MapTree]` is honored *only* by Dapper’s `ToTreeList`. FreeSql’s `ToTreeList` relies on native tree-building capabilities and *ignores* `[MapTree]`; see [FreeSql (Part III): Advanced Terminal Methods](freesql-advanced.md).

### Tree Contract (`Children` Hard Constraint)

Before building the tree, `ToTreeList` validates each DTO against three criteria. Failure of *any* criterion throws `MapConfigurationException` (error message specifies expected property name + guidance via `[MapTree(childrenProperty: "...")]`):

1. **Existence**: DTO declares a property named `Children` (default) or the name specified by the third `[MapTree]` parameter.
2. **Self-Referencing Collection**: That property is a generic collection whose element type matches the DTO itself (e.g., `List<CategoryDto> Children`).
3. **Writeability**: That property has a setter (required to attach child nodes during tree construction).

```csharp
// ❌ Missing `Children` property → exception (message shows expected name "Children" + custom-name guidance + flat-scenario hint)
public class CategoryDto
{
    public int NodeId { get; set; }
    public int? ParentNodeId { get; set; }
}

// ❌ `Children` type mismatch (string is not a self-referencing collection) → exception (message distinguishes "type mismatch" cause)
public class CategoryDto
{
    public int NodeId { get; set; }
    public string Children { get; set; } = "";
}

// ✅ Writable self-referencing collection property (default name matches)
public class CategoryDto
{
    public int NodeId { get; set; }
    public int? ParentNodeId { get; set; }
    public List<CategoryDto> Children { get; set; } = new();
}
```

> **Flat scenario**: If you only need a flat list (no hierarchy), use `ToList` / `Query` — *do not* use `ToTreeList`. Violating the tree contract triggers an explicit exception; no silent fallback occurs.  
> **Multiple self-referencing collections**: If the DTO declares multiple self-referencing collection properties (e.g., `Children` + `SubItems`), `[MapTree]`’s specified name (or default `Children`) is matched *exactly* — automatic selection of the first candidate is *disabled*, eliminating ambiguity.

---

## Common Misuses

### Misuse 1: Using DTO Type in `ToDictionary`’s `keySelector`

```csharp
// ❌ Misuse: `keySelector` uses DTO type instead of Entity type
connection.AsDto<UserEntity>()
    .ToDictionary<int, UserDto>("SELECT * FROM Users", null, (UserDto d) => d.Id);
// ↑ `keySelector` signature is `Func<UserEntity, TKey>`, *not* `Func<UserDto, TKey>`
// Compile-time error

// ✅ Correct: `keySelector` uses entity type
connection.AsDto<UserEntity>()
    .ToDictionary<int, UserDto>("SELECT * FROM Users", null, (UserEntity e) => e.Id);
```

### Misuse 2: Using `QueryMultiple` in `GridReader` Style

```csharp
// ❌ Misuse: No `GridReader`-style API exists (no `multi.Read<T>()` or `using` disposal semantics)
// var multi = connection.AsDto<UserEntity>().QueryMultiple("SELECT ...");
// var users = multi.Read<UserDto>().ToList();   // Compile failure: `Read<T>()` does not exist

// ✅ Correct: Direct deconstruction via generic `ValueTuple` overload
var (users, orders) = connection.AsDto<UserEntity>()
    .QueryMultiple<UserDto, OrderDto>("SELECT * FROM Users; SELECT * FROM Orders", null);
```

### Misuse 3: Omitting `[MapTree]` on DTO for `ToTreeList`

```csharp
// ❌ Misuse: DTO lacks `[MapTree]` annotation → `ToTreeList` cannot identify parent-ID field
[MapFrom(typeof(CategoryEntity))]
public class CategoryDto
{
    public int NodeId { get; set; }
    public int? ParentNodeId { get; set; }  // No `[MapTree]` → tree construction fails
    public List<CategoryDto> Children { get; set; } = new();
}

// ✅ Correct: Annotate with `[MapTree]` to specify parent-ID field
[MapFrom(typeof(CategoryEntity))]
[MapTree("NodeId", nameof(CategoryEntity.ParentNodeId))]  // Positional form (; key name must match entity primary key `NodeId`)
public class CategoryDto
{
    public int NodeId { get; set; }
    public int? ParentId { get; set; }
    public List<CategoryDto> Children { get; set; } = new();
}
```

### Misuse 4: `ToChunkDto` Batch Size Too Small

```csharp
// ❌ Misuse: Batch size too small (e.g., 1), causing excessive database round-trips
connection.AsDto<UserEntity>()
    .ToChunkDto<UserDto>("SELECT * FROM Users", null, 1, chunk =>
    {
        all.AddRange(chunk);  // 1 item per batch → 10,000 items = 10,000 SQL queries
    });

// ✅ Correct: Reasonable batch size (100–1000)
connection.AsDto<UserEntity>()
    .ToChunkDto<UserDto>("SELECT * FROM Users", null, 500, chunk =>
    {
        all.AddRange(chunk);  // 500 items per batch → 10,000 items = 20 SQL queries
    });
```

---


## Summary

| Capability | API | Feature |
|---|---|---|
| Dictionary | `.ToDictionary<TKey, TDto>(sql, param, keySelector)` | Key selector operates on the Entity |
| Chunked Reading | `.ToChunkDto<TDto>(sql, param, size, action)` | SQL pagination with batch-by-batch execution |
| Asynchronous Chunking | `.ToChunkDtoAsync<TDto>(sql, param, size, action)` | Asynchronous variant |
| Multiple Result Sets | `.QueryMultiple<TDto1, TDto2>(sql, param)` | Generic `ValueTuple` deconstruction for multiple result sets |
| Hierarchical Projection | `.ToTreeList<TDto>(sql, param)` | Uses `[MapTree]` attribute to map parent ID |


## Current Support Status for Fluent DTOs

The support for Fluent DTOs across all endpoints covered in this article (`ToTreeList`, `QueryMultiple`, `ToChunkDto`) aligns exactly with that described in the *Dapper Query* section (i.e., snapshot registry hit selects the appropriate mapper; in-memory mapping preserves full semantics). For `ToTreeList`, tree construction consumes snapshots declared via `Tree()` or `Tree(id, parent, children)` (matching the `[MapTree]` attribute behavior — opt-in only). For `QueryMultiple`, non-generic inference reads `[MapFrom]`; Fluent DTO usage requires explicit generic entry points.

---

Runnable demo for this page (Scenario 502, in-memory SQLite database):

[!code-csharp[Advanced Endpoints: Pagination / Dictionary / Grouping / Chunking / Tree Queries](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced)]

[!code-csharp[Advanced Endpoints: Pagination / Dictionary / Grouping / Chunking / Tree Queries · ToDictionary](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToDictionary)]

[!code-csharp[Advanced Endpoints: Pagination / Dictionary / Grouping / Chunking / Tree Queries · ToLookup](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToLookup)]

[!code-csharp[Advanced Endpoints: Pagination / Dictionary / Grouping / Chunking / Tree Queries · ToChunkDto](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToChunkDto)]

[!code-csharp[Advanced Endpoints: Pagination / Dictionary / Grouping / Chunking / Tree Queries · ToTreeList](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToTreeList)]


## Companion Example

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 502
```

Source code is located at `samples/NCMapper.Samples/05-Orm/DapperScenarios.cs`, covering sub-scenarios including `ToDictionary`, `ToChunkDto`, `QueryMultiple`, and `ToTreeList`.

---


## Further Reading

- [Dapper (I): Query Series](dapper-query.md) · [FreeSql (I)](freesql-single-table.md)
- Technical Documentation: [EF Core and Dapper Extension Packages](../../explanation/orm-efcore-dapper.md) §11.4
- Extension Source Code: `src/NCMapper.Dapper/NCMapperDapperExtensions.cs`
