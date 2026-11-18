#!/usr/bin/env bash
# ================================================================
# NCMapper 后3 (SELF 3 变体) 专属基线构建脚本
# ----------------------------------------------------------------
# 仅跑 baseline_self (自己组件 3 变体: Ext/NCMapper/Runtime
#   + DeepCopy 的 Generated/Manual/Runtime = 35 类), 单进程独占跑完,
# 作为"今后回归基线 (层1)"的独立测量, 不复用前7 (full) 部分。
# 复用 run_baseline.sh 的解析 (parse_pass) 与 gen_report.py 报告生成。
# 为兼容监护任务与报告生成器, 仍产出 full_median.csv / compare_7vs3.csv
#   的表头占位文件 (内容为 0 类), 避免读取缺失报错。
# 用法: ./run_self_baseline.sh [framework] [repeats]
# 示例: ./run_self_baseline.sh net10.0 3
# 产物: artifacts/benchmarks/baseline/<framework>_<时间戳>/
#       ├─ self_median.csv        (主产物, 层1 回归基线)
#       ├─ full_median.csv        (表头占位, 本脚本不跑 full)
#       ├─ compare_7vs3.csv       (表头占位, 本脚本不跑 full)
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

DESC="${BASELINE_DESC:-NCMapper ${FRAMEWORK} 后3 (SELF 3变体) 专属基线}"
COMMIT="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)"
HOST="$(hostname 2>/dev/null || echo unknown)"
DOTNET_VER="$(dotnet --version 2>/dev/null || echo unknown)"
START_TS="$(date '+%Y-%m-%d %H:%M:%S')"

cat > "$RUNDIR/meta.json" <<EOF
{
  "description": "$DESC",
  "framework": "$FRAMEWORK",
  "repeats": $REPEATS,
  "mode": "self_only",
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
echo "  NCMapper 后3 (SELF) 专属基线构建"
echo "  框架: $FRAMEWORK | 外部重复: $REPEATS 次"
echo "  任务说明: $DESC"
echo "  commit: $COMMIT | dotnet: $DOTNET_VER"
echo "  输出目录: $RUNDIR"
echo "=============================================="

# ---------- Step 0.5: 重建 BenchmarkDotNet 锁定模式拦截文件 ----------
# BenchmarkDotNet 自生成项目位于 bin/ 下，向上继承仓库根 Directory.Build.props
# （RestoreLockedMode=true），其 restore RID 与锁定文件不匹配 → NU1004（72 次 build
# failed 实测）。bin/ 为 gitignore 产物（CI 克隆后不存在），脚本运行时重建拦截文件。
BENCH_BIN_DIR="$(cd "$(dirname "$PROJ")" && pwd)/bin"
mkdir -p "$BENCH_BIN_DIR"
cat > "$BENCH_BIN_DIR/Directory.Build.props" <<'INTERCEPT_EOF'
<Project>
  <PropertyGroup>
    <!-- BenchmarkDotNet 自生成项目位于 bin/ 下，继承仓库根锁定模式（RestoreLockedMode=true）
         导致其 restore 报 NU1004（RID 与锁定文件不匹配）。此处拦截禁用锁定，
         仅影响基准运行时的自生成项目（差异 #16）。 -->
    <RestoreLockedMode>false</RestoreLockedMode>
    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
  </PropertyGroup>
</Project>
INTERCEPT_EOF
echo ">>> [0.5/3] BenchmarkDotNet 锁定拦截文件已就位: $BENCH_BIN_DIR/Directory.Build.props"

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

# ---------- Step 2: 仅运行 baseline_self (后3) × REPEATS ----------
echo ">>> [2/3] 运行基线 (baseline_self 自己组件 3变体) × $REPEATS 次 ..."
if seq 1 1 >/dev/null 2>&1; then
  for r in $(seq 1 "$REPEATS"); do
    echo "  --- 第 $r/$REPEATS 次重复 ---"
    mkdir -p "$RUNDIR/run$r"
    echo "    [SELF 3变体] baseline_self ..."
    dotnet run --project "$PROJ" --no-build -c Release -f "$FRAMEWORK" -- baseline_self \
      > "$RUNDIR/run$r/self.log" 2>&1 || echo "    WARN: baseline_self 第$r次存在错误, 见日志"
  done
else
  # 环境兼容：Git Bash 缺 seq 时用 brace expansion 兜底
  for r in $(eval echo "{1..$REPEATS}"); do
    echo "  --- 第 $r/$REPEATS 次重复 (brace fallback) ---"
    mkdir -p "$RUNDIR/run$r"
    echo "    [SELF 3变体] baseline_self ..."
    dotnet run --project "$PROJ" --no-build -c Release -f "$FRAMEWORK" -- baseline_self \
      > "$RUNDIR/run$r/self.log" 2>&1 || echo "    WARN: baseline_self 第$r次存在错误, 见日志"
  done
fi
echo ""

# ---------- Step 3: 解析 + 中位数 + 兼容占位 ----------
echo ">>> [3/3] 解析日志, 计算 SELF 中位数, 生成兼容占位与报告 ..."

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

# 收集各次 self log 路径
SELF_LOGS=()
for r in $(seq 1 "$REPEATS"); do
  SELF_LOGS+=("$RUNDIR/run$r/self.log")
done

# parse_pass 写出无 header 的数据, 随后补 header 行
parse_pass self "${SELF_LOGS[@]}"
{ printf "class,median_mean_ns,repeat_count\n"; cat "$RUNDIR/self_median.csv"; } > "$RUNDIR/_tmp" && mv "$RUNDIR/_tmp" "$RUNDIR/self_median.csv"

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
json.dump(m, open(p, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
PY

# 生成带时间戳名称的 HTML 报告
echo ">>> 生成 HTML 报告 ..."
$PYTHON "$SCRIPT_DIR_W/gen_report.py" "$RUNDIR_W" || echo "    WARN: 报告生成失败, 见上方错误"

echo ""
echo "=============================================="
echo "  后3 (SELF) 基线构建完成"
echo "  SELF (3变体) 类数: $SELF_N  (预期 ~35)"
echo "  FULL (7变体) 类数: $FULL_N  (本脚本不跑, 占位)"
echo "  对比重叠类数:      $CMP_N  (本脚本不跑, 占位)"
echo "  运行目录: $RUNDIR"
echo "  产物: self_median.csv / full_median.csv(占位) / compare_7vs3.csv(占位) / meta.json"
echo "         NCMapper性能回归基线报告_${TS}.html"
echo "=============================================="

# 标记完成
rm -f "$RUNDIR/RUNNING"
echo "$END_TS" > "$RUNDIR/DONE"
