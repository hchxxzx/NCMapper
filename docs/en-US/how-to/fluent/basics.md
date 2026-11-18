> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: fluent/basics
Diagnostic codes involved: NCMAP_F02, F04-F08, F11, F15, F16, F17, F23, F59, F60, F64-F67, F68-F71, NCMAP019  
Library version: Unreleased (to be released with the next version)  
Last updated: 2026-10-23  
---

# Fluent Basics (Chained Declaration Entry Point)

> The primary entry point for the Fluent API. This article covers foundational Fluent concepts: the *three-part Member syntax*, the *Ignore family*, *Tree’s three semantics*, *dual-mode auto-matching and whitelisting*, and *full-family diagnostic indexing*. Terminal-specific topics are covered in [terminals](terminals.md), [convert](convert.md), [from-source](from-source.md), and [hooks](hooks.md); cross-API comparisons are available in [api-comparison](../../reference/api-comparison.md).

## Overview

NCMapper recommends declaring mappings via the **Fluent chained entry point**: annotate a dedicated **Mapper configuration class** with `[MapperConfig]`, and declare a `static void M(FluentMappingConfig cfg)` method inside that class (**method name is arbitrary**) to configure mappings using chained calls. If you prefer annotating DTO classes directly, use the equivalent attribute-based approach: apply `[MapFrom]` on DTO types to declare mapping intent (see [Basic Mapping](../declarative/basics.md)).

> **Anchor type**: The attribute type corresponding to `[MapperConfig]` is `MapperConfigAttribute` (applicable only to classes; at most one per class) (source anchor: MapperConfigAttribute.cs:17).

These two declaration channels **coexist long-term (mode coexistence)**. You may freely mix them across different `(Source, Target)` type pairs. However, if **the same type pair is declared via both channels**, a compile-time error `NCMAP_F11` (mutual exclusion prohibition) is raised — one diagnostic per location, with messages cross-referencing each other.

Here, “same type pair” is determined by **unordered type pair**: `{S, T}` and `{T, S}` are considered identical. Thus, declaring `For<A, B>()` in Fluent and then either `For<B, A>()` or applying `[MapFrom]` on either `A` or `B` triggers mutual exclusion (unordered cross-reference detected by `NCMAP025`). Different type pairs remain independent and fully compatible with mixed channel usage.

Runnable example (`dotnet run --scenario 101` for live validation):

[!code-csharp[Fluent Basic Chain: Three-Part Member Syntax — From/Ignore/Mask + AfterMapping Chaining](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper)]

[!code-csharp[Fluent Basic Chain: Three-Part Member Syntax — From/Ignore/Mask + AfterMapping Chaining · Configure](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_Configure)]

[!code-csharp[Fluent Basic Chain: Three-Part Member Syntax — From/Ignore/Mask + AfterMapping Chaining · ApplyComputedFields](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_ApplyComputedFields)]

At compile time, the source generator scans the `Configure` method body and emits a `GeneratedMapper` mapping method **identical in behavior** to the one produced by the `[MapFrom]` channel (6 forward variants + 4 reverse variants, deep copy, registration). At runtime, there is zero behavioral difference.

## Three-Part Chained Syntax (Member Syntax)

Field-level declarations follow the **Three-Part Member Syntax**:  
`.Member(dest)` anchors the target field (Part I) → declares source/ignore (Part II, optional) → applies transformations (Part III, optional). **The field name appears only once**, and multiple terminals for the same field can be chained without repeating the `dest` lambda:

Runnable version (`Order`/`OrderDto` type pair, centralized `[MapperConfig]` declaration, scenario menu not attached):

[!code-csharp[Three-Part Chain: Anchor → Explicit Source → Transform / Name-Match Omission of From / Mask / Ignore](~/samples/NCMapper.Samples/01-Fluent/FluentBasicsScenarios.cs#FluentBasics_OrderChain_Configure)]

- **Part I (Anchor)**: `Member<TProp>(dest)` anchors the target property (single-segment pure property path). `TProp` is inferred from the lambda — this generic parameter disappears from subsequent field-stage method signatures. Returns the field "head" configurator `MemberHead`.
- **Part II (Header Declaration)**: One of `From`, `FromSource` (shorthand), or `Ignore`. **Only one header declaration is allowed per field**. `From`/`FromSource` return the field configuration object `MemberConfig`; `Ignore` returns the terminal `AfterIgnore` (final state — see *Ignore Family*).
- **Part III (Configuration Stage)**: `Mask`, `Format`, `FromJson`, `MapTree`, `Convert`, `ConvertBack`, `MapWhen`, or field-level `NoAutoInclude` — all supported at both `MemberHead` and `MemberConfig` positions. When placed immediately after the header (i.e., no explicit `From`), it implies *name-matching source resolution*: the source property is resolved by matching the target field name.
- **Returning to Outer Scope**: `MemberConfig` inherits from the pair-level configurator — after completing field-stage configuration, you may chain another `.Member(...)` or invoke a pair-level terminal (e.g., `NoAutoMatch`, `Tree`, hooks, `FromSource` blocks, etc.). `AfterIgnore` also inherits from the pair-level configurator.
- **Compile-Time Layered Constraints**: `MemberHead` does *not* inherit from the pair-level configurator — a bare `.Member(d => d.X)` with no further declarations is invalid (extraction phase warns with `NCMAP_F68` to prompt adding `From`/`Ignore`/`Transform`). Within `MemberConfig`, no additional `From`/`Ignore` headers may be added (enforced structurally; uniqueness of source per field is guaranteed at the header stage).

## Terminal Method Reference

The chained entry `cfg.For<TSource, TTarget>()` returns the pair-level configurator `FluentTypeConfig<TSource, TTarget>`. The table below lists all terminals, grouped by the three-part syntax layer. Field-stage terminals return to the outer scope and allow chaining; pair-level terminals return `this` and support continued chaining.

| Layer | Terminal Signature | Key Arguments / Generics | Semantics |
| --- | --- | --- | --- |
| Entry | `For<TSource, TTarget>()` | — | Declares the type pair; starting point of every chain (every chain must begin with `For`) |
| Anchor | `Member<TProp>(dest)` | Target property lambda (single-segment pure property path) | Part I of three-part syntax: anchors the target field; `TProp` is inferred and omitted from subsequent signatures |
| Header | `From<TSrcProp>(src, overrideIgnore = false)` | Source property lambda (supports nested paths and null-suppression `!`) + optional override gate | Explicit source binding (supports same-type and cross-type mappings; e.g., `DateTime → string` is handled by built-in converters in generated code); replaces legacy two-argument source declarations |
| Header | `FromSource<TSubSource>(src)` | Sub-source property lambda (`object` placeholder, supports nested paths) | Cross-source binding shorthand (see dedicated [from-source](from-source.md) section) |
| Header | `Ignore()` | No arguments | Ignores the target field (equivalent to `[MapIgnore]`; terminal state — see below) |
| Header | `Ignore(direction)` | `Direction` argument (`ToDto`/`ToEntity`/`Both`) | Directional ignore (equivalent to `[MapIgnore(Direction)]`; see dedicated section below) |
| Config | `Mask<TMask>(overrideIgnore = false)` | `where TMask : IMask` + optional override gate | Data masking (shares `MaskProvider` downstream with `[Mask]`; target must be `string`; composable with explicit `From`) |
| Config | `Format(format, overrideIgnore = false)` | Compile-time constant format string + optional override gate | Property-level formatting (source `IFormattable` → target `string`; semantic mirror of `[MapFormat]`; see dedicated [format](format.md) section) |
| Config | `FromJson()` | No arguments | JSON string expansion (deserializes source `string` via `JsonConverter`; equivalent to `[MapProperty(IsFromJson = true)]`) |
| Config | `MapTree()` | No arguments | Tree-shaped mapping (recursively applies the same `(S,T)` config to this field; if `From` is omitted, resolves source by name; distinction from pair-level `Tree()` explained under *Three Tree Semantics* below) |
| Config | `Convert(converter, overrideIgnore = false)` (4 overloads) | T0: `Func<TProp>`<br>T1: `Convert<TSrcProp>(Func<TSrcProp, TProp>)`<br>T2: `Func<TSource, TProp>`<br>T3: `Convert<TSrcProp>(Func<TSource, TSrcProp, TProp>)` | Forward custom conversion (Entity → DTO; semantic mirror of `[Convert]`; four forms detailed in [convert](convert.md)) |
| Config | `ConvertBack<TEntityProp>(converter, overrideIgnore = false)` (4 overloads) | T0: `Func<TEntityProp>`<br>T1: `Func<TProp, TEntityProp>`<br>T2: `Func<TTarget, TEntityProp>`<br>T3: `Func<TTarget, TProp, TEntityProp>` | Reverse custom conversion (DTO → Entity; semantic mirror of `[ConvertBack]`; see [convert](convert.md)). Write-back target resolution order: name match → auto-flattened leaf → lambda body. When names differ (first two steps fail), use lambda form to specify explicitly: T2 full-source form `(dto) => dto.XxxYyy` (direct member access on parameter = write-back target); T3 composite form `(dto, fieldValue) => M(fieldValue)` (write-back target matches forward source field). Resolution rules are intentionally narrow — composite forms do *not* perform inference (still report `NCMAP_F18`) |
| Config | `MapWhen(predicate)` (T1 field-value) | `Func<TProp, bool>`: named static method group or capture-free inline lambda | Conditional guard (maps only when predicate evaluates to `true`; T1 is non-generic — `TProp` already anchored, so inline lambdas are usable directly; see dedicated [terminals](terminals.md) section) |
| Config | `MapWhen<TInput>(condition)` (T2 full-source) | Explicit type argument `TInput` ∈ {`TProp`, `TSource`}; full-source form uses `<TSource>` | Conditional guard (multi-field evaluation); requires explicit type argument — inline lambdas lack inference context; see dedicated [terminals](terminals.md) section |
| Config | `NoAutoInclude` (field-stage, no args) | No arguments | **Field-level** disable of auto-inclusion (skips ORM downgrade-path navigation collection for this field; equivalent to `[MapNoAutoInclude]`; distinction from pair-level `NoAutoMatch` explained under *Dual Mode* below) |
| Ignore Terminal | Sole exception on `AfterIgnore`: `NoAutoInclude` (no args) | No arguments | Only operation allowed after `Ignore()` ("ignore + skip query loading" combination); returns immediately to pair-level — no second operation permitted |
| Pair-Level | `NoAutoMatch()` | No arguments | **Pair-level** whitelist toggle: disables auto-matching/auto-inclusion for this `(S,T)` pair (see *Dual Mode* section below; legacy pair-level name has been renamed to this) |
| Pair-Level | `Tree()` / `Tree(id, parent, children)` | No arguments, or triplet of property names (`id`/`parent`/`children`) | Pair-level tree configuration terminal (not a field-mapping terminal): fluent equivalent of `[MapTree]`, registered opt-in; tree mounting position defaults to `false` if not invoked (see *Three Tree Semantics* below) |
| Pair-Level | `FromSource<TSubSource>(configure)` | Group block: `Action<FluentTypeConfig<TSubSource, TTarget>>` | Grouped multi-field declaration from same sub-source (block scope = same-type `For` chain; see dedicated [from-source](from-source.md) section) |
| Pair-Level | `BeforeMapping(handler)` / `AfterMapping(handler)` | `Action<TSource, TTarget>` static method group | Lifecycle pre-/post-mapping hooks (see dedicated [hooks](hooks.md) section) |
| Pair-Level | `MapCtorParam<TSrcProp>(paramName, sourceProp)` / `MapCtorParam(paramName, sourcePropertyName)` | Parameter name + single-segment source lambda / source property name string | Explicit constructor parameter binding (Fluent-exclusive; see dedicated [terminals](terminals.md) section) |
| Pair-Level | `MapSubclass<TSrcDerived, TTargetDerived>()` | Two generic arguments (derived source / derived target) | Polymorphic subtype pair registration (pair-level; see dedicated [terminals](terminals.md) section) |

> The override gate for `From`/`Mask`/`Format`/`Convert`/`ConvertBack` is implemented as a **single-signature built-in optional parameter** `bool overrideIgnore = false` (see dedicated section below), not as separate overloads.

## OverrideIgnore: Global Ignore Override Gate

Override capability resides in a **single-signature optional parameter** `bool overrideIgnore = false` on five source/transformation terminals (`From`/`Mask`/`Format`/`Convert`/`ConvertBack`). Its semantics mirror the attribute-side named parameter `OverrideIgnore`: when the **global ignore list** (configured via `MapOptions`) matches this field, the override takes effect — mapping, masking, conversion, or formatting proceeds normally and is *not suppressed*, applying to both forward and reverse chains.

Runnable example (`dotnet run --scenario 102` for live validation; override-gate as optional parameter):

[!code-csharp[OverrideIgnore Gate — Chained Declaration](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_Ignore)]

Declaring both `overrideIgnore = true` and `Ignore()` on the same field remains contradictory (unambiguous within declaration surface; fails fast with `NCMAP_F23`/`NCMAP_F17`).

## Ignore Direction Overload: Directional Ignore

`Ignore(direction)` is semantically equivalent to `[MapIgnore(Direction)]`: ignores the field *only in the specified direction*, while mapping proceeds normally in the other direction.

Runnable example (`dotnet run --scenario 102` for live validation):

[!code-csharp[Ignore Direction Overload: ToDto/ToEntity Directional Ignore](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_Ignore)]

Omitting the direction (bare `Ignore()`) equals bidirectional ignore (`Both` semantics).

**Ignore is a terminal state**: `Ignore()` / `Ignore(direction)` ends the field-stage declaration — no further mapping/transformation operations are permitted afterward (enforced at compile time). The sole exception is the field-level `NoAutoInclude` used in the "ignore + skip query loading" combination (returns immediately to pair-level; no second operation allowed).

## Default Auto-Matching Behavior

Consistent with the `[MapFrom]` channel: **unmapped properties with matching names are still automatically matched and auto-flattened**. When only explicitly named fields are declared, all other name-matching properties are mapped as usual. To enable *"map only explicitly declared fields"* (whitelist mode), use the pair-level `NoAutoMatch()` (see *Dual Mode* section below).

## Dual Modes: Auto-Matching and Whitelist (Distinguishing `NoAutoMatch` and Field-Level `NoAutoInclude`)

> Terminology note: "**Pair**" = a type pair (mapping source type `S` to target type `T` forms a mapping pair `(S, T)`, a common industry term, not coined here). "**Pair-level**" = configuration scope applies to the entire mapping relationship between the pair (configured on the pair-level configurator returned by `For<S, T>()`). "**Field-level**" = applies only to a single property (within the `.Member(d => d.X)` segment).

**Mnemonic: Whether it *matches* is controlled by `Match` (pair-level); whether it’s *included* is controlled by `Include` (field-level).**

Although sharing a naming family, these two switches have **distinct identities**:

| | `NoAutoMatch()` (pair-level) | `NoAutoInclude` (field-level, inside `.Member(d => d.X)`) |
| --- | --- | --- |
| Scope | **Disables auto-matching** — disables the automatic matching scope for this `(S,T)` pair; only explicitly declared mappings are applied | **Excludes from query loading** — this field is excluded from automatic navigation collection (navigation `Include` is skipped along ORM fallback paths; evaluated by `NavigationTreeCollector`) |
| Granularity | Pair-level (matching policy for **all fields** in the pair) | Field-level (behavior affects **only that one field**) |
| Entry point | Written directly after `For<S,T>()`; reachable at any position after `From(...)` via inheritance surfaces | Written directly inside the `.Member(d => d.X)` segment (or stacked after a `From` declaration) |
| Dual-channel support | **None** (Fluent-only); class-level attribute equivalent `[MapNoAutoMatch]` is documented in [dual-channel](../../explanation/dual-channel.md) and diagnostic `NCMAP047` | **Yes**: `[MapNoAutoInclude]` (property-level, dual-channel equivalent) |

The two switches operate in **parallel, independent capacities** (neither migrates nor overrides the other); when both are applied, their effects are unioned.

### What `NoAutoMatch` Disables (The “Three Auto” List)

By default, three kinds of automatic behavior occur even without explicit declarations. `NoAutoMatch()` disables **all three**:

| # | Disabled "Auto" Behavior | Description |
|---|---|---|
| ① | **Case-insensitive name-based auto-matching** for undeclared properties | Undeclared properties—even those with identical names—are not matched |
| ② | **Configuration-aware auto-completion** | After declaring some fields, the engine automatically fills in remaining fields by default; enabling `NoAutoMatch` disables this completion |
| ③ | **Automatic flattening** | Automatic concatenation of nested paths (e.g., `Address.City` → `AddressCity`) is disabled |

### Whitelist Mode Does *Not* Prohibit Mapping Itself

`NoAutoMatch` disables only the word "**auto**", not mapping itself: mapping methods are still generated (6 forward + 4 reverse), registration proceeds normally, and `Map(...)` calls succeed as usual. Runtime behavior comparison (empirically verified):

| State | Mapping Execution | Explicitly Declared Properties | Undeclared but Name-Matching Properties |
|---|---|---|---|
| **Default** | ✅ Normal | ✅ Mapped | ✅ **Automatically matched** |
| **`NoAutoMatch` Enabled** | ✅ **Still normal** | ✅ Mapped | ❌ **Not matched** (even if names match) |
| (Misconception) "Mapping prohibited" | ❌ Fails / unavailable | —— | —— |

**One-sentence summary (corrected):**

> **Mapping remains fully functional—but switching to whitelist mode means *only* properties explicitly named in Fluent (i.e., declared via `.Member(...)`) will be mapped. All others—even name-matching ones or those resolvable via automatic path flattening—are excluded.**

Two footnotes:

- **"Naming" has two valid syntaxes**, both count:  
  `.Member(d => d.X).From(s => s.Y)` (explicit source specified)  
  and `.Member(d => d.X)` (source omitted → defaults to same-named source property).  
  The key is **the act of writing `.Member(...)` itself** — the phrase “only explicitly declared / same-name-default properties” in the `NoAutoMatch` documentation does *not* mean “same-name properties are mapped by default”; rather, “same-name default” is syntactic sugar *within* the field declaration chain: once declared, omitting `From` implies same-name resolution; no declaration means no access at all.
- **Reverse mapping follows the same rule**: reverse mappings are generated by flipping each forward declaration individually — under whitelist mode, reverse mapping covers *only* the explicitly named properties.

**Use-case positioning ("accidental change guard")**: Default auto-mode is more convenient; manual mode (`NoAutoMatch`) should be used only for security-sensitive pairs. A typical scenario: a future source-type property addition (e.g., `Password`) would silently flow into the DTO under default mode on next build; enabling `NoAutoMatch()` forces new properties to be explicitly named before inclusion.

**Two common sources of confusion**:

1. **Name-family similarity**: Both start with `NoAuto*` — but `Match` refers to *whether to match (map)*, while `Include` refers to *whether to include (load in queries)* — two distinct domains.
2. **Silent cross-scope invocation**: Calling `NoAutoMatch()` *after* `.Member(...)` (via inheritance surfaces) is syntactically legal but affects the **entire pair**, not just the field. Misplaced usage: using bare pair-level method names other than `NoAutoMatch` triggers compile-time errors (CS1061); **swapping the two switches (writing one where the other belongs) results in silent semantic change and zero diagnostics** — verify using the mnemonic above.

Runnable demos for both switches are available in Scenario 101 (`dotnet run --scenario 101`; real code in `samples/NCMapper.Samples/01-Fluent/FluentModels.cs`: one pair configured with pair-level whitelist, another with field-level navigation exclusion).

## `[MapTree]` / `MapTree()` (inside `.Member` segment) / `Tree()` — Three Distinct Tree Semantics

These three similarly named tree-related declarations have **different semantics** (similar names, divergent meanings; used complementarily; recursive mapping typically occurs at the `Children` attachment point):

Runnable example (`dotnet run --scenario 101` validates live; `Tree()` custom triplet pair-level config shown below):

[!code-csharp[Tree terminal: Custom triplet (Key/ParentKey/Nodes) pair-level config](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_TreeNodeMapper)]

[!code-csharp[Tree terminal: Custom triplet (Key/ParentKey/Nodes) pair-level config · Configure](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_TreeNodeMapper_Configure)]

| Declaration | Granularity | Semantics |
|-------------|-------------|-----------|
| `[MapTree]` (class-level attribute) | Pair-level build configuration | Declares the tree attachment triplet (defaults to `Id`/`ParentId`/`Children`) consumed by `ToTreeList` tree construction and ORM projection tree-attachment detection (`IsTreeChildrenSlot`). **Consumed reflectively at runtime by `TreeBuilder`**; Fluent DTOs also benefit from its fallback resolution. |
| `MapTree()` (Fluent configuration terminal) | Field-level mapping declaration | Declares "*this field is recursively mapped*" (if `From` is omitted on the source side, resolution falls back to same-named property; recursion follows identical configuration). This is a materialized-layer mapping declaration. |
| `Tree()` / `Tree(id, parent, children)` | Pair-level build configuration | **Fluent equivalent of `[MapTree]`**: backed by registry (`GetTreeConfig` preferred, reflection fallback), opt-in — if never called, `TreeConfig=null` in registry and tree-attachment detection always returns `false` (prevents double-attachment edge case where a self-referencing collection property happens to be named `Children`). |

Similarly, the property-level attribute `[MapNoAutoInclude]` (skips ORM fallback-path navigation collection for this property) has a Fluent equivalent: write parameterless `NoAutoInclude` directly inside the `.Member(...)` segment (e.g., after anchoring `.Member(d => d.AuditLogs)` — the *only* field-level syntax). Its granularity differs from the pair-level whitelist switch `NoAutoMatch()` — see the Dual Modes section above.

## Limitations and Boundaries (Current Version)

- The `Configure` method body supports **only pure fluent-style static calls** (dynamic construction triggers `NCMAP_F08`).

- Property-level attributes (`[MapProperty]`/`[MapIgnore]`/`[Mask]`/`[Convert]`) **do not take effect** in the Fluent channel; use fluent methods to express equivalent semantics. Tree configuration and field-level auto-include disablement also have fluent equivalents (`Tree()` ↔ `[MapTree]`, `NoAutoInclude` inside `.Member(...)` ↔ `[MapNoAutoInclude]`; see the Three Semantics and Dual Modes sections above). Fluent inherently satisfies the “property-level configuration follows class anchor” constraint (mapping configuration follows class anchor): all terminal declarations appear on the `For<S, T>()` chain, so no configuration exists outside the mapping declaration — no extra validation needed. Note the distinction: what *does not take effect* is **property-level attribute annotation**; semantics written into the registry via fluent declarations (e.g., `.Member(d => d.X).Mask<TMask>()`) **do take full effect at runtime** — the registry consumes declarations directly, unimpeded by attribute anchoring constraints.

- Target property path in `Member` must be **single-segment** (`From` source side supports multi-segment nested paths).

- Lambda expressions support **only property access paths** (field references are discarded during parsing).

- Structural errors (chain not starting with `For`, incorrect generic parameters, unknown method names, etc.) are reported by the **C# compiler**, not the source generator.

- Repeated Fluent declarations for the same `(S,T)` pair trigger `NCMAP_F16` (**Error**): each conflicting location reports one message (listing all conflicting mapper class names); *none* of the conflicting declarations generate mapping methods (consumer-side CS0111 errors vanish accordingly); merge into a single declaration or remove duplicates.

- True conflicts on the same anchor property (duplicate declarations in the same slot, or simultaneous declaration and `Ignore`) trigger `NCMAP_F59` (**Error**); duplicate field segments within the same source slot or transform slot trigger `NCMAP_F70` (**Error**) — valid combinations (e.g., `From` + `Mask`) are listed in the Three-Part and Dual Modes sections above.

- Fluent type pairs **benefit from ORM projection acceleration**: `AsDto` performs **per-declaration translation evaluation** via dual reads against the runtime registry. Purely translatable declarations (source declarations, `Ignore`, etc.) execute on the SQL side; `Mask`/`Convert`/`FromJson`/`AfterMapping`/`BeforeMapping`/`MapWhen`/`FromSource`/`MapSubclass` force full memory mapping (full semantic fidelity; `MapSubclass` uses `isinst` dispatch chains, categorized as `SubclassDispatch`); `MapCtorParam` does *not* trigger degradation (it’s excluded from the non-translatable category set). See [ORM Degradation Mechanism §2.3](../../explanation/orm-degradation.md) and the “Fluent DTO Support Status” section in each ORM how-to guide.

## Compile-Time Diagnostics (NCMAP_F Series) Full Index

| Error Code    | Scenario                                                                                                                    |
| ------------- | ------------------------------------------------------------------------------------------------------------------------- |
| `NCMAP_F02`   | A property referenced in a lambda does not exist on the type.                                                               |
| `NCMAP_F04`   | The root of the lambda is not a parameter (i.e., not a local variable or static class reference).                         |
| `NCMAP_F05`   | The lambda contains a null-conditional access (`?.`).                                                                     |
| `NCMAP_F06`   | The lambda contains an indexer access (`[]`).                                                                             |
| `NCMAP_F07`   | The lambda contains a method call (`()`).                                                                                 |
| `NCMAP_F08`   | The `Configure` method body contains dynamic configuration statements (e.g., `if`, `for`, `foreach`, `while`, `switch`, `try`, ternary operators, assignments, or local variables); only pure static chained calls are allowed. |
| `NCMAP_F11`   | The same `(S,T)` pair is declared both via `[MapFrom]` and Fluent configuration (Error: mutual location references; no mapping method is generated on either side). |
| `NCMAP_F12`   | The target method for `AfterMapping` is `private` (Error).                                                                |
| `NCMAP_F14`   | The target of `AfterMapping` is not a statically resolvable method group (Error).                                         |
| `NCMAP_F15`   | No method with a matching signature exists inside a class marked with `[MapperConfig]` (hint: entry-point convention).  |
| `NCMAP_F16`   | The same `(S,T)` pair is redundantly declared within the Fluent pipeline (Error: each declaration reports separately, listing all conflicting mapper class names; no mapping method is generated for any conflicting declaration). |
| `NCMAP_F17`   | Conflict between `Ignore` and `ConvertBack` on the same field segment (Error: `Ignore` excludes the property from candidates, so reverse write-back never executes). |
| `NCMAP_F18`   | The reverse write-back point for `ConvertBack` does not exist on the entity side (neither name-matching nor flattened resolution succeeds; Error). |
| `NCMAP_F19`   | The `ConvertBack` converter is `private`/`protected` (Error: generated code would be inaccessible from the same assembly). |
| `NCMAP_F20`   | The `ConvertBack` converter is a closure (capturing outer state), a local function, or instance-bound (Error; uncaptured inline lambdas are permitted and transcribed into generated code). |
| `NCMAP_F21`   | The `ConvertBack` converter’s signature does not match the four-form template (`T0`/`T1`/`T2`/`T3`; generic methods, optional parameters, `params`, `ref`/`out` are excluded; Error). |
| `NCMAP_F22`   | The anchor property for `ConvertBack` lacks a `public` setter or `init` accessor (Warning: the declaration will be ignored during materialization; change to `public`/`init` setter or use the attribute-based `[ConvertBack]` on the entity side). |
| `NCMAP_F23`   | Conflict between `Ignore` and `Convert` on the same field segment (Error: `Ignore` excludes the property from candidates, so forward write never executes). |
| `NCMAP_F24`   | The source side for `Convert` is unreachable (name-matching, flattening, or reverse lookup via `[MapProperty]` all fail; Error, fail-fast; note: attribute-side missing sources fall back to `default`, reflecting channel boundary differences). |
| `NCMAP_F25`   | The `Convert` converter is `private`/`protected` (Error: generated code would be inaccessible from the same assembly). |
| `NCMAP_F26`   | The `Convert` converter is a closure (capturing outer state), a local function, or instance-bound (Error; uncaptured inline lambdas are permitted and transcribed into generated code). |
| `NCMAP_F27`   | The `Convert` converter’s signature does not match the four-form template (`T0`/`T1`/`T2`/`T3`; generic methods, optional parameters, `params`, `ref`/`out` are excluded; Error). |
| `NCMAP_F28`   | The anchor property for `Convert` lacks a `public` setter or `init` accessor (Warning: the declaration will be ignored during materialization). |
| `NCMAP_F29`   | Conflicts in main-chain `FromSource` bindings targeting the same destination / multiple bindings to the same sub-source (Error: one property can only be supplied by one source; within the same sub-source group, only path-bearing declarations (`From`/`FromSource` shorthands), `MapTree`, and `Convert` are counted; modifier attachments like `Mask`/`FromJson` are excluded from counting). |
| `NCMAP_F30`   | `ConvertBack` is prohibited inside a `FromSource` block (Error: `FromSource` bindings support only forward mapping; no reverse write-back point exists). |
| `NCMAP_F31`   | Disabling auto-matching switches is prohibited at the peer level inside a `FromSource` block (Error: all sub-source fields must be explicitly declared; the block context has no implicit inclusion scope to disable). |
| `NCMAP_F32`   | `AfterMapping` is prohibited inside a `FromSource` block (Error: the generated `Compose` pipeline has no hook injection points; such declarations never execute). |
| `NCMAP_F35`   | The target method for `BeforeMapping` is `private` (Error: inaccessible across classes). |
| `NCMAP_F36`   | The target of `BeforeMapping` is not a statically resolvable method group (e.g., lambda, closure, or captured variable; Error). |
| `NCMAP_F37`   | The target type is constructed via a parameterized or `init-only` constructor (construction and assignment cannot be separated; thus, no insertion point exists for `BeforeMapping`; Error — reported on both sides). |
| `NCMAP_F38`   | `BeforeMapping` is prohibited inside a `FromSource` block (Error: the `Compose` pipeline has no hook injection points; such declarations never execute). |
| `NCMAP_F39`   | The same mapping pair declares identical lifecycle hooks via both channels (e.g., `Before`×`Before` or `After`×`After`; Error; cross-combinations do not conflict). |
| `NCMAP_F40`   | The predicate method group contract for `MapWhen` form A fails (unresolvable, non-static, insufficient visibility, signature mismatch, or missing implicit conversion; Error). |
| `NCMAP_F41`   | The inline lambda in `MapWhen` form B captures variables or accesses private members (transcription prohibition; Error). |
| `NCMAP_F42`   | The inline lambda in `MapWhen` form B contains statement bodies or disallowed AST nodes (transcription prohibition; Error). |
| `NCMAP_F43`   | The anchor field for `MapWhen` has no guardable mapping (no same-name source and no explicit `From`, or excluded by static `Ignore`; Error — the guard never activates). |
| `NCMAP_F44`   | Duplicate `MapWhen` declarations on the same property (single condition; Error; use predicate negation for negative filtering). |
| `NCMAP_F45`   | The main-chain `MapWhen` anchor property is overridden solely by `FromSource` cross-source binding (the `Compose` pipeline has no emission landing point; Error; declarations inside the block remain valid). |
| `NCMAP_F51`   | The source property bound by `MapCtorParam` does not exist or lacks a public readable getter (Error, fail-fast). |
| `NCMAP_F52`   | The target public constructor does not declare the corresponding parameter (message lists available parameter names; Error; `ByRef`/pointers are not bindable). |
| `NCMAP_F53`   | Duplicate binding or conflict with an explicit `From` entry for the same parameter (Error, fail-fast). |
| `NCMAP_F54`   | Multiple constructors of equal precedence coexist (Info hint: selector silently resolves by parameter count; explicit binding declarations can resolve ambiguity). |
| `NCMAP_F59`   | True conflict on the same anchor property (Error; semantic revision as of 2026-10-09: original “later-write-overrides-earlier-write (Warning)” narrowed to “only true conflicts trigger errors” — i.e., duplicate declarations in the same slot or simultaneous mapping + `Ignore` declarations are rejected outright; retain only one, or combine per the above list of valid combinations). |
| `NCMAP_F60`   | An unrecognized terminal method name appears in the Fluent chain (compiles but is not a known terminal — e.g., unknown calls before three-stage consumption adapters are fully implemented; Warning — that node’s declaration is skipped; entire chain discarded in main-chain scenarios). |
| `NCMAP_F64`   | The target property for `Format` is not of type `string` (Error: formatted output must be textual). |
| `NCMAP_F65`   | The source property for `Format` is unreachable or not `IFormattable` (Error: supported types include `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, numeric types, enums, and their `Nullable<T>` wrappers). |
| `NCMAP_F66`   | `Format` and `Convert` are declared on the same anchor (Error: choose one). |
| `NCMAP_F67`   | Reverse projection for `Format` is hit but `ConvertBack` is not annotated (Warning — reverse assignment for this property is skipped; round-trip requires `ConvertBack`; see [format](format.md)). |
| `NCMAP_F68`   | A `Member` field segment has no subsequent declaration (bare `.Member(...)`; Warning — add a source (`From`/`FromSource`), ignore (`Ignore`), or transformation (`Mask`/`Format`, etc.)). |
| `NCMAP_F69`   | Source is omitted in a field segment and the source type (or `FromSource` sub-source) has no member with the same name (Error — legacy field-level syntax silently dropped such declarations; the explicit three-stage `Member` syntax now raises an error; supply `From` to specify the source). |
| `NCMAP_F70`   | Duplicate declaration on the same property (duplicate in the same slot — source slot and transform slot each allow only one declaration; Error — reports the first and duplicate declaration kinds; retain only one). |
| `NCMAP_F71`   | For the whole-source form of `MapWhen`, the type argument neither matches the target property type nor the source type (Error — whole-source form writes `MapWhen<SourceType>(...)`; field-value form omits the type argument). |

> Note 1: `NCMAP019` (“DTO naming heuristic suggests missing `[MapFrom]`”) **automatically exempts Fluent DTOs**: Once a Fluent type is registered by the source generator, this diagnostic is suppressed (hybrid attribute+Fluent projects are also exempt; unannotated plain DTOs remain subject to negative checks without relaxation). Removing the corresponding Fluent declaration restores the diagnostic (additions/removals support incremental validation).
> Note 2: `NCMAP_F46`–`NCMAP_F50` (`MapSubclass` family) are documented individually in the `MapSubclass`-specific diagnostics table under [terminals](terminals.md); `NCMAP_F55`–`NCMAP_F58`/`NCMAP_F62`/`NCMAP_F63` (lambda forms and chain-parsing governance family) are documented individually in the main diagnostics rules table at [diagnostics/rules.md](../../diagnostics/rules.md).
> Note 3: Attribute-side dual-channel parsing diagnostics `NCMAP038`/`039`/`040` (`[BeforeMapping]`/`[AfterMapping]` hook methods unresolved/signature mismatch/overload ambiguity — mirror `NCMAP035`/`036`/`037`’s `Convert` family) and `NCMAP041` (`[MapWhen]` predicate resolution failure) are detailed in their respective dedicated sections: [terminals](terminals.md) and [hooks](hooks.md); full entries appear in [diagnostics/rules.md](../../diagnostics/rules.md).

## Companion Examples

Runnable demonstrations are located in `samples/NCMapper.Samples/` (scenarios 101–107, 109; run with `dotnet run --scenario N`):

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 101   # Fluent basic chaining: Member.From/Ignore/Mask/AfterMapping, MapTree, per-type NoAutoMatch, per-field NoAutoInclude, Tree, index segments
dotnet run --scenario 102   # Fluent conversions: Convert/ConvertBack in four forms, pairing, optional guard parameters, FromJson, entity-side anchor families
dotnet run --scenario 103   # Fluent FromSource: shorthands/grouped blocks, Mask/FromJson inside blocks, order-based override, bidirectional support
dotnet run --scenario 104   # Fluent lifecycle hooks: BeforeMapping/AfterMapping (both Fluent and class-level attributes), phase ordering, cross-pair signature filtering
dotnet run --scenario 105   # Fluent conditional guards: MapWhen in four forms within the same chain, [MapWhen] attribute support
dotnet run --scenario 106   # Fluent constructor parameter binding: MapCtorParam in two forms, record boundary clarifications, MapList at element level
dotnet run --scenario 107   # Fluent polymorphic subtype mapping: MapSubclass with four dispatch families, base-class fallback, reverse subclass pairs, [MapSubclass] attribute support
dotnet run --scenario 109   # Compose runtime metadata (RuntimeComposeMapping) + new syntax overlays: cross-type From, From+Format+Mask (FormatThenMask), From+Mask+MapWhen
```

The example code in this article shares a single source of truth with the `samples` directory (referenced via `[!code-csharp]`). Changes should be made in the `samples` directory.

## Next Steps

- Declarative basic mapping: [declarative/basics](../declarative/basics.md)
- Dot-path notation and nesting: [mapping/dot-path](../mapping/dot-path.md)
- Data masking: [infrastructure/masking](../infrastructure/masking.md)
- Diagnostic rules: [diagnostics/rules](../../diagnostics/rules.md) (complete numeric-family table + F-family paginated navigation)
- Cross-API comparison: [api-comparison](../../reference/api-comparison.md)
