using System.IO;
using System.Diagnostics.CodeAnalysis;

namespace NCMapper;

/// <summary>
/// 深拷贝策略选择器 — 用于从配置（appsettings.json / 代码）驱动 <see cref="DeepCopyProviderBase.Current"/>
/// <para>对应三个内置 <see cref="DeepCopyProviderBase"/> 实现：</para>
/// <list type="bullet">
/// <item><see cref="Fast"/> — <see cref="FastDeepCopyProvider"/>（默认；完整处理循环引用 / 自引用对象，不保持 DAG 共享引用，性能优先）</item>
/// <item><see cref="ExpressionTree"/> — <see cref="ExpressionTreeDeepCopyProvider"/>（含循环引用检测）</item>
/// <item><see cref="Json"/> — <see cref="JsonDeepCopyProvider"/>（JSON 序列化中转兜底，不支持循环引用）</item>
/// </list>
/// </summary>
public enum DeepCopyStrategy
{
    /// <summary>
    /// 快速深拷贝（默认）— 完整处理循环引用与自引用对象（Acyclic 零检测 + Cyclic/Unknown 完整检测），不保持 DAG 共享引用，性能优先
    /// </summary>
    Fast = 0,

    /// <summary>
    /// 表达式树深拷贝 — 含循环引用检测，对象图存在循环引用时使用
    /// </summary>
    ExpressionTree = 1,

    /// <summary>
    /// JSON 序列化中转深拷贝 — 兼容性兜底，不支持循环引用
    /// </summary>
    Json = 2,
}

/// <summary>
/// NCMapper 配置选项 — 支持 IOptions 模式和 appsettings.json 配置
/// <para>用法1（代码配置）：</para>
/// <para><c>services.AddNCMapper(opts => { opts.ValidationEnabled = false; });</c></para>
/// <para>用法2（appsettings.json）：</para>
/// <para><c>"NCMapper": { "ValidationEnabled": false, "MaxRecursionDepth": 128, "DeepCopyStrategy": "ExpressionTree" }</c></para>
/// <para><c>services.AddNCMapper(configuration.GetSection("NCMapper"));</c></para>
/// </summary>
public class NCMapperOptions
{
    /// <summary>
    /// 是否启用映射验证 — 默认 true
    /// </summary>
    public bool ValidationEnabled { get; set; } = true;

    /// <summary>
    /// 是否启用映射诊断 Trace — 默认 false
    /// </summary>
    public bool TraceEnabled { get; set; } = false;

    /// <summary>
    /// 最大映射递归深度 — 默认 64，超过此值视为循环引用
    /// <para>默认值判据源收敛至<see cref="Mapper.DefaultMaxRecursionDepth"/>（原 字面量）。</para>
    /// </summary>
    public int MaxRecursionDepth { get; set; } = Mapper.DefaultMaxRecursionDepth;

    /// <summary>
    /// 表达式树构建时的最大内联深度 — 默认 5
    /// </summary>
    public int MaxInlineDepth { get; set; } = Mapper.MaxInlineDepth;

    /// <summary>
    /// 映射诊断 Trace 输出目标 — 默认 null（使用 Console.Out）
    /// <para>注意：TextWriter 无法从 JSON 绑定，仅可通过代码配置</para>
    /// <para><c>null</c> 语义为<b>不调整</b>当前输出
    /// （非重置）——多次 Apply 时传 null 不会恢复默认输出；需恢复默认请显式设为
    /// <c>Console.Out</c>。契约固化文档化，不引入 sentinel 破坏属性可空形态。</para>
    /// </summary>
    public TextWriter? TraceOutput { get; set; }

    /// <summary>
    /// 深拷贝策略 — 默认 <see cref="NCMapper.DeepCopyStrategy.Fast"/>
    /// <para>应用到 <see cref="DeepCopyProviderBase.Current"/>，等价于代码赋值对应 Provider 实例</para>
    /// </summary>
    public DeepCopyStrategy DeepCopyStrategy { get; set; } = DeepCopyStrategy.Fast;


    /// <summary>
    /// 将配置选项应用到静态 Mapper 全局状态
    /// </summary>
    [RequiresDynamicCode("Apply builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    internal void Apply()
    {
        Mapper.ValidationEnabled = ValidationEnabled;
        Mapper.TraceEnabled = TraceEnabled;
        Mapper.MaxRecursionDepth = MaxRecursionDepth;
        Mapper.MaxInlineDepth = MaxInlineDepth;
        if (TraceOutput != null)
            Mapper.TraceOutput = TraceOutput;
        DeepCopyProviderBase.Current = DeepCopyStrategy switch
        {
            DeepCopyStrategy.ExpressionTree => new ExpressionTreeDeepCopyProvider(),
            DeepCopyStrategy.Json => JsonDeepCopyProvider.Instance,
            _ => new FastDeepCopyProvider(),
        };
    }
}
