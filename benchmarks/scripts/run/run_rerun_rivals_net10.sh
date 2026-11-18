#!/bin/bash
# ================================================================
# 复测 net10.0 rivals 段（2026-08-30 决策）
#   背景：主 run 20260830_013309 的 net10.0 rivals 段被发现净计时偏移
#         (+3~17%，五重证据：锚点 DeepCopy_Manual=1.056 / 0819 纵向 /
#         Manual 漂移 / Allocated 不变量 / 全历史分布离群)
#   目的：安静时段单独复测该段，对照历史（20260819_000607 等）定位偏移，
#         回填报告数据
#   范围：仅 net10.0 × baseline_rivals（49 类，含 Mapperly 11 场景 + 竞品）
#   用法: ./run_rerun_rivals_net10.sh [每框架超时秒数, 默认 7200]
# ================================================================
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/bench_lib.sh"

BENCH_MODE="baseline_rivals"
BENCH_FRAMEWORKS="net10.0"
BENCH_TITLE="net10.0 rivals 段复测（Mapperly 纵向验证 + 净偏移定位）"
BENCH_SCOPE="Stage2 baseline_rivals(49类) × net10.0；对照历史 20260819_000607 与主 run 20260830_013309"
BENCH_LOG_BASE="rerun_rivals_net10"
BENCH_RUN_TESTS=0          # 复测不重跑 Phase1 单测（主 run 已全绿）
BENCH_TIMEOUT="${1:-7200}" # 默认 2h，net10 rivals 49 类实测约 1.5-1.6h

# 机器级充分预热（2026-08-30 要求：跑前充分热身，否则数据不准确）
#   目的：CPU 睿频/热稳态 + Defender 扫描缓存命中；协议内 __prescan__ 照常执行，不影响可比性
echo "--- 机器级预热：build net10.0 + 连续 5 轮 __prescan__（满载 ~1 分钟）---"
dotnet build benchmarks/NCMapper.Benchmarks/NCMapper.Benchmarks.csproj -c Release -f net10.0 -m:1 --no-restore \
  > /dev/null 2>&1 || { echo "ERROR: 预热前构建失败"; exit 2; }
for i in 1 2 3 4 5; do
  dotnet run -c Release -f net10.0 --no-build \
    --project benchmarks/NCMapper.Benchmarks/NCMapper.Benchmarks.csproj -- __prescan__ > /dev/null 2>&1
  echo "  预预热轮 $i/5 完成"
done
sleep 10

bench_main
