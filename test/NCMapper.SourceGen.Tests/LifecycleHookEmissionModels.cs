using NCMapper.Attributes;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 发射层运行时模型（特性通道 [MapFrom]+[BeforeMapping]/[AfterMapping]）——
/// 本文件随测试工程编译即被源生成器处理，生成的 GeneratedMapper 方法编译进测试程序集供运行时断言：
/// Before 于首条赋值前触发（Core 降级分支③形态）/ _Update 形态 Before 见 dst 旧值 / After 行为不变（F4）。
/// </summary>

/// <summary>发射层验证源实体</summary>
public class LhEmitSrc
{
    /// <summary>Id</summary>
    public int Id { get; set; }

    /// <summary>名称</summary>
    public string Name { get; set; } = "";
}

/// <summary>发射层验证目标 DTO（Before/After 双钩子 + Before 观测载体）</summary>
[MapFrom(typeof(LhEmitSrc))]
[BeforeMapping(nameof(Before))]
[AfterMapping(nameof(After))]
public class LhEmitDto
{
    /// <summary>Before 触发时观测到的 dst.Id（运行时断言载体；null = 未触发）</summary>
    public static int? BeforeSawId { get; set; }

    /// <summary>Id</summary>
    public int Id { get; set; }

    /// <summary>名称</summary>
    public string Name { get; set; } = "";

    /// <summary>After 副作用载体（F4 回归锚）</summary>
    public string? Tag { get; set; }

    /// <summary>Before 钩子（单参 ctor 形态——声明于 DTO 自身）</summary>
    public static void Before(LhEmitSrc src, LhEmitDto dst) => BeforeSawId = dst.Id;

    /// <summary>After 钩子（既有发射行为回归锚）</summary>
    public static void After(LhEmitSrc src, LhEmitDto dst) => dst.Tag = "after:" + src.Id;
}
