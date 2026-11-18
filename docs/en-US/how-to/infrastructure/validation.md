> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: how-to/infrastructure/validation
Related diagnostic codes: NCMAP001, NCMAP002, NCMAP005, NCMAP018, NCMAP019, NCMAP032, NCMAP033, NCMAP034, NCMAP035, NCMAP036, NCMAP037, NCMAP099
Library version: v1.0.0+
Last updated: 2026-09-15
---

# Validation and Diagnostics

## Overview

NCMapper provides two runtime diagnostic tools:  
- `MapValidator` checks mapping property completeness.  
- `MapTracer` outputs detailed diagnostic logs of the mapping process.  

Used together, they enable rapid identification of mapping configuration issues during development and on-demand diagnostics in production environments.

Core APIs: `Mapper.Validate()` / `MapValidator.ValidateMapping(srcType, tgtType)` / `MapTracer.GloballyEnabled` / `MapTraceOptions.Enable()` + `MapTracer.Trace`

---

## 1. MapValidator: Property Completeness Validation

### Usage

[!code-csharp[Validate all types or a single type pair](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateMapping)]

### Validation Result Levels

| Level | Meaning | Typical Scenario |
|---|---|---|
| `OK` | All properties matched successfully | Mapping configuration is correct |
| `Warning` | Some properties unmatched | Source type mismatch in `[MapProperty]` (NCMAP002) |
| `Error` | Mapping is unavailable | Target type lacks a public constructor, source type does not exist, etc. |

> **Related API**: `MapValidationResult` — the result type for a single type-pair validation (with properties such as `SourceType`, `TargetType`, `Level`, `MappedCount`, `TotalCount`, `UnmappedProperties`, `TypeMismatchProperties`, `Message`, etc.). It is both the return type of `MapValidator.ValidateMapping(srcType, tgtType)` and the element type of the list returned by `Mapper.Validate()` (source anchor: MapValidator.cs:30).

### Usage

[!code-csharp[Full scan / assembly-scoped validation / single type-pair validation](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAll)]

**Key Points**:

- `Validate()` scans all assemblies loaded in the current AppDomain for type pairs annotated with `[MapFrom]`.
- `Validate(params Assembly[])` restricts scanning to specified assemblies.
- Results are grouped by level: `OK` / `Warning` / `Error`.
- `ValidateOrThrow()` throws `MapConfigurationException` if any `Error`-level result is present.

### Validation Report Output

[!code-csharp[Print validation report](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAll)]

---

## 2. MapTracer: Mapping Process Diagnostics

### Usage

```csharp
// Approach 1: Global toggle
MapTracer.GloballyEnabled = true;

var dto = Mapper.Map<UserEntity, UserDto>(entity);
// → Console output showing each mapping step:
//   [Map] UserEntity → UserDto
//   [Property] Id: 1 → 1 (int → int)
//   [Property] Name: "ZhangSan" → "ZhangSan" (string → string)
//   [Property] Age: 30 → 30 (int → int)
//   [Skip] Email: source type mismatch
//   [Convert] Status: Active → "Active" (enum → string)

MapTracer.GloballyEnabled = false;

// Approach 2: Redirect to TextWriter
using var writer = new StreamWriter("trace.log");
var opts = MapTraceOptions.Enable(writer);
var report = MapTracer.Trace<UserEntity, UserDto>(entity, opts);
// → Writes mapping trace report to trace.log file.
// Note: MapTracer.Enable() does not exist; use MapTraceOptions.Enable() + MapTracer.Trace().
```

**Key Points**:

- Trace output shows every step of mapping: property matching, type conversion, skip reasons.
- Disabled by default in production (incurs ~15–20% performance overhead).
- Can redirect output to a file via `MapTraceOptions.Enable(TextWriter)`.
- Ideal for debugging mapping issues (e.g., “Why wasn’t this property mapped?”).

### Trace Output Tags

| Tag | Meaning |
|---|---|
| `[Map]` | Start of type-pair mapping |
| `[Property]` | Successful property mapping (includes source→target value and type info) |
| `[Skip]` | Property skipped (reason: type mismatch, `[MapIgnore]`, etc.) |
| `[Convert]` | Type conversion (built-in or via `Convert`) |
| `[Mask]` | Data masking applied (includes mask type) |
| `[PostMap]` | Execution of `PostMap` callback |

---

### 2.1 LogCallback and Enable() Factory

Beyond global toggling and `TextWriter` redirection, `MapTracer` supports two advanced usage patterns.

**Integrating with Logging Frameworks (LogCallback)**

Setting `MapTracer.LogCallback` causes all trace output to be forwarded to that callback — useful for integration with `ILogger`, Serilog, or NLog:

[!code-csharp[LogCallback integration with logging frameworks (collector pattern)](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceLogCallback)]

**Key Points**:

- `LogCallback` is a `static Action<string>?`, declared `volatile` to ensure thread visibility.
- The callback receives full trace report text per mapping (one line per mapping), suitable for structured logging.
- Callbacks fire only when tracing is actually enabled (globally or per-call).

**Enable() Factory Method (No Global State Mutation)**

`MapTraceOptions.Enable()` creates an *already-enabled* `MapTraceOptions` instance without modifying the global `GloballyEnabled` state:

[!code-csharp[Capture trace report per call (MapTracer.Trace)](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Trace_PerCallOptions)]

**Mapping Path Classification**: Each property in the trace report is labeled with its mapping strategy, enabling quick root-cause analysis:

| Label | Meaning |
|---|---|
| `direct` | Direct mapping of same-named, same-typed properties |
| `nested (A→B)` | Nested reference-type mapping |
| `json (A↔B)` | `IsFromJson` object ↔ string conversion |
| `convert (A→B)` | Type conversion (built-in or via `Convert`) |
| `nullable→T` / `T→nullable` | Nullable conversion |
| `skipped (MapIgnore)` | Skipped due to `[MapIgnore]` |
| `skipped (source type ≠)` | Skipped due to source type mismatch |
| `⚠️ source field 'x' not found` | Source field missing (related to NCMAP001 / NCMAP002) |

> 💡 `ShowValues` defaults to `false` (CWE-209 mitigation): When enabled, masked properties appear as `[MASKED]` instead of raw values, preventing sensitive data leakage into logs.

---

## 3. MapException and Diagnostic Codes

NCMapper’s compile-time diagnostic codes (full list in [Diagnostic Rules](../../diagnostics/rules.md)) and runtime exceptions form a complete diagnostic chain.

### Compile-Time Diagnostic Codes

| Code | Meaning | Severity |
|---|---|---|
| NCMAP001 | Source field not found (`[MapProperty]` references a field absent from the source type) | Warning |
| NCMAP002 | Incompatible target property type in `[MapProperty]` (assignment impossible) | Warning |
| NCMAP005 | Cross-table field referenced via `[MapProperty(typeof(SubSource), "field")]` does not exist in `SubSource` | Warning |
| NCMAP018 | `Contains` on arrays unsupported in net10 | Warning |
| NCMAP099 | Internal source generator error | Warning |
| NCMAP019 | Missing `[MapFrom]` attribute (mapping attributes present but no `[MapFrom]`; SourceGen emits no code, though runtime mapping still works) | Info |
| NCMAP032 | Update-phase init-only target property skipped (writable only during creation path) | Info |
| NCMAP033 | `[Convert]` reverse direction not consumed by default (conversion path written by type; use `[ConvertBack]` for custom reverse conversion) | Info |
| NCMAP034 | `[Mask]` property participates in reverse write-back (masked value written back verbatim — lossy) | Info |
| NCMAP035 | `[Convert]`/`[ConvertBack]` method reference: no accessible static method found on the anchored type (DeclaredOnly semantics + accessibility whitelist) | Error |
| NCMAP036 | `[Convert]`/`[ConvertBack]` method reference: multiple overloads match signature template (ambiguity) | Error |
| NCMAP037 | `[Convert]`/`[ConvertBack]` method reference: no overload matches any of the four templates (signature mismatch) | Error |

> Full diagnostic code index: [Diagnostic Code Index](../../diagnostics/rules.md).

### Runtime Exceptions

```csharp
try
{
    var dto = Mapper.Map<SourceEntity, TargetDto>(src);
}
catch (MapException ex)
{
    // Errors occurring during mapping execution (e.g., type incompatibility, property access failure)
    Console.WriteLine($"Mapping failed: {ex.Message}");
}
catch (MapConfigurationException ex)
{
    // Configuration errors (e.g., `[Mask]` type does not implement `IMask`)
    Console.WriteLine($"Configuration error: {ex.Message}");
}
```

| Exception Type | Base Class | Trigger Scenario |
|---|---|---|
| `MapException` | `InvalidOperationException` | Errors during mapping execution |
| `MapConfigurationException` | `MapException` | Configuration errors (e.g., `[Mask]` type doesn’t implement `IMask`) |

---

### 3.1 MapException Diagnostic Properties

`MapException` carries three diagnostic properties to precisely locate failure points inside `catch` blocks:

[!code-csharp[Reading MapException's three diagnostic fields](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Infra_Validation_ExceptionFields)]

| Property | Type | Meaning |
|---|---|---|
| `SourceType` | `Type?` | Source type involved in the mapping |
| `TargetType` | `Type?` | Target type involved in the mapping |
| `PropertyName` | `string?` | Name of the property triggering the exception (optional; may be `null`) |

**Exception Inheritance Hierarchy**:

```
InvalidOperationException
    └── MapException                   (errors during mapping execution)
            └── MapConfigurationException  (configuration errors, e.g., [Mask] type doesn’t implement IMask)
```

**Distinguishing Configuration vs. Runtime Errors**:

```csharp
try
{
    var dto = Mapper.Map<SourceEntity, TargetDto>(src);
}
catch (MapConfigurationException ex)
{
    // Configuration error: verify correctness of [MapFrom], [MapProperty], [Mask], etc.
    Console.WriteLine($"[Configuration Error] {ex.Message}");
}
catch (MapException ex)
{
    // Runtime error: data or type conversion failure
    Console.WriteLine($"[Runtime Error] {ex.Message}");
}
```

> Since `MapConfigurationException` inherits from `MapException`, place its `catch` block *before* the base `MapException` handler (more specific exceptions first).

---

## 4. Integration into CI Validation

### Usage

Automatically validate mapping completeness in your CI pipeline:

```csharp
// Validate all mappings after build
var results = Mapper.Validate(typeof(Startup).Assembly);
if (results.Any(r => r.Level == MapValidationLevel.Error))
    throw new InvalidOperationException("Mapping validation failed");

// Or use ValidateOrThrow directly
Mapper.ValidateOrThrow();
```

**Key Points**:

- Call `Validate()` or `ValidateOrThrow()` in CI to enforce correct mapping configuration.
- Focus on `Error`-level results (`Warning`s are acceptable but recommended for remediation).
- Combine with `PrintReport` to emit validation reports into CI logs.

---

## 5. MapTracer and DeepCopy Integration

When `MapTracer` is enabled, `DeepCopy`’s `Action` callback triggers a warning before execution, highlighting potential reference assignment risks:

[!code-csharp[MapTracer-wrapped DeepCopy+Action reference warning](~/samples/NCMapper.Samples/06-Infrastructure/TraceScenarios.cs#Infra_Tracer_DeepCopyProbe)]

## 6. Mapping Relationship Visualization Report

The NCMapper source generator can produce a Markdown-formatted mapping relationship report at compile time, with no runtime dependencies.

**How to enable**: Add the MSBuild property in your `.csproj` file:

```xml
<PropertyGroup>
  <NCMapperGenerateReport>true</NCMapperGenerateReport>
</PropertyGroup>
```

Once enabled, the compiler generates `NCMapperMappingReport.g.cs` under the `obj/Generated/` directory. This file contains the constant `NCMapper.Generated.MappingReport.Report`, which holds the Markdown report text:

```csharp
// obj/Generated/NCMapperMappingReport.g.cs (excerpt)
namespace NCMapper.Generated
{
    internal static class MappingReport
    {
        internal const string Report = @"# NCMapper Mapping Relationship Report
Number of mapping pairs: 12


## UserEntity → UserDto
| Source Field | Target Field | Mapping Method | Notes |
|--------------|--------------|----------------|-------|
| Id | Id | direct | |
| Name | DisplayName | [MapProperty] rename | |
| Address | AddressCity | navigation flatten | Navigation property inferred automatically |
...";
    }
}
```

**Report contents**:

- Source → target mapping relationships for each `[MapFrom]` type pair
- Per-field table: source field, target field, mapping method (`direct` / `[MapProperty] rename` / `navigation flatten` / `[Convert]` / `[Mask]`, etc.), and notes
- Total count of mapping pairs

**Key points**:

- This report is intended solely for review and documentation generation; it does **not** participate in runtime mapping.
- Deterministic builds: Identical source code + identical compiler version yields bit-for-bit identical output (no timestamps), making it suitable for incremental compilation.
- The mapping method categories used in this report align precisely with those used in Trace reports.

---


## Common Misuses


### Misuse 1: Enabling Trace Long-Term in Production

```csharp
// ❌ Misuse: Enabling MapTracer in production
MapTracer.GloballyEnabled = true;  // Performance overhead ~15–20%
// Detailed diagnostic logs emitted on every mapping — severely impacts performance

// ✅ Correct: Enable only temporarily during debugging/troubleshooting
if (env.IsDevelopment())
{
    MapTracer.GloballyEnabled = true;
}
// Keep disabled in production
```


### Misuse 2: Ignoring Validate Warnings

```csharp
// ❌ Misuse: Checking only for Errors, ignoring Warnings
var results = Mapper.Validate();
if (results.Any(r => r.Level == MapValidationLevel.Error))
    throw new InvalidOperationException("Mapping failed");
// Warnings ignored → issues like inconsistent source types in `[MapProperty]` remain unaddressed

// ✅ Correct: Treat Warnings as actionable items
var hasIssues = results.Any(r => r.Level >= MapValidationLevel.Warning);
if (hasIssues)
{
    MapValidator.PrintReport(results, Console.Out, showOk: false);
    // Manually review and decide whether to fix
}
```


### Misuse 3: Using Trace Instead of Proper Performance Profiling

```csharp
// ❌ Misuse: Using MapTracer for performance analysis
MapTracer.GloballyEnabled = true;
var sw = Stopwatch.StartNew();
var dto = Mapper.Map<UserEntity, UserDto>(entity);
sw.Stop();
// Trace itself incurs ~15–20% overhead — measurements become inaccurate

// ✅ Correct: Disable Trace during performance profiling; use professional tools like BenchmarkDotNet
MapTracer.GloballyEnabled = false;
// Measure using BenchmarkDotNet or Stopwatch (after proper warmup)
```

---


## Summary

| Capability | API | Characteristics |
|---|---|---|
| Full validation | `Mapper.Validate()` | Scans all `[MapFrom]` type pairs |
| Scoped validation | `Mapper.Validate(Assembly[])` | Restricts scanning scope |
| Single-type-pair validation | `MapValidator.ValidateMapping(src, tgt)` | Validates a specific type pair |
| Validation with exception throw | `Mapper.ValidateOrThrow()` | Throws exception if any `Error` is found |
| Validation report formatting | `MapValidator.PrintReport(results, writer, showOk)` | Formatted output |
| Global Trace toggle | `MapTracer.GloballyEnabled` | On/off switch; ~15–20% overhead |
| File-based Trace | `MapTraceOptions.Enable(TextWriter)` + `MapTracer.Trace` | Redirects trace output to a file |
| Logging framework integration | `MapTracer.LogCallback` | Integrates with `ILogger` / Serilog / NLog |
| Analytical Trace | `MapTracer.Trace<T,S>(src, opts)` | Returns trace report *without* performing mapping |
| Exception types | `MapException` / `MapConfigurationException` | Runtime diagnostics |
| Exception diagnostic properties | `ex.SourceType` / `ex.TargetType` / `ex.PropertyName` | Locate failure point inside `catch` blocks |
| Mapping relationship report | `<NCMapperGenerateReport>true</NCMapperGenerateReport>` | Generated at compile time as Markdown |

---


## Companion Examples

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 602
```

Core example source code (single-point, same-source; runnable via `dotnet run --scenario` for verification):

[!code-csharp[Full validation](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios)]

[!code-csharp[Full validation · ValidateAll](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAll)]

[!code-csharp[Full validation · ValidateMapping](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateMapping)]

[!code-csharp[Full validation · ValidateOrThrowDemo](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateOrThrowDemo)]

[!code-csharp[Full validation · ValidateAssembly](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ValidateAssembly)]

[!code-csharp[Full validation · MapNullSource](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_MapNullSource)]

[!code-csharp[Full validation · TraceMapping](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceMapping)]

[!code-csharp[Full validation · TraceToWriter](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceToWriter)]

[!code-csharp[Full validation · TraceLogCallback](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceLogCallback)]

[!code-csharp[Full validation · TraceOptionsFactory](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_TraceOptionsFactory)]

[!code-csharp[Full validation · ToggleValidationSwitch](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_ToggleValidationSwitch)]

[!code-csharp[Full validation · PrintReportToWriters](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_PrintReportToWriters)]

[!code-csharp[Full validation · DiagnoseError](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_DiagnoseError)]

[!code-csharp[Full validation · Run](~/samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs#Validation_ValidationScenarios_Run)]

Source code resides in `samples/NCMapper.Samples/06-Infrastructure/ValidationScenarios.cs`, covering sub-scenarios such as `Validate`, `ValidateMapping`, `ValidateOrThrow`, `TraceToWriter`, `TraceLogCallback`, and more.

---


## Further Reading

- Related topics: [DI Integration](di.md) · [Pipeline](pipeline.md) · [Complete Diagnostic Code Index](../../diagnostics/rules.md)
- In-depth technical documentation: [Mapping Validation](../../explanation/validation.md) §12
- Validation source code: `src/NCMapper/MapValidator.cs` · `src/NCMapper/MapTracer.cs`
