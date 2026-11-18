#!/usr/bin/env bash
# check-api-coverage.sh — API 参考覆盖率对账（CI docs.yml 步骤）
#
# 对账两侧类型集合（以 **完全限定名 FQN** 为键，断言一致）：
#   左侧 = 公共 API 快照 test/NCMapper.Tests/PublicApi.NCMapper.approved.txt
#          （approved.txt 测试的单一事实源，PublicApiGenerator 格式）
#          → 按 `namespace X` 上下文把类型名补全为 FQN（NCMapper.IMapper 等）
#   右侧 = DocFX metadata 输出 docs/zh-CN/reference/api/*.yml（YamlMime 格式）
#          → 取 `commentId: T:NCMapper[.xxx]` 行（本程序集类型引用，去重后即类型页集合）；
#            泛型 arity 反引号（PagedList`1）剥离
#
# 设计性豁免（两侧各有规则，均不拦截，仅提示）：
#   MISSING 侧：[EditorBrowsable(EditorBrowsableState.Never)] 的公共类型 DocFX 不生成页面
#     （当前仅 NCMapper.GeneratedMapConfig — "仅供生成代码调用"）。
#   EXTRA 侧：被公共成员签名引用的 internal 类型会被 DocFX 一并收录，
#     但公共 API 快照正确地不含它们（当前：DeepCopyCompiler / DeepCopyTypeClassifier /
#     JsonDeepCopyProvider / SystemTextJsonSerializer）。
#
# 输出：
#   MISSING — 快照有、API 参考无 → 新公共 API 未被 DocFX 收录（门禁拦截）
#   EXTRA   — API 参考有、快照无 → 快照过期（approved.txt 未随 API 变更重新生成，门禁拦截）
#   一致    — 打印统计，exit 0
#
# 用法（仓库根或任意目录均可）：
#   ./scripts/check-api-coverage.sh
# 前置：先运行 `dotnet docfx metadata`（缺失输出时报 exit 2 并提示）
# 退出码：0=一致  1=不一致（门禁拦截）  2=前置缺失（快照或 metadata 输出不存在）
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

approved="$repo_root/test/NCMapper.Tests/PublicApi.NCMapper.approved.txt"
api_dir="$repo_root/docs/zh-CN/reference/api"
src_dir="$repo_root/src/NCMapper"

if [ ! -f "$approved" ]; then
  echo "[ERROR] 公共 API 快照缺失：$approved" >&2
  exit 2
fi
if [ ! -d "$api_dir" ]; then
  echo "[ERROR] DocFX metadata 输出缺失：$api_dir" >&2
  echo "        请先运行：dotnet docfx metadata" >&2
  exit 2
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# --- 左侧：approved.txt 公共类型 FQN ---
# PublicApiGenerator 格式：`namespace X` 行（大括号另起一行）内缩进声明
#   public [sealed |abstract |partial |static] {class|interface|struct|enum|record} Name[<T,...>] [: bases]
# awk 跟踪当前 namespace，类型名补全为 ns.Name；泛型参数 <...> 剥离。
awk '
  /^[[:space:]]*namespace[[:space:]]+[A-Za-z_][A-Za-z0-9_.]*[[:space:]]*([;{]|$)/ {
    ns = $2
    sub(/;$/, "", ns)
    sub(/\{$/, "", ns)
    next
  }
  /^[[:space:]]*public[[:space:]]+((sealed|abstract|partial|static)[[:space:]]+)*(class|interface|struct|enum|record)[[:space:]]+[A-Za-z_][A-Za-z0-9_]*/ {
    line = $0
    sub(/^[[:space:]]*public[[:space:]]+/, "", line)
    sub(/^((sealed|abstract|partial|static)[[:space:]]+)+/, "", line)
    sub(/^(class|interface|struct|enum|record)[[:space:]]+/, "", line)
    sub(/[[:space:]].*$/, "", line)   # 剥离基类/泛型参数等尾部
    sub(/<.*$/, "", line)
    if (ns != "") print ns "." line
    else print line
  }
' "$approved" | sort -u > "$tmp/approved.txt"

# --- 右侧：metadata yml（YamlMime 格式）中本程序集类型的 commentId ---
# YamlMime 的 items 为扁平列表：类型页 / 成员页 / 类型参数 / 外部类型引用混排，
# 无法靠缩进区分；但本程序集公共类型的 commentId 必为 T:NCMapper[.xxx]，
# 且每个公共类型至少出现一次（自身条目），引用去重后集合 = 类型页集合。
# 泛型 arity 有两种写法，须都归一化为裸类型名（与左侧对齐）：
#   反引号 arity（PagedList`1 / FluentTypeConfig`2）→ 剥离 `N
#   模板占位符变体（FluentTypeConfig{``0,``1}）→ 剥离 {...}
# 两类写法指向同一类型，去重后与 approved.txt 的 `FluentTypeConfig<TSource, TTarget>` 对齐。
{
  grep -h 'commentId: T:NCMapper' "$api_dir"/*.yml 2>/dev/null \
    | sed -E 's/.*commentId: T://; s/`[0-9]+//g; s/\{.*$//'
} | sort -u > "$tmp/metadata.txt"

approved_count="$(wc -l < "$tmp/approved.txt" | tr -d ' ')"
metadata_count="$(wc -l < "$tmp/metadata.txt" | tr -d ' ')"

missing="$(comm -23 "$tmp/approved.txt" "$tmp/metadata.txt")"
extra="$(comm -13 "$tmp/approved.txt" "$tmp/metadata.txt")"

# --- 设计性豁免（查源码声明处） ---
# 声明行定位：src/NCMapper 内首个 `... <Kind> <Base>` 顶层声明
find_decl_line() {
  local fq="$1" base
  base="${fq##*.}"
  grep -rnE "(class|interface|struct|enum|record)[[:space:]]+${base}([[:space:]:<{(]|$)" \
    "$src_dir" --include='*.cs' 2>/dev/null | head -1 || true
}

# MISSING 侧：[EditorBrowsable(Never)] 的公共类型 DocFX 不生成页面（查声明处 ±3 行）
is_editor_browsable_never() {
  local fq="$1" decl file lineno start
  decl="$(find_decl_line "$fq")"
  [ -z "$decl" ] && return 1
  file="${decl%%:*}"
  lineno="${decl#*:}"; lineno="${lineno%%:*}"
  start=$(( lineno > 4 ? lineno - 3 : 1 ))
  sed -n "${start},$(( lineno + 1 ))p" "$file" | grep -q 'EditorBrowsable(EditorBrowsableState.Never)'
}

# EXTRA 侧：internal 类型被公共成员签名引用时 DocFX 一并收录（快照正确地不含它们）
is_internal_type() {
  local fq="$1" decl rest
  decl="$(find_decl_line "$fq")"
  [ -z "$decl" ] && return 1
  rest="${decl#*:}"; rest="${rest#*:}"   # 去掉 文件:行号: 前缀
  printf '%s\n' "$rest" | grep -qE '(^|[[:space:]({;])internal[[:space:]]'
}

exempt=""
if [ -n "$missing" ]; then
  remaining=""
  for t in $missing; do
    if is_editor_browsable_never "$t"; then
      exempt="${exempt}${t}\n"
    else
      remaining="${remaining}${t}\n"
    fi
  done
  missing="$(printf '%b' "$remaining" | sed '/^$/d')"
fi

exempt_internal=""
if [ -n "$extra" ]; then
  remaining=""
  for t in $extra; do
    if is_internal_type "$t"; then
      exempt_internal="${exempt_internal}${t}\n"
    else
      remaining="${remaining}${t}\n"
    fi
  done
  extra="$(printf '%b' "$remaining" | sed '/^$/d')"
fi

status=0
missing_count=0; extra_count=0
[ -n "$missing" ] && missing_count="$(printf '%s\n' "$missing" | wc -l | tr -d ' ')"
[ -n "$extra" ] && extra_count="$(printf '%s\n' "$extra" | wc -l | tr -d ' ')"

if [ -n "$missing" ]; then
  status=1
  echo "[MISSING] 快照有、API 参考无（$missing_count 项）："
  printf '%s\n' "$missing"
fi
if [ -n "$extra" ]; then
  status=1
  echo "[EXTRA] API 参考有、快照无（快照过期，需重新生成 approved.txt，$extra_count 项）："
  printf '%s\n' "$extra"
fi

if [ -n "$exempt" ]; then
  echo "[豁免] EditorBrowsable(Never)，DocFX 不生成页面（设计性，不计入拦截）："
  printf '%b' "$exempt"
fi
if [ -n "$exempt_internal" ]; then
  echo "[豁免] internal 类型（被公共签名引用，DocFX 一并收录；快照正确地不含，不计入拦截）："
  printf '%b' "$exempt_internal"
fi

if [ "$status" -ne 0 ]; then
  echo "结果：不一致（approved=$approved_count, metadata=$metadata_count）"
  exit 1
fi

echo "API 参考覆盖率对账通过：approved.txt 与 docs/zh-CN/reference/api 类型 FQN 集合一致（$approved_count 个公共类型）。"
exit 0
