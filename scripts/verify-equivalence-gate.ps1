<#
.SYNOPSIS
Equivalence gate check 3 - PR diff touches materialization surface but no Equivalence/ change => fail.

.DESCRIPTION
CI gate check 3.
Trigger list (prefix patterns, v1.2: DualDriverArbiter removed):
  - src/NCMapper.SourceGen/MappingExtractor*.cs (four partials)
  - src/NCMapper.SourceGen/FluentExtractor*.cs
  - src/NCMapper.SourceGen/ChainCallParser.cs
  - src/NCMapper.SourceGen/Models.Snapshots.cs
  - src/NCMapper.SourceGen/MappingSnapshotBuilder*.cs
  (NOT DualDriverArbiter*.cs - removed from trigger list)

Decision logic:
  PR diff n materialization-surface list != empty
  AND diff n (Equivalence/ dir) = empty
  => exit 1 (gate fail)

NOTE (header-vs-code alignment): the obligation surface is exactly $obligationPrefixes
  below (Equivalence/ directory only). There is NO registry-file exemption channel in
  this implementation — an earlier header revision claimed one; the wording was
  corrected to match the code. Red-paths via "edit a registry file" do not exist:
  fulfilling the obligation means adding/updating Equivalence/ tests.

Escape hatch: PR label 'skip-equivalence-gate' => job-level skip.

.PARAMETER BaseRef
Base reference for git diff (default: origin/main). Use a temp branch to test locally.

.EXAMPLE
.\scripts\verify-equivalence-gate.ps1 -BaseRef origin/main
.\scripts\verify-equivalence-gate.ps1 -BaseRef HEAD~1   # test against last commit
#>
[CmdletBinding()]
param(
    [string]$BaseRef = "origin/main"
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# Materialization surface trigger list (prefix patterns)
$triggerPrefixes = @(
    'src/NCMapper.SourceGen/MappingExtractor'
    'src/NCMapper.SourceGen/FluentExtractor'
    'src/NCMapper.SourceGen/ChainCallParser.cs'
    'src/NCMapper.SourceGen/Models.Snapshots.cs'
    'src/NCMapper.SourceGen/MappingSnapshotBuilder'
)

# Equivalence obligation paths (changes here = obligation fulfilled, gate passes)
$obligationPrefixes = @(
    'test/NCMapper.SourceGen.Tests/Equivalence/'
)

# Get PR diff file list (three-dot = merge-base diff, PR semantics)
$diffFiles = git -C $repo diff --name-only "$BaseRef...HEAD" 2>$null
if ($LASTEXITCODE -ne 0) {
    # Fallback: two-dot diff for local branches
    $diffFiles = git -C $repo diff --name-only "$BaseRef" HEAD 2>$null
    if ($LASTEXITCODE -ne 0) {
        # Fail CLOSED: unresolvable BaseRef must not silently pass the gate
        Write-Host "[equivalence-gate] ERROR - git diff failed for BaseRef '$BaseRef' (exit $LASTEXITCODE). Failing closed."
        exit 1
    }
}

if (-not $diffFiles) {
    Write-Host "[equivalence-gate] No diff files found - gate PASS (empty diff)"
    exit 0
}

$diffFileList = $diffFiles | Where-Object { $_ -ne '' }

# Check: does diff touch materialization surface?
$materializationTouched = $false
foreach ($file in $diffFileList) {
    foreach ($prefix in $triggerPrefixes) {
        if ($file -like "$prefix*") {
            $materializationTouched = $true
            break
        }
    }
    if ($materializationTouched) { break }
}

# Check: does diff touch Equivalence obligation paths?
$obligationFulfilled = $false
foreach ($file in $diffFileList) {
    foreach ($prefix in $obligationPrefixes) {
        if ($file -like "$prefix*") {
            $obligationFulfilled = $true
            break
        }
    }
    if ($obligationFulfilled) { break }
}

# Gate decision
if ($materializationTouched -and -not $obligationFulfilled) {
    Write-Host "[equivalence-gate] FAIL - PR diff touches materialization surface but no Equivalence/ changes."
    Write-Host "[equivalence-gate] Materialization files touched:"
    foreach ($file in $diffFileList) {
        foreach ($prefix in $triggerPrefixes) {
            if ($file -like "$prefix*") {
                Write-Host "  $file"
                break
            }
        }
    }
    Write-Host "[equivalence-gate] If this is intentional, add 'skip-equivalence-gate' label to the PR."
    Write-Host "[equivalence-gate] Otherwise, add/update Equivalence/ tests to fulfill the obligation."
    exit 1
}

Write-Host "[equivalence-gate] PASS - materialization surface not touched, or obligation fulfilled."
exit 0
