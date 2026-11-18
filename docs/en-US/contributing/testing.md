> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §19 Test Infrastructure -->

## 19. Test Infrastructure

NCMapper employs a layered, multi-TFM test strategy. The full test suite—including parameterized test cases across three TFMs (net8.0, net9.0, and net10.0)—covers the core library, source generators, ORM integrations, fuzz testing, and AOT compilation:

| Test Project                      | Topic              | Description                                                                                                                                                                                                 |
| --------------------------------- | ------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `NCMapper.Tests`                  | Core Library Unit Tests | Deep copy / collections / composition / pipelines / validation / data masking / type conversion / basic mapping / enums / nullable handling / DI configuration / expression trees / edge-case exceptions, including Stryker mutation testing (100% mutation score) |
| `NCMapper.SourceGen.Tests`        | Source Generator Tests   | Correctness of generated code, diagnostic code triggering (NCMAP001–034 [004 omitted] / 099), mask generation, circular reference handling, flattening depth, regression coverage                                                                                     |
| `NCMapper.IntegrationTests`       | ORM Integration Tests    | Projections with EF Core / FreeSql / Dapper; fallback behavior for Mask+Projection and Convert+Projection                                                                                                                                                         |
| `NCMapper.FuzzTests`              | Fuzz Testing             | Property-level randomized input                                                                                                                                                                                                                                     |
| `NCMapper.AotTests`               | AOT Compilation Compatibility | NativeAOT deployment verification                                                                                                                                                                                                                                    |

Mutation testing (Stryker) measures the test suite’s ability to detect intentional changes (mutations) in source code. It is a critical defense against the “green test but broken logic” scenario.

### 19.1 Dual-Channel Equivalence Tests

NCMapper supports two parallel mapping configuration channels: the `[MapFrom]` attribute-based channel and the Fluent API channel (`[MapperConfig]` + `Configure(FluentMappingConfig)`). Both channels produce concrete `MappingSnapshot` instances. The **equivalence test mechanism** ensures structural identity between outputs from both channels: if a change to the attribute-channel materialization logic is not mirrored in the Fluent-channel logic, equivalence tests will fail during CI.

**When equivalence pairs are mandatory**: When a PR modifies any of the following *materialization surface* files (prefix-matched) and the change may semantically affect any `MappingSnapshot` field in either channel, you must add or update an equivalence fixture under `test/NCMapper.SourceGen.Tests/Equivalence/`. The CI `equivalence-gate` job enforces this check.

- `src/NCMapper.SourceGen/MappingExtractor*.cs`
- `src/NCMapper.SourceGen/FluentExtractor*.cs`
- `src/NCMapper.SourceGen/ChainCallParser.cs`
- `src/NCMapper.SourceGen/Models.Snapshots.cs`
- `src/NCMapper.SourceGen/MappingSnapshotBuilder*.cs`

**How to write an equivalence fixture** (see existing 8 fixtures in `Equivalence/EquivalenceTests.*.cs`):

1. **Dual-source convention**: Express the same mapping semantics in both channels — annotated S/T classes for the attribute channel; clean S/T classes plus a separate `[MapperConfig]` class for the Fluent channel. Class names, namespaces, and property sets must be identical across both sides.
2. **Two-layer assertion**: Inherit from `EquivalenceFixtureBase` and call `AssertEquivalent(attrSource, fluentSource, pairId, mapMethodName)`:
   - L1 (field-level): Uses `SnapshotEquivalenceComparer` for normalized comparison; passes only if the diff list is empty.
   - L2 (text-level): Compares the generated `Map_{S}_To_{T}` method body byte-for-byte. If L2 is inapplicable (e.g., due to differing nested recursive call patterns), downgrade to `AssertL1Only` and document the reason.
3. **Tagging and registration**: Annotate the test method with `[EqPair("A-xx")]` and `[Trait("EqPair", "A-xx")]` (enabling single-pair execution via `--filter-trait "EqPair=A-xx"`), and register entry `A-xx` in `EquivalenceRegistry`. `EquivalenceRegistryIntegrityTests` validates bidirectional completeness of registration (1/2 checks).
4. **Capability classification**: Only capabilities supported by *both* channels qualify for equivalence assertions (Class A, currently 8 items). Channel-exclusive features (e.g., `AfterMapping`, `NoAutoInclude`) are excluded from equivalence assertions but *must* be registered in the registry to declare per-channel support.

**Escape hatch**: For materialization-surface changes confirmed to be non-breaking (e.g., purely cosmetic diagnostic message edits), apply the `skip-equivalence-gate` label to bypass the gate (job-level skip). **You must explicitly state the justification in the PR description** for reviewer confirmation.

**Local verification**: Run `powershell -File scripts/verify-equivalence-gate.ps1 -BaseRef origin/main` (same parameters as CI).

### 19.2 Snapshot Field Registry Maintenance

The `SnapshotFieldRegistry` (`test/NCMapper.SourceGen.Tests/Equivalence/SnapshotFieldRegistry.cs`) serves as the single source of truth for fields excluded from equivalence comparison—replacing scattered `TopLevelDenyList` string arrays, hard-coded `Get*Coverage` arrays, and inline `STRIPPED` comments inside `Compare*` methods.

**Three-column registration rules** (`FieldRegistration` record):

| Column                | Meaning                                                                                           |
| --------------------- | ------------------------------------------------------------------------------------------------- |
| `Field`               | Field name (exactly matches `PropertyInfo.Name` from reflection)                              |
| `Role`                | `Compared` (included in comparison) / `Stripped` (excluded from comparison)                     |
| `Reason`              | Justification for inclusion/exclusion (symbolic reference only; no line numbers)              |
| `MigrationCondition`  | “When can this become comparable?” condition for `Stripped` entries (empty values must explicitly state `"permanent"` or `"none yet"`) |

**Registration scope**:
- Top-level `MappingSnapshot`: Only 4 fields are registered as `Stripped` (`TargetIdentifierLocation`, `NoAutoInclude`, `AfterMappingMethodRef`, `IsFluentForward`). All other fields are implicitly `Compared` = reflection full set − `Stripped` (no manual enumeration required at runtime).
- Six hand-written nested projection types (`PropertyRef`, `PropertyMappingSnapshot`, `DiagnosticSnapshot`, `DeepCopyNodeShape`, `DeepCopyPropertyShape`, `ComposeMappingSnapshot`) require explicit per-field registration (with `Compared` fields matching those actually compared in `Compare*` methods, and `Stripped` fields grouped per inline comment).
- ConverterBack quartet (`HasConvertBack`, etc., 4 fields): Any `Stripped` entry must include a gap statement (e.g., `E-1: migration condition unmet`).

**Frozen assertion consumption** (`SnapshotFieldRegistryTests`):
- `AllTypes_ComparedUnionStripped_EqualsReflectionFullSet`: For each type, `Compared ∪ Stripped = full reflection set` — newly added fields without registration cause test failure.
- `AllStrippedEntries_HaveNonEmptyReasonAndMigrationCondition`: Enforces completeness of all three columns.
- `ContainerElementTypes_AreSubsetOfRegistryTypes`: Structural type-level assertion — container element type set ⊆ registry type set (bidirectionally closed).

**Workflow for adding new fields or nested types**:
1. After adding a field in `Models.Snapshots.cs`, immediately register it in the corresponding type’s `IReadOnlyList<FieldRegistration>` within `SnapshotFieldRegistry` (as `Compared` or `Stripped`, with reason and migration condition).
2. Adding a new container element type (e.g., `T` in `List<T>`) requires registering that type in the registry; otherwise `ContainerElementTypes_AreSubsetOfRegistryTypes` fails.
3. Run `SnapshotFieldRegistryTests` to full green before merging — confirms registration completeness.

**Mutation validation**: Mutations at the registry or structural layer (e.g., deleting a field, clearing a `Reason`, omitting a nested type) must cause the corresponding frozen assertions to fail — predicted failure set ≡ observed failure set (no false greens; reverse-validated).
