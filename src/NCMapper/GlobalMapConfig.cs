using NCMapper.Attributes;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using NCMapper.Internal;
namespace NCMapper;

/// <summary>
/// 全局映射配置 — 进程级单实例冻结配置模型（internal，运行时路径统一经
/// <see cref="GetEffective"/> 读取冻结配置）。
/// <para>两段式注册语义（方案 v1.2 W2/G4/I2）：</para>
/// <list type="number">
/// <item><b>注册阶段</b>（<see cref="Register"/> 调用时）：未冻结 → 记录（配置内容 + 来源程序集名）；
/// 已冻结且内容相同 → 幂等无操作；已冻结且内容不同 → 当场抛 <see cref="MapConfigurationException"/>。</item>
/// <item><b>冻结阶段</b>（首次映射使用 / <see cref="Mapper.Validate()"/> 触发）：清单排序规范化后统一比较；
/// 全部相同 → 冻结；存在不同 → 抛异常并列出冲突程序集清单；NamingStrategy 取值校验 0-3。</item>
/// </list>
/// <para>读取通道：① 生成代码快照（<see cref="Register"/> 已注册）→ ② 反射回退
/// （扫描已加载程序集的 [assembly: MapDefaults]）→ ③ 内建默认（等价于现状行为）。</para>
/// <para>冻结后只读不可变，不提供任何修改入口；<see cref="Mapper.ClearCache"/> 可重置冻结状态
/// （测试场景专用，经 <see cref="Reset"/> 调用）。</para>
/// </summary>
internal sealed class GlobalMapConfig
{
    // CacheRegistry 登记制自注册：仅登记清理委托，不触发缓存构建（防自触发）
    static GlobalMapConfig()
    {
        CacheRegistry.Register("global-map-config", CacheLayer.Full, Reset);
    }

    /// <summary>
    /// 命名策略
    /// </summary>
    // <GENERATED:gmc-props>
    public NamingStrategy NamingStrategy { get; }

    /// <summary>
    /// 源属性名前缀剥离清单（冻结时排序规范化）
    /// </summary>
    public string[] Prefixes { get; }

    /// <summary>
    /// 源属性名后缀剥离清单（冻结时排序规范化）
    /// </summary>
    public string[] Postfixes { get; }

    /// <summary>
    /// 双向忽略类型全名清单（排序规范化）
    /// </summary>
    public string[] IgnoreTypeFullNames { get; }

    /// <summary>
    /// 双向忽略属性名清单（排序规范化）
    /// </summary>
    public string[] IgnoreNames { get; }

    /// <summary>
    /// 双向忽略属性名前缀清单（排序规范化）
    /// </summary>
    public string[] IgnoreNamePrefixes { get; }

    /// <summary>
    /// 双向忽略属性名后缀清单（排序规范化）
    /// </summary>
    public string[] IgnoreNameSuffixes { get; }

    /// <summary>
    /// ToEntity 方向额外忽略类型全名清单（排序规范化）
    /// </summary>
    public string[] IgnoreTypeFullNamesToEntity { get; }

    /// <summary>
    /// ToEntity 方向额外忽略属性名清单（排序规范化）
    /// </summary>
    public string[] IgnoreNamesToEntity { get; }

    /// <summary>
    /// ToEntity 方向额外忽略属性名前缀清单（排序规范化）
    /// </summary>
    public string[] IgnoreNamePrefixesToEntity { get; }

    /// <summary>
    /// ToEntity 方向额外忽略属性名后缀清单（排序规范化）
    /// </summary>
    public string[] IgnoreNameSuffixesToEntity { get; }
    // </GENERATED:gmc-props>

    /// <summary>
    /// 冻结配置的来源程序集名清单（冲突消息用；内建默认来源为空）
    /// </summary>
    public string[] SourceAssemblies { get; }

    /// <summary>
    /// 进程级冻结锁 — 保护注册表与冻结状态（Interlocked 双重检查基于 volatile 读）
    /// </summary>
    private static readonly object s_lock = new();

    /// <summary>
    /// 已冻结配置（volatile 保证多线程可见性；null = 未冻结）
    /// </summary>
    private static volatile GlobalMapConfig? s_frozen;

    /// <summary>
    /// 注册记录列表（锁内访问；清空见 <see cref="Reset"/>）
    /// </summary>
    private static readonly List<RegisteredConfig> s_registered = new();

    /// <summary>
    /// 内建默认配置（NamingStrategy.Default + 空清单，等价于现状行为）
    /// </summary>
    private static readonly GlobalMapConfig s_default = new(new GeneratedMapConfig(), Array.Empty<string>());

    /// <summary>
    /// 创建配置实例 — 仅冻结阶段调用；构造时校验 NamingStrategy 取值 0-3
    /// </summary>
    /// <param name="snapshot">规范化后的配置快照（清单数组由本实例独占，冻结后只读）</param>
    /// <param name="sourceAssemblies">来源程序集名清单</param>
    /// <exception cref="MapConfigurationException">NamingStrategy 越界时抛出</exception>
    private GlobalMapConfig(GeneratedMapConfig snapshot, string[] sourceAssemblies)
    {
        if (snapshot.NamingStrategy is < 0 or > 3)
        {
            throw new MapConfigurationException(
                ErrorMessages.NamingStrategyOutOfRange(
                    snapshot.NamingStrategy,
                    $"0-3，对应 {nameof(NamingStrategy.Default)}/{nameof(NamingStrategy.CaseSensitive)}/{nameof(NamingStrategy.SnakeCase)}/{nameof(NamingStrategy.UpperSnakeCase)}"));
        }
        // <GENERATED:gmc-ctor>
        NamingStrategy = (NamingStrategy)snapshot.NamingStrategy;
        Prefixes = snapshot.Prefixes;
        Postfixes = snapshot.Postfixes;
        IgnoreTypeFullNames = snapshot.IgnoreTypeFullNames;
        IgnoreNames = snapshot.IgnoreNames;
        IgnoreNamePrefixes = snapshot.IgnoreNamePrefixes;
        IgnoreNameSuffixes = snapshot.IgnoreNameSuffixes;
        IgnoreTypeFullNamesToEntity = snapshot.IgnoreTypeFullNamesToEntity;
        IgnoreNamesToEntity = snapshot.IgnoreNamesToEntity;
        IgnoreNamePrefixesToEntity = snapshot.IgnoreNamePrefixesToEntity;
        IgnoreNameSuffixesToEntity = snapshot.IgnoreNameSuffixesToEntity;
        // </GENERATED:gmc-ctor>
        SourceAssemblies = sourceAssemblies;
    }

    /// <summary>
    /// 注册记录 — 配置内容 + 来源程序集名（冲突消息用）
    /// </summary>
    private sealed class RegisteredConfig
    {
        /// <summary>
        /// 配置内容（原始，未规范化；比较时按需规范化）
        /// </summary>
        public GeneratedMapConfig Config { get; }

        /// <summary>
        /// 来源程序集名（生成器内联常量）
        /// </summary>
        public string AssemblyName { get; }

        /// <summary>
        /// 创建注册记录
        /// </summary>
        /// <param name="config">配置内容</param>
        /// <param name="assemblyName">来源程序集名</param>
        public RegisteredConfig(GeneratedMapConfig config, string assemblyName)
        {
            Config = config;
            AssemblyName = assemblyName;
        }
    }

    /// <summary>
    /// 获取生效配置 — 触发冻结（首次调用时裁决，幂等）
    /// <para>冻结时机：首次映射使用 / <see cref="Mapper.Validate()"/>；
    /// Lazy + Interlocked 双重保护，多线程首次竞争安全。</para>
    /// </summary>
    /// <returns>冻结后的全局映射配置（只读不可变）</returns>
    [RequiresUnreferencedCode("Freezing global config reflects over [assembly: MapDefaults] when no generated snapshot exists. Types annotated with [MapFrom] use the generated snapshot path with zero reflection.")]
    public static GlobalMapConfig GetEffective()
    {
        var frozen = s_frozen;
        if (frozen != null) return frozen;
        lock (s_lock)
        {
            if (s_frozen != null) return s_frozen;
            var result = FreezeCore();
            s_frozen = result;
            return result;
        }
    }

    /// <summary>
    /// 注册配置快照（源生成代码调用；手工调用违反单一事实来源原则——
    /// 测试/工具场景经 <see cref="Mapper.ClearCache"/> 重置冻结状态后可模拟生成器注册）
    /// <para>两段式注册语义：未冻结记录 / 已冻结同内容幂等 / 已冻结不同内容当场抛异常。</para>
    /// </summary>
    /// <param name="config">配置快照；null 表示不注册（等价于未配置）</param>
    /// <param name="assemblyName">来源程序集名（生成器内联常量，编译期确定，零反射）</param>
    /// <exception cref="MapConfigurationException">已冻结后注册不同内容时抛出</exception>
    public static void Register(GeneratedMapConfig? config, string assemblyName)
    {
        if (config == null) return;
        lock (s_lock)
        {
            var frozen = s_frozen;
            if (frozen != null)
            {
                if (ContentEquals(Normalize(config), frozen.ToSnapshot())) return;
                var frozenSources = frozen.SourceAssemblies.Length == 0
                    ? "<内建默认>"
                    : string.Join(", ", frozen.SourceAssemblies);
                throw new MapConfigurationException(
                    ErrorMessages.GlobalConfigConflict(assemblyName, frozenSources));
            }
            // Register 即 Normalize
            // 原存可变引用延迟规范化，注册后调用方继续修改 config 实例将改变冻结结果
            //（TOCTOU 语义陷阱）；存规范化副本即固化注册时刻的配置语义。
            s_registered.Add(new RegisteredConfig(Normalize(config), assemblyName));
        }
    }

    /// <summary>
    /// 获取当前冻结配置的只读视图 — 未冻结时先触发冻结（幂等，失败时抛
    /// <see cref="MapConfigurationException"/>），供调试、测试断言、日志使用。
    /// </summary>
    /// <returns>冻结配置的只读视图（不可变）</returns>
    [RequiresUnreferencedCode("GetView triggers config freeze, which reflects over [assembly: MapDefaults] when no generated snapshot exists.")]
    public static GlobalConfigView? GetView()
    {
        var frozen = GetEffective();
        return new GlobalConfigView(
            // <GENERATED:gmc-getview>
            frozen.NamingStrategy,
            frozen.Prefixes, frozen.Postfixes,
            frozen.IgnoreTypeFullNames, frozen.IgnoreNames,
            frozen.IgnoreNamePrefixes, frozen.IgnoreNameSuffixes,
            frozen.IgnoreTypeFullNamesToEntity, frozen.IgnoreNamesToEntity,
            frozen.IgnoreNamePrefixesToEntity, frozen.IgnoreNameSuffixesToEntity);
            // </GENERATED:gmc-getview>
    }

    /// <summary>
    /// 重置冻结状态（保留注册表）— 仅供 <see cref="Mapper.ClearCache"/> 级联调用。
    /// <para>只清 <c>s_frozen</c> 不清 <c>s_registered</c>
    /// 原全清语义导致 ClearCache 后已注册配置丢失，重新冻结经反射回退（无 [MapDefaults]）落到
    /// 内建默认（配置静默回退）。ClearCache 语义 = 清运行时缓存产物，
    /// 注册表是编译期资产须保持；行为变更见 CHANGELOG（迁移指引：依赖全清的测试改用
    /// <see cref="ResetForTests"/>）。</para>
    /// </summary>
    public static void Reset()
    {
        lock (s_lock)
        {
            s_frozen = null;
        }
    }

    /// <summary>
    /// 测试专用全清（清冻结状态 + 注册表）— 供测试隔离使用（原 Reset 全清语义，
    /// 收敛后由本显式入口承载；internal 经 InternalsVisibleTo 可见，不进公共 API 面）。
    /// </summary>
    internal static void ResetForTests()
    {
        lock (s_lock)
        {
            s_registered.Clear();
            s_frozen = null;
        }
    }

    /// <summary>
    /// 冻结裁决 — 读取通道顺序：生成代码快照 → 反射回退 → 内建默认
    /// <para>统一比较前对清单排序规范化（["a","b"] 与 ["b","a"] 判同）。</para>
    /// </summary>
    /// <returns>冻结后的配置实例</returns>
    [RequiresUnreferencedCode("Reflection fallback reads [assembly: MapDefaults] attributes. Assemblies with generated snapshots skip this path.")]
    private static GlobalMapConfig FreezeCore()
    {
        if (s_registered.Count > 0)
        {
            var first = Normalize(s_registered[0].Config);
            foreach (var item in s_registered.Skip(1))
            {
                if (!ContentEquals(Normalize(item.Config), first))
                    throw BuildConflictException(s_registered);
            }
            return new GlobalMapConfig(first, DistinctAssemblies(s_registered));
        }

        // 反射回退：扫描已加载程序集的 [assembly: MapDefaults]（仅无生成代码快照时触发）
        var fromAssemblies = new List<RegisteredConfig>();
        foreach (var assembly in SafeGetAssemblies())
        {
            MapDefaultsAttribute? attr;
            try
            {
                attr = assembly.GetCustomAttribute<MapDefaultsAttribute>();
            }
            catch (Exception ex)
            {
                // 程序集类型加载失败等异常 — 跳过该程序集，不阻断冻结
                // 保持静默降级但留痕可观测
                //（原裸 catch 零记录——坏程序集被跳过时无任何诊断线索）
                MapTracer.Write($"[NCMapper] GlobalMapConfig 冻结：程序集 '{assembly.GetName().Name ?? assembly.FullName ?? "<unknown>"}' 的 [MapDefaults] 特性查询异常，已跳过（{ex.GetType().Name}: {ex.Message}）");
                continue;
            }
            if (attr == null) continue;
            fromAssemblies.Add(new RegisteredConfig(
                FromAttribute(attr), assembly.GetName().Name ?? assembly.FullName ?? "<unknown>"));
        }
        if (fromAssemblies.Count > 0)
        {
            var first = Normalize(fromAssemblies[0].Config);
            foreach (var item in fromAssemblies.Skip(1))
            {
                if (!ContentEquals(Normalize(item.Config), first))
                    throw BuildConflictException(fromAssemblies);
            }
            return new GlobalMapConfig(first, DistinctAssemblies(fromAssemblies));
        }
        return s_default;
    }

    /// <summary>
    /// 安全获取已加载程序集 — 异常时返回空集合（防御性）
    /// </summary>
    /// <returns>已加载程序集集合</returns>
    private static IEnumerable<Assembly> SafeGetAssemblies()
    {
        try
        {
            return AppDomain.CurrentDomain.GetAssemblies();
        }
        catch (Exception ex)
        {
            // 保持静默降级（返回空集合走内建默认）但留痕可观测
            MapTracer.Write($"[NCMapper] GlobalMapConfig 冻结：获取已加载程序集列表异常，返回空集合走内建默认（{ex.GetType().Name}: {ex.Message}）");
            return Array.Empty<Assembly>();
        }
    }

    /// <summary>
    /// 从 [assembly: MapDefaults] 特性转换为快照载体（Type 转为全名）。用途约束：仅供 <see cref="FreezeCore"/> 反射回退分支与测试使用，不得用于绕过冻结流程。
    /// </summary>
    /// <param name="attr">程序集级特性</param>
    /// <returns>快照载体</returns>
    internal static GeneratedMapConfig FromAttribute(MapDefaultsAttribute attr)
    {
        return new GeneratedMapConfig
        {
            // <GENERATED:gmc-fromattr>
            NamingStrategy = (int)attr.NamingStrategy,
            Prefixes = attr.Prefixes ?? Array.Empty<string>(),
            Postfixes = attr.Postfixes ?? Array.Empty<string>(),
            IgnoreTypeFullNames = (attr.IgnoreTypes ?? Array.Empty<Type>())
                .Select(t => t.FullName ?? t.Name).ToArray(),
            IgnoreNames = attr.IgnoreNames ?? Array.Empty<string>(),
            IgnoreNamePrefixes = attr.IgnoreNamePrefixes ?? Array.Empty<string>(),
            IgnoreNameSuffixes = attr.IgnoreNameSuffixes ?? Array.Empty<string>(),
            IgnoreTypeFullNamesToEntity = (attr.IgnoreTypesToEntity ?? Array.Empty<Type>())
                .Select(t => t.FullName ?? t.Name).ToArray(),
            IgnoreNamesToEntity = attr.IgnoreNamesToEntity ?? Array.Empty<string>(),
            IgnoreNamePrefixesToEntity = attr.IgnoreNamePrefixesToEntity ?? Array.Empty<string>(),
            IgnoreNameSuffixesToEntity = attr.IgnoreNameSuffixesToEntity ?? Array.Empty<string>(),
            // </GENERATED:gmc-fromattr>
        };
    }

    /// <summary>
    /// 配置内容规范化 — 清单排序（Ordinal）后复制为全新快照
    /// </summary>
    /// <param name="config">原始配置</param>
    /// <returns>规范化后的快照（数组全新实例）</returns>
    private static GeneratedMapConfig Normalize(GeneratedMapConfig config)
    {
        return new GeneratedMapConfig
        {
            // <GENERATED:gmc-normalize>
            NamingStrategy = config.NamingStrategy,
            Prefixes = Normalized(config.Prefixes),
            Postfixes = Normalized(config.Postfixes),
            IgnoreTypeFullNames = Normalized(config.IgnoreTypeFullNames),
            IgnoreNames = Normalized(config.IgnoreNames),
            IgnoreNamePrefixes = Normalized(config.IgnoreNamePrefixes),
            IgnoreNameSuffixes = Normalized(config.IgnoreNameSuffixes),
            IgnoreTypeFullNamesToEntity = Normalized(config.IgnoreTypeFullNamesToEntity),
            IgnoreNamesToEntity = Normalized(config.IgnoreNamesToEntity),
            IgnoreNamePrefixesToEntity = Normalized(config.IgnoreNamePrefixesToEntity),
            IgnoreNameSuffixesToEntity = Normalized(config.IgnoreNameSuffixesToEntity),
            // </GENERATED:gmc-normalize>
        };
    }

    /// <summary>
    /// 清单排序规范化 — null/空 → 空数组
    /// </summary>
    /// <param name="items">原始清单</param>
    /// <returns>排序后的新数组</returns>
    private static string[] Normalized(string[]? items)
        => items == null || items.Length == 0
            ? Array.Empty<string>()
            : items.OrderBy(x => x, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// 内容相等比较（输入须为已规范化快照）
    /// </summary>
    /// <param name="a">快照 A</param>
    /// <param name="b">快照 B</param>
    /// <returns>内容相等返回 true</returns>
    private static bool ContentEquals(GeneratedMapConfig a, GeneratedMapConfig b)
    {
        // <GENERATED:gmc-contentequals>
        return a.NamingStrategy == b.NamingStrategy
            && SequenceEquals(a.Prefixes, b.Prefixes)
            && SequenceEquals(a.Postfixes, b.Postfixes)
            && SequenceEquals(a.IgnoreTypeFullNames, b.IgnoreTypeFullNames)
            && SequenceEquals(a.IgnoreNames, b.IgnoreNames)
            && SequenceEquals(a.IgnoreNamePrefixes, b.IgnoreNamePrefixes)
            && SequenceEquals(a.IgnoreNameSuffixes, b.IgnoreNameSuffixes)
            && SequenceEquals(a.IgnoreTypeFullNamesToEntity, b.IgnoreTypeFullNamesToEntity)
            && SequenceEquals(a.IgnoreNamesToEntity, b.IgnoreNamesToEntity)
            && SequenceEquals(a.IgnoreNamePrefixesToEntity, b.IgnoreNamePrefixesToEntity)
            && SequenceEquals(a.IgnoreNameSuffixesToEntity, b.IgnoreNameSuffixesToEntity);
            // </GENERATED:gmc-contentequals>
    }

    /// <summary>
    /// 字符串序列相等（Ordinal，输入已排序规范化）
    /// </summary>
    /// <param name="a">序列 A</param>
    /// <param name="b">序列 B</param>
    /// <returns>长度与逐项均相等返回 true</returns>
    private static bool SequenceEquals(string[] a, string[] b)
        => a.Length == b.Length && a.SequenceEqual(b, StringComparer.Ordinal);

    /// <summary>
    /// 提取冲突消息用程序集名清单（去重保序）
    /// </summary>
    /// <param name="records">注册记录</param>
    /// <returns>去重后的程序集名数组</returns>
    private static string[] DistinctAssemblies(List<RegisteredConfig> records)
        => records.Select(r => r.AssemblyName).Distinct().ToArray();

    /// <summary>
    /// 构建冲突异常 — 消息明示冲突程序集清单
    /// </summary>
    /// <param name="records">注册记录（冲突双方或多方）</param>
    /// <returns>冲突异常实例</returns>
    private static MapConfigurationException BuildConflictException(List<RegisteredConfig> records)
    {
        var names = string.Join(", ", DistinctAssemblies(records));
        return new MapConfigurationException(
            ErrorMessages.GlobalConfigMultiAssemblyConflict(names));
    }

    /// <summary>
    /// 导出规范化快照 — 供冻结后幂等比较复用（数组为实例内部引用，只读使用）
    /// </summary>
    /// <returns>规范化快照</returns>
    private GeneratedMapConfig ToSnapshot()
    {
        return new GeneratedMapConfig
        {
            // <GENERATED:gmc-tosnapshot>
            NamingStrategy = (int)NamingStrategy,
            Prefixes = Prefixes,
            Postfixes = Postfixes,
            IgnoreTypeFullNames = IgnoreTypeFullNames,
            IgnoreNames = IgnoreNames,
            IgnoreNamePrefixes = IgnoreNamePrefixes,
            IgnoreNameSuffixes = IgnoreNameSuffixes,
            IgnoreTypeFullNamesToEntity = IgnoreTypeFullNamesToEntity,
            IgnoreNamesToEntity = IgnoreNamesToEntity,
            IgnoreNamePrefixesToEntity = IgnoreNamePrefixesToEntity,
            IgnoreNameSuffixesToEntity = IgnoreNameSuffixesToEntity,
            // </GENERATED:gmc-tosnapshot>
        };
    }
}
