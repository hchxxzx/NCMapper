#!/usr/bin/env bash
# Fluent Member 三段式「结构性反例」回归门。
# 用法: bash scripts/check-fluent-negative-cases.sh [--mode coexist|final]
#   final（默认，断代后终态）= 18/18 全拦；N2/N3 实测码翻转 CS1501/CS0305 → CS1061（旧成员删除后
#                   "有方法无此参"变"成员无定义"——翻转落点，实测回填）；
#                   N9/N10 = 扩展用例（Convert/ConvertBack 旧形态双参）；
#                   N11~N18 = 长尾方法族扩展（字段段终态后续接 / 对级与入口根接收者缺失 /
#                   泛型元数与委托元数三类误用形态，逐文件探针实测码回填）
#   coexist        = 并存期历史态（断代前快照：7/8 拦截 + N5 零错误例外）——断代后不可复现，参数保留仅供登记
# 断言口径：逐例错误码 + 拦截计数；build FAILED 为承载形态预期（退出码不检查，只解析逐例诊断）。
set -u
MODE="final"
if [ "${1:-}" = "--mode" ]; then MODE="${2:?usage: --mode coexist|final}"; fi
case "$MODE" in coexist|final) ;; *) echo "unknown mode: $MODE" >&2; exit 2 ;; esac
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/test/NCMapper.FluentNegativeCases/NCMapper.FluentNegativeCases.csproj"
export PATH="$HOME/.dotnet:$PATH"
LOG="$(mktemp /tmp/fluent-neg-XXXXXX.log)"
dotnet build "$PROJ" --configuration Release --nologo > "$LOG" 2>&1

# 双态期望表（并存期实测回填口径）
declare -A EXPECT=(
  [N1_ConfigThenFrom.cs]=CS1061
  [N2_FromThenIgnore.cs]=CS1501
  [N3_IgnoreThenMask.cs]=CS0305
  [N4_SecondFrom.cs]=CS1061
  [N6_BareMemberThenMember.cs]=CS1061
  [N7_BareMemberThenTree.cs]=CS1061
  [N8_ConfigThenFieldFromSource.cs]=CS1061
)
# 终态翻转（断代清扫后实测回填）：N5 恢复拦截；
# N2/N3 旧成员删除后由 CS1501/CS0305 变 CS1061（成员无定义）
# 扩展用例（N9/N10——Convert/ConvertBack 旧形态 (dest, converter) 双参）：
#   实测码回填——N9 = CS1503（第二实参方法组不可转 bool，重载接管）；
#   N10 = CS1061（ConvertBack 无 (dest, converter) 重载，lambda 形参按 string 绑定）。
#   承载意义 = "旧形态无编译来源"前提锁定（断代清扫的语义地基）。
if [ "$MODE" = "final" ]; then
  EXPECT[N2_FromThenIgnore.cs]=CS1061
  EXPECT[N3_IgnoreThenMask.cs]=CS1061
  EXPECT[N5_SecondNoAutoInclude.cs]=CS1061
  EXPECT[N9_OldFormConvertWithDestLambda.cs]=CS1503
  EXPECT[N10_OldFormConvertBackWithDestLambda.cs]=CS1061
  # 长尾方法族扩展（N11~N18，逐文件探针实测回填）——字段段终态后续接（N11/N13）、
  # 对级与入口根接收者缺失（N12/N14/N18）、泛型元数与委托元数（N15/N16/N17）
  EXPECT[N11_IgnoreThenFormat.cs]=CS1061
  EXPECT[N12_FromJsonOnPairConfig.cs]=CS1061
  EXPECT[N13_IgnoreThenMapTree.cs]=CS1061
  EXPECT[N14_NoAutoMatchOnConfig.cs]=CS1061
  EXPECT[N15_MapCtorParamMissingArg.cs]=CS1501
  EXPECT[N16_MapSubclassSingleTypeArg.cs]=CS0305
  EXPECT[N17_AfterMappingWrongArity.cs]=CS1593
  EXPECT[N18_BeforeMappingOnConfig.cs]=CS1061
fi

FAIL=0; BLOCKED=0
for f in "$ROOT"/test/NCMapper.FluentNegativeCases/N*.cs; do
  base="$(basename "$f")"
  exp="${EXPECT[$base]:-}"
  if [ -z "$exp" ]; then
    # 无期望表项 = 并存期 N5 例外：期望零错误
    act="$(grep "$base" "$LOG" | grep -oE "error [A-Za-z]+[0-9]+" | sort -u | tr '\n' ' ')"
    if [ -z "$act" ]; then
      echo "PASS  $base  (并存期零错误——N5 例外留档，对级旧名在位)"
    else
      echo "FAIL  $base  期望零错误（并存期），实际: $act"; FAIL=$((FAIL+1))
    fi
    continue
  fi
  act="$(grep "$base" "$LOG" | grep -oE "error [A-Za-z]+[0-9]+" | sort -u | tr '\n' ' ')"
  case " $act " in
    *" $exp "*) BLOCKED=$((BLOCKED+1)); echo "PASS  $base  ($exp)" ;;
    *) echo "FAIL  $base  期望 $exp，实际: ${act:-<无错误>}"; FAIL=$((FAIL+1)) ;;
  esac
done
rm -f "$LOG"
echo "----"
EXPECTED_BLOCKED=7; [ "$MODE" = "final" ] && EXPECTED_BLOCKED=18
echo "mode=$MODE blocked=$BLOCKED/$EXPECTED_BLOCKED failed_files=$FAIL"
if [ "$FAIL" -ne 0 ] || [ "$BLOCKED" -ne "$EXPECTED_BLOCKED" ]; then exit 1; fi
echo "FLUENT-NEGATIVE-CASES PASS"
