> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
title: Configuration and Extensibility
---

<!-- Source: NCMapper Main Technical Documentation §17 Configuration and Extensibility (structural reorganization, chapter-level migration on 2026-08-19; content order unchanged) -->

## 17. Configuration and Extensibility

NCMapper’s default behavior is fully replaceable on demand: the JSON serialization strategy defaults to `System.Text.Json`, but can be swapped for `Newtonsoft.Json`; the deep-copy strategy defaults to `FastDeepCopyProvider`, but can be customized; runtime toggles control validation, trace logging, and performance metrics. Extension points such as `JsonSerializerBase.Current`, `DeepCopyProviderBase.Current`, and `NCMapperOptions` enable seamless adaptation of the library to diverse project technology stacks and requirements.

### 17.1 JSON Serialization Strategy

```csharp
// Uses System.Text.Json by default
// Replace with Newtonsoft.Json
public class NewtonsoftJsonSerializer : JsonSerializerBase
{
    public override string Serialize(object? obj) => JsonConvert.SerializeObject(obj);
    public override object? Deserialize(string json, Type type) => JsonConvert.DeserializeObject(json, type);
}
JsonSerializerBase.Current = new NewtonsoftJsonSerializer();
```

### 17.2 Deep-Copy Strategy

The default `FastDeepCopyProvider` uses a three-value classification dispatch: an acyclic zero-detection fast path (~401 ns) and a fallback to full-cycle detection in the base class for cyclic or unknown cases (arbitrary cycle length). It may be replaced with `ExpressionTreeDeepCopyProvider` (with full circular-reference detection + DAG shared-reference preservation, ~409 ns) or a custom strategy (all benchmarks measured on net10.0 Linux). See [Deep-Copy Architecture §8.2](../explanation/deep-copy.md) for details.

> ⚠ **DAG Shared References**: In the fast path for acyclic types, DAG shared references are *independently copied at each occurrence*.

### 17.3 Source Generator Controls

| Switch                           | Default | Description              |
| -------------------------------- | :-----: | ------------------------ |
| `ValidationEnabled`            |  `true` | Enables runtime validation |
| `TraceEnabled`                 | `false` | Enables mapping trace logs |
| `MetricsEnabled`               | `false` | Enables performance metrics collection |
| `<NCMapperSourceGenEnabled>`   |  `true` | Enables compile-time diagnostics |
| `<NCMapperEmitGeneratedFiles>` | `false` | Emits source-generated files to disk (`obj/NCMapper/Generated`) at build time (disabled by default) |

### 17.4 `NCMapperOptions` Configuration Modes

`NCMapperOptions` supports both programmatic configuration (via callback) and declarative configuration via `appsettings.json`. Supported options:

| Option                | Type       | Default | Bindable from JSON | Description                                                                 |
| --------------------- | ------------ | ------- | ------------------ | --------------------------------------------------------------------------- |
| `ValidationEnabled`   | `bool`       | `true`  | Yes                | Enables mapping validation                                                  |
| `TraceEnabled`        | `bool`       | `false` | Yes                | Enables mapping diagnostic tracing                                            |
| `MaxRecursionDepth`   | `int`        | `64`    | Yes                | Maximum recursion depth during mapping; exceeding it triggers cycle detection |
| `MaxInlineDepth`      | `int`        | `5`     | Yes                | Maximum inline depth when building expression trees                         |
| `DeepCopyStrategy`    | `enum`       | `Fast`  | Yes                | Deep-copy strategy: `Fast`, `ExpressionTree`, or `Json`                    |
| `TraceOutput`         | `TextWriter` | `null` (i.e., `Console.Out`) | No | Trace output destination — `TextWriter` cannot be bound from JSON; must be set programmatically |

The `DeepCopyStrategy` enum maps to three built-in `DeepCopyProviderBase` implementations, assigned to `DeepCopyProviderBase.Current`:  
- `Fast` → `FastDeepCopyProvider` (default; three-value classification dispatch: acyclic zero-detection fast path / full-cycle detection for cyclic or unknown cases),  
- `ExpressionTree` → `ExpressionTreeDeepCopyProvider` (full circular-reference detection + DAG shared-reference preservation),  
- `Json` → `JsonDeepCopyProvider` (JSON round-trip fallback, ~1697 ns; does not support circular references).

```json
// appsettings.json
{
  "NCMapper": {
    "ValidationEnabled": false,
    "TraceEnabled": true,
    "MaxRecursionDepth": 128,
    "MaxInlineDepth": 5,
    "DeepCopyStrategy": "ExpressionTree"
  }
}
```

Two executable registration patterns (Pattern 1 = direct section binding via `AddNCMapper(IConfiguration)` overload; Pattern 2 = Options callback, required for non-JSON-bindable properties like `TraceOutput`):

[!code-csharp[Pattern 1 (Recommended): Direct configuration-section binding](~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs#DI_ConfigSectionBind)]
[!code-csharp[Pattern 2: Code-based registration (Options delegate)](~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs#DI_ResolveMapperWithOptions)]

> ℹ️ **JSON Serialization Strategy Is Code-Only Configurable**: The only built-in `JsonSerializerBase` implementation is `SystemTextJsonSerializer`. Custom serializers (e.g., `Newtonsoft.Json`) require users to inherit `JsonSerializerBase` and assign an instance via `JsonSerializerBase.Current = new MySerializer()` in code. This setting is *not part of `NCMapperOptions`*, nor is it suitable for activation via configuration strings (which would require reflection-based instantiation of user-defined types — incompatible with the library’s AOT-oriented design).

### 17.5 Global Mapping Configuration (`[assembly: MapDefaults]`)

> A single declaration applies globally to **all** mappings: naming strategy, prefix/suffix stripping, and ignore lists (by name, by type, by prefix/suffix), plus ToEntity-direction-only exclusions. Eliminates repetitive per-property annotations — for existing projects, one global configuration block suffices to uniformly standardize naming conventions and ignore rules.

```csharp
// Assembly-level attribute (in AssemblyInfo.cs or any file after `using` statements and before `namespace`)
[assembly: MapDefaults(
    NamingStrategy = NamingStrategy.SnakeCase,          // Naming strategy (see below)
    Prefixes       = new[] { "Tbl" },                   // Strips prefixes from source property names
    Postfixes      = new[] { "Entity" },                // Strips suffixes from source property names
    IgnoreNames    = new[] { "RowVersion", "CreatedAt" }, // Bidirectional ignore: exact name match
    IgnoreTypes    = new[] { typeof(byte[]) },           // Bidirectional ignore: exact type match
    IgnoreNamePrefixes = new[] { "Internal" },          // Bidirectional ignore: name prefix match
    IgnoreNameSuffixes = new[] { "Stamp" },             // Bidirectional ignore: name suffix match
    IgnoreNamesToEntity = new[] { "ComputedField" })    // ToEntity-direction-only ignore
```

**Naming Strategy Enum** (`NamingStrategy`, applied to *character-level comparison of single-segment property names*; all four strategies remain orthogonal to automatic flattening):

| Enum Value         | Matching Rule                                      | Example                     |
| ------------------ | -------------------------------------------------- | --------------------------- |
| `Default` (default) | Case-insensitive exact name match (current behavior, zero breaking change) | `username` ↔ `UserName`     |
| `CaseSensitive`    | Case-sensitive exact match (disables case-insensitivity) | `UserName` ↔ `UserName`; `username` does *not* match |
| `SnakeCase`        | Bidirectional normalization: `snake_case` ↔ `PascalCase` | `user_name` ↔ `UserName`    |
| `UpperSnakeCase`   | Bidirectional normalization: `UPPER_SNAKE_CASE` ↔ `PascalCase` | `USER_NAME` ↔ `UserName`    |

**Full Parameter Reference** (11 total parameters, grouped by scope; each list capped at 8 items; overflow degrades to “unspecified” + warning NCMAP020):

| Group              | Parameter                      | Matching Rule                                                                 |
| ------------------ | -------------------------------- | ----------------------------------------------------------------------------- |
| Naming             | `NamingStrategy`                 | Global naming strategy (`Default` by default; `SnakeCase`/`UpperSnakeCase` normalize to “remove underscores + uppercase first letter”, yielding broader match coverage than pure snake↔pascal) |
| Source-side Stripping | `Prefixes` / `Postfixes`         | Strips prefixes/suffixes from source property names *before* matching target names (`OrdinalIgnoreCase`; stripped name must be non-empty; among variants, the *first-declared* wins on key collision) |
| Bidirectional Ignore | `IgnoreNames`                    | Property name matched *exactly* using `OrdinalIgnoreCase` (case-insensitive) |
| Bidirectional Ignore | `IgnoreTypes`                    | Property type matched *exactly* by `Type.FullName` (`Ordinal`; open generics do *not* match) |
| Bidirectional Ignore | `IgnoreNamePrefixes` / `IgnoreNameSuffixes` | Property name prefix/suffix matched using `OrdinalIgnoreCase` (differs from stripping: match causes immediate skip, no further matching attempted) |
| ToEntity-Only Ignore | `IgnoreNamesToEntity` / `IgnoreTypesToEntity` / `IgnoreNamePrefixesToEntity` / `IgnoreNameSuffixesToEntity` | Applies *only* in ToEntity direction; unioned with bidirectional ignore lists |

**Directional Semantics**:  
- ToEntity-direction ignore = (bidirectional list hit) ∨ (ToEntity-only list hit);  
- ToDto-direction ignore = *only* bidirectional list hits (no ToDto-specific lists exist).  
**Activation Timing**: In the source-generation pipeline, `[MapFrom]`-annotated types currently produce *hardcoded* ToDto-direction mappings (`isToDto=true`). Thus, `*ToEntity` lists are *not consulted* there — they serve **runtime reverse mapping**, where target types *without* `[MapFrom]` are assumed ToEntity-direction and resolved dynamically by `PropertyIgnoreChecker`.

**Three-Tier Priority Chain** (non-null values override lower tiers, fine-grained > coarse-grained):

1. **Property-level attributes** >  
2. **`[assembly: MapDefaults]`** >  
3. **MSBuild properties** (e.g., `<NCMapperNamingStrategy>`, passed via whitelisted channel in `buildTransitive/NCMapper.targets`) >  
   Built-in defaults (`Default` + empty lists).

**MSBuild Channel** (applies only to compilation units participating in source generation; list items delimited by comma `,`, *not* semicolon):

```xml
<PropertyGroup>
  <NCMapperNamingStrategy>SnakeCase</NCMapperNamingStrategy>
  <NCMapperIgnoreNames>RowVersion,CreatedAt</NCMapperIgnoreNames>
</PropertyGroup>
```

**Two-Phase Freeze Semantics**: `MapDefaults` configuration is captured as a snapshot by the source generator at compile time and emitted as `Mapper.RegisterGeneratedConfig` registration code. The first mapping or validation operation triggers **freezing** — after which the configuration becomes immutable (no modification API exists). When multiple assemblies declare conflicting configurations, the first freeze throws `MapConfigurationException`, listing all conflicting assembly names (fail-fast). Identical configurations are merged idempotently.

**Seven Semantic Boundaries** (implementation contracts ensuring documentation and behavior align):

| # | Semantic Boundary                              | Description                                                                                                                                 |
|---|-----------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------|
| 1 | Prefix/suffix stripping applies **only to source side** | `Prefixes`/`Postfixes` strip *source* property names before matching target names; *no reverse stripping* is performed on targets (v1.1 G2) |
| 2 | `[MapProperty].FromField` always uses `OrdinalIgnoreCase` literal matching | Explicit annotations are *not* affected by naming strategy: under `SnakeCase`, `FromField = "user_name"` still matches literally as `"user_name"` (v1.1 G3) |
| 3 | `IgnoreTypes` requires exact match              | Matches `Type.FullName` exactly (`Ordinal`); open generics (including backtick arity) *do not match* (v1.1 G4) |
| 4 | MSBuild channel applies only to source-gen units | Runtime fallback paths (assemblies lacking `[MapFrom]`) *do not read* MSBuild properties; `Mapper.Validate()` also triggers freezing (v1.1 G6) |
| 5 | Two-phase freeze + assembly-name reporting      | Conflicting registrations after freeze throw immediately (message includes *both* conflicting assembly names); list-order differences (e.g., `["a","b"]` vs `["b","a"]`) are *not* treated as conflicts (v1.1 W2) |
| 6 | Strip-variant key collision: first-declared wins | Source dictionary is populated in declaration order (original → stripped → composite variants); earlier declarations claim keys, later same-name properties are discarded — strip lists *can alter binding outcomes* (v1.2 Q2) |
| 7 | Exemption is mapping-pair–level               | `OverrideIgnore=true` placed on the **target property** exempts the *entire mapping pair* (both source and target sides bypass all global ignore checks) (v1.2 I2) |

**Interaction with `[MapIgnore]` / `OverrideIgnore`**:  
- Property-level `[MapIgnore]` is explicit and *always* honored — it does *not* participate in exemption logic. If both `[MapIgnore]` and `OverrideIgnore=true` are applied to the same property, it’s a contradiction; the source generator reports error NCMAP022.  
- Global ignore lists follow *fail-closed* semantics: a match means immediate ignore — *unless* the target property carries `OverrideIgnore=true` on an explicit attribute (`[MapProperty]`, `[Convert]`, or `[Mask]`), which then exempts the *entire mapping pair* (NCMAP021 guidance).

**Reading Frozen Configuration**: `Mapper.GetGlobalConfig()` returns an immutable, read-only view `GlobalConfigView` (an immutable `record`; lists are defensive copies; underlying arrays are never exposed), useful for debugging, logging, and test assertions:

[!code-csharp[Reading global config view](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal)]

**Fallback Channel**: In assemblies *without* source-generated code (i.e., no `[MapFrom]` types), `Mapper` falls back to reading `[assembly: MapDefaults]` via reflection. However, the reflection channel *does not read* MSBuild properties (that channel is exclusive to source-gen–participating compilation units).
