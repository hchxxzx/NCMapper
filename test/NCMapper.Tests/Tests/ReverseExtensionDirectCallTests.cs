using NCMapper;
using NCMapper.Attributes;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 反向直调扩展方法端到端测试（生成委托路径）— 直接调用生成的
    /// <c>dto.MapToXxxEntity()</c> / <c>MapToXxxEntityList()</c> 扩展，实证
    /// 反向直调的用户实际使用路径（非字符串形态、非表达式树绕行）。
    /// <para>与 ReverseDirectCallGenTests（生成形态断言）和 ReverseDirectCallRuntimeTests
    /// （表达式树值行为，RegisterGenerated(null!) 绕开生成缓存）构成三列闭环：
    /// 本类锁定生成扩展调用的值行为（生成的 Map_*_To_* 委托路径）。</para>
    /// <para>覆盖：6 重载可调用性、点路径写回、Update 覆盖、D5 null=清空三行为、
    /// Compose 型只写主源字段、往返不变式、与表达式树引擎双引擎差分。</para>
    /// </summary>
    public class ReverseExtensionDirectCallTests
    {
        // ==================== 模型（独立前缀 RdExt，避免与既有测试共享静态注册状态） ====================

        /// <summary>
        /// 点路径写回模型 — 实体（含地址导航）
        /// </summary>
        public class RdExtEntity
        {
            /// <summary>
            /// 唯一标识
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 地址导航（点路径中间节点）
            /// </summary>
            public RdExtAddress? Address { get; set; }
        }

        /// <summary>
        /// 点路径写回模型 — 地址
        /// </summary>
        public class RdExtAddress
        {
            /// <summary>
            /// 城市（可空引用叶）
            /// </summary>
            public string? City { get; set; }

            /// <summary>
            /// 邮编（非可空值叶）
            /// </summary>
            public int Zip { get; set; }
        }

        /// <summary>
        /// 点路径写回模型 — DTO（扁平化自 Address 路径）
        /// </summary>
        [MapFrom(typeof(RdExtEntity))]
        public class RdExtDto
        {
            /// <summary>
            /// 唯一标识
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 城市（扁平化自 Address.City）
            /// </summary>
            [MapProperty(typeof(RdExtEntity), "Address.City")]
            public string? City { get; set; }

            /// <summary>
            /// 邮编（扁平化自 Address.Zip，可空源 → 非可空值叶）
            /// </summary>
            [MapProperty(typeof(RdExtEntity), "Address.Zip")]
            public int? Zip { get; set; }
        }

        /// <summary>
        /// Compose 模型 — 订单实体（主源）
        /// </summary>
        public class RdExtOrderEntity
        {
            /// <summary>
            /// 订单标识
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 订单金额
            /// </summary>
            public decimal Amount { get; set; }
        }

        /// <summary>
        /// Compose 模型 — 客户实体（子源）
        /// </summary>
        public class RdExtCustomerEntity
        {
            /// <summary>
            /// 客户姓名
            /// </summary>
            public string? Name { get; set; }

            /// <summary>
            /// 客户等级
            /// </summary>
            public int Level { get; set; }
        }

        /// <summary>
        /// Compose 模型 — 跨表 DTO（主源字段 + 子源字段）
        /// </summary>
        [MapFrom(typeof(RdExtOrderEntity))]
        public class RdExtMultiDto
        {
            /// <summary>
            /// 订单标识（主源）
            /// </summary>
            [MapProperty(typeof(RdExtOrderEntity), nameof(RdExtOrderEntity.Id))]
            public int OrderId { get; set; }

            /// <summary>
            /// 订单金额（主源）
            /// </summary>
            [MapProperty(typeof(RdExtOrderEntity), nameof(RdExtOrderEntity.Amount))]
            public decimal OrderAmount { get; set; }

            /// <summary>
            /// 客户姓名（子源，反向不写回）
            /// </summary>
            [MapProperty(typeof(RdExtCustomerEntity), nameof(RdExtCustomerEntity.Name))]
            public string? CustomerName { get; set; }
        }

        // ==================== 用例 ====================

        /// <summary>
        /// 扩展直调新建：src-only 重载返回新实例，普通字段 + 点路径写回均生效
        /// </summary>
        [Fact]
        public void SrcOnly_NewInstance_CreatesAndWrites()
        {
            var dto = new RdExtDto { Id = 1, City = "Guangzhou", Zip = 510000 };

            // 生成扩展直调（生成委托路径，非表达式树）
            var entity = dto.MapToRdExtEntity();

            Assert.NotNull(entity);
            Assert.Equal(1, entity!.Id);
            Assert.NotNull(entity.Address);               // 源非 null → 中间节点被创建
            Assert.Equal("Guangzhou", entity.Address!.City);
            Assert.Equal(510000, entity.Address.Zip);
        }

        /// <summary>
        /// 扩展 Update 重载：对既有目标实例覆盖生效（编辑表单提交主场景）
        /// </summary>
        [Fact]
        public void Update_ExistingInstance_Overwrites()
        {
            var dto = new RdExtDto { Id = 2, City = "Shenzhen", Zip = 518000 };
            var entity = new RdExtEntity
            {
                Id = 99,
                Address = new RdExtAddress { City = "OldCity", Zip = 1000 },
            };

            // 更新重载：dto.MapToXxxEntity(dst)
            dto.MapToRdExtEntity(entity);

            Assert.Equal(2, entity.Id);
            Assert.Equal("Shenzhen", entity.Address!.City);
            Assert.Equal(518000, entity.Address.Zip);
        }

        /// <summary>
        /// D5 经生成路径：源 null + 链存在 → 清叶（可空引用叶 null / 非可空值叶 default）
        /// </summary>
        [Fact]
        public void Update_NullSource_ChainExists_ClearsLeaf()
        {
            var dto = new RdExtDto { Id = 3, City = null, Zip = null };
            var entity = new RdExtEntity
            {
                Id = 3,
                Address = new RdExtAddress { City = "OldCity", Zip = 1000 },
            };

            dto.MapToRdExtEntity(entity);

            Assert.Null(entity.Address!.City);   // 可空引用叶 → null
            Assert.Equal(0, entity.Address.Zip); // 非可空值叶 → default(int)
        }

        /// <summary>
        /// D5 经生成路径：源 null + 链不存在 → no-op（不创建中间节点）
        /// </summary>
        [Fact]
        public void Update_NullSource_ChainAbsent_NoOp()
        {
            var dto = new RdExtDto { Id = 4, City = null, Zip = null };

            // 新建实例（链不存在）：反向不创建 Address
            var entity = dto.MapToRdExtEntity();

            Assert.NotNull(entity);
            Assert.Equal(4, entity!.Id);
            Assert.Null(entity.Address);
        }

        /// <summary>
        /// Action 重载：回调被执行且可修改目标（4 参 → 新建 + 回调）
        /// </summary>
        [Fact]
        public void ActionOverload_InvokesCallback()
        {
            var dto = new RdExtDto { Id = 5, City = "Beijing", Zip = 100000 };
            var callbackFired = false;

            var entity = dto.MapToRdExtEntity((src, dst) =>
            {
                callbackFired = true;
                Assert.Equal(5, src.Id);
                dst.Id = 55; // 回调可修改目标
            });

            Assert.True(callbackFired, "Action 回调应被执行");
            Assert.Equal(55, entity!.Id);
        }

        /// <summary>
        /// 列表重载：MapToXxxEntityList 映射全部元素
        /// </summary>
        [Fact]
        public void ListOverload_MapsAllElements()
        {
            var dtos = new[]
            {
                new RdExtDto { Id = 6, City = "Shanghai", Zip = 200000 },
                new RdExtDto { Id = 7, City = "Hangzhou", Zip = 310000 },
            };

            var entities = dtos.MapToRdExtEntityList();

            Assert.NotNull(entities);
            Assert.Equal(2, entities!.Count);
            Assert.Equal("Shanghai", entities[0].Address!.City);
            Assert.Equal(7, entities[1].Id);
            Assert.Equal(310000, entities[1].Address!.Zip);
        }

        /// <summary>
        /// 列表 + Action 重载：逐元素回调生效
        /// </summary>
        [Fact]
        public void ListActionOverload_InvokesCallback()
        {
            var dtos = new[]
            {
                new RdExtDto { Id = 8, City = "Chengdu", Zip = 610000 },
                new RdExtDto { Id = 9, City = "Wuhan", Zip = 430000 },
            };
            var callbackCount = 0;

            var entities = dtos.MapToRdExtEntityList((src, dst) =>
            {
                callbackCount++;
                dst.Id = src.Id + 100;
            });

            Assert.Equal(2, callbackCount);
            Assert.Equal(108, entities![0].Id);
            Assert.Equal(109, entities[1].Id);
        }

        /// <summary>
        /// 声明跨表子源字段的 DTO 的反向扩展直调（所有 [MapFrom] DTO 统一生成反向扩展）：
        /// 只写主源字段（OrderId/OrderAmount），子源字段（CustomerName）不写回
        /// </summary>
        [Fact]
        public void ComposeDto_OnlyPrimarySourceWritten()
        {
            var dto = new RdExtMultiDto { OrderId = 10, OrderAmount = 99.5m, CustomerName = "Alice" };

            // 反向扩展指向主源实体（MapToRdExtOrderEntity；反向只覆盖主源字段）
            var order = dto.MapToRdExtOrderEntity();

            Assert.NotNull(order);
            Assert.Equal(10, order!.Id);
            Assert.Equal(99.5m, order.Amount);
            // 子源字段不参与反向（DTO 无对应实体属性可写；此处以主源实体无 Name 属性佐证无异常且未写入）
        }

        /// <summary>
        /// 往返不变式经扩展路径：entity → dto（正向扩展）→ entity（反向扩展）值不丢
        /// </summary>
        [Fact]
        public void RoundTrip_EntityToDtoThenBack_Invariant()
        {
            var entity = new RdExtEntity
            {
                Id = 11,
                Address = new RdExtAddress { City = "Nanjing", Zip = 210000 },
            };

            // 正向扩展（实体 → DTO）
            var dto = entity.MapToRdExtDto()!;
            Assert.Equal("Nanjing", dto.City);
            Assert.Equal(210000, dto.Zip);

            // 反向扩展（DTO → 实体）
            var entity2 = dto.MapToRdExtEntity()!;
            Assert.Equal(11, entity2.Id);
            Assert.Equal("Nanjing", entity2.Address!.City);
            Assert.Equal(210000, entity2.Address.Zip);
        }

        /// <summary>
        /// 双引擎差分：生成扩展路径（GeneratedMapper 委托）与表达式树路径（Mapper.Map）对同一 DTO
        /// 产生一致结果（同语义、两路径实证——补全 ReverseDirectCallRuntimeTests 绕行后缺失的对照）
        /// </summary>
        [Fact]
        public void GeneratedExtension_ConsistentWithExpressionTreeEngine()
        {
            var dto = new RdExtDto { Id = 12, City = "Xian", Zip = 710000 };

            // 路径 A：生成扩展直调（生成委托）
            var viaExtension = dto.MapToRdExtEntity()!;

            // 路径 B：表达式树引擎（取消生成缓存注册，强制运行时路径）
            Mapper.RegisterGenerated<RdExtEntity, RdExtDto>(null!);
            Mapper.RegisterGeneratedCore<RdExtEntity, RdExtDto>(null!);
            Mapper.RegisterGenerated<RdExtDto, RdExtEntity>(null!);
            Mapper.RegisterGeneratedCore<RdExtDto, RdExtEntity>(null!);
            try
            {
                var viaExprTree = Mapper.Map<RdExtDto, RdExtEntity>(dto)!;

                Assert.Equal(viaExtension.Id, viaExprTree.Id);
                Assert.Equal(viaExtension.Address!.City, viaExprTree.Address!.City);
                Assert.Equal(viaExtension.Address.Zip, viaExprTree.Address.Zip);
            }
            finally
            {
                // 恢复注册表，避免污染其他测试
                Mapper.ClearGenerated();
            }
        }
    }
}
