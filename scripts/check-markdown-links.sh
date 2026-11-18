#!/usr/bin/env bash
# check-markdown-links.sh — markdown 链接闭合自检（构建前置门禁）— wrapper
#
# 重构：判定主体从 perl 单行程序改为 python
# （check-markdown-links.py，判据逐条对齐原 perl 版）。动机（实证）：
# 本机无 perl 时原 :97 的 `|| true` 吞掉 `command not found`，随后按「无命中」判通过
# （假绿：stdout「链接闭合自检通过」+ stderr「perl: command not found」+ exit 0）。
# 本 wrapper 对 python 缺失 fail-fast（exit 2），禁止降级为「通过」。
#
# 调用面保持不变：scripts/build-offline-docs.sh:119 直调本脚本，参数透传。
# 退出码：0=全部闭合  1=发现未闭合链接（门禁拦截）  2=前置缺失（python 缺失 / 路径不存在 / 无 md）
# 用法（默认扫描 docs/ 全量 + 仓库根 CONTRIBUTING.md）：
#   ./scripts/check-markdown-links.sh [path ...]     # path 可为目录或文件
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

py_bin="$(command -v python3 || command -v python || true)"
if [ -z "$py_bin" ]; then
  echo "[ERROR] 缺少依赖：python3/python 均不可用——链接闭合自检无法执行（fail-fast，不降级为通过）。" >&2
  echo "        判定主体已迁移至 scripts/check-markdown-links.py，需任一 python3 兼容解释器。" >&2
  exit 2
fi

exec "$py_bin" "$script_dir/check-markdown-links.py" "$@"
