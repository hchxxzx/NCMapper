---
wiki-id: fluent/convert
涉及诊断码: NCMAP_F17-F28
库版本: Unreleased（随下个版本发布）
更新时间: 2026-10-23
---

# Fluent Convert / ConvertBack：双向自定义转换

> Fluent 线值级变换专题（正/反向自定义转换四形态 + FromJson）。链式基础见 [basics](basics.md)；转换的引擎级语义（转换链、反向不消费规则）见语义共享层 [direction-semantics](../../explanation/semantics/direction-semantics.md)。

## Convert：正向自定义转换

`Convert` 是 Member 三段式配置段终端，为 Fluent 通道提供正向自定义转换能力，语义与特性通道 `[Convert]` **1:1 镜像**（与特性通道能力对齐）。锚点为 `.Member` 段首锚定的目标字段（TTarget 侧，TProp 随锚定从签名消失），源侧解析序与特性侧一致：**同名属性 → 自动扁平化 → `[MapProperty]` 反向查找**；同字段段先行的 `From` 显式源路径优先（链序无关）。与 `From`（表达式树来源声明）同锚点可**共存不冲突**（方法组编译期解析 vs 表达式树运行时求值，机制不同）——变换作用于显式来源取值（支持"自定义来源 + 转换"组合）。

**四个重载即转换形态**（与 `[Convert]` 的 4 形态模板对应；`TProp` = Member 锚定的目标字段类型）：

| 重载 converter 形态                                                 | 转换形态  | 典型场景                     |
| ----------------------------------------------------------------- | ----- | ----------------------- |
| `Convert(Func<TProp>)`（`static TProp M()`）                     | T0 常量 | 写入固定值（源侧同名属性须存在，值不读取）  |
| `Convert<TSrcProp>(Func<TSrcProp, TProp>)`（`static TProp M(TField)`）      | T1 字段值 | DateTime→string 格式化（最常见） |
| `Convert(Func<TSource, TProp>)`（`static TProp M(TSource)`）       | T2 整源 | 多字段组合（读整个源对象；方法组可直用——入参 TSource 类级固定有推断位）           |
| `Convert<TSrcProp>(Func<TSource, TSrcProp, TProp>)`（`static TProp M(TSource, TField)`） | T3 组合 | 整源 + 字段值双入参             |

> 单参 == 源实体类型判 T2 整源，否则判 T1 字段值（提取期形态判定，与特性侧 Q3 优先序同构；单参同时 == 源实体与源字段类型时判整源 T2）。源侧属性 Nullable + converter 入参非空时自动生成 `HasValue` 三元守卫（null 不调用、目标保持 default）。

可运行示例（`dotnet run --scenario 102` 实跑验证）：

[!code-csharp[Convert 四形态链式声明（T0/T1/T2/T3）](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_Convert)]

**方法组推断指引（重要）**：C# 类型推断**不从方法组推断委托输入位的泛型参数**，T1（字段值）/T3（组合）重载的 `TSrcProp` 位于委托入参位（无推断位），方法组直用须显式类型参 `.Convert<TSrcProp>(M)`，否则触发编译器 `CS1503`。三种合法写法：

三种合法写法示例（T1 显式类型参形态见 Convert 四形态示例；委托转换形态见 ConvertBack 解歧示例）：

[!code-csharp[方法组推断：显式类型参与委托转换实写](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_Convert)]

T0（无参）与 T2（整源，入参 `TSource` 类级固定）重载的方法组可直用；converter 方法组自身多重载（如同时含 `M(string)` 与 `M(Foo)`）时可能触发 `CS0121`，解歧手段同上（显式委托转换）。

**可见性契约**：converter 须为**可静态解析的方法组**或**无捕获的内联 lambda**（转录进生成代码）；`private`/`protected` 报 `NCMAP_F25`；**闭包**（捕获外层状态）/局部函数/实例绑定报 `NCMAP_F26`（转录禁令的残余面）；签名不匹配四形态模板报 `NCMAP_F27`；get-only/非 public setter 锚点报 `NCMAP_F28`（Warning）；源侧（同名/扁平化/反向查找）全不可达报 `NCMAP_F24`（Error）；同字段段 `Ignore` + `Convert` 矛盾报 `NCMAP_F23`（Error）。F26 是 Convert 的既有码（收窄后只管闭包类），MapWhen 内联转录禁令码是 F41/F42（另一码族），勿混用。

## ConvertBack：反向自定义转换

`ConvertBack` 是 Member 三段式配置段终端，为 Fluent 通道提供反向自定义转换能力，语义与特性通道 `[ConvertBack]` **1:1 镜像**（与特性通道能力对齐）。锚点为 `.Member` 段首锚定的目标字段（DTO 侧，与链上其他终端方法同向；TProp 随锚定从签名消失，实体侧按名解析反向写回点），实体侧按**同名属性**解析反向写回点；**正向映射不消费**本声明（正向按类型转换路径处理）；与同字段段正向转换声明配对、各自独立可选。

**四个重载即转换形态**（委托元数自动选择，与 `[ConvertBack]` 的 4 形态模板对应；`TProp` = Member 锚定的 DTO 侧字段类型）：

| 重载 converter 形态                                                             | 转换形态  | 典型场景                    |
| --------------------------------------------------------------------------- | ----- | ----------------------- |
| `ConvertBack<TEntityProp>(Func<TEntityProp>)`（`static TEntityProp M()`）                                   | T0 常量 | 反向写入固定值                 |
| `ConvertBack<TEntityProp>(Func<TProp, TEntityProp>)`（`static TEntityProp M(TField)`）                   | T1 单值 | string→DateTime 解析（最常见） |
| `ConvertBack<TEntityProp>(Func<TTarget, TEntityProp>)`（`static TEntityProp M(TDto)`）                   | T2 整源 | 多字段组合（读整个 DTO）          |
| `ConvertBack<TEntityProp>(Func<TTarget, TProp, TEntityProp>)`（`static TEntityProp M(TDto, TField)`） | T3 组合 | 整 DTO + 字段值双入参          |

可运行示例（`dotnet run --scenario 102` 实跑验证；四形态 + 同锚点配对承载于同一条链）：

[!code-csharp[ConvertBack 四形态与同锚点配对链式声明](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_ConvertBack)]

**可见性契约**：converter 须为**可静态解析的方法组**或**无捕获的内联 lambda**（转录进生成代码，与 MapWhen 内联形态共享转录引擎）；`private`/`protected` 报 `NCMAP_F19`；**闭包**（捕获外层状态）/局部函数/实例绑定报 `NCMAP_F20`（转录禁令的残余面）；签名不匹配四形态模板报 `NCMAP_F21`；get-only/非 public setter 锚点报 `NCMAP_F22`（Warning，声明将被物化阶段忽略，改用 public/init setter 或特性通道实体侧 `[ConvertBack]`）；同字段段 `Ignore` + `ConvertBack` 矛盾报 `NCMAP_F17`（Error）。注意：F20 是 ConvertBack 的既有码（收窄后只管闭包类），MapWhen 内联转录禁令码是 F41/F42（另一码族），勿混用。

**重载歧义解歧**：当 `TProp == TTarget`（自引用 DTO）或 converter 方法组自身多重载（如工具类同时含 `M(string)` 与 `M(OrderDto)`）时，可能触发编译器 `CS0121`，**显式类型参数不能消解**；解歧手段是 converter 实参显式委托转换：

解歧写法示例（T1/T3 形态输入位显式委托转换，`dotnet run --scenario 102` 可运行）：

[!code-csharp[ConvertBack 转换器显式委托转换解歧](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_ConvertBack)]

**Nullable 语义**：锚点属性为 `Nullable<T>` 时，converter 入参同为 `Nullable<T>` 即直传（null 由转换器自行处理）；入参为非空 `T` 的守卫变体（`HasValue` 三元短路）经方法组 API 结构性不可达（委托无值类型逆变），Fluent 通道建议转换器直接接收 `Nullable<T>` 自行判空。

**ReverseOnly 场景形态建议**：反向无源条目（对级 `NoAutoMatch` 白名单，或自动匹配未命中）的 T1/T3 形态字段实参为 `default` 字面量（与特性侧无源条目同构），需要字段值实参的场景请优先使用 T0/T2 形态，或为锚点字段配正向声明（`From` / 同名可自动匹配）。

## FromJson：JSON 字符串展开

配置段终端 `FromJson()`（挂在 Member 字段段上）语义等同 `[MapProperty(IsFromJson = true)]`：源侧 **string** 属性经 `JsonConverter` 反序列化为目标**复杂类型**属性（正向 string→对象；反向序列化写回）。源/目标均须为 string 或引用类型（值类型参与时声明被静默丢弃，与特性侧同口径）；省略 `From` 时按目标字段名自动匹配同名源属性。

可运行示例（`dotnet run --scenario 102` 实跑验证）：

[!code-csharp[FromJson：JSON 字符串展开声明](~/samples/NCMapper.Samples/01-Fluent/CvModels.cs#FluentConvert_FromJson)]

## 相关页

- 全族诊断索引：[basics 诊断表](basics.md)
- 特性侧等价形态：[api-comparison](../../reference/api-comparison.md)
