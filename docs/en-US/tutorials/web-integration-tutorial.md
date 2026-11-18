> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: tutorials/web-integration-tutorial
Diagnostic codes involved:
Library version: v1.0.0+
Last updated: 2026-09-29
---

# Web Project Integration Tutorial: Integrating with ASP.NET Core

## Overview

This tutorial addresses integration from scratch: how to adopt NCMapper in an ASP.NET Core project — which NuGet package to install, how to register it, how to obtain mapping capabilities in controllers or services, and what common configurations are available. By following this guide step-by-step, your project will gain full support for *dependency injection*, *declarative mapping*, and *compile-time code generation*.

The examples align semantically with scenario 06 (603) in `samples/NCMapper.Samples` and are executable for verification. Each section provides concise illustrative code snippets; a complete end-to-end example is included at the end for holistic understanding. For exhaustive details on registration and configuration, refer to [DI Integration and Configuration](../how-to/infrastructure/di.md); this tutorial focuses specifically on getting from zero to a working integration.

---

## 1. Installation and Registration

Install the core package (`NCMapper`) into your web project. The DI extension is bundled within the core package — no separate reference is required:

```bash
dotnet add package NCMapper
```

Register NCMapper in `Program.cs` (three lines to integrate):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNCMapper();          // Register with default configuration
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
```

`AddNCMapper` offers three overloads — choose based on your preferred configuration source:

| Overload | Purpose |
|----------|---------|
| `AddNCMapper()` | Default configuration (validation enabled, trace disabled, recursion depth limit = 64) |
| `AddNCMapper(opts => ...)` | Configure directly in code |
| `AddNCMapper(configuration.GetSection("NCMapper"))` | Load configuration from `appsettings.json` |

---

## 2. Using Injection: Mapping in Controllers and Services

After registration, `IMapper` can be injected directly into controllers or services (same pattern as demo scenario `DI_ResolveMapper`):

[!code-csharp[Generic Host DI: AddNCMapper Registration and IMapper Resolution](~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs#DI_ResolveMapper)]

In a real-world project, it looks like this:

```csharp
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IMapper _mapper;

    public UsersController(IMapper mapper) => _mapper = mapper;

    [HttpGet("{id}")]
    public IActionResult Get(int id)
    {
        var entity = _userService.Find(id);
        var dto = _mapper.Map<UserEntity, UserDto>(entity);
        return Ok(dto);
    }
}
```

The `IMapper` interface exposes only the unified entry points: `Map`, `MapList`, and `Map(src, target)` for in-place updates. Declarative extension methods (e.g., `src.MapToUserDto()`) do *not* rely on DI — they’re invoked directly after adding `using NCMapper.Generated`. Both approaches coexist without conflict.

---

## 3. Declaring Mappings: Choose Your Preferred Channel

After integration, declare type mappings using one of two channels — select based on team preference (see [Dual-Channel Architecture](../explanation/dual-channel.md) for underlying principles):

- **Attribute Channel**: Apply `[MapFrom]` to DTO classes; properties with matching names are automatically mapped — fastest onboarding, since modifying the DTO modifies the mapping.
- **Fluent Channel**: Apply `[MapperConfig]` to a dedicated configuration class and define mappings via fluent syntax — ideal when mappings grow complex or when you want to keep DTOs clean and free of attributes.

Both channels converge into the same generated code at compile time. Mixing different channel types across distinct type pairs is fully supported. However, avoid declaring the *same* type pair in both channels — doing so triggers compile-time diagnostic `NCMAP_F11`.

---

## 4. Common Configuration Options

Configuration can be specified either in code or in `appsettings.json`; both are equivalent:

```json
"NCMapper": {
  "ValidationEnabled": true,
  "TraceEnabled": false,
  "MaxRecursionDepth": 128,
  "DeepCopyStrategy": "Fast"
}
```

| Option | Default | Description |
|--------|---------|-------------|
| `ValidationEnabled` | `true` | Whether to run mapping validation during startup |
| `TraceEnabled` | `false` | Enables diagnostic logging for mapping (useful when troubleshooting projection degradation) |
| `MaxRecursionDepth` | `64` | Maximum allowed nesting depth for recursive mappings; exceeding this threshold triggers a circular-reference detection |
| `DeepCopyStrategy` | `Fast` | Strategy used for deep copying (`Fast` / `ExpressionTree` / `Json`) |

For web projects, we recommend keeping validation enabled to surface misconfigurations early at startup. Temporarily enable `TraceEnabled` only when diagnosing projection-related issues.

---

## 5. Recommended Layering Strategy

Where should mapping logic reside? Here’s a practical, enforceable convention:

| Layer | Responsibility | Mapping Usage |
|-------|----------------|----------------|
| DTO Project | Defines DTOs + mapping declarations (`[MapFrom]` or Fluent config classes) | Declarations only — no invocation |
| Service Layer | Orchestrates business logic | Performs entity ↔ DTO conversion *at service boundaries* |
| Controller | Handles HTTP protocol concerns | **No mapping logic** — only receives/sends DTOs |

Why centralize mapping at service boundaries?  
- Mapping declarations become auditable in one place (`Mapper.Validate()` validates all declared type pairs in one call).  
- Controllers remain thin and focused.  
- Entity–DTO relationships are discoverable in a single location.

---

## 6. Pre-Deployment Self-Checklist

| Check | Method |
|-------|--------|
| All declared type pairs generate successfully, with no conflicts | Zero diagnostics at build time + `Mapper.Validate()` reports no issues |
| DI injection works | Inject `IMapper` into any service and execute a real mapping |
| AOT / trimming deployment (if applicable) | Ensure all type pairs use declarative syntax; see [AOT Deployment](../how-to/aot/deployment.md) |
| Projection scenarios (if applicable) | Enable `TraceEnabled` to detect unexpected degradation; see [ORM Degradation Mechanism](../explanation/orm-degradation.md) |

---

## 7. Frequently Asked Questions

**What does `AddNCMapper` actually register?**  
It registers the `IMapper` instance and related configuration. The source generator emits static mapping methods compiled directly into your assembly (automatically registered upon assembly load), independent of DI. DI merely provides a standardized injection point for `IMapper`.

**Do static `Mapper.Map` and injected `IMapper` conflict?**  
No. They share the same generated code and internal caching. Static usage is convenient for quick calls; injected usage supports testability and mocking. Mixed usage in the same project is safe and encouraged.

**What happens if configuration is invalid?**  
When using `AddNCMapper(configuration...)`, missing fields fall back to defaults. Type mismatches in code-based configuration cause immediate compile-time errors. To surface configuration issues early, call `Mapper.ValidateOrThrow()` at startup.

---

## 8. Complete Working Example

End-to-end minimal runnable code (pedagogical completeness; implement data access methods like `Find` per your actual project):

```csharp
// ================= 1. Program.cs: Registration =================
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNCMapper(opts =>
{
    opts.ValidationEnabled = true;     // Surface mapping misconfigurations at startup
    opts.MaxRecursionDepth = 128;      // Increase for deeply nested models, if needed
});
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();

// ================= 2. Models/UserDto.cs: Declare Mapping =================
using NCMapper.Attributes;

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}

// ================= 3. Services/UserService.cs: Perform Mapping at Service Boundary =================
using NCMapper;

public class UserService
{
    private readonly IMapper _mapper;

    public UserService(IMapper mapper) => _mapper = mapper;

    public UserEntity? Find(int id)
    {
        // Implement data access per your stack (EF Core / Dapper / Repository, etc.)
        throw new NotImplementedException();
    }

    public UserDto ToView(UserEntity entity)
        => _mapper.Map<UserEntity, UserDto>(entity)!;   // Mapping centralized at service boundary
}

// ================= 4. Controllers/UsersController.cs: Keep Controllers Thin =================
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly UserService _service;

    public UsersController(UserService service) => _service = service;

    [HttpGet("{id}")]
    public IActionResult Get(int id)
    {
        var entity = _service.Find(id);
        if (entity is null) return NotFound();
        return Ok(_service.ToView(entity));   // Controllers only receive/send DTOs — no mapping logic
    }
}
```

Read this code top-down: registration (3-line setup) → declaration (one `[MapFrom]` enables bidirectional mapping) → service layer (mapping enforced at boundary) → controller (DTO-only interface).

---

## Related Documentation

- [DI Integration and Configuration](../how-to/infrastructure/di.md): Full reference for registration and configuration  
- [Mapping Validation](../how-to/infrastructure/validation.md): Interpreting `Mapper.Validate()` health reports  
- [Dual-Channel Architecture](../explanation/dual-channel.md): Rationale behind choosing between attribute and fluent declaration  
- [Daily Bidirectional Mapping Tutorial](mapping-tutorial.md): How to write your first mapping after integration
