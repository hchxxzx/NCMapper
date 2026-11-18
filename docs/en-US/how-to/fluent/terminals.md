> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: fluent/terminals  
Diagnostic Codes Involved: NCMAP_F40-F45, F46-F54, NCMAP041  
Library Version: Unreleased (to be released with the next version)  
Last Updated: 2026-10-23  
---

# Fluent Four-Terminal Operators: `MapWhen`, `MapCtorParam`, `MapSubclass`

> Advanced Fluent terminal operators — conditional guarding, constructor parameter binding, and polymorphic subtype dispatch. For authoritative definitions of channel asymmetry and feature-side equivalence of the four terminals, see [api-comparison](../../reference/api-comparison.md); `MapCtorParam` is Fluent-exclusive (no feature-side counterpart exists).

## `MapWhen`: Conditional Guard

`MapWhen` is a three-part configuration terminal for `.Member`: it declares a **conditional guard** on the target field anchored by `.Member`. The mapping assignment for that field executes *only* when the predicate evaluates to `true` ("true → map", hence the name’s polarity). Guards may anchor onto two kinds of source mapping carriers: auto-matched same-named source properties, or explicitly declared `From` sources. It composes legally with companion terminals (`Convert`, `Mask`) within the same member segment.

**Two overloads correspond to two predicate parameter shapes**, both attached to the `MemberHead`/`MemberConfig` member segment:

| Overload | Predicate Parameter | Shape |
| --- | --- | --- |
| `MapWhen(Func<TProp, bool>)` | Source field value (the source value mapped to the anchored field; `TProp` is already fixed by the `Member` anchor) | T1 — Field Value (most common; **non-generic** — inline lambdas work directly) |
| `MapWhen<TInput>(Func<TInput, bool>)` | Entire source object (`TInput` = `TSource`) | T2 — Full Source (multi-field evaluation; **explicit type argument required** — inline lambdas lack inference context; write `.MapWhen<SourceType>(...)`; supplying a type argument neither equal to `TProp` nor `TSource` triggers `NCMAP_F71`) |

- **Shape A (named static method group)**: `public/internal static bool M(T)` (visible from the same assembly as generated code) — use `MapWhen(M)` for T1 (parameter type = source field type), or `MapWhen<TSource>(M)` for T2 (parameter type = source entity type);  
- **Shape B (capture-free inline lambda)**: expression-bodied lambda; the transcription engine rewrites it using symbol-level fully qualified names and embeds it directly into generated code (no runtime delegate, AOT-friendly; T1 works directly, T2 requires explicit type argument);  
- **Feature-side**: `[MapWhen(nameof(HasContent))]` (property-level, `Inherited = true`, singleton instance): the predicate method is resolved *by name* on the **declaring type of the annotated property** (self-contained per-type resolution, consistent with the single-parameter form of `[Convert(string)]`); expressiveness is asymmetric: feature arguments must be compile-time constants, so only Shape A is supported; inline transcription (Shape B) is Fluent-exclusive.

**Key Points**:

- **Execution Order = Condition → Conversion → Assignment**: the condition sees the *pre-conversion* source field value; combining `From` + `MapWhen` in the same member segment is legal, and the predicate reads the raw source field value—not the converted result;  
- **False condition skips assignment**: newly constructed objects retain their initializer values (or `default` if none); `_Update` paths preserve existing destination values; constructor-mapped targets are explicitly assigned `default`;  
- **Single-condition constraint**: declaring `MapWhen` twice on the same property is disallowed (`F44`); use negated predicates for negative filtering, and combine multiple criteria inside a single predicate. **`IgnoreWhen`-style negative entry points are never implemented**: negation is semantically equivalent and handled entirely within the predicate — no second API surface is provided;  
- **Reverse mapping ignores this declaration**: guards apply *only* to forward mapping (Entity → DTO);  
- **PATCH idiom**: `.Member(d => d.Name).MapWhen(DtoRules.NotNull)` expresses “`null` = not provided” → skip assignment (guards skip mapping only; they do *not* distinguish “not provided” from “explicitly cleared”; precise semantics require caller-side pre-checking);  
- **`FromSource` blocks**: `MapWhen` on `Member` chains *inside* a `FromSource` block has real emission targets (sub-mappings inside the block perform actual assignments); `MapWhen` anchored on a property covered *only* via cross-source `FromSource` on the main chain triggers `NCMAP_F45`.

**Diagnostics** (Trigger Conditions & Remediation):

| Code | Trigger Condition | Remediation |
| --- | --- | --- |
| `NCMAP_F40` | Shape A method group contract violation (unresolvable / non-static / insufficient visibility / signature mismatch / missing implicit conversion) | Predicate must conform to `public/internal static bool M(T)`, where `T` is either the source field type or the source entity type |
| `NCMAP_F41` | Shape B inline lambda captures variables or accesses private members (transcription prohibition) | Extract into a named static method and switch to Shape A |
| `NCMAP_F42` | Shape B inline lambda contains statement bodies or disallowed AST nodes (e.g., `dynamic`, `unsafe`, `await`, query syntax; whitelist includes member chains, arithmetic/comparison, null-conditional access, static/instance invocations, nameliterals) | Same as above |
| `NCMAP_F43` | Anchored field has no mappable source (no auto-matched source and no explicit `From`, or excluded by `Ignore`; guard will never activate) | Fix field name (often a typo) or declare the mapping before attaching the guard |
| `NCMAP_F44` | Duplicate `MapWhen` declarations on the same property (single-condition rule) | Merge criteria into one predicate |
| `NCMAP_F45` | Main-chain `MapWhen` anchors a property covered *only* via cross-source `FromSource` (main-chain `Compose` does not expand per-property assignment chains; guard has no emission target) | Declare the guard *inside* the `FromSource` block |
| `NCMAP041` | Feature-side `[MapWhen]` predicate resolution failure (method not found / ambiguous overloads / non-static / insufficient visibility / signature mismatch / parameter type neither matches source field type nor source entity type) | Predicate must be declared on the type containing the annotated property; signature must match *exactly* one of the T1/T2 shapes |

**Projection Behavior**: Projection paths always degrade (guard predicates are CLR delegates and cannot be translated to SQL; classified under `MapWhen`, same category as `[Mask]`/`[Convert]`). See [ORM Degradation Mechanism §2.3](../../explanation/orm-degradation.md).

**Runnable Examples** (`dotnet run --scenario 105` for live validation; all four shapes — named method group, inline transcription, full-source, and same-anchor composition — coexist in one chain):

[!code-csharp[Four `MapWhen` Shapes in One Chain (Fluent)](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MapWhenFluentMapper)]

[!code-csharp[Four `MapWhen` Shapes in One Chain (Fluent) · Configure](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MapWhenFluentMapper_Configure)]

Feature-side property-level guard (predicate method declared on the DTO itself — self-contained per-type):

[!code-csharp[Feature-side `\[MapWhen\]` Property-Level Guard](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto)]

[!code-csharp[Feature-side `\[MapWhen\]` Property-Level Guard · Id](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Id)]

[!code-csharp[Feature-side `\[MapWhen\]` Property-Level Guard · Name](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Name)]

[!code-csharp[Feature-side `\[MapWhen\]` Property-Level Guard · Score](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Score)]

[!code-csharp[Feature-side `\[MapWhen\]` Property-Level Guard · HasContent](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_HasContent)]

Scenario output validates skip semantics: all-true → full mapping; false condition on new object → retains default; `_Update` with false condition → preserves existing value, true condition → updates normally; feature-side behavior is isomorphic:

[!code-csharp[`MapWhen` Skip-Value Assertions (Scenario 105 — Four Sub-Scenarios)](~/samples/NCMapper.Samples/01-Fluent/MapWhenScenarios.cs#MapWhen_Run)]

Runnable demonstration of `MapWhen` inline lambda transcription (Scenario 108):

[!code-csharp[Capture-Free Inline Lambda Allowed (True/False Path Comparison)](~/samples/NCMapper.Samples/01-Fluent/InlineLambdaScenarios.cs#FluentInline_Passthrough)]

[!code-csharp[Captured Scenario Resolved via Static Method Group](~/samples/NCMapper.Samples/01-Fluent/InlineLambdaScenarios.cs#FluentInline_CapturedExtracted)]

## `MapCtorParam`: Explicit Constructor Parameter Binding

`.MapCtorParam` explicitly binds a source property value to a specified **parameter** of a public constructor on the target type. It applies when the target type has **no public parameterless constructor**: pure constructor parameters correspond to no target properties (immutable parameterized classes — no writable destination for `.Member`, and auto-name matching fails), leaving a binding gap filled by two-point injection.

**Two Shapes**:

| Shape | Signature | Source Anchoring |
| --- | --- | --- |
| Shape 1 (Primary, Recommended) | `MapCtorParam<TSrcProp>(string paramName, Expression<Func<TSource, TSrcProp>> sourceProp)` | Single-segment pure property-path lambda; `TSrcProp` inferred from lambda return type; multi-segment chains/method calls (non-pure paths) are rejected per existing lambda diagnostics |
| Shape 2 (Secondary) | `MapCtorParam(string paramName, string sourcePropertyName)` | Source property name as string (suitable for dynamic/no-type-context scenarios); misspelled names or missing public getters trigger `NCMAP_F51` at generation time (fail-fast — no silent null binding) |

**Key Points (Two-Point Injection)**:

- **Constructor Selector Weighting**: candidate public constructors whose parameter names match the bound parameter name receive priority; ties are broken mechanically by “most parameters first”;  
- **Parameter Matching Priority**: during construction, constructor arguments are resolved first from the explicit binding table, then fall back to same-name matching;  
- Parameter name matching is **case-insensitive**; `ref`/pointer parameters are *not bindable*;  
- **`record` Boundary Case**: `record` positional parameters correspond to same-named `init` properties; existing `.Member(d => d.X).From(...)` declarations suffice to feed the constructor (matching key = target property name); `MapCtorParam` is a convenience supplement for “no-target-property anchoring”, *not* a requirement for `record`s;  
- **Channel Asymmetry (Fluent-Exclusive)**: no feature-side counterpart exists (declarative attributes cannot express two-point constructor parameter injection; `PublicApi` lacks `MapCtorParamAttribute`). DTOs declared via `[MapFrom]` channel *cannot* declare this terminal; if binding is needed, move the pair to Fluent-channel declaration (dual-channel declaration for the same pair is mutually exclusive, triggering `NCMAP_F11`), or add same-named properties to the target for auto-matching.

**Diagnostics** (Trigger Conditions & Remediation):

| Code | Trigger Condition | Remediation |
| --- | --- | --- |
| `NCMAP_F51` | Bound source property does not exist or lacks a public readable getter (Shape 2 string parsing failure; Shape 1 non-pure path rejection under same rules) | Pass a single-segment lambda (Shape 1) or a valid public property name (Shape 2) |
| `NCMAP_F52` | Target type declares no public constructor accepting a bindable parameter with the given name (message lists available parameter names) | Correct the parameter name (case-insensitive); `ref`/pointer parameters are not bindable |
| `NCMAP_F53` | Duplicate binding to the same constructor parameter, or conflict between binding and an explicit `From` entry targeting the same parameter (contradictory declarations — fail-fast, no silent override) | Remove one declaration; for `record` name-mismatch cases, existing `.Member(...).From(...)` suffices |
| `NCMAP_F54` | Multiple public constructors tie for maximum parameter count (Info-level hint: selector silently picks by parameter count) | Use `.MapCtorParam` bindings to resolve constructor selection explicitly |

**Projection Behavior**: This terminal is *not* included in the untranslatable classification set (`ClassifyUntranslatableDeclaration` has no `MapCtorParam` category) and does *not* trigger projection degradation: pairs containing only pure binding declarations (plus pure `From`/`Ignore`) still project on the SQL side; degradation is triggered only if the same pair also contains `Mask`/`Convert`/`FromJson`/hooks/`MapWhen`/`FromSource` declarations — those determine degradation.

**Runnable Examples** (`dotnet run --scenario 106` for live validation; both shapes + `record` boundary clarification, including element-level usage with `.MapList`):

[!code-csharp[`MapCtorParam` Dual Shapes and `record` Boundary Clarification](~/samples/NCMapper.Samples/01-Fluent/CpModels.cs#CtorParam_CtorParamFluentMapper)]

[!code-csharp[`MapCtorParam` Dual Shapes and `record` Boundary Clarification · Configure](~/samples/NCMapper.Samples/01-Fluent/CpModels.cs#CtorParam_CtorParamFluentMapper_Configure)]

## MapSubclass: Polymorphic Subtype Mapping

`.MapSubclass<TSrcDerived, TTargetDerived>()` registers a pair of subtype mappings for the current (TSource, TTarget) **base-class mapping pair**. At runtime, the base-class mapping routes to the dedicated subclass mapping body based on the source instance’s **actual type**, using an `isinst` dispatch chain. This feature is designed for polymorphic list and polymorphic field scenarios where base-class entities map to base-class DTOs, and derived entities map to derived DTOs (including derived-specific fields).

**Syntax** (class-level terminal—applies to the entire pair, not individual properties):

| Declaration Side | Form | Anchoring Requirement |
| --- | --- | --- |
| Fluent | `cfg.For<TEntity, TBaseDto>().MapSubclass<TDerived, TDerivedDto>()` (supports chaining multiple pairs) | `cfg.For` anchors the base-class pair; the DTO side must *not* be annotated with `[MapFrom]` (dual-channel declaration within the same pair is mutually exclusive — error `NCMAP_F11`) |
| Attribute | `[MapFrom(typeof(TEntity))]` + `[MapSubclass(typeof(TDerived), typeof(TDerivedDto))]` applied to the base-class DTO (type-level, multi-instance: each instance registers one pair) | The annotated class must also carry `[MapFrom(typeof(BaseSource))]`; missing anchoring triggers `NCMAP_F49` |

**Dispatch Semantics** (consistent across the four families: `Map`, `_Update`, `MapList`, `MapListInto`):

- **Assignable Match**: A match occurs when `src is TSrcDerived`; if no exact match is registered, the dispatch falls back to the **closest registered ancestor level**.
- **Most-Derived-First Priority**: When multiple registrations match simultaneously, arbitration proceeds lexicographically by *(source-side inheritance distance, target-side inheritance distance)*. Partially overlapping registrations (e.g., same source type at different inheritance levels, or same source with different target types) are legal and do not trigger diagnostics.
- **Base-Class Fallback**: If no registered subclass matches—including unregistered third-party derived types—the base-class mapping body executes; subclass-specific fields retain their default values.
- **Dual-Instance Matching** (`_Update`/`MapListInto`): The subclass body is invoked only if *both* `src` and `dst` satisfy `isinst` against their respective registered subclass types. If `dst`’s runtime type is *not* a registered subclass target, execution falls back to the base-class body (subclass-specific fields are *not* written).
- **Reverse Mapping**: Each subclass pair automatically generates reverse mappings and reverse direct-call overloads via the reverse-registration pipeline (registrations are *not* one-way). Reverse base-class mappings do *not* participate in dispatch (the reverse target is an existing entity instance supplied by the caller); asymmetry is surfaced via `NCMAP_F50` (Info-level diagnostic).

**Inheritance Constraints and Boundaries**:

- **Two-Channel Constraint Enforcement**:  
  - Fluent-side generic constraints (`where TSrcDerived : TSource`) are enforced by the C# compiler at the call site (errors `CS0311`/`CS0314`).  
  - Attribute-side `typeof` arguments have *no compile-time constraint*; invalid inheritance relationships trigger `NCMAP_F46` during configuration extraction.
- `TTargetDerived == TTarget` (polymorphic source, converged target) is a valid use case.  
  `TSrcDerived == TSource` (registering the base class itself) causes infinite self-referential dispatch and is rejected with `NCMAP_F47`, as is duplicate registration of the *exact same* `(TSrcDerived, TTargetDerived)` pair.
- Registration fails with `NCMAP_F48` if the subclass target type has no public mappable surface or violates construction constraints (e.g., synthesized snapshot is skipped or yields zero property mappings).
- `[MapSubclass]` has `Inherited = false` (derived DTOs do *not* inherit superclass’s subclass registrations) and `AllowMultiple = true` (supports multiple instances registering multiple pairs).

**Diagnostics** (Trigger Conditions & Remediation):

| Code | Channel | Trigger Condition | Remediation |
| --- | --- | --- | --- |
| `NCMAP_F46` | Attribute-only | `[MapSubclass(typeof(S), typeof(T))]` arguments violate inheritance requirements: `S` must derive from the base-pair source type, and `T` must derive from the base-pair target type. (No generic constraints exist for attributes; this check runs during configuration extraction.) | Correct the `typeof` arguments. Equivalent Fluent errors are caught by the compiler (`CS0311`/`CS0314`) at the call site and *do not* emit this code. |
| `NCMAP_F47` | Universal | Duplicate registration of the *identical* `(TSrcDerived, TTargetDerived)` pair (first registration retained), or registration of `TSrcDerived == TSource` (self-registration causing infinite dispatch, explicitly disallowed). Partial overlaps are legal and resolved silently via most-derived-first arbitration. | Remove the duplicate or self-registration. |
| `NCMAP_F48` | Universal | Registered subclass pair yields no mappable output: the subclass target type lacks a public mappable surface or violates construction constraints (synthesized snapshot skipped or zero-property mapping). | Expose public mappable properties and ensure the subclass target is constructible. |
| `NCMAP_F49` | Attribute-only | `[MapSubclass]` is applied to a class lacking the required anchoring `[MapFrom(typeof(BaseSource))]`, meaning the base-class mapping pair does not exist and this registration is orphaned (unreferenced by any dispatch surface). | Add `[MapFrom(typeof(BaseSource))]`, or switch to the Fluent channel (`cfg.For` provides implicit anchoring). |
| `NCMAP_F50` | Universal (Info) | A registered `MapSubclass` pair exhibits silent behavior: `"reverse-asymmetry"` (reverse base-class mapping is generated but never dispatched, so subclass-specific fields remain unfilled) or `"dual-instance-fallback"` (dual-instance matching fell back to the base-class body). | Expected semantic behavior—no action required. To populate subclass fields, verify the runtime type of `dst`. |

**Projection Behavior**: Base-class mapping pairs registered with `MapSubclass` **always degrade** under ORM projection (the `isinst` dispatch chain cannot be translated to SQL; classified as `SubclassDispatch`). `[MapSubclass]` implements `IProjectionPolicy`, aligning its projection semantics with `[Mask]`, `[Convert]`, and `[MapWhen]`.

**Dual-Channel Consistency** (two distinct perspectives—do *not* conflate):  
- **Functional Symmetry**: Fluent `.MapSubclass` and attribute `[MapSubclass]` are semantically equivalent; both share identical dispatch logic.  
- **Diagnostic Asymmetry**: Error codes `F46` and `F49` are *attribute-only*. Equivalent Fluent errors are caught by the C# compiler (`CS0311`/`CS0314`) at the call site—these diagnostics are unreachable by the generator, and users should *never* expect to see `F46` or `F49` in Fluent usage.

**Runnable Example** (`dotnet run --scenario 107` for live validation; covers all four dispatch families, fallback, reverse mapping, and attribute-side registration):

[!code-csharp[Fluent class-level registration and attribute-side multi-instance registration](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto)]

[!code-csharp[Fluent class-level registration and attribute-side multi-instance registration · ScAttrAnimalDto_Name](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Name)]

[!code-csharp[Fluent class-level registration and attribute-side multi-instance registration · ScAttrAnimalDto_Weight](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Weight)]

[!code-csharp[Fluent class-level registration and attribute-side multi-instance registration · ScAttrDogDto](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrDogDto)]

[!code-csharp[Fluent class-level registration and attribute-side multi-instance registration · ScAttrDogDto_Breed](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrDogDto_Breed)]

[!code-csharp[Fluent class-level registration and attribute-side multi-instance registration · ScAttrCatDto](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrCatDto)]

[!code-csharp[Fluent class-level registration and attribute-side multi-instance registration · ScAttrCatDto_Indoor](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrCatDto_Indoor)]

[!code-csharp[Fluent class-level registration and attribute-side multi-instance registration · SubclassFluentMapper](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_SubclassFluentMapper)]

[!code-csharp[Fluent class-level registration and attribute-side multi-instance registration · SubclassFluentMapper_Configure](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_SubclassFluentMapper_Configure)]

[!code-csharp[Four-family dispatch, base-class fallback, and _Update dual-instance matching](~/samples/NCMapper.Samples/01-Fluent/SubclassScenarios.cs#MapSubclass_Dispatch)]

## `From` Terminal Family Composition and Runtime Metadata

The `.Member(dest).From(...)` terminal family supports multi-segment composition and cross-type binding (cross-type conversions are handled internally by generated code). Details of `Compose`-style cross-source declarations (shorthand + grouped blocks) are available at runtime via `RuntimeSnapshotStore.TryGetFluentDetailMeta` (corresponding to `reference/architecture-map.md` §6 `RuntimeComposeMapping`). All three composition forms—including detailed `Compose` metadata—are demonstrated and individually asserted in scenario 109 (`dotnet run --scenario 109`):

[!code-csharp[Three composition forms of the From terminal family and Compose cross-source declarations (Mapper configuration)](~/samples/NCMapper.Samples/01-Fluent/RuntimeComposeScenarios.cs#RuntimeCompose_Declarations)]

[!code-csharp[Three composition forms of the From terminal family and Compose cross-source declarations (Mapper configuration) · Configure](~/samples/NCMapper.Samples/01-Fluent/RuntimeComposeScenarios.cs#RuntimeCompose_Declarations_Configure)]

[!code-csharp[Scenario 109 full sub-scenario assertions and runtime metadata retrieval](~/samples/NCMapper.Samples/01-Fluent/RuntimeComposeScenarios.cs#RuntimeCompose_Run)]
