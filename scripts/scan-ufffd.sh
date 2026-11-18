#!/usr/bin/env bash
# scan-ufffd.sh -- detect the U+FFFD replacement character (byte sequence EF BF BD) in repository text.
#
# GUARD SEMANTICS
#   Linux twin of scripts/scan-ufffd.ps1. Both implementations MUST share one detection semantics:
#   a raw byte-sequence match of EF BF BD. EF BF BD is the complete UTF-8 encoding of U+FFFD and can
#   never occur inside another valid UTF-8 sequence, so the byte-level match is exact and does not
#   depend on any decoder fallback behaviour (a decoded [char] comparison would diverge between the
#   PS1 and SH implementations once a file contains invalid UTF-8).
#
# SCAN SURFACE
#   gate (affects exit code) : src/**/*.cs, src/**/*.resx, docs/**/*.md
#                              (resx surface taken as every .resx under src/ -- wider than the
#                               Resources/-only wording of the task spec; the repo currently holds
#                               exactly 2 resx files, both under Resources/, so both readings agree
#                               today and the wider one fails safe for future additions)
#   optional (report only)   : locales/**/*.xml   (build by-product written back by csproj targets,
#                              NOT a source file -- see task spec A2)
#   excluded                 : src/**/bin/**, src/**/obj/**  (build output, incl. generated .g.cs)
#
# OUTPUT
#   <relative-path>:<line>:<column>     one line per hit; column = 1-based byte offset in the line.
#   Lines are located by LF; a CRLF file therefore reports the column measured from the line start.
#
# EXIT CODE
#   0 = zero hits on the gate surface; 1 = one or more gate hits.
#   --include-optional only appends the report-only section; it never changes the exit code.
#
# USAGE
#   bash scripts/scan-ufffd.sh [--include-optional] [--root <repo-root>]
set -u

if [ -z "${BASH_VERSION:-}" ]; then
    echo "scan-ufffd.sh requires bash." >&2
    exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
INCLUDE_OPTIONAL=0

while [ $# -gt 0 ]; do
    case "$1" in
        --include-optional) INCLUDE_OPTIONAL=1; shift ;;
        --root) ROOT="$2"; shift 2 ;;
        -h|--help)
            echo "usage: bash scripts/scan-ufffd.sh [--include-optional] [--root <repo-root>]"
            echo "  scans src/**/*.cs + src/**/*.resx + docs/**/*.md for byte sequence EF BF BD"
            echo "  exit 0 = clean gate surface, exit 1 = hits on the gate surface"
            exit 0 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

if [ ! -d "$ROOT" ]; then
    echo "repo root not found: $ROOT" >&2
    exit 2
fi

rel_path() {
    case "$1" in
        "$ROOT"/*) printf '%s' "${1#"$ROOT"/}" ;;
        *) printf '%s' "$1" ;;
    esac
}

# print every EF BF BD occurrence as <path>:<line>:<col>
scan_one() {
    local f="$1" r
    r="$(rel_path "$f")"
    LC_ALL=C awk -v path="$r" '
        {
            line = $0
            col = 1
            while ((p = index(line, "\357\277\275")) > 0) {
                printf "%s:%d:%d\n", path, FNR, col + p - 1
                line = substr(line, p + 3)
                col += p + 2
            }
        }
    ' "$f"
}

# collect files for the gate surface
collect_gate() {
    LC_ALL=C find "$ROOT/src" -type f \( -name '*.cs' -o -name '*.resx' \) \
        -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null
    LC_ALL=C find "$ROOT/docs" -type f -name '*.md' 2>/dev/null
}

collect_optional() {
    LC_ALL=C find "$ROOT/locales" -type f -name '*.xml' 2>/dev/null
}

gate_hits=0
gate_files=0

echo "== scan-ufffd (gate surface: src/**/*.cs + src/**/*.resx + docs/**/*.md) =="
while IFS= read -r f; do
    [ -n "$f" ] || continue
    gate_files=$((gate_files + 1))
    out="$(scan_one "$f")"
    if [ -n "$out" ]; then
        printf '%s\n' "$out"
        n=$(printf '%s\n' "$out" | wc -l)
        gate_hits=$((gate_hits + n))
    fi
done < <(collect_gate)
echo "[gate] files scanned: $gate_files, hits: $gate_hits"

optional_hits=0
if [ "$INCLUDE_OPTIONAL" -eq 1 ]; then
    echo ""
    echo "== scan-ufffd (optional surface: locales/**/*.xml -- report only, build by-product) =="
    opt_n=0
    while IFS= read -r f; do
        [ -n "$f" ] || continue
        out="$(scan_one "$f")"
        if [ -n "$out" ]; then
            printf '%s\n' "$out"
            n=$(printf '%s\n' "$out" | wc -l)
            optional_hits=$((optional_hits + n))
            opt_n=$((opt_n + 1))
        fi
    done < <(collect_optional)
    echo "[optional] files with hits: $opt_n, hits: $optional_hits (not part of the exit code)"
fi

echo ""
if [ "$gate_hits" -gt 0 ]; then
    echo "RESULT: FAIL -- $gate_hits hit(s) on the gate surface"
    exit 1
fi
echo "RESULT: PASS -- zero hits on the gate surface"
exit 0
