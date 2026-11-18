#!/usr/bin/env python3
"""从 BenchmarkDotNet 日志中提取 Mean，计算中位数（去掉最高最低，取中间值）"""

import sys
import os
import re
import glob
import statistics

def extract_mean(filepath):
    """从 BDN 日志提取 Mean (ns)"""
    with open(filepath, 'r', encoding='utf-8', errors='replace') as f:
        content = f.read()
    match = re.search(r'Mean\s*=\s*([\d.]+)\s*ns', content)
    if match:
        return float(match.group(1))
    # 尝试表格格式
    match = re.search(r'\|\s*\d[\d.]*\s+ns', content)
    if match:
        return float(match.group().strip('| ns'))
    return None

def main():
    if len(sys.argv) < 2:
        print("用法: python3 extract_medians.py <results_dir>")
        sys.exit(1)

    results_dir = sys.argv[1]
    if not os.path.isdir(results_dir):
        print(f"目录不存在: {results_dir}")
        sys.exit(1)

    scenarios = ["flat", "namediff", "nested", "collection", "bidir", "action"]
    components = ["ncmapper", "mapperly"]
    # bidir 有两个子方向
    bidir_sub = {
        "ncmapper": ["BidirED_NCMapper", "BidirDE_NCMapper"],
        "mapperly": ["BidirED_Mapperly", "BidirDE_Mapperly"],
    }

    print("=" * 80)
    print(f"  NCMapper vs Mapperly — Median Results (5 rounds, drop min/max)")
    print("=" * 80)
    print()

    all_results = []

    for scenario in scenarios:
        for component in components:
            if scenario == "bidir":
                for sub_cls in bidir_sub[component]:
                    pattern = os.path.join(results_dir, f"bidir_{component}_round*_{sub_cls}.log")
                    files = sorted(glob.glob(pattern))
                    if not files:
                        continue
                    means = [extract_mean(f) for f in files]
                    means = [m for m in means if m is not None]
                    if len(means) < 3:
                        print(f"  {sub_cls:30s}  insufficient data ({len(means)} rounds)")
                        continue
                    # 去掉最高最低，取中间值的中位数
                    means_sorted = sorted(means)
                    trimmed = means_sorted[1:-1]  # 去掉 min 和 max
                    median = statistics.median(trimmed)
                    direction = "E→D" if "ED" in sub_cls else "D→E"
                    label = f"Bidir_{direction}_{component}"
                    print(f"  {label:30s}  median={median:8.2f} ns   all={[f'{m:.2f}' for m in means]}")
                    all_results.append((label, median))
            else:
                # 获取对应的 Benchmark 类名
                cls_map = {
                    ("flat", "ncmapper"): "Flat_NCMapper",
                    ("flat", "mapperly"): "Flat_Mapperly",
                    ("namediff", "ncmapper"): "NameDiff_NCMapper",
                    ("namediff", "mapperly"): "NameDiff_Mapperly",
                    ("nested", "ncmapper"): "Nested_NCMapper",
                    ("nested", "mapperly"): "Nested_Mapperly",
                    ("collection", "ncmapper"): "Collection_NCMapper",
                    ("collection", "mapperly"): "Collection_Mapperly",
                    ("action", "ncmapper"): "Action_NCMapper",
                    ("action", "mapperly"): "Action_Mapperly",
                }
                cls_name = cls_map.get((scenario, component))
                if not cls_name:
                    continue

                pattern = os.path.join(results_dir, f"{scenario}_{component}_round*_{cls_name}.log")
                files = sorted(glob.glob(pattern))
                if not files:
                    continue
                means = [extract_mean(f) for f in files]
                means = [m for m in means if m is not None]
                if len(means) < 3:
                    print(f"  {scenario}_{component:30s}  insufficient data ({len(means)} rounds)")
                    continue
                means_sorted = sorted(means)
                trimmed = means_sorted[1:-1]
                median = statistics.median(trimmed)
                label = f"{scenario}_{component}"
                print(f"  {label:30s}  median={median:8.2f} ns   all={[f'{m:.2f}' for m in means]}")
                all_results.append((label, median))

    # 对比汇总
    print()
    print("=" * 80)
    print("  vs Comparison Summary")
    print("=" * 80)
    print(f"  {'Scenario':<25} {'NCMapper':>12} {'Mapperly':>12} {'Diff':>10} {'Ratio':>8}")
    print(f"  {'-'*25} {'-'*12} {'-'*12} {'-'*10} {'-'*8}")

    pairs = [
        ("flat", "Flat_NCMapper", "Flat_Mapperly"),
        ("namediff", "NameDiff_NCMapper", "NameDiff_Mapperly"),
        ("nested", "Nested_NCMapper", "Nested_Mapperly"),
        ("collection", "Collection_NCMapper", "Collection_Mapperly"),
        ("bidir E→D", "Bidir_E→D_ncmapper", "Bidir_E→D_mapperly"),
        ("bidir D→E", "Bidir_D→E_ncmapper", "Bidir_D→E_mapperly"),
        ("action", "Action_NCMapper", "Action_Mapperly"),
    ]

    result_dict = {label: val for label, val in all_results}

    for display, nc_key, mp_key in pairs:
        # 模糊匹配
        nc_val = None
        mp_val = None
        for label, val in all_results:
            if nc_key.lower().replace("_", "") in label.lower().replace("_", ""):
                nc_val = val
            if mp_key.lower().replace("_", "") in label.lower().replace("_", ""):
                mp_val = val

        if nc_val and mp_val:
            diff = nc_val - mp_val
            ratio = nc_val / mp_val if mp_val > 0 else 0
            winner = "🥇NCMapper" if nc_val < mp_val else "Mapperly"
            print(f"  {display:<25} {nc_val:>10.2f}ns {mp_val:>10.2f}ns {diff:>+8.2f}ns {ratio:>7.2f}x  {winner}")
        else:
            print(f"  {display:<25} {'N/A':>12} {'N/A':>12}")

    print()
    print("=" * 80)


if __name__ == "__main__":
    main()
