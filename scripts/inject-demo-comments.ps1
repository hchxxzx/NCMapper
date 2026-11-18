#Requires -Version 5.1
# inject-demo-comments.ps1 -- demo code comment i18n injection (2026-09-30, design A: comment sidecars)
# NOTE: keep this file pure ASCII (no BOM dependency) -- Windows PowerShell 5.1 mis-parses
# non-ASCII .ps1 without a UTF-8 BOM.
#
# Architecture: .cs files under samples/ are the single code source (Chinese comments canonical).
# Per-language comment translations live in sidecars (pure data, no code):
#   docs/i18n/<lang>/samples/.../<file>.cs.json
# This script fills the translated comments back into the code and writes to -OutputDir
# (mirroring source paths). Only comment spans are touched -> code byte-identity is a
# structural guarantee; #region anchors (preprocessor directives, not comments) are intact.
#
# Sidecar format:
#   { "source": "samples/.../X.cs", "language": "<lang>",
#     "generatedFrom": "<sha256 of source at generation time>",
#     "comments": [ { "i": 0, "zh": "original comment text", "<lang>": "translation" }, ... ] }
# i = global comment index in the file (all comments, translated or not, for stable locating).
# The translation field is named after -Language (en sidecars use "en", ja sidecars "ja", ...);
# a missing/empty translation field fails the build (exit 2).
#
# Staleness guard: each sidecar entry's zh must exactly match the current text of comment #i
# in the source file, otherwise the build fails (source changed -> regenerate sidecar).
#
# Usage:
#   pwsh scripts/inject-demo-comments.ps1 -Language en -SidecarDir docs/i18n -OutputDir temp/en-demo-overlay
# Exit codes: 0 = ok, 2 = sidecar stale/malformed
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$Language,
  [Parameter(Mandatory)][string]$SidecarDir,
  [Parameter(Mandatory)][string]$OutputDir
)
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
Set-Location $repoRoot

# Comment scan: returns spans (start/end offsets + type). Recognizes // line comments and
# /* */ block comments, skipping string/char literals (so // inside URLs is not a comment).
function Get-CommentSpans([string]$text) {
  $spans = New-Object System.Collections.Generic.List[object]
  $n = $text.Length; $i = 0
  while ($i -lt $n) {
    $c = $text[$i]
    if ($c -eq '"' -or $c -eq "'") {
      # literal skip MUST terminate on the matching quote (no break = swallow rest of file)
      $q = $c; $i++
      while ($i -lt $n) {
        if ($text[$i] -eq '\') { $i += 2; continue }
        if ($text[$i] -eq $q) { $i++; break }
        $i++
      }
    }
    elseif ($c -eq '/' -and $i+1 -lt $n -and $text[$i+1] -eq '/') {
      $start = $i; $i += 2
      while ($i -lt $n -and $text[$i] -ne "`n") { $i++ }
      $spans.Add(@{ start = $start; end = $i; type = 'line' })
    }
    elseif ($c -eq '/' -and $i+1 -lt $n -and $text[$i+1] -eq '*') {
      $start = $i; $i += 2
      while ($i -lt $n -and -not ($text[$i] -eq '*' -and $i+1 -lt $n -and $text[$i+1] -eq '/')) { $i++ }
      $i += 2
      $spans.Add(@{ start = $start; end = $i; type = 'block' })
    }
    else { $i++ }
  }
  return $spans
}

# Comment text: line comment = content after the slashes; block comment = between /* and */
function Get-CommentText([string]$text, $span) {
  if ($span.type -eq 'line') {
    $slash = 2
    while ($slash -lt ($span.end - $span.start) -and $text[$span.start + $slash] -eq '/') { $slash++ }
    return $text.Substring($span.start + $slash, $span.end - $span.start - $slash)
  }
  return $text.Substring($span.start + 2, $span.end - $span.start - 4)
}

$sidecarRoot = Join-Path $repoRoot $SidecarDir
$langDir = Join-Path $sidecarRoot $Language
if (-not (Test-Path $langDir)) { Write-Host "[$Language] no sidecar dir $langDir (skip; Chinese originals used)"; return }

$files = Get-ChildItem $langDir -Recurse -Filter '*.cs.json'
if ($files.Count -eq 0) { Write-Host "[$Language] no sidecar files (skip)"; return }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

foreach ($f in $files) {
  $relJson = $f.FullName.Substring($langDir.Length + 1) -replace '\\', '/'
  $relSrc = $relJson -replace '\.json$', ''
  $srcPath = Join-Path $repoRoot $relSrc
  if (-not (Test-Path $srcPath)) { Write-Host "[ERROR] sidecar source file missing: $relSrc"; exit 2 }

  $side = Get-Content $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
  $src = [IO.File]::ReadAllText($srcPath, [Text.Encoding]::UTF8)
  $spans = Get-CommentSpans $src

  # Staleness guard: each sidecar zh must exactly match comment #i in the current source
  foreach ($c in $side.comments) {
    if ($c.i -ge $spans.Count) {
      Write-Host "[ERROR] $relSrc sidecar STALE: comment index $($c.i) beyond current total $($spans.Count) (source changed; regenerate sidecar)"
      exit 2
    }
    $cur = (Get-CommentText $src $spans[$c.i]).Trim()
    $exp = $c.zh.Trim()
    if ($cur -cne $exp) {
      Write-Host "[ERROR] $relSrc sidecar STALE: comment #$($c.i) changed in source"
      Write-Host "    sidecar: $exp"
      Write-Host "    current: $cur"
      exit 2
    }
  }

  # Translation field = sidecar property named after -Language (dynamic access; pure ASCII script)
  foreach ($c in $side.comments) {
    $tv = [string]$c.$Language
    if ([string]::IsNullOrWhiteSpace($tv)) {
      Write-Host "[ERROR] $relSrc sidecar missing '$Language' field on comment #$($c.i) (regenerate sidecar for $Language)"
      exit 2
    }
  }

  # Replace (descending by start offset to keep offsets valid)
  $out = $src
  $ordered = $side.comments | Sort-Object { $_.i } -Descending
  foreach ($c in $ordered) {
    $tr = [string]$c.$Language
    $span = $spans[$c.i]
    if ($span.type -eq 'line') {
      $slash = 2
      while ($slash -lt ($span.end - $span.start) -and $src[$span.start + $slash] -eq '/') { $slash++ }
      $seg = $src.Substring($span.start + $slash, $span.end - $span.start - $slash)
      $ws = ''
      $m = [regex]::Match($seg, '^\s+')
      if ($m.Success) { $ws = $m.Value }
      if ($tr -match "`r?`n") { Write-Host "[ERROR] $relSrc comment #$($c.i) translation contains newline (line-comment translation must be single-line)"; exit 2 }
      $newSeg = ('/' * $slash) + $ws + $tr
      $out = $out.Substring(0, $span.start) + $newSeg + $out.Substring($span.end)
    } else {
      $inner = $src.Substring($span.start + 2, $span.end - $span.start - 4)
      $lead = [regex]::Match($inner, '^\s*').Value
      $trail = [regex]::Match($inner, '\s*$').Value
      $newSeg = '/*' + $lead + $tr + $trail + '*/'
      $out = $out.Substring(0, $span.start) + $newSeg + $out.Substring($span.end)
    }
  }

  $dstPath = Join-Path $OutputDir $relSrc
  New-Item -ItemType Directory -Force -Path (Split-Path $dstPath -Parent) | Out-Null
  [IO.File]::WriteAllText($dstPath, $out, (New-Object Text.UTF8Encoding($false)))

  # Self-check: injected output, comments stripped, must equal source code stream
  $norm = { param($x)
    $sb = New-Object System.Text.StringBuilder; $n = $x.Length; $i = 0
    while ($i -lt $n) {
      $ch = $x[$i]
      if ($ch -eq '"' -or $ch -eq "'") {
        $q = $ch; $sb.Append($ch) | Out-Null; $i++
        while ($i -lt $n) {
          $sb.Append($x[$i]) | Out-Null
          if ($x[$i] -eq '\') { if ($i+1 -lt $n) { $sb.Append($x[$i+1]) | Out-Null; $i++ }; $i++; continue }
          if ($x[$i] -eq $q) { $i++; break }
          $i++
        }
      }
      elseif ($ch -eq '/' -and $i+1 -lt $n -and $x[$i+1] -eq '/') { while ($i -lt $n -and $x[$i] -ne "`n") { $i++ } }
      elseif ($ch -eq '/' -and $i+1 -lt $n -and $x[$i+1] -eq '*') { $i += 2; while ($i -lt $n -and -not ($x[$i] -eq '*' -and $i+1 -lt $n -and $x[$i+1] -eq '/')) { $i++ }; $i += 2 }
      else { $sb.Append($ch) | Out-Null; $i++ }
    }
    $sb.ToString().Replace("`r`n", "`n").Replace("`r", "`n")
  }
  # NOTE: never write `if (&$norm $out -cne ...)` -- & passes trailing tokens as script-block
  # args, making the condition always true. Evaluate each side first.
  $normOut = (& $norm $out)
  $normSrc = (& $norm $src)
  if ($normOut -cne $normSrc) {
    Write-Host "[ERROR] $relSrc code stream mismatch after injection (internal error)"
    exit 2
  }
  Write-Host "[$Language] injected OK: $relSrc ($($side.comments.Count) comments)"
}
