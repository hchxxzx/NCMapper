# Contributing to NCMapper

Thank you for your interest in NCMapper! This document describes the development workflow and conventions for contributing to the project.

## Development Environment

- .NET SDK 10.0 (see `global.json` for the pinned version; the library multi-targets `net8.0` / `net9.0` / `net10.0`)
- Visual Studio 2026, Visual Studio Code, or any editor with C# tooling
- Git

## Project Layout

```
NCMapper/
├── src/
│   ├── NCMapper/                          # Core runtime library (net8.0/net9.0/net10.0)
│   │   ├── Mapper.cs                      # Public entry points + JIT generic caches
│   │   ├── Mapper.Compose.cs              # Multi-source Compose engine
│   │   ├── Mapper.Pipeline.cs             # PostMap pipeline hooks
│   │   ├── MapperBase*.cs                 # Core expression-tree engine (partials)
│   │   ├── ExpressionBuilder.cs           # Expression tree builder
│   │   ├── PropertyMatcher.cs             # Property matching (incl. bidirectional rules)
│   │   ├── PropertyIgnoreChecker.cs       # [MapIgnore]/global ignore evaluation
│   │   ├── GlobalMapConfig.cs             # [assembly: MapDefaults] global config
│   │   ├── MapFromAttribute.cs            # [MapFrom] (source-generator entry)
│   │   ├── MapPropertyAttribute.cs        # [MapProperty] explicit property mapping
│   │   ├── MapIgnoreAttribute.cs          # [MapIgnore] with Direction
│   │   ├── ConvertUsingAttribute.cs       # [ConvertUsing] custom value converters
│   │   ├── MaskAttribute.cs               # [Mask] data masking
│   │   ├── DeepCopy*.cs                   # DeepCopy providers (Fast/ExpressionTree/Json)
│   │   ├── MapException.cs                # Exception hierarchy
│   │   ├── ErrorMessages.cs               # Localized runtime error messages
│   │   ├── MapTracer.cs                   # Diagnostic tracing
│   │   ├── MapValidator.cs                # Mapping validation
│   │   ├── NCMapperOptions.cs             # Options
│   │   ├── NCMapperServiceExtensions.cs   # DI integration (AddNCMapper)
│   │   ├── NCMapperMapper.cs              # Default IMapper implementation
│   │   ├── IMapper.cs / IMask.cs / IProjectionPolicy.cs
│   │   ├── PagedList.cs / NavigationResolver.cs
│   │   └── TypeConverter.cs / TypeConvertHelper.cs / TypeHelper.cs / ReflectionHelper.cs
│   └── NCMapper.SourceGen/                # Source generator (netstandard2.0)
│       ├── NCMapperSourceGenerator.cs     # IIncrementalGenerator entry
│       ├── CodeGenerator*.cs              # Mapping/Compose/DeepCopy/Validate codegen
│       ├── MappingExtractor*.cs           # Mapping configuration extraction
│       ├── Models.cs                      # Generator data models
│       └── TypeHelpers.cs                # Type helpers
│
├── test/
│   ├── NCMapper.Tests/                    # Core unit tests
│   ├── NCMapper.SourceGen.Tests/          # Source generator tests
│   ├── NCMapper.IntegrationTests/         # ORM integration tests (EF Core / FreeSql / Dapper)
│   ├── NCMapper.AotTests/                 # NativeAOT compatibility tests
│   └── NCMapper.FuzzTests/                # Fuzz/robustness tests
│
├── benchmarks/                            # BenchmarkDotNet benchmarks
│   ├── NCMapper.Benchmarks/
│   └── NCMapper.SelfBench/
├── samples/
│   ├── NCMapper.Samples/                  # Interactive full-feature demo (scenarios 101-607)
│   └── NCMapper.AotSample/                # Minimal NativeAOT deployment demo
├── tools/
│   └── AotReflectionGuard/                # CI guard: scans ORM extensions for unannotated reflection
├── docs/
│   ├── zh-CN/                             # Chinese docs (authoritative source; Divio quadrants + diagnostics)
│   │   ├── index.md                       # Documentation portal (zh-CN)
│   │   └── getting-started/ how-to/ tutorials/ explanation/ reference/ diagnostics/ contributing/ ...
│   ├── en-US/                             # English translation (mirrors the zh-CN tree)
├── locales/                               # Localized IntelliSense XML docs (zh-Hans, en)
├── scripts/                               # Tooling scripts (e.g. check-requires-messages.ps1)
├── assets/                                # Package icon etc.
├── Directory.Build.props                  # Common MSBuild properties
├── Directory.Packages.props               # NuGet Central Package Management
├── NuGet.config                           # NuGet sources & audit configuration
├── NCMapper.slnx                          # Full solution
├── NCMapper.Lib.slnf                      # CI build filter (library + samples + benchmarks)
└── NCMapper.Core.slnf                     # Core subset filter
```

## Building and Testing

```bash
# Restore + build (CI uses the Lib filter with warnaserror)
dotnet build NCMapper.Lib.slnf --configuration Release --warnaserror

# Run the full test suite (CI also collects coverage)
dotnet test NCMapper.slnx --configuration Release

# Run a single test project
dotnet test test/NCMapper.Tests/NCMapper.Tests.csproj
```

> The repository uses NuGet Central Package Management (`Directory.Packages.props`) and lock files (`packages.lock.json`). Restore runs in locked mode to guarantee reproducible builds — do not regenerate lock files casually.

### Before You Open a Pull Request

CI runs the full gate on every pull request, so you do not need to reproduce everything locally. The essentials are:

```bash
# 1. Build with warnings as errors (same as CI's build job)
dotnet build NCMapper.Lib.slnf -c Release --warnaserror

# 2. Run the full test suite (same as CI's test job)
dotnet test NCMapper.slnx -c Release
```

If both pass, open the PR — CI will report anything else. You do **not** need a Windows AOT toolchain, a coverage threshold, or the packaging tools to contribute.

**Optional, by area of change.** Only if your change touches the relevant area, a targeted check is a good idea:

| If your change touches | Consider running |
|---|---|
| Markdown docs (`docs/**`) | `bash scripts/check-markdown-links.sh` |
| Code-comment anchors (`*.cs` doc references) | `python scripts/check-doc-anchors.py` |
| Localized IntelliSense XML (`locales/**`) | `pwsh scripts/check-locales-drift.ps1` |
| `[Requires*]` attribute messages | `pwsh scripts/check-requires-messages.ps1` |
| Any comment or diagnostic text (`src/**`, `samples/**`, resx `<value>`) | `pwsh scripts/check-comment-process-noise.ps1` |

**Have a Windows dev machine and want a closer match to CI?** `scripts/local-ci-check.ps1` runs the locally reproducible CI stages in one go:

```powershell
.\scripts\local-ci-check.ps1                 # scan + build + test (+ aot/samples/baseline)
.\scripts\local-ci-check.ps1 -Phase build,test   # just the essentials
```

See `scripts/README.md` for the full list of checks and what each one guards.

### Reproducing CI on Linux

`scripts/verify-linux.sh` is the Linux counterpart of `local-ci-check.ps1`: it runs the CI stages that are executable outside Windows (AOT is Windows/MSVC-only and is covered separately by `NCMapper.AotTests`). Use it to reproduce a red CI job without pushing.

```bash
bash scripts/verify-linux.sh              # all stages
SKIP_TESTS=1 bash scripts/verify-linux.sh  # skip the test stage
```

The stages are `restore` → `build` (Lib filter, `--warnaserror`) → `build samples` → `test` → `generated-baseline` → `check-diagnostics-sync` → `check-api-demo-coverage` → `check-fluent-negative-cases` → `check-fluent-legacy-scan`.

**Prerequisites.** Beyond `dotnet` and `git`, the script needs:

| Requirement | Why |
|---|---|
| .NET SDK matching `global.json` (10.0.403) | `rollForward` is `disable` — a different SDK fails resolution outright |
| .NET **runtimes** 8.0.x, 9.0.x and 10.0.x | The test stage runs every test project on all three TFMs; an SDK-only install ships the 10.x runtime alone and the net8/net9 runs abort with "You must install or update .NET" |
| PowerShell 7 (`pwsh`) | The `generated-baseline` stage shells out to `verify-generated-baseline.ps1`. **Without `pwsh` that stage is skipped and the run still reports PASS** — see the note below |
| `python3`, `rsync` | `check-doc-anchors.py` / `check-fluent-legacy-scan.py`, and the ext4 shadow tree |
| A `.git` directory in the working tree | The build stage uses `--warnaserror`; SourceLink emits "repository not found" warnings in a non-git tree, and those warnings become errors |

**Offline or DNS-restricted hosts.** Restore needs the exact versions pinned in `packages.lock.json` (locked mode rejects anything else), and the repository's `NuGet.config` begins with `<clear />`, so a user-level source is discarded. To restore without network access, put the pinned `.nupkg` files in a directory and point a repository-level `NuGet.config` at it:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="offline" value="/absolute/path/to/nupkg-directory" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="offline"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

The set to seed is every package referenced by the `packages.lock.json` files, **plus** the framework packs that the SDK resolves implicitly and therefore never lists: `Microsoft.NETCore.App.Ref`, `Microsoft.AspNetCore.App.Ref`, `Microsoft.NET.ILLink.Tasks` and `Microsoft.NETCore.App.Host.linux-x64`, each at the version matching the TFM (`8.0.33` / `9.0.22` / `10.0.14` for the current lock files). The host pack is the one most easily missed — it is not in any lock file, and its absence fails only the test and benchmark projects.

**Two ways the byte-identity gate can report a false PASS.** Both are worth knowing before you trust a green run:

- `verify-generated-baseline.ps1` treats a **failed build** as a skipped comparison and still exits 0, so a red build surfaces as `[baseline-check] PASS`. Read the per-project lines, not just the summary.
- `verify-linux.sh` skips its `generated-baseline` stage entirely when `pwsh` is missing, and the overall run still reports PASS.

Because a skip and a match are printed differently but summarised identically, confirm what actually ran:

```bash
pwsh -NoProfile -File scripts/verify-generated-baseline.ps1   # each project must print IDENTICAL
```

**Do not reimplement the comparison with byte-level tools.** The gate decides with a string comparison (`$a -ceq $b`) over `[System.IO.File]::ReadAllText()`, which strips a UTF-8 BOM on read. Reimplementing it as `sha256sum` over raw bytes reports a difference wherever a BOM is present on one side only — a false alarm, because the gate itself considers those files identical. The `Get-Sha256` hash the gate prints is for display only and takes no part in the verdict. Run the real script instead.

## Coding Conventions

- **XML doc comments on all public members** (classes, methods, properties, enum members, parameters, returns). Chinese is the working language for comments; technical terms stay in English.
- **`Requires*` attribute messages must be in English** — they surface as compiler diagnostics (IL2026/IL3050) to consumers. Enforced by `scripts/check-requires-messages.ps1` in CI.
- **.NET naming conventions**: `PascalCase` for public members, `_camelCase` for private fields.
- **Test naming**: `Feature_Scenario_ExpectedResult`.
- **Test projects disable parallelization** (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`).
- **Mapping semantics**: `[MapFrom]` enables bidirectional mapping regardless of which side (entity or DTO) carries the attribute; mutual `[MapFrom]` declarations (interlocking references) are an error (NCMAP025). Keep these rules in mind when touching mapping logic.

## Comment Content Contract

A comment has one job: state what the code guarantees. Project history belongs to git, not to comments.

| Layer | Where it lives | What belongs there |
|---|---|---|
| **Contract** | XML doc (`///`) on public members, diagnostic text (`*.resx` `<value>`), comments emitted into generated `.g.cs` | What the API does, parameter / return / exception behaviour, thread semantics, AOT and performance behaviour, capacity and eviction policy, usage constraints |
| **Implementation** | `//` comments inside method bodies | Local traps, invariants, why a non-obvious implementation is correct |
| **Evolution** | git history, commit messages, PR descriptions | When and why something changed, review rounds, task context |

**The one-line test.** If you delete the sentence, would a caller or a maintainer make a *wrong decision*? No → delete it. Yes → keep it, but phrase it as behaviour (“`ConcurrentDictionary` guarantees multi-thread visibility”), never as history (“was `volatile`, changed in …”).

### Never write in a comment

- task, requirement, review or batch identifiers of any shape — `TASK-YYYYMMDD-NNN`, `T-001`, `S-12`, `F-B9`, `W3_1`, `Q5 定案`, `波3-E`, `（评审 S-6）`, …
- references to internal design documents or their sections — `方案书 §5.3`, `任务书 §10.5`, a bare `§4.2`, `ADR-002 决策 3`
- dates and change narration — `2026-10-12`, “previously … now …”, “originally hard-coded in `Foo.cs:417-424`”
- process status words — `收口` / `整改` / `批阅` / `本任务` / `跟进` / `登记` / `定稿`
- line-number references into other files

The gate `scripts/check-comment-process-noise.ps1` enforces this over `src/**` and `samples/**` (including resx `<value>` and trailing comments) and runs as part of `scripts/local-ci-check.ps1 -Phase scan`; a new hit fails the gate.

### Four steps when a comment seems to need history

1. Write the guarantee the reader must respect — not the story that produced it.
2. If the reason comes from another component, name the component and the constraint, not the task that discovered it.
3. If the history really matters, put it in the commit message or the PR description.
4. Before committing, run `pwsh scripts/check-comment-process-noise.ps1` and keep it green.

## Commit Message Conventions

Format: `type(scope): description [REF]`

- **type**: `feat` / `fix` / `perf` / `refactor` / `docs` / `test` / `chore` / `ci` / `build`
- **scope**: optional, usually the affected area (`samples`, `sourcegen`, `core`, `infra`, ...)
- **REF**: optional trailing reference — typically an issue number (`[#123]`). Outside contributors can simply omit it.

Examples:

```
feat(samples): add NativeAOT deployment sample
docs(infra): document version strategy
fix(ci): declare dual RuntimeIdentifiers to align lock files across platforms [#42]
```

## Pull Request Process

1. Fork the repository and create a feature branch.
2. Ensure all tests pass locally (`dotnet test NCMapper.slnx`).
3. Add test coverage for any new functionality.
4. Update `CHANGELOG.md` for user-visible changes.
5. Open the PR and describe the change. CI runs the full gate: `build` (warnaserror + AOT reflection guard), `test` (coverage), `aot`, `samples`, `pack` — all must be green.

## Documentation Conventions

- User-facing docs live under `docs/zh-CN/` (Divio quadrants: getting-started / how-to / tutorials / explanation / reference / diagnostics, plus the portal `index.md`). English translations go to `docs/en-US/`.
- When adding or moving a page, keep both language trees and the corresponding `toc.md` entries in sync.
- Localized IntelliSense XML (`zh-Hans` and `en` under `locales/`) is generated/translated via the project's DocTranslate toolchain; source comments stay Chinese.

## License

By contributing, you agree that your contributions are licensed under the [Apache License 2.0](LICENSE).
