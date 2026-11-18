namespace NCMapper.Attributes;

/// <summary>
/// 全局/类级映射诊断配置（对齐 Mapperly RequiredMappingStrategy 语义）。
/// <para>
/// 拆为两个正交开关：ReportUnmappedTargetMembers 控制目标侧诊断（NCMAP001，对标 Mapperly RMG012）、
/// ReportUnmappedSourceMembers 控制源侧诊断（NCMAP007，对标 Mapperly RMG020）。
/// 默认值（用户决策）：目标侧默认不报（目标属性在源类型中不存在 = AutoMapper 默认忽略语义，
/// 且基类/嵌套/外部引用类型无法标注），源侧默认报（源成员存在但未映射，可能是遗漏，提示有价值）。
/// </para>
/// </summary>
/// <remarks>
/// 读取优先级：类级（目标类型上的特性）&gt; 程序集级（[assembly: NCMapperOptions]）&gt; 默认（false, true）。
/// [MapProperty] 显式 FromField 错误（NCMAP001 保真场景）不受开关影响，始终报出。
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = false)]
public sealed class NCMapperOptionsAttribute : Attribute
{
    /// <summary>
    /// 目标属性在源类型中不存在（NCMAP001）是否报出；默认 false（宽松）。
    /// <para>AutoMapper 默认忽略语义下，源中无字段即不映射；基类（程序集）、嵌套声明、
    /// 外部引用对象均无法通过 [MapIgnore] 标注消除，默认不报避免噪音。
    /// 本地可标注的类型若需提示，可显式设 true。</para>
    /// </summary>
    public bool ReportUnmappedTargetMembers { get; set; } = false;

    /// <summary>
    /// 源成员未映射到任何目标成员（NCMAP007）是否报出；默认 true（严格）。
    /// <para>源成员存在但未映射，通常是遗漏信号（可标注 [MapIgnore]/[MapProperty] 消除），
    /// 默认报出以提醒用户核对。</para>
    /// </summary>
    public bool ReportUnmappedSourceMembers { get; set; } = true;
}
