> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §10 Multi-Source Composition (Compose) -->

## 10. Multi-Source Composition (Compose)

> ⚠️ **Experimental API** (Note): `Mapper.Compose` / `ComposeList` and `IMapper.Compose` are annotated with `[Experimental("NCMapper001")]`. The first call triggers compiler warning NCMapper001; this code identifies experimental APIs and differs from the source generator diagnostic code NCMAP001—please distinguish them carefully.

When a DTO’s fields are scattered across multiple entities (e.g., `UserDto` requires basic user info from `UserEntity`, department name from `DepartmentEntity`, and company name from `CompanyEntity`), the traditional approach is to query each entity separately and manually assemble the result. With `Compose`, you declare the source entity for each field using `[MapProperty(typeof(X), "field")]`, enabling a single call to synthesize the target DTO from multiple source objects—including support for batch composition and callbacks.

> Both the field-name string (`"field"`) and the two-parameter form (`typeof + nameof`) are **valid syntaxes**; `nameof` is recommended for compile-time validation. The `fromField` parameter supports dotted paths such as `xx.xx` (multi-table dotted paths are parsed segment-by-segment by the source generator); failures emit diagnostic NCMAP005 including the failing segment name—see [§5.2](../reference/features.md) / [§18.3](limitations.md) for details.

### 10.1 Basic Usage

```csharp
// DTO annotation: UserDto's primary source is UserEntity; other fields come from different entities
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }                                   // UserEntity
    public string Name { get; set; }                              // UserEntity
    [MapProperty(typeof(DepartmentEntity), "DeptName")]
    public string Department { get; set; }                        // DepartmentEntity
    [MapProperty(typeof(CompanyEntity), "CompanyName")]
    public string Company { get; set; }                           // CompanyEntity
}
```

Four runnable composition-call variants (demonstrating the same pattern across three sources: Order/Customer/Product):

[!code-csharp[Composition Call (Three Sources)](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose_MultiSourceCompose)]
[!code-csharp[Composition with Action Callback](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose_ComposeWithAction)]
[!code-csharp[Batch Composition](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_ComposeList)]
[!code-csharp[Batch Composition + Callback](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_ComposeListWithAction)]

### 10.2 Rules

**Source Processing Order (Rule 2):**

1. **Primary source located by type**: The type declared via `[MapFrom(typeof(X))]` is the primary source — at runtime, it is located within the input array *by type* (**position is unrestricted; it need not be the first element**). It is always processed first and is responsible for creating the DTO instance.
2. **Secondary sources follow array order**: Non-primary sources are processed in the exact order they appear in the `sources` array, sequentially updating the DTO.
3. **Later sources override earlier ones**: If both the primary and a non-primary source map to the same DTO field (the latter via `[MapProperty]`), the non-primary source’s value overwrites the primary source’s value.

Additional rules:

- Non-primary sources only map fields explicitly annotated with `[MapProperty(typeof(ThatSourceType), ...)]`; the primary source automatically maps all fields with matching names.
- If the primary source is missing, an exception (`MapConfigurationException`) is thrown, with a message clearly identifying which primary-source type is absent.
- A `null` source is skipped silently and does not affect the processing order of other sources.
- **Duplicate primary sources**: If multiple sources in the array are annotated as primary sources for the same type (i.e., duplicate `[MapFrom]` declarations), `ComposeRuntime` performs two passes — the *first* occurrence creates the DTO, while subsequent duplicates trigger `Update` (overwrite-style updates) on the already-created DTO. Previously, the source generator silently ignored duplicate primary sources; this behavior has now been aligned to match the runtime semantics.

[!code-csharp[Multi-source composition: Primary source may appear anywhere but is always processed first](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose)]
