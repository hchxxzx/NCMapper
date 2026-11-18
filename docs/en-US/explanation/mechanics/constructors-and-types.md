> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §6.4–6.5 record Types and Enum Mapping -->

### 6.4 record Types

NCMapper fully supports mapping of C# `record` types. Records define properties via primary constructors and lack parameterless constructors. NCMapper automatically matches constructor parameter names to source property names and generates mapping code in the form `new T(arg1, arg2, ...)`.

#### Constructor Parameter Matching Rules

| Rule | Description |
|------|-------------|
| Matching Strategy | Constructor parameter name ↔ destination property name, case-insensitive (`OrdinalIgnoreCase`) |
| Constructor Selection | The public constructor with the most parameters (`OrderByDescending(Parameters.Length)`) |
| Unmatched Parameters | Use explicit default values if provided; otherwise use `null` for reference types and `default(T)` for value types |
| Type Conversion | Direct assignment for identical types; implicit conversions use cast; all others use `TypeConvertHelper` |

```csharp
public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Primary constructor parameter names are matched automatically to source properties (case-insensitive)
[MapFrom(typeof(UserEntity))]
public record UserDto(int Id, string Name, DateTime CreatedAt);

// Mapping — source generator produces: new UserDto(src.Id, src.Name, src.CreatedAt)
var dto = Mapper.Map<UserEntity, UserDto>(entity);
```

#### Additional Properties (Outside the Primary Constructor)

A `record` may declare both primary constructor parameters and additional writable properties. Constructor parameters are assigned via `new T(...)`, while additional properties are assigned *after* construction using `dst.Prop = value`:

```csharp
[MapFrom(typeof(UserEntity))]
public record UserDto(int Id, string Name)
{
    public string? Email { get; set; }       // Additional property, assigned post-construction
    public DateTime MappedAt { get; set; }   // Additional property, assigned post-construction
}

// Source generator produces (pseudocode):
// var dst = new UserDto(src.Id, src.Name);
// dst.Email = src.Email;
// dst.MappedAt = src.MappedAt;
```

> **`init-only` properties** (`{ get; init; }`): The source generator supports them in the object-initializer path (`new T { Prop = value }`). In the constructor path, `init-only` properties that are *not* constructor parameters are skipped (they cannot be assigned after construction).

#### Inheritance

When a `record` inherits (`record B(int X) : A(Y)`), base-class properties are matched normally up the inheritance chain. `FindProperty` searches upward along `BaseType`, and constructor parameter matching takes precedence over inherited properties:

```csharp
public record BaseEntity(int Id, DateTime CreatedAt);

[MapFrom(typeof(UserEntity))]
public record UserDto(int Id, DateTime CreatedAt, string Name) : BaseEntity(Id, CreatedAt);
// Base-class parameters Id and CreatedAt, plus own parameter Name, are all matched from source properties
```

#### Source Generator vs Runtime Engine

| Dimension | Source Generator | Runtime Engine |
|-----------|------------------|----------------|
| Recognition | No public parameterless constructor → constructor-based path | `GetConstructor(Type.EmptyTypes) == null` → constructor-based path |
| Constructor Selection | Public constructor with the most parameters | Prioritizes full matches; otherwise selects the one with the most matched parameters |
| Code Generation | Literal code: `new T(arg1, arg2, ...)` | Expression tree: `Expression.New(ctor, args[])` |
| Additional Properties | Assigned individually post-construction | Assigned in a `Block` expression post-construction |
| First-Use Latency | Zero | First call to `Expression.Compile()` |

#### DeepCopy Differences

Deep copying of `record`s passes deeply copied values as constructor arguments:

```csharp
// Source generator produces (pseudocode):
// var dst = new UserDto(src.Id, Mapper.DeepCopy(src.Address), src.Name);
```

> ℹ️ **Record Cycle Classification Fallback**: Since the destination `record` is constructed *inside* the constructor, it cannot be pre-registered into the cycle-detection table before construction. Therefore, the constructor-based path inherently does not support cyclic reference graphs. The source generator **automatically classifies** the property graph at compile time (three-valued classification: `Acyclic`/`Cyclic`/`Unknown`). If classified as **`Cyclic` or `Unknown`** (e.g., self-reference `A.Self = A`, bidirectional reference `A↔B`, or edges involving `object`/interfaces—statically unresolvable), it **automatically emits fallback code** calling `Mapper.DeepCopyRuntimeOnly<T>` (which uses the full runtime engine with cycle detection) and issues warning **NCMAP012**. Acyclic `record`s still use the fast source-generator path. Users simply call `Mapper.DeepCopy(recordObj)` as usual—the correct behavior is guaranteed **without manual intervention**. See [Limitations and Notes §18.6](../limitations.md).

#### Complete Example

```csharp
// Source entity
public class CtOrderEntity
{
    public int Id { get; set; }
    public string OrderNo { get; set; }
    public decimal Amount { get; set; }
    public CtCustomerEntity? Customer { get; set; }
}

// Record DTO — primary constructor + additional properties + nested record
[MapFrom(typeof(CtOrderEntity))]
public record CtOrderDto(int Id, string OrderNo, decimal Amount)
{
    public CtCustomerDto? Customer { get; set; }   // Nested object, assigned post-construction
    public string? Remark { get; set; }          // Additional property
}

[MapFrom(typeof(CtCustomerEntity))]
public record CtCustomerDto(string Name, string Email);  // Nested record
```

Three mapping forms (forward / list / reverse `record→class`) — generated shape:
`var dst = new CtOrderDto(src.Id, src.OrderNo, src.Amount);` + nested sub-mapping + additional property assignments:

[!code-csharp[record Constructor Binding Tour: Map / MapList / Reverse Map](~/samples/NCMapper.Samples/01-Fluent/CpModels.cs#CtorParam_RecordCtorTour)]

> **Known Limitation (Intentional Counterexample)**: The reverse mapping above, `back = Mapper.Map<CtOrderDto, CtOrderEntity>`, is shown as a **zero-call** demonstration (the tour section omits `Run()`). Since `CtCustomerDto` lacks a declared reverse mapping, an actual invocation would throw `MapConfigurationException`; a validation report already surfaces this registration gap as an Error-level entry `CtOrderEntity→CtOrderDto Customer`. Important note: **Omitting `Run()` only guarantees non-execution—not zero change in registration state or printed counts (the declarative registry is global)**. This example is the first concrete instance of the “Never executed, therefore never tested” observation window.

### 6.5 Enum Mapping

NCMapper supports automatic conversion between enum types and strings, numeric types, and other enums. The source generator emits `switch` expressions at compile time (zero reflection), while the runtime engine uses `Expression.Switch` or `TypeConvertHelper`.

#### Supported Conversion Directions

| Direction | Source Generator Implementation | Runtime Implementation |
|-----------|-----------------------------------|------------------------|
| `enum → string` | `switch` expression: each member → string literal | `Expression.Switch` with integer dispatch |
| `string → enum` | `switch` expression (case-insensitive: `ToLowerInvariant` then match); throws `MapException` on invalid input | `Enum.Parse(ignoreCase: true)` |
| `enum → enum` (different types) | Value-based cast: `(TargetEnum)src` | `Expression.Convert` |
| `enum → numeric` | Fallback to `TypeConvertHelper.ConvertValue` | `Convert.ChangeType` |
| `numeric → enum` | Fallback to `TypeConvertHelper.ConvertValue` | `Enum.ToObject` |

#### Source Generator Code Generation

```csharp
public enum OrderStatus { Pending, Processing, Shipped, Delivered }

public class OrderEntity
{
    public OrderStatus Status { get; set; }       // Enum
    public OrderStatus? Priority { get; set; }    // Nullable enum
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public string Status { get; set; }            // enum → string
    public string? Priority { get; set; }         // enum? → string
}
```

The source generator emits a `switch` expression for `enum → string`:

```csharp
// Generated code
dst.Status = src.Status switch
{
    OrderStatus.Pending => "Pending",
    OrderStatus.Processing => "Processing",
    OrderStatus.Shipped => "Shipped",
    OrderStatus.Delivered => "Delivered",
    _ => src.Status.ToString(),   // Fallback (used for `[Flags]` composite values)
};

// Nullable enum → string (null-safe)
dst.Priority = src.Priority switch
{
    OrderStatus.Pending => "Pending",
    OrderStatus.Processing => "Processing",
    OrderStatus.Shipped => "Shipped",
    OrderStatus.Delivered => "Delivered",
    _ => src.Priority?.ToString() ?? "",
};
```

For `string → enum`, it emits a reverse `switch`:

```csharp
// Generated code
dst.Status = src.Status switch
{
    "Pending" => OrderStatus.Pending,
    "Processing" => OrderStatus.Processing,
    "Shipped" => OrderStatus.Shipped,
    "Delivered" => OrderStatus.Delivered,
    _ => throw new NCMapper.MapException($"Cannot map '{src.Status}' to {nameof(OrderStatus)}: no matching enum member"),
};
```

For `enum → enum` (different enum types), it emits a value-based cast:

```csharp
// Generated code
dst.Status = (TargetEnum)src.Status;
```

#### NCMAP010 Diagnostic — No Enum Member Overlap

When the source and target enums have **no member names in common**, the source generator emits warning NCMAP010:

```csharp
public enum Color { Red, Green, Blue }
public enum Size { Small, Medium, Large }   // No name overlap with Color

[MapFrom(typeof(Entity))]
public class Dto
{
    // ⚠️ NCMAP010: Source enum 'Color' and target enum 'Size' share no member names.
    //    Value-based casting may yield unexpected results.
    public Size Color { get; set; }
}
```

> This diagnostic does *not* trigger when names overlap (e.g., `Color.Red` and `TargetColor.Red`). It applies *only* to `enum→enum` value-based casting—not to `enum→string` or `string→enum`.

#### `[Flags]` Enums

`[Flags]` enums receive no special handling—they use the same `switch` expressions as regular enums. Composite values (e.g., `Read | Write = 3`) do not match any single-member case and fall through to the default branch:

| Path | Composite Value Handling | Result |
|------|--------------------------|--------|
| Source generator `enum→string` | Default branch: `src.ToString()` | `"Read, Write"` (correct) |
| Runtime `enum→string` | `source.ToString()` | `"Read, Write"` (correct) |
| Source generator `string→enum` | No match → **throws `MapException`** | First matching member (potentially unintended) |

> For `[Flags]` composite `string→enum` scenarios, prefer using an `Action` callback or the `[Convert]` attribute for explicit handling.

#### Nullable Enums

`Enum?` types are handled automatically: the source generator strips the `Nullable<T>` wrapper using `GetUnderlyingType`, then generates null-safe `switch` expressions (see `Priority` example above).

Nullable-to-non-nullable conversions follow the general nullable handling path: `src.HasValue ? src.Value : default`.

#### Source Generator vs Runtime: Key Differences

| Scenario                     | Source Generator                                | Runtime                                        |
| ---------------------------- | ----------------------------------------------- | ---------------------------------------------- |
| `string` → `enum` case sensitivity | **Case-insensitive** (`ToLowerInvariant` followed by exact switch matching; invalid values throw `MapException`) | **Case-insensitive** (`Enum.Parse(ignoreCase: true)`) |
| No-match `string` → `enum`   | **Throws `MapException`** (silent fallback to `default` is not allowed) | Empty string returns `default`; invalid string throws `MapException` |
| `enum` → numeric value       | Fallback to `TypeConvertHelper` (runtime conversion) | `Convert.ChangeType` (equivalent behavior)     |
| `[Flags]` composite value → `string` | Default branch uses `ToString()` (correct result) | `ToString()` (correct result)                  |

#### Complete Example

```csharp
public enum OrderStatus { Pending, Processing, Shipped, Delivered }
public enum OrderStatusDto { Pending, Processing, Shipped, Delivered, Cancelled }

public class OrderEntity
{
    public OrderStatus Status { get; set; }
    public int StatusCode { get; set; }           // numeric
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public string Status { get; set; }            // enum → string (switch expression)
    public OrderStatusDto StatusCode { get; set; } // int → enum (TypeConvertHelper)
}

// Reverse mapping: string → enum
[MapFrom(typeof(OrderDto))]
public class OrderEntity2
{
    public OrderStatus Status { get; set; }       // string → enum (switch expression)
}
```
