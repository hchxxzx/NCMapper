#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check-markdown-links.py -- markdown 链接闭合自检（python 主实现）。

背景（实证）：docs 下曾出现 114 处形如 `[文本](路径` 的未闭合链接。DocFX 对其不报错
（构建 0 warning 静默通过），而是把整段渲染为纯文本——站点导航/正文链接「点不动」。
本脚本在构建前置拦截该类缺陷。原 .sh 主体为 perl 单行程序，本机无 perl 时 `|| true`
吞掉 command not found、按「无命中」判通过（实测假绿），故去依赖重写：
python 缺失由 .sh wrapper fail-fast（exit 2），禁止降级为通过。

判据（与原 perl 版逐条对齐）：对每个 `](` 在其后寻找 `)`；若先遇到 URL 终止符
（空白/中英文标点/`|`）、行尾、或下一个 `](`，则判定未闭合。剔除：围栏代码块
（```/~~~，含围栏行本身）与行内代码（`...`）内的 `](`；每行最多报一次。

退出码：0=全部闭合  1=发现未闭合链接  2=前置缺失（路径不存在 / 无 md）
"""
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent

# 终止符码点（对齐原 perl 版 @termCodes）：空格/Tab/、/，/。/；/：/·/）/（/！/？/“/”/【/】/|
TERM_CHARS = {chr(c) for c in (0x20, 0x09, 0x3001, 0xFF0C, 0x3002, 0xFF1B, 0xFF1A,
                               0x00B7, 0xFF09, 0xFF08, 0xFF01, 0xFF1F, 0x201C,
                               0x201D, 0x3010, 0x3011, 0x7C)}
FENCE_RE = re.compile(r'^\s*(?:```|~~~)')
INLINE_CODE_RE = re.compile(r'`[^`]*`')


def collect_md_files(targets):
    """收集待扫 md 文件；目录递归、文件直取、缺失路径 exit 2（对齐原 .sh 收集段）。"""
    files = []
    for t in targets:
        p = Path(t)
        if p.is_dir():
            files.extend(str(q) for q in sorted(p.rglob('*.md')) if q.is_file())
        elif p.is_file():
            files.append(str(p))
        else:
            print(f"[ERROR] 路径不存在：{t}", file=sys.stderr)
            sys.exit(2)
    return sorted(set(files))


def line_has_unclosed(line):
    """单行判定：存在未闭合 `](` 返回 True（状态机与 perl 版逐条对齐）。"""
    s = INLINE_CODE_RE.sub('', line)
    pos = 0
    while True:
        i = s.find('](', pos)
        if i < 0:
            return False
        j, state = i + 2, ""
        while j < len(s):
            if s[j] == ')':
                state = "ok"
                break
            if s[j] in TERM_CHARS or s[j:j + 2] == '](':
                state = "bad"
                break
            j += 1
        if state != "ok":  # bad（终止符/`](`）或行尾未决（""）均判未闭合（对齐 perl 版 :90 行尾置 bad）
            return True
        pos = i + 2


def main():
    targets = sys.argv[1:] or [str(REPO_ROOT / 'docs'), str(REPO_ROOT / 'CONTRIBUTING.md')]
    files = collect_md_files(targets)
    if not files:
        print(f"[ERROR] 未找到任何 md 文件（扫描目标：{' '.join(targets)}）", file=sys.stderr)
        sys.exit(2)

    hits = []
    for f in files:
        try:
            text = Path(f).read_text(encoding='utf-8')
        except OSError as e:
            print(f"[ERROR] 无法读取：{f}（{e}）", file=sys.stderr)
            continue
        fence = False
        for ln, line in enumerate(text.splitlines(), start=1):
            if FENCE_RE.match(line):
                fence = not fence
                continue
            if not fence and line_has_unclosed(line):
                hits.append(f"{f}:{ln}: {line}")

    if hits:
        print(f"[UNCLOSED-LINK] 发现 {len(hits)} 行含未闭合 markdown 链接（形如 [文本](路径 缺右括号）：")
        for h in hits:
            print(h)
        print("  → DocFX 会把未闭合链接静默渲染为纯文本（构建不报错），导致站点导航/正文链接不可点。")
        print("  → 修复：在链接 URL 后补右括号 ')'（勿改动路径本身）。")
        sys.exit(1)

    print(f"链接闭合自检通过：{len(files)} 个 md 文件全部链接闭合。")
    sys.exit(0)


if __name__ == '__main__':
    main()
