using Xunit;

// 禁止所有测试并行执行 — NCMapper 使用全局静态状态（全局配置冻结、Mapper.Pipeline、DeepCopyProviderBase.Current 等）
// 与主测试项目（NCMapper.Tests）一致：全局串行是保证测试隔离的最可靠方式
// 投影一致性测试依赖进程级全局配置冻结，无串行时与其他 ORM 测试并行会触发冻结竞态
[assembly: CollectionBehavior(DisableTestParallelization = true)]
