> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: reference/api-comparison
Library Version: v1.0.0+
Last Updated: 2026-09-30
---

# Fluent vs. Attribute Dual-API Comparison

> This page is the **sole authoritative reference** for API selection and migration across both APIs. All equivalences align precisely with the official [Fluent vs. `[MapFrom]` Channel Comparison](../how-to/fluent/basics.md). For a high-level overview of selection criteria, see also [getting-started/overview](../getting-started/overview.md).

## 1. 30-Second Selection Guide

| Your Scenario | Recommendation |
| -------------- | ---------------- |
| DTOs already exist and you prefer zero intrusion (i.e., no pollution of DTO classes) | **Fluent Channel**: Declare mappings centrally in a `MapperConfig` class using the `Configure` method. |
| You prefer “what-you-see-is-what-you-get” attribute-based declaration directly on DTOs, and your team follows an attribute-centric style | **Attribute Channel**: Use the `[MapFrom]` family of attributes. |
| AOT / trimmed deployment required; compile-time determinism is critical | Both channels use **compile-time source generation with zero runtime difference** — choose based on *declaration location preference*: use Fluent if you want DTOs to remain completely unmodified (centralized configuration), or Attribute if you prefer annotations directly on types. |
| Constructor parameter binding (`MapCtorParam`) is required | **Fluent Channel only** (no equivalent attribute form exists). |
| Chained sequential dependencies or multi-terminal composition within the same chain is required | **Fluent Channel only** |

Both channels are **semantically equivalent and coexist** for the same mapping pair; however, declaring the same mapping pair in *both* channels is prohibited (`NCMAP_F11` conflict).

## 2. Equivalence Summary Table

| Dimension | Fluent Channel | `[MapFrom]` Channel |
| --------- | ---------------- | ------------------- |
| Entry Point | Mapper configuration class + `Configure` method | Attributes applied directly on DTO classes |
| DTO Class Pollution | No (intent is centralized in the Mapper class) | Yes (attributes are placed directly on DTOs) |
| Configuration Granularity | Imperative, fluent chaining (property-level + order-aware) | Property-level declarative statements |
| Sequential Dependencies | Supported (natural ordering via chained calls) | Not supported |
| Forward Custom Conversion | `.Convert` (4 template forms, equivalent to attribute side; inline within Member scope) | `[Convert]` (4 forms) |
| Reverse Custom Conversion | `.ConvertBack` (4 template forms, equivalent to attribute side; inline within Member scope) | `[ConvertBack]` (4 forms) |
| Directional Ignoring | `.Ignore(direction)` (within Member scope; equivalent to attribute side) | `[MapIgnore(Direction)]` |
| Lifecycle Hooks | `.BeforeMapping` / `.AfterMapping` (equivalent in both channels) | `[BeforeMapping]` / `[AfterMapping]` (class-scoped) |
| Conditional Guards | `.MapWhen` (supports both named and inline transcription forms) | `[MapWhen]` (property-scoped, named form only) |
| Property-Level Formatting | `.Format(format)` (within Member scope; semantic mirror — both channels share identical snapshot semantics; `OverrideIgnore`/`overrideIgnore` parameters are consistent across both sides of the override gate) | `[MapFormat("format-string")]` (source `IFormattable` → target `string`) |
| Constructor Parameter Binding | `.MapCtorParam` (**Fluent-only**) | **No attribute equivalent** |
| Polymorphic Subtype Mapping | `.MapSubclass<TSrcDerived, TTargetDerived>()` (pair-level, equivalent to attribute side) | `[MapSubclass]` (class-scoped, supports multiple instances) |
| JSON Unfolding | `.FromJson()` (within Member scope; equivalent to attribute side) | `[MapProperty(IsFromJson = true)]` |
| Cross-Source Binding (`FromSource`) | `.Member(d => d.X).FromSource<SubSource>(s => s.Y)` (shorthand) / `.FromSource<SubSource>(cfg => …)` (pair-level grouped block) | `[MapProperty(FromSource = typeof(SubSource))]` |
| Disabling Auto-Include (ORM Navigation) | `NoAutoInclude` (field-level, see [fluent-index](fluent-index.md)) / `.NoAutoMatch()` (pair-level allowlist toggle) | `[MapNoAutoInclude]` (property-level) / `[MapNoAutoMatch]` (class-level) |
| Tree Declaration (consumed by `ToTreeList`) | `.Tree()` / `.Tree(id, parent, children)` (pair-level mount triplet); recursive property-level mapping via `.Member(d => d.X).MapTree()` (**no attribute equivalent at property level**) | `[MapTree]` (class-level mount triplet) |
| Global Configuration | No dedicated Fluent terminal (global config is shared across runtime; read-only view via `Mapper.GetGlobalConfig()`) | `[assembly: MapDefaults]` (assembly-level: naming strategy / ignore list) |
| Compile-Time Diagnostics | `NCMAP_F` series | `NCMAP0xx` |

> The above table reflects the authoritative specification. Engine-level semantics (matching rules, directionality, diagnostic model) are unified and sourced from a single shared semantic layer: [Mapping Rules](../explanation/semantics/mapping-rules.md), [Bidirectional Direction Semantics](../explanation/semantics/direction-semantics.md), and [Diagnostic Code System Model](../explanation/semantics/diagnostics-model.md). For architectural insights into why both channels coexist—and how they converge and enforce mutual exclusivity at compile time—see [Dual-Channel Architecture](../explanation/dual-channel.md).

## 3. Differentiated Positioning

- **Fluent Channel (Primary Path)**: Highest expressive power (sequential dependencies, inline lambda transcription, `MapCtorParam`, intra-chain composition); intent remains centralized without polluting DTOs; new features are documented by default in the Fluent channel.
- **Attribute Channel (Secondary Path)**: Source generator entry point at compile time; self-descriptive DTOs; recommended for AOT/zero-reflection scenarios; bidirectional semantics of `[MapFrom]` are declared once and cover both forward and reverse directions.

## 4. Migration Guidance

To migrate from mixed-style pages (where both Fluent and Attribute syntax appear side-by-side) to a single-channel organization:

1. Determine the primary channel using the **30-Second Selection Guide**;
2. Replace each equivalent declaration row-by-row per the table above (equivalence IDs like `A-xx` serve as migration reconciliation keys);
3. Retain Attribute-only constructs (e.g., `[assembly: MapDefaults]` global configuration) on the Attribute side — do *not* force-translate them into Fluent;
4. Duplicate declarations for the same mapping pair trigger `NCMAP_F11`; after migration, delete the declaration from the other channel.
