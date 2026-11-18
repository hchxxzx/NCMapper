---
wiki-id: how-to/orm/dapper-query
涉及诊断码: （无）
库版本: v1.0.0+
更新时间: 2026-09-15
---

# Dapper 集成（一）：Query 系列

## 概述

NCMapper 为 Dapper 提供统一的 `connection.AsDto<TEntity>()` 入口，链式调用 `.Query<TDto>()` / `.QueryFirst<TDto>()` / `.ToPagedList<TDto>()` 等终端方法。Dapper 集成与 FreeSql/EFCore 不同——SQL 由用户手写，NCMapper 负责结果集到 DTO 的映射。

核心 API：`connection.AsDto<TEntity>().Query<TDto>(sql, param)`

---

## 1. 基本查询

### 用法

[!code-csharp[Dapper 查询基础（using 与首查询形态）](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Query)]

**要点**：

- `AsDto<TEntity>()` 指定映射源实体类型（提供属性元数据）
- `Query<TDto>(sql, param)` 执行 SQL 并映射到 DTO
- 支持 Dapper 的所有参数化查询特性
- 需引入命名空间 `NCMapper.Dapper`

---

## 2. Query 终端方法

`AsDto<TEntity>()` 后可链式调用以下终端方法：

| 方法 | 说明 | 返回类型 |
|---|---|---|
| `.Query<TDto>(sql)` | 多行查询 | `IEnumerable<TDto>` |
| `.Query<TDto>(sql, param)` | 参数化多行查询 | `IEnumerable<TDto>` |
| `.QueryFirst<TDto>(sql)` | 首行 | `TDto` |
| `.QueryFirstOrDefault<TDto>(sql)` | 首行或默认值 | `TDto?` |
| `.QuerySingle<TDto>(sql)` | 单行（不唯一则抛异常） | `TDto` |
| `.QuerySingleOrDefault<TDto>(sql)` | 单行或默认值 | `TDto?` |
| `.QueryAsync<TDto>(sql)` | 异步查询 | `Task<IEnumerable<TDto>>` |
| `.QueryFirstOrDefaultAsync<TDto>(sql)` | 异步首行或默认 | `Task<TDto?>` |
| `.QueryUnbufferedAsync<TDto>(sql)` | 异步流式查询（逐元素枚举，内存占用恒定） | `IAsyncEnumerable<TDto>` |

> **流式终端仅此异步形态**：`QueryUnbufferedAsync` 没有同步版本（其余 Query 终端均同步/异步成对，它反之只有异步形态）；完整用法与 `ToChunkDto` 的对比见 [Dapper（二）：高级方法](dapper-advanced.md) 3.1 流式查询（源码锚：DapperDtoSelector.cs:575）。

### 各方法示例

[!code-csharp[Dapper QueryFirst 取首条](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QueryFirst)]
[!code-csharp[Dapper QueryFirstOrDefault 首条或默认](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QueryFirstOrDefault)]
[!code-csharp[Dapper QuerySingle 唯一一条](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QuerySingle)]
[!code-csharp[Dapper QuerySingleOrDefault 唯一或默认](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QuerySingleOrDefault)]

---

## 3. ToPagedList：SQL 级分页

### 用法

[!code-csharp[Dapper SQL 级分页 ToPagedList（方言自动检测）](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced)]

**要点**：

- 支持 6 种 SQL 方言自动检测（SQL Server / MySQL / PostgreSQL / SQLite / Oracle / DB2）
- 分页在 SQL 侧完成，非内存分页
- 参数：`(sql, param, page, size)` — page 从 1 开始
- 可显式传入 `ISqlDialect` 实现自定义方言

### SQL 方言

| 方言类 | 数据库 | 分页语法 |
|---|---|---|
| `SqlServerDialect` | SQL Server | `OFFSET ... ROWS FETCH NEXT ...` |
| `MySqlDialect` | MySQL | `LIMIT ... OFFSET ...` |
| `PostgreSqlDialect` | PostgreSQL | `LIMIT ... OFFSET ...` |
| `SqliteDialect` | SQLite | `LIMIT ... OFFSET ...` |
| `OracleDialect` | Oracle | `OFFSET ... ROWS FETCH NEXT ...` |
| `Db2Dialect` | DB2 | `OFFSET ... ROWS FETCH NEXT ...` |

### 显式指定方言

[!code-csharp[Dapper 显式指定方言（方言经 AsDto 入口注入）](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Advanced_PagedWithDialect)]

> 不显式传方言时，NCMapper 自动经 `SqlDialectResolver.Resolve(IDbConnection)` 按连接实例解析方言（对常见包装连接先做解包探测；无法识别时回退 MySQL 并输出一次性告警）——自定义连接包装类型时可自行调用同一入口取方言（源码锚：SqlDialects.cs:298，类声明 :284）。

---

### 3.1 自定义 ISqlDialect

内置 6 种方言覆盖主流数据库，但非标准数据库（或需要拦截分页 SQL）时，可实现 `ISqlDialect` 接口自定义计数与分页 SQL。内置方言类为 `sealed`，采用**组合模式**包装而非继承：

```csharp
public class LoggingSqliteDialect : ISqlDialect
{
    private readonly ISqlDialect _inner = new SqliteDialect();

    public string CountSql(string originalSql)
    {
        Console.WriteLine($"[CountSql] {originalSql}");
        return _inner.CountSql(originalSql);
    }

    public string PageSql(string originalSql, string offsetParam, string pageSizeParam)
    {
        Console.WriteLine($"[PageSql] {originalSql}");
        return _inner.PageSql(originalSql, offsetParam, pageSizeParam);
    }
}

// 包装内置方言并增加日志
var paged = connection.AsDto<UserEntity>(new LoggingSqliteDialect())
    .ToPagedList<UserDto>("SELECT * FROM Users", null, 1, 10);
```

`ISqlDialect` 接口两个方法：

| 方法 | 签名 | 职责 |
|---|---|---|
| `CountSql` | `string CountSql(string originalSql)` | 将原始 SQL 包装为子查询取 `COUNT(*)`；SQL Server / DB2 会剥离末尾 `ORDER BY` |
| `PageSql` | `string PageSql(string originalSql, string offsetParam, string pageSizeParam)` | 在原始 SQL 后追加分页子句；SQL Server / DB2 无 `ORDER BY` 时自动插入 `ORDER BY (SELECT NULL)` |

**已知差异**：

- **Oracle `CountSql`**：Oracle 要求 `SELECT COUNT(*) FROM (...)` 的子查询有别名，自定义方言若套用通用包裹模式需补别名（内置 `OracleDialect` 已正确处理）
- SQL Server / DB2 的 `CountSql` 会自动剥离末尾 `ORDER BY`（子查询不允许 `ORDER BY`），自定义实现若直接包装需注意

> **相关 API**：`LimitOffsetDialect`——LIMIT/OFFSET 型方言的现成继承点（`public abstract class`，内置 `MySqlDialect`/`PostgreSqlDialect`/`SqliteDialect` 均继承自它，计数与分页 SQL 模板可直接复用）（源码锚：SqlDialects.cs:9/:219/:228/:236）。

---

## 4. 异步查询

### 用法

[!code-csharp[Dapper 异步查询 QueryAsync](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QueryAsync)]
[!code-csharp[Dapper 异步首行或默认 QueryFirstOrDefaultAsync](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_QueryFirstOrDefaultAsync)]

---

## 常见误用

### 误用 1：不指定 DTO 类型参数

```csharp
// ❌ 误用：不指定 DTO 类型参数
var result = connection.AsDto<UserEntity>().Query("SELECT * FROM Users");
// → Query 是泛型方法，必须指定 TDto 类型

// ✅ 正确
var dtos = connection.AsDto<UserEntity>().Query<UserDto>("SELECT * FROM Users");
```

### 误用 2：SQL 注入风险

```csharp
// ❌ 误用：字符串拼接 SQL（SQL 注入风险）
var name = "Alice'; DROP TABLE Users;--";
var dtos = connection.AsDto<UserEntity>()
    .Query<UserDto>($"SELECT * FROM Users WHERE Name = '{name}'");

// ✅ 正确：使用参数化查询
var dtos = connection.AsDto<UserEntity>()
    .Query<UserDto>("SELECT * FROM Users WHERE Name = @Name", new { Name = name });
```

### 误用 3：忘记 Open 连接

```csharp
// ❌ 误用：未 Open 连接就查询
using var connection = new SqliteConnection("Data Source=:memory:");
var dtos = connection.AsDto<UserEntity>().Query<UserDto>("SELECT * FROM Users");
// → 异常：connection is not open

// ✅ 正确：先 Open
using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
var dtos = connection.AsDto<UserEntity>().Query<UserDto>("SELECT * FROM Users");
```

---

## 小结

| 能力 | API | 特性 |
|---|---|---|
| 基本查询 | `.AsDto<T>().Query<TDto>(sql, param)` | 参数化查询 |
| 首行 | `.QueryFirst<TDto>(sql)` | 无数据抛异常 |
| 首行或默认 | `.QueryFirstOrDefault<TDto>(sql)` | 无数据返回 null |
| 单行 | `.QuerySingle<TDto>(sql)` | 多行抛异常 |
| 单行或默认 | `.QuerySingleOrDefault<TDto>(sql)` | 多行抛异常 |
| SQL 分页 | `.ToPagedList<TDto>(sql, param, page, size)` | 6 种方言 |
| 异步 | `.QueryAsync<TDto>(sql)` | 异步变体 |

## Fluent DTO 支持现状

Dapper 扩展无 SQL 侧投影与降级检测器（列名→实体属性由 **Dapper 自身**的默认类型映射（`DefaultTypeMap`）完成，NCMapper 不介入 SQL 列名段，故该段不消费 `[MapProperty]` 列名声明；`[MapProperty]` 于**实体→DTO 段**生效）——**Fluent DTO 与特性通道 DTO 在 Dapper 行为一致**：`Query` 系列经映射管道执行，Fluent 类型对经注册表（`IsFluentRegisteredDto`）命中生成映射器/注册表回退，内存映射全语义生效。注意 `QueryMultiple` 的非泛型源类型推断读 `[MapFrom]`，Fluent DTO 请使用显式泛型入口。

---

## 配套示例

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 502
```

核心示例源码（单点同源，`dotnet run --scenario` 可运行验证）：

[!code-csharp[Query 查询](~/samples/NCMapper.Samples/05-Orm/DapperScenarios.cs#Dapper_Query)]

代码见 `samples/NCMapper.Samples/05-Orm/DapperScenarios.cs`，涵盖 Query/QueryFirst/QuerySingle/ToPagedList 等子场景。

---

## 进一步阅读

- [Dapper（二）：高级方法](dapper-advanced.md) · [EF Core 投影](efcore-projection.md)
- 技术文档：[EF Core 与 Dapper 扩展包](../../explanation/orm-efcore-dapper.md) §11.4
- 扩展源码：`src/NCMapper.Dapper/NCMapperDapperExtensions.cs` · `src/NCMapper.Dapper/SqlDialects.cs`
