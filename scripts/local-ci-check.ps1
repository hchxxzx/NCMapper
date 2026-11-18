<#
.SYNOPSIS
本地 CI 门禁编排脚本——在推送前把 ci.yml 里可在本机复现的 job 全部跑一遍，先本地验证再推送。

.DESCRIPTION
镜像 .github/workflows/ci.yml 的 job 结构，复用仓库既有的 .ps1 门禁脚本（check-requires-messages /
gen-global-config / check-locales-drift / check-aot-il-warnings / verify-generated-baseline），
避免"推到 GitHub 才知道红"——GitHub Actions 只是把同样的命令放到干净 runner 上跑，本地先跑一遍
是发布纪律的第一道闸。

默认阶段集合 = scan / build / test / equivalence-gate / aot / samples / generated-baseline
（唯一口径见 param 默认值与 $allPhases，本注不重复计数）。
重型或依赖环境的阶段用参数显式开启：pack（SourceLink 需 CI 单 remote，本地仅弱验证）、
benchmark（时长+观察期）。任一阶段失败即聚合退出码非零。

equivalence-gate 阶段（本地门激活）调用既有 scripts/verify-equivalence-gate.ps1
（不改 gate 脚本本体），语义 = 三点 diff 触发面/义务面判定 + BaseRef 不可解析时 fail closed。
本地直推工作流的 BaseRef 取**直推起点 commit**（直推语义），用 -GateBaseRef 传入；
不传则沿 gate 脚本默认 origin/main（CI 侧 PR-only 语义，本地多 remote 下区间会偏大）。

scan 阶段（机械护栏）串联 U+FFFD 扫描（scripts/scan-ufffd.ps1）、注释锚点校验
（scripts/check-doc-anchors.py）、注释符号引用门（scripts/check-comment-symbol-refs.ps1）、
注释过程噪声门（scripts/check-comment-process-noise.ps1），以及块五桶对账门
（scripts/check-block-buckets.py）与演示覆盖对账门（scripts/check-demo-coverage.sh）。
后两支取【退出码硬门】沿脚本本体设计语义：buckets 0 绿 / 1 断言红 / 2 假门禁防护红，
coverage 告警版本体 exit 恒 0、对账源缺失 exit 2 红；anchors 已在 scan，故差集只补这两支。
前两者采用【基线比对判定】而非 Invoke-Check 的
"退出码非零即失败"语义——只要与登记基线一致即 PASS、偏离即 FAIL。当前两个基线均已归零
（见 scripts/README.md）。符号引用门为【退出码硬门】（0 绿 / 1 红 / 2 参数错均判红）；
HISTORICAL 行 informational 透出不判红；scanned 引用数 = 0 视为空转判红（vacuous-pass 护栏）。
过程噪声门脚本本体同为【退出码硬门】，挂载腿取【红面基线比对判定】：存量全量命中已入库
（scripts/tests/fixtures/comment-noise/redface-<日期>.txt），当前命中 ⊆ 基线即 PASS（存量只减
不增合法），出现基线外新命中即 FAIL——存量红面不得让 scan 阶段恒红，否则出口门不可达；
"门禁本体自身 exit 0"的验收判据由直跑脚本承载。informational（状态词 / 日期）透出不判红。
Python 解释器缺失时锚点校验经 WSL python3 回退实跑（预检失败输出 [SKIP] + WARN——环境降级
显式化，不落 [L1] 缺失假红）。
文档告警构建耗时较长，默认不进 scan，用 -IncludeDocWarnings 显式开启。

本地与 CI 的环境差异（本脚本已处理或需注意）：
  1. build 阶段本地需 -p:EnableSourceLink=false（多 remote 场景 SourceLink 不可用）；CI 单 remote 不加。
  1b. build 阶段本地需 --no-incremental：增量构建会跳过未改动项目——
      仅改核心库时 NCMapper.SourceGen 被 up-to-date 跳过，其承载的 CS 空态报错站点不参与编译，
      "零错误"判定假绿。CI 全新 clone 天然全量，不加。
  2. aot 阶段 NativeAOT publish 需要本机具备 MSVC + Windows SDK 环境变量
     （VCINSTALLDIR/WindowsSdkDir/WindowsSDKVersion/INCLUDE/LIB，见 MEMORY.md）；本机 reg.exe
     被安全策略拦截需手动预设。若环境变量缺失，脚本给出提示并建议用 -SkipAotEnvCheck 显式跳过
     环境检查（仅跳过前置校验，仍执行 publish，让真实错误暴露）。
  3. pack 阶段 SourceLink 嵌入断言在本地多 remote 下必然失败，故默认不跑；如需观察打包产物完整性，
     用 -RunPack（会输出 SourceLink 相关的已知差异说明，不阻塞其它阶段）。
  4. benchmark 阶段观察期 continue-on-error:true，默认不跑，用 -RunBenchmark 显式开启。
  5. NuGetAudit 探测门控：开跑时一次探测审计源可达性
     （api.nuget.org v3 index，HEAD，5s 超时；powershell5 回落链前置 Tls12——PS 5.1 旧默认
     TLS 连不上 nuget.org 会假阴性误关审计；若设 HTTP(S)_PROXY 则探测同走代理——与 NuGet
     的网络视图对齐，防代理环境下探测误判在线）。在线 → 不传属性、NuGet 审计实跑，真实
     NU1901-1904 漏洞告警经 --warnaserror 照升红（在线信号恢复）；不可达 → restore/build/
     samples 各 --warnaserror 腿条件直传 -p:NuGetAudit=false（直办实证的稳定通道：
     属性走构建请求必达 MSBuild 复用节点，环境变量到不了已活节点故不稳）+ 门级 WARN
     （环境码不可判，非审计结论；探测结果不入 PASS/FAIL 判定）。
     本地审计代偿执行点：CI 在线审计 + 手动配方 dotnet list package --vulnerable
     --include-transitive（NuGetAuditMode=direct 仅审计直接依赖，该配方含传递依赖为净扩大；
     无 PASS/FAIL 退出码语义、不适配门禁腿模型，故仅作人工观察）。

.PARAMETER Phase
要执行的阶段子集（数组，逗号分隔），默认 = 本脚本 param 块写死的默认阶段集合（与 $allPhases 同口径，
不在注释里重复计数）。合法值见 $allPhases。

.PARAMETER GateBaseRef
equivalence-gate 阶段的 git diff 基准（直推起点 commit，直推语义）。留空 = 沿 verify-equivalence-gate.ps1
自身默认（origin/main）。BaseRef 不可解析时 gate fail closed（退出码非零）。

.PARAMETER IncludeDocWarnings
scan 阶段追加"文档告警构建"（SourceGen 工程 -p:GenerateDocumentationFile=true，含 --no-incremental），
按 scripts/README.md §3 登记的基线值判定"零新增"。默认关闭（构建耗时较长）。

.PARAMETER RepoRoot
仓库根目录（默认取脚本所在目录的上一级）。

.PARAMETER SkipAotEnvCheck
aot 阶段跳过 MSVC/Windows SDK 环境变量前置校验（仍执行 publish，让真实错误暴露）。

.PARAMETER RunPack
显式包含 pack 阶段（SourceLink 在本地多 remote 下为已知差异，结果仅供观察不阻塞）。

.PARAMETER RunBenchmark
显式包含 benchmark 阶段（时长较长，且 CI 处于观察期 continue-on-error）。

.EXAMPLE
.\scripts\local-ci-check.ps1                                        # 默认阶段集合（见 param 块 / $allPhases）
.\scripts\local-ci-check.ps1 -Phase build,aot                       # 仅 build + aot
.\scripts\local-ci-check.ps1 -Phase build,test,pack -RunPack        # 含 pack 弱验证
.\scripts\local-ci-check.ps1 -GateBaseRef <直推起点 commit>          # 按直推区间判定等价门
#>
[CmdletBinding()]
param(
    [string[]]$Phase = @('scan','build','test','equivalence-gate','aot','samples','generated-baseline'),
    [string]$RepoRoot = "",
    [string]$GateBaseRef = "",
    [switch]$SkipAotEnvCheck,
    [switch]$RunPack,
    [switch]$RunBenchmark,
    [switch]$IncludeDocWarnings
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$RepoRoot = (Resolve-Path $RepoRoot).Path
$scripts = Join-Path $RepoRoot 'scripts'

$allPhases = @('scan','build','test','equivalence-gate','aot','samples','generated-baseline','pack','benchmark')
# 过滤非法阶段名
# -Phase 兼容三种传法：数组（-Command 形态）、逗号单串、分号单串——powershell -File 不做
# 参数解析，`-Phase a,b` 会以单串 "a,b" 到达，旧写法过滤后为空集即误报"未识别的阶段"。
$Phase = @($Phase | ForEach-Object { $_ -split '[,;]' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Phase = @($Phase | Where-Object { $allPhases -contains $_ })
if ($Phase.Count -eq 0) {
    Write-Error "未识别的阶段（合法值: $($allPhases -join '/')）。"
    exit 2
}
# 重型阶段仅在显式开关下生效
if ($Phase -contains 'pack' -and -not $RunPack) { $Phase = $Phase | Where-Object { $_ -ne 'pack' } }
if ($Phase -contains 'benchmark' -and -not $RunBenchmark) { $Phase = $Phase | Where-Object { $_ -ne 'benchmark' } }

# 过滤后为空 → 明确报错，绝不静默"空通过"（防假绿）
if ($Phase.Count -eq 0) {
    Write-Error "所选阶段均未启用：pack 需 -RunPack，benchmark 需 -RunBenchmark。未执行任何阶段。"
    exit 2
}

# ============================================================
# NuGetAudit 探测门控（一次探测、结果全腿共用）
# 一次探测、结果全腿共用、不落盘不跨轮缓存；仅含 build/samples 阶段时执行（唯一消费面）。
# 探测结果不入 PASS/FAIL 判定——不可达时仅 WARN + 各 --warnaserror 腿条件直传关闭属性。
# ============================================================
$nugetAuditArgs = @()
if (($Phase -contains 'build') -or ($Phase -contains 'samples')) {
    # powershell5 回落链前置 Tls12（:157 $altShell 链在无 pwsh 机器走 PS 5.1——旧默认 TLS 连不上
    # nuget.org 造成假阴性误关审计；本机 SystemDefault 实测亦可达，此处为旧默认机器防御性前置）
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    # 代理对齐（实现细化）：NuGet 认 HTTP(S)_PROXY 而 PS 5.1 IWR 默认不认（实测）——
    # 显式传 -Proxy 保持探测与 NuGet 网络视图一致，防代理环境下探测误判在线
    $probeProxy = $env:HTTPS_PROXY
    if (-not $probeProxy) { $probeProxy = $env:HTTP_PROXY }
    $auditReachable = $false
    $probeSw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if ($probeProxy) {
            Invoke-WebRequest -Uri 'https://api.nuget.org/v3/index.json' -Method Head -TimeoutSec 5 -UseBasicParsing -Proxy $probeProxy -ErrorAction Stop | Out-Null
        } else {
            Invoke-WebRequest -Uri 'https://api.nuget.org/v3/index.json' -Method Head -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop | Out-Null
        }
        $auditReachable = $true
    } catch { $auditReachable = $false }
    $probeSw.Stop()
    if ($auditReachable) {
        Write-Host ("[INFO] NuGet 审计源可达（探测 {0} ms）——本轮在线：NuGetAudit 实跑，真实漏洞告警 NU1901-1904 照常升红" -f [int]$probeSw.ElapsedMilliseconds)
    } else {
        $nugetAuditArgs = @('-p:NuGetAudit=false')
        Write-Host ("[WARN] NuGet 审计源不可达（探测 {0} ms）——本轮关闭 NuGetAudit（环境码不可判，非审计结论）" -f [int]$probeSw.ElapsedMilliseconds) -ForegroundColor DarkYellow
    }
}

Write-Host ""
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host " NCMapper 本地 CI 门禁（镜像 ci.yml）" -ForegroundColor Cyan
Write-Host " 仓库: $RepoRoot" -ForegroundColor Cyan
Write-Host " 阶段: $($Phase -join ' → ')" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan

$results = @{}   # phase -> $true/$false
$failed = $false

function Invoke-Check {
    param([string]$Name, [scriptblock]$Body)
    Write-Host ""
    Write-Host "---- [阶段] $Name ----" -ForegroundColor Yellow
    try {
        & $Body
        if ($LASTEXITCODE -ne 0) {
            throw "退出码非零: $LASTEXITCODE"
        }
        Write-Host "[PASS] $Name" -ForegroundColor Green
        $script:results[$Name] = $true
    } catch {
        Write-Host "[FAIL] $Name : $($_.Exception.Message)" -ForegroundColor Red
        $script:results[$Name] = $false
        $script:failed = $true
    }
}

# ============================================================
# 阶段 scan —— 机械护栏（U+FFFD 扫描 + 注释锚点校验）
# 判定语义：基线比对（非 Invoke-Check 的"退出码非零即失败"）——
#   与登记基线一致 = PASS，偏离 = FAIL。当前两个基线均已归零（见 scripts/README.md）。
# ============================================================
if ($Phase -contains 'scan') {
    Write-Host ""
    Write-Host "---- [阶段] scan ----" -ForegroundColor Yellow
    $scanOk = $true
    $scanSkipped = @()

    try {
        # ---------- 1) U+FFFD 扫描（基线比对判定） ----------
        # 基线 = 零命中（空集合）；历史首轮值（2 行 5 处）见 scripts/README.md 历史段
        $ufffdBaseline = @()
        $ufffdScript = Join-Path $scripts 'scan-ufffd.ps1'
        if (-not (Test-Path $ufffdScript)) {
            Write-Host "[FAIL] scan-ufffd 脚本缺失: $ufffdScript" -ForegroundColor Red
            $scanOk = $false
        } else {
            Write-Host ">> scan-ufffd.ps1（U+FFFD 扫描，基线比对判定）"
            # 子进程执行：该脚本以 exit 结束，同进程 & 调用会终止宿主脚本
            $altShell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }
            $ufffdOut = @(& $altShell -NoProfile -ExecutionPolicy Bypass -File $ufffdScript -RepoRoot $RepoRoot 2>&1)
            $ufffdExit = $LASTEXITCODE
            $ufffdHits = @($ufffdOut | Where-Object { $_ -match '^[^\[]+:\d+:\d+$' } | Sort-Object)
            $ufffdExpected = @($ufffdBaseline | Sort-Object)
            if (($ufffdHits -join '|') -eq ($ufffdExpected -join '|')) {
                Write-Host ("[PASS] scan-ufffd：命中与基线一致（{0} 处）" -f $ufffdExpected.Count) -ForegroundColor Green
            } else {
                Write-Host ("[FAIL] scan-ufffd：命中与基线不一致（脚本退出码 {0}）" -f $ufffdExit) -ForegroundColor Red
                Write-Host ("  基线 {0} 处: {1}" -f $ufffdExpected.Count, ($ufffdExpected -join ', '))
                Write-Host ("  实际 {0} 处: {1}" -f $ufffdHits.Count, ($ufffdHits -join ', '))
                $scanOk = $false
            }
        }

        # ---------- 2) 注释锚点校验（基线比对判定） ----------
        # 基线 = 0 条 L1 真失配；历史首轮值（8 条）见 scripts/README.md 历史段
        $anchorsBaseline = 0
        $anchorsScript = Join-Path $scripts 'check-doc-anchors.py'
        if (-not (Test-Path $anchorsScript)) {
            Write-Host "[FAIL] check-doc-anchors 脚本缺失: $anchorsScript" -ForegroundColor Red
            $scanOk = $false
        } else {
            # 解释器解析链：NCMAPPER_PYTHON -> python3 -> python；三者皆无 -> SKIP
            $py = $null
            if (-not [string]::IsNullOrWhiteSpace($env:NCMAPPER_PYTHON) -and (Test-Path $env:NCMAPPER_PYTHON)) {
                $py = $env:NCMAPPER_PYTHON
            } else {
                foreach ($cand in @('python3', 'python')) {
                    $cmdInfo = Get-Command $cand -ErrorAction SilentlyContinue
                    if ($cmdInfo) { $py = $cmdInfo.Source; break }
                }
            }

            if ($null -eq $py) {
                # ---- WSL 回退（Windows 无 Python 时经 WSL python3 实跑锚点门）----
                # 预检 = wsl.exe 在位 ∧ 发行版内 python3 可用（任一失败 → [SKIP]+WARN 升级措辞，
                # 不落 [L1] 缺失假红；受限沙箱拦截 wsl.exe 属此分支）
                $wslOk = $false
                if (Get-Command wsl -ErrorAction SilentlyContinue) {
                    try {
                        $wslProbe = @(& wsl -e bash -lc 'python3 --version' 2>&1)
                        if ($LASTEXITCODE -eq 0 -and ((@($wslProbe) -join ' ') -match 'Python 3')) { $wslOk = $true }
                    } catch { $wslOk = $false }
                }
                if ($wslOk) {
                    # 路径换算 D:\a\b → /mnt/d/a/b（check-doc-anchors.py repo_root 取 __file__ 上二级，
                    # /mnt 路径可行（实测）；/mnt 9p IO 为轻读，src 面 ≈245 文件可容忍）
                    $wslRoot = '/mnt/' + $RepoRoot.Substring(0, 1).ToLower() + ($RepoRoot.Substring(2) -replace '\\', '/')
                    Write-Host (">> check-doc-anchors.py（WSL python3 回退；L1 真失配基线 {0} 条）" -f $anchorsBaseline)
                    $anchorsOut = @(& wsl -e bash -lc ("cd `"{0}`" && python3 scripts/check-doc-anchors.py --report-only" -f $wslRoot) 2>&1)
                    # 判定沿既有 [L1] count: 基线语义（Windows 原生分支解析的 WSL 版复制——
                    # 保持原生分支逐字不动，判定语义零变化）
                    $l1Count = $null
                    foreach ($line in $anchorsOut) {
                        if ("$line" -match '^\[L1\] count:\s*(\d+)\s*$') { $l1Count = [int]$Matches[1] }
                    }
                    if ($null -eq $l1Count) {
                        Write-Host "[FAIL] check-doc-anchors（WSL）：输出中未找到 [L1] count 行（脚本异常）" -ForegroundColor Red
                        $anchorsOut | Select-Object -Last 8 | ForEach-Object { Write-Host ("  {0}" -f $_) }
                        $scanOk = $false
                    } elseif ($l1Count -eq $anchorsBaseline) {
                        Write-Host ("[PASS] check-doc-anchors（WSL）：L1 真失配与基线一致（{0} 条）" -f $l1Count) -ForegroundColor Green
                    } else {
                        Write-Host ("[FAIL] check-doc-anchors（WSL）：L1 真失配 {0} 条，基线 {1} 条" -f $l1Count, $anchorsBaseline) -ForegroundColor Red
                        $scanOk = $false
                    }
                } else {
                    Write-Host "[SKIP] check-doc-anchors：未找到 Python 解释器（NCMAPPER_PYTHON / python3 / python 均不可用），且 WSL 回退预检失败（wsl.exe 缺失/被拦截或发行版无 python3）" -ForegroundColor DarkYellow
                    Write-Host "[WARN] 锚点门本轮跳过——Windows Python 与 WSL python3 双通道不可达（环境降级读数，非门禁结论）" -ForegroundColor DarkYellow
                    $scanSkipped += 'check-doc-anchors'
                }
            } else {
                Write-Host (">> check-doc-anchors.py（解释器: {0}；L1 真失配基线 {1} 条）" -f $py, $anchorsBaseline)
                $anchorsOut = @(& $py $anchorsScript --report-only 2>&1)
                $l1Count = $null
                foreach ($line in $anchorsOut) {
                    if ($line -match '^\[L1\] count:\s*(\d+)\s*$') { $l1Count = [int]$Matches[1] }
                }
                if ($null -eq $l1Count) {
                    Write-Host "[FAIL] check-doc-anchors：输出中未找到 [L1] count 行（脚本异常）" -ForegroundColor Red
                    $anchorsOut | Select-Object -Last 8 | ForEach-Object { Write-Host ("  {0}" -f $_) }
                    $scanOk = $false
                } elseif ($l1Count -eq $anchorsBaseline) {
                    Write-Host ("[PASS] check-doc-anchors：L1 真失配与基线一致（{0} 条）" -f $l1Count) -ForegroundColor Green
                } else {
                    Write-Host ("[FAIL] check-doc-anchors：L1 真失配 {0} 条，基线 {1} 条" -f $l1Count, $anchorsBaseline) -ForegroundColor Red
                    $scanOk = $false
                }
            }
        }

        # ---------- 3) 注释符号引用门 ----------
        # 退出码硬门：0 绿 / 1（未解析引用）红 / 2（参数错）红；HISTORICAL 行 informational
        # 透出不判红；scanned 引用数 = 0 视为空转判红（vacuous-pass 护栏——主防线为脚本
        # 自身 :83-86 的 exit 2 双防线，护栏冗余防未来重构）。
        # 子进程执行：该脚本以 exit 结束，同进程 & 调用会终止宿主（scan-ufffd 同口径）。
        $refsScript = Join-Path $scripts 'check-comment-symbol-refs.ps1'
        if (-not (Test-Path $refsScript)) {
            Write-Host "[FAIL] check-comment-symbol-refs 脚本缺失: $refsScript" -ForegroundColor Red
            $scanOk = $false
        } else {
            Write-Host ">> check-comment-symbol-refs.ps1（默认面 test；退出码硬门——未解析注释符号引用即红）"
            $refsAltShell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }
            $refsOut = @(& $refsAltShell -NoProfile -ExecutionPolicy Bypass -File $refsScript -RepoRoot $RepoRoot 2>&1)
            $refsExit = $LASTEXITCODE
            $refsOut | ForEach-Object { Write-Host ("  {0}" -f $_) }
            # vacuous-pass 护栏：scanned = 0 → 空转判红（含 include 面失效形态）
            $scannedCount = $null
            foreach ($line in $refsOut) {
                if ("$line" -match 'scanned comment references = (\d+)') { $scannedCount = [int]$Matches[1] }
            }
            if ($refsExit -eq 0 -and $scannedCount -eq 0) {
                Write-Host "[FAIL] check-comment-symbol-refs：scanned 引用数 = 0（空转——vacuous-pass 护栏触发）" -ForegroundColor Red
                $scanOk = $false
            } elseif ($refsExit -eq 0 -and $null -eq $scannedCount) {
                # 解析缺失防御：脚本输出契约变更致 scanned 行
                # 解析不到时，护栏静默放行 = 门禁缺陷——显式判红，与"静默跳过须 WARN"纪律同向
                Write-Host "[FAIL] check-comment-symbol-refs：输出中未解析到 scanned 计数行（解析缺失——护栏失效防御，脚本输出契约可能已变更）" -ForegroundColor Red
                $scanOk = $false
            } elseif ($refsExit -eq 0) {
                Write-Host "[PASS] check-comment-symbol-refs：注释符号引用全解析" -ForegroundColor Green
            } else {
                Write-Host ("[FAIL] check-comment-symbol-refs：退出码 {0}（存在未解析注释符号引用——见上）" -f $refsExit) -ForegroundColor Red
                $scanOk = $false
            }
        }

        # ---------- 4) 注释过程噪声门 ----------
        # 脚本本体 = 退出码硬门（0 绿 / 1 硬失败 / 2 参数错）；挂载腿 = 红面基线比对判定：
        # 开工日全量命中已入库（scripts/tests/fixtures/comment-noise/redface-<开工日>.txt），
        # 当前命中 ⊆ 基线 即 PASS（存量只减不增合法），出现基线外新命中即 FAIL——
        # 与 scan-ufffd / check-doc-anchors 的基线比对语义同族（存量红面在清偿完成前不得让
        # scan 阶段恒红，否则出口门不可达；验收判据"门禁本体 exit 0"由脚本直跑承载）。
        # informational（状态词 / 日期）透出不判红；scanned = 0 空转判红（沿 :311-323 模式）。
        # 子进程执行：该脚本以 exit 结束，同进程 & 调用会终止宿主（scan-ufffd 同口径）。
        $noiseScript = Join-Path $scripts 'check-comment-process-noise.ps1'
        if (-not (Test-Path $noiseScript)) {
            Write-Host "[FAIL] check-comment-process-noise 脚本缺失: $noiseScript" -ForegroundColor Red
            $scanOk = $false
        } else {
            $noiseBaselineDir = Join-Path $RepoRoot 'scripts/tests/fixtures/comment-noise'
            $noiseBaselineFile = Get-ChildItem -Path $noiseBaselineDir -Filter 'redface-*.txt' -File -ErrorAction SilentlyContinue |
                Sort-Object Name | Select-Object -Last 1
            if (-not $noiseBaselineFile) {
                Write-Host "[FAIL] check-comment-process-noise：红面基线缺失（$noiseBaselineDir/redface-*.txt）——挂载腿无判定基准，不静默放行" -ForegroundColor Red
                $scanOk = $false
            } else {
                Write-Host ">> check-comment-process-noise.ps1（默认面 src;samples；红面基线比对判定——基线外新增命中即红）"
                $noiseAltShell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }
                # 命中清单走文件不走管道：子进程 stdout 经控制台代码页解码会把中文注释文本变成
                # mojibake，与入库基线（UTF-8）逐行比对会全量误判为"新增"——文件通道绕开该层。
                $noiseCurrent = Join-Path ([IO.Path]::GetTempPath()) 'ncmapper-comment-noise-current.txt'
                if (Test-Path $noiseCurrent) { Remove-Item $noiseCurrent -Force }
                $noiseOut = @(& $noiseAltShell -NoProfile -ExecutionPolicy Bypass -File $noiseScript -RepoRoot $RepoRoot -Include 'src;samples' -OutputFile $noiseCurrent 2>&1)
                $noiseExit = $LASTEXITCODE
                $noiseScanned = $null
                foreach ($line in $noiseOut) {
                    if ("$line" -match 'scanned comment lines = (\d+)') { $noiseScanned = [int]$Matches[1] }
                }
                $noiseHits = @()
                $noiseFileOk = $false
                if (Test-Path $noiseCurrent) {
                    $noiseFileOk = $true
                    $noiseHits = @([IO.File]::ReadAllLines($noiseCurrent, [System.Text.UTF8Encoding]::new($false)) | Where-Object { $_ -match '\S' })
                }
                $noiseInfo = @($noiseOut | Where-Object { "$_" -match '^  ~ ' })
                $noiseBase = @([IO.File]::ReadAllLines($noiseBaselineFile.FullName, [System.Text.UTF8Encoding]::new($false)) | Where-Object { $_ -match '\S' })
                # 比对键 = "类/形态 + 文本"，不含行号：修噪声时会移动行号，含行号键会把"已修复面的
                # 位移"误判为新增（Batch-2 实测 39 处假新增）。多重集计数比对：同一文本新增一条即红。
                $noiseCurMap = @{}; $noiseSample = @{}
                foreach ($h in $noiseHits) {
                    $k = ($h -replace '^[^ ]+:\d+\s*', '')
                    if ($noiseCurMap.ContainsKey($k)) { $noiseCurMap[$k] = $noiseCurMap[$k] + 1 } else { $noiseCurMap[$k] = 1; $noiseSample[$k] = $h }
                }
                $noiseBaseMap = @{}
                foreach ($b in $noiseBase) {
                    $k = ($b -replace '^[^ ]+:\d+\s*', '')
                    if ($noiseBaseMap.ContainsKey($k)) { $noiseBaseMap[$k] = $noiseBaseMap[$k] + 1 } else { $noiseBaseMap[$k] = 1 }
                }
                $noiseNew = @()
                foreach ($k in $noiseCurMap.Keys) {
                    $extra = $noiseCurMap[$k] - [int]$noiseBaseMap[$k]
                    for ($q = 0; $q -lt $extra; $q++) { $noiseNew += $noiseSample[$k] }
                }
                if ($noiseExit -eq 2) {
                    Write-Host "[FAIL] check-comment-process-noise：退出码 2（参数错——见上）" -ForegroundColor Red
                    $noiseOut | Select-Object -First 6 | ForEach-Object { Write-Host ("  {0}" -f $_) }
                    $scanOk = $false
                } elseif ($null -eq $noiseScanned) {
                    # 解析缺失防御（与 check-comment-symbol-refs 腿同向）：scanned 行解析不到
                    # = 护栏失效，显式判红，不静默放行
                    Write-Host "[FAIL] check-comment-process-noise：输出中未解析到 scanned 计数行（解析缺失——护栏失效防御，脚本输出契约可能已变更）" -ForegroundColor Red
                    $noiseOut | Select-Object -Last 6 | ForEach-Object { Write-Host ("  {0}" -f $_) }
                    $scanOk = $false
                } elseif ($noiseScanned -eq 0) {
                    Write-Host "[FAIL] check-comment-process-noise：scanned 计数 = 0（空转——vacuous-pass 护栏触发）" -ForegroundColor Red
                    $scanOk = $false
                } elseif (-not $noiseFileOk) {
                    Write-Host "[FAIL] check-comment-process-noise：-OutputFile 命中清单未产出（输出契约断裂，比对无基准——不静默放行）" -ForegroundColor Red
                    $scanOk = $false
                } elseif ($noiseNew.Count -gt 0) {
                    Write-Host ("[FAIL] check-comment-process-noise：基线外新增过程噪声 {0} 处（红面基线 {1} 处 / 当前 {2} 处）" -f $noiseNew.Count, $noiseBase.Count, $noiseHits.Count) -ForegroundColor Red
                    $noiseNew | Select-Object -First 20 | ForEach-Object { Write-Host ("  {0}" -f $_) }
                    $scanOk = $false
                } else {
                    Write-Host ("[PASS] check-comment-process-noise：零基线外新增噪声（红面基线 {0} 处 → 当前 {1} 处；informational {2} 处不判红；基线文件 {3}）" -f $noiseBase.Count, $noiseHits.Count, $noiseInfo.Count, $noiseBaselineFile.Name) -ForegroundColor Green
                }
            }
        }

        # ---------- 5) 块五桶对账门 ----------
        # check-block-buckets.py 本体＝【退出码硬门】（0 全绿 / 1 五桶断言破 / 2 扫描面缺失·零块·
        # 解析异常——假门禁防护内建）。现态 exit0 可直拦（无存量红，可直接作硬门）；
        # 只接入不改判据＝本体零改动。解释器复用 anchors 腿 $py 解析链；双通道不可达 → [SKIP]+WARN
        # （环境降级显式化，同 anchors 缺失语义，不落假红）。
        $bucketsScript = Join-Path $scripts 'check-block-buckets.py'
        if (-not (Test-Path $bucketsScript)) {
            Write-Host "[FAIL] check-block-buckets 脚本缺失: $bucketsScript" -ForegroundColor Red
            $scanOk = $false
        } elseif ($null -eq $py) {
            Write-Host "[SKIP] check-block-buckets：无可用 Python 解释器（环境降级读数，非门禁结论）" -ForegroundColor DarkYellow
            $scanSkipped += 'check-block-buckets'
        } else {
            Write-Host (">> check-block-buckets.py（解释器: {0}；五桶互斥+和==实测总块数动态断言；退出码硬门）" -f $py)
            # 子进程 utf-8 口径（本机 GBK 控制台环境备案）：buckets 输出含中文＋U+2212，
            # Windows GBK 控制台下 print 即 UnicodeEncodeError→exit1＝环境假红非判据破——
            # 挂载腿仅此子进程临时注入 PYTHONUTF8=1，finally 恢复原值不外溢后续阶段（aot vcvars
            # 快照-恢复同模式）；脚本本体零改动。
            $pyUtf8Prev = $env:PYTHONUTF8
            $env:PYTHONUTF8 = '1'
            try {
                $bucketsOut = @(& $py $bucketsScript 2>&1)
                $bucketsExit = $LASTEXITCODE
            } finally {
                if ($null -ne $pyUtf8Prev) { $env:PYTHONUTF8 = $pyUtf8Prev } else { Remove-Item Env:PYTHONUTF8 -ErrorAction SilentlyContinue }
            }
            $bucketsOut | Select-Object -Last 6 | ForEach-Object { Write-Host ("  {0}" -f $_) }
            if ($bucketsExit -eq 0) {
                Write-Host "[PASS] check-block-buckets：五桶断言全绿（见上方读数）" -ForegroundColor Green
            } else {
                Write-Host ("[FAIL] check-block-buckets：退出码 $bucketsExit（五桶断言破或扫描面缺失——见上）" -f $bucketsExit) -ForegroundColor Red
                $scanOk = $false
            }
        }

        # ---------- 6) 演示覆盖对账门 ----------
        # check-demo-coverage.sh 本体＝告警版设计（三查清单透出不判红、exit 恒 0；对账源缺失 exit 2
        # 拦截——本体 :12-14 语义，挂载腿沿【退出码硬门】不放宽不收紧）。bash 解析链（本机备案＝
        # 本机裸 bash 为 WSL stub 不可用）：NCMAPPER_BASH → Git bin/usr bash.exe → 非 Windows 系统路径的 Get-Command 命中。
        $coverScript = Join-Path $scripts 'check-demo-coverage.sh'
        if (-not (Test-Path $coverScript)) {
            Write-Host "[FAIL] check-demo-coverage 脚本缺失: $coverScript" -ForegroundColor Red
            $scanOk = $false
        } else {
            $bashExe = $null
            if (-not [string]::IsNullOrWhiteSpace($env:NCMAPPER_BASH) -and (Test-Path $env:NCMAPPER_BASH)) {
                $bashExe = $env:NCMAPPER_BASH
            } else {
                foreach ($bcand in @('C:\Program Files\Git\bin\bash.exe', 'C:\Program Files\Git\usr\bin\bash.exe')) {
                    if (Test-Path $bcand) { $bashExe = $bcand; break }
                }
                if (-not $bashExe) {
                    foreach ($cinfo in @(Get-Command bash -ErrorAction SilentlyContinue)) {
                        if ($cinfo.Source -notmatch '\\Windows\\') { $bashExe = $cinfo.Source; break }
                    }
                }
            }
            if (-not $bashExe) {
                Write-Host "[SKIP] check-demo-coverage：无可用 bash（裸 bash 为 WSL stub 且未定位到 Git bash——环境降级读数，非门禁结论）" -ForegroundColor DarkYellow
                $scanSkipped += 'check-demo-coverage'
            } else {
                Write-Host (">> check-demo-coverage.sh（bash: {0}；三查双向对账，告警版本体；退出码硬门仅前置面破判红）" -f $bashExe)
                $coverOut = @(& $bashExe $coverScript 2>&1)
                $coverExit = $LASTEXITCODE
                # 告警面全量透出（不截尾——截尾会把观察列 warn 压成假静默读数面；全量 ≈65 行可容忍）
                $coverOut | ForEach-Object { Write-Host ("  {0}" -f $_) }
                if ($coverExit -eq 0) {
                    Write-Host "[PASS] check-demo-coverage：对账链实跑完成（告警面透出——本体设计不判红）" -ForegroundColor Green
                } else {
                    Write-Host ("[FAIL] check-demo-coverage：退出码 $coverExit（对账源缺失——见上）" -f $coverExit) -ForegroundColor Red
                    $scanOk = $false
                }
            }
        }

        # ---------- 7) 文档告警构建（可选，-IncludeDocWarnings） ----------
        if ($IncludeDocWarnings) {
            Write-Host ">> 文档告警构建（SourceGen, -p:GenerateDocumentationFile=true --no-incremental）"
            # 基线：门禁计数口径
            # （构建日志 csproj 后缀行原始计数，不去重；含同次构建的双份输出，见 scripts/README.md §3）
            # CS1573 62→80：+18 = 新增形参未补 param 标签（已归因并正式上调）
            # CS1573 80→106 / CS1587 2→0（复测）：
            # 复测在 oss 4a03f90（未触 SourceGen 的态）执行，与登记值不符属**登记基线过期**、
            # 非本次改动引入（改前改后同次构建读数一致：CS1570 8 / CS1571 0 / CS1572 22 /
            # CS1573 106 / CS1587 0）。按"零新增"判定语义，基线取复测值；CS1587 归零后
            # 任一再现即 OVER 判红（与"CS1587 出现即回滚"口径一致）。
            $docBaseline = @{ 'CS1570' = 8; 'CS1571' = 0; 'CS1572' = 22; 'CS1573' = 106; 'CS1587' = 0 }
            $docOut = @(dotnet build (Join-Path $RepoRoot 'src/NCMapper.SourceGen/NCMapper.SourceGen.csproj') `
                -c Release --no-incremental -p:GenerateDocumentationFile=true -p:EnableSourceLink=false --nologo 2>&1)
            $docCounts = @{}
            foreach ($line in $docOut) {
                # 连带修复（该分支从未被执行过：第二个 -match 无捕获组会清空
                # $Matches[1]，导致 $code = null -> ContainsKey(null) 抛异常）——交换两条件顺序，
                # 使含捕获组的 match 置于最后；判定语义不变（两条件仍须同时成立）
                if ($line -match '\[.*\.csproj\]\s*$' -and $line -match 'warning (CS\d+):') {
                    $code = $Matches[1]
                    if ($docCounts.ContainsKey($code)) { $docCounts[$code] = $docCounts[$code] + 1 }
                    else { $docCounts[$code] = 1 }
                }
            }
            $docOver = @()
            foreach ($code in $docBaseline.Keys) {
                $actual = if ($docCounts.ContainsKey($code)) { $docCounts[$code] } else { 0 }
                $mark = if ($actual -le $docBaseline[$code]) { 'ok' } else { 'OVER' }
                Write-Host ("  {0}: {1}（基线 {2}）{3}" -f $code, $actual, $docBaseline[$code], $mark)
                if ($actual -gt $docBaseline[$code]) { $docOver += $code }
            }
            if ($docOver.Count -eq 0) {
                Write-Host "[PASS] 文档告警：相对基线零新增" -ForegroundColor Green
            } else {
                Write-Host ("[FAIL] 文档告警超出基线: {0}" -f ($docOver -join ', ')) -ForegroundColor Red
                $scanOk = $false
            }
        }
    } catch {
        Write-Host "[FAIL] scan : $($_.Exception.Message)" -ForegroundColor Red
        $scanOk = $false
    }

    if ($scanSkipped.Count -gt 0) {
        Write-Host ("[SKIP] scan 部分跳过: {0}" -f ($scanSkipped -join ', ')) -ForegroundColor DarkYellow
    }
    $script:results['scan'] = $scanOk
    if (-not $scanOk) {
        Write-Host "[FAIL] scan" -ForegroundColor Red
        $script:failed = $true
    } elseif ($scanSkipped.Count -gt 0) {
        Write-Host ("[PASS] scan（部分跳过: {0}）" -f ($scanSkipped -join ', ')) -ForegroundColor Green
    } else {
        Write-Host "[PASS] scan" -ForegroundColor Green
    }
}

# ============================================================
# CrashProbe 残留解锁——test 阶段前置
# 挂死/残留 CrashProbe 进程锁 bin 产物 → MSB3027/MSB3021（实际锁点
# bin/Release/net8.0/）。处置序：
#   1) 清历史 rename 残留（*.locked.*，防跨轮累积）
#   2) 枚举进程：可终止者 Stop-Process；终止后仍驻留者（僵尸/不可终止态）WARN 登记不静默
#   3) 驻留进程存续时对 bin 产物做锁探测，实锁文件 rename 为 *.locked.<UTC时间戳>
#      （FILE_SHARE_DELETE 允许 .NET 已加载程序集改名放行拷贝；exe 映像为加载器持有、
#      rename 不动——不可终止进程持 exe 锁须重启机器，本腿 WARN 后构建期报真实错误）
# ============================================================
function Unlock-CrashProbe {
    $probeName = 'NCMapper.CrashProbe'
    $probeBin = Join-Path $RepoRoot 'test/NCMapper.CrashProbe/bin/Release'

    # 1) 历史 rename 残留清理（进程已死者文件可删；仍被锁则跳过，下轮再试）
    $cleaned = 0
    if (Test-Path $probeBin) {
        $residuals = @(Get-ChildItem -Path $probeBin -Recurse -File -Filter '*.locked.*' -ErrorAction SilentlyContinue)
        foreach ($f in $residuals) {
            Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue
            $cleaned++
        }
        if ($cleaned -gt 0) {
            Write-Host ("[INFO] CrashProbe 解锁残留清理：删除上轮 rename 残留 {0} 个文件" -f $cleaned)
        }
    }

    # 2) 残留进程枚举与终止
    $procs = @(Get-Process -Name $probeName -ErrorAction SilentlyContinue)
    if ($procs.Count -gt 0) {
        Write-Host ("[INFO] CrashProbe 残留进程 {0} 个——尝试终止（解锁前置）" -f $procs.Count)
        foreach ($p in $procs) {
            try { Stop-Process -Id $p.Id -Force -ErrorAction Stop }
            catch { Write-Host ("[WARN] CrashProbe 进程 {0} 不可终止（挂死）：{1}" -f $p.Id, $_.Exception.Message) -ForegroundColor DarkYellow }
        }
        Start-Sleep -Milliseconds 800   # 句柄释放缓冲
        $survivors = @(Get-Process -Name $probeName -ErrorAction SilentlyContinue)
        if ($survivors.Count -gt 0) {
            Write-Host ("[WARN] CrashProbe 进程终止后仍驻留 {0} 个（僵尸/不可终止态）——不静默登记；若其不持锁则构建照常" -f $survivors.Count) -ForegroundColor DarkYellow
            # 3) 驻留存续：锁探测 + rename 兜底（仅动实锁文件）
            $renamed = 0
            if (Test-Path $probeBin) {
                $ts = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
                $candidates = @(Get-ChildItem -Path $probeBin -Recurse -File -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -notmatch '\.locked\.' })
                foreach ($f in $candidates) {
                    $locked = $false
                    try { $fs = [IO.File]::Open($f.FullName, 'Open', 'ReadWrite', 'None'); $fs.Close() }
                    catch { $locked = $true }
                    if ($locked) {
                        try {
                            Rename-Item -LiteralPath $f.FullName -NewName ("{0}.locked.{1}" -f $f.Name, $ts) -ErrorAction Stop
                            $renamed++
                        } catch { Write-Host ("[WARN] 被锁产物 rename 失败（不可终止进程持有，须重启机器）：{0}" -f $f.Name) -ForegroundColor DarkYellow }
                    }
                }
                if ($renamed -gt 0) {
                    Write-Host ("[INFO] CrashProbe 被锁产物 rename 解锁 {0} 个 → *.locked.{1}（下轮运行自动清理）" -f $renamed, $ts)
                }
            }
        }
    }
}

# ============================================================
# 阶段 build —— 镜像 ci.yml build job（ubuntu），本地需 EnableSourceLink=false
# ============================================================
if ($Phase -contains 'build') {
    Invoke-Check 'build' {
        Write-Host ">> restore NCMapper.Lib.slnf"
        dotnet restore (Join-Path $RepoRoot 'NCMapper.Lib.slnf') @nugetAuditArgs
        if ($LASTEXITCODE -ne 0) { throw "restore 失败" }

        Write-Host ">> check-requires-messages.ps1（Requires* 消息语言门禁）"
        & (Join-Path $scripts 'check-requires-messages.ps1')
        if ($LASTEXITCODE -ne 0) { throw "Requires* 消息语言门禁失败" }

        Write-Host ">> dotnet build NCMapper.Lib.slnf --warnaserror（本地加 EnableSourceLink=false + --no-incremental 防增量假绿）"
        dotnet build (Join-Path $RepoRoot 'NCMapper.Lib.slnf') --configuration Release --no-restore --no-incremental --warnaserror -p:EnableSourceLink=false @nugetAuditArgs
        if ($LASTEXITCODE -ne 0) { throw "Lib.slnf 构建失败" }

        Write-Host ">> gen-global-config.ps1 -Apply + git diff（生成区与清单一致性）"
        & (Join-Path $scripts 'gen-global-config.ps1') -Apply
        if ($LASTEXITCODE -ne 0) { throw "gen-global-config 失败" }
        $consist = @(
            'src/NCMapper/MapDefaultsAttribute.cs',
            'src/NCMapper/GeneratedMapConfig.cs',
            'src/NCMapper/GlobalConfigView.cs',
            'src/NCMapper/GlobalMapConfig.cs',
            'src/NCMapper.SourceGen/GlobalConfigReader.cs',
            'src/NCMapper.SourceGen/CodeGenerator.cs',
            'src/NCMapper/NCMapper.targets'
        )
        # git diff 已做 EOL 归一化（.gitattributes * text=auto eol=lf），工作区 CRLF 非仓库违规
        # （S1 口径，见 .gitattributes 注释）——仅真实内容漂移才令 --exit-code 非零。勿手动改行尾，
        # 避免与 git autocrlf 重新物化互相打架。
        #
        # stderr 局部丢弃（避免噪声掩盖真实失败）：
        # git 的 CRLF/LF 提示写 stderr，调用侧若以 `*>&1` 合并即成 error record，使宿主 pwsh 退出码 = 1
        # 而本脚本聚合输出仍为「失败 0 阶段」——判定口径被迫依赖读聚合输出。
        # 与 verify-equivalence-gate.ps1:52/:55 同口径：git stderr 局部降级，判定只看 $LASTEXITCODE；
        # 真实内容漂移的 diff 正文走 stdout，不受影响。
        git diff --exit-code -- $consist 2>$null
        if ($LASTEXITCODE -ne 0) { throw "生成区与清单不一致（git diff 非零），请检查 gen-global-config 产出" }
        Write-Host "[PASS] 生成区与清单一致"

        Write-Host ">> check-locales-drift.ps1（locales key-diff 门禁，比对面 = en）"
        & (Join-Path $scripts 'check-locales-drift.ps1') -Language en
        if ($LASTEXITCODE -ne 0) { throw "locales drift 门禁失败" }

        Write-Host ">> Build AotReflectionGuard（AOT 反射守护）"
        dotnet build (Join-Path $RepoRoot 'tools/AotReflectionGuard/NCMapper.AotReflectionGuard.csproj') --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "AotReflectionGuard 构建失败" }

        Write-Host ">> Run AotReflectionGuard"
        dotnet run --project (Join-Path $RepoRoot 'tools/AotReflectionGuard/NCMapper.AotReflectionGuard.csproj') --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw "AOT 反射守护失败" }
    }
}

# ============================================================
# 阶段 test —— 镜像 ci.yml test job（ubuntu），本地直接跑
# ============================================================
if ($Phase -contains 'test') {
    Invoke-Check 'test' {
        # ---- CrashProbe 残留解锁前置：杀残留进程 + 清 rename 残留 + 实锁文件改名兜底 ----
        Unlock-CrashProbe

        Write-Host ">> Build CrashProbe（崩溃类红测依赖的子进程 runner）"
        $crashBuildOut = @(dotnet build (Join-Path $RepoRoot 'test/NCMapper.CrashProbe/NCMapper.CrashProbe.csproj') --configuration Release 2>&1)
        if ($LASTEXITCODE -ne 0) {
            # 反应式解锁：MSB3021/MSB3027（产物被进程句柄锁定）——从错误消息解析目标文件，
            # rename 放行后单次重试；仍失败则透传真实错误（不可终止进程持 exe 锁须重启机器）
            $msbLock = @($crashBuildOut | Where-Object { "$_" -match 'MSB302[17]' })
            if ($msbLock.Count -gt 0) {
                Write-Host "[WARN] CrashProbe 构建撞产物锁（MSB3021/MSB3027）——解析被锁目标并 rename 解锁后重试" -ForegroundColor DarkYellow
                $ts = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
                $unlockedFiles = @()
                foreach ($line in $msbLock) {
                    $quotes = [regex]::Matches("$line", '[“"]([^“”"]+)[”"]')
                    if ($quotes.Count -ge 2) {
                        # MSB302x 消息形如：无法将“SRC”复制到“DST”——第 2 个引号路径为目标（bin 产物）
                        $dst = $quotes[1].Groups[1].Value
                        if (-not [IO.Path]::IsPathRooted($dst)) { $dst = Join-Path (Join-Path $RepoRoot 'test/NCMapper.CrashProbe') $dst }
                        if ((Test-Path $dst) -and ($dst.StartsWith((Join-Path $RepoRoot 'test/NCMapper.CrashProbe/bin'), [StringComparison]::OrdinalIgnoreCase)) -and ($dst -notin $unlockedFiles)) {
                            try {
                                Rename-Item -LiteralPath $dst -NewName ("{0}.locked.{1}" -f (Split-Path $dst -Leaf), $ts) -ErrorAction Stop
                                $unlockedFiles += $dst
                                Write-Host ("[INFO] rename 解锁：{0} → *.locked.{1}" -f $dst, $ts)
                            } catch {
                                Write-Host ("[WARN] 被锁产物 rename 失败（不可终止进程持有，须重启机器）：{0}" -f $dst) -ForegroundColor DarkYellow
                            }
                        }
                    }
                }
                if ($unlockedFiles.Count -gt 0) {
                    dotnet build (Join-Path $RepoRoot 'test/NCMapper.CrashProbe/NCMapper.CrashProbe.csproj') --configuration Release
                    if ($LASTEXITCODE -ne 0) {
                        $crashBuildOut | Select-Object -Last 12 | ForEach-Object { Write-Host ("  {0}" -f $_) }
                        throw "CrashProbe 构建失败（rename 解锁重试后仍红）"
                    }
                    Write-Host "[INFO] rename 解锁重试通过"
                } else {
                    $crashBuildOut | Select-Object -Last 12 | ForEach-Object { Write-Host ("  {0}" -f $_) }
                    throw "CrashProbe 构建失败（产物被不可终止进程锁定，rename 无解锁面——须重启机器，详见上方 MSB 错误）"
                }
            } else {
                $crashBuildOut | Select-Object -Last 12 | ForEach-Object { Write-Host ("  {0}" -f $_) }
                throw "CrashProbe 构建失败"
            }
        }

        Write-Host ">> dotnet test NCMapper.slnx --configuration Release"
        dotnet test (Join-Path $RepoRoot 'NCMapper.slnx') --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "dotnet test 失败" }
    }
}

# ============================================================
# 阶段 equivalence-gate —— 本地门激活
# 调既有 scripts/verify-equivalence-gate.ps1（不改 gate 脚本本体）：
#   触发面（物化面文件）命中而义务面（Equivalence/ 目录）未跟 → 退出码非零；
#   BaseRef 不可解析 → gate 自身 fail closed（不静默通过）。
# BaseRef 取直推起点 commit（直推语义）：传 -GateBaseRef <rev>；
# 留空 = 沿 gate 默认 origin/main（CI 侧 PR-only 语义，本地多 remote 下区间会偏大）。
# 子进程执行：gate 脚本以 exit 结束，同进程 & 调用会终止宿主脚本（scan-ufffd 同口径）。
# CI 侧不变：ci.yml 的 equivalence-gate job 仍 PR-only（本阶段只补本地调用点）。
# ============================================================
if ($Phase -contains 'equivalence-gate') {
    Invoke-Check 'equivalence-gate' {
        $gate = Join-Path $scripts 'verify-equivalence-gate.ps1'
        if (-not (Test-Path $gate)) { throw "verify-equivalence-gate.ps1 缺失: $gate" }
        $altShell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }
        $gateArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$gate)
        if (-not [string]::IsNullOrWhiteSpace($GateBaseRef)) {
            $gateArgs += @('-BaseRef', $GateBaseRef)
            Write-Host ">> verify-equivalence-gate.ps1 -BaseRef $GateBaseRef（直推起点，直推语义）"
        } else {
            Write-Host ">> verify-equivalence-gate.ps1（BaseRef = gate 默认 origin/main）"
        }
        & $altShell @gateArgs
        if ($LASTEXITCODE -ne 0) { throw "等价门失败（退出码 $LASTEXITCODE）" }
    }
}

# ============================================================
# 阶段 aot —— 镜像 ci.yml aot job（windows-latest），本机正是 Windows
# ============================================================
if ($Phase -contains 'aot') {
    Invoke-Check 'aot' {
        # ---- MSVC/Windows SDK 环境自适应 ----
        # 缺失时自动定位 vcvars64.bat 并临时导入五项变量（Platform 不导入——vcvars 置
        # Platform=x64 会令 .slnx/.slnf metaproj 报 MSB4126，此即根因）。
        # 隔离选型 = 主会话注入 + 快照-恢复：aot 腿执行期间主会话临时持有
        # 所缺变量（子进程 dotnet publish 经继承获得）、阶段结束 finally 恢复原值——
        # 后续 samples/generated-baseline 阶段零残留（INCLUDE/Platform 前后对照可验）。
        # -SkipAotEnvCheck 语义保持：跳过校验 + 跳过自动导入，直接执行让真实错误暴露。
        $vcvarsEnvSnapshot = $null
        $vcvarsInjected = $false
        if (-not $SkipAotEnvCheck) {
            $need = @('VCINSTALLDIR','WindowsSdkDir','WindowsSDKVersion','INCLUDE','LIB')
            $missing = $need | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) }
            if ($missing.Count -gt 0) {
                # 定位 vcvars64.bat：vswhere（VS 安装器在位时）→ 固定候选路径兜底
                $vcvars = $null
                $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
                if (Test-Path $vswhere) {
                    $found = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find '**\VC\Auxiliary\Build\vcvars64.bat' 2>$null
                    if ($found) { $vcvars = @($found)[0] }
                }
                if (-not $vcvars) {
                    foreach ($cand in @(
                        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat'),
                        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat'),
                        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat'),
                        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat')
                    )) {
                        if (Test-Path $cand) { $vcvars = $cand; break }
                    }
                }
                if ($vcvars) {
                    Write-Host ("[INFO] NativeAOT 环境变量缺失（{0}）——自动导入 vcvars64：{1}" -f ($missing -join ', '), $vcvars)
                    # 快照当前值（含缺失=空）→ cmd 子会话跑 vcvars 后回读 env → 仅注入所缺五项
                    $vcvarsEnvSnapshot = @{}
                    foreach ($k in $need) { $vcvarsEnvSnapshot[$k] = [Environment]::GetEnvironmentVariable($k) }
                    $dump = & cmd /d /c "`"$vcvars`" >NUL 2>&1 && set" 2>&1
                    $dumpMap = @{}
                    foreach ($line in $dump) {
                        if ("$line" -match '^([A-Za-z0-9_()]+)=(.*)$') { $dumpMap[$Matches[1]] = $Matches[2] }
                    }
                    $imported = @()
                    foreach ($k in $need) {
                        if ($dumpMap.ContainsKey($k)) { Set-Item -Path ("Env:{0}" -f $k) -Value $dumpMap[$k]; $imported += $k }
                    }
                    if ($imported.Count -gt 0) {
                        $vcvarsInjected = $true
                        Write-Host ("[INFO] vcvars 注入 {0} 项（Platform 显式不导入——防 MSB4126）；aot 阶段结束即恢复原值" -f $imported.Count)
                    }
                } else {
                    Write-Host "[WARN] NativeAOT 环境变量缺失: $($missing -join ', ')" -ForegroundColor DarkYellow
                    Write-Host "       且未定位到 vcvars64.bat（vswhere/固定候选路径均未命中）——无法自动导入。"
                    Write-Host "       手动设置 MSVC + Windows SDK 环境变量后重跑，或加 -SkipAotEnvCheck 跳过校验暴露真实错误。"
                    throw "NativeAOT 环境变量缺失且无法自动导入（用 -SkipAotEnvCheck 跳过校验）"
                }
            }
        }
        try {

        Write-Host ">> check-aot-il-warnings.ps1 -RequiredMembers '*DeepCopyRuntimeOnly*,*Validate*'"
        & (Join-Path $scripts 'check-aot-il-warnings.ps1') -RequiredMembers '*DeepCopyRuntimeOnly*','*Validate*'
        if ($LASTEXITCODE -ne 0) { throw "AOT IL 门禁失败" }

        Write-Host ">> 运行 AOT 二进制（冒烟）"
        $aotExe = Join-Path $RepoRoot 'test/NCMapper.AotTests/bin/Release/net10.0/win-x64/publish/NCMapper.AotTests.exe'
        if (-not (Test-Path $aotExe)) { throw "AOT 可执行不存在（publish 未产出）: $aotExe" }
        & $aotExe
        if ($LASTEXITCODE -ne 0) { throw "AOT 冒烟运行失败（退出码 $LASTEXITCODE）" }
        } finally {
            # 快照-恢复：注入的 vcvars 变量恢复原值（原缺失项移除）——环境不外溢后续阶段（隔离纪律）
            if ($vcvarsInjected -and $vcvarsEnvSnapshot) {
                foreach ($k in $vcvarsEnvSnapshot.Keys) {
                    if ($null -ne $vcvarsEnvSnapshot[$k]) { Set-Item -Path ("Env:{0}" -f $k) -Value $vcvarsEnvSnapshot[$k] }
                    else { Remove-Item -Path ("Env:{0}" -f $k) -ErrorAction SilentlyContinue }
                }
                Write-Host "[INFO] vcvars 注入已恢复（aot 阶段环境零残留——后续阶段不受影响）"
            }
        }
    }
}

# ============================================================
# 阶段 samples —— 镜像 ci.yml samples job（ubuntu）
# ============================================================
if ($Phase -contains 'samples') {
    Invoke-Check 'samples' {
        Write-Host ">> dotnet build NCMapper.Samples --warnaserror"
        dotnet build (Join-Path $RepoRoot 'samples/NCMapper.Samples/NCMapper.Samples.csproj') --configuration Release --warnaserror -p:EnableSourceLink=false @nugetAuditArgs
        if ($LASTEXITCODE -ne 0) { throw "Samples 构建失败" }

        Write-Host ">> dotnet run NCMapper.Samples -- --all"
        dotnet run --project (Join-Path $RepoRoot 'samples/NCMapper.Samples/NCMapper.Samples.csproj') --configuration Release -- --all
        if ($LASTEXITCODE -ne 0) { throw "Samples 运行失败" }

        Write-Host ">> dotnet build NCMapper.AotSample --warnaserror"
        dotnet build (Join-Path $RepoRoot 'samples/NCMapper.AotSample/NCMapper.AotSample.csproj') --configuration Release --warnaserror -p:EnableSourceLink=false @nugetAuditArgs
        if ($LASTEXITCODE -ne 0) { throw "AotSample 构建失败" }

        Write-Host ">> dotnet run NCMapper.AotSample"
        dotnet run --project (Join-Path $RepoRoot 'samples/NCMapper.AotSample/NCMapper.AotSample.csproj') --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "AotSample 运行失败" }
    }
}

# ============================================================
# 阶段 generated-baseline —— 镜像 ci.yml generated-baseline job（ubuntu）
# ============================================================
if ($Phase -contains 'generated-baseline') {
    Invoke-Check 'generated-baseline' {
        Write-Host ">> verify-generated-baseline.ps1（生成产物基线一致性）"
        & (Join-Path $scripts 'verify-generated-baseline.ps1')
        if ($LASTEXITCODE -ne 0) { throw "生成产物基线一致性失败" }
    }
}

# ============================================================
# 阶段 pack —— 镜像 ci.yml pack job（ubuntu）；本地 SourceLink 为已知差异，仅弱验证
# ============================================================
if ($Phase -contains 'pack') {
    Invoke-Check 'pack' {
        Write-Host "[NOTE] 本地多 remote 下 SourceLink 嵌入断言为已知差异，本阶段仅供观察打包完整性，不阻塞。"
        Write-Host ">> dotnet pack NCMapper.Lib.slnf"
        $packOut = dotnet pack (Join-Path $RepoRoot 'NCMapper.Lib.slnf') --configuration Release --output (Join-Path $RepoRoot 'nupkgs') -p:EnableSourceLink=false 2>&1
        $packOut | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { throw "pack 失败" }
        if ($packOut -match 'warning NU5128') { throw "pack 存在 NU5128 警告（locales 依赖组）" }
        Write-Host "[PASS] pack 零 NU5128 警告（SourceLink 完整性请以 CI pack job 为准）"

        # ---- packsmoke 消费实证（镜像 ci.yml windows-pack "Smoke test from package"）----
        # 首跑实测教训：仅 pack 成功不等于包可消费——NCMapper 声明 M.E.* 三依赖，仓外单本地源
        # restore 即 NU1101（详见 ci.yml windows-pack 注释）。此步在仓外 temp 目录以双源
        # NuGet.config（nuget.org + 本地 nupkgs）做真实消费链：new console → add package → run。
        # 同步义务：双源口径与 ci.yml windows-pack 步骤保持一致（改动须两处同改）。
        Write-Host ">> packsmoke：仓外双源消费实证（new console → add package → run）"
        $smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'ncmapper-packsmoke'
        if (Test-Path $smokeRoot) { Remove-Item $smokeRoot -Recurse -Force }
        New-Item -ItemType Directory -Path $smokeRoot | Out-Null
        $nupkgDir = (Resolve-Path (Join-Path $RepoRoot 'nupkgs')).Path
        @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
<packageSources>
<clear />
<add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
<add key="local-ncmapper" value="__NUPKGDIR__" />
</packageSources>
</configuration>
'@ -replace '__NUPKGDIR__', $nupkgDir | Set-Content -Path (Join-Path $smokeRoot 'NuGet.config') -Encoding utf8

        Push-Location $smokeRoot
        try {
            dotnet new console -n PackSmoke -f net10.0 | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "packsmoke: dotnet new console 失败" }
            dotnet add PackSmoke package NCMapper --source "$nupkgDir" --prerelease | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "packsmoke: add package NCMapper 失败（NU1101 族——依赖解析或包元数据问题）" }

            @'
using NCMapper;
using NCMapper.Attributes;

var src = new SourceEntity { Name = "smoke-ok" };
var dst = Mapper.Map<SourceEntity, TargetDto>(src)!;
System.Console.WriteLine($"MAPPED:{dst.Name}");
if (dst.Name != "smoke-ok") { System.Environment.Exit(1); }

public class SourceEntity
{
    public string Name { get; set; } = "";
}

[MapFrom(typeof(SourceEntity))]
public class TargetDto
{
    public string Name { get; set; } = "";
}
'@ | Set-Content -Path (Join-Path $smokeRoot 'PackSmoke/Program.cs') -Encoding utf8

            $smokeRun = dotnet run --project PackSmoke 2>&1
            $smokeRun | ForEach-Object { Write-Host $_ }
            if ($LASTEXITCODE -ne 0) { throw "packsmoke: 冒烟运行失败（退出码 $LASTEXITCODE）" }
            if (-not ($smokeRun -match 'MAPPED:smoke-ok')) { throw "packsmoke: 输出未含映射标记" }
            Write-Host "[PASS] packsmoke 消费链通过（restore 依赖解析 / TFM / 源生成 / 运行）"
        }
        finally {
            Pop-Location
            Remove-Item $smokeRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# ============================================================
# 阶段 benchmark —— 镜像 ci.yml benchmark job（ubuntu）；CI 观察期 continue-on-error
# ============================================================
if ($Phase -contains 'benchmark') {
    Invoke-Check 'benchmark' {
        Write-Host ">> ci-benchmark.sh check（CI 自建基线语义，本地仅供观察）"
        if (Test-Path (Join-Path $RepoRoot 'benchmarks/ci-baseline/ci-baseline.json')) {
            bash (Join-Path $scripts 'ci-benchmark.sh') check
        } else {
            Write-Host "[WARN] CI 基线不存在（首次）。观察期：跳过门禁，需 bash scripts/ci-benchmark.sh init 建立基线。"
            Write-Host "       （本地不阻塞——CI 本阶段同为 continue-on-error 观察期）"
            # WARN 跳过分支本意即 PASS（本地不阻塞）——重置 $LASTEXITCODE 防 Invoke-Check 误判：
            # benchmark-only 调用时无前置原生命令，$LASTEXITCODE 为 $null → "$null -ne 0" 为真 → 假红；
            # 或前序阶段红态遗留非零值同样污染本判定（已附带修复）
            $global:LASTEXITCODE = 0
        }
    }
}

# ============================================================
# 汇总
# ============================================================
Write-Host ""
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host " 本地 CI 门禁汇总" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan
$okCount = ($results.Values | Where-Object { $_ }).Count
$failCount = ($results.Values | Where-Object { -not $_ }).Count
foreach ($p in $Phase) {
    if ($results.ContainsKey($p)) {
        $mark = if ($results[$p]) { 'PASS' } else { 'FAIL' }
        $color = if ($results[$p]) { 'Green' } else { 'Red' }
        Write-Host ("  [{0}] {1}" -f $mark, $p) -ForegroundColor $color
    }
}
Write-Host ""
Write-Host ("通过 {0} 阶段，失败 {1} 阶段。" -f $okCount, $failCount)
if ($failed) {
    Write-Host "存在失败阶段——请先修复再推送（本机验证是推送前第一道闸）。" -ForegroundColor Red
    exit 1
}
Write-Host "全部通过——本地 CI 门禁合格，可推送。" -ForegroundColor Green
exit 0
