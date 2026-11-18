#!/usr/bin/env bash
# run-demo-all.sh — NCMapper Samples 全场景批量驱动 + 错误汇总（2026-08-30）
#
# 目的：为"AI/CI 自动捕捉 demo 错误"提供机器可判定的驱动入口——
#   - 逐场景运行 demo（--scenario N，非交互），每场景 stdout+stderr 落盘 temp/demo/
#   - 依赖 OutputHelper.PrintErrorSummary 的失败信号（有错误时进程 exit 1）
#   - 汇总通过/失败场景清单，任一失败整体 exit 非 0
#
# 用法（仓库根执行）：
#   ./scripts/run-demo-all.sh
# 产物：
#   temp/demo/scenario-NN.log  （各场景完整输出，含 Error Summary）
#   temp/demo/SUMMARY.txt      （汇总：通过/失败清单）
# 退出码：全部通过 0；任一失败 1（供 AI/CI 以 $? 判定）
#
# 失败后 AI 闭环：读 scenario-NN.log 的 Error Summary → 定位源码 → 修复
# → 重新构建 → 重跑本脚本验证（本脚本自动先 build samples，--no-build 复用产物）。

set -u
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/temp/demo"
mkdir -p "$OUT"

echo "=== [1/2] 构建 Samples ==="
(cd "$ROOT" && dotnet build samples/NCMapper.Samples -p:EnableSourceLink=false --nologo -v q) || {
    echo "构建失败，中止"; exit 1; }

echo "=== [2/2] 逐场景运行（场景清单从 ScenarioRegistry.cs 动态提取）==="
# 2026-10-29 修：原为硬编码 11 个场景号（201 301 401 501 601 602 603 502 604 605 606），
# 与注册表 23 个场景脱节，缺 101-109/202/503/607 共 12 个——"全场景驱动"实际只覆盖 48%。
# 改为注册表动态提取：注册表新增场景即自动纳入，无需同步维护本清单。
REGISTRY="$ROOT/samples/NCMapper.Samples/ScenarioRegistry.cs"
scenario_ids="$(grep -oE 'new\([0-9]{3},' "$REGISTRY" 2>/dev/null | grep -oE '[0-9]{3}' | sort -n)"
if [ -z "$scenario_ids" ]; then
    echo "[warn] 场景清单解析失败：未从 $REGISTRY 提取到场景号（防假全量，中止）"
    exit 2
fi
id_count=$(printf '%s\n' "$scenario_ids" | wc -l | tr -d ' ')
echo "[info] 注册表场景数：$id_count"

pass=0; fail=0; failed_list=""
for i in $scenario_ids; do
    num=$(printf %03d "$i")
    log="$OUT/scenario-$num.log"
    (cd "$ROOT" && dotnet run --project samples/NCMapper.Samples --no-build -p:EnableSourceLink=false -- --scenario "$i") > "$log" 2>&1
    rc=$?
    if [ "$rc" -eq 0 ]; then
        pass=$((pass+1)); echo "场景 $i: 通过"
    else
        fail=$((fail+1)); failed_list="$failed_list $i"; echo "场景 $i: 失败 (exit=$rc) → temp/demo/scenario-$num.log"
    fi
done

{
    echo "NCMapper Samples 全场景驱动汇总（$(date '+%Y-%m-%d %H:%M:%S')）"
    echo "通过: $pass / 失败: $fail"
    if [ -n "$failed_list" ]; then
        echo "失败场景:$failed_list （日志见 temp/demo/scenario-*.log）"
    else
        echo "全部场景通过"
    fi
} > "$OUT/SUMMARY.txt"
echo "=== 汇总: 通过 $pass / 失败 $fail（详见 temp/demo/SUMMARY.txt）==="
[ "$fail" -eq 0 ]
