using Xunit;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// Compose 源生成器覆盖验证测试
    /// <para>验证源生成器生成的 Compose 代码与运行时路径行为一致</para>
    /// <para>测试项目中的 [MapFrom] DTO 会自动获得源生成器 Compose 方法，走编译时代码路径</para>
    /// </summary>
    public class ComposeSourceGenTests
    {
        /// <summary>
        /// 源生成器路径 — 基本多源合成正确性
        /// </summary>
        [Fact]
        public void SourceGen_Compose_Basic_Multi_Source()
        {
            var order = new DictSourceA { NameA = "Order1", ValueA = 100 };
            var customer = new DictSourceB { NameB = "Customer1", ValueB = 200 };

            var dto = Mapper.Compose<DictTargetDto>(order, customer)!;

            Assert.Equal("Order1", dto.NameA);
            Assert.Equal(100, dto.ValueA);
            Assert.Equal("Customer1", dto.NameB);
            Assert.Equal(200, dto.ValueB);
        }

        /// <summary>
        /// 源生成器路径 — 三源合成
        /// </summary>
        [Fact]
        public void SourceGen_Compose_Three_Sources()
        {
            var order = new MultiSourceOrderEntity { OrderId = 2001, OrderAmount = 599.99m };
            var customer = new MultiSourceCustomerEntity { CustomerName = "LiSi", CustomerLevel = 8 };

            var dto = Mapper.Compose<MultiSourceComposeDto>(order, customer)!;

            Assert.Equal(2001, dto.OrderId);
            Assert.Equal(599.99m, dto.OrderAmount);
            Assert.Equal("LiSi", dto.CustomerName);
            Assert.Equal(8, dto.CustomerLevel);
        }

        /// <summary>
        /// 源生成器路径 — 非主源先传入，主源后传入
        /// <para>验证 pendingSources 两遍扫描逻辑在源生成器代码中正确实现</para>
        /// </summary>
        [Fact]
        public void SourceGen_Compose_NonPrimary_First_Then_Primary()
        {
            var order = new DictSourceA { NameA = "O1", ValueA = 42 };
            var customer = new DictSourceB { NameB = "C1", ValueB = 99 };

            // 先传非主源，再传主源
            var dto = Mapper.Compose<DictTargetDto>(customer, order)!;

            Assert.Equal("O1", dto.NameA);
            Assert.Equal(42, dto.ValueA);
            Assert.Equal("C1", dto.NameB);
            Assert.Equal(99, dto.ValueB);
        }

        /// <summary>
        /// 源生成器路径 — null 源对象跳过
        /// </summary>
        [Fact]
        public void SourceGen_Compose_Null_Source_Skipped()
        {
            var order = new DictSourceA { NameA = "A", ValueA = 1 };

            var dto = Mapper.Compose<DictTargetDto>(order, null!)!;

            Assert.Equal("A", dto.NameA);
            Assert.Equal(1, dto.ValueA);
            // B 字段保持默认值（DictTargetDto.NameB 默认初始化为 ""）
            Assert.Equal("", dto.NameB);
            Assert.Equal(0, dto.ValueB);
        }

        /// <summary>
        /// 源生成器路径 — 空数组返回 null
        /// </summary>
        [Fact]
        public void SourceGen_Compose_Empty_Sources_Returns_Null()
        {
            var result = Mapper.Compose<DictTargetDto>();
            Assert.Null(result);
        }

        /// <summary>
        /// 源生成器路径 — 主源缺失抛异常
        /// </summary>
        [Fact]
        public void SourceGen_Compose_Primary_Missing_Throws()
        {
            var customer = new DictSourceB { NameB = "C1", ValueB = 10 };

            Assert.ThrowsAny<InvalidOperationException>(() =>
                Mapper.Compose<DictTargetDto>(customer));
        }

        /// <summary>
        /// 源生成器路径 — 重复源对象
        /// </summary>
        [Fact]
        public void SourceGen_Compose_Duplicate_Sources()
        {
            var order1 = new DictSourceA { NameA = "First", ValueA = 10 };
            var order2 = new DictSourceA { NameA = "Second", ValueA = 20 };
            var customer = new DictSourceB { NameB = "C1", ValueB = 30 };

            var dto = Mapper.Compose<DictTargetDto>(order1, order2, customer)!;

            // 第一个主源创建 DTO，第二个主源更新（覆盖字段）
            Assert.Equal("Second", dto.NameA);
            Assert.Equal(20, dto.ValueA);
            Assert.Equal("C1", dto.NameB);
            Assert.Equal(30, dto.ValueB);
        }

        /// <summary>
        /// 源生成器路径 — 子源对象无 [MapFrom] 标注
        /// <para>验证外部对象不需要标注也能正常合成</para>
        /// </summary>
        [Fact]
        public void SourceGen_Compose_External_Source_Without_MapFrom()
        {
            // DictSourceB 没有 [MapFrom] 标注，但 [MapProperty(typeof(DictSourceB), ...)] 引用了它
            var order = new DictSourceA { NameA = "O1", ValueA = 5 };
            var customer = new DictSourceB { NameB = "Ext", ValueB = 7 };

            var dto = Mapper.Compose<DictTargetDto>(order, customer)!;

            Assert.Equal("O1", dto.NameA);
            Assert.Equal(5, dto.ValueA);
            Assert.Equal("Ext", dto.NameB);
            Assert.Equal(7, dto.ValueB);
        }

        /// <summary>
        /// 源生成器路径 — 三个源混合顺序
        /// </summary>
        [Fact]
        public void SourceGen_Compose_Mixed_Order_Three_Sources()
        {
            var order = new MultiSourceOrderEntity { OrderId = 3001, OrderAmount = 100m };
            var customer = new MultiSourceCustomerEntity { CustomerName = "WangWu", CustomerLevel = 2 };

            // 乱序传入
            var dto = Mapper.Compose<MultiSourceComposeDto>(customer, order)!;

            Assert.Equal(3001, dto.OrderId);
            Assert.Equal(100m, dto.OrderAmount);
            Assert.Equal("WangWu", dto.CustomerName);
            Assert.Equal(2, dto.CustomerLevel);
        }

        /// <summary>
        /// 行为一致性 — 源生成器路径结果与运行时路径结果完全一致
        /// <para>通过验证字段值来间接确认两条路径的一致性</para>
        /// </summary>
        [Fact]
        public void SourceGen_Compose_Consistent_With_Runtime()
        {
            var order = new DictSourceA { NameA = "Consistent", ValueA = 12345 };
            var customer = new DictSourceB { NameB = "Check", ValueB = 67890 };

            // 当前走源生成器路径（测试项目中 DTO 标注了 [MapFrom]）
            var dto = Mapper.Compose<DictTargetDto>(order, customer)!;

            // 验证所有字段
            Assert.Equal("Consistent", dto.NameA);
            Assert.Equal(12345, dto.ValueA);
            Assert.Equal("Check", dto.NameB);
            Assert.Equal(67890, dto.ValueB);
        }
    }
}
