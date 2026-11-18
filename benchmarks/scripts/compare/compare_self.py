#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
NCMapper SELF（3 变体）基线对比工具
------------------------------------------------------------
对比两份基线测试中 baseline_self 阶段的中位数（已统一量纲为 ns）：
  - dir1: 整轮测试（commit A，含 前7+后3）
  - dir2: 后3 专属测试（commit B，仅 SELF）
解析两个目录下 run1/2/3 的 self.log，提取每个类首个 "Mean = X unit"，
µs/μs 归一化为 ns，按类取三轮中位数，再做相对差异对比。
输出 CSV 到 artifacts/benchmarks/baseline/ 并打印分析。
"""
import os
import re
import glob
import csv
import statistics

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
DIR1 = os.path.join(REPO, "artifacts/benchmarks/baseline/net10.0_20260707_192721")  # 整轮 4d5a832
DIR2 = os.path.join(REPO, "artifacts/benchmarks/baseline/net10.0_20260708_081526")  # 后3 d07057e
COMMIT1 = "4d5a832"  # 整轮
COMMIT2 = "d07057e"  # 后3

RUN_RE = re.compile(r'^\s*Running\s+([A-Za-z0-9_]+)\.\.\.\s*$')
MEAN_RE = re.compile(r'Mean\s*=\s*([0-9.]+)\s*([a-zA-Zµμ]+)')


def extract_dir(d: str):
    """返回 (median_dict, log_count)。median_dict: class -> ns 中位数。"""
    vals = {}
    logs = sorted(glob.glob(os.path.join(d, "run[1-3]", "self.log")))
    for log in logs:
        cur = None
        captured = False
        with open(log, encoding="utf-8", errors="replace") as f:
            for line in f:
                m = RUN_RE.match(line)
                if m:
                    cur = m.group(1)
                    captured = False
                    continue
                if cur and not captured:
                    mm = MEAN_RE.search(line)
                    if mm:
                        v = float(mm.group(1))
                        u = mm.group(2).lower()
                        if u in ("us", "µs", "μs"):
                            v *= 1000.0
                        vals.setdefault(cur, []).append(v)
                        captured = True
    med = {c: statistics.median(lst) for c, lst in vals.items() if lst}
    return med, len(logs)


def main():
    med1, n1 = extract_dir(DIR1)
    med2, n2 = extract_dir(DIR2)

    print(f"整轮({COMMIT1}) SELF 类数: {len(med1)}  (来自 {n1} 个 self.log)")
    print(f"后3 ({COMMIT2}) SELF 类数: {len(med2)}  (来自 {n2} 个 self.log)")
    print()

    rows = []
    allc = sorted(set(med1) | set(med2))
    for c in allc:
        a = med1.get(c)
        b = med2.get(c)
        if a is None:
            diff = None
            status = "仅后3有(整轮缺失)"
        elif b is None:
            diff = None
            status = "仅整轮有(后3缺失)"
        else:
            diff = (b - a) / a * 100.0
            if diff > 5.0:
                status = "回归(>5%)"
            elif diff < -5.0:
                status = "改善(<-5%)"
            else:
                status = "基本持平"
        rows.append((c, a, b, diff, status))

    # 排序：有 diff 的按 |diff| 降序，缺失类排后
    rows.sort(key=lambda r: (r[3] is None, -(abs(r[3]) if r[3] is not None else 0)))

    header = f"{'class':<22}{'整轮_ns':>16}{'后3_ns':>16}{'diff%':>10}  status"
    print(header)
    print("-" * len(header))
    for c, a, b, d, s in rows:
        a_s = f"{a:.3f}" if a is not None else "-"
        b_s = f"{b:.3f}" if b is not None else "-"
        d_s = f"{d:+.1f}" if d is not None else "-"
        print(f"{c:<22}{a_s:>16}{b_s:>16}{d_s:>10}  {s}")

    # 统计
    reg = [r for r in rows if r[4].startswith("回归")]
    imp = [r for r in rows if r[4].startswith("改善")]
    flat = [r for r in rows if r[4] == "基本持平"]
    miss = [r for r in rows if "缺失" in r[4]]
    print()
    print(f"回归(>5%): {len(reg)} | 改善(<-5%): {len(imp)} | 基本持平: {len(flat)} | 缺失/不对称: {len(miss)}")

    out = os.path.join(REPO, "artifacts/benchmarks/baseline",
                       f"self_compare_{COMMIT1}_vs_{COMMIT2}.csv")
    with open(out, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["class", f"median_ns_{COMMIT1}", f"median_ns_{COMMIT2}",
                    "diff_pct", "status"])
        for c, a, b, d, s in rows:
            w.writerow([c,
                        f"{a:.4f}" if a is not None else "",
                        f"{b:.4f}" if b is not None else "",
                        f"{d:.2f}" if d is not None else "",
                        s])
    print("\nCSV ->", out)


if __name__ == "__main__":
    main()
