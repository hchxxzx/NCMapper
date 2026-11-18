> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: how-to/infrastructure/di
Diagnostic codes involved: (none)
Library version: v1.0.0+
Last updated: 2026-09-15
---

# DI Integration and Configuration

## Overview

NCMapper supports registration into the .NET DI container via `IServiceCollection.AddNCMapper()`, enabling injection and use of the `IMapper` interface. It supports custom configuration via `NCMapperOptions` (e.g., deep-copy strategy, recursion depth) and binding configuration from `IConfiguration`.

Core APIs: `services.AddNCMapper()` / `services.AddNCMapper(opts => { ... })` / `services.AddNCMapper(configuration)` / `IMapper`

---

## 1. Basic Registration

### Usage

[!code-csharp[Full DI registration in a generic host](~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs#DI_ResolveMapper)]

**Key points**:

- `AddNCMapper()` registers `IMapper` as a singleton.
- The `IMapper` interface behaves identically to the static `Mapper` class.
- The interface method `IMapper.Map<T, U>(src)` behaves identically to `Mapper.Map<T, U>(src)`.
- Suitable for dependency-injection scenarios such as ASP.NET Core and Worker Services.

### IMapper Interface

```csharp
public interface IMapper
{
    TTarget? Map<TSource, TTarget>(TSource source);
    TTarget? Map<TSource, TTarget>(TSource source, Action<TSource, TTarget>? action);
    void Map<TSource, TTarget>(TSource source, TTarget target);
    void Map<TSource, TTarget>(TSource source, TTarget target, Action<TSource, TTarget>? action);
    List<TTarget>? MapList<TSource, TTarget>(IEnumerable<TSource> sources);
    List<TTarget>? MapList<TSource, TTarget>(IEnumerable<TSource> sources, Action<TSource, TTarget>? action);
    TTarget? Compose<TTarget>(params object[] sources);  // Implemented as params object[], no primarySource parameter
    TTarget? DeepCopy<T>(T source);
    TTarget? DeepCopy<TTarget>(object source);  // This overload does not exist; for cross-type deep copy, use Mapper.DeepCopy<TSource, TTarget>
}
```

---

## 2. NCMapperOptions Configuration

### Usage

[!code-csharp[Register custom configuration using AddNCMapper(options callback) — runnable wrapper](~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs#DI_ResolveMapperWithOptions)]

### Configuration Options

| Option | Type | Default Value | Description |
|---|---|---|---|
| `ValidationEnabled` | `bool` | `true` | Enables mapping validation. |
| `TraceEnabled` | `bool` | `false` | Enables mapping diagnostic tracing. |
| `MaxRecursionDepth` | `int` | `64` | Maximum recursion depth for mapping/deep copying (prevents stack overflow). |
| `MaxInlineDepth` | `int` | `5` | Maximum inline depth during expression tree construction. |
| `TraceOutput` | `TextWriter?` | `null` (defaults to `Console.Out`) | Destination for trace output; `TextWriter` cannot be bound from JSON — only configurable in code. |
| `DeepCopyStrategy` | `DeepCopyStrategy` | `Fast` | Deep-copy strategy (`Fast` / `ExpressionTree` / `Json`). |
| `EFCoreProjectionAsNoTrackingDefault` | `static bool` | `true` | Whether EF Core AsDto projections default to `AsNoTracking`; can be disabled globally. |

> `EFCoreProjectionAsNoTrackingDefault` is a `static` property. Modifying it affects global EF Core projection behavior (not isolated per DI container — not container-scoped).

### DeepCopyStrategy Enumeration

```csharp
public enum DeepCopyStrategy
{
    /// <summary>
    /// Fast strategy (default) — high performance, supports circular references (Acyclic zero-detection + full Cyclic/Unknown detection), but does *not* preserve DAG reference sharing.
    /// </summary>
    Fast,

    /// <summary>
    /// ExpressionTree strategy — includes circular-reference detection, prioritizes safety.
    /// </summary>
    ExpressionTree,

    /// <summary>
    /// JSON strategy — serialization + deserialization, fallback for compatibility.
    /// </summary>
    Json
}
```

**Key points**:

- `MaxRecursionDepth` controls the maximum recursion depth for both mapping and deep copying.
- `DeepCopyStrategy` controls the global deep-copy strategy (affects both `Mapper.DeepCopy` and `IMapper.DeepCopy`).
- `ValidationEnabled` / `TraceEnabled` control validation and diagnostics toggles.
- `TraceOutput` specifies the trace output destination — configurable only in code (cannot be bound from `appsettings.json`).
- Instance-level options are set once during `AddNCMapper` and are immutable at runtime; `EFCoreProjectionAsNoTrackingDefault` is a `static` global toggle.

---

## 3. Binding from IConfiguration

The `AddNCMapper(IConfiguration)` overload internally calls `configuration.Bind(options)` to deserialize a configuration section into instance-level properties of `NCMapperOptions`. This is suitable for environments like ASP.NET Core where `IConfiguration` is already built.

### appsettings.json

```json
{
  "NCMapper": {
    "ValidationEnabled": false,
    "TraceEnabled": false,
    "MaxRecursionDepth": 128,
    "MaxInlineDepth": 4,
    "DeepCopyStrategy": "ExpressionTree"
  }
}
```

### Registration

```csharp
// Program.cs (ASP.NET Core)
var builder = WebApplication.CreateBuilder(args);

// Bind from configuration section "NCMapper"
builder.Services.AddNCMapper(builder.Configuration.GetSection("NCMapper"));

var app = builder.Build();
```

> The default configuration section name is `NCMapper`. If your section uses a different name, pass the corresponding `GetSection`, e.g., `configuration.GetSection("MyMapper")`.

### Bindable Keys

| Configuration Key | Type | Bindable from Configuration? |
|---|---|---|
| `ValidationEnabled` | `bool` | ✅ Yes |
| `TraceEnabled` | `bool` | ✅ Yes |
| `MaxRecursionDepth` | `int` | ✅ Yes |
| `MaxInlineDepth` | `int` | ✅ Yes |
| `DeepCopyStrategy` | `enum` | ✅ Yes (as string: `"Fast"` / `"ExpressionTree"` / `"Json"`) |
| `TraceOutput` | `TextWriter` | ❌ **Not bindable**: No JSON equivalent type — must be assigned in code only. |
| `EFCoreProjectionAsNoTrackingDefault` | `static bool` | ❌ **Not bindable**: `static` members are ignored by `ConfigurationBinder.Bind` — must be set via the `Action` overload. |

### Two Binding Blind Spots (Important)

```csharp
// ❌ The following two settings are silently ignored when configured via appsettings.json:

// 1. TraceOutput (TextWriter — no JSON representation)
//    → Use the Action overload instead:
builder.Services.AddNCMapper(o =>
{
    o.TraceOutput = Console.Out;   // Settable only in code
});

// 2. EFCoreProjectionAsNoTrackingDefault (static property — Bind only processes instance properties)
//    → Use the Action overload (note: it's a static property):
builder.Services.AddNCMapper(o =>
{
    NCMapperOptions.EFCoreProjectionAsNoTrackingDefault = false;
});
```

> If you need both configuration binding *and* programmatic overrides in the same setup, chain the overloads: first call `AddNCMapper(config.GetSection("NCMapper"))` for binding, then `AddNCMapper(o => { o.TraceOutput = ...; })` for supplemental settings — the latter modifies the same `NCMapperOptions` instance (last assignment wins).

### Other Configuration Sources

`IConfiguration` supports multi-source composition, so the following also apply:

- **Environment variables**: `NCMapper__MaxRecursionDepth=256` (double underscore denotes nesting)
- **Command line**: `--NCMapper:DeepCopyStrategy=Json`
- **User secrets / other custom configuration providers**

**Key points**:

- `AddNCMapper(IConfiguration)` is functionally equivalent to `AddNCMapper(opts => configuration.GetSection(...).Bind(opts))`.
- The default section name is `NCMapper`, but it’s customizable.
- Only **instance-level read/write properties** are bound; `TraceOutput` and the `static` `EFCoreProjectionAsNoTrackingDefault` require the `Action` overload.
- All standard .NET configuration sources are supported (`appsettings` / environment variables / command line / custom providers).

---

## 4. Relationship Between Static `Mapper` and DI `IMapper`

| Dimension | Static `Mapper` | DI `IMapper` |
|---|---|---|
| Registration | Not required | `services.AddNCMapper()` |
| Configuration source | Global static | Injected via DI container |
| Lifetime | Process-scoped | Singleton |
| Configuration isolation | None (globally shared) | None (globally shared — later registrations override earlier ones) |
| Extension methods | ✅ `src.MapToXxxDto()` | ❌ Interface methods only |
| Typical usage | Console apps, simple scenarios | ASP.NET Core, Worker Services |

### Configuration Isolation Example (Implemented as process-wide global sharing)

```csharp
// ⚠️ Options passed to AddNCMapper are written directly to global statics (e.g., Mapper.* / DeepCopyProviderBase.Current),
// resulting in process-wide global sharing: later registrations overwrite earlier ones — mappers are *not* independent.
// For true isolation: use separate AppDomains/processes, or await future support for per-instance configuration.
var services1 = new ServiceCollection();
services1.AddNCMapper(opts => opts.MaxRecursionDepth = 32);
var mapper1 = services1.BuildServiceProvider().GetRequiredService<IMapper>();

var services2 = new ServiceCollection();
services2.AddNCMapper(opts => opts.MaxRecursionDepth = 64);
var mapper2 = services2.BuildServiceProvider().GetRequiredService<IMapper>();
// In practice, mapper1 and mapper2 share the same process-wide global configuration — the latter (`MaxRecursionDepth=64`) overwrites the former; they are not independent.
```

---

## 5. ASP.NET Core Integration

### Usage

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register NCMapper
builder.Services.AddNCMapper(opts =>
{
    opts.MaxRecursionDepth = 32;
    opts.DeepCopyStrategy = DeepCopyStrategy.Fast;
});

var app = builder.Build();

// Inject and use in Controller
app.MapGet("/users/{id}", (int id, IMapper mapper, IUserService userService) =>
{
    var entity = userService.Get(id);
    return mapper.Map<UserEntity, UserDto>(entity);
});

app.Run();
```

**Key points**:

- `AddNCMapper()` is called during the `ConfigureServices` phase.
- Controllers/handlers inject `IMapper` via constructor injection.
- Registered as a singleton — thread-safe.

---

## Common Misuses

### Misuse 1: Using Static `Mapper.Map` in DI Scenarios

```csharp
// ❌ Misuse: Using static Mapper.Map in a DI context
// Custom configuration applied via DI will *not* affect static Mapper.Map
public class UserService
{
    public UserDto GetUser(int id)
    {
        var entity = _repo.Get(id);
        return Mapper.Map<UserEntity, UserDto>(entity);  // Ignores DI configuration
    }
}

// ✅ Correct: Use injected IMapper instance
public class UserService
{
    private readonly IMapper _mapper;
    public UserService(IMapper mapper) => _mapper = mapper;

    public UserDto GetUser(int id)
    {
        var entity = _repo.Get(id);
        return _mapper.Map<UserEntity, UserDto>(entity);  // Honors DI configuration
    }
}
```

### Misuse 2: Multiple Registrations of AddNCMapper

```csharp
// ❌ Misuse: Calling AddNCMapper multiple times
services.AddNCMapper(opts => opts.MaxRecursionDepth = 32);
services.AddNCMapper(opts => opts.MaxRecursionDepth = 64);  // Overwrites the previous registration
// Only the last registration takes effect; earlier configurations are overwritten

// ✅ Correct: Register only once
services.AddNCMapper(opts =>
{
    opts.MaxRecursionDepth = 32;
    // All configurations in one place
});
```

### Misuse 3: Registering IMapper as Scoped or Transient

```csharp
// ❌ Misuse: Manually overriding IMapper lifetime
services.AddScoped<IMapper, NCMapperMapper>();  // Should NOT be manually registered
// AddNCMapper already registers IMapper as a Singleton; manual registration causes duplication

// ✅ Correct: Use AddNCMapper, which correctly registers IMapper as a Singleton
services.AddNCMapper();
```

> `IMapper` is thread-safe; registering it as a Singleton is the correct choice. The mapping cache is globally shared and safe for concurrent access across multiple threads.

---

## Summary

| Capability | API | Feature |
|---|---|---|
| Basic Registration | `services.AddNCMapper()` | Registers as Singleton |
| Configurable Registration | `services.AddNCMapper(opts => { ... })` | Accepts `NCMapperOptions` |
| Configuration Binding | `services.AddNCMapper(configuration)` | Binds from `IConfiguration` |
| Mapping Interface | `IMapper.Map<T, U>(src)` | Behaves identically to the static `Mapper` |
| Deep Copy Interface | `IMapper.DeepCopy<T>(src)` | Uses DI-configured strategies |
| Compose Interface | `IMapper.Compose<T>(p, ...)` | Composes from multiple sources |

---

## Companion Sample

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 603
```

Core sample source code (single-point, same-origin; runnable and verifiable via `dotnet run --scenario`):

[!code-csharp[DI Resolution](~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs#DI_ResolveMapper)]

Source code resides in `samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs`, covering sub-scenarios such as DI registration, `IMapper` injection, and mapping usage.

---

## Further Reading

- Related Topics: [Validation & Diagnostics](validation.md) · [Basic Mapping](../declarative/basics.md)
- In-depth Technical Documentation: [DI Integration](../../reference/di-integration.md) §9
- DI Source Code: `src/NCMapper/NCMapperServiceExtensions.cs` · `src/NCMapper/NCMapperOptions.cs` · `src/NCMapper/IMapper.cs`
