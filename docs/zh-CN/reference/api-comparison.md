---
wiki-id: reference/api-comparison
库版本: v1.0.0+
更新时间: 2026-09-30
---

# Fluent 与 Attribute 双 API 面对照

> 本页是跨 API 面选型与迁移的**唯一对照页**。等价关系与 [Fluent 与 \[MapFrom\] 通道对比](../how-to/fluent/basics.md) 权威口径一致。选型总览另见 [getting-started/overview](../getting-started/overview.md)。

## 1. 30 秒选型

| 你的场景 | 建议 |
| -------- | ---- |
| DTO 已存在且希望零侵入（不污染 DTO 类） | **Fluent 通道**：`[MapperConfig]` 配置类集中声明 |
| 偏好"特性标在 DTO 上即所见即所得"、团队习惯 Attribute 风格 | **Attribute 通道**：`[MapFrom]` 族特性 |
| AOT / Trimmed 部署、追求编译期确定 | 两通道**同为编译期源生成、运行时零差异**——按声明位置偏好选择：DTO 想保持零侵入选 Fluent（配置类集中声明），偏好标注在类上选 Attribute |
| 需要构造器形参绑定（MapCtorParam） | **Fluent 通道**（独有能力，无特性侧形态） |
| 需要链式顺序依赖、同链多终端组合 | **Fluent 通道** |

两通道对同一映射对**语义等价且并存**；但同一对禁止双声明（`NCMAP_F11` 互斥）。

## 2. 等价关系总表

| 维度 | Fluent 通道 | `[MapFrom]` 通道 |
| ---- | ----------- | --------------- |
| 入口 | Mapper 配置类 + Configure 方法 | DTO 类上的特性 |
| DTO 类污染 | 否（意图集中在 Mapper 类） | 是（特性标在 DTO 上） |
| 配置粒度 | 命令式链式（属性级 + 顺序） | 属性级声明 |
| 顺序依赖 | 支持（链式调用天然顺序） | 不支持 |
| 正向自定义转换 | `.Convert`（4 形态模板，与特性侧等价；Member 段内连写） | `[Convert]`（4 形态） |
| 反向自定义转换 | `.ConvertBack`（4 形态模板，与特性侧等价；Member 段内连写） | `[ConvertBack]`（4 形态） |
| 方向性忽略 | `.Ignore(direction)`（Member 段内，与特性侧等价） | `[MapIgnore(Direction)]` |
| 生命周期钩子 | `.BeforeMapping`/`.AfterMapping`（双通道等价） | `[BeforeMapping]`/`[AfterMapping]`（类级） |
| 条件守卫 | `.MapWhen`（命名 + 内联转录双形态） | `[MapWhen]`（属性级，仅命名形态） |
| 属性级格式化 | `.Format(format)`（Member 段内；语义镜像，两通道同快照承载；`OverrideIgnore`/`overrideIgnore` 豁免门两侧同参） | `[MapFormat("格式串")]`（源 `IFormattable` → 目标 string） |
| 构造器形参绑定 | `.MapCtorParam`（Fluent 独有） | **无特性侧形态** |
| 多态子类型映射 | `.MapSubclass<TSrcDerived, TTargetDerived>()`（对级，与特性侧等价） | `[MapSubclass]`（类级多实例） |
| JSON 展开 | `.FromJson()`（Member 段内，与特性侧等价） | `[MapProperty(IsFromJson)]` |
| FromSource 跨源绑定 | `.Member(d => d.X).FromSource<子源>(s => s.Y)`（速记）/ `.FromSource<子源>(cfg => …)`（对级分组块） | `[MapProperty(FromSource = typeof(子源))]` |
| 禁用自动 Include（ORM 导航） | `Member` 段内 `NoAutoInclude`（字段级，见 [fluent-index](fluent-index.md)）/ `.NoAutoMatch()`（对级白名单开关） | `[MapNoAutoInclude]`（属性级）/ `[MapNoAutoMatch]`（类级） |
| 树形声明（`ToTreeList` 消费） | `.Tree()` / `.Tree(id, parent, children)`（对级挂载位三元组）；属性级递归另有 `.Member(d => d.X).MapTree()`（特性侧无属性级形态） | `[MapTree]`（类级挂载位三元组） |
| 全局配置 | 无独立 Fluent 终端（同一运行时全局配置，`Mapper.GetGlobalConfig()` 读只读视图） | `[assembly: MapDefaults]`（程序集级：命名策略/忽略清单） |
| 编译期诊断 | `NCMAP_F` 系列 | `NCMAP0xx` |

> 上表为权威口径。引擎级语义（匹配规则/方向/诊断模型）由语义共享层单点同源承载：[映射规则](../explanation/semantics/mapping-rules.md)、[双向方向语义](../explanation/semantics/direction-semantics.md)、[诊断码体系模型](../explanation/semantics/diagnostics-model.md)。两通道为什么并存、如何在编译期汇流与互斥的架构原理见 [双通道架构](../explanation/dual-channel.md)。

## 3. 差异化定位

- **Fluent 通道（第一主线）**：表达力上限（顺序依赖、内联 lambda 转录、MapCtorParam、链内组合）；意图集中不污染 DTO；新功能文档默认进 Fluent 线。
- **Attribute 通道（第二体系）**：源生成器编译期入口，DTO 自描述；AOT/零反射场景推荐面；`[MapFrom]` 双向语义一次声明承载正反两向。

## 4. 迁移指引

从混写页（同一页并列两写法）迁移到单面组织：

1. 按 30 秒选型定主面；
2. 等价声明按上表逐行替换（等价对编号 A-xx 为迁移对账键）；
3. 特性通道独有表达（如 `[assembly: MapDefaults]` 全局配置）保留在特性侧，不强行翻成 Fluent；
4. 双声明同对会触发 `NCMAP_F11`，迁移后删除另一侧声明。
