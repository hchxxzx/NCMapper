---
wiki-id: tutorials/orm-projection-tutorial
涉及诊断码: NCMAP017
库版本: v1.0.0+
更新时间: 2026-08-19
---

# ORM 投影场景演练

## 概述

本教程解决列表页的典型性能问题：实体有几十个字段，页面只展示几个，全量加载再逐条映射浪费带宽与内存。你会用一条 SQL 完成查询与 DTO 映射，并处理分页与降级。

示例来自 `samples/NCMapper.Samples` 的 04 场景，使用 FreeSql 内存模式，可离线运行。投影能力的完整 API 见 [FreeSql 单表](../how-to/orm/freesql-single-table.md) 与 [EFCore 投影](../how-to/orm/efcore-projection.md)，本教程侧重业务决策主线。

---

## 1. 场景：用户列表页

用户列表页需要展示姓名、年龄、邮箱，按年龄排序，分页显示。`UserEntity` 还含地址、状态、注册时间等字段，页面用不到。

两种做法：

- **先 `ToList()` 全量加载实体，再内存映射**：SELECT 所有列，DTO 用不到的字段也进内存
- **投影 `AsDto()`**：SQL 侧只查 DTO 需要的列，一次查询完成

`AsDto()` 是第二种做法：在 ORM 查询链上直接投影为 DTO，列裁剪与映射都在数据库侧完成。

| 维度 | 全量加载 + 内存映射 | 投影 `AsDto()` |
|------|--------------------|---------------|
| SQL 列 | 实体全部列 | 仅 DTO 声明的列 |
| 查询次数 | 1 次（但数据面大） | 1 次 |
| 内存占用 | 实体 + DTO 两份数据 | 仅 DTO 数据 |
| 过滤/排序 | 数据库侧或内存侧 | 数据库侧 |
| 适合场景 | 需要实体全字段的后处理 | 列表、导出、接口响应 |

---

## 2. 安装扩展包

投影是 ORM 集成能力，需要安装对应扩展包（三选一）：

| 扩展包 | 适用 ORM |
|--------|---------|
| `NCMapper.FreeSql` | FreeSql |
| `NCMapper.EFCore` | EF Core |
| `NCMapper.Dapper` | Dapper |

核心包不包含 ORM 依赖。安装后 `fsql.Select<T>()` 查询链即可使用 `AsDto()` 扩展。

安装注意：

- 扩展包版本与核心包保持一致（同一版本号）
- 同一进程可同时安装多个扩展包（如 FreeSql + EFCore），互不干扰
- 投影是终端操作：`AsDto()` 之后不能再追加实体查询条件，条件需写在 `AsDto()` 之前

---

## 3. 单表投影：查询即映射

在 FreeSql 查询链上追加 `AsDto()`：

[!code-csharp[单表 AsDto 投影](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto)]

[!code-csharp[单表 AsDto 投影 · SingleTableFirst](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto_SingleTableFirst)]

这段代码生成一条 SQL：`SELECT Id, Name, Age, Email FROM User WHERE Age > 20 ORDER BY Age`，直接映射为 `UserDto` 列表。过滤（`Where`）与排序（`OrderBy`）在数据库完成，页面拿到的就是最终数据。

与内存映射的区别在数据面：投影只取 DTO 声明的列，实体其他字段不加载。结果类型是 `List<UserDto>`，无需二次转换。

投影对 DTO 的要求：

- DTO 属性名与实体列对应（同名或 `[MapProperty]` 声明），与 `Mapper.Map` 的匹配规则一致
- 只投影页面需要的字段，DTO 声明越精简，SQL 裁剪越彻底
- 嵌套对象属性按导航路径投影（自动 Include，见 [§4 自动 Include](../how-to/orm/freesql-single-table.md#4-自动-include)）

---

## 4. 取单行

详情页或编辑页只需要一条记录：

[!code-csharp[单行投影](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto)]

[!code-csharp[单行投影 · SingleTableFirst](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto_SingleTableFirst)]

`First()` 取第一条匹配记录，与直接 `Select<User>` 的语义一致。

---

## 5. 分页：Count + 分页查询一步完成

列表页需要分页。`ToPagedList` 一次性完成计数与分页查询：

[!code-csharp[SQL 级分页](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTablePaged)]

返回 `PagedList<UserDto>`，包含 `Items` / `PageIndex` / `PageSize` / `TotalCount`。分页在 SQL 侧执行（`LIMIT/OFFSET`），不加载全量数据到内存再切片。

分页参数：

- `page`（页码）与 `size`（每页条数）由页面传入
- `size < 1` 抛 `ArgumentOutOfRangeException`，调用前校验参数
- 返回的 `TotalCount` 是过滤后的总数，用于渲染分页条

---

## 6. 降级：遇到 SQL 翻译不了的特性怎么办

不是所有映射都能翻译成 SQL。两类典型场景：

| 特性 | SQL 侧行为 |
|------|-----------|
| `[Convert]` 自定义转换 | 无法翻译为 SQL 表达式 |
| `[Mask]` 脱敏 | 无法翻译为 SQL 表达式 |

遇到这类特性时，`AsDto()` 自动降级为二步映射：先按 `AsDto` 可翻译的部分查 SQL，再在内存中完成剩余映射。降级是透明的：

- 行为不变：调用方看到的结果与纯内存映射一致
- 不需要改调用代码：投影与内存映射共用同一份映射配置
- 代价：多一次查询往返，列表量大的场景可观测

**降级可观测**：开启 `Mapper.TraceEnabled` 后，降级路径输出带终端方法名前缀的 Trace 行（如 `[FreeSql AsDto] ToList 降级: ...`），用于定位降级点。Trace 开关与使用说明见 [验证与诊断](../how-to/infrastructure/validation.md)。

需要控制降级时，检查映射配置中是否混入不可翻译特性（[投影路径与降级路径](../how-to/orm/freesql-single-table.md#3-投影路径与降级路径)）。

降级触发条件（按特性）：

| 特性 | 是否可 SQL 翻译 | 降级行为 |
|------|---------------|---------|
| 同名属性 / `[MapProperty]` 简单字段 | 可翻译 | 不降级 |
| `[Convert]` 自定义转换 | 不可翻译 | 降级二步映射 |
| `[Mask]` 脱敏 | 不可翻译 | 降级二步映射 |
| 嵌套导航属性 | 可翻译（自动 Include） | 不降级 |
| 集合索引路径（`[n]`） | 部分可翻译 | 按 ORM 降级 |

---

## 终端方法一览

`AsDto()` 之后需要终端方法执行查询，按结果形态选择：

| 终端方法 | 返回 | 场景 |
|---------|------|------|
| `ToList<TDto>()` | `List<TDto>` | 常规列表 |
| `First<TDto>()` | `TDto` | 取第一条（空结果抛异常） |
| `Single<TDto>()` | `TDto` | 取唯一一条（空/多条抛异常） |
| `ToPagedList<TDto>(page, size)` | `PagedList<TDto>` | 分页列表 |
| `ToChunkDto<TDto>(size, action)` | 分批回调 | 大批量流式处理 |

`First` / `Single` 的异常语义与 ORM 原生一致（空结果 `InvalidOperationException`）。`ToChunkDto` 适合导出、批处理场景，按批回调避免整表驻留内存。终端方法的完整签名与异步版本见 [§2 终端方法](../how-to/orm/freesql-single-table.md#2-终端方法)。

## 运行验证

```bash
dotnet run --project samples/NCMapper.Samples -- --scenario 501
```

场景 501 输出单表投影、单行、分页、分批、Compose 等子场景结果。samples 使用 FreeSql 内存模式并预置数据，不需要外部数据库。

---

## 何时不用投影

投影不是万能的，两类场景建议回到内存映射：

| 场景 | 原因 |
|------|------|
| 需要实体全字段做复杂业务计算 | 投影只取 DTO 列，实体数据不全 |
| 数据来自多个表且主表投影无法覆盖 | 用 `Compose` 多源合成（见 [多源合成场景演练](./compose-tutorial.md)） |

投影与合成可以组合：主表走 `AsDto` 投影取主源字段，副源信息用 `Compose` 分批组装（见 [compose-tutorial 组合用法](./compose-tutorial.md)）。

## 常见坑

| 坑 | 现象 | 改法 |
|----|------|------|
| 在 `AsDto` 前 `ToList()` 全量加载 | 投影未生效，等价于内存映射 | 查询链末端再调用 `ToList<TDto>()`，中间不中断 |
| 投影混入不可翻译特性 | 静默降级，列表大时性能下降 | 查看 Trace 输出定位降级点，评估是否移除特性或接受降级 |
| 分页结果误用 `List` 接收 | 丢失 `TotalCount` | 用 `PagedList<TDto>` 接收 `ToPagedList` 结果 |

---

## 速查表

| 场景 | API | 说明 |
|------|-----|------|
| 安装扩展包 | `NCMapper.FreeSql` / `EFCore` / `Dapper` | 三选一，核心包无 ORM 依赖 |
| 单表投影 | `fsql.Select<T>().AsDto().ToList<TDto>()` | SQL 侧列裁剪 + 映射 |
| 单行投影 | `.AsDto().First<TDto>()` | 取第一条 |
| SQL 分页 | `.AsDto().ToPagedList<TDto>(page, size)` | Count + 分页一步完成 |
| 自动降级 | 不可翻译特性自动二步映射 | 透明，行为不变 |

本教程的投影流程到此完成：从安装扩展包、单表投影、单行取数、SQL 分页到降级认知，覆盖列表页的完整数据链路。

---

## 相关文档

- [FreeSql 单表](../how-to/orm/freesql-single-table.md)：投影与降级机制详解
- [EFCore 投影](../how-to/orm/efcore-projection.md)：EF Core 投影
- [多源合成场景演练](./compose-tutorial.md)：多表数据合成 DTO
- [15 分钟上手教程](../getting-started/tutorial-15min.md)：基础映射完整演练
- [映射配置所有权三原则](../explanation/semantics/mapping-config-ownership.md)：点路径与 API 变更说明
