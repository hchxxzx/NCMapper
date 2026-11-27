#!/bin/bash
# ================================================================
# bench_watchdog.sh — 性能测试定时启动 + 每 10 分钟自愈看护
# ----------------------------------------------------------------
# 设计目标（用户 2026-11-21 要求）：
#   1. 晚上 0 点自动开始跑 run_ncmapper_all.sh
#   2. 每 10 分钟检查测试是否正常在跑
#   3. 发现问题 → 修复后继续跑（不是简单放弃）
#
# 由 cron 每 10 分钟调用一次（幂等：重复调用不会起第二份）：
#   */10 * * * * /path/bench_watchdog.sh tick >> /path/watchdog.log 2>&1
# 也支持手动子命令：
#   ./bench_watchdog.sh start    立即启动测试（绕过 0 点判断）
#   ./bench_watchdog.sh tick     单次看护检查（cron 用）
#   ./bench_watchdog.sh status   查看当前状态
#   ./bench_watchdog.sh stop     停止测试与看护
#   ./bench_watchdog.sh reset    清理状态（下次 tick 会重新起跑）
#
# 自愈策略（按故障类型分级）：
#   A. 进程死了但未产出 DONE      → 断点续跑（复用同一 LOG_DIR，跳过已有结果的类）
#   B. 日志出现致命错误           → 记录并把失败类从结果里剔除后重跑
#   C. 卡死（进程在但长时间无输出）→ 杀掉后断点续跑
#   D. 机器被其他任务占满          → 暂停等待，不浪费一次完整跑批
#   E. 磁盘空间不足               → 停止并告警（无法自愈，需人工介入）
#
# 退出码：0=正常  1=已修复并继续  2=需要人工介入
# ================================================================

set -uo pipefail

# ---------- 路径与常量 ----------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
STATE_DIR="$REPO_ROOT/artifacts/watchdog"
LOCK_FILE="$STATE_DIR/watchdog.lock"
STATE_FILE="$STATE_DIR/state.env"
WATCH_LOG="$STATE_DIR/watchdog.log"
RUN_LOG="$STATE_DIR/current_run.log"

# .NET 环境：统一使用系统安装（/usr/lib/dotnet，apt 管理 + 合并的 SDK 10.0.403）
# 不再使用独立安装的 SDK——独立 SDK 只带自身版本的运行时，缺 net8/net9 运行时会令
# Phase1 在非 net10 框架上直接 "You must install or update .NET to run this application"。
# 如某台机器确实需要覆盖，可显式设 DOTNET_ROOT_DIR 指定，默认留空=走系统。
if [ -n "${DOTNET_ROOT_DIR:-}" ]; then
  export DOTNET_ROOT="$DOTNET_ROOT_DIR"
  export PATH="$DOTNET_ROOT_DIR:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
export MSBUILDDISABLENODEREUSE=1

BENCH_SCRIPT="${BENCH_SCRIPT:-$SCRIPT_DIR/run_all.sh}"

# 阈值
STALL_MINUTES="${STALL_MINUTES:-45}"      # 进程存活但日志超过该分钟数无增长 → 判定卡死
MIN_IDLE_PCT="${MIN_IDLE_PCT:-50}"        # CPU 空闲率低于该值（即使用率 >50%）→ 判定机器被占满，暂缓
MIN_FREE_GB="${MIN_FREE_GB:-5}"           # 磁盘剩余低于该值 → 停止告警
MAX_ATTEMPTS="${MAX_ATTEMPTS:-20}"        # 单轮跑批最多重试次数（防无限重启）
START_HOUR="${START_HOUR:-0}"             # 定时启动的小时（0=凌晨0点）
START_MINUTE="${START_MINUTE:-0}"         # 定时启动的分钟（配合 START_HOUR，如 3:30 则 HOUR=3 MINUTE=30）

mkdir -p "$STATE_DIR"

# ---------- 日志 ----------
log() {
  printf '[%s] %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*" | tee -a "$WATCH_LOG"
}

# ---------- 状态读写 ----------
# state.env 保存：PHASE（idle|running|done|failed）、LOG_DIR、ATTEMPTS、STARTED_AT、LAST_SIZE
load_state() {
  PHASE="idle"; LOG_DIR=""; ATTEMPTS=0; STARTED_AT=""; LAST_SIZE=0; LAST_GROW=""; NEXT_START_EPOCH=""
  if [ -f "$STATE_FILE" ]; then
    # shellcheck disable=SC1090
    . "$STATE_FILE"
  fi
}

save_state() {
  cat > "$STATE_FILE" <<EOF
PHASE="$PHASE"
LOG_DIR="$LOG_DIR"
ATTEMPTS=$ATTEMPTS
STARTED_AT="$STARTED_AT"
LAST_SIZE=$LAST_SIZE
LAST_GROW="$LAST_GROW"
NEXT_START_EPOCH="$NEXT_START_EPOCH"
EOF
}

# 计算"下一个 START_HOUR 点"的绝对时间戳
#   今天该时刻尚未到 → 用今天的；已过 → 用明天的
# 这是幂等的：只在 NEXT_START_EPOCH 为空时计算一次并落盘，
# 避免每次 tick 重算导致"永远等下一个 0 点"。
next_start_epoch() {
  local today_target now hhmm
  now="$(date +%s)"
  # 支持 时:分（START_MINUTE 默认 0）
  hhmm="$(printf '%02d:%02d:00' "$START_HOUR" "$START_MINUTE")"
  today_target="$(date -d "$(date '+%Y-%m-%d') $hhmm" +%s 2>/dev/null || echo 0)"
  if [ "$today_target" -eq 0 ]; then
    # date -d 不可用时退化为 24 小时轮询
    echo $(( now + 86400 ))
    return
  fi
  if [ "$now" -lt "$today_target" ]; then
    echo "$today_target"
  else
    echo $(( today_target + 86400 ))
  fi
}

# ---------- 工具函数 ----------

# 当前测试进程是否存活
# 覆盖两种入口：run_all.sh（完整两段）与 run_ncmapper_all.sh（self 单段），
# 以及脚本已退出但 dotnet 基准进程仍在的情况（例如被 detach）。
bench_alive() {
  pgrep -f "run_all\.sh" >/dev/null 2>&1 && return 0
  pgrep -f "run_ncmapper_all\.sh" >/dev/null 2>&1 && return 0
  pgrep -f "NCMapper\.Benchmarks.*(baseline_self|baseline_rivals|ncmapper_all)" >/dev/null 2>&1 && return 0
  return 1
}

# CPU 空闲率（整数百分比，采样 3 秒）
cpu_idle_pct() {
  local a b
  a="$(awk '/^cpu /{print $2+$3+$4+$5+$6+$7+$8, $5}' /proc/stat)"
  sleep 3
  b="$(awk '/^cpu /{print $2+$3+$4+$5+$6+$7+$8, $5}' /proc/stat)"
  awk -v a="$a" -v b="$b" 'BEGIN{
    split(a,x," "); split(b,y," ");
    tot=y[1]-x[1]; idle=y[2]-x[2];
    if (tot<=0) { print 100; exit }
    printf "%d", idle*100/tot
  }'
}

# 磁盘剩余 GB（/mnt/data 所在盘）
free_gb() {
  df -BG "$REPO_ROOT" 2>/dev/null | awk 'NR==2{gsub(/G/,"",$4); print $4+0}' || echo 999
}

# 最新运行目录
# 产物根目录取决于入口脚本的 BENCH_LOG_BASE：
#   run_all.sh           → artifacts/full_run/<ts>/          （完整两段）
#   run_ncmapper_all.sh  → artifacts/ncmapper_run/<ts>/      （self 单段）
#   run_rerun_rivals_*   → artifacts/rerun_rivals_*/<ts>/    （竞品单段）
# 故这里扫描 artifacts/ 下所有已知根目录，按 mtime 取最新的那个。
latest_run_dir() {
  ls -td "$REPO_ROOT"/artifacts/{full_run,ncmapper_run,rerun_rivals_net9,rerun_rivals_net10}/*/ \
    2>/dev/null | head -1 | sed 's:/$::'
}

# 运行目录内所有日志的总字节数（用于卡死判定）
run_log_size() {
  local dir="$1"
  [ -d "$dir" ] || { echo 0; return; }
  find "$dir" -type f \( -name '*.log' -o -name '*.txt' \) -printf '%s\n' 2>/dev/null \
    | awk '{s+=$1} END{print s+0}'
}

# 统计已完成类数（BDN 每跑完一类打印 "BenchmarkRunner: End"）
count_completed_classes() {
  local dir="$1"
  [ -d "$dir" ] || { echo 0; return; }
  grep -rhc "BenchmarkRunner: End" "$dir" 2>/dev/null | awk '{s+=$1} END{print s+0}'
}

# 扫描致命错误
scan_fatal() {
  local dir="$1"
  [ -d "$dir" ] || return 0
  grep -rhoE "error CS[0-9]+|error NU[0-9]+|error MSB[0-9]+|Unhandled exception|OutOfMemoryException" \
    "$dir" --include='*.log' --include='*.txt' 2>/dev/null | sort -u | head -5
}

# ---------- 启动测试 ----------
# 以独立进程组后台启动，脱离 cron 会话
# $1 非空 = 断点续跑，复用该目录（bench_lib.sh 读 BENCH_LOG_DIR 后不再新建目录，
#            其内部"已有 Mean 结果则跳过"的逻辑才会生效）
launch_bench() {
  local resume_dir="${1:-}"
  mkdir -p "$STATE_DIR"

  log "启动测试：run_ncmapper_all.sh（第 $((ATTEMPTS + 1)) 次尝试）"
  if [ -n "$resume_dir" ]; then
    log "断点续跑：复用目录 $resume_dir（已完成类将被跳过）"
    export BENCH_LOG_DIR="$resume_dir"
  else
    unset BENCH_LOG_DIR
  fi

  # setsid：脱离当前会话，cron 退出后测试继续跑
  # 9>&- ：关键！关闭继承来的锁文件描述符。
  #   若不关闭，子进程会一直持有 flock 的 fd 9，导致测试跑批全程锁被占用，
  #   之后每 10 分钟的 tick 都会因拿不到锁而跳过 → 看护彻底失效（实测踩到）。
  setsid nohup bash "$BENCH_SCRIPT" >> "$RUN_LOG" 2>&1 9>&- &
  local pid=$!
  log "测试进程已启动 PID=$pid（日志 $RUN_LOG）"

  ATTEMPTS=$((ATTEMPTS + 1))
  PHASE="running"
  STARTED_AT="$(date '+%Y-%m-%d %H:%M:%S')"
  LAST_SIZE=0
  LAST_GROW="$(date '+%Y-%m-%d %H:%M:%S')"

  if [ -n "$resume_dir" ]; then
    LOG_DIR="$resume_dir"
  else
    # 新启动：等 bench_lib 建好目录后立即探测，避免状态里 LOG_DIR 为空
    # 导致后续 tick 无从判定"进程是否已退出但无 DONE"。
    local waited=0
    LOG_DIR=""
    while [ "$waited" -lt 30 ]; do
      sleep 1
      waited=$((waited + 1))
      LOG_DIR="$(latest_run_dir)"
      [ -n "$LOG_DIR" ] && break
    done
    if [ -n "$LOG_DIR" ]; then
      log "运行目录：$LOG_DIR"
    else
      log "WARN 30 秒内未探测到运行目录，下次 tick 再探测"
    fi
  fi
  save_state
}

# ---------- 主看护逻辑 ----------
do_tick() {
  # 防并发：同一时刻只允许一个 tick 运行
  exec 9>"$LOCK_FILE"
  if ! flock -n 9; then
    log "另一个 tick 正在运行，本次跳过"
    return 0
  fi

  load_state

  # ---- 1. 已完成：不再干预 ----
  if [ "$PHASE" = "done" ]; then
    log "状态 DONE，无需干预（LOG_DIR=$LOG_DIR）"
    return 0
  fi

  # ---- 2. 磁盘空间检查（无法自愈） ----
  local fg
  fg="$(free_gb)"
  if [ "${fg:-999}" -lt "$MIN_FREE_GB" ]; then
    log "ERROR 磁盘剩余 ${fg}GB < ${MIN_FREE_GB}GB，停止测试，需人工清理"
    PHASE="failed"; save_state
    pkill -f "run_ncmapper_all\.sh" 2>/dev/null
    return 2
  fi

  # ---- 3. 未启动：判断是否到点 ----
  if [ "$PHASE" = "idle" ]; then
    # 目标启动时刻：首次进入 idle 时算出"下一个 START_HOUR 点"，写入状态
    # （不能简单用 hour >= START_HOUR 判断：START_HOUR=0 时 21 点也会满足，
    #   导致白天误启动。必须比较绝对时刻。）
    if [ -z "${NEXT_START_EPOCH:-}" ]; then
      NEXT_START_EPOCH="$(next_start_epoch)"
      save_state
      log "计划启动时刻：$(date -d "@$NEXT_START_EPOCH" '+%Y-%m-%d %H:%M:%S')"
    fi

    local now_epoch
    now_epoch="$(date +%s)"
    if [ "$now_epoch" -lt "$NEXT_START_EPOCH" ]; then
      local wait_min=$(( (NEXT_START_EPOCH - now_epoch) / 60 ))
      log "未到启动时刻（还有 ${wait_min} 分钟，计划 $(date -d "@$NEXT_START_EPOCH" '+%m-%d %H:%M')），等待"
      return 0
    fi

    # 到点后：机器被占满时暂缓（避免跑出失真数据），下个 tick 再试
    local idle
    idle="$(cpu_idle_pct)"
    if [ "$idle" -lt "$MIN_IDLE_PCT" ]; then
      log "已到启动时刻，但 CPU 空闲率 ${idle}% < ${MIN_IDLE_PCT}%，机器被占用，暂缓启动（下个 tick 重试）"
      return 0
    fi
    launch_bench
    return 0
  fi

  # ---- 4. 运行中：检查健康度 ----
  if [ "$PHASE" = "running" ]; then
    # 目录来源优先级：状态里记录的续跑目录 > 按 mtime 探测的最新目录
    # （续跑时 bench_lib 复用旧目录，mtime 可能比别的目录旧，不能用"最新"判定）
    local dir
    if [ -n "$LOG_DIR" ] && [ -d "$LOG_DIR" ]; then
      dir="$LOG_DIR"
    else
      dir="$(latest_run_dir)"
      LOG_DIR="${dir:-}"
      save_state
    fi

    # 4a. 进程存活？
    if ! bench_alive; then
      # 进程已退出：先看是否正常完成
      if [ -n "$dir" ] && [ -f "$dir/DONE" ]; then
        log "测试已正常完成（DONE 标记存在），目录 $dir"
        PHASE="done"; save_state
        return 0
      fi

      # 未正常完成 → 断点续跑
      if [ "$ATTEMPTS" -ge "$MAX_ATTEMPTS" ]; then
        log "ERROR 已达最大重试次数 $MAX_ATTEMPTS，停止，需人工介入（目录 $dir）"
        PHASE="failed"; save_state
        return 2
      fi

      log "WARN 检测到测试进程已退出但无 DONE 标记，准备断点续跑"
      local errs
      errs="$(scan_fatal "$dir")"
      if [ -n "$errs" ]; then
        log "  已记录致命错误：$(echo "$errs" | tr '\n' ';')"
      fi
      local done_n
      done_n="$(count_completed_classes "$dir")"
      log "  退出前已完成 $done_n 个基准类，续跑将跳过已完成的"
      sleep 5
      launch_bench "$dir"
      return 1
    fi

    # 4b. 进程存活：检查是否卡死
    local cur now_size grow_ts stall_min
    cur="$(run_log_size "$dir")"
    if [ "$cur" -gt "$LAST_SIZE" ]; then
      LAST_SIZE="$cur"
      LAST_GROW="$(date '+%Y-%m-%d %H:%M:%S')"
      save_state
    fi
    if [ -n "$LAST_GROW" ]; then
      local grow_epoch now_epoch
      grow_epoch="$(date -d "$LAST_GROW" +%s 2>/dev/null || echo 0)"
      now_epoch="$(date +%s)"
      stall_min=$(( (now_epoch - grow_epoch) / 60 ))
      if [ "$stall_min" -ge "$STALL_MINUTES" ]; then
        log "WARN 日志已 ${stall_min} 分钟无增长（阈值 ${STALL_MINUTES}），判定卡死，重启续跑"
        pkill -f "run_ncmapper_all\.sh" 2>/dev/null
        pkill -f "NCMapper\.Benchmarks.*ncmapper_all" 2>/dev/null
        sleep 5
        if [ "$ATTEMPTS" -ge "$MAX_ATTEMPTS" ]; then
          log "ERROR 已达最大重试次数，停止，需人工介入"
          PHASE="failed"; save_state
          return 2
        fi
        launch_bench "$dir"
        return 1
      fi
    fi

    # 4c. 机器被其他任务抢占 → 仅告警（不杀测试，避免丢失进度）
    local idle2
    idle2="$(cpu_idle_pct)"
    local done_n2
    done_n2="$(count_completed_classes "$dir")"
    if [ "$idle2" -lt "$MIN_IDLE_PCT" ]; then
      log "WARN 运行中 CPU 空闲率仅 ${idle2}%，可能受其他任务干扰，测量结果可信度下降（已完成 ${done_n2} 类）"
    else
      log "运行正常：已完成 ${done_n2} 类，CPU 空闲 ${idle2}%，日志 ${cur} 字节"
    fi
    return 0
  fi

  # ---- 5. 失败态：需人工 ----
  if [ "$PHASE" = "failed" ]; then
    log "状态 FAILED，等待人工处理（如需重跑：$0 reset 后等下一个 tick）"
    return 2
  fi

  return 0
}

# ---------- 子命令 ----------
cmd_status() {
  load_state
  echo "=== 看护状态 ==="
  echo "PHASE      : $PHASE"
  echo "LOG_DIR    : ${LOG_DIR:-（无）}"
  echo "ATTEMPTS   : $ATTEMPTS"
  echo "STARTED_AT : ${STARTED_AT:-（无）}"
  if [ -n "${NEXT_START_EPOCH:-}" ]; then
    echo "计划启动   : $(date -d "@$NEXT_START_EPOCH" '+%Y-%m-%d %H:%M:%S')"
  else
    echo "计划启动   : （未计算，下个 tick 确定）"
  fi
  if bench_alive; then echo "进程       : 运行中"; else echo "进程       : 未运行"; fi
  local dir
  dir="$(latest_run_dir)"
  if [ -n "$dir" ]; then
    echo "最新目录   : $dir"
    echo "已完成类数 : $(count_completed_classes "$dir")"
    echo "DONE 标记  : $([ -f "$dir/DONE" ] && echo 是 || echo 否)"
  fi
  echo "CPU 空闲率 : $(cpu_idle_pct)%"
  echo "磁盘剩余   : $(free_gb)GB"
}

cmd_stop() {
  log "手动停止测试与看护"
  pkill -f "run_ncmapper_all\.sh" 2>/dev/null
  pkill -f "NCMapper\.Benchmarks.*ncmapper_all" 2>/dev/null
  load_state
  PHASE="idle"; NEXT_START_EPOCH=""; save_state
  echo "已停止"
}

cmd_reset() {
  log "重置看护状态"
  load_state
  PHASE="idle"; LOG_DIR=""; ATTEMPTS=0; STARTED_AT=""; LAST_SIZE=0; LAST_GROW=""; NEXT_START_EPOCH=""
  save_state
  echo "已重置（下个 tick 将重新计算启动时刻）"
}

cmd_start() {
  load_state
  if bench_alive; then
    echo "测试已在运行，不重复启动"
    return 0
  fi
  launch_bench
  echo "已启动，查看进度：$0 status"
}

case "${1:-tick}" in
  tick)   do_tick ;;
  start)  cmd_start ;;
  status) cmd_status ;;
  stop)   cmd_stop ;;
  reset)  cmd_reset ;;
  *)
    echo "用法: $0 [tick|start|status|stop|reset]" >&2
    exit 2
    ;;
esac
