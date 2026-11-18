using Xunit;

// 禁止所有测试并行执行 — NCMapper 使用全局静态状态（DeepCopyProviderBase.Current、Mapper.TraceEnabled、
// 全局映射配置冻结 GlobalMapConfig 等）。串行化是保证测试隔离的最可靠方式。
// 补注（差异 #18）：全局配置（进程级单例）注册/冻结测试类与未感知存量测试类跨集合并行会竞态
// （实测 net8.0 KnownIssueTests 大小写匹配被 CaseSensitive 残留污染失败）；禁并行全套 1425 测试仍约 4s。
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// xUnit v3 要求显式定义 CollectionDefinition — v2 的隐式集合已移除
[CollectionDefinition("DeepCopyGlobalState", DisableParallelization = true)]
public class DeepCopyGlobalStateCollection { }
