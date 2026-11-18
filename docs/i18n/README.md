# 演示代码注释多语言机制（注释侧车 + 构建期注入）

> 2026-09-30 落地（en）。机制本身支持任意语言（`docs/i18n/<lang>/`，每语言一份侧车）。
> 解决：各语言离线文档站通过 `[!code-csharp]` 嵌入的 `samples/` 演示代码中，中文注释在
> 非中文站残留的问题。代码只有一份（中文注释为权威版），各语言的注释译文以纯数据侧车
> 存放，构建期确定性注入。

## 1. 背景与方案取舍

EN 站 md 中的 `[!code-csharp[标题](~/samples/.../X.cs#region)]` 在 **docfx 构建时**读取文件
并内联进 HTML（烤死，非运行时加载）。嵌入引用不限于 docfx.json 的 `resource` 段——只要 md
引用了仓库根下任意路径即可。

**被否方案：**

| 方案 | 否决原因 |
|---|---|
| 每语言一份完整 .cs 拷贝 | N 语言 × 31 文件维护爆炸；代码逻辑改动要同步 N 份，必然漂移 |
| 构建期 LLM 实时翻译 | 非确定性（同源码两次构建结果不同）；构建需联网，违反 G2 零网络门禁 |
| docfx 多 content 树 | `[!code-csharp]` 路径写在 md 里，动 md = 每语言一份 md，回到拷贝方案 |

**采纳方案（注释侧车 + 构建期注入）：**

```
samples/.../X.cs            唯一代码源（中文注释权威，#region 锚点不动）
docs/i18n/<lang>/<镜像路径>.cs.json   纯注释数据（每语言一份小 JSON）
        │
        ▼  构建期（en 站构建前）
scripts/inject-demo-comments.ps1    校验 + 只替换注释跨度 → temp/en-demo-overlay/<镜像路径>
        │
        ▼
scripts/prepare-en-site-sources.sh swap-on    备份原文件 → 覆盖 samples/ 原路径
        │
        ▼
docfx build（全量）                    [!code-csharp] 读到英文注释版
        │
        ▼
prepare-en-site-sources.sh swap-off  还原中文原版（含 trap EXIT 兜底）
```

关键性质：注入器**只动注释跨度**，代码流（去注释后的字节流）恒等为结构保证；
`#region` 是预处理指令不是注释，不受影响；构建后源码树无残留。

## 2. 侧车格式（docs/i18n/<lang>/samples/.../X.cs.json）

```json
{
  "source": "samples/NCMapper.Samples/01-Fluent/FluentModels.cs",
  "language": "en",
  "generatedFrom": "<源文件生成侧车时的 sha256（小写 hex）>",
  "comments": [
    { "i": 0, "zh": "文件头注释原文（去空白）", "en": "File header comment" },
    { "i": 2, "zh": "链式声明写在...", "en": "Fluent declarations..." }
  ]
}
```

- `i` = **全局注释序号**（0 起，含未翻译的注释）。用序号定位而不是文本定位：同一注释文本
  在文件内可能出现多次，序号是稳定锚。
- `zh` = 该序号注释原文的精确文本（去首尾空白）。**这是陈旧守卫的比对基准**：源码注释
  一旦改动，zh 失配 → 构建失败，提示重新生成侧车。
- **译文字段名 = 语言代码**：en 侧车用 `en` 字段，ja 侧车用 `ja`，…（每语言目录内字段名
  与目录一致）。注入器按 `-Language` 动态取该字段；缺失/空译文 → exit 2 构建失败（不静默回退）。
- `generatedFrom` = 生成时源文件 sha256，供生成器跳过逻辑与人工排查用。
- 文件路径镜像源相对路径，后缀 `.cs` → `.cs.json`。

## 3. 注入器契约（scripts/inject-demo-comments.ps1）

参数：`-Language <lang> -SidecarDir docs/i18n -OutputDir <临时目录>`。退出码 0=成功，2=侧车
陈旧/内部错误（构建应失败）。多语言：`-Language` 既选侧车目录 `docs/i18n/<lang>`，也决定
取哪条译文字段（`comments[].<lang>`）——en 侧车跑 `-Language en`，ja 侧车跑 `-Language ja`，
同源码同序号，字段各取各的。

1. **注释扫描**：线性扫全文件，识别 `//` 行注释与 `/* */` 块注释；**跳过字符串/字符字面量**
   （字面量内的 `//` 不算注释，如 URL）。
   ⚠ 字面量跳过循环**必须按配对引号终止**——漏写终止条件会把首个字符串之后的整个文件吞掉，
   注释清单静默残缺（本项目实测事故：FluentModels 103 条中文注释只识别 7 条）。
2. **陈旧守卫**：每条侧车 `zh` 必须与当前源码第 `i` 号注释文本精确一致（大小写敏感），
   `i` 不得越界。任一失败 → 报错退出 2。
3. **替换**（按 start 降序处理保持偏移有效）：
   - 行注释：保留原斜杠数（`//` / `///`）与注释段首空白，只换文本；译文必须单行。
   - 块注释：保留 `/* */` 间的前导/尾随空白。
4. **代码流自检**：输出与源码各自"去全部注释"后比对，不等 → 内部错误退出 2。
   （双保险：即使替换逻辑有 bug，也保证代码字节不被改动。）

**硬约束：注入器保持纯 ASCII**（无 BOM 依赖）。Windows PowerShell 5.1 把无 BOM 的
.ps1 按 GBK 解析，中文注释行会被与**后续代码行合并**（实测：`$full = Join-Path ...` 整行
被上一行中文注释吞掉 → 变量恒 null → 产物写到错误位置还打印成功消息）。临时生成脚本同理。

## 4. 构建集成

`scripts/build-offline-docs.sh`（win-x64 all 流程中）：

- 某语言站构建前：`prepare-en-site-sources.sh swap-on [lang]`（lang 缺省 en，2026-10-01 起
  按语言参数化；en 站调用方式不变）
  - 注入器跑 `docs/i18n/<lang>` 全量侧车 → `temp/<lang>-demo-overlay/`（镜像路径）
  - 逐个 `cp` 覆盖 `samples/` 原路径，原文件备份 `temp/_orig_<lang>_<basename>`
    （**备份命名空间按语言隔离**，各语言构建互不干扰）
  - `trap swap-off EXIT` 兜底
- `docfx build`（**构建前必须 `rm -rf` 站点输出目录，强制全量**——见 §8.1）
- 构建后：`swap-off [lang]`（按备份名去 `_orig_<lang>_` 前缀，在 `temp/<lang>-demo-overlay`
  镜像里 `find -name` 定位还原；基名必须全仓唯一，当前 31 个 .cs 满足）

`docs/en-US/site-assets/samples/*/README.md`：samples 目录内的 README 是 docfx resource，
EN 包内用 `site-assets` 下的英文版覆盖（构建后 raw 拷贝，不走注入器）。
⚠ **该目录必须留在 `docfx.en.json` 的 `content.exclude` 内**：它是 build 后的 raw 覆盖素材，
本身不参与内容构建；若被 `docs/en-US/**/*.md` 通配扫进，其内按"最终落点"书写的相对链接会
成批报 `InvalidFileLink`（2026-10-01 实测 16 条，见 §8.22）。

**链接告警门禁（2026-10-01 新增）**：各语言站构建均须确保 `docfx build docfx.<lang>.json`
零链接类告警。目标形态 = 给每个站都加 `--warningsAsErrors`；当前仅 zh 站有（en/ja 无→静默
带病，见 §8.21）。**新增语言站时必须同步加该开关**，否则新站的链接缺陷不会阻断 CI。

CI（.github/workflows/docs.yml）调用**同一套 prepare 脚本**，行为与本地一致。

## 5. 如何添加新语言（操作手册）

以新语言 `<lang>`（如 ja）为例：

1. **生成侧车**：`pwsh scripts/gen-i18n-sidecars.ps1 -Languages <lang>[,<lang>…]`
   （2026-10-01 由 `temp/gen-sidecars.ps1` 转正，多语言参数化；temp 版留档不再维护）。
   - **自动发现目标**：扫 `docs/en-US/**/*.md` 中所有
     `code-csharp\[[^\]]*\]\(~/samples/([^)#\s]+)` 引用，去重 → 嵌入 .cs 全集（31 个）。
     ⚠ 不要以 docfx.json resource 段为准（覆盖不全，本项目实测事故源头）。
     ⚠ 捕获组不含 `samples/` 前缀，拼接时补回。
     ⚠ **该正则的 `[^\]]*` 遇标题内未转义 `]` 会提前截断 → 漏发现该 .cs**（与 §8.20 同源
     缺陷）。2026-10-01 已将全部 `[!code-csharp]` 标题内方括号转义，当前无此风险；**新写
     md 时若不转义，本发现机制会静默漏收**。
     （多语言共用同一嵌入集——各语言站 md 引用同一批 .cs，发现一次即可。）
   - 每文件：扫描注释 → 提取含 CJK（源语言特征）的条目 → **分块** LLM 翻译（块 ≤25 条；
     严格 JSON 数组；计数/零源语言/单行校验**放在重试环内**，3 次失败 → 逐条兜底重译）。
   - **校验门控按目标语言脚本区分**（2026-10-01 教训，见 §8.13）：非 CJK 脚本语言（含 ko）
     查"零 CJK"；**ja 禁用 CJK 检查**（汉字合法），改查"译文 ≠ 源文逐字节"防直通复制。
     系统提示同步按语言措辞（ja 明示"汉字正常、必须真译、禁止复制中文"）。
   - LLM 响应 JSON 解析：CJK 响应大量 `\uXXXX` 转义 → 必须 `ConvertFrom-Json` 解析；
     模型偶发内层引号不转义（`<param name="x">`）→ 宽松兜底取首末引号间内容（见 §8.16）。
   - 写 `docs/i18n/<lang>/<镜像路径>.cs.json`（含 generatedFrom sha256），写后 Test-Path 验盘。
   - 生成器自带注入器自校验（每语言跑一遍 injector 到 `temp/<lang>-demo-overlay`）。
   - **多语言并行**：每语言一个独立进程/作业（进程内串行）；实测 11 语并发共享
     一个 API key 无压垮，单语 31 文件（1227 条注释、约 49 块）实测约 45-60 分钟，
     总墙钟时间 ≈ 最慢单语。长批处理用 DSH 受管后台作业（`run_in_background`），
     不要用游离 `Start-Process`（会被会话回收，见 §8.18）；多语言合并跑时
     `-Languages` 用**重复标志**传（`-Languages de -Languages es …`）——逗号串在作业
     包装下是单个 argv token，`[string[]]` 参数不会按逗号拆分。
2. **prepare 脚本**：已按语言参数化（`swap-on/swap-off [lang]`，缺省 en；备份命名空间
   `temp/_orig_<lang>_*`，注入产物 `temp/<lang>-demo-overlay`），新语言零改动直接用。
3. **构建脚本**：新语言站构建流程套同一段 swap-on → 全量 build → swap-off。
4. **docfx.<lang>.json**：.cs 无需进 resource 段（md 引用即可嵌入）；仅需 README 类 raw 资产
   （当前仅 EN 有 site-assets，其余语言暂无 raw 资产）。
   **配置文件必须放在仓库根目录**（2026-10-01 实测）：DocFX 的 `~` 链接基准绑定在
   `docfx.json` 所在目录，且配置目录最多只能比仓库根深一层（再深则构建期抛
   `Relative to path has too many '../'` 直接失败）——因此 `docfx.<lang>.json` 是本套
   多语言站点里**唯一无法下移**的文件，13 语言即 13 个配置文件并列于根目录，这是硬下限。
5. **navbar 三件套的分工与落点**（2026-10-01 收拢，与 §8 实测对应）：
   - **`docfx.<lang>.json`** → 仓库根（原因见上条）。
   - **`docs/toc-<lang>.md`**（站点 navbar，由 `"toc"` 字段显式声明）→ 放 `docs/` 下。
     DocFX 解析 toc 内链接时的基准是 **toc 文件自身所在目录**，所以在 `docs/` 下的 toc
     里写 `<lang>/path.md`（如 `zh-CN/getting-started/overview.md`），**不要写 `docs/<lang>/`**。
     这与翻译脚本从 `docs/<lang>/toc.md` 产出的相对写法一致，翻译产物可直接使用。
   - **`docs/index-<lang>.html`**（站点根跳转页）→ 放 `docs/` 下。页面内链接按
     **「站点根」视角**书写（`docs/<lang>/index.html`），因为打包脚本会把它从
     `<site>/docs/` 搬移到站点根 `<site>/index.html`（见下条）。zh 站源文件名 `index.html`，
     en/ja 站 `index-<lang>.html`。
6. **离线包接线**（`scripts/build-offline-docs.sh`，2026-10-01 ja 实测事故，见 §8.19）：
   - 语言表加一行：`lang_cfg[<lang>]="docfx.<lang>.json"` + `lang_dir/lang_pkg/lang_zhname/lang_port`
     （**端口必须唯一**：zh 8080 / en 8081 / ja 8082 / …）+ `lang_order` 追加。
   - `docfx.<lang>.json`（套 docfx.en.json 口径：dll + `locales/<lang>/NCMapper.xml` metadata、
     xrefService 关闭、`_lang` 对应 BCP-47）+ `docs/toc-<lang>.md` 站点 navbar +
     `docs/index-<lang>.html` 根跳转页。
   - **站点根跳转页搬移**：DocFX 按源路径把跳转页拷成 `<site>/docs/index.html`（或
     `<site>/docs/index-<lang>.html`），打包脚本里的搬移循环会把它移到 `<site>/index.html`
     （离线包默认文件要求）。**新增语言时若文件名不是上述三种之一，必须同步扩这条搬移清单**，
     否则离线包双击打开将没有默认页。
   - **包说明 README 按包目标语言生成（文件名+内容）**：README 生成段是**逐语言分支**
     （en 全英文 / ja 全日文 / else 中文）——新语言必须加自己的分支，否则落入 else 中文分支
     （ja 包曾装出 `README-离线文档说明.txt`，2026-10-01 事故）。同时把新包端口补进
     其他包 README 的端口说明行。
   - `.gitignore` 加 `docs/<lang>/reference/api/`（metadata 生成物，对齐 zh/en 口径）——
     漏加则该目录以 untracked 混进工作树。
   - **安装示例一律不写版本号**（2026-10-01 定案）：md 里的
     `<PackageReference Include="NCMapper[.Xxx]" />` **不得带 `Version=` 属性**，
     也不要用固定版本号。理由：这类示例是给读者照抄的，写死版本号等于把读者锁在旧版本上；
     且 15+ 处分散在多个语言目录，每次发版都要全量同步、漏一处就语言间不一致。
     `<PackageReference>` 不带 `Version` 时 NuGet 解析最新稳定版，写 `<PackageReference
     Include="NCMapper" />` 即可；同理 `dotnet add package NCMapper`（不带 `--version`）
     也是正确写法。**翻译新语言时不要从旧译文继承版本号**——旧四语（zh/en/ja/de）里
     22 处硬编码版本号已于 2026-10-01 全量清除，新语言不得重新引入。
7. **验证**（§7 清单），重点核对：新用户视角的 2-3 个典型页面 + 全站源语言扫描
   （**ja 站不适用 CJK 扫描**，见 §7.2/§8.13）+ **链接类告警清零**（§7.8：新站首跑
   `docfx build docfx.<lang>.json` 必看告警——翻译站高发 `InvalidBookmark` 锚点残留与
   跨语言链接未登记，见 §8.23）+ **`docfx.<lang>.json` 的 `exclude` 与 `resource` 对齐**
   （素材目录入 exclude；跨语言首页入 resource）。

## 6. 源码注释改动后的维护

- 改中文注释（权威源）→ 下次构建注入器陈旧守卫失败（zh 失配/越界）→ 报错指明文件与序号。
- 修复 = 重跑该语言侧车生成器（生成器的 hash 跳过逻辑见 §8.8 的陷阱）。
- 新增被 md 引用的 .cs → 自动发现机制天然覆盖，只需重跑生成器。

## 7. 验证清单（每次出包必跑）

1. 注入器自校验：31 文件全 `injected OK`（构建日志可见）。
2. **全站代码块源语言扫描**（非中文站）：解包后对 site 下所有 HTML 的 `<pre>` 块：
   去标签 → HTML 实体反转义（`&quot; &lt; &gt;` 先，`&amp;` 最后）→ 剥离字符串/字符字面量
   （引号配对跳过，反斜杠转义）→ 剩余源语言字符应为 0。
   **验收口径：注释零源语言；字符串字面量内的演示数据源语言字符允许保留**（张三丰、京A12345、
   脱敏输出示例等——它们是功能演示数据，不是语言）。
   **门控按目标脚本区分（§8.13）：非 CJK 脚本站查"零 CJK"；ja 站汉字合法，CJK 扫描不适用**
   （改以侧车生成期"非直通"门控 + 页面抽查为准）。
3. 抽查用户实际看过的页面（逐字核对关键代码块）。
4. README 链接目标 200；包内无 CHANGELOG/CONTRIBUTING/xrefmap。
5. 中文包不受影响（嵌入式代码注释保持中文，CJK 计数 > 0 即可）。
6. swap-off 后 `git status` 干净（samples/ 无残留改动、无 temp/_orig_* 泄漏进包）。
7. **包级资产按包目标语言**（2026-10-01 ja 事故后新增）：解包后核对包根 README
   的文件名与内容均为包目标语言（零源语言/零其他语言）；`git check-ignore docs/<lang>/reference/api/`
   命中（生成物已忽略）。start.bat/start.sh 提示文案口径：与 EN 包一致保持中文（仅语言名本地化）。
8. **各语言站链接类告警清零**（2026-10-01 新增）：对每个语言站跑完整流程
   （`prepare-api-src <lang>` → `docfx metadata` → `docfx build`），**逐站**确认
   `InvalidFileLink` / `InvalidBookmark` 等链接类告警为 0。
   - **必须跑完整流程**：只跑 `build` 不跑 `metadata` 会产生假阳性——`reference/index.md`
     引 `api/NCMapper.yml`、`docs/<lang>/toc.md` 缺 api toc 都会误报（完整流程下消失）。
   - 统计时**排除打包期副本**（如 `site-assets/`）的告警。
   - 检查**全部告警类型**，不只 `InvalidFileLink`（`InvalidBookmark` 同样阻塞
     `--warningsAsErrors`，见 §8.23）。
   - 可用 `~/.workbuddy/skills/docfx-link-forensics/` 的 `scripts/verify_sites.sh` 一键跑多站。
9. **安装示例零版本号**（2026-10-01 新增）：全库扫描
   `Include="NCMapper` 与 `Version=` 同现的行，应得 **0 处**（口径见 §5.6 末条）。
   命令：`grep -rn 'Include="NCMapper.*Version=' docs/`。
   新增语言 / 新增 how-to 篇目时同样适用——写示例不要带版本号。

## 8. 踩坑记录（本项目实测）

1. **docfx 增量构建对"外部输入"失明**：`[!code-csharp]` 引用的 .cs 不在 docfx 输入跟踪内，
   md 未改动时增量构建跳过重渲染，换入的英文注释根本不进 HTML（只有 resource 段声明过的
   2 个文件因输入变更被连带重渲染，制造了"机制有效"的假象）。→ 构建前 `rm -rf` 输出目录。
2. **嵌入发现范围**：以 docfx.json `resource` 为清单 → 只覆盖 2/31。→ 以 md 引用正则自动发现。
3. **注释扫描器字面量跳过无终止条件** → 从首个字符串起吞掉整个文件，注释清单静默残缺，
   陈旧守卫全部通过（残缺但一致），注入"成功"。→ 引号配对终止 + 生成后人工核对条目量级。
4. **PS 5.1 非 ASCII .ps1 误解析**（无 BOM 按 GBK）：中文注释行与下一行代码合并 → 赋值语句
   消失、变量恒 null、产物写错位置但打印成功。→ 仓库内 .ps1 一律纯 ASCII；临时脚本若含中文
   必须带 BOM（且注意部分编辑工具会剥 BOM——最终回归纯 ASCII 最稳）。
5. **PS `@() + scalar` 坍缩**：`$targets = @(); $targets += 'x'` 第一次即变字符串，后续 `+=`
   全部变字符串拼接（31 个路径拼成一个巨型字符串）。→ 用 `List[string].Add()`。
6. **PS 调用语法陷阱**：`& $script args...` 会把尾随 token 全当脚本参数（条件恒真）；
   双引号串内 `$var:` 是驱动限定变量（用 `${var}` 或改措辞）；host 单行层会吃掉内联正则的
   反斜杠 → 逻辑一律走 `-File 脚本.ps1`，单行只做编排。
7. **LLM 大批量丢条目**：109 条一批连续 3 次都返回 108（近似行被去重）；个别术语反复残留
   （"闭环"）→ 分块 ≤25 + 系统提示明确"零源语言字符，术语也要译" + 校验在重试环内 + 逐条兜底。
8. **"hash 匹配"≠"侧车完整"**：生成器用 `generatedFrom == 当前源 sha256` 跳过已生成文件，
   但旧算法生成的残缺侧车 hash 也匹配 → 被当"最新"跳过。→ **扫描器/算法变更后必须全量重生成**
   （删目录），不能依赖跳过逻辑。
9. **HTML 校验的转义顺序**：`&quot;` 不反转义就剥字面量 → 演示数据被误判为注释残留；
   双重转义 `&amp;quot;` 需 `&amp;` 最后处理。
10. **swap-off 通用还原**：备份名是**前缀** `_orig_<basename>`，用 `basename -s _orig_`
    （后缀剥离）取不到文件名 → 除首批硬编码外全部还原失败。→ `${base#_orig_}` 前缀剥离 +
    overlay 镜像 `find -name`；要求基名全仓唯一。
11. **grep -c 零匹配退出码 1** 会断 bash `&&` 链 → 后续步骤（如 swap-off）被静默跳过。
    脚本里用 `[ "$(grep -c ...)" -eq 0 ]` 之外的判断都要防这个。
12. **PowerShell `>` 重定向**按 host 编码（UTF-16）重编码原始字节 → 取原始字节用
    `cmd /c "git show ... > file"`。
13. **目标语言 CJK 假阳性（2026-10-01）**："零 CJK"被当普适门控 → ja 译文（合法含汉字）逐条
    被拒 → 3 次重试 + 逐条兜底全灭 → 生成器硬失败。门控必须按目标脚本区分：非 CJK 脚本语言
    （含 ko，谚文不在 U+4E00-9FFF）用零 CJK；ja 禁用 CJK 检查，改查"译文 ≠ 源文逐字节"
    （防直通复制）+ 非空 + 单行。
14. **GBK 控制台显示假乱码（2026-10-01）**：日志/文件里的日文在 GBK 控制台渲染成
    `銉囥儣…`（UTF-8 字节按 GBK 显示）——**不是数据损坏**。判乱码必须按 UTF-8 解析文件
    内容后判断（正则/计数在内存字符串上跑，不受影响）；勿凭控制台渲染"修复"正确产物。
15. **`$var:` 驱动限定再犯（2026-10-01）**：生成器转正时日志消息写成 `"…$attempt: …"`，
    PS 5.1 把 `$attempt:` 当驱动限定变量 → 整脚本报 ParserError（进程秒退、stdout 为空）。
    即使已读 skill 也会照犯——新写/改写双引号字符串时逐字检查 `$var` 后紧跟 `:` 的形态
    （用 `[$var]` 或 `${var}:`）。排查特征：Start-Process 后进程立即消失、stdout 0 字节、
    stderr 是 ParserError。
16. **LLM CJK 响应的 JSON 转义与裸串（2026-10-01）**：① qwen-plus 对 CJK 响应大量输出
    `\uXXXX` 转义 → 手动"去引号+删反斜杠"会把 `\uXXXX` 撕成裸文本（假翻译）→ 必须
    `ConvertFrom-Json` 正规解析；② 模型偶发不转义内层引号（注释含 `<param name="x">` 时）
    → 严格解析失败 → 需宽松兜底（取首末引号间内容 + 还原 `\"`）；③ 模型偶发返回**未加引号的
    裸字符串**（忽略"返回 JSON 串"指令）→ 需第三级兜底（响应无引号时整体取值，门控仍生效）；
    ④ 逐条兜底提示词写 "(zero CJK allowed in output)" 被模型理解为"可不译 CJK" → 原样复制
    源文 → 提示词必须写"必须真译、禁止复制中文、短词也要译"。
17. **PS 5.1 子脚本退出码与多语言参数（2026-10-01）**：① `&` 调用的子 .ps1 执行
    `exit N` 不更新调用方 $LASTEXITCODE（只由原生 exe 更新）→ `if ($LASTEXITCODE -ne 0)`
    对全 OK 的注入器自检恒真报假失败 → 取子脚本退出码须起原生 `powershell.exe -File`
    子进程读 `Process.ExitCode`；② `-File` 模式下 `-Languages` 传逗号串是单个 argv token，
    `[string[]]` 参数不拆分（整串当语言名）；重复命名参数、追加裸 token 均被拒 →
    生成器参数改为单 `[string]` + 脚本内按逗号切分；③ PS 5.1 `Join-Path` 对 rooted 子路径
     是**拼接**不是取子（与 .NET `Path.Combine` 相反，PS 7.4+ 才修正）→ 注入器内部自拼
     repo 根，`-SidecarDir` 必须传相对路径；传入绝对路径后侧车目录"不存在"，注入器
     **静默跳过**（"no sidecar dir … skip; Chinese originals used" + exit 0）→ 空转自检
     形似通过，必须盯输出里每语言的 `injected OK` 行数（31 行/语言）。
18. **短中文词直通复制（2026-10-01）**：qwen-plus 对 2 字短词（`<summary>数量</summary>`、
    `<param name="name">名称</param>`）反复原样复制（分块 3/3、逐条 3/3 全直通），措辞加严
    无效 → **系统提示加具体示例**（中文词→目标词，如 数量→数え）才根治；示例用
    `[char]0x6570` 码点拼接构造，保持生成器纯 ASCII（§8.3 的延续）。直通检测门控
    （译文≠源文逐字节）是最后防线，防的是"漏网直通"，不是替代翻译质量。
19. **离线包包级资产回归源语言（2026-10-01）**：ja 离线包装出中文
    `README-离线文档说明.txt`——根因是 `build-offline-docs.sh` 的 README 生成段只有
    en/else 两分支，新语言（ja）落入 else 中文分支；同批漏掉 `.gitignore` 的
    `docs/ja/reference/api/` 条目（生成物 untracked 混入工作树）。→ 修复：ja 独立分支
    （`README-オフラインパッケージ説明.txt`，文件名+内容全日文，对齐 EN 包"全英文"口径）+
    gitignore 补条目。**通则：新语言接入时，包级用户可见资产（README 文件名/内容、端口说明、
    gitignore API 输出）必须逐一走一遍"包目标语言"检查**——站点内容（md/API 页/代码注释）
    有各自门控，但包级资产没有任何门控兜底，只能靠接入清单（§5.5/§7.7）。
20. **`[!code-csharp]` 标题内的方括号必须转义（2026-10-01，zh 站 CI 首暴）**：docfx 的
    `[!code-LANG[标题](路径#region)]` 语法中，**标题段内未转义的 `[` 或 `]` 会破坏 Markdown
    链接解析**，docfx 提取到的路径被截断 → `InvalidFileLink`。**最阴险之处**：报错只说"链接
    无效"，而目标 .cs 与 `#region` 锚点**真实存在**，极易误判为"CI checkout 落后于本机"
    或环境问题。
    - **判别口诀**：同一文档内写法"看起来一样"的两处，一处报一处不报——差异必在**标题里
      有没有漏转义的方括号**（已转义 `\[MapWhen\]` 的不报；`\[MapFormat\]` 转义了但
      `[ConvertBack]` 漏转义的仍报）。
    - **反引号包裹无效**：`` `[MapWhen]` `` 仍按链接语法解析，不逃逸（实测证实）。
    - 修法：标题内 `[` → `\[`，`]` → `\]`（圆括号不需转义）。
    - **全站扫描**：必须用**从左到右的括号配对解析**，不能用正则 `[^\]]*?`（遇标题里的裸 `]`
      会提前截断 → 假阴性，扫出 0 处但实际有多处）。扫描器须用 `git show HEAD:<file>` 取
      修改前版本**反向验证有效性**（能检出已知问题行，才可采信"其余无问题"）。
21. **`--warningsAsErrors` 站间不对称导致静默带病（2026-10-01）**：`.github/workflows/docs.yml`
    的 zh 站 build 带 `--warningsAsErrors`、**en/ja 站不带**。后果：zh 站 7 条告警直接 fail
    暴露，而 en/ja 站同类缺陷（实测 en 11 处、ja 9 处）**只打印告警、不失败、静默进入离线包**。
    - **通则：修完带门禁的站后，必须主动检查其余站是否有同类问题**——不要只修报告出来的那几条。
    - 检查须覆盖**全部告警类型**，不只 `InvalidFileLink`：ja 站实测另有
      `InvalidBookmark`（翻译残留，见 §8.23），同样会阻塞 `--warningsAsErrors`。
22. **`site-assets` 素材被 `**/*.md` 通配误纳入内容构建（2026-10-01，en 站 16 条）**：
    `docs/en-US/site-assets/samples/*/README.md`（§4 的 raw 覆盖资产）被
    `docfx.en.json` 的 `files: ["docs/en-US/**/*.md"]` **扫进内容构建**。其内相对链接按
    **最终落点**（站点 `samples/` 目录）书写，在素材位置解析不到 → 成批 `InvalidFileLink`。
    - **判据：凡是打包脚本在 `docfx build` 之后通过 overlay / raw 拷贝使用的目录，都不参与
      内容构建，必须显式加入 `content.exclude`。**
    - 修法：`docfx.en.json` 的 `exclude` 加 `"docs/en-US/site-assets/**"`（不影响 build 后的
      overlay 拷贝，那是独立步骤）。
    - **同源纪律**：往 `content.exclude` 加条目时，**必须同批检查所有语言的 toc 是否还引用它**
      （本项目已两次发生"exclude 加了、toc 忘删"→ `toc-<lang>.md` 引 `CONTRIBUTING.md` 报错）。
23. **翻译站锚点与跨语言链接残留（2026-10-01，ja 站）**：翻译工具把**标题**译成目标语言，
    但**正文里指向该标题的页内锚点引用没同步** → `InvalidBookmark`。ja 站实测 6 处含中文的
    锚点引用，其中 2 处报错（另 4 处因标题译文恰好保留了中文片段而侥幸可解析）。
    - 典型：`#23-json-序列化策略可替换`（标题已译为 `### 2.3 JSON シリアル化戦略の置き換え`）
      → 改 `#23-json-シリアル化戦略の置き換え`。
    - **同类**：跨页链接可能同时带**路径前缀错误**（如多写 `docs/ja/`，相对路径应自当前文件
      起算）。
    - **通则：翻译机制的验证门控（§7）只覆盖"代码注释/正文源语言"，不覆盖"锚点/交叉链接的
      有效性"**。新语言站接入后应独立跑一遍 `docfx build docfx.<lang>.json`（不带
      `--warningsAsErrors` 也要看告警），逐条核对 `InvalidBookmark` / `InvalidFileLink`。
24. **门户首页语言切换条：单语言离线包天然无法跨语言跳转（2026-10-01 定案，已删除）**：
    三站门户首页原各含一行语言切换条（如 `[**English**](../en-US/index.md) · 中文 · 日本語`）。
    - **问题根因**：离线包"一个包 = 一种语言"，**包里根本没有其他语言的目录**，所以任何指向
      其他语言的链接在包内必然断链。此前用"把 `docs/en-US/index.md` 注册进 `resource.files`"
      让 DocFX 构建期"可解析"（不报 `InvalidFileLink`），代价：
      - 包里混进一个**未渲染的 `.md` 源文件**（浏览器打开是一坨纯文本，且不在导航/搜索里）；
      - **en 包根本没登记** → en 包的 `../en-US/index.md` 指向打包后的自己、渲染成 `index.html`；
      - 文案与实际脱节（三站都写"日本語（即将开放）"，而日文包早已产出；en 站注释还写着
        "This is the Chinese site"）。
    - **定案（用户 2026-10-01 裁决）**：**彻底删除语言条**，不做任何跨语言链接。理由：单语言
      包不可能提供可用的跨语言跳转，与其留一个"点了要么死链、要么看到源码"的入口，不如诚实
      地不放。多语言发现改由各语言包内的 `README-离线包说明.txt` 承担（已写明"其他语言请下载
      对应包"）。
    - **配套改动（三处必须同批）**：① 三站 `docs/<lang>/index.md` 删语言条 + 清理相邻过时注释
      块；② `docfx.zh.json` / `docfx.ja.json` 的 `resource.files` 删 `"docs/en-US/index.md"`
      登记（否则包里继续残留未渲染的 .md）；③ `.github/workflows/docs.yml` 删 en 站的跨语链
      `InvalidFileLink` 豁免（`grep -v 'Invalid file link:(~/docs/zh-CN/index.md)'` 等）——
      语言条一删，这些豁免条目就是死代码，留着会掩盖未来真正的新增断链。
    - **教训通则**：**"让构建期门禁闭嘴"不等于"问题被修好"**。把断链加进豁免名单（或塞 resource
      强行可解析）只是把告警藏起来，产物里坏掉的链接依然存在。遇到断链应先问"这个链接在**最终
      交付形态**里能不能真的点通"，而不是先想怎么让 CI 变绿。
25. **toc 不得直接链接 metadata 输出目录（2026-10-01，zh 站 `--warningsAsErrors` 首次实证暴露）**：
   三站 `toc-<lang>.md`（navbar）与 `docs/<lang>/toc.md`（内容目录）第 89 行的
   `##[API 参考（自动生成）](<lang>/reference/api/)` 指向 **metadata 生成目录**。
   - **报错**：`toc-zh.md(89,1): warning InvalidFileLink: Invalid file link:(~/docs/zh-CN/reference/api/)`
     → zh 站 `--warningsAsErrors` 下 **exit 255**，CI 必红。
   - **根因**：DocFX 解析"目录链接"时**要求目录内有 `toc.yml` / `toc.md`**；而
     `docs/<lang>/reference/api/` 是 `docfx metadata` 生成物（`.gitignore` 已忽略），
     里面**只有 `*.yml`，没有任何 toc 文件** → 目录链接无法解析。
   - **为何长期未被发现**：`docs.yml` 只给 zh 站加了 `--warningsAsErrors`，en/ja 站没有 →
     **三站同缺陷，只有 zh 暴露**（与 §8.21 的"站间不对称"是同一类，本次给出具体后果）。
     而 zh 站的所谓"基线 0 告警"也从未在**干净克隆**下实证过。
   - **修法**：改指向**具体页面** `<lang>/reference/api/NCMapper.yml`（DocFX 会渲染为
     `NCMapper.html`）。这与 `reference/index.md` 正文里
     `[API 参考（NCMapper 命名空间）](api/NCMapper.yml)` 的写法一致，语义精确。
     **不要**改指 `reference/index.md`——该页已由同分节的"参考总览"条目占用，会造成两条目重复。
   - **同类预防**：toc 里任何指向**目录**的条目都要确认该目录有 toc 文件；指向
     `reference/api/` 这类生成物目录一律直接写具体 `.yml`。
   - **验证（干净克隆 + 完整 CI 链序 `metadata → strip → check-api-coverage → build`）**：
     修复前三站各 1 告警（zh exit 255）；修复后**三站 0 告警**，zh `--warningsAsErrors` **exit 0**。

## 9. 文件索引

| 文件 | 角色 |
|---|---|
| `scripts/gen-i18n-sidecars.ps1` | 侧车生成器（多语言参数化；自动发现 + 分块 LLM 翻译 + 目标语言门控 + 注入器自校验） |
| `scripts/inject-demo-comments.ps1` | 注入器（纯 ASCII；陈旧守卫 + 代码流自检；译文字段按 `-Language` 动态取） |
| `scripts/prepare-en-site-sources.sh` | swap-on/swap-off [lang]/overlay（构建期换入还原；备份按语言隔离） |
| `scripts/build-offline-docs.sh` | 构建编排（清场全量构建 + 换入位置） |
| `docs/i18n/<lang>/samples/.../*.cs.json` | 各语言注释数据（本机制的"翻译成果"；12 语 × 31 文件） |
| `docs/en-US/site-assets/samples/*/README.md` | EN 包内 samples README（raw 覆盖；须留在 content.exclude，勿被通配纳入） |
| `docfx.en.json` / `.github/workflows/docs.yml` | 构建/CI 接线（三语 metadata + build + 告警门禁 + G2 外链审计；2026-10-01 起 ja 站纳入） |
| `toc-<lang>.md` | 各语言站 navbar（链接须指向真实文件，且不得引用已 exclude 的文件） |
| `docfx.<lang>.json` | 各语言站配置（`content.exclude` 管误纳入、`resource.files` 管跨语言链接登记） |
| `~/.workbuddy/skills/docfx-link-forensics/` | 链接类告警取证 skill（括号配对扫描器 + 批量转义器 + 多站验证脚本 + 规则参考） |
