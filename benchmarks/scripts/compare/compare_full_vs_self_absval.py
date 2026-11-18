#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
整轮测试内部：FULL(前7, 7变体) vs SELF(后3, 3变体) 同名变体的绝对数值对比。
从 run1/2/3 的 full.log / self.log 提取首个 Mean（µs->ns 归一化），每类取中位数。
旨在回答「前7 与 后3 的绝对数值谁更快」。
"""
import os
import re
import glob
import statistics

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
D = os.path.join(REPO, "artifacts/benchmarks/baseline/net10.0_20260707_192721")

RUN_RE = re.compile(r'^\s*Running\s+([A-Za-z0-9_]+)\.\.\.\s*$')
MEAN_RE = re.compile(r'Mean\s*=\s*([0-9.]+)\s*([a-zA-Zµμ]+)')


def extract(stage: str):
    vals = {}
    for log in sorted(glob.glob(os.path.join(D, f"run[1-3]/{stage}.log"))):
        cur = None
        cap = False
        with open(log, encoding="utf-8", errors="replace") as f:
            for line in f:
                m = RUN_RE.match(line)
                if m:
                    cur = m.group(1)
                    cap = False
                    continue
                if cur and not cap:
                    mm = MEAN_RE.search(line)
                    if mm:
                        v = float(mm.group(1))
                        u = mm.group(2).lower()
                        if u in ("us", "µs", "μs"):
                            v *= 1000.0
                        vals.setdefault(cur, []).append(v)
                        cap = True
    return {c: statistics.median(l) for c, l in vals.items() if l}


def main():
    full = extract("full")
    self = extract("self")
    common = sorted(set(full) & set(self))
    full_faster = [c for c in common if full[c] < self[c]]
    self_faster = [c for c in common if self[c] < full[c]]

    print(f"FULL(前7) 变体数 = {len(full)}")
    print(f"SELF(后3) 变体数 = {len(self)}")
    print(f"同名变体（可直比）= {len(common)}")
    print(f"  其中 FULL 更快 = {len(full_faster)}")
    print(f"  其中 SELF 更快 = {len(self_faster)}")
    print()
    print("--- 同名变体绝对数值（ns，按 FULL-SELF 差值升序 Top8，即 SELF 快最多）---")
    for c in sorted(common, key=lambda x: full[x] - self[x])[:8]:
        print(f"  {c:<22} FULL={full[c]:>10.3f}  SELF={self[c]:>10.3f}  SELF/FULL={self[c]/full[c]*100:5.1f}%")
    print("--- 同名变体绝对数值（ns，按 FULL-SELF 差值降序 Bottom8，即 FULL 快最多）---")
    for c in sorted(common, key=lambda x: full[x] - self[x])[-8:]:
        print(f"  {c:<22} FULL={full[c]:>10.3f}  SELF={self[c]:>10.3f}  SELF/FULL={self[c]/full[c]*100:5.1f}%")


if __name__ == "__main__":
    main()
