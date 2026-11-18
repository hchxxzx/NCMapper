<#
.SYNOPSIS
    对比性能基准与当前结果，检测性能回归
.DESCRIPTION
    读取基准目录和当前结果目录中的 BenchmarkDotNet JSON 结果，
    按场景对比 Mean 值。回归超过阈值时输出详细报告并返回非零退出码，
    使 CI 流水线失败。
.PARAMETER BaselineDir
    基准结果目录路径
.PARAMETER CurrentDir
    当前结果目录路径
.PARAMETER Threshold
    回归阈值百分比（默认 10，即耗时增加超过 10% 视为回归）
.NOTES
    JSON 解析失败 / 无可比场景 / 0 场景均回退非零退出码，不再静默 exit 0。
.EXAMPLE
    ./benchmarks/scripts/compare/compare_perf.ps1 -BaselineDir ./perf-baseline -CurrentDir ./perf-results
    ./benchmarks/scripts/compare/compare_perf.ps1 -BaselineDir ./perf-baseline -CurrentDir ./perf-results -Threshold 15
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$BaselineDir,

    [Parameter(Mandatory = $true)]
    [string]$CurrentDir,

    [int]$Threshold = 10
)

$ErrorActionPreference = "Continue"

# ── 基线不存在：首次运行，跳过对比 ──
if (-not (Test-Path $BaselineDir)) {
    Write-Host "::warning::Baseline directory not found: $BaselineDir -- first run, skipping comparison"
    Write-Host "First run -- no baseline to compare against"
    exit 0
}

# ── 查找 JSON 结果文件 ──
$baselineFiles = Get-ChildItem -Path $BaselineDir -Filter "*.json" -Recurse -ErrorAction SilentlyContinue
$currentFiles = Get-ChildItem -Path $CurrentDir -Filter "*.json" -Recurse -ErrorAction SilentlyContinue

if (-not $baselineFiles -or $baselineFiles.Count -eq 0) {
    Write-Host "::warning::No baseline JSON files found -- skipping comparison"
    exit 0
}

if (-not $currentFiles -or $currentFiles.Count -eq 0) {
    Write-Host "::error::No current result JSON files found"
    exit 1
}

# ── 对比逻辑 ──
$regressions = @()
$totalScenarios = 0
$parseFailures = 0
$improvedScenarios = 0
$okScenarios = 0

foreach ($currentFile in $currentFiles) {
    $baselineFile = $baselineFiles | Where-Object { $_.Name -eq $currentFile.Name } | Select-Object -First 1

    if (-not $baselineFile) {
        Write-Host "  SKIP $($currentFile.Name) -- no matching baseline"
        continue
    }

    try {
        $baselineJson = Get-Content $baselineFile.FullName -Raw | ConvertFrom-Json
        $currentJson = Get-Content $currentFile.FullName -Raw | ConvertFrom-Json

        $benchmarks = $currentJson.Benchmarks
        if (-not $benchmarks) { continue }

        foreach ($bench in $benchmarks) {
            $currentResults = $bench.Results
            if (-not $currentResults) { continue }

            $baselineBench = $baselineJson.Benchmarks | Where-Object { $_.DisplayInfo -eq $bench.DisplayInfo } | Select-Object -First 1
            if (-not $baselineBench -or -not $baselineBench.Results) { continue }

            foreach ($result in $currentResults) {
                $baselineResult = $baselineBench.Results | Where-Object { $_.DisplayInfo -eq $result.DisplayInfo } | Select-Object -First 1
                if (-not $baselineResult) { continue }

                $totalScenarios++

                $baselineMean = [double]$baselineResult.Statistics.Mean
                $currentMean = [double]$result.Statistics.Mean

                if ($baselineMean -eq 0) { continue }

                $changePercent = (($currentMean - $baselineMean) / $baselineMean) * 100
                $scenario = $result.DisplayInfo

                if ($changePercent -gt $Threshold) {
                    $status = "REGRESSION"
                    $regressions += [PSCustomObject]@{
                        Scenario = $scenario
                        Baseline = $baselineMean
                        Current  = $currentMean
                        Change   = [math]::Round($changePercent, 1)
                    }
                } elseif ($changePercent -lt -$Threshold) {
                    $status = "IMPROVED"
                    $improvedScenarios++
                } else {
                    $status = "OK"
                    $okScenarios++
                }

                Write-Host ("  {0,-60} {1,8:N1}%  {2}" -f $scenario, $changePercent, $status)
            }
        }
    } catch {
        Write-Host "::warning::Failed to parse: $($currentFile.Name) -- $_"
        $parseFailures++   # 解析失败计入门禁
    }
}

# ── 汇总报告 ──
Write-Host ""
Write-Host "============================================"
Write-Host "  Performance Regression Report"
Write-Host "============================================"
Write-Host "  Threshold : ${Threshold}%"
Write-Host "  Total     : $totalScenarios scenarios"
Write-Host "  OK        : $okScenarios"
Write-Host "  Improved  : $improvedScenarios"
Write-Host "  Regressed : $($regressions.Count)"
Write-Host "============================================"

# 解析失败/无可比场景不得静默 exit 0（CI 性能门禁失效）
if ($parseFailures -gt 0) {
    Write-Host ""
    Write-Host "::error::$parseFailures result file(s) failed to parse -- performance gate failure"
    exit 1
}

if ($totalScenarios -eq 0) {
    Write-Host ""
    Write-Host "::error::No comparable scenarios found (all skipped/mismatched) -- performance gate cannot be evaluated"
    exit 1
}

if ($regressions.Count -gt 0) {
    Write-Host ""
    Write-Host "::error::Performance regression detected! ($($regressions.Count)/$totalScenarios scenarios exceed ${Threshold}% threshold)"
    Write-Host ""
    foreach ($r in $regressions) {
        Write-Host ("  {0,-60}  baseline {1:N1}ns -> current {2:N1}ns  (+{3:N1}%)" -f $r.Scenario, $r.Baseline, $r.Current, $r.Change)
    }
    Write-Host ""
    Write-Host "To update the baseline, re-run with 'update-baseline: true' in workflow_dispatch."
    Write-Host "============================================"

    # 非零退出码 — CI 将此步骤标记为失败
    exit 1
}

Write-Host ""
Write-Host "No performance regressions detected."
Write-Host "============================================"
exit 0
