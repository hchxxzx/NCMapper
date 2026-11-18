<#
.SYNOPSIS
    NCMapper 全框架串行性能测试脚本
.DESCRIPTION
    依次编译并运行 net8.0 / net9.0 / net10.0 三个框架的完整 benchmark。
    支持参数选择运行范围（all / ncmapper / Quick / 单场景名）。
    结果输出到 BenchmarkResults 目录，按框架和时间戳分目录。
.PARAMETER Mode
    运行模式：all（全组件全场景）、ncmapper（仅NCMapper）、Quick（快速三路径）、或场景名（如 Flat）
    默认 all
.PARAMETER Frameworks
    框架列表，默认 "net8.0;net9.0;net10.0"
.EXAMPLE
    .\run_all_frameworks.ps1
    .\run_all_frameworks.ps1 -Mode ncmapper
    .\run_all_frameworks.ps1 -Mode Quick
    .\run_all_frameworks.ps1 -Mode Flat
#>
param(
    [string]$Mode = "all",
    [string]$Frameworks = "net8.0;net9.0;net10.0"
)

$ErrorActionPreference = "Stop"
$rootDir = Resolve-Path "$PSScriptRoot/../../.."
$projPath = "$rootDir/benchmarks/NCMapper.Benchmarks/NCMapper.Benchmarks.csproj"
$resultsDir = "$rootDir/BenchmarkResults"

# 创建结果目录
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$sessionDir = "$resultsDir/$timestamp"
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " NCMapper Benchmark Runner" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Mode:       $Mode"
Write-Host " Frameworks: $Frameworks"
Write-Host " Results:    $sessionDir"
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. 编译 Release（三框架一次编译）
Write-Host "[1/3] Building Release..." -ForegroundColor Yellow
$buildStart = Get-Date
dotnet build $projPath -c Release 2>&1 | Select-String "error|已成功|个错误" | ForEach-Object { Write-Host "  $_" }
$buildEnd = Get-Date
Write-Host "  Build time: $(($buildEnd - $buildStart).ToString('mm\:ss'))"
Write-Host ""

# 2. 逐框架运行
$fwList = $Frameworks -split ';'
$totalStart = Get-Date

for ($i = 0; $i -lt $fwList.Count; $i++) {
    $fw = $fwList[$i]
    $fwDir = "$sessionDir/$fw"
    New-Item -ItemType Directory -Force -Path $fwDir | Out-Null

    Write-Host "[2/3] Running $fw ($($i+1)/$($fwList.Count))..." -ForegroundColor Yellow
    Write-Host "  Mode: $Mode"
    Write-Host "  Output: $fwDir/benchmark-output.txt"
    Write-Host "  Started: $(Get-Date -Format 'HH:mm:ss')"

    $fwStart = Get-Date
    $projDir = "$rootDir/benchmarks/NCMapper.Benchmarks"
    $dllPath = "$rootDir/benchmarks/NCMapper.Benchmarks/bin/Release/$fw/NCMapper.Benchmarks.dll"

    # 全场景分组（与 Program.cs 中 groups 保持一致）
    $scenarioGroups = @(
        "Flat", "NameDiff", "Nested", "Collection",
        "BidirED", "BidirDE", "Action",
        "EnumString", "Record", "Nullable", "AutoFlatten", "DeepCopy",
        "Runtime", "Quick"
    )

    # 确定要跑的场景列表
    if ($Mode -eq "all") {
        $runList = $scenarioGroups
    } elseif ($Mode -eq "ncmapper") {
        $runList = @("ncmapper")
    } else {
        $runList = @($Mode)
    }

    $totalScenarios = $runList.Count
    $scenarioIdx = 0
    $outputPath = "$fwDir/benchmark-output.txt"

    # 先清空输出文件
    "" | Out-File -FilePath $outputPath -Encoding UTF8

    foreach ($scenario in $runList) {
        $scenarioIdx++
        Write-Host "  [$scenarioIdx/$totalScenarios] $scenario ..." -ForegroundColor Cyan

        # 清理 BDN artifacts 和隔离构建残留，防止累积导致后续构建失败
        $bdnRoot = "$projDir/BenchmarkDotNet.Artifacts"
        if (Test-Path $bdnRoot) {
            Remove-Item $bdnRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        # 清理 BDN 隔离构建的 GUID 目录（bin/Release/<fw>/uuid/）
        $isolatedDirs = Get-ChildItem "$projDir/bin/Release/$fw" -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' }
        foreach ($dir in $isolatedDirs) {
            Remove-Item $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }

        # 每个场景独立启动 dotnet 进程，输出追加到日志文件
        Push-Location $projDir
        try {
            & dotnet $dllPath $scenario 2>&1 | Tee-Object -FilePath $outputPath -Append
        } finally {
            Pop-Location
        }

        # 每个场景跑完立即收集 CSV（到框架总目录，后续场景的 CSV 也追加）
        $bdnResults = "$projDir/BenchmarkDotNet.Artifacts/results"
        if (Test-Path $bdnResults) {
            $csvFiles = Get-ChildItem "$bdnResults/*.csv" -ErrorAction SilentlyContinue
            if ($csvFiles) {
                Copy-Item "$bdnResults/*.csv" $fwDir/ -Force
            }
        }

        Write-Host ""
    }

    $fwEnd = Get-Date
    $fwDuration = $fwEnd - $fwStart
    Write-Host "  $fw completed in $($fwDuration.ToString('hh\:mm\:ss'))"
    Write-Host ""
}

# 3. 汇总
$totalEnd = Get-Date
$totalDuration = $totalEnd - $totalStart

Write-Host "[3/3] Summary" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Green
Write-Host " Total time: $($totalDuration.ToString('hh\:mm\:ss'))"
Write-Host " Results:    $sessionDir"
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Framework directories:"
foreach ($fw in $fwList) {
    $fwDir = "$sessionDir/$fw"
    $csvCount = (Get-ChildItem "$fwDir/*.csv" -ErrorAction SilentlyContinue | Measure-Object).Count
    $logSize = if (Test-Path "$fwDir/benchmark-output.txt") {
        "{0:N0} KB" -f ((Get-Item "$fwDir/benchmark-output.txt").Length / 1KB)
    } else { "N/A" }
    Write-Host "  $fw : $csvCount CSVs, log $logSize"
}
Write-Host ""
Write-Host "Done. Review results in: $sessionDir"
