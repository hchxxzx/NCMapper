<#
.SYNOPSIS
scan-tautology-assertions.ps1 - Archive of the three-round tautological-assertion sweep (plus a fourth structural pass) that produced finding A-2.

.DESCRIPTION
Investigation archive script. NOT a standing gate:
static detection of vacuous assertions has a high false-positive rate, so this script is a re-runnable
evidence channel, not a build blocker. Exit code is always 0 unless arguments are wrong.

Rounds and measured false-positive rates (investigation run, baseline HEAD 9693257 / anchors re-read
at 0efa85e - re-verify before quoting):
  R1 Where -> assertion co-occurrence ............ 74 candidates, 2 confirmed real issues reviewed
  R2 Assert.All without a guard ..................  7 candidates, 1 reviewed
  R3 filter predicate == assertion target set .... 12 candidates (true-positive attribution)
  R4 structural fingerprint (mechanical R3) ......  same family as R3, adjacency-constrained
Every hit needs manual review; the only confirmed tautology found was A-2
(SnapshotFieldRegistryTests.ContainerElementTypes_AreSubsetOfRegistryTypes).

.PARAMETER RepoRoot
Repository root (default: parent of the scripts directory).

.PARAMETER Root
Subdirectory to scan, relative to RepoRoot (default: test).

.EXAMPLE
pwsh scripts/scan-tautology-assertions.ps1
pwsh scripts/scan-tautology-assertions.ps1 -Root test/NCMapper.SourceGen.Tests
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string]$Root = 'test'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
$scanRoot = Join-Path $RepoRoot ($Root -replace '/', [IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path $scanRoot)) { Write-Host "[tautology] ERROR - scan root missing: $scanRoot"; exit 2 }

# Assertion call names considered "the assertion side".
$assertRx   = '\bAssert(?:\.\w+)?\s*\.(\w+)\s*\('
$whereRx    = '\.Where\s*\(\s*(\w+)\s*=>'
$containsRx = '(?<set>[A-Za-z_][\w\.]*)\s*\.\s*Contains\s*\(\s*(?<var>\w+)\s*\)'

function Split-Methods([string]$text) {
    # Crude method segmentation: remember the signature line, then close the method when the brace depth
    # returns to the depth that was current when the signature was seen (methods live at depth >= 1 inside
    # a class, so comparing against 0 would never close them - first-run attribution).
    $methods = [System.Collections.Generic.List[object]]::new()
    $lines = $text -split "`n"
    $depth = 0; $start = -1; $base = -1; $sig = ''
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($start -lt 0 -and $depth -ge 1 -and $line -notmatch '^\s*(//|///|\*)' `
            -and $line -match '\b(?:void|Task|bool|int|string|object)\s+\w+\s*\(') {
            $sig = $line.Trim(); $start = $i; $base = $depth
        }
        foreach ($ch in $line.ToCharArray()) {
            if ($ch -eq '{') { $depth++ }
            elseif ($ch -eq '}') {
                $depth--
                if ($start -ge 0 -and $depth -eq $base) {
                    $methods.Add([pscustomobject]@{ Signature = $sig; StartLine = $start + 1; Body = (($lines[$start..$i]) -join "`n") })
                    $start = -1; $sig = ''; $base = -1
                }
            }
        }
    }
    return $methods
}

$hits = New-Object Collections.Generic.List[object]
$files = Get-ChildItem -Path $scanRoot -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

foreach ($file in $files) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $rel = $file.FullName.Substring($RepoRoot.Length).TrimStart('\','/').Replace('\','/')
    foreach ($method in (Split-Methods $text)) {
        $body = $method.Body

        $wheres = @([regex]::Matches($body, $whereRx) | ForEach-Object { $_.Groups[1].Value })
        if ($wheres.Count -eq 0) { continue }
        $asserts = @([regex]::Matches($body, $assertRx))
        if ($asserts.Count -eq 0) { continue }

        # R1: a Where lambda variable is reused on an assertion line in the same method.
        # (Checked line-wise: the assertion's argument list is not parsed, so matching only the
        #  "Assert.Xxx(" prefix would never see the lambda variable - first-run attribution.)
        foreach ($var in $wheres) {
            $varToken = '(?<![A-Za-z0-9_])' + [regex]::Escape($var) + '(?![A-Za-z0-9_])'
            foreach ($line in ($body -split "`n")) {
                if ($line -match 'Assert' -and $line -match $varToken) {
                    $hits.Add([pscustomobject]@{ Round='R1'; File=$rel; Line=$method.StartLine; Detail="Where lambda variable '$var' also appears on an assertion line" })
                    break
                }
            }
        }

        # R2: Assert.All over a collection produced by Where, with no count guard in the method.
        if ($body -match 'Assert\s*\.\s*All\s*\(') {
            if ($body -notmatch 'Assert\s*\.\s*(Equal|True|False)\s*\([^)]*\b(Count|Length)\b') {
                $hits.Add([pscustomobject]@{ Round='R2'; File=$rel; Line=$method.StartLine; Detail='Assert.All without a count/size guard in the same method' })
            }
        }

        # R3/R4: filter predicate "Set.Contains(v)" and assertion "Contains(v, Set)" on the same set+variable.
        # Filter predicates are taken ONLY from inside Where(...) lambdas (paren-balanced), otherwise the
        # assertion's own "Set.Contains(v)" text would match itself - a self-inflicted false positive
        # (first-run attribution).
        $filterSets = [System.Collections.Generic.List[object]]::new()
        foreach ($wm in [regex]::Matches($body, '\.Where\s*\(')) {
            $depth = 0; $sb = New-Object Text.StringBuilder; $started = $false
            for ($k = $wm.Index; $k -lt $body.Length; $k++) {
                $ch = $body[$k]
                if ($ch -eq '(') { $depth++; $started = $true }
                if ($started) { [void]$sb.Append($ch) }
                if ($ch -eq ')') { $depth--; if ($depth -eq 0) { break } }
            }
            foreach ($cm2 in [regex]::Matches($sb.ToString(), $containsRx)) {
                $filterSets.Add([pscustomobject]@{ Set = $cm2.Groups['set'].Value; Var = $cm2.Groups['var'].Value })
            }
        }
        foreach ($fs in $filterSets) {
            $assertContains = 'Assert\s*\.\s*Contains\s*\(\s*' + [regex]::Escape($fs.Var) + '\s*,\s*' + [regex]::Escape($fs.Set) + '\s*\)'
            $assertTrue     = 'Assert\s*\.\s*True\s*\(\s*' + [regex]::Escape($fs.Set) + '\s*\.\s*Contains\s*\(\s*' + [regex]::Escape($fs.Var) + '\s*\)'
            if ($body -match $assertContains -or $body -match $assertTrue) {
                $hits.Add([pscustomobject]@{ Round='R3'; File=$rel; Line=$method.StartLine; Detail="filter predicate '$($fs.Set).Contains($($fs.Var))' equals assertion target on the same set+variable - tautology candidate" })
            }
            # R4: adjacency-constrained structural fingerprint (Where(...) line followed by Assert.Contains line).
            $adj = 'Where\s*\(\s*\w+\s*=>\s*' + [regex]::Escape($fs.Set) + '\s*\.\s*Contains\s*\(\s*' + [regex]::Escape($fs.Var) + '\s*\)\s*\)[^\r\n]*\r?\n\s*Assert\s*\.\s*Contains\s*\(\s*' + [regex]::Escape($fs.Var) + '\s*,\s*' + [regex]::Escape($fs.Set)
            if ($body -match $adj) {
                $hits.Add([pscustomobject]@{ Round='R4'; File=$rel; Line=$method.StartLine; Detail="structural fingerprint: Where(v => $fs.Set.Contains(v)) immediately followed by Assert.Contains(v, $fs.Set)" })
            }
        }
    }
}

Write-Host "[tautology] scanned files = $($files.Count) under $Root"
Write-Host "[tautology] candidates = $($hits.Count) (R1=$(@($hits | Where-Object Round -eq 'R1').Count) R2=$(@($hits | Where-Object Round -eq 'R2').Count) R3=$(@($hits | Where-Object Round -eq 'R3').Count) R4=$(@($hits | Where-Object Round -eq 'R4').Count))"
foreach ($h in ($hits | Sort-Object Round, File, Line)) {
    Write-Host ("  [{0}] {1}:{2}  {3}  ({4})" -f $h.Round, $h.File, $h.Line, $h.Signature, $h.Detail)
}
Write-Host "[tautology] NOTE - candidates only; every hit requires manual review (measured false-positive rates in the header). Not a standing gate."
exit 0
