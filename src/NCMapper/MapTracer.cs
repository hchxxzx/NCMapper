using System.Diagnostics.CodeAnalysis;
using System.Reflection;
namespace NCMapper;
/// <summary>
/// 映射诊断选项 — 控制映射过程的 Trace 输出
/// </summary>
public sealed class MapTraceOptions
{
    /// <summary>
    /// 是否启用 Trace 输出（默认 false）
    /// <para><see cref="MapTracer.Trace{TSource,TTarget}"/>
    /// 为"强制分析模式"——本开关对其<b>无效</b>（该入口始终生成并返回报告，返回 string 的
    /// 设计绑定强制语义）；本开关仅作用于 <see cref="MapTracer.WriteTrace{TSource,TTarget}"/>
    /// 与全局 <see cref="MapTracer.GloballyEnabled"/> 路径。</para>
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 输出目标 — 默认 Console.Out，可替换为 StringWriter 用于测试
    /// </summary>
    public System.IO.TextWriter Output { get; set; } = Console.Out;

    /// <summary>
    /// 是否输出属性值（CW默认 false，设为 true 才输出属性值）
    /// <para>⚠️ 开启后标注了 [Mask] 的属性显示为 "[MASKED]" 而非原值，防止敏感数据泄露</para>
    /// </summary>
    public bool ShowValues { get; set; } = false;

    /// <summary>
    /// 创建默认开启的 Trace 选项
    /// </summary>
    /// <returns>启用状态的 Trace 选项实例</returns>
    public static MapTraceOptions Enable() => new() { Enabled = true };

    /// <summary>
    /// 创建写入指定 Writer 的 Trace 选项
    /// </summary>
    /// <param name="writer">Trace 输出目标（如 StringWriter 用于测试）</param>
    /// <returns>启用状态的 Trace 选项实例</returns>
    public static MapTraceOptions Enable(System.IO.TextWriter writer) => new() { Enabled = true, Output = writer };
}

/// <summary>
/// 映射诊断器 — 输出映射过程详情，用于调试
/// </summary>
public static class MapTracer
{
    /// <summary>
    /// Trace 输出时属性值的最大显示长度 — 超过此长度截断并加 "..."
    /// </summary>
    private const int MaxValueDisplayLength = 50;

    /// <summary>
    /// Trace 写入锁— 进程级串行化 TextWriter 写入
    /// <para>共享 Writer（如 StringWriter→StringBuilder 非线程安全）并发写会抛
    /// ArgumentException("Destination is too short") 或行交错——诊断低频路径，锁开销可忽略。</para>
    /// </summary>
    private static readonly object s_writeLock = new();

    /// <summary>
    /// 全局 Trace 开关 — 默认 false（关闭）
    /// 设为 true 后所有映射操作都会输出 Trace（除非 per-call 覆盖）
    /// </summary>
    public static bool GloballyEnabled
    {
        get => s_globallyEnabled;
        set => s_globallyEnabled = value;
    }

    /// <summary>
    /// 全局启用总开关（volatile 保证多线程可见性）
    /// </summary>
    private static volatile bool s_globallyEnabled = false;

    /// <summary>
    /// 全局输出目标 — 默认 Console.Out
    /// </summary>
    public static System.IO.TextWriter GlobalOutput
    {
        get => s_globalOutput ?? Console.Out;
        set => s_globalOutput = value;
    }

    /// <summary>
    /// 全局输出目标（volatile 保证多线程可见性）
    /// </summary>
    private static volatile System.IO.TextWriter? s_globalOutput = Console.Out;

    /// <summary>
    /// 日志回调 — 设置后 Trace 输出同时写入此回调，可用于集成 ILogger/Serilog
    /// <para>用法：<c>MapTracer.LogCallback = msg => logger.LogDebug(msg);</c></para>
    /// <para>CW使用 volatile 确保多线程下不会读到部分更新的委托引用</para>
    /// </summary>
    public static Action<string>? LogCallback
    {
        get => s_logCallback;
        set => s_logCallback = value;
    }

    /// <summary>
    /// 日志回调委托（volatile 保证多线程可见性）
    /// </summary>
    private static volatile Action<string>? s_logCallback;

    /// <summary>
    /// 分析映射对，生成 Trace 报告（不实际执行映射）
    /// <para><b>强制分析模式</b>：本方法无视
    /// <see cref="MapTraceOptions.Enabled"/> 与 <see cref="GloballyEnabled"/>，始终生成
    /// 并返回完整报告（返回 string 的设计绑定强制语义——调用方即为索取报告而来）；
    /// 如需受开关控制的 Trace 输出请用 <see cref="WriteTrace{TSource,TTarget}"/>。</para>
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <param name="source">源对象（可为 null，只分析不执行）</param>
    /// <param name="options">Trace 选项（null 时用全局设置；Enabled 对本方法无效）</param>
    /// <returns>Trace 报告文本</returns>
    [RequiresUnreferencedCode("Trace enumerates type properties and attributes via reflection. Some members may be trimmed. Diagnostics tracing is not available on the AOT-safe source-generated path.")]
    public static string Trace<TSource, TTarget>(TSource source, MapTraceOptions? options = null)
        where TSource : class
        where TTarget : class
    {
        var sb = new System.Text.StringBuilder();
        var sw = new System.IO.StringWriter(sb);
        // 浅拷贝 options — 不再覆写调用方实例的 Output（入参不污染）
        var opts = new MapTraceOptions { Enabled = true, ShowValues = options?.ShowValues ?? false, Output = sw };

        WriteTraceReport<TSource, TTarget>(source, opts, sw, indent: "");

        return sb.ToString();
    }

    /// <summary>
    /// 输出映射 Trace 到指定 Writer
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <param name="source">源对象（可为 null）</param>
    /// <param name="target">目标对象（可为 null）</param>
    /// <param name="options">Trace 选项（null 时使用全局设置）</param>
    [RequiresUnreferencedCode("WriteTrace scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static void WriteTrace<TSource, TTarget>(TSource source, TTarget? target, MapTraceOptions? options)
        where TSource : class
        where TTarget : class
    {
        var enabled = options?.Enabled ?? GloballyEnabled;
        if (!enabled) return;

        var output = options?.Output ?? GlobalOutput;

        // 整个报告构建到 StringBuilder，最后一次性写入，避免并发交错
        var sb = new System.Text.StringBuilder();
        var buffer = new System.IO.StringWriter(sb);
        WriteTraceReport<TSource, TTarget>(source, options ?? new MapTraceOptions(), buffer, "");
        var report = sb.ToString();
        // TextWriter 并发写守卫（共享 Writer 的 StringBuilder 非线程安全）
        lock (s_writeLock)
        {
            output.Write(report);
        }

        // LogCallback 集成 — 支持 ILogger/Serilog（不再限于 StringWriter）
        var callback = LogCallback;
        if (callback != null)
        {
            callback(report);
        }
    }

    /// <summary>
    /// 输出一条 Projection/AsDto 诊断信息 — 仅在 <see cref="GloballyEnabled"/> 为 true 时写入 <see cref="GlobalOutput"/>
    /// <para>internal 方法，供 NCMapper.EFCore / NCMapper.FreeSql / NCMapper.Dapper 扩展包通过 InternalsVisibleTo 调用</para>
    /// </summary>
    /// <param name="message">诊断信息文本</param>
    internal static void Write(string message)
    {
        if (GloballyEnabled)
        {
            // TextWriter 并发写守卫（共享 Writer 的 StringBuilder 非线程安全）
            lock (s_writeLock)
            {
                GlobalOutput.WriteLine(message);
            }
        }
    }

    /// <summary>
    /// 生成并输出 Trace 报告 — 核心实现
    /// 分析源类型到目标类型的属性映射关系，输出每个属性的映射方式（direct/nested/json/convert）
    /// 以及 MapIgnore 跳过、字段未找到警告等信息
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <param name="source">源对象（可为 null）</param>
    /// <param name="options">Trace 选项</param>
    /// <param name="output">输出 Writer</param>
    /// <param name="indent">缩进字符串（嵌套递归时用）</param>
    [RequiresUnreferencedCode("WriteTraceReport scans type members and attributes via reflection. Some members may be trimmed.")]
    private static void WriteTraceReport<TSource, TTarget>(TSource source, MapTraceOptions options, System.IO.TextWriter output, string indent)
    {
        var sourceType = typeof(TSource);
        var targetType = typeof(TTarget);

        output.WriteLine($"{indent}[NCMapper] {sourceType.Name} → {targetType.Name}");

        // 获取属性映射信息
        var sourceProps = GetPublicProperties(sourceType);
        var targetProps = GetPublicProperties(targetType);

        // 删除 sourceAttr/sourceIsDefine 死变量（名实不符——读取的是目标类型特性且从未使用；targetIsDefine 已覆盖方向语义）
        var targetIsDefine = targetType.IsDefined(typeof(Attributes.MapFromAttribute), false);

        foreach (var targetProp in targetProps)
        {
            if (!targetProp.CanWrite) continue;

            // MapIgnore 或全局忽略（跳过原因标记 GlobalIgnore 便于诊断）
            // T1（审计 / 诊断面分层条款）：锚门 + 无效配置报告
            // 锚判定主体 = targetProp.DeclaringType（与消费门 PropertyMatcher.TryGetMapPropertyInfo 同源，
            // Inherited=true 继承形态下防 targetType 口径假报/漏报）；报告分支纯报告语义（WriteLine+continue，零消费语义）
            // c：**三态报告**
            // 特性锚（有 [MapFrom]）= skipped 照旧 / **特性锚假但双锚真（Fluent 对）= not consumed**
            //（标注对 Fluent 对不消费，非无效配置）/ 双锚假（无锚）= invalid config 照旧
            if (targetProp.IsDefined(typeof(Attributes.MapIgnoreAttribute)))
            {
                if (Runtime.RuntimeAnchorGate.HasAttributeAnchor(targetProp.DeclaringType!))
                    output.WriteLine($"{indent}  {targetProp.Name}: skipped (MapIgnore)");
                else if (Runtime.RuntimeAnchorGate.HasAnchor(targetProp.DeclaringType!))
                    output.WriteLine($"{indent}  {targetProp.Name}: ⚠️ not consumed (Fluent pair — attribute ignored; use Fluent declarations)");
                else
                    output.WriteLine($"{indent}  {targetProp.Name}: ⚠️ invalid config ([MapIgnore] inert without mapping anchor)");
                continue;
            }
            // 映射对级豁免（目标属性 OverrideIgnore=true）→ 两侧全局判定均跳过
            var globalIgnoreExempt = PropertyIgnoreChecker.HasOverrideIgnore(targetProp);
            if (PropertyMatcher.ShouldIgnore(targetProp, targetIsDefine, globalIgnoreExempt))
            {
                output.WriteLine($"{indent}  {targetProp.Name}: skipped (GlobalIgnore)");
                continue;
            }

            // MapProperty
            // 无锚 [MapProperty] 报告分支——对齐 MapValidator 达标形态，
            // 显式报告"无效配置"（门内已按名回退的误导输出由此前置拦截）；纯报告语义（WriteLine+continue）
            // c：三态——特性锚假 + 双锚真（Fluent 对）→ not consumed；
            // 双锚假（无锚）→ invalid config 照旧；特性锚真 → 落正常报告路径不变
            if (targetProp.IsDefined(typeof(Attributes.MapPropertyAttribute))
                && !Runtime.RuntimeAnchorGate.HasAttributeAnchor(targetProp.DeclaringType!))
            {
                if (Runtime.RuntimeAnchorGate.HasAnchor(targetProp.DeclaringType!))
                    output.WriteLine($"{indent}  {targetProp.Name}: ⚠️ not consumed (Fluent pair — attribute ignored; use Fluent declarations)");
                else
                    output.WriteLine($"{indent}  {targetProp.Name}: ⚠️ invalid config ([MapProperty] inert without mapping anchor)");
                continue;
            }
            PropertyMatcher.TryGetMapPropertyInfo(targetProp, sourceType,
                out string sourceFieldName, out Type fieldSourceType, out bool isFromJson);

            // 检查源类型是否匹配
            if (fieldSourceType != sourceType)
            {
                output.WriteLine($"{indent}  {targetProp.Name}: skipped (source type {fieldSourceType.Name} ≠ {sourceType.Name})");
                continue;
            }

            // 查找源属性
            var sourceProp = sourceProps.FirstOrDefault(p =>
                string.Equals(p.Name, sourceFieldName, StringComparison.OrdinalIgnoreCase));

            if (sourceProp == null)
            {
                output.WriteLine($"{indent}  {targetProp.Name}: ⚠️ source field '{sourceFieldName}' not found");
                continue;
            }

            // 源侧判定：源属性命中全局忽略 → 报 GlobalIgnore
            // 与运行时 MapperBase 双侧判定（ShouldIgnore(source) || ShouldIgnore(target)）一致
            if (PropertyMatcher.ShouldIgnore(sourceProp, targetIsDefine, globalIgnoreExempt))
            {
                output.WriteLine($"{indent}  {targetProp.Name}: skipped (GlobalIgnore, source side)");
                continue;
            }

        // 获取值
        object? sourceValue = null;
        try { sourceValue = source != null ? sourceProp.GetValue(source) : null; }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[NCMapper] Trace 获取属性值失败: {sourceProp.Name}: {ex.Message}"); }

            // 判断映射方式
            var srcType = sourceProp.PropertyType;
            var tgtType = targetProp.PropertyType;
            string mode;

            if (srcType == tgtType)
                mode = "direct";
            else if (isFromJson)
                mode = $"json ({srcType.Name}↔{tgtType.Name})";
            else if (IsNullable(srcType) && !IsNullable(tgtType))
                mode = $"nullable→{tgtType.Name}";
            else if (!IsNullable(srcType) && IsNullable(tgtType))
                mode = $"{srcType.Name}→nullable";
            else if (srcType.IsClass && tgtType.IsClass && !srcType.IsGenericType && !tgtType.IsGenericType
                     && srcType != typeof(string) && tgtType != typeof(string))
                mode = $"nested ({srcType.Name}→{tgtType.Name})";
            else
                mode = $"convert ({srcType.Name}→{tgtType.Name})";

            // 输出 — 脱敏属性输出 "[MASKED]"，防止敏感数据泄露到诊断输出
            // 脱敏判定并入注册表 Fluent .Mask<>() 通道
            // 原仅 [Mask] 特性反射（Fluent 声明无特性凭据 → ShowValues 明文泄露）；
            // inherit 实参订正 false（MaskAttribute AttributeUsage Inherited=false，原 true 无效）
            string valueStr;
            if (options.ShowValues && IsMaskedProperty(sourceType, targetType, targetProp))
            {
                valueStr = "[MASKED]";
            }
            else
            {
                valueStr = options.ShowValues ? FormatValue(sourceValue) : "...";
            }
            output.WriteLine($"{indent}  {targetProp.Name}: {sourceProp.Name}={valueStr} ({mode})");
        }
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
    /// 脱敏属性判定——[Mask] 特性反射 ∪ 注册表
    /// Fluent .Mask&lt;() 通道（RuntimePropertyMapping.HasMask 承载，按 (源,目标) 对查询）
    /// </summary>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="targetProp">目标属性</param>
    /// <returns>该属性任一通道声明脱敏时 true</returns>
    private static bool IsMaskedProperty(Type sourceType, Type targetType, PropertyInfo targetProp)
    {
        // 特性通道（inherit:false——MaskAttribute Inherited=false，实参与特性语义一致）
        // c：加**特性锚门**——Fluent 对上的残留
        // [Mask] 不构成"脱敏"报告依据（运行时不消费，与引擎同谓词同口径）；注册表 HasMask 腿不变
        if (Runtime.RuntimeAnchorGate.HasAttributeAnchor(targetProp.DeclaringType!)
            && targetProp.IsDefined(typeof(Attributes.MaskAttribute), inherit: false))
            return true;

        // 注册表 Fluent 通道（HasMask 信号槽，按目标属性名匹配）
        var properties = Runtime.RuntimeSnapshotStore.FindByPair(sourceType, targetType)?.Properties;
        if (properties is { Length: > 0 })
        {
            foreach (var p in properties)
            {
                if (p.HasMask && string.Equals(p.Target?.Name, targetProp.Name, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 判断类型是否为可空值类型（Nullable&lt;T&gt;）
    /// </summary>
    /// <param name="type">要检查的类型</param>
    /// <returns>是 Nullable&lt;T&gt; 返回 true</returns>
    private static bool IsNullable(Type type)
        => TypeConverter.IsNullableType(type);

    /// <summary>
    /// 格式化属性值为 Trace 输出用的字符串
    /// — null → "(null)"
    /// — 字符串超过 <see cref="MaxValueDisplayLength"/> <b>文本元素</b>截断加 "..."
    ///   （原 char 级切片可切出孤立代理项，对齐 MaskHelper 文本元素口径）
    /// — 其他类型调用 ToString()
    /// </summary>
    /// <param name="value">属性值</param>
    /// <returns>格式化后的字符串</returns>
    private static string FormatValue(object? value)
    {
        if (value == null) return "(null)";
        if (value is string s)
        {
            if (s.Length <= MaxValueDisplayLength) return $"\"{s}\"";
            // 全 ASCII 快路径（同款）：char 即文本元素
            bool ascii = true;
            foreach (var ch in s)
                if (ch >= 0x80) { ascii = false; break; }
            if (ascii) return $"\"{s[..MaxValueDisplayLength]}...\"";
            // 非 ASCII：元素级截断（代理对/组合字符不切断）
            var si = new System.Globalization.StringInfo(s);
            return si.LengthInTextElements > MaxValueDisplayLength
                ? $"\"{si.SubstringByTextElements(0, MaxValueDisplayLength)}...\""
                : $"\"{s}\"";
        }
        return value.ToString() ?? "(null)";
    }
}
