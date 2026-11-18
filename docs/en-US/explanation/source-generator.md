> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §7 Source Generators -->

## 7. Source Generators

**How It Works** — `NCMapper.SourceGen` is a Roslyn **incremental source generator** (`IsRoslynComponent=true`) distributed as an analyzer with the NuGet package. It runs automatically at compile time with zero configuration. During compilation, it scans all DTOs annotated with `[MapFrom]`, extracts mapping rules (source type, property mappings, `[Convert]` converters, etc.), and generates equivalent mapping code: `GeneratedMapper` (core mapping methods) and `GeneratedExtensions` (`MapToXxxDto` extension methods). The generated delegates are automatically registered with `Mapper` at assembly load time via `[ModuleInitializer]`, enabling `Mapper.Map` to preferentially invoke source-generated delegates—falling back to runtime expression trees only when no match is found.

**Significance** —

| Dimension      | Description                                                                 |
|----------------|-----------------------------------------------------------------------------|
| Zero Reflection | Mapping code is generated at compile time and invoked directly at runtime, eliminating reflection overhead. |
| AOT-Friendly    | All code is determined at compile time; NativeAOT deployments avoid runtime reflection/expression-tree compilation issues. |
| Type Safety     | Mapping rules are validated at compile time; property renames trigger immediate compiler errors—not silent runtime failures. |
| Compile-Time Diagnostics | Configuration errors (e.g., converter type mismatches, enum mapping mismatches) emit warnings/errors/hints with diagnostic IDs `NCMAP001`–`NCMAP034` [excluding `004`] and `NCMAP099`. |
| Optimal Performance Path | Generated extension methods incur zero delegate overhead and are JIT-inlinable; `Mapper.Map` prioritizes source-generated delegates. |

> Section 7.1 below illustrates the specific stages of the Roslyn incremental pipeline.

### 7.1 Architecture

```
Roslyn Incremental Pipeline
  │
  ├── ForAttributeWithMetadataName("NCMapper.Attributes.MapFromAttribute")
  │     └── Filters all classes annotated with [MapFrom]
  │
  ├── MappingExtractor.ExtractMappingInfo()
  │     └── Extracts semantic information + emits mapping-related diagnostics (including 099)
  │
  ├── Collect() + Combine(globalOptions)
  │
  └── RegisterSourceOutput()
        ├── CodeGenerator.GenerateMapperCode()  — Generates mapping code
        └── EmitDiagnostics()                    — Emits diagnostic warnings
```

> **`CancellationToken` Support**: The pipeline checks `CancellationToken` inside loop bodies, supporting IDE cancellation of long-running code generation operations.  
>   
> **Automatic Registration via `ModuleInitializer`**: The generated `GeneratedMapperInitializer` class uses the `[ModuleInitializer]` attribute to automatically register all mapping delegates with `Mapper` upon module load—no manual invocation required by users.

### 7.2 Generated Artifacts

| Class                            | Content                                             |
|----------------------------------|-----------------------------------------------------|
| `GeneratedMapper`                | Core mapping methods, Action wrappers, `MapList`, `DeepCopy` |
| `GeneratedExtensions`            | Extension methods: `MapTo[Target]()` / `MapTo[Target]List()` |
| `GeneratedMapperInitializer`     | `[ModuleInitializer]` that auto-registers all delegates with `Mapper` |

> **Fallback Comments**: When the source generator cannot resolve a type conversion at compile time (e.g., non-implicit conversions, enums), it inserts a comment before the generated assignment: `// fallback to runtime: TypeConvertHelper.ConvertValue<S, T>(src.Prop)`. This helps developers identify properties falling back to runtime resolution.

### 7.3 Extension Methods (`GeneratedExtensions`)

At compile time, the source generator automatically produces extension methods for each DTO annotated with `[MapFrom]`, placed under the `NCMapper.Generated` namespace. These extension methods call core mapping logic directly—**incurring zero delegate overhead** and being JIT-inlinable (`[MethodImpl(AggressiveInlining)]`). They represent the highest-performance invocation path.

#### Available Methods

For each `XxxDto` annotated with `[MapFrom(typeof(XxxEntity))]`, the following extension methods are auto-generated:

| Extension Method                 | Signature                                                     | Description         |
|----------------------------------|----------------------------------------------------------------|---------------------|
| `MapToXxxDto()`                  | `this XxxEntity → XxxDto?`                                     | Basic mapping, zero delegate overhead |
| `MapToXxxDto(Action)`            | `this XxxEntity + Action → XxxDto?`                            | Mapping with callback |
| `MapToXxxDtoList()`              | `this IEnumerable<XxxEntity> → List<XxxDto>?`                | List mapping          |
| `MapToXxxDtoList(Action)`        | `this IEnumerable<XxxEntity> + Action → List<XxxDto>?`       | List mapping with callback |
| `MapToXxxDto(target)`            | `this XxxEntity + XxxDto → void`                               | Update existing target object |
| `MapToXxxDto(target, Action)`    | `this XxxEntity + XxxDto + Action → void`                      | Update existing target object with callback |

> ORM projections are *not* exposed via extension methods. Projection entry points use the terminal operation `AsDto` (see [ORM Projection Design](orm-projection-design.md)), which internally handles fallback logic automatically.

#### Usage Example

[!code-csharp[Four entry forms for source generators: direct call / with Action / list / update existing](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#Core_Generated_EntryExtensions)]

#### Difference from `Mapper.Map`

| Dimension      | `Mapper.Map<TEntity, TDto>()` | `entity.MapToUserDto()`        |
|----------------|--------------------------------|--------------------------------|
| Invocation Style | Generic method                   | Instance extension method         |
| Delegate Overhead | Yes (generic cache lookup)        | None (direct call to `GeneratedMapper.Core`) |
| JIT Inlining     | No                              | Yes (`AggressiveInlining`)      |
| Use Case         | General-purpose / dynamic / non-source-gen paths | Performance-critical hot paths   |
| Availability     | Always available                  | Requires successful source-gen compilation |

### 7.4 Toggle Switch

```xml
<PropertyGroup>
  <NCMapperSourceGenEnabled>false</NCMapperSourceGenEnabled>
</PropertyGroup>
```

### 7.5 Mapping Relationship Visualization

```xml
<NCMapperGenerateReport>true</NCMapperGenerateReport>
```

At compile time, generates `NCMapperMappingReport.g.cs` under `obj/Generated/`, containing a Markdown-formatted mapping relationship report. Covers categories including direct mappings, `[MapProperty]` renames, navigation flattening, and `[Convert]`-based mappings.

### 7.6 `NCMAP018` Analyzer: Array `Contains` Expression Tree Binding Detection

`ArrayContainsInExpressionAnalyzer` is a standalone `DiagnosticAnalyzer` embedded in the source generator assembly (`NCMapper.SourceGen.dll`), distributed as an analyzer asset alongside the core package. Though co-located in the same assembly, its responsibilities are strictly separated from the incremental source generator: the generator handles code generation; this analyzer performs compile-time detection.

**Trigger Mechanism:**  
1. Detects `InvocationExpression` syntax nodes invoking the `Contains` method.  
2. Uses semantic model analysis to determine whether the method symbol resolves to `MemoryExtensions.Contains(ReadOnlySpan<T>, T)` (not `Enumerable.Contains(IEnumerable<T>, T)`).  
3. Determines whether the invocation occurs within an expression tree context (`Expression<Func<...>>` lambda).

When *all three conditions* are satisfied, `NCMAP018` (Warning) is emitted. Starting with .NET 10/C# 14, overload resolution changes cause `T[].Contains()` inside expression trees to bind to `MemoryExtensions.Contains`, whereas ORM expression tree parsers (e.g., FreeSql, EF Core) only recognize `Enumerable.Contains`—leading to `NotSupportedException` at runtime.

**Recommended Fixes:**

```csharp
// Pattern triggering NCMAP018 (.NET 10 + C# 14)
fsql.Select<OrderEntity>()
    .Where(o => ids.Contains(o.Id))  // ids is int[] → binds to MemoryExtensions.Contains → NotSupportedException
    .ToList();

// Fix #1: Explicitly qualify with Enumerable.Contains
fsql.Select<OrderEntity>()
    .Where(o => Enumerable.Contains(ids, o.Id))
    .ToList();

// Fix #2: Convert array to List<T> (List<T>.Contains avoids overload resolution change)
var idList = ids.ToList();
fsql.Select<OrderEntity>()
    .Where(o => idList.Contains(o.Id))
    .ToList();
```

> **Automatic TFM Adaptation**: The analyzer relies on method symbol resolution—not explicit target framework checks—so on `net8`/`net9` (C# 13), `T[].Contains` still binds to `Enumerable.Contains`, avoiding false positives for `NCMAP018`.
