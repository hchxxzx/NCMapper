# verify-package-xml.ps1 — 打包后 XML 合法性守卫
# 校验 nupkg 内 buildTransitive 下所有 .targets/.props 为合法 XML，失败退出码 1。
# 用法：powershell -ExecutionPolicy Bypass scripts/verify-package-xml.ps1 [-Nupkg <path>] [-LocalDir <dir>]
# 默认：校验 D:\nuget\local 下最新的 NCMapper*.nupkg（无参直接跑）。

param(
    [string]$Nupkg,
    [string]$LocalDir = 'D:\nuget\local'
)

Add-Type -AssemblyName System.IO.Compression.FileSystem

$target = $Nupkg
if (-not $target) {
    $target = Get-ChildItem -Path $LocalDir -Filter 'NCMapper*.nupkg' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $target -or -not (Test-Path $target)) {
    Write-Host "[FAIL] 未找到 nupkg（参数 -Nupkg 或本地源 $LocalDir）" -ForegroundColor Red
    exit 1
}
Write-Host "校验包: $target"

$zip = [System.IO.Compression.ZipFile]::OpenRead($target)
try {
    $xmlEntries = @($zip.Entries | Where-Object { $_.FullName -match '^buildTransitive/.*\.(targets|props)$' })
    if ($xmlEntries.Count -eq 0) {
        Write-Host "[WARN] 包内无 buildTransitive XML 文件" -ForegroundColor Yellow
        exit 0
    }
    $fail = 0
    foreach ($e in $xmlEntries) {
        $reader = New-Object System.IO.StreamReader($e.Open())
        try {
            $text = $reader.ReadToEnd()
            [void][xml]$text
            Write-Host ("[OK]   " + $e.FullName + " (" + $text.Length + " 字符)") -ForegroundColor Green
        } catch {
            $fail++
            Write-Host ("[FAIL] " + $e.FullName + " XML 解析失败: " + $_.Exception.Message) -ForegroundColor Red
        } finally {
            $reader.Dispose()
        }
    }
    if ($fail -gt 0) {
        Write-Host ("共 " + $fail + " 个 XML 文件非法") -ForegroundColor Red
        exit 1
    }
    Write-Host "XML 合法性守卫通过" -ForegroundColor Green
    exit 0
} finally {
    $zip.Dispose()
}
