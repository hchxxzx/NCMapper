### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NCMAP099 | NCMapper | Warning | Source generation failed for mapping
NCMAP018 | NCMapper | Warning | Array Contains binds to MemoryExtensions.Contains in expression-tree lambda (.NET 10/C# 14 overload resolution change)
NCMAP012 | NCMapper | Warning | Record circular reference, DeepCopy falls back to runtime
NCMAP013 | NCMapper | Info | Reverse mapping property skipped
NCMAP014 | NCMapper | Error | Mask type does not implement IMask
NCMAP015 | NCMapper | Error | Mask target is not string
NCMAP017 | NCMapper | Info | MapNoAutoInclude on reference property
NCMAP019 | NCMapper | Error | Mapping configuration attributes ([MapProperty]/[Convert]/[ConvertBack]/[MapIgnore]/[BeforeMapping]/[AfterMapping]/[Mask]/[MapWhen]/[MapNoAutoInclude]/[MapFormat]/[MapNoAutoMatch]) present without [MapFrom] — configuration has no activation path (narrowed from the former dual-scenario Info; scenario ② DTO-naming split out to NCMAP042; severity Info → Error; [MapTree] removed from the family — class-level tree definition, not a mapping pair; [MapNoAutoMatch] added 2026-10-11 — orphan-attribute governance via the family route)
NCMAP016 | NCMapper | Info | DeepCopy graph statically unresolvable, full cycle detection retained
NCMAP026 | NCMapper | Warning | Mask annotation position is non-canonical (source side)
NCMAP020 | NCMapper | Warning | Global mapping config value invalid, treated as unset
NCMAP021 | NCMapper | Warning | Explicit annotation hits global ignore (fail-closed ignore)
NCMAP022 | NCMapper | Warning | MapIgnore with OverrideIgnore=true contradiction
NCMAP023 | NCMapper | Info | Enum member name differs only by case, string→enum falls back to exact match
NCMAP024 | NCMapper | Info | Duplicate enum member values, string→enum numeric label degrades
NCMAP025 | NCMapper | Error | Mutual mapping declaration on both sides ([MapFrom] or Fluent For); Fluent-side diagnostics locate at the For<S,T>() invocation (message generalized + location split per channel)
NCMAP027 | NCMapper | Info | Type-incompatible member skipped (AutoMapper semantics alignment)
NCMAP028 | NCMapper | Info | Inaccessible member skipped
NCMAP031 | NCMapper | Error | Reverse write-back path intermediate node cannot be instantiated (interface/abstract/no public parameterless ctor)
NCMAP032 | NCMapper | Info | init-only target property skipped in update path (constructor/creation path only)
NCMAP033 | NCMapper | Info | Reverse mapping does not consume [Convert] by default (converter generics bind forward direction); annotate [ConvertBack] for reverse custom conversion
NCMAP034 | NCMapper | Info | [Mask] property written back in reverse mapping (masked value written as-is, round-trip lossy)
NCMAP035 | NCMapper | Error | Convert/ConvertBack static method not found or inaccessible on anchor type (declared-only semantics, accessibility whitelist)
NCMAP036 | NCMapper | Error | Convert/ConvertBack static method overload ambiguity — multiple overloads match signature templates
NCMAP037 | NCMapper | Error | Convert/ConvertBack static method signature mismatch — no overload matches T0-T3 templates
NCMAP029 | NCMapper | Info | Nested mutual-mapping cycle detected, registered pairs demoted to runtime fallback (prevents cyclic StackOverflow)
NCMAP030 | NCMapper | Info | Explicit dot-path value not consumed by [Mask]/[Convert] (observability only, current-version limitation)
NCMAP038 | NCMapper | Error | Lifecycle hook method ([BeforeMapping]/[AfterMapping]) not resolvable on the anchor type or visibility insufficient
NCMAP039 | NCMapper | Error | Lifecycle hook method signature mismatch — void(TSource,TTarget) expected; open-generic anchor rejected
NCMAP040 | NCMapper | Error | Lifecycle hook method overload ambiguity — multiple overloads compatible with the mapping-pair signature, no exact match
NCMAP041 | NCMapper | Error | [MapWhen] predicate method resolution failed — not found / non-static / bad signature / visibility insufficient
NCMAP042 | NCMapper | Info | Type follows the DTO naming pattern (Dto/DTO suffix) without [MapFrom] — source-generated mapping code not produced; runtime reflection fallback unavailable in AOT/trimmed scenarios (scenario ② split out from the former single-code NCMAP019; Fluent target types exempted)
NCMAP_F55 | NCMapper | Warning | Fluent multi-segment destination path not supported; declaration skipped
NCMAP_F56 | NCMapper | Error | Fluent property argument is not a lambda expression; entire declaration dropped
NCMAP_F57 | NCMapper | Warning | Fluent lambda block body or conditional expression; declaration skipped
NCMAP_F58 | NCMapper | Error | Fluent argument not statically evaluable; value ignored, default applies
NCMAP_F59 | NCMapper | Error | Fluent true conflict on the same anchor — same-slot repetition or coexistence with an Ignore; keep one declaration or resolve the conflict (revised 2026-10-09: severity Warning → Error, semantics narrowed from cross-kind last-write-wins to true-conflict rejection)
NCMAP_F60 | NCMapper | Warning | Unknown Fluent chain terminal; its declaration skipped
NCMAP_F62 | NCMapper | Warning | Fluent lambda source chain exceeds the maximum member access depth; declaration dropped
NCMAP_F63 | NCMapper | Warning | Cycle detection stopped: registered-mapping graph exceeds the visit budget
NCMAP043 | NCMapper | Error | [MapFormat] target property type is not string
NCMAP044 | NCMapper | Error | [MapFormat] source property type does not implement IFormattable
NCMAP045 | NCMapper | Error | [MapFormat] and [Convert] applied to the same property
NCMAP046 | NCMapper | Warning | [MapFormat] property is skipped in reverse mapping; declare [ConvertBack] for round-tripping (severity revised 2026-09-29)
NCMAP047 | NCMapper | Error | Property carries a mapping transform attribute ([Convert]/[Mask]/[MapFormat]/[MapWhen]) but no [MapProperty] head declaration under the [MapNoAutoMatch] whitelist — not mapped, not added to candidates; add [MapProperty] or remove the transform attribute
NCMAP_F64 | NCMapper | Error | Fluent .Format target property type is not string
NCMAP_F65 | NCMapper | Error | Fluent .Format source property does not implement IFormattable
NCMAP_F66 | NCMapper | Error | Fluent .Format and .Convert target the same property
NCMAP_F67 | NCMapper | Warning | Fluent .Format property is skipped in reverse mapping; declare .ConvertBack for round-tripping (severity revised 2026-09-29)
NCMAP_F68 | NCMapper | Warning | Fluent member carries no declaration after Member — add a source (From / FromSource), an ignore (Ignore), or a transform such as Mask or Format
NCMAP_F69 | NCMapper | Error | Fluent member omits its source and the source type has no same-named member — declare the source with .From(...)
NCMAP_F70 | NCMapper | Error | Fluent property repeats its declaration — the same slot allows only one declaration (first/duplicate kinds reported)
NCMAP_F71 | NCMapper | Error | Fluent MapWhen whole-source type argument matches neither the target property type nor the source type — use .MapWhen<TSource>(...) or omit the type argument for the field-value form

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NCMAP008 | NCMapper | Error | Converter type mismatch (interface-form IValueConverter generic validation retired; replaced by NCMAP035/036/037 static-method signature validation)
NCMAP_F61 | NCMapper | Info | Retired with the Fluent S-side anchoring terminals (introduced in f93c4dd, removed in 6242ff2) — retired empty ID, number not reused
