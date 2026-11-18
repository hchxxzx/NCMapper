> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
title: Project Structure
---

<!-- Source: NCMapper Main Technical Documentation §3 Project Structure (structural reorganization, chapter-level migration on 2026-08-19; content not reordered) -->

## 3. Project Structure

> Verify against the actual repository file list (as of 2026-09-30). Partial class main files and their sub-files are grouped by indentation.

```
src/
├── NCMapper/                              — Core library (targeting net8.0;net9.0;net10.0, 113 .cs files: top-level 64 + Internal 11 + Runtime 9 + Shared 10 + Strategies 19)
│   ├── Mapper.cs                           — Public entry point + JIT generic caching + strategy dispatch
│   ├── IMapper.cs                          — DI interface
│   ├── NCMapperMapper.cs                   — Default implementation of IMapper (Singleton)
│   ├── NCMapperServiceExtensions.cs        — AddNCMapper() DI extension method
│   ├── MapperBase.cs                       — Core generic class + delegate compilation + static fields
│   ├── MapperBase.ExpressionBuilder.cs     — GetMapBody + inline expression construction
│   ├── MapperBase.Properties.cs            — Property matching (case-insensitive + flattened)
│   ├── ExpressionBuilder.cs                — Expression tree construction helpers (partial main file)
│   │   ├── ExpressionBuilder.Converters.cs — Nested/JSON/conversion expression subpaths
│   │   ├── ExpressionBuilder.Collections.cs — Collection/dictionary expression subpaths
│   │   └── ExpressionBuilder.Paths.cs      — Path (dot-path/flattened) expression subpaths
│   │
│   ├── MapFromAttribute.cs                 — `[MapFrom]` class-level mapping declaration anchor
│   ├── MapperConfigAttribute.cs            — `[MapperConfig]` Fluent-channel configuration class marker
│   ├── NCMapperOptionsAttribute.cs         — `[assembly: NCMapperOptions]` compile-time diagnostic toggle
│   ├── MapPropertyAttribute.cs             — `[MapProperty]` property-level attribute (alias/JSON/FromSource/exemption gate)
│   ├── MapIgnoreAttribute.cs               — `[MapIgnore]` direction-controlled ignore
│   ├── ConvertAttribute.cs                 — `[Convert]` custom value conversion attribute (static method reference)
│   ├── ConvertBackAttribute.cs             — `[ConvertBack]` reverse value conversion attribute (static method reference)
│   ├── MapFormatAttribute.cs               — `[MapFormat]` property-level formatting (IFormattable→string, IProjectionPolicy)
│   ├── MaskAttribute.cs                    — `[Mask]` property-level masking attribute (implements IProjectionPolicy)
│   ├── MapWhenAttribute.cs                 — `[MapWhen]` property-level conditional guard
│   ├── MapNoAutoIncludeAttribute.cs        — `[MapNoAutoInclude]` disables automatic navigation Include (NCMAP017 Info)
│   ├── MapTreeAttribute.cs                 — `[MapTree]` tree-shaped field name customization attribute
│   ├── MapSubclassAttribute.cs             — `[MapSubclass]` class-level polymorphic subtype registration
│   ├── BeforeMappingAttribute.cs           — `[BeforeMapping]` class-level lifecycle pre-hook
│   ├── AfterMappingAttribute.cs            — `[AfterMapping]` class-level lifecycle post-hook
│   ├── MapDefaultsAttribute.cs             — `[assembly: MapDefaults]` global mapping configuration attribute (naming strategy/ignore list)
│   ├── NamingStrategy.cs                   — Naming strategy enumeration and conversion (PascalCase/SnakeCase, etc.)
│   ├── IMask.cs                            — Masking interface (`string Mask(string? value)`)
│   ├── MaskProvider.cs                     — Masking implementation registry (built-in implementations can be overridden at startup)
│   ├── Masks.cs                            — 9 built-in masking implementations (`MaskPhone`, `MaskIdCard`, ...)
│   ├── IProjectionPolicy.cs                — Projection downgrade policy interface (`RequiresDowngrade`)
│   ├── GlobalIgnoreChecker.cs              — Global ignore list checker (fail-closed; `OverrideIgnore` can exempt)
│   ├── PropertyIgnoreChecker.cs            — Extraction of `[MapIgnore]` checking logic
│   │
│   ├── FluentMappingConfig.cs              — Fluent channel: full set of fluent chain terminals (`FluentMappingConfig`/`FluentTypeConfig`)
│   ├── GeneratedMapConfig.cs               — Source generator–produced configuration registration entry point
│   ├── NCMapperOptions.cs                  — Options-pattern configuration class
│   ├── GlobalMapConfig.cs                  — Global mapping configuration model (three-tier priority aggregation)
│   ├── GlobalConfigView.cs                 — Read-only global configuration view (immutable after freezing)
│   │
│   ├── NavigationResolver.cs               — Projection navigation property resolver (Include path resolution)
│   ├── MapValidator.cs                     — Mapping validator + result model
│   ├── MapTracer.cs                        — Mapping diagnostic trace
│   ├── MapException.cs                     — Mapping exception class + `MapConfigurationException` (includes source/destination type constructors)
│   │
│   ├── DeepCopyProviderBase.cs             — Deep copy strategy abstract base class (two-phase + cycle detection)
│   ├── DeepCopyTypeClassifier.cs           — Deep copy three-value classifier (`Acyclic`/`Cyclic`/`Unknown` conditional dispatch)
│   ├── DeepCopyContext.cs                  — Deep copy shared context (caching/cycle detection state)
│   ├── FastDeepCopyProvider.cs             — Fast deep copy (default strategy, three-value classification dispatch: `Acyclic`/`Cyclic`/`Unknown`)
│   ├── ExpressionTreeDeepCopyProvider.cs   — Expression tree deep copy (with circular reference detection)
│   ├── DeepCopyCompiler.cs                 — Deep copy expression tree compiler
│   ├── CollectionCopier.cs                 — Collection copying (full coverage of 16 collection types)
│   ├── CollectionTypeDetector.cs           — Collection type detector
│   │
│   ├── JsonSerializerBase.cs               — JSON serialization strategy base class (replaceable)
│   ├── JsonConverter.cs                    — JSON conversion helper (forwards to strategy)
│   ├── TypeConvertHelper.cs                — Runtime type conversion (public, referenced by source generator)
│   ├── TypeConverter.cs                    — Implicit numeric conversion lookup table
│   ├── TypeHelper.cs                       — Shared property extraction utilities
│   ├── ReflectionHelper.cs                 — Reflection helper methods
│   ├── CacheMetrics.cs                     — Internal cache metrics observation (`internal static`, for performance diagnostics)
│   ├── ProjectionNameResolver.cs           — Projection field name resolver (flattened/tree naming conventions)
│   ├── PropertyMatcher.cs                  — Property matcher
│   ├── MapPipeline.cs                      — Mapping pipeline (PostMap dispatch)
│   ├── Mapper.Pipeline.cs                  — Mapping pipeline implementation (onion model)
│   ├── Mapper.Compose.cs                   — Compose multi-source composition implementation
│   ├── ErrorMessages.cs                    — English and Chinese error message resources
│   ├── PagedList.cs                        — Pagination query result type (shared across FreeSql/EFCore/Dapper extensions)
│   │
│   ├── Shared/                             — Dual-engine shared layer (consumed identically by source generator and runtime)
│   │   ├── assignmentplan.cs               — Assignment plan model (`AssignmentKind` exhaustive set: `Direct`/`Convert`/`Json`/`Mask`/`Format`/`FormatThenMask`/...)
│   │   ├── assignmentplanner.cs            — Assignment plan orchestration
│   │   ├── NameCandidateResolver.cs        — Name candidate resolver (case-insensitive + flattened)
│   │   ├── PathResolver.cs（+.Type.cs）    — Dot-path resolver (symbolic surface / type surface)
│   │   ├── TypeCompatAdapter.cs（+typecompatview.cs） — Type compatibility adapter
│   │   ├── TypeCompatibilityCore.cs        — Core type compatibility determination
│   │   ├── DeepCopyGraphRules.cs           — Deep copy graph rules
│   │   └── MutualMapPairRules.cs           — Mutual reference pair rules (NCMAP025 determination)
│   ├── Runtime/                            — Runtime engine hosting surface (snapshots/channels/anchor gates)
│   │   ├── RuntimeSnapshotStore.cs         — Runtime snapshot store
│   │   ├── RuntimeSnapshotPair.cs          — Directional pair snapshot
│   │   ├── RuntimeMappingChannel.cs        — Mapping channel
│   │   ├── RuntimePropertyMapping.cs       — Property mapping slot (includes `FormatString`/`MaskType`)
│   │   ├── RuntimeTargetPropertySlot.cs    — Target property slot (includes exemption gate state)
│   │   ├── RuntimeAnchorGate.cs            — Anchor gate (no anchor → no consumption)
│   │   ├── RuntimeMapperMetadata.cs        — Mapping metadata
│   │   ├── RuntimeTreeConfig.cs            — Tree configuration
│   │   └── MappingMetaView.cs              — Mapping metadata view
│   ├── Strategies/                         — Runtime assignment strategy set (19 strategies/parsers, including `FormatStrategy`/`FormatThenMaskStrategy`)
│   │   └── IAssignmentStrategy.cs etc.     — `Direct`/`Nested`/`Collection`/`Json`/`Mask`/`Format`/`Convert*`/`CrossTable`/`Skip`/`Ignore`...
│   ├── Internal/                           — ORM projection infrastructure (shared across extension packages)
│   │   ├── AsDtoExecutor.cs                — `AsDto` execution skeleton
│   │   ├── DtoSelectorSkeleton.cs          — Terminal method selector skeleton
│   │   ├── DegradationDetectorBase.cs（+.Agnostic.cs） — Downgrade detection base class (driven by `IProjectionPolicy`)
│   │   ├── NavigationTree.cs（+NavigationTreeCollector.cs） — Navigation tree and collector
│   │   ├── ProjectionExpressionFactory.cs  — Projection expression factory
│   │   ├── ProjectionStrategy.cs           — Projection strategy
│   │   ├── TreeBuilder.cs                  — Tree assembly
│   │   ├── LeafStructGuard.cs              — Leaf structure guard
│   │   └── CacheRegistry.cs                — Registry cache
│
├── NCMapper.SourceGen/                     — Source generator (netstandard2.0, 46 top-level .cs files + Resources)
│   ├── NCMapperSourceGenerator.cs          — `IIncrementalGenerator` entry point
│   ├── SourceGenPipeline.cs                — Generation pipeline orchestration (Prepare → Extract → Arbitrate → Materialize → Generate)
│   ├── GenerationPreparation.cs            — Generation preparation phase
│   ├── GenerationContext.cs                — Shared context per generation
│   ├── GlobalConfigReader.cs               — Reads `[assembly: MapDefaults]` global configuration
│   │
│   ├── MappingExtractor.cs                 — Attribute channel: semantic information extraction from `[MapFrom]` + diagnostic generation (partial main file)
│   │   ├── MappingExtractor.Properties.cs  — Property-level matching/annotation extraction subpath
│   │   ├── MappingExtractor.Materialize.cs — Materialization phase (declaration finalization/diagnostic emission)
│   │   ├── MappingExtractor.Lifecycle.cs   — Lifecycle hook extraction
│   │   ├── MappingExtractor.SnapshotConfig.cs — Snapshot configuration
│   │   ├── MappingExtractor.Traversal.cs   — Type traversal
│   │   └── MappingExtractor.GlobalConfig.cs — Global configuration application
│   ├── FluentExtractor.cs                  — Fluent channel: `[MapperConfig]` declaration extraction (partial main file)
│   │   ├── FluentExtractor.Parsing.cs      — Chain-call parsing
│   │   ├── FluentExtractor.Declarations.cs — Declaration materialization
│   │   └── FluentExtractor.Equality.cs     — Equivalence/conflict determination
│   ├── DualDriverArbiter.cs                — Dual-channel snapshot merge arbitration (channel/side exclusivity principle enforcement)
│   ├── MappingSnapshotBuilder.cs           — Mapping snapshot construction (partial main file)
│   │   └── MappingSnapshotBuilder.When.cs  — `MapWhen` guard snapshot
│   ├── Models.Snapshots.cs                 — Snapshot models
│   ├── SnapshotMetadataEmitter.cs          — Snapshot metadata emission
│   ├── RuntimeSnapshotGenerator.cs         — Runtime engine snapshot (dual-engine identical hosting)
│   ├── ReversePlanner.cs                   — Reverse mapping planning
│   │
│   ├── CodeGenerator.cs                    — Mapping code generation entry orchestration (partial main file)
│   │   ├── CodeGenerator.Mapping.cs        — Core/Action/MapList + property assignment
│   │   ├── CodeGenerator.Collections.cs    — Collection/dictionary assignment materialization
│   │   ├── CodeGenerator.Conversion.cs     — Built-in conversion expressions
│   │   ├── CodeGenerator.ConverterCall.cs  — Converter call rendering
│   │   ├── CodeGenerator.ConverterMethod.cs — Converter method materialization
│   │   ├── CodeGenerator.WhenGuard.cs      — `MapWhen` guard code
│   │   ├── CodeGenerator.DeepCopy.cs       — `DeepCopy` record/collection/parameterless constructor paths
│   │   ├── CodeGenerator.Compose.cs        — `Compose` composite mapping
│   │   ├── CodeGenerator.Validate.cs       — Compile-time validation results
│   │   └── CodeGenerator.Report.cs         — Mapping relationship visualization report generation (`NCMapperGenerateReport`)
│   │
│   ├── DiagnosticCollector.cs              — Diagnostic collection
│   ├── DiagnosticDescriptors.Fluent.cs     — `NCMAP_Fxx` family diagnostic descriptors
│   ├── DiagnosticOptionsResolver.cs        — Diagnostic option resolution
│   ├── ArrayContainsInExpressionAnalyzer.cs — `NCMAP018` diagnostic analyzer: detects `T[].Contains` binding to `MemoryExtensions.Contains` in expression tree lambdas (net10/C#14)
│   ├── ChainCallParser.cs                  — Chain-call parser
│   ├── LambdaParser.cs                     — Lambda parser
│   ├── TernaryFinder.cs                    — Ternary expression finder
│   ├── InlineTranscriber.cs                — Capture-free lambda inline transcription
│   ├── ConfigureMethodLocator.cs           — `Configure` method locator
│   ├── SubclassSyntheticPipeline.cs        — `MapSubclass` synthetic pipeline
│   ├── PathResolver.Symbol.cs              — Dot-path resolver (symbolic surface)
│   ├── PlanSymbolAdapter.cs                — Plan/symbol adapter
│   ├── TypeCompatAdapter.Symbol.cs         — Type compatibility adapter (symbolic surface)
│   ├── TypeCompatAdapter.TypeRef.cs        — Type compatibility adapter (TypeRef surface)
│   ├── TypeRefHelpers.cs                   — `TypeRef` utilities
│   ├── TypeRefDeepCopyClassifier.cs        — Deep copy three-value classifier (`TypeRef` surface)
│   ├── TypeHelpers.cs                      — Roslyn type judgment helpers
│   ├── attributenames.cs                   — Attribute fully qualified name constants
│   ├── SourceGenEscape.cs                  — Generated code escaping
│   ├── Models.cs                           — Data models (`MappingInfo`/`PropertyMapping`/`DiagnosticInfo`)
│   └── Resources/                          — Diagnostic message resources (`DiagnosticResources.resx` + `zh-CN`)
│
├── NCMapper.FreeSql/                       — FreeSql projection extension package
│   ├── NCMapperFreeSqlExtensions.cs          — Query extensions: `AsDto` terminal operation + automatic downgrade + multi-table `Compose` + pagination + tree/dictionary projection + mapped handoff (partial main file)
│   │   ├── NCMapperFreeSqlExtensions.Compose.cs — `Compose` multi-table composition subpath
│   │   └── NCMapperFreeSqlExtensions.Support.cs — Pagination/tree/dictionary support subpath
│   ├── NCMapperFreeSqlGuardExtensions.cs     — Compile-time guard: provides compile-time error hints for incompatible query chains (e.g., `GroupBy`)
│   ├── FreeSqlProjectionBuilder.cs           — Projection expression builder: generates ORM-compatible projection expressions (single/dual/triple table `Compose`)
│   ├── FreeSqlDegradationDetector.cs         — Downgrade detection: determines whether downgrade to two-step mapping is required (FreeSql nested projection limit: 3 levels)
│   ├── FreeSqlNavigationCollector.cs         — Navigation path collector: analyzes DTO cross-table dependencies, produces navigation tree for downgrade path `IncludeByPropertyName`
│   └── Internal/ComposeProjectionKernel.cs   — `Compose` projection kernel (multi-table JOIN composition)
│
├── NCMapper.EFCore/                        — EF Core projection extension package
│   ├── NCMapperEFCoreExtensions.cs           — Query extensions: `AsDto` terminal operation + automatic downgrade + pagination + dictionary/lookup + mapped handoff
│   ├── NCMapperEFCoreGuardExtensions.cs      — Compile-time guard: provides compile-time error hints when `AsDto` is used after `GroupBy` or after leaving `IQueryable`
│   ├── EfCoreProjectionBuilder.cs            — Projection expression builder: generates `Expression<Func<TSource,TTarget>>` (including naming convention flattening, collection navigation projection)
│   ├── EfCoreDegradationDetector.cs          — Downgrade detection: `IProjectionPolicy`-driven determination of whether downgrade to two-step mapping is required
│   └── EfCoreNavigationCollector.cs          — Navigation path collector: analyzes DTO cross-table dependencies, produces navigation tree for automatic downgrade path `Include`
│
├── NCMapper.Dapper/                        — Dapper adapter extension package
│   ├── NCMapperDapperExtensions.cs           — Unified `AsDto` entry-point extension
│   ├── DapperDtoSelector.cs                 — Terminal method selector (`Query`/`ToPagedList`/`ToTreeList`/`QueryMultiple`, etc.; partial main file)
│   │   ├── DapperDtoSelector.Execution.cs    — Execution subpath
│   │   ├── DapperDtoSelector.MultiResult.cs  — Multi-result-set subpath
│   │   ├── DapperDtoSelector.Paging.cs       — Pagination subpath
│   │   └── DapperDtoSelector.Tree.cs         — Tree subpath
│   ├── ISqlDialect.cs                       — SQL dialect interface (`CountSql` / `PageSql`)
│   └── SqlDialects.cs                       — 6 built-in dialects + `SqlDialectResolver` auto-detection
```
