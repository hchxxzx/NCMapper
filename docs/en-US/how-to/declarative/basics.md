> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: declarative/basics
Diagnostic codes involved: NCMAP001, NCMAP002, NCMAP005, NCMAP019  
Library version: v1.0.0+  
Last updated: 2026-09-29  
---

# Basic Mapping (Attribute-Based Channel)

## Overview

Basic mapping is the core capability of NCMapper: copying property values from one object to another. NCMapper provides a dual-engine architecture that automatically selects the optimal path:

- **Source Generator Engine**: At compile time, generates static mapping methods for each `[MapFrom]` type pair—zero delegate lookups, fully JIT-inlinable.  
- **Runtime Expression Tree Engine**: For unregistered type pairs, compiles and caches expression trees on first invocation.

Both engines produce identical results; the only difference lies in the execution path. Users need not concern themselves with which engine is used—`Mapper.Map<TSource, TTarget>(src)` automatically selects the optimal one.

> **Declarative Dual Channels**:  
> - *Attribute Channel*: DTOs annotated with `[MapFrom]` and related attributes.  
> - *Fluent Channel*: Configuration declared via `FluentMappingConfig` within a class marked with `[MapperConfig]`.  
>   
> These two channels are equivalent and coexist:  
> - **Different type pairs may freely mix both channels**.  
> - **The same type pair (in either direction) may be declared by only one channel**; duplicate declarations trigger compile-time error `NCMAP_F11`. Circular or unordered mutual references are guarded by the `NCMAP025` family of diagnostics.  
>   
> This article focuses on the Attribute Channel. Equivalent Fluent syntax and all terminal usage examples are documented in [Fluent Entry](../fluent/basics.md).

---

## 1. Whitelist Matching (`[MapNoAutoMatch]`)

Beyond default name-matching behavior, the Attribute Channel provides class-level whitelisting via `[MapNoAutoMatch]`: when applied, **only explicitly declared `[MapProperty]` members participate in mapping**, and all other identically named properties—even if matching in name and type—are excluded. This is functionally equivalent to `.NoAutoMatch()` in the Fluent Channel (demonstrated in scenario `101-no-auto-match`). It is ideal for scenarios requiring strict field control—e.g., “only map fields explicitly declared, prevent accidental mapping of newly added same-named properties.”

## 1. Single-Object and List Mapping

### Usage

Once a target type is annotated with `[MapFrom]`, calling `Mapper.Map` performs automatic name-based property copying.

```csharp
public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}
```

Single-object mapping —  

[!code-csharp[Single-object mapping](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

List mapping —  

[!code-csharp[List mapping](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ListMap)]

With `Action` callback: modify the target immediately after mapping —  

[!code-csharp[Mapping with Action callback](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapWithAction)]

Updating an existing object: no new instance is created —  

[!code-csharp[Updating an existing object](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_UpdateExisting)]

The equivalent Fluent declaration for the same type pair (declared in a configuration class, without modifying the DTO) is:

```csharp
[MapperConfig]
public class UserMapperConfig
{
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<UserEntity, UserDto>();      // Equivalent to [MapFrom(typeof(UserEntity))]; choose one
}

// Call site remains unchanged: Mapper.Map<UserEntity, UserDto>(entity)
```

**Key Points**:

- `[MapFrom]` is placed on the **target type**, specifying the source type.
- Properties with identical names and types are mapped automatically—no extra configuration required.
- `MapList` maps the entire list and returns a **new list instance**.
- The `Action` callback executes immediately after mapping completes—avoiding a second enumeration pass.
- When updating an existing object, source property values overwrite corresponding target properties.

### 1.1 Automatic Collection Property Mapping

When DTO and Entity share collection-typed properties with identical names, element-wise mapping occurs automatically:

```csharp
public class OrderEntity
{
    public int OrderId { get; set; }
    public List<OrderItemEntity> Items { get; set; } = new();
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public int OrderId { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}
// Items elements are automatically mapped from OrderItemEntity → OrderItemDto
```

Collection types may differ (e.g., `List<T>` → `IList<U>`), and conversion is handled automatically. `Queue<T>` and `Stack<T>` preserve element order. `ObservableCollection<T>` creates a new instance suitable for UI binding.

**Common Misuses**:

```csharp
// ❌ Direct List → Dictionary mapping is unsupported (key source is ambiguous)
var dict = Mapper.MapList<UserEntity, UserDto>(entities);  // Returns List<UserDto>, not Dictionary

// ✅ Map to List first, then manually ToDictionary
var list = Mapper.MapList<UserEntity, UserDto>(entities);
var dict = list.ToDictionary(d => d.Id);

// ❌ Modifying the source collection post-mapping does NOT synchronize with the target
dtos = Mapper.MapList<UserEntity, UserDto>(entities);
entities[0].Name = "Modified";  // dtos[0].Name remains unchanged—mapping produces independent copies
```

The complete matrix of supported collection types and internal engine mechanics are detailed in [Mapping Rules (Semantics): Collections & Dictionaries](../../explanation/semantics/mapping-rules.md).

> ⚠️ Version Note: Compile-time mapping code requires a **mapping declaration anchor**—either a `[MapFrom]` attribute on the DTO, or a `For<S,T>()` declaration in a Fluent configuration class. If no anchor exists, the runtime expression tree engine still works silently—but any property-level annotations (e.g., `[MapProperty]`, `[Convert]`) **are ignored entirely**, and compilation fails with **NCMAP019 Error**. Purely undeclared types (with zero annotations) emit only **NCMAP042 Info** (DTO naming heuristic).

---

## 2. Mismatched Field Names and Custom Conversions

### Usage

When source and target property names differ, use `[MapProperty]` to declare the correspondence. For multi-field composition or formatting logic, use `[Convert]`.

> **Class-Level Anchor Requirement (Mapping Configurations Follow the Class Anchor)**: Property-level attributes (`[MapProperty]`, `[Convert]`, `[MapIgnore]`, etc.) take effect **only if the containing class has a mapping declaration anchor**—i.e., either `[MapFrom]` on the class itself, or a `For<S,T>()` declaration in a Fluent config class where this class participates as either source or target. Without such an anchor, property-level attributes are ignored (compile-time `NCMAP019` error). In the example below, `UserDetailDto` is annotated with `[MapFrom]`, so its property-level attributes are active.

```csharp
[MapFrom(typeof(UserEntity))]
public class UserDetailDto
{
    // Mismatched mapping: source Name → target DisplayName
    [MapProperty(typeof(UserEntity), nameof(UserEntity.Name))]
    public string DisplayName { get; set; } = "";

    // Enum → string conversion (built-in)
    [MapProperty(typeof(UserEntity), nameof(UserEntity.Status))]
    public string StatusText { get; set; } = "";

    // DateTime → formatted date string (combined [MapProperty] + [Convert])
    [MapProperty(typeof(UserEntity), nameof(UserEntity.CreatedAt))]
    [Convert(typeof(DateTimeFormatter), nameof(DateTimeFormatter.Convert))]
    public string RegisterDate { get; set; } = "";

    // Standalone [Convert]: no corresponding source property—derives value from the source parameter
    [Convert(typeof(FullNameConverter), nameof(FullNameConverter.Convert))]
    public string FullName { get; set; } = "";
}
```

**Key Points**:

- In `[MapProperty(typeof(T), nameof(T.Field))]`, the first type argument `T` is the **actual source type from which the field value is read**:  
  - If `T` matches the primary source declared in `[MapFrom]`, it’s a standard same-source mapping (most common).  
  - If `T` is a different type, it enables cross-table or composed mappings—**no requirement to match the primary source**, and no error is raised.
- `[Convert]` may be used standalone (no source property) or combined with `[MapProperty]`.
- Converter methods must be static and conform to recognized signature templates (T0–T3); they can be reused across multiple mappings.
- Enum-to-string conversion is built-in—no custom `[Convert]` needed.

### 2.1 Reverse Custom Conversion (`[ConvertBack]`)

`[Convert]` applies only to forward mapping (Entity → DTO). For reverse mapping (DTO → Entity), annotate the DTO property with `[ConvertBack(typeof(ConverterType), nameof(ConverterType.Method))]`. Placing `[Convert]` and `[ConvertBack]` on the same property is valid and safe—forward uses `[Convert]`, reverse uses `[ConvertBack]`, with no interference.

```csharp
// Forward converter: DateTime → "yyyy-MM-dd" (T1 signature)
public static class DateFormatter
{
    public static string Convert(DateTime fieldValue) => fieldValue.ToString("yyyy-MM-dd");
}

// Reverse converter: string → DateTime parsing (T1 signature)
public static class DateParser
{
    public static DateTime Convert(string fieldValue)
        => DateTime.ParseExact(fieldValue, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    [MapProperty(typeof(UserEntity), nameof(UserEntity.CreatedAt))]
    [Convert(typeof(DateFormatter), nameof(DateFormatter.Convert))]      // Forward: formatting
    [ConvertBack(typeof(DateParser), nameof(DateParser.Convert))]        // Reverse: parsing
    public string CreatedDate { get; set; } = "";
}
```

**Key Points**:

- Method signature templates (T0–T3) and accessibility requirements for `[ConvertBack]` match those for `[Convert]` (see [Mapping Rules (Semantics): Type Conversion Chains](../../explanation/semantics/mapping-rules.md)).
- Parsing conversions like `string → DateTime` have **no built-in support**, due to format and culture variability requiring business-specific decisions—custom converters are mandatory.
- Converter methods should be pure functions (no side effects); otherwise, mapping results become order-dependent and hard to test.

**Common Misuses**:

```csharp
// ❌ Method is private: triggers NCMAP035 Error (no accessible static method found on anchored type)
public class BadConverter
{
    private static string Convert(int value) => "";
}

// ❌ Using single-parameter nameof(OtherType.M) truncates: nameof yields only "M", discarding OtherType silently.  
//    When the method resides in another type, always use the two-parameter form:  
//    [Convert(typeof(OtherType), nameof(OtherType.M))]
```

---

### 2.2 JSON Column Mapping (`IsFromJson`)

When a database stores structured objects as JSON strings in a column, use `[MapProperty(IsFromJson = true)]` to enable bidirectional **object ↔ JSON string** conversion—no manual serialization/deserialization required.

```csharp
public class ProductEntity
{
    public int Id { get; set; }
    // JSON string column in database
    public string? SpecJson { get; set; }
}

// string (source) → object (target): deserializes JSON
[MapFrom(typeof(ProductEntity))]
public class ProductDto
{
    [MapProperty(typeof(ProductEntity), nameof(ProductEntity.SpecJson), IsFromJson = true)]
    public ProductSpec? Spec { get; set; }
}

// object (source) → string (target): serializes to JSON
[MapFrom(typeof(ProductDto))]
public class ProductEntity2
{
    [MapProperty(typeof(ProductDto), nameof(ProductDto.Spec), IsFromJson = true)]
    public string? SpecJson { get; set; }
}
```

**Behavior Rules**:

- Supports **object ↔ string** only: the object side must be a complex reference type (neither `string` nor a value type).
- `string → string`: `IsFromJson` has no effect—direct assignment occurs.
- Combinations involving value types are unsupported: the property is skipped, and the skip reason is logged via `MapTracer` (visible when `GloballyEnabled` is set).
- Empty or `null` JSON: target receives `default` (`default(T)` for value types, `null` for reference types).
- Malformed JSON: deserialization failure throws `MapException`, wrapping the underlying `JsonException`.

**Key Points**:

- `IsFromJson` sets `RequiresDowngrade = true`—ORM projections (e.g., `AsDto()`) encountering this attribute automatically **downgrade to in-memory mapping**, because SQL cannot express JSON deserialization.
- JSON serialization strategy is replaceable: see [§2.3 Replaceable JSON Serialization Strategy](#23-replaceable-json-serialization-strategy).

### 2.3 Replaceable JSON Serialization Strategy

NCMapper’s JSON bridging (`IsFromJson` mapping + JSON deep copy) uses `System.Text.Json` by default. You can replace it with another implementation (e.g., Newtonsoft.Json) via `JsonSerializerBase.Current`:

```csharp
public class NewtonsoftJsonSerializer : JsonSerializerBase
{
    public override string Serialize(object? obj) =>
        Newtonsoft.Json.JsonConvert.SerializeObject(obj);
    public override object? Deserialize(string json, Type targetType) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject(json, targetType);
}

// Global replacement
JsonSerializerBase.Current = new NewtonsoftJsonSerializer();
```

**Key Points**:

- `JsonSerializerBase.Current` is a `static` global instance, defaulting to `SystemTextJsonSerializer`.
- After replacement, *all* JSON bridging mappings and JSON deep copies use the new instance.
- Configuration is code-only (not via `NCMapperOptions`).
- ⚠️ **AOT Note**: `Serialize`/`Deserialize` in `JsonSerializerBase` are annotated with `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]` — reflection-based JSON serialization is unavailable under NativeAOT; use source-generated `JsonSerializerContext` instead.

> **Related API**: `JsonConverter` — an engine-side helper for JSON interconversion (expression-tree lambdas and source-generated code use it to convert between objects and JSON strings; internally delegates to `JsonSerializerBase.Current`. Replacing `Current` replaces its underlying serialization implementation). (Source anchor: JsonConverter.cs:8)

---

## 3. PascalCase Flattening

### Usage

Target property names are automatically split on PascalCase boundaries and matched against nested paths in the source object.

```csharp
public class UserEntity
{
    public Address? Address { get; set; }
}
public class Address { public string City { get; set; } public string Street { get; set; } }

[MapFrom(typeof(UserEntity))]
public class UserDetailDto
{
    public string AddressCity { get; set; } = "";    // → src.Address?.City
    public string AddressStreet { get; set; } = "";  // → src.Address?.Street
}
```

**Key Points**:

- Splitting rule: `AddressCity` → `Address.City`
- Supported across source generators, ORM projections, and runtime `Mapper.Map`; also supports same-name matching.
- Source generator emits `src.Address?.City` expressions at compile time, null-safe.

> ⚠️ TFM Differences: PascalCase flattening behaves identically across all TFMs. The source generator processes it at compile time (generating null-safe chain `src.Address?.City`); runtime `Mapper.Map` and ORM projections share the `BuildPathCandidates` algorithm (case-insensitive ordinal segment matching, forward-only).

### 3.1 Multi-level Nested Flattening

PascalCase flattening supports path depths of 2–5 segments and auto-generates null-conditional (`?.`) chained access:

```csharp
public class OrderEntity
{
    public Customer? Customer { get; set; }
}
public class Customer
{
    public Address? Address { get; set; }
}
public class Address
{
    public string City { get; set; } = "";
}

[MapFrom(typeof(OrderEntity))]
public class OrderDetailDto
{
    // 2-segment: src.Customer?.Address
    public string? CustomerAddress { get; set; }

    // 3-segment: src.Customer?.Address?.City
    public string? CustomerAddressCity { get; set; }

    // 4-segment: src.Customer?.Address?.City?.Length (value-type leaf nodes auto-apply ?? default)
    public int CustomerAddressCityLength { get; set; }
}
```

**Splitting Rules**:

- Bitmask-based enumeration splitting: property names are split at uppercase letter boundaries (e.g., `CustomerAddressCity` → `Customer` + `Address` + `City`).
- Supports 2–5 path segments; splits beyond 5 segments are discarded.
- Consecutive uppercase letters exceeding 4 characters (e.g., `HTTPResponse`) are not split to avoid mis-splitting acronyms.
- Value-type leaf nodes automatically append `?? default(int)` to prevent null-related exceptions.
- **Directional Constraint**: Forward direction is read-only (nested path → flattened property); reverse direction (DTO → Entity) performs **write-back** (flattened property → nested path assignment): intermediate `null` nodes are auto-instantiated; `null` source values skip assignment entirely; `[Convert]` is an exception (outputs `null`, no assignment). Collection indices and interface-typed intermediate nodes are skipped, with diagnostics/tracing.

> ⚠️ **Relationship with Projection**: ORM projections (`AsDto`) cannot translate `?.` expressions into SQL. In projection contexts, use navigation properties + `[MapProperty]` instead of flattening.

---

## 4. Bidirectional Mapping and Direction Control

### Usage

After applying `[MapFrom]`, both forward and reverse mapping are available without duplicate configuration. `[MapIgnore]` controls whether a specific property participates in mapping in a given direction.

```csharp
[MapFrom(typeof(CustomerEntity))]
public class CustomerDto
{
    public string Name { get; set; } = "";
    public string Level { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Region { get; set; } = "";

    // Ignored during reverse mapping (DTO → Entity)
    [MapIgnore(Direction.ToEntity)]
    public string DisplayLabel { get; set; } = "";
}
```

Forward mapping (executable version, sample scenario 201 `ForwardMap`) —  

[!code-csharp[Forward mapping: Entity → DTO](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ForwardMap)]

Executable reverse-mapping verification (sample scenario 201 `BackwardMap`, run via `dotnet run --scenario 201`; `DisplayLabel` is not written to the Entity):

[!code-csharp[Reverse mapping verification: \[MapIgnore(Direction.ToEntity)\] takes effect](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_MapIgnore)]

**Key Points**:

- `Direction` enum values: `ToDto` (ignore when mapping Entity → DTO), `ToEntity` (ignore when mapping DTO → Entity), `Both` (ignore bidirectionally). (`ToTarget` is a legacy alias, implemented as `ToDto`.)
- Bidirectional mapping requires `[MapFrom]` on only one side.
- **Side Exclusivity**: A class may be annotated with `[MapFrom]` at most once (duplicate annotations trigger compile-time error CS0579); globally, only one anchor exists per type pair. In Fluent configuration, declaring both `For<A, B>` and `For<B, A>` is disallowed (`NCMAP025` diagnostic).

---

## 5. Source Generator Extension Methods

### Usage

The source generator emits extension methods (in namespace `NCMapper.Generated`) for each `[MapFrom]` type pair — zero-delegate, direct calls.

```csharp
using NCMapper.Generated;

var dto = entity.MapToUserDto();                          // Creates new object
var dto = entity.MapToUserDto((src, tgt) => { ... });     // With callback
entity.MapToUserDto(existingDto);                         // Updates existing object (void)
```

**Call Path Comparison**:

| Call Style | Path | Performance |
|---|---|---|
| `Mapper.Map<UserEntity, UserDto>(entity)` | Looks up delegate registry → invokes generated code | ~17–29 ns |
| `entity.MapToUserDto()` | Direct call to generated static method, zero delegate | ~13–27 ns |

Results are identical; only the call path differs. Extension methods eliminate one dictionary lookup, ideal for hot paths.

### 5.1 `[ModuleInitializer]` Pre-registration

The source generator emits a `GeneratedMapperInitializer` class using the `[ModuleInitializer]` attribute to auto-register all mapping delegates into the `Mapper` static class upon module load:

```csharp
// Auto-generated by source generator (no user code required; CodeGenerator.GenerateModuleInitializer)
[System.Runtime.CompilerServices.ModuleInitializer]
internal static void Initialize()
{
    NCMapper.Mapper.RegisterGeneratedCore<UserEntity, UserDto>(GeneratedMapper.Map_UserEntity_To_UserDto);
    NCMapper.Mapper.RegisterGenerated<UserEntity, UserDto>(GeneratedMapper.Map_UserEntity_To_UserDto_WithAction);
    // ... All [MapFrom] type pairs (method name template: Map_{Source}_To_{Target})
}
```

**Key Points**:

- `[ModuleInitializer]` executes automatically at assembly load — no manual registration needed in `Program.cs`.
- This enables `Mapper.Map<T,S>(src)` to use source-generated paths — pre-registration populates the delegate lookup table.
- `Mapper.ClearCache()` clears only runtime caches, *not* pre-registered source-generated delegates.
- Extension methods and `Mapper.Map` share the same source-generated mapping logic — performance is effectively equivalent.

### 5.2 Full Set of Source Generator Extension Methods

Each `[MapFrom]` type pair generates six extension methods:

| Method | Description | Return Type |
|---|---|---|
| `src.MapToXxxDto()` | Creates new object | `XxxDto` |
| `src.MapToXxxDto(Action<src,dto>)` | Creates with callback | `XxxDto` |
| `src.MapToXxxDto(dto target)` | Updates existing object | `void` |
| `src.MapToXxxDto(dto target, Action<src,dto>)` | Updates with callback | `void` |
| `srcs.MapToXxxDtoList()` | List mapping | `List<XxxDto>` |
| `srcs.MapToXxxDtoList(Action<src,dto>)` | List mapping with callback | `List<XxxDto>` |

> **Reverse Direct Calls (DTO → Entity)**: `[MapFrom]` DTOs likewise generate symmetric reverse overloads (e.g., `dto.MapToXxxEntity()`, `MapToXxxEntity(Action)`, `MapToXxxEntity(entity)` for update, `MapToXxxEntity(entity, Action)`, `dtos.MapToXxxEntityList()`, `MapToXxxEntityList(Action)`), with method names following `MapTo{forward-source-type-name}`. DTOs declaring cross-table sub-source fields also generate these; reverse mapping updates only primary-source fields ([Compose §5.1](../compose/compose.md)).

[!code-csharp[Bidirectional update: zero heap allocation + CRUD write-back](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#Declarative_ReverseMap_Pair)]

> 💡 **Performance Tip**: For CRUD scenarios, prefer `MapToXxxDto(target)` to update existing objects — avoids heap allocations from new object creation. The source-generated `_Update` method directly invokes the generated static method: zero delegate + zero heap allocation.

---

## Common Misuses

### Misuse 1: DTO Without Mapping Declaration (or Annotated but Unanchored)

```csharp
// ❌ Misuse A: DTO lacks mapping declaration → source generator emits no mapping code
public class UserDto { ... }
var dto = Mapper.Map<UserEntity, UserDto>(entity);
// → NCMAP042 Info: UserDto name resembles a DTO but has no mapping declaration (DTO naming heuristic)

// ❌ Misuse B: Class contains property-level configuration attributes but lacks mapping anchor
public class UserDto
{
    [MapProperty(typeof(UserEntity), nameof(UserEntity.Name))]
    public string DisplayName { get; set; } = "";
}
// → NCMAP019 Error: Mapping configuration attribute applied to class lacking mapping anchor (no anchor → no consumption, annotation ignored)

// ✅ Correct: Declare [MapFrom(typeof(SourceType))] or declare For<S,T>() in Fluent config
[MapFrom(typeof(UserEntity))]
public class UserDto { ... }
```

### Misuse 2: `[MapProperty]` Type Incompatibility

```csharp
// ❌ Misuse: Target property type incompatible with source field type
[MapFrom(typeof(UserEntity))]
public class UserDetailDto
{
    [MapProperty(typeof(UserEntity), nameof(UserEntity.Name))]
    public int DisplayName { get; set; }  // string → int assignment impossible
}
// → NCMAP002 Warning: Target property type (int) incompatible with source field type (string)

// ✅ Correct: Target property type compatible with source field type
[MapProperty(typeof(UserEntity), nameof(UserEntity.Name))]
public string DisplayName { get; set; } = "";
```

> 💡 Explanation: The first type parameter of `[MapProperty]` must be the **actual source type from which the field is read** — equal to the `[MapFrom]` primary source for same-source mapping (most common); equal to another source type for **cross-table / Compose mapping** (e.g., reading a field from Compose’s second source). This usage is fully supported and *does not* trigger errors just because it differs from the `[MapFrom]` source. `NCMAP002` fires *only* when the target property type and the specified source field type are assignment-incompatible — it is unrelated to whether the first parameter matches the `[MapFrom]` source. If the field name does not exist in the declared source type, same-source cases report `NCMAP001`, cross-table cases report `NCMAP005`.

> `NCMAP001` detects that the source field named in `[MapProperty]` does not exist in the declared source type (compile-time Warning); same-source missing-field reports `NCMAP001`, cross-table missing-field reports `NCMAP005`. `NCMAP002` detects assignment incompatibility between the target property type and the `[MapProperty]`-declared source field type. Configuration attributes without a mapping anchor trigger `NCMAP019` (Error: no anchor → no consumption); purely undeclared types trigger only `NCMAP042` (Info). See the full diagnostic index at [Diagnostic Rules Index](../../diagnostics/rules.md).

### Misuse 3: Reverse Mapping with PascalCase-Flattened Properties

```csharp
// ❌ Misuse: Reverse mapping a DTO containing PascalCase-flattened properties
[MapFrom(typeof(UserEntity))]
public class UserDetailDto
{
    public string AddressCity { get; set; } = "";  // Flattened property
}

// During reverse mapping, AddressCity has no corresponding property in UserEntity and is silently skipped.
var entity = Mapper.Map<UserDetailDto, UserEntity>(dto);

// ✅ Correct: Flattened properties are intended for forward mapping only; declare object properties when reverse mapping is required.
[MapFrom(typeof(UserEntity))]
public class UserDetailDto
{
    public Address? Address { get; set; }  // Object property enabling bidirectional mapping
}
```

### Misuse 4: Concern That `[MapIgnore]` Impacts Forward-Mapping Performance

`[MapIgnore]` is resolved during the mapping expression construction phase and introduces no runtime branching — adding any number of such attributes has zero impact on runtime performance. It serves purely as a compile-time logical control mechanism.

The **priority order when multiple mechanisms apply to the same property** (Ignore > Custom Conversion > Name-Routing > Name-Matching) and the full contract for reverse mapping are detailed in [Mapping Rules (Semantics)](../../explanation/semantics/mapping-rules.md).

---

## Summary

| Capability | API | Feature |
|---|---|---|
| Single-object mapping | `Mapper.Map<TSource, TTarget>(src)` | `[MapFrom]` |
| List mapping | `Mapper.MapList<TSource, TTarget>(srcs)` | `[MapFrom]` |
| Callbacks | `Mapper.Map(src, Action)` | — |
| Updating existing objects | `Mapper.Map(src, target)` | — |
| Mismatched field names | — | `[MapProperty]` |
| Custom conversions | — | `[Convert]` (static method reference) |
| JSON column mapping | `[MapProperty(IsFromJson = true)]` | Bidirectional object ↔ string mapping; triggers projection downgrade |
| JSON serialization strategy | `JsonSerializerBase.Current` | Replaceable with Newtonsoft.Json |
| Flattening | — | PascalCase naming convention (source generator) |
| Bidirectional mapping | Forward & reverse `Mapper.Map` | `[MapFrom]` |
| Direction control | — | `[MapIgnore(Direction)]` |
| Zero-delegate direct invocation | `src.MapToXxxDto()` | Source-generator extension methods |

---

## Companion Examples

The sample code for this article resides in `samples/NCMapper.Samples/02-Declarative/`:

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 201
```

Core example source code (single-point, same-source; runnable and verifiable via `dotnet run --scenario`):

[!code-csharp[Single-object mapping](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

[!code-csharp[Updating existing objects](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_UpdateExisting)]

[!code-csharp[Reverse direct-invocation overloads overview](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ReverseExtensions)]

The core logic resides in `BasicScenarios.cs`, where each static method corresponds to a distinct sub-scenario.

---

## Further Reading

- Related topics: [Mapping Rules (Semantics)](../../explanation/semantics/mapping-rules.md) · [advanced](advanced.md)
- In-depth technical documentation: Chapter 5 of the [Comprehensive Technical Reference](../../reference/features.md)
