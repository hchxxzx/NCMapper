#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# ================================================================
# gen_full_run_report.py — 生成 run_all.sh（档1）性能测试汇总报告
# ----------------------------------------------------------------
# 输入：run_all.sh 运行目录（artifacts/full_run/<ts>/，不传参数取最新）
# 输出（写入运行目录）：
#   self_summary.json + self_summary_report.html
#     —— Stage1 baseline_self 汇总（3 形态 × 3 框架）
#   rivals_compare.json + rivals_compare_report.html
#     —— self vs 竞品对比（baseline_self + baseline_rivals）
#
# 解析方法：按日志中 "Running X..." 切块，提取该块内 BDN summary 表格
#   "| Run |" 行的 Mean/Error/StdDev/Allocated（按表头语义定位列，
#   兼容 BDN v0.14 有无 Median/Gen0 列的表头变体）。
#
# 注：alloc 按表头语义取 Allocated 列（历史会话解析曾固定取第 6 列，
#   在含 Median 的表头下会错取到 Gen0 列，此处为口径修正，mean 不受影响）。
#
# 用法: python benchmarks/scripts/compare/gen_full_run_report.py [run_dir]
# 退出码: 0=成功, 1=运行目录/日志缺失, 2=解析异常（部分类缺失仍生成，附注记）
# ================================================================
import json
import os
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
FULL_RUN = REPO / "artifacts" / "full_run"

# 场景 → 形态后缀归类（baseline_self 37 类 = 11 场景 × 3 形态 − 历史缺 BidirDE_Ext 已补齐（2026-08-18）+ 4 DeepCopy）
FORMAT = {
    "Ext": "Ext",
    "NCMapper": "NCMapper",
    "Runtime": "Runtime",
    "Manual": "Manual",
    "Generated": "Generated",
    "Expression": "Expression",
}

# 竞品前缀映射（baseline_rivals 46 类）
RIVALS_PREFIX = ["AutoMapper", "Manual", "Mapperly", "Mapster", "MemoryPack", "MessagePack", "Json"]


def parse_summary_table(block: str):
    """从单个 benchmark 类日志块提取 summary 表格首行数据。

    返回 dict {mean, error, stddev, alloc, mean_raw}；无表格返回 None。
    """
    lines = block.splitlines()
    for i, ln in enumerate(lines):
        if not ln.strip().startswith("| Method"):
            continue
        header = [c.strip() for c in ln.strip().strip("|").split("|")]
        # 跳过分隔行（|---|），定位 "| Run |" 行
        for j in range(i + 1, min(i + 4, len(lines))):
            row = lines[j].strip()
            if row.startswith("| Run"):
                cells = [c.strip() for c in row.strip().strip("|").split("|")]
                idx = {name: k for k, name in enumerate(header)}
                def get(col):
                    k = idx.get(col)
                    return cells[k] if k is not None and k < len(cells) else None
                mean_raw = get("Mean")
                if mean_raw is None:
                    return None
                m = re.match(r"([\d.]+)\s*ns", mean_raw)
                return {
                    "mean_ns": float(m.group(1)) if m else None,
                    "error_ns": _to_float(get("Error")),
                    "stddev_ns": _to_float(get("StdDev")),
                    "alloc": get("Allocated") or "",
                    "mean_raw": mean_raw,
                }
    return None


def _to_float(s):
    if not s:
        return None
    m = re.match(r"([\d.]+)", s)
    return float(m.group(1)) if m else None


def parse_bench_log(log_path: Path):
    """解析单个 bench_log_<mode>.txt，返回 {类名: 数据}。"""
    if not log_path.exists():
        return {}
    text = log_path.read_text(encoding="utf-8", errors="replace")
    # 按 "Running X..." 切块
    chunks = re.split(r"^\s*Running\s+(\S+)\.\.\.\s*$", text, flags=re.M)
    # chunks[0] 为前缀，其后成对出现 (类名, 块内容)
    result = {}
    for i in range(1, len(chunks) - 1, 2):
        name = chunks[i]
        data = parse_summary_table(chunks[i + 1])
        if data:
            result[name] = data
    return result


def git_short_head():
    try:
        import subprocess
        return subprocess.run(
            ["git", "rev-parse", "--short", "HEAD"], cwd=str(REPO),
            capture_output=True, text=True, timeout=10).stdout.strip()
    except Exception:
        return "unknown"


def _fmt_mean(ns):
    """数值格式化：>=1000 用 us，其余 ns。"""
    if ns is None:
        return "—"
    if ns >= 1000:
        return f"{ns / 1000:.2f} us"
    return f"{ns:.2f} ns"


def _scene_of(name):
    """从类名取场景名（去掉形态后缀）。如 Action_Ext → Action。"""
    for suffix in ("_Ext", "_NCMapper", "_Runtime", "_Generated", "_Expression", "_Manual"):
        if name.endswith(suffix):
            return name[: -len(suffix)]
    # 竞品类：AutoMapper_Flat → Flat；DeepCopy_xxx → DeepCopy
    for prefix in RIVALS_PREFIX:
        if name.startswith(prefix + "_"):
            return name[len(prefix) + 1:]
    if name.startswith("DeepCopy_"):
        return "DeepCopy"
    return name


def _variant_of(name):
    """从类名取形态/变体名。"""
    for suffix in ("_Ext", "_NCMapper", "_Runtime", "_Generated", "_Expression", "_Manual"):
        if name.endswith(suffix):
            return suffix[1:]
    for prefix in RIVALS_PREFIX:
        if name.startswith(prefix + "_"):
            return prefix
    return name


def build_self_summary(run_dir: Path):
    """解析 6 个 self 日志，生成 {framework: {class: data}}。"""
    frameworks = ["net8.0", "net9.0", "net10.0"]
    out = {}
    for tfm in frameworks:
        log = run_dir / "bench_net8.0" / "bench_log_baseline_self.txt"
        log = run_dir / f"bench_{tfm}" / "bench_log_baseline_self.txt"
        out[tfm] = parse_bench_log(log)
    return out


def build_rivals(run_dir: Path):
    """解析 rivals 日志，生成 {framework: {class: data}}。"""
    frameworks = ["net8.0", "net9.0", "net10.0"]
    out = {}
    for tfm in frameworks:
        log = run_dir / f"bench_{tfm}" / "bench_log_baseline_rivals.txt"
        out[tfm] = parse_bench_log(log)
    return out


def geo_mean(values):
    """几何平均（跳过 None）。"""
    vals = [v for v in values if v is not None]
    if not vals:
        return None
    import math
    return math.exp(sum(math.log(v) for v in vals) / len(vals))


def render_self_html(run_dir: Path, self_data, meta):
    """生成 self_summary_report.html（场景 × 形态 × 框架表 + DeepCopy 表）。"""
    frameworks = ["net8.0", "net9.0", "net10.0"]
    variants = ["Ext", "NCMapper", "Runtime"]
    # 场景集合（按 self 数据中出现的场景名，排除 DeepCopy）
    scenes = []
    for tfm in frameworks:
        for name in self_data.get(tfm, {}):
            scene, variant = _scene_of(name), _variant_of(name)
            if scene == "DeepCopy" or variant not in variants:
                continue
            if scene not in scenes:
                scenes.append(scene)

    rows = []
    best_map = {}  # scene -> (tfm, variant) 最快
    for scene in scenes:
        for tfm in frameworks:
            for v in variants:
                d = self_data.get(tfm, {}).get(f"{scene}_{v}")
                if d and d["mean_ns"] is not None:
                    cur = best_map.get(scene)
                    if cur is None or d["mean_ns"] < cur[0]:
                        best_map[scene] = (d["mean_ns"], tfm, v)

    for scene in scenes:
        cells = []
        for tfm in frameworks:
            for v in variants:
                name = f"{scene}_{v}"
                d = self_data.get(tfm, {}).get(name)
                if d is None:  # 该形态不存在（如 BidirDE_Ext）
                    cells.append("<td class='na'>—</td>")
                    continue
                best = best_map.get(scene)
                cls = " class='best'" if best and best[1] == tfm and best[2] == v else ""
                cells.append(f"<td{cls}>{_fmt_mean(d['mean_ns'])}</td>")
        rows.append(f"<tr><td class='scene'>{scene}</td>{''.join(cells)}</tr>")

    # DeepCopy 表
    dc_names = [f"DeepCopy_{v}" for v in ["Manual", "Generated", "Runtime", "Expression"]]
    dc_rows = []
    for name in dc_names:
        cells = []
        prev = None
        for tfm in frameworks:
            d = self_data.get(tfm, {}).get(name)
            cells.append(f"<td>{_fmt_mean(d['mean_ns']) if d else '—'}</td>")
            if d and d["mean_ns"] is not None:
                prev = d["mean_ns"]
        cells.append(f"<td class='chg'>{_pct(prev, _first_mean(self_data, name))}</td>")
        dc_rows.append(f"<tr><td class='scene'>{name}</td>{''.join(cells)}</tr>")

    # net10 三形态几何平均
    gm = {}
    for v in variants:
        vals = [d["mean_ns"] for name, d in self_data.get("net10.0", {}).items()
                if _variant_of(name) == v and _scene_of(name) != "DeepCopy" and d["mean_ns"] is not None]
        gm[v] = geo_mean(vals)
    summary = f"""
<div class="summarybox">
<b>三种形态几何平均耗时对比（net10.0，{len(scenes)} 场景，值越低越快）：</b><br>
Ext（扩展路径）: {_fmt_mean(gm['Ext'])} ｜ NCMapper（标准映射）: {_fmt_mean(gm['NCMapper'])}
（=Ext 的 {gm['NCMapper'] / gm['Ext'] * 100:.1f}%）｜ Runtime（运行时表达式）: {_fmt_mean(gm['Runtime'])}
（NCMapper = Runtime 的 {gm['NCMapper'] / gm['Runtime'] * 100:.0f}%）
</div>"""

    html = f"""<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="utf-8">
<title>NCMapper 性能测试 Stage1 汇总（self 三种形态）</title>
<style>
body{{font-family:'Segoe UI','Microsoft YaHei',sans-serif;margin:24px;color:#2C2C2A;background:#fff}}
h1{{font-size:20px;font-weight:500;margin-bottom:4px}}
h2{{font-size:15px;font-weight:500;margin:22px 0 8px;color:#0C447C}}
.meta{{font-size:12px;color:#5F5E5A;margin-bottom:16px}}
table{{border-collapse:collapse;font-size:12.5px;margin:8px 0}}
th,td{{border:1px solid #D3D1C7;padding:4px 10px;text-align:right;white-space:nowrap}}
th{{background:#E6F1FB;font-weight:500;color:#0C447C}}
td.scene{{text-align:left;font-weight:500;background:#F1EFE8}}
td.best{{background:#EAF3DE;font-weight:500}}
td.na{{color:#B4B2A9}}
td.chg{{background:#FAEEDA}}
.note{{font-size:12px;color:#5F5E5A;margin-top:6px}}
.summarybox{{background:#F1EFE8;border:1px solid #D3D1C7;border-radius:8px;padding:10px 14px;font-size:12.5px;line-height:1.7}}
</style></head><body>
<h1>NCMapper 性能测试 Stage1 汇总（baseline_self · 三种形态）</h1>
<div class="meta">运行: {meta['date']} ｜ commit {meta['commit']} ｜ {len(scenes)} 场景 × net8/9/10 ｜ BDN launch=5 warmup=3 iter=15 ｜ i7-12700K P-core 锁定</div>
{summary}
<h2>{len(scenes)} 场景 × 三种形态 × 三框架（Mean，绿底=该场景三框架九格中最快）</h2>
<table>
<tr><th>场景</th><th colspan="3">net8.0</th><th colspan="3">net9.0</th><th colspan="3">net10.0</th></tr>
<tr><th></th><th>Ext</th><th>NCMapper</th><th>Runtime</th><th>Ext</th><th>NCMapper</th><th>Runtime</th><th>Ext</th><th>NCMapper</th><th>Runtime</th></tr>
{''.join(rows)}
</table>
<h2>DeepCopy 4 变体 × 三框架（Mean）</h2>
<table>
<tr><th>变体</th><th>net8.0</th><th>net9.0</th><th>net10.0</th><th>net8→net10</th></tr>
{''.join(dc_rows)}
</table>
<div class="note">DeepCopy 只测 Copy 单次操作；Manual=手写基线，Generated=源生成器，Runtime=运行时表达式，Expression=表达式树。</div>
</body></html>"""
    return html


def _first_mean(data, name):
    for tfm in ("net8.0", "net9.0", "net10.0"):
        d = data.get(tfm, {}).get(name)
        if d and d["mean_ns"] is not None:
            return d["mean_ns"]
    return None


def _pct(cur, base):
    if cur is None or base is None:
        return "—"
    return f"{(cur / base - 1) * 100:+.1f}%"


def render_rivals_html(run_dir, self_data, rivals_data, meta):
    """生成 rivals_compare_report.html（NCMapper vs 竞品，按场景）。"""
    frameworks = ["net8.0", "net9.0", "net10.0"]
    # 场景集合（取 rivals 数据中的场景名）
    scenes = []
    for tfm in frameworks:
        for name in rivals_data.get(tfm, {}):
            scene = _scene_of(name)
            if scene != "DeepCopy" and scene not in scenes:
                scenes.append(scene)
    rows = []
    for scene in scenes:
        cells = []
        for tfm in frameworks:
            nc = self_data.get(tfm, {}).get(f"{scene}_NCMapper")
            best_rival = None
            for name, d in rivals_data.get(tfm, {}).items():
                if _scene_of(name) == scene and d["mean_ns"] is not None:
                    if best_rival is None or d["mean_ns"] < best_rival[1]:
                        best_rival = (name, d["mean_ns"])
            if nc and nc["mean_ns"] is not None:
                cells.append(f"<td>{_fmt_mean(nc['mean_ns'])}</td>")
            else:
                cells.append("<td class='na'>—</td>")
            if best_rival:
                cells.append(f"<td>{_fmt_mean(best_rival[1])} ({best_rival[0]})</td>")
            else:
                cells.append("<td class='na'>—</td>")
        rows.append(f"<tr><td class='scene'>{scene}</td>{''.join(cells)}</tr>")

    html = f"""<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="utf-8">
<title>NCMapper 性能测试 Stage2 对比（self vs 竞品）</title>
<style>
body{{font-family:'Segoe UI','Microsoft YaHei',sans-serif;margin:24px;color:#2C2C2A;background:#fff}}
h1{{font-size:20px;font-weight:500;margin-bottom:4px}}
h2{{font-size:15px;font-weight:500;margin:22px 0 8px;color:#0C447C}}
.meta{{font-size:12px;color:#5F5E5A;margin-bottom:16px}}
table{{border-collapse:collapse;font-size:12.5px;margin:8px 0}}
th,td{{border:1px solid #D3D1C7;padding:4px 10px;text-align:right;white-space:nowrap}}
th{{background:#E6F1FB;font-weight:500;color:#0C447C}}
td.scene{{text-align:left;font-weight:500;background:#F1EFE8}}
td.na{{color:#B4B2A9}}
.note{{font-size:12px;color:#5F5E5A;margin-top:6px}}
</style></head><body>
<h1>NCMapper 性能测试 Stage2 对比（baseline_self NCMapper vs baseline_rivals 最优竞品）</h1>
<div class="meta">运行: {meta['date']} ｜ commit {meta['commit']} ｜ {len(scenes)} 场景 × net8/9/10 ｜ BDN launch=5 warmup=3 iter=15</div>
<table>
<tr><th>场景</th><th colspan="2">net8.0</th><th colspan="2">net9.0</th><th colspan="2">net10.0</th></tr>
<tr><th></th><th>NCMapper</th><th>最优竞品</th><th>NCMapper</th><th>最优竞品</th><th>NCMapper</th><th>最优竞品</th></tr>
{''.join(rows)}
</table>
<div class="note">竞品 = AutoMapper/Manual/Mapperly/Mapster（映射）与 MemoryPack/MessagePack/Json（DeepCopy）；每格取该场景三框架各自最快的竞品变体。值越低越快。</div>
</body></html>"""
    return html


def main():
    if len(sys.argv) > 1:
        run_dir = Path(sys.argv[1])
    else:
        dirs = sorted(FULL_RUN.glob("*"), key=lambda p: p.stat().st_mtime, reverse=True) if FULL_RUN.exists() else []
        if not dirs:
            print(f"ERROR: 无运行目录（{FULL_RUN} 不存在或为空）")
            return 1
        run_dir = dirs[0]
    if not run_dir.is_dir():
        print(f"ERROR: 运行目录不存在: {run_dir}")
        return 1

    meta = {
        "base": str(run_dir).replace("\\", "/"),
        "commit": git_short_head(),
        "date": run_dir.name[:8],
        "mode": "baseline_self",
    }

    self_data = build_self_summary(run_dir)
    rivals_data = build_rivals(run_dir)
    missing = []
    for tfm in ("net8.0", "net9.0", "net10.0"):
        if not self_data.get(tfm):
            missing.append(f"self/{tfm}")

    if missing:
        print(f"WARNING: 缺失日志: {', '.join(missing)}，报告不完整")

    # self_summary
    self_json = {"meta": dict(meta, mode="baseline_self"), "classes": self_data}
    (run_dir / "self_summary.json").write_text(
        json.dumps(self_json, ensure_ascii=False, indent=1), encoding="utf-8")
    (run_dir / "self_summary_report.html").write_text(
        render_self_html(run_dir, self_data, meta), encoding="utf-8")

    # rivals_compare
    rivals_json = {"meta": dict(meta, mode="baseline_self+rivals"), "data": {"self": self_data, "rivals": rivals_data}}
    (run_dir / "rivals_compare.json").write_text(
        json.dumps(rivals_json, ensure_ascii=False, indent=1), encoding="utf-8")
    (run_dir / "rivals_compare_report.html").write_text(
        render_rivals_html(run_dir, self_data, rivals_data, meta), encoding="utf-8")

    total_self = sum(len(v) for v in self_data.values())
    total_rivals = sum(len(v) for v in rivals_data.values())
    print(f"OK: {run_dir.name} self={total_self} 类 rivals={total_rivals} 类")
    print(f"  self_summary_report.html / self_summary.json")
    print(f"  rivals_compare_report.html / rivals_compare.json")
    return 0 if not missing else 2


if __name__ == "__main__":
    sys.exit(main())
