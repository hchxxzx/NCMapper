#!/usr/bin/env bash
# strip-external-xref.sh — 剥离 DocFX metadata yml 中的 BCL 外链
#
# 背景：Docfx.Dotnet.dll 内嵌 BCL 程序集 → learn.microsoft.com 的映射，
# `dotnet docfx metadata` 会把 4200+ 处 `isExternal: true` + `href: https://learn.microsoft.com/...`
# 写入生成的 yml，build 将其渲染为 API 页外链，违反 G2（零外链）。
# `xrefService: []` 只禁构建期联网拉取 xrefmap，不影响该内嵌映射（批 1 实证）。
#
# 用法：./scripts/strip-external-xref.sh [api-dir ...]
#   默认处理全部已接入语言的 API 参考目录（2026-10-01 改：从 docfx.*.json 的 metadata.dest 自动
#   推导，新增语言站接入后无需再改本脚本）：
#     docs/zh-CN/reference/api   docs/en-US/reference/api
#   也可传自定义目录（一个或多个，逐个处理）。
# 流水线位置：docfx metadata 之后、docfx build 之前
#   （CI 链：docfx metadata && ./scripts/strip-external-xref.sh && ./scripts/check-api-coverage.sh && docfx build）
# yml 为生成物（.gitignore 已忽略 reference/api/ 目录）；本脚本幂等，不改任何叙事正文。
#
# 目录表按语言展开；新增语言须同步登记，否则该语言的 API 页会残留外部 xref 外链导致外链审计失败。
# 现改为从仓库根的 docfx.*.json 里提取 `metadata.dest` 字段（即各语言的 API 输出目录），
# 语言站一旦接入配置即自动纳入，杜绝同类"新增语言漏改脚本"缺口。
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

if (( $# > 0 )); then
  api_dirs=("$@")
else
  # 从各语言 docfx 配置提取 metadata.dest（去掉 JSON 行注释后按行匹配，配置为项目自有格式）
  api_dirs=()
  for cfg in "$repo_root"/docfx.*.json; do
    [[ -f "$cfg" ]] || continue
    dest="$(sed -E 's@//.*$@@' "$cfg" | grep -oE '"dest"[[:space:]]*:[[:space:]]*"[^"]+"' \
            | sed -E 's/.*"dest"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/' | head -n1)"
    [[ -n "$dest" ]] && api_dirs+=("$dest")
  done
  if (( ${#api_dirs[@]} == 0 )); then
    echo "strip-external-xref: 未能从 docfx.*.json 推导出任何 API 目录，回退默认两语" >&2
    api_dirs=(docs/zh-CN/reference/api docs/en-US/reference/api)
  fi
fi

shopt -s nullglob
for api_dir in "${api_dirs[@]}"; do
  if [[ ! -d "$api_dir" ]]; then
    echo "strip-external-xref: 目录不存在: $api_dir（跳过）" >&2
    continue
  fi

  files=("$api_dir"/*.yml)
  if (( ${#files[@]} == 0 )); then
    echo "strip-external-xref: $api_dir 下无 yml，跳过"
    continue
  fi

  count=0
  for f in "${files[@]}"; do
    if grep -qE 'isExternal: true|^ *href: https?://' "$f"; then
      sed -i -E '/isExternal: true/d; /^ *href: https?:\/\//d' "$f"
      count=$((count + 1))
    fi
  done
  echo "strip-external-xref: 已清理 $count/${#files[@]} 个 yml（目录: $api_dir）"
done
