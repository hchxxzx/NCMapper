> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: explanation/semantics/direction-semantics
Diagnostic codes involved: NCMAP013, NCMAP025, NCMAP031, NCMAP032, NCMAP033, NCMAP034, NCMAP046, NCMAP_F67  
Library version: v1.0.0+  
Last updated: 2026-09-30  
---

# Semantic Sharing Layer: Bidirectional Direction Semantics

> This page is the **single-source-of-truth** for direction semantics: both API surfaces (attribute-based and Fluent) reference this page for guidance. For the full expansion of reverse mapping—including all six generated overloads and migration guidance—see [Reverse Mapping Semantics](../reverse-mapping-semantics.md).

## 1. Direction Is Determined by Declaration Location

The `[MapFrom(typeof(Source))]` attribute is applied to the **target type (DTO)**, conveying the semantic meaning “target maps *from* source.” This defines bidirectionality as follows:

| Direction | Start | End | Generated Artifacts |
|-----------|-------|-----|---------------------|
| Forward (`ToDto`) | Entity | DTO | e.g., `entity.MapToXxxDto()` |
| Reverse (`ToEntity`) | DTO | Entity | e.g., `dto.MapToXxxEntity()` (all six reverse overloads are uniformly generated) |

In the Fluent API, a pair is declared via `cfg.For<Source, Target>()`. Its direction semantics align exactly with those of the attribute-based API.

## 2. Final Bidirectionality Principle (D8)

`[MapFrom]` expresses **bidirectional semantics**: a single declaration simultaneously defines *both* forward and reverse mapping relationships. Splitting declarations by direction is neither required nor supported. Supporting rulings include:

- `[Map]` itself is bidirectional and position-insensitive (swapping Entity/DTO does not affect behavior), *except* in mutual-reference scenarios.
- **Mutual double-true referencing** (i.e., A declares `[MapFrom(B)]` *and* B declares `[MapFrom(A)]`) constitutes a contradictory declaration: it triggers `NCMAP025` (error-level), blocking generation of that pair. Removing one side restores validity.
- Similarly, declaring the same pair via *both* attributes and Fluent configuration is mutually exclusive (`NCMAP_F11`).

## 3. Asymmetries in Reverse Mapping

Reverse mapping is *not* a mirror of forward mapping; the following behaviors are intentionally asymmetric:

| Topic | Forward | Reverse |
|-------|---------|---------|
| Custom conversion | `[Convert]` takes effect | **Not consumed by default** (`NCMAP033` Info); use `[ConvertBack]` to enable reverse conversion |
| Data masking | `[Mask]` masks target `string` values | Masked values are **written back verbatim** to the entity (`NCMAP034` Info; lossy and irreversible; prefer `[MapIgnore(ToEntity)]`) |
| Formatting | `[MapFormat]` / `.Format` converts source `IFormattable` → target `string` | **No assignment occurs** (`NCMAP046`/`F67` Warn; entity-side value remains unchanged, other fields are written back normally)—like masking, this is a *unidirectional*, *lossy* presentation directive; the library does *not* attempt reverse parsing. To support round-trip formatting, supplement with `[ConvertBack]` / `.ConvertBack` (when combined, reverse conversion proceeds normally without warnings) |
| `init-only` properties | Writable during creation path | Skipped during update path (`NCMAP032` Info) |
| Nested source fields | Participate in composition | Excluded from write-back (only top-level source fields participate) |

## 4. Direction-Aware Ignoring

`[MapIgnore]` supports direction parameters (`ToDto`/`ToEntity`, or bidirectional by default). In the Fluent API, the equivalent is `.Member(d => d.X).Ignore(direction)` (direction parameter is optional within the Member clause). Conflicts between ignore directives and converter declarations on the same property (e.g., `Ignore` + `Convert` or `ConvertBack`) trigger `NCMAP_F17`/`F23`.

## 5. Further Reading

- [Reverse Mapping Semantics](../reverse-mapping-semantics.md) (full expansion of reverse mapping semantics)
- [diagnostics/rules.md](../../diagnostics/rules.md) (**Primary diagnostic code reference**; the codes referenced here — `NCMAP013`, `NCMAP025`, `NCMAP031`, `NCMAP032`, `NCMAP033`, `NCMAP034`, `NCMAP046`, and `NCMAP_F67` — are documented either directly on this page or on its linked subpages)
