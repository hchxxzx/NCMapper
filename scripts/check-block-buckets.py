#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# =====================================================================
# check-block-buckets.py — 手抄代码块全量互斥五桶分桶门
#
# 判据版本：v1.0-confirm（分桶确认「全照建议」口径 A/B/C/D/E/F）
#   固化自探索判据 v1.0-explore。
#   探索期 17 种候选口径的对照与选定依据见探索记录；本脚本为权威实现，
#   判据常量与 samples region 编写规范（R-1~R-7）同步消费。
#
# 口径（定稿）：
#   B2 含类型定义 = 剥注释/字符串后词边界 class|interface|enum|struct|record
#      （record struct 计入，全库 0 实例；成员片段 { get; } 不计入）
#   B3 豁免 = 无类型 ∧（省略号 `...`/`…` 任意位置含注释内 ∨ 伪代码 P1/P2/P4）
#   B1 直转 = 无类型 ∧ 无B3 ∧ 代码位含三键 Mapper.|MapTo*|MapValidator.*
#      （大小写敏感；NCMapper. 全限定名命中按维持口径处理，注记影响 0 块）
#   B5 扩展API = 无类型 ∧ 无B3 ∧ 无三键 ∧ 代码位含 NCMapper 扩展 API 面
#      （设立该桶的目的：把扩展 API 面的块单列，处置归后续批次评估）
#   B4 残余 = 其余（逐块处置登记于 DISPOSITIONS_B4，防线：不得留无登记块）
#   归属顺序 B2→B3→B1→B5→B4 机械保证唯一归属。
#
# 断言（不硬编码编制期读数）：
#   五桶之和 == 当前 HEAD 实测总块数（动态）；读数断言 EXPECT_READINGS；
#   跨桶重复 == 0；双向差集为空；331→333 差异以注记输出（+2 归因 commit 1c43d4d
#   新增的两块手抄示例；全链 27 commit 穷举定位）。
#
# 假门禁防护：扫描面缺失/目录空/提取零块/解析异常 → exit 2；
#   断言失败 → exit 1；全绿 → exit 0。
#
# 用法（仓库根或任意处执行）：
#   python scripts/check-block-buckets.py [--check-list <外部权威清单目录>]
#   --check-list：与外部权威清单 buckets-B{1..5}.json + bucket-final-assign.json
#                 逐块双向差集对账（可选，不依赖仓外文件为默认）。
# =====================================================================
import argparse
import json
import re
import sys
from pathlib import Path

CRITERIA_VERSION = "v1.0-confirm"
REPO_ROOT = Path(__file__).resolve().parent.parent
DOCS_DIR = REPO_ROOT / "docs" / "zh-CN"

# ---------- 判据常量（顶部固化，验收 2） ----------
OPEN_FENCE = re.compile(r"^[ \t]*```csharp")            # 块开：与 check-doc-codeblock-bind.sh 同口径
CLOSE_FENCE = re.compile(r"^[ \t]*```[ \t]*$")          # 块闭
TYPE_PAT = re.compile(r"\b(class|interface|enum|struct|record)\b")   # C15：代码位词边界
ELL = re.compile(r"\.\.\.|\u2026")                      # 省略号：任意位置含注释内（裁 C）
CALL3 = re.compile(r"Mapper\.|\bMapTo[A-Z]\w*|\bMapValidator\b")     # 三键：大小写敏感（裁 D）
EXT = re.compile(                                        # NCMapper 扩展 API 面（B5）
    r"\.AsDto\s*[<(]|\.For\s*<|AddNCMapper|MapTracer|NCMapperOptions|DeepCopyProvider"
    r"|IMapper\b|MapDefaults|\bMapFrom\b|\bMapProperty\b|\bMapIgnore\b|\bMapFormat\b"
    r"|\bMapTree\b|\bMapCtorParam\b|\.Member\s*\(|\.AfterMapping|\.BeforeMapping"
    r"|\.ToPagedList|\.ToDictionary\s*[<(]|\.QueryMultiple|\.QueryUnbuffered|\.ToList\s*<|\.ToLookup"
    r"|\.Compose\s*[<(]|\.DeepCopy\s*[<(]|\.MapList\s*[<(]|\.Validate\s*[<(]|\.PrintReport"
    r"|\.Mask\s*<|\.MaskProvider|MaskProvider|\.Ignore\s*\(\)|\.From\s*\(|NamingStrategy"
    r"|Direction\.To(Dto|Entity)|\bConvert\s*\(\s*typeof|\.AsTracking|\.ToPageList"
)
CJK = re.compile(r"[\u4e00-\u9fff]")                     # 伪代码 P2：剥后代码位仍含 CJK
P4 = re.compile(r"伪代码|生成代码|内部实现|源生成器生成|用户无需调用")  # 伪代码 P4：注释自标

# 读数断言（确认基线；文档内容变化时本断言红＝门禁语义，须重跑分桶确认后更新）
# 基线版本沿革（记录读数为何变动，便于维护者判断某次红是文档变化还是判据变化）：
#   v1.0-confirm＝分桶确认时点（「全照建议」口径 A/B/C/D/E/F，B1=81 全量待转）
#   v1.1-postB1wave1＝第一波直转后（「同意按 B 执行」）：
#     B1 81→28（转 53＝复用 35 + 新增 18，逐块对账全覆盖）；
#     B2 121 / B3 29 / B4 5 / B5 97 分毫不动（B2 与 B5 归后续批次、B3 归豁免面）。
#     交叉验证：bind 脚本 DFM 304→357（=304+53）、裸块 333→280（=333−53），与本断言算术闭合。
#   v1.2-postB10closeout＝全批处置完成后升版（被审字节 7ab8009；
#     读数须由独立复跑另签，自跑不作验收证据）：
#     B3 29→28（幽灵清除删 `dapper-query.md`「EnsureRegistered 整套虚构」块＝豁免页第 26 号条目，
#       两侧同步 −1 故上限仍平衡）；
#     B4 5→4（「转 1」腿兑现＝localization.md 双语块转 DFM，
#       Validation_CultureBilingualTour 巡览区复用既有注册对零新类型对）；
#     B5 97→44（六跳 −53：段1 −6／B5-B 一期 −19／段2 −6／段3 −7／二期一阶 M1 −8／二期二阶 M2 −7，
#       逐跳和==同快照总块数经 13 快照独立复跑坐实）；
#     B1 28／B2 121 全程分毫不动（B2 走「类型段留 md 致拆分转不计减」路线，
#       实测 121 恰＝原 121 经拆转腿数对账）。
#     交叉验证：bind DFM 357→485／裸 280→225；refs 355→483（refs=可解析引用实例计数，与 bind 引用行
#       计数天然差 2＝两处散文行内 `[!code-csharp` 字面提及，起点即有，非去重口径）；
#       region 208→258；净口径下 B5-B 两期 samples 净增 ≈719（258+6+423+32）超出预估
#       [180,280] 区间，实测登记为不停线。
#   v1.3-postFenceFix＝围栏笔误修复后回声（commit 66171ab 同批；升基线）：
#     zh-CN/how-to/orm/freesql-multi-table.md:222 原写作两个反引号 ``csharp（不是合法围栏），
#     该块在 docfx 下不渲染、本脚本亦不计入——即「从未被计数的块」；修成 ```csharp 后
#     块真实可见，总块数 225→226、该类块按判据归 B5（无类型∧无B3∧含扩展API面：
#     [MapProperty(typeof(...), "Address.City")] 命中 MapProperty 判据）。
#     故 B5 44→45，其余四桶分毫不动（B1 28／B2 121／B3 28／B4 4，逐桶复跑坐实）。
#     判据本体一字未改＝只回升一个「修复后新可见」的真实块，不是放宽口径。
BASELINE_VERSION = "v1.3-postFenceFix"
EXPECT_READINGS = {"B1": 28, "B2": 121, "B3": 28, "B4": 4, "B5": 45}
BASELINE_NOTE_CREATED = 331                              # 编制期参考，仅注记不硬编码
BASELINE_NOTE_CONFIRM = 333                              # 分桶确认时点实测
WAVE1_CONVERTED = 53                                       # 第一波直转块数（复用 35＋新增 18）
ATTRIBUTION_NOTE = ("沿革：编制期 %d → 分桶确认时点实测 %d（+2 系 commit 1c43d4d "
                    "新增手抄示例 freesql-advanced.md:309、core-api.md:92，blame 到行 + 全链 27 "
                    "commit 穷举定位）→ 第一波 DFM 直转 −%d（「按 B 执行」口径，"
                    "逐块对账全覆盖）") % (
                        BASELINE_NOTE_CREATED, BASELINE_NOTE_CONFIRM, WAVE1_CONVERTED)

# B4 残余逐块处置登记（防线：不得留无规则、无登记块）
# 锚定方式＝「文件相对路径|块首行前 16 字符」**内容锚定**，不用行号——
#   原因：行号随文档编辑漂移（第一波直转即致 3 块漂移 :31→:25／:132→:115／:72→:65），
#   行号键会静默失配 → 防线假通过；内容锚定只在块本身语义变化时当红，位置移动不影响。
DISPOSITIONS_B4 = {
    "explanation/mechanics/callbacks-collections.md|// Mapper.Map 内部":
        "内部机制示意（简化分发逻辑）→ 豁免人工通读（语义近 B3）",
    "explanation/source-generator.md|// 触发 NCMAP018 的":
        "FreeSql 原生反例演示（代码位无 NCMapper API）→ 不转（外部 ORM 用法对比示意）",
    "how-to/fluent/format.md|MemberConfig<TSo":
        "NCMapper API 签名声明（不可执行）→ 不转（签名文档非示例代码）",
    "reference/localization.md|// 中文环境（默认 / zh-":
        "【「转 1」已兑现】原判「mapper.Map 小写实例调用（大小写敏感落此）→ 可转候选」；"
        "按该裁定转 DFM（OSS d43516b/a66f230）：samples 新写 Validation_CultureBilingualTour 巡览区"
        "复用既有注册对 F9Dto→F9Entity（零新类型对＝教训遵行），本块自 v1.2 起不再落 B4；"
        "键保留作处置沿革",
    "how-to/orm/freesql-multi-table.md|// LeftJoin（左连接）":
        "FreeSql 原生 Join 三段并列示意（无终结符不可独立编译）→ 不转/重构改写",
}
# 判据调整预案（**非字典条目**，故不会静默失配）：以下 2 块当前经 P4 伪代码判据落 **B3**，
#   若将来 P4 词表调整致其落 B4，须在此补登内容锚定键，否则处置登记校验会正确报 [FAIL] 无处置登记：
#     explanation/mechanics/name-matching.md      生成代码 fallback 注释 → 拟豁免人工通读
#     explanation/mechanics/constructors-and-types.md  生成代码枚举 switch 示意 → 拟豁免人工通读
#   沿革：这 2 块曾系探索期「B3↔B1 差集」误判的对象，该误判从未成立
#   （三路实测与独立复核均为差集 0），此处仅留判据敏感性备忘。


def strip_all(text):
    """剥 // 行注释（含 ///）、/* */ 块注释、\"..\" 串（含转义）、@\"..\" 逐字串（含跨行与 \"\" 转义）、
    'c' 字符面量。已知边界：$\"..\" 插值串整体剥（串内表达式含类型关键字会漏判——全库实测 0 实例）。"""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            i += 2
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                i += 1
            i += 2
            continue
        if c == "@" and i + 1 < n and text[i + 1] == '"':
            i += 2
            while i < n:
                if text[i] == '"' and i + 1 < n and text[i + 1] == '"':
                    i += 2
                    continue
                if text[i] == '"':
                    i += 1
                    break
                i += 1
            continue
        if c == '"':
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == '"':
                    i += 1
                    break
                if text[i] == "\n":
                    break
                i += 1
            continue
        if c == "'":
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == "'":
                    i += 1
                    break
                if text[i] == "\n":
                    break
                i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def extract_blocks():
    """docs/zh-CN/*.md 全量 ```csharp 围栏块；返回 [(key, 首行摘要, 原文, 代码位)]。"""
    if not DOCS_DIR.is_dir():
        print(f"[FATAL] 扫描面缺失：{DOCS_DIR} 不存在（假门禁防护：扫描面不可用即拒绝出绿）", file=sys.stderr)
        sys.exit(2)
    files = sorted(DOCS_DIR.rglob("*.md"))
    if not files:
        print(f"[FATAL] 扫描面为空：{DOCS_DIR} 下无 .md（假门禁防护：扫描面不可用即拒绝出绿）", file=sys.stderr)
        sys.exit(2)
    blocks = []
    for path in files:
        rel = path.relative_to(DOCS_DIR).as_posix()
        try:
            lines = path.read_text(encoding="utf-8-sig").splitlines()
        except (OSError, UnicodeDecodeError) as e:
            print(f"[FATAL] 解析异常：{path}: {e}", file=sys.stderr)
            sys.exit(2)
        i = 0
        while i < len(lines):
            if OPEN_FENCE.match(lines[i]):
                start = i + 1
                j, body = i + 1, []
                while j < len(lines) and not CLOSE_FENCE.match(lines[j]) and not OPEN_FENCE.match(lines[j]):
                    body.append(lines[j])
                    j += 1
                if j >= len(lines) and not CLOSE_FENCE.match(lines[-1]):
                    print(f"[FATAL] 未闭合围栏：{rel}:{start}", file=sys.stderr)
                    sys.exit(2)
                raw = "\n".join(body)
                first = next((l.strip() for l in body if l.strip()), "")
                blocks.append((f"{rel}:{start}", first[:70], raw, strip_all(raw)))
                i = j + 1
                continue
            i += 1
    if not blocks:
        print("[FATAL] 提取零块（围栏开标记 0 处=假门禁，请检查扫描范围）", file=sys.stderr)
        sys.exit(2)
    return blocks


def classify(raw, code):
    """归属顺序 B2→B3→B1→B5→B4（机械唯一归属）。返回 (桶, 依据)。"""
    if TYPE_PAT.search(code):
        return "B2", "type"
    if ELL.search(raw):
        return "B3", "ellipsis"
    if (not code.strip()) or CJK.search(code) or P4.search(raw):
        return "B3", "pseudo"
    if CALL3.search(code):
        return "B1", "call3"
    if EXT.search(code):
        return "B5", "extAPI"
    return "B4", "residual"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check-list", help="外部权威清单目录（逐块双向差集对账）")
    args = ap.parse_args()

    print(f"=== 手抄块五桶分桶（docs/zh-CN）===")
    print(f"判据版本：{CRITERIA_VERSION}（分桶确认「全照建议」口径 A/B/C/D/E/F）")

    blocks = extract_blocks()
    total = len(blocks)
    buckets, why = {}, {}
    for key, first, raw, code in blocks:
        b, w = classify(raw, code)
        if key in buckets:
            print(f"[FATAL] 同键重复（提取器异常）：{key}", file=sys.stderr)
            sys.exit(1)
        buckets[key] = b
        why[key] = (b, w, first)
    counts = {b: 0 for b in ("B1", "B2", "B3", "B4", "B5")}
    for b in buckets.values():
        counts[b] += 1

    ok = True
    if sum(counts.values()) != total:                      # 裁 A：动态和断言
        print(f"[FAIL] 五桶之和 {sum(counts.values())} != 当前 HEAD 实测总块数 {total}")
        ok = False
    for b, exp in EXPECT_READINGS.items():                 # 读数断言（确认基线）
        flag = "OK" if counts[b] == exp else "FAIL"
        if counts[b] != exp:
            ok = False
        print(f"[{flag}] {b} = {counts[b]}（确认基线 {exp}）")
    print(f"[info] 总块数（当前 HEAD 实测）= {total}；{ATTRIBUTION_NOTE}")
    print(f"[info] 读数基线版本 = {BASELINE_VERSION}（判据版本 {CRITERIA_VERSION} 未变）")

    for key, (b, w, first) in sorted(why.items()):         # 逐块清单（交付物 2 输出形态）
        print(f"{b}|{w:12s}|{key}|{first}")

    if counts["B4"]:                                       # B4 处置登记（无登记即判红）
        print("=== B4 残余逐块处置（处置登记防线，内容锚定）===")
        for key, (b, _w, _f) in sorted(why.items()):
            if b != "B4":
                continue
            anchor = key.rsplit(":", 1)[0] + "|" + _f[:16]   # 与 DISPOSITIONS_B4 同锚定式（弃行号）
            d = DISPOSITIONS_B4.get(anchor)
            print(f"  {anchor} -> {d if d else '[FAIL] 无处置登记'}")
            if not d:
                ok = False

    if args.check_list:                                    # 与探索权威清单对账
        ld = Path(args.check_list)
        try:
            ref = {}
            for f in ld.glob("buckets-B*.json"):
                for k in json.loads(f.read_text(encoding="utf-8")):
                    ref[k] = f.stem.split("-")[-1].upper()
            assign = json.loads((ld / "bucket-final-assign.json").read_text(encoding="utf-8"))
            for k, v in assign.items():
                if ref.get(k, v) != v:
                    print(f"[FAIL] 清单内部不一致：{k} {ref.get(k)} vs {v}")
                    ok = False
                ref[k] = v
            only_script = sorted(set(buckets) - set(ref))
            only_ref = sorted(set(ref) - set(buckets))
            mismatch = sorted(k for k in set(buckets) & set(ref) if buckets[k] != ref[k])
            if only_script or only_ref or mismatch:
                print(f"[FAIL] 双向差集非空：脚本独有={only_script[:5]}({len(only_script)}) "
                      f"清单独有={only_ref[:5]}({len(only_ref)}) 桶不一致={mismatch[:5]}({len(mismatch)})")
                ok = False
            else:
                print(f"[OK] 与探索权威清单逐块双向差集为空（{len(ref)} 键，桶归属全一致）")
        except (OSError, json.JSONDecodeError) as e:
            print(f"[FATAL] 对账清单读取异常：{e}", file=sys.stderr)
            sys.exit(2)

    if not ok:
        print("结果：分桶断言失败")
        sys.exit(1)
    print(f"结果：分桶通过（判据版本 {CRITERIA_VERSION}；五桶之和 == 实测总块数 {total}，每块唯一归属）")
    sys.exit(0)


if __name__ == "__main__":
    main()
