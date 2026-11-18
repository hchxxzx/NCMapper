# NCMapper 文档门户

NCMapper 是 .NET 高性能对象映射库：**源生成器 + 表达式树**双引擎自动选择最优路径，编译期诊断码守护映射正确性，覆盖 ORM 投影（`AsDto`）、深拷贝、脱敏、DI 集成。

**3 秒定位**——先回答"你要做什么"：

| 你要做什么 | 去这里 |
|---|---|
| 先弄明白它是什么、能做什么 | [NCMapper 是什么](getting-started/overview.md) |
| 第一次使用，学会基本映射 | [快速开始](getting-started/quickstart.md) |
| 用链式声明替代特性标注（Fluent 通道） | [Fluent 入口](how-to/fluent/basics.md) |
| 解决一个具体问题（"怎么配置 X"） | [操作指南](how-to/fluent/basics.md)（任务导向，Fluent 入口） |
| 查 API、配置、诊断码事实 | [API 参考](reference/index.md) · [诊断码](diagnostics/rules.md) |
| 理解原理与设计决策 | [原理与机制](explanation/dual-engine.md) |
| 其他映射库迁入 | [从其他映射库迁移](getting-started/migrating-from-other-mappers.md) |

## 四象限入口

| 象限 | 目录 | 内容 |
|---|---|---|
| Tutorial 教程 | [getting-started/](getting-started/overview.md) · [tutorials/](tutorials/compose-tutorial.md) | 15 分钟上手、场景演练（Compose / ORM 投影 / 深拷贝） |
| How-to 操作指南 | [how-to/](how-to/fluent/basics.md) | 任务导向文章（与 samples 场景映射对齐；Fluent 入口为主线） |
| Reference 参考 | [reference/](reference/index.md) · [diagnostics/](diagnostics/rules.md) | 核心 API、特性系统、诊断码索引、配置 |
| Explanation 解释 | [explanation/](explanation/dual-engine.md) | 双引擎架构、ORM 降级机制、反向映射语义、深拷贝体系 |

## 高频直达

- [15 分钟上手教程](getting-started/tutorial-15min.md) — 实体/DTO 定义到校验的完整演练
- [诊断码全索引](diagnostics/rules.md) — NCMAP 诊断码速查
- [全局映射配置](how-to/infrastructure/global-config.md) — 全局默认值、未映射策略
- [性能与最佳实践](how-to/infrastructure/performance.md) — 引擎对比、热路径优化
- [AOT / NativeAOT 部署](how-to/aot/deployment.md) — 发布模式与诊断码对照

## P2 内容新篇

| 篇目 | 定位 |
|------|------|
| [15 分钟上手教程](getting-started/tutorial-15min.md) | 从定义实体到校验的端到端演练（DFM 引用 samples 多域示例与共享模型） |
| [多源合成场景演练](tutorials/compose-tutorial.md) | 订单+客户+商品合成聚合 DTO 的完整流程 |
| [ORM 投影场景演练](tutorials/orm-projection-tutorial.md) | 列表页一条 SQL、分页与降级行为 |
| [深拷贝场景演练](tutorials/deepcopy-tutorial.md) | 编辑快照、三策略选择与深度限制 |
| [双引擎架构](explanation/dual-engine.md) | 为什么双引擎、选择路径与一致性保证 |
| [ORM 降级机制](explanation/orm-degradation.md) | 触发条件、降级后行为与避免建议 |
| [反向映射语义](explanation/reverse-mapping-semantics.md) | 反向方向、转换器处理（NCMAP033/034）与 null 写回 |
| [映射配置所有权三原则](explanation/semantics/mapping-config-ownership.md) | 无锚不消费、编译期诊断与承载侧登记面 |

## 运行示例

全部示例代码位于 `samples/NCMapper.Samples/`，与 how-to 篇目对齐（映射关系见 [samples 场景映射表](reference/samples-index.md)）：

```bash
cd samples/NCMapper.Samples
dotnet run --scenario 201   # 运行指定场景
dotnet run --all           # 运行全部场景
```
