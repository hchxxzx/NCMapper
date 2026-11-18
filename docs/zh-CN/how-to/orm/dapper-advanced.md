---
wiki-id: how-to/orm/dapper-advanced
涉及诊断码: （无）
库版本: v1.0.0+
更新时间: 2026-09-15
---

# Dapper 集成（二）：高级方法

## 概述

本篇覆盖 Dapper 集成的高级终端方法：`ToDictionary`、`ToChunkDto`、`ToTreeList` 和 `QueryMultiple`。这些方法在 `AsDto<TEntity>()` 后链式调用，覆盖字典映射、分批读取、树形数据和多结果集场景。

核心 API：`.AsDto<T>().ToDictionary<TKey,TDto>(sql, param, keySelector)` / `.ToChunkDto<TDto>(sql, param, size, action)` / `.QueryMultiple(sql)`

> 终端族另有 `ToLookup()` / `ToLookupAsync()`（分组查找表，返回 `ILookup<TKey, TDto>`）与 `ToPagedList()` / `ToPagedListAsync()`（SQL 级分页，返回 `PagedList<TDto>`）：与上述终端宿主于同一选择器，单点用法与分页方言说明见 [Dapper（一）：Query 系列](dapper-query.md) §3（源码锚：DapperDtoSelector.cs:513/:543；DapperDtoSelector.Paging.cs:28/:65）。

---

## 1. ToDictionary：映射为字典

### 用法

将查询结果映射为字典，Key 由实体属性选择：

[!code-csharp[Dapper 集成：using NCMapper.Dapper 与查询形态](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Query)]

**要点**：

- Key 选择器作用于 `TEntity`（源实体），非 `TDto`
- 返回 `Dictionary<TKey, TDto>`
- 如果有重复 Key，后者覆盖前者
- 参数：`(sql, param, keySelector)` — keySelector 是 `Func<TEntity, TKey>`

### 为什么 keySelector 作用于 Entity 而非 DTO？

因为 Dapper 的结果集先映射到 Entity（通过 TypeMap），再从 Entity 映射到 DTO。Key 选择在 Entity 阶段完成，避免 DTO 中找不到 Key 属性的情况。

---

## 2. ToChunkDto：分批读取

### 用法

大数据量场景的分批回调，每批 size 条：

[!code-csharp[Dapper 分批读取 ToChunkDto（每批 size 条回调累加）](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToChunkDto)]

**要点**：

- 大数据量场景下控制内存占用
- 内部实现：SQL 分页 + 逐批读取
- 有异步版本 `ToChunkDtoAsync`

### 异步分批读取

[!code-csharp[Dapper 异步分批读取 ToChunkDtoAsync](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToChunkDtoAsync)]

---

## 3. QueryMultiple：多结果集

### 用法

一次执行多条 SQL，返回多结果集（实现为泛型 ValueTuple 重载，无 GridReader 形式）：

[!code-csharp[Dapper QueryMultiple 双结果集解构](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_QueryMultiple)]

**要点**：

- `QueryMultiple<TDto1, TDto2>(sql, param)` 返回 `(List<TDto1>, List<TDto2>)`，三结果集用三泛型重载
- 按 DTO 类型顺序对应 SQL 结果集
- 适合一次连接获取多组数据的场景（减少数据库往返）

### 多结果集场景示例

[!code-csharp[Dapper QueryMultiple 三结果集三泛型重载](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_QueryMultiple3)]

> 注意：每个结果集都映射为对应 DTO 列表（无 GridReader 逐行读取形式）；统计类标量结果请用独立查询

---

### 3.1 流式查询（QueryUnbufferedAsync）

千万级结果集场景，用 `QueryUnbufferedAsync` 以 `IAsyncEnumerable<TDto>` 逐元素流式枚举，避免一次性全量加载到内存：

[!code-csharp[Dapper QueryUnbufferedAsync 流式枚举](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_QueryUnbufferedAsync)]

**实现原理**：

- 内部用 Dapper `ExecuteReaderAsync` + `GetRowParser` 实现真正的流式读取，非全量缓存
- `CancellationToken` 取消后不再 `yield` 后续元素，已 `yield` 的元素不受影响

**与 ToChunkDto 的区别**：

| 方法 | 模式 | 适用场景 |
|---|---|---|
| `QueryUnbufferedAsync<TDto>` | `IAsyncEnumerable` 逐元素流式 | 超大数据集、流式处理管线 |
| `ToChunkDto<TDto>(size, action)` | 分批回调（Action / Func） | 批量落库、分批发送 |

**要点**：

- 返回的 `IAsyncEnumerable` 需在 `await foreach` 中消费；若用 `ToListAsync()` 收集会全量加载，失去流式意义
- 流式读取期间连接保持打开，处理完成后及时释放

---

## 4. ToTreeList：树形投影

### 用法

将扁平的父子结构数据映射为树形 DTO 列表：

```csharp
public class CategoryEntity
{
    public int NodeId { get; set; }
    public int? ParentNodeId { get; set; }
    public string Name { get; set; } = "";
}

[MapFrom(typeof(CategoryEntity))]
[MapTree("NodeId", nameof(CategoryEntity.ParentNodeId))]  // 位置参数形式（；键名须用实体主键 NodeId）
public class CategoryDto
{
    public int NodeId { get; set; }
    public string Name { get; set; } = "";

    [MapProperty(typeof(CategoryEntity), nameof(CategoryEntity.ParentNodeId))]
    public int? ParentId { get; set; }

    public List<CategoryDto> Children { get; set; } = new();
}

```

[!code-csharp[Dapper 树形投影 ToTreeList（扁平查询建树）](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToTreeList)]

**要点**：

- 实体需有 `ParentId` 字段
- `[MapTree]` 标注树形映射的主键 / 父键 / 子集合属性名
- **树契约硬约束**：DTO 必须有可写的自引用集合属性（默认名 `Children`，可用 `[MapTree]` 第三参数自定义），缺失或类型不符抛 `MapConfigurationException`（见下方"树契约"小节）
- 返回 `List<TDto>`，根节点在顶层，`Children` 递归填充
- 参数：`(sql, param)`
- 自定义字段名：`[MapTree]` 默认用 `Id` / `ParentId` / `Children` 作为主键 / 父键 / 子集合属性名（`MapTreeAttribute` 构造器默认参数，与示例中的 `NodeId` 场景不同——**实体主键不是 `Id`/`ParentId` 时必须显式指定**，否则树构建按默认名找不到字段）；属性为只读，请用位置参数或构造器命名实参 `idProperty:` / `parentIdProperty:` / `childrenProperty:` 显式指定其他名称：

```csharp
// 实体主键字段为 NodeId、父键字段为 ParentNodeId 时
[MapTree("NodeId", "ParentNodeId")]  // 位置参数形式
public class CategoryDto { ... }

// 子集合属性名不是默认 Children（如 SubCategories）时，用第三参数指定
[MapTree("NodeId", "ParentNodeId", "SubCategories")]  // 位置参数形式
public class CategoryDto
{
    public int NodeId { get; set; }
    public int? ParentNodeId { get; set; }
    public List<CategoryDto> SubCategories { get; set; } = new();
}
```

> ⚠️ `[MapTree]` 仅 Dapper `ToTreeList` 读取。FreeSql `ToTreeList` 基于原生树构建能力（不读 `[MapTree]`），见 [FreeSql（三）：高级终端方法](freesql-advanced.md)。

### 树契约（Children 硬约束）

`ToTreeList` 构建树前对 DTO 逐项校验三判据，任一不满足抛 `MapConfigurationException`（消息报期望属性名 + `[MapTree(childrenProperty: "...")]` 引导）：

1. **存在性**：DTO 有名为 `Children`（默认）或 `[MapTree]` 第三参数指定名的属性；
2. **自引用集合**：该属性是泛型集合且元素类型为 DTO 自身（如 `List<CategoryDto> Children`）；
3. **可写性**：该属性有 setter（树构建需向其挂载子节点）。

```csharp
// ❌ 缺少 Children 属性 → 抛异常（消息报期望名 "Children" + 自定义名引导 + 扁平场景提示）
public class CategoryDto
{
    public int NodeId { get; set; }
    public int? ParentNodeId { get; set; }
}

// ❌ Children 类型不符（string 非自引用集合）→ 抛异常（消息区分"类型不符"失败因）
public class CategoryDto
{
    public int NodeId { get; set; }
    public string Children { get; set; } = "";
}

// ✅ 可写自引用集合属性（默认名命中）
public class CategoryDto
{
    public int NodeId { get; set; }
    public int? ParentNodeId { get; set; }
    public List<CategoryDto> Children { get; set; } = new();
}
```

> **扁平场景**：只需扁平列表（无树形）时请用 `ToList` / `Query`，勿用 `ToTreeList`——树契约不满足时显式报错，不做静默降级。
>
> **多自引用集合**：DTO 有多个自引用集合属性（如 `Children` + `SubItems`）时，按 `[MapTree]` 指定名精确命中（默认 `Children`），不再自动取第一个——消除多候选歧义。

---

## 常见误用

### 误用 1：ToDictionary 的 keySelector 使用 DTO 类型

```csharp
// ❌ 误用：ToDictionary 的 keySelector 使用 DTO 类型而非 Entity 类型
connection.AsDto<UserEntity>()
    .ToDictionary<int, UserDto>("SELECT * FROM Users", null, (UserDto d) => d.Id);
// ↑ keySelector 签名是 Func<UserEntity, TKey>，不是 Func<UserDto, TKey>
// 编译错误

// ✅ 正确：keySelector 使用实体类型
connection.AsDto<UserEntity>()
    .ToDictionary<int, UserDto>("SELECT * FROM Users", null, (UserEntity e) => e.Id);
```

### 误用 2：QueryMultiple 按 GridReader 方式使用

```csharp
// ❌ 误用：实现无 GridReader 形式（无 multi.Read<T>() / using 释放语义）
// var multi = connection.AsDto<UserEntity>().QueryMultiple("SELECT ...");
// var users = multi.Read<UserDto>().ToList();   // 编译失败：不存在 Read<T>()

// ✅ 正确：泛型 ValueTuple 重载直接解构
var (users, orders) = connection.AsDto<UserEntity>()
    .QueryMultiple<UserDto, OrderDto>("SELECT * FROM Users; SELECT * FROM Orders", null);
```

### 误用 3：ToTreeList 缺少 [MapTree] 标注

```csharp
// ❌ 误用：DTO 未标注 [MapTree]，ToTreeList 无法识别父 ID 字段
[MapFrom(typeof(CategoryEntity))]
public class CategoryDto
{
    public int NodeId { get; set; }
    public int? ParentNodeId { get; set; }  // 无 [MapTree] → 树形构建失败
    public List<CategoryDto> Children { get; set; } = new();
}

// ✅ 正确：标注 [MapTree] 指定父 ID 字段
[MapFrom(typeof(CategoryEntity))]
[MapTree("NodeId", nameof(CategoryEntity.ParentNodeId))]  // 位置参数形式（；键名须用实体主键 NodeId）
public class CategoryDto
{
    public int NodeId { get; set; }
    public int? ParentId { get; set; }
    public List<CategoryDto> Children { get; set; } = new();
}
```

### 误用 4：ToChunkDto 批次大小过小

```csharp
// ❌ 误用：批次大小过小（如 1），导致大量数据库往返
connection.AsDto<UserEntity>()
    .ToChunkDto<UserDto>("SELECT * FROM Users", null, 1, chunk =>
    {
        all.AddRange(chunk);  // 每批 1 条 → 10000 条数据 = 10000 次 SQL 查询
    });

// ✅ 正确：批次大小合理（100-1000）
connection.AsDto<UserEntity>()
    .ToChunkDto<UserDto>("SELECT * FROM Users", null, 500, chunk =>
    {
        all.AddRange(chunk);  // 每批 500 条 → 10000 条数据 = 20 次 SQL 查询
    });
```

---

## 小结

| 能力 | API | 特性 |
|---|---|---|
| 字典 | `.ToDictionary<TKey, TDto>(sql, param, keySelector)` | Key 作用于 Entity |
| 分批读取 | `.ToChunkDto<TDto>(sql, param, size, action)` | SQL 分页逐批 |
| 异步分批 | `.ToChunkDtoAsync<TDto>(sql, param, size, action)` | 异步变体 |
| 多结果集 | `.QueryMultiple<TDto1, TDto2>(sql, param)` | 泛型 ValueTuple 解构多组数据 |
| 树形投影 | `.ToTreeList<TDto>(sql, param)` | `[MapTree]` 标注父 ID |

## Fluent DTO 支持现状

本篇各终端（`ToTreeList`/`QueryMultiple`/`ToChunkDto`）对 Fluent DTO 的支持口径与 Dapper Query 篇一致（快照注册表命中选择映射器，内存映射全语义）。`ToTreeList` 树构建消费 `Tree()`/`Tree(id, parent, children)` 声明的快照承载（与 `[MapTree]` 特性同口径，opt-in）；`QueryMultiple` 非泛型推断读 `[MapFrom]`，Fluent DTO 使用显式泛型入口。

---


本页可运行演示（场景 502，SQLite 内存库）：

[!code-csharp[高级终端群：分页/字典/分组/分批/树查询](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced)]

[!code-csharp[高级终端群：分页/字典/分组/分批/树查询 · ToDictionary](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToDictionary)]

[!code-csharp[高级终端群：分页/字典/分组/分批/树查询 · ToLookup](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToLookup)]

[!code-csharp[高级终端群：分页/字典/分组/分批/树查询 · ToChunkDto](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToChunkDto)]

[!code-csharp[高级终端群：分页/字典/分组/分批/树查询 · ToTreeList](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToTreeList)]

## 配套示例

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 502
```

代码见 `samples/NCMapper.Samples/05-Orm/DapperScenarios.cs`，涵盖 ToDictionary、ToChunkDto、QueryMultiple、ToTreeList 等子场景。

---

## 进一步阅读

- [Dapper（一）：Query 系列](dapper-query.md) · [FreeSql（一）](freesql-single-table.md)
- 技术文档：[EF Core 与 Dapper 扩展包](../../explanation/orm-efcore-dapper.md) §11.4
- 扩展源码：`src/NCMapper.Dapper/NCMapperDapperExtensions.cs`
