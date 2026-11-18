using Xunit;

/// <summary>
/// 禁用测试并行化 — FsCheck 属性测试涉及全局可变状态
/// <para>根因：AdvancedDeepCopyProperties 修改 DeepCopyProviderBase.Current 和 Mapper.RegisterGeneratedDeepCopy 全局状态；</para>
/// <para>      PipelineProperties 修改 Mapper.Pipeline 全局单例。xUnit v3 默认并行执行不同测试类，</para>
/// <para>      导致 try/finally 恢复窗口外的并行测试读到污染状态（如 COPY: 委托泄漏、JsonDeepCopyProvider 污染 Stack 顺序）。</para>
/// <para>方案：assembly 级别禁用并行，所有测试类串行执行。FsCheck 每方法 100 次随机输入，串行对总耗时影响可忽略（&lt;2s）。</para>
/// </summary>
[assembly: CollectionBehavior(DisableTestParallelization = true)]
