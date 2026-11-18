# -*- coding: utf-8 -*-
"""竞品测试(rivals_only) vs 前7整轮(full 7变体) 的竞品变体子集对比。
说明: 这是跨协议+跨run对比(前7=7变体同场, 竞品=竞品单独冷启动), 仅看量级方向, 不可定量。
"""
import csv, statistics, os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
BASE = os.path.join(ROOT, "artifacts", "benchmarks", "baseline")
RIVALS = os.path.join(BASE, "net10.0_20260708_132439", "rivals_median.csv")
FULL7  = os.path.join(BASE, "net10.0_20260707_192721", "full_median.csv")
OUT    = os.path.join(BASE, "net10.0_20260708_132439", "rivals_vs_full7.csv")

def load(path):
    d = {}
    with open(path) as f:
        for row in csv.DictReader(f):
            d[row['class']] = float(row['median_mean_ns'])
    return d

riv = load(RIVALS)
full = load(FULL7)
common = [c for c in riv if c in full]
rows = [(c, riv[c], full[c], riv[c]/full[c]) for c in sorted(common)]

with open(OUT, 'w', newline='') as f:
    w = csv.writer(f)
    w.writerow(['class','rivals_ns','full7_ns','ratio_rivals_over_full7'])
    for c, r, fl, ratio in rows:
        w.writerow([c, f"{r:.4f}", f"{fl:.4f}", f"{ratio:.4f}"])

ratios = [rt for *_, rt in rows]
riv_mean = statistics.mean([r for _, r, _, _ in rows])
full_mean = statistics.mean([fl for _, _, fl, _ in rows])
med = statistics.median(ratios)
print(f"竞品类数(交集): {len(rows)}")
print(f"竞品测试均值(ns): {riv_mean:.3f}")
print(f"前7竞品子集均值(ns): {full_mean:.3f}")
print(f"整体均值比 ratio(竞品/前7): {riv_mean/full_mean:.4f}")
print(f"比值中位数: {med:.4f}")
print(f"比值 min~max: {min(ratios):.4f} ~ {max(ratios):.4f}")
print(f"竞品更快(ratio<1): {sum(1 for x in ratios if x<1)} / {len(ratios)}")
print(f"竞品更慢(ratio>1): {sum(1 for x in ratios if x>1)} / {len(ratios)}")
print("\n异常项 (ratio 偏离中位数>30% 或 >3/<0.5):")
for c, r, fl, ratio in rows:
    if abs(ratio-med) > 0.3*med or ratio > 3 or ratio < 0.5:
        print(f"  {c}: 竞品={r:.3f} 前7={fl:.3f} ratio={ratio:.4f}")
