#!/usr/bin/env bash
# ================================================================
# bench_guard.sh — 性能测试运行守护检查（供定时轮询/自动化调用）
# ----------------------------------------------------------------
# 检查最新运行目录的运行状态，输出一行 STATUS 供上层（定时任务/自动化/人工）决策：
#   1. RUNNING_OK   — 运行中且日志无致命错误（正常，继续观察）
#   2. ERROR        — 运行中/已停止但日志存在致命错误（需修复重跑）
#   3. DONE         — 已完成；附带各阶段类数与期望值下限对比
#   4. STALE        — 运行目录存在但 RUNNING/DONE 标记均缺失（异常中断）
#   5. NO_RUN       — 无运行目录（无测试在跑）
# 协议兼容：
#   - run_all.sh 协议（artifacts/full_run/<ts>/）：
#       build_net<tfm>.log（根目录）、bench_<tfm>/bench_log_<mode>.txt（子目录）
#       DONE 校验：baseline_self ≥ 36 类、baseline_rivals ≥ 46 类（下限，含重试超量）
#   - baseline 脚本协议（artifacts/benchmarks/baseline/...）：
#       build.log、run*/self.log、meta.json、self_median.csv（EXPECTED 41/51）
# 两类日志同时扫描，按存在的产物自动识别。
# 用法: ./benchmarks/scripts/guard/bench_guard.sh [run_dir]
#   不传参数时读取 artifacts/benchmarks/baseline/latest.txt；不存在则取
#   artifacts/full_run/ 最新目录（run_all.sh 产物，按 mtime）。
# 退出码: 0=正常(运行中/已完成), 1=异常(需修复), 2=无运行
# ================================================================

# 仓库根（本库位于 benchmarks/scripts/guard/，向上三级）
_GUARD_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GUARD_REPO="$(cd "$_GUARD_DIR/../../.." && pwd)"

# 运行目录：参数优先 → latest.txt（baseline 脚本产物）→ artifacts/full_run 最新目录（run_all.sh 产物）
if [ -n "${1:-}" ]; then
  RUN_DIR="$1"
else
  LATEST_FILE="$GUARD_REPO/artifacts/benchmarks/baseline/latest.txt"
  if [ -f "$LATEST_FILE" ]; then
    RUN_DIR="$(cat "$LATEST_FILE")"
  else
    # run_all.sh 不写 latest.txt，产物在 artifacts/full_run/<时间戳>/（按 mtime 取最新）
    RUN_DIR="$(ls -td "$GUARD_REPO"/artifacts/full_run/*/ 2>/dev/null | head -1)"
    RUN_DIR="${RUN_DIR%/}"
    [ -z "$RUN_DIR" ] && RUN_DIR=""
  fi
fi

if [ -z "$RUN_DIR" ] || [ ! -d "$RUN_DIR" ]; then
  echo "STATUS=NO_RUN REASON=dir_missing DIR=${RUN_DIR:-}"
  exit 2
fi

# 致命错误检测：排除 "0 个错误"/"0 errors" 等正常输出
scan_errors() {
  local log="$1" name="$2"
  # 分隔符用 |（目录名含 / 会破坏默认 s/// 语法）
  grep -nE 'error CS[0-9]+|error NU[0-9]+|error MSB[0-9]+|未捕获的异常|Unhandled exception|timed out|timedout|Timeout' "$log" 2>/dev/null | head -3 | sed "s|^|${name}:|"
  grep -n '^  ERROR:' "$log" 2>/dev/null | head -3 | sed "s|^|${name}:|"
  grep -nE 'WARN: baseline_(self|rivals) 第[0-9]+次存在错误' "$log" 2>/dev/null | head -3 | sed "s|^|${name}:|"
}

# 扫描两种协议下全部日志的致命错误
scan_all_logs() {
  local ERR_OUT=""
  # baseline 协议：build.log / run*/self.log
  if [ -f "$RUN_DIR/build.log" ]; then
    ERR_OUT+="$(scan_errors "$RUN_DIR/build.log" build)"
  fi
  for rlog in "$RUN_DIR"/run*/self.log; do
    [ -f "$rlog" ] || continue
    ERR_OUT+="$(scan_errors "$rlog" "$(basename "$(dirname "$rlog")")/self")"
  done
  # run_all 协议：build_net<tfm>.log（根目录）
  for blog in "$RUN_DIR"/build_net*.log; do
    [ -f "$blog" ] || continue
    ERR_OUT+="$(scan_errors "$blog" "$(basename "$blog")")"
  done
  # run_all 协议：bench_<tfm>/bench_log_<mode>.txt（子目录）
  for blog in "$RUN_DIR"/bench_*/bench_log_*.txt; do
    [ -f "$blog" ] || continue
    ERR_OUT+="$(scan_errors "$blog" "$(basename "$(dirname "$blog")")/$(basename "$blog")")"
  done
  echo "$ERR_OUT"
}

# run_all 协议下已完成 benchmark 类数（BenchmarkRunner: End 计数总和，含重试超量）
count_bench_classes() {
  local pattern="$1" total=0 c
  for blog in "$RUN_DIR"/bench_*/bench_log_${pattern}.txt; do
    [ -f "$blog" ] || continue
    c="$(grep -c 'BenchmarkRunner: End' "$blog" 2>/dev/null)"
    [ -z "$c" ] && c=0
    total=$((total + c))
  done
  echo "$total"
}

# 运行标记判定
if [ -f "$RUN_DIR/DONE" ]; then
  # load 协议（NCMapper.LoadBench 高峰负载压测）：校验 summary 与三场景产物
  if ls "$RUN_DIR"/load_*.log >/dev/null 2>&1; then
    LOAD_CS_OK=0; [ -f "$RUN_DIR/concurrency_Flat_Map.csv" ] && LOAD_CS_OK=1
    if [ -f "$RUN_DIR/load_summary.json" ] && [ -f "$RUN_DIR/endurance.csv" ] && [ "$LOAD_CS_OK" -eq 1 ]; then
      echo "STATUS=DONE DIR=$RUN_DIR PROTOCOL=load COLD_START=ok CONCURRENCY=ok ENDURANCE=ok"
      exit 0
    fi
    echo "STATUS=ERROR PHASE=DONE DIR=$RUN_DIR PROTOCOL=load REASON=load_artifacts_incomplete"
    exit 1
  fi
  # 已完成：校验类数下限（重试导致的超量不判失败）
  EXPECTED=0
  if [ -f "$RUN_DIR/meta.json" ]; then
    MODE="$(grep -o '"mode": *"[^"]*"' "$RUN_DIR/meta.json" | cut -d'"' -f4)"
    case "$MODE" in
      self_only)   EXPECTED=41 ;;
      rivals_only) EXPECTED=51 ;;
      *)           EXPECTED=0 ;;
    esac
  fi
  if [ -f "$RUN_DIR/self_median.csv" ]; then
    SELF_N=$(($(wc -l < "$RUN_DIR/self_median.csv") - 1))
  else
    SELF_N=0
  fi
  RIVALS_N=0
  if [ -f "$RUN_DIR/bench_log_baseline_self.txt" ] || ls "$RUN_DIR"/bench_*/bench_log_baseline_self.txt >/dev/null 2>&1; then
    SELF_N="$(count_bench_classes baseline_self)"
    [ "$EXPECTED" -eq 0 ] && EXPECTED=36
  fi
  if ls "$RUN_DIR"/bench_*/bench_log_baseline_rivals.txt >/dev/null 2>&1; then
    RIVALS_N="$(count_bench_classes baseline_rivals)"
    # 两阶段都跑：期望提升为 self + rivals 下限和
    [ "$EXPECTED" -eq 36 ] && EXPECTED=$((36 + 46))
  fi
  if [ "$EXPECTED" -gt 0 ] && [ $((SELF_N + RIVALS_N)) -lt "$EXPECTED" ]; then
    echo "STATUS=ERROR PHASE=DONE DIR=$RUN_DIR SELF_CLASSES=$SELF_N RIVALS_CLASSES=$RIVALS_N EXPECTED=$EXPECTED REASON=class_count_shortfall"
    exit 1
  fi
  echo "STATUS=DONE DIR=$RUN_DIR SELF_CLASSES=$SELF_N RIVALS_CLASSES=$RIVALS_N EXPECTED=$EXPECTED"
  exit 0
fi

if [ -f "$RUN_DIR/RUNNING" ]; then
  # 幽灵运行检测：RUNNING 标记存在但目录内无任何产物文件（build/test/bench 日志均未产生）
  # 且目录创建已超过 10 分钟 → 判定孤儿标记（进程早已消亡），避免误报 RUNNING_OK
  # （2026-08-14 00:47 幽灵目录 20260814_004733 实证：仅有 RUNNING、无日志、无进程）
  ARTIFACT_COUNT=$(find "$RUN_DIR" -maxdepth 1 -type f ! -name RUNNING 2>/dev/null | wc -l)
  if [ "$ARTIFACT_COUNT" -eq 0 ]; then
    DIR_MTIME="$(stat -c %Y "$RUN_DIR" 2>/dev/null || echo "$(date +%s)")"
    DIR_AGE_MIN=$(( ($(date +%s) - DIR_MTIME) / 60 ))
    if [ "$DIR_AGE_MIN" -ge 10 ]; then
      echo "STATUS=STALE DIR=$RUN_DIR REASON=ghost_run_running_marker_only_age=${DIR_AGE_MIN}min"
      exit 1
    fi
  fi
  # 运行中：扫描各日志致命错误
  ERR_OUT="$(scan_all_logs)"
  if [ -n "$ERR_OUT" ]; then
    ERR_SUM="$(echo "$ERR_OUT" | grep -v '^$' | head -5 | tr '\n' ';')"
    echo "STATUS=ERROR PHASE=RUNNING DIR=$RUN_DIR ERRORS=$ERR_SUM"
    exit 1
  fi
  # 运行时长（RUNNING 内容为开始时间）与已完成类数（判断当前阶段）
  START_TS="$(cat "$RUN_DIR/RUNNING" 2>/dev/null)"
  SELF_DONE="$(count_bench_classes baseline_self)"
  RIVALS_DONE="$(count_bench_classes baseline_rivals)"
  if [ "$RIVALS_DONE" -gt 0 ]; then
    PHASE="stage2_rivals"
  elif [ "$SELF_DONE" -gt 0 ]; then
    PHASE="stage1_self"
  else
    PHASE="prep_or_stage1"
  fi
  echo "STATUS=RUNNING_OK DIR=$RUN_DIR START=$START_TS PHASE=$PHASE SELF_CLASSES=$SELF_DONE RIVALS_CLASSES=$RIVALS_DONE"
  exit 0
fi

# 目录存在但无任何标记 → 异常中断
echo "STATUS=STALE DIR=$RUN_DIR REASON=no_running_or_done_marker"
exit 1
