<!-- 来源：NCMapper 主技术文档 §11.0-11.1 设计原则与自动降级机制 -->
## 11. ORM Projection

### 11.0 设计原则

**AsDto 解决的是"实体→DTO 属性映射"问题**：属性匹配、类型转换、嵌套导航、扁平化、脱敏、自定义转换器。它将 `[MapFrom]` / `[MapProperty]` 声明转换为 ORM 可执行的 `Expression<Func<TSource, TTarget>>` 投影表达式。

**AsDto 不解决"聚合/表达式投影"问题**：`GroupBy` + `Sum` / `Count`、计算列、条件表达式等复合查询是 ORM 原生职责。NCMapper 在此无自动化空间，用户使用各 ORM 的 Select 语法自行组合。

**两者职责不同，不互相替代。**

---

NCMapper 通过三个独立扩展包提供 ORM 投影能力，一条 SQL 完成查询 + DTO 映射，无需先查实体再内存映射。扩展包以 `AsDto()` 为唯一终端操作入口，内部通过各扩展包自有的 `ProjectionBuilder` 构建投影表达式，并由 `DegradationDetector` 自动检测降级条件——当 DTO 使用了不可翻译为 SQL 的特性（`[Convert]`、`[Mask]`、Pipeline PostMap、集合类型 T[]/HashSet、record 无参构造器等）时，自动切换为"SQL 查实体 → 内存 MapList"二步映射，所有特性全量生效，用户无需手动判断。

```xml

<PackageReference Include="NCMapper.FreeSql" />
<PackageReference Include="NCMapper.EFCore" />
<PackageReference Include="NCMapper.Dapper" />
```

三项可运行版本（演示条件按样本数据设定）：

[!code-csharp[FreeSql 一行完成查询 + 映射](~/samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs#OrmProjection_SingleTableAsDto)]
[!code-csharp[EF Core 同样简洁](~/samples/NCMapper.Samples/05-Orm/EFCoreScenarios.cs#OrmProjection_SingleTableAsDto)]
[!code-csharp[多表投影 + First（FreeSql 双表 Compose 链）](~/samples/NCMapper.Samples/05-Orm/FreeSqlMultiTableScenarios.cs#FreeSqlMultiTable_TwoTableFirst)]

**投影能力**：同类型直接赋值、C# 隐式数值转换、导航属性自动推断（命名约定扁平化 `AddressCity → src.Address.City`、点路径 `"Address.City" → src.Address.City`）、嵌套对象递归（子类型有 `[MapFrom]` 且源类型匹配时）。

**AsDto 为终端操作** — `Where` / `OrderBy` / `Skip` / `Take` 等过滤排序必须在 `AsDto()` 之前的实体 `IQueryable` / `ISelect` 上完成。`AsDto()` 返回 DtoSelector 中间对象，调用 `ToList<TTarget>()` / `First<TTarget>()` 等终端方法时执行查询。

### 11.0.1 快照消费架构（快照优先、反射兜底）

投影与映射消费**同一份快照**：源生成器在编译期把 `[MapFrom]` / Fluent 声明发射为运行时元数据（身份段 + 属性明细段 + `ComposeMappings` 跨表明细），由模块初始化器注册进 `RuntimeSnapshotStore`（Type 键控注册表）。投影主链与降级检测器统一经 `RuntimeSnapshotStore.TryGetMappingDetail`（按 (源, 目标) 精确对）取数，三态分流：

| 态 | 条件 | 行为 |
| --- | --- | --- |
| ① 明细命中 | 条目存在且明细非空 | 快照为权威——按明细构建投影，零反射、零特性读取 |
| ② 明细不可达 | off 态身份键（明细为空） | Fluent 通道维持强制降级；`[MapFrom]` 通道落既有判定序 |
| ③ miss | 未注册对 | 反射兜底——特性解析（纯运行时模式） |

投影表达式缓存按注册表 `ContentVersion` 版本化：注册/合并/清理任一内容变更即递增版本，缓存条目失配自动重算——声明变更（程序集重载、测试注册）后无需手工失效。

> 消费面架构口径：**"投影与映射消费同一快照"**——ORM 投影（`AsDto`）、降级检测、内存映射管道读取同一份编译期发射的声明明细，消除"生成端与运行时端双读分叉"。

### 11.1 自动降级机制

AsDto 终端方法内部通过各扩展包自有的降级检测器（`FreeSqlDegradationDetector` / `EfCoreDegradationDetector`）判断路径；Dapper 包无降级检测器——始终走内存映射全管道：

| 路径   | 条件          | 行为                                                       |
| ---- | ----------- | -------------------------------------------------------- |
| 正常路径 | DTO 无不可翻译特性 | `ProjectionBuilder.Build` 构建表达式 → ORM 侧投影（一条 SQL 直出 DTO） |
| 降级路径 | 检测到降级条件     | SQL 查实体 → `Mapper.MapList` 内存映射（所有特性生效）                  |

**触发降级的条件**（各 ORM 扩展包按优先级检测）：

| 检测项                                                      | 说明                                                                  |
| -------------------------------------------------------- | ------------------------------------------------------------------- |
| 类级/属性级特性实现 `IProjectionPolicy.RequiresDowngrade == true` | `[Convert]`、`[Mask]`、`[MapProperty(IsFromJson=true)]` 均返回 true |
| NCMapper 命名空间特性未实现 `IProjectionPolicy`                   | 安全兜底：未声明的特性默认降级                                                     |
| 集合类型不可投影（C-1）                                            | DTO 属性为 `T[]` / `HashSet<T>` 等不可翻译类型时降级                             |
| 嵌套 DTO 无无参构造器（C-3）                                       | record 类型等无 Default Ctor 时降级                                        |
| 全局忽略清单命中（目标侧或源侧）                                      | DTO 属性命中忽略清单，或异名 `[MapProperty].FromField` 源字段命中清单时降级（映射对级豁免 `OverrideIgnore=true` 不降级） |
| Pipeline PostMap 注册                                      | `HasPostMapFor(sourceType, targetType)` 为 true，触发降级，切换为内存映射路径 |

> **扁平化/点路径叶节点命中忽略清单不触发降级**：降级检测器静态判定的“源侧命中”仅覆盖可预检的异名 `FromField` 源字段；命名约定扁平化（`AuthorName → Author.Name`）与点路径（`Author.Name`）的叶节点命中发生在投影表达式构建期（`TryBuildFlattenedExpression` / `TryBuildDotPathExpression` 叶节点判定），命中后投影直接跳过该属性——与内存映射同形态（属性留空），**非数据丢失路径**，无需降级。判定粒度=叶节点，中间导航节点命中不判定（与源生成器值模型管线一致）。

各扩展包降级检测器均带缓存（`ConcurrentDictionary`，key 含 Pipeline 版本号），Pipeline 注册新回调后旧缓存自动失效。降级时通过 `MapTracer.Write` 输出诊断（需 `MapTracer.GloballyEnabled = true`）。

> 外部特性（如 `DataAnnotations` 的 `[Required]`）不触发降级——安全网仅限 NCMapper 命名空间。

#### 自动 Include 机制

降级路径（SQL 查实体 → `Mapper.MapList` 内存映射）在 FreeSql/EF Core 下会自动分析 DTO 的跨表字段依赖并追加导航属性 Include，避免 N+1 查询。

**触发条件：** 当 DTO 的 `[MapProperty]` 引用了非 `TSource` 的其他源类型字段时（如 `[MapProperty(typeof(CustomerEntity), "CustomerName")]`，DTO 标注为 `[MapFrom(typeof(OrderEntity))]`），框架自动分析 `OrderEntity` 的导航属性中哪些指向 `CustomerEntity`，并在查询实体时追加 `Include()`。

**行为：**
- FreeSql 侧：通过 `FreeSqlNavigationCollector` 分析跨表字段 → 构建导航树 → 调用 `IncludeByPropertyName` 逐层追加导航
- EF Core 侧：通过 `EfCoreNavigationCollector` 分析跨表字段 → 构建导航树 → 调用 `Include` / `ThenInclude` 逐层追加导航
- Dapper 侧：无此机制（Dapper 无 ORM 导航属性概念，`SELECT` 语句完全由用户控制）
- 投影路径（SQL 侧表达式树）：不触发自动 Include（投影路径直接在 SQL 侧完成字段映射，无需加载导航属性）

**内置开关（内部配置、非用户 API）：**
- FreeSql：`FreeSqlNavigationCollector.AutoIncludeEnabled` = `true`（internal 常量）
- EF Core：`EfCoreNavigationCollector.AutoIncludeEnabled` = `true`（private 常量）
- 当前版本硬编码为 `true`，不提供用户可配置的全局开关；计划后续迭代公开为 `NCMapperOptions` 的配置项。

#### [MapNoAutoInclude] — 排除自动 Include

标注在 DTO 的属性上，排除该属性参与自动 Include 分析。适用于：

- **大集合导航属性**：如 `List<OrderDetailEntity>` 等不需要每次自动加载的子集合
- **延迟加载场景**：用户计划按需手动 Include
- **循环引用**：A→B→A 的导航链可能导致无限递归 Include

**不适用场景：**
- 引用导航属性（如 `OrderEntity.Customer`）——这类属性即使 Include 也仅一条，开销极小
- 标注在无跨表依赖的属性上会触发 NCMAP017（Info 级诊断）

```csharp
[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public int Id { get; set; }

    // 正常的跨表字段 — 自动 Include Customer 导航属性
    [MapProperty(typeof(CustomerEntity), "CustomerName")]
    public string CustomerName { get; set; }

    // 标记为不自动 Include — 即使有跨表依赖也不追加 OrderDetail 导航
    [MapNoAutoInclude]
    [MapProperty(typeof(OrderDetailEntity), "ProductName")]
    public string ProductName { get; set; }
}
```

> **适用边界**：[MapNoAutoInclude] 仅在 FreeSql / EF Core 降级路径生效；Dapper 包无此机制（用户自主控制 SQL）；投影路径不触发自动 Include。

