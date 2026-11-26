> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: declarative/advanced
Diagnostic codes involved: NCMAP020-022, NCMAP041, NCMAP_F11, NCMAP_F40-F45, NCMAP_F46-F50, NCMAP_F51-F54  
Library version: Unreleased (to be released with the next version)  
Last updated: 2026-09-30  
---

# Advanced Attribute Forms

> This is a **self-contained** article covering advanced topics of the *Attribute Channel (Second System)*: conditional guards, polymorphic subtypes, and the complete semantics of global default configuration — all fully readable on this page without needing to navigate elsewhere. For basic usage, see [basics](basics.md); for a side-by-side comparison of the two channels, see [api-comparison](../../reference/api-comparison.md); for the full attribute reference, see [attributes-index](../../reference/attributes-index.md). Fluent-channel equivalents are documented at [fluent/terminals](../fluent/terminals.md) (for comparison and extension).

## 1. `[MapWhen]`: Property-Level Conditional Guard

The `[MapWhen(nameof(Predicate))]` attribute, applied to a target property, causes mapping assignment for that property to execute only when the predicate evaluates to `true` ("true → map", polarity reflected in the name). The declaration can anchor onto two kinds of mapping targets: properties matched automatically by name, and properties explicitly annotated with `[MapProperty]`.

> **Class-level anchoring prerequisite**: All property-level annotations in this article (`[MapWhen]`, `[MapSubclass]`, `[MapProperty]`, etc.) require that the containing class has a *mapping declaration anchor* — either a `[MapFrom]` attribute, or a `For<S, T>()` declaration in a Fluent configuration class for the type pair involving this class. Without such an anchor, property-level annotations are ignored (compile-time error `NCMAP019`). See [basics §2 Class-level anchoring prerequisite](basics.md).

### Predicate Resolution Rules

- **Resolution scope**: The predicate method is resolved *by name* on the **declaring type of the annotated property**, within a single-type self-contained context. Scope is `DeclaredOnly` (includes all parts of a `partial` type). Setting `Inherited = true` allows the declaration to be inherited by DTO subclasses.
- **Visibility gate**: The method must be `public` or `internal static` (visible within the same assembly). `private`/`protected` methods are inaccessible across types and thus cause resolution failure.
- **Signature gate**: Must accept exactly one parameter, return `bool`, and have no `ref`/`out`/`in` parameters.
- **Input-parameter shape inference** (automatically determined at extraction time; no explicit specification required): If the single parameter’s type matches the `[MapFrom]` source entity type of the DTO, it is treated as a *T2 whole-source* (multi-field evaluation); otherwise, it is treated as a *T1 field value* (the parameter type must be implicitly compatible with the source field type). Inference rules match those used by the `[Convert]` family.

### Skip-Value Behavior

When the condition evaluates to `false`, assignment to the property is skipped. The resulting value depends on the mapping path:

| Mapping Path | Result When Condition Is `false` |
| ------------ | -------------------------------- |
| New instance (`Mapper.Map<TSource, TTarget>(src)`) | Retains the target property’s initializer value; if none exists, defaults to `default` |
| Update existing instance (`Mapper.Map(src, dst)`) | Preserves the existing value in `dst` |
| Constructor-mapped target | Explicitly assigned `default` |

### Composition and Constraints

- **Execution order**: Condition → Conversion → Assignment. The guard observes the *pre-conversion* source field value. Combining `[MapProperty]` and `[MapWhen]` on the same anchor is valid.
- **Single condition**: `AllowMultiple = false` and `Inherited = true` may coexist, but language rules permit only one `[MapWhen]` per property. Combine multiple criteria inside a single predicate.
- **Negation**: Use predicate negation (e.g., `!HasContent(...)`) for negative filtering; there is no `IgnoreWhen`-style attribute.
- **Reverse mapping**: Guards are not consumed during reverse mapping (they apply only to forward Entity→DTO mapping).

### Diagnostics

| Code | Trigger Condition | Resolution |
| ---- | ----------------- | ---------- |
| `NCMAP041` | Predicate resolution failure: method does not exist, ambiguous overload, non-static (including lambdas and local functions), insufficient visibility, invalid signature, or input parameter type is neither the source field type nor the source entity type | Declare the predicate on the same type as the annotated property, and ensure its signature conforms strictly to either the T1 or T2 pattern |

### Projection Behavior

`[MapWhen]` implements `IProjectionPolicy` with `RequiresDowngrade = true`: The guard predicate is a CLR delegate path, which cannot be translated to SQL. ORM projections always downgrade to in-memory mapping (category `MapWhen`, consistent with `[Mask]` and `[Convert]`). See [ORM Downgrade Mechanism](../../explanation/orm-degradation.md) for details.

### Example

Predicate declared on the DTO itself: `public static bool HasContent(string)` matches the T1 field-value shape (`dotnet run --scenario 105`):

[!code-csharp[Attribute-side `\[MapWhen\]` property-level guard](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto)]

[!code-csharp[Attribute-side `\[MapWhen\]` property-level guard · Id](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Id)]

[!code-csharp[Attribute-side `\[MapWhen\]` property-level guard · Name](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Name)]

[!code-csharp[Attribute-side `\[MapWhen\]` property-level guard · Score](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Score)]

[!code-csharp[Attribute-side `\[MapWhen\]` property-level guard · HasContent](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_HasContent)]

## 2. `[MapSubclass]`: Polymorphic Subtype Registration

`[MapSubclass(typeof(DerivedSource), typeof(DerivedTarget))]` is applied to a base-class DTO annotated with `[MapFrom]`, registering a subtype mapping pair for the base-class mapping. At runtime, the base-class mapping routes to the appropriate subclass-specific mapping body based on the actual runtime type of the source instance (via compile-time `isinst` dispatch chain). This supports polymorphic lists and fields where base entities map to base DTOs, and derived entities map to derived DTOs (including derived-specific fields).

### Declaration Form

- Class-level annotation, with `Inherited = false` (derived DTOs do *not* inherit subclass registrations from their base) and `AllowMultiple = true` (multiple registrations allowed, each defining one subtype pair).
- The annotated class must also carry `[MapFrom(typeof(BaseSource))]` to anchor the base mapping pair. Missing this anchor results in an orphaned registration and triggers `NCMAP_F49`.

### Dispatch Semantics

Applies uniformly across the four families: `Map`, `_Update`, `MapList`, and `MapListInto`:

- **Assignable match**: A match occurs when `src is DerivedSource`; unregistered deeper derivations fall back to the nearest registered ancestor level.
- **Most-derived-first**: When multiple registrations match simultaneously, arbitration uses lexicographic ordering of `(source inheritance distance, target inheritance distance)`. Partially overlapping registrations (same source, different levels; same source, different targets) are permitted and do not trigger errors.
- **Base fallback**: If no registered subclass matches (including third-party unregistered derivations), the base mapping body is used, and subclass-specific fields retain their defaults.
- **Dual-instance match**: For `_Update` and `MapListInto`, both `src` and `dst` must match the same registered subclass to route to the subclass body. If `dst`’s runtime type is not a registered subclass target, routing falls back to the base body, and subclass-specific fields remain unwritten.
- **Reverse mapping**: Reverse mappings and direct reverse overloads are automatically generated via the reverse-registration pipeline (registrations are *not* one-way). Base-class reverse mappings do *not* participate in dispatch (reverse targets are pre-existing entity instances supplied by the caller). This asymmetry is surfaced via `NCMAP_F50` (Info level).

### Inheritance Constraints and Boundaries

- **Cross-channel constraint alignment**: Fluent-side generic constraints (`where TSrcDerived : TSource`) are enforced by the compiler at call sites (CS0311/CS0314); attribute-side `typeof` arguments lack compile-time constraints — inheritance violations are reported at extraction time as `NCMAP_F46`.
- `TTargetDerived == BaseTarget` (polymorphic source, converged target) is a valid use case.
- `TSrcDerived == BaseSource` (registering the base class itself, causing infinite self-referential dispatch) or exact duplicate registrations trigger `NCMAP_F47`.
- If the subclass target lacks a public, mappable surface or fails construction constraints, `NCMAP_F48` is raised.

### Diagnostics

| Code | Channel | Trigger Condition | Resolution |
| ---- | ------- | ----------------- | ---------- |
| `NCMAP_F46` | Attribute-only | `typeof` argument violates inheritance constraints (i.e., `DerivedSource` must derive from `BaseSource`, and `DerivedTarget` must derive from `BaseTarget`) | Correct the `typeof` arguments. Fluent-side equivalents are caught by CS0311/CS0314 at call sites; no diagnostic code is emitted. |
| `NCMAP_F47` | Universal | Duplicate registration of identical subtype pairs (first occurrence retained), or registration of the base class itself | Remove duplicates or the base-class self-registration. |
| `NCMAP_F48` | Universal | Subclass target lacks a public, mappable surface or fails construction constraints | Ensure the subclass target exposes public, mappable properties and supports construction. |
| `NCMAP_F49` | Attribute-only | Annotated class lacks `[MapFrom]` anchor — base mapping pair does not exist (orphaned registration) | Add `[MapFrom(typeof(BaseSource))]`, or switch to the Fluent channel. |
| `NCMAP_F50` | Universal (Info) | Silent semantic aspects: reverse asymmetry (base reverse mapping is generated but never dispatched), or dual-instance fallback to base body | Expected behavior; no action needed. To write subclass fields, verify `dst`’s runtime type. |

### Projection Behavior

`[MapSubclass]` implements `IProjectionPolicy` with `RequiresDowngrade = true`: The `isinst` dispatch chain cannot be translated to SQL. ORM projections always downgrade (category `SubclassDispatch`, consistent with `[Mask]`, `[Convert]`, and `[MapWhen]`).

### Dual-Channel Alignment

Two distinct perspectives — keep them separate:

- **Functional symmetry**: `[MapSubclass]` and Fluent `.MapSubclass<TSrcDerived, TTargetDerived>()` are semantically equivalent, sharing identical dispatch logic.
- **Diagnostic asymmetry**: `NCMAP_F46` and `NCMAP_F49` are attribute-only diagnostics. Fluent-side equivalents are intercepted by the compiler via generic constraints (CS0311/CS0314) at call sites — unreachable by the generator. Users should *not* expect these codes in Fluent scenarios.

### Example

Attribute-side multi-instance registration on a base-class DTO (`dotnet run --scenario 107`):

[!code-csharp[Fluent-level registration vs. attribute-side multi-instance registration](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto)]

[!code-csharp[Fluent-level registration vs. attribute-side multi-instance registration · ScAttrAnimalDto_Name](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Name)]

[!code-csharp[Fluent-level registration vs. attribute-side multi-instance registration · ScAttrAnimalDto_Weight](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Weight)]

[!code-csharp[Fluent-level registration vs. attribute-side multi-instance registration · ScAttrDogDto](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrDogDto)]

[!code-csharp[Fluent-level registration vs. attribute-side multi-instance registration · ScAttrDogDto_Breed](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrDogDto_Breed)]

[!code-csharp[Fluent-level registration vs. attribute-side multi-instance registration · ScAttrCatDto](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrCatDto)]

[!code-csharp[Fluent-level registration vs. attribute-side multi-instance registration · ScAttrCatDto_Indoor](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrCatDto_Indoor)]

[!code-csharp[Fluent-level registration vs. attribute-side multi-instance registration · SubclassFluentMapper](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_SubclassFluentMapper)]

[!code-csharp[Fluent-level registration vs. attribute-side multi-instance registration · SubclassFluentMapper_Configure](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_SubclassFluentMapper_Configure)]

## 3. `[assembly: MapDefaults]`: Global Default Configuration

Assembly-level `[assembly: MapDefaults(...)]` declares configuration once, applying it to **all** mappings within that assembly: unified naming conventions, bulk ignoring of audit/version fields, standardized source property prefix/suffix stripping — replacing repetitive per-property `[MapProperty]`/`[MapIgnore]` annotations.

### Declaration Form

Example directly synced from samples (`NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs`, scenario 606):

```csharp
[assembly: MapDefaults(
    NamingStrategy = NamingStrategy.SnakeCase,
    IgnoreNames = new[] { "RowVersion" })]
```

Applicable only at the assembly level, with `AllowMultiple = false` (only one declaration per assembly allowed).

### Parameter Surface (All 11 Parameters)

| Parameter | Type | Scope | Semantics |
| --------- | ---- | ----- | --------- |
| `NamingStrategy` | `NamingStrategy` enum | Global | Naming strategy; default is `Default` |
| `Prefixes` | `string[]` | Source only | Strips prefixes from source property names before matching |
| `Postfixes` | `string[]` | Source only | Strips postfixes from source property names before matching |
| `IgnoreNames` | `string[]` | Bidirectional | Exact property-name match triggers ignore |
| `IgnoreTypes` | `Type[]` | Bidirectional | Exact `Type.FullName` match on property type triggers ignore |
| `IgnoreNamePrefixes` | `string[]` | Bidirectional | Property-name prefix match triggers ignore |
| `IgnoreNameSuffixes` | `string[]` | Bidirectional | Property-name suffix match triggers ignore |
| `IgnoreNamesToEntity` | `string[]` | To-Entity only | Additional name-based ignore for reverse mapping |
| `IgnoreTypesToEntity` | `Type[]` | To-Entity only | Additional type-based ignore for reverse mapping |
| `IgnoreNamePrefixesToEntity` | `string[]` | To-Entity only | Additional prefix-based ignore for reverse mapping |
| `IgnoreNameSuffixesToEntity` | `string[]` | To-Entity only | Additional suffix-based ignore for reverse mapping |

Lists exceeding eight items are downgraded to “unspecified” and trigger `NCMAP020`. Each parameter is individually defined with code examples in [global-config § Parameter Details](../infrastructure/global-config.md) (deep-dive page; this page already presents the full parameter surface and rules — the link serves only for extension, not semantic delegation).

### Naming Strategies

| Enum Value | Matching Rule |
| ---------- | ------------- |
| `Default` | Case-insensitive exact name match (zero-change default behavior) |
| `CaseSensitive` | Case-sensitive exact match |
| `SnakeCase` | Normalized comparison: remove underscores + uppercase remaining letters |
| `UpperSnakeCase` | Normalization algorithm identical to `SnakeCase`, semantically intended for all-uppercase snake-case scenarios |

Normalization semantics: Both `SnakeCase` and `UpperSnakeCase` normalize via “remove underscores + uppercase”, so `user_name`, `USER_NAME`, `UserName`, and `username` all normalize to the same key. `Default` ignores only case, *not* underscores. Strategies affect only *unannotated* matches; `[MapProperty].FromField` always performs literal matching (`OrdinalIgnoreCase`) and is unaffected by strategy.

### Matching and Overriding Rules

- **Directional Semantics**: The `ToEntity` direction ignores ⟺ (bidirectional list hit) ∨ (ToEntity list hit); the `ToDto` direction only honors bidirectional lists. `"ToDto"` refers to the direction where the target type is annotated with `[MapFrom]`, while `"ToEntity"` is the reverse direction.
- **Stripping Variants** are generated *only on the source side*, in the order: original name → strip each matched prefix → strip each matched suffix → strip prefixes first, then suffixes. Stripped variants are still normalized according to the naming strategy before comparison.
- **Key Collision Resolution**: Dictionary keys are filled in declaration order of source properties. The first-declared property (including its stripped variants) claims the normalized key; subsequent properties with the same normalized key are discarded. Declaration order directly affects binding results.

### Exemption Mechanism

Global ignore is *fail-closed*: a match immediately triggers ignore. A target property may opt out of global ignore at the **mapping-pair level** by setting `OverrideIgnore = true`, which skips global ignore checks on *both* source and target sides.

- Exemption overrides **five attributes**: `[MapProperty]`, `[Convert]`, `[Mask]`, `[ConvertBack]`, and `[MapFormat]`. If *any one* of these carries `OverrideIgnore = true`, the entire mapping pair is exempted. The exemption check is attached to the *target property*.
- `[MapIgnore]` **does not participate** in exemption. Property-level explicit ignore always takes effect. Marking the same property with both `[MapIgnore]` and `OverrideIgnore = true` constitutes a contradictory configuration and triggers diagnostic `NCMAP022`.

### Diagnostics

| Code | Severity | Trigger Condition | Action |
|------|----------|-------------------|--------|
| `NCMAP020` | Warning | Invalid configuration value (e.g., enum out-of-bounds, list exceeding 8 items, full name of complex type) | The affected item is downgraded to “unconfigured” and does not block compilation; correct it using valid values or upper bounds. |
| `NCMAP021` | Warning | An explicit attribute (`[MapProperty]`, `[Convert]`, `[ConvertBack]`, `[Mask]`, or `[MapFormat]`) matches a global ignore rule but lacks `OverrideIgnore = true` | Either remove the attribute (it will be ignored anyway), or add `OverrideIgnore = true` to restore the mapping. |
| `NCMAP022` | Warning | Both `[MapIgnore]` and `OverrideIgnore = true` are applied to the same property (contradictory configuration) | Remove one of them. |

### Activation and Migration

Three-tier priority chain: non-null values override sequentially, with finer-grained layers taking precedence:

```text
Property-level attributes ([MapProperty] / [MapIgnore] / [Convert] / [Mask])
      >  [assembly: MapDefaults]
      >  MSBuild properties (e.g., `<NCMapperNamingStrategy>`, delivered via `buildTransitive` targets)
      >  Built-in defaults (Default strategy + empty lists)
```

- **Registration & Freezing**: The source generator reads `[assembly: MapDefaults]` at compile time and generates snapshot registration code. Configuration is frozen on the first mapping operation or when `Validate()` is called. Once frozen, no modifications are allowed. If multiple assemblies declare conflicting `[assembly: MapDefaults]`, the first freeze throws `MapConfigurationException` (*fail-fast*); identical declarations are merged idempotently.
- **Fallback Path**: Assemblies without source-generated code (i.e., those containing no `[MapFrom]` types) fall back to reflection-based reading of assembly-level attributes by `Mapper`. This path *does not read* MSBuild properties. At runtime, use `Mapper.GetGlobalConfig()` to obtain a read-only view (for debugging and assertions).
- **Migration Guidance**: Replace per-property annotations like `[MapProperty(FromField = "user_name")]` and individual `[MapIgnore]`s with a single `[assembly: MapDefaults(NamingStrategy = SnakeCase, IgnoreNames = [...])]`. DTO-side becomes zero-annotation.

For details on MSBuild channel, reflection fallback, and freezing behavior, see [global-config § Three-Tier Priority Chain](../infrastructure/global-config.md).

### Example

Run `dotnet run --scenario 606` to verify result consistency between per-property annotation and global configuration versions:

[!code-csharp[Global configuration in effect (MapGlobal)](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal)]

[!code-csharp[Global configuration in effect (MapGlobal) · RunSummary](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal_RunSummary)]

[!code-csharp[Global configuration in effect (MapGlobal) · Run](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal_Run)]

## 4. `MapCtorParam`: Attribute-Side Absence Explanation

Constructor parameter binding has **no attribute-side form**: there is no `MapCtorParamAttribute` in the public API. This is because declarative attributes cannot express the two required injection points — namely, the **target constructor parameter name** and the **source-side value expression** — since attribute arguments must be compile-time constants.

When constructor parameter binding is needed, use one of the following paths:

- Declare the mapping pair via the Fluent API (`.MapCtorParam`). Dual declaration for the same `(source, target)` pair across both channels is prohibited and triggers `NCMAP_F11`.
- Design a same-named property on the target type and rely on automatic name matching. For records, positional parameters correspond to same-named `init` properties — so `.Map` alone suffices to feed the constructor.

Fluent-side forms and diagnostics (`NCMAP_F51`–`NCMAP_F54`) are documented in [fluent/terminals § `MapCtorParam`](../fluent/terminals.md) (including asymmetry rules between channels).

## 5. Diagnostic Ownership Quick Reference

This table maps diagnostic codes for topics covered on this page across the two configuration channels. The primary diagnostic code table is in [diagnostics/rules.md](../../diagnostics/rules.md) (full numeric family + Fluent family by page reference); this table is not exhaustive.

| Topic | Attribute-Side Code | Fluent-Side Code |
|-------|---------------------|------------------|
| Conditional Guards | `NCMAP041` | `NCMAP_F40`–`NCMAP_F45` |
| Polymorphic Subtype Registration | `NCMAP_F46`, `NCMAP_F49` (attribute-specific) | Generic constraint errors CS0311/CS0314 (no `NCMAP` code) |
| Global Ignore & Global Configuration | `NCMAP020`–`NCMAP022` | Same-source (exemption gate overload; no dedicated codes) |
| Constructor Parameter Binding | None (see §4) | `NCMAP_F51`–`NCMAP_F54` |

## 6. Summary: Three-Column Quick Lookup

| Capability | API (Fluent Side) | Attribute (Attribute Side) |
|------------|-------------------|----------------------------|
| Property-level conditional guard | `.Member(d => d.X).MapWhen(cond)` / `.MapWhen<TSource>(cond)` (name-based + inline dual forms) | `[MapWhen(nameof(Predicate))]` (name-based only) |
| Polymorphic subtype registration | `.MapSubclass<TSrcDerived, TTargetDerived>()` | `[MapSubclass(typeof(DerivedSource), typeof(DerivedTarget))]` |
| Global default configuration | No dedicated terminal (same assembly attribute; read-only view available at runtime via `Mapper.GetGlobalConfig()`) | `[assembly: MapDefaults(...)]` (11 parameters) |
| Global ignore exemption | Same-source (exemption gate overload) | `OverrideIgnore = true` (on any of the five attributes) |
| Constructor parameter binding | `.MapCtorParam(paramName, source)` | No attribute-side form |

## Further Reading

- Related Topics: [basics](basics.md) · [attributes-index](../../reference/attributes-index.md) · [api-comparison](../../reference/api-comparison.md)
- Semantic Source Mapping: [fluent/terminals](../fluent/terminals.md) (Fluent-side semantics) · [infrastructure/global-config](../infrastructure/global-config.md) (deep dive into global configuration)
