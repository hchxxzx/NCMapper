#!/usr/bin/env bash
# check-summaries.sh — API 参考页 summary 与源码 XML 注释逐字一致
# 口径：类型级条目全量核对（Class/Interface/Enum/Struct）；
#       yml 侧（DocFX 产物，<para>→<p>、实体编码）与源码侧（/// 注释 <summary> 段）
#       均剥离 XML 标记、解码实体、空白归一后逐字比较。
# 用法：bash scripts/check-summaries.sh   （在仓库根执行；exit 0=全一致 / 1=有不一致）
set -u
cd "$(dirname "$0")/.."
API=docs/zh-CN/reference/api
SRC=src/NCMapper
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

# ---------- 1) yml 类型条目: uid \t summary(归一) ----------
awk '
  function norm(s) {
    gsub(/&lt;/, "<", s); gsub(/&gt;/, ">", s)
    gsub(/&quot;/, "\"", s); gsub(/&#39;/, "\047", s)
    gsub(/&amp;/, "\\&", s)
    gsub(/<see cref="[^"]*">/, "", s); gsub(/<see langword="[^"]*">/, "", s)
    gsub(/<\/?(p|c|b|i|em|strong|para|code|paramref|typeparam|exception|value|ul|li)[^>]*>/, " ", s)
    gsub(/<[^>]*>/, " ", s)
    gsub(/[ \t\r\n]+/, " ", s)
    sub(/^ /, "", s); sub(/ $/, "", s)
    return s
  }
  function flush() {
    if (uid != "" && istype) print uid "\t" norm(s)
    uid = ""; istype = 0; s = ""; inblock = 0
  }
  /^- uid: / { flush(); uid = $0; sub(/^- uid: /, "", uid); next }
  /^  type: (Class|Interface|Enum|Struct)$/ { istype = 1; next }
  istype && /^  summary: >-$/ { inblock = 1; next }
  istype && !inblock && /^  summary: / { s = $0; sub(/^  summary: /, "", s); inblock = 9; next }
  inblock == 1 {
    if ($0 == "") { s = s "\n"; next }
    if ($0 ~ /^    /) { line = $0; sub(/^    /, "", line); s = s line " "; next }
    inblock = 0
  }
  inblock == 9 { inblock = 0 }   # 单行标量：下一行即结束
  END { flush() }
' "$API"/*.yml > "$TMP/yml.tsv"

# ---------- 2) 每个类型条目找源码 <summary> 并比较 ----------
ok=0; bad=0; nosrc=0
while IFS=$'\t' read -r uid ysum; do
  [ -z "$uid" ] && continue
  base=${uid##*.}
  base=${base%%\`*}
  case "$base" in *[!A-Za-z0-9_]*) continue;; esac
  # 候选文件（声明行带词边界，排除 NCMapperMapper 匹配 Mapper 之类）
  declre="^[[:space:]]*((public|internal|protected|private)[[:space:]]+)*((sealed|static|abstract|partial|readonly)[[:space:]]+)*(class|interface|struct|enum|record)[[:space:]]+${base}([[:space:]({:<]|$)"
  cand=$(grep -lE "$declre" "$SRC"/*.cs 2>/dev/null)
  src=""
  for f in $cand; do
    ln=$(grep -nE "$declre" "$f" | head -1 | cut -d: -f1)
    [ -z "$ln" ] && continue
    t=$(awk -v L="$ln" '
      { lines[NR] = $0 }
      END {
        # 声明行向上：先跳过属性行/空行（[AttributeUsage] 等可夹在注释与声明之间），再取连续 /// 块
        i = L - 1
        while (i >= 1 && (lines[i] ~ /^[[:space:]]*$/ || lines[i] ~ /^[[:space:]]*\[/)) i--
        dend = i; dstart = i
        while (dstart >= 1 && lines[dstart] ~ /^\/\//) dstart--
        s = ""
        for (k = dstart + 1; k <= dend; k++) { line = lines[k]; sub(/^[[:space:]]*\/\/\/[[:space:]]?/, "", line); s = s line "\n" }
        if (match(s, /<summary>/)) { rest = substr(s, RSTART + RLENGTH); s = (match(rest, /<\/summary>/) ? substr(rest, 1, RSTART - 1) : rest) } else s = ""
        gsub(/&lt;/, "<", s); gsub(/&gt;/, ">", s)
        gsub(/&quot;/, "\"", s); gsub(/&#39;/, "\047", s)
        gsub(/&amp;/, "\\&", s)
        gsub(/<see cref="[^"]*">/, "", s); gsub(/<see langword="[^"]*">/, "", s)
        gsub(/<\/?(p|c|b|i|em|strong|para|code|paramref|typeparam|exception|value|ul|li)[^>]*>/, " ", s)
        gsub(/<[^>]*>/, " ", s)
        gsub(/[ \t\r\n]+/, " ", s)
        sub(/^ /, "", s); sub(/ $/, "", s)
        print s
      }' "$f")
    [ -n "$t" ] && { src="$t"; break; }
  done
  if [ -z "$src" ]; then
    nosrc=$((nosrc + 1))
    echo "NO-SRC     $uid"
    continue
  fi
  if [ "$src" = "$ysum" ]; then
    ok=$((ok + 1))
  else
    bad=$((bad + 1))
    echo "MISMATCH   $uid"
    echo "  yml: $ysum"
    echo "  src: $src"
  fi
done < "$TMP/yml.tsv"

total=$((ok + bad + nosrc))
echo "summary 逐字一致: $ok / 不一致: $bad / 无源码注释: $nosrc (类型条目共 $total)"
[ "$bad" -eq 0 ] && [ "$nosrc" -eq 0 ]
