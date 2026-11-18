<#
.SYNOPSIS
检查 locales 各语言翻译相对权威源（zh-Hans）的 key 漂移（缺失 / 多余残留）。

.DESCRIPTION
以 locales/zh-Hans/NCMapper.xml 的 <member name="..."> key 集合为权威基准，逐语比对：
  MISSING — 权威源有、该语言无（缺失 key，需补齐翻译）
  EXTRA   — 该语言有、权威源无（多余残留：zh-Hans 已删除/改名的旧 key，需清理或视为过时）
检测到漂移时默认非零退出码（CI 门禁）；--Warn 切换纯报告模式（退出码恒 0），
供用户 12 语翻译补齐过渡期使用——补齐复验 0 漂移后移除 --Warn 转强制门禁。
本脚本为只读检测工具，不修改任何文件。

判定口径说明：本脚本以 key 集合差为唯一漂移判据（MISSING = zh-Hans − 该语，EXTRA = 该语 − zh-Hans）。
summary 文本差异不参与判定——各语言为人工意译，文本必然不同；"过时"（EXTRA）即 zh-Hans 已删除
而译文仍残留的旧 key。

.PARAMETER RepoRoot
仓库根目录（默认取脚本所在目录的上一级）。

.PARAMETER Warn
纯报告模式：检测到漂移仅报告不失败（退出码恒 0）。翻译补齐过渡期使用。

.PARAMETER Language
仅检查指定语言（如 en）；缺省检查全部非 zh-Hans 语言。

.PARAMETER Detail
输出完整缺失/多余 key 清单（默认仅输出汇总；漂移量 ≤20 时自动附带完整清单）。

.EXAMPLE
.\scripts\check-locales-drift.ps1                  # 严格模式，存在漂移则 exit 1
.\scripts\check-locales-drift.ps1 -Warn            # 报告模式，恒 exit 0
.\scripts\check-locales-drift.ps1 -Language en     # 仅检查 en
.\scripts\check-locales-drift.ps1 -Detail          # 附完整 key 清单（补齐对账用）
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [switch]$Warn,
    [string]$Language = "",
    [switch]$Detail
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($RepoRoot)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = Join-Path $scriptDir '..'
}

$localesDir = Join-Path $RepoRoot 'locales'
$authoritative = Join-Path $localesDir 'zh-Hans\NCMapper.xml'

if (-not (Test-Path $authoritative)) {
    Write-Host "[ERROR] 权威源缺失: $authoritative (zh-Hans/NCMapper.xml 不存在，请先构建生成)"
    exit 2
}

# 提取 XML 文档的 member key 集合（<member name="..."> 属性去重，XPath 提取规避 PS5.1 XML 适配器差异）
function Get-MemberKeys([string]$Path) {
    [xml]$doc = Get-Content -Path $Path -Encoding UTF8 -Raw
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $nodes = $doc.SelectNodes('//member[@name]')
    foreach ($node in $nodes) {
        [void]$set.Add($node.GetAttribute('name'))
    }
    return $set
}

$authKeys = Get-MemberKeys -Path $authoritative
Write-Host ("权威源 zh-Hans/NCMapper.xml : {0} key" -f $authKeys.Count)
Write-Host ""

$dirs = Get-ChildItem -Path $localesDir -Directory | Where-Object { $_.Name -ne 'zh-Hans' } | Sort-Object Name
if (-not [string]::IsNullOrEmpty($Language)) {
    $dirs = $dirs | Where-Object { $_.Name -eq $Language }
}

if ($dirs.Count -eq 0) {
    Write-Host "[WARN] 未找到待检查语言目录（locales/ 下非 zh-Hans 目录为空，或指定语言不存在）"
}

$anyDrift = $false
foreach ($d in $dirs) {
    $xml = Join-Path $d.FullName 'NCMapper.xml'
    if (-not (Test-Path $xml)) {
        Write-Host ("{0,-8} MISSING 文件 — 该语言目录无 NCMapper.xml（全部 {1} key 缺失）" -f $d.Name, $authKeys.Count)
        $anyDrift = $true
        continue
    }
    $langKeys = Get-MemberKeys -Path $xml
    $missing = @($authKeys | Where-Object { -not $langKeys.Contains($_) } | Sort-Object)
    $extra   = @($langKeys | Where-Object { -not $authKeys.Contains($_) } | Sort-Object)

    if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
        Write-Host ("{0,-8} OK   同步（{1} key 与权威源一致）" -f $d.Name, $langKeys.Count)
        continue
    }
    $anyDrift = $true
    Write-Host ("{0,-8} DRIFT 缺失 {1} | 多余 {2}（共 {3} key，权威 {4}）" -f $d.Name, $missing.Count, $extra.Count, $langKeys.Count, $authKeys.Count)
    $detailThreshold = 20
    if ($Detail -or ($missing.Count + $extra.Count) -le $detailThreshold) {
        foreach ($k in $missing) { Write-Host ("    [MISSING] {0}" -f $k) }
        foreach ($k in $extra)   { Write-Host ("    [EXTRA]   {0}" -f $k) }
    }
    else {
        Write-Host ("    （缺失/多余合计 {0} 项，超出自动明细阈值；用 -Detail 输出完整清单）" -f ($missing.Count + $extra.Count))
    }
}

Write-Host ""
if ($anyDrift) {
    Write-Host "检测到 locales 漂移（缺失/多余 key），见上方逐语清单。"
    if ($Warn) {
        Write-Host "当前为 --Warn 报告模式：退出码 0（翻译补齐过渡期；补齐复验 0 漂移后移除 --Warn 转强制门禁）。"
        exit 0
    }
    Write-Host "当前为严格模式：退出码 1（CI 门禁失败）。"
    exit 1
}
Write-Host "所有语言与权威源 zh-Hans key 集合一致，无漂移。"
exit 0
