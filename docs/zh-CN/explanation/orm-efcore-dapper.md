<!-- 来源：NCMapper 主技术文档 §11.3-11.4 EF Core 与 Dapper 扩展包 -->
### 11.3 NCMapper.EFCore 扩展包

#### 安装与引用

```xml
<PackageReference Include="NCMapper.EFCore" />
```

#### 用法

各终端的可运行版本（`using NCMapper;` + `NCMapper.EFCore` 扩展；演示条件按样本数据设定）：

[!code-csharp[列表投影 ToList](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_SingleTableAsDto)]
[!code-csharp[单条投影 First](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_SingleTableAsDto_SingleTableFirst)]
[!code-csharp[多表投影——在 AsDto() 之前用 Include 加载导航属性](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_NestedCrossTable)]
[!code-csharp[异步终端巡览：ToListAsync 与 ToPagedListAsync](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_EFAsyncTerminals)]

EF Core 多表场景通过 `Include()` 在 `AsDto()` 之前加载导航属性，降级路径由 `MapList` 自动处理嵌套导航属性映射。终端方法与 FreeSql 包对称，并额外提供 `ToLookup` 与 `ToChunkDtoAsync`：

| 方法                                                                                       | 说明                     |
| ---------------------------------------------------------------------------------------- | ---------------------- |
| `ToList<TTarget>()` / `ToListAsync<TTarget>(ct)`                                         | 列表 / 异步列表映射            |
| `First<TTarget>()` / `FirstAsync<TTarget>(ct)`                                           | 单条 / 异步单条映射            |
| `Single<TTarget>()` / `SingleAsync<TTarget>(ct)`                                         | 恰好一条 / 异步恰好一条映射（零条或多条抛异常） |
| `ToPagedList<TTarget>(page, pageSize)` / `ToPagedListAsync<TTarget>(page, pageSize, ct)` | 分页 / 异步分页              |
| `ToChunkDto<TTarget>(size, done)` / `ToChunkDtoAsync<TTarget>(size, done, ct)`           | 分块 / 异步分块（回调内为已映射 DTO） |
| `ToDictionary<TTarget>(keySelector)` / `ToDictionaryAsync<TTarget>(keySelector, ct)`     | 字典 / 异步字典投影            |
| `ToLookup<TTarget>(keySelector)`                                                         | 查找（`ILookup`）投影        |

> **`Single` / `SingleAsync` 异常消息**：零条或多条记录时抛 `InvalidOperationException`，消息取自本库错误资源、**含目标 DTO 类型名**，并随当前 UI 区域性在中英文之间切换；与 FreeSql 包**同场景同口径**（两侧消息文本一致）。

**代码示例：**

```csharp
// ToChunkDtoAsync — 流式分批处理大结果集
await dbContext.Orders
    .Where(o => o.IsActive)
    .AsDto()
    .ToChunkDtoAsync<OrderDto>(size: 500, async chunk =>
    {
        foreach (var dto in chunk)
            await ProcessOrderAsync(dto);
    });
```

前三项的可运行版本：

[!code-csharp[SingleAsync 恰好一条（异步）](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_SingleTableAsDto_SingleTableSingle)]
[!code-csharp[ToLookup 按键分组构建 ILookup](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_ToLookup)]
[!code-csharp[ToDictionaryAsync 按键构建字典（异步）](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_ToDictionaryAsync)]

#### AsNoTracking 默认行为

EFCore 扩展包**默认对所有查询追加 `AsNoTracking()`**，投影和降级路径均不产生变更跟踪实体。这减少了 DbContext 内存开销，符合"只读查询"的使用场景。

默认不跟踪与显式 `AsTracking` 对照的可运行版本（ChangeTracker 形态差异见集成测试 `AsTracking_Explicit_Keeps_Tracking`）：

[!code-csharp[默认查询不跟踪 + 显式 AsTracking 对照](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_AsTrackingExplicit)]

**全局关闭 AsNoTracking**：

```csharp
NCMapperOptions.EFCoreProjectionAsNoTrackingDefault = false;
```

#### 不兼容场景及替代写法

| 场景                       | 原因                                                                      | 替代写法                                                |
| ------------------------ | ----------------------------------------------------------------------- | --------------------------------------------------- |
| `GroupBy` 后 AsDto        | **编译错误**（`[Obsolete(error:true)]`） — 返回 `IQueryable<IGrouping<>>` 非单条实体 | **推荐**：EF Core 原生 `Select` 聚合（SQL 侧 GROUP BY）；**不推荐**：先 `.AsDto().ToList()` 再内存 GroupBy（全量物化，性能损失大） |
| `AsEnumerable()` 后 AsDto | **编译错误**（`[Obsolete(error:true)]`） — 已脱离 IQueryable 域                   | 使用 `Mapper.MapList<TSource, TTarget>(items)`        |
| `ToList()` 后 AsDto       | 同上 — 结果为 `List<T>` 非 `IQueryable<T>`                                    | 数据已加载到内存，直接用 `Mapper.MapList`                       |

### 11.4 NCMapper.Dapper 扩展包

#### 安装与引用

```xml
<PackageReference Include="NCMapper.Dapper" />
```

#### 用法

Dapper 包以 `AsDto<TSource>()` 为**唯一入口**，返回 `DapperDtoSelector<TSource>`，所有查询与终端方法均经此选择器调用。内部通过 Dapper 原生 `Query<TSource>` 查询源实体，再经 `Mapper.MapList` 走**完整 NCMapper 管道**：`[MapProperty]` 列名映射 → `[Convert]` 值转换 → `[Mask]` 脱敏 → Pipeline PostMap，全部特性永远生效（无"投影路径"与"降级路径"之分，始终为内存映射全管道）。

```csharp
using NCMapper;
using NCMapper.Dapper;

// DTO 标注
[MapFrom(typeof(OrderEntity))]
public class OrderDetailDto
{
    public int Id { get; set; }

    [MapProperty(typeof(OrderEntity), "OrderNo")]
    public string OrderNumber { get; set; }

    [MapProperty(typeof(OrderEntity), "CustomerName")]
    public string Customer { get; set; }

    [Mask]
    public string Phone { get; set; }
}

// 所有终端方法通过 AsDto<TSource>() 入口链式调用
using var connection = new SqlConnection(connectionString);
```

三项的可运行版本（链形态一致，演示类型对与条件按样本数据设定）：

[!code-csharp[列表查询 Query](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Query)]
[!code-csharp[分页查询 ToPagedList（SQL 级分页，方言自动检测）](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced)]
[!code-csharp[指定方言 AsDto(dialect) 入口](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_PagedWithDialect)]

> **管道行为声明**：所有终端方法永远走 `Mapper.MapList` 完整管道。Mask / Convert / Pipeline 在所有路径全部生效，无需也不能"注册"或"选择路径"。

#### 可用终端方法

`DapperDtoSelector<TSource>` 提供的全部公共方法如下。除 `Query` / `QueryAsync` 外，所有终端方法内部均查询源实体后经 `Mapper.MapList` 映射——即始终走完整 NCMapper 管道。

| 方法系列 | 同步 | 异步 | 说明 |
| -------- | ---- | ---- | ---- |
| **Query** | `Query<TDto>(sql, param?)` | `QueryAsync<TDto>(sql, param?, ct?)` | 查询实体列表 → MapList 映射为 DTO 列表 |
| **Query（CommandDefinition）** | `Query<TDto>(CommandDefinition)` | `QueryAsync<TDto>(CommandDefinition)` | 通过 CommandDefinition 传递 CancellationToken 等全部参数 |
| **QueryFirst** | `QueryFirst<TDto>(sql, param?)` | `QueryFirstAsync<TDto>(sql, param?, ct?)` | 返回第一条记录映射为 DTO（无记录时抛异常） |
| **QueryFirstOrDefault** | `QueryFirstOrDefault<TDto>(sql, param?)` | `QueryFirstOrDefaultAsync<TDto>(sql, param?, ct?)` | 返回第一条记录映射为 DTO（无记录时返回 `default`） |
| **QuerySingle** | `QuerySingle<TDto>(sql, param?)` | `QuerySingleAsync<TDto>(sql, param?, ct?)` | 返回**恰好一条**记录映射为 DTO（零或多条记录抛异常） |
| **QuerySingleOrDefault** | `QuerySingleOrDefault<TDto>(sql, param?)` | `QuerySingleOrDefaultAsync<TDto>(sql, param?, ct?)` | 返回恰好一条记录映射为 DTO（零条返回 `default`，多条抛异常） |
| **ToPagedList** | `ToPagedList<TDto>(sql, param?, page, pageSize)` | `ToPagedListAsync<TDto>(sql, param?, page, pageSize, ct?)` | SQL 级分页（先 Count 后分页），返回 `PagedList<TDto>` |
| **ToDictionary** | `ToDictionary<TKey,TDto>(sql, param?, keySelector)` | `ToDictionaryAsync<TKey,TDto>(sql, param?, keySelector, ct?)` | 按 `keySelector` 分组构建 `Dictionary<TKey, TDto>` |
| **ToLookup** | `ToLookup<TKey,TDto>(sql, param?, keySelector)` | `ToLookupAsync<TKey,TDto>(sql, param?, keySelector, ct?)` | 按 `keySelector` 分组构建 `ILookup<TKey, TDto>` |
| **ToChunkDto** | `ToChunkDto<TDto>(sql, param?, size, done)` | `ToChunkDtoAsync<TDto>(sql, param?, size, done, ct?)` | 分块查询 + 回调，每批映射为 DTO，适合大结果集分批处理 |
| **QueryUnbufferedAsync** | — | `QueryUnbufferedAsync<TDto>(sql, param?, ct?)` | 异步流式枚举（`IAsyncEnumerable<TDto>`），适合逐条处理超大结果集 |
| **ToTreeList** | `ToTreeList<TDto>(sql, param?)` | `ToTreeListAsync<TDto>(sql, param?, ct?)` | 树形结构映射，DTO 的 `Id`/`ParentId` 字段名可通过 `[MapTree]` 自定义 |
| **QueryMultiple（2 结果集）** | `QueryMultiple<TDto1,TDto2>(sql, param?)` | — | 多结果集查询，返回 `ValueTuple<List<TDto1>, List<TDto2>>` |
| **QueryMultiple（3 结果集）** | `QueryMultiple<TDto1,TDto2,TDto3>(sql, param?)` | — | 三结果集查询，返回 `ValueTuple<List<TDto1>, List<TDto2>, List<TDto3>>` |

> **QueryFirst vs QuerySingle 语义差异**：`QueryFirst` 取第一条（零条抛异常），`QuerySingle` 要求恰好一条（零条或多条均抛异常）。后者适合按主键查询、唯一约束查询等确定性场景。

#### QuerySingle 系列

在按主键或唯一约束查询且期望恰好一条记录时使用：

确保恰好一条（零条或多条均抛异常）——

[!code-csharp[QuerySingle 可运行版本（按主键恰好一条）](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QuerySingle)]

允许零条，多条抛异常——

[!code-csharp[QuerySingleOrDefault 可运行版本（恰好一条或 null）](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QuerySingleOrDefault)]

#### QueryUnbufferedAsync 流式枚举

适合千万级结果集的逐条处理，避免一次性加载全量到内存：

[!code-csharp[QueryUnbufferedAsync 异步流式枚举可运行版本（语义同 QueryUnbuffered，仅入参 SQL 不同）](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_QueryUnbufferedAsync)]

#### QueryMultiple 多结果集

一次 SQL 返回多组结果集，每组独立映射为对应 DTO 列表：

两结果集（双泛型重载）——

[!code-csharp[QueryMultiple 两结果集可运行版本](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_QueryMultiple)]

三结果集（三泛型重载）——

[!code-csharp[QueryMultiple 三结果集可运行版本](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_QueryMultiple3)]

#### ToTreeList 树形结构

查询扁平列表后按 `Id`/`ParentId` 关系构建树。DTO 的字段名默认为 `Id` / `ParentId`，非默认字段名通过 `[MapTree]` 特性自定义（详见 [§5 特性系统](../reference/features.md) 的 [MapTree] 小节）：

```csharp
// 自定义字段名的树形 DTO
[MapTree("NodeId", "ParentNodeId")]
public class CategoryDto
{
    public int NodeId { get; set; }
    public int ParentNodeId { get; set; }
    public string Name { get; set; }
    public List<CategoryDto> Children { get; set; }
}
```

[!code-csharp[ToTreeList 树形投影可运行版本（消费上方 CategoryDto 形态）](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_ToTreeList)]

> **适用边界**：[MapTree] 由 Dapper `ToTreeList` 与 EF Core `ToTreeList`/`ToTreeListAsync` 读取；FreeSql `ToTreeList` 为 FreeSql 原生树构建，不读 [MapTree]。EF Core 无原生树查询，采用"平铺查询 + 内存建树"（核心库 `TreeBuilder` 按 Id/ParentId 组装），读 [MapTree]，语义与 Dapper 一致。详见 [how-to §6 树形查询](../how-to/orm/efcore-projection.md)。

#### 方言体系（ISqlDialect）

`AsDto<TSource>(connection, dialect?)` 的 `dialect` 参数允许显式传入 SQL 方言实例。`null` 时由 `SqlDialectResolver.Resolve(connection)` 按 `IDbConnection` 类型名自动检测。方言对象仅影响两个 SQL 改写行为：

- **`CountSql(originalSql)`** — 将查询 SQL 改写为 COUNT 语句（用于分页前获取总数）。大多数方言剥离最外层 `ORDER BY`；Oracle 方言通过子查询包装保留内层 `ORDER BY`。
- **`PageSql(originalSql, offsetParam, pageSizeParam)`** — 在原始 SQL 上追加分页子句（`LIMIT` / `OFFSET FETCH` / `ROWNUM` 等），根据不同数据库语法差异自动处理 `ORDER BY (SELECT NULL)` 等兼容性细节。

**内置方言列表：**

| 方言类 | 适用数据库 | 命名空间 |
| ------ | ---------- | -------- |
| `SqlServerDialect` | SQL Server（2012+） | `NCMapper.Dapper` |
| `MySqlDialect` | MySQL / MariaDB | `NCMapper.Dapper` |
| `PostgreSqlDialect` | PostgreSQL | `NCMapper.Dapper` |
| `SqliteDialect` | SQLite | `NCMapper.Dapper` |
| `OracleDialect` | Oracle | `NCMapper.Dapper` |
| `Db2Dialect` | IBM Db2 | `NCMapper.Dapper` |

**显式指定方言：**

[!code-csharp[Dapper 显式方言注入（AsDto 入口传 ISqlDialect）分页](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_PagedWithDialect)]

**自定义方言：**

实现 `ISqlDialect` 接口的 `CountSql` / `PageSql` 两个方法即可。常用于包装内置方言并在改写时附加日志、或适配非标准 SQL 数据库：

```csharp
public class LoggingSqliteDialect : ISqlDialect
{
    private readonly SqliteDialect _inner = new();

    public string CountSql(string sql)
    {
        var countSql = _inner.CountSql(sql);
        Console.WriteLine($"[Count] {countSql}");
        return countSql;
    }

    public string PageSql(string sql, string offsetParam, string pageSizeParam)
    {
        var pageSql = _inner.PageSql(sql, offsetParam, pageSizeParam);
        Console.WriteLine($"[Page] {pageSql}");
        return pageSql;
    }
}

// 使用自定义方言
var page = connection.AsDto<OrderEntity>(new LoggingSqliteDialect())
    .ToPagedList<OrderDto>(sql, param, 1, 20);
```

> **已知差异**：Oracle `CountSql` 通过子查询包装保留内层 `ORDER BY`（与 SqlServer/DB2 剥离顶层 `ORDER BY` 行为不同）；`PageSql` 中 `ORDER BY (SELECT NULL)` 自动追加策略因数据库而异。

