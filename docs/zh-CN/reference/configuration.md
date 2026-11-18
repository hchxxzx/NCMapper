---
title: 配置与扩展
---

<!-- 来源：NCMapper 主技术文档 §17 配置与扩展（结构重组，2026-08-19 章级迁移，内容未重排） -->
## 17. 配置与扩展

NCMapper 的默认行为可按需替换：JSON 序列化策略默认 `System.Text.Json`，可换 `Newtonsoft.Json`；深拷贝策略默认 `FastDeepCopyProvider`，可自定义；运行时开关控制验证、Trace、性能指标的启停。通过 `JsonSerializerBase.Current` / `DeepCopyProviderBase.Current` / `NCMapperOptions` 等扩展点，让组件适配不同项目的技术栈与需求。

### 17.1 JSON 序列化策略

```csharp
// 默认使用 System.Text.Json
// 替换为 Newtonsoft.Json
public class NewtonsoftJsonSerializer : JsonSerializerBase
{
    public override string Serialize(object? obj) => JsonConvert.SerializeObject(obj);
    public override object? Deserialize(string json, Type type) => JsonConvert.DeserializeObject(json, type);
}
JsonSerializerBase.Current = new NewtonsoftJsonSerializer();
```

### 17.2 深拷贝策略

默认 `FastDeepCopyProvider`（三值分类条件分发：Acyclic 零检测快路径 ~401ns / Cyclic-Unknown 转发基类完整检测，任意环长），可替换为 `ExpressionTreeDeepCopyProvider`（含完整循环引用检测 + DAG 共享引用保持，~409ns）或自定义策略（均为 net10.0 Linux 实测口径）。详见[深拷贝体系 §8.2](../explanation/deep-copy.md)。

> ⚠ **DAG 共享引用**：Fast 路径 Acyclic 类型的 DAG 共享引用为"逐处独立复制"。

### 17.3 源生成器控制

| 开关                           |   默认  | 说明       |
| ---------------------------- | :---: | -------- |
| `ValidationEnabled`          |  true | 运行时验证    |
| `TraceEnabled`               | false | 映射 Trace |
| `MetricsEnabled`             | false | 性能指标计数   |
| `<NCMapperSourceGenEnabled>` |  true | 编译时诊断    |
| `<NCMapperEmitGeneratedFiles>` | false | 编译期将源生成产物写出为物理文件（obj/NCMapper/Generated），默认关闭 |

### 17.4 NCMapperOptions 配置模式

`NCMapperOptions` 支持代码回调与 appsettings.json 两种配置方式。配置项一览：

| 配置项                 | 类型         | 默认值               | 可从 JSON 绑定 | 说明                                          |
| ------------------- | ---------- | ----------------- | ---------- | ------------------------------------------- |
| `ValidationEnabled` | bool       | true              | 是          | 是否启用映射验证                                    |
| `TraceEnabled`      | bool       | false             | 是          | 是否启用映射诊断 Trace                              |
| `MaxRecursionDepth` | int        | 64                | 是          | 最大映射递归深度，超过视为循环引用                           |
| `MaxInlineDepth`    | int        | 5                 | 是          | 表达式树构建时的最大内联深度                              |
| `DeepCopyStrategy`  | 枚举         | Fast              | 是          | 深拷贝策略，可选 `Fast` / `ExpressionTree` / `Json` |
| `TraceOutput`       | TextWriter | null（Console.Out） | 否          | Trace 输出目标，TextWriter 无法从 JSON 绑定，仅代码配置     |

`DeepCopyStrategy` 枚举对应三个内置 `DeepCopyProviderBase` 实现，应用到 `DeepCopyProviderBase.Current`：`Fast` → `FastDeepCopyProvider`（默认，三值分类条件分发：Acyclic 零检测快路径 / Cyclic-Unknown 转发基类完整检测）、`ExpressionTree` → `ExpressionTreeDeepCopyProvider`（含完整循环引用检测 + DAG 共享引用保持）、`Json` → `JsonDeepCopyProvider`（JSON 中转兜底 ~1697ns，不支持循环引用）。

```json
// appsettings.json
{
  "NCMapper": {
    "ValidationEnabled": false,
    "TraceEnabled": true,
    "MaxRecursionDepth": 128,
    "MaxInlineDepth": 5,
    "DeepCopyStrategy": "ExpressionTree"
  }
}
```

两种注册方式的可运行版本（方式一 = `AddNCMapper(IConfiguration)` 重载直绑配置节；方式二 = Options 回调，用于 `TraceOutput` 等无法从 JSON 绑定的项）：

[!code-csharp[方式一（推荐）：直接绑定配置节](~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs#DI_ConfigSectionBind)]
[!code-csharp[方式二：代码回调注册（Options 委托）](~/samples/NCMapper.Samples/06-Infrastructure/DIScenarios.cs#DI_ResolveMapperWithOptions)]

> ℹ️ **JSON 序列化策略仅代码配置**：`JsonSerializerBase.Current` 内置实现只有 `SystemTextJsonSerializer`，自定义序列化器（如 Newtonsoft）是用户自行继承 `JsonSerializerBase` 编写的类型，需通过 `JsonSerializerBase.Current = new MySerializer()` 在代码中赋值。它不属于 `NCMapperOptions`，也不适合用配置字符串激活（那需要反射创建用户类型，与库的 AOT 取向相悖）。

### 17.5 全局映射配置（`[assembly: MapDefaults]`）

> 一次声明即应用于**所有**映射的全局规则：命名策略、前后缀剥离、按名/按类型/按前后缀忽略清单、ToEntity 方向额外清单。解决"逐属性标注"的重复劳动——对既有项目，一段全局配置即可批量统一命名约定与忽略规则。

```csharp
// 程序集级特性（AssemblyInfo.cs 或任意文件的 using 区后、namespace 前）
[assembly: MapDefaults(
    NamingStrategy = NamingStrategy.SnakeCase,          // 命名策略（见下）
    Prefixes       = new[] { "Tbl" },                   // 源属性名前缀剥离
    Postfixes      = new[] { "Entity" },                // 源属性名后缀剥离
    IgnoreNames    = new[] { "RowVersion", "CreatedAt" }, // 双向忽略：精确名
    IgnoreTypes    = new[] { typeof(byte[]) },           // 双向忽略：类型全名
    IgnoreNamePrefixes = new[] { "Internal" },          // 双向忽略：名前缀
    IgnoreNameSuffixes = new[] { "Stamp" },             // 双向忽略：名后缀
    IgnoreNamesToEntity = new[] { "ComputedField" })    // 仅 ToEntity 方向忽略
```

**命名策略枚举**（`NamingStrategy`，作用于"单段属性名的字符级比较"；四种策略下自动扁平化均保持生效，二者正交叠加）：

| 枚举值 | 匹配规则 | 示例 |
|--------|----------|------|
| `Default`（默认） | 忽略大小写同名匹配（现状行为，零变更） | `username` ↔ `UserName` |
| `CaseSensitive` | 大小写敏感精确匹配（关闭忽略大小写） | `UserName` ↔ `UserName`；`username` 不匹配 |
| `SnakeCase` | snake_case ↔ PascalCase 双向归一化 | `user_name` ↔ `UserName` |
| `UpperSnakeCase` | UPPER_SNAKE_CASE ↔ PascalCase 双向归一化 | `USER_NAME` ↔ `UserName` |

**参数全览**（共 11 个，按作用域分三组；各清单上限 8 项，超限降级"未设置"+ NCMAP020）：

| 分组 | 参数 | 匹配规则 |
|------|------|----------|
| 命名 | `NamingStrategy` | 全局命名策略（默认 `Default`；Snake/UpperSnake 归一化为"去下划线+转大写"，匹配面宽于蛇形↔驼峰） |
| 源侧剥离 | `Prefixes` / `Postfixes` | 剥离源属性名前缀/后缀后再匹配（OrdinalIgnoreCase、剥后名非空；剥缀变体入键**撞键先到先得**——声明序首个保留） |
| 双向忽略 | `IgnoreNames` | 属性名 **OrdinalIgnoreCase** 精确命中即忽略（不区分大小写） |
| 双向忽略 | `IgnoreTypes` | 属性类型精确命中即忽略（按 `Type.FullName` Ordinal，开放泛型不匹配） |
| 双向忽略 | `IgnoreNamePrefixes` / `IgnoreNameSuffixes` | 属性名前缀/后缀 **OrdinalIgnoreCase** 命中即忽略（与剥离的区别：命中直接放弃，不继续匹配） |
| ToEntity 额外 | `IgnoreNamesToEntity` / `IgnoreTypesToEntity` / `IgnoreNamePrefixesToEntity` / `IgnoreNameSuffixesToEntity` | 仅 ToEntity 方向生效，与双向清单取并集 |

**方向语义**：ToEntity 方向忽略 ⟺（双向清单命中）∨（ToEntity 清单命中）；ToDto 方向仅双向清单生效（无 ToDto 额外清单）。**生效时机**：源生成路径本期对 `[MapFrom]` 类型固定产出 ToDto 方向（`isToDto=true` 硬编码），`*ToEntity` 清单不参与；它服务于**运行时反向映射**（目标类型无 `[MapFrom]` 即判定为 ToEntity 方向，由运行时 `PropertyIgnoreChecker` 裁决）。

**三层优先链**（逐项非空覆盖，细粒度层优先）：

1. **属性级标注** > 2. **`[assembly: MapDefaults]`** > 3. **MSBuild 属性**（`<NCMapperNamingStrategy>` 等，经 `buildTransitive/NCMapper.targets` 白名单通道）> 内建默认（`Default` + 空清单）。

**MSBuild 通道**（对参与源生成的编译单元生效；清单分隔符为逗号 `,`，非分号）：

```xml
<PropertyGroup>
  <NCMapperNamingStrategy>SnakeCase</NCMapperNamingStrategy>
  <NCMapperIgnoreNames>RowVersion,CreatedAt</NCMapperIgnoreNames>
</PropertyGroup>
```

**两段式冻结语义**：`MapDefaults` 快照由源生成器在编译期读取并生成 `Mapper.RegisterGeneratedConfig` 注册代码；首次映射/验证触发**冻结**——冻结后配置不可修改（无修改入口）。多程序集各自声明不同配置时，首次冻结抛 `MapConfigurationException` 并列出冲突程序集名（fail-fast）；内容一致则幂等合并。

**七项语义边界**（实现约定，文档与行为一致）：

| # | 语义边界 | 说明 |
|---|----------|------|
| 1 | 前后缀剥离仅作用于**源侧** | `Prefixes`/`Postfixes` 只剥离源属性名后再匹配目标属性名，不对目标侧做反向剥离（v1.1 G2） |
| 2 | `[MapProperty].FromField` 恒 OrdinalIgnoreCase 字面匹配 | 显式标注不随命名策略变化：SnakeCase 下 `FromField = "user_name"` 仍按字面 `user_name` 匹配（v1.1 G3） |
| 3 | `IgnoreTypes` 精确匹配 | 按 `Type.FullName` 精确匹配，开放泛型（含反引号 arity）不匹配（v1.1 G4） |
| 4 | MSBuild 通道仅对参与源生成的编译单元生效 | 无 `[MapFrom]` 的运行时回退路径不读 MSBuild 属性；`Mapper.Validate()` 同样触发冻结（v1.1 G6） |
| 5 | 两段式冻结 + 程序集名报告 | 冻结后注册不同内容当场抛异常（消息含冲突双方程序集名）；清单顺序差异（`["a","b"]` vs `["b","a"]`）不误判冲突（v1.1 W2） |
| 6 | 剥缀变体撞键先到先得 | 源字典按声明序填充（原名→剥缀变体→组合变体），先声明的属性抢占键，同名后声明属性被丢弃——剥缀清单可能改变同名属性的绑定（v1.2 Q2） |
| 7 | 豁免为映射对级 | `OverrideIgnore=true` 挂在**目标属性**上，豁免作用于整对映射（源、目标两侧全局判定均跳过）（v1.2 I2） |

**与 `[MapIgnore]` / `OverrideIgnore` 的交互**：属性级 `[MapIgnore]` 为显式忽略，恒生效、不参与豁免（与 `OverrideIgnore=true` 同标即矛盾，源生成器报 NCMAP022）；全局忽略清单为 fail-closed——命中即忽略，但目标属性带 `OverrideIgnore=true` 的显式特性（`[MapProperty]`/`[Convert]`/`[Mask]`）可豁免整对映射（NCMAP021 指引）。

**读取冻结配置**：`Mapper.GetGlobalConfig()` 返回只读视图 `GlobalConfigView`（不可变 record，清单为防御性副本，底层数组不外泄），用于调试、日志、测试断言：

[!code-csharp[读取全局配置视图](~/samples/NCMapper.Samples/06-Infrastructure/GlobalConfigScenarios.cs#GlobalConfig_MapGlobal)]

**回退通道**：无源生成代码的程序集（无 `[MapFrom]` 类型）中，`Mapper` 通过反射读取 `[assembly: MapDefaults]` 兜底；但反射通道不读 MSBuild 属性（该通道仅对参与源生成的编译单元生效）。

---

