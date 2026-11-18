using Microsoft.CodeAnalysis;

namespace NCMapper.SourceGen;

/// <summary>
/// 未映射成员诊断开关（NCMapperOptions 解析结果）
/// <para>对齐 Mapperly RequiredMappingStrategy 语义拆为两个正交开关：
/// ReportUnmappedTargetMembers 控制 NCMAP001（目标属性在源类型中不存在，对标 RMG012）、
/// ReportUnmappedSourceMembers 控制 NCMAP007（源成员未映射，对标 RMG020）。</para>
/// </summary>
internal sealed class DiagnosticOptions
{
    /// <summary>
    /// 目标属性在源类型中不存在（NCMAP001）是否报出；默认 false（宽松——AutoMapper 默认忽略语义，基类/嵌套/外部类型无法标注）
    /// </summary>
    public bool ReportUnmappedTargetMembers { get; set; } = false;

    /// <summary>
    /// 源成员未映射到任何目标成员（NCMAP007）是否报出；默认 true（严格——源成员存在但未映射，可能是遗漏）
    /// </summary>
    public bool ReportUnmappedSourceMembers { get; set; } = true;
}

/// <summary>
/// NCMapperOptions 配置解析器
/// <para>读取优先级：类级（目标类型上的 [NCMapperOptions]）&gt; 程序集级（[assembly: NCMapperOptions]）&gt; 默认（目标侧 false、源侧 true）。
/// 程序集级读取复用 GlobalConfigReader.ReadFromAssemblyAttribute 同款模式（GetTypeByMetadataName + Assembly.GetAttributes）。</para>
/// </summary>
internal static class DiagnosticOptionsResolver
{
    /// <summary>
    /// [NCMapperOptions] 特性全名常量
    /// </summary>
    private const string AttributeFullName = AttributeNames.NCMapperOptions;

    /// <summary>
    /// 解析目标类型对应的未映射成员诊断开关
    /// </summary>
    ///
    /// <param name="targetType">目标类型符号（类级特性读取）</param>
    /// <returns>解析后的诊断开关（无标注时返回默认值）</returns>
    public static DiagnosticOptions Resolve(INamedTypeSymbol targetType)
    {
        // transform 仅保留类级解析（TargetSymbol 派生合法）；
        // 程序集级 [NCMapperOptions] 已迁移至管线 mergedConfig 注入（ApplyGlobalConfig），
        // 消除 transform 内 Compilation.Assembly 全局语义访问（V1 审计唯一全局点）。
        // Resolve 与 HasClassLevelOptions 对同一目标类型
        // 各自全量 GetAttributes 扫描（调用面两次）→ 共用单次扫描内核 ResolveCore。
        return ResolveCore(targetType).Options;
    }

    /// <summary>
    /// 仅解析类级 [NCMapperOptions]（目标类型标注整体覆盖；无标注返回默认）
    /// </summary>
    /// <param name="targetType">目标类型符号</param>
    /// <returns>解析后的类级诊断选项（无标注时为默认）</returns>
    public static DiagnosticOptions ResolveClassLevel(INamedTypeSymbol targetType)
        => ResolveCore(targetType).Options;

    /// <summary>
    /// 目标类型是否标注类级 [NCMapperOptions]（管线注入的覆盖条件判定）
    /// </summary>
    /// <param name="targetType">目标类型符号</param>
    /// <returns>标注类级 [NCMapperOptions] 时为 true</returns>
    public static bool HasClassLevelOptions(INamedTypeSymbol targetType)
        => ResolveCore(targetType).HasClassLevel;

    /// <summary>
    /// 单次扫描内核— 一次 GetAttributes 遍历同时产出"类级选项 + 是否标注"二元组，
    /// 两个公开入口各自取半；判定式缓存化（AttrSymbolCache，B3-11 同设施——口径外同族顺带登记）。
    /// </summary>
    /// <param name="targetType">目标类型符号</param>
    /// <returns>(类级诊断选项, 是否标注类级特性)</returns>
    private static (DiagnosticOptions Options, bool HasClassLevel) ResolveCore(INamedTypeSymbol targetType)
    {
        var classAttr = targetType.GetAttributes()
            .FirstOrDefault(a => AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeFullName);
        if (classAttr != null)
            return (Apply(classAttr), true);
        return (new DiagnosticOptions(), false);
    }

    /// <summary>
    /// 将特性命名参数应用到诊断开关（未显式设置的属性保持特性默认值）
    /// </summary>
    /// <param name="attr">NCMapperOptions 特性数据</param>
    /// <returns>应用后的诊断开关</returns>
    private static DiagnosticOptions Apply(AttributeData attr)
    {
        var options = new DiagnosticOptions();
        foreach (var kvp in attr.NamedArguments)
        {
            switch (kvp.Key)
            {
                case "ReportUnmappedTargetMembers" when kvp.Value.Value is bool target:
                    options.ReportUnmappedTargetMembers = target;
                    break;
                case "ReportUnmappedSourceMembers" when kvp.Value.Value is bool source:
                    options.ReportUnmappedSourceMembers = source;
                    break;
            }
        }
        return options;
    }
}
