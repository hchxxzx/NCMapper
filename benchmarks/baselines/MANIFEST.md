# NCMapper 性能基线资产清单（net10）

> **基线资产治理产物** — 2026-08-09 入库
>
> 本目录为**正式基线资产**（治理纪律：产物入库 + commit 记录 + 版本策略）。
> 来源：`BenchmarkDotNet.Artifacts/results/` 45 类 × report-github.md 摘要（html/log 不入库）。

## 性能专项基线（2026-09-20）

> 与下方竞品对比基线（Windows 12700K，45 类）**不同机不同协议，二者不互比**；
> 本节为性能专项基准资产，各条目"前后对比达标"验收均以本节为锚。

| 项 | 值 |
|----|------|
| src 基线 commit | **`0434ffc`**（归档冻结点；本轮零 src 改动，生成产物字节门 8 项 IDENTICAL 复验通过） |
| bench 载体 commit | **`fac0acb`**（基线资产化——运行期 6 夹具 + 编译期 8 夹具 + 探针守门 + 字节门复验） |
| 运行日期 | 2026-09-20（三轮：R1/R2 迭代数 5 噪声校准 → R3 正式基线） |
| 框架 | net10.0（.NET 10.0.12，SDK 10.0.401） |
| 机器 | Linux Ubuntu 24.04.4，Intel Xeon Silver 4314（40 逻辑核） |
| BDN 配置 | BenchmarkDotNet 0.14.0，**IterationCount=15 / LaunchCount=1 / WarmupCount=3**（迭代数由 5 上调至 15 以压低噪声） |
| 判定口径 | **主指标 = Allocated（跨轮逐字节全等，确定性）**；耗时阈值 1.05× |
| 运行入口 | `dotnet run -c Release -f net10.0 --project benchmarks/NCMapper.Benchmarks --no-build -- sourcegen-driver` / `sourcegen-fixtures` / `RuntimeHotPathBench` |
| 探针守门 | GlobalSetup 空产物抛错 + 生成文本内容锚（防假基准，SourceGenDriverBench.cs:92-97 先例）；C-13 夹具含 MapList 预填断言（防 ClearTreeChildrenSlot 空转） |

### 编译期基线（SourceGenDriverBench 族；探针 = 生成文本 UTF-8 字节数）

| 夹具 | 性能条目 | Mean | Error | Allocated | 探针字节 |
|------|---------|------:|------:|----------:|---------:|
| SourceGenDriverBench（既有单对） | B1-09 | 2.123 ms | ±0.174 ms | 803.52 KB | 29,355 |
| MapListShape | B1-10 | 11.28 ms | ±0.238 ms | 4.89 MB | 122,871 |
| MutualPairs3 | B1-11 | 14.81 ms | ±0.213 ms | 6.21 MB | 57,238 |
| WideProperties | B3-11 | 37.86 ms | ±0.990 ms | 16.4 MB | 267,332 |
| DiagScale | B3-13+B4-11 / B4-16 / B4-17 | 17.03 ms | ±0.369 ms | 7.46 MB | 209,580 |
| RepeatedDeclarations3 | B2-14（含 B2-15/B2-16 面） | 7.005 ms | ±0.234 ms | 3.02 MB | 101,087 |
| SelfRecursiveInline | B1-21 | 2.451 ms | ±0.035 ms | 1.14 MB | 44,343 |
| DeepCopyGraphShared | B1-22 | 11.41 ms | ±0.219 ms | 5.06 MB | 132,217 |
| FluentChainSubclass | B2-15 / B2-16 / B3-12 | 7.470 ms | ±0.398 ms | 3.15 MB | 115,779 |

### 运行期基线（RuntimeHotPathBench）

| 夹具 | 性能条目 | Mean | Error | Allocated |
|------|---------|------:|------:|----------:|
| MapPipelinePostMap | A1-05 | 65.03 ns | ±0.674 ns | 32 B |
| ConvertValueBulk | A1-10 | 1.404 ms | ±0.013 ms | 235,451 B |
| CollectionCopy | A3-16 | 794.46 µs | ±6.25 µs | 317,272 B |
| FastPathAcyclic | A3-17 | 363.64 ns | ±3.55 ns | 120 B |
| DictValueMapperMixed | A3-18 | 56.34 µs | ±0.50 µs | 46,888 B |
| ClearTreeChildren | C-13 | 398.19 µs | ±3.65 µs | 462,152 B |

### 本节版本策略

1. 每个性能条目落码后，同夹具前后对比（同机同 SDK 同 commit 链）；
2. 本节夹具改动（含改名）须登记并同步本节；
3. `Directory.Packages.props` 依赖变更按下方竞品对比基线的版本策略执行（本节基线仅加工程引用，零包变更，未触发）。

---

## per-iteration 冷路径口径（2026-09-23）

> **与上方热路径基线协议不同，禁止互比**（协议不同不互比）。
> 机制：CWT 缓存族（B3-11 AttrSymbolCache / B3-12 TypeRefFromSymbol / B2-14 FindProperty）在
> 热口径（同 compilation 跨迭代）下全部命中 = 热路径；本节冷口径 = **每 iteration 重建
> `CSharpCompilation`**（symbol 实例全新 → 符号键控 CWT 全 miss），对齐热口径的
> "per-iteration 新 compilation 协议"与 "单 run 内去重"目标口径（进程热 + 新 compilation，
> 已排除纯进程隔离形态——JIT 冷启动会混入）。

| 项 | 值 |
|----|------|
| 载体 | `benchmarks/NCMapper.Benchmarks/SourceGenDriverColdBench.cs`（9 平行冷夹具；源文本经反射从热夹具收割，零复制） |
| 运行入口 | `dotnet run -c Release -f net10.0 --no-build --project benchmarks/NCMapper.Benchmarks -- sourcegen-cold [夹具名过滤]` |
| BDN 配置 | `Cold` job：LaunchCount=1 / WarmupCount=3 / IterationCount=15（对齐热口径 :21）+ **IterationSetup 重建 compilation**；探针守门用独立 probe compilation（symbol 与 workload 隔离，不热化 Benchmark） |
| 运行日期 | 2026-09-23（Linux 40 核，dotnet 10.0.401；两轮复现） |
| 判定口径 | **主判据 = Allocated（两轮逐字节全等，确定性）——9/9 夹具两轮全等实测**；耗时阈值仅对两轮偏差 <8% 的 7 夹具设立（上限校准法 max(R1,R2)×1.10）；DiagScaleCold（12.57%）/ DeepCopyGraphSharedCold（9.03%）两轮偏差超判据 → **耗时仅登记不设门** |
| 对账脚本 | `scripts/cold-bench-threshold-check.py`（Allocated 精确相等 + GATE 夹具耗时阈值；`--inject` 模式供双边界自证） |

### 冷路径基线（RunGeneratorDriverCold，net10.0，两轮）

| 夹具 | R1 Mean (ms) | R2 Mean (ms) | 两轮偏差 | R1/R2 Allocated | 阈值（Allocated） | 耗时阈值 |
|------|-------------:|-------------:|---------:|-----------------|-------------------|---------:|
| SourceGenDriverColdBench | 52.77 | 51.91 | 1.64% | 5.47 MB = 5.47 MB | 精确相等 | ≥58.0 ms 告警 |
| MapListShapeCold | 127.50 | 125.40 | 1.66% | 11.62 MB = 11.62 MB | 精确相等 | ≥140.3 ms 告警 |
| MutualPairs3Cold | 69.94 | 68.93 | 1.45% | 6.42 MB = 6.42 MB | 精确相等 | ≥76.9 ms 告警 |
| WidePropertiesCold | 174.60 | 179.50 | 2.77% | 11.68 MB = 11.68 MB | 精确相等 | ≥197.4 ms 告警 |
| RepeatedDeclarations3Cold | 74.63 | 74.90 | 0.36% | 5.97 MB = 5.97 MB | 精确相等 | ≥82.4 ms 告警 |
| SelfRecursiveInlineCold | 52.36 | 51.78 | 1.11% | 5.83 MB = 5.83 MB | 精确相等 | ≥57.6 ms 告警 |
| FluentChainSubclassCold | 69.11 | 65.79 | 4.92% | 6.48 MB = 6.48 MB | 精确相等 | ≥76.0 ms 告警 |
| DiagScaleCold | 79.69 | 90.38 | **12.57%** | 8.18 MB = 8.18 MB | 精确相等 | **不设门**（两轮偏差超判据，仅登记） |
| DeepCopyGraphSharedCold | 69.45 | 63.45 | **9.03%** | 6.88 MB = 6.88 MB | 精确相等 | **不设门**（两轮偏差超判据，仅登记） |

探针字节（GlobalSetup，与热口径逐字节一致）：29,355 / 122,871 / 57,238 / 267,332 / 209,580 / 101,087 / 44,343 / 132,217 / 115,779。

> 本节为**新增节**：上方热基线的数字零改动；本节数字 = 冷口径首轮两轮基线，
> 后续 per-iteration 口径回归以本节为锚（同机同 SDK 同 commit 链复跑）。

---

## 运行信息（单次运行，2026-08-08）

| 项 | 值 |
|----|------|
| 运行 commit | **d3cafa6**（复核基线） |
| 运行日期 | 2026-08-08 12:05（产物时间戳） |
| 框架 | net10.0（.NET 10.0.10，SDK 10.0.302） |
| 协议 | baseline_self（自基线协议） |
| BDN 配置 | BenchmarkDotNet 0.14.0，IterationCount=15 / LaunchCount=5 / WarmupCount=3 |

## 环境声明

| 项 | 值 |
|----|------|
| CPU | 12th Gen Intel Core i7-12700K（1 CPU / 20 逻辑核 / 12 物理核，P-core 锁定 0xFFFF） |
| OS | Windows 10 1809（10.0.17763.9020） |
| GC | **ServerGC**（基准项目配置；Workstation 对照用 DOTNET_gcServer=0） |
| JIT | X64 RyuJIT AVX2 |
| 数据口径 | Mean / Error / StdDev / Gen0 / Allocated（BDN 标准输出） |

## 清单（45 类）

| # | 基准类 | 文件 |
|---|--------|------|
| 1 | Action_Ext | `net10/Action_Ext.md` |
| 2 | Action_NCMapper | `net10/Action_NCMapper.md` |
| 3 | Action_Runtime | `net10/Action_Runtime.md` |
| 4 | AutoFlatten_Ext | `net10/AutoFlatten_Ext.md` |
| 5 | AutoFlatten_NCMapper | `net10/AutoFlatten_NCMapper.md` |
| 6 | AutoFlatten_Runtime | `net10/AutoFlatten_Runtime.md` |
| 7 | AutoFlattenBig_NCMapper | `net10/AutoFlattenBig_NCMapper.md` |
| 8 | BidirDE_NCMapper | `net10/BidirDE_NCMapper.md` |
| 9 | BidirDE_Runtime | `net10/BidirDE_Runtime.md` |
| 10 | BidirED_Ext | `net10/BidirED_Ext.md` |
| 11 | BidirED_NCMapper | `net10/BidirED_NCMapper.md` |
| 12 | BidirED_Runtime | `net10/BidirED_Runtime.md` |
| 13 | Collection_Ext | `net10/Collection_Ext.md` |
| 14 | Collection_NCMapper | `net10/Collection_NCMapper.md` |
| 15 | Collection_Runtime | `net10/Collection_Runtime.md` |
| 16 | ComposeMultiSource_NCMapper | `net10/ComposeMultiSource_NCMapper.md` |
| 17 | DeepCopy_Expression | `net10/DeepCopy_Expression.md` |
| 18 | DeepCopy_Generated | `net10/DeepCopy_Generated.md` |
| 19 | DeepCopy_Manual | `net10/DeepCopy_Manual.md` |
| 20 | DeepCopy_Runtime | `net10/DeepCopy_Runtime.md` |
| 21 | EnumString_Ext | `net10/EnumString_Ext.md` |
| 22 | EnumString_NCMapper | `net10/EnumString_NCMapper.md` |
| 23 | EnumString_Runtime | `net10/EnumString_Runtime.md` |
| 24 | Flat_Ext | `net10/Flat_Ext.md` |
| 25 | Flat_NCMapper | `net10/Flat_NCMapper.md` |
| 26 | Flat_Runtime | `net10/Flat_Runtime.md` |
| 27 | NameDiff_Ext | `net10/NameDiff_Ext.md` |
| 28 | NameDiff_NCMapper | `net10/NameDiff_NCMapper.md` |
| 29 | NameDiff_Runtime | `net10/NameDiff_Runtime.md` |
| 30 | NameDiffBig_Ext | `net10/NameDiffBig_Ext.md` |
| 31 | NameDiffBig_NCMapper | `net10/NameDiffBig_NCMapper.md` |
| 32 | Nested_Ext | `net10/Nested_Ext.md` |
| 33 | Nested_NCMapper | `net10/Nested_NCMapper.md` |
| 34 | Nested_Runtime | `net10/Nested_Runtime.md` |
| 35 | Nullable_Ext | `net10/Nullable_Ext.md` |
| 36 | Nullable_NCMapper | `net10/Nullable_NCMapper.md` |
| 37 | Nullable_Runtime | `net10/Nullable_Runtime.md` |
| 38 | Record_Ext | `net10/Record_Ext.md` |
| 39 | Record_NCMapper | `net10/Record_NCMapper.md` |
| 40 | Record_Runtime | `net10/Record_Runtime.md` |
| 41 | RecordBig_Ext | `net10/RecordBig_Ext.md` |
| 42 | RecordBig_NCMapper | `net10/RecordBig_NCMapper.md` |
| 43 | Update_Ext | `net10/Update_Ext.md` |
| 44 | Update_NCMapper | `net10/Update_NCMapper.md` |
| 45 | Update_Runtime | `net10/Update_Runtime.md` |

## 版本策略（2026-08-09）

1. **触发重跑**：`Directory.Packages.props` 依赖变更（含 MessagePack/Dapper/FreeSql/EFCore 等）→ 本基线失效，须按同协议重跑 45 类并更新清单；
2. **commit 记录**：每次基线更新必须同步更新本清单的运行 commit / 日期字段；
3. **环境声明**：CPU / GC 形态 / OS / .NET 版本变更必须重跑；
4. **历史资产**：历史 37 NA 报告仅归档标注，不物理删除。

## 历史基线归档说明

- 历史 47 报告（08-06 采集）中 37 个 NA 因 `RestoreLockedMode=true` 与 BDN RID 不匹配构建失败——已归档标注，不迁移入本目录；
- 本目录为**唯一正式基线来源**（替代历史资产）。

## 跨 TFM 抽测记录（2026-08-09）

> 抽测性质：非正式基线（BL-1 盲区闭合），单次运行，仅判断"跨 TFM 是否存在显著性能差异"。
> 同机对照：net8/net9 与 net10 正式基线同环境（12700K/ServerGC/P-core 锁定）。

| 基准 | net8 | net9 | net10（正式基线） | 结论 |
|------|------|------|-------------------|------|
| Flat_NCMapper | 14.10 ns | 13.56 ns | 14.16 ns | 三框架一致（差异 <5%），无跨 TFM 显著差异 |
| DeepCopy_Generated | 101.0 ns | — | 99.7 ns | net8 vs net10 一致（+1.3%），无显著差异 |

**结论（BL-1 闭合）**：抽样基准跨 TFM（net8/net9/net10）无显著性能差异，**不值得建立正式跨 TFM 基线**（如未来出现 TFM 分叉再评估）。
