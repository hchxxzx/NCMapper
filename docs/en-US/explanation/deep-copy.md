> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Primary Technical Documentation §8 Deep Copy System -->

## 8. Deep Copy System

NCMapper’s deep copy system is a **multi-strategy, swappable** deep copy engine that recursively copies all levels of an object graph—including POCOs, `record`s, nested objects, collections, and dictionaries—to produce a completely independent new instance from the source object. It supports both same-type deep copying (`DeepCopy<T>`) and cross-type deep copying (`DeepCopy<TSource, TTarget>`), which delegates to `Map`. At runtime, the deep copy strategy is selected by `DeepCopyTypeClassifier`, which classifies the target type into one of three categories—`Acyclic`, `Cyclic`, or `Unknown`—and accordingly chooses either a zero-overhead fast path (for acyclic types) or a full cycle-detection path (see §8.1 for details).

.NET has long lacked a unified native deep copy solution: `ICloneable` is deprecated and does not guarantee deep copying; `BinaryFormatter` was removed in .NET 5+ due to security concerns; JSON serialization round-trips suffer from poor performance and loss of type fidelity; and manual per-property copying leads to verbose, error-prone boilerplate code. NCMapper delivers a declarative, high-performance, type-safe deep copy solution: after annotating source types with `[MapFrom]`, a source generator emits optimized deep copy code at compile time, while at runtime it falls back to expression tree compilation and multi-level fallbacks.

**Key Highlights** —

| Dimension         | Description                                                                                                                              |
|-------------------|------------------------------------------------------------------------------------------------------------------------------------------|
| Multi-strategy & Swappable | Default `FastDeepCopyProvider` (three-value classification dispatch: `Acyclic` ~401 ns / `Cyclic`-`Unknown` delegates to base-class full-cycle detection, supporting arbitrary cycle lengths); optional `ExpressionTreeDeepCopyProvider` (full cycle detection + DAG shared-reference preservation, ~409 ns) |
| Source Generator Path | Compile-time generation, zero delegates, ~179 ns (measured on net10.0 Linux), AOT-friendly (fastest)                                                               |
| Full Type Coverage | POCOs, `record`s (constructor argument matching), nested objects, collections, dictionaries                                                             |
| Safety Guarantees | `MaxRecursionDepth` prevents stack overflow; failures throw `MapException`—no shallow copy is ever returned                                     |
| Cross-Type Deep Copy | `DeepCopy<TSource, TTarget>` delegates to `Map`, reusing existing mapping rules                                                                |

> Section 8.1 below illustrates the complete dispatch flow of the deep copy system.

### 8.1 Strategy Architecture

```
Mapper.DeepCopy<T>(source)
  │
  ├── [Source generator registered]  → GeneratedMapper.DeepCopy_T()     (~179 ns, fastest)
  │        Generator emits conditional logic based on three-value classification (Acyclic/Cyclic/Unknown):
  │        Acyclic omits RegisterDeepCopyTarget; record Cyclic/Unknown falls back to DeepCopyRuntimeOnly
  │
  ├── [Default] → DeepCopyProviderBase.Current
  │            └── FastDeepCopyProvider (default strategy, three-value classification dispatch)
  │                  ├── Acyclic: zero-detection fast path ~401 ns (cycle scanning only used as a safety net against false negatives in classification)
  │                  ├── Cyclic/Unknown: delegates to base-class full-cycle detection (CycleCache + record placeholder, supports arbitrary cycle lengths)
  │                  ├── POCO: `new T()` + recursive per-property deep copy
  │                  ├── record: `new T(ctor args)` with constructor argument matching (Unknown falls back to runtime path)
  │                  └── `MaxRecursionDepth` protection (throws `MapException` on overflow; no stack overflow occurs)
  │
  ├── [With cycles + requires DAG shared-reference preservation] → ExpressionTreeDeepCopyProvider (~409 ns, includes cycle detection + DAG shared-reference preservation)
  │
  └── [Fallback] → JSON serialization (degrades when expression tree compilation fails; throws `MapException` if all fallbacks fail)
```

> ⚠ **DAG Shared References**: In the `Fast` path, DAG shared references for `Acyclic` types are copied *independently at each occurrence*.
>
> To preserve DAG shared references (i.e., ensure that multiple references to the same source instance result in multiple references to the *same copied instance*), use `ExpressionTreeDeepCopyProvider` instead.
>
> ⚠️ **Deep Copy Failure Behavior**: All fallback paths—including JSON serialization, `CopyEnumerable` (when no parameterless constructor exists), and `CopyDictionaryByKeyValue` (for non-dual-generic dictionaries)—throw `MapException` on failure. They never return the original reference (i.e., no shallow copy is ever returned).

### 8.2 Strategy Selection

| Strategy                             | Performance | Cycle Detection | Interfaces / Abstract Classes | Default |
|--------------------------------------|:-----------:|:---------------:|:-----------------------------:|:-------:|
| `FastDeepCopyProvider`           | ★★★ | ✅ (arbitrary cycle length; does *not* preserve DAG shared references) | ❌ | ✅ |
| `ExpressionTreeDeepCopyProvider` | ★★☆ | ✅ | ❌ | — |
| Custom JSON Strategy               | ★☆☆ | ❌ | ✅ | — |

```csharp
// Uses FastDeepCopyProvider by default (three-value classification dispatch; zero overhead for Acyclic)
// Switch to ExpressionTreeDeepCopyProvider when DAG shared-reference preservation is required
DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();

// Custom JSON serialization strategy
JsonSerializerBase.Current = new MyCustomJsonSerializer();
```

### 8.3 Collection Deep Copy Coverage

| Type                         | Method                                        | Deep/Shallow |
|------------------------------|-----------------------------------------------|:------------:|
| `T[]`                        | `DeepCopyArray`                               | Deep         |
| `List<T>`                    | `DeepCopyList`                                | Deep         |
| `Dictionary<K,V>`            | `CopyDictionary`                              | Deep         |
| `ConcurrentDictionary<K,V>`  | `CopyConcurrentDictionary`                    | Deep         |
| `HashSet<T>`                 | `CopyHashSet`                                 | Deep         |
| `Queue<T>`                   | `CopyQueue`                                   | Deep         |
| `Stack<T>`                   | `CopyStack`                                   | Deep         |
| `ConcurrentQueue<T>` / `ConcurrentStack<T>` | `CopyConcurrent*`                           | Deep         |
| `ReadOnlyCollection<T>`      | `CopyReadOnlyCollection`                      | Deep         |
| `SortedSet<T>` / `SortedDictionary<K,V>` etc. | `CopyEnumerable` / `CopyDictionaryByKeyValue` | Deep         |

### 8.4 `DeepCopy` API Matrix

| Method                                                               | Purpose              | Description                                                                                     |
|----------------------------------------------------------------------|----------------------|-------------------------------------------------------------------------------------------------|
| `DeepCopy<T>(T source)`                                          | Same-type deep copy     | Recursively creates independent copies of all reference-typed properties                            |
| `DeepCopy<T>(T source, Action<T,T>? action)`                     | Same-type deep copy + callback | Executes `action` after deep copy completes ⚠️ (see warning below)                                  |
| `DeepCopy<TSource,TTarget>(TSource source)`                      | Cross-type deep copy    | Delegates to `Map<TSource,TTarget>`; reference-typed properties across types are naturally deep-copied             |
| `DeepCopy<TSource,TTarget>(TSource source, Action<TSource,TTarget>? action)` | Cross-type deep copy + callback | Same as above, plus callback ⚠️ (see warning below)                                                  |

> **Note**: `DeepCopy<TSource,TTarget>` throws `InvalidOperationException` when `TSource == TTarget`, guiding users to prefer `DeepCopy<T>`.

> **⚠️ Action Reference Assignment Pitfall**: Both overloads accepting an `Action` receive both `source` and `copy`/`target` parameters. Assigning a **reference-typed property** from `source` directly to `copy`/`target` inside the `action` (e.g., `tgt.Address = src.Address`) **violates deep copy semantics**, causing both objects to share the same referenced instance. Correct practice: modify only value-type fields inside the `action`; to propagate reference-typed properties, explicitly deep-copy them again (e.g., `tgt.Address = Mapper.DeepCopy(src.Address!)`). Enabling `MapTracer.GloballyEnabled = true` logs a warning before executing the `action`.

**Typical Use Cases** —

| Scenario          | Description                                                                 |
|-------------------|-----------------------------------------------------------------------------|
| Defensive Copying | Deep-copy external objects upon method entry to prevent caller-side mutations from affecting internal state |
| Cache Snapshotting | Deep-copy before caching to ensure cached objects remain immutable against subsequent modifications       |
| Undo/Redo         | Save a deep copy before performing an operation, enabling rollback to prior state                       |
| Independent Object Graph Modifications | Generate multiple independent copies from a single source for parallel, isolated modifications         |
| Unit Test Fixtures | Deep-copy shared test objects between tests to guarantee isolation                                    |

```csharp
// Defensive Copy — Internal modifications do not affect the caller's original object
public void ProcessOrder(OrderEntity order)
{
    var snapshot = Mapper.DeepCopy(order);  // Deep copy; operate on the copy internally
    snapshot.Items.Add(new OrderItem { /* ... */ });
    // The caller's `order` remains unaffected
}
```

---
