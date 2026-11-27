# 性能测试报告归档

本目录存放**历次完整跑批**的测试报告与原始数据。

与 `benchmarks/baselines/` 的区别：

| 目录 | 内容 | 用途 |
|---|---|---|
| `baselines/` | 单类 BenchmarkDotNet 原始报告（每类一个 md） | 长期基线资产，供回归对比 |
| `reports/` | **整轮跑批的汇总报告 + 原始数据 CSV** | 阶段性存档，供版本间对比、对外汇报 |

---

## 20261127 — 三框架完整跑批

### 测试概要

| 项 | 值 |
|---|---|
| 测试时间 | 2026-11-27 00:00 – 07:54（UTC+8） |
| 耗时 | 7 小时 54 分 |
| 完成规模 | 270 个基准类（Stage1 42×3 + Stage2 49×3） |
| 框架 | .NET 8.0 / 9.0 / 10.0 |
| 执行方式 | `run_all.sh` 全自动无人值守，零重试 |
| 测试机器 | 192.168.3.38（Ubuntu 24.04.5，Xeon Silver 4314，32 逻辑核，200GB） |

### 文件清单

| 文件 | 说明 |
|---|---|
| `NCMapper-性能基准测试报告.md` | 完整报告：执行摘要、对比矩阵、选型建议、方法与限制 |
| `raw_results.csv` | 273 条原始数据（framework, class, mean_ns） |

### 数据来源

服务器运行目录：`/mnt/data/ncmapper/NCMapper/artifacts/full_run/20261127_000004/`

原始 BenchmarkDotNet 报告（Markdown / CSV / HTML）：
`/mnt/data/ncmapper/NCMapper/BenchmarkDotNet.Artifacts/results/`

### 核心结论

| 实现 | 相对手写代码（net10.0） |
|---|---|
| Mapperly | 0.94–1.01× |
| **NCMapper Ext** | **0.95–1.12×** |
| NCMapper 委托路径 | 1.05–1.22× |
| Mapster | 1.03–2.10× |
| AutoMapper | 1.25–4.56× |

深拷贝（net10.0）：手写 85.47 ns > **NCMapper Generated 190.14 ns** > MemoryPack 301.14 ns > NCMapper Runtime 423.48 ns > MessagePack 538.55 ns > Json 1873 ns。

### 注意事项

1. **本报告数值仅代表该机器该时段**，不同 CPU/OS 会有 1–2 倍差异，不可跨环境直接比较绝对值。
2. **同源可比**：本次全部 270 个基准类来自同一次运行，跨实现/跨框架的对比可靠。
3. **net9 全面偏慢**：11 个场景无一最优，原因未定论（推断为 JIT 优化策略），用于生产前建议专项验证。
4. **`DeepCopy_Manual` 有两条记录**：Stage1 作 PGO 锚点、Stage2 作竞品各跑一次，net10 上分别为 85.47 / 89.09 ns。

### 复现方式

```bash
cd benchmarks/scripts/run
./run_all.sh                    # 完整两段（self + 冷却 600s + rivals）
SKIP_TESTS=1 ./run_all.sh       # 跳过 Phase 1 单元测试
BENCH_COOLDOWN=300 ./run_all.sh # 自定义段间冷却秒数
```

---

## 如何新增一份报告

1. 建目录 `reports/YYYYMMDD/`
2. 放入 `NCMapper-性能基准测试报告.md` 与 `raw_results.csv`
3. 在本 README 追加一节，写明测试概要、文件清单、核心结论、数据来源
4. 报告放这里而**不是** `docs/zh-CN/`——`docfx.zh.json` 会把 `docs/zh-CN/**/*.md` 全部发布到离线文档站点，一次性测试报告不该进站点
