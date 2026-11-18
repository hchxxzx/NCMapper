#!/usr/bin/env bash
# ================================================================
# run_load.sh — NCMapper.LoadBench 高峰负载压测运行脚本
# ----------------------------------------------------------------
# 场景：coldstart（冷启动）/ concurrency（高并发）/ endurance（持久）/ reuse（MapListInto 复用）
# 产物：artifacts/load_run/<ts>/ 下
#   load_<tfm>.log（运行日志）、load_summary.json、endurance.csv、
#   concurrency_<workload>.csv、reuse.csv、DONE 标记
# 用法: ./benchmarks/scripts/load/run_load.sh [--tfm net8.0] [--scenario all]
#   --tfm      目标框架（net8.0/net9.0/net10.0），可逗号分隔；默认全部
#   --scenario  场景（all|coldstart|concurrency|endurance|reuse），默认 all
#   --duration  持久场景时长（秒），默认 60
#   --gc-mode   设置 DOTNET_GCDynamicAdaptationMode（0=关 DATAS / 1=开），默认不设置
# ================================================================

set -e

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
TS="$(date +%Y%m%d_%H%M%S)"
OUT_DIR="$REPO/artifacts/load_run/$TS"
PROJ="$REPO/benchmarks/NCMapper.LoadBench/NCMapper.LoadBench.csproj"

# 参数解析
TFMS="net8.0,net9.0,net10.0"
SCENARIO="all"
DURATION="60"
GC_MODE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --tfm)      TFMS="$2"; shift 2 ;;
    --scenario) SCENARIO="$2"; shift 2 ;;
    --duration) DURATION="$2"; shift 2 ;;
    --gc-mode)  GC_MODE="$2"; shift 2 ;;
    *) echo "未知参数: $1" >&2; exit 1 ;;
  esac
done

mkdir -p "$OUT_DIR"
# 转换为 Windows 风格路径（避免 MSYS 路径解析到盘符根下 / MSBuild 开关误解析）
OUT_WIN="$(cygpath -w "$OUT_DIR" 2>/dev/null || echo "$OUT_DIR")"
PROJ_WIN="$(cygpath -w "$PROJ" 2>/dev/null || echo "$PROJ")"
echo "run_load.sh | ts=$TS | tfm=$TFMS | scenario=$SCENARIO | duration=$DURATION"
echo "输出目录: $OUT_WIN"
if [ -n "$GC_MODE" ]; then
  export DOTNET_GCDynamicAdaptationMode="$GC_MODE"
  echo "GC 模式: DOTNET_GCDynamicAdaptationMode=$GC_MODE（DATAS $([ "$GC_MODE" = "0" ] && echo 关闭 || echo 开启)）"
fi

# 多 TFM 逐 TFM 分目录（目录名含 TFM，杜绝 summary 互相覆盖）。
# 单 TFM 时产物直接落根目录；多 TFM 时各落 $OUT_DIR/<tfm>/，根目录仅聚合日志。
IFS=',' read -ra TFM_ARRAY <<< "$TFMS"
if [ "${#TFM_ARRAY[@]}" -gt 1 ]; then
  echo "多 TFM 模式：每个 TFM 独立子目录（$OUT_DIR/<tfm>/）"
fi
for tfm in "${TFM_ARRAY[@]}"; do
  TFM_OUT_DIR="$OUT_DIR"
  if [ "${#TFM_ARRAY[@]}" -gt 1 ]; then
    TFM_OUT_DIR="$OUT_DIR/$tfm"
    mkdir -p "$TFM_OUT_DIR"
  fi
  TFM_OUT_WIN="$(cygpath -w "$TFM_OUT_DIR" 2>/dev/null || echo "$TFM_OUT_DIR")"
  echo "==> 构建 $tfm（scenario=$SCENARIO）..."
  dotnet build "$PROJ_WIN" -c Release -f "$tfm" --nologo -v q > "$TFM_OUT_DIR/build_${tfm}.log" 2>&1 || { echo "    构建失败，见 build_${tfm}.log" >&2; exit 1; }
  echo "==> 运行 $tfm ..."
  "$REPO/benchmarks/NCMapper.LoadBench/bin/Release/$tfm/NCMapper.LoadBench.exe" \
    --scenario "$SCENARIO" --duration "$DURATION" --out "$TFM_OUT_WIN" \
    > "$TFM_OUT_DIR/load_${tfm}.log" 2>&1
  echo "    $tfm 完成（exit=$?，产物目录 $TFM_OUT_DIR）"
done

# 完整性校验：summary + 三场景产物（多 TFM 时逐子目录校验）
check() {
  [ -f "$1" ] && echo "  [OK] $1" || { echo "  [缺失] $1" >&2; return 1; }
}
OK=1
TARGET_DIRS=("$OUT_DIR")
if [ "${#TFM_ARRAY[@]}" -gt 1 ]; then
  TARGET_DIRS=()
  for tfm in "${TFM_ARRAY[@]}"; do
    TARGET_DIRS+=("$OUT_DIR/$tfm")
  done
fi
for TD in "${TARGET_DIRS[@]}"; do
  check "$TD/load_summary.json" || OK=0
  case "$SCENARIO" in
    all|coldstart)  grep -q '"coldStart"' "$TD/load_summary.json" || { echo "  [缺失] $TD coldStart 数据" >&2; OK=0; } ;;
  esac
  case "$SCENARIO" in
    all|concurrency) check "$TD/concurrency_Flat_Map.csv" || OK=0 ;;
  esac
  case "$SCENARIO" in
    all|endurance) check "$TD/endurance.csv" || OK=0 ;;
  esac
  case "$SCENARIO" in
    all|reuse) check "$TD/reuse.csv" || OK=0 ;;
  esac
done

if [ "$OK" = "1" ]; then
  touch "$OUT_DIR/DONE"
  echo "==> 全部完成: STATUS=DONE DIR=$OUT_DIR"
else
  echo "==> 产物不完整，未写 DONE 标记" >&2
  exit 1
fi
