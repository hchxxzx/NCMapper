> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: fluent/hooks  
Diagnostic codes involved: NCMAP_F12, F14, F35–F39, NCMAP038–040  
Library version: Unreleased (to be released with the next version)  
Last updated: 2026-09-30  
---

# Fluent Hooks: Lifecycle Hooks

> Dedicated documentation for Fluent mapping lifecycle hooks (`BeforeMapping`/`AfterMapping`), including dual-channel equivalence with attribute-based declarations. For a quick reference of attributes, see [attributes-index](../../reference/attributes-index.md); for projection degradation behavior, see [ORM Degradation Mechanism](../../explanation/orm-degradation.md).

Two lifecycle hooks are available in the mapping pipeline:  
- `BeforeMapping` is invoked **after the target instance is constructed but before the first property assignment** (property initializer values are visible and may be preprocessed by the hook).  
- `AfterMapping` is invoked **after all property assignments have completed**.  

Fluent-style and attribute-based `[BeforeMapping]`/`[AfterMapping]` declarations populate the same snapshot-backed fields (dual-channel equivalence).

**Fluent-style syntax**:

| Terminal | Signature | Execution Timing |
|----------|-----------|------------------|
| `BeforeMapping(handler)` | `Action<TSource, TTarget>` method group | Before the first property assignment (fired only for forward mappings; not fired for any of the 4 reverse-mapping forms) |
| `AfterMapping(handler)` | `Action<TSource, TTarget>` method group | At the end of the shared core method (inherited by all 6 forward-mapping forms; not fired for any of the 4 reverse-mapping forms) |

The `handler` must be a **statically resolvable method group**: the target method must be `public` or `internal static`, and visible to generated code (i.e., within the same assembly). Lambdas, closures, or methods capturing variables lack a statically addressable entry point.

**Attribute-based syntax** (class-level annotation; `Inherited = false`: derived DTOs do *not* inherit hooks — explicit re-annotation is required; consistent scope with `[MapTree]`):

[!code-csharp[Attribute-based dual hooks (two-parameter host form + \[MapFrom\] anchoring)](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto)]

[!code-csharp[Attribute-based dual hooks (two-parameter host form + \[MapFrom\] anchoring) · Id](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Id)]

[!code-csharp[Attribute-based dual hooks (two-parameter host form + \[MapFrom\] anchoring) · Title](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Title)]

[!code-csharp[Attribute-based dual hooks (two-parameter host form + \[MapFrom\] anchoring) · Qty](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Qty)]

[!code-csharp[Attribute-based dual hooks (two-parameter host form + \[MapFrom\] anchoring) · NormalizedTitle](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_NormalizedTitle)]

[!code-csharp[Attribute-based dual hooks (two-parameter host form + \[MapFrom\] anchoring) · Total](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Total)]

- **Two-parameter constructor**: `[BeforeMapping(typeof(HookHost), nameof(HookHost.AttrNormalize))]` — statically locates a static callback method on the specified type (recommended pattern; enables centralizing reusable methods in a common host class across entities, aligning stylistically with `[Convert(typeof(X), nameof(X.M))]`).  
- **One-parameter constructor**: `[BeforeMapping(nameof(Normalize))]` — resolves within the scope of the DTO class annotated with the attribute (including all `partial` parts, but excluding base-class inheritance and outer nested types). If `nameof(OtherType.M)` is used, only the method name is retained; if the method resides outside the DTO, the two-parameter form *must* be used — otherwise `NCMAP038` is reported.  
- **Hooks activate per DTO and per signature pair**: The second parameter of the method signature is the DTO type for that pair (the signature itself declares applicability). A hook fires only for mapping pairs `(TSource, TTarget)` whose signatures match (including purely Fluent-declared pairs, injected via cross-pair propagation); non-matching signatures neither fire nor emit diagnostics.

Implementation of the host methods referenced by the two-parameter constructor (`HookHost` supports both hooks; verified via `dotnet run --scenario 104`; hooks write to **derived fields** — i.e., properties on the DTO side with names matching no source-side property; mapping assignments affect only source-backed properties and never overwrite hook outputs):

[!code-csharp[HookHost dual-hook methods (AttrNormalize/AttrComputeTotal)](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_HookHostAttrMethods)]

[!code-csharp[HookHost dual-hook methods (AttrNormalize/AttrComputeTotal) · AttrComputeTotal](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_HookHostAttrMethods_AttrComputeTotal)]

- **`AttrNormalize` (pre-hook)**: Executes after target construction but before the first property assignment — trims and uppercases `src.Title`, writing the result into the derived field `dst.NormalizedTitle`.  
- **`AttrComputeTotal` (post-hook)**: Executes after all property assignments — computes `dst.Total` as `src.Qty * src.UnitPrice`.

**Key points**:

- **Derived-field pattern**: Hooks write to derived fields on the DTO side (e.g., `NormalizedTitle`, `Total`) whose names match no source-side property. Mapping assignments affect only source-backed properties and never overwrite hook outputs (this is the pattern demonstrated in the runnable example).  
- **PATCH semantics**: In `_Update`-style mappings (reusing an existing target object), `BeforeMapping` observes the *old* `dst` values.  
- **Constructor-integrated targets**: Targets constructed via parameterized or `init-only` constructors (where construction and assignment are inseparable) provide no insertion point for `BeforeMapping`, triggering diagnostic `NCMAP_F37` (reported identically on both Fluent and attribute sides; see table below).  
- **Hook conflicts within the same mapping pair**: Declaring the *same kind* of hook (e.g., `Before×Before` or `After×After`) on both channels for the same mapping pair triggers `NCMAP_F39`; cross-combinations (e.g., attribute `After` + Fluent `Before`) are *not* considered conflicting.

**Diagnostics** (trigger conditions and remediation):

| Code | Trigger Condition | Remediation |
|------|-------------------|-------------|
| `NCMAP_F12` | `AfterMapping` target method is `private` | Promote to `public` or `internal static` |
| `NCMAP_F14` | `AfterMapping` target is not a statically resolvable method group (e.g., lambda, closure, or captured variable) | Extract into a named static method and reference it via method group |
| `NCMAP_F35` | `BeforeMapping` target method is `private` (counterpart to `NCMAP_F12` for `Before`) | Same as above |
| `NCMAP_F36` | `BeforeMapping` target is not a statically resolvable method group reference (e.g., lambda, closure, or captured variable) | Same as above |
| `NCMAP_F37` | Target type uses a parameterized or `init-only` constructor (no separation between construction and assignment → no `BeforeMapping` insertion point) | Switch to a target with writable properties, or remove the hook |
| `NCMAP_F38` | `BeforeMapping` declared inside a `FromSource` block (generated `Compose` pipeline has no hook injection point → declaration is never executed) | Declare hooks on the main mapping chain instead |
| `NCMAP_F39` | Same-kind hooks declared on both channels for the same mapping pair (`Before×Before` / `After×After`; cross-combinations are *not* conflicting) | Declare the hook on only one channel |
| `NCMAP038` | Attribute-based hook method is unresolved (no accessible static method exists on the anchored type; or method is declared outside the DTO but the one-parameter form was used) | Use the two-parameter form to explicitly specify the host type |
| `NCMAP039` | Attribute-based hook signature violates the `void(TSource, TTarget)` contract (e.g., open generic anchors unsupported) | Refine signature; for open-generic hosts, use the two-parameter form with a closed type |
| `NCMAP040` | Multiple accessible overloads of the attribute-based hook method exist that are compatible with the mapping pair signature but lack an exact match (ambiguity) | Narrow down to a single unambiguous method |

**Projection behavior**: Any mapping pair declaring *either* hook is forcibly degraded under ORM projections (nested projections skip generated code entirely, causing hooks to be lost — a semantic error). Hooks are categorized alongside `[Mask]`/`[Convert]` for degradation purposes. See [ORM Degradation Mechanism §2.3](../../explanation/orm-degradation.md) for full details.

**Runnable example** (`dotnet run --scenario 104` validates execution; includes Fluent dual-hook chain + attribute-based dual hooks + cross-pair signature filtering):

[!code-csharp[Fluent dual-hook chain (chained `Before` + `After` method groups)](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_HookFluentMapper)]

[!code-csharp[Fluent dual-hook chain (chained `Before` + `After` method groups) · Configure](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_HookFluentMapper_Configure)]

Scenario output verifies correct phase ordering (`Before` → assignment → `After`), presence of derived-field outputs, preservation of mapped properties, and observation of old `dst` values in `_Update` mode:

[!code-csharp[Hook phase assertions (four sub-scenarios in scenario 104)](~/samples/NCMapper.Samples/01-Fluent/FluentHooksScenarios.cs#FluentHooks_Run)]
