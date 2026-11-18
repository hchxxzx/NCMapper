#!/usr/bin/env bash
# =====================================================================
# check-detection-baseline.sh — 三层检出能力基线
#
# 目的：把矩阵附录 B 的一次性手工变异实验（E1/E2/E3）脚本化，
#   让「检出能力」可持续自证——这条链有没有能力发现错误；不追求覆盖率（B.5 #5）。
#
# 三变异（对应附录 B.1 实测先例）：
#   E1 断言层  ：BasicScenarios.DotPath 源对象缺数据（src → new UserEntity { Id = src.Id }）
#                → 场景 201 必红（exit 1 + 输出含 201-dot-path 断言消息）
#   E2 引用层  ：region 名 BasicMapping_SimpleNameMatch 加 X → check-doc-sample-refs 必红
#                （NOREGION，含受影响页）
#   E3 手抄块层：向首个裸手抄块插 3 个假 API → 七支门禁行为与干净态完全一致
#                ＝「手抄块层无检出能力（预期）」——E3 的绿是结论，不是 bug（F7）。
#     · 动态选块：不硬编码 core-api.md——按「docs/zh-CN 下含
#       ^```csharp 围栏的 md」取字典序首个（与 check-doc-codeblock-bind.sh 裸块口径同源），
#       页名+行号写入日志；若全库无裸块 → 输出「无裸块可选＝零检出区已消除」exit 0
#       （清块完成后的达成形态）。
#
# 复原保障（F6）：每变异后 git checkout -- 复原并验工作区干净；结束前 git status --short
#   必须为空，否则 exit 2。变异态产物不得提交。
# 退出码：0=三层检出形态符合预期  1=任一预期未命中  2=前置缺失/工作区未复原
# 判定口径：脚本类门禁以输出判定词/输出 diff 为准（本机 sandbox 拦截 rm -rf 可污染退出码，
#   矩阵附录 B.5 #4）；CI（Linux）不受影响。
# 用法（仓库根执行）：./scripts/check-detection-baseline.sh
# =====================================================================

set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

basic="samples/NCMapper.Samples/02-Declarative/BasicScenarios.cs"
e1_old='=> Mapper.Map<UserEntity, UserDotPathDto>(src)!;'
e1_new='=> Mapper.Map<UserEntity, UserDotPathDto>(new UserEntity { Id = src.Id })!;'
fail=0
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# dirty 判定排除脚本自身（首次运行时本脚本尚未入库属正常状态，F6 判据针对的是变异文件的复原）
dirty() {
    [ -n "$(git status --porcelain | grep -v 'scripts/check-detection-baseline.sh')" ]
}

if dirty; then
    echo "[FAIL] 前置缺失：工作区不干净（git status 非空），拒绝施加变异（防误复原他人改动）"
    exit 2
fi

echo "=== 检出能力基线：三层变异实验（干净工作区已确认） ==="

# ---------- E1 断言层 ----------
echo "--- E1 断言层：DotPath 源对象缺数据 → 场景 201 必红 ---"
if ! grep -qF "$e1_old" "$basic"; then
    echo "[FAIL] E1 变异锚未命中（$basic）——实现形态可能已变，请人工核对后更新锚"
    exit 1
fi
dotnet build samples/NCMapper.Samples -p:EnableSourceLink=false --nologo -v q > /dev/null 2>&1 || {
    echo "[FAIL] E1 干净态预构建失败"; exit 1; }

content="$(cat "$basic")"
printf '%s\n' "${content/"$e1_old"/$e1_new}" > "$basic"
dotnet build samples/NCMapper.Samples -p:EnableSourceLink=false --nologo -v q > /dev/null 2>&1 || {
    echo "[FAIL] E1 变异态构建失败（变异本身应可编译）"; git checkout -- "$basic"; exit 1; }

e1_out="$(dotnet run --project samples/NCMapper.Samples --no-build -p:EnableSourceLink=false -- --scenario 201 2>&1)"
e1_rc=$?
if [ "$e1_rc" -eq 1 ] && printf '%s' "$e1_out" | grep -q "201-dot-path: flat properties did not pick up nested source path values"; then
    echo "[PASS] E1：demo 断言层检出变异（exit 1 + 201-dot-path 断言消息）——行为错误能被发现"
else
    echo "[FAIL] E1：预期场景 201 exit 1 且输出含 201-dot-path 断言，实测 exit=$e1_rc"
    printf '%s\n' "$e1_out" | tail -5
    fail=1
fi
git checkout -- "$basic"
dotnet build samples/NCMapper.Samples -p:EnableSourceLink=false --nologo -v q > /dev/null 2>&1 || true
if dirty; then echo "[FAIL] E1 复原后工作区不干净"; exit 2; fi

# ---------- E2 引用层 ----------
echo "--- E2 引用层：region 改名 → DFM 门禁必红 ---"
sed -i 's/#region BasicMapping_SimpleNameMatch$/#region BasicMapping_SimpleNameMatchX/' "$basic"
e2_out="$(bash scripts/check-doc-sample-refs.sh 2>/dev/null)"
e2_cnt="$(printf '%s\n' "$e2_out" | grep -c 'NOREGION')"
if [ "$e2_cnt" -ge 1 ]; then
    echo "[PASS] E2：DFM 引用门禁检出 region 失联（NOREGION ×$e2_cnt，受影响页见上）——引用断裂能被发现"
    printf '%s\n' "$e2_out" | grep 'NOREGION' | head -3
else
    echo "[FAIL] E2：预期 check-doc-sample-refs 报 NOREGion，实测无"
    printf '%s\n' "$e2_out" | tail -3
    fail=1
fi
git checkout -- "$basic"
if dirty; then echo "[FAIL] E2 复原后工作区不干净"; exit 2; fi

# ---------- E3 手抄块层（动态选块） ----------
echo "--- E3 手抄块层：动态选块 → 插 3 个假 API → 预期门禁行为不变（零检出区仍在/已消除） ---"
# 裸块页清单（口径与 check-doc-codeblock-bind.sh 同源：docs/zh-CN 下含 ^```csharp 的 md）
bare_pages="$(grep -rl '^```csharp' docs/zh-CN --include='*.md' | sort)"
if [ -z "$bare_pages" ]; then
    echo "[PASS] E3：无裸块可选＝零检出区已消除（清块达成形态）"
    echo "=== 检出能力基线汇总 ==="
    [ "$fail" -eq 0 ] && { echo "[PASS] 三层检出形态符合预期（E1 断言层/E2 引用层必红 + E3 零检出区已消除）"; exit 0; }
    echo "[FAIL] 存在未命中预期项（见上）"; exit 1
fi

e3_page="$(printf '%s\n' "$bare_pages" | head -1)"
e3_line="$(grep -n '^```csharp' "$e3_page" | head -1 | cut -d: -f1)"
echo "[info] E3 变异点（动态选取）：$e3_page:$e3_line（首个裸块围栏行后；F9 动态选块，不硬编码）"

# 干净态基线（七支门禁，对齐附录 B.2 观察面）
: > "$work/base.txt"
{ bash scripts/check-doc-sample-refs.sh 2>/dev/null; } > "$work/m1.txt"
{ bash scripts/check-diagnostics-sync.sh 2>/dev/null; } > "$work/m2.txt"
{ bash scripts/check-api-demo-coverage.sh 2>/dev/null; } > "$work/m3.txt"
{ bash scripts/check-demo-coverage.sh 2>/dev/null; } > "$work/m4.txt"
{ bash scripts/check-demo-assertions.sh -Strict 2>/dev/null; } > "$work/m5.txt"
{ bash scripts/check-doc-codeblock-bind.sh 2>/dev/null; } > "$work/m6.txt"
{ python scripts/check-doc-freshness.py --strict 2>/dev/null; } > "$work/m7.txt"

# 变异：首个裸块围栏行后插 3 个假 API（附录 B.1 形态）
{ head -n "$e3_line" "$e3_page"; \
  printf '%s\n' \
    'var _e3fake1 = Mapper.TotallyFakeApi(entity);   // E3 变异探针（脚本自动复原）' \
    'var _e3fake2 = entity.NotARealProperty;' \
    'var _e3fake3 = ctx.AnotherFakeGetter();'; \
  tail -n "+$((e3_line + 1))" "$e3_page"; } > "$e3_page.mut" && mv "$e3_page.mut" "$e3_page"

# 变异态复跑七支，逐支与干净态 diff
e3_diff=0
for i in 1 2 3 4 5 6 7; do
    case $i in
        1) bash scripts/check-doc-sample-refs.sh 2>/dev/null > "$work/r$i.txt";;
        2) bash scripts/check-diagnostics-sync.sh 2>/dev/null > "$work/r$i.txt";;
        3) bash scripts/check-api-demo-coverage.sh 2>/dev/null > "$work/r$i.txt";;
        4) bash scripts/check-demo-coverage.sh 2>/dev/null > "$work/r$i.txt";;
        5) bash scripts/check-demo-assertions.sh -Strict 2>/dev/null > "$work/r$i.txt";;
        6) bash scripts/check-doc-codeblock-bind.sh 2>/dev/null > "$work/r$i.txt";;
        7) python scripts/check-doc-freshness.py --strict 2>/dev/null > "$work/r$i.txt";;
    esac
    if ! diff -q "$work/m$i.txt" "$work/r$i.txt" > /dev/null 2>&1; then
        echo "[FAIL] E3：门禁 #$i 对手抄块内容产生了行为变化（干净态 vs 变异态输出不一致）——这可能是检出能力的新发现，请人工核对："
        diff "$work/m$i.txt" "$work/r$i.txt" | head -6
        e3_diff=1
    fi
done
git checkout -- "$e3_page"
if dirty; then echo "[FAIL] E3 复原后工作区不干净"; exit 2; fi

if [ "$e3_diff" -eq 0 ]; then
    echo "[PASS] E3：手抄块层无检出能力（预期——变异点 $e3_page:$e3_line 插入 3 个假 API，七支门禁行为与干净态完全一致）"
    echo "       E3 的绿是结论不是 bug：没有任何脚本的判据覆盖手抄块内容——这正是需要幽灵 API 门禁的理由"
else
    echo "[FAIL] E3：存在门禁行为变化（见上），预期全一致（零检出区仍在）"
    fail=1
fi

# ---------- 汇总 ----------
echo "=== 检出能力基线汇总 ==="
if [ "$fail" -eq 0 ]; then
    echo "[PASS] 三层检出形态符合预期：E1 断言层能发现行为错误 / E2 引用层能发现引用断裂 / E3 手抄块层零检出（零检出区仍在，变异点 $e3_page:$e3_line）"
    echo "       工作区干净（F6 通过）。对应矩阵附录 B.3 三层检出边界。"
    exit 0
fi
echo "[FAIL] 存在未命中预期项（见上方 [FAIL] 清单）"
exit 1
