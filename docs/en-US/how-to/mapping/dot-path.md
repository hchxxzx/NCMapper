> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: how-to/mapping/dot-path
Diagnostic codes involved: NCMAP001, NCMAP005, NCMAP033, NCMAP034  
Library version: v1.0.0  
Last updated: 2026-09-15  
---

# Dot Paths and Nested Mapping

## 1. What Is a Dot Path?

The `fromField` parameter of `[MapProperty]` supports dot notation (`xx.xx`) to declare the source of a **nested property** mapping:

```csharp
// Dot path: maps the City field from the Address navigation property of the source entity → target CustomerCity property
[MapProperty(typeof(CustomerEntity), "Address.City")]
public string CustomerCity { get; set; } = "";
```

Dot paths are parsed segment-by-segment (case-insensitive matching using `OrdinalIgnoreCase`). Arbitrary nesting depth is supported (e.g., `"Order.Customer.Address.City"`); intermediate segments must be `class` or `interface` types (value types and `string` cannot serve as intermediate segments).

### 1.1 Root-Type Anchoring and Path Resolution

The `typeof(CustomerEntity)` argument anchors the **root type** for path resolution: `"Address.City"` resolves to `CustomerEntity.Address.City` (i.e., `source.Address.City`, where `source` is an instance of `CustomerEntity`). Note that the root-type segment is **not written in `fromField`**, but is implicitly provided by the `typeof` argument:

```csharp
[MapProperty(typeof(CustomerEntity), "Address.City")] // Resolves to: CustomerEntity.Address.City
public string CustomerCity { get; set; } = "";
```

- **The first segment must be a direct property of the root type**: `Address` must exist directly on `CustomerEntity`.  
- To map a grandchild property, specify the full path: `"CustomerInfo.Address.City"` (the first segment still starts from the root type).  
- Segment matching is case-insensitive: `"address.city"` matches identically.

## 2. Dot Path vs. Naming-Convention-Based Flattening

| Approach | Syntax | Trigger |
|----------|--------|---------|
| **Dot path** | `[MapProperty(typeof(T), "Address.City")]` | Explicit declaration — mapping source = deep field within nested navigation. |
| **Inferred flattening** | Target property name `AddressCity` | Automatically splits on PascalCase boundaries → `Address.City` (no attribute required). |

These two mechanisms are independent: inferred flattening is automatically triggered by naming conventions; dot paths are explicitly declared, used when the target property name cannot express the desired source via naming convention alone (e.g., `CustomerCity` must unambiguously map from `Address.City`, not `Customer.City`).

## 3. Declaration Forms

The `fromField` string parameter of `[MapProperty]` supports three forms:

- **Simple property name**: `"DeptName"` (most common for same-source, same-type mappings)  
- **Dot path**: `"Address.City"` (for nested navigation)  
- **"Type.Property" syntax**: Equivalent to `typeof` + `nameof`

We recommend the strongly-typed `typeof` + `nameof` form for compile-time safety (renaming properties automatically updates references); string-based forms are suitable for dynamic or simplified scenarios:

```csharp
[MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Address))]
public string City { get; set; } = "";
```

## 4. Behavior Across Four Engines

Dot paths are consistently supported across all four engines: Source Generator, Runtime, ORM Projection, and Compose (SourceGen):

| Engine | Behavior |
|--------|----------|
| Source Generator | Parses segments and generates value-access chains; cross-table (Compose) support included. |
| Runtime | Accesses values segment-by-segment via expression trees. |
| ORM Projection | JOINs or path degradation apply (see [FreeSql Multi-Table](../orm/freesql-multi-table.md)). |
| Compose | During multi-table composition, retrieves values following the declared segment chain. |

## 5. Observable Failure Handling

Dot path failures are **explicitly observable**, not silently skipped:

- **Source Generator**: Segment resolution failure triggers **NCMAP001** (standard mapping) or **NCMAP005** (Compose cross-table) warnings, and **omits code generation for that mapping**. Diagnostics include the failing segment name and its declaring type.  
- **Runtime**: Throws `MapConfigurationException`.  
- **Projection side**: Skips the property and emits a Trace log.

**First-segment failure (missing root-type property) fails immediately**, without proceeding to subsequent segments. For example, if `CustomerEntity` has no `Address` property:

- **Runtime message** (includes failing segment + declaring type):  
  ```
  [NCMapper] Explicit dot path 'Address.City' failed at segment 'Address' (property not found in type CustomerEntity); target property CustomerCity cannot be mapped.
  ```

- **Compile-time**: NCMAP001 diagnostic reports failing segment `Address` and type `CustomerEntity`; no mapping code is generated for `CustomerCity`.

> **Related API**: `DotPathFailureKind` — an enumeration categorizing dot-path failure causes (5 forward-direction cases: invalid indexer syntax / indexer must be non-negative integer / property not found / value type not navigable / non-collection type cannot be indexed; 2 reverse-direction cases: property not found / value type not navigable). Runtime error messages are generated from `ErrorMessages` based on this classification (source anchor: ErrorMessages.cs:816).

## 6. Composition with `[Mask]` / `[Convert]`

`[Mask]` and `[Convert]` compose seamlessly with dot paths — masking and custom conversion on dot-path properties behave identically to those on direct properties:

- **Forward direction (source → target)**: The leaf value of the dot path is passed through null-safe access before entering the converter/masker; `[Convert]` output may be further masked via `[Mask]` (`Mask(Convert(...))` pattern).  
- **Reverse direction (DTO → Entity write-back)**: `[ConvertBack]` is consumed if present; otherwise, `[Convert]` is **not consumed** during reverse mapping (converters are generically bound to the forward direction only — see NCMAP033 warning). To enable reverse custom conversion, explicitly annotate with `[ConvertBack]`.  
- `[Mask]` reverse write-back writes the masked value unchanged (round-trip lossy — see NCMAP034 warning).

## 7. Reverse Write-Back (DTO → Entity)

In the reverse direction (DTO → Entity), nested paths perform **write-back** (assigning flat DTO values into nested object paths):

- Intermediate nodes are `new`-created if `null`.  
- Entire assignment is skipped if the source value is `null`.  
- `[ConvertBack]` is an exception (enables reverse custom conversion); without it, `[Convert]` is ignored (NCMAP033).  
- Collection indexing and interface intermediate nodes are skipped, with diagnostics/Trace emitted.

```csharp
// Forward: Entity.Address.City → DTO.City (read path)
// Reverse: DTO.City → Entity.Address.City (write-back; creates Address if null)
```

`null` write-back semantics:  
- If source value is `null` and the target path exists → leaf is cleared (nullable leaf assigned `null`; non-nullable leaf assigned `default(T)`).  
- If target path does not exist → no-op.  
- If source value is non-`null` → intermediate nodes are created if `null`, then leaf is assigned.

## 8. Related Topics

- [Mapping Rules (Semantics)](../../explanation/semantics/mapping-rules.md): `[MapProperty]` alias mapping and ignore lists.

This page includes a runnable demo (Scenario 201: Dot Path Flattening):

[!code-csharp[Dot Path and Flattening: AddressCity ← Address.City](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_DotPath)]
- [FreeSql Multi-Table](../orm/freesql-multi-table.md): Dot paths in Compose projections  
- [Nested Object Scenarios Explained](../../explanation/mechanics/nested-mapping.md): Complete coverage of flattening and reverse write-back scenarios
