> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
wiki-id: tutorials/deepcopy-tutorial
Related diagnostic codes:  
Library version: v1.0.0+  
Last updated: 2026-08-19  
---

# Deep Copy Scenario Walkthrough

## Overview

This tutorial addresses a classic problem in editing scenarios: before submitting a form, you must save a “pre-edit snapshot”; upon cancellation, you restore the original state. Direct assignment by reference causes unintended side effects — you need a copy that is *completely independent* from the source object. In this tutorial, you’ll implement basic deep copying, post-clone processing, strategy selection, and deep copying of collections.

The examples are drawn from scenario 03 in `samples/NCMapper.Samples` and can be run for verification. For underlying mechanisms and strategy principles, see [Deep Copy](../how-to/deep-copy/deep-copy.md); this tutorial focuses on *decision-making in business contexts*.

---

## 1. Scenario: Cancel-and-Restore for Edit Forms

When an edit page opens, first save a snapshot of the original data:

```csharp
var original = Mapper.DeepCopy(entity);   // Pre-edit snapshot
// User edits entity ...
// User clicks Cancel:
//   entity has been modified by the form — cannot be restored directly
//   original is an independent copy — safe to use for restoration
```

Why not simply `var original = entity`? Assignment copies only the *reference*, so `original` and `entity` point to the same object. Modifying `entity` also changes `original`, invalidating the snapshot. Nested objects (e.g., `Address`) share references identically.

`DeepCopy` recursively duplicates the entire object graph: each nested object is instantiated anew, yielding a copy fully independent from the source.

| Copy method | Top-level reference | Nested objects | Result |
|-------------|---------------------|----------------|--------|
| Reference assignment (`=`) | Shared | Shared | Modifying source affects copy — snapshot fails |
| Shallow copy (field-level copy) | Independent | Shared | Top-level independent; nested objects still shared |
| Deep copy (`Mapper.DeepCopy`) | Independent | Independent | Fully independent copy |

Typical use cases for deep copy: edit-form snapshots, transaction rollbacks, audit snapshots, cache immutability enforcement.

---

## 2. Basic Deep Copy

Call `Mapper.DeepCopy(src)`:

[!code-csharp[Same-type deep copy](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType)]

[!code-csharp[Same-type deep copy · DeepCopyWithAction](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyWithAction)]

[!code-csharp[Same-type deep copy · DeepCopyCrossType](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyCrossType)]

The result is a copy with identical type and content as the source, but with *no shared references*. Modifying any nested property of the copy leaves the source unaffected.

Differences between deep copy and mapping:

| Dimension | Mapping (`Mapper.Map`) | Deep copy (`Mapper.DeepCopy`) |
|-----------|------------------------|-------------------------------|
| Target type | Usually differs from source (e.g., Entity → DTO) | Same as source |
| Semantics | Property copying + transformation | Object-graph cloning with reference isolation |
| Typical use case | Layered data transfer | Snapshots, rollbacks, immutability enforcement |

When copying same-type objects, `DeepCopy` uses deep-copy semantics; for cross-type copies, it delegates to the mapping pipeline ([Deep Copy](../how-to/deep-copy/deep-copy.md)).

---

## 3. Post-Clone Processing

To annotate the clone (e.g., add a log prefix), use the overload accepting an `Action` callback:

[!code-csharp[Deep copy with callback](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType)]

[!code-csharp[Deep copy with callback · DeepCopyWithAction](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyWithAction)]

[!code-csharp[Deep copy with callback · DeepCopyCrossType](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyCrossType)]

The callback receives the *target* (i.e., the copy) and executes *after* copying completes. Ideal for timestamping snapshots or adding audit markers.

---

## 4. Choosing Among the Three Strategies

NCMapper provides multiple deep-copy strategies, switchable at runtime via `DeepCopyProviderBase.Current`:

[!code-csharp[Fast strategy](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyFast)]

[!code-csharp[ExpressionTree strategy](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyExpressionTree)]

[!code-csharp[JSON strategy](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyJson)]

| Strategy | Characteristics | Use case |
|----------|-----------------|----------|
| Source Generator (default, preferred) | Compile-time generated; zero first-call overhead | Types registered with `[MapFrom]`; suitable for most scenarios |
| Fast | Optimized path; *no cycle detection* | Acyclic object graphs; performance-critical paths |
| ExpressionTree | Full validation, including cycle detection | Cyclic graphs (self-references, parent-child bidirectional links) |
| JSON | Serialization + deserialization | Cross-assembly scenarios; intermediate filtering/transformation required |

Selection guidance:  
- **Default**: Prefer source generator unless constraints apply.  
- **Cyclic graphs or unresolvable types** (e.g., `object`, interfaces, `dynamic`): Switch to `ExpressionTree` or `JSON`.  
- **Intermediate filtering/transformation needed**: Use `JSON`.  
- **Performance-sensitive & acyclic**: Default path suffices (source generator is optimal).  

Strategy switching is global (`DeepCopyProviderBase.Current`). Always restore the prior value in `finally` (as shown in sample code) to avoid affecting subsequent calls.

Decision table:

| Question | Decision |
|----------|----------|
| Might the object graph contain cycles (self-reference, parent-child mutual reference)? | Yes → `ExpressionTree`; No → default |
| Does the type contain unresolvable references (`object`, interface, `dynamic`)? | Yes → `ExpressionTree` or `JSON` |
| Is intermediate transformation/filtering required during copy? | Yes → `JSON` |
| Is performance critical *and* the graph acyclic? | Default path already optimal (source generator first) |

Always wrap strategy switches in `try/finally` to restore `Current`, preventing downstream impact (demonstrated in samples).

---

## 5. Deep Copying Collections

When the object graph contains collections, `DeepCopy` clones each element individually:

[!code-csharp[List deep copy](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyJson)]

Supported collection types include `List<T>`, arrays, `Dictionary<K,V>`, and `HashSet<T>`: new containers are created, elements are independently cloned, and no references are shared with the source collection.

| Collection type | Copy behavior |
|-----------------|----------------|
| `List<T>` | New list; elements independently cloned |
| `T[]` | New array; elements independently cloned |
| `Dictionary<K,V>` | New dictionary; keys preserved; values independently cloned |
| `HashSet<T>` | New set; elements independently cloned |

Nested combinations (e.g., collections containing collections, or elements with navigation properties) are handled recursively. In snapshot scenarios, associated collections (e.g., order line items) are fully and independently copied — restoring them leaves no trace of edits.

---

## 6. Recursion Depth Limit

Recursive copying enforces a depth limit (default: `Mapper.MaxRecursionDepth`) to prevent runaway recursion on deeply nested or cyclic structures:

[!code-csharp[Depth limit demo](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_MaxRecursionDepth)]

Exceeding the limit throws `MapException`. Production object graphs rarely hit this ceiling; if triggered, first inspect for *unintended cycles* (mutual object references) rather than blindly increasing the limit.

Guidelines for adjusting depth limit:

- Increase *only* when your domain model genuinely exceeds the default (e.g., extremely deep configuration trees).
- Raising the limit increases stack usage and recursion overhead — use sparingly.
- Increasing the limit does *not* resolve cyclic structures — break cycles first (see Common Pitfalls #2).

## Complete Snapshot Flow for Edit Pages

Combining concepts from this tutorial, here’s the full flow for an edit-page snapshot:

```text
1. Open edit page: original = Mapper.DeepCopy(entity)     // Take snapshot
2. User edits:      Form binds to entity — fields modified
3. Save:            Validation passes → submit entity, discard snapshot
4. Cancel:          Restore entity from original (or reload)
```

Key points:

- Create the snapshot *once* on page load — never rebuild after every edit.
- Explicitly discard the snapshot after successful save to avoid memory retention.
- On restore, either replace the current object with the snapshot *or* overwrite fields individually using snapshot values.

---

## Run & Verify

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 401
```

Scenario 401 outputs results for same-type copy, callback usage, all three strategies, collection handling, and depth-limit testing.

---

## Common Pitfalls

| Pitfall | Symptom | Fix |
|---------|---------|-----|
| Using reference assignment instead of deep copy | Snapshot changes during editing — cancel/restore fails | Use `Mapper.DeepCopy` to create a truly independent copy |
| Using Fast strategy on cyclic graphs | Infinite recursion or `MapException` due to depth overflow | Switch to `ExpressionTree` strategy (includes cycle detection) |
| Assigning source’s reference properties inside callback | Copy shares nested references with source — independence lost | Callbacks should modify only value types or the copy itself — never assign source references |

---

## Quick Reference Table

| Scenario | API | Description |
|----------|-----|-------------|
| Basic deep copy | `Mapper.DeepCopy(src)` | Recursively clones the object graph into an independent copy |
| Post-clone processing | `Mapper.DeepCopy(src, (s, t) => ...)` | Callback to mutate the copy after cloning |
| Strategy switching | `DeepCopyProviderBase.Current = new XxxProvider()` | Choose `Fast` / `ExpressionTree` / `JSON` |
| Collection deep copy | `Mapper.DeepCopy(list)` | New container; elements independently cloned |
| Recursion depth limit | `Mapper.MaxRecursionDepth` | Protection against infinite recursion |

For snapshot scenarios, follow the simple workflow: *snapshot on open → discard on save → restore on cancel*. Strategy switching is needed *only* when encountering cycles or requiring intermediate filtering.

For advanced deep-copy edge cases (AOT fallback, cycle protection details), see the corresponding sections in [Deep Copy](../how-to/deep-copy/deep-copy.md).

---

## Related Documentation

- [Deep Copy](../how-to/deep-copy/deep-copy.md): Strategy mechanics and AOT behavior  
- [Quick Start](../getting-started/tutorial-15min.md): 15-minute foundational mapping walkthrough  
- [Multi-Source Composition Scenario Walkthrough](./compose-tutorial.md): Alternative approach for aggregating DTOs  
- [ORM Projection Scenario Walkthrough](./orm-projection-tutorial.md): Data assembly on query side  
- [Three Principles of Mapping Configuration Ownership](../explanation/semantics/mapping-config-ownership.md)
