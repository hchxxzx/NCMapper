<!-- 来源：NCMapper 主技术文档 §6.2-6.3 嵌套对象与自动扁平化 -->
### 6.2 嵌套对象

当源属性与目标属性都是 class 类型但类型不同时（如 `CustomerEntity → CustomerDto`），NCMapper 自动递归映射嵌套对象，无需手动调用 `Mapper.Map`。

#### 触发条件

嵌套对象映射在以下条件**全部满足**时触发：

| 条件         | 说明                                    |
| ---------- | ------------------------------------- |
| 双方均为 class | struct / record struct 不触发            |
| 非 string   | string 走类型转换路径                        |
| 非泛型类型      | `List<T>`、`Dictionary<K,V>` 等走集合/字典路径 |
| 非数组        | `T[]` 走集合路径                           |
| 源类型 ≠ 目标类型 | 同类型走直接赋值（浅拷贝）                         |

```csharp
public class OrderEntity
{
    public int Id { get; set; }
    public CustomerEntity Customer { get; set; }      // 嵌套对象 → 递归映射
    public AddressEntity ShippingAddress { get; set; } // 嵌套对象 → 递归映射
    public List<OrderItemEntity> Items { get; set; }   // 集合 → 集合映射路径
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public int Id { get; set; }
    public CustomerDto Customer { get; set; }          // CustomerEntity → CustomerDto
    public AddressDto ShippingAddress { get; set; }    // AddressEntity → AddressDto
    public List<OrderItemDto> Items { get; set; }      // 集合元素递归映射
}
```

#### 内联深度控制（MaxInlineDepth）

运行时引擎将嵌套对象映射**内联展开**到外层表达式树中，避免委托调用开销。内联深度由 `Mapper.MaxInlineDepth`（默认 5）控制：

```
OrderEntity → OrderDto
  └── Customer (深度 1) → 内联展开
        └── Address (深度 2) → 内联展开
              └── City (深度 3) → 内联展开
                    └── ... (深度 4, 5) → 内联展开
                          └── 深度 6 → 超过 MaxInlineDepth → 回退到 MapperBase<S,T>.Map() 委托
```

|           深度           | 行为                               | 性能      |
| :--------------------: | -------------------------------- | ------- |
| ≤ MaxInlineDepth（默认 5） | 内联展开到外层表达式树，零委托调用                | 最优      |
|    > MaxInlineDepth    | 回退到 `MapperBase<S,T>.Map()` 委托调用 | 多一次委托间接 |

[!code-csharp[调整内联深度（范围 0-16）](~/samples/NCMapper.Samples/02-Declarative/NestedScenarios.cs#Core_Nested_InlineDepthTune)]

> **设计目的**：防止循环引用类型（如 `A.Child → B.Parent → A`）导致表达式树无限展开。超过内联深度后委托路径有独立的 `MaxRecursionDepth`（默认 64）循环引用防护。

#### Null 处理

源嵌套对象为 null 时，目标属性赋值为 null（不创建空对象）：

```csharp
var order = new OrderEntity { Id = 1, Customer = null };
var dto = Mapper.Map<OrderEntity, OrderDto>(order);
// dto.Customer == null（不会 new CustomerDto()）
```

| 引擎   | 实现                                                             |
| ---- | -------------------------------------------------------------- |
| 源生成器 | `if (src.Customer != null) dst.Customer = ...;`（条件赋值，null 时跳过） |
| 运行时  | `Condition(src.Customer != null, mapExpression, null)`（三元表达式）  |

#### [MapFrom] 对嵌套映射的影响

嵌套目标类型**不要求**标注 `[MapFrom]`，但标注与否影响源生成器的代码生成路径：

| 嵌套目标类型                             | 源生成器生成代码                                                           | 性能        |
| ---------------------------------- | ------------------------------------------------------------------ | --------- |
| 有 `[MapFrom(typeof(ChildEntity))]` | `GeneratedMapper.Map_ChildEntity_To_ChildDto(src.Child)` — 编译时方法直调 | 零首次延迟     |
| 无 `[MapFrom]`                      | `NCMapper.Mapper.Map<ChildEntity, ChildDto>(src.Child)` — 回退运行时    | 首次触发表达式编译 |

```csharp
// 推荐：嵌套类型也标注 [MapFrom]，全链路走源生成器
[MapFrom(typeof(CustomerEntity))]
public class CustomerDto
{
    public string Name { get; set; }
    public string Email { get; set; }
}
```

> 运行时引擎不关心 `[MapFrom]`——任何两个不同的非泛型 class 都会自动递归映射（通过内联 BuildMapBody 或 `MapperBase<S,T>.Map`）。

#### 集合中的嵌套对象

`List<ChildEntity> → List<ChildDto>` 等集合属性，元素按嵌套对象规则递归映射：

```csharp
public class OrderEntity
{
    public List<OrderItemEntity> Items { get; set; }
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public List<OrderItemDto> Items { get; set; }  // 逐元素递归映射
}
```

| 引擎   | 实现                                                                |
| ---- | ----------------------------------------------------------------- |
| 源生成器 | 调用 `GeneratedMapper.MapList_X_To_Y(src.Items)`（内联 for 循环 + 预分配容量） |
| 运行时  | 调用 `MapperBase<X,Y>.MapList(src.Items, null)`（编译的列表委托）            |

集合中的 null 元素保留为 null（保持索引一致性）。

#### 源生成器 vs 运行时对比

| 维度            | 源生成器                                    | 运行时引擎                                   |
| ------------- | --------------------------------------- | --------------------------------------- |
| 嵌套有 [MapFrom] | 调用 `GeneratedMapper.Map_X_To_Y()` 编译时方法 | 内联 BuildMapBody 或 `MapperBase.Map`      |
| 嵌套无 [MapFrom] | 回退 `Mapper.Map<S,T>()` → 运行时            | 内联 BuildMapBody 或 `MapperBase.Map`      |
| 内联优化          | 无内联（总是方法调用）                             | 前 MaxInlineDepth 层内联展开（零委托）             |
| 循环引用防护        | 无（编译时代码不存在循环引用风险）                       | MaxRecursionDepth(64) 防护                |
| 首次延迟          | 零（编译时生成）                                | 首次触发 Expression.Compile()               |
| AOT 兼容        | 完全兼容                                    | 需要 DynamicCode                          |
| null 处理       | `if (src != null)` 条件赋值                 | `Condition(src != null, map, null)` 表达式 |

### 6.3 自动扁平化

分层架构中，Entity 常通过导航属性组织关联数据（如 `Order.Customer.Name`），而 DTO 倾向扁平结构（如 `CustomerName`）以减少前端嵌套解析。自动扁平化让源生成器在编译期自动将 DTO 的 PascalCase 属性名拆分为嵌套路径，匹配源实体的导航属性链——无需手写 `[MapProperty]` 或 Action 赋值。

> **引擎支持**：自动扁平化在**源生成器路径**（按目标属性名大写字母边界拆解，如 `AddressCity → Address.City`）和 **ORM 投影路径**中均已实现。运行时 `MapProjection.TryBuildFlattenedExpression` 按大写字母拆分候选路径并生成 `Expression.Property` 链（`AddressCity → src.Address.City`），可翻译为 SQL；运行时 `Mapper.Map` 的表达式树引擎（`ExpressionBuilder`）支持扁平化匹配（段匹配 OrdinalIgnoreCase，与源生成器/投影同规则，仅正向），同名（`OrdinalIgnoreCase`）与 `[MapProperty]` 匹配同样支持。

#### 匹配算法

源生成器对每个**未标注 `[MapProperty]` 且同名匹配失败**的 DTO 属性，执行以下流程：

```
DTO 属性名 "CustomerName"
  │
  ├── 1. PascalCase 拆分 — 在每个大写字母位置尝试切分
  │     大写字母位置: [C]ustomer[N]ame → 切分点: 位置 8
  │     位掩码枚举所有组合 → 候选: ["Customer", "Name"]
  │
  ├── 2. 路径验证 — 逐级在源类型中查找属性（大小写不敏感）
  │     "Customer" → OrderEntity.Customer ✓ (类型: CustomerEntity, class)
  │     "Name"     → CustomerEntity.Name ✓ (叶节点)
  │
  └── 3. 匹配成功 → 生成 src.Customer?.Name 赋值代码
```

**拆分规则细节**：

| 规则      | 说明                           |
| ------- | ---------------------------- |
| 切分点     | 属性名中每个大写字母位置（首位除外）           |
| 组合枚举    | 位掩码枚举所有 2^n 种切分组合            |
| 大写字母上限  | 超过 4 个大写字母 → 放弃拆分（避免组合爆炸）    |
| 路径段数    | 仅保留 2~5 段的候选（1 段即同名匹配，已先行处理） |
| 候选排序    | 段数少的优先尝试（贪心策略，优先匹配更浅嵌套）      |
| 首个匹配即采用 | 第一个完全通过路径验证的候选即被采用，不回溯       |

**路径验证约束**：

| 约束             | 说明                                  |
| -------------- | ----------------------------------- |
| 中间节点必须是 class  | 值类型（int/DateTime/struct）、接口不可作为中间节点 |
| 中间节点必须有 getter | 只写属性（无 `get`）不可作为路径中间节点             |
| 叶节点无类型限制       | 最终属性可以是任意类型（string/int/DateTime 等）  |
| 大小写不敏感         | 每级匹配均使用 `OrdinalIgnoreCase`         |

#### 基础示例

```csharp
// 源实体 — 嵌套导航属性
public class NmInvoiceEntity
{
    public int Id { get; set; }
    public NmCustomerEntity Customer { get; set; }   // 导航属性
    public NmShipperEntity Shipper { get; set; }     // 导航属性
}

public class NmCustomerEntity
{
    public string Name { get; set; }
    public string Email { get; set; }
}

public class NmShipperEntity
{
    public string Name { get; set; }
    public string Phone { get; set; }
}

// DTO — 扁平属性名，自动匹配嵌套路径
[MapFrom(typeof(NmInvoiceEntity))]
public class NmInvoiceDto
{
    public int Id { get; set; }                    // 同名直接匹配
    public string CustomerName { get; set; }       // → Customer.Name
    public string CustomerEmail { get; set; }      // → Customer.Email
    public string ShipperName { get; set; }        // → Shipper.Name
    public string ShipperPhone { get; set; }       // → Shipper.Phone
}
```

嵌套映射的可运行版本——源生成器自动生成 `src.Customer?.Name` 等赋值代码（如 `dto.CustomerName = invoice.Customer.Name`、`dto.ShipperPhone = invoice.Shipper.Phone`）：

[!code-csharp[嵌套导航扁平化巡览（源生成扩展直调）](~/samples/NCMapper.Samples/02-Declarative/NestedScenarios.cs#Core_Nested_InvoiceFlatTour)]

#### 多级嵌套扁平化

支持最多 **5 段**路径深度：

```csharp
// 源实体 — 4 级嵌套
public class OrderEntity
{
    public int Id { get; set; }
    public CustomerEntity Customer { get; set; }
}
public class CustomerEntity
{
    public AddressEntity Address { get; set; }
}
public class AddressEntity
{
    public CityEntity City { get; set; }
}
public class CityEntity
{
    public string Name { get; set; }
    public string Province { get; set; }
}

// DTO — 多级扁平化
[MapFrom(typeof(OrderEntity))]
public class OrderFlatDto
{
    public int Id { get; set; }
    public string CustomerAddressCityName { get; set; }      // → Customer.Address.City.Name (4段)
    public string CustomerAddressCityProvince { get; set; }  // → Customer.Address.City.Province (4段)
}

// 生成代码: dst.CustomerAddressCityName = src.Customer?.Address?.City?.Name;
```

| 路径段数 | 示例属性名                           | 匹配路径                                |   结果  |
| :--: | ------------------------------- | ----------------------------------- | :---: |
|   2  | `CustomerName`                  | `Customer.Name`                     |   ✅   |
|   3  | `CustomerAddressCity`           | `Customer.Address.City`             |   ✅   |
|   4  | `CustomerAddressCityName`       | `Customer.Address.City.Name`        |   ✅   |
|   5  | `Level2Level3Level4Level5Value` | `Level2.Level3.Level4.Level5.Value` |   ✅   |
|  6+  | —                               | 超出上限                                | ❌ 不映射 |

#### 优先级规则

自动扁平化处于匹配优先级链的**最末位**：

```
1. [MapProperty] 显式标注    → 最高优先级，直接使用标注的源字段
2. 同名匹配（OrdinalIgnoreCase）→ 源类型有同名属性时直接匹配
3. [Convert] 无源属性   → 转换器仍被调用（fieldValue = default）
4. 自动扁平化匹配           → 仅当上述均未命中时触发
```

**歧义处理**：若源类型同时存在直接属性 `CustomerName` 和嵌套路径 `Customer.Name`，同名匹配（优先级 2）先命中直接属性，扁平化**不会被触发**。

```csharp
public class OrderEntity
{
    public string CustomerName { get; set; }       // 直接属性
    public CustomerEntity Customer { get; set; }   // 导航属性（Customer.Name 也存在）
}

[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    // 匹配 OrderEntity.CustomerName（同名直接匹配），而非 Customer.Name
    public string CustomerName { get; set; }
}
```

若需强制使用嵌套路径而非同名直接属性，用 `[MapProperty]` 显式指定：

```csharp
[MapProperty(typeof(OrderEntity), nameof(OrderEntity.Customer))]  // 显式指定走嵌套
public string CustomerName { get; set; }
```

#### Null 安全

源生成器为扁平化路径生成 **null-conditional（`?.`）链式访问**，中间导航属性为 null 时不抛 `NullReferenceException`：

```csharp
// 生成代码（源生成器输出）
dst.CustomerName = src.Customer?.Name;
dst.CityName = src.Customer?.Address?.City?.Name;

// 值类型叶节点 — ?. 链产生 T?，自动补充 ?? default
dst.CustomerAge = src.Customer?.Age ?? default(int);
```

| 叶节点类型          | 生成代码                                | 中间为 null 时结果 |
| -------------- | ----------------------------------- | ------------ |
| 引用类型（string 等） | `src.Customer?.Name`                | `null`       |
| 值类型（int 等）     | `src.Customer?.Age ?? default(int)` | `default(T)` |
| 可空值类型（int? 等）  | `src.Customer?.Score`               | `null`       |

#### 方向限制

自动扁平化支持 **Entity→DTO 方向**（`[MapFrom]` 标注的正向映射）与 **DTO→Entity 反向写回**：

```csharp
[MapFrom(typeof(OrderEntity))]
public class OrderDto
{
    public string CustomerName { get; set; }  // Entity→DTO: 自动扁平化 ✓
}

// 正向：OrderEntity → OrderDto，CustomerName 自动从 Customer.Name 取值 ✓
var dto = Mapper.Map<OrderEntity, OrderDto>(entity);

// 反向：OrderDto → OrderEntity，CustomerName 写回 Customer.Name ✓
// （中间节点 Customer 为 null 时自动 new 创建；Customer 已存在时复用并写 Name）
var entity = Mapper.Map<OrderDto, OrderEntity>(dto);
```

**反向写回语义（源生成器 + 运行时双引擎一致）**：

| 场景 | 行为 |
|------|------|
| 源值（DTO 属性）非 null，中间节点 null | 创建中间实例并写叶值（仅创建 null 段，已有实例复用） |
| 源值非 null，中间节点已存在 | 复用现有实例并写叶值（不覆盖导航对象其他成员） |
| 源值 null | 整体跳过（不创建中间节点、不写叶值，保留实体原值） |
| 源值 null + `[Convert]` | 走转换器（输入 null）；输出非 null → 创建（如需）并写叶值；输出 null → 整体跳过 |
| 中间节点为接口/抽象类/无公共无参构造器 | 源生成器 NCMAP031（Error 编译阻断）；运行时跳过 + MapTracer 警告 |
| 中间段 init-only/private setter | 跳过 + NCMAP013 诊断（源生成器）/ Trace（运行时） |
| 路径解析失败（段缺失/值类型或 string 中断） | 运行时抛 `MapConfigurationException`（不进失败缓存）；源生成器 NCMAP001 |
| 集合索引路径反向 | 登记不适用：跳过 + NCMAP013（源生成器）/ Trace（运行时） |

**优先级规则**：推导扁平化折叠受"同名直接属性优先"约束（实体存在同名可写属性 → 普通赋值优先，与正向对称）；显式 `[MapProperty]` 路径标注 = 契约（路径优先，与正向一致）。**实体侧路径标注不参与反向**：实体属性上的 `[MapProperty(typeof(Dto), "A.B")]` 仅用于正向读路径，反向写回只消费 DTO 侧标注。

> **反向写回与 EF 变更跟踪/延迟加载代理的交互（O-2）**：写回在中间节点为 null 时执行 `new T()` 创建实例，EF Core 变更跟踪会将新实例识别为 **Added** 并计划 INSERT 新行（或改变外键归属）——这是数据正确性风险而非构造副作用。编辑表单提交（`Map(dto, entity)` + `SaveChanges`）前请确认：① 中间导航对象缺失时"新建关联行"是否符合业务预期；② 使用延迟加载代理时，代理类型通常有公共无参构造器（可创建）但其导航属性初始化行为与普通实体不同。若需"仅更新已有链、不创建新节点"的保守语义，请用 `[MapIgnore(Direction.ToEntity)]` 拦截对应 DTO 属性或改用 Action 回调显式处理。

#### 与 Projection 的关系

ORM Projection 由各 ORM 扩展包独立实现（`FreeSqlProjectionBuilder` / `EfCoreProjectionBuilder`），通过 `AsDto()` 终端操作提供。源生成器不再生成公共 Projection 扩展方法。投影当前不支持 null-conditional 表达式（`src.Customer?.Name`），嵌套对象扁平化通过导航属性 + `[MapProperty]` 或命名约定实现。详见[ORM Projection 设计](../orm-projection-design.md)。

> 投影表达式构建和降级检测为各扩展包 internal 实现，用户通过 AsDto 终端操作使用。

**全局忽略清单 × 扁平化/点路径**：投影路径对扁平化/点路径命中项执行源侧忽略判定，判定粒度为**叶节点**（最终选中路径的末段）——叶节点命中全局忽略清单 → 该属性不映射（与源生成器/运行时内存映射一致，四引擎收敛）；**中间导航节点命中清单不判定**（如 `AuthorName` 的中间节点 `Author` 命中清单不生效，仅叶节点 `Name` 命中才跳过）。点路径（`[MapProperty(typeof(Entity), "Author.Name")]`）同规则。该判定不触发降级：投影构建期直接跳过该属性（详见[ORM 降级机制 §2](../orm-degradation.md) 降级触发条件）。

#### 边界与限制汇总

| 条件                    | 行为                   |
| --------------------- | -------------------- |
| 属性标注了 `[MapProperty]` | 跳过扁平化，使用显式标注         |
| 源类型有同名属性              | 同名匹配优先，不触发扁平化        |
| 大写字母超过 4 个            | 放弃拆分，属性不映射           |
| 路径超过 5 段              | 不生成候选，属性不映射          |
| 中间节点是值类型/接口/struct    | 路径验证失败，尝试下一候选        |
| 中间节点无 getter          | 路径验证失败               |
| 反向映射（DTO→Entity）      | 反向写回（折叠）：中间节点 null 创建、源值 null 跳过、Convert 例外；集合索引/接口中间节点跳过 |
| ORM Projection        | 支持命名约定扁平化（`AddressCity` → `src.Address.City`） |
| 运行时引擎（无源生成器）          | 支持扁平化（段匹配 OrdinalIgnoreCase，仅正向） |
| 集合元素内部                | 扁平化仅作用于顶层属性，不递归到集合元素 |

#### 与 [MapProperty] 的取舍

| 维度         | 自动扁平化           | `[MapProperty]` 显式标注    |
| ---------- | --------------- | ----------------------- |
| 声明成本       | 零（属性名即规则）       | 每个属性一行特性                |
| 编译期检查      | 源生成器验证路径存在性     | typeof + nameof 强类型检查   |
| 可读性        | 属性名隐含路径（需了解规则）  | 映射关系一目了然                |
| 反向映射       | 支持（折叠写回，见上文） | 支持                      |
| Projection | 支持（命名约定扁平化）  | 支持                      |
| 适用场景       | 快速原型、简单 2~3 级嵌套 | 生产代码、需要显式契约 |

> **建议**：简单场景（2~3 级嵌套）可用自动扁平化减少样板代码；需要显式契约（消除路径歧义、锁定字段来源）时，使用 `[MapProperty]` 显式声明更明确。

