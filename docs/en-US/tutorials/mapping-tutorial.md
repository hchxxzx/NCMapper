> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: tutorials/mapping-tutorial
Diagnostic Code: NCMAP033
Library Version: v1.0.0+
Last Updated: 2026-09-29
---

# Daily Bidirectional Mapping Walkthrough: User Profile Page and Profile Update

## Overview

This tutorial addresses one of the most common business scenarios: the “read” and “update” operations in a user profile module. The display page must convert an entity into a DTO for frontend consumption; the Save button must write the submitted DTO back to the entity and persist it to the database. You’ll use name-matching auto-mapping, compile-time direct extension methods, in-place updates, nested property access, and directional ignore — all among the highest-frequency mapping patterns used in daily development.

The examples align semantically with scenario `02` in `samples/NCMapper.Samples` and are fully executable for validation. Each section provides concise illustrative code snippets; complete working code is provided at the end for holistic understanding. Full parameter documentation for each method is available in [Attribute-Based Basic Mapping](../how-to/declarative/basics.md) and [Dot-Path and Nested Mapping](../how-to/mapping/dot-path.md); this tutorial focuses on how to integrate mapping into real-world business workflows.

---

## 1. Scenario: Two Operations in One Profile Module

| Operation | Direction | Characteristics |
|-----------|-----------|-----------------|
| Opening the profile page | Entity → DTO | Display fields largely share names with entity properties |
| Saving the profile | DTO → Entity | Writing back to the database entity; display-only fields must *not* be persisted |

Model (simplified for demonstration):

```csharp
public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
    public Address Address { get; set; } = new();   // Nested: City / Street
}

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}
```

Placing `[MapFrom]` on the DTO enables automatic name-based property matching — **no explicit mapping code required**.

---

## 2. Opening the Profile Page: Starting with Name-Based Mapping

The most common data-fetching pattern for display pages (concise illustration; matches demo scenario `BasicMapping_SimpleNameMatch`):

```csharp
public static class UserScenarios
{
    // Three overloads: single object / list / callback — runnable versions referenced below
}
```

[!code-csharp[Single object: auto-match by property name](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]
[!code-csharp[List mapping](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ListMap)]
[!code-csharp[With callback: enrich DTO post-mapping (e.g., prefix VIP users)](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapWithAction)]

Three key points:

- `Mapper.Map<S, T>(src)` handles single objects; `Mapper.MapList<S, T>(srcs)` handles lists.
- The callback overload (`Map(src, (s, t) => ...)`) is ideal for enriching the DTO immediately after mapping (e.g., adding a prefix for VIP users).
- Properties with identical names are matched automatically; nested properties (e.g., `Address.City`) are automatically flattened to `AddressCity` (see Section 5).

---

## 3. Going Further: Compile-Time Direct Extension Methods

For type pairs annotated with `[MapFrom]`, the source generator emits dedicated extension methods — eliminating runtime lookup of `Mapper.Map` entirely:

[!code-csharp[Source-generated extension: `using NCMapper.Generated` + direct call](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#Core_Generated_EntryExtensions)]

These extensions execute the same generated logic as the unified `Mapper.Map` entry point — zero performance difference. Their advantage is **compile-time type safety**: incorrect type names cause compilation failures, and IDE navigation works out-of-the-box. Adoption is optional and team-preference–driven.

---

## 4. Saving the Profile: Writing DTO Back to Entity

The save operation supports three distinct patterns, corresponding to different business needs:

[!code-csharp[Reverse direct-call patterns: new instance / in-place update / with callback](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#Declarative_ReverseMap_New)]

The reverse extension family offers six overloads (① new instance, ② in-place update, ③ new instance with callback, ④ in-place update with callback, ⑤ list new, ⑥ list with callback), automatically generated for every `[MapFrom]` DTO — **one declaration yields full bidirectional support**:

[!code-csharp[All six reverse extension overloads](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ReverseExtensions)]

In-place updates (i.e., no new object allocation) are handled by `Mapper.Map(src, target)`; corresponding extension methods mirror this signature exactly:

[!code-csharp[Signature for updating existing target](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_UpdateExisting)]

---

## 5. Nested Property Access: How to Display Addresses

The address is modeled as a nested object in the entity (`Address.City` / `Address.Street`), while UI forms typically expose two flat text inputs. Automatic flattening bridges this gap seamlessly: naming a DTO property `AddressCity` causes it to be populated from `Address.City`:

```csharp
[MapFrom(typeof(UserEntity))]
public class UserDotPathDto
{
    public string? AddressCity { get; set; }    // Auto-resolved from Address.City (PascalCase flattening)
    public string? AddressStreet { get; set; }  // Auto-resolved from Address.Street
}
```

[!code-csharp[Nested property access: flat-property auto-mapping](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_DotPath)]

When explicit control is needed (e.g., non-matching names or deep paths), use `[MapProperty(typeof(SourceType), "Address.City")]` to declare dot-path expressions. During reverse mapping, flat properties can also write back to nested paths — if the source value is `null`, leaf fields are cleared (no stale values remain).

---

## 6. Display-Only Fields: Preventing Write-Back to Database

Profile pages often include fields intended *only for display*, such as `DisplayLabel` (a concatenated display string). Directional ignore controls participation per mapping direction:

```csharp
[MapFrom(typeof(CustomerEntity))]
public class CustomerDto
{
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";

    // DTO-only computed field: excluded from both directions (value precomputed by service layer)
    [MapIgnore(Direction.Both)]
    public string DisplayLabel { get; set; } = "";
}
```

On save: `DisplayLabel` is *not* written back to the entity —

[!code-csharp[Reverse mapping ignores display field (`DisplayLabel` not persisted)](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapIgnore)]

`[MapIgnore]` supports three direction modes — choose based on field semantics:

| Syntax | Behavior | Use Case |
|--------|----------|----------|
| `[MapIgnore]` or `[MapIgnore(Direction.Both)]` | Excluded from both directions | DTO-only fields (display labels, computed columns) |
| `[MapIgnore(Direction.ToEntity)]` | Excluded only during reverse mapping (DTO → Entity) | Field read for display but never persisted |
| `[MapIgnore(Direction.ToDto)]` | Excluded only during forward mapping (Entity → DTO) | Field used exclusively during save (e.g., raw input tokens) |

---

## 7. Validation and Common Issues

**How do I verify no fields are missed in either direction?** Run `Mapper.Validate()` once during development — it scans all `[MapFrom]` type pairs and reports unmapped properties ([Mapping Validation](../how-to/infrastructure/validation.md)).

**Why isn’t my forward-direction `[Convert]` applied during reverse mapping?** Forward-declared `[Convert]` converters are *not* reused in reverse by default (a compile-time `NCMAP033` diagnostic warns about this). For round-trip scenarios (format on display, parse on save), explicitly declare a reverse converter using `[ConvertBack]` — formatting and parsing are declared and applied independently.

**Does name-based mapping overwrite fields I don’t want modified?** Yes — matching names trigger assignment. To protect fields, apply `[MapIgnore]` (both directions) or `[MapIgnore(Direction.ToEntity)]` (save direction only), as shown in Section 6.

---

## 8. Complete Code Example

Combines patterns from Sections 2–6 into a cohesive profile service implementation (pedagogical completeness: enum `Level` is auto-converted to text; models are simplified for clarity):

```csharp
using System;
using System.Collections.Generic;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Generated;

// ================= 1. Models =================
public class Address
{
    public string City { get; set; } = "";
    public string Street { get; set; } = "";
}

public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
    public Address Address { get; set; } = new();
}

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}

// Flattened DTO: nested address exposed as two text inputs
[MapFrom(typeof(UserEntity))]
public class UserDotPathDto
{
    public string? AddressCity { get; set; }
    public string? AddressStreet { get; set; }
}

public class CustomerEntity
{
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Region { get; set; } = "";
}

[MapFrom(typeof(CustomerEntity))]
public class CustomerDto
{
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";

    // DTO-only computed field: excluded from both directions
    [MapIgnore(Direction.Both)]
    public string DisplayLabel { get; set; } = "";
}
```

All six `UserProfileService` business methods have runnable implementations (same type pairs, file `BasicScenarios.cs`):

[!code-csharp[Read: single object](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]
[!code-csharp[Read: list](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ListMap)]
[!code-csharp[Read: flattened nested address view](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_DotPath)]
[!code-csharp[Update: new instance (reverse direct-call `MapToUserEntity`)](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ReverseDirectExtension)]
[!code-csharp[Update: in-place update (reverse direct-call with `target`)](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ReverseDirectUpdate)]
[!code-csharp[Update: save customer (display fields skipped automatically)](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ForwardMap)]

Reading order recommendation:  
1. First examine the models — one `[MapFrom]` annotation delivers full bidirectional capability.  
2. Then review the service — each business action maps in a single line.  
3. Finally, note `CustomerDto.DisplayLabel`: excluded from *both* directions, representing the canonical pattern for “DTO-only fields”.

---

## Related Documentation

- [Attribute-Based Basic Mapping](../how-to/declarative/basics.md): Reference for attribute-driven configuration
- [Dot-Path and Nested Mapping](../how-to/mapping/dot-path.md): Complete rules for nested property access and reverse write-back
- [Property-Level Formatting](../how-to/declarative/map-format.md): Use `[MapFormat]`/`.Format` to output formatted dates, numbers, or enum text in one line
- [Reverse Mapping Semantics](../explanation/reverse-mapping-semantics.md): In-depth explanation of reverse-derived behavior
- [Fluent Modeling Tutorial](fluent-modeling-tutorial.md): Switch to Fluent-style centralized mapping declarations when relationships grow complex
