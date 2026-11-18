#!/usr/bin/env bash
# ================================================================
# wiki-sync-mapping.sh — docs/zh-CN 叙事层 → GitHub Wiki 扁平页映射与暂存
# （从 wiki-sync.yml 内联逻辑抽取，支持本地 dry-run）
#
# 职责：
#   ① 同步源前缀修齐：docs/zh-CN/**（双语拆分实态；修复 docs/how-to 旧前缀
#      导致的触发器永不命中、复制源恒空双重失效）
#   ② 域路径 → wiki 扁平页名映射：how-to/<domain>/<page>.md → wiki/<Domain>-<Page>
#      （PascalCase 区分跨域重名，如 how-to/fluent/basics.md → wiki/Fluent-Basics）
#      how-to 平铺页（域分章前遗留形态）→ wiki/<原文件名去 .md>（过渡兼容，后续自然空转）
#   ③ 其余叙事目录同名复制：getting-started→GettingStarted/、tutorials→Tutorials/、
#      explanation→Explanation/（沿用既有约定）
#   ④ _Sidebar.md / Home.md 导航块按域结构生成
#   ⑤ 产物页名唯一性断言（冲突即失败，防镜像推错）
#
# 用法：
#   bash scripts/wiki-sync-mapping.sh <repo_root> <wiki_dir>          # 实际复制+生成
#   WIKI_DRY_RUN=1 bash scripts/wiki-sync-mapping.sh <repo_root> <out> # 仅打印映射计划，不写文件
# 退出码：0=成功  1=页名冲突  2=源目录缺失
# ================================================================
set -euo pipefail

ROOT="${1:?usage: wiki-sync-mapping.sh <repo_root> <wiki_dir>}"
WIKI="${2:?usage: wiki-sync-mapping.sh <repo_root> <wiki_dir>}"
DRY="${WIKI_DRY_RUN:-0}"
SRC="$ROOT/docs/zh-CN"

if [ ! -d "$SRC" ]; then
  echo "[ERROR] 同步源缺失：$SRC（期望 docs/zh-CN，前缀已修齐至双语拆分实态）" >&2
  exit 2
fi

# PascalCase：kebab/snake 段首字母大写拼接（faq→Faq，deep-copy→DeepCopy）
pascal() {
  printf '%s' "$1" | awk -F'[-_]' '{out=""; for(i=1;i<=NF;i++) out=out toupper(substr($i,1,1)) substr($i,2); print out}'
}

declare -a PLAN=()   # 每项 "相对源路径<TAB>wiki目标页名（含路径）"
declare -A SEEN=()
conflict=0

add_plan() {  # $1=src rel path  $2=wiki dest (page name or dir/page)
  local src="$1" dst="$2"
  local page
  page="$(basename "$dst")"
  if [ -n "${SEEN[$page]:-}" ]; then
    echo "[CONFLICT] wiki 页名冲突：$page（${SEEN[$page]} vs $src）" >&2
    conflict=1
    return
  fi
  SEEN[$page]="$src"
  PLAN+=("$(printf '%s\t%s' "$src" "$dst")")
}

# --- ① how-to 域子目录页：how-to/<domain>/<page>.md → wiki/<Domain>-<Page> ---
shopt -s nullglob
for dir in "$SRC"/how-to/*/; do
  domain="$(basename "$dir")"
  dp="$(pascal "$domain")"
  for f in "$dir"*.md; do
    stem="$(basename "$f" .md)"
    add_plan "${f#"$ROOT"/}" "$dp-$(pascal "$stem")"
  done
done
# --- ② how-to 平铺页（过渡兼容）---
for f in "$SRC"/how-to/*.md; do
  stem="$(basename "$f" .md)"
  add_plan "${f#"$ROOT"/}" "$stem"
done
# --- ③ 其余叙事目录同名复制 ---
for pair in "getting-started:GettingStarted" "tutorials:Tutorials" "explanation:Explanation"; do
  sub="${pair%%:*}"; dst_dir="${pair##*:}"
  for f in "$SRC/$sub"/*.md; do
    [ -e "$f" ] || continue
    stem="$(basename "$f" .md)"
    add_plan "${f#"$ROOT"/}" "$dst_dir/$stem"
  done
done

if [ "$conflict" -ne 0 ]; then
  echo "[ERROR] 页名冲突，中止（防镜像推错）" >&2
  exit 1
fi

if [ "$DRY" = "1" ]; then
  echo "[DRY-RUN] 映射计划 ${#PLAN[@]} 页（唯一性断言通过）："
  printf '  %s -> %s\n' "${PLAN[@]}" | sort -k2
  exit 0
fi

mkdir -p "$WIKI"
for item in "${PLAN[@]}"; do
  src_rel="${item%%$'\t'*}"
  dst="${item##*$'\t'}"
  mkdir -p "$WIKI/$(dirname "$dst")"
  cp "$ROOT/$src_rel" "$WIKI/$dst.md"
done

# --- ④ _Sidebar.md / Home.md 导航块（域结构驱动）---
sidebar="$WIKI/_Sidebar.md"
{
  echo "## 操作指南"
  for dir in "$SRC"/how-to/*/; do
    domain="$(basename "$dir")"
    dp="$(pascal "$domain")"
    pages=()
    for f in "$dir"*.md; do
      stem="$(basename "$f" .md)"
      pages+=("- [$(pascal "$stem")]($dp-$(pascal "$stem"))")
    done
    [ ${#pages[@]} -eq 0 ] && continue
    echo "### $dp"
    printf '%s\n' "${pages[@]}"
  done
  for pair in "getting-started:GettingStarted" "tutorials:Tutorials" "explanation:Explanation"; do
    sub="${pair%%:*}"; dst_dir="${pair##*:}"
    label="$(pascal "$sub")"
    pages=()
    for f in "$SRC/$sub"/*.md; do
      [ -e "$f" ] || continue
      stem="$(basename "$f" .md)"
      pages+=("- [$stem]($dst_dir/$stem)")
    done
    [ ${#pages[@]} -eq 0 ] && continue
    echo "### $label"
    printf '%s\n' "${pages[@]}"
  done
} > "$sidebar"

home="$WIKI/Home.md"
touch "$home"
if ! grep -q "操作指南（自动生成）" "$home" 2>/dev/null; then
  {
    echo ""
    echo "## 操作指南（自动生成）"
    echo ""
    echo "<!-- wiki-sync 自动生成：docs/zh-CN/how-to 域结构 → wiki 扁平页（<Domain>-<Page>） -->"
  } >> "$home"
fi

echo "wiki 映射暂存完成：${#PLAN[@]} 页 → $WIKI（含 _Sidebar.md / Home.md 导航块）"
