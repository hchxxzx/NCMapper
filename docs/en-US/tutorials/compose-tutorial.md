> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: tutorials/compose-tutorial
Diagnostic codes involved:  
Library version: v1.0.0+  
Last updated: 2026-08-21  
---

# Multi-Source Composition (Compose) Scenario Walkthrough

## Overview

This tutorial addresses a concrete business problem: data required for a list page is scattered across multiple entities—how do you compose them into a single DTO in one operation? You’ll implement dual-source, triple-source, and batch composition in an order-related scenario, then verify the results.

The examples are drawn from scenario 02 in `samples/NCMapper.Samples` and are executable for validation. For a detailed API reference on composition capabilities, see [Multi-Source Composition (Compose)](../how-to/compose/compose.md). This tutorial focuses on the complete decision-making process within real-world business scenarios.

---

## 1. Scenario: Where Does Data for the Order List Page Come From?

The order list page must display three categories of information simultaneously:

| Data | Source | Description |
|------|--------|-------------|
| Order number, amount, status | `OrderEntity` | Primary source — one record per row |
| Customer name, tier, phone | `CustomerEntity` | Secondary source — associated by customer ID |
| Product information | `ProductEntity` | Tertiary source — used in reporting contexts |

Two common approaches exist:

- **Query orders first, then fetch customers individually**: Results in N+1 queries — poor performance for list pages  
- **Assemble an aggregated DTO**: One query to compose all required data — direct binding to the UI  

`Mapper.Compose` implements the second approach: synthesizing a target DTO from multiple source objects. Secondary-source fields are explicitly declared using `[MapProperty]`.

Comparison of trade-offs between the two approaches:

| Dimension | Individual Queries + In-Memory Join | Compose-Based Synthesis |
|-----------|-------------------------------------|--------------------------|
| Number of queries | N+1 (1 for orders + 1 per order for customer) | 1 (or determined by underlying data-fetching strategy) |
| Mapping configuration | Handwritten glue code, scattered across pages | Declared inside the DTO — defined once, reused everywhere |
| Maintainability | Adding/removing fields requires modifying join logic | Update only the DTO declaration |
| Suitable scenarios | When sources cannot be fetched together (e.g., cross-service calls) | When all data is already available and only view-model assembly remains |

Compose is ideal for scenarios where *data is already ready* and only needs to be assembled into a presentation model. When data resides across disparate storage systems (e.g., microservices or separate databases), it should still be retrieved in layers before composition.

---

## 2. Defining the Composition DTO

Define an `OrderDetailDto`, annotated with `[MapFrom(typeof(OrderEntity))]` to declare the primary source. Secondary-source fields (e.g., customer info) are declared using `[MapProperty(typeof(CustomerEntity), ...)]`:

[!code-csharp[OrderDetailDto Composition DTO Definition](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto)]

[!code-csharp[OrderDetailDto Composition DTO Definition · OrderId](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_OrderId)]

[!code-csharp[OrderDetailDto Composition DTO Definition · OrderNumber](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_OrderNumber)]

[!code-csharp[OrderDetailDto Composition DTO Definition · TotalAmount](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_TotalAmount)]

[!code-csharp[OrderDetailDto Composition DTO Definition · StatusText](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_StatusText)]

[!code-csharp[OrderDetailDto Composition DTO Definition · CustomerName](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_CustomerName)]

[!code-csharp[OrderDetailDto Composition DTO Definition · CustomerLevel](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_CustomerLevel)]

[!code-csharp[OrderDetailDto Composition DTO Definition · CustomerPhone](~/samples/NCMapper.Samples/Models/Common/Dtos/OrderDetailDto.cs#Common_OrderDetailDto_CustomerPhone)]

Key points:

- The **primary source** is declared via `[MapFrom]`. Properties from this source are matched automatically by name. For non-matching names, use `[MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderId))]` (see `OrderId`, `OrderNumber`).  
- All **secondary-source** fields must be explicitly declared using `[MapProperty(typeof(SecondarySourceType), nameof(SecondarySourceType.PropertyName))]`. At runtime, values are pulled from the corresponding source object as specified.  
- Enum-to-string conversions (`StatusText`, `CustomerLevel`) are declared identically to regular properties; the mapper handles type conversion along registered paths.

The first generic argument of `[MapProperty]` specifies the field’s source type — this is the core contract of composition DTOs: each property maps unambiguously to exactly one data source, resolved at compile time.

Source entity fields involved in this scenario:

| Source Entity | Field | Target DTO Property |
|---------------|-------|------------------------|
| `OrderEntity` (primary) | `OrderId`, `OrderNo`, `Amount`, `Status` | `OrderId`, `OrderNumber`, `TotalAmount`, `StatusText` |
| `CustomerEntity` (secondary) | `Name`, `Level`, `Phone` | `CustomerName`, `CustomerLevel`, `CustomerPhone` |

Even when primary-source property names match (e.g., `OrderId` → `OrderId`), explicit `[MapProperty]` declarations are required. All secondary-source properties must declare their source types explicitly.

---

## 3. Dual-Source Composition: Order + Customer

Call `Mapper.Compose<TTarget>(source1, source2)` to compose:

[!code-csharp[Dual-Source Composition](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose)]

[!code-csharp[Dual-Source Composition · MultiSourceCompose](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose_MultiSourceCompose)]

[!code-csharp[Dual-Source Composition · ComposeWithAction](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose_ComposeWithAction)]

Result: `OrderId`, `OrderNumber`, and `TotalAmount` come from `order`; `CustomerName`, `CustomerLevel`, and `CustomerPhone` come from `customer`.

Difference from single-source mapping: The number of arguments passed to `Compose` corresponds to the count of `[MapFrom]` + secondary-source declarations. The type specified in `[MapFrom]` identifies the primary source; subsequent arguments are matched against `[MapProperty]`-declared source types in order.

| Dimension | Single-Source Mapping (`Mapper.Map`) | Composition (`Mapper.Compose`) |
|-----------|--------------------------------------|----------------------------------|
| Number of sources | 1 | 2–3 |
| Primary source declaration | `[MapFrom]` | `[MapFrom]` (type-based lookup — position independent) |
| Secondary-source fields | Not applicable | Must be explicitly declared via `[MapProperty]` |
| Reverse extension | Writes back all fields | Writes back only primary-source fields |
| Typical use case | Entity ↔ DTO (unidirectional/bidirectional) | Aggregated view models from multiple tables |

---

## 4. Triple-Source Composition: Order + Customer + Product

Reporting scenarios often require a third source — simply add another parameter:

[!code-csharp[Triple-Source Composition](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose)]

[!code-csharp[Triple-Source Composition · MultiSourceCompose](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose_MultiSourceCompose)]

[!code-csharp[Triple-Source Composition · ComposeWithAction](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose_ComposeWithAction)]

`Compose<T>` supports any number of sources (this guide demonstrates up to three). The primary source is located by matching the type declared in `[MapFrom]` (**position-independent — need not be the first argument**); remaining arguments must match the types declared in `[MapProperty]` attributes in order — resolution occurs at runtime by type.

---

## 5. Batch Composition: Order List

A list page contains many items—not just one. Use `ComposeList` for bulk composition:

[!code-csharp[Batch Composition for Lists](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_ComposeList)]

`ComposeList` accepts a *jagged array of source objects*: the outer array represents one DTO per element; the inner array holds all source objects (primary + secondary) needed to compose that DTO. All inner arrays must have identical lengths — otherwise, misalignment occurs (see Common Pitfalls #3).

Batch composition suits two input patterns:

- **Pre-grouped data**: Orders and customers queried separately, then paired by index  
- **Directly correlated query results**: Main query and joins return rows in identical order  

Important notes:

- The **order of source objects per group must be consistent**: Group 1 = `(order1, customer1)`, Group 2 = `(order2, customer2)` — no interleaving  
- `ComposeList` applies the same composition configuration to each group — behavior matches `Compose` for individual items  
- For large datasets, process in batches (e.g., per page) to avoid constructing oversized arrays  

---

## 6. Post-Composition Processing

When composition results require uniform post-processing (e.g., prefixing, formatting), use an inline `Action` callback:

[!code-csharp[Post-Composition Processing](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_L60)]

The callback executes after composition completes, receiving the composed DTO as its argument. Ideal for pre-rendering transformations in the presentation layer — avoids polluting mapping configurations.

---

## 7. Reverse Direct Mapping (DTO → Primary Entity)

DTOs declaring cross-table secondary-source fields also generate reverse extensions — but only primary-source fields are written back:

[!code-csharp[Compose Reverse Direct Mapping](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_L83)]

The reverse extension targets the primary entity declared in `[MapFrom]` (`MapToOrderEntity`). Secondary-source fields (e.g., customer info) are excluded from write-back — because secondary-source objects are not retained as references on the DTO, reverse mapping affects only the primary source. To persist changes to secondary sources, update those entities explicitly in the business layer.

---

## Combined Usage: Projection for Primary Table + Compose for Assembly

For high-volume list pages, combine both capabilities: First use ORM projection (`AsDto`) to fetch the primary-table DTO list directly in SQL, then batch-compose secondary-source data. Primary fields come from projection; secondary fields come from composition — leveraging strengths of both (see [ORM Projection Scenario Walkthrough](./orm-projection-tutorial.md)).

Key integration points:

- The primary-table DTO and the Compose DTO can be the same type: Projection populates primary-source fields; secondary-source fields are filled later via composition  
- Fetch secondary sources in bulk using primary-table foreign keys — avoid N+1 lookups  
- Maintain strict ordering across source groups during batch processing  

## Running Validation

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 301
```

Scenario 301 outputs mapping results for dual-source, triple-source, batch, callback, and reverse sub-scenarios. No ORM installation is required beforehand — Compose is part of the core library and has zero external dependencies.

## Validating Composition Configuration

Composition DTOs participate fully in mapping validation. `Mapper.Validate()` scans `[MapFrom]` type pairs, including Compose scenarios:

- If a `[MapProperty]` declares a nonexistent source type or field name, diagnostics appear at compile time — and `Validate()` reports them at runtime  
- Swapping primary and secondary source types (e.g., placing a secondary type in `[MapFrom]`) surfaces at composition call time  

We recommend running `Validate()` immediately after defining your composition DTO to confirm all `[MapProperty]` declarations resolve correctly ([Validation & Diagnostics](../how-to/infrastructure/validation.md)).

## Common Pitfalls

| Pitfall | Symptom | Fix |
|---------|---------|-----|
| Missing `[MapProperty]` on secondary-source fields | Secondary fields retain default values — silent failure | Always declare secondary fields with `[MapProperty(typeof(SecondaryType), nameof(...))]` |
| Confusing primary vs. secondary sources | Secondary fields overwritten or omitted due to naming collisions | Primary source = type in `[MapFrom]`; all others must declare source types explicitly |
| Mismatched lengths in `ComposeList` source arrays | Misaligned mappings → incorrect data | Ensure every inner array has identical length and consistent ordering |
| Expecting reverse mapping to write back secondary sources | Secondary fields unchanged in target entities | Secondary-source updates must be handled separately in the business layer (see Section 7) |

---

## Quick Reference Table

| Scenario | API | Description |
|----------|-----|-------------|
| Declare composition DTO | `[MapFrom(typeof(PrimarySource))]` + `[MapProperty(typeof(SecondarySource), ...)]` | Primary-source properties matched by name; secondary sources declared explicitly |
| Dual-source composition | `Mapper.Compose<T>(src1, src2)` | Primary source + one secondary source |
| Multi-source composition | `Mapper.Compose<T>(src1, src2, src3, ...)` | Any number of sources (demonstrated up to three) |
| Batch composition | `Mapper.ComposeList<T>(sourceGroups)` | Jagged array: outer = DTOs, inner = per-DTO source objects |
| Post-composition processing | `Mapper.Compose<T>(dto => ..., sources)` | Inline `Action` callback executed after composition |
| Reverse direct mapping | `dto.MapToOrderEntity()` | Writes back *only* primary-source fields (secondary fields ignored) |

---

## Related Documentation

- [Multi-Source Composition (Compose)](../how-to/compose/compose.md): Full API reference and AOT behavior  
- [Quick Start](../getting-started/tutorial-15min.md): 15-minute foundational mapping walkthrough  
- [ORM Projection Scenario Walkthrough](./orm-projection-tutorial.md): DTO composition on the query side
