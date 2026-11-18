#!/usr/bin/env bash
# ================================================================
# wiki-sync-mapping.sh — docs/<lang> 叙事层 → GitHub Wiki 扁平页映射与暂存
# （从 wiki-sync.yml 内联逻辑抽取，支持本地 dry-run）
#
# 职责：
#   ① 同步源：docs/<lang>/**（lang 由第 3 参数或 WIKI_LANG 指定，默认 en-US）
#   ② 域路径 → wiki 扁平页名映射：how-to/<domain>/<page>.md → wiki/<Domain>-<Page>
#      （PascalCase 区分跨域重名，如 how-to/fluent/basics.md → wiki/Fluent-Basics）
#      how-to 平铺页（域分章前遗留形态）→ wiki/<原文件名去 .md>（过渡兼容，后续自然空转）
#   ③ 其余叙事目录同名复制：getting-started→GettingStarted/、tutorials→Tutorials/、
#      explanation→Explanation/（沿用既有约定）
#   ④ _Sidebar.md / Home.md 导航块按域结构生成
#   ⑤ 产物页名唯一性断言（冲突即失败，防镜像推错）
#
# 用法：
#   bash scripts/wiki-sync-mapping.sh <repo_root> <wiki_dir> [lang]     # 实际复制+生成
#   WIKI_DRY_RUN=1 bash scripts/wiki-sync-mapping.sh <repo_root> <out> [lang]
#   lang 可省略，此时取 WIKI_LANG，再缺省为 en-US
#   例：bash scripts/wiki-sync-mapping.sh "$PWD" wiki            # 英文（默认）
#       bash scripts/wiki-sync-mapping.sh "$PWD" wiki zh-CN      # 中文
# 退出码：0=成功  1=页名冲突  2=源目录缺失  3=语言标识非法
# ================================================================
set -euo pipefail

ROOT="${1:?usage: wiki-sync-mapping.sh <repo_root> <wiki_dir> [lang]}"
WIKI="${2:?usage: wiki-sync-mapping.sh <repo_root> <wiki_dir> [lang]}"
LANG_ID="${3:-${WIKI_LANG:-en-US}}"
DRY="${WIKI_DRY_RUN:-0}"

# 语言标识白名单：仅允许字母 + 连字符（防路径穿越与拼写噪音）
case "$LANG_ID" in
  *[!A-Za-z-]* | '')
    echo "[ERROR] 语言标识非法：'$LANG_ID'（只允许字母与连字符，如 en-US / zh-CN）" >&2
    exit 3
    ;;
esac

SRC="$ROOT/docs/$LANG_ID"

if [ ! -d "$SRC" ]; then
  echo "[ERROR] 同步源缺失：$SRC（期望 docs/$LANG_ID，前缀已修齐至双语拆分实态）" >&2
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

# 把 DocFX 的代码引用语法展开成 Wiki 能渲染的围栏代码块：
#
#   [!code-csharp[标题](~/samples/X/Y.cs#RegionName)]
#     → ```csharp
#       <region 内的代码，去掉 #region/#endregion 与公共缩进>
#       ```
#
# DocFX 是微软的文档生成器，该语法它认；GitHub Wiki 不认，会原样显示成一串路径文本。
# 实测 docs/en-US 全量 485 处、分布 54/84 个文件，属系统性缺陷。
#
# 参数：$1=输入文件  $2=输出文件  $3=仓库根（解析 ~/ 前缀用）
# 找不到 region 或源文件时，降级为一行指向 GitHub 的链接，绝不静默丢内容。
expand_code_refs() {
  local in="$1" out="$2" root="$3"
  : > "$out"
  while IFS= read -r line || [ -n "$line" ]; do
    # 匹配整行的单条引用（标题里可能有 \] 转义，用贪婪到最后一个 ]( 的形式）
    if printf '%s' "$line" | grep -qE '^[[:space:]]*\[!code-[A-Za-z]+\[.*\]\([^)]+\)\][[:space:]]*$'; then
      local lang path frag
      lang=$(printf '%s' "$line" | sed -E 's/^[[:space:]]*\[!code-([A-Za-z]+)\[.*/\1/')
      # 取最后一个 ]( 与结尾 )] 之间的目标，避开标题里的 ] 字符
      path=$(printf '%s' "$line" | sed -E 's/.*\]\(([^)]+)\)\][[:space:]]*$/\1/')
      frag=""
      case "$path" in
        *#*) frag="${path##*#}"; path="${path%%#*}" ;;
      esac
      # ~/ 前缀 → 仓库根
      case "$path" in
        '~/'*) path="$root/${path#\~/}" ;;
        /*)    path="$root$path" ;;
      esac
      if [ -f "$path" ] && [ -n "$frag" ]; then
        # 抽 region：从 "#region <frag>" 到其配对的 "#endregion"
        sed -n "/#region[[:space:]]\{1,\}${frag}\$/,/#endregion/p" "$path" \
          | sed '1d;$d' \
          | awk '
              # 去掉公共缩进（以非空行的最小缩进为基准）
              { lines[NR] = $0
                if ($0 ~ /[^[:space:]]/) {
                  match($0, /^[[:space:]]*/)
                  ind = RLENGTH
                  if (min == "" || ind < min) min = ind
                } }
              END {
                if (min == "") min = 0
                for (i = 1; i <= NR; i++) {
                  s = lines[i]
                  if (length(s) >= min) s = substr(s, min + 1)
                  print s
                }
              }' > "$out.__frag"
        if [ -s "$out.__frag" ]; then
          {
            printf '```%s\n' "$lang"
            cat "$out.__frag"
            printf '```\n'
          } >> "$out"
        else
          # region 名对不上（改名/删除）：降级为源码链接，保留可追溯性
          local repo_rel="${path#"$root"/}"
          printf '> ⚠ Code snippet `%s` not found in `%s` — see the [source on GitHub](https://github.com/hchxxzx/NCMapper/blob/main/%s).\n' \
            "$frag" "$repo_rel" "$repo_rel" >> "$out"
        fi
        rm -f "$out.__frag"
      else
        printf '%s\n' "$line" >> "$out"
      fi
    else
      printf '%s\n' "$line" >> "$out"
    fi
  done < "$in"
}

# 复制单页并剥离 Wiki 不渲染/不该显示的头部块：
#   ① 机器翻译声明（> [!WARNING] 引用块）——仓库内的翻译溯源提示，Wiki 不需要；
#   ② 页头元信息块——Wiki 不解析，留着会被当正文渲染成横线与键值行。
#      实测该块有三种分隔写法，都要认：
#        ---  ... ---            （标准 YAML front matter）
#        ***  ... ----------------（多数机翻页的变体）
#      且 WARNING 块常排在元信息之前，此时元信息不在文件首行，必须按"块"剥而非只看首行。
# 实现：循环剥离开头的 WARNING 块与元信息块，直到文件真正以正文开头。
emit_page() {  # $1=源文件绝对路径  $2=目标文件
  local src="$1" dst="$2"
  awk '
    # 判定是否为元信息块的分隔行：--- / *** / ___ / ---- 等纯符号行（>=3 个同类符号）
    function is_rule(s) {
      return (s ~ /^-[[:space:]]*-[-[:space:]]*$/ || s ~ /^\*[[:space:]]*\*[*[:space:]]*$/ || s ~ /^_[[:space:]]*_[_[:space:]]*$/)
    }
    BEGIN { s = 0 }   # s=0 扫头部  s=1 元信息块内  s=2 正文
    s == 0 {
      if ($0 ~ /^>[[:space:]]*\[!WARNING\]/) next      # WARNING 首行
      if ($0 ~ /^>[[:space:]]*/)              next      # WARNING 续行
      if ($0 ~ /^[[:space:]]*$/)              next      # 空行
      if (is_rule($0))                        { s = 1; next }   # 元信息块起始
      s = 2
    }
    s == 1 {
      if (is_rule($0)) { s = 0; next }                 # 元信息块结束，回头部继续扫
      next                                             # 块内键值行丢弃
    }
    { print }
  ' "$src" > "$dst"
}

mkdir -p "$WIKI"
for item in "${PLAN[@]}"; do
  src_rel="${item%%$'\t'*}"
  dst="${item##*$'\t'}"
  mkdir -p "$WIKI/$(dirname "$dst")"
  emit_page "$ROOT/$src_rel" "$WIKI/$dst.md.__body"
  # 第一阶段剥头部、第二阶段展开 DocFX 代码引用，两阶段分离便于单独调试
  expand_code_refs "$WIKI/$dst.md.__body" "$WIKI/$dst.md" "$ROOT"
  rm -f "$WIKI/$dst.md.__body"
done

# --- ④ _Sidebar.md / Home.md 导航块（域结构驱动）---
# 侧栏小标题按语言取词：中文「操作指南」，其余语言用英文标题（Wiki 主体面向国际读者）
if [ "$LANG_ID" = "zh-CN" ]; then
  HOWTO_LABEL="操作指南"
else
  HOWTO_LABEL="How-to Guides"
fi

sidebar="$WIKI/_Sidebar.md"
{
  echo "## $HOWTO_LABEL"
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
  # how-to 平铺页（faq、pitfalls 等无域子目录的页）：此前只并入映射计划、未进侧栏，
  # 导致页面存在但导航里点不到。与 ② 的映射口径保持一致（页名 = 去 .md 的原文件名）。
  flat_pages=()
  for f in "$SRC"/how-to/*.md; do
    [ -e "$f" ] || continue
    stem="$(basename "$f" .md)"
    flat_pages+=("- [$stem]($stem)")
  done
  if [ ${#flat_pages[@]} -gt 0 ]; then
    if [ "$LANG_ID" = "zh-CN" ]; then echo "### 其他"; else echo "### Other"; fi
    printf '%s\n' "${flat_pages[@]}"
  fi
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
# 判定标记用语言无关的 HTML 注释（wiki 渲染时不可见）：
# 若用可见标题做标记，切换同步语言后会因标题文本不同而失配，导致重复追加。
MARKER="<!-- wiki-sync:generated -->"
if ! grep -qF "$MARKER" "$home" 2>/dev/null; then
  {
    echo ""
    echo "## $HOWTO_LABEL"
    echo ""
    echo "$MARKER"
    echo "<!-- 由 docs/$LANG_ID/how-to 域结构自动同步；请勿直接编辑本页 -->"
  } >> "$home"
fi

echo "wiki 映射暂存完成：${#PLAN[@]} 页 → $WIKI（含 _Sidebar.md / Home.md 导航块）"
