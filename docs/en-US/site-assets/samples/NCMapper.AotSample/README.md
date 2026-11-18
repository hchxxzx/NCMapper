# NCMapper AotSample — NativeAOT Deployment Example

A **minimal NativeAOT deployment example** for NCMapper: demonstrates how mapping flows entirely through the source generator path (i.e., all code is generated at compile time with zero reflection) in an AOT publishing environment. The resulting executable can be published directly as NativeAOT and run successfully.

This sample deliberately complements [NCMapper.Samples](../NCMapper.Samples/README.md):  
- *Samples* is a full-featured, interactive demonstration (covering ORM, DI, reflection, and all other capabilities), and thus **is not suitable** for AOT publishing.  
- This *AotSample*, by contrast, retains only the minimal AOT-compatible surface area, serving as a reference baseline for production AOT deployments.

## Running the Sample

```bash
# Run normally (smoke test; process exits with code 0 after all five entry points pass assertions)
cd samples/NCMapper.AotSample
dotnet run

# Publish as NativeAOT (requires Windows SDK + MSVC; see NativeAOT prerequisites)
dotnet publish -c Release -r win-x64 --self-contained

# Run the published output directly
./bin/Release/net10.0/win-x64/publish/NCMapper.AotSample.exe
```

`PublishAot=true` is already set in the project file, so NativeAOT compilation (including trimming and AOT analysis) is automatically enabled upon publishing.

## Smoke Test Entry Points

The program executes five core entry points using only AOT-safe usage patterns. Any failure throws an exception and exits with a non-zero exit code (for CI smoke testing):

| Entry Point | Validation Target | Description |
|-------------|-------------------|-------------|
| `Mapper.Map` | Single-object mapping (Entity → DTO) | DTO is annotated with `[MapFrom]`; mapping uses source-generated delegate at runtime |
| `Mapper.MapList` | Collection mapping | Uses source generator’s batch path — zero per-element delegates |
| `Mapper.DeepCopy` | Deep copy for object isolation | Uses source-generated `DeepCopy`, AOT-safe |
| `Mapper.Validate` | Compile-time validation result consumption | Parameterless overload consumes source-generated validation result — zero reflection |
| `Mapper.Compose` | Multi-source composition (e.g., `OrderEntity` + `CustomerEntity` → `OrderDetailDto`) | Uses source-generated `RegisterGeneratedCompose` delegate |

## Three Principles of AOT Deployment

1. **Annotate all DTOs with `[MapFrom]`**: Ensures mappings flow exclusively through the source generator path (all code generated at compile time) — this is the foundational requirement for AOT compatibility.  
2. **Avoid unannotated fallback paths**: Type pairs without `[MapFrom]` annotations fall back to runtime reflection or expression tree compilation — which is unsupported under NativeAOT (and triggers IL warnings at compile time).  
3. **ORM extension considerations**: Projection paths (e.g., `AsDto()` → `ToList()`) are **not supported** under AOT (no source generator alternative exists); however, in-memory mapping fallback paths become conditionally usable *if* the target DTO is annotated with `[MapFrom]`.

## Model Definitions

Models are embedded directly in `Program.cs` (self-contained design, zero shared dependencies):

| Type | Role | Description |
|------|------|-------------|
| `UserEntity` / `UserDto` | Demonstrates `Map`, `MapList`, and `DeepCopy` | `UserDto` is annotated with `[MapFrom(typeof(UserEntity))]` |
| `OrderEntity` / `CustomerEntity` / `OrderDetailDto` | Demonstrates `Compose` | `OrderDetailDto` is annotated with `[MapFrom]` and three `[MapProperty]` attributes declaring multi-source origins |

## Related Documentation

- AOT / NativeAOT capability boundaries and limitations: [Limitations and Considerations §18.4](../../docs/en-US/explanation/limitations.md)  
- Complete NativeAOT deployment guide: [AOT / NativeAOT Deployment](../../docs/en-US/how-to/aot/deployment.md)  
- Full-featured interactive demo: [NCMapper.Samples](../NCMapper.Samples/README.md)
