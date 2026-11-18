> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

# NCMapper Documentation Portal

NCMapper is a high-performance .NET object mapping library: a dual-engine system (**source generator + expression trees**) that automatically selects the optimal mapping path; compile-time diagnostics safeguard correctness; and coverage includes ORM projections (`AsDto`), deep copying, data sanitization, and DI integration.

**Find what you need in 3 seconds** — first answer *“What are you trying to do?”*:

| What you want to do | Go here |
|---|---|
| Understand what it is and what it can do | [What Is NCMapper?](getting-started/overview.md) |
| Use it for the first time — learn basic mapping | [Quick Start](getting-started/quickstart.md) |
| Replace attribute-based configuration with fluent declarations (Fluent API) | [Fluent API Overview](how-to/fluent/basics.md) |
| Solve a specific problem (e.g., “How do I configure X?”) | [How-To Guides](how-to/fluent/basics.md) (task-oriented, Fluent API–first) |
| Look up APIs, configuration options, or diagnostic codes | [API Reference](reference/index.md) · [Diagnostic Codes](diagnostics/rules.md) |
| Understand underlying principles and design decisions | [Explanation & Mechanisms](explanation/dual-engine.md) |
| Migrate from other mapping libraries | [Migrating from Other Mappers](getting-started/migrating-from-other-mappers.md) |

## Four-Quadrant Navigation

| Quadrant | Directory | Content |
|---|---|---|
| **Tutorial** | [getting-started/](getting-started/overview.md) · [tutorials/](tutorials/compose-tutorial.md) | Get started in 15 minutes; scenario-based walkthroughs (Compose / ORM Projection / Deep Copy) |
| **How-To** | [how-to/](how-to/fluent/basics.md) | Task-oriented articles (aligned with sample scenarios; Fluent API–centric) |
| **Reference** | [reference/](reference/index.md) · [diagnostics/](diagnostics/rules.md) | Core APIs, attribute system, diagnostic code index, configuration |
| **Explanation** | [explanation/](explanation/dual-engine.md) | Dual-engine architecture, ORM degradation mechanism, reverse-mapping semantics, deep-copy system |

## Frequently Accessed Topics

- [15-Minute Getting-Started Tutorial](getting-started/tutorial-15min.md) — End-to-end walkthrough: from entity/DTO definition to validation  
- [Complete Diagnostic Code Index](diagnostics/rules.md) — Quick reference for all NCMAP diagnostic codes  
- [Global Mapping Configuration](how-to/infrastructure/global-config.md) — Global defaults and unmapped-member strategies  
- [Performance & Best Practices](how-to/infrastructure/performance.md) — Engine comparison, hot-path optimization  
- [AOT / NativeAOT Deployment](how-to/aot/deployment.md) — Publishing modes and corresponding diagnostic codes  

## New P2 Content

| Article | Focus |
|---------|--------|
| [15-Minute Getting-Started Tutorial](getting-started/tutorial-15min.md) | End-to-end walkthrough from entity definition to validation (DFM references multi-domain samples and shared models) |
| [Multi-Source Composition Tutorial](tutorials/compose-tutorial.md) | Full workflow for composing an aggregate DTO from Order + Customer + Product |
| [ORM Projection Tutorial](tutorials/orm-projection-tutorial.md) | Single-SQL list page, pagination, and degradation behavior |
| [Deep Copy Tutorial](tutorials/deepcopy-tutorial.md) — Edit snapshots, three strategy options, and depth limits |
| [Dual-Engine Architecture](explanation/dual-engine.md) — Why two engines, path selection logic, and consistency guarantees |
| [ORM Degradation Mechanism](explanation/orm-degradation.md) — Trigger conditions, post-degradation behavior, and mitigation guidance |
| [Reverse-Mapping Semantics](explanation/reverse-mapping-semantics.md) — Reverse-direction behavior, converter handling (NCMAP033/034), and null write-back |
| [Three Principles of Mapping Configuration Ownership](explanation/semantics/mapping-config-ownership.md) — “No anchor, no consumption”, compile-time diagnostics, and registration surface ownership |

## Run the Samples

All sample code resides in `samples/NCMapper.Samples/`, aligned with the How-To guides (see the [sample-to-scenario mapping table](reference/samples-index.md)):

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 201   # Run a specific scenario
dotnet run --all           # Run all scenarios
```
