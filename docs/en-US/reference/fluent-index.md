> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: reference/fluent-index
Library Version: Unreleased (to be released with the next version)  
Last Updated: 2026-10-23  
---

# Fluent Terminal Quick Reference

> Complete list of all Fluent terminal methods (**18 terminal names / 45 overloads**, based on publicly exposed methods in source code `FluentMappingConfig`; excludes the entry point `For` — including it yields 19 / 46). For channel selection comparison, see [api-comparison](api-comparison.md); for attribute-to-terminal equivalence mapping, see [Attribute Quick Reference](attributes-index.md); for full semantics per terminal and runnable examples, see [Fluent Fundamentals (Chained Declaration Entry Point)](../how-to/fluent/basics.md).

## Chained Entry Point

| Terminal | Signature | Purpose |
|----------|-----------|---------|
| `For<TSource, TTarget>()` | `FluentTypeConfig<TSource, TTarget>` | Declares a mapping pair; serves as the chain’s starting point (every chain must begin with `For`; equivalent attribute form: `[MapFrom(typeof(Source))]`) |

## Three-Stage Layering

Field-level declarations follow the **Member Three-Stage Pattern**:  
`.Member(dest)` anchors the target field → *Head declaration* (`From` / `FromSource` / `Ignore`) specifies the source or declares ignore → *Configuration stage* (`Mask` / `Format` / `FromJson` / `MapTree` / `Convert` / `ConvertBack` / `MapWhen` / `NoAutoInclude`) applies transformations. **The field name is written only once**; multiple terminals targeting the same field may be chained without repeating the `dest` lambda.

| Stage | Host Type | Terminals |
|-------|-----------|-----------|
| Stage 1 (Anchor) | `FluentTypeConfig` → `MemberHead` | `Member<TProp>(dest)` |
| Stage 2 (Head Declaration) | `MemberHead` | `From` / `FromSource` (shorthand) / `Ignore` |
| Stage 3 (Configuration) | `MemberHead` or `MemberConfig` | `Mask` / `Format` / `FromJson` / `MapTree` / `Convert` / `ConvertBack` / `MapWhen` / `NoAutoInclude` |
| Back to Outer Level (Pair-Level) | `MemberConfig` / `AfterIgnore` inherits from `FluentTypeConfig` | `NoAutoMatch` / `Tree` / `BeforeMapping` / `AfterMapping` / `MapCtorParam` / `MapSubclass` / `FromSource` (group block) |

> **Two Hosts**: Each of `Mask`, `Format`, `FromJson`, `MapTree`, `Convert`, `ConvertBack`, `MapWhen`, and `NoAutoInclude` has two possible starting points — one on `MemberHead` (when `From` is omitted, resolves same-named source by default), and one on `MemberConfig` (already inside configuration stage, allowing explicit `From` sources to be combined). Therefore, overload counts in the table below are aggregated **across both hosts per terminal name**.

## Property Mapping & Conversion

| Terminal | Overload Count | Host | Purpose | Equivalent Attribute Form |
|----------|----------------|------|---------|----------------------------|
| `Member<TProp>(dest)` | 1 | `FluentTypeConfig` | Stage 1 of three-stage pattern: anchors the target field (`TProp` is inferred from the `dest` lambda; subsequent signatures omit `dest`) | — (on attribute side, the property itself serves as anchor) |
| `From<TSrcProp>(src, overrideIgnore = false)` | 1 | `MemberHead` | Explicit source declaration (supports nested source paths and `!` suppression; cross-type conversion is handled internally by generated code; unresolvable conversions raise diagnostic `NCMAP027`) | `[MapProperty]` |
| `Convert(converter, overrideIgnore = false)` | 8 | `MemberHead` + `MemberConfig` | Forward custom conversion (four template forms × two hosts: T0 `Func<TProp>` / T1 `Convert<TSrcProp>(Func<TSrcProp, TProp>)` / T2 `Func<TSource, TProp>` / T3 `Convert<TSrcProp>(Func<TSource, TSrcProp, TProp>)`) | `[Convert]` |
| `ConvertBack<TEntityProp>(converter, overrideIgnore = false)` | 8 | `MemberHead` + `MemberConfig` | Reverse custom conversion (DTO → Entity; four template forms × two hosts: T0 `Func<TEntityProp>` / T1 `Func<TProp, TEntityProp>` / T2 `Func<TTarget, TEntityProp>` / T3 `Func<TTarget, TProp, TEntityProp>`) | `[ConvertBack]` |
| `Format(format, overrideIgnore = false)` | 2 | `MemberHead` + `MemberConfig` | Property-level formatting: compiles-time constant format string applied to source `IFormattable` to produce text output (target must be `string`; forward-only declaration; reverse assignment disabled with `NCMAP_F67`; mutually exclusive with `.Convert` at same anchor point via `NCMAP_F66`) | `[MapFormat("format-string")]` |
| `FromJson()` | 2 | `MemberHead` + `MemberConfig` | JSON string expansion (source `string` → target object; reverse serialization writes back) | `[MapProperty(IsFromJson = true)]` |
| `MapTree()` | 2 | `MemberHead` + `MemberConfig` | Property-level recursive tree mapping (this field recursively applies the same `(S,T)` configuration); semantically distinct from pair-level `Tree()` despite similar naming — see next section | (no attribute-level equivalent) |

## Ignoring & Guards

| Terminal | Overload Count | Host | Purpose | Equivalent Attribute Form |
|----------|----------------|------|---------|----------------------------|
| `Ignore()` | 2 | `MemberHead` | Ignores the target field; second overload accepts `Direction` (directional ignore). Terminal state: only `NoAutoInclude` may follow it | `[MapIgnore]` / `[MapIgnore(Direction)]` |
| `MapWhen(predicate)` / `MapWhen<TInput>(condition)` | 4 | `MemberHead` + `MemberConfig` | Conditional guard (maps only if predicate evaluates to `true`): T1 non-generic `Func<TProp, bool>` predicate on field value / T2 explicitly typed `TInput` predicate on entire source; two forms × two hosts | `[MapWhen(nameof(predicate))]` |
| `NoAutoInclude` (parameterless) | 3 | `MemberHead` + `MemberConfig` + `AfterIgnore` | **Field-level** disable of auto-inclusion (skips this field during ORM downgrade path navigation collection). The overload on `AfterIgnore` is the *only* operation allowed after `Ignore()` | `[MapNoAutoInclude]` (property-level) |

## Data Masking & Tree Construction

| Terminal | Overload Count | Host | Purpose | Equivalent Attribute Form |
|----------|----------------|------|---------|----------------------------|
| `Mask<TMask>(overrideIgnore = false)` | 2 | `MemberHead` + `MemberConfig` | Output masking (target must be `string`; `where TMask : IMask`; shares `MaskProvider` with attribute side) | `[Mask(typeof(implementation))]` |
| `Tree()` / `Tree(idProperty, parentIdProperty, childrenProperty)` | 2 | `FluentTypeConfig` | **Pair-level** tree construction configuration: declares the tree mount triplet (`[MapTree]`’s fluent equivalent, consumed by `ToTreeList`); opt-in — tree mount position detection always returns `false` unless called | `[MapTree]` |

## Cross-Source & Constructor Binding

| Terminal | Overload Count | Host | Purpose | Equivalent Attribute Form |
|----------|----------------|------|---------|----------------------------|
| `FromSource<TSubSource>(src)` | 1 | `MemberHead` | Sub-source shorthand declaration (binds one sub-source property per declaration; `object` placeholder type is restored to actual type via symbol resolution) | `[MapProperty(FromSource = typeof(SubSource))]` |
| `FromSource<TSubSource>(configure)` | 1 | `FluentTypeConfig` | **Pair-level** grouped block: declares multiple fields bound to the same sub-source (block body = chain identical to `For`) | `[MapProperty(FromSource = typeof(SubSource))]` |
| `MapCtorParam<TSrcProp>(paramName, sourceProp)` / `MapCtorParam(paramName, sourcePropertyName)` | 2 | `FluentTypeConfig` | Explicit constructor parameter binding (Fluent-exclusive; Form 1 uses source lambda as primary; Form 2 uses source property name string as fallback) | (no attribute equivalent) |

## Lifecycle & Polymorphism

| Terminal | Overload Count | Host | Purpose | Equivalent Attribute Form |
|----------|----------------|------|---------|----------------------------|
| `BeforeMapping(handler)` | 1 | `FluentTypeConfig` | Lifecycle pre-hook (`Action<TSource, TTarget>`; requires statically resolvable method group reference) | `[BeforeMapping]` |
| `AfterMapping(handler)` | 1 | `FluentTypeConfig` | Lifecycle post-hook (executed at end of shared core method; inherited across 6 forward forms; not emitted for any of the 4 reverse forms) | `[AfterMapping]` |
| `MapSubclass<TSrcDerived, TTargetDerived>()` | 1 | `FluentTypeConfig` | Polymorphic subtype dispatch registration (via `isinst` chain; pair-level; arbitration by inheritance distance — “most derived first” on both source and target sides) | `[MapSubclass]` |

## Auto-Match Toggle (Pair-Level)

| Terminal | Overload Count | Host | Purpose | Equivalent Attribute Form |
|----------|----------------|------|---------|----------------------------|
| `NoAutoMatch()` | 1 | `FluentTypeConfig` | **Pair-level** allowlist toggle: disables auto-matching/auto-inclusion (including auto-flattening) for this `(S,T)` pair; only explicitly declared mappings and same-name defaults are applied | `[MapNoAutoMatch]` (class-level) |

> **Distinction (Same Name, Different Semantics)**: `NoAutoMatch()` is a **pair-level** toggle, whereas `NoAutoInclude` (parameterless terminal) is a **field-level** operation. Field-level usage appears inside the `Member` stage — anchor the field first (e.g., `.Member(d => d.AuditLogs)`), then append the parameterless `NoAutoInclude`. Though syntactically similar to the legacy pair-level name, its semantics differ. When porting code, do *not* mistakenly rewrite field-level usages as pair-level toggles.

## Override Ignore Flag (`overrideIgnore`)

The five terminals — `From`, `Mask`, `Format`, `Convert`, and `ConvertBack` — each accept an optional `bool overrideIgnore = false` parameter (not a separate overload): when the field matches a global ignore list (e.g., `IgnoreNames` in `[assembly: MapDefaults]`), this flag exempts it, allowing mapping/masking/conversion/formatting to proceed normally. Its semantics mirror the attribute-side `OverrideIgnore` named argument and apply to both forward and reverse chains. Full rules governing global ignore lists and exemptions are documented in [Global Mapping Configuration](../how-to/infrastructure/global-config.md).

## Diagnostics

Compile-time diagnostic codes (`NCMAP_F` family) triggered by terminal constraint violations are listed individually in [Diagnostic Rules](../diagnostics/rules.md) (this page serves as the **master table + pagination redirect**). The three core principles — *channel exclusivity*, *direction exclusivity*, and *configuration follows class anchor* — and their corresponding detectors are also covered there.

## Further Reading

- [Fluent Fundamentals (Chained Declaration Entry Point)](../how-to/fluent/basics.md) — Chained declaration, override-ignore semantics, tree mapping distinctions, and full diagnostic index  
- [Fluent Four Terminals](../how-to/fluent/terminals.md) — Dedicated coverage of `MapWhen`, `MapCtorParam`, and `MapSubclass`  
- [Fluent Conversions](../how-to/fluent/convert.md) — In-depth guide to `Convert` and `ConvertBack`  
- [Fluent Formatting](../how-to/fluent/format.md) — Dedicated coverage of `Format` terminal (guard diagnostics F64–F67)  
- [Fluent Cross-Source Binding](../how-to/fluent/from-source.md) — Dedicated coverage of `FromSource`
