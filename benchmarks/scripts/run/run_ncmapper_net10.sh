#!/bin/bash
set -uo pipefail  # 移除 set -e——bench_lib 的 Phase1/Phase2
                  # 容错（rc 检查、PHASE1_FAILS、exit 3）此前被 set -e 架空
# ================================================================
# 档3：NCMapper 本组件全路径快速回归（小改后，net10 单框架）
#   模式 ncmapper_all = NCMapper / Ext / Runtime / Expression / Generated
#   框架仅 net10.0，43 类 × 1（约为档2 三分之一耗时）
#   大核锁定仅 Windows 生效（基准程序 Program.cs 内 mask=0xFFFF）；
#   Linux 等非 Windows 平台不锁核，需限核请用 taskset/numactl
# 用法: ./run_ncmapper_net10.sh [超时秒数, 默认 14400]
#       SKIP_TESTS=1 ./run_ncmapper_net10.sh   # 跳过 Phase 1 单元测试，更快
# ================================================================
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/bench_lib.sh"

BENCH_MODE="ncmapper_all"
BENCH_FRAMEWORKS="net10.0"
BENCH_TITLE="NCMapper 本组件全路径快速回归（net10 单框架）"
BENCH_SCOPE="NCMapper / Ext / Runtime / Expression / Generated（43 类全场景，仅 net10）"
BENCH_LOG_BASE="ncmapper_run"
BENCH_RUN_TESTS=1
BENCH_TIMEOUT="${1:-14400}"

bench_main
