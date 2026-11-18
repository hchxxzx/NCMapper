# DeepCopy_Expression disassembly 三框架对比脚本
# 跑三框架 BDN, 分别保存 -asm.md 文件以便对比

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot/../../.."
$ProjPath = "$RepoRoot\benchmarks\NCMapper.Benchmarks\NCMapper.Benchmarks.csproj"
$ArtifactsDir = "$RepoRoot\BenchmarkDotNet.Artifacts\results"
$SaveDir = "$RepoRoot\BenchmarkDotNet.Artifacts\disasm_compare"

if (-not (Test-Path $SaveDir)) {
    New-Item -ItemType Directory -Path $SaveDir -Force | Out-Null
}

Write-Host "=== DeepCopy_Expression Disassembly 三框架对比 ===" -ForegroundColor Cyan

# Build all frameworks
Write-Host "Building..." -ForegroundColor Yellow
& dotnet build $ProjPath -c Release --nologo 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed!" -ForegroundColor Red; exit 1 }
Write-Host "Build OK." -ForegroundColor Green

$frameworks = @("net8.0", "net9.0", "net10.0")
foreach ($fw in $frameworks) {
    Write-Host ""
    Write-Host "=== Running $fw ===" -ForegroundColor Cyan

    $dllPath = "$RepoRoot\benchmarks\NCMapper.Benchmarks\bin\Release\$fw\NCMapper.Benchmarks.dll"
    if (-not (Test-Path $dllPath)) {
        Write-Host "  DLL not found: $dllPath" -ForegroundColor Red
        continue
    }

    # 删除旧 artifacts 防止混淆
    $oldAsm = "$ArtifactsDir\NCMapper.Benchmarks.DeepCopy_Expression-asm.md"
    if (Test-Path $oldAsm) { Remove-Item $oldAsm -Force }

    # 跑 BDN: 只跑 DeepCopy_Expression
    & dotnet $dllPath DeepCopy_Expression 2>&1 | Out-Null

    # 复制 asm 文件
    if (Test-Path $oldAsm) {
        $newPath = "$SaveDir\DeepCopy_Expression-${fw}-asm.md"
        Copy-Item $oldAsm $newPath -Force
        Write-Host "  Saved: $newPath" -ForegroundColor Green
    } else {
        Write-Host "  ERROR: asm file not generated" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== All frameworks complete ===" -ForegroundColor Green
Write-Host "Disasm files saved to: $SaveDir"
Write-Host ""
Write-Host "Files:"
Get-ChildItem $SaveDir | ForEach-Object { Write-Host "  $($_.Name) ($($_.Length) bytes)" }
