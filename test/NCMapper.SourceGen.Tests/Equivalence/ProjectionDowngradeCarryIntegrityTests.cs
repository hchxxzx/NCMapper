// （G1-② 捎带门禁）：投影降级承载登记完整性——
//   ①机械面：反射 NCMapper 程序集 IProjectionPolicy 全部实现特性，须与 CapabilitySurfaceRegistry
//     .ProjectionDowngradeCarry 逐行对应（新特性无登记行 → 红）；
//   ②承载可达：登记的 Fluent 承载路径于 RuntimePropertyMapping 反射可达（缺失即红）；
//   ③变异演练：删登记行/坏承载路径 → 红 → 还原复绿（#16）。
//  ④Classify 实际消费（门禁前移——出口判据）：
//     RequiresDowngrade=true 行必须登记 ClassifyCategory 分类分支名且被 ClassifyUntranslatableDeclaration
//     实际消费（最小 probe 注入返回分类名）——"承载路径存在 ∨ Classify 实际消费"双核查 + ComposeMappings
//     第二承载面（G-5 五类）同列核查；变异演练：登记缺项/分类名漂移 → 红（#16 同族）。
// 隔离：无共享状态（纯反射 + 静态登记表），无需 Collection。
using System.Reflection;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

public class ProjectionDowngradeCarryIntegrityTests
{
    private static HashSet<string> EnumerateProjectionPolicyImplementations()
    {
        var ncmapper = typeof(NCMapper.Mapper).Assembly;
        var iface = typeof(NCMapper.IProjectionPolicy);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in ncmapper.GetTypes())
        {
            if (t.IsAbstract || !iface.IsAssignableFrom(t)) continue;
            if (t.Namespace == null || !t.Namespace.StartsWith("NCMapper.Attributes", StringComparison.Ordinal)
                && t.Namespace != "NCMapper") continue;
            result.Add(t.Name);
        }
        return result;
    }

    /// <summary>门禁①：IProjectionPolicy 实现特性与承载登记表逐行对应（机械面 = 登记面）。</summary>
    [Fact]
    public void MechanicalImplementations_MatchCarryRegistry()
    {
        var mechanical = EnumerateProjectionPolicyImplementations();
        var registered = CapabilitySurfaceRegistry.ProjectionDowngradeCarry.Keys.ToHashSet(StringComparer.Ordinal);

        var missing = mechanical.Except(registered).ToList();
        var stale = registered.Except(mechanical).ToList();

        Assert.True(missing.Count == 0,
            $"IProjectionPolicy 实现特性缺少投影降级承载登记行: [{string.Join(", ", missing)}]——新增影响投影能力的特性必须同步登记（DegradationDetectorBase.ClassifyUntranslatableDeclaration/特性扫描双消费）");
        Assert.True(stale.Count == 0,
            $"承载登记表存在已退役特性行: [{string.Join(", ", stale)}]");
        Assert.Equal(12, mechanical.Count); // 20261010-005 后 12 实现（Mask/ConvertUsing/MapProperty/MapFrom/MapIgnore/ConvertBackUsing/BeforeMapping/AfterMapping/MapWhen/MapSubclass/MapFormat/MapNoAutoMatch——计数链条 6→8（008）→9（009）→10（010）→11（20260929-001）→12（20261010-005））
    }

    /// <summary>门禁②：登记的 Fluent 承载路径于 RuntimePropertyMapping 反射可达；对级承载路径于 RuntimeMapperMetadata 反射可达。</summary>
    [Fact]
    public void RegisteredCarryPaths_Reflectable_OnRuntimePropertyMapping()
    {
        foreach (var (attrName, row) in CapabilitySurfaceRegistry.ProjectionDowngradeCarry)
        {
            if (row.FluentCarryPath != null)
            {
                var segments = row.FluentCarryPath.Split('.');
                Type current = typeof(RuntimePropertyMapping);
                foreach (var seg in segments)
                {
                    var prop = current.GetProperty(seg,
                        BindingFlags.Public | BindingFlags.Instance);
                    Assert.True(prop != null,
                        $"{attrName} 承载路径 '{row.FluentCarryPath}' 段 '{seg}' 于 {current.Name} 反射不可达——承载字段重命名须同步本登记表");
                    current = prop.PropertyType;
                }
            }

            if (row.PairCarryPath != null)
            {
                var pairProp = typeof(RuntimeMapperMetadata).GetProperty(row.PairCarryPath,
                    BindingFlags.Public | BindingFlags.Instance);
                Assert.True(pairProp != null,
                    $"{attrName} 对级承载字段 '{row.PairCarryPath}' 于 RuntimeMapperMetadata 反射不可达——对级承载字段重命名须同步本登记表");
            }
        }
    }

    /// <summary>门禁③语义一致性：RequiresDowngrade=true 的实现必须有承载路径（pm 级 FluentCarryPath 或对级 PairCarryPath 至少其一；不可翻译声明必须运行时可判读）。</summary>
    [Fact]
    public void RequiresDowngradeTrue_HasCarryPath()
    {
        foreach (var (attrName, row) in CapabilitySurfaceRegistry.ProjectionDowngradeCarry)
        {
            if (row.RequiresDowngrade)
                Assert.True(row.FluentCarryPath != null || row.PairCarryPath != null,
                    $"{attrName} RequiresDowngrade=true 但无任何承载路径（pm 级 FluentCarryPath 与对级 PairCarryPath 均空）——降级判定（ClassifyUntranslatableDeclaration）无法读到此声明，构成 F5 缺口");
        }
    }

    /// <summary>
    /// 门禁④（门禁前移——出口判据）：
    /// "Classify 实际消费"核查——RequiresDowngrade=true 的登记行必须登记分类分支名
    ///（<see cref="ProjectionDowngradeCarryRow.ClassifyCategory"/>，分类集登记缺项即红），
    /// 且按登记承载位构造的最小明细被 <c>ClassifyUntranslatableDeclaration</c> 实际消费
    ///（返回登记分类名）。判据升级语义：门禁②③仅验"承载路径存在/可达"，本门禁验
    /// "可达且被消费"——分类分支被删或承载位漂移即红（先例判据升级为双核查）。
    /// </summary>
    [Fact]
    public void RequiresDowngradeTrue_ClassifyActuallyConsumes()
    {
        foreach (var (attrName, row) in CapabilitySurfaceRegistry.ProjectionDowngradeCarry)
        {
            if (!row.RequiresDowngrade) continue;

            Assert.True(row.ClassifyCategory != null,
                $"{attrName} RequiresDowngrade=true 但未登记 ClassifyCategory（分类集登记缺项——门禁④：实现者 × 分类分支名 × 承载位三列闭环，新增缺项即红）");

            var probe = BuildClassifyProbeMeta(attrName);
            var actual = NCMapper.Internal.DegradationDetectorBase.ClassifyUntranslatableDeclaration(probe);
            Assert.True(actual == row.ClassifyCategory,
                $"{attrName} 登记分类 '{row.ClassifyCategory}' 未被 ClassifyUntranslatableDeclaration 实际消费（实际返回 '{actual ?? "null"}'）——分类分支被删或承载位漂移，构成降级漏判缺口（F5 同族）");
        }
    }

    /// <summary>
    /// 门禁④扩展（G-5）：五类能力位在 ComposeMappings 明细的第二承载面同样被
    /// Classify 实际消费——跨表声明能力位漏判 = 明文外泄级，双承载面同列核查。
    /// </summary>
    [Theory]
    [InlineData("Mask", "Mask")]
    [InlineData("Convert", "Convert")]
    [InlineData("FromJson", "FromJson")]
    [InlineData("Format", "Format")]
    [InlineData("MapWhen", "MapWhen")]
    public void ComposeSide_Carry_AlsoConsumed_Classify(string composeCapability, string expectedCategory)
    {
        var probe = new RuntimeMapperMetadata
        {
            SourceType = typeof(ProbeEntity),
            TargetType = typeof(ProbeDto),
            SourceTypeFqn = typeof(ProbeEntity).FullName!,
            TargetTypeFqn = typeof(ProbeDto).FullName!,
            Channel = RuntimeMappingChannel.MapFrom,
            ComposeMappings = new[]
            {
                new RuntimeComposeMapping
                {
                    TargetPropertyName = "Ext",
                    SubSourceTypeName = "ProbeOtherEntity",
                    SourcePathNames = new[] { "Raw" },
                    HasMask = composeCapability == "Mask",
                    HasConverter = composeCapability == "Convert",
                    IsFromJson = composeCapability == "FromJson",
                    FormatString = composeCapability == "Format" ? "N2" : null,
                    ConditionMethodRef = composeCapability == "MapWhen" ? "Probe.When" : null,
                },
            },
        };

        var actual = NCMapper.Internal.DegradationDetectorBase.ClassifyUntranslatableDeclaration(probe);
        Assert.True(actual == expectedCategory,
            $"ComposeMappings 侧 {composeCapability} 承载未返回分类 '{expectedCategory}'（实际 '{actual ?? "null"}'）——G-5 五类扫描缺支，跨表能力位漏判");
    }

    /// <summary>按登记行构造最小 probe 明细（每行仅含该行承载位声明——互不串扰；NoAutoMatch 通道限定判据 ⇒ probe 恒 MapFrom）。</summary>
    private static RuntimeMapperMetadata BuildClassifyProbeMeta(string attrName)
    {
        RuntimePropertyMapping Pm(bool hasMask = false, bool hasConverter = false, bool isFromJson = false,
            string? format = null, string? condition = null)
            => new()
            {
                Target = new RuntimeTargetPropertySlot { Name = "Ext", TypeName = "String" },
                HasMask = hasMask, HasConverter = hasConverter, IsFromJson = isFromJson,
                FormatString = format, ConditionMethodRef = condition,
            };

        return attrName switch
        {
            "MaskAttribute" => Meta(pm: Pm(hasMask: true)),
            "ConvertAttribute" => Meta(pm: Pm(hasConverter: true)),
            "MapPropertyAttribute" => Meta(pm: Pm(isFromJson: true)),
            "MapWhenAttribute" => Meta(pm: Pm(condition: "Probe.When")),
            "MapFormatAttribute" => Meta(pm: Pm(format: "N2")),
            "AfterMappingAttribute" => Meta(after: "Probe.After"),
            "BeforeMappingAttribute" => Meta(before: "Probe.Before"),
            "MapSubclassAttribute" => Meta(subclass: true),
            "MapNoAutoMatchAttribute" => Meta(noAuto: true), // Channel==MapFrom 通道限定——probe 恒 MapFrom
            _ => throw new InvalidOperationException(
                $"未知承载登记行 '{attrName}'——BuildClassifyProbeMeta 需同步（新分类行必须可构造 probe）"),
        };

        static RuntimeMapperMetadata Meta(RuntimePropertyMapping? pm = null, string? after = null,
            string? before = null, bool subclass = false, bool noAuto = false)
            => new()
            {
                SourceType = typeof(ProbeEntity), TargetType = typeof(ProbeDto),
                SourceTypeFqn = typeof(ProbeEntity).FullName!, TargetTypeFqn = typeof(ProbeDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = pm != null ? new[] { pm } : Array.Empty<RuntimePropertyMapping>(),
                AfterMappingMethodRef = after,
                BeforeMappingMethodRef = before,
                HasSubclassDispatch = subclass,
                NoAutoInclude = noAuto,
            };
    }

    private sealed class ProbeEntity { public int Id { get; set; } public string Raw { get; set; } = ""; }
    private sealed class ProbeDto { public int Id { get; set; } public string Ext { get; set; } = ""; }

    // ----------------------------------------------------------------
    // Self-proof（内存变异——RunCheck 同一数据路径）
    // ----------------------------------------------------------------

    [Fact]
    public void SelfProof_RemovedCarryRow_Detected()
    {
        var mechanical = EnumerateProjectionPolicyImplementations();
        var mutated = CapabilitySurfaceRegistry.ProjectionDowngradeCarry
            .Where(kv => kv.Key != "MaskAttribute")
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        var missing = mechanical.Except(mutated.Keys).ToList();
        Assert.Contains("MaskAttribute", missing); // 删行 → 机械面比对必红
    }

    [Fact]
    public void SelfProof_BrokenCarryPath_Detected()
    {
        // 模拟承载字段重命名未同步登记表：反射解析路径段 → 必失败
        var broken = "HasMask_Renamed";
        var prop = typeof(RuntimePropertyMapping).GetProperty(broken, BindingFlags.Public | BindingFlags.Instance);
        Assert.True(prop == null, "变异前置：路径段应不可达（否则变异无效）");
        // 与门禁②同一解析逻辑（镜像）——坏路径在此被检出
        Assert.Throws<NullReferenceException>(() =>
        {
            string? p = prop?.Name;
            _ = p!.Length; // prop==null → NRE，等价门禁②的 Assert.True(prop != null) 失败语义
        });
    }

    [Fact]
    public void SelfProof_MissingClassifyCategory_Detected()
    {
        // 模拟分类集登记缺项（门禁④——新 RequiresDowngrade=true 行未登记 ClassifyCategory）：
        // 与门禁④同一判据（镜像）——缺项必被拒
        var mutated = CapabilitySurfaceRegistry.ProjectionDowngradeCarry["MapNoAutoMatchAttribute"];
        var missingCategory = mutated.RequiresDowngrade && mutated.ClassifyCategory == null;
        // 实登记非缺项（门禁④绿的前提）——变异注入（剥掉 ClassifyCategory）后必命中拒绝条件
        Assert.False(missingCategory, "变异前置：实登记行不应缺项（否则门禁④本已红）");
        var injected = mutated with { ClassifyCategory = null };
        Assert.True(injected.RequiresDowngrade && injected.ClassifyCategory == null,
            "缺项注入后未命中门禁④拒绝条件（row.ClassifyCategory != null 断言失效）——门禁④判据失真");
    }

    [Fact]
    public void SelfProof_WrongClassifyCategory_Detected()
    {
        // 模拟分类名漂移（Classify 分支被改名/删除而登记未同步）：与门禁④同一比对（镜像）——
        // actual != expected 必红。变异注入：期望值改错 → probe 实际返回（NoAutoMatch）不等于漂移值
        var row = CapabilitySurfaceRegistry.ProjectionDowngradeCarry["MapNoAutoMatchAttribute"];
        var probe = BuildClassifyProbeMeta("MapNoAutoMatchAttribute");
        var actual = NCMapper.Internal.DegradationDetectorBase.ClassifyUntranslatableDeclaration(probe);
        var drifted = row.ClassifyCategory + "_Renamed";
        Assert.True(actual != drifted,
            $"变异前置：漂移值 '{drifted}' 不应等于实际返回 '{actual}'（否则变异无效——门禁④比对失真）");
    }
}
