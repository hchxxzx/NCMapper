#!/usr/bin/env bash
# check-diagnostics-sync.sh — 诊断码三方对账（docs.yml 步骤）
#
# 对账三源（键 = 规则 ID NCMAPnnn，按列语义解析而非位置解析）：
#   rules   = docs/zh-CN/diagnostics/rules.md 表格行（`| NCMAPnnn | 严重度 | 说明 |`）
#             严重度列规范三态：❌ Error / ⚠️ Warn / ℹ️ Info
#   shipped = src/NCMapper.SourceGen/AnalyzerReleases.Shipped.md
#             （`NCMAPnnn | 分类 | 严重度 | 备注`，严重度词 Warning/Error/Info）
#   unshipped = src/NCMapper.SourceGen/AnalyzerReleases.Unshipped.md（同上）
#   节感知：两文件仅计 `### New Rules` 节内行；`### Removed Rules`/`### Changed Rules`
#   （Roslyn 标准 release-tracking 节）不计有效登记——退役码（Shipped 历史 + Unshipped Removed）不误判 DUPRELEASE。
#
# 断言（三方对账口径）：
#   ① 规则 ID 集合相等：rules.md = shipped ∪ unshipped
#      （rules.md 缺码 → MISSING；rules.md 含幽灵码 → GHOST）
#   ② 每条严重度一致：rules.md 词（Error/Warn/Info）== 发布文件词（Error/Warning/Info）
#      （Warn ≡ Warning 归一化比对；不一致 → MISMATCH）
#   ③ rules.md 严重度单元格为规范三态（❌/⚠️/ℹ️ 符号 + 对应词；
#      变体选择符 U+FE0F 归一化后精确比对 → 占位符如 `?? Info` 判 BADSEV）
#   ④ 发布文件侧：同一 ID 不得在两文件的 `### New Rules` 节重复登记（DUPRELEASE）；
#      Roslyn 标准退役登记（Shipped 历史 + Unshipped `### Removed Rules`）不算重复；
#      rules.md 表格同一 ID 不得重复出现（DUPTABLE）
#   ⑤ 全索引句上界一致（三语 rules.md：头注「涉及诊断码」行 + 正文「诊断码全集」句）：
#      文中出现的 `NCMAP001<分隔>NNN` 区间上界必须 == 发布文件最大数字码（INDEXRANGE）。
#      背景：①-④ 只解析表格行，头注/正文索引句不在对账域——此缺口经确认后落
#      NCMAP047 时只改了正文全集句，三语头注仍停在 046（同一文件自相矛盾、门禁全绿未拦）。
#      口径：仅数字族 NCMAPnnn；NCMAP099 为兜底码不入顺序族（上界不计）；Fluent 族
#      （NCMAP_Fxx）三语既有漂移已登记，本条不判、不扩面。
#
# 口径说明：NCMAP004 已移除（与 NCMAP002 语义重复，Models.cs 注释明确），
#   三源均不含该码，属预期状态，不判缺失。
#
# 输出：差异清单（每条一行，前缀 MISSING/GHOST/MISMATCH/BADSEV/DUPRELEASE/DUPTABLE/INDEXRANGE）
# 用法（仓库根或任意目录均可）：
#   ./scripts/check-diagnostics-sync.sh
# 退出码：0=对账通过  1=不一致（门禁拦截）  2=前置缺失（任一源文件不存在）
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

rules_md="$repo_root/docs/zh-CN/diagnostics/rules.md"
rules_md_en="$repo_root/docs/en-US/diagnostics/rules.md"
shipped_md="$repo_root/src/NCMapper.SourceGen/AnalyzerReleases.Shipped.md"
unshipped_md="$repo_root/src/NCMapper.SourceGen/AnalyzerReleases.Unshipped.md"

for f in "$rules_md" "$shipped_md" "$unshipped_md"; do
  if [ ! -f "$f" ]; then
    echo "[ERROR] 对账源文件缺失：$f" >&2
    exit 2
  fi
done

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# --- 解析 rules.md 表格行 → id<TAB>严重度单元格 ---
# 表格行形如 `| NCMAP001 | ⚠️ Warn | 说明 |`；表头/分隔行/正文引用行（> 开头）均不匹配。
# 退役划线 `~~❌ Error~~`（对照 18 号页 :27 格式）：剥离 ~~ 后按三态校验。
awk -F'|' '
  /^[[:space:]]*\|[[:space:]]*NCMAP[0-9]+[[:space:]]*\|/ {
    id = $2; gsub(/[[:space:]]/, "", id)
    sev = $3; gsub(/^[[:space:]]+/, "", sev); gsub(/[[:space:]]+$/, "", sev); gsub(/~/, "", sev)
    print id "\t" sev
  }
' "$rules_md" > "$tmp/rules.tsv"

# --- 解析发布文件（Shipped/Unshipped）→ id<TAB>严重度词 ---
# 行形如 `NCMAP001 | NCMapper | Warning | notes`（首列 ID 顶格，区别于 rules.md 的 `|` 开头）。
# 节感知：仅 `### New Rules` 节内行计为有效登记；
# `### Removed Rules`（退役声明）/`### Changed Rules` 节行跳过，`## Release` 标题重置。
parse_release() {
  awk -F'|' '
    /^###[[:space:]]/ {
      inNew = 0
      if ($0 ~ /New Rules/) inNew = 1
      next
    }
    /^##[^#]/ { inNew = 0; next }
    inNew && /^NCMAP[0-9]+[[:space:]]*\|/ {
      id = $1; gsub(/[[:space:]]+/, "", id)
      sev = $3; gsub(/^[[:space:]]+/, "", sev); gsub(/[[:space:]]+$/, "", sev)
      print id "\t" sev
    }
  ' "$1"
}
parse_release "$shipped_md" > "$tmp/shipped.tsv"
parse_release "$unshipped_md" > "$tmp/unshipped.tsv"
cat "$tmp/shipped.tsv" "$tmp/unshipped.tsv" > "$tmp/release.tsv"

# --- 严重度词归一化：取空白分隔末词（单元格内形如 `⚠️ warn`），小写，warning→warn ---
# 注意：不能用 -F'\t'（整单元格是单 tab 字段），用 awk 默认空白分隔。
norm_word() { awk '{ w = tolower($NF); sub(/warning$/, "warn", w); print $1 "\t" w }'; }
norm_word < "$tmp/rules.tsv" > "$tmp/rules.word"
norm_word < "$tmp/release.tsv" > "$tmp/release.word"

problems=0

# --- ① ID 集合相等 ---
cut -f1 "$tmp/rules.tsv" | sort -u > "$tmp/rules.ids"
cut -f1 "$tmp/release.tsv" | sort -u > "$tmp/release.ids"
missing_in_rules="$(comm -23 "$tmp/release.ids" "$tmp/rules.ids")"
ghost_in_rules="$(comm -13 "$tmp/release.ids" "$tmp/rules.ids")"
if [ -n "$missing_in_rules" ]; then
  problems=1
  echo "[MISSING] 发布文件有、rules.md 无（补录规则行）："
  printf '%s\n' "$missing_in_rules"
fi
if [ -n "$ghost_in_rules" ]; then
  problems=1
  echo "[GHOST] rules.md 有、发布文件无（幽灵码，需从 AnalyzerReleases 移除或补登记）："
  printf '%s\n' "$ghost_in_rules"
fi

# --- ② 严重度一致性（交集 ID 逐条比对归一化词） ---
mismatch="$(awk -F'\t' '
  NR==FNR { rel[$1] = $2; next }
  ($1 in rel) && rel[$1] != $2 { print $1 " rules.md=" $2 " release=" rel[$1] }
' "$tmp/release.word" "$tmp/rules.word")"
if [ -n "$mismatch" ]; then
  problems=1
  echo "[MISMATCH] 严重度不一致（rules.md 词 vs 发布文件词）："
  printf '%s\n' "$mismatch"
fi

# --- ③ rules.md 严重度单元格规范三态（剥离变体选择符 U+FE0F 后精确比对） ---
badsev=""
while IFS="$(printf '\t')" read -r id cell; do
  cell_norm="$(printf '%s' "$cell" | sed 's/\xef\xb8\x8f//g')"
  case "$cell_norm" in
    "❌ Error" | "⚠ Warn" | "ℹ Info") : ;;
    *) badsev="${badsev}${id}: ${cell}"$'\n' ;;
  esac
done < "$tmp/rules.tsv"
if [ -n "$badsev" ]; then
  problems=1
  echo "[BADSEV] 严重度单元格非规范三态（❌ Error / ⚠️ Warn / ℹ️ Info；占位符/符号错误）："
  printf '%b' "$badsev"
fi

# --- ④ 重复登记检查 ---
dup_release="$(cut -f1 "$tmp/release.tsv" | sort | uniq -d)"
if [ -n "$dup_release" ]; then
  problems=1
  echo "[DUPRELEASE] Shipped 与 Unshipped 重复登记的规则 ID："
  printf '%s\n' "$dup_release"
fi
dup_table="$(cut -f1 "$tmp/rules.tsv" | sort | uniq -d)"
if [ -n "$dup_table" ]; then
  problems=1
  echo "[DUPTABLE] rules.md 表格中重复出现的规则 ID："
  printf '%s\n' "$dup_table"
fi

# --- ⑤ 全索引句上界一致（三语 rules.md：头注行 + 正文「诊断码全集」句） ---
# 期望上界 = 发布文件（Shipped ∪ Unshipped `### New Rules`）最大数字码；NCMAP099 为兜底码不计入。
# 判定面 = 文中每一处 `NCMAP001<分隔><数字>` 的上界（头注行与全集句各一处；表格行与
# `<NoWarn>NCMAP001</NoWarn>` 行因分隔符后非数字自动跳过）。awk 按字节匹配，`[^0-9A-Za-z]+`
# 覆盖 `-` / `–` / `～` 及反引号组合，不依赖 locale；`10#` 前缀防八进制误算（047 ≠ 39）。
expected_upper="$(cut -f1 "$tmp/release.ids" | sed 's/^NCMAP//' | { grep -v '^099$' || true; } | sort -n | tail -1)"
if [ -n "$expected_upper" ]; then
  expected_num="$((10#$expected_upper))"
  index_problems=""
  for f in "$rules_md" "$rules_md_en"; do
    [ -f "$f" ] || continue
    hits="$(awk '
      /NCMAP001/ {
        if (match($0, /NCMAP001[^0-9A-Za-z]+/)) {
          rest = substr($0, RSTART + RLENGTH)
          sub(/^NCMAP/, "", rest)
          if (match(rest, /^[0-9][0-9][0-9]?/)) print NR "\t" substr(rest, RSTART, RLENGTH) + 0
        }
      }
    ' "$f")"
    if [ -z "$hits" ]; then
      index_problems="${index_problems}${f#"$repo_root/"} 未找到 NCMAP001–NNN 全索引句（头注行 / 正文全集句至少各一处）"$'\n'
      continue
    fi
    while IFS="$(printf '\t')" read -r lineno found; do
      if [ "$found" -ne "$expected_num" ]; then
        index_problems="${index_problems}${f#"$repo_root/"}:${lineno} 记 NCMAP001–${found}，应为 NCMAP001–${expected_num}"$'\n'
      fi
    done <<< "$hits"
  done
  if [ -n "$index_problems" ]; then
    problems=1
    echo "[INDEXRANGE] rules.md 全索引句上界与发布文件最大码不符（头注行 + 正文全集句须同步）："
    printf '%b' "$index_problems"
  fi
fi

rules_count="$(wc -l < "$tmp/rules.tsv" | tr -d ' ')"
release_count="$(wc -l < "$tmp/release.tsv" | tr -d ' ')"

if [ "$problems" -ne 0 ]; then
  echo "结果：对账失败（rules.md=${rules_count} 行, 发布文件=${release_count} 行）"
  exit 1
fi

index_note=""
if [ -n "${expected_upper:-}" ]; then
  index_note="，全索引上界 NCMAP${expected_upper}（三语头注行 + 正文全集句已核对）"
fi
echo "诊断码三方对账通过：rules.md × AnalyzerReleases.Shipped/Unshipped 共 ${rules_count} 条规则，ID 集合与严重度一致${index_note}。"
exit 0
