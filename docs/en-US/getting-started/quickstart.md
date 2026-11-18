> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §2 Quick Start -->

## 2. Quick Start

### 2.1 Installation

```xml
<PackageReference Include="NCMapper" />
```

### 2.2 Basic Usage (Fluent Declaration — Recommended Entry Point)

NCMapper recommends declaring mappings via the **Fluent pipeline**: mapping rules are centralized in a configuration class, keeping entities and DTOs clean (no attributes required).

```csharp
// 1. Define the entity
public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}

// 2. Define the DTO (plain class, no attributes needed)
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}

// 3. Write the mapping configuration class:
//    Annotate with [MapperConfig], and declare a static method inside (name arbitrary, signature fixed)
[MapperConfig]
public class UserMapperConfig
{
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<UserEntity, UserDto>();   // Auto-matches properties by name and type; no per-property declaration needed
}
```

A runnable version of Step 4 — “Mapping: Shared entry point with the attribute-based pipeline; same-name properties assigned automatically” — demonstrating how `Mapper.Map` serves as the unified entry point, and how types annotated with `[MapFrom]` are handled (same underlying generated code as Fluent declarations):

[!code-csharp[Automatic mapping by matching property names](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

**No manual registration is required** after declaring the configuration class: the source generator scans the body of the `Configure` method at compile time to generate mapping code, which is automatically registered into `Mapper` via a module initializer.

When automatic name-based matching is insufficient, append terminal methods to the `For(...)` chain (e.g., for renaming, ignoring, masking, custom conversions, or conditional guards). The full list of terminal methods is available in [Fluent Entry Point](../how-to/fluent/basics.md). Below is a complete chained example (`Map` for renaming + nested path + `Ignore` + `Mask` for obfuscation + `AfterMapping` for post-processing):

[!code-csharp[Fluent basic chain: Map/Ignore/Mask/AfterMapping in sequence](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper)]

[!code-csharp[Fluent basic chain: Map/Ignore/Mask/AfterMapping in sequence · Configure](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_Configure)]

[!code-csharp[Fluent basic chain: Map/Ignore/Mask/AfterMapping in sequence · ApplyComputedFields](~/samples/NCMapper.Samples/01-Fluent/FluentModels.cs#FluentBasics_ChainOrderMapper_ApplyComputedFields)]

Runnable verification:

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 101
```

Correspondence between sample scenarios and documentation pages is listed in the [Samples Scenario Mapping Table](../reference/samples-index.md).

### 2.3 Equivalent Approach: Attribute-Based Pipeline

The same mapping pair can alternatively be declared using the **attribute-based pipeline**: simply annotate the DTO class directly with `[MapFrom]`, making the mapping rules self-descriptive alongside the type. Both pipelines offer equivalent capabilities; the source generator produces **identical** mapping code for either approach, resulting in zero runtime differences:

```csharp
// Annotating the DTO completes the declaration — no configuration class needed
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

Only **one declaration per type pair** is allowed (choose either Fluent or attribute-based); declaring both simultaneously triggers compile-time error `NCMAP_F11`. A full list of attributes is available in the [Attribute Quick Reference](../reference/attributes-index.md); cross-API feature selection guidance and dimension-by-dimension equivalence details are covered in [Dual API Surface Comparison](../reference/api-comparison.md).

### 2.4 Common Invocation Entry Points

Invocation entry points are shared across both pipelines (using the same `UserEntity → UserDto` example from Section 2.2):

```csharp
// 1. With Action callback: perform additional processing after mapping
var dto2 = Mapper.Map<UserEntity, UserDto>(user, (s, t) =>
{
    t.Name = t.Name.ToUpper();
});

// 2. List mapping
var users = new List<UserEntity> { ... };
var dtos = Mapper.MapList<UserEntity, UserDto>(users);

// 3. Update an existing object (only mapped fields are overwritten)
var existing = new UserDto();
Mapper.Map(user, existing);

// 4. Extension method entry points (source-generated extension methods — more concise syntax)
//    These rely on the *same* source-generated mapping code used by Mapper.Map / MapList:
//    Internally, Mapper.Map first attempts to invoke the source-generated delegate
//    (automatically registered via [ModuleInitializer]), falling back to expression trees only if no match is found.
//    Extension methods are *not* the "only path" to source-generated code.
//    Performance is virtually identical (extension methods avoid one delegate indirection — negligible difference);
//    choose based on syntactic preference.
//    To use extension methods, import the generated namespace (not required for Mapper.Map):
using NCMapper.Generated;  // Required only when calling extension methods
var dto3 = user.MapToUserDto();               // Equivalent to Mapper.Map<UserEntity, UserDto>(user)
var dto4 = user.MapToUserDto((s, t) => { });  // With Action callback
var dtos2 = users.MapToUserDtoList();          // Equivalent to Mapper.MapList
```

### 2.5 DI Registration

```csharp
services.AddNCMapper();

// Usage
public class MyService
{
    private readonly IMapper _mapper;
    public MyService(IMapper mapper) => _mapper = mapper;
    public void Do() => _mapper.Map<Entity, Dto>(entity);
}
```
