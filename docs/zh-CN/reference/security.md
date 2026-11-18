---
title: 安全机制
---

<!-- 来源：NCMapper 主技术文档 §15 安全机制（结构重组，2026-08-19 章级迁移，内容未重排） -->
## 15. 安全机制

对象映射在高并发、复杂对象图、null 输入等场景下容易出问题——循环引用导致栈溢出、并发调用委托缓存竞争、null 引发空引用异常。NCMapper 内建多重安全保障，让映射在生产环境稳定运行。

### 15.1 循环引用防护

运行时引擎使用 `[ThreadStatic]` 递归深度计数器，每进入一层嵌套映射 +1，退出 -1。超过 `MaxRecursionDepth`（默认 64）时抛出 `MapException`，而非让栈溢出：

```csharp
// 循环引用：A.Child → B.Parent → A.Child → ...
// 第 64 层时抛出 MapException（可 catch），不会 StackOverflow
try
{
    var dto = Mapper.Map<NodeEntity, NodeDto>(cyclicNode);
}
catch (MapException ex)
{
    // "递归深度超过 MaxRecursionDepth(64)，可能存在循环引用"
}
```

| 场景         | 计数行为                 |
| ---------- | -------------------- |
| 嵌套对象映射     | 每进入一层 +1，退出 -1       |
| MapList 迭代 | 每个元素 +1，完成后 -1       |
| 源生成器路径     | 无计数器（编译时代码不存在循环引用风险） |

[!code-csharp[调整最大递归深度（默认 64，上界 4096）](~/samples/NCMapper.Samples/06-Infrastructure/SecurityScenarios.cs#Core_Security_RecursionDepth)]

### 15.2 线程安全

NCMapper 的静态缓存和委托注册在多线程环境下安全：

| 机制                                            | 保护对象     | 实现              |
| --------------------------------------------- | -------- | --------------- |
| `ConcurrentDictionary<(Type,Type), Delegate>` | 源生成器委托缓存 | 并发读写安全          |
| `Interlocked.CompareExchange`                 | 表达式树编译委托 | 原子赋值，多线程仅首次编译一次 |
| `ConcurrentDictionary<Type, Dictionary>`      | 属性字典缓存   | 并发读写安全          |
| `[ThreadStatic]` 深度计数器                        | 递归深度     | 线程隔离，无锁         |
| `lock (_sync)` + 原子版本递增                     | MapPipeline PostMap 注册/清除 | `AddPostMap`/`Clear` 同锁，队列写入/标志置位/版本递增/逆序缓存清除五动作原子化，消除并发注册竞态（回调不被静默吞掉、标志不被覆盖致回调永不执行） |
| `ConcurrentDictionary` 分槽（按声明类型）            | 投影缓存清理委托、Validate 注册 | EFCore/FreeSql 同进程共存时清理委托均被执行，不互相覆盖；Validate 注册字典分槽避免跨程序集冲突 |

> 多线程同时首次调用同一类型对的 `Mapper.Map` 时，表达式树可能被编译多次（`Interlocked.CompareExchange` 保证最终只有一个委托被使用），但不会抛异常或产生错误结果。

### 15.3 null 守卫

NCMapper 对 null 输入采用"静默返回"策略，不抛 `ArgumentNullException`：

```csharp
Mapper.Map<UserEntity, UserDto>(null);    // → null（不抛异常）
Mapper.MapList<UserEntity, UserDto>(null); // → null
Mapper.DeepCopy<UserEntity>(null);          // → null
```

| 输入         | 行为                       |
| ---------- | ------------------------ |
| 源对象为 null  | 返回 `default`（引用类型为 null） |
| 源集合为 null  | 返回 null                  |
| 嵌套属性为 null | 目标嵌套属性赋 null（不创建空对象）     |
| 集合元素为 null | 保留 null（保持索引一致性）         |

### 15.4 [MapIgnore] 方向安全

`[MapIgnore]` 的 Direction 枚举确保忽略规则只作用于指定方向，不会意外影响反向映射：

```csharp
[MapIgnore(Direction.ToDto)]       // 仅 Entity→DTO 时忽略，DTO→Entity 时正常映射
public string InternalNote { get; set; }

[MapIgnore(Direction.ToEntity)]    // 仅 DTO→Entity 时忽略
public string ComputedField { get; set; }

[MapIgnore]                         // 双向忽略（Direction.Both）
public string Temp { get; set; }
```

---

