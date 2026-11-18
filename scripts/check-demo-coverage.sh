#!/usr/bin/env bash
# check-demo-coverage.sh — 演示覆盖对账（告警版）
#
# 目的：防"文档页与可运行演示脱钩"再次发生——7 类零演示缺口当年靠人肉盘点发现，
# 本脚本把覆盖对账机械化（三查双向），让缺口在本地构建时显式暴露。
#
# 三查（双向对账）：
#   ① 操作页 → 演示：how-to 操作页（含 getting-started 教学页）至少挂 1 个 [!code-csharp] 引用
#   ② 引用 → 登记：文档引用的 samples 源文件是否在 samples-index.md 登记行中
#   ③ 登记 → 引用：samples-index.md 每行登记的关键 region 是否真有文档页引用（防空登记）
#
# 输出：[warn] 清单 + [info] 覆盖统计；退出码恒 0（告警版——只提醒不拦截构建，
#       操作页覆盖不足不应阻塞文档发布，由维护者按清单补挂或登记豁免）。
# 豁免清单写死在下方 EXEMPT_PAGES（新增豁免 = 修改本脚本的有意识动作，须在变更日志登记）。
# 用法（仓库根执行）：./scripts/check-demo-coverage.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

docs_dir="$repo_root/docs/zh-CN"
index_md="$repo_root/docs/zh-CN/reference/samples-index.md"

for f in "$docs_dir" "$index_md"; do
  if [ ! -e "$f" ]; then
    echo "[ERROR] 对账源缺失：$f" >&2
    exit 2
  fi
done

# ---- 豁免页（相对 docs/zh-CN 的路径；无可运行演示属页面性质而非缺漏）----
# 豁免理由：FAQ/陷阱为问答清单形态无教学演示位；安装指引无代码演示位
EXEMPT_PAGES=(
  "how-to/faq.md"
  "how-to/pitfalls.md"
  "getting-started/installation.md"
)

is_exempt() {
  local rel="$1"
  for e in "${EXEMPT_PAGES[@]}"; do
    [ "$rel" = "$e" ] && return 0
  done
  return 1
}

warns=0

# ---- ① 操作页 → 演示 ----
# 操作页集合 = how-to 全部 md + getting-started 教学页（overview/quickstart/tutorial-15min）
total_pages=0
covered_pages=0
missing_pages=()
while IFS= read -r md; do
  rel="${md#"$docs_dir"/}"
  is_exempt "$rel" && continue
  total_pages=$((total_pages + 1))
  if grep -q '\[!code-csharp' "$md"; then
    covered_pages=$((covered_pages + 1))
  else
    missing_pages+=("$rel")
  fi
done < <(find "$docs_dir/how-to" "$docs_dir/getting-started" -name '*.md' | sort)

for rel in "${missing_pages[@]:-}"; do
  [ -z "$rel" ] && continue
  echo "[warn] 操作页无演示引用：$rel（判据：每个操作页至少挂 1 个可运行片段）"
  warns=$((warns + 1))
done

# ---- ② 引用 → 登记 ----
# 提取全站 DFM 引用的 源文件路径 + region 名，对照 samples-index 表行
# title 段兼容转义方括号形态 `\[..\]`（66 条盲区结清）——
#   title 体＝(反斜杠+任意字符 | 非右方括号)*；普通形态命中集合零收紧（新旧提取对 comm 对账
#   旧有新无＝∅，实测两环境一致）。
refs_file="$(mktemp)"
while IFS= read -r md; do
  grep -hoE '\[!code-csharp\[(\\.|[^]])*\]\(~/samples/NCMapper\.Samples/[^)]+\)' "$md" 2>/dev/null \
    | sed -E 's/^\[!code-csharp\[(\\.|[^]])*\]\(~\/samples\/NCMapper\.Samples\/([^#)]+)#([^)]+)\)/\2|\3/' >> "$refs_file" || true
done < <(find "$docs_dir" -name '*.md')
refs_file_n="$(mktemp -u)"
sort -u "$refs_file" > "$refs_file_n" 2>/dev/null || sort -u "$refs_file" | tee "$refs_file_n" >/dev/null

unregistered=0
while IFS='|' read -r src region; do
  [ -z "$src" ] && continue
  if ! grep -qF "$src" "$index_md"; then
    echo "[warn] 引用未登记：$src（引用页须在 samples-index 对应场景行登记）"
    unregistered=$((unregistered + 1))
  fi
done < "$refs_file_n"
warns=$((warns + unregistered))

# ---- ③ 登记 → 引用 ----
# samples-index 每行"关键 region"列的 region 名应在全站文档中被引用
unreferenced=0
while IFS= read -r region; do
  [ -z "$region" ] && continue
  if ! grep -rqF "#$region)" "$docs_dir"; then
    echo "[warn] 登记行无页面引用：region $region（samples-index 已登记但无文档页挂接，反向缺口）"
    unreferenced=$((unreferenced + 1))
  fi
done < <(sed -nE 's/^\| .*\| ([A-Za-z0-9_、.]+) \|$/\1/p' "$index_md" | tr '、' '\n' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' | grep -E '^[A-Za-z][A-Za-z0-9_]+$' | sort -u)
warns=$((warns + unreferenced))

# ---- ④ DFM 绑定区零调用点 —— 观察列 ----
# 判据：对每个文档引用 region，取区内首个 static 方法名；调用点＝同文件区外裸调用
#       （前无 '.'、非定义行）＋跨文件 "Class.Name(" 引用；零调用点 → warn 入列。
#       仅观察不判红：并入 warns 统计但不改退出码（本脚本退出码恒 0 语义不变）。
zero_call=0
# 解释器显式探测（ubuntu runner 无裸 python 时旧形态 heredoc 行 127
# 而告警版 exit 恒 0＝观察列假静默——从本体封死；docs.yml step 级 shim 保留＝双保险零改动）：
# python3 优先 ∧ python 回退；两者皆缺＝显式 [warn] 声明跳过（非静默空列）。
cov_py="$(command -v python3 || command -v python)"
if [ -z "$cov_py" ]; then
  echo "[warn] DFM 绑定区零调用点观察列：python3/python 均不可用——本轮显式跳过（非静默空列）"
  zero_call_lines=""
else
  zero_call_lines="$("$cov_py" - "$repo_root" "$refs_file_n" <<'PYEOF'
import re, sys
from pathlib import Path
root = Path(sys.argv[1]); refs = Path(sys.argv[2])
SAMP = root / "samples" / "NCMapper.Samples"
pairs = set()
for ln in refs.read_text(encoding="utf-8").splitlines():
    if "|" in ln:
        s, r = ln.split("|", 1)
        if s.strip():
            pairs.add((s.strip(), r.strip()))
out = []
texts = {}
for q in SAMP.rglob("*.cs"):
    if "\\bin\\" in str(q) or "\\obj\\" in str(q):
        continue
    texts[q] = q.read_text(encoding="utf-8-sig")
for src, region in sorted(pairs):
    p = SAMP / src
    if not p.is_file():
        continue
    lines = p.read_text(encoding="utf-8-sig").splitlines()
    s = None; e = None
    for i, l in enumerate(lines):
        if re.match(r"^\s*#region\s+" + re.escape(region) + r"\s*$", l):
            s = i
        elif s is not None and e is None and re.match(r"^\s*#endregion", l) and i > s:
            e = i
    if s is None or e is None:
        continue
    name = None
    for l in lines[s+1:e]:
        m = re.search(r"(?:public|internal)\s+static\b.*\b([A-Za-z_]\w*)\s*(?:<[^<>]*>)?\s*\(", l)
        if m:
            name = m.group(1)
            break
    if not name or not re.fullmatch(r"[A-Za-z_]\w*", name):
        continue
    cls = None
    for l in lines:
        m = re.search(r"\bclass\s+([A-Za-z_]\w*)", l)
        if m: cls = m.group(1); break
    calls = 0
    pat_bare = re.compile(r"\b" + re.escape(name) + r"\s*[<(]")
    for i, l in enumerate(lines):
        if s <= i <= e:
            continue
        if re.search(r"\b(?:public|internal)\s+static\b", l):
            continue
        if pat_bare.search(l):
            calls += 1
    if cls:
        pat_x = re.compile(re.escape(cls) + r"\." + re.escape(name) + r"\b")
        for q, t in texts.items():
            if q == p:
                continue
            calls += len(pat_x.findall(t))
    if calls == 0:
        out.append(f"{src}#{region} (method {name})")
print("\n".join(out))
PYEOF
)"
fi
if [ -n "$zero_call_lines" ]; then
  while IFS= read -r z; do
    [ -z "$z" ] && continue
    echo "[warn] DFM 绑定区零调用点：$z（区已绑定文档但 samples 内无调用——方法体运行时语义不被执行，观察列不判红）"
    zero_call=$((zero_call + 1))
  done <<< "$zero_call_lines"
fi

# ---- ⑤ 602 场景打印计数漂移守卫 —— 观察列（因果链）----
# 因果链：「不挂 Run()」不保证注册场面与打印计数
#   零变化——声明式注册表是全局面，Ct* record 族入表曾致 602 打印确定性变化：
#   Count 75→81／Assembly results 77→83／Error 级 6→7（21/23 场景日志字节全等、604 计时噪声另计）。
# 基线（现态实测钉死，读数落探索记录）：
#   运行时打印面（dotnet run --project samples/NCMapper.Samples -- --scenario 602 直读；
#   仅作注释基线，不属本脚本判据面）：Count: 81 ／ Assembly results: 83 ／ Error 级（"Level": 2）＝7
#   静态注册场面（守卫提取面＝上述打印的驱动源，文本计数零运行时依赖）：
#     R1 samples 全库 [MapFrom(typeof 声明计数（剥 obj/bin 产物面）＝ 50
#     R2 ScenarioRegistry 场景注册行数（101-109/201-202/301/401/501-503/601-607）＝ 23
# 漂移＝[warn] 入列不判红（沿 ④ 观察列先例＝本脚本退出码恒 0 语义不变）；
# 604 计时噪声排除口径＝守卫只读静态注册场面，不触任何运行时打印/计时行；
# 场面变化时更新 R1/R2 常量＝修改本脚本的有意识动作（F8 同族，须附逐条理由）。
S602_MAPFROM_BASE=50    # R1 基线（ffe4541 态实测钉死）
S602_SCENARIO_BASE=23   # R2 基线（同上）
mapfrom_decl="$(grep -r --include='*.cs' -E '\[MapFrom\(typeof' "$repo_root/samples" 2>/dev/null | grep -vE '/(obj|bin)/' | wc -l || true)"
scenario_reg="$(grep -cE '^[[:space:]]+new\([0-9]+, ' "$repo_root/samples/NCMapper.Samples/ScenarioRegistry.cs" 2>/dev/null || true)"
if [ "$mapfrom_decl" != "$S602_MAPFROM_BASE" ] || [ "$scenario_reg" != "$S602_SCENARIO_BASE" ]; then
  echo "[warn] 602 计数守卫：注册场面漂移（R1 [MapFrom(typeof 声明=$mapfrom_decl 基线=$S602_MAPFROM_BASE／R2 场景注册=$scenario_reg 基线=$S602_SCENARIO_BASE）——602 打印 Count/Assembly results/Error 级随注册场面确定性随动（基线见上方注释），须复核并按有意识动作更新常量；观察列不判红）"
fi

# ---- 覆盖统计 ----
pct=0
[ "$total_pages" -gt 0 ] && pct=$((covered_pages * 100 / total_pages))
echo "[info] 操作页覆盖：$covered_pages/$total_pages（${pct}%），豁免页 ${#EXEMPT_PAGES[@]} 个：${EXEMPT_PAGES[*]}"
echo "[info] 对账结果：引用未登记 $unregistered 处、登记无引用 $unreferenced 处、无演示页 ${#missing_pages[@]} 个"

rm -f "$refs_file" "$refs_file_n" 2>/dev/null || true
# 告警版：退出码恒 0（不拦截构建）
exit 0
