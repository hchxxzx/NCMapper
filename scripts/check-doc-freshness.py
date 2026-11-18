#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check-doc-freshness.py -- verify that hand-maintained inventory documents are not stale
relative to the artifacts they describe.

WHY THIS EXISTS
  Inventory pages (architecture map, sample index, fluent/attribute indexes) are the *denominator*
  of every coverage claim made in this repo. If one of them silently falls behind the code, every
  downstream count, coverage percentage and "is it complete" answer is wrong -- yet nothing in the
  existing gate matrix goes red, because those scripts only check *content* (is X mentioned /
  is the link valid), never *freshness* (is this page still current).

  Real instance (2026-10-29): `architecture-map.md` declared `更新时间: 2026-09-30` while its last
  content commit was 2026-10-23. Any reader -- human or agent -- took a 23-day-stale date at face
  value and started questioning whether the whole page was obsolete.

JUDGEMENT (three checks, all read-only, none of them proves correctness)
  L1 timeline (warn)      last commit touching the doc vs last commit touching its guard artifact.
                          Doc older than guard  => the guard changed after the doc was written, so
                          the doc may not reflect it. Prints the day gap.
  L2 front-matter (warn)  the `更新时间:` / `更新时间：` value declared in the doc vs the doc's own
                          last commit date. A mismatch means the declared date is stale metadata --
                          cheap to fix, and it is what misleads readers first.
  L3 snapshot coverage    doc says it is keyed on a public-API snapshot. Every public type in the
    (architecture only)   snapshot must be mentioned somewhere in the doc. Compares AFTER stripping
                          the `Attribute` suffix, because docs write `[MapFrom]` while the snapshot
                          writes `MapFromAttribute`; comparing raw names yields false "missing" hits.

EXIT CODE
  Default (advisory) : always 0. Intended for periodic sweeps, not for blocking CI.
  --strict           : 1 if any WARN was emitted. Opt in only when the repo is clean and the
                       freshness contract is meant to be enforced.

SCOPE / NON-GOALS
  - Does NOT judge whether the doc's *content* is correct (wrong link target, claimed-but-absent
    section). Those are content defects; see the coverage scripts and the 2026-10-29 matrix.
  - Does NOT parse every date-like string; only the declared `更新时间` key is read.
  - Guard artifacts may be files or directories; a directory is resolved to its recursively
    newest-tracked file.

USAGE
  python scripts/check-doc-freshness.py            # advisory sweep
  python scripts/check-doc-freshness.py --strict   # fail on any WARN
  python scripts/check-doc-freshness.py -v         # show OK lines too
"""
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# doc -> guard artifacts that the doc is supposed to track, plus optional coverage mode
TARGETS = [
    {
        "doc": "docs/zh-CN/reference/architecture-map.md",
        "guards": [
            "test/NCMapper.Tests/PublicApi.NCMapper.approved.txt",
            "test/NCMapper.Tests/PublicApi.NCMapper.Dapper.approved.txt",
            "test/NCMapper.Tests/PublicApi.NCMapper.EFCore.approved.txt",
            "test/NCMapper.Tests/PublicApi.NCMapper.FreeSql.approved.txt",
        ],
        "coverage": "publicapi",
    },
    {
        "doc": "docs/zh-CN/reference/samples-index.md",
        "guards": ["samples/NCMapper.Samples/ScenarioRegistry.cs"],
    },
    {
        "doc": "docs/zh-CN/reference/fluent-index.md",
        "guards": ["src/NCMapper/FluentMappingConfig.cs"],
    },
    {
        "doc": "docs/zh-CN/reference/attributes-index.md",
        # 特性类直接位于 src/NCMapper，无 Attributes 子目录
        "guards": ["src/NCMapper/*Attribute.cs"],
    },
    {
        "doc": "docs/zh-CN/diagnostics/rules.md",
        # 必须是诊断码的定义与登记文件；用 src 整目录会把 packages.lock.json 这类
        # 构建产物变更当成"基准更新"，产生假阳性（首版实测踩到）
        "guards": [
            "src/NCMapper.SourceGen/DiagnosticDescriptors.Fluent.cs",
            "src/NCMapper.SourceGen/AnalyzerReleases.Shipped.md",
            "src/NCMapper.SourceGen/AnalyzerReleases.Unshipped.md",
        ],
    },
]

DATE_KEYS = ("更新时间", "更新時間", "updated")


def git_last_commit_date(path):
    """Last commit date (YYYY-MM-DD) touching `path`; None if untracked/unknown."""
    try:
        out = subprocess.run(
            ["git", "log", "-1", "--format=%ad", "--date=short", "--", path],
            cwd=ROOT, capture_output=True, text=True, timeout=30,
        ).stdout.strip()
        return out or None
    except Exception:
        return None


def resolve_guard(path):
    """Resolve a guard spec to (representative_path, last_commit_date).

    Supports three forms:
      - plain file            -> its own date
      - directory             -> newest-tracked file inside (bin/obj/.git excluded)
      - glob (contains '*')   -> newest of all matches
    """
    if "*" in path:
        import glob as _glob
        matches = [p for p in _glob.glob(os.path.join(ROOT, path)) if os.path.isfile(p)]
        if not matches:
            return path, None
        best_path, best_date = None, None
        for p in matches:
            rel = os.path.relpath(p, ROOT).replace("\\", "/")
            d = git_last_commit_date(rel)
            if d and (best_date is None or d > best_date):
                best_path, best_date = rel, d
        return best_path, best_date

    abs_path = os.path.join(ROOT, path)
    if os.path.isdir(abs_path):
        newest, newest_date = None, None
        for dirpath, dirnames, filenames in os.walk(abs_path):
            dirnames[:] = [d for d in dirnames if d not in ("bin", "obj", ".git")]
            for fn in filenames:
                rel = os.path.relpath(os.path.join(dirpath, fn), ROOT).replace("\\", "/")
                d = git_last_commit_date(rel)
                if d and (newest_date is None or d > newest_date):
                    newest, newest_date = rel, d
        return newest, newest_date
    return path, git_last_commit_date(path)


def read_declared_date(abs_path):
    """Extract the doc's own declared `更新时间` value from front matter."""
    try:
        with open(abs_path, encoding="utf-8") as f:
            head = [next(f) for _ in range(30)]
    except Exception:
        return None
    for ln in head:
        m = re.search(r"(%s)\s*[:：]\s*([0-9]{4}-[0-9]{2}-[0-9]{2})" % "|".join(DATE_KEYS), ln)
        if m:
            return m.group(2)
    return None


def publicapi_types():
    """Public type names across all four API snapshots."""
    names = set()
    for fn in ("PublicApi.NCMapper.approved.txt", "PublicApi.NCMapper.Dapper.approved.txt",
               "PublicApi.NCMapper.EFCore.approved.txt", "PublicApi.NCMapper.FreeSql.approved.txt"):
        p = os.path.join(ROOT, "test", "NCMapper.Tests", fn)
        if not os.path.isfile(p):
            continue
        with open(p, encoding="utf-8") as f:
            for ln in f:
                m = re.search(
                    r"public (?:sealed |static |abstract |partial )*"
                    r"(?:class|record|interface|enum|struct) ([A-Za-z0-9_]+)", ln)
                if m:
                    names.add(m.group(1))
    return names


def mentioned(text, name):
    cands = {name}
    if name.endswith("Attribute"):
        cands.add(name[: -len("Attribute")])
    return any(re.search(r"\b%s\b" % re.escape(c), text) for c in cands)


def main(argv):
    strict = "--strict" in argv
    verbose = "-v" in argv or "--verbose" in argv
    warns = 0
    oks = 0

    print("=== check-doc-freshness：清单类文档新鲜度 ===")
    for t in TARGETS:
        doc = t["doc"]
        abs_doc = os.path.join(ROOT, doc)
        if not os.path.isfile(abs_doc):
            print("[skip] %s（文件不存在）" % doc)
            continue

        doc_date = git_last_commit_date(doc)
        guard_path, guard_date = resolve_guard(t["guards"][0])
        for g in t["guards"][1:]:
            _, gd = resolve_guard(g)
            if gd and (guard_date is None or gd > guard_date):
                guard_path, guard_date = g, gd

        print("\n--- %s ---" % doc)
        print("    文档最后改动 : %s" % (doc_date or "（未跟踪）"))
        print("    守护基准     : %s @ %s" % (guard_path, guard_date or "（未跟踪）"))

        # L1 timeline
        if doc_date and guard_date:
            if guard_date > doc_date:
                warns += 1
                print("    [warn] L1 基准晚于文档 %s 天 —— 文档可能未反映该变更"
                      % _days(guard_date, doc_date))
            else:
                oks += 1
                if verbose:
                    print("    [ok]   L1 文档不落后于基准（领先 %s 天）"
                          % _days(doc_date, guard_date))

        # L2 front matter
        declared = read_declared_date(abs_doc)
        if declared:
            if doc_date and declared != doc_date:
                warns += 1
                print("    [warn] L2 front matter 声明 %s，实际最后改动 %s（差 %s 天）"
                      % (declared, doc_date, _days(doc_date, declared)))
            else:
                oks += 1
                if verbose:
                    print("    [ok]   L2 front matter 与最后改动一致（%s）" % declared)
        else:
            if verbose:
                print("    [info] 未声明更新时间，跳过 L2")

        # L3 snapshot coverage
        if t.get("coverage") == "publicapi":
            types = publicapi_types()
            with open(abs_doc, encoding="utf-8") as f:
                text = f.read()
            missing = sorted(n for n in types if not mentioned(text, n))
            if missing:
                warns += 1
                print("    [warn] L3 快照类型零提及 %d/%d：%s"
                      % (len(missing), len(types), ", ".join(missing[:8])))
            else:
                oks += 1
                if verbose:
                    print("    [ok]   L3 快照公开类型 %d/%d 全部提及" % (len(types), len(types)))

        # L4 link-target coverage：架构图「文档」列的链接目标页须实际提及该行 API。
        # 背景（矩阵附录 A.4/A.5）：「链错页/虚假承载」此前无任何机器报警——错页链接被读者点开才发现。
        # 口径：行级 OR——该行任一 .md 链接目标页命中 API 关键词（首 API 名取「.」末段）即 pass；
        #       无链接行（如「—（文档缺）」）与非反引号行跳过。
        if abs_doc.replace("\\", "/").endswith("reference/architecture-map.md"):
            link_misses = []
            for line in text.splitlines():
                cm = re.match(r"^\|([^|]+)\|", line)
                if not cm or "](" not in line:
                    continue
                bm = re.search(r"`([^`]+)`", cm.group(1))
                if not bm:
                    continue
                api_first = bm.group(1).split("/")[0].strip()
                api_key = (api_first.split(".")[-1].split("(")[0]
                           .split("<")[0].replace("*", "").strip())
                if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", api_key):
                    continue  # 解析残渣（多实体/嵌套标注行）跳过，避免误报
                targets = re.findall(r"\]\(([^)]+\.md)\)", line)
                if not targets or not api_key:
                    continue
                hit = False
                for target in targets:
                    tp = os.path.normpath(os.path.join(os.path.dirname(abs_doc), target))
                    if os.path.isfile(tp):
                        with open(tp, encoding="utf-8") as tf:
                            if mentioned(tf.read(), api_key):
                                hit = True
                                break
                if not hit:
                    link_misses.append("%s → %s" % (api_key, targets[0]))
            if link_misses:
                warns += 1
                print("    [warn] L4 链接目标页未提及该行 API %d 处：%s"
                      % (len(link_misses), "; ".join(link_misses[:6])))
            else:
                oks += 1
                if verbose:
                    print("    [ok]   L4 文档列链接目标页全部命中 API 关键词")

    print("\n=== 汇总：ok=%d  warn=%d ===" % (oks, warns))
    if warns:
        print("提示：freshness 只说明「文档可能未跟上」，不等于文档错误；"
              "内容正确性需另查（链接目标页是否真讲了、声明是否成立）。")
    if strict and warns:
        return 1
    return 0


def _days(a, b):
    """Calendar-day gap b - a, parsed from YYYY-MM-DD."""
    from datetime import date
    try:
        ya, ma, da = (int(x) for x in a.split("-"))
        yb, mb, db = (int(x) for x in b.split("-"))
        return (date(ya, ma, da) - date(yb, mb, db)).days
    except Exception:
        return "?"


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
