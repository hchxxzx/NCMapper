using System.IO;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// Compose Trace 文本抽样断言（⑦ 回归）评审
/// <para>验证点：薄壳化后 Trace 前缀按元数参数化的文本逐字保留
/// （"双表"/"三表" 与 "T1/T2 中"/"T1/T2/T3 中"；原 FreeSqlProjectionBuilder
/// 2Core 8 处 Trace 收敛至 ComposeProjectionKernel，评审 P1-3 用户可观测行为零变更）。
/// 抽样场景：DTO 属性字段来源类型不在传入源表中 → Trace 1（"字段来源类型 X 不在 T1/T2（/T3）中"）。</para>
/// </summary>
public class FreeSqlComposeTraceTests : IClassFixture<FreeSqlTestFixture>
{
    /// <summary>
    /// FreeSql 实例（fixture 提供 sqlite 库与种子数据）
    /// </summary>
    private readonly IFreeSql _fsql;

    /// <summary>
    /// 构造 — 注入 fixture
    /// </summary>
    /// <param name="fixture">共享 FreeSql 测试夹具</param>
    public FreeSqlComposeTraceTests(FreeSqlTestFixture fixture) => _fsql = fixture.Fsql;

    /// <summary>
    /// 双表 Trace 前缀文本逐字保留 — 字段来源类型 FsComment 不在 {FsTopic, FsCategory} 中
    /// <para>触发点 ComposeProjectionKernel.BuildComposeCoreN bindings 循环（sourcePropsByType 未命中分支）。</para>
    /// </summary>
    [Fact]
    public void TwoTable_TraceFieldSourceNotInScope_PrefixAndScopeTextPreserved()
    {
        var writer = new StringWriter();
        var originalEnabled = MapTracer.GloballyEnabled;
        var originalOutput = MapTracer.GlobalOutput;
        try
        {
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = writer;

            // 主源 FsTopic 匹配（不抛异常）；CommentId 字段来源 FsComment 不在源表中 → Trace 1
            var expr = FreeSqlProjectionBuilder.BuildCompose<FsTopic, FsCategory, ComposeTraceTwoTableDto>();
            Assert.NotNull(expr);
        }
        finally
        {
            MapTracer.GloballyEnabled = originalEnabled;
            MapTracer.GlobalOutput = originalOutput;
        }

        var traceText = writer.ToString();
        // 前缀"双表"逐字
        Assert.Contains("[NCMapper.FreeSql] ComposeProjection(双表): 跳过属性", traceText);
        // 作用域"T1/T2 中"逐字（原 :147 文本）
        Assert.Contains("不在 T1/T2 中", traceText);
        // 字段来源类型名（FsComment）出现在 Trace 中
        Assert.Contains(nameof(FsComment), traceText);
    }

    /// <summary>
    /// 三表 Trace 前缀文本逐字保留 — 字段来源类型 FsComment 不在 {FsTopic, FsCategory, FsCategoryType} 中
    /// </summary>
    [Fact]
    public void ThreeTable_TraceFieldSourceNotInScope_PrefixAndScopeTextPreserved()
    {
        var writer = new StringWriter();
        var originalEnabled = MapTracer.GloballyEnabled;
        var originalOutput = MapTracer.GlobalOutput;
        try
        {
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = writer;

            var expr = FreeSqlProjectionBuilder.BuildCompose3<FsTopic, FsCategory, FsCategoryType, ComposeTraceThreeTableDto>();
            Assert.NotNull(expr);
        }
        finally
        {
            MapTracer.GloballyEnabled = originalEnabled;
            MapTracer.GlobalOutput = originalOutput;
        }

        var traceText = writer.ToString();
        // 前缀"三表"逐字
        Assert.Contains("[NCMapper.FreeSql] ComposeProjection(三表): 跳过属性", traceText);
        // 作用域"T1/T2/T3 中"逐字（原 :255 文本）
        Assert.Contains("不在 T1/T2/T3 中", traceText);
        Assert.Contains(nameof(FsComment), traceText);
    }
}

/// <summary>
/// 双表 Trace 被测 DTO — 主源 FsTopic；CommentId 字段来源 FsComment（不在源表中，触发 Trace 1）
/// </summary>
[MapFrom(typeof(FsTopic))]
public class ComposeTraceTwoTableDto
{
    /// <summary>
    /// 标识字段（主源 FsTopic 正常映射）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 评论标识（字段来源 FsComment 不在 {FsTopic, FsCategory} 中 → Trace 跳过）
    /// </summary>
    [MapProperty(typeof(FsComment), nameof(FsComment.Id))]
    public int CommentId { get; set; }
}

/// <summary>
/// 三表 Trace 被测 DTO — 主源 FsTopic；CommentId 字段来源 FsComment（不在源表中，触发 Trace 1）
/// </summary>
[MapFrom(typeof(FsTopic))]
public class ComposeTraceThreeTableDto
{
    /// <summary>
    /// 标识字段（主源 FsTopic 正常映射）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 评论标识（字段来源 FsComment 不在 {FsTopic, FsCategory, FsCategoryType} 中 → Trace 跳过）
    /// </summary>
    [MapProperty(typeof(FsComment), nameof(FsComment.Id))]
    public int CommentId { get; set; }
}
