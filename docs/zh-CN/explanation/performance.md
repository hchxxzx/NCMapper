<!-- 来源：NCMapper 主技术文档 §14 性能优化机制 -->
## 14. 性能优化机制

NCMapper 的映射性能接近手写代码——源生成器直调约 7~33 ns（视场景复杂度，net10.0 Linux 口径），源生成器深拷贝 ~179 ns。这靠的是贯穿编译期到运行时的多层优化：源生成器编译期生成零委托代码、JIT 泛型静态缓存按类型组合隔离实现零开销访问、表达式树编译缓存避免重复编译、MemberInit 简化表达式利于 JIT 内联、内联深度控制平衡展开深度与编译时间。

### 14.1 JIT 泛型静态缓存

```csharp
// 每个类型组合生成独立的静态字段（JIT 零开销访问）
private static class GeneratedCache<TSource, TTarget> {
    public static Func<TSource, TTarget?>? CoreFunc;            // 无 Action 热路径
    public static Func<TSource, Action<TSource,TTarget>?, TTarget?>? Func;  // 带 Action
    public static Func<List<TSource>, List<TTarget>>? ListFunc; // 列表
}
```

### 14.2 表达式树编译缓存

| 委托                            | 用途              |
| ----------------------------- | --------------- |
| `CoreFunc` / `Func` | 创建新对象映射（无 Action 热路径 / 带 Action） |
| `UpdateAction`                | 更新已有对象          |
| `ListFunc`                    | 列表映射（内联 for 循环） |

### 14.3 Projection 表达式缓存

各 ORM 扩展包的 ProjectionBuilder（`EfCoreProjectionBuilder` / `FreeSqlProjectionBuilder`）经内部 ProjectionStrategy 缓存构建的 Lambda 表达式。首次构建后重复调用零反射开销。

Compose 多表表达式缓存同理（`FreeSqlProjectionBuilder.s_composeCache` / `s_compose3Cache`），同一类型组合重复调用直接返回缓存实例。

### 14.4 MemberInit 优化

简单映射（无嵌套、无转换）生成 `new T { Prop = src.Prop }` 仅通过 MemberInit 表达式，避免 Block 变量赋值开销。JIT 更容易内联。

### 14.5 内联深度控制

嵌套对象在表达式树中内联展开（`MaxInlineDepth = 5`），超限回退到 `Mapper.Map<>` 委托调用，防止表达式树无限展开导致编译超时。

### 14.6 性能基准数据

> **测试环境**: Intel Xeon Silver 4314 @ 2.40GHz（2 插槽 / 40 逻辑核，基准锁定 0-19），62 GB 内存，Ubuntu 24.04.5 LTS，.NET 8.0/9.0/10.0，BenchmarkDotNet 0.14.0（5 次启动 × 15 次迭代，预热 3 次）  
> **测试日期**: 2026-10-07 07:49 ~ 15:16 UTC（全组件全框架全场景跑批，总耗时 7 小时 27 分）| 完整数据见《NCMapper全组件全框架全场景性能测试报告_20261007_Linux》  
> **口径说明**: 性能数值随测试机 CPU/OS/.NET 版本差异较大（跨机器同场景实测可差 1~2 倍），本节数值仅代表该 Linux 环境下的相对水平与量级，不作为跨环境绝对性能承诺。

#### 映射性能（net10.0，全场景跑批）

| 场景 | 源生成器直调 | API 调用 | 运行时表达式树 | 手写 | 说明 |
| ------------------------- | -------: | -------: | -------: | -------: | ------------------------- |
| Flat（9 字段平铺） | 20.36 ns | 23.94 ns | 34.76 ns | 19.97 ns | 最基础的属性同名映射 |
| NameDiff（异名） | 16.40 ns | 19.67 ns | 28.38 ns | 16.28 ns | 全部属性需 `[MapProperty]` 重定向 |
| Nested（嵌套对象） | 26.69 ns | 29.16 ns | 38.35 ns | 26.16 ns | 含内层对象递归映射 |
| Record（不可变记录） | 16.35 ns | 19.75 ns | 32.94 ns | 15.75 ns | 不可变类型，走构造器参数匹配 |
| Nullable（可空类型） | 13.30 ns | 16.94 ns | 27.35 ns | 14.25 ns | 可空/非可空混合映射 |
| AutoFlatten（扁平化） | 23.23 ns | 26.70 ns | 30.85 ns | 23.37 ns | 命名约定扁平化（`AddressCity`） |
| EnumString（枚举↔字符串） | 15.44 ns | 18.42 ns | 29.92 ns | 26.70 ns | 编译期生成 switch 表达式 |
| Action（带回调） | 32.41 ns | 37.41 ns | 46.22 ns | 34.45 ns | 带回调的完整映射 |
| Update（更新已有对象） | 7.06 ns | 10.36 ns | 18.56 ns | 8.22 ns | 不创建新对象，直接赋值 |
| BidirDE（双向 实体→DTO） | 24.62 ns | 26.80 ns | 34.51 ns | 22.94 ns | 双向映射（实体→DTO 方向） |
| BidirED（双向 DTO→实体） | 20.72 ns | 23.73 ns | 34.94 ns | 19.58 ns | 双向映射（DTO→实体 方向） |
| Collection（集合，100 元素） | 2.491 μs | 2.461 μs | 2.131 μs | 2.142 μs | `MapList` / 源生成列表扩展 |
| CollectionInto（入已有集合，100 元素） | — | 476.1 ns | 762.8 ns | — | 复用预分配目标，顶层零分配 |

> **「手写」列口径**：`ManualMapper` 手写逐字段赋值基准（`benchmarks/NCMapper.Benchmarks/Program.cs`，理论最优参照线），取同一 2026-10-07 全量跑批（同机，net10.0）的 rivals 段数据；vs Manual 百分比 = (NCMapper − 手写) / 手写，负值 = 比手写快。CollectionInto 无手写对位（无"复用预分配集合"的手写惯例）。完整 36 组对比与分位数据见上述完整报告。

**三条路径的含义**：

- **源生成器直调**：源生成器生成 `src.MapToXxxDto()` 等直调方法（扩展方法形态），JIT 内联后零委托开销，是性能最优路径。
- **API 调用**：通过 `Mapper.Map<S,T>(src)` 泛型 API 调用，经静态缓存查找委托，多一层间接。
- **运行时表达式树**：无 `[MapFrom]` 标注时自动走表达式树编译，首次有编译延迟，后续走缓存。

#### 深拷贝性能（三框架对比）

| 路径 | net8.0 | net9.0 | net10.0 | 说明 |
| ---- | -------: | -------: | ------: | --------------------------- |
| 源生成器 | 233.0 ns | 203.1 ns | 179.2 ns | 编译期生成逐字段拷贝代码，最快 |
| 运行时 | 530.5 ns | 456.1 ns | 400.9 ns | `FastDeepCopyProvider`，默认策略 |
| 表达式树 | 555.0 ns | 474.6 ns | 409.2 ns | 含循环引用检测，适合复杂对象图 |

（手写参照：87.0 / 84.4 / 70.6 ns；深拷贝竞品 MemoryPack / MessagePack / Json 数据见上述完整报告。）

#### 跨框架趋势

所有路径均随 .NET 版本升级而改善。以深拷贝表达式树路径为例：net8.0 555.0 ns → net9.0 474.6 ns → net10.0 409.2 ns，net8.0 → net10.0 提升约 26%，主要受益于 JIT 内联策略和去虚化改进；映射路径（源生成器直调 / API 调用 / 运行时）net10.0 相对 net8.0 普遍下降（约 3~73%，多数超过 10%），其中运行时表达式树路径改善最显著（如 Update 69.85 ns → 18.56 ns，-73%；Action 96.34 ns → 46.22 ns，-52%）。

#### 竞品对比（12 可比场景 × 3 框架）

- 自家最快路 **15/36 组快于竞品最快**，21 组在竞品最快 ±15% 以内，0 组落后超过 15%；
- **34/36 组达到手写实现的 1.10 倍以内**；
- EnumString 转换比手写快约 42%；Update 比 Mapster 快 2~3 倍、比 AutoMapper 快近 9 倍（Update 场景 Mapperly 无实现）；
- 唯一相对偏弱的场景是 Collection：直调路径比手写慢约 16%，但最快路径（运行时表达式树）与手写、Mapster、Mapperly 差距都在 5% 以内。

**逐场景对比（net10.0，单次操作均值，2026-10-07 跑批）**：

| 场景 | NCMapper 直调 | Mapperly | Mapster | AutoMapper | 手写 |
| ---- | -------: | -------: | -------: | -------: | -------: |
| Update（更新已有对象） | 7.06 ns | — | 17.43 ns | 62.38 ns | 8.22 ns |
| Nullable（可空类型） | 13.30 ns | 14.00 ns | 25.71 ns | 72.36 ns | 14.25 ns |
| Record（不可变记录） | 16.35 ns | 16.65 ns | 38.19 ns | 76.13 ns | 15.75 ns |
| NameDiff（异名） | 16.40 ns | 15.83 ns | 25.44 ns | 76.40 ns | 16.28 ns |
| BidirED（DTO→实体） | 20.72 ns | 19.94 ns | 30.38 ns | 78.97 ns | 19.58 ns |
| Flat（9 字段平铺） | 20.36 ns | 19.78 ns | 30.28 ns | 79.30 ns | 19.97 ns |
| BidirDE（实体→DTO） | 24.62 ns | 23.86 ns | 33.78 ns | 82.27 ns | 22.94 ns |
| Nested（嵌套对象） | 26.69 ns | 25.60 ns | 37.81 ns | 82.78 ns | 26.16 ns |
| EnumString（枚举↔字符串） | 15.44 ns | 16.60 ns | 30.62 ns | 83.17 ns | 26.70 ns |
| Action（带回调） | 32.41 ns | 33.24 ns | 42.19 ns | 92.04 ns | 34.45 ns |
| Collection（100 元素） | 2.491 μs | 2.197 μs | 2.067 μs | 2.734 μs | 2.142 μs |
| AutoFlatten（扁平化） | 23.23 ns | 22.89 ns | — | — | 23.37 ns |

> `—` = 该库未实现该场景。NCMapper 列取源生成器直调路径（最快路径），`Mapper.Map` API 调用路径见上表；全部数值取自同一台基准机的同批（2026-10-07）跑测。

### 14.7 缓存治理

`ClearCache()` 提供统一的缓存清理入口，内部级联清理多层静态缓存：

| 清理对象                                       | 说明                                       |
| ------------------------------------------ | ---------------------------------------- |
| 映射委托缓存（GeneratedCache / MapperBase 静态 Lazy 字段） | 源生成委托 + 表达式树编译委托                      |
| PropertyMatcher / ReflectionHelper          | 属性字典、反射元数据缓存                             |
| MapValidator / CollectionCopier / DeepCopy* | 验证结果、集合拷贝器、深拷贝分类器缓存                     |
| 投影缓存清理委托（多槽位 `ConcurrentDictionary`）      | EFCore/FreeSql 扩展注册的 ProjectionBuilder 缓存，`ClearCache()` 遍历执行 |

**关键约束**：

- **动态类型不缓存**：`MapperBase<TSource,TTarget>` 的静态 `Lazy<T>` 字段以封闭类型组合为键；涉及 `dynamic` / 开放泛型 / 匿名类型的映射走运行时表达式树且不被静态缓存，性能略低但保证正确性。
- **配置重冻结限制**：`[assembly: MapDefaults]` 全局配置在首次消费后冻结为不可变视图（`GlobalConfigView`）；修改配置需先 `ClearGenerated()` 解除冻结再重注册，否则新配置对**已编译**的类型对不生效（`MapperBase` 静态 `Lazy` 一经物化不受 ClearCache / 配置重冻结影响）。
- **CacheMetrics 内部观测**：`CacheMetrics`（internal static）记录缓存命中/未命中计数，`ClearCache()` 与 `ResetMetrics()` 均会级联 `CacheMetrics.Reset()`，供性能诊断，不属于公开 API。

---

