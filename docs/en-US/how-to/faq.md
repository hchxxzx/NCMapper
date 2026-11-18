> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation, Appendix B — Frequently Asked Questions (FAQ) -->

## Appendix B: Frequently Asked Questions (FAQ)

### Development FAQs

**Q: I annotated a type with `[MapFrom]`, but the source generator did not produce any code. Why?**

Check the following:
- Does your project reference the `NCMapper` NuGet package? (The source generator is embedded in this package; no separate installation is required.)
- The target type must be a `class` (structs and record structs are not supported).
- `[MapFrom]` must be applied to the **target type** (e.g., DTO), *not* the source type.
- Ensure `<NCMapperSourceGenEnabled>` is not explicitly set to `false` in your project file.
- In your IDE, try **Clean + Rebuild** to force the source generator to re-execute.

**Q: After mapping, a property is `null`. How do I troubleshoot?**

Enable trace diagnostics (`Mapper.TraceEnabled = true`) and inspect the logged mapping strategy for that property. Common causes include:
- Mismatched property names (NCMapper uses `OrdinalIgnoreCase` matching — case-insensitive but spelling must match exactly).
- Incompatible source and target property types with no implicit conversion available.
- The property is annotated with `[MapIgnore]`.
- The source property for a nested object is itself `null` (NCMapper does *not* instantiate null nested objects).

You may also use `Mapper.Validate()` at application startup to batch-validate all registered mappings.

**Q: How do I resolve warning `NCMAP006` ("Target property is unmapped")?**

This warning means: a property of the same name exists on the source type, but the target property’s setter is not `public` (`private`, `protected`, `internal`, `init`, or missing entirely), so generated code cannot assign to it.

Two resolution options:
- If the property *should* be mapped, change its setter to `public set`.
- If the property *should not* be mapped, explicitly annotate it with `[MapIgnore]`.

Distinguish from related diagnostics:
- `NCMAP007`: "Source property is unmapped" (a source property has no corresponding target property).
- `NCMAP003`: "`[MapFrom]` is applied but no properties can be mapped" (no valid property pairs found).

**Q: When should I use the source generator path vs. the runtime path?**

- **Source generator path** is recommended for production: mapping logic is fully generated at compile time after applying `[MapFrom]`. It incurs zero first-call latency, zero reflection, and is AOT-friendly.
- **Runtime path** (expression trees) suits scenarios where you cannot modify the source code of target types (e.g., third-party library types), require dynamic type mapping, or are in early prototyping/validation phases.

Both paths coexist seamlessly: types annotated with `[MapFrom]` use the source generator; unannotated types fall back to the runtime engine.

**Q: Which deep copy strategy should I choose?**

- Default `FastDeepCopyProvider`: optimal for most cases. Uses a trivalent dispatch:
  - Acyclic graphs → fast-path with zero cycle detection (~401 ns).
  - Cyclic/Unknown graphs → delegates to base-class full-cycle detection (supports arbitrary cycle lengths).
- Use `ExpressionTreeDeepCopyProvider` (~409 ns) if your object graph must **preserve DAG shared references** (i.e., multiple references to the same source instance must map to the same destination instance).
- If the type is annotated with `[MapFrom]`, the source generator path activates automatically (~179 ns — fastest option).

All timings above were measured on `net10.0` under Linux. Actual values vary significantly across hardware and OS.

⚠️ Note: For `record` types, if the property graph contains cyclic references or statically unresolvable edges (e.g., `object`, `dynamic`, interface-typed, or `virtual` properties), the source generator detects this at compile time and automatically falls back to the runtime provider (emitting diagnostic `NCMAP012`; see [Limitations & Considerations](../explanation/limitations.md)). Manual switching is unnecessary.

**Q: What is the difference between `Mapper.Map<TSrc, TTarget>()` and extension methods like `MapToXxx()`?**

- `Mapper.Map<TSrc, TTarget>(src)` performs a generic static cache lookup — minimal but non-zero indirection overhead.
- Extension methods like `src.MapToXxxDto()` are generated *at compile time* by the source generator. They call the core mapping method directly, with zero delegate overhead and JIT inline optimization (`[MethodImpl(AggressiveInlining)]`). This is the highest-performance path.

Functionally, both are equivalent. For hot paths, prefer extension methods.

**Q: How do I handle `MapException` caused by circular references?**

The NCMapper runtime engine uses a `ThreadStatic` recursion depth counter (default limit: 64 levels). Exceeding this throws `MapException` instead of causing a stack overflow.

Solutions:
- If your object graph is genuinely deep (not cyclic), increase `Mapper.MaxRecursionDepth`.
- If it's a true circular reference, break the cycle on the DTO side (e.g., omit reverse navigation properties), or annotate the problematic property with `[MapIgnore]`.

The source generator path emits fixed-depth code at compile time and has *no* recursion depth limitation.

**Q: What is the difference between `MapList<TSrc, TTarget>()` and manual `Select(x => Mapper.Map<TSrc, TTarget>(x)).ToList()`?**

- `Mapper.MapList<TSrc, TTarget>(list)` internally uses an inlined `for` loop, calling the core mapper per element — avoiding per-element generic cache lookups. It also returns `null` for `null` input collections (rather than an empty list).
- `list.Select(x => Mapper.Map<TSrc, TTarget>(x)).ToList()` performs a generic cache lookup for *every* element.

For performance-sensitive scenarios, prefer `MapList`. For LINQ chaining (e.g., `Where`, `OrderBy`), use the generated extension method `src.MapToXxxDtoList()`.

### Comparison & Selection Guidance

**Q: What are the core differences between NCMapper and AutoMapper?**

- **AutoMapper**: Pure runtime solution based on reflection + expression trees. Configuration lives in `Profile` classes. First mapping incurs compilation delay.
- **NCMapper**: Dual-engine architecture:
  - **Source generator**: Generates all mapping code at compile time — zero reflection, zero first-call latency, AOT-compatible.
  - **Runtime engine**: Expression-tree-based fallback for unannotated types.
- Mapping configuration uses attribute-based declaration (`[MapFrom]`) instead of centralized `Profile`s — mapping logic lives alongside type definitions, enabling IDE navigation and safe refactoring.
- NCMapper includes built-in features that require extra setup or third-party libraries in AutoMapper: deep copying, ORM projections (FreeSQL / EF Core / Dapper), multi-source composition, and mapping validation.

**Q: What are the core differences between NCMapper and Mapperly?**

Both leverage Roslyn source generators for compile-time mapping code generation and share similar design philosophies.

Key distinctions:
- NCMapper provides a **full runtime engine** (expression trees) as a fallback — unannotated types still map automatically at runtime, eliminating the need to annotate every type pair.
- NCMapper ships with built-in, pluggable features: deep copy (three swappable strategies), multi-source composition, ORM projection (FreeSQL / EF Core / Dapper), mapping validation, and trace diagnostics.
- NCMapper’s ecosystem includes a dedicated, standalone **AutoMapper migration tool** (`NCMapper.Migrate`).
- Mapperly is lighter-weight for pure compile-time-only scenarios where runtime flexibility is unnecessary.

**Q: When should I choose NCMapper?**

NCMapper is ideal when:
- You require **maximum mapping performance**, approaching hand-written code (source generator path).
- Your project needs both **compile-time safety** *and* **runtime flexibility** (dual-engine design).
- You need advanced capabilities like deep copying, ORM projections, or multi-source composition.
- You’re migrating from AutoMapper (NCMapper provides `NCMapper.Migrate`).
- You deploy using **AOT / NativeAOT** (source generator path is fully compatible).

If you only need simple compile-time mapping and *never* require runtime fallback, Mapperly remains a reasonable alternative.

**Q: Where can I find performance benchmarks?**

Performance benchmark data (comparing three mapping paths + three deep-copy strategies, including cross-framework comparisons) is published in a standalone report.

Key results (as of 2026-10-07, full batch run on Linux: Intel Xeon Silver 4314 / Ubuntu 24.04.5, `net10.0`):
- Extension-method path: ~7–33 ns (varies by scenario complexity), nearly identical to handwritten assignment.
- Deep-copy source-generator path: ~179 ns — the fastest automatic deep-copy solution available.

Full dataset (13 mapping scenarios × 3 frameworks + 3 deep-copy strategies) is available in the repository’s `benchmarks/` directory and `docs/` performance reports. Performance numbers vary significantly across CPU/OS configurations — always refer to the environment specified in the report.

### Competitor Feature Equivalents

Below are common capabilities from AutoMapper / Mapperly / Mapster and their NCMapper equivalents. Most have dedicated APIs or global configurations (explicitly named in each item); others achieve equivalent behavior via existing mechanisms.

**Q: How do I implement conditional mapping (AutoMapper’s `.Condition()`, Mapster’s `.When()`)?**

NCMapper provides the dedicated `MapWhen` guard API: the property is mapped *only* if the predicate evaluates to `true`; otherwise, assignment is skipped (new instances retain default initializers; updates preserve existing values):

```csharp
[MapFrom(typeof(User))]
public class UserDto
{
    [MapWhen(nameof(IsAgeValid))]   // Maps only if predicate returns true
    public int Age { get; set; }

    // Field-value predicate: single parameter type == source field type;
    // declared on the same type as the annotated property
    public static bool IsAgeValid(int fieldValue) => fieldValue > 0;
}
```

Fluent API equivalent (terminal form): anchor on the field via `.Member(d => d.Age)`, then apply `MapWhen`:
- For **field-value predicates**, use `.MapWhen(predicate)` (parameter type matches the *source field value*).
- For **entire-source predicates**, use `.MapWhen<User>(condition)` (explicit type argument required; interpreted as “entire source” if the predicate’s parameter type matches the source entity type).

Predicates support both named method groups and capture-free inline lambdas. Full semantics (syntax forms and diagnostics) are documented in [Fluent Terminals](fluent/terminals.md) and [Feature System](../reference/features.md) §5.8. Try it live: `dotnet run --scenario 105`.

For more complex logic upon condition match, use `[Convert]` inside the conversion method, or an action callback: `Mapper.Map<User, UserDto>(user, (s, t) => { if (s.Age <= 0) t.Age = 0; });`

**Q: How do I implement null substitution (AutoMapper’s `.NullSubstitute()`)?**

Use `[Convert]` with a static conversion method that handles null fallback:

```csharp
public static class NicknameConverter
{
    public static string Convert(string? fieldValue)
        => fieldValue ?? "Anonymous User";
}

[MapFrom(typeof(User))]
public class UserDto
{
    [Convert(typeof(NicknameConverter), nameof(NicknameConverter.Convert))]
    [MapProperty(typeof(User), nameof(User.Nickname))]
    public string Nickname { get; set; }
}
```

The `fieldValue` parameter passed to the `[Convert]` method is the raw source field value (including `null`), so `??` works directly.

**Q: How do I implement custom object mapping methods (Mapperly’s `partial` user methods)?**

Mapperly allows writing a user-defined mapping method for a type pair, which the generator calls automatically wherever that pair appears in nested mappings.

NCMapper offers two equivalent approaches:

**Approach 1: `[Convert]` — full object construction inside the converter**

```csharp
public static class CustomerConverter
{
    public static CustomerDto? Convert(Customer? fieldValue)
    {
        if (fieldValue is null) return null;
        return new CustomerDto
        {
            FullName = $"{fieldValue.FirstName} {fieldValue.LastName}",
            Level = fieldValue.Points > 1000 ? "VIP" : "Normal"
        };
    }
}

[MapFrom(typeof(Order))]
public class OrderDto
{
    [Convert(typeof(CustomerConverter), nameof(CustomerConverter.Convert))]
    [MapProperty(typeof(Order), nameof(Order.Customer))]
    public CustomerDto? Customer { get; set; }
}
```

**Approach 2: `[MapIgnore]` + PostMap pipeline — skip auto-mapping and handle manually**

```csharp
[MapFrom(typeof(Order))]
public class OrderDto
{
    public int Id { get; set; }

    [MapIgnore]
    public CustomerDto? Customer { get; set; }
}

// Register pipeline at startup
Mapper.Pipeline.AddPostMap<Order, OrderDto>((src, dst) =>
{
    dst.Customer = MyCustomCustomerMap(src.Customer);
});
```

**Key difference**:  
Mapperly’s `partial` method applies globally — once defined, it’s used automatically for *every* occurrence of `Customer → CustomerDto`.  
In NCMapper, `[Convert]` must be applied *per property* where the mapping is needed. However, the same converter class/method can be reused across multiple DTOs.

**Q: How do I implement inheritance/multi-type mapping (AutoMapper’s `.Include<Base, Derived>()`)?**

NCMapper provides the dedicated `MapSubclass` API for polymorphic subtype dispatch: at runtime, base-class mapping routes to the appropriate derived-type mapping body based on the **actual runtime type** of the source instance (using `isinst`-based dispatch chain — most-derived-first, falling back to base if unmatched):

```csharp
[MapFrom(typeof(Animal))]
[MapSubclass(typeof(Dog), typeof(DogDto))]    // Register one per subtype pair (AllowMultiple)
[MapSubclass(typeof(Cat), typeof(CatDto))]
public class AnimalDto { public string Name { get; set; } = ""; }

public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
public class CatDto : AnimalDto { public bool Indoor { get; set; } }
```

Fluent API equivalent terminal: `.MapSubclass<TSrcDerived, TTargetDerived>()` (type-pair registration; `cfg.For<...>` anchors the base mapping).  
Attribute-side requires `[MapFrom]` on the base mapping pair (missing anchor triggers `NCMAP_F49`). Dispatch covers all four families: `Map`, `_Update`, `MapList`, and `MapListInto`, including reverse-direction mappings.

Full semantics (diagnostics F46–F50) are in [Feature System](../reference/features.md) §5.10 and [Declarative Advanced](declarative/advanced.md). Try it live: `dotnet run --scenario 107`.

Only when the set of subtypes is *not enumerable at compile time* (e.g., plugin-based dynamic loading) should you manually dispatch in business logic (e.g., `switch` over concrete types).

**Q: How do I configure naming conventions/prefix-suffix stripping (AutoMapper’s `RecognizePrefixes()`)?**

NCMapper provides assembly-level global configuration via `[assembly: MapDefaults]`:

```csharp
[assembly: MapDefaults(
    Prefixes  = new[] { "m_" },                    // Strip prefix from source prop names: m_CustomerName → CustomerName
    Postfixes = new[] { "Entity" },                // Strip postfix from source prop names: CustomerNameEntity → CustomerName
    NamingStrategy = NamingStrategy.SnakeCase)]    // Normalize naming style: user_name ↔ UserName
```

- `Prefixes` / `Postfixes`: lists of prefixes/postfixes to strip from **source property names only** (≤8 items each; exceeding triggers `NCMAP020` and degrades the list to "unconfigured").
- Stripped names are normalized per `NamingStrategy`, then used for exact-name matching and automatic flattening.

- `NamingStrategy`: Options include `Default` (case-insensitive, default), `CaseSensitive`, `SnakeCase` (`user_name` ↔ `UserName`), and `UpperSnakeCase` (`USER_NAME` ↔ `UserName`).

For exceptional cases, use `[MapProperty]` per-property for precise control.

The complete flow — candidate name generation → normalization → lookup matching → ignore evaluation — is detailed in [Global Mapping Configuration](infrastructure/global-config.md).
