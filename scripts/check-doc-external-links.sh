#!/usr/bin/env bash
# check-doc-external-links.sh — 站点产物外链审计（docs.yml 步骤）
#
# 扫描站点产物目录下全部 *.html，断言 src=/href= 属性中无 http:// / https://
# 绝对外链（离线包零外链门禁：file:// 协议下外链不可达且破坏离线性）。
# 放行：相对路径、锚点（#xxx）、data: URI。
#
# 输出：命中时逐条打印 `文件:行号: 匹配片段`；通过时打印扫描文件数统计。
# 用法（仓库根或任意目录均可；参数为站点产物目录，默认仓库根 temp/site/）：
#   ./scripts/check-doc-external-links.sh [site_dir]
# 退出码：0=零外链  1=发现外链（门禁拦截）  2=前置缺失（目录不存在或无 html）
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

site_dir="${1:-$repo_root/temp/site}"

if [ ! -d "$site_dir" ]; then
  echo "[ERROR] 站点产物目录缺失：$site_dir" >&2
  echo "        请先运行：dotnet docfx build（或确认 docs.yml build 步骤产物路径）" >&2
  exit 2
fi

html_count="$(find "$site_dir" -type f -name '*.html' | wc -l | tr -d ' ')"
if [ "$html_count" -eq 0 ]; then
  echo "[ERROR] 站点产物目录内无任何 html 文件：$site_dir（docfx build 未产出？）" >&2
  exit 2
fi

# 匹配 src=/href= 直接跟 http:// 或 https://（允许引号、允许无引号）。
# -I 跳过二进制；输出 文件:行号:行内容 供定位。
hits="$(grep -rInE '(src|href)[[:space:]]*=[[:space:]]*"?https?://' "$site_dir" --include='*.html' || true)"

if [ -n "$hits" ]; then
  count="$(printf '%s\n' "$hits" | wc -l | tr -d ' ')"
  echo "[EXTERNAL-LINK] 站点产物发现 ${count} 处 http(s) 外链（离线包零外链门禁拦截）："
  printf '%s\n' "$hits"
  exit 1
fi

echo "外链审计通过：${html_count} 个 html 文件 src=/href= 零 http(s) 外链。"
exit 0
