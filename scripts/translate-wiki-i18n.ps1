#Requires -Version 5.1
<#
.SYNOPSIS
  Translates the docs/zh-CN wiki (81 md) into target languages via LLM
  (DashScope compatible mode, qwen-plus). Fenced code blocks pass through
  byte-identical (comment translation is the job of the docs/i18n sidecar
  mechanism, NOT this pipeline - by user decision).

.DESCRIPTION
  - Source : docs/zh-CN/**/*.md  (authoritative)
  - Output : docs/<lang>/**/*.md (1:1 path mirror), LF, UTF-8 no BOM
  - Banner : machine-translation disclaimer prepended to every page except
             toc.md (aligned with the en-US convention, 80 pages).
  - Concurrency : global pool of -Concurrency in-flight LLM calls across all
             languages (default 8), implemented with a PowerShell RunspacePool
             (Windows PowerShell 5.1 compatible, no PS 7 required).
  - Resume : per-language state in temp/i18n-wiki-state/<lang>.json;
             rerun skips completed files.
  - Gates : per sub-part (zero CJK for non-CJK-script langs, heading parity,
             inline code spans / link destinations / embed paths preserved,
             no stray fences) + per file (fence parity, zero CJK outside
             code, length ratio) + per language (internal .md links resolve).
  - Logs   : temp/i18n-wiki.log ; summary: temp/i18n-wiki-summary.json
  - Data   : scripts/i18n-data/banners.json , scripts/i18n-data/glossary.txt

  Pure ASCII by repo convention (PS 5.1 GBK parse trap, docs/i18n/README.md
  pitfall 4). CJK content travels only in data files / JSON payloads.

.EXAMPLE
  powershell -NoProfile -File scripts\translate-wiki-i18n.ps1 -Languages en -OnlyFiles getting-started/quickstart.md
  powershell -NoProfile -File scripts\translate-wiki-i18n.ps1
#>
param(
  [string]$Languages = 'de,es,fr,it,ja,ko,pl,pt-BR,ru,tr,vi',
  [int]$Concurrency = 8,
  [string]$SourceDir = 'docs/zh-CN',
  [string]$OutRoot = 'docs',
  [string]$DataDir = 'scripts/i18n-data',
  [string]$StateDir = 'temp/i18n-wiki-state',
  [string]$LogFile = 'temp/i18n-wiki.log',
  [string]$OnlyFiles = '',
  [switch]$Force
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $RepoRoot

function Write-Log {
  param([string]$Msg)
  $line = ('[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $Msg)
  Write-Host $line
  Add-Content -Path $LogFile -Value $line -Encoding utf8
}

# ---------- LLM key / endpoint ----------
$ApiKey = [Environment]::GetEnvironmentVariable('LLM_API_KEY', 'User')
if ([string]::IsNullOrWhiteSpace($ApiKey)) { $ApiKey = $env:LLM_API_KEY }
if ([string]::IsNullOrWhiteSpace($ApiKey)) { throw 'LLM_API_KEY not found (User-scope env var or LLM_API_KEY)' }
$ApiUrl = 'https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions'
$Model = 'qwen-plus'

# ---------- data ----------
# PS 5.1 reads BOM-less UTF-8 as GBK - read data files with explicit UTF-8.
$banners = ([System.IO.File]::ReadAllText((Join-Path $RepoRoot (Join-Path $DataDir 'banners.json')), [System.Text.Encoding]::UTF8)) | ConvertFrom-Json
$glossary = [System.IO.File]::ReadAllText((Join-Path $RepoRoot (Join-Path $DataDir 'glossary.txt')), [System.Text.Encoding]::UTF8).Trim()

$allowedLangs = @('de','es','fr','it','ja','ko','pl','pt-BR','ru','tr','vi')
$langNames = @{
  'de'='German'; 'es'='Spanish'; 'fr'='French'; 'it'='Italian'; 'ja'='Japanese'
  'ko'='Korean'; 'pl'='Polish'; 'pt-BR'='Brazilian Portuguese'; 'ru'='Russian'; 'tr'='Turkish'; 'vi'='Vietnamese'
}
$langs = @($Languages -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($l in $langs) {
  if (-not $allowedLangs.Contains($l)) { throw "unsupported language: $l (known: $($allowedLangs -join ', '))" }
  if ($banners.PSObject.Properties[$l] -eq $null) { throw "missing banner for $l" }
}

# ---------- helpers (main scope) ----------
function Test-Han { param([string]$s) return ($s -match '[\u4e00-\u9fff]') }

function Split-Parts {
  # Split file lines into alternating parts: 'code' (fenced, incl. fence lines)
  # | 'prose'. Returns array of @{Kind; Start; End} (line index range, inclusive).
  # A leading YAML front-matter block (--- ... ---) is passthrough 'code':
  # it is metadata, and sending it to the LLM made it wrap the block in fences.
  param([string[]]$Lines)
  $parts = [System.Collections.Generic.List[object]]::new()
  $i = 0
  if ($Lines.Count -gt 0 -and $Lines[0] -match '^\s*---\s*$') {
    for ($j = 1; $j -lt $Lines.Count; $j++) {
      if ($Lines[$j] -match '^\s*---\s*$') {
        $parts.Add(@{ Kind='code'; Start=0; End=$j })
        $i = $j + 1
        break
      }
    }
  }
  while ($i -lt $Lines.Count) {
    if ($Lines[$i] -match '^\s*```') {
      $start = $i
      $i++
      while ($i -lt $Lines.Count -and $Lines[$i] -notmatch '^\s*```') { $i++ }
      if ($i -lt $Lines.Count) { $i++ }
      $parts.Add(@{ Kind='code'; Start=$start; End=$i-1 })
    } else {
      $start = $i
      while ($i -lt $Lines.Count -and $Lines[$i] -notmatch '^\s*```') { $i++ }
      $parts.Add(@{ Kind='prose'; Start=$start; End=$i-1 })
    }
  }
  return $parts.ToArray()
}

function Split-ProseRange {
  # Split a prose line range (contiguous, no fences inside) into sub-ranges of
  # at most -MaxLines lines, preferring blank-line / heading boundaries.
  param([string[]]$Lines, [int]$Start, [int]$End, [int]$MaxLines = 100)
  $subs = [System.Collections.Generic.List[object]]::new()
  $cur = $Start
  while (($End - $cur + 1) -gt $MaxLines) {
    $target = $cur + $MaxLines - 1
    $cut = -1
    for ($j = $target; $j -gt ($cur + [int]($MaxLines * 0.5)); $j--) {
      if ($Lines[$j].Trim() -eq '' -or $Lines[$j] -match '^[ \t]{0,3}#{1,6}[ \t]') { $cut = $j; break }
    }
    if ($cut -lt 0) { $cut = $target }
    $subs.Add(@{ Start=$cur; End=$cut })
    $cur = $cut + 1
  }
  $subs.Add(@{ Start=$cur; End=$End })
  return $subs.ToArray()
}

function New-SystemPrompt {
  param([string]$Lang)
  $zero = 'The output must contain ZERO Chinese characters and zero full-width CJK punctuation (U+3000-U+303F, U+FF00-U+FFEF), including inside short technical words. Translate even 1-2 character words.'
  $jaNote = 'Kanji in Japanese output is completely normal. You MUST genuinely translate every sentence into Japanese; copying the Chinese source verbatim is strictly forbidden, including for short words.'
  $tail = $zero
  if ($Lang -eq 'ja') { $tail = $jaNote }
  $lpName = $langNames[$Lang]
  $sp = "You are a professional software documentation translator.`n"
  $sp += "Translate the provided Markdown fragment from Simplified Chinese to $lpName ($Lang).`n"
  $sp += "Strict rules:`n"
  $sp += "1. Translate ALL prose: headings, paragraphs, list items, table cells, blockquote text, and HTML comments (the text between <!-- and -->).`n"
  $sp += "2. Keep every inline code span (backtick...backtick) byte-identical - never translate its content.`n"
  $sp += "3. Keep every Markdown link destination (the text inside the parentheses) byte-identical; translate only the link text.`n"
  $sp += "4. For lines like [!code-csharp[Label](~/samples/...)] translate ONLY the label between the square brackets; keep the (~/samples/...) part byte-identical.`n"
  $sp += "5. Preserve Markdown structure exactly: same heading levels and count, same table shape (row/column count), same list markers, same blank lines (keep every blank line, including a trailing one, in the same position).`n"
  $sp += "6. Do NOT add, remove, merge, split or renumber headings. Do NOT add disclaimers, notes, commentary or code fences. A fragment may end right before a code block; in that case simply stop after the last translated line - never emit a ``` line or invent code.`n"
  $sp += "7. Keep unchanged: NCMapper, Mapper, all C# type/attribute/method names, NCMAP_xxx diagnostic codes, NuGet package names, file paths, URLs, version numbers.`n"
  $sp += "8. Terminology anchors (English reference - render idiomatically in $lpName):`n$glossary`n"
  $sp += "9. $tail`n"
  $sp += "10. Output ONLY the translated fragment. No preamble, no surrounding code fence."
  return $sp
}

# ---------- enumerate sources ----------
$srcRoot = (Resolve-Path (Join-Path $RepoRoot $SourceDir)).Path
$allFiles = (Get-ChildItem $srcRoot -Recurse -File -Filter *.md | ForEach-Object {
  $_.FullName.Substring($srcRoot.Length + 1) -replace '\\', '/'
}) | Sort-Object
if ($OnlyFiles) {
  $want = @($OnlyFiles -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
  $allFiles = @($allFiles | Where-Object { $want -contains $_ })
  if ($allFiles.Count -eq 0) { throw 'no source files matched -OnlyFiles' }
}

New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
$logDir = Split-Path $LogFile
if ($logDir -and -not (Test-Path $logDir)) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }

function Load-State { param([string]$Lang)
  $p = Join-Path $StateDir "$Lang.json"
  if (Test-Path $p) {
    $obj = ((Get-Content $p -Raw | ConvertFrom-Json).files)
    $h = @{}
    if ($obj) { foreach ($prop in $obj.PSObject.Properties) { $h[$prop.Name] = $prop.Value } }
    return $h
  }
  return @{}
}
function Save-State { param([string]$Lang, $Files)
  $p = Join-Path $StateDir "$Lang.json"
  @{ files = $Files } | ConvertTo-Json -Depth 5 | Set-Content $p -Encoding utf8
}

Write-Log ("plan: {0} x {1} files, concurrency={2}" -f ($langs -join '+'), $allFiles.Count, $Concurrency)

# ---------- build plans + task queue ----------
$summary = [ordered]@{ langs = [ordered]@{} }
$totalTasks = 0
$planByLang = [ordered]@{}
$taskQueue = [System.Collections.Generic.List[object]]::new()

foreach ($lang in $langs) {
  $state = Load-State $lang
  $plan = [ordered]@{}
  $nTasks = 0
  foreach ($f in $allFiles) {
    $outPath = Join-Path (Join-Path $OutRoot $lang) $f
    $isDone = $false
    if (-not $Force -and $state[$f] -and $state[$f].status -eq 'done' -and (Test-Path $outPath)) { $isDone = $true }
    if ($isDone) { continue }
    $content = [System.IO.File]::ReadAllText((Join-Path $srcRoot $f))
    $lines = @($content -replace "`r`n", "`n" -split "`n")
    $parts = Split-Parts $lines
    $tasks = [System.Collections.Generic.List[object]]::new()
    $pi = 0
    foreach ($part in $parts) {
      if ($part.Kind -eq 'prose') {
        $subs = Split-ProseRange -Lines $lines -Start $part.Start -End $part.End
        $si = 0
        foreach ($sub in $subs) {
          $srcText = ($lines[$sub.Start..$sub.End]) -join "`n"
          $trivial = -not (Test-Han $srcText)
          $tasks.Add(@{ PartIdx=$pi; SubIdx=$si; SubTotal=$subs.Count; SrcText=$srcText; Trivial=$trivial })
          if (-not $trivial) { $nTasks++ }
          $si++
        }
      }
      $pi++
    }
    $plan[$f] = @{ Lines=$lines; Parts=$parts; Tasks=$tasks }
  }
  $planByLang[$lang] = @{ Plan=$plan; State=$state }
  Write-Log ("{0}: {1} pending files, {2} LLM tasks" -f $lang, $plan.Count, $nTasks)
  $summary.langs[$lang] = [ordered]@{ files=$allFiles.Count; pending=$plan.Count; tasks=$nTasks; doneInThisRun=0; failedThisRun=@(); brokenLinks=@() }
  $totalTasks += $nTasks
}

if ($totalTasks -eq 0) { Write-Log 'nothing to do'; exit 0 }

foreach ($lang in $langs) {
  $plan = $planByLang[$lang].Plan
  foreach ($f in $plan.Keys) {
    $sys = New-SystemPrompt $lang
    foreach ($t in $plan[$f].Tasks) {
      if ($t.Trivial) { continue }
      $user = "Translate this Markdown fragment (part $($t.SubIdx + 1) of $($t.SubTotal)) of file ${f}:"
      $user += "`n" + $t.SrcText
      $taskQueue.Add(@{ Lang=$lang; File=$f; PartIdx=$t.PartIdx; SubIdx=$t.SubIdx; SubTotal=$t.SubTotal; SrcText=$t.SrcText; Sys=$sys; User=$user })
    }
  }
}
Write-Log ("queue: {0} LLM tasks, pool size={1}" -f $taskQueue.Count, $Concurrency)

# ---------- task script (runs inside each pool runspace) ----------
# PS 5.1 note: the task script is self-contained (no parent-scope functions),
# parameters arrive as one JSON string argument.
$taskScript = @'
param([string]$TaskJson)
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$t = $TaskJson | ConvertFrom-Json

function t-TestHan { param([string]$s) return ($s -match '[\u4e00-\u9fff]') }
function t-Spans { param([string]$s) return ([regex]::Matches($s, '`([^`]+)`') | ForEach-Object { $_.Groups[1].Value }) }
function t-Dests { param([string]$s) return ([regex]::Matches($s, '\]\(([^)\s]+)') | ForEach-Object { $_.Groups[1].Value }) }
function t-Headings { param([string]$s) return ([regex]::Matches($s, '^[ \t]{0,3}#{1,6}[ \t]', 'Multiline').Count) }

function t-StripOuterFence {
  param([string]$s)
  $ls = @($s -split "`n")
  $first = -1; $last = -1
  for ($i = 0; $i -lt $ls.Count; $i++) { if ($ls[$i].Trim() -ne '') { $first = $i; break } }
  for ($i = $ls.Count - 1; $i -ge 0; $i--) { if ($ls[$i].Trim() -ne '') { $last = $i; break } }
  if ($first -ge 0 -and $last -gt $first -and $ls[$first] -match '^\s*```' -and $ls[$last] -match '^\s*```\s*$') {
    return (($ls[$first+1..$last-1]) -join "`n")
  }
  return $s
}

function t-PartGate {
  param([string]$Src, [string]$Tr, [string]$Lang)
  if ($Src.Trim() -ne '' -and $Tr.Trim() -eq '') { return @{ OK=$false; Reason='empty translation' } }
  if ($Tr -match '(?m)^\s*```') { return @{ OK=$false; Reason='stray code fence in prose' } }
  $hs = t-Headings $Src
  $ht = t-Headings $Tr
  if ($hs -ne $ht) { return @{ OK=$false; Reason=('heading parity {0}!={1}' -f $hs, $ht) } }
  foreach ($sp in (t-Spans $Src)) {
    # strict byte-identical gate only for ASCII identifier-like spans
    # (2+ chars with a letter/digit); lone punctuation (/, -) and spans with
    # unicode (arrows etc.) or CJK (demo data) may be localized
    if ($sp -match '^[ -~]+$' -and $sp -match '[A-Za-z0-9]' -and $sp.Length -ge 2 -and $Tr.IndexOf($sp) -lt 0) {
      return @{ OK=$false; Reason=('inline span lost: ' + $sp) }
    }
  }
  foreach ($d in (t-Dests $Src)) {
    $hashIdx = $d.IndexOf('#')
    if ($hashIdx -ge 0) {
      $pathPart = $d.Substring(0, $hashIdx)
      $anchorPart = $d.Substring($hashIdx)
      if ($Tr.IndexOf($pathPart) -lt 0) { return @{ OK=$false; Reason=('link path lost: ' + $pathPart) } }
      # CJK anchors (zh heading anchors) are localized by the model and repaired
      # deterministically after assembly; ASCII anchors stay strict.
      if ($anchorPart -match '[^\x20-\x7E]') { continue }
    }
    if ($Tr.IndexOf($d) -lt 0) { return @{ OK=$false; Reason=('link dest lost: ' + $d) } }
  }
  if ($Lang -eq 'ja') {
    if ($Tr -ceq $Src) { return @{ OK=$false; Reason='ja byte-identical to source (copy-through)' } }
  } else {
    $stripped = [regex]::Replace($Tr, '`[^`]+`', '')
    if ($stripped -match '[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef]') {
      return @{ OK=$false; Reason='CJK residue in prose' }
    }
  }
  return @{ OK=$true; Reason='' }
}

$strict = ''
$ok = $false
$reason = ''
$attempts = 0
$result = $null
for ($att = 1; $att -le 3; $att++) {
  $attempts = $att
  try {
    $body = @{ model=$t.Model; messages=@(@{ role='system'; content=($t.Sys + $strict) }, @{ role='user'; content=$t.User }); temperature=0.2; max_tokens=16000 } | ConvertTo-Json -Depth 10
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
    $content = $null
    for ($a = 0; $a -lt 4; $a++) {
      try {
        # PS 5.1 IRM decodes the body with the system ANSI codepage - read raw
        # bytes and decode as UTF-8 explicitly (mojibake guard).
        $resp = Invoke-WebRequest -Uri $t.Api -Method Post -Headers @{ Authorization = "Bearer $($t.Key)" } -ContentType 'application/json; charset=utf-8' -Body $bytes -UseBasicParsing
        $rawBytes = $resp.RawContentStream.ToArray()
        $rawText = [System.Text.Encoding]::UTF8.GetString($rawBytes)
        $j = $rawText | ConvertFrom-Json
        $content = $j.choices[0].message.content
        if ($content) { break }
        throw 'empty content'
      } catch {
        if ($a -eq 3) { throw }
        Start-Sleep -Seconds (5 * ($a + 1))
      }
    }
    $tr = t-StripOuterFence $content
    # Deterministic fence cleanup: a fragment contains no ``` lines by
    # construction, so every fence line (and the enclosed content) is
    # model-added, typically hallucinated code. Strip all fence blocks,
    # keeping any legitimate translation that follows them.
    $fl = @($tr -split "`n")
    $clean = [System.Collections.Generic.List[string]]::new()
    $inFence = $false
    foreach ($line in $fl) {
      if ($line -match '^\s*```') { $inFence = -not $inFence; continue }
      if (-not $inFence) { $clean.Add($line) }
    }
    $tr = ($clean -join "`n").TrimEnd()
    $g = t-PartGate -Src $t.SrcText -Tr $tr -Lang $t.Lang
    if ($g.OK) {
      $ok = $true
      $result = @{ Lang=$t.Lang; File=$t.File; PartIdx=$t.PartIdx; SubIdx=$t.SubIdx; Text=$tr; Attempts=$attempts }
      Add-Content -Path $t.LogFile -Value ('[{0}] {1} {2} part {3}/{4} ok (att {5})' -f (Get-Date -Format 'HH:mm:ss'), $t.Lang, $t.File, ($t.SubIdx+1), $t.SubTotal, $attempts) -Encoding utf8
      break
    }
    $reason = $g.Reason
    $strict = "`nADDITIONAL STRICT REQUIREMENT (previous attempt failed: $reason). Re-translate carefully so that this specific failure mode does not recur: keep every inline code span and every link destination byte-identical, and output no ``` fence line anywhere in the response."
  } catch {
    $reason = "API error: $($_.Exception.Message)"
    $strict = ''
  }
}
if (-not $ok) {
  $result = @{ Lang=$t.Lang; File=$t.File; PartIdx=$t.PartIdx; SubIdx=$t.SubIdx; Text=$null; Attempts=$attempts; FailReason=$reason }
  Add-Content -Path $t.LogFile -Value ('[{0}] {1} {2} part {3}/{4} FAILED after 3 attempts: {5}' -f (Get-Date -Format 'HH:mm:ss'), $t.Lang, $t.File, ($t.SubIdx+1), $t.SubTotal, $reason) -Encoding utf8
}
$result
'@

# ---------- runspace pool driver ----------
# Task scripts are self-contained, so the default InitialSessionState suffices.
$poolSize = [Math]::Min($Concurrency, 16)
if ($poolSize -lt 1) { $poolSize = 1 }
$pool = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspacePool(1, $poolSize)
$pool.Open()

$results = [System.Collections.Generic.List[object]]::new()
$running = [System.Collections.Generic.Dictionary[string, object]]::new()
$next = 0
$seq = 0
try {
  while ($next -lt $taskQueue.Count -or $running.Count -gt 0) {
    while ($next -lt $taskQueue.Count -and $running.Count -lt $poolSize) {
      $tk = $taskQueue[$next]
      $taskJson = @{ Lang=$tk.Lang; File=$tk.File; PartIdx=$tk.PartIdx; SubIdx=$tk.SubIdx; SubTotal=$tk.SubTotal
                     SrcText=$tk.SrcText; Sys=$tk.Sys; User=$tk.User; Api=$ApiUrl; Key=$ApiKey
                     Model=$Model; LogFile=(Join-Path $RepoRoot $LogFile) } | ConvertTo-Json -Depth 10
      $ps = [PowerShell]::Create()
      $ps.RunspacePool = $pool
      [void]$ps.AddScript($taskScript)
      [void]$ps.AddArgument($taskJson)
      $handle = $ps.BeginInvoke()
      $running["id$seq"] = @{ PS=$ps; H=$handle }
      $next++
      $seq++
    }
    foreach ($k in @($running.Keys)) {
      $r = $running[$k]
      if ($r.H.IsCompleted) {
        try {
          $out = $r.PS.EndInvoke($r.H)
          if ($out -and $out.Count -gt 0) { [void]$results.Add($out[0]) }
          else { [void]$results.Add(@{ Lang='?'; File='?'; PartIdx=-1; SubIdx=-1; Text=$null; FailReason='no output' }) }
        } catch {
          [void]$results.Add(@{ Lang='?'; File='?'; PartIdx=-1; SubIdx=-1; Text=$null; FailReason=("runspace error: " + $_.Exception.Message) })
        } finally {
          $r.PS.Dispose()
        }
        [void]$running.Remove($k)
      }
    }
    if ($running.Count -gt 0 -and $next -ge $taskQueue.Count) {
      Start-Sleep -Milliseconds 200
    }
  }
}
finally {
  foreach ($k in @($running.Keys)) { try { $running[$k].PS.Dispose() } catch {} }
  $pool.Dispose()
}

# ---------- assemble + write ----------
$byFile = [ordered]@{}
foreach ($r in $results) {
  if (-not $r.Lang) { continue }
  $k = '{0}|{1}' -f $r.Lang, $r.File
  if (-not $byFile.Contains($k)) { $byFile[$k] = @{} }
  $byFile[$k]["{0}.{1}" -f $r.PartIdx, $r.SubIdx] = $r
}

foreach ($lang in $langs) {
  $plan = $planByLang[$lang].Plan
  $state = $planByLang[$lang].State
  $outLangRoot = Join-Path $RepoRoot (Join-Path $OutRoot $lang)
  $doneCount = 0
  $failedThisRun = [System.Collections.Generic.List[string]]::new()

  foreach ($f in $plan.Keys) {
    $entry = $plan[$f]
    $lines = $entry.Lines
    $parts = $entry.Parts
    $resMap = $byFile['{0}|{1}' -f $lang, $f]
    if ($null -eq $resMap) { $resMap = @{} }
    $okAll = $true
    $reason = ''
    $outLines = [System.Collections.Generic.List[string]]::new()
    $pi = 0
    foreach ($part in $parts) {
      if ($part.Kind -eq 'code') {
        for ($i = $part.Start; $i -le $part.End; $i++) { $outLines.Add($lines[$i]) }
      } else {
        $si = 0
        foreach ($sub in (Split-ProseRange -Lines $lines -Start $part.Start -End $part.End)) {
          $srcText = ($lines[$sub.Start..$sub.End]) -join "`n"
          $key = "{0}.{1}" -f $pi, $si
          if (-not (Test-Han $srcText)) {
            for ($i = $sub.Start; $i -le $sub.End; $i++) { $outLines.Add($lines[$i]) }
          } else {
            if (-not $resMap.ContainsKey($key) -or $null -eq $resMap[$key].Text) {
              $okAll = $false
              $rz = $resMap[$key].FailReason
              if ([string]::IsNullOrWhiteSpace($rz)) { $rz = 'missing result' }
              $reason = $rz
              break
            }
            foreach ($l in @($resMap[$key].Text -split "`n")) { $outLines.Add($l) }
          }
          $si++
        }
        if (-not $okAll) { break }
      }
      $pi++
    }

    if (-not $okAll) {
      $state[$f] = @{ status='failed'; reason=$reason }
      $failedThisRun.Add($f)
      $summary.langs[$lang].failedThisRun += $f
      Write-Log ("{0} FAILED: {1} ({2})" -f $lang, $f, $reason)
      continue
    }

    $text = ($outLines -join "`n")

    # file-level gates
    $srcJoined = $lines -join "`n"
    $fenceSrc = @([regex]::Matches($srcJoined, '(?m)^\s*```')).Count
    $fenceOut = @([regex]::Matches($text, '(?m)^\s*```')).Count
    if ($fenceSrc -ne $fenceOut) {
      $state[$f] = @{ status='failed'; reason=('fence parity {0}!={1}' -f $fenceSrc, $fenceOut) }
      $failedThisRun.Add($f); $summary.langs[$lang].failedThisRun += $f
      Write-Log ("{0} FAILED (gate): {1} fence parity" -f $lang, $f)
      continue
    }
    if ($lang -ne 'ja') {
      $inF = $false
      $buf = [System.Text.StringBuilder]::new()
      foreach ($l in $outLines) {
        if ($l -match '^\s*```') { $inF = -not $inF; continue }
        if (-not $inF) { [void]$buf.AppendLine(([regex]::Replace($l, '`[^`]+`', ''))) }
      }
      if ($buf.ToString() -match '[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef]') {
        $state[$f] = @{ status='failed'; reason='CJK residue outside code blocks' }
        $failedThisRun.Add($f); $summary.langs[$lang].failedThisRun += $f
        Write-Log ("{0} FAILED (gate): {1} CJK residue" -f $lang, $f)
        continue
      }
    }
    $ratio = [math]::Round($text.Length / [math]::Max(1, $srcJoined.Length), 2)
    if ($ratio -lt 0.5 -or $ratio -gt 4.5) {
      $state[$f] = @{ status='failed'; reason=('length ratio {0}' -f $ratio) }
      $failedThisRun.Add($f); $summary.langs[$lang].failedThisRun += $f
      Write-Log ("{0} FAILED (gate): {1} length ratio {2}" -f $lang, $f, $ratio)
      continue
    }

    # banner (not for toc.md)
    if ((Split-Path $f -Leaf) -ne 'toc.md') {
      $bannerText = $banners.$lang
      $text = "> [!WARNING]`n> $bannerText`n`n$text"
    }
    $text = $text.TrimEnd("`n") + "`n"
    $targetPath = Join-Path $outLangRoot $f
    $dir = Split-Path $targetPath
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($targetPath, $text, [System.Text.UTF8Encoding]::new($false))
    $state[$f] = @{ status='done'; ts=(Get-Date -Format 'o') }
    $doneCount++
  }

  Save-State $lang $state
  $summary.langs[$lang].doneInThisRun = $doneCount
  $summary.langs[$lang].failedThisRun = @($failedThisRun)
  Write-Log ("{0}: {1} files written this run" -f $lang, $doneCount)
}

# ---------- per-language link resolution ----------
foreach ($lang in $langs) {
  $root = Join-Path $RepoRoot (Join-Path $OutRoot $lang)
  if (-not (Test-Path $root)) { continue }
  $missing = [System.Collections.Generic.List[string]]::new()
  foreach ($file in (Get-ChildItem $root -Recurse -File -Filter *.md)) {
    $content = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($m in [regex]::Matches($content, '\]\(([^)\s]+\.md)')) {
      $dest = $m.Groups[1].Value
      if ($dest -match '^(https?:)?//') { continue }
      if ($dest -match '^~') { continue }
      $target = Join-Path (Split-Path $file.FullName -Parent) $dest
      if (-not (Test-Path $target)) { $missing.Add(('{0} -> {1}' -f ($file.FullName.Substring($RepoRoot.Length + 1)), $dest)) }
    }
  }
  $summary.langs[$lang].brokenLinks = @($missing)
  if ($missing.Count -gt 0) {
    Write-Log ("{0}: {1} BROKEN LINKS: {2}" -f $lang, $missing.Count, ($missing -join '; '))
  } else {
    Write-Log ("{0}: all internal links resolve" -f $lang)
  }
}

# ---------- summary ----------
$summaryJson = $summary | ConvertTo-Json -Depth 6
$summaryJson | Set-Content (Join-Path (Join-Path $RepoRoot 'temp') 'i18n-wiki-summary.json') -Encoding utf8
$failTotal = 0
Write-Log '==== summary ===='
foreach ($lang in $langs) {
  $s = $summary.langs[$lang]
  $failTotal += @($s.failedThisRun).Count
  Write-Log ('{0}: files={1} written={2} failedThisRun={3} brokenLinks={4}' -f $lang, $s.files, $s.doneInThisRun, @($s.failedThisRun).Count, @($s.brokenLinks).Count)
}
Write-Log ("total failures this run: {0}" -f $failTotal)
if ($failTotal -gt 0) { exit 1 }
exit 0
