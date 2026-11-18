#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
NCMapper 性能回归基线 HTML 报告生成器
读取基线运行目录的 CSV + meta.json, 生成带"年月日时分秒"名称的 HTML 报告。
用法: python gen_report.py <运行目录>
输出: <运行目录>/NCMapper性能回归基线报告_YYYYMMDD_HHMMSS.html
"""
import sys
import os
import re
import csv
import json
from datetime import datetime

def read_csv(path):
    """读取 CSV 返回 (header, rows)。文件不存在返回 (None, [])。"""
    if not os.path.isfile(path):
        return None, []
    with open(path, encoding='utf-8') as f:
        r = csv.reader(f)
        rows = list(r)
    if not rows:
        return [], []
    return rows[0], rows[1:]

def fmt_ns(v):
    try:
        return f"{float(v):.2f} ns"
    except Exception:
        return str(v)

def main():
    if len(sys.argv) < 2:
        print("用法: gen_report.py <运行目录>", file=sys.stderr)
        sys.exit(1)
    run_dir = sys.argv[1].rstrip('/')
    run_dir = os.path.normpath(run_dir)

    # 时间戳: 从运行目录名提取 (net10.0_20260707_191800)
    base = os.path.basename(run_dir)
    m = re.search(r'_(\d{8}_\d{6})$', base)
    ts = m.group(1) if m else datetime.now().strftime('%Y%m%d_%H%M%S')
    html_name = f"NCMapper性能回归基线报告_{ts}.html"
    html_path = os.path.join(run_dir, html_name)

    # 元数据
    meta = {}
    meta_path = os.path.join(run_dir, 'meta.json')
    if os.path.isfile(meta_path):
        try:
            meta = json.load(open(meta_path, encoding='utf-8'))
        except Exception:
            meta = {}

    # CSV
    full_hdr, full_rows = read_csv(os.path.join(run_dir, 'full_median.csv'))
    self_hdr, self_rows = read_csv(os.path.join(run_dir, 'self_median.csv'))
    cmp_hdr, cmp_rows = read_csv(os.path.join(run_dir, 'compare_7vs3.csv'))

    full_n = len(full_rows)
    self_n = len(self_rows)
    cmp_n = len(cmp_rows)

    # compare_7vs3 统计
    ratios = []
    for row in cmp_rows:
        if len(row) >= 4:
            try:
                ratios.append(float(row[3]))
            except Exception:
                pass
    avg_ratio = (sum(ratios) / len(ratios)) if ratios else 0.0
    max_ratio = max(ratios) if ratios else 0.0
    min_ratio = min(ratios) if ratios else 0.0

    desc = meta.get('description', 'NCMapper 性能回归基线')
    fw = meta.get('framework', '?')
    commit = meta.get('commit', '?')
    start = meta.get('startTime', '?')
    end = meta.get('endTime', '?')
    host = meta.get('hostname', '?')
    dver = meta.get('dotnetVersion', '?')
    reps = meta.get('repeats', '?')

    # 模式感知: rivals_only 时把"SELF"相关标签改为"竞品", 隐藏无意义的 PGO 对比段
    rivals_only = (meta.get('mode') == 'rivals_only')
    if rivals_only:
        self_label = "竞品变体类数 (独立冷启动基线)"
        v_title = "竞品变体中位数（独立冷启动，与层1 基线同协议）"
    else:
        self_label = "SELF 3变体类数 (层1 回归基线)"
        v_title = "SELF 3变体中位数（层1 自己组件回归基线）"

    def table_html(hdr, rows, cls_col=0):
        th = ''.join(f'<th>{h}</th>' for h in hdr) if hdr else ''
        body = ''
        for row in rows:
            tds = ''.join(f'<td>{"".join("<br/>".join(c.split("\n"))) if isinstance(c,str) else c}</td>' for c in row)
            body += f'<tr>{tds}</tr>'
        return f'<table><thead><tr>{th}</tr></thead><tbody>{body}</tbody></table>'

    # compare 表增加高亮 (ratio>1 偏红)
    cmp_body = ''
    for row in cmp_rows:
        cells = ''
        for i, c in enumerate(row):
            if i == 3:
                try:
                    rv = float(c)
                    color = '#b91c1c' if rv > 1.05 else ('#15803d' if rv < 0.95 else '#1f2933')
                    cells += f'<td style="color:{color};font-weight:600">{c}</td>'
                except Exception:
                    cells += f'<td>{c}</td>'
            else:
                cells += f'<td>{c}</td>'
        cmp_body += f'<tr>{cells}</tr>'
    cmp_th = ''.join(f'<th>{h}</th>' for h in cmp_hdr) if cmp_hdr else ''

    ratio_interp = ("ratio_3over7 平均值 {:.4f}：3 变体独占进程相对 7 变体同场的平均性能比值。"
                    "若显著 &gt; 1，证明 3 变体因 PGO 饱和更窄而更慢——这正是「先7后3」方法论成立的直接证据，"
                    "也说明今后回归必须与层1基线保持同一饱和广度（同进程 3 变体独占）。").format(avg_ratio)

    # compare_7vs3 段: 仅当存在重叠类数据时才展示 PGO 饱和差异; 竞品独立运行无 7 变体数据则给出说明
    if cmp_n > 0:
        cmp_section = f"""<div class="panel">  # 修复：标题由主模板统一输出，避免同名 h2 重复渲染
<p>{ratio_interp}</p>
<table style="width:auto">
  <tr><th>指标</th><th>值</th></tr>
  <tr><td>平均 ratio_3over7</td><td><strong>{avg_ratio:.4f}</strong></td></tr>
  <tr><td>最大 ratio</td><td>{max_ratio:.4f}</td></tr>
  <tr><td>最小 ratio</td><td>{min_ratio:.4f}</td></tr>
</table>
</div>
<h3>3.1 重叠类明细（ratio = 3变体均值 / 7变体均值）</h3>
<table><thead><tr>{cmp_th}</tr></thead><tbody>{cmp_body}</tbody></table>"""
    else:
        cmp_section = """<div class="panel">
<p class="muted">本运行（竞品独立冷启动基线，mode=rivals_only）不含 7 变体同场数据，故无 PGO 饱和差异（ratio_3over7）对比。竞品性能数据见下方「竞品变体中位数」。</p>
</div>"""

    html = f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8" />
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
<title>NCMapper 性能回归基线报告 {ts}</title>
<style>
  :root {{
    --c-bg:#fff; --c-panel:#f6f8fb; --c-border:#e2e8f0; --c-text:#1f2933;
    --c-sub:#52606d; --c-blue:#2563eb; --c-blue-50:#eff6ff; --c-green:#15803d;
    --c-green-50:#ecfdf5; --c-amber:#b45309; --c-amber-50:#fffbeb;
  }}
  * {{ box-sizing:border-box; }}
  body {{ margin:0; padding:0; background:var(--c-bg); color:var(--c-text);
    font-family:"Segoe UI","Microsoft YaHei",system-ui,sans-serif; line-height:1.65; font-size:15px; }}
  .wrap {{ max-width:1100px; margin:0 auto; padding:32px 28px 64px; }}
  header.hero {{ border-bottom:3px solid var(--c-blue); padding-bottom:18px; margin-bottom:8px; }}
  h1 {{ font-size:25px; margin:0 0 6px; }}
  .sub {{ color:var(--c-sub); font-size:13.5px; }}
  h2 {{ font-size:19px; margin:34px 0 12px; padding-left:12px; border-left:4px solid var(--c-blue); }}
  h3 {{ font-size:15.5px; margin:20px 0 8px; color:var(--c-blue); }}
  p {{ margin:8px 0; }}
  .muted {{ color:var(--c-sub); font-size:13px; }}
  .panel {{ background:var(--c-panel); border:1px solid var(--c-border); border-radius:10px; padding:16px 20px; margin:14px 0; }}
  table {{ width:100%; border-collapse:collapse; margin:10px 0; font-size:13px; }}
  th,td {{ border:1px solid var(--c-border); padding:6px 9px; text-align:left; vertical-align:top; }}
  th {{ background:var(--c-blue-50); color:var(--c-blue); font-weight:600; }}
  tr:nth-child(even) td {{ background:#fafbfc; }}
  code {{ background:#f1f5f9; padding:2px 6px; border-radius:4px; font-family:Consolas,monospace; font-size:12.5px; }}
  .badge {{ display:inline-block; padding:3px 10px; border-radius:999px; font-size:12px; font-weight:600; margin-right:8px; }}
  .b-green {{ background:var(--c-green-50); color:var(--c-green); }}
  .b-blue {{ background:var(--c-blue-50); color:var(--c-blue); }}
  .grid3 {{ display:grid; grid-template-columns:repeat(3,1fr); gap:12px; }}
  @media (max-width:760px) {{ .grid3 {{ grid-template-columns:1fr; }} }}
  .stat {{ background:var(--c-blue-50); border-radius:8px; padding:14px; text-align:center; }}
  .stat .n {{ font-size:26px; font-weight:700; color:var(--c-blue); }}
  .stat .l {{ font-size:12.5px; color:var(--c-sub); }}
  ul {{ margin:8px 0; padding-left:22px; }}
  footer {{ margin-top:46px; padding-top:16px; border-top:1px solid var(--c-border); color:var(--c-sub); font-size:12.5px; }}
  a {{ color:var(--c-blue); }}
</style>
</head>
<body>
<div class="wrap">
<header class="hero">
  <h1>NCMapper 性能回归基线报告</h1>
  <div class="sub">生成时间戳：{ts} ｜ 框架：{fw} ｜ commit：{commit} ｜ 外部重复：{reps} 次</div>
  <div style="margin-top:10px"><span class="badge b-green">基线已建成</span><span class="badge b-blue">带时间戳归档</span></div>
</header>

<h2>一、任务元数据</h2>
<div class="panel">
<table>
  <tr><th style="width:22%">字段</th><th>值</th></tr>
  <tr><td>任务说明</td><td>{desc}</td></tr>
  <tr><td>开始时间</td><td>{start}</td></tr>
  <tr><td>结束时间</td><td>{end}</td></tr>
  <tr><td>目标框架</td><td>{fw}</td></tr>
  <tr><td>外部重复次数</td><td>{reps}</td></tr>
  <tr><td>代码 commit</td><td><code>{commit}</code></td></tr>
  <tr><td>dotnet 版本</td><td>{dver}</td></tr>
  <tr><td>运行主机</td><td>{host}</td></tr>
  <tr><td>运行目录</td><td><code>{run_dir}</code></td></tr>
</table>
</div>

<h2>二、汇总</h2>
<div class="grid3">
  <div class="stat"><div class="n">{full_n}</div><div class="l">FULL 7变体类数 (层2 竞品快照)</div></div>
  <div class="stat"><div class="n">{self_n}</div><div class="l">{self_label}</div></div>
  <div class="stat"><div class="n">{cmp_n}</div><div class="l">7变体 vs 3变体 重叠类数</div></div>
</div>
<p class="muted">FULL 预期 ~80 类，SELF 预期 ~35 类。若类数明显偏少，说明部分基准未产出 Mean（详见下方各表与运行日志）。</p>

<h2>三、PGO 饱和差异验证（compare_7vs3）</h2>
{cmp_section}

<h2>四、FULL 7变体中位数（层2 竞品同场快照）</h2>
{table_html(full_hdr, full_rows)}

<h2>五、{v_title}</h2>
{table_html(self_hdr, self_rows)}

<h2>六、原始数据</h2>
<ul>
  <li><a href="full_median.csv">full_median.csv</a> — 7 变体同场中位数</li>
  <li><a href="self_median.csv">self_median.csv</a> — 3 变体独占中位数（回归比对基准）</li>
  <li><a href="compare_7vs3.csv">compare_7vs3.csv</a> — 7变体 vs 3变体 PGO 饱和差异</li>
  <li><a href="meta.json">meta.json</a> — 任务元数据</li>
</ul>

<footer>
<p>NCMapper 性能回归基线报告 ｜ 时间戳 {ts} ｜ 由 <code>benchmarks/scripts/baseline/gen_report.py</code> 生成</p>
<p>方法论：{("本运行仅跑 baseline_rivals（竞品变体），与 baseline_self（后3）同协议冷启动 × " + str(reps) + " 次取中位数；竞品数据可直接与层1 基线（后3）横比。") if rivals_only else ("先7后3 两次（baseline 全量 + baseline_self 自己组件）× " + str(reps) + " 次取中位数。今后回归只跑 baseline_self，与本报告 SELF 中位数比对。")}</p>
</footer>
</div>
</body>
</html>"""

    with open(html_path, 'w', encoding='utf-8') as f:
        f.write(html)
    print(f"报告已生成: {html_path}")
    print(f"  FULL={full_n} SELF={self_n} CMP={cmp_n} avg_ratio_3over7={avg_ratio:.4f}")

if __name__ == '__main__':
    main()
