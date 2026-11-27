# NCMapper Benchmarks

Performance benchmark suite for NCMapper. It contains three projects:

| Project | Purpose |
|---------|---------|
| `NCMapper.Benchmarks` | Main suite (BenchmarkDotNet): per-scenario comparison of NCMapper variants against **AutoMapper / Mapster / Mapperly / manual mapping**, plus compile-time source-generator driver benchmarks |
| `NCMapper.SelfBench` | Lightweight hand-rolled micro-benchmarks (no BenchmarkDotNet dependency), used as an independent cross-check of the main suite |
| `NCMapper.LoadBench` | Load-oriented scenarios: cold start, concurrency scaling, endurance (memory pressure), and in-place mapping reuse |

## Test reports

Full-batch results (all frameworks × all implementations) are archived in [`reports/`](reports/) — see [`reports/README.md`](reports/README.md) for the index.

| Date | Scope | Report |
|------|-------|--------|
| 2026-11-27 | net8/9/10 × (self 42 + rivals 49 classes) = 270 benchmarks | [NCMapper-性能基准测试报告.md](reports/20261127/NCMapper-性能基准测试报告.md) |

> Per-class BenchmarkDotNet reports live in [`baselines/`](baselines/) (long-term regression assets). `reports/` holds whole-run summaries plus raw data — the two serve different purposes.

## Running

Release configuration is required for meaningful numbers. Run one target framework at a time (`net8.0` / `net9.0` / `net10.0`):

```bash
# Full main suite (all scenario groups; can take a long time to complete)
dotnet run -c Release -f net10.0 --project benchmarks/NCMapper.Benchmarks

# Rival comparison only: NCMapper vs AutoMapper / Mapster / Mapperly (+ manual baseline)
dotnet run -c Release -f net10.0 --project benchmarks/NCMapper.Benchmarks -- baseline_rivals

# One scenario group (all variants of that group)
dotnet run -c Release -f net10.0 --project benchmarks/NCMapper.Benchmarks -- Flat

# One exact benchmark class
dotnet run -c Release -f net10.0 --project benchmarks/NCMapper.Benchmarks -- Flat_NCMapper
```

`NCMapper.Benchmarks` uses its own argument dispatcher (`args[0]` = mode / exact class name / scenario prefix); BenchmarkDotNet's `--filter` is not supported.

Other projects:

```bash
dotnet run -c Release -f net10.0 --project benchmarks/NCMapper.SelfBench
dotnet run -c Release -f net10.0 --project benchmarks/NCMapper.LoadBench
```

## Reading results

- BenchmarkDotNet writes per-class reports to `BenchmarkDotNet.Artifacts/results/` (Markdown / CSV / HTML) and prints a summary table.
- Compare `Mean` for throughput and `Allocated` for allocation pressure. On flat single-object scenarios the differences are measured in nanoseconds — prefer the scenario you actually care about (batch mapping, deep copy, JSON fields, ORM projection) over a single headline number.

## Reproducing the published numbers

The performance table in the main README and in [the Performance chapter](../docs/en-US/explanation/performance.md) (batch of **2026-10-07**) was produced with this suite:

- **Machine**: Intel Xeon Silver 4314, Ubuntu 24.04.5, .NET 10 (net10.0 target)
- **Rival versions** (pinned in `Directory.Packages.props`): AutoMapper 16.1.1, Mapster 10.0.9, Mapperly 4.3.1, BenchmarkDotNet 0.14.0
- **Command**:

```bash
dotnet run -c Release -f net10.0 --project benchmarks/NCMapper.Benchmarks -- baseline_rivals
```

Per-scenario results land in `BenchmarkDotNet.Artifacts/results/` (Markdown / CSV / HTML). The README comparison chart is rendered from these results; rerunning the command above on comparable hardware should reproduce the same relative ordering. Rival versions are pinned — quote the pinned versions whenever you republish numbers.

## Fair-comparison notes

- Rival package versions are pinned in `Directory.Packages.props` — quote the pinned versions whenever you publish numbers.
- Benchmark numbers are hardware-dependent: state the CPU, .NET runtime and GC mode alongside published results, and do not mix numbers collected on different machines.
- For stable measurements, close background applications and avoid other workloads during the run.
