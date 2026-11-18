> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: how-to/infrastructure/global-config
Diagnostic codes involved: NCMAP001, NCMAP013, NCMAP020, NCMAP021, NCMAP022
Library version: v1.0.6
Last updated: 2026-09-30
---

# Global Map Configuration

## Overview

`[assembly: MapDefaults]` is an assembly-level global configuration: declared once, it automatically applies to **all** mappings. It enables unified naming conventions, bulk ignoring of auditing/versioning fields, and standardized prefix/suffix handling—replacing repetitive per-property attributes like `[MapProperty]` or `[MapIgnore]`.

**Directional semantics**: Ignore lists are split into two groups — “bidirectional” and “ToEntity-direction-only”. A property is ignored in the ToEntity direction if it matches *either* the bidirectional list *or* the ToEntity-specific list; in the ToDto direction, only the bidirectional list applies (no ToDto-specific ignore list).  
- “ToDto” means the mapping direction where the *target type* is annotated with `[MapFrom]` (e.g., Entity → DTO).  
- “ToEntity” means the reverse direction (e.g., DTO → Entity), where the target type has *no* `[MapFrom]` attribute.

```csharp
[assembly: MapDefaults(
    NamingStrategy = NamingStrategy.SnakeCase,     // Naming strategy
    Prefixes       = new[] { "Tbl" },              // Strip prefixes from source property names
    Postfixes      = new[] { "Entity" },           // Strip suffixes from source property names
    IgnoreNames    = new[] { "RowVersion", "CreatedAt" }, // Bidirectional ignore: exact name match
    IgnoreTypes    = new[] { typeof(byte[]) },      // Bidirectional ignore: full type name match
    IgnoreNamePrefixes = new[] { "Internal" },      // Bidirectional ignore: name prefix match
    IgnoreNameSuffixes = new[] { "Stamp" },         // Bidirectional ignore: name suffix match
    IgnoreNamesToEntity = new[] { "ComputedField" },// ToEntity-direction-only ignore: exact name match
    IgnoreNamePrefixesToEntity = new[] { "Db" })    // ToEntity-direction-only ignore: name prefix match
```

Precise semantics of each parameter are detailed in “Parameter Reference”; each list has a hard limit of **8 items**, and exceeding this limit causes that list to be downgraded to “unconfigured” and triggers diagnostic code NCMAP020.

## Quick Start

Under the `SnakeCase` naming strategy, snake_case source fields automatically match PascalCase target properties—no per-property annotations required:

```csharp
public class UserEntity
{
    public string user_name { get; set; } = "";   // ← Automatically matches UserName
}

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public string UserName { get; set; } = "";    // Zero-configuration
}
```

Prefix/suffix stripping automatically resolves naming mismatches (**source-side only**):

```csharp
[assembly: MapDefaults(
    Prefixes  = new[] { "Tbl" },
    Postfixes = new[] { "Entity" })]

public class TblUserEntity
{
    public int UserId { get; set; }               // Strips "Tbl"/"Entity" → matches UserId
}

public class UserDto
{
    public int UserId { get; set; }               // Zero-configuration
}
```

## How It Works: The Matching Pipeline

Before diving into individual parameters, understand the full matching flow from source property → target property (exactly mirrored by the source implementation `NameCandidateResolver`):

```
① Source property generates a candidate name set:
    Original name + stripped variants (if Prefixes/Postfixes match — source-side only)
② Each candidate name is normalized into a comparison key using NamingStrategy:
    Default/CaseSensitive → original name;
    Snake/UpperSnake → remove underscores + uppercase all letters
③ Comparison keys populate the "source dictionary" (first-come-first-served on key collision):
    First declaration (by source property order) wins; subsequent same-key candidates are discarded
④ Target property name is normalized (same strategy) and looked up in the source dictionary → match found
⑤ Ignore evaluation (fail-closed): if *either* the target or source property matches any ignore rule → discard this mapping pair
```

Two commonly misunderstood details:

- **`SnakeCase` matching is broader than “snake_case ↔ PascalCase”**: Normalization is “remove underscores + uppercase”, so `user_name`, `USER_NAME`, `UserName`, and `username` all normalize to `USERNAME`. Thus, under `SnakeCase`, *any combination of case and underscores* matches as long as removing underscores yields identical text. (`Default` only ignores case—not underscores.)
- **Flattened decomposition and segment matching are strategy-agnostic**: Automatic flattening like `OrderItemName → Order.Item.Name` works identically across all four strategies (flattening logic is fixed `OrdinalIgnoreCase`; consistent across all four engines: source generator / runtime `Mapper.Map` / ORM projection / `Compose`). However, **flattening activation is affected by strategy-based name collisions**: Under `SnakeCase`, if a single-segment source property `customer_name` exists, `CustomerName` will match it directly and *not* trigger flattening. Once a flattened mapping is selected, it **does not re-enter strategy-based matching** (source code `ReplaceWithConfigAwareMatch` skips `IsFlattened` candidates).
- **Global ignore lists × flattened/segmented paths (leaf-node evaluation)**: For both flattening (`OrderItemName → Order.Item.Name`) and explicit dot-path attributes (`[MapProperty(typeof(Entity), "Order.Item.Name")]`), ignore evaluation operates at the **leaf node** (i.e., the final segment of the resolved path)—a leaf node matching any ignore rule causes the entire property to be skipped (consistent across all four engines: source generator value model pipeline / runtime `Mapper.Map` / Dapper / ORM projection). **Intermediate navigation nodes are *not* evaluated** (e.g., if `AuthorName`’s intermediate node `Author` matches an ignore rule, it has no effect; only leaf node `Name` matching matters — consistent with source generator value model pipeline). On the projection side, this evaluation **does not trigger fallback**: during projection build time, the property is simply omitted (same behavior as in-memory mapping—not a data-loss path; see [ORM Projection Design](../../explanation/orm-projection-design.md) §11.1 for details).
- **Explicit dot-path support**: `[MapProperty(typeof(Entity), "Order.Item.Name")]` is fully aligned across all four engines (source generator / runtime `Mapper.Map` / ORM projection / `Compose(SourceGen)`), sharing multi-segment path semantics with inferred flattening (`SourcePath` canonical form + `PathOrigin` distinguishes origin). Failures are **explicitly observable**: source generator emits NCMAP001/005 with failing segment name and declaring type; runtime throws `MapConfigurationException` (**explicit failure**, never silent skip); projection omits + logs trace. `[Mask]`/`[Convert]` × dot-path are **fully consumed** (path-mapped masking and conversion apply; projection consumes via two-step fallback chain).
- **`[MapProperty].FromField` always uses literal matching**: Explicitly specified field names are *never* transformed by naming strategies (they use `OrdinalIgnoreCase` literal comparison); strategy-based matching never overwrites them.

## Parameter Reference

### 1. `NamingStrategy` — Naming Strategy

| Item | Value |
|------|-------|
| Type | `NamingStrategy` enum |
| Default | `Default` |
| Scope | Global (applies to all mappings) |

| Enum Value | Matching Rule | Normalized Key Example |
|------------|----------------|--------------------------|
| `Default` (default) | Case-insensitive exact name match (current default behavior, zero breaking change) | `username` ↔ `UserName` |
| `CaseSensitive` | Case-sensitive exact match (disables case-insensitivity) | `UserName` ↔ `UserName`; `username` ✗ |
| `SnakeCase` | Remove underscores + uppercase, then exact compare | `user_name` ↔ `UserName` ↔ `USER_NAME` ↔ `username` |
| `UpperSnakeCase` | Same normalization algorithm as `SnakeCase` (semantically intended for all-uppercase snake_case scenarios) | `USER_NAME` ↔ `UserName` |

**Boundaries**:

- All four strategies preserve automatic flattening; `CaseSensitive` requires *exact character-by-character* match.
- Strategies affect *only unannotated* mappings; `[MapProperty].FromField` always uses literal matching (see above).
- New enum values must be handled in `switch` statements with a `default` branch.

### 2. `Prefixes` — Source Property Name Prefix Stripping

| Item | Value |
|------|-------|
| Type | `string[]`, nullable (`null` = unconfigured, inherits from lower layer) |
| Limit | 8 items (exceeding → NCMAP020, this list downgraded to unconfigured) |
| Scope | **Source-side only** (no stripping applied to target side) |

**Matching rule**: A source property name matches `StartsWith(prefix, OrdinalIgnoreCase)` *and* the resulting stripped name is non-empty (`name.Length > prefix.Length`) → generates a “stripped variant” for matching. Stripping is case-insensitive (`tblUser` matches prefix `"Tbl"`).

**Variant generation order** (`GetStripVariants`):  
① Original name →  
② Strip each matched prefix →  
③ Strip each matched postfix →  
④ Strip prefix *then* postfix (combinatorial).  
All variants are normalized (per `NamingStrategy`) before entering the source dictionary.

**Example**:

```csharp
[assembly: MapDefaults(Prefixes = new[] { "Tbl", "Get" })]

public class TblUserEntity
{
    public int TblUserId { get; set; }     // Strip "Tbl" → "UserId", matches target "UserId"
    public string GetName { get; set; }    // Strip "Get" → "Name", matches target "Name"
}

public class UserDto { public int UserId { get; set; } public string Name { get; set; } }
```

**Boundaries (critical)**:

- Stripping affects *only the lookup key*, not the property itself.
- **First-match-wins on key collision**: The source dictionary is populated in source property declaration order. The first property (including its stripped variants) to claim a key wins; later properties with the same key are discarded. Example: If source declares `GetName` *before* `Name`, and `Prefixes = ["Get"]`, then `GetName`’s stripped variant `Name` enters the dictionary first, causing the plain `Name` property’s original name to collide and be discarded. Target `Name` binds to `GetName`. Declaration order directly impacts binding outcomes.
- Stripping and naming strategy combine: Stripped variants are still normalized per strategy (e.g., under `SnakeCase`, `Tbl_user_name` stripped of `"Tbl"` → `user_name` → normalized to match `UserName`).

### 3. `Postfixes` — Source Property Name Suffix Stripping

| Item | Value |
|------|-------|
| Type | `string[]`, nullable (`null` = unconfigured) |
| Limit | 8 items |
| Scope | **Source-side only** |

**Matching rule**: A source property name matches `EndsWith(postfix, OrdinalIgnoreCase)` *and* the resulting stripped name is non-empty → generates a “stripped variant”. Variant generation, key collision rules, and strategy interaction are *identical* to `Prefixes`.

**Example**:

```csharp
[assembly: MapDefaults(Postfixes = new[] { "Entity" })]

public class UserEntity
{
    public int UserIdEntity { get; set; }  // Strip "Entity" → "UserId", matches target "UserId"
}

public class UserDto { public int UserId { get; set; } }
```

### 4. `IgnoreNames` — Bidirectional Exact-Name Ignore

| Item | Value |
|------|-------|
| Type | `string[]`, nullable |
| Limit | 8 items |
| Scope | **Bidirectional** (applies to both ToDto and ToEntity directions) |

**Matching rule**: Property name matches `OrdinalIgnoreCase` exactly — **case-insensitive** (`rowversion`, `ROWVERSION` both match `"RowVersion"`). Match → ignore (fail-closed): target property remains unset; source property excluded from candidate set.

**Example**:

```csharp
[assembly: MapDefaults(IgnoreNames = new[] { "RowVersion", "CreatedAt" })]

public class UserEntity
{
    public string RowVersion { get; set; }  // Globally ignored → DTO has no such field
    public DateTime CreatedAt { get; set; } // Same
}
```

### 5. `IgnoreTypes` — Bidirectional Type-Based Ignore

| Item | Value |
|------|-------|
| Type | `Type[]`, nullable |
| Limit | 8 items |
| Scope | **Bidirectional** |
| Matching key | `Type.FullName` (exact `Ordinal` comparison) |

**Matching rule**: Exact `Type.FullName` match → ignore. Key points:

- **Open generics do not match**: Listing `typeof(List<>)` will *not* match `List<string>`; you must specify closed types like `typeof(List<string>)`. MVP makes *no guarantee* about matching full names containing backticks for generic arity (e.g., `List`1`); MSBuild channel directly downgrades to NCMAP020; attribute-channel open generic types generate backticked full names that also fail to match.
- Array types: Specify `typeof(byte[])`; matching key is `System.Byte[]` (consistent between SourceGen and runtime `Type.FullName`).
- Nested types: Full name uses `+` separator (e.g., `Outer+Inner`).

**Typical use case**: Type-based ignore suits “entire categories of system fields excluded from mapping”—e.g., ORM concurrency token columns (SQL Server `rowversion`/`timestamp` mapped to `byte[]`), platform infrastructure types.

**Example**:

```csharp
[assembly: MapDefaults(IgnoreTypes = new[] { typeof(byte[]) })]  // All byte[] properties globally ignored

public class UserEntity
{
    public byte[] RowVersion { get; set; }    // SQL Server rowversion/timestamp (ORM concurrency token) → ignored
    public byte[] RowStamp { get; set; }      // Same
}
```

**⚠️ Warning: Blunt instrument** — Type-based ignore has the broadest impact: *all* properties of that type are excluded. If business data (e.g., images, file content) is also `byte[]`, it will be silently ignored. Prefer `IgnoreNames` for precise targeting (e.g., only `RowVersion`, `RowStamp`) or use `OverrideIgnore = true` on target properties you wish to retain.

### 6. `IgnoreNamePrefixes` — Bidirectional Name-Prefix Ignore

| Item | Value |
|------|-------|
| Type | `string[]`, nullable |
| Limit | 8 items |
| Scope | **Bidirectional** |

**Matching rule**: Property name matches `StartsWith(prefix, OrdinalIgnoreCase)` → ignore (case-insensitive).

**Key distinction vs. `Prefixes`**: `Prefixes` strips the prefix and *continues matching*; `IgnoreNamePrefixes` matches the prefix and *immediately discards the mapping*.

**Example**:

```csharp
[assembly: MapDefaults(IgnoreNamePrefixes = new[] { "Internal" })]

public class UserEntity
{
    public string InternalRemark { get; set; }  // "Internal*" → ignored
    public string InternalState { get; set; }   // Same
}
```

### 7. `IgnoreNameSuffixes` — Bidirectional Property Name Suffix-Based Ignoring

| Item | Value |
|------|-------|
| Type | `string[]`, nullable |
| Maximum items | 8 |
| Scope | **Bidirectional** |

**Matching rule**: A property name is ignored if it ends with any of the specified suffixes (case-insensitive ordinal comparison via `EndsWith(…, OrdinalIgnoreCase)`). The distinction from `Postfixes` (see item #6) is the same: one “strips and continues matching”, while the other “abandons immediately”.

**Example**:

```csharp
[assembly: MapDefaults(IgnoreNameSuffixes = new[] { "Stamp" })]

public class UserEntity
{
    public string RowStamp { get; set; }     // *Stamp → ignored
    public string TimeStamp { get; set; }    // same as above
}
```

### 8. `IgnoreNamesToEntity` — ToEntity-Direction Name-Based Ignoring

| Item | Value |
|------|-------|
| Type | `string[]`, nullable |
| Maximum items | 8 |
| Scope | **ToEntity direction only** (DTO → Entity; target type has no `[MapFrom]`) |

**Matching rule**: Same as `IgnoreNames` (case-insensitive ordinal exact match), but applies **only when `isToDto = false`**; merged (union) with bidirectional ignore lists.

**When it takes effect**: The ToEntity direction refers to reverse mapping (e.g., `Mapper.Map<UserDto, UserEntity>(dto)`, where the target `UserEntity` has no `[MapFrom]`). This list applies to **both source generator reverse paths and runtime paths**:

- **Source Generator Reverse Path** (F1-aligned): During reverse snapshot construction, the global ignore list is applied with `isToDto = false`. The same filtered snapshot is used by all three consumers — reverse method generation, module initializer registration, and reverse extension generation. Properties matching this list are skipped and reported as diagnostic `NCMAP013`. If the filtered list becomes empty, no reverse methods or extensions are generated.
- **Runtime Path**: `PropertyIgnoreChecker` infers direction based on whether the target type is annotated with `[MapFrom]`. Absence of `[MapFrom]` implies ToEntity direction, and the `*ToEntity` lists take effect.

**Example**:

```csharp
[assembly: MapDefaults(IgnoreNamesToEntity = new[] { "ComputedField" })]

public class UserDto
{
    public string ComputedField { get; set; }   // ignored during reverse mapping back to entity
}
```

### 9. `IgnoreTypesToEntity` — ToEntity-Direction Type-Based Ignoring

| Item | Value |
|------|-------|
| Type | `Type[]`, nullable |
| Maximum items | 8 |
| Scope | **ToEntity direction only** |

**Matching rule**: Same as `IgnoreTypes` (`Type.FullName` ordinal exact match), but applies **only when `isToDto = false`**. Typical use case: transient large-object fields in DTOs (e.g., `byte[]` attachments) that should not be written back to entities.

### 10. `IgnoreNamePrefixesToEntity` — ToEntity-Direction Name Prefix-Based Ignoring

| Item | Value |
|------|-------|
| Type | `string[]`, nullable |
| Maximum items | 8 |
| Scope | **ToEntity direction only** |

**Matching rule**: Same as `IgnoreNamePrefixes` (case-insensitive ordinal prefix match), but applies **only when `isToDto = false`**.

### 11. `IgnoreNameSuffixesToEntity` — ToEntity-Direction Name Suffix-Based Ignoring

| Item | Value |
|------|-------|
| Type | `string[]`, nullable |
| Maximum items | 8 |
| Scope | **ToEntity direction only** |

**Matching rule**: Same as `IgnoreNameSuffixes` (case-insensitive ordinal suffix match), but applies **only when `isToDto = false`**.

> **Note on `*ToEntity` Lists**: SourceGen emits ToDto-direction mappings only for types annotated with `[MapFrom]` (source code hardcodes `isToDto = true`). Therefore, `*ToEntity` lists do **not participate in the source generation path**, but serve **runtime reverse mapping** exclusively. If your scenario never uses runtime reverse mapping, `*ToEntity` lists have no effect.

## Override Mechanism (`OverrideIgnore`)

Global ignoring is fail-closed (a match results in immediate ignoring), but individual target properties can explicitly declare **per-mapping-pair overrides**:

```csharp
[MapProperty(OverrideIgnore = true)]        // Any one of these suffices to override the entire pair
// or [Convert(OverrideIgnore = true)] / [Mask(OverrideIgnore = true)] / [ConvertBack(OverrideIgnore = true)]
public string RowVersion { get; set; }      // matches global ignore, but explicitly restored
```

**Rules**:

- Overrides apply to five attributes: `[MapProperty]`, `[Convert]`, `[Mask]`, `[ConvertBack]`, and `[MapFormat]`. Any one of them with `OverrideIgnore = true` bypasses **the entire mapping pair**, skipping global ignore checks on both source and target sides.
- Override evaluation occurs on the **target property**.
- `[MapIgnore]` does **not support overriding**: Property-level explicit ignore always takes precedence. Declaring both `[MapIgnore]` and `OverrideIgnore = true` on the same property is a contradictory configuration, causing the source generator to emit diagnostic **NCMAP022** (Warning).
- If an explicit attribute (one of the five) matches a global ignore rule but omits `OverrideIgnore = true`, the source generator emits diagnostic **NCMAP021** (Warning + bidirectional guidance: either remove the attribute to follow global ignore, or add `OverrideIgnore = true` to restore the mapping).

**Boundary with the Three Principles (Out-of-Class Objects)**: Global ignore lists (`IgnoreNames`, `IgnoreTypes`, `IgnoreNamePrefixes`, `IgnoreNameSuffixes`) are **assembly-level global configurations**, not tied to any class nor declared as mapping annotations. Thus, they are **not constrained** by the “attribute anchoring to class” rule — even unanchored types may match global ignores. Other out-of-class objects include:
- Nested name-based recursion (baseline behavior for name matching),
- Class-level `[MapTree]` (tree construction definition, activated by `ToTreeList` calls),
- `[MapSubclass]` (missing anchor is flagged by dedicated diagnostic `NCMAP_F49`).  
A plain-language explanation of the Three Principles appears in [Mapping Configuration Ownership — Three Principles](../../explanation/semantics/mapping-config-ownership.md).

## Three-Tier Priority Chain

Non-null entries override top-down, with finer-grained layers taking precedence:

```
Property-level attributes ([MapProperty] / [MapIgnore] / [Convert] / [Mask])
      >  [assembly: MapDefaults]
      >  MSBuild properties (e.g., `<NCMapperNamingStrategy>`, delivered via `buildTransitive` targets)
      >  Built-in defaults (Default strategy + empty lists)
```

MSBuild channel (applies only to compilation units participating in source generation; list items are comma-separated `,`, **not semicolon** — semicolons are line-end comments in editorconfig syntax and would truncate values; full list of 11 properties below):

```xml
<PropertyGroup>
  <NCMapperNamingStrategy>SnakeCase</NCMapperNamingStrategy>
  <NCMapperIgnoreNames>RowVersion,CreatedAt</NCMapperIgnoreNames>
  <NCMapperIgnoreNamesToEntity>DbState</NCMapperIgnoreNamesToEntity>
</PropertyGroup>
```

Complete whitelist of MSBuild properties (`CompilerVisibleProperty` in `src/NCMapper/NCMapper.targets`):

```
NCMapperNamingStrategy
NCMapperPrefixes                NCMapperPostfixes
NCMapperIgnoreTypes             NCMapperIgnoreNames
NCMapperIgnoreNamePrefixes      NCMapperIgnoreNameSuffixes
NCMapperIgnoreTypesToEntity     NCMapperIgnoreNamesToEntity
NCMapperIgnoreNamePrefixesToEntity  NCMapperIgnoreNameSuffixesToEntity
```

> Runtime fallback paths (for assemblies without `[MapFrom]`) do **not read MSBuild properties**, which are visible only to source-generation compilation units.

## Freeze Semantics (Two-Phase)

1. **Registration**: At compile time, the source generator reads `[assembly: MapDefaults]` and generates `Mapper.RegisterGeneratedConfig` snapshot registration code.
2. **Freezing**: Triggered by the first mapping operation or `Validate()` call — after freezing, configuration becomes immutable (no modification API exists).

When multiple assemblies declare conflicting configurations, the first freeze throws `MapConfigurationException`, listing conflicting assembly names (fail-fast). Identical configurations are idempotently merged (differences in list order do not trigger conflict detection). `Mapper.GetGlobalConfig()` returns a read-only view (immutable; intended for debugging and assertions).

**Fallback Channel**: For assemblies lacking source-generated code (i.e., containing no `[MapFrom]` types), `Mapper` falls back to reflection-based reading of `[assembly: MapDefaults]`.

## Reading and Debugging

[!code-csharp[Reading global config read-only view](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal)]

Useful for debugging, logging, and test assertions; underlying list arrays are never exposed.

## Diagnostic Rules

| Diagnostic | Severity | Trigger Condition |
|------------|----------|-------------------|
| NCMAP020 | Warning | Invalid configuration value (e.g., enum out of bounds, list exceeding 8 items, invalid fully qualified type name) → that item is downgraded to “unconfigured”, without blocking compilation |
| NCMAP021 | Warning | An explicit attribute (`[MapProperty]`/`[Convert]`/`[ConvertBack]`/`[Mask]`/`[MapFormat]`) matches a global ignore rule but lacks `OverrideIgnore` → bidirectional resolution guidance |
| NCMAP022 | Warning | `[MapIgnore]` and `OverrideIgnore = true` coexist on the same property (contradictory configuration) |

## Runnable Demo (Scenario 606)

[!code-csharp[Naming Strategy and Ignore Lists (SnakeCase Matching + Global Ignore)](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_StrategyAndIgnore)]

[!code-csharp[Fluent Override Gate Interacting with Naming Strategy (overrideIgnore restores globally ignored field)](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_FluentInteract)]

## Migration Comparison

- **Before migration**: Per-property `[MapProperty(FromField="user_name")]` + per-property `[MapIgnore]`.
- **After migration**: `[assembly: MapDefaults(NamingStrategy = SnakeCase, IgnoreNames = [...])]`, zero annotations on DTOs.

Runnable examples reside in `samples/NCMapper.Samples/` (Scenario 606, verifying consistency across two mapping paths).

Core example source (single-source-of-truth):

[!code-csharp[Global config in effect (MapGlobal)](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal)]

[!code-csharp[Global config in effect (MapGlobal) · RunSummary](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal_RunSummary)]

[!code-csharp[Global config in effect (MapGlobal) · Run](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal_Run)]
