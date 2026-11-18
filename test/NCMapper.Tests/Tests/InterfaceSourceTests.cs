using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 接口源映射测试 — 验证运行时路径对接口（含继承接口链、嵌套对象、集合）的属性发现和映射
    /// </summary>
    public class InterfaceSourceTests
    {
        // ================================================================
        // 1. 接口继承链
        // ================================================================

        /// <summary>
        /// 派生接口 IProductEntity 继承 IEntityBase，
        /// 验证直接声明属性和继承接口属性均能正确映射
        /// </summary>
        [Fact]
        public void InterfaceSource_InheritedProperties_MappedCorrectly()
        {
            IProductEntity source = new ProductEntity
            {
                Id = 2001,
                CreatedAt = new DateTime(2026, 7, 19, 8, 0, 0),
                Name = "Test Product",
                Price = 199.95m,
                Category = "Testing"
            };

            var dto = Mapper.Map<IProductEntity, ProductFromInterfaceDto>(source)!;

            // 继承接口 IEntityBase 的属性
            Assert.Equal(2001, dto.Id);
            Assert.Equal(new DateTime(2026, 7, 19, 8, 0, 0), dto.CreatedAt);

            // 直接声明在 IProductEntity 上的属性
            Assert.Equal("Test Product", dto.Name);
            Assert.Equal(199.95m, dto.Price);
            Assert.Equal("Testing", dto.Category);
        }

        /// <summary>
        /// 使用默认值的实体，验证所有字段均被映射（非默认值残留）
        /// </summary>
        [Fact]
        public void InterfaceSource_DefaultEntity_AllFieldsMapped()
        {
            IProductEntity source = new ProductEntity();

            var dto = Mapper.Map<IProductEntity, ProductFromInterfaceDto>(source)!;

            Assert.Equal(1001, dto.Id);
            Assert.Equal(new DateTime(2025, 3, 15, 10, 30, 0), dto.CreatedAt);
            Assert.Equal("NCMapper Pro", dto.Name);
            Assert.Equal(99.99m, dto.Price);
            Assert.Equal("Library", dto.Category);
        }

        // ================================================================
        // 2. 简单接口（无继承）
        // ================================================================

        /// <summary>
        /// 简单接口源（无继承链），验证最基本的接口映射
        /// </summary>
        [Fact]
        public void SimpleInterfaceSource_NoInheritance_MappedCorrectly()
        {
            ISimpleService source = new SimpleService
            {
                Name = "MessageQueue",
                Version = 5,
                IsActive = false
            };

            var dto = Mapper.Map<ISimpleService, SimpleServiceDto>(source)!;

            Assert.Equal("MessageQueue", dto.Name);
            Assert.Equal(5, dto.Version);
            Assert.False(dto.IsActive);
        }

        /// <summary>
        /// 简单接口源默认值验证
        /// </summary>
        [Fact]
        public void SimpleInterfaceSource_DefaultValues_MappedCorrectly()
        {
            ISimpleService source = new SimpleService();

            var dto = Mapper.Map<ISimpleService, SimpleServiceDto>(source)!;

            Assert.Equal("CacheService", dto.Name);
            Assert.Equal(3, dto.Version);
            Assert.True(dto.IsActive);
        }

        // ================================================================
        // 3. 接口源 + 嵌套对象属性
        // ================================================================

        /// <summary>
        /// 接口源的属性为嵌套 class 类型，验证嵌套映射正确触发
        /// </summary>
        [Fact]
        public void InterfaceSource_NestedObject_MappedCorrectly()
        {
            IOrderSource source = new OrderSource
            {
                Id = 8001,
                Address = new AddressInfo { City = "Shanghai", Street = "Nanjing Rd" }
            };

            var dto = Mapper.Map<IOrderSource, OrderFromInterfaceDto>(source)!;

            Assert.Equal(8001, dto.Id);
            Assert.NotNull(dto.Address);
            Assert.Equal("Shanghai", dto.Address!.City);
            Assert.Equal("Nanjing Rd", dto.Address.Street);
        }

        /// <summary>
        /// 接口源嵌套对象为 null 时，目标也应为 null
        /// </summary>
        [Fact]
        public void InterfaceSource_NestedObject_Null_HandledCorrectly()
        {
            IOrderSource source = new OrderSource { Id = 8002, Address = null! };

            var dto = Mapper.Map<IOrderSource, OrderFromInterfaceDto>(source)!;

            Assert.Equal(8002, dto.Id);
            Assert.Null(dto.Address);
        }

        // ================================================================
        // 4. 接口源 + 集合属性
        // ================================================================

        /// <summary>
        /// 接口源含 List 和数组属性，验证集合映射正确
        /// </summary>
        [Fact]
        public void InterfaceSource_Collections_MappedCorrectly()
        {
            ICollectionSource source = new CollectionSourceImpl
            {
                Id = 9001,
                Tags = new List<string> { "alpha", "beta", "gamma" },
                Scores = new[] { 100, 88, 76 }
            };

            var dto = Mapper.Map<ICollectionSource, CollectionFromInterfaceDto>(source)!;

            Assert.Equal(9001, dto.Id);
            Assert.NotNull(dto.Tags);
            Assert.Equal(3, dto.Tags!.Count);
            Assert.Equal("alpha", dto.Tags[0]);
            Assert.Equal("gamma", dto.Tags[2]);
            Assert.NotNull(dto.Scores);
            Assert.Equal(new[] { 100, 88, 76 }, dto.Scores);
        }

        /// <summary>
        /// 接口源集合为空列表时，目标也应为空列表（非 null）
        /// </summary>
        [Fact]
        public void InterfaceSource_EmptyCollections_MappedCorrectly()
        {
            ICollectionSource source = new CollectionSourceImpl
            {
                Id = 9002,
                Tags = new List<string>(),
                Scores = Array.Empty<int>()
            };

            var dto = Mapper.Map<ICollectionSource, CollectionFromInterfaceDto>(source)!;

            Assert.Equal(9002, dto.Id);
            Assert.NotNull(dto.Tags);
            Assert.Empty(dto.Tags!);
            Assert.NotNull(dto.Scores);
            Assert.Empty(dto.Scores!);
        }

        // ================================================================
        // 5. 源生成器路径（RegisterGenerated 模拟）
        // ================================================================

        /// <summary>
        /// 模拟源生成器路径：注册 Generated 委托后，接口源映射走 Generated 路径
        /// 验证 Generated 路径也能正确处理接口源
        /// </summary>
        [Fact]
        public void InterfaceSource_GeneratedPath_MappedCorrectly()
        {
            // 模拟源生成器生成的映射委托
            Mapper.RegisterGenerated<ISimpleService, SimpleServiceDto>((src, action) =>
            {
                var dto = new SimpleServiceDto
                {
                    Name = src.Name,
                    Version = src.Version,
                    IsActive = src.IsActive
                };
                action?.Invoke(src, dto);
                return dto;
            });

            try
            {
                ISimpleService source = new SimpleService
                {
                    Name = "GenService",
                    Version = 10,
                    IsActive = true
                };

                var dto = Mapper.Map<ISimpleService, SimpleServiceDto>(source)!;

                Assert.Equal("GenService", dto.Name);
                Assert.Equal(10, dto.Version);
                Assert.True(dto.IsActive);
            }
            finally
            {
                // 清理注册，避免影响其他测试
                Mapper.RegisterGenerated<ISimpleService, SimpleServiceDto>(null!);
            }
        }
    }
}
