# Fluent Member grammar structural negative cases gate.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-fluent-negative-cases.ps1 [-Mode coexist|final]
#   final (default, 断代后终态) = 18/18 blocked; N2/N3 实测码翻转 CS1501/CS0305 → CS1061（旧成员删除后
#                       "有方法无此参"变"成员无定义"——翻转落点，实测回填）
#                       N11~N18 = 长尾方法族扩展（字段段终态后续接 / 对级与入口根接收者缺失 / 泛型元数与
#                       委托元数三类误用形态，逐文件探针实测码回填）
#   coexist           = 并存期历史态（断代前快照：7/8 blocked + N5 零错误例外）——断代后不可复现，参数保留仅供登记
# Assertion: per-case expected error code + blocked count; build FAILURE is the expected carrier form
# (exit code ignored; per-file diagnostics parsed instead).
param([string]$Mode = 'final')
$ErrorActionPreference = 'Continue'
if ($Mode -ne 'coexist' -and $Mode -ne 'final') { Write-Error "unknown mode: $Mode"; exit 2 }
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'test\NCMapper.FluentNegativeCases\NCMapper.FluentNegativeCases.csproj'
$log = [System.IO.Path]::GetTempFileName()
dotnet build $proj --configuration Release --nologo > $log 2>&1

# dual-state expectation table (interim measured values)
$expect = @{
  'N1_ConfigThenFrom.cs'            = 'CS1061'
  'N2_FromThenIgnore.cs'            = 'CS1501'
  'N3_IgnoreThenMask.cs'            = 'CS0305'
  'N4_SecondFrom.cs'                = 'CS1061'
  'N6_BareMemberThenMember.cs'      = 'CS1061'
  'N7_BareMemberThenTree.cs'        = 'CS1061'
  'N8_ConfigThenFieldFromSource.cs' = 'CS1061'
}
if ($Mode -eq 'final') {
  # 终态翻转（断代清扫后实测回填）：N5 恢复拦截；
  # N2/N3 旧成员删除后由 CS1501/CS0305 变 CS1061（成员无定义）
  $expect['N2_FromThenIgnore.cs']        = 'CS1061'
  $expect['N3_IgnoreThenMask.cs']        = 'CS1061'
  $expect['N5_SecondNoAutoInclude.cs']   = 'CS1061'
  # 扩展用例（N9/N10 = Convert/ConvertBack 旧形态 (dest, converter) 双参；
  # 实测码回填）——锁定"旧形态无编译来源"前提（断代清扫地基）
  $expect['N9_OldFormConvertWithDestLambda.cs']      = 'CS1503'
  $expect['N10_OldFormConvertBackWithDestLambda.cs'] = 'CS1061'
  # 长尾方法族扩展（N11~N18，逐文件探针实测回填）——字段段终态后续接（N11/N13）、
  # 对级与入口根接收者缺失（N12/N14/N18）、泛型元数与委托元数（N15/N16/N17）
  $expect['N11_IgnoreThenFormat.cs']              = 'CS1061'
  $expect['N12_FromJsonOnPairConfig.cs']          = 'CS1061'
  $expect['N13_IgnoreThenMapTree.cs']             = 'CS1061'
  $expect['N14_NoAutoMatchOnConfig.cs']           = 'CS1061'
  $expect['N15_MapCtorParamMissingArg.cs']        = 'CS1501'
  $expect['N16_MapSubclassSingleTypeArg.cs']      = 'CS0305'
  $expect['N17_AfterMappingWrongArity.cs']        = 'CS1593'
  $expect['N18_BeforeMappingOnConfig.cs']         = 'CS1061'
}

$fail = 0; $blocked = 0
Get-ChildItem -Path (Join-Path $repo 'test\NCMapper.FluentNegativeCases') -Filter 'N*.cs' -File | ForEach-Object {
  $base = $_.Name
  $codes = Select-String -Path $log -Pattern $base -SimpleMatch | ForEach-Object {
    if ($_.Line -match 'error ([A-Za-z]+\d+)') { $Matches[1] }
  } | Sort-Object -Unique
  $act = $codes -join ' '
  $exp = $null; if ($expect.ContainsKey($base)) { $exp = $expect[$base] }
  if (-not $exp) {
    # no table entry = interim N5 exception: expect zero errors
    if (-not $codes) {
      Write-Output "PASS  $base  (interim zero-error - N5 exception, legacy pair-level name in place)"
    } else {
      Write-Output "FAIL  $base  expect zero errors (interim), actual: $act"; $fail++
    }
    return
  }
  if ($codes -contains $exp) { $blocked++; Write-Output "PASS  $base  ($exp)" }
  else { Write-Output "FAIL  $base  expect $exp, actual: $(if ($act) { $act } else { '<none>' })"; $fail++ }
}
Remove-Item $log -ErrorAction SilentlyContinue
$expectedBlocked = 7; if ($Mode -eq 'final') { $expectedBlocked = 18 }
Write-Output "----"
Write-Output "mode=$Mode blocked=$blocked/$expectedBlocked failed_files=$fail"
if ($fail -ne 0 -or $blocked -ne $expectedBlocked) { exit 1 }
Write-Output 'FLUENT-NEGATIVE-CASES PASS'
