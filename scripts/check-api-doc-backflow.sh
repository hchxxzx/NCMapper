#!/usr/bin/env bash
# =====================================================================
# check-api-doc-backflow.sh — 公开 API 清单 ↔ wiki 页 记载回灌反查
#
# 目的：核对 architecture-map.md 清单表格
#   每行的「文档」列跳转后，该行登记的 API 标识符是否能在目标页正文找到。
#   用于发现「清单已登记、源码实存、读者按文档列跳转后却找不到说明」的缺口。
#
# 本脚本同时是该判定的复跑口径。暂不挂门禁链（退出码恒 0），结论以 stdout 为准。
#
# 实现说明：全流程纯 bash（不调用 grep/awk/sed 等外部命令），以确保跨机（含受限沙箱）
#   反复运行结果一致、不因外部命令差异漂移。
#
# 口径（复现基准）：
#   1. 只解析标题（# 起）下的清单表格行；排除表头行、分隔行、架构图与非表格文本。
#   2. 「覆盖统计」小节整表排除（统计汇总，非 API 清单）。
#   3. 表格列布局：
#        - 标准 5 列表：        API/功能点 | 说明 | 演示 | 片段 | 文档
#        - 双通道对照表：      功能点 | 特性通道↔Fluent 终端 | 演示 | 片段 | 文档
#                               （标识符取自第 2 列与第 3 列两处）
#        - 取值枚举表：        枚举 | 用户接触点 | 文档（3 列，无「说明/演示/片段」）
#   4. 标识符抽取：API 列「反引号代码段」内、符合「首字母大写 + 长度 >= 5」的标识符。
#      Async 变体归并到基名（XxxAsync → Xxx，避免同族重复计数）；泛型形参占位
#      （T 开头大写字母或数字，如 TSource/TTarget/T1）与 BCL 短类型名剔除。
#      参数名（source/target/action/writer 等，首字母小写）由规则自动排除。
#   5. 三态判定（逐 token 在目标页全文做字面子串匹配）：
#        OK    全命中：该行全部 token 在目标页出现
#        PART  部分缺：部分 token 在目标页出现
#        MISS  全缺：该行全部 token 在目标页均不出现
#   6. 目标页解析：文档列 markdown 链接 `](相对路径)`（取最后一个），相对
#      docs/zh-CN/reference/ 解析；文档列为「—」（无链接）的行跳过（无跳转目标）。
#
# 用法：bash scripts/check-api-doc-backflow.sh        （仓库根或任意目录均可）
# =====================================================================

set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
map_md="$repo_root/docs/zh-CN/reference/architecture-map.md"
ref_dir="$repo_root/docs/zh-CN/reference"

if [ ! -f "$map_md" ]; then
  echo "[error] 清单页缺失：$map_md"
  exit 0
fi

trim() {
  local s="$1"
  s="${s#"${s%%[![:space:]]*}"}"
  s="${s%"${s##*[![:space:]]}"}"
  printf '%s' "$s"
}

mapfile -t lines < "$map_md"

re_link='\]\(([^)]+)\)'
re_ident='([A-Za-z_][A-Za-z0-9_]*)'

lineno=0
sec=""
n_ok=0; n_part=0; n_miss=0; n_skip=0; n_rows=0

printf '%-6s %-7s %-44s %s\n' "行号" "状态" "目标页" "缺口 token（未命中）"
printf '%s\n' "-----------------------------------------------------------------------------"

for line in "${lines[@]}"; do
  lineno=$((lineno + 1))

  # 任意标题行（#/##/###）更新当前小节
  if [[ "$line" == "#"* ]]; then sec="${line##*# }"; continue; fi
  [[ "$line" == "|"* ]] || continue
  case "$sec" in *覆盖统计*) continue ;; esac

  IFS='|' read -r -a cells <<< "$line"
  ncols=${#cells[@]}
  (( ncols >= 3 )) || continue

  c1="$(trim "${cells[1]}")"

  # 分隔行（去掉 | - : 空格 后为空）
  whole="${line//[| -:]/}"
  [ -z "$whole" ] && continue

  # 表头行
  case "$c1" in API|功能点|枚举|分层|属性|字段|类型) continue ;; esac

  # 文档列 = 最后一个非空列（尾随空字段可能被 read 丢弃）
  doc_cell=""
  for ((i = ncols - 1; i >= 1; i--)); do
    cc="$(trim "${cells[$i]}")"
    if [ -n "$cc" ]; then doc_cell="$cc"; break; fi
  done
  [ -z "$doc_cell" ] && { n_skip=$((n_skip + 1)); continue; }

  # 取最后一个 markdown 链接
  doc_link=""; tmp="$doc_cell"
  while [[ "$tmp" =~ $re_link ]]; do
    doc_link="${BASH_REMATCH[1]}"
    tmp="${tmp#*"${BASH_REMATCH[0]}"}"
  done
  if [ -z "$doc_link" ]; then n_skip=$((n_skip + 1)); continue; fi

  page="$ref_dir/$doc_link"
  if [ ! -f "$page" ]; then
    printf '%-6s %-7s %-44s %s\n' "$lineno" "NOFILE" "$doc_link" "（目标页不存在）"
    n_miss=$((n_miss + 1))
    continue
  fi

  # 标识符来源列：双通道表取第 2、3 列；其余取第 2 列
  src="${cells[1]}"
  case "$sec" in *双通道*) src="${cells[1]} ${cells[2]}" ;; esac

  # 只取反引号代码段（附录 A 口径：API 列反引号内的标识符）
  bt='`'
  tmp="$src"; spans=""
  while [[ "$tmp" == *"$bt"* ]]; do
    tmp="${tmp#*"$bt"}"
    [[ "$tmp" == *"$bt"* ]] || break
    spans="$spans ${tmp%%"$bt"*}"
    tmp="${tmp#*"$bt"}"
  done

  # 抽取 token（纯 bash，按出现顺序去重；Async 变体归并到基名）
  toks=(); declare -A tokseen=()
  rest="$spans"
  while [[ "$rest" =~ $re_ident ]]; do
    w="${BASH_REMATCH[1]}"
    rest="${rest#*"$w"}"
    w="${w%Async}"
    ((${#w} >= 5)) || continue
    [[ "$w" == [A-Z]* ]] || continue
    case "$w" in T[A-Z]*|T[0-9]*) continue ;; esac
    case "$w" in Type|String|Object|Task|List|Naming) continue ;; esac
    [ -z "${tokseen[$w]:-}" ] || continue
    tokseen[$w]=1; toks+=("$w")
  done
  if [ "${#toks[@]}" -eq 0 ]; then n_skip=$((n_skip + 1)); continue; fi

  content="$(<"$page")"

  n_rows=$((n_rows + 1))
  miss=""; hit=0; total=0
  for t in "${toks[@]}"; do
    total=$((total + 1))
    if [[ "$content" == *"$t"* ]]; then
      hit=$((hit + 1))
    else
      miss="$miss $t"
    fi
  done

  if [ "$hit" -eq "$total" ]; then
    st="OK"; n_ok=$((n_ok + 1))
  elif [ "$hit" -eq 0 ]; then
    st="MISS"; n_miss=$((n_miss + 1))
  else
    st="PART"; n_part=$((n_part + 1))
  fi

  if [ "$st" != "OK" ]; then
    printf '%-6s %-7s %-44s %s\n' "$lineno" "$st" "$doc_link" "$miss"
  fi
done

printf '%s\n' "-----------------------------------------------------------------------------"
echo "=== 汇总 ==="
echo "参与判定行数 $n_rows（另有 $n_skip 行文档列为「—」/无标识符，跳过）"
echo "全命中 OK $n_ok / 部分缺 PART $n_part / 全缺 MISS $n_miss"
echo "缺口行（PART+MISS）合计 $((n_part + n_miss))"
echo "[info] 本工具为核查底稿，不参与门禁拦截；判定须结合脚本头注的假阳性规则人工甄别。"
exit 0
