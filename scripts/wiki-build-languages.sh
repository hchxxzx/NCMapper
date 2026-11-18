#!/usr/bin/env bash
# =============================================================================
# wiki-build-languages.sh — 多语言 Wiki 构建总入口
#
# 一次跑完所有启用的语言，产出单一共享的 _Sidebar.md（含语言切换入口），
# 避免按语言分批跑时 _Sidebar / Home 互相覆盖。
#
# 用法：
#   bash scripts/wiki-build-languages.sh <repo_root> <wiki_dir> [lang1 lang2 ...]
#   省略语言列表时取 WIKI_LANGS 环境变量，再缺省为 "en-US zh-CN"
#
# 设计要点（与 wiki-sync-mapping.sh 的分工）：
#   - wiki-sync-mapping.sh 负责「单语言：页名映射 + 正文改写 + 生成该语言侧栏」
#   - 本脚本负责「多语言编排：逐语言调用上者 + 生成共享语言入口 + 汇总侧栏」
#
# 为什么必须一次跑完：_Sidebar.md 是全站唯一一份共享文件。若分两次 workflow
# 调用，后跑的会把先跑的覆盖掉，最终状态取决于执行顺序（方案 §4.5 阶段 3）。
# =============================================================================
set -euo pipefail

ROOT="${1:?usage: wiki-build-languages.sh <repo_root> <wiki_dir> [langs...]}"
WIKI="${2:?usage: wiki-build-languages.sh <repo_root> <wiki_dir> [langs...]}"
shift 2
if [ "$#" -gt 0 ]; then
  LANGS=("$@")
else
  read -r -a LANGS <<< "${WIKI_LANGS:-en-US zh-CN}"
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MAPPER="$SCRIPT_DIR/wiki-sync-mapping.sh"

if [ ! -f "$MAPPER" ]; then
  echo "[ERROR] 缺少 $MAPPER" >&2
  exit 2
fi

mkdir -p "$WIKI"

# 语言显示名（供切换入口）
label_of() {
  case "$1" in
    zh-CN) echo "中文" ;;
    en-US) echo "English" ;;
    ja)    echo "日本語" ;;
    de)    echo "Deutsch" ;;
    fr)    echo "Français" ;;
    es)    echo "Español" ;;
    ru)    echo "Русский" ;;
    pt-BR) echo "Português" ;;
    ko)    echo "한국어" ;;
    it)    echo "Italiano" ;;
    *)     echo "$1" ;;
  esac
}

# 语言页名前缀（与 wiki-sync-mapping.sh 保持一致）
prefix_of() {
  case "$1" in
    zh-CN) echo "Zh" ;;
    en-US) echo "En" ;;
    ja)    echo "Ja" ;;
    de)    echo "De" ;;
    *)
      printf '%s' "${1%%-*}" | awk '{print toupper(substr($0,1,1)) substr($0,2)}'
      ;;
  esac
}

echo "=============================================="
echo " 多语言 Wiki 构建：${LANGS[*]}"
echo "=============================================="

declared=()   # 实际产出成功的语言
for lang in "${LANGS[@]}"; do
  if [ ! -d "$ROOT/docs/$lang" ]; then
    echo "[skip] docs/$lang 不存在，跳过"
    continue
  fi
  echo ""
  echo "---- 语言 $lang ----"
  # 每种语言各自生成，_Sidebar 先写到临时名，最后统一合并
  if bash "$MAPPER" "$ROOT" "$WIKI" "$lang"; then
    declared+=("$lang")
    # 保存该语言的侧栏分节（供合并）
    if [ -f "$WIKI/_Sidebar.md" ]; then
      mv "$WIKI/_Sidebar.md" "$WIKI/.__sidebar_$lang"
    fi
  else
    echo "[ERROR] 语言 $lang 同步失败" >&2
    exit 1
  fi
done

if [ "${#declared[@]}" -eq 0 ]; then
  echo "[ERROR] 没有任何语言成功产出" >&2
  exit 2
fi

# ---------------------------------------------------------------------------
# 合并 _Sidebar：顶部语言入口 + 各语言分节折叠块
#
# 语言多时（13 语）分节全铺开会很长，故非当前语言收进 <details> 折叠块。
# 但 wiki 侧栏里的 <details> 渲染支持不稳定，默认只展开「第一个语言」，
# 其余语言收进折叠块——即使折叠块不生效，也只是变长，不会丢内容。
# ---------------------------------------------------------------------------
sidebar="$WIKI/_Sidebar.md"
{
  echo "<details open>"
  echo "<summary><b>Languages</b></summary>"
  echo ""
  for lang in "${declared[@]}"; do
    p="$(prefix_of "$lang")"
    echo "- [$(label_of "$lang")]($p-Home)"
  done
  echo ""
  echo "</details>"
  echo ""
  # 主导语言（第一个）完整铺开，其余收折叠
  first="${declared[0]}"
  echo "<details open>"
  echo "<summary><b>$(label_of "$first")</b></summary>"
  echo ""
  cat "$WIKI/.__sidebar_$first"
  echo ""
  echo "</details>"
  for lang in "${declared[@]:1}"; do
    echo ""
    echo "<details>"
    echo "<summary><b>$(label_of "$lang")</b></summary>"
    echo ""
    cat "$WIKI/.__sidebar_$lang"
    echo ""
    echo "</details>"
  done
} > "$sidebar"
rm -f "$WIKI"/.__sidebar_*

# ---------------------------------------------------------------------------
# 各语言首页：用首页模板覆盖自动生成的空壳，再补语言切换行
#
# 模板位置：scripts/wiki-home/<lang>.md
#   放在 scripts/ 而非 docs/<lang>/ 下，是因为 docs 目录会被多处门禁脚本
#   rglob("*.md") 全量扫描（如 check-block-buckets.py 的代码块分桶统计），
#   首页模板里的示例代码块会被误计入文档统计、把基线数字顶破。
# 模板缺失时保留脚本生成的最小导航块（不会留空白页）。
# ---------------------------------------------------------------------------
HOMEDIR="$SCRIPT_DIR/wiki-home"
for lang in "${declared[@]}"; do
  p="$(prefix_of "$lang")"
  h="$WIKI/$p-Home.md"
  tpl="$HOMEDIR/$lang.md"
  if [ -f "$tpl" ]; then
    cp "$tpl" "$h"
  fi
done

for lang in "${declared[@]}"; do
  p="$(prefix_of "$lang")"
  h="$WIKI/$p-Home.md"
  marker="<!-- wiki-sync:langbar -->"
  if ! grep -qF "$marker" "$h" 2>/dev/null; then
    bar="[ 🌐 "
    for l2 in "${declared[@]}"; do
      p2="$(prefix_of "$l2")"
      if [ "$l2" = "$lang" ]; then
        bar="$bar **$(label_of "$l2")** |"
      else
        bar="$bar [$(label_of "$l2")]($p2-Home) |"
      fi
    done
    bar="${bar% |} ]"
    tmp="$h.__langbar"
    {
      echo "$marker"
      echo "$bar"
      echo ""
      [ -f "$h" ] && cat "$h"
    } > "$tmp"
    mv "$tmp" "$h"
  fi
done

# ---------------------------------------------------------------------------
# 根 Home：GitHub Wiki 默认打开 /wiki/Home，多语言下它当作「语言选择入口」。
# 若不生成，脚本单语言跑时留下的 Home 空壳会顶在这里，读者一进来就看到
# 一段没有语言入口的残页。
# ---------------------------------------------------------------------------
{
  echo "# NCMapper Wiki"
  echo ""
  echo "> .NET object mapping — dual engine (compile-time source generator + runtime expression trees),"
  echo "> Apache-2.0, free forever, Native AOT friendly."
  echo ""
  echo "[github.com/hchxxzx/NCMapper](https://github.com/hchxxzx/NCMapper) · [NuGet](https://www.nuget.org/packages/NCMapper)"
  echo ""
  echo "## Choose your language / 选择语言"
  echo ""
  echo "| Language | 语言 |"
  echo "| --- | --- |"
  for lang in "${declared[@]}"; do
    p="$(prefix_of "$lang")"
    echo "| [$(label_of "$lang")]($p-Home) | $(label_of "$lang") |"
  done
  echo ""
  echo "---"
  echo ""
  echo "💡 Prefer reading on the docs site? [docs/en-US](https://github.com/hchxxzx/NCMapper/tree/main/docs/en-US) · [docs/zh-CN](https://github.com/hchxxzx/NCMapper/tree/main/docs/zh-CN)"
  echo ""
  echo "<!-- wiki-sync:generated -->"
} > "$WIKI/Home.md"

echo ""
echo "=============================================="
echo " 完成：${#declared[@]} 种语言（${declared[*]}）"
echo " 页数合计：$(find "$WIKI" -name '*.md' -type f | wc -l)"
echo "=============================================="
