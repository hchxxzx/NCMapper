using System.Reflection;
using NCMapper.Attributes;
using System.Diagnostics.CodeAnalysis;
using NCMapper.Shared;
using NCMapper.Internal;
using NCMapper.Runtime;
namespace NCMapper;
/// <summary>
/// 映射验证结果级别
/// </summary>
public enum MapValidationLevel
{
    /// <summary>
    /// 正常 — 所有属性已映射
    /// </summary>
    OK,
    /// <summary>
    /// 警告 — 部分属性未映射
    /// </summary>
    Warn,
    /// <summary>
    /// 错误 — 类型不兼容或无可用映射
    /// </summary>
    Error
}

/// <summary>
/// 单个映射对的验证结果
/// </summary>
public sealed class MapValidationResult
{
    /// <summary>
    /// 源类型
    /// </summary>
    public Type SourceType { get; init; } = null!;
    /// <summary>
    /// 目标类型
    /// </summary>
    public Type TargetType { get; init; } = null!;
    /// <summary>
    /// 结果级别
    /// </summary>
    public MapValidationLevel Level { get; init; }
    /// <summary>
    /// 已映射的目标属性数
    /// </summary>
    public int MappedCount { get; init; }
    /// <summary>
    /// 目标属性总数（可写、非 MapIgnore）
    /// </summary>
    public int TotalCount { get; init; }
    /// <summary>
    /// 未映射的属性名列表
    /// </summary>
    public List<string> UnmappedProperties { get; init; } = new();
    /// <summary>
    /// 类型不兼容的属性详情
    /// </summary>
    public List<string> TypeMismatchProperties { get; init; } = new();
    /// <summary>
    /// 描述信息
    /// <para>Warn/Error 级时消息自带前导标点（zh 全角逗号 / en ASCII 逗号），消费方见
    /// MapValidationResult.ToString 报表行拼接与 Mapper.ValidateOrThrow 剥离（TrimStart 字符集）。</para>
    /// </summary>
    public string Message { get; init; } = "";

    /// <summary>
    /// 该映射对在 ORM 投影下是否将降级为内存映射
    /// <para>ORM 无关判定层口径（<c>DegradationDetectorBase.EvaluateChannelAgnostic</c>）——
    /// 仅呈现 ORM 无关判定层结论；ORM 特定检查（EFCore/FreeSql 各自规则）以各 ORM 检测器运行时为准。
    /// 降级标记不改变 <see cref="Level"/>（纯可翻译但投影降级的对 Level 仍为 OK）。</para>
    /// </summary>
    public bool RequiresProjectionDowngrade { get; init; }

    /// <summary>
    /// 投影降级原因（<see cref="RequiresProjectionDowngrade"/> 为 true 时非空；
    /// 文本与降级判定层逐字一致，报告呈现层不二次加工）
    /// </summary>
    public string ProjectionDowngradeReason { get; init; } = "";

    /// <summary>
    /// 携带降级维度重建结果行（internal）—— <see cref="MapValidationResult"/> 为 sealed class
    /// 且属性 init-only，无法 <c>with</c> 表达式；同值重建后填充降级字段
    /// （internal 成员不出现在 PublicApi 快照）
    /// </summary>
    /// <param name="requires">是否投影降级</param>
    /// <param name="reason">降级原因（requires 为 true 时非空）</param>
    /// <returns>降级维度已填充的新实例（其余字段与当前实例值等价）</returns>
    internal MapValidationResult WithProjectionDowngrade(bool requires, string reason) => new()
    {
        SourceType = SourceType,
        TargetType = TargetType,
        Level = Level,
        MappedCount = MappedCount,
        TotalCount = TotalCount,
        UnmappedProperties = UnmappedProperties,
        TypeMismatchProperties = TypeMismatchProperties,
        Message = Message,
        RequiresProjectionDowngrade = requires,
        ProjectionDowngradeReason = reason,
    };

    /// <summary>
    /// 友好的文本摘要
    /// </summary>
    /// <returns>包含图标、类型名、映射统计的格式化字符串</returns>
    public override string ToString()
    {
        var icon = Level switch
        {
            MapValidationLevel.OK => "✅",
            MapValidationLevel.Warn => "⚠️",
            MapValidationLevel.Error => "❌",
            _ => "?"
        };
        // 降级标记追加于行尾；不改变既有格式与 Level 语义
        var degradeMark = RequiresProjectionDowngrade ? ErrorMessages.ValidatorProjectionDowngradeMark() : "";
        return $"{icon} " + ErrorMessages.ValidatorReportLine(SourceType.Name, TargetType.Name, MappedCount, TotalCount) + $"{Message}{degradeMark}";
    }
}

/// <summary>
/// 映射验证器 — 启动时扫描程序集中所有 [MapFrom] 标注的 DTO，检查映射完整性
/// 默认开启，可通过 Mapper.ValidationEnabled = false 关闭
/// </summary>
public static class MapValidator
{
    // CacheRegistry 登记制自注册：仅登记清理委托，不触发缓存构建（防自触发）
    static MapValidator()
    {
        CacheRegistry.Register("map-validator", CacheLayer.Full, ClearCache);
    }

    /// <summary>
    /// 程序集扫描结果缓存 — 避免重复调用 Validate 时重复扫描程序集类型和反射读取特性
    /// <para>键为程序集，值为该程序集中所有标注 [MapFrom] 的非抽象类及其特性列表</para>
    /// <para>改用 <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey, TValue}"/>
    /// 弱键——可收集程序集（CollectibleAssembly/ALC）卸载后条目随 GC 自动失效，
    /// 消除原 ConcurrentDictionary 强键阻止 ALC 回收的泄漏
    /// （值虽强引用 Type，但值仅经条目可达，键死则条目与值一并回收）。
    /// ClearCache 以换新实例实现（订正：net8+ 的
    /// ConditionalWeakTable 已提供 Clear()，原"CWT 无 Clear API"表述系 netstandard2.0 时代
    /// 前提错误——机制收敛登记，本批仅订正注释、机制不动，旧表整体交 GC）。</para>
    /// <para>换新字段补 volatile——引用赋值本身原子，
    /// 但无 volatile 时并发读者可能长期读到旧表（ClearCache 后扫描结果未失效的可见性缺口）；
    /// volatile 保证换新对后续 ScanAssembly 读取即时可见。</para>
    /// </summary>
    private static volatile System.Runtime.CompilerServices.ConditionalWeakTable<Assembly, List<(Type Type, MapFromAttribute Attr)>> s_assemblyScanCache = new();

    /// <summary>
    /// 扫描单个程序集中所有标注 [MapFrom] 的非抽象类（带缓存，重复调用不重复反射扫描）
    /// </summary>
    /// <param name="asm">需要扫描的程序集</param>
    /// <returns>该程序集中标注 [MapFrom] 的类型与特性列表；程序集类型加载失败时返回空列表</returns>
    [RequiresUnreferencedCode("ScanAssembly scans type members and attributes via reflection. Some members may be trimmed.")]
    private static List<(Type Type, MapFromAttribute Attr)> ScanAssembly(Assembly asm)
        => s_assemblyScanCache.GetValue(asm, a =>
        {
            Type[] types;
            try { types = a.GetTypes(); }
            catch (Exception ex)
            {
                // 跳过无法加载的类型，记录 Trace 便于诊断
                MapTracer.Write(ErrorMessages.ValidatorAssemblyScanFailure(a.GetName().Name ?? "", ex.Message));
                return new List<(Type, MapFromAttribute)>();
            }

            var list = new List<(Type, MapFromAttribute)>();
            foreach (var type in types)
            {
                if (!type.IsClass || type.IsAbstract) continue;

                var mapFromAttr = type.GetCustomAttribute<MapFromAttribute>();
                if (mapFromAttr == null) continue;

                list.Add((type, mapFromAttr));
            }
            return list;
        });

    /// <summary>
    /// 清除所有反射缓存 — 供 Mapper.ClearCache() 调用，支持插件系统动态卸载程序集场景
    /// </summary>
    internal static void ClearCache() => s_assemblyScanCache = new(); // 换新实例清理（net8+ 虽有 CWT Clear()，此处换新实例语义保持）

    /// <summary>
    /// 验证 AppDomain 中所有 [MapFrom] 标注的映射对（运行时反射路径）
    /// <para>触发全局配置冻结（v1.2 配置通道生效边界的运行时触发点之一，与首次映射并列；
    /// 冻结失败（多程序集配置冲突）时抛 <see cref="MapConfigurationException"/> 并明示冲突程序集清单）。
    /// 重复调用幂等（冻结已完成后无副作用）。</para>
    /// </summary>
    /// <returns>所有映射对的验证结果列表</returns>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("MapValidator.Validate uses reflection to scan assemblies and types. This is not supported in AOT/Trimmed scenarios.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("MapValidator.Validate uses reflection to scan assemblies and types. Some types may be trimmed in AOT/Trimmed scenarios.")]
    public static List<MapValidationResult> Validate()
        => Validate(AppDomain.CurrentDomain.GetAssemblies());

    /// <summary>
    /// 验证指定程序集中所有 [MapFrom] 标注的映射对
    /// <para><b>AOT/Trimming</b>：此方法使用反射扫描程序集和类型，AOT 下不可用。
    /// 有源生成器覆盖（Phase 3a）后此限制将解除。</para>
    /// <para>程序集扫描结果带缓存，重复调用不重复反射扫描</para>
    /// <para>触发全局配置冻结（v1.2 G6）：冻结失败（多程序集配置冲突）时抛
    /// <see cref="MapConfigurationException"/> 并明示冲突程序集清单；重复调用幂等。</para>
    /// </summary>
    /// <param name="assemblies">需要扫描的程序集列表；为空时扫描 AppDomain 全部程序集</param>
    /// <returns>所有映射对的验证结果列表</returns>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("MapValidator.Validate uses reflection to scan assemblies and types. This is not supported in AOT/Trimmed scenarios.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("MapValidator.Validate uses reflection to scan assemblies and types. Some types may be trimmed in AOT/Trimmed scenarios.")]
    public static List<MapValidationResult> Validate(params Assembly[] assemblies)
    {
        // v1.2 Validate 触发全局配置冻结（配置通道生效边界）；冻结失败抛冲突异常（fail-fast）
        GlobalMapConfig.GetEffective();

        var targetAssemblies = assemblies.Length > 0
            ? assemblies
            : AppDomain.CurrentDomain.GetAssemblies();

        var results = new List<MapValidationResult>();

        foreach (var asm in targetAssemblies)
        {
            // 走缓存的程序集扫描，避免重复反射调用 GetTypes/GetCustomAttribute
            var mappedTypes = ScanAssembly(asm);
            foreach (var (type, mapFromAttr) in mappedTypes)
            {
                var result = ValidateMapping(mapFromAttr.FromSource, type);
                // 降级维度填充：特性通道对同经 ORM 无关判定层；
                // needs=false 时保留原实例（零重建，既有行为逐字节保持）
                var needs = DegradationDetectorBase.EvaluateChannelAgnostic(mapFromAttr.FromSource, type, out var downgradeReason);
                results.Add(needs ? result.WithProjectionDowngrade(true, downgradeReason) : result);
            }
        }

        // Fluent 通道补扫——特性扫描覆盖不到 Fluent 声明对
        // （无 [MapFrom] 特性），经 RuntimeSnapshotStore.All 枚举补齐（B10/P-e：同程序集授权读取面，
        // All 为 internal——禁改可见性、禁反射字段/独立缓存旁路）。
        // 程序集过滤：meta.SourceType.Assembly ∈ targetAssemblies，
        // 与 Validate(params Assembly[]) "验证指定程序集"语义对齐；无参重载自然退化为全集。
        // 直接构造结果行不调用 ValidateMapping（方案 (a)）：属性匹配链为
        // 特性驱动，对纯 Fluent DTO（尤其异名 SourceName 声明）按字面执行将报"源字段不存在"假 Warn，
        // 违背报告可信度立项目标 → Level 恒 OK、MappedCount/TotalCount=0、Message 注记。
        var fluentPairs = new HashSet<(Type Source, Type Target)>();
        foreach (var meta in RuntimeSnapshotStore.All)
        {
            if (meta.Channel != RuntimeMappingChannel.Fluent) continue;
            if (!targetAssemblies.Contains(meta.SourceType.Assembly)) continue;
            // 按对粒度去重（同 (S,T) 幂等合并契约保证注册表唯一；此处防同对多条历史形态字面重复）
            if (!fluentPairs.Add((meta.SourceType, meta.TargetType))) continue;

            var fluentNeeds = DegradationDetectorBase.EvaluateChannelAgnostic(meta.SourceType, meta.TargetType, out var fluentReason);
            results.Add(new MapValidationResult
            {
                SourceType = meta.SourceType,
                TargetType = meta.TargetType,
                Level = MapValidationLevel.OK,
                MappedCount = 0,
                TotalCount = 0,
                Message = ErrorMessages.ValidatorFluentPairNote(),
                RequiresProjectionDowngrade = fluentNeeds,
                ProjectionDowngradeReason = fluentNeeds ? fluentReason : "",
            });
        }

        return results;
    }

    /// <summary>
    /// 验证单个映射对
    /// </summary>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型（DTO）</param>
    /// <returns>验证结果</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("ValidateMapping enumerates type properties and attributes via reflection. Some members may be trimmed. Use the source-generated Mapper.Validate() for AOT-safe validation.")]
    public static MapValidationResult ValidateMapping(Type sourceType, Type targetType)
    {
        var sourceProps = GetPublicProperties(sourceType);
        var targetProps = GetPublicProperties(targetType);

        int mapped = 0;
        int total = 0;
        var unmapped = new List<string>();
        var typeMismatch = new List<string>();

        // 方向判定：目标类型标注 [MapFrom] → ToDto 方向（全局忽略方向清单用）
        var isToDto = targetType.IsDefined(typeof(Attributes.MapFromAttribute));

        foreach (var targetProp in targetProps)
        {
            // 跳过 MapIgnore（属性级显式忽略）
            // T5 锚门化：所在类型无锚时 [MapIgnore] 不消费——属性进入
            // 正常统计通道（无映射配对即报 unmapped = "无效配置报告"的自然承载）
            // 收紧：准入改**特性锚**——Fluent 对不消费属性级 [MapIgnore]，
            // 属性同样进入正常统计通道（三态口径：特性对跳过 / Fluent 对与无锚均不消费——
            // 本工具无逐属性文案面，差异由统计承载自然呈现）
            if (RuntimeAnchorGate.HasAttributeAnchor(targetType)
                && targetProp.IsDefined(typeof(Attributes.MapIgnoreAttribute)))
                continue;
            // 跳过全局忽略属性（视为"有意跳过"，不计入未映射误报；）
            var globalIgnoreExempt = PropertyIgnoreChecker.HasOverrideIgnore(targetProp);
            if (PropertyMatcher.ShouldIgnore(targetProp, isToDto, globalIgnoreExempt))
                continue;
            if (!targetProp.CanWrite) continue;

            total++;

            // 查找 [MapProperty]
            PropertyMatcher.TryGetMapPropertyInfo(targetProp, sourceType,
                out string sourceFieldName, out Type fieldSourceType, out _);

            // 只有源类型匹配时才检查
            if (fieldSourceType != sourceType)
                continue; // 属于其他源类型，不算未映射

            // 查找源属性（复用 PropertyMatcher 匹配链，与映射侧同源；
            // 原内联 FirstOrDefault 实现删除）
            var sourceProp = GetSourcePropertyByName(sourceType, targetType, targetProp);

            // 验证端同步拆段——FromField 含 '.' 时按段解析（与运行时
            // ExpressionBuilder.BuildExplicitDotPathAssignment 同逻辑），消除"验证报未映射、映射实际成功"分叉
            // 触发条件含 '['（单段集合索引 "Posts[0]" 无 '.'）
            if (sourceProp == null && (sourceFieldName.Contains('.') || sourceFieldName.Contains('[')))
            {
                sourceProp = ResolveDotPathForValidation(sourceType, sourceFieldName);
            }

            if (sourceProp == null)
            {
                unmapped.Add(ErrorMessages.ValidatorUnmappedItem(targetProp.Name, sourceFieldName));
                continue;
            }

            // 检查类型兼容性（索引路径有效源类型 = 集合元素类型）
            var validationSrcType = sourceFieldName.Contains('[')
                && NavigationResolver.GetCollectionElementType(sourceProp.PropertyType) is { } idxElemType
                ? idxElemType
                : sourceProp.PropertyType;
            if (!IsTypeCompatible(validationSrcType, targetProp.PropertyType))
            {
                typeMismatch.Add(ErrorMessages.ValidatorTypeMismatchItem(targetProp.Name, sourceProp.PropertyType.Name, targetProp.PropertyType.Name));
                continue;
            }

            mapped++;
        }

        // 判定级别
        MapValidationLevel level;
        string message = "";

        if (typeMismatch.Count > 0)
        {
            level = MapValidationLevel.Error;
            message = ErrorMessages.ValidatorTypeMismatchSummary(typeMismatch.Count, string.Join(", ", typeMismatch));
        }
        else if (unmapped.Count > 0)
        {
            level = MapValidationLevel.Warn;
            message = ErrorMessages.ValidatorUnmappedSummary(unmapped.Count, string.Join(", ", unmapped));
        }
        else
        {
            level = MapValidationLevel.OK;
        }

        return new MapValidationResult
        {
            SourceType = sourceType,
            TargetType = targetType,
            Level = level,
            MappedCount = mapped,
            TotalCount = total,
            UnmappedProperties = unmapped,
            TypeMismatchProperties = typeMismatch,
            Message = message
        };
    }

    /// <summary>
    /// 打印验证报告到指定 TextWriter
    /// </summary>
    /// <param name="results">验证结果列表</param>
    /// <param name="output">输出目标；默认 Console.Out</param>
    /// <param name="showOk">是否显示 OK 的结果（默认只显示 Warn/Error）</param>
    public static void PrintReport(List<MapValidationResult> results, TextWriter? output = null, bool showOk = false)
    {
        var writer = output ?? Console.Out;
        writer.WriteLine(ErrorMessages.ValidatorReportTitle());
        var ok = results.Count(r => r.Level == MapValidationLevel.OK);
        var warn = results.Count(r => r.Level == MapValidationLevel.Warn);
        var error = results.Count(r => r.Level == MapValidationLevel.Error);
        // 降级汇总恒输出（不受 showOk 控制）
        var degrade = results.Count(r => r.RequiresProjectionDowngrade);
        writer.WriteLine(ErrorMessages.ValidatorReportTotals(results.Count, ok, warn, error, degrade));
        writer.WriteLine();

        foreach (var r in results)
        {
            if (!showOk && r.Level == MapValidationLevel.OK) continue;
            writer.WriteLine($"  {r}");
            // 逐行降级明细：可见性跟随所属结果行（受 showOk 控制）
            if (r.RequiresProjectionDowngrade)
                writer.WriteLine(ErrorMessages.ValidatorDowngradeReasonLine(r.ProjectionDowngradeReason));
        }

        writer.WriteLine("==========================================");
    }

    /// <summary>
    /// 获取类型的所有公开可读写实例属性（排除索引器）
    /// </summary>
    /// <param name="type">目标类型</param>
    /// <returns>属性列表</returns>
    [RequiresUnreferencedCode("GetPublicProperties scans type members and attributes via reflection. Some members may be trimmed.")]
    private static List<PropertyInfo> GetPublicProperties(Type type)
        => TypeHelper.GetPublicProperties(type);

    /// <summary>
    /// 验证端点路径拆段解析— 与运行时 ExpressionBuilder.BuildExplicitDotPathAssignment 同逻辑：
    /// 按 '.' 拆段逐级反射查找（OrdinalIgnoreCase，C2），中间节点排除值类型与 string；任一段缺失返回 null。
    /// 支持集合索引段 "B[0]"（属性名 + 非负整数；string 不可索引导航——与运行时判据一致）。
    /// </summary>
    /// <param name="sourceType">源类型</param>
    /// <param name="dotPath">点路径（如 "A.B.C"、"A.B[0].C"）</param>
    /// <returns>叶节点属性（集合索引段返回集合属性本身——验证端仅确认路径可解析）；解析失败返回 null</returns>
    [RequiresUnreferencedCode("ResolveDotPathForValidation scans type properties and attributes via reflection. Some members may be trimmed.")]
    private static PropertyInfo? ResolveDotPathForValidation(Type sourceType, string dotPath)
    {
        var segments = dotPath.Split('.');
        var currentType = sourceType;
        PropertyInfo? leaf = null;

        for (var i = 0; i < segments.Length; i++)
        {
            // 解析集合索引段 "B[0]" → 属性名 B（校验仅确认路径可解析，索引值不参与类型判定）
            var segName = segments[i];
            var bracket = segName.IndexOf('[');
            if (bracket >= 0)
            {
                if (bracket == 0 || !segName.EndsWith("]")
                    || !int.TryParse(segName.Substring(bracket + 1, segName.Length - bracket - 2), out var parsed)
                    || parsed < 0)
                {
                    return null;
                }
                segName = segName.Substring(0, bracket);
            }

            var props = GetPublicProperties(currentType);
            var prop = props.FirstOrDefault(p => string.Equals(p.Name, segName, StringComparison.OrdinalIgnoreCase));
            if (prop == null || !prop.CanRead) return null;
            leaf = prop;

            if (i < segments.Length - 1)
            {
                // 索引段取集合元素类型判可导航性；string 实现 IEnumerable<char> 不可索引导航（与运行时/生成器判据一致）
                var propType = bracket >= 0
                    ? NavigationResolver.GetCollectionElementType(prop.PropertyType)
                    : prop.PropertyType;
                if (propType == null || propType.IsValueType || propType == typeof(string)) return null;
                currentType = propType;
            }
            else if (bracket >= 0)
            {
                // 末段索引：集合类型校验（string/非集合不可索引）
                if (prop.PropertyType == typeof(string)
                    || NavigationResolver.GetCollectionElementType(prop.PropertyType) == null)
                {
                    return null;
                }
            }
        }

        return leaf;
    }

    /// <summary>
    /// 按名查找源属性 — 收敛：复用 PropertyMatcher 匹配链
    /// （删除 ValidateMapping 内联 FirstOrDefault 实现），与映射侧
    /// （MapperBase / ExpressionBuilder 的 GetSourceItemByTargetItem 调用）同源。
    /// <para>sourceIsDefine/targetIsDefine 按 [MapFrom] 标注实算（与 MapperBase 同口径）；
    /// 源属性列表取 GetPropertiesDictIgnoreCase.Values 并过滤索引器（与映射侧 BuildMapBody 同口径）。
    /// 相对旧内联 OrdinalIgnoreCase 纯按名的行为差异：非默认命名策略匹配、[MapProperty] FromField
    /// 消费（FromSource=当前源）、源侧反向标注消费——均为"校验与映射同源"的预期收敛，
    /// 变化清单登记。</para>
    /// </summary>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型（用于 [MapFrom] 方向判定）</param>
    /// <param name="targetItem">目标属性</param>
    /// <returns>匹配到的源属性；无匹配返回 null</returns>
    [RequiresUnreferencedCode("GetSourcePropertyByName scans type properties and attributes via reflection. Some members may be trimmed.")]
    private static PropertyInfo? GetSourcePropertyByName(Type sourceType, Type targetType, PropertyInfo targetItem)
        => PropertyMatcher.GetSourceItemByTargetItem(
            sourceType,
            targetItem,
            PropertyMatcher.GetPropertiesDictIgnoreCase(sourceType)?.Values
                .Where(p => p.GetIndexParameters().Length == 0)
                .ToList()
                ?? new List<PropertyInfo>(),
            sourceIsDefine: sourceType.IsDefined(typeof(Attributes.MapFromAttribute)),
            targetIsDefine: targetType.IsDefined(typeof(Attributes.MapFromAttribute)),
            out _);

    /// <summary>
    /// 判断源类型到目标类型是否兼容（可赋值或可转换）
    /// <para> 收敛：判定规则统一由 <see cref="NCMapper.Shared.TypeCompatView.CanConvert"/>
    /// 提供（单一判定权威，与生成侧 HasMappingPath 三向一致），本方法不再自建规则表。
    /// 原 :341"string?任意放行"宽松点与 Guid 死分支随收敛消失（
    /// 宽松点仅对 IConvertible 侧放行；死分支实际被 遮蔽）。</para>
    /// <para>校验结论按收敛目标允许合理变化（如 string→非 IConvertible 引用类型不再放行、
    /// 嵌套类判定与 PropertyMatcher 同源），变化清单经测试 2 输出登记。</para>
    /// </summary>
    /// <param name="sourceType">源属性类型</param>
    /// <param name="targetType">目标属性类型</param>
    /// <returns>类型兼容返回 true，否则返回 false</returns>
    private static bool IsTypeCompatible(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type sourceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type targetType)
        => TypeCompatView.CanConvert(sourceType, targetType);
}
