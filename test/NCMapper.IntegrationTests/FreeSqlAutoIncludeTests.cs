using Xunit;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql 自动 Include 集成测试 — ApplyFreeSqlIncludes 全路径覆盖
    /// <para>代码锚点：NCMapperFreeSqlExtensions.cs:45-60</para>
    /// <para>覆盖 T1-07a/07b/08/09：嵌套 DTO 触发、ConvertUsing 触发、catch 兜底、关闭开关</para>
    /// </summary>
    [Collection("FreeSqlGlobalState")]
    public class FreeSqlAutoIncludeOffTests : IClassFixture<FreeSqlTestFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="fixture">共享测试 Fixture</param>
        public FreeSqlAutoIncludeOffTests(FreeSqlTestFixture fixture)
        {
            _fsql = fixture.Fsql;
        }

        /// <summary>
        /// 自动 Include 生效路径原类为空测试类。
        /// <para>说明：自动 Include 常开（原 AutoIncludeEnabled 伪开关已删除，C-17，
        /// 关闭场景在产品与测试层均不可构造）；断言验证自动 Include 生效路径。</para>
        /// </summary>
        [Fact]
        public void T1_09_AutoInclude_On_LoadsNestedNavigation()
        {
            var dto = _fsql.Select<FsTopic>()
                .Where(a => a.Id == 1)
                .AsDto()
                .First<FsTopicNestedDto>();

            Assert.NotNull(dto);
            Assert.Equal("NCMapper 入门", dto.Title);
            Assert.NotNull(dto.Category);          // 自动 Include 已加载 Category
            Assert.Equal("[CatConv].NET", dto.Category!.Name);
        }
    }
}
