#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check-doc-fakescan.py — 手抄代码块「幽灵 API」过渡守护

背景：文档手抄代码块与 samples 之间没有机器可校验的关联，手抄块写了不存在的 API 时
没有任何门禁能发现（实测：变异一个假 API 后全部既有门禁仍为绿）。
本脚本兜住这一类错——文档手抄块写了不存在的 API。

扫描源：docs/zh-CN/**/*.md 的 ```csharp 围栏块（DFM 引用 [!code-csharp 为独立单行语法，
不开围栏，天然不进扫描；围栏块全部视为手抄）。

抽取六类（覆盖面不得小于六类——五类前缀式会漏 Fluent 链式终端）：
  1. Mapper.<成员>
  2. <Xxx>Mapper.<成员>          （如 NCMapperServiceExtensions.AddNCMapper）
  3. MapTo[A-Z]\\w*              （源生成扩展方法 MapToXxxDto / MapToXxxDtoList）
  4. MapValidator.<成员>
  5. SqlDialectResolver.<成员>
  6. 链式终端 .(MapFrom|MapTo|ConvertUsing|Condition|PreCondition|NullSubstitute|MapListInto)(

存在性判定：成员名在 src/（排除 obj/bin）做 \\b<成员>\\b 全文检索。

C7 / 主语判据 / arity 观察列扩面（六类既有抽取与判定语义一字未动；新增三面）：
  C7 类名.方法名组合存在性（第三形态盲区＝「类名实存即放行」假阴性）：
    抽取围栏块内 <大写宿主>.<方法>( 调用；主语判据先行——宿主命中外部库
    一等概念词族（SUBJECT_EXTERNAL_HOSTS）或不在 src+文档类型索引 ⇒ 不判（以外部库为主语
    的正确描述非幽灵）；宿主实存（NCMapper/文档自定义类型＝以 NCMapper 为主语）∧ 方法名
    src 全文零命中 ⇒ [FAKE_API]「类真·方法虚构」。生成产物名先过类型索引判定（判定零放宽）。
  arity 观察列（签名级参数形态）：链式调用实参 new Type( 形态 vs src 声明槽位
    形参类型（多重载按参数量纲过滤后唯一存活才比对；继承闭包相容放行）⇒ [WARN_ARITY]
    仅观察不判红。参数量纲级违例即报形态经现字节仿真否证（9641 噪声）不实现，登记残线索。
  内建自证扩面（阳性对照常驻）：C7 假例五枚必检出 ∧ 外部主语免判四例 ∧ 生成产物对照免判；
    任一失效 ⇒ exit 2（检索链失效，非零幽灵）。arity 自证挂 src 声明面前提（软常驻）。

退出码：
  0 = 扫描完成且零幽灵
  1 = 存在幽灵 API（[FAKE_API] 明细见输出）
  2 = 检索链失效（src 缺失 / 阳性对照未命中）——绝不可输出「零幽灵」
"""
import re
import sys
from pathlib import Path
# Windows 控制台/管道默认用 ANSI 代码页（cp1252 / cp936）编码 stdout，输出中文会抛
# UnicodeEncodeError 而非打印乱码——在 CI 的 windows runner（英文系统 cp1252）上直接
# 导致门禁假红。强制 UTF-8 并放宽错误策略，保证任何平台输出中文都不崩。
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

REPO = Path(__file__).resolve().parent.parent
ZH = REPO / 'docs' / 'zh-CN'
SRC = REPO / 'src'

# 白名单：空起步；确需加入时逐条附理由
ALLOWLIST = [
    # (成员名, 理由)
]

# 阳性对照符号：src/ 中必然存在的符号（检索链自检防线）
POSITIVE_CONTROLS = ('Map', 'MapFrom', 'MapProperty')

# 六类抽取正则：(类别名, 编译正则, 成员名捕获组号)
PATTERNS = [
    ('C1 Mapper.<成员>',        re.compile(r'\bMapper\.([A-Z][A-Za-z0-9_]*)'), 1),
    ('C2 <Xxx>Mapper.<成员>',   re.compile(r'\b([A-Za-z_][A-Za-z0-9_]*Mapper)\.([A-Z][A-Za-z0-9_]*)'), 2),
    ('C3 MapTo[Xxx] 扩展',      re.compile(r'\bMapTo([A-Z][A-Za-z0-9_]*)'), 0),
    ('C4 MapValidator.<成员>',  re.compile(r'\bMapValidator\.([A-Z][A-Za-z0-9_]*)'), 1),
    ('C5 SqlDialectResolver',   re.compile(r'\bSqlDialectResolver\.([A-Z][A-Za-z0-9_]*)'), 1),
    ('C6 Fluent 链式终端',      re.compile(r'\.(MapFrom|MapTo|ConvertUsing|Condition|PreCondition|NullSubstitute|MapListInto)\('), 1),
]

# ── C7「类名.方法名」组合抽取（第三形态盲区＝类名实存即放行）──
# 宿主＝大写起首类型形态（≥3 字符）＋方法名大写起首＋可选泛型槽＋'('。实例链（小写 host）不入 C7。
C7_CALL = re.compile(r'\b([A-Z][A-Za-z0-9_]{2,})\.([A-Z][A-Za-z0-9_]*)\s*(?:<[^<>]{0,80}>)?\(')
# C1/C2/C4/C5 抽取域宿主（XxxMapper 后缀∧MapValidator∧SqlDialectResolver）已由既有六类覆盖 ⇒ C7 跳过免双报；
# 其判定语义（成员名 src 全文零命中才判）与 C7 完全同构＝零放宽。
C7_SKIP_HOST = re.compile(r'(?:Mapper)$')
C7_SKIP_HOSTS_EXACT = ('MapValidator', 'SqlDialectResolver')

# ── 主语判据层（成员引用的宿主为外部库一等概念时不判幽灵；同词两判）──
# 逐族附理由（白名单须逐条给理由）；NCMapper 自有词汇（ISqlDialect/DapperDtoSelector 等）禁入。
SUBJECT_EXTERNAL_HOSTS = frozenset({
    # Dapper 原生（取证＝库自带 Dapper.xml：T:Dapper.DefaultTypeMap／CustomPropertyTypeMap／SqlMapper.GetTypeMap／AddTypeMap）
    'SqlMapper', 'TypeMap', 'DefaultTypeMap', 'CustomPropertyTypeMap', 'TypeHandlerStack', 'GridReader',
    # FreeSql 原生（IFreeSql/ISelect/IInsert/IUpdate/IDelete/IAdo 门面族）
    'IFreeSql', 'FreeSql', 'FreeSqlBuilder', 'ISelect', 'IInsert', 'IUpdate', 'IDelete', 'IAdo',
    # EF Core 原生（DbContext/DbSet/ModelBuilder 一等概念族）
    'DbContext', 'DbSet', 'ModelBuilder', 'EntityTypeBuilder', 'RelationalTypeMapping', 'ChangeTracker', 'Database',
    # ADO.NET/BCL 惯用宿主（双保险：即便与 NCMapper 类型撞名也不判外部主语）
    'IDbConnection', 'IDbCommand', 'IDbTransaction', 'DbType', 'CommandType',
    'Console', 'DateTime', 'DateTimeOffset', 'Convert', 'Enumerable', 'Queryable', 'Task', 'Thread',
    'Stopwatch', 'JsonConvert', 'StringBuilder', 'Encoding', 'Activator', 'Math', 'Guid',
})

# ── arity 观察列（签名级参数形态＝类型级；量纲级现字节仿真否证，不实现）──
ARITY_CALL = re.compile(r'\.([A-Z][A-Za-z0-9_]*)\s*(?:<[^<>]{0,80}>)?\(')
ARITY_NEW_ARG = re.compile(r'^new\s+([A-Z][A-Za-z0-9_]*)\s*(?:<[^<>]*>)?\s*\(')
ARITY_MOD_LINE = re.compile(r'(?:^|[\s>])(?:public|private|internal|protected|static|virtual|override|async|abstract|partial|extern|unsafe|sealed)\b')
ARITY_DECL_NAME = re.compile(r'([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^<>]{0,60}>)?\s*\(')
ARITY_TYPE_EXT = re.compile(r'\b(?:class|interface|record|struct)\s+([A-Z][A-Za-z0-9_]*)[^{;]*?:\s*([^{;]+)')

# ── 内建自证假例（阳性对照常驻扩面；假例集正本＝仓外 fakecases.txt A/B 组）──
SELFTEST_C7_FAKE = (   # 类真·方法虚构／嫁接型＝判据必检出，漏检 ⇒ 检索链失效 exit 2
    ('DapperDtoSelector', 'EnsureRegistered'),                 # A-1（第三形态原型）
    ('DapperDtoSelector', 'AddTypeMap'),                       # A-2（嫁接：Dapper 动词挂 NCMapper 主语）
    ('EFCoreDtoSelector', 'RegisterTypeMap'),                  # A-4
    ('FreeSqlDtoSelector', 'EnsureTypeHandlersRegistered'),    # A-5
    ('NCMapperDapperExtensions', 'RegisterTypeMaps'),          # A-6（Extensions 宿主＝六类盲区）
)
SELFTEST_C7_EXEMPT = ( # 外部主语＝必不判（主语判据反例）
    ('SqlMapper', 'AddTypeMap'),
    ('DefaultTypeMap', 'FindMappedMembers'),
    ('DbContext', 'SaveChanges'),
    ('IFreeSql', 'Select'),
)
SELFTEST_ARITY_CALL = 'selector.ToPagedList<UserDto>(sql, null, 1, 10, new MySqlDialect())'  # A-3（arity 观察列原型）

FENCE_OPEN = re.compile(r'^(\s*)```csharp\s*$')
FENCE_CLOSE = re.compile(r'^\s*```\s*$')


def build_symbol_index():
    """一次性缓存 src/ 全部 .cs 文本（排除 obj/bin），返回 (files 数, 拼接文本)。"""
    if not SRC.is_dir():
        return 0, None
    chunks = []
    n = 0
    for cs in SRC.rglob('*.cs'):
        rel = cs.relative_to(REPO).as_posix()
        if '/obj/' in '/' + rel or '/bin/' in '/' + rel:
            continue
        try:
            chunks.append(cs.read_text(encoding='utf-8', errors='replace'))
            n += 1
        except OSError:
            continue
    return n, '\n'.join(chunks)


TYPE_DECL = re.compile(r'\b(?:class|interface|record|struct|enum)\s+([A-Z][A-Za-z0-9_]*)')


def build_type_index(docs_text_chunks=None):
    """类型名索引：src/ + samples/（生成方法落在用户程序集）+ docs 围栏块内自定义类型（机制讲解合法示意）。"""
    names = set()
    for root in (SRC, REPO / 'samples'):
        if not root.is_dir():
            continue
        for cs in root.rglob('*.cs'):
            rel = cs.relative_to(REPO).as_posix()
            if '/obj/' in '/' + rel or '/bin/' in '/' + rel:
                continue
            try:
                names.update(TYPE_DECL.findall(cs.read_text(encoding='utf-8', errors='replace')))
            except OSError:
                continue
    for md in ZH.rglob('*.md'):
        try:
            names.update(TYPE_DECL.findall(md.read_text(encoding='utf-8', errors='replace')))
        except OSError:
            continue
    return names


def symbol_exists(name, src_text):
    return re.search(r'\b%s\b' % re.escape(name), src_text) is not None


GEN_NAME = re.compile(r'MapTo([A-Z][A-Za-z0-9_]*?)(List)?$|'
                      r'(Map|MapList|Update)_([A-Za-z0-9_]+)_To_([A-Za-z0-9_]+?)(_WithAction)?$')


def generated_name_ok(name, types):
    """源生成产物示意名合法性（MapTo{Target} / Map_{S}_To_{T} 等模板，CodeGenerator.Compose.cs:105/:227）。
    返回 True=类型存在（合法示意）/ False=类型不存在（幽灵）/ None=非生成产物形态（交常规判定）。"""
    m = GEN_NAME.match(name)
    if not m:
        return None
    if m.group(1) is not None:          # MapToXxx[List]
        return m.group(1) in types
    return m.group(4) in types and m.group(5) in types   # Map|MapList|Update_S_To_T[_WithAction]


# ══════════════ 新增判据层（C7 组合存在性 ∧ 主语判据 ∧ arity 观察列）══════════════

def balanced_close(text, open_idx):
    """text[open_idx]=='(' → 返回配平闭括号下标（跳字符串/字符面）；不配平返回 -1。"""
    depth, i, in_s, in_c, esc = 0, open_idx, False, False, False
    while i < len(text):
        ch = text[i]
        if esc:
            esc = False
        elif ch == '\\' and (in_s or in_c):
            esc = True
        elif ch == '"' and not in_c:
            in_s = not in_s
        elif ch == "'" and not in_s:
            in_c = not in_c
        elif not in_s and not in_c:
            if ch in '([{':
                depth += 1
            elif ch in ')]}':
                depth -= 1
                if depth == 0:
                    return i
        i += 1
    return -1


def top_level_split(seg):
    """顶层逗号拆分（括号/字符串感知）。"""
    parts, depth, cur, in_s, in_c, esc = [], 0, [], False, False, False
    for ch in seg:
        if esc:
            cur.append(ch); esc = False; continue
        if ch == '\\' and (in_s or in_c):
            cur.append(ch); esc = True; continue
        if ch == '"' and not in_c:
            in_s = not in_s; cur.append(ch); continue
        if ch == "'" and not in_s:
            in_c = not in_c; cur.append(ch); continue
        if not in_s and not in_c:
            if ch in '([{':
                depth += 1
            elif ch in ')]}':
                depth -= 1
            elif ch == ',' and depth == 0:
                parts.append(''.join(cur)); cur = []; continue
        cur.append(ch)
    parts.append(''.join(cur))
    return [p.strip() for p in parts if p.strip()]


def build_decl_index():
    """src/ 方法声明索引（arity 观察列消费）：name -> [(rel, 参列表, 必填数, 最大数)]；
    继承/实现面：Type -> {直接基类与接口}。声明识别＝同行前缀含访问/修饰符（保守门）。"""
    decls, type_ext = {}, {}
    for cs in SRC.rglob('*.cs'):
        rel = cs.relative_to(REPO).as_posix()
        if '/obj/' in '/' + rel or '/bin/' in '/' + rel:
            continue
        try:
            t = cs.read_text(encoding='utf-8', errors='replace')
        except OSError:
            continue
        for m in ARITY_DECL_NAME.finditer(t):
            line_start = t.rfind('\n', 0, m.start()) + 1
            if not ARITY_MOD_LINE.search(t[line_start:m.start()]):
                continue
            ci = m.end() - 1
            ce = balanced_close(t, ci)
            if ce < 0 or ce - ci > 1200:
                continue
            plist = top_level_split(t[ci + 1:ce])
            mx = len(plist)
            mn = len([p for p in plist if '=' not in p])
            decls.setdefault(m.group(1), []).append((rel, plist, mn, mx))
        for m in ARITY_TYPE_EXT.finditer(t):
            type_ext.setdefault(m.group(1), set()).update(
                x.strip().split('<')[0].strip() for x in top_level_split(m.group(2)))
    # 继承/实现面补采 docs 围栏自定义类型（示例类如 JapanPhoneMask : IMask 定义在文档＝与类型索引面同构，
    # 防观察列假报；方法声明索引不采 docs＝保守不扩判面）
    for md in ZH.rglob('*.md'):
        try:
            t = md.read_text(encoding='utf-8', errors='replace')
        except OSError:
            continue
        for m in ARITY_TYPE_EXT.finditer(t):
            type_ext.setdefault(m.group(1), set()).update(
                x.strip().split('<')[0].strip() for x in top_level_split(m.group(2)))
    return decls, type_ext


def type_closure(ty, type_ext, limit=64):
    """继承/实现闭包（浅链，上限防失控）。"""
    seen, stack = set(), [ty]
    while stack:
        c = stack.pop()
        if c in seen:
            continue
        seen.add(c)
        if len(seen) < limit:
            stack.extend(type_ext.get(c, ()))
    return seen


def judge_c7(host, method, types, src_text, allow):
    """C7 组合存在性判定（主语判据先行）。返回 (kind, why)：
    kind='fake' ⇒ 幽灵；kind='exempt' ⇒ 主语/生成产物/索引外宿主免判；kind='pass' ⇒ 放行或交既有类覆盖。"""
    if C7_SKIP_HOST.search(host) or host in C7_SKIP_HOSTS_EXACT:
        return ('pass', 'covered-by-C1/C2/C4/C5')
    if host in SUBJECT_EXTERNAL_HOSTS:
        return ('exempt', 'external-subject')
    gen = generated_name_ok(method, types)
    if gen is True:
        return ('exempt', 'generated-type-ok')
    if gen is False:
        return ('fake', '生成产物类型不存在')
    if method in allow:
        return ('pass', 'allowlist')
    if host not in types:
        return ('exempt', 'host-not-ncmapper-subject')
    if not symbol_exists(method, src_text):
        return ('fake', '类真·方法虚构')
    return ('pass', 'member-src-exists')


def observe_arity(block_text, block_start_line, decls, type_ext):
    """arity 观察列（类型级＝签名级参数形态）：链式调用实参 new Type( 与声明槽位形参类型
    不兼容（双向继承闭包不含）⇒ 记 WARN。门＝多重载按参数量纲过滤后唯一存活（保守）。不判红。"""
    warns = []
    for m in ARITY_CALL.finditer(block_text):
        nm = m.group(1)
        ds = decls.get(nm)
        if not ds:
            continue
        ci = m.end() - 1
        ce = balanced_close(block_text, ci)
        if ce < 0:
            continue
        args = top_level_split(block_text[ci + 1:ce])
        n = len(args)
        live = [d for d in ds if d[2] <= n <= d[3]]
        if len(live) != 1:
            continue
        dfile, plist = live[0][0], live[0][1]
        for k, a in enumerate(args):
            mm = ARITY_NEW_ARG.match(a)
            if not mm or k >= len(plist):
                continue
            actual = mm.group(1)
            toks = re.split(r'[\s=]', plist[k].strip())
            formal = next((w for w in toks if w and not re.match(r'^(ref|out|in|params|this)$', w)), '')
            formal = formal.replace('?', '').replace('global::', '').split('<')[0]
            if not formal or not formal[0].isupper():
                continue
            if actual != formal and actual not in type_closure(formal, type_ext) \
                    and formal not in type_closure(actual, type_ext):
                warns.append((block_start_line + block_text.count('\n', 0, m.start()),
                              nm, k + 1, actual, formal, dfile))
    return warns


def iter_fenced_blocks(lines):
    """围栏 ```csharp 块收集：[(块首行号, 块文本)]（与主环同构的只读遍历，供 arity 页级观察）。"""
    out, in_block, start, buf = [], False, 0, []
    for i, ln in enumerate(lines):
        if not in_block and FENCE_OPEN.match(ln):
            in_block, start, buf = True, i + 1, []
            continue
        if in_block and FENCE_CLOSE.match(ln):
            in_block = False
            out.append((start, '\n'.join(buf)))
            continue
        if in_block:
            buf.append(ln)
    return out


def run_selftests(types, src_text, decls, type_ext):
    """内建自证（阳性对照常驻扩面）：C7 假例必检出 ∧ 外部主语必免判 ∧ 生成产物对照必免判 ∧ arity 假例必出 warn。
    返回 (fails, notes)；fails 非空 ⇒ 检索链失效。"""
    fails, notes = [], []
    empty_allow = frozenset()
    skipped = 0
    for host, meth in SELFTEST_C7_FAKE:
        if host not in types:
            skipped += 1
            notes.append('C7 假例前提缺失跳过：%s 不在类型索引' % host)
            continue
        kind, why = judge_c7(host, meth, types, src_text, empty_allow)
        if kind != 'fake':
            fails.append('C7 假例未检出：%s.%s（verdict=%s/%s）' % (host, meth, kind, why))
    if skipped == len(SELFTEST_C7_FAKE):
        fails.append('C7 假例五枚宿主全缺（src 检索链失效）')
    for host, meth in SELFTEST_C7_EXEMPT:
        kind, why = judge_c7(host, meth, types, src_text, empty_allow)
        if kind == 'fake':
            fails.append('主语判据假命中：外部主语 %s.%s 被判幽灵（主语判据违例）' % (host, meth))
    probe_type = next((t for t in sorted(types) if t.endswith('Dto')), None)
    if probe_type:
        kind, why = judge_c7('DapperDtoSelector', 'MapTo' + probe_type, types, src_text, empty_allow)
        if kind == 'fake':
            fails.append('生成产物对照假命中：MapTo%s（类型实存）被判幽灵' % probe_type)
    else:
        notes.append('生成产物对照自证跳过（索引无 *Dto 类型）')
    if 'ToPagedList' in decls:
        if not observe_arity(SELFTEST_ARITY_CALL, 1, decls, type_ext):
            fails.append('arity 观察列自证失效：签名级形态假例（第五位参类型嫁接）未触发 warn')
    else:
        notes.append('arity 自证前提未成立（src 无 ToPagedList 声明）＝软跳过非失效')
    return fails, notes




def main():
    # ---- 检索链自检（阳性对照）：src 缺失或三符号任一未命中 → exit 2 ----
    n_files, src_text = build_symbol_index()
    if src_text is None or n_files == 0:
        print('[ERROR] 检索链失效：src/ 目录不存在或无 .cs 文件（n=%s）——拒绝输出「零幽灵」' % n_files, file=sys.stderr)
        return 2
    missing = [s for s in POSITIVE_CONTROLS if not symbol_exists(s, src_text)]
    if missing:
        print('[ERROR] 检索链失效：阳性对照符号 %s 在 src/ 零命中（必然存在的符号都找不到=检索坏了）' % missing, file=sys.stderr)
        return 2
    types = build_type_index()
    print('[ok] 阳性对照：%s 三符号在 src/ 全部命中（%d 个 .cs，类型索引 %d 个）' % ('/'.join(POSITIVE_CONTROLS), n_files, len(types)))

    # ---- 内建自证（阳性对照扩面）：任一失效 → exit 2，绝不可输出「零幽灵」 ----
    decls, type_ext = build_decl_index()
    st_fails, st_notes = run_selftests(types, src_text, decls, type_ext)
    for n_ in st_notes:
        print('[info] 自证注记：%s' % n_)
    if st_fails:
        print('[ERROR] 检索链失效：内建自证（阳性对照扩面）失败——拒绝输出「零幽灵」：', file=sys.stderr)
        for f_ in st_fails:
            print('   %s' % f_, file=sys.stderr)
        return 2
    print('[ok] 内建自证：C7 假例 %d 枚必检出已过 ∧ 外部主语 %d 例免判已过 ∧ 生成产物对照免判 ∧ arity 假例 warn（声明索引 %d 名）'
          % (len(SELFTEST_C7_FAKE), len(SELFTEST_C7_EXEMPT), len(decls)))

    if not ZH.is_dir():
        print('[ERROR] 检索链失效：%s 不存在' % ZH, file=sys.stderr)
        return 2

    # ---- 扫描围栏块并抽取 ----
    class_hits = {name: 0 for name, _, _ in PATTERNS}
    fakes = []          # (rel, line, 类别名, 成员名)
    pages = 0
    blocks = 0
    allow = {a[0] for a in ALLOWLIST}
    # 观察计数（C7 免判分解 ∧ arity warn 面）
    c7_total = 0
    c7_exempt_subject = 0   # 外部库一等概念主语
    c7_exempt_host = 0      # 宿主 ∉ src+文档类型索引＝非 NCMapper 主语
    c7_exempt_gen = 0       # 生成产物示意名（类型实存）
    c7_covered = 0          # XxxMapper/MapValidator/SqlDialectResolver 宿主＝交 C1/C2/C4/C5
    arity_warns = []        # (rel, line, method, slot, actual, formal, decl_file)

    for md in sorted(ZH.rglob('*.md')):
        rel = md.relative_to(ZH).as_posix()
        try:
            lines = md.read_text(encoding='utf-8').splitlines()
        except OSError:
            continue
        in_block = False
        block_start = 0
        page_has_block = False
        for i, ln in enumerate(lines):
            if not in_block and FENCE_OPEN.match(ln):
                in_block = True
                block_start = i + 1
                page_has_block = True
                blocks += 1
                continue
            if in_block and FENCE_CLOSE.match(ln):
                in_block = False
                continue
            if not in_block:
                continue
            for pname, pat, grp in PATTERNS:
                for m in pat.finditer(ln):
                    class_hits[pname] += 1
                    name = (m.group(grp) if grp else 'MapTo' + m.group(1))
                    # 主语判据层：C2 宿主命中外部库一等概念词族 ⇒ 不判幽灵（同词两判）
                    if pname[0] == 'C' and pname[1] == '2' and m.group(1) in SUBJECT_EXTERNAL_HOSTS:
                        c7_exempt_subject += 1
                        continue
                    if name in allow:
                        continue
                    gen = generated_name_ok(name, types)
                    if gen is True:
                        continue    # 生成产物示意名且类型真实存在（MapToUserDto 等）
                    if gen is False:
                        fakes.append((rel, i + 1, pname, name, '生成产物类型不存在'))
                        continue
                    if not symbol_exists(name, src_text):
                        fakes.append((rel, i + 1, pname, name, 'src 零命中'))
            # C7 类名.方法名组合存在性（第三形态＝类名实存即放行盲区；主语判据先行）
            for m in C7_CALL.finditer(ln):
                c7_total += 1
                kind, why = judge_c7(m.group(1), m.group(2), types, src_text, allow)
                if kind == 'fake':
                    fakes.append((rel, i + 1, 'C7 类名.方法名组合', m.group(2), why))
                elif kind == 'exempt':
                    if why.startswith('external-subject'):
                        c7_exempt_subject += 1
                    elif why.startswith('host-not'):
                        c7_exempt_host += 1
                    else:
                        c7_exempt_gen += 1
                elif why.startswith('covered'):
                    c7_covered += 1
        if page_has_block:
            pages += 1

    # ---- arity 观察列（类型级；页级围栏重遍历，只 warn 不判红、不影响退出码） ----
    for md in sorted(ZH.rglob('*.md')):
        rel = md.relative_to(ZH).as_posix()
        try:
            lines = md.read_text(encoding='utf-8', errors='replace').splitlines()
        except OSError:
            continue
        for start, btxt in iter_fenced_blocks(lines):
            for w in observe_arity(btxt, start + 1, decls, type_ext):
                arity_warns.append((rel,) + w)

    # ---- 输出 ----
    print('[info] 扫描 %d 页 / %d 个 ```csharp 手抄块' % (pages, blocks))
    for pname, _, _ in PATTERNS:
        print('[info]   %s 抽取 %d 次' % (pname, class_hits[pname]))
    print('[info] 符号总数（含重复）= %d，白名单豁免 = %d' % (sum(class_hits.values()), len(ALLOWLIST)))
    print('[info]   C7 类名.方法名组合 抽取 %d 次（外部主语免判 %d / 索引外宿主免判 %d / 生成产物免判 %d / 交 C1-C5 覆盖 %d）'
          % (c7_total, c7_exempt_subject, c7_exempt_host, c7_exempt_gen, c7_covered))
    for rel, ln_no, nm, slot, actual, formal, dfile in arity_warns:
        print('[WARN_ARITY] docs/zh-CN/%s:%d  %s() 第 %d 位实参 new %s ≠ 声明槽形参 %s（%s）'
              % (rel, ln_no, nm, slot, actual, formal, dfile))
    print('[info] arity 观察列（类型级）warn = %d（只观察不判红；量纲级仿真否证＝残线索）' % len(arity_warns))

    if fakes:
        print('[FAIL] 幽灵 API %d 处：' % len(fakes))
        for rel, lineno, pname, name, why in fakes:
            print('[FAKE_API] docs/zh-CN/%s:%d  %s  (%s, %s)' % (rel, lineno, name, pname, why))
        return 1
    print('[PASS] 零幽灵（阳性对照已过，检索链有效）')
    return 0


if __name__ == '__main__':
    sys.exit(main())
