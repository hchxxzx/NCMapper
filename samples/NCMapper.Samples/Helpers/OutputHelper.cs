using System.Text.Encodings.Web;
using System.Text.Json;

namespace NCMapper.Samples.Helpers;

/// <summary>
/// 输出辅助类 — JSON dump、错误收集和汇总打印。
/// </summary>
public static class OutputHelper
{
    /// <summary>
    /// 错误收集列表
    /// </summary>
    private static readonly List<string> s_errors = new();

    /// <summary>
    /// JSON 序列化选项（缩进输出 + 不转义非 ASCII 字符）。
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
        // demo 控制台输出以可读性优先：默认 Encoder 会把中文等非 ASCII 字符转义为
        // \uXXXX（如 \u7B49\u4EF7），SQL 中的双引号也会变成 \u0022，控制台难以阅读；
        // UnsafeRelaxedJsonEscaping 让中文原样输出，仅保留 JSON 语法必需的 \" 与 \r\n 转义。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 本次运行是否有错误。
    /// </summary>
    public static bool HasErrors => s_errors.Count > 0;

    // ===== 新 API（JSON dump 风格）=====

    /// <summary>
    /// 以 JSON 格式输出映射结果（含输入和输出）。
    /// </summary>
    /// <typeparam name="TInput">输入类型</typeparam>
    /// <typeparam name="TOutput">输出类型</typeparam>
    /// <param name="tag">场景标识（如 "201-simple-name-match"）</param>
    /// <param name="input">源对象</param>
    /// <param name="output">映射结果</param>
    public static void Dump<TInput, TOutput>(string tag, TInput input, TOutput output)
    {
        Console.WriteLine($"[{tag}]");
        Console.WriteLine($"  input  : {JsonSerializer.Serialize(input, s_jsonOpts)}");
        Console.WriteLine($"  output : {JsonSerializer.Serialize(output, s_jsonOpts)}");
        Console.WriteLine();
    }

    /// <summary>
    /// 以 JSON 格式输出单个值。
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="tag">场景标识</param>
    /// <param name="value">要输出的值</param>
    public static void Dump<T>(string tag, T value)
    {
        Console.WriteLine($"[{tag}]");
        Console.WriteLine($"  value : {JsonSerializer.Serialize(value, s_jsonOpts)}");
        Console.WriteLine();
    }

    /// <summary>
    /// 输出"预期失败演示"——output 为故意触发的异常信息。
    /// 与 <see cref="Dump{TInput,TOutput}"/> 同格式，额外带一行醒目标注，
    /// 避免读者把演示性异常误判为程序错误。
    /// </summary>
    /// <typeparam name="TInput">输入类型</typeparam>
    /// <typeparam name="TOutput">输出类型</typeparam>
    /// <param name="tag">场景标识</param>
    /// <param name="input">触发输入</param>
    /// <param name="output">捕获的异常信息</param>
    public static void DumpExpectedFailure<TInput, TOutput>(string tag, TInput input, TOutput output)
    {
        Console.WriteLine($"[{tag}]");
        Console.WriteLine("  note   : Expected demo — the exception below is triggered on purpose, not a program error");
        Console.WriteLine($"  input  : {JsonSerializer.Serialize(input, s_jsonOpts)}");
        Console.WriteLine($"  output : {JsonSerializer.Serialize(output, s_jsonOpts)}");
        Console.WriteLine();
    }

    // ===== 错误处理 =====

    /// <summary>
    /// 记录并打印错误信息（红色）。
    /// </summary>
    /// <param name="message">错误描述</param>
    public static void Error(string message)
    {
        s_errors.Add(message);
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [ERROR] {message}");
        Console.ForegroundColor = prev;
    }

    /// <summary>
    /// 以红色向指定输出流打印 CLI/菜单层错误（不计入错误收集列表——
    /// 此类错误由退出码 2 单独表达，无需再进 Error Summary）。
    /// </summary>
    /// <param name="writer">输出流（参数解析错走 Console.Error，交互菜单走 Console.Out）</param>
    /// <param name="message">错误描述</param>
    public static void CliError(TextWriter writer, string message)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        writer.WriteLine(message);
        Console.ForegroundColor = prev;
    }

    /// <summary>
    /// 打印累计错误汇总（全部运行结束后调用）。
    /// 存在错误时将进程退出码置为 1——供脚本/CI/AI 驱动以 $? 机器判定运行成败
    /// （此前仅打印文本，有错误也 exit 0，自动化无法检测）。
    /// </summary>
    public static void PrintErrorSummary()
    {
        if (s_errors.Count == 0) return;

        Console.WriteLine();
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  === Error Summary ({s_errors.Count} items) ===");
        for (int i = 0; i < s_errors.Count; i++)
            Console.WriteLine($"  {i + 1}. {s_errors[i]}");
        Console.ForegroundColor = prev;

        // 失败信号：有错误 → 退出码 1（脚本/AI 以 $? 判定；无错误保持 0）
        Environment.ExitCode = 1;
    }

    /// <summary>
    /// 清除错误记录（新一轮运行前调用）。
    /// </summary>
    public static void ClearErrors() => s_errors.Clear();
}
