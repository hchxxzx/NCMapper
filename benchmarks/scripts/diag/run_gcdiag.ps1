# DeepCopy GC Diagnostic Runner
# 用途: 在 net8/9/10 三框架下跑 GC 诊断, 定位 DeepCopy 路径回退根因
# 调用:
#   .\run_gcdiag.ps1                                  # 默认 1M 次, all 路径, 三框架
#   .\run_gcdiag.ps1 -Path expression                # 只跑 Expression 路径
#   .\run_gcdiag.ps1 -Iterations 500000 -BatchSize 50000
#   .\run_gcdiag.ps1 -Frameworks "net8.0","net10.0"  # 指定框架
#   .\run_gcdiag.ps1 -OutputDir results_gcdiag        # 自定义输出目录

param(
    [string]$Path = "all",
    [long]$Iterations = 1000000,
    [int]$BatchSize = 100000,
    [string[]]$Frameworks = @("net8.0", "net9.0", "net10.0"),
    [string]$OutputDir = "results_gcdiag"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot/../../.."
$ProjPath = "$RepoRoot\benchmarks\NCMapper.Benchmarks\NCMapper.Benchmarks.csproj"

Write-Host "=== DeepCopy GC Diagnostic Runner ===" -ForegroundColor Cyan
Write-Host "  Path:        $Path"
Write-Host "  Iterations:  $Iterations"
Write-Host "  BatchSize:   $BatchSize"
Write-Host "  Frameworks:  $($Frameworks -join ', ')"
Write-Host "  OutputDir:   $OutputDir"
Write-Host ""

# Build (一次构建, 三框架共享)
Write-Host "Building benchmarks project..." -ForegroundColor Yellow
& dotnet build $ProjPath -c Release --nologo 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build OK." -ForegroundColor Green
Write-Host ""

# 创建输出目录
$OutFullPath = Join-Path $RepoRoot $OutputDir
if (-not (Test-Path $OutFullPath)) {
    New-Item -ItemType Directory -Path $OutFullPath -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

foreach ($fw in $Frameworks) {
    Write-Host "=== Running $fw ===" -ForegroundColor Cyan

    $dllPath = "$RepoRoot\benchmarks\NCMapper.Benchmarks\bin\Release\$fw\NCMapper.Benchmarks.dll"
    if (-not (Test-Path $dllPath)) {
        Write-Host "  DLL not found: $dllPath" -ForegroundColor Red
        continue
    }

    $logFile = Join-Path $OutFullPath "gcdiag_${fw}_${timestamp}.log"
    Write-Host "  Log: $logFile"

    # 运行诊断
    & dotnet $dllPath gcdiag $Iterations $BatchSize $Path 2>&1 | Tee-Object -FilePath $logFile
    Write-Host ""
}

Write-Host "=== All frameworks complete ===" -ForegroundColor Green
Write-Host "Logs saved to: $OutFullPath"
Write-Host ""
# （2026-08-09）：形态声明固化，报告引用日志 GC Mode 行即可
Write-Host "【形态声明（PERF-2 强制）】每份日志头部含 GC Mode: ServerGC/Workstation + ServerGC(bool)" -ForegroundColor Yellow
Write-Host "  引用报告时须保留 GC Mode 行，禁止裸引用 Gen0 数字（ServerGC Gen0 8.3-8.8 vs Workstation 1.7/100K 差异达 5 倍）" -ForegroundColor Yellow
Write-Host ""
Write-Host "对比要点 (确认 net9 Expression 回退根因):" -ForegroundColor Yellow
Write-Host "  1. Gen0/100K: net8 vs net9 vs net10 — 各代 GC 频次"
Write-Host "  2. KB/batch: 每框架每次 batch 的分配字节 (应基本一致)"
Write-Host "  3. ns/op: 净单次时间 (排除 GC 影响)"
Write-Host "  4. 如果 Gen0/Gen1 频次差异大 → GC 压力问题"
Write-Host "  5. 如果 ns/op 差异大但 GC 频次一致 → JIT 问题"
Write-Host "  6. 如果 net10 部分恢复 → 看 Gen2 频次是否回归 net8 水平"
