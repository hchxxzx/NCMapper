> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: explanation/dual-channel
Related Diagnostic Codes: NCMAP_F11, NCMAP_F16, NCMAP_F39, NCMAP_F15, NCMAP025  
Library Version: v1.0.0+  
Last Updated: 2026-09-29  
---

# Dual-Channel Architecture Principles

## Overview

NCMapper provides two distinct channels for declaring type mappings:

- **Fluent Channel** (Primary Path): An independent configuration class is annotated with `[MapperConfig]`, and mappings are declared using a fluent, method-chaining API.  
- **Attribute Channel** (Secondary Path): The `[MapFrom]` family of attributes is applied directly onto DTO classes.

Both channels declare the *same conceptual mapping* — “how to convert from a source type to a target type” — and ultimately execute through the *same runtime code path*. This article explains:  
- Why two channels exist;  
- How both channels share identical semantics;  
- Why dual declaration for the same type pair is prohibited;  
- How declarations from both channels are unified into a single generated code artifact at compile time.

For engine-level responsibilities (source generator vs. expression tree), see [Dual-Engine Architecture](./dual-engine.md). For a detailed side-by-side comparison and selection guide between Fluent and Attribute APIs, see [Fluent vs. Attribute API Comparison](../reference/api-comparison.md). This article focuses exclusively on architectural principles — it does *not* duplicate the comparison table.

---

## 1. Why Two Channels?

A single channel cannot simultaneously satisfy two distinct user needs:

| Requirement | Fluent Channel | Attribute Channel |
|-------------|:---:|:---:|
| Keep DTO classes clean — no mapping annotations | ✅ (mapping intent lives in a separate config class) | ❌ (attributes reside directly on the DTO) |
| See all mapping relationships by opening the DTO | ❌ (must navigate to the config class) | ✅ (WYSIWYG) |
| Support chain-order dependency & multi-terminal composition within one chain | ✅ | ❌ |
| Constructor parameter binding (`MapCtorParam`) | ✅ (Fluent-only feature) | ❌ (no attribute equivalent) |
| Team preference for “annotate-on-class” style | ❌ (not idiomatic) | ✅ (idiomatic) |

The design conclusion is *not* “choose one”, but rather: **both channels coexist and are semantically equivalent**. Users select based on *where they prefer to declare mappings*, and different type pairs may freely mix both channels. Dual declaration is only prohibited when *the exact same type pair* is declared via both channels (see Section 4).

---

## 2. What Do the Two Channels Look Like?

### 2.1 Attribute Channel: Self-Describing DTOs

```csharp
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public string Name { get; set; }
    [MapProperty("user_name")]
    public string UserName { get; set; }
}
```

`[MapFrom]` is applied to the DTO class itself; property-level attributes (`[MapProperty]`, `[Convert]`, `[Mask]`, etc.) follow their respective properties. Advantage: All mapping relationships are immediately visible upon opening the class. Disadvantage: DTO classes become “polluted” with mapping concerns.

### 2.2 Fluent Channel: Centralized Configuration Classes

```csharp
[MapperConfig]
public class UserMapper
{
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<UserEntity, UserDto>()
           .Member(d => d.UserName).From(s => s.user_name)
           .Member(d => d.Secret).Ignore();
    }
}
```

`[MapperConfig]` decorates an independent configuration class (method name is arbitrary, but signature *must* be `static void Xxx(FluentMappingConfig cfg)`). All mapping intent is expressed inside the method body using fluent chaining. DTO classes require *zero* attributes. If no method matching this signature exists in the class, diagnostic `NCMAP_F15` is triggered.

Entry-point conventions are defined in:  
- `MapperConfigAttribute.cs` (attribute definition)  
- `FluentMappingConfig.cs` (fluent configuration object)

---

## 3. Semantic Sharing Layer: One Rule Set, Two Entry Points

The two channels are *not* implemented independently. At compile time, the source generator translates declarations from *both* channels into a **single internal mapping manifest** (internal type `MappingSnapshot`), which records:  
- Which source/target type pair is mapped;  
- Where each target property originates;  
- How conversion is performed;  
- Which members are ignored or masked.

From this point onward, generated code consumes *only the manifest* — it is agnostic to which channel produced the original declaration.

Therefore:

- Core semantics — including property matching, type conversion, ignore rules, and nested handling — are **shared identically** across both channels (semantic singularity, see the three semantic guides: [Mapping Rules](./semantics/mapping-rules.md), [Bidirectional Direction Semantics](./semantics/direction-semantics.md), and [Diagnostic Code System Model](./semantics/diagnostics-model.md));  
- For any given type pair, both channels generate **identical mapping methods**, covering all six forward forms + four reverse forms (derived), deep copy, and runtime registration — with *zero runtime behavioral difference*;  
- Feature equivalence is rigorously aligned item-by-item (e.g., Fluent `.Convert()` ↔ Attribute `[Convert]`, `.Ignore()` ↔ `[MapIgnore]`, etc.; equivalence IDs A-xx). These alignments are enforced line-by-line via automated tests and mechanical gatekeeping to prevent semantic drift across versions.

---

## 4. Compile-Time Merging and Mutual Exclusion Rules

The outputs of both channels converge at a **merge point** inside the source generator (internally implemented as the `DualDriverArbiter`). Declarations are grouped by key `(sourceType, targetType)` into a unified manifest before code generation. During merging, a set of mutual exclusion checks is enforced — **if the same mapping is declared twice with potentially conflicting intent, the generator refuses to guess and emits no code**.

### 4.1 Four Mutual Exclusion / Conflict Rules

| Diagnostic Code | Scenario | Resolution |
|-----------------|----------|------------|
| `NCMAP_F11` | The *same type pair* is declared via **both channels** (cross-channel dual declaration) | Each declaration location reports one diagnostic (messages cross-reference each other); *neither* declaration generates code. Runtime falls back to the universal expression-tree engine. |
| `NCMAP025` | **Within the same channel**, bidirectional mutual reference (e.g., `A [MapFrom(B)]` *and* `B [MapFrom(A)]`) | Error-level diagnostic; *no code generated* for that pair. Runtime falls back to name-based matching. Removing one declaration restores code generation. |
| `NCMAP_F16` | **Within the Fluent channel**, duplicate declaration of the same type pair (e.g., two config classes both contain `For<A, B>()`) | Each occurrence reports one diagnostic (message lists all conflicting locations); *none* generate code. |
| `NCMAP_F39` | Lifecycle hooks (e.g., `Before`/`After`) for the *same type pair* conflict *across channels*: both sides declare `Before`, or both declare `After`, using *different methods* | Each location reports one diagnostic, naming the conflicting method. |

Note the division of labor between `NCMAP_F11` and `NCMAP025`:  
- `F11` governs **cross-channel conflicts** (Fluent vs. Attribute competing for the same type pair);  
- `025` governs **intra-channel bidirectional mutual references**.  
Type pairs are compared *unordered*: `For<A, B>()` followed by `For<B, A>()` is treated as the *same pair*.

### 4.2 Coexistence Is the Default

Mutual exclusion applies *only* to the *same type pair*. For *different* type pairs, mixing channels is unrestricted and encouraged:

```csharp
[MapperConfig]
public class OrderMapper
{
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<OrderEntity, OrderDto>()...;   // Orders use Fluent
    }
}

[MapFrom(typeof(ProductEntity))]              // Products use Attribute
public class ProductDto { ... }
```

This is the recommended pattern: choose per-type-pair based on required capabilities and team workflow preferences — with zero interference.

### 4.3 Explicit Declarations Override Auto-Synthesis

When polymorphic subtype mapping (`MapSubclass`) is enabled, the source generator *automatically synthesizes* declarations for derived type pairs. Synthesis follows **explicit-over-implicit priority**: if *any* channel has an explicit declaration for a subtype pair, synthesis yields and produces *no conflict*. Synthesis exists solely to fill gaps — not to compete.

---

## 5. Diagnostic Code Domain Partitioning

Diagnostic codes are partitioned into two number ranges — the code itself reveals which channel the declaration resides in:

| Range | Channel | Example |
|-------|---------|---------|
| `NCMAP0xx` (three-digit) | Attribute Channel (attributes applied to DTO classes) | `NCMAP025`: bidirectional mutual reference; `NCMAP033`: reverse converter not consumed |
| `NCMAP_Fxx` (F-prefixed) | Fluent Channel (fluent declarations inside config classes) | `NCMAP_F11`: cross-channel conflict; `NCMAP_F16`: intra-Fluent duplication; `NCMAP_F15`: config class lacks valid method |

Cross-channel diagnostics (`F11`, `F39`) belong to the Fluent range because resolution typically involves deleting *one* declaration — and the Fluent diagnostic pipeline takes responsibility for surfacing these issues first. Full family index: [Fluent Compile-Time Diagnostics](../how-to/fluent/basics.md#compile-time-diagnostics-ncmap_f-series-full-index) and [Diagnostic Rules](../diagnostics/rules.md).

---

## 6. Capability Divergence Surface

Semantic equivalence does *not* imply full capability parity. A few features exist exclusively on one side:

- **Fluent-only**:  
  - Constructor parameter binding (`.MapCtorParam`)  
  - Chain-order dependency (multi-terminal composition respects declaration order within a single chain)  
  - Inline lambda guards for `.MapWhen` (write condition logic directly at call site, no need to define named methods)  

- **Attribute-retained**:  
  - Reverse converters (`[ConvertBack]`) on *entity side* (non-DTO side) — natively supported in attributes; no forced Fluent counterpart required.

Full equivalence mapping and divergence details are documented in [Dual API Comparison §2](../reference/api-comparison.md). New capabilities are introduced first in the Fluent channel by default, then back-ported to the Attribute channel via equivalence-mapping mechanisms to ensure gradual alignment.

---

## Quick Reference Table

| Concept | Description |
|---------|-------------|
| **Dual Channel** | Fluent Channel (`[MapperConfig]` config class + fluent declarations, primary path) and Attribute Channel (`[MapFrom]` on DTOs, secondary path) |
| **Semantic Sharing** | Both channels are compiled into a *single internal mapping manifest*; matching, conversion, ignore, and nesting semantics are unified and source-identical; generated code is byte-for-byte identical |
| **Merge Point** | Inside the source generator, declarations are merged by key `(sourceType, targetType)`; mutual exclusion checks run during merge |
| **Same-Pair Dual Declaration** | `NCMAP_F11`: each location reports one diagnostic; neither generates code; runtime falls back to expression-tree engine |
| **Intra-Channel Mutual Reference** | `NCMAP025`: bidirectional error-level block; `NCMAP_F16`: Fluent-side duplicate declaration |
| **Pattern Coexistence** | Different type pairs may freely mix channels — fully isolated and officially recommended |
| **Diagnostic Domain** | `NCMAP0xx` = Attribute side; `NCMAP_Fxx` = Fluent side |

---

## Frequently Asked Questions

**Do both channels generate separate mapping methods?**  
No. After merging at the merge point, *exactly one* mapping method is generated per type pair. When dual-declared, *neither* generates (blocked by `F11`), so no duplicate methods ever appear.

**Does mixing channels cause interference?**  
No — different type pairs are fully isolated. Mixing is the *recommended usage pattern*. Only *one* scenario causes failure: dual declaration of the *exact same type pair*, which triggers `NCMAP_F11` at compile time — preventing faulty runtime behavior.

**If a type pair is dual-declared, can it still map at runtime?**  
Yes — but only via the generic fallback path (expression-tree engine), and *only after emitting a mandatory diagnostic*. Because the two declarations may conflict, the fallback result is *not guaranteed* to match either declaration. The correct fix is to delete one declaration.

**Why are new capabilities introduced in Fluent first?**  
Fluent is the primary path: centralized configuration enables complex intent (e.g., chain ordering) without polluting DTOs. Once stabilized in Fluent, features are systematically back-ported to the Attribute channel via equivalence-mapping — ensuring progressive, reliable alignment.

---

## Related Documentation

- [Dual-Engine Architecture](./dual-engine.md): How the source generator and expression-tree engines divide responsibilities *after* declarations are unified into generated code  
- [Adaptive Routing Mechanism](./adaptive-routing.md): How components automatically select execution paths at decision points after declaration  
- [Fluent vs. Attribute API Comparison](../reference/api-comparison.md): Side-by-side equivalence mapping and selection guide (the *only* authoritative comparison page)  
- [Fluent Basics (Fluent Declaration Entry Point)](../how-to/fluent/basics.md): Fluent channel usage guide and full `F`-series diagnostic index  
- [Declarative (Attribute) Basic Mapping](../how-to/declarative/basics.md): Attribute channel usage guide  
- [Mapping Rules (Semantics)](./semantics/mapping-rules.md): The shared semantic single source of truth for both channels
