# tools/PublicApiDump — PublicApi 基线再生成器

把某个程序集的**公开 API 快照**导成文本，用于维护 `test/NCMapper.Tests/PublicApi.*.approved.txt`
五份基线。该工具此前按需临时重建多次、载体散落，故固定驻留入库以避免重复搭建。

## 1. 口径（唯一事实源在测试侧，本工具只做同口径复刻）

生成逻辑逐字对齐 `test/NCMapper.Tests/PublicApiTests.cs` 的 `GeneratePublicApi(Assembly)`：

```
ApiGenerator.GeneratePublicApi(asm)                   // PublicApiGenerator 11.1.0
  → 过滤 [assembly: System.Runtime.Versioning.TargetFramework…] / [assembly: …AssemblyMetadata…]
  → '\n' join → TrimEnd
```

落盘形态：UTF-8 **无 BOM**；默认写单个尾换行，`--no-eol` 则不写（见 §3）。

> 基线由 `AssertPublicApiMatches`（同文件 `:46`）以「整文件逐字比对（两侧先归一 `\r\n`、TrimEnd）」
> 消费。因此**与生成器输出不一致的手工编辑必然导致快照门失败**——本工具是更新基线的**唯一**合法路径。

## 2. 用法

```bash
# 构建（工具不在 NCMapper.slnx 内，须显式点名）
dotnet build tools/PublicApiDump/PublicApiDump.csproj --configuration Release

# 生成：<目标程序集 dll> <输出 txt> [--no-eol]
dotnet run --project tools/PublicApiDump/PublicApiDump.csproj --configuration Release --no-build -- \
  src/NCMapper/bin/Release/net10.0/NCMapper.dll /tmp/PublicApi.NCMapper.new.txt --no-eol

# 覆盖前核对（cmp 比 diff 更严格：能同时抓尾换行与 BOM 差异）
cmp test/NCMapper.Tests/PublicApi.NCMapper.approved.txt /tmp/PublicApi.NCMapper.new.txt && echo IDENTICAL
```

五份基线、目标程序集与其**末字节形态**（2026-09-21 实测，决定是否加 `--no-eol`；
NCMapper 行系 2026-09-21 过期记录——2026-10-09 复核订正：该基线实为**有尾换行**，须用**默认 eol** 生成，`--no-eol` 会产生尾字节 DIFF）：

| 基线文件（`test/NCMapper.Tests/`） | 目标程序集 | 末字节 | 生成参数 | 行数 |
|---|---|---|---|---|
| `PublicApi.NCMapper.approved.txt` | `src/NCMapper/bin/Release/net10.0/NCMapper.dll` | `0a` 有尾换行 | 默认 | 1127 |
| `PublicApi.NCMapper.Dapper.approved.txt` | `src/NCMapper.Dapper/bin/Release/net10.0/NCMapper.Dapper.dll` | `7d` 无尾换行 | `--no-eol` | 278 |
| `PublicApi.NCMapper.SourceGen.approved.txt` | `src/NCMapper.SourceGen/bin/Release/netstandard2.0/NCMapper.SourceGen.dll` | `7d` 无尾换行 | `--no-eol` | 23 |
| `PublicApi.NCMapper.EFCore.approved.txt` | `src/NCMapper.EFCore/bin/Release/net10.0/NCMapper.EFCore.dll` | `0a` 有尾换行 | 默认 | 112 |
| `PublicApi.NCMapper.FreeSql.approved.txt` | `src/NCMapper.FreeSql/bin/Release/net10.0/NCMapper.FreeSql.dll` | `0a` 有尾换行 | 默认 | 215 |

> 尾换行形态**库内本就不统一**（历史遗留），测试断言两侧均 `TrimEnd`，两种形态等价——
> 但要做 `cmp` 级全等覆盖，必须按上表选对参数。

## 3. 坑与纪律

- **依赖必须可解析**：`Assembly.LoadFrom` 只从目标 dll 所在目录解析依赖。四个多 TFM 程序集
  （NCMapper / Dapper / EFCore / FreeSql）在各自 `bin/Release/net10.0/` 下运行即可；
  **SourceGen 例外**——它的 `bin/Release/netstandard2.0/` 只有自身 dll（Roslyn 属 analysis-only 引用，
  不被复制），直接跑会抛
  `Mono.Cecil.AssemblyResolutionException: Failed to resolve assembly: 'Microsoft.CodeAnalysis, Version=4.8.0.0…'`。
  配方（2026-09-21 实测可行）：建临时目录，放 `NCMapper.SourceGen.dll` +
  `Microsoft.CodeAnalysis.dll`（`~/.nuget/packages/microsoft.codeanalysis.common/4.8.0/lib/**`）+
  `Microsoft.CodeAnalysis.CSharp.dll`（`~/.nuget/packages/microsoft.codeanalysis.csharp/4.8.0/lib/**`），
  再以该目录下的 dll 为入参。
- **TFM 选取与测试腿一致**：基线由 **net10.0** 腿产生（SourceGen 取 netstandard2.0 单一 TFM）；
  取错腿会得到"合理但非基线"的差异。
- **禁止手抄/按行手改**：必须取生成器实际输出后整段覆盖（含 `+` 续行与 100 字符折行形态，手工拼写必然失配）。
- **记录生成时的 commit**：快照须可追溯具体 HEAD（历史一次性驱动脚本用 `git rev-parse` 硬守卫锁 commit）。
- `packages.lock.json` 随工程入库（仓库级 `RestorePackagesWithLockFile` / `RestoreLockedMode` 生效）；
  改依赖后用 `dotnet restore tools/PublicApiDump/PublicApiDump.csproj -p:RestoreLockedMode=false` 重锁。

## 4. 验证记录

2026-09-21 于 Linux 构建机（`@0d5bc09` 干净检出）首轮验证：工具构建 0 warning / 0 error、
`packages.lock.json` 生成后锁模式复建通过；对五个程序集生成并与库内基线 `cmp`：

```
IDENTICAL  core (1075) / Dapper (278) / SourceGen (23) / EFCore (112) / FreeSql (215)
```

## 5. 边界

本工程**不入** `NCMapper.slnx` / `NCMapper.Lib.slnf`，CI 与本地门禁也不构建它
（同 `tools/DocSiteServer` 先例）；`IsPackable=false`，不参与打包与发布链。
PublicApi 基线的**判定门**始终是 `PublicApiTests` 快照门，本工具只负责生成。

## 相关文档

- 消费方与口径源：`test/NCMapper.Tests/PublicApiTests.cs`
- 门禁脚本索引：`scripts/README.md`（§5）
- 相关经验记录见项目文档
