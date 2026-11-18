> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
title: Feature System
---

<!-- Source: NCMapper Main Technical Documentation §5 Feature System (Structural Reorganization, Chapter-level Migration on 2026-08-19; Content Unrearranged) -->

## 5. Feature System

> **Declarative Dual Channels**: This section describes the **Attribute Channel**, where DTO classes are annotated with attribute families such as `[MapFrom]`, `[MapProperty]`, and `[Convert]`. NCMapper also provides a **Fluent Channel**, wherein `FluentMappingConfig` is configured via method chaining inside a class marked with `[MapperConfig]` (supporting Member three-part syntax plus per-pair terminal options like `Tree` and `NoAutoMatch`). Both channels coexist long-term (type-pair conflicts are mutually exclusive under NCMAP_F11); for details, see [Fluent Entry Point](../how-to/fluent/basics.md).

### 5.1 `[MapFrom]` — Class-Level Annotation

```csharp
[MapFrom(typeof(UserEntity))]
public class UserDto { ... }
```

- Applied to the DTO class to declare its primary source type for mapping.
- May be used only once per DTO.
- Valid only on `class` types.
- Bidirectional mapping: NCMapper performs property-name-based matching. Annotating just one side suffices for bidirectional mapping. For example, if `UserDto` is annotated with `[MapFrom(typeof(User))]`, both `Map<User, UserDto>` and `Map<UserDto, User>` will work—no need to annotate `User`.

### 5.2 `[MapProperty]` — Property-Level Annotation

```csharp
[MapProperty(typeof(OtherEntity), nameof(OtherEntity.OtherName))]
public string Name { get; set; }
```

| Parameter     | Description                                      |
|---------------|--------------------------------------------------|
| `fromSource`  | The source type (may differ from the primary source type). |
| `fromField`   | The field name in the source type.               |
| `IsFromJson`  | Whether JSON serialization/deserialization should be used as an intermediary (default: `false`). |

**Detailed Explanation of `IsFromJson`**

When structured data is stored in database JSON text columns, setting `IsFromJson = true` enables the mapper to automatically convert between `string` and objects:

```csharp
// Entity — ShippingAddressJson column stores '{"City":"Shanghai","Street":"..."}'
public class OrderEntity
{
    public int Id { get; set; }
    public string ShippingAddressJson { get; set; }  // JSON text column
    public string TagsJson { get; set; }             // JSON text column
}

// DTO — Deserialized into strongly-typed objects
[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public int Id { get; set; }

    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.ShippingAddressJson), IsFromJson = true)]
    public ShippingAddress? ShippingAddress { get; set; }  // string → object

    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.TagsJson), IsFromJson = true)]
    public List<string>? Tags { get; set; }                // string → collection
}
```

Behavioral Rules:

- **Forward direction** (`string` source → reference-type target): Invokes `JsonSerializer.Deserialize` to deserialize the JSON string into the target object. Throws `JsonException` on invalid JSON or unrecoverable deserialization errors (data corruption must be surfaced immediately—not silently swallowed). Extra or missing properties at the property level are handled by the serializer’s default policy; the mapping layer performs no additional validation.
- **Reverse direction** (reference-type source → `string` target): Invokes `JsonSerializer.Serialize` to serialize the object into a JSON string. If the source is `null`, the target receives an empty string (`""`).
- **`string` → `string`**: Direct assignment; `IsFromJson` has no effect.
- **Value types unsupported**: If a value type (`int`, `DateTime`, `Guid`, etc.) appears on either side, the property is skipped (left at `default`) and a Trace-level diagnostic message is logged. Value-type fields should use same-name mapping or `[MapProperty]` with explicit renaming—JSON round-tripping is unnecessary.
- **`null` guard**: In the forward direction (`string` source → reference-type target), if the source JSON string is `null` or whitespace-only (`string.IsNullOrWhiteSpace`), the target is assigned `null` directly—bypassing deserialization entirely (to avoid misinterpreting blank input as malformed JSON and throwing `JsonException`). In the reverse direction (reference-type source → `string` target), a `null` source yields an empty string (as above).
- **ORM projection fallback**: Fields marked with `IsFromJson` cannot be translated to SQL. When EF Core or FreeSql encounters such fields during projection, they automatically fall back to in-memory mapping (two-step query).
- **Replaceable serialization strategy**: Uses `System.Text.Json` by default. Alternate serialization implementations can be substituted by replacing `JsonSerializerBase.Current`.

**String Path Support and Strongly-Typed Syntax**

The `fromField` parameter of `[MapProperty]` accepts a string and **fully supports string paths**: simple property names (`"DeptName"`), nested dot notation (`"Address.City"`), and `"Type.Property"` combinations—all supported consistently across the four engines: Source Generator, Runtime, Projection, and Compose (SourceGen). String paths provide dynamic flexibility; however, the strongly-typed `typeof` + `nameof` syntax is recommended, as both forms are semantically equivalent:

- `typeof(OtherEntity)` ensures compile-time verification of the source type—renaming the type automatically updates references.
- `nameof(OtherEntity.OtherName)` ensures compile-time verification of the property name—renaming the property automatically updates references.
- String paths (e.g., `"OtherEntity.OtherName"`) lack compile-time property checking and require manual synchronization during refactoring—thus, the **strongly-typed syntax is the preferred choice**, while string paths remain suitable for dynamic or simplified scenarios.

> ⚠️ **Dot-Path Behavior Boundaries**: The `fromField` parameter of `[MapProperty]` supports **simple property names** (e.g., `"DeptName"`) and **dot-notation paths** (e.g., `"Address.City"`), with full alignment across all four engines: Source Generator, Runtime, Projection, and Compose (SourceGen). Dot-path failures are **explicitly observable**:  
> - The Source Generator emits **NCMAP001** (standard case) or **NCMAP005** (Compose cross-table case) as a *Warning* and omits code generation for that mapping; diagnostics include the failing segment name and containing type.  
> - At runtime, a `MapConfigurationException` is thrown (*explicit failure*).  
> - During projection, the property is skipped and a Trace message is logged.  
> Cross-table or nested navigation remains possible using explicit `[MapProperty(typeof(NavType), "Field")]`. See [§18.3 Projection Limitations](../explanation/limitations.md) for details.  
> Combining type and property name naturally expresses the `"Type.Property"` mapping semantics while preserving strong-typing reliability.

### 5.3 `[MapIgnore]` — Directional Control

```csharp
public enum Direction { Both, ToDto, ToEntity }

[MapIgnore]                           // Ignores in both directions
[MapIgnore(Direction.ToDto)]          // Ignores only when mapping Entity → DTO
[MapIgnore(Direction.ToEntity)]       // Ignores only when mapping DTO → Entity
public string InternalField { get; set; }
```

> **Direction Resolution in Code Generation (Bidirectional)**: The Source Generator evaluates `[MapIgnore]` directionality by inspecting **both source and target sides**. Specifically:  
> - `Direction.ToEntity` excludes the property *only* from the `Entity → DTO` direction (i.e., the DTO property won’t be populated from the Entity),  
> - `Direction.ToDto` excludes it *only* from the `DTO → Entity` direction.  
> When dual annotations are applied (e.g., `[MapIgnore(Direction.ToDto)]` alongside a reverse-direction `[MapProperty]`), the generator applies directional trimming independently on each side—eliminating legacy issues where single-side inspection caused unintended omission of reverse mappings.

### 5.4 [Convert] — Custom Value Conversion (Static Method Reference)

Annotates a property to specify that a **static conversion method** should be invoked during mapping, replacing the default conversion path:

```csharp
// Single-field conversion — Template T1: M(TField)
public static class DateTimeToStringConverter
{
    public static string Convert(DateTime fieldValue) => fieldValue.ToString("yyyy-MM-dd");
}

// Multi-field composition — Template T2: M(TSource)
// FullName = FirstName + " " + LastName
public static class FullNameCalculator
{
    public static string Convert(UserEntity source)
        => $"{source.FirstName} {source.LastName}";
}

// Annotated on the property (two-parameter form — recommended)
[Convert(typeof(DateTimeToStringConverter), nameof(DateTimeToStringConverter.Convert))]
public string CreatedAt { get; set; }

// One-parameter form: method must be declared in the same type as the annotated property
[Convert(nameof(Convert))]
public string FullName { get; set; }
```

**Signature Templates** (automatically detected):

| Template | Signature | Use Case |
|---|---|---|
| T0 | `M()` | Constant / fixed value |
| T1 | `M(TField)` | Single source field value (reusable across entities) |
| T2 | `M(TSource)` | Compose multiple fields from the source object |
| T3 | `M(TSource, TField)` | Composition + the field value itself |

| Concept | Description |
| ------------------------------------------- | ------------------------------------------ |
| Anchor Type | The type declaring the static conversion method (specified in two-parameter form); in one-parameter form, equals the type containing the annotated property |
| `[Convert]` | Property-level attribute specifying the static converter method (`typeof` + `nameof` for two-parameter form / `nameof` alone for one-parameter form) |
| `fieldValue` | The matched source field value (passed as `default(TField)` if no matching source property exists) |
| `source` | The entire source entity object (used for multi-field composition, conditional mapping, etc.) |
| Execution Order | Takes precedence over type compatibility checks, implicit conversions, and `TypeConvertHelper` fallback |

**Method Resolution Rules** — The method must be `static`, declared directly in the anchor type (inherited static methods from base types are *not* allowed), and accessible (`public` / `internal` within the same assembly / `protected internal` visible within the same assembly). Overloads must resolve uniquely to exactly one template: zero matches → **NCMAP037**; multiple matches → **NCMAP036**; method does not exist or is inaccessible → **NCMAP035** (Error, compilation-blocking). There is *no* overload-resolution syntax based on signature — rename or remove conflicting overloads instead.

> **`nameof` Truncation Warning (One-Parameter Form)**: `nameof(OtherType.M)` compiles to `"M"` only — `OtherType` is silently discarded. When the method resides in another type, *always* use the two-parameter form `[Convert(typeof(X), nameof(X.M))]`; otherwise, **NCMAP035** will be reported with a corrective guidance message (no silent misbinding).

**Purpose** — The default conversion path only supports automatic name-and-type-matching, implicit conversions, and `TypeConvertHelper` fallback. It cannot express **custom logic** required by target properties — such as formatting, cross-field composition, conditional mapping, or unit conversion. `[Convert]` provides a declarative extension point: encapsulate conversion logic in static methods — visible at compile time, reusable, type-safe, and directly invoked via source-generated code.

**Typical Scenarios**:

| Scenario | Example |
| ------ | --------------------------------------- |
| Formatting | `DateTime` → `"yyyy-MM-dd"` string |
| Multi-field composition | `FullName = FirstName + " " + LastName` |
| Enum ↔ String | Enum with localized descriptions |
| Encryption / Masking | Password hashing, phone number masking |
| Unit conversion | Celsius → Fahrenheit, cents → yuan |
| Conditional mapping | Determine target value based on `source.Status` |
| Cross-field calculation | `BMI = Weight / (Height/100)²` |
| Pure computed property | Target property has *no corresponding field* in source — computed entirely from `source` |

**No-Source-Property Scenario** — When the target property has *no matching field* in the source entity, the `[Convert]`-annotated method **is still invoked**, with `fieldValue` passed as `default(TField)` (for T1/T3 templates). The conversion method computes the result using the `source` parameter. The above `FullNameCalculator` example falls into this category (`FullName` has no corresponding field in the source).

**Trade-off vs. Action Callbacks**:

| Dimension | `[Convert]` | Action Callback |
| ----- | ---------------- | ------------- |
| Style | Declarative, encapsulated as static method | Imperative, inline lambda |
| Reusability | Defined once, annotated in many places | One-time, bound to a single invocation |
| Compile-time validation | Source generator validates method existence, accessibility, and signature | None |
| Performance | Source generator emits inlined static method calls | Runtime delegate invocation |

> Use `[Convert]` for reusable, generic conversion logic (e.g., date formatting, unit conversion); use Action callbacks for one-off, mapping-specific logic.

**Performance & AOT** — The source generator emits **inlined static method calls** (no singleton fields) per conversion point — **zero reflection, zero allocations, AOT-friendly**. **Constraint**: conversion methods must be stateless pure functions (must not access mutable static state); for stateful logic, use Action callbacks instead.

**Reverse Direction ([ConvertBack])** — `[Convert]` binds only the **forward** direction. By default, the reverse direction (DTO → Entity) **does not consume** `[Convert]` (compilation-time **NCMAP033** Info diagnostic, including migration guidance) and falls back to standard type-conversion paths. To customize reverse conversion, use the symmetric attribute `[ConvertBack(typeof(X), nameof(X.M))]`:

```csharp
// Reverse converter: In T2/T3 templates, `TSource` = DTO type (the source object in reverse mapping)
public static class BackConverter
{
    public static decimal Convert(string fieldValue) => decimal.Parse(fieldValue);
}

[ConvertBack(typeof(BackConverter), nameof(BackConverter.Convert))]
public decimal Amount { get; set; }
```

| Annotation Combination | Forward (Entity → DTO) | Reverse (DTO → Entity) |
|-------------------------|------------------------|-------------------------|
| Only `[Convert]` | Consumes `[Convert]` | Does *not* consume `[Convert]` (**NCMAP033**), uses type-conversion path |
| Only `[ConvertBack]` | Uses type-conversion path | Consumes `[ConvertBack]` |
| Both present | Consumes `[Convert]` | Consumes `[ConvertBack]` |

- Errors resolving reverse conversion methods or signature mismatches report **NCMAP035/036/037** (Error, compilation-blocking).

### 5.5 [Mask] — Property-Level Data Masking

Annotates a `string` property on a DTO to automatically apply masking during mapping. Similar to `[Convert]` (declarative conversion point + direct call), but semantically clearer — it expresses "*this field requires masking*", not "*this field requires type conversion*".

```csharp
// Interface definition
public interface IMask
{
    string Mask(string? value);
}

// Annotated on the property
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public string Name { get; set; }

    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; }       // 138****5678

    [Mask(typeof(MaskIdCard))]
    public string IdCard { get; set; }      // 1101**********1234

    [Mask(typeof(MaskEmail))]
    public string Email { get; set; }       // z***@example.com
}
```

#### Built-in Masking Implementations

| Class Name | Masking Rule | Example |
| ------------------ | ------------------------------------- | -------------------- |
| `MaskPhone` | Keep first 3 and last 4 digits; replace middle with `****` | `138****5678` |
| `MaskIdCard` | Keep first 4 and last 4 characters; replace middle with equal-length `*` | `1101**********1234` |
| `MaskBankCard` | Keep first 4 and last 4 digits; replace middle with `****` | `6222****1234` |
| `MaskEmail` | First character + `***` + `@` and domain | `z***@example.com` |
| `MaskChineseName` | 1-character name unchanged; 2-character = first + `*`; ≥3-character = first + equal-length `*` + last | `张*丰` |
| `MaskAddress` | Keep first 6 characters + `****` | `北京市朝阳区****` |
| `MaskIp` | Keep first 2 octets; replace last 2 with `*` | `192.168.*.*` |
| `MaskLicensePlate` | Keep first 2 and last 2 characters; replace middle with equal-length `*` | `京A***45` |
| `MaskLandline` | With `-`: keep area code + `****` + last 4; without `-`: keep first 3 and last 4 | `010-****5678` |

All built-in classes are `sealed` and stateless, satisfying the singleton-zero-allocation constraint.

#### Orthogonal Composition with [Convert]

`[Mask]` and `[Convert]` may be applied to the same property. Execution order is **convert first, then mask**:

```csharp
[Convert(typeof(DateTimeToStringConverter), nameof(DateTimeToStringConverter.Convert))]
[Mask(typeof(MaskPhone))]
public string Phone { get; set; }
// Generated code: dst.Phone = __mask_Phone.Mask(DateTimeToStringConverter.Convert(src.Phone));
```

> **Dual-Annotation Semantics**: When both `[Mask]` and `[Convert]` are applied, both engines (source generator / runtime expression tree) behave identically — always executing "`[Convert]` then `[Mask]`" (matching the generated code semantics above).

#### Directional Semantics: Forward-Only

`[Mask]` is **strictly forward-directional** — masking is applied *only* in the Entity → DTO direction (i.e., `Mask()` is called in mapping code). In the reverse direction (DTO → Entity), **no masking is performed** (reverse write-back code contains *no* `Mask()` call — consistent with `[Convert]`'s non-consumption in reverse).

However, masking is **lossy and irreversible**: after forward mapping, the DTO property holds the masked value. In a typical CRUD flow ("fetch entity → map to DTO (masked) → edit → write back to entity"), the reverse write-back will persist the **masked value** onto the entity, overwriting the original full data (round-trip is lossy). Though masking is *not executed* in reverse, its *effect* propagates to the source via write-back.

| Direction | Behavior | Diagnostic |
|------|------|------|
| Forward (Entity → DTO) | Executes masking (`Mask()` call) | — |
| Reverse (DTO → Entity) | **No masking** (no `Mask()` call); current DTO value (including previously masked value) is written back verbatim | **NCMAP034** (Info) "Masked value will be written back to target entity — masking is lossy and irreversible" |

To prevent masked-value write-back, annotate the corresponding DTO property with `[MapIgnore(Direction.ToEntity)]` (or use a global `ToEntity` ignore list); alternatively, retain an unmasked field on the DTO (e.g., annotated with `[MapIgnore(Direction.ToDto)]`) for write-back purposes. See [Reverse Mapping Semantics §4](../explanation/reverse-mapping-semantics.md) and [Masking](../how-to/infrastructure/masking.md).

#### Custom Masking

Implement the `IMask` interface:

```csharp
public sealed class MaskCustomRule : IMask
{
    // Mask method body — see binding reference below for runnable version
}

// Usage
[Mask(typeof(MaskCustomRule))]
public string SensitiveField { get; set; }
```

[!code-csharp[Custom IMask implementation (keep first/last N chars, star middle)](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_MaskCustomRule_Mask)]

#### Dynamic Replacement of Built-in Implementations

`MaskProvider.Override<TMask>()` allows replacing any built-in `IMask` implementation at application startup—**without modifying DTO attribute annotations**. This is suitable for internationalization adaptation (e.g., Japanese mobile number format `090-****-1234`) or compliance policy adjustments.

[!code-csharp[Register and replace mask implementation at startup (Override)](~/samples/NCMapper.Samples/06-Infrastructure/MaskScenarios.cs#Mask_ConvertUsing_OverrideMask)]

Constraints and Notes:

| Constraint | Description |
| --- | --- |
| Timing | Must be invoked **before the first mapping call** (the source generator’s `static readonly` field initialization does not re-parse afterward) |
| Replacement Type | Must implement the `IMask` interface (`Override<TMask>` enforces `where TMask : IMask` at compile time) |
| Thread Safety | Internally, `MaskProvider` uses a `Dictionary<Type, IMask>`; `Override` should be called on a single thread during startup |
| Clearing | `MaskProvider.Clear()` explicitly clears all registered overrides (for testing). `Mapper.ClearCache()` only clears runtime instance caches and **does not** clear override registrations—overrides have an independent lifecycle to prevent silent fallback to built-in PII masking |

> **Internal Mechanism**: The source generator calls `MaskProvider.Resolve<TMask>()` instead of `new TMask()` directly. `Resolve` first checks the `_overrides` dictionary; if found, it returns the overridden implementation; otherwise, it creates the default instance. See [`MaskProvider.cs`](~/src/NCMapper/MaskProvider.cs) for details.

#### Scope of Effectiveness

| Path | Behavior |
| --- | --- |
| `Mapper.Map` / Source Generator Extension Methods | Masking is applied |
| `Mapper.MapList` | Masking is applied per element |
| AsDto Projection (DTO defines no masking) | Uses SQL projection path (this path does not invoke CLR delegates and does not involve masking, so it has no effect) |
| AsDto Projection (DTO defines masking) | **Automatically downgrades to two-step mapping; masking is fully applied** — `[Mask]` triggers `IProjectionPolicy.RequiresDowngrade`, forcing the framework onto the downgrade path where masking is guaranteed to take effect |
| Reverse Mapping (Entity ← DTO) | No masking is applied |

> **Key Conclusion**: `[Mask]` implements `IProjectionPolicy` (`RequiresDowngrade => true`). **As long as masking is defined on the DTO, the `AsDto` terminal method will automatically switch to the downgrade path and apply masking—there is no scenario where masking is declared but fails to take effect on the normal path.** The SQL projection path is used *only* for DTOs without downgrade-triggering attributes like `[Mask]`. Once masking is declared, the framework forces downgrade and guarantees masking takes effect—no user intervention required. This behavior is uniformly detected by the `*DegradationDetector` in the FreeSql/EF Core extension packages at `AsDto` invocation time (`EfCoreDegradationDetector` / `FreeSqlDegradationDetector` both scan for `IProjectionPolicy.RequiresDowngrade`; Dapper package has no degradation detector—it always uses in-memory mapping across the full pipeline, so `Mask` is always effective).

#### Source Generator Behavior

For each `[Mask]` property, the source generator emits a `private static readonly` singleton field plus a direct `Mask()` call—zero reflection, zero extra allocations:

```csharp
// Generated code (simplified)
private static readonly MaskPhone __mask_UserDto_Phone = new();

// Assignment site
dst.Phone = __mask_UserDto_Phone.Mask(src.Phone);
```

**Compile-time Diagnostics**:  
- If the type specified by `[Mask]` does not implement `IMask`, emits `NCMAP014` (Error).  
- If the target property is not of type `string`, emits `NCMAP015` (Error).

### 5.6 `[MapTree]` — Customizing Tree Field Names

Applied to a DTO class to specify the identifier and parent-identifier field names for tree structures. Default values are `"Id"` / `"ParentId"`:

```csharp
// Default field names: Id / ParentId
[MapFrom(typeof(CategoryEntity))]
public class CategoryDto
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public string Name { get; set; }
    public List<CategoryDto> Children { get; set; }
}

// Non-default field names: customized via [MapTree]
[MapFrom(typeof(CategoryEntity))]
[MapTree("NodeId", "ParentNodeId")]
public class CategoryDto
{
    public int NodeId { get; set; }
    public int ParentNodeId { get; set; }
    public string Name { get; set; }
    public List<CategoryDto> Children { get; set; }
}
```

**Use Case**: When database table identifier and parent-identifier columns are not named `Id` / `ParentId` (e.g., legacy systems using `CategoryId` / `ParentCategoryId`, or multi-tenant systems using `NodeGuid` / `ParentGuid`), you need not rename fields in the DTO—simply declare the actual column names via `[MapTree]`.

**Default Behavior**: If `[MapTree]` is not applied, `ToTreeList` defaults to searching for `Id` / `ParentId` fields (case-sensitive).

> **Applicability Boundary**: `[MapTree]` is read only by Dapper’s `DapperDtoSelector.ToTreeList` and EF Core’s `EFCoreDtoSelector.ToTreeList`. FreeSql’s `ToTreeList` (`FreeSqlDtoSelector.ToTreeList`) wraps FreeSql’s native tree-building capability and **does not read** `[MapTree]`. EF Core’s `ToTreeList`/`ToTreeListAsync` (`EFCoreDtoSelector`) uses “flat query + in-memory tree building”, with the core library `TreeBuilder` assembling trees by `Id`/`ParentId`, reading `[MapTree]` and behaving semantically identically to Dapper. See [§11.4 Dapper Extension Package](../explanation/orm-efcore-dapper.md) and [how-to §6 Tree Queries](../how-to/orm/efcore-projection.md).

---

### 5.7 Mapping Direction and Annotation Placement Conventions

Since version 8.4, the mapping engine has strengthened semantic constraints around **direction determination** and **annotation placement**, preventing implicit errors such as “a declaration on one side affecting the other in bidirectional mapping”.

#### Direction Quadrants

Mapping is evaluated independently in two directions: **ToDto** (source → target, Entity → DTO) and **ToEntity** (target → source, DTO → Entity). `[MapFrom]` declares *which type is the primary source*, while direction is determined by the actual flow at invocation time.

- `[MapFrom(typeof(B))]` only declares “this type treats B as its primary source”; it imposes no direction restriction. Both B→A and A→B directions generate mappings independently.
- `[MapIgnore(Direction.ToEntity)]` ignores only the DTO→Entity direction; the Entity→DTO direction still maps that property.

#### Mutual Reference Pair (NCMAP025, Error-level)

If `A [MapFrom(B)]` and `B [MapFrom(A)]` hold simultaneously, they form a **mutual reference pair**. Bidirectional mapping with a unique generation source is ambiguous—the generator cannot determine a single authoritative source → **no code is generated for this pair**, and `NCMAP025` (Error, build failure) is emitted. At runtime (when source generation is disabled), name-based fallback applies. Fix: Remove `[MapFrom]` from one side to restore generation, or remove one side entirely and use `Mapper.Map` for runtime mapping. See [Diagnostic Rules](../diagnostics/rules.md) §16.

#### Reverse Matching on Opposite Side

When declaring `A.Prop1 → B.Prop2` (via `[MapProperty(typeof(B), "Prop2")]`), the engine automatically attempts reverse matching `B.Prop2 → A.Prop1` on the opposite direction (prioritizing same-name/same-type matches), requiring no duplicate annotation. If no corresponding property exists on the reverse side or types are incompatible, that direction is silently skipped (with no impact on forward mapping).

#### Annotation Placement Conventions (NCMAP026, Warning-level)

- `[MapFrom]` must be placed on the **primary source class** (same side as generated code).
- `[Mask]` must be placed on the **target-side property** (same side as `[MapFrom]`). Placing `[Mask]` on the *opposite side* (i.e., source side) **has no effect** (masking is unidirectional: only targets `string` values), and `NCMAP026` warns to relocate it to the correct position.
- Direction semantics for `[MapIgnore]` / `[Convert]` / `[MapProperty]` are covered in §5.3 / §5.4 / §5.2.

#### `IsFromJson` Fallback

When `[MapProperty(IsFromJson=true)]` cannot be translated into SQL within ORM projections (`AsDto`), it automatically degrades to in-memory mapping (two-step query); see §5.1 and [ORM Projection Design and Degradation Mechanism](../explanation/orm-projection-design.md). If the JSON text is `null` or blank, forward mapping assigns `null` directly (see §5.1 null guard) without entering deserialization.

---

### 5.8 `[MapWhen]` — Property-Level Conditional Guard (Equivalent to Fluent `.MapWhen`)

Applied to a property to declare a **conditional guard**: mapping and assignment for that property occur only when the predicate returns `true` (“true means map”—polarity reflected in the name). Guards can anchor against auto-matched same-name source properties or explicitly declared `[MapProperty]` bindings. The fluent equivalent terminal is `.MapWhen` (dual-form), whereas this attribute supports **name-only form only** (arguments must be compile-time constants; inline lambda form is exclusive to fluent and offers asymmetric expressiveness).

Runnable example (`dotnet run --scenario 105` for live validation; `[MapWhen(nameof(HasContent))]` is property-level, and the predicate method is declared on the DTO itself):

[!code-csharp[Attribute-side `\[MapWhen\]` property-level guard](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto)]

[!code-csharp[Attribute-side `\[MapWhen\]` property-level guard · Id](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Id)]

[!code-csharp[Attribute-side `\[MapWhen\]` property-level guard · Name](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Name)]

[!code-csharp[Attribute-side `\[MapWhen\]` property-level guard · Score](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_Score)]

[!code-csharp[Attribute-side `\[MapWhen\]` property-level guard · HasContent](~/samples/NCMapper.Samples/01-Fluent/MwModels.cs#MapWhen_MwAttrItemDto_HasContent)]

Key Behavioral Points:

- **Property-level annotation, `Inherited = true`** (inherited with property, unlike class-level hook attributes where `Inherited = false`); singleton—duplicate annotations on the same property are disallowed.
- **Predicate Resolution Scope**: Predicate methods must be declared on the **declaring type of the annotated property** (self-contained single-type resolution, consistent with the single-parameter form of `[Convert(string)]`).
- **Predicate Signature**: `public/internal static bool M(T)`; single parameter equals source entity type `T2` for whole-source evaluation (predicate reads entire source object), otherwise `T1` for source field value (predicate reads source field value).
- **Pipeline Order**: Condition → Conversion → Assignment (condition sees source field value *before* conversion); if condition evaluates `false`, assignment is skipped (new-path retains initializer value; `_Update` path retains existing value).
- **Reverse Mapping Ignored**: Guards apply only to forward mapping (Entity → DTO).
- **Projection Behavior**: Guards are CLR delegate paths, non-translatable to SQL; projection always degrades (classified as `MapWhen`).

| Dimension | `[MapWhen]` (Attribute) | `.MapWhen` (Fluent) |
| --- | --- | --- |
| Placement | Property-level | Chained terminal (anchored to target property) |
| Predicate Form | Name-only method (Form A) | Named method group + capture-free inline lambda (Forms A/B dual) |
| Resolution Scope | Declaring type of annotated property | Method group statically resolvable → valid (cross-class reuse allowed) |
| Compile-time Diagnostics | `NCMAP041` (predicate resolution failure: method missing/multiple overloads/ambiguous/non-static/insufficient visibility/wrong signature) | `NCMAP_F40`–`F45` |

> Full semantics—including co-anchored combinations, single-condition usage, PATCH idioms, and guards inside `FromSource` blocks—are covered in the [Fluent Four-Terminal MapWhen Dedicated Section](../how-to/fluent/terminals.md).

### 5.9 `[BeforeMapping]` / `[AfterMapping]` — Class-Level Lifecycle Hooks (Equivalent to Fluent `.BeforeMapping` / `.AfterMapping`)

Class-level attribute-based mapping pipeline hooks: `[BeforeMapping]` is invoked **after the target instance is constructed but before the first property assignment**, and `[AfterMapping]` is invoked **after all property assignments are completed**. `Inherited = false` (derived DTOs do not inherit these hooks; they must be explicitly annotated—consistent with `[MapTree]`). The Fluent equivalents `.BeforeMapping(handler)` / `.AfterMapping(handler)` populate the same snapshot-backed fields (dual-channel equivalence).

Runnable example (`dotnet run --scenario 104` for live validation; dual-parameter host form + `[MapFrom]` anchoring):

[!code-csharp[Attribute-side dual hooks (dual-parameter host form + \[MapFrom\] anchoring)](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto)]

[!code-csharp[Attribute-side dual hooks (dual-parameter host form + \[MapFrom\] anchoring) · Id](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Id)]

[!code-csharp[Attribute-side dual hooks (dual-parameter host form + \[MapFrom\] anchoring) · Title](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Title)]

[!code-csharp[Attribute-side dual hooks (dual-parameter host form + \[MapFrom\] anchoring) · Qty](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Qty)]

[!code-csharp[Attribute-side dual hooks (dual-parameter host form + \[MapFrom\] anchoring) · NormalizedTitle](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_NormalizedTitle)]

[!code-csharp[Attribute-side dual hooks (dual-parameter host form + \[MapFrom\] anchoring) · Total](~/samples/NCMapper.Samples/01-Fluent/HookModels.cs#FluentHooks_AttrHookDto_Total)]

Key behavioral notes:

- **Dual-parameter constructor (recommended)**: `[BeforeMapping(typeof(HookHost), nameof(HookHost.AttrNormalize))]` — statically locates a callback method on the specified type; enables centralized, cross-entity reuse of the host class (style consistent with `[Convert(typeof(X), nameof(X.M))]`)
- **Single-parameter constructor**: `[BeforeMapping(nameof(Normalize))]` — declares the hook inline; resolution scope is narrowed to the DTO type itself where the attribute is applied; `nameof(OtherType.M)` is truncated to just the method name, so use the dual-parameter form when the method resides outside the DTO (otherwise `NCMAP038`)
- **Method signature**: `void(TSource, TTarget)` — the second parameter is the DTO type for this mapping pair (the signature defines applicability); only pairs matching the signature trigger the hook; non-matching pairs neither trigger nor emit diagnostics
- **Derived-field pattern**: Hooks write derived fields with names matching source-less properties; mapping assignments affect only source-backed properties and never overwrite hook-produced values
- **PATCH semantics**: In `_Update` scenarios (reusing an existing destination object), `BeforeMapping` observes the pre-existing `dst` values
- **Projection behavior**: Any mapping pair declaring either hook degrades to client-side evaluation under ORM projection (nested projections skip generated code entirely; missing hooks result in semantic errors)

| Dimension | `[BeforeMapping]`/`[AfterMapping]` (Attributes) | `.BeforeMapping`/`.AfterMapping` (Fluent) |
| --- | --- | --- |
| Annotation location | Class-level (`Inherited = false`) | Chained terminal (method group) |
| Construction form | Dual-parameter host / single-parameter local | Direct method-group pass-through |
| Callback requirement | Accessible static method (dual-parameter strong binding / single-parameter local resolution) | Statically resolvable method group (`public` / `internal static`) |
| Compile-time diagnostics | `NCMAP038`–`NCMAP040` (unresolvable / signature mismatch / overload ambiguity) + F35–F39 on both sides | `NCMAP_F35`–`F39` |

> Full semantics—including dual-channel conflict (F39), no insertion point for constructor-integrated targets (F37), and prohibition of declaration inside `FromSource` blocks (F38)—are detailed in the [Fluent Hooks Lifecycle Hooks section](../how-to/fluent/hooks.md).

### 5.10 `[MapSubclass]` — Class-Level Polymorphic Subtype Mapping (Equivalent to Fluent `.MapSubclass<TSrcDerived, TTargetDerived>()`)

Annotated on a base-class DTO to register a subtype mapping pair for the base-class mapping. At runtime, the base-class mapping dispatches to the appropriate subclass implementation based on the **actual runtime type** of the source instance (via `isinst`-based dispatch chain). Type-level multi-instance support (`AllowMultiple = true`; each instance registers one pair); the annotated class must also carry `[MapFrom(typeof(BaseSource))]` to anchor the base-class mapping pair (missing anchor triggers `NCMAP_F49`). The Fluent equivalent is `.MapSubclass<TSrcDerived, TTargetDerived>()` (pair-level registration; `cfg.For` serves as the anchor—dual-channel equivalence).

Runnable example (`dotnet run --scenario 107` for live validation; `[MapFrom]` + two `[MapSubclass]` instances registering corresponding subclass DTOs):

[!code-csharp[Attribute-side \[MapSubclass\] multi-instance registration and subclass DTOs](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto)]

[!code-csharp[Attribute-side \[MapSubclass\] multi-instance registration and subclass DTOs · ScAttrAnimalDto_Name](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Name)]

[!code-csharp[Attribute-side \[MapSubclass\] multi-instance registration and subclass DTOs · ScAttrAnimalDto_Weight](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrAnimalDto_Weight)]

[!code-csharp[Attribute-side \[MapSubclass\] multi-instance registration and subclass DTOs · ScAttrDogDto](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrDogDto)]

[!code-csharp[Attribute-side \[MapSubclass\] multi-instance registration and subclass DTOs · ScAttrDogDto_Breed](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrDogDto_Breed)]

[!code-csharp[Attribute-side \[MapSubclass\] multi-instance registration and subclass DTOs · ScAttrCatDto](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrCatDto)]

[!code-csharp[Attribute-side \[MapSubclass\] multi-instance registration and subclass DTOs · ScAttrCatDto_Indoor](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_ScAttrCatDto_Indoor)]

[!code-csharp[Attribute-side \[MapSubclass\] multi-instance registration and subclass DTOs · SubclassFluentMapper](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_SubclassFluentMapper)]

[!code-csharp[Attribute-side \[MapSubclass\] multi-instance registration and subclass DTOs · SubclassFluentMapper_Configure](~/samples/NCMapper.Samples/01-Fluent/ScModels.cs#MapSubclass_ScAttrAnimalDto_SubclassFluentMapper_Configure)]

Key behavioral notes:

- **`Inherited = false`** (derived DTOs do not inherit superclass’s subclass registrations); `AllowMultiple = true` (supports multiple instances registering multiple pairs)
- **No generic constraints**: Inheritance relationships (S derived from base source, T derived from base target) are validated at extraction time—not compile time—if violated, `NCMAP_F46` is reported (Fluent side relies on generic constraints `CS0311`/`CS0314`, enforced at call site; users should not expect `NCMAP_F46` in Fluent usage)
- **Dispatch semantics**: Dispatch occurs on assignable-match hit; most-derived match wins; unmatched cases fall back to the base-class body; `_Update`/`MapListInto` require *both* source and destination to match the subclass pair to route to the subclass body
- **Reverse mapping**: Subclass mappings automatically generate reverse mappings and reverse direct-call overloads via the reverse-registration pipeline (registrations are bidirectional by default); `NCMAP_F50` (Info) warns about “reverse-asymmetry” and other silent behaviors
- **Projection behavior**: Base-class mappings registered with `MapSubclass` always degrade to client-side evaluation under ORM projection (`isinst` dispatch chains cannot be translated to SQL; classified as `SubclassDispatch`)

| Dimension | `[MapSubclass]` (Attribute) | `.MapSubclass<TSrcDerived, TTargetDerived>()` (Fluent) |
| --- | --- | --- |
| Annotation location | Class-level, multi-instance (type-level) | Chained terminal (pair-level) |
| Anchoring requirement | Requires co-annotation with `[MapFrom(typeof(BaseSource))]` (missing anchor → `NCMAP_F49`) | `cfg.For` anchors the base-class pair |
| Inheritance constraint | No compile-time constraint; validation at extraction time (`NCMAP_F46`) | Generic constraints (`CS0311`/`CS0314`) enforced at call site |
| Other diagnostics | `NCMAP_F47`/`F48`/`F50` (general-purpose) | Same as left column |

> Full semantics—including most-derived-first dispatch, base-class fallback, dual-instance matching, and reverse asymmetry—are covered in the [Fluent Four-Terminal MapSubclass section](../how-to/fluent/terminals.md).

### 5.11 [MapFormat] — Property-Level Formatting (Equivalent to Fluent `.Format`)

The source property must implement `IFormattable` (e.g., `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, numeric types, enums—including their `Nullable<T>` wrappers), and the target must be a `string`. During mapping, the formatted string is produced using:  
`IFormattable.ToString(format, null)`. The format provider is always passed as `null` (= current culture semantics). This intentional design diverges from the invariant-culture conversion path used for unannotated properties; if culture-agnostic consistent output is required, use `[Convert]` instead to explicitly control the culture.

A runnable example (`dotnet run --scenario 202` to verify in practice):

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask)](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrder_CreatedAt](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_CreatedAt)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrder_Amount](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_Amount)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrder_Status](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_Status)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrder_Duration](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_Duration)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrder_ShippedDate](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_ShippedDate)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrder_CardNo](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrder_CardNo)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrderDto](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrderDto_CreatedAt](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_CreatedAt)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrderDto_Amount](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_Amount)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrderDto_Status](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_Status)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrderDto_Duration](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_Duration)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrderDto_ShippedDate](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_ShippedDate)]

[!code-csharp[Attribute-side `\[MapFormat\]` annotation paired with `\[ConvertBack\]` for round-trip (full coverage: dates, numbers, enums, time spans, nullable types, FormatThenMask) · MfOrderDto_CardNo](~/samples/NCMapper.Samples/06-MapFormat/MfModels.cs#MapFormat_AttrChannel_MfOrderDto_CardNo)]

Key behavioral notes:

- **One-way display declaration**: Consumed only in the forward direction (Entity → DTO); during reverse mapping, this property is **not assigned** (the entity-side value remains unchanged, while other fields are written back normally). A bare declaration is valid but triggers warning `NCMAP046`; round-trip usage requires pairing with `[ConvertBack]` (the two attributes are orthogonal and may coexist).
- **Mutually exclusive with `[Convert]`**: Coexistence on the same member raises error `NCMAP045` (choose one).
- **Global ignore override gate**: `OverrideIgnore = true` (one of five override gates; see [Global Mapping Configuration](../how-to/infrastructure/global-config.md)).
- **Projection behavior**: Implements `IProjectionPolicy` (`RequiresDowngrade = true`). ORM `AsDto()` automatically downgrades to a two-step mapping (formatting occurs on the CLR path and cannot be translated into SQL).
- **Three narrow formatting domains**:  
  - `Guid`: Only supports format specifiers `N`, `D`, `B`, `P`, or `X`.  
  - `TimeSpan`: Custom format strings containing colons or periods must escape them (e.g., `hh\:mm`).  
  - Enums: Format strings must use enum-specific specifiers such as `G`, `D`, `X`, or `F`. Format string semantics are evaluated at runtime; compile-time validation only checks for non-emptiness.
- **No anchor, no consumption**: Same scope as `[Mask]` — annotations on classes without explicit mapping anchors are ignored, intercepted by `NCMAP019`.

| Dimension | `[MapFormat]` (attribute) | `.Format(format)` (Fluent) |
| --- | --- | --- |
| Annotation location | Target `string` property (`Inherited = false`) | Chain terminal (within the `Member` segment, field-level) |
| Format string requirement | Any `string` argument (validated for non-emptiness at construction; empty/whitespace throws `ArgumentException`) | Compile-time constant (non-constant expressions are silently skipped) |
| Source matching | Name-based automatic matching (cross-field not supported) | Same as left (for cross-field mapping, combine with `.Member(d => d.X).From(s => s.Y)`) |
| Guard diagnostics | `NCMAP043`–`NCMAP046` | `NCMAP_F64`–`F67` (same diagnostic codes, aligned across both engines) |

> For full semantics—including selection rationale, reverse-mapping non-assignment behavior, and round-trip syntax—see the dedicated [MapFormat Channel Section](../how-to/declarative/map-format.md) and [Reverse Mapping Semantics §5](../explanation/reverse-mapping-semantics.md).
