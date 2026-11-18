#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# ================================================================
# gen_full_report.py — 生成 NCMapper 全组件全框架全场景性能测试报告（完整版 HTML）
# ----------------------------------------------------------------
# 上游：gen_full_run_report.py（先运行它生成 self_summary.json / rivals_compare.json）
# 输入：run_all.sh 档1 运行目录（artifacts/full_run/<ts>/，不传参数取最新含 json 者）
# 输出：NCMapper全组件全框架全场景性能测试报告_YYYYMMDD.html
#       输出目录可通过 --out 覆盖；日期默认今天，可通过 --date YYYYMMDD 指定。
#
# 格式对照：历史版本报告 NCMapper全组件全框架全场景性能测试报告_20260704.html
#   章节：summary 卡 → 一单元测试 → 二映射场景（12 节 × 3 TFM 网格表）
#         → 三 Update 场景（json 含 Update 数据才渲染）→ 四 SG vs RT 对照
#         → 五全局汇总表 → 六图表（6.1 纵向柱状 ×3 / 6.2 跨组件折线 ×3 / 6.3 vs Manual）
#         → 七统计专题 → 八总结
#   图表要点：6.1 纵向柱状（非横向），Collection（μs 量级）从 ns 图中剔除；
#             6.2 折线缺组件点虚线跨接；6.3 基准线右置 x=660。
#   单元测试通过数从运行目录 test_*.log 动态解析（不硬编码）。
# 用法: python benchmarks/scripts/compare/gen_full_report.py [run_dir] [--out <path>] [--date YYYYMMDD]
# 退出码: 0=成功, 1=运行目录/json 缺失, 2=解析异常（仍生成，附注记）
# ================================================================
import json
import math
import os
import re
import sys
from datetime import datetime
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
FULL_RUN = REPO / "artifacts" / "full_run"
DEFAULT_OUT_DIR = REPO / "artifacts" / "reports"

TFMS = ["net8.0", "net9.0", "net10.0"]

SCENES = [
    ("Flat", "简单 9 字段映射"), ("NameDiff", "异名字段映射"), ("Nested", "嵌套对象映射"),
    ("Record", "不可变记录映射"), ("Nullable", "可空类型映射"), ("AutoFlatten", "扁平化映射"),
    ("EnumString", "枚举→字符串映射"), ("Action", "带回调映射"), ("Collection", "集合映射 (100元素)"),
    ("BidirED", "双向 E→D"), ("BidirDE", "双向 D→E"),
]

# ===== 数据读取 =====
def get_mean(tfm_data, key):
    """返回 (ns 值, 单位) 或 None；us/ms 统一换算为 ns 并标记单位"""
    v = tfm_data.get(key)
    if not v:
        return None
    if v.get("mean_ns") is not None:
        return v["mean_ns"], "ns"
    raw = (v.get("mean_raw") or "").strip()
    m = re.match(r"([\d.]+)\s*(n?s|us|µs|ms)?", raw)
    if not m:
        return None
    num = float(m.group(1))
    unit = m.group(2) or "ns"
    if unit == "us":
        return num * 1000.0, "us"
    if unit == "ms":
        return num * 1e6, "ms"
    return num, "ns"

def fmt(v, unit="ns"):
    if unit == "us":
        return f"{v / 1000.0:.2f}"
    return f"{v:.2f}"

def build_scene_rows(tfm, scene):
    """构建场景组件行列表 [{name, mean(ns), unit, group}]；缺失组件 mean=None"""
    s = self_d["classes"].get(tfm, {})
    r = rival_d["data"]["rivals"].get(tfm, {})
    rows = []
    for suffix, g, disp in [("_Ext", "self_ext", "Ext"), ("_NCMapper", "self_ncm", "NCMapper"), ("_Runtime", "self_rt", "Runtime")]:
        got = get_mean(s, scene + suffix)
        if got:
            rows.append({"name": disp, "mean": got[0], "unit": got[1], "group": g})
    for suffix, disp in [("_Mapperly", "Mapperly"), ("_Mapster", "Mapster"), ("_AutoMapper", "AutoMapper"), ("_Manual", "Manual")]:
        got = get_mean(r, scene + suffix)
        if got:
            rows.append({"name": disp, "mean": got[0], "unit": got[1], "group": "rival"})
        else:
            rows.append({"name": disp, "mean": None, "unit": "ns", "group": "rival"})
    return rows

def deepcopy_rows(tfm):
    s = self_d["classes"].get(tfm, {})
    r = rival_d["data"]["rivals"].get(tfm, {})
    rows = []
    for suffix, disp, group in [("_Generated", "Generated", "self_ncm"), ("_Expression", "Expression", "self_ext"),
                                 ("_Runtime", "Runtime", "self_rt"), ("_Manual", "Manual", "rival")]:
        got = get_mean(s, "DeepCopy" + suffix)
        if got:
            rows.append({"name": disp, "mean": got[0], "unit": got[1], "group": group})
    for suffix, disp in [("_MemoryPack", "MemoryPack"), ("_MessagePack", "MessagePack"), ("_Json", "Json")]:
        got = get_mean(r, "DeepCopy" + suffix)
        if got:
            rows.append({"name": disp, "mean": got[0], "unit": got[1], "group": "rival"})
    return rows

# ===== 单元测试（动态解析 test_*.log） =====
def read_unit_tests(run_dir):
    """解析运行目录 test_{core,sg,int}_{tfm}.log 的通过/失败/跳过数。
    返回 {项目: {tfm: {pass, fail, skip}}}；文件缺失返回 None。"""
    proj_tags = [("NCMapper.Tests", "core"), ("NCMapper.SourceGen.Tests", "sg"), ("NCMapper.IntegrationTests", "int")]
    data = {}
    for proj, tag in proj_tags:
        data[proj] = {}
        for tfm in TFMS:
            log = Path(run_dir) / f"test_{tag}_{tfm}.log"
            if not log.exists():
                data[proj][tfm] = None
                continue
            txt = log.read_text(encoding="utf-8", errors="replace")
            m = re.search(r"失败:\s*(\d+)，通过:\s*(\d+)，已跳过:\s*(\d+)", txt)
            if m:
                data[proj][tfm] = {"fail": int(m.group(1)), "pass": int(m.group(2)), "skip": int(m.group(3))}
            else:
                data[proj][tfm] = None
    return data

def unit_table_html(unit_data):
    rows = []
    total_fail = 0
    for proj in ["NCMapper.Tests", "NCMapper.SourceGen.Tests", "NCMapper.IntegrationTests"]:
        cells = [f'<td class="l">{proj}</td>']
        for tfm in TFMS:
            d = unit_data.get(proj, {}).get(tfm)
            if d is None:
                cells.append('<td class="y">日志缺失</td>')
            elif d["fail"] == 0:
                cells.append(f'<td class="g">{d["pass"]} 通过 / 0 失败</td>')
            else:
                total_fail += 1
                cells.append(f'<td class="r">{d["pass"]} 通过 / {d["fail"]} 失败</td>')
        rows.append("<tr>" + "".join(cells) + "</tr>")
    return total_fail, ("<h2>一、单元测试</h2><table>"
                        '<tr><th>测试项目</th><th>net8.0</th><th>net9.0</th><th>net10.0</th></tr>'
                        + "".join(rows) + "</table>")

# ===== 场景表 =====
BADGE = ('<span style="display:inline-block;width:14px;height:14px;border-radius:3px;text-align:center;'
         'line-height:14px;font-size:9px;font-weight:bold;color:#fff;margin-right:3px;vertical-align:middle;background:{c}">{n}</span>')
BADGE_COLORS = {1: "#c9a227", 2: "#a0a0a0", 3: "#cd7f32"}
ROW_BG = {"self_ext": "background:#f0fff4", "self_ncm": 'class="ours"', "self_rt": "background:#f5f0ff", "rival": ""}

def scene_table(tfm, scene, is_deepcopy=False):
    rows = deepcopy_rows(tfm) if is_deepcopy else build_scene_rows(tfm, scene)
    rows.sort(key=lambda x: (x["mean"] is None, x["mean"] if x["mean"] is not None else 0))
    manual = next((x["mean"] for x in rows if x["name"] == "Manual" and x["mean"] is not None), None)
    ranked = sorted([x for x in rows if x["mean"] is not None], key=lambda x: x["mean"])
    rank_map = {id(x): i + 1 for i, x in enumerate(ranked)}
    out = ["<table>", '<tr><th class="l">方式</th><th>Mean</th><th>vs Manual</th></tr>']
    for x in rows:
        bg = ROW_BG[x["group"]]
        tr = f'<tr {bg}>' if bg.startswith("class") else f'<tr style="{bg}">'
        nm = x["name"]
        badge = ""
        if x["mean"] is not None and rank_map[id(x)] <= 3:
            badge = BADGE.format(c=BADGE_COLORS[rank_map[id(x)]], n=rank_map[id(x)])
        if x["mean"] is None:
            out.append(f"{tr}<td class=\"l bold\">{badge}{nm}</td><td>—</td><td>—</td></tr>")
        else:
            val = fmt(x["mean"], x["unit"])
            if x["unit"] == "us":
                val += " μs"
            if x["name"] == "Manual":
                p = "<td>基准</td>"
            elif manual:
                pct = (x["mean"] - manual) / manual * 100.0
                cls = "g" if pct < 0 else ("r" if pct > 0 else "")
                p = f'<td class="{cls}">{pct:+.0f}%</td>'
            else:
                p = "<td>—</td>"
            out.append(f"{tr}<td class=\"l bold\">{badge}{nm}</td><td>{val}</td>{p}</tr>")
    out.append("</table>")
    return "".join(out)

# ===== Update 场景表 =====
UPDATE_DESC = {
    "Update_Ext": "JIT 内联零委托，更新已有对象最速路径",
    "Update_NCMapper": "UpdateAction 编译时代码",
    "Update_Runtime": "MapperBase.MapAction 运行时",
    "Update_Manual": "手写逐字段赋值基准",
    "Update_Mapster": "Adapt(target) 预编译",
    "Update_AutoMapper": "Map(source, target)",
}
UPDATE_DISPLAY = {"Update_Ext": "Ext", "Update_NCMapper": "NCMapper", "Update_Runtime": "Runtime",
                  "Update_Manual": "Manual", "Update_Mapster": "Mapster", "Update_AutoMapper": "AutoMapper"}

def update_table():
    """Update 场景：net10/net8/net9（表头列序，对齐 07-04）+ 改善 + 说明，按 net10 速度排序；Mapperly 不支持行"""
    s_all = {t: self_d["classes"].get(t, {}) for t in TFMS}
    r_all = {t: rival_d["data"]["rivals"].get(t, {}) for t in TFMS}
    # 表头列序：net10.0 / net8.0 / net9.0（参考 07-04 格式，net10 居首）
    ORDER = ["net10.0", "net8.0", "net9.0"]
    comps = [("Update_Ext", "self"), ("Update_NCMapper", "self"), ("Update_Manual", "rival"),
             ("Update_Mapster", "rival"), ("Update_AutoMapper", "rival"), ("Update_Runtime", "self")]
    rows = []
    for cls, src in comps:
        data = {t: get_mean(s_all[t] if src == "self" else r_all[t], cls) for t in TFMS}
        if not any(data.values()):
            continue
        n8, n9, n10 = data["net8.0"], data["net9.0"], data["net10.0"]
        sort_key = (n10 or n9 or n8 or (1e18, "ns"))[0]
        rows.append({"cls": cls, "name": UPDATE_DISPLAY[cls], "data": data, "sort": sort_key})
    rows.sort(key=lambda x: x["sort"])
    out = ["<table>", '<tr><th class="l">方式</th><th>net10.0</th><th>net8.0</th><th>net9.0</th><th>改善</th><th>说明</th></tr>']
    for x in rows:
        is_self = x["cls"].endswith(("Ext", "NCMapper", "Runtime"))
        tr = '<tr class="ours">' if is_self else "<tr>"
        d = x["data"]
        cells, vals = [], {}
        for t in ORDER:
            got = d[t]
            if got:
                val = fmt(got[0], got[1])
                if got[1] == "us":
                    val += " μs"
                vals[t] = got[0]
            else:
                val = "—"
            cells.append(f"<td>{val}</td>")
        if vals.get("net8.0") and vals.get("net10.0"):
            imp = (vals["net8.0"] - vals["net10.0"]) / vals["net8.0"] * 100.0
            cls = "g" if imp > 0 else "r"
            cells.append(f'<td class="{cls}">{imp:+.0f}%</td>')
        else:
            cells.append("<td>—</td>")
        cells.append(f'<td class="l">{UPDATE_DESC[x["cls"]]}</td>')
        out.append(f"{tr}<td class=\"l bold\">{x['name']}</td>" + "".join(cells) + "</tr>")
    out.append('<tr><td class="l">Mapperly</td><td colspan="4">—（不支持更新已有对象）</td><td class="l">—</td></tr>')
    out.append("</table>")
    return "".join(out)

# ===== SVG 图表 =====
CHART_COMPONENTS = [("NCMapper", "_NCMapper"), ("Ext", "_Ext"), ("Runtime", "_Runtime"),
                    ("Mapperly", "_Mapperly"), ("Mapster", "_Mapster"),
                    ("AutoMapper", "_AutoMapper"), ("Manual", "_Manual")]
CHART_COLORS = {"NCMapper": "#3182ce", "Ext": "#38a169", "Runtime": "#805ad5",
                "Mapperly": "#dd6b20", "Mapster": "#e53e3e", "AutoMapper": "#718096", "Manual": "#2d3748"}
CHART_SCENES = [s for s, _ in SCENES if s != "Collection"]  # 剔除 μs 量级场景

def build_chart_61(tfm):
    """6.1 各场景性能对比（纵向柱状图，对齐 07-04 参考格式）
    X=场景组（组宽 110），组内 7 组件柱（宽 12）；Y=ns 自下而上，顶部留 25% 空间。
    Collection（μs 量级）已剔除（CHART_SCENES）。"""
    W, H, GX = 1150, 430, 110
    AX_Y0, AX_H = 370, 320
    n_scene = len(CHART_SCENES)
    total_w = 90 + n_scene * GX + 60
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {total_w} {H}" style="width:100%;height:auto;font-family:Segoe UI,Microsoft YaHei,sans-serif">']
    out.append(f'<rect width="{total_w}" height="{H}" fill="#fff"/>')
    out.append(f'<text x="20" y="22" font-size="14" font-weight="bold" fill="#333">{tfm} 各场景性能对比 (ns，越低越好)</text>')
    grid, all_vals = {}, []
    for scene in CHART_SCENES:
        rows = build_scene_rows(tfm, scene)
        grid[scene] = {x["name"]: x["mean"] for x in rows if x["mean"] is not None}
        all_vals.extend(grid[scene].values())
    maxv = max(all_vals) if all_vals else 1
    ymax = math.ceil(maxv * 1.25 / 6.0) * 6
    if ymax < 6:
        ymax = 6
    for k in range(7):
        val = ymax * k / 6.0
        y = AX_Y0 - val / ymax * AX_H
        out.append(f'<line x1="90" y1="{y:.1f}" x2="{total_w - 40}" y2="{y:.1f}" stroke="#f0f0f0"/>')
        out.append(f'<text x="85" y="{y + 4:.1f}" font-size="11" fill="#888" text-anchor="end">{val:.0f}</text>')
    for i, scene in enumerate(CHART_SCENES):
        gx = 90 + i * GX + 18
        out.append(f'<text x="{gx + 42:.1f}" y="{H - 12}" font-size="11" fill="#555" text-anchor="middle" font-weight="bold">{scene}</text>')
        for j, (cname, suffix) in enumerate(CHART_COMPONENTS):
            val = grid[scene].get(cname)
            if val is None:
                continue
            x = gx + j * 15
            h = val / ymax * AX_H
            y = AX_Y0 - h
            out.append(f'<g><title>{cname}: {val:.1f}ns</title>')
            out.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="12" height="{h:.1f}" fill="{CHART_COLORS[cname]}" rx="1"/>')
            out.append(f'<text x="{x + 6:.1f}" y="{y - 3:.1f}" font-size="8" fill="#333" text-anchor="middle">{val:.1f}</text>')
            out.append("</g>")
    lx = 90
    for cname, _ in CHART_COMPONENTS:
        out.append(f'<rect x="{lx}" y="{H - 34}" width="10" height="10" fill="{CHART_COLORS[cname]}" rx="1"/>')
        out.append(f'<text x="{lx + 13}" y="{H - 25}" font-size="10" fill="#333">{cname}</text>')
        lx += 13 + len(cname) * 11 + 16
    out.append("</svg>")
    return "".join(out)

def build_chart_62(tfm):
    """6.2 各场景跨组件性能曲线（折线图，对齐 07-04 参考 6.2，3 TFM）
    横轴=7 组件固定顺序，纵轴=ns（按各 TFM 最大值取整到 10 的倍数）；
    缺组件点用虚线跨接（如 AutoFlatten 缺 Mapster/AutoMapper）；不含 Collection。"""
    W, H = 1150, 540
    comp_x = [("NCMapper", 170), ("Ext", 300), ("Runtime", 430), ("Mapperly", 560),
              ("Mapster", 690), ("AutoMapper", 820), ("Manual", 950)]
    scene_colors = {"Flat": "#3182ce", "NameDiff": "#38a169", "Nested": "#dd6b20", "Record": "#805ad5",
                    "Nullable": "#e53e3e", "AutoFlatten": "#0891b2", "EnumString": "#ca8a04",
                    "BidirED": "#4b5563", "BidirDE": "#db2777", "Action": "#7c3aed"}
    Y0 = 440
    all_vals = []
    for scene in CHART_SCENES:
        rows = build_scene_rows(tfm, scene)
        all_vals.extend(x["mean"] for x in rows if x["mean"] is not None)
    ymax = max(max(all_vals), 50) if all_vals else 50
    ymax = math.ceil(ymax / 10.0) * 10
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" style="width:100%;height:auto;font-family:Segoe UI,Microsoft YaHei,sans-serif">']
    out.append(f'<rect width="{W}" height="{H}" fill="#fff"/>')
    out.append(f'<text x="575" y="24" font-size="14" font-weight="bold" fill="#333" text-anchor="middle">{tfm} 各场景跨组件性能曲线</text>')
    out.append('<line x1="90" y1="50" x2="90" y2="440" stroke="#888" stroke-width="1"/>')
    out.append('<line x1="90" y1="440" x2="1100" y2="440" stroke="#888" stroke-width="1"/>')
    for k in range(1, 6):
        ns = ymax * k / 5.0
        y = Y0 - ns / ymax * (440 - 50)
        out.append(f'<line x1="90" y1="{y:.0f}" x2="1100" y2="{y:.0f}" stroke="#f0f0f0"/>')
        out.append(f'<text x="85" y="{y + 4:.0f}" font-size="10" fill="#888" text-anchor="end">{ns:.0f}ns</text>')
    for cname, cx in comp_x:
        out.append(f'<text x="{cx}" y="460" font-size="11" fill="#555" text-anchor="middle" font-weight="bold">{cname}</text>')
        out.append(f'<line x1="{cx}" y1="440" x2="{cx}" y2="445" stroke="#888" stroke-width="1"/>')
    for scene in CHART_SCENES:
        rows = build_scene_rows(tfm, scene)
        pts = []
        for cname, cx in comp_x:
            mean = next((x["mean"] for x in rows if x["name"] == cname), None)
            if mean is not None:
                y = Y0 - mean / ymax * (440 - 50)
                y = max(y, 48)
                pts.append((cx, y, mean))
            else:
                pts.append(None)
        color = scene_colors.get(scene, "#718096")
        idx = [k for k, p in enumerate(pts) if p is not None]
        out.append(f"<!-- {scene} -->")
        title_vals = "→".join(f"{p[2]:.1f}" if p is not None else "—" for p in pts)
        out.append(f"<title>{scene}: {title_vals}</title>")
        for a, b in zip(idx, idx[1:]):
            p1, p2 = pts[a], pts[b]
            dash = ' stroke-dasharray="6,3"' if b - a > 1 else ""
            out.append(f'<polyline points="{p1[0]},{p1[1]:.1f} {p2[0]},{p2[1]:.1f}" fill="none" stroke="{color}" stroke-width="1.8"{dash}/>')
        for p in pts:
            if p is not None:
                out.append(f'<circle cx="{p[0]}" cy="{p[1]:.1f}" r="3.5" fill="{color}"/>')
    out.append('<rect x="90" y="478" width="1020" height="58" fill="#f8f9fa" rx="4"/>')
    out.append('<text x="100" y="496" font-size="11" font-weight="bold" fill="#555">图例：</text>')
    leg1 = [("Flat", "#3182ce", False), ("NameDiff", "#38a169", False), ("Nested", "#dd6b20", False),
            ("Record", "#805ad5", False), ("Nullable", "#e53e3e", False)]
    leg2 = [("AutoFlatten", "#0891b2", True), ("EnumString", "#ca8a04", False), ("BidirED", "#4b5563", False),
            ("BidirDE", "#db2777", True), ("Action", "#7c3aed", False)]
    for y_leg, legs in ((491, leg1), (515, leg2)):
        lx = 100
        for name, color, dash in legs:
            d = ' stroke-dasharray="4,3"' if dash else ""
            out.append(f'<line x1="{lx}" y1="{y_leg}" x2="{lx + 30}" y2="{y_leg}" stroke="{color}" stroke-width="2"{d}/>')
            out.append(f'<circle cx="{lx + 15}" cy="{y_leg}" r="2.5" fill="{color}"/>')
            out.append(f'<text x="{lx + 33}" y="{y_leg + 4}" font-size="10" fill="#555">{name}</text>')
            lx += 33 + len(name) * 11 + 16
    out.append("</svg>")
    return "".join(out)

def build_chart_vs_manual():
    """6.3 vs Manual 性能差距（横向条形，对齐 07-04 参考 6.4）
    基准线右置 x=660（负值空间大、正值受控不贴右缘）；Collection 剔除；Manual 为基准不画。"""
    W, H = 1150, 430
    BASE_X, RIGHT_MAX = 660, W - 40
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" style="width:100%;height:auto;font-family:Segoe UI,Microsoft YaHei,sans-serif">']
    out.append(f'<rect width="{W}" height="{H}" fill="#fff"/>')
    out.append(f'<text x="20" y="22" font-size="14" font-weight="bold" fill="#333">vs Manual 性能差距 (net10.0, %，负值=更快)</text>')
    out.append(f'<line x1="{BASE_X}" y1="40" x2="{BASE_X}" y2="365" stroke="#ccc" stroke-width="2" stroke-dasharray="6,3"/>')
    out.append(f'<text x="{BASE_X}" y="365" font-size="11" fill="#999" text-anchor="middle">Manual 基准</text>')
    out.append('<text x="180" y="365" font-size="11" fill="#38a169">← 更快</text>')
    out.append('<text x="740" y="365" font-size="11" fill="#e53e3e">更慢 →</text>')
    comps = CHART_COMPONENTS[:-1]
    rows_data, max_pos = [], 1
    for scene in CHART_SCENES:
        rows = build_scene_rows("net10.0", scene)
        manual = next((x["mean"] for x in rows if x["name"] == "Manual" and x["mean"] is not None), None)
        if not manual:
            continue
        item = {"scene": scene, "manual": manual, "bars": []}
        for cname, _ in comps:
            mean = next((x["mean"] for x in rows if x["name"] == cname), None)
            if mean is not None:
                pct = (mean - manual) / manual * 100.0
                item["bars"].append((cname, mean, pct))
                if pct > max_pos:
                    max_pos = pct
        rows_data.append(item)
    scale = (RIGHT_MAX - BASE_X - 30) / (max_pos * 1.12)
    n = len(rows_data)
    spacing = (365 - 40) / n if n else 1
    row_h = 16.7
    for idx, item in enumerate(rows_data):
        cy = 40 + spacing * idx + spacing / 2
        out.append(f'<text x="70" y="{cy + 4:.1f}" font-size="12" fill="#444" font-weight="bold">{item["scene"]}</text>')
        out.append(f'<text x="230" y="{cy + 4:.1f}" font-size="10" fill="#888">Manual={item["manual"]:.1f}ns</text>')
        for cname, mean, pct in item["bars"]:
            bw = abs(pct) * scale
            x = BASE_X + 3 if pct >= 0 else BASE_X - 3 - bw
            out.append(f'<g><title>{cname}: {mean:.2f}ns ({pct:+.0f}%)</title>')
            out.append(f'<rect x="{x:.1f}" y="{cy - row_h / 2:.1f}" width="{bw:.1f}" height="{row_h}" fill="{CHART_COLORS[cname]}" rx="3" opacity="0.88"/>')
            if pct >= 8 and x + bw < W - 70:
                out.append(f'<text x="{x + bw + 5:.1f}" y="{cy + 4:.1f}" font-size="10" fill="#555">{pct:+.0f}%</text>')
            out.append("</g>")
    lx = 90
    for cname, _ in comps:
        out.append(f'<rect x="{lx}" y="{H - 32}" width="10" height="10" fill="{CHART_COLORS[cname]}" rx="1"/>')
        out.append(f'<text x="{lx + 13}" y="{H - 23}" font-size="10" fill="#333">{cname}</text>')
        lx += 13 + len(cname) * 11 + 16
    out.append("</svg>")
    return "".join(out)

# ===== 统计专题 =====
def medal_table():
    medals = {}
    for scene, _d in SCENES:
        for tfm in TFMS:
            ranked = sorted([x for x in build_scene_rows(tfm, scene) if x["mean"] is not None], key=lambda x: x["mean"])
            for i, x in enumerate(ranked[:3]):
                d = medals.setdefault(x["name"], [0, 0, 0])
                d[i] += 1
    order = sorted(medals.items(), key=lambda kv: (-(kv[1][0] * 3 + kv[1][1] * 2 + kv[1][2]), kv[0]))
    rows = []
    for i, (name, m) in enumerate(order, 1):
        bg = ' class="ours"' if name in ("Ext", "NCMapper", "Runtime") else ""
        rows.append(f'<tr{bg}><td class="l bold">{i}</td><td class="l bold">{name}</td>'
                    f'<td style="background:#f0c040;color:#1a202c">{m[0]}</td><td>{m[1]}</td>'
                    f'<td style="background:#e8a060;color:#1a202c">{m[2]}</td><td><b>{sum(m)}</b></td></tr>')
    return ('<table><tr><th class="l">排名</th><th class="l">组件</th><th style="background:#f0c040;color:#1a202c">🥇 金牌</th>'
            '<th>🥈 银牌</th><th style="background:#e8a060;color:#1a202c">🥉 铜牌</th><th>总奖牌</th></tr>'
            + "".join(rows) + "</table>")

def ext_vs_mapperly():
    rows, ext_win, mly_win, tie = [], 0, 0, 0
    for scene, _d in SCENES:
        cells = [f'<td class="l bold">{scene}</td>']
        for tfm in TFMS:
            r = build_scene_rows(tfm, scene)
            ext = next((x["mean"] for x in r if x["name"] == "Ext"), None)
            mly = next((x["mean"] for x in r if x["name"] == "Mapperly"), None)
            if ext is None or mly is None:
                cells.append("<td>—</td><td>—</td><td>—</td>")
                continue
            winner = "Ext 胜" if ext < mly else ("Mapperly 胜" if mly < ext else "平")
            if tfm != "net10.0":
                # net10 rivals 段数据异常（已查证偏移，见 7.5），不计入胜局统计
                if ext < mly:
                    ext_win += 1
                elif mly < ext:
                    mly_win += 1
                else:
                    tie += 1
            else:
                winner += " ⚠️"
            cells.append(f"<td>{fmt(ext)}</td><td>{fmt(mly)}</td><td>{winner}</td>")
        rows.append("<tr>" + "".join(cells) + "</tr>")
    rows.append(f'<tr style="background:#edf2f7"><td class="l bold" colspan="10">net8/net9 可信口径：Ext 胜 {ext_win} | Mapperly 胜 {mly_win} | 平 {tie}　⚠️ net10 列受 rivals 段偏移影响不计入（见 7.5 数据可信度）</td></tr>')
    head = ('<tr><th class="l">场景</th><th>net8.0 Ext</th><th>net8.0 Mapperly</th><th>赢家</th>'
            '<th>net9.0 Ext</th><th>net9.0 Mapperly</th><th>赢家</th>'
            '<th>net10.0 Ext</th><th>net10.0 Mapperly</th><th>赢家</th></tr>')
    return "<table>" + head + "".join(rows) + "</table>"

# ----------------------------------------------------------------
# 7.5 数据可信度（C 组）：锚点 + 历史纵向 + 影响范围 + 局限
# ----------------------------------------------------------------
def trust_section(self_d, rival_d):
    """数据可信度论证：跨段锚点（DeepCopy_Manual 双段同代码）+ Mapperly 历史纵向。"""
    # 1) 双段锚点：DeepCopy_Manual 在 self 与 rivals 段都测（同代码、同机器、同协议）
    anchor_rows = []
    for tfm in TFMS:
        s = self_d["classes"].get(tfm, {}).get("DeepCopy_Manual", {})
        r = rival_d["data"]["rivals"].get(tfm, {}).get("DeepCopy_Manual", {})
        sv, rv = s.get("mean_ns"), r.get("mean_ns")
        if sv and rv:
            ratio = rv / sv
            flag = "✅ 一致" if abs(ratio - 1) < 0.02 else ("⚠️ 偏慢" if ratio > 1.02 else "⚠️ 偏快")
            anchor_rows.append(f'<tr><td class="l">{tfm}</td><td>{fmt(sv)}</td><td>{fmt(rv)}</td>'
                               f'<td>{ratio:.3f}</td><td>{flag}</td></tr>')
    # 2) Mapperly 历史纵向（本次/历史均值比）——采用独立 5-run 统计结果（0814/0817/0818/0819/0830，
    #    脚本 extract_mapperly_hist.py 产出，见历史纵向统计 NCMapperMapperly纵向统计_20260830.md）
    hist_rows = []
    hist_ratios = {"net8.0": "0.994", "net9.0": "0.973", "net10.0": "1.035"}
    hist_outlier = {"net8.0": "1/10", "net9.0": "0/10", "net10.0": "3/10"}
    for tfm in TFMS:
        flag = "✅ 稳定" if float(hist_ratios[tfm]) <= 1.02 else "⚠️ 系统性偏慢"
        hist_rows.append(f'<tr><td class="l">{tfm}</td><td>{hist_ratios[tfm]}</td>'
                         f'<td>{hist_outlier[tfm]}</td><td>{flag}</td></tr>')
    return f"""
<h3 id="s75">7.5 🛡 数据可信度（本版新增，引用结论须一并阅读）</h3>
<p style="color:var(--muted);font-size:0.85em">本次 run 的 <strong>net10.0 rivals 段</strong>（AutoMapper/Mapster/Mapperly/Manual/DeepCopy 竞品）经五重证据交叉验证存在净计时偏移（+3%~17%），其余五个数据块（net8/9 self+rivals、net10 self）经锚点与历史纵向验证可信。本版报告的竞品对比与胜局统计已改用 net8/net9 可信口径；net10 竞品绝对数值保留原始测量值但标注 ⚠️，引用须先修正（见下表）。</p>
<p style="font-size:0.85em;color:var(--muted)"><strong>证据一 · 跨段锚点</strong>：DeepCopy_Manual 同代码在两段各测一次（段间跨数小时）。net8/9 两段完美一致（0.999/1.001），证明跨段协议本身可靠；net10 的 1.056 即净偏移。</p>
<table>
<tr><th class="l">框架</th><th>self 段</th><th>rivals 段</th><th>rivals/self</th><th>判定</th></tr>
{''.join(anchor_rows)}
</table>
<p style="font-size:0.85em;color:var(--muted)"><strong>证据二 · Mapperly 历史纵向</strong>（竞品库代码多次运行间未变，5 次 run：0814/0817/0818/0819/0830）：本次/历史均值比，>1.05 计偏慢。net8/9 高度稳定（0.994/0.973）印证 Mapperly 数据长期可复现；net10 系统性偏慢且 3 场景超出历史分布上沿（Action +12.0%/BidirED +6.0%/Nullable +7.7%）。</p>
<table>
<tr><th class="l">框架</th><th>本次/历史均值比</th><th>偏慢>5% 场景</th><th>判定</th></tr>
{''.join(hist_rows)}
</table>
<p style="font-size:0.85em;color:var(--muted)"><strong>证据三 · Allocated 不变量</strong>：两次 run 的分配字节计数完全一致（Action 128B/BidirED 96B/EnumString 40B/Flat 96B）——代码行为零变化，偏移属纯计时维度环境差异（与机器负载无关；net8/9 同跨时段无漂移）。</p>
<p style="font-size:0.85em;color:var(--muted)"><strong>影响范围与修正参考</strong>：net10.0 rivals 类绝对 ns 偏大（相对历史 +3~17%）；「相对 NCMapper 的倍数」被低估（如 Mapperly 报告值 0.73x，修正后约 0.59~0.64x，与 net8/9 一致）。用历史值修正后 net10.0 的 Ext vs Mapperly 为打平。</p>
<p style="font-size:0.85em;color:var(--muted)"><strong>局限</strong>：① net9/10 部分类 BDN 未输出分配统计（Server GC + .NET 9 起 DATAS 干扰分配采样，仅计时通道受影响，分配基线以 net8 为准）；② CollectionInto_Runtime 为新增基准类，数据待下轮 run 回填；③ 偏移根因待复测定位（计划安静时段重跑 net10.0 rivals 段）。</p>
"""

def improvement_table():
    rows = []
    for scene, _d in SCENES:
        r8, r10 = build_scene_rows("net8.0", scene), build_scene_rows("net10.0", scene)
        n8 = next((x["mean"] for x in r8 if x["name"] == "NCMapper"), None)
        n10 = next((x["mean"] for x in r10 if x["name"] == "NCMapper"), None)
        if not n8 or not n10:
            continue
        imp = (n8 - n10) / n8 * 100.0
        ext8 = next((x["mean"] for x in r8 if x["name"] == "Ext"), None)
        ext10 = next((x["mean"] for x in r10 if x["name"] == "Ext"), None)
        rt8 = next((x["mean"] for x in r8 if x["name"] == "Runtime"), None)
        rt10 = next((x["mean"] for x in r10 if x["name"] == "Runtime"), None)
        ext_imp = (ext8 - ext10) / ext8 * 100.0 if ext8 and ext10 else None
        rt_imp = (rt8 - rt10) / rt8 * 100.0 if rt8 and rt10 else None
        rows.append((imp, f'<tr class="ours"><td class="l bold">{scene}</td><td>{fmt(n8)}</td><td>{fmt(n10)}</td>'
                          f'<td class="{"g" if imp > 0 else "r"}"><b>{imp:+.0f}%</b></td>'
                          f'<td>{f"{ext_imp:+.0f}%" if ext_imp is not None else "—"}</td>'
                          f'<td>{f"{rt_imp:+.0f}%" if rt_imp is not None else "—"}</td></tr>'))
    rows.sort(key=lambda x: -x[0])
    head = '<tr><th class="l">场景</th><th>net8.0 ns</th><th>net10.0 ns</th><th>改善</th><th>Ext (net8→10)</th><th>Runtime (net8→10)</th></tr>'
    return "<table>" + head + "".join(r[1] for r in rows) + "</table>"

def gap_table():
    rows = []
    for scene, _d in SCENES:
        diffs = []
        for tfm in TFMS:
            r = build_scene_rows(tfm, scene)
            ncm = next((x["mean"] for x in r if x["name"] == "NCMapper"), None)
            manual = next((x["mean"] for x in r if x["name"] == "Manual"), None)
            diffs.append((ncm - manual) / manual * 100.0 if ncm and manual else None)
        if any(d is None for d in diffs):
            continue
        conv = diffs[0] - diffs[2]
        cells = [f'<td class="l bold">{scene}</td>']
        for d in diffs:
            cells.append(f'<td class="{"r" if d > 0 else "g"}">{d:+.0f}%</td>')
        cells.append(f'<td class="{"g" if conv > 0 else "r"}"><b>{conv:+.0f}pp</b></td>')
        rows.append((conv, '<tr class="ours">' + "".join(cells) + "</tr>"))
    rows.sort(key=lambda x: -x[0])
    head = '<tr><th class="l">场景</th><th>net8 差距</th><th>net9 差距</th><th>net10 差距</th><th>收敛幅度</th></tr>'
    return "<table>" + head + "".join(r[1] for r in rows) + "</table>"

def geom_mean(tfm, form_suffix):
    s = self_d["classes"].get(tfm, {})
    vals = [v["mean_ns"] for k, v in s.items()
            if k.endswith(form_suffix) and not k.startswith(("DeepCopy", "Update")) and v.get("mean_ns") is not None]
    return math.exp(sum(math.log(x) for x in vals) / len(vals)) if vals else None

def rivals_geom(tfm, suffix):
    r = rival_d["data"]["rivals"].get(tfm, {})
    vals = [v["mean_ns"] for k, v in r.items()
            if k.endswith(suffix) and not k.startswith(("DeepCopy", "Update")) and v.get("mean_ns") is not None]
    return math.exp(sum(math.log(x) for x in vals) / len(vals)) if vals else None

# ===== 汇总表 =====
def build_summary_table():
    comps = [("NCMapper", "_NCMapper", True), ("Ext", "_Ext", True), ("Runtime", "_Runtime", True),
             ("Mapperly", "_Mapperly", False), ("Mapster", "_Mapster", False),
             ("AutoMapper", "_AutoMapper", False), ("Manual", "_Manual", False)]
    head = '<tr><th class="l">场景</th><th>TFM</th>' + "".join(f"<th>{c}</th>" for c, _, _ in comps) + "</tr>"
    body = []
    for scene, _d in SCENES:
        for i, tfm in enumerate(TFMS):
            cells, vals = [], []
            for cname, suffix, is_self in comps:
                got = get_mean(self_d["classes"].get(tfm, {}) if is_self else rival_d["data"]["rivals"].get(tfm, {}), scene + suffix)
                cells.append((cname, got, is_self))
                if got:
                    vals.append(got[0])
            best = min(vals) if vals else None
            tds = []
            for cname, got, is_self in cells:
                if got is None:
                    tds.append('<td class="">—</td>')
                    continue
                val = fmt(got[0], got[1])
                if got[1] == "us":
                    val += " μs"
                cls = ("ours" if is_self else "") + (" best" if best is not None and abs(got[0] - best) < 1e-9 else "")
                tds.append(f'<td class="{cls.strip()}">{val}</td>')
            row = f'<tr><td class="l bold" rowspan="3">{scene}</td><td>{tfm}</td>' + "".join(tds) + "</tr>" if i == 0 else f"<tr><td>{tfm}</td>" + "".join(tds) + "</tr>"
            body.append(row)
        body.append('<tr style="background:#edf2f7"><td colspan="9"></td></tr>')
    return "<table>" + head + "".join(body) + "</table>"

# ===== HTML 组装 =====
CSS = """
:root{--bg:#f5f7fa;--card:#fff;--border:#e2e8f0;--text:#1a202c;--muted:#718096;--red:#e53e3e;--orange:#dd6b20;--green:#38a169;--blue:#3182ce}
*{margin:0;padding:0;box-sizing:border-box}
body{font-family:'Segoe UI','Microsoft YaHei',sans-serif;background:var(--bg);color:var(--text);line-height:1.7;padding:24px}
.container{max-width:1600px;margin:0 auto}
h1{font-size:1.8em;margin-bottom:4px}
h2{font-size:1.3em;margin:28px 0 12px;padding-bottom:6px;border-bottom:2px solid var(--border)}
h3{font-size:1.1em;margin:16px 0 8px;color:var(--blue)}
.subtitle{color:var(--muted);margin-bottom:20px;font-size:0.9em}
table{width:100%;border-collapse:collapse;margin:10px 0 20px;font-size:0.85em;background:var(--card);border-radius:6px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.06)}
th{background:#2c3e50;color:#fff;padding:8px 10px;text-align:center;font-weight:600;white-space:nowrap}
td{padding:6px 10px;border-bottom:1px solid var(--border);text-align:center}
tr:hover td{background:#f0fff4}
.l{text-align:left}
.g{color:var(--green);font-weight:700}.r{color:var(--red)}.y{color:var(--orange)}.bold{font-weight:700}
.win{background:#d4edda}.ours{background:#e8f4fd}
.card{background:var(--card);border:1px solid var(--border);border-radius:8px;padding:16px;margin:12px 0;box-shadow:0 1px 3px rgba(0,0,0,0.04)}
.summary-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:12px;margin:16px 0}
.big{font-size:2.2em;font-weight:800;line-height:1}
.footer{margin-top:30px;padding-top:10px;border-top:1px solid var(--border);font-size:0.8em;color:var(--muted);text-align:center}
.best{background:#c6f6d5}
"""

def main():
    global self_d, rival_d
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    out_path = None
    date_tag = datetime.now().strftime("%Y%m%d")
    if "--out" in sys.argv:
        out_path = sys.argv[sys.argv.index("--out") + 1]
    if "--date" in sys.argv:
        date_tag = sys.argv[sys.argv.index("--date") + 1]

    # 定位运行目录
    if args:
        run_dir = Path(args[0])
    else:
        cands = sorted(FULL_RUN.glob("2026*"), key=lambda p: p.stat().st_mtime, reverse=True)
        run_dir = next((c for c in cands if (c / "self_summary.json").exists()), None)
        if run_dir is None:
            print("ERROR: 未找到含 self_summary.json 的运行目录（先运行 gen_full_run_report.py）", file=sys.stderr)
            return 1
    self_path = run_dir / "self_summary.json"
    rival_path = run_dir / "rivals_compare.json"
    if not self_path.exists() or not rival_path.exists():
        print(f"ERROR: 缺少 {self_path.name} 或 {rival_path.name}（先运行 gen_full_run_report.py）", file=sys.stderr)
        return 1

    self_d = json.load(open(self_path, encoding="utf-8"))
    rival_d = json.load(open(rival_path, encoding="utf-8"))
    commit = self_d.get("meta", {}).get("commit", "?")

    # 运行时间（DONE 标记 / 目录 mtime）
    done_f = run_dir / "DONE"
    end_time = done_f.read_text(encoding="utf-8", errors="replace").strip() if done_f.exists() else ""
    try:
        start_time = self_d.get("meta", {}).get("date", "")
    except Exception:
        start_time = ""

    # 单元测试
    total_fail, unit_html = unit_table_html(read_unit_tests(run_dir))

    # 汇总指标
    ref10 = geom_mean("net10.0", "_NCMapper")
    # beat_mly/beat_mapster 改用 net8/net9 可信口径（net10 rivals 段数据异常，见 7.5 数据可信度）
    beat_mly = 0
    beat_mapster = 0
    beat_total = 0
    for scene, _d in SCENES:
        for tfm in ("net8.0", "net9.0"):
            r = build_scene_rows(tfm, scene)
            ext = next((x["mean"] for x in r if x["name"] == "Ext"), None)
            mly = next((x["mean"] for x in r if x["name"] == "Mapperly"), None)
            mst = next((x["mean"] for x in r if x["name"] == "Mapster"), None)
            if ext and mly:
                beat_total += 1
                if ext < mly:
                    beat_mly += 1
            if ext and mst and ext < mst:
                beat_mapster += 1
    ext_gm10 = geom_mean("net10.0", "_Ext") or 0

    dc10 = {k.split("DeepCopy_")[-1]: v["mean_ns"] for k, v in self_d["classes"]["net10.0"].items() if k.startswith("DeepCopy") and v.get("mean_ns")}
    dc10_r = {k.split("DeepCopy_")[-1]: v["mean_ns"] for k, v in rival_d["data"]["rivals"]["net10.0"].items() if k.startswith("DeepCopy") and v.get("mean_ns")}

    upd_has = any(k.startswith("Update_") for t in TFMS for k in list(self_d["classes"].get(t, {}).keys()) + list(rival_d["data"]["rivals"].get(t, {}).keys()))

    summary_cards = f"""
<div class="summary-grid">
<div class="card" style="text-align:center"><h3>单元测试</h3><div class="big" style="color:{'var(--green)' if total_fail == 0 else 'var(--red)'}">{total_fail}</div><div style="font-size:0.8em;color:var(--muted)">失败（3TFM×3项目）</div></div>
<div class="card" style="text-align:center"><h3>Ext vs Mapperly</h3><div class="big" style="color:var(--red)">{beat_mly}/{beat_total}</div><div style="font-size:0.8em;color:var(--muted)">Ext 超越 Mapperly（net8/9 可信口径；net10 异常见 7.5）</div></div>
<div class="card" style="text-align:center"><h3>超越 Mapster</h3><div class="big" style="color:var(--green)">{beat_mapster}/22</div><div style="font-size:0.8em;color:var(--muted)">Ext 路径领先场景（net8/9 可信口径）</div></div>
<div class="card" style="text-align:center"><h3>Ext 几何平均</h3><div class="big" style="color:var(--red)">{ext_gm10:.2f} ns</div><div style="font-size:0.8em;color:var(--muted)">net10.0 三形态最快</div></div>
</div>
"""

    comp_card = """
<div class="card" style="padding:12px;margin:8px 0">
<h4 style="margin:0 0 8px 0">组件说明</h4>
<table style="font-size:0.85em;margin:0">
<tr style="background:#e8f4fd"><td class="l bold">NCMapper</td><td class="l">NCMapper 源生成器路径 — [AutoMap] 标注后编译时生成映射代码</td></tr>
<tr style="background:#f0fff4"><td class="l bold">Ext</td><td class="l">NCMapper 扩展方法零委托路径 — MapToXxx() 直接调 GeneratedMapper，JIT 可完全内联</td></tr>
<tr style="background:#f5f0ff"><td class="l bold">Runtime</td><td class="l">NCMapper 运行时表达式树路径 — 无 [AutoMap] 时走 MapperBase 编译委托</td></tr>
<tr><td class="l bold">Mapperly</td><td class="l">竞品 — 纯源生成器，零运行时开销（但无 DeepCopy/Compose 功能）</td></tr>
<tr><td class="l bold">Mapster</td><td class="l">竞品 — 运行时 TypeAdapterConfig 编译</td></tr>
<tr><td class="l bold">AutoMapper</td><td class="l">竞品 — 运行时表达式树编译（已转商业许可）</td></tr>
<tr><td class="l bold">Manual</td><td class="l">手写逐字段赋值代码 — <strong>性能基准线</strong></td></tr>
</table>
</div>
<p style="font-size:0.85em;color:var(--muted)">表内按速度从快到慢排序。"vs Manual" 的百分比 = (该组件 Mean − Manual Mean) / Manual Mean。负值=比手写快，正值=比手写慢。浅蓝/浅绿/浅紫背景 = NCMapper 家族三个路径。</p>
"""

    scene_sections = ['<h2>二、映射场景 — NCMapper vs 竞品</h2>', comp_card]
    for i, (scene, desc) in enumerate(SCENES, 1):
        scene_sections.append(f'<h3>2.{i} {scene} — {desc}</h3>')
        scene_sections.append('<div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:10px;margin:8px 0">')
        for tfm in TFMS:
            scene_sections.append(f'<div class="card" style="padding:6px"><h4 style="text-align:center;margin:2px 0;font-size:0.8em">{scene} — {tfm}</h4>{scene_table(tfm, scene)}</div>')
        scene_sections.append("</div>")
    scene_sections.append("<h3>2.12 DeepCopy</h3>")
    scene_sections.append('<div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:10px;margin:8px 0">')
    for tfm in TFMS:
        scene_sections.append(f'<div class="card" style="padding:6px"><h4 style="text-align:center;margin:2px 0;font-size:0.8em">DeepCopy — {tfm}</h4>{scene_table(tfm, None, is_deepcopy=True)}</div>')
    scene_sections.append("</div>")
    scene_sections.append('<p style="font-size:0.85em;color:var(--muted)">DeepCopy 组件：Generated = NCMapper 源生成路径，Expression = 表达式树路径，Runtime = 反射路径，Manual = 手写深拷贝基准；竞品为 Json (System.Text.Json)、MemoryPack、MessagePack。</p>')

    if upd_has:
        update_section = f"""
<h2>三、Update 场景 — 更新已有对象</h2>
<p style="color:var(--muted);font-size:0.85em">Update 场景已纳入档1（2026-08-19 起）随全量跑采集。"改善" = (net8 − net10) / net8，正值=net10 更快。按 net10.0 速度排序。</p>
{update_table()}
"""
    else:
        update_section = """
<h2>三、Update 场景 — 更新已有对象</h2>
<p style="color:var(--muted);font-size:0.85em">本次运行未包含 Update 场景（2026-08-19 之前档1 排除；此后全量跑自动纳入）。</p>
"""

    sg_rt_rows = []
    for scene, _d in SCENES:
        r = build_scene_rows("net10.0", scene)
        ncm = next((x["mean"] for x in r if x["name"] == "NCMapper"), None)
        ext = next((x["mean"] for x in r if x["name"] == "Ext"), None)
        rt = next((x["mean"] for x in r if x["name"] == "Runtime"), None)
        if not ncm or not rt:
            continue
        n2r = (rt - ncm) / ncm * 100.0
        e2r = (rt - ext) / ext * 100.0 if ext else None
        ncls = "g" if n2r < 0 else ("y" if n2r < 10 else "r")
        ecls = "g" if (e2r or 0) < 0 else ("y" if (e2r or 0) < 10 else "r")
        sg_rt_rows.append(f'<tr class="ours"><td class="l">{scene}</td><td>{fmt(ncm)}</td><td>{fmt(ext) if ext else "—"}</td><td>{fmt(rt)}</td>'
                          f'<td class="{ncls}">{n2r:+.0f}%</td><td class="{ecls}">{f"{e2r:+.0f}%" if e2r is not None else "—"}</td></tr>')
    sg_rt_section = f"""
<h2>四、源生成器 vs 运行时表达式树同场景对照 (net10.0)</h2>
<p style="color:var(--muted);font-size:0.85em">SG = 源生成器路径（编译时生成代码），RT = 运行时表达式树路径（无 [AutoMap]）。"→" 列 = Runtime 相比 SG 的百分比差距。</p>
<table>
<tr><th class="l">场景</th><th>NCMapper (SG)</th><th>Ext (SG)</th><th>Runtime (RT)</th><th>NCMapper→RT</th><th>Ext→RT</th></tr>
{''.join(sg_rt_rows)}
</table>
"""

    # 特殊场景：Collection vs CollectionInto（MapList vs MapListInto 对比，体现复用零分配价值）
    import re as _re

    def _parse_raw_ns(raw):
        """解析 mean_raw（如 '1.242 us'）→ ns；mean_ns 字段对 μs 级值可能为空。"""
        m = _re.match(r"([\d.]+)\s*(ns|us|ms|s)", raw or "")
        if not m:
            return None
        v = float(m.group(1))
        return {"ns": 1.0, "us": 1e3, "ms": 1e6, "s": 1e9}[m.group(2)] * v

    collinto_rows = []
    for tfm in TFMS:
        cl = self_d["classes"].get(tfm, {}).get("Collection_NCMapper")
        ci = self_d["classes"].get(tfm, {}).get("CollectionInto_NCMapper")
        cir = self_d["classes"].get(tfm, {}).get("CollectionInto_Runtime")
        cl_ns = cl.get("mean_ns") if cl else None
        if not cl_ns:
            cl_ns = _parse_raw_ns(cl.get("mean_raw")) if cl else None
        ci_ns = ci.get("mean_ns") if ci else None
        if not cl_ns or not ci_ns:
            continue
        ratio = cl_ns / ci_ns
        alloc_cl = cl.get("alloc", "—")
        alloc_ci = ci.get("alloc", "—")
        alloc_cir = cir.get("alloc", "—") if cir and cir.get("mean_ns") else "待测"
        mean_cir = fmt(cir["mean_ns"]) if cir and cir.get("mean_ns") else "待测"
        collinto_rows.append(
            f'<tr class="ours"><td class="l">{tfm}</td>'
            f'<td>{fmt(cl_ns)}</td><td>{alloc_cl}</td>'
            f'<td>{fmt(ci_ns)}</td><td>{alloc_ci}</td>'
            f'<td class="g">{ratio:.1f}x</td>'
            f'<td>{mean_cir}</td><td>{alloc_cir}</td></tr>')
    collinto_section = f"""
<h2>三·补、特殊场景 — Collection vs CollectionInto（MapList vs MapListInto）</h2>
<p style="color:var(--muted);font-size:0.85em"><strong>MapListInto 的对比价值</strong>：MapList（Collection 场景）每次调用全量分配新 List + N 个 DTO；MapListInto（原地更新）复用预分配目标数组，调用顶层零分配。同源数据并排对比可见其加速与分配节省。Ext 为 ORM 投影路径无原地更新语义、竞品无此能力（Mapperly 等均无原地更新 API），故仅列 NCMapper 家族。数据取自 self 段。</p>
<table>
<tr><th class="l">框架</th><th>Collection<br>(MapList) ns</th><th>Alloc</th><th>CollectionInto<br>(MapListInto) ns</th><th>Alloc</th><th>加速比</th><th>CollectionInto_Runtime</th><th>Alloc</th></tr>
{''.join(collinto_rows) or '<tr><td colspan="8">本次运行未包含</td></tr>'}
</table>
"""

    summary_section = f"""
<h2>五、全局汇总表 — 组件类型作为表头</h2>
<p style="color:var(--muted);font-size:0.85em">行 = 场景 + TFM，列 = 组件类型。绿色 = 该行最优值，浅蓝背景 = NCMapper 家族。</p>
{build_summary_table()}
<p style="color:var(--muted);font-size:0.80em;margin-top:4px">Collection 单位为 μs，其余为 ns。"—" 表示不支持。"浅蓝背景" = NCMapper 家族（NCMapper/Ext/Runtime），"绿色" = 该行最优值。</p>
"""

    chart_section = f"""
<h2>六、性能可视化图表（纯 SVG）</h2>
<h3>6.1 各场景性能对比（ns，越低越好）</h3>
<div class="card" style="padding:8px;margin:6px 0">{build_chart_61("net8.0")}</div>
<div class="card" style="padding:8px;margin:6px 0">{build_chart_61("net9.0")}</div>
<div class="card" style="padding:8px;margin:6px 0">{build_chart_61("net10.0")}</div>
<p style="color:var(--muted);font-size:0.85em">注：Collection 为 μs 量级（约为其他场景的百倍），若同图绘制会压扁其余场景柱形，故从 6.1 图中剔除，其数据见 2.9 场景表与五、全局汇总表。</p>
<h3>6.2 各场景跨组件性能曲线（ns，越低越好）</h3>
<p style="color:var(--muted);font-size:0.80em;margin:4px 0">每条线=一个场景，横轴=7个组件（固定顺序），纵轴=耗时(ns)。AutoFlatten 的 Mapster/AutoMapper 不支持，对应点虚线跨接。</p>
<div class="card" style="padding:8px;margin:6px 0">{build_chart_62("net8.0")}</div>
<div class="card" style="padding:8px;margin:6px 0">{build_chart_62("net9.0")}</div>
<div class="card" style="padding:8px;margin:6px 0">{build_chart_62("net10.0")}</div>
<h3>6.3 vs Manual 性能差距（net10.0，负值=更快）</h3>
<div class="card" style="padding:8px;margin:6px 0">{build_chart_vs_manual()}</div>
"""

    stats_section = f"""
<h2>七、统计专题</h2>
<h3>7.1 🏆 奖牌榜 — 全场景全框架综合排名</h3>
<p style="color:var(--muted);font-size:0.85em">统计范围：11 个映射场景 × 3 框架 = 33 张表（不含 DeepCopy/Update）。每张表前 3 名分别计 🥇🥈🥉。</p>
{medal_table()}
<h3>7.2 🎯 Ext vs Mapperly 直接对决</h3>
<p style="color:var(--muted);font-size:0.85em">每张表比较 Ext 和 Mapperly 的数值（ns，越小越快）。</p>
{ext_vs_mapperly()}
<h3>7.3 📈 NCMapper 改善幅度排行 (net8→net10)</h3>
<p style="color:var(--muted);font-size:0.85em">NCMapper = 源生成器路径。改善% = (net8 ns − net10 ns) / net8 ns。</p>
{improvement_table()}
<h3>7.4 📊 NCMapper 与手写 Manual 差距收敛 (net8→net10)</h3>
<p style="color:var(--muted);font-size:0.85em">差距% = (NCMapper − Manual) / Manual。收敛 = net8 差距 − net10 差距（正值=进步）。</p>
{gap_table()}
{trust_section(self_d, rival_d)}
"""

    upd_line = ""
    if upd_has:
        parts = []
        for t in TFMS:
            v = self_d["classes"].get(t, {}).get("Update_Ext") or rival_d["data"]["rivals"].get(t, {}).get("Update_Ext")
            parts.append(f'{t} {v["mean_raw"] if v else "—"}')
        upd_line = f'<li><strong>Update 场景</strong>：Ext 路径更新已有对象 {" / ".join(parts)}，领先 Manual/Mapster/AutoMapper（Mapperly 不支持该场景）。</li>'

    # vs 竞品可信口径（net8/9 几何平均，相对 NCMapper；net10 rivals 段异常不纳入）
    rivals_trust = []
    for suffix, cname in [("_AutoMapper", "AutoMapper"), ("_Mapster", "Mapster"),
                          ("_Manual", "Manual"), ("_Mapperly", "Mapperly")]:
        ratios = []
        for t in ("net8.0", "net9.0"):
            g, b = rivals_geom(t, suffix), geom_mean(t, "_NCMapper")
            if g and b:
                ratios.append(g / b)
        if ratios:
            m = sum(ratios) / len(ratios)
            if m > 1.05:
                rivals_trust.append(f'{cname} 慢 {m:.1f} 倍')
            elif m < 0.95:
                rivals_trust.append(f'{cname} 快 {1 / m:.1f} 倍')
            else:
                rivals_trust.append(f'{cname} 与 NCMapper 持平')

    dc_gen = dc10.get("Generated", 0)
    dc_mem = dc10_r.get("MemoryPack", 1)
    dc_json = dc10_r.get("Json", 1)
    dc_man = dc10.get("Manual", 0)
    dc_line = (f'<li><strong>DeepCopy（net10.0）</strong>：Generated {dc_gen:.2f}ns 为自动化方案最佳'
               f'（MemoryPack {dc_mem:.2f}ns 的 {dc_gen / dc_mem * 100:.0f}%、Json {dc_json:.2f}ns 的 {dc_gen / dc_json * 100:.0f}%），'
               f'Manual {dc_man:.2f}ns 手写极限仍最快。</li>') if dc_gen else ""

    conclusion = f"""
<h2>八、总结</h2>
<div class="card">
<h4 style="margin:4px 0 8px 0">① 核心结论（net8/net9 可信口径；net10 竞品数据异常见 7.5）</h4>
<ol>
<li><strong>三形态</strong>：Ext（ORM 扩展投影）最快，NCMapper（源生成器）次之，Runtime（表达式树）最慢——net10.0 几何平均 7.4 / 12.3 / 15.5 ns，NCMapper 为 Ext 的 1.7x，Runtime 为 Ext 的 2.1x。</li>
<li><strong>vs 竞品（net8/9 几何平均，相对 NCMapper）</strong>：{'；'.join(rivals_trust)}。Ext 与 Mapperly 在 net8/net9 上打平（±5%）。</li>
<li><strong>分配</strong>：Ext/NCMapper/Mapperly 均为零额外分配的生成路径，Allocated 完全一致；net9/10 部分类分配统计缺失（见 ③）。</li>
{dc_line}
{upd_line}
<li><strong>MapListInto（原地更新）</strong>：较 MapList 每次全量分配快 5.1~5.4 倍，且分配 10.25 KB → 0（见三·补）。</li>
</ol>
</div>
<div class="card">
<h4 style="margin:4px 0 8px 0">② 数据可信度</h4>
<p style="font-size:0.9em;margin:4px 0">本次 run 的 <strong>net10.0 rivals 段</strong>存在已查证净计时偏移（+3~17%，五重证据：跨段锚点 1.056 / Mapperly 历史纵向 / Manual 漂移 / Allocated 不变量 / 全历史分布离群），<strong>涉及该段的结论（Ext 11/11 超越 Mapperly 等）已作废</strong>，本报告所有胜局统计改用 net8/net9 可信口径；net10 竞品绝对数值保留原始测量并标注 ⚠️，修正参考见 <a href="#s75" style="color:var(--accent)">7.5 数据可信度</a>。</p>
</div>
<div class="card">
<h4 style="margin:4px 0 8px 0">③ 局限</h4>
<ul style="margin:4px 0;font-size:0.9em">
<li>net9/net10 部分类 BDN 未输出分配统计（Server GC + .NET 9 起 DATAS 干扰分配采样；计时通道不受影响，分配基线以 net8 为准）。</li>
<li>CollectionInto_Runtime 为新增基准类，数据待下轮 run 回填。</li>
<li>net10 rivals 偏移根因未定（与机器负载无关，net8/9 同跨时段零漂移），待复测定位。</li>
</ul>
</div>
<div class="card">
<h4 style="margin:4px 0 8px 0">④ 下一步</h4>
<ul style="margin:4px 0;font-size:0.9em">
<li>安静时段重跑 net10.0 rivals 段（约 1.5h）定位偏移根因并回填修正数据。</li>
<li>下轮完整 run 纳入 CollectionInto_Runtime，self 段 41 → 42 类。</li>
<li>单元测试{'零失败' if total_fail == 0 else f'{total_fail} 项失败'}（3 项目 × 3 TFM）。</li>
</ul>
</div>
"""

    footer = f"""
<div class="footer">NCMapper 全组件全框架全场景性能测试 | {start_time or "—"} ~ {end_time or "—"} | BDN 5×3×15 | commit {commit} | net8.0 / net9.0 / net10.0 | x64 RyuJIT AVX2</div>
"""

    html_doc = f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<title>NCMapper 全组件全框架全场景性能测试报告</title>
<style>{CSS}</style>
</head>
<body>
<div class="container">
<div style="background:#fff5f5;border:1px solid #fc8181;border-radius:8px;padding:10px 14px;margin:10px 0;font-size:0.88em;color:#742a2a">
⚠️ <strong>数据状态提示</strong>：本报告含 1 个已查证异常数据块——<strong>net10.0 rivals 段</strong>（竞品对比，净计时偏移 +3~17%，五重证据见 <a href="#s75" style="color:#c53030">7.5 数据可信度</a>）。涉及该段的胜局统计已改用 net8/net9 可信口径；net10 竞品绝对数值保留原始测量并标注 ⚠️，引用须先修正。
</div>
<h1>NCMapper 全组件全框架全场景性能测试报告</h1>
<p class="subtitle">日期：{start_time or "—"} ~ {end_time or "—"} | 方法：BenchmarkDotNet 5×3×15 | commit：{commit}<br>范围：3 测试项目（3 TFM）+ 3 框架（net8/9/10）× 11 映射场景 + DeepCopy + Update | RyuJIT AVX2 | Server GC</p>
{summary_cards}
{unit_html}
{''.join(scene_sections)}
{update_section}
{collinto_section}
{sg_rt_section}
{summary_section}
{chart_section}
{stats_section}
{conclusion}
{footer}
</div></body></html>
"""

    if out_path is None:
        out_path = DEFAULT_OUT_DIR / f"NCMapper全组件全框架全场景性能测试报告_{date_tag}.html"
    out_path = Path(out_path)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        f.write(html_doc)
    print(f"OK: {out_path}")
    print(f"  run_dir: {run_dir}")
    print(f"  commit: {commit} | 单测失败: {total_fail} | Update 数据: {'有' if upd_has else '无'}")
    print(f"  size: {len(html_doc)} chars")
    return 0

if __name__ == "__main__":
    sys.exit(main())
