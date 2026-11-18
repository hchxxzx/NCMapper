using NCMapper;
using NCMapper.Attributes;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Fluent;

// ================================================================
// 场景 108 — MapWhen 内联 lambda 转录运行对比。
// F41（捕获变量/私有成员）与 F42（语句体/白名单外节点）是编译期拒绝，
// 可运行工程不能内嵌编译失败示例——其拒绝形态由本文件注释与
// how-to/fluent/terminals.md 文档承载；本场景运行演示两个合法形态：
//   ① 无捕获内联 lambda 放行（转录进生成代码）
//   ② 捕获场景提取静态方法组后生效
// ================================================================

/// <summary>内联转录演示实体（Fluent 通道域内模型）。</summary>
public class InlineScoreEntity
{
    /// <summary>分值（内联谓词入参）。</summary>
    public int Score { get; set; }

    /// <summary>姓名。</summary>
    public string Name { get; set; } = "";
}

/// <summary>内联转录演示 DTO。</summary>
public class InlineScoreDto
{
    /// <summary>分值（守卫通过时映射，否则保持默认值）。</summary>
    public int Score { get; set; }

    /// <summary>姓名。</summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// 内联转录运行对比场景集（01-Fluent 域，108）。
/// </summary>
[MapperConfig]
public class InlineScoreMapper
{
    /// <summary>
    /// 链式声明：无捕获内联 lambda 谓词——编译期转录进生成代码，运行时零委托开销。
    /// </summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<InlineScoreEntity, InlineScoreDto>()
              .Member(d => d.Score).MapWhen<InlineScoreEntity>(s => s.Score >= 60);   // 无捕获内联：仅访问 lambda 参数，放行转录
}

/// <summary>
/// 捕获场景对照：谓词需要读取外层变量（threshold）——内联 lambda 捕获外层状态被
/// 编译期拒绝（F41，转录禁令），提取为**静态方法组**（T1 字段值形态）后生效。
/// 阈值以静态只读字段承载（编译期常量语义，方法组可静态解析）。
/// </summary>
public static class InlineThresholdPredicate
{
    /// <summary>守卫阈值（静态只读——方法组内可静态解析的读取面）。</summary>
    public static readonly int Threshold = 60;

    /// <summary>T1 形态谓词：分值 ≥ 阈值。</summary>
    /// <param name="score">源字段值</param>
    /// <returns>真则执行该属性映射</returns>
    public static bool AtLeastThreshold(int score) => score >= Threshold;
}

/// <summary>
/// 捕获形态的 Fluent 声明（静态方法组谓词）。
/// </summary>
[MapperConfig]
public class InlineThresholdMapper
{
    /// <summary>链式声明：静态方法组谓词（可静态解析）。</summary>
    /// <param name="cfg">Fluent 配置根</param>
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<InlineScoreEntity, InlineThresholdDto>()
              .Member(d => d.Score).MapWhen(InlineThresholdPredicate.AtLeastThreshold);
}

/// <summary>静态方法组守卫演示 DTO。</summary>
public class InlineThresholdDto
{
    /// <summary>分值（守卫通过时映射）。</summary>
    public int Score { get; set; }

    /// <summary>姓名。</summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// 场景 108 入口：内联放行与静态方法组两个运行形态的对比演示。
/// </summary>
public static class InlineLambdaScenarios
{
    /// <summary>场景注册入口（ScenarioRegistry 注册）。</summary>
    public static void Run()
    {
        InlinePassthrough();
        CapturedExtractedToStatic();
    }

    /// <summary>
    /// 无捕获内联 lambda 放行：Score ≥ 60 的输入映射生效、&lt; 60 的输入保持默认值——
    /// 谓词已转录进生成代码，运行时按编译期产物执行。
    /// </summary>
    #region FluentInline_Passthrough
    public static void InlinePassthrough()
    {
        var pass = Mapper.Map<InlineScoreEntity, InlineScoreDto>(new InlineScoreEntity { Score = 85, Name = "pass" });
        var blocked = Mapper.Map<InlineScoreEntity, InlineScoreDto>(new InlineScoreEntity { Score = 40, Name = "blocked" });
        OutputHelper.Dump("108-inline-passthrough",
            "captureless inline predicate Score>=60: pass=85 / blocked=40", new[] { pass, blocked });
        // Assert: the true path applies the mapping while the false path is intercepted by the guard (demo is valid once guard semantics are observable;
        // Map returns a nullable, so `?.` turns a "null return" into an assert failure instead of a null-reference crash)
        if (pass?.Score != 85)
            OutputHelper.Error("108-inline-passthrough: mapping not applied when guard is true");
        if (blocked?.Score != 0)
            OutputHelper.Error("108-inline-passthrough: not blocked when guard is false (Score should stay at default 0)");
    }
    #endregion

    /// <summary>
    /// 捕获场景提取静态方法组后生效：捕获外层变量的内联 lambda 是 F41 编译期拒绝
    /// （不可运行演示），提取为静态方法组（可静态解析）后映射正常工作。
    /// </summary>
    #region FluentInline_CapturedExtracted
    public static void CapturedExtractedToStatic()
    {
        // [X] compile-time rejected form (F41: captures outer state) -- cannot be embedded in a runnable project:
        //    var threshold = 60;
        //    cfg.For<S, T>().Member(d => d.Score).MapWhen<S>(s => s.Score >= threshold);  // F41 Error
        //
        // [OK] extracted to a static method group (the actual declaration of this scenario, see InlineThresholdMapper):

        var pass = Mapper.Map<InlineScoreEntity, InlineThresholdDto>(new InlineScoreEntity { Score = 72, Name = "static-pass" });
        var blocked = Mapper.Map<InlineScoreEntity, InlineThresholdDto>(new InlineScoreEntity { Score = 30, Name = "static-blocked" });
        OutputHelper.Dump("108-captured-extracted",
            "captured scenario extracted to static method group (threshold 60): pass=72 / blocked=30", new[] { pass, blocked });
        // Assert: the static method group predicate takes effect at runtime (threshold decision correct; `?.` as above, a null result lands as an assert failure)
        if (pass?.Score != 72 || blocked?.Score != 0)
            OutputHelper.Error("108-captured-extracted: static method group guard did not apply by threshold");
    }
    #endregion
}
