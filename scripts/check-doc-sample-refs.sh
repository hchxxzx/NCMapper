#!/usr/bin/env bash
# check-doc-sample-refs.sh — DFM 示例引用完整性对账（docs.yml Gate DFM 步骤）
#
# 改造说明：
#   ① 扫描范围收敛至 docs/zh-CN（en 侧 22 文件/52 处 samples 旧路径
#      引用与 18 处单体链接退出门禁，en-US 侧按既有豁免登记）
#   ② 校验形态双轨（过渡期口径：不因改造制造 CI 红窗）：
#      - 行号区间形态（#L<start> 或 #L<start>-L<end>）：沿用边界校验（1-based、start≤end、
#        end≤文件行数）。此为存量过渡形态，随页迁移转 region 后归零。
#      - region 直名形态（#<RegionName>，PoC 定稿语法）：校验目标文件存在
#        `#region <Name>` 精确行（避免前缀误匹配）。
#   ③ 纯注解（无 URL）不计；引用完整性侧门禁；示例可运行性由 ci.yml samples job 负责。
#
# 输出：失败时逐条打印 `源文件:行号: 问题`；通过时打印引用总数与形态统计。
# 退出码：0=全部合法  1=存在非法引用（门禁拦截）  2=前置缺失（未提取到任何引用=假门禁防护）
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

docs_dir="$repo_root/docs/zh-CN"
if [ ! -d "$docs_dir" ]; then
  echo "[ERROR] docs/zh-CN 目录缺失：$docs_dir" >&2
  exit 2
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# --- 提取引用：源文件<TAB>源行号<TAB>URL（~/ 相对路径 + 可选 #片段） ---
# 正则说明：标题段允许含转义方括号（编写标准的方括号转义要求，如
# `\[MapFrom\]`）——旧正则 `[^\]]*` 在首个 `]` 截断导致此类引用整体不可见（门禁盲区，
# 多轮实测 66-68 处 vs 旧口径 63 处的差因）。改为 `.*?\]\(` 惰性匹配到首个 `](`。
while IFS= read -r md_file; do
  refs="$(grep -noE '\[!code-[A-Za-z0-9_-]+\[.*?\]\([^)]+\)\]' "$md_file" || true)"
  [ -z "$refs" ] && continue
  while IFS= read -r ref; do
    [ -z "$ref" ] && continue
    line_no="${ref%%:*}"
    ref_body="${ref#*:}"
    url="$(printf '%s' "$ref_body" | sed -E 's/.*\(([^)]+)\)\].*/\1/')"
    [ -z "$url" ] && continue
    printf '%s\t%s\t%s\n' "$md_file" "$line_no" "$url"
  done <<< "$refs"
done < <(find "$docs_dir" -type f -name '*.md') > "$tmp/refs.tsv"

total="$(wc -l < "$tmp/refs.tsv" | tr -d ' ')"
if [ "$total" -eq 0 ]; then
  echo "[ERROR] 未提取到任何 [!code- 片段引用（docs/zh-CN）——对账空转即假门禁（F2），请检查提取正则或文档现状。" >&2
  exit 2
fi

problems=0
legacy_lines=0
region_refs=0

# --- 逐条校验：目标存在 + 片段合法（行号区间=过渡形态 / region 直名=定稿形态） ---
while IFS="$(printf '\t')" read -r src_file src_line url; do
  case "$url" in
    '~/'*) rel="${url:2}" ;;   # 去 ~/ 前缀（子串截取，规避 tilde 展开坑）
    *)     rel="$url" ;;
  esac
  rel="${rel%%#*}"
  frag=""
  case "$url" in
    *'#'*) frag="${url##*#}" ;;
  esac
  target="$repo_root/$rel"

  if [ ! -f "$target" ]; then
    problems=1
    echo "[BROKEN] $src_file:$src_line 引用目标不存在：$url"
    continue
  fi

  if [ -z "$frag" ]; then
    continue   # 文件级引用（无片段）：目标存在即合法
  fi

  if [[ "$frag" =~ ^L[0-9]+(-L[0-9]+)?$ ]]; then
    # ---- 过渡形态：行号区间 ----
    legacy_lines=$((legacy_lines + 1))
    start="${frag#L}"
    end="$start"
    case "$start" in
      *-L*) end="${start#*-L}"; start="${start%%-*}" ;;
    esac
    line_count="$(grep -c '' "$target" || true)"
    if ! [[ "$start" =~ ^[0-9]+$ ]] || ! [[ "$end" =~ ^[0-9]+$ ]]; then
      problems=1
      echo "[BADREF] $src_file:$src_line 行号片段非法：$url"
    elif [ "$start" -lt 1 ] || [ "$end" -lt "$start" ] || [ "$end" -gt "$line_count" ]; then
      problems=1
      echo "[BADREF] $src_file:$src_line 行号区间越界：$url（文件共 ${line_count} 行）"
    fi
  elif [[ "$frag" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]]; then
    # ---- 定稿形态：region 直名（docfx 对 `#region <Name>` 按名提取）----
    region_refs=$((region_refs + 1))
    if ! grep -qE "^[[:space:]]*#region[[:space:]]+${frag}[[:space:]]*$" "$target"; then
      problems=1
      echo "[NOREGION] $src_file:$src_line 引用 region 在目标文件不存在：$url（期望精确行 `#region ${frag}`）"
    fi
  else
    problems=1
    echo "[BADREF] $src_file:$src_line 片段形态不识别（既非 #L 行号区间亦非合法 region 名）：$url"
  fi
done < "$tmp/refs.tsv"

if [ "$problems" -ne 0 ]; then
  echo "结果：DFM 引用完整性对账失败（共检查 ${total} 条引用：region 形态 ${region_refs}、行号区间过渡形态 ${legacy_lines}）"
  exit 1
fi

echo "DFM 示例引用完整性对账通过：${total} 条引用（region 直名 ${region_refs}、行号区间过渡形态 ${legacy_lines}——后者随页迁移归零）。"
exit 0
