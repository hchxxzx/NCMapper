"""提取后3 专属测试（net10.0_20260708_081526）3 次外重复（run1/run2/run3）的逐基准成绩。

输出：
1) 控制台打印 基准 | run1 | run2 | run3 | 中位数 | 极差% 的表格（35 行）
2) CSV 交付物：artifacts/benchmarks/baseline/self_only_3rounds.csv
"""
import os
import re
import statistics
import csv

ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))), "artifacts", "benchmarks", "baseline")
RUNDIR = os.path.join(ROOT, "net10.0_20260708_081526")
OUT_CSV = os.path.join(ROOT, "self_only_3rounds.csv")

UNIT_NS = {"ns": 1.0, "us": 1e3, "µs": 1e3, "ms": 1e6, "s": 1e9}


def parse_log(path):
    """返回 {基准名: mean_ns}。每个 Running 块取首个 | Run | 汇总行。"""
    cur = None
    rows = {}
    with open(path, encoding="utf-8", errors="ignore") as f:
        for line in f:
            m = re.match(r"\s*Running\s+(\S+)\.\.\.\s*$", line)
            if m:
                cur = m.group(1)
                continue
            s = line.strip()
            if cur and s.startswith("|"):
                parts = [p.strip() for p in s.split("|")]
                if "Run" in parts:
                    i = parts.index("Run")
                    if i + 1 < len(parts):
                        mm = re.match(r"([\d.]+)\s*(ns|us|µs|ms|s)", parts[i + 1])
                        if mm:
                            rows[cur] = float(mm.group(1)) * UNIT_NS[mm.group(2)]
                    cur = None
    return rows


def main():
    runs = {}
    for rn in ["run1", "run2", "run3"]:
        p = os.path.join(RUNDIR, rn, "self.log")
        runs[rn] = parse_log(p) if os.path.exists(p) else {}

    all_bench = sorted(set().union(*[set(v) for v in runs.values()]))

    table = []
    for b in all_bench:
        vals = [runs[rn].get(b) for rn in ["run1", "run2", "run3"]]
        valid = [v for v in vals if v is not None]
        if valid:
            med = statistics.median(valid)
            mn, mx = min(valid), max(valid)
            spread = (mx - mn) / med * 100.0 if med else 0.0
        else:
            med = mn = mx = spread = None
        table.append((b, vals[0], vals[1], vals[2], med, mn, mx, spread))

    # 控制台打印
    hdr = f"{'Benchmark':24s} {'run1':>12s} {'run2':>12s} {'run3':>12s} {'median':>12s} {'spread%':>8s}"
    print(hdr)
    print("-" * len(hdr))
    for b, r1, r2, r3, med, mn, mx, sp in table:
        def f(v):
            return f"{v:12.1f}" if v is not None else f"{'NA':>12s}"
        sp_s = f"{sp:7.1f}%" if sp is not None else "    NA"
        print(f"{b:24s} {f(r1)} {f(r2)} {f(r3)} {f(med)} {sp_s}")

    # 写 CSV
    with open(OUT_CSV, "w", newline="", encoding="utf-8-sig") as cf:
        w = csv.writer(cf)
        w.writerow(["Benchmark", "Run1_ns", "Run2_ns", "Run3_ns", "Median_ns", "Min_ns", "Max_ns", "Spread_pct"])
        for b, r1, r2, r3, med, mn, mx, sp in table:
            w.writerow([
                b,
                f"{r1:.4f}" if r1 is not None else "",
                f"{r2:.4f}" if r2 is not None else "",
                f"{r3:.4f}" if r3 is not None else "",
                f"{med:.4f}" if med is not None else "",
                f"{mn:.4f}" if mn is not None else "",
                f"{mx:.4f}" if mx is not None else "",
                f"{sp:.2f}" if sp is not None else "",
            ])
    print(f"\nCSV 已写出: {OUT_CSV}")
    print(f"基准总数: {len(table)}  |  全部有效: {sum(1 for t in table if t[4] is not None)}/35")


if __name__ == "__main__":
    main()
