---
wiki-id: how-to/orm/freesql-multi-table
涉及诊断码: （无）
库版本: v1.0.0+
更新时间: 2026-09-15
---

# FreeSql 投影（二）：多表查询

## 概述

NCMapper 支持 FreeSql 多表查询的 `AsDto` 投影：通过 `ISelect<T1, T2>` 或 `ISelect<T1, T2, T3>` 连接多表，在 DTO 上用 `[MapProperty(typeof(Tx), ...)]` 标记字段来源。多表投影在 SQL 侧完成 JOIN 和列裁剪，避免全量加载。

核心 API：`fsql.Select<T1, T2>().LeftJoin(...).AsDto().ToList<TDto>()`

> **相关 API**：`NCMapperFreeSqlExtensions.ThrowOnIncludeApplicationFailure`——自动 Include 部分失败时是否向调用方重抛异常的全局开关（`public static bool` 字段，默认 `false` 保留降级契约并 Trace 留痕；置 `true` 后失败对调用方显式可见）（源码锚：NCMapperFreeSqlExtensions.Support.cs:368）。

---

## 1. 双表查询

### 用法

[!code-csharp[FreeSql 多表：using NCMapper.FreeSql 与双表联投](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_TwoTableJoin)]

DTO 定义（字段来自两个表）：

```csharp
[MapFrom(typeof(OrderEntity))]  // 主表
public class OrderDetailDto
{
    // 主表字段
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderNo))]
    public string OrderNumber { get; set; } = "";

    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.Amount))]
    public decimal Amount { get; set; }

    // 副表字段
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
    public string CustomerName { get; set; } = "";

    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Phone))]
    public string CustomerPhone { get; set; } = "";
}
```

生成的 SQL：

```sql
SELECT a.OrderNo, a.Amount, b.Name, b.Phone
FROM Orders a
LEFT JOIN Customers b ON a.UserId = b.Id
```

**要点**：

- `[MapFrom]` 标注主表类型，`[MapProperty]` 标注跨表字段来源
- 支持 `LeftJoin`、`InnerJoin`、`RightJoin` 三种 JOIN 类型
- DTO 的 `[MapProperty]` 精确声明字段来自哪个表
- SQL 侧完成 JOIN 和列裁剪，不传输多余列

### JOIN 类型

```csharp
// LeftJoin（左连接）
fsql.Select<OrderEntity, CustomerEntity>()
    .LeftJoin((o, c) => o.UserId == c.Id)

// InnerJoin（内连接）
fsql.Select<OrderEntity, CustomerEntity>()
    .InnerJoin((o, c) => o.UserId == c.Id)

// RightJoin（右连接）
fsql.Select<OrderEntity, CustomerEntity>()
    .RightJoin((o, c) => o.UserId == c.Id)
```

---

## 2. 三表查询

### 用法

[!code-csharp[FreeSql 三表 Join AsDto Compose](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_MultiTableThreeWay)]

DTO 定义（字段来自三个表）：

```csharp
[MapFrom(typeof(OrderEntity))]  // 主表
public class ReportDto
{
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderNo))]
    public string OrderNumber { get; set; } = "";

    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
    public string CustomerName { get; set; } = "";

    [MapProperty(typeof(ProductEntity), nameof(ProductEntity.Name))]
    public string ProductName { get; set; } = "";

    [MapProperty(typeof(ProductEntity), nameof(ProductEntity.Price))]
    public decimal ProductPrice { get; set; }
}
```

**要点**：

- 最多支持 3 表 JOIN（`ISelect<T1, T2, T3>`）
- 每个 `LeftJoin` lambda 的参数顺序与 `Select<T1, T2, T3>` 一致
- `From<T2>` lambda 参 `ISelectFromExpression<T1>`

---

## 3. 多表 Compose 降级

当多表查询无法在 SQL 侧完成投影时（如需要运行时计算、Convert 等），NCMapper 自动降级为内存 Compose：

```
ISelect<T1, T2>.AsDto().ToList<TDto>()
    → 检测到不可投影的字段
    → 降级：SQL 查全量（所有需要的列）
    → 内存侧调用 Mapper.Compose 合成 DTO
```

### 降级路径行为

| 行为 | 投影路径 | 降级路径 |
|---|---|---|
| 自动 Include | ✅ 生效 | ✅ 生效 |
| PostMap 回调 | ✅ 触发 | ✅ 触发 |
| GlobalFilter | ✅ 传播 | ✅ 传播 |
| 列裁剪 | ✅ SQL 侧 | ❌ 查全量 |

> **降级路径的 Include 行为**：降级路径同样自动 Include 导航属性（FreeSql 侧经 IncludeByPropertyName / EFCore 侧经导航收集器自动追加），无需手动 `.Include()`；`[MapNoAutoInclude]` 可排除指定导航属性。

---

## 4. 多表 WithSql

当需要复杂 SQL（子查询、CTE 等）时，用 `WithSql` 传入自定义 SQL：

[!code-csharp[FreeSql 多表 WithSql 双数据源替换](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_TwoTableWithSql)]

**要点**：

- 多表 WithSql：`sqlT2` 参数必填（指定第二张表的 SQL）
- `WithSql(sqlT1, sqlT2, param)` — 两个 SQL 对应两张表
- WithSql 后仍可链式调用 `LeftJoin` 等操作

---

## 5. GlobalFilter 传播

FreeSql 的 `GlobalFilter`（软删除、租户隔离等）自动传播到多表查询的两条路径：

```csharp
fsql.GlobalFilter.Apply<ISoftDelete>("SoftDelete", e => e.IsDeleted == false);

// 多表查询自动附加 WHERE 条件
var dtos = fsql.Select<OrderEntity, CustomerEntity>()
    .LeftJoin((o, c) => o.UserId == c.Id)
    .AsDto()
    .ToList<OrderDetailDto>();
// SQL: ... FROM Orders a LEFT JOIN Customers b ON ...
//      WHERE a.IsDeleted = false AND b.IsDeleted = false
```

**要点**：

- GlobalFilter 在投影路径和降级路径均生效
- 多表查询时，所有参与 JOIN 的表（实现了过滤接口的）都会附加 WHERE 条件

---

## 常见误用

### 误用 1：跨表字段未标注 [MapProperty] 来源

```csharp
// ❌ 误用：跨表字段没有 [MapProperty(typeof(T2), ...)]
// DTO 的跨表字段没有标注 → 该字段保持默认值
[MapFrom(typeof(OrderEntity))]
public class OrderDetailDto
{
    public string CustomerName { get; set; } = "";  // 无 [MapProperty] → 默认值
}

// ✅ 正确：跨表字段必须显式声明来源类型
[MapFrom(typeof(OrderEntity))]
public class OrderDetailDto
{
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
    public string CustomerName { get; set; } = "";
}
```

### 误用 2：多表 WithSql 缺少 sqlT2

```csharp
// ❌ 误用：双表 WithSql 只传一个 SQL
var dtos = fsql.Select<OrderEntity, CustomerEntity>()
    .WithSql("SELECT * FROM Orders WHERE Amount > 1000")  // 缺少 sqlT2
    .LeftJoin((o, c) => o.UserId == c.Id)
    .AsDto()
    .ToList<OrderDetailDto>();
// 运行时异常：CustomerEntity 无数据源

// ✅ 正确：双表 WithSql 需传两个 SQL
var dtos = fsql.Select<OrderEntity, CustomerEntity>()
    .WithSql("SELECT * FROM Orders WHERE Amount > 1000",
             "SELECT * FROM Customers")
    .LeftJoin((o, c) => o.UserId == c.Id)
    .AsDto()
    .ToList<OrderDetailDto>();
```

### 点路径能力

多表 Compose 投影支持**点路径**与**推导扁平化**（与单表一致，BuildCompose/BuildCompose3 复用段链共用循环）：

```csharp
// 点路径：显式声明嵌套字段（2026-08 起四引擎一致支持）
[MapProperty(typeof(CustomerEntity), "Address.City")]
public string CustomerCity { get; set; } = "";

// 推导扁平化：属性名自动拆解（AddressCity → Address.City）
public string? AddressCity { get; set; }

// 两者均经 JOIN 或降级路径生效；失败时 Trace 输出（不再静默跳过）
```

---

## 小结

| 能力 | API | 特性 |
|---|---|---|
| 双表查询 | `Select<T1,T2>().LeftJoin(...).AsDto()` | SQL 侧 JOIN |
| 三表查询 | `Select<T1,T2,T3>().LeftJoin(...)` | 最多 3 表 |
| JOIN 类型 | LeftJoin / InnerJoin / RightJoin | 三种 JOIN |
| 降级路径 | 自动触发 | SQL 全量→内存 Compose |
| WithSql | `WithSql(sqlT1, sqlT2, param)` | 自定义 SQL，sqlT2 必填 |
| GlobalFilter | 自动传播 | 两路径均生效 |

## Fluent DTO 支持现状

跨表字段的 **Fluent 通道**声明（`FromSource`）在多表场景经合成面强制降级闭合：`FromSource` 声明无逐属性投影明细（`HasComposeMappings` 承载），Compose 合成走内存多源合成——与特性通道跨表字段（`[MapProperty(FromSource)]`）的降级路径行为一致。主链纯可翻译声明仍可享受投影加速（见 FreeSql 单表篇口径）。

---


本页可运行演示（场景 503，FreeSql 内存模式）：

[!code-csharp[双表 JOIN 投影：跨表字段取数](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_TwoTableJoin)]

[!code-csharp[一对多导航集合投影：IncludeMany](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_IncludeManyNavigation)]

[!code-csharp[Fluent DTO 投影（注册表承载）](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_FluentProjection)]

> 投影消费口径（快照优先）：跨表声明（特性 `[MapProperty(FromSource)]` / Fluent `FromSource`）由运行时快照的 `ComposeMappings` 明细承载，单表 `AsDto` 时经主源导航属性推断完成跨表字段投影；未注册对回退特性解析，行为不变。

### Fluent Compose 解锁

Fluent 通道的多源 `FromSource` 声明对在 ORM 投影面已由"恒降级内存映射"改为**明细可译性判定**：

| 声明形态 | 投影行为 |
| --- | --- |
| 副源字段直取 / 点路径 / 索引段（无脱敏/转换/格式化/JSON/条件守卫承载） | **可译**——走 SQL 侧投影（单表导航推断 + 双表 Compose 门面双路生效，跨表字段首次真正可映射） |
| cm 级 `Mask` / `Convert` / `Format` / `FromJson` / `When` 任一 | 维持降级（reason 标注具体类别） |
| 对级 `AfterMapping` / `BeforeMapping` / `MapSubclass` | 维持降级 |
| 声明有、产出无（`NCMAP_F29` 族） | 保守维持降级 |

**排查入口**：降级发生时开启 `MapTracer.GloballyEnabled = true`，Trace 的 reason 类别（如 `含不可翻译声明（Mask）`）即不可译位——移除对应能力位即可走投影。

**如需维持当前两步形态**：不可译形态自动维持降级（无需操作）；可译形态如需保持自行两步查询 + 内存映射管道（`Mapper.MapList`）的做法——框架不提供全局回退开关。注意：降级内存映射不产出副源字段（跨表字段为 `default`）。

## 配套示例

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 503
```

代码见 `samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs` 中的 `TwoTableJoin`、`IncludeManyNavigation` 与 `FluentProjection` 方法；场景 501 另含特性通道多表 Compose 演示（`05-Orm/FreeSqlScenarios.cs` 的 `MultiTableTwoWay` / `MultiTableThreeWay`）。

---

## 进一步阅读

- [FreeSql（一）：单表](freesql-single-table.md) · [FreeSql（三）：高级终端方法](freesql-advanced.md)
- 技术文档：[NCMapper.FreeSql 扩展包](../../explanation/orm-freesql.md) §11.2
- 扩展源码：`src/NCMapper.FreeSql/NCMapperFreeSqlExtensions.cs`
