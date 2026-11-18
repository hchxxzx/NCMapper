# -*- coding: utf-8 -*-
"""生成 竞品测试 vs 前7整轮(竞品子集) 对比 HTML 报告。"""
import csv, statistics, json, os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
BASE = os.path.join(ROOT, "artifacts", "benchmarks", "baseline")
RIVDIR = os.path.join(BASE, "net10.0_20260708_132439")
FULLDIR = os.path.join(BASE, "net10.0_20260707_192721")
CMP = os.path.join(RIVDIR, "rivals_vs_full7.csv")
OUT = os.path.join(RIVDIR, "竞品测试vs前7对比报告_20260708.html")

def load_csv(path):
    with open(path) as f:
        return list(csv.DictReader(f))

def load_json(path):
    with open(path) as f:
        return json.load(f)

cmp_rows = load_csv(CMP)
meta_riv = load_json(os.path.join(RIVDIR, "meta.json"))
meta_full = load_json(os.path.join(FULLDIR, "meta.json"))

rows = []
for r in cmp_rows:
    rows.append((r['class'], float(r['rivals_ns']), float(r['full7_ns']), float(r['ratio_rivals_over_full7'])))

ratios = [rt for *_, rt in rows]
med = statistics.median(ratios)
riv_mean = statistics.mean([r for _, r, _, _ in rows])
full_mean = statistics.mean([fl for _, _, fl, _ in rows])
n = len(rows)
faster = sum(1 for *_, rt in rows if rt < 1)
slower = n - faster

# 排除 DeepCopy_Json 的稳健统计
norm = [(c, r, fl, rt) for c, r, fl, rt in rows if c != "DeepCopy_Json"]
norm_riv_mean = statistics.mean([r for _, r, _, _ in norm])
norm_full_mean = statistics.mean([fl for _, _, fl, _ in norm])
norm_ratio = norm_riv_mean / norm_full_mean

def fmt(x): return f"{x:.3f}"

# 表格行
trs = []
for c, r, fl, rt in rows:
    if c == "DeepCopy_Json":
        cls = "row-anom"
        tag = '<span class="tag-anom">前7异常·不可比</span>'
        ratio_cell = f'<b>{rt:.1f}</b>'
    elif rt < 1:
        cls = "row-fast"
        tag = '<span class="tag-fast">竞品更快</span>'
        ratio_cell = f'{rt:.4f}'
    else:
        cls = "row-slow"
        tag = '<span class="tag-slow">竞品更慢</span>'
        ratio_cell = f'{rt:.4f}'
    trs.append(f'<tr class="{cls}"><td class="cls">{c}</td><td>{fmt(r)}</td><td>{fmt(fl)}</td><td class="num">{ratio_cell}</td><td>{tag}</td></tr>')

html = f"""<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>竞品测试 vs 前7 对比报告</title>
<style>
* {{ box-sizing: border-box; }}
body {{ font-family: -apple-system, "Microsoft YaHei", sans-serif; margin: 0; padding: 28px; background: #F5F7FA; color: #2C2C2A; }}
h1 {{ font-size: 22px; margin: 0 0 4px; }}
.sub {{ color: #666; font-size: 13px; margin-bottom: 18px; }}
.card {{ background: #fff; border: 1px solid #E3E8EF; border-radius: 10px; padding: 16px 18px; margin-bottom: 16px; }}
.warn {{ background: #FFF7E6; border: 1px solid #FFD591; }}
.danger {{ background: #FFF1F0; border: 1px solid #FFA39E; }}
.card h2 {{ font-size: 15px; margin: 0 0 10px; }}
.kpis {{ display: flex; flex-wrap: wrap; gap: 12px; }}
.kpi {{ flex: 1; min-width: 150px; background: #F7F9FC; border: 1px solid #E3E8EF; border-radius: 8px; padding: 12px; }}
.kpi .v {{ font-size: 22px; font-weight: 700; color: #1F5FB0; }}
.kpi .l {{ font-size: 12px; color: #666; margin-top: 4px; }}
table {{ border-collapse: collapse; width: 100%; font-size: 13px; }}
th, td {{ border: 1px solid #E3E8EF; padding: 6px 10px; text-align: left; }}
th {{ background: #EEF3FA; color: #2C2C2A; }}
td.num {{ text-align: right; font-variant-numeric: tabular-nums; }}
td.cls {{ font-family: Consolas, monospace; color: #444441; }}
.row-fast td.num {{ color: #237804; font-weight: 600; }}
.row-slow td.num {{ color: #C0392B; font-weight: 600; }}
.row-anom {{ background: #FFF7E6; }}
.tag-fast {{ color: #237804; font-size: 11px; }}
.tag-slow {{ color: #C0392B; font-size: 11px; }}
.tag-anom {{ color: #AD6800; font-size: 11px; }}
.note {{ font-size: 12px; color: #555; line-height: 1.7; }}
ul {{ margin: 6px 0; padding-left: 20px; }}
code {{ background: #EEF3FA; padding: 1px 5px; border-radius: 4px; font-size: 12px; }}
</style></head><body>
<h1>竞品测试 vs 前7 整轮对比报告</h1>
<div class="sub">竞品测试 RUNDIR=net10.0_20260708_132439 (commit {meta_riv.get('commit')}) ｜ 前7 RUNDIR=net10.0_20260707_192721 (commit {meta_full.get('commit')}) ｜ 生成于 2026-07-08 20:24</div>

<div class="card danger">
  <h2>⚠️ 方法学警示：本对比为跨协议 + 跨 run，不可定量</h2>
  <div class="note">
  <ul>
    <li><b>协议不同</b>：前7 = 7变体<b>同场</b>单进程跑完（PGO 跨全部变体饱和、进程内资源竞争）；竞品 = 竞品变体<b>单独冷启动</b>独占进程（PGO 仅跨竞品）。单变体在"同场"测得偏慢是协议效应，非竞品本身变慢。</li>
    <li><b>热态不同</b>：前7 跑于 07-07 19:27（启动不久/冷），竞品 跑于 07-08 13:24（机器热态）。热态通常更快。</li>
    <li><b>结论边界</b>：本对比只能看<b>量级与方向</b>，不能得出"竞品性能变化 X%"的定量结论。真正可比的"竞品回归"需未来同协议（<code>baseline_rivals</code>）重测，当前竞品测试是<b>首个</b>该协议基线。</li>
  </ul>
  </div>
</div>

<div class="card warn">
  <h2>⚠️ 数据质量提示</h2>
  <div class="note">
  <ul>
    <li><b>self 文件实为 rivals 副本</b>：竞品测试的 <code>self_median.csv</code> 与 <code>rivals_median.csv</code> 内容完全相同（脚本在 <code>rivals_only</code> 模式下用竞品数据填充了 self 文件）。<code>rivals_only</code> 本就不测自有组件，故"自有组件在该测试中的表现"实际缺失；本次对比仅用 <code>rivals_median.csv</code>（竞品，正确）。</li>
    <li><b>DeepCopy_Json 前7 异常</b>：前7 中 <code>DeepCopy_Json=1.077ns</code> 远快于同组 <code>DeepCopy_Manual=51.4</code>，不符合"JSON 序列化最慢"的常识，属异常样本；竞品测试 <code>834.012ns</code> 反而合理。该单项已标注"不可比"并排除出稳健统计。</li>
    <li><b>full_median.csv / compare_7vs3.csv 在竞品测试中为空</b>：因 <code>rivals_only</code> 不含"全量/前7后3"概念，属预期内。</li>
  </ul>
  </div>
</div>

<div class="card">
  <h2>统计摘要（46 类竞品变体交集）</h2>
  <div class="kpis">
    <div class="kpi"><div class="v">{n}</div><div class="l">竞品变体可比类数</div></div>
    <div class="kpi"><div class="v">{med:.3f}</div><div class="l">比值中位数 (竞品/前7)<br>=&gt; 竞品快 { (1-med)*100:.1f}%</div></div>
    <div class="kpi"><div class="v" style="color:#237804">{faster}/{n}</div><div class="l">竞品更快类数 (ratio&lt;1)</div></div>
    <div class="kpi"><div class="v" style="color:#C0392B">{slower}/{n}</div><div class="l">竞品更慢类数 (ratio&gt;1)</div></div>
    <div class="kpi"><div class="v">{norm_ratio:.3f}</div><div class="l">排除Json后均值比<br>=&gt; 竞品快 {(1-norm_ratio)*100:.1f}%</div></div>
    <div class="kpi"><div class="v" style="color:#AD6800">1.217</div><div class="l">含Json原始均值比<br>(被Json拉高)</div></div>
  </div>
  <div class="note" style="margin-top:10px">
  逐类比值范围 <b>{min(ratios):.3f} ~ {max(ratios):.1f}</b>。若含 <code>DeepCopy_Json</code>，整体均值比被拉至 1.217（看似竞品慢）；排除后均值比 0.706，与中位数 0.717 一致，方向明确：<b>竞品单独冷启动普遍快于前7同场</b>。
  </div>
</div>

<div class="card">
  <h2>逐类对比（ratio = 竞品测试 / 前7同场子集）</h2>
  <table>
    <thead><tr><th>类 (class)</th><th>竞品测试 ns</th><th>前7同场 ns</th><th>ratio</th><th>方向</th></tr></thead>
    <tbody>
    {''.join(trs)}
    </tbody>
  </table>
</div>

<div class="card">
  <h2>结论</h2>
  <div class="note">
  <ul>
    <li>竞品测试建立了<b>首个竞品变体独立冷启动基线</b>（46 类，commit {meta_riv.get('commit')}），数值量级与常识一致（JSON 最慢、Manual/Mapperly 较快）。</li>
    <li>与前7（7变体同场）竞品子集相比，<b>45/46 类竞品变体在单独冷启动下更快</b>，中位数快约 28%。这是<b>协议效应</b>（同场→资源竞争/PGO广度）所致，<b>不能解读为竞品变体自身性能变化</b>。</li>
    <li><code>DeepCopy_Json</code> 前7 样本异常（1.077ns），已排除；该差异不代表真实性能。</li>
    <li><b>下一步</b>：要评估"竞品差距"或"竞品回归"，应在相同 commit + 相同 <code>baseline_rivals</code> 协议下重测并对比本基线；当前尚无同协议历史可比。</li>
  </ul>
  </div>
</div>
</body></html>"""

with open(OUT, 'w', encoding='utf-8') as f:
    f.write(html)
print("报告已生成:", OUT)
print(f"中位数ratio={med:.4f} 排除Json均值比={norm_ratio:.4f} 竞品更快={faster}/{n}")
