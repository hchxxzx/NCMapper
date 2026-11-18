<!-- 来源：NCMapper 主技术文档 §11.5-11.6 分页结果模型与实现原理 -->
### 11.5 分页结果模型 PagedList

`ToPagedList` / `ToPagedListAsync` 返回 `PagedList<T>`：

| 属性           | 类型        | 说明           |
| ------------ | --------- | ------------ |
| `Items`      | `List<T>` | 当前页数据列表      |
| `TotalCount` | `long`    | 总记录数         |
| `Page`       | `int`     | 当前页码（从 1 开始） |
| `PageSize`   | `int`     | 每页条数         |
| `TotalPages` | `int`     | 总页数（计算属性）    |
| `HasNext`    | `bool`    | 是否有下一页       |
| `HasPrev`    | `bool`    | 是否有上一页       |

参数校验：`page < 1` 或 `pageSize < 1` 时抛 `ArgumentOutOfRangeException`。

### 11.6 实现原理

各 ORM 扩展包通过 `InternalsVisibleTo` 访问 NCMapper 核心的 internal API（`PropertyMatcher`、`NavigationResolver`、`TypeConverter`、`TypeHelper`），各自独立实现投影表达式构建器（`FreeSqlProjectionBuilder` / `EfCoreProjectionBuilder`）和降级检测器（`FreeSqlDegradationDetector` / `EfCoreDegradationDetector`）；Dapper 包无降级检测器——其 `DapperDtoSelector<TSource>` 始终走 `Mapper.MapList` 内存映射全管道。用户不直接调用这些内部方法——`AsDto()` 是唯一公共入口，内部自动完成路径判断、表达式构建、降级切换。

正常路径：`XxxProjectionBuilder.Build<TSource, TTarget>()` 获取 Projection 表达式（首次构建后缓存于 `ConcurrentDictionary`），转发给 ORM 的 `ToList(expr)` / `Select(expr)` 方法，一条 SQL 完成查询 + 映射。

降级路径：ORM 查实体 → `Mapper.MapList<TSource, TTarget>(entities)` 内存映射。相比正常路径多一次内存遍历映射，但 `[Convert]` / `[Mask]` / Pipeline PostMap 等全部生效。

> 投影表达式构建和降级检测均为各扩展包的 internal 实现，用户通过 AsDto 终端操作使用投影，无需也无法绕过降级检测。

---

