#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cold-bench-threshold-check.py — per-iteration 冷路径阈值门

对账对象：benchmarks/baselines/MANIFEST.md "per-iteration 冷路径口径（2026-09-23）"节的基线表
（单一事实源 = MANIFEST，本脚本解析该表，不内嵌副本——防双源漂移）。

判定判据（MANIFEST 节口径）：
  1. 主判据 Allocated：results csv 值与 MANIFEST 基线**字符串精确相等**
     （精度口径：BDN csv 的 Allocated 为格式化串，粒度 0.01 MB ≈ 10 KB——
      "逐字节相等"判据在 csv 载体上退化为"显示粒度内相等"，该精度缝隙已在
      MANIFEST 节登记；需要字节级判定的场合须改用 BDN json/
      diagnoser 原始输出，见回流项）。
  2. 耗时：仅"两轮偏差 <8%"的 GATE 夹具判定（Mean ≤ MANIFEST 阈值 = 上限校准
     max(R1,R2)×1.10，从宽首阈）；NOGATE 夹具（DiagScaleCold / DeepCopyGraphSharedCold）
     耗时仅打印不判定。
  3. 缺失夹具 / MANIFEST 表解析失败 / 无法定位 results → 非零退出（fail-closed）。

用法：
  python3 scripts/cold-bench-threshold-check.py --results <BDN results 目录>
  python3 scripts/cold-bench-threshold-check.py --results <dir> --inject MapListShapeCold
      # 双边界自证：将指定夹具的实测 Allocated/Mean 合成 +30%，期望判定告警（退出码非零）
      # ——证明"分配/耗时劣化必被抓"的判定逻辑对称性（端到端注入=改夹具代码重跑 BDN，
      #    证据强度更高）。

退出码：0 = 全部通过；1 = 存在告警/错误。
"""
import argparse
import csv
import glob
import os
import re
import sys

MANIFEST = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "benchmarks", "baselines", "MANIFEST.md"
)

# 预期夹具集合（与 MANIFEST 节对位表一致；缺一即 fail-closed）
EXPECTED = [
    "SourceGenDriverColdBench",
    "MapListShapeCold",
    "MutualPairs3Cold",
    "WidePropertiesCold",
    "RepeatedDeclarations3Cold",
    "SelfRecursiveInlineCold",
    "FluentChainSubclassCold",
    "DiagScaleCold",
    "DeepCopyGraphSharedCold",
]


def parse_manifest(path):
    """解析 MANIFEST 冷路径基线表 → {fixture: {"alloc": "6.88 MB", "time_ms": 58.0 or None}}"""
    if not os.path.exists(path):
        raise SystemExit(f"FAIL: MANIFEST not found: {path}")
    text = open(path, encoding="utf-8").read()
    # 定位冷路径节（以节标题为锚，防误读热基线表）
    m = re.search(r"## per-iteration 冷路径口径.*?\n(.*?)(?=\n## |\Z)", text, re.S)
    if not m:
        raise SystemExit("FAIL: MANIFEST cold-path section not found")
    section = m.group(1)
    table = {}
    for line in section.splitlines():
        if not line.strip().startswith("|"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) < 7 or cells[0] in ("夹具", "---", ""):
            continue
        fixture = cells[0]
        if fixture not in EXPECTED:
            continue
        alloc_cell = cells[4]  # "6.88 MB = 6.88 MB"
        parts = [p.strip() for p in alloc_cell.split("=")]
        if len(parts) != 2 or parts[0] != parts[1]:
            raise SystemExit(f"FAIL: MANIFEST Allocated 两轮不等或格式异常: {fixture}: {alloc_cell!r}")
        time_cell = cells[6]
        time_match = re.search(r"([\d.]+)\s*ms", time_cell)
        table[fixture] = {
            "alloc": parts[0],
            "time_ms": float(time_match.group(1)) if time_match else None,
        }
    missing = [f for f in EXPECTED if f not in table]
    if missing:
        raise SystemExit(f"FAIL: MANIFEST 表缺夹具: {missing}")
    return table


def load_results(results_dir):
    """读 BDN results 目录的 *-report.csv → {fixture: {"alloc": "… MB", "mean_ms": float}}"""
    out = {}
    for f in glob.glob(os.path.join(results_dir, "*-report.csv")):
        base = os.path.basename(f)
        m = re.match(r"NCMapper\.Benchmarks\.(.+?)-report\.csv", base)
        if not m:
            continue
        fixture = m.group(1)
        if fixture not in EXPECTED:
            continue
        rows = list(csv.DictReader(open(f, encoding="utf-8-sig")))
        for row in rows:
            if not row.get("Method", "").endswith("Cold"):
                continue
            alloc = (row.get("Allocated") or "").strip()
            mean_raw = row.get("Mean") or ""
            mm = re.search(r"([\d.]+)\s*ms", mean_raw)
            out[fixture] = {"alloc": alloc, "mean_ms": float(mm.group(1)) if mm else None}
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--results", required=True, help="BDN results 目录（含 *-report.csv）")
    ap.add_argument("--inject", default=None, help="双边界自证：指定夹具名，实测值合成 +30% 后判定（期望告警）")
    args = ap.parse_args()

    baseline = parse_manifest(MANIFEST)
    results = load_results(args.results)
    if not results:
        raise SystemExit("FAIL: results 目录无 Cold 夹具 report——fail-closed")

    missing = [f for f in EXPECTED if f not in results]
    if missing:
        raise SystemExit(f"FAIL: results 缺夹具 report: {missing}")

    alarms = []
    inject_hit = False
    print(f"{'Fixture':34s} {'Allocated':>16s} {'Alloc判':>6s} {'Mean ms':>10s} {'阈值':>9s} {'耗时判':>8s}")
    for fixture in EXPECTED:
        r = results[fixture]
        b = baseline[fixture]
        alloc = r["alloc"]
        if args.inject and fixture == args.inject:
            # 合成注入：Allocated +30%（MB 值 ×1.3，保持串格式）+ Mean ×1.3
            mnum = re.search(r"([\d.]+)", alloc)
            if mnum:
                alloc = f"{float(mnum.group(1)) * 1.3:.2f} MB"
            if r["mean_ms"] is not None:
                r["mean_ms"] = r["mean_ms"] * 1.3
        alloc_ok = alloc == b["alloc"]
        alloc_tag = "OK" if alloc_ok else "ALARM"
        if not alloc_ok:
            alarms.append(f"{fixture}: Allocated {alloc} != 基线 {b['alloc']}")
        if args.inject and fixture == args.inject and not alloc_ok:
            inject_hit = True

        mean = r["mean_ms"]
        if b["time_ms"] is None:
            time_tag = "登记"  # NOGATE：不判定
            time_ok = True
            mean_disp = f"{mean:.3f}" if mean is not None else "?"
            thr_disp = "-"
        else:
            thr = b["time_ms"]
            time_ok = mean is not None and mean <= thr
            time_tag = "OK" if time_ok else "ALARM"
            if not time_ok:
                alarms.append(f"{fixture}: Mean {mean:.3f} ms > 阈值 {thr} ms")
            if args.inject and fixture == args.inject and not time_ok:
                inject_hit = True
            mean_disp = f"{mean:.3f}" if mean is not None else "?"
            thr_disp = f"{thr:.1f}"
        print(f"{fixture:34s} {alloc:>16s} {alloc_tag:>6s} {mean_disp:>10s} {thr_disp:>9s} {time_tag:>8s}")

    if args.inject:
        if inject_hit:
            print(f"\n[SELF-TEST] 注入 {args.inject} 后判定已触发告警——双边界（拦截侧）成立")
            # 注入模式：退出码 0 = 自证通过（触发即符合期望）；告警清单同时打印
            sys.exit(0)
        else:
            print(f"\n[SELF-TEST] 注入 {args.inject} 后未触发任何告警——判定逻辑失效！")
            sys.exit(1)

    print()
    if alarms:
        print("存在告警：")
        for a in alarms:
            print("  - " + a)
        sys.exit(1)
    print("冷路径阈值门全部通过（Allocated 精确相等 × 9 + 耗时阈值 × 7 GATE；2 NOGATE 仅登记）")


if __name__ == "__main__":
    main()
