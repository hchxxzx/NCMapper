#!/usr/bin/env bash
# prepare-en-site-sources.sh — 英文站资产换入（2026-09-30）
#
# 背景：英文离线包/在线英文站需全英文资产，但仓库以中文原文件为权威源（中文包从原版构建）。
# 口径：英文包只装 wiki 文档 + DEMO（samples），仓库级文件（CHANGELOG/CONTRIBUTING）不入英文站。
#
# DEMO .cs 注释多语言（方案A：注释侧车，2026-09-30）：
#   代码唯一源 samples/...（中文注释）；每语言译文存 docs/i18n/<lang>/samples/.../<文件>.cs.json
#   （纯注释数据，不含代码；侧车 zh 原文与源文件逐条对账，源文件改动 → 构建失败提示重译）。
#   构建期由 scripts/inject-demo-comments.ps1 把注释填回代码（只改注释区间，代码字节恒等是
#   结构性保证；#region 锚点非注释，docfx [!code-csharp] 引用不受影响）。加语言 = 加侧车，零代码改动。
#
# 操作：
#   swap-on     docfx build docfx.en.json 之前：注入英文注释版并临时覆盖仓库原文件
#   swap-off    build 之后：还原中文原版（必须执行，即使构建失败——调用方用 trap 兜底）
#   overlay <site>  build 之后：站点内 raw 文件资产（不经构建期渲染/嵌入的）直接覆盖英文版
#
# raw 资产（overlay 类）：
#   docs/en-US/site-assets/samples/NCMapper.Samples/README.md  → 站点 samples/NCMapper.Samples/README.md
#   docs/en-US/site-assets/samples/NCMapper.AotSample/README.md → 站点 samples/NCMapper.AotSample/README.md
#
# 用法（仓库根执行）：
#   ./scripts/prepare-en-site-sources.sh swap-on [lang]    # lang 缺省 en
#   ./scripts/prepare-en-site-sources.sh swap-off [lang]   # lang 缺省 en
#   ./scripts/prepare-en-site-sources.sh overlay <site_dir>
# 多语言口径（2026-10-01）：swap-on/swap-off 按语言参数化——侧车目录 docs/i18n/<lang>、
# 注入产物 temp/<lang>-demo-overlay、备份命名空间 temp/_orig_<lang>_<文件名>（按语言隔离，
# 各语言构建互不干扰）。overlay 仍为 EN 站 raw 资产（其余语言暂无 raw 资产）。
# 退出码：0=成功  2=参数/前置缺失
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"
mkdir -p temp

# PowerShell 解释器（本地 Windows=powershell 5.1，CI ubuntu=pwsh 预装）
if command -v pwsh >/dev/null 2>&1; then PS_BIN="pwsh"; else PS_BIN="powershell"; fi

# dst_rel:en 对（overlay 类：raw 文件资产，build 后直接覆盖站点产物）
overlay_pairs=(
  "samples/NCMapper.Samples/README.md:docs/en-US/site-assets/samples/NCMapper.Samples/README.md"
  "samples/NCMapper.AotSample/README.md:docs/en-US/site-assets/samples/NCMapper.AotSample/README.md"
)

case "${1:-}" in
  swap-on)
    lang="${2:-en}"
    overlay_dir="temp/${lang}-demo-overlay"
    # 1) 注入：侧车 docs/i18n/<lang> → temp/<lang>-demo-overlay（镜像 samples 路径）
    # -ExecutionPolicy Bypass：本机 Windows PowerShell 5.1 默认策略禁脚本（CI pwsh 下无害）
    rm -rf "$overlay_dir"
    "$PS_BIN" -NoProfile -ExecutionPolicy Bypass -File scripts/inject-demo-comments.ps1 -Language "$lang" -SidecarDir docs/i18n -OutputDir "$overlay_dir"
    # 2) 覆盖仓库原文件（备份至 temp/_orig_<lang>_*，swap-off 还原）
    count=0
    while IFS= read -r f; do
      rel="${f#./}"
      if [ ! -f "$rel" ]; then
        echo "[ERROR] 注入产物对应源文件缺失：$rel" >&2
        exit 2
      fi
      cp -f "$rel" "temp/_orig_${lang}_$(basename "$rel")"
      cp -f "$overlay_dir/$rel" "$rel"
      count=$((count+1))
    done < <(cd "$overlay_dir" && find . -type f -name '*.cs')
    echo "[OK] $lang 站源文件换入完成（$count 个注入 .cs，原文件备份至 temp/_orig_${lang}_*）"
    ;;
  swap-off)
    lang="${2:-en}"
    overlay_dir="temp/${lang}-demo-overlay"
    restored=0
    for f in "temp/_orig_${lang}_"*; do
      [ -f "$f" ] || continue
      base="$(basename "$f")"
      case "$base" in
        _orig_${lang}_ComposeScenarios.cs) dst="samples/NCMapper.Samples/03-Compose/ComposeScenarios.cs" ;;
        _orig_${lang}_FreeSqlScenarios.cs) dst="samples/NCMapper.Samples/05-Orm/FreeSqlScenarios.cs" ;;
        *)
          # 其余演示文件：按注入产物镜像路径还原（备份名 _orig_<lang>_<文件名> → 去前缀找镜像）
          if [ -d "$overlay_dir" ]; then
            match="$(cd "$overlay_dir" && find . -type f -name "${base#_orig_${lang}_}" | head -n1)"
            if [ -n "$match" ]; then dst="${match#./}"; else echo "[WARN] 无法识别的备份文件：$f" >&2; continue; fi
          else
            echo "[WARN] 无法识别的备份文件：$f" >&2; continue
          fi
          ;;
      esac
      cp -f "$f" "$dst"
      rm -f "$f"
      restored=$((restored+1))
    done
    echo "[OK] $lang 站中文原版已还原（$restored 个）"
    ;;
  overlay)
    site="${2:?用法: prepare-en-site-sources.sh overlay <site_dir>}"
    [ -d "$site" ] || { echo "[ERROR] 站点目录不存在：$site" >&2; exit 2; }
    for pair in "${overlay_pairs[@]}"; do
      dst_rel="${pair%%:*}"; en="${pair#*:}"
      cp -f "$en" "$site/$dst_rel"
    done
    echo "[OK] EN 站 raw 资产已覆盖（${#overlay_pairs[@]} 个）"
    ;;
  *)
    echo "[ERROR] 未知命令：${1:-}（可选 swap-on / swap-off / overlay）" >&2
    exit 2
    ;;
esac
