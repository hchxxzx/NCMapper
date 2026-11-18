#requires -Version 5.1
<#
.SYNOPSIS
    全局配置族载体生成器。
.DESCRIPTION
    从 scripts/config-items.json 生成 7 载体文件的生成区内容（21 个生成区）。
    模式：
      默认（dry-run） : 输出全部生成区内容到 stdout（-Quiet 时仅摘要）
      -Verify         : 与手写代码对应段逐行比对，输出差异（首轮 diff 为零门禁）
      -Apply          : 将生成区内容写回载体文件（仅替换 <GENERATED:...> 标记对之间的内容）
    行尾与编码：输出 UTF-8 无 BOM；行尾跟随源文件（默认 CRLF）。
#>
param(
    [switch]$Apply,
    [switch]$Verify,
    [switch]$Mark,
    [switch]$Quiet
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$jsonPath = Join-Path $PSScriptRoot 'config-items.json'
$items = ([System.IO.File]::ReadAllText($jsonPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json).items

# ---------- 工具 ----------
function Get-Indent([int]$Level) { ' ' * ($Level * 4) }

function Get-Camel([string]$Name) {
    return $Name.Substring(0, 1).ToLowerInvariant() + $Name.Substring(1)
}


# ---------- 载体 1: MapDefaultsAttribute.cs（1 区）----------
function Render-AttrProps {
    $lines = @()
    foreach ($it in $items) {
        switch ($it.type) {
            'Enum'      { $lines += (Get-Indent 1) + 'public NamingStrategy ' + $it.attrName + ' { get; set; }' + $it.attrDefault + ';' }
            'StringList'{ $lines += (Get-Indent 1) + 'public string[]? ' + $it.attrName + ' { get; set; }' }
            'TypeList'  { $lines += (Get-Indent 1) + 'public Type[]? ' + $it.attrName + ' { get; set; }' }
        }
    }
    return $lines
}

# ---------- 载体 2: GeneratedMapConfig.cs（1 区）----------
function Render-GenProps {
    $lines = @()
    foreach ($it in $items) {
        if ($it.type -eq 'Enum') {
            $lines += (Get-Indent 1) + 'public int ' + $it.snapshotName + ' { get; set; }'
        } else {
            $lines += (Get-Indent 1) + 'public string[] ' + $it.snapshotName + ' { get; set; } = [];'
        }
    }
    return $lines
}

# ---------- 载体 3: GlobalConfigView.cs（3 区）----------
function Render-ViewProps {
    $lines = @()
    foreach ($it in $items) {
        if ($it.type -eq 'Enum') {
            $lines += (Get-Indent 1) + 'public NamingStrategy ' + $it.snapshotName + ' { get; }'
        } else {
            $lines += (Get-Indent 1) + 'public IReadOnlyList<string> ' + $it.snapshotName + ' { get; }'
        }
    }
    return $lines
}

function Render-ViewCtorParams {
    $lines = @()
    for ($i = 0; $i -lt $items.Count; $i++) {
        $it = $items[$i]
        $camel = Get-Camel $it.snapshotName
        if ($it.type -eq 'Enum') {
            $line = (Get-Indent 2) + 'NamingStrategy ' + $camel
        } else {
            $line = (Get-Indent 2) + 'IReadOnlyList<string>? ' + $camel
        }
        if ($i -lt $items.Count - 1) { $line += ',' } else { $line += ')' }
        $lines += $line
    }
    return $lines
}

function Render-ViewCtorAssign {
    $lines = @()
    foreach ($it in $items) {
        $camel = Get-Camel $it.snapshotName
        if ($it.type -eq 'Enum') {
            $lines += (Get-Indent 2) + $it.snapshotName + ' = ' + $camel + ';'
        } else {
            $lines += (Get-Indent 2) + $it.snapshotName + ' = Copy(' + $camel + ');'
        }
    }
    return $lines
}

# ---------- 载体 4: GlobalMapConfig.cs（7 区）----------
function Render-GmcProps {
    $lines = @()
    foreach ($it in $items) {
        if ($it.type -eq 'Enum') {
            $lines += (Get-Indent 1) + 'public NamingStrategy ' + $it.snapshotName + ' { get; }'
        } else {
            $lines += (Get-Indent 1) + 'public string[] ' + $it.snapshotName + ' { get; }'
        }
    }
    return $lines
}

function Render-GmcCtor {
    $lines = @()
    foreach ($it in $items) {
        if ($it.type -eq 'Enum') {
            $lines += (Get-Indent 2) + $it.snapshotName + ' = (NamingStrategy)snapshot.' + $it.snapshotName + ';'
        } else {
            $lines += (Get-Indent 2) + $it.snapshotName + ' = snapshot.' + $it.snapshotName + ';'
        }
    }
    return $lines
}

function Render-GmcGetView {
    $lines = @()
    $lines += (Get-Indent 3) + 'frozen.NamingStrategy,'
    $rest = @($items | Where-Object { $_.type -ne 'Enum' })
    for ($i = 0; $i -lt $rest.Count; $i += 2) {
        $a = $rest[$i].snapshotName
        if ($i + 1 -lt $rest.Count) {
            $line = (Get-Indent 3) + 'frozen.' + $a + ', frozen.' + $rest[$i + 1].snapshotName + ','
            if ($i + 2 -ge $rest.Count) { $line = $line.Substring(0, $line.Length - 1) + ');' }
        } else {
            $line = (Get-Indent 3) + 'frozen.' + $a + ');'
        }
        $lines += $line
    }
    return $lines
}

function Render-GmcFromAttr {
    $lines = @()
    foreach ($it in $items) {
        switch ($it.type) {
            'Enum' {
                $lines += (Get-Indent 3) + $it.snapshotName + ' = (int)attr.' + $it.attrName + ','
            }
            'StringList' {
                $lines += (Get-Indent 3) + $it.snapshotName + ' = attr.' + $it.attrName + ' ?? Array.Empty<string>(),'
            }
            'TypeList' {
                $lines += (Get-Indent 3) + $it.snapshotName + ' = (attr.' + $it.attrName + ' ?? Array.Empty<Type>())'
                $lines += (Get-Indent 4) + '.Select(t => t.FullName ?? t.Name).ToArray(),'
            }
        }
    }
    return $lines
}

function Render-GmcNormalize {
    $lines = @()
    foreach ($it in $items) {
        if ($it.type -eq 'Enum') {
            $lines += (Get-Indent 3) + $it.snapshotName + ' = config.' + $it.snapshotName + ','
        } else {
            $lines += (Get-Indent 3) + $it.snapshotName + ' = Normalized(config.' + $it.snapshotName + '),'
        }
    }
    return $lines
}

function Render-GmcContentEquals {
    $lines = @()
    $lines += (Get-Indent 2) + 'return a.NamingStrategy == b.NamingStrategy'
    $rest = @($items | Where-Object { $_.type -ne 'Enum' })
    for ($i = 0; $i -lt $rest.Count; $i++) {
        $line = (Get-Indent 3) + '&& SequenceEquals(a.' + $rest[$i].snapshotName + ', b.' + $rest[$i].snapshotName + ')'
        if ($i -eq $rest.Count - 1) { $line += ';' }
        $lines += $line
    }
    return $lines
}

function Render-GmcToSnapshot {
    $lines = @()
    foreach ($it in $items) {
        if ($it.type -eq 'Enum') {
            $lines += (Get-Indent 3) + $it.snapshotName + ' = (int)' + $it.snapshotName + ','
        } else {
            $lines += (Get-Indent 3) + $it.snapshotName + ' = ' + $it.snapshotName + ','
        }
    }
    return $lines
}

# ---------- 载体 5: GlobalConfigReader.cs（7 区）----------
function Render-RdrProps {
    $lines = @()
    foreach ($it in $items) {
        if ($it.type -eq 'Enum') {
            $lines += (Get-Indent 1) + 'public int ' + $it.snapshotName + ' { get; set; } = -1;'
        } else {
            $lines += (Get-Indent 1) + 'public string[]? ' + $it.snapshotName + ' { get; set; }'
        }
    }
    return $lines
}

function Render-RdrIsEmpty {
    $lines = @()
    $lines += (Get-Indent 2) + '=> NamingStrategy < 0'
    $rest = @($items | Where-Object { $_.type -ne 'Enum' })
    for ($i = 0; $i -lt $rest.Count; $i += 2) {
        $a = $rest[$i].snapshotName
        if ($i + 1 -lt $rest.Count) {
            $line = (Get-Indent 3) + '&& ' + $a + ' == null && ' + $rest[$i + 1].snapshotName + ' == null'
        } else {
            $line = (Get-Indent 3) + '&& ' + $a + ' == null'
        }
        $lines += $line
    }
    # 快照专属 bool 项（诊断开关，不属 11 项映射配置族）：作为生成区尾行并终结表达式（分号）
    $lines += (Get-Indent 3) + '&& ReportUnmappedTargetMembers == null && ReportUnmappedSourceMembers == null;'
    return $lines
}

function Render-RdrEquals {
    $lines = @()
    $lines += (Get-Indent 2) + 'return NamingStrategy == other.NamingStrategy'
    $rest = @($items | Where-Object { $_.type -ne 'Enum' })
    for ($i = 0; $i -lt $rest.Count; $i++) {
        $line = (Get-Indent 3) + '&& SequenceEqual(' + $rest[$i].snapshotName + ', other.' + $rest[$i].snapshotName + ')'
        $lines += $line
    }
    # 快照专属 bool 项（诊断开关）：作为生成区尾行并终结表达式（分号）
    $lines += (Get-Indent 3) + '&& ReportUnmappedTargetMembers == other.ReportUnmappedTargetMembers'
    $lines += (Get-Indent 3) + '&& ReportUnmappedSourceMembers == other.ReportUnmappedSourceMembers;'
    return $lines
}

function Render-RdrGetHash {
    $lines = @()
    $lines += (Get-Indent 3) + 'var hash = NamingStrategy;'
    $rest = @($items | Where-Object { $_.type -ne 'Enum' })
    foreach ($it in $rest) {
        $lines += (Get-Indent 3) + 'hash = (hash * 397) ^ HashList(' + $it.snapshotName + ');'
    }
    return $lines
}

function Get-ParseFn([string]$Type) {
    switch ($Type) {
        'Enum'      { return 'ParseNamingStrategy' }
        'StringList'{ return 'ParseStringList' }
        'TypeList'  { return 'ParseTypeFullNameList' }
    }
}

function Render-RdrReadMsBuild {
    $lines = @()
    foreach ($it in $items) {
        $fn = Get-ParseFn $it.type
        $lines += (Get-Indent 2) + 'snapshot.' + $it.snapshotName + ' = ' + $fn + '('
        $lines += (Get-Indent 3) + 'GetProperty(options, "' + $it.msbuildName + '"), "' + $it.msbuildName + '", snapshot);'
    }
    return $lines
}

function Render-RdrSwitch {
    $lines = @()
    foreach ($it in $items) {
        $lines += (Get-Indent 4) + 'case "' + $it.attrName + '":'
        if ($it.type -eq 'Enum') {
            $lines += (Get-Indent 5) + 'if (TryGetEnumValue(kvp.Value.Value, out var enumValue))'
            $lines += (Get-Indent 6) + 'snapshot.' + $it.snapshotName + ' = enumValue;'
            $lines += (Get-Indent 5) + 'else'
            $lines += (Get-Indent 6) + 'AddInvalid(snapshot, name, kvp.Value.Value?.ToString() ?? "<null>", "枚举值无法解析");'
        } elseif ($it.type -eq 'TypeList') {
            $lines += (Get-Indent 5) + 'snapshot.' + $it.snapshotName + ' = FromTypeConstantArray(kvp.Value, name, snapshot);'
        } else {
            $lines += (Get-Indent 5) + 'snapshot.' + $it.snapshotName + ' = FromConstantArray(kvp.Value, name, snapshot);'
        }
        $lines += (Get-Indent 5) + 'break;'
    }
    return $lines
}

function Render-RdrMerge {
    $lines = @()
    foreach ($it in $items) {
        if ($it.type -eq 'Enum') {
            $lines += (Get-Indent 2) + 'result.' + $it.snapshotName + ' = attribute?.' + $it.snapshotName + ' >= 0'
            $lines += (Get-Indent 3) + '? attribute!.' + $it.snapshotName
            $lines += (Get-Indent 3) + ': (msbuild?.' + $it.snapshotName + ' ?? -1);'
        } else {
            $lines += (Get-Indent 2) + 'result.' + $it.snapshotName + ' = Pick(attribute?.' + $it.snapshotName + ', msbuild?.' + $it.snapshotName + ');'
        }
    }
    # 快照专属 bool 项（诊断开关）：固定作为生成区尾行（B-6 程序集级选项合并）
    $lines += (Get-Indent 2) + 'result.ReportUnmappedTargetMembers = attribute?.ReportUnmappedTargetMembers ?? msbuild?.ReportUnmappedTargetMembers;'
    $lines += (Get-Indent 2) + 'result.ReportUnmappedSourceMembers = attribute?.ReportUnmappedSourceMembers ?? msbuild?.ReportUnmappedSourceMembers;'
    return $lines
}

# ---------- 载体 6: CodeGenerator.cs（1 区）----------
function Render-CgOutput {
    $lines = @()
    foreach ($it in $items) {
        if ($it.type -eq 'Enum') {
            $lines += (Get-Indent 2) + 'if (config.' + $it.snapshotName + ' >= 0)'
            $lines += (Get-Indent 3) + 'sb.AppendLine($"                ' + $it.snapshotName + ' = {config.' + $it.snapshotName + '},");'
        } else {
            $lines += (Get-Indent 2) + 'AppendListProperty(sb, "' + $it.snapshotName + '", config.' + $it.snapshotName + ');'
        }
    }
    return $lines
}

# ---------- 载体 7: NCMapper.targets（1 区）----------
function Render-TgtCvp {
    $lines = @()
    foreach ($it in $items) {
        $lines += (Get-Indent 1) + '<CompilerVisibleProperty Include="' + $it.msbuildName + '" />'
    }
    return $lines
}

# ---------- 生成区注册表 ----------
# 每项: 区名 / 载体文件（相对仓库根） / 渲染函数 / 首行匹配 / 末行匹配（正则；末行模式含 $ 表示"该行后结束"）
$sections = @(
    @{ Name = 'attr-props';      File = 'src/NCMapper/MapDefaultsAttribute.cs';     Render = 'Render-AttrProps';       Filter = '^\s*public\s'; First = 'public NamingStrategy NamingStrategy { get; set; }'; Last = 'public string\[\]\? IgnoreNameSuffixesToEntity \{ get; set; \}' },
    @{ Name = 'gen-props';       File = 'src/NCMapper/GeneratedMapConfig.cs';        Render = 'Render-GenProps';        Filter = '^\s*public\s'; First = 'public int NamingStrategy \{ get; set; \}'; Last = 'public string\[\] IgnoreNameSuffixesToEntity \{ get; set; \} = \[\];' },
    @{ Name = 'view-props';      File = 'src/NCMapper/GlobalConfigView.cs';          Render = 'Render-ViewProps';       Filter = '^\s*public\s'; First = 'public NamingStrategy NamingStrategy \{ get; \}'; Last = 'public IReadOnlyList<string> IgnoreNameSuffixesToEntity \{ get; \}' },
    @{ Name = 'view-ctor-params';File = 'src/NCMapper/GlobalConfigView.cs';          Render = 'Render-ViewCtorParams';  First = 'NamingStrategy namingStrategy,'; Last = 'IReadOnlyList<string>\? ignoreNameSuffixesToEntity\)' },
    @{ Name = 'view-ctor-assign';File = 'src/NCMapper/GlobalConfigView.cs';          Render = 'Render-ViewCtorAssign';  First = 'NamingStrategy = namingStrategy;'; Last = 'IgnoreNameSuffixesToEntity = Copy\(ignoreNameSuffixesToEntity\);' },
    @{ Name = 'gmc-props';       File = 'src/NCMapper/GlobalMapConfig.cs';           Render = 'Render-GmcProps';        Filter = '^\s*public\s'; First = 'public NamingStrategy NamingStrategy \{ get; \}'; Last = 'public string\[\] IgnoreNameSuffixesToEntity \{ get; \}' },
    @{ Name = 'gmc-ctor';        File = 'src/NCMapper/GlobalMapConfig.cs';           Render = 'Render-GmcCtor';         First = 'NamingStrategy = \(NamingStrategy\)snapshot.NamingStrategy;'; Last = 'IgnoreNameSuffixesToEntity = snapshot.IgnoreNameSuffixesToEntity;' },
    @{ Name = 'gmc-getview';     File = 'src/NCMapper/GlobalMapConfig.cs';           Render = 'Render-GmcGetView';      First = 'frozen.NamingStrategy,'; Last = 'frozen.IgnoreNamePrefixesToEntity, frozen.IgnoreNameSuffixesToEntity\);' },
    @{ Name = 'gmc-fromattr';    File = 'src/NCMapper/GlobalMapConfig.cs';           Render = 'Render-GmcFromAttr';     First = 'NamingStrategy = \(int\)attr.NamingStrategy,'; Last = 'IgnoreNameSuffixesToEntity = attr.IgnoreNameSuffixesToEntity \?\? Array.Empty<string>\(\),' },
    @{ Name = 'gmc-normalize';   File = 'src/NCMapper/GlobalMapConfig.cs';           Render = 'Render-GmcNormalize';    First = 'NamingStrategy = config.NamingStrategy,'; Last = 'IgnoreNameSuffixesToEntity = Normalized\(config.IgnoreNameSuffixesToEntity\),' },
    @{ Name = 'gmc-contentequals';File = 'src/NCMapper/GlobalMapConfig.cs';          Render = 'Render-GmcContentEquals'; First = 'return a.NamingStrategy == b.NamingStrategy'; Last = '&& SequenceEquals\(a.IgnoreNameSuffixesToEntity, b.IgnoreNameSuffixesToEntity\);' },
    @{ Name = 'gmc-tosnapshot';  File = 'src/NCMapper/GlobalMapConfig.cs';           Render = 'Render-GmcToSnapshot';   First = 'NamingStrategy = \(int\)NamingStrategy,'; Last = 'IgnoreNameSuffixesToEntity = IgnoreNameSuffixesToEntity,' },
    @{ Name = 'rdr-props';       File = 'src/NCMapper.SourceGen/GlobalConfigReader.cs'; Render = 'Render-RdrProps';      Filter = '^\s*public\s'; First = 'public int NamingStrategy \{ get; set; \} = -1;'; Last = 'public string\[\]\? IgnoreNameSuffixesToEntity \{ get; set; \}' },
    @{ Name = 'rdr-isempty';     File = 'src/NCMapper.SourceGen/GlobalConfigReader.cs'; Render = 'Render-RdrIsEmpty';     First = '=> NamingStrategy < 0'; Last = '&& ReportUnmappedTargetMembers == null && ReportUnmappedSourceMembers == null;' },
    @{ Name = 'rdr-equals';      File = 'src/NCMapper.SourceGen/GlobalConfigReader.cs'; Render = 'Render-RdrEquals';      First = 'return NamingStrategy == other.NamingStrategy'; Last = '&& ReportUnmappedSourceMembers == other.ReportUnmappedSourceMembers;' },
    @{ Name = 'rdr-gethash';     File = 'src/NCMapper.SourceGen/GlobalConfigReader.cs'; Render = 'Render-RdrGetHash';      First = 'var hash = NamingStrategy;'; Last = 'hash = \(hash \* 397\) \^ HashList\(IgnoreNameSuffixesToEntity\);' },
    @{ Name = 'rdr-readmsbuild'; File = 'src/NCMapper.SourceGen/GlobalConfigReader.cs'; Render = 'Render-RdrReadMsBuild';   First = 'snapshot.NamingStrategy = ParseNamingStrategy\('; Last = 'GetProperty\(options, "NCMapperIgnoreNameSuffixesToEntity"\), "NCMapperIgnoreNameSuffixesToEntity", snapshot\);' },
    @{ Name = 'rdr-switch';      File = 'src/NCMapper.SourceGen/GlobalConfigReader.cs'; Render = 'Render-RdrSwitch';       First = 'case "NamingStrategy":'; Last = 'case "IgnoreNameSuffixesToEntity":' },
    @{ Name = 'rdr-merge';       File = 'src/NCMapper.SourceGen/GlobalConfigReader.cs'; Render = 'Render-RdrMerge';        First = 'result.NamingStrategy = attribute\?.NamingStrategy >= 0'; Last = 'result.ReportUnmappedSourceMembers = attribute\?.ReportUnmappedSourceMembers \?\? msbuild\?.ReportUnmappedSourceMembers;' },
    @{ Name = 'cg-output';       File = 'src/NCMapper.SourceGen/CodeGenerator.cs';    Render = 'Render-CgOutput';        First = 'if \(config.NamingStrategy >= 0\)'; Last = 'AppendListProperty\(sb, "IgnoreNameSuffixesToEntity", config.IgnoreNameSuffixesToEntity\);' },
    @{ Name = 'tgt-cvp';         File = 'src/NCMapper/NCMapper.targets';              Render = 'Render-TgtCvp';          First = '<CompilerVisibleProperty Include="NCMapperNamingStrategy" />'; Last = '<CompilerVisibleProperty Include="NCMapperIgnoreNameSuffixesToEntity" />' }
)

# ---------- 标记语法分派（按载体文件类型选择注释语法，修复 targets XML 非法缺陷） ----------
function Get-TagOpen {
    param([string]$File, [string]$Name)
    if ($File -match '\.(targets|props|xml)$') { return '<!-- GENERATED:' + $Name + ' -->' }
    return '// <GENERATED:' + $Name + '>'
}

function Get-TagClose {
    param([string]$File, [string]$Name)
    if ($File -match '\.(targets|props|xml)$') { return '<!-- /GENERATED:' + $Name + ' -->' }
    return '// </GENERATED:' + $Name + '>'
}

# ---------- 主流程 ----------
if (-not $Quiet) {
    Write-Host "全局配置族生成器：$($items.Count) 项 / $($sections.Count) 个生成区" -ForegroundColor Cyan
}

# 按文件分组渲染（dry-run 输出）
if (-not $Apply -and -not $Verify -and -not $Mark) {
    foreach ($s in $sections) {
        Write-Host ("--- " + $s.File + " :: " + $s.Name + " ---")
        & $s.Render | ForEach-Object { Write-Output $_ }
    }
    exit 0
}

# -Mark：在载体文件中插入生成区标记对（首轮固化用，幂等）
if ($Mark) {
    foreach ($s in $sections) {
        $path = Join-Path $root ($s.File -replace '/', '\')
        $srcLines = @([System.IO.File]::ReadAllLines($path))
        $openTag = Get-TagOpen -File $s.File -Name $s.Name
        $closeTag = Get-TagClose -File $s.File -Name $s.Name
        $firstIdx = -1
        for ($i = 0; $i -lt $srcLines.Count; $i++) {
            if ($srcLines[$i].TrimEnd("`r") -match $s.First) { $firstIdx = $i; break }
        }
        $lastIdx = -1
        for ($i = $firstIdx + 1; $i -lt $srcLines.Count; $i++) {
            if ($srcLines[$i].TrimEnd("`r") -match $s.Last) { $lastIdx = $i }
        }
        if ($firstIdx -lt 0 -or $lastIdx -lt 0) {
            Write-Host ("[FAIL] " + $s.Name + " 定位失败") -ForegroundColor Red
            continue
        }
        $endIdx = $lastIdx
        if ($s.Name -eq 'rdr-switch') { $endIdx = $lastIdx + 2 }
        # 幂等：已存在标记则跳过
        if ($firstIdx -gt 0 -and $srcLines[$firstIdx - 1].TrimEnd("`r") -eq $openTag) {
            Write-Host ("[SKIP] " + $s.Name + " 已有标记") -ForegroundColor Yellow
            continue
        }
        # 标记行缩进跟随内容行（类内 4 空格 / 方法内 8 空格等）
        $firstIndent = ($srcLines[$firstIdx].TrimEnd("`r") -replace '^(\s*).*$', '$1')
        $lastIndent = ($srcLines[$endIdx].TrimEnd("`r") -replace '^(\s*).*$', '$1')
        $openLine = $firstIndent + $openTag
        $closeLine = $lastIndent + $closeTag
        $newLines = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt $firstIdx; $i++) { $newLines.Add($srcLines[$i].TrimEnd("`r")) }
        $newLines.Add($openLine)
        for ($i = $firstIdx; $i -le $endIdx; $i++) { $newLines.Add($srcLines[$i].TrimEnd("`r")) }
        $newLines.Add($closeLine)
        for ($i = $endIdx + 1; $i -lt $srcLines.Count; $i++) { $newLines.Add($srcLines[$i].TrimEnd("`r")) }
        $eol = "`r`n"
        $fullText = [System.IO.File]::ReadAllText($path)
        if (-not $fullText.Contains("`r`n")) { $eol = "`n" }
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($path, ($newLines -join $eol), $utf8NoBom)
        Write-Host ("[OK] " + $s.Name + " 标记已插入 (L" + ($firstIdx + 1) + "-L" + ($endIdx + 1) + ")") -ForegroundColor Green
    }
    exit 0
}

# -Verify：与手写代码对应段逐行比对
if ($Verify) {
    $totalDiff = 0
    foreach ($s in $sections) {
        $path = Join-Path $root ($s.File -replace '/', '\')
        $srcLines = [System.IO.File]::ReadAllLines($path)
        $rendered = @(& $s.Render)
        # 定位首行
        $firstIdx = -1
        for ($i = 0; $i -lt $srcLines.Count; $i++) {
            if ($srcLines[$i].TrimEnd("`r") -match $s.First) { $firstIdx = $i; break }
        }
        # 定位末行（从首行之后找最后一个匹配）
        $lastIdx = -1
        for ($i = $firstIdx + 1; $i -lt $srcLines.Count; $i++) {
            if ($srcLines[$i].TrimEnd("`r") -match $s.Last) { $lastIdx = $i }
        }
        if ($firstIdx -lt 0 -or $lastIdx -lt 0) {
            Write-Host ("[FAIL] " + $s.Name + " 定位失败 first=" + $firstIdx + " last=" + $lastIdx) -ForegroundColor Red
            $totalDiff++
            continue
        }
        # rdr-switch 特殊：末 case 块含后续 2 行（赋值 + break）
        $endIdx = $lastIdx
        if ($s.Name -eq 'rdr-switch') { $endIdx = $lastIdx + 2 }
        $actual = @()
        for ($i = $firstIdx; $i -le $endIdx; $i++) { $actual += $srcLines[$i].TrimEnd("`r") }
        # 属性段：XML 注释留手工区，仅比对声明行
        if ($s.Filter) { $actual = @($actual | Where-Object { $_ -match $s.Filter }) }
        if ($actual.Count -ne $rendered.Count) {
            Write-Host ("[FAIL] " + $s.Name + " 行数不符 手写=" + $actual.Count + " 生成=" + $rendered.Count) -ForegroundColor Red
            $totalDiff++
            continue
        }
        $diffCnt = 0
        for ($i = 0; $i -lt $actual.Count; $i++) {
            if ($actual[$i] -ne $rendered[$i]) {
                $diffCnt++
                Write-Host ("[DIFF] " + $s.Name + " L" + ($firstIdx + $i + 1)) -ForegroundColor Yellow
                Write-Host ("  手写: " + $actual[$i])
                Write-Host ("  生成: " + $rendered[$i])
            }
        }
        if ($diffCnt -eq 0) {
            Write-Host ("[OK] " + $s.Name + " (" + $actual.Count + " 行)") -ForegroundColor Green
        } else {
            $totalDiff += $diffCnt
        }
    }
    if ($totalDiff -eq 0) {
        Write-Host "首轮生成 diff = 零：全部生成区与手写代码一致" -ForegroundColor Green
        exit 0
    } else {
        Write-Host ("diff 非零：共 " + $totalDiff + " 处差异") -ForegroundColor Red
        exit 1
    }
}

# -Apply：替换生成区标记对之间的内容
if ($Apply) {
    foreach ($s in $sections) {
        $path = Join-Path $root ($s.File -replace '/', '\')
        $content = [System.IO.File]::ReadAllText($path)
        $openTag = Get-TagOpen -File $s.File -Name $s.Name
        $closeTag = Get-TagClose -File $s.File -Name $s.Name
        # 定位：before 含缩进与完整 openTag 行；after 从 closeTag 行首（含缩进）开始
        $tagStart = $content.IndexOf($openTag)
        $closeStart = $content.IndexOf($closeTag)
        if ($tagStart -lt 0 -or $closeStart -lt 0) {
            Write-Host ("[SKIP] " + $s.Name + " 无生成区标记（首轮需手工打标记）") -ForegroundColor Yellow
            continue
        }
        $lineStart = $closeStart
        while ($lineStart -gt 0 -and $content[$lineStart - 1] -ne "`n" -and $content[$lineStart - 1] -ne "`r") { $lineStart-- }
        $before = $content.Substring(0, $tagStart + $openTag.Length)
        $inner = $content.Substring($tagStart + $openTag.Length, $lineStart - $tagStart - $openTag.Length)
        $after = $content.Substring($lineStart)
        # 行尾跟随源文件（本地 CRLF / CI LF）
        $eol = "`r`n"
        if (-not $content.Contains("`r`n")) { $eol = "`n" }
        $rendered = @(& $s.Render)
        # 属性段（Filter）：按属性名对齐单元（注释块 + 声明行），支持新增/删除；
        # 既有项注释保留（手工区），新增项注释由清单 comment 生成
        if ($s.Filter) {
            $innerLines = New-Object System.Collections.Generic.List[string]
            foreach ($ln in ($inner -split "`r?`n")) { $innerLines.Add($ln) }
            while ($innerLines.Count -gt 0 -and $innerLines[0] -eq '') { $innerLines.RemoveAt(0) }
            while ($innerLines.Count -gt 0 -and $innerLines[$innerLines.Count - 1] -eq '') { $innerLines.RemoveAt($innerLines.Count - 1) }
            # 提取区内单元：声明行 + 上方连续 /// 注释块 + 下方连续空行
            $units = New-Object System.Collections.Generic.List[object]  # @{ Name; Comment; Decl; Trail }
            for ($i = 0; $i -lt $innerLines.Count; $i++) {
                if ($innerLines[$i] -match $s.Filter) {
                    $decl = $innerLines[$i]
                    $name = ''
                    if ($decl -match '\s(\w+)\s*\{ get') { $name = $Matches[1] }
                    $comment = New-Object System.Collections.Generic.List[string]
                    $j = $i - 1
                    while ($j -ge 0 -and $innerLines[$j].TrimStart().StartsWith('///')) { $comment.Insert(0, $innerLines[$j]); $j-- }
                    $trail = New-Object System.Collections.Generic.List[string]
                    $k = $i + 1
                    while ($k -lt $innerLines.Count -and $innerLines[$k].Trim() -eq '') { $trail.Add($innerLines[$k]); $k++ }
                    $units.Add(@{ Name = $name; Comment = $comment; Decl = $decl; Trail = $trail })
                }
            }
            # 重建：按渲染顺序输出（同名保留注释与尾部空行；新增生成注释；删除丢弃）
            $out = New-Object System.Collections.Generic.List[string]
            foreach ($rd in $rendered) {
                $rdName = ''
                if ($rd -match '\s(\w+)\s*\{ get') { $rdName = $Matches[1] }
                $unit = $null
                foreach ($u in $units) { if ($u.Name -eq $rdName) { $unit = $u; break } }
                if ($unit -ne $null) {
                    foreach ($cl in $unit.Comment) { $out.Add($cl) }
                    $out.Add($rd)
                    foreach ($tl in $unit.Trail) { $out.Add($tl) }
                } else {
                    # 新增项：注释由清单 comment 生成，尾部补一个空行与下一单元分隔
                    $it = $null
                    foreach ($ii in $items) { if ($ii.snapshotName -eq $rdName -or $ii.attrName -eq $rdName) { $it = $ii; break } }
                    $indent = ($rd -replace '^(\s*).*$', '$1')
                    $out.Add($indent + '/// <summary>')
                    $out.Add($indent + '/// ' + $(if ($it) { $it.comment } else { $rdName }))
                    $out.Add($indent + '/// </summary>')
                    $out.Add($rd)
                    $out.Add('')
                }
            }
            $renderedText = ($out -join $eol)
        } else {
            $renderedText = ($rendered -join $eol)
        }
        $content = $before + $eol + $renderedText + $eol + $after
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($path, $content, $utf8NoBom)
        Write-Host ("[OK] " + $s.Name + " 已更新") -ForegroundColor Green
    }
}
