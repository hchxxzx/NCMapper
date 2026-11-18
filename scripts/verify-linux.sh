#!/usr/bin/env bash
# =============================================================================
# verify-linux.sh — Linux 环境快速门禁（scripts/local-ci-check.ps1 的 Linux 子集）
#
# 定位：Windows 沙箱 dotnet 不可用、local-ci-check.ps1（PowerShell/MSVC aot）无法
# 在 Linux 完整执行时的替代门禁。**阶段口径逐条对齐 .github/workflows/ci.yml**：
#
#   [1] build-strict   = ci.yml:45  Lib.slnf Release + --warnaserror（零警告门）
#   [2] build-samples  = ci.yml:242 samples Release + --warnaserror
#   [3] test           = ci.yml:125/174 全量测试（Release 口径）
#   [4] baseline       = ci.yml generated-baseline job（pwsh 脚本复用）
#   [5] diag-sync      = 诊断码三方对账（check-diagnostics-sync.sh）
#   [6] demo-coverage  = 演示覆盖五查（check-api-demo-coverage.sh）
#   [7] fluent-negative = Fluent 反例回归（check-fluent-negative-cases.sh，终态 18/18——2026-10-10 挂接）
#   [8] fluent-legacy   = Fluent 旧形态常驻回归扫描（check-fluent-legacy-scan.py，需 python3——
#                         2026-10-10 挂接；判据/豁免清单见脚本头注释）
#
# 排除：aot 阶段（win-x64/MSVC 专属，Linux 无法执行——本地以 NCMapper.AotTests 的
# linux-x64 NativeAOT publish 替代验证）。
#
# 用法：
#   bash scripts/verify-linux.sh              # 全阶段
#   SKIP_TESTS=1 bash scripts/verify-linux.sh # 跳过 [3]（改跑最小测试集时自配）
#
# 纪律：替代验证套件必须逐条对照 CI job 命令补口径；构建验证禁止只看 error 行
# （警告统计必须过目）。
# =============================================================================
set -u

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"   # -P：解析 symlink，防经软链目录调用时影子树/自证门漏触发（回验 P3-2）
cd "$REPO"

export PATH="$HOME/.dotnet:$PATH"
[ -d "$HOME/.dotnet" ] && export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"   # #172：apphost 不经 PATH 找运行时；目录不存在（apt 装机等他处）时不注入，留给 install_location 解析（回验 P3-7）
command -v dotnet >/dev/null || { echo "[fatal] dotnet 不在 PATH（需 \$HOME/.dotnet）"; exit 2; }
HAVE_PWSH=1
command -v pwsh >/dev/null || HAVE_PWSH=0

FAILED=()

step() { echo ""; echo "========== [$1] $2 =========="; }

# ---------- [0.0] ext4 影子树（跨 OS obj 污染根治） ----------
# 病灶：Windows 侧（VS/手工 restore）会持续重写共享工作树的 obj/——其 *.nuget.g.targets
# 用反斜杠路径，Linux MSBuild 的 Exists() 判假 → MTP targets 被静默跳过导入 →
# dotnet test 报 MSB4057 "InvokeTestingPlatform 不存在"（多 TFM 测试项目）；其
# project.assets.json 记录 VS fallback folder（C:\...）→ MSB4018。增量 restore 对
# Windows 写的产物判"最新"跳过重写，共享树上洗不掉（2026-10-20"已排除 obj 复用"
# 系假阴性）。对策：检测到 /mnt/*（drvfs/9p 共享树）时把源码（排除 obj/bin/.git/IDE 垃圾）
# 同步到 ext4 影子树，全部门禁在影子树执行；CI 原生 Linux 路径不受影响。
# 检测面＝/mnt/* 前缀（drvfs 默认挂载形态）；非 /mnt 前缀自定义挂载与 symlink 指向 /mnt
# 的调用（L30 已 pwd -P 解析）由 [0] 反斜杠自证门兜底（回验 P3-2/P3-3）。.git 排除＝
# [4]-[8] 实测零 git 依赖（回验 G 项），且防 Windows 侧 git 进行中同步到撕裂快照
# （回验 P3-8）；未来若门禁需 git，须改同步并解除排除。
# NCMAPPER_NO_SHADOW=1 强制原地跑（仅调试）；NCMAPPER_WSL_SHADOW 自定义影子树路径。
SHADOW="${NCMAPPER_WSL_SHADOW:-$HOME/.ncmapper_gate_shadow}"
if [ "${REPO#/mnt/}" != "$REPO" ] && [ "${NCMAPPER_NO_SHADOW:-0}" != "1" ]; then
  step 0.0 "共享树检测 → rsync 源码至 ext4 影子树 $SHADOW（排除 obj/bin/IDE 垃圾）"
  mkdir -p "$SHADOW" || { echo "[fatal] 影子树目录创建失败: $SHADOW"; exit 2; }
  command -v rsync >/dev/null || { echo "[fatal] 影子树需要 rsync（sudo apt install rsync），或 NCMAPPER_NO_SHADOW=1 接受共享树风险"; exit 2; }
  # 排除 obj/bin（跨 OS 污染源）、.git（门禁零 git 依赖＋防撕裂快照）与 IDE/构建垃圾
  # （.vs 索引锁文件会 Permission denied）。rsync rc=23（部分文件未传，Windows 侧占用）
  # 容忍为 WARN——门禁所需源码文件不受影响；其余非零 rc 视为致命。--delete 不触碰
  # 目标端被 exclude 的 obj/（增量构建友好）。
  rsync -a --delete --exclude=obj/ --exclude=bin/ --exclude=.git/ --exclude=.vs/ --exclude=TestResults/ --exclude=artifacts/ "$REPO/" "$SHADOW/"
  RSYNC_RC=$?
  if [ "$RSYNC_RC" == "23" ]; then
    echo "[shadow][WARN] rsync 部分文件未同步（Windows 侧占用，rc=23）——继续，门禁以源码文件为准"
  elif [ "$RSYNC_RC" != "0" ]; then
    echo "[fatal] rsync 同步失败（rc=${RSYNC_RC}）"; exit 2
  fi
  REPO="$SHADOW"; cd "$REPO"
  echo "[shadow] 执行根已切换影子树（源码=当前工作树；obj/ 由本侧 ext4 独立生成，Windows 写入者不可达）"
fi

# ---------- [0] restore（CI 由 workflow 前置步骤承担，本地脚本自带） ----------
step 0 "restore"
dotnet restore NCMapper.Lib.slnf >/dev/null || { echo "[fatal] restore 失败"; exit 2; }
dotnet restore NCMapper.slnx      >/dev/null || { echo "[fatal] restore 失败"; exit 2; }
# 反斜杠 g.targets 自证门（fail-closed）：Windows 写的 *.nuget.g.targets 在 Linux 上
# Exists() 判假 → MTP targets 静默不导入 → 阶段 [3] 必报 MSB4057 假红。影子树模式
# 天然干净；原地模式（NCMAPPER_NO_SHADOW=1 / 非 /mnt 共享树）若检出污染立即中止，
# 指引用影子树或删 obj 重 restore。全量扫描不抽样——跨 OS 交替写时各目录分隔符可以
# 不一致（回验 P2-1：head -1 抽样对混合污染树漏检）。find 目录列表＝当前 csproj 全部
# 分布面（回验 P3-6），新增顶层项目目录须同步扩展本列表。
GTAKE_BAD=$(find src test samples benchmarks tools -name '*.nuget.g.targets' 2>/dev/null -print0 \
  | xargs -0 -r grep -l 'Import Project="[^"]*\\' 2>/dev/null)
if [ -n "$GTAKE_BAD" ]; then
  echo "[fatal] 检出 Windows 反斜杠 g.targets——共享树 obj 污染，dotnet test 必报 MSB4057 假红："
  echo "$GTAKE_BAD" | sed 's/^/        /'
  echo "        处置：默认走 ext4 影子树（去掉 NCMAPPER_NO_SHADOW），或 find . -type d -name obj -prune -exec rm -rf {} + 后重跑。"
  exit 2
fi
echo "restore OK"

# ---------- [1] Lib 严格门禁（零警告） ----------
step 1 "build NCMapper.Lib.slnf -c Release --no-restore --no-incremental --warnaserror"
dotnet build NCMapper.Lib.slnf --configuration Release --no-restore --no-incremental --warnaserror --nologo
[ $? -ne 0 ] && FAILED+=("build-strict")

# ---------- [2] Samples 门禁（零警告） ----------
step 2 "build samples -c Release --warnaserror"
dotnet build samples/NCMapper.Samples/NCMapper.Samples.csproj --configuration Release --warnaserror --nologo
[ $? -ne 0 ] && FAILED+=("build-samples")

# ---------- [3] 全量测试（Release 口径） ----------
if [ "${SKIP_TESTS:-0}" != "1" ]; then
  step 3 "test NCMapper.slnx -c Release（10 模块 × 3 TFM，约 10-20 分钟）"
  # B01 崩溃红测前置：CrashProbe 刻意独立于 slnx（净控制台最小形态），dotnet test 不会
  # 构建它，RedTestRunner 靠其 bin 目录探测可执行输出——CI 有 Build CrashProbe 前置步骤
  # （ci.yml:104，2026-10-21 对齐补入本脚本；缺失时 2 个 Red_B01_SelfRefContainer 用例
  # 抛 InvalidOperationException 假红，非代码回归）。
  dotnet build test/NCMapper.CrashProbe/NCMapper.CrashProbe.csproj --configuration Release --nologo >/dev/null \
    || echo "[WARN] CrashProbe 构建失败——B01 崩溃红测 2 用例将假红（见 ci.yml:98-106）"
  # 三重判据（2026-10-21 堵假绿）：旧实现只数 "Failed!"——MSB4057/MSB4018 发生在
  # 编排期，0 个测试执行、输出无 "Failed!" 行，TEST_BAD=0 会绿灯放行。
  # 现判据：①退出码非零 ②Failed! 计数>0 ③error 行（MSB4057/MSB4018/NETSDK/run failed）>0，任一即红。
  # 编译/锁等硬失败（含 MSB3021/3027/CS）由 ①退出码兜底（回验 P3-1 注：原注释误列 CS 于 ③，现对齐实现）。
  # 日志 mktemp 防多用户主机可预测名 symlink 抢占（回验 P3-5）；红日志保留供诊断，WSL /tmp 重启即清无累积。
  TEST_LOG=$(mktemp /tmp/ncmapper-gate-test.XXXXXX.log)
  dotnet test NCMapper.slnx --configuration Release --no-restore --nologo >"$TEST_LOG" 2>&1
  TEST_RC=$?
  grep -E "Passed!|Failed!|error" "$TEST_LOG" | head -40
  TEST_BAD=$(grep -c "Failed!" "$TEST_LOG")
  TEST_ERR=$(grep -cE "error MSB4057|error MSB4018|error NETSDK|run failed" "$TEST_LOG")
  if   [ "$TEST_RC"  != "0" ]; then FAILED+=("test(rc=${TEST_RC})")
  elif [ "$TEST_BAD" != "0" ]; then FAILED+=("test(Failed!=${TEST_BAD})")
  elif [ "$TEST_ERR" != "0" ]; then FAILED+=("test(error行=${TEST_ERR})")
  fi
  if [ "$TEST_RC" == "0" ] && [ "$TEST_BAD" == "0" ] && [ "$TEST_ERR" == "0" ]; then
    rm -f "$TEST_LOG"
  else
    echo "[test] 完整日志保留：$TEST_LOG"
  fi
else
  echo "SKIP_TESTS=1 —— 跳过"
fi

# ---------- [4] 字节门（需 pwsh；-SkipBuild 复用 [2] 产物） ----------
step 4 "generated-baseline（字节一致性）"
if [ "$HAVE_PWSH" == "1" ]; then
  pwsh -NoProfile -File scripts/verify-generated-baseline.ps1 -SkipBuild 2>&1 | grep -E "DIFF|IDENTICAL|PASS|FAIL" | head -10
  pwsh -NoProfile -File scripts/verify-generated-baseline.ps1 -SkipBuild >/dev/null 2>&1 || FAILED+=("baseline")
else
  echo "无 pwsh —— 跳过（字节门需 pwsh 执行 verify-generated-baseline.ps1）"
fi

# ---------- [5] 诊断码三方对账 ----------
step 5 "check-diagnostics-sync（描述符/resx/Designer 一致性）"
bash scripts/check-diagnostics-sync.sh
[ $? -ne 0 ] && FAILED+=("diag-sync")

# ---------- [6] 演示覆盖五查 ----------
step 6 "check-api-demo-coverage（快照类型反查登记）"
bash scripts/check-api-demo-coverage.sh
[ $? -ne 0 ] && FAILED+=("demo-coverage")

# ---------- [7] Fluent 反例回归（终态 18/18） ----------
step 7 "check-fluent-negative-cases（终态 18/18 拦截——编译失败为预期承载形态）"
bash scripts/check-fluent-negative-cases.sh
[ $? -ne 0 ] && FAILED+=("fluent-negative-cases")

# ---------- [8] Fluent 旧形态常驻回归扫描 ----------
step 8 "check-fluent-legacy-scan（旧签名/旧名/new 压制复活检测 + 旧形态调用扫描）"
python3 scripts/check-fluent-legacy-scan.py
[ $? -ne 0 ] && FAILED+=("fluent-legacy-scan")

# ---------- 摘要 ----------
echo ""
echo "=========================================="
if [ ${#FAILED[@]} -eq 0 ]; then
  echo "VERIFY-LINUX PASS（全阶段绿）"
  exit 0
else
  echo "VERIFY-LINUX FAIL：${FAILED[*]}"
  exit 1
fi
