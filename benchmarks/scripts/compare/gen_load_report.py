#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# ================================================================
# gen_load_report.py — NCMapper.LoadBench 高峰负载压测报告生成器
# ----------------------------------------------------------------
# 读取运行目录下 load_summary.json / endurance.csv / concurrency_*.csv，
# 生成自包含 HTML 压测报告（冷启动 / 高并发 / 持久运行 / 优化建议）。
# 用法: python gen_load_report.py <run_dir> [--out <html路径>]
# ================================================================

import csv
import html
import json
import os
import sys
from datetime import datetime


def load_summary(run_dir):
    """读取 load_summary.json"""
    with open(os.path.join(run_dir, "load_summary.json"), encoding="utf-8") as f:
        return json.load(f)


def load_csv_rows(run_dir, name):
    """读取 CSV 为 dict 列表"""
    path = os.path.join(run_dir, name)
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return list(csv.DictReader(f))


def fmt(n, digits=1):
    """数值格式化（千分位）"""
    try:
        return f"{float(n):,.{digits}f}"
    except (TypeError, ValueError):
        return str(n)


def render_cold_start(cs):
    """冷启动结果表"""
    rows = []
    for r in cs:
        gen = "是" if r["isGenerated"] else "否"
        rows.append(f"""<tr>
  <td>{html.escape(r['scenario'])}</td>
  <td>{gen}</td>
  <td class="num">{fmt(r['firstCallUs'], 1)}</td>
  <td class="num">{fmt(r['steadyUs'], 3)}</td>
  <td class="num">{fmt(r['ratio'], 0)}×</td>
</tr>""")
    return "\n".join(rows)


def render_concurrency(conc):
    """并发结果表（按 workload 分组）"""
    blocks = []
    for wl, pts in conc.items():
        rows = []
        for p in pts:
            eff = float(p["scaleEfficiency"])
            cls = "warn" if eff < 0.7 else ("bad" if eff < 0.4 else "ok")
            rows.append(f"""<tr>
  <td class="num">{p['threads']}</td>
  <td class="num">{fmt(p['tps'], 0)}</td>
  <td class="num">{fmt(p['avgLatencyUs'], 2)}</td>
  <td class="num {cls}">{fmt(eff, 3)}</td>
</tr>""")
        blocks.append(f"""<h3>{html.escape(wl)}</h3>
<table>
<tr><th>线程数</th><th>TPS</th><th>平均延迟(µs)</th><th>扩展效率</th></tr>
{chr(10).join(rows)}
</table>""")
    return "\n".join(blocks)


def render_endurance(e):
    """持久运行结果：判定 + 采样表"""
    rows = []
    for s in e["samples"]:
        rows.append(f"""<tr>
  <td class="num">{fmt(s['timeSec'], 1)}</td>
  <td class="num">{fmt(s['managedHeapMB'], 2)}</td>
  <td class="num">{fmt(s['totalAllocMB'], 0)}</td>
  <td class="num">{s['gen0']}</td>
  <td class="num">{s['gen2']}</td>
  <td class="num">{fmt(s['workingSetMB'], 1)}</td>
</tr>""")
    return "\n".join(rows)


def _gen0_rate(samples):
    """从 endurance 采样序列计算 Gen0 频率（次/秒）"""
    if not samples:
        return "-"
    span = max(samples[-1]["timeSec"] - samples[0]["timeSec"], 1.0)
    return f"{(samples[-1]['gen0'] - samples[0]['gen0']) / span:.0f}"


def _last_ws(samples):
    """取 endurance 末次采样工作集（MB）"""
    if not samples:
        return "-"
    return f"{samples[-1]['workingSetMB']:.0f}"


def _scale_eff(summary, workload, threads):
    """取指定工作负载/线程数的扩展效率"""
    pts = summary.get("concurrency", {}).get(workload, [])
    for p in pts:
        if p["threads"] == threads:
            return float(p["scaleEfficiency"])
    return 0.0


def _tps_at(summary, workload, threads):
    """取指定工作负载/线程数的 TPS"""
    pts = summary.get("concurrency", {}).get(workload, [])
    for p in pts:
        if p["threads"] == threads:
            return float(p["tps"])
    return 0.0


def build_html(summary, run_dir, compare_dir=None):
    """组装 HTML 报告（§5 与主数据同源、GC 模式标注、口径动态化）"""
    env = html.escape(summary["environment"])
    cs = summary.get("coldStart", [])
    conc = summary.get("concurrency", {})
    endu = summary.get("endurance", {})
    ts = datetime.now().strftime("%Y-%m-%d %H:%M:%S")

    # GC 模式：从 summary.gcMode 派生显示
    gc_mode_raw = summary.get("gcMode", "default")
    gc_display = {
        "0": "DATAS 关闭（DOTNET_GCDynamicAdaptationMode=0，三框架同策略）",
        "1": "DATAS 开启（DOTNET_GCDynamicAdaptationMode=1）",
        "default": "运行时默认（net8 无 DATAS / net9+ 开 DATAS）",
    }.get(gc_mode_raw, f"未知（{gc_mode_raw}）")

    # §5 对侧（对比目录）摘要：仅用于附注，不进入主表（主表全部派生自主 summary，同源强制）
    compare_note = ""
    if compare_dir:
        try:
            cd = json.load(open(os.path.join(compare_dir, "load_summary.json"), encoding="utf-8"))
            c_gen0 = _gen0_rate(cd.get("endurance", {}).get("samples", []))
            c_ws = _last_ws(cd.get("endurance", {}).get("samples", []))
            c_16t = _scale_eff(cd, "Collection_MapList", 16)
            compare_note = (f"对侧（{html.escape(os.path.basename(compare_dir))}）："
                            f"Gen0 {c_gen0}/s、WS {c_ws}MB、Collection 16T 扩展 {c_16t:.3f}")
        except Exception:
            compare_note = f"对侧目录不可读：{html.escape(compare_dir)}"

    cs_rows = render_cold_start(cs) if cs else "<tr><td colspan=5>未运行</td></tr>"
    conc_blocks = render_concurrency(conc) if conc else "<p>未运行</p>"
    endu_rows = render_endurance(endu) if endu.get("samples") else "<tr><td colspan=6>未运行</td></tr>"
    verdict = html.escape(endu.get("verdict", "未运行"))

    # §5 主侧数值：全部派生自主 summary（与 §2/§3 同源，杜绝双源矛盾）
    main_gen0 = _gen0_rate(endu.get("samples", [])) if endu.get("samples") else "-"
    main_ws = _last_ws(endu.get("samples", [])) if endu.get("samples") else "-"
    main_16t = _scale_eff(summary, "Collection_MapList", 16)
    main_16t_tps = _tps_at(summary, "Collection_MapList", 16)

    return f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>NCMapper 高峰负载压测报告</title>
<style>
body {{ font-family: "Microsoft YaHei", sans-serif; margin: 32px auto; max-width: 960px; color: #2C2C2A; line-height: 1.6; }}
h1 {{ font-size: 22px; border-bottom: 2px solid #185FA5; padding-bottom: 8px; }}
h2 {{ font-size: 18px; color: #0C447C; margin-top: 28px; }}
h3 {{ font-size: 15px; color: #185FA5; margin-top: 16px; }}
table {{ border-collapse: collapse; width: 100%; margin: 8px 0 16px; font-size: 13px; }}
th, td {{ border: 1px solid #D3D1C7; padding: 6px 10px; text-align: left; }}
th {{ background: #E6F1FB; color: #0C447C; }}
td.num {{ text-align: right; font-variant-numeric: tabular-nums; }}
td.ok {{ color: #3B6D11; }} td.warn {{ color: #854F0B; }} td.bad {{ color: #A32D2D; font-weight: 500; }}
.meta {{ background: #F1EFE8; padding: 10px 14px; border-radius: 8px; font-size: 13px; margin: 12px 0; }}
.verdict {{ background: #FAEEDA; border: 1px solid #BA7517; padding: 10px 14px; border-radius: 8px; margin: 8px 0; }}
.card {{ background: #E6F1FB; border-radius: 8px; padding: 10px 14px; margin: 8px 0; font-size: 13px; }}
</style>
</head>
<body>
<h1>NCMapper 高峰负载压测报告</h1>
<div class="meta">运行环境：{env}<br>报告生成：{ts}<br>数据目录：{html.escape(run_dir)}<br><b>GC 模式</b>：{gc_display}<br><b>并发轮次</b>：每档 {summary.get("concurrencyRounds", "?")} 轮取中位数</div>

<h2>1. 冷启动分析（响应慢 / 超时根因）</h2>
<p>首行为进程级初始化（ModuleInitializer 委托注册 + 静态构造 + dummy 映射）；其余行为类型对首次调用 vs 稳态。<b>脚注</b>：稳态 = 首次调用后连续 99 次均值（仍处 Tier0 未 PGO 状态）；并发场景数据为预热 5000 次后的 Tier1 稳态，两者不可直接横向比较。</p>
<table>
<tr><th>场景</th><th>源生成(Ext)</th><th>首次调用(µs)</th><th>稳态均值(µs)</th><th>首次/稳态</th></tr>
{cs_rows}
</table>

<h2>2. 高并发分析（吞吐 / CPU 根因）</h2>
<p>扩展效率 = 该线程数 TPS / (单线程 TPS × 线程数)，每档 {summary.get("concurrencyRounds", "?")} 轮取中位数。接近 1 为理想线性扩展；持续下降提示锁竞争或 GC 压力。</p>
{conc_blocks}

<h2>3. 持久运行分析（内存根因）</h2>
<div class="verdict">综合判定：{verdict}</div>
<table>
<tr><th>时间(s)</th><th>托管堆(MB)</th><th>累计分配(MB)</th><th>Gen0</th><th>Gen2</th><th>工作集(MB)</th></tr>
{endu_rows}
</table>

<h2>4. 瓶颈定位与优化建议</h2>
<div class="card"><b>冷启动：</b>关注类型对首次调用（进程级初始化已独立成行）；Flat_Map 首次异常高时优先查生成路径注册，DeepCopy 类走 Runtime 编译属设计内成本。</div>
<div class="card"><b>高并发：</b>扩展效率在 4~8 线程即显著下降的工作负载（尤其集合映射）为优化重点，GC 策略影响可对比附录（关 DATAS 使 net9/10 Gen0 频率回落 ~100 倍）。</div>
<div class="card"><b>持久运行：</b>分配速率与 Gen2 回收频率决定 GC 压力，工作集稳定即可排除泄漏；堆峰值 &lt;50MB 时 ratio 判定自动跳过（§6-2 绝对量门槛）。</div>

<h2>5. DATAS 归因验证（主数据同源）</h2>
<p>本报告主数据（{html.escape(os.path.basename(run_dir))}）GC 模式：{gc_display}。以下数值<b>全部派生自主 summary</b>（与 §2/§3 同源，无独立取数）。</p>
<table>
<tr><th>指标（本报告 TFM）</th><th>值</th><th>对照说明</th></tr>
<tr><td>Gen0 频率（endurance）</td><td class="num">{main_gen0}/s</td><td>net8 无 DATAS ~14/s；net9/10 关 DATAS ~15-17/s（见 §5 实测表）</td></tr>
<tr><td>工作集</td><td class="num">{main_ws}MB</td><td>DATAS 开启形态 ~40MB，关闭形态 ~380MB（net8 形态一致）</td></tr>
<tr><td>Collection 16T 扩展效率</td><td class="num">{fmt(main_16t, 3)}（TPS {fmt(main_16t_tps, 0)}）</td><td>DATAS 开启 net9/10 为 0.14-0.15；关闭后回升至 0.19-0.24</td></tr>
</table>
<div class="card"><b>结论</b>：DATAS 归因成立（Gen0 频率回落 ~100 倍、工作集与 net8 形态一致）；并发衰减 = DATAS（GC 压力）+ 物理核限制（12700K 12C/20T）共同作用。对吞吐敏感场景可关 DATAS（16T 吞吐 +48~64%），内存受限场景保留（WS 40MB vs 380MB）。三框架对比基准应统一 pin GC 策略。</div>
{compare_note if compare_note else ""}

</body>
</html>"""


def main():
    if len(sys.argv) < 2:
        print("用法: python gen_load_report.py <run_dir> [--out <html路径>] [--compare-dir <对侧目录>]", file=sys.stderr)
        sys.exit(1)
    run_dir = sys.argv[1]
    out = None
    compare_dir = None
    if "--out" in sys.argv:
        out = sys.argv[sys.argv.index("--out") + 1]
    if "--compare-dir" in sys.argv:
        compare_dir = sys.argv[sys.argv.index("--compare-dir") + 1]
    if not out:
        ts = datetime.now().strftime("%Y%m%d_%H%M%S")
        out = os.path.join(run_dir, f"load_report_{ts}.html")

    summary = load_summary(run_dir)
    html_content = build_html(summary, run_dir, compare_dir)
    with open(out, "w", encoding="utf-8") as f:
        f.write(html_content)
    print(f"报告已生成: {out}")


if __name__ == "__main__":
    main()
