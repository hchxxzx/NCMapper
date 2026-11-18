using Xunit;
using NCMapper.Tests.Models;
using Microsoft.Extensions.DependencyInjection;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// DI 集成测试 — 验证 IMapper 接口与 NCMapperMapper 实现
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class DependencyInjectionTests
    {
        /// <summary>
        /// IMapper.Map 跨类型映射正常工作
        /// </summary>
        [Fact]
        public void IMapper_Map_CrossType_Works()
        {
            IMapper mapper = new NCMapperMapper();

            var entity = new FullTypeEntity { IntProp = 42, StringProp = "DI Test" };
            var dto = mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal(42, dto.IntProp);
            Assert.Equal("DI Test", dto.StringProp);
        }

        /// <summary>
        /// IMapper.Map 同类型拷贝 — 新对象与原对象非同一引用
        /// </summary>
        [Fact]
        public void IMapper_Map_SameType_Works()
        {
            IMapper mapper = new NCMapperMapper();

            var entity = new FullTypeEntity { IntProp = 99, StringProp = "Copy" };
            var copy = mapper.Map(entity)!;

            Assert.Equal(99, copy.IntProp);
            Assert.Equal("Copy", copy.StringProp);
            Assert.NotSame(entity, copy);
        }

        /// <summary>
        /// IMapper.Map 带 Action 回调 — 映射后可通过回调修改目标对象
        /// </summary>
        [Fact]
        public void IMapper_Map_WithAction_Works()
        {
            IMapper mapper = new NCMapperMapper();

            var entity = new FullTypeEntity { IntProp = 1, StringProp = "Action" };
            var dto = mapper.Map<FullTypeEntity, SameTypeDto>(entity, (s, d) => d.IntProp += 100)!;

            Assert.Equal(101, dto.IntProp);
            Assert.Equal("Action", dto.StringProp);
        }

        /// <summary>
        /// IMapper.Map 更新已有目标对象
        /// </summary>
        [Fact]
        public void IMapper_Map_Update_Target_Works()
        {
            IMapper mapper = new NCMapperMapper();

            var entity = new FullTypeEntity { IntProp = 55, StringProp = "Update" };
            var dto = new SameTypeDto();
            mapper.Map(entity, dto);

            Assert.Equal(55, dto.IntProp);
            Assert.Equal("Update", dto.StringProp);
        }

        /// <summary>
        /// IMapper.MapList 跨类型列表映射 — 数量和元素值正确
        /// </summary>
        [Fact]
        public void IMapper_MapList_CrossType_Works()
        {
            IMapper mapper = new NCMapperMapper();

            var entities = new List<FullTypeEntity>
            {
                new() { IntProp = 1 },
                new() { IntProp = 2 },
                new() { IntProp = 3 }
            };

            var dtos = mapper.MapList<FullTypeEntity, SameTypeDto>(entities)!;

            Assert.Equal(3, dtos.Count);
            Assert.Equal(1, dtos[0].IntProp);
            Assert.Equal(3, dtos[2].IntProp);
        }

        /// <summary>
        /// IMapper.MapList 同类型列表拷贝 — 元素非同一引用
        /// </summary>
        [Fact]
        public void IMapper_MapList_SameType_Works()
        {
            IMapper mapper = new NCMapperMapper();

            var entities = new List<FullTypeEntity>
            {
                new() { IntProp = 10 },
                new() { IntProp = 20 }
            };

            var copies = mapper.MapList(entities)!;

            Assert.Equal(2, copies.Count);
            Assert.Equal(10, copies[0].IntProp);
            Assert.NotSame(entities[0], copies[0]);
        }

        /// <summary>
        /// IMapper 与静态 Mapper 结果完全一致
        /// </summary>
        [Fact]
        public void IMapper_Results_Match_Static_Mapper()
        {
            IMapper mapper = new NCMapperMapper();
            var entity = new FullTypeEntity { IntProp = 777, StringProp = "Match" };

            var viaInterface = mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;
            var viaStatic = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal(viaStatic.IntProp, viaInterface.IntProp);
            Assert.Equal(viaStatic.StringProp, viaInterface.StringProp);
        }

        /// <summary>
        /// AddNCMapper 正确注册 IMapper 为 Singleton
        /// </summary>
        [Fact]
        public void AddNCMapper_Registers_IMapper_As_Singleton()
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddNCMapper();

            var provider = services.BuildServiceProvider();

            var mapper1 = provider.GetService<IMapper>();
            var mapper2 = provider.GetService<IMapper>();

            Assert.NotNull(mapper1);
            Assert.Same(mapper1, mapper2); // Singleton
        }

        /// <summary>
        /// DI 容器解析的 IMapper 可正常工作
        /// </summary>
        [Fact]
        public void AddNCMapper_Resolved_Mapper_Works()
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddNCMapper();
            var provider = services.BuildServiceProvider();

            var mapper = provider.GetRequiredService<IMapper>();
            var entity = new FullTypeEntity { IntProp = 123, StringProp = "DI" };

            var dto = mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal(123, dto.IntProp);
            Assert.Equal("DI", dto.StringProp);
        }

        /// <summary>
        /// IMapper.Compose 多源合成正确工作
        /// </summary>
        [Fact]
        public void IMapper_Compose_Works()
        {
            IMapper mapper = new NCMapperMapper();

            var order = new MultiSourceOrderEntity { OrderId = 100, OrderAmount = 50m };
            var customer = new MultiSourceCustomerEntity { CustomerName = "DI", CustomerLevel = 2 };

            var dto = mapper.Compose<MultiSourceComposeDto>(order, customer)!;

            Assert.Equal(100, dto.OrderId);
            Assert.Equal(50m, dto.OrderAmount);
            Assert.Equal("DI", dto.CustomerName);
            Assert.Equal(2, dto.CustomerLevel);
        }

        /// <summary>
        /// IMapper.DeepCopy 深拷贝正确工作
        /// </summary>
        [Fact]
        public void IMapper_DeepCopy_Works()
        {
            IMapper mapper = new NCMapperMapper();

            var entity = new FullTypeEntity { IntProp = 42, StringProp = "Copy" };
            var copy = mapper.DeepCopy(entity)!;

            Assert.Equal(42, copy.IntProp);
            Assert.Equal("Copy", copy.StringProp);
            Assert.NotSame(entity, copy);
        }

    }
}
