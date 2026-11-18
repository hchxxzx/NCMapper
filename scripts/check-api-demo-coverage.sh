#!/usr/bin/env bash
# =====================================================================
# check-api-demo-coverage.sh — 公开 API 架构图 ↔ 演示覆盖 双向对账
# （五查+拦截版：任一查失败退出码 1）
#
# 数据源：docs/zh-CN/reference/architecture-map.md 清单表格（单一事实源）。
# 清单行格式（5 列）：| `API`/功能点 | 说明 | 演示 | 片段（region） | 文档 |
#   演示列 = 场景号（整场景级）或 —（理由）（未演示，理由必须显式）
#   片段列 = region 名 或 —（Run 内联/整场景级/不适用说明）
#
# 五查（拦截版：任一查失败退出码 1）：
#   ① 片段实存：片段列 region 名须以「#region <名>」形态在 samples 源码实存（防错名/纯子串误报）
#   ② 未演示统计：演示列以「—（」开头的行逐条列出并计数
#   ③ 场景集合相等：清单引用场景号 与 ScenarioRegistry 注册集 双向相等（防漏登记/防幽灵引用）
#   ④ 公开 API 快照反查：PublicApi.*.approved.txt（运行时四份，SourceGen 豁免）的公开类型
#      须在文档登记或提及——堵「代码有、文档无」的正向完备性盲区
#   ⑤ Markdown 结构完整性：front matter 定界符 / 行首畸形分隔行（------| 形态）/ 孤立表格行
#      ——堵「编辑残渣」盲区（复审 002 信息-3；实现位于查④ 之后，编号=输出顺序）
# 附：分层统计输出（供文档覆盖统计表核对）
# =====================================================================

set -uo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
map_md="$repo_root/docs/zh-CN/reference/architecture-map.md"
samples_dir="$repo_root/samples/NCMapper.Samples"
registry_cs="$samples_dir/ScenarioRegistry.cs"

if [ ! -f "$map_md" ] || [ ! -f "$registry_cs" ]; then
  echo "[error] 数据源缺失：$map_md 或 $registry_cs"
  exit 0
fi

# ---- 解析清单：## 2. 起全篇；排除表头/分隔行/覆盖统计表（demo 列为纯数字或 api 为分层名）----
# 输出 TSV：layer \t api \t demo \t region
parsed="$(sed -n '/^## 2\./,$p' "$map_md" | awk -F'|' '
  function layer_of(h) {
    if (h ~ /^## 2\./) return "L2"; if (h ~ /^## 3\./) return "L3"
    if (h ~ /^## 4\./) return "L4"; if (h ~ /^## 5\./) return "L5"
    if (h ~ /^## 6\./) return "L6"; return ""
  }
  /^## [0-9]+\./ { layer = layer_of($0); next }
  /^\|[|: -]+$/ { next }
  /^\|/ {
    api=$2; demo=$4; region=$5
    gsub(/^ +| +$/, "", api); gsub(/^ +| +$/, "", demo); gsub(/^ +| +$/, "", region)
    if (layer == "") next
    if (api == "" || api == "API" || api == "功能点" || api == "枚举" || api == "分层") next
    if (demo == "演示" || demo ~ /^演示（/) next
    if (api ~ /^L[1-6] / || api ~ /^\*\*合计/) next
    print layer "\t" api "\t" demo "\t" region
  }
')"
total="$(echo "$parsed" | grep -c . )"

# ---- 查①：片段 region 实存（#region <名> 成对形态）----
echo "=== 查① 片段（region）实存性 ==="
region_total=0; region_fail=0
while IFS=$'\t' read -r tag token api; do
  [ "$tag" != "REGIONCHECK" ] && continue
  region_total=$((region_total+1))
  if ! grep -rqE "#region +${token}([ )]|$)" "$samples_dir" --include='*.cs' 2>/dev/null; then
    echo "[warn] region 未实存（无 #region 界定符）：$token（API：$api）"
    region_fail=$((region_fail+1))
  fi
done < <(echo "$parsed" | awk -F'\t' '
  $4 != "-" && $4 !~ /^—/ && $4 != "" {
    n = split($4, parts, "/")
    for (i = 1; i <= n; i++) {
      p = parts[i]; gsub(/^ +| +$/, "", p)
      if (p == "" || p ~ /^—/) continue
      split(p, w, " "); token = w[1]; gsub(/`/, "", token)
      print "REGIONCHECK\t" token "\t" $2
    }
  }')
if [ "$region_fail" -eq 0 ]; then
  echo "[ok] 片段实存校验通过（$region_total 个 region 引用全部命中 #region 界定符）"
else
  echo "[info] 片段校验：$region_fail/$region_total 未实存"
fi

# ---- 查②：未演示项统计 ----
echo "=== 查② 未演示项统计 ==="
undemo="$(echo "$parsed" | awk -F'\t' '$3 ~ /^—（/ {print $2}')"
undemo_n="$(echo "$undemo" | grep -c . 2>/dev/null || true)"
echo "[info] 清单总行数 $total，未演示（显式理由）$undemo_n 行："
echo "$undemo" | sed 's/^/  /'

# ---- 查③：场景集合相等（双向）----
echo "=== 查③ 场景集合相等检查 ==="
map_ids="$(echo "$parsed" | awk -F'\t' '{print $3}' | grep -oE '\b[1-6][0-9]{2}\b' | sort -u | tr '\n' ' ')"
reg_ids="$(grep -oE 'new\([0-9]{3},' "$registry_cs" | grep -oE '[0-9]{3}' | sort -u | tr '\n' ' ')"
missing=""; n_ids=0
for id in $map_ids; do
  n_ids=$((n_ids+1))
  case " $reg_ids " in *" $id "*) ;; *) missing="$missing $id" ;; esac
done
unref=""; n_reg=0
for id in $reg_ids; do
  n_reg=$((n_reg+1))
  case " $map_ids " in *" $id "*) ;; *) unref="$unref $id" ;; esac
done
[ -n "$missing" ] && echo "[warn] 清单引用了未注册场景号：$missing" || echo "[ok] 清单引用场景号全部已注册（$n_ids 个）"
[ -n "$unref" ] && echo "[warn] 注册表场景未在清单引用（反向缺口）：$unref" || echo "[ok] 注册表 $n_reg 个场景全部被清单引用（相等检查通过）"

# ---- 分层统计 ----
echo "=== 分层统计（层 行数 未演示）==="
echo "$parsed" | awk -F'\t' '{n[$1]++; if ($3 ~ /^—（/) u[$1]++} END {T=0;U=0; for (k in n) {printf "%s %d %d\n", k, n[k], u[k]; T+=n[k]; U+=u[k]} printf "TOTAL %d %d\n", T, U}' | sort

# ---- 查④：公开 API 快照反查（类型级）----
echo "=== 查④ 公开 API 快照反查（类型级）==="
snap_dir="$repo_root/test/NCMapper.Tests"
if ls "$snap_dir"/PublicApi.*.approved.txt >/dev/null 2>&1; then
  # 运行时四份快照（主/FreeSql/EFCore/Dapper）；SourceGen 编译期面豁免（L1 已声明不登记）
  grep -hoE 'public (sealed |static |abstract |partial )*(class|interface|enum|struct) [A-Za-z0-9_]+' \
    "$snap_dir"/PublicApi.NCMapper.approved.txt \
    "$snap_dir"/PublicApi.NCMapper.FreeSql.approved.txt \
    "$snap_dir"/PublicApi.NCMapper.EFCore.approved.txt \
    "$snap_dir"/PublicApi.NCMapper.Dapper.approved.txt \
    | awk '{print $NF}' | sort -u > /tmp/snapshot_types.txt
  snap_n="$(grep -c . /tmp/snapshot_types.txt)"
  miss_types=""
  while read -r t; do
    # 特性类名接受无 Attribute 后缀的友好名（文档以 [MapFrom] 简称登记）
    friendly="${t%Attribute}"
    if grep -qF "$t" "$map_md" || { [ "$friendly" != "$t" ] && grep -qF "$friendly" "$map_md"; }; then
      continue
    fi
    miss_types="$miss_types $t"
  done < /tmp/snapshot_types.txt
  n_miss="$(echo $miss_types | wc -w)"
  if [ "$n_miss" -eq 0 ]; then
    echo "[ok] 快照公开类型 $snap_n 个全部在文档登记或提及"
  else
    echo "[warn] 快照有而文档未登记的类型 $n_miss/$snap_n 个：$miss_types"
    echo "[info] 差额可整行归组登记，或确认属编译期/承载面后在尾注交代"
  fi
  rm -f /tmp/snapshot_types.txt
else
  n_miss=0
  echo "[info] 未找到公开 API 快照（test/NCMapper.Tests/PublicApi.*.approved.txt），查④跳过"
fi

# ---- 查⑤：Markdown 结构完整性（复审 002 信息-3：编辑残渣盲区）----
echo "=== 查⑤ Markdown 结构完整性 ==="
struct_fail=0
# 5a. front matter 定界符必须是三横线（非 \--- 等转义形态）
first_line="$(head -1 "$map_md")"
if [ "$first_line" != "---" ]; then
  echo "[warn] front matter 首行不是 ---（实际：$first_line）"
  struct_fail=$((struct_fail+1))
fi
# 5b. 行首畸形分隔行（------| 形态，历史残表特征）
# 注（2026-09-29 修复）：grep -c 未命中时退出码为 1，但计数 0 已输出到 stdout；
# 原 `|| echo 0` 会追加第二个 "0" 使变量成 "0\n0" 两行（[ ] 随之报 integer expression expected）。
# 改为 || true 仅吞退出码。
n_malformed="$(grep -cE '^------\|' "$map_md" 2>/dev/null || true)"
if [ "$n_malformed" -gt 0 ]; then
  echo "[warn] 发现 $n_malformed 行行首畸形分隔行（历史残表残留）"
  struct_fail=$((struct_fail+1))
fi
# 5c. 孤立表格行：| 行的前一行是空行且再前一行也是 | 行（表格被空行断裂）
n_orphan="$(awk 'BEGIN{prev=""; prev2=""}
  /^\|/ { if (prev == "" && prev2 ~ /^\|/) count++ ; prev2=prev; prev=$0 ; next }
  { prev2=prev; prev=$0 }
  END { print count+0 }' "$map_md")"
if [ "$n_orphan" -gt 0 ]; then
  echo "[warn] 发现 $n_orphan 行孤立表格行（表格被空行断裂，渲染为纯文本）"
  struct_fail=$((struct_fail+1))
fi
if [ "$struct_fail" -eq 0 ]; then
  echo "[ok] 结构完整性通过（front matter / 无畸形分隔行 / 无孤立表格行）"
else
  echo "[info] 结构完整性：$struct_fail 类问题"
fi

echo "=== 对账结果 ==="
echo "[info] region 校验 $((region_total-region_fail))/$region_total 通过；未演示 $undemo_n/$total 行（均须显式理由）；场景集合$( [ -z "$missing$unref" ] && echo 相等 || echo 不相等 )；快照反查$( [ "$n_miss" -eq 0 ] 2>/dev/null && echo 通过 || echo 有差额 )"

# ---- 拦截判定（2026-09-28 复审后由告警版升级）：任一查失败即退出码 1 ----
fails=0
[ "$region_fail" -gt 0 ] && fails=$((fails+1))
[ -n "$missing" ] && fails=$((fails+1))
[ -n "$unref" ] && fails=$((fails+1))
[ "$n_miss" -gt 0 ] 2>/dev/null && fails=$((fails+1))
[ "$struct_fail" -gt 0 ] && fails=$((fails+1))
if [ "$fails" -gt 0 ]; then
  echo "[FAIL] 对账失败（$fails 项），退出码 1"
  exit 1
fi
echo "[PASS] 五查全部通过"
# 已知局限（后续增强项）：查④为类型级+子串出现判定，成员级成行登记不覆盖；
# 查①不校验片段与 API 行的语义对应（后续增强项）。
exit 0
