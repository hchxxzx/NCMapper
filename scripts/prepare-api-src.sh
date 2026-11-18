#!/usr/bin/env bash
# prepare-api-src.sh — 准备指定语言 API 参考的 metadata 源（dll + 对应语言 XML）
#
# 背景（双语言 API 参考生成方案，2026-09-01 定稿）：
#   DocFX `dotnet docfx metadata` 从 .csproj 编译生成 API 参考时，注释绑定的是编译产物 obj 目录的
#   中文 XML（源码重新生成，无法注入其他语言 XML）。因此中英两语 API 参考统一改用「dll + 同目录同名 XML」：
#   DocFX 从 dll 反射生成成员集合时，注释源 = dll 旁的 NCMapper.xml（可随语言切换）。
#   这样中英两语 API 面一致（含构造器/运算符/继承成员，zh 762 / en 769），口径统一。
#
# 本脚本把指定语言的 API metadata 输入组装到 temp/{lang}-api-src/：
#   - NCMapper.dll   ：复制已构建的程序集（默认 Release/net8.0，与 CI docs.yml 构建口径一致）
#   - NCMapper.xml   ：复制 locales/{lang}/NCMapper.xml（该语言权威翻译，public-only，与 zh-Hans 对齐）
#   组装后对应 docfx.{lang}.json 的 metadata 条目 src=temp/{lang}-api-src/*.dll 即可生成该语言 API 参考。
#
# 用法（仓库根执行）：
#   ./scripts/prepare-api-src.sh <lang> [TFM]   （lang=zh-Hans|en|...，默认 TFM net8.0）
# 前置：已构建 src/NCMapper 对应 TFM（CI docs.yml 在 metadata 前先 build Release/net8.0）。
# 退出码：0=成功  1=失败（源缺失）  2=参数错误
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

if (( $# < 1 )); then
  echo "[ERROR] 缺少语言参数：$0 <lang> [TFM]" >&2
  exit 2
fi
lang="$1"
tfm="${2:-net8.0}"
case "$tfm" in
  net8.0|net9.0|net10.0) ;;
  *)
    echo "[ERROR] 不支持的 TFM：$tfm（可选 net8.0 / net9.0 / net10.0）" >&2
    exit 2
    ;;
esac

dll_src="$repo_root/src/NCMapper/bin/Release/$tfm/NCMapper.dll"
xml_src="$repo_root/locales/$lang/NCMapper.xml"
src_dir="$repo_root/temp/${lang}-api-src"

# ---- 前置检查 ----
if [ ! -f "$dll_src" ]; then
  echo "[ERROR] 程序集缺失：$dll_src（请先 build src/NCMapper -c Release -f $tfm）" >&2
  exit 1
fi
if [ ! -f "$xml_src" ]; then
  echo "[ERROR] XML 缺失：$xml_src（locales/$lang/ 不存在或未翻译）" >&2
  exit 1
fi

# ---- API 输出目录预清场 ----
# docfx metadata 不清空旧输出：类型改名/退役后旧名 yml 会与新页并存，离线包混入退役类型页
# （实证：ConvertUsing/ConvertBackUsingAttribute 旧名 yml 残留致 api-coverage 门禁报 EXTRA）。
# 限定目录写死（中英两站同受益）；该目录 git 不跟踪、由 metadata 步骤再生。
for api_dir in "$repo_root/docs/zh-CN/reference/api" "$repo_root/docs/en-US/reference/api"; do
  if [ -d "$api_dir" ]; then
    rm -rf "$api_dir"
    echo "[OK] API 输出目录已清场：$api_dir"
  fi
done

# ---- 组装 ----
mkdir -p "$src_dir"
cp -f "$dll_src" "$src_dir/NCMapper.dll"
cp -f "$xml_src" "$src_dir/NCMapper.xml"

echo "[OK] $lang API metadata 源已就绪：$src_dir"
echo "   dll : NCMapper.dll（Release/$tfm，$(du -h "$src_dir/NCMapper.dll" | cut -f1)）"
echo "   xml : NCMapper.xml（locales/$lang，$(grep -c 'member name=' "$src_dir/NCMapper.xml") members）"
exit 0
