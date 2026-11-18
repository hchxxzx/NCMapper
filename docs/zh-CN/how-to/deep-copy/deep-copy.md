---
wiki-id: how-to/deep-copy/deep-copy
涉及诊断码: （无）
库版本: v1.0.0+
更新时间: 2026-09-29
---

# 深拷贝

## 概述

NCMapper 的深拷贝由 `Mapper.DeepCopy<T>(src)` 驱动，实际执行分为两层，框架按类型自动路由，用户无需手动选择：

- **层 1 · 源生成器优先（最优路径）**：若类型标记了 `[MapFrom]`（源生成器为其生成了深拷贝委托），则直接调用编译期生成的专用委托，**绕过运行时 Provider**，典型 ~179ns/op（net10.0 Linux 口径），是所有路径中最快的。
- **层 2 · 运行时策略回退**：无源生成委托时，转发到 `DeepCopyProviderBase.Current` 指向的策略 Provider（由 `DeepCopyStrategy` 枚举决定，默认 `Fast`）。

因此"默认策略是 Fast"仅在**类型未带 `[MapFrom]`** 的前提下成立；带 `[MapFrom]` 的类型永远走更快的源生成路径，与 `DeepCopyStrategy` 配置无关。

核心 API：`Mapper.DeepCopy<T>(src)` / `Mapper.DeepCopy<T>(src, Action)`

---

## 1. 基础深拷贝

### 用法

创建源对象的完全独立副本，包括嵌套引用类型。

[!code-csharp[深拷贝嵌套引用类型全量复制](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyCrossType)]

**要点**：

- 深拷贝创建完全独立的对象图，包括所有嵌套引用类型
- 值类型和 string 直接复制（值语义）
- 集合类型递归深拷贝每个元素

---

## 2. 带 Action 回调

[!code-csharp[深拷贝 + Action 回调](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyWithAction)]

> ⚠️ **引用赋值陷阱**：action 同时接收 `src` 和 `tgt` 两个参数。在 action 中将 `src` 的引用类型属性直接赋值给 `tgt`（如 `tgt.Address = src.Address`）会**破坏深拷贝语义**，导致 `tgt` 与 `src` 共享同一引用对象，后续对一方的修改将影响另一方。
>
> **正确做法**：action 中应仅修改 `tgt` 的值类型字段或字符串；若需传递引用属性，应对其再次调用 `Mapper.DeepCopy` 创建独立副本：
> ```csharp
> var copy = Mapper.DeepCopy(original, (src, tgt) =>
> {
>     tgt.Name = $"[COPY] {src.Name}";
>     tgt.Address = Mapper.DeepCopy(src.Address!);  // ✅ 对引用属性再次 DeepCopy
>     // tgt.Address = src.Address;                // ❌ 破坏深拷贝
> });
> ```
>
> 启用 `MapTracer.GloballyEnabled = true` 后，action 执行前会输出引用赋值风险警示，便于调试时发现问题。

---

## 3. 三种策略对比

深拷贝策略由 `DeepCopyStrategy` 枚举定义，共三个枚举值，默认 `Fast`：

| 枚举值 | 对应 Provider 类型 | 语义 |
| --- | --- | --- |
| `DeepCopyStrategy.Fast` | `FastDeepCopyProvider` | 默认（无 `[MapFrom]` 类型时），支持循环引用，Acyclic 零检测快路径 |
| `DeepCopyStrategy.ExpressionTree` | `ExpressionTreeDeepCopyProvider` | 含完整循环引用检测，支持 DAG 共享引用保持 |
| `DeepCopyStrategy.Json` | `JsonDeepCopyProvider` | JSON 序列化中转兜底，不支持循环引用 |

> 📌 **概念澄清**：`Fast` 策略内部会对类型图进行 **Acyclic / Cyclic / Unknown** 三值分类（决定走零检测快路径还是完整检测），这是 `Fast` 策略的**实现细节**，与上面三个"策略枚举值"是两个不同层级的概念，请勿混淆。

| 策略 | 性能 | 循环引用 | 适用场景 |
|---|---|---|---|
| Fast（默认，条件分发） | 无环 ~401ns（Acyclic 快路径）/ 有环：转发基类完整检测（任意环长） | 支持（Acyclic 零检测 + Cyclic 完整检测） | 默认首选；不保持 DAG 共享引用 |
| ExpressionTree | ~409ns | 任意环长 | 需要 DAG 共享引用保持 |
| JSON | ~1697ns | 不支持 | 兼容性兜底（序列化+反序列化） |

> **测试环境说明**：本节性能数值为 2026-10-07 Linux 全量基准跑批实测（net10.0，Intel Xeon Silver 4314 / Ubuntu 24.04.5 LTS / BenchmarkDotNet 0.14.0，源生成器深拷贝同口径 ~179ns）。性能数值随测试机 CPU / OS / .NET 版本差异较大，此处仅供量级与路径间相对差距参考。

> ⚠ **DAG 共享引用**：Fast 路径现在基于编译期/首编译的三值分类（Acyclic/Cyclic/Unknown）条件分发。**Acyclic 类型**的 DAG 共享引用为"逐处独立复制"（每个共享节点独立复制）。如需保留 DAG 共享引用，请改用 `ExpressionTreeDeepCopyProvider`。

### 切换策略

通过 `DeepCopyProviderBase.Current` 切换全局策略。也可经 `NCMapperOptions.DeepCopyStrategy` 属性或 appsettings.json 的 `DeepCopyStrategy` 键（字符串值 `Fast` / `ExpressionTree` / `Json`，默认 `Fast`）配置，二者等价——`NCMapperOptions.Apply()` 会把枚举映射到 `DeepCopyProviderBase.Current`：

[!code-csharp[深拷贝策略切换：ExpressionTree 策略](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyExpressionTree)]
[!code-csharp[深拷贝策略切换：JSON 策略（工厂属性）](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyJson)]

**要点**：

- 策略切换是全局的，影响后续所有 `Mapper.DeepCopy` 调用
- 切换后记得恢复原策略（典型的 try/finally 模式）
- `FastDeepCopyProvider` 是默认策略（仅对**无 `[MapFrom]` 类型**生效），对类型图进行三值分类（Acyclic/Cyclic/Unknown）条件分发：
  - **Acyclic**：零检测快路径（~401ns）；环形缓冲仅作分类假阴性兜底（环长 ≤ 8），正常不命中
  - **Cyclic/Unknown**：转发基类完整检测（CycleCache + record 占位，任意环长）
  - **Fast 现已完整支持循环引用**，无需为"处理自引用/环"而改用 `ExpressionTree`；两者能力差异仅在 DAG 共享引用语义（见下）
- `ExpressionTreeDeepCopyProvider` 含完整循环引用检测（任意环长），且**保持 DAG 共享引用**；Fast 的 Acyclic 路径为性能将共享节点逐处独立复制（共享节点逐处独立复制）

> ⚠️ MaxRecursionDepth：通过 `DeepCopyContext` 控制递归深度上限，防止无限递归栈溢出。

```mermaid
flowchart TD
    Start[Mapper.DeepCopy&#60;T&#62;&#40;src&#41;] --> HasGen{类型带 [MapFrom]<br/>有源生成委托?}
    HasGen -->|是| Gen[源生成器深拷贝 ~179ns<br/>编译期委托，最高优先]
    HasGen -->|否| Cls{Fast 策略下<br/>类型图三值分类}
    Cls -->|Acyclic| FastAcyclic[Fast 零检测快路径 ~401ns]
    Cls -->|Cyclic / Unknown| FastFull[Fast 完整检测<br/>转发基类，任意环长]
    Start -->|策略=ExprTree| Expr[ExpressionTreeDeepCopyProvider ~409ns<br/>保持 DAG 共享]
    Start -->|策略=JSON| Json[JsonDeepCopyProvider ~1697ns<br/>不支持循环引用]
    Gen --> End[返回独立副本]
    FastAcyclic --> End
    FastFull --> End
    Expr --> End
    Json --> End
```

---

## 常见误用

### 误用 1：认为 DeepCopy 等效于 MemberwiseClone

```csharp
// ❌ 误解：DeepCopy 不是浅拷贝
// DeepCopy 递归复制所有嵌套引用类型，创建完全独立的对象图

// ✅ DeepCopy 是深拷贝，MemberwiseClone 是浅拷贝（只复制根对象引用）
```

### 误用 2：需要 DAG 共享引用保持时使用 Fast 策略

[!code-csharp[DAG 共享引用保持：切换 ExpressionTreeDeepCopyProvider](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyExpressionTree)]

### 误用 3：action 中将 source 的引用属性直接赋值给 copy

```csharp
// ❌ 误用：action 中直接赋值 source 的引用属性，破坏深拷贝
var copy = Mapper.DeepCopy(order, (src, tgt) =>
{
    tgt.Customer = src.Customer;  // copy.Customer 现在指向 src.Customer 同一对象
    tgt.Items = src.Items;        // 集合引用共享
});
// 后果：修改 copy.Customer.Address 会影响 src.Customer.Address

// ✅ 正确：action 中仅修改值类型字段；引用属性用 DeepCopy 创建独立副本
var copy = Mapper.DeepCopy(order, (src, tgt) =>
{
    tgt.Status = "Copied";                         // 值类型，安全
    tgt.Customer = Mapper.DeepCopy(src.Customer!);  // 引用属性再次 DeepCopy
});
```

> 💡 启用 `MapTracer.GloballyEnabled = true` 可在 action 执行前输出警示，帮助发现此陷阱。

---

## 4. 集合深拷贝

深拷贝自动递归处理集合类型的每个元素：

```csharp
// List 深拷贝
var list = new List<UserEntity>
{
    new() { Id = 1, Name = "Alice", Address = new Address { City = "Beijing" } },
    new() { Id = 2, Name = "Bob", Address = new Address { City = "Shanghai" } },
};
var copyList = Mapper.DeepCopy(list);
// copyList 是新的 List<UserEntity>
// 每个元素都是独立副本（包括 Address 嵌套对象）

copyList[0].Name = "Modified";
copyList[0].Address.City = "Guangzhou";
// list[0].Name == "Alice"（不受影响）
// list[0].Address.City == "Beijing"（嵌套对象也独立）

// Dictionary 深拷贝
var dict = new Dictionary<string, UserEntity>
{
    ["k1"] = new() { Id = 10, Name = "Dict-A" },
    ["k2"] = new() { Id = 20, Name = "Dict-B" },
};
var copyDict = Mapper.DeepCopy(dict);
// copyDict 是新的 Dictionary，Key 和 Value 都是独立副本

// HashSet 深拷贝
var set = new HashSet<UserEntity>
{
    new() { Id = 1, Name = "A" },
    new() { Id = 2, Name = "B" },
};
var copySet = Mapper.DeepCopy(set);
// copySet 是新的 HashSet<UserEntity>，元素独立
```

**要点**：

- List / Dictionary / HashSet / Queue / Stack 等集合类型自动递归深拷贝
- 集合本身创建新实例，每个元素走深拷贝逻辑
- 嵌套集合（如 `List<List<T>>`）递归处理每一层

---

## 5. 深拷贝与浅拷贝对比

| 维度 | `Mapper.DeepCopy` | `MemberwiseClone` |
|---|---|---|
| 根对象 | 新实例 | 新实例 |
| 值类型字段 | 独立副本 | 独立副本 |
| string 字段 | 独立副本（string 不可变，等效共享） | 共享引用 |
| 引用类型字段 | **递归深拷贝**（独立副本） | **共享引用** |
| 集合字段 | 递归深拷贝每个元素 | 共享引用 |
| 嵌套对象 | 完全独立的对象图 | 与源对象共享 |
| 循环引用 | ExpressionTree 策略支持 | 不支持（会循环） |
| 性能 | 无环 ~401ns（Fast Acyclic）/ 有环转发基类完整检测（Fast）/ ~409ns（ExprTree）/ ~179ns（源生成器，最优；均为 net10.0 Linux 口径） | ~5-10ns |

```csharp
// 浅拷贝：MemberwiseClone
var shallow = entity; // 实际无法直接调用 MemberwiseClone（protected）
// 引用类型字段共享 → 修改 shallow.Address.City 会影响 entity

// 深拷贝：Mapper.DeepCopy
var deep = Mapper.DeepCopy(entity);
// 引用类型字段独立 → 修改 deep.Address.City 不影响 entity
```

---

## 6. AOT 回退行为（NativeAOT 部署须知）

>源生成器在两类场景下会回退到运行时深拷贝，
> 触发 IL2026/IL3050 警告（已由生成代码中的精确 `#pragma` 抑制并附原因注释）。
> 本节说明 AOT 下触发回退时的**实际行为**与规避方式。

### 6.1 P5b：循环引用回退（安全机制，不可规避）

`record` 类型 + 循环引用图的组合**无法**由源生成器深拷贝：record 的构造器路径无法在
`new T(...)` 前预登记目标对象，循环引用会导致无限递归。检测到该场景时生成代码回退到
运行时 `DeepCopyRuntimeOnly`（基于 ConditionalWeakTable，支持 record 循环引用检测）。

- **AOT 下行为**：回退路径调用运行时反射，`RequiresUnreferencedCode`/`RequiresDynamicCode`
  警告已被抑制——**但运行时反射在裁剪/AOT 下可能失败**（成员被裁剪或动态代码不可用）。
- **规避方式**：
  - 优先使用**无参构造器类型**（走生成代码路径，零反射）；
  - record 类型务必保持**无环图**（编译期分类 Acyclic，免回退）；
  - 确需 record + 环：改走 `DeepCopyProviderBase.Current.DeepCopy` 并手动保留反射所需成员
    （如 `[DynamicallyAccessedMembers]`），或在运行时 Provider 未裁剪场景下使用。

### 6.2 P5c：编译失败兜底（理论路径）

目标类型无法编译生成（如无可解析构造器）时回退到运行时。正常类型不会触发；
触发即说明类型形态超出生成器能力边界，建议调整类型设计（改用无参构造器 + 可写属性）。

---

## 小结

| 能力 | API | 说明 |
|---|---|---|
| 深拷贝 | `Mapper.DeepCopy<T>(src)` | 完全独立副本 |
| 带回调 | `Mapper.DeepCopy<T>(src, action)` | 副本后修改 |
| 策略切换 | `DeepCopyProviderBase.Current` | 全局策略，后恢复 |
| 递归深度 | `DeepCopyContext` | 控制 MaxRecursionDepth |

---

## 配套示例

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 401
```

核心示例源码（单点同源，`dotnet run --scenario` 可运行验证）：

[!code-csharp[同类型深拷贝](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType)]

[!code-csharp[同类型深拷贝 · DeepCopyWithAction](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyWithAction)]

[!code-csharp[同类型深拷贝 · DeepCopyCrossType](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyCrossType)]

[!code-csharp[深度限制：MaxRecursionDepth 守护](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_MaxRecursionDepth)]

---

## 进一步阅读

- 相关篇目：[基础映射](../declarative/basics.md)
- 技术文档深挖：[深拷贝体系](../../explanation/deep-copy.md) §8
