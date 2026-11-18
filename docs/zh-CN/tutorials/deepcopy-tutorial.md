---
wiki-id: tutorials/deepcopy-tutorial
涉及诊断码:
库版本: v1.0.0+
更新时间: 2026-08-19
---

# 深拷贝场景演练

## 概述

本教程解决编辑场景的经典问题：表单提交前要先保存一份"编辑前快照"，取消时还原。直接赋值引用会互相影响，你需要一个与源对象完全独立的副本。你会完成基础深拷贝、克隆后处理、策略选择与集合深拷贝。

示例来自 `samples/NCMapper.Samples` 的 03 场景，可运行验证。深拷贝机制与策略原理见 [深拷贝](../how-to/deep-copy/deep-copy.md)，本教程侧重业务场景的决策过程。

---

## 1. 场景：编辑表单的取消还原

编辑页面打开时，先保存一份原始数据：

```csharp
var original = Mapper.DeepCopy(entity);   // 编辑前快照
// 用户编辑 entity ...
// 用户点取消：
//   entity 已被表单修改，无法还原
//   original 是独立副本，可用于还原
```

为什么不直接 `var original = entity`？赋值只是复制引用，`original` 与 `entity` 指向同一对象，修改 `entity` 时 `original` 同步变化，快照失效。嵌套对象（如 `Address`）同样共享引用。

`DeepCopy` 递归复制整个对象图，每个嵌套对象都创建新实例，副本与源完全独立。

| 复制方式 | 顶层引用 | 嵌套对象 | 结果 |
|---------|---------|---------|------|
| 引用赋值（`=`） | 共享 | 共享 | 修改源影响副本，快照失效 |
| 浅拷贝（字段级复制） | 独立 | 共享 | 顶层独立，嵌套仍受影响 |
| 深拷贝（`Mapper.DeepCopy`） | 独立 | 独立 | 完全独立副本 |

深拷贝的典型应用：编辑表单快照、事务回滚、审计快照、缓存防篡改。

---

## 2. 基础深拷贝

调用 `Mapper.DeepCopy(src)`：

[!code-csharp[同类型深拷贝](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType)]

[!code-csharp[同类型深拷贝 · DeepCopyWithAction](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyWithAction)]

[!code-csharp[同类型深拷贝 · DeepCopyCrossType](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyCrossType)]

结果是与源对象类型相同、内容相同、引用完全独立的副本。修改副本的任意嵌套属性不影响源对象。

深拷贝与映射的区别：

| 维度 | 映射（`Mapper.Map`） | 深拷贝（`Mapper.DeepCopy`） |
|------|--------------------|---------------------------|
| 目标类型 | 通常与源不同（实体 → DTO） | 与源相同 |
| 语义 | 属性复制 + 转换 | 对象图克隆，断开引用 |
| 典型场景 | 分层数据传递 | 快照、回滚、防篡改 |

`DeepCopy` 对同类型对象复制时走深拷贝语义；跨类型时转发到映射路径（[深拷贝](../how-to/deep-copy/deep-copy.md)）。

---

## 3. 克隆后处理

克隆后要给副本打标记（如日志前缀），用带 Action 回调的重载：

[!code-csharp[带回调的深拷贝](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType)]

[!code-csharp[带回调的深拷贝 · DeepCopyWithAction](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyWithAction)]

[!code-csharp[带回调的深拷贝 · DeepCopyCrossType](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopySameType_DeepCopyCrossType)]

回调参数为目标（副本），在复制完成后执行。适合快照打时间戳、审计标记等场景。

---

## 4. 三种策略怎么选

NCMapper 提供多种深拷贝策略，运行时通过 `DeepCopyProviderBase.Current` 切换：

[!code-csharp[Fast 策略](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyFast)]

[!code-csharp[ExpressionTree 策略](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyExpressionTree)]

[!code-csharp[JSON 策略](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyJson)]

| 策略 | 特点 | 适用场景 |
|------|------|---------|
| 源生成器（默认优先） | 编译期生成，零首次延迟 | 类型对已注册 `[MapFrom]`，绝大多数场景 |
| Fast | 快速路径，不检测循环引用 | 对象图无环，性能优先 |
| ExpressionTree | 完整检测，含循环引用防护 | 对象图可能成环（自引用、父子互指） |
| JSON | 序列化 + 反序列化 | 跨程序集、需要过滤中间转换 |

选择依据：默认不切换，走源生成器最优路径；对象图有环或类型不可静态解析时，切换 ExpressionTree；需要中间过滤时用 JSON。切换是全局的（`DeepCopyProviderBase.Current`），用完在 `finally` 恢复原值（示例代码即此模式）。

策略决策表：

| 判断 | 结论 |
|------|------|
| 对象图是否可能成环（自引用、父子互指）？ | 是 → ExpressionTree；否 → 默认 |
| 类型是否含不可静态解析引用（object / 接口 / 动态）？ | 是 → ExpressionTree 或 JSON |
| 是否需要在拷贝过程中做中间转换/过滤？ | 是 → JSON |
| 性能敏感且图无环？ | 默认路径已足够（源生成器优先） |

切换策略时用 `try/finally` 恢复 `Current`，避免影响后续调用（示例代码已演示）。

---

## 5. 集合深拷贝

对象图包含集合时，`DeepCopy` 逐个元素克隆：

[!code-csharp[列表深拷贝](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_DeepCopyJson)]

`List` / 数组 / `Dictionary` / `HashSet` 均支持：集合容器新建，元素独立克隆，源集合元素不共享引用。

| 集合类型 | 拷贝行为 |
|---------|---------|
| `List<T>` | 新列表，元素独立克隆 |
| `T[]` | 新数组，元素独立克隆 |
| `Dictionary<K,V>` | 新字典，Key 保留，Value 独立克隆 |
| `HashSet<T>` | 新集合，元素独立克隆 |

混合嵌套（集合里又是集合、元素含导航对象）同样递归处理。快照场景中，实体关联的集合（如订单明细）会整体独立复制，还原时不会残留编辑痕迹。

---

## 6. 深度限制

递归复制有深度上限（默认 `Mapper.MaxRecursionDepth`），防止深层或循环结构失控：

[!code-csharp[深度限制演示](~/samples/NCMapper.Samples/04-DeepCopy/DeepCopyScenarios.cs#DeepCopy_MaxRecursionDepth)]

深度超限抛 `MapException`。正常业务对象图不会触顶；触发时先检查是否有意外成环（对象互相引用），而不是盲目调大上限。

深度上限的调整时机：

- 业务对象图层级确实超过默认值（如超深配置树）时才调大
- 调大上限会放大递归开销与栈风险，谨慎使用
- 成环结构调大上限无效，应先切断环（见常见坑 2）

## 编辑快照的完整流程

把本教程的组合起来，一个编辑页面的快照流程：

```text
1. 打开编辑页：original = Mapper.DeepCopy(entity)     // 快照
2. 用户编辑：   表单绑定 entity，字段被修改
3. 保存：       校验通过 → 提交 entity，丢弃快照
4. 取消：       entity 从 original 还原（或重新加载）
```

要点：

- 快照在页面打开时创建一次，不要在每次编辑后重建
- 保存成功后显式丢弃快照，避免驻留
- 还原时直接使用快照替换当前对象，或按快照字段回写

---

## 运行验证

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 401
```

场景 401 输出同类型、回调、三策略、集合、深度限制各子场景结果。

---

## 常见坑

| 坑 | 现象 | 改法 |
|----|------|------|
| 用引用赋值代替深拷贝 | 快照随编辑变化，取消还原失效 | 用 `Mapper.DeepCopy` 创建独立副本 |
| 对象图有环却用 Fast 策略 | 递归死循环或深度超限异常 | 切换到 ExpressionTree 策略（含循环引用检测） |
| 回调中把源的引用属性赋给副本 | 副本与源共享嵌套引用，独立失效 | 回调只改值类型或副本自身，不引入源的引用 |

---

## 速查表

| 场景 | API | 说明 |
|------|-----|------|
| 基本深拷贝 | `Mapper.DeepCopy(src)` | 递归复制对象图，独立副本 |
| 克隆后处理 | `Mapper.DeepCopy(src, (s, t) => ...)` | 回调修改副本 |
| 切换策略 | `DeepCopyProviderBase.Current = new XxxProvider()` | Fast / ExpressionTree / JSON |
| 集合深拷贝 | `Mapper.DeepCopy(list)` | 容器新建，元素独立克隆 |
| 深度上限 | `Mapper.MaxRecursionDepth` | 递归深度保护 |

快照场景按"打开时快照、保存后丢弃、取消时还原"的流程使用即可，策略切换只在你遇到环或过滤需求时才需要。

更多深拷贝边界（AOT 回退、循环引用防护）见 [深拷贝](../how-to/deep-copy/deep-copy.md) 的对应章节。

---

## 相关文档

- [深拷贝](../how-to/deep-copy/deep-copy.md)：策略机制与 AOT 行为
- [快速开始](../getting-started/tutorial-15min.md)：15 分钟基础映射演练
- [多源合成场景演练](./compose-tutorial.md)：聚合 DTO 的另一种思路
- [ORM 投影场景演练](./orm-projection-tutorial.md)：查询侧数据组装
- [映射配置所有权三原则](../explanation/semantics/mapping-config-ownership.md)
