> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: reference/attributes-index
Library Version: v1.0.0+
Last Updated: 2026-10-23
---

# Attribute Quick Reference

> Complete list of all attributes (17 total). For attribute channel system positioning and Fluent API equivalents, see [api-comparison](api-comparison.md); for engine-level semantics, refer to the Semantic Sharing Layer ([Mapping Rules](../explanation/semantics/mapping-rules.md), [Direction Semantics](../explanation/semantics/direction-semantics.md)).

## Pair-Level Declarations

| Attribute | Applicable Location | Purpose | Fluent Equivalent Terminal |
|-----------|---------------------|---------|----------------------------|
| `[MapFrom(typeof(Source))]` | DTO class | Declares a mapping pair (bidirectional semantics; one declaration covers both forward and reverse directions) | `cfg.For<Source, Target>()` |
| `[MapperConfig]` | Configuration class | Marks the Fluent configuration entry class (host of the `Configure` method) | (The Fluent channel entry itself) |
| `[MapDefaults]` | Assembly | Global default mapping configuration (naming strategy / ignore list) | No dedicated Fluent terminal (assembly-level declaration; read-only view accessed at runtime via `Mapper.GetGlobalConfig()`) |

## Property-Level Declarations

| Attribute | Applicable Location | Purpose | Fluent Equivalent Terminal |
|-----------|---------------------|---------|----------------------------|
| `[MapProperty("Source", "Target")]` | Target property | Maps properties with different names; for cross-table fields, use two-parameter form with `typeof(SubSource)` | `.Member(d => d.Target).From(s => s.Source)` |
| `[MapProperty(IsFromJson = true)]` | Target property | JSON field expansion (`string` ↔ object, bidirectional) | `.Member(d => d.X).FromJson()` |
| `[MapProperty(FromSource = typeof(SubSource))]` | Target property | Declares Compose-style cross-source binding | `.Member(d => d.X).FromSource<SubSource>(s => s.Y)` (shorthand) / `.FromSource<SubSource>(cfg => …)` (pair-level grouped block) |
| `[MapIgnore]` / `[MapIgnore(Direction)]` | Property | Directional ignore (bidirectional / ToDto / ToEntity) | `.Member(d => d.X).Ignore()` / `.Ignore(direction)` |
| `[Convert]` | Target property | Custom forward conversion (4 supported static method signatures) | `.Convert` (4 signature templates, within Member section) |
| `[ConvertBack]` | Target property | Custom reverse conversion (4 supported signatures) | `.ConvertBack` (4 signature templates, within Member section) |
| `[MapFormat("FormatString")]` | Target `string` property | Property-level formatting: source `IFormattable` is formatted into text using the format string (for dates, numbers, enums, etc.; forward-only declaration; reverse direction is not assigned — error `NCMAP046`; round-trip requires explicit `[ConvertBack]`; mutually exclusive with `[Convert]` — error `NCMAP045`) | `.Format(format)` (within Member section, supports `overrideIgnore` exemption flag) |
| `[Mask(typeof(MaskImplementation))]` | Target `string` property | Output masking (applies only on target side; applying on source side raises `NCMAP026`) | `.Member(d => d.X).Mask<TMask>()` |
| `[MapWhen(nameof(Predicate))]` | Target property | Conditional guard (named method form) | `.MapWhen` (supports both named and inline forms, within Member section) |
| `[MapNoAutoInclude]` | Property | Skips this property during ORM projection navigation collection | `NoAutoInclude` (parameterless, within Member section) |
| `[MapTree]` | Class | Declares tree-mounting triplet (defaults to `Id`/`ParentId`/`Children`) | `Tree(id, parent, children)` (pair-level) |

> **Pair-Level Whitelist**: `[MapNoAutoMatch]` (class-level) corresponds to Fluent’s `.NoAutoMatch()` (pair-level), but differs in granularity and coexistence from the property-level `[MapNoAutoInclude]` above — the former disables automatic matching for the entire pair, while the latter only excludes a single property from navigation collection. Semantic distinctions are detailed in [Fluent Basics: Dual Modes of Auto-Matching and Whitelisting](../how-to/fluent/basics.md).

## Class-Level Declarations (Hooks & Polymorphism)

| Attribute | Applicable Location | Purpose | Fluent Equivalent Terminal |
|-----------|---------------------|---------|----------------------------|
| `[BeforeMapping]` / `[AfterMapping]` | DTO class | Lifecycle hooks (`void(TSource, TTarget)` contract; validated by diagnostics `NCMAP038`/`NCMAP039`/`NCMAP040`) | `.BeforeMapping` / `.AfterMapping` |
| `[MapSubclass(typeof(DerivedSource), typeof(DerivedTarget))]` | DTO class (multiple instances allowed) | Registers polymorphic subtype dispatch (via `isinst` chain) | `.MapSubclass<TSrcDerived, TTargetDerived>()` (pair-level) |

## Diagnostics & Runtime

| Attribute | Purpose |
|-----------|---------|
| `[NCMapperOptions]` | Assembly- or class-level mapping diagnostics configuration (`ReportUnmappedTargetMembers` / `ReportUnmappedSourceMembers` toggles; precedence order: class-level > assembly-level > default) |

`[NCMapperOptions]` controls diagnostic strictness at *compile time*:  
- `ReportUnmappedTargetMembers`: whether unmapped target members should be reported (default: `false`)  
- `ReportUnmappedSourceMembers`: whether unconsumed source members should be reported (default: `true`)  

Applied either at the assembly level (`[assembly: NCMapperOptions(...)]`) or on individual DTO classes. Class-level settings override assembly-level ones; unannotated types fall back to defaults. This attribute has *no effect* on the runtime switch `Mapper.ValidationEnabled` (configured via DI `NCMapperOptions` type) — it affects *only* source generator diagnostics output.

A complete list of reference validity diagnostics (method reference signature templates, hook contracts, predicate signatures) is available in [diagnostics/rules.md](../diagnostics/rules.md).
