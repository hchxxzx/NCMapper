> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: how-to/orm/dapper-query
diagnostic codes: (none)
library version: v1.0.0+
last updated: 2026-09-15
---

# Dapper Integration (I): Query Methods

## Overview

NCMapper provides a unified entry point for Dapper: `connection.AsDto<TEntity>()`, followed by chainable terminal methods such as `.Query<TDto>()`, `.QueryFirst<TDto>()`, and `.ToPagedList<TDto>()`. Unlike FreeSql or EF Core integrations, with Dapper the SQL is written manually by the user; NCMapper handles only the mapping from result sets to DTOs.

Core API: `connection.AsDto<TEntity>().Query<TDto>(sql, param)`

---

## 1. Basic Queries

### Usage

[!code-csharp[Dapper basic query (using and first-row patterns)](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Query)]

**Key points**:

- `AsDto<TEntity>()` specifies the source entity type (providing property metadata).
- `Query<TDto>(sql, param)` executes the SQL and maps results to `TDto`.
- Supports all Dapper parameterized query features.
- Requires `using NCMapper.Dapper;`.

---

## 2. Query Terminal Methods

After `AsDto<TEntity>()`, the following terminal methods can be chained:

| Method | Description | Return Type |
|---|---|---|
| `.Query<TDto>(sql)` | Multi-row query | `IEnumerable<TDto>` |
| `.Query<TDto>(sql, param)` | Parameterized multi-row query | `IEnumerable<TDto>` |
| `.QueryFirst<TDto>(sql)` | First row only | `TDto` |
| `.QueryFirstOrDefault<TDto>(sql)` | First row or default value | `TDto?` |
| `.QuerySingle<TDto>(sql)` | Exactly one row (throws exception if zero or multiple rows) | `TDto` |
| `.QuerySingleOrDefault<TDto>(sql)` | Exactly one row or default value | `TDto?` |
| `.QueryAsync<TDto>(sql)` | Asynchronous query | `Task<IEnumerable<TDto>>` |
| `.QueryFirstOrDefaultAsync<TDto>(sql)` | Asynchronous first row or default | `Task<TDto?>` |
| `.QueryUnbufferedAsync<TDto>(sql)` | Asynchronous streaming query (enumerates elements one-by-one; constant memory footprint) | `IAsyncEnumerable<TDto>` |

> **Streaming terminals exist only in async form**: `QueryUnbufferedAsync` has no synchronous counterpart (all other Query terminals come in synchronous/async pairs; this one is exclusively async). For full usage and comparison with `ToChunkDto`, see [Dapper (II): Advanced Methods](dapper-advanced.md), Section 3.1 Streaming Queries (source anchor: `DapperDtoSelector.cs:575`).

### Examples of Each Method

[!code-csharp[Dapper QueryFirst — get first item](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QueryFirst)]
[!code-csharp[Dapper QueryFirstOrDefault — first or default](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QueryFirstOrDefault)]
[!code-csharp[Dapper QuerySingle — exactly one item](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QuerySingle)]
[!code-csharp[Dapper QuerySingleOrDefault — one or default](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QuerySingleOrDefault)]

---

## 3. ToPagedList: SQL-Level Pagination

### Usage

[!code-csharp[Dapper SQL-level pagination via ToPagedList (dialect auto-detection)](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced)]

**Key points**:

- Auto-detects 6 SQL dialects: SQL Server / MySQL / PostgreSQL / SQLite / Oracle / DB2.
- Pagination occurs at the SQL level—not in-memory.
- Parameters: `(sql, param, page, size)` — `page` is 1-based.
- You may explicitly pass an `ISqlDialect` implementation for custom dialect behavior.

### SQL Dialects

| Dialect Class | Database | Pagination Syntax |
|---|---|---|
| `SqlServerDialect` | SQL Server | `OFFSET ... ROWS FETCH NEXT ...` |
| `MySqlDialect` | MySQL | `LIMIT ... OFFSET ...` |
| `PostgreSqlDialect` | PostgreSQL | `LIMIT ... OFFSET ...` |
| `SqliteDialect` | SQLite | `LIMIT ... OFFSET ...` |
| `OracleDialect` | Oracle | `OFFSET ... ROWS FETCH NEXT ...` |
| `Db2Dialect` | DB2 | `OFFSET ... ROWS FETCH NEXT ...` |

### Explicitly Specifying a Dialect

[!code-csharp[Dapper explicit dialect specification (dialect injected via AsDto entry)](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_PagedWithDialect)]

> When no dialect is explicitly provided, NCMapper automatically resolves it via `SqlDialectResolver.Resolve(IDbConnection)` using the connection instance (first unwrapping common wrapped connections for detection; falls back to MySQL with a one-time warning if unrecognized). Custom connection wrapper types may call this same resolver method directly (source anchor: `SqlDialects.cs:298`, class declaration at line `:284`).

---

### 3.1 Custom `ISqlDialect`

The six built-in dialects cover mainstream databases. For non-standard databases—or when you need to intercept or modify pagination SQL—you can implement the `ISqlDialect` interface to customize count and pagination logic. Built-in dialect classes are `sealed`; use **composition**, not inheritance, to wrap them:

```csharp
public class LoggingSqliteDialect : ISqlDialect
{
    private readonly ISqlDialect _inner = new SqliteDialect();

    public string CountSql(string originalSql)
    {
        Console.WriteLine($"[CountSql] {originalSql}");
        return _inner.CountSql(originalSql);
    }

    public string PageSql(string originalSql, string offsetParam, string pageSizeParam)
    {
        Console.WriteLine($"[PageSql] {originalSql}");
        return _inner.PageSql(originalSql, offsetParam, pageSizeParam);
    }
}

// Wrap built-in dialect with logging
var paged = connection.AsDto<UserEntity>(new LoggingSqliteDialect())
    .ToPagedList<UserDto>("SELECT * FROM Users", null, 1, 10);
```

The `ISqlDialect` interface defines two methods:

| Method | Signature | Responsibility |
|---|---|---|
| `CountSql` | `string CountSql(string originalSql)` | Wraps the original SQL in a subquery to compute `COUNT(*)`; strips trailing `ORDER BY` for SQL Server / DB2 (subqueries disallow `ORDER BY`) |
| `PageSql` | `string PageSql(string originalSql, string offsetParam, string pageSizeParam)` | Appends pagination clause to the original SQL; for SQL Server / DB2, inserts `ORDER BY (SELECT NULL)` automatically if no `ORDER BY` is present |

**Known differences**:

- **Oracle `CountSql`**: Oracle requires an alias for the subquery in `SELECT COUNT(*) FROM (...)`. If your custom dialect uses generic wrapping, ensure the alias is added (the built-in `OracleDialect` handles this correctly).
- SQL Server / DB2 `CountSql` automatically strips trailing `ORDER BY`. Custom implementations that directly wrap must account for this.

> **Related API**: `LimitOffsetDialect` — A ready-made base class for LIMIT/OFFSET-style dialects (`public abstract class`). Built-in `MySqlDialect`, `PostgreSqlDialect`, and `SqliteDialect` inherit from it, reusing shared count and pagination SQL templates (source anchor: `SqlDialects.cs:9/:219/:228/:236`).

---

## 4. Asynchronous Queries

### Usage

[!code-csharp[Dapper asynchronous query via QueryAsync](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QueryAsync)]
[!code-csharp[Dapper asynchronous first-or-default via QueryFirstOrDefaultAsync](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QueryFirstOrDefaultAsync)]

---

## Common Misuses

### Misuse 1: Omitting the DTO Type Parameter

```csharp
// ❌ Incorrect: Missing TDto type argument
var result = connection.AsDto<UserEntity>().Query("SELECT * FROM Users");
// → Query is generic and requires explicit TDto

// ✅ Correct
var dtos = connection.AsDto<UserEntity>().Query<UserDto>("SELECT * FROM Users");
```

### Misuse 2: SQL Injection Risk

```csharp
// ❌ Incorrect: String concatenation (SQL injection risk)
var name = "Alice'; DROP TABLE Users;--";
var dtos = connection.AsDto<UserEntity>()
    .Query<UserDto>($"SELECT * FROM Users WHERE Name = '{name}'");

// ✅ Correct: Use parameterized queries
var dtos = connection.AsDto<UserEntity>()
    .Query<UserDto>("SELECT * FROM Users WHERE Name = @Name", new { Name = name });
```

### Misuse 3: Forgetting to Open the Connection

```csharp
// ❌ Incorrect: Querying without opening the connection
using var connection = new SqliteConnection("Data Source=:memory:");
var dtos = connection.AsDto<UserEntity>().Query<UserDto>("SELECT * FROM Users");
// → Exception: "connection is not open"

// ✅ Correct: Call Open() first
using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
var dtos = connection.AsDto<UserEntity>().Query<UserDto>("SELECT * FROM Users");
```

---

## Summary

| Capability | API | Feature |
|---|---|---|
| Basic query | `.AsDto<T>().Query<TDto>(sql, param)` | Parameterized query |
| First row | `.QueryFirst<TDto>(sql)` | Throws exception if no data |
| First row or default | `.QueryFirstOrDefault<TDto>(sql)` | Returns `null` if no data |
| Single row | `.QuerySingle<TDto>(sql)` | Throws exception if zero or multiple rows |
| Single row or default | `.QuerySingleOrDefault<TDto>(sql)` | Throws exception if multiple rows |
| SQL pagination | `.ToPagedList<TDto>(sql, param, page, size)` | Supports 6 dialects |
| Asynchronous | `.QueryAsync<TDto>(sql)` | Async variant |

---

## Fluent DTO Support Status

Dapper extensions do not perform SQL-side projection or column-name downgrade detection. Column-to-property mapping is handled entirely by **Dapper’s own default type mapper (`DefaultTypeMap`)**. NCMapper does not intervene in the SQL column name segment—so `[MapProperty]` column declarations are **not consumed there**. Instead, `[MapProperty]` applies only during the **entity-to-DTO mapping phase**. Consequently, **Fluent DTOs behave identically to attribute-declared DTOs under Dapper**: `Query`-family methods execute through the full mapping pipeline; fluent types trigger mapper generation or registry fallback (via `IsFluentRegisteredDto`) and fully support in-memory mapping semantics. Note: `QueryMultiple`’s non-generic source-type inference reads `[MapFrom]`; for Fluent DTOs, always use the explicit generic entry point.

---

## Companion Sample

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 502
```

Core sample source (single-source-of-truth; runnable via `dotnet run --scenario`):

[!code-csharp[Query methods](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Query)]

Full source file: `samples/NCMapper.Samples/05-Orm/DapperScenarios.cs`, covering `Query`, `QueryFirst`, `QuerySingle`, `ToPagedList`, and related sub-scenarios.

---

## Further Reading

- [Dapper (II): Advanced Methods](dapper-advanced.md) · [EF Core Projection](efcore-projection.md)
- Technical documentation: [EF Core and Dapper Extension Packages](../../explanation/orm-efcore-dapper.md) §11.4
- Extension source code: `src/NCMapper.Dapper/NCMapperDapperExtensions.cs` · `src/NCMapper.Dapper/SqlDialects.cs`
