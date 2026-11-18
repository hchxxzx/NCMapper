namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// Channel support classification for equivalence capabilities.
/// </summary>
internal enum ChannelSupport
{
    Both,        // A-class: dual-channel equivalent pair
    FluentOnly,  // B-class: Fluent channel exclusive
    AttrOnly,    // C-class: Attribute channel exclusive
}

/// <summary>
/// Equivalence capability entry — (Id, Name, FixtureFile, Support) four fields.
/// No DeniedFields (v1.2 removed — scheme).
/// </summary>
internal sealed record EquivalenceCapability(string Id, string Name, string? FixtureFile, ChannelSupport Support);

/// <summary>
/// Equivalence capability registry — sole source of truth for CI gate checks 1/2/3 and fixture integrity.
/// 条目计数不在此登记（防漂移）——总数与 A/B/C 分类计数由
/// EquivalenceRegistryIntegrityTests.Registry_Has24Entries_23A_1B_0C 钉死
///（A 类须 fixture / B 类 Fluent 独有 / C 类已退役）。
///（历史计数漂移注记已删：计数以该测试为唯一口径；A-14/A-15 退役与
/// →A-25 转类的实数核写过程见各任务归档，不在本注留陈旧数字。）
/// scheme manifest finalized form; A-09 added by (C-01 → A-09 migration);
/// A-10/A-11/A-12 added by (C-02/C-04/C-05 → migration，等价机制第二批能力转类);
/// A-13 added by (C-03 → A-13 migration，等价机制第三批能力转类——
/// Fluent 通道 Compose 多源组合落地，C 类归零，AttrOnly 枚举退役保留为后续 X6);
/// A-17/A-18 added by (G2+D1——Fluent .Tree()/.Member(d).NoAutoInclude() 落地，
/// 红名单 5/5 清账);
/// A-19 added by (→ A-19 migration，等价机制第四批能力转类——
/// AfterMapping 双通道等价化，特性侧 [AfterMapping] 落地);
/// A-20 added by (→ A-20 migration，等价机制第五批能力转类——
/// BeforeMapping 双通道等价化，特性侧 [BeforeMapping] 落地，双钩子双通道等价化收口);
/// A-21 added by (MapWhen 条件守卫双通道等价化——第六批能力转类).
/// added by (MapCtorParam 显式构造器形参绑定——Fluent-only 等价能力，
/// 特性侧裁决 No-Go（Stripped 备案 X2）；等价形态 = record 同名场景
/// .MapCtorParam 直喂形参 ↔ 等价 .Map（同通道终端对），fixture 自定义 L1 机制/L2 文本断言).
/// A-25 added by (→ A-25 migration，等价机制第十一批能力转类——
/// 特性侧 [MapNoAutoMatch] 对级白名单落地，NoAutoInclude 承载字段由通道性 Stripped 转 Compared).
/// </summary>
internal static class EquivalenceRegistry
{
    internal static readonly IReadOnlyList<EquivalenceCapability> AllCapabilities = new EquivalenceCapability[]
    {
        // ---- A class: dual-channel equivalent pairs (fixture required; A-14/A-15 retired)
        //      条目计数由 EquivalenceRegistryIntegrityTests.Registry_Has24Entries_23A_1B_0C 钉死 ----
        new("A-01", "Map",              "EquivalenceTests.Map.cs",              ChannelSupport.Both),
        new("A-02", "Ignore",           "EquivalenceTests.Ignore.cs",            ChannelSupport.Both),
        new("A-03", "Mask",             "EquivalenceTests.Mask.cs",              ChannelSupport.Both),
        new("A-04", "Map",              "EquivalenceTests.Convert.cs",           ChannelSupport.Both),
        new("A-05", "MapTree",          "EquivalenceTests.MapTree.cs",           ChannelSupport.Both),
        new("A-06", "AutoMatchFlatten", "EquivalenceTests.AutoMatchFlatten.cs",  ChannelSupport.Both),
        new("A-07", "MapProperty",      "EquivalenceTests.MapProperty.cs",       ChannelSupport.Both),
        new("A-08", "ReverseDerive",     "EquivalenceTests.ReverseDerive.cs",    ChannelSupport.Both),
        new("A-09", "ConvertBack",      "EquivalenceTests.ConvertBack.cs",      ChannelSupport.Both),
        new("A-10", "Convert",         "EquivalenceTests.ConvertStatic.cs",     ChannelSupport.Both),
        new("A-11", "DirectionalIgnore","EquivalenceTests.ConvertStatic.cs",     ChannelSupport.Both),
        new("A-12", "IsFromJson",       "EquivalenceTests.ConvertStatic.cs",     ChannelSupport.Both),
        new("A-13", "Compose",          "EquivalenceTests.Compose.cs",           ChannelSupport.Both),
        // A-14/A-15 退役（S 侧锚定形态——IgnoreSource/ConvertBackSource 终端
        // 退役，实体侧（源侧锚定）形态为 019 Error 拦截对象（D1），双通道规则性退出；承载侧合法形态
        // 由 A-11（[MapIgnore(Direction)] ↔ .Ignore(dest, Direction)）/ A-09（[ConvertBack] ↔
        // .ConvertBack）覆盖（能力面定案 v1.1）
        new("A-16", "IndexSegment",     "EquivalenceTests.IndexSegment.cs",     ChannelSupport.Both),
        new("A-17", "TreeConfig",       "EquivalenceTests.TreeConfig.cs",       ChannelSupport.Both),
        new("A-18", "PropertyNoAutoInclude", "EquivalenceTests.PropertyNoAutoInclude.cs", ChannelSupport.Both),
        // → A-19 迁移（AfterMapping 双通道等价化——特性侧 [AfterMapping]
        // 落地，迁出条件兑现；等价 fixture EquivalenceTests.LifecycleHooks.cs）
        new("A-19", "AfterMapping",     "EquivalenceTests.LifecycleHooks.cs",   ChannelSupport.Both),
        // → A-20 迁移（BeforeMapping 双通道等价化——特性侧 [BeforeMapping]
        // 落地，迁出条件兑现；等价 fixture EquivalenceTests.LifecycleHooks.cs 同文件扩充，
        // 第五批能力转类——双钩子双通道等价化收口）
        new("A-20", "BeforeMapping",    "EquivalenceTests.LifecycleHooks.cs",   ChannelSupport.Both),
        // A-21 MapWhen 新增（条件守卫双通道等价化——特性 [MapWhen]
        // 与 Fluent .MapWhen 同批落地（Batch-1），pm.ConditionMethodRef 双侧同承载 P-e；
        // 等价 fixture EquivalenceTests.MapWhen.cs，第六批能力转类）
        new("A-21", "MapWhen",          "EquivalenceTests.MapWhen.cs",          ChannelSupport.Both),
        // A-22 MapSubclass 新增（多态子类型对登记双通道等价化——
        // 特性 [MapSubclass]（O-1 方案 1，Inherited=false）与 Fluent .MapSubclass 同批落地，
        // MappingSnapshot.SubclassPairs 双侧同承载 P-f（顶层"反射全集 − Stripped"自动 Compared）；
        // 等价 fixture EquivalenceTests.MapSubclass.cs，第七批能力登记）
        new("A-22", "MapSubclass",      "EquivalenceTests.MapSubclass.cs",      ChannelSupport.Both),
        // A-23 MapFormat 新增（格式化双通道等价化——特性 [MapFormat]
        // 与 Fluent .Format 双通道同承载 pm.FormatString（Batch-1 批内登记 + Batch-4
        // .Format 终端落地）；等价 fixture EquivalenceTests.MapFormat.cs，第九批能力登记；
        // CapabilitySurfaceRegistry Rows 3 行 C→A 迁移同批收口）
        new("A-23", "MapFormat",        "EquivalenceTests.MapFormat.cs",        ChannelSupport.Both),
        // A-24 MemberCombination 新增（成员级组合叠加双通道等价化——
        // 「来源 + 变换」维度合并通道（落地）双通道承载对齐：
        // 特性侧 = [MapProperty(源路径)] + [Mask]/[Convert]/[MapFormat]/[IsFromJson]/[MapWhen]
        // 同属性叠加；Fluent 侧 = .Member(d).From(s).<变换>() 配置段叠加。fixture
        // EquivalenceTests.MemberCombination.cs 五组合类 + 跨型/豁免门变体（评审 002 P1-3①
        // 通道对照硬性化——Batch-3 通道对照行承载，登记与承载同批——经验 #135）
        new("A-24", "MemberCombination", "EquivalenceTests.MemberCombination.cs", ChannelSupport.Both),
        // → A-25 迁移（对级白名单双通道等价化——特性侧 [MapNoAutoMatch]
        // 契约+ 提取链门+ 反射腿门落地，迁出条件（SnapshotFieldRegistry
        // 顶层 NoAutoInclude 行原文"Fluent 落地对级 NoAutoMatch（原 NoAutoInclude 改名）等价物"）兑现；
        // 等价 fixture EquivalenceTests.NoAutoMatch.cs（L1 快照字段级含 NoAutoInclude 参与比较 + L2 方法体
        // 文本级 + 运行时四象限/反向/写回段负例/NCMAP007 双零），第十一批能力转类）
        new("A-25", "NoAutoMatch",      "EquivalenceTests.NoAutoMatch.cs",      ChannelSupport.Both),

        // ---- B class: Fluent-only (single-channel exclusive) ----
        // NoAutoMatch 迁入 A-25（特性侧 [MapNoAutoMatch] 落地，
        // 对级白名单双通道等价化；承载字段 NoAutoInclude 由通道性 Stripped 转 Compared，同批翻转）
        // MapCtorParam 新增（显式构造器形参绑定——Fluent-only 等价能力，
        // 特性侧 No-Go（X2 Stripped 备案）；等价形态 = record 同名场景
        // .MapCtorParam 直喂形参 ↔ 等价 .Map（同通道终端对，L2 文本等价 + 绑定机制 L1 断言）；
        // fixture 带 [EqPair("B-04")] 直测——B 类不参与 Check1 强制（Traversal 直测文件同款登记）
 new("B-04", "MapCtorParam", "EquivalenceTests.MapCtorParam.cs", ChannelSupport.FluentOnly),

        // ---- C class: Attr-only（C 类归零——枚举退役保留为后续 X6 形态占位）----
        // C-01 ConvertBack 迁入 A-09（Fluent .ConvertBack 落地，等价机制首次能力转类）
        // C-02 StaticConverter 迁入 A-10 / C-04 DirectionalIgnore 迁入 A-11 / C-05 IsFromJson 迁入 A-12
        //（Fluent .Convert(原 ConvertUsing，20260904-002 更名)/.Ignore(dest,Direction)/.FromJson 落地，第二批能力转类）
        // C-03 Compose 迁入 A-13（Fluent .Compose<TSub> 落地，第三批能力转类，C 类归零）
        // 实体侧 [MapIgnore] 曾迁入 A-14 / 实体侧 [ConvertBack] 曾迁入 A-15（G5/G6）
        // ——A-14/A-15 于退役（S 侧锚定形态双通道规则性退出）
        // FromField 索引路径迁入 A-16（G7——Fluent lambda 索引段落地，红名单第 3 行关闭）
        // [MapTree] 树配置迁入 A-17 / 属性级 [MapNoAutoInclude] 迁入 A-18（G2+D1——
        // Fluent .Tree()/.NoAutoInclude(dest) 落地，红名单余 2 行关闭，5/5 清账；运行时承载为通道性 Stripped，
        // 行为等价经 fixture 运行时断言 + L2 方法体等价保证）
    };

    /// <summary>
    /// All registered capability IDs (for check 2 reverse lookup).
    /// </summary>
    internal static HashSet<string> AllIds => new(AllCapabilities.Select(c => c.Id), StringComparer.Ordinal);

    /// <summary>
    /// 全部已注册 fixture 文件（Check 2 supplementary 目录扫描的唯一注册数据源）——
    /// A 类能力条目 FixtureFile + 基础设施/直测类 fixture 文件。
    /// <para><c>EquivalenceTests.Traversal.cs</c> 为共享遍历门/ReverseOnly 门序
    /// 直测载体（无 A 类等价对），须登记为注册 fixture 文件才能过目录扫描核查（G5 核查 2）；
    /// 避免新增 16 条能力条目破坏 15 计数（12A/2B/1C 不变）。</para>
    /// </summary>
    internal static HashSet<string> RegisteredFixtureFiles => new(
        AllCapabilities
            .Select(c => c.FixtureFile)
            .Where(f => f != null)
            .Cast<string>()
            .Append("EquivalenceTests.Traversal.cs"),
        StringComparer.Ordinal);
}
