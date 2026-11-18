> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: tutorials/orm-projection-tutorial
Diagnostic Code: NCMAP017  
Library Version: v1.0.0+  
Last Updated: 2026-08-19  
---

# ORM Projection Scenario Tutorial

## Overview

This tutorial addresses a typical performance issue on list pages: an entity has dozens of fields, but the page displays only a few — loading the full entity and then mapping it field-by-field wastes bandwidth and memory. You’ll use a single SQL query to both retrieve data and map it directly to a DTO, while also handling pagination and graceful degradation.

The example is drawn from scenario 04 in `samples/NCMapper.Samples`, using FreeSql’s in-memory mode — fully runnable offline. For the complete API reference for projection capabilities, see [FreeSql Single-Table Queries](../how-to/orm/freesql-single-table.md) and [EF Core Projection](../how-to/orm/efcore-projection.md). This tutorial focuses on the core business decision flow.

---

## 1. Scenario: User List Page

The user list page must display Name, Age, and Email; sort by Age; and paginate results. The `UserEntity` also contains Address, Status, RegistrationTime, and other fields — none of which are used on this page.

Two approaches exist:

- **Load full entities via `ToList()` first, then map in-memory**: SELECTs all columns — including those unused by the DTO — into memory.
- **Projection via `AsDto()`**: The SQL queries *only* the columns required by the DTO, completing both retrieval and mapping in one round-trip.

`AsDto()` implements the second approach: it projects directly to a DTO within the ORM query chain — column pruning and mapping both occur on the database side.

| Dimension | Full-Entity Load + In-Memory Mapping | Projection via `AsDto()` |
|-----------|--------------------------------------|--------------------------|
| SQL Columns Selected | All entity columns | Only columns declared in the DTO |
| Query Count | 1 (but large data payload) | 1 |
| Memory Usage | Both full entity and DTO copies | DTO only |
| Filtering/Sorting | Database-side or in-memory | Database-side only |
| Best For | Post-processing requiring full entity fields | Lists, exports, API responses |

---

## 2. Install the Extension Package

Projection is an ORM-integration capability — you must install the corresponding extension package (choose one):

| Extension Package | Supported ORM |
|-------------------|---------------|
| `NCMapper.FreeSql` | FreeSql |
| `NCMapper.EFCore` | EF Core |
| `NCMapper.Dapper` | Dapper |

The core package does *not* include ORM dependencies. After installing an extension, the `fsql.Select<T>()` query chain gains access to the `AsDto()` extension method.

Installation Notes:

- Extension package version must match the core package version (identical version number).
- Multiple extension packages (e.g., `NCMapper.FreeSql` + `NCMapper.EFCore`) may coexist in the same process without interference.
- Projection is a *terminal operation*: no further entity-level query conditions (e.g., `Where`, `OrderBy`) may be appended *after* `AsDto()`. All such conditions must appear *before* `AsDto()`.

---

## 3. Single-Table Projection: Query = Mapping

Append `AsDto()` to your FreeSql query chain:

[!code-csharp[Single-Table AsDto Projection](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto)]

[!code-csharp[Single-Table AsDto Projection · SingleTableFirst](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto_SingleTableFirst)]

This generates a single SQL statement:  
`SELECT Id, Name, Age, Email FROM User WHERE Age > 20 ORDER BY Age`,  
and maps the result directly to a `List<UserDto>`. Filtering (`Where`) and sorting (`OrderBy`) execute entirely on the database — the page receives final, ready-to-use data.

The key difference from in-memory mapping lies in the *data surface*: projection retrieves *only* columns declared in the DTO — no extra entity fields are loaded. The result type is `List<UserDto>` — no secondary conversion step is needed.

DTO Requirements for Projection:

- DTO property names must match entity column names (case-insensitive), or be explicitly mapped using `[MapProperty]`. Matching rules align with those used by `Mapper.Map`.
- Declare *only* the fields needed by the UI — the leaner the DTO, the more aggressive the SQL column pruning.
- Nested object properties are projected automatically along navigation paths (with implicit `Include`; see [§4 Automatic Include](../how-to/orm/freesql-single-table.md#4-automatic-include)).

---

## 4. Fetching a Single Record

Detail or edit pages often require just one record:

[!code-csharp[Single-Record Projection](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto)]

[!code-csharp[Single-Record Projection · SingleTableFirst](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto_SingleTableFirst)]

`First()` returns the first matching record — semantics identical to `Select<User>().First()`.

---

## 5. Pagination: Count + Paginated Query in One Step

List pages require pagination. `ToPagedList` performs counting and paginated querying atomically:

[!code-csharp[SQL-Level Pagination](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTablePaged)]

Returns a `PagedList<UserDto>` containing `Items`, `PageIndex`, `PageSize`, and `TotalCount`. Pagination executes natively in SQL (`LIMIT/OFFSET`) — no full dataset is loaded into memory before slicing.

Pagination Parameters:

- `page` (1-based page index) and `size` (items per page) are supplied by the UI layer.
- If `size < 1`, an `ArgumentOutOfRangeException` is thrown — validate inputs before calling.
- `TotalCount` reflects the count *after* filtering — used to render pagination controls.

---

## 6. Graceful Degradation: Handling Untranslatable SQL Features

Not all mapping features can be translated into SQL. Two common examples:

| Feature | SQL-Side Behavior |
|---------|-------------------|
| `[Convert]` custom converters | Cannot be expressed as SQL expressions |
| `[Mask]` data masking | Cannot be expressed as SQL expressions |

When such features are encountered, `AsDto()` automatically degrades to a two-step process:  
1. Execute the SQL query for all *translatable* parts of the DTO projection.  
2. Perform remaining mappings (e.g., `[Convert]`, `[Mask]`) in-memory.

Degradation is transparent:

- Behavior remains unchanged: callers receive identical results as with pure in-memory mapping.
- No code changes required: projection and in-memory mapping share the same mapping configuration.
- Cost: one additional round-trip; impact is observable for large lists.

**Degradation is observable**: When `Mapper.TraceEnabled` is set to `true`, degraded execution paths emit trace lines prefixed with the terminal method name (e.g., `[FreeSql AsDto] ToList degraded: ...`). Use these traces to locate degradation points. For full trace configuration and usage, see [Validation & Diagnostics](../how-to/infrastructure/validation.md).

To control degradation, audit your mapping configuration for untranslatable features ([Projection Path vs. Degradation Path](../how-to/orm/freesql-single-table.md#3-projection-path-vs-fallback-path)).

Degradation Triggers (by feature):

| Feature | Translatable to SQL? | Degradation Behavior |
|---------|----------------------|----------------------|
| Same-name properties / `[MapProperty]` simple fields | ✅ Yes | No degradation |
| `[Convert]` custom converter | ❌ No | Degrades to two-step mapping |
| `[Mask]` data masking | ❌ No | Degrades to two-step mapping |
| Nested navigation properties | ✅ Yes (auto `Include`) | No degradation |
| Collection indexer paths (e.g., `Orders[0].Name`) | ⚠️ Partially | ORM-dependent degradation |

---

## Terminal Methods Overview

`AsDto()` must be followed by a *terminal method* to execute the query. Choose based on result shape:

| Terminal Method | Returns | Use Case |
|-----------------|---------|----------|
| `ToList<TDto>()` | `List<TDto>` | Standard list rendering |
| `First<TDto>()` | `TDto` | Retrieve first match (throws on empty) |
| `Single<TDto>()` | `TDto` | Retrieve exactly one match (throws on empty or multiple) |
| `ToPagedList<TDto>(page, size)` | `PagedList<TDto>` | Paginated list |
| `ToChunkDto<TDto>(size, action)` | Batch callback | Streaming bulk processing (e.g., export) |

`First`/`Single` exception semantics match native ORM behavior (`InvalidOperationException` on empty/multiple results). `ToChunkDto` avoids holding entire datasets in memory — ideal for exports or batch jobs. Full signatures and async variants are documented in [§2 Terminal Methods](../how-to/orm/freesql-single-table.md#2-terminal-methods).

## Run & Verify

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 501
```

Scenario 501 outputs results for sub-scenarios including single-table projection, single-record fetch, pagination, chunked processing, and `Compose`. Samples use FreeSql’s in-memory mode with preloaded test data — no external database required.

---

## When *Not* to Use Projection

Projection isn’t universally optimal. Consider reverting to in-memory mapping in these cases:

| Scenario | Reason |
|----------|--------|
| Complex business logic requiring *all* entity fields | Projection loads only DTO-declared columns — entity data is incomplete |
| Data originates from multiple tables, and single-table projection cannot cover all required fields | Use `Compose` to synthesize multi-source data ([Multi-Source Composition Tutorial](./compose-tutorial.md)) |

Projection and composition *can be combined*: project primary table fields via `AsDto()`, then enrich with auxiliary data via `Compose` in batches ([compose-tutorial: Combined Usage](./compose-tutorial.md)).

## Common Pitfalls

| Pitfall | Symptom | Fix |
|---------|---------|-----|
| Calling `ToList()` *before* `AsDto()` | Projection is bypassed — equivalent to in-memory mapping | Call `ToList<TDto>()` *only* at the end of the query chain — never interrupt the chain mid-flow |
| Including untranslatable features in projection | Silent degradation → performance drop on large lists | Check Trace output to locate degradation points; decide whether to remove the feature or accept the cost |
| Assigning `ToPagedList` result to `List<TDto>` | `TotalCount` is lost | Always assign to `PagedList<TDto>` |

---

## Quick Reference Table

| Scenario | API | Description |
|----------|-----|-------------|
| Install Extension | `NCMapper.FreeSql` / `EFCore` / `Dapper` | Pick one — core package has zero ORM dependencies |
| Single-Table Projection | `fsql.Select<T>().AsDto().ToList<TDto>()` | Column pruning + mapping at SQL level |
| Single-Record Projection | `.AsDto().First<TDto>()` | Fetch first matching record |
| SQL-Level Pagination | `.AsDto().ToPagedList<TDto>(page, size)` | Atomic count + pagination |
| Automatic Degradation | Untranslatable features trigger two-step mapping | Transparent — behavior preserved |

That concludes the projection workflow covered in this tutorial: from installing extensions, through single-table projection, single-record fetch, SQL pagination, to understanding degradation — covering the full data pipeline for list pages.

---

## Related Documentation

- [FreeSql Single-Table Queries](../how-to/orm/freesql-single-table.md): Deep dive into projection and degradation mechanics  
- [EF Core Projection](../how-to/orm/efcore-projection.md): EF Core-specific projection guidance  
- [Multi-Source Composition Tutorial](./compose-tutorial.md): Composing DTOs from multiple tables  
- [15-Minute Getting Started Tutorial](../getting-started/tutorial-15min.md): End-to-end basic mapping walkthrough  
- [Mapping Configuration Ownership: Three Principles](../explanation/semantics/mapping-config-ownership.md): Path semantics and API evolution notes
