#!/usr/bin/env bash
# =============================================================
# CI 基准门禁脚本 — CI 自建基线语义
#
# 设计：
#   - 禁止以本地入库基线（benchmarks/baselines/，12700K+ServerGC）作为 CI 对照基准
#     （CPU/OS/GC 形态不同，Mean 绝对值系统性漂移，不可比）
#   - 采用 CI 自建基线：首次 CI 运行（建立模式）跑抽样基准并落盘基线 JSON，
#     后续 CI 运行（门禁模式）与 CI 基线对照，偏差超阈值即失败
#   - 阈值依据：本地同机噪声实测（Flat_NCMapper 3 次 Mean 极差 7.3%，2026-08-09）
#     + CI 基线语义，取 10% 安全阈值（噪声上限 × 安全系数，有实证依据）
#
# 多轮统计：
#   - 每类默认 3 轮（CI_BENCH_ROUNDS 可调）取中位数，降单轮波动
#     （实测：共享开发机单轮噪声 ±5%）
#   - 各轮原始 Mean 逐行打印到 stderr——噪声带观测读数出口（勿删，探索判据消费）
#
# 反射稳态面接线（根因：共享开发机噪声）：
#   - Flat_Runtime = runtime fallback 反射路径的稳态执行——无 [MapFrom] 对照形态，
#     委托经运行时反射构建后缓存（表达式树 Compile 家族；对照实验实证其简单映射
#     走 MemberInit 快捷路径 MapperBase.CreateMapFunc，BuildMapBody 仅服务复杂映射
#     Block 回退——首建面均为观察项不入被测面）
#   - 本机实测（按维持接线的裁定执行）：Flat_Runtime 同提交
#     三组中位数极差 4.0% > 转正判据 1/3（3.33%）——超界形态为本机跨运行噪声共性
#     （对照组 DeepCopy_Generated 4.4% 同超界，非反射项特有）；CI 独占 runner 噪声
#     更小，转正时按上述三判据判，勿以本机读数先验否决
#   - 本机 SelfBench 10 轮 P50 微基准门 = 观察项，不作验收门（口径注）
#
# 观察期转正判据（写死——将来呈报转正时按此判）：
#   连续 N≥5 轮 CI check 绿 + 同提交连续 3 轮中位数极差 ≤ 阈值 1/3
#   + 反射项区分度实测在案——三判据齐备方可呈报转正
#
# 用法：
#   scripts/ci-benchmark.sh init     # 建立模式：跑抽样基准，写入 ci-baseline.json
#   scripts/ci-benchmark.sh check    # 门禁模式：跑抽样基准，与 ci-baseline.json 对照
#
# 环境变量：
#   CI_BENCH_THRESHOLD   阈值（默认 0.10，即 10%）
#   CI_BASELINE_FILE     基线文件路径（默认 benchmarks/ci-baseline/ci-baseline.json）
#   CI_BENCH_ROUNDS      每类轮数（默认 3，取中位数）
# =============================================================
set -euo pipefail

# 仓库根（脚本位于 scripts/ 下，向上二级即仓库根）
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

MODE="${1:-check}"
THRESHOLD="${CI_BENCH_THRESHOLD:-0.10}"
BASELINE_FILE="${CI_BASELINE_FILE:-benchmarks/ci-baseline/ci-baseline.json}"
ROUNDS="${CI_BENCH_ROUNDS:-3}"

# 6 个抽样基准（Map 热路径/集合/深拷贝各 1 + AutoFlatten 1 + Compose 1 + 反射稳态 1）
# Flat_Runtime = 反射稳态面接线（无 [MapFrom] 对照形态走表达式树路径，
#   runtime fallback 反射路径的稳态执行入被测面——首建面为观察项，见头注口径注）
SAMPLE_BENCHES=(
  "Flat_NCMapper"
  "Collection_NCMapper"
  "DeepCopy_Generated"
  "AutoFlatten_NCMapper"
  "ComposeMultiSource_NCMapper"
  "Flat_Runtime"
)

# 单类跑 N 轮取中位数（ns）——Program.cs 输出格式："| Run | 14.16 ns | ..."
# 各轮原始 Mean 打印到 stderr（噪声带观测读数）；提取为空按执行错误终止（防假红）
run_bench_median() {
  local bench="$1"
  local values=()
  local round v output
  for round in $(seq 1 "$ROUNDS"); do
    echo "  Running ${bench} (round ${round}/${ROUNDS})..." >&2
    output="$(dotnet run --project benchmarks/NCMapper.Benchmarks/NCMapper.Benchmarks.csproj \
      -c Release -f net10.0 -- "$bench" 2>/dev/null)"
    # 取最后一行 "| Run |" 数据行（跳过首次波动）——单位归一化到 ns
    # （探索首跑发现：BDN 按量级自适应单位，慢类输出 μs/ms，原提取链不识别单位
    #   会令基线绝对值失真——类内对照虽不受影响，绝对值与跨类读数不可用；原缺陷 CI 从未
    #   实跑故未触发。μ 兼容三形态：U+03BC 希腊mu / U+00B5 micro / ASCII us）
    v="$(echo "$output" | grep -E '^\| Run' | tail -1 | awk -F'|' '{
      s = $3
      gsub(/,/, "", s)
      sub(/^[ \t]+/, "", s); sub(/[ \t\r]+$/, "", s)
      split(s, p, /[ \t]+/)
      val = p[1] + 0
      unit = (p[2] != "" ? p[2] : "ns")
      if (unit == "μs" || unit == "µs" || unit == "us") val *= 1000
      else if (unit == "ms") val *= 1000000
      else if (unit == "s") val *= 1000000000
      print val
    }')"
    if [ -z "$v" ] || [ "$v" = "0" ]; then
      echo "  ❌ ${bench} 第 ${round} 轮提取为空或零值（BDN 未产出 Run 行？）——防 -100% 假红，按执行错误终止" >&2
      exit 2
    fi
    values+=("$v")
    echo "  ${bench} round ${round}: ${v} ns" >&2
  done
  # 中位数（奇数轮取中间值，偶数轮取中间两值均值）
  printf '%s\n' "${values[@]}" | sort -g | awk -v n="$ROUNDS" '
    { a[NR] = $1 + 0 }
    END {
      if (n % 2 == 1) { printf "%.2f\n", a[(n + 1) / 2] }
      else { printf "%.2f\n", (a[n / 2] + a[n / 2 + 1]) / 2 }
    }'
}

# 建立模式：跑全部抽样基准并写入基线
init_baseline() {
  echo "=== CI 基准门禁：建立模式（init）==="
  echo "  基准: ${SAMPLE_BENCHES[*]}"
  echo "  阈值: $THRESHOLD"
  echo "  统计: ${ROUNDS} 轮中位数"
  mkdir -p "$(dirname "$BASELINE_FILE")"

  local json="{"
  local first=1
  for bench in "${SAMPLE_BENCHES[@]}"; do
    local median
    median="$(run_bench_median "$bench")"
    echo "  ${bench} 中位数(${ROUNDS}轮) = ${median} ns"
    if [ "$first" -ne 1 ]; then json+=","; fi
    json+="\"${bench}\": ${median}"
    first=0
  done
  json+="}"

  # rounds/stat = 顶层标量（基线统计口径自描述——check 端 informational 消费；
  #   外层键 median_ns 语义如实，两消费者 local-ci-check.ps1:779 / ci.yml:275 仅在位判定不解析键名）
  cat > "$BASELINE_FILE" <<EOF
{
  "generated_at": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "runner": "${RUNNER_OS:-unknown}-${RUNNER_ARCH:-unknown}",
  "threshold": $THRESHOLD,
  "rounds": $ROUNDS,
  "stat": "median",
  "median_ns": $json
}
EOF
  echo "  ✅ CI 基线已写入: $BASELINE_FILE"
  echo "  ⚠️ 首次基线需人工复核后提交入库（确认数值合理、非异常环境波动）"
}

# 门禁模式：跑抽样基准与基线对照
check_gate() {
  echo "=== CI 基准门禁：门禁模式（check）==="
  if [ ! -f "$BASELINE_FILE" ]; then
    echo "  ❌ CI 基线不存在: $BASELINE_FILE"
    echo "  → 请先运行 scripts/ci-benchmark.sh init 建立基线"
    exit 2
  fi

  echo "  阈值: $THRESHOLD"
  echo "  统计: ${ROUNDS} 轮中位数"
  # 基线统计口径 informational：rounds/stat 自描述字段缺 =
  # 单轮 Mean 旧口径基线，与多轮中位数数值不可比（现状无历史文件，仅防护）
  local b_rounds b_stat
  b_rounds="$(grep -o '"rounds": [0-9]*' "$BASELINE_FILE" | awk -F': ' '{print $2}')"
  b_stat="$(grep -o '"stat": "[a-z]*"' "$BASELINE_FILE" | sed 's/.*: "//;s/"$//')"
  if [ -n "$b_rounds" ] && [ -n "$b_stat" ]; then
    echo "  基线口径: rounds=${b_rounds} stat=${b_stat}"
  else
    echo "  ⚠️ 基线无 rounds/stat 字段（单轮 Mean 旧口径？）——与中位数口径不可比，建议重新 init"
  fi
  local failed=0
  for bench in "${SAMPLE_BENCHES[@]}"; do
    local baseline new_median diff_pct
    baseline="$(grep -o "\"${bench}\": [0-9.]*" "$BASELINE_FILE" | awk -F': ' '{print $2}')"
    if [ -z "$baseline" ]; then
      echo "  ⚠️ 基线缺 ${bench}（旧基线？）→ 需重新 init"
      failed=1
      continue
    fi
    new_median="$(run_bench_median "$bench")"
    diff_pct="$(awk -v a="$new_median" -v b="$baseline" 'BEGIN{printf "%.1f", (a-b)/b*100}')"
    echo "  ${bench}: 基线 ${baseline} ns vs 本次 ${new_median} ns → ${diff_pct}%"
    if awk -v d="$diff_pct" -v t="$THRESHOLD" 'BEGIN{exit !(d>t*100 || d < -t*100)}'; then
      echo "  ❌ ${bench} 偏差超阈值"
      failed=1
    fi
  done

  if [ "$failed" -ne 0 ]; then
    echo "=== ❌ CI 基准门禁失败：存在超阈值基准 ==="
    exit 1
  fi
  echo "=== ✅ CI 基准门禁通过 ==="
}

case "$MODE" in
  init) init_baseline ;;
  check) check_gate ;;
  *) echo "用法: $0 [init|check]" >&2; exit 2 ;;
esac
