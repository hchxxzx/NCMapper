> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: getting-started/installation
Library Version: v1.0.0+
Last Updated: 2026-09-15
---

# Installation and Package Selection

> NuGet package matrix and selection guidance. For API style selection (Fluent vs. Attribute), see [overview](overview.md) and [api-comparison](../reference/api-comparison.md).

## Package Matrix

| Package | Purpose | When to Install |
| -- | ---- | -------- |
| `NCMapper` | Core library (runtime engine + expression trees + reflection fallback) + source generator (distributed as an Analyzer asset with the package) | Required (for all scenarios) |
| `NCMapper.FreeSql` | FreeSql integration (`AsDto<T>()` projection, pagination, tree construction) | When using FreeSql |
| `NCMapper.EFCore` | EF Core integration (`AsDto<T>()` projection, fallback detection) | When using EF Core |
| `NCMapper.Dapper` | Dapper integration (`AsDto` column name mapping, `ToPagedList`, `ToTreeList`) | When using Dapper |

```bash
dotnet add package NCMapper
dotnet add package NCMapper.FreeSql    # Optional
```

## Target Frameworks

Supports net8.0 / net9.0 / net10.0.

## Enabling the Source Generator

The source generator is built into the `NCMapper` package. Referencing the core package automatically enables it (distributed as an Analyzer asset); no separate installation is required. Verification: After annotating a DTO with `[MapFrom]`, compilation should generate `MapToXxxDto()` extension methods. To disable: `<NCMapperSourceGenEnabled>false</NCMapperSourceGenEnabled>`.

## Minimal Working Example

```csharp
using NCMapper.Attributes;

[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
```

[!code-csharp[Generated entry extension methods in three forms (create/callback/update)](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#Core_Generated_EntryExtensions)]

Next steps: [Quick Start](quickstart.md), [15-Minute Tutorial](tutorial-15min.md).
