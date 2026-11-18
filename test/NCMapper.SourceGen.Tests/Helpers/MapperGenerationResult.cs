using Microsoft.CodeAnalysis;

namespace NCMapper.SourceGen.Tests.Helpers;

/// <summary>
/// 封装源生成器运行结果，包含生成的方法字典、诊断信息和完整生成代码
/// </summary>
public class MapperGenerationResult
{
    /// <summary>
    /// 从诊断列表和方法字典构建 MapperGenerationResult 实例
    /// </summary>
    /// <param name="diagnostics">诊断信息列表</param>
    /// <param name="diagnosticsByDescriptorId">按描述符 ID 分组的诊断字典</param>
    /// <param name="methods">方法名到生成方法实例列表的字典（支持重载方法）</param>
    /// <param name="generatedCode">完整生成代码文本（用于调试）</param>
    public MapperGenerationResult(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>> diagnosticsByDescriptorId,
        IReadOnlyDictionary<string, IReadOnlyList<GeneratedMethod>> methods,
        string generatedCode)
    {
        Diagnostics = diagnostics;
        DiagnosticsByDescriptorId = diagnosticsByDescriptorId;
        Methods = methods;
        GeneratedCode = generatedCode;
    }

    /// <summary>
    /// 所有诊断信息列表
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// 按诊断描述符 ID 分组的诊断字典
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>> DiagnosticsByDescriptorId { get; }

    /// <summary>
    /// 生成的方法字典（方法名 → GeneratedMethod 实例列表，支持重载）
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<GeneratedMethod>> Methods { get; }

    /// <summary>
    /// 完整生成代码文本（用于调试）
    /// </summary>
    public string GeneratedCode { get; }

    /// <summary>
    /// 按方法名获取第一个匹配的方法（处理重载情况）
    /// </summary>
    /// <param name="methodName">方法名称</param>
    /// <returns>第一个匹配的 GeneratedMethod 实例；未找到时返回 null</returns>
    public GeneratedMethod? GetMethod(string methodName)
    {
        return Methods.TryGetValue(methodName, out var list) && list.Count > 0 ? list[0] : null;
    }
}

