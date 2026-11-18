# 用 DOTNET_JitDisasm 让 JIT 直接输出 Mapper.DeepCopy 的 native asm
# 对比三框架的 JIT 产物

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot/../../.."
$SaveDir = "$RepoRoot\BenchmarkDotNet.Artifacts\disasm_compare"

if (-not (Test-Path $SaveDir)) {
    New-Item -ItemType Directory -Path $SaveDir -Force | Out-Null
}

Write-Host "=== JIT Direct Disasm (DOTNET_JitDisasm) 三框架对比 ===" -ForegroundColor Cyan

# 配置 JIT disasm: 匹配 DeepCopy 相关方法
# 注意: DOTNET_JitDisasm 在 release 下需要 DOTNET_JitDisasmAsBackground=1 或通过 ETW
# 简单方式: 设置环境变量直接输出到 stderr
$env:DOTNET_JitDisasm = "*DeepCopy*"
$env:DOTNET_JitDisasmOnly = "0"  # 输出所有匹配的方法
# 关闭分层编译保证拿到 tier-1 反汇编
$env:DOTNET_TieredCompilation = "0"

$frameworks = @("net8.0", "net9.0", "net10.0")
foreach ($fw in $frameworks) {
    Write-Host ""
    Write-Host "=== Running $fw ===" -ForegroundColor Cyan

    $dllPath = "$RepoRoot\benchmarks\NCMapper.Benchmarks\bin\Release\$fw\NCMapper.Benchmarks.dll"

    $logFile = "$SaveDir\jit_disasm_${fw}.log"

    # 用 gcdiag 模式跑一次 DeepCopy (走 Expression 路径, 触发 JIT)
    # gcdiag 程序用 FastDeepCopyProvider 默认值, 我们需要切到 Expression
    # 直接调用 Expression 路径: gcdiag 50000 5000 expression
    & dotnet $dllPath gcdiag 50000 5000 expression 2>&1 | Out-File -FilePath $logFile -Encoding utf8

    $size = (Get-Item $logFile).Length
    Write-Host "  Log: $logFile ($size bytes)" -ForegroundColor Green

    # 看下日志开头是否有 JIT disasm 输出
    $firstLines = Get-Content $logFile -TotalCount 5
    Write-Host "  First 5 lines:" -ForegroundColor Yellow
    foreach ($line in $firstLines) {
        Write-Host "    $line"
    }
}

Remove-Item Env:DOTNET_JitDisasm
Remove-Item Env:DOTNET_JitDisasmOnly
Remove-Item Env:DOTNET_TieredCompilation

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
