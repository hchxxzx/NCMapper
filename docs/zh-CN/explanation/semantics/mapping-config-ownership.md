---
wiki-id: explanation/semantics/mapping-config-ownership
库版本: v1.0.0+
更新时间: 2026-10-10
---

# 映射配置所有权三原则

> [!IMPORTANT]
> 以下为核心口径，正式版延续。

NCMapper 对映射配置的消费遵循三条置顶原则（白话版；正式定义与设计意图见 [映射规则（语义）§6](mapping-rules.md)）：

1. **没有锚，就视而不见**：如果类型既没有标 `[MapFrom]`、也没有经 Fluent `cfg.For<S,T>()`
   声明为映射对，NCMapper 对该类型上的**任何**映射配置标注（`[MapProperty]`/`[MapIgnore]`/
   `[Mask]`/`[Convert]`/`[ConvertBack]`/`[MapWhen]`/`[MapFormat]`/`[MapNoAutoInclude]`/
   `[BeforeMapping]`/`[AfterMapping]`/`[MapNoAutoMatch]` 十一项）一律**视而不见**：不消费、不生效，
   映射退化为纯按名匹配。
2. **编译期给你看**：带上配置标注却无锚的类型不会静默失效，源生成器在编译期报诊断：
   含映射配置标注的报 **NCMAP019（Error）**；仅 Dto 命名启发式命中的报 **NCMAP042（Info）**。
3. **承载侧是唯一登记面**：方向性声明（如 `[MapIgnore(Direction.ToEntity)]`）以承载侧
   （标 `[MapFrom]` 的一侧）为唯一登记位。


## 锚的两条通道与属性级标注的准入（2026-10-10 起）

"有锚"不是单一判据——映射声明锚有两条通道，而**属性级标注的准入按特性锚判定**：

| 承载形态 | 判定输入 | 属性级标注（`[MapProperty]`/`[MapIgnore]`/`[Mask]`/`[Convert]`/`[ConvertBack]`/`[MapFormat]`） |
|---|---|---|
| 特性通道：DTO 标 `[MapFrom]` | 特性锚 = 真 | **消费**（生成端与运行时回退端一致） |
| Fluent 通道：`cfg.For<S,T>()` 声明、DTO 无 `[MapFrom]` | 特性锚 = 假（仅 Fluent 锚） | **不消费**——配置请用 Fluent 声明（`.Member(...).From/MapTree/Mask/Format/FromJson/Convert`）表达 |
| 混合：`[MapFrom]` 与 `cfg.For<S,T>()` 并存 | 特性锚 = 真 | **消费**（并存不互相抵消） |
| 无锚 | 两锚均假 | 不消费 + 编译期 NCMAP019（Error）/ NCMAP042（Info） |

要点：

- **Fluent 对上的残留标注不再"压过"Fluent 明细**：运行时回退路径不会用属性级标注覆盖 Fluent 声明的结果（此前"生成端不读 / 运行时读"的双引擎分叉已修正）。
- 同一口径贯穿 ORM 投影与降级判定：Fluent 对上的残留标注既不会把列从投影中剔除，也不会单独触发投影降级。
- 例外：`[MapWhen]` 属特性通道的条件守卫声明——Fluent 对上同样不作为映射配置消费，但**仍会触发 ORM 投影降级**（保守语义，保证条件守卫不被静默丢弃）。
- 诊断口径：Fluent 对上的属性级标注在 `MapTracer` 报告中如实标为 `⚠️ not consumed (Fluent pair — attribute ignored; use Fluent declarations)`，与"无锚 = `⚠️ invalid config`"分列两态。

## 补锚路径（遇到 NCMAP019 / NCMAP042 怎么办）

| 诊断 | 含义 | 修复路径 |
|------|------|----------|
| NCMAP019（Error） | 类型带映射配置标注但无映射声明锚 | ① 给类型标 `[MapFrom(typeof(源类型))]`（特性通道）；② 或在 `[MapperConfig]` 配置类中 `cfg.For<S, T>()` 声明（Fluent 通道）；③ 若标注本就多余，直接删除标注。逐码条目见[诊断规则](../../diagnostics/rules.md) |
| NCMAP042（Info） | 类型名以 Dto/DTO 结尾但无映射声明（AOT/裁剪下反射回退不可用的提示） | ① 同上补锚（生成代码后提示前提消失）；② 或确认该类型不参与映射，忽略本提示 |

## 外部程序集与动态类型

- **外部程序集类型**（第三方库的 DTO/实体，无法修改源码标注）：用 Fluent 通道
  `cfg.For<S, T>()` 声明映射对即可，无需改动类型本身；编译期诊断按声明状态判定，
  行为与特性通道一致。
- **动态类型**（Reflection.Emit / 可收集程序集）：反射回退路径对动态类型直算不入缓存；
  动态类型上的映射配置标注**一律不消费**（无编译期诊断面，详见
  [双向映射语义](../reverse-mapping-semantics.md)）。
