> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §6.6–6.7 Action Callbacks and Collection Mapping -->

### 6.6 Action Callbacks

All mapping methods support an optional `Action<TSource, TTarget>` callback parameter, enabling custom processing of the target object after the core mapping completes. When `action` is `null`, the callback is entirely skipped—zero overhead.

#### Execution Timing

```
PostMap (executed in reverse registration order)
```

The `Action` executes **after** the core mapping (i.e., property assignment) but **before** any Pipeline `PostMap` handlers. At this point, all properties of the target object have already been assigned, so the `Action` may perform additional computation based on the source object—or even overwrite previously assigned values:

[!code-csharp[Basic mapping: direct name-matching assignment](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

#### Null = Zero Overhead — Dual Delegate Separation

NCMapper registers **two independent delegates** per mapping pair. When `action` is `null`, NCMapper uses the `CoreFunc` path (which omits the `Action` parameter entirely), avoiding any null-check branching:

| Delegate     | Signature                                      | Trigger Condition                              |
| ------------ | ---------------------------------------------- | ---------------------------------------------- |
| `CoreFunc`   | `Func<TSource, TTarget?>`                      | `action == null` (hot path; zero Action overhead) |
| `Func`       | `Func<TSource, Action<S,T>?, TTarget?>`        | `action != null`                               |

```csharp
// Internal dispatch logic inside Mapper.Map (simplified)
if (action == null && CoreFunc != null)
    return CoreFunc(source);          // No Action parameter; no null check
else if (Func != null)
    return Func(source, action);      // Internally: Core() → action?.Invoke(src, dst)
```

> This means that calls omitting the `Action` parameter (the vast majority of usage scenarios) achieve identical performance to a method signature with no `Action` parameter at all—no `if (action != null)` branch-prediction penalty is incurred.

#### Action in `MapList`

When mapping collections, the `Action` is invoked **once per element**, not once per collection:

[!code-csharp[List mapping via MapList](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ListMap)]

| Scenario                                  | Behavior                                                                 |
| ----------------------------------------- | ------------------------------------------------------------------------ |
| `action == null` + source is `List<T>`    | Uses the batched `ListFunc` path (inlined `for` loop; no per-element callback) |
| `action != null`                          | Maps each element individually, then invokes `action(element, dto)`     |
| Element is `null`                           | Preserves `null`; skips `Action` invocation                             |

#### Updating Existing Objects

In `Map(source, target, action)`, the `Action` executes *after* all properties have been assigned to the pre-existing `target`:

[!code-csharp[Mapping into an existing target object](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_UpdateExisting)]

#### Relationship with Pipeline

| Dimension     | Action Callback                     | Pipeline `PostMap`                            |
| --------------| ------------------------------------- | --------------------------------------------- |
| Scope           | Per-call                              | Global / per-type-pair filtering              |
| Execution Order | After core mapping, before `PostMap`  | After `Action` (reverse registration order)   |
| Definition      | Inline lambda (one-time use)          | Registered via `AddPostMap<S,T>()` (persistent) |
| Use Case        | Special handling for a single mapping   | Cross-cutting concerns across multiple mappings |

> ✅ **Extension methods also trigger Pipeline**: Source-generated extension methods (e.g., `entity.MapToUserDto(action)`) automatically invoke `PostMap` when `MapPipeline.IsPipelineEnabled` is `true`, behaving identically to `Mapper.Map(entity, action)`. When no pipeline handlers are registered, only a single `volatile` read occurs (overhead is virtually zero)—there is no need to switch to `Mapper.Map` solely to enable Pipeline functionality.

#### Action in `DeepCopy`

[!code-csharp[Deep copy of same-type objects + Action callback](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyWithAction)]

#### Complete Examples

```csharp
// Basic usage — compose fields after mapping
var dto = Mapper.Map<OrderEntity, OrderDto>(order, (src, tgt) =>
{
    tgt.Summary = $"{tgt.OrderNo} - {tgt.Amount:C}";
});

// List mapping + Action — assign sequential index to each element
int index = 0;
var dtos = Mapper.MapList<ItemEntity, ItemDto>(items, (src, tgt) =>
{
    tgt.SortOrder = ++index;
});

// Extension method + Action (also triggers PostMap)
using NCMapper.Generated;
var dto2 = order.MapToOrderDto((src, tgt) =>
{
    tgt.Remark = "via extension";
});

// Update existing object + Action
Mapper.Map(order, existingDto, (src, tgt) =>
{
    tgt.LastSyncAt = DateTime.UtcNow;
});
```

### 6.7 Collection Mapping

When both the source and target properties are collection types (e.g., `List<OrderItemEntity>` → `List<OrderItemDto>`), NCMapper automatically infers the element type and recursively maps each element. Same-type collections take a fast shallow-copy path; cross-type collections follow the recursive mapping path.

#### Same-Type vs Cross-Type Collections

| Scenario                         | Behavior                                    | Notes                                                                 |
| -------------------------------- | ------------------------------------------- | --------------------------------------------------------------------- |
| Same-type `List<T>` → `List<T>`   | **Independent copy** (new container + element-wise copy) | Via `CollectionCopier.CopyList`: new `List<T>` containing independent elements |
| Same-type `T[]` → `T[]`           | **Independent copy**                          | `Array.Clone()` followed by element-wise copy; elements are independent         |
| Cross-type `List<TS>` → `List<TT>` | **Recursive mapping** (each element invokes `Map`) | New instances created per element; reference-typed properties are independently mapped |
| Cross-type `TS[]` → `TT[]`         | **Recursive mapping** + `.ToArray()`        | First maps to `List<TT>`, then converts to array                        |

> **Same-type collection copying**: In the `Mapper.Map` path, same-type collections are copied as **independent copies** (new container + element-wise copy). Modifying reference-type elements *within the copied collection* will **not** affect the original collection. To obtain shared-reference semantics (i.e., deep cloning), explicitly use `Mapper.DeepCopy`.

#### Supported Collection Types

**Cross-type mapping (element-wise recursive `Map`)**:

| Source Type                                     | Target Type                                              | Supported |
| ----------------------------------------------- | ---------------------------------------------------------- | :-------: |
| `List<T>` / `T[]` / any `IEnumerable<T>`      | `List<TTarget>`                                            |     ✅     |
| Any `IEnumerable<T>`                            | `TTarget[]`                                                |     ✅     |
| Any `IEnumerable<T>`                            | `ISet<TTarget>` / `IReadOnlySet<TTarget>`                  | ✅ (→ `HashSet<TTarget>`) |
| Any `IEnumerable<T>`                            | `IReadOnlyList<TTarget>` / `IEnumerable<TTarget>`          | ✅ (→ `List<TTarget>` upcast) |
| `Dictionary<K,VS>`                              | `Dictionary<K,VT>`                                         | ✅ (values recursively mapped) |

**Same-type copying (handled by `CollectionCopier`)**:

`List<T>`, `T[]`, `HashSet<T>`, `Queue<T>`, `Stack<T>`, `Dictionary<K,V>`, `ConcurrentDictionary<K,V>`, `ConcurrentQueue<T>`, `ConcurrentStack<T>`, `ReadOnlyCollection<T>`, `SortedSet<T>`, `LinkedList<T>`, `ObservableCollection<T>`, `ConcurrentBag<T>`, `SortedDictionary<K,V>`, `SortedList<K,V>`, `ReadOnlyDictionary<K,V>`

> **Primitive heterogeneous collections**: Mappings like `List<int>` → `List<long>` or `HashSet<short>` → `HashSet<int>` are uniformly handled using the base element type (`List<TElement>` / `IEnumerable<TElement>`), with runtime numeric conversion applied per-element via `TypeConvertHelper`.

#### Source Generator Code Generation

The source generator emits optimized three-path `MapList` methods for cross-type collections:

```csharp
// Generated code (simplified)
public static List<TTarget>? MapList_X_To_Y(IEnumerable<TSource> sources)
{
    if (sources is null) return null;

    // Path 1: Array — for-loop + pre-allocation (JIT can eliminate bounds checks)
    if (sources is TSource[] arr)
    {
        var list = new List<TTarget>(arr.Length);
        for (int i = 0; i < arr.Length; i++)
        {
            var src = arr[i];
            if (src is null) { list.Add(null!); continue; }
            var dst = new TTarget { Prop = src.Prop, ... };  // Inlined element mapping
            list.Add(dst);
        }
        return list;
    }

    // Path 2: ICollection — foreach + pre-allocation
    if (sources is ICollection<TSource> coll)
    {
        var list = new List<TTarget>(coll.Count);
        foreach (var src in coll) { /* inlined mapping */ }
        return list;
    }

    // Path 3: Pure IEnumerable — foreach without pre-allocation
    {
        var list = new List<TTarget>();
        foreach (var src in sources) { /* inlined mapping */ }
        return list;
    }
}
```

#### Null and Empty Collection Behavior

| Input                    | Output                                |
| ------------------------ | ------------------------------------- |
| Source collection is `null` | Target property assigned `null`         |
| Source is empty collection | Target assigned a new empty collection instance (non-`null`) |
| Element within collection is `null` | Preserved as `null` (maintains index consistency) |

> **Null semantics in `Update` path**: In `Mapper.Update`, when the target already holds a collection: if the **source collection is `null`**, the **existing target collection is cleared** (ensuring target state matches source exactly); if the source is an empty collection, a new empty collection instance is assigned. This behavior applies equally to cross-type collection updates.

#### Source Generator vs Runtime Engine

| Dimension         | Source Generator                                 | Runtime Engine                   |
| ------------------ | ------------------------------------------------ | ---------------------------------- |
| Same-type collections | Direct assignment `dst.Prop = src.Prop` (shared references) | Shallow copy (new container, shared elements) |
| Cross-type iteration | Array `for` / `ICollection` `foreach` (inlined element mapping) | Compiled `MapListFunc` (`for` loop) |
| Capacity pre-allocation | ✅ (`arr.Length` / `coll.Count`)                 | ✅ (`collection.Count`)            |
| Interface targets     | `ISet` → `HashSet`, others → `List` (direct assignment) | `Expression.Convert` upcast        |
| First-use latency     | Zero                                             | Initial `Expression.Compile()`   |
| AOT compatibility     | Fully compatible                                 | Requires `DynamicCode`             |

#### Example

```csharp
public class OrderEntity
{
    public int Id { get; set; }
    public List<OrderItemEntity> Items { get; set; }       // Cross-type collection
    public string[] Tags { get; set; }                      // Homogeneous array
    public Dictionary<string, AddressEntity> Addresses { get; set; }  // Dictionary
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public int Id { get; set; }
    public List<OrderItemDto> Items { get; set; }          // Element-wise recursive mapping
    public string[] Tags { get; set; }                      // Shallow copy (homogeneous type)
    public Dictionary<string, AddressDto> Addresses { get; set; }  // Recursive mapping of Value
}

// Mapping
var dto = Mapper.Map<OrderEntity, OrderDto>(entity);
// Items: Each OrderItemEntity → OrderItemDto (recursive mapping, independent instances)
// Tags: Shallow copy (new array; string elements are shared—safe because strings are immutable)
// Addresses: Keys remain unchanged; Values are recursively mapped from AddressEntity → AddressDto
```
