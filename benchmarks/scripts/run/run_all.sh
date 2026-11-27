#!/bin/bash
set -uo pipefail  # 移除 set -e——bench_lib 的 Phase1/Phase2
                  # 容错（rc 检查、PHASE1_FAILS、exit 3）此前被 set -e 架空
# ================================================================
# 档1：全组件完整性能测试（长期积累，单脚本覆盖全部）
#   两阶段分进程运行:
#     Stage1 baseline_self   = NCMapper 自己组件 3 变体 + DeepCopy 4 变体 + Update 3 变体 + CollectionInto 1 变体（41 类，Big 已排除；
#                               BidirDE_Ext 2026-08-18 补齐；Update 2026-08-19 补齐；CollectionInto_NCMapper 2026-08-20 新增）× net8/9/10
#     Stage2 baseline_rivals = 竞品变体 AutoMapper/Manual/Mapperly/Mapster + DeepCopy 竞品 + Update 3 变体（49 类）× net8/9/10
#   执行顺序：Stage1 三框架全部跑完 → 停止 10 分钟防过热降温 → Stage2 三框架（共冷却一次）
#   冷却动机：防止 CPU 过热影响计时，保证两段测试热状态一致（非 PGO 隔离目的）
#   大核锁定仅 Windows 生效（基准程序 Program.cs 内 mask=0xFFFF）；
#   Linux 等非 Windows 平台不锁核，需限核请用 taskset/numactl
# 用法: ./run_all.sh [每框架超时秒数, 默认 14400]
#       SKIP_TESTS=1 ./run_all.sh   # 跳过 Phase 1 单元测试
# 说明: 段间冷却秒数可改 BENCH_COOLDOWN 环境变量（默认 600=10 分钟，防过热降温）
# ================================================================
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/bench_lib.sh"

BENCH_MODE="baseline"
BENCH_COOLDOWN="${BENCH_COOLDOWN:-600}"
BENCH_STAGES="baseline_self:${BENCH_COOLDOWN}:baseline_rivals"
BENCH_FRAMEWORKS="net8.0 net9.0 net10.0"
BENCH_TITLE="NCMapper 全组件完整性能测试（self + 竞品分阶段，段间冷却 ${BENCH_COOLDOWN}s）"
BENCH_SCOPE="Stage1 baseline_self(41类) → 冷却${BENCH_COOLDOWN}s → Stage2 baseline_rivals(49类)；不含 Big/Array/Dict/Compose 场景（Update 2026-08-19 起纳入；CollectionInto 2026-08-20 起纳入）"
BENCH_LOG_BASE="full_run"
BENCH_RUN_TESTS=1
BENCH_TIMEOUT="${1:-14400}"

bench_main
