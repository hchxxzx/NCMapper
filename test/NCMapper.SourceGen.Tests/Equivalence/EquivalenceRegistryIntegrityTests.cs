using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// Registry integrity tests — check 1 (forward) + check 2 (reverse) + self-proofs.
/// scheme check rules:
///   Check 1 (forward): every A-class registry entry → fixture file exists on disk + [EqPair(Id)] method exists.
///   Check 2 (reverse): every [EqPair(Id)] method → Id ∈ registry; directory scan → no unregistered
///                      "EquivalenceTests.*.cs" fixture file (prefix filter keeps infrastructure files out).
/// Check logic lives in pure helpers (RunCheck1/RunCheck2/RunDirectoryScan) so the self-proof tests
/// exercise the same code path the real checks use (anti-fake-test, #22/#12.3).
/// </summary>
public class EquivalenceRegistryIntegrityTests
{
    // ----------------------------------------------------------------
    // Check 1 (forward): every A-class entry → fixture file exists + [EqPair] method exists
    // ----------------------------------------------------------------

    [Fact]
    public void Check1_Forward_EveryAClassEntry_HasFixtureFileAndEqPairMethod()
    {
        var eqPairIds = GetAllEqPairMethodIds();
        var failures = RunCheck1(EquivalenceRegistry.AllCapabilities, eqPairIds, EquivalenceDir);

        Assert.True(failures.Count == 0,
            "Check 1 FAILED:\n" + string.Join("\n", failures));
    }

    // ----------------------------------------------------------------
    // Check 2 (reverse): every [EqPair(Id)] method → Id ∈ registry
    // ----------------------------------------------------------------

    [Fact]
    public void Check2_Reverse_EveryEqPairMethod_RegisteredInRegistry()
    {
        var eqPairIds = GetAllEqPairMethodIds();
        var failures = RunCheck2(eqPairIds, EquivalenceRegistry.AllIds);

        Assert.True(failures.Count == 0,
            "Check 2 FAILED:\n" + string.Join("\n", failures));
    }

    // ----------------------------------------------------------------
    // Check 2 supplementary: directory scan — no unregistered "EquivalenceTests.*.cs" file
    // ----------------------------------------------------------------

    [Fact]
    public void Check2_Supplementary_NoUnregisteredFixtureFiles()
    {
        var onDisk = ScanFixtureFiles(EquivalenceDir);
        var registered = EquivalenceRegistry.RegisteredFixtureFiles;

        var failures = RunDirectoryScan(onDisk, registered);

        Assert.True(failures.Count == 0,
            "Check 2 supplementary FAILED:\n" + string.Join("\n", failures));
    }

    // ----------------------------------------------------------------
    // Self-proof (in-memory, #22/#12.3): simulate mutations through the SAME check helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Check 1 self-proof: a registry entry whose [EqPair] method is missing (simulated by Id rename)
    /// MUST be detected by RunCheck1. Proves check 1 is not vacuous.
    /// </summary>
    [Fact]
    public void SelfProof_Check1_DetectsEntryWithoutEqPairMethod()
    {
        var eqPairIds = GetAllEqPairMethodIds();

        // Simulate: A-01 registry entry's fixture lost its [EqPair] marker (Id no longer matches any method)
        var mutated = EquivalenceRegistry.AllCapabilities
            .Select(c => c.Id == "A-01"
                ? c with { Id = "A-01-renamed" }
                : c)
            .ToList();

        var failures = RunCheck1(mutated, eqPairIds, EquivalenceDir);

        Assert.Contains(failures, f => f.Contains("A-01-renamed"));
    }

    /// <summary>
    /// Check 2 self-proof: an orphaned [EqPair] Id (simulated) MUST be detected by RunCheck2.
    /// (Complements the real drill: commenting out a registry entry orphans the fixture's [EqPair] Id.)
    /// </summary>
    [Fact]
    public void SelfProof_Check2_DetectsOrphanedEqPairId()
    {
        var eqPairIds = GetAllEqPairMethodIds();
        Assert.Contains("A-01", eqPairIds); // precondition: A-01 method exists

        // Simulate: registry no longer contains A-01 (entry commented out) -> A-01 becomes orphaned
        var registryWithoutA01 = EquivalenceRegistry.AllIds;
        registryWithoutA01.Remove("A-01");

        var failures = RunCheck2(eqPairIds, registryWithoutA01);

        Assert.Contains(failures, f => f.Contains("A-01"));
    }

    /// <summary>
    /// Directory-scan self-proof: an unregistered fixture file (simulated) MUST be detected.
    /// (Complements the real drill: creating EquivalenceTests.Fake.cs must turn the scan red.)
    /// </summary>
    [Fact]
    public void SelfProof_DirectoryScan_DetectsUnregisteredFixtureFile()
    {
        var registered = EquivalenceRegistry.RegisteredFixtureFiles;

        // Simulate: a new fixture file appears on disk without a registry entry
        var onDiskWithFake = ScanFixtureFiles(EquivalenceDir)
            .Append("EquivalenceTests.Fake.cs")
            .ToHashSet(StringComparer.Ordinal);

        var failures = RunDirectoryScan(onDiskWithFake, registered);

        Assert.Contains(failures, f => f.Contains("EquivalenceTests.Fake.cs"));
    }

    // ----------------------------------------------------------------
    // Registry entry count verification (24 total)
    // ----------------------------------------------------------------

    [Fact]
    public void Registry_Has24Entries_23A_1B_0C()
    {
        // C-01 → A-09 迁移（ConvertBack 双通道等价化）
        // C-02 → A-10 / C-04 → A-11 / C-05 → A-12 迁移（第二批能力转类）
        // C-03 → A-13 迁移（Compose 双通道等价化，第三批能力转类，C 类归零）
        // A-14/A-15 新增（实体侧 [MapIgnore]/[ConvertBack] ↔ Fluent 终端，红名单前 2 行关闭）
        // A-14/A-15 退役（S 侧锚定形态双通道规则性退出；承载侧由 A-11/A-09 覆盖）
        // A-17/A-18 新增（[MapTree] 树配置/属性级 [MapNoAutoInclude] ↔ Fluent 终端，红名单 5/5 清账）
        // → A-19 迁移（AfterMapping 双通道等价化，第四批能力转类）
        // → A-20 迁移（BeforeMapping 双通道等价化，第五批能力转类——双钩子双通道等价化收口）
        // A-21 新增（MapWhen 条件守卫双通道等价化，第六批能力转类）
        // A-22 新增（MapSubclass 多态子类型对登记双通道等价化，第七批能力登记）
        // 新增（MapCtorParam 显式 ctor 形参绑定——Fluent-only 等价能力，
        //   特性侧 No-Go（X2）；record 同名场景 .MapCtorParam ↔ .Map 同通道终端等价，第八批能力登记）
        // A-23 新增（MapFormat 格式化双通道等价化，第九批能力登记——
        //   CapabilitySurfaceRegistry Rows 3 行 C→A 迁移同批收口）
        // A-24 新增（MemberCombination 成员级组合叠加双通道等价化，
        //   第十批能力登记——评审 002 P1-3① 通道对照硬性化承载，登记与承载同批——经验 #135）
        // → A-25 迁移（对级白名单 [MapNoAutoMatch] ↔ .NoAutoMatch()
        //   双通道等价化，第十一批能力转类；方法名按新计数订正——原名 Registry_Has23Entries_22A_1B_0C
        //   自 A-24 登记起已名实漂移（方案书 A.4 注 5 / 任务书 E4），N-1 实数核写同批修复）
        Assert.Equal(24, EquivalenceRegistry.AllCapabilities.Count);

        var aClass = EquivalenceRegistry.AllCapabilities.Where(c => c.Support == ChannelSupport.Both).ToList();
        var bClass = EquivalenceRegistry.AllCapabilities.Where(c => c.Support == ChannelSupport.FluentOnly).ToList();
        var cClass = EquivalenceRegistry.AllCapabilities.Where(c => c.Support == ChannelSupport.AttrOnly).ToList();

        Assert.Equal(23, aClass.Count);
        Assert.Equal(1, bClass.Count);
        Assert.Equal(0, cClass.Count);

        // A-25 归位断言：对级白名单能力双通道化 + fixture 绑定
        var a25 = Assert.Single(EquivalenceRegistry.AllCapabilities, c => c.Id == "A-25");
        Assert.Equal("NoAutoMatch", a25.Name);
        Assert.Equal("EquivalenceTests.NoAutoMatch.cs", a25.FixtureFile);
        Assert.Equal(ChannelSupport.Both, a25.Support);

        // 退役断言：转类后不得残留 FluentOnly 同能力条目（防"双登记"假绿——同 Name 复登即红）
 Assert.DoesNotContain(EquivalenceRegistry.AllCapabilities, c => c.Id == "");
        Assert.DoesNotContain(EquivalenceRegistry.AllCapabilities,
            c => c.Name == "NoAutoMatch" && c.Support == ChannelSupport.FluentOnly);

        // A-24 归位断言：MemberCombination 组合叠加双通道能力 + fixture 绑定（通道对照行）
        var a24 = Assert.Single(EquivalenceRegistry.AllCapabilities, c => c.Id == "A-24");
        Assert.Equal("MemberCombination", a24.Name);
        Assert.Equal("EquivalenceTests.MemberCombination.cs", a24.FixtureFile);
        Assert.Equal(ChannelSupport.Both, a24.Support);

        // A-23 归位断言：MapFormat 格式化能力双通道化 + fixture 绑定
        var a23 = Assert.Single(EquivalenceRegistry.AllCapabilities, c => c.Id == "A-23");
        Assert.Equal("MapFormat", a23.Name);
        Assert.Equal("EquivalenceTests.MapFormat.cs", a23.FixtureFile);
        Assert.Equal(ChannelSupport.Both, a23.Support);

        // 归位断言：MapCtorParam Fluent-only 等价能力 + fixture 绑定
 var b04 = Assert.Single(EquivalenceRegistry.AllCapabilities, c => c.Id == "B-04");
        Assert.Equal("MapCtorParam", b04.Name);
        Assert.Equal("EquivalenceTests.MapCtorParam.cs", b04.FixtureFile);
        Assert.Equal(ChannelSupport.FluentOnly, b04.Support);

        // A-16 归位断言：集合索引路径能力双通道化 + fixture 绑定
        var a16 = Assert.Single(EquivalenceRegistry.AllCapabilities, c => c.Id == "A-16");
        Assert.Equal("IndexSegment", a16.Name);
        Assert.Equal("EquivalenceTests.IndexSegment.cs", a16.FixtureFile);
        Assert.Equal(ChannelSupport.Both, a16.Support);

        // A-17 归位断言：树构建配置能力双通道化 + fixture 绑定（红名单第 4 行关闭）
        var a17 = Assert.Single(EquivalenceRegistry.AllCapabilities, c => c.Id == "A-17");
        Assert.Equal("TreeConfig", a17.Name);
        Assert.Equal("EquivalenceTests.TreeConfig.cs", a17.FixtureFile);
        Assert.Equal(ChannelSupport.Both, a17.Support);

        // A-18 归位断言：属性级 NoAutoInclude 能力双通道化 + fixture 绑定（红名单第 5 行关闭）
        var a18 = Assert.Single(EquivalenceRegistry.AllCapabilities, c => c.Id == "A-18");
        Assert.Equal("PropertyNoAutoInclude", a18.Name);
        Assert.Equal("EquivalenceTests.PropertyNoAutoInclude.cs", a18.FixtureFile);
        Assert.Equal(ChannelSupport.Both, a18.Support);

        // A-19 归位断言：AfterMapping 双通道等价化 + fixture 绑定
        var a19 = Assert.Single(EquivalenceRegistry.AllCapabilities, c => c.Id == "A-19");
        Assert.Equal("AfterMapping", a19.Name);
        Assert.Equal("EquivalenceTests.LifecycleHooks.cs", a19.FixtureFile);
        Assert.Equal(ChannelSupport.Both, a19.Support);

        // A-20 归位断言：BeforeMapping 双通道等价化 + fixture 绑定
        var a20 = Assert.Single(EquivalenceRegistry.AllCapabilities, c => c.Id == "A-20");
        Assert.Equal("BeforeMapping", a20.Name);
        Assert.Equal("EquivalenceTests.LifecycleHooks.cs", a20.FixtureFile);
        Assert.Equal(ChannelSupport.Both, a20.Support);

        // A-21 归位断言：MapWhen 条件守卫双通道等价化 + fixture 绑定
        var a21 = Assert.Single(EquivalenceRegistry.AllCapabilities, c => c.Id == "A-21");
        Assert.Equal("MapWhen", a21.Name);
        Assert.Equal("EquivalenceTests.MapWhen.cs", a21.FixtureFile);
        Assert.Equal(ChannelSupport.Both, a21.Support);

        // A-22 归位断言：MapSubclass 多态子类型对登记双通道等价化 + fixture 绑定
        var a22 = Assert.Single(EquivalenceRegistry.AllCapabilities, c => c.Id == "A-22");
        Assert.Equal("MapSubclass", a22.Name);
        Assert.Equal("EquivalenceTests.MapSubclass.cs", a22.FixtureFile);
        Assert.Equal(ChannelSupport.Both, a22.Support);

        // 退役断言（已迁出 B 类）
 Assert.DoesNotContain(EquivalenceRegistry.AllCapabilities, c => c.Id == "");
    }

    // ----------------------------------------------------------------
    // Pure check-logic helpers (single code path for real checks + self-proofs)
    // ----------------------------------------------------------------

    /// <summary>Check 1 logic: A-class entries must have a [EqPair(Id)] method and an existing fixture file.</summary>
    internal static List<string> RunCheck1(
        IEnumerable<EquivalenceCapability> capabilities, HashSet<string> eqPairIds, string equivalenceDir)
    {
        var failures = new List<string>();
        foreach (var entry in capabilities)
        {
            if (entry.FixtureFile == null) continue; // A-class only (fixture required)

            if (!eqPairIds.Contains(entry.Id))
                failures.Add($"A-class entry '{entry.Id}' ({entry.Name}) has no [EqPair] method. Expected fixture: {entry.FixtureFile}");

            var fixturePath = Path.Combine(equivalenceDir, entry.FixtureFile);
            if (!File.Exists(fixturePath))
                failures.Add($"A-class entry '{entry.Id}' ({entry.Name}) fixture file not found on disk: {fixturePath}");
        }
        return failures;
    }

    /// <summary>Check 2 logic: every [EqPair(Id)] method must map to a registry entry.</summary>
    internal static List<string> RunCheck2(IEnumerable<string> eqPairIds, HashSet<string> registryIds)
    {
        return eqPairIds
            .Where(id => !registryIds.Contains(id))
            .Select(id => $"[EqPair(\"{id}\")] method found but '{id}' is not in registry. Either register it or remove the [EqPair] attribute.")
            .ToList();
    }

    /// <summary>Check 2 supplementary logic: every on-disk fixture file must be registered.</summary>
    internal static List<string> RunDirectoryScan(HashSet<string> onDiskFiles, HashSet<string> registeredFiles)
    {
        return onDiskFiles
            .Where(f => !registeredFiles.Contains(f))
            .Select(f => $"Fixture file '{f}' exists in Equivalence/ but is not registered in EquivalenceRegistry. Register it or remove the file.")
            .ToList();
    }

    /// <summary>
    /// Scan the Equivalence/ source directory for fixture files ("EquivalenceTests.*.cs" prefix).
    /// Infrastructure files (comparer, fixture base, registry, tests) are excluded by the prefix filter.
    /// </summary>
    internal static HashSet<string> ScanFixtureFiles(string equivalenceDir)
    {
        return Directory.EnumerateFiles(equivalenceDir, "EquivalenceTests.*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Source directory of the Equivalence test files (compile-time path — works from any bin layout).
    /// </summary>
    private static string EquivalenceDir { get; } = Path.GetDirectoryName(GetSourceFilePath())!;

    private static string GetSourceFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    /// <summary>
    /// Scan test assembly for all [EqPair(Id)] attributes and return the set of IDs.
    /// </summary>
    private static HashSet<string> GetAllEqPairMethodIds()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<EqPairAttribute>();
                if (attr != null && !string.IsNullOrEmpty(attr.Id))
                    ids.Add(attr.Id);
            }
        }

        return ids;
    }
}
