<!-- 来源：NCMapper 主技术文档 §11.2 NCMapper.FreeSql 扩展包 -->
### 11.2 NCMapper.FreeSql 扩展包

#### 安装与引用

```xml
<PackageReference Include="NCMapper.FreeSql" />
```

#### 单表查询

各项可运行版本（`using NCMapper;` + `NCMapper.FreeSql` 扩展；演示条件按样本数据设定）：

[!code-csharp[列表投影 ToList](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto)]
[!code-csharp[单条投影 First](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto_SingleTableFirst)]
[!code-csharp[异步终端巡览：ToListAsync 与 FirstAsync](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_FreeSqlAsyncTerminals)]

#### 多表 Compose 查询

FreeSql 的 `ISelect<T1, T2>` / `ISelect<T1, T2, T3>` 多表接口同样支持 `AsDto()`：

```csharp
// 双表 — DTO 通过 [MapProperty(typeof(CustomerEntity), ...)] 声明跨表字段
var dtos = fsql.Select<OrderEntity, CustomerEntity>()
    .AsDto()
    .ToList<OrderDetailDto>();

// 三表
var dtos = fsql.Select<OrderEntity, CustomerEntity, ProductEntity>()
    .AsDto()
    .ToList<OrderFullDto>();
```

默认走**投影表达式路径**（`ISelect<T1,T2>.ToList(expr)` 在 SQL 侧完成多表字段映射）；仅当导航属性无法翻译为 SQL 时才降级：查询 `Tuple<T1, T2>` 实体 → 逐条 `Mapper.Compose<TTarget>(item1, item2)` 内存组合映射。

#### 分页查询

[!code-csharp[ToPagedListAsync 异步分页（page/TotalPages/TotalCount 状态可在返回的 PagedList 上读取）](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_FreeSqlPagedAsync)]

分页在 SQL 侧完成（两条路径均如此）：先 `Count()` 获取总数，再 `Page(page, pageSize)` 分页查询。

#### 可用终端方法

| 方法                                              | 说明                                             |
| ----------------------------------------------- | ---------------------------------------------- |
| `ToList<TTarget>()`                             | 查询列表映射为 DTO                                    |
| `ToListAsync<TTarget>(ct)`                      | 异步查询列表                                         |
| `First<TTarget>()`                              | 查询单条映射为 DTO                                    |
| `FirstAsync<TTarget>(ct)`                       | 异步查询单条                                         |
| `ToPagedList<TTarget>(page, pageSize)`          | 分页查询                                           |
| `ToPagedListAsync<TTarget>(page, pageSize, ct)` | 异步分页查询                                         |
| **`ToChunkDto<TTarget>(size, done)`**           | 分块查询 + 回调，每批映射为 DTO（替代 ToChunk）                |
| `ToTreeList<TTarget>()`                         | 树形 DTO 投影（自引用树 DTO 须含 `List<TTarget>` 子集合导航属性） |
| `ToTreeListAsync<TTarget>(ct)`                  | 异步树形 DTO 投影                                    |
| `ToDictionary<TTarget>(keySelector)`            | 字典投影（`keySelector` 为 `Func<>` 内存执行，无需 SQL 翻译）  |
| `ToDictionaryAsync<TTarget>(keySelector, ct)`   | 异步字典投影                                         |

**ToChunkDto 分块查询**：

[!code-csharp[FreeSql ToChunkDto 回调式分块处理](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_ToChunkDto)]

#### 适用查询模式与边界

`AsDto()` 的本质是**实体→DTO 属性名映射**：NCMapper 根据 `[MapFrom]` / `[MapProperty]` 声明，生成 `Expression<Func<TSource, TTarget>>` 投影表达式交给 FreeSql 翻译为 SQL SELECT。因此，凡是 FreeSql 查询链最终仍以 `ISelect<TSource>` 形态返回**完整实体**的场景，均可在 `.AsDto()` 之前自由组合。

**兼容的查询模式**（AsDto 之前任意链式）：

前四项（WhereIf / WithSql / 子查询 Any / IncludeMany）的可运行版本：

[!code-csharp[WhereIf 动态条件](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_WhereIfFilter)]
[!code-csharp[WithSql 子查询作为数据源](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_WithSqlAsDto)]
[!code-csharp[子查询过滤（Exists / In，Any 谓词）](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_AnySubquery)]
[!code-csharp[Include / IncludeMany（降级路径自动处理集合导航）](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_IncludeManyNavigation)]

其余项（FreeSql 原生链形态，NCMapper 仅在其后消费 AsDto）：

```csharp
// Count(out total) + Page 手动分页
fsql.Select<UserEntity>()
    .Where(u => u.IsActive)
    .Count(out var total)
    .Page(pageIndex, pageSize)
    .AsDto().ToList<UserDto>();

// OrderBy / OrderByDescending
fsql.Select<UserEntity>()
    .OrderByDescending(u => u.CreatedAt)
    .AsDto().ToList<UserDto>();

// WhereCascade 广播条件
fsql.Select<OrderEntity>()
    .LeftJoin(o => o.Customer.Id == o.CustomerId)
    .WhereCascade(x => x.IsDeleted == false)
    .AsDto().ToList<OrderDto>();

// 导航属性联表（自动 LeftJoin）
fsql.Select<OrderEntity>()
    .LeftJoin(o => o.Customer.Id == o.CustomerId)
    .AsDto().ToList<OrderDto>();
```

**不兼容的场景及替代写法**：

| 场景                         | 原因                                                                                                                        | 替代写法                                                 |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------- |
| `GroupBy` 聚合               | **编译错误**（`[Obsolete(error:true)]`） — GroupBy 后返回 `ISelectGrouping`，结果不再是实体而是聚合值                                           | **推荐**：FreeSql 原生聚合投影（SQL 侧 GROUP BY）；**不推荐**：先 `.AsDto().ToList()` 再在内存中 GroupBy（全量物化，性能损失大） |
| `ToChunk` 分块流式             | ToChunk 返回 void，无返回值供 AsDto 链式                                                                                            | 使用 `.AsDto().ToChunkDto(size, done)`                 |
| `ToTreeList` 树形            | **已支持**：`AsDto().ToTreeList<TTarget>()` / `ToTreeListAsync<TTarget>()` 直接生成树形 DTO 投影（自引用树 DTO 须含 `List<TTarget>` 子集合导航属性） | 详见 [freesql-advanced 树形投影](../how-to/orm/freesql-advanced.md)                          |
| FreeSql 原生 `ToList<Dto>()` | FreeSql 自身的 DTO 映射（按属性名匹配，无 NCMapper 特性支持）                                                                                | 需要特性链用 AsDto；简单同名映射两者皆可                              |

**GroupBy 示例（编译期错误 + 替代写法）**：

```csharp
// ✗ 编译错误 — [Obsolete(error:true)] 阻止编译
// fsql.Select<OrderEntity>().GroupBy(a => a.Status).AsDto().ToList<OrderStatsDto>();
// 错误信息: "FreeSql GroupBy 后不可使用 AsDto。替代写法：① 先.AsDto().ToList() 再在内存中 GroupBy；② 使用 FreeSql 原生 Select 聚合语法"
// 注：错误信息中的 ①② 是并列列举，不代表优先级；实际应优先选用 ② 原生聚合

// ✓ 推荐 — FreeSql 原生聚合投影（SQL 侧完成 GROUP BY）
var stats = fsql.Select<OrderEntity>()
    .GroupBy(a => a.Status)
    .ToList(a => new OrderStatsDto
    {
        Status = a.Key,
        Count  = a.Count(),
        Total  = a.Sum(b => b.Amount)
    });

// ⚠ 不推荐 — 先取内存再 LINQ 分组（仅在无法用 SQL 聚合表达时使用）
// 需先把全量（或过滤后）明细物化到内存，失去 SQL 侧 GROUP BY 下推与索引加速
var dtos = fsql.Select<OrderEntity>().AsDto().ToList<OrderDto>();
var grouped = dtos.GroupBy(d => d.Status);
```

> **设计原则**：AsDto 解决的是"实体→DTO 自动映射"问题（属性匹配、类型转换、嵌套导航、脱敏、自定义转换器）。聚合投影是"表达式→字段"问题，需要用户显式指定聚合函数和目标列，NCMapper 在此无自动化空间。两者职责不同，不互相替代。

#### 树形投影（ToTreeList）

`ToTreeList<TTarget>()` / `ToTreeListAsync<TTarget>()` 直接生成树形 DTO 投影，无需先取实体树再递归 `Mapper.Map`：

```csharp
// 自引用树 DTO：须含元素类型为自身的子集合导航属性
[MapFrom(typeof(MenuEntity))]
public class MenuDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<MenuDto> Children { get; set; }   // 自引用子集合（树形必需）
}
```

树形投影的可运行版本（链形态一致，演示类型对为 Wiki 树族）：

[!code-csharp[ToTreeList 树形投影（自动 LEFT JOIN 父子关系 + MapList 组装树）](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_ToTreeList)]

约束与兼容性：

- DTO 必须包含**自引用集合属性**（元素类型为 `TTarget` 自身），否则入口校验 `ValidateTreeDtoTarget` 报错；
- 与 `Include` / `AsTreeCte` 兼容，`ApplyFreeSqlIncludes` 可自动补全导航；
- 跨表 `[MapProperty]` 经 `Include` 补全后仍可用于树形节点。

#### 字典投影（ToDictionary）

`ToDictionary<TTarget>(keySelector)` / `ToDictionaryAsync<TTarget>(keySelector, ct)` 将投影结果组装为字典。`keySelector` 为 `Func<>`（内存执行，无需 SQL 翻译）：

[!code-csharp[ToDictionary 字典投影可运行版本（keySelector 作用于源实体）](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_ToDictionary)]

#### AsDto 带映射转手

当 DTO 已标注 `[MapFrom]`（携带完整映射配置）时，AsDto 的终端方法走\*\*「原生终端方法 + Map / MapList」转手路径\*\*：直接调用 ORM 原生终端（如 FreeSql `ToList`、EFCore `ToListAsync`）取实体，再用 NCMapper 内存映射转手为 DTO。**该路径不走 `NeedsDegradation` 检测**，是确定性的高性能路径（仅当投影表达式不可翻译才降级，而带映射转手固定转手）。`ToList` / `First` / `ToPagedList` / `ToChunkDto` 等所有终端方法均支持此模式。

