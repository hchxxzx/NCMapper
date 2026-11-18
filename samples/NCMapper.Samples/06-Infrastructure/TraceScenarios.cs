using NCMapper.Attributes;
using NCMapper.Samples.Helpers;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Infrastructure;

// ================================================================
// 场景 607 — 映射追踪：
// 全局 TraceEnabled 开关全流程 + 按调用 MapTraceOptions.Enable(writer) 单次报告。
// Trace 输出重定向到内存 TextWriter 断言，不污染控制台与其他场景。
// ================================================================

/// <summary>追踪演示客户实体（嵌套映射源）。</summary>
public class TraceCustomerEntity
{
    /// <summary>城市。</summary>
    public string City { get; set; } = "";
}

/// <summary>追踪演示客户 DTO（嵌套映射目标）。</summary>
public class TraceCustomerDto
{
    /// <summary>城市。</summary>
    public string City { get; set; } = "";
}

/// <summary>追踪演示源实体——四类映射方式各占一个属性（direct/nested/convert/skipped）。</summary>
public class TraceOrderEntity
{
    /// <summary>主键（direct：同名同型直赋）。</summary>
    public int Id { get; set; }

    /// <summary>标题（direct）。</summary>
    public string Title { get; set; } = "";

    /// <summary>下单时间（convert：[Convert] 格式化为字符串）。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>客户（nested：嵌套对象映射）。</summary>
    public TraceCustomerEntity Customer { get; set; } = new();

    /// <summary>密钥（skipped：[MapIgnore] 忽略）。</summary>
    public string Secret { get; set; } = "";
}

/// <summary>追踪演示 DTO。</summary>
[MapFrom(typeof(TraceOrderEntity))]
public class TraceOrderDto
{
    /// <summary>主键（direct）。</summary>
    public int Id { get; set; }

    /// <summary>标题（direct）。</summary>
    public string Title { get; set; } = "";

    /// <summary>下单时间（convert：静态转换器格式化）。</summary>
    [MapProperty(typeof(TraceOrderEntity), nameof(TraceOrderEntity.CreatedAt))]
    [Convert(typeof(TraceDateFormatter), nameof(TraceDateFormatter.Format))]
    public string CreatedText { get; set; } = "";

    /// <summary>客户（nested）。</summary>
    public TraceCustomerDto Customer { get; set; } = new();

    /// <summary>密钥（skipped：MapIgnore）。</summary>
    [MapIgnore]
    public string Secret { get; set; } = "";
}

/// <summary>追踪演示日期转换器（T1 模板）。</summary>
public static class TraceDateFormatter
{
    /// <summary>DateTime → yyyy-MM-dd 字符串。</summary>
    /// <param name="value">源时间</param>
    /// <returns>格式化文本</returns>
    public static string Format(DateTime value) => value.ToString("yyyy-MM-dd");
}

/// <summary>
/// 映射追踪场景集（06-Infra 域，607）。
/// </summary>
public static class TraceScenarios
{
    /// <summary>场景 607 入口（ScenarioRegistry 注册）。</summary>
    public static void Run()
    {
        GlobalTraceEnabledFlow();
        PerCallTraceReport();
    }

    /// <summary>
    /// 全局 TraceEnabled 开关全流程：开启 → LogCallback 收集 → 映射 → 断言 → 关闭恢复。
    /// </summary>
    #region Trace_GlobalEnabled
    public static void GlobalTraceEnabledFlow()
    {
        var src = MakeSource();
        var messages = new List<string>();

        // Enable: global switch + callback channel (Trace content is written to both GlobalOutput and the callback)
        MapTracer.LogCallback = messages.Add;
        MapTracer.GloballyEnabled = true;
        try
        {
            _ = Mapper.Map<TraceOrderEntity, TraceOrderDto>(src);
        }
        finally
        {
            // Restore off: Trace is a global diagnostic surface; it must be turned off after use to avoid polluting other scenarios
            MapTracer.GloballyEnabled = false;
            MapTracer.LogCallback = null;
        }

        OutputHelper.Dump("607-global-trace", $"GloballyEnabled full-flow capture, {messages.Count} lines", messages.Take(3).ToList());
        // Assert: mapping-pair markers captured (type names appear in Trace output); failure means Error Summary -> exit code 1
        if (messages.Count == 0 || !messages.Any(m => m.Contains(nameof(TraceOrderEntity)) && m.Contains(nameof(TraceOrderDto))))
            OutputHelper.Error("607-global-trace: global Trace did not capture mapping-pair output");
    }
    #endregion

    /// <summary>
    /// 按调用追踪报告：MapTraceOptions.Enable(writer) 捕获到内存后做包含检查——
    /// 报告应覆盖四类映射方式标记（direct / nested / convert / skipped (MapIgnore)）。
    /// </summary>
    #region Trace_PerCallOptions
    public static void PerCallTraceReport()
    {
        var src = MakeSource();
        // Trace is a forced-analysis mode (ignores the switch; the report is delivered via the return value -- not written to options.Output)
        string report = MapTracer.Trace<TraceOrderEntity, TraceOrderDto>(src);

        OutputHelper.Dump("607-per-call-report", "MapTraceOptions.Enable(writer) report (first 12 lines)",
            report.Split('\n').Take(12).ToList());
        // Assert: all four mapping-kind markers appear (direct/nested/convert/skipped); a miss means non-zero exit code
        foreach (var marker in new[] { "direct", "nested", "convert", "skipped" })
            if (!report.Contains(marker))
                OutputHelper.Error($"607-per-call-report: Trace report missing {marker} marker (all four mapping kinds should be covered)");
    }
    #endregion

    /// <summary>构造四类映射方式齐备的源实体。</summary>
    /// <returns>源实体</returns>
    private static TraceOrderEntity MakeSource() => new()
    {
        Id = 1,
        Title = "Trace-Demo",
        CreatedAt = new DateTime(2026, 9, 28, 10, 0, 0),
        Customer = new TraceCustomerEntity { City = "Beijing" },
        Secret = "should-be-skipped",
    };

    /// <summary>Trace 输出到文件事后分析：TraceEnabled + TraceOutput 重定向 StreamWriter，用后恢复默认输出。</summary>
    #region Trace_OutputToFile
    public static void TraceToFileDemo(string path, TraceOrderEntity src)
    {
        // Redirect trace output to a file for post-mortem analysis of a mapping problem
        Mapper.TraceEnabled = true;
        Mapper.TraceOutput = new StreamWriter(path);
        try
        {
            _ = Mapper.Map<TraceOrderEntity, TraceOrderDto>(src);
        }
        finally
        {
            // Restore console output and switch off: global diagnostic surface must not leak
            Mapper.TraceOutput = Console.Out;
            Mapper.TraceEnabled = false;
        }
    }
    #endregion

    /// <summary>MapTracer 全局开关包裹 DeepCopy + Action：Action 中直引 src 引用属性会被 Trace 标警告，用后关闭恢复。</summary>
    #region Infra_Tracer_DeepCopyProbe
    public static UserEntity TracerDeepCopyProbe(UserEntity src)
    {
        MapTracer.GloballyEnabled = true;
        try
        {
            // Trace emits a warning when the Action assigns a reference property straight from src to tgt
            return Mapper.DeepCopy(src, (s, t) => t.Name = $"[COPY] {s.Name}");
        }
        finally
        {
            MapTracer.GloballyEnabled = false;
        }
    }
    #endregion
}
