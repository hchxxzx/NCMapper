> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: how-to/aot/deployment
Diagnostic code involved: NCMAP019  
Library version: v1.0.0+  
Last updated: 2026-09-15  
---

# AOT / NativeAOT Deployment

## Overview

NCMapper adopts a **dual-engine architecture** combining *source generators* and *runtime expression trees*. The source generator path **generates all mapping code at compile time** (zero reflection), enabling full deployment as a NativeAOT executable—this is the key distinction from libraries like AutoMapper, which rely on runtime reflection.

**Package-level compatibility declaration**: All four libraries (NCMapper, EF Core, FreeSql, and Dapper) declare both `<IsAotCompatible>true</IsAotCompatible>` and `<IsTrimmable>true</IsTrimmable>` in their project files. This prevents false positives during trim/AOT analysis due to missing annotations.

**Runnable reference**: `samples/NCMapper.AotSample` is the minimal AOT deployment example (`PublishAot=true`, targeting `net10.0`). All DTOs are annotated with `[MapFrom]`, routing mappings through the source generator path. After smoke-testing the five entry points—`Map`, `MapList`, `DeepCopy`, `Validate`, and `Compose`—the application can be fully published and run.

## Quick Start

```bash
cd samples/NCMapper.AotSample
dotnet run                                  # Smoke test (five-entry assertions)
dotnet publish -c Release -r win-x64 --self-contained   # NativeAOT publish
./bin/Release/net10.0/win-x64/publish/NCMapper.AotSample.exe
```

Core sample source (single-source-of-truth; runs and validates via `dotnet run`):

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose)](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · Program_Main](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_Program_Main)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · Program_Assert](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_Program_Assert)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · UserEntity](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserEntity)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · UserEntity_Id](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserEntity_Id)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · UserEntity_Name](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserEntity_Name)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · UserEntity_Email](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserEntity_Email)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · UserDto](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserDto)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · UserDto_Id](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserDto_Id)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · UserDto_Name](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserDto_Name)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · UserDto_Email](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_UserDto_Email)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · OrderEntity](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderEntity)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · OrderEntity_OrderId](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderEntity_OrderId)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · OrderEntity_Amount](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderEntity_Amount)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · CustomerEntity](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_CustomerEntity)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · CustomerEntity_Name](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_CustomerEntity_Name)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · OrderDetailDto](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderDetailDto)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · OrderDetailDto_OrderId](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderDetailDto_OrderId)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · OrderDetailDto_Amount](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderDetailDto_Amount)]

[!code-csharp[AOT Entry Validation (Map/MapList/DeepCopy/Validate/Compose) · OrderDetailDto_CustomerName](~/samples/NCMapper.AotSample/Program.cs#AotSample_EntryValidation_OrderDetailDto_CustomerName)]

Publication requires meeting NativeAOT prerequisites (e.g., Windows SDK + MSVC or Linux + clang). See the official .NET NativeAOT documentation for details.

## Three Principles of AOT Deployment

1. **Annotate all DTOs with `[MapFrom]`**: Ensures mappings use the source generator path (full code generation at compile time)—this is the foundation of AOT compatibility.
2. **Avoid unannotated fallback paths**: Unannotated type pairs fall back to runtime reflection/expression-tree compilation, which is unavailable under NativeAOT (IL warnings issued at compile time).
3. **ORM extension considerations**: Projection paths (e.g., `AsDto` → `ToList`) are unavailable under AOT (no source-generator alternative exists); however, *in-memory fallback paths* become conditionally usable when types are annotated with `[MapFrom]`. **Fluent DTOs are similarly conditionally usable**: Fluent type pairs are carried by the `RuntimeSnapshot` registry (`NCMapperEmitRuntimeSnapshot` is enabled by default); registry hits bypass reflection-based attribute reading. When disabled, fallback to reflection occurs—and fails under AOT.

## API Capability Matrix

| API | AOT Availability | Notes |
|-----|------------------|-------|
| `Mapper.Map` / `MapList` | ✅ Available | DTOs annotated with `[MapFrom]` use source-generated delegates. |
| `Mapper.Map` (Fluent DTO) | ✅ Available | Fluent type pairs are carried by the `RuntimeSnapshot` registry (snapshot enabled by default; disabled → reflection fallback → not AOT-compatible). |
| `Mapper.DeepCopy` | ✅ Available (source-generated path only) | Runtime expression-tree provider path is unavailable (marked with `[RequiresDynamicCode]`). |
| `Mapper.Validate()` (parameterless) | ✅ Available | Uses compile-time validation results from the source generator—zero reflection. `[Experimental("NCMapper001")]` indicates API stability warning—not an AOT restriction. |
| `Mapper.Compose` | ✅ Conditionally available | Available only when `RegisterGeneratedCompose` delegate is registered by the source generator; fallback to runtime `MakeGenericType` path is unavailable. |
| `Mapper.Validate(Assembly[])` | ❌ Unavailable | Relies on reflection-based assembly scanning, marked with `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]`. |
| ORM projections (`AsDto` → `ToList`/`ToChunkDto`, etc.) | ❌ Unavailable | `BuildProjection` uses `MakeGenericMethod` to construct SQL projection expression trees—no source-generator alternative exists (`RegisterGenerated*` APIs cover seven scenarios but exclude Projection). |
| ORM in-memory fallback (mapping) | ✅ Conditionally available | Annotated types (`[MapFrom]`) use source-generated delegates; depends on the ORM’s own AOT compatibility. |
| ORM in-memory fallback (Fluent DTO) | ✅ Conditionally available | Carried by `RuntimeSnapshot` registry (same as above)—purely declarative projection/fallback logic reads registry details; disabled state conservatively falls back to reflection. |
| Dapper `Query` delegates | ❌ Unavailable | Delegate compilation uses `Expression.Compile()`; mapping portions annotated with `[MapFrom]` *are* AOT-safe (source-generated). |

> Full boundaries are documented in [Limitations and Considerations §18.4](../../explanation/limitations.md).

## Runtime Fallback Behavior

Unannotated mapping pairs silently fall back to runtime reflection/expression-tree paths during *regular execution*. Under AOT publishing, however, those paths are trimmed or non-executable, triggering `IL2026` / `IL3050` warnings at compile time. The source generator applies precise `#pragma warning disable IL2026, IL3050` directives—including explanatory comments—at all seven known safe fallback call sites (Compose pipeline list downgrade, nested mapping fallback, init-only fallback, parameterless constructor fallback, collection element fallback, `DeepCopy` cycle-reference fallback, and `DeepCopy` compilation-failure fallback). As a result, `AotSample` builds with zero warnings.

## Trimming and Safeguards

- **Trimming declaration**: All four libraries declare both `<IsTrimmable>true</IsTrimmable>` and `<IsAotCompatible>true</IsAotCompatible>`—a complete, unambiguous package-level compatibility declaration.
- **AOT reflection guard**: `tools/AotReflectionGuard` scans the public method bodies (IL) of the three ORM extension packages during CI builds using dual channels—`MetadataLoadContext` and `PEReader`. If it detects *reflection calls without corresponding `Requires*` attributes*, the CI build fails—ensuring AOT reflection constraints (24 items) never break silently.
- **Test safeguards**: `test/NCMapper.AotTests` (validates AOT compilation compatibility, included in the same solution) plus the CI `samples` job’s `AotSample` build (`--warnaserror`, zero-warning gate) and smoke-run verification provide dual protection.

## Diagnostic Rules

| Diagnostic | Severity | Trigger Condition |
|------------|----------|-------------------|
| `IL2026` / `IL3050` | Warning | Mapping pair lacks `[MapFrom]`, falling back to runtime path—or an AOT-incompatible API is invoked (exposed at compile time during AOT publishing). |
| NCMAP019 | Info | Suggests adding `[MapFrom]` when a DTO follows naming convention (type name ends in `Dto` or `DTO`) but lacks the annotation—under AOT/trimmed scenarios, runtime mapping will fall back to reflection. |

## Related Resources

- Runnable example: [samples/NCMapper.AotSample](~/samples/NCMapper.AotSample/README.md)  
- Full-featured demo (non-AOT surface): [samples/NCMapper.Samples](~/samples/NCMapper.Samples/README.md)  
- [Limitations and Considerations §18.4](../../explanation/limitations.md)  
- Test project: `test/NCMapper.AotTests`
