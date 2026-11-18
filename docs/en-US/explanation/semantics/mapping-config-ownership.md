> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: explanation/semantics/mapping-config-ownership
Library Version: v1.0.0+
Last Updated: 2026-10-10
---

# Three Principles of Mapping Configuration Ownership

> [!IMPORTANT]
> The following constitute the authoritative specification, carried forward into the official release.

NCMapper adheres to three foundational principles (plain-language version; formal definitions and design intent are detailed in [Mapping Rules (Semantics) §6](mapping-rules.md)) when consuming mapping configurations:

1. **No anchor → no recognition**: If a type is neither annotated with `[MapFrom]` nor declared as a mapping pair via Fluent `cfg.For<S,T>()`, NCMapper **ignores all eleven** mapping configuration attributes applied to that type — including `[MapProperty]`, `[MapIgnore]`, `[Mask]`, `[Convert]`, `[ConvertBack]`, `[MapWhen]`, `[MapFormat]`, `[MapNoAutoInclude]`, `[BeforeMapping]`, `[AfterMapping]`, and `[MapNoAutoMatch]`. Such configurations are neither consumed nor applied; mapping falls back to pure name-based matching.

2. **Compilation-time visibility**: Types bearing mapping configuration attributes but lacking an anchor do *not* fail silently. The source generator emits diagnostics at compile time:  
   - Types containing any mapping configuration attributes trigger **NCMAP019 (Error)**.  
   - Types matched *only* by DTO naming heuristics (e.g., ending in `Dto`/`DTO`) without explicit anchors trigger **NCMAP042 (Info)**.

3. **The declaring side is the sole registration surface**: Directional declarations (e.g., `[MapIgnore(Direction.ToEntity)]`) are registered *exclusively* on the declaring side — i.e., the side annotated with `[MapFrom]`.

## Two Anchor Channels & Attribute-Level Annotation Eligibility (Effective 2026-10-10)

“Having an anchor” is not a binary check — there are two distinct anchor channels, and **attribute-level annotation eligibility is determined solely by the *attribute anchor***:

| Hosting Mechanism | Anchor Determination | Attribute-Level Annotations (`[MapProperty]`, `[MapIgnore]`, `[Mask]`, `[Convert]`, `[ConvertBack]`, `[MapFormat]`) |
|---|---|---|
| Attribute Channel: DTO annotated with `[MapFrom]` | Attribute anchor = `true` | **Consumed** (consistently by both source generator and runtime fallback path) |
| Fluent Channel: `cfg.For<S,T>()` declared, DTO has *no* `[MapFrom]` | Attribute anchor = `false` (Fluent anchor only) | **Not consumed** — configure via Fluent instead (e.g., `.Member(...).From`, `.MapTree`, `.Mask`, `.Format`, `.FromJson`, `.Convert`) |
| Mixed: Both `[MapFrom]` *and* `cfg.For<S,T>()` present | Attribute anchor = `true` | **Consumed** (coexistence does *not* cancel either anchor) |
| No anchor | Both anchors = `false` | Not consumed + compile-time diagnostics: **NCMAP019 (Error)** or **NCMAP042 (Info)** |

Key points:

- **Fluent-declared pairs no longer “override” residual attribute annotations**: The runtime fallback path *does not* use attribute-level annotations to override Fluent declarations (the prior dual-engine divergence — “generator ignores / runtime reads” — has been resolved).
- Consistent semantics apply across ORM projections and degradation decisions: Residual attribute annotations on Fluent-declared pairs neither exclude columns from ORM projections nor independently trigger projection degradation.
- Exception: `[MapWhen]` is a *guard condition declaration* belonging exclusively to the attribute channel. Though ignored as mapping configuration on Fluent pairs, it **still triggers ORM projection degradation** (conservative semantics — ensures guard conditions are never silently discarded).
- Diagnostic classification: Attribute-level annotations on Fluent pairs appear in `MapTracer` reports as `⚠️ not consumed (Fluent pair — attribute ignored; use Fluent declarations)`, clearly distinguished from the `⚠️ invalid config` state for unanchored types.

## Remediation Paths (How to resolve NCMAP019 / NCMAP042)

| Diagnostic | Meaning | Remediation |
|------|------|----------|
| **NCMAP019 (Error)** | Type carries mapping configuration attributes but lacks a mapping declaration anchor | ① Annotate the type with `[MapFrom(typeof(SourceType))]` (attribute channel);<br>② Or declare the mapping pair in your `[MapperConfig]` class using `cfg.For<S, T>()` (Fluent channel);<br>③ If the attributes are truly redundant, remove them outright.<br>See full per-code-item guidance in [Diagnostic Rules](../../diagnostics/rules.md). |
| **NCMAP042 (Info)** | Type name ends in `Dto`/`DTO`, but no explicit mapping declaration exists (a hint that reflection-based fallback is unavailable under AOT or trimming) | ① Apply an anchor as above (the diagnostic disappears once generated code is available);<br>② Or confirm this type is intentionally excluded from mapping and safely ignore the message. |

## External Assemblies and Dynamic Types

- **Types from external assemblies** (e.g., DTOs/entities from third-party libraries whose source cannot be modified): Declare the mapping pair via the Fluent channel — `cfg.For<S, T>()`. No modification to the type itself is required. Compile-time diagnostics evaluate based on declared state, and behavior matches the attribute channel exactly.

- **Dynamic types** (e.g., built via `Reflection.Emit` or collectible assemblies): The reflection fallback path explicitly excludes dynamic types from caching. Mapping configuration attributes applied to dynamic types are **never consumed**, and no compile-time diagnostics are emitted for them (see [Bidirectional Mapping Semantics](../reverse-mapping-semantics.md) for details).
