---
wiki-id: how-to/orm/freesql-advanced
涉及诊断码: （无）
库版本: v1.0.0+
更新时间: 2026-09-15
---

# FreeSql 投影（三）：高级终端方法

## 概述

本篇覆盖 FreeSql 集成的高级终端方法：`ToTreeList`、`ToDictionary`、`ToChunkDto`、`ToListAsync`，以及异步变体。这些方法在 `AsDto()` 后链式调用，覆盖树形数据、字典、分批读取、异步等场景。

核心 API：`.AsDto().ToTreeList<TDto>()` / `.AsDto().ToDictionary<TDto>(keySelector)` / `.AsDto().ToChunkDto<TDto>(size, action)`

---

## 1. ToTreeList：树形数据投影

### 用法

将扁平的父子结构数据（含 `ParentId` 字段）映射为树形 DTO 列表：

> ⚠️ **FreeSql ToTreeList 不读 `[MapTree]` 特性**。FreeSql 使用原生树构建能力（基于 `ParentId` 字段名约定或 `[Navigate(nameof(ParentId))]` 特性）。`[MapTree]` 仅 Dapper ToTreeList 读取（见 [Dapper（二）](dapper-advanced.md)）。

```csharp
using FreeSql.DataAnnotations;
using NCMapper.FreeSql;

public class CategoryEntity
{
    public int NodeId { get; set; }
    public int? ParentNodeId { get; set; }  // 父节点 ID（FreeSql 约定字段名）
    public string Name { get; set; } = "";

    // 自引用导航属性 — FreeSql 原生树构建依据，也是 DTO 子集合的映射源
    [Navigate(nameof(ParentNodeId))]
    public List<CategoryEntity> Children { get; set; } = new();
}

[MapFrom(typeof(CategoryEntity))]
public class CategoryDto
{
    public int NodeId { get; set; }
    public string Name { get; set; } = "";

    [MapProperty(typeof(CategoryEntity), nameof(CategoryEntity.ParentNodeId))]
    public int? ParentId { get; set; }

    // 与实体导航属性同名 — MapList 按名递归映射（勿标 [MapIgnore]）
    public List<CategoryDto> Children { get; set; } = new();
}

```

[!code-csharp[FreeSql ToTreeList 树形投影](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_ToTreeList)]

**要点**：

- 实体需有父键字段与自引用导航属性（`[Navigate(nameof(父键))]` 一对多集合，FreeSql 树形查询约定）
- **DTO 子集合属性参与 `MapList` 按名映射**，须与实体自引用导航属性**同名**（元素类型为 DTO 自身，递归映射）；勿标 `[MapIgnore]`——否则同名也静默扁平（子集合恒空），且同名硬校验无法拦截
- 返回 `List<TDto>`，其中根节点（父键为 null）在顶层，`Children` 属性递归填充
- 支持多层嵌套，无层数限制（受 `MaxRecursionDepth` 控制）
- 实体任一自引用导航名与 DTO 任一自引用集合名同名即通过（任一命中）；全不匹配抛 `MapConfigurationException`（同名硬校验，见下方警示）

> ⚠️ **同名硬校验**：FreeSql `ToTreeList`/`ToTreeListAsync` 入口校验 DTO 自引用集合属性名与实体自引用导航属性名——**全不匹配时抛 `MapConfigurationException`**（此前行为：静默丢子树、仅返回根节点，`Children` 恒空）。异常消息列出两侧全部候选属性名并含迁移引导：将 DTO 子集合属性重命名为与实体导航同名（或调整实体导航属性名）。注意 FreeSql 不读 `[MapTree]`，自定义子集合属性名在此路径无效（Dapper 树契约见 [Dapper（二）](dapper-advanced.md)）；实体无自引用导航属性时跳过校验（普通非树场景零影响）。

---

## 2. ToChunkDto：分批读取

### 用法

大数据量场景的分批回调，每批 size 条，走客户端内存映射路径（编译投影委托在内存中逐条映射，
非 SQL 侧投影，说明如下）：

[!code-csharp[FreeSql 分批读取 ToChunkDto](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_ToChunkDto)]

**要点**：

- 大数据量场景的分批回调，每批 size 条
- 客户端内存映射：`ToChunk` 按批取实体后在内存中映射（内存占用按批可控，非 SQL 侧投影）
- 正常路径与降级路径均先自动 `Include` 导航属性（此前正常路径不 Include，致导航静默 null，已修复）
- 异步版本 `ToChunkDtoAsync<TDto>(size, asyncAction, cancellationToken)` 已提供——真异步实现：逐页 `Skip/Take` + `ToListAsync(ct)` 取数，每轮取数前检查取消令牌（取消后不再触发回调）
- 异步版与同步版的内存语义差异：同步版走 FreeSql 原生 `ToChunk` 流式分块，异步版为逐页查询（每页仅物化 size 条，内存占用仍按批可控）
- `Skip/Take` 分页依赖稳定排序：请在查询上自备 `OrderBy`，否则大结果集跨页取数可能静默漏行/重行

---

## 3. ToDictionary

### 用法

将查询结果映射为字典，Key 由实体属性选择：

[!code-csharp[FreeSql ToDictionary 字典投影](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_ToDictionary)]

**要点**：

- Key 选择器作用于 `TSource`（源实体），非 `TDto`（实现签名 `ToDictionary<TKey, TTarget>(Func<TSource, TKey>)`）
- 返回 `Dictionary<TKey, TDto>`
- 如果有重复 Key，后者覆盖前者

### 多表查询的 ToDictionary

```csharp
var dict = fsql.Select<OrderEntity, CustomerEntity>()
    .LeftJoin((o, c) => o.UserId == c.Id)
    .AsDto()
    .ToDictionary<int, OrderDetailDto>(o => o.OrderId);  // keySelector 参数为主表实体
```

### ToLookup

将查询结果映射为分组查找表（`ILookup<TKey, TDto>`）：

[!code-csharp[FreeSql 分组查找表 ToLookup（keySelector 作用于源实体）](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlAdvanced_ToLookup)]

**要点**：

- Key 选择器作用于 `TSource`（源实体），非 `TDto`（实现签名 `ToLookup<TKey, TTarget>(Func<TSource, TKey>)`）
- 返回 `ILookup<TKey, TDto>`；`ILookup` 索引器对不存在的键返回空序列（不抛异常）
- FreeSql 3.5.103 不提供 ToLookup/ToLookupAsync，实现为同步 `ToList` 取实体 + 内存分组（每组批量 `MapList` 映射）

---

## 4. 异步终端方法

| 方法 | 说明 | 返回类型 |
|---|---|---|
| `.ToListAsync<TDto>()` | 异步列表 | `Task<List<TDto>>` |
| `.FirstAsync<TDto>()` | 异步首条（无记录时返回 `null`） | `Task<TDto?>` |
| `.ToDictionaryAsync<TKey, TDto>()` | 异步字典 | `Task<Dictionary<TKey, TDto>>` |
| `.ToChunkDtoAsync<TDto>(size, action)` | 异步分批 | `Task` |

### 用法

```csharp
// 异步列表
var dtos = await fsql.Select<UserEntity>()
    .Where(u => u.Age > 20)
    .AsDto()
    .ToListAsync<UserDto>();

// 异步首条
var first = await fsql.Select<UserEntity>()
    .Where(u => u.Id == 1)
    .AsDto()
    .FirstAsync<UserDto>();
```

---

## 5. IncludeMany：子集合投影

当 DTO 包含子集合属性时，用 `IncludeMany` 预加载子集合数据：

```csharp
public class OrderEntity
{
    public int OrderId { get; set; }
    public List<OrderItemEntity> Items { get; set; } = new();
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public int OrderId { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

```

[!code-csharp[IncludeMany 预加载子集合后 AsDto 投影（场景 503）](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_IncludeManyNavigation)]

**要点**：

- `IncludeMany` 预加载子集合（一对多关系）
- 子集合字段裁剪：DTO 的子集合属性决定 IncludeMany 的 SELECT 列
- 投影路径下 `IncludeMany` 自动列裁剪；降级路径需手动处理

---

## 常见误用

### 误用 1：GroupBy + AsDto

```csharp
// ❌ 误用：GroupBy + AsDto 不受支持
// FreeSql GroupBy 后返回 ISelectGrouping<TKey,TValue>（分组聚合），不再是实体查询
// → 无法做实体→DTO 属性映射，NCMapper 用 [Obsolete(error:true)] 编译期拦截此用法
var dtos = fsql.Select<UserEntity>()
    .GroupBy(u => u.Age)
    .AsDto()  // 编译错误！
    .ToList<UserAgeGroupDto>();
```

> ℹ️ **拦截原因**：`GroupBy` 返回 `ISelectGrouping<TKey, TValue>`，元素是分组而非单条实体，不存在可供 NCMapper 做属性匹配的实体结构。NCMapper 以 `[Obsolete(error:true)]` 在编译期阻止该链式写法，并在错误信息中直接给出下述两条替代路径。

**替代写法（两条路径）**：

```csharp
// 路径 A（推荐）：FreeSql 原生聚合语法，在 SQL 侧完成 GROUP BY
// 聚合结果直接映射到匿名类型/DTO，无需 NCMapper 投影介入
var stats = fsql.Select<UserEntity>()
    .GroupBy(u => u.Age)
    .ToList(g => new { Age = g.Key, Count = g.Count() });
// 也可映射到 DTO：.ToList<UserAgeGroupDto>(g => new UserAgeGroupDto { ... })

// 路径 B（不推荐，仅提供实现途径）：先取内存再 LINQ 分组
// 先 .AsDto().ToList() 把对象取出到内存，再用 LINQ 的 .GroupBy() 完成分组
var rows = fsql.Select<UserEntity>()
    .AsDto()
    .ToList<UserDto>();                 // 全量数据加载到内存
var grouped = rows
    .GroupBy(d => d.Age)                // 在内存中分组
    .Select(g => new { Age = g.Key, Count = g.Count() })
    .ToList();
```

> ⚠️ **路径 B 不推荐**：必须先 `ToList` 把全量（或过滤后）数据拉进内存再客户端分组，**性能损失较大**，既多一次性全量物化开销，又失去 SQL 侧 `GROUP BY` 的下推与索引加速。若分组聚合可用 SQL 表达（计数/求和/均值等），**优先选用路径 A**；路径 B 仅作为确实无法用 SQL 聚合表达时的一种实现途径，请结合实际性能预算审慎选用。

### 误用 2：ToDictionary 的 keySelector 签名理解错误

`ToDictionary<TKey, TTarget>(Func<TSource, TKey> keySelector)` 的 keySelector 作用于**源实体（TSource）**（实现签名见 `NCMapperFreeSqlExtensions.ToDictionary`），不是投影后的 DTO（按 DTO 写法仅传单泛型参数时 TTarget 无法推断，直接编译错误）。

```csharp
// ✅ 正确：keySelector 作用于源实体（TSource），Key = UserEntity.Id
var dict = fsql.Select<UserEntity>()
    .AsDto()
    .ToDictionary<int, UserDto>(u => u.Id);

// ❌ 误用：仅传一个泛型参数（TTarget 无法推断 → CS0411 编译错误）
var dict = fsql.Select<UserEntity>()
    .AsDto()
    .ToDictionary<UserDto>(u => u.Id);
```

### 误用 3：在 FreeSql 场景误用 [MapTree]

```csharp
// ❌ 误用：FreeSql ToTreeList 不读 [MapTree] 特性
// [MapTree] 仅 Dapper ToTreeList 读取，FreeSql 使用原生树构建
[MapFrom(typeof(CategoryEntity))]
[MapTree("Id", nameof(CategoryEntity.ParentNodeId))]  // 位置参数形式（实现仅位置参数构造器，命名参数无法编译）
public class CategoryDto { ... }

// ✅ 正确：FreeSql ToTreeList 依赖父键字段名约定或 [Navigate] 特性
// DTO 无需标注 [MapTree]；子集合属性与实体自引用导航同名（参与按名映射，勿标 [MapIgnore]）
[MapFrom(typeof(CategoryEntity))]
public class CategoryDto
{
    public int NodeId { get; set; }
    public List<CategoryDto> Children { get; set; } = new();  // 与实体导航同名，按名递归映射
}
```

> `[MapTree]` 是 Dapper 集成专用的树形映射特性（支持自定义 IdProperty/ParentIdProperty 字段名）。FreeSql ToTreeList 基于 FreeSql 原生树构建能力，不读 `[MapTree]`。详见 [Dapper（二）：高级方法](dapper-advanced.md)。

---

## 小结

| 能力 | API | 特性 |
|---|---|---|
| 树形投影 | `.AsDto().ToTreeList<TDto>()` | FreeSql 原生树构建（不读 [MapTree]，同名硬校验） |
| 分批读取 | `.AsDto().ToChunkDto<TDto>(size, action)` | 客户端内存映射（按批可控）；异步版逐页真异步 |
| 分组查找表 | `.AsDto().ToLookup<TKey, TDto>(keySelector)` | Key 作用于源实体（TSource）；同步取实体 + 内存分组 |
| 字典 | `.AsDto().ToDictionary<TKey, TDto>(keySelector)` | Key 作用于源实体（TSource） |
| 异步列表 | `.AsDto().ToListAsync<TDto>()` | 异步变体 |
| 异步字典 | `.AsDto().ToDictionaryAsync<TKey, TDto>()` | 异步变体 |
| 子集合 | `.IncludeMany(o => o.Items)` | 一对多预加载 |
| GroupBy 拦截 | `[Obsolete(error:true)]` | 编译期阻止 |

## Compose 多源投影（双表/三表 AsDto）

FreeSql 多表 JOIN 场景下，NCMapper 提供 `NCMapperFreeSqlComposeExtensions` 的 `AsDto<T1, T2>()` / `AsDto<T1, T2, T3>()` 入口：把 `ISelect<(T1, T2)>` / `ISelect<(T1, T2, T3)>` 多表查询结果映射为合并 DTO。调用 `AsDto()` 后得到 `FreeSqlComposeDtoSelector<T1, T2>`（三元 `FreeSqlComposeDtoSelector3<T1, T2, T3>`）选择器，提供与单表投影同形的终端族（各 8 个：List/First 等形态）。

[!code-csharp[FreeSql 双表 Join AsDto Compose](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_MultiTableTwoWay)]

DTO 侧用 `[MapProperty]` 声明字段来自哪一源（与单表投影的同名匹配/异名规则一致）。本节示例即该形态的可运行演示。

## Fluent DTO 支持现状

- **ToTreeList 树构建**：Fluent 通道的 `Tree()`/`Tree(id, parent, children)` 声明与 `[MapTree]` 特性同享注册表承载（`RuntimeSnapshotStore.GetTreeConfig`，opt-in 语义——未声明不启用树挂载位判定），自定义三元组挂载与特性通道一致；
- **属性级 NoAutoInclude**：Fluent `Member(d => d.X)` 段内无参终端 `NoAutoInclude` 声明经注册表（`IsPropertyNoAutoInclude`）在导航收集时跳过该属性的导航 Include，语义镜像属性级 `[MapNoAutoInclude]`；
- **嵌套 Fluent 子 DTO**：投影翻译判定覆盖嵌套层（子 DTO 的不可翻译声明同样触发降级——嵌套链声明明细统一消费）。

---


本页可运行演示（场景 503，FreeSql 内存模式）：

[!code-csharp[异步分批读取：ToChunkDtoAsync（OrderBy 稳定排序前提）](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlAdvanced_ToChunkDtoAsync)]

[!code-csharp[分组查找表：ToLookup](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlAdvanced_ToLookup)]

## 配套示例

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 503
```

代码见 `samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs` 中的 `ToChunkDtoAsyncDemo`、`ToLookupDemo` 等方法；场景 501 另含同步分批与导航读取演示（`05-Orm/FreeSqlScenarios.cs` 的 `ToChunkDto` / `IncludeNavigation`）。

---

## 进一步阅读

- [FreeSql（一）：单表](freesql-single-table.md) · [FreeSql（二）：多表](freesql-multi-table.md)
- 技术文档：[NCMapper.FreeSql 扩展包](../../explanation/orm-freesql.md) §11.2
- 扩展源码：`src/NCMapper.FreeSql/NCMapperFreeSqlExtensions.cs`
