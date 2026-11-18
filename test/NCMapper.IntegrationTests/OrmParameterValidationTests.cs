using FreeSql;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// ORM 降级路径参数校验断言测试（C25 补测）
/// <para>断言形态：ORM 降级路径 Assert.Throws 断言。
/// 专项5 D-4/BL-2：ORM 22 处 AOORE 均为 page/pageSize/size 参数校验
/// （符合 .NET 异常设计指南），但无断言用例；本测试补代表路径
/// （FreeSql ToPagedList/ToPagedListAsync，其余路径逐批补）。</para>
/// </summary>
public class OrmParameterValidationTests : IClassFixture<FreeSqlTestFixture>
{
    /// <summary>
    /// FreeSql 实例（fixture 提供 sqlite 库与种子数据）
    /// </summary>
    private readonly IFreeSql _fsql;

    /// <summary>
    /// 构造 — 注入 fixture
    /// </summary>
    /// <param name="fixture">共享 FreeSql 测试夹具</param>
    public OrmParameterValidationTests(FreeSqlTestFixture fixture) => _fsql = fixture.Fsql;

    /// <summary>
    /// ToPagedList page &lt; 1 → ArgumentOutOfRangeException（NCMapperFreeSqlExtensions:270）
    /// </summary>
    [Fact]
    public void FreeSql_ToPagedList_InvalidPage_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _fsql.Select<FsTopic>().AsDto().ToPagedList<FsTopicFullDto>(0, 10));

        Assert.Equal("page", ex.ParamName);
    }

    /// <summary>
    /// ToPagedList pageSize &lt; 1 → ArgumentOutOfRangeException（:271）
    /// </summary>
    [Fact]
    public void FreeSql_ToPagedList_InvalidPageSize_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _fsql.Select<FsTopic>().AsDto().ToPagedList<FsTopicFullDto>(1, 0));

        Assert.Equal("pageSize", ex.ParamName);
    }

    /// <summary>
    /// ToPagedListAsync page &lt; 1 → ArgumentOutOfRangeException（:305）
    /// </summary>
    [Fact]
    public async Task FreeSql_ToPagedListAsync_InvalidPage_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _fsql.Select<FsTopic>().AsDto().ToPagedListAsync<FsTopicFullDto>(0, 10, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("page", ex.ParamName);
    }

    /// <summary>
    /// ToPagedListAsync pageSize &lt; 1 → ArgumentOutOfRangeException（:306）
    /// </summary>
    [Fact]
    public async Task FreeSql_ToPagedListAsync_InvalidPageSize_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _fsql.Select<FsTopic>().AsDto().ToPagedListAsync<FsTopicFullDto>(1, 0, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("pageSize", ex.ParamName);
    }
}
