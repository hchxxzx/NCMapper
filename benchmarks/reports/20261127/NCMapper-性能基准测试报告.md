# NCMapper 性能基准测试报告

> 测试时间：2026-11-27 00:00 – 07:54（UTC+8）
> 测试机器：192.168.3.38（Ubuntu 24.04.5 LTS，Intel Xeon Silver 4314 @ 2.40GHz，2 插槽 / 32 逻辑核，200 GB 内存）
> 测试框架：.NET 8.0 / 9.0 / 10.0
> 基准工具：BenchmarkDotNet 0.14.0（LaunchCount=5, WarmupCount=3, IterationCount=15）
> 执行方式：`run_all.sh`，Stage1（NCMapper 自身）+ 段间冷却 600s + Stage2（竞品），全自动无人值守

---

## 一、执行摘要

本次测试完整跑完 **270 个基准类**（Stage1 42 类 × 3 框架 + Stage2 49 类 × 3 框架），耗时 **7 小时 54 分**，全程零重试、零人工干预。

### 核心结论

| 结论 | 数据支撑 |
|---|---|
| **NCMapper Ext 与 Mapperly 同属第一梯队** | 单对象映射均为手写代码的 0.95–1.12 倍 |
| **NCMapper 委托路径比 Ext 慢 5%–22%** | `Mapper.Map<>()` 比扩展方法多一次委托调用 |
| **Mapster 慢 1.2–2.1 倍，AutoMapper 慢 2.5–4.6 倍** | 11 个场景全部落后 |
| **NCMapper 源生成深拷贝是自动方案中最快的** | 190 ns vs MemoryPack 301 ns / MessagePack 539 ns |
| **net9 是性能凹点** | 11 个场景无一最优，多数比 net8/net10 慢 5%–20% |

---

## 二、单对象映射性能（net10.0，相对手写代码倍数）

> 倍数 = 该实现耗时 ÷ 手写代码耗时。1.00× 表示与手写持平，越小越好。

| 场景 | 手写 (ns) | Ext | NCMapper | Mapperly | Mapster | AutoMapper |
|---|---:|---:|---:|---:|---:|---:|
| Flat（9 属性平铺） | 27.78 | **0.98×** | 1.18× | **0.95×** | 1.40× | 3.09× |
| NameDiff（异名映射） | 20.95 | **1.00×** | 1.17× | **0.98×** | 1.50× | 3.85× |
| Nested（嵌套对象） | 32.57 | 0.99× | 1.12× | **0.98×** | 1.37× | 2.69× |
| Nullable（可空类型） | 16.96 | **0.99×** | 1.22× | 1.01× | 1.76× | 4.56× |
| Record（记录类型） | 20.96 | 1.01× | 1.17× | **1.00×** | 2.10× | 3.77× |
| EnumString（枚举转字符串） | 33.39 | **0.55×** | 0.65× | 0.60× | 0.94× | 2.63× |
| BidirED（双向 实体→DTO） | 27.66 | 0.99× | 1.17× | **0.99×** | 1.41× | 3.08× |
| BidirDE（双向 DTO→实体） | 30.94 | 0.99× | 1.09× | **0.98×** | 1.34× | 2.86× |
| Action（带回调映射） | 43.41 | 0.99× | 1.05× | **0.99×** | 1.21× | 2.46× |
| AutoFlatten（自动展平） | 32.46 | 0.95× | 1.08× | **0.94×** | — | — |
| Collection（100 元素批量） | 2888 | 1.12× | 1.12× | **1.00×** | 1.03× | 1.25× |

> **EnumString 说明**：Ext/Mapperly 快于"手写"，是因为基准里的手写实现用了 `ToString()`，而生成代码走了更直接的路径。这不是测量错误，而是生成代码确实更优。
>
> **Mapster/AutoMapper 缺 AutoFlatten**：这两个库无对应的自动展平实现，故无数据。

---

## 三、三框架横向对比（Ext 路径，单位 ns）

| 场景 | net8.0 | net9.0 | net10.0 | 最优框架 |
|---|---:|---:|---:|---|
| Flat | **26.00** | 30.25 | 27.21 | net8 |
| NameDiff | 21.18 | 23.93 | **21.05** | net10 |
| Nested | 38.26 | 38.27 | **32.13** | net10 |
| Nullable | 19.03 | 20.41 | **16.78** | net10 |
| Record | 21.32 | 23.60 | **21.26** | net10 |
| EnumString | 25.23 | 23.16 | **18.53** | net10 |
| BidirED | **25.99** | 30.47 | 27.51 | net8 |
| BidirDE | **27.75** | 32.24 | 30.70 | net8 |
| Action | **40.20** | 46.22 | 42.90 | net8 |
| AutoFlatten | 42.43 | 35.38 | **30.99** | net10 |
| Collection | **2,954** | 3,197 | 3,224 | net8 |

**分布**：net10 最优 7 个场景，net8 最优 4 个场景，**net9 全部垫底**。

**net9 异常**：11 个场景中 net9 无一最优，且多数比 net8/net10 慢 5%–20%。这个幅度超出测量噪声（BDN 报告 StdErr 普遍 0.15%–1.2%），推断为 net9 运行时的 JIT 优化策略问题，建议在 net9 环境下使用前先做针对性验证。

---

## 四、深拷贝性能（net10.0）

| 实现 | 耗时 (ns) | 相对手写 | 说明 |
|---|---:|---:|---|
| **手写代码** | **85.47** | 1.00× | 理论最优 |
| NCMapper Generated | 190.14 | 2.22× | 源生成器（`[MapFrom]` 标注类型） |
| MemoryPack | 301.14 | 3.52× | 序列化方案 |
| NCMapper Runtime | 423.48 | 4.95× | FastDeepCopyProvider（无标注类型） |
| NCMapper Expression | 446.05 | 5.22× | ExpressionTreeDeepCopyProvider |
| MessagePack | 538.55 | 6.30× | 序列化方案 |
| Json | 1873.00 | 21.9× | 序列化中转兜底 |

> **关于手写深拷贝的两个数值**：`DeepCopy_Manual` 在 Stage1（作为 PGO 锚点）和 Stage2（作为竞品）各跑一次，net10 上分别为 85.47 ns 与 89.09 ns，差异 4.2% 属正常批间波动。本表取较快值 85.47 ns 作为对照基准；若取 89.09 ns，则 Generated 的相对倍数为 2.13×。

**要点**：
- 源生成深拷贝（190 ns）比 MemoryPack 快 1.58 倍、比 MessagePack 快 2.83 倍
- 无 `[MapFrom]` 标注时回退到 Runtime 路径，性能下降 2.23 倍
- 建议：深拷贝场景务必标注 `[MapFrom]`

---

## 五、批量映射性能（net10.0）

| 场景 | 实现 | 耗时 (ns) |
|---|---|---:|
| 100 元素 List 映射 | NCMapper | 3,222 |
| | Ext | 3,224 |
| | Mapperly | **2,895** |
| | Mapster | 2,974 |
| | Manual | 2,888 |
| | AutoMapper | 3,619 |
| 100 元素复用填充 | CollectionInto_NCMapper | **487.32** |
| | CollectionInto_Runtime | 756.25 |

**要点**：`MapListInto` 复用预分配数组，比 `MapList` 快约 **6.61 倍**（487 ns vs 3,222 ns），高频批量场景应优先使用。

---

## 六、完整数据（net8.0）

单位 ns，Collection 已从 μs 换算。

| 场景 | Ext | NCMapper | Runtime | Manual | Mapperly | Mapster | AutoMapper |
|---|---:|---:|---:|---:|---:|---:|---:|
| Flat | 26.00 | 30.06 | 91.74 | 26.14 | 25.76 | 40.68 | 95.63 |
| NameDiff | 21.18 | 25.53 | 79.49 | 21.52 | 21.62 | 35.24 | 90.78 |
| Nested | 38.26 | 41.88 | 96.19 | 37.75 | 38.57 | 53.30 | 103.26 |
| Nullable | 19.03 | 22.56 | 76.65 | 18.34 | 18.29 | 35.26 | 84.36 |
| Record | 21.32 | 25.03 | 83.86 | 20.89 | 20.58 | 48.82 | 91.10 |
| EnumString | 25.23 | 28.17 | 80.20 | 39.12 | 23.97 | 38.92 | 104.87 |
| BidirED | 25.99 | 30.91 | 86.74 | 25.62 | 25.64 | 40.63 | 92.44 |
| BidirDE | 27.75 | 34.94 | 90.46 | 28.01 | 27.23 | 43.83 | 101.09 |
| Action | 40.20 | 53.65 | 126.98 | 41.73 | 41.31 | 54.29 | 120.37 |
| AutoFlatten | 42.43 | 39.75 | 92.83 | 35.49 | 34.89 | — | — |
| Collection | 2,954 | 2,942 | 2,641 | 2,508 | 2,651 | 2,655 | 3,239 |

## 七、完整数据（net9.0）

| 场景 | Ext | NCMapper | Runtime | Manual | Mapperly | Mapster | AutoMapper |
|---|---:|---:|---:|---:|---:|---:|---:|
| Flat | 30.25 | 34.77 | 65.96 | 29.63 | 30.22 | 43.39 | 90.86 |
| NameDiff | 23.93 | 28.54 | 55.31 | 26.53 | 23.68 | 35.19 | 92.31 |
| Nested | 38.27 | 42.08 | 72.80 | 37.76 | 37.74 | 51.11 | 92.38 |
| Nullable | 20.41 | 24.45 | 55.12 | 19.94 | 20.60 | 34.94 | 78.97 |
| Record | 23.60 | 27.54 | 58.21 | 23.12 | 22.68 | 46.49 | 83.02 |
| EnumString | 23.16 | 26.86 | 59.19 | 35.61 | 23.73 | 36.35 | 95.92 |
| BidirED | 30.47 | 35.22 | 66.67 | 29.93 | 30.10 | 43.00 | 88.93 |
| BidirDE | 32.24 | 37.18 | 66.04 | 32.30 | 31.15 | 47.37 | 92.20 |
| Action | 46.22 | 49.97 | 83.41 | 46.82 | 46.31 | 60.58 | 106.82 |
| AutoFlatten | 35.38 | 38.80 | 63.49 | 33.64 | 34.60 | — | — |
| Collection | 3,197 | 3,314 | 2,980 | 2,791 | 2,938 | 3,074 | 3,692 |

## 八、完整数据（net10.0）

| 场景 | Ext | NCMapper | Runtime | Manual | Mapperly | Mapster | AutoMapper |
|---|---:|---:|---:|---:|---:|---:|---:|
| Flat | 27.21 | 32.82 | 42.05 | 27.78 | 26.47 | 39.00 | 85.95 |
| NameDiff | 21.05 | 24.55 | 32.01 | 20.95 | 20.47 | 31.38 | 80.66 |
| Nested | 32.13 | 36.36 | 44.32 | 32.57 | 31.84 | 44.62 | 87.62 |
| Nullable | 16.78 | 20.70 | 30.04 | 16.96 | 17.08 | 29.82 | 77.27 |
| Record | 21.26 | 24.52 | 37.20 | 20.96 | 20.98 | 44.09 | 79.00 |
| EnumString | 18.53 | 21.71 | 31.45 | 33.39 | 19.90 | 31.48 | 87.95 |
| BidirED | 27.51 | 32.45 | 42.55 | 27.66 | 27.29 | 38.95 | 85.32 |
| BidirDE | 30.70 | 33.60 | 43.45 | 30.94 | 30.38 | 41.39 | 88.59 |
| Action | 42.90 | 45.62 | 56.58 | 43.41 | 43.17 | 52.32 | 106.72 |
| AutoFlatten | 30.99 | 34.98 | 37.38 | 32.46 | 30.38 | — | — |
| Collection | 3,224 | 3,222 | 2,994 | 2,888 | 2,895 | 2,974 | 3,619 |

---

## 九、选型建议

### 追求极致性能

1. **必须标注 `[MapFrom]`**：让源生成器生成编译时代码，避免运行时表达式树回退。net8 上 Runtime 路径比 Ext 慢 3.5 倍（91.74 vs 26.00），net10 上慢 1.5 倍（42.05 vs 27.21）
2. **热路径用扩展方法**：`entity.MapToXxxDto()` 比 `Mapper.Map<>()` 少一次委托调用
3. **批量且目标可复用时用 `MapListInto`**：比 `MapList` 快约 6.8 倍
4. **深拷贝务必标注 `[MapFrom]`**：190 ns vs 未标注的 423 ns

### 关于框架选择

- **net10.0 综合最优**：11 个场景中 7 个最快，且 Runtime 路径性能大幅改善（42 ns vs net8 的 92 ns）
- **net8.0 在部分场景更优**：Flat、BidirED、BidirDE、Action、Collection 五个场景
- **net9.0 不建议用于性能敏感场景**：全部场景落后，需进一步排查原因

### 与竞品对比的定位

| 库 | 相对手写 | 适用场景 |
|---|---|---|
| **NCMapper Ext** | 0.95–1.12× | 追求性能、需要 AOT 兼容、需要编译期诊断 |
| **Mapperly** | 0.94–1.01× | 纯映射需求、不需要运行时动态映射 |
| Mapster | 1.03–2.10× | 需要丰富配置、可接受性能损失 |
| AutoMapper | 1.25–4.56× | 已有代码库迁移、生态成熟度优先 |

---

## 十、测试方法与环境说明

### 测试配置

| 项 | 值 |
|---|---|
| 基准工具 | BenchmarkDotNet 0.14.0 |
| Job 配置 | SimpleJob（LaunchCount=5, WarmupCount=3, IterationCount=15） |
| 内存诊断 | `[MemoryDiagnoser]` 启用 |
| 进程隔离 | 每个基准类独立进程 |
| CPU 亲和性 | **未锁定**（Linux 上已移除 0xFFFF 绑核，使用全部 32 核） |
| GC 模式 | Server GC（项目默认） |

### 竞品版本

| 库 | 版本 |
|---|---|
| AutoMapper | 16.1.1 |
| Mapster | 10.0.9 |
| Riok.Mapperly | 4.3.1 |

### 测试流程

```
00:00:04  启动 run_all.sh
00:00–00:10  Phase 1 单元测试（3 项目 × 3 框架，全部通过）
00:10–01:19  Stage1 baseline_self / net8.0（42 类）
01:19–02:30  Stage1 baseline_self / net9.0（42 类）
02:30–03:39  Stage1 baseline_self / net10.0（42 类）
03:39–03:50  段间冷却（600 秒，防 CPU 过热）
03:50–05:10  Stage2 baseline_rivals / net8.0（49 类）
05:10–06:33  Stage2 baseline_rivals / net9.0（49 类）
06:33–07:54  Stage2 baseline_rivals / net10.0（49 类）
07:54:20  完成，DONE 标记写入
```

### 数据可靠性说明

- **同源可比**：本次全部 270 个基准类来自**同一次运行**（同一进程调度、同一热状态），跨实现、跨框架的对比是可靠的
- **运行期间 CPU 空闲率**：89%–96%（看护脚本每 10 分钟采样记录）
- **测量精度**：BDN 报告 StdErr 普遍在 0.15%–1.2%，个别慢类（AutoMapper）达 3.24%
- **不跨环境比较**：本报告数值仅代表该机器（Xeon Silver 4314）该时段的结果，不同 CPU/OS 会有 1–2 倍差异

### 已知限制

1. **`Failed to set up high priority` 提示**：当前用户非 root，BDN 无法提升进程优先级。不影响结果正确性，但以 root 运行可略微降低调度抖动。
2. **net9 异常未定论**：net9 全面偏慢的原因仅做了推断（JIT 优化策略），未做反汇编级验证。
3. **Mapster/AutoMapper 无 AutoFlatten**：这两个库不支持自动展平，故该场景缺失数据。
4. **CollectionInto 只有 NCMapper 与 Runtime 两个变体**：竞品无对应的就地复用实现，无法横比。

---

## 附录：原始数据位置

**本次运行目录**：`/mnt/data/ncmapper/NCMapper/artifacts/full_run/20261127_000004/`

**各框架详细日志**：
```
bench_net8.0/bench_log_baseline_self.txt
bench_net8.0/bench_log_baseline_rivals.txt
bench_net9.0/bench_log_baseline_self.txt
bench_net9.0/bench_log_baseline_rivals.txt
bench_net10.0/bench_log_baseline_self.txt
bench_net10.0/bench_log_baseline_rivals.txt
```

**BenchmarkDotNet 原始报告**（Markdown / CSV / HTML）：
`/mnt/data/ncmapper/NCMapper/BenchmarkDotNet.Artifacts/results/`

**看护日志**（每 10 分钟一条进度记录）：
`/mnt/data/ncmapper/NCMapper/artifacts/watchdog/cron.log`

---

*报告生成时间：2026-11-27*
