<!-- 来源：NCMapper 主技术文档 §10 多源合成 Compose -->
## 10. 多源合成 Compose

> ⚠️ **Experimental API**（ 补注）：`Mapper.Compose` / `ComposeList` 及 `IMapper.Compose` 标注 `[Experimental("NCMapper001")]`，首次调用产生 NCMapper001 编译警告；该码为实验性 API 标记，与源生成器诊断码 NCMAP001 命名不同，请注意区分。

当一个 DTO 的字段分散在多个实体中（如 `UserDto` 同时需要 `UserEntity` 的基本信息、`DepartmentEntity` 的部门名、`CompanyEntity` 的公司名），传统做法是分别查询再手工拼装。`Compose` 用 `[MapProperty(typeof(X), "field")]` 声明每个字段的来源实体，一次调用从多个源对象合成目标 DTO，支持批量合成与回调。

> 字段名字符串（`"field"`）与 `typeof + nameof` 双参均为**合法写法**，推荐 `nameof` 以获得编译期校验。`fromField` 支持 `xx.xx` 点路径（Compose 跨表点路径由源生成器拆段解析）；失败时输出 NCMAP005 带失败段名，详见 [§5.2](../reference/features.md) / [§18.3](limitations.md)。

### 10.1 基础用法

```csharp
// DTO 标注：UserDto 主源是 UserEntity，其他字段来自不同实体
[MapFrom(typeof(UserEntity))]
public class UserDto
{
    public int Id { get; set; }                                   // UserEntity
    public string Name { get; set; }                              // UserEntity
    [MapProperty(typeof(DepartmentEntity), "DeptName")]
    public string Department { get; set; }                        // DepartmentEntity
    [MapProperty(typeof(CompanyEntity), "CompanyName")]
    public string Company { get; set; }                           // CompanyEntity
}
```

四类合成调用的可运行版本（演示类型对为订单/客户/产品三源，形态一致）：

[!code-csharp[合成调用（三源）](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose_MultiSourceCompose)]
[!code-csharp[带 Action 回调](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose_ComposeWithAction)]
[!code-csharp[批量合成](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_ComposeList)]
[!code-csharp[批量合成 + 回调](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_ComposeListWithAction)]

### 10.2 规则

**源处理顺序（规则2）：**

1. **主源按类型定位**：`[MapFrom(typeof(X))]` 声明的类型为主源——运行时按类型在数组中定位（**位置不限，不必位于数组首位**），恒第一个处理，负责创建 DTO
2. **其余按数组顺序**：非主源按传入 `sources` 数组的原始顺序依次更新 DTO
3. **后来者覆盖**：若主源与非主源映射了 DTO 的同一字段（非主源通过 `[MapProperty]` 声明），非主源的值覆盖主源的值

其他规则：

- 非主源仅映射标注了 `[MapProperty(typeof(该源类型), ...)]` 的字段；主源自动映射所有同名字段
- 主源未找到时抛出异常（`MapConfigurationException`，消息明确指出缺少哪个主源类型）
- 传入 `null` 的源被跳过，不影响其余源的处理顺序
- **重复主源**：若多个源类型都声明为主源（重复 `[MapFrom]` 主源类型），运行时 `ComposeRuntime` 两遍扫描——第一个主源负责创建 DTO，后续重复主源对其执行 `Update`（覆盖式更新）；源生成器路径此前静默忽略重复主源，现已对齐为相同语义。

[!code-csharp[多源合成：主源可在任意位置，始终第一个处理](~/samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs#Compose_BasicCompose)]

---

