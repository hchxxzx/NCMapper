#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check-doc-anchors.py -- validate `File.cs:NNN` code anchors written inside source comments.

GUARD SEMANTICS
  An "anchor" is a reference of the form `Name.cs:123` / `Name.cs:123-456` / `Name.cs:1/2/3`
  inside a `//` or `///` comment of a scanned source file. This script is the mechanical guard for
  the highest-frequency audit family (cross-file anchors that drift when the target file is edited).
  It is read-only: it never writes any file.

INPUT SURFACE (fixed)
  Scanned comments : every `//` and `///` line of the `*.cs` files under the given roots.
                     Block comments (`/* ... */`) are NOT scanned -- real sampling of this repo
                     found all anchors on line comments (see task spec 5.2).
                     Only the comment part of a line is inspected; code before `//` is ignored.
  Default roots    : <repo-root>/src
  Excluded         : bin/, obj/, .git/ (build output incl. generated .g.cs)

ANCHOR FORM (regular expression, whitelist based -- chosen in the first design round)
  (?P<file>[A-Za-z_][\\w./-]*\\.(?:cs|resx|md)):(?P<lines>\\d+(?:/\\d+)*)(?:-(?P<end>\\d+))?
  A multi-line-number anchor (`X.cs:617/623/634`) is SPLIT into one segment per number and every
  segment is judged on its own. A range (`X.cs:617-623`) uses the upper bound as the anchor line.

JUDGEMENT (two levels)
  L1 hard mismatch (gate, affects exit code):
      the target file cannot be located, or the anchor line (range upper bound) exceeds the total
      line count of the target file.
  L2 soft observation (reported, NEVER gates):
      L1 passed but no declaration line is found within N lines above the anchor
      (N = --soft-window, default 5). Many valid anchors point into a method body on purpose, so a
      line-level declaration test would flag them all -- hence L2 stays advisory.
      Declaration test uses an anchored word boundary:
          ^[ \\t]*(public|internal|protected|private|static|partial|sealed|abstract|readonly|
                  record|struct|class|interface|enum|delegate|void)\\b
      Substring matching (`.*class`, `.*void`) is forbidden: it matches "avoid" and prose.

TARGET RESOLUTION ORDER (first hit wins)
  1. relative to the repository root
  2. relative to <repo-root>/src
  3. relative to the directory of the scanned file (self-contained fixture support)
  4. unique basename match anywhere under the scan roots. Required in practice: this repo writes
     most anchors as bare file names (e.g. `CodeGenerator.Mapping.cs:1677`) while the target lives
     in a sibling project directory, so 1-3 alone mis-report existing files as missing. First-round
     measurement: 22 of 27 L1 items were false positives removed by this step (0 ambiguous).
  5. basename match disambiguated by a path suffix (`Internal/Foo.cs` -> any `*/Internal/Foo.cs`).
  Missing in all five -> L1 "file-not-found". Multiple basename hits that no suffix can split ->
     "ambiguous target" (reported in the soft section, never a hard mismatch).

WHITELIST / EXCLUSION RULES
  1. Historical anchors -- a reference preceded by a `原...` (formerly-...) qualifier, e.g.
     `原硬编码于 ExpressionBuilder.cs:617`. These point at locations that have since moved, so
     they are counted in a separate "historical references" section and never treated as mismatch.
  2. Multi line numbers -- split per segment (see ANCHOR FORM).
  3. Missing file / out-of-range line -- L1 hard mismatch.

OUTPUT AND EXIT CODE
  stdout: L1 mismatch list + L2 soft observation section + historical reference section + summary.
  exit 1 when at least one L1 hard mismatch exists; exit 0 otherwise.
  --report-only forces exit 0 (first-round baseline registration / F5 downgrade form).

USAGE
  python3 scripts/check-doc-anchors.py                       # repo src/ surface, gate mode
  python3 scripts/check-doc-anchors.py --report-only         # baseline registration
  python3 scripts/check-doc-anchors.py scripts/tests/fixtures/anchors/ --report-only
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

ANCHOR_RE = re.compile(
    r"(?P<file>[A-Za-z_][\w./-]*\.(?:cs|resx|md))"
    r":(?P<lines>\d+(?:/\d+)*)"
    r"(?:-(?P<end>\d+))?"
)

HISTORY_TAIL_RE = re.compile(r"原[^，。；、]{0,20}$")

DECL_RE = re.compile(
    r"^[ \t]*(?:public|internal|protected|private|static|partial|sealed|abstract|readonly|"
    r"record|struct|class|interface|enum|delegate|void)\b"
)

COMMENT_MARK = "//"
SKIP_DIRS = {"bin", "obj", ".git", ".vs"}
TARGET_SUFFIXES = (".cs", ".resx", ".md")


def iter_scanned_files(root: Path):
    """Yield every *.cs file below root, skipping build output directories."""
    if root.is_file():
        if root.name.endswith(".cs"):
            yield root
        return
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS)
        for name in sorted(filenames):
            if name.endswith(".cs"):
                yield Path(dirpath) / name


def read_lines(path: Path):
    """Read a text file as a list of lines (UTF-8, undecodable bytes ignored); None when unreadable."""
    try:
        text = path.read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return None
    return text.splitlines()


def iter_target_files(root: Path):
    """Yield every anchor-targetable file (*.cs / *.resx / *.md) below root."""
    if root.is_file():
        if root.name.endswith(TARGET_SUFFIXES):
            yield root
        return
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS)
        for name in sorted(filenames):
            if name.endswith(TARGET_SUFFIXES):
                yield Path(dirpath) / name


def build_basename_index(roots):
    """Map basename -> list of candidate paths, over every scan root."""
    index = {}
    for root in roots:
        if not root.exists():
            continue
        for path in iter_target_files(root):
            index.setdefault(path.name, []).append(path)
    return index


def resolve_target(name: str, scanned: Path, repo_root: Path, basename_index):
    """Resolve an anchor target file; first hit of the documented order wins.

    Returns (path, how) with how in {path, basename, suffix}, or (None, "ambiguous"|"missing").
    """
    candidates = [repo_root / name, repo_root / "src" / name, scanned.parent / name]
    for candidate in candidates:
        if candidate.is_file():
            return candidate, "path"

    hits = basename_index.get(Path(name).name, [])
    if len(hits) == 1:
        return hits[0], "basename"
    if len(hits) > 1:
        normalized = name.replace("\\", "/")
        suffix_hits = [h for h in hits if h.as_posix().endswith("/" + normalized)]
        if len(suffix_hits) == 1:
            return suffix_hits[0], "suffix"
        return None, "ambiguous"
    return None, "missing"


def classify(text: str, scanned: Path, repo_root: Path, soft_window: int, line_cache: dict,
             basename_index):
    """Split one comment line into judged anchor segments.

    Returns a list of dicts: kind = hard | soft | pass | history | ambiguous.
    """
    results = []
    for match in ANCHOR_RE.finditer(text):
        name = match.group("file")
        raw_lines = match.group("lines")
        end = match.group("end")
        is_history = HISTORY_TAIL_RE.search(text[: match.start()]) is not None

        segments = [(int(part), None) for part in raw_lines.split("/")]
        if end is not None:
            start, _ = segments[-1]
            segments[-1] = (start, int(end))

        for start, upper in segments:
            anchor_line = upper if upper is not None else start
            label = "%s:%d" % (name, start)
            if upper is not None:
                label = "%s:%d-%d" % (name, start, upper)

            if is_history:
                results.append({"kind": "history", "label": label, "anchor_line": anchor_line})
                continue

            target, how = resolve_target(name, scanned, repo_root, basename_index)
            if target is None:
                if how == "ambiguous":
                    results.append({"kind": "ambiguous", "label": label,
                                    "anchor_line": anchor_line,
                                    "reason": "ambiguous basename (multiple candidates)"})
                else:
                    results.append({"kind": "hard", "label": label, "anchor_line": anchor_line,
                                    "reason": "file-not-found"})
                continue

            key = str(target)
            if key not in line_cache:
                line_cache[key] = read_lines(target)
            target_lines = line_cache[key]
            if target_lines is None:
                results.append({"kind": "hard", "label": label, "anchor_line": anchor_line,
                                "reason": "target-unreadable"})
                continue
            if anchor_line > len(target_lines):
                results.append({"kind": "hard", "label": label, "anchor_line": anchor_line,
                                "reason": "line-out-of-range (file has %d lines)" % len(target_lines)})
                continue

            window_start = max(1, anchor_line - soft_window)
            window = target_lines[window_start - 1: anchor_line]
            if any(DECL_RE.match(line) for line in window):
                results.append({"kind": "pass", "label": label, "anchor_line": anchor_line})
            else:
                results.append({"kind": "soft", "label": label, "anchor_line": anchor_line,
                                "reason": "no declaration within %d lines above" % soft_window})
    return results


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Validate `File.cs:NNN` anchors inside source comments (read-only guard).")
    parser.add_argument("roots", nargs="*",
                        help="scan roots (default: <repo-root>/src)")
    parser.add_argument("--root", default=None,
                        help="repository root used to resolve anchor targets "
                             "(default: parent of the scripts/ directory)")
    parser.add_argument("--report-only", action="store_true",
                        help="always exit 0 (baseline registration / downgrade form)")
    parser.add_argument("--soft-window", type=int, default=5,
                        help="L2 look-back window in lines (default: 5)")
    args = parser.parse_args(argv)

    if args.soft_window < 0:
        parser.error("--soft-window must be >= 0")

    repo_root = Path(args.root).resolve() if args.root else Path(__file__).resolve().parent.parent
    roots = [Path(r).resolve() for r in args.roots] or [repo_root / "src"]

    hard, soft, history, passed, ambiguous = [], [], [], [], []
    files_scanned = 0
    anchors_found = 0
    line_cache = {}
    basename_index = build_basename_index(roots)

    for root in roots:
        if not root.exists():
            print("[WARN] scan root not found: %s" % root, file=sys.stderr)
            continue
        for scanned in iter_scanned_files(root):
            lines = read_lines(scanned)
            files_scanned += 1
            if not lines:
                continue
            for lineno, line in enumerate(lines, 1):
                if COMMENT_MARK not in line:
                    continue
                text = line[line.index(COMMENT_MARK):]
                if ":" not in text:
                    continue
                for item in classify(text, scanned, repo_root, args.soft_window, line_cache,
                                     basename_index):
                    anchors_found += 1
                    rel = os.path.relpath(str(scanned), str(repo_root)).replace("\\", "/")
                    record = (rel, lineno, item)
                    if item["kind"] == "hard":
                        hard.append(record)
                    elif item["kind"] == "soft":
                        soft.append(record)
                    elif item["kind"] == "history":
                        history.append(record)
                    elif item["kind"] == "ambiguous":
                        ambiguous.append(record)
                    else:
                        passed.append(record)

    print("== L1 hard mismatches (gate) ==")
    for rel, lineno, item in hard:
        print("%s:%d -> %s [%s]" % (rel, lineno, item["label"], item["reason"]))
    print("[L1] count: %d" % len(hard))

    print("")
    print("== L2 soft observations (report only, never gates) ==")
    for rel, lineno, item in soft:
        print("%s:%d -> %s [%s]" % (rel, lineno, item["label"], item["reason"]))
    print("[L2] count: %d" % len(soft))

    print("")
    print("== ambiguous targets (report only, never gates) ==")
    for rel, lineno, item in ambiguous:
        print("%s:%d -> %s [%s]" % (rel, lineno, item["label"], item["reason"]))
    print("[ambiguous] count: %d" % len(ambiguous))

    print("")
    print("== historical references (excluded from the gate) ==")
    for rel, lineno, item in history:
        print("%s:%d -> %s" % (rel, lineno, item["label"]))
    print("[history] count: %d" % len(history))

    print("")
    print("== summary ==")
    print("scan roots      : %s" % ", ".join(str(r) for r in roots))
    print("files scanned   : %d" % files_scanned)
    print("anchors judged  : %d" % anchors_found)
    print("  resolved (L1+L2 pass) : %d" % len(passed))
    print("  L2 soft observations  : %d" % len(soft))
    print("  ambiguous targets     : %d" % len(ambiguous))
    print("  historical references : %d" % len(history))
    print("  L1 hard mismatches    : %d" % len(hard))

    if args.report_only:
        print("")
        print("RESULT: REPORT-ONLY -- exit code forced to 0 (L1 hard mismatches: %d)" % len(hard))
        return 0

    print("")
    if hard:
        print("RESULT: FAIL -- %d L1 hard mismatch(es)" % len(hard))
        return 1
    print("RESULT: PASS -- zero L1 hard mismatch")
    return 0


if __name__ == "__main__":
    sys.exit(main())
