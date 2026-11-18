> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: fluent/from-source
Diagnostic codes involved: NCMAP_F29–F32, F38, F45  
Library version: Unreleased (to be released with the next version)  
Last updated: 2026-10-23  
---

# Fluent FromSource: Cross-Source Binding

> This document covers the Fluent pipeline’s cross-source binding capability. For details on the runtime composition mechanism for multi-source synthesis (the *Compose surface*), see [explanation/compose](../../explanation/compose.md); for directional semantics, see [direction-semantics](../../explanation/semantics/direction-semantics.md).

`FromSource<TSubSource>` binds fields of a **sub-source** type to target DTO properties. The main chain `For<TSource, TTarget>` carries the primary source mapping, while sub-source fields are declared as cross-source inputs via `FromSource`. This corresponds structurally to the attribute-based approach: `[MapFrom(typeof(PrimarySource))]` applied to the class, plus `[MapProperty(FromSource = typeof(SubSource), ...)]` applied to individual properties. The terminal name `FromSource` aligns exactly with the `FromSource` property in the attribute-based configuration.

## Two Declaration Styles

**Shorthand (single-field binding)** — Member-head declaration: first `.Member` anchors the target property, then `FromSource<SubSource>(subSourcePath)` binds the source:

Runnable example (`dotnet run --scenario 103` for live validation):

[!code-csharp[FromSource Shorthand: Single-field cross-source binding](~/samples/NCMapper.Samples/01-Fluent/FsModels.cs#FluentFromSource_Bindings)]

**Grouped block (multiple fields from the same sub-source)** — Block receiver type is `FluentTypeConfig<TSubSource, TTarget>`. Inside the block, the fluent chain is identical to `For<TSubSource, TTarget>`, and field declarations follow the same three-part Member pattern: `From` / `Mask` / `FromJson` / `Convert`, etc., with the *sub-source* serving as the source anchor:

Runnable example (`dotnet run --scenario 103` for live validation):

[!code-csharp[FromSource Grouped Block: Multiple fields from same sub-source (From + Mask + FromJson)](~/samples/NCMapper.Samples/01-Fluent/FsModels.cs#FluentFromSource_Bindings)]

## Dual Runtime Surfaces

Two distinct runtime surfaces coexist under the same Fluent declaration:

- **Primary-source surface**: `Mapper.Map<Order, OrderDto>(order)` — populates only primary-chain properties; properties matched by `FromSource` remain at their default values (*materialized divergence*: `FromSource`-matched properties enter `ComposeMappings` but *not* `PropertyMappings`).  
- **Composition surface**: `Mapper.Compose<OrderDto>(order, customer, ...)` — performs one-shot composition of both primary-chain and all sub-source fields.

## Runtime Shape & AOT Compatibility

- The source generator emits dedicated composition delegates for `FromSource` targets and registers them automatically. `Mapper.Compose` prioritizes this zero-reflection generated path (AOT-friendly).  
- **AOT Exemption Note**: Fluent `FromSource` declarations reuse the *same* composition emitter used by the attribute-based channel — introducing *no new emission shape*. Therefore, existing AOT coverage for attribute-based `Compose` is inherited directly. The `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]` attributes on `Mapper.Compose` constrain *only* the fallback reflection path (`ComposeRuntime`); the generated-delegate path remains unaffected.  
- When no generated delegate is registered (e.g., source generator disabled or absent), execution falls back to the reflection-based `ComposeRuntime` path. This path requires the target type to be annotated with `[MapFrom]`; otherwise, using a Fluent DTO without this attribute throws `MapConfigurationException`.

## Ordering-Based Override

The same target property may be bound by *different sub-sources* (a Fluent-exclusive capability — not expressible in the attribute-based channel, which operates at single-field/single-sub-source granularity): `Mapper.Compose` processes sub-sources in the order they appear in the input array, with **later bindings overriding earlier ones**.

## Declaration Conflicts & Block-Level Restrictions (NCMAP_F29–F32)

- `NCMAP_F29` (**Error**): Conflict on the same destination property across dual runtime surfaces:  
  ① Any primary-chain field declaration (`From` source / `Ignore` / `Mask` / `MapTree` / `FromJson` / `Convert` / `ConvertBack`) conflicts with a `FromSource` sub-source binding targeting the same property (a property must be supplied by *exactly one* source).  
  ② Multiple *carrier-style* bindings from the *same sub-source* — e.g., `From` shorthand / block-scoped `From` / `MapTree` — or multiple `Convert` declarations. (*Note:* Block-scoped `Mask` / `FromJson` are *modifier-style* attachments and excluded from conflict counting; a single `Convert` combined with `From` is permitted via attachment merging.)  

- `FromSource` bindings support *forward-only* mapping and provide *no hook injection points*. The following terminals inside a `FromSource` block trigger **Error**:  
  - `ConvertBack` (**F30**) — no reverse-write sink point exists.  
  - `NoAutoMatch` (**F31**) — disables automatic matching at the pair level (formerly named `NoAutoInclude`; this restriction takes effect upon renaming; sub-source fields must be declared explicitly, and auto-inclusion has no meaning in the block context).  
  - `AfterMapping` (**F32**) — the generation pipeline provides no hook injection point; such declarations are never executed.

## Mask (Enabled Inside Blocks)

Block-scoped `Mask` declarations also follow the three-part Member pattern — e.g., `.Member(d => d.X).Mask<TMask>()` is fully materialized and consumed: runtime composition values are masked per declaration, consistent with primary-chain `Mask` semantics. Combining `Format` and `Mask` on the same field within a block (`FormatThenMask` dual annotation) is supported — formatting occurs *before* masking, equivalent to the primary-chain pattern `From + Format + Mask` (see scenario 109’s `JoinedAt` for a runnable demonstration).

## Comparison With Attribute-Based Configuration

| Fluent Channel | Attribute-Based Channel |
|----------------|-------------------------|
| `.Member(d => d.X).FromSource<Sub>(s => s.Y)` (shorthand, Member-head declaration — one statement binds one sub-source field) | `[MapProperty(FromSource = typeof(Sub), nameof(Sub.Y))]` (class-level `[MapFrom(typeof(PrimarySource))]` required separately) |
| Block-scoped `c.Member(d => d.X).Convert<T1>(M)` (`T1` = source field type; target type inferred from `Member` anchor) | `[MapProperty(FromSource = typeof(Sub), ...)]` + `[Convert(typeof(MClass), nameof(MClass.M))]` |
| Ordering-based override of the same target field across different sub-sources | Not expressible (attribute channel is limited to single-field/single-sub-source granularity) |
