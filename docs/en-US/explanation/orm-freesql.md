> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §11.2 NCMapper.FreeSql Extension Package -->

### 11.2 NCMapper.FreeSql Extension Package

#### Installation and Reference

```xml
<PackageReference Include="NCMapper.FreeSql" />
```

#### Single-Table Queries

The following runnable examples assume `using NCMapper;` and the `NCMapper.FreeSql` extension are in scope, with conditions set according to sample data:

[!code-csharp[List Projection: ToList](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto)]
[!code-csharp[Single Item Projection: First](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto_SingleTableFirst)]
[!code-csharp[Async Terminal Methods: ToListAsync and FirstAsync](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_FreeSqlAsyncTerminals)]

#### Multi-Table Compose Queries

FreeSql’s multi-table interfaces — `ISelect<T1, T2>` and `ISelect<T1, T2, T3>` — also support `AsDto()`:

```csharp
// Two tables — cross-table fields declared via [MapProperty(typeof(CustomerEntity), ...)]
var dtos = fsql.Select<OrderEntity, CustomerEntity>()
    .AsDto()
    .ToList<OrderDetailDto>();

// Three tables
var dtos = fsql.Select<OrderEntity, CustomerEntity, ProductEntity>()
    .AsDto()
    .ToList<OrderFullDto>();
```

By default, mapping proceeds via the **projection expression path**, where FreeSql translates the multi-table field mapping directly into SQL (e.g., `ISelect<T1,T2>.ToList(expr)`). A fallback occurs only when navigation properties cannot be translated to SQL: the query first retrieves `Tuple<T1, T2>` entities, then maps them in-memory using `Mapper.Compose<TTarget>(item1, item2)`.

#### Paginated Queries

[!code-csharp[ToPagedListAsync — Asynchronous Pagination (page, TotalPages, and TotalCount states are available on the returned PagedList)](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_FreeSqlPagedAsync)]

Pagination is performed entirely on the SQL side (both paths below apply): first, `Count()` retrieves the total count; then, `Page(page, pageSize)` executes the paginated query.

#### Available Terminal Methods

| Method                                              | Description                                                                 |
| --------------------------------------------------- | --------------------------------------------------------------------------- |
| `ToList<TTarget>()`                                 | Projects query results into a list of DTOs.                               |
| `ToListAsync<TTarget>(ct)`                          | Asynchronously projects query results into a list of DTOs.                  |
| `First<TTarget>()`                                  | Projects a single query result into a DTO.                                  |
| `FirstAsync<TTarget>(ct)`                           | Asynchronously projects a single query result into a DTO.                   |
| `ToPagedList<TTarget>(page, pageSize)`              | Performs paginated query and returns a `PagedList<TTarget>`.              |
| `ToPagedListAsync<TTarget>(page, pageSize, ct)`     | Asynchronously performs paginated query and returns a `PagedList<TTarget>`. |
| **`ToChunkDto<TTarget>(size, done)`**              | Chunked query + callback: each chunk is projected into DTOs (replaces `ToChunk`). |
| `ToTreeList<TTarget>()`                             | Tree-shaped DTO projection (self-referencing tree DTOs must declare a `List<TTarget>` child collection navigation property). |
| `ToTreeListAsync<TTarget>(ct)`                      | Asynchronously projects tree-shaped DTOs.                                   |
| `ToDictionary<TTarget>(keySelector)`                | Dictionary projection (`keySelector` is a `Func<>` executed in-memory; no SQL translation required). |
| `ToDictionaryAsync<TTarget>(keySelector, ct)`       | Asynchronously projects dictionary DTOs.                                    |

**Chunked Query with `ToChunkDto`**:

[!code-csharp[FreeSql ToChunkDto — Callback-Based Chunked Processing](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_ToChunkDto)]

#### Supported Query Patterns and Boundaries

`AsDto()` fundamentally performs **entity-to-DTO property-name mapping**: NCMapper generates an `Expression<Func<TSource, TTarget>>` projection expression based on `[MapFrom]` / `[MapProperty]` declarations, which FreeSql then translates into a SQL `SELECT`. Therefore, any FreeSql query chain that ultimately returns a full entity via `ISelect<TSource>` can freely compose before `.AsDto()`.

**Compatible Query Patterns** (arbitrary chaining before `AsDto()`):

The first four patterns (with runnable examples):

[!code-csharp[WhereIf — Dynamic Filtering](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_WhereIfFilter)]
[!code-csharp[WithSql — Subquery as Data Source](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_WithSqlAsDto)]
[!code-csharp[Subquery Filtering (Exists / In / Any Predicates)](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_AnySubquery)]
[!code-csharp[Include / IncludeMany (Collection navigations handled automatically in fallback path)](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_IncludeManyNavigation)]

Other native FreeSql chain forms (NCMapper consumes `AsDto()` only *after* these):

```csharp
// Manual pagination: Count(out total) + Page
fsql.Select<UserEntity>()
    .Where(u => u.IsActive)
    .Count(out var total)
    .Page(pageIndex, pageSize)
    .AsDto().ToList<UserDto>();

// Ordering
fsql.Select<UserEntity>()
    .OrderByDescending(u => u.CreatedAt)
    .AsDto().ToList<UserDto>();

// WhereCascade — Broadcast condition across joins
fsql.Select<OrderEntity>()
    .LeftJoin(o => o.Customer.Id == o.CustomerId)
    .WhereCascade(x => x.IsDeleted == false)
    .AsDto().ToList<OrderDto>();

// Navigation-property-based joins (automatically translated to LEFT JOIN)
fsql.Select<OrderEntity>()
    .LeftJoin(o => o.Customer.Id == o.CustomerId)
    .AsDto().ToList<OrderDto>();
```

**Incompatible Scenarios and Alternatives**:

| Scenario                     | Reason                                                                                                                                 | Alternative                                                                 |
| ---------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| `GroupBy` Aggregation         | **Compilation error** (`[Obsolete(error:true)]`) — `GroupBy` returns `ISelectGrouping`, whose result is not an entity but aggregated values. | **Recommended**: Use FreeSql’s native aggregation projection (SQL-side `GROUP BY`). <br> **Not recommended**: Call `.AsDto().ToList()` first, then group in-memory (full materialization → severe performance penalty). |
| `ToChunk` Streaming           | `ToChunk` returns `void`; no return value for chaining `AsDto()`.                                                                      | Use `.AsDto().ToChunkDto(size, done)`.                                      |
| `ToTreeList` Tree Projection  | **Supported**: `AsDto().ToTreeList<TTarget>()` / `ToTreeListAsync<TTarget>()` directly produce tree-shaped DTO projections (self-referencing DTOs require `List<TTarget>` child collection navigation property). | See [freesql-advanced tree projection](../how-to/orm/freesql-advanced.md). |
| FreeSql-native `ToList<Dto>()` | FreeSql’s built-in DTO mapping (name-based matching only; no NCMapper attribute support).                                               | Use `AsDto()` when attributes or advanced mapping logic are needed; simple name-matching works with either. |

**`GroupBy` Example (Compilation Error + Alternatives)**:

```csharp
// ✗ Compilation error — [Obsolete(error:true)] blocks compilation
// fsql.Select<OrderEntity>().GroupBy(a => a.Status).AsDto().ToList<OrderStatsDto>();
// Error message: "AsDto() is not allowed after FreeSql GroupBy. Alternatives: ① Call .AsDto().ToList() then group in-memory; ② Use FreeSql's native SELECT aggregation syntax."
// Note: Items ① and ② in the error message are listed in parallel — they do not imply priority. Prefer ② (native aggregation) in practice.

// ✓ Recommended — FreeSql native aggregation projection (GROUP BY executed in SQL)
var stats = fsql.Select<OrderEntity>()
    .GroupBy(a => a.Status)
    .ToList(a => new OrderStatsDto
    {
        Status = a.Key,
        Count  = a.Count(),
        Total  = a.Sum(b => b.Amount)
    });

// ⚠ Not recommended — In-memory grouping after full materialization (only use when SQL aggregation is impossible)
// Requires loading full (or filtered) detail records into memory, losing SQL-side GROUP BY pushdown and index acceleration.
var dtos = fsql.Select<OrderEntity>().AsDto().ToList<OrderDto>();
var grouped = dtos.GroupBy(d => d.Status);
```

> **Design Principle**: `AsDto()` solves the “entity → DTO automatic mapping” problem (property matching, type conversion, nested navigation, data masking, custom converters). Aggregation projection solves the “expression → column” problem, requiring explicit specification of aggregate functions and target columns. NCMapper provides no automation here. These responsibilities are distinct and non-interchangeable.

#### Tree-Shaped Projection (`ToTreeList`)

`ToTreeList<TTarget>()` / `ToTreeListAsync<TTarget>()` directly project hierarchical DTOs — no need to fetch an entity tree first and recursively call `Mapper.Map`:

```csharp
// Self-referencing tree DTO: must declare a child collection navigation property whose element type is itself
[MapFrom(typeof(MenuEntity))]
public class MenuDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<MenuDto> Children { get; set; }   // Self-referencing child collection (required for trees)
}
```

Runnable tree-projection example (same fluent chain pattern; demo types correspond to Wiki hierarchy):

[!code-csharp[ToTreeList — Tree Projection (automatic LEFT JOIN for parent-child relationships + MapList for tree assembly)](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_ToTreeList)]

Constraints and Compatibility:

- The DTO **must declare a self-referencing collection property** (element type = `TTarget`); otherwise, entry validation `ValidateTreeDtoTarget` throws an error.
- Compatible with `Include` / `AsTreeCte`; `ApplyFreeSqlIncludes` can auto-populate navigations.
- Cross-table `[MapProperty]` declarations remain usable in tree nodes after being populated via `Include`.

#### Dictionary Projection (`ToDictionary`)

`ToDictionary<TTarget>(keySelector)` / `ToDictionaryAsync<TTarget>(keySelector, ct)` assemble projection results into a dictionary. `keySelector` is a `Func<>` executed in-memory (no SQL translation required):

[!code-csharp[ToDictionary — Runnable Dictionary Projection (keySelector operates on source entity)](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_ToDictionary)]

#### AsDto with Mapping Transfer

When a DTO is annotated with `[MapFrom]` (carrying complete mapping configuration), the terminal methods of `AsDto` follow the **"native terminal method + Map / MapList" transfer path**: they directly invoke the ORM's native terminal methods (e.g., FreeSql’s `ToList`, EF Core’s `ToListAsync`) to retrieve entities, then perform in-memory mapping to DTOs using NCMapper. **This path bypasses the `NeedsDegradation` check**, making it a deterministic high-performance path (degradation occurs only when the projection expression cannot be translated—whereas mapping-based transfer is fixed and guaranteed). All terminal methods—including `ToList`, `First`, `ToPagedList`, and `ToChunkDto`—support this mode.
