<!-- 来源：NCMapper 主技术文档 §6.6-6.7 Action 回调与集合映射 -->
### 6.6 Action 回调

所有映射方法均支持可选的 `Action<TSource, TTarget>` 回调参数，在映射核心完成后对目标对象执行自定义处理。Action 为 null 时完全跳过，零开销。

#### 执行时机

```
PostMap（按注册逆序）
```

Action 在映射核心（属性赋值）**完成之后**、Pipeline PostMap **之前**执行。此时目标对象的所有属性已被赋值，Action 可基于源对象做额外计算或覆盖：

[!code-csharp[基础映射：同名属性直赋](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_SimpleNameMatch)]

#### Null 零开销 — 双委托分离

NCMapper 为每个映射对注册**两个独立委托**，Action 为 null 时走无 Action 参数的 Core 路径，完全不执行 null 检查：

| 委托         | 签名                                      | 触发条件                              |
| ---------- | --------------------------------------- | --------------------------------- |
| `CoreFunc` | `Func<TSource, TTarget?>`               | `action == null`（热路径，零 Action 开销） |
| `Func`     | `Func<TSource, Action<S,T>?, TTarget?>` | `action != null`                  |

```csharp
// Mapper.Map 内部分发逻辑（简化）
if (action == null && CoreFunc != null)
    return CoreFunc(source);          // 无 Action 参数，无 null 检查
else if (Func != null)
    return Func(source, action);      // 内部: Core() → action?.Invoke(src, dst)
```

> 这意味着不传 Action 的调用（绝大多数场景）与无 Action 参数的方法性能完全一致，不存在 `if (action != null)` 分支预测开销。

#### MapList 中的 Action

列表映射时，Action **对每个元素执行**（非仅一次）：

[!code-csharp[列表映射 MapList](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_ListMap)]

| 场景                              | 行为                                  |
| ------------------------------- | ----------------------------------- |
| `action == null` + 源为 `List<T>` | 走 `ListFunc` 批量路径（内联 for 循环，无逐元素回调） |
| `action != null`                | 逐元素映射后执行 `action(element, dto)`     |
| 元素为 null                        | 保留 null，跳过 Action                   |

#### 更新已有对象

`Map(source, target, action)` 中，Action 在所有属性赋值到已有 target 之后执行：

[!code-csharp[映射到已有目标对象](~/samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs#BasicMapping_UpdateExisting)]

#### 与 Pipeline 的关系

| 维度   | Action 回调         | Pipeline PostMap            |
| ---- | ----------------- | --------------------------- |
| 作用域  | 单次调用              | 全局/类型对过滤                    |
| 执行顺序 | 映射核心之后，PostMap 之前 | Action 之后（逆序）               |
| 定义方式 | 内联 lambda，一次性     | `AddPostMap<S,T>()` 注册，持久生效 |
| 适用场景 | 某次映射的特殊处理         | 跨多个映射对的横切关注点                |

> ✅ **扩展方法同样触发 Pipeline**：源生成扩展方法（如 `entity.MapToUserDto(action)`）在 `MapPipeline.IsPipelineEnabled` 为真时会自动触发 PostMap，与 `Mapper.Map(entity, action)` 行为一致；管道为空时仅做一次 `volatile` 读取（开销近乎为零），无需为了启用 Pipeline 而改用 `Mapper.Map`。

#### DeepCopy 中的 Action

[!code-csharp[同类深拷贝 + Action 回调](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyWithAction)]

#### 完整示例

```csharp
// 基础用法 — 映射后组合字段
var dto = Mapper.Map<OrderEntity, OrderDto>(order, (src, tgt) =>
{
    tgt.Summary = $"{tgt.OrderNo} - {tgt.Amount:C}";
});

// 列表 + Action — 每个元素添加序号
int index = 0;
var dtos = Mapper.MapList<ItemEntity, ItemDto>(items, (src, tgt) =>
{
    tgt.SortOrder = ++index;
});

// 扩展方法 + Action（同样会触发 PostMap）
using NCMapper.Generated;
var dto2 = order.MapToOrderDto((src, tgt) =>
{
    tgt.Remark = "via extension";
});

// 更新已有对象 + Action
Mapper.Map(order, existingDto, (src, tgt) =>
{
    tgt.LastSyncAt = DateTime.UtcNow;
});
```

### 6.7 集合映射

当源属性与目标属性都是集合类型时（如 `List<OrderItemEntity> → List<OrderItemDto>`），NCMapper 自动识别集合元素类型并逐元素递归映射。同类型集合走浅拷贝快速路径，跨类型集合走递归映射路径。

#### 同类型 vs 跨类型

| 场景                        | 行为                      | 说明                                         |
| ------------------------- | ----------------------- | ------------------------------------------ |
| 同类型 `List<T> → List<T>`   | **独立副本**（新容器 + 元素逐项拷贝）     | `CollectionCopier.CopyList`，全新 List 含独立元素 |
| 同类型 `T[] → T[]`           | **独立副本**                 | `Array.Clone()` 后逐元素拷贝，元素独立             |
| 跨类型 `List<TS> → List<TT>` | **递归映射**（每个元素调用 Map）    | 元素创建新实例，引用属性独立                             |
| 跨类型 `TS[] → TT[]`         | **递归映射** + `.ToArray()` | 先映射为 List 再转数组                             |

> **同类型集合复制**：`Mapper.Map` 路径下生成侧同类型集合为**独立副本**（新容器 + 元素逐项拷贝），修改拷贝集合中的引用元素**不会**影响原集合；需要共享引用语义时可显式使用 `Mapper.DeepCopy` 获取深拷贝。

#### 支持的集合类型

**跨类型映射（元素递归 Map）**：

| 源类型                                     | 目标类型                                              |       支持       |
| --------------------------------------- | ------------------------------------------------- | :------------: |
| `List<T>` / `T[]` / 任何 `IEnumerable<T>` | `List<TTarget>`                                   |        ✅       |
| 任何 `IEnumerable<T>`                     | `TTarget[]`                                       |        ✅       |
| 任何 `IEnumerable<T>`                     | `ISet<TTarget>` / `IReadOnlySet<TTarget>`         |  ✅（→ HashSet）  |
| 任何 `IEnumerable<T>`                     | `IReadOnlyList<TTarget>` / `IEnumerable<TTarget>` | ✅（→ List 向上转型） |
| `Dictionary<K,VS>`                      | `Dictionary<K,VT>`                                |  ✅（Value 递归映射） |

**同类型拷贝（CollectionCopier 覆盖）**：

`List<T>`、`T[]`、`HashSet<T>`、`Queue<T>`、`Stack<T>`、`Dictionary<K,V>`、`ConcurrentDictionary<K,V>`、`ConcurrentQueue<T>`、`ConcurrentStack<T>`、`ReadOnlyCollection<T>`、`SortedSet<T>`、`LinkedList<T>`、`ObservableCollection<T>`、`ConcurrentBag<T>`、`SortedDictionary<K,V>`、`SortedList<K,V>`、`ReadOnlyDictionary<K,V>`

> **基元异类型集合**：`List<int> → List<long>`、`HashSet<short> → HashSet<int>` 等基元异类型集合统一以**基类元素类型**（`List<TElement>`/`IEnumerable<TElement>`）承载映射，运行时按 `TypeConvertHelper` 逐元素数值转换。

#### 源生成器代码生成

源生成器为跨类型集合生成三层快速路径的 MapList 方法：

```csharp
// 生成代码（简化）
public static List<TTarget>? MapList_X_To_Y(IEnumerable<TSource> sources)
{
    if (sources is null) return null;

    // 路径 1：数组 — for 循环 + 预分配（JIT 可消除边界检查）
    if (sources is TSource[] arr)
    {
        var list = new List<TTarget>(arr.Length);
        for (int i = 0; i < arr.Length; i++)
        {
            var src = arr[i];
            if (src is null) { list.Add(null!); continue; }
            var dst = new TTarget { Prop = src.Prop, ... };  // 内联元素映射
            list.Add(dst);
        }
        return list;
    }

    // 路径 2：ICollection — foreach + 预分配
    if (sources is ICollection<TSource> coll)
    {
        var list = new List<TTarget>(coll.Count);
        foreach (var src in coll) { /* 内联映射 */ }
        return list;
    }

    // 路径 3：纯 IEnumerable — foreach 无预分配
    {
        var list = new List<TTarget>();
        foreach (var src in sources) { /* 内联映射 */ }
        return list;
    }
}
```

#### Null 与空集合行为

| 输入          | 输出                 |
| ----------- | ------------------ |
| 源集合为 null   | 目标属性赋 null         |
| 源为空集合       | 目标为新的空集合实例（非 null） |
| 集合中元素为 null | 保留为 null（保持索引一致性）  |

> **Update 路径集合 null 语义**：`Mapper.Update` 对目标已有集合——若**源集合为 null**，则**清空目标旧集合**（保证目标状态与源一致）；源为空集合时赋新空实例。跨类型集合 Update 同样适用。

#### 源生成器 vs 运行时

| 维度     | 源生成器                                 | 运行时引擎                   |
| ------ | ------------------------------------ | ----------------------- |
| 同类型集合  | 直接赋值 `dst.Prop = src.Prop`（共享引用）     | 浅拷贝（新容器，元素共享）           |
| 跨类型循环  | 数组 for / ICollection foreach（内联元素映射） | 编译的 MapListFunc（for 循环） |
| 预分配容量  | ✅（arr.Length / coll.Count）           | ✅（collection.Count）     |
| 接口目标   | ISet→HashSet，其他→List 直接赋值            | Expression.Convert 向上转型 |
| 首次延迟   | 零                                    | 首次 Expression.Compile() |
| AOT 兼容 | 完全兼容                                 | 需要 DynamicCode          |

#### 示例

```csharp
public class OrderEntity
{
    public int Id { get; set; }
    public List<OrderItemEntity> Items { get; set; }       // 跨类型集合
    public string[] Tags { get; set; }                      // 同类型数组
    public Dictionary<string, AddressEntity> Addresses { get; set; }  // 字典
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public int Id { get; set; }
    public List<OrderItemDto> Items { get; set; }          // 逐元素递归映射
    public string[] Tags { get; set; }                      // 浅拷贝（同类型）
    public Dictionary<string, AddressDto> Addresses { get; set; }  // Value 递归映射
}

// 映射
var dto = Mapper.Map<OrderEntity, OrderDto>(entity);
// Items: 每个 OrderItemEntity → OrderItemDto（递归映射，独立实例）
// Tags: 浅拷贝（新数组，字符串元素共享——string 不可变所以安全）
// Addresses: Key 不变，Value 递归映射 AddressEntity → AddressDto
```

---

