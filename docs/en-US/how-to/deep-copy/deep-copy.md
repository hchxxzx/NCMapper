> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: how-to/deep-copy/deep-copy
Diagnostic codes involved: (none)
Library version: v1.0.0+
Last updated: 2026-09-29
---

# Deep Copy

## Overview

Deep copy in NCMapper is driven by `Mapper.DeepCopy<T>(src)`. The actual execution is split into two layers, with the framework automatically routing based on type — no manual selection required:

- **Layer 1 · Source Generator First (Optimal Path)**: If the type is annotated with `[MapFrom]` (and the source generator has generated a deep-copy delegate for it), the compiled-time-generated dedicated delegate is invoked directly, **bypassing the runtime provider**. Typical performance: ~179 ns/op (net10.0 Linux). This is the fastest path available.
- **Layer 2 · Runtime Strategy Fallback**: When no source-generated delegate exists, the call is forwarded to the strategy provider referenced by `DeepCopyProviderBase.Current`, which is determined by the `DeepCopyStrategy` enum (default: `Fast`).

Thus, “the default strategy is `Fast`” holds **only when the type does *not* carry `[MapFrom]`**; types marked with `[MapFrom]` always take the faster source-generator path, regardless of the `DeepCopyStrategy` configuration.

Core APIs: `Mapper.DeepCopy<T>(src)` / `Mapper.DeepCopy<T>(src, Action)`

---

## 1. Basic Deep Copy

### Usage

Creates a fully independent copy of the source object, including all nested reference types.

[!code-csharp[Deep copy of nested reference types — full replication](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyCrossType)]

**Key points**:

- Deep copy produces a completely independent object graph, including all nested reference types.
- Value types and `string` are copied by value (value semantics).
- Collection types recursively deep-copy each element.

---

## 2. Deep Copy with Action Callback

[!code-csharp[Deep copy + Action callback](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyWithAction)]

> ⚠️ **Reference Assignment Pitfall**: The `Action` receives both `src` and `tgt` parameters. Assigning a reference-type property from `src` directly to `tgt` inside the action (e.g., `tgt.Address = src.Address`) **violates deep-copy semantics**, causing `tgt` and `src` to share the same referenced object — subsequent modifications to either will affect the other.
>
> **Correct approach**: Within the action, only mutate value-type fields or strings on `tgt`. To propagate a reference-type property, explicitly invoke `Mapper.DeepCopy` again:
> ```csharp
> var copy = Mapper.DeepCopy(original, (src, tgt) =>
> {
>     tgt.Name = $"[COPY] {src.Name}";
>     tgt.Address = Mapper.DeepCopy(src.Address!);  // ✅ Re-deep-copy the reference property
>     // tgt.Address = src.Address;                // ❌ Breaks deep-copy semantics
> });
> ```
>
> When `MapTracer.GloballyEnabled = true`, a warning about risky reference assignments is emitted before the action executes — helpful for debugging.

---

## 3. Strategy Comparison

Deep-copy strategies are defined by the `DeepCopyStrategy` enum, with three values (default: `Fast`):

| Enum Value | Corresponding Provider Type | Semantics |
| --- | --- | --- |
| `DeepCopyStrategy.Fast` | `FastDeepCopyProvider` | Default (for types *without* `[MapFrom]`); supports cyclic references; uses zero-overhead fast path for acyclic graphs |
| `DeepCopyStrategy.ExpressionTree` | `ExpressionTreeDeepCopyProvider` | Full cyclic-reference detection; preserves shared references in DAGs |
| `DeepCopyStrategy.Json` | `JsonDeepCopyProvider` | JSON serialization round-trip fallback; **does not support cyclic references** |

> 📌 **Concept Clarification**: Internally, the `Fast` strategy classifies type graphs into a **three-valued taxonomy** (`Acyclic` / `Cyclic` / `Unknown`) to decide whether to use the zero-detection fast path or full detection. This is an **implementation detail** of the `Fast` strategy — distinct from the top-level strategy enum values. Do not conflate them.

| Strategy | Performance | Cyclic References | Use Case |
|---|---|---|---|
| `Fast` (default, conditional dispatch) | Acyclic: ~401 ns (fast path) / Cyclic: delegates to base-class full detection (any cycle length) | Supported (`Acyclic`: zero-detection fast path; `Cyclic`/`Unknown`: full detection) | Default choice; *does not preserve DAG shared references* |
| `ExpressionTree` | ~409 ns | Any cycle length | Required when DAG shared-reference preservation is needed |
| `JSON` | ~1697 ns | Not supported | Compatibility fallback (serialization + deserialization) |

> **Benchmark Environment Note**: Performance figures in this section reflect full-scale benchmark runs conducted on 2026-10-07 under Linux (net10.0, Intel Xeon Silver 4314 / Ubuntu 24.04.5 LTS / BenchmarkDotNet 0.14.0). Source-generator deep copy (same measurement baseline): ~179 ns. Absolute numbers vary significantly across CPU/OS/.NET versions — these values serve only as relative magnitude and path-comparison guidance.

> ⚠ **DAG Shared References**: The `Fast` path now performs conditional dispatch based on compile-time or first-run three-valued classification (`Acyclic`/`Cyclic`/`Unknown`). For **`Acyclic` types**, DAG shared references are *copied independently at each occurrence*. To retain shared references in DAGs, use `ExpressionTreeDeepCopyProvider`.

### Switching Strategies

Switch globally via `DeepCopyProviderBase.Current`. Alternatively, configure via `NCMapperOptions.DeepCopyStrategy` property or the `DeepCopyStrategy` key in `appsettings.json` (string values: `"Fast"` / `"ExpressionTree"` / `"Json"`, default `"Fast"`). Both are equivalent — `NCMapperOptions.Apply()` maps the enum to `DeepCopyProviderBase.Current`:

[!code-csharp[Deep copy strategy switch: ExpressionTree](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyExpressionTree)]
[!code-csharp[Deep copy strategy switch: JSON (factory property)](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyJson)]

**Key points**:

- Strategy switching is global and affects all subsequent `Mapper.DeepCopy` calls.
- Always restore the original strategy after temporary changes (e.g., using `try`/`finally`).
- `FastDeepCopyProvider` is the default strategy (**applies only to types *without* `[MapFrom]`**) and performs three-valued classification (`Acyclic`/`Cyclic`/`Unknown`) for conditional dispatch:
  - **`Acyclic`**: Zero-detection fast path (~401 ns); circular buffer used only as false-negative fallback (cycle length ≤ 8), rarely triggered.
  - **`Cyclic`/`Unknown`**: Delegates to base-class full detection (using `CycleCache` + record placeholders; handles any cycle length).
  - **`Fast` now fully supports cyclic references** — there’s no need to switch to `ExpressionTree` solely to handle self-references or cycles. The sole functional difference lies in DAG shared-reference semantics (see below).
- `ExpressionTreeDeepCopyProvider` includes full cyclic-reference detection (any cycle length) and **preserves DAG shared references**; `Fast`’s `Acyclic` path copies shared nodes independently per occurrence (performance-optimized).

> ⚠️ `MaxRecursionDepth`: Controlled via `DeepCopyContext` to prevent stack overflow from infinite recursion.

```mermaid
flowchart TD
    Start[Mapper.DeepCopy&#60;T&#62;&#40;src&#41;] --> HasGen{Type has [MapFrom]<br/>and source-generated delegate?}
    HasGen -->|Yes| Gen[Source-generator deep copy ~179ns<br/>Compile-time delegate, highest priority]
    HasGen -->|No| Cls{Under Fast strategy:<br/>Three-valued type-graph classification}
    Cls -->|Acyclic| FastAcyclic[Fast zero-detection fast path ~401ns]
    Cls -->|Cyclic / Unknown| FastFull[Fast full detection<br/>Delegates to base class, any cycle length]
    Start -->|Strategy=ExprTree| Expr[ExpressionTreeDeepCopyProvider ~409ns<br/>Preserves DAG shared references]
    Start -->|Strategy=JSON| Json[JsonDeepCopyProvider ~1697ns<br/>No cyclic-reference support]
    Gen --> End[Return independent copy]
    FastAcyclic --> End
    FastFull --> End
    Expr --> End
    Json --> End
```

---

## Common Misuses

### Misuse 1: Assuming `DeepCopy` Is Equivalent to `MemberwiseClone`

```csharp
// ❌ Misconception: DeepCopy is *not* a shallow copy
// DeepCopy recursively copies *all* nested reference types, producing a fully independent object graph

// ✅ DeepCopy is deep copy; MemberwiseClone is shallow copy (copies root object references only)
```

### Misuse 2: Using `Fast` Strategy When DAG Shared-Reference Preservation Is Required

[!code-csharp[DAG shared-reference preservation: switch to ExpressionTreeDeepCopyProvider](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyExpressionTree)]

### Misuse 3: Directly Assigning Source’s Reference Properties Inside `Action`

```csharp
// ❌ Misuse: Direct assignment of source’s reference properties inside action breaks deep copy
var copy = Mapper.DeepCopy(order, (src, tgt) =>
{
    tgt.Customer = src.Customer;  // copy.Customer now points to the *same* object as src.Customer
    tgt.Items = src.Items;        // Collection reference is shared
});
// Consequence: Modifying copy.Customer.Address affects src.Customer.Address

// ✅ Correct: Modify only value-type fields in action; deep-copy reference properties explicitly
var copy = Mapper.DeepCopy(order, (src, tgt) =>
{
    tgt.Status = "Copied";                         // Value type — safe
    tgt.Customer = Mapper.DeepCopy(src.Customer!);  // Deep-copy the reference property
});
```

> 💡 Enabling `MapTracer.GloballyEnabled = true` emits warnings before action execution to help detect this pitfall.

---

## 4. Collection Deep Copy

Deep copy automatically and recursively processes every element in collection types:

```csharp
// List deep copy
var list = new List<UserEntity>
{
    new() { Id = 1, Name = "Alice", Address = new Address { City = "Beijing" } },
    new() { Id = 2, Name = "Bob", Address = new Address { City = "Shanghai" } },
};
var copyList = Mapper.DeepCopy(list);
// copyList is a new List<UserEntity>
// Each element is an independent copy (including nested Address objects)

copyList[0].Name = "Modified";
copyList[0].Address.City = "Guangzhou";
// list[0].Name == "Alice" (unaffected)
// list[0].Address.City == "Beijing" (nested object also independent)

// Dictionary deep copy
var dict = new Dictionary<string, UserEntity>
{
    ["k1"] = new() { Id = 10, Name = "Dict-A" },
    ["k2"] = new() { Id = 20, Name = "Dict-B" },
};
var copyDict = Mapper.DeepCopy(dict);
// copyDict is a new Dictionary; both keys and values are independent copies

// HashSet deep copy
var set = new HashSet<UserEntity>
{
    new() { Id = 1, Name = "A" },
    new() { Id = 2, Name = "B" },
};
var copySet = Mapper.DeepCopy(set);
// copySet is a new HashSet<UserEntity>; elements are independent
```

**Key points**:

- Collection types such as `List<T>`, `Dictionary<TKey, TValue>`, `HashSet<T>`, `Queue<T>`, `Stack<T>` are automatically and recursively deep-copied.
- The collection itself is instantiated anew; each element undergoes deep-copy logic.
- Nested collections (e.g., `List<List<T>>`) are processed recursively at every level.

---

## 5. Deep Copy vs. Shallow Copy

| Dimension | `Mapper.DeepCopy` | `MemberwiseClone` |
|---|---|---|
| Root object | New instance | New instance |
| Value-type fields | Independent copy | Independent copy |
| `string` fields | Independent copy (`string` is immutable — sharing is semantically equivalent) | Shared reference |
| Reference-type fields | **Recursive deep copy** (independent copy) | **Shared reference** |
| Collection fields | Recursive deep copy of each element | Shared reference |
| Nested objects | Fully independent object graph | Shared with source object |
| Cyclic references | Supported (via `ExpressionTree` strategy) | Not supported (causes infinite loop) |
| Performance | Acyclic: ~401 ns (`Fast` Acyclic) / Cyclic: delegates to full detection (`Fast`) / ~409 ns (`ExpressionTree`) / ~179 ns (source generator, optimal; all net10.0 Linux) | ~5–10 ns |

```csharp
// Shallow copy: MemberwiseClone
var shallow = entity; // Cannot call MemberwiseClone directly (it's protected)
// Reference-type fields are shared → modifying shallow.Address.City affects entity

// Deep copy: Mapper.DeepCopy
var deep = Mapper.DeepCopy(entity);
// Reference-type fields are independent → modifying deep.Address.City does *not* affect entity
```

---

## 6. AOT Fallback Behavior (NativeAOT Deployment Notes)

> The source generator falls back to runtime deep copy in two scenarios,  
> triggering IL2026/IL3050 warnings (suppressed in generated code via precise `#pragma` directives with explanatory comments).  
> This section documents the **actual behavior** and mitigation strategies when fallback occurs under AOT.

### 6.1 P5b: Circular Reference Fallback (Safety Mechanism, Not Bypassable)

The combination of `record` types and cyclic reference graphs **cannot** be deep-copied by the source generator: a `record`’s constructor path cannot pre-register the target object before `new T(...)`, and circular references would cause infinite recursion. When this scenario is detected, generated code falls back to `DeepCopyRuntimeOnly` at runtime (which uses `ConditionalWeakTable` and supports cycle detection for `record`s).

- **Behavior under AOT**: The fallback path invokes runtime reflection; `RequiresUnreferencedCode`/`RequiresDynamicCode` warnings have been suppressed — **but runtime reflection may fail under trimming/AOT** (members may be trimmed or dynamic code may be unavailable).
- **Mitigation strategies**:
  - Prefer **parameterless-constructor types** (to stay on the generated-code path, zero reflection);
  - Ensure `record` types maintain an **acyclic graph** (classified as `Acyclic` at compile time, avoiding fallback);
  - If `record` + cycles are truly required: use `DeepCopyProviderBase.Current.DeepCopy` and manually preserve reflection-required members (e.g., via `[DynamicallyAccessedMembers]`), or rely on runtime providers in untrimmed environments.

### 6.2 P5c: Compilation Failure Fallback (Theoretical Path)

When the target type cannot be compiled into generator code (e.g., no resolvable constructor), the system falls back to runtime deep copying. This does not occur for well-formed types; if triggered, it indicates the type’s shape exceeds the generator’s capability boundary — consider redesigning the type (e.g., adopt parameterless constructors + writable properties).

---

## Summary

| Capability | API | Description |
|---|---|---|
| Deep Copy | `Mapper.DeepCopy<T>(src)` | Produces a fully independent copy |
| With Callback | `Mapper.DeepCopy<T>(src, action)` | Modifies the copy after creation |
| Strategy Switching | `DeepCopyProviderBase.Current` | Global strategy override (restored afterward) |
| Recursion Depth Control | `DeepCopyContext` | Configures `MaxRecursionDepth` |

---

## Companion Examples

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 401
```

Core example source (single-source, runnable for verification via `dotnet run --scenario`):

[!code-csharp[Deep Copy of Same Type](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType)]

[!code-csharp[Deep Copy of Same Type · DeepCopyWithAction](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyWithAction)]

[!code-csharp[Deep Copy of Same Type · DeepCopyCrossType](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyCrossType)]

[!code-csharp[Depth Limiting: MaxRecursionDepth Guard](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_MaxRecursionDepth)]

---

## Further Reading

- Related topic: [Basic Mapping](../declarative/basics.md)
- In-depth technical documentation: [Deep Copy Architecture](../../explanation/deep-copy.md) §8
