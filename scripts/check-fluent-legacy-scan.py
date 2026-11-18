#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check-fluent-legacy-scan.py -- Fluent 断代后旧形态常驻回归门。

定位：
  断代清扫删除的旧面实体与旧形态调用，防止后续开发无意回归（复活/新写）。
  本门为「常驻辅助门」——主判据仍是编译门（旧签名删除后旧调用不可编译）与全量测试门；
  本门额外覆盖编译门不可达的两类：
    ①src 面删除项「复活」（新增签名编译合法、无门拦截）；
    ②test/samples/benchmarks 面字符串/注释内旧形态文本（编译不报错——已见两类实例）。

判据（全部经核对在当前 HEAD 实测标定——命中=疑似回归，需处理或显式豁免）：
  [1] src 面断代项零命中：
      a. 旧签名头组合特征（旧 26 签名清单——返回类型 FluentTypeConfig<TSource, TTarget>
         + 旧字段级方法名）+ FromSource 专项（定义首参须为 Action<＝块版保留；Expression 首参
         ＝旧速记版疑似复活）
      b. 对级旧名 NoAutoInclude 定义复活（FluentTypeConfig 类区内非注释文本命中）
      c. new 压制复活（FluentMappingConfig.cs 内 `public new` 修饰命中）
  [2] test/samples/benchmarks 旧形态调用扫描：
      判据 = 执行期 scan-oldform（首实参 lambda ∧（实参≥2 ∨ 单参族
      {Mask,FromJson,MapTree,NoAutoInclude,Ignore}））逐行解析——命中集须 ⊆ EXEMPT 豁免清单。
      已知局限（与执行期工具一致）：行内完整解析口径，跨行调用与字符串转义形态解析失败即跳过。

已知豁免（EXEMPT——7 处，逐条归因标定；新增命中须先归因再显式豁免，勿直接放宽判据）：
  EF Core ModelBuilder API 假阳性 ×2（假阳性类）+ Dump 标签文档面留置 ×1
  + 负例工程承载 ×2（N9/N10）
  + 新形态 lambda 转换器 + 非恒定实参假阳性 ×2（同族用例）。

用法（仓库根直跑）：python3 scripts/check-fluent-legacy-scan.py
退出码：0 = 全绿；1 = 存在未豁免命中。
"""
import re
import sys
from pathlib import Path

# ---------------------------------------------------------------------------
# [1a] 旧签名头组合特征（清单 25 字段级 + 对级 1；此处覆盖字段级 10 组方法名）
# ---------------------------------------------------------------------------
OLD_SIG_PATTERNS = [
    "FluentTypeConfig<TSource, TTarget> Map<TSrcProp, TTargetProp>",
    "FluentTypeConfig<TSource, TTarget> Ignore<TProp>",
    "FluentTypeConfig<TSource, TTarget> Mask<TMask, TProp>",
    "FluentTypeConfig<TSource, TTarget> Format<TProp>",
    "FluentTypeConfig<TSource, TTarget> Convert<",
    "FluentTypeConfig<TSource, TTarget> ConvertBack<",
    "FluentTypeConfig<TSource, TTarget> FromJson<TProp>",
    "FluentTypeConfig<TSource, TTarget> MapTree<TProp>",
    "FluentTypeConfig<TSource, TTarget> MapWhen",
]
# FromSource 专项：块版（保留）首参为 Action<；旧速记版（已删）首参为 Expression<
FROM_SOURCE_SIG = "FluentTypeConfig<TSource, TTarget> FromSource<TSubSource>("

# [1b] 对级旧名定义复活检测（FluentTypeConfig 类区）
FLUENT_CONFIG_FILE = "src/NCMapper/FluentMappingConfig.cs"
CONFIG_CLASS_ANCHOR = "public class FluentTypeConfig<"
CONFIG_CLASS_END = "public class "

# ---------------------------------------------------------------------------
# [2] 旧形态调用扫描（移植执行期 scan-oldform.ps1 判据，保持逐行解析口径）
# ---------------------------------------------------------------------------
NAMES = ["Map", "Ignore", "Mask", "Format", "Convert", "ConvertBack",
         "FromJson", "MapTree", "MapWhen", "NoAutoInclude", "FromSource"]
SINGLE_OK = {"Mask", "FromJson", "MapTree", "NoAutoInclude", "Ignore"}
NAME_RE = re.compile(r"\.(" + "|".join(NAMES) + r")\s*(<|\()")
LAMBDA_RE = re.compile(r"^(\([^)]*\)|[A-Za-z_@][\w@]*)\s*=>")

SCAN_DIRS = ["test", "samples", "benchmarks"]
EXCLUDE_PARTS = ("/bin/", "/obj/", "/temp/", "/node_modules/")

# 已知豁免（路径，行内容特征，理由）——逐条标定，新增须逐条归因
EXEMPT = [
    ("test/NCMapper.IntegrationTests/FluentProjectionMatrixP2Tests.cs",
     "modelBuilder.Entity<P2Entity>().Ignore(e => e.Address)",
     "EF Core ModelBuilder API（非 Fluent DSL——假阳性类）"),
    ("samples/NCMapper.Samples/05-Orm/InMemoryDbContext.cs",
     "e.Ignore(u => u.Address)",
     "EF Core ModelBuilder API（非 Fluent DSL——假阳性类）"),
    # ── 扩展新增（逐条归因，判据未放宽）──
    ("test/NCMapper.FluentNegativeCases/N9_OldFormConvertWithDestLambda.cs",
     ".Convert(d => d.Tag, N9Conv.ToText)",
     "负例工程指定承载（check-fluent-negative-cases N9——旧形态调用的唯一合法承载位，编译失败为预期）"),
    ("test/NCMapper.FluentNegativeCases/N10_OldFormConvertBackWithDestLambda.cs",
     ".ConvertBack(d => d.Tag, N10Conv.ToText)",
     "负例工程指定承载（check-fluent-negative-cases N10——同上）"),
    ("test/NCMapper.SourceGen.Tests/Fluent/FluentNonStaticArgDispatchTests.cs",
     '.Convert((string v) => "x:" + v, flag)',
     "新形态 lambda 转换器 + 非恒定 overrideIgnore 实参（检测面用例）——扫描启发式"
     "「首实参 lambda ∧ 实参≥2」假阳性类（新形态第二参为 bool，旧形态第二参为方法组，行内解析不可区分）"),
    ("test/NCMapper.SourceGen.Tests/Fluent/FluentNonStaticArgDispatchTests.cs",
     ".Convert((E s) => s.Name, flag)",
     "新形态整源 lambda 转换器 + 非恒定实参（同上假阳性类——纯路径 lambda 亚型用例）"),
]


def read_text(path):
    return path.read_text(encoding="utf-8-sig")


def skip_str(s, i):
    """跳过字符串字面量（移植 scan-oldform Skip-Str：支持 \\ 转义；未闭合返回行尾）。"""
    c = s[i]
    if c not in ('"', "'"):
        return -1
    j = i + 1
    n = len(s)
    while j < n:
        if s[j] == "\\":
            j += 2
            continue
        if s[j] == c:
            return j + 1
        j += 1
    return n


def find_close(s, open_idx):
    """从 '(' 找配对右括号（跳过字符串与嵌套括号）——移植 scan-oldform Find-Close。"""
    depth = 0
    i = open_idx
    n = len(s)
    while i < n:
        c = s[i]
        if c in ('"', "'"):
            j = skip_str(s, i)
            if j < 0:
                return -1
            i = j
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
            if depth == 0 and c == ")":
                return i
            if depth < 0:
                return -1
        i += 1
    return -1


def split_args(inner):
    """顶层逗号切分实参（跳过字符串与嵌套括号）——移植 scan-oldform Split-Args。"""
    res = []
    depth = 0
    start = 0
    i = 0
    n = len(inner)
    while i < n:
        c = inner[i]
        if c in ('"', "'"):
            j = skip_str(inner, i)
            if j < 0:
                j = i + 1
            i = j
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        elif c == "," and depth == 0:
            res.append(inner[start:i].strip())
            start = i + 1
        i += 1
    res.append(inner[start:].strip())
    return res


def scan_legacy_calls(lines):
    """旧形态调用扫描（逐行；返回 [(lineno, name, line_text)]）。"""
    hits = []
    for idx, s in enumerate(lines):
        pos = 0
        while True:
            m = NAME_RE.search(s, pos)
            if not m:
                break
            st = m.start(1) - 1  # '.' 位置
            name = m.group(1)
            p = m.start(2)       # '<' 或 '(' 位置
            if m.group(2) == "<":
                # 跳过泛型实参找 '('
                q = p
                while q < len(s) and s[q] != "(":
                    if s[q] in ";{":
                        break
                    q += 1
                p = q
            if p >= len(s) or s[p] != "(":
                pos = st + len(name) + 1
                continue
            cl = find_close(s, p)
            if cl < 0:
                pos = p + 1
                continue
            args = split_args(s[p + 1:cl])
            first = args[0] if args else ""
            is_lam = LAMBDA_RE.match(first) is not None
            if is_lam and (len(args) >= 2 or name in SINGLE_OK):
                hits.append((idx + 1, name, s.strip()))
            pos = cl + 1
    return hits


def iter_cs_files(root, subdirs):
    for d in subdirs:
        base = root / d
        if not base.is_dir():
            continue
        for p in sorted(base.rglob("*.cs")):
            rel = "/" + p.as_posix()
            if any(part in rel for part in EXCLUDE_PARTS):
                continue
            yield p


def main():
    root = Path(__file__).resolve().parent.parent
    failures = []

    # ---------- [1a] 旧签名头零命中 ----------
    print("== [1] src 面断代项零命中 ==")
    print("-- [1a] 旧签名头组合特征（清单）--")
    sig_hits = 0
    for p in iter_cs_files(root, ["src"]):
        rel = p.relative_to(root).as_posix()
        lines = read_text(p).splitlines()
        for i, line in enumerate(lines, 1):
            for pat in OLD_SIG_PATTERNS:
                if pat in line:
                    sig_hits += 1
                    failures.append(f"[1a] {rel}:{i} 旧签名形态: {pat}")
        # FromSource 专项：出现处首参须为 Action<（块版）；否则疑似旧速记复活
        for i, line in enumerate(lines, 1):
            if FROM_SOURCE_SIG in line:
                joined = "\n".join(lines[i - 1:i + 2])
                tail = joined.split(FROM_SOURCE_SIG, 1)[1]
                mm = re.match(r"\s*([A-Za-z_]\w*)", tail)
                token = mm.group(1) if mm else "<none>"
                if token != "Action":
                    sig_hits += 1
                    failures.append(
                        f"[1a] {rel}:{i} FromSource 首参为 {token}（非块版 Action<）——疑似旧速记复活")
    print(f"   9 模式 + FromSource 专项：命中 {sig_hits} 处"
          if sig_hits else "   9 模式 + FromSource 专项：0 命中")

    # ---------- [1b] 对级旧名定义复活 ----------
    print("-- [1b] 对级 NoAutoInclude 定义复活（FluentTypeConfig 类区非注释文本）--")
    cfg_lines = read_text(root / FLUENT_CONFIG_FILE).splitlines()
    starts = [i for i, l in enumerate(cfg_lines) if l.startswith(CONFIG_CLASS_ANCHOR)]
    if not starts:
        failures.append(f"[1b] {FLUENT_CONFIG_FILE} 未找到 '{CONFIG_CLASS_ANCHOR}' 锚点——文件结构变动，判据失效")
        region_start, region_end = 0, 0
    else:
        region_start = starts[0]
        region_end = next((i for i, l in enumerate(cfg_lines)
                           if i > region_start and l.startswith(CONFIG_CLASS_END)), len(cfg_lines))
    for i in range(region_start + 1, region_end):
        raw = cfg_lines[i]
        if raw.lstrip().startswith("//"):
            continue  # 注释行（XML /// 与 //）不计——类区内合法提及
        if "NoAutoInclude" in raw:
            failures.append(
                f"[1b] {FLUENT_CONFIG_FILE}:{i + 1} FluentTypeConfig 类区非注释文本含 NoAutoInclude"
                f"——疑似对级旧名复活: {raw.strip()[:120]}")
    print(f"   类区行区间 {region_start + 1}~{region_end} 检查完成")

    # ---------- [1c] new 压制复活 ----------
    print("-- [1c] new 压制复活（public new 修饰）--")
    new_hits = [(i + 1, l) for i, l in enumerate(cfg_lines) if "public new" in l]
    for i, l in new_hits:
        failures.append(f"[1c] {FLUENT_CONFIG_FILE}:{i} public new 修饰——疑似压制复活: {l.strip()[:120]}")
    print(f"   public new：命中 {len(new_hits)} 处" if new_hits else "   public new：0 命中")

    # ---------- [2] 旧形态调用扫描 ----------
    print("== [2] 旧形态调用扫描（test/samples/benchmarks）==")
    total = 0
    exempt_cnt = 0
    for p in iter_cs_files(root, SCAN_DIRS):
        rel = p.relative_to(root).as_posix()
        lines = read_text(p).splitlines()
        for lineno, name, text in scan_legacy_calls(lines):
            total += 1
            matched = None
            for epath, etext, reason in EXEMPT:
                if rel == epath and etext in text:
                    matched = (reason,)
                    break
            if matched:
                exempt_cnt += 1
                print(f"   EXEMPT {rel}:{lineno} [{name}] —— {matched[0]}")
            else:
                failures.append(f"[2] {rel}:{lineno} [{name}] 未豁免旧形态调用: {text[:140]}")
    print(f"   命中 {total} 条（豁免 {exempt_cnt} 条——已知豁免表 {len(EXEMPT)} 项）")

    # ---------- 摘要 ----------
    print("----")
    if failures:
        print("FLUENT-LEGACY-SCAN FAIL：")
        for f in failures:
            print("  - " + f)
        sys.exit(1)
    print("FLUENT-LEGACY-SCAN PASS（src 断代项零命中；旧形态调用命中全部豁免）")
    sys.exit(0)


if __name__ == "__main__":
    main()
