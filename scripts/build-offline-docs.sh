#!/usr/bin/env bash
# build-offline-docs.sh — 离线文档包打包（双语言拆分）
#
# 架构（2026-09-01 定稿）：一个离线包 = 一种语言。中英两语各自独立构建并产出独立离线包：
#   zh-Hans 包（中文）：zh 叙事 + 中文 API（dll + zh-Hans XML）
#   en       包（英文）：en 叙事 + 英文 API（dll + en XML）
# 默认端口按语言区分（2026-09-30）：zh-Hans=8080，en=8081——两包可同时运行互不顶端口；
# start 脚本启动前自检端口占用，冲突时明确报错（修复"开英文包却落在中文站"问题）。
# 每个包组装四要素：start 脚本（bat/sh）+ server/（NativeAOT 单文件）+ site/（DocFX 站点）+ README-离线文档说明.txt。
# 中英两语 API 都用「dll + 对应语言 XML」统一口径（zh 762 / en 769 uid），
# 每个包只含一种语言 API → 无 DuplicateUids（原单站双语方案 547 个冲突已消除）。
#
# PDF 已移除：本包面向有浏览器的内网/离网环境，
# 打印/PDF 需求用浏览器打印功能完成（README 中说明）。
#
# 与 CI docs.yml 同链构建 site/（metadata → strip-external-xref → build，保证零外链），
# 零外链审计（scripts/check-doc-external-links.sh）为打包前置门禁，exit 非 0 中止打包。
# 另有链接闭合门禁（scripts/check-markdown-links.sh），置于构建前置：源 md 阶段拦截
# 未闭合链接（形如 `[文本](路径` 缺右括号，DocFX 静默渲染为纯文本 → 站点导航不可点）。
#
# 用法（仓库根执行）：
#   ./scripts/build-offline-docs.sh [win-x64|linux-x64|all] [语言|language...|all]（默认 win-x64 zh-Hans）
#   语言：单值（zh-Hans / en）、逗号分隔多值（en,de）、或 all（全部已接入语言）。
#         可用语言 = 脚本内 lang_cfg 映射表的键；接入新语言只需加映射表一行。
#   all 平台在本地 Windows 仅产出 win-x64 —— linux-x64 变体固定由 CI ubuntu 双 runner 发布
#   （NativeAOT 不支持 Windows 交叉发布 linux-x64；offline-package.yml 承载）。
# 示例：
#   ./scripts/build-offline-docs.sh                                  # win-x64 中文
#   ./scripts/build-offline-docs.sh win-x64 all                       # win-x64 全部语言
#
# 产物：dist/offline/NCMapper-离线包-{语言}-v{版本}-{rid}.zip（.gitignore 已覆盖 dist/）
# 构建临时区：temp/offline/（统一临时区，zip 仍落 dist/offline 供 CI 抓包）
# 退出码：0=成功  1=失败  2=前置缺失
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

# ---- 平台参数 ----
rid="${1:-win-x64}"
case "$rid" in
  win-x64|linux-x64) ;;
  all)
    # 本地 all：Windows 仅产 win-x64（linux 由 CI），见文件头说明
    rid="win-x64"
    echo "[INFO] all 模式本地仅产出 win-x64（linux-x64 由 CI offline-package.yml 发布）"
    ;;
  *)
    echo "[ERROR] 不支持的平台参数：$rid（可选 win-x64 / linux-x64 / all）" >&2
    exit 2
    ;;
esac

# ---- 语言映射表（前移至此：语言解析需查询本表）----
# 语言 → 配置/目录/包名/端口映射。每个语言独立构建站点，产出独立离线包。
# 语言包目录命名用英文（zip 文件名可含中文，目录名避免）。
# 支持的语言集合 = 本表的键集合；新增语言只需在此加一行（端口须唯一），
# 白名单校验与 all 的展开均自动生效 —— 见下方「语言参数」段。
# 注意：bash 关联数组需在此一次性声明，键名即语言标识（与 locales/<lang>/、docs/<lang>/ 对齐）。
declare -A lang_cfg lang_dir lang_pkg lang_zhname lang_port
lang_cfg[zh-Hans]="docfx.zh.json";  lang_dir[zh-Hans]="zh-Hans";  lang_pkg[zh-Hans]="NCMapper-离线包-中文";   lang_zhname[zh-Hans]="中文"; lang_port[zh-Hans]=8080
lang_cfg[en]="docfx.en.json";       lang_dir[en]="en";            lang_pkg[en]="NCMapper-Offline-EN";       lang_zhname[en]="英文"; lang_port[en]=8081

# all 展开顺序 = 本表键的显式声明顺序（bash 关联数组无序，故另存一份有序列表）
lang_order=( zh-Hans en )

# ---- 语言参数 ----
# 形态（已扩展）：
#   单语言     zh-Hans | en | <新增语言>          → 只构建该语言
#   多语言     en,de（逗号分隔，无空格）        → 依次构建所列语言
#   all        全部已接入语言（= lang_order 全量）
# 合法性 = 是否为映射表的键；不再硬编码白名单，接入新语言只需加映射表一行。
lang_spec="${2:-zh-Hans}"
langs=()
if [ "$lang_spec" = "all" ]; then
  langs=( "${lang_order[@]}" )
else
  # 逗号分隔 → 逐个校验（IFS 只按逗号切，不用 read -a 以兼容空段检测）
  IFS=',' read -r -a _requested <<< "$lang_spec"
  for _lang in "${_requested[@]}"; do
    if [ -z "$_lang" ]; then
      echo "[ERROR] 语言参数含空段（连续逗号或首尾逗号）：'$lang_spec'" >&2
      exit 2
    fi
    if [ -z "${lang_cfg[$_lang]:-}" ]; then
      echo "[ERROR] 不支持的语言：$_lang（可选：${lang_order[*]} / all / 逗号分隔多选）" >&2
      exit 2
    fi
    langs+=( "$_lang" )
  done
fi
if [ "${#langs[@]}" -eq 0 ]; then
  echo "[ERROR] 语言参数为空" >&2
  exit 2
fi

# ---- 版本号 ----
version="$(git describe --tags 2>/dev/null || git rev-parse --short HEAD)"
# git describe 输出可能含 '/'（如 tag/branch 名），文件名中替换为 '-'（zip 命名防路径拆分）
zip_version="${version//\//-}"
echo "[0/7] 版本：$version（rid：$rid，语言：${langs[*]}）"

# ---- 前置检查 ----
if [ ! -f .config/dotnet-tools.json ]; then
  echo "[ERROR] .config/dotnet-tools.json 缺失（docfx 工具版本锁定文件）" >&2
  exit 2
fi
dotnet tool restore >/dev/null

# ---- 链接闭合门禁（构建前置）----
# 源 md 阶段拦截未闭合 markdown 链接（形如 `[文本](路径` 缺右括号）。DocFX 对这类链接
# 不报错、静默渲染为纯文本，曾导致站点左侧导航/门户/页脚链接全部不可点（2026-09-27
# 修复 114 处后加此门禁）。exit 非 0 由 set -e 中止打包，早于 docfx build。
echo "[gate] markdown 链接闭合自检"
"$script_dir/check-markdown-links.sh"

# 演示覆盖对账（告警版：只提醒不拦截，退出码恒 0）
echo "[gate] 演示覆盖对账（告警版）"
"$script_dir/check-demo-coverage.sh" || true

# 公开 API 架构图 ↔ 演示覆盖 反向对账（告警版：退出码恒 0）
echo "[gate] API 架构图覆盖对账（告警版）"
"$script_dir/check-api-demo-coverage.sh" || true

dist_dir="$repo_root/dist/offline"
tmp_dir="$repo_root/temp/offline"
# 清理上次残留：删除失败不阻塞（临时产物目录，gitignore 覆盖；本地安全删除机制可能拦截批量删除）
rm -rf "$tmp_dir" 2>/dev/null || true
mkdir -p "$dist_dir" "$tmp_dir"

# 传给 dotnet/docfx/powershell 的路径必须为 Windows 风格（.NET 不识别 Git Bash 的 /d/ 前缀；
# CI ubuntu 无 cygpath 时回退原路径，Linux .NET 认 Unix 路径）。
win_root="$(cygpath -w "$repo_root" 2>/dev/null || echo "$repo_root")"

# 语言映射表已前移至「语言参数」段之前（解析需查表），此处不再重复声明。

# ---- 1/7 站点构建（各语言独立构建，与 CI docs.yml 同链）----
echo "[1/7] 构建各语言 DocFX 站点（metadata → strip-xref → build）"
for lang in "${langs[@]}"; do
  echo "  —— 构建 ${lang_zhname[$lang]}站（${lang_cfg[$lang]}）——"
  # API metadata 源准备（dll + 该语言 XML）
  if [ ! -f src/NCMapper/bin/Release/net8.0/NCMapper.dll ]; then
    echo "[INFO] Release/net8.0 dll 缺失，先构建（API 参考依赖）"
    dotnet build src/NCMapper -c Release -f net8.0 >/dev/null
  fi
  "$script_dir/prepare-api-src.sh" "$lang" net8.0

  echo "[1/7] docfx metadata（${lang_zhname[$lang]} API：dll + ${lang} XML）"
  dotnet docfx metadata "${lang_cfg[$lang]}" >/dev/null

  echo "[2/7] 剥离 BCL 外链（审计兜底）：${lang_zhname[$lang]}"
  lang_api_dir="docs/${lang_cfg[zh-Hans]:+zh-CN}/reference/api"
  if [ "$lang" = "en" ]; then lang_api_dir="docs/en-US/reference/api"; fi
    "$script_dir/strip-external-xref.sh" "$lang_api_dir"

  # 叙事站（en）：build 前注入该语言注释版演示 .cs（注释侧车 docs/i18n/<lang>/ →
  # 临时覆盖，[!code-csharp] 构建期嵌入页面）；build 后还原中文原版（失败也经 trap 还原，
  # 见 scripts/prepare-en-site-sources.sh + scripts/inject-demo-comments.ps1，已按语言参数化）
  case "$lang" in
    en)
      "$script_dir/prepare-en-site-sources.sh" swap-on "$lang"
      trap "\"$script_dir/prepare-en-site-sources.sh\" swap-off $lang" EXIT
      ;;
  esac

  echo "[3/7] docfx build → 站点（${lang_zhname[$lang]}）"
  win_tmp_site="$win_root/temp/offline/_site_tmp_${lang}_$rid"
  # 强制全量构建：docfx 增量构建以输出目录为缓存，未改动的 md 不重渲染；而
  # [!code-csharp] 引用的 samples .cs 是外部输入（不在 docfx 输入跟踪内），en 站
  # 注释侧车换入/还原不会被增量构建感知 → 必须清场全量，否则嵌入代码注释语言残留旧值
  rm -rf "$win_tmp_site"
  dotnet docfx build "${lang_cfg[$lang]}" -o "$win_tmp_site" >/dev/null
  html_count="$(find "$tmp_dir/_site_tmp_${lang}_$rid" -type f -name '*.html' | wc -l | tr -d ' ')"
  if [ "$html_count" -eq 0 ]; then
    echo "[ERROR] ${lang_zhname[$lang]}站构建产物为空（docfx build 未产出 html）" >&2
    exit 1
  fi
  case "$lang" in
    en)
      "$script_dir/prepare-en-site-sources.sh" swap-off "$lang"
      trap - EXIT
      ;;
  esac
done

# ---- 4/7 服务器发布（NativeAOT self-contained 单文件，两语言共享同一 server 二进制）----
echo "[4/7] 发布 DocSiteServer（$rid，NativeAOT）"
# 注意：不吞输出——NativeAOT 需要本机/runner 的 C++ 链接器（clang/lld，VS C++ workload），
# 失败时错误必须可见（P7：本地若缺工具链，publish 失败为环境性，实证以 CI offline-package.yml 为准）
win_tmp_server="$win_root/temp/offline/_server_tmp_$rid"
dotnet publish tools/DocSiteServer/DocSiteServer.csproj \
  --configuration Release \
  --runtime "$rid" \
  --self-contained true \
  -p:PublishAot=true \
  --output "$win_tmp_server"

server_exe="DocSiteServer"
[ "$rid" = "win-x64" ] && server_exe="DocSiteServer.exe"
if [ ! -f "$tmp_dir/_server_tmp_$rid/$server_exe" ]; then
  echo "[ERROR] NativeAOT 发布产物缺失：$tmp_dir/_server_tmp_$rid/$server_exe" >&2
  exit 1
fi
server_size="$(du -h "$tmp_dir/_server_tmp_$rid/$server_exe" | cut -f1)"

# ---- 5/7 组装包结构（每语言一个包：start + server + site + README）----
echo "[5/7] 组装各语言包结构（server $server_size）"
for lang in "${langs[@]}"; do
  tmp_site="$tmp_dir/_site_tmp_${lang}_$rid"
  tmp_server="$tmp_dir/_server_tmp_$rid"
  pkg_root="$tmp_dir/pkg_${lang}_$rid"
  mkdir -p "$pkg_root"
  mv "$tmp_site" "$pkg_root/site"

  # NativeAOT 发布的调试符号（.pdb，win-x64 实测约 45 MB）运行时用不到，不入包。
  # 说明：csproj 里的 StripSymbols=true 只剥离可执行文件内部符号，不影响独立 pdb 被复制到发布目录，
  # 故须在打包环节显式剔除。只删 temp 临时发布目录内的 pdb（每次打包重新生成），不动源码树；
  # 模式同时覆盖 win（DocSiteServer.pdb）与 linux（DocSiteServer.pdb / .dbf）两种命名。
  find "$tmp_server" -type f \( -name '*.pdb' -o -name '*.dbf' \) -delete
  cp -r "$tmp_server" "$pkg_root/server"

  # en 站：raw 文件资产（samples README，不经构建期渲染，build 后覆盖安全）→ 英文版。
  if [ "$lang" = "en" ]; then
    "$script_dir/prepare-en-site-sources.sh" overlay "$pkg_root/site"
  fi

  # 站点根 index.html：各语言包统一以 index.html 作为离线包默认文件。
  # 跳转页源文件位于 docs/（navbar 三件套收拢），DocFX 按源路径拷为
  # <site>/docs/index.html（zh）与 <site>/docs/index-<lang>.html（en），此处一并搬移到站点根。
  # 页面内链接按「站点根」视角书写（docs/<lang>/index.html），搬移后即正确，无需改写内容。
  for _idx in docs/index.html docs/index-en.html; do
    if [ -f "$pkg_root/site/$_idx" ]; then
      mv -f "$pkg_root/site/$_idx" "$pkg_root/site/index.html"
    fi
  done

  # 构建残留清理：xrefmap.yml 是 DocFX 的 uid→页面交叉引用索引，构建期内部使用，
  # 浏览器运行时不读取（xrefService 已关闭，无处发布）——离线包不装构建残留
  rm -f "$pkg_root/site/xrefmap.yml"

  # DocFX 默认模板自带的 source map（.map，合计约 3.6 MB：docfx.vendor.min.js.map 3.0 MB 等）
  # 仅供开发者调试压缩后的 js/css，离线包运行时零读取（浏览器仅在开发者工具"查看源代码"时才拉取）
  # ——不入包
  find "$pkg_root/site" -type f -name '*.map' -delete

  # manifest.json 是 DocFX 构建期内部清单（源路径→输出映射），站点运行时零引用
  # （全站 grep 无命中，styles/ 脚本亦不读取）；且内含构建机本地绝对路径
  # （source_base_path，如本机绝对路径），构成信息泄露 —— 不入包
  rm -f "$pkg_root/site/manifest.json"

  # start.bat（Windows；bat 用 CRLF 行尾保证 cmd 兼容；默认端口按语言区分，启动前自检占用）
  {
    printf '@echo off\r\n'
    printf 'REM NCMapper %s 文档离线包启动脚本（win-x64，默认端口 %s）\r\n' "${lang_zhname[$lang]}" "${lang_port[$lang]}"
    printf 'cd /d "%%~dp0"\r\n'
    printf 'set PORT=%s\r\n' "${lang_port[$lang]}"
    printf 'netstat -ano | findstr /C:":%%PORT%% " | findstr /C:"LISTENING" >nul\r\n'
    printf 'if not errorlevel 1 (\r\n'
    printf '  echo [ERROR] 端口 %%PORT%% 被占用（可能另一语言离线包正在运行）。请先停止占用端口的服务，或手动指定其他端口运行：server\\DocSiteServer.exe 9090\r\n'
    printf '  pause\r\n'
    printf '  exit /b 1\r\n'
    printf ')\r\n'
    printf 'echo Starting NCMapper %s docs at http://localhost:%%PORT%% ...\r\n' "${lang_zhname[$lang]}"
    printf 'start "" "http://localhost:%%PORT%%"\r\n'
    printf 'server\\DocSiteServer.exe %%PORT%%\r\n'
  } > "$pkg_root/start.bat"

  # start.sh（Linux；sh 兼容，免 chmod，用 sh start.sh 执行；默认端口按语言区分，启动前自检占用）
  {
    printf '#!/usr/bin/env sh\n'
    printf '# NCMapper %s 文档离线包启动脚本（linux-x64，默认端口 %s）\n' "${lang_zhname[$lang]}" "${lang_port[$lang]}"
    printf 'cd "$(dirname "$0")"\n'
    printf 'PORT=%s\n' "${lang_port[$lang]}"
    printf 'if command -v ss >/dev/null 2>&1 && ss -ltn 2>/dev/null | grep -q ":$PORT "; then\n'
    printf '  echo "[ERROR] 端口 $PORT 被占用（可能另一语言离线包正在运行）。请先停止占用端口的服务，或手动指定其他端口运行：./server/DocSiteServer 9090"\n'
    printf '  exit 1\n'
    printf 'fi\n'
    printf 'echo "Starting NCMapper %s docs at http://localhost:$PORT ..."\n' "${lang_zhname[$lang]}"
    printf './server/DocSiteServer $PORT\n'
  } > "$pkg_root/start.sh"

  # 包说明文件：各语言包用本语言文件名与内容（中文包沿用中文；英文包全英文；日文包全日文）
  if [ "$lang" = "en" ]; then
    readme_name="README-Offline-Package.txt"
    {
      echo "NCMapper English Documentation Offline Package"
      echo "=================="
      echo "Version: ${version} (${rid})"
      echo ""
      echo "Package structure:"
      echo "  start.bat / start.sh  Launcher scripts (Windows / Linux)"
      echo "  server/               Static docs server (NativeAOT single file, no .NET install needed)"
      echo "  site/                 Documentation site (complete offline copy, zero external links)"
      echo ""
      echo "Starting:"
      echo "  Windows: double-click start.bat; or from a terminal: server\\DocSiteServer.exe [port]"
      echo "  Linux:   sh start.sh; or ./server/DocSiteServer [port]"
      echo "  Default port ${lang_port[$lang]} (Chinese package 8080 / English package 8081, all can run at the same time);"
      echo "  on port conflict pass a different one, e.g.: server\\DocSiteServer.exe 9090"
      echo ""
      echo "Access:"
      echo "  Local:  http://localhost:${lang_port[$lang]}"
      echo "  LAN:    http://<server-ip>:${lang_port[$lang]} (all accessible addresses are printed in the startup log)"
      echo ""
      echo "Notes:"
      echo "  * This package targets intranet/offline environments with a browser; fully functional offline (incl. site search)."
      echo "  * For print/PDF use your browser's print function (this package ships no PDFs, decision of 2026-08-20)."
      echo "  * The site is a build-time snapshot; see the version above. See the project repo docs for the full update process."
      echo "  * Chinese and English are released as separate offline packages; download the other package for the other language."
    } > "$pkg_root/$readme_name"
  elif [ "$lang" = "de" ]; then
    readme_name="README-オフラインパッケージ説明.txt"
    {
      echo "NCMapper 日本語ドキュメント オフラインパッケージ"
      echo "=================="
      echo "バージョン：${version}（${rid}）"
      echo ""
      echo "パッケージ構成："
      echo "  start.bat / start.sh  起動スクリプト（Windows / Linux）"
      echo "  server/               静的ドキュメントサーバー（NativeAOT 単一ファイル、.NET のインストール不要）"
      echo "  site/                 ドキュメントサイト（完全なオフラインコピー、外部リンクゼロ）"
      echo ""
      echo "起動："
      echo "  Windows：start.bat をダブルクリック；またはコマンドラインで server\\DocSiteServer.exe [ポート] を実行"
      echo "  Linux：  sh start.sh；または ./server/DocSiteServer [ポート]"
      echo "  デフォルトポート ${lang_port[$lang]}（中国語パッケージ 8080 / 英語パッケージ 8081 / 日本語パッケージ 8082、同時に実行可能）；"
      echo "  ポート競合時は別のポートを引数で指定、例：server\\DocSiteServer.exe 9090"
      echo ""
      echo "アクセス："
      echo "  ローカル：  http://localhost:${lang_port[$lang]}"
      echo "  LAN：       http://<サーバーIP>:${lang_port[$lang]}（起動ログに全アクセス可能アドレスを出力）"
      echo ""
      echo "備考："
      echo "  * このパッケージはブラウザを備えたイントラネット/オフライン環境を対象としており、ネットワーク遮断下でも全機能が利用可能（サイト内検索を含む）。"
      echo "  * 印刷/PDF が必要な場合はブラウザの印刷機能をご使用ください（本パッケージは PDF を同梱していません、2026-08-20 決定）。"
      echo "  * サイトはビルド時スナップショットであり、バージョンは上記を参照；完全な更新フローはプロジェクトリポジトリのドキュメントを参照。"
      echo "  * 各言語は独立したオフラインパッケージとして個別にリリースされています；他言語が必要な場合は該当するパッケージをダウンロードしてください。"
    } > "$pkg_root/$readme_name"
  else
    readme_name="README-离线文档说明.txt"
    {
      echo "NCMapper ${lang_zhname[$lang]}文档离线包"
      echo "=================="
      echo "版本：${version}（${rid}）"
      echo ""
      echo "包结构："
      echo "  start.bat / start.sh  启动脚本（Windows / Linux）"
      echo "  server/               静态文档服务器（NativeAOT 单文件，无需安装 .NET）"
      echo "  site/                 文档站点（完整离线副本，零外链）"
      echo ""
      echo "启动："
      echo "  Windows：双击 start.bat；或命令行运行 server\\DocSiteServer.exe [端口]"
      echo "  Linux：  sh start.sh；或 ./server/DocSiteServer [端口]"
      echo "  默认端口 ${lang_port[$lang]}（中文包 8080 / 英文包 8081 / 日文包 8082，三包可同时运行）；端口冲突时以参数指定，如：server\\DocSiteServer.exe 9090"
      echo ""
      echo "访问："
      echo "  本机：    http://localhost:${lang_port[$lang]}"
      echo "  局域网：  http://<服务器IP>:${lang_port[$lang]}（启动日志会打印全部可访问地址）"
      echo ""
      echo "说明："
      echo "  * 本包面向有浏览器的内网/离网环境，断网全功能可用（含站内搜索）。"
      echo "  * 打印/PDF 需求请使用浏览器打印功能（本包不含 PDF，2026-08-20 决策）。"
      echo "  * 站点为构建时快照，版本见上方；完整更新流程见项目仓库文档。"
      echo "  * 中英两语分开发布独立离线包；如需另一语言请下载对应包。"
    } > "$pkg_root/$readme_name"
  fi

  # 6/7 外链审计（打包前置门禁）
  echo "[6/7] 外链审计（${lang_zhname[$lang]}站点）"
  "$script_dir/check-doc-external-links.sh" "$pkg_root/site"

  # 7/7 压缩
  echo "[7/7] 压缩（${lang_zhname[$lang]}）"
  zip_name="${lang_pkg[$lang]}-v${zip_version}-${rid}.zip"
  zip_path="$dist_dir/$zip_name"
  rm -f "$zip_path"
  if command -v zip >/dev/null 2>&1; then
    (cd "$pkg_root" && zip -qr "$zip_path" .)
  else
    # Windows Git Bash 无 zip 时用 PowerShell Compress-Archive（路径须 Windows 风格）
    win_pkg_root="$win_root/temp/offline/pkg_${lang}_$rid"
    powershell -NoProfile -Command "Compress-Archive -Path '$win_pkg_root/*' -DestinationPath '$win_root/dist/offline/$zip_name' -Force"
  fi
  if [ ! -f "$zip_path" ]; then
    echo "[ERROR] zip 产出失败：$zip_path" >&2
    exit 1
  fi
  echo "离线包完成：$zip_path"
  echo "  内容：start.bat + start.sh + server/（${server_size}）+ site/（${html_count} html）+ $readme_name"
done

rm -rf "$tmp_dir"
exit 0
