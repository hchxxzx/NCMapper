> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §11.3–11.4 EF Core and Dapper Extension Packages -->

### 11.3 NCMapper.EFCore Extension Package

#### Installation and Reference

```xml
<PackageReference Include="NCMapper.EFCore" />
```

#### Usage

Runnable versions for each terminal (`using NCMapper;` + `NCMapper.EFCore` extensions; sample conditions based on sample data):

[!code-csharp[List projection ToList](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_SingleTableAsDto)]
[!code-csharp[Single-item projection First](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_SingleTableAsDto_SingleTableFirst)]
[!code-csharp[Multi-table projection — load navigation properties via `Include()` before `AsDto()`](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_NestedCrossTable)]
[!code-csharp[Async terminal overview: `ToListAsync` and `ToPagedListAsync`](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_EFAsyncTerminals)]

In EF Core multi-table scenarios, navigation properties are loaded via `Include()` *before* `AsDto()`. The fallback path automatically handles nested navigation property mapping via `MapList`. Terminal methods mirror those in the FreeSql package and additionally provide `ToLookup` and `ToChunkDtoAsync`:

| Method                                                                                       | Description                                      |
| ---------------------------------------------------------------------------------------- | ------------------------------------------------ |
| `ToList<TTarget>()` / `ToListAsync<TTarget>(ct)`                                         | List / async list mapping                        |
| `First<TTarget>()` / `FirstAsync<TTarget>(ct)`                                           | Single-item / async single-item mapping          |
| `Single<TTarget>()` / `SingleAsync<TTarget>(ct)`                                         | Exactly-one / async exactly-one mapping (throws `InvalidOperationException` if zero or multiple items) |
| `ToPagedList<TTarget>(page, pageSize)` / `ToPagedListAsync<TTarget>(page, pageSize, ct)` | Paged / async paged mapping                      |
| `ToChunkDto<TTarget>(size, done)` / `ToChunkDtoAsync<TTarget>(size, done, ct)`         | Chunked / async chunked mapping (callback receives already-mapped DTOs) |
| `ToDictionary<TTarget>(keySelector)` / `ToDictionaryAsync<TTarget>(keySelector, ct)`     | Dictionary / async dictionary projection         |
| `ToLookup<TTarget>(keySelector)`                                                         | Lookup (`ILookup`) projection                    |

> **`Single` / `SingleAsync` Exception Messages**: Throws `InvalidOperationException` when zero or multiple records are found. Error messages are sourced from this library’s error resources, **include the target DTO type name**, and switch between Chinese and English based on the current UI culture; behavior is **identical to the FreeSql package** (message text matches exactly across both packages).

**Code Example:**

```csharp
// ToChunkDtoAsync — streaming large result sets in batches
await dbContext.Orders
    .Where(o => o.IsActive)
    .AsDto()
    .ToChunkDtoAsync<OrderDto>(size: 500, async chunk =>
    {
        foreach (var dto in chunk)
            await ProcessOrderAsync(dto);
    });
```

Runnable versions of the first three methods:

[!code-csharp[SingleAsync — exactly one (async)](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_SingleTableAsDto_SingleTableSingle)]
[!code-csharp[ToLookup — build ILookup by key](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_ToLookup)]
[!code-csharp[ToDictionaryAsync — build dictionary by key (async)](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_ToDictionaryAsync)]

#### Default `AsNoTracking` Behavior

The EFCore extension package **automatically appends `AsNoTracking()` to all queries by default**, ensuring that neither projection nor fallback paths produce change-tracked entities. This reduces `DbContext` memory overhead and aligns with typical "read-only query" usage patterns.

Runnable version comparing default non-tracking behavior versus explicit `AsTracking` (see integration test `AsTracking_Explicit_Keeps_Tracking` for `ChangeTracker` state differences):

[!code-csharp[Default non-tracking query vs explicit AsTracking](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_AsTrackingExplicit)]

**Globally disable `AsNoTracking`:**

```csharp
NCMapperOptions.EFCoreProjectionAsNoTrackingDefault = false;
```

#### Incompatible Scenarios and Alternatives

| Scenario                     | Reason                                                                 | Alternative                                                                 |
| ---------------------------- | ---------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| `GroupBy` followed by `AsDto` | **Compilation error** (`[Obsolete(error:true)]`) — returns `IQueryable<IGrouping<>>`, not a single-entity type | **Recommended**: Native EF Core `Select` aggregations (SQL-side `GROUP BY`); **Not recommended**: `.AsDto().ToList()` then in-memory `GroupBy` (full materialization, high performance cost) |
| `AsEnumerable()` followed by `AsDto` | **Compilation error** (`[Obsolete(error:true)]`) — already outside `IQueryable` domain | Use `Mapper.MapList<TSource, TTarget>(items)`                             |
| `ToList()` followed by `AsDto` | Same as above — result is `List<T>`, not `IQueryable<T>`                | Data is already in memory; use `Mapper.MapList` directly                   |

### 11.4 NCMapper.Dapper Extension Package

#### Installation and Reference

```xml
<PackageReference Include="NCMapper.Dapper" />
```

#### Usage

The Dapper package exposes `AsDto<TSource>()` as its **sole entry point**, returning a `DapperDtoSelector<TSource>`. All query and terminal methods are invoked through this selector. Internally, it uses Dapper’s native `Query<TSource>` to fetch source entities, then applies the **full NCMapper pipeline** via `Mapper.MapList`: `[MapProperty]` column-name mapping → `[Convert]` value conversion → `[Mask]` data masking → Pipeline `PostMap`. All features remain active at all times — there is no distinction between “projection” and “fallback” paths; mapping always occurs in-memory using the complete pipeline.

```csharp
using NCMapper;
using NCMapper.Dapper;

// DTO annotations
[MapFrom(typeof(OrderEntity))]
public class OrderDetailDto
{
    public int Id { get; set; }

    [MapProperty(typeof(OrderEntity), "OrderNo")]
    public string OrderNumber { get; set; }

    [MapProperty(typeof(OrderEntity), "CustomerName")]
    public string Customer { get; set; }

    [Mask]
    public string Phone { get; set; }
}

// All terminal methods are chained from the AsDto<TSource>() entry point
using var connection = new SqlConnection(connectionString);
```

Runnable versions of three common scenarios (consistent fluent chain structure; types and conditions based on sample data):

[!code-csharp[Query — list projection](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Query)]
[!code-csharp[ToPagedList — SQL-level pagination (dialect auto-detected)](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced)]
[!code-csharp[AsDto(dialect) — specify dialect at entry point](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_PagedWithDialect)]

> **Pipeline Behavior Declaration**: All terminal methods always invoke `Mapper.MapList` and thus execute the full NCMapper pipeline. `Mask`, `Convert`, and Pipeline `PostMap` apply uniformly across all paths — no registration or path selection is required or supported.

#### Available Terminal Methods

The following public methods are exposed by `DapperDtoSelector<TSource>`. Except for `Query` / `QueryAsync`, all terminal methods internally execute the source query first, then map results via `Mapper.MapList` — i.e., they always follow the full NCMapper pipeline.

| Method Family | Synchronous | Asynchronous | Description |
| -------------- | ----------- | ------------ | ----------- |
| **Query** | `Query<TDto>(sql, param?)` | `QueryAsync<TDto>(sql, param?, ct?)` | Query entity list → map to `List<TDto>` via `MapList` |
| **Query (CommandDefinition)** | `Query<TDto>(CommandDefinition)` | `QueryAsync<TDto>(CommandDefinition)` | Pass full parameters (e.g., `CancellationToken`) via `CommandDefinition` |
| **QueryFirst** | `QueryFirst<TDto>(sql, param?)` | `QueryFirstAsync<TDto>(sql, param?, ct?)` | Return first record mapped to `TDto` (throws exception if no records) |
| **QueryFirstOrDefault** | `QueryFirstOrDefault<TDto>(sql, param?)` | `QueryFirstOrDefaultAsync<TDto>(sql, param?, ct?)` | Return first record mapped to `TDto` (returns `default` if no records) |
| **QuerySingle** | `QuerySingle<TDto>(sql, param?)` | `QuerySingleAsync<TDto>(sql, param?, ct?)` | Return **exactly one** record mapped to `TDto` (throws exception if zero or multiple records) |
| **QuerySingleOrDefault** | `QuerySingleOrDefault<TDto>(sql, param?)` | `QuerySingleOrDefaultAsync<TDto>(sql, param?, ct?)` | Return exactly one record mapped to `TDto` (returns `default` if zero records; throws exception if multiple) |
| **ToPagedList** | `ToPagedList<TDto>(sql, param?, page, pageSize)` | `ToPagedListAsync<TDto>(sql, param?, page, pageSize, ct?)` | SQL-level pagination (first `COUNT`, then `OFFSET`/`FETCH`), returns `PagedList<TDto>` |
| **ToDictionary** | `ToDictionary<TKey,TDto>(sql, param?, keySelector)` | `ToDictionaryAsync<TKey,TDto>(sql, param?, keySelector, ct?)` | Build `Dictionary<TKey, TDto>` grouped by `keySelector` |
| **ToLookup** | `ToLookup<TKey,TDto>(sql, param?, keySelector)` | `ToLookupAsync<TKey,TDto>(sql, param?, keySelector, ct?)` | Build `ILookup<TKey, TDto>` grouped by `keySelector` |
| **ToChunkDto** | `ToChunkDto<TDto>(sql, param?, size, done)` | `ToChunkDtoAsync<TDto>(sql, param?, size, done, ct?)` | Chunked query + callback; each batch is mapped to `TDto`, suitable for batch processing large result sets |
| **QueryUnbufferedAsync** | — | `QueryUnbufferedAsync<TDto>(sql, param?, ct?)` | Async streaming enumeration (`IAsyncEnumerable<TDto>`), ideal for row-by-row processing of extremely large result sets |
| **ToTreeList** | `ToTreeList<TDto>(sql, param?)` | `ToTreeListAsync<TDto>(sql, param?, ct?)` | Hierarchical tree mapping; `Id`/`ParentId` field names customizable via `[MapTree]` |
| **QueryMultiple (2 result sets)** | `QueryMultiple<TDto1,TDto2>(sql, param?)` | — | Multi-result-set query; returns `ValueTuple<List<TDto1>, List<TDto2>>` |
| **QueryMultiple (3 result sets)** | `QueryMultiple<TDto1,TDto2,TDto3>(sql, param?)` | — | Three-result-set query; returns `ValueTuple<List<TDto1>, List<TDto2>, List<TDto3>>` |

> **Semantic Difference: `QueryFirst` vs `QuerySingle`**: `QueryFirst` returns the first record (throws exception if none exists); `QuerySingle` requires *exactly one* record (throws exception if zero *or* multiple exist). The latter is appropriate for primary-key lookups, unique-constraint queries, and other deterministic scenarios.

#### QuerySingle Series

Use these methods when querying by primary key or unique constraint and expecting exactly one record:

Ensures exactly one result (throws an exception for zero or multiple results):

[!code-csharp[QuerySingle runnable version (exactly one result by primary key)](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QuerySingle)]

Allows zero results; throws an exception for multiple results:

[!code-csharp[QuerySingleOrDefault runnable version (exactly one result or null)](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QuerySingleOrDefault)]

#### QueryUnbufferedAsync — Streaming Enumeration

Suitable for processing large result sets (e.g., millions of rows) row-by-row, avoiding full in-memory materialization:

[!code-csharp[QueryUnbufferedAsync async streaming enumeration runnable version (semantically identical to QueryUnbuffered; only the SQL parameter differs)](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_QueryUnbufferedAsync)]

#### QueryMultiple — Multiple Result Sets

Executes a single SQL statement returning multiple result sets, each mapped independently to its corresponding DTO list:

Two result sets (two-type generic overload):

[!code-csharp[QueryMultiple two-result-set runnable version](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_QueryMultiple)]

Three result sets (three-type generic overload):

[!code-csharp[QueryMultiple three-result-set runnable version](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_QueryMultiple3)]

#### ToTreeList — Hierarchical Tree Construction

Converts a flat list into a hierarchical tree structure based on `Id`/`ParentId` relationships. By default, DTO properties are expected to be named `Id` and `ParentId`. Non-default property names can be specified using the `[MapTree]` attribute (see the `[MapTree]` subsection under [§5 Attribute System](../reference/features.md)):

```csharp
// Tree-shaped DTO with custom property names
[MapTree("NodeId", "ParentNodeId")]
public class CategoryDto
{
    public int NodeId { get; set; }
    public int ParentNodeId { get; set; }
    public string Name { get; set; }
    public List<CategoryDto> Children { get; set; }
}
```

[!code-csharp[ToTreeList hierarchical projection runnable version (consumes the CategoryDto type above)](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToTreeList)]

> **Applicability Boundary**: The `[MapTree]` attribute is read by Dapper’s `ToTreeList` and EF Core’s `ToTreeList`/`ToTreeListAsync`. FreeSql’s `ToTreeList` performs native tree construction and does *not* read `[MapTree]`. EF Core has no native hierarchical query support; it uses “flattened query + in-memory tree building” (the core library `TreeBuilder` assembles trees based on `Id`/`ParentId`). It reads `[MapTree]`, matching Dapper’s semantics. For details, see [how-to §6 Hierarchical Queries](../how-to/orm/efcore-projection.md).

#### SQL Dialect System (`ISqlDialect`)

The `dialect` parameter of `AsDto<TSource>(connection, dialect?)` allows explicit injection of a SQL dialect instance. When `null`, `SqlDialectResolver.Resolve(connection)` automatically detects the dialect based on the `IDbConnection` type name. The dialect object affects only two SQL rewriting behaviors:

- **`CountSql(originalSql)`** — Rewrites the original query SQL into a `COUNT` statement (used to retrieve total count before pagination). Most dialects strip the outermost `ORDER BY`; Oracle wraps the query in a subquery to preserve inner `ORDER BY`.
- **`PageSql(originalSql, offsetParam, pageSizeParam)`** — Appends pagination clauses (e.g., `LIMIT` / `OFFSET FETCH` / `ROWNUM`) to the original SQL, automatically handling compatibility details such as `ORDER BY (SELECT NULL)` across different database syntaxes.

**Built-in Dialects:**

| Dialect Class | Target Database | Namespace |
|---------------|-----------------|-----------|
| `SqlServerDialect` | SQL Server (2012+) | `NCMapper.Dapper` |
| `MySqlDialect` | MySQL / MariaDB | `NCMapper.Dapper` |
| `PostgreSqlDialect` | PostgreSQL | `NCMapper.Dapper` |
| `SqliteDialect` | SQLite | `NCMapper.Dapper` |
| `OracleDialect` | Oracle | `NCMapper.Dapper` |
| `Db2Dialect` | IBM Db2 | `NCMapper.Dapper` |

**Explicit Dialect Specification:**

[!code-csharp[Dapper explicit dialect injection (passing `ISqlDialect` to `AsDto`) for pagination](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_PagedWithDialect)]

**Custom Dialects:**

Implement the two methods `CountSql` and `PageSql` from the `ISqlDialect` interface. Common use cases include wrapping built-in dialects to add logging, or adapting to non-standard SQL databases:

```csharp
public class LoggingSqliteDialect : ISqlDialect
{
    private readonly SqliteDialect _inner = new();

    public string CountSql(string sql)
    {
        var countSql = _inner.CountSql(sql);
        Console.WriteLine($"[Count] {countSql}");
        return countSql;
    }

    public string PageSql(string sql, string offsetParam, string pageSizeParam)
    {
        var pageSql = _inner.PageSql(sql, offsetParam, pageSizeParam);
        Console.WriteLine($"[Page] {pageSql}");
        return pageSql;
    }
}

// Using the custom dialect
var page = connection.AsDto<OrderEntity>(new LoggingSqliteDialect())
    .ToPagedList<OrderDto>(sql, param, 1, 20);
```

> **Known Differences**: Oracle’s `CountSql` preserves inner `ORDER BY` via subquery wrapping (unlike SQL Server/DB2, which strip the top-level `ORDER BY`). The automatic `ORDER BY (SELECT NULL)` appending strategy in `PageSql` varies across databases.
