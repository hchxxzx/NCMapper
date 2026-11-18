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

add_plan() {  # $1=src rel path  $2=wiki dest page name（扁平，不含 .md）
  local src="$1" dst="$2"
  if [ -n "${SEEN[$dst]:-}" ]; then
    echo "[CONFLICT] wiki 页名冲突：$dst（${SEEN[$dst]} vs $src）" >&2
    conflict=1
    return
  fi
  SEEN[$dst]="$src"
  PLAN+=("$(printf '%s\t%s' "$src" "$dst")")
}

# ============================================================================
# 页面命名：全语言统一「语言前缀 + 全路径 PascalCase 扁平」
#
#   中 how-to/fluent/basics.md  →  Zh-Fluent-Basics
#   英 how-to/fluent/basics.md  →  En-Fluent-Basics
#   英 getting-started/quickstart.md → En-GettingStarted-Quickstart
#   英 explanation/dual-engine.md    → En-Explanation-DualEngine
#   英 how-to/faq.md            →  En-Faq
#
# 为什么必须扁平：GitHub Wiki 的页面名就是文件名，官方文档《About wiki
# filenames》明确禁止在标题中使用 `/`，实测子目录页 /wiki/A/B 直接 HTTP
# 错误、页面打不开（探针页对照实验确认）。故所有页一律平铺于顶层。
#
# 为什么加语言前缀：多语言并存时同一篇文档的三种译本必须各有唯一页名，
# 否则 En 与 Ja 会互撞。前缀同时让人一眼看出语种。规则见方案
# 20261007-001 §4.2（候选 A：全语言统一前缀）。
# ============================================================================
LANG_PREFIX=""
case "$LANG_ID" in
  zh-CN) LANG_PREFIX="Zh" ;;
  en-US) LANG_PREFIX="En" ;;
  ja)    LANG_PREFIX="Ja" ;;
  de)    LANG_PREFIX="De" ;;
  *)
    # 未登记语言：取主语言段首字母大写（pt-BR → Pt），保证仍有前缀、不与既有撞
    LANG_PREFIX="$(printf '%s' "${LANG_ID%%-*}" | awk '{print toupper(substr($0,1,1)) substr($0,2)}')"
    ;;
esac

# --- ① how-to 域子目录页：how-to/<domain>/<page>.md → <Lang>-<Domain>-<Page> ---
shopt -s nullglob
for dir in "$SRC"/how-to/*/; do
  domain="$(basename "$dir")"
  dp="$(pascal "$domain")"
  for f in "$dir"*.md; do
    stem="$(basename "$f" .md)"
    add_plan "${f#"$ROOT"/}" "$LANG_PREFIX-$dp-$(pascal "$stem")"
  done
done
# --- ② how-to 平铺页（faq / pitfalls 等）→ <Lang>-<Page> ---
for f in "$SRC"/how-to/*.md; do
  stem="$(basename "$f" .md)"
  add_plan "${f#"$ROOT"/}" "$LANG_PREFIX-$(pascal "$stem")"
done
# --- ③ 其余叙事目录：<sub>/<page>.md → <Lang>-<Group>-<Page> ---
for pair in "getting-started:GettingStarted" "tutorials:Tutorials" "explanation:Explanation"; do
  sub="${pair%%:*}"; grp="${pair##*:}"
  for f in "$SRC/$sub"/*.md; do
    [ -e "$f" ] || continue
    stem="$(basename "$f" .md)"
    add_plan "${f#"$ROOT"/}" "$LANG_PREFIX-$grp-$(pascal "$stem")"
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
#
# 性能：纯 awk 单进程实现。早先用 shell 逐行 + grep/sed 的子进程版本跑单个语言要
# 十分钟以上（每行 3~5 次 fork），全量同步会拖到 CI 超时。
expand_code_refs() {
  local in="$1" out="$2" root="$3"
  awk -v ROOT="$root" '
    # 从 "!code-LANG[标题](目标)" 解析 lang 与 target
    function parse(line, A,   p, t, lang, target) {
      # 找 "!code-"
      p = index(line, "!code-")
      if (p == 0) return 0
      t = substr(line, p + 6)
      # lang 到第一个 [
      if (match(t, /^[A-Za-z]+\[/) == 0) return 0
      lang = substr(t, 1, RLENGTH - 1)
      # target：最后一个 "](" 之后、结尾 ")]" 之前
      t = substr(t, RLENGTH + 1)
      p = 0
      s = t
      while ((q = index(s, "](")) > 0) { p += q; s = substr(s, q + 2) }
      if (p == 0) return 0
      target = s
      sub(/\)\][[:space:]]*$/, "", target)
      A["lang"] = lang
      A["target"] = target
      return 1
    }

    {
      line = $0
      # 仅处理整行就是一条引用的形态（正文中提到语法名的保持原样）
      if (line !~ /^[[:space:]]*\[!code-/) { print line; next }
      if (line !~ /\)\][[:space:]]*$/)         { print line; next }

      split("", A)
      if (parse(line, A) == 0) { print line; next }

      target = A["target"]; lang = A["lang"]
      frag = ""
      p = index(target, "#")
      if (p > 0) { frag = substr(target, p + 1); target = substr(target, 1, p - 1) }

      # ~/ 前缀 → 仓库根；/ 开头 → 仓库根
      if (substr(target, 1, 2) == "~/")      target = ROOT "/" substr(target, 3)
      else if (substr(target, 1, 1) == "/")  target = ROOT target

      # 读源文件并抽 region
      n = 0; inside = 0; min = ""
      if (frag != "") {
        while ((getline l < target) > 0) {
          if (inside == 0) {
            if (l ~ ("#region[[:space:]]+" frag "$")) { inside = 1 }
            continue
          }
          if (l ~ /#endregion/) break
          n++; body[n] = l
          if (l ~ /[^[:space:]]/) {
            match(l, /^[[:space:]]*/)
            if (min == "" || RLENGTH < min) min = RLENGTH
          }
        }
        close(target)
      }

      if (n > 0) {
        if (min == "") min = 0
        print "```" lang
        for (i = 1; i <= n; i++) {
          s = body[i]
          if (length(s) >= min) s = substr(s, min + 1)
          print s
        }
        print "```"
      } else {
        rel = target
        sub("^" ROOT "/", "", rel)
        printf "> ⚠ Code snippet `%s` not found in `%s` — see the [source on GitHub](https://github.com/hchxxzx/NCMapper/blob/main/%s).\n", frag, rel, rel
      }
    }
  ' "$in" > "$out"
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

# ============================================================================
# 链接改写：页面扁平化 + 加语言前缀后，正文里的相对链接与原页名全部失效，
# 必须按映射表重写，否则 554 个站内链接全断。
#
# 规则（方案 20261007-001 §4.3）：
#   ① 同步面内目标（how-to/ getting-started/ tutorials/ explanation/）：
#      改写为对应新页名（含语言前缀）。同语言链接，锚点照抄。
#   ② 同步面外目标（reference/ diagnostics/ contributing/ index.md 等）：
#      Wiki 上没有这些页，相对路径必断——改写为 GitHub 源码链接。
#   ③ 未命中映射表的 .md 链接（点错路径等）：保持原样，交给后续检查暴露。
#   ④ 代码块内的内容一律不动（避免误改示例里的伪链接）。
# ============================================================================
# 构造映射表文件：绝对源路径 -> 新页名
MAPFILE="$WIKI/.__map"
: > "$MAPFILE"
for item in "${PLAN[@]}"; do
  src_rel="${item%%$'\t'*}"
  dst="${item##*$'\t'}"
  printf '%s\t%s\n' "$ROOT/$src_rel" "$dst" >> "$MAPFILE"
done

rewrite_links() {  # $1=待改写文件  $2=该文件对应的源绝对路径  $3=语言标识
  local f="$1" srcabs="$2" lang="$3"
  local srcdir
  srcdir="$(dirname "$srcabs")"
  awk -v MAP="$MAPFILE" -v SRCDIR="$srcdir" -v LANG="$lang" -v ROOT="$ROOT" '
    BEGIN {
      # 载入映射表
      while ((getline line < MAP) > 0) {
        p = index(line, "\t")
        if (p > 0) TBL[substr(line, 1, p - 1)] = substr(line, p + 1)
      }
      close(MAP)
      inFence = 0
    }
    # 代码围栏内不改写
    /^[[:space:]]*(```|~~~)/ { inFence = !inFence; print; next }
    inFence { print; next }
    {
      line = $0
      out = ""
      rest = line
      # 逐个扫描 ](target) 形态
      while (match(rest, /\]\([^)[:space:]]+\.md(#[^)]*)?\)/)) {
        pre = substr(rest, 1, RSTART - 1)
        hit = substr(rest, RSTART, RLENGTH)
        rest = substr(rest, RSTART + RLENGTH)
        out = out pre
        # 拆出 target 与锚点。hit 形如 "](x.md)"，去掉前 2 与后 1 字符：
        # 长度 = length(hit) - 3（早期写成 -4 会吃掉目标末尾的 d，导致映射表永不命中）
        t = substr(hit, 3, length(hit) - 3)
        anchor = ""
        if (match(t, /#[^#]*$/)) { anchor = substr(t, RSTART); t = substr(t, 1, RSTART - 1) }
        # 相对路径 → 绝对（用 SRCDIR 拼）
        if (t ~ /^\//) { abs = t }
        else { abs = SRCDIR "/" t }
        # 归一化 ../ 与 ./ 与重复斜杠。
        # 注意不能用"/"+拼接（会在 Windows 的 D:/... 前多出斜杠，与映射表键对不上）：
        # 首段若含冒号（盘符）则保留原样，否则补前导斜杠。
        n = split(abs, parts, "/")
        depth = 0
        for (i = 1; i <= n; i++) {
          if (parts[i] == "" || parts[i] == ".") continue
          if (parts[i] == "..") { if (depth > 0) depth-- ; continue }
          stack[++depth] = parts[i]
        }
        norm = ""
        for (i = 1; i <= depth; i++) {
          if (i == 1) {
            norm = (stack[i] ~ /:/) ? stack[i] : "/" stack[i]
          } else {
            norm = norm "/" stack[i]
          }
        }
        delete stack
        if (norm in TBL) {
          out = out "](" TBL[norm] anchor ")"
        } else if (index(norm, ROOT "/docs/") == 1) {
          # 未命中映射表，但确实指向 docs/ 下的文件 → 同步面外
          # （reference/ diagnostics/ contributing/ index.md 等：Wiki 上没有这些页，
          #  相对路径必断，改写为 GitHub 源码链接）
          #
          # 注意判断顺序：不能先判「是否在 docs/<lang>/ 下」再判「是否同步面外」——
          # 同步面外文件同样位于 docs/<lang>/ 内（如 docs/en-US/reference/），
          # 先判语言会把它们误当成"面内未登记"而保持原样，导致链接照旧断掉。
          rel = substr(norm, length(ROOT) + 2)
          out = out "](https://github.com/hchxxzx/NCMapper/blob/main/" rel ")"
        } else {
          out = out hit
        }
      }
      print out rest
    }
  ' "$f" > "$f.__rw"
  mv "$f.__rw" "$f"
}

for item in "${PLAN[@]}"; do
  src_rel="${item%%$'\t'*}"
  dst="${item##*$'\t'}"
  emit_page "$ROOT/$src_rel" "$WIKI/$dst.md.__body"
  # 三阶段流水：剥头部 → 展开 DocFX 代码 → 改写站内链接
  # 分阶段而非一次做完，任一步出错都能单独复现定位。
  expand_code_refs "$WIKI/$dst.md.__body" "$WIKI/$dst.md.__code" "$ROOT"
  rewrite_links "$WIKI/$dst.md.__code" "$ROOT/$src_rel" "$LANG_ID"
  mv "$WIKI/$dst.md.__code" "$WIKI/$dst.md"
  rm -f "$WIKI/$dst.md.__body"
done
rm -f "$MAPFILE"

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
      # 页名与映射口径一致：<Lang>-<Domain>-<Page>
      pages+=("- [$(pascal "$stem")]($LANG_PREFIX-$dp-$(pascal "$stem"))")
    done
    [ ${#pages[@]} -eq 0 ] && continue
    echo "### $dp"
    printf '%s\n' "${pages[@]}"
  done
  # how-to 平铺页（faq、pitfalls 等无域子目录的页）：此前只并入映射计划、未进侧栏，
  # 导致页面存在但导航里点不到。与 ② 的映射口径保持一致。
  flat_pages=()
  for f in "$SRC"/how-to/*.md; do
    [ -e "$f" ] || continue
    stem="$(basename "$f" .md)"
    flat_pages+=("- [$(pascal "$stem")]($LANG_PREFIX-$(pascal "$stem"))")
  done
  if [ ${#flat_pages[@]} -gt 0 ]; then
    if [ "$LANG_ID" = "zh-CN" ]; then echo "### 其他"; else echo "### Other"; fi
    printf '%s\n' "${flat_pages[@]}"
  fi
  for pair in "getting-started:GettingStarted" "tutorials:Tutorials" "explanation:Explanation"; do
    sub="${pair%%:*}"; grp="${pair##*:}"
    label="$grp"
    pages=()
    for f in "$SRC/$sub"/*.md; do
      [ -e "$f" ] || continue
      stem="$(basename "$f" .md)"
      # 页名与映射口径一致：<Lang>-<Group>-<Page>
      pages+=("- [$stem]($LANG_PREFIX-$grp-$(pascal "$stem"))")
    done
    [ ${#pages[@]} -eq 0 ] && continue
    echo "### $label"
    printf '%s\n' "${pages[@]}"
  done
} > "$sidebar"

# ---------------------------------------------------------------------------
# 多语言入口：_Sidebar 是全站唯一一份共享文件，按语言分批跑会互相覆盖，
# 所以语言入口行不写在 sidebar 生成段里，而是由调用方（workflow）在全部语言
# 跑完后统一插入一次。这里只写「本语言那一行」，交给 append_language_entry 汇总。
# ---------------------------------------------------------------------------

# 注意：本脚本不再生成 Home.md ——
# 多语言下 /wiki/Home 由 scripts/wiki-build-languages.sh 统一生成为「语言选择入口」，
# 各语言首页是 <Lang>-Home。单语言跑时若这里也写 Home.md，会与入口页互相覆盖，
# 最终内容取决于执行顺序。故 Home 的生成职责完全上移到多语言编排脚本。

echo "wiki 映射暂存完成：${#PLAN[@]} 页 → $WIKI（页名 <Lang>-<Group>-<Page> 全扁平）"
