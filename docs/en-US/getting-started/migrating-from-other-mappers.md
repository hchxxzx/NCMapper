> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: getting-started/migrating-from-other-mappers
Library Version: Unreleased
Last Updated: 2026-09-08
---

# Migrating from Other Mapping Libraries: Concept Mapping

This page provides a conceptual mapping between competing mapping libraries—such as MapStruct, AutoMapper, Mapster, and Mapperly—and NCMapper, for users transitioning to NCMapper. NCMapper uses a source generator to materialize mapping code at compile time. All configuration is validated during compilation, and no reflection-based configuration parsing occurs at runtime (AOT-friendly).

## Naming Conventions

When introducing new endpoints, NCMapper follows the “default bilateral name symmetry” naming rule: both the Fluent and attribute-based configuration channels share the same root term, with syntactic variations adapted to each channel’s grammar. Naming differences relative to common conventions in competing libraries are summarized below:

| Capability | Competing Library Concept | NCMapper Endpoint | Channel | Naming Rationale |
|---|---|---|---|---|
| Conditional Mapping | AutoMapper `Condition`, Mapster `Map(dest, src, cond)`, MapStruct `@Condition`, ModelMapper `when()` | `MapWhen` | Bilateral name symmetry | Root term `When` expresses guard semantics; the condition family is unipolar—no explicit negative entry (e.g., `IgnoreWhen`) is provided, since predicate negation suffices. |
| Polymorphic / Inheritance Dispatch | AutoMapper `Include`/`IncludeBase`, MapStruct `@SubclassMapping`, Mapperly `MapDerivedType`, Mapster inheritance family | `MapSubclass` | Bilateral name symmetry | Anchored on MapStruct’s `@SubclassMapping`; AutoMapper’s `Include` was rejected because it describes “registration inclusion”, not “subtype dispatch” itself. |
| Constructor Parameter Binding | AutoMapper `ForCtorParam` | `MapCtorParam` | Fluent only | Root term `CtorParam` aligns with AutoMapper. |
| Pre-Mapping Hook | Mapster `BeforeMapping`, AutoMapper `BeforeMap`, MapStruct `@BeforeMapping` | `BeforeMapping` | Bilateral name symmetry | Direct adoption of industry-standard terminology. |
| Post-Mapping Hook | AutoMapper `AfterMap`, MapStruct `@AfterMapping` | `AfterMapping` | Bilateral name symmetry | Direct adoption of industry-standard terminology. |
| Custom Conversion | AutoMapper resolver / value resolver, MapStruct `qualified@` | `Convert` (Fluent) / `Convert` (Attribute) | Dual-channel fork preserved | Consistent root term; both channels retain the same name (current design). |

Two categories of naming divergence:

1. **Name-aligned endpoints**: `MapWhen`, `MapSubclass`, `MapCtorParam`, `BeforeMapping`, and `AfterMapping` use identical root terms across both Fluent and attribute channels—their syntax differs only per channel (method call vs. attribute annotation).
2. **Dual-channel `Convert`**: Both `Convert` (Fluent) and `Convert` (attribute) coexist and remain unchanged.

## Capability Comparison Matrix

The table below compares final capability support across four major libraries, with NCMapper shown separately for both channels:

| Capability | NCMapper (Fluent) | NCMapper (Attribute) | MapStruct | AutoMapper | Mapster | Mapperly |
|---|:--:|:--:|:--:|:--:|:--:|:--:|
| Property Mapping (cross-type / nested) | `Map` | `[MapProperty]` | Supported | `ForMember` | Supported | Supported |
| Conditional Guard | `MapWhen` | `[MapWhen]` | `@Condition` | `Condition` | Three-argument `Map` | Not supported |
| Polymorphic / Inheritance Dispatch | `MapSubclass` | `[MapSubclass]` | `@SubclassMapping` | `Include` | Inheritance family | `MapDerivedType` |
| Constructor Parameter Binding | `MapCtorParam` | No corresponding attribute | Constructor injection | `ForCtorParam` | None | Constructor mapping |
| Lifecycle Hooks | `BeforeMapping` / `AfterMapping` | `[BeforeMapping]` / `[AfterMapping]` | Supported | `BeforeMap` / `AfterMap` | Supported | `After` only |
| Custom Conversion | `Convert` (four forms) | `[Convert]` | Supported | Resolver | Supported | Supported |
| Reverse Mapping | `ConvertBack` (four forms) | `[ConvertBack]` | Supported | `ReverseMap` | `Reverse` | PR in progress |
| Data Masking | `Mask` | `[Mask]` | None | None | None | None |
| JSON Field Expansion | `FromJson` | `[MapProperty(IsFromJson)]` | None | None | None | None |
| Multi-Source Composition | `Compose` / `FromSource` | `[MapFrom]` + `[MapProperty]` | Multi-source parameters | None | None | Multi-parameter |
| ORM Projection | EF Core / FreeSql / Dapper | Same as left | Not applicable | `ProjectTo` | `ProjectTo` | Supported |
| Compile-Time Diagnostics | `NCMAP_Fxxx` family | `NCMAP0xx` family | Unmapped field checks | Runtime Assert | Compile-time validation | Compile-time |

Key asymmetries between the two NCMapper channels (most common pitfalls during migration):

1. **`MapCtorParam` is Fluent-only** — no corresponding attribute type exists. Constructor injection is triggered when the target type lacks a public parameterless constructor. An explicit `MapCtorParam` declaration binds specified parameters using *two* sources: constructor arguments and source property names.
2. **`MapSubclass` is equivalent across both channels**: Fluent `MapSubclass<TSub,TSubDto>()` and attribute `[MapSubclass]` generate identical dispatch structures; both may coexist within the same assembly.
3. **`MapWhen` and lifecycle hooks are bilaterally equivalent**: Guards and callbacks have identical semantics—only syntax differs.

Runnable example — basic name-matching mapping (live demonstration of the “Property Mapping” row above, NCMapper attribute channel; verify by running `dotnet run --scenario 201`):

[!code-csharp[Basic name-matching mapping: UserEntity → UserDto (attribute channel)](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

## Behavioral Differences

When migrating from runtime reflection-based mappers (e.g., AutoMapper, Mapster), note the following behavioral distinctions:

1. **Configuration Timing**: NCMapper consumes mapping configurations at compile time via its source generator and emits concrete mapping code. At runtime, no configuration objects are parsed. Configuration errors surface as compile-time diagnostics (Error-level), not runtime exceptions or assertions.
2. **Inline Expression Transcription**: `MapWhen` predicates and `Convert` converters support *capture-free inline lambdas*, which the transcription engine materializes into static methods inside generated code. Capturing variables, statement-bodied lambdas, or private member access are explicitly disallowed and produce precise diagnostics (e.g., F41/F42). This contrasts with reflection-based mappers, where “any delegate can be passed”; static method groups are always permitted.
3. **ORM Projection Degradation Boundary**: DTOs declared with `MapWhen`, `BeforeMapping`, `AfterMapping`, or `MapSubclass` are *forced to degrade to in-memory mapping* in ORM projection (`IQueryable`) scenarios—guards and callbacks cannot be expressed inside projections. `MapCtorParam` does *not* trigger degradation.
4. **Reverse Mapping**: Both Fluent and attribute channels automatically generate reverse mapping entry points. `MapSubclass` additionally synthesizes reverse subclass pairs—reverse dispatch respects the actual DTO subclass type at runtime.
5. **Multiple Constructor Ambiguity**: When no parameterless constructor exists, NCMapper defaults to the public constructor with the most parameters. No configuration API yet resolves ambiguity among multiple constructors; use `MapCtorParam` to explicitly anchor parameters when needed.

## Migration Recommendations

1. Audit custom logic in your source library (resolvers, conditions, before/after callbacks) and map each to its corresponding NCMapper endpoint using the matrix above.
2. Replace DTO-side configuration with either `[MapperConfig]` + Fluent chains *or* `[MapFrom]` attributes—choose the channel based on team preference (both produce functionally equivalent output).
3. Resolve all `NCMAP` diagnostics one-by-one at compile time (each includes precise location and remediation guidance—see [Diagnostic Rules](../diagnostics/rules.md)).
4. After successful compilation, invoke `Mapper.Validate()` for full end-to-end validation to confirm mapping completeness.

Related pages: [Fluent Mapping Overview](../how-to/fluent/basics.md), [Mapping Rules (Semantics)](../explanation/semantics/mapping-rules.md), [Complete Diagnostic Code Index](../diagnostics/rules.md).
