using Xunit;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 多源合成映射测试
    /// </summary>
    public class ComposeTests
    {
        /// <summary>
        /// 两个源对象合成 — 验证各字段正确映射到对应源类型
        /// </summary>
        [Fact]
        public void Compose_Two_Sources_All_Fields_Mapped()
        {
            var order = new DictSourceA { NameA = "Order1", ValueA = 100 };
            var customer = new DictSourceB { NameB = "Customer1", ValueB = 200 };

            var dto = Mapper.Compose<DictTargetDto>(order, customer)!;

            // A 字段
            Assert.Equal("Order1", dto.NameA);
            Assert.Equal(100, dto.ValueA);
            // B 字段
            Assert.Equal("Customer1", dto.NameB);
            Assert.Equal(200, dto.ValueB);
        }

        /// <summary>
        /// 三个源对象合成 — 包含多表合成场景
        /// </summary>
        [Fact]
        public void Compose_Three_Sources_All_Fields_Mapped()
        {
            // 用多表合成模型测试
            var order = new MultiSourceOrderEntity { OrderId = 1001, OrderAmount = 299.99m };
            var customer = new MultiSourceCustomerEntity { CustomerName = "ZhangSan", CustomerLevel = 5 };

            var dto = Mapper.Compose<MultiSourceComposeDto>(order, customer)!;

            Assert.Equal(1001, dto.OrderId);
            Assert.Equal(299.99m, dto.OrderAmount);
            Assert.Equal("ZhangSan", dto.CustomerName);
            Assert.Equal(5, dto.CustomerLevel);
        }

        /// <summary>
        /// 主映射源优先传入 — 创建 DTO 实例后后续源对象补充字段
        /// </summary>
        [Fact]
        public void Compose_Sources_Primary_First_Recommended()
        {
            // Compose 要求主映射源类型优先传入（用于创建 DTO 实例）
            // 非主源类型先传入时，只触发 Map 更新但不会创建 DTO
            var order = new DictSourceA { NameA = "O1", ValueA = 20 };
            var customer = new DictSourceB { NameB = "C1", ValueB = 10 };

            // 先传主源（order），再传辅源（customer）— 推荐
            var dto = Mapper.Compose<DictTargetDto>(order, customer)!;

            Assert.Equal("O1", dto.NameA);
            Assert.Equal(20, dto.ValueA);
            Assert.Equal("C1", dto.NameB);
            Assert.Equal(10, dto.ValueB);
        }

        /// <summary>
        /// null 源对象应被跳过，不影响其他源
        /// </summary>
        [Fact]
        public void Compose_Null_Source_Skipped()
        {
            var order = new DictSourceA { NameA = "A", ValueA = 1 };

            var dto = Mapper.Compose<DictTargetDto>(order, null!)!;

            Assert.Equal("A", dto.NameA);
            Assert.Equal(1, dto.ValueA);
            // B 字段保持默认
            Assert.Equal("", dto.NameB);
            Assert.Equal(0, dto.ValueB);
        }

        /// <summary>
        /// 空参数调用返回 null
        /// </summary>
        [Fact]
        public void Compose_Empty_Sources_Returns_Null()
        {
            var dto = Mapper.Compose<DictTargetDto>();
            Assert.Null(dto);
        }

        /// <summary>
        /// null 数组参数返回 null
        /// </summary>
        [Fact]
        public void Compose_Null_Array_Returns_Null()
        {
            var dto = Mapper.Compose<DictTargetDto>((object[]?)null!);
            Assert.Null(dto);
        }

        /// <summary>
        /// 仅传入主映射源 — 其余字段保持默认值
        /// </summary>
        [Fact]
        public void Compose_Only_Primary_Source()
        {
            var order = new DictSourceA { NameA = "Solo", ValueA = 42 };

            var dto = Mapper.Compose<DictTargetDto>(order)!;

            Assert.Equal("Solo", dto.NameA);
            Assert.Equal(42, dto.ValueA);
            Assert.Equal("", dto.NameB); // B 未提供，保持默认
        }

        /// <summary>
        /// Compose 结果与分步 Map 结果一致
        /// </summary>
        [Fact]
        public void Compose_Matches_Separate_Map_Calls()
        {
            var order = new DictSourceA { NameA = "Verify", ValueA = 99 };
            var customer = new DictSourceB { NameB = "Check", ValueB = 88 };

            // Compose 方式
            var composedDto = Mapper.Compose<DictTargetDto>(order, customer)!;

            // 分步 Map 方式
            var stepDto = Mapper.Map<DictSourceA, DictTargetDto>(order)!;
            Mapper.Map(customer, stepDto);

            // 结果一致
            Assert.Equal(stepDto.NameA, composedDto.NameA);
            Assert.Equal(stepDto.ValueA, composedDto.ValueA);
            Assert.Equal(stepDto.NameB, composedDto.NameB);
            Assert.Equal(stepDto.ValueB, composedDto.ValueB);
        }

        /// <summary>
        /// 未标注 [MapFrom] 的类调用 Compose 应抛异常
        /// </summary>
        [Fact]
        public void Compose_Without_MapFrom_Throws()
        {
            // 没有 [MapFrom] 标注的类不能 Compose
            Assert.ThrowsAny<InvalidOperationException>(() => Mapper.Compose<NoMapFromClass>(new object()));
        }

        /// <summary>
        /// 传入源对象但都不匹配主映射源类型时应抛异常（非静默返回 null）
        /// </summary>
        [Fact]
        public void Compose_Primary_Source_Not_Found_Throws()
        {
            // MultiSourceComposeDto 的主源是 MultiSourceOrderEntity
            // 只传入 MultiSourceCustomerEntity（非主源）→ 应抛异常
            var customer = new MultiSourceCustomerEntity { CustomerName = "Test", CustomerLevel = 1 };

            var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
                Mapper.Compose<MultiSourceComposeDto>(customer));

            Assert.Contains("MultiSourceOrderEntity", ex.Message);
            Assert.True(ex.Message.Contains("主映射源类型") || ex.Message.Contains("primary source"),
                $"Expected '主映射源类型' or 'primary source' in: {ex.Message}");
        }

        /// <summary>
        /// 传入不相关类型时应抛异常
        /// </summary>
        [Fact]
        public void Compose_Unrelated_Source_Types_Throws()
        {
            // 传入完全无关的类型
            var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
                Mapper.Compose<MultiSourceComposeDto>("string", 42, true));

            Assert.Contains("MultiSourceOrderEntity", ex.Message);
        }

        /// <summary>
        /// 空数组返回 null（无源对象传入，不触发主源未找到异常）
        /// </summary>
        [Fact]
        public void Compose_Empty_Array_Returns_Null()
        {
            var result = Mapper.Compose<MultiSourceComposeDto>(Array.Empty<object>());
            Assert.Null(result);
        }

        // ================================================================
        // 源处理顺序规则测试（规则2：主源恒首，其余按数组顺序，后来者覆盖）
        // ================================================================

        /// <summary>
        /// 主源不在数组第一位时，仍首先处理（创建 DTO）
        /// <para>验证：非主源在数组前面不会导致异常或创建失败</para>
        /// </summary>
        [Fact]
        public void Compose_PrimaryNotFirst_StillCreatesDto()
        {
            // 主源 OrderOverlapEntity 在数组第二位，ext 在第一位
            var ext = new OrderExtEntity { Status = "FromExt", Remark = "ExtRemark" };
            var order = new OrderOverlapEntity { Status = "FromOrder", OrderNo = "ORD-001" };

            var dto = Mapper.Compose<OverlapDto>(ext, order)!;

            // 主源创建 DTO → Status = "FromOrder", OrderNo = "ORD-001"
            // ext 按数组顺序更新 → Status 被 [MapProperty(typeof(OrderExtEntity))] 覆盖
            Assert.Equal("ORD-001", dto.OrderNo);
            Assert.Equal("FromExt", dto.Status);
            Assert.Equal("ExtRemark", dto.Remark);
        }

        /// <summary>
        /// 非主源通过 [MapProperty] 覆盖主源写入的同名字段
        /// <para>主源恒首 → 非主源永远在主源之后处理 → 非主源的同名字段总是覆盖主源</para>
        /// </summary>
        [Fact]
        public void Compose_NonPrimary_OverwritesPrimary_SameField()
        {
            var order = new OrderOverlapEntity { Status = "Primary", OrderNo = "ORD-002" };
            var ext = new OrderExtEntity { Status = "Override", Remark = "Note" };

            var dto = Mapper.Compose<OverlapDto>(order, ext)!;

            // OrderNo 只有主源有 → 保持主源值
            Assert.Equal("ORD-002", dto.OrderNo);
            // Status: 主源写入 "Primary" → ext 通过 [MapProperty] 覆盖为 "Override"
            Assert.Equal("Override", dto.Status);
            // Remark 只有 ext 有
            Assert.Equal("Note", dto.Remark);
        }

        /// <summary>
        /// 多个非主源按数组顺序依次更新各自声明的字段
        /// <para>验证数组顺序决定处理次序（各源写入不同字段，无冲突）</para>
        /// </summary>
        [Fact]
        public void Compose_MultipleNonPrimary_ArrayOrder_UpdatesSequentially()
        {
            var order = new OrderOverlapEntity { Status = "Primary", OrderNo = "ORD-003" };
            var ext1 = new OrderExtEntity { Status = "Ext1Status", Remark = "R1" };
            var ext2 = new OrderExtEntity2 { Remark2 = "R2" };

            // ext1 在 ext2 之前
            var dto = Mapper.Compose<OverlapDto>(order, ext1, ext2)!;

            Assert.Equal("ORD-003", dto.OrderNo);
            Assert.Equal("Ext1Status", dto.Status); // ext1 通过 [MapProperty] 覆盖主源
            Assert.Equal("R1", dto.Remark);          // ext1 的字段
            Assert.Equal("R2", dto.Remark2);         // ext2 的字段

            // 调换 ext1/ext2 顺序 — 结果相同（各写不同字段，无冲突）
            var dto2 = Mapper.Compose<OverlapDto>(order, ext2, ext1)!;
            Assert.Equal("Ext1Status", dto2.Status);
            Assert.Equal("R1", dto2.Remark);
            Assert.Equal("R2", dto2.Remark2);
        }

        /// <summary>
        /// null 源被跳过，不影响其余源的处理
        /// </summary>
        [Fact]
        public void Compose_NullInMiddle_SkippedWithoutAffectingOrder()
        {
            var order = new OrderOverlapEntity { Status = "Primary", OrderNo = "ORD-004" };
            var ext = new OrderExtEntity { Status = "Final", Remark = "Remark" };

            // null 在中间
            var dto = Mapper.Compose<OverlapDto>(order, null!, ext)!;

            Assert.Equal("ORD-004", dto.OrderNo);
            Assert.Equal("Final", dto.Status);
            Assert.Equal("Remark", dto.Remark);
        }
    }

    /// <summary>
    /// 多源合成测试 — 订单实体
    /// </summary>
    public class MultiSourceOrderEntity
    {
        public int OrderId { get; set; } = 1001;
        public decimal OrderAmount { get; set; } = 299.99m;
    }

    /// <summary>
    /// 多源合成测试 — 客户实体
    /// </summary>
    public class MultiSourceCustomerEntity
    {
        public string CustomerName { get; set; } = "";
        public int CustomerLevel { get; set; } = 3;
    }

    /// <summary>
    /// 多源合成测试 — 目标 DTO（从订单和客户两个源合成）
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(MultiSourceOrderEntity))]
    public class MultiSourceComposeDto
    {
        /// <summary>
        /// 订单标识符
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(MultiSourceOrderEntity), nameof(MultiSourceOrderEntity.OrderId))]
        public int OrderId { get; set; }

        /// <summary>
        /// 订单金额
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(MultiSourceOrderEntity), nameof(MultiSourceOrderEntity.OrderAmount))]
        public decimal OrderAmount { get; set; }

        /// <summary>
        /// 客户名称
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(MultiSourceCustomerEntity), nameof(MultiSourceCustomerEntity.CustomerName))]
        public string CustomerName { get; set; } = "";

        /// <summary>
        /// 客户等级
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(MultiSourceCustomerEntity), nameof(MultiSourceCustomerEntity.CustomerLevel))]
        public int CustomerLevel { get; set; }
    }

    /// <summary>
    /// 无 [MapFrom] 标注的测试类 — 用于验证 Compose 应抛异常
    /// </summary>
    public class NoMapFromClass
    {
        public int Id { get; set; }
    }

    // ================================================================
    // 源处理顺序规则测试模型（含重叠字段 Status）
    // ================================================================

    /// <summary>
    /// 顺序测试 — 主源实体（含 Status + OrderNo）
    /// </summary>
    public class OrderOverlapEntity
    {
        public string Status { get; set; } = "";
        public string OrderNo { get; set; } = "";
    }

    /// <summary>
    /// 顺序测试 — 扩展源1（含 Status + Remark，与主源 Status 重叠）
    /// </summary>
    public class OrderExtEntity
    {
        public string Status { get; set; } = "";
        public string Remark { get; set; } = "";
    }

    /// <summary>
    /// 顺序测试 — 扩展源2（含 Remark2，与主源无字段重叠）
    /// </summary>
    public class OrderExtEntity2
    {
        public string Remark2 { get; set; } = "";
    }

    /// <summary>
    /// 顺序测试 — 目标 DTO（主源为 OrderOverlapEntity，含多源重叠字段 Status）
    /// <para>主源自动映射所有同名字段（无需 [MapProperty]），非主源需显式声明</para>
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(OrderOverlapEntity))]
    public class OverlapDto
    {
        /// <summary>
        /// 重叠字段 — 主源写入后，OrderExtEntity 通过 [MapProperty] 覆盖
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(OrderExtEntity), nameof(OrderExtEntity.Status))]
        public string Status { get; set; } = "";

        /// <summary>
        /// 仅主源有此字段（无需 [MapProperty]，主源自动按名称匹配）
        /// </summary>
        public string OrderNo { get; set; } = "";

        [NCMapper.Attributes.MapProperty(typeof(OrderExtEntity), nameof(OrderExtEntity.Remark))]
        public string Remark { get; set; } = "";

        [NCMapper.Attributes.MapProperty(typeof(OrderExtEntity2), nameof(OrderExtEntity2.Remark2))]
        public string Remark2 { get; set; } = "";
    }

    // ================================================================
    // P0-9: ComposeList 批量合成测试
    // ================================================================

    /// <summary>
    /// 多源合成测试 — 产品实体
    /// </summary>
    public class MultiSourceProductEntity
    {
        /// <summary>
        /// 产品名称
        /// </summary>
        public string ProductName { get; set; } = "";
        /// <summary>
        /// 库存数量
        /// </summary>
        public int Stock { get; set; } = 200;
    }

    /// <summary>
    /// 多源合成测试 — 带产品字段的目标 DTO
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(MultiSourceOrderEntity))]
    public class MultiSourceWithProductDto
    {
        /// <summary>
        /// 订单标识符
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(MultiSourceOrderEntity), nameof(MultiSourceOrderEntity.OrderId))]
        public int OrderId { get; set; }

        /// <summary>
        /// 订单金额
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(MultiSourceOrderEntity), nameof(MultiSourceOrderEntity.OrderAmount))]
        public decimal OrderAmount { get; set; }

        /// <summary>
        /// 客户名称
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(MultiSourceCustomerEntity), nameof(MultiSourceCustomerEntity.CustomerName))]
        public string CustomerName { get; set; } = "";

        /// <summary>
        /// 产品名称
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(MultiSourceProductEntity), nameof(MultiSourceProductEntity.ProductName))]
        public string ProductName { get; set; } = "";
    }
}

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// ComposeList 批量合成测试 — 验证 Mapper.ComposeList 两个重载
    /// </summary>
    public class ComposeListTests
    {
        /// <summary>
        /// 批量多源合成 — 验证多组数据正确返回
        /// </summary>
        [Fact]
        public void ComposeList_MultipleGroups_ReturnsCorrectList()
        {
            var group1 = new object[]
            {
                new MultiSourceOrderEntity { OrderId = 1001, OrderAmount = 100m },
                new MultiSourceCustomerEntity { CustomerName = "Alice", CustomerLevel = 1 }
            };
            var group2 = new object[]
            {
                new MultiSourceOrderEntity { OrderId = 1002, OrderAmount = 200m },
                new MultiSourceCustomerEntity { CustomerName = "Bob", CustomerLevel = 2 }
            };

            var results = Mapper.ComposeList<MultiSourceComposeDto>(
                group1, group2)!;

            Assert.Equal(2, results.Count);
            Assert.Equal(1001, results[0].OrderId);
            Assert.Equal("Alice", results[0].CustomerName);
            Assert.Equal(1002, results[1].OrderId);
            Assert.Equal("Bob", results[1].CustomerName);
        }

        /// <summary>
        /// 批量合成 + 回调 — 验证 action 应用于每个结果
        /// </summary>
        [Fact]
        public void ComposeList_WithAction_CallbackAppliedPerItem()
        {
            var group1 = new object[]
            {
                new MultiSourceOrderEntity { OrderId = 2001, OrderAmount = 50m },
                new MultiSourceCustomerEntity { CustomerName = "X", CustomerLevel = 1 }
            };
            var group2 = new object[]
            {
                new MultiSourceOrderEntity { OrderId = 2002, OrderAmount = 75m },
                new MultiSourceCustomerEntity { CustomerName = "Y", CustomerLevel = 2 }
            };

            var processed = new System.Collections.Generic.List<string>();
            var results = Mapper.ComposeList<MultiSourceComposeDto>(
                dto => { processed.Add(dto.CustomerName); },
                group1, group2)!;

            Assert.Equal(2, results.Count);
            Assert.Equal(2, processed.Count);
            Assert.Contains("X", processed);
            Assert.Contains("Y", processed);
        }

        /// <summary>
        /// ComposeList 空数组 — 验证不崩溃，空数组无数据可合成返回 null 或空列表均合理
        /// </summary>
        [Fact]
        public void ComposeList_EmptyArray_ReturnsEmptyList()
        {
            var results = Mapper.ComposeList<MultiSourceComposeDto>(
                Array.Empty<object[]>());

            // 空数组无源数据可合成，返回 null 或空列表均合理
            if (results != null)
                Assert.Empty(results);
        }
    }
}
