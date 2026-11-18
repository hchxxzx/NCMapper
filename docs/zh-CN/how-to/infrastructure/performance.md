---
wiki-id: how-to/infrastructure/performance
涉及诊断码: NCMAP001, NCMAP019, NCMAP099
库版本: v1.0.0+
更新时间: 2026-09-29
---

# 性能与最佳实践

## 概述

NCMapper 双引擎架构（源生成器 + 运行时表达式树）在不同场景下有不同性能特征。本篇覆盖性能对比数据、优化建议、常见误用和最佳实践，帮助在热路径场景获得最优性能。

核心 API：`Mapper.Map<T,S>(src)` / `src.MapToXxxDto()` / `Mapper.MapList<T,S>(srcs)` / `Mapper.DeepCopy<T>(src)` / `Mapper.ClearCache()`

---

## 1. 双引擎性能对比

NCMapper 提供两条映射执行路径，根据 DTO 是否标注 `[MapFrom]` 自动选择：

> **测试环境与口径**：本页「典型性能」数值取自 2026-10-07 全量基准跑批（net10.0）：**Intel Xeon Silver 4314 @ 2.40GHz（2 插槽 / 40 逻辑核，基准锁定 0-19），Ubuntu 24.04.5 LTS，BenchmarkDotNet 0.14.0**（5 次启动 × 15 次迭代，预热 3 次）。
> **性能数值随测试机 CPU / OS / .NET 版本差异较大**（不同机器同场景实测可差 1~2 倍），本页数值仅用于判断**路径间相对差距与量级**，不作为跨环境的绝对性能承诺。完整三框架 × 全场景数据见[性能优化机制](../../explanation/performance.md) §14.6。

| 调用方式 | 执行路径 | 典型性能 | 首次开销 |
|---|---|---|---|
| `entity.MapToUserDto()` | 源生成器扩展方法，零委托直调 | ~13-27ns/op | 无（编译时生成） |
| `Mapper.Map<UserEntity, UserDto>(entity)` | 委托注册表查找 → 源生成器代码 | ~17-29ns/op | 无 |
| `Mapper.Map<T, U>(src)`（未标注 `[MapFrom]`） | 运行时表达式树编译，首次编译后缓存 | ~19-38ns/op | 有（首次编译 ~1-5ms） |

### 用法

[!code-csharp[映射三路径热对比：生成扩展/标注对/运行时表达式树](~/samples/NCMapper.Samples/06-Infrastructure/PerformanceScenarios.cs#Core_Perf_GeneratedHotPath)]

**要点**：

- 源生成器路径在热路径场景最优——零委托查表，JIT 可内联
- `Mapper.Map` 和源生成器扩展方法的结果完全一致，差异仅在调用路径
- 首次调用运行时表达式树引擎时有编译开销（~1-5ms），后续调用走缓存
- 未标注 `[MapFrom]` 会触发 NCMAP019 编译期 Info，提示性能路径缺失（运行时映射仍可用）

### 性能指标统计

NCMapper 内置映射指标统计，可用于性能分析：

[!code-csharp[指标统计开关与三计数器](~/samples/NCMapper.Samples/06-Infrastructure/PerformanceScenarios.cs#Infra_Perf_MetricsObserve)]

| 指标 | 含义 |
|---|---|
| `MetricsEnabled` | 指标开关，默认 `false`；开启后 Interlocked 原子计数 |
| `TotalMapCalls` | 映射调用总次数 |
| `CacheHits` | 缓存命中次数（走源生成器或已编译的表达式树） |
| `CompilationCount` | 生成缓存未命中、回退运行时引擎的**调用次数**（每次回退计数；同一类型仅首次真正编译表达式树，后续命中运行时引擎内部缓存——非纯编译次数。带 `action` 的调用经生成器 WithAction 委托正常命中不计数；仅当手工只注册 `RegisterGeneratedCore` 而未注册 WithAction 委托时，带 `action` 调用才回退计数） |
| `ResetMetrics()` | 将 `TotalMapCalls` / `CacheHits` / `CompilationCount` 归零 |

---

## 2. 深拷贝策略性能

深拷贝策略由 `DeepCopyStrategy` 枚举定义，共三个枚举值，默认 `Fast`，分别映射到以下 Provider：

**运作原理（执行优先级）**：`Mapper.DeepCopy<T>` 不会直接落到某个策略 Provider，而是先查源生成委托，两层自动路由：

1. **层 1 · 源生成器（最优）**：类型带 `[MapFrom]` 时，源生成器在编译期注册专用深拷贝委托，`Mapper.DeepCopy` 直接调用（~179ns/op，net10.0 Linux 口径），完全绕过 `DeepCopyProviderBase.Current` 与 `DeepCopyStrategy` 配置。
2. **层 2 · 运行时策略**：无源生成委托时转发到 `DeepCopyProviderBase.Current`（默认 `Fast`）。

因此"默认 = Fast"仅在**类型未带 `[MapFrom]`** 时成立。

| 枚举值 | 对应 Provider 类型 | 语义 |
| --- | --- | --- |
| `DeepCopyStrategy.Fast` | `FastDeepCopyProvider` | 默认（无 `[MapFrom]` 类型时），支持循环引用，Acyclic 零检测快路径 |
| `DeepCopyStrategy.ExpressionTree` | `ExpressionTreeDeepCopyProvider` | 含完整循环引用检测 |
| `DeepCopyStrategy.Json` | `JsonDeepCopyProvider` | JSON 序列化中转兜底，不支持循环引用 |

| 策略 | 典型性能 | 循环引用 | 适用场景 |
|---|---|---|---|
| `FastDeepCopyProvider`（默认） | 无环 ~401ns（Acyclic 快路径）/ 有环：转发基类完整检测（任意环长） | 支持（Acyclic 零检测 + Cyclic 完整检测） | 条件分发，默认首选；不保持 DAG 共享引用 |
| `ExpressionTreeDeepCopyProvider` | ~409ns | 任意环长 | 需要 DAG 共享引用保持 |
| `JsonDeepCopyProvider` | ~1697ns | 不支持 | 兼容性兜底（序列化+反序列化） |

> ⚠ **DAG 共享引用**：Fast 路径 Acyclic 类型的 DAG 共享引用为"逐处独立复制"。如需保持共享，请改用 `ExpressionTreeDeepCopyProvider`。

### 切换策略

也可经 `NCMapperOptions.DeepCopyStrategy` 属性或 appsettings.json 的 `DeepCopyStrategy` 键配置（字符串值 Fast/ExpressionTree/Json，默认 Fast），与直接赋值二者等价。三种切换的可运行版本（try/finally 恢复原策略）：

[!code-csharp[切换到 ExpressionTree 策略（含循环引用检测）](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyExpressionTree)]

[!code-csharp[切换到 JSON 策略（通过工厂属性）](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyJson)]

[!code-csharp[切换回 Fast 策略（默认）](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyFast)]

**要点**：

- 策略切换是全局的，影响后续所有 `Mapper.DeepCopy` 调用
- 切换后记得恢复原策略（典型的 try/finally 模式）
- `FastDeepCopyProvider` 是默认策略（仅对**无 `[MapFrom]` 类型**生效），对类型图进行三值分类（Acyclic/Cyclic/Unknown）条件分发：
  - **Acyclic**：零检测快路径（~401ns）；环形缓冲仅作分类假阴性兜底（环长 ≤ 8），正常不命中
  - **Cyclic/Unknown**：转发基类完整检测（CycleCache + record 占位，任意环长）
  - **Fast 现已完整支持循环引用**，无需为"处理自引用/环"而改用 `ExpressionTree`；两者能力差异仅在 DAG 共享引用语义（见下）
- `ExpressionTreeDeepCopyProvider` 含完整循环引用检测（任意环长），且**保持 DAG 共享引用**；Fast 的 Acyclic 路径为性能将共享节点逐处独立复制（共享节点逐处独立复制）
- 含 `[MapFrom]` 的类型会走**源生成器深拷贝**（编译期生成，绕过上述 Provider 策略），典型 ~179ns/op（net10.0 Linux 口径），为**所有路径中最快**（注意：它不属于 `DeepCopyStrategy` 枚举，是层 1 优先路径，不可经 `DeepCopyProviderBase.Current` 切换）
- `MaxRecursionDepth` 通过 `DeepCopyContext` 控制递归深度上限，防止栈溢出

---

## 3. 列表与集合映射性能

| 操作 | 典型性能（1000 元素） | 说明 |
|---|---|---|
| `Mapper.MapList<T, U>(list)` | ~24.6μs | 批量映射，内部优化 |
| 逐条 `Mapper.Map<T, U>(item)` | ~25μs | 与 MapList 同量级 |
| 源生成器 `list.MapToXxxDtoList()` | ~24.6μs | 零委托批量映射 |

**要点**：

- `MapList` 与逐条 Map 同量级（均 ~25μs/1000，Linux 20261007 口径），`MapList` 省去逐项委托查找
- 源生成器列表扩展方法（`MapToXxxDtoList()`）进一步省去委托查表
- 集合类型自动识别（List/Array/HashSet/Dictionary 等 14 种），无需手动转换
- 高频批量且目标对象可复用时，用 `MapListInto` 填充预分配数组：复用调用顶层零分配，见 [MapListInto](../mapping/map-list-into.md)

**MapListInto 复用场景（单线程 = 20261007 Linux 全量跑批；多线程 = 20260820 负载实测）**：

- 单线程吞吐（100 元素/次）：~0.48μs/次（net8.0/net9.0/net10.0：481/482/476 ns，NCMapper 路径），为同批 `MapList` 的 5.1×-5.6×
- 16 线程缩放效率 0.66-0.71（net8.0/net9.0/net10.0：0.660/0.701/0.710；口径 TPS_16T / (TPS_1T×16)，20260820 负载实测）
- 复用场景顶层零分配（实测 0 B / 0 Gen0 ×10000）；嵌套引用属性每轮仍新建子对象

---

## 4. Compose 与 Pipeline 开销

| 操作 | 典型开销 | 说明 |
|---|---|---|
| Compose 双源 | ~30-50ns/op | 多源合成，比单源 Map 略慢 |
| Compose 三源 | ~40-60ns/op | 三源合成 |
| PostMap 单层 | ~5-10ns/op | 洋葱模型外层回调 |
| PostMap 三层 | ~15-30ns/op | 三层嵌套回调 |

**要点**：

- Compose 的开销主要来自多源属性查找，比单源 Map 略慢但远低于手动拼接
- PostMap 按注册逆序执行（洋葱模型），层数越多开销越大
- `Mapper.Pipeline.Clear()` 清空所有 PostMap 并使只读属性 `IsEnabled` 变为 false——后续映射跳过所有 PostMap；重新启用需重新 `AddPostMap` 注册（注册即自动置启用，源码锚：MapPipeline.cs:133/:158）

---

## 5. 冷启动与缓存

NCMapper 的"冷启动"指首次调用某个类型对映射时的初始化开销：

| 场景 | 冷启动开销 | 后续调用 |
|---|---|---|
| 标注 `[MapFrom]`（源生成器路径） | ~0ns（编译时已生成） | ~17-29ns |
| 未标注 `[MapFrom]`（运行时编译） | ~1-5ms（表达式树编译） | ~19-38ns |

[!code-csharp[ClearCache 后冷启动首调耗时（含表达式树编译）](~/samples/NCMapper.Samples/06-Infrastructure/PerformanceScenarios.cs#Core_Perf_ColdStartAfterClear)]

**要点**：

- `Mapper.ClearCache()` 清除所有映射缓存 + MaskProvider Override，后续首次调用需重新编译
- 生产环境避免在运行时调用 `ClearCache()`（会导致下一次映射冷启动）
- 测试环境中 `ClearCache()` 用于验证冷启动行为

---

## 6. AOT / NativeAOT 兼容性

NCMapper 在设计上兼顾 AOT 场景，但部分反射式能力在 NativeAOT 下不可用。

**AOT 友好的部分**：

- 源生成器路径（`[MapFrom]` 生成的静态映射方法）完全 AOT 兼容——无运行时反射
- `[MapFrom]` / `[MapProperty]` / `[MapIgnore]` 等特性在编译期完成分析
- 映射委托由源生成器编译时生成（零反射、不依赖 `[DynamicallyAccessedMembers]`），trimming 不丢成员

**AOT 下不可用的部分**：

| API | 原因 | 应对 |
|---|---|---|
| `Mapper.Validate(params Assembly[])` | 用反射扫描程序集与类型（标注 `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]`） | 生产环境依赖全量 `[MapFrom]` 的源生成器路径，避免运行时反射扫描 |
| `JsonSerializerBase` 反射式序列化 | 标注 `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]` | 改用源生成的 `JsonSerializerContext` |
| ORM 扩展投影路径（`AsDto`→`ToList` 等） | `BuildProjection` 用 `MakeGenericMethod`（`[RequiresDynamicCode]` / `[RequiresUnreferencedCode]`），无源生成器替代 | 投影路径 AOT 下不可用；降级路径（内存 `MapList`/`Compose`）标 `[MapFrom]` 后条件可用 |

**要点**：

- 生产环境推荐为所有 DTO 标注 `[MapFrom]`，让映射完全走源生成器路径（零运行时反射）
- NativeAOT 发布时，未标注 `[MapFrom]` 的类型对会在首次映射时因运行时反射失败——务必全量标注
- `Validate(params Assembly[])` 仅用于开发期全量检查；NativeAOT 下应改用源生成器路径保证可用性

---

## 常见误用

### 误用 1：Stopwatch 测量未预热的冷启动

```csharp
// ❌ 误用：首次调用包含表达式树编译开销，测量数据失真
Mapper.ClearCache();
var sw = Stopwatch.StartNew();
var dto = Mapper.Map<UserEntity, UserDto>(entity);  // 含 ~1-5ms 编译开销
sw.Stop();
Console.WriteLine($"{sw.Elapsed.TotalMicroseconds:F2}μs");  // 结果偏大 100 倍+

// ✅ 正确：先预热（触发编译+缓存），再测量
Mapper.ClearCache();
Mapper.Map<UserEntity, UserDto>(entity);  // 预热：触发编译
var sw = Stopwatch.StartNew();
for (int i = 0; i < 1000; i++)
    Mapper.Map<UserEntity, UserDto>(entity);  // 热路径测量
sw.Stop();
Console.WriteLine($"{sw.Elapsed.TotalMicroseconds / 1000:F2}μs/op");
```

> NCMAP099 是源生成器的内部诊断码，用于报告生成过程中的异常。如果 Benchmark 中出现 NCMAP099 相关错误，通常是源生成器在编译时遇到了无法处理的类型结构——需检查 DTO 的 `[MapFrom]` 标注是否正确。

### 误用 2：GC 干扰导致数据失真

```csharp
// ❌ 误用：未控制 GC，GC 暂停导致测量数据跳动
var sw = Stopwatch.StartNew();
for (int i = 0; i < 10000; i++)
    Mapper.Map<UserEntity, UserDto>(entity);
sw.Stop();
// 如果循环中触发 GC，结果会偏大 10-100 倍

// ✅ 正确：测量前强制 GC，测量中避免分配
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

var sw = Stopwatch.StartNew();
for (int i = 0; i < 10000; i++)
    Mapper.Map<UserEntity, UserDto>(entity);  // 映射本身无分配
sw.Stop();
```

### 误用 3：生产环境运行时 ClearCache

```csharp
// ❌ 误用：在 Web 请求处理中调用 ClearCache
app.MapGet("/users/{id}", (int id) =>
{
    Mapper.ClearCache();  // 每次请求都清除缓存！
    return Mapper.Map<UserEntity, UserDto>(userService.Get(id));
});
// 后果：每个请求都冷启动，性能下降 100 倍+

// ✅ 正确：ClearCache 仅在启动时或测试中使用
// 生产环境映射缓存是自动管理的，无需手动清除
```

### 误用 4：未标注 [MapFrom] 导致运行时编译

```csharp
// ❌ 误用：DTO 未标注 [MapFrom]，运行时表达式树编译
public class UserDto { ... }  // 无 [MapFrom]
var dto = Mapper.Map<UserEntity, UserDto>(entity);  // ~19-38ns，首次 ~1-5ms 编译
// NCMAP019 Info: UserDto 未声明 [MapFrom]（运行时映射仍可用，但 SourceGen 性能路径缺失）

// ✅ 正确：标注 [MapFrom] 让源生成器生成编译时代码
[MapFrom(typeof(UserEntity))]
public class UserDto { ... }
var dto = Mapper.Map<UserEntity, UserDto>(entity);  // ~17-29ns，零编译开销
```

### 误用 5：认为 Fast 深拷贝不支持长环而改用其他策略

[!code-csharp[FastDeepCopyProvider 循环引用完整支持](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyFast)]

### 误用 6：生产环境长期开启 Trace

```csharp
// ❌ 误用：生产环境开启 MapTracer
MapTracer.GloballyEnabled = true;  // 性能开销 ~15-20%
// 每次映射都输出详细诊断日志，严重影响性能

// ✅ 正确：仅在调试/排查时临时开启
if (env.IsDevelopment())
{
    MapTracer.GloballyEnabled = true;
}
// 生产环境保持关闭
```

---

## 最佳实践

1. **始终标注 `[MapFrom]`**：让源生成器生成编译时代码，避免运行时编译开销（NCMAP019 提示）
2. **热路径用扩展方法**：`entity.MapToUserDto()` 比 `Mapper.Map<>()` 少一次字典查找
3. **列表映射用 MapList**：`MapList` 内部批量处理，比逐条 Map 高效
4. **深拷贝选对策略**：环路径 ≤ 8 节点用 Fast 默认，环路径 > 8 节点或需 DAG 共享保持用 ExpressionTree
5. **生产环境关闭 Trace**：`MapTracer.GloballyEnabled` 有 ~15-20% 性能开销
6. **正确选择 PostMap 作用域**：PostMap 是进程级全局注册，横切关注点（审计/日志/格式化）在应用启动时注册一次、长期保留即可，不要为每次映射调用 `Clear`；若只需单次定制变换，改用 `Mapper.Map(src, action)` 的 `Action` 回调（随调用自动清理）。仅单元测试等受控作用域才需要「注册 + teardown Clear」对称收尾
7. **避免运行时 ClearCache**：仅在启动或测试中调用，生产环境缓存自动管理
8. **Benchmark 预热**：测量前先调用一次映射触发编译，避免冷启动数据失真
9. **Benchmark 控制 GC**：测量前 `GC.Collect()`，避免 GC 暂停干扰数据
10. **策略切换用 try/finally**：深拷贝策略切换后记得恢复默认策略

---

## 小结

| 场景 | 最优 API | 典型性能 |
|---|---|---|
| 单对象映射（热路径） | `src.MapToXxxDto()` | ~13-27ns |
| 单对象映射 | `Mapper.Map<T, U>(src)` | ~17-29ns |
| 未注册类型 | `Mapper.Map<T, U>(src)` | ~19-38ns |
| 列表映射 | `srcs.MapToXxxDtoList()` | ~24.6μs/1000 |
| 深拷贝（环路径≤8节点） | `Mapper.DeepCopy<T>(src)` Fast | ~401ns |
| 深拷贝（含循环引用） | `Mapper.DeepCopy<T>(src)` ExprTree | ~409ns |
| 多源合成 | `Mapper.Compose<T>(p, s)` | ~30-50ns* |

> \* 多源合成（Compose）与 Pipeline 开销未纳入 20261007 Linux 全量跑批，数值为既有实测参考；其余行均为 2026-10-07 Linux 全量跑批 net10.0 口径（测试环境见本页 §1 说明）。

---

## 配套示例

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 604
```

核心示例源码（单点同源，`dotnet run --scenario` 可运行验证）：

[!code-csharp[Mapper.Map 耗时测量](~/samples/NCMapper.Samples/06-Infrastructure/PerformanceScenarios.cs#Performance_MeasureMapPerformance)]

代码见 `samples/NCMapper.Samples/06-Infrastructure/PerformanceScenarios.cs`，涵盖双引擎对比、冷启动、Compose 性能、深拷贝策略、Pipeline 开销、指标统计等 13 个子场景。

---

## 进一步阅读

- 相关篇目：[基础映射](../declarative/basics.md) · [深拷贝](../deep-copy/deep-copy.md)
- 技术文档深挖：[性能优化机制](../../explanation/performance.md) §14
- 性能源码：`samples/NCMapper.Samples/06-Infrastructure/PerformanceScenarios.cs`
- 诊断码索引：[诊断码全索引](../../diagnostics/rules.md)（NCMAP001/NCMAP099 等）
