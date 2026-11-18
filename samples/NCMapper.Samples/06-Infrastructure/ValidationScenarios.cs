using System.Globalization;
using System.IO;
using NCMapper.Attributes;
using NCMapper.Samples.Declarative;
using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Infrastructure;

/// <summary>
/// 验证诊断场景集 — MapValidator、MapTracer、错误诊断。
/// </summary>
/// <remarks>
/// <para><b>编译期诊断码最小复现指引</b>：
/// NCMAP001（源字段不存在）/ NCMAP002（类型不兼容）为<b>编译期诊断</b>——真实触发会阻断
/// samples 编译，本组以运行时验证/诊断演示为主；最小复现样例与负向先验锚由
/// 源生成器测试套锁定（NCMapper.SourceGen.Tests：NCMAP019FluentExemptionTests 的
/// PlainDto_StillReported_WithPositiveAnchor 为 019 先验锚基准；001/002 触发样例见
/// Models/Dtos 各诊断 fixture）。18 号页诊断码全索引（docs/zh-CN/reference/18-Diagnostics-Index.md）
/// 与测试套一一对应。</para>
/// </remarks>
    #region Validation_ValidationScenarios
public static class ValidationScenarios
{
    #endregion

    /// <summary>
    /// 扫描当前程序集中所有 [MapFrom] 类型对，验证映射完整性。
    /// </summary>
    /// <returns>验证结果列表</returns>
    #region Validation_ValidationScenarios_ValidateAll
    public static List<MapValidationResult> ValidateAll()
        => Mapper.Validate();
    #endregion


    /// <summary>
    /// 验证特定类型对的映射。
    /// </summary>
    /// <returns>单条验证结果</returns>
    #region Validation_ValidationScenarios_ValidateMapping
    public static MapValidationResult ValidateMapping()
        => MapValidator.ValidateMapping(typeof(UserEntity), typeof(UserDto));
    #endregion


    /// <summary>
    /// ValidateOrThrow：启动时全量校验，Error 级别抛异常。
    /// </summary>
    /// <returns>异常信息或 null（校验通过）</returns>
    #region Validation_ValidationScenarios_ValidateOrThrowDemo
    public static string? ValidateOrThrowDemo()
    {
        try
        {
            Mapper.ValidateOrThrow();
            return null;
        }
        catch (MapException ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
    #endregion


    /// <summary>
    /// Validate(assembly)：指定程序集校验。
    /// </summary>
    /// <returns>验证结果列表</returns>
    #region Validation_ValidationScenarios_ValidateAssembly
    public static List<MapValidationResult> ValidateAssembly()
        => Mapper.Validate(typeof(UserEntity).Assembly);
    #endregion


    /// <summary>
    /// null 源对象映射：Map(null) 返回 null。
    /// </summary>
    /// <returns>映射结果（预期 null）</returns>
    #region Validation_ValidationScenarios_MapNullSource
    public static UserDto? MapNullSource()
        => Mapper.Map<UserEntity, UserDto>(null!);
    #endregion


    /// <summary>
    /// 开启 MapTracer 输出属性映射路径诊断。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>映射后的 DTO</returns>
    #region Validation_ValidationScenarios_TraceMapping
    public static UserDto TraceMapping(UserEntity src)
    {
        MapTracer.GloballyEnabled = true;
        var result = Mapper.Map<UserEntity, UserDto>(src)!;
        MapTracer.GloballyEnabled = false;
        return result;
    }
    #endregion


    /// <summary>
    /// TraceOutput 设置 StringWriter 输出目标。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>捕获的 Trace 行数</returns>
    #region Validation_ValidationScenarios_TraceToWriter
    public static int TraceToWriter(UserEntity src)
    {
        var sw = new StringWriter();
        Mapper.TraceEnabled = true;
        Mapper.TraceOutput = sw;
        Mapper.Map<UserEntity, UserDto>(src);
        Mapper.TraceEnabled = false;
        return sw.ToString().Split('\n').Length;
    }
    #endregion


    /// <summary>
    /// MapTraceOptions.Enable() 工厂 + MapTracer.LogCallback 集成日志框架。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>LogCallback 捕获的消息条数</returns>
    #region Validation_ValidationScenarios_TraceLogCallback
    public static int TraceLogCallback(UserEntity src)
    {
        var messages = new List<string>();
        MapTracer.LogCallback = msg => messages.Add(msg);
        try
        {
            Mapper.TraceEnabled = true;
            Mapper.Map<UserEntity, UserDto>(src);
            return messages.Count;
        }
        finally
        {
            MapTracer.LogCallback = null;
            Mapper.TraceEnabled = false;
        }
    }
    #endregion


    /// <summary>
    /// MapTraceOptions.Enable() 工厂方法：按调用创建配置。
    /// </summary>
    /// <returns>配置的 Enabled 状态</returns>
    #region Validation_ValidationScenarios_TraceOptionsFactory
    public static bool TraceOptionsFactory()
    {
        var opts = MapTraceOptions.Enable();
        return opts.Enabled;
    }
    #endregion


    /// <summary>
    /// ValidationEnabled 开关双态：false 时 Validate() 一律返回空列表，恢复 true 后非空。
    /// <para>finally 恢复开关，防污染同进程后续场景</para>
    /// </summary>
    /// <returns>(关闭态结果数, 恢复态结果数)</returns>
    #region Validation_ValidationScenarios_ToggleValidationSwitch
    public static (int OffCount, int OnCount) ToggleValidationSwitch()
    {
        Mapper.ValidationEnabled = false;
        int off;
        try { off = ValidateAll().Count; }
        finally { Mapper.ValidationEnabled = true; }
        return (off, ValidateAll().Count);
    }
    #endregion


    /// <summary>
    /// PrintReport 重载双态：showOk 默认 false 跳过 OK 行，true 全行输出。
    /// <para>自造 1 OK + 1 Warn 结果集（MapValidationResult 为 public init 属性可构造），
    /// StringWriter 捕获文本供断言，不污染 Console 输出契约</para>
    /// </summary>
    /// <returns>(过滤态报告文本, 全量态报告文本)</returns>
    #region Validation_ValidationScenarios_PrintReportToWriters
    public static (string Filtered, string Full) PrintReportToWriters()
    {
        var reportResults = new List<MapValidationResult>
        {
            new() { SourceType = typeof(UserEntity), TargetType = typeof(UserDto),
                    Level = MapValidationLevel.OK, MappedCount = 5, TotalCount = 5 },
            new() { SourceType = typeof(UserEntity), TargetType = typeof(BadDto),
                    Level = MapValidationLevel.Warn, MappedCount = 0, TotalCount = 1,
                    UnmappedProperties = new List<string> { "Id" } },
        };
        var swFiltered = new StringWriter();
        MapValidator.PrintReport(reportResults, swFiltered);
        var swFull = new StringWriter();
        MapValidator.PrintReport(reportResults, swFull, showOk: true);
        return (swFiltered.ToString(), swFull.ToString());
    }
    #endregion


    /// <summary>
    /// 错误诊断：int→string 走内建转换（不抛异常）——不兼容类型运行时静默跳过赋值的对照演示。
    /// <para>实测修正：原注释「触发 MapException」与实际行为不符（MapException 运行时
    /// 触发路径为空串枚举/深度超限等窄形态，见 201-f9 与 401-depth），DumpExpectedFailure 已改普通 Dump。</para>
    /// </summary>
    /// <returns>捕获的 MapException 信息，或 null（内建转换成功）</returns>
    #region Validation_ValidationScenarios_DiagnoseError
    public static string? DiagnoseError()
    {
        try
        {
            var entity = new UserEntity { Id = 1, Name = "Test" };
            Mapper.Map<UserEntity, BadDto>(entity);
            return null;
        }
        catch (MapException ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
    #endregion

    /// <summary>MapException 诊断三字段读取：SourceType / TargetType / PropertyName（均可能为 null），失败信息拼为字符串返回。</summary>
    #region Infra_Validation_ExceptionFields
    public static string? ExceptionFields(UserEntity src)
    {
        try
        {
            _ = Mapper.Map<UserEntity, BadDto>(src);
            return null;
        }
        catch (MapException ex)
        {
            // The three diagnostic fields of MapException (PropertyName may be null for type-level failures)
            return $"{ex.Message} | source={ex.SourceType?.Name} target={ex.TargetType?.Name} property={ex.PropertyName}";
        }
    }
    #endregion

    /// <summary>
    /// 双语切换巡览 — CurrentUICulture 切换前后 MapException 消息语言对照（reference/localization.md 使用示例绑定源；巡览不挂 Run）。
    /// <para>失败样例复用 201-f9 已实证的空串→枚举必抛 MapException 路径：零新类型、零新注册映射对（不进声明式注册表，602 计数不受扰动）。</para>
    /// </summary>
    #region Validation_CultureBilingualTour
    public static void CultureBilingualTour()
    {
        var savedCulture = CultureInfo.CurrentUICulture;
        try
        {
            // Reuse the proven 201-f9 empty-string -> enum failure (guaranteed MapException);
            // no new type pair and no new registry entry are introduced by this tour.
            var failingSource = new F9Dto { Level = "", Grade = "Low" };

            CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");
            try { BasicScenarios.StringEnumSemantics(failingSource); }
            catch (MapException ex) { OutputHelper.Dump("602-culture-zh", "zh-CN MapException sample", ex.Message); }

            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            try { BasicScenarios.StringEnumSemantics(failingSource); }
            catch (MapException ex) { OutputHelper.Dump("602-culture-en", "en-US MapException sample", ex.Message); }
        }
        finally
        {
            // Restore thread UI culture: global state must not leak across scenarios
            CultureInfo.CurrentUICulture = savedCulture;
        }
    }
    #endregion

    #region Validation_ValidationScenarios_Run

    // ===== Scenario registration entry =====
    public static void Run()
    {
    var results = ValidationScenarios.ValidateAll();
    OutputHelper.Dump("602-validate-all", $"Count: {results.Count}", results.Select(r => new { r.Level, r.Message }).ToList());
    // hardening (expected-value source: "scan all type pairs annotated with [MapFrom]" semantics)
    if (results.Count <= 0 || !results.Any(r => r.TargetType == typeof(UserDto)))
        OutputHelper.Error($"602-validate-all: Validate() should scan [MapFrom] pairs including UserEntity→UserDto (actual: {results.Count} results)");

    var single = ValidationScenarios.ValidateMapping();
    OutputHelper.Dump("602-validate-single", "UserEntity→UserDto", new { single.Level, single.Message });
    // hardening (source: MapValidationResult field contract -- MappedCount/TotalCount structural consistency)
    if (single.MappedCount <= 0 || single.TotalCount < single.MappedCount)
        OutputHelper.Error($"602-validate-single: UserEntity→UserDto should map ≥1 property with TotalCount ≥ MappedCount (actual: {single.MappedCount}/{single.TotalCount})");

    var validateThrow = ValidationScenarios.ValidateOrThrowDemo();
    OutputHelper.DumpExpectedFailure("602-validate-or-throw", "ValidateOrThrow", validateThrow ?? "PASS");
    // hardening (source: "throw MapConfigurationException when Error-level results exist" -- must throw when the assembly contains an Error-level pair)
    if (validateThrow == null || !validateThrow.Contains("Exception"))
        OutputHelper.Error($"602-validate-or-throw: Error-level results should throw a NCMapper exception (actual: {validateThrow ?? "no exception"})");

    var assemblyResults = ValidationScenarios.ValidateAssembly();
    OutputHelper.Dump("602-validate-assembly", $"Assembly results: {assemblyResults.Count}", "");
    // hardening (expected-value source: Validate(assembly) assembly-specific overload -- results include type pairs inside that assembly)
    if (!assemblyResults.Any(r => r.TargetType == typeof(UserDto)))
        OutputHelper.Error("602-validate-assembly: assembly-scoped Validate should include UserEntity→UserDto pair");

    var nullResult = ValidationScenarios.MapNullSource();
    OutputHelper.Dump("602-map-null", "Map(null)", nullResult == null ? "null ✓" : "unexpected");
    // hardening (expected-value source: NCMapper null semantics "what comes in goes out unchanged" -- mapping a null source returns null)
    if (nullResult != null)
        OutputHelper.Error("602-map-null: Map(null) should return null (null passes through)");

    var src = new UserEntity { Id = 1, Name = "Trace", Age = 25, Email = "trace@test.com" };
    var traceLines = ValidationScenarios.TraceToWriter(src);
    OutputHelper.Dump("602-trace-writer", $"StringWriter lines", traceLines);
    // hardening (expected-value source: Trace output-channel contract -- StringWriter captures at least 1 line)
    if (traceLines < 1)
        OutputHelper.Error($"602-trace-writer: Trace output should capture at least 1 line (actual: {traceLines})");

    var logCount = ValidationScenarios.TraceLogCallback(src);
    OutputHelper.Dump("602-trace-logcallback", $"LogCallback messages", logCount);
    // hardening (expected-value source: LogCallback integration contract)
    if (logCount < 1)
        OutputHelper.Error($"602-trace-logcallback: LogCallback should capture at least 1 message (actual: {logCount})");

    var traced = ValidationScenarios.TraceMapping(src);
    OutputHelper.Dump("602-trace-mapping", src, traced);
    // hardening (structural: the Trace switch does not affect mapping results -- output fields stay complete)
    if (traced.Name != "Trace" || traced.Email != "trace@test.com")
        OutputHelper.Error($"602-trace-mapping: tracing should not alter mapping output (actual: {traced.Name}/{traced.Email})");

    var errorDiag = ValidationScenarios.DiagnoseError();
    OutputHelper.Dump("602-error-diagnostics", "int→string built-in conversion (no exception path)", errorDiag ?? "no exception (converted)");
    // hardening (measured correction: int->string goes through the built-in conversion without throwing -- the original DumpExpectedFailure label vs. the actual behavior
    // contradiction was adjudicated into a compatible-conversion demo (boss approved option B); the MapException runtime-trigger demo is carried by f9 empty-string enumeration / depth-limit overflow)
    if (errorDiag != null && !errorDiag.Contains("Exception"))
        OutputHelper.Error($"602-error-diagnostics: thrown exception should be a NCMapper exception type (actual: {errorDiag})");

    // supplemental demo (ValidationEnabled switch dual-state; expected-value source: ValidationEnabled contract -- when !ValidationEnabled
    // Validate() always returns an empty list; after restoring true it is non-empty -- the [MapFrom] pairs sharing the same source as 602-validate-all do exist)
    var (offCount, onCount) = ValidationScenarios.ToggleValidationSwitch();
    OutputHelper.Dump("602-validation-toggle", $"Off={offCount}, Restored={onCount}", "");
    if (offCount != 0 || onCount <= 0)
        OutputHelper.Error($"602-validation-toggle: ValidationEnabled=false should yield 0 results, restored should be >0 (actual: {offCount}/{onCount})");

    // supplemental demo (PrintReport overload dual-state; expected-value source: MapValidator showOk parameter contract -- showOk=false skips the OK row
    // (report rows contain TargetType.Name while title/statistics rows contain no type name; "UserDto" is not a substring of "UserEntity"/"BadDto", so no collision);
    // :388 titles in both languages contain NCMapper (ErrorMessages.cs:945-948); :406 tail is a hardcoded separator line of 42 "=" characters)
    var (reportFiltered, reportFull) = ValidationScenarios.PrintReportToWriters();
    OutputHelper.Dump("602-print-report", $"Filtered lines: {reportFiltered.Split('\n').Length}, Full lines: {reportFull.Split('\n').Length}", "");
    if (!reportFiltered.Contains("BadDto") || reportFiltered.Contains("UserDto"))
        OutputHelper.Error("602-print-report: default showOk=false should drop the OK row (UserDto) and keep the Warn row (BadDto)");
    if (!reportFull.Contains("UserDto") || !reportFull.Contains("NCMapper")
        || !reportFull.Contains("=========================================="))
        OutputHelper.Error("602-print-report: showOk=true should print all rows with title and 42-equals trailing separator");
    }
}
    #endregion

/// <summary>
/// 错误诊断用 DTO — Id 为 string 类型，与 UserEntity.Id (int) 不兼容，触发 MapException。
/// </summary>
[MapFrom(typeof(UserEntity))]
public class BadDto
{
    /// <summary>
    /// 故意使用 string 而非 int，用于触发类型不兼容异常。
    /// </summary>
    public string Id { get; set; } = "";

}
