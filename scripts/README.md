# scripts/ — 本地门禁与诊断脚本

本目录存放仓库的本地门禁脚本与辅助工具，镜像 `.github/workflows/ci.yml` 中可在本机复现的检查项，
是"推送前先在本地把关"的第一道闸。

本文件登记本目录下三脚本护栏（U+FFFD 扫描 / 注释锚点校验 / 文档告警构建）的用法、判定口径与**基线值**。

---

## 1. U+FFFD 扫描（`scan-ufffd.ps1` / `scan-ufffd.sh`）

### 用途

检出文件中的 U+FFFD 替换字符（REPLACEMENT CHARACTER）。该字符既可能是"有意写入的替换字符"，
也可能是编码损坏的残留；本脚本以**字节序列**为唯一判据，使二者不再混淆。
（历史实例：`src/NCMapper.Dapper/DapperDtoSelector.cs` 中 2 行 5 处 U+FFFD 曾随 XML 文档进入 NuGet 包，已修复。）

### 扫描面

| 面 | 路径 | 是否影响退出码 |
|----|------|----------------|
| 声明面（门禁） | `src/**/*.cs`、`src/**/*.resx`、`docs/**/*.md` | 是 |
| 可选面（只报告） | `locales/**/*.xml` | 否（构建副产物写回，非源文件） |
| 排除面 | `src/**/bin/**`、`src/**/obj/**` | — |

> 排除面必要性已实证：`src` 下 `*.cs` 共 259 个，其中 80 个位于 `bin/`、`obj/`（含生成的 `.g.cs`）；
> 排除后参与扫描的声明面文件总数为 328（179 + 2 + 147）。
>
> resx 面取 `src/**/*.resx`（**全 resx 面**），比"仅 `Resources/` 目录"更宽：当前仓内 resx 恰 2 份且都在
> `Resources/` 下，两种口径结果相同；取宽口径是为后续新增 resx 时失败更安全。

### 检测口径

按**原始字节流**匹配 `EF BF BD`（U+FFFD 的完整 UTF-8 编码）。该序列在合法 UTF-8 中不可能跨界出现，
因此字节级匹配精确且与解码器回退行为无关——ps1 用 `[System.IO.File]::ReadAllBytes` 逐字节比对，
sh 用 `awk`（`LC_ALL=C`）字节查找，两侧语义一致。**禁止**用 `ReadAllText + IndexOf` 之类解码后比较：
解码会把非法字节替换为 U+FFFD，反而抹掉"损坏"与"有意写入"的区别。

输出格式：`<相对路径>:<行>:<列>`，列号为行内 1 基字节偏移。

### 用法

```bash
bash scripts/scan-ufffd.sh                          # 声明面，全仓
bash scripts/scan-ufffd.sh --include-optional       # 追加可选面分节（不改变退出码）
bash scripts/scan-ufffd.sh --root <dir>             # 指定仓根（自测用）
pwsh scripts/scan-ufffd.ps1                         # Windows / Linux pwsh 等价实现
pwsh scripts/scan-ufffd.ps1 -IncludeOptional
```

### 退出码

`0` = 声明面零命中；`1` = 声明面有命中。可选面命中不参与退出码。

### 门禁基线值

| 面 | 结果 |
|----|------|
| 声明面 | **0 处命中** |
| resx 面 | 0 |
| 可选面 `locales/**/*.xml` | 0 |

判定口径：`local-ci-check.ps1` 的 scan 阶段 `$ufffdBaseline` 为**空集合**（期望集合与实测命中集合等值：
空即零命中 → PASS、任意命中 → FAIL）。

### 自测

```bash
bash  scripts/scan-ufffd.sh --root scripts/tests/fixtures/ufffd/hit     # 期望命中 1 处，退出码 1
bash  scripts/scan-ufffd.sh --root scripts/tests/fixtures/ufffd/clean   # 期望零命中，退出码 0
pwsh  scripts/scan-ufffd.ps1 -RepoRoot scripts/tests/fixtures/ufffd/hit
pwsh  scripts/scan-ufffd.ps1 -RepoRoot scripts/tests/fixtures/ufffd/clean
```

fixture 目录为"伪仓根"结构（`hit/src`、`clean/src`），因此 `-RepoRoot` / `--root` 可直接指向。

### 编码纪律

`.ps1` 必须为**纯 ASCII（注释/消息一律英文）+ UTF-8 BOM + CRLF**。无 BOM 的 `.ps1` 会被
Windows PowerShell 5.1 按 ANSI/GBK 解析，导致行号错位、字符串字面量被吞，甚至静默不执行。

---

## 2. 注释锚点校验（`check-doc-anchors.py`）

### 用途

校验源码注释中 `文件名:行号` 形态的代码锚点是否仍然有效。锚点漂移（目标文件改名/搬迁、行号越界）
是最高频的文档缺陷族，人工核验成本高且反复回流。本脚本为只读工具，不写任何文件。

### 输入面与锚点形态

- 输入面：扫描根下 `*.cs` 的 `//` 与 `///` 行注释（块注释不扫；仅检查行内 `//` 之后的文本）
- 默认扫描根：`<仓根>/src`；排除 `bin/`、`obj/`、`.git/`
- 锚点正则（白名单）：`([A-Za-z_][\w./-]*\.(?:cs|resx|md)):(\d+(?:/\d+)*)(?:-(\d+))?`
- 多行号形态（`X.cs:617/623/634`）**逐段切分**，各段独立判定；区间形态（`X.cs:617-623`）以上界为锚点行

### 判定两级

| 级别 | 判定 | 是否影响退出码 |
|------|------|----------------|
| **L1 真失配** | 目标文件无法定位，或锚点行（区间上界）超过目标文件总行数 | 是 |
| **L2 软观察** | L1 通过，但锚点行及其向上 N 行（`--soft-window`，默认 5）内无声明行 | 否 |
| ambiguous | basename 命中多个候选且路径后缀不足以区分 | 否（列入软分节） |

L2 保持"观察级"的原因：大量合法锚点本就指向方法体内的语句行，行级声明检测会对它们全部误判。
声明正则使用锚定式词边界 `^[ \t]*(public|internal|protected|private|static|partial|sealed|abstract|readonly|record|struct|class|interface|enum|delegate|void)\b`，
禁用 `.*class` / `.*void` 类子串匹配（会命中 "avoid" 与叙述行）。

### 目标解析顺序（首个命中即用）

1. 相对仓根
2. 相对 `<仓根>/src`
3. 相对被扫描文件所在目录（支撑自持 fixture）
4. **扫描根下 basename 唯一匹配**——本仓多数锚点只写裸文件名（如 `CodeGenerator.Mapping.cs:1677`），
   而目标文件位于兄弟工程目录，仅靠 1~3 会把大量真实存在的文件误报为缺失
5. basename 多候选时，用路径后缀进一步区分（`Internal/Foo.cs` → `*/Internal/Foo.cs`）

五条都不中 → L1 `file-not-found`；多候选且无法区分 → ambiguous（不计失配）。

### 白名单/排除规则

1. **历史锚点**：锚点前带 `原...` 限定（如 `原硬编码于 ExpressionBuilder.cs:617`）的引用一律不计失配，
   单列"历史引用"分节——该族指向已搬走的旧位置，形式与活锚点相同
2. **多行号形态**：逐段切分（见上）
3. **文件缺失 / 行号越界**：L1 真失配

### 用法

```bash
python3 scripts/check-doc-anchors.py                              # 门禁模式，src/ 面
python3 scripts/check-doc-anchors.py --report-only                # 报告模式，恒退 0
python3 scripts/check-doc-anchors.py <dir> --report-only          # 指定扫描根（自测）
python3 scripts/check-doc-anchors.py --soft-window 10             # 调整 L2 回看窗口
```

退出码：L1 > 0 → `1`，否则 `0`；`--report-only` 恒为 `0`。

### 当前结果

| 分类 | 计数 |
|------|------|
| 通过（L1+L2） | 8 |
| L2 软观察 | 30 |
| 历史引用（不计门禁） | 38 |
| ambiguous | 0 |
| **L1 真失配** | **0** |

判定口径：`local-ci-check.ps1` 的 `$anchorsBaseline` = 0（判定为计数等值）。

### 自测

```bash
python3 scripts/check-doc-anchors.py scripts/tests/fixtures/anchors/ --report-only
```

fixture 为**自持样本**：锚点指向 fixture 目录内的 `target_sample.cs`（30 行）与 `cross_target.cs`，
不指向真实 `src/` 文件，因此源树后续变更不会让 fixture 漂移变红。覆盖形态：

| 用例 | 形态 | 期望归类 |
|------|------|----------|
| `case_valid_and_near.cs` | 声明行锚点；方法体语句行锚点（窗口内有声明） | 通过 ×2 |
| `case_soft_far.cs` | 深方法体内语句行（窗口内无声明） | L2 软观察 |
| `case_hard.cs` | 指向不存在文件；指向越界行号 | L1 真失配 ×2 |
| `case_history.cs` | `原硬编码于 ...` 前缀 | 历史引用分节 |
| `case_multiline.cs` | `X.cs:7/11/24` 多行号切分 | 通过 ×2 + L2 ×1 |
| `case_cross_file.cs` | 跨文件锚点；`.txt` 非白名单引用 | 通过 ×1 + 忽略 ×1 |

---

## 3. 文档告警构建（`-p:GenerateDocumentationFile=true`）

### 背景

`src/NCMapper.SourceGen/NCMapper.SourceGen.csproj` 显式 `GenerateDocumentationFile=false`（覆盖
`src/Directory.Build.props` 的全局 `true`），因此 XML 注释类告警（悬空注释、参数文档缺失等）在
默认构建下**完全静默**。要收集它们须单独构建并开启该属性。

### 命令（Linux 副本执行）

```bash
dotnet restore src/NCMapper.SourceGen/NCMapper.SourceGen.csproj
dotnet build src/NCMapper.SourceGen/NCMapper.SourceGen.csproj -c Release --no-incremental \
    -p:GenerateDocumentationFile=true
```

要点：

- **不加 `-f`**：该工程为单框架 `netstandard2.0`，指定 `-f net10.0` 会报 `NETSDK1005`
- **必须带 `--no-incremental`**：增量构建会吞掉警告，产生"零警告"假基线
- 本工程未开 `TreatWarningsAsErrors`；若在开启该属性的环境运行，参数为 `-p:TreatWarningsAsErrors=false`
  （`-p:WarningsAsErrors=false` 不是构建级开关，不具关闭作用）

### 计数口径

以构建日志中 **csproj 后缀行**逐码 grep 计数——同一次构建中每条警告会出现两次字节相同的输出，
直接 `grep -c` 会得到 2 倍值（实测 170 vs 官方汇总行 85）。**去重口径**对账以官方汇总行 `N Warning(s)` 为准。

> **门禁常量口径**：`local-ci-check.ps1` 的 `$docBaseline` 按**原始计数（不去重）**取值——
> 脚本判定段逐行匹配 csproj 后缀行、不做去重，常量须与之一致；本文件 §3 的「基线值」表即该口径。

### 基线值

门禁计数口径（构建日志 csproj 后缀行**原始计数、不去重**；见上「计数口径」节）：

| 码 | 含义 | 计数 |
|----|------|------|
| CS1570 | XML 注释含格式错误的 XML | 8 |
| CS1571 | `param` 标签重复 | 0 |
| CS1572 | `param` 标签无对应参数 | 22 |
| CS1573 | 参数缺少 `param` 标签 | **80** |
| CS1587 | XML 注释未放置在有效语言元素上（**悬空注释，本项核心族**） | 2 |
| **官方汇总行** | `50 Warning(s)` / `0 Error(s)`（去重口径） | **50** |

> **CS1587 = 2（去重口径 1）**：留存项为 `MappingExtractor.GlobalConfig.cs:67`，随该整文件删除处置。

### 判读口径

目标不是"清零"，而是**相对当前基线值零新增**：各码计数应 ≤ 上「基线值」表数值。

---

## 4. 本地门禁挂接

`scripts/local-ci-check.ps1` 的 `scan` 阶段（置于 `build` 之前），串联：

1. `scan-ufffd.ps1` — **基线比对判定**（输出与**当前基线**（= 零命中）一致即 PASS，偏离即 FAIL），
   不复用 `Invoke-Check` 的"非零即失败"语义
2. `check-doc-anchors.py` — 按 L1 结果判定；解释器解析顺序为环境变量 `NCMAPPER_PYTHON`
   → `python3` → `python`；三者皆不可用时**回退 WSL**（`wsl -e bash -lc` + `/mnt/` 路径换算，
   输出仍按 `[L1] count:` 基线判定）——WSL 预检失败（无发行版 / WSL 内无 python3 / 通道拦截）
   才落 `[SKIP]` 并输出 WARN（不计 PASS 也不计 FAIL，不落假红）
3. `check-comment-symbol-refs.ps1` — 测试面注释符号引用门：**退出码硬门**（未解析引用清零才绿；
   HISTORICAL 态行透出为 informational 不判红）；`scanned` 引用数 = 0 时判红
   （vacuous 护栏，防空跑假绿）
4. `check-comment-process-noise.ps1` — 注释/资源文本过程噪声门（§7）：脚本本体为**退出码硬门**，
   挂载腿为**红面基线比对判定**（当前命中 ⊆ 入库红面基线即 PASS，出现基线外新命中即 FAIL；
   基线 = `scripts/tests/fixtures/comment-noise/redface-<开工日>.txt`，随清偿批次单调下降）；
   informational（状态词 / 日期）透出不判红；`scanned` = 0 判红（vacuous 护栏）
5. 文档告警构建为可选（默认不进 `scan`，构建耗时较长，按需显式开启）

scan 阶段任一腿 FAIL 即阶段 FAIL；`[SKIP]` 腿必须在输出中有对应 WARN 登记（静默跳过 = 门禁缺陷）。
用法与判定细节见各脚本头部注释。

### 4.1 Windows 侧运行要求（**重要**）

**本仓所有含中文字面量的 `.ps1` 脚本必须保存为 UTF-8 with BOM。**

**原因**：Windows PowerShell 5.1 在读取**无 BOM** 的脚本时按系统 ANSI 代码页（简体中文环境为 GBK/CP936）解码。含中文的脚本会因此解码错乱，导致**引号失配**，在**解析期**即抛 `MissingArrayIndexExpression` / `UnexpectedToken` 一类错误。

**关键后果**：解析期失败时，**脚本内任何代码都不会执行**——包括放在脚本首部的自检、`try/catch`、版本探测。因此这类问题**无法用脚本内部自检兜住**，只能靠编码纪律预防。

**判据（实测驱动，勿凭"是否含非 ASCII"推定）**：

```powershell
# 逐个脚本实测能否被 PS 5.1 解析；报错集为空即通过
$e = $null
[void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $f).Path, [ref]$null, [ref]$e)
if ($e.Count -gt 0) { "FAIL: $($e.Count)" } else { "OK" }
```

**历史事故**：`local-ci-check.ps1` 与 `compare_perf.ps1` 曾因 BOM 被提交删除（`69d3d28`，2026-10-14）而在 PS 5.1 下完全不可运行（分别报 56 处 / 1 处解析错），门禁本体失效但无任何门禁能检出（**门禁检不了门禁自己**）。2026-10-20 已补回 BOM 并在本文件登记约束。

**编辑工具注意**：部分编辑器/工具保存时会剥离 BOM。改完脚本后请复验首三字节：

```bash
head -c 3 scripts/local-ci-check.ps1 | xxd -p   # 应为 efbbbf
```

**备选运行方式**（已带 BOM 时不必）：若脚本无 BOM 且不便修改，可用 PowerShell 7（`pwsh`）运行——它默认按 UTF-8 解码。本机未安装 `pwsh` 时需先安装。

---

## 5. 相邻工具：PublicApi 基线再生成（`tools/PublicApiDump/`）

`test/NCMapper.Tests/PublicApi.*.approved.txt` 五份快照**不是手工维护**的，由 `tools/PublicApiDump/`
（PublicApiGenerator 11.1.0，口径与 `PublicApiTests.GeneratePublicApi` 同源）再生成。

- 该工程不在 `NCMapper.slnx` / `NCMapper.Lib.slnf` 内，CI 与本目录门禁均不构建，须显式点名；
- 用法、五份基线清单（含各自**末字节形态**与 `--no-eol` 的选择）、SourceGen 的 Roslyn 依赖解析坑，
  见 `tools/PublicApiDump/README.md`；
- 基线变更的**判定门**是 `PublicApiTests` 整文件快照门。注意 `check-api-coverage.sh` 的证据力有限：
  ① 覆盖面仅**核心程序集**（`:33` 的 `approved` 只指向 `PublicApi.NCMapper.approved.txt`，其余四份不参与）；
  ② 比较键是**类型 FQN 集合**（左侧 `:50-71` 由 `awk` 提取类型名并 `sort -u`，右侧 `:73` 起取
  metadata yml 的 `T:NCMapper…` commentId）——对快照内**特性文本**一类变更恒绿。

---

## 6. 离线文档包打包（`build-offline-docs.sh` / `build-offline-docs.bat`）

一个离线包 = 一种语言。每个包独立构建、独立压缩，内含四要素：
`start.bat` + `start.sh`（启动脚本）、`server/`（NativeAOT 单文件静态服务器，免装 .NET）、
`site/`（DocFX 站点，零外链）、`README-离线文档说明.txt`。

### 6.1 参数

命令行两个位置参数，顺序固定：**平台 语言**。两者都可省略。

| 位置 | 参数 | 可选值 | 缺省 |
|---|---|---|---|
| 1 | 平台（rid） | `win-x64` / `linux-x64` / `all` | `win-x64` |
| 2 | 语言 | 单个语言标识 / 逗号分隔多语言 / `all` | `zh-Hans` |

**平台参数**：

- `win-x64` / `linux-x64`：出对应平台的包。
- `all` 在本机 Windows 上**只产 win-x64**（脚本会打印提示）；linux-x64 由 CI 的
  `offline-package.yml`（ubuntu runner）产出——NativeAOT 不支持 Windows 交叉发布 linux-x64。

**语言参数**（三种形态，2026-10-01 扩展）：

| 形态 | 写法 | 含义 |
|---|---|---|
| 单语言 | `zh-Hans`、`en` … | **只构建并打包这一个语言** |
| 多语言 | `en,de`（逗号分隔，**不能有空格**） | 依次构建所列语言，各出一个 zip |
| 全部 | `all` | 构建映射表内**所有已接入语言** |

- 可用语言 = 脚本内 `lang_cfg` 映射表（`build-offline-docs.sh` 约 63-65 行）的键集合；
  传未接入的语言会报 `不支持的语言：xxx` 并列出全部可选值。
- **接入新语言只需在映射表加一行**（端口须唯一），白名单校验与 `all` 的展开自动生效——
  无需改动参数解析代码。详见 §6.7。
- 语言标识**大小写敏感**（`EN` 不认，须写 `en`）；首尾或连续逗号（`,en`、`en,,de`）会报错。

### 6.2 执行方式（三选一，注意别踩坑）

**坑（务必看清）：不要在 cmd / PowerShell 里直接敲 `.sh` 文件。**
Windows 上 `.sh` 没有"执行"关联，只有"用某程序打开"关联——敲 `build-offline-docs.sh win-x64 en`
会被系统拿去"打开"（实测会弹编辑器，或拉起一个 Git Bash 窗口），**脚本根本不会执行**，
表现为敲完命令一行输出都没有、直接回到提示符。**这是最易踩的误区。**

| 终端 | 正确写法 |
|---|---|
| **cmd / PowerShell** | `build-offline-docs.bat win-x64 en` |
| **Git Bash** | `cd /d/OpenCode/ncmapper` 然后 `./scripts/build-offline-docs.sh win-x64 en` |
| **CI** | `bash scripts/build-offline-docs.sh linux-x64 all`（见 `offline-package.yml`） |

- cmd 里用 `.bat` 入口：它会自动定位 Git Bash（顺序：Git for Windows 标准安装位置 →
  WorkBuddy 自带 PortableGit → PATH 兜底，且强制跳过 WSL 的 `System32\bash.exe`），
  再把参数原样转给 `.sh`。
- 若 Git Bash 里报权限不足，可改用 `sh scripts/build-offline-docs.sh win-x64 en` 显式调用。
- `build-offline-docs.bat` **必须是纯 ASCII**，不得写中文注释：cmd.exe 按系统 ANSI 代码页（GBK）
  解析 bat，UTF-8 中文注释行会被当命令执行，刷出一串乱码报错。

### 6.3 各语言打包命令一览

**写法换算**：下表 cmd 列把 `build-offline-docs.bat` 换成 `./scripts/build-offline-docs.sh`，
即为 Git Bash 列写法（Git Bash 里须先 `cd` 到仓库根）。平台参数把 `win-x64` 换成 `linux-x64`
即出 Linux 包（需在 Linux 上跑）。

**（一）单语言打包**——每种语言一条独立命令，互不影响：

| 语言 | 语言标识 | cmd 命令 | 包名 | 默认端口 |
|---|---|---|---|---|
| 中文 | `zh-Hans` | `build-offline-docs.bat win-x64 zh-Hans` | `NCMapper-离线包-中文-v{版本}-{rid}.zip` | 8080 |
| 英文 | `en` | `build-offline-docs.bat win-x64 en` | `NCMapper-Offline-EN-v{版本}-{rid}.zip` | 8081 |

> 当前脚本映射表已接入 `zh-Hans`、`en` 两种语言（端口依次 8080 / 8081）。
> 新增语言按 §6.7 补齐 `docs/<lang>/`、`docfx.<lang>.json`、`locales/<lang>/NCMapper.xml`
> 并在映射表加一行即可出包，无需改脚本参数解析。

**（二）多语言一次打包**——逗号分隔，按所列顺序依次构建：

```cmd
build-offline-docs.bat win-x64 en
```

```bash
# Git Bash（仓库根）
./scripts/build-offline-docs.sh win-x64 en
```

**（三）全部语言**——映射表内所有已接入语言各出一个包：

```cmd
build-offline-docs.bat win-x64 all
```

```bash
./scripts/build-offline-docs.sh win-x64 all
```

> 提示：`all` 会依次跑完所有语言的完整构建（每语言含 metadata → 剥离外链 → docfx build），
> 语言越多耗时越长。只出某几个语言时，用上面的多语言形态更省时。
> `all` 的展开顺序 = 脚本内 `lang_order` 数组的声明顺序。

### 6.4 各语言的构建依赖

每种语言的离线包需要三样输入齐备，缺一即该语言构建失败（不影响其他语言）：

| 输入 | 路径 | 说明 |
|---|---|---|
| 叙事文档 | `docs/<lang>/` | 该语言的 markdown 正文 |
| API 注释 | `locales/<lang>/NCMapper.xml` | 该语言的 API XML 注释 |
| 演示注释侧车 | `docs/i18n/<lang>/`（可选） | 演示代码的中文注释译文（纯数据 JSON），缺省则演示代码保留中文注释 |

另有 `docfx.<lang>.json`（DocFX 配置）需存在。演示注释的机制详见 `docs/i18n/README.md`。

### 6.5 产出物

```
dist/offline/
├─ NCMapper-离线包-中文-v{版本}-win-x64.zip      ← 中文包
├─ NCMapper-Offline-EN-v{版本}-win-x64.zip       ← 英文包
└─ ...（其余语言各一个）
```

包内结构：

```
<包名>/
├─ start.bat / start.sh          启动脚本（双击 / sh start.sh）
├─ server/DocSiteServer.exe      静态服务器（NativeAOT 单文件，约 9.6 MB）
├─ site/                         文档站点（离线全量）
└─ README-离线文档说明.txt
```

**打包时自动剔除的文件**（运行时均不需要，剔除后 `server/` 由 55 MB 降至 9.6 MB）：

| 剔除项 | 体积 | 原因 |
|---|---|---|
| `server/*.pdb`、`server/*.dbf` | 约 45 MB | NativeAOT 调试符号。注意 csproj 的 `StripSymbols=true` **不阻止**独立 pdb 落盘 |
| `site/**/*.map` | 约 3.6 MB | DocFX 模板自带 source map，浏览器仅在开发者工具中拉取 |
| `site/manifest.json` | 88 KB | DocFX 构建期内部清单（零引用），且含构建机本地绝对路径 |
| `site/xrefmap.yml` | — | 构建期交叉引用索引，`xrefService` 已关闭 |

### 6.6 常见故障

| 现象 | 原因 | 处理 |
|---|---|---|
| cmd 里敲 `.sh` 后**无任何输出** | `.sh` 被文件关联"打开"（弹编辑器/拉起 Git Bash），未执行 | 改用 `build-offline-docs.bat` |
| `/bin/bash: D:\...\sh: No such file or directory`（exit 127） | 命中了 WSL 的 `System32\bash.exe`，它不认 Windows 路径 | 当前 bat 已修（自动跳过 WSL）；确认用的是最新 bat |
| 输出中文全为乱码 | cmd 控制台代码页（GBK）与脚本 UTF-8 输出不匹配 | 仅影响日志可读性，不影响结果；在 Git Bash 里跑显示正常 |
| 只有部分语言出包 | 某语言的 `docs/<lang>/`、`locales/<lang>/NCMapper.xml` 或缺配置 | 按 §6.4 补齐该语言输入 |
| `不支持的语言：xxx` | 该语言未接入脚本的映射表 | 见 §6.7（加映射表一行即可） |
| 离线包目录被清空只剩 zip | 脚本末尾 `rm -rf temp/offline` 清临时区 | 正常，zip 即最终交付物 |

### 6.7 新增一种语言的接入清单

以新增一种语言（示例记为 `xx`）为例，需五处同步（白名单由映射表驱动，**接入点由 5 处减为 4 处**）：

1. **叙事文档**：新建 `docs/xx/**`（结构对照 `docs/en-US/`）。
2. **DocFX 配置**：新建 `docfx.xx.json`（对照 `docfx.en.json` 改 `content.files` 路径、`_lang`、页脚等）。
3. **API 注释**：`locales/xx/NCMapper.xml`。
4. **演示注释侧车**（可选）：`docs/i18n/xx/**/*.cs.json`，由 `scripts/gen-i18n-sidecars.ps1 -Languages xx` 生成。
5. **打包脚本接入**（`build-offline-docs.sh`）：
   - **语言映射表**（约 63-65 行，在语言参数段之前）：新增 `lang_cfg` / `lang_dir` / `lang_pkg` /
     `lang_zhname` / `lang_port` 五组映射（**端口须唯一**，避免同机多语言包同时运行时冲突）；
   - **`lang_order` 数组**（约 68 行，映射表下方）：追加语言标识——决定 `all` 是否纳入及展开顺序。
     只加映射表而不加 `lang_order`，则该语言可单独指定、但**不被 `all` 纳入**；
   - `strip-external-xref.sh` 的 API 目录参数：为新语言补 `docs/xx/reference/api` 分支；
   - `prepare-api-src.sh` 的 API 源目录清场列表（约 57 行）：加入新语言的 API 输出目录。

> **白名单无需单独维护**：语言参数解析改为查映射表（`lang_cfg` 的键即合法语言），
> 加一行映射表 → 单指定命令与 `all` 展开同时生效。传未接入语言会报错并列出全部可选值。
>
> 注：当前映射表已接入 `zh-Hans`、`en`。
> 但对应 `docs/<lang>/`、`docfx.<lang>.json`、`locales/<lang>/NCMapper.xml` 与映射表条目
> 尚需按上表补齐后方可出包；补齐后 §6.3（一）表中的命令直接可用。

### 6.8 相关门禁

打包前脚本会依次跑三道门禁，任一 `exit 非 0` 即中止打包：

1. `check-markdown-links.sh` — markdown 链接闭合自检（未闭合链接 DocFX 会静默渲染为纯文本）；
2. `check-demo-coverage.sh` — 演示覆盖对账（告警版，恒 0）；
3. `check-api-demo-coverage.sh` — API 架构图 ↔ 演示覆盖反向对账（告警版，恒 0）。

站点组装完成后另有 `check-doc-external-links.sh` 做零外链审计（**硬门禁**，非 0 即中止）。

---

## 7. 注释过程噪声门（`check-comment-process-noise.ps1`）

### 用途

拦截写进**交付面**的过程信息：任务编号、评审批次标记、内部文档章节引用、日期、状态词。
这类文本随 XML doc 进 IntelliSense、随源码分发进开源仓库、随发射模板进生成码，并且必然过期。
演进信息属 git 历史与内部设计文档，不属源码文本。

### 扫描面

- 默认 `-Include src;samples`（**目录列表**；经 `powershell -File` 调用时须用分号单串形态，`-File` 不传数组）；
- `test/`、`docs/`、`locales/` 不在默认面——清理这些面时须显式扩面；
- 对象 = `*.cs` 的**注释文本**（`///` / 整行 `//` / 行中注释 / 块注释）+ `*.resx` 的 `<value>` 文本与 `<!-- -->` 头注。

### 行中注释提取规则（唯一口径）

单遍状态机（`Get-SignificantSpans`）同时产出**注释 span** 与**字符串/字符字面量 span**：
常规串、逐字串 `@"…"`（含 `""` 转义）、插值串 `$"…"` / `$@"…"`、原始串 `"""…"""`、字符 `'…'` 全部识别并跳过。
判定 = 注释 span 所在行的代码部分非空 → 该 span 为**行中注释**；代码部分为空 → 整行注释（`///` 开头记为 doc）。
因此串内 `//`、URL 的 `://` 不会被读成注释，串也不会被读成注释。
**读数唯一性**：既往三套临时正则对行中面给出 62 / 64 / 55 的互异值（差异全在串内 `//` 的归属边界），
本门给出可复现定值 = 开工 HEAD `4a03f90` 上 src 行中注释宽口径命中 **55 行 / 16 文件**。

### 判据词表（与脚本内模式逐条同集，改动须两处同步）

| 类 | 模式（脚本内形态） | 语义 | 级别 |
|----|--------------------|------|------|
| H01 | `TASK-\d{8}` | 任务书编号 | 硬失败 |
| H02 | `REQ-\d{8}` | 需求登记编号 | 硬失败 |
| H03 | `\bT-\d{2,3}\b` | 任务号（册内子任务） | 硬失败 |
| H04 | `\bF-B\d+\b` | 评审反馈编号 | 硬失败 |
| H05 | `\bW\dE-\d+\b` | 波次-子任务编号 | 硬失败 |
| H06 | `\u6CE2\d-E`（波\d-E） | 中文波次标记 | 硬失败 |
| H07 | `\uFF08\u8BC4\u5BA1 ?S-\d+`（（评审 ?S-） | 评审批次标记（左括号形态） | 硬失败 |
| H08 | `\bS-\d+\b` | 评审条目号 | 硬失败 |
| H09 | `\bQ\d ?(\u5B9A\u6848\|\u5173\u95ED\u58F0\u660E\|closure)` | 裁定/关闭声明 | 硬失败 |
| H10 | `\u65B9\u6848\u4E66 ?\u00A7`（内部文档名 + §） | 内部文档章节引用（具名） | 硬失败 |
| H11 | `introduced in T-` | 英文"由 T- 引入"注记 | 硬失败 |
| H12 | `(\u4EFB\u52A1\u4E66\|\u65B9\u6848\u4E66\|\u65B9\u6848\|\u8BC4\u4F30\u62A5\u544A\|\u8BC4\u4F30\u8BB0\u5F55\|\u767B\u8BB0\u7C3F\|\u7EDF\u7B79\|\u7ECF\u9A8C\u5E93\|ADR-[0-9]+)[^\n]{0,24}\u00A7` | 内部文档章节引用（文档名 + §） | 硬失败 |
| H13 | `\u00A7\s*[0-9]`（裸 §N） | 无主语章节引用 | 硬失败（外部规范引用走白名单） |
| L01 | 非注释行的引号内含 `TASK-\d{8}` | 发射模板 / 特性实参字符串面 | 硬失败 |
| I01 | `\d{4}-\d{2}-\d{2}` | 日期 | informational |
| I02 | `(\u6536\u53E3\|\u6574\u6539\|\u6279\u9605\|\u672C\u4EFB\u52A1\|\u5386\u53F2\u8BB0\u5F55\|\u6B64\u524D\|\u66FE\u901A\u8FC7)`（收口/整改/批阅/本任务/历史记录/此前/曾通过） | 过程状态词 | informational |

表内模式列即脚本内转义序列的逐字登记（`|` 在 markdown 表内转义为 `\|`），
两处任一改动必须同步另一处（验收判据"README 登记的判据词表与脚本内模式逐条一致"）。

### 退出码与护栏

- `0` = 无硬失败（informational 命中允许，透出供巡检）；
- `1` = 存在硬失败，**或** `scanned` 计数 = 0（vacuous-pass 护栏，防空跑假绿）；
- `2` = 参数错（include 路径缺失 / 无文件）。
- 白名单：逐条 `Path + Class + Snippet + Reason`，只用于**外部规范引用**（实测全仓 2 处：
  `src/NCMapper/Shared/TypeCompatibilityCore.cs:193`、`src/NCMapper.SourceGen/TypeHelpers.cs:12`，
  均为 C# 语言规范第 10.3.2 节）；禁批量豁免，新增条目须附理由。

### 挂载语义（`local-ci-check.ps1` scan 第 4 腿）

脚本本体是退出码硬门；挂载腿把**开工日红面全量命中**当作基线做集合判定：
当前命中 ⊆ 基线 → PASS（清偿批次单调下降合法），出现基线外新命中 → FAIL。
理由 = 存量红面在清偿完成前不得让 scan 阶段恒红（否则批次出口门不可达），
与 `scan-ufffd` / `check-doc-anchors` 的基线比对语义同族；
"新门本体 exit 0 且 scanned>0"的收口判据由直跑脚本承载，不由挂载腿承载。
基线文件 = `scripts/tests/fixtures/comment-noise/redface-<开工日>.txt`（UTF-8 无 BOM，
行数 + SHA256 登记在基线台账）；命中清单走 `-OutputFile` 文件通道，不走 stdout
（子进程 stdout 经控制台代码页解码会把中文注释文本变成 mojibake，逐行比对会全量误判）。

### 自测（先证红，经验 #169）

夹具目录 `scripts/tests/fixtures/comment-noise/`（不在默认扫描面，不会被自己判红）：

| 夹具 | 期望 |
|------|------|
| `clean/` | exit 0，scanned>0 |
| `hit/`（含 `TASK-99999999` 探针） | exit 1，精确定位到 `sample_hit.cs:6` |
| `empty/`（无注释文件） | exit 1，vacuous 护栏触发 |
| `classes/`（13 类 + L01 各 ≥1 例） | exit 1，H01-H13 与 L01 全部出现 |
| `informational/`（仅日期 / 状态词） | exit 0，informational 透出 4 条 |

挂载腿验收（植入 → 转红 → 还原 → 复绿）：在 `src/NCMapper/CacheMetrics.cs` 插入
`// probe: TASK-99999999` → `-Phase scan` FAIL 并列出该行；`git checkout --` 还原 → scan 复绿、
既有三门读数不回退。

### 编码纪律

脚本纯 ASCII，中文判据词一律 `\uXXXX` 转义（沿 `check-comment-symbol-refs.ps1:122-126` 先例），
无 BOM——本机执行环境实测为 Windows PowerShell 5.1，直书中文字面量存在失配风险。

---

## 相关文档

- 演示注释多语言机制：`docs/i18n/README.md`
- 离线包 CI：`.github/workflows/offline-package.yml`
- 提交前本地自查清单：`CONTRIBUTING.md`
