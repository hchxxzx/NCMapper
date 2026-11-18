# benchmarks/scripts — 性能测试脚本目录

所有性能相关脚本集中于此，按用途分子目录管理。此前脚本散落在仓库根、`benchmarks/`、`benchmarks/NCMapper.Benchmarks/` 等多处，已于 2026-07-26 统一迁移到本目录。

## 目录结构

| 子目录 | 用途 | 语言 |
|--------|------|------|
| `run/` | 常规性能测试运行脚本（日常使用） | bash |
| `baseline/` | 性能基线构建 + HTML 报告生成 | bash + python |
| `compare/` | 结果对比分析（含 CI 门禁脚本与一次性历史分析） | python + ps1 |
| `diag/` | 诊断工具（反汇编 / GC 分析，Windows 专属） | PowerShell |
| `guard/` | 基线运行守护检查（定时轮询判定状态，供自动化调用） | bash |
| `legacy/` | 已删除（开源前清理，含过时机器路径引用；见下方说明） | — |

## 通用约定

**路径推导**：所有脚本从自身位置推导仓库根，既不依赖调用时的工作目录，也不硬编码机器路径。脚本位于 `benchmarks/scripts/<子目录>/`，向上三级即仓库根：

```bash
# bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
cd "$REPO_ROOT"
```

```powershell
# PowerShell
$RepoRoot = Resolve-Path "$PSScriptRoot/../../.."
```

> `run_bench.sh` 例外：它会 `cd` 到基准项目目录 `benchmarks/NCMapper.Benchmarks`，因为其内部 `dotnet build/run` 依赖项目目录作为工作目录。

**命名规范**：动词_对象，如 `run_all.sh`、`gen_report.py`、`compare_perf.ps1`。

**CPU 亲和**：大核锁定由基准程序 `Program.cs` 的 `Main` 入口集中保证（`ProcessorAffinity = 0xFFFF`，i7-12700K 逻辑核 0-15 = 8 P-core × 2HT；E-core 为 16-19），BDN 派生的测量子进程继承父进程亲和性。因此任何运行脚本/模式都自动锁定 P-core，脚本层无需重复设置。

## run/ — 运行脚本（三档）

性能测试收敛为**三档入口**，按改动规模选用。三者均为 `bench_lib.sh` 的薄封装（仅设模式 + 框架列表），稳定性逻辑（构建检查 / GUID 清理 / 每框架超时 / 断点续跑 / 日志）由 `bench_lib.sh` 统一提供。

| 档位 | 脚本 | 基准模式 | 框架 | 规模 | 用途 |
|------|------|----------|------|------|------|
| 档1 | `run_all.sh` | `baseline_self` → 冷却 600s → `baseline_rivals`（两阶段） | net8/9/10 | 36 + 46 类 × 3 | 长期积累完整测试（先 self 三框架 → 停止 10 分钟防过热降温 → 再 rivals 三框架，保证两段热状态一致可横比） |
| 档2 | `run_ncmapper_all.sh` | `ncmapper_all` | net8/9/10 | 43 类 × 3 | 大量修改后性能回归 |
| 档3 | `run_ncmapper_net10.sh` | `ncmapper_all` | net10 | 43 类 × 1 | 小规模修改后快速回归（约档2 三分之一耗时） |

> 档1 段间冷却：默认 600s（10 分钟，防过热降温保证两段热状态一致），可用环境变量 `BENCH_COOLDOWN` 覆盖；两阶段日志分别落盘 `bench_log_baseline_self.txt` / `bench_log_baseline_rivals.txt`。档2/3 的 `ncmapper_all` 于 2026-08-13 起排除 Big 场景，2026-08-20 实测 43 类（含 CollectionInto_NCMapper 纳入；此前注释 41/46 口径不一，已统一）。

用法（每框架超时秒数可选，默认 14400=4h；`SKIP_TESTS=1` 跳过 Phase 1 单元测试）：

```bash
./run_ncmapper_net10.sh            # 小改后快测
./run_ncmapper_all.sh              # 大改后回归
./run_all.sh                       # 完整测试（含竞品横比）
SKIP_TESTS=1 ./run_ncmapper_net10.sh   # 跳过单测，更快
```

**场景集差异**：档2/3 的 `ncmapper_all` 按后缀过滤，含 Array/Dictionary/Compose/Update 的 NCMapper 变体；档1 的 `baseline` 按设计排除这几类（无竞品实现，无法横比）。故档2/3 场景集是档1 自有部分的超集——回归求最大覆盖，横比求竞品齐全，二者有意不同。

| 辅助文件 | 说明 |
|----------|------|
| `bench_lib.sh` | 三档共享库（稳定性逻辑单一来源），由入口脚本 `source`，不直接执行 |
| `run_bench.sh` | ad-hoc 工具：单框架按类逐个跑（带超时/断点续跑/GUID 清理），用于单独调查某个基准类，非回归档位 |

## baseline/ — 基线构建

| 脚本 | 说明 |
|------|------|
| `run_baseline.sh` | 基线整轮构建（先 full 7 变体 + 后 self 3 变体）× REPEATS 取中位数 |
| `run_self_baseline.sh` | 后3（SELF 3 变体）专属基线构建，作为回归层1 基线 |
| `run_rivals_baseline.sh` | 同类组件变体基线构建，与 baseline_self 同协议可横比 |
| `gen_manifest.sh` | 生成基线场景清单（`artifacts/benchmarks/baseline_manifest.txt`） |
| `gen_report.py` | 读取基线运行目录的 CSV + meta.json，生成 HTML 报告 |

## compare/ — 对比分析

| 脚本 | 说明 |
|------|------|
| `compare_perf.ps1` | **CI 门禁**：对比基线与当前结果，回归超阈值返回非零退出码（被 `.github/workflows/perf-regression.yml` 调用） |
| `extract_medians.py` | 从结果目录提取各类中位数（参数化，可复用） |
| `compare_self.py` | 两份基线 baseline_self 阶段中位数对比 |
| `compare_rivals_vs_full7.py` | 同类组件 vs 前7 对比 |
| `compare_full_vs_self_absval.py` | full vs self 绝对数值对比 |
| `compare_standalone_vs_whole.py` | standalone-self vs whole-full 同名基准对比 |
| `compare_within_run.py` | 同一 run 内前7 vs 后3 差异量化 |
| `extract_self_three_rounds.py` | 后3 专属测试三轮原始数据 + 中位数 + 极差 |
| `gen_rivals_vs_full7_report.py` | 生成同类组件 vs 前7 HTML 报告 |
| `gen_report_net10_self_rivals.py` | 生成 net10 self vs rivals HTML 报告 |

> 注：除 `compare_perf.ps1`（CI 用）和 `extract_medians.py`（参数化）外，其余多为**一次性历史分析脚本**，内部硬编码了特定基线运行目录（如 `net10.0_20260707_192721`）与机器路径，绑定当时数据，仅作方法论参考保留，不再维护其可移植性。

## diag/ — 诊断工具（Windows）

| 脚本 | 说明 |
|------|------|
| `run_all_frameworks.ps1` | 全框架串行性能测试（支持 all/ncmapper/Quick/单场景模式） |
| `run_jit_disasm.ps1` | 用 DOTNET_JitDisasm 输出 Mapper.DeepCopy native asm，对比三框架 JIT 产物 |
| `run_disasm_compare.ps1` | DeepCopy_Expression 反汇编三框架对比 |
| `run_disasm_nopgo.ps1` | 关闭 PGO 重跑 net9/10 反汇编（验证 Dynamic PGO 影响） |
| `run_gcdiag.ps1` | DeepCopy GC 诊断 |
| `extract_mapper_deepcopy.ps1` | 从 jit_disasm 日志提取 Mapper.DeepCopy 反汇编片段 |

## legacy/ — 已删除

历史归档脚本已于开源前清理中删除（含过时的机器路径引用，不再维护）。
