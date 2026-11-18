using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// Check 4：能力面完备性门禁（第三层——评审_001 J-5）。
/// <para>Check 4（forward）：机械枚举（NCMapper.Attributes 全部 Attribute 派生类型 type-level 行
/// ＋ DeclaredOnly 公共实例属性 member 行）↔ <see cref="CapabilitySurfaceRegistry"/> 人工登记行逐行比对 0 差异；
/// C 类行必须挂追踪任务号、N-A 行必须写明理由、A/B 类行必须引用 EquivalenceRegistry 既有编号。</para>
/// <para>核查逻辑提取为纯函数（RunCheck4/SurfaceOfType）——真实核查与 self-proof 自证测试共用同一代码路径
///（防假测试 #38：内存变异走真实核查函数，拔掉核查逻辑断言必红）。</para>
/// </summary>
public class CapabilitySurfaceIntegrityTests
{
    // ----------------------------------------------------------------
    // Check 4 (real): mechanical surface == registry, 0 diff
    // ----------------------------------------------------------------

    [Fact]
    public void Check4_MechanicalSurface_MatchesRegistryRowForRow()
    {
        var mechanical = EnumerateCurrentSurface();
        var failures = RunCheck4(mechanical, CapabilitySurfaceRegistry.Rows, EquivalenceRegistry.AllIds);

        Assert.True(failures.Count == 0,
            "Check 4 FAILED:\n" + string.Join("\n", failures));
    }

    // ----------------------------------------------------------------
    // 编制时点锚：11 个特性类型在机械面中出现（新增类型自动进机械面 → 未登记即 Check 4 红）
    // ----------------------------------------------------------------

    [Fact]
    public void MechanicalSurface_ContainsAllElevenKnownAttributeTypes()
    {
        var mechanical = EnumerateCurrentSurface();
        var typeNames = mechanical.Where(r => r.Member == null).Select(r => r.AttributeType).ToHashSet(StringComparer.Ordinal);

        foreach (var known in new[]
                 {
                     "MapFromAttribute", "MapIgnoreAttribute", "MaskAttribute", "MapPropertyAttribute",
                     "ConvertAttribute", "ConvertBackAttribute", "MapTreeAttribute",
                     "MapNoAutoIncludeAttribute", "MapDefaultsAttribute", "NCMapperOptionsAttribute",
                     "MapperConfigAttribute",
                 })
        {
            Assert.True(typeNames.Contains(known), $"机械面缺少已知特性类型 {known}（枚举源失效？）");
        }

        Assert.True(typeNames.Count >= 11, $"机械面特性类型数 {typeNames.Count} 少于编制时点值 11");
    }

    // ----------------------------------------------------------------
    // 红名单登记断言（标准 2 的人工审查机器锚——对照）
    // ----------------------------------------------------------------

    [Fact]
    public void RedList_FiveRows_AllClosed_AsEquivalencePairs()
    {
        // 首跑红名单 5 行全部关闭（5/5 清账，收尾）——
        // 断言由"C 类挂追踪号"转为"A 类等价对登记防回退"（关闭行为变更即交付物，B2 纪律）
        var rows = CapabilitySurfaceRegistry.Rows;

        // 改挂断言：A-14/A-15 退役，红名单第 1/2 行改挂 A-11/A-09 承载侧声明
        //（Note 含"承载侧"机械判定锚关键词；旧"实体侧"字样随改写移除防假绿——复审 P2-F 口径）
        var remountedMi = rows.First(r => r.AttributeType == "MapIgnoreAttribute" && r.Member == "Direction");
        Assert.Equal(CapabilitySurfaceClass.A, remountedMi.Class);
        Assert.Equal("A-11", remountedMi.Registration);
        Assert.Contains("承载侧", remountedMi.Note);

        var remountedCbs = rows.First(r => r.AttributeType == "ConvertBackAttribute" && r.Member == null);
        Assert.Equal(CapabilitySurfaceClass.A, remountedCbs.Class);
        Assert.Equal("A-09", remountedCbs.Registration);
        Assert.Contains("承载侧", remountedCbs.Note);

        // 红名单第 3 行：FromField 索引路径 → A-16（G7 关闭）
        var closedIdx = rows.First(r => r.AttributeType == "MapPropertyAttribute" && r.Member == "FromField");
        Assert.Equal(CapabilitySurfaceClass.A, closedIdx.Class);
        Assert.Equal("A-16", closedIdx.Registration);

        // 红名单第 4 行：[MapTree] 树配置 → A-17（G2 关闭——类型行 + 三属性行全转 A）
        foreach (var member in new string?[] { null, "IdProperty", "ParentIdProperty", "ChildrenProperty" })
        {
            var row = rows.First(r => r.AttributeType == "MapTreeAttribute" && r.Member == member);
            Assert.Equal(CapabilitySurfaceClass.A, row.Class);
            Assert.Equal("A-17", row.Registration);
        }

        // 红名单第 5 行：属性级 [MapNoAutoInclude] → A-18（D1 关闭）
        var closedNai = rows.First(r => r.AttributeType == "MapNoAutoIncludeAttribute" && r.Member == null);
        Assert.Equal(CapabilitySurfaceClass.A, closedNai.Class);
        Assert.Equal("A-18", closedNai.Registration);

        // A-25 归位断言：对级白名单类型行转 A 挂 A-25
        var closedNam = rows.First(r => r.AttributeType == "MapNoAutoMatchAttribute" && r.Member == null);
        Assert.Equal(CapabilitySurfaceClass.A, closedNam.Class);
        Assert.Equal("A-25", closedNam.Registration);

        // C 类行清零防回退：全表不得存在 C 类缺口行——批内登记的例外已随转 A 全部清账，
        // 本表口径「C 类行随对应批次落地转 A 并更新本表」。
        var openCTracking = rows.Where(r => r.Class == CapabilitySurfaceClass.C).ToList();
        Assert.Empty(openCTracking);
    }

    private static void AssertGapRow(
        IReadOnlyList<CapabilitySurfaceRow> rows, string attributeType, string? member, string tracking)
    {
        var row = rows.FirstOrDefault(r => r.AttributeType == attributeType && r.Member == member);
        Assert.True(row != null, $"红名单行缺失：({attributeType}, {member})");
        Assert.Equal(CapabilitySurfaceClass.C, row.Class);
        Assert.Equal(tracking, row.Registration);
    }

    // ----------------------------------------------------------------
    // Self-proofs（内存变异——与真实核查同一 RunCheck4 代码路径）
    // ----------------------------------------------------------------

    /// <summary>登记表漂移（删 1 行）→ RunCheck4 必红（标准 3 变异演练的内存同构）。</summary>
    [Fact]
    public void SelfProof_RemovedRegistryRow_Detected()
    {
        var mechanical = EnumerateCurrentSurface();
        var mutated = CapabilitySurfaceRegistry.Rows
            .Where(r => !(r.AttributeType == "MaskAttribute" && r.Member == "MaskType"))
            .ToList();

        var failures = RunCheck4(mechanical, mutated, EquivalenceRegistry.AllIds);

        Assert.Contains(failures, f => f.Contains("UNREGISTERED") && f.Contains("(MaskAttribute, MaskType)"));
    }

    /// <summary>门禁语义负向：新增特性（模拟机械面新行）无登记行 → 必红（标准 4）。</summary>
    [Fact]
    public void SelfProof_NewAttributeWithoutRegistration_Detected()
    {
        var mutatedMechanical = EnumerateCurrentSurface()
            .Append(("FakeNewAttribute", null, "Class"))
            .ToList();

        var failures = RunCheck4(mutatedMechanical, CapabilitySurfaceRegistry.Rows, EquivalenceRegistry.AllIds);

        Assert.Contains(failures, f => f.Contains("UNREGISTERED") && f.Contains("FakeNewAttribute"));
    }

    /// <summary>新增参数（既有特性新增公共属性，模拟机械面新 member 行）无登记行 → 必红。</summary>
    [Fact]
    public void SelfProof_NewParameterWithoutRegistration_Detected()
    {
        var mutatedMechanical = EnumerateCurrentSurface()
            .Append(("MaskAttribute", "NewCapabilityParameter", "Boolean"))
            .ToList();

        var failures = RunCheck4(mutatedMechanical, CapabilitySurfaceRegistry.Rows, EquivalenceRegistry.AllIds);

        Assert.Contains(failures, f => f.Contains("UNREGISTERED") && f.Contains("(MaskAttribute, NewCapabilityParameter)"));
    }

    /// <summary>C 类行无追踪任务号 → 必红（CI 门禁语义）。</summary>
    /// <remarks> 后真实表 C 类清零，自证改经临时假 C 行（机械面+登记面同步伪造）走同一 RunCheck4。</remarks>
    [Fact]
    public void SelfProof_CRowWithoutTrackingId_Detected()
    {
        var mechanical = EnumerateCurrentSurface()
            .Append(("FakeCAttribute", null, "Class"))
            .ToList();
        var mutated = CapabilitySurfaceRegistry.Rows
            .Append(new CapabilitySurfaceRow(
                "FakeCAttribute", null, "Class", CapabilitySurfaceClass.C, "", "自证用假 C 行"))
            .ToList();

        var failures = RunCheck4(mechanical, mutated, EquivalenceRegistry.AllIds);

        Assert.Contains(failures, f => f.Contains("C 类") && f.Contains("追踪任务号"));
    }

    /// <summary>N-A 行无理由 → 必红。</summary>
    [Fact]
    public void SelfProof_NaRowWithoutReason_Detected()
    {
        var mechanical = EnumerateCurrentSurface();
        var mutated = CapabilitySurfaceRegistry.Rows
            .Select(r => r.AttributeType == "MapFromAttribute" && r.Member == "RequiresDowngrade"
                ? r with { Registration = "" }
                : r)
            .ToList();

        var failures = RunCheck4(mechanical, mutated, EquivalenceRegistry.AllIds);

        Assert.Contains(failures, f => f.Contains("N-A") && f.Contains("理由"));
    }

    /// <summary>AttributeUsage 位漂移（ValidOn 变化未重新登记）→ DRIFT 必红。</summary>
    [Fact]
    public void SelfProof_AttributeUsageDrift_Detected()
    {
        var mutatedMechanical = EnumerateCurrentSurface()
            .Select(r => r.AttributeType == "MaskAttribute" && r.Member == null
                ? (r.AttributeType, r.Member, "Property|Class")
                : r)
            .ToList();

        var failures = RunCheck4(mutatedMechanical, CapabilitySurfaceRegistry.Rows, EquivalenceRegistry.AllIds);

        Assert.Contains(failures, f => f.Contains("DRIFT") && f.Contains("MaskAttribute"));
    }

    /// <summary>A 类行引用不存在的等价对编号 → 必红（编号引用完整性）。</summary>
    [Fact]
    public void SelfProof_ARowWithUnknownEquivalenceId_Detected()
    {
        var mechanical = EnumerateCurrentSurface();
        var mutated = CapabilitySurfaceRegistry.Rows
            .Select(r => r.AttributeType == "MapFromAttribute" && r.Member == null
                ? r with { Registration = "A-999" }
                : r)
            .ToList();

        var failures = RunCheck4(mechanical, mutated, EquivalenceRegistry.AllIds);

        Assert.Contains(failures, f => f.Contains("A-999"));
    }

    // ----------------------------------------------------------------
    // 纯核查逻辑（真实核查 + self-proof 共用——单一代码路径）
    // ----------------------------------------------------------------

    /// <summary>
    /// Check 4 逻辑：机械面 ↔ 登记表逐行比对 + 分类语义校验。
    /// 返回失败清单（空 = 通过）。
    /// </summary>
    internal static List<string> RunCheck4(
        IReadOnlyList<(string AttributeType, string? Member, string Descriptor)> mechanical,
        IReadOnlyList<CapabilitySurfaceRow> registry,
        HashSet<string> equivalenceIds)
    {
        var failures = new List<string>();
        var mechanicalKeys = new HashSet<(string, string?)>(
            mechanical.Select(r => (r.AttributeType, r.Member)));
        var registryKeys = new HashSet<(string, string?)>(
            registry.Select(r => (r.AttributeType, r.Member)));

        // 登记表键唯一性（重复登记行 = 维护错误）
        var duplicates = registry
            .GroupBy(r => (r.AttributeType, r.Member))
            .Where(g => g.Count() > 1);
        foreach (var dup in duplicates)
            failures.Add($"DUPLICATE: 登记行 ({dup.Key.AttributeType}, {dup.Key.Member ?? "<type>"}) 重复登记 {dup.Count()} 行，须合并为 1 行");

        // 正向：每机械行必须有登记行（新增特性/参数/未登记 → 红）
        foreach (var m in mechanical)
        {
            if (!registryKeys.Contains((m.AttributeType, m.Member)))
                failures.Add($"UNREGISTERED: 机械面行 ({m.AttributeType}, {m.Member ?? "<type>"}) [{m.Descriptor}] 无人工登记行——新增特性面必须先在 CapabilitySurfaceRegistry 登记（A/B/C/N-A 分类），C 类缺口须挂追踪任务号");
        }

        // 反向：每登记行必须有机械行对应（特性/参数已删除未清表 → 红）
        foreach (var r in registry)
        {
            if (!mechanicalKeys.Contains((r.AttributeType, r.Member)))
                failures.Add($"STALE: 登记行 ({r.AttributeType}, {r.Member ?? "<type>"}) 在机械面中不存在（特性/参数已删除？）——清理登记行");
        }

        foreach (var r in registry)
        {
            if (!mechanicalKeys.Contains((r.AttributeType, r.Member))) continue;

            // 漂移检测：AttributeUsage ValidOn / 属性类型变化未重新登记 → 红
            var m = mechanical.First(x => x.AttributeType == r.AttributeType && x.Member == r.Member);
            if (!string.Equals(m.Descriptor, r.Descriptor, StringComparison.Ordinal))
                failures.Add($"DRIFT: 登记行 ({r.AttributeType}, {r.Member ?? "<type>"}) 描述符漂移——登记 [{r.Descriptor}] vs 机械 [{m.Descriptor}]（AttributeUsage/属性类型变化须重新登记）");

            switch (r.Class)
            {
                case CapabilitySurfaceClass.A:
                case CapabilitySurfaceClass.B:
                    if (!equivalenceIds.Contains(r.Registration))
                        failures.Add($"INVALID: 登记行 ({r.AttributeType}, {r.Member ?? "<type>"}) {r.Class} 类引用编号 '{r.Registration}' 不在 EquivalenceRegistry 中");
                    break;
                case CapabilitySurfaceClass.C:
                    if (!Regex.IsMatch(r.Registration, @"^T-\d{3}$"))
                        failures.Add($"INVALID: 登记行 ({r.AttributeType}, {r.Member ?? "<type>"}) C 类缺口必须挂追踪任务号（形如 T-008），当前 '{r.Registration}'——禁止隐瞒豁免（评审 J-5.3）");
                    break;
                case CapabilitySurfaceClass.NA:
                    if (string.IsNullOrWhiteSpace(r.Registration))
                        failures.Add($"INVALID: 登记行 ({r.AttributeType}, {r.Member ?? "<type>"}) N-A 行必须写明理由（Registration 字段）");
                    break;
                default:
                    failures.Add($"INVALID: 登记行 ({r.AttributeType}, {r.Member ?? "<type>"}) 未知分类 {r.Class}");
                    break;
            }
        }

        return failures;
    }

    /// <summary>当前产物程序集的机械面枚举（真实核查入口）。
    /// 枚举源 = NCMapper.Attributes 命名空间全部 Attribute 派生类型 + Fluent 通道锚点 MapperConfigAttribute
    ///（位于 NCMapper 命名空间——任务书「10 文件 + MapperConfigAttribute」口径）。</summary>
    internal static List<(string AttributeType, string? Member, string Descriptor)> EnumerateCurrentSurface()
        => EnumerateAttributeSurface(
            typeof(MapFromAttribute).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(Attribute))
                    && (t.Namespace == "NCMapper.Attributes" || t == typeof(MapperConfigAttribute)))
                .OrderBy(t => t.Name, StringComparer.Ordinal));

    /// <summary>
    /// 机械面枚举纯函数：每特性类型产 type-level 行（Descriptor=[AttributeUsage] ValidOn 归一化）
    /// ＋ 每公共实例属性（DeclaredOnly）产 member 行（Descriptor=属性类型名）。
    /// </summary>
    internal static List<(string AttributeType, string? Member, string Descriptor)> EnumerateAttributeSurface(
        IEnumerable<Type> attributeTypes)
    {
        var rows = new List<(string, string?, string)>();
        foreach (var type in attributeTypes)
        {
            var usage = type.GetCustomAttribute<AttributeUsageAttribute>(inherit: false);
            var validOn = usage == null ? "" : NormalizeValidOn(usage.ValidOn);
            rows.Add((type.Name, null, validOn));

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .OrderBy(p => p.Name, StringComparer.Ordinal);
            foreach (var prop in props)
                rows.Add((type.Name, prop.Name, prop.PropertyType.Name));
        }

        return rows
            .OrderBy(r => r.Item1, StringComparer.Ordinal)
            .ThenBy(r => r.Item2 ?? "", StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>AttributeTargets 归一化："Assembly, Class" → "Assembly|Class"（Flags 组合稳定化）。</summary>
    internal static string NormalizeValidOn(AttributeTargets targets)
        => string.Join("|", targets.ToString().Split(',').Select(s => s.Trim()));

    // ----------------------------------------------------------------
    // Check 5（P5，004）：
    // 能力标签分层完整性（主校验）+ 锚点列辅助校验（弱校验）
    // ----------------------------------------------------------------

    /// <summary>三桶扫描根（相对仓库根）与桶名——004 分桶口径。</summary>
    private static readonly (string Dir, string Bucket)[] TraitBucketRoots =
    {
        ("test/NCMapper.Tests", "L3"),
        ("test/NCMapper.IntegrationTests", "L4"),
        ("test/NCMapper.SourceGen.Tests", "L2"),
    };

    private static readonly Regex CapabilityTraitRegex = new(
        "\\[Trait\\(\"Capability\",\\s*\"(?<id>(A|B)-\\d{2})\"\\)\\]",
        RegexOptions.Compiled);

    /// <summary>仓库根定位：从程序集运行目录向上找含 src/NCMapper 的祖先目录。</summary>
    internal static string FindRepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "src", "NCMapper")))
                return dir.FullName;
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("仓库根定位失败（向上未找到 src/NCMapper 祖先目录）");
    }

    /// <summary>
    /// 能力标签分层扫描（真实核查数据源）：正则统计三桶源文件
    /// <c>[Trait("Capability","X-xx")]</c> 出现次数，按桶归类（004 -②a）。
    /// </summary>
    internal static Dictionary<string, Dictionary<string, int>> ScanCapabilityTraits(string repoRoot)
    {
        var scan = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (dir, bucket) in TraitBucketRoots)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var root = System.IO.Path.Combine(repoRoot, dir.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (System.IO.Directory.Exists(root))
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(root, "*.cs", System.IO.SearchOption.AllDirectories))
                {
                    if (file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}")
                        || file.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}"))
                        continue; // 产物/生成文件剔除
                    foreach (Match m in CapabilityTraitRegex.Matches(System.IO.File.ReadAllText(file)))
                    {
                        var id = m.Groups["id"].Value;
                        counts[id] = counts.TryGetValue(id, out var c) ? c + 1 : 1;
                    }
                }
            }
            scan[bucket] = counts;
        }
        return scan;
    }

    /// <summary>
    /// Check 5 纯核查逻辑（真实核查与 self-proof 自证共用——防假测试 #38 同路径纪律）：
    /// <para>a) 能力标签分层完整性：A 类能力全集 = 登记表 A 行 distinct Registration；
    /// B 类能力全集 = EquivalenceRegistry B 行（Fluent 独有）。每 A 类 L3 桶 ≥1 且 L4 桶 ≥1；
    /// 每 B 类 L3 桶 ≥1（004 -②a 主校验）。</para>
    /// <para>b) 锚点列辅助校验：按 Registration 归组，每组每列（EndToEndAnchor/OrmAnchor）
    /// ≥1 行非空或显式 "N/A:&lt;理由&gt;" 退出格；值形态校验——"文件::方法" → 文件存在（仓库根相对）
    /// 且文件文本含方法名（弱校验，方法重命名须同步登记）；"T-" → 开放追踪清单（信息面不判红，
    /// 收口目标 = 清零）；"—" → A/B 组红（仅 B/N-A 行允许——004）。</para>
    /// </summary>
    internal static List<string> RunCheck5(
        IReadOnlyList<CapabilitySurfaceRow> registry,
        Dictionary<string, Dictionary<string, int>> traitScan,
        string repoRoot,
        out List<string> openTrackings)
    {
        var failures = new List<string>();
        openTrackings = new List<string>();

        var aIds = registry.Where(r => r.Class == CapabilitySurfaceClass.A)
            .Select(r => r.Registration).Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var bIds = EquivalenceRegistry.AllCapabilities
            .Where(c => c.Id.StartsWith("B-", StringComparison.Ordinal))
            .Select(c => c.Id).Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        void AssertLayer(string id, string bucket, string projectDesc)
        {
            var count = traitScan.TryGetValue(bucket, out var b) && b.TryGetValue(id, out var c) ? c : 0;
            if (count == 0)
                failures.Add($"Check5 分层缺口：能力 {id} 在 {projectDesc}（{bucket} 桶）无 [Trait(\"Capability\",\"{id}\")] 标注");
        }

        foreach (var id in aIds)
        {
            AssertLayer(id, "L3", "NCMapper.Tests");
            AssertLayer(id, "L4", "NCMapper.IntegrationTests");
        }
        foreach (var id in bIds)
            AssertLayer(id, "L3", "NCMapper.Tests");

        foreach (var g in registry
                     .Where(r => r.Class == CapabilitySurfaceClass.A || r.Class == CapabilitySurfaceClass.B)
                     .GroupBy(r => r.Registration, StringComparer.Ordinal))
        {
            foreach (var (column, getAnchor) in new (string, Func<CapabilitySurfaceRow, string?>)[]
                     {
                         ("EndToEndAnchor", r => r.EndToEndAnchor),
                         ("OrmAnchor", r => r.OrmAnchor),
                     })
            {
                var values = g.Select(getAnchor)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.Ordinal).ToList();
                if (values.Count == 0)
                {
                    failures.Add($"Check5 锚点缺口：能力 {g.Key} 无任何 {column} 非空行（文件::方法 / T-追踪号 / \"N/A:<理由>\" 必居其一）");
                    continue;
                }
                foreach (var anchor in values)
                {
                    if (anchor.StartsWith("N/A:", StringComparison.Ordinal))
                        continue; // 规则性退出格（理由随值登记）
                    if (anchor == "—")
                    {
                        failures.Add($"Check5 锚点失效：能力 {g.Key}.{column} = \"—\"（占位形态仅 B/N-A 行允许，A 类组不可）");
                        continue;
                    }
                    if (anchor.StartsWith("T-", StringComparison.Ordinal))
                    {
                        openTrackings.Add($"{g.Key}.{column} = {anchor}");
                        continue;
                    }
                    var sep = anchor.IndexOf("::", StringComparison.Ordinal);
                    if (sep <= 0)
                    {
                        failures.Add($"Check5 锚点形态非法：能力 {g.Key}.{column} = \"{anchor}\"（应为 文件相对路径::方法名 / T-追踪号 / \"N/A:<理由>\"）");
                        continue;
                    }
                    var relFile = anchor[..sep].Replace('/', System.IO.Path.DirectorySeparatorChar);
                    var method = anchor[(sep + 2)..];
                    var full = System.IO.Path.Combine(repoRoot, relFile);
                    if (!System.IO.File.Exists(full))
                        failures.Add($"Check5 锚点失效：能力 {g.Key}.{column} 文件不存在 {anchor[..sep]}");
                    else if (!System.IO.File.ReadAllText(full).Contains(method, StringComparison.Ordinal))
                        failures.Add($"Check5 锚点失效：能力 {g.Key}.{column} 方法名 {method} 未出现在 {anchor[..sep]}（方法重命名须同步登记）");
                }
            }
        }
        return failures;
    }

    [Fact]
    public void Check5_CapabilityTagLayering_AndAnchors()
    {
        var repoRoot = FindRepoRoot();
        var failures = RunCheck5(CapabilitySurfaceRegistry.Rows, ScanCapabilityTraits(repoRoot), repoRoot, out var openTrackings);
        Assert.True(failures.Count == 0,
            "Check 5 FAILED:\n" + string.Join("\n", failures)
            + (openTrackings.Count > 0
                ? "\n开放追踪清单（T- 形态锚点——收口目标 = 清零）:\n" + string.Join("\n", openTrackings)
                : ""));
    }

    [Fact]
    public void Check5_SelfProof_MissingL3Trait_Detected()
    {
        // 变异 1（004 -②）：移除 L3 桶一条能力计数 → 检出
        var repoRoot = FindRepoRoot();
        var scan = ScanCapabilityTraits(repoRoot);
        scan["L3"].Remove("A-10");
        var failures = RunCheck5(CapabilitySurfaceRegistry.Rows, scan, repoRoot, out _);
        Assert.Contains(failures, f => f.Contains("A-10") && f.Contains("L3"));
    }

    [Fact]
    public void Check5_SelfProof_AnchorToNonexistentMethod_Detected()
    {
        // 变异 2（004 -②）：锚点指向不存在的方法名 → 检出（弱校验逼同步登记）
        var repoRoot = FindRepoRoot();
        var rows = CapabilitySurfaceRegistry.Rows.Select(r =>
            r.Registration == "A-10" && r.Member == null
                ? r with { EndToEndAnchor = "test/NCMapper.Tests/Runtime/FluentRuntimeConvertTests.cs::NoSuchMethod_Check5Probe" }
                : r).ToList();
        var failures = RunCheck5(rows, ScanCapabilityTraits(repoRoot), repoRoot, out _);
        Assert.Contains(failures, f => f.Contains("A-10") && f.Contains("NoSuchMethod_Check5Probe"));
    }

    [Fact]
    public void Check5_SelfProof_AnchorToNonexistentFile_Detected()
    {
        // 变异 3（004 -②）：锚点指向不存在的文件 → 检出
        var repoRoot = FindRepoRoot();
        var rows = CapabilitySurfaceRegistry.Rows.Select(r =>
            r.Registration == "A-10" && r.Member == null
                ? r with { EndToEndAnchor = "test/No/Such/Directory/NoFile.cs::Something" }
                : r).ToList();
        var failures = RunCheck5(rows, ScanCapabilityTraits(repoRoot), repoRoot, out _);
        Assert.Contains(failures, f => f.Contains("A-10") && f.Contains("文件不存在"));
    }
}
