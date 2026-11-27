#!/bin/bash
set -uo pipefail  # 移除 set -e——bench_lib 的 Phase1/Phase2
                  # 容错（rc 检查、PHASE1_FAILS、exit 3）此前被 set -e 架空
# ================================================================
# 档2：NCMapper 本组件全路径全框架回归测试（大改后）
#   模式 ncmapper_all = NCMapper / Ext / Runtime / Expression / Generated
#   框架 net8.0 / net9.0 / net10.0，43 类 × 3（实测 2026-08-20；含 CollectionInto_NCMapper，Big 3 类 2026-08-13 裁决移除后注释未更新）
#   大核锁定仅 Windows 生效（基准程序 Program.cs 内 mask=0xFFFF）；
#   Linux 等非 Windows 平台不锁核，需限核请用 taskset/numactl
# 用法: ./run_ncmapper_all.sh [每框架超时秒数, 默认 14400]
#       SKIP_TESTS=1 ./run_ncmapper_all.sh   # 跳过 Phase 1 单元测试
# ================================================================
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/bench_lib.sh"

BENCH_MODE="ncmapper_all"
BENCH_FRAMEWORKS="net8.0 net9.0 net10.0"
BENCH_TITLE="NCMapper 本组件全路径全框架回归测试"
BENCH_SCOPE="NCMapper / Ext / Runtime / Expression / Generated（43 类全场景，含 CollectionInto_NCMapper）"
BENCH_LOG_BASE="ncmapper_run"
BENCH_RUN_TESTS=1
BENCH_TIMEOUT="${1:-14400}"

bench_main
