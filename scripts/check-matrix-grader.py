#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check-matrix-grader.py -- 对位矩阵「到位分级」判定器。

按矩阵的分级规则对 108 行独立重判「到位分级」，与矩阵编制期人工判定列（「到位」列）
逐行对照——「通过标准 = 5/5 与编制期人工判定一致」由 --check 样本机制承载。

分级规则：
  E 无文档   架构图「文档」列为 —
  D 文档缺   链接目标页内功能点关键词零命中
  C 仅提及   关键词只在表格行（以 | 开头）出现，且表格块上方紧邻处无独立小节标题（无主小节）
  B 有说明   目标页正文（非表格行）命中，或表格命中所在表格块直接位于独立小节标题之下
             （主小节覆盖——对 B 第一分支「有主小节但缺边界说明」的机械细化），
             但无以关键词为题的独立小节
  A 完整     有以关键词为题的独立小节，且小节内含边界说明词（注意/限制/误用/风险/⚠️/默认）
命中面恒为 md 正文本身：小节归属识别只改「表格行命中」落 C 还是 B 的归属判断，
不引入任何页外内容（DFM region 等）参与命中面（seq 17 类零命中页不得被升档）。

用法：
  python scripts/check-matrix-grader.py <matrix.md>            # 全量重判 + 对照汇总
  python scripts/check-matrix-grader.py <matrix.md> --check 9,13,23,39,101
                                                                 # 只对照指定 seq（对照样本）
退出码：0=对照通过（判定行一致率 100%；非 API 跳过行单段登记、不参与一致率）  1=存在不一致  2=前置缺失
"""
import os
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
ZH = REPO / 'docs' / 'zh-CN'

# 文档页短名 → 候选路径（相对 docs/zh-CN；按序探测第一个存在的文件）
PAGE_DIRS = ['reference', 'how-to/orm', 'how-to/fluent', 'how-to/infrastructure',
             'how-to/declarative', 'how-to/mapping', 'how-to/deep-copy',
             'how-to/compose', 'how-to/aot', 'explanation', 'tutorials', '']
# 边界说明词（对齐矩阵「注意/限制/误用/风险等」开放列举 + 实测补强：「不再/降级」为强边界否定/降级行为词；
# 「默认」不列入——普通描述行高频出现，会虚升 A）
BOUNDARY_WORDS = ('注意', '限制', '误用', '风险', '⚠️', '不支持', '边界', '不再', '降级', '禁止', '无法')


def resolve_page(short):
    short = short.strip().strip('`')
    if not short or short == '—':
        return None
    # 子路径消歧形态（如 declarative/basics）：直接按相对路径解析
    if '/' in short:
        cand = ZH / (short + '.md')
        return cand if cand.is_file() else None
    for d in PAGE_DIRS:
        cand = ZH / d / (short + '.md') if d else ZH / (short + '.md')
        if cand.is_file():
            return cand
    return None


def _table_block_under_section(lines, idx):
    """P1 小节归属识别：命中表格行 lines[idx] 沿连续表格行上溯至表格块顶，
    跳过空行后最近的非表格行若是独立小节标题（#{2,4}）⇒ 该表格块直接位于小节之下，
    小节对这些 API 名构成「主小节」（B 第一分支「有主小节」）。
    取紧邻归属而非祖先包含：删去该小节标题行后表格块即悬于其它正文之上、退回 C——
    小节标题的存在即 B 判定的直接且唯一证据（中和测试据此设计，判定非恒真）。"""
    i = idx
    while i > 0 and lines[i - 1].lstrip().startswith('|'):
        i -= 1
    j = i - 1
    while j >= 0 and not lines[j].strip():
        j -= 1
    return j >= 0 and bool(re.match(r'^#{2,4}\s', lines[j]))


def grade_line(pat, page_path):
    """对单行做五级判定（脚本独立判定）。pat=已编译的 API 关键词正则。
    仅「全部命中都是表格行」时的 C/B 归属由小节识别决定；A 判定与命中面不动。"""
    if page_path is None:
        return 'E', '文档列为 —'
    try:
        text = page_path.read_text(encoding='utf-8')
    except OSError:
        return 'D', '页面不可读'
    if not pat.search(text):
        return 'D', '目标页零命中'
    lines = text.splitlines()
    section_hits = []
    cur_section, cur_has_boundary, cur_is_table = None, False, False
    # P1：命中表格行直接位于独立小节标题之下的表格块 ⇒ 主小节覆盖 → B 而非 C。命中面不变。
    table_under_section = False
    for idx, ln in enumerate(lines):
        hm = re.match(r'^(#{2,4})\s+(.*)', ln)
        if hm:
            if cur_section is not None and pat.search(cur_section):
                section_hits.append(cur_has_boundary)
            cur_section = hm.group(2)
            cur_has_boundary = False
            cur_is_table = False
            continue
        if pat.search(ln):
            if ln.lstrip().startswith('|'):
                cur_is_table = True
                if _table_block_under_section(lines, idx):
                    table_under_section = True
            else:
                cur_is_table = False
        # 边界说明在小节任意行即算（人工口径：小节含边界说明；同行耦合会漏「已知差异」「**边界**：」独立行形态）
        if any(w in ln for w in BOUNDARY_WORDS):
            cur_has_boundary = True
    if cur_section is not None and pat.search(cur_section):
        section_hits.append(cur_has_boundary)
    if section_hits:
        return ('A', '独立小节且含边界说明') if any(section_hits) \
            else ('B', '有独立小节但缺边界说明')
    non_table = any(pat.search(ln) for ln in lines if not ln.lstrip().startswith('|'))
    if non_table:
        return 'B', '正文有独立段落说明'
    if table_under_section:
        return 'B', '表格命中位于独立小节内（主小节覆盖）'
    return 'C', '仅表格行命中（不在任何小节内）'


def main(argv):
    matrix_arg = next((a for a in argv if a.endswith('.md')), None)
    if matrix_arg is None:
        print('用法：check-matrix-grader.py <matrix.md> [--check seq,seq,...] [--verbose]', file=sys.stderr)
        return 2
    matrix = Path(matrix_arg)
    if not matrix.is_file():
        print('[ERROR] 矩阵文件不存在：%s' % matrix, file=sys.stderr)
        return 2
    check_only = None
    verbose = '--verbose' in argv
    if '--check' in argv:
        check_only = {int(x) for x in argv[argv.index('--check') + 1].split(',')}

    text = matrix.read_text(encoding='utf-8')
    rows = []
    for ln in text.split('\n'):
        m = re.match(r'^\|\s*(\d+)\s*\|\s*([^|]+)\|([^|]+)\|([^|]+)\|([^|]+)\|([^|]+)\|', ln)
        if not m:
            continue
        seq, fp, demo, asrt, page, human = (m.group(i).strip() for i in range(1, 7))
        rows.append((int(seq), fp, page, human))
    if not rows:
        print('[ERROR] 未解析到矩阵主表行', file=sys.stderr)
        return 2
    if check_only:
        rows = [r for r in rows if r[0] in check_only]
        if len(rows) < 5:
            print('[ERROR] 对照样本不足 5 行', file=sys.stderr)
            return 2

    mismatch = 0
    skipped = 0
    judged = 0
    for seq, fp, page, human in rows:
        page_path = resolve_page(page)
        api_first = fp.split('/')[0].split('（')[0].strip()
        # 链式调用（fsql.Select<T>().AsDto()）：逐段剥 <...> 与 (...) 后取链尾段为 key，全链为前缀
        segs = [s.split('(')[0].split('<')[0].replace('*', '').strip()
                for s in api_first.split('.') if s.split('(')[0].split('<')[0].strip()]
        parts = [s for s in segs if re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', s)]
        if len(parts) >= 2:
            # 全称或省略命名空间前缀的裸方法名任一命中（C# 文档惯例混用；页指向已由矩阵消歧保证）
            key = parts[-1]
            pat = re.compile(r'\b(?:%s\.)?%s\b' % (re.escape('.'.join(parts[:-1])), re.escape(key)))
        elif len(parts) == 1:
            key = parts[0]
            pat = re.compile(r'\b%s\b' % re.escape(key))
        else:
            print('seq %-4s 跳过（非 API 形态功能点：%s）' % (seq, fp[:20]))
            skipped += 1
            continue
        got, why = grade_line(pat, page_path)
        judged += 1
        ok = (got == human)
        if not ok:
            mismatch += 1
        mark = 'OK ' if ok else 'MISMATCH'
        print('%s seq %-4s 人工=%s 脚本=%s（%s）  %s' % (mark, seq, human, got, why, fp[:28]))

    # 汇总双段口径（实判/跳过双段∧跳过计同的裸总分式永废）：
    # 「一致」分数只由脚本实判行构成（total−mismatch 把跳过行计入分子＝跳过计同空洞，废）；
    # 非 API 跳过行以「y 行登记」单段呈现（逐行打印保留），不并入一致率、不判红。
    print('\n=== 对照汇总：实判 %d/%d 一致＋非 API 跳过 %d 行登记 ===' % (judged - mismatch, judged, skipped))
    if mismatch:
        print('[FAIL] 存在人工/脚本判定不一致——逐行核对上表 MISMATCH（判据口径或人工判定需复核）')
        return 1
    print('[PASS] 对照全部一致（通过标准达成）')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
