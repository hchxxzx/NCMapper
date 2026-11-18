<#
.SYNOPSIS
scan-snapshot-field-positions.ps1 - Reconcile SnapshotFieldRegistry Compared/Stripped slots against the hand-written Compare* projection bodies (dual-caliber recount channel).

.DESCRIPTION
Investigation archive script. It is NOT a standing gate:
the authoritative guard is the Roslyn-based ComparerFieldCoverageTests test class. This script exists so the
"79 / 77 / 2" style counts quoted in investigation documents can be recomputed by anyone, at any commit.

Two calibers (do NOT hardcode 79/77/2 as a permanent pass criterion):
  1) current  = working tree files            -> expected to agree with the B-3 counting gate (83/83/0 in current state)
  2) survey   = -AtCommit <rev> (git show)    -> the investigation-time snapshot (79/77/2 at 0efa85e)

Matching caliber (documented, heuristic on purpose):
  A Compared field counts as "actually compared" when a line inside its mapped Compare* method body contains
  `expected.<F>` or `actual.<F>` AND the same line carries a comparison operator/call (== != Equals SequenceEqual
  CompareList Compare*). Line-based, so a mention split across lines is under-counted (reported as a gap, never
  hidden). Comments and message arguments are excluded by requiring the comparison token on the same line.

Exit codes: 0 = recount produced output (informational, never fails a build); 2 = bad arguments / files missing.

.PARAMETER RepoRoot
Repository root (default: parent of the scripts directory).

.PARAMETER AtCommit
Optional revision. When set, both files are read through `git show <rev>:<path>` (survey caliber).

.PARAMETER BaseRef
Alias for -AtCommit (kept for parity with verify-equivalence-gate.ps1 argument style).

.EXAMPLE
pwsh scripts/scan-snapshot-field-positions.ps1
pwsh scripts/scan-snapshot-field-positions.ps1 -AtCommit 0efa85e
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string]$AtCommit = "",
    [string]$BaseRef = ""
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
if (-not (Test-Path (Join-Path $RepoRoot 'NCMapper.slnx'))) {
    Write-Host "[scan-fields] ERROR - not a repository root: $RepoRoot"
    exit 2
}
$rev = if ([string]::IsNullOrWhiteSpace($AtCommit)) { $BaseRef } else { $AtCommit }

$registryRel  = 'test/NCMapper.SourceGen.Tests/Equivalence/SnapshotFieldRegistry.cs'
$comparerRel  = 'test/NCMapper.SourceGen.Tests/Equivalence/SnapshotEquivalenceComparer.cs'

function Get-FileText([string]$root, [string]$rel, [string]$revision) {
    if ([string]::IsNullOrWhiteSpace($revision)) {
        $path = Join-Path $root ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path $path)) { Write-Host "[scan-fields] ERROR - missing file: $path"; exit 2 }
        return [IO.File]::ReadAllText($path)
    }
    $out = & git -C $root show "${revision}:${rel}" 2>$null
    if ($LASTEXITCODE -ne 0) { Write-Host "[scan-fields] ERROR - git show failed for ${revision}:${rel}"; exit 2 }
    return ($out -join "`n")
}

$registryText = Get-FileText $RepoRoot $registryRel $rev
$comparerText = Get-FileText $RepoRoot $comparerRel $rev

# ---- registry sections: type name -> Compared / Stripped field lists -------------------------------
$typeSections = [ordered]@{}
$sectionMatch = [regex]::Matches($registryText, '(?s)public static IReadOnlyList<FieldRegistration> (\w+)\s*\{\s*get;\s*\}\s*=\s*new\[\]\s*\{(.*?)\n\s*\};')
foreach ($m in $sectionMatch) {
    $typeName = $m.Groups[1].Value
    $body = $m.Groups[2].Value
    $compared  = @([regex]::Matches($body, 'Compared\("(\w+)"')  | ForEach-Object { $_.Groups[1].Value })
    $stripped  = @([regex]::Matches($body, 'Stripped\("(\w+)"')  | ForEach-Object { $_.Groups[1].Value })
    $typeSections[$typeName] = [pscustomobject]@{ Compared = $compared; Stripped = $stripped }
}

# ---- type -> Compare* method map, read from ComparerMethodByType (single source, not hardcoded) ----
$methodMap = [ordered]@{}
$mapMatch = [regex]::Matches($registryText, '\[nameof\((\w+)\)\]\s*=\s*(null|"(\w+)")\s*,')
foreach ($m in $mapMatch) {
    $methodMap[$m.Groups[1].Value] = if ($m.Groups[3].Success) { $m.Groups[3].Value } else { $null }
}

# Survey fallback: at revisions predating ComparerMethodByType (e.g. the investigation snapshot),
# derive the same mapping from the comparer's own hand-written signatures
# (`private static void CompareXxx(<Type> expected, <Type> actual, ...)`) so the survey caliber stays computable.
if ($mapMatch.Count -eq 0) {
    Write-Host "[scan-fields] note - no ComparerMethodByType at this revision; deriving type->method from comparer signatures (survey fallback)"
    foreach ($m in [regex]::Matches($comparerText, 'private static void (Compare\w+)\(\s*([A-Za-z_][\w]*)\??\s+expected')) {
        $t = $m.Groups[2].Value
        if (-not $methodMap.Contains($t)) { $methodMap[$t] = $m.Groups[1].Value }
    }
}

function Get-MethodBody([string]$text, [string]$methodName) {
    $rx = [regex]('private static void ' + [regex]::Escape($methodName) + '\(')
    $m = $rx.Match($text)
    if (-not $m.Success) { return $null }
    $depth = 0; $started = $false; $sb = New-Object Text.StringBuilder
    for ($i = $m.Index; $i -lt $text.Length; $i++) {
        $c = $text[$i]
        if ($c -eq '{') { $depth++; $started = $true }
        if ($started) { [void]$sb.Append($c) }
        if ($c -eq '}') { $depth--; if ($depth -eq 0) { break } }
    }
    return $sb.ToString()
}

$comparisonToken = '(==|!=|\bEquals\(|\bstring\.Equals\(|\.SequenceEqual\(|\bSequenceEqualOrdinal\(|\bCompareList\(|\bCompare\w+\()'

$totalCompared = 0; $totalComparedHit = 0; $totalStripped = 0; $totalStrippedLeak = 0
$missing = New-Object Collections.Generic.List[string]
$leaks   = New-Object Collections.Generic.List[string]
$skipped = New-Object Collections.Generic.List[string]

Write-Host "[scan-fields] caliber = $(if ([string]::IsNullOrWhiteSpace($rev)) { 'current (working tree)' } else { "survey @ $rev" })"
Write-Host "[scan-fields] registry sections = $($typeSections.Count); method map entries = $($methodMap.Count)"

foreach ($typeName in $typeSections.Keys) {
    $methodName = if ($methodMap.Contains($typeName)) { $methodMap[$typeName] } else { '<unmapped>' }
    if ($null -eq $methodName) { $skipped.Add("$typeName (no hand-written body - reflection + denylist derived)"); continue }

    $body = Get-MethodBody $comparerText $methodName
    if ($null -eq $body) { $skipped.Add("$typeName -> $methodName (method not located)"); continue }

    $sec = $typeSections[$typeName]
    $hitCount = 0
    foreach ($field in $sec.Compared) {
        $pattern = '(expected|actual)\.' + [regex]::Escape($field) + '\b'
        $lines = $body -split "`n"
        $hit = $false
        foreach ($line in $lines) {
            if ($line -match $pattern -and $line -match $comparisonToken) { $hit = $true; break }
        }
        if ($hit) { $hitCount++ } else { $missing.Add("$typeName.$field") }
    }
    $leakCount = 0
    foreach ($field in $sec.Stripped) {
        $pattern = '(expected|actual)\.' + [regex]::Escape($field) + '\b'
        foreach ($line in ($body -split "`n")) {
            if ($line -match $pattern -and $line -match $comparisonToken) { $leakCount++; break }
        }
    }

    $totalCompared      += $sec.Compared.Count
    $totalComparedHit   += $hitCount
    $totalStripped      += $sec.Stripped.Count
    $totalStrippedLeak  += $leakCount

    Write-Host ("  {0,-26} {1,-26} Compared={2,3}  compared-in-body={3,3}  Stripped={4,3}  stripped-in-comparison={5}" -f `
        $typeName, $methodName, $sec.Compared.Count, $hitCount, $sec.Stripped.Count, $leakCount)
}

Write-Host ""
Write-Host ("TOTAL (mapped types): Compared={0}  compared-in-body={1}  missing={2}  Stripped={3}  stripped-in-comparison={4}" -f `
    $totalCompared, $totalComparedHit, ($totalCompared - $totalComparedHit), $totalStripped, $totalStrippedLeak)
if ($missing.Count -gt 0) { Write-Host ("MISSING (registered Compared, no comparison line): {0}" -f ($missing -join ', ')) }
if ($leaks.Count   -gt 0) { Write-Host ("LEAK (registered Stripped, appears in comparison line): {0}" -f ($leaks -join ', ')) }
if ($skipped.Count -gt 0) { Write-Host ("SKIPPED: {0}" -f ($skipped -join ' | ')) }
Write-Host "[scan-fields] NOTE - line-based heuristic; authoritative guard is ComparerFieldCoverageTests (Roslyn comparative-mention predicate)."
exit 0
