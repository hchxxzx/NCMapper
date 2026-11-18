# DeepCopy_Expression disassembly 关闭 PGO 重跑 net9/10
# 目的: 验证 net9/10 BDN 跨不进 Mapper.DeepCopy 是否因 Dynamic PGO

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot/../../.."
$ArtifactsDir = "$RepoRoot\BenchmarkDotNet.Artifacts\results"
$SaveDir = "$RepoRoot\BenchmarkDotNet.Artifacts\disasm_compare"

Write-Host "=== DeepCopy_Expression Disassembly (No PGO) net9/10 ===" -ForegroundColor Cyan

$frameworks = @("net9.0", "net10.0")
foreach ($fw in $frameworks) {
    Write-Host ""
    Write-Host "=== Running $fw (TieredPGO=0, TieredCompilation=0) ===" -ForegroundColor Cyan

    $dllPath = "$RepoRoot\benchmarks\NCMapper.Benchmarks\bin\Release\$fw\NCMapper.Benchmarks.dll"

    # 删除旧 asm
    $oldAsm = "$ArtifactsDir\NCMapper.Benchmarks.DeepCopy_Expression-asm.md"
    if (Test-Path $oldAsm) { Remove-Item $oldAsm -Force }

    # 关闭 PGO + Tiered Compilation, 跑 BDN
    $env:DOTNET_TieredPGO = "0"
    $env:DOTNET_TieredCompilation = "0"
    & dotnet $dllPath DeepCopy_Expression 2>&1 | Out-Null
    Remove-Item Env:DOTNET_TieredPGO
    Remove-Item Env:DOTNET_TieredCompilation

    if (Test-Path $oldAsm) {
        $newPath = "$SaveDir\DeepCopy_Expression-${fw}-asm-NoPGO.md"
        Copy-Item $oldAsm $newPath -Force
        $size = (Get-Item $newPath).Length
        Write-Host "  Saved: $newPath ($size bytes)" -ForegroundColor Green
    } else {
        Write-Host "  ERROR: asm file not generated" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
