#!/usr/bin/env bash
# docs-snapshot.sh — 发布时点文档版本快照
#
# 构建当前工作树的 DocFX 站点，归档至 dist/snapshots/<版本标识>/（不入 git，.gitignore 已覆盖 dist/）。
# 与 CI docs.yml 同链：metadata → strip-external-xref → build（保证快照零外链，口径一致）。
#
# 语义：发布动作（打 tag / Release / Pages 部署）由维护者执行，本脚本供发布流程人工触发，
# 不在 CI 自动触发（方案 O2「发布时点」语义）。发布载体：
#   ① 离线包 = 该版本站点快照（zip 随 Release 附件，见 scripts/build-offline-docs.sh）；
#   ② 在线版本目录 /v{版本}/ 属 Phase 4 站点部署，启用 Pages 后按 dist/snapshots/<tag>/ 发布。
#
# 用法（仓库根执行）：
#   ./scripts/docs-snapshot.sh [版本标识]
#   版本标识默认 = git describe --tags；无 tag 时 = git rev-parse --short HEAD。
# 退出码：0=成功  1=构建/归档失败  2=前置缺失（dotnet tool manifest / docfx 未 restore）
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

# ---- 版本标识 ----
version="${1:-}"
if [ -z "$version" ]; then
  if git describe --tags >/dev/null 2>&1; then
    version="$(git describe --tags 2>/dev/null)"
  else
    version="$(git rev-parse --short HEAD)"
  fi
fi
# 版本标识中不允许路径分隔符（防路径注入 / 目录拆分）；git describe 输出可能含 '/'（tag/branch 名），替换为 '-'
case "$version" in
  */*|*\\*) echo "[WARN] 版本标识含路径分隔符，已替换为 '-'：$version" >&2; version="${version//\//-}" ;;
esac

echo "[1/4] 前置检查：docfx 工具 manifest"
if [ ! -f .config/dotnet-tools.json ]; then
  echo "[ERROR] .config/dotnet-tools.json 缺失（docfx 工具版本锁定文件）" >&2
  exit 2
fi
dotnet tool restore >/dev/null

echo "[2/5] docfx metadata（中英两语 API：dll + 对应语言 XML，docfx.zh.json / docfx.en.json）"
"$script_dir/prepare-api-src.sh" zh-Hans net8.0
"$script_dir/prepare-api-src.sh" en net8.0
dotnet docfx metadata docfx.zh.json >/dev/null
dotnet docfx metadata docfx.en.json >/dev/null

echo "[3/5] 剥离 BCL 外链（审计兜底，中英两语）"
"$script_dir/strip-external-xref.sh"

out_dir="dist/snapshots/$version"
echo "[4/5] docfx build（zh-Hans）→ $out_dir/zh-Hans"
rm -rf "$out_dir"
mkdir -p "$out_dir/zh-Hans"
dotnet docfx build docfx.zh.json -o "$out_dir/zh-Hans" >/dev/null
echo "[5/5] docfx build（en）→ $out_dir/en"
dotnet docfx build docfx.en.json -o "$out_dir/en" >/dev/null

# 快照产物完整性守卫（防静默失败）
html_count="$(find "$out_dir" -type f -name '*.html' | wc -l | tr -d ' ')"
if [ "$html_count" -eq 0 ]; then
  echo "[ERROR] 快照产物为空：$out_dir（docfx build 未产出 html）" >&2
  exit 1
fi

echo "快照完成：$out_dir（${html_count} 个 html，含 zh-Hans/ 与 en/）"
echo "  当前 HEAD：$(git rev-parse --short HEAD)（发布前请核对与 tag $version 一致）"
exit 0
