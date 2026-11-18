#!/bin/bash
set -uo pipefail  # 移除 set -e——timeout/失败分支的显式容错
                  # （TIMEOUT/FAILED 记录、断点续跑）此前被 set -e 架空成死代码；显式错误检查保留
# NCMapper vs 全组件 自动化测试脚本（稳定性增强版）
# 6 组件 × 7 场景，SimpleJob(launchCount=5) 内部处理多轮
# 用法：./run_bench.sh <framework> <output_dir>
# 示例：./run_bench.sh net10.0 ./results
#
# 稳定性改进 (2026-07-26):
#   - 移除 set -e，改为显式错误检查（避免静默终止）
#   - 构建步骤加保护（失败明确报错退出）
#   - 每个类加 600s 超时（防 BDN 挂起）
#   - 断点续跑：已有有效结果的类自动跳过
#   - 跑前清理 BDN 残留 GUID 目录（防磁盘/路径干扰）

# 脚本位于 benchmarks/scripts/run/，向上三级为仓库根；基准项目在 benchmarks/NCMapper.Benchmarks
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
source "$SCRIPT_DIR/bench_lib.sh"
bench_fix_encoding   # 统一 UTF-8 输出，消除 dotnet 子进程 GBK 乱码
PROJ_DIR="$REPO_ROOT/benchmarks/NCMapper.Benchmarks"
cd "$PROJ_DIR"

FRAMEWORK=${1:-net10.0}
OUTPUT_DIR=${2:-./results}
TIMEOUT_SEC=${3:-600}

# 全部 Benchmark 类（按场景 × 组件交错排列）
RUN_ORDER=(
    "Flat_NCMapper"        "Flat_Mapperly"        "Flat_Mapster"        "Flat_AutoMapper"
    "NameDiff_NCMapper"    "NameDiff_Mapperly"    "NameDiff_Mapster"    "NameDiff_AutoMapper"
    "Nested_NCMapper"      "Nested_Mapperly"      "Nested_Mapster"      "Nested_AutoMapper"
    "Collection_NCMapper"  "Collection_Mapperly"  "Collection_Mapster"  "Collection_AutoMapper"
    "BidirED_NCMapper"     "BidirED_Mapperly"     "BidirED_Mapster"     "BidirED_AutoMapper"
    "BidirDE_NCMapper"     "BidirDE_Mapperly"     "BidirDE_Mapster"     "BidirDE_AutoMapper"
    "Action_NCMapper"      "Action_Mapperly"      "Action_Mapster"      "Action_AutoMapper"
)

mkdir -p "$OUTPUT_DIR"

# 结果汇总文件（追加模式，支持续跑）
SUMMARY_FILE="$OUTPUT_DIR/summary.csv"
if [ ! -f "$SUMMARY_FILE" ]; then
    echo "class,mean_ns,elapsed_s" > "$SUMMARY_FILE"
fi

echo "=========================================="
echo "  NCMapper Full Bench (stability-enhanced)"
echo "  Framework: $FRAMEWORK"
echo "  BDN: launchCount=5, warmup=3, iter=15"
echo "  Total: ${#RUN_ORDER[@]} benchmarks"
echo "  Timeout: ${TIMEOUT_SEC}s per class"
echo "  Output: $OUTPUT_DIR"
echo "=========================================="
echo ""

# Step 0: 清理 BDN 残留 GUID 目录（崩溃后未清理的临时构建目录）
BIN_DIR="$PROJ_DIR/bin/Release/$FRAMEWORK"
if [ -d "$BIN_DIR" ]; then
    GUID_COUNT=$(find "$BIN_DIR" -maxdepth 1 -type d -name "*-*-*-*-*" 2>/dev/null | wc -l)
    if [ "$GUID_COUNT" -gt 0 ]; then
        echo ">>> Cleaning $GUID_COUNT orphaned BDN GUID directories..."
        find "$BIN_DIR" -maxdepth 1 -type d -name "*-*-*-*-*" -exec rm -rf {} + 2>/dev/null
        echo "    Done."
        echo ""
    fi
fi

# Step 1: Shutdown stale MSBuild server
dotnet build-server shutdown 2>/dev/null

# Step 2: Build once (with explicit error check)
echo ">>> Building (Release / $FRAMEWORK)..."
if ! dotnet build -c Release -f "$FRAMEWORK" --nologo -v q -m:1; then
    echo "ERROR: Build failed for $FRAMEWORK. Aborting."
    exit 1
fi
echo ""

START_TIME=$(date +%s)
TOTAL=${#RUN_ORDER[@]}
DONE=0
SKIPPED=0
FAILED=0

for cls in "${RUN_ORDER[@]}"; do
    DONE=$((DONE + 1))
    log_file="$OUTPUT_DIR/${cls}.log"

    # 断点续跑：如果已有有效结果（含 Mean），跳过
    if [ -f "$log_file" ] && grep -qP "Mean\s*=\s*[\d.]+" "$log_file" 2>/dev/null; then
        printf "[%2d/%2d] %-30s ... SKIPPED (has result)\n" "$DONE" "$TOTAL" "$cls"
        SKIPPED=$((SKIPPED + 1))
        continue
    fi

    printf "[%2d/%2d] %-30s ... " "$DONE" "$TOTAL" "$cls"

    ITEM_START=$(date +%s)

    # BDN 内部 launchCount=5，加超时保护
    timeout "$TIMEOUT_SEC" dotnet run -c Release -f "$FRAMEWORK" --no-build -- "$cls" \
        > "$log_file" 2>&1
    EXIT_CODE=$?

    ITEM_END=$(date +%s)
    ITEM_TIME=$((ITEM_END - ITEM_START))

    # 记录用时和退出码到日志末尾
    echo "ELAPSED:${ITEM_TIME}s EXIT_CODE:${EXIT_CODE}" >> "$log_file"

    if [ $EXIT_CODE -eq 124 ]; then
        # timeout 命令返回 124 = 超时
        echo "TIMEOUT (${TIMEOUT_SEC}s)"
        echo "${cls},TIMEOUT,${ITEM_TIME}" >> "$SUMMARY_FILE"
        FAILED=$((FAILED + 1))
        continue
    fi

    # 提取 Mean
    MEAN=$(grep -oP "Mean\s*=\s*\K[\d.]+" "$log_file" | tail -1)
    if [ -n "$MEAN" ]; then
        echo "${MEAN}ns  [${ITEM_TIME}s]"
        echo "${cls},${MEAN},${ITEM_TIME}" >> "$SUMMARY_FILE"
    else
        echo "FAILED  [${ITEM_TIME}s]"
        echo "${cls},FAILED,${ITEM_TIME}" >> "$SUMMARY_FILE"
        FAILED=$((FAILED + 1))
    fi
done

END_TIME=$(date +%s)
TOTAL_TIME=$((END_TIME - START_TIME))

echo ""
echo "=========================================="
echo "  Total: ${TOTAL} benchmarks, ${TOTAL_TIME}s ($(( TOTAL_TIME / 60 ))m$(( TOTAL_TIME % 60 ))s)"
echo "  Skipped: ${SKIPPED}, Failed: ${FAILED}"
echo "  Logs:  $OUTPUT_DIR/"
echo "  CSV:   $SUMMARY_FILE"
echo "=========================================="
echo ""

if [ $FAILED -gt 0 ]; then
    echo "WARNING: $FAILED benchmark(s) failed or timed out."
    exit 3
fi
