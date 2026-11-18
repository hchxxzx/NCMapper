using Xunit;

// 禁用测试并行执行 — SourceGen.Tests 触发全局静态 Mapper 注册（源生成器生成代码注册
// 泛型静态缓存），与并行化前提（无共享静态状态）冲突（双轴审查硬违规 1）。
// 与 NCMapper.Tests/AssemblyInfo.cs 同口径；CONTRIBUTING.md 要求测试项目禁用并行化。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
