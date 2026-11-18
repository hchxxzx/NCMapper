#!/bin/bash
# ================================================================
# bench_lib.sh — NCMapper 性能测试共享库（稳定性逻辑单一来源）
# ----------------------------------------------------------------
# 由三档入口脚本 source 使用，不直接执行：
#   run_all.sh            档1 全组件 (baseline 模式) × net8/9/10   —— 长期积累完整测试
#   run_ncmapper_all.sh   档2 NCMapper 全路径 × net8/9/10          —— 大改后回归
#   run_ncmapper_net10.sh 档3 NCMapper 全路径 × net10              —— 小改后快速回归
#
# 调用方 source 后设置下列变量，再调用 bench_main：
#   BENCH_MODE        基准程序运行模式 (ncmapper_all | baseline | ...)
#   BENCH_FRAMEWORKS  框架列表，空格分隔 (如 "net8.0 net9.0 net10.0")
#   BENCH_TITLE       报告标题（首行）
#   BENCH_SCOPE       范围描述行（路径/变体说明）
#   BENCH_LOG_BASE    artifacts 下日志子目录名 (如 ncmapper_run | full_run)
#   BENCH_RUN_TESTS   是否先跑 Phase 1 单元测试 (1|0，默认 1；SKIP_TESTS=1 可强制跳过)
#   BENCH_TIMEOUT     每框架 benchmark 超时秒数（默认 14400=4h）
#
# 大核锁定：基准程序 Program.cs Main 入口已设 ProcessorAffinity=0xFFFF
#   (P-core, i7-12700K 逻辑核 0-15)，BDN 子进程继承父进程亲和性，
#   故任何模式/框架均自动锁大核，脚本层无需重复处理。
#
# 稳定性设计 (源自 run_ncmapper_all.sh 2026-07-26 验证版)：
#   - Phase 1 用 exit code 判定失败（非 grep "失败:"）
#   - Phase 2 构建用独立日志 + exit code 判定（修复管道 tail 吞掉退出码的问题）
#   - Phase 2 每框架 timeout 保护 + 直接重定向（不用 tee 避免管道断裂）
#   - 跑前清理 BDN 残留 GUID 目录
#   - 输出编码统一 (bench_fix_encoding)：chcp.com 65001 + DOTNET_CLI_FORCE_UTF8_ENCODING
#     消除 dotnet 子进程(vstest/MSBuild)按 GBK(936) 写日志与脚本 UTF-8 输出混杂的乱码
#
# 退出码: 0=成功, 1=Phase1失败, 3=Phase2有异常但部分完成
# ================================================================

# 仓库根（本库位于 benchmarks/scripts/run/，向上三级）
_BENCH_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BENCH_REPO_ROOT="$(cd "$_BENCH_LIB_DIR/../../.." && pwd)"

# 统一输出编码：消除 dotnet 子进程日志的 GBK 乱码
# ----------------------------------------------------------------
# 中文 Windows 控制台输出代码页默认 GBK(936)，dotnet 子进程
# (vstest/MSBuild) 按代码页写日志，与脚本自身的 UTF-8 输出混杂
# 后日志半编码、终端显示乱码。双保险：
#   1) chcp.com 65001 —— 控制台代码页切 UTF-8，对所有依附该控制台
#      的子进程生效（无控制台的 nohup 场景静默跳过）
#   2) DOTNET_CLI_FORCE_UTF8_ENCODING=true —— dotnet CLI/MSBuild
#      强制 UTF-8 输出（无控制台场景兜底）
# 2026-08-03 实测：两方案均使 MSBuild 错误信息输出为合法 UTF-8。
bench_fix_encoding() {
  export DOTNET_CLI_FORCE_UTF8_ENCODING=true
  if command -v chcp.com >/dev/null 2>&1; then
    chcp.com 65001 >/dev/null 2>&1 || true
  fi
}

bench_init() {
  bench_fix_encoding
  cd "$BENCH_REPO_ROOT"
  # 沙箱适配：禁用 MSBuild Server + 节点复用，避免子进程被沙箱拦截文件操作
  export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
  export MSBUILDDISABLENODEREUSE=1
  BENCH_RUN_TESTS=${BENCH_RUN_TESTS:-1}
  [ "${SKIP_TESTS:-0}" = "1" ] && BENCH_RUN_TESTS=0
  BENCH_TIMEOUT=${BENCH_TIMEOUT:-14400}
  # 多阶段支持（可选）："mode1:cooldownSec:mode2:cooldownSec:mode3"，
  # 阶段外层→框架内层：先跑完当前阶段全部框架，段间冷却一次再进入下一阶段；
  # 空值=单段 BENCH_MODE（原行为，框架外层循环）
  BENCH_STAGES=${BENCH_STAGES:-}
  LOG_DIR="artifacts/${BENCH_LOG_BASE:-ncmapper_run}/$(date '+%Y%m%d_%H%M%S')"
  mkdir -p "$LOG_DIR"
  # 运行状态标记：供守护检查脚本（guard/bench_guard.sh）判定运行中/已完成
  echo "$(date '+%Y-%m-%d %H:%M:%S')" > "$LOG_DIR/RUNNING"
}

bench_header() {
  echo "================================================================"
  echo " $BENCH_TITLE"
  echo " 范围: $BENCH_SCOPE"
  echo " 模式: $BENCH_MODE"
  if [ -n "${BENCH_STAGES:-}" ]; then
    echo " 阶段: $BENCH_STAGES  （段间自动冷却）"
  fi
  echo " 框架: $BENCH_FRAMEWORKS"
  echo " BDN:  launchCount=5, warmup=3, iter=15"
  echo " CPU:  P-core 锁定 (mask=0xFFFF, 逻辑核 0-15)"
  echo " 预热: 每框架跑前启动进程 warmup + Defender 扫描预热"
  echo " 冷却: 段间停止 ${BENCH_COOLDOWN:-}s 防过热（仅多阶段模式）"
  echo " 开始时间: $(date '+%Y-%m-%d %H:%M:%S')"
  echo " 输出目录: $LOG_DIR"
  echo " Benchmark 超时: ${BENCH_TIMEOUT}s/框架"
  echo "================================================================"
  echo ""
}

# 清理 BDN 崩溃后残留的 GUID 临时构建目录
bench_clean_guid() {
  for TFM in $BENCH_FRAMEWORKS; do
    BIN_DIR="benchmarks/NCMapper.Benchmarks/bin/Release/$TFM"
    if [ -d "$BIN_DIR" ]; then
      GUID_COUNT=$(find "$BIN_DIR" -maxdepth 1 -type d -name "*-*-*-*-*" 2>/dev/null | wc -l)
      if [ "$GUID_COUNT" -gt 0 ]; then
        echo "  Cleaning $GUID_COUNT orphaned GUID dirs ($TFM)..."
        find "$BIN_DIR" -maxdepth 1 -type d -name "*-*-*-*-*" -exec rm -rf {} + 2>/dev/null || true
      fi
    fi
  done
  echo ""
}

# 单个测试项目（exit code 判定）
_bench_test_one() {
  local name="$1" proj="$2" tag="$3" tfm="$4"
  echo "  $name ($tfm)..."
  dotnet test "$proj" -f "$tfm" --no-restore -p:MicrosoftCodeCoverageEnabled=false \
    --logger "trx;LogFileName=$LOG_DIR/test_${tag}_${tfm}.trx" > "$LOG_DIR/test_${tag}_${tfm}.log" 2>&1
  local rc=$?
  if [ $rc -ne 0 ]; then
    echo "  FAILED (exit $rc): $name ($tfm)"
    tail -5 "$LOG_DIR/test_${tag}_${tfm}.log"
    PHASE1_FAILS=$((PHASE1_FAILS + 1))
  else
    tail -1 "$LOG_DIR/test_${tag}_${tfm}.log"
  fi
}

# Phase 1: 单元测试（仅 BENCH_FRAMEWORKS 中的框架）
bench_phase1() {
  echo "========== Phase 1: 单元测试 =========="
  PHASE1_FAILS=0
  # （性能测试 P0RedBaseline 修复）：NCMapper.Tests 的红色基线测试
  # Red_B01_SelfRefContainer 经子进程运行 NCMapper.CrashProbe 验证"自引用容器循环引用崩溃"，
  # CrashProbe 经输出目录探测定位（非 ProjectReference），故 Phase1 必须先 restore + build 它，
  # 否则 RedTest 报"未找到 NCMapper.CrashProbe 构建输出"。CrashProbe 为 net8.0 单 TFM，构建一次即可。
  echo "  构建 NCMapper.CrashProbe（红色基线测试依赖）..."
  if ! dotnet restore test/NCMapper.CrashProbe/NCMapper.CrashProbe.csproj > "$LOG_DIR/build_crashprobe.log" 2>&1 \
     || ! dotnet build test/NCMapper.CrashProbe/NCMapper.CrashProbe.csproj -c Debug -f net8.0 -m:1 --no-restore \
          >> "$LOG_DIR/build_crashprobe.log" 2>&1; then
    echo "  FAILED: NCMapper.CrashProbe 构建失败"
    tail -10 "$LOG_DIR/build_crashprobe.log"
    PHASE1_FAILS=$((PHASE1_FAILS + 1))
  else
    echo "  NCMapper.CrashProbe 构建成功"
  fi
  for TFM in $BENCH_FRAMEWORKS; do
    echo ""
    echo "--- TFM: $TFM ---"
    _bench_test_one "NCMapper.Tests" "test/NCMapper.Tests/NCMapper.Tests.csproj" "core" "$TFM"
    _bench_test_one "NCMapper.SourceGen.Tests" "test/NCMapper.SourceGen.Tests/NCMapper.SourceGen.Tests.csproj" "sg" "$TFM"
    _bench_test_one "NCMapper.IntegrationTests" "test/NCMapper.IntegrationTests/NCMapper.IntegrationTests.csproj" "int" "$TFM"
  done
  echo ""
  echo "========== Phase 1 完成: $(date '+%H:%M:%S') =========="
  if [ $PHASE1_FAILS -gt 0 ]; then
    # 实现头注释"1=Phase1失败"语义（此前仅 WARNING 继续）
    echo "ERROR: Phase 1 有 $PHASE1_FAILS 个测试项目失败（含编译错误），退出（exit 1）"
    echo "  查看日志: $LOG_DIR/test_*.log"
    exit 1
  else
    echo "Phase 1 全部通过。"
  fi
}

# Phase 2: Benchmark（指定模式 × BENCH_FRAMEWORKS，每框架单进程跑完；
#          若设 BENCH_STAGES 则每框架内多阶段依次执行、段间自动冷却）
bench_phase2() {
  echo ""
  if [ -n "${BENCH_STAGES:-}" ]; then
    echo "========== Phase 2: Benchmark (多阶段: $BENCH_STAGES) =========="
  else
    echo "========== Phase 2: Benchmark ($BENCH_MODE) =========="
  fi
  PHASE2_FAILS=0

  # 解析 BENCH_STAGES："mode1:cooldownSec:mode2:cooldownSec:mode3"（交替 模式/冷却秒数）
  STAGE_MODES=()
  STAGE_COOLDOWNS=()
  if [ -n "${BENCH_STAGES:-}" ]; then
    local i=0 part
    IFS=':' read -ra STAGE_PARTS <<< "$BENCH_STAGES"
    for part in "${STAGE_PARTS[@]}"; do
      if [ $((i % 2)) -eq 0 ]; then
        STAGE_MODES+=("$part")
      else
        STAGE_COOLDOWNS+=("$part")
      fi
      i=$((i + 1))
    done
  fi
  local STAGE_COUNT=${#STAGE_MODES[@]}
  # 单段模式：无 BENCH_STAGES 时退化为 [BENCH_MODE]（兼容原行为）
  if [ "$STAGE_COUNT" -eq 0 ]; then
    STAGE_MODES=("$BENCH_MODE")
    STAGE_COUNT=1
  fi

  # 逐阶段执行（阶段外层 → 框架内层）：先跑完当前阶段全部框架，冷却后再进入下一阶段
  for ((s = 0; s < STAGE_COUNT; s++)); do
    local mode="${STAGE_MODES[$s]}"
    echo ""
    if [ "$STAGE_COUNT" -gt 1 ]; then
      echo "========== Stage $((s + 1))/$STAGE_COUNT: ${mode} =========="
    fi

    for TFM in $BENCH_FRAMEWORKS; do
      echo ""
      echo "--- Building Benchmark ($TFM, Release) ---"
      dotnet build benchmarks/NCMapper.Benchmarks/NCMapper.Benchmarks.csproj -c Release -f "$TFM" -m:1 --no-restore \
        > "$LOG_DIR/build_$TFM.log" 2>&1
      BRC=$?
      tail -3 "$LOG_DIR/build_$TFM.log"
      if [ $BRC -ne 0 ]; then
        echo "ERROR: Build failed for $TFM (exit $BRC)"
        PHASE2_FAILS=$((PHASE2_FAILS + 1))
        continue
      fi

      # 预热：启动进程加载全部 DLL 并执行 warmup 迭代（CPU 热稳态 + Defender 扫描预热）
      #   两段（self/rivals）及每框架跑前均执行，保证正式计时在稳定热状态进行
      echo "--- 预热（进程 warmup + Defender 扫描）---"
      dotnet run -c Release -f "$TFM" --no-build \
        --project benchmarks/NCMapper.Benchmarks/NCMapper.Benchmarks.csproj -- __prescan__ \
        > /dev/null 2>&1
      sleep 10

      BENCH_OUT="$LOG_DIR/bench_$TFM"
      mkdir -p "$BENCH_OUT"

      local log_name="bench_log.txt"
      [ "$STAGE_COUNT" -gt 1 ] && log_name="bench_log_${mode}.txt"
      echo "--- Running ${mode} benchmarks ($TFM) at $(date '+%H:%M:%S') ---"
      timeout "$BENCH_TIMEOUT" dotnet run -c Release -f "$TFM" --no-build \
        --project benchmarks/NCMapper.Benchmarks/NCMapper.Benchmarks.csproj -- "$mode" \
        > "$BENCH_OUT/$log_name" 2>&1
      RC=$?

      if [ $RC -eq 124 ]; then
        echo "ERROR: Benchmark ${mode} ($TFM) timed out after ${BENCH_TIMEOUT}s!"
        PHASE2_FAILS=$((PHASE2_FAILS + 1))
      elif [ $RC -ne 0 ]; then
        echo "WARNING: Benchmark ${mode} ($TFM) exited with code $RC"
        COMPLETED=$(grep -c "BenchmarkRunner: End" "$BENCH_OUT/$log_name" 2>/dev/null)
        COMPLETED=${COMPLETED:-0}
        echo "  Completed classes: $COMPLETED"
        if [ "$COMPLETED" -eq 0 ]; then
          PHASE2_FAILS=$((PHASE2_FAILS + 1))
        fi
      fi

      echo "--- TFM $TFM (${mode}) 完成: $(date '+%H:%M:%S') ---"
    done

    # 段间冷却（仅非末段且配置了冷却秒数）
    if [ $((s + 1)) -lt "$STAGE_COUNT" ]; then
      local cool="${STAGE_COOLDOWNS[$s]:-0}"
      if [ "$cool" -gt 0 ] 2>/dev/null; then
        echo ""
        echo "--- 停止 ${cool}s 防过热降温（${mode} 全部框架完成 → 下一阶段 ${STAGE_MODES[$((s + 1))]}）---"
        sleep "$cool"
      fi
    fi
  done
}

bench_footer() {
  echo ""
  echo "================================================================"
  echo " 测试完成: $(date '+%Y-%m-%d %H:%M:%S')"
  echo " 模式: $BENCH_MODE ｜ 框架: $BENCH_FRAMEWORKS"
  echo " 结果目录: $LOG_DIR"
  if [ ${PHASE2_FAILS:-0} -gt 0 ]; then
    echo " WARNING: $PHASE2_FAILS 个框架的 benchmark 有异常"
  fi
  echo "================================================================"
  # 完成标记：RUNNING → DONE（供守护脚本判定；有异常也记录完成，由守护评估）
  rm -f "$LOG_DIR/RUNNING"
  echo "$(date '+%Y-%m-%d %H:%M:%S')" > "$LOG_DIR/DONE"
  if [ ${PHASE2_FAILS:-0} -gt 0 ]; then
    exit 3
  fi
}

# 主驱动：初始化 → 横幅 → 清 GUID → Phase1(可选) → Phase2 → 收尾
bench_main() {
  bench_init
  bench_header
  bench_clean_guid
  if [ "$BENCH_RUN_TESTS" = "1" ]; then
    bench_phase1
  else
    echo "========== Phase 1: 已跳过 (SKIP_TESTS=1) =========="
    echo ""
  fi
  bench_phase2
  bench_footer
}
