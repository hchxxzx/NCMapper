<#
.SYNOPSIS
Scans repository text files for the U+FFFD replacement character (byte sequence EF BF BD).

.DESCRIPTION
Windows twin of scripts/scan-ufffd.sh. Both implementations MUST share one detection semantics:
a raw byte-sequence match of EF BF BD. The script reads every file with
[System.IO.File]::ReadAllBytes and never decodes text, because decoding replaces invalid bytes by
U+FFFD and would collapse the distinction between "an intentional replacement character" and
"encoding damage" (the very defect family this scan guards against).

EF BF BD is the complete UTF-8 encoding of U+FFFD and can never occur inside another valid UTF-8
sequence, so the byte-level match is exact and decoder-independent.

SCAN SURFACE
  gate (affects exit code) : src/**/*.cs, src/**/*.resx, docs/**/*.md
                             (resx surface taken as every .resx under src/ -- wider than the
                              Resources/-only wording of the task spec; the repo currently holds
                              exactly 2 resx files, both under Resources/, so both readings agree
                              today and the wider one fails safe for future additions)
  optional (report only)   : locales/**/*.xml  (build by-product written back by csproj targets,
                             not a source file)
  excluded                 : src/**/bin/**, src/**/obj/**  (build output, incl. generated .g.cs)

OUTPUT
  <relative-path>:<line>:<column>   one line per hit; column = 1-based byte offset in the line.

EXIT CODE
  0 = zero hits on the gate surface; 1 = one or more gate hits.
  -IncludeOptional only appends the report-only section; it never changes the exit code.

.PARAMETER RepoRoot
Repository root (defaults to the parent directory of this script).

.PARAMETER IncludeOptional
Also print hits found on the optional surface (locales/**/*.xml), reported but never gating.

.EXAMPLE
.\scripts\scan-ufffd.ps1
.\scripts\scan-ufffd.ps1 -IncludeOptional
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [switch]$IncludeOptional
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$RepoRoot = (Resolve-Path $RepoRoot).Path

function Get-RelativePath {
    param([string]$FullPath, [string]$Root)
    if ($FullPath.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $FullPath.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')
    }
    return $FullPath.Replace('\', '/')
}

# Byte-level scan: locate every EF BF BD occurrence, tracking line (LF count) and byte column.
function Get-UfffdHit {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hits = New-Object System.Collections.ArrayList
    $line = 1
    $lineStart = 0
    $i = 0
    $len = $bytes.Length

    while ($i -lt $len) {
        if ($bytes[$i] -eq 0x0A) {
            $line++
            $lineStart = $i + 1
            $i++
            continue
        }
        if ($bytes[$i] -eq 0xEF -and ($i + 2) -lt $len -and $bytes[$i + 1] -eq 0xBF -and $bytes[$i + 2] -eq 0xBD) {
            [void]$hits.Add([pscustomobject]@{
                Line = $line
                Column = ($i - $lineStart + 1)
            })
            $i += 3
            continue
        }
        $i++
    }
    return $hits
}

function Get-ScanFile {
    param([string]$Directory, [string[]]$Include)

    if (-not (Test-Path $Directory)) { return @() }
    $found = New-Object System.Collections.ArrayList
    $items = @(Get-ChildItem -Path $Directory -Recurse -File -Include $Include -ErrorAction SilentlyContinue)
    foreach ($item in $items) {
        if ($item.FullName -match '[\\/](bin|obj)[\\/]') { continue }
        [void]$found.Add($item)
    }
    return $found
}

$gateFiles = New-Object System.Collections.ArrayList
foreach ($item in (Get-ScanFile -Directory (Join-Path $RepoRoot 'src') -Include @('*.cs', '*.resx'))) {
    [void]$gateFiles.Add($item)
}
foreach ($item in (Get-ScanFile -Directory (Join-Path $RepoRoot 'docs') -Include @('*.md'))) {
    [void]$gateFiles.Add($item)
}

$gateHits = 0
Write-Host "== scan-ufffd (gate surface: src/**/*.cs + src/**/*.resx + docs/**/*.md) =="
foreach ($file in $gateFiles) {
    $rel = Get-RelativePath -FullPath $file.FullName -Root $RepoRoot
    foreach ($hit in (Get-UfffdHit -Path $file.FullName)) {
        Write-Host ("{0}:{1}:{2}" -f $rel, $hit.Line, $hit.Column)
        $gateHits++
    }
}
Write-Host ("[gate] files scanned: {0}, hits: {1}" -f $gateFiles.Count, $gateHits)

if ($IncludeOptional) {
    Write-Host ""
    Write-Host "== scan-ufffd (optional surface: locales/**/*.xml -- report only, build by-product) =="
    $optHits = 0
    $optFiles = 0
    foreach ($file in (Get-ScanFile -Directory (Join-Path $RepoRoot 'locales') -Include @('*.xml'))) {
        $rel = Get-RelativePath -FullPath $file.FullName -Root $RepoRoot
        $fileHits = @(Get-UfffdHit -Path $file.FullName)
        if ($fileHits.Count -eq 0) { continue }
        $optFiles++
        foreach ($hit in $fileHits) {
            Write-Host ("{0}:{1}:{2}" -f $rel, $hit.Line, $hit.Column)
            $optHits++
        }
    }
    Write-Host ("[optional] files with hits: {0}, hits: {1} (not part of the exit code)" -f $optFiles, $optHits)
}

Write-Host ""
if ($gateHits -gt 0) {
    Write-Host ("RESULT: FAIL -- {0} hit(s) on the gate surface" -f $gateHits)
    exit 1
}
Write-Host "RESULT: PASS -- zero hits on the gate surface"
exit 0
