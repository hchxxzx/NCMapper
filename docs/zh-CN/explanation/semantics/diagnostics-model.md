---
wiki-id: explanation/semantics/diagnostics-model
涉及诊断码: NCMAP099
库版本: v1.0.0+
更新时间: 2026-09-28
---

# 语义共享层：诊断码体系模型

> 本篇是诊断码体系模型的**单点同源**页。全量诊断码条目表见 [diagnostics/rules.md](../../diagnostics/rules.md)（诊断码主表：数字族全表 + Fluent 族分页转指，数字族与 AnalyzerReleases 三方对账）；本篇只讲体系模型。

## 1. 两族命名

| 族 | 形态 | 覆盖 |
| -- | ---- | ---- |
| 特性/引擎族 | `NCMAP001`-`NCMAP042` + `NCMAP099` | 特性通道声明诊断 + 引擎行为诊断（深拷贝/Compose/ORM 投影/全局配置等） |
| Fluent 族 | `NCMAP_F02`-`NCMAP_F60`、`NCMAP_F62`-`NCMAP_F63` | Fluent 链式声明面诊断（lambda 形态校验、链约束、终端语义冲突、静默语义显式化等） |

`NCMAP004`、`NCMAP008` 与 Fluent 族 `F13/F33/F34/F61` 为预留空号，码号不再使用、不复用编号。

## 2. 三级严重度语义

| 级别 | 行为 | 使用哲学 |
| ---- | ---- | ---- |
| Error | 阻断编译 | 声明矛盾、签名不可满足等"生成即错误"的场景 |
| Warning | 不阻断，建议处理 | 可能有意的写法但大概率是遗漏（消息给双向处置指引） |
| Info | 仅提示 | 刻意设计的退化/跳过/限制，保证**可观测**而非可忽略 |

## 3. 三方同步纪律

新诊断码登记须三方同步（G3 门禁机械化校验）：

1. `src/NCMapper.SourceGen/AnalyzerReleases.Shipped.md` / `Unshipped.md`（Roslyn 标准 release tracking，New Rules 节）；
2. `docs/zh-CN/diagnostics/rules.md`（用户可见诊断码主表：数字族全表 + Fluent 族分页转指）；
3. 源码描述符（ID + 严重度以描述符为准绳）。

`scripts/check-diagnostics-sync.sh`（CI G3 步骤）断言 ID 集合相等、严重度一致、无重复登记。文档侧新增码时同步登记，违例即 CI 红。

## 4. 三原则与检测器对照

映射声明的三条基本规则与编译期检测器成对：

| 规则 | 检测器 |
|------|--------|
| P1 通道互斥（同一对类型只能一条通道声明） | `NCMAP_F11` · `NCMAP025` |
| P2 侧别互斥（`[MapFrom]` 单类单锚；`For<A,B>`/`For<B,A>` 不得同声明） | `NCMAP025` · CS0579 |
| P3 配置跟随类锚（属性级标注无锚不认） | `NCMAP019` · `NCMAP042` |

完整对照表见 [diagnostics/rules.md 三原则与检测器对照](../../diagnostics/rules.md)。

## 5. 抑制与兜底

- 单码抑制：`<NoWarn>NCMAP001</NoWarn>`（Error 级不可抑制）。
- `NCMAP099`：单映射源生成失败的兜底警告（生成失败降级运行时路径，其余映射不受影响）；若复现请提 Issue。

## 6. 深入阅读

- [diagnostics/rules.md](../../diagnostics/rules.md)（全码条目表）
- [验证与诊断](../../how-to/infrastructure/validation.md)（运行时验证/追踪操作向）
