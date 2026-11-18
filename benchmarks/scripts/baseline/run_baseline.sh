#!/usr/bin/env bash
# ================================================================
# NCMapper 性能回归基线构建脚本（2026-07-07 定型）
# ----------------------------------------------------------------
# 设计: 先7后3 两次, 每遍单进程内顺序跑完, 确保 PGO 饱和广度一致
#   - baseline      (全 7 变体同场): 自己组件 Ext/NCMapper/Runtime
#                   + 竞品 Mapperly/Manual/Mapster/AutoMapper
#                   + DeepCopy 全 6 变体 = 80 类, 单进程一次跑完
#                   -> 竞品同场快照 + 7 变体数据 (层2)
#   - baseline_self (仅自己组件 3 变体): Ext/NCMapper/Runtime
#                   + DeepCopy 的 Generated/Manual/Runtime = 35 类
#                   单进程独占跑完 -> 今后回归基线 (层1)
# 两遍分进程: 今后回归只跑 baseline_self, 与基线自己组件部分 PGO
#            饱和广度一致, 消除 0707 vs 0706 的伪差漂移。
# 每遍外部重复 REPEATS(默认3)次, 取中位数, 抗离群。
# 排除: *Big / Quick_* / *_Layer* / ArrayMapping* / DictionaryMapping*
#       / ComposeMultiSource* / Update*
# 用法: ./run_baseline.sh [framework] [repeats]
# 示例: ./run_baseline.sh net10.0 3
# 自定义任务说明: BASELINE_DESC="Phase4后回归" ./run_baseline.sh net10.0 3
# 产物: artifacts/benchmarks/baseline/<framework>_<时间戳>/
#       ├─ full_median.csv / self_median.csv / compare_7vs3.csv
#       ├─ meta.json            (任务说明/时间/commit 等元数据)
#       ├─ RUNNING / DONE       (运行状态标记, 供监护任务判定)
#       └─ NCMapper性能回归基线报告_<时间戳>.html
# ================================================================
set -euo pipefail

# 仓库根 (脚本位于 <repo>/benchmarks/scripts/baseline/)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
cd "$REPO_ROOT"

# 禁用 MSBuild Server + 节点复用，避免子进程被沙箱拦截文件操作
export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
export MSBUILDDISABLENODEREUSE=1

PROJ="benchmarks/NCMapper.Benchmarks/NCMapper.Benchmarks.csproj"
FRAMEWORK="${1:-net10.0}"
REPEATS="${2:-3}"

# Python 解释器检测 (GitBash 下 python3 可能不在 PATH)
if command -v python3 >/dev/null 2>&1; then
  PYTHON=python3
elif command -v python >/dev/null 2>&1; then
  PYTHON=python
else
  PYTHON="python3"
fi

# ---------- 时间戳运行目录 + 元数据 ----------
TS="$(date +%Y%m%d_%H%M%S)"
RUNDIR="$REPO_ROOT/artifacts/benchmarks/baseline/${FRAMEWORK}_${TS}"
mkdir -p "$RUNDIR"
# Windows 版 Python 无法解析 GitBash 的 POSIX 路径 (/d/... -> 误判为 C:\d\...),
# 故把运行目录转换为 Windows 原生路径, 供后续 python 调用安全打开文件。
RUNDIR_W="$(cygpath -w "$RUNDIR" 2>/dev/null || echo "$RUNDIR")"
# 供监护定时任务定位当前运行
echo "$RUNDIR" > "$REPO_ROOT/artifacts/benchmarks/baseline/latest.txt"

DESC="${BASELINE_DESC:-NCMapper ${FRAMEWORK} 性能回归基线 (先7后3)}"
COMMIT="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)"
HOST="$(hostname 2>/dev/null || echo unknown)"
DOTNET_VER="$(dotnet --version 2>/dev/null || echo unknown)"
START_TS="$(date '+%Y-%m-%d %H:%M:%S')"

cat > "$RUNDIR/meta.json" <<EOF
{
  "description": "$DESC",
  "framework": "$FRAMEWORK",
  "repeats": $REPEATS,
  "commit": "$COMMIT",
  "hostname": "$HOST",
  "dotnetVersion": "$DOTNET_VER",
  "startTime": "$START_TS",
  "runDir": "$RUNDIR"
}
EOF

# 运行状态标记
echo "$START_TS" > "$RUNDIR/RUNNING"

echo "=============================================="
echo "  NCMapper 性能回归基线构建"
echo "  框架: $FRAMEWORK | 外部重复: $REPEATS 次"
echo "  任务说明: $DESC"
echo "  commit: $COMMIT | dotnet: $DOTNET_VER"
echo "  输出目录: $RUNDIR"
echo "=============================================="

# ---------- Step 1: 构建 (Release, 目标框架) ----------
echo ">>> [1/3] 构建基准项目 (Release / $FRAMEWORK) ..."
if ! dotnet build "$PROJ" -c Release -f "$FRAMEWORK" > "$RUNDIR/build.log" 2>&1; then
  echo "ERROR: 构建失败, 详见 $RUNDIR/build.log" >&2
  rm -f "$RUNDIR/RUNNING"
  exit 1
fi
tail -3 "$RUNDIR/build.log"
echo ""

# Defender 预扫描：强制 OS 加载全部 DLL，让实时扫描集中在非计时阶段
echo ">>> [prescan] Pre-loading binaries (Defender warm-up) ..."
dotnet run --project "$PROJ" --no-build -c Release -f "$FRAMEWORK" -- __prescan__ \
  > /dev/null 2>&1
sleep 10

# ---------- Step 2: 运行 baseline (先7后3) × REPEATS ----------
echo ">>> [2/3] 运行基线 (baseline 全量 + baseline_self 自己组件) × $REPEATS 次 ..."
for r in $(seq 1 "$REPEATS"); do
  echo "  --- 第 $r/$REPEATS 次重复 ---"
  mkdir -p "$RUNDIR/run$r"
  echo "    [FULL 7变体] baseline ..."
  dotnet run --project "$PROJ" --no-build -c Release -f "$FRAMEWORK" -- baseline \
    > "$RUNDIR/run$r/full.log" 2>&1 || echo "    WARN: baseline 第$r次存在错误, 见日志"
  echo "    [SELF 3变体] baseline_self ..."
  dotnet run --project "$PROJ" --no-build -c Release -f "$FRAMEWORK" -- baseline_self \
    > "$RUNDIR/run$r/self.log" 2>&1 || echo "    WARN: baseline_self 第$r次存在错误, 见日志"
done
echo ""

# ---------- Step 3: 解析 + 中位数 + 对比 ----------
echo ">>> [3/3] 解析日志, 计算中位数, 生成对比与报告 ..."

# awk 解析器: 对每个 log, 取每个类首个 "Mean = X ns" (在 "Running Class..." 之后),
# 跨多个 log 文件按类聚合, 排序取中位数。
parse_pass() {
  local pass="$1"; shift   # 剩余参数 = 该遍各次 log 文件
  awk '
  {
    if ($1 == "Running" && $2 ~ /^[A-Za-z0-9_]+\.\.\.$/) {
      cur = $2; sub(/\.+$/, "", cur); captured[cur] = 0; next
    }
    if (cur != "" && captured[cur] == 0 && $0 ~ /Mean[ =]/) {
      s = $0; sub(/.*Mean[ \t]*=[ \t]*/, "", s); sub(/[^0-9.].*/, "", s)
      if (s != "") { data[FILENAME, cur] = s + 0; captured[cur] = 1 }
      next
    }
  }
  END {
    for (k in data) { split(k, p, SUBSEP); c = p[2]; vals[c, ++cnt[c]] = data[k] }
    for (c in cnt) {
      m = cnt[c]
      for (i = 1; i <= m; i++) a[i] = vals[c, i]
      for (i = 1; i <= m; i++) for (j = i + 1; j <= m; j++) if (a[i] > a[j]) { t = a[i]; a[i] = a[j]; a[j] = t }
      med = (m % 2 == 1) ? a[(m + 1) / 2] : (a[m / 2] + a[m / 2 + 1]) / 2
      printf "%s,%.4f,%d\n", c, med, m
    }
  }' "$@" | sort > "$RUNDIR/${pass}_median.csv"
}

# 收集各次 log 路径
FULL_LOGS=(); SELF_LOGS=()
for r in $(seq 1 "$REPEATS"); do
  FULL_LOGS+=("$RUNDIR/run$r/full.log")
  SELF_LOGS+=("$RUNDIR/run$r/self.log")
done

# parse_pass 写出无 header 的数据, 随后补 header 行
parse_pass full "${FULL_LOGS[@]}"
{ printf "class,median_mean_ns,repeat_count\n"; cat "$RUNDIR/full_median.csv"; } > "$RUNDIR/_tmp" && mv "$RUNDIR/_tmp" "$RUNDIR/full_median.csv"

parse_pass self "${SELF_LOGS[@]}"
{ printf "class,median_mean_ns,repeat_count\n"; cat "$RUNDIR/self_median.csv"; } > "$RUNDIR/_tmp" && mv "$RUNDIR/_tmp" "$RUNDIR/self_median.csv"

# 对比: 7变体 vs 3变体 (重叠的自己组件类), 验证 PGO 饱和差异
printf "class,mean_7variant_ns,mean_3variant_ns,ratio_3over7\n" > "$RUNDIR/compare_7vs3.csv"
awk -F, '
  FNR == 1 { next }                      # 跳过 header
  NR == FNR { full[$1] = $2; next }      # 第一文件: full
  ($1 in full) {                         # 第二文件: self, 仅取与 full 重叠者
    fm = full[$1] + 0; sm = $2 + 0
    ratio = (fm > 0) ? sm / fm : 0
    printf "%s,%.4f,%.4f,%.4f\n", $1, fm, sm, ratio
  }
' "$RUNDIR/full_median.csv" "$RUNDIR/self_median.csv" | sort >> "$RUNDIR/compare_7vs3.csv"

# 统计
FULL_N=$(($(wc -l < "$RUNDIR/full_median.csv") - 1))
SELF_N=$(($(wc -l < "$RUNDIR/self_median.csv") - 1))
CMP_N=$(($(wc -l < "$RUNDIR/compare_7vs3.csv") - 1))
END_TS="$(date '+%Y-%m-%d %H:%M:%S')"

# 将结束时间补写进 meta.json (简单追加字段, 保持 JSON 合法)
$PYTHON - "$RUNDIR_W/meta.json" "$END_TS" <<'PY' || { echo "    WARN: meta.json 更新失败 (非致命)"; }
import json, sys
p, end = sys.argv[1], sys.argv[2]
try:
    m = json.load(open(p, encoding='utf-8'))
except Exception:
    m = {}
m['endTime'] = end
m['fullClassCount'] = None
m['selfClassCount'] = None
json.dump(m, open(p, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
PY

# 生成带时间戳名称的 HTML 报告
echo ">>> 生成 HTML 报告 ..."
$PYTHON "$SCRIPT_DIR/gen_report.py" "$RUNDIR_W" || echo "    WARN: 报告生成失败, 见上方错误"

echo ""
echo "=============================================="
echo "  基线构建完成"
echo "  FULL (7变体) 类数: $FULL_N  (预期 ~80)"
echo "  SELF (3变体) 类数: $SELF_N  (预期 ~35)"
echo "  对比重叠类数:      $CMP_N"
echo "  运行目录: $RUNDIR"
echo "  产物: full_median.csv / self_median.csv / compare_7vs3.csv / meta.json"
echo "         NCMapper性能回归基线报告_${TS}.html"
echo "=============================================="

# 标记完成
rm -f "$RUNDIR/RUNNING"
echo "$END_TS" > "$RUNDIR/DONE"
