> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: diagnostics/rules  
Affected diagnostic codes: Full index (NCMAP001–047 [excluding NCMAP004, vacant], 099; NCMAP_F02–F71 [excluding NCMAP_F61, vacant] — see the diagnostics tables on each Fluent “How-To” page)  
Library version: v1.0.0+  
Last updated: 2026-10-23  
---

# Diagnostic Rules (Full NCMAP Numeric Family + Fluent Family Page-Referenced Layout)

> This table is the **primary NCMAP diagnostic code table**: the numeric family `NCMAP0xx` (including `NCMAP099`) appears here in its complete, single-source form — mechanically reconciled against `src/NCMapper.SourceGen/AnalyzerReleases.Shipped/Unshipped.md` via the G3 gatekeeper (`scripts/check-diagnostics-sync.sh`); the set of IDs and their severity levels are strictly identical between both sources. The Fluent family `NCMAP_Fxx` follows a **primary-table + page-referenced layout**: full per-code distribution is detailed at the end of this document in the cross-reference statement (see: *basics* diagnostics table / *terminals MapSubclass* dedicated section / `F`-row entries in this table). For the underlying system model—including naming conventions, three-level semantics, and tripartite registration discipline for both families—see [explanation/semantics/diagnostics-model](../explanation/semantics/diagnostics-model.md).

## 16. Diagnostic Rules

| Rule       | Severity | Description                                                                                                                               |
|------------|:--------:|-------------------------------------------------------------------------------------------------------------------------------------------|
| NCMAP001   | ⚠️ Warn  | Source field specified by `[MapProperty]` does not exist in the source type.                                                            |
| NCMAP002   | ⚠️ Warn  | Property type is incompatible with the source field type.                                                                                 |
| NCMAP003   | ⚠️ Warn  | DTO is annotated with `[MapFrom]`, but no properties can be mapped.                                                                       |
| NCMAP005   | ⚠️ Warn  | Composed cross-table field not found.                                                                                                     |
| NCMAP006   | ⚠️ Warn  | Target property has a matching source property, but its setter is not `public` (`private set` / read-only), so assignment code cannot be generated. |
| NCMAP007   | ℹ️ Info  | Source property is unmapped (no matching target property; hints that the DTO is out of sync).                                            |
| NCMAP008   | ❌ Error | Reserved number; no longer triggered.                                                                                                     |
| NCMAP009   | ⚠️ Warn  | Duplicate `[MapProperty]` annotations pointing to the same source property.                                                              |
| NCMAP010   | ⚠️ Warn  | Enum mapping members have no value overlap (explicit cast by value may yield unexpected results).                                         |
| NCMAP011   | ℹ️ Info  | Non-`public` types are skipped by the source generator (`internal`/`private` types do not generate mapping code).                      |
| NCMAP012   | ⚠️ Warn  | Record circular reference causes `DeepCopy` to fall back to runtime (source generator cannot handle recursive record structures).         |
| NCMAP013   | ℹ️ Info  | Reverse-mapping properties are skipped (certain properties are unmappable in the Entity ← DTO direction; semantic narrowing: dot-path/flat reverse write-back is already implemented; scenarios like `Convert`, missing setter/unavailable setter, or collection-index reverse registration remain). |
| NCMAP031   | ❌ Error | Intermediate node in reverse write-back path is not instantiable (interface/abstract class/no public parameterless constructor); compilation blocks illegal code generation. |
| NCMAP014   | ❌ Error | Type specified by `[Mask]` does not implement the `IMask` interface.                                                                     |
| NCMAP015   | ❌ Error | `[Mask]` only supports `string`-typed properties; target property type is invalid.                                                      |
| NCMAP016   | ℹ️ Info  | Deep copy via source generation: type contains statically unresolvable reference edges (`object`/`dynamic`/interface/virtual reference properties, etc.), classified as `Unknown`; full runtime detection is used. |
| NCMAP017   | ℹ️ Info  | `[MapNoAutoInclude]` on navigation properties is meaningless (navigation properties are explicitly included by users anyway).            |
| NCMAP018   | ⚠️ Warn  | `T[].Contains(T)` in expression tree lambdas binds to `MemoryExtensions.Contains` (.NET 10 / C# 14 — FreeSql cannot translate it); recommend using `AsEnumerable().Contains` instead. |
| NCMAP019   | ❌ Error | Type is annotated with mapping configuration attributes (eleven property-level attributes: `[MapProperty]`/`[Convert]`/`[ConvertBack]`/`[MapIgnore]`/`[BeforeMapping]`/`[AfterMapping]`/`[Mask]`/`[MapWhen]`/`[MapNoAutoInclude]`/`[MapFormat]`/`[MapNoAutoMatch]`) but lacks `[MapFrom]` — configuration has no activation path and silently fails; paired rules are enforced at compile time. Add `[MapFrom]` on the configuration-bearing side or remove the attributes. Fluent-configured target types (`cfg.For<S,T>()`) are exempt. `[MapTree]` defines class-level tree construction (not a mapping pair) and does not trigger this diagnostic. |
| NCMAP042   | ℹ️ Info  | Type name conforms to the DTO pattern (`Dto`/`DTO` suffix) but lacks `[MapFrom]`, preventing source-generated mapping code; falls back to reflection at runtime, which is unavailable under AOT/Trimmed scenarios. Types carrying mapping configuration attributes report `NCMAP019` (Error) instead. Fluent-configured target types are exempt. |
| NCMAP020   | ⚠️ Warn  | Global mapping configuration value is invalid (e.g., enum out-of-bounds, unresolvable fully qualified type name, list exceeding 8 items); the item is treated as unset and does not block compilation (analogous to Mapperly RMG095). |
| NCMAP021   | ⚠️ Warn  | Explicit annotations (`[MapProperty]`/`[Convert]`/`[ConvertBack]`/`[Mask]`/`[MapFormat]`) match global ignore rules without `OverrideIgnore`, resulting in fail-closed ignoring; message provides bidirectional handling guidance. |
| NCMAP022   | ⚠️ Warn  | Contradictory configuration: property is annotated with both `[MapIgnore]` and `OverrideIgnore=true`; `[MapIgnore]` takes effect (it does not participate in global ignore exemption); cleanup guidance provided. |
| NCMAP023   | ℹ️ Info  | Enum member names differ only by case (violates CLS compliance); `string`→`enum` generates a `switch` that degrades to exact case-sensitive matching; invalid values still throw `MapException`. |
| NCMAP024   | ℹ️ Info  | Duplicate enum member values (alias enums); `string`→`enum` numeric string labels degrade to exact name matching; numeric strings cannot be converted during code generation. |
| NCMAP025   | ❌ Error | Dual mapping declaration for the same unordered type pair (`A [MapFrom(B)]` and `B [MapFrom(A)]`; applies to both attribute-based and Fluent `For<S,T>()` declarations). Mapping code is not generated for such pairs (mapping is bidirectional but generation must have a unique source), causing build failure. Runtime falls back to name-based matching; removing one declaration restores functionality. Fluent diagnostics pinpoint the `For<S,T>()` call site. |
| NCMAP026   | ⚠️ Warn  | `[Mask]` annotation is placed non-conformantly (on the source side, opposite to `[MapFrom]`); source-side placement has no effect (masking semantics are unidirectional: target `string` is masked); move annotation to the target property. |
| NCMAP027   | ℹ️ Info  | `[MapFrom]` automatic property matching skips members when source and target types are completely incompatible (e.g., `string` → `Stream`) and issues a notification. |
| NCMAP028   | ℹ️ Info  | Mapped property is skipped: member is inaccessible (visibility is not `public`; generated code accesses only `public` members; cross-assembly access to `internal`/`protected`/`private` triggers CS0122). |
| NCMAP029   | ℹ️ Info  | Nested mutual invocation cycles have been downgraded (two registered DTOs mutually invoke each other via nested properties; registrations on the cycle are downgraded and skipped to prevent StackOverflow from cyclic data; direct self-reference remains protected by depth limits). |
| NCMAP030   | ℹ️ Info  | Explicit dot-path mappings do not consume `[Mask]` / `[Convert]` (mapped values are neither masked nor converted; known limitation in current version; this diagnostic ensures observability only). |
| NCMAP032   | ℹ️ Info  | `init-only` target properties are skipped in update paths (`init` allows assignment only during construction/creation; existing target objects cannot be reassigned, so the property is skipped). |
| NCMAP033   | ℹ️ Info  | Reverse mapping does not consume `[Convert]` converters by default (converters are generically bound to the forward direction); reverse writes follow type conversion paths. For custom reverse conversion, annotate with `[ConvertBack]`. |
| NCMAP034   | ℹ️ Info  | `[Mask]`-annotated properties participate in reverse write-back (masked values are written unchanged into the target entity; masking is lossy and irreversible; for CRUD update scenarios, consider annotating masked DTO properties with `[MapIgnore(ToEntity)]`). |
| NCMAP035   | ❌ Error | `[Convert]`/`[ConvertBack]` method reference form: no accessible static method exists on the anchor type (DeclaredOnly semantics; accessibility whitelist: `public`, `internal` within same assembly, `protected internal` within same assembly). |
| NCMAP036   | ❌ Error | `[Convert]`/`[ConvertBack]` method reference form: multiple overloads with identical signature templates are found (ambiguity; intentionally no overload selection mechanism based on signature is provided). |
| NCMAP037   | ❌ Error | `[Convert]`/`[ConvertBack]` method reference form: no overload matches any of the four templates (non-generic static, no optional/`params`/`ref`/`out`/`in` parameters, return type matches target property type, parameter types exactly match). |
| NCMAP038   | ❌ Error | `[BeforeMapping]`/`[AfterMapping]` hook methods are unresolved: no accessible static method exists on the anchor type (must be `public` or `internal` within same assembly); if declared on a host type outside the annotated DTO, use the two-parameter constructor form to explicitly specify the host. |
| NCMAP039   | ❌ Error | `[BeforeMapping]`/`[AfterMapping]` hook methods violate the `void(TSource, TTarget)` contract (non-`void` return, ≠2 parameters, parameter types incompatible with the mapping pair, or anchor type is an open generic). |
| NCMAP040   | ❌ Error | `[BeforeMapping]`/`[AfterMapping]` hook methods: multiple accessible overloads compatible with the mapping pair signature exist without an exact match (ambiguity; intentionally no overload selection mechanism based on signature is provided). |
| NCMAP041   | ❌ Error | `[MapWhen]` predicate method resolution failed: method does not exist / ambiguous overloads / not static / insufficient visibility / signature mismatch (single parameter must be either source field type `T1` or source entity type `T2`). |
| NCMAP043   | ❌ Error | `[MapFormat]` target property type is not `string` (formatted output must be textual); change to `string` property or use `[Convert]` instead. |
| NCMAP044   | ❌ Error | `[MapFormat]` source property type does not implement `IFormattable` (supported: `DateTime`/`DateTimeOffset`/`TimeSpan`/`Guid`/numeric types/enums, including `Nullable` wrappers; `string` sources are unsupported); use `[Convert]` instead. |
| NCMAP045   | ❌ Error | `[MapFormat]` and `[Convert]` are simultaneously annotated on the same property (semantically overlapping and mutually exclusive); choose one. |
| NCMAP046   | ⚠️ Warn  | `[MapFormat]` properties are not assigned in reverse mapping — formatted output is not reversibly parsable (lossy and culture-dependent); reverse mapping skips this property (entity retains original value). For round-trip support, declare `[ConvertBack]` manually. |
| NCMAP047   | ❌ Error | In `[MapNoAutoMatch]` whitelist mode, a property carries transformation attributes (`[Convert]`/`[Mask]`/`[MapFormat]`/`[MapWhen]`) but lacks `[MapProperty]` on the same property — the whitelist maps only explicitly declared properties, so this property is excluded entirely. Minimal example: `[MapFrom(typeof(U))] [MapNoAutoMatch] class UDto { [Convert(typeof(C))] public string Name { get; set; } }` → triggers this diagnostic (`Name` lacks `[MapProperty]`). Two solutions: add `[MapProperty]` to explicitly declare it, or remove the transformation attributes. |
| NCMAP_F55  | ⚠️ Warn  | Fluent multi-segment target paths are unsupported (e.g., `Map(d => d.Address.City, ...)` has no implementation yet); such declarations are skipped. |
| NCMAP_F18  | ❌ Error | `ConvertBack` reverse write-back target does not exist: the entity type lacks a property corresponding to the anchor (write-back target resolution follows three-tier order: “same name → auto-flattened leaf → lambda body”; all tiers miss, or lambda body violates narrowing rules — only single-level member access on parameters or direct field parameter usage is recognized; compound expressions are not inferred). Solutions: add the property to the entity type, rename the anchor, remove the `ConvertBack` declaration, or for name mismatches, use lambda form to explicitly indicate the write-back target. |
| NCMAP_F56  | ❌ Error | Fluent property argument is not a lambda expression (e.g., method group / delegate variable — though compilable, they are invalid); entire declaration is discarded, with explicit compile-time error. |
| NCMAP_F57  | ⚠️ Warn  | Fluent lambda contains statement blocks or ternary expressions, preventing reduction to a property path; declaration is skipped (`MapWhen`/`Convert` inline predicates are handled by inline transcribers and unaffected). |
| NCMAP_F58  | ❌ Error | Fluent arguments cannot be statically evaluated (e.g., `overrideIgnore`/direction parameters passed as variables); such arguments are ignored and default values apply (declaration proceeds normally; mapping behavior remains unchanged). |
| NCMAP_F59  | ❌ Error | True conflict on the same anchor property: duplicate declarations in the same slot, or simultaneous mapping and `Ignore` declarations — true conflicts are rejected outright. Retain one or combine per conflict checklist (semantic revision as of 2026-10-09: original “later-write-overrides-earlier” warning narrowed to “only true conflicts raise errors”; severity changed from Warn → Error). |
| NCMAP_F60  | ⚠️ Warn  | Unrecognized method names appear in Fluent chain (e.g., inherited `object` members — compilable but non-terminal calls); the node declaration is skipped (entire chain is discarded in main-chain scenarios). |
| NCMAP_F62  | ⚠️ Warn  | Fluent lambda source chain member access depth exceeds limit (16 segments); deep-chain declarations are discarded, suggesting intermediate projection splitting or source model flattening. |
| NCMAP_F63  | ⚠️ Warn  | Cycle detection visit budget exhausted (DFS visits from a single starting point exceed limit); cycle detection result for this start point remains undetermined (no `NCMAP029` emitted, no downgrade marked); only this diagnostic reports it. |
| NCMAP_F64  | ❌ Error | Fluent `.Format` target property type is not `string`; change to `string` property or use `.Convert` instead. |
| NCMAP_F65  | ❌ Error | Fluent `.Format` bound source property does not implement `IFormattable` (supported: `DateTime`/`DateTimeOffset`/`TimeSpan`/`Guid`/numeric types/enums, including `Nullable` wrappers); use `.Convert` instead. |
| NCMAP_F66  | ❌ Error | Fluent `.Format` and `.Convert` both target the same property (semantically overlapping and mutually exclusive); choose one. |
| NCMAP_F67  | ⚠️ Warn  | Fluent `.Format` property is not assigned in reverse mapping — formatted output is not reversibly parsable (lossy and culture-dependent); reverse mapping skips this property (entity property retains default value). For round-trip support, declare reverse conversion manually: two options — if entity has same-named property, use `.ConvertBack(converter)`; if names differ, use lambda form `.ConvertBack<TEntityProp>((dto) => ...)` where write-back target is resolved from member access on lambda parameter (no name-based lookup). |
| NCMAP_F68  | ⚠️ Warn  | Member segment has no following declaration (bare `Member`) — please add source (`From` / `FromSource`), ignore (`Ignore`), or transformation (`Mask` / `Format`, etc.). |
| NCMAP_F69  | ❌ Error | Member segment omits source and source type (or `FromSource` sub-source) lacks a member with the same name — old syntax silently dropped such declarations; new behavior raises explicit error. Use `.From(...)` to specify source. |
| NCMAP_F70  | ❌ Error | Duplicate declaration for the same property (duplicate in same slot — source slot / transformation slot each allow only one); reports first and duplicate declaration kinds; retain one. |
| NCMAP_F71  | ❌ Error | `MapWhen` whole-source form type argument matches neither target property type nor source type — whole-source form uses `.MapWhen<SourceType>(...)`; field-value form (condition parameter = target property type) omits type argument. |
| NCMAP099   | ⚠️ Warn  | Single mapping source generation failed (other mappings unaffected; falls back to runtime).                                                |

> Full diagnostic code set: `NCMAP001`–`NCMAP047` (`NCMAP004` is reserved and unused) + `NCMAP099` + Fluent family `NCMAP_F02`–`NCMAP_F71` (`F61` is reserved and unused); among these, `NCMAP014/015/019/025/031/035/036/037/038/039/040/041/043/044/045/047` and Fluent family `F11/F12/F14/F16/F17/F18/F19/F20/F21/F23/F24/F25/F26/F27/F29/F30/F31/F32/F35/F36/F37/F38/F39/F40/F41/F42/F43/F44/F45/F46/F47/F48/F49/F51/F52/F53/F56/F58/F59/F64/F65/F66/F69/F70/F71` are Error-level diagnostics.

> Fluent channel diagnostic family `NCMAP_F02`–`NCMAP_F71` (Fluent declaration surface; `F46`–`F50` = `MapSubclass` family; `F20`/`F26` residual due to transcription engine narrowing to closure classes; `F55`–`F58` = silent semantic governance family; `F59`/`F60`/`F62`/`F63` = chain parsing explicitness family; `F64`–`F67` = `Format` terminal family; `F68`–`F71` = `Member` three-segment safeguard family — `F68`: bare field warning; `F69`: omitted source with no matching name / `F70`: same-slot duplicate / `F71`: `MapWhen` type argument mismatch = Error): individual entries distributed as “main table + pagination redirection” — most codes in this family (`F02`–`F45`, `F51`–`F54`, `F59`, `F60`, `F64`–`F71`) appear individually in the [Fluent Entry](../how-to/fluent/basics.md) diagnostic table; `F46`–`F50` = `MapSubclass` family appears individually in the `MapSubclass` dedicated diagnostic table under [terminals](../how-to/fluent/terminals.md); `F55`–`F58`, `F62`, `F63` appear individually in respective rows above in this main table. Attribute-side dual-channel parsing family `NCMAP038`–`NCMAP041` (lifecycle hooks / conditional guards) appears in table entries; `NCMAP043`–`NCMAP046` = `[MapFormat]` attribute family appears in table entries; `NCMAP047` = `[MapNoAutoMatch]` whitelist gate (transformation attributes missing `[MapProperty]` header declaration) appears in table entry.

## Three Principles and Corresponding Detectors

Each of the three fundamental rules governing mapping declarations has a corresponding compile-time detector (a violation of any rule always triggers a compile-time diagnostic—“principle-detector pairing”). The formal definitions and design intent of the three principles are detailed in [Mapping Rules (Semantics) §6](../explanation/semantics/mapping-rules.md):

| Rule | One-Sentence Summary | Detector |
|------|----------------------|----------|
| P1 Channel Exclusivity | A given type pair (regardless of direction) may be declared *only* via either an attribute-based channel *or* a Fluent channel; different type pairs may freely mix both channels. | `NCMAP_F11` (Dual Declaration Exclusivity) · `NCMAP025` (Unordered Mutual Reference) |
| P2 Side Exclusivity | A class may be annotated with `[MapFrom]` at most once (duplicate annotations trigger compile-time error CS0579); `For<A, B>` and `For<B, A>` must not be declared simultaneously. | `NCMAP025` · CS0579 |
| P3 Configuration Anchoring to Class | Property-level mapping configuration attributes (eleven in total) take effect *only* when the declaring class has a mapping anchor (`[MapFrom]` or `For<S,T>()`); configurations without such an anchor are ignored entirely. | `NCMAP019` (Error, “eleven-member family” includes `[MapFormat]`/`[MapNoAutoMatch]`) · `NCMAP042` (Info, Dto naming heuristic only) |


---

## How to Handle Diagnostics

1. **Warnings do not block compilation**, but it is recommended to address all of them (to avoid unexpected runtime behavior).
2. **Errors block compilation** and must be fixed.
3. **Infos are purely informational** and do not block compilation (used to flag observable known limitations or degraded behaviors).
4. Per-code suppression: add `<NoWarn>NCMAP001</NoWarn>` to the project file (suppression is *not allowed* for Error-level diagnostics).
5. When a diagnostic is confirmed as a false positive or is already handled elsewhere, suppress it individually and include a comment explaining the rationale.

---

## Related Pages

- [explanation/semantics/diagnostics-model](../explanation/semantics/diagnostics-model.md) — Diagnostic system model (two naming families / three semantic levels / discipline of tripartite registration)
- [Validation and Diagnostics](../how-to/infrastructure/validation.md) — Runtime validation, tracing, and exception diagnostics operations
- [reference/api-comparison](../reference/api-comparison.md) — Mapping between dual API surfaces and their diagnostic-family affiliations
