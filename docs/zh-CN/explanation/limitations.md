<!-- 来源：NCMapper 主技术文档 §18 限制与注意事项 -->
## 18. 限制与注意事项

### 18.1 表达式树限制

- 不支持 `dynamic` 类型映射
- 不支持 `ref struct` / `Span<T>`
- 循环引用深度限制 64 层

### 18.2 源生成器限制

- 仅处理标注 `[MapFrom]` 的 class（不处理 struct/record struct）
- 跨程序集映射需要目标程序集也引用 NCMapper.SourceGen
- `[MapProperty].FromSource` 的跨类型字段必须在同一编译单元可见

### 18.3 Projection 限制

AsDto 投影由各 ORM 扩展包独立实现，限制因 ORM 翻译能力而异。以下按 ORM 分类说明。

#### 通用限制（三 ORM 共同）

| 限制                                                            | 行为            | 说明                                                                                                                                                                                                                                                                                         |
| ------------------------------------------------------------- | ------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `[Convert]` / `[Mask]` / Pipeline PostMap / `IsFromJson` | 自动降级          | `DegradationDetector` 检测到即走二步映射                                                                                                                                                                                                                                                            |
| 集合类型 `T[]` / `HashSet<T>`                                     | 自动降级          | 仅 `List<T>` 和可赋值接口（`IEnumerable<T>`/`IList<T>`/`ICollection<T>`/`IReadOnlyList<T>`/`IReadOnlyCollection<T>`）可投影                                                                                                                                                                            |
| record 嵌套 DTO（无无参 ctor）                                       | 自动降级          | record types lack parameterless ctor → ProjectionBuilder 无法生成 MemberInit                                                                                                                                                                                                                   |
| 命名约定扁平化                                                       | 支持            | `AddressCity → src.Address.City` 自动拆解                                                                                                                                                                                                                                                      |
| 点路径 `[MapProperty(typeof(T), "A.B")]`                         | **源生成器 / 运行时 / 投影 / Compose(SourceGen) 均支持**（四引擎对齐） | 四引擎一致：合法路径按段解析（段匹配 OrdinalIgnoreCase，中间节点须 class/interface 非值类型/string），中间节点 null → 不抛（运行时/生成器 default/null；投影 SQL 翻译下 NULL 列）；叶节点命中全局忽略清单 → 跳过（粒度=叶节点）。**失败均为显性可观测**：源生成器 NCMAP001（普通）/ NCMAP005（Compose 跨表）带失败段名与所在类型 Warning；运行时抛 MapConfigurationException（显性失败）；投影构建期跳过该属性 + Trace（分段失败/中断措辞）。`[Mask]` / `[Convert]` × 点路径：生成端/运行时不消费仅可观测（NCMAP030/警告），投影降级链二步映射输出明文（注：明文仅指 **`[Mask]` / `[Convert]` 特性形态**；Fluent 通道经注册表明细声明的脱敏在降级后内存映射已生效——注册表明细覆盖点路径与扁平化叶节点）。注意：命名约定扁平化（`AddressCity → src.Address.City`）是自动拆解**目标属性名**大写边界，与用户手写的 `.` 点路径写法无关 |

#### FreeSql 特定

| 限制              | 行为                                           |
| --------------- | -------------------------------------------- |
| 集合导航投影          | 不支持 SQL 翻译，恒触发降级                             |
| 引用导航嵌套投影        | 支持 SQL 投影（IIF 剥保护改写）；**null 导航 → 空壳 DTO**，与 EF Core 的 null 传播语义不同，见下方详注 |
| 嵌套投影深度          | 上限 3 层（超过降级）                                 |
| GroupBy 后 AsDto | **编译错误**（`[Obsolete(error:true)]`）           |
| ToChunk         | ToChunkDto 替代（分块返回已映射 DTO，无需降级）              |
| ToTreeList      | 直接支持 `ToTreeList<TTarget>()` 树形 DTO 投影（无需降级） |

**引用导航嵌套投影的空壳语义（FreeSql 通道）**：共享内核嵌套投影以 `IIF(导航 == null, null, new 嵌套DTO)` 表达 null 传播，EF Core 翻译器支持该形态；FreeSql 翻译器不支持。NCMapper 对 FreeSql 通道在 SQL 投影前剥除该保护（`IIF` → 直接 `new 嵌套DTO`），保持 SQL 投影能力。行为差异：

- **正常导航**：双端等值，无差异。
- **null 导航**：EF Core 给 `null`（精确 null 传播）；FreeSql 给**空壳 DTO**（嵌套对象非 `null`，`int` 字段为 `0`、引用字段为 `null`）——依赖 `dto.Dept != null` 判断"是否存在关联"的代码在 FreeSql 通道需改判空壳（如 `dto.Dept.Id != 0`）。
- **chunk 内存路径**（`ToChunkDto` / `ToChunkDtoAsync`）：不经剥保护，保持原版精确 `null` 语义（与 EF Core 一致）。
- **SQL 投影异常兜底**：剥保护后仍翻译失败时自动降级内存映射，并输出含原始异常的 Trace（不裸炸、诊断不静默）。
- **集合导航嵌套**：仍恒降级（既有保守策略不变）。

#### EF Core 特定

| 限制                       | 行为                                                                |
| ------------------------ | ----------------------------------------------------------------- |
| 集合导航投影                   | 支持（`Select+ToList` 子查询），但目标类型必须为 `List<T>` 或可赋值接口                 |
| 嵌套投影深度                   | 上限 8 层                                                            |
| GroupBy 后 AsDto          | **编译错误**（`[Obsolete(error:true)]`）                                |
| `AsEnumerable().AsDto()` | **编译错误**（`[Obsolete(error:true)]`）                                |
| AsNoTracking             | 默认启用，可通过 `NCMapperOptions.EFCoreProjectionAsNoTrackingDefault` 关闭 |

#### Dapper 特定

| 限制    | 行为                                          |
| ----- | ------------------------------------------- |
| 多结果集  | 不支持，用 `QueryMultiple` + `Mapper.MapList` 组合 |
| 投影表达式 | 不适用（Dapper 走 TypeMap 列名映射，非表达式树）            |

> **投影实现为内部 API**：各扩展包的 `ProjectionBuilder` 和 `DegradationDetector` 均为 `internal`，用户通过 `AsDto()` 终端操作间接使用。源生成器不再生成公共 Projection 扩展方法。

#### Fluent 通道边界

| 边界 | 说明 |
|------|------|
| Configure 方法体仅支持**纯链式静态调用** | 动态构造（if/for/赋值/局部变量等）报 `NCMAP_F08`；lambda 仅支持属性路径（`?.`/方法调用报 `NCMAP_F05`/`F07`；`Map` 源路径的常量整数索引段放行，变量/方法调用索引仍拒） |
| FromSource 块内禁区 | 块内禁止 `ConvertBack`（F30）/`NoAutoInclude`（F31）/`AfterMapping`（F32）——合成面无反向写回/自动包含/钩子落点 |
| NCMAP019 豁免 | Fluent 类型对被源生成器登记后，Dto 命名启发式提示对 Fluent DTO **自动豁免**（删除声明即恢复提示，增减可增量验证） |
| ORM 投影/降级边界 | 逐声明分流：纯可翻译声明走投影、`Mask`/`Convert`/`FromJson`/`AfterMapping`/`FromSource` 强制降级——详见 [explanation ORM 降级机制](./orm-degradation.md) §2.3 与各 ORM how-to 的「Fluent DTO 支持现状」节 |

### 18.4 AOT / NativeAOT 限制

- **源生成器路径**：AOT 友好，编译时生成全部代码
- **Compose**：公共 API 标注 `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`（AOT 限制标注）。源生成器已注册 `RegisterGeneratedCompose` 委托时 AOT 可用；回退到运行时 `MakeGenericType` 路径时不可用
- **Validate**：`Validate()` 无参版 AOT 可用（使用源生成器编译时验证结果；标注 `[Experimental("NCMapper001")]` 为 API 稳定性警告，非 AOT 限制）；`Validate(params Assembly[])` 标注 `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`，AOT 下不可用（反射扫描程序集）
- **DeepCopy**：源生成器路径 AOT 友好，运行时表达式树路径标注 `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`
- **ORM 扩展（FreeSql / EFCore）**：投影路径（`AsDto` → `ToList`/`ToChunkDto` 等）**无条件不可用**——`BuildProjection` 用 `MakeGenericMethod` 构建 SQL 投影表达式树，无源生成器替代（`RegisterGenerated*` 七种不含 Projection）；降级路径（内存 `MapList`/`Compose`）标 `[MapFrom]` 后条件可用，且依赖 ORM 运行时自身的 AOT 兼容性
- **ORM 扩展（Dapper）**：映射部分标 `[MapFrom]` 走源生成器（AOT 安全）；`Query` 委托编译用 `Expression.Compile()`，AOT 下不可用
- **通用降级路径**：`NeedsDegradation=true` 时 `Mapper.MapList/Compose`——标 `[MapFrom]` 走源生成器委托（AOT 安全）；未标则回退运行时编译（AOT 不可用）
- 生产环境推荐全部 `[MapFrom]` 标注走源生成器路径

**包级 trim 兼容声明**：NCMapper / EFCore / FreeSql / Dapper 四个库的项目文件均同时声明 `<IsAotCompatible>true</IsAotCompatible>` 与 `<IsTrimmable>true</IsTrimmable>`，提供无遮蔽标注的完整包级 trim 兼容声明——启用 trim 分析时不会因缺失标注而误报。

**AOT 反射守护**：`tools/AotReflectionGuard` 在 CI build 阶段以 MetadataLoadContext + PEReader 双通道扫描三个 ORM 扩展包的公开方法体 IL，一旦发现「含反射调用但无 `RequiresDynamicCode`/`RequiresUnreferencedCode` 标注」即令 CI 失败，确保 AOT 反射约束（24 项）不静默断裂、持续受监控。

### 18.5 性能建议

- 生产环境使用源生成器路径（零首次延迟，~13-27ns，net10.0 Linux 口径）
- 扩展方法 `MapToXxx()` 是最快路径（零委托，JIT 可内联，~13-27ns）
- 避免在热路径上使用 Compose（反射开销）
- 大批量映射使用 `MapList`（内联 for 循环，无逐元素委托）
- 深拷贝优先使用源生成器路径（~179ns，net10.0 Linux 口径），标注 `[MapFrom]` 自动生成
- `MetricsEnabled` 默认关闭以避免 Interlocked 开销
- .NET 10 PGO 对运行时路径有免费提升（-6% ~ -47%），但 DeepCopy 存在 net10 回归风险

### 18.6 Record 类型 + 循环引用图：源生成 DeepCopy 自动回退运行时路径

- **结论（用户无需手工干预）**：源生成器在**编译期自动对 record（或任何无无参构造器的类型）的属性图进行三值分类**（Acyclic/Cyclic/Unknown）。一旦分类为 **Cyclic 或 Unknown**（含自引用 `A.Self = A`、双向引用 `A↔B`、树形带父指针、或含 object/接口/开放泛型/virtual 引用属性等静态不可解析边），生成的 DeepCopy 方法体会**自动改写为** `return Mapper.DeepCopyRuntimeOnly<T>(src, ctx);`，直接走运行时路径（基于 `ConditionalWeakTable` 的循环引用检测 + 完整检测），并同时发出 **NCMAP012** 警告。Acyclic record 仍走源生成器快路径（省略 `RegisterDeepCopyTarget` 登记）。因此用户照常调用 `Mapper.DeepCopy(recordObj)` 即可得到**正确结果**（不会栈溢出），无需任何额外操作，只是 Cyclic/Unknown 类型放弃源生成器的性能优势、回退到运行时路径开销。
- **根因**：record 路径通过 `new T(arg1, arg2, ...)` 构造器调用创建 dst，dst 对象在构造器内部诞生，**无法在构造前预登记到循环引用检测表**；而无参构造路径会在 `var dst = new T()` 之后立即调用 `Mapper.RegisterDeepCopyTarget(src, dst)` 预登记 dst，使递归 `Mapper.DeepCopy(src.Prop)` 命中已登记 dst 并打破循环。由于构造器路径先天无法预登记，源生成器选择**编译期检测 + 整体回退**而非生成会无限递归的代码。
- **检测策略**：`HasCircularReference` 以 DFS 遍历类型的公开实例属性图（自动剥离 `Nullable` 注解、提取集合元素类型，如 `List<Node> → Node`），判断能否从类型自身出发回到自身。
- **影响范围**：仅影响源生成路径（Layer 1，`GeneratedDeepCopyCache<T>.Func`）；运行时 Provider 路径（Layer 2/3，基于 `ConditionalWeakTable`）对 record 循环引用图正常工作，回退后即走此路径。
- **手工干预（仅边缘场景）**：通常无需手工处理。仅在以下情况可考虑显式操作：
  - 循环引用在编译期不可见（如通过运行时动态装配形成的对象图），此时可直接调用 `DeepCopyProviderBase.Current.DeepCopy(source)` 强制走运行时路径；
  - 希望某类型的所有 DeepCopy 一律走运行时 Provider，可通过 `Mapper.RegisterGeneratedDeepCopy<T>(null)` 注销源生成委托。
- **典型场景**：record 自引用、record 双向导航属性、record 树形结构带父指针——这些均被编译期自动检测并回退，用户无感。

---

