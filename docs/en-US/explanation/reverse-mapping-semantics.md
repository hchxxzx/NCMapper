> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

***

wiki-id: explanation/reverse-mapping-semantics  
Diagnostic codes involved: NCMAP032, NCMAP033, NCMAP034, NCMAP035, NCMAP036, NCMAP037, NCMAP046  
Library version: v1.0.0+  
Last updated: 2026-09-30  
----------------

# Reverse Mapping Semantics

## Overview

Reverse mapping refers to the DTO → Entity direction — a direction that is *not symmetric* with forward mapping (Entity → DTO). This article explains the definition of reverse mapping direction, custom conversion handling rules, risks of masked-value writeback, `null` writeback semantics, the ignore list for reverse mapping, and related diagnostic codes.

The generation rules for reverse direct-call extensions (six overloads) are covered elsewhere; this article focuses exclusively on semantics: `[MapFrom]` DTOs are uniformly generated, and DTOs declaring cross-table sub-source fields are also generated (see [Compose §5.1](../how-to/compose/compose.md)).

***

## 1. Reverse Mapping Direction Definition

### 1.1 Direction Determination

When `[MapFrom(typeof(Entity))]` is declared on a DTO, it means “DTO maps *from* Entity.” This defines the direction as follows:

| Direction        | Source | Target | Generated Artifacts                                                |
| ---------------- | ------ | ------ | ------------------------------------------------------------------ |
| Forward (`ToDto`)    | Entity | DTO    | `Map_Entity_To_DTO`, `entity.MapToXxxDto()`                         |
| Reverse (`ToEntity`) | DTO    | Entity | `dto.MapToXxxEntity()`, `dtos.MapToXxxEntityList()`                 |

Six reverse-overload extensions are uniformly generated for `[MapFrom]` DTOs: new instance (`()`), with callback (`(Action)`), update existing object (`(entity)` / `(entity, Action)`), and list variants (`List()` / `(Action)`), mirroring the forward overloads. DTOs declaring cross-table sub-source fields are likewise generated; however, reverse mapping writes back *only* to primary source fields (see [Compose §5.1](../how-to/compose/compose.md)).

### 1.2 Scope of Reverse Mapping

Reverse mapping applies *only* to properties in the DTO that declare an explicit source (i.e., primary source fields). For DTOs declaring cross-table sub-source fields, those sub-source fields *do not participate* in reverse writeback (see [Compose §5.1](../how-to/compose/compose.md)).

### 1.3 Direction Determination Rules

Direction is determined by the location of the `[MapFrom]` attribute: when the *target type* is annotated with `[MapFrom(SourceType)]`, it means “target maps *from* source.”

| Annotation Location                          | Forward                           | Reverse                            |
| -------------------------------------------- | ----------------------------------- | ------------------------------------ |
| `[MapFrom(typeof(Entity))]` on DTO          | Entity → DTO (generates `MapToXxxDto`)   | DTO → Entity (generates `MapToXxxEntity`) |
| Neither side annotated with `[MapFrom]`      | Direction cannot be inferred; `[MapIgnore]` directionality rules are constrained | Same as left column                  |

Direction determination applies to directional ignore rules such as `[MapIgnore(Direction.ToEntity)]`. When annotated at the canonical location (i.e., on the DTO), direction inference is unambiguous. Non-canonical annotation (e.g., `[MapFrom]` placed on the Entity) yields counterintuitive direction inference; a one-time Trace warning is emitted at runtime (see [Mapping Rules (Semantics)](./semantics/mapping-rules.md)).

***

## 2. `[Convert]` Is Not Consumed in Reverse Mapping (NCMAP033)

### 2.1 Rule

Static conversion methods referenced by `[Convert]` are bound to the *forward direction*: `TField` (in generic signatures T1/T3) denotes the *source field type on the Entity*, and `TSource` (in T2/T3) denotes the *Entity type*. Therefore, reverse mapping (DTO → Entity) **does not consume** these conversion methods by default:

- Direct property assignments and writeback paths use standard type-conversion logic.

- The compiler reports **NCMAP033** (Info): "`[Convert]` property skipped during reverse mapping."

### 2.2 Reason

The method signature is bound to the forward direction (`TSource` = Entity type, `TField` = Entity-side field type). During reverse invocation, input types (DTO object / DTO field) mismatch the expected parameter types — consuming the method would cause type mismatches. To implement custom reverse conversions, use `[ConvertBack]` (see next section).

***

## 3. `[ConvertBack]`: Custom Reverse Conversion (NCMAP035/036/037)

### 3.1 Rule

`[ConvertBack(typeof(Converter), nameof(Converter.Convert))]` is applied to a DTO property to explicitly invoke the static conversion method during reverse mapping. Its signature template (T0–T3, structurally identical to `[Convert]`, but with `TSource`/`TField` bound to the *reverse direction*) is:

```csharp
public static TTarget Convert(DTOType source, TField fieldValue)  // T3 template
```

Annotating both `[Convert]` and `[ConvertBack]` on the same property is valid: forward mapping consumes `[Convert]`, reverse mapping consumes `[ConvertBack]`, and they do not interfere.

### 3.2 Annotation Location and Two Consumption Patterns

The canonical annotation location for `[ConvertBack]` is on the DTO property (§3.1). Annotation on the Entity side (i.e., the *reverse target* side) is also supported and consumed in two patterns — behavior is consistent between runtime and source generator engines:

**Pattern ①: Entity-side annotation + no matching DTO property (“source-less” reverse consumption)**  
When `[ConvertBack]` is applied to an Entity property *and* no property of the same name exists on the DTO, the converter is still invoked — the `source` parameter receives the *entire DTO object* (`TSource` = DTO type). This supports one-to-many splitting or computed properties without a direct DTO counterpart (e.g., splitting DTO `FullName` into Entity `FirstName`/`LastName`).  

Gating conditions (identical to ordinary reverse properties; hit any gate → converter is skipped):

- **Writeability Gate**: Skipped if the Entity property is read-only (`get`-only); `init`-only properties are consumed normally.  
  *(Source generator reports NCMAP013; runtime emits Trace skip.)*

- **Global Ignore Gate**: Suppressed if the Entity property name matches a global `Ignore*ToEntity` list entry (§7, see NCMAP021 guidance); overridden via `[ConvertBack(..., OverrideIgnore = true)]`.

- **Writeback Override Gate**: Silently excluded if the Entity property is already covered as a segment of a reverse writeback path (e.g., flattened via `[MapProperty]` path folding). Writeback chain output takes precedence to avoid double assignment to the same property.

**Pattern ②: Matching-pair annotation on Entity side (“fallback position”, DTO-first priority)**  
When both Entity and DTO have a property of the same name *and* `[ConvertBack]` is annotated on the Entity, it serves as a *fallback*: the DTO-side annotation takes precedence if present; otherwise, the Entity-side annotation is consumed. Template binding remains consistent: `TSource` = DTO type, `TField` = DTO-side field type, return type = Entity property type.

**Pattern ③: Annotation on Entity leaf property in writeback chains (“non-canonical position”, DTO-first / leaf-fallback)**  
In reverse writeback chains (e.g., flat DTO property `AddressCity` → nested Entity path `"Address.City"` via `[MapProperty(typeof(Entity), "Address.City")]`), annotating `[ConvertBack]` on the **Entity leaf property** (e.g., `City` in `Address.City`) is also valid: DTO-side annotation takes precedence if present; otherwise, the leaf-side annotation is consumed (lookup order matches Pattern ②). Template binding: `TSource` = DTO type, `TField` = DTO flat property type, return type = Entity leaf property type (converter’s `source` parameter is the DTO object); when source is `null`, the leaf is cleared *without invoking the converter* (§6.2 semantics preserved). Behavior is consistent across runtime and source generator engines.

Unsupported annotations (silently ignored, no consumption):

- **Intermediate properties**: Navigation containers (non-value endpoints) are silently excluded (writeback chain takes precedence; no double assignment).

- **Collection index paths** (e.g., `"Items[0].Name"`): Entire reverse writeback is skipped (undefined semantics), so validation is also skipped.

Signature mismatches report NCMAP035/036/037 (§3.3); diagnostics for leaf-side annotations target the leaf property.

See full examples in [Custom Reverse Conversion](../how-to/declarative/basics.md) (procedural) and [Reverse Conversion Consumption Semantics](./semantics/mapping-rules.md) (semantic).

### 3.3 Mismatch Diagnostics

Method resolution failures report **NCMAP035** (no accessible static method found on anchor type), **NCMAP036** (overload ambiguity), or **NCMAP037** (no overload matches T0–T3 templates) — all compile-time errors. This early detection prevents runtime surprises from invalid method references.

***

## 4. `[Mask]` Property Reverse Writeback (NCMAP034)

### 4.1 Rule: Mask Is Forward-Only

`[Mask]` is a **forward-only** attribute: `IMask.Mask()` is invoked *only* during forward mapping (Entity → DTO); reverse mapping (DTO → Entity) **does not invoke** the masker — reverse writeback uses the DTO property’s *current value*, regardless of whether `[Mask]` is applied.

### 4.2 Risk

If the DTO property’s current value is the result of forward masking, reverse writeback performs “masked value written back verbatim”:

```text
Entity (full value) → forward masking → DTO (masked value) → reverse writeback → Entity (masked value, original data overwritten)
```

This round-trip is *lossy*: the Entity’s full data is overwritten by the masked value. The compiler reports **NCMAP034** (Info): "`[Mask]` property participates in reverse writeback (masked value will be written back verbatim)." NCMAP034 is a **warning only** (indicating lossy writeback), *not* an assertion that masking occurs again in reverse — the masker is never invoked during reverse mapping.

### 4.3 Recommended Mitigation

In CRUD update scenarios, mask-annotated DTO properties should be annotated with `[MapIgnore(Direction.ToEntity)]` to block reverse writeback and prevent overwriting full Entity data with masked values. Retaining masking for display while blocking writeback is a common pattern.

***

## 5. `[MapFormat]` Does Not Assign in Reverse (NCMAP046)

### 5.1 Rule

`[MapFormat]` (Fluent equivalent: `.Format`) is a **forward-only** declaration: during forward mapping, the source value is formatted into text using the format string and assigned to the target `string` property (`IFormattable.ToString(format, null)`, always passing `null` provider = current culture). In reverse mapping (DTO → Entity), properties annotated with `[MapFormat]` are **not assigned**:

- The Entity-side property retains its original value; other fields are written back normally.
- Compile-time warning **NCMAP046** (Warn; Fluent channel equivalent: NCMAP_F67).
- **Bare declaration is fully valid**: `[MapFormat]` alone (without `[ConvertBack]`) is *not* an error — formatting is a one-way display declaration.

### 5.2 Isomorphism with `[Mask]`

Both are **unidirectional, lossy display declarations**: `[Mask]` loses information via truncation; `[MapFormat]` loses information via formatting (and culture dependence). Neither can be losslessly reversed; the library does *not* attempt heuristic reverse parsing. Key differences lie in reverse handling and diagnostic severity:  
- `[Mask]`: masked value is **written back verbatim**, causing lossy round-trip (NCMAP034 Info warns about the writeback behavior).  
- `[MapFormat]`: value is **never written back**, leaving Entity unchanged (NCMAP046 Warn indicates the skip behavior).

### 5.3 Round-Trip Implementation

When a formatted field must also be written back to the Entity, supplement the same anchor with `[ConvertBack]` / `.ConvertBack`: the two attributes (terminal) are **orthogonal in direction** — forward consumes `[MapFormat]` (formatting output), reverse consumes `[ConvertBack]` (explicit reverse parsing). They may coexist legally on the same property without interference; NCMAP046 is suppressed when `[ConvertBack]` is hit. To eliminate the warning without round-trip support, annotate with `[MapIgnore(Direction.ToEntity)]` to explicitly block reverse mapping.

***

## 6. `null` Writeback and Clearing Semantics

### 6.1 Rule

When a `null` source value is written back along a reverse path:

| Path State     | Behavior                                  |
| -------------- | ----------------------------------------- |
| Path exists    | Leaf cleared: nullable leaf → `null`, non-nullable value leaf → `default(T)` |
| Path does not exist | no-op: no intermediate nodes created, leaf not written |

Path clearing proceeds segment-by-segment without creating intermediate nodes. In `[ConvertBack]` branches, the converter is *not invoked* when source is `null` (leaf-clearing semantics remain unchanged, see §6.2).

### 6.2 Exception in Conversion Branches

`[ConvertBack]` branch: when the source value (DTO property) is `null`, the **converter is not invoked**, yet leaf-clearing semantics match ordinary properties — if the path exists, the leaf is cleared (`null` for nullable leaves, `default(T)` for non-nullable value leaves); if the path does not exist, it’s a no-op (no intermediate nodes created, leaf not written). What is skipped is the **converter call** (invoking with `null` input yields unpredictable output — skipping is safer), *not* leaf assignment — when source is `null`, the Entity leaf is still cleared to `null`. `[Convert]` is not consumed in reverse anyway (NCMAP033), making it irrelevant to `null` handling.

***

## 7. `ToEntity` Ignore List

### 7.1 Rule

Global `Ignore*ToEntity` lists (including `[MapIgnore(Direction.ToEntity)]` and globally configured `ToEntity` ignore lists) apply to reverse mapping, and behavior is **consistent across both engines**:

- Runtime reverse paths respect the list.
- Source generator reverse paths likewise respect it; matched properties are skipped and reported as **NCMAP013**.
- If filtering results in an empty set, reverse methods/extensions are *not generated*.

### 7.2 Semantics

The ignore list is the primary mechanism to block reverse mapping: DTO properties that should *not* be written back to the Entity (e.g., display-only fields, derived fields) are uniformly ignored via `ToEntity` ignore rules, preventing unwanted overwrites of Entity data.

### 7.3 Combination with `MapIgnore` Directionality

Directional ignore has three usage modes; their combination determines the fate of a property in reverse mapping:

| Annotation                                | Forward (`ToDto`) | Reverse (`ToEntity`) |
| ----------------------------------------- | ----------------- | --------------------- |
| None                                      | Mapped            | Mapped (subject to other rules) |
| `[MapIgnore(Direction.ToDto)]`          | Skipped           | Mapped                |
| `[MapIgnore(Direction.ToEntity)]`       | Mapped            | Skipped (NCMAP013)    |
| `[MapIgnore(Direction.Both)]`           | Skipped           | Skipped               |

Ignoring display fields (masked values, derived text) via `ToEntity` is the recommended practice in CRUD scenarios: needed for UI display, unnecessary for writeback.

***

## 8. Updating Existing Objects and `init-only`

The “update” overload of reverse mapping (`dto.MapToXxxEntity(entity)`) assigns values to an existing target object. When a target property is `init-only`, reassignment is impossible; the property is skipped and **NCMAP032** (Info) is reported: “`init-only` property skipped during update (writable only during creation).” This is a constraint of C# `init` accessor semantics, *not* a configuration error; the creation path (`dto.MapToXxxEntity()`) remains unaffected.

***

## 9. Frequently Asked Questions

**Why doesn’t reverse mapping reuse forward conversion methods directly?**  
Forward conversion method signatures bind to the forward direction (`TSource` = Entity type, `TField` = Entity-side field type). During reverse invocation, inputs are DTO objects/fields — type mismatches would occur. Reuse would cause type confusion, and conversions are often irreversible (e.g., formatting, masking); automatic reverse inference would fail. Explicit `[ConvertBack]` annotation is required for reverse conversion.

**How to handle masked DTOs in update forms?**  
Writing back masked values overwrites full Entity data (NCMAP034 warning). Recommendation: use masked DTOs for display, and before writeback, annotate sensitive properties with `[MapIgnore(Direction.ToEntity)]` to block them — or explicitly exclude masked fields during updates.

**Are formatted fields written back in reverse?**  
No. `[MapFormat]` is a one-way display declaration: reverse mapping skips the property (Entity retains its original value), and the compiler reports NCMAP046 (Warn) to indicate the skip. For round-trip support, add `[ConvertBack]` at the same anchor to explicitly reverse-parse (forward formatting + reverse conversion — orthogonal directions, §5.3); if round-trip is unnecessary, no action is required.

**Why aren’t reverse extension methods generated?**  
Verify `[MapFrom]` is annotated on the DTO; if `Ignore*ToEntity` filtering results in an empty set, reverse methods are not generated (NCMAP013). Also confirm the generated namespace is imported (`using NCMapper.Generated;`).

**Is reverse mapping equivalent to AutoMapper’s `ReverseMap`?**  
Directionally yes (DTO → Entity), but NCMapper’s semantics are more precise: reverse mapping does *not* auto-infer forward conversion methods (NCMAP033), masked writeback is lossy (NCMAP034), and `null` writeback clears values. When migrating, verify behavioral differences against this guide.  
***

## Quick Reference Table

| Semantic                 | Rule                                                                                                                                 | Diagnostic Code         |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ | ----------------------- |
| Direction definition     | Forward = Entity → DTO (`ToDto`); Reverse = DTO → Entity (`ToEntity`)                                                              | —                       |
| `[Convert]` (reverse)    | Not consumed by default; written along the type-conversion path                                                                      | NCMAP033 (Info)         |
| `[ConvertBack]`          | Explicitly consumed in reverse direction; static method signature binds to the reverse direction (`TSource`/`TField` = DTO side); DTO property serves as the canonical anchor, while the entity side is consumed in one of two fallback modes: (1) no-source (entire DTO passed as parameter) or (2) matching-pair fallback (DTO takes precedence) (§3.2) | NCMAP035/036/037 (Error) |
| `[Mask]` (reverse write-back) | Consumed only in forward direction (masking logic is not invoked in reverse); masked values are written back unchanged, resulting in lossy round-trip behavior | NCMAP034 (Info)         |
| `[MapFormat]` (reverse)  | Consumed only in forward direction (formatting output is lossy and culture-sensitive; reverse parsing is not attempted); **no assignment occurs in reverse**, and the entity-side value remains unchanged; round-trip requires explicit anchoring via `[ConvertBack]` (§5) | NCMAP046 (Warn)         |
| `null` write-back        | If the member chain exists, the leaf node is cleared; if the chain does not exist, the operation is a no-op                              | —                       |
| `ToEntity` ignore list   | Consistent across both engines; matched members are skipped                                                                            | NCMAP013                |
| `init-only` updates      | Skipped during update paths; allowed during creation paths                                                                           | NCMAP032 (Info)         |

***

## Related Documentation

- [Mapping Rules (Semantics)](./semantics/mapping-rules.md): `[MapIgnore]` and ignore lists, reverse-mapping contracts

- [Property-Level Formatting](../how-to/declarative/map-format.md): `[MapFormat]` channel (§5 page describing reverse-direction non-assignment behavior)

- [Masking](../how-to/infrastructure/masking.md): Usage of `[Mask]`

- [Three Principles of Mapping Configuration Ownership](semantics/mapping-config-ownership.md): “No anchor, no consumption”, compile-time diagnostics, and registration surface on the hosting side

- [15-Minute Getting Started Tutorial](../getting-started/tutorial-15min.md): First use of reverse mapping (samples 01)

- [Explanation of the Dual-Engine Architecture](./dual-engine.md): Background on dual-engine consistency for reverse semantics
