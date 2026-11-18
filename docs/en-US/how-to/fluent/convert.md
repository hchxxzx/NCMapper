> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: fluent/convert
Diagnostic codes involved: NCMAP_F17-F28  
Library version: Unreleased (to be released with the next version)  
Last updated: 2026-10-23  
---

# Fluent Convert / ConvertBack: Bidirectional Custom Conversion

> Fluent value-level transformation topic (four conversion forms — forward/reverse custom conversion + FromJson). For foundational chaining concepts, see [basics](basics.md); for engine-level semantics of conversion (conversion chains, “reverse direction does not consume” rule), see the shared semantic layer [direction-semantics](../../explanation/semantics/direction-semantics.md).

## Convert: Forward Custom Conversion

`Convert` is a terminal segment in the Member three-part configuration syntax, providing forward custom conversion capability for Fluent pipelines. Its semantics are a **1:1 mirror** of the attribute-based channel `[Convert]` (fully aligned in capabilities). The anchor is the target field declared at the start of the `.Member` segment (on the `TTarget` side; `TProp` disappears from the signature due to anchoring). Source-side resolution order matches the attribute-based channel: **property-by-name → automatic flattening → reverse lookup via `[MapProperty]`**. Explicit source paths declared via preceding `From` segments take precedence (regardless of chain order). `Convert` and `From` (which declares expression-tree-based sources) can **coexist without conflict** at the same anchor point (method-group resolution occurs at compile time vs. expression-tree evaluation at runtime — distinct mechanisms). Transformations apply to explicitly sourced values (supporting combinations like “custom source + conversion”).

**The four overloads correspond directly to the four conversion forms**, matching the `[Convert]` template (where `TProp` = the type of the target field anchored by `.Member`):

| Overload converter form | Conversion form | Typical scenario |
|-------------------------|-----------------|------------------|
| `Convert(Func<TProp>)` (`static TProp M()`) | T0 Constant | Writing a fixed value (source-side property of same name must exist, but its value is not read) |
| `Convert<TSrcProp>(Func<TSrcProp, TProp>)` (`static TProp M(TField)`) | T1 Field value | DateTime → string formatting (most common) |
| `Convert(Func<TSource, TProp>)` (`static TProp M(TSource)`) | T2 Full source | Multi-field composition (reads entire source object; method groups can be used directly — `TSource` is class-level fixed and inferable) |
| `Convert<TSrcProp>(Func<TSource, TSrcProp, TProp>)` (`static TProp M(TSource, TField)`) | T3 Composition | Full source + field value as dual parameters |

> Single-parameter delegates are classified as T2 (full source) if the parameter type matches `TSource`; otherwise, they are classified as T1 (field value) (determined during extraction phase, consistent with Q3 priority ordering in the attribute-based channel). When the source-side property is `Nullable<T>` and the converter parameter is non-nullable, an automatic `HasValue` ternary guard is generated (`null` bypasses invocation; target retains `default`).  

Runnable example (`dotnet run --scenario 102` for live validation):

[!code-csharp[Four-form Convert chain declaration (T0/T1/T2/T3)](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_Convert)]

**Method group inference guidance (important)**: C# type inference **does not infer generic type arguments from method groups for delegate input positions**. For T1 (field value) and T3 (composition) overloads, `TSrcProp` appears in the delegate’s parameter position (no inference site), so direct use of method groups requires explicit generic type arguments: `.Convert<TSrcProp>(M)`. Otherwise, the compiler emits `CS1503`. Three valid approaches:

Three valid usage examples (T1 with explicit type argument shown in the Four-form Convert example; delegate conversion shown in the ConvertBack disambiguation example):

[!code-csharp[Method group inference: explicit type arguments and delegate conversion](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_Convert)]

Method groups for T0 (no parameters) and T2 (full source, where `TSource` is class-level fixed) overloads can be used directly. If a method group itself has multiple overloads (e.g., both `M(string)` and `M(Foo)` exist), `CS0121` may occur; disambiguation uses the same techniques (explicit delegate conversion).

**Visibility contract**: Converters must be either **statically resolvable method groups** or **capturing-free inline lambdas** (transcribed into generated code). `private`/`protected` members trigger `NCMAP_F25`; **closures** (capturing outer state), local functions, or instance-bound methods trigger `NCMAP_F26` (a residual restriction from transcription prohibition); signatures mismatching any of the four forms trigger `NCMAP_F27`; anchors that are get-only or lack a public/init setter trigger `NCMAP_F28` (Warning); when no source path is reachable on the source side (by name, flattening, or reverse lookup), `NCMAP_F24` (Error) is reported; declaring both `Ignore` and `Convert` on the same field segment triggers `NCMAP_F23` (Error). Note: `F26` is the existing diagnostic code for `Convert` (narrowed to closures only); `MapWhen` inline transcription prohibitions use `F41`/`F42` (a separate family) — do not mix them.

## ConvertBack: Reverse Custom Conversion

`ConvertBack` is a terminal segment in the Member three-part configuration syntax, providing reverse custom conversion capability for Fluent pipelines. Its semantics are a **1:1 mirror** of the attribute-based channel `[ConvertBack]` (fully aligned in capabilities). The anchor is the target field declared at the start of the `.Member` segment (on the DTO side, oriented consistently with other terminal methods in the chain; `TProp` disappears from the signature due to anchoring, while the entity-side write-back point is resolved by name). Entity-side write-back points are resolved by **property-by-name**; this declaration is **not consumed during forward mapping** (forward mapping follows standard type-conversion paths); it pairs independently and optionally with forward conversion declarations on the same field.

**The four overloads correspond directly to the four conversion forms**, selected automatically by delegate arity, matching the `[ConvertBack]` template (where `TProp` = the type of the DTO-side field anchored by `.Member`):

| Overload converter form | Conversion form | Typical scenario |
|-------------------------|-----------------|------------------|
| `ConvertBack<TEntityProp>(Func<TEntityProp>)` (`static TEntityProp M()`) | T0 Constant | Writing a fixed value in reverse direction |
| `ConvertBack<TEntityProp>(Func<TProp, TEntityProp>)` (`static TEntityProp M(TField)`) | T1 Single value | string → DateTime parsing (most common) |
| `ConvertBack<TEntityProp>(Func<TTarget, TEntityProp>)` (`static TEntityProp M(TDto)`) | T2 Full source | Multi-field composition (reads entire DTO) |
| `ConvertBack<TEntityProp>(Func<TTarget, TProp, TEntityProp>)` (`static TEntityProp M(TDto, TField)`) | T3 Composition | Full DTO + field value as dual parameters |

Runnable example (`dotnet run --scenario 102` for live validation; includes all four forms plus pairing on the same anchor within one chain):

[!code-csharp[Four-form ConvertBack and same-anchor paired chain declaration](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_ConvertBack)]

**Visibility contract**: Converters must be either **statically resolvable method groups** or **capturing-free inline lambdas** (transcribed into generated code, sharing the transcription engine with `MapWhen` inline forms); `private`/`protected` members trigger `NCMAP_F19`; **closures** (capturing outer state), local functions, or instance-bound methods trigger `NCMAP_F20` (a residual restriction from transcription prohibition); signatures mismatching any of the four forms trigger `NCMAP_F21`; anchors that are get-only or lack a public/init setter trigger `NCMAP_F22` (Warning — the declaration will be ignored during materialization; prefer public/init setters or the entity-side `[ConvertBack]` attribute); declaring both `Ignore` and `ConvertBack` on the same field segment triggers `NCMAP_F17` (Error). Note: `F20` is the existing diagnostic code for `ConvertBack` (narrowed to closures only); `MapWhen` inline transcription prohibitions use `F41`/`F42` (a separate family) — do not mix them.

**Overload disambiguation**: When `TProp == TTarget` (self-referential DTO) or the converter method group itself has multiple overloads (e.g., a utility class contains both `M(string)` and `M(OrderDto)`), the compiler may emit `CS0121`. **Explicit generic type arguments cannot resolve this ambiguity**. Disambiguation requires explicit delegate conversion on the converter argument:

Disambiguation example (explicit delegate conversion for T1/T3 input positions; runnable via `dotnet run --scenario 102`):

[!code-csharp[ConvertBack converter disambiguation via explicit delegate conversion](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_ConvertBack)]

**Nullable semantics**: When the anchored property is `Nullable<T>`, the converter parameter should also be `Nullable<T>` to receive `null` directly (handling `null` is left to the converter). Guard variants using non-nullable `T` (e.g., `HasValue` ternary short-circuit) are structurally unreachable via method-group APIs (delegates lack value-type contravariance); Fluent pipelines recommend that converters accept `Nullable<T>` directly and handle nullability explicitly.

**ReverseOnly scenario guidance**: For T1/T3 forms where no source entry exists (e.g., top-level `NoAutoMatch` allowlist or auto-matching failure), the field argument is passed as the `default` literal (matching the attribute-based channel’s no-source-entry behavior). When field-value arguments are required, prefer T0/T2 forms, or pair the anchored field with a forward declaration (`From` / name-matched auto-match).

## FromJson: JSON String Expansion

The configuration-terminal `FromJson()` (attached to a Member field segment) is semantically equivalent to `[MapProperty(IsFromJson = true)]`: a source-side **string** property is deserialized via `JsonConverter` into a target **complex-type** property (forward: string → object; reverse: serialization back to string). Both source and target must be `string` or reference types (value types cause the declaration to be silently discarded, consistent with the attribute-based channel). When `From` is omitted, the source property is automatically matched by name to the target field.

Runnable example (`dotnet run --scenario 102` for live validation):

[!code-csharp[FromJson: JSON string expansion declaration](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_FromJson)]

## Related pages

- Full diagnostic index: [basics diagnostics table](basics.md)  
- Equivalent attribute-based forms: [api-comparison](../../reference/api-comparison.md)
