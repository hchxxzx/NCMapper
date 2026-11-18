#!/usr/bin/env bash
# =====================================================================
# check-doc-codeblock-bind.sh — 文档代码示例「机器绑定率」对账（告警版，恒 0）
#
# 目的（门禁洞 3）：
#   check-doc-sample-refs.sh 只校验已存在的 DFM 引用（[!code-csharp[...](~/samples/...#Region)]）
#   是否合法，管不到「页面自己手抄的裸代码块」——后者与 samples 源码无任何绑定，
#   改源码不会让文档报错，属示例保真盲区。本脚本把绑定率与完全未绑定页面量化出来。
#
# 口径：
#   DFM 引用数   = 页面内 `[!code-csharp` 出现次数（机器绑定，随源码变化）
#   裸代码块数   = 页面内 ```csharp 围栏开标记数（手抄，无绑定）
#   绑定率       = DFM / (DFM + 裸块)
#   完全未绑定页 = 裸块 > 0 且 DFM = 0（风险最高：示例全靠人工同步）
#
# 范围：仅 docs/zh-CN（中文为母版；其他语言由维护者自行翻译，差异不在本审计范围 X-A/X-B）
# 输出：[info] 汇总 + 绑定率为 0 的页面清单 + 绑定率最低 Top N
# 退出码：0（告警版；手抄块不必然错误，接入拦截前需先定口径）
# 用法（仓库根执行）：./scripts/check-doc-codeblock-bind.sh [--exempt-file <豁免页路径>]
#   --exempt-file（已启用）：消费豁免页 docs/zh-CN/reference/samples-exemptions.md，
#     按「页面相对路径|块首行前 16 字符」**内容锚定**匹配裸块（与 check-block-buckets.py 的
#     DISPOSITIONS_B4 同锚定式，**禁按行号**——行号随编辑漂移会静默失配＝豁免漏计）。
#     仅新增观测列（豁免命中数／登记未命中数），**不改变 DFM／裸块主读数与退出码**；
#     不传该参数＝行为与历史逐字一致。
# =====================================================================

set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
docs_dir="$repo_root/docs/zh-CN"

EXEMPT_FILE=""
if [ "${1:-}" = "--exempt-file" ]; then
    EXEMPT_FILE="${2:-}"
    if [ -z "$EXEMPT_FILE" ] || [ ! -f "$EXEMPT_FILE" ]; then
        echo "[warn] --exempt-file 缺参或豁免页不存在（收到：'${EXEMPT_FILE}'）——本次按无豁免消费，主读数不受影响"
        EXEMPT_FILE=""
    fi
fi

if [ ! -d "$docs_dir" ]; then
    echo "[warn] 前置缺失：$docs_dir 不存在"
    exit 0
fi

total_dfm=0
total_bare=0
pages=0
unbound_pages=0

detail="$(mktemp)"
trap 'rm -f "$detail"' EXIT

# --exempt-file 消费：豁免页每行格式
#   | N | 所在页[:行] | 块首行内容 | 判据 | 结论 |
# 消费锚＝「页面相对路径|块首行前 16 字符」（弃行号）；与 check-block-buckets.py 同式。
exempt_keys="$(mktemp)"
bare_heads="$(mktemp)"
trap 'rm -f "$detail" "$exempt_keys" "$bare_heads"' EXIT
if [ -n "$EXEMPT_FILE" ]; then
    awk -F'|' '/^\| *[0-9]+ *\|/ {
        page=$3; head=$4
        sub(/^ +/,"",page); sub(/ +$/,"",page)
        sub(/:[0-9]+$/,"",page)                     # 弃行号（漂移静默失配的根源）
        gsub(/^ +| +$/,"",head)
        if (page=="" || head=="") next
        print page "|" substr(head,1,16)
    }' "$EXEMPT_FILE" | sort -u > "$exempt_keys"
fi

while IFS= read -r f; do
    rel="${f#"$docs_dir"/}"
    dfm=$(grep -c '\[!code-csharp' "$f" || true)
    bare=$(grep -c '^[[:space:]]*```csharp' "$f" || true)

    if [ -n "$EXEMPT_FILE" ] && [ "$bare" -gt 0 ]; then
        # 围栏后第一个非空行去前导空白取前 16 字符＝与豁免页「块首行」同口径
        awk -v rel="$rel" '/^[[:space:]]*```csharp/ {inc=1; next}
            inc { line=$0; gsub(/^[ \t\r]+|[ \t\r]+$/,"",line); if (line=="") next
                  print rel "|" substr(line,1,16); inc=0 }' "$f" >> "$bare_heads"
    fi

    [ "$dfm" -eq 0 ] && [ "$bare" -eq 0 ] && continue

    pages=$((pages + 1))
    total_dfm=$((total_dfm + dfm))
    total_bare=$((total_bare + bare))

    if [ "$dfm" -eq 0 ] && [ "$bare" -gt 0 ]; then
        unbound_pages=$((unbound_pages + 1))
        echo "UNBOUND|$rel|$dfm|$bare" >> "$detail"
    else
        echo "BOUND|$rel|$dfm|$bare" >> "$detail"
    fi
done < <(find "$docs_dir" -name '*.md' | sort)

sum=$((total_dfm + total_bare))
if [ "$sum" -gt 0 ]; then
    rate=$(awk -v d="$total_dfm" -v s="$sum" 'BEGIN { printf "%.1f", d * 100 / s }')
else
    rate="NA"
fi

echo "=== 文档代码示例绑定率（docs/zh-CN）==="
echo "[info] 含代码示例页面：$pages"
echo "[info] DFM 机器绑定引用：$total_dfm 处"
echo "[info] 裸代码块（手抄）：$total_bare 处"
echo "[info] 绑定率：${rate}%"
echo "[info] 完全未绑定页面（有手抄块、零 DFM）：$unbound_pages 个"

if [ "$unbound_pages" -gt 0 ]; then
    echo "=== 完全未绑定页面清单（示例保真风险最高）==="
    grep '^UNBOUND|' "$detail" | awk -F'|' '{ printf "  %-52s 手抄块 %s\n", $2, $4 }'
fi

if [ -n "$EXEMPT_FILE" ]; then
    sort -u "$bare_heads" -o "$bare_heads"
    n_reg=$(wc -l < "$exempt_keys" | tr -d ' ')
    hits=$(comm -12 "$exempt_keys" "$bare_heads" | wc -l | tr -d ' ')
    reg_miss=$(comm -23 "$exempt_keys" "$bare_heads" | wc -l | tr -d ' ')
    echo "=== 豁免页消费（--exempt-file，内容锚定 file|块首行前 16 字符，弃行号）==="
    echo "[info] 豁免登记：$n_reg 条 ｜ 命中裸块：$hits ｜ 登记未命中（已转/已删/首行变更）：$reg_miss"
    echo "[info] 口径注：登记条数与唯一锚可不等——同页同首行多块（occurrence）共享一锚（如 constructors-and-types.md 三条「// 生成代码」）；"
    echo "         本列按唯一锚判定「锚是否仍有实存块」，条数对账仍以 check-block-buckets.py 的 B3 实测为准。"
    if [ "$reg_miss" -gt 0 ]; then
        echo "[warn] 以下登记锚在当前字节无匹配块——豁免上限口径须人工核对："
        comm -23 "$exempt_keys" "$bare_heads" | sed 's/^/  /'
    fi
fi

echo "=== 说明 ==="
echo "[info] 手抄块不必然错误；本读数用于确定「哪些页面的示例需要人工/编译核验」的抽样范围"
exit 0
