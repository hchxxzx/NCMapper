> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: how-to/orm/freesql-multi-table
Diagnostic codes involved: (none)
Library version: v1.0.0+
Last updated: 2026-09-15
---

# FreeSql Projection (II): Multi-Table Queries

## Overview

NCMapper supports `AsDto` projection for FreeSql multi-table queries via `ISelect<T1, T2>` or `ISelect<T1, T2, T3>`. Fields in the DTO are annotated with `[MapProperty(typeof(Tx), ...)]` to declare their source table(s). Multi-table projection performs JOINs and column pruning on the SQL side, avoiding full-entity loading.

Core API: `fsql.Select<T1, T2>().LeftJoin(...).AsDto().ToList<TDto>()`

> **Related API**: `NCMapperFreeSqlExtensions.ThrowOnIncludeApplicationFailure` — A global toggle (`public static bool` field, default `false`) controlling whether exceptions from partial automatic `Include` failures are re-thrown to callers. When `false`, degradation is preserved and a trace log is emitted; when `true`, failures become explicitly visible to callers. (Source anchor: NCMapperFreeSqlExtensions.Support.cs:368)

---

## 1. Two-Table Query

### Usage

[!code-csharp[FreeSql Multi-Table: using NCMapper.FreeSql and Two-Table Join Projection](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_TwoTableJoin)]

DTO definition (fields sourced from two tables):

```csharp
[MapFrom(typeof(OrderEntity))]  // Primary table
public class OrderDetailDto
{
    // Fields from primary table
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderNo))]
    public string OrderNumber { get; set; } = "";

    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.Amount))]
    public decimal Amount { get; set; }

    // Fields from secondary table
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
    public string CustomerName { get; set; } = "";

    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Phone))]
    public string CustomerPhone { get; set; } = "";
}
```

Generated SQL:

```sql
SELECT a.OrderNo, a.Amount, b.Name, b.Phone
FROM Orders a
LEFT JOIN Customers b ON a.UserId = b.Id
```

**Key points**:

- `[MapFrom]` annotates the primary table type; `[MapProperty]` declares the source table for cross-table fields.
- Supports three JOIN types: `LeftJoin`, `InnerJoin`, and `RightJoin`.
- Each `[MapProperty]` in the DTO precisely declares which table supplies that field.
- JOIN and column pruning occur on the SQL side—no extraneous columns are transmitted.

### JOIN Types

```csharp
// LeftJoin (left outer join)
fsql.Select<OrderEntity, CustomerEntity>()
    .LeftJoin((o, c) => o.UserId == c.Id)

// InnerJoin (inner join)
fsql.Select<OrderEntity, CustomerEntity>()
    .InnerJoin((o, c) => o.UserId == c.Id)

// RightJoin (right outer join)
fsql.Select<OrderEntity, CustomerEntity>()
    .RightJoin((o, c) => o.UserId == c.Id)
```

---

## 2. Three-Table Query

### Usage

[!code-csharp[FreeSql Three-Table Join AsDto Compose](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_MultiTableThreeWay)]

DTO definition (fields sourced from three tables):

```csharp
[MapFrom(typeof(OrderEntity))]  // Primary table
public class ReportDto
{
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderNo))]
    public string OrderNumber { get; set; } = "";

    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
    public string CustomerName { get; set; } = "";

    [MapProperty(typeof(ProductEntity), nameof(ProductEntity.Name))]
    public string ProductName { get; set; } = "";

    [MapProperty(typeof(ProductEntity), nameof(ProductEntity.Price))]
    public decimal ProductPrice { get; set; }
}
```

**Key points**:

- Supports up to three tables (`ISelect<T1, T2, T3>`).
- Parameter order in each `LeftJoin` lambda matches the generic arguments of `Select<T1, T2, T3>`.
- The `From<T2>` lambda parameter implements `ISelectFromExpression<T1>`.

---

## 3. Multi-Table Compose Degradation

When multi-table projection cannot be fully expressed in SQL (e.g., due to runtime computation, `Convert`, etc.), NCMapper automatically degrades to in-memory Compose:

```
ISelect<T1, T2>.AsDto().ToList<TDto>()
    → Detects non-projectable fields
    → Degrades: SQL fetches all required columns (full load)
    → In-memory calls Mapper.Compose to assemble the DTO
```

### Degradation Path Behavior

| Behavior | Projection Path | Degradation Path |
|---|---|---|
| Automatic `Include` | ✅ Applied | ✅ Applied |
| `PostMap` callbacks | ✅ Invoked | ✅ Invoked |
| `GlobalFilter` propagation | ✅ Propagated | ✅ Propagated |
| Column pruning | ✅ SQL-side | ❌ Full column load |

> **`Include` behavior in degradation path**: Navigation properties are still automatically included (via FreeSql’s `IncludeByPropertyName` or EF Core’s navigation collector), without requiring manual `.Include()`. Use `[MapNoAutoInclude]` to exclude specific navigation properties.

---

## 4. Multi-Table `WithSql`

For complex SQL (subqueries, CTEs, etc.), use `WithSql` to supply custom SQL:

[!code-csharp[FreeSql Multi-Table WithSql: Dual Data Source Replacement](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_TwoTableWithSql)]

**Key points**:

- For multi-table `WithSql`: the `sqlT2` parameter is mandatory (specifies SQL for the second table).
- Signature: `WithSql(sqlT1, sqlT2, param)` — two SQL strings correspond to two tables.
- After `WithSql`, you may still chain `LeftJoin`, etc.

---

## 5. `GlobalFilter` Propagation

FreeSql’s `GlobalFilter` (e.g., soft-delete, tenant isolation) is automatically propagated to both projection and degradation paths in multi-table queries:

```csharp
fsql.GlobalFilter.Apply<ISoftDelete>("SoftDelete", e => e.IsDeleted == false);

// Multi-table query automatically appends WHERE conditions
var dtos = fsql.Select<OrderEntity, CustomerEntity>()
    .LeftJoin((o, c) => o.UserId == c.Id)
    .AsDto()
    .ToList<OrderDetailDto>();
// SQL: ... FROM Orders a LEFT JOIN Customers b ON ...
//      WHERE a.IsDeleted = false AND b.IsDeleted = false
```

**Key points**:

- `GlobalFilter` applies in both projection and degradation paths.
- For multi-table queries, `WHERE` conditions are appended to *all* joined tables implementing the filter interface.

---

## Common Misuses

### Misuse 1: Cross-Table Fields Missing `[MapProperty]` Source Annotation

```csharp
// ❌ Misuse: Cross-table field lacks [MapProperty(typeof(T2), ...)]
// Unannotated cross-table field retains its default value
[MapFrom(typeof(OrderEntity))]
public class OrderDetailDto
{
    public string CustomerName { get; set; } = "";  // No [MapProperty] → defaults
}

// ✅ Correct: Cross-table fields must explicitly declare source type
[MapFrom(typeof(OrderEntity))]
public class OrderDetailDto
{
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
    public string CustomerName { get; set; } = "";
}
```

### Misuse 2: Missing `sqlT2` in Multi-Table `WithSql`

```csharp
// ❌ Misuse: Two-table WithSql supplied with only one SQL
var dtos = fsql.Select<OrderEntity, CustomerEntity>()
    .WithSql("SELECT * FROM Orders WHERE Amount > 1000")  // Missing sqlT2
    .LeftJoin((o, c) => o.UserId == c.Id)
    .AsDto()
    .ToList<OrderDetailDto>();
// Runtime exception: CustomerEntity has no data source

// ✅ Correct: Two-table WithSql requires two SQL strings
var dtos = fsql.Select<OrderEntity, CustomerEntity>()
    .WithSql("SELECT * FROM Orders WHERE Amount > 1000",
             "SELECT * FROM Customers")
    .LeftJoin((o, c) => o.UserId == c.Id)
    .AsDto()
    .ToList<OrderDetailDto>();
```

### Dot-Path Capabilities

Multi-table Compose projection supports **dot-path notation** and **inferred flattening**, consistent with single-table behavior (shared reuse of `BuildCompose`/`BuildCompose3` segment chains and loops):

```csharp
// Dot-path: Explicitly declare nested field (supported uniformly across all four engines since 2026-08)
[MapProperty(typeof(CustomerEntity), "Address.City")]
public string CustomerCity { get; set; } = "";

// Inferred flattening: Property name auto-resolves (AddressCity → Address.City)
public string? AddressCity { get; set; }

// Both work under JOIN or degradation paths; failures emit Trace output (no longer silently skipped)
```

---

## Summary

| Capability | API | Feature |
|---|---|---|
| Two-table query | `Select<T1,T2>().LeftJoin(...).AsDto()` | JOIN performed on SQL side |
| Three-table query | `Select<T1,T2,T3>().LeftJoin(...)` | Maximum 3 tables supported |
| JOIN types | `LeftJoin` / `InnerJoin` / `RightJoin` | All three standard JOIN types |
| Degradation path | Automatically triggered | Full SQL load → in-memory Compose |
| `WithSql` | `WithSql(sqlT1, sqlT2, param)` | Custom SQL; `sqlT2` is mandatory for two-table scenarios |
| `GlobalFilter` | Automatically propagated | Applies to both projection and degradation paths |

## Fluent DTO Support Status

In multi-table scenarios, **Fluent-channel** declarations for cross-table fields (`FromSource`) are *forced to degrade* at the composition surface: `FromSource` declarations lack per-property projection details (`HasComposeMappings` carries them), so Compose synthesis proceeds via in-memory multi-source composition — matching the degradation-path behavior of attribute-based cross-table fields (`[MapProperty(FromSource)]`). Purely translatable declarations on the main chain still benefit from projection acceleration (see FreeSql Single-Table guide).

---

Runnable demo on this page (Scenario 503, FreeSql in-memory mode):

[!code-csharp[Two-Table JOIN Projection: Cross-Table Field Retrieval](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_TwoTableJoin)]

[!code-csharp[One-to-Many Navigation Collection Projection: IncludeMany](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_IncludeManyNavigation)]

[!code-csharp[Fluent DTO Projection (Registry-Based)](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_FluentProjection)]

> Projection consumption contract (snapshot-first): Cross-table declarations (attribute `[MapProperty(FromSource)]` / Fluent `FromSource`) are carried by runtime snapshot `ComposeMappings` details. In single-table `AsDto`, cross-table field projection is inferred from primary-source navigation properties. Unregistered cases fall back to attribute resolution, preserving existing behavior.

### Fluent Compose Unlocking

Fluent-channel multi-source `FromSource` declarations in ORM projection have shifted from “always-degrade-to-in-memory-mapping” to **fine-grained translatability evaluation**:

| Declaration Form | Projection Behavior |
| --- | --- |
| Direct secondary-source field access / dot-path / index segment (no masking/conversion/formatting/JSON/conditional guards) | **Translatable** — projects on SQL side (single-table navigation inference + dual-table Compose facade both apply; cross-table fields become truly mappable for the first time) |
| Any `cm`-level `Mask` / `Convert` / `Format` / `FromJson` / `When` | Remains degraded (reason tagged with specific category) |
| Class-level `AfterMapping` / `BeforeMapping` / `MapSubclass` | Remains degraded |
| Declaration present but no output produced (`NCMAP_F29` family) | Conservatively remains degraded |

**Troubleshooting entry point**: When degradation occurs, enable `MapTracer.GloballyEnabled = true`; the logged `reason` (e.g., `Contains non-translatable declaration (Mask)`) identifies the non-translatable element — removing it enables SQL-side projection.

**To retain current two-step pattern**: Non-translatable forms automatically degrade (no action needed); for translatable forms, if you prefer explicit two-step queries + in-memory mapping pipeline (`Mapper.MapList`), do so manually — the framework provides no global fallback switch. Note: Degraded in-memory mapping does *not* populate secondary-source fields (cross-table fields remain `default`).

## Companion Examples

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 503
```

The corresponding code is located in the `TwoTableJoin`, `IncludeManyNavigation`, and `FluentProjection` methods within `samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs`. Scenario 501 additionally includes a demonstration of multi-table Compose via attribute-based configuration (`MultiTableTwoWay` / `MultiTableThreeWay` in `05-Orm/FreeSqlScenarios.cs`).

---


## Further Reading

- [FreeSql (I): Single-Table Operations](freesql-single-table.md) · [FreeSql (III): Advanced Terminal Methods](freesql-advanced.md)
- Technical Documentation: [NCMapper.FreeSql Extension Package](../../explanation/orm-freesql.md) §11.2
- Extension Source Code: `src/NCMapper.FreeSql/NCMapperFreeSqlExtensions.cs`
