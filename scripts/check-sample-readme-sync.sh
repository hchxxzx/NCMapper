#!/usr/bin/env bash
# =====================================================================
# check-sample-readme-sync.sh — samples README 场景声明 ↔ ScenarioRegistry 对账
#                                （拦截版：任一断言失败退出码 1）
#
# 目的（门禁洞 1）：
#   既有 check-api-demo-coverage.sh 查③ 以 docs/zh-CN/reference/architecture-map.md
#   为数据源做「场景集合相等」，不覆盖 samples/NCMapper.Samples/README.md 的
#   场景总数声明与「场景域目录」表——导致 README 写「22 个场景」而注册表实为 23、
#   且域表漏登 109/202 之类漂移零告警（2026-10-29 基线实测）。
#
# 口径：
#   注册集 = ScenarioRegistry.cs 中 `new(<3 位场景号>, "<名称>"` 的 ID 集合
#   README  = samples/NCMapper.Samples/README.md
#             (a) 首段总数声明：正则 `N 个场景`
#             (b) 场景号表：**逐表判定**（v2 加固）——每张含
#                 「场景号」表头的表各自展开场景号列并与注册集对账，任一张
#                 表有 MISSING/GHOST 即失败；不再对多表取并集（并集会掩盖
#                 第二张表的漏登/错登）
#
# 断言（任一失败 → 退出码 1）：
#   ① COUNT_MISMATCH     声明总数 ≠ 注册集大小
#   ② MISSING_IN_README  注册有、某张表未列（漏登；逐表报告）
#   ③ GHOST_IN_README    某张表有、注册集无（幽灵引用；逐表报告）
#   ④ RESIDUAL_PATTERN   已知漂移形态残留（`101-108` 字面 / 孤立 `201` 单元格；
#                        2026-10-29 漂移回归防护——未来注册表结构变更时依据
#                        本注释解除该段）
#
# 输出：[ok]/[warn]/[info] 行；失败时逐条打印差异。
# 退出码：0=一致  1=存在差异  2=前置缺失（解析到 0 个注册场景 = 假门禁防护）
# 用法（仓库根执行）：./scripts/check-sample-readme-sync.sh
# =====================================================================

set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

registry="$repo_root/samples/NCMapper.Samples/ScenarioRegistry.cs"
readme="$repo_root/samples/NCMapper.Samples/README.md"

for f in "$registry" "$readme"; do
    if [ ! -f "$f" ]; then
        echo "[warn] 前置缺失：$f 不存在"
        exit 2
    fi
done

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# ---------- 注册集 ----------
grep -oE 'new\([0-9]{3},' "$registry" \
    | grep -oE '[0-9]{3}' | sort -u > "$work/registered.txt"

reg_count=$(wc -l < "$work/registered.txt" | tr -d ' ')
if [ "$reg_count" -eq 0 ]; then
    echo "[warn] 前置缺失：未从 ScenarioRegistry.cs 解析到任何场景号（解析规则失效，请核对源文件格式）"
    exit 2
fi

# ---------- README 声明总数 ----------
# 先排除「N 个场景域」形态（域计数，非场景总数），再取首个真总数；回退取最后一个匹配
declared=$(grep -oE '[0-9]+ *个场景[^域]' "$readme" | head -1 | grep -oE '^[0-9]+')
if [ -z "${declared:-}" ]; then
    declared=$(grep -oE '[0-9]+ *个场景' "$readme" | tail -1 | grep -oE '^[0-9]+')
fi
if [ -z "${declared:-}" ]; then
    echo "[warn] COUNT_UNPARSED：README 未找到「N 个场景」总数声明（口径可能已改，请核对文案）"
    declared="NA"
fi

# ---------- README 场景号表（逐表提取） ----------
# 每张含「场景号」表头的 markdown 表单独成段；输出 "T<n> <单元格>" 行。
# 第 2 列为场景号列；支持区间与逗号分隔；`—`（占位域）不匹配正则自然跳过。
# mawk 可移植性修复（解析语义不变）：原嵌套量词正则
#   /^[0-9]{3}(-[0-9]{3})?(,[0-9]{3}(-[0-9]{3})?)*$/ 在 mawk 1.3.4 触发
#   「REcompile() - panic: values still on machine stack」（Linux 实证 RC=100、表 0 张）；
#   换等价函数判定＝逗号 split 后逐段验「3 位号」或「3 位号-3 位号」（两枚单层正则），
#   与 ERE 逐字符等价、无嵌套量词、无 {n} 区间，gawk/mawk 同一实现不分叉。
awk -F'|' '
    function seg_ok(s) {
        return (s ~ /^[0-9][0-9][0-9]$/ || s ~ /^[0-9][0-9][0-9]-[0-9][0-9][0-9]$/)
    }
    function cell_ok(col,   nn, arr, j) {
        nn = split(col, arr, ",")
        if (nn == 0) return 0
        for (j = 1; j <= nn; j++) if (!seg_ok(arr[j])) return 0
        return 1
    }
    /^[[:space:]]*\|/ {
        col = $3
        gsub(/[[:space:]]/, "", col)
        if (col == "场景号") {        # 表头行 → 开新表（第 2 列表头文字恰为「场景号」）
            intable = 1
            tn++
            next
        }
        if (intable && cell_ok(col))
            print "T" tn, col
        next
    }
    { intable = 0 }                   # 离开表格区（空行/正文行）→ 表结束
' "$readme" > "$work/tables.raw"

# 按表展开区间并落各自文件
: > "$work/tables.idx"
while read -r t cell; do
    [ -z "$cell" ] && continue
    if ! grep -qx "$t" "$work/tables.idx"; then
        echo "$t" >> "$work/tables.idx"
        : > "$work/table_$t.txt"
    fi
    IFS=',' read -ra parts <<< "$cell"
    for p in "${parts[@]}"; do
        if [[ "$p" == *-* ]]; then
            lo="${p%%-*}"; hi="${p##*-}"
            for ((n = 10#$lo; n <= 10#$hi; n++)); do printf '%03d\n' "$n" >> "$work/table_$t.txt"; done
        else
            printf '%s\n' "$p" >> "$work/table_$t.txt"
        fi
    done
done < "$work/tables.raw"
while read -r t; do
    sort -u "$work/table_$t.txt" -o "$work/table_$t.txt"
done < "$work/tables.idx"

tab_total=$(wc -l < "$work/tables.idx" | tr -d ' ')
sort -u "$work"/table_T*.txt 2>/dev/null | wc -l | tr -d ' ' > "$work/listed_count.txt"
listed_count=$(cat "$work/listed_count.txt")

# ---------- 对账 ----------
fail=0

echo "=== samples README 场景声明对账 ==="
echo "[info] 注册表实场景数：$reg_count"
echo "[info] README 声明总数：$declared"
echo "[info] README 场景号表张数：$tab_total（逐表判定）"
echo "[info] README 各表展开场景并集：$listed_count"

if [ "$declared" != "NA" ] && [ "$declared" -ne "$reg_count" ]; then
    echo "[warn] COUNT_MISMATCH：README 声明 $declared 个场景，注册表实为 $reg_count 个"
    fail=1
fi

if [ "$tab_total" -eq 0 ]; then
    echo "[warn] TABLE_UNPARSED：README 未解析到任何含「场景号」表头的表（表结构可能已改，请核对）"
    fail=1
fi

# 逐表对账：每张表各自与注册集全集一致
tab_ok=0
while read -r t; do
    t_miss=$(comm -23 "$work/registered.txt" "$work/table_$t.txt")
    t_ghost=$(comm -13 "$work/registered.txt" "$work/table_$t.txt")
    if [ -z "$t_miss" ] && [ -z "$t_ghost" ]; then
        tab_ok=$((tab_ok + 1))
        continue
    fi
    if [ -n "$t_miss" ]; then
        echo "[warn] MISSING_IN_README（表 $t）：注册有、该表未列——"
        while IFS= read -r id; do
            name=$(grep -oE "new\($id, \"[^\"]+\"" "$registry" | sed -E 's/^new\([0-9]+, "([^"]+)".*/\1/' | head -1)
            echo "         $id  ${name:-（名称未解析）}"
        done <<< "$t_miss"
        fail=1
    fi
    if [ -n "$t_ghost" ]; then
        echo "[warn] GHOST_IN_README（表 $t）：该表有、注册表无——"
        echo "$t_ghost" | sed 's/^/         /'
        fail=1
    fi
done < "$work/tables.idx"

# ---------- 已知漂移形态残留零容忍（断言④） ----------
residual_range=$(grep -n '101-108' "$readme" | head -5)
if [ -n "$residual_range" ]; then
    echo "[warn] RESIDUAL_PATTERN：README 残留已知漂移形态「101-108」（2026-10-29 漂移回归）——"
    echo "$residual_range" | sed 's/^/         /'
    fail=1
fi
residual_201=$(grep -nE '\|[[:space:]]*201[[:space:]]*\|' "$readme" | head -5)
if [ -n "$residual_201" ]; then
    echo "[warn] RESIDUAL_PATTERN：README 场景号单元格残留孤立「201」（2026-10-29 漂移回归）——"
    echo "$residual_201" | sed 's/^/         /'
    fail=1
fi

echo "=== 对账结果 ==="
if [ "$fail" -eq 0 ] && [ "$tab_ok" -eq "$tab_total" ]; then
    echo "[PASS] README 场景声明与注册表一致（总数 $reg_count，逐表一致 $tab_ok/$tab_total，残留 0）"
    exit 0
fi

echo "[FAIL] README 与注册表存在差异（见上方 warn 清单）"
exit 1
