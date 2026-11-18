<#
.SYNOPSIS
check-comment-process-noise.ps1 - Hard gate against process noise (task ids, review markers, internal document section references) written into shipped source comments and resx message text.

.DESCRIPTION
Prevents re-accumulation of the noise cleaned by the comment-hygiene design (scheme 20261012-001 section 4.4).
Comment bodies carrying task ids, review-round labels or "changed on <date>" prose leak into IntelliSense,
into the shipped source and into generated code, and they go stale. The evolution layer belongs to git
history and to the private documentation, not to the source text.

The pattern set (13 hard classes + 1 literal class + 2 informational classes) is the SAME set as the
cleaning criteria, so "gate is green" and "surface is clean" cannot drift apart.

Scanned surface:
  * *.cs   - comment text only: XML doc (///), whole-line comments (//), trailing comments after code,
             and block comments. One stateful pass also records string / char literal spans, so "//" inside
             a string or a URL is never read as a comment and a quoted code span is never read as a comment.
             Trailing-comment rule (the single definition, documented in scripts/README.md): a comment span
             that begins on a line whose code part is not empty. Ad-hoc regexes disagreed (62 / 64 / 55)
             because they counted "//" inside string literals; this scanner gives one reproducible reading.
  * *.resx - <value> text and <!-- --> comments (diagnostic message surface).

Exit codes: 0 = no hard failure (informational hits allowed, printed for review);
1 = at least one hard failure, or scanned count = 0 (vacuous-pass guard);
2 = bad arguments (missing include path, no files).

Self-check (non-vacuity): drop a scratch .cs file under the scanned tree whose comment contains
TASK-99999999; the script must exit 1 and name it. Verified on the first run of this gate.

.PARAMETER RepoRoot
Repository root (default: parent of the scripts directory).

.PARAMETER Include
Directories (relative to RepoRoot) whose *.cs / *.resx are scanned. Default 'src' + 'samples'.
A single element may carry several directories separated by ';' - that form is required when the
script is launched through "powershell -File", which cannot pass a real array.
test/, docs/, locales/ are outside this default; a batch that cleans them must widen the list explicitly.

.PARAMETER OutputFile
Write the hard-hit list to this file (UTF-8, no BOM; relative paths resolve against RepoRoot).
Used by the red-face baseline and by the local-ci scan leg, which compares against the baseline
instead of piping hits through the console (console codepage would mangle non-ASCII comment text).

.PARAMETER HitsOnly
Print only the machine-readable hard-hit list - used for the red-face baseline snapshot.

.EXAMPLE
pwsh scripts/check-comment-process-noise.ps1
pwsh scripts/check-comment-process-noise.ps1 -Include @('src','samples')
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string[]]$Include = @('src', 'samples'),
    [string]$OutputFile = "",
    [switch]$HitsOnly
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
if (-not (Test-Path (Join-Path $RepoRoot 'NCMapper.slnx'))) {
    Write-Host "[comment-noise] ERROR - not a repository root: $RepoRoot"
    exit 2
}

# ---- hard classes (identical to the cleaning criteria; Chinese words as \uXXXX, file stays pure ASCII) ----
$hardClasses = [ordered]@{
    'H01' = 'TASK-\d{8}'
    'H02' = 'REQ-\d{8}'
    'H03' = '\bT-\d{2,3}\b'
    'H04' = '\bF-B\d+\b'
    'H05' = '\bW\dE-\d+\b'
    'H06' = '\u6CE2\d-E'
    'H07' = '\uFF08\u8BC4\u5BA1 ?S-\d+'
    'H08' = '\bS-\d+\b'
    'H09' = '\bQ\d ?(\u5B9A\u6848|\u5173\u95ED\u58F0\u660E|closure)'
    'H10' = '\u65B9\u6848\u4E66 ?\u00A7'
    'H11' = 'introduced in T-'
    'H12' = '(\u4EFB\u52A1\u4E66|\u65B9\u6848\u4E66|\u65B9\u6848|\u8BC4\u4F30\u62A5\u544A|\u8BC4\u4F30\u8BB0\u5F55|\u767B\u8BB0\u7C3F|\u7EDF\u7B79|\u7ECF\u9A8C\u5E93|ADR-[0-9]+)[^\n]{0,24}\u00A7'
    'H13' = '\u00A7\s*[0-9]'
}
# informational classes: surfaced for review, never a failure (context-dependent, high false-positive rate)
$infoClasses = [ordered]@{
    'I01' = '\d{4}-\d{2}-\d{2}'
    'I02' = '(\u6536\u53E3|\u6574\u6539|\u6279\u9605|\u672C\u4EFB\u52A1|\u5386\u53F2\u8BB0\u5F55|\u6B64\u524D|\u66FE\u901A\u8FC7)'
}
$literalRx = 'TASK-\d{8}'

# ---- whitelist: one entry per kept line, each with a recorded reason (no blanket exemptions) --------------
$whitelist = @(
    [pscustomobject]@{ Path = 'src/NCMapper/Shared/TypeCompatibilityCore.cs'; Class = 'H13'; Snippet = '10.3.2'; Reason = 'reference to the C# language specification (external standard), not an internal document section' },
    [pscustomobject]@{ Path = 'src/NCMapper.SourceGen/TypeHelpers.cs';        Class = 'H13'; Snippet = '10.3.2'; Reason = 'reference to the C# language specification (external standard), not an internal document section' }
)
function Test-Whitelisted($rel, $class, $lineText) {
    foreach ($w in $whitelist) {
        if ($w.Path -eq $rel -and $w.Class -eq $class -and $lineText.Contains($w.Snippet)) { return $w.Reason }
    }
    return $null
}

# ---- stateful scanner: comment spans + string/char literal spans -----------------------------------------
function Get-SignificantSpans([string]$text) {
    $spans = [System.Collections.Generic.List[object]]::new()
    $i = 0; $n = $text.Length
    while ($i -lt $n) {
        $c = $text[$i]
        if ($c -eq '/' -and $i + 1 -lt $n -and $text[$i + 1] -eq '/') {
            $start = $i
            while ($i -lt $n -and $text[$i] -ne "`n") { $i++ }
            $spans.Add([pscustomobject]@{ Start = $start; End = $i; Kind = 'comment' }); continue
        }
        if ($c -eq '/' -and $i + 1 -lt $n -and $text[$i + 1] -eq '*') {
            $start = $i; $i += 2
            while ($i + 1 -lt $n -and -not ($text[$i] -eq '*' -and $text[$i + 1] -eq '/')) { $i++ }
            $end = [Math]::Min($i + 2, $n)
            $spans.Add([pscustomobject]@{ Start = $start; End = $end; Kind = 'comment' }); $i = $end; continue
        }
        if ($c -eq '@' -and $i + 1 -lt $n -and $text[$i + 1] -eq '"') {
            $start = $i; $i = Close-Verbatim $text ($i + 2)
            $spans.Add([pscustomobject]@{ Start = $start; End = $i; Kind = 'string' }); continue
        }
        if ($c -eq '$' -and $i + 1 -lt $n) {
            if ($text[$i + 1] -eq '@' -and $i + 2 -lt $n -and $text[$i + 2] -eq '"') {
                $start = $i; $i = Close-Verbatim $text ($i + 3)
                $spans.Add([pscustomobject]@{ Start = $start; End = $i; Kind = 'string' }); continue
            }
            if ($text[$i + 1] -eq '"') {
                $start = $i
                if ($i + 3 -lt $n -and $text[$i + 2] -eq '"' -and $text[$i + 3] -eq '"') { $i = Close-Raw $text ($i + 4) }
                else { $i = Close-Regular $text ($i + 2) }
                $spans.Add([pscustomobject]@{ Start = $start; End = $i; Kind = 'string' }); continue
            }
        }
        if ($c -eq '"' -and $i + 2 -lt $n -and $text[$i + 1] -eq '"' -and $text[$i + 2] -eq '"') {
            $start = $i; $i = Close-Raw $text ($i + 3)
            $spans.Add([pscustomobject]@{ Start = $start; End = $i; Kind = 'string' }); continue
        }
        if ($c -eq '"') {
            $start = $i; $i = Close-Regular $text ($i + 1)
            $spans.Add([pscustomobject]@{ Start = $start; End = $i; Kind = 'string' }); continue
        }
        if ($c -eq "'") {
            $start = $i; $i++; $n2 = $text.Length
            while ($i -lt $n2) {
                if ($text[$i] -eq '\') { $i += 2; continue }
                if ($text[$i] -eq "'") { $i++; break }
                if ($text[$i] -eq "`n") { break }
                $i++
            }
            $spans.Add([pscustomobject]@{ Start = $start; End = $i; Kind = 'string' }); continue
        }
        $i++
    }
    return $spans
}
function Close-Regular([string]$t, [int]$i) {
    $n = $t.Length
    while ($i -lt $n) {
        $d = $t[$i]
        if ($d -eq '\') { $i += 2; continue }
        if ($d -eq '"') { return $i + 1 }
        if ($d -eq "`n") { return $i }
        $i++
    }
    return $n
}
function Close-Verbatim([string]$t, [int]$i) {
    $n = $t.Length
    while ($i -lt $n) {
        if ($t[$i] -eq '"') {
            if ($i + 1 -lt $n -and $t[$i + 1] -eq '"') { $i += 2; continue }
            return $i + 1
        }
        $i++
    }
    return $n
}
function Close-Raw([string]$t, [int]$i) {
    $n = $t.Length
    while ($i + 2 -lt $n) {
        if ($t[$i] -eq '"' -and $t[$i + 1] -eq '"' -and $t[$i + 2] -eq '"') { return $i + 3 }
        $i++
    }
    return $n
}

# ---- collection --------------------------------------------------------------------------------------------
$dirs = @($Include | ForEach-Object { $_.Trim() } | Where-Object { $_ } | ForEach-Object { $_.Split(';') } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($dirs.Count -eq 0) { Write-Host "[comment-noise] ERROR - empty -Include"; exit 2 }
$csFiles = @(); $resxFiles = @()
foreach ($d in $dirs) {
    $p = Join-Path $RepoRoot ($d -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $p)) { Write-Host "[comment-noise] ERROR - include path missing: $p"; exit 2 }
    $csFiles += Get-ChildItem -Path $p -Recurse -Filter '*.cs' -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    $resxFiles += Get-ChildItem -Path $p -Recurse -Filter '*.resx' -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
}
if ($csFiles.Count + $resxFiles.Count -eq 0) {
    Write-Host "[comment-noise] ERROR - no .cs / .resx files under: $($dirs -join ', ')"; exit 2
}

$hardHits = [System.Collections.Generic.List[string]]::new()
$infoHits = [System.Collections.Generic.List[string]]::new()
$whitelisted = [System.Collections.Generic.List[string]]::new()
$scannedLines = 0; $scannedRegions = 0

function Classify-Probe($probe, $rel, $lineNo, $lineText, $kindTag) {
    foreach ($k in $script:hardClasses.Keys) {
        if ([regex]::IsMatch($probe, $script:hardClasses[$k])) {
            $where = "{0}:{1} [{2}/{3}] {4}" -f $rel, $lineNo, $k, $kindTag, $lineText.Trim()
            $reason = Test-Whitelisted $rel $k $lineText
            if ($null -ne $reason) { $script:whitelisted.Add("$where  -> kept: $reason") }
            else { $script:hardHits.Add($where) }
        }
    }
    foreach ($k in $script:infoClasses.Keys) {
        if ([regex]::IsMatch($probe, $script:infoClasses[$k])) {
            $script:infoHits.Add(("{0}:{1} [{2}/{3}] {4}" -f $rel, $lineNo, $k, $kindTag, $lineText.Trim()))
        }
    }
}

foreach ($f in $csFiles) {
    $rel = $f.FullName.Substring($RepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    $text = [IO.File]::ReadAllText($f.FullName)
    $lines = $text -split "`n"
    $starts = [System.Collections.Generic.List[int]]::new()
    $pos = 0
    foreach ($l in $lines) { $starts.Add($pos); $pos += $l.Length + 1 }
    $spans = Get-SignificantSpans $text
    $ln = 0
    foreach ($s in $spans) {
        while ($ln + 1 -lt $starts.Count -and $starts[$ln + 1] -le $s.Start) { $ln++ }
        $lineText = $lines[$ln].TrimEnd("`r")
        if ($s.Kind -eq 'string') {
            if ($s.End - $s.Start -le 2000 -and $text.Substring($s.Start, $s.End - $s.Start) -match $literalRx) {
                $where = "{0}:{1} [L01/literal] {2}" -f $rel, ($ln + 1), $lineText.Trim()
                $reason = Test-Whitelisted $rel 'L01' $lineText
                if ($null -ne $reason) { $whitelisted.Add("$where  -> kept: $reason") }
                else { $hardHits.Add($where) }
            }
            continue
        }
        $codePart = $lineText.Substring(0, [Math]::Min($s.Start - $starts[$ln], $lineText.Length)).TrimEnd()
        $kind = if ($codePart.Length -eq 0) { 'line' } else { 'trailing' }
        $probe = $text.Substring($s.Start, $s.End - $s.Start)
        if ($probe.StartsWith('///')) { $kind = 'doc' }
        $scannedLines++
        Classify-Probe $probe $rel ($ln + 1) $lineText $kind
    }
}

foreach ($f in $resxFiles) {
    $rel = $f.FullName.Substring($RepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    $text = [IO.File]::ReadAllText($f.FullName)
    $lines = $text -split "`n"
    foreach ($m in [regex]::Matches($text, '(?s)<value>(.*?)</value>|<!--(.*?)-->')) {
        $inner = if ($m.Groups[1].Success) { $m.Groups[1].Value } else { $m.Groups[2].Value }
        $baseOff = $m.Index + $m.Value.IndexOf($inner)
        $scannedRegions++
        foreach ($k in $hardClasses.Keys) {
            $mm = [regex]::Match($inner, $hardClasses[$k])
            if (-not $mm.Success) { continue }
            $lineNo = 1 + ([regex]::Matches($text.Substring(0, [Math]::Min($baseOff + $mm.Index, $text.Length)), "`n")).Count
            $lineText = $lines[$lineNo - 1].TrimEnd("`r")
            $where = "{0}:{1} [{2}/resx] {3}" -f $rel, $lineNo, $k, $lineText.Trim()
            $reason = Test-Whitelisted $rel $k $lineText
            if ($null -ne $reason) { $whitelisted.Add("$where  -> kept: $reason") }
            else { $hardHits.Add($where) }
        }
        foreach ($k in $infoClasses.Keys) {
            $mm = [regex]::Match($inner, $infoClasses[$k])
            if (-not $mm.Success) { continue }
            $lineNo = 1 + ([regex]::Matches($text.Substring(0, [Math]::Min($baseOff + $mm.Index, $text.Length)), "`n")).Count
            $infoHits.Add(("{0}:{1} [{2}/resx] {3}" -f $rel, $lineNo, $k, $lines[$lineNo - 1].TrimEnd("`r").Trim()))
        }
    }
}

# ---- report -------------------------------------------------------------------------------------------------
if (-not [string]::IsNullOrWhiteSpace($OutputFile)) {
    # absolute-path discipline: a relative -OutputFile is resolved against RepoRoot, never against the
    # process working directory (experience #171).
    $outFull = if ([IO.Path]::IsPathRooted($OutputFile)) { $OutputFile } else { Join-Path $RepoRoot $OutputFile }
    $outDir = Split-Path -Parent $outFull
    if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
    [IO.File]::WriteAllLines($outFull, [string[]]$hardHits, [System.Text.UTF8Encoding]::new($false))
    Write-Host ("[comment-noise] hard-hit list written: {0} ({1} lines)" -f $outFull, $hardHits.Count)
}
if ($HitsOnly) {
    foreach ($h in $hardHits) { Write-Host $h }
}
else {
    Write-Host ("[comment-noise] scanned comment lines = {0} (resx regions = {1}, cs files = {2}, resx files = {3}, include = {4})" -f $scannedLines, $scannedRegions, $csFiles.Count, $resxFiles.Count, ($dirs -join ','))
    if ($whitelisted.Count -gt 0) {
        Write-Host ("[comment-noise] whitelisted hits (each carries a recorded reason): {0}" -f $whitelisted.Count)
        foreach ($w in $whitelisted) { Write-Host "  + $w" }
    }
    if ($infoHits.Count -gt 0) {
        Write-Host ("[comment-noise] INFORMATIONAL hits (status words / dates - review, not failures): {0}" -f $infoHits.Count)
        foreach ($h in $infoHits) { Write-Host "  ~ $h" }
    }
}

if ($scannedLines + $scannedRegions -eq 0) {
    Write-Host "[comment-noise] FAIL - scanned count = 0 (vacuous-pass guard: nothing was inspected)" -ForegroundColor Red
    exit 1
}
if ($hardHits.Count -eq 0) {
    if (-not $HitsOnly) { Write-Host "[comment-noise] PASS - no process-noise pattern hit in the scanned comment surface." }
    exit 0
}
if (-not $HitsOnly) {
    Write-Host ("[comment-noise] FAIL - process noise in shipped comments / message text ({0}):" -f $hardHits.Count) -ForegroundColor Red
    foreach ($h in $hardHits) { Write-Host "  $h" }
    Write-Host "[comment-noise] Delete the id / date / section reference and keep the sentence (whitelist entries are only for external standard references, each with a reason)."
}
exit 1
