<#
.SYNOPSIS
检查 Requires* 特性消息语言门禁 — 检测 RequiresUnreferencedCode / RequiresDynamicCode
特性消息参数中是否包含 CJK 字符（中文/日文/韩文）。

.DESCRIPTION
Requires* 特性消息由编译器原样输出到 IL2026/IL3050 警告，属于对外诊断文本，
按项目规范必须使用英文（见 CONTRIBUTING.md）。本脚本守护该规范，防止中文消息再生。

匹配算法：整段参数匹配。正则捕获 Requires(UnreferencedCode|DynamicCode)( 后跟随的
完整参数段（一个或多个字符串字面量，含 \s*+\s* 连接的拼接形态），在该段内检测
[\u4e00-\u9fff]；命中输出 "文件:行号:消息片段" 并 exit 1，无命中 exit 0。
注释（//、///）中的中文不匹配（正则锚定在 Requires( 前缀，注释无此形态）。

.EXAMPLE
pwsh scripts/check-requires-messages.ps1

.NOTES
CI build job 通过 shell: pwsh 调用本脚本。
#>
[CmdletBinding()]
param(
    # 扫描根目录（默认 src，显式排除 bin/obj 下的生成文件）
    [string[]]$ScanRoots = @("src"),

    # 需排除的子目录名（匹配路径段）
    [string[]]$ExcludeSegments = @("bin", "obj")
)

$ErrorActionPreference = "Stop"

# Requires* 特性参数段：锚定特性声明（[ 前缀，排除注释内字面量），支持转义引号、
# verbatim 字符串（@ 前缀）、+ 拼接、以及消息文本内的 [ ] 字符。
# 结构：\[ (非 [ ] 字符或完整字符串)*? Requires(UnreferencedCode|DynamicCode)( 参数段
# ⚠️ 性能防护：前导 [^\[\]] 若允许换行，每个 [ 位置都会
# 跨行扩展 → 大文件 O(n×括号数) 灾难性回溯（实测 src 全量扫描挂死/超时）。限制单行
# （[^\[\]\n] / [^"\\\n]）后每 [ 仅在本行内扩展，线性复杂度。
$paramPattern = '\[(?:[^\[\]\n]|"(?:[^"\\\n]|\\.)*")*?Requires(?:UnreferencedCode|DynamicCode)\(\s*@?(?:"(?:[^"\\\n]|\\.)*"(?:\s*\+\s*)?)+'
# CJK 检测范围：中日韩统一表意文字
$cjkPattern = '[\u4e00-\u9fff]'

# 带超时的正则实例：单文件匹配超时 10s 抛 RegexMatchTimeoutException → 由逐文件 try/catch
# 捕获并记 SCAN_ERROR（完整性自检裁决），避免正则回溯拖死整个门禁（无输出挂死）
$regex = New-Object System.Text.RegularExpressions.Regex(
    $paramPattern,
    [System.Text.RegularExpressions.RegexOptions]::None,
    [TimeSpan]::FromSeconds(10))

$failed = $false
$hitCount = 0
$fileCount = 0
$scanErrorCount = 0

# 收集扫描文件：递归 ScanRoots 下 *.cs，排除命中 ExcludeSegments 路径段的文件
$files = Get-ChildItem -Path $ScanRoots -Recurse -Filter "*.cs" -File -ErrorAction SilentlyContinue |
    Where-Object {
        $path = $_.FullName
        $excluded = $false
        foreach ($seg in $ExcludeSegments) {
            if ($path -match ("(^|[\\/])" + [regex]::Escape($seg) + "([\\/]|$)")) {
                $excluded = $true
                break
            }
        }
        -not $excluded
    }

$totalScanned = 0
foreach ($file in $files) {
    try {
        # 逐文件异常隔离：单个文件读取/匹配失败不终止整体遍历，
        # 避免 $ErrorActionPreference=Stop 下漏扫后续文件（实测遍历中断缺陷）
        $content = [System.IO.File]::ReadAllText($file.FullName)
        $attrMatches = $regex.Matches($content)

        foreach ($attr in $attrMatches) {
            if ($attr.Value -match $cjkPattern) {
                # 计算命中行号：匹配起点前的换行数 + 1
                $lineNo = ($content.Substring(0, $attr.Index) -split "`n").Count
                # 压缩消息片段中的空白，便于单行输出
                $fragment = ($attr.Value -replace '\s+', ' ').Trim()
                Write-Output "$($file.FullName):${lineNo}:${fragment}"
                $hitCount++
                $failed = $true
            }
        }
        $fileCount++
    }
    catch {
        # 读取/处理失败：记录并继续（完整性自检在结尾裁决）
        Write-Output "SCAN_ERROR $($file.FullName): $($_.Exception.Message)"
        $scanErrorCount++
    }
}

# 遍历完整性自检（AUD-9）：处理失败的文件数非零时视为脚本自身故障，
# 即使无 CJK 命中也不得静默通过（防止漏扫导致门禁失效）
if ($scanErrorCount -gt 0) {
    $msg = "Scan incomplete: {0} file(s) failed to process out of {1} scanned. " +
        "The gate cannot guarantee completeness; check SCAN_ERROR lines above. " +
        "See CONTRIBUTING.md."
    Write-Error ($msg -f $scanErrorCount, ($files.Count))
    exit 1
}

if ($failed) {
    $msg = "CJK characters found in Requires* attribute messages ({0} file(s) scanned, {1} hit(s) above). " +
        "Requires* messages are consumer-facing diagnostics and must be English. " +
        "See CONTRIBUTING.md."
    Write-Error ($msg -f $fileCount, $hitCount)
    exit 1
}

Write-Output ("OK: no CJK characters in Requires* attribute messages ({0} file(s) scanned)." -f $fileCount)
exit 0
