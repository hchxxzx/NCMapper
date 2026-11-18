> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

---
title: Security Mechanisms
---

<!-- Source: NCMapper Main Technical Documentation §15 Security Mechanisms (structural reorganization; chapter-level migration on 2026-08-19; content not reordered) -->

## 15. Security Mechanisms

Object mapping can easily encounter issues in high-concurrency scenarios, complex object graphs, or when handling `null` inputs — such as stack overflows caused by circular references, delegate cache contention under concurrent calls, or `NullReferenceException`s triggered by `null` values. NCMapper includes multiple built-in safety mechanisms to ensure stable and reliable mapping behavior in production environments.

### 15.1 Circular Reference Protection

The runtime engine uses a `[ThreadStatic]` recursion depth counter: it increments by 1 upon entering each nested mapping level and decrements by 1 upon exiting. When the depth exceeds `MaxRecursionDepth` (default: 64), a `MapException` is thrown instead of allowing a `StackOverflowException`:

```csharp
// Circular reference: A.Child → B.Parent → A.Child → ...
// At depth 64, MapException is thrown (catchable), avoiding StackOverflow
try
{
    var dto = Mapper.Map<NodeEntity, NodeDto>(cyclicNode);
}
catch (MapException ex)
{
    // "Recursion depth exceeded MaxRecursionDepth(64); possible circular reference detected"
}
```

| Scenario              | Counting Behavior                     |
| --------------------- | --------------------------------------- |
| Nested object mapping | +1 on entry, −1 on exit                  |
| `MapList` iteration   | +1 per element, −1 after completion      |
| Source generator path | No counter (no circular reference risk at compile time) |

[!code-csharp[Adjusting maximum recursion depth (default 64, upper bound 4096)](~/samples/NCMapper.Samples/06-Infrastructure/SecurityScenarios.cs#Core_Security_RecursionDepth)]

### 15.2 Thread Safety

NCMapper’s static caches and delegate registrations are thread-safe:

| Mechanism                                             | Protected Object         | Implementation                                                                 |
| ----------------------------------------------------- | ------------------------ | -------------------------------------------------------------------------------- |
| `ConcurrentDictionary<(Type,Type), Delegate>`        | Source generator delegate cache | Thread-safe concurrent reads/writes                                            |
| `Interlocked.CompareExchange`                         | Expression tree compiled delegates | Atomic assignment; expression trees compiled only once per thread                |
| `ConcurrentDictionary<Type, Dictionary>`              | Property dictionary cache  | Thread-safe concurrent reads/writes                                            |
| `[ThreadStatic]` depth counter                        | Recursion depth            | Thread-isolated, lock-free                                                       |
| `lock (_sync)` + atomic version increment             | `MapPipeline.PostMap` registration/clearance | `AddPostMap`/`Clear` share the same lock; five operations — queue write, flag setting, version increment, reverse-cache cleanup — are atomically grouped to eliminate race conditions during concurrent registration (callbacks won’t be silently dropped; flags won’t be overwritten causing callbacks to never execute) |
| `ConcurrentDictionary` partitioned by declaring type  | Projection cache cleanup delegates, `Validate` registrations | Ensures cleanup delegates for EF Core and FreeSql coexist safely within the same process without overwriting each other; `Validate` registration dictionaries are partitioned to avoid cross-assembly conflicts |

> When multiple threads simultaneously invoke `Mapper.Map` for the same type pair for the first time, the expression tree *may* be compiled multiple times (though `Interlocked.CompareExchange` guarantees only one resulting delegate is ultimately used). This causes no exceptions or incorrect results.

### 15.3 Null Guards

NCMapper adopts a “silent return” strategy for `null` inputs and does **not** throw `ArgumentNullException`:

```csharp
Mapper.Map<UserEntity, UserDto>(null);    // → null (no exception thrown)
Mapper.MapList<UserEntity, UserDto>(null); // → null
Mapper.DeepCopy<UserEntity>(null);          // → null
```

| Input                 | Behavior                                      |
| --------------------- | --------------------------------------------- |
| `null` source object  | Returns `default` (`null` for reference types) |
| `null` source collection | Returns `null`                                 |
| `null` nested property | Assigns `null` to target nested property (does not instantiate empty objects) |
| `null` collection element | Preserves `null` (maintains index consistency) |

### 15.4 `[MapIgnore]` Directional Safety

The `Direction` enum in `[MapIgnore]` ensures ignore rules apply *only* to the specified direction and never unintentionally affect reverse mapping:

```csharp
[MapIgnore(Direction.ToDto)]       // Ignored only during Entity→DTO mapping; normal mapping applies for DTO→Entity
public string InternalNote { get; set; }

[MapIgnore(Direction.ToEntity)]    // Ignored only during DTO→Entity mapping
public string ComputedField { get; set; }

[MapIgnore]                         // Ignored bidirectionally (Direction.Both)
public string Temp { get; set; }
```
