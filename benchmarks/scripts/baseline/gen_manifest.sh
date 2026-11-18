#!/usr/bin/env bash
# ================================================================
# NCMapper 性能回归基线 Benchmark 清单生成器
# ----------------------------------------------------------------
# 用途: 固化"基线包含哪些 benchmark 类"的权威定义, 确保可复现。
# 三维（2026-07-07 定型）:
#   框架 = net8.0 / net9.0 / net10.0
#   组件 = Ext / NCMapper / Runtime(自己组件)
#          + Mapperly / Manual / Mapster / AutoMapper(竞品对照)
#   场景 = 11 业务族(Flat/NameDiff/Nested/Record/Nullable/AutoFlatten/
#          EnumString/Action/Collection/BidirED/BidirDE)
#          + DeepCopy(6 变体: Manual/Generated/Runtime/MemoryPack/MessagePack/Json)
# 排除规则（逐轮确认）:
#   *Big 类          -- 18:08 去除(字段多/少对比场景)
#   Quick_* 系列      -- 18:14 去除(快速配置微基准)
#   Nested_Layer*     -- 18:25 去除(嵌套深度基准)
# 命名约定（已于 2026-07-07 改名完成）:
#   标准组件统一用后缀式 <场景>_<组件> (如 Flat_NCMapper, Flat_Runtime)。
#   Runtime 路径已由原 Runtime_Flat 前缀统一重命名为 Flat_Runtime 后缀,
#   与其余组件命名一致 (Program.cs + 本生成器同步更新)。
# 变体缺失声明: 某些场景在特定组件下无 benchmark 类,
#   是功能层面该组件不支持此映射语义 (非清单遗漏, 非命名问题)。
#   例: AutoFlatten 无 AutoMapper/Mapster 变体（BidirDE_Ext 已于 2026-08-18 补齐，原"无 Ext 变体"限制解除）。
#   建基线时按模式通配匹配存在的类, 缺失变体自动跳过; 矩阵空位标
#   "N/A(该组件无此场景实现)", 不当错误。
# 输出: 默认写 artifacts/benchmarks/baseline_manifest.txt (gitignore), 同时打印到 stdout。
# ================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
PC="$REPO_ROOT/benchmarks/NCMapper.Benchmarks/Program.cs"
OUT="${1:-$REPO_ROOT/artifacts/benchmarks/baseline_manifest.txt}"

fams="Flat NameDiff Nested Record Nullable AutoFlatten EnumString Action Collection BidirED BidirDE"
suffix_comps="Ext NCMapper Mapperly Manual Mapster AutoMapper"
deepcopy_comps="Generated Json Manual MemoryPack MessagePack Runtime"

# 排除判定: 类名含 Big / Quick / Nested_Layer 则跳过
is_excluded() {
  case "$1" in
    *Big*|*Quick*|*Nested_Layer*) return 0 ;;
    *) return 1 ;;
  esac
}

# 组件分类: DeepCopy_* 与业务族分开判定 Manual 归属
classify() {
  local cls="$1"
  if [[ "$cls" == DeepCopy_* ]]; then
    case "${cls#DeepCopy_}" in
      Generated|Manual|Runtime) echo "SELF(自己组件)";;
      *) echo "EXTSER(外部序列化)";;
    esac
  else
    case "$cls" in
      *_Ext|*_NCMapper|*_Runtime) echo "SELF(自己组件)";;
      *) echo "COMP(竞品对照)";;
    esac
  fi
}

{
  echo "# NCMapper 性能回归基线 Benchmark 清单 ($(date +%Y-%m-%d_%H:%M) 生成)"
  echo "# 框架: net8.0 / net9.0 / net10.0"
  echo "# 场景: 11 业务族 + DeepCopy; 组件: 7 (Ext/NCMapper/Runtime=自己组件, Mapperly/Manual/Mapster/AutoMapper=竞品对照)"
  echo "# 排除: *Big 类, Quick_* 系列, Nested_Layer* 深度变体"
  echo "# 注: 部分场景×组件组合在代码库无 benchmark 类(功能使然), 矩阵空位标 N/A, 非遗漏"
  echo ""
} > "$OUT"

count=0
for fam in $fams; do
  for c in $suffix_comps; do
    cls="${fam}_${c}"
    is_excluded "$cls" && continue
    if grep -qE "class $cls\b" "$PC"; then
      echo "$cls | $(classify "$cls")" >> "$OUT"; count=$((count+1))
    fi
  done
  # Runtime 路径 (已统一为 <场景>_Runtime 后缀)
  cls="${fam}_Runtime"
  is_excluded "$cls" && continue
  if grep -qE "class $cls\b" "$PC"; then
    echo "$cls | $(classify "$cls")" >> "$OUT"; count=$((count+1))
  fi
done
for c in $deepcopy_comps; do
  cls="DeepCopy_$c"
  is_excluded "$cls" && continue
  if grep -qE "class $cls\b" "$PC"; then
    echo "$cls | $(classify "$cls")" >> "$OUT"; count=$((count+1))
  fi
done
echo "" >> "$OUT"

# === 断言: 排除规则必须生效, 若有漏入则报错退出 ===
leak=0
while IFS= read -r line; do
  [[ "$line" =~ ^# ]] && continue
  [[ -z "$line" ]] && continue
  cls="${line%%|*}"
  cls="$(echo "$cls" | tr -d ' ')"
  if [[ "$cls" == *Big* || "$cls" == *Quick* || "$cls" == *Nested_Layer* ]]; then
    echo "断言失败: 排除类漏入清单 -> $cls" >&2
    leak=1
  fi
done < "$OUT"
if [ "$leak" -ne 0 ]; then
  echo "生成中止: 排除规则未生效, 请检查 Program.cs 或排除判定逻辑。" >&2
  exit 1
fi

echo "=== 生成完毕: 共 $count 个 benchmark 类 ==="
echo "  SELF(自己组件)   = $(grep -c 'SELF' "$OUT")"
echo "  COMP(竞品对照)   = $(grep -c 'COMP' "$OUT")"
echo "  EXTSER(外部序列化)= $(grep -c 'EXTSER' "$OUT")"
echo "  输出文件: $OUT"
