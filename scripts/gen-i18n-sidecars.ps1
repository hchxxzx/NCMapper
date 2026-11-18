#Requires -Version 5.1
# gen-i18n-sidecars.ps1 -- generate per-language comment sidecars for every .cs file embedded
# by the docs site ([!code-csharp](~/samples/...)). Formalized from temp/gen-sidecars.ps1
# (one-shot EN generator, 2026-09-30).
#
# Output: docs/i18n/<lang>/<source-relative-path>.cs.json
#   { "source": "...", "language": "<lang>", "generatedFrom": "<sha256>",
#     "comments": [ { "i": 0, "zh": "<original>", "<lang>": "<translation>" }, ... ] }
#
# ASCII ONLY in this file: Windows PowerShell 5.1 merges lines after non-ASCII comment text
# (GBK mis-read of a BOM-less .ps1). No Chinese anywhere in this file.
#
# Usage:
#   pwsh scripts/gen-i18n-sidecars.ps1 -Languages en
#   (-Languages takes ONE comma-separated token; comma splitting happens in-script, because
#    powershell.exe -File passes it as a single argv token and PS 5.1 does not split [string[]])
# LLM key: LLM_API_KEY (User scope env var). Endpoint: DashScope compatible mode, qwen-plus.
# After generation the script runs the injector self-check for each language (exit 2 on fail).
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$Languages
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $scriptDir
Set-Location $repo

$key = [Environment]::GetEnvironmentVariable('LLM_API_KEY', 'User')
if ([string]::IsNullOrWhiteSpace($key)) { $key = $env:LLM_API_KEY }
if ([string]::IsNullOrWhiteSpace($key)) { throw 'LLM_API_KEY not found (set User-scope env var or LLM_API_KEY)' }
$api = 'https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions'

# Display names for the LLM prompts (ASCII only).
$langNames = @{
  'en' = 'English'
  'de' = 'German'
  'es' = 'Spanish'
  'fr' = 'French'
  'it' = 'Italian'
  'ja' = 'Japanese'
  'ko' = 'Korean'
  'pl' = 'Polish'
  'pt-BR' = 'Brazilian Portuguese'
  'ru' = 'Russian'
  'tr' = 'Turkish'
  'vi' = 'Vietnamese'
}

function Llm([string]$system, [string]$user) {
  $body = @{ model = 'qwen-plus'; messages = @(@{ role = 'system'; content = $system }, @{ role = 'user'; content = $user }); temperature = 0.2; max_tokens = 16000 } | ConvertTo-Json -Depth 10
  $reqBytes = [Text.Encoding]::UTF8.GetBytes($body)
  $resp = Invoke-WebRequest -Uri $api -Method Post -Headers @{ Authorization = "Bearer $key" } -ContentType 'application/json; charset=utf-8' -Body $reqBytes -UseBasicParsing
  $json = [Text.Encoding]::UTF8.GetString($resp.RawContentStream.ToArray())
  $c = ($json | ConvertFrom-Json).choices[0].message.content
  if ([string]::IsNullOrWhiteSpace($c)) { throw 'empty completion' }
  return $c
}

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
    elseif ($c -eq '/' -and $i + 1 -lt $n -and $text[$i + 1] -eq '/') {
      $start = $i; $i += 2
      while ($i -lt $n -and $text[$i] -ne "`n") { $i++ }
      $spans.Add(@{ start = $start; end = $i; type = 'line' })
    }
    elseif ($c -eq '/' -and $i + 1 -lt $n -and $text[$i + 1] -eq '*') {
      $start = $i; $i += 2
      while ($i -lt $n -and -not ($text[$i] -eq '*' -and $i + 1 -lt $n -and $text[$i + 1] -eq '/')) { $i++ }
      $i += 2
      $spans.Add(@{ start = $start; end = $i; type = 'block' })
    }
    else { $i++ }
  }
  return $spans
}

function Get-CommentText([string]$text, $span) {
  if ($span.type -eq 'line') {
    $slash = 2
    while ($slash -lt ($span.end - $span.start) -and $text[$span.start + $slash] -eq '/') { $slash++ }
    return $text.Substring($span.start + $slash, $span.end - $span.start - $slash)
  }
  return $text.Substring($span.start + 2, $span.end - $span.start - 4)
}

# Auto-discover embedded files: every [!code-csharp ...](~/samples/...cs#...) referenced by the
# EN docs. Never use the docfx.json resource section (incomplete; known incident).
# Note: never use $targets += (array+scalar collapses to string concat); use List.
$targets = [System.Collections.Generic.List[string]]::new()
Get-ChildItem "$repo\docs\en-US" -Recurse -Filter '*.md' | ForEach-Object {
  $t = [IO.File]::ReadAllText($_.FullName)
  [regex]::Matches($t, 'code-csharp\[[^\]]*\]\(~/samples/([^)#\s]+)') | ForEach-Object {
    $targets.Add('samples/' + $_.Groups[1].Value)
  }
}
$targets = @($targets | Sort-Object -Unique)
"discovered $($targets.Count) embedded .cs files"

# Pre-scan: comment spans + CJK items per source file (source is shared across languages).
$srcCache = @{}
foreach ($rel in $targets) {
  $srcPath = "$repo\$($rel -replace '/', '\')"
  $src = [IO.File]::ReadAllText($srcPath, [Text.Encoding]::UTF8)
  $spans = Get-CommentSpans $src
  $items = [System.Collections.Generic.List[object]]::new()
  for ($i = 0; $i -lt $spans.Count; $i++) {
    $txt = (Get-CommentText $src $spans[$i]).Trim()
    if ($txt -match '[\u4e00-\u9fff]') { $items.Add(@($i, $txt)) }
  }
  $srcCache[$rel] = @{ sha = (Get-FileHash $srcPath -Algorithm SHA256).Hash.ToLower(); items = $items }
}
$totalItems = 0
foreach ($rel in $targets) { $totalItems += $srcCache[$rel].items.Count }
"total CJK comments across files: $totalItems"

# Split the comma-separated token here (not in the param binder): powershell.exe -File passes
# "de,es,fr" as one argv token, and PS 5.1 will not comma-split it for a [string[]] parameter,
# while repeated named parameters and bare extra tokens are both rejected.
$LangList = [System.Collections.Generic.List[string]]::new()
foreach ($tok in ($Languages -split ',')) { $t = $tok.Trim(); if ($t -ne '') { $LangList.Add($t) } }

foreach ($lang in $LangList) {
  if (-not $langNames.ContainsKey($lang)) { throw "unsupported language: $lang (known: $($langNames.Keys -join ', '))" }
  $langName = $langNames[$lang]
  $outRoot = "$repo\docs\i18n\$lang"
  "=== LANGUAGE $lang ($langName) ==="

  # Target-script gate: ja output legitimately contains CJK (kanji) -- the zero-CJK gate is
  # ONLY valid for non-CJK-script targets. For ja, guard against pass-through (translation
  # byte-identical to the zh source) instead. 2026-10-01 incident: uniform CJK gate rejected
  # every ja translation (3 retries + per-item fallback -> hard fail).
  $jaLike = ($lang -in @('ja'))
  if ($jaLike) {
    # Concrete example built from char codes (keeps this file ASCII). 数量 -> 数え
    $exZh = [string][char]0x6570 + [char]0x91CF
    $exJa = [string][char]0x6570 + [char]0x307E
    $zeroLangNote = "Japanese use of kanji is normal and expected -- do NOT avoid CJK characters. Every comment must be a genuine Japanese translation, never a copy of the Chinese text. " +
                    "Short Chinese words inside doc tags must be translated too: for example <summary>$exZh</summary> must become <summary>$exJa</summary> -- returning the Chinese word unchanged is a failure."
  }
  else {
    $zeroLangNote = "Every output string must contain ZERO Chinese/CJK characters -- translate every term, including technical ones; never keep a Chinese word 'for flavor'."
  }
  $sysPrompt = "You translate C# comments from Chinese to $langName. $zeroLangNote Keep code identifiers, attributes and type names verbatim. Output a JSON array of strings only. No markdown fences."

  $written = 0; $skipped = 0
  foreach ($rel in $targets) {
    $info = $srcCache[$rel]
    $items = $info.items
    $full = "$outRoot\$($rel -replace '/', '\')"
    $fullJson = "$full.json"
    if (Test-Path $fullJson) {
      $prev = Get-Content $fullJson -Raw -Encoding UTF8 | ConvertFrom-Json
      if ($prev.generatedFrom -eq $info.sha) { $skipped++; continue }
    }
    if ($items.Count -eq 0) {
      # No CJK comments: write an empty sidecar so the language set is complete (optional; keeps counts aligned)
      if (-not (Test-Path $fullJson)) {
        $dir = Split-Path $full -Parent
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        $empty = [pscustomobject]@{ source = $rel; language = $lang; generatedFrom = $info.sha; comments = @() }
        [IO.File]::WriteAllText($fullJson, ($empty | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
        $skipped++
      } else { $skipped++ }
      continue
    }
    "  --- $rel : $($items.Count) CJK comments"

    # Chunked translation (max 25/chunk). Count / zero-CJK / single-line checks run INSIDE the
    # retry loop (3 attempts); then per-item fallback for stubborn chunks.
    $translations = [System.Collections.Generic.List[string]]::new()
    $chunkSize = 25
    for ($c0 = 0; $c0 -lt $items.Count; $c0 += $chunkSize) {
      $chunk = @($items | Select-Object -Skip $c0 -First $chunkSize)
      $k = $chunk.Count
      $lines = [System.Collections.Generic.List[string]]::new()
      for ($x = 0; $x -lt $k; $x++) { $lines.Add("$($x + 1). $($chunk[$x][1])") }
      $userPrompt = "Translate each numbered C# comment from Chinese to $langName. Keep the technical meaning precise. " +
                    "Chinese text inside XML doc tags (<summary>, <param>, <returns>, <exception>) must be translated too -- only the tag structure stays. " +
                    "Return ONLY a JSON array of $k strings in the same order, nothing else. Inside each JSON string, write a backslash before every inner double quote."
      $chunkResult = $null
      for ($attempt = 1; $attempt -le 3 -and $null -eq $chunkResult; $attempt++) {
        $raw = Llm $sysPrompt ($userPrompt + "`n" + ($lines -join "`n"))
        $t = $raw.Trim()
        if ($t.StartsWith('```')) {
          $nl = $t.IndexOf("`n"); if ($nl -ge 0) { $t = $t.Substring($nl + 1) }
          if ($t.TrimEnd().EndsWith('```')) { $t = $t.TrimEnd().Substring(0, $t.TrimEnd().Length - 3) }
        }
        $s = $t.IndexOf('['); $e = $t.LastIndexOf(']')
        if ($s -ge 0 -and $e -gt $s) {
          try {
            $arr = $t.Substring($s, $e - $s + 1) | ConvertFrom-Json
            if ($arr.Count -eq $k) {
              $ok = $true
              for ($j = 0; $j -lt $arr.Count; $j++) {
                $tv = [string]$arr[$j]
                if ([string]::IsNullOrWhiteSpace($tv)) { "    chunk $c0 attempt [$attempt] empty item $j"; $ok = $false; break }
                if (-not $jaLike -and $tv -match '[\u4e00-\u9fff]') { "    chunk $c0 attempt [$attempt] CJK residue in item $j = $tv"; $ok = $false; break }
                if ($jaLike -and $tv -ceq $chunk[$j][1]) { "    chunk $c0 attempt [$attempt] pass-through (identical to zh) item $j"; $ok = $false; break }
                if ($tv -match "`r?`n") { "    chunk $c0 attempt [$attempt] multiline item $j"; $ok = $false; break }
              }
              if ($ok) { $chunkResult = @($arr) }
            }
            else { "    chunk $c0 attempt [$attempt] count $($arr.Count) != $k" }
          }
          catch { "    chunk $c0 attempt [$attempt] JSON parse = $($_.Exception.Message)" }
        }
        else { "    chunk $c0 attempt [$attempt] no JSON array found" }
      }
      if ($null -eq $chunkResult) {
        # Per-item fallback (resistant terms), 3 attempts each
        Write-Host "    chunk $c0 failed x3 -> per-item fallback"
        for ($x = 0; $x -lt $chunk.Count; $x++) {
          $item = $chunk[$x]
          $one = $null
          for ($attempt = 1; $attempt -le 3 -and $null -eq $one; $attempt++) {
            $onePrompt = "Translate this single C# comment from Chinese to $langName. It must be a genuine $langName translation -- do NOT copy the Chinese text; short Chinese words must be translated too.`n" +
                         "Comment: $($item[1])`nReturn ONLY a single JSON string."
            $raw = Llm $sysPrompt $onePrompt
            $t2 = $raw.Trim()
            if ($t2.StartsWith('```')) { $nl = $t2.IndexOf("`n"); if ($nl -ge 0) { $t2 = $t2.Substring($nl + 1) }; if ($t2.TrimEnd().EndsWith('```')) { $t2 = $t2.TrimEnd().Substring(0, $t2.TrimEnd().Length - 3) } }
            # Parse with ConvertFrom-Json so \uXXXX escapes (common in CJK responses) unescape
            # correctly; fall back to lenient extraction (first..last quote) when the model left
            # inner quotes unescaped (e.g. <param name="x"> inside the JSON string); and when the
            # model returned a bare unquoted string, accept the whole text as the value.
            $tv = $null
            try {
              $p = $t2 | ConvertFrom-Json
              if ($p -is [string]) { $tv = $p }
            }
            catch { $tv = $null }
            if ($null -eq $tv) {
              $s = $t2.IndexOf('"'); $e = $t2.LastIndexOf('"')
              if ($s -ge 0 -and $e -gt $s) {
                $tv = $t2.Substring($s + 1, $e - $s - 1).Replace('\"', '"').Replace('\n', ' ').Replace('\t', ' ')
              }
              elseif ($t2.IndexOf('"') -lt 0) {
                $tv = $t2.Replace("`r", ' ')
              }
            }
            if ($null -ne $tv -and ($tv -is [string])) {
              $tv = [string]$tv
              $okOne = (-not [string]::IsNullOrWhiteSpace($tv)) -and ($tv -notmatch "`r?`n")
              if (-not $jaLike) { $okOne = $okOne -and ($tv -notmatch '[\u4e00-\u9fff]') }
              else { $okOne = $okOne -and ($tv -cne $item[1]) }
              if ($okOne) { $one = $tv }
              else {
                $why = 'gate'
                if ($tv -match "`r?`n") { $why = 'multiline' }
                if ($jaLike -and $tv -ceq $item[1]) { $why = 'pass-through' }
                if (-not $jaLike -and $tv -match '[\u4e00-\u9fff]') { $why = 'cjk-residue' }
                $head = $tv.Substring(0, [Math]::Min(50, $tv.Length)).Replace("`r", ' ').Replace("`n", ' ')
                Write-Host "    per-item $x attempt [$attempt] FAIL[$why] head=$head"
              }
            }
            else {
              $head = $t2.Substring(0, [Math]::Min(60, $t2.Length)).Replace("`r", ' ').Replace("`n", ' ')
              Write-Host "    per-item $x attempt [$attempt] FAIL[parse] head=$head"
            }
          }
          if ($null -eq $one) { throw "translation failed for $lang / $rel chunk $c0 item $x (per-item)" }
          $translations.Add($one)
        }
        continue
      }
      foreach ($v in $chunkResult) { $translations.Add([string]$v) }
    }

    # Write sidecar; translation field name == language code. Post-write disk verify.
    $dir = Split-Path $full -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $commentObjs = @()
    for ($j = 0; $j -lt $items.Count; $j++) {
      $o = @{ i = $items[$j][0]; zh = $items[$j][1] }
      $o[$lang] = $translations[$j]
      $commentObjs += [pscustomobject]$o
    }
    $side = [pscustomobject]@{ source = $rel; language = $lang; generatedFrom = $info.sha; comments = $commentObjs }
    [IO.File]::WriteAllText($fullJson, ($side | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    if (-not (Test-Path $fullJson)) { throw "sidecar write failed: $fullJson" }
    "    sidecar written: $fullJson ($($items.Count) entries)"
    $written++
  }
  "=== LANGUAGE $lang done: $written written, $skipped skipped/up-to-date ==="

  # Injector self-check for this language (staleness guard + code stream identity).
  # Run as a native powershell process: a child .ps1's `exit N` does NOT propagate into
  # $LASTEXITCODE under PS 5.1 `&` invocation (stays empty -> false failure), only native
  # exe exit codes are reliable.
  $overlayDir = "$repo\temp\$lang-demo-overlay"
  Remove-Item -Recurse -Force $overlayDir -ErrorAction SilentlyContinue
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = "powershell.exe"
  # -SidecarDir must be RELATIVE (the injector Join-Path's it onto its own repo root; PS 5.1
  # Join-Path does not honor rooted child paths and would concatenate them).
  $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$repo\scripts\inject-demo-comments.ps1`" -Language $lang -SidecarDir docs/i18n -OutputDir `"$overlayDir`""
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $p = [System.Diagnostics.Process]::Start($psi)
  $jOut = $p.StandardOutput.ReadToEnd()
  $jErr = $p.StandardError.ReadToEnd()
  $p.WaitForExit()
  foreach ($jl in ($jOut -split "`r?`n")) { if ($jl -ne '') { Write-Host "  $jl" } }
  if ($p.ExitCode -ne 0) {
    foreach ($jl in ($jErr -split "`r?`n")) { if ($jl -ne '') { Write-Host "  $jl" } }
    throw "injector self-check failed for $lang (exit $($p.ExitCode))"
  }
}
"ALL DONE"
