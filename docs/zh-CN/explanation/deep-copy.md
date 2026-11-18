<!-- 来源：NCMapper 主技术文档 §8 深拷贝体系 -->
## 8. 深拷贝体系

NCMapper 的深拷贝体系是一套**多策略、可替换**的深拷贝引擎：递归复制对象图的所有层级（POCO、record、嵌套对象、集合、字典），生成与源对象完全独立的新实例。支持同类型深拷贝（`DeepCopy<T>`）与异类型深拷贝（`DeepCopy<TSource,TTarget>`，转发到 `Map`）。深拷贝策略在运行时由 `DeepCopyTypeClassifier` 对目标类型做 Acyclic / Cyclic / Unknown 三值分类，据此选择零检测快路径或完整循环引用检测路径（详见 §8.1）。

.NET 原生深拷贝长期缺乏统一方案：`ICloneable` 已废弃且不保证深拷贝；`BinaryFormatter` 因安全问题在 .NET 5+ 被移除；JSON 序列化往返性能差且破坏类型信息；手动逐属性复制样板代码多、易漏字段。NCMapper 提供声明式、高性能、类型安全的深拷贝——`[MapFrom]` 标注后源生成器编译期生成深拷贝代码，运行时再以表达式树编译 + 多级降级兜底。

**核心亮点** —

| 维度     | 说明                                                                                         |
| ------ | ------------------------------------------------------------------------------------------ |
| 多策略可替换 | 默认 `FastDeepCopyProvider`（三值分类条件分发：Acyclic ~401ns / Cyclic-Unknown 转发基类完整检测，任意环长）；可选 `ExpressionTreeDeepCopyProvider`（含完整循环引用检测 + DAG 共享引用保持，~409ns） |
| 源生成器路径 | 编译期生成，零委托，~179ns（net10.0 Linux 口径），AOT 友好（最快）                                                        |
| 全类型覆盖  | POCO、record（构造器参数匹配）、嵌套对象、集合、字典                                                            |
| 安全保障   | `MaxRecursionDepth` 递归深度保护防栈溢出；失败抛 `MapException` 不返回浅拷贝                                   |
| 异类型深拷贝 | `DeepCopy<TSource,TTarget>` 转发 `Map`，复用映射规则                                                |

> 下方 8.1 策略体系展示深拷贝的完整分发路径。

### 8.1 策略体系

```
Mapper.DeepCopy<T>(source)
  │
  ├── [源生成器已注册]  → GeneratedMapper.DeepCopy_T()     (~179ns, 最快)
  │        源生成侧按三值分类（Acyclic/Cyclic/Unknown）条件化生成：
  │        Acyclic 省略 RegisterDeepCopyTarget；record Cyclic/Unknown 回退 DeepCopyRuntimeOnly
  │
  ├── [默认] → DeepCopyProviderBase.Current
  │            └── FastDeepCopyProvider（默认策略，三值分类条件分发）
  │                  ├── Acyclic: 零检测快路径 ~401ns（环扫描仅作分类假阴性兜底）
  │                  ├── Cyclic/Unknown: 转发基类完整检测（CycleCache + record 占位，任意环长）
  │                  ├── POCO: new T() + 逐属性递归深拷贝
  │                  ├── record: new T(ctor args) 参数匹配（Unknown 回退运行时路径）
  │                  └── MaxRecursionDepth 深度保护（超限抛 MapException，不会栈溢出）
  │
  ├── [有循环引用 + 需 DAG 共享保持] → ExpressionTreeDeepCopyProvider（~409ns，含循环引用检测 + DAG 共享引用保持）
  │
  └── [回退] → JSON 序列化（表达式树编译失败时降级，全部失败时抛出 MapException）
```

> ⚠ **DAG 共享引用**：Fast 路径 Acyclic 类型的 DAG 共享引用为"逐处独立复制"。
>
> 如需保留 DAG 共享引用（同一源实例被多处引用，拷贝后仍共享），请改用 `ExpressionTreeDeepCopyProvider`。
>
> ⚠️ **深拷贝失败行为**：所有降级路径（JSON 序列化、CopyEnumerable 无构造器、
>
> CopyDictionaryByKeyValue 非双泛型）失败时均抛出 MapException，不返回原引用（浅拷贝）。

### 8.2 策略选择

| 策略                             |  性能 | 循环引用检测 | 接口/抽象类 |  默认 |
| ------------------------------ | :-: | :----: | :----: | :-: |
| FastDeepCopyProvider           | ★★★ |   支持（任意环长；不保持 DAG 共享）   |    ❌   |  ✅  |
| ExpressionTreeDeepCopyProvider | ★★☆ |    ✅   |    ❌   |  —  |
| 自定义 JSON 策略                    | ★☆☆ |    ❌   |    ✅   |  —  |

```csharp
// 默认使用 FastDeepCopyProvider（三值分类条件分发，Acyclic 零检测开销）
// 需 DAG 共享引用保持时切换 ExpressionTreeDeepCopyProvider
DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();

// 自定义 JSON 序列化策略
JsonSerializerBase.Current = new MyCustomJsonSerializer();
```

### 8.3 集合深拷贝覆盖

| 类型                         | 方法                                        | 深/浅 |
| -------------------------- | ----------------------------------------- | :-: |
| T[]                        | DeepCopyArray                             |  深  |
| List\<T>                   | DeepCopyList                              |  深  |
| Dictionary\<K,V>           | CopyDictionary                            |  深  |
| ConcurrentDictionary\<K,V> | CopyConcurrentDictionary                  |  深  |
| HashSet\<T>                | CopyHashSet                               |  深  |
| Queue\<T>                  | CopyQueue                                 |  深  |
| Stack\<T>                  | CopyStack                                 |  深  |
| ConcurrentQueue/Stack\<T>  | CopyConcurrent*                           |  深  |
| ReadOnlyCollection\<T>     | CopyReadOnlyCollection                    |  深  |
| SortedSet/Dict 等           | CopyEnumerable / CopyDictionaryByKeyValue |  深  |

### 8.4 DeepCopy API 矩阵

| 方法                                                               | 用途         | 说明                                      |
| ---------------------------------------------------------------- | ---------- | --------------------------------------- |
| `DeepCopy<T>(T source)`                                          | 同类全深拷贝     | 递归创建所有引用属性的独立副本                         |
| `DeepCopy<T>(T source, Action<T,T>? action)`                     | 同类深拷贝 + 回调 | 深拷贝后执行 action 调整结果 ⚠️（见下方警示）            |
| `DeepCopy<TSource,TTarget>(TSource source)`                      | 异类深拷贝      | 转发 `Map<TSource,TTarget>`，不同类型引用属性天然深拷贝 |
| `DeepCopy<TSource,TTarget>(TSource source, Action<S,T>? action)` | 异类深拷贝 + 回调 | 同上 + 回调 ⚠️（见下方警示）                       |

> **注意**：`DeepCopy<TSource,TTarget>` 当 `TSource == TTarget` 时抛 `InvalidOperationException`，引导用户使用 `DeepCopy<T>`。

> **⚠️ action 引用赋值陷阱**：两个带 `Action` 回调的重载同时接收 source 和 copy/target 参数。在 action 中将 source 的**引用类型属性**直接赋值给 copy/target（如 `tgt.Address = src.Address`）会**破坏深拷贝语义**，导致双方共享同一引用对象。正确做法：action 中仅修改值类型字段；若需传递引用属性，应对其再次调用 `Mapper.DeepCopy` 创建独立副本（如 `tgt.Address = Mapper.DeepCopy(src.Address!)`）。启用 `MapTracer.GloballyEnabled = true` 可在 action 执行前输出警示信息。

**典型应用场景** —

| 场景      | 说明                          |
| ------- | --------------------------- |
| 防御性拷贝   | 方法接收外部对象时先深拷贝，避免调用方修改影响内部状态 |
| 缓存快照    | 缓存前深拷贝，确保缓存中的对象不被后续修改污染     |
| 撤销/重做   | 操作前保存深拷贝副本，用于回滚到历史状态        |
| 对象图独立修改 | 需要基于原对象派生多个独立副本分别修改时        |
| 单元测试夹具  | 测试间共享对象时深拷贝，保证测试隔离          |

```csharp
// 防御性拷贝 — 方法内部修改不影响调用方的原始对象
public void ProcessOrder(OrderEntity order)
{
    var snapshot = Mapper.DeepCopy(order);  // 深拷贝，内部操作副本
    snapshot.Items.Add(new OrderItem { /* ... */ });
    // 调用方的 order 不受影响
}
```

---

