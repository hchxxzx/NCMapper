#!/usr/bin/env bash
# ================================================================
# NCMapper 竞品 (rivals) 独立冷启动基线构建脚本
# ----------------------------------------------------------------
# 仅跑 baseline_rivals (竞品变体: 标准场景 AutoMapper/Manual/Mapperly/Mapster
#   + DeepCopy 的 Json/MemoryPack/MessagePack/Manual), 单进程独占冷启动跑完,
# 作为"竞品差距"对比的独立测量。与 baseline_self (后3) 完全同协议:
#   冷启动 / 全局 SimpleJob / 相同 REPEATS, 故二者可直接横比。
# 复用 run_baseline.sh 的解析 (parse_pass) 与 gen_report.py 报告生成。
# 竞品数据写入 self_median.csv (供报告管线通用), 同时另存 rivals_median.csv 作为规范文件名。
# full_median.csv / compare_7vs3.csv 仅留表头占位 (本脚本不跑 full, 无 7 变体对比)。
# 用法: ./run_rivals_baseline.sh [framework] [repeats]
# 示例: ./run_rivals_baseline.sh net10.0 3
# 产物: artifacts/benchmarks/baseline/<framework>_<时间戳>/
#       ├─ self_median.csv        (竞品数据主体, 报告管线读取)
#       ├─ rivals_median.csv      (同数据规范文件名, 对比脚本用)
#       ├─ full_median.csv        (表头占位)
#       ├─ compare_7vs3.csv       (表头占位)
#       ├─ meta.json / RUNNING / DONE
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
# 脚本自身路径同样需转 Windows 原生路径, 否则 Windows 版 Python 会把 POSIX 前缀 /d/ 误判为 D:\d\... 而找不到文件。
SCRIPT_DIR_W="$(cygpath -w "$SCRIPT_DIR" 2>/dev/null || echo "$SCRIPT_DIR")"
# 供监护定时任务定位当前运行
echo "$RUNDIR" > "$REPO_ROOT/artifacts/benchmarks/baseline/latest.txt"

DESC="${BASELINE_DESC:-NCMapper ${FRAMEWORK} 竞品(rivals) 独立冷启动基线}"
COMMIT="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)"
HOST="$(hostname 2>/dev/null || echo unknown)"
DOTNET_VER="$(dotnet --version 2>/dev/null || echo unknown)"
START_TS="$(date '+%Y-%m-%d %H:%M:%S')"

cat > "$RUNDIR/meta.json" <<EOF
{
  "description": "$DESC",
  "framework": "$FRAMEWORK",
  "repeats": $REPEATS,
  "mode": "rivals_only",
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
echo "  NCMapper 竞品(rivals) 独立冷启动基线构建"
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

# ---------- Step 2: 仅运行 baseline_rivals (竞品变体) × REPEATS ----------
echo ">>> [2/3] 运行基线 (baseline_rivals 竞品变体) × $REPEATS 次 ..."
for r in $(seq 1 "$REPEATS"); do
  echo "  --- 第 $r/$REPEATS 次重复 ---"
  mkdir -p "$RUNDIR/run$r"
  echo "    [竞品变体] baseline_rivals ..."
  dotnet run --project "$PROJ" --no-build -c Release -f "$FRAMEWORK" -- baseline_rivals \
    > "$RUNDIR/run$r/self.log" 2>&1 || echo "    WARN: baseline_rivals 第$r次存在错误, 见日志"
done
echo ""

# ---------- Step 3: 解析 + 中位数 + 兼容占位 ----------
echo ">>> [3/3] 解析日志, 计算竞品中位数, 生成兼容占位与报告 ..."

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

# 收集各次 self log 路径 (竞品数据亦写 self_median.csv, 供报告管线通用)
SELF_LOGS=()
for r in $(seq 1 "$REPEATS"); do
  SELF_LOGS+=("$RUNDIR/run$r/self.log")
done

# parse_pass 写出无 header 的数据, 随后补 header 行
parse_pass self "${SELF_LOGS[@]}"
{ printf "class,median_mean_ns,repeat_count\n"; cat "$RUNDIR/self_median.csv"; } > "$RUNDIR/_tmp" && mv "$RUNDIR/_tmp" "$RUNDIR/self_median.csv"

# 规范文件名: 竞品数据另存 rivals_median.csv (与 self_median.csv 内容一致)
cp "$RUNDIR/self_median.csv" "$RUNDIR/rivals_median.csv"

# 兼容占位 (本脚本不跑 full, 故 full/compare 仅留表头, 供报告与监护读取不报错)
printf "class,median_mean_ns,repeat_count\n" > "$RUNDIR/full_median.csv"
printf "class,mean_7variant_ns,mean_3variant_ns,ratio_3over7\n" > "$RUNDIR/compare_7vs3.csv"

# 统计
FULL_N=0
SELF_N=$(($(wc -l < "$RUNDIR/self_median.csv") - 1))
CMP_N=0
END_TS="$(date '+%Y-%m-%d %H:%M:%S')"

# 将结束时间补写进 meta.json (保持 JSON 合法, 非致命)
$PYTHON - "$RUNDIR_W/meta.json" "$END_TS" "$SELF_N" <<'PY' || { echo "    WARN: meta.json 更新失败 (非致命)"; }
import json, sys
p, end, self_n = sys.argv[1], sys.argv[2], sys.argv[3]
try:
    m = json.load(open(p, encoding='utf-8'))
except Exception:
    m = {}
m['endTime'] = end
m['fullClassCount'] = 0
m['selfClassCount'] = int(self_n)
m['rivalsClassCount'] = int(self_n)
json.dump(m, open(p, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
PY

# 生成带时间戳名称的 HTML 报告
echo ">>> 生成 HTML 报告 ..."
$PYTHON "$SCRIPT_DIR_W/gen_report.py" "$RUNDIR_W" || echo "    WARN: 报告生成失败, 见上方错误"

echo ""
echo "=============================================="
echo "  竞品(rivals) 独立冷启动基线构建完成"
echo "  竞品变体类数: $SELF_N"
echo "  FULL (7变体) 类数: $FULL_N  (本脚本不跑, 占位)"
echo "  对比重叠类数:      $CMP_N  (本脚本不跑, 占位)"
echo "  运行目录: $RUNDIR"
echo "  产物: self_median.csv / rivals_median.csv / full_median.csv(占位) / compare_7vs3.csv(占位) / meta.json"
echo "         NCMapper性能回归基线报告_${TS}.html"
echo "=============================================="

# 标记完成
rm -f "$RUNDIR/RUNNING"
echo "$END_TS" > "$RUNDIR/DONE"
