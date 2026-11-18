<#
.SYNOPSIS
check-comment-symbol-refs.ps1 - Assert that test/class names referenced inside source comments actually exist in the repository.

.DESCRIPTION
Mechanical guard against "phantom symbol" comments:
a comment that names a test method which does not exist is worse than no comment - it launders a stale
claim into apparent evidence. The first such case (a comment citing a *_HasFourEntries* test that never
existed) is what motivated this script.

Scope complementarity: scripts/check-doc-anchors.py validates `File:NNN` style anchors under src/ only.
This script validates symbol names written in comments under the equivalence/registry family, which is
where the cross-reference discipline lives.

Detection heuristic (deliberately narrow, to keep the false-positive rate near zero):
  * comment text is taken from // and /// lines only;
  * path/file-name tokens are stripped first (comments quote file paths such as test/.../FooTests.cs -
    those are not symbol references);
  * quoted code spans (<c>...</c> and backticks) are stripped - they quote code, they do not claim a guard;
  * a candidate is an identifier that either
      (a) starts uppercase, contains at least one underscore, length >= 12  (test-method naming style), or
      (b) ends with "Tests"                                                  (test class naming style);
    and is not preceded or followed by a word character (so the tail of a longer token such as
    "__conv_SgConflictDtoA_ComputedField" is not read as a separate name, and the head of a longer
    real name such as AssignmentPlanHubTestsExtended is not truncated into a phantom substring -
    trailing-boundary fix);
  * all-uppercase tokens (NCMAP007, STD-...) are skipped - matched with -cmatch, because
    PowerShell -match is case-insensitive and would otherwise swallow every PascalCase name
    (first-run attribution).

Resolution channels (a reference is OK when ANY holds):
  C1 declaration channel - the name is declared somewhere in the .cs corpus (void/class/record/...);
  C2 corpus channel      - the name occurs as an identifier or string literal in CODE. Comments are stripped
                           before building the corpus, otherwise a phantom would resolve itself - that is the
                           vacuous-lint failure mode this script exists to catch. This channel also covers
                           source-generated member names such as Map_X_To_Y, which have no declaration in the
                           source corpus;
  C3 historical channel  - the enclosing comment BLOCK is written about a removed/renamed symbol or a
                           commented-out placeholder (markers below, written as \uXXXX escapes so this file
                           stays pure ASCII and parses identically under Windows PowerShell 5.1 without a BOM).
                           Such hits are reported as HISTORICAL (informational), not failures.

Exit codes: 0 = no unresolved references (HISTORICAL allowed, printed for review); 1 = at least one live
phantom reference; 2 = bad arguments.

Self-check (non-vacuity): drop a scratch file under the scanned directory whose comment references a name
that exists nowhere; the script must exit 1 and name it. Verified (probe
"Nonexistent_GuardMethod_ThatDoesNotExist" -> FAIL; probe deleted afterwards).

.PARAMETER RepoRoot
Repository root (default: parent of the scripts directory).

.PARAMETER Include
Semicolon-separated directories (relative to RepoRoot) whose .cs comments are scanned.
Default 'test' - repository-wide test tree, widened after the sweep
cleared the unresolved phantoms and adjudicated the 30 historical references (point-at-real-name,
de-naming, or explicit retired-marker exemption). The original narrow default
'test/NCMapper.SourceGen.Tests/Equivalence' (where the discipline was established)
can still be selected explicitly.

.EXAMPLE
pwsh scripts/check-comment-symbol-refs.ps1
pwsh scripts/check-comment-symbol-refs.ps1 -Include "test"
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string]$Include = 'test'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
if (-not (Test-Path (Join-Path $RepoRoot 'NCMapper.slnx'))) {
    Write-Host "[symbol-refs] ERROR - not a repository root: $RepoRoot"
    exit 2
}

$dirs = $Include.Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
$csFiles = @()
foreach ($d in $dirs) {
    $p = Join-Path $RepoRoot ($d -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $p)) { Write-Host "[symbol-refs] ERROR - include path missing: $p"; exit 2 }
    $csFiles += Get-ChildItem -Path $p -Recurse -Filter '*.cs' -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
}
if ($csFiles.Count -eq 0) { Write-Host "[symbol-refs] ERROR - no .cs files under: $($dirs -join ', ')"; exit 2 }

# ---- corpus of declared symbols and code tokens (whole repo, not only the scanned subset) ----------
function Remove-CommentsForCorpus([string]$text) {
    $blockless = [regex]::Replace($text, '(?s)/\*.*?\*/', ' ')
    $kept = [System.Collections.Generic.List[string]]::new()
    foreach ($line in ($blockless -split "`n")) {
        $t = $line.TrimStart()
        if ($t.StartsWith('//')) { continue }                       # full-line comment
        $idx = $line.IndexOf('//')
        if ($idx -gt 0) {
            $before = $line.Substring(0, $idx)
            if (([regex]::Matches($before, '"')).Count % 2 -eq 0 -and $before.EndsWith(' ')) { $line = $before }
        }
        $kept.Add($line)
    }
    return ($kept -join "`n")
}

$allCs = Get-ChildItem -Path $RepoRoot -Recurse -Filter '*.cs' -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$declared = [System.Collections.Generic.HashSet[string]]::new()
$corpusTokens = [System.Collections.Generic.HashSet[string]]::new()
$declRx = [regex]'(?m)(?:\b(?:void|class|record|enum|interface|struct)\s+|\b(?:const|static|public|internal|private|protected)[\w\s<>,\?\[\]]*?\s)(?<name>[A-Za-z_][\w]*)\s*(?:\(|=|;|\{)'
foreach ($f in $allCs) {
    $text = Remove-CommentsForCorpus ([IO.File]::ReadAllText($f.FullName))
    foreach ($m in $declRx.Matches($text)) { [void]$declared.Add($m.Groups['name'].Value) }
    foreach ($m in [regex]::Matches($text, '\b(?:void|Task)\s+(?<name>[A-Za-z_][\w]*)\s*\(')) { [void]$declared.Add($m.Groups['name'].Value) }
    foreach ($m in [regex]::Matches($text, '[A-Za-z_][\w]*')) { [void]$corpusTokens.Add($m.Value) }
}

# ---- candidate references inside comments ---------------------------------------------------------
$commentRx = '(?m)^\s*(?://|///|/\*)\s?(?<text>.*)$'
$nameRx = [regex]'(?<![\w])(?<n>[A-Z][A-Za-z0-9_]{11,}(?:_[A-Za-z0-9]+)+|[A-Z][A-Za-z0-9]*Tests)(?![\w])'
$skipRx = '^[A-Z0-9_]+$'   # all-caps tokens (NCMAP007 style) are not symbol references
$pathTokenRx = '[A-Za-z0-9_.\\/-]*[\\/][A-Za-z0-9_.\\/-]*(?:\.(?:cs|ps1|py|sh|yml|md|json|xml|txt))?'

# Historical / non-executable markers, as \uXXXX escapes (pure-ASCII source):
#   \u539F former/original   \u65E7 old        \u5DF2 already      \u5220 delete
#   \u79FB remove/move       \u6CE8 annotate   \u5360 placeholder  \u66FE once
#   \u9000 retired           \u6539\u540D renamed  \u4E34\u65F6 temporary  \u53D6\u6D88 cancel
$historyRx = '\u539F|\u65E7|\u5DF2|\u5220|\u79FB|\u6CE8|\u5360|\u66FE|\u9000|\u6539\u540D|\u4E34\u65F6|\u53D6\u6D88|formerly|previously|renamed|stale|no longer|commented[- ]out|placeholder'

$problems = [System.Collections.Generic.List[string]]::new()
$historical = [System.Collections.Generic.List[string]]::new()
$checked = 0

foreach ($f in $csFiles) {
    $rel = $f.FullName.Substring($RepoRoot.Length).TrimStart('\','/').Replace('\','/')
    $lines = [IO.File]::ReadAllText($f.FullName) -split "`n"

    # Contiguous comment lines form a block; the historical judgement is made on the block, because a note
    # may say "former name X" one line above the reference itself.
    $blocks = [System.Collections.Generic.List[object]]::new()
    $current = [System.Collections.Generic.List[string]]::new()
    $blockStart = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $isComment = [regex]::IsMatch($lines[$i], '^\s*(?://|///|/\*)')
        if ($isComment) {
            if ($current.Count -eq 0) { $blockStart = $i + 1 }
            $current.Add($lines[$i])
        }
        $isLast = ($i -eq $lines.Count - 1)
        if ((-not $isComment) -or $isLast) {
            if ($current.Count -gt 0) {
                $blocks.Add([pscustomobject]@{ Start = $blockStart; End = $i + 1; Text = ($current -join "`n") })
                $current.Clear()
            }
        }
    }

    $lineNo = 0
    foreach ($line in $lines) {
        $lineNo++
        $cm = [regex]::Match($line, $commentRx)
        if (-not $cm.Success) { continue }
        $raw = $cm.Groups['text'].Value
        # Order matters: strip quoted code spans BEFORE path tokens, otherwise the path-token pattern eats the
        # "</c>" closing tag (it contains a slash) and the code span stops being recognised.
        $text = [regex]::Replace($raw, '(?s)<c>.*?</c>', ' ')
        $text = [regex]::Replace($text, '`[^`]*`', ' ')
        $text = [regex]::Replace($text, $pathTokenRx, ' ')

        $block = ''
        foreach ($b in $blocks) { if ($lineNo -ge $b.Start -and $lineNo -le $b.End) { $block = $b.Text; break } }
        if ([string]::IsNullOrWhiteSpace($block)) { $block = $raw }

        foreach ($m in $nameRx.Matches($text)) {
            $name = $m.Groups['n'].Value
            if ($name -cmatch $skipRx) { continue }
            if ($name.Length -lt 12) { continue }
            $checked++
            if ($declared.Contains($name)) { continue }        # C1 declaration channel
            if ($corpusTokens.Contains($name)) { continue }    # C2 corpus channel (generated surface)
            $where = "{0}:{1}  '{2}'" -f $rel, $lineNo, $name
            if ($block -match $historyRx) { $historical.Add($where) } else { $problems.Add($where) }
        }
    }
}

Write-Host "[symbol-refs] scanned comment references = $checked (files = $($csFiles.Count), declared = $($declared.Count), corpus tokens = $($corpusTokens.Count), include = $Include)"
if ($historical.Count -gt 0) {
    Write-Host "[symbol-refs] HISTORICAL references (past-tense prose or commented-out placeholders - review, not failures): $($historical.Count)"
    foreach ($h in $historical) { Write-Host "  ~ $h" }
}
if ($problems.Count -eq 0) {
    Write-Host "[symbol-refs] PASS - every live test/class name referenced in comments resolves."
    exit 0
}
Write-Host "[symbol-refs] FAIL - unresolved comment references ($($problems.Count)):"
foreach ($p in $problems) { Write-Host "  $p" }
Write-Host "[symbol-refs] Fix by pointing at a real lock/symbol or deleting the stale reference (do not add the name to a whitelist)."
exit 1
