// P1#5（001 清单钩子缺口清零——004 v1.1 纳入：FluentRuntimeHooksTests）：
// Fluent BeforeMapping/AfterMapping 整机端到端（此前整机 0 覆盖；特性侧参照 LifecycleHooks 矩阵面）——
// 前置/后置钩子执行 + 时点顺序（Before 先于赋值 / After 后于赋值）+ 对级声明自动匹配对照锚。
// 隔离范式（经验 #71/#60）：[Collection("GlobalConfigState")]；[MapperConfig] fixture 编译进测试程序集
// → Mapper.Map 真运行时断言。钩子用命名静态方法承载（A-21 MapWhen 先例——内联 lambda 合成匿名方法
// 致 MethodRef 三字段 L1 差异面，规避）；同对单 fixture（For<S,T> 重复注册冲突规避）。
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class FluentRuntimeHooksTests
{
    // ================================================================
    // 夹具：对级声明 + 异名 + 双钩子（命名方法承载——记流水 + 语义变更一体）
    // ================================================================

    public class HkEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class HkView
    {
        public int Id { get; set; }
        public string AliasName { get; set; } = "";
    }

    // 钩子宿主（静态命名方法——MethodRef 稳定承载；流水仅本类消费——xunit 类内串行，静态安全）
    internal static class HkHooks
    {
        public static readonly System.Collections.Generic.List<string> Calls = new();

        public static void Before(HkEntity s, HkView d)
        {
            Calls.Add("before");
            s.Name = "hk-" + s.Name;   // 源侧变更——应被后续赋值消费
        }

        public static void After(HkEntity s, HkView d)
        {
            Calls.Add("after");
            d.AliasName = d.AliasName + "!";   // 目标侧变更——不被赋值覆盖
        }
    }

    [MapperConfig]
    internal class HkMapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<HkEntity, HkView>()
                  .Member(d => d.AliasName).From(s => s.Name)
                  .BeforeMapping(HkHooks.Before)
                  .AfterMapping(HkHooks.After);
    }

    // ================================================================
    // 用例
    // ================================================================

    /// <summary>双钩子执行顺序实录：Before → 属性赋值 → After（静态流水 + 时点交叉断言）。</summary>
    [Fact]
    [Trait("Capability", "A-19")]
    [Trait("Capability", "A-20")]
    public void Hooks_ExecutionOrder_Before_Assignments_After()
    {
        HkHooks.Calls.Clear();
        var dto = Mapper.Map<HkEntity, HkView>(new HkEntity { Id = 2, Name = "li" });
        Assert.Equal(new[] { "before", "after" }, HkHooks.Calls);
        // 时点交叉证明：Before 的源变更（"hk-li"）已被赋值消费、After 的目标变更（"!"）未被覆盖
        // ——赋值环节介于两钩子之间
        Assert.Equal("hk-li!", dto.AliasName);
    }

    /// <summary>BeforeMapping 时点：先于属性赋值——源侧变更被映射消费（A-20 前置钩子执行）。</summary>
    [Fact]
    [Trait("Capability", "A-20")]
    public void BeforeMapping_RunsBeforeAssignments_SourceMutationConsumed()
    {
        var dto = Mapper.Map<HkEntity, HkView>(new HkEntity { Id = 1, Name = "zhang" });
        // "hk-" 前缀 = Before 源变更被赋值消费（若 Before 晚于赋值则为 "zhang"）；
        // "!" 尾缀 = 同 fixture 的 After 钩子效果（双钩子共存面）
        Assert.Equal("hk-zhang!", dto.AliasName);
    }

    /// <summary>AfterMapping 时点：后于属性赋值——目标侧变更不被赋值覆盖（A-19 后置钩子执行）。</summary>
    [Fact]
    [Trait("Capability", "A-19")]
    public void AfterMapping_RunsAfterAssignments_TargetMutationSticks()
    {
        var dto = Mapper.Map<HkEntity, HkView>(new HkEntity { Id = 1, Name = "zhang" });
        Assert.Equal("hk-zhang!", dto.AliasName);
    }

    /// <summary>对照基线：对级声明 + 自动匹配（Id 同名直通）——钩子宿主 fixture 的声明面（A-01）。</summary>
    [Fact]
    [Trait("Capability", "A-01")]
    public void PairDeclaration_AutoMatch_ValuesPreserved()
    {
        var dto = Mapper.Map<HkEntity, HkView>(new HkEntity { Id = 3, Name = "wang" });
        Assert.Equal(3, dto.Id);            // 自动匹配（同名直通）
        Assert.Equal("hk-wang!", dto.AliasName); // 异名 From + 双钩子流水
    }
}
