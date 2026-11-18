#!/usr/bin/env bash
# =====================================================================
# check-demo-assertions.sh — demo 场景内建断言覆盖对账（告警版，默认恒 0）
#
# 目的（门禁洞 2）：
#   run-demo-all.sh 的失败信号依赖 OutputHelper.PrintErrorSummary 的退出码 1，
#   而该信号只反映「场景内是否调用过 OutputHelper.Error」。零 Error 调用的场景
#   对全量跑恒绿——无论输出是否正确。本脚本把该盲区机械化为可数清单。
#
# 口径：
#   扫描 samples/NCMapper.Samples/<域>/*Scenarios.cs；
#   每文件统计 OutputHelper.Error（断言）/ OutputHelper.Dump*（打印）/ Console.WriteLine。
#   场景号由 ScenarioRegistry.cs 的 `new(<id>, "<name>", <域>.<类>.Run)` 反查类名后映射到文件名。
#
# 判定：
#   assertion == 0  → [warn] ZERO_ASSERT：该文件对 run-demo-all.sh 恒绿，正确性不可判定
#   assertion > 0   → [ok]
#
# 输出：逐文件表 + 汇总（零断言文件数 / 覆盖场景号）
# 退出码：0=告警版恒 0；带 -Strict 时存在零断言文件 → 1
# 用法（仓库根执行）：./scripts/check-demo-assertions.sh [-Strict]
# =====================================================================

set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

strict=0
[ "${1:-}" = "-Strict" ] && strict=1

samples_dir="$repo_root/samples/NCMapper.Samples"
registry="$repo_root/samples/NCMapper.Samples/ScenarioRegistry.cs"

if [ ! -d "$samples_dir" ]; then
    echo "[warn] 前置缺失：$samples_dir 不存在"
    exit 0
fi

# ---------- 类名 → 场景号（多场景可共用同一类，取首个登记号并收集全部） ----------
if [ -f "$registry" ]; then
    grep -oE 'new\([0-9]{3}, "[^"]+", [A-Za-z.]+\.[A-Za-z0-9_]+\.Run\)' "$registry" \
        | sed -E 's/^new\(([0-9]{3}), "([^"]+)", [A-Za-z.]+\.([A-Za-z0-9_]+)\.Run\)$/\1\t\3/' \
        | sort > /tmp/ncmap_scenario_class.tsv
else
    : > /tmp/ncmap_scenario_class.tsv
fi

zero_count=0
zero_ids=""
total=0

printf '%-42s %-10s %-6s %-6s %s\n' "文件" "场景号" "断言" "打印" "结论"
printf '%s\n' "------------------------------------------------------------------------------"

while IFS= read -r f; do
    base="$(basename "$f" .cs)"
    total=$((total + 1))

    assertions=$(grep -c 'OutputHelper\.Error' "$f" || true)
    dumps=$(grep -c 'OutputHelper\.Dump' "$f" || true)

    ids=$(awk -F'\t' -v c="$base" '$2 == c { printf "%s ", $1 }' /tmp/ncmap_scenario_class.tsv)
    [ -z "$ids" ] && ids="—"

    if [ "$assertions" -eq 0 ]; then
        # 方法库豁免（已确认，豁免项须逐条给理由）：
        # 无 Run 且无打印的 *Scenarios.cs 是纯静态方法库（如 501 三子文件 EFCore/FreeSql/FxScenarios
        # ——Run 统一在调度文件 ProjectionScenarios，断言由调度文件承载），无「打印点→断言对位」需求，
        # 不计入零断言（消除文件级口径假象）。有 Run 或有打印但 0 断言的文件仍是真缺口。
        run_count=$(grep -c 'static void Run' "$f" || true)
        if [ "$dumps" -eq 0 ] && [ "$run_count" -eq 0 ]; then
            printf '%-42s %-10s %-6s %-6s %s\n' "$base" "$ids" "$assertions" "$dumps" "[info] METHOD-LIB"
        else
            printf '%-42s %-10s %-6s %-6s %s\n' "$base" "$ids" "$assertions" "$dumps" "[warn] ZERO_ASSERT"
            zero_count=$((zero_count + 1))
            for i in $ids; do
                [ "$i" = "—" ] || zero_ids="$zero_ids $i"
            done
        fi
    else
        printf '%-42s %-10s %-6s %-6s %s\n' "$base" "$ids" "$assertions" "$dumps" "[ok]"
    fi
done < <(find "$samples_dir" -name '*Scenarios.cs' -not -path '*/obj/*' -not -path '*/bin/*' | sort)

echo "=== 汇总 ==="
echo "[info] 场景文件总数：$total；零断言文件：$zero_count"
if [ "$zero_count" -gt 0 ]; then
    echo "[warn] 零断言场景号（对 run-demo-all.sh 恒绿，正确性不可判定）：$zero_ids"
fi

if [ "$strict" -eq 1 ] && [ "$zero_count" -gt 0 ]; then
    echo "[FAIL] -Strict 模式：存在零断言场景文件"
    exit 1
fi

echo "[info] 告警版：退出码 0（接入 CI 拦截前先完成断言补齐）"
exit 0
