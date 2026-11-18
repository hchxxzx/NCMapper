# exception-doc-coverage.ps1 — 公共 API <exception> 声明覆盖率统计
# 口径（对齐专项5 报告 A-005-F1）：分母 = 公共方法 IL 中直接 newobj MapException/MapConfigurationException 的方法；
# 分子 = NCMapper.xml（XML Doc）中含 <exception cref="T:NCMapper.MapException"> 或 MapConfigurationException 的公共方法。
# 用法：powershell -ExecutionPolicy Bypass -File scripts/exception-doc-coverage.ps1 -DllPath <NCMapper.dll> -XmlDoc <NCMapper.xml>

param(
    [string]$DllPath = "src/NCMapper/bin/Release/net10.0/NCMapper.dll",
    [string]$XmlDoc = "src/NCMapper/obj/Release/net10.0/NCMapper.xml"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dllFull = Join-Path $root $DllPath
$xmlFull = Join-Path $root $XmlDoc

# ── 1. IL 分析：收集直接 newobj MapException 的公共方法 ──
$throwMethods = [System.Collections.Generic.Dictionary[string,string]]::new()  # 方法名 -> 全名(用于定位)
$asm = [System.Reflection.Assembly]::LoadFrom($dllFull)

function Test-ThrowsMapException([System.Reflection.MethodBase]$method) {
    try {
        $body = $method.GetMethodBody()
        if ($null -eq $body) { return $false }
        $il = $body.GetILAsByteArray()
        if ($null -eq $il) { return $false }
        for ($i = 0; $i -lt $il.Length - 4; $i++) {
            $op = $il[$i]
            # newobj=0x73, ldtoken=0x72, call=0x28, callvirt=0x6F（覆盖直接 throw 与内联调用）
            if ($op -eq 0x73 -or $op -eq 0x72) {
                $token = [System.BitConverter]::ToInt32($il, $i + 1)
                try { $member = $method.Module.ResolveMember($token) } catch { continue }
                if ($null -ne $member -and $null -ne $member.DeclaringType) {
                    if ($member.DeclaringType.FullName -match '\.MapException$|\.MapConfigurationException$') {
                        return $true
                    }
                }
            }
        }
    } catch { }
    return $false
}

foreach ($type in $asm.GetExportedTypes()) {
    $flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::DeclaredOnly
    foreach ($method in $type.GetMethods($flags)) {
        if (Test-ThrowsMapException $method) {
            $key = "$($type.Name).$($method.Name)"
            $full = "$($type.FullName).$($method.Name)"
            [void]$throwMethods.TryAdd($key, $full)
        }
    }
    foreach ($ctor in $type.GetConstructors($flags)) {
        if (Test-ThrowsMapException $ctor) {
            $key = "$($type.Name).$($type.Name)"
            $full = "$($type.FullName)..ctor"
            [void]$throwMethods.TryAdd($key, $full)
        }
    }
}

# ── 2. 解析 XML Doc 中公共成员 exception 声明 ──
$declared = [System.Collections.Generic.Dictionary[string,string]]::new()
if (Test-Path -LiteralPath $xmlFull) {
    [xml]$doc = [System.IO.File]::ReadAllText($xmlFull, [System.Text.Encoding]::UTF8)
    foreach ($member in $doc.doc.members.member) {
        if ($member.name -notlike 'M:NCMapper.*') { continue }
        foreach ($e in $member.SelectNodes('exception')) {
            if ($e.cref -match 'MapException') {
                if ($member.name -match 'M:NCMapper\.([\w.]+)\.(\w+(?:``\d+)?)\(') {
                    $key = "$($matches[1]).$($matches[2])"
                    [void]$declared.TryAdd($key, $member.name)
                }
            }
        }
    }
}

# ── 3. 汇总（短名匹配：Class.Method）──
$total = $throwMethods.Count
$matched = 0
$missing = [System.Collections.Generic.List[string]]::new()
foreach ($kv in $throwMethods) {
    $shortKey = $kv.Key
    $hit = $declared.Keys | Where-Object { $_ -eq $shortKey }
    if ($hit) { $matched++ } else { [void]$missing.Add($kv.Value) }
}
$coverage = if ($total -gt 0) { [math]::Round($matched * 100.0 / $total, 1) } else { 0 }

Write-Output "=== exception 声明覆盖率（IL 口径）==="
Write-Output "抛 MapException 公共方法数（IL newobj 检测）: $total"
Write-Output "XML Doc 声明数（exception cref MapException）: $($declared.Count)"
Write-Output "匹配声明数: $matched"
Write-Output "覆盖率: $coverage%"
Write-Output ""
Write-Output "未声明方法清单（供补标注）:"
$missing | Sort-Object | ForEach-Object { Write-Output "  - $_" }
