> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: getting-started/tutorial-15min
Related diagnostic codes:
Library version: v1.0.0+
Last updated: 2026-09-28
---

# 15-Minute Getting Started Tutorial

## Overview

This tutorial walks you through a complete mapping workflow: defining entities and DTOs, declaring mapping configurations, mapping single objects, mapping lists, projection, and validation. All examples are drawn from the `samples/NCMapper.Samples` project and are executable for verification.

This tutorial is ideal for readers encountering NCMapper for the first time. For installation and minimal usage, see [Quick Start](./quickstart.md); this tutorial focuses on a full end-to-end walkthrough, with steps executed sequentially—each step includes verifiable outcomes.

> **Declarative Dual-Channel Support**: This tutorial uses the **Fluent channel** (chain-style declarations via `[MapperConfig]` configuration classes) as its primary path; the attribute channel (`[MapFrom]` annotations) is introduced in Step 1 as an equivalent alternative. Both channels generate *identical* mapping code. A complete list of Fluent entry points is available at [Fluent Basics](../how-to/fluent/basics.md). All examples in this tutorial can be seamlessly translated between the two channels.

---

## 0. Prerequisites

### 0.1 Obtain the Sample Project

Clone the repository and navigate to the `samples/NCMapper.Samples` directory:

```bash
git clone <repository-url>
cd NCMapper
```

Run scenario 201 to confirm your environment is ready:

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 201
```

The output displays results for each sub-scenario of basic mapping (single-object mapping, list mapping, updating existing objects, extension methods). The `--scenario 201` flag corresponds one-to-one with the sample program number; verification for Steps 2 and 3 in this tutorial relies on scenario 201.

### 0.2 What You’ll Use

| Component | Purpose | Tutorial Steps |
|-----------|---------|----------------|
| NCMapper Core Package | Mapping capabilities (`Mapper.Map`, `MapList`, `Validate`) | 1/2/3/5 |
| NCMapper.FreeSql Extension Package | Projection (`AsDto` terminal operation) | 4 |
| `samples` Project | Single-source, runnable examples for verification | All |

If your project does not yet reference the NCMapper packages, add the `PackageReference` per [Quick Start §2.1](./quickstart.md).

---

## 1. Define Entities and DTOs

First, define the source entity. This example uses `UserEntity`, which contains a nested object `Address`:

[!code-csharp[UserEntity definition](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity)]

[!code-csharp[UserEntity definition · Id](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_Id)]

[!code-csharp[UserEntity definition · Name](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_Name)]

[!code-csharp[UserEntity definition · Age](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_Age)]

[!code-csharp[UserEntity definition · Email](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_Email)]

[!code-csharp[UserEntity definition · Status](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_Status)]

[!code-csharp[UserEntity definition · CreatedAt](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_CreatedAt)]

[!code-csharp[UserEntity definition · Address](~/samples/NCMapper.Samples/Models/Common/Entities/UserEntity.cs#Common_UserEntity_Address)]

Next, define the target DTO. Under the Fluent channel, the DTO is a plain class with no attributes:

```csharp
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}
```

**Declare Mapping (Fluent Configuration Class)**: Create a new configuration class annotated with `[MapperConfig]`. Inside it, declare a static method (name arbitrary, signature fixed) and use `cfg.For<Source, Target>()` to declare the type pair:

```csharp
[MapperConfig]
public class UserMapperConfig
{
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<UserEntity, UserDto>();   // Auto-matches properties by name and type; no per-property declaration needed
}
```

Once declared, the configuration class requires no manual registration: the source generator scans the body of the `Configure` method at compile time to generate mapping code, which is automatically registered into `Mapper` via a module initializer. Properties are matched automatically by naming convention: `Id` maps to `Id`, `Name` to `Name`, etc., provided types match. For fine-grained control—such as renaming, ignoring, or masking—append terminal methods to the `For(...)` chain. The full syntax is shown below (renaming via `Map`, nested path, `Ignore`, `Mask`, and `AfterMapping` chained together):

[!code-csharp[Fluent basic chain: Map/Ignore/Mask/AfterMapping](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper)]

[!code-csharp[Fluent basic chain: Map/Ignore/Mask/AfterMapping · Configure](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_Configure)]

[!code-csharp[Fluent basic chain: Map/Ignore/Mask/AfterMapping · ApplyComputedFields](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_ApplyComputedFields)]

**Equivalent Approach (Attribute Channel)**: If you prefer annotating directly on the DTO class, apply `[MapFrom(typeof(SourceType))]` to the DTO—no configuration class required. Both channels offer identical functionality, and the source generator produces *exactly the same* mapping code (the `UserDto` in `samples` uses this attribute-based form):

[!code-csharp[UserDto definition (attribute-channel equivalent)](~/samples/NCMapper.Samples/Models/Common/Dtos/UserDto.cs#Common_UserDto)]

[!code-csharp[UserDto definition (attribute-channel equivalent) · Id](~/samples/NCMapper.Samples/Models/Common/Dtos/UserDto.cs#Common_UserDto_Id)]

[!code-csharp[UserDto definition (attribute-channel equivalent) · Name](~/samples/NCMapper.Samples/Models/Common/Dtos/UserDto.cs#Common_UserDto_Name)]

[!code-csharp[UserDto definition (attribute-channel equivalent) · Age](~/samples/NCMapper.Samples/Models/Common/Dtos/UserDto.cs#Common_UserDto_Age)]

[!code-csharp[UserDto definition (attribute-channel equivalent) · Email](~/samples/NCMapper.Samples/Models/Common/Dtos/UserDto.cs#Common_UserDto_Email)]

Note: For any given type pair, only *one* declaration is allowed across both channels (choose one). Declaring on both sides triggers compile-time diagnostic `NCMAP_F11`.

NCMapper is a dual-engine component:

- **Source Generator Engine**: At compile time, generates static mapping code for every declared type pair (via Fluent configuration classes or `[MapFrom]` attributes), delivering zero first-call latency and compile-time diagnostics.
- **Runtime Expression Tree Engine**: For undeclared type pairs, compiles and caches expression trees on first invocation.

You do not need to choose an engine—the `Mapper.Map` API automatically uses pre-generated code when available, falling back to expression trees otherwise.

---

## 2. Single-Object Mapping

Call `Mapper.Map<TSource, TTarget>(src)` to perform a single mapping. Entry points are channel-agnostic; the following example uses the attribute-channel variant from `samples`:

[!code-csharp[Single-object mapping](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

Result: `dto.Id = 1`, `dto.Name = "Zhang San"`, and all matching-name-and-type properties are copied. The target object is newly instantiated by `Mapper.Map`; the source object remains unmodified.

Key points:

- Mapping copies properties between objects—it is *not* type conversion (`[Convert]`). Matching-name-and-type properties are assigned directly. When names or types differ, explicit declarations are required: use `.Member(d => d.Target).From(s => s.Source)` in Fluent mode, or `[MapProperty]` in attribute mode—or implement custom conversions (see [Mapping Rules (Semantics)](../explanation/semantics/mapping-rules.md) and [declarative/basics](../how-to/declarative/basics.md)).
- To perform post-mapping logic (e.g., formatting fields), use the overload accepting an `Action<TTarget>` callback:

[!code-csharp[Mapping with Action callback](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapWithAction)]

The callback executes *after* mapping completes, receiving the target object as its parameter—allowing direct mutation.

To update an existing object (without creating a new instance), use `Map(src, target)`:

[!code-csharp[Updating an existing object](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_UpdateExisting)]

The source generator also emits extension methods for each declared type pair—offering more concise syntax (requires `using NCMapper.Generated;`):

[!code-csharp[Source-generator extension method](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SourceGenExtension)]

[!code-csharp[Source-generator extension method · SourceGenWithAction](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SourceGenExtension_SourceGenWithAction)]

[!code-csharp[Source-generator extension method · SourceGenUpdateExisting](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SourceGenExtension_SourceGenUpdateExisting)]

[!code-csharp[Source-generator extension method · SourceGenList](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SourceGenExtension_SourceGenList)]

These extension methods rely on the same generated code as `Mapper.Map`; choose based on syntactic preference.

---

## 3. List Mapping

Use `Mapper.MapList<TSource, TTarget>(srcs)` to map collections in bulk:

[!code-csharp[List mapping](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ListMap)]

`MapList` applies the same mapping logic to each element in the collection, instantiating a distinct target object per element—no shared references. If the source list is `null`, `MapList` returns `null` without throwing.

List mapping suits bulk query result transformations (list pages, exports, API responses) and shares the *exact same* mapping configuration as single-object mapping—no separate rules exist.

Verify execution:

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 201
```

In scenario 201’s output, the list-mapping sub-scenario reflects the `MapList` result.

---

## 4. Simple Projection

Projection refers to ORM-side SQL operations that directly project entities into DTOs (`AsDto`)—performing both query and mapping in a single SQL statement. Projection requires an ORM extension package (`NCMapper.FreeSql`, `NCMapper.EFCore`, or `NCMapper.Dapper`; pick one). The core package contains *no* ORM dependencies.

FreeSql single-table projection example:

[!code-csharp[Single-table AsDto projection](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto)]

[!code-csharp[Single-table AsDto projection · SingleTableFirst](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto_SingleTableFirst)]

`AsDto()` trims columns and maps to the DTO *at the SQL level*, while supporting database-side filtering (`Where`), sorting (`OrderBy`), and pagination (`ToPagedList`). When encountering features SQL cannot translate (e.g., `[Convert]`, `[Mask]`), projection automatically degrades to a two-step in-memory mapping—yielding identical results but requiring one additional query ([FreeSql Single Table](../how-to/orm/freesql-single-table.md)).

Degradation is transparent: projection and in-memory mapping share the *same* mapping configuration. Translation proceeds at the SQL level where possible; unsupported features trigger automatic fallback—requiring *no changes* to calling code.

Verify execution:

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 501
```

The projection scenarios in `samples` use FreeSql’s in-memory mode with preloaded data—enabling offline execution.

## 5. Validation

Mapping configuration errors (e.g., incompatible property types) surface at runtime. Use `Mapper.Validate()` to detect them early during development:

[!code-csharp[Validate assembly](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios)]

[!code-csharp[Validate assembly · ValidateAll](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAll)]

[!code-csharp[Validate assembly · ValidateMapping](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateMapping)]

[!code-csharp[Validate assembly · ValidateOrThrowDemo](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateOrThrowDemo)]

[!code-csharp[Validate assembly · ValidateAssembly](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAssembly)]

[!code-csharp[Validate assembly · MapNullSource](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_MapNullSource)]

[!code-csharp[Validate assembly · TraceMapping](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceMapping)]

[!code-csharp[Validate assembly · TraceToWriter](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceToWriter)]

[!code-csharp[Validate assembly · TraceLogCallback](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceLogCallback)]

[!code-csharp[Validate assembly · TraceOptionsFactory](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceOptionsFactory)]

[!code-csharp[Validate assembly · ToggleValidationSwitch](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ToggleValidationSwitch)]

[!code-csharp[Validate assembly · PrintReportToWriters](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_PrintReportToWriters)]

[!code-csharp[Validate assembly · DiagnoseError](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_DiagnoseError)]

[!code-csharp[Validate assembly · Run](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_Run)]

`Validate()` scans all declared mapping type pairs in the assembly and returns a result categorized as OK / Warn / Error. An Error-level configuration means the mapping will fail at runtime and must be corrected before release; a Warn-level report indicates improvement opportunities (e.g., nonstandard attribute placement).

In production, perform full validation at startup using `ValidateOrThrow()`, which throws an exception immediately upon encountering any Error-level configuration ([Validation and Diagnostics](../how-to/infrastructure/validation.md)):

[!code-csharp[Full validation at startup](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios)]

[!code-csharp[Full validation at startup · ValidateAll](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAll)]

[!code-csharp[Full validation at startup · ValidateMapping](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateMapping)]

[!code-csharp[Full validation at startup · ValidateOrThrowDemo](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateOrThrowDemo)]

[!code-csharp[Full validation at startup · ValidateAssembly](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAssembly)]

[!code-csharp[Full validation at startup · MapNullSource](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_MapNullSource)]

[!code-csharp[Full validation at startup · TraceMapping](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceMapping)]

[!code-csharp[Full validation at startup · TraceToWriter](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceToWriter)]

[!code-csharp[Full validation at startup · TraceLogCallback](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceLogCallback)]

[!code-csharp[Full validation at startup · TraceOptionsFactory](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceOptionsFactory)]

[!code-csharp[Full validation at startup · ToggleValidationSwitch](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ToggleValidationSwitch)]

[!code-csharp[Full validation at startup · PrintReportToWriters](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_PrintReportToWriters)]

[!code-csharp[Full validation at startup · DiagnoseError](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_DiagnoseError)]

[!code-csharp[Full validation at startup · Run](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_Run)]

Run validation:

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 602
```

---

## 6. Next Steps

- **Scenario walkthroughs**: Multi-source composition (`Compose`), ORM projection, deep cloning — see the `tutorials` directory  
- **How-to guides**: Search by specific problem — see the `how-to` directory (basic mapping, type conversion, collections, deep cloning, etc.)  
- **Conceptual explanations**: Dual-engine architecture rationale, ORM fallback mechanism, reverse-mapping semantics — see the `explanation` directory  

---

## Common Misuses

| Misuse | Symptom | Fix |
|--------|---------|-----|
| Calling `Mapper.Map` without declaring a mapping for the target type (no configuration class nor `[MapFrom]`) | Falls back to expression trees: functionality remains but compile-time diagnostics and generated code are lost | Declare with `cfg.For<TSource, TTarget>()`, or annotate the DTO with `[MapFrom(typeof(SourceType))]` |
| Inconsistent property names without explicit declaration | Target properties retain default values (mapping is silently omitted), with no warning | Use Fluent syntax `.Member(d => d.Target).From(s => s.Source)`; or use the `[MapProperty]` attribute |
| Confusing *mapping* with *conversion* | Expecting format transformation, but only getting raw value copying | Use *mapping* for property copying; use `[Convert]` or Fluent `.Convert` for type/format transformations |

---

## Quick Reference Table

| Step | API | Description |
|------|-----|-------------|
| Fluent mapping declaration | `[MapperConfig]` + `cfg.For<TSource, TTarget>()` | Configuration-class-based declaration; generates mapping code at compile time |
| Attribute-based mapping declaration (equivalent) | `[MapFrom(typeof(T))]` | Annotate DTOs; generates mapping code at compile time |
| Single-object mapping | `Mapper.Map<TSource, TTarget>(src)` | Creates a new target object |
| Post-mapping processing | `Mapper.Map(src, (s, t) => ...)` | Action callback to modify the target object |
| Updating an existing object | `Mapper.Map(src, target)` | Reuses the provided target instance (no new allocation) |
| List mapping | `Mapper.MapList<TSource, TTarget>(srcs)` | Batch-maps a collection |
| Extension methods | `src.MapToXxxDto()` | Direct source-generator invocation; requires `using NCMapper.Generated` |
| Projection | `fsql.Select<T>().AsDto().ToList<TDto>()` | SQL-side projection (ORM extension package) |
| Validation | `Mapper.Validate()` | Scans the assembly to validate mapping configurations |

---

## Related Documentation

- [Quick Start](./quickstart.md): Installation and minimal usage  
- [Fluent Entry Point](../how-to/fluent/basics.md): Chain-based declaration of all endpoints  
- [Basic Mapping](../how-to/declarative/basics.md): Detailed explanation of mapping rules  
- [Custom Reverse Conversions](../how-to/declarative/basics.md): Using `[Convert]`/`[ConvertBack]` for type conversions  
- [Explanation: Dual Engine](../explanation/source-generator.md): Principles behind source generators and expression trees
