#!/usr/bin/env bash
# =====================================================================
# check-region-lang.sh — samples region 注释语言门禁（拦截版）
#
# 规范：《NCMapper samples region 编写规范》R-1~R-7（本脚本为其执行体）
# 判据三要素：
#   ① 分组键 = (源文件路径, region 名)——泄漏按 region 计，同文件两 region 互不影响
#   ② 豁免键 = 同二元组全等匹配（EXEMPT_REGIONS 初始空，新增须附逐条理由）；
#      键面相对/绝对两写法归一到「相对仓库根」规范形后比对，
#      在册未命中＝[EXEMPT_UNMATCHED] exit 2（fail-loud）；
#      自证锚二元组不可豁免＝[EXEMPT_SELFTEST_FORBIDDEN] exit 1
#   ③ 自证植入 = samples/NCMapper.Samples/_RegionLangSelftest.cs :: Selftest_ChineseLeak
#      常驻必报（R-7）；该文件不删除、不入 DFM 引用、不进后续归零范围
#
# 判据流水线：
#   扫描面 = 全库 samples region（DFM 引用面解析仅作定位手段复用）
#   region 定位复用 check-doc-sample-refs.sh 精确行正则（避免前缀误匹配）
#   逐行分类：空行跳过 → region 内出现 /* 或 """ 发防御 warn（双保险）
#   → ^\s*/// 发 DOC_IN_REGION warn（不计违规、不影响退出码，R-1 边界信号）
#   → 整行 // 与行尾 // 剥字符串字面量（"..." 与 '...' 含转义）后查 CJK
#     （U+3000-303F ∪ U+4E00-9FFF ∪ U+FF01-FF60；/// 与 // 双查），
#     命中报 [CJK_IN_COMMENT] 文件:行 region名 原文
#   汇总「扫描 N region / 违规 M 条」；M>0 → exit 1
#
# 退出码：0=region 内注释全英文  1=存在 CJK_IN_COMMENT 违规（拦截）
#         2=前置缺失（扫描面为空=假门禁防护，对齐 check-doc-sample-refs.sh 口径）
# 基线口径：本门上线即红属预期态（实测 114 条涉 29 区 + 自证植入 2 条），后续边界重构与注释英文化为归零批次。
# 用法（仓库根执行）：bash scripts/check-region-lang.sh [文件或目录 ...]——缺省扫描 samples/（排除 obj/bin），显式参数用于测试回归。
# =====================================================================
set -uo pipefail
export LC_ALL=C

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

# ---- 豁免清单（初始为空；每项「相对路径::region名」全等匹配，须附逐条理由注释）----
# 豁免键归一化：键的「相对仓库根」与「绝对」两种写法在构造/比对前双向归一到
#   同一规范形（绝对形折去 repo_root 前缀∧awk 侧 FILENAME 同折），实测两类缺陷就此结清：
#   ①绝对形键静默命中豁免致假出绿／②相对形键缺省扫描不命中白配——两形态行为一致；
#   且归一后仍零命中的在册豁免项发 [EXEMPT_UNMATCHED] 硬读数 exit 2（fail-loud，白配不静默）。
# 自证机械防呆：常驻自证锚（_RegionLangSelftest.cs::Selftest_ChineseLeak，:20/:21
#   两枚）不可被豁免——豁免项命中该锚即发 [EXEMPT_SELFTEST_FORBIDDEN] 硬读数 exit 1，
#   该豁免项不入 exempt_map（常驻两枚违规原样保留）；锚检测内嵌 awk 内核＝任何载体
#   （脚本数组/外部注入 csv）喂入同一判，不依赖配置层自觉。
EXEMPT_REGIONS=()

# ---- 收集目标 cs 文件 ----
targets=()
if [ "$#" -gt 0 ]; then
  for t in "$@"; do
    if [ -f "$t" ]; then targets+=("$t")
    elif [ -d "$t" ]; then
      while IFS= read -r -d '' f; do targets+=("$f"); done \
        < <(find "$t" -type f -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 | sort -z)
    else
      echo "[error] 参数不存在：$t" >&2; exit 2
    fi
  done
else
  while IFS= read -r -d '' f; do targets+=("$f"); done \
    < <(find "$repo_root/samples" -type f -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 | sort -z)
fi
if [ "${#targets[@]}" -eq 0 ]; then
  echo "[error] 未收集到任何 .cs 文件——扫描面为空即假门禁，拒绝出绿。" >&2
  exit 2
fi

# ---- awk 判据内核（字节级 UTF-8，LC_ALL=C：CJK 三字节序列按前导/续字节区间判定）----
awk_prog="$(mktemp)"
trap 'rm -f "$awk_prog"' EXIT
cat > "$awk_prog" <<'AWK_EOF'
function ord_init(   i) { for (i = 0; i < 256; i++) _ord[sprintf("%c", i)] = i }
# 剥字符串字面量：双引号/单引号串整体移除（含 \ 转义对）。
# 未闭合引号回退为普通字符（选项①，修复回归）：
# 修复前遇未配对 ' 吞至行尾，致 `// don't 中文` 形态漏报；修复后进入字符串态前先探测
# 剩余串是否存在同类闭合引号（跳过 \ 转义对），无闭合则该引号按普通字符输出并继续扫描。
function has_close(s, from, q,   i2, n2, c2) {
  i2 = from; n2 = length(s)
  while (i2 <= n2) {
    c2 = substr(s, i2, 1)
    if (c2 == "\\") { i2 += 2; continue }
    if (c2 == q) return 1
    i2++
  }
  return 0
}
function strip_strings(s,   out, i, n, c, q) {
  out = ""; i = 1; n = length(s)
  while (i <= n) {
    c = substr(s, i, 1)
    if ((c == "\"" || c == "'") && has_close(s, i + 1, c)) {
      q = c; i++
      while (i <= n) {
        c = substr(s, i, 1)
        if (c == "\\") { i += 2; continue }
        if (c == q) { i++; break }
        i++
      }
      continue
    }
    out = out c; i++
  }
  return out
}
# CJK 集（UTF-8 字节级）：U+3000-303F ∪ U+4E00-9FFF ∪ U+FF01-FF60
# mawk 可移植性修复（等价换实现，判据语义一丝不动）：
# 根因实证（t7）：mawk 1.3.4 不支持 0xHH 十六进制数字字面量（0xE4 解析为 0——
# probe x=228 时 x==0xE4 为假、0xE3 作数值得 0），致区间比较全部失真、CJK 恒漏检
# （Linux「违规 0」）；_ord 查表本身在 mawk 下构造正常（实证 byte_via_ord=228）。
# 修复＝上述字面量一律换十进制（E3=227 EF=239 80=128 BF=191 E4=228 8F=143 B8=184
# E5=229 E9=233 BC=188 BD=189 81=129 A0=160），gawk/mawk 共用同一套判据，不分叉。
function has_cjk(s,   i, n, b1, b2, b3) {
  n = length(s)
  for (i = 1; i <= n - 2; i++) {
    b1 = _ord[substr(s, i, 1)]
    if (b1 < 227 || b1 > 239) continue
    b2 = _ord[substr(s, i + 1, 1)]; b3 = _ord[substr(s, i + 2, 1)]
    if (b2 < 128 || b2 > 191 || b3 < 128 || b3 > 191) continue
    if (b1 == 227 && b2 <= 143) return 1
    if (b1 == 228 && b2 >= 184) return 1
    if (b1 >= 229 && b1 <= 233) return 1
    if (b1 == 239 && ((b2 == 188 && b3 >= 129) || (b2 == 189 && b3 <= 160))) return 1
  }
  return 0
}
function norm_fn(f) {
  # FILENAME 归一化：repo_root 前缀折除→「相对仓库根」规范形；面外文件保持原形
  if (ROOTP != "" && index(f, ROOTP) == 1) return substr(f, length(ROOTP) + 1)
  return f
}
BEGIN {
  ord_init()
  ne = split(EXEMPTS, ex_arr, ",")
  for (k = 1; k <= ne; k++) {
    if (ex_arr[k] == "") continue
    # 自证机械防呆：命中自证锚的豁免项拒绝入册＋硬读数红——锚在 kernel＝任何载体同判
    if (ex_arr[k] ~ /_RegionLangSelftest\.cs::Selftest_ChineseLeak$/) {
      print "[EXEMPT_SELFTEST_FORBIDDEN] " ex_arr[k]
      forb++
      continue
    }
    exempt_map[ex_arr[k]] = 1
  }
  region = ""
}
FNR == 1 { region = ""; nfn = norm_fn(FILENAME) }
/^[ \t]*#region[ \t]+/ {
  region = $0
  sub(/^[ \t]*#region[ \t]+/, "", region)
  sub(/[ \t]+$/, "", region)
  regions++
  next
}
/^[ \t]*#endregion/ { region = ""; next }
region == "" { next }
/^[ \t]*$/ { next }
{
  if (index($0, "/*") > 0) {
    print "[BLOCK_COMMENT_IN_REGION] " FILENAME ":" FNR " " region " " $0; warns++; next
  }
  if (index($0, "\"\"\"") > 0) {
    print "[MULTILINE_STRING_IN_REGION] " FILENAME ":" FNR " " region " " $0; warns++; next
  }
  if ($0 ~ /^[ \t]*\/\/\//) {
    print "[DOC_IN_REGION] " FILENAME ":" FNR " " region " " $0; warns++; next
  }
  s = strip_strings($0)
  if (s ~ /^[ \t]*\/\//) c = s
  else { p = index(s, "//"); if (p == 0) next; c = substr(s, p) }
  if (has_cjk(c)) {
    key = nfn "::" region
    if (key in exempt_map) { exempted++; used[key]++; next }
    print "[CJK_IN_COMMENT] " FILENAME ":" FNR " " region " " $0
    viol++
  }
}
END {
  for (e in exempt_map) if (!(e in used)) { print "[EXEMPT_UNMATCHED] " e; un++ }
  printf "SUMMARY: 扫描 %d region / 违规 %d 条 / warn %d 条 / 豁免命中 %d 条\n", regions, viol, warns, exempted
  if (forb > 0) exit 1
  if (un > 0) exit 2
  exit (viol > 0 ? 1 : 0)
}
AWK_EOF

# 豁免键归一化：绝对形（repo_root 前缀）折为「相对仓库根」规范形再喂内核；
# 缺 '::' 分隔＝配置错 fail-loud exit 2（不静默）；面外绝对路径键保持原形（与 FILENAME 原形同侧可比）。
norm_exempt=()
for e in "${EXEMPT_REGIONS[@]:-}"; do
  [ -z "$e" ] && continue
  if [ "${e#*::}" = "$e" ]; then
    echo "[error] 豁免项缺 '::' 分隔（应为「相对路径::region名」）：$e" >&2
    exit 2
  fi
  p="${e%%::*}"; r="${e#*::}"
  case "$p" in "$repo_root"/*) p="${p#"$repo_root"/}";; esac
  norm_exempt+=("$p::$r")
done
exempt_csv="$(IFS=,; printf '%s' "${norm_exempt[*]:-}")"
awk -v EXEMPTS="$exempt_csv" -v ROOTP="$repo_root/" -f "$awk_prog" "${targets[@]}"
rc=$?
if [ "$rc" -eq 0 ]; then
  echo "[PASS] region 内注释语言全英文（豁免在册 ${#EXEMPT_REGIONS[@]} 项）"
fi
exit "$rc"
