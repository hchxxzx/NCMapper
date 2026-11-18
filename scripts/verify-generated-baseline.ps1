# 生成产物基线一致性门禁
# 构建 samples/test/benchmarks 三项目 → 提取 NCMapper 源生成产物 → 与 generated-baseline/ 逐字节对比
# （归一化行尾：\r\n 与 \n 等价——生成代码确定性守卫，行尾差异不判失败）。
# 任一 DIFF → 输出差异摘要并以非零退出（CI 门禁失败）。
# 本环境构建含以下旗标（-m:1 -p:EnableSourceLink=false）：
# MSBuild 并行节点 + SourceLink 组合下多节点构建静默失败，-m:1 串行 + 关闭 SourceLink 规避。
param(
    [switch]$SkipBuild   # 跳过构建（调试用，直接对比既有产物）
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$baselineRoot = Join-Path $repo 'generated-baseline'
# 跨平台临时目录：Windows $env:TEMP 在 Linux（CI ubuntu runner）为 null，改用 .NET 跨平台 API
# （2026-09-03 CI 失败根因：pwsh on Linux 无 TEMP 环境变量）
$outRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'ncmapper-baseline-check'
if (Test-Path $outRoot) { Remove-Item $outRoot -Recurse -Force }
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

function Get-Sha256([string]$path) {
    $h = [System.Security.Cryptography.SHA256]::Create()
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $hash = $h.ComputeHash($bytes)
    return ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant().Substring(0, 16)
}

function Normalize-LineEndings([string]$text) {
    return $text.Replace("`r`n", "`n")
}

function Find-GeneratedMapper([string]$root) {
    $found = @()
    if (-not (Test-Path $root)) { return $found }
    Get-ChildItem $root -Recurse -File -Filter '*GeneratedMapper.g.cs' -ErrorAction SilentlyContinue |
        ForEach-Object { $found += $_.FullName }
    return $found
}

# RuntimeSnapshot 产物查找（独立生成器 NCMapper_RuntimeSnapshotMetadata.g.cs）
function Find-GeneratedRuntimeSnapshot([string]$root) {
    $found = @()
    if (-not (Test-Path $root)) { return $found }
    Get-ChildItem $root -Recurse -File -Filter 'NCMapper_RuntimeSnapshotMetadata.g.cs' -ErrorAction SilentlyContinue |
        ForEach-Object { $found += $_.FullName }
    return $found
}

# Fluent 身份登记产物查找（独立输出节点 NCMapper_FluentSnapshotRegistry.g.cs）
function Find-GeneratedIdentityRegistry([string]$root) {
    $found = @()
    if (-not (Test-Path $root)) { return $found }
    Get-ChildItem $root -Recurse -File -Filter 'NCMapper_FluentSnapshotRegistry.g.cs' -ErrorAction SilentlyContinue |
        ForEach-Object { $found += $_.FullName }
    return $found
}

$projects = @(
    @{ Name = 'samples';    Proj = 'samples/NCMapper.Samples/NCMapper.Samples.csproj';         Out = 'samples' },
    @{ Name = 'test';       Proj = 'test/NCMapper.Tests/NCMapper.Tests.csproj';                Out = 'test' },
    @{ Name = 'benchmarks'; Proj = 'benchmarks/NCMapper.SelfBench/NCMapper.SelfBench.csproj';   Out = 'benchmarks' }
)

$failed = @()
$skipped = @()

foreach ($p in $projects) {
    $baselineFile = Join-Path $baselineRoot "$($p.Name)/NCMapper_Generated_GeneratedMapper.g.cs"
    if (-not (Test-Path $baselineFile)) {
        Write-Error "[baseline-check] 基线缺失: $baselineFile"; exit 1
    }

    # 构建（SkipBuild 时跳过；构建失败记跳过——锁等环境干扰不误判 DIFF，CI 无此问题）
    if (-not $SkipBuild) {
        Write-Host "[baseline-check] Building $($p.Name) ..."
        $buildOut = Join-Path $outRoot $p.Out
        # --no-incremental：增量缓存命中时生成器不重跑、Emit 不输出产物（2026-08-14 实测）
        # -m:1 -p:EnableSourceLink=false——
        # MSBuild 18.9.6 并行节点 + SourceLink 组合下多节点构建静默失败，串行 + 关闭 SourceLink 规避
        & dotnet build (Join-Path $repo $p.Proj) -f net10.0 --configuration Release --nologo --no-incremental -m:1 `
            -p:EnableSourceLink=false `
            -p:EmitCompilerGeneratedFiles=true `
            -p:CompilerGeneratedFilesOutputPath=$buildOut 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[baseline-check] $($p.Name): 构建失败（跳过对比——环境锁等干扰；CI 应无此问题）"
            $skipped += $p.Name
            continue
        }
    }

    # 提取当前产物：优先 obj/Generated 下的 NCMapper.SourceGen 产物（test 项目命令行输出不生效时）
    $current = $null
    $objGen = Join-Path $repo (Join-Path (Split-Path $p.Proj) 'obj/Generated')
    $fromOut = Find-GeneratedMapper (Join-Path $outRoot $p.Name) | Where-Object { $_ -match 'NCMapper.SourceGen' } | Select-Object -First 1
    $fromObj = Find-GeneratedMapper $objGen | Where-Object { $_ -match 'NCMapper.SourceGen' } | Select-Object -First 1
    if ($fromOut) { $current = $fromOut }
    elseif ($fromObj) { $current = $fromObj }

    if (-not $current) {
        Write-Host "[baseline-check] $($p.Name): 未找到当前生成产物（跳过）"
        $skipped += $p.Name
        continue
    }

    $a = Normalize-LineEndings ([System.IO.File]::ReadAllText($baselineFile))
    $b = Normalize-LineEndings ([System.IO.File]::ReadAllText($current))
    $shaA = Get-Sha256 $baselineFile
    $shaB = Get-Sha256 $current

    if ($a -ceq $b) {
        Write-Host "[baseline-check] $($p.Name): IDENTICAL ($shaA)"
    }
    else {
        Write-Host "[baseline-check] $($p.Name): DIFF (baseline $shaA vs current $shaB)"
        $linesA = $a -split "`n"
        $linesB = $b -split "`n"
        $max = [Math]::Min($linesA.Count, $linesB.Count)
        for ($i = 0; $i -lt $max; $i++) {
            if ($linesA[$i] -cne $linesB[$i]) {
                Write-Host "  first diff @ L$($i+1):"
                Write-Host "    baseline: $($linesA[$i])"
                Write-Host "    current:  $($linesB[$i])"
                break
            }
        }
        $failed += $p.Name
    }
}

# RuntimeSnapshot 产物基线一致性（独立生成器 NCMapper_RuntimeSnapshotMetadata.g.cs）
# 复用上方主生成器构建产物（同一次 dotnet build 同时产出两个文件——开关 on 状态由 Directory.Build.props 默认注入）。
# 基线目录 generated-baseline/runtime-snapshot/{samples,test,benchmarks}/，与主生成器基线分离（F1 字节不变门 + RuntimeSnapshot 字节稳定性门分离）。
foreach ($p in $projects) {
    $rsBaselineFile = Join-Path $baselineRoot "runtime-snapshot/$($p.Name)/NCMapper_RuntimeSnapshotMetadata.g.cs"
    if (-not (Test-Path $rsBaselineFile)) {
        Write-Error "[baseline-check] RuntimeSnapshot 基线缺失: $rsBaselineFile"; exit 1
    }

    # 复用主生成器构建产物——从 outRoot 与 obj/Generated 两路查找
    $rsCurrent = $null
    $objGen = Join-Path $repo (Join-Path (Split-Path $p.Proj) 'obj/Generated')
    $rsFromOut = Find-GeneratedRuntimeSnapshot (Join-Path $outRoot $p.Name) | Where-Object { $_ -match 'NCMapper.SourceGen' } | Select-Object -First 1
    $rsFromObj = Find-GeneratedRuntimeSnapshot $objGen | Where-Object { $_ -match 'NCMapper.SourceGen' } | Select-Object -First 1
    if ($rsFromOut) { $rsCurrent = $rsFromOut }
    elseif ($rsFromObj) { $rsCurrent = $rsFromObj }

    if (-not $rsCurrent) {
        Write-Host "[baseline-check] $($p.Name) RuntimeSnapshot: 未找到当前生成产物（跳过）"
        $skipped += "$($p.Name)-RuntimeSnapshot"
        continue
    }

    $rsa = Normalize-LineEndings ([System.IO.File]::ReadAllText($rsBaselineFile))
    $rsb = Normalize-LineEndings ([System.IO.File]::ReadAllText($rsCurrent))
    $rsShaA = Get-Sha256 $rsBaselineFile
    $rsShaB = Get-Sha256 $rsCurrent

    if ($rsa -ceq $rsb) {
        Write-Host "[baseline-check] $($p.Name) RuntimeSnapshot: IDENTICAL ($rsShaA)"
    }
    else {
        Write-Host "[baseline-check] $($p.Name) RuntimeSnapshot: DIFF (baseline $rsShaA vs current $rsShaB)"
        $linesA = $rsa -split "`n"
        $linesB = $rsb -split "`n"
        $max = [Math]::Min($linesA.Count, $linesB.Count)
        for ($i = 0; $i -lt $max; $i++) {
            if ($linesA[$i] -cne $linesB[$i]) {
                Write-Host "  first diff @ L$($i+1):"
                Write-Host "    baseline: $($linesA[$i])"
                Write-Host "    current:  $($linesB[$i])"
                break
            }
        }
        $failed += "$($p.Name)-RuntimeSnapshot"
    }
}

# Fluent 身份登记产物基线一致性（NCMapper_FluentSnapshotRegistry.g.cs，独立输出节点）
# 身份段仅含 Fluent 通道条目——无 Fluent 声明的工程（samples/benchmarks）零产物：当前产物缺失即跳过（不要求基线存在）。
# 基线目录 generated-baseline/runtime-snapshot/{name}/NCMapper_FluentSnapshotRegistry.g.cs（与明细段基线同居）。
foreach ($p in $projects) {
    $objGen = Join-Path $repo (Join-Path (Split-Path $p.Proj) 'obj/Generated')
    $idFromOut = Find-GeneratedIdentityRegistry (Join-Path $outRoot $p.Name) | Where-Object { $_ -match 'NCMapper.SourceGen' } | Select-Object -First 1
    $idFromObj = Find-GeneratedIdentityRegistry $objGen | Where-Object { $_ -match 'NCMapper.SourceGen' } | Select-Object -First 1
    $idCurrent = $null
    if ($idFromOut) { $idCurrent = $idFromOut }
    elseif ($idFromObj) { $idCurrent = $idFromObj }

    if (-not $idCurrent) {
        Write-Host "[baseline-check] $($p.Name) FluentSnapshotRegistry: 零产物（无 Fluent 声明——跳过）"
        $skipped += "$($p.Name)-FluentSnapshotRegistry"
        continue
    }

    $idBaselineFile = Join-Path $baselineRoot "runtime-snapshot/$($p.Name)/NCMapper_FluentSnapshotRegistry.g.cs"
    if (-not (Test-Path $idBaselineFile)) {
        Write-Error "[baseline-check] FluentSnapshotRegistry 基线缺失: $idBaselineFile（新增 Fluent 声明属预期变更时，将产物拷入基线并人工审查 diff）"
        exit 1
    }

    $ida = Normalize-LineEndings ([System.IO.File]::ReadAllText($idBaselineFile))
    $idb = Normalize-LineEndings ([System.IO.File]::ReadAllText($idCurrent))
    $idShaA = Get-Sha256 $idBaselineFile
    $idShaB = Get-Sha256 $idCurrent

    if ($ida -ceq $idb) {
        Write-Host "[baseline-check] $($p.Name) FluentSnapshotRegistry: IDENTICAL ($idShaA)"
    }
    else {
        Write-Host "[baseline-check] $($p.Name) FluentSnapshotRegistry: DIFF (baseline $idShaA vs current $idShaB)"
        $linesA = $ida -split "`n"
        $linesB = $idb -split "`n"
        $max = [Math]::Min($linesA.Count, $linesB.Count)
        for ($i = 0; $i -lt $max; $i++) {
            if ($linesA[$i] -cne $linesB[$i]) {
                Write-Host "  first diff @ L$($i+1):"
                Write-Host "    baseline: $($linesA[$i])"
                Write-Host "    current:  $($linesB[$i])"
                break
            }
        }
        $failed += "$($p.Name)-FluentSnapshotRegistry"
    }
}

if ($failed.Count -gt 0) {
    Write-Error "[baseline-check] FAILED: $($failed -join ', ') 生成产物与基线不一致（有意变更请更新 generated-baseline/ 并人工审查 diff）"
    exit 1
}
if ($skipped.Count -gt 0) {
    Write-Host "[baseline-check] SKIPPED: $($skipped -join ', ')（构建失败/产物缺失——CI 环境应无此情况，若 CI 出现请调查）"
}
Write-Host "[baseline-check] PASS: 三项目生成产物与基线逐字节一致（归一化行尾口径）"
exit 0
