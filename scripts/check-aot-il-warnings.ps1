<#
.SYNOPSIS
检查 NativeAOT publish 输出是否包含预期 IL2026（RequiresUnreferencedCode）与 IL3050（RequiresDynamicCode）警告。

.DESCRIPTION
AOT 反射路径（如 Mapper.DeepCopyRuntimeOnly、Mapper.Validate(params Assembly[])）的真实契约
是"编译期已用 Requires* 标注声明反射风险"，而非"运行时必定抛异常"——NativeAOT 下该类方法被
直接引用（root）时方法体保留、反射目标成员保留则反射可成功执行（详见深拷贝设计文档
"可能失败"语义）。因此 AOT 契约应以构建期 IL 警告为硬门禁，而非运行时"靠撞运气"断言必须抛。

本脚本在 CI aot job 中取代 Publish AOT 步骤：内部执行 `dotnet publish`，捕获完整输出，断言：
  1. publish 成功（退出码 0）；
  2. 输出包含至少一条 IL2026 警告（RequiresUnreferencedCode 契约已声明）；
  3. 输出包含至少一条 IL3050 警告（RequiresDynamicCode 契约已声明）；
  4.（可选）输出命中指定成员路径（-RequiredMembers），验证具体反射路径的契约标注在位。

任一断言不满足即非零退出码（CI 门禁失败）。本脚本为只读检测工具，不修改任何源码文件
（publish 产物落在 bin/obj，属构建输出）。

注意：NativeAOT publish 需要本机具备 MSVC + Windows SDK（CI windows-latest 自带；
本机需预先设置 VCINSTALLDIR/WindowsSdkDir/WindowsSDKVersion/INCLUDE/LIB，见 MEMORY.md）。

.PARAMETER RepoRoot
仓库根目录（默认取脚本所在目录的上一级）。

.PARAMETER AotProject
AotTests 工程相对路径（默认 test/NCMapper.AotTests/NCMapper.AotTests.csproj）。

.PARAMETER Configuration
publish 配置（默认 Release）。

.PARAMETER RequiredMembers
需在 IL 警告中出现的成员路径（支持通配，如 *DeepCopyRuntimeOnly*、*Validate*）；缺省仅断言
IL2026+IL3050 各至少一条，不限定具体成员。可传逗号分隔多个。

.EXAMPLE
.\scripts\check-aot-il-warnings.ps1                                    # 默认断言 IL2026+IL3050 各至少一条
.\scripts\check-aot-il-warnings.ps1 -RequiredMembers '*DeepCopyRuntimeOnly*,*Validate*'
.\scripts\check-aot-il-warnings.ps1 -RepoRoot 'D:\OpenCode\ncmapper'
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string]$AotProject = "test/NCMapper.AotTests/NCMapper.AotTests.csproj",
    [string]$Configuration = "Release",
    [string[]]$RequiredMembers = @()
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$RepoRoot = (Resolve-Path $RepoRoot).Path
$projPath = Join-Path $RepoRoot $AotProject
if (-not (Test-Path $projPath)) {
    Write-Error "AOT 工程不存在: $projPath"
    exit 2
}

Write-Host "=== check-aot-il-warnings.ps1 ==="
Write-Host "工程: $projPath"
Write-Host "配置: $Configuration"

# 1) 捕获 publish 输出（含 IL2026/IL3050 警告）。
#    关键：增量 publish 不会重发 IL 警告（ILC 步骤被跳过），会漏检。故先 dotnet clean 清掉
#    obj/bin 中间产物（含源生成代码），强制全量重编译——保证 IL 警告每次都在输出中。
#    --no-restore 复用已有还原避免 CI 网络抖动；若还原缺失由下方兜底重试带 restore。
& dotnet clean $projPath --configuration $Configuration 2>&1 | Out-Null

$publishOut = & dotnet publish $projPath --configuration $Configuration --no-restore 2>&1
$publishExit = $LASTEXITCODE

if ($publishExit -ne 0) {
    # 兜底：--no-restore 失败（如锁文件缺 RID）时重试带 restore
    Write-Host "[WARN] publish 首次退出码 $publishExit（疑似还原问题），重试带 restore..."
    $publishOut = & dotnet publish $projPath --configuration $Configuration 2>&1
    $publishExit = $LASTEXITCODE
}

if ($publishExit -ne 0) {
    $publishOut | ForEach-Object { Write-Host $_ }
    Write-Error "NativeAOT publish 失败（退出码 $publishExit）。"
    exit 1
}

Write-Host "[PASS] publish 成功（退出码 0）"

# 2) 统计 IL 警告
$il2026 = $publishOut | Select-String -Pattern 'IL2026' | Measure-Object | Select-Object -ExpandProperty Count
$il3050 = $publishOut | Select-String -Pattern 'IL3050' | Measure-Object | Select-Object -ExpandProperty Count

Write-Host ("[INFO] IL2026 (RequiresUnreferencedCode) 警告数: {0}" -f $il2026)
Write-Host ("[INFO] IL3050 (RequiresDynamicCode)     警告数: {0}" -f $il3050)

$failures = @()
if ($il2026 -lt 1) {
    $failures += "输出缺少 IL2026 警告（RequiresUnreferencedCode 契约未声明——AOT 反射路径契约断裂）"
}
if ($il3050 -lt 1) {
    $failures += "输出缺少 IL3050 警告（RequiresDynamicCode 契约未声明——AOT 反射路径契约断裂）"
}

# 3) 可选：校验指定成员路径出现在 IL 警告中
# 将通配模式（如 *DeepCopyRuntimeOnly*）转为正则：除 * 外全部转义，* → .*
foreach ($m in $RequiredMembers) {
    $rx = [regex]::Escape($m).Replace('\*', '.*')
    $hit = $publishOut | Select-String -Pattern $rx | Measure-Object | Select-Object -ExpandProperty Count
    Write-Host ("[INFO] 成员路径 '{0}' 在 IL 警告中命中 {1} 次" -f $m, $hit)
    if ($hit -lt 1) {
        $failures += "IL 警告中未找到成员路径: $m（对应反射路径契约未声明）"
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host ("[FAIL] {0}" -f $f) }
    Write-Host "AOT 反射路径契约门禁失败：IL2026/IL3050 警告缺失，契约已断裂。"
    exit 1
}

Write-Host "[PASS] AOT IL 契约门禁通过（IL2026/IL3050 警告已声明）"
exit 0
