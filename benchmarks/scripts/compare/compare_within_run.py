#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""同一 run 内：前7(full) 与 后3(self) 同名「自己组件」基准的绝对数值对比。
目的：量化「前7 vs 后3」的差异到底是结构性的，还是仅跨 run 噪声。
"""
import re
import os
import statistics

BASE = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))), "artifacts", "benchmarks", "baseline")
WHOLE = os.path.join(BASE, "net10.0_20260707_192721")   # 4d5a832 整轮（含前7+后3）

SELF_COMPS = ("NCMapper", "Ext", "Runtime", "Generated", "Manual")

def parse_log(path):
    """返回 {class: [median per run]} ，每个 run 取其 Run 汇总行的 Mean。"""
    if not os.path.exists(path):
        return {}
    runs = {}
    cur = None
    with open(path, encoding="utf-8", errors="ignore") as f:
        for line in f:
            m = re.match(r"\s{2}Running\s+([A-Za-z0-9_]+)\.\.\.\s*$", line)
            if m:
                cur = m.group(1)
                runs.setdefault(cur, [])
                continue
            if cur and line.strip().startswith("| Run"):
                # | Run | 30.74 ns | 1.234 ns | 3.456 ns | ...
                parts = [p.strip() for p in line.split("|")]
                # parts[0]='', [1]='Run', [2]='30.74 ns'...
                if len(parts) >= 3:
                    val = parts[2]
                    if "NA" in val:
                        continue
                    mv = re.search(r"([\d.]+)\s*(ns|us|µs|ms)", val)
                    if mv:
                        num = float(mv.group(1))
                        unit = mv.group(2)
                        if unit == "us":
                            num *= 1_000.0
                        elif unit == "µs":
                            num *= 1_000.0
                        elif unit == "ms":
                            num *= 1_000_000.0
                        runs[cur].append(num)
    # 每个类取跨 run 的中位数
    out = {}
    for cls, vals in runs.items():
        if vals:
            out[cls] = statistics.median(vals)
    return out

def is_self_comp(name):
    if name.startswith("DeepCopy_"):
        return name.split("_")[-1] in ("Generated", "Manual", "Runtime")
    return name.split("_")[-1] in ("NCMapper", "Ext", "Runtime")

full = {}
for r in (1, 2, 3):
    p = os.path.join(WHOLE, f"run{r}", "full.log")
    d = parse_log(p)
    for k, v in d.items():
        full.setdefault(k, []).append(v)
selfd = {}
for r in (1, 2, 3):
    p = os.path.join(WHOLE, f"run{r}", "self.log")
    d = parse_log(p)
    for k, v in d.items():
        selfd.setdefault(k, []).append(v)

# 同名「自己组件」类
common = sorted(set(full) & set(selfd))
common_self = [c for c in common if is_self_comp(c)]

print(f"前7 类数={len(full)}  后3 类数={len(selfd)}")
print(f"同名类(全部)={len(common)}  其中自己组件类={len(common_self)}")
print()

# 仅看「自己组件」：前7 vs 后3
rows = []
for c in common_self:
    fmed = statistics.median(full[c])
    smed = statistics.median(selfd[c])
    if fmed > 0:
        diff = (smed - fmed) / fmed * 100.0
    else:
        diff = 0.0
    rows.append((c, fmed, smed, diff))

print(f"{'Benchmark':<26}{'前7(ns)':>12}{'后3(ns)':>12}{'diff%':>10}")
print("-" * 62)
full_faster = self_faster = 0
absdiffs = []
for c, fv, sv, d in sorted(rows, key=lambda x: abs(x[3]), reverse=True):
    print(f"{c:<26}{fv:>12.2f}{sv:>12.2f}{d:>9.1f}%")
    if d < 0:
        full_faster += 1
    else:
        self_faster += 1
    absdiffs.append(abs(d))

print("-" * 62)
print(f"前7更快={full_faster}  后3更快={self_faster}")
if absdiffs:
    print(f"|diff%| 中位数={statistics.median(absdiffs):.1f}%  最大={max(absdiffs):.1f}%  均值={statistics.mean(absdiffs):.1f}%")
print()
print("结论：若 |diff%| 普遍 < 5%~10% 且方向对称，则「前7 vs 后3 自己组件」")
print("      在同一 run 内基本一致，差异来自 run-to-run 噪声而非结构。")
