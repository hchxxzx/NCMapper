using System.Globalization;
using FreeSql;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// Compose 双表/三表主源不匹配消息触发测试（C21/C22 补测）
/// <para>断言形态：InvalidOperationException + 消息含主源/泛型名。
/// 触发点（W0-1 ⑦ 薄壳化后符号定位）：ComposeProjectionKernel.BuildComposeCoreN 主源校验
/// （2 表 throw :51 / 3 表 throw :54，ErrorMessages 调用行 :52/:55——原 FreeSqlProjectionBuilder
/// 2Core :121/:122/:227/:228 已随薄壳化收敛至内核，_005 I2 以符号定位为准）。
/// 主源类型（[MapFrom].FromSource）不在传入源表中 → 按元数分派 ErrorMessages.ComposeProjectionSourceTypeMismatch(3)
/// （ErrorMessages.cs:148/:161，消息文本逐字保留，评审 P1-3）。
/// 零覆盖缺口（TC-002-4）：:148/:161 消息定义无触发测试。</para>
/// </summary>
public class FreeSqlComposeMismatchTests : IClassFixture<FreeSqlTestFixture>
{
    /// <summary>
    /// FreeSql 实例（fixture 提供 sqlite 库与种子数据）
    /// </summary>
    private readonly IFreeSql _fsql;

    /// <summary>
    /// 构造 — 注入 fixture
    /// </summary>
    /// <param name="fixture">共享 FreeSql 测试夹具</param>
    public FreeSqlComposeMismatchTests(FreeSqlTestFixture fixture) => _fsql = fixture.Fsql;

    /// <summary>
    /// C21 双表主源不匹配 — 主源 FsCategoryType 不在 {FsTopic, FsCategory} 中
    /// <para>消息断言：含主源名（FsCategoryType）与源表类型名（ComposeProjectionSourceTypeMismatch 模板）。</para>
    /// </summary>
    [Fact]
    public void C21_ComposeTwoSources_PrimarySourceMismatch_ThrowsWithNames()
    {
        var query = _fsql.Select<FsTopic, FsCategory>();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            query.AsDto().ToList<C21MismatchDto>());

        // 消息含主源名（C21MismatchDto 的 [MapFrom] 主源）
        Assert.Contains(nameof(FsCategoryType), ex.Message);
        // 消息含传入源类型名（双表）
        Assert.Contains(nameof(FsTopic), ex.Message);
        // 消息模板措辞逐字断言（防恒真：断言模板固定措辞，双语机制下按当前文化分支）
        Assert.Contains(ComposeMismatchText.TwoSources, ex.Message);
    }

    /// <summary>
    /// C22 三表主源不匹配 — 主源 FsComment 不在 {FsTopic, FsCategory, FsCategoryType} 中
    /// <para>触发点 ComposeProjectionKernel.BuildComposeCoreN 主源校验（3 表分支，ComposeProjectionSourceTypeMismatch3）。</para>
    /// </summary>
    [Fact]
    public void C22_ComposeThreeSources_PrimarySourceMismatch_ThrowsWithNames()
    {
        var query = _fsql.Select<FsTopic, FsCategory, FsCategoryType>();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            query.AsDto().ToList<C22MismatchDto>());

        // 消息含主源名与传入源类型名（三表）
        Assert.Contains(nameof(FsComment), ex.Message);
        Assert.Contains(nameof(FsTopic), ex.Message);
        // 消息模板措辞逐字断言（评审 P1-5 防恒真）
        Assert.Contains(ComposeMismatchText.ThreeSources, ex.Message);
    }
}

/// <summary>
/// 消息模板固定措辞（双语感知）——C21/C22 逐字断言用（评审 P1-5 防恒真）
/// <para>ErrorMessages 按 CurrentUICulture 切换中/英模板（en 开头 = 英文，其余 = 中文）；
/// 断言取当前文化对应模板的固定措辞，不依赖语言切换。</para>
/// </summary>
internal static class ComposeMismatchText
{
    /// <summary>
    /// 双表模板固定措辞（ComposeProjectionSourceTypeMismatch）：主源不在泛型参数 T1/T2 中
    /// </summary>
    public static string TwoSources =>
        CultureInfo.CurrentUICulture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "is not among the generic parameters T1"
            : "不在泛型参数 T1";

    /// <summary>
    /// 三表模板固定措辞（ComposeProjectionSourceTypeMismatch3）：主源不在 T1/T2/T3 中
    /// </summary>
    public static string ThreeSources =>
        CultureInfo.CurrentUICulture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "is not among T1("
            : "不在 T1(";
}

/// <summary>
/// C21 被测 DTO — 主源 FsCategoryType（双表查询 {FsTopic, FsCategory} 中不含）
/// </summary>
[MapFrom(typeof(FsCategoryType))]
public class C21MismatchDto
{
    /// <summary>
    /// 标识字段
    /// </summary>
    public int Id { get; set; }
}

/// <summary>
/// C22 被测 DTO — 主源 FsComment（三表查询 {FsTopic, FsCategory, FsCategoryType} 中不含）
/// </summary>
[MapFrom(typeof(FsComment))]
public class C22MismatchDto
{
    /// <summary>
    /// 标识字段
    /// </summary>
    public int Id { get; set; }
}
