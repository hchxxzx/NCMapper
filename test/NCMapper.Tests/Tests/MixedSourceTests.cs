using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 多源混合映射测试 — 一个 DTO 从多个不同实体拼装字段，借助 Action 回调
    /// </summary>
    public class MixedSourceTests
    {
        /// <summary>
        /// 主映射从 CustomerEntity 取 Id/Name，
        /// Action 回调从 FullTypeEntity 额外取 Score/StatusText 填入 DTO。
        /// 适用于一个 DTO 需要从多个数据源组装字段的场景。
        /// </summary>
        [Fact]
        public void CrossEntity_Assembly_Via_Action()
        {
            // 主数据源：客户信息
            var customer = new CustomerEntity { Id = 5, Name = "Charlie" };
            // 辅助数据源：评分和状态
            var extra = new FullTypeEntity { IntProp = 88, StatusProp = OrderStatus.Processing };

            // 映射后在 Action 回调中追加辅助数据
            var dto = Mapper.Map<CustomerEntity, MixedSourceDto>(customer, (s, d) =>
            {
                d.Score = extra.IntProp;
                d.StatusText = extra.StatusProp.ToString();
            });

            Assert.NotNull(dto);
            // 来自 CustomerEntity 的自动映射
            Assert.Equal(5, dto!.Id);
            Assert.Equal("Charlie", dto.Name);
            // 来自 FullTypeEntity 的 Action 追加
            Assert.Equal(88, dto.Score);
            Assert.Equal("Processing", dto.StatusText);
        }
    }
}
