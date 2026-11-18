using System.Threading;

namespace NCMapper.SourceGen;

/// <summary>
/// 生成期上下文对象
/// 收纳 per-run 诊断收集器与取消令牌；生成方法经此追加诊断，缓存 MappingInfo 保持只读。
/// 注：StringBuilder 未收纳（GenerateXxx 保留既有 sb 参数）——收纳将导致 12+ 方法全量
/// 签名重构、数千行 sb 引用变更，超出改动估算；按最小可行修改原则保留 sb 参数
/// （见 差异登记）。
/// </summary>
internal sealed class GenerationContext
{
    /// <summary>
    /// 创建生成期上下文
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public GenerationContext(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
        Diagnostics = new DiagnosticCollector();
    }

    /// <summary>
    /// per-run 生成期诊断收集器
    /// </summary>
    public DiagnosticCollector Diagnostics { get; }

    /// <summary>
    /// 取消令牌 — 生成期长任务响应取消（与既有 spc.CancellationToken.ThrowIfCancellationRequested 模式对齐）
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// 发射前准备产物（W3_1 C2a 决策前移）— GenerationPreparation.Prepare 一次构造后挂载，
    /// 发射方法族经此查询 InitOnlyExprPlans / ElementExprPlans（ctx 通道传递，沿用本类收纳先例，
    /// 避免方法族 typeNameMap 签名全量传染；null = 未挂载，发射端 miss 兜底现场决策）
    /// </summary>
    public GenerationPreparation? Preparation { get; internal set; }
}
