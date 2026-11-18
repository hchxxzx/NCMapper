> [!WARNING]
> This documentation has been machine-translated from Chinese source comments and may contain inaccuracies. Please refer to the original Chinese IntelliSense documentation or the source code for authoritative descriptions.

<!-- Source: NCMapper Main Technical Documentation §11.5–11.6 Paged Result Model and Implementation Principles -->

### 11.5 Paged Result Model: `PagedList<T>`

`ToPagedList` / `ToPagedListAsync` returns a `PagedList<T>`:

| Property     | Type      | Description                     |
| ------------ | --------- | --------------------------------- |
| `Items`      | `List<T>` | List of items on the current page |
| `TotalCount` | `long`    | Total number of records           |
| `Page`       | `int`     | Current page number (1-based)     |
| `PageSize`   | `int`     | Number of items per page          |
| `TotalPages` | `int`     | Total number of pages (calculated property) |
| `HasNext`    | `bool`    | Whether a next page exists        |
| `HasPrev`    | `bool`    | Whether a previous page exists    |

Parameter validation: Throws `ArgumentOutOfRangeException` if `page < 1` or `pageSize < 1`.

### 11.6 Implementation Principles

Each ORM extension package accesses internal APIs in the NCMapper core (`PropertyMatcher`, `NavigationResolver`, `TypeConverter`, `TypeHelper`) via `InternalsVisibleTo`. Each independently implements its own projection expression builder (`FreeSqlProjectionBuilder` / `EfCoreProjectionBuilder`) and degradation detector (`FreeSqlDegradationDetector` / `EfCoreDegradationDetector`). The Dapper package does not include a degradation detector — its `DapperDtoSelector<TSource>` always uses the full in-memory mapping pipeline via `Mapper.MapList`. Users do not call these internal methods directly — `AsDto()` is the sole public entry point, which internally handles path resolution, expression building, and automatic degradation switching.

**Normal Path**: `XxxProjectionBuilder.Build<TSource, TTarget>()` obtains the projection expression (cached in a `ConcurrentDictionary` after first use), then forwards it to the ORM’s `ToList(expr)` / `Select(expr)` method — completing both query and mapping in a single SQL statement.

**Degraded Path**: ORM queries entities first → then performs in-memory mapping via `Mapper.MapList<TSource, TTarget>(entities)`. Compared to the normal path, this incurs an additional in-memory iteration for mapping, but fully supports all features such as `[Convert]`, `[Mask]`, and Pipeline PostMap.

> Projection expression building and degradation detection are internal implementations specific to each extension package. Users consume projection functionality exclusively through the terminal `AsDto()` operator; bypassing degradation detection is neither supported nor possible.
