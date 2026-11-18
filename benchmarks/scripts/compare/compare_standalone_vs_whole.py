"""对比 standalone-self（后3 专属测试 d07057e）与各同名基准的 whole-full（前7 整轮 4d5a832）绝对数值。

用法：python benchmarks/scripts/compare/compare_standalone_vs_whole.py
每个基准在 run1/2/3 中取中位数，再跨同名基准比较 standalone-self 与 whole-full。
"""
import os
import re
import statistics

ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))), "artifacts", "benchmarks", "baseline")
WHOLE = os.path.join(ROOT, "net10.0_20260707_192721")  # 前7 整轮（含 full + self）
STAND = os.path.join(ROOT, "net10.0_20260708_081526")  # 后3 专属（仅 self）

UNIT_NS = {"ns": 1.0, "us": 1e3, "µs": 1e3, "ms": 1e6, "s": 1e9}


def parse_log(path):
    """返回 {基准名: [各 Run 行的 mean_ns, ...]}。每个 Running 块只取首个 | Run | 汇总行。"""
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
                            val = float(mm.group(1)) * UNIT_NS[mm.group(2)]
                            rows.setdefault(cur, []).append(val)
                    cur = None  # 每个 Running 块只取首个 Run 汇总
    return rows


def median_per_name(rundir, logfile):
    """对 run1/2/3 的同一基准名取中位数。"""
    allv = {}
    for rn in ["run1", "run2", "run3"]:
        p = os.path.join(rundir, rn, logfile)
        if not os.path.exists(p):
            continue
        rows = parse_log(p)
        for k, v in rows.items():
            allv.setdefault(k, []).extend(v)
    return {k: statistics.median(v) for k, v in allv.items() if v}


def main():
    whole_full = median_per_name(WHOLE, "full.log")
    stand_self = median_per_name(STAND, "self.log")

    common = sorted(set(whole_full) & set(stand_self))
    print(f"whole-full(前7) 基准数: {len(whole_full)}")
    print(f"standalone-self(后3) 基准数: {len(stand_self)}")
    print(f"同名可直比基准数: {len(common)}")

    faster = slower = 0
    diffs = []
    for k in common:
        w = whole_full[k]
        s = stand_self[k]
        d = (s - w) / w * 100.0
        diffs.append((k, w, s, d))
        if d < 0:
            faster += 1
        else:
            slower += 1

    print(f"\nstandalone-self 更快(diff<0): {faster}")
    print(f"standalone-self 更慢(diff>0): {slower}")
    if diffs:
        ds = [d for _, _, _, d in diffs]
        print(f"diff% 中位数: {statistics.median(ds):+.1f}%")
        print(f"diff% 范围: {min(ds):+.1f}% ~ {max(ds):+.1f}%")

    print("\n样例（基准名 | whole-full_ns | standalone-self_ns | diff%）:")
    for k, w, s, d in sorted(diffs, key=lambda x: x[3])[:14]:
        print(f"  {k:24s} {w:12.1f}  {s:12.1f}  {d:+6.1f}%")
    print("  ...")
    for k, w, s, d in sorted(diffs, key=lambda x: x[3])[-6:]:
        print(f"  {k:24s} {w:12.1f}  {s:12.1f}  {d:+6.1f}%")


if __name__ == "__main__":
    main()
